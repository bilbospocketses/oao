> **Note:** post-rename. References to `OpenAudioOrchestrator.*` paths and
> config keys reflect their original names. Current equivalents are
> `oao.*` and `oao:*`.
# Phase 5: Real-Time SignalR Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add real-time updates to the dashboard via a single SignalR hub — live container status, GPU metrics, TTS notifications, and Docker container log streaming.

**Architecture:** Single `OrchestratorHub` at `/hubs/orchestrator` broadcasts container status and GPU metrics from the existing `HealthMonitorService` every 30s. Container operations push immediate updates. A `ContainerLogService` manages shared Docker log streams with per-client subscriptions. A `GpuMetricsState` singleton provides reactive GPU data to UI components.

**Tech Stack:** ASP.NET SignalR (built-in), Docker.DotNet log streaming, Blazor Server components

---

## File Structure

### New Files

| File | Responsibility |
|------|---------------|
| `src/.../Hubs/OrchestratorHub.cs` | SignalR hub with log subscribe/unsubscribe methods |
| `src/.../Hubs/HubEvents.cs` | DTO records for hub event payloads |
| `src/.../Services/IContainerLogService.cs` | Interface for container log stream management |
| `src/.../Services/ContainerLogService.cs` | Docker log stream management with shared subscriptions |
| `src/.../Services/GpuMetricsState.cs` | Singleton holding latest GPU metrics with change notification |
| `src/.../Services/GpuMetricsParser.cs` | Static parser for nvidia-smi output |
| `src/.../Components/Pages/Logs.razor` | Full container log viewer page |
| `tests/.../SignalR/GpuMetricsParserTests.cs` | GPU metrics parsing unit tests |
| `tests/.../SignalR/ContainerLogServiceTests.cs` | Subscription tracking unit tests |
| `tests/.../SignalR/HealthMonitorHubTests.cs` | Hub notification dispatch tests |

### Modified Files

| File | Change |
|------|--------|
| `src/.../Program.cs` | Add SignalR, ContainerLogService, GpuMetricsState, MapHub |
| `src/.../Services/HealthMonitorService.cs` | Push status + GPU metrics through hub after each cycle |
| `src/.../Services/DockerOrchestratorService.cs` | Push immediate status updates after container operations |
| `src/.../Services/TtsClientService.cs` | Push TTS notifications after generation |
| `src/.../Components/Pages/Dashboard.razor` | Real-time hub updates, GPU panel, log preview, toast |
| `src/.../Components/Pages/TtsPlayground.razor` | Listen for status changes and TTS notifications |
| `src/.../Components/Layout/NavMenu.razor` | Use GpuMetricsState instead of nvidia-smi process |

---

## Task 1: Hub Event DTOs

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Hubs/HubEvents.cs`

- [ ] **Step 1: Create the event records**

Create `src/FishAudioOrchestrator.Web/Hubs/HubEvents.cs`:

```csharp
namespace FishAudioOrchestrator.Web.Hubs;

public record ContainerStatusEvent(
    int ModelId,
    string Name,
    string Status,
    int HostPort,
    DateTime? LastStartedAt);

public record GpuMetricsEvent(
    int MemoryUsedMb,
    int MemoryTotalMb,
    int UtilizationPercent);

public record TtsNotificationEvent(
    string? UserId,
    string Text,
    string OutputFileName,
    long DurationMs,
    bool Success,
    string? Error);

public record LogLineEvent(
    string ContainerId,
    DateTime Timestamp,
    string Line);
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Hubs/HubEvents.cs
git commit -m "feat: add SignalR hub event DTO records"
```

---

## Task 2: GpuMetricsParser and GpuMetricsState

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Services/GpuMetricsParser.cs`
- Create: `src/FishAudioOrchestrator.Web/Services/GpuMetricsState.cs`
- Test: `tests/FishAudioOrchestrator.Tests/SignalR/GpuMetricsParserTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/FishAudioOrchestrator.Tests/SignalR/GpuMetricsParserTests.cs`:

```csharp
using FishAudioOrchestrator.Web.Hubs;
using FishAudioOrchestrator.Web.Services;

namespace FishAudioOrchestrator.Tests.SignalR;

public class GpuMetricsParserTests
{
    [Fact]
    public void Parse_ValidOutput_ReturnsMetrics()
    {
        var output = "2048, 12288, 35";
        var result = GpuMetricsParser.Parse(output);

        Assert.NotNull(result);
        Assert.Equal(2048, result!.MemoryUsedMb);
        Assert.Equal(12288, result.MemoryTotalMb);
        Assert.Equal(35, result.UtilizationPercent);
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsNull()
    {
        var result = GpuMetricsParser.Parse("");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_MalformedOutput_ReturnsNull()
    {
        var result = GpuMetricsParser.Parse("not a number, also not");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_TwoFieldsOnly_ReturnsNull()
    {
        var result = GpuMetricsParser.Parse("2048, 12288");
        Assert.Null(result);
    }

    [Fact]
    public void GpuMetricsState_UpdateAndNotify()
    {
        var state = new GpuMetricsState();
        var notified = false;
        state.OnChange += () => notified = true;

        state.Update(new GpuMetricsEvent(4096, 12288, 50));

        Assert.True(notified);
        Assert.Equal(4096, state.MemoryUsedMb);
        Assert.Equal(12288, state.MemoryTotalMb);
        Assert.Equal(50, state.UtilizationPercent);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~GpuMetricsParserTests" -v q`
Expected: Compilation error — `GpuMetricsParser` and `GpuMetricsState` do not exist.

- [ ] **Step 3: Create GpuMetricsParser**

Create `src/FishAudioOrchestrator.Web/Services/GpuMetricsParser.cs`:

```csharp
using FishAudioOrchestrator.Web.Hubs;

namespace FishAudioOrchestrator.Web.Services;

public static class GpuMetricsParser
{
    public static GpuMetricsEvent? Parse(string nvidiaSmiOutput)
    {
        if (string.IsNullOrWhiteSpace(nvidiaSmiOutput))
            return null;

        var parts = nvidiaSmiOutput.Trim().Split(',');
        if (parts.Length < 3)
            return null;

        if (!int.TryParse(parts[0].Trim(), out var memUsed) ||
            !int.TryParse(parts[1].Trim(), out var memTotal) ||
            !int.TryParse(parts[2].Trim(), out var util))
            return null;

        return new GpuMetricsEvent(memUsed, memTotal, util);
    }

    public static async Task<GpuMetricsEvent?> CollectAsync()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=memory.used,memory.total,utilization.gpu --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return null;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return Parse(output);
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Create GpuMetricsState**

Create `src/FishAudioOrchestrator.Web/Services/GpuMetricsState.cs`:

```csharp
using FishAudioOrchestrator.Web.Hubs;

namespace FishAudioOrchestrator.Web.Services;

public class GpuMetricsState
{
    public int MemoryUsedMb { get; private set; }
    public int MemoryTotalMb { get; private set; }
    public int UtilizationPercent { get; private set; }
    public DateTime LastUpdated { get; private set; }

    public event Action? OnChange;

    public void Update(GpuMetricsEvent metrics)
    {
        MemoryUsedMb = metrics.MemoryUsedMb;
        MemoryTotalMb = metrics.MemoryTotalMb;
        UtilizationPercent = metrics.UtilizationPercent;
        LastUpdated = DateTime.UtcNow;
        OnChange?.Invoke();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~GpuMetricsParserTests" -v q`
Expected: 5 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Services/GpuMetricsParser.cs src/FishAudioOrchestrator.Web/Services/GpuMetricsState.cs tests/FishAudioOrchestrator.Tests/SignalR/GpuMetricsParserTests.cs
git commit -m "feat: add GpuMetricsParser and GpuMetricsState for real-time GPU monitoring"
```

---

## Task 3: OrchestratorHub

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Hubs/OrchestratorHub.cs`

- [ ] **Step 1: Create the hub**

Create `src/FishAudioOrchestrator.Web/Hubs/OrchestratorHub.cs`:

```csharp
using FishAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FishAudioOrchestrator.Web.Hubs;

[Authorize]
public class OrchestratorHub : Hub
{
    private readonly IContainerLogService _logService;

    public OrchestratorHub(IContainerLogService logService)
    {
        _logService = logService;
    }

    public async Task SubscribeLogs(string containerId)
    {
        await _logService.SubscribeAsync(containerId, Context.ConnectionId);
    }

    public async Task UnsubscribeLogs(string containerId)
    {
        await _logService.UnsubscribeAsync(containerId, Context.ConnectionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _logService.UnsubscribeAllAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build errors — `IContainerLogService` does not exist yet. This is expected; it's created in the next task.

- [ ] **Step 3: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Hubs/OrchestratorHub.cs
git commit -m "feat: add OrchestratorHub with log subscribe/unsubscribe and auth"
```

---

## Task 4: ContainerLogService

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Services/IContainerLogService.cs`
- Create: `src/FishAudioOrchestrator.Web/Services/ContainerLogService.cs`
- Test: `tests/FishAudioOrchestrator.Tests/SignalR/ContainerLogServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/FishAudioOrchestrator.Tests/SignalR/ContainerLogServiceTests.cs`:

```csharp
using FishAudioOrchestrator.Web.Hubs;
using FishAudioOrchestrator.Web.Services;
using Docker.DotNet;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace FishAudioOrchestrator.Tests.SignalR;

public class ContainerLogServiceTests
{
    private static (ContainerLogService service, Mock<IHubContext<OrchestratorHub>> hubMock) CreateService()
    {
        var dockerMock = new Mock<IDockerClient>();
        var hubMock = new Mock<IHubContext<OrchestratorHub>>();
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.Client(It.IsAny<string>())).Returns(clientProxyMock.Object);
        clientsMock.Setup(c => c.Clients(It.IsAny<IReadOnlyList<string>>())).Returns(clientProxyMock.Object);
        hubMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        var service = new ContainerLogService(dockerMock.Object, hubMock.Object);
        return (service, hubMock);
    }

    [Fact]
    public async Task Subscribe_AddsConnectionToContainer()
    {
        var (service, _) = CreateService();

        await service.SubscribeAsync("container-1", "conn-a");

        Assert.True(service.HasSubscribers("container-1"));
    }

    [Fact]
    public async Task Unsubscribe_RemovesConnectionFromContainer()
    {
        var (service, _) = CreateService();

        await service.SubscribeAsync("container-1", "conn-a");
        await service.UnsubscribeAsync("container-1", "conn-a");

        Assert.False(service.HasSubscribers("container-1"));
    }

    [Fact]
    public async Task UnsubscribeAll_RemovesConnectionFromAllContainers()
    {
        var (service, _) = CreateService();

        await service.SubscribeAsync("container-1", "conn-a");
        await service.SubscribeAsync("container-2", "conn-a");
        await service.UnsubscribeAllAsync("conn-a");

        Assert.False(service.HasSubscribers("container-1"));
        Assert.False(service.HasSubscribers("container-2"));
    }

    [Fact]
    public async Task MultipleSubscribers_StreamSurvivesPartialUnsubscribe()
    {
        var (service, _) = CreateService();

        await service.SubscribeAsync("container-1", "conn-a");
        await service.SubscribeAsync("container-1", "conn-b");
        await service.UnsubscribeAsync("container-1", "conn-a");

        Assert.True(service.HasSubscribers("container-1"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~ContainerLogServiceTests" -v q`
Expected: Compilation error — types do not exist.

- [ ] **Step 3: Create IContainerLogService**

Create `src/FishAudioOrchestrator.Web/Services/IContainerLogService.cs`:

```csharp
namespace FishAudioOrchestrator.Web.Services;

public interface IContainerLogService
{
    Task SubscribeAsync(string containerId, string connectionId);
    Task UnsubscribeAsync(string containerId, string connectionId);
    Task UnsubscribeAllAsync(string connectionId);
    bool HasSubscribers(string containerId);
}
```

- [ ] **Step 4: Create ContainerLogService**

Create `src/FishAudioOrchestrator.Web/Services/ContainerLogService.cs`:

```csharp
using System.Collections.Concurrent;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using FishAudioOrchestrator.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace FishAudioOrchestrator.Web.Services;

public class ContainerLogService : IContainerLogService
{
    private readonly IDockerClient _docker;
    private readonly IHubContext<OrchestratorHub> _hub;
    private readonly ConcurrentDictionary<string, ContainerLogStream> _streams = new();

    public ContainerLogService(IDockerClient docker, IHubContext<OrchestratorHub> hub)
    {
        _docker = docker;
        _hub = hub;
    }

    public async Task SubscribeAsync(string containerId, string connectionId)
    {
        var stream = _streams.GetOrAdd(containerId, _ => new ContainerLogStream());

        lock (stream.Lock)
        {
            stream.Subscribers.Add(connectionId);
        }

        if (stream.ReaderTask is null || stream.ReaderTask.IsCompleted)
        {
            stream.Cts = new CancellationTokenSource();
            stream.ReaderTask = Task.Run(() => ReadLogStreamAsync(containerId, stream));
        }
    }

    public Task UnsubscribeAsync(string containerId, string connectionId)
    {
        if (!_streams.TryGetValue(containerId, out var stream))
            return Task.CompletedTask;

        bool shouldCancel;
        lock (stream.Lock)
        {
            stream.Subscribers.Remove(connectionId);
            shouldCancel = stream.Subscribers.Count == 0;
        }

        if (shouldCancel)
        {
            stream.Cts?.Cancel();
            _streams.TryRemove(containerId, out _);
        }

        return Task.CompletedTask;
    }

    public Task UnsubscribeAllAsync(string connectionId)
    {
        foreach (var kvp in _streams)
        {
            bool shouldCancel;
            lock (kvp.Value.Lock)
            {
                kvp.Value.Subscribers.Remove(connectionId);
                shouldCancel = kvp.Value.Subscribers.Count == 0;
            }

            if (shouldCancel)
            {
                kvp.Value.Cts?.Cancel();
                _streams.TryRemove(kvp.Key, out _);
            }
        }

        return Task.CompletedTask;
    }

    public bool HasSubscribers(string containerId)
    {
        if (!_streams.TryGetValue(containerId, out var stream))
            return false;

        lock (stream.Lock)
        {
            return stream.Subscribers.Count > 0;
        }
    }

    private async Task ReadLogStreamAsync(string containerId, ContainerLogStream logStream)
    {
        try
        {
            var parameters = new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = true,
                Tail = "50",
                Timestamps = true
            };

            using var stream = await _docker.Containers.GetContainerLogsAsync(
                containerId, false, parameters, logStream.Cts!.Token);

            var buffer = new byte[4096];
            while (!logStream.Cts.Token.IsCancellationRequested)
            {
                var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, logStream.Cts.Token);
                if (result.Count == 0) break;

                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    var logEvent = ParseLogLine(containerId, line);

                    string[] subscribers;
                    lock (logStream.Lock)
                    {
                        subscribers = logStream.Subscribers.ToArray();
                    }

                    if (subscribers.Length > 0)
                    {
                        await _hub.Clients.Clients(subscribers)
                            .SendAsync("ReceiveLogLine", logEvent, logStream.Cts.Token);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    private static LogLineEvent ParseLogLine(string containerId, string rawLine)
    {
        // Docker log lines with timestamps: "2026-03-29T12:00:00.000000000Z message"
        var trimmed = rawLine.Trim();
        if (trimmed.Length > 30 && trimmed[4] == '-' && trimmed[10] == 'T')
        {
            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx > 0 && DateTime.TryParse(trimmed[..spaceIdx], out var ts))
            {
                return new LogLineEvent(containerId, ts, trimmed[(spaceIdx + 1)..]);
            }
        }

        return new LogLineEvent(containerId, DateTime.UtcNow, trimmed);
    }

    private class ContainerLogStream
    {
        public readonly object Lock = new();
        public readonly HashSet<string> Subscribers = new();
        public CancellationTokenSource? Cts;
        public Task? ReaderTask;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~ContainerLogServiceTests" -v q`
Expected: 4 passed, 0 failed.

- [ ] **Step 6: Verify full build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded (hub can now resolve IContainerLogService).

- [ ] **Step 7: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Services/IContainerLogService.cs src/FishAudioOrchestrator.Web/Services/ContainerLogService.cs tests/FishAudioOrchestrator.Tests/SignalR/ContainerLogServiceTests.cs
git commit -m "feat: add ContainerLogService for shared Docker log stream management"
```

---

## Task 5: Extend HealthMonitorService with Hub Notifications

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Services/HealthMonitorService.cs`
- Test: `tests/FishAudioOrchestrator.Tests/SignalR/HealthMonitorHubTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/FishAudioOrchestrator.Tests/SignalR/HealthMonitorHubTests.cs`:

```csharp
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Hubs;
using FishAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FishAudioOrchestrator.Tests.SignalR;

public class HealthMonitorHubTests
{
    private static AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static IConfiguration CreateConfig()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FishOrchestrator:HealthCheckIntervalSeconds"] = "1"
            })
            .Build();
    }

    private static IServiceScopeFactory CreateScopeFactory(AppDbContext context)
    {
        var mockScope = new Mock<IServiceScope>();
        var mockProvider = new Mock<IServiceProvider>();
        mockProvider.Setup(p => p.GetService(typeof(AppDbContext))).Returns(context);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockProvider.Object);

        var mockFactory = new Mock<IServiceScopeFactory>();
        mockFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);
        return mockFactory.Object;
    }

    [Fact]
    public async Task CheckHealthAsync_PushesContainerStatusToHub()
    {
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "test-model",
            CheckpointPath = @"D:\path",
            ImageTag = "test:latest",
            HostPort = 9001,
            ContainerId = "container-1",
            Status = ModelStatus.Running
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var mockTts = new Mock<ITtsClientService>();
        mockTts.Setup(t => t.GetHealthAsync(It.IsAny<string>())).ReturnsAsync(true);

        var mockHub = new Mock<IHubContext<OrchestratorHub>>();
        var mockClients = new Mock<IHubClients>();
        var mockAll = new Mock<IClientProxy>();
        mockClients.Setup(c => c.All).Returns(mockAll.Object);
        mockHub.Setup(h => h.Clients).Returns(mockClients.Object);

        var gpuState = new GpuMetricsState();

        var service = new HealthMonitorService(
            CreateScopeFactory(context), mockTts.Object, CreateConfig(),
            NullLogger<HealthMonitorService>.Instance, mockHub.Object, gpuState);

        await service.CheckHealthAsync();

        mockAll.Verify(c => c.SendCoreAsync(
            "ReceiveContainerStatus",
            It.Is<object?[]>(args => args.Length == 1 && args[0] is IEnumerable<ContainerStatusEvent>),
            default), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~HealthMonitorHubTests" -v q`
Expected: Compilation error — HealthMonitorService constructor doesn't accept hub context yet.

- [ ] **Step 3: Update HealthMonitorService**

Replace `src/FishAudioOrchestrator.Web/Services/HealthMonitorService.cs` with:

```csharp
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FishAudioOrchestrator.Web.Services;

public class HealthMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITtsClientService _ttsClient;
    private readonly int _intervalSeconds;
    private readonly ILogger<HealthMonitorService> _logger;
    private readonly IHubContext<OrchestratorHub> _hub;
    private readonly GpuMetricsState _gpuState;
    private int _consecutiveFailures;

    public HealthMonitorService(
        IServiceScopeFactory scopeFactory,
        ITtsClientService ttsClient,
        IConfiguration config,
        ILogger<HealthMonitorService> logger,
        IHubContext<OrchestratorHub> hub,
        GpuMetricsState gpuState)
    {
        _scopeFactory = scopeFactory;
        _ttsClient = ttsClient;
        _intervalSeconds = int.Parse(
            config["FishOrchestrator:HealthCheckIntervalSeconds"] ?? "30");
        _logger = logger;
        _hub = hub;
        _gpuState = gpuState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
            await CheckHealthAsync();
        }
    }

    public async Task CheckHealthAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var running = await context.ModelProfiles
            .FirstOrDefaultAsync(m => m.Status == ModelStatus.Running);

        if (running is not null)
        {
            var baseUrl = $"http://localhost:{running.HostPort}";
            var healthy = await _ttsClient.GetHealthAsync(baseUrl);

            if (healthy)
            {
                _consecutiveFailures = 0;
                if (running.Status != ModelStatus.Running)
                {
                    running.Status = ModelStatus.Running;
                    await context.SaveChangesAsync();
                }
            }
            else
            {
                _consecutiveFailures++;
                _logger.LogWarning(
                    "Health check failed for {ModelName} ({Failures} consecutive)",
                    running.Name, _consecutiveFailures);

                running.Status = ModelStatus.Error;
                await context.SaveChangesAsync();
            }
        }
        else
        {
            _consecutiveFailures = 0;
        }

        // Push container status for all models
        var allModels = await context.ModelProfiles.ToListAsync();
        var statusEvents = allModels.Select(m => new ContainerStatusEvent(
            m.Id, m.Name, m.Status.ToString(), m.HostPort, m.LastStartedAt)).ToList();
        await _hub.Clients.All.SendAsync("ReceiveContainerStatus", statusEvents);

        // Collect and push GPU metrics
        var gpuMetrics = await GpuMetricsParser.CollectAsync();
        if (gpuMetrics is not null)
        {
            _gpuState.Update(gpuMetrics);
            await _hub.Clients.All.SendAsync("ReceiveGpuMetrics", gpuMetrics);
        }
    }
}
```

- [ ] **Step 4: Update existing HealthMonitorService tests**

The existing tests in `tests/FishAudioOrchestrator.Tests/Services/HealthMonitorServiceTests.cs` need the new constructor parameters. Update the `HealthMonitorService` instantiations to include the hub mock and GPU state:

Add these helpers at the top of the class:

```csharp
private static (Mock<IHubContext<OrchestratorHub>> hubMock, GpuMetricsState gpuState) CreateHubMocks()
{
    var hubMock = new Mock<IHubContext<OrchestratorHub>>();
    var clientsMock = new Mock<IHubClients>();
    var clientProxyMock = new Mock<IClientProxy>();
    clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
    hubMock.Setup(h => h.Clients).Returns(clientsMock.Object);
    return (hubMock, new GpuMetricsState());
}
```

Update each test's service construction from:
```csharp
var service = new HealthMonitorService(
    scopeFactory, mockTts.Object, CreateConfig(),
    NullLogger<HealthMonitorService>.Instance);
```
to:
```csharp
var (hubMock, gpuState) = CreateHubMocks();
var service = new HealthMonitorService(
    scopeFactory, mockTts.Object, CreateConfig(),
    NullLogger<HealthMonitorService>.Instance, hubMock.Object, gpuState);
```

Add the required usings at the top:
```csharp
using FishAudioOrchestrator.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
```

- [ ] **Step 5: Run all tests**

Run: `dotnet test --nologo -v q`
Expected: All tests pass (64 existing + 5 GPU + 4 ContainerLog + 1 HubTests = ~74).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: extend HealthMonitorService to push status and GPU metrics through SignalR hub"
```

---

## Task 6: Wire SignalR in Program.cs

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Program.cs`

- [ ] **Step 1: Add SignalR and new services**

In `Program.cs`, add after the existing `builder.Services.AddHostedService<HealthMonitorService>();` line:

```csharp
// SignalR
builder.Services.AddSignalR();
builder.Services.AddSingleton<IContainerLogService, ContainerLogService>();
builder.Services.AddSingleton<GpuMetricsState>();
```

Add the hub mapping after `app.MapRazorComponents<App>()...`:

```csharp
app.MapHub<OrchestratorHub>("/hubs/orchestrator");
```

Add the using at the top of Program.cs:
```csharp
using FishAudioOrchestrator.Web.Hubs;
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 3: Run tests**

Run: `dotnet test --nologo -v q`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Program.cs
git commit -m "feat: wire SignalR hub, ContainerLogService, and GpuMetricsState in Program.cs"
```

---

## Task 7: DockerOrchestratorService Hub Notifications

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Services/DockerOrchestratorService.cs`

- [ ] **Step 1: Add hub context to DockerOrchestratorService**

Add `IHubContext<OrchestratorHub>` as a constructor parameter and field. After each operation that changes model state (`CreateAndStartModelAsync`, `StopModelAsync`, `RemoveModelAsync`), push an immediate status update:

```csharp
using FishAudioOrchestrator.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
```

Add to constructor parameters:
```csharp
private readonly IHubContext<OrchestratorHub> _hub;
```

Add to constructor body:
```csharp
_hub = hub;
```

Add this private helper method:
```csharp
private async Task PushStatusUpdateAsync()
{
    var allModels = await _context.ModelProfiles.ToListAsync();
    var statusEvents = allModels.Select(m => new ContainerStatusEvent(
        m.Id, m.Name, m.Status.ToString(), m.HostPort, m.LastStartedAt)).ToList();
    await _hub.Clients.All.SendAsync("ReceiveContainerStatus", statusEvents);
}
```

Call `await PushStatusUpdateAsync();` at the end of `CreateAndStartModelAsync`, `StopModelAsync`, and `RemoveModelAsync` (after `SaveChangesAsync`).

- [ ] **Step 2: Update DockerOrchestratorService tests**

The existing tests in `tests/FishAudioOrchestrator.Tests/Services/DockerOrchestratorServiceTests.cs` need the hub mock added to the constructor. Add a mock `IHubContext<OrchestratorHub>` parameter. Read the test file first to understand the current construction pattern, then update all instantiations.

- [ ] **Step 3: Run all tests**

Run: `dotnet test --nologo -v q`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: push immediate container status updates from DockerOrchestratorService via SignalR"
```

---

## Task 8: TtsClientService Hub Notifications

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Services/TtsClientService.cs`

- [ ] **Step 1: Add hub context to TtsClientService**

Add `IHubContext<OrchestratorHub>` as a constructor parameter. After `SaveGenerationLogAsync` completes in `GenerateAsync`, push a `TtsNotificationEvent`:

Add to constructor:
```csharp
private readonly IHubContext<OrchestratorHub> _hub;
```

At the end of `GenerateAsync`, after the `SaveGenerationLogAsync` call, add:
```csharp
var notification = new TtsNotificationEvent(
    null, // UserId will be set by the caller if available
    request.Text.Length > 50 ? request.Text[..50] + "..." : request.Text,
    outputFileName,
    sw.ElapsedMilliseconds,
    true,
    null);
await _hub.Clients.All.SendAsync("ReceiveTtsNotification", notification);
```

Wrap the existing try body in a try-catch and on failure push an error notification:
```csharp
catch (Exception ex)
{
    var errorNotification = new TtsNotificationEvent(
        null,
        request.Text.Length > 50 ? request.Text[..50] + "..." : request.Text,
        "",
        0,
        false,
        ex.Message);
    await _hub.Clients.All.SendAsync("ReceiveTtsNotification", errorNotification);
    throw;
}
```

Add usings:
```csharp
using FishAudioOrchestrator.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
```

- [ ] **Step 2: Update TtsClientService tests**

Read `tests/FishAudioOrchestrator.Tests/Services/TtsClientServiceTests.cs` and add a mock `IHubContext<OrchestratorHub>` to all constructor calls.

- [ ] **Step 3: Run all tests**

Run: `dotnet test --nologo -v q`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: push TTS generation notifications through SignalR hub"
```

---

## Task 9: Update NavMenu to Use GpuMetricsState

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Components/Layout/NavMenu.razor`

- [ ] **Step 1: Replace nvidia-smi call with GpuMetricsState**

Replace `src/FishAudioOrchestrator.Web/Components/Layout/NavMenu.razor` with:

```razor
@using Microsoft.AspNetCore.Components.Authorization
@implements IDisposable
@inject GpuMetricsState GpuState

<nav class="navbar navbar-expand navbar-dark bg-dark px-3">
    <a class="navbar-brand" href="/">Fish Orchestrator</a>
    <AuthorizeView>
        <Authorized>
            <div class="navbar-nav">
                <NavLink class="nav-link" href="/" Match="NavLinkMatch.All">Dashboard</NavLink>
                <AuthorizeView Roles="Admin" Context="adminCtx1">
                    <NavLink class="nav-link" href="/deploy">Deploy</NavLink>
                </AuthorizeView>
                <NavLink class="nav-link" href="/voices">Voices</NavLink>
                <NavLink class="nav-link" href="/playground">TTS</NavLink>
                <NavLink class="nav-link" href="/history">History</NavLink>
                <NavLink class="nav-link" href="/logs">Logs</NavLink>
                <AuthorizeView Roles="Admin" Context="adminCtx2">
                    <NavLink class="nav-link" href="/admin/users">Users</NavLink>
                    <NavLink class="nav-link" href="/admin/settings">Settings</NavLink>
                </AuthorizeView>
            </div>
            <div class="navbar-nav ms-auto">
                <span class="nav-link text-muted" id="gpu-indicator">@_gpuInfo</span>
                <NavLink class="nav-link" href="/account">@context.User.Identity?.Name</NavLink>
            </div>
        </Authorized>
    </AuthorizeView>
</nav>

@code {
    private string _gpuInfo = "GPU: loading...";

    protected override void OnInitialized()
    {
        GpuState.OnChange += UpdateGpuInfo;
        if (GpuState.MemoryTotalMb > 0)
        {
            UpdateGpuInfo();
        }
    }

    private void UpdateGpuInfo()
    {
        _gpuInfo = $"GPU: {GpuState.MemoryUsedMb} / {GpuState.MemoryTotalMb} MB ({GpuState.UtilizationPercent}%)";
        InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        GpuState.OnChange -= UpdateGpuInfo;
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Layout/NavMenu.razor
git commit -m "feat: replace per-client nvidia-smi with reactive GpuMetricsState in NavMenu"
```

---

## Task 10: Real-Time Dashboard Page

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Components/Pages/Dashboard.razor`

- [ ] **Step 1: Rewrite Dashboard.razor with hub integration**

Replace `src/FishAudioOrchestrator.Web/Components/Pages/Dashboard.razor` with:

```razor
@page "/"
@attribute [Authorize]
@inject AppDbContext Db
@inject IDockerOrchestratorService Orchestrator
@inject GpuMetricsState GpuState
@using Microsoft.EntityFrameworkCore
@using FishAudioOrchestrator.Web.Data
@using FishAudioOrchestrator.Web.Hubs
@using Microsoft.AspNetCore.SignalR.Client
@implements IAsyncDisposable

<PageTitle>Dashboard — Fish Orchestrator</PageTitle>

<h2>Dashboard</h2>

@* GPU Metrics Panel *@
<div class="card mb-3">
    <div class="card-body py-2">
        <div class="d-flex align-items-center">
            <strong class="me-3">GPU</strong>
            @if (GpuState.MemoryTotalMb > 0)
            {
                <div class="flex-grow-1 me-3">
                    <div class="progress" style="height: 20px;">
                        <div class="progress-bar bg-info" role="progressbar"
                             style="width: @((int)(100.0 * GpuState.MemoryUsedMb / GpuState.MemoryTotalMb))%">
                            @GpuState.MemoryUsedMb / @GpuState.MemoryTotalMb MB
                        </div>
                    </div>
                </div>
                <span class="text-muted">Util: @GpuState.UtilizationPercent%</span>
            }
            else
            {
                <span class="text-muted">loading...</span>
            }
        </div>
    </div>
</div>

@if (_activeModel is not null)
{
    <div class="active-banner d-flex align-items-center">
        <span class="status-dot running"></span>
        <div class="flex-grow-1">
            <strong>@_activeModel.Name</strong>
            <small class="text-muted ms-2">
                Port @_activeModel.HostPort · FP16 @(_activeModel.EnableHalf ? "enabled" : "disabled")
                @if (_activeModel.LastStartedAt.HasValue)
                {
                    <text> · Started @_activeModel.LastStartedAt.Value.ToString("g")</text>
                }
            </small>
        </div>
        <button class="btn btn-sm btn-danger" @onclick="() => RequestStop(_activeModel)">Stop</button>
    </div>
}

@if (_models.Count == 0)
{
    <div class="alert alert-info">
        No models registered. <a href="/deploy">Deploy your first model</a>.
    </div>
}
else
{
    <table class="table table-hover">
        <thead>
            <tr>
                <th>Status</th>
                <th>Name</th>
                <th>Checkpoint</th>
                <th>Port</th>
                <th>Actions</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var model in _models)
            {
                <tr>
                    <td>
                        <span class="status-dot @model.Status.ToString().ToLower()"></span>
                        <StatusBadge Status="model.Status" />
                    </td>
                    <td>@model.Name</td>
                    <td><small class="text-muted">@model.CheckpointPath</small></td>
                    <td>@model.HostPort</td>
                    <td>
                        @if (model.Status == ModelStatus.Running)
                        {
                            <button class="btn btn-sm btn-warning" @onclick="() => RequestStop(model)">Stop</button>
                        }
                        else
                        {
                            <button class="btn btn-sm btn-success" @onclick="() => RequestStart(model)"
                                    disabled="@_isOperating">Start</button>
                            <button class="btn btn-sm btn-danger ms-1" @onclick="() => RequestRemove(model)"
                                    disabled="@_isOperating">Remove</button>
                        }
                    </td>
                </tr>
            }
        </tbody>
    </table>
}

@* Log Preview *@
@if (_activeModel is not null)
{
    <div class="card mt-3">
        <div class="card-header d-flex justify-content-between align-items-center py-2"
             @onclick="() => _logExpanded = !_logExpanded" style="cursor: pointer;">
            <span>Container Logs</span>
            <span>@(_logExpanded ? "▼" : "▶")</span>
        </div>
        @if (_logExpanded)
        {
            <div class="card-body p-0">
                <pre class="bg-dark text-light p-2 mb-0" style="max-height: 250px; overflow-y: auto; font-size: 0.85em;">@foreach (var line in _logLines.TakeLast(10))
{<text>@line
</text>}</pre>
            </div>
            <div class="card-footer py-1">
                <a href="/logs" class="small text-muted">View full logs</a>
            </div>
        }
    </div>
}

@* TTS Toast *@
@if (_toast is not null)
{
    <div class="position-fixed bottom-0 end-0 p-3" style="z-index: 1050;">
        <div class="toast show @(_toast.Success ? "border-success" : "border-danger")" role="alert">
            <div class="toast-header bg-dark text-light">
                <strong class="me-auto">TTS @(_toast.Success ? "Complete" : "Failed")</strong>
                <button type="button" class="btn-close btn-close-white" @onclick="() => _toast = null"></button>
            </div>
            <div class="toast-body bg-dark text-light">
                @if (_toast.Success)
                {
                    <text>Generated in @_toast.DurationMs ms</text>
                }
                else
                {
                    <text>@_toast.Error</text>
                }
            </div>
        </div>
    </div>
}

@if (!string.IsNullOrEmpty(_error))
{
    <div class="alert alert-danger mt-3">@_error</div>
}

<ConfirmDialog @ref="_confirmDialog"
               Title="@_confirmTitle"
               Message="@_confirmMessage"
               ConfirmText="@_confirmButtonText"
               OnConfirm="_confirmAction" />

@code {
    private List<ModelProfile> _models = new();
    private ModelProfile? _activeModel;
    private bool _isOperating;
    private string? _error;
    private bool _logExpanded;
    private List<string> _logLines = new();
    private TtsNotificationEvent? _toast;
    private HubConnection? _hubConnection;
    private string? _subscribedContainerId;

    private ConfirmDialog _confirmDialog = null!;
    private string _confirmTitle = "";
    private string _confirmMessage = "";
    private string _confirmButtonText = "Confirm";
    private EventCallback _confirmAction;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await LoadModels();
        GpuState.OnChange += OnGpuChange;

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(Nav.ToAbsoluteUri("/hubs/orchestrator"))
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<List<ContainerStatusEvent>>("ReceiveContainerStatus", OnContainerStatus);
        _hubConnection.On<TtsNotificationEvent>("ReceiveTtsNotification", OnTtsNotification);
        _hubConnection.On<LogLineEvent>("ReceiveLogLine", OnLogLine);

        await _hubConnection.StartAsync();

        if (_activeModel?.ContainerId is not null)
        {
            _subscribedContainerId = _activeModel.ContainerId;
            await _hubConnection.InvokeAsync("SubscribeLogs", _subscribedContainerId);
        }
    }

    private async Task OnContainerStatus(List<ContainerStatusEvent> events)
    {
        foreach (var evt in events)
        {
            var model = _models.FirstOrDefault(m => m.Id == evt.ModelId);
            if (model is not null)
            {
                if (Enum.TryParse<ModelStatus>(evt.Status, out var status))
                    model.Status = status;
                model.LastStartedAt = evt.LastStartedAt;
            }
        }

        var newActive = _models.FirstOrDefault(m => m.Status == ModelStatus.Running);

        // Update log subscription if active model changed
        if (newActive?.ContainerId != _subscribedContainerId && _hubConnection is not null)
        {
            if (_subscribedContainerId is not null)
            {
                await _hubConnection.InvokeAsync("UnsubscribeLogs", _subscribedContainerId);
                _logLines.Clear();
            }
            _subscribedContainerId = newActive?.ContainerId;
            if (_subscribedContainerId is not null)
            {
                await _hubConnection.InvokeAsync("SubscribeLogs", _subscribedContainerId);
            }
        }

        _activeModel = newActive;
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnTtsNotification(TtsNotificationEvent notification)
    {
        _toast = notification;
        await InvokeAsync(StateHasChanged);

        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            if (_toast == notification)
            {
                _toast = null;
                await InvokeAsync(StateHasChanged);
            }
        });
    }

    private async Task OnLogLine(LogLineEvent logLine)
    {
        _logLines.Add($"[{logLine.Timestamp:HH:mm:ss}] {logLine.Line}");
        if (_logLines.Count > 100) _logLines.RemoveRange(0, _logLines.Count - 100);
        await InvokeAsync(StateHasChanged);
    }

    private void OnGpuChange()
    {
        InvokeAsync(StateHasChanged);
    }

    private async Task LoadModels()
    {
        _models = await Db.ModelProfiles
            .OrderByDescending(m => m.Status == ModelStatus.Running)
            .ThenBy(m => m.Name)
            .ToListAsync();
        _activeModel = _models.FirstOrDefault(m => m.Status == ModelStatus.Running);
    }

    private void RequestStart(ModelProfile model)
    {
        _confirmTitle = "Start Model";
        _confirmMessage = _activeModel is not null
            ? $"This will stop '{_activeModel.Name}' before starting '{model.Name}'. Continue?"
            : $"Start '{model.Name}'?";
        _confirmButtonText = "Start";
        _confirmAction = EventCallback.Factory.Create(this, () => StartModel(model));
        _confirmDialog.Show();
    }

    private void RequestStop(ModelProfile model)
    {
        _confirmTitle = "Stop Model";
        _confirmMessage = $"Stop '{model.Name}'?";
        _confirmButtonText = "Stop";
        _confirmAction = EventCallback.Factory.Create(this, () => StopModel(model));
        _confirmDialog.Show();
    }

    private void RequestRemove(ModelProfile model)
    {
        _confirmTitle = "Remove Model";
        _confirmMessage = $"Remove '{model.Name}'? This will delete the container (not the checkpoint files).";
        _confirmButtonText = "Remove";
        _confirmAction = EventCallback.Factory.Create(this, () => RemoveModel(model));
        _confirmDialog.Show();
    }

    private async Task StartModel(ModelProfile model)
    {
        _isOperating = true;
        _error = null;
        try
        {
            await Orchestrator.SwapModelAsync(model);
        }
        catch (Exception ex)
        {
            _error = $"Failed to start model: {ex.Message}";
        }
        finally
        {
            _isOperating = false;
        }
    }

    private async Task StopModel(ModelProfile model)
    {
        _isOperating = true;
        _error = null;
        try
        {
            await Orchestrator.StopModelAsync(model);
        }
        catch (Exception ex)
        {
            _error = $"Failed to stop model: {ex.Message}";
        }
        finally
        {
            _isOperating = false;
        }
    }

    private async Task RemoveModel(ModelProfile model)
    {
        _isOperating = true;
        _error = null;
        try
        {
            await Orchestrator.RemoveModelAsync(model);
            Db.ModelProfiles.Remove(model);
            await Db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _error = $"Failed to remove model: {ex.Message}";
        }
        finally
        {
            _isOperating = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        GpuState.OnChange -= OnGpuChange;
        if (_hubConnection is not null)
        {
            if (_subscribedContainerId is not null)
            {
                try { await _hubConnection.InvokeAsync("UnsubscribeLogs", _subscribedContainerId); } catch { }
            }
            await _hubConnection.DisposeAsync();
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/Dashboard.razor
git commit -m "feat: add real-time dashboard with GPU panel, log preview, and TTS toast notifications"
```

---

## Task 11: Logs Page

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Components/Pages/Logs.razor`

- [ ] **Step 1: Create Logs.razor**

Create `src/FishAudioOrchestrator.Web/Components/Pages/Logs.razor`:

```razor
@page "/logs"
@attribute [Authorize]
@inject AppDbContext Db
@inject NavigationManager Nav
@using Microsoft.EntityFrameworkCore
@using FishAudioOrchestrator.Web.Data
@using FishAudioOrchestrator.Web.Hubs
@using Microsoft.AspNetCore.SignalR.Client
@implements IAsyncDisposable

<PageTitle>Container Logs — Fish Orchestrator</PageTitle>

<h2>Container Logs</h2>

<div class="mb-3">
    <label class="form-label">Container</label>
    <select class="form-select bg-dark text-light border-secondary" style="max-width: 400px;"
            @onchange="OnContainerChanged">
        <option value="">-- Select a container --</option>
        @foreach (var model in _models)
        {
            <option value="@model.ContainerId" selected="@(model.ContainerId == _selectedContainerId)">
                @model.Name (@model.Status) @(model.ContainerId?[..12] ?? "no container")
            </option>
        }
    </select>
</div>

@if (!string.IsNullOrEmpty(_selectedContainerId))
{
    <div class="bg-dark border border-secondary rounded p-2"
         style="height: 70vh; overflow-y: auto; font-family: monospace; font-size: 0.85em;"
         @ref="_logContainer">
        @foreach (var line in _logLines)
        {
            <div class="text-light">@line</div>
        }
        @if (_logLines.Count == 0)
        {
            <div class="text-muted">Waiting for log output...</div>
        }
    </div>
}
else
{
    <div class="alert alert-info">Select a container to view its logs.</div>
}

@code {
    private List<ModelProfile> _models = new();
    private string? _selectedContainerId;
    private List<string> _logLines = new();
    private HubConnection? _hubConnection;
    private ElementReference _logContainer;

    protected override async Task OnInitializedAsync()
    {
        _models = await Db.ModelProfiles
            .Where(m => m.ContainerId != null)
            .OrderByDescending(m => m.Status == ModelStatus.Running)
            .ThenBy(m => m.Name)
            .ToListAsync();

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(Nav.ToAbsoluteUri("/hubs/orchestrator"))
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<LogLineEvent>("ReceiveLogLine", OnLogLine);
        await _hubConnection.StartAsync();

        // Auto-select running container
        var running = _models.FirstOrDefault(m => m.Status == ModelStatus.Running);
        if (running?.ContainerId is not null)
        {
            _selectedContainerId = running.ContainerId;
            await _hubConnection.InvokeAsync("SubscribeLogs", _selectedContainerId);
        }
    }

    private async Task OnContainerChanged(ChangeEventArgs e)
    {
        var newId = e.Value?.ToString();

        if (_hubConnection is not null && _selectedContainerId is not null)
        {
            await _hubConnection.InvokeAsync("UnsubscribeLogs", _selectedContainerId);
        }

        _logLines.Clear();
        _selectedContainerId = string.IsNullOrEmpty(newId) ? null : newId;

        if (_hubConnection is not null && _selectedContainerId is not null)
        {
            await _hubConnection.InvokeAsync("SubscribeLogs", _selectedContainerId);
        }
    }

    private async Task OnLogLine(LogLineEvent logLine)
    {
        _logLines.Add($"[{logLine.Timestamp:HH:mm:ss}] {logLine.Line}");
        if (_logLines.Count > 5000) _logLines.RemoveRange(0, _logLines.Count - 5000);
        await InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
        {
            if (_selectedContainerId is not null)
            {
                try { await _hubConnection.InvokeAsync("UnsubscribeLogs", _selectedContainerId); } catch { }
            }
            await _hubConnection.DisposeAsync();
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/Logs.razor
git commit -m "feat: add full container log viewer page with auto-select and live streaming"
```

---

## Task 12: TTS Playground Hub Integration

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Components/Pages/TtsPlayground.razor`

- [ ] **Step 1: Add hub listeners to TtsPlayground**

Read the current file first. Add the following changes:

Add usings and interface:
```razor
@using FishAudioOrchestrator.Web.Hubs
@using Microsoft.AspNetCore.SignalR.Client
@implements IAsyncDisposable
```

Add `@inject NavigationManager Nav` if not already present.

In the `@code` block, add hub connection setup in `OnInitializedAsync` after the existing logic:

```csharp
private HubConnection? _hubConnection;

// Add at end of OnInitializedAsync:
_hubConnection = new HubConnectionBuilder()
    .WithUrl(Nav.ToAbsoluteUri("/hubs/orchestrator"))
    .WithAutomaticReconnect()
    .Build();

_hubConnection.On<List<ContainerStatusEvent>>("ReceiveContainerStatus", async events =>
{
    var runningEvent = events.FirstOrDefault(e => e.Status == "Running");
    if (runningEvent is null && _activeModel is not null)
    {
        _activeModel = null;
        await InvokeAsync(StateHasChanged);
    }
    else if (runningEvent is not null && _activeModel is not null && runningEvent.ModelId != _activeModel.Id)
    {
        _activeModel = await Db.ModelProfiles.FirstOrDefaultAsync(m => m.Id == runningEvent.ModelId);
        await InvokeAsync(StateHasChanged);
    }
});

await _hubConnection.StartAsync();
```

Add dispose:
```csharp
public async ValueTask DisposeAsync()
{
    if (_hubConnection is not null)
        await _hubConnection.DisposeAsync();
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/TtsPlayground.razor
git commit -m "feat: add real-time container status updates to TTS Playground"
```

---

## Task 13: Add SignalR Client Package to Web Project

**IMPORTANT:** This task MUST be completed before Tasks 10-12 (Dashboard, Logs, TtsPlayground) since they use `HubConnectionBuilder` from this package. Execute this task immediately after Task 9, or bundle it with Task 6.

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/FishAudioOrchestrator.Web.csproj`

- [ ] **Step 1: Add SignalR client package**

The Dashboard, Logs, and TtsPlayground pages use `HubConnectionBuilder` which requires the SignalR client package:

Run:
```bash
cd src/FishAudioOrchestrator.Web
dotnet add package Microsoft.AspNetCore.SignalR.Client --version 9.0.3
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 3: Run all tests**

Run: `dotnet test --nologo -v q`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/FishAudioOrchestrator.Web/FishAudioOrchestrator.Web.csproj
git commit -m "chore: add Microsoft.AspNetCore.SignalR.Client package"
```

**Note:** This task should be executed BEFORE Tasks 10-12 if the build fails due to missing `HubConnectionBuilder`. Alternatively, the implementer can reorder to add this package first.

---

## Task 14: Full Integration Verification

- [ ] **Step 1: Run entire test suite**

Run: `dotnet test --nologo -v q`
Expected: All tests pass (~74+ tests).

- [ ] **Step 2: Verify clean Release build**

Run: `dotnet build -c Release --nologo -v q`
Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Tag the release**

```bash
git tag v0.5.0-phase5
```

- [ ] **Step 4: Push tag (only if user confirms)**

```bash
git push origin v0.5.0-phase5
```
