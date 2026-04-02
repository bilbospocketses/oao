using Docker.DotNet;
using Docker.DotNet.Models;
using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using OpenAudioOrchestrator.Web.Hubs;
using OpenAudioOrchestrator.Web.Proxy;
using OpenAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace OpenAudioOrchestrator.Tests.Services;

public class DockerOrchestratorServiceTests
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

    private static Mock<IHubContext<OrchestratorHub>> CreateHubMock()
    {
        var hubMock = new Mock<IHubContext<OrchestratorHub>>();
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        hubMock.Setup(h => h.Clients).Returns(clientsMock.Object);
        return hubMock;
    }

    private static Mock<IDockerClient> CreateMockDockerClient()
    {
        var mock = new Mock<IDockerClient>();
        var containerOps = new Mock<IContainerOperations>();

        containerOps.Setup(c => c.CreateContainerAsync(
                It.IsAny<CreateContainerParameters>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateContainerResponse { ID = "abc123def456" });

        containerOps.Setup(c => c.StartContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerStartParameters>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        containerOps.Setup(c => c.StopContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerStopParameters>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        containerOps.Setup(c => c.RemoveContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerRemoveParameters>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        containerOps.Setup(c => c.InspectContainerAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInspectResponse
            {
                State = new ContainerState { Status = "running", Running = true }
            });

        mock.Setup(d => d.Containers).Returns(containerOps.Object);
        return mock;
    }

    [Fact]
    public async Task CreateAndStartModelAsync_CreatesContainerAndUpdatesProfile()
    {
        using var context = CreateInMemoryContext();
        var mockDocker = CreateMockDockerClient();
        var mockConfig = new Mock<IContainerConfigService>();
        mockConfig.Setup(c => c.BuildCreateParams(It.IsAny<ModelProfile>()))
            .Returns(new CreateContainerParameters { Image = "test" });

        var mockProxy = new Mock<ProxyConfigProvider>();
        var mockNetwork = new Mock<IDockerNetworkService>();
        mockNetwork.Setup(n => n.GetContainerIpAsync(It.IsAny<string>()))
            .ReturnsAsync("172.18.0.5");

        var profile = new ModelProfile
        {
            Name = "test", CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001, Status = ModelStatus.Created
        };
        context.ModelProfiles.Add(profile);
        await context.SaveChangesAsync();

        var service = new DockerOrchestratorService(mockDocker.Object, mockConfig.Object, context, mockProxy.Object, mockNetwork.Object, CreateHubMock().Object, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance), NullLogger<DockerOrchestratorService>.Instance);
        await service.CreateAndStartModelAsync(profile);

        Assert.Equal("abc123def456", profile.ContainerId);
        Assert.Equal(ModelStatus.Running, profile.Status);
        Assert.NotNull(profile.LastStartedAt);
    }

    [Fact]
    public async Task StopModelAsync_StopsContainerAndUpdatesStatus()
    {
        using var context = CreateInMemoryContext();
        var mockDocker = CreateMockDockerClient();
        var mockConfig = new Mock<IContainerConfigService>();
        var mockProxy = new Mock<ProxyConfigProvider>();
        var mockNetwork = new Mock<IDockerNetworkService>();

        var profile = new ModelProfile
        {
            Name = "running-model", CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001, ContainerId = "abc123def456", Status = ModelStatus.Running
        };
        context.ModelProfiles.Add(profile);
        await context.SaveChangesAsync();

        var service = new DockerOrchestratorService(mockDocker.Object, mockConfig.Object, context, mockProxy.Object, mockNetwork.Object, CreateHubMock().Object, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance), NullLogger<DockerOrchestratorService>.Instance);
        await service.StopModelAsync(profile);

        Assert.Equal(ModelStatus.Stopped, profile.Status);
        mockDocker.Verify(d => d.Containers.StopContainerAsync(
            "abc123def456", It.IsAny<ContainerStopParameters>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RemoveModelAsync_DeletesProfileAndRemovesContainer()
    {
        using var context = CreateInMemoryContext();
        var mockDocker = CreateMockDockerClient();
        var mockConfig = new Mock<IContainerConfigService>();
        var mockProxy = new Mock<ProxyConfigProvider>();
        var mockNetwork = new Mock<IDockerNetworkService>();

        var profile = new ModelProfile
        {
            Name = "to-remove", CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001, ContainerId = "abc123def456", Status = ModelStatus.Stopped
        };
        context.ModelProfiles.Add(profile);
        await context.SaveChangesAsync();
        var profileId = profile.Id;

        var service = new DockerOrchestratorService(mockDocker.Object, mockConfig.Object, context, mockProxy.Object, mockNetwork.Object, CreateHubMock().Object, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance), NullLogger<DockerOrchestratorService>.Instance);
        await service.RemoveModelAsync(profile);

        // Entity should be deleted from the database
        var deleted = await context.ModelProfiles.FindAsync(profileId);
        Assert.Null(deleted);

        // Docker container removal should have been called
        mockDocker.Verify(d => d.Containers.RemoveContainerAsync(
            "abc123def456", It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SwapModelAsync_StopsCurrentBeforeStartingNew()
    {
        using var context = CreateInMemoryContext();
        var mockDocker = CreateMockDockerClient();
        var mockConfig = new Mock<IContainerConfigService>();
        mockConfig.Setup(c => c.BuildCreateParams(It.IsAny<ModelProfile>()))
            .Returns(new CreateContainerParameters { Image = "test" });

        var mockProxy = new Mock<ProxyConfigProvider>();
        var mockNetwork = new Mock<IDockerNetworkService>();
        mockNetwork.Setup(n => n.GetContainerIpAsync(It.IsAny<string>()))
            .ReturnsAsync("172.18.0.5");

        var running = new ModelProfile
        {
            Name = "current", CheckpointPath = @"D:\path1",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001, ContainerId = "aabbccddeeff", Status = ModelStatus.Running
        };
        var newModel = new ModelProfile
        {
            Name = "new-model", CheckpointPath = @"D:\path2",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9002, Status = ModelStatus.Created
        };
        context.ModelProfiles.AddRange(running, newModel);
        await context.SaveChangesAsync();

        var service = new DockerOrchestratorService(mockDocker.Object, mockConfig.Object, context, mockProxy.Object, mockNetwork.Object, CreateHubMock().Object, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance), NullLogger<DockerOrchestratorService>.Instance);
        await service.SwapModelAsync(newModel);

        Assert.Equal(ModelStatus.Stopped, running.Status);
        Assert.Equal(ModelStatus.Running, newModel.Status);
        Assert.Equal("abc123def456", newModel.ContainerId);
    }

    [Fact]
    public async Task GetContainerStatusAsync_ReturnsDockerState()
    {
        using var context = CreateInMemoryContext();
        var mockDocker = CreateMockDockerClient();
        var mockConfig = new Mock<IContainerConfigService>();
        var mockProxy = new Mock<ProxyConfigProvider>();
        var mockNetwork = new Mock<IDockerNetworkService>();

        var service = new DockerOrchestratorService(mockDocker.Object, mockConfig.Object, context, mockProxy.Object, mockNetwork.Object, CreateHubMock().Object, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance), NullLogger<DockerOrchestratorService>.Instance);
        var status = await service.GetContainerStatusAsync("abc123def456");

        Assert.Equal("running", status);
    }

    [Fact]
    public async Task CreateAndStartModelAsync_NotifiesProxyWithContainerIp()
    {
        using var context = CreateInMemoryContext();
        var mockDocker = CreateMockDockerClient();
        var mockConfig = new Mock<IContainerConfigService>();
        mockConfig.Setup(c => c.BuildCreateParams(It.IsAny<ModelProfile>()))
            .Returns(new CreateContainerParameters { Image = "test" });

        var mockProxy = new Mock<ProxyConfigProvider>();
        var mockNetwork = new Mock<IDockerNetworkService>();
        mockNetwork.Setup(n => n.GetContainerIpAsync("abc123def456"))
            .ReturnsAsync("172.18.0.5");

        var profile = new ModelProfile
        {
            Name = "proxy-test",
            CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            Status = ModelStatus.Created
        };
        context.ModelProfiles.Add(profile);
        await context.SaveChangesAsync();

        var service = new DockerOrchestratorService(
            mockDocker.Object, mockConfig.Object, context, mockProxy.Object, mockNetwork.Object, CreateHubMock().Object, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance), NullLogger<DockerOrchestratorService>.Instance);

        await service.CreateAndStartModelAsync(profile);

        Assert.Equal("abc123def456", profile.ContainerId);
        mockProxy.Verify(p => p.UpdateDestination("http://172.18.0.5:8080"), Times.Once);
    }

    [Fact]
    public async Task StopModelAsync_ClearsProxyDestination()
    {
        using var context = CreateInMemoryContext();
        var mockDocker = CreateMockDockerClient();
        var mockConfig = new Mock<IContainerConfigService>();
        var mockProxy = new Mock<ProxyConfigProvider>();
        var mockNetwork = new Mock<IDockerNetworkService>();

        var profile = new ModelProfile
        {
            Name = "stop-proxy-test",
            CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = "abc123def456",
            Status = ModelStatus.Running
        };
        context.ModelProfiles.Add(profile);
        await context.SaveChangesAsync();

        var service = new DockerOrchestratorService(
            mockDocker.Object, mockConfig.Object, context, mockProxy.Object, mockNetwork.Object, CreateHubMock().Object, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance), NullLogger<DockerOrchestratorService>.Instance);

        await service.StopModelAsync(profile);

        mockProxy.Verify(p => p.ClearDestination(), Times.Once);
    }

    [Fact]
    public async Task StopModelAsync_InvalidContainerId_ReturnsEarlyWithoutCallingDocker()
    {
        using var context = CreateInMemoryContext();
        var mockDocker = CreateMockDockerClient();
        var mockConfig = new Mock<IContainerConfigService>();
        var mockProxy = new Mock<ProxyConfigProvider>();
        var mockNetwork = new Mock<IDockerNetworkService>();

        var profile = new ModelProfile
        {
            Name = "bad-id-model",
            CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = "not-a-valid-container-id!",
            Status = ModelStatus.Running
        };
        context.ModelProfiles.Add(profile);
        await context.SaveChangesAsync();

        var service = new DockerOrchestratorService(
            mockDocker.Object, mockConfig.Object, context, mockProxy.Object, mockNetwork.Object, CreateHubMock().Object, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance), NullLogger<DockerOrchestratorService>.Instance);

        await service.StopModelAsync(profile);

        // Status should be unchanged — we returned early without calling Docker
        Assert.Equal(ModelStatus.Running, profile.Status);
        mockDocker.Verify(d => d.Containers.StopContainerAsync(
            It.IsAny<string>(), It.IsAny<ContainerStopParameters>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RemoveModelAsync_InvalidContainerId_SkipsDockerCallButDeletesProfile()
    {
        using var context = CreateInMemoryContext();
        var mockDocker = CreateMockDockerClient();
        var mockConfig = new Mock<IContainerConfigService>();
        var mockProxy = new Mock<ProxyConfigProvider>();
        var mockNetwork = new Mock<IDockerNetworkService>();

        var profile = new ModelProfile
        {
            Name = "bad-id-remove",
            CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = "INVALID_ID",
            Status = ModelStatus.Stopped
        };
        context.ModelProfiles.Add(profile);
        await context.SaveChangesAsync();
        var profileId = profile.Id;

        var service = new DockerOrchestratorService(
            mockDocker.Object, mockConfig.Object, context, mockProxy.Object, mockNetwork.Object, CreateHubMock().Object, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance), NullLogger<DockerOrchestratorService>.Instance);

        await service.RemoveModelAsync(profile);

        // Profile should still be removed from DB
        var deleted = await context.ModelProfiles.FindAsync(profileId);
        Assert.Null(deleted);

        // Docker removal should NOT have been called
        mockDocker.Verify(d => d.Containers.RemoveContainerAsync(
            It.IsAny<string>(), It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAndStartModelAsync_InvalidExistingContainerId_ReturnsEarlyWithoutCallingDocker()
    {
        using var context = CreateInMemoryContext();
        var mockDocker = CreateMockDockerClient();
        var mockConfig = new Mock<IContainerConfigService>();
        var mockProxy = new Mock<ProxyConfigProvider>();
        var mockNetwork = new Mock<IDockerNetworkService>();

        var profile = new ModelProfile
        {
            Name = "bad-existing-id",
            CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = "not-hex!",
            Status = ModelStatus.Stopped
        };
        context.ModelProfiles.Add(profile);
        await context.SaveChangesAsync();

        var service = new DockerOrchestratorService(
            mockDocker.Object, mockConfig.Object, context, mockProxy.Object, mockNetwork.Object, CreateHubMock().Object, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance), NullLogger<DockerOrchestratorService>.Instance);

        await service.CreateAndStartModelAsync(profile);

        // Status should be unchanged — we returned early without calling Docker
        Assert.Equal(ModelStatus.Stopped, profile.Status);
        mockDocker.Verify(d => d.Containers.InspectContainerAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        mockDocker.Verify(d => d.Containers.StartContainerAsync(
            It.IsAny<string>(), It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
