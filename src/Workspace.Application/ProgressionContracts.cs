using Workspace.Domain;

namespace Workspace.Application;

public enum ProgressionOperation { ReportProgress, StartActivity, CorrectActivity, EndActivity, Requalify, Disqualify, InvalidateQualification, RecordIncome, RecordConversion, CorrectCredits, Archive, CreateReplacement }
public sealed record ProgressionCommand(Guid RequestId, ProgressionOperation Operation, long ExpectedVersion = 0,
    Guid? ProfileId = null, long Level = 0, long TaskItems = 0, long MeritBalance = 0, long Amount = 0,
    long ExpectedCreditVersion = 0, Guid? ActivityId = null, long ExpectedActivityVersion = 0,
    string Channel = "", DateTimeOffset? OccurredAt = null, string Reason = "", string ReplacementName = "");
public sealed record SaveProfileCommand(string Name, long LevelTarget, long TaskItemTarget, long MeritTarget, long ExpectedVersion);
public sealed record SaveProgressionSettingsCommand(string TimeZoneId, bool ConversionRequiresActiveActivity, Guid AccumulationStageId, long ExpectedVersion);
public sealed record SaveTransitionCommand(StageRequirement Requirement, Guid[] AllowedFromStageIds, long ExpectedVersion);
public sealed record QualificationDto(Guid Id, DateTimeOffset QualifiedAt, DateTimeOffset EligibleFrom, string TimeZoneId, int ResetHour,
    ProgressionProfile ThresholdSnapshot, DateTimeOffset? ActivatedAt, DateTimeOffset? InvalidatedAt, string InvalidationReason);
public sealed record ActivityDto(Guid Id, Guid QualificationCycleId, string Channel, DateTimeOffset OccurredAt, DateTimeOffset RecordedAt,
    DateTimeOffset? EndedAt, string? OperatorName, long Version);
public sealed record CreditEntryDto(Guid Id, CreditKind Kind, long Amount, long TotalAfter, string Reason, DateTimeOffset RecordedAt);
public sealed record CardProgressionDto(Guid CardId, string Name, Guid AccountId, Guid? StageId, Guid? ReplacesCardId, DateTimeOffset? ArchivedAt,
    Guid? ProfileId, long Level, long TaskItems, long MeritBalance, long CumulativeCredits, long Version, long CreditVersion,
    bool PermanentlyDisqualified, string QualificationStatus, IReadOnlyList<QualificationDto> Cycles, IReadOnlyList<ActivityDto> Activities, IReadOnlyList<CreditEntryDto> Entries);
public sealed record ProgressionSnapshotDto(long Version, DateTimeOffset ServerTime, ProgressionSettings Settings, IReadOnlyList<ProgressionProfile> Profiles, IReadOnlyList<StageDefinition> Stages,
    IReadOnlyList<CardProgressionDto> Cards);
public sealed record ProgressionResultDto(Guid RequestId, long WorkspaceVersion, long CardVersion, long CreditVersion, long MeritBalance,
    long CumulativeCredits, Guid? ActivityId, Guid? ReplacementCardId);
public interface IProgressionService
{
    Task<ProgressionSnapshotDto> GetAsync(CancellationToken ct);
    Task<ProgressionResultDto> ExecuteAsync(CurrentSessionDto actor, Guid cardId, ProgressionCommand command, CancellationToken ct);
    Task SaveProfileAsync(CurrentSessionDto actor, Guid id, SaveProfileCommand command, CancellationToken ct);
    Task SaveSettingsAsync(CurrentSessionDto actor, SaveProgressionSettingsCommand command, CancellationToken ct);
    Task SaveTransitionAsync(CurrentSessionDto actor, Guid stageId, SaveTransitionCommand command, CancellationToken ct);
}
