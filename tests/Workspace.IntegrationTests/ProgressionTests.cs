using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Workspace.Application;
using Workspace.Domain;
using Workspace.Infrastructure;
using Workspace.Infrastructure.Persistence;

namespace Workspace.IntegrationTests;

[Collection("PostgreSQL coordination")]
public sealed class ProgressionTests
{
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.Parse("2026-09-09T19:59:59Z"); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Silent : IWorkspaceNotifier { public Task SnapshotChangedAsync(long version, CancellationToken ct) => Task.CompletedTask; }
    private static string Connection => Environment.GetEnvironmentVariable("ConnectionStrings__Workspace") ?? "Host=localhost;Database=workspace_dev;Username=workspace;Password=workspace_dev_only";
    private static WorkspaceDbContext Db(string? connection = null) => new(new DbContextOptionsBuilder<WorkspaceDbContext>().UseNpgsql(connection ?? Connection).Options);
    private sealed record Fixture(Guid Card, Guid Profile, CurrentSessionDto Actor, Clock Clock);
    private static async Task<Fixture> Setup(bool qualify = true, bool activate = false, string nickname = "M4 測試")
    {
        await using var db = Db(); var account = new GameAccount(Guid.NewGuid(), "M4 虛構帳號"); var card = new CharacterCard(Guid.NewGuid(), account.Id, "M4 虛構卡片");
        var actor = new ParticipantSession(Guid.NewGuid(), Guid.NewGuid(), nickname, DateTimeOffset.UtcNow);
        var profile = new ProgressionProfile { Id = Guid.NewGuid(), Name = "虛構職業", LevelTarget = 10, TaskItemTarget = 2, MeritTarget = 1000 };
        db.Accounts.Add(account); db.Cards.Add(card); db.Participants.Add(actor); db.ProgressionProfiles.Add(profile); await db.SaveChangesAsync();
        var fixture = new Fixture(card.Id, profile.Id, new(actor.Id, nickname, actor.IsAdmin), new Clock());
        if (qualify) await Execute(fixture, new(Guid.NewGuid(), ProgressionOperation.ReportProgress, ProfileId: profile.Id, Level: 10, TaskItems: 2, MeritBalance: 1000));
        if (activate) { fixture.Clock.Now += TimeSpan.FromSeconds(1); await Execute(fixture, new(Guid.NewGuid(), ProgressionOperation.StartActivity, ExpectedVersion: 1, Channel: "分流 3")); }
        return fixture;
    }
    private static async Task<ProgressionResultDto> Execute(Fixture f, ProgressionCommand command) { await using var db = Db(); return await new ProgressionService(db, new Silent(), f.Clock).ExecuteAsync(f.Actor, f.Card, command, default); }
    private static async Task<CardProgressionDto> Read(Fixture f) { await using var db = Db(); return (await new ProgressionService(db, new Silent(), f.Clock).GetAsync(default)).Cards.Single(x => x.CardId == f.Card); }
    private static ProgressionCommand Command(ProgressionOperation operation, long version = 0) => new(Guid.NewGuid(), operation, version);

    [Fact]
    public async Task AC036_037_threshold_snapshot_and_server_time_survive_repeated_reports_and_settings_changes()
    {
        var f = await Setup(); var first = Assert.Single((await Read(f)).Cycles);
        await Execute(f, new(Guid.NewGuid(), ProgressionOperation.ReportProgress, 1, f.Profile, 10, 2, 1000));
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(Assert.Single((await Read(f)).Cycles)));
        await using var db = Db(); var service = new ProgressionService(db, new Silent(), f.Clock);
        await service.SaveProfileAsync(f.Actor, f.Profile, new("門檻新名稱", 20, 3, 2000, 1), default);
        var settings = await db.ProgressionSettings.AsNoTracking().SingleAsync();
        try
        {
            await service.SaveSettingsAsync(f.Actor, new("UTC", false, settings.AccumulationStageId, settings.Version), default);
            Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(Assert.Single((await Read(f)).Cycles)));
            await Assert.ThrowsAsync<DomainRuleException>(() => Execute(f, Command(ProgressionOperation.StartActivity, 2) with { Channel = "3", OccurredAt = f.Clock.Now.AddDays(1) }));
            f.Clock.Now = first.EligibleFrom;
            await Execute(f, Command(ProgressionOperation.StartActivity, 2) with { Channel = "3" });
            Assert.Equal("Active", (await Read(f)).QualificationStatus);
        }
        finally
        {
            await using var reset = Db(); await reset.ProgressionSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.TimeZoneId, settings.TimeZoneId).SetProperty(x => x.ConversionRequiresActiveActivity, settings.ConversionRequiresActiveActivity));
        }
    }
    [Fact]
    public async Task AC038_041_conversion_receipt_returns_original_result_without_double_credit_or_audit()
    {
        var f = await Setup(activate: true); await Execute(f, Command(ProgressionOperation.RecordIncome) with { Amount = 200 });
        var command = Command(ProgressionOperation.RecordConversion) with { Amount = 300 };
        var original = await Execute(f, command); Assert.Equal(700, original.MeritBalance); Assert.Equal(500, original.CumulativeCredits);
        await Execute(f, Command(ProgressionOperation.RecordConversion) with { Amount = 700 });
        Assert.Equal(original, await Execute(f, command));
        var state = await Read(f); Assert.Equal(0, state.MeritBalance); Assert.Equal(1200, state.CumulativeCredits); Assert.Equal("Active", state.QualificationStatus);
        await using var db = Db(); Assert.Equal(3, await db.CreditEntries.CountAsync(x => x.CardId == f.Card));
        Assert.Equal(5, await db.AuditEvents.CountAsync(x => x.TargetId == f.Card));
        await Assert.ThrowsAsync<DomainRuleException>(() => Execute(f, command with { Amount = 301 }));
    }
    [Fact]
    public async Task AC040_parallel_conversions_cannot_overdraw_and_same_request_race_only_commits_once()
    {
        var f = await Setup(activate: true);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Attempt(ProgressionCommand command) { await gate.Task; return await Record.ExceptionAsync(() => Execute(f, command)); }
        var a = Attempt(Command(ProgressionOperation.RecordConversion) with { Amount = 800 }); var b = Attempt(Command(ProgressionOperation.RecordConversion) with { Amount = 800 }); gate.SetResult();
        var results = await Task.WhenAll(a, b); Assert.Single(results, x => x == null); Assert.Single(results, x => x is DomainRuleException);
        var state = await Read(f); Assert.Equal(200, state.MeritBalance); Assert.Equal(800, state.CumulativeCredits);
        var same = Command(ProgressionOperation.RecordConversion) with { Amount = 200 };
        gate = new(TaskCreationOptions.RunContinuationsAsynchronously); var one = Attempt(same); var two = Attempt(same); gate.SetResult(); Assert.All(await Task.WhenAll(one, two), Assert.Null);
        Assert.Equal(1000, (await Read(f)).CumulativeCredits);
    }
    [Fact]
    public async Task AC057_independent_income_adds_and_stale_correction_rejects()
    {
        var f = await Setup(qualify: false); var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Add(long amount) { await gate.Task; await Execute(f, Command(ProgressionOperation.RecordIncome) with { Amount = amount }); }
        var one = Add(10); var two = Add(20); gate.SetResult(); await Task.WhenAll(one, two);
        Assert.Equal(30, (await Read(f)).CumulativeCredits);
        await Assert.ThrowsAsync<VersionConflictException>(() => Execute(f, Command(ProgressionOperation.CorrectCredits) with { Amount = 25, ExpectedCreditVersion = 0, Reason = "補正" }));
        await Execute(f, Command(ProgressionOperation.CorrectCredits) with { Amount = 25, ExpectedCreditVersion = 2, Reason = "重複通報更正" });
        var corrected = await Read(f); Assert.Equal(25, corrected.CumulativeCredits); Assert.Contains(corrected.Entries, x => x.Kind == CreditKind.Correction && x.Amount == -5 && x.Reason == "重複通報更正");
    }
    [Fact]
    public async Task AC043_audit_database_failure_rolls_back_balance_receipt_and_income_entry()
    {
        var f = await Setup(activate: true); await using var db = Db();
        await db.Database.OpenConnectionAsync();
        await using var injectFailure = new NpgsqlCommand($$"""
            CREATE FUNCTION m4_fail_audit() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN IF NEW."TargetId" = '{{f.Card}}'::uuid THEN RAISE EXCEPTION 'injected audit failure'; END IF; RETURN NEW; END $body$;
            CREATE TRIGGER m4_fail_audit BEFORE INSERT ON audit_events FOR EACH ROW EXECUTE FUNCTION m4_fail_audit();
            """, (NpgsqlConnection)db.Database.GetDbConnection());
        await injectFailure.ExecuteNonQueryAsync();
        var command = Command(ProgressionOperation.RecordConversion) with { Amount = 300 };
        try { await Assert.ThrowsAsync<DbUpdateException>(() => Execute(f, command)); }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER m4_fail_audit ON audit_events; DROP FUNCTION m4_fail_audit();"); }
        var state = await Read(f); Assert.Equal(1000, state.MeritBalance); Assert.Equal(0, state.CumulativeCredits); Assert.Empty(state.Entries);
        Assert.False(await db.CommandReceipts.AnyAsync(x => x.RequestId == command.RequestId));
        await Execute(f, command); Assert.Equal(300, (await Read(f)).CumulativeCredits);
    }
    [Fact]
    public async Task AC044_045_requalification_preserves_level_items_income_and_permanent_flag_cannot_be_cleared()
    {
        var f = await Setup(activate: true); var current = await Read(f); var activity = Assert.Single(current.Activities);
        await Execute(f, Command(ProgressionOperation.RecordIncome) with { Amount = 200 });
        await Assert.ThrowsAsync<DomainRuleException>(() => Execute(f, Command(ProgressionOperation.Requalify, 3) with { Reason = "死亡過多", MeritBalance = 0 }));
        await Execute(f, Command(ProgressionOperation.EndActivity, 3) with { ActivityId = activity.Id, ExpectedActivityVersion = 1 });
        await Execute(f, Command(ProgressionOperation.Requalify, 4) with { Reason = "死亡過多", MeritBalance = 0 });
        current = await Read(f); Assert.Equal(10, current.Level); Assert.Equal(2, current.TaskItems); Assert.Equal(200, current.CumulativeCredits); Assert.Equal("NeedsRequalification", current.QualificationStatus);
        Assert.Equal(Guid.Parse("50000000-0000-0000-0000-000000000003"), current.StageId);
        await Execute(f, new(Guid.NewGuid(), ProgressionOperation.ReportProgress, 5, f.Profile, 10, 2, 1000));
        current = await Read(f); Assert.Equal(2, current.Cycles.Count); Assert.Equal(f.Clock.Now.AddDays(1), current.Cycles.Single(x => x.InvalidatedAt == null).EligibleFrom);
        await Execute(f, Command(ProgressionOperation.Disqualify, 6) with { Reason = "TK 永久失格" });
        await Execute(f, new(Guid.NewGuid(), ProgressionOperation.ReportProgress, 7, f.Profile, 99, 99, 9999));
        Assert.Equal("Disqualified", (await Read(f)).QualificationStatus);
        f.Clock.Now += TimeSpan.FromDays(2);
        await Assert.ThrowsAsync<DomainRuleException>(() => Execute(f with { Actor = f.Actor with { Nickname = "Admin", CanReadAudit = true } }, Command(ProgressionOperation.StartActivity, 8) with { Channel = "3" }));
        await using var db = Db(); var card = await db.Cards.SingleAsync(x => x.Id == f.Card);
        await Assert.ThrowsAsync<DomainRuleException>(() => new ConfigurationService(db, new Silent(), f.Clock).MoveStageAsync(f.Actor, f.Card, new(Guid.Parse("50000000-0000-0000-0000-000000000004"), card.MetadataVersion), default));
    }
    [Fact]
    public async Task AC046_archive_requires_explicit_release_and_replacement_starts_independent()
    {
        var f = await Setup(activate: true); var state = await Read(f);
        await using var db = Db(); var coordination = new WorkspaceCoordinator(db, new Silent()); var account = await db.Cards.Where(x => x.Id == f.Card).Select(x => x.AccountId).SingleAsync();
        var version = await db.Accounts.Where(x => x.Id == account).Select(x => x.CoordinationVersion).SingleAsync();
        await coordination.ReserveAsync(f.Actor, new(f.Card, Guid.Parse("20000000-0000-0000-0000-000000000001"), version), default);
        await Assert.ThrowsAsync<DomainRuleException>(() => Execute(f, Command(ProgressionOperation.Archive, state.Version)));
        await Execute(f, Command(ProgressionOperation.EndActivity, state.Version) with { ActivityId = state.Activities[0].Id, ExpectedActivityVersion = 1 });
        await Assert.ThrowsAsync<DomainRuleException>(() => Execute(f, Command(ProgressionOperation.Archive, state.Version + 1)));
        var reservation = await db.Reservations.AsNoTracking().SingleAsync(x => x.CardId == f.Card); db.ChangeTracker.Clear();
        version = await db.Accounts.Where(x => x.Id == account).Select(x => x.CoordinationVersion).SingleAsync();
        await coordination.CancelAsync(f.Actor, new(f.Card, version, reservation.Version), default);
        await Execute(f, Command(ProgressionOperation.RecordIncome) with { Amount = 10 });
        var archived = await Execute(f, Command(ProgressionOperation.Archive, state.Version + 2));
        var replacement = await Execute(f, Command(ProgressionOperation.CreateReplacement, archived.CardVersion) with { ReplacementName = "接替測試" });
        var snapshot = await new ProgressionService(db, new Silent(), f.Clock).GetAsync(default);
        var next = snapshot.Cards.Single(x => x.CardId == replacement.ReplacementCardId);
        Assert.Equal(0, next.CumulativeCredits); Assert.Empty(next.Cycles); Assert.Equal(f.Card, next.ReplacesCardId); Assert.Equal(account, next.AccountId);
        Assert.Equal(10, snapshot.Cards.Single(x => x.CardId == f.Card).CumulativeCredits);
        Assert.DoesNotContain((await coordination.GetSnapshotAsync(f.Actor, default)).Accounts.SelectMany(x => x.Cards), x => x.Id == f.Card);
        await Assert.ThrowsAsync<DomainRuleException>(() => coordination.SetUsageAsync(f.Actor, f.Card, new(CardUsageStatus.InUse, true), default));
    }
    [Fact]
    public async Task AC047_049_new_activities_have_new_ids_and_corrections_keep_recorded_time_and_hide_admin()
    {
        var f = await Setup(activate: true, nickname: "Admin"); var first = Assert.Single((await Read(f)).Activities); Assert.Null(first.OperatorName);
        f.Clock.Now += TimeSpan.FromHours(1);
        await Execute(f, Command(ProgressionOperation.CorrectActivity, 2) with { ActivityId = first.Id, ExpectedActivityVersion = 1, Channel = "分流 8", OccurredAt = f.Clock.Now.AddMinutes(-30), Reason = "補填實際入場" });
        var corrected = Assert.Single((await Read(f)).Activities); Assert.Equal(first.RecordedAt, corrected.RecordedAt); Assert.Equal("分流 8", corrected.Channel); Assert.NotEqual(first.OccurredAt, corrected.OccurredAt);
        await Execute(f, Command(ProgressionOperation.EndActivity, 3) with { ActivityId = first.Id, ExpectedActivityVersion = 2 });
        var second = await Execute(f, Command(ProgressionOperation.StartActivity, 4) with { Channel = "分流 8" }); Assert.NotEqual(first.Id, second.ActivityId);
        await using var db = Db(); (await db.Participants.SingleAsync(x => x.Id == f.Actor.ParticipantId)).Rename("改成普通人"); await db.SaveChangesAsync();
        Assert.All((await Read(f)).Activities, x => Assert.Null(x.OperatorName));
        Assert.DoesNotContain(f.Actor.ParticipantId.ToString(), JsonSerializer.Serialize(await Read(f)));
    }
    [Fact]
    public async Task Stage_gates_use_ids_and_progress_not_display_names()
    {
        var f = await Setup(qualify: false); await using var db = Db(); var service = new ConfigurationService(db, new Silent(), f.Clock);
        var taskStage = Guid.Parse("50000000-0000-0000-0000-000000000002");
        await Assert.ThrowsAsync<DomainRuleException>(() => service.MoveStageAsync(f.Actor, f.Card, new(taskStage, 1), default));
        await Execute(f, new(Guid.NewGuid(), ProgressionOperation.ReportProgress, 0, f.Profile, 10, 0, 0));
        await service.MoveStageAsync(f.Actor, f.Card, new(taskStage, 1), default);
        await Assert.ThrowsAsync<DomainRuleException>(() => service.MoveStageAsync(f.Actor, f.Card, new(Guid.Parse("50000000-0000-0000-0000-000000000003"), 2), default));
    }
    [Fact]
    public async Task Api_uses_server_clock_and_receipts_even_with_forged_actor_or_future_client_time()
    {
        var f = await Setup(); using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services => { services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(f.Clock); }));
        using var client = factory.CreateClient();
        async Task Token() { client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (await client.GetFromJsonAsync<JsonElement>("/api/session/csrf")).GetProperty("token").GetString()); }
        await Token(); (await client.PostAsJsonAsync("/api/session", new { nickname = "API 測試" })).EnsureSuccessStatusCode(); await Token();
        var early = await client.PostAsJsonAsync($"/api/cards/{f.Card}/progression", new { requestId = Guid.NewGuid(), operation = "StartActivity", expectedVersion = 1, channel = "3", occurredAt = f.Clock.Now.AddDays(1), actorId = f.Actor.ParticipantId });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        f.Clock.Now += TimeSpan.FromSeconds(1);
        var body = new { requestId = Guid.NewGuid(), operation = "StartActivity", expectedVersion = 1, channel = "3" };
        var one = await client.PostAsJsonAsync($"/api/cards/{f.Card}/progression", body); one.EnsureSuccessStatusCode();
        var two = await client.PostAsJsonAsync($"/api/cards/{f.Card}/progression", body); two.EnsureSuccessStatusCode(); Assert.Equal(await one.Content.ReadAsStringAsync(), await two.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Archive_and_reservation_race_cannot_hide_an_active_reservation()
    {
        var f = await Setup(qualify: false);
        await using var setup = Db();
        var accountId = await setup.Cards.Where(x => x.Id == f.Card).Select(x => x.AccountId).SingleAsync();
        var version = await setup.Accounts.Where(x => x.Id == accountId).Select(x => x.CoordinationVersion).SingleAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Archive() { await gate.Task; return await Record.ExceptionAsync(() => Execute(f, Command(ProgressionOperation.Archive))); }
        async Task<Exception?> Reserve() { await gate.Task; await using var db = Db(); return await Record.ExceptionAsync(() => new WorkspaceCoordinator(db, new Silent()).ReserveAsync(f.Actor, new(f.Card, Guid.Parse("20000000-0000-0000-0000-000000000001"), version), default)); }
        var one = Archive(); var two = Reserve(); gate.SetResult();
        var results = await Task.WhenAll(one, two); Assert.Single(results, x => x == null);
        await using var verify = Db();
        var archived = (await verify.Cards.SingleAsync(x => x.Id == f.Card)).ArchivedAt != null;
        var reserved = await verify.Reservations.AnyAsync(x => x.CardId == f.Card && x.State != ReservationState.Released);
        Assert.NotEqual(archived, reserved);
    }
    [Fact]
    public async Task Concurrent_activity_entries_commit_one_complete_activity_and_cross_card_request_reuse_rejects()
    {
        var f = await Setup(); f.Clock.Now += TimeSpan.FromSeconds(1);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Enter(string channel) { await gate.Task; return await Record.ExceptionAsync(() => Execute(f, Command(ProgressionOperation.StartActivity, 1) with { Channel = channel })); }
        var one = Enter("甲"); var two = Enter("乙"); gate.SetResult(); Assert.Single(await Task.WhenAll(one, two), x => x == null);
        var state = await Read(f); Assert.Single(state.Activities); Assert.Equal(f.Clock.Now, state.Activities[0].OccurredAt);
        var receipt = Command(ProgressionOperation.RecordIncome) with { Amount = 10 }; await Execute(f, receipt);
        var other = await Setup(qualify: false);
        await Assert.ThrowsAsync<DomainRuleException>(() => Execute(other, receipt)); Assert.Equal(0, (await Read(other)).CumulativeCredits);
    }
    [Fact]
    public async Task Conversion_policy_can_require_current_activity_without_erasing_activation()
    {
        var f = await Setup(activate: true); var activity = Assert.Single((await Read(f)).Activities);
        await Execute(f, Command(ProgressionOperation.EndActivity, 2) with { ActivityId = activity.Id, ExpectedActivityVersion = 1 });
        await using var db = Db(); var service = new ProgressionService(db, new Silent(), f.Clock);
        var settings = await db.ProgressionSettings.AsNoTracking().SingleAsync();
        try
        {
            await service.SaveSettingsAsync(f.Actor, new(settings.TimeZoneId, true, settings.AccumulationStageId, settings.Version), default);
            await Assert.ThrowsAsync<DomainRuleException>(() => Execute(f, Command(ProgressionOperation.RecordConversion) with { Amount = 10 }));
            Assert.Equal("Active", (await Read(f)).QualificationStatus);
            await service.SaveSettingsAsync(f.Actor, new(settings.TimeZoneId, false, settings.AccumulationStageId, settings.Version + 1), default);
            await Execute(f, Command(ProgressionOperation.RecordConversion) with { Amount = 10 }); Assert.Equal(10, (await Read(f)).CumulativeCredits);
        }
        finally { await using var reset = Db(); await reset.ProgressionSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.ConversionRequiresActiveActivity, settings.ConversionRequiresActiveActivity)); }
    }
    [Fact]
    public async Task M3_upgrade_preserves_custom_values_and_occupancy_without_inventing_qualification()
    {
        var builder = new NpgsqlConnectionStringBuilder(Connection) { Database = "m4_upgrade_" + Guid.NewGuid().ToString("N") };
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(Connection) { Database = "postgres" }.ConnectionString); await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {builder.Database}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            await using var db = Db(builder.ConnectionString); await db.GetService<IMigrator>().MigrateAsync("ConfigurableData");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO resource_reservations ("Id","AccountId","CardId","RegionId","State","CreatedByParticipantId","UpdatedAt","Version") VALUES
                ('90000000-0000-0000-0000-000000000004','10000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','Occupied','90000000-0000-0000-0000-000000000002',now(),9);
                INSERT INTO record_field_values ("FieldId","RecordId","ValueJson","Attached","Version")
                SELECT "Id", '10000000-0000-0000-0000-000000000001', '"fictional-m3-preserved"', true, 4 FROM field_definitions WHERE "CollectionId"='40000000-0000-0000-0000-000000000002' LIMIT 1;
                """);
            await db.Database.MigrateAsync(); Assert.Equal(4, (await db.Database.GetAppliedMigrationsAsync()).Count());
            Assert.Equal(9, (await db.Reservations.SingleAsync()).Version); Assert.Contains("fictional-m3-preserved", (await db.RecordValues.SingleAsync()).ValueJson);
            Assert.Equal(4, (await db.RecordValues.SingleAsync()).Version);
            Assert.Empty(await db.QualificationCycles.ToListAsync()); Assert.Empty(await db.Progressions.ToListAsync()); Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { NpgsqlConnection.ClearAllPools(); await using var drop = new NpgsqlCommand($"DROP DATABASE {builder.Database} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync(); }
    }
}
