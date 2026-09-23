using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Workspace.Application;
using Workspace.Infrastructure.Persistence;

public static class SessionAuthority
{
    public static ClaimsPrincipal Principal(ParticipantSession participant) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, participant.Id.ToString()), new Claim(ClaimTypes.Name, participant.Nickname),
        new Claim("can_read_audit", participant.IsAdmin ? "true" : "false")], CookieAuthenticationDefaults.AuthenticationScheme));
    public static async Task Validate(CookieValidatePrincipalContext context)
    {
        if (!Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) { context.RejectPrincipal(); return; }
        var db = context.HttpContext.RequestServices.GetRequiredService<WorkspaceDbContext>();
        var participant = await db.Participants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, context.HttpContext.RequestAborted);
        if (participant == null) { context.RejectPrincipal(); return; }
        // Never use stale nickname/capability claims in a cookie after a rename from another tab.
        context.ReplacePrincipal(Principal(participant));
    }
}
public sealed class WorkspaceHub(ICollaborationService service) : Hub
{
    public Task<PresenceSnapshotDto> Heartbeat(PresenceCommand command) => service.HeartbeatAsync(Context.ConnectionId,
        Guid.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!), command, Context.ConnectionAborted);
    public override async Task OnDisconnectedAsync(Exception? exception) { await service.DisconnectAsync(Context.ConnectionId, CancellationToken.None); await base.OnDisconnectedAsync(exception); }
}
public sealed class CollaborationNotifier(IHubContext<WorkspaceHub> hub, ILogger<CollaborationNotifier> logger) : ICollaborationNotifier
{
    public async Task PresenceChangedAsync(PresenceSnapshotDto snapshot, CancellationToken ct)
    {
        try { await hub.Clients.All.SendAsync("presenceChanged", snapshot, ct); }
        catch (Exception exception) { logger.LogWarning(exception, "在線提示傳送失敗，後續心跳會重新核對。"); }
    }
    public async Task SessionChangedAsync(IReadOnlyList<string> connections, CancellationToken ct)
    {
        try { await hub.Clients.Clients(connections).SendAsync("sessionChanged", ct); }
        catch (Exception exception) { logger.LogWarning(exception, "工作階段提示傳送失敗，後續校對會重新取得權限。"); }
    }
}
public sealed class PresenceExpiryWorker(IServiceScopeFactory scopes, ILogger<PresenceExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<ICollaborationService>().GetPresenceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning(exception, "在線提示清理暫時失敗。"); }
        }
    }
}
