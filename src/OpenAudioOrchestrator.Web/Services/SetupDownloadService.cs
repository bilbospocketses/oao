using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OpenAudioOrchestrator.Web.Services;

/// <summary>
/// Manages pre-flight checks and background downloads for the setup wizard.
/// Registered as a singleton to persist download state across requests.
/// </summary>
public partial class SetupDownloadService
{
    private readonly ILogger<SetupDownloadService> _logger;
    private Process? _modelDownloadProcess;
    private Process? _dockerPullProcess;
    private readonly List<string> _modelDownloadOutput = new();
    private readonly List<string> _dockerPullOutput = new();
    private readonly object _lock = new();

    public bool IsModelDownloading => _modelDownloadProcess is not null && !_modelDownloadProcess.HasExited;
    public bool IsDockerPulling => _dockerPullProcess is not null && !_dockerPullProcess.HasExited;
    public bool ModelDownloadCompleted { get; private set; }
    public bool DockerPullCompleted { get; private set; }
    public string? ModelDownloadError { get; private set; }
    public string? DockerPullError { get; private set; }
    public bool HasActiveDownloads => IsModelDownloading || IsDockerPulling;

    // Docker image tags: [registry/]name[:tag] — alphanumeric, dots, hyphens, underscores, slashes, colons
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._/:-]*$")]
    private static partial Regex ValidImageTagRegex();

    // Characters that could enable shell injection when interpolated into process arguments
    [GeneratedRegex(@"[`$|;&<>!""'\x00]")]
    private static partial Regex UnsafePathCharsRegex();

    public SetupDownloadService(ILogger<SetupDownloadService> logger)
    {
        _logger = logger;
    }

    internal static void ValidateImageTag(string imageTag)
    {
        if (string.IsNullOrWhiteSpace(imageTag) || !ValidImageTagRegex().IsMatch(imageTag))
            throw new ArgumentException($"Invalid Docker image tag format: {imageTag}");
    }

    internal static void ValidatePath(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"{paramName} cannot be empty.");
        if (UnsafePathCharsRegex().IsMatch(path))
            throw new ArgumentException($"{paramName} contains unsafe characters: {path}");
    }

    // --- Pre-checks ---

    public async Task<(bool available, string? error)> CheckGitAsync()
    {
        try
        {
            var (exitCode, _) = await RunCommandAsync("git", "--version");
            if (exitCode != 0)
                return (false, PlatformDefaults.GitInstallHint);
            return (true, null);
        }
        catch
        {
            return (false, PlatformDefaults.GitInstallHint);
        }
    }

    public async Task<(bool available, string? error)> CheckGitLfsAsync()
    {
        try
        {
            var (exitCode, _) = await RunCommandAsync("git", "lfs", "version");
            if (exitCode != 0)
                return (false, PlatformDefaults.GitLfsInstallHint);
            return (true, null);
        }
        catch
        {
            return (false, PlatformDefaults.GitLfsInstallHint);
        }
    }

    public async Task<(bool available, string? error)> CheckDockerAsync()
    {
        try
        {
            var (exitCode, _) = await RunCommandAsync("docker", "version", "--format", "{{.Server.Version}}");
            if (exitCode != 0)
                return (false, "Docker is not responding. Ensure Docker Desktop is installed and running.");
            return (true, null);
        }
        catch
        {
            return (false, "Docker is not installed or not in PATH. Install Docker Desktop from docker.com and ensure it is running.");
        }
    }

    public async Task<bool> IsDockerImagePresentAsync(string imageTag)
    {
        try
        {
            ValidateImageTag(imageTag);
            var (exitCode, output) = await RunCommandAsync("docker", "images", "-q", imageTag);
            return exitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }

    public bool IsModelPresent(string checkpointsDir)
    {
        var s2ProPath = Path.Combine(checkpointsDir, "s2-pro");
        return Directory.Exists(s2ProPath) && Directory.GetFiles(s2ProPath).Length > 0;
    }

    // --- Background downloads ---

    public void StartModelDownload(string checkpointsDir, Action? onOutput = null)
    {
        if (IsModelDownloading) return;

        ValidatePath(checkpointsDir, nameof(checkpointsDir));

        ModelDownloadCompleted = false;
        ModelDownloadError = null;
        lock (_lock) { _modelDownloadOutput.Clear(); }

        var targetPath = Path.Combine(checkpointsDir, "s2-pro");

        _modelDownloadProcess = StartBackgroundProcess(
            "git", new[] { "clone", "https://huggingface.co/fishaudio/s2-pro", targetPath },
            _modelDownloadOutput,
            onOutput,
            exitCode =>
            {
                if (exitCode == 0)
                    ModelDownloadCompleted = true;
                else
                    ModelDownloadError = "Model download failed. Check the output for details.";
            });
    }

    public void StartDockerPull(string imageTag, Action? onOutput = null)
    {
        if (IsDockerPulling) return;

        ValidateImageTag(imageTag);

        DockerPullCompleted = false;
        DockerPullError = null;
        lock (_lock) { _dockerPullOutput.Clear(); }

        _dockerPullProcess = StartBackgroundProcess(
            "docker", new[] { "pull", imageTag },
            _dockerPullOutput,
            onOutput,
            exitCode =>
            {
                if (exitCode == 0)
                    DockerPullCompleted = true;
                else
                    DockerPullError = "Docker image pull failed. Check the output for details.";
            });
    }

    public List<string> GetModelDownloadOutput()
    {
        lock (_lock) { return _modelDownloadOutput.ToList(); }
    }

    public List<string> GetDockerPullOutput()
    {
        lock (_lock) { return _dockerPullOutput.ToList(); }
    }

    // --- Internals ---

    private Process StartBackgroundProcess(
        string fileName, string[] args,
        List<string> outputBuffer,
        Action? onOutput,
        Action<int>? onExit)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_lock) { outputBuffer.Add(e.Data); }
            onOutput?.Invoke();
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_lock) { outputBuffer.Add(e.Data); }
            onOutput?.Invoke();
        };

        process.Exited += (_, _) =>
        {
            onExit?.Invoke(process.ExitCode);
            onOutput?.Invoke();
            process.Dispose();
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    private static async Task<(int exitCode, string output)> RunCommandAsync(string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output.Trim());
    }
}
