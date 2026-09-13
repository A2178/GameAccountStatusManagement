using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Workspace.Infrastructure.Persistence;

namespace Workspace.IntegrationTests;

[Collection("PostgreSQL coordination")]
public sealed class ApiCoordinationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ApiCoordinationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Direct_API_cannot_reserve_different_regions_for_the_same_account()
    {
        await ResetAsync();
        using var first = _factory.CreateClient(); using var second = _factory.CreateClient();
        var firstCsrf = await SignInAsync(first, "小明"); var secondCsrf = await SignInAsync(second, "Admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await first.GetAsync("/api/audit")).StatusCode);
        (await second.GetAsync("/api/audit")).EnsureSuccessStatusCode();
        var firstResponse = await PostAsync(first, "/api/cards/30000000-0000-0000-0000-000000000001/reserve", firstCsrf,
            new { cardId = Guid.NewGuid(), regionId = Guid.Parse("20000000-0000-0000-0000-000000000001"), expectedAccountVersion = 0 });
        firstResponse.EnsureSuccessStatusCode();
        var secondResponse = await PostAsync(second, "/api/cards/30000000-0000-0000-0000-000000000002/reserve", secondCsrf,
            new { cardId = Guid.NewGuid(), regionId = Guid.Parse("20000000-0000-0000-0000-000000000002"), expectedAccountVersion = 1 });
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        var body = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("其他區域", body.GetProperty("message").GetString() ?? string.Empty);
    }

    private static async Task<string> SignInAsync(HttpClient client, string nickname)
    {
        var anonymousToken = await TokenAsync(client);
        var response = await PostAsync(client, "/api/session", anonymousToken, new { nickname }); response.EnsureSuccessStatusCode();
        return await TokenAsync(client);
    }
    private static async Task<string> TokenAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/session/csrf")).GetProperty("token").GetString()!;
    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string csrf, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf); return client.SendAsync(request);
    }
    private static async Task ResetAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Workspace") ?? "Host=localhost;Database=workspace_dev;Username=workspace;Password=workspace_dev_only";
        await using var db = new WorkspaceDbContext(new DbContextOptionsBuilder<WorkspaceDbContext>().UseNpgsql(connection).Options);
        await db.Reservations.ExecuteDeleteAsync(); await db.AuditEvents.ExecuteDeleteAsync();
        await db.Accounts.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CoordinationVersion, 0));
        await db.WorkspaceStates.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, 0));
    }
}
