using OpenAudioOrchestrator.Web;

namespace OpenAudioOrchestrator.Tests;

public class PlatformDefaultsTests
{
    [Fact]
    public void DataRoot_ReturnsNonEmptyAbsolutePath()
    {
        var result = PlatformDefaults.DataRoot;
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.True(Path.IsPathRooted(result));
    }

    [Fact]
    public void DbPath_ContainsDataRoot()
    {
        var result = PlatformDefaults.DbPath;
        Assert.StartsWith(PlatformDefaults.DataRoot, result);
        Assert.EndsWith("AudioOrchestrator.db", result);
    }

    [Fact]
    public void DockerEndpoint_ReturnsValidUri()
    {
        var result = PlatformDefaults.DockerEndpoint;
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.True(
            result.StartsWith("npipe://") || result.StartsWith("unix://"),
            $"Expected npipe:// or unix:// but got: {result}");
    }

    [Fact]
    public void GitInstallHint_ReturnsNonEmptyString()
    {
        Assert.False(string.IsNullOrWhiteSpace(PlatformDefaults.GitInstallHint));
    }

    [Fact]
    public void GitLfsInstallHint_ReturnsNonEmptyString()
    {
        Assert.False(string.IsNullOrWhiteSpace(PlatformDefaults.GitLfsInstallHint));
    }

    [Fact]
    public void ConfigValueOrDefault_ReturnsDefaultForNullOrEmpty()
    {
        Assert.Equal("fallback", PlatformDefaults.ConfigValueOrDefault(null, "fallback"));
        Assert.Equal("fallback", PlatformDefaults.ConfigValueOrDefault("", "fallback"));
        Assert.Equal("fallback", PlatformDefaults.ConfigValueOrDefault("  ", "fallback"));
    }

    [Fact]
    public void ConfigValueOrDefault_ReturnsValueWhenPresent()
    {
        Assert.Equal("/custom/path", PlatformDefaults.ConfigValueOrDefault("/custom/path", "fallback"));
    }
}
