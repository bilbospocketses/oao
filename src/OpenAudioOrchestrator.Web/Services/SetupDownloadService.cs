using System.Diagnostics;
using System.Text.Json;
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

    public bool IsModelDownloading => _modelDownloadProcess is not null;
    public bool IsDockerPulling => _dockerPullProcess is not null;
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

    /// <summary>
    /// Validates local s2-pro model files against the HuggingFace repository.
    /// Returns a result indicating whether all files are present and the correct size.
    /// </summary>
    public async Task<ModelValidationResult> ValidateModelAsync(string checkpointsDir)
    {
        var s2ProPath = Path.Combine(checkpointsDir, "s2-pro");

        if (!Directory.Exists(s2ProPath))
            return new ModelValidationResult(false, "Model folder not found.");

        List<HuggingFaceFileEntry> remoteFiles;
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            var json = await http.GetStringAsync("https://huggingface.co/api/models/fishaudio/s2-pro/tree/main");
            remoteFiles = JsonSerializer.Deserialize<List<HuggingFaceFileEntry>>(json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not reach HuggingFace API for model validation");
            // Fall back to basic check when offline
            return IsModelPresent(checkpointsDir)
                ? new ModelValidationResult(true, "Model folder present (offline — could not verify against HuggingFace).")
                : new ModelValidationResult(false, "Model folder not found.");
        }

        var missing = new List<string>();
        var sizeMismatch = new List<string>();

        foreach (var remote in remoteFiles.Where(f => f.Type == "file"))
        {
            var localPath = Path.Combine(s2ProPath, remote.Path);
            if (!File.Exists(localPath))
            {
                missing.Add(remote.Path);
                continue;
            }

            var localSize = new FileInfo(localPath).Length;
            if (localSize != remote.Size)
                sizeMismatch.Add($"{remote.Path} (expected {FormatBytes(remote.Size)}, found {FormatBytes(localSize)})");
        }

        if (missing.Count == 0 && sizeMismatch.Count == 0)
            return new ModelValidationResult(true, "All model files verified.");

        var problems = new List<string>();
        if (missing.Count > 0)
            problems.Add($"Missing files: {string.Join(", ", missing)}");
        if (sizeMismatch.Count > 0)
            problems.Add($"Size mismatch: {string.Join("; ", sizeMismatch)}");

        return new ModelValidationResult(false, string.Join("\n", problems));
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024 => $"{bytes / 1_024.0:F0} KB",
            _ => $"{bytes} B"
        };
    }

    public record ModelValidationResult(bool IsValid, string Message);

    private record HuggingFaceFileEntry(
        [property: System.Text.Json.Serialization.JsonPropertyName("type")] string Type,
        [property: System.Text.Json.Serialization.JsonPropertyName("path")] string Path,
        [property: System.Text.Json.Serialization.JsonPropertyName("size")] long Size);

    // --- Background downloads ---

    public void StartModelDownload(string checkpointsDir, Action? onOutput = null)
    {
        if (IsModelDownloading) return;

        ValidatePath(checkpointsDir, nameof(checkpointsDir));

        ModelDownloadCompleted = false;
        ModelDownloadError = null;
        lock (_lock) { _modelDownloadOutput.Clear(); }

        var targetPath = Path.Combine(checkpointsDir, "s2-pro");

        // If the directory already exists as a git repo (partial download),
        // run "git lfs pull" to fetch missing LFS files instead of re-cloning.
        string fileName;
        string[] args;
        string? workingDir = null;

        if (Directory.Exists(Path.Combine(targetPath, ".git")))
        {
            fileName = "git";
            args = new[] { "lfs", "pull" };
            workingDir = targetPath;
        }
        else
        {
            fileName = "git";
            args = new[] { "clone", "https://huggingface.co/fishaudio/s2-pro", targetPath };
        }

        _modelDownloadProcess = StartBackgroundProcess(
            fileName, args,
            _modelDownloadOutput,
            onOutput,
            exitCode =>
            {
                if (exitCode == 0)
                    ModelDownloadCompleted = true;
                else
                    ModelDownloadError = "Model download failed. Check the output for details.";
                _modelDownloadProcess = null;
            },
            workingDir: workingDir);
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
                _dockerPullProcess = null;
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
        Action<int>? onExit,
        string? workingDir = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (workingDir is not null)
            psi.WorkingDirectory = workingDir;
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
            var exitCode = process.ExitCode;
            process.Dispose();
            onExit?.Invoke(exitCode);
            onOutput?.Invoke();
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
