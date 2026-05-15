> **Note:** post-rename. References to `OpenAudioOrchestrator.*` paths and
> config keys reflect their original names. Current equivalents are
> `oao.*` and `oao:*`.
# Enhanced Setup Wizard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 3-step setup wizard with a 7-step guided installer covering directories, model download, Docker image pull, configuration, and account creation.

**Architecture:** A new `SetupService` handles settings file I/O, pre-checks (git, docker), and background process management. The existing `Setup.razor` is rewritten with 7 steps. Background downloads (git clone, docker pull) run as child processes with stdout streaming to the UI and survive page navigation within the app.

**Tech Stack:** .NET 9, Blazor Server, System.Text.Json.Nodes, System.Diagnostics.Process

---

## File Structure

| Action | File | Responsibility |
|--------|------|---------------|
| Create | `Services/SetupService.cs` | Settings writer, pre-checks, background process runner |
| Rewrite | `Components/Pages/Setup.razor` | 7-step wizard UI |
| Modify | `Components/Pages/Deploy.razor` | Model download banner when s2-pro missing |

---

### Task 1: Create SetupService

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Services/SetupService.cs`
- Modify: `src/FishAudioOrchestrator.Web/Program.cs`

- [ ] **Step 1: Create SetupService**

Create `src/FishAudioOrchestrator.Web/Services/SetupService.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FishAudioOrchestrator.Web.Services;

public class SetupService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SetupService> _logger;
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

    public SetupService(IWebHostEnvironment env, ILogger<SetupService> logger)
    {
        _env = env;
        _logger = logger;
    }

    // --- Pre-checks ---

    public async Task<(bool available, string? error)> CheckGitAsync()
    {
        try
        {
            var (exitCode, output) = await RunCommandAsync("git", "--version");
            if (exitCode != 0)
                return (false, "Git is not installed. Install it from PowerShell:\nwinget install Git.Git\nThen click Retry.");
            return (true, null);
        }
        catch
        {
            return (false, "Git is not installed or not in PATH. Install it from PowerShell:\nwinget install Git.Git\nThen click Retry.");
        }
    }

    public async Task<(bool available, string? error)> CheckGitLfsAsync()
    {
        try
        {
            var (exitCode, output) = await RunCommandAsync("git", "lfs version");
            if (exitCode != 0)
                return (false, "Git LFS is not installed. Run the following in PowerShell:\ngit lfs install\nThen click Retry.");
            return (true, null);
        }
        catch
        {
            return (false, "Git LFS is not installed. Run the following in PowerShell:\ngit lfs install\nThen click Retry.");
        }
    }

    public async Task<(bool available, string? error)> CheckDockerAsync()
    {
        try
        {
            var (exitCode, output) = await RunCommandAsync("docker", "version --format '{{.Server.Version}}'");
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
            var (exitCode, output) = await RunCommandAsync("docker", $"images -q {imageTag}");
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

        ModelDownloadCompleted = false;
        ModelDownloadError = null;
        lock (_lock) { _modelDownloadOutput.Clear(); }

        var targetPath = Path.Combine(checkpointsDir, "s2-pro");

        _modelDownloadProcess = StartBackgroundProcess(
            "git", $"clone https://huggingface.co/fishaudio/s2-pro \"{targetPath}\"",
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

        DockerPullCompleted = false;
        DockerPullError = null;
        lock (_lock) { _dockerPullOutput.Clear(); }

        _dockerPullProcess = StartBackgroundProcess(
            "docker", $"pull {imageTag}",
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

    public bool HasActiveDownloads => IsModelDownloading || IsDockerPulling;

    // --- Settings writer ---

    public async Task SaveSettingsAsync(
        string checkpointsDir,
        string referencesDir,
        string outputDir,
        int portStart,
        int portEnd,
        string? domain,
        string? email)
    {
        var settingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
        var json = await File.ReadAllTextAsync(settingsPath);
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!;

        // Derive DataRoot as the common parent of the three directories
        var dataRoot = Path.GetDirectoryName(checkpointsDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                       ?? checkpointsDir;

        // ConnectionStrings
        root["ConnectionStrings"]!["Default"] = $"Data Source={Path.Combine(dataRoot, "fishorch.db")}";

        // FishOrchestrator
        var fish = root["FishOrchestrator"]!;
        fish["DataRoot"] = dataRoot;
        fish["PortRange"]!["Start"] = portStart;
        fish["PortRange"]!["End"] = portEnd;
        fish["Domain"] = domain ?? "";

        // LettuceEncrypt
        var le = root["LettuceEncrypt"]!;
        if (!string.IsNullOrWhiteSpace(domain))
        {
            le["DomainNames"] = new JsonArray(domain);
            le["EmailAddress"] = email ?? "";
        }
        else
        {
            le["DomainNames"] = new JsonArray();
            le["EmailAddress"] = "";
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        var output = root.ToJsonString(options);
        await File.WriteAllTextAsync(settingsPath, output);

        _logger.LogInformation("Settings saved to {Path}", settingsPath);
    }

    // --- Validation helpers ---

    public static bool IsValidDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        return Regex.IsMatch(domain, @"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)+$");
    }

    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    }

    public static bool IsValidPort(int port)
    {
        return port >= 1024 && port <= 65535;
    }

    // --- Internals ---

    private Process StartBackgroundProcess(
        string fileName, string arguments,
        List<string> outputBuffer,
        Action? onOutput,
        Action<int>? onExit)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

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
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    private static async Task<(int exitCode, string output)> RunCommandAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output.Trim());
    }
}
```

- [ ] **Step 2: Register in Program.cs**

In `src/FishAudioOrchestrator.Web/Program.cs`, add after the existing `builder.Services.AddSingleton<OrchestratorEventBus>();` line:

```csharp
builder.Services.AddSingleton<SetupService>();
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build src/FishAudioOrchestrator.Web`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Services/SetupService.cs src/FishAudioOrchestrator.Web/Program.cs
git commit -m "feat: add SetupService for wizard pre-checks, downloads, and settings"
```

---

### Task 2: Rewrite Setup.razor — Steps 1-4 (Infrastructure)

**Files:**
- Rewrite: `src/FishAudioOrchestrator.Web/Components/Pages/Setup.razor`

- [ ] **Step 1: Replace the entire file**

Replace `src/FishAudioOrchestrator.Web/Components/Pages/Setup.razor` with the full 7-step wizard. This is a large file — the complete content follows:

```razor
@page "/setup"
@using Microsoft.AspNetCore.Identity
@using System.Text.RegularExpressions
@inject UserManager<AppUser> UserManager
@inject RoleManager<IdentityRole> RoleManager
@inject ITotpService TotpService
@inject IConfiguration Config
@inject IWebHostEnvironment Env
@inject NavigationManager Nav
@inject SetupService Setup
@layout EmptyLayout

<PageTitle>Setup — Fish Orchestrator</PageTitle>

<div class="d-flex justify-content-center align-items-center min-vh-100 bg-dark py-4">
    <div class="card bg-dark text-light border-secondary" style="width: 600px;">
        <div class="card-body">
            <h3 class="card-title text-center mb-2">Fish Orchestrator Setup</h3>
            <p class="text-muted text-center mb-4">Step @_step of 7</p>

            @if (!string.IsNullOrEmpty(_error))
            {
                <div class="alert alert-danger">@_error</div>
            }
            @if (!string.IsNullOrEmpty(_success))
            {
                <div class="alert alert-success">@_success</div>
            }

            @if (_step == 1)
            {
                @* --- Step 1: Data Directories --- *@
                <h5>Data Storage</h5>
                <p class="text-muted">Fish Orchestrator stores model checkpoints, reference voice samples, and generated audio files in separate directories. Choose where to store each. The directories will be created if they don't exist.</p>

                <div class="mb-3">
                    <label class="form-label">Checkpoints Directory</label>
                    <input class="form-control bg-dark text-light border-secondary" @bind="_checkpointsDir" />
                    <small class="text-muted">Model checkpoint files (e.g. s2-pro)</small>
                </div>
                <div class="mb-3">
                    <label class="form-label">References Directory</label>
                    <input class="form-control bg-dark text-light border-secondary" @bind="_referencesDir" />
                    <small class="text-muted">Voice reference audio samples for cloning</small>
                </div>
                <div class="mb-3">
                    <label class="form-label">Output Directory</label>
                    <input class="form-control bg-dark text-light border-secondary" @bind="_outputDir" />
                    <small class="text-muted">Generated speech audio files</small>
                </div>
                <button class="btn btn-primary w-100" @onclick="ValidateStep1">Next</button>
            }
            else if (_step == 2)
            {
                @* --- Step 2: Model Download --- *@
                <h5>Download Fish Speech Model</h5>
                <p class="text-muted">Fish Orchestrator requires the Fish Audio s2-pro model. This is a large download (~11 GB) from HuggingFace. Git with Git LFS support must be installed.</p>

                @if (_modelAlreadyPresent)
                {
                    <div class="alert alert-success">Model already downloaded.</div>
                    <button class="btn btn-primary w-100" @onclick="() => _step = 3">Next</button>
                }
                else if (!string.IsNullOrEmpty(_preCheckError))
                {
                    <div class="alert alert-warning" style="white-space: pre-wrap;">@_preCheckError</div>
                    <button class="btn btn-outline-light w-100 mb-2" @onclick="RunModelPreChecks">Retry</button>
                    <button class="btn btn-outline-secondary w-100" @onclick="SkipModelDownload">Skip — I'll download it manually later</button>
                }
                else if (Setup.IsModelDownloading)
                {
                    <div class="mb-3">
                        <div class="bg-dark border border-secondary rounded p-2" style="height: 200px; overflow-y: auto; font-family: monospace; font-size: 0.8em;">
                            @foreach (var line in _downloadOutput)
                            {
                                <div>@line</div>
                            }
                        </div>
                    </div>
                    <button class="btn btn-primary w-100" @onclick="() => _step = 3">Next (download continues in background)</button>
                }
                else if (Setup.ModelDownloadCompleted)
                {
                    <div class="alert alert-success">Model downloaded successfully.</div>
                    <button class="btn btn-primary w-100" @onclick="() => _step = 3">Next</button>
                }
                else if (!string.IsNullOrEmpty(Setup.ModelDownloadError))
                {
                    <div class="alert alert-danger">@Setup.ModelDownloadError</div>
                    <div class="bg-dark border border-secondary rounded p-2 mb-3" style="height: 150px; overflow-y: auto; font-family: monospace; font-size: 0.8em;">
                        @foreach (var line in _downloadOutput)
                        {
                            <div>@line</div>
                        }
                    </div>
                    <button class="btn btn-outline-light w-100 mb-2" @onclick="StartModelDownload">Retry Download</button>
                    <button class="btn btn-outline-secondary w-100" @onclick="SkipModelDownload">Skip</button>
                }
                else
                {
                    <button class="btn btn-success w-100 mb-2" @onclick="StartModelDownload">Download Model (~11 GB)</button>
                    <button class="btn btn-outline-secondary w-100" @onclick="SkipModelDownload">Skip — I'll download it manually later</button>
                }
            }
            else if (_step == 3)
            {
                @* --- Step 3: Docker Image --- *@
                <h5>Download Docker Image</h5>
                <p class="text-muted">Fish Orchestrator runs the TTS model inside a Docker container. The required image is approximately 12 GB.</p>

                @if (_dockerImagePresent)
                {
                    <div class="alert alert-success">Docker image already available.</div>
                    <button class="btn btn-primary w-100" @onclick="() => _step = 4">Next</button>
                }
                else if (!string.IsNullOrEmpty(_preCheckError))
                {
                    <div class="alert alert-warning" style="white-space: pre-wrap;">@_preCheckError</div>
                    <button class="btn btn-outline-light w-100" @onclick="RunDockerPreChecks">Retry</button>
                }
                else if (Setup.IsDockerPulling)
                {
                    <div class="mb-3">
                        <div class="bg-dark border border-secondary rounded p-2" style="height: 200px; overflow-y: auto; font-family: monospace; font-size: 0.8em;">
                            @foreach (var line in _pullOutput)
                            {
                                <div>@line</div>
                            }
                        </div>
                    </div>
                    <button class="btn btn-primary w-100" @onclick="() => _step = 4">Next (download continues in background)</button>
                }
                else if (Setup.DockerPullCompleted)
                {
                    <div class="alert alert-success">Docker image downloaded successfully.</div>
                    <button class="btn btn-primary w-100" @onclick="() => _step = 4">Next</button>
                }
                else if (!string.IsNullOrEmpty(Setup.DockerPullError))
                {
                    <div class="alert alert-danger">@Setup.DockerPullError</div>
                    <div class="bg-dark border border-secondary rounded p-2 mb-3" style="height: 150px; overflow-y: auto; font-family: monospace; font-size: 0.8em;">
                        @foreach (var line in _pullOutput)
                        {
                            <div>@line</div>
                        }
                    </div>
                    <button class="btn btn-outline-light w-100" @onclick="StartDockerPull">Retry Download</button>
                }
                else
                {
                    <button class="btn btn-success w-100" @onclick="StartDockerPull">Download Docker Image (~12 GB)</button>
                }
            }
            else if (_step == 4)
            {
                @* --- Step 4: Server Configuration --- *@
                <h5>Server Configuration</h5>
                <p class="text-muted">Configure how Docker containers are assigned ports, and optionally set up a domain for automatic HTTPS.</p>

                <h6 class="mt-3">Container Port Range</h6>
                <p class="text-muted small">When deploying models, each container is assigned a port from this range. Most users need only 1-2 ports unless running multiple models on a high-end GPU.</p>
                <div class="row mb-3">
                    <div class="col">
                        <label class="form-label">Start Port</label>
                        <input type="number" class="form-control bg-dark text-light border-secondary" @bind="_portStart" />
                    </div>
                    <div class="col">
                        <label class="form-label">End Port</label>
                        <input type="number" class="form-control bg-dark text-light border-secondary" @bind="_portEnd" />
                    </div>
                </div>

                <h6 class="mt-4">Domain & HTTPS (Optional)</h6>
                <p class="text-muted small">By default, the app runs on port 5206 over HTTP. If you have a domain name pointing to this server and want automatic HTTPS via Let's Encrypt, enter it below. Otherwise, leave blank.</p>
                <div class="mb-3">
                    <label class="form-label">Domain</label>
                    <input class="form-control bg-dark text-light border-secondary" @bind="_domain" placeholder="e.g. fish.example.com" />
                </div>

                @if (!string.IsNullOrWhiteSpace(_domain))
                {
                    <div class="mb-3">
                        <label class="form-label">Email Address (required for Let's Encrypt)</label>
                        <input class="form-control bg-dark text-light border-secondary" @bind="_email" placeholder="admin@example.com" />
                    </div>
                    <div class="alert alert-info small">
                        <strong>Important:</strong> After setup, restart the app and navigate to <strong>https://@_domain</strong>.
                        The domain must have a DNS A record pointing to this server's public IP address.
                        Ports 80 and 443 must be accessible from the internet for Let's Encrypt to validate domain ownership and provision the certificate.
                        If you are unfamiliar with DNS configuration and certificate provisioning, consult an AI assistant or your DNS provider's documentation before proceeding.
                    </div>
                }
                else
                {
                    <p class="text-muted small">After setup, restart the app and navigate to <strong>http://localhost:5206</strong>.</p>
                }

                <button class="btn btn-primary w-100" @onclick="ValidateStep4">Next</button>
            }
            else if (_step == 5)
            {
                @* --- Step 5: Create Admin Account --- *@
                <h5>Create Admin Account</h5>
                <p class="text-muted">Create the first administrator account. This account has full access to all features.</p>

                <EditForm Model="_adminModel" OnValidSubmit="CreateAdmin" FormName="setup-admin">
                    <div class="mb-3">
                        <label class="form-label">Username</label>
                        <InputText @bind-Value="_adminModel.Username" class="form-control bg-dark text-light border-secondary" />
                    </div>
                    <div class="mb-3">
                        <label class="form-label">Display Name</label>
                        <InputText @bind-Value="_adminModel.DisplayName" class="form-control bg-dark text-light border-secondary" />
                    </div>
                    <div class="mb-3">
                        <label class="form-label">Password</label>
                        <InputText @bind-Value="_adminModel.Password" type="password" class="form-control bg-dark text-light border-secondary" />
                        <div class="form-text text-muted">8+ characters, upper, lower, digit, special character.</div>
                    </div>
                    <div class="mb-3">
                        <label class="form-label">Confirm Password</label>
                        <InputText @bind-Value="_adminModel.ConfirmPassword" type="password" class="form-control bg-dark text-light border-secondary" />
                    </div>
                    <button type="submit" class="btn btn-primary w-100">Next</button>
                </EditForm>
            }
            else if (_step == 6)
            {
                @* --- Step 6: TOTP Setup --- *@
                <h5>TOTP Setup</h5>
                <p class="text-muted">Scan this QR code with your authenticator app (Google Authenticator, Authy, etc.).</p>

                @if (!string.IsNullOrEmpty(_qrDataUri))
                {
                    <div class="text-center mb-3">
                        <img src="@_qrDataUri" alt="TOTP QR Code" style="width: 200px; height: 200px;" />
                    </div>
                    <div class="mb-3">
                        <label class="form-label">Manual Key</label>
                        <input type="text" readonly class="form-control bg-dark text-light border-secondary text-center" value="@_manualKey" />
                    </div>
                }

                <div class="mb-3">
                    <label class="form-label">Enter 6-digit code to verify</label>
                    <InputText @bind-Value="_totpCode" class="form-control bg-dark text-light border-secondary text-center" maxlength="6" />
                </div>
                <button class="btn btn-primary w-100" @onclick="VerifyTotpAndComplete" disabled="@_loading">
                    @(_loading ? "Completing setup..." : "Complete Setup")
                </button>
            }
            else if (_step == 7)
            {
                @* --- Step 7: Setup Complete --- *@
                <h5>Setup Complete</h5>

                @if (Setup.HasActiveDownloads)
                {
                    <div class="alert alert-warning">
                        Downloads are still in progress. Please wait for them to complete before restarting the application.
                        Stopping the app now will cancel the downloads and they will need to start over.
                    </div>

                    @if (Setup.IsModelDownloading)
                    {
                        <h6>Model Download</h6>
                        <div class="bg-dark border border-secondary rounded p-2 mb-3" style="height: 150px; overflow-y: auto; font-family: monospace; font-size: 0.8em;">
                            @foreach (var line in Setup.GetModelDownloadOutput())
                            {
                                <div>@line</div>
                            }
                        </div>
                    }

                    @if (Setup.IsDockerPulling)
                    {
                        <h6>Docker Image Download</h6>
                        <div class="bg-dark border border-secondary rounded p-2 mb-3" style="height: 150px; overflow-y: auto; font-family: monospace; font-size: 0.8em;">
                            @foreach (var line in Setup.GetDockerPullOutput())
                            {
                                <div>@line</div>
                            }
                        </div>
                    }
                }
                else
                {
                    <div class="alert alert-success">
                        <p><strong>Data directories:</strong> Configured</p>
                        <p><strong>Model:</strong> @(_modelSkipped ? "Skipped (download manually before deploying)" : "Downloaded")</p>
                        <p><strong>Docker image:</strong> @(Setup.DockerPullCompleted || _dockerImagePresent ? "Available" : "Not downloaded")</p>
                        <p><strong>Port range:</strong> @_portStart - @_portEnd</p>
                        @if (!string.IsNullOrWhiteSpace(_domain))
                        {
                            <p><strong>Domain:</strong> @_domain</p>
                        }
                        <p><strong>Admin account:</strong> @_adminModel.Username</p>
                        <p><strong>TOTP:</strong> Enabled</p>
                    </div>

                    <div class="card bg-dark border-secondary mb-3">
                        <div class="card-body">
                            <p>Stop the application (<code>Ctrl+C</code> in the terminal) and restart with:</p>
                            <pre class="bg-dark text-info p-2 rounded">dotnet run --project src/FishAudioOrchestrator.Web</pre>
                            @if (!string.IsNullOrWhiteSpace(_domain))
                            {
                                <p>Then navigate to <strong>https://@_domain</strong></p>
                            }
                            else
                            {
                                <p>Then navigate to <strong>http://localhost:5206</strong></p>
                            }
                        </div>
                    </div>
                }
            }
        </div>
    </div>
</div>

@code {
    private int _step = 1;
    private string? _error;
    private string? _success;
    private string? _preCheckError;
    private bool _loading;

    // Step 1
    private string _checkpointsDir = @"D:\DockerData\FishAudio\Checkpoints";
    private string _referencesDir = @"D:\DockerData\FishAudio\References";
    private string _outputDir = @"D:\DockerData\FishAudio\Output";

    // Step 2
    private bool _modelAlreadyPresent;
    private bool _modelSkipped;
    private List<string> _downloadOutput = new();
    private Timer? _downloadTimer;

    // Step 3
    private bool _dockerImagePresent;
    private List<string> _pullOutput = new();
    private Timer? _pullTimer;
    private string _imageTag = "fishaudio/fish-speech:server-cuda-v2.0.0-beta";

    // Step 4
    private int _portStart = 9001;
    private int _portEnd = 9099;
    private string _domain = "";
    private string _email = "";

    // Steps 5-6
    private AdminModel _adminModel = new();
    private string? _qrDataUri;
    private string? _manualKey;
    private string _totpCode = "";
    private AppUser? _createdUser;

    // Step 7
    private Timer? _completionTimer;

    private sealed class AdminModel
    {
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Password { get; set; } = "";
        public string ConfirmPassword { get; set; } = "";
    }

    protected override Task OnInitializedAsync()
    {
        if (UserManager.Users.Any())
        {
            Nav.NavigateTo("/", forceLoad: true);
        }
        return Task.CompletedTask;
    }

    // --- Step 1: Data Directories ---

    private void ValidateStep1()
    {
        _error = null;

        if (string.IsNullOrWhiteSpace(_checkpointsDir) ||
            string.IsNullOrWhiteSpace(_referencesDir) ||
            string.IsNullOrWhiteSpace(_outputDir))
        {
            _error = "All three directories are required.";
            return;
        }

        var dirs = new[] { _checkpointsDir.Trim(), _referencesDir.Trim(), _outputDir.Trim() };
        var normalized = dirs.Select(d => Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant()).ToArray();
        if (normalized.Distinct().Count() != 3)
        {
            _error = "Each directory must be unique.";
            return;
        }

        foreach (var dir in dirs)
        {
            try
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                _error = $"Could not create directory '{dir}': {ex.Message}";
                return;
            }
        }

        _checkpointsDir = dirs[0];
        _referencesDir = dirs[1];
        _outputDir = dirs[2];

        // Check if model is already present
        _modelAlreadyPresent = Setup.IsModelPresent(_checkpointsDir);

        // Pre-check git for step 2
        _ = RunModelPreChecks();

        _step = 2;
    }

    // --- Step 2: Model Download ---

    private async Task RunModelPreChecks()
    {
        _preCheckError = null;
        _modelAlreadyPresent = Setup.IsModelPresent(_checkpointsDir);
        if (_modelAlreadyPresent) return;

        var (gitOk, gitError) = await Setup.CheckGitAsync();
        if (!gitOk) { _preCheckError = gitError; return; }

        var (lfsOk, lfsError) = await Setup.CheckGitLfsAsync();
        if (!lfsOk) { _preCheckError = lfsError; return; }
    }

    private void StartModelDownload()
    {
        _error = null;
        Setup.StartModelDownload(_checkpointsDir, () => InvokeAsync(RefreshDownloadOutput));
        _downloadTimer = new Timer(_ => InvokeAsync(RefreshDownloadOutput), null, 1000, 2000);
    }

    private void RefreshDownloadOutput()
    {
        _downloadOutput = Setup.GetModelDownloadOutput();
        if (!Setup.IsModelDownloading)
        {
            _downloadTimer?.Dispose();
            _downloadTimer = null;
            _modelAlreadyPresent = Setup.ModelDownloadCompleted;
        }
        StateHasChanged();
    }

    private void SkipModelDownload()
    {
        _modelSkipped = true;
        _step = 3;
        _ = RunDockerPreChecks();
    }

    // --- Step 3: Docker Image ---

    private async Task RunDockerPreChecks()
    {
        _preCheckError = null;

        var (dockerOk, dockerError) = await Setup.CheckDockerAsync();
        if (!dockerOk) { _preCheckError = dockerError; return; }

        _dockerImagePresent = await Setup.IsDockerImagePresentAsync(_imageTag);
    }

    private void StartDockerPull()
    {
        _error = null;
        Setup.StartDockerPull(_imageTag, () => InvokeAsync(RefreshPullOutput));
        _pullTimer = new Timer(_ => InvokeAsync(RefreshPullOutput), null, 1000, 2000);
    }

    private void RefreshPullOutput()
    {
        _pullOutput = Setup.GetDockerPullOutput();
        if (!Setup.IsDockerPulling)
        {
            _pullTimer?.Dispose();
            _pullTimer = null;
            _dockerImagePresent = Setup.DockerPullCompleted;
        }
        StateHasChanged();
    }

    // --- Step 4: Configuration ---

    private async Task ValidateStep4()
    {
        _error = null;

        if (!SetupService.IsValidPort(_portStart) || !SetupService.IsValidPort(_portEnd))
        {
            _error = "Ports must be between 1024 and 65535.";
            return;
        }
        if (_portStart >= _portEnd)
        {
            _error = "Start port must be less than end port.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_domain))
        {
            if (!SetupService.IsValidDomain(_domain.Trim()))
            {
                _error = "Invalid domain format. Example: fish.example.com";
                return;
            }
            if (string.IsNullOrWhiteSpace(_email) || !SetupService.IsValidEmail(_email.Trim()))
            {
                _error = "A valid email address is required for Let's Encrypt certificate provisioning.";
                return;
            }
        }

        // Save all settings
        try
        {
            await Setup.SaveSettingsAsync(
                _checkpointsDir, _referencesDir, _outputDir,
                _portStart, _portEnd,
                string.IsNullOrWhiteSpace(_domain) ? null : _domain.Trim(),
                string.IsNullOrWhiteSpace(_email) ? null : _email.Trim());
        }
        catch (Exception ex)
        {
            _error = $"Failed to save settings: {ex.Message}";
            return;
        }

        _step = 5;
    }

    // --- Step 5: Create Admin ---

    private async Task CreateAdmin()
    {
        _error = null;

        if (string.IsNullOrWhiteSpace(_adminModel.Username))
        { _error = "Username is required."; return; }
        if (string.IsNullOrWhiteSpace(_adminModel.DisplayName))
        { _error = "Display name is required."; return; }
        if (_adminModel.Password != _adminModel.ConfirmPassword)
        { _error = "Passwords do not match."; return; }

        var user = new AppUser
        {
            UserName = _adminModel.Username,
            DisplayName = _adminModel.DisplayName,
            MustChangePassword = false,
            MustSetupTotp = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await UserManager.CreateAsync(user, _adminModel.Password);
        if (!result.Succeeded)
        {
            _error = string.Join(" ", result.Errors.Select(e => e.Description));
            return;
        }

        await UserManager.AddToRoleAsync(user, "Admin");
        _createdUser = user;

        var (manualKey, qrDataUri) = await TotpService.GenerateSetupInfoAsync(user, "FishOrchestrator");
        _manualKey = manualKey;
        _qrDataUri = qrDataUri;
        _step = 6;
    }

    // --- Step 6: TOTP ---

    private async Task VerifyTotpAndComplete()
    {
        _error = null;
        _loading = true;

        if (_createdUser is null)
        {
            _error = "Setup error. Please refresh and start over.";
            _loading = false;
            return;
        }

        var isValid = await TotpService.VerifyCodeAsync(_createdUser, _totpCode.Trim());
        if (!isValid)
        {
            _error = "Invalid code. Please try again.";
            _loading = false;
            return;
        }

        await UserManager.SetTwoFactorEnabledAsync(_createdUser, true);
        _createdUser.MustSetupTotp = false;
        await UserManager.UpdateAsync(_createdUser);

        _step = 7;
        _loading = false;

        // Start polling for download completion on step 7
        if (Setup.HasActiveDownloads)
        {
            _completionTimer = new Timer(_ => InvokeAsync(() =>
            {
                if (!Setup.HasActiveDownloads)
                {
                    _completionTimer?.Dispose();
                    _completionTimer = null;
                }
                StateHasChanged();
            }), null, 2000, 3000);
        }
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build src/FishAudioOrchestrator.Web`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/Setup.razor
git commit -m "feat: rewrite Setup.razor with 7-step guided wizard"
```

---

### Task 3: Update Deploy page with model download banner

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Components/Pages/Deploy.razor`

- [ ] **Step 1: Add model download banner**

In `src/FishAudioOrchestrator.Web/Components/Pages/Deploy.razor`, add `@inject SetupService Setup` after the existing `@inject IConfiguration Config` line.

Add the following markup immediately after the `<h2>Deploy Fish Speech Model</h2>` line, before the `<div class="row">`:

```razor
@if (!_modelPresent)
{
    <div class="alert alert-warning mb-3">
        <strong>No model found</strong> in the Checkpoints directory.
        @if (Setup.IsModelDownloading)
        {
            <div class="mt-2 bg-dark border border-secondary rounded p-2" style="height: 150px; overflow-y: auto; font-family: monospace; font-size: 0.8em;">
                @foreach (var line in Setup.GetModelDownloadOutput())
                {
                    <div>@line</div>
                }
            </div>
        }
        else if (Setup.ModelDownloadCompleted)
        {
            <div class="mt-2 alert alert-success mb-0">Model downloaded successfully. Refresh the page to continue.</div>
        }
        else
        {
            <p class="mb-2">Download the s2-pro model (~11 GB) to get started.</p>
            <button class="btn btn-success" @onclick="StartModelDownload">Download Model</button>
        }
    </div>
}
```

Add these fields and methods in the `@code` block:

After the existing `private bool _isDeploying;` field, add:

```csharp
private bool _modelPresent;
private Timer? _deployDownloadTimer;
```

At the end of `OnInitializedAsync`, add:

```csharp
var checkpointsParent = Path.GetDirectoryName(_form.CheckpointPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? _form.CheckpointPath;
_modelPresent = Setup.IsModelPresent(checkpointsParent);
```

Add this method:

```csharp
private void StartModelDownload()
{
    var checkpointsParent = Path.GetDirectoryName(_form.CheckpointPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? _form.CheckpointPath;
    Setup.StartModelDownload(checkpointsParent, () => InvokeAsync(StateHasChanged));
    _deployDownloadTimer = new Timer(_ => InvokeAsync(() =>
    {
        if (!Setup.IsModelDownloading)
        {
            _deployDownloadTimer?.Dispose();
            _modelPresent = Setup.ModelDownloadCompleted;
        }
        StateHasChanged();
    }), null, 1000, 2000);
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build src/FishAudioOrchestrator.Web`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/Deploy.razor
git commit -m "feat: add model download banner to Deploy page"
```

---

### Task 4: Build, verify, push

- [ ] **Step 1: Full build**

Run: `dotnet build src/FishAudioOrchestrator.Web`
Expected: 0 errors, 0 warnings (or only pre-existing warnings)

- [ ] **Step 2: Push**

```bash
git push origin master
```
