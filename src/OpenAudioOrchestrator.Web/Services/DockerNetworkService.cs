using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Configuration;

namespace OpenAudioOrchestrator.Web.Services;

public class DockerNetworkService : IDockerNetworkService
{
    private readonly IDockerClient _docker;
    private readonly ILogger<DockerNetworkService> _logger;
    public string NetworkName { get; }

    public DockerNetworkService(IDockerClient docker, IConfiguration config, ILogger<DockerNetworkService> logger)
    {
        _docker = docker;
        _logger = logger;
        NetworkName = config["OpenAudioOrchestrator:DockerNetworkName"] ?? "oao-network";
    }

    public async Task<string> EnsureNetworkExistsAsync()
    {
        var networks = await _docker.Networks.ListNetworksAsync(
            new NetworksListParameters());

        var existing = networks.FirstOrDefault(n => n.Name == NetworkName);
        if (existing is not null)
            return existing.ID;

        var response = await _docker.Networks.CreateNetworkAsync(
            new NetworksCreateParameters
            {
                Name = NetworkName,
                Driver = "bridge"
            });

        return response.ID;
    }

    public async Task<string?> GetContainerIpAsync(string containerId)
    {
        if (!ContainerIdValidator.IsValid(containerId))
        {
            _logger.LogWarning(
                "Skipping IP lookup: invalid container ID format '{ContainerId}'", containerId);
            return null;
        }

        var inspect = await _docker.Containers.InspectContainerAsync(containerId);

        if (inspect.NetworkSettings?.Networks != null &&
            inspect.NetworkSettings.Networks.TryGetValue(NetworkName, out var endpoint))
        {
            return endpoint.IPAddress;
        }

        return null;
    }
}
