using Workspace.Domain;

namespace Workspace.Application;

public sealed record CurrentSessionDto(Guid ParticipantId, string Nickname, bool CanReadAudit);
public sealed record RegionDto(Guid Id, string DisplayName);
public sealed record ReservationDto(Guid Id, Guid RegionId, string RegionName, ReservationState State, long Version);
public sealed record CardDto(Guid Id, Guid AccountId, string DisplayName, CardUsageStatus UsageStatus, string? PrimaryOperatorName, ReservationDto? Reservation);
public sealed record AccountDto(Guid Id, string DisplayName, long CoordinationVersion, IReadOnlyList<CardDto> Cards);
public sealed record WorkspaceSnapshotDto(long Version, IReadOnlyList<RegionDto> Regions, IReadOnlyList<AccountDto> Accounts);
public sealed record AuditEventDto(Guid Id, DateTimeOffset OccurredAt, string Description);
public sealed record CoordinationCommand(Guid CardId, Guid RegionId, long ExpectedAccountVersion);
public sealed record ReleaseCommand(Guid CardId, long ExpectedAccountVersion, long ExpectedReservationVersion);
public sealed record CardUsageCommand(CardUsageStatus Status, bool AssignMeAsPrimaryOperator);
public sealed record CreateAccountCommand(string DisplayName);
public sealed record CreateCardCommand(string DisplayName);

public interface IWorkspaceCoordinator
{
    Task<WorkspaceSnapshotDto> GetSnapshotAsync(CurrentSessionDto session, CancellationToken cancellationToken);
    Task<WorkspaceSnapshotDto> ReserveAsync(CurrentSessionDto session, CoordinationCommand command, CancellationToken cancellationToken);
    Task<WorkspaceSnapshotDto> EnterAsync(CurrentSessionDto session, CoordinationCommand command, CancellationToken cancellationToken);
    Task<WorkspaceSnapshotDto> CancelAsync(CurrentSessionDto session, ReleaseCommand command, CancellationToken cancellationToken);
    Task<WorkspaceSnapshotDto> ReturnHomeAsync(CurrentSessionDto session, ReleaseCommand command, CancellationToken cancellationToken);
    Task<WorkspaceSnapshotDto> SetUsageAsync(CurrentSessionDto session, Guid cardId, CardUsageCommand command, CancellationToken cancellationToken);
    Task<WorkspaceSnapshotDto> CreateAccountAsync(CurrentSessionDto session, CreateAccountCommand command, CancellationToken cancellationToken);
    Task<WorkspaceSnapshotDto> CreateCardAsync(CurrentSessionDto session, Guid accountId, CreateCardCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditEventDto>> GetAuditAsync(CurrentSessionDto session, CancellationToken cancellationToken);
}

public interface IWorkspaceNotifier
{
    Task SnapshotChangedAsync(long version, CancellationToken cancellationToken);
}

public sealed class VersionConflictException : Exception
{
    public VersionConflictException() : base("資料已由其他人更新，畫面已重新整理，請確認後再操作。") { }
}
