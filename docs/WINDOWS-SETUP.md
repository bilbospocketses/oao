# Windows Setup Guide

This guide covers installing and running Open Audio Orchestrator on Windows. For Linux, see [`LINUX-SETUP.md`](LINUX-SETUP.md).

## Prerequisites

You need: an NVIDIA GPU with CUDA drivers, Docker Desktop, .NET 10 SDK, and Git with Git LFS.

### 1. NVIDIA Drivers

Install the latest NVIDIA Game Ready or Studio drivers from [nvidia.com/drivers](https://www.nvidia.com/Download/index.aspx). Tested on RTX 3060 12 GB.

### 2. Docker Desktop

Download and install [Docker Desktop](https://www.docker.com/products/docker-desktop/). During setup, ensure **WSL 2 backend** is selected (recommended).

After installation, verify Docker is running:

```powershell
docker --version
docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi
```

The second command should show your GPU. If it fails, open Docker Desktop Settings > Docker Engine, and ensure the NVIDIA runtime is configured. You may also need to install the [NVIDIA Container Toolkit for WSL2](https://docs.nvidia.com/cuda/wsl-user-guide/index.html).

### 3. .NET 10 SDK

Install via winget:

```powershell
winget install Microsoft.DotNet.SDK.10
```

Or download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0).

### 4. Git + Git LFS

```powershell
winget install Git.Git
git lfs install
```

Or download from [git-scm.com](https://git-scm.com/) and [git-lfs.com](https://git-lfs.com/).

## Build and Run

```powershell
git clone https://github.com/bilbospocketses/oao.git
cd oao
dotnet run --project src/oao.Web -c Release
```

Navigate to `http://localhost:5206` and complete the setup wizard. The wizard detects your platform and shows Windows-appropriate defaults.

## Setup Wizard

The 7-step setup wizard guides you through:

1. **Data Storage** — choose directories for checkpoints, references, output files, and the database
2. **Model Download** — download the Fish Audio s2-pro model (~11 GB) from HuggingFace, or skip to download later
3. **Docker Image** — download the Fish Speech Docker image (~5 GB)
4. **Server Configuration** — database encryption key, container port range, optional domain + automatic HTTPS via Let's Encrypt
5. **Admin Account** — create your administrator username, display name, and password
6. **TOTP Setup** — scan QR code with your authenticator app
7. **Complete** — review settings and restart instructions

Downloads in steps 2 and 3 run in the background while you continue through the wizard. The final page waits for any active downloads to complete before showing restart instructions.

After completing the wizard, stop the app (Ctrl+C) and restart it. Log in with your admin credentials.

## Running as a Windows Service

For production deployments, run the app as a Windows service using [Servy](https://github.com/aelassas/servy).

Install Servy from its [releases page](https://github.com/aelassas/servy/releases) and ensure `servy-cli.exe` is on your `PATH` (or substitute its full path in the commands below).

```powershell
# Publish the app
dotnet publish src/oao.Web -c Release -o C:\oao\app

# Install as a service (run elevated)
servy-cli install `
    --name oao `
    --displayName "Open Audio Orchestrator" `
    --description "Open Audio Orchestrator dashboard for Fish Speech TTS containers" `
    --path "%ProgramFiles%\dotnet\dotnet.exe" `
    --params "oao.Web.dll" `
    --startupDir "C:\oao\app" `
    --startupType AutomaticDelayedStart `
    --envVars "ASPNETCORE_URLS=http://0.0.0.0:5206;DOTNET_ENVIRONMENT=Production"

servy-cli start --name oao
```

Manage the service with the same `--name oao` argument:

```powershell
servy-cli status    --name oao
servy-cli restart   --name oao
servy-cli stop      --name oao
servy-cli uninstall --name oao
```

> **Future automation.** This manual `dotnet publish` + `servy-cli install`
> flow is the interim production-deploy path. The forthcoming Velopack
> installer (active TODO) will replace it: app binaries will live at
> `C:\Program Files\oao\current` (Velopack `current/` sibling layout),
> user settings + database + ACME certs + Data-Protection keys + model
> files at `C:\ProgramData\oao`, and the app will gain a `--service`
> entrypoint that Servy invokes directly (no `dotnet.exe` wrapper). The
> Servy install command will simplify to roughly:
>
> ```powershell
> servy-cli install --name oao `
>     --path "%ProgramFiles%\oao\current\oao.Web.exe" `
>     --params "--service" `
>     --startupDir "%ProgramFiles%\oao\current" `
>     --envVars "oao__DataRoot=%ProgramData%\oao"
> ```

**First-run note:** Servy registers a Windows Event Log source named `Servy` on first invocation, which requires Administrator elevation. After that one-time registration, day-to-day commands can run as a normal user.

## Troubleshooting

**Docker Desktop not starting:**
Ensure WSL 2 is installed and up to date: `wsl --update`. Restart Docker Desktop after updating.

**GPU not detected by Docker:**
Verify NVIDIA drivers are installed (`nvidia-smi` in PowerShell should show your GPU). Ensure Docker Desktop is using the WSL 2 backend, not the legacy Hyper-V backend.

**"winget not found":**
Winget is included with Windows 10 1709+ and Windows 11. If missing, install [App Installer](https://apps.microsoft.com/detail/9nblggh4nns1) from the Microsoft Store.

**Setup wizard shows wrong default paths:**
The wizard auto-detects your platform. If it shows Linux paths on Windows, the `PlatformDefaults` detection may have failed — please open an issue.
