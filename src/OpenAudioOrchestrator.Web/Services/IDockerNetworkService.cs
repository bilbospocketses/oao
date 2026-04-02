namespace OpenAudioOrchestrator.Web.Services;

public interface IDockerNetworkService
{
    Task<string> EnsureNetworkExistsAsync();
    Task<string?> GetContainerIpAsync(string containerId);
    string NetworkName { get; }
}
