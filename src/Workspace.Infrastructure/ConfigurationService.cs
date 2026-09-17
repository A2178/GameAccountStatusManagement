using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Workspace.Application;
using Workspace.Domain;
using Workspace.Infrastructure.Persistence;

namespace Workspace.Infrastructure;

public sealed class ConfigurationService(WorkspaceDbContext db, IWorkspaceNotifier notifier) : IConfigurationService
{
    public async Task<ConfigurationDto> GetAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        return new(await db.Settings.AsNoTracking().SingleAsync(ct),
            await db.Collections.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct),
            (await db.Views.AsNoTracking().OrderBy(x => x.Position).ThenBy(x => x.Id).ToListAsync(ct)).Select(View).ToList(),
            await db.Stages.AsNoTracking().OrderBy(x => x.Position).ToListAsync(ct),
            await db.Regions.AsNoTracking().Select(x => new NamedTargetDto(x.Id, x.DisplayName, x.Version)).ToListAsync(ct));
    }

    public async Task<CollectionSnapshotDto> GetCollectionAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var collection = await Collection(id, ct);
        List<RecordDto> records = collection.Kind switch
        {
            CollectionKind.Cards => await db.Cards.AsNoTracking().Select(x => new RecordDto(x.Id, x.DisplayName, x.AccountId, x.StageId, x.MetadataVersion)).ToListAsync(ct),
            CollectionKind.Accounts => await db.Accounts.AsNoTracking().Select(x => new RecordDto(x.Id, x.DisplayName, null, null, x.MetadataVersion)).ToListAsync(ct),
            _ => await db.DataRecords.AsNoTracking().Where(x => x.CollectionId == id).Select(x => new RecordDto(x.Id, x.Name, null, null, x.Version)).ToListAsync(ct)
        };
        var fields = await db.Fields.AsNoTracking().Where(x => x.CollectionId == id && !x.Deleted).OrderBy(x => x.Position).ThenBy(x => x.Id).ToListAsync(ct);
        var fieldIds = fields.Select(x => x.Id).ToArray();
        var values = (await db.RecordValues.AsNoTracking().Where(x => fieldIds.Contains(x.FieldId)).ToListAsync(ct))
            .Select(x => new ValueDto(x.FieldId, x.RecordId, Json(x.ValueJson), x.Attached, x.Version)).ToList();
        values.AddRange((await db.SharedValues.AsNoTracking().Where(x => fieldIds.Contains(x.FieldId)).ToListAsync(ct))
            .Select(x => new ValueDto(x.FieldId, null, Json(x.ValueJson), true, x.Version)));
        // Bound cells are projections only. Their write path remains the existing coordination commands.
        foreach (var field in fields.Where(x => x.Binding == FieldBinding.Resource))
        {
            var reservations = await db.Reservations.AsNoTracking().Where(x => x.State != ReservationState.Released).ToListAsync(ct);
            values.AddRange(records.Select(record => new ValueDto(field.Id, record.Id,
                JsonSerializer.SerializeToElement(reservations.SingleOrDefault(x => x.CardId == record.Id)?.RegionId), true, 0)));
        }
        return new(collection, records, fields.Select(Field).ToList(), values);
    }

    public Task SaveFieldAsync(CurrentSessionDto actor, Guid id, SaveFieldCommand command, CancellationToken ct) => Mutate(actor, id, async () =>
    {
        await LockField(id, ct);
        var field = await db.Fields.SingleOrDefaultAsync(x => x.Id == id, ct);
        Check(field?.Version ?? 0, command.ExpectedVersion);
        if (field?.Deleted == true) throw new DomainRuleException("欄位已刪除，請重新建立定義。");
        var collection = await Collection(command.CollectionId, ct);
        if (!Enum.IsDefined(command.Kind) || !Enum.IsDefined(command.Scope)) throw new DomainRuleException("不支援此欄位設定。");
        if (field != null && field.CollectionId != collection.Id) throw new DomainRuleException("不能移動既有欄位至其他資料集。");
        var options = command.Options ?? [];
        if (options.Length > 100 || options.Any(x => x.Id == Guid.Empty) || options.Select(x => x.Id).Distinct().Count() != options.Length)
            throw new DomainRuleException("選項 ID 必須唯一，最多 100 個選項。");
        options = options.Select(x => x with { Name = FieldValuePolicy.Name(x.Name), Color = Color(x.Color) }).ToArray();
        if (command.Kind == FieldKind.Relation && command.RelationCollectionId != BuiltInCollections.Accounts && command.RelationCollectionId != BuiltInCollections.Cards)
            throw new DomainRuleException("關聯欄位需指定帳號或卡片資料集。");
        if (field?.Binding == FieldBinding.Resource && (command.Kind != field.Kind || command.Scope != field.Scope || command.Required || command.RelationCollectionId != null))
            throw new DomainRuleException("核心綁定僅能調整名稱、顏色、順序及顯示。");
        var proposed = new FieldDefinition { Id = id, CollectionId = collection.Id, Name = FieldValuePolicy.Name(command.Name), Kind = command.Kind,
            Scope = command.Scope, OptionsJson = JsonSerializer.Serialize(options), RelationCollectionId = command.Kind == FieldKind.Relation ? command.RelationCollectionId : null,
            Color = Color(command.Color), Position = command.Position, Required = command.Required, Hidden = command.Hidden,
            Binding = field?.Binding ?? FieldBinding.None, Version = (field?.Version ?? 0) + 1 };
        if (field != null)
        {
            var values = await db.RecordValues.Where(x => x.FieldId == id).ToListAsync(ct);
            var shared = await db.SharedValues.SingleOrDefaultAsync(x => x.FieldId == id, ct);
            if (field.Scope != proposed.Scope && (values.Count > 0 || shared != null)) throw new DomainRuleException("已有值的欄位不能直接切換值範圍；請另建欄位，避免遺失資料。");
            foreach (var value in values.Where(x => x.Attached).Select(x => x.ValueJson).Concat(shared == null ? [] : new[] { shared.ValueJson }))
                await ValidateValue(proposed, Json(value), ct);
            db.Entry(field).CurrentValues.SetValues(proposed);
        }
        else db.Fields.Add(proposed);
        return $"調整「{collection.Name}」的欄位「{proposed.Name}」";
    }, ct);

    public Task DeleteFieldAsync(CurrentSessionDto actor, Guid id, long expectedVersion, CancellationToken ct) => Mutate(actor, id, async () =>
    {
        await LockField(id, ct);
        var field = await ActiveField(id, ct); Check(field.Version, expectedVersion);
        // Resource rules reference reservation/resource IDs, never this presentation definition.
        field.Deleted = true; field.Version++;
        return $"移除欄位外觀「{field.Name}」，保留原有業務狀態";
    }, ct);

    public async Task<ValueDto> SaveValueAsync(CurrentSessionDto actor, Guid fieldId, Guid? recordId, SaveValueCommand command, CancellationToken ct)
    {
        ValueDto? result = null;
        await Mutate(actor, recordId ?? fieldId, async () =>
        {
            // All value writes and definition changes lock the same field first. This also serializes first insert.
            await LockField(fieldId, ct);
            var field = await ActiveField(fieldId, ct);
            if (field.Version != command.ExpectedDefinitionVersion) throw new FieldConflictException("欄位定義已更新，草稿已保留，請確認目前型別與選項。");
            if (field.Binding != FieldBinding.None) throw new DomainRuleException("此欄位綁定正式狀態，請使用預約、入場、取消或回村操作。");
            if (field.Scope == FieldScope.Shared != (recordId == null)) throw new DomainRuleException("值的保存範圍不符合欄位定義。");
            if (!command.Attached && field.Scope != FieldScope.Individual) throw new DomainRuleException("只有個別標籤可以從單筆資料移除。");
            string targetName = field.Name;
            if (recordId.HasValue) targetName = await RecordName(field.CollectionId, recordId.Value, ct);
            if (command.Attached) await ValidateValue(field, command.Value, ct);
            var json = command.Attached && command.Value.ValueKind != JsonValueKind.Undefined ? command.Value.GetRawText() : "null";
            if (field.Scope == FieldScope.Shared)
            {
                var value = await db.SharedValues.SingleOrDefaultAsync(x => x.FieldId == fieldId, ct);
                var current = new ValueDto(fieldId, null, Json(value?.ValueJson ?? "null"), true, value?.Version ?? 0);
                CheckValue(current, command.ExpectedVersion);
                if (value == null) { value = new() { FieldId = fieldId, Version = 0 }; db.SharedValues.Add(value); }
                value.ValueJson = json; value.Version++;
                result = new(fieldId, null, Json(json), true, value.Version);
            }
            else
            {
                var value = await db.RecordValues.SingleOrDefaultAsync(x => x.FieldId == fieldId && x.RecordId == recordId, ct);
                var current = new ValueDto(fieldId, recordId, Json(value?.ValueJson ?? "null"), value?.Attached ?? field.Scope != FieldScope.Individual, value?.Version ?? 0);
                CheckValue(current, command.ExpectedVersion);
                if (value == null) { value = new() { FieldId = fieldId, RecordId = recordId!.Value, Version = 0 }; db.RecordValues.Add(value); }
                value.ValueJson = json; value.Attached = command.Attached; value.Version++;
                result = new(fieldId, recordId, Json(json), value.Attached, value.Version);
            }
            // Values (including credentials in renamed/custom text fields) never enter the audit log.
            return $"{(command.Attached ? "更新" : "移除")}「{targetName}」的「{field.Name}」";
        }, ct);
        return result!;
    }

    public Task SaveViewAsync(CurrentSessionDto actor, Guid id, SaveViewCommand command, CancellationToken ct) => Mutate(actor, id, async () =>
    {
        await LockSettings(ct);
        var view = await db.Views.SingleOrDefaultAsync(x => x.Id == id, ct); Check(view?.Version ?? 0, command.ExpectedVersion);
        var collection = await Collection(command.CollectionId, ct);
        if (view != null && view.CollectionId != command.CollectionId) throw new DomainRuleException("既有視圖不能移至其他資料集。");
        if (command.Display is not ("Table" or "Board" or "Dashboard" or "Panel")) throw new DomainRuleException("不支援此顯示方式。");
        if (command.StageId.HasValue && (collection.Kind != CollectionKind.Cards || !await db.Stages.AnyAsync(x => x.Id == command.StageId, ct))) throw new DomainRuleException("無效的階段篩選。");
        var fieldIds = (command.HiddenFields ?? []).Concat(new[] { command.SortFieldId, command.FilterFieldId }.Where(x => x.HasValue).Select(x => x!.Value)).Distinct().ToArray();
        if (await db.Fields.CountAsync(x => fieldIds.Contains(x.Id) && x.CollectionId == collection.Id && !x.Deleted, ct) != fieldIds.Length) throw new DomainRuleException("視圖引用了不存在的欄位。");
        if ((command.FilterText ?? "").Length > 4000) throw new DomainRuleException("篩選文字過長。");
        if (view == null) { view = new() { Id = id, CollectionId = collection.Id, Version = 0 }; db.Views.Add(view); }
        view.Name = FieldValuePolicy.Name(command.Name); view.Display = command.Display; view.Position = command.Position; view.StageId = command.StageId;
        view.HiddenFieldsJson = JsonSerializer.Serialize(command.HiddenFields ?? []); view.SortFieldId = command.SortFieldId; view.Descending = command.Descending;
        view.FilterFieldId = command.FilterFieldId; view.FilterText = command.FilterText ?? ""; view.Version++;
        return $"調整分頁「{view.Name}」";
    }, ct);

    public Task CreateCollectionAsync(CurrentSessionDto actor, CreateCollectionCommand command, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        return Mutate(actor, id, async () =>
        {
            var name = FieldValuePolicy.Name(command.Name);
            db.Collections.Add(new() { Id = id, Name = name, Kind = CollectionKind.Free });
            db.Views.Add(new() { Id = Guid.NewGuid(), CollectionId = id, Name = name, Position = 100 });
            await Task.CompletedTask; return $"新增表格「{name}」";
        }, ct);
    }

    public Task CreateRecordAsync(CurrentSessionDto actor, Guid collectionId, CreateCollectionCommand command, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        return Mutate(actor, id, async () =>
        {
            var collection = await Collection(collectionId, ct);
            if (collection.Kind != CollectionKind.Free) throw new DomainRuleException("帳號與卡片請使用既有新增入口，確保帳號關聯一致。");
            var name = FieldValuePolicy.Name(command.Name);
            db.DataRecords.Add(new() { Id = id, CollectionId = collectionId, Name = name });
            return $"在「{collection.Name}」新增「{name}」";
        }, ct);
    }

    public Task RenameAsync(CurrentSessionDto actor, string target, Guid id, RenameCommand command, CancellationToken ct) => Mutate(actor, id, async () =>
    {
        var name = FieldValuePolicy.Name(command.Name);
        string old;
        // Business rows are always locked before workspace/settings rows, as in M1.
        switch (target)
        {
            case "accounts":
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM preview_accounts WHERE \"Id\" = {id} FOR UPDATE", ct);
                var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
                Check(account.MetadataVersion, command.ExpectedVersion); old = account.DisplayName; account.Rename(name); break;
            case "cards":
                var accountId = await db.Cards.Where(x => x.Id == id).Select(x => (Guid?)x.AccountId).SingleOrDefaultAsync(ct) ?? throw Missing();
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM preview_accounts WHERE \"Id\" = {accountId} FOR UPDATE", ct);
                var card = await db.Cards.SingleAsync(x => x.Id == id, ct); Check(card.MetadataVersion, command.ExpectedVersion); old = card.DisplayName; card.Rename(name); break;
            case "regions":
                await LockSettings(ct);
                var region = await db.Regions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing(); Check(region.Version, command.ExpectedVersion); old = region.DisplayName; region.Rename(name); break;
            case "stages":
                await LockSettings(ct);
                var stage = await db.Stages.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing(); Check(stage.Version, command.ExpectedVersion); old = stage.Name; stage.Name = name; stage.Version++; break;
            case "collections":
                await LockSettings(ct);
                var collection = await Collection(id, ct); Check(collection.Version, command.ExpectedVersion); old = collection.Name; collection.Name = name; collection.Version++; break;
            case "records":
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM data_records WHERE \"Id\" = {id} FOR UPDATE", ct);
                var record = await db.DataRecords.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing(); Check(record.Version, command.ExpectedVersion); old = record.Name; record.Name = name; record.Version++; break;
            default: throw Missing();
        }
        return $"將「{old}」改名為「{name}」";
    }, ct);

    public Task SaveSettingsAsync(CurrentSessionDto actor, SaveSettingsCommand command, CancellationToken ct) => Mutate(actor, Guid.Empty, async () =>
    {
        await LockSettings(ct); var settings = await db.Settings.SingleAsync(ct); Check(settings.Version, command.ExpectedVersion);
        settings.Name = FieldValuePolicy.Name(command.Name); settings.CardLabel = FieldValuePolicy.Name(command.CardLabel); settings.SettingsLabel = FieldValuePolicy.Name(command.SettingsLabel); settings.Version++;
        return "更新工作區名稱與顯示用語";
    }, ct);

    public Task MoveStageAsync(CurrentSessionDto actor, Guid cardId, MoveStageCommand command, CancellationToken ct) => Mutate(actor, cardId, async () =>
    {
        var accountId = await db.Cards.Where(x => x.Id == cardId).Select(x => (Guid?)x.AccountId).SingleOrDefaultAsync(ct) ?? throw Missing();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM preview_accounts WHERE \"Id\" = {accountId} FOR UPDATE", ct);
        var card = await db.Cards.SingleAsync(x => x.Id == cardId, ct); Check(card.MetadataVersion, command.ExpectedVersion);
        var stage = await db.Stages.SingleOrDefaultAsync(x => x.Id == command.StageId, ct) ?? throw Missing();
        card.MoveStage(stage.Id); return $"將「{card.DisplayName}」移至「{stage.Name}」（所在地不變）";
    }, ct);

    private async Task Mutate(CurrentSessionDto actor, Guid targetId, Func<Task<string>> action, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var description = await action();
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM workspace_state WHERE \"Id\" = 1 FOR UPDATE", ct);
        var state = await db.WorkspaceStates.SingleAsync(ct); state.Advance();
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), actor.ParticipantId, actor.Nickname, $"{actor.Nickname} {description}。", DateTimeOffset.UtcNow, targetId));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await notifier.SnapshotChangedAsync(state.Version, ct);
    }

    private Task LockField(Guid id, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 0))", ct);
    private Task LockSettings(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("SELECT 1 FROM workspace_settings WHERE \"Id\" = 1 FOR UPDATE", ct);
    private async Task<FieldDefinition> ActiveField(Guid id, CancellationToken ct) => await db.Fields.SingleOrDefaultAsync(x => x.Id == id && !x.Deleted, ct) ?? throw new FieldConflictException("欄位已刪除，草稿已保留，可複製至其他欄位。");
    private async Task<CollectionDefinition> Collection(Guid id, CancellationToken ct) => await db.Collections.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
    private async Task<string> RecordName(Guid collectionId, Guid id, CancellationToken ct)
    {
        var collection = await Collection(collectionId, ct);
        return collection.Kind switch
        {
            CollectionKind.Accounts => await db.Accounts.Where(x => x.Id == id).Select(x => x.DisplayName).SingleOrDefaultAsync(ct) ?? throw Missing(),
            CollectionKind.Cards => await db.Cards.Where(x => x.Id == id).Select(x => x.DisplayName).SingleOrDefaultAsync(ct) ?? throw Missing(),
            _ => await db.DataRecords.Where(x => x.Id == id && x.CollectionId == collectionId).Select(x => x.Name).SingleOrDefaultAsync(ct) ?? throw Missing()
        };
    }
    private async Task ValidateValue(FieldDefinition field, JsonElement value, CancellationToken ct)
    {
        FieldValuePolicy.Validate(field, value);
        if (field.Kind == FieldKind.Relation && value.ValueKind == JsonValueKind.String) await RecordName(field.RelationCollectionId!.Value, value.GetGuid(), ct);
    }
    private static DomainRuleException Missing() => new("找不到指定資料。");
    private static void Check(long actual, long expected) { if (actual != expected) throw new VersionConflictException(); }
    private static void CheckValue(ValueDto current, long expected) { if (current.Version != expected) throw new FieldConflictException("資料已更新。草稿已保留，請比較最新值後再決定是否保存。", current); }
    private static string Color(string? value) => value != null && System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$") ? value : throw new DomainRuleException("顏色需為六位十六進位色碼。");
    private static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);
    private static FieldDto Field(FieldDefinition x) => new(x.Id, x.CollectionId, x.Name, x.Kind, x.Scope, x.Binding, JsonSerializer.Deserialize<FieldOption[]>(x.OptionsJson)!, x.RelationCollectionId, x.Color, x.Position, x.Required, x.Hidden, x.Version);
    private static ViewDto View(ViewDefinition x) => new(x.Id, x.CollectionId, x.Name, x.Display, x.Position, x.StageId, JsonSerializer.Deserialize<Guid[]>(x.HiddenFieldsJson)!, x.SortFieldId, x.Descending, x.FilterFieldId, x.FilterText, x.Version);
}
