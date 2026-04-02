using Docker.DotNet;
using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using OpenAudioOrchestrator.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenAudioOrchestrator.Web.Services;

public class HealthMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDockerClient _docker;
    private readonly int _intervalSeconds;
    private readonly ILogger<HealthMonitorService> _logger;
    private readonly IHubContext<OrchestratorHub> _hub;
    private readonly GpuMetricsState _gpuState;
    private readonly OrchestratorEventBus _eventBus;
    private volatile int _consecutiveFailures;

    public HealthMonitorService(
        IServiceScopeFactory scopeFactory,
        IDockerClient docker,
        IConfiguration config,
        ILogger<HealthMonitorService> logger,
        IHubContext<OrchestratorHub> hub,
        GpuMetricsState gpuState,
        OrchestratorEventBus eventBus)
    {
        _scopeFactory = scopeFactory;
        _docker = docker;
        _intervalSeconds = int.Parse(
            config["OpenAudioOrchestrator:HealthCheckIntervalSeconds"] ?? "30");
        _logger = logger;
        _hub = hub;
        _gpuState = gpuState;
        _eventBus = eventBus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run health checks and GPU metrics on separate intervals
        var healthTask = RunHealthChecksAsync(stoppingToken);
        var gpuTask = RunGpuMetricsAsync(stoppingToken);
        await Task.WhenAll(healthTask, gpuTask);
    }

    private async Task RunHealthChecksAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckHealthAsync();
            try { await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task RunGpuMetricsAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectGpuMetricsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GPU metrics collection failed");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task CollectGpuMetricsAsync()
    {
        var gpuMetrics = await GpuMetricsParser.CollectAsync();
        if (gpuMetrics is not null)
        {
            _gpuState.Update(gpuMetrics);
            await _hub.Clients.All.SendAsync("ReceiveGpuMetrics", gpuMetrics);
            _eventBus.RaiseGpuMetrics(gpuMetrics);
        }
    }

    public async Task CheckHealthAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ttsClient = scope.ServiceProvider.GetRequiredService<ITtsClientService>();

        var running = await context.ModelProfiles
            .FirstOrDefaultAsync(m => m.Status == ModelStatus.Running || m.Status == ModelStatus.Error);

        if (running is null || running.ContainerId is null)
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            return;
        }

        // If TTS jobs are active, just check Docker container status instead of
        // hitting the HTTP health endpoint (which won't respond during generation)
        var hasActiveJobs = await context.TtsJobs
            .AnyAsync(j => j.Status == TtsJobStatus.Processing || j.Status == TtsJobStatus.Queued);

        bool healthy;
        if (hasActiveJobs && running.ContainerId is not null)
        {
            try
            {
                var inspect = await _docker.Containers.InspectContainerAsync(running.ContainerId);
                healthy = inspect.State.Running;
            }
            catch
            {
                healthy = false;
            }
        }
        else
        {
            var baseUrl = $"http://localhost:{running.HostPort}";
            healthy = await ttsClient.GetHealthAsync(baseUrl);
        }

        if (healthy)
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            if (running.Status != ModelStatus.Running)
            {
                running.Status = ModelStatus.Running;
                await context.SaveChangesAsync();
            }
        }
        else
        {
            var failures = Interlocked.Increment(ref _consecutiveFailures);
            _logger.LogWarning(
                "Health check failed for {ModelName} ({Failures} consecutive)",
                running.Name, failures);

            if (failures >= 5)
            {
                running.Status = ModelStatus.Error;
                await context.SaveChangesAsync();
            }
        }

        // Push container status for all models
        var allModels = await context.ModelProfiles.ToListAsync();
        var statusEvents = allModels.Select(m => new ContainerStatusEvent(
            m.Id, m.Name, m.Status.ToString(), m.HostPort, m.LastStartedAt)).ToList();
        await _hub.Clients.All.SendAsync("ReceiveContainerStatus", statusEvents);
        _eventBus.RaiseContainerStatus(statusEvents);
    }
}
