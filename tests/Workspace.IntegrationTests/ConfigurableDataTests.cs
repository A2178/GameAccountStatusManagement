using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Workspace.Application;
using Workspace.Domain;
using Workspace.Infrastructure;
using Workspace.Infrastructure.Persistence;

namespace Workspace.IntegrationTests;

[Collection("PostgreSQL coordination")]
public sealed class ConfigurableDataTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;
    public ConfigurableDataTests(WebApplicationFactory<Program> factory) => this.factory = factory;
    private static readonly CurrentSessionDto Actor = new(Guid.NewGuid(), "M2 測試", false);
    private static string Connection => Environment.GetEnvironmentVariable("ConnectionStrings__Workspace") ?? "Host=localhost;Database=workspace_dev;Username=workspace;Password=workspace_dev_only";
    private static WorkspaceDbContext Db(string? connection = null) => new(new DbContextOptionsBuilder<WorkspaceDbContext>().UseNpgsql(connection ?? Connection).Options);
    private static ConfigurationService Service(WorkspaceDbContext db) => new(db, new NullNotifier());
    private static SaveFieldCommand Definition(Guid collection, FieldKind kind = FieldKind.Text, FieldScope scope = FieldScope.Common) => new(collection, "測試欄位", kind, scope, [], null, "#526b84", 0, false, false, 0);
    private static SaveValueCommand Value(object? value, long version = 0, long definition = 1, bool attached = true) => new(JsonSerializer.SerializeToElement(value), version, definition, attached);
    private static async Task<(Guid Account, Guid A, Guid B)> Cards()
    {
        await using var db = Db(); var account = new GameAccount(Guid.NewGuid(), "虛構 M2 帳號");
        var a = new CharacterCard(Guid.NewGuid(), account.Id, "M2 甲"); var b = new CharacterCard(Guid.NewGuid(), account.Id, "M2 乙");
        db.Accounts.Add(account); db.Cards.AddRange(a, b); await db.SaveChangesAsync(); return (account.Id, a.Id, b.Id);
    }

    [Fact]
    public async Task AC015_016_common_definition_has_independent_values_and_applies_to_future_cards_only()
    {
        var cards = await Cards(); var field = Guid.NewGuid();
        await using var db = Db(); var service = Service(db);
        await service.SaveFieldAsync(Actor, field, Definition(BuiltInCollections.Cards), default);
        await service.SaveValueAsync(Actor, field, cards.A, Value("戰士"), default);
        await service.SaveValueAsync(Actor, field, cards.B, Value("法師"), default);
        var snapshot = await service.GetCollectionAsync(BuiltInCollections.Cards, default);
        Assert.Equal("戰士", snapshot.Values.Single(x => x.RecordId == cards.A && x.FieldId == field).Value.GetString());
        Assert.Equal("法師", snapshot.Values.Single(x => x.RecordId == cards.B && x.FieldId == field).Value.GetString());
        var newCard = Guid.NewGuid(); db.Cards.Add(new(newCard, cards.Account, "新卡片")); await db.SaveChangesAsync();
        snapshot = await service.GetCollectionAsync(BuiltInCollections.Cards, default);
        Assert.Contains(snapshot.Records, x => x.Id == newCard); Assert.Contains(snapshot.Fields, x => x.Id == field);
        Assert.DoesNotContain(snapshot.Values, x => x.RecordId == newCard && x.FieldId == field);
        Assert.DoesNotContain((await service.GetCollectionAsync(BuiltInCollections.Accounts, default)).Fields, x => x.Id == field);
        await service.DeleteFieldAsync(Actor, field, 1, default);
        Assert.DoesNotContain((await service.GetCollectionAsync(BuiltInCollections.Cards, default)).Fields, x => x.Id == field);
        await Assert.ThrowsAsync<FieldConflictException>(() => service.SaveValueAsync(Actor, field, cards.A, Value("刪除後草稿", 1), default));
    }

    [Fact]
    public async Task AC017_shared_value_race_returns_latest_value_and_never_exposes_actor()
    {
        var field = Guid.NewGuid();
        await using (var db = Db()) await Service(db).SaveFieldAsync(Actor, field, Definition(BuiltInCollections.Cards, scope: FieldScope.Shared), default);
        using var a = factory.CreateClient(); using var b = factory.CreateClient();
        await Login(a, "小明"); await Login(b, "Admin");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Attempt(HttpClient client, string value) { await gate.Task; return await client.PutAsJsonAsync($"/api/fields/{field}/shared", Value(value)); }
        var one = Attempt(a, "alpha"); var two = Attempt(b, "beta"); gate.SetResult();
        var responses = await Task.WhenAll(one, two);
        Assert.Single(responses, x => x.IsSuccessStatusCode);
        var conflict = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var body = await conflict.Content.ReadAsStringAsync(); Assert.DoesNotContain("Admin", body);
        Assert.Contains("current", body);
        await using var verify = Db(); Assert.Equal(1, await verify.SharedValues.CountAsync(x => x.FieldId == field));
        Assert.Equal(1, (await verify.SharedValues.SingleAsync(x => x.FieldId == field)).Version);
    }

    [Fact]
    public async Task AC019_020_parallel_different_fields_succeed_but_first_insert_same_cell_conflicts()
    {
        var cards = await Cards(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await using (var setup = Db()) { await Service(setup).SaveFieldAsync(Actor, first, Definition(BuiltInCollections.Cards), default); await Service(setup).SaveFieldAsync(Actor, second, Definition(BuiltInCollections.Cards), default); }
        async Task<Exception?[]> Race(Guid a, Guid b)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<Exception?> Attempt(Guid field, string text)
            {
                await using var db = Db(); await gate.Task;
                return await Record.ExceptionAsync(() => Service(db).SaveValueAsync(Actor, field, cards.A, Value(text), default));
            }
            var one = Attempt(a, "one"); var two = Attempt(b, "two"); gate.SetResult(); return await Task.WhenAll(one, two);
        }
        Assert.All(await Race(first, second), Assert.Null);
        var third = Guid.NewGuid(); await using (var setup = Db()) await Service(setup).SaveFieldAsync(Actor, third, Definition(BuiltInCollections.Cards), default);
        var outcomes = await Race(third, third); Assert.Single(outcomes, x => x == null); Assert.Single(outcomes, x => x is FieldConflictException);
        await using var verify = Db(); Assert.Equal(3, await verify.RecordValues.CountAsync(x => x.RecordId == cards.A));
    }

    [Fact]
    public async Task AC018_021_option_rename_keeps_id_type_changes_revalidate_and_zero_is_not_null()
    {
        var cards = await Cards(); var field = Guid.NewGuid(); var option = Guid.NewGuid();
        var definition = Definition(BuiltInCollections.Cards, FieldKind.SingleSelect) with { Options = [new(option, "戰士", "#ffffff")] };
        await using var db = Db(); var service = Service(db);
        await service.SaveFieldAsync(Actor, field, definition, default);
        await service.SaveValueAsync(Actor, field, cards.A, Value(option), default);
        await service.SaveFieldAsync(Actor, field, definition with { Name = "新職業", Options = [new(option, "騎士", "#ffffff")], ExpectedVersion = 1 }, default);
        var snapshot = await service.GetCollectionAsync(BuiltInCollections.Cards, default);
        Assert.Equal(option, snapshot.Values.Single(x => x.FieldId == field).Value.GetGuid());
        Assert.Equal("騎士", snapshot.Fields.Single(x => x.Id == field).Options.Single().Name);
        await Assert.ThrowsAsync<FieldConflictException>(() => service.SaveValueAsync(Actor, field, cards.B, Value(option), default));
        await Assert.ThrowsAsync<DomainRuleException>(() => service.SaveFieldAsync(Actor, field, definition with { Kind = FieldKind.Integer, ExpectedVersion = 2 }, default));
        var number = Guid.NewGuid(); await service.SaveFieldAsync(Actor, number, Definition(BuiltInCollections.Cards, FieldKind.Integer), default);
        await service.SaveValueAsync(Actor, number, cards.A, Value(0), default);
        await service.SaveValueAsync(Actor, number, cards.B, Value(null), default);
        snapshot = await service.GetCollectionAsync(BuiltInCollections.Cards, default);
        Assert.Equal(0, snapshot.Values.Single(x => x.FieldId == number && x.RecordId == cards.A).Value.GetInt32());
        Assert.Equal(JsonValueKind.Null, snapshot.Values.Single(x => x.FieldId == number && x.RecordId == cards.B).Value.ValueKind);
    }

    [Fact]
    public async Task Individual_detach_preserves_version_and_scope_cannot_silently_destroy_values()
    {
        var cards = await Cards(); var field = Guid.NewGuid(); var definition = Definition(BuiltInCollections.Cards, scope: FieldScope.Individual);
        await using var db = Db(); var service = Service(db); await service.SaveFieldAsync(Actor, field, definition, default);
        await service.SaveValueAsync(Actor, field, cards.A, Value("標記"), default);
        await service.SaveValueAsync(Actor, field, cards.A, Value(null, 1, attached: false), default);
        var value = (await service.GetCollectionAsync(BuiltInCollections.Cards, default)).Values.Single(x => x.FieldId == field);
        Assert.False(value.Attached); Assert.Equal(2, value.Version);
        await Assert.ThrowsAsync<FieldConflictException>(() => service.SaveValueAsync(Actor, field, cards.A, Value("舊草稿"), default));
        await Assert.ThrowsAsync<DomainRuleException>(() => service.SaveFieldAsync(Actor, field, definition with { Scope = FieldScope.Shared, ExpectedVersion = 1 }, default));
    }

    [Fact]
    public async Task AC011_012_014_022_plain_credentials_relations_and_appearance_changes_preserve_core_identity()
    {
        var cards = await Cards(); var password = Guid.NewGuid();
        await using var db = Db(); var service = Service(db);
        await service.SaveFieldAsync(Actor, password, Definition(BuiltInCollections.Accounts) with { Name = "虛構密碼" }, default);
        await service.SaveValueAsync(Actor, password, cards.Account, Value("fictional-password-123"), default);
        Assert.Equal("fictional-password-123", JsonSerializer.Deserialize<string>((await db.RecordValues.AsNoTracking().SingleAsync(x => x.FieldId == password)).ValueJson));
        Assert.DoesNotContain(await db.AuditEvents.AsNoTracking().ToListAsync(), x => x.Description.Contains("fictional-password-123"));
        await service.RenameAsync(Actor, "accounts", cards.Account, new("帳號新別名", 1), default);
        var region = await db.Regions.AsNoTracking().FirstAsync();
        var coordinator = new WorkspaceCoordinator(db, new NullNotifier());
        await coordinator.ReserveAsync(Actor, new(cards.A, region.Id, 0), default);
        var bound = await db.Fields.AsNoTracking().FirstAsync(x => x.Binding == FieldBinding.Resource && !x.Deleted);
        using var client = factory.CreateClient(); await Login(client, "Admin");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/fields/{bound.Id}/records/{cards.A}", Value("其他區域", definition: bound.Version))).StatusCode);
        await service.SaveFieldAsync(Actor, bound.Id, new(bound.CollectionId, "目的地別名", bound.Kind, bound.Scope, [], null, bound.Color, 0, false, true, bound.Version), default);
        Assert.True(await db.Reservations.AnyAsync(x => x.CardId == cards.A && x.State == ReservationState.Reserved));
        var ordinary = Guid.NewGuid(); await service.SaveFieldAsync(Actor, ordinary, Definition(BuiltInCollections.Cards) with { Name = "區域" }, default);
        await service.SaveValueAsync(Actor, ordinary, cards.A, Value("自由文字不改占用"), default);
        Assert.Equal(cards.Account, (await db.Cards.AsNoTracking().SingleAsync(x => x.Id == cards.A)).AccountId);
        // Restore seeded appearance for other/browser tests.
        await service.SaveFieldAsync(Actor, bound.Id, new(bound.CollectionId, bound.Name, bound.Kind, bound.Scope, [], null, bound.Color, 0, false, false, bound.Version + 1), default);
    }

    [Fact]
    public async Task AC022_free_table_relates_to_existing_accounts_and_rejects_cross_collection_values()
    {
        var cards = await Cards(); await using var db = Db(); var service = Service(db);
        var name = "備忘" + Guid.NewGuid().ToString("N")[..6]; await service.CreateCollectionAsync(Actor, new(name), default);
        var collection = (await service.GetAsync(default)).Collections.Single(x => x.Name == name);
        await service.CreateRecordAsync(Actor, collection.Id, new("自由資料"), default);
        var record = (await service.GetCollectionAsync(collection.Id, default)).Records.Single();
        var field = Guid.NewGuid(); await service.SaveFieldAsync(Actor, field, Definition(collection.Id, FieldKind.Relation) with { RelationCollectionId = BuiltInCollections.Accounts }, default);
        await service.SaveValueAsync(Actor, field, record.Id, Value(cards.Account), default);
        await Assert.ThrowsAsync<DomainRuleException>(() => service.SaveValueAsync(Actor, field, cards.A, Value(cards.Account), default));
        await Assert.ThrowsAsync<DomainRuleException>(() => service.SaveValueAsync(Actor, field, record.Id, Value(cards.A, 1), default));
    }

    [Fact]
    public async Task AC018_stage_region_and_view_names_keep_ids_and_stage_moves_do_not_release_occupancy()
    {
        var cards = await Cards(); var stageId = Guid.NewGuid(); var regionId = Guid.NewGuid(); var viewId = Guid.NewGuid();
        await using var db = Db(); db.Stages.Add(new() { Id = stageId, Name = "測試階段" }); db.Regions.Add(new(regionId, "測試區域")); await db.SaveChangesAsync();
        var service = Service(db);
        await new WorkspaceCoordinator(db, new NullNotifier()).ReserveAsync(Actor, new(cards.A, regionId, 0), default);
        await service.MoveStageAsync(Actor, cards.A, new(stageId, 1), default);
        await service.RenameAsync(Actor, "stages", stageId, new("階段新名稱", 1), default);
        await service.RenameAsync(Actor, "regions", regionId, new("區域新名稱", 1), default);
        var command = new SaveViewCommand(BuiltInCollections.Cards, "測試分頁", "Table", 3, stageId, [], null, false, null, "", 0);
        await service.SaveViewAsync(Actor, viewId, command, default);
        await service.SaveViewAsync(Actor, viewId, command with { Name = "分頁新名稱", Display = "Panel", ExpectedVersion = 1 }, default);
        await Assert.ThrowsAsync<VersionConflictException>(() => service.SaveViewAsync(Actor, viewId, command with { ExpectedVersion = 1 }, default));
        var configuration = await service.GetAsync(default);
        Assert.Equal(stageId, configuration.Views.Single(x => x.Id == viewId).StageId);
        Assert.Equal("分頁新名稱", configuration.Views.Single(x => x.Id == viewId).Name);
        Assert.Equal("階段新名稱", configuration.Stages.Single(x => x.Id == stageId).Name);
        var snapshot = await new WorkspaceCoordinator(db, new NullNotifier()).GetSnapshotAsync(Actor, default);
        Assert.Equal("區域新名稱", snapshot.Accounts.Single(x => x.Id == cards.Account).Cards.Single(x => x.Id == cards.A).Reservation!.RegionName);
        Assert.Equal(stageId, (await db.Cards.AsNoTracking().SingleAsync(x => x.Id == cards.A)).StageId);
    }

    [Fact]
    public async Task AC011_deleting_bound_presentation_never_deletes_reservation_or_bypasses_region_rule()
    {
        var cards = await Cards(); var id = Guid.NewGuid();
        await using var db = Db();
        db.Fields.Add(new() { Id = id, CollectionId = BuiltInCollections.Cards, Name = "區域投影", Binding = FieldBinding.Resource }); await db.SaveChangesAsync();
        var regions = await db.Regions.AsNoTracking().Take(2).ToListAsync();
        var coordinator = new WorkspaceCoordinator(db, new NullNotifier()); await coordinator.ReserveAsync(Actor, new(cards.A, regions[0].Id, 0), default);
        await Service(db).DeleteFieldAsync(Actor, id, 1, default);
        Assert.True(await db.Reservations.AnyAsync(x => x.CardId == cards.A && x.State == ReservationState.Reserved));
        await Assert.ThrowsAsync<DomainRuleException>(() => coordinator.ReserveAsync(Actor, new(cards.B, regions[1].Id, 1), default));
    }

    [Fact]
    public async Task AC021_inflight_write_waits_for_definition_change_then_revalidates()
    {
        var cards = await Cards(); var field = Guid.NewGuid();
        await using (var setup = Db()) await Service(setup).SaveFieldAsync(Actor, field, Definition(BuiltInCollections.Cards), default);
        await using var editor = Db(); await using var writer = Db();
        await using var tx = await editor.Database.BeginTransactionAsync();
        await editor.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({field.ToString()}, 0))");
        var definition = await editor.Fields.SingleAsync(x => x.Id == field); definition.Kind = FieldKind.Integer; definition.Version++;
        var pending = Service(writer).SaveValueAsync(Actor, field, cards.A, Value("stale text"), default);
        await editor.SaveChangesAsync(); await tx.CommitAsync();
        await Assert.ThrowsAsync<FieldConflictException>(() => pending);
        Assert.False(await editor.RecordValues.AnyAsync(x => x.FieldId == field));
    }

    [Fact]
    public async Task AC053_M1_upgrade_preserves_reservation_account_card_and_history()
    {
        var builder = new NpgsqlConnectionStringBuilder(Connection); var database = "m2_upgrade_" + Guid.NewGuid().ToString("N");
        var adminBuilder = new NpgsqlConnectionStringBuilder(Connection) { Database = "postgres" };
        await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString); await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin)) await create.ExecuteNonQueryAsync();
        builder.Database = database;
        try
        {
            await using var db = Db(builder.ConnectionString);
            await db.GetService<IMigrator>().MigrateAsync("CoreCoordination");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO resource_reservations ("Id", "AccountId", "CardId", "RegionId", "State", "CreatedByParticipantId", "UpdatedAt", "Version") VALUES
                ('90000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','Occupied','90000000-0000-0000-0000-000000000002',now(),7);
                """);
            await db.Database.MigrateAsync();
            var reservation = await db.Reservations.SingleAsync(); Assert.Equal(7, reservation.Version); Assert.Equal(ReservationState.Occupied, reservation.State);
            Assert.Equal(reservation.AccountId, (await db.Cards.SingleAsync(x => x.Id == reservation.CardId)).AccountId);
            Assert.Equal(3, (await db.Database.GetAppliedMigrationsAsync()).Count());
            Assert.Equal(2, await db.Collections.CountAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { NpgsqlConnection.ClearAllPools(); await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync(); }
    }
    private static async Task Login(HttpClient client, string nickname)
    {
        async Task<string> Token() => (await client.GetFromJsonAsync<JsonElement>("/api/session/csrf")).GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await Token()); (await client.PostAsJsonAsync("/api/session", new { nickname })).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await Token());
    }
    private sealed class NullNotifier : IWorkspaceNotifier { public Task SnapshotChangedAsync(long version, CancellationToken ct) => Task.CompletedTask; }
}
