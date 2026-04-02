using Docker.DotNet.Models;
using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace OpenAudioOrchestrator.Web.Services;

public class ContainerConfigService : IContainerConfigService
{
    private readonly IConfiguration _config;
    private readonly AppDbContext _context;
    private readonly string _networkName;

    public ContainerConfigService(IConfiguration config, AppDbContext context)
    {
        _config = config;
        _context = context;
        _networkName = config["OpenAudioOrchestrator:DockerNetworkName"] ?? "oao-network";
    }

    public CreateContainerParameters BuildCreateParams(ModelProfile profile)
    {
        var dataRoot = _config["OpenAudioOrchestrator:DataRoot"]!;

        var checkpointsRoot = Path.Combine(dataRoot, "Checkpoints");
        var subFolder = Path.GetRelativePath(checkpointsRoot, profile.CheckpointPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var containerCheckpointPath = $"checkpoints/{subFolder.Replace('\\', '/')}";

        var tz = TimeZoneInfo.Local.Id;
        // Convert Windows timezone ID to IANA for Linux containers
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(tz, out var ianaId))
            tz = ianaId;

        var env = new List<string>
        {
            "COMPILE=0",
            $"TZ={tz}",
            $"LLAMA_CHECKPOINT_PATH={containerCheckpointPath}",
            $"DECODER_CHECKPOINT_PATH={containerCheckpointPath}/codec.pth"
        };

        if (!string.IsNullOrWhiteSpace(profile.CudaAllocConf))
        {
            env.Add($"PYTORCH_CUDA_ALLOC_CONF={profile.CudaAllocConf}");
        }

        var cmd = new List<string>();
        if (profile.EnableHalf)
        {
            cmd.Add("--half");
        }

        return new CreateContainerParameters
        {
            Image = profile.ImageTag,
            Name = $"oao-{profile.Name}",
            Env = env,
            Cmd = cmd,
            ExposedPorts = new Dictionary<string, EmptyStruct>
            {
                ["8080/tcp"] = default
            },
            HostConfig = new HostConfig
            {
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    ["8080/tcp"] = new List<PortBinding>
                    {
                        new PortBinding { HostIP = "127.0.0.1", HostPort = profile.HostPort.ToString() }
                    }
                },
                Binds = new List<string>
                {
                    $@"{Path.Combine(dataRoot, "Checkpoints")}:/app/checkpoints",
                    $@"{Path.Combine(dataRoot, "References")}:/app/references",
                    $@"{Path.Combine(dataRoot, "Output")}:/app/output"
                },
                DeviceRequests = new List<DeviceRequest>
                {
                    new DeviceRequest
                    {
                        Count = -1,
                        Driver = "nvidia",
                        Capabilities = new List<IList<string>>
                        {
                            new List<string> { "gpu" }
                        }
                    }
                }
            },
            NetworkingConfig = new NetworkingConfig
            {
                EndpointsConfig = new Dictionary<string, EndpointSettings>
                {
                    [_networkName] = new EndpointSettings()
                }
            }
        };
    }

    public async Task<int> AllocatePortAsync()
    {
        var start = int.Parse(_config["OpenAudioOrchestrator:PortRange:Start"]!);
        var end = int.Parse(_config["OpenAudioOrchestrator:PortRange:End"]!);

        var usedPorts = await _context.ModelProfiles
            .Select(m => m.HostPort)
            .ToListAsync();

        for (int port = start; port <= end; port++)
        {
            if (!usedPorts.Contains(port))
                return port;
        }

        throw new InvalidOperationException($"No available ports in range {start}-{end}");
    }
}
