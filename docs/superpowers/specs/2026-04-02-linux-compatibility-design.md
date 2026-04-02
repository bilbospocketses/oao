# Linux Compatibility — Design Spec

**Date:** 2026-04-02
**Goal:** Make Open Audio Orchestrator run natively on Linux with the same experience as Windows — correct defaults, platform-aware setup wizard, and deployment documentation.

## Target Platforms

- **Windows 10/11** — existing, no changes to behavior
- **Debian/Ubuntu** — primary Linux target (apt)
- **RHEL/Fedora** — secondary Linux target (dnf)
- **Alpine** — supported with caveats (apk, OpenRC instead of systemd, NVIDIA driver complexity)

macOS is permanently out of scope (no NVIDIA GPU support).

## 1. PlatformDefaults Static Class

New file: `src/OpenAudioOrchestrator.Web/PlatformDefaults.cs`

A single static class that returns platform-correct values based on `OperatingSystem.IsWindows()` / `OperatingSystem.IsLinux()`. No dependencies, no state.

### Properties

| Property | Windows | Linux |
|----------|---------|-------|
| `DataRoot` | `C:\MyOpenAudioProj` | `/opt/OpenAudioOrchestrator` |
| `DbPath` | `C:\MyOpenAudioProj\AudioOrchestrator.db` | `/opt/OpenAudioOrchestrator/AudioOrchestrator.db` |
| `DockerEndpoint` | `npipe://./pipe/docker_engine` | `unix:///var/run/docker.sock` |
| `GitInstallHint` | winget instructions | apt/dnf/apk instructions |
| `GitLfsInstallHint` | PowerShell instructions | apt/dnf/apk instructions |

`DbPath` is derived from `DataRoot` via `Path.Combine()`.

Linux install hints cover all three distro families in a single string:
```
Git is not installed. Install it with your package manager:
  Debian/Ubuntu: sudo apt install git
  RHEL/Fedora: sudo dnf install git
  Alpine: sudo apk add git
Then click Retry.
```

## 2. Call Site Updates

Every location that hardcodes a Windows-specific default gets updated to use `PlatformDefaults`. The pattern at each site is the same: `Config["key"] ?? PlatformDefaults.Property`, treating empty strings as missing.

### Data root path (`PlatformDefaults.DataRoot`)

| File | Current code |
|------|-------------|
| `Program.cs` | `?? @"C:\MyOpenAudioProj"` |
| `AudioEndpoints.cs` | `?? @"C:\MyOpenAudioProj"` |
| `Deploy.razor` | `?? @"C:\MyOpenAudioProj"` |
| `GenerationHistory.razor` | `?? @"C:\MyOpenAudioProj"` |
| `TtsJobProcessor.cs` | `?? @"C:\MyOpenAudioProj"` |
| `Setup.razor` | `_rootDir = @"C:\MyOpenAudioProj"` |

### Database path (`PlatformDefaults.DbPath`)

| File | Current code |
|------|-------------|
| `StartupTasks.cs` | `DefaultDbPath = @"C:\MyOpenAudioProj\AudioOrchestrator.db"` |

### Docker endpoint (`PlatformDefaults.DockerEndpoint`)

| File | Current code |
|------|-------------|
| `Program.cs` | `?? "npipe://./pipe/docker_engine"` |

### Install instructions (`PlatformDefaults.GitInstallHint`, `GitLfsInstallHint`)

| File | Current code |
|------|-------------|
| `SetupDownloadService.cs` | 4 messages referencing winget/PowerShell |

The 4 messages (git not installed, git not in PATH, git-lfs not installed, git-lfs not in PATH) consolidate to 2 properties since the install advice is the same whether the tool is missing or not in PATH.

## 3. OS-Agnostic appsettings.json

Ship `appsettings.json` with empty values for all platform-specific settings:

```json
{
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
    "DatabaseKey": "",
    "AdminUser": "",
    "AdminPassword": ""
  },
  "LettuceEncrypt": {
    "AcceptTermsOfService": true,
    "DomainNames": [],
    "EmailAddress": ""
  }
}
```

Port range, image tag, network name, and health check interval are not platform-specific and keep their defaults. The setup wizard writes real platform-appropriate values on first run. Code treats empty/null config values as "use PlatformDefaults."

## 4. Code Already Cross-Platform (No Changes Needed)

These were audited and confirmed correct:

- **File permissions** (`StartupTasks.cs`) — Windows ACL branch + `File.SetUnixFileMode()` Linux branch already implemented
- **Data Protection** (`Program.cs`) — `OperatingSystem.IsWindows()` guard around `ProtectKeysWithDpapi()` already implemented
- **Timezone conversion** (`ContainerConfigService.cs`) — `TryConvertWindowsIdToIanaId()` is a no-op on Linux where IDs are already IANA
- **Path separators** (`ContainerConfigService.cs`) — backslash-to-forward-slash conversion for container paths already implemented
- **nvidia-smi** (`GpuMetricsParser.cs`) — invoked by name, works on both platforms when NVIDIA drivers are installed and in PATH
- **Setup wizard path display** (`Setup.razor`) — uses `Path.DirectorySeparatorChar` already

## 5. Linux Deployment Documentation

New file: `docs/LINUX-SETUP.md`

### Structure

**1. Prerequisites**

Three subsections with distro-specific commands:

- **Debian/Ubuntu:**
  - NVIDIA drivers (apt: `nvidia-driver-XXX` or official NVIDIA repo)
  - NVIDIA Container Toolkit (NVIDIA's apt repo)
  - Docker CE (Docker's apt repo)
  - .NET 9 SDK (Microsoft's apt repo)
  - Git + Git LFS (apt)

- **RHEL/Fedora:**
  - NVIDIA drivers (dnf: RPM Fusion or official NVIDIA repo)
  - NVIDIA Container Toolkit (NVIDIA's dnf repo)
  - Docker CE (Docker's dnf repo)
  - .NET 9 SDK (Microsoft's dnf repo)
  - Git + Git LFS (dnf)

- **Alpine:**
  - NVIDIA drivers — community repo or manual install, note complexity
  - NVIDIA Container Toolkit — manual setup
  - Docker (apk)
  - .NET 9 SDK (apk from community or Microsoft tar)
  - Git + Git LFS (apk)
  - Caveat: Alpine uses musl libc; .NET 9 has official Alpine support

**2. NVIDIA Container Toolkit Setup**

Shared section — add NVIDIA's repo, install `nvidia-container-toolkit`, configure Docker daemon (`/etc/docker/daemon.json`), restart Docker. This is the most common stumbling block for Linux GPU workloads.

**3. Build and Run**

```bash
git clone https://github.com/bilbospocketses/OpenAudioOrchestrator.git
cd OpenAudioOrchestrator
dotnet run --project src/OpenAudioOrchestrator.Web
```

Navigate to `http://localhost:5206`, complete setup wizard. Same as Windows.

**4. Running as a systemd Service**

Steps:
1. Create a service account (`useradd -r -s /usr/sbin/nologin oao`)
2. Publish the app (`dotnet publish -c Release -o /opt/OpenAudioOrchestrator/app`)
3. Set ownership (`chown -R oao:oao /opt/OpenAudioOrchestrator`)
4. Install the unit file
5. Enable and start (`systemctl enable --now oao`)

Sample unit file:
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

**5. Alpine Notes**

- Alpine uses OpenRC, not systemd — adapt the service configuration or use `s6`/`supervise`
- NVIDIA driver installation differs from Debian/RHEL — refer to NVIDIA's documentation
- .NET 9 is officially supported on Alpine (musl)

**6. README.md Update**

- Replace "Linux testing and instructions coming soon..." with link to `docs/LINUX-SETUP.md`
- Update Prerequisites section to list both Windows and Linux requirements
- Update Quick Start to show both platforms

## 6. Testing

### Unit Tests

Add tests for `PlatformDefaults`:
- All properties return non-null, non-empty strings
- `DataRoot` returns a valid absolute path
- `DbPath` contains `DataRoot` as a prefix
- `DockerEndpoint` returns a valid URI scheme (`npipe://` or `unix://`)
- `GitInstallHint` and `GitLfsInstallHint` contain distro-relevant keywords

These are basic sanity checks — we can't change the OS at runtime, so we verify the shape of the output for the current platform.

### Manual Validation

- **Windows:** fresh clone → wizard → deploy → TTS — confirm no regression
- **Linux:** fresh clone → wizard → verify Linux defaults shown → deploy → TTS

### Existing Tests

No changes needed to the existing 201 tests. They use in-memory configs and temp directories, never hitting `PlatformDefaults` fallback paths.

## Out of Scope

- Containerizing the Blazor app (separate To Do item)
- OpenRC service script for Alpine
- Building service management into the app UI
- macOS support (permanently out of scope)
