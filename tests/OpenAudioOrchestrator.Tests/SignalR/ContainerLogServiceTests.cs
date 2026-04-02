using OpenAudioOrchestrator.Web.Hubs;
using OpenAudioOrchestrator.Web.Services;
using Docker.DotNet;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace OpenAudioOrchestrator.Tests.SignalR;

public class ContainerLogServiceTests
{
    private static (ContainerLogService service, Mock<IHubContext<OrchestratorHub>> hubMock) CreateService()
    {
        var dockerMock = new Mock<IDockerClient>();
        var hubMock = new Mock<IHubContext<OrchestratorHub>>();
        var clientsMock = new Mock<IHubClients>();
        var singleClientProxyMock = new Mock<ISingleClientProxy>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.Client(It.IsAny<string>())).Returns(singleClientProxyMock.Object);
        clientsMock.Setup(c => c.Clients(It.IsAny<IReadOnlyList<string>>())).Returns(clientProxyMock.Object);
        hubMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        var service = new ContainerLogService(dockerMock.Object, hubMock.Object, NullLogger<ContainerLogService>.Instance);
        return (service, hubMock);
    }

    // Valid 12-char hex container IDs used throughout these tests
    private const string ContainerId1 = "aabbccddeeff";
    private const string ContainerId2 = "112233445566";

    [Fact]
    public async Task Subscribe_AddsConnectionToContainer()
    {
        var (service, _) = CreateService();
        await service.SubscribeAsync(ContainerId1, "conn-a");
        Assert.True(service.HasSubscribers(ContainerId1));
    }

    [Fact]
    public async Task Unsubscribe_RemovesConnectionFromContainer()
    {
        var (service, _) = CreateService();
        await service.SubscribeAsync(ContainerId1, "conn-a");
        await service.UnsubscribeAsync(ContainerId1, "conn-a");
        Assert.False(service.HasSubscribers(ContainerId1));
    }

    [Fact]
    public async Task UnsubscribeAll_RemovesConnectionFromAllContainers()
    {
        var (service, _) = CreateService();
        await service.SubscribeAsync(ContainerId1, "conn-a");
        await service.SubscribeAsync(ContainerId2, "conn-a");
        await service.UnsubscribeAllAsync("conn-a");
        Assert.False(service.HasSubscribers(ContainerId1));
        Assert.False(service.HasSubscribers(ContainerId2));
    }

    [Fact]
    public async Task MultipleSubscribers_StreamSurvivesPartialUnsubscribe()
    {
        var (service, _) = CreateService();
        await service.SubscribeAsync(ContainerId1, "conn-a");
        await service.SubscribeAsync(ContainerId1, "conn-b");
        await service.UnsubscribeAsync(ContainerId1, "conn-a");
        Assert.True(service.HasSubscribers(ContainerId1));
    }

    [Fact]
    public void SubscribeCallback_AddsCallback()
    {
        var (service, _) = CreateService();
        Action<LogLineEvent> callback = _ => { };

        service.SubscribeCallback(ContainerId1, "sub-1", callback);

        Assert.True(service.HasSubscribers(ContainerId1));
    }

    [Fact]
    public void UnsubscribeCallback_RemovesCallback()
    {
        var (service, _) = CreateService();
        Action<LogLineEvent> callback = _ => { };

        service.SubscribeCallback(ContainerId1, "sub-1", callback);
        Assert.True(service.HasSubscribers(ContainerId1));

        service.UnsubscribeCallback(ContainerId1, "sub-1");
        Assert.False(service.HasSubscribers(ContainerId1));
    }

    [Fact]
    public void UnsubscribeAllCallbacks_RemovesAllForContainer()
    {
        var (service, _) = CreateService();
        Action<LogLineEvent> callback1 = _ => { };
        Action<LogLineEvent> callback2 = _ => { };

        service.SubscribeCallback(ContainerId1, "sub-1", callback1);
        service.SubscribeCallback(ContainerId2, "sub-1", callback2);
        Assert.True(service.HasSubscribers(ContainerId1));
        Assert.True(service.HasSubscribers(ContainerId2));

        service.UnsubscribeAllCallbacks("sub-1");
        Assert.False(service.HasSubscribers(ContainerId1));
        Assert.False(service.HasSubscribers(ContainerId2));
    }

    // BUG-04 regression tests: unsubscribe methods must not cancel the stream
    // when the other subscriber type still has active subscribers.

    [Fact]
    public async Task UnsubscribeAsync_DoesNotCancelStream_WhenCallbackSubscriberStillPresent()
    {
        var (service, _) = CreateService();
        Action<LogLineEvent> callback = _ => { };

        await service.SubscribeAsync(ContainerId1, "conn-a");
        service.SubscribeCallback(ContainerId1, "sub-1", callback);

        // Remove the SignalR subscriber — callback subscriber is still active.
        await service.UnsubscribeAsync(ContainerId1, "conn-a");

        // The callback subscriber keeps HasSubscribers true.
        Assert.True(service.HasSubscribers(ContainerId1));
    }

    [Fact]
    public async Task UnsubscribeAllAsync_DoesNotCancelStream_WhenCallbackSubscriberStillPresent()
    {
        var (service, _) = CreateService();
        Action<LogLineEvent> callback = _ => { };

        await service.SubscribeAsync(ContainerId1, "conn-a");
        service.SubscribeCallback(ContainerId1, "sub-1", callback);

        // Remove the SignalR subscriber via UnsubscribeAllAsync — callback subscriber is still active.
        await service.UnsubscribeAllAsync("conn-a");

        Assert.True(service.HasSubscribers(ContainerId1));
    }

    [Fact]
    public void UnsubscribeCallback_DoesNotCancelStream_WhenSignalRSubscriberStillPresent()
    {
        var (service, _) = CreateService();
        Action<LogLineEvent> callback = _ => { };

        service.SubscribeCallback(ContainerId1, "sub-1", callback);
        // Manually subscribe a SignalR connection (SubscribeAsync without Docker = task is started but ignored here)
        _ = service.SubscribeAsync(ContainerId1, "conn-a");

        // Remove the callback subscriber — SignalR subscriber is still active.
        service.UnsubscribeCallback(ContainerId1, "sub-1");

        Assert.True(service.HasSubscribers(ContainerId1));
    }

    [Fact]
    public async Task UnsubscribeAsync_CancelsStream_WhenBothSubscriberTypesGone()
    {
        var (service, _) = CreateService();
        Action<LogLineEvent> callback = _ => { };

        await service.SubscribeAsync(ContainerId1, "conn-a");
        service.SubscribeCallback(ContainerId1, "sub-1", callback);

        // Remove both subscriber types.
        await service.UnsubscribeAsync(ContainerId1, "conn-a");
        service.UnsubscribeCallback(ContainerId1, "sub-1");

        Assert.False(service.HasSubscribers(ContainerId1));
    }

    [Fact]
    public void UnsubscribeCallback_CancelsStream_WhenBothSubscriberTypesGone()
    {
        var (service, _) = CreateService();
        Action<LogLineEvent> callback = _ => { };

        service.SubscribeCallback(ContainerId1, "sub-1", callback);
        // No SignalR subscribers — removing the sole callback subscriber must clear everything.
        service.UnsubscribeCallback(ContainerId1, "sub-1");

        Assert.False(service.HasSubscribers(ContainerId1));
    }
}
