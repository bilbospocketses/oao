# Linux Compatibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Open Audio Orchestrator run natively on Linux with platform-correct defaults, setup wizard guidance, and deployment documentation.

**Architecture:** A single `PlatformDefaults` static class centralizes all OS-aware defaults. Call sites replace hardcoded Windows values with `PlatformDefaults` properties. The shipped `appsettings.json` is OS-agnostic (empty platform-specific values). A comprehensive Linux deployment guide covers Debian/Ubuntu, RHEL/Fedora, and Alpine.

**Tech Stack:** .NET 9, `OperatingSystem.IsWindows()`/`IsLinux()`, xUnit

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/OpenAudioOrchestrator.Web/PlatformDefaults.cs` | Create | All platform-specific defaults in one place |
| `src/OpenAudioOrchestrator.Web/appsettings.json` | Modify | Remove hardcoded Windows values, ship OS-agnostic |
| `src/OpenAudioOrchestrator.Web/Program.cs` | Modify | Use `PlatformDefaults` for DataRoot and DockerEndpoint fallbacks |
| `src/OpenAudioOrchestrator.Web/StartupTasks.cs` | Modify | Replace `DefaultDbPath` constant with `PlatformDefaults.DbPath` |
| `src/OpenAudioOrchestrator.Web/Endpoints/AudioEndpoints.cs` | Modify | Use `PlatformDefaults.DataRoot` fallback |
| `src/OpenAudioOrchestrator.Web/Components/Pages/Deploy.razor` | Modify | Use `PlatformDefaults.DataRoot` fallback |
| `src/OpenAudioOrchestrator.Web/Components/Pages/GenerationHistory.razor` | Modify | Use `PlatformDefaults.DataRoot` fallback |
| `src/OpenAudioOrchestrator.Web/Components/Pages/Setup.razor` | Modify | Use `PlatformDefaults.DataRoot` for default root dir |
| `src/OpenAudioOrchestrator.Web/Services/TtsJobProcessor.cs` | Modify | Use `PlatformDefaults.DataRoot` fallback |
| `src/OpenAudioOrchestrator.Web/Services/SetupDownloadService.cs` | Modify | Use `PlatformDefaults` for Git/GitLfs install hints |
| `tests/OpenAudioOrchestrator.Tests/PlatformDefaultsTests.cs` | Create | Unit tests for PlatformDefaults |
| `docs/LINUX-SETUP.md` | Create | Linux deployment guide |
| `README.md` | Modify | Update Prerequisites, Quick Start, link to Linux guide |

---

### Task 1: Create PlatformDefaults class with tests

**Files:**
- Create: `src/OpenAudioOrchestrator.Web/PlatformDefaults.cs`
- Create: `tests/OpenAudioOrchestrator.Tests/PlatformDefaultsTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/OpenAudioOrchestrator.Tests/PlatformDefaultsTests.cs`:

```csharp
using OpenAudioOrchestrator.Web;

namespace OpenAudioOrchestrator.Tests;

public class PlatformDefaultsTests
{
    [Fact]
    public void DataRoot_ReturnsNonEmptyAbsolutePath()
    {
        var result = PlatformDefaults.DataRoot;
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.True(Path.IsPathRooted(result));
    }

    [Fact]
    public void DbPath_ContainsDataRoot()
    {
        var result = PlatformDefaults.DbPath;
        Assert.StartsWith(PlatformDefaults.DataRoot, result);
        Assert.EndsWith("AudioOrchestrator.db", result);
    }

    [Fact]
    public void DockerEndpoint_ReturnsValidUri()
    {
        var result = PlatformDefaults.DockerEndpoint;
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.True(
            result.StartsWith("npipe://") || result.StartsWith("unix://"),
            $"Expected npipe:// or unix:// but got: {result}");
    }

    [Fact]
    public void GitInstallHint_ReturnsNonEmptyString()
    {
        Assert.False(string.IsNullOrWhiteSpace(PlatformDefaults.GitInstallHint));
    }

    [Fact]
    public void GitLfsInstallHint_ReturnsNonEmptyString()
    {
        Assert.False(string.IsNullOrWhiteSpace(PlatformDefaults.GitLfsInstallHint));
    }

    [Fact]
    public void ConfigValueOrDefault_ReturnsDefaultForNullOrEmpty()
    {
        Assert.Equal("fallback", PlatformDefaults.ConfigValueOrDefault(null, "fallback"));
        Assert.Equal("fallback", PlatformDefaults.ConfigValueOrDefault("", "fallback"));
        Assert.Equal("fallback", PlatformDefaults.ConfigValueOrDefault("  ", "fallback"));
    }

    [Fact]
    public void ConfigValueOrDefault_ReturnsValueWhenPresent()
    {
        Assert.Equal("/custom/path", PlatformDefaults.ConfigValueOrDefault("/custom/path", "fallback"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OpenAudioOrchestrator.Tests --filter "FullyQualifiedName~PlatformDefaultsTests" -v n`
Expected: FAIL — `PlatformDefaults` class does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/OpenAudioOrchestrator.Web/PlatformDefaults.cs`:

```csharp
namespace OpenAudioOrchestrator.Web;

public static class PlatformDefaults
{
    public static string DataRoot =>
        OperatingSystem.IsWindows() ? @"C:\MyOpenAudioProj" : "/opt/OpenAudioOrchestrator";

    public static string DbPath =>
        Path.Combine(DataRoot, "AudioOrchestrator.db");

    public static string DockerEndpoint =>
        OperatingSystem.IsWindows()
            ? "npipe://./pipe/docker_engine"
            : "unix:///var/run/docker.sock";

    public static string GitInstallHint =>
        OperatingSystem.IsWindows()
            ? "Git is not installed. Install it from PowerShell:\nwinget install Git.Git\nThen click Retry."
            : "Git is not installed. Install it with your package manager:\n  Debian/Ubuntu: sudo apt install git\n  RHEL/Fedora: sudo dnf install git\n  Alpine: sudo apk add git\nThen click Retry.";

    public static string GitLfsInstallHint =>
        OperatingSystem.IsWindows()
            ? "Git LFS is not installed. Run the following in PowerShell:\ngit lfs install\nThen click Retry."
            : "Git LFS is not installed. Install it with your package manager:\n  Debian/Ubuntu: sudo apt install git-lfs && git lfs install\n  RHEL/Fedora: sudo dnf install git-lfs && git lfs install\n  Alpine: sudo apk add git-lfs && git lfs install\nThen click Retry.";

    /// <summary>
    /// Returns the config value if non-empty, otherwise returns the platform default.
    /// Use this instead of ?? when reading from IConfiguration, since empty strings
    /// are returned as "" (not null) and won't trigger the null-coalescing operator.
    /// </summary>
    public static string ConfigValueOrDefault(string? configValue, string defaultValue) =>
        string.IsNullOrWhiteSpace(configValue) ? defaultValue : configValue;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/OpenAudioOrchestrator.Tests --filter "FullyQualifiedName~PlatformDefaultsTests" -v n`
Expected: All 7 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/OpenAudioOrchestrator.Web/PlatformDefaults.cs tests/OpenAudioOrchestrator.Tests/PlatformDefaultsTests.cs
git commit -m "feat: add PlatformDefaults class for cross-platform defaults"
```

---

### Task 2: Make appsettings.json OS-agnostic

**Files:**
- Modify: `src/OpenAudioOrchestrator.Web/appsettings.json`

- [ ] **Step 1: Replace platform-specific values with empty strings**

Change `src/OpenAudioOrchestrator.Web/appsettings.json` to:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "Default": ""
  },
  "OpenAudioOrchestrator": {
    "DockerEndpoint": "",
    "DataRoot": "",
    "PortRange": {
      "Start": 9001,
      "End": 9099
    },
    "DefaultImageTag": "fishaudio/fish-speech:server-cuda-v2.0.0-beta",
    "DockerNetworkName": "oao-network",
    "HealthCheckIntervalSeconds": 30,
    "Domain": "",
    "AdminUser": "",
    "AdminPassword": "",
    "DatabaseKey": ""
  },
  "LettuceEncrypt": {
    "AcceptTermsOfService": true,
    "DomainNames": [],
    "EmailAddress": ""
  }
}
```

Three values changed: `ConnectionStrings:Default`, `OpenAudioOrchestrator:DockerEndpoint`, and `OpenAudioOrchestrator:DataRoot` are now empty. The setup wizard writes real values on first run.

- [ ] **Step 2: Commit**

```bash
git add src/OpenAudioOrchestrator.Web/appsettings.json
git commit -m "refactor: make appsettings.json OS-agnostic"
```

---

### Task 3: Update Program.cs to use PlatformDefaults

**Files:**
- Modify: `src/OpenAudioOrchestrator.Web/Program.cs:55`
- Modify: `src/OpenAudioOrchestrator.Web/Program.cs:67`
- Modify: `src/OpenAudioOrchestrator.Web/Program.cs:139-140`

- [ ] **Step 1: Update DataRoot fallback (line 55)**

Change:
```csharp
var dataRoot = builder.Configuration["OpenAudioOrchestrator:DataRoot"] ?? @"C:\MyOpenAudioProj";
```
To:
```csharp
var dataRoot = PlatformDefaults.ConfigValueOrDefault(
    builder.Configuration["OpenAudioOrchestrator:DataRoot"], PlatformDefaults.DataRoot);
```

- [ ] **Step 2: Update ConnectionString fallback (line 67)**

Change:
```csharp
var connectionString = builder.Configuration.GetConnectionString("Default")!;
```
To:
```csharp
var connectionString = PlatformDefaults.ConfigValueOrDefault(
    builder.Configuration.GetConnectionString("Default"),
    $"Data Source={PlatformDefaults.DbPath}");
```

- [ ] **Step 3: Update DockerEndpoint fallback (lines 139-140)**

Change:
```csharp
    var endpoint = builder.Configuration["OpenAudioOrchestrator:DockerEndpoint"]
        ?? "npipe://./pipe/docker_engine";
```
To:
```csharp
    var endpoint = PlatformDefaults.ConfigValueOrDefault(
        builder.Configuration["OpenAudioOrchestrator:DockerEndpoint"],
        PlatformDefaults.DockerEndpoint);
```

- [ ] **Step 4: Build to verify no errors**

Run: `dotnet build src/OpenAudioOrchestrator.Web`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/OpenAudioOrchestrator.Web/Program.cs
git commit -m "refactor: use PlatformDefaults in Program.cs"
```

---

### Task 4: Update StartupTasks.cs

**Files:**
- Modify: `src/OpenAudioOrchestrator.Web/StartupTasks.cs:14`

- [ ] **Step 1: Replace the DefaultDbPath constant**

Change line 14:
```csharp
    private const string DefaultDbPath = @"C:\MyOpenAudioProj\AudioOrchestrator.db";
```
To:
```csharp
    private static string DefaultDbPath => PlatformDefaults.DbPath;
```

Note: changes from `const` to a static property since `PlatformDefaults.DbPath` is not a compile-time constant.

- [ ] **Step 2: Build to verify no errors**

Run: `dotnet build src/OpenAudioOrchestrator.Web`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/OpenAudioOrchestrator.Web/StartupTasks.cs
git commit -m "refactor: use PlatformDefaults in StartupTasks"
```

---

### Task 5: Update service and endpoint call sites

**Files:**
- Modify: `src/OpenAudioOrchestrator.Web/Endpoints/AudioEndpoints.cs:7`
- Modify: `src/OpenAudioOrchestrator.Web/Services/TtsJobProcessor.cs:34`
- Modify: `src/OpenAudioOrchestrator.Web/Services/SetupDownloadService.cs:62,67,77,82`

- [ ] **Step 1: Update AudioEndpoints.cs (line 7)**

Change:
```csharp
        var dataRoot = app.Configuration["OpenAudioOrchestrator:DataRoot"] ?? @"C:\MyOpenAudioProj";
```
To:
```csharp
        var dataRoot = PlatformDefaults.ConfigValueOrDefault(
            app.Configuration["OpenAudioOrchestrator:DataRoot"], PlatformDefaults.DataRoot);
```

- [ ] **Step 2: Update TtsJobProcessor.cs (line 34)**

Change:
```csharp
        var dataRoot = config["OpenAudioOrchestrator:DataRoot"] ?? @"C:\MyOpenAudioProj";
```
To:
```csharp
        var dataRoot = PlatformDefaults.ConfigValueOrDefault(
            config["OpenAudioOrchestrator:DataRoot"], PlatformDefaults.DataRoot);
```

- [ ] **Step 3: Update SetupDownloadService.cs — CheckGitAsync (lines 62, 67)**

Change the `CheckGitAsync` method body to:
```csharp
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
```

- [ ] **Step 4: Update SetupDownloadService.cs — CheckGitLfsAsync (lines 77, 82)**

Change the `CheckGitLfsAsync` method body to:
```csharp
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
```

- [ ] **Step 5: Build to verify no errors**

Run: `dotnet build src/OpenAudioOrchestrator.Web`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/OpenAudioOrchestrator.Web/Endpoints/AudioEndpoints.cs src/OpenAudioOrchestrator.Web/Services/TtsJobProcessor.cs src/OpenAudioOrchestrator.Web/Services/SetupDownloadService.cs
git commit -m "refactor: use PlatformDefaults in services and endpoints"
```

---

### Task 6: Update Razor page call sites

**Files:**
- Modify: `src/OpenAudioOrchestrator.Web/Components/Pages/Deploy.razor:118`
- Modify: `src/OpenAudioOrchestrator.Web/Components/Pages/GenerationHistory.razor:182`
- Modify: `src/OpenAudioOrchestrator.Web/Components/Pages/Setup.razor:387`

- [ ] **Step 1: Update Deploy.razor (line 118)**

Change:
```csharp
        var dataRoot = Config["OpenAudioOrchestrator:DataRoot"] ?? @"C:\MyOpenAudioProj";
```
To:
```csharp
        var dataRoot = PlatformDefaults.ConfigValueOrDefault(
            Config["OpenAudioOrchestrator:DataRoot"], PlatformDefaults.DataRoot);
```

- [ ] **Step 2: Update GenerationHistory.razor (line 182)**

Change:
```csharp
        var dataRoot = Config["OpenAudioOrchestrator:DataRoot"] ?? @"C:\MyOpenAudioProj";
```
To:
```csharp
        var dataRoot = PlatformDefaults.ConfigValueOrDefault(
            Config["OpenAudioOrchestrator:DataRoot"], PlatformDefaults.DataRoot);
```

- [ ] **Step 3: Update Setup.razor (line 387)**

Change:
```csharp
    private string _rootDir = @"C:\MyOpenAudioProj";
```
To:
```csharp
    private string _rootDir = PlatformDefaults.DataRoot;
```

- [ ] **Step 4: Build to verify no errors**

Run: `dotnet build src/OpenAudioOrchestrator.Web`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/OpenAudioOrchestrator.Web/Components/Pages/Deploy.razor src/OpenAudioOrchestrator.Web/Components/Pages/GenerationHistory.razor src/OpenAudioOrchestrator.Web/Components/Pages/Setup.razor
git commit -m "refactor: use PlatformDefaults in Razor pages"
```

---

### Task 7: Run full test suite

**Files:** None (verification only)

- [ ] **Step 1: Build the full solution**

Run: `dotnet build OpenAudioOrchestrator.sln`
Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 2: Run all tests**

Run: `dotnet test OpenAudioOrchestrator.sln`
Expected: All tests pass except the 3 pre-existing rate-limited `AuthEndpointTests.Signin` failures (unrelated to this work).

- [ ] **Step 3: Verify no remaining hardcoded Windows paths**

Run: `grep -rn "C:\\\\MyOpenAudioProj\|C:\\MyOpenAudioProj\|npipe://./pipe/docker_engine\|winget install" src/ --include="*.cs" --include="*.razor" --include="*.json" | grep -v /bin/ | grep -v /obj/`
Expected: Zero results.

- [ ] **Step 4: Commit (if any fixes were needed)**

Only commit if a fix was required. Otherwise, move on.

---

### Task 8: Write Linux deployment guide

**Files:**
- Create: `docs/LINUX-SETUP.md`

- [ ] **Step 1: Write the deployment guide**

Create `docs/LINUX-SETUP.md` with the following content:

```markdown
# Linux Setup Guide

This guide covers installing and running Open Audio Orchestrator on Linux. For Windows, see the main [README](../README.md).

## Prerequisites

You need: an NVIDIA GPU with CUDA drivers, Docker with NVIDIA Container Toolkit, .NET 9 SDK, and Git with Git LFS.

### Debian / Ubuntu

```bash
# NVIDIA drivers (if not already installed)
sudo apt update
sudo apt install -y nvidia-driver-550

# NVIDIA Container Toolkit
curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey | sudo gpg --dearmor -o /usr/share/keyrings/nvidia-container-toolkit-keyring.gpg
curl -s -L https://nvidia.github.io/libnvidia-container/stable/deb/nvidia-container-toolkit.list | \
  sed 's#deb https://#deb [signed-by=/usr/share/keyrings/nvidia-container-toolkit-keyring.gpg] https://#g' | \
  sudo tee /etc/apt/sources.list.d/nvidia-container-toolkit.list
sudo apt update
sudo apt install -y nvidia-container-toolkit

# Docker CE
sudo apt install -y docker.io
sudo systemctl enable --now docker
sudo nvidia-ctk runtime configure --runtime=docker
sudo systemctl restart docker

# .NET 9 SDK
sudo apt install -y dotnet-sdk-9.0

# Git + Git LFS
sudo apt install -y git git-lfs
git lfs install
```

### RHEL / Fedora

```bash
# NVIDIA drivers (RPM Fusion or official NVIDIA repo)
sudo dnf install -y akmod-nvidia

# NVIDIA Container Toolkit
curl -s -L https://nvidia.github.io/libnvidia-container/stable/rpm/nvidia-container-toolkit.repo | \
  sudo tee /etc/yum.repos.d/nvidia-container-toolkit.repo
sudo dnf install -y nvidia-container-toolkit

# Docker CE
sudo dnf install -y docker-ce docker-ce-cli containerd.io
sudo systemctl enable --now docker
sudo nvidia-ctk runtime configure --runtime=docker
sudo systemctl restart docker

# .NET 9 SDK
sudo dnf install -y dotnet-sdk-9.0

# Git + Git LFS
sudo dnf install -y git git-lfs
git lfs install
```

### Alpine

> **Note:** Alpine uses musl libc and OpenRC (not systemd). .NET 9 has official Alpine support. NVIDIA driver installation on Alpine is more involved — refer to [NVIDIA's documentation](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html) for your setup.

```bash
# Docker
sudo apk add docker
sudo rc-update add docker default
sudo service docker start

# .NET 9 SDK
sudo apk add dotnet9-sdk

# Git + Git LFS
sudo apk add git git-lfs
git lfs install
```

## Verify GPU Access

After installing prerequisites, verify Docker can access your GPU:

```bash
docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi
```

You should see your GPU listed. If this fails, check that the NVIDIA Container Toolkit is installed and Docker has been restarted.

## Build and Run

```bash
git clone https://github.com/bilbospocketses/OpenAudioOrchestrator.git
cd OpenAudioOrchestrator
dotnet run --project src/OpenAudioOrchestrator.Web
```

Navigate to `http://localhost:5206` and complete the setup wizard. The wizard detects your platform and shows Linux-appropriate defaults.

## Running as a systemd Service

For production deployments, run the app as a systemd service.

### 1. Create a service account

```bash
sudo useradd -r -s /usr/sbin/nologin oao
```

### 2. Publish the app

```bash
dotnet publish src/OpenAudioOrchestrator.Web -c Release -o /opt/OpenAudioOrchestrator/app
sudo chown -R oao:oao /opt/OpenAudioOrchestrator
```

### 3. Add the service user to the docker group

```bash
sudo usermod -aG docker oao
```

### 4. Install the systemd unit

Create `/etc/systemd/system/oao.service`:

```ini
[Unit]
Description=Open Audio Orchestrator
After=network.target docker.service
Requires=docker.service

[Service]
Type=notify
User=oao
WorkingDirectory=/opt/OpenAudioOrchestrator/app
ExecStart=/usr/bin/dotnet OpenAudioOrchestrator.Web.dll
Restart=on-failure
RestartSec=10
Environment=ASPNETCORE_URLS=http://0.0.0.0:5206
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

### 5. Enable and start

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now oao
sudo journalctl -u oao -f   # watch logs
```

Navigate to `http://your-server:5206` and complete the setup wizard.

## Alpine Notes

- Alpine uses **OpenRC**, not systemd. Adapt the service configuration to use OpenRC init scripts, `s6`, or `supervise`.
- NVIDIA driver installation on Alpine differs from Debian/RHEL. Consult the [NVIDIA Container Toolkit docs](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html).
- .NET 9 is officially supported on Alpine (musl). No compatibility issues expected.

## Troubleshooting

**"Permission denied" connecting to Docker:**
Add your user to the `docker` group: `sudo usermod -aG docker $USER`, then log out and back in.

**nvidia-smi not found:**
Ensure NVIDIA drivers are installed and `nvidia-smi` is in your PATH. The app uses this for GPU metrics on the dashboard.

**Setup wizard shows wrong default paths:**
The wizard auto-detects your platform. If it shows Windows paths on Linux, the `PlatformDefaults` detection may have failed — please open an issue.
```

- [ ] **Step 2: Commit**

```bash
git add docs/LINUX-SETUP.md
git commit -m "docs: add Linux deployment guide"
```

---

### Task 9: Update README.md

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update Prerequisites section**

Change lines 38-43:
```markdown
## Prerequisites

- Windows 10/11 with [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- NVIDIA GPU with CUDA drivers (tested on RTX 3060 12 GB)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Git](https://git-scm.com/) with [Git LFS](https://git-lfs.com/) (for model download during setup)
```
To:
```markdown
## Prerequisites

**Windows:**
- Windows 10/11 with [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- NVIDIA GPU with CUDA drivers (tested on RTX 3060 12 GB)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Git](https://git-scm.com/) with [Git LFS](https://git-lfs.com/) (for model download during setup)

**Linux:**
- Docker CE with [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html)
- NVIDIA GPU with CUDA drivers
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Git with Git LFS
- See [`docs/LINUX-SETUP.md`](docs/LINUX-SETUP.md) for detailed setup instructions
```

- [ ] **Step 2: Update To Do section**

Change:
```markdown
- [ ] Create Linux native version (platform-aware paths, Linux setup instructions), and validate deployment on several Linux variants (Debian/Ubuntu, RH/Fedora, etc)
```
To:
```markdown
- [x] ~~Create Linux native version (platform-aware paths, Linux setup instructions), and validate deployment on several Linux variants (Debian/Ubuntu, RH/Fedora, etc)~~
```

- [ ] **Step 3: Build to verify nothing broke**

Run: `dotnet build OpenAudioOrchestrator.sln`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: update README with Linux prerequisites and setup link"
```

---

### Task 10: Final verification and push

**Files:** None (verification only)

- [ ] **Step 1: Full build**

Run: `dotnet build OpenAudioOrchestrator.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test OpenAudioOrchestrator.sln`
Expected: All pass except the 3 pre-existing rate-limited auth tests.

- [ ] **Step 3: Verify zero remaining hardcoded Windows defaults in source**

Run: `grep -rn "C:\\\\MyOpenAudioProj\|C:\\MyOpenAudioProj\|npipe://./pipe/docker_engine\|winget install" src/ --include="*.cs" --include="*.razor" --include="*.json" | grep -v /bin/ | grep -v /obj/`
Expected: Zero results.

- [ ] **Step 4: Push**

Run: `git push personal master`
