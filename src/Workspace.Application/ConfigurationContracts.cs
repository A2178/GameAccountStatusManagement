using System.Text.Json;
using Workspace.Domain;

namespace Workspace.Application;

public sealed record FieldDto(Guid Id, Guid CollectionId, string Name, FieldKind Kind, FieldScope Scope, FieldBinding Binding, FieldOption[] Options, Guid? RelationCollectionId, string Color, int Position, bool Required, bool Hidden, long Version);
public sealed record ValueDto(Guid FieldId, Guid? RecordId, JsonElement Value, bool Attached, long Version);
public sealed record RecordDto(Guid Id, string Name, Guid? AccountId, Guid? StageId, long Version);
public sealed record CollectionSnapshotDto(CollectionDefinition Collection, IReadOnlyList<RecordDto> Records, IReadOnlyList<FieldDto> Fields, IReadOnlyList<ValueDto> Values);
public sealed record ViewDto(Guid Id, Guid CollectionId, string Name, string Display, int Position, Guid? StageId, Guid[] HiddenFields, Guid? SortFieldId, bool Descending, Guid? FilterFieldId, string FilterText, long Version);
public sealed record ConfigurationDto(WorkspaceSettings Settings, IReadOnlyList<CollectionDefinition> Collections, IReadOnlyList<ViewDto> Views, IReadOnlyList<StageDefinition> Stages, IReadOnlyList<NamedTargetDto> Regions);
public sealed record NamedTargetDto(Guid Id, string Name, long Version);
public sealed record SaveFieldCommand(Guid CollectionId, string Name, FieldKind Kind, FieldScope Scope, FieldOption[] Options, Guid? RelationCollectionId, string Color, int Position, bool Required, bool Hidden, long ExpectedVersion);
public sealed record SaveValueCommand(JsonElement Value, long ExpectedVersion, long ExpectedDefinitionVersion, bool Attached = true);
public sealed record SaveViewCommand(Guid CollectionId, string Name, string Display, int Position, Guid? StageId, Guid[] HiddenFields, Guid? SortFieldId, bool Descending, Guid? FilterFieldId, string FilterText, long ExpectedVersion);
public sealed record RenameCommand(string Name, long ExpectedVersion);
public sealed record SaveSettingsCommand(string Name, string CardLabel, string SettingsLabel, long ExpectedVersion);
public sealed record MoveStageCommand(Guid StageId, long ExpectedVersion);
public sealed record VersionCommand(long ExpectedVersion);
public sealed record CreateCollectionCommand(string Name);

public interface IConfigurationService
{
    Task<ConfigurationDto> GetAsync(CancellationToken ct);
    Task<CollectionSnapshotDto> GetCollectionAsync(Guid id, CancellationToken ct);
    Task SaveFieldAsync(CurrentSessionDto actor, Guid id, SaveFieldCommand command, CancellationToken ct);
    Task DeleteFieldAsync(CurrentSessionDto actor, Guid id, long expectedVersion, CancellationToken ct);
    Task<ValueDto> SaveValueAsync(CurrentSessionDto actor, Guid fieldId, Guid? recordId, SaveValueCommand command, CancellationToken ct);
    Task SaveViewAsync(CurrentSessionDto actor, Guid id, SaveViewCommand command, CancellationToken ct);
    Task CreateCollectionAsync(CurrentSessionDto actor, CreateCollectionCommand command, CancellationToken ct);
    Task CreateRecordAsync(CurrentSessionDto actor, Guid collectionId, CreateCollectionCommand command, CancellationToken ct);
    Task RenameAsync(CurrentSessionDto actor, string target, Guid id, RenameCommand command, CancellationToken ct);
    Task SaveSettingsAsync(CurrentSessionDto actor, SaveSettingsCommand command, CancellationToken ct);
    Task MoveStageAsync(CurrentSessionDto actor, Guid cardId, MoveStageCommand command, CancellationToken ct);
}

public sealed class FieldConflictException(string message, ValueDto? current = null) : Exception(message)
{
    public ValueDto? Current { get; } = current;
}
