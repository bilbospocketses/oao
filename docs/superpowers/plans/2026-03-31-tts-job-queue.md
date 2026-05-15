> **Note:** post-rename. References to `OpenAudioOrchestrator.*` paths and
> config keys reflect their original names. Current equivalents are
> `oao.*` and `oao:*`.
# TTS Background Job Queue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace synchronous TTS generation with a background job queue so users can submit requests and navigate freely.

**Architecture:** A `TtsJob` entity persisted to SQLite tracks each request. A `TtsJobProcessor` BackgroundService processes jobs serially using existing `TtsClientService`. The TTS Playground submits jobs and shows their status via `OrchestratorEventBus` events. Completed jobs become `GenerationLog` entries in History.

**Tech Stack:** .NET 9, Blazor Server, EF Core (SQLite), BackgroundService

---

## File Structure

| Action | File | Responsibility |
|--------|------|---------------|
| Create | `Data/Entities/TtsJob.cs` | Job entity + status enum |
| Create | `Services/TtsJobProcessor.cs` | Background service: serial queue processor |
| Modify | `Data/AppDbContext.cs` | Add `DbSet<TtsJob>` + entity config |
| Modify | `Hubs/HubEvents.cs` | Add `TtsJobStatusEvent` record |
| Modify | `Services/OrchestratorEventBus.cs` | Add `OnTtsJobStatus` event |
| Modify | `Program.cs` | Register `TtsJobProcessor` |
| Modify | `Components/Pages/TtsPlayground.razor` | Job submission + status table |
| Generate | `Data/Migrations/[timestamp]_AddTtsJobs.cs` | EF migration |

---

### Task 1: Create TtsJob entity and update DbContext

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Data/Entities/TtsJob.cs`
- Modify: `src/FishAudioOrchestrator.Web/Data/AppDbContext.cs`

- [ ] **Step 1: Create the TtsJob entity**

Create `src/FishAudioOrchestrator.Web/Data/Entities/TtsJob.cs`:

```csharp
namespace FishAudioOrchestrator.Web.Data.Entities;

public enum TtsJobStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}

public class TtsJob
{
    public int Id { get; set; }
    public string? UserId { get; set; }
    public int ModelProfileId { get; set; }
    public int? ReferenceVoiceId { get; set; }
    public required string InputText { get; set; }
    public required string Format { get; set; }
    public string? ReferenceId { get; set; }
    public required string OutputFileName { get; set; }
    public TtsJobStatus Status { get; set; } = TtsJobStatus.Queued;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ModelProfile ModelProfile { get; set; } = null!;
    public ReferenceVoice? ReferenceVoice { get; set; }
    public AppUser? User { get; set; }
}
```

- [ ] **Step 2: Add DbSet and entity configuration to AppDbContext**

In `src/FishAudioOrchestrator.Web/Data/AppDbContext.cs`, add the DbSet after the existing `GenerationLogs` line:

```csharp
public DbSet<TtsJob> TtsJobs => Set<TtsJob>();
```

Add entity configuration inside `OnModelCreating`, after the `GenerationLog` configuration block (before the `AppUser` block):

```csharp
modelBuilder.Entity<TtsJob>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.InputText).IsRequired();
    entity.Property(e => e.OutputFileName).IsRequired().HasMaxLength(255);
    entity.Property(e => e.Format).IsRequired().HasMaxLength(10);
    entity.Property(e => e.Status)
        .HasConversion<string>()
        .HasMaxLength(20);
    entity.HasOne(e => e.ModelProfile)
        .WithMany()
        .HasForeignKey(e => e.ModelProfileId)
        .OnDelete(DeleteBehavior.Cascade);
    entity.HasOne(e => e.ReferenceVoice)
        .WithMany()
        .HasForeignKey(e => e.ReferenceVoiceId)
        .OnDelete(DeleteBehavior.SetNull);
    entity.HasOne(e => e.User)
        .WithMany()
        .HasForeignKey(e => e.UserId)
        .OnDelete(DeleteBehavior.SetNull);
});
```

- [ ] **Step 3: Generate EF migration**

Run:
```bash
dotnet ef migrations add AddTtsJobs --project src/FishAudioOrchestrator.Web
```

If `dotnet ef` is not installed:
```bash
dotnet tool install --global dotnet-ef
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build src/FishAudioOrchestrator.Web`
Expected: 0 errors

- [ ] **Step 5: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Data/Entities/TtsJob.cs src/FishAudioOrchestrator.Web/Data/AppDbContext.cs src/FishAudioOrchestrator.Web/Data/Migrations/
git commit -m "feat: add TtsJob entity and migration"
```

---

### Task 2: Add TtsJobStatusEvent to event bus

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Hubs/HubEvents.cs`
- Modify: `src/FishAudioOrchestrator.Web/Services/OrchestratorEventBus.cs`

- [ ] **Step 1: Add TtsJobStatusEvent record**

In `src/FishAudioOrchestrator.Web/Hubs/HubEvents.cs`, add at the end of the file:

```csharp
public record TtsJobStatusEvent(
    int JobId,
    string Status,
    string? ErrorMessage);
```

- [ ] **Step 2: Add event to OrchestratorEventBus**

In `src/FishAudioOrchestrator.Web/Services/OrchestratorEventBus.cs`, add the event and raise method after the existing `OnGpuMetrics`:

```csharp
public event Action<TtsJobStatusEvent>? OnTtsJobStatus;

public void RaiseTtsJobStatus(TtsJobStatusEvent evt)
    => OnTtsJobStatus?.Invoke(evt);
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build src/FishAudioOrchestrator.Web`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Hubs/HubEvents.cs src/FishAudioOrchestrator.Web/Services/OrchestratorEventBus.cs
git commit -m "feat: add TtsJobStatusEvent to event bus"
```

---

### Task 3: Create TtsJobProcessor background service

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Services/TtsJobProcessor.cs`
- Modify: `src/FishAudioOrchestrator.Web/Program.cs`

- [ ] **Step 1: Create TtsJobProcessor**

Create `src/FishAudioOrchestrator.Web/Services/TtsJobProcessor.cs`:

```csharp
using System.Diagnostics;
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Hubs;
using Microsoft.EntityFrameworkCore;

namespace FishAudioOrchestrator.Web.Services;

public class TtsJobProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OrchestratorEventBus _eventBus;
    private readonly ILogger<TtsJobProcessor> _logger;
    private readonly string _outputPath;

    public TtsJobProcessor(
        IServiceScopeFactory scopeFactory,
        OrchestratorEventBus eventBus,
        ILogger<TtsJobProcessor> logger,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _eventBus = eventBus;
        _logger = logger;
        var dataRoot = config["FishOrchestrator:DataRoot"] ?? @"D:\DockerData\FishAudio";
        _outputPath = Path.Combine(dataRoot, "Output");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup recovery: handle jobs that were Processing when app stopped
        await RecoverInterruptedJobsAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TTS job processor loop");
            }

            await Task.Delay(2000, stoppingToken);
        }
    }

    private async Task RecoverInterruptedJobsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var interruptedJobs = await db.TtsJobs
            .Where(j => j.Status == TtsJobStatus.Processing)
            .ToListAsync();

        foreach (var job in interruptedJobs)
        {
            var filePath = Path.Combine(_outputPath, job.OutputFileName);
            if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
            {
                _logger.LogInformation("Recovering completed job {JobId} — output file exists", job.Id);
                await PromoteToGenerationLogAsync(db, job, filePath);
            }
            else
            {
                _logger.LogInformation("Re-queuing interrupted job {JobId}", job.Id);
                job.Status = TtsJobStatus.Queued;
                job.StartedAt = null;
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task ProcessNextJobAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = await db.TtsJobs
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(j => j.Status == TtsJobStatus.Queued, stoppingToken);

        if (job is null) return;

        // Mark as processing
        job.Status = TtsJobStatus.Processing;
        job.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(stoppingToken);
        _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Processing", null));

        // Find the running model
        var model = await db.ModelProfiles
            .FirstOrDefaultAsync(m => m.Status == ModelStatus.Running, stoppingToken);

        if (model is null)
        {
            job.Status = TtsJobStatus.Failed;
            job.ErrorMessage = "No running model available";
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(stoppingToken);
            _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Failed", job.ErrorMessage));
            return;
        }

        var baseUrl = $"http://localhost:{model.HostPort}";
        var request = new TtsRequest
        {
            Text = job.InputText,
            ReferenceId = job.ReferenceId,
            Format = job.Format
        };

        try
        {
            var ttsClient = scope.ServiceProvider.GetRequiredService<ITtsClientService>();
            var result = await ttsClient.GenerateAsync(baseUrl, request,
                job.ModelProfileId, job.ReferenceVoiceId);

            // Success — the file is already saved by GenerateAsync, and GenerationLog is created.
            // Now delete the job.
            db.TtsJobs.Remove(job);
            await db.SaveChangesAsync(stoppingToken);
            _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Completed", null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TTS job {JobId} failed", job.Id);
            job.Status = TtsJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(stoppingToken);
            _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Failed", ex.Message));
        }
    }

    private async Task PromoteToGenerationLogAsync(AppDbContext db, TtsJob job, string filePath)
    {
        var fileInfo = new FileInfo(filePath);

        var log = new GenerationLog
        {
            ModelProfileId = job.ModelProfileId,
            ReferenceVoiceId = job.ReferenceVoiceId,
            UserId = job.UserId,
            InputText = job.InputText,
            OutputFileName = job.OutputFileName,
            Format = job.Format,
            DurationMs = 0, // Unknown — generation happened during restart
            CreatedAt = job.CreatedAt
        };

        db.GenerationLogs.Add(log);
        db.TtsJobs.Remove(job);

        _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Completed", null));

        var notification = new TtsNotificationEvent(
            job.UserId,
            job.InputText.Length > 50 ? job.InputText[..50] + "..." : job.InputText,
            job.OutputFileName,
            0,
            true,
            null);
        _eventBus.RaiseTtsNotification(notification);
    }
}
```

- [ ] **Step 2: Register in Program.cs**

In `src/FishAudioOrchestrator.Web/Program.cs`, after the existing `builder.Services.AddHostedService<HealthMonitorService>();` line, add:

```csharp
builder.Services.AddHostedService<TtsJobProcessor>();
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build src/FishAudioOrchestrator.Web`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Services/TtsJobProcessor.cs src/FishAudioOrchestrator.Web/Program.cs
git commit -m "feat: add TtsJobProcessor background service"
```

---

### Task 4: Rewrite TtsPlayground.razor for job submission + status table

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Components/Pages/TtsPlayground.razor`

- [ ] **Step 1: Replace the entire file contents**

Replace `src/FishAudioOrchestrator.Web/Components/Pages/TtsPlayground.razor` with:

```razor
@page "/playground"
@attribute [Authorize]
@inject IVoiceLibraryService VoiceService
@inject AppDbContext Db
@using Microsoft.EntityFrameworkCore
@using FishAudioOrchestrator.Web.Data
@using FishAudioOrchestrator.Web.Data.Entities
@using FishAudioOrchestrator.Web.Hubs
@using Microsoft.AspNetCore.Identity
@inject UserManager<AppUser> UserManager
@inject OrchestratorEventBus EventBus
@implements IAsyncDisposable

<PageTitle>TTS Playground — Fish Orchestrator</PageTitle>

<h2>TTS Playground</h2>

@if (_activeModel is null)
{
    <div class="alert alert-warning">
        No model is currently running. <a href="/">Start a model</a> from the dashboard first.
    </div>
}
else
{
    <div class="row">
        <div class="col-md-8 col-lg-6">
            <div class="card">
                <div class="card-body">
                    <div class="mb-3">
                        <label class="form-label">Active Model</label>
                        <input class="form-control" value="@_activeModel.Name (port @_activeModel.HostPort)" disabled />
                    </div>

                    <div class="mb-3">
                        <label class="form-label">Reference Voice (optional)</label>
                        <select class="form-select" @bind="_selectedVoiceId">
                            <option value="">-- Default voice (no reference) --</option>
                            @foreach (var voice in _voices)
                            {
                                <option value="@voice.VoiceId">@voice.DisplayName (@voice.VoiceId)</option>
                            }
                        </select>
                    </div>

                    <div class="mb-3">
                        <label class="form-label">Text to Synthesize</label>
                        <textarea class="form-control" @bind="_inputText" rows="5"
                                  placeholder="Enter text to convert to speech..."></textarea>
                    </div>

                    <div class="mb-3">
                        <label class="form-label">Output Format</label>
                        <select class="form-select" @bind="_format">
                            <option value="wav">WAV</option>
                            <option value="mp3">MP3</option>
                            <option value="opus">Opus</option>
                        </select>
                    </div>

                    @if (!string.IsNullOrEmpty(_error))
                    {
                        <div class="alert alert-danger">@_error</div>
                    }

                    <button class="btn btn-primary w-100" @onclick="SubmitJob">
                        Submit to Queue
                    </button>
                </div>
            </div>
        </div>
    </div>
}

@if (_jobs.Count > 0)
{
    <h4 class="mt-4">Active Jobs</h4>
    <table class="table table-hover">
        <thead>
            <tr>
                <th>Status</th>
                <th>Text</th>
                <th>Format</th>
                <th>Submitted</th>
                <th>Actions</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var job in _jobs)
            {
                <tr>
                    <td>
                        @if (job.Status == TtsJobStatus.Processing)
                        {
                            <span class="badge bg-info">
                                <span class="spinner-border spinner-border-sm me-1"></span>
                                Processing
                            </span>
                        }
                        else if (job.Status == TtsJobStatus.Queued)
                        {
                            <span class="badge bg-secondary">Queued</span>
                        }
                        else if (job.Status == TtsJobStatus.Failed)
                        {
                            <span class="badge bg-danger">Failed</span>
                        }
                    </td>
                    <td><small class="text-muted">@Truncate(job.InputText, 60)</small></td>
                    <td><code>@job.Format</code></td>
                    <td><small>@job.CreatedAt.ToLocalTime().ToString("t")</small></td>
                    <td>
                        @if (job.Status == TtsJobStatus.Queued)
                        {
                            <button class="btn btn-sm btn-outline-warning" @onclick="() => CancelJob(job)">Cancel</button>
                        }
                        else if (job.Status == TtsJobStatus.Failed)
                        {
                            <button class="btn btn-sm btn-outline-primary me-1" @onclick="() => RetryJob(job)">Retry</button>
                            <button class="btn btn-sm btn-outline-danger" @onclick="() => DismissJob(job)">Dismiss</button>
                        }
                    </td>
                </tr>
                @if (job.Status == TtsJobStatus.Failed && !string.IsNullOrEmpty(job.ErrorMessage))
                {
                    <tr>
                        <td colspan="5">
                            <small class="text-danger">@job.ErrorMessage</small>
                        </td>
                    </tr>
                }
            }
        </tbody>
    </table>
}

@code {
    private ModelProfile? _activeModel;
    private List<ReferenceVoice> _voices = new();
    private List<TtsJob> _jobs = new();
    private string _selectedVoiceId = "";
    private string _inputText = "";
    private string _format = "wav";
    private string? _error;

    [CascadingParameter]
    private Task<AuthenticationState> AuthState { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        _activeModel = await Db.ModelProfiles
            .FirstOrDefaultAsync(m => m.Status == ModelStatus.Running);
        _voices = await VoiceService.ListVoicesAsync();

        var state = await AuthState;
        var user = await UserManager.GetUserAsync(state.User);
        var userId = user?.Id;

        _jobs = await Db.TtsJobs
            .Where(j => j.UserId == userId && j.Status != TtsJobStatus.Completed)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync();

        EventBus.OnContainerStatus += OnContainerStatusRaw;
        EventBus.OnTtsJobStatus += OnTtsJobStatusRaw;
    }

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
            else if (runningEvent is not null && (_activeModel is null || runningEvent.ModelId != _activeModel.Id))
            {
                _activeModel = await Db.ModelProfiles.FirstOrDefaultAsync(m => m.Id == runningEvent.ModelId);
                StateHasChanged();
            }
        });
    }

    private void OnTtsJobStatusRaw(TtsJobStatusEvent evt)
    {
        _ = InvokeAsync(() =>
        {
            var job = _jobs.FirstOrDefault(j => j.Id == evt.JobId);
            if (job is null) return;

            if (evt.Status == "Completed")
            {
                _jobs.Remove(job);
            }
            else if (evt.Status == "Failed")
            {
                job.Status = TtsJobStatus.Failed;
                job.ErrorMessage = evt.ErrorMessage;
            }
            else if (evt.Status == "Processing")
            {
                job.Status = TtsJobStatus.Processing;
            }

            StateHasChanged();
        });
    }

    public ValueTask DisposeAsync()
    {
        EventBus.OnContainerStatus -= OnContainerStatusRaw;
        EventBus.OnTtsJobStatus -= OnTtsJobStatusRaw;
        return ValueTask.CompletedTask;
    }

    private async Task SubmitJob()
    {
        if (string.IsNullOrWhiteSpace(_inputText))
        {
            _error = "Please enter text to synthesize.";
            return;
        }

        if (_activeModel is null) return;

        _error = null;

        var state = await AuthState;
        var user = await UserManager.GetUserAsync(state.User);

        int? voiceDbId = null;
        if (!string.IsNullOrEmpty(_selectedVoiceId))
        {
            var voice = await Db.ReferenceVoices
                .FirstOrDefaultAsync(v => v.VoiceId == _selectedVoiceId);
            voiceDbId = voice?.Id;
        }

        var job = new TtsJob
        {
            UserId = user?.Id,
            ModelProfileId = _activeModel.Id,
            ReferenceVoiceId = voiceDbId,
            InputText = _inputText.Trim(),
            Format = _format,
            ReferenceId = string.IsNullOrEmpty(_selectedVoiceId) ? null : _selectedVoiceId,
            OutputFileName = TtsClientService.GenerateOutputFileName(_format)
        };

        Db.TtsJobs.Add(job);
        await Db.SaveChangesAsync();

        _jobs.Add(job);
        _inputText = "";
    }

    private async Task CancelJob(TtsJob job)
    {
        Db.TtsJobs.Remove(job);
        await Db.SaveChangesAsync();
        _jobs.Remove(job);
    }

    private async Task RetryJob(TtsJob job)
    {
        job.Status = TtsJobStatus.Queued;
        job.ErrorMessage = null;
        job.StartedAt = null;
        job.CompletedAt = null;
        await Db.SaveChangesAsync();
    }

    private async Task DismissJob(TtsJob job)
    {
        Db.TtsJobs.Remove(job);
        await Db.SaveChangesAsync();
        _jobs.Remove(job);
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build src/FishAudioOrchestrator.Web`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/TtsPlayground.razor
git commit -m "feat: rewrite TtsPlayground for background job queue"
```

---

### Task 5: Verify end-to-end and push

- [ ] **Step 1: Run the app**

Run: `dotnet run --project src/FishAudioOrchestrator.Web`

Verify the migration applies automatically (the app calls `db.Database.MigrateAsync()` at startup).

- [ ] **Step 2: Smoke test**

1. Navigate to TTS Playground
2. Submit a short text — should appear in Active Jobs table as "Queued"
3. Within 2 seconds, status changes to "Processing"
4. Navigate away to Dashboard, History, etc. — no errors
5. Come back to Playground — job still shows in table
6. When generation completes, job disappears from Playground
7. Check History — the completed generation appears

- [ ] **Step 3: Push**

```bash
git push origin master
```
