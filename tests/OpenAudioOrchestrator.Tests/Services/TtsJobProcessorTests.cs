using Docker.DotNet;
using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using OpenAudioOrchestrator.Web.Hubs;
using OpenAudioOrchestrator.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace OpenAudioOrchestrator.Tests.Services;

public class TtsJobProcessorTests : IDisposable
{
    private readonly string _testDataRoot;
    private readonly string _outputPath;

    public TtsJobProcessorTests()
    {
        _testDataRoot = Path.Combine(Path.GetTempPath(), $"oao-test-{Guid.NewGuid()}");
        _outputPath = Path.Combine(_testDataRoot, "Output");
        Directory.CreateDirectory(_outputPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataRoot))
            Directory.Delete(_testDataRoot, true);
    }

    private static AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private IConfiguration CreateConfig()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAudioOrchestrator:DataRoot"] = _testDataRoot
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

    private TtsJobProcessor CreateProcessor(AppDbContext context, OrchestratorEventBus? eventBus = null)
    {
        var scopeFactory = CreateScopeFactory(context);
        eventBus ??= new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        var mockDocker = new Mock<IDockerClient>();
        return new TtsJobProcessor(
            scopeFactory,
            mockDocker.Object,
            eventBus,
            NullLogger<TtsJobProcessor>.Instance,
            CreateConfig(),
            new TtsJobSignal());
    }

    private static ModelProfile CreateRunningModel(string containerId = "abcdef123456")
    {
        return new ModelProfile
        {
            Name = "test-model",
            CheckpointPath = @"D:\models\test",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = containerId,
            Status = ModelStatus.Running
        };
    }

    private static TtsJob CreateJob(
        int modelProfileId,
        TtsJobStatus status = TtsJobStatus.Queued,
        string? outputFileName = null,
        DateTimeOffset? createdAt = null)
    {
        return new TtsJob
        {
            ModelProfileId = modelProfileId,
            InputText = "Hello, this is a test generation.",
            Format = "wav",
            OutputFileName = outputFileName ?? $"output-{Guid.NewGuid()}.wav",
            Status = status,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        };
    }

    // -------------------------------------------------------------------
    // RecoverInterruptedJobs tests
    // -------------------------------------------------------------------

    [Fact]
    public async Task RecoverInterruptedJobs_PromotesCompletedJob_WhenOutputFileExists()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var model = CreateRunningModel();
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var job = CreateJob(model.Id, TtsJobStatus.Processing, "completed-output.wav");
        job.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        context.TtsJobs.Add(job);
        await context.SaveChangesAsync();

        // Create a fake output file > 1000 bytes
        var filePath = Path.Combine(_outputPath, "completed-output.wav");
        await File.WriteAllBytesAsync(filePath, new byte[2000]);

        var eventBus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        TtsJobStatusEvent? receivedEvent = null;
        TtsNotificationEvent? receivedNotification = null;
        eventBus.OnTtsJobStatus += evt => receivedEvent = evt;
        eventBus.OnTtsNotification += evt => receivedNotification = evt;

        var processor = CreateProcessor(context, eventBus);

        // Act — ExecuteAsync calls RecoverInterruptedJobsAsync, then loops.
        // We use a short-lived CancellationToken so it stops after recovery.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try { await processor.StartAsync(cts.Token); await Task.Delay(600); }
        catch (OperationCanceledException) { }
        finally { await processor.StopAsync(CancellationToken.None); }

        // Assert — job should be promoted to GenerationLog and removed from TtsJobs
        Assert.False(await context.TtsJobs.AnyAsync(j => j.Id == job.Id));
        var log = await context.GenerationLogs.FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal(job.InputText, log.InputText);
        Assert.Equal(job.OutputFileName, log.OutputFileName);
        Assert.Equal(job.Format, log.Format);
        Assert.Equal(job.ModelProfileId, log.ModelProfileId);
        Assert.Equal(job.ReferenceVoiceId, log.ReferenceVoiceId);
        Assert.Equal(job.UserId, log.UserId);
        Assert.Equal(job.CreatedAt, log.CreatedAt);

        // Events should have fired
        Assert.NotNull(receivedEvent);
        Assert.Equal(job.Id, receivedEvent.JobId);
        Assert.Equal("Completed", receivedEvent.Status);

        Assert.NotNull(receivedNotification);
        Assert.True(receivedNotification.Success);
        Assert.Equal(job.OutputFileName, receivedNotification.OutputFileName);
    }

    [Fact]
    public async Task RecoverInterruptedJobs_RequeuesJob_WhenNoOutputFile()
    {
        // Arrange — use a model with no ContainerId so IsCurlRunningAsync is skipped
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "no-container-model",
            CheckpointPath = @"D:\models\test",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = null,
            Status = ModelStatus.Stopped
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var job = CreateJob(model.Id, TtsJobStatus.Processing, "missing-output.wav");
        job.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        context.TtsJobs.Add(job);
        await context.SaveChangesAsync();

        // No output file exists — job should be re-queued during recovery.
        // After recovery, the process loop picks it up again and fails it
        // (no running model), which proves it was re-queued first.

        var eventBus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        var statusEvents = new List<TtsJobStatusEvent>();
        eventBus.OnTtsJobStatus += evt => statusEvents.Add(evt);

        var processor = CreateProcessor(context, eventBus);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await processor.StartAsync(cts.Token); await Task.Delay(3100); }
        catch (OperationCanceledException) { }
        finally { await processor.StopAsync(CancellationToken.None); }

        // Assert — job was NOT promoted to GenerationLog (key: it was re-queued, not completed)
        Assert.False(await context.GenerationLogs.AnyAsync());

        // Job was re-queued then picked up by ProcessNextJobAsync which failed it
        // (no running model). The "Processing" event proves it was re-queued and re-picked.
        var finalJob = await context.TtsJobs.FirstAsync(j => j.Id == job.Id);
        Assert.Equal(TtsJobStatus.Failed, finalJob.Status);
        Assert.Contains("No running model", finalJob.ErrorMessage!);
        Assert.Contains(statusEvents, e => e.Status == "Processing" && e.JobId == job.Id);
    }

    [Fact]
    public async Task RecoverInterruptedJobs_RequeuesJob_WhenOutputFileTooSmall()
    {
        // Arrange — use a model with no ContainerId so IsCurlRunningAsync is skipped
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "no-container-model",
            CheckpointPath = @"D:\models\test",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = null,
            Status = ModelStatus.Stopped
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var job = CreateJob(model.Id, TtsJobStatus.Processing, "small-output.wav");
        job.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        context.TtsJobs.Add(job);
        await context.SaveChangesAsync();

        // Create a file that is too small (< 1000 bytes) — should not promote
        var filePath = Path.Combine(_outputPath, "small-output.wav");
        await File.WriteAllBytesAsync(filePath, new byte[500]);

        var eventBus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        var statusEvents = new List<TtsJobStatusEvent>();
        eventBus.OnTtsJobStatus += evt => statusEvents.Add(evt);

        var processor = CreateProcessor(context, eventBus);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await processor.StartAsync(cts.Token); await Task.Delay(3100); }
        catch (OperationCanceledException) { }
        finally { await processor.StopAsync(CancellationToken.None); }

        // Assert — job should NOT be promoted to GenerationLog
        Assert.False(await context.GenerationLogs.AnyAsync());

        // Job was re-queued then picked up and failed (no running model)
        var finalJob = await context.TtsJobs.FirstAsync(j => j.Id == job.Id);
        Assert.Equal(TtsJobStatus.Failed, finalJob.Status);
        Assert.Contains("No running model", finalJob.ErrorMessage!);
    }

    [Fact]
    public async Task RecoverInterruptedJobs_IgnoresQueuedJobs()
    {
        // Arrange — recovery only acts on Processing jobs, not Queued ones.
        // We verify by having a Queued job alongside a Processing job with
        // a valid output file. Only the Processing job should be promoted.
        var context = CreateInMemoryContext();
        var model = CreateRunningModel();
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        // Processing job with valid output — will be promoted
        var processingJob = CreateJob(model.Id, TtsJobStatus.Processing, "recovery-target.wav");
        processingJob.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        // Queued job — recovery should NOT touch this
        var queuedJob = CreateJob(model.Id, TtsJobStatus.Queued, "queued-job.wav",
            DateTimeOffset.UtcNow.AddMinutes(-1));

        context.TtsJobs.AddRange(processingJob, queuedJob);
        await context.SaveChangesAsync();

        var filePath = Path.Combine(_outputPath, "recovery-target.wav");
        await File.WriteAllBytesAsync(filePath, new byte[2000]);

        var processor = CreateProcessor(context);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try { await processor.StartAsync(cts.Token); await Task.Delay(600); }
        catch (OperationCanceledException) { }
        finally { await processor.StopAsync(CancellationToken.None); }

        // Assert — only the Processing job was promoted
        Assert.False(await context.TtsJobs.AnyAsync(j => j.Id == processingJob.Id));
        Assert.True(await context.GenerationLogs.AnyAsync(g => g.OutputFileName == "recovery-target.wav"));

        // Queued job should still exist in TtsJobs (recovery didn't touch it)
        Assert.True(await context.TtsJobs.AnyAsync(j => j.Id == queuedJob.Id));
    }

    // -------------------------------------------------------------------
    // ProcessNextJob tests
    // -------------------------------------------------------------------

    [Fact]
    public async Task ProcessNextJob_FailsJob_WhenNoRunningModel()
    {
        // Arrange — no model at all in the database
        var context = CreateInMemoryContext();

        // Need a model to satisfy the FK, but make it Stopped
        var model = new ModelProfile
        {
            Name = "stopped-model",
            CheckpointPath = @"D:\models\test",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = null,
            Status = ModelStatus.Stopped
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var job = CreateJob(model.Id, TtsJobStatus.Queued);
        context.TtsJobs.Add(job);
        await context.SaveChangesAsync();

        var eventBus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        var receivedEvents = new List<TtsJobStatusEvent>();
        eventBus.OnTtsJobStatus += evt => receivedEvents.Add(evt);

        var processor = CreateProcessor(context, eventBus);

        // Act — let it run long enough for one ProcessNextJobAsync cycle
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await processor.StartAsync(cts.Token); await Task.Delay(3100); }
        catch (OperationCanceledException) { }
        finally { await processor.StopAsync(CancellationToken.None); }

        // Assert
        var failedJob = await context.TtsJobs.FirstAsync(j => j.Id == job.Id);
        Assert.Equal(TtsJobStatus.Failed, failedJob.Status);
        Assert.Equal("No running model available", failedJob.ErrorMessage);
        Assert.NotNull(failedJob.CompletedAt);

        // Should have raised Processing then Failed events
        Assert.Contains(receivedEvents, e => e.Status == "Processing" && e.JobId == job.Id);
        Assert.Contains(receivedEvents, e => e.Status == "Failed" && e.JobId == job.Id);
    }

    [Fact]
    public async Task ProcessNextJob_PicksOldestQueuedJob()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "stopped-model",
            CheckpointPath = @"D:\models\test",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            Status = ModelStatus.Stopped // No running model, so both will fail — but we can check which fails first
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        // Older job (should be picked first)
        var olderJob = CreateJob(model.Id, TtsJobStatus.Queued, "older.wav",
            DateTimeOffset.UtcNow.AddMinutes(-10));
        // Newer job
        var newerJob = CreateJob(model.Id, TtsJobStatus.Queued, "newer.wav",
            DateTimeOffset.UtcNow);

        context.TtsJobs.AddRange(olderJob, newerJob);
        await context.SaveChangesAsync();

        var eventBus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        var processingJobIds = new List<int>();
        eventBus.OnTtsJobStatus += evt =>
        {
            if (evt.Status == "Processing")
                processingJobIds.Add(evt.JobId);
        };

        var processor = CreateProcessor(context, eventBus);

        // Act — let it process at least one job
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await processor.StartAsync(cts.Token); await Task.Delay(3100); }
        catch (OperationCanceledException) { }
        finally { await processor.StopAsync(CancellationToken.None); }

        // Assert — older job should have been picked first
        Assert.NotEmpty(processingJobIds);
        Assert.Equal(olderJob.Id, processingJobIds[0]);
    }

    [Fact]
    public async Task ProcessNextJob_MarksJobAsProcessing()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "stopped-model",
            CheckpointPath = @"D:\models\test",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            Status = ModelStatus.Stopped
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var job = CreateJob(model.Id, TtsJobStatus.Queued);
        context.TtsJobs.Add(job);
        await context.SaveChangesAsync();

        var eventBus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        var receivedEvents = new List<TtsJobStatusEvent>();
        eventBus.OnTtsJobStatus += evt => receivedEvents.Add(evt);

        var processor = CreateProcessor(context, eventBus);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await processor.StartAsync(cts.Token); await Task.Delay(3100); }
        catch (OperationCanceledException) { }
        finally { await processor.StopAsync(CancellationToken.None); }

        // Assert — the first event should be "Processing"
        Assert.Contains(receivedEvents, e => e.Status == "Processing" && e.JobId == job.Id);
    }

    // -------------------------------------------------------------------
    // CancelJobAsync tests
    // -------------------------------------------------------------------

    [Fact]
    public async Task CancelJobAsync_MarksJobAsFailed()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var model = CreateRunningModel();
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var job = CreateJob(model.Id, TtsJobStatus.Processing);
        job.StartedAt = DateTimeOffset.UtcNow;
        context.TtsJobs.Add(job);
        await context.SaveChangesAsync();

        var eventBus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        TtsJobStatusEvent? receivedEvent = null;
        eventBus.OnTtsJobStatus += evt => receivedEvent = evt;

        var processor = CreateProcessor(context, eventBus);

        // Act
        await processor.CancelJobAsync(job.Id);

        // Assert
        var cancelled = await context.TtsJobs.FirstAsync(j => j.Id == job.Id);
        Assert.Equal(TtsJobStatus.Failed, cancelled.Status);
        Assert.Equal("Cancelled by user", cancelled.ErrorMessage);
        Assert.NotNull(cancelled.CompletedAt);

        Assert.NotNull(receivedEvent);
        Assert.Equal(job.Id, receivedEvent.JobId);
        Assert.Equal("Failed", receivedEvent.Status);
        Assert.Equal("Cancelled by user", receivedEvent.ErrorMessage);
    }

    [Fact]
    public async Task CancelJobAsync_CleansUpPartialOutputFile()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var model = CreateRunningModel();
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var outputFileName = "partial-output.wav";
        var job = CreateJob(model.Id, TtsJobStatus.Processing, outputFileName);
        job.StartedAt = DateTimeOffset.UtcNow;
        context.TtsJobs.Add(job);
        await context.SaveChangesAsync();

        // Create a partial output file
        var filePath = Path.Combine(_outputPath, outputFileName);
        await File.WriteAllBytesAsync(filePath, new byte[500]);
        Assert.True(File.Exists(filePath));

        var processor = CreateProcessor(context);

        // Act
        await processor.CancelJobAsync(job.Id);

        // Assert — partial file should be deleted
        Assert.False(File.Exists(filePath));

        var cancelled = await context.TtsJobs.FirstAsync(j => j.Id == job.Id);
        Assert.Equal(TtsJobStatus.Failed, cancelled.Status);
    }

    [Fact]
    public async Task CancelJobAsync_DoesNothingForMissingJob()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var processor = CreateProcessor(context);

        // Act — should not throw
        await processor.CancelJobAsync(9999);

        // Assert — no jobs exist, nothing happened
        Assert.False(await context.TtsJobs.AnyAsync());
    }

    [Fact]
    public async Task CancelJobAsync_DoesNotThrowWhenOutputFileDoesNotExist()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var model = CreateRunningModel();
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var job = CreateJob(model.Id, TtsJobStatus.Processing, "nonexistent-file.wav");
        job.StartedAt = DateTimeOffset.UtcNow;
        context.TtsJobs.Add(job);
        await context.SaveChangesAsync();

        var processor = CreateProcessor(context);

        // Act — should not throw even though file doesn't exist
        await processor.CancelJobAsync(job.Id);

        // Assert
        var cancelled = await context.TtsJobs.FirstAsync(j => j.Id == job.Id);
        Assert.Equal(TtsJobStatus.Failed, cancelled.Status);
        Assert.Equal("Cancelled by user", cancelled.ErrorMessage);
    }

    // -------------------------------------------------------------------
    // FailJob tests (via CancelJobAsync which calls same status-setting pattern)
    // -------------------------------------------------------------------

    [Fact]
    public async Task FailJob_SetsStatusAndError()
    {
        // FailJob is private, so we test via ProcessNextJob with no running model,
        // which triggers FailJob("No running model available")
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "stopped",
            CheckpointPath = @"D:\models\test",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            Status = ModelStatus.Stopped
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var job = CreateJob(model.Id, TtsJobStatus.Queued);
        context.TtsJobs.Add(job);
        await context.SaveChangesAsync();

        var eventBus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        var failedEvents = new List<TtsJobStatusEvent>();
        eventBus.OnTtsJobStatus += evt =>
        {
            if (evt.Status == "Failed")
                failedEvents.Add(evt);
        };

        var processor = CreateProcessor(context, eventBus);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await processor.StartAsync(cts.Token); await Task.Delay(3100); }
        catch (OperationCanceledException) { }
        finally { await processor.StopAsync(CancellationToken.None); }

        // Assert
        var failedJob = await context.TtsJobs.FirstAsync(j => j.Id == job.Id);
        Assert.Equal(TtsJobStatus.Failed, failedJob.Status);
        Assert.NotNull(failedJob.ErrorMessage);
        Assert.Contains("No running model", failedJob.ErrorMessage);
        Assert.NotNull(failedJob.CompletedAt);

        Assert.Single(failedEvents);
        Assert.Equal("No running model available", failedEvents[0].ErrorMessage);
    }

    // -------------------------------------------------------------------
    // PromoteToGenerationLog tests (via recovery path)
    // -------------------------------------------------------------------

    [Fact]
    public async Task PromoteToGenerationLog_CreatesLogAndRemovesJob()
    {
        // PromoteToGenerationLog is private, so we test via recovery
        // where an output file > 1000 bytes exists for a Processing job
        var context = CreateInMemoryContext();
        var model = CreateRunningModel();
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var job = CreateJob(model.Id, TtsJobStatus.Processing, "promote-test.wav");
        job.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-3);
        job.UserId = "user-123";
        job.ReferenceVoiceId = null;
        job.ReferenceId = "ref-abc";
        context.TtsJobs.Add(job);
        await context.SaveChangesAsync();
        var originalJobId = job.Id;
        var originalCreatedAt = job.CreatedAt;

        // Create a valid-sized output file
        var filePath = Path.Combine(_outputPath, "promote-test.wav");
        await File.WriteAllBytesAsync(filePath, new byte[5000]);

        var eventBus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        TtsJobStatusEvent? statusEvent = null;
        TtsNotificationEvent? notificationEvent = null;
        eventBus.OnTtsJobStatus += evt => statusEvent = evt;
        eventBus.OnTtsNotification += evt => notificationEvent = evt;

        var processor = CreateProcessor(context, eventBus);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try { await processor.StartAsync(cts.Token); await Task.Delay(600); }
        catch (OperationCanceledException) { }
        finally { await processor.StopAsync(CancellationToken.None); }

        // Assert — TtsJob removed
        Assert.False(await context.TtsJobs.AnyAsync(j => j.Id == originalJobId));

        // GenerationLog created with correct fields
        var log = await context.GenerationLogs.FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal(model.Id, log.ModelProfileId);
        Assert.Equal(job.UserId, log.UserId);
        Assert.Equal(job.InputText, log.InputText);
        Assert.Equal("promote-test.wav", log.OutputFileName);
        Assert.Equal("wav", log.Format);
        Assert.Equal(originalCreatedAt, log.CreatedAt);

        // Events
        Assert.NotNull(statusEvent);
        Assert.Equal(originalJobId, statusEvent.JobId);
        Assert.Equal("Completed", statusEvent.Status);
        Assert.Null(statusEvent.ErrorMessage);

        Assert.NotNull(notificationEvent);
        Assert.True(notificationEvent.Success);
        Assert.Equal("promote-test.wav", notificationEvent.OutputFileName);
        Assert.Equal("user-123", notificationEvent.UserId);
    }

    [Fact]
    public async Task PromoteToGenerationLog_TruncatesLongInputTextInNotification()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var model = CreateRunningModel();
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var longText = new string('A', 100); // > 50 chars
        var job = new TtsJob
        {
            ModelProfileId = model.Id,
            InputText = longText,
            Format = "wav",
            OutputFileName = "long-text.wav",
            Status = TtsJobStatus.Processing,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        context.TtsJobs.Add(job);
        await context.SaveChangesAsync();

        var filePath = Path.Combine(_outputPath, "long-text.wav");
        await File.WriteAllBytesAsync(filePath, new byte[2000]);

        var eventBus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        TtsNotificationEvent? notification = null;
        eventBus.OnTtsNotification += evt => notification = evt;

        var processor = CreateProcessor(context, eventBus);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try { await processor.StartAsync(cts.Token); await Task.Delay(600); }
        catch (OperationCanceledException) { }
        finally { await processor.StopAsync(CancellationToken.None); }

        // Assert — notification text should be truncated to 50 chars + "..."
        Assert.NotNull(notification);
        Assert.Equal(53, notification.Text.Length); // 50 + "..."
        Assert.EndsWith("...", notification.Text);
    }

    // -------------------------------------------------------------------
    // Multiple interrupted jobs recovery
    // -------------------------------------------------------------------

    [Fact]
    public async Task RecoverInterruptedJobs_HandlesMultipleJobs()
    {
        // Arrange — model with no ContainerId so IsCurlRunningAsync is skipped
        // and Stopped so ProcessNextJobAsync will fail re-queued jobs
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "no-container-model",
            CheckpointPath = @"D:\models\test",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = null,
            Status = ModelStatus.Stopped
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        // Job 1: has a valid output file — should be promoted
        var completedJob = CreateJob(model.Id, TtsJobStatus.Processing, "multi-completed.wav");
        completedJob.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        // Job 2: no output file — should be re-queued (then failed by process loop)
        var stuckJob = CreateJob(model.Id, TtsJobStatus.Processing, "multi-stuck.wav");
        stuckJob.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-3);

        context.TtsJobs.AddRange(completedJob, stuckJob);
        await context.SaveChangesAsync();

        var completedFilePath = Path.Combine(_outputPath, "multi-completed.wav");
        await File.WriteAllBytesAsync(completedFilePath, new byte[3000]);

        var processor = CreateProcessor(context);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await processor.StartAsync(cts.Token); await Task.Delay(3100); }
        catch (OperationCanceledException) { }
        finally { await processor.StopAsync(CancellationToken.None); }

        // Assert
        // Completed job should be promoted (removed from TtsJobs, added to GenerationLogs)
        Assert.False(await context.TtsJobs.AnyAsync(j => j.Id == completedJob.Id));
        Assert.True(await context.GenerationLogs.AnyAsync(g => g.OutputFileName == "multi-completed.wav"));

        // Stuck job was re-queued, then picked up by process loop and failed
        // (no running model). The key assertion: it was NOT promoted.
        Assert.False(await context.GenerationLogs.AnyAsync(g => g.OutputFileName == "multi-stuck.wav"));
        var stuckFinal = await context.TtsJobs.FirstAsync(j => j.Id == stuckJob.Id);
        Assert.Equal(TtsJobStatus.Failed, stuckFinal.Status);
        Assert.Contains("No running model", stuckFinal.ErrorMessage!);
    }

    // -------------------------------------------------------------------
    // Edge case: CancelJobAsync on a Queued job
    // -------------------------------------------------------------------

    [Fact]
    public async Task CancelJobAsync_WorksOnQueuedJob()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var model = CreateRunningModel();
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var job = CreateJob(model.Id, TtsJobStatus.Queued);
        context.TtsJobs.Add(job);
        await context.SaveChangesAsync();

        var eventBus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        TtsJobStatusEvent? receivedEvent = null;
        eventBus.OnTtsJobStatus += evt => receivedEvent = evt;

        var processor = CreateProcessor(context, eventBus);

        // Act
        await processor.CancelJobAsync(job.Id);

        // Assert — even queued jobs can be cancelled
        var cancelled = await context.TtsJobs.FirstAsync(j => j.Id == job.Id);
        Assert.Equal(TtsJobStatus.Failed, cancelled.Status);
        Assert.Equal("Cancelled by user", cancelled.ErrorMessage);
        Assert.NotNull(cancelled.CompletedAt);
        Assert.NotNull(receivedEvent);
    }
}
