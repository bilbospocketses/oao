using Docker.DotNet;
using Docker.DotNet.Models;
using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using OpenAudioOrchestrator.Web.Hubs;
using OpenAudioOrchestrator.Web.Proxy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace OpenAudioOrchestrator.Web.Services;

public class DockerOrchestratorService : IDockerOrchestratorService
{
    private readonly IDockerClient _docker;
    private readonly IContainerConfigService _configService;
    private readonly AppDbContext _context;
    private readonly ProxyConfigProvider _proxyProvider;
    private readonly IDockerNetworkService _networkService;
    private readonly IHubContext<OrchestratorHub> _hub;
    private readonly OrchestratorEventBus _eventBus;
    private readonly ILogger<DockerOrchestratorService> _logger;

    public DockerOrchestratorService(
        IDockerClient docker,
        IContainerConfigService configService,
        AppDbContext context,
        ProxyConfigProvider proxyProvider,
        IDockerNetworkService networkService,
        IHubContext<OrchestratorHub> hub,
        OrchestratorEventBus eventBus,
        ILogger<DockerOrchestratorService> logger)
    {
        _docker = docker;
        _configService = configService;
        _context = context;
        _proxyProvider = proxyProvider;
        _networkService = networkService;
        _hub = hub;
        _eventBus = eventBus;
        _logger = logger;
    }

    private async Task PushStatusUpdateAsync()
    {
        var allModels = await _context.ModelProfiles.ToListAsync();
        var statusEvents = allModels.Select(m => new ContainerStatusEvent(
            m.Id, m.Name, m.Status.ToString(), m.HostPort, m.LastStartedAt)).ToList();
        await _hub.Clients.All.SendAsync("ReceiveContainerStatus", statusEvents);
        _eventBus.RaiseContainerStatus(statusEvents);
    }

    public async Task CreateAndStartModelAsync(ModelProfile profile)
    {
        // Load fresh from our tracked context to avoid detached entity issues
        var tracked = await _context.ModelProfiles.FindAsync(profile.Id);
        if (tracked is null) return;

        // If we already have a container ID, try to start the existing container
        if (tracked.ContainerId is not null)
        {
            if (!ContainerIdValidator.IsValid(tracked.ContainerId))
            {
                _logger.LogWarning(
                    "Skipping start for profile {ProfileId}: invalid container ID format '{ContainerId}'",
                    tracked.Id, tracked.ContainerId);
                return;
            }

            try
            {
                var inspect = await _docker.Containers.InspectContainerAsync(tracked.ContainerId);
                // Container exists — start it if not already running
                if (inspect.State.Status != "running")
                {
                    await _docker.Containers.StartContainerAsync(
                        tracked.ContainerId, new ContainerStartParameters());
                }

                tracked.Status = ModelStatus.Running;
                tracked.LastStartedAt = DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync();
                await UpdateProxyAsync(tracked);
                await PushStatusUpdateAsync();
                return;
            }
            catch (DockerContainerNotFoundException)
            {
                // Container was removed externally — fall through to create a new one
                tracked.ContainerId = null;
            }
        }

        var createParams = _configService.BuildCreateParams(tracked);
        var response = await _docker.Containers.CreateContainerAsync(createParams);
        tracked.ContainerId = response.ID;

        await _docker.Containers.StartContainerAsync(
            response.ID, new ContainerStartParameters());

        tracked.Status = ModelStatus.Running;
        tracked.LastStartedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();
        await UpdateProxyAsync(tracked);
        await PushStatusUpdateAsync();
    }

    private async Task UpdateProxyAsync(ModelProfile profile)
    {
        var containerIp = await _networkService.GetContainerIpAsync(profile.ContainerId!);
        if (containerIp is not null)
        {
            _proxyProvider.UpdateDestination($"http://{containerIp}:8080");
        }
        else
        {
            _proxyProvider.UpdateDestination($"http://localhost:{profile.HostPort}");
        }
    }

    public async Task StopModelAsync(ModelProfile profile)
    {
        // Load fresh from our tracked context to avoid detached entity issues
        var tracked = await _context.ModelProfiles.FindAsync(profile.Id);
        if (tracked is null || tracked.ContainerId is null) return;

        if (!ContainerIdValidator.IsValid(tracked.ContainerId))
        {
            _logger.LogWarning(
                "Skipping stop for profile {ProfileId}: invalid container ID format '{ContainerId}'",
                tracked.Id, tracked.ContainerId);
            return;
        }

        await ThrowIfJobProcessingAsync();

        await _docker.Containers.StopContainerAsync(
            tracked.ContainerId,
            new ContainerStopParameters { WaitBeforeKillSeconds = 30 });

        tracked.Status = ModelStatus.Stopped;
        await _context.SaveChangesAsync();

        _proxyProvider.ClearDestination();

        await PushStatusUpdateAsync();
    }

    public async Task RemoveModelAsync(ModelProfile profile)
    {
        // Load fresh from our tracked context to avoid detached entity issues
        var tracked = await _context.ModelProfiles.FindAsync(profile.Id);
        if (tracked is null) return;

        if (tracked.ContainerId is not null)
        {
            if (!ContainerIdValidator.IsValid(tracked.ContainerId))
            {
                _logger.LogWarning(
                    "Skipping Docker removal for profile {ProfileId}: invalid container ID format '{ContainerId}'",
                    tracked.Id, tracked.ContainerId);
            }
            else
            {
                if (tracked.Status == ModelStatus.Running)
                {
                    await StopModelAsync(tracked);
                }

                await _docker.Containers.RemoveContainerAsync(
                    tracked.ContainerId,
                    new ContainerRemoveParameters { Force = true });
            }
        }

        _context.ModelProfiles.Remove(tracked);
        await _context.SaveChangesAsync();

        await PushStatusUpdateAsync();
    }

    public async Task SwapModelAsync(ModelProfile newModel)
    {
        await ThrowIfJobProcessingAsync();

        var running = await _context.ModelProfiles
            .FirstOrDefaultAsync(m => m.Status == ModelStatus.Running);

        if (running is not null && running.Id != newModel.Id)
        {
            await StopModelAsync(running);
        }

        await CreateAndStartModelAsync(newModel);
    }

    private async Task ThrowIfJobProcessingAsync()
    {
        var hasActiveJob = await _context.TtsJobs
            .AnyAsync(j => j.Status == TtsJobStatus.Processing);

        if (hasActiveJob)
            throw new InvalidOperationException(
                "Cannot change models while a TTS job is processing. Wait for the job to finish or cancel it first.");
    }

    public async Task<string> GetContainerStatusAsync(string containerId)
    {
        var inspect = await _docker.Containers.InspectContainerAsync(containerId);
        return inspect.State.Status;
    }
}
