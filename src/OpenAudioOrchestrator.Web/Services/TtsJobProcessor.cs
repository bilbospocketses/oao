using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using OpenAudioOrchestrator.Web.Hubs;
using Microsoft.EntityFrameworkCore;

namespace OpenAudioOrchestrator.Web.Services;

public class TtsJobProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDockerClient _docker;
    private readonly OrchestratorEventBus _eventBus;
    private readonly ILogger<TtsJobProcessor> _logger;
    private readonly string _outputPath;
    private readonly TtsJobSignal _jobSignal;
    private static readonly TimeSpan JobTimeout = TimeSpan.FromHours(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    public TtsJobProcessor(
        IServiceScopeFactory scopeFactory,
        IDockerClient docker,
        OrchestratorEventBus eventBus,
        ILogger<TtsJobProcessor> logger,
        IConfiguration config,
        TtsJobSignal jobSignal)
    {
        _scopeFactory = scopeFactory;
        _docker = docker;
        _eventBus = eventBus;
        _logger = logger;
        var dataRoot = config["OpenAudioOrchestrator:DataRoot"] ?? @"C:\MyOpenAudioProj";
        _outputPath = Path.Combine(dataRoot, "Output");
        _jobSignal = jobSignal;
    }

    private static void ValidateContainerId(string containerId)
    {
        if (!ContainerIdValidator.IsValid(containerId))
            throw new ArgumentException($"Invalid container ID format: {containerId}");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedJobsAsync(stoppingToken);

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

            // Wait for a signal from the UI or poll every 30 seconds as a safety net
            try { await _jobSignal.WaitAsync(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task RecoverInterruptedJobsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var interruptedJobs = await db.TtsJobs
            .Include(j => j.ModelProfile)
            .Where(j => j.Status == TtsJobStatus.Processing)
            .ToListAsync(stoppingToken);

        foreach (var job in interruptedJobs)
        {
            var filePath = Path.Combine(_outputPath, job.OutputFileName);

            if (File.Exists(filePath) && new FileInfo(filePath).Length > 1000)
            {
                // File written during restart — promote to completed
                _logger.LogInformation("Recovering completed job {JobId} — output file exists", job.Id);
                PromoteToGenerationLog(db, job);
                await db.SaveChangesAsync(CancellationToken.None);
                continue;
            }

            // Check if curl is still running inside the container
            if (job.ModelProfile?.ContainerId is not null && await IsCurlRunningAsync(job.ModelProfile.ContainerId))
            {
                _logger.LogInformation("Job {JobId} still processing in container — resuming poll", job.Id);
                await PollForOutputFileAsync(db, job, stoppingToken);
                continue;
            }

            // curl not running, file doesn't exist — re-queue
            _logger.LogInformation("Re-queuing interrupted job {JobId}", job.Id);
            job.Status = TtsJobStatus.Queued;
            job.StartedAt = null;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task ProcessNextJobAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = await db.TtsJobs
            .OrderBy(j => j.Id)
            .FirstOrDefaultAsync(j => j.Status == TtsJobStatus.Queued, stoppingToken);

        if (job is null) return;

        // Mark as processing
        job.Status = TtsJobStatus.Processing;
        job.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Processing", null));

        // Find the running model
        var model = await db.ModelProfiles
            .FirstOrDefaultAsync(m => m.Status == ModelStatus.Running || m.Status == ModelStatus.Error, stoppingToken);

        if (model?.ContainerId is null)
        {
            await FailJob(db, job, "No running model available");
            return;
        }

        ValidateContainerId(model.ContainerId);

        // Write the JSON request to a temp file in the mounted output directory
        // so curl can read it from inside the container
        var request = new TtsRequest
        {
            Text = job.InputText,
            ReferenceId = job.ReferenceId,
            Format = job.Format
        };
        var json = TtsClientService.BuildRequestJson(request);
        var containerOutputPath = $"/app/output/{job.OutputFileName}";
        var requestFileName = $"_req_{job.Id}.json";
        var requestFilePath = Path.Combine(_outputPath, requestFileName);
        var containerRequestPath = $"/app/output/{requestFileName}";

        await File.WriteAllTextAsync(requestFilePath, json);

        _logger.LogInformation("Starting docker exec curl for job {JobId}", job.Id);

        try
        {
            // Create an exec instance inside the container via Docker SDK
            var execCreateResponse = await _docker.Exec.ExecCreateContainerAsync(
                model.ContainerId,
                new ContainerExecCreateParameters
                {
                    Cmd = new[]
                    {
                        "curl", "-s", "-X", "POST",
                        "http://localhost:8080/v1/tts",
                        "-H", "Content-Type: application/json",
                        "-d", $"@{containerRequestPath}",
                        "--output", containerOutputPath,
                        "--max-time", "18000"
                    },
                    AttachStdout = true,
                    AttachStderr = true
                },
                stoppingToken);

            var execId = execCreateResponse.ID;

            // Start the exec (attached so Docker tracks it, but we poll for the output file)
            using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(
                execId, tty: false, stoppingToken);

            // Poll for completion: check file, job status (for cancel), and timeout
            await PollForOutputFileAsync(db, job, stoppingToken, execId, stream, model.ContainerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TTS job {JobId} failed", job.Id);
            await FailJob(db, job, ex.Message);
        }
        finally
        {
            // Clean up the request JSON file
            try { if (File.Exists(requestFilePath)) File.Delete(requestFilePath); } catch { }
        }
    }

    private async Task PollForOutputFileAsync(AppDbContext db, TtsJob job,
        CancellationToken stoppingToken, string? execId = null, MultiplexedStream? execStream = null,
        string? containerId = null)
    {
        var filePath = Path.Combine(_outputPath, job.OutputFileName);
        var startTime = job.StartedAt ?? DateTimeOffset.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            // Check if the exec has finished (via Docker SDK inspect)
            if (execId is not null)
            {
                var execInspect = await _docker.Exec.InspectContainerExecAsync(execId, CancellationToken.None);
                if (!execInspect.Running)
                {
                    if (File.Exists(filePath))
                    {
                        var fileSize = new FileInfo(filePath).Length;
                        if (fileSize > 1000)
                        {
                            _logger.LogInformation("Job {JobId} completed — output file found ({Size} bytes)", job.Id, fileSize);
                            await Task.Delay(1000, CancellationToken.None);
                            var duration = (long)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                            PromoteToGenerationLog(db, job, duration);
                            await db.SaveChangesAsync(CancellationToken.None);
                            return;
                        }
                        else
                        {
                            var errorContent = await File.ReadAllTextAsync(filePath);
                            _logger.LogError("TTS API returned error for job {JobId}: {Error}", job.Id, errorContent);
                            try { File.Delete(filePath); } catch { }
                            await FailJob(db, job, $"TTS API error: {errorContent}");
                            return;
                        }
                    }
                    else
                    {
                        // Read stderr from the exec stream if available
                        var stderr = await ReadExecStreamAsync(execStream);
                        _logger.LogError("docker exec failed for job {JobId} (exit code {ExitCode}): {Error}",
                            job.Id, execInspect.ExitCode, stderr);
                        await FailJob(db, job, $"docker exec failed (exit code {execInspect.ExitCode}): {stderr}");
                        return;
                    }
                }
            }

            // Check if file appeared (for recovery polling without an exec reference)
            if (execId is null && File.Exists(filePath) && new FileInfo(filePath).Length > 1000)
            {
                _logger.LogInformation("Job {JobId} completed — output file found during recovery", job.Id);
                await Task.Delay(1000, CancellationToken.None);
                var duration = (long)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                PromoteToGenerationLog(db, job, duration);
                await db.SaveChangesAsync(CancellationToken.None);
                return;
            }

            // Check if job was cancelled via UI
            var currentStatus = await db.TtsJobs
                .Where(j => j.Id == job.Id)
                .Select(j => j.Status)
                .FirstOrDefaultAsync(CancellationToken.None);

            if (currentStatus == TtsJobStatus.Failed || currentStatus == default)
            {
                _logger.LogInformation("Job {JobId} was cancelled", job.Id);
                // Ensure curl is stopped even if CancelJobAsync hasn't killed it yet
                if (containerId is not null)
                    await KillCurlInContainerAsync(containerId);
                return;
            }

            // Check timeout
            if (DateTimeOffset.UtcNow - startTime > JobTimeout)
            {
                _logger.LogWarning("Job {JobId} timed out after {Hours} hours", job.Id, JobTimeout.TotalHours);
                await FailJob(db, job, $"Generation timed out after {JobTimeout.TotalHours} hours");
                return;
            }
        }
    }

    private static async Task<string> ReadExecStreamAsync(MultiplexedStream? stream)
    {
        if (stream is null) return "";
        try
        {
            var buffer = new byte[4096];
            var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, CancellationToken.None);
            return result.Count > 0 ? Encoding.UTF8.GetString(buffer, 0, result.Count) : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Cancels a processing job by killing curl inside the container.
    /// Called from the UI via the event bus or directly.
    /// </summary>
    public async Task CancelJobAsync(int jobId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = await db.TtsJobs
            .Include(j => j.ModelProfile)
            .FirstOrDefaultAsync(j => j.Id == jobId);

        if (job is null) return;

        // Kill curl inside the container via Docker SDK
        if (job.ModelProfile?.ContainerId is not null)
        {
            await KillCurlInContainerAsync(job.ModelProfile.ContainerId);
        }

        // Clean up partial output file
        var filePath = Path.Combine(_outputPath, job.OutputFileName);
        if (File.Exists(filePath))
        {
            try { File.Delete(filePath); } catch { }
        }

        job.Status = TtsJobStatus.Failed;
        job.ErrorMessage = "Cancelled by user";
        job.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Failed", job.ErrorMessage));
    }

    private async Task FailJob(AppDbContext db, TtsJob job, string error)
    {
        try
        {
            job.Status = TtsJobStatus.Failed;
            job.ErrorMessage = error;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Failed", error));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update job {JobId} status to Failed", job.Id);
        }
    }

    private void PromoteToGenerationLog(AppDbContext db, TtsJob job, long durationMs = 0)
    {
        var log = new GenerationLog
        {
            ModelProfileId = job.ModelProfileId,
            ReferenceVoiceId = job.ReferenceVoiceId,
            UserId = job.UserId,
            InputText = job.InputText,
            OutputFileName = job.OutputFileName,
            Format = job.Format,
            DurationMs = durationMs,
            CreatedAt = job.CreatedAt
        };

        db.GenerationLogs.Add(log);
        db.TtsJobs.Remove(job);

        _eventBus.RaiseTtsJobStatus(new TtsJobStatusEvent(job.Id, "Completed", null));

        var notification = new TtsNotificationEvent(
            job.UserId,
            job.InputText.Length > 50 ? job.InputText[..50] + "..." : job.InputText,
            job.OutputFileName,
            durationMs,
            true,
            null);
        _eventBus.RaiseTtsNotification(notification);
    }

    private async Task<bool> IsCurlRunningAsync(string containerId)
    {
        try
        {
            ValidateContainerId(containerId);
            var execCreateResponse = await _docker.Exec.ExecCreateContainerAsync(
                containerId,
                new ContainerExecCreateParameters
                {
                    Cmd = new[] { "pgrep", "-f", "curl" },
                    AttachStdout = true
                });

            using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(
                execCreateResponse.ID, tty: false);

            // Read output to let the exec complete
            var buffer = new byte[1024];
            await stream.ReadOutputAsync(buffer, 0, buffer.Length, CancellationToken.None);

            var inspect = await _docker.Exec.InspectContainerExecAsync(execCreateResponse.ID);
            return inspect.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task KillCurlInContainerAsync(string containerId)
    {
        try
        {
            ValidateContainerId(containerId);
            var execCreateResponse = await _docker.Exec.ExecCreateContainerAsync(
                containerId,
                new ContainerExecCreateParameters
                {
                    Cmd = new[] { "pkill", "-f", "curl" },
                    AttachStdout = true
                });

            using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(
                execCreateResponse.ID, tty: false);

            // Read to completion
            var buffer = new byte[1024];
            await stream.ReadOutputAsync(buffer, 0, buffer.Length, CancellationToken.None);
        }
        catch { }
    }
}
