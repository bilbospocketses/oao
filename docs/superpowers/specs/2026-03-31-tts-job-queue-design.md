> **Note:** post-rename. References to `OpenAudioOrchestrator.*` paths and
> config keys reflect their original names. Current equivalents are
> `oao.*` and `oao:*`.
# TTS Background Job Queue — Design Spec

## Goal

Replace the synchronous TTS generation flow (which blocks the page and times out) with a background job queue. Users submit requests from the TTS Playground, can navigate freely, and completed results appear in Generation History.

## Architecture

A serial background job queue backed by SQLite. Jobs are persisted so they survive app restarts.

**Components:**
- **`TtsJob` entity** — new DB table tracking each generation request through its lifecycle
- **`TtsJobProcessor`** — `BackgroundService` singleton that polls for queued jobs, processes one at a time via `TtsClientService.GenerateAsync`, promotes completed jobs to `GenerationLog`
- **`TtsJobEvent` on `OrchestratorEventBus`** — new event for real-time job status updates to the UI
- **Updated `TtsPlayground.razor`** — submit button creates a job and returns immediately; page shows active job table with status

## TtsJob Entity

```
TtsJob
  Id              int (PK)
  UserId          string?
  ModelProfileId  int (FK → ModelProfile)
  ReferenceVoiceId int? (FK → ReferenceVoice)
  InputText       string
  Format          string (wav/mp3/opus)
  ReferenceId     string? (voice string ID for API call)
  OutputFileName  string (generated at submission time)
  Status          enum: Queued, Processing, Completed, Failed
  ErrorMessage    string?
  CreatedAt       DateTime
  StartedAt       DateTime?
  CompletedAt     DateTime?
```

One new migration adds the `TtsJobs` table. No changes to existing tables.

## Processing Flow

1. User clicks Generate on TTS Playground
2. A `TtsJob` record is created with status `Queued`, output filename pre-generated
3. `TtsJobProcessor` (polling every 2 seconds) picks it up, sets `Processing`, fires `TtsJobEvent`
4. Calls `TtsClientService.GenerateAsync` with no timeout concern (background service)
5. On success: creates `GenerationLog`, deletes the `TtsJob`, fires `TtsNotificationEvent`
6. On failure: sets `Failed` with error message, fires `TtsJobEvent`

## Startup Recovery

When `TtsJobProcessor` starts, it checks for any `Processing` jobs (interrupted by app restart):
- If the output file exists on disk and has size > 0 → the container finished during restart → promote to `GenerationLog`, delete the job
- If the output file doesn't exist → re-queue by setting status back to `Queued`

## TTS Playground UI

The page changes from a blocking "Generate" flow to:

**Submit form** (top) — same fields as today (model, voice, text, format). Generate button creates a job and clears the form. No loading spinner, no blocking.

**Active jobs table** (below form) — shows all jobs for the current user that are `Queued`, `Processing`, or `Failed`:

| Status | Text | Format | Submitted | Actions |
|--------|------|--------|-----------|---------|
| Processing | how does this sound | wav | 5:17 PM | — |
| Queued | another test | mp3 | 5:18 PM | Cancel |
| Failed | broken text | wav | 5:15 PM | Retry / Dismiss |

- **Queued** jobs have a Cancel button (deletes the job)
- **Processing** jobs have no actions (can't cancel mid-generation)
- **Failed** jobs have Retry (re-queues) and Dismiss (deletes)
- **Completed** jobs are removed from this table (they're in History now)

The table updates in real-time via `OrchestratorEventBus.OnTtsJobStatus`.

## Event Bus Addition

New event on `OrchestratorEventBus`:

```csharp
public record TtsJobStatusEvent(int JobId, string Status, string? ErrorMessage);
public event Action<TtsJobStatusEvent>? OnTtsJobStatus;
public void RaiseTtsJobStatus(TtsJobStatusEvent evt) => OnTtsJobStatus?.Invoke(evt);
```

The processor fires this whenever a job changes state. The existing `TtsNotificationEvent` continues to fire on completion (for toast notifications).

## Serial Processing

Only one job processes at a time. The processor:
1. Queries for the oldest `Processing` job (in case of restart recovery)
2. If none, queries for the oldest `Queued` job
3. Processes it
4. Loops

This prevents GPU OOM from concurrent generation requests.

## Error Handling

- **No running model:** Job marked `Failed` with "No running model available"
- **HTTP/network error:** Job marked `Failed` with exception message
- **App restart during processing:** Recovered via startup check (file exists → complete, else re-queue)
- **Container crashes during generation:** HTTP call fails, job marked `Failed`

## Files Affected

| Action | File |
|--------|------|
| Create | `Data/Entities/TtsJob.cs` |
| Create | `Services/TtsJobProcessor.cs` |
| Create | `Data/Migrations/[timestamp]_AddTtsJobs.cs` (generated) |
| Modify | `Data/AppDbContext.cs` — add `DbSet<TtsJob>` |
| Modify | `Services/OrchestratorEventBus.cs` — add `TtsJobStatusEvent` |
| Modify | `Hubs/HubEvents.cs` — add `TtsJobStatusEvent` record |
| Modify | `Components/Pages/TtsPlayground.razor` — new submit + job table UI |
| Modify | `Program.cs` — register `TtsJobProcessor` as hosted service |
