using Microsoft.EntityFrameworkCore;
using Workspace.Application;
using Workspace.Infrastructure;
using Workspace.Infrastructure.Persistence;
using Workspace.Domain;

namespace Workspace.IntegrationTests;

[Collection("PostgreSQL coordination")]
public sealed class PostgreSqlCoordinationTests
{
    private const string AccountIdText = "10000000-0000-0000-0000-000000000001";
    private static readonly Guid CardA = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid CardB = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid RegionA = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid RegionB = Guid.Parse("20000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Concurrent_different_regions_for_one_account_cannot_both_succeed()
    {
        await ResetAsync();
        await using var dbA = CreateDb(); await using var dbB = CreateDb();
        var actorA = new CurrentSessionDto(Guid.NewGuid(), "小明", false);
        var actorB = new CurrentSessionDto(Guid.NewGuid(), "小林", false);
        var serviceA = new WorkspaceCoordinator(dbA, new NullNotifier());
        var serviceB = new WorkspaceCoordinator(dbB, new NullNotifier());
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attemptA = Task.Run(async () => { await gate.Task; return await CaptureAsync(() => serviceA.ReserveAsync(actorA, new(CardA, RegionA, 0), default)); });
        var attemptB = Task.Run(async () => { await gate.Task; return await CaptureAsync(() => serviceB.ReserveAsync(actorB, new(CardB, RegionB, 0), default)); });
        gate.SetResult();
        var results = await Task.WhenAll(attemptA, attemptB);
        Console.WriteLine("AC-008 concurrent results: {0} / {1}", results[0]?.GetType().Name ?? "Success", results[1]?.GetType().Name ?? "Success");

        Assert.Single(results.Where(result => result is null));
        Assert.Single(results.Where(result => result is VersionConflictException));
        await using var verification = CreateDb();
        var regions = await verification.Reservations.Where(x => x.State != ReservationState.Released).Select(x => x.RegionId).Distinct().ToListAsync();
        Console.WriteLine("AC-008 active distinct region count: {0}; region: {1}", regions.Count, regions.Single());
        Assert.Single(regions);
    }

    [Fact]
    public async Task Admin_is_constrained_but_hidden_and_stale_release_cannot_clear_reservation()
    {
        await ResetAsync();
        var adminId = Guid.NewGuid();
        await using (var setup = CreateDb())
        {
            setup.Participants.Add(new ParticipantSession(adminId, Guid.NewGuid(), "Admin", DateTimeOffset.UtcNow));
            await setup.SaveChangesAsync();
        }
        var admin = new CurrentSessionDto(adminId, "Admin", true);
        var ordinary = new CurrentSessionDto(Guid.NewGuid(), "小明", false);
        await using var db = CreateDb(); var service = new WorkspaceCoordinator(db, new NullNotifier());

        var reserved = await service.ReserveAsync(admin, new(CardA, RegionA, 0), default);
        var reservationVersion = reserved.Accounts.Single(x => x.Id == Guid.Parse(AccountIdText)).Cards.Single(x => x.Id == CardA).Reservation!.Version;
        var inUse = await service.SetUsageAsync(admin, CardA, new(CardUsageStatus.InUse, true), default);
        var account = inUse.Accounts.Single(x => x.Id == Guid.Parse(AccountIdText));
        Assert.Null(account.Cards.Single(x => x.Id == CardA).PrimaryOperatorName);
        await Assert.ThrowsAsync<DomainRuleException>(() => service.ReserveAsync(ordinary, new(CardB, RegionB, account.CoordinationVersion), default));
        await Assert.ThrowsAsync<VersionConflictException>(() => service.CancelAsync(admin, new(CardA, 1, reservationVersion), default));
        var current = await service.GetSnapshotAsync(ordinary, default);
        Assert.NotNull(current.Accounts.Single(x => x.Id == Guid.Parse(AccountIdText)).Cards.Single(x => x.Id == CardA).Reservation);
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action) { try { await action(); return null; } catch (Exception exception) { return exception; } }
    private static WorkspaceDbContext CreateDb() => new(new DbContextOptionsBuilder<WorkspaceDbContext>().UseNpgsql(ConnectionString()).Options);
    private static string ConnectionString() => Environment.GetEnvironmentVariable("ConnectionStrings__Workspace") ?? "Host=localhost;Database=workspace_dev;Username=workspace;Password=workspace_dev_only";
    private static async Task ResetAsync()
    {
        await using var db = CreateDb();
        await db.Reservations.ExecuteDeleteAsync(); await db.AuditEvents.ExecuteDeleteAsync();
        await db.Accounts.Where(x => x.Id == Guid.Parse(AccountIdText)).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CoordinationVersion, 0));
        await db.WorkspaceStates.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, 0));
    }
    private sealed class NullNotifier : IWorkspaceNotifier { public Task SnapshotChangedAsync(long version, CancellationToken cancellationToken) => Task.CompletedTask; }
}

[CollectionDefinition("PostgreSQL coordination", DisableParallelization = true)]
public sealed class PostgreSqlCoordinationCollection;
