using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Text.Json;

namespace Workspace.IntegrationTests;

[Collection("PostgreSQL coordination")]
public sealed class SessionEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public SessionEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Same_nickname_in_two_cookie_sessions_gets_different_participant_ids()
    {
        using var first = _factory.CreateClient(); using var second = _factory.CreateClient();
        var firstId = await SignInAsync(first, "小明"); var secondId = await SignInAsync(second, "小明");
        Assert.NotEqual(firstId, secondId);
    }

    private static async Task<Guid> SignInAsync(HttpClient client, string nickname)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/session/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/session") { Content = JsonContent.Create(new { nickname, participantId = Guid.NewGuid() }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var response = await client.SendAsync(request); response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        return session.GetProperty("participantId").GetGuid();
    }
}
