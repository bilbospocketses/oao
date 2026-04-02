using Docker.DotNet;
using Docker.DotNet.Models;
using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using OpenAudioOrchestrator.Web.Hubs;
using OpenAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace OpenAudioOrchestrator.Tests.Services;

public class HealthMonitorServiceTests
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

    private static IConfiguration CreateConfig(int intervalSeconds = 1)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAudioOrchestrator:HealthCheckIntervalSeconds"] = intervalSeconds.ToString()
            })
            .Build();
    }

    [Fact]
    public async Task CheckHealthAsync_SetsErrorOnFailure()
    {
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "unhealthy",
            CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = "container-1",
            Status = ModelStatus.Running
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var mockTts = new Mock<ITtsClientService>();
        mockTts.Setup(t => t.GetHealthAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        var mockDocker = new Mock<IDockerClient>();
        var scopeFactory = CreateScopeFactory(context, mockTts.Object);
        var (hubMock, gpuState) = CreateHubMocks();
        var service = new HealthMonitorService(
            scopeFactory, mockDocker.Object, CreateConfig(),
            NullLogger<HealthMonitorService>.Instance, hubMock.Object, gpuState, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance));

        // Need 5 consecutive failures to set Error status
        for (int i = 0; i < 5; i++)
            await service.CheckHealthAsync();

        var updated = await context.ModelProfiles.FirstAsync(m => m.Name == "unhealthy");
        Assert.Equal(ModelStatus.Error, updated.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ConfirmsRunningOnSuccess()
    {
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "healthy",
            CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = "container-1",
            Status = ModelStatus.Running
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var mockTts = new Mock<ITtsClientService>();
        mockTts.Setup(t => t.GetHealthAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        var mockDocker = new Mock<IDockerClient>();
        var scopeFactory = CreateScopeFactory(context, mockTts.Object);
        var (hubMock, gpuState) = CreateHubMocks();
        var service = new HealthMonitorService(
            scopeFactory, mockDocker.Object, CreateConfig(),
            NullLogger<HealthMonitorService>.Instance, hubMock.Object, gpuState, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance));

        await service.CheckHealthAsync();

        var updated = await context.ModelProfiles.FirstAsync(m => m.Name == "healthy");
        Assert.Equal(ModelStatus.Running, updated.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_DoesNothingWhenNoRunningModel()
    {
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "stopped",
            CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            Status = ModelStatus.Stopped
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var mockTts = new Mock<ITtsClientService>();
        var mockDocker = new Mock<IDockerClient>();
        var scopeFactory = CreateScopeFactory(context, mockTts.Object);
        var (hubMock, gpuState) = CreateHubMocks();
        var service = new HealthMonitorService(
            scopeFactory, mockDocker.Object, CreateConfig(),
            NullLogger<HealthMonitorService>.Instance, hubMock.Object, gpuState, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance));

        await service.CheckHealthAsync();

        mockTts.Verify(t => t.GetHealthAsync(It.IsAny<string>()), Times.Never);
    }

    private static IServiceScopeFactory CreateScopeFactory(AppDbContext context, ITtsClientService? ttsClient = null)
    {
        var mockScope = new Mock<IServiceScope>();
        var mockProvider = new Mock<IServiceProvider>();
        mockProvider.Setup(p => p.GetService(typeof(AppDbContext))).Returns(context);
        if (ttsClient is not null)
            mockProvider.Setup(p => p.GetService(typeof(ITtsClientService))).Returns(ttsClient);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockProvider.Object);

        var mockFactory = new Mock<IServiceScopeFactory>();
        mockFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);
        return mockFactory.Object;
    }

    [Fact]
    public async Task CheckHealthAsync_UsesDockerInspection_WhenActiveJobsExist()
    {
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "busy-model",
            CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = "container-busy",
            Status = ModelStatus.Running
        };
        context.ModelProfiles.Add(model);

        var job = new TtsJob
        {
            ModelProfileId = model.Id,
            InputText = "test",
            Format = "wav",
            OutputFileName = "test.wav",
            Status = TtsJobStatus.Processing
        };
        context.TtsJobs.Add(job);
        await context.SaveChangesAsync();

        var mockTts = new Mock<ITtsClientService>();
        mockTts.Setup(t => t.GetHealthAsync(It.IsAny<string>())).ReturnsAsync(true);

        var mockDocker = new Mock<IDockerClient>();
        var mockContainers = new Mock<Docker.DotNet.IContainerOperations>();
        mockContainers.Setup(c => c.InspectContainerAsync("container-busy", default))
            .ReturnsAsync(new ContainerInspectResponse
            {
                State = new ContainerState { Running = true }
            });
        mockDocker.Setup(d => d.Containers).Returns(mockContainers.Object);

        var scopeFactory = CreateScopeFactory(context, mockTts.Object);
        var (hubMock, gpuState) = CreateHubMocks();
        var service = new HealthMonitorService(
            scopeFactory, mockDocker.Object, CreateConfig(),
            NullLogger<HealthMonitorService>.Instance, hubMock.Object, gpuState, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance));

        await service.CheckHealthAsync();

        mockContainers.Verify(c => c.InspectContainerAsync("container-busy", default), Times.Once);
        mockTts.Verify(t => t.GetHealthAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CheckHealthAsync_DoesNotSetError_BeforeFiveFailures()
    {
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "almost-failing",
            CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = "container-4",
            Status = ModelStatus.Running
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var mockTts = new Mock<ITtsClientService>();
        mockTts.Setup(t => t.GetHealthAsync(It.IsAny<string>())).ReturnsAsync(false);

        var mockDocker = new Mock<IDockerClient>();
        var scopeFactory = CreateScopeFactory(context, mockTts.Object);
        var (hubMock, gpuState) = CreateHubMocks();
        var service = new HealthMonitorService(
            scopeFactory, mockDocker.Object, CreateConfig(),
            NullLogger<HealthMonitorService>.Instance, hubMock.Object, gpuState, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance));

        // Only 4 failures — should NOT set Error
        for (int i = 0; i < 4; i++)
            await service.CheckHealthAsync();

        var updated = await context.ModelProfiles.FirstAsync(m => m.Name == "almost-failing");
        Assert.Equal(ModelStatus.Running, updated.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ResetsConsecutiveFailures_OnSuccess()
    {
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "intermittent",
            CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = "container-int",
            Status = ModelStatus.Running
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var mockTts = new Mock<ITtsClientService>();
        var mockDocker = new Mock<IDockerClient>();
        var scopeFactory = CreateScopeFactory(context, mockTts.Object);
        var (hubMock, gpuState) = CreateHubMocks();
        var service = new HealthMonitorService(
            scopeFactory, mockDocker.Object, CreateConfig(),
            NullLogger<HealthMonitorService>.Instance, hubMock.Object, gpuState, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance));

        // Fail 3 times
        mockTts.Setup(t => t.GetHealthAsync(It.IsAny<string>())).ReturnsAsync(false);
        for (int i = 0; i < 3; i++)
            await service.CheckHealthAsync();

        // Succeed once — resets the counter
        mockTts.Setup(t => t.GetHealthAsync(It.IsAny<string>())).ReturnsAsync(true);
        await service.CheckHealthAsync();

        // Fail 4 more times — total consecutive is only 4, not 7
        mockTts.Setup(t => t.GetHealthAsync(It.IsAny<string>())).ReturnsAsync(false);
        for (int i = 0; i < 4; i++)
            await service.CheckHealthAsync();

        var updated = await context.ModelProfiles.FirstAsync(m => m.Name == "intermittent");
        Assert.Equal(ModelStatus.Running, updated.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_RecoverFromError_OnSuccessfulHealthCheck()
    {
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "recovering",
            CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            ContainerId = "container-err",
            Status = ModelStatus.Error
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var mockTts = new Mock<ITtsClientService>();
        mockTts.Setup(t => t.GetHealthAsync(It.IsAny<string>())).ReturnsAsync(true);

        var mockDocker = new Mock<IDockerClient>();
        var scopeFactory = CreateScopeFactory(context, mockTts.Object);
        var (hubMock, gpuState) = CreateHubMocks();
        var service = new HealthMonitorService(
            scopeFactory, mockDocker.Object, CreateConfig(),
            NullLogger<HealthMonitorService>.Instance, hubMock.Object, gpuState, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance));

        await service.CheckHealthAsync();

        var updated = await context.ModelProfiles.FirstAsync(m => m.Name == "recovering");
        Assert.Equal(ModelStatus.Running, updated.Status);
    }

    private static (Mock<IHubContext<OrchestratorHub>> hubMock, GpuMetricsState gpuState) CreateHubMocks()
    {
        var hubMock = new Mock<IHubContext<OrchestratorHub>>();
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        hubMock.Setup(h => h.Clients).Returns(clientsMock.Object);
        return (hubMock, new GpuMetricsState());
    }
}
