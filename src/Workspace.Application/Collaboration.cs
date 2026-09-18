using System.Text.Json;

namespace Workspace.Application;

public sealed record PresenceCommand(Guid? CollectionId, Guid? RecordId, Guid? FieldId, string Mode = "Viewing");
public sealed record PresenceTargetDto(Guid CollectionId, Guid? RecordId, Guid? FieldId, string Mode, string Label);
public sealed record PresenceMemberDto(Guid ParticipantId, string Nickname, string ShortCode, string Color, IReadOnlyList<PresenceTargetDto> Targets);
public sealed record PresenceSnapshotDto(Guid Epoch, long Version, IReadOnlyList<PresenceMemberDto> Members);
public sealed record CollaborationSettingsDto(int HeartbeatSeconds, int ReconcileSeconds);
public sealed record ChangeNicknameCommand(string Nickname);
public sealed record WorkspaceVersionDto(long Version);
public interface ICollaborationService
{
    Task<PresenceSnapshotDto> HeartbeatAsync(string connectionId, Guid participantId, PresenceCommand command, CancellationToken ct);
    Task DisconnectAsync(string connectionId, CancellationToken ct);
    Task<PresenceSnapshotDto> GetPresenceAsync(CancellationToken ct);
    Task<CurrentSessionDto> RenameAsync(Guid participantId, ChangeNicknameCommand command, CancellationToken ct);
}
public interface ICollaborationNotifier
{
    Task PresenceChangedAsync(PresenceSnapshotDto snapshot, CancellationToken ct);
    Task SessionChangedAsync(IReadOnlyList<string> connections, CancellationToken ct);
}

// One registry per server instance. Only projected, ordinary members affect its public revision.
// All callers hold Gate across authoritative session lookup and projection/publication.
public sealed class PresenceRegistry(TimeProvider clock, TimeSpan lifetime)
{
    private sealed record Connection(CurrentSessionDto Session, PresenceTargetDto? Target, DateTimeOffset Seen);
    private readonly Dictionary<string, Connection> connections = [];
    private readonly Guid epoch = Guid.NewGuid();
    private long version;
    private string fingerprint = "[]";
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public void Touch(string connectionId, CurrentSessionDto session, PresenceTargetDto? target) => connections[connectionId] = new(session, target, clock.GetUtcNow());
    public void Remove(string connectionId) => connections.Remove(connectionId);
    public IReadOnlyList<string> Rename(CurrentSessionDto session)
    {
        var keys = connections.Where(x => x.Value.Session.ParticipantId == session.ParticipantId).Select(x => x.Key).ToArray();
        foreach (var key in keys) connections[key] = connections[key] with { Session = session, Target = null };
        return keys;
    }
    public (PresenceSnapshotDto Snapshot, bool Changed) Snapshot()
    {
        var now = clock.GetUtcNow();
        foreach (var key in connections.Where(x => now - x.Value.Seen >= lifetime).Select(x => x.Key).ToArray()) connections.Remove(key);
        var members = connections.Values.Where(x => !x.Session.CanReadAudit)
            .GroupBy(x => x.Session.ParticipantId).OrderBy(x => x.Key).Select(group =>
            {
                var session = group.First().Session; var id = session.ParticipantId.ToString("N");
                string[] colors = ["#67e8f9", "#c4b5fd", "#fcd34d", "#6ee7b7", "#f9a8d4", "#fdba74"];
                var targets = group.Where(x => x.Target != null).Select(x => x.Target!).Distinct().OrderBy(x => x.CollectionId).ThenBy(x => x.RecordId).ThenBy(x => x.FieldId).ThenBy(x => x.Mode).ToArray();
                return new PresenceMemberDto(session.ParticipantId, session.Nickname, id[..6], colors[Convert.ToInt32(id[..2], 16) % colors.Length], targets);
            }).ToArray();
        var next = JsonSerializer.Serialize(members); var changed = next != fingerprint;
        if (changed) { fingerprint = next; version++; }
        return (new(epoch, version, members), changed);
    }
}
