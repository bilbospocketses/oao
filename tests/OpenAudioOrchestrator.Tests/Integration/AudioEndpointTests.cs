using System.Net;

namespace OpenAudioOrchestrator.Tests.Integration;

public class AudioEndpointTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public AudioEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.SeedTestDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AudioOutput_Unauthenticated_RedirectsToLogin()
    {
        var client = _factory.CreateNonRedirectClient();

        var response = await client.GetAsync("/audio/output/test.wav");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task AudioReferences_Unauthenticated_RedirectsToLogin()
    {
        var client = _factory.CreateNonRedirectClient();

        var response = await client.GetAsync("/audio/references/voice1/sample.wav");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task AudioOutput_Authenticated_NonexistentFile_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/audio/output/nonexistent.wav");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AudioOutput_Authenticated_PathTraversal_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // URL-encoded path traversal
        var response = await client.GetAsync("/audio/output/..%2F..%2Fsecrets.txt");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AudioReferences_Authenticated_PathTraversal_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/audio/references/..%2F..%2Fsecrets.txt");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AudioOutput_Authenticated_RealFile_ReturnsAudio()
    {
        // Create a test audio file in the test output directory
        var outputDir = Path.Combine(_factory.TestDataRoot, "Output");
        var testFile = Path.Combine(outputDir, "test-audio.wav");
        await File.WriteAllBytesAsync(testFile, new byte[] { 0x52, 0x49, 0x46, 0x46 });

        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/audio/output/test-audio.wav");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/wav", response.Content.Headers.ContentType!.MediaType);
    }
}
