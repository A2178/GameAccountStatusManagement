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
    options.Events.OnValidatePrincipal = SessionAuthority.Validate;
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddSignalR();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks();
builder.Services.AddDbContext<WorkspaceDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("Workspace")));
builder.Services.AddScoped<IWorkspaceCoordinator, WorkspaceCoordinator>();
builder.Services.AddScoped<IConfigurationService, ConfigurationService>();
builder.Services.AddScoped<IProgressionService, ProgressionService>();
builder.Services.AddSingleton<IWorkspaceNotifier, SignalRWorkspaceNotifier>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new PresenceRegistry(sp.GetRequiredService<TimeProvider>(), TimeSpan.FromSeconds(Math.Clamp(builder.Configuration.GetValue("Collaboration:PresenceTimeoutSeconds", 30), 3, 300))));
builder.Services.AddScoped<ICollaborationService, CollaborationService>();
builder.Services.AddSingleton<ICollaborationNotifier, CollaborationNotifier>();
builder.Services.AddHostedService<PresenceExpiryWorker>();

var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (FieldConflictException exception) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { message = exception.Message, current = exception.Current }); }
    catch (VersionConflictException exception) { await ErrorAsync(context, 409, exception.Message); }
    catch (DomainRuleException exception) { await ErrorAsync(context, 409, exception.Message); }
    catch (UnauthorizedAccessException exception) { await ErrorAsync(context, 403, exception.Message); }
    catch (AntiforgeryValidationException) { await ErrorAsync(context, 400, "安全驗證已失效，請重新整理後再試一次。"); }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") &&
        (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method) ||
         HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method)))
    {
        await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
    }
    await next(context);
});

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
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, SessionAuthority.Principal(participant));
    return Results.Ok(new CurrentSessionDto(participant.Id, participant.Nickname, participant.IsAdmin));
});

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/progression", (IProgressionService service, CancellationToken ct) => service.GetAsync(ct));
api.MapPost("/cards/{cardId:guid}/progression", (Guid cardId, ProgressionCommand command, ClaimsPrincipal user, IProgressionService service, CancellationToken ct) => service.ExecuteAsync(ToSession(user), cardId, command, ct));
api.MapPut("/progression/profiles/{id:guid}", async (Guid id, SaveProfileCommand command, ClaimsPrincipal user, IProgressionService service, CancellationToken ct) => { await service.SaveProfileAsync(ToSession(user), id, command, ct); return Results.Ok(new { saved = true }); });
api.MapPut("/progression/settings", async (SaveProgressionSettingsCommand command, ClaimsPrincipal user, IProgressionService service, CancellationToken ct) => { await service.SaveSettingsAsync(ToSession(user), command, ct); return Results.Ok(new { saved = true }); });
api.MapPut("/progression/stages/{id:guid}", async (Guid id, SaveTransitionCommand command, ClaimsPrincipal user, IProgressionService service, CancellationToken ct) => { await service.SaveTransitionAsync(ToSession(user), id, command, ct); return Results.Ok(new { saved = true }); });
api.MapPut("/session/nickname", (ChangeNicknameCommand command, ClaimsPrincipal user, ICollaborationService service, CancellationToken ct) => service.RenameAsync(ToSession(user).ParticipantId, command, ct));
api.MapGet("/presence", (ICollaborationService service, CancellationToken ct) => service.GetPresenceAsync(ct));
api.MapGet("/collaboration/settings", () => new CollaborationSettingsDto(Math.Clamp(builder.Configuration.GetValue("Collaboration:HeartbeatSeconds", 10), 1, 60), Math.Clamp(builder.Configuration.GetValue("Collaboration:ReconcileSeconds", 15), 1, 60)));
api.MapGet("/version", async (WorkspaceDbContext db, CancellationToken ct) => new WorkspaceVersionDto(await db.WorkspaceStates.AsNoTracking().Select(x => x.Version).SingleAsync(ct)));
api.MapGet("/snapshot", (ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.GetSnapshotAsync(ToSession(user), ct));
api.MapPost("/cards/{cardId:guid}/reserve", (Guid cardId, CoordinationCommand command, ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.ReserveAsync(ToSession(user), command with { CardId = cardId }, ct));
api.MapPost("/cards/{cardId:guid}/enter", (Guid cardId, CoordinationCommand command, ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.EnterAsync(ToSession(user), command with { CardId = cardId }, ct));
api.MapPost("/cards/{cardId:guid}/cancel", (Guid cardId, ReleaseCommand command, ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.CancelAsync(ToSession(user), command with { CardId = cardId }, ct));
api.MapPost("/cards/{cardId:guid}/return-home", (Guid cardId, ReleaseCommand command, ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.ReturnHomeAsync(ToSession(user), command with { CardId = cardId }, ct));
api.MapPut("/cards/{cardId:guid}/usage", (Guid cardId, CardUsageCommand command, ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.SetUsageAsync(ToSession(user), cardId, command, ct));
api.MapPost("/accounts", (CreateAccountCommand command, ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.CreateAccountAsync(ToSession(user), command, ct));
api.MapPost("/accounts/{accountId:guid}/cards", (Guid accountId, CreateCardCommand command, ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.CreateCardAsync(ToSession(user), accountId, command, ct));
api.MapGet("/audit", (ClaimsPrincipal user, IWorkspaceCoordinator service, CancellationToken ct) => service.GetAuditAsync(ToSession(user), ct));
api.MapGet("/configuration", (IConfigurationService service, CancellationToken ct) => service.GetAsync(ct));
api.MapGet("/collections/{id:guid}", (Guid id, IConfigurationService service, CancellationToken ct) => service.GetCollectionAsync(id, ct));
api.MapPost("/collections", async (CreateCollectionCommand command, ClaimsPrincipal user, IConfigurationService service, CancellationToken ct) => { await service.CreateCollectionAsync(ToSession(user), command, ct); return Results.Ok(new { saved = true }); });
api.MapPost("/collections/{id:guid}/records", async (Guid id, CreateCollectionCommand command, ClaimsPrincipal user, IConfigurationService service, CancellationToken ct) => { await service.CreateRecordAsync(ToSession(user), id, command, ct); return Results.Ok(new { saved = true }); });
api.MapPut("/fields/{id:guid}", async (Guid id, SaveFieldCommand command, ClaimsPrincipal user, IConfigurationService service, CancellationToken ct) => { await service.SaveFieldAsync(ToSession(user), id, command, ct); return Results.Ok(new { saved = true }); });
api.MapPost("/fields/{id:guid}/delete", async (Guid id, VersionCommand command, ClaimsPrincipal user, IConfigurationService service, CancellationToken ct) => { await service.DeleteFieldAsync(ToSession(user), id, command.ExpectedVersion, ct); return Results.Ok(new { saved = true }); });
api.MapPut("/fields/{id:guid}/records/{recordId:guid}", (Guid id, Guid recordId, SaveValueCommand command, ClaimsPrincipal user, IConfigurationService service, CancellationToken ct) => service.SaveValueAsync(ToSession(user), id, recordId, command, ct));
api.MapPut("/fields/{id:guid}/shared", (Guid id, SaveValueCommand command, ClaimsPrincipal user, IConfigurationService service, CancellationToken ct) => service.SaveValueAsync(ToSession(user), id, null, command, ct));
api.MapPut("/views/{id:guid}", async (Guid id, SaveViewCommand command, ClaimsPrincipal user, IConfigurationService service, CancellationToken ct) => { await service.SaveViewAsync(ToSession(user), id, command, ct); return Results.Ok(new { saved = true }); });
api.MapPut("/names/{target}/{id:guid}", async (string target, Guid id, RenameCommand command, ClaimsPrincipal user, IConfigurationService service, CancellationToken ct) => { await service.RenameAsync(ToSession(user), target, id, command, ct); return Results.Ok(new { saved = true }); });
api.MapPut("/settings", async (SaveSettingsCommand command, ClaimsPrincipal user, IConfigurationService service, CancellationToken ct) => { await service.SaveSettingsAsync(ToSession(user), command, ct); return Results.Ok(new { saved = true }); });
api.MapPut("/cards/{id:guid}/stage", async (Guid id, MoveStageCommand command, ClaimsPrincipal user, IConfigurationService service, CancellationToken ct) => { await service.MoveStageAsync(ToSession(user), id, command, ct); return Results.Ok(new { saved = true }); });

app.MapHub<WorkspaceHub>("/hubs/workspace").RequireAuthorization();
app.MapHealthChecks("/health");
app.UseDefaultFiles(); app.UseStaticFiles(); app.MapFallbackToFile("index.html");
app.Run();

static CurrentSessionDto ToSession(ClaimsPrincipal user) => new(Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), user.Identity!.Name!, user.FindFirstValue("can_read_audit") == "true");
static async Task ErrorAsync(HttpContext context, int status, string message) { context.Response.StatusCode = status; await context.Response.WriteAsJsonAsync(new { message }); }

public sealed record NicknameRequest(string Nickname);
public sealed class SignalRWorkspaceNotifier(IHubContext<WorkspaceHub> hub, ILogger<SignalRWorkspaceNotifier> logger) : IWorkspaceNotifier
{
    public async Task SnapshotChangedAsync(long version, CancellationToken ct)
    {
        try { await hub.Clients.All.SendAsync("snapshotChanged", version, ct); }
        catch (Exception exception) { logger.LogError(exception, "資料已提交，但即時通知失敗；客戶端將以快照校對恢復。"); }
    }
}
public partial class Program;
