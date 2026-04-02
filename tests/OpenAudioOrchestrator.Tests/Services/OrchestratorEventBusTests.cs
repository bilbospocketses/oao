using OpenAudioOrchestrator.Web.Hubs;
using OpenAudioOrchestrator.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenAudioOrchestrator.Tests.Services;

public class OrchestratorEventBusTests
{
    private static ContainerStatusEvent CreateContainerStatus() =>
        new(ModelId: 1, Name: "test", Status: "Running", HostPort: 9001, LastStartedAt: DateTimeOffset.UtcNow);

    private static TtsNotificationEvent CreateTtsNotification() =>
        new(UserId: "user1", Text: "Hello", OutputFileName: "out.wav", DurationMs: 500, Success: true, Error: null);

    private static LogLineEvent CreateLogLine() =>
        new(ContainerId: "abc123", Timestamp: DateTimeOffset.UtcNow, Line: "info: started");

    private static GpuMetricsEvent CreateGpuMetrics() =>
        new(MemoryUsedMb: 2048, MemoryTotalMb: 8192, UtilizationPercent: 45);

    private static TtsJobStatusEvent CreateTtsJobStatus() =>
        new(JobId: 1, Status: "Completed", ErrorMessage: null);

    // --- RaiseContainerStatus ---

    [Fact]
    public void RaiseContainerStatus_NotifiesSubscribers()
    {
        var bus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        List<ContainerStatusEvent>? received = null;

        bus.OnContainerStatus += events => received = events;

        var expected = new List<ContainerStatusEvent> { CreateContainerStatus() };
        bus.RaiseContainerStatus(expected);

        Assert.NotNull(received);
        Assert.Same(expected, received);
    }

    [Fact]
    public void RaiseContainerStatus_DoesNotThrowWithNoSubscribers()
    {
        var bus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        var ex = Record.Exception(() => bus.RaiseContainerStatus(new List<ContainerStatusEvent> { CreateContainerStatus() }));
        Assert.Null(ex);
    }

    // --- RaiseTtsNotification ---

    [Fact]
    public void RaiseTtsNotification_NotifiesSubscribers()
    {
        var bus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        TtsNotificationEvent? received = null;

        bus.OnTtsNotification += evt => received = evt;

        var expected = CreateTtsNotification();
        bus.RaiseTtsNotification(expected);

        Assert.NotNull(received);
        Assert.Equal(expected, received);
    }

    // --- RaiseLogLine ---

    [Fact]
    public void RaiseLogLine_NotifiesSubscribers()
    {
        var bus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        LogLineEvent? received = null;

        bus.OnLogLine += evt => received = evt;

        var expected = CreateLogLine();
        bus.RaiseLogLine(expected);

        Assert.NotNull(received);
        Assert.Equal(expected, received);
    }

    // --- RaiseGpuMetrics ---

    [Fact]
    public void RaiseGpuMetrics_NotifiesSubscribers()
    {
        var bus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        GpuMetricsEvent? received = null;

        bus.OnGpuMetrics += evt => received = evt;

        var expected = CreateGpuMetrics();
        bus.RaiseGpuMetrics(expected);

        Assert.NotNull(received);
        Assert.Equal(expected, received);
    }

    // --- RaiseTtsJobStatus ---

    [Fact]
    public void RaiseTtsJobStatus_NotifiesSubscribers()
    {
        var bus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        TtsJobStatusEvent? received = null;

        bus.OnTtsJobStatus += evt => received = evt;

        var expected = CreateTtsJobStatus();
        bus.RaiseTtsJobStatus(expected);

        Assert.NotNull(received);
        Assert.Equal(expected, received);
    }

    // --- Multiple subscribers ---

    [Fact]
    public void MultipleSubscribers_AllReceiveEvent()
    {
        var bus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        TtsNotificationEvent? received1 = null;
        TtsNotificationEvent? received2 = null;

        bus.OnTtsNotification += evt => received1 = evt;
        bus.OnTtsNotification += evt => received2 = evt;

        var expected = CreateTtsNotification();
        bus.RaiseTtsNotification(expected);

        Assert.Equal(expected, received1);
        Assert.Equal(expected, received2);
    }

    // --- Unsubscribe ---

    [Fact]
    public void UnsubscribedHandler_DoesNotReceiveEvent()
    {
        var bus = new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance);
        TtsNotificationEvent? received = null;

        void Handler(TtsNotificationEvent evt) => received = evt;

        bus.OnTtsNotification += Handler;
        bus.OnTtsNotification -= Handler;

        bus.RaiseTtsNotification(CreateTtsNotification());

        Assert.Null(received);
    }
}
