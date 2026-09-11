using System.Data;
using Microsoft.EntityFrameworkCore;
using Workspace.Application;
using Workspace.Domain;
using Workspace.Infrastructure.Persistence;

namespace Workspace.Infrastructure;

public sealed class WorkspaceCoordinator(WorkspaceDbContext db, IWorkspaceNotifier notifier) : IWorkspaceCoordinator
{
    public Task<WorkspaceSnapshotDto> GetSnapshotAsync(CurrentSessionDto session, CancellationToken ct) => ProjectAsync(session, ct);

    public Task<WorkspaceSnapshotDto> ReserveAsync(CurrentSessionDto session, CoordinationCommand command, CancellationToken ct) =>
        MutateAsync(session, command.CardId, command.ExpectedAccountVersion, async (account, card, now) =>
        {
            var region = await db.Regions.SingleOrDefaultAsync(x => x.Id == command.RegionId, ct) ?? throw new DomainRuleException("找不到指定區域。");
            var active = await ActiveAsync(account.Id, ct);
            AccountRegionPolicy.EnsureDestinationAllowed(active, region.Id);
            if (active.Any(x => x.CardId == card.Id)) throw new DomainRuleException("這張卡片已有有效預約或占用。");
            db.Reservations.Add(new ResourceReservation(Guid.NewGuid(), account.Id, card.Id, region.Id, session.ParticipantId, now));
            return $"{session.Nickname} 為「{card.DisplayName}」預約「{region.DisplayName}」。";
        }, ct);

    public Task<WorkspaceSnapshotDto> EnterAsync(CurrentSessionDto session, CoordinationCommand command, CancellationToken ct) =>
        MutateAsync(session, command.CardId, command.ExpectedAccountVersion, async (account, card, now) =>
        {
            var reservation = await db.Reservations.SingleOrDefaultAsync(x => x.CardId == card.Id && x.State == ReservationState.Reserved, ct) ?? throw new DomainRuleException("找不到尚未入場的預約。");
            if (reservation.RegionId != command.RegionId) throw new DomainRuleException("預約目的地已變更，請重新整理。");
            AccountRegionPolicy.EnsureDestinationAllowed(await ActiveAsync(account.Id, ct), command.RegionId);
            reservation.Enter(now);
            return $"{session.Nickname} 回報「{card.DisplayName}」已進入區域。";
        }, ct);

    public Task<WorkspaceSnapshotDto> CancelAsync(CurrentSessionDto session, ReleaseCommand command, CancellationToken ct) =>
        ReleaseAsync(session, command, false, ct);

    public Task<WorkspaceSnapshotDto> ReturnHomeAsync(CurrentSessionDto session, ReleaseCommand command, CancellationToken ct) =>
        ReleaseAsync(session, command, true, ct);

    public Task<WorkspaceSnapshotDto> SetUsageAsync(CurrentSessionDto session, Guid cardId, CardUsageCommand command, CancellationToken ct) =>
        MutateWithoutExpectedAsync(session, cardId, async (account, card, now) =>
        {
            card.SetUsage(command.Status, command.AssignMeAsPrimaryOperator && command.Status == CardUsageStatus.InUse ? session.ParticipantId : null);
            await Task.CompletedTask;
            return $"{session.Nickname} 將「{card.DisplayName}」使用狀態改為「{command.Status}」。";
        }, ct);

    public async Task<WorkspaceSnapshotDto> CreateAccountAsync(CurrentSessionDto session, CreateAccountCommand command, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var account = new GameAccount(Guid.NewGuid(), command.DisplayName); db.Accounts.Add(account);
        var version = await AdvanceWorkspaceAsync(ct);
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), session.ParticipantId, session.Nickname, $"{session.Nickname} 新增帳號「{account.DisplayName}」。", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); await notifier.SnapshotChangedAsync(version, ct);
        return await ProjectAsync(session, ct);
    }

    public async Task<WorkspaceSnapshotDto> CreateCardAsync(CurrentSessionDto session, Guid accountId, CreateCardCommand command, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM preview_accounts WHERE \"Id\" = {accountId} FOR UPDATE", ct);
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == accountId, ct) ?? throw new DomainRuleException("找不到指定帳號。");
        var card = new CharacterCard(Guid.NewGuid(), accountId, command.DisplayName); db.Cards.Add(card); account.AdvanceVersion();
        var version = await AdvanceWorkspaceAsync(ct);
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), session.ParticipantId, session.Nickname, $"{session.Nickname} 在「{account.DisplayName}」新增卡片「{card.DisplayName}」。", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); await notifier.SnapshotChangedAsync(version, ct);
        return await ProjectAsync(session, ct);
    }

    public async Task<IReadOnlyList<AuditEventDto>> GetAuditAsync(CurrentSessionDto session, CancellationToken ct)
    {
        if (!session.CanReadAudit) throw new UnauthorizedAccessException("只有 Admin 可以讀取操作紀錄。");
        return await db.AuditEvents.OrderByDescending(x => x.OccurredAt).Take(100).Select(x => new AuditEventDto(x.Id, x.OccurredAt, x.Description)).ToListAsync(ct);
    }

    private async Task<WorkspaceSnapshotDto> ReleaseAsync(CurrentSessionDto session, ReleaseCommand command, bool returnHome, CancellationToken ct) =>
        await MutateAsync(session, command.CardId, command.ExpectedAccountVersion, async (account, card, now) =>
        {
            var reservation = await db.Reservations.SingleOrDefaultAsync(x => x.CardId == card.Id && x.State != ReservationState.Released, ct) ?? throw new DomainRuleException("這張卡片目前沒有有效預約或占用。");
            if (reservation.Version != command.ExpectedReservationVersion) throw new VersionConflictException();
            if (returnHome) reservation.ReturnHome(now); else reservation.Cancel(now);
            return returnHome ? $"{session.Nickname} 回報「{card.DisplayName}」已回村。" : $"{session.Nickname} 取消「{card.DisplayName}」的預約。";
        }, ct);

    private Task<WorkspaceSnapshotDto> MutateWithoutExpectedAsync(CurrentSessionDto session, Guid cardId, Func<GameAccount, CharacterCard, DateTimeOffset, Task<string>> mutation, CancellationToken ct) =>
        MutateAsync(session, cardId, null, mutation, ct);

    private async Task<WorkspaceSnapshotDto> MutateAsync(CurrentSessionDto session, Guid cardId, long? expectedVersion, Func<GameAccount, CharacterCard, DateTimeOffset, Task<string>> mutation, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var card = await db.Cards.SingleOrDefaultAsync(x => x.Id == cardId, ct) ?? throw new DomainRuleException("找不到指定卡片。");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM preview_accounts WHERE \"Id\" = {card.AccountId} FOR UPDATE", ct);
        var account = await db.Accounts.SingleAsync(x => x.Id == card.AccountId, ct);
        if (expectedVersion.HasValue && account.CoordinationVersion != expectedVersion.Value) throw new VersionConflictException();
        var now = DateTimeOffset.UtcNow;
        var description = await mutation(account, card, now);
        account.AdvanceVersion();
        var version = await AdvanceWorkspaceAsync(ct);
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), session.ParticipantId, session.Nickname, description, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.SnapshotChangedAsync(version, ct);
        return await ProjectAsync(session, ct);
    }

    private Task<List<ResourceReservation>> ActiveAsync(Guid accountId, CancellationToken ct) =>
        db.Reservations.Where(x => x.AccountId == accountId && x.State != ReservationState.Released).ToListAsync(ct);

    private async Task<long> AdvanceWorkspaceAsync(CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM workspace_state WHERE \"Id\" = 1 FOR UPDATE", ct);
        var state = await db.WorkspaceStates.SingleAsync(x => x.Id == 1, ct); state.Advance();
        return state.Version;
    }

    private async Task<WorkspaceSnapshotDto> ProjectAsync(CurrentSessionDto session, CancellationToken ct)
    {
        var version = (await db.WorkspaceStates.AsNoTracking().SingleAsync(x => x.Id == 1, ct)).Version;
        var regions = await db.Regions.AsNoTracking().OrderBy(x => x.DisplayName).Select(x => new RegionDto(x.Id, x.DisplayName)).ToListAsync(ct);
        var participants = await db.Participants.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var active = await db.Reservations.AsNoTracking().Where(x => x.State != ReservationState.Released).ToListAsync(ct);
        var regionNames = regions.ToDictionary(x => x.Id, x => x.DisplayName);
        var cards = await db.Cards.AsNoTracking().OrderBy(x => x.DisplayName).ToListAsync(ct);
        var accounts = await db.Accounts.AsNoTracking().OrderBy(x => x.DisplayName).ToListAsync(ct);
        return new WorkspaceSnapshotDto(version, regions, accounts.Select(account => new AccountDto(account.Id, account.DisplayName, account.CoordinationVersion,
            cards.Where(card => card.AccountId == account.Id).Select(card =>
            {
                var reservation = active.SingleOrDefault(x => x.CardId == card.Id);
                string? operatorName = null;
                if (card.PrimaryOperatorId is Guid operatorId && participants.TryGetValue(operatorId, out var participant) && !participant.IsAdmin) operatorName = participant.Nickname;
                return new CardDto(card.Id, card.AccountId, card.DisplayName, card.UsageStatus, operatorName,
                    reservation is null ? null : new ReservationDto(reservation.Id, reservation.RegionId, regionNames[reservation.RegionId], reservation.State, reservation.Version));
            }).ToList())).ToList());
    }
}
