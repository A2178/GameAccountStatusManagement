using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Workspace.Application;
using Workspace.Domain;
using Workspace.Infrastructure.Persistence;

namespace Workspace.Infrastructure;

public sealed class ProgressionService(WorkspaceDbContext db, IWorkspaceNotifier notifier, TimeProvider clock) : IProgressionService
{
    public async Task<ProgressionSnapshotDto> GetAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var now = clock.GetUtcNow();
        var version = await db.WorkspaceStates.AsNoTracking().Select(x => x.Version).SingleAsync(ct);
        var settings = await db.ProgressionSettings.AsNoTracking().SingleAsync(ct);
        var profiles = await db.ProgressionProfiles.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        var cards = await db.Cards.AsNoTracking().OrderBy(x => x.DisplayName).ToListAsync(ct);
        var states = await db.Progressions.AsNoTracking().ToDictionaryAsync(x => x.CardId, ct);
        var cycles = await db.QualificationCycles.AsNoTracking().OrderByDescending(x => x.QualifiedAt).ToListAsync(ct);
        var activities = await db.Activities.AsNoTracking().OrderByDescending(x => x.RecordedAt).ToListAsync(ct);
        var entries = await db.CreditEntries.AsNoTracking().OrderByDescending(x => x.RecordedAt).ToListAsync(ct);
        var people = await db.Participants.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        return new(version, now, settings, profiles, await db.Stages.AsNoTracking().OrderBy(x => x.Position).ToListAsync(ct), cards.Select(card => {
            var state = states.GetValueOrDefault(card.Id) ?? new() { CardId = card.Id };
            var cycle = cycles.SingleOrDefault(x => x.Id == state.CurrentCycleId);
            var status = card.ArchivedAt != null ? "Archived" : state.PermanentlyDisqualified ? "Disqualified" : cycle?.InvalidatedAt != null ? "NeedsRequalification"
                : cycle?.ActivatedAt != null ? "Active" : cycle != null ? (now >= cycle.EligibleFrom ? "Eligible" : "Waiting") : state.ProfileId == null ? "NotConfigured" : "Accumulating";
            return new CardProgressionDto(card.Id, card.DisplayName, card.AccountId, card.StageId, card.ReplacesCardId, card.ArchivedAt,
                state.ProfileId, state.Level, state.TaskItems, state.MeritBalance, state.CumulativeCredits, state.Version, state.CreditVersion,
                state.PermanentlyDisqualified, status,
                cycles.Where(x => x.CardId == card.Id).Select(x => new QualificationDto(x.Id, x.QualifiedAt, x.EligibleFrom, x.TimeZoneId, x.ResetHour,
                    JsonSerializer.Deserialize<ProgressionProfile>(x.ThresholdSnapshotJson)!, x.ActivatedAt, x.InvalidatedAt, x.InvalidationReason)).ToArray(),
                activities.Where(x => x.CardId == card.Id).Select(x => new ActivityDto(x.Id, x.QualificationCycleId, x.Channel, x.OccurredAt, x.RecordedAt, x.EndedAt,
                    !x.OperatorWasHidden && people.TryGetValue(x.OperatorParticipantId, out var person) && !person.IsAdmin ? person.Nickname : null, x.Version)).ToArray(),
                entries.Where(x => x.CardId == card.Id).Select(x => new CreditEntryDto(x.Id, x.Kind, x.Amount, x.TotalAfter, x.Reason, x.RecordedAt)).ToArray());
        }).ToArray());
    }

    public async Task<ProgressionResultDto> ExecuteAsync(CurrentSessionDto actor, Guid cardId, ProgressionCommand command, CancellationToken ct)
    {
        if (command.RequestId == Guid.Empty || !Enum.IsDefined(command.Operation)) throw new DomainRuleException("操作及請求識別不正確。");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { cardId, command }))));
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Request locks precede business locks and are never acquired by non-retryable commands.
        // This also serializes accidental reuse of one request ID for two different cards.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"request:" + command.RequestId}, 0))", ct);
        var receipt = await db.CommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == command.RequestId, ct);
        if (receipt != null)
        {
            if (receipt.PayloadHash != hash) throw new DomainRuleException("這個請求識別已用於不同內容，請先確認原操作結果。");
            return JsonSerializer.Deserialize<ProgressionResultDto>(receipt.ResultJson)!;
        }
        var accountId = await db.Cards.AsNoTracking().Where(x => x.Id == cardId).Select(x => (Guid?)x.AccountId).SingleOrDefaultAsync(ct) ?? throw Missing();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM preview_accounts WHERE \"Id\" = {accountId} FOR UPDATE", ct);
        var card = await db.Cards.SingleAsync(x => x.Id == cardId, ct);
        if (card.ArchivedAt != null && command.Operation is not (ProgressionOperation.CreateReplacement or ProgressionOperation.CorrectCredits)) throw new DomainRuleException("卡片已封存，不能執行此操作。");
        var state = await db.Progressions.SingleOrDefaultAsync(x => x.CardId == cardId, ct);
        if (state == null) { state = new() { CardId = cardId }; db.Progressions.Add(state); }
        if (command.Operation is not (ProgressionOperation.RecordIncome or ProgressionOperation.RecordConversion or ProgressionOperation.CorrectCredits)) Check(state.Version, command.ExpectedVersion);
        var now = clock.GetUtcNow();
        var cycle = state.CurrentCycleId.HasValue ? await db.QualificationCycles.SingleAsync(x => x.Id == state.CurrentCycleId, ct) : null;
        var active = await db.Activities.SingleOrDefaultAsync(x => x.CardId == cardId && x.EndedAt == null, ct);
        var settings = await db.ProgressionSettings.AsNoTracking().SingleAsync(ct);
        Guid? activityId = active?.Id, replacementId = null;
        string description;
        switch (command.Operation)
        {
            case ProgressionOperation.ReportProgress:
                CardProgression.Amount(command.Level); CardProgression.Amount(command.TaskItems); CardProgression.Amount(command.MeritBalance);
                var profile = await db.ProgressionProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.ProfileId, ct) ?? throw new DomainRuleException("請先設定並選擇職業門檻。");
                if (cycle is { InvalidatedAt: null } && state.ProfileId != profile.Id) throw new DomainRuleException("本輪已達標；變更職業前請先明確撤銷資格或回報重刷。");
                state.ProfileId = profile.Id; state.Level = command.Level; state.TaskItems = command.TaskItems; state.MeritBalance = command.MeritBalance;
                if (!state.PermanentlyDisqualified && (cycle == null || cycle.InvalidatedAt != null) && state.Meets(profile))
                {
                    cycle = new() { Id = Guid.NewGuid(), CardId = cardId, QualifiedAt = now, EligibleFrom = GameDayPolicy.NextBoundary(now, settings.TimeZoneId, settings.ResetHour),
                        TimeZoneId = settings.TimeZoneId, ResetHour = settings.ResetHour, ThresholdSnapshotJson = JsonSerializer.Serialize(profile) };
                    db.QualificationCycles.Add(cycle); state.CurrentCycleId = cycle.Id;
                }
                description = $"確認養成進度（等級 {state.Level}、道具 {state.TaskItems}、功勳 {state.MeritBalance}）"; break;
            case ProgressionOperation.StartActivity:
                if (state.PermanentlyDisqualified) throw new DomainRuleException("此卡片已永久失格，不能進入活動。");
                if (cycle == null) throw new DomainRuleException("尚未達標，不能進入活動。");
                cycle.EnsureEligible(now);
                if (active != null) throw new DomainRuleException("已有未結束活動，請先回報結束。");
                var occurred = command.OccurredAt?.ToUniversalTime() ?? now;
                if (occurred < cycle.EligibleFrom || occurred > now) throw new DomainRuleException("入場時間需在本輪可投入時間之後，且不得晚於伺服器目前時間。");
                active = new() { Id = Guid.NewGuid(), CardId = cardId, QualificationCycleId = cycle.Id, OperatorParticipantId = actor.ParticipantId,
                    OperatorWasHidden = actor.CanReadAudit, Channel = FieldValuePolicy.Name(command.Channel), OccurredAt = occurred, RecordedAt = now };
                db.Activities.Add(active); cycle.ActivatedAt ??= occurred; activityId = active.Id;
                card.SetUsage(CardUsageStatus.InUse, actor.ParticipantId);
                description = $"回報進入分流「{active.Channel}」，時間 {occurred:O}"; break;
            case ProgressionOperation.CorrectActivity:
            case ProgressionOperation.EndActivity:
                var activity = await db.Activities.SingleOrDefaultAsync(x => x.Id == command.ActivityId && x.CardId == cardId, ct) ?? throw Missing();
                Check(activity.Version, command.ExpectedActivityVersion);
                if (command.Operation == ProgressionOperation.EndActivity)
                {
                    if (activity.EndedAt != null) throw new DomainRuleException("這場活動已結束。");
                    activity.EndedAt = now; description = $"結束分流「{activity.Channel}」活動（野外占用不變）";
                }
                else
                {
                    var reason = Reason(command.Reason);
                    var originalCycle = await db.QualificationCycles.AsNoTracking().SingleAsync(x => x.Id == activity.QualificationCycleId, ct);
                    var corrected = command.OccurredAt?.ToUniversalTime() ?? throw new DomainRuleException("請提供實際進入時間。");
                    if (corrected < originalCycle.EligibleFrom || corrected > (activity.EndedAt ?? now)) throw new DomainRuleException("更正時間不在可投入至活動結束的範圍內。");
                    var oldChannel = activity.Channel; var oldTime = activity.OccurredAt;
                    activity.Channel = FieldValuePolicy.Name(command.Channel); activity.OccurredAt = corrected;
                    description = $"更正活動「{oldChannel}」{oldTime:O} →「{activity.Channel}」{corrected:O}；原因：{reason}";
                }
                activity.Version++; activityId = activity.Id; break;
            case ProgressionOperation.Requalify:
            case ProgressionOperation.InvalidateQualification:
            case ProgressionOperation.Disqualify:
                var invalidReason = Reason(command.Reason);
                if (command.Operation != ProgressionOperation.Disqualify && active != null) throw new DomainRuleException("請先明確結束目前活動，再撤銷資格或回報重刷。");
                if (cycle is { InvalidatedAt: null }) { cycle.InvalidatedAt = now; cycle.InvalidationReason = invalidReason; }
                if (command.Operation == ProgressionOperation.Disqualify) state.PermanentlyDisqualified = true;
                else if (command.Operation == ProgressionOperation.Requalify)
                {
                    CardProgression.Amount(command.MeritBalance); state.MeritBalance = command.MeritBalance;
                    card.MoveStage(settings.AccumulationStageId);
                }
                description = command.Operation == ProgressionOperation.Disqualify ? $"回報永久失格；原因：{invalidReason}"
                    : command.Operation == ProgressionOperation.Requalify ? $"回報需重刷，確認剩餘功勳 {state.MeritBalance}；原因：{invalidReason}" : $"撤銷本輪資格；原因：{invalidReason}";
                break;
            case ProgressionOperation.RecordIncome:
            case ProgressionOperation.RecordConversion:
            case ProgressionOperation.CorrectCredits:
                var kind = command.Operation == ProgressionOperation.RecordIncome ? CreditKind.Income : command.Operation == ProgressionOperation.RecordConversion ? CreditKind.Conversion : CreditKind.Correction;
                var amount = command.Amount; var reasonText = "";
                if (kind == CreditKind.Correction)
                {
                    Check(state.CreditVersion, command.ExpectedCreditVersion); CardProgression.Amount(command.Amount); reasonText = Reason(command.Reason);
                    amount = command.Amount - state.CumulativeCredits; state.CumulativeCredits = command.Amount; state.CreditVersion++;
                }
                else if (kind == CreditKind.Conversion) state.Convert(amount, cycle, active != null, settings.ConversionRequiresActiveActivity);
                else state.Credit(amount);
                db.CreditEntries.Add(new() { Id = Guid.NewGuid(), CardId = cardId, RequestId = command.RequestId, Kind = kind, Amount = amount, TotalAfter = state.CumulativeCredits, Reason = reasonText, RecordedAt = now });
                description = kind == CreditKind.Correction ? $"更正累積金幣為 {state.CumulativeCredits}；原因：{reasonText}" : kind == CreditKind.Conversion ? $"記錄已完成兌換 {amount}，剩餘功勳 {state.MeritBalance}，累積金幣 {state.CumulativeCredits}" : $"記錄所得 {amount}，累積金幣 {state.CumulativeCredits}"; break;
            case ProgressionOperation.Archive:
                if (active != null || await db.Reservations.AnyAsync(x => x.CardId == cardId && x.State != ReservationState.Released, ct)) throw new DomainRuleException("請先確認回村／取消預約並結束活動，再封存卡片。");
                card.Archive(now); description = "封存卡片，保留歷史與累積所得"; break;
            case ProgressionOperation.CreateReplacement:
                if (card.ArchivedAt == null) throw new DomainRuleException("請先妥善結束狀態並封存舊卡片，再建立接替卡片。");
                var replacement = new CharacterCard(Guid.NewGuid(), card.AccountId, FieldValuePolicy.Name(command.ReplacementName)); replacement.Replace(card.Id);
                db.Cards.Add(replacement); replacementId = replacement.Id;
                description = $"建立接替卡片「{replacement.DisplayName}」，資格與累積金幣重新開始"; break;
            default: throw new DomainRuleException("不支援此操作。");
        }
        state.Version++;
        (await db.Accounts.SingleAsync(x => x.Id == accountId, ct)).AdvanceVersion();
        var workspaceVersion = await Audit(actor, cardId, $"「{card.DisplayName}」{description}", now, ct);
        var result = new ProgressionResultDto(command.RequestId, workspaceVersion, state.Version, state.CreditVersion, state.MeritBalance, state.CumulativeCredits, activityId, replacementId);
        db.CommandReceipts.Add(new() { RequestId = command.RequestId, CardId = cardId, PayloadHash = hash, ResultJson = JsonSerializer.Serialize(result) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await notifier.SnapshotChangedAsync(workspaceVersion, ct);
        return result;
    }

    public Task SaveProfileAsync(CurrentSessionDto actor, Guid id, SaveProfileCommand command, CancellationToken ct) => Configure(actor, id, async () => {
        CardProgression.Amount(command.LevelTarget, true); CardProgression.Amount(command.TaskItemTarget); CardProgression.Amount(command.MeritTarget, true);
        var profile = await db.ProgressionProfiles.SingleOrDefaultAsync(x => x.Id == id, ct); Check(profile?.Version ?? 0, command.ExpectedVersion);
        if (id == Guid.Empty) throw new DomainRuleException("設定識別不能空白。");
        if (profile == null) { profile = new() { Id = id, Version = 0 }; db.ProgressionProfiles.Add(profile); }
        profile.Name = FieldValuePolicy.Name(command.Name); profile.LevelTarget = command.LevelTarget; profile.TaskItemTarget = command.TaskItemTarget; profile.MeritTarget = command.MeritTarget; profile.Version++;
        return $"調整職業門檻「{profile.Name}」（既有資格快照不變）";
    }, ct);
    public Task SaveSettingsAsync(CurrentSessionDto actor, SaveProgressionSettingsCommand command, CancellationToken ct) => Configure(actor, Guid.Empty, async () => {
        var zone = TimeZoneInfo.TryConvertWindowsIdToIanaId(command.TimeZoneId, out var iana) ? iana! : command.TimeZoneId;
        GameDayPolicy.NextBoundary(clock.GetUtcNow(), zone);
        if (!await db.Stages.AnyAsync(x => x.Id == command.AccumulationStageId, ct)) throw Missing();
        var settings = await db.ProgressionSettings.SingleAsync(ct); Check(settings.Version, command.ExpectedVersion);
        settings.TimeZoneId = zone; settings.ConversionRequiresActiveActivity = command.ConversionRequiresActiveActivity; settings.AccumulationStageId = command.AccumulationStageId; settings.Version++;
        return "調整時區與活動規則（既有資格快照不變）";
    }, ct);
    public Task SaveTransitionAsync(CurrentSessionDto actor, Guid stageId, SaveTransitionCommand command, CancellationToken ct) => Configure(actor, stageId, async () => {
        if (!Enum.IsDefined(command.Requirement)) throw new DomainRuleException("不支援此階段條件。");
        var from = command.AllowedFromStageIds ?? [];
        if (from.Distinct().Count() != from.Length || await db.Stages.CountAsync(x => from.Contains(x.Id), ct) != from.Length) throw new DomainRuleException("來源階段不正確。");
        var stage = await db.Stages.SingleOrDefaultAsync(x => x.Id == stageId, ct) ?? throw Missing(); Check(stage.Version, command.ExpectedVersion);
        stage.EntryRequirement = command.Requirement; stage.AllowedFromStageIdsJson = JsonSerializer.Serialize(from); stage.Version++;
        return $"調整「{stage.Name}」的允許來源與進入條件";
    }, ct);
    private async Task Configure(CurrentSessionDto actor, Guid targetId, Func<Task<string>> action, CancellationToken ct)
    {
        db.ChangeTracker.Clear(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM workspace_settings WHERE \"Id\" = 1 FOR UPDATE", ct);
        var description = await action(); var version = await Audit(actor, targetId, description, clock.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); await notifier.SnapshotChangedAsync(version, ct);
    }
    private async Task<long> Audit(CurrentSessionDto actor, Guid id, string description, DateTimeOffset now, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM workspace_state WHERE \"Id\" = 1 FOR UPDATE", ct);
        var workspace = await db.WorkspaceStates.SingleAsync(ct); workspace.Advance();
        db.AuditEvents.Add(new(Guid.NewGuid(), actor.ParticipantId, actor.Nickname, $"{actor.Nickname} {description}。", now, id)); return workspace.Version;
    }
    private static string Reason(string value) => FieldValuePolicy.Name(value);
    private static void Check(long actual, long expected) { if (actual != expected) throw new VersionConflictException(); }
    private static DomainRuleException Missing() => new("找不到指定資料。");
}
