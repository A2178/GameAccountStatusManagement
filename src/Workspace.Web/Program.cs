using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Workspace.Application;
using Workspace.Domain;
using Workspace.Infrastructure;
using Workspace.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "workspace.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddSignalR();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks();
builder.Services.AddDbContext<WorkspaceDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("Workspace")));
builder.Services.AddScoped<IWorkspaceCoordinator, WorkspaceCoordinator>();
builder.Services.AddSingleton<IWorkspaceNotifier, SignalRWorkspaceNotifier>();

var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (VersionConflictException exception) { await ErrorAsync(context, 409, exception.Message); }
    catch (DomainRuleException exception) { await ErrorAsync(context, 409, exception.Message); }
    catch (UnauthorizedAccessException exception) { await ErrorAsync(context, 403, exception.Message); }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/api/session/csrf", (IAntiforgery antiforgery, HttpContext context) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { token = tokens.RequestToken });
});
app.MapGet("/api/session", (ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true ? Results.Ok(ToSession(user)) : Results.Unauthorized());
app.MapPost("/api/session", async (NicknameRequest request, WorkspaceDbContext db, HttpContext context, CancellationToken ct) =>
{
    var nickname = request.Nickname?.Trim() ?? string.Empty;
    if (nickname.Length is < 1 or > 80) return Results.BadRequest(new { message = "暱稱長度必須為 1 到 80 個字元。" });
    var participant = new ParticipantSession(Guid.NewGuid(), Guid.NewGuid(), nickname, DateTimeOffset.UtcNow);
    db.Participants.Add(participant); await db.SaveChangesAsync(ct);
    var claims = new[] { new Claim(ClaimTypes.NameIdentifier, participant.Id.ToString()), new Claim(ClaimTypes.Name, participant.Nickname), new Claim("can_read_audit", participant.IsAdmin ? "true" : "false") };
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok(new CurrentSessionDto(participant.Id, participant.Nickname, participant.IsAdmin));
}).RequireAntiforgery();

var api = app.MapGroup("/api").RequireAuthorization().RequireAntiforgery();
api.MapGet("/snapshot", (ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.GetSnapshotAsync(ToSession(user), ct));
api.MapPost("/cards/{cardId:guid}/reserve", (Guid cardId, CoordinationCommand command, ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.ReserveAsync(ToSession(user), command with { CardId = cardId }, ct));
api.MapPost("/cards/{cardId:guid}/enter", (Guid cardId, CoordinationCommand command, ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.EnterAsync(ToSession(user), command with { CardId = cardId }, ct));
api.MapPost("/cards/{cardId:guid}/cancel", (Guid cardId, ReleaseCommand command, ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.CancelAsync(ToSession(user), command with { CardId = cardId }, ct));
api.MapPost("/cards/{cardId:guid}/return-home", (Guid cardId, ReleaseCommand command, ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.ReturnHomeAsync(ToSession(user), command with { CardId = cardId }, ct));
api.MapPut("/cards/{cardId:guid}/usage", (Guid cardId, CardUsageCommand command, ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.SetUsageAsync(ToSession(user), cardId, command, ct));
api.MapPost("/accounts", (CreateAccountCommand command, ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.CreateAccountAsync(ToSession(user), command, ct));
api.MapPost("/accounts/{accountId:guid}/cards", (Guid accountId, CreateCardCommand command, ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.CreateCardAsync(ToSession(user), accountId, command, ct));
api.MapGet("/audit", (ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.GetAuditAsync(ToSession(user), ct));

app.MapHub<WorkspaceHub>("/hubs/workspace").RequireAuthorization();
app.MapHealthChecks("/health");
app.UseDefaultFiles(); app.UseStaticFiles(); app.MapFallbackToFile("index.html");
app.Run();

static CurrentSessionDto ToSession(ClaimsPrincipal user) => new(Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), user.Identity!.Name!, user.FindFirstValue("can_read_audit") == "true");
static async Task ErrorAsync(HttpContext context, int status, string message) { context.Response.StatusCode = status; await context.Response.WriteAsJsonAsync(new { message }); }

public sealed record NicknameRequest(string Nickname);
public sealed class WorkspaceHub : Hub;
public sealed class SignalRWorkspaceNotifier(IHubContext<WorkspaceHub> hub, ILogger<SignalRWorkspaceNotifier> logger) : IWorkspaceNotifier
{
    public async Task SnapshotChangedAsync(long version, CancellationToken ct)
    {
        try { await hub.Clients.All.SendAsync("snapshotChanged", version, ct); }
        catch (Exception exception) { logger.LogError(exception, "資料已提交，但即時通知失敗；客戶端將以快照校對恢復。"); }
    }
}
public partial class Program;
