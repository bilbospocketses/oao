using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using OpenAudioOrchestrator.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace OpenAudioOrchestrator.Tests.Services;

public class ContainerConfigServiceTests
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

    private static IConfiguration CreateConfig(int portStart = 9001, int portEnd = 9099,
        string dataRoot = @"D:\DockerData\OpenAudio")
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAudioOrchestrator:PortRange:Start"] = portStart.ToString(),
                ["OpenAudioOrchestrator:PortRange:End"] = portEnd.ToString(),
                ["OpenAudioOrchestrator:DataRoot"] = dataRoot,
                ["OpenAudioOrchestrator:DefaultImageTag"] = "fishaudio/fish-speech:server-cuda",
                ["OpenAudioOrchestrator:DockerNetworkName"] = "oao-network"
            })
            .Build();
    }

    [Fact]
    public void BuildCreateParams_SetsImageAndName()
    {
        using var context = CreateInMemoryContext();
        var service = new ContainerConfigService(CreateConfig(), context);
        var profile = new ModelProfile
        {
            Name = "test-model",
            CheckpointPath = @"D:\DockerData\OpenAudio\Checkpoints\test-model",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            EnableHalf = true,
            Status = ModelStatus.Created
        };
        var result = service.BuildCreateParams(profile);
        Assert.Equal("fishaudio/fish-speech:server-cuda", result.Image);
        Assert.Equal("oao-test-model", result.Name);
    }

    [Fact]
    public void BuildCreateParams_ConfiguresGpuDeviceRequest()
    {
        using var context = CreateInMemoryContext();
        var service = new ContainerConfigService(CreateConfig(), context);
        var profile = new ModelProfile
        {
            Name = "gpu-test", CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda", HostPort = 9001,
            EnableHalf = false, Status = ModelStatus.Created
        };
        var result = service.BuildCreateParams(profile);
        Assert.NotNull(result.HostConfig.DeviceRequests);
        var gpu = Assert.Single(result.HostConfig.DeviceRequests);
        Assert.Equal(-1, gpu.Count);
        Assert.Contains(gpu.Capabilities, c => c.Contains("gpu"));
    }

    [Fact]
    public void BuildCreateParams_SetsPortBinding()
    {
        using var context = CreateInMemoryContext();
        var service = new ContainerConfigService(CreateConfig(), context);
        var profile = new ModelProfile
        {
            Name = "port-test", CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda", HostPort = 9005,
            EnableHalf = true, Status = ModelStatus.Created
        };
        var result = service.BuildCreateParams(profile);
        Assert.True(result.HostConfig.PortBindings.ContainsKey("8080/tcp"));
        var binding = Assert.Single(result.HostConfig.PortBindings["8080/tcp"]);
        Assert.Equal("9005", binding.HostPort);
    }

    [Fact]
    public void BuildCreateParams_SetsVolumeBinds()
    {
        using var context = CreateInMemoryContext();
        var service = new ContainerConfigService(
            CreateConfig(dataRoot: @"D:\DockerData\OpenAudio"), context);
        var profile = new ModelProfile
        {
            Name = "vol-test",
            CheckpointPath = @"D:\DockerData\OpenAudio\Checkpoints\s2-pro",
            ImageTag = "fishaudio/fish-speech:server-cuda", HostPort = 9001,
            EnableHalf = true, Status = ModelStatus.Created
        };
        var result = service.BuildCreateParams(profile);
        Assert.Contains(result.HostConfig.Binds,
            b => b == @"D:\DockerData\OpenAudio\Checkpoints:/app/checkpoints");
        Assert.Contains(result.HostConfig.Binds,
            b => b == @"D:\DockerData\OpenAudio\References:/app/references");
        Assert.Contains(result.HostConfig.Binds,
            b => b == @"D:\DockerData\OpenAudio\Output:/app/output");
    }

    [Fact]
    public void BuildCreateParams_SetsHalfFlagViaEnvVar()
    {
        using var context = CreateInMemoryContext();
        var service = new ContainerConfigService(CreateConfig(), context);
        var profile = new ModelProfile
        {
            Name = "half-test", CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda", HostPort = 9001,
            EnableHalf = true, Status = ModelStatus.Created
        };
        var result = service.BuildCreateParams(profile);
        Assert.Contains(result.Cmd, c => c == "--half");
    }

    [Fact]
    public void BuildCreateParams_OmitsHalfFlagWhenDisabled()
    {
        using var context = CreateInMemoryContext();
        var service = new ContainerConfigService(CreateConfig(), context);
        var profile = new ModelProfile
        {
            Name = "no-half", CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda", HostPort = 9001,
            EnableHalf = false, Status = ModelStatus.Created
        };
        var result = service.BuildCreateParams(profile);
        Assert.DoesNotContain(result.Cmd, c => c == "--half");
    }

    [Fact]
    public void BuildCreateParams_SetsCudaAllocConf()
    {
        using var context = CreateInMemoryContext();
        var service = new ContainerConfigService(CreateConfig(), context);
        var profile = new ModelProfile
        {
            Name = "cuda-test", CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda", HostPort = 9001,
            EnableHalf = true, CudaAllocConf = "max_split_size_mb:512",
            Status = ModelStatus.Created
        };
        var result = service.BuildCreateParams(profile);
        Assert.Contains(result.Env, e => e == "PYTORCH_CUDA_ALLOC_CONF=max_split_size_mb:512");
    }

    [Fact]
    public async Task AllocatePortAsync_ReturnsFirstAvailablePort()
    {
        using var context = CreateInMemoryContext();
        context.ModelProfiles.Add(new ModelProfile
        {
            Name = "existing", CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda", HostPort = 9001,
            Status = ModelStatus.Created
        });
        await context.SaveChangesAsync();
        var service = new ContainerConfigService(CreateConfig(portStart: 9001, portEnd: 9099), context);
        var port = await service.AllocatePortAsync();
        Assert.Equal(9002, port);
    }

    [Fact]
    public void BuildCreateParams_AttachesToBridgeNetwork()
    {
        using var context = CreateInMemoryContext();
        var service = new ContainerConfigService(CreateConfig(), context);
        var profile = new ModelProfile
        {
            Name = "net-test",
            CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            EnableHalf = true,
            Status = ModelStatus.Created
        };

        var result = service.BuildCreateParams(profile);

        Assert.NotNull(result.NetworkingConfig);
        Assert.True(result.NetworkingConfig.EndpointsConfig.ContainsKey("oao-network"));
    }

    [Fact]
    public async Task AllocatePortAsync_ReturnsStartWhenNoneUsed()
    {
        using var context = CreateInMemoryContext();
        var service = new ContainerConfigService(CreateConfig(portStart: 9001, portEnd: 9099), context);
        var port = await service.AllocatePortAsync();
        Assert.Equal(9001, port);
    }
}
