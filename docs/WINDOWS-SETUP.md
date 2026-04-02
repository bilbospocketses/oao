# Windows Setup Guide

This guide covers installing and running Open Audio Orchestrator on Windows. For Linux, see [`LINUX-SETUP.md`](LINUX-SETUP.md).

## Prerequisites

You need: an NVIDIA GPU with CUDA drivers, Docker Desktop, .NET 9 SDK, and Git with Git LFS.

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

### 3. .NET 9 SDK

Install via winget:

```powershell
winget install Microsoft.DotNet.SDK.9
```

Or download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/9.0).

### 4. Git + Git LFS

```powershell
winget install Git.Git
git lfs install
```

Or download from [git-scm.com](https://git-scm.com/) and [git-lfs.com](https://git-lfs.com/).

## Build and Run

```powershell
git clone https://github.com/bilbospocketses/OpenAudioOrchestrator.git
cd OpenAudioOrchestrator
dotnet run --project src/OpenAudioOrchestrator.Web
```

Navigate to `http://localhost:5206` and complete the setup wizard. The wizard detects your platform and shows Windows-appropriate defaults.

## Setup Wizard

The 7-step setup wizard guides you through:

1. **Data Storage** — choose directories for checkpoints, references, output files, and the database
2. **Model Download** — download the Fish Audio s2-pro model (~11 GB) from HuggingFace, or skip to download later
3. **Docker Image** — download the Fish Speech Docker image (~12 GB)
4. **Server Configuration** — database encryption key, container port range, optional domain + HTTPS via Let's Encrypt
5. **Admin Account** — create your administrator username and password
6. **TOTP Setup** — scan QR code with your authenticator app
7. **Complete** — review settings and restart instructions

Downloads in steps 2 and 3 run in the background while you continue through the wizard. The final page waits for any active downloads to complete before showing restart instructions.

After completing the wizard, stop the app (Ctrl+C) and restart it. Log in with your admin credentials.

## Running as a Windows Service

For production deployments, you can run the app as a Windows service using [NSSM](https://nssm.cc/) or the built-in `sc.exe`:

### Using NSSM (recommended)

```powershell
# Publish the app
dotnet publish src/OpenAudioOrchestrator.Web -c Release -o C:\OpenAudioOrchestrator\app

# Install as a service
nssm install OpenAudioOrchestrator "C:\Program Files\dotnet\dotnet.exe" "OpenAudioOrchestrator.Web.dll"
nssm set OpenAudioOrchestrator AppDirectory "C:\OpenAudioOrchestrator\app"
nssm set OpenAudioOrchestrator AppEnvironmentExtra "ASPNETCORE_URLS=http://0.0.0.0:5206" "DOTNET_ENVIRONMENT=Production"
nssm start OpenAudioOrchestrator
```

## Troubleshooting

**Docker Desktop not starting:**
Ensure WSL 2 is installed and up to date: `wsl --update`. Restart Docker Desktop after updating.

**GPU not detected by Docker:**
Verify NVIDIA drivers are installed (`nvidia-smi` in PowerShell should show your GPU). Ensure Docker Desktop is using the WSL 2 backend, not the legacy Hyper-V backend.

**"winget not found":**
Winget is included with Windows 10 1709+ and Windows 11. If missing, install [App Installer](https://apps.microsoft.com/detail/9nblggh4nns1) from the Microsoft Store.

**Setup wizard shows wrong default paths:**
The wizard auto-detects your platform. If it shows Linux paths on Windows, the `PlatformDefaults` detection may have failed — please open an issue.
