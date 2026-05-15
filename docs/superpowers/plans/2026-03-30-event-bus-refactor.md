> **Note:** post-rename. References to `OpenAudioOrchestrator.*` paths and
> config keys reflect their original names. Current equivalents are
> `oao.*` and `oao:*`.
# Event Bus Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace client-side SignalR `HubConnection` in Blazor Server components with a server-side `OrchestratorEventBus`, restoring `[Authorize]` on the hub.

**Architecture:** A singleton `OrchestratorEventBus` service with C# events that services raise and components subscribe to. `ContainerLogService` gains an event-based callback for in-process subscribers (keyed by a GUID, not a SignalR connection ID). The hub keeps `[Authorize]` and remains available for future external clients but is no longer used by Blazor components.

**Tech Stack:** .NET 9, Blazor Server, SignalR (hub-side only), C# events

---

## File Structure

| Action | File | Responsibility |
|--------|------|---------------|
| Create | `Services/OrchestratorEventBus.cs` | Singleton event bus: raises events, manages log subscriptions |
| Modify | `Services/ContainerLogService.cs` | Add callback-based subscriber path alongside SignalR |
| Modify | `Services/IContainerLogService.cs` | Add callback overload for subscribe |
| Modify | `Services/HealthMonitorService.cs` | Raise events on bus in addition to hub |
| Modify | `Services/DockerOrchestratorService.cs` | Raise events on bus in addition to hub |
| Modify | `Services/TtsClientService.cs` | Raise events on bus in addition to hub |
| Modify | `Hubs/OrchestratorHub.cs` | Restore `[Authorize]` |
| Modify | `Components/Pages/Dashboard.razor` | Subscribe to event bus, remove HubConnection |
| Modify | `Components/Pages/Logs.razor` | Subscribe to event bus, remove HubConnection |
| Modify | `Components/Pages/TtsPlayground.razor` | Subscribe to event bus, remove HubConnection |
| Modify | `Program.cs` | Register `OrchestratorEventBus` as singleton, remove `AddHttpContextAccessor` |

---

### Task 1: Create OrchestratorEventBus

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Services/OrchestratorEventBus.cs`

- [ ] **Step 1: Create the event bus class**

```csharp
using FishAudioOrchestrator.Web.Hubs;

namespace FishAudioOrchestrator.Web.Services;

public class OrchestratorEventBus
{
    public event Action<List<ContainerStatusEvent>>? OnContainerStatus;
    public event Action<TtsNotificationEvent>? OnTtsNotification;
    public event Action<LogLineEvent>? OnLogLine;
    public event Action<GpuMetricsEvent>? OnGpuMetrics;

    public void RaiseContainerStatus(List<ContainerStatusEvent> events)
        => OnContainerStatus?.Invoke(events);

    public void RaiseTtsNotification(TtsNotificationEvent notification)
        => OnTtsNotification?.Invoke(notification);

    public void RaiseLogLine(LogLineEvent logLine)
        => OnLogLine?.Invoke(logLine);

    public void RaiseGpuMetrics(GpuMetricsEvent metrics)
        => OnGpuMetrics?.Invoke(metrics);
}
```

- [ ] **Step 2: Register in Program.cs**

In `Program.cs`, add after the existing `builder.Services.AddSingleton<GpuMetricsState>()` line:

```csharp
builder.Services.AddSingleton<OrchestratorEventBus>();
```

Also remove the `builder.Services.AddHttpContextAccessor()` line (no longer needed).

Add the using at the top of Program.cs if not already present:
```csharp
using FishAudioOrchestrator.Web.Services;
```
(It's already there via the other service registrations.)

- [ ] **Step 3: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Services/OrchestratorEventBus.cs src/FishAudioOrchestrator.Web/Program.cs
git commit -m "feat: add OrchestratorEventBus singleton service"
```

---

### Task 2: Update ContainerLogService to support callback-based subscribers

The log service currently tracks subscribers by SignalR connection ID and sends via `IHubContext`. We need to also support in-process callback subscribers so Blazor components can receive log lines directly.

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Services/IContainerLogService.cs`
- Modify: `src/FishAudioOrchestrator.Web/Services/ContainerLogService.cs`

- [ ] **Step 1: Add callback subscribe/unsubscribe to the interface**

Replace the full contents of `IContainerLogService.cs`:

```csharp
using FishAudioOrchestrator.Web.Hubs;

namespace FishAudioOrchestrator.Web.Services;

public interface IContainerLogService
{
    Task SubscribeAsync(string containerId, string connectionId);
    Task UnsubscribeAsync(string containerId, string connectionId);
    Task UnsubscribeAllAsync(string connectionId);
    bool HasSubscribers(string containerId);

    void SubscribeCallback(string containerId, string subscriberId, Action<LogLineEvent> callback);
    void UnsubscribeCallback(string containerId, string subscriberId);
    void UnsubscribeAllCallbacks(string subscriberId);
}
```

- [ ] **Step 2: Add callback storage and dispatch to ContainerLogService**

In `ContainerLogService.cs`, add a field after the existing `_streams` field:

```csharp
private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Action<LogLineEvent>>> _callbackSubscribers = new();
```

Add the three new methods at the end of the class (before the `ReadLogStreamAsync` method):

```csharp
public void SubscribeCallback(string containerId, string subscriberId, Action<LogLineEvent> callback)
{
    var subs = _callbackSubscribers.GetOrAdd(containerId, _ => new ConcurrentDictionary<string, Action<LogLineEvent>>());
    subs[subscriberId] = callback;

    // Ensure the Docker log reader is running for this container
    var stream = _streams.GetOrAdd(containerId, _ => new ContainerLogStream());
    lock (stream.Lock)
    {
        if (stream.ReaderTask is null || stream.ReaderTask.IsCompleted)
        {
            stream.Cts = new CancellationTokenSource();
            stream.ReaderTask = Task.Run(() => ReadLogStreamAsync(containerId, stream));
        }
    }
}

public void UnsubscribeCallback(string containerId, string subscriberId)
{
    if (_callbackSubscribers.TryGetValue(containerId, out var subs))
    {
        subs.TryRemove(subscriberId, out _);
        if (subs.IsEmpty)
        {
            _callbackSubscribers.TryRemove(containerId, out _);
        }
    }

    // If no subscribers of either kind remain, stop the reader
    if (!HasSubscribers(containerId) && !HasCallbackSubscribers(containerId))
    {
        if (_streams.TryRemove(containerId, out var stream))
        {
            stream.Cts?.Cancel();
        }
    }
}

public void UnsubscribeAllCallbacks(string subscriberId)
{
    foreach (var kvp in _callbackSubscribers)
    {
        kvp.Value.TryRemove(subscriberId, out _);
        if (kvp.Value.IsEmpty)
        {
            _callbackSubscribers.TryRemove(kvp.Key, out _);
        }
    }
}

private bool HasCallbackSubscribers(string containerId)
{
    return _callbackSubscribers.TryGetValue(containerId, out var subs) && !subs.IsEmpty;
}
```

- [ ] **Step 3: Dispatch log lines to callback subscribers in ReadLogStreamAsync**

In `ReadLogStreamAsync`, after the existing block that sends via `_hub.Clients.Clients(subscribers)`, add callback dispatch. Find this section:

```csharp
if (subscribers.Length > 0)
{
    await _hub.Clients.Clients(subscribers)
        .SendAsync("ReceiveLogLine", logEvent, logStream.Cts.Token);
}
```

Add immediately after:

```csharp
// Dispatch to in-process callback subscribers
if (_callbackSubscribers.TryGetValue(containerId, out var callbackSubs))
{
    foreach (var cb in callbackSubs.Values)
    {
        try { cb(logEvent); } catch { }
    }
}
```

- [ ] **Step 4: Update HasSubscribers to include callback subscribers**

Replace the existing `HasSubscribers` method:

```csharp
public bool HasSubscribers(string containerId)
{
    if (_streams.TryGetValue(containerId, out var stream))
    {
        lock (stream.Lock)
        {
            if (stream.Subscribers.Count > 0) return true;
        }
    }

    return HasCallbackSubscribers(containerId);
}
```

- [ ] **Step 5: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Services/IContainerLogService.cs src/FishAudioOrchestrator.Web/Services/ContainerLogService.cs
git commit -m "feat: add callback-based log subscriptions to ContainerLogService"
```

---

### Task 3: Wire services to raise events on the bus

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Services/HealthMonitorService.cs`
- Modify: `src/FishAudioOrchestrator.Web/Services/DockerOrchestratorService.cs`
- Modify: `src/FishAudioOrchestrator.Web/Services/TtsClientService.cs`

- [ ] **Step 1: Update HealthMonitorService**

Add `OrchestratorEventBus` to the constructor. Add a field:

```csharp
private readonly OrchestratorEventBus _eventBus;
```

Update the constructor signature to include `OrchestratorEventBus eventBus` and assign `_eventBus = eventBus;`.

The full constructor becomes:

```csharp
public HealthMonitorService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<HealthMonitorService> logger,
    IHubContext<OrchestratorHub> hub,
    GpuMetricsState gpuState,
    OrchestratorEventBus eventBus)
{
    _scopeFactory = scopeFactory;
    _intervalSeconds = int.Parse(
        config["FishOrchestrator:HealthCheckIntervalSeconds"] ?? "30");
    _logger = logger;
    _hub = hub;
    _gpuState = gpuState;
    _eventBus = eventBus;
}
```

In `CheckHealthAsync`, after the existing `SendAsync("ReceiveContainerStatus", statusEvents)` line, add:

```csharp
_eventBus.RaiseContainerStatus(statusEvents);
```

After the existing `SendAsync("ReceiveGpuMetrics", gpuMetrics)` line, add:

```csharp
_eventBus.RaiseGpuMetrics(gpuMetrics);
```

- [ ] **Step 2: Update DockerOrchestratorService**

Add `OrchestratorEventBus` to the constructor. Add a field:

```csharp
private readonly OrchestratorEventBus _eventBus;
```

Update the constructor to include `OrchestratorEventBus eventBus` and assign `_eventBus = eventBus;`.

The full constructor becomes:

```csharp
public DockerOrchestratorService(
    IDockerClient docker,
    IContainerConfigService configService,
    AppDbContext context,
    FishProxyConfigProvider proxyProvider,
    IDockerNetworkService networkService,
    IHubContext<OrchestratorHub> hub,
    OrchestratorEventBus eventBus)
{
    _docker = docker;
    _configService = configService;
    _context = context;
    _proxyProvider = proxyProvider;
    _networkService = networkService;
    _hub = hub;
    _eventBus = eventBus;
}
```

In `PushStatusUpdateAsync`, after the existing `SendAsync("ReceiveContainerStatus", statusEvents)` line, add:

```csharp
_eventBus.RaiseContainerStatus(statusEvents);
```

- [ ] **Step 3: Update TtsClientService**

Add `OrchestratorEventBus` to the constructor. Add a field:

```csharp
private readonly OrchestratorEventBus _eventBus;
```

Update the constructor to include `OrchestratorEventBus eventBus` and assign `_eventBus = eventBus;`.

The full constructor becomes:

```csharp
public TtsClientService(HttpClient httpClient, IConfiguration config, AppDbContext context, IHubContext<OrchestratorHub> hub, OrchestratorEventBus eventBus)
{
    _httpClient = httpClient;
    var dataRoot = config["FishOrchestrator:DataRoot"]!;
    _outputPath = Path.Combine(dataRoot, "Output");
    _context = context;
    _hub = hub;
    _eventBus = eventBus;
}
```

In `GenerateAsync`, after the success `SendAsync("ReceiveTtsNotification", notification)` line, add:

```csharp
_eventBus.RaiseTtsNotification(notification);
```

After the failure `SendAsync("ReceiveTtsNotification", failNotification)` line, add:

```csharp
_eventBus.RaiseTtsNotification(failNotification);
```

- [ ] **Step 4: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Services/HealthMonitorService.cs src/FishAudioOrchestrator.Web/Services/DockerOrchestratorService.cs src/FishAudioOrchestrator.Web/Services/TtsClientService.cs
git commit -m "feat: wire services to raise events on OrchestratorEventBus"
```

---

### Task 4: Refactor Dashboard.razor to use event bus

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Components/Pages/Dashboard.razor`

- [ ] **Step 1: Replace HubConnection with event bus**

At the top of the file, remove these lines:
```razor
@using FishAudioOrchestrator.Web.Hubs
@using Microsoft.AspNetCore.SignalR.Client
```

Replace them with:
```razor
@using FishAudioOrchestrator.Web.Hubs
```

(Keep the Hubs using for the event types, remove SignalR.Client.)

In the `@code` block, remove these fields:
```csharp
private HubConnection? _hubConnection;
private string? _subscribedContainerId;
```

Replace with:
```csharp
private string? _subscribedContainerId;
private readonly string _subscriberId = Guid.NewGuid().ToString();
```

Add injections at the top of the `@code` block (or use `@inject` directives):

Add these `@inject` lines near the existing ones at the top of the file:
```razor
@inject OrchestratorEventBus EventBus
@inject IContainerLogService LogService
```

- [ ] **Step 2: Rewrite OnInitializedAsync**

Replace the entire `OnInitializedAsync` method:

```csharp
protected override async Task OnInitializedAsync()
{
    await LoadModels();
    GpuState.OnChange += OnGpuChange;

    EventBus.OnContainerStatus += OnContainerStatusRaw;
    EventBus.OnTtsNotification += OnTtsNotificationRaw;
    EventBus.OnLogLine += OnLogLineRaw;

    if (_activeModel?.ContainerId is not null)
    {
        _subscribedContainerId = _activeModel.ContainerId;
        LogService.SubscribeCallback(_subscribedContainerId, _subscriberId, OnLogLineRaw);
    }
}
```

- [ ] **Step 3: Add raw event handlers that marshal to the Blazor sync context**

Replace the existing `OnContainerStatus`, `OnTtsNotification`, and `OnLogLine` methods with these:

```csharp
private void OnContainerStatusRaw(List<ContainerStatusEvent> events)
{
    _ = InvokeAsync(() => OnContainerStatus(events));
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

    if (newActive?.ContainerId != _subscribedContainerId)
    {
        if (_subscribedContainerId is not null)
        {
            LogService.UnsubscribeCallback(_subscribedContainerId, _subscriberId);
            _logLines.Clear();
        }
        _subscribedContainerId = newActive?.ContainerId;
        if (_subscribedContainerId is not null)
        {
            LogService.SubscribeCallback(_subscribedContainerId, _subscriberId, OnLogLineRaw);
        }
    }

    _activeModel = newActive;
    StateHasChanged();
}

private void OnTtsNotificationRaw(TtsNotificationEvent notification)
{
    _ = InvokeAsync(() => OnTtsNotification(notification));
}

private async Task OnTtsNotification(TtsNotificationEvent notification)
{
    _toast = notification;
    StateHasChanged();

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

private void OnLogLineRaw(LogLineEvent logLine)
{
    _ = InvokeAsync(() =>
    {
        _logLines.Add($"[{logLine.Timestamp:HH:mm:ss}] {logLine.Line}");
        if (_logLines.Count > 100) _logLines.RemoveRange(0, _logLines.Count - 100);
        StateHasChanged();
    });
}
```

- [ ] **Step 4: Update DisposeAsync**

Replace the existing `DisposeAsync`:

```csharp
public ValueTask DisposeAsync()
{
    GpuState.OnChange -= OnGpuChange;
    EventBus.OnContainerStatus -= OnContainerStatusRaw;
    EventBus.OnTtsNotification -= OnTtsNotificationRaw;
    EventBus.OnLogLine -= OnLogLineRaw;

    if (_subscribedContainerId is not null)
    {
        LogService.UnsubscribeCallback(_subscribedContainerId, _subscriberId);
    }

    return ValueTask.CompletedTask;
}
```

- [ ] **Step 5: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/Dashboard.razor
git commit -m "refactor: Dashboard uses event bus instead of HubConnection"
```

---

### Task 5: Refactor Logs.razor to use event bus

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Components/Pages/Logs.razor`

- [ ] **Step 1: Replace the full file contents**

Replace the entire `Logs.razor` with:

```razor
@page "/logs"
@attribute [Authorize]
@inject AppDbContext Db
@inject IContainerLogService LogService
@using Microsoft.EntityFrameworkCore
@using FishAudioOrchestrator.Web.Data
@using FishAudioOrchestrator.Web.Hubs
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
         style="height: 70vh; overflow-y: auto; font-family: monospace; font-size: 0.85em;">
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
    private readonly string _subscriberId = Guid.NewGuid().ToString();

    protected override async Task OnInitializedAsync()
    {
        _models = await Db.ModelProfiles
            .Where(m => m.ContainerId != null)
            .OrderByDescending(m => m.Status == ModelStatus.Running)
            .ThenBy(m => m.Name)
            .ToListAsync();

        var running = _models.FirstOrDefault(m => m.Status == ModelStatus.Running);
        if (running?.ContainerId is not null)
        {
            _selectedContainerId = running.ContainerId;
            LogService.SubscribeCallback(_selectedContainerId, _subscriberId, OnLogLineRaw);
        }
    }

    private void OnContainerChanged(ChangeEventArgs e)
    {
        var newId = e.Value?.ToString();

        if (_selectedContainerId is not null)
        {
            LogService.UnsubscribeCallback(_selectedContainerId, _subscriberId);
        }

        _logLines.Clear();
        _selectedContainerId = string.IsNullOrEmpty(newId) ? null : newId;

        if (_selectedContainerId is not null)
        {
            LogService.SubscribeCallback(_selectedContainerId, _subscriberId, OnLogLineRaw);
        }
    }

    private void OnLogLineRaw(LogLineEvent logLine)
    {
        _ = InvokeAsync(() =>
        {
            _logLines.Add($"[{logLine.Timestamp:HH:mm:ss}] {logLine.Line}");
            if (_logLines.Count > 5000) _logLines.RemoveRange(0, _logLines.Count - 5000);
            StateHasChanged();
        });
    }

    public ValueTask DisposeAsync()
    {
        if (_selectedContainerId is not null)
        {
            LogService.UnsubscribeCallback(_selectedContainerId, _subscriberId);
        }

        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/Logs.razor
git commit -m "refactor: Logs page uses event bus instead of HubConnection"
```

---

### Task 6: Refactor TtsPlayground.razor to use event bus

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Components/Pages/TtsPlayground.razor`

- [ ] **Step 1: Update imports and injections**

At the top, remove:
```razor
@using Microsoft.AspNetCore.SignalR.Client
@inject NavigationManager Nav
```

Add:
```razor
@inject OrchestratorEventBus EventBus
```

Keep the existing `@using FishAudioOrchestrator.Web.Hubs` for the event types.

- [ ] **Step 2: Replace hub code in @code block**

Remove the field:
```csharp
private HubConnection? _hubConnection;
```

Replace `OnInitializedAsync`:

```csharp
protected override async Task OnInitializedAsync()
{
    _activeModel = await Db.ModelProfiles
        .FirstOrDefaultAsync(m => m.Status == ModelStatus.Running);
    _voices = await VoiceService.ListVoicesAsync();

    EventBus.OnContainerStatus += OnContainerStatusRaw;
}
```

Add the event handler:

```csharp
private void OnContainerStatusRaw(List<ContainerStatusEvent> events)
{
    _ = InvokeAsync(async () =>
    {
        var runningEvent = events.FirstOrDefault(e => e.Status == "Running");
        if (runningEvent is null && _activeModel is not null)
        {
            _activeModel = null;
            StateHasChanged();
        }
        else if (runningEvent is not null && _activeModel is not null && runningEvent.ModelId != _activeModel.Id)
        {
            _activeModel = await Db.ModelProfiles.FirstOrDefaultAsync(m => m.Id == runningEvent.ModelId);
            StateHasChanged();
        }
    });
}
```

Replace `DisposeAsync`:

```csharp
public ValueTask DisposeAsync()
{
    EventBus.OnContainerStatus -= OnContainerStatusRaw;
    return ValueTask.CompletedTask;
}
```

- [ ] **Step 3: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/TtsPlayground.razor
git commit -m "refactor: TtsPlayground uses event bus instead of HubConnection"
```

---

### Task 7: Restore [Authorize] on hub and clean up

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Hubs/OrchestratorHub.cs`
- Modify: `src/FishAudioOrchestrator.Web/Program.cs`

- [ ] **Step 1: Restore [Authorize] on the hub**

In `OrchestratorHub.cs`, add back the using and attribute:

```csharp
using FishAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FishAudioOrchestrator.Web.Hubs;

[Authorize]
public class OrchestratorHub : Hub
```

- [ ] **Step 2: Remove AddHttpContextAccessor from Program.cs**

In `Program.cs`, remove the line:
```csharp
builder.Services.AddHttpContextAccessor();
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build src/FishAudioOrchestrator.Web`
Expected: 0 errors, 0 warnings (CS1998 warning acceptable)

- [ ] **Step 4: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Hubs/OrchestratorHub.cs src/FishAudioOrchestrator.Web/Program.cs
git commit -m "refactor: restore [Authorize] on hub, remove HttpContextAccessor"
```

---

### Task 8: Manual smoke test

- [ ] **Step 1: Run the app**

Run: `dotnet run --project src/FishAudioOrchestrator.Web`

- [ ] **Step 2: Verify these behaviors**

1. Login works (redirects to dashboard)
2. Dashboard loads without errors in console
3. Navigate to Logs page — no 401 errors
4. Navigate to TTS Playground — no 401 errors
5. Navigate to History — loads correctly
6. Navigate between pages repeatedly — no circuit crashes
7. Sign out works from account page
8. After sign out, cannot access dashboard (redirected to login)

- [ ] **Step 3: Push all changes**

```bash
git push origin master
```
