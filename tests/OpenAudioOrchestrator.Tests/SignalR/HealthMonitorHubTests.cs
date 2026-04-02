using Docker.DotNet;
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

namespace OpenAudioOrchestrator.Tests.SignalR;

public class HealthMonitorHubTests
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

    private static IConfiguration CreateConfig()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAudioOrchestrator:HealthCheckIntervalSeconds"] = "1"
            })
            .Build();
    }

    private static IServiceScopeFactory CreateScopeFactory(AppDbContext context, ITtsClientService ttsClient)
    {
        var mockScope = new Mock<IServiceScope>();
        var mockProvider = new Mock<IServiceProvider>();
        mockProvider.Setup(p => p.GetService(typeof(AppDbContext))).Returns(context);
        mockProvider.Setup(p => p.GetService(typeof(ITtsClientService))).Returns(ttsClient);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockProvider.Object);
        var mockFactory = new Mock<IServiceScopeFactory>();
        mockFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);
        return mockFactory.Object;
    }

    [Fact]
    public async Task CheckHealthAsync_PushesContainerStatusToHub()
    {
        var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "test-model",
            CheckpointPath = @"D:\path",
            ImageTag = "test:latest",
            HostPort = 9001,
            ContainerId = "container-1",
            Status = ModelStatus.Running
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();

        var mockTts = new Mock<ITtsClientService>();
        mockTts.Setup(t => t.GetHealthAsync(It.IsAny<string>())).ReturnsAsync(true);

        var mockDocker = new Mock<IDockerClient>();

        var mockHub = new Mock<IHubContext<OrchestratorHub>>();
        var mockClients = new Mock<IHubClients>();
        var mockAll = new Mock<IClientProxy>();
        mockClients.Setup(c => c.All).Returns(mockAll.Object);
        mockHub.Setup(h => h.Clients).Returns(mockClients.Object);

        var gpuState = new GpuMetricsState();

        var service = new HealthMonitorService(
            CreateScopeFactory(context, mockTts.Object), mockDocker.Object, CreateConfig(),
            NullLogger<HealthMonitorService>.Instance, mockHub.Object, gpuState, new OrchestratorEventBus(NullLogger<OrchestratorEventBus>.Instance));

        await service.CheckHealthAsync();

        mockAll.Verify(c => c.SendCoreAsync(
            "ReceiveContainerStatus",
            It.Is<object?[]>(args => args.Length == 1),
            default), Times.Once);
    }
}
