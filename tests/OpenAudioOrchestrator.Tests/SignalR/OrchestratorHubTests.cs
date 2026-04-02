using OpenAudioOrchestrator.Web.Services;

namespace OpenAudioOrchestrator.Tests.SignalR;

public class OrchestratorHubTests
{
    [Theory]
    [InlineData("abc123def456", true)]
    [InlineData("abc123def456abc123def456abc123def456abc123def456abc123def456abcd", true)]
    [InlineData("ABC123", false)]              // uppercase
    [InlineData("abc", false)]                // too short
    [InlineData("abc123def456; rm -rf /", false)] // injection
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ContainerIdValidator_IsValid_ValidatesContainerIdFormat(string? id, bool expected)
    {
        Assert.Equal(expected, ContainerIdValidator.IsValid(id));
    }
}
