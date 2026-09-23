using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workspace.Application;
using Workspace.Domain;
using Workspace.Infrastructure;
using Workspace.Infrastructure.Persistence;

namespace Workspace.IntegrationTests;

[Collection("PostgreSQL coordination")]
public sealed class CollaborationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;
    public CollaborationTests(WebApplicationFactory<Program> factory) => this.factory = factory;
    private static WorkspaceDbContext Db() => new(new DbContextOptionsBuilder<WorkspaceDbContext>().UseNpgsql(
        Environment.GetEnvironmentVariable("ConnectionStrings__Workspace") ?? "Host=localhost;Database=workspace_dev;Username=workspace;Password=workspace_dev_only").Options);
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Notifications : ICollaborationNotifier, IWorkspaceNotifier
    {
        public readonly List<PresenceSnapshotDto> Public = [];
        public Task PresenceChangedAsync(PresenceSnapshotDto snapshot, CancellationToken ct) { Public.Add(snapshot); return Task.CompletedTask; }
        public Task SessionChangedAsync(IReadOnlyList<string> connections, CancellationToken ct) => Task.CompletedTask;
        public Task SnapshotChangedAsync(long version, CancellationToken ct) => Task.CompletedTask;
    }
    [Fact]
    public void AC029_031_tabs_aggregate_same_names_remain_distinct_and_expire_independently()
    {
        var clock = new Clock(); var registry = new PresenceRegistry(clock, TimeSpan.FromSeconds(30));
        var first = new CurrentSessionDto(Guid.NewGuid(), "同名", false); var second = new CurrentSessionDto(Guid.NewGuid(), "同名", false);
        var target = new PresenceTargetDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Editing", "虛構欄位");
        registry.Touch("a", first, target); registry.Touch("b", first, target); registry.Touch("c", second, null);
        var snapshot = registry.Snapshot().Snapshot;
        Assert.Equal(2, snapshot.Members.Count); Assert.Single(snapshot.Members.Single(x => x.ParticipantId == first.ParticipantId).Targets);
        var identity = snapshot.Members.Single(x => x.ParticipantId == first.ParticipantId);
        registry.Remove("a"); Assert.False(registry.Snapshot().Changed);
        clock.Now += TimeSpan.FromSeconds(29); registry.Touch("b", first, target);
        clock.Now += TimeSpan.FromSeconds(1); Assert.Single(registry.Snapshot().Snapshot.Members);
        clock.Now += TimeSpan.FromSeconds(30); Assert.Empty(registry.Snapshot().Snapshot.Members);
        registry.Touch("reconnected", first, target);
        var reconnected = Assert.Single(registry.Snapshot().Snapshot.Members);
        Assert.Equal(identity.ParticipantId, reconnected.ParticipantId); Assert.Equal(identity.ShortCode, reconnected.ShortCode); Assert.Equal(identity.Color, reconnected.Color);
    }
    [Fact]
    public void AC027_hidden_join_focus_and_leave_do_not_change_public_payload_or_revision()
    {
        var registry = new PresenceRegistry(new Clock(), TimeSpan.FromSeconds(30));
        var admin = new CurrentSessionDto(Guid.NewGuid(), "Admin", true);
        var before = JsonSerializer.Serialize(registry.Snapshot().Snapshot);
        registry.Touch("secret", admin, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Editing", "secret target"));
        Assert.False(registry.Snapshot().Changed); Assert.Equal(before, JsonSerializer.Serialize(registry.Snapshot().Snapshot));
        registry.Remove("secret"); Assert.Equal(before, JsonSerializer.Serialize(registry.Snapshot().Snapshot));
    }
    [Theory]
    [InlineData(" Admin ", true)] [InlineData("admin", false)] [InlineData("ADMIN", false)] [InlineData("Admin2", false)]
    public async Task AC025_026_028_current_database_authority_overrides_old_cookie_after_rename(string name, bool admin)
    {
        using var client = factory.CreateClient(); var original = await Login(client, "Admin");
        (await client.GetAsync("/api/audit")).EnsureSuccessStatusCode();
        var renamed = await client.PutAsJsonAsync("/api/session/nickname", new { nickname = name }); renamed.EnsureSuccessStatusCode();
        // Rename deliberately does not issue another authentication cookie: this is the original Admin cookie.
        Assert.False(renamed.Headers.TryGetValues("Set-Cookie", out _));
        var current = (await client.GetFromJsonAsync<CurrentSessionDto>("/api/session"))!;
        Assert.Equal(original.ParticipantId, current.ParticipantId); Assert.Equal(name.Trim(), current.Nickname); Assert.Equal(admin, current.CanReadAudit);
        Assert.Equal(admin ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.GetAsync("/api/audit")).StatusCode);
    }
    [Fact]
    public async Task AC027_028_existing_connections_use_renamed_authority_and_hide_all_tabs()
    {
        using var client = factory.CreateClient(); var actor = await Login(client, "可見成員");
        var registry = new PresenceRegistry(new Clock(), TimeSpan.FromSeconds(30)); var notifications = new Notifications();
        await using var db = Db(); var service = new CollaborationService(db, registry, notifications, notifications);
        await service.HeartbeatAsync("one", actor.ParticipantId, new(null, null, null), default);
        await service.HeartbeatAsync("two", actor.ParticipantId, new(null, null, null), default);
        await service.RenameAsync(actor.ParticipantId, new("Admin"), default);
        Assert.Empty((await service.HeartbeatAsync("one", actor.ParticipantId, new(null, null, null), default)).Members);
        Assert.Empty((await service.GetPresenceAsync(default)).Members);
        Assert.DoesNotContain(actor.ParticipantId.ToString(), JsonSerializer.Serialize(notifications.Public.Last()));
        await service.RenameAsync(actor.ParticipantId, new("恢復可見"), default);
        Assert.Equal("恢復可見", Assert.Single((await service.GetPresenceAsync(default)).Members).Nickname);
    }
    [Fact]
    public async Task AC030_expired_editing_presence_does_not_change_occupancy_primary_operator_or_usage()
    {
        using var client = factory.CreateClient(); var actor = await Login(client, "在線測試");
        var clock = new Clock(); var registry = new PresenceRegistry(clock, TimeSpan.FromSeconds(30)); var notifications = new Notifications();
        Guid cardId;
        await using (var setup = Db())
        {
            var account = new GameAccount(Guid.NewGuid(), "M3 虛構帳號"); var card = new CharacterCard(Guid.NewGuid(), account.Id, "M3 占用卡片"); cardId = card.Id;
            setup.Accounts.Add(account); setup.Cards.Add(card); await setup.SaveChangesAsync();
        }
        await using (var db = Db())
        {
            var coordination = new WorkspaceCoordinator(db, notifications);
            await coordination.ReserveAsync(actor, new(cardId, Guid.Parse("20000000-0000-0000-0000-000000000001"), 0), default);
            await coordination.SetUsageAsync(actor, cardId, new(CardUsageStatus.InUse, true), default);
            var before = await coordination.GetSnapshotAsync(actor, default);
            var service = new CollaborationService(db, registry, notifications, notifications);
            await service.HeartbeatAsync("tab", actor.ParticipantId, new(BuiltInCollections.Cards, cardId, null, "Editing"), default);
            clock.Now += TimeSpan.FromSeconds(31); Assert.Empty((await service.GetPresenceAsync(default)).Members);
            var after = await coordination.GetSnapshotAsync(actor, default);
            Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        }
    }
    [Fact]
    public async Task Shared_focus_normalizes_record_and_rejects_other_collection_records()
    {
        using var client = factory.CreateClient(); var actor = await Login(client, "共用編輯者");
        await using var db = Db(); var notifications = new Notifications(); var field = Guid.NewGuid();
        await new ConfigurationService(db, notifications).SaveFieldAsync(actor, field, new(BuiltInCollections.Cards, "共用測試", FieldKind.Text, FieldScope.Shared, [], null, "#ffffff", 0, false, false, 0), default);
        var service = new CollaborationService(db, new PresenceRegistry(new Clock(), TimeSpan.FromSeconds(30)), notifications, notifications);
        var result = await service.HeartbeatAsync("tab", actor.ParticipantId, new(BuiltInCollections.Cards, Guid.Parse("30000000-0000-0000-0000-000000000001"), field, "Editing"), default);
        var target = Assert.Single(Assert.Single(result.Members).Targets); Assert.Null(target.RecordId); Assert.Equal(field, target.FieldId);
        await Assert.ThrowsAsync<DomainRuleException>(() => service.HeartbeatAsync("bad", actor.ParticipantId, new(BuiltInCollections.Accounts, Guid.Parse("30000000-0000-0000-0000-000000000001"), null), default));
    }
    [Fact]
    public async Task AC032_056_notification_failure_does_not_roll_back_committed_business_data()
    {
        using var client = factory.CreateClient(); var actor = await Login(client, "通知中斷測試");
        await using var db = Db(); var hub = new BrokenHub();
        var notifier = new SignalRWorkspaceNotifier(hub, NullLogger<SignalRWorkspaceNotifier>.Instance);
        var name = "已提交 " + Guid.NewGuid();
        var saved = await new WorkspaceCoordinator(db, notifier).CreateAccountAsync(actor, new(name), default);
        await using var verify = Db(); Assert.True(await verify.Accounts.AnyAsync(x => x.DisplayName == name));
        Assert.Equal(saved.Version, await verify.WorkspaceStates.Select(x => x.Version).SingleAsync());
        var presence = new CollaborationNotifier(hub, NullLogger<CollaborationNotifier>.Instance);
        await presence.PresenceChangedAsync(new(Guid.NewGuid(), 1, []), default);
        await presence.SessionChangedAsync(["tab"], default);
    }
    private static async Task<CurrentSessionDto> Login(HttpClient client, string name)
    {
        async Task Token() { client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (await client.GetFromJsonAsync<JsonElement>("/api/session/csrf")).GetProperty("token").GetString()); }
        await Token(); var response = await client.PostAsJsonAsync("/api/session", new { nickname = name }); response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<CurrentSessionDto>())!; await Token(); return session;
    }
    private sealed class BrokenHub : IHubContext<WorkspaceHub>
    {
        public IHubClients Clients => throw new IOException("simulated transport failure");
        public IGroupManager Groups => throw new NotSupportedException();
    }
}
