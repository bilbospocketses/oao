> **Note:** post-rename. References to `OpenAudioOrchestrator.*` paths and
> config keys reflect their original names. Current equivalents are
> `oao.*` and `oao:*`.
# Phase 5: Real-Time SignalR Dashboard Design

## Overview

Add real-time updates to the Fish Audio Orchestration Dashboard using a single SignalR hub. Replace the current poll-on-load dashboard with live container status, GPU metrics, TTS generation notifications, and Docker container log streaming.

## SignalR Hub

Single hub at `/hubs/orchestrator`, protected by `[Authorize]` (all authenticated users).

### Server → Client Events

| Method | Payload | Trigger |
|--------|---------|---------|
| `ReceiveContainerStatus` | `{ ModelId, Name, Status, HostPort, LastStartedAt }` | Health check cycle (30s) or container operation (start/stop/swap/remove) |
| `ReceiveGpuMetrics` | `{ MemoryUsedMb, MemoryTotalMb, UtilizationPercent }` | Health check cycle (30s) via nvidia-smi |
| `ReceiveTtsNotification` | `{ UserId, Text (truncated to 50 chars), OutputFileName, DurationMs, Success, Error? }` | TTS generation completes (success or failure) |
| `ReceiveLogLine` | `{ ContainerId, Timestamp, Line }` | Docker container log stream (follow mode) |

### Client → Server Methods

| Method | Purpose |
|--------|---------|
| `SubscribeLogs(containerId)` | Start streaming logs for a container |
| `UnsubscribeLogs(containerId)` | Stop streaming logs for a container |

Status, GPU metrics, and TTS notifications are broadcast to all connected clients automatically — no subscription needed. Log streaming requires explicit subscribe/unsubscribe per container.

### Connection Lifecycle

- `OnConnectedAsync`: No special setup needed. Client receives broadcasts immediately.
- `OnDisconnectedAsync`: Clean up any log subscriptions for the disconnected client. If the client was the last subscriber for a container's log stream, cancel the stream.

## Backend Services

### HealthMonitorService Changes

Extend the existing `HealthMonitorService` to push real-time updates after each health check cycle:

1. After checking container health, push `ReceiveContainerStatus` for all model profiles via `IHubContext<OrchestratorHub>`
2. Run `nvidia-smi --query-gpu=memory.used,memory.total,utilization.gpu --format=csv,noheader,nounits` and push `ReceiveGpuMetrics`
3. GPU metrics parsing extracted to a testable static method

This replaces the per-client nvidia-smi call currently in NavMenu.

### New ContainerLogService

Manages Docker log streams with shared subscriptions:

- `SubscribeAsync(containerId, connectionId)` — if no stream exists for this container, start one using `docker.Containers.GetContainerLogsAsync` with `Follow=true, ShowStdout=true, ShowStderr=true, Tail="50"`. Add the connection to the subscriber set.
- `UnsubscribeAsync(containerId, connectionId)` — remove the connection from the subscriber set. If no subscribers remain, cancel the stream's `CancellationToken`.
- `UnsubscribeAllAsync(connectionId)` — remove the connection from all container subscriptions. Called from `OnDisconnectedAsync`.

Subscription tracking: `ConcurrentDictionary<string, ContainerLogStream>` where `ContainerLogStream` contains the `CancellationTokenSource`, the `Task` running the stream reader, and a `HashSet<string>` of subscribed connection IDs (synchronized with a lock).

Log lines are read from Docker's multiplexed stream, parsed for timestamp and content, and pushed to subscribed clients via `IHubContext<OrchestratorHub>`.

### New GpuMetricsState Service

Singleton service holding the latest GPU metrics. Updated by `HealthMonitorService` after each nvidia-smi call. Exposes an `OnChange` event that components (NavMenu, Dashboard) subscribe to for reactive updates. Properties: `MemoryUsedMb`, `MemoryTotalMb`, `UtilizationPercent`, `LastUpdated`.

### DockerOrchestratorService Changes

After each container operation (start, stop, swap, remove), push an immediate `ReceiveContainerStatus` update for all models via `IHubContext<OrchestratorHub>`. This provides instant feedback without waiting for the next health check cycle.

### TtsClientService Changes

After a generation completes (success or failure), push a `ReceiveTtsNotification` via `IHubContext<OrchestratorHub>` to all connected clients. The notification includes the user ID so clients can highlight their own generations.

## Dashboard UI Changes

### Dashboard Page (`/`)

Replace poll-on-load with real-time hub updates:

- Connect to `OrchestratorHub` on page initialization
- `ReceiveContainerStatus` handler: update the model list and active model banner in-place, call `StateHasChanged()`
- GPU metrics panel at the top of the page: memory used/total with progress bar, utilization percentage. Updates on `ReceiveGpuMetrics`. Shows "GPU: loading..." until first push arrives.
- Collapsible log preview panel below the model table: last 10 lines from the running container. Auto-subscribes to logs when a running container exists. Auto-unsubscribes on container stop or page disposal.
- TTS notification toast: on `ReceiveTtsNotification`, show a brief dismissible toast ("Generation complete: 1.2s" or "Generation failed: {error}").

Still loads initial data from DB on page init for immediate render, then switches to hub updates.

### NavMenu Changes

Remove the `nvidia-smi` process call from NavMenu. GPU info now comes from a shared `GpuMetricsState` service (registered as singleton) that is updated by `HealthMonitorService`. NavMenu injects this service and reads the latest values. The service implements `INotifyPropertyChanged` or exposes an `OnChange` event so NavMenu can call `StateHasChanged()` when metrics update. This avoids NavMenu needing its own hub connection.

### New Logs Page (`/logs`)

Full container log viewer at `/logs`, protected by `[Authorize]`:

- Container selector dropdown populated from model profiles
- Auto-selects the running container if one exists
- On selection: subscribes to log stream, receives backfill (last 50 lines) then live lines
- On container change: unsubscribes from previous, subscribes to new
- Scrollable log output area: monospace font, auto-scrolls to bottom, pauses auto-scroll when user scrolls up
- Each line shows timestamp and content
- Dispose: unsubscribes from current stream on navigation away

### TTS Playground Changes

Minor additions to the existing page:

- Listen for `ReceiveContainerStatus`: if the running model stops/errors while user is on the page, immediately show the warning banner instead of requiring a page refresh
- Listen for `ReceiveTtsNotification`: no change to existing result display, but other users on the dashboard see the toast notification

## Program.cs Changes

```
builder.Services.AddSignalR();
builder.Services.AddSingleton<IContainerLogService, ContainerLogService>();
builder.Services.AddSingleton<GpuMetricsState>();
...
app.MapHub<OrchestratorHub>("/hubs/orchestrator");
```

`ContainerLogService` is registered as singleton since it manages shared state across connections.

## Configuration

No new configuration. GPU and status push frequency is tied to the existing `HealthCheckIntervalSeconds` (30s) in appsettings.json.

## Packages

No new NuGet packages. SignalR is built into ASP.NET Core. Docker.DotNet's `GetContainerLogsAsync` is already available.

## Testing Strategy

- Unit tests for `ContainerLogService`: subscription add/remove, stream lifecycle (start on first subscriber, cancel on last unsubscribe), concurrent access
- Unit tests for GPU metrics parsing: valid output, missing nvidia-smi, malformed output
- Unit tests for hub event dispatching: verify `HealthMonitorService` calls correct hub methods with correct payloads after health check
- Unit tests for `DockerOrchestratorService` hub notifications: verify container operations push status updates
- Integration test: verify hub requires authentication (anonymous connection rejected)
- Extends existing test suite (~64 tests)
