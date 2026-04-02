using System.Net;
using System.Net.Http.Json;

namespace OpenAudioOrchestrator.Tests.Integration;

public class ThemeEndpointTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public ThemeEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;
    public async Task InitializeAsync() => await _factory.SeedTestDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SetTheme_Authenticated_UpdatesPreference()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/auth/theme", new { theme = "light" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetTheme_InvalidTheme_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/auth/theme", new { theme = "neon" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTheme_Authenticated_ReturnsPreference()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/auth/theme");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ThemeResponse>();
        Assert.NotNull(body);
        Assert.Contains(body!.Theme, new[] { "dark", "light" });
    }

    [Fact]
    public async Task SetThenGet_ReturnsSameTheme()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        await client.PostAsJsonAsync("/api/auth/theme", new { theme = "light" });
        var response = await client.GetAsync("/api/auth/theme");
        var body = await response.Content.ReadFromJsonAsync<ThemeResponse>();
        Assert.Equal("light", body!.Theme);
    }

    private record ThemeResponse(string Theme);
}
