using Microsoft.EntityFrameworkCore;
using Workspace.Application;
using Workspace.Domain;
using Workspace.Infrastructure.Persistence;

namespace Workspace.Infrastructure;

public sealed class CollaborationService(WorkspaceDbContext db, PresenceRegistry registry, ICollaborationNotifier notifier, IWorkspaceNotifier workspaceNotifier) : ICollaborationService
{
    public async Task<PresenceSnapshotDto> HeartbeatAsync(string connectionId, Guid participantId, PresenceCommand command, CancellationToken ct)
    {
        await registry.Gate.WaitAsync(ct);
        try
        {
            var session = await Session(participantId, ct);
            var target = await Target(command, ct);
            registry.Touch(connectionId, session, target);
            return await Publish(ct);
        }
        finally { registry.Gate.Release(); }
    }
    public async Task DisconnectAsync(string connectionId, CancellationToken ct)
    {
        await registry.Gate.WaitAsync(ct);
        try { registry.Remove(connectionId); await Publish(ct); }
        finally { registry.Gate.Release(); }
    }
    public async Task<PresenceSnapshotDto> GetPresenceAsync(CancellationToken ct)
    {
        await registry.Gate.WaitAsync(ct);
        try { return await Publish(ct); }
        finally { registry.Gate.Release(); }
    }
    public async Task<CurrentSessionDto> RenameAsync(Guid participantId, ChangeNicknameCommand command, CancellationToken ct)
    {
        var name = FieldValuePolicy.Name(command.Nickname);
        await registry.Gate.WaitAsync(ct);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var participant = await db.Participants.SingleOrDefaultAsync(x => x.Id == participantId, ct) ?? throw new UnauthorizedAccessException("工作階段已失效。");
            var old = participant.Nickname; participant.Rename(name);
            await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM workspace_state WHERE \"Id\" = 1 FOR UPDATE", ct);
            var state = await db.WorkspaceStates.SingleAsync(ct); state.Advance();
            db.AuditEvents.Add(new(Guid.NewGuid(), participant.Id, old, $"「{old}」將暱稱改為「{name}」。", DateTimeOffset.UtcNow, participant.Id));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            var session = new CurrentSessionDto(participant.Id, name, participant.IsAdmin);
            var connections = registry.Rename(session);
            await Publish(ct);
            await notifier.SessionChangedAsync(connections, ct);
            await workspaceNotifier.SnapshotChangedAsync(state.Version, ct);
            return session;
        }
        finally { registry.Gate.Release(); }
    }
    private async Task<CurrentSessionDto> Session(Guid id, CancellationToken ct) => await db.Participants.AsNoTracking().Where(x => x.Id == id)
        .Select(x => new CurrentSessionDto(x.Id, x.Nickname, x.Nickname == "Admin")).SingleOrDefaultAsync(ct) ?? throw new UnauthorizedAccessException("工作階段已失效。");
    private async Task<PresenceSnapshotDto> Publish(CancellationToken ct)
    {
        var (snapshot, changed) = registry.Snapshot();
        if (changed) await notifier.PresenceChangedAsync(snapshot, ct);
        return snapshot;
    }
    private async Task<PresenceTargetDto?> Target(PresenceCommand command, CancellationToken ct)
    {
        if (command.Mode is not ("Viewing" or "Editing")) throw new DomainRuleException("不支援此編輯狀態。");
        if (command.CollectionId == null)
        {
            if (command.RecordId != null || command.FieldId != null) throw new DomainRuleException("請指定資料集。");
            return null;
        }
        var collection = await db.Collections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.CollectionId, ct) ?? throw new DomainRuleException("資料集不存在。");
        var label = collection.Name;
        if (command.RecordId is Guid recordId)
        {
            var name = collection.Kind switch
            {
                CollectionKind.Accounts => await db.Accounts.Where(x => x.Id == recordId).Select(x => x.DisplayName).SingleOrDefaultAsync(ct),
                CollectionKind.Cards => await db.Cards.Where(x => x.Id == recordId).Select(x => x.DisplayName).SingleOrDefaultAsync(ct),
                _ => await db.DataRecords.Where(x => x.Id == recordId && x.CollectionId == collection.Id).Select(x => x.Name).SingleOrDefaultAsync(ct)
            };
            if (name == null) throw new DomainRuleException("資料列不屬於指定資料集。");
            label = name;
        }
        Guid? normalizedRecord = command.RecordId;
        if (command.FieldId is Guid fieldId)
        {
            var field = await db.Fields.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fieldId && x.CollectionId == collection.Id && !x.Deleted, ct);
            // A deleted field clears focus, while the browser keeps its unsaved draft.
            if (field == null) return new(collection.Id, command.RecordId, null, "Viewing", label);
            if (field.Scope == FieldScope.Shared) { normalizedRecord = null; label = collection.Name; }
            else if (command.RecordId == null) throw new DomainRuleException("此欄位需要指定資料列。");
            label += "／" + field.Name;
        }
        return new(collection.Id, normalizedRecord, command.FieldId, command.Mode, label);
    }
}
