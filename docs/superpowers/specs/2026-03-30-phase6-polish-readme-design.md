> **Note:** post-rename. References to `OpenAudioOrchestrator.*` paths and
> config keys reflect their original names. Current equivalents are
> `oao.*` and `oao:*`.
# Phase 6: Polish & README Design

## Overview

Final polish phase for the Fish Audio Orchestration Dashboard. Add project documentation (README), consistent dark-theme CSS, GPLv3 license, and improved .gitignore. No new features or architecture changes.

## README

`README.md` at project root covering:

### Header
- Project name: Fish Audio Orchestrator
- One-line description: A local Blazor Server dashboard for managing Fish Speech Docker containers with voice cloning, TTS generation, and real-time monitoring.
- GPLv3 license badge

### Overview
What the project does: runs locally on Windows, manages Fish Speech Docker containers via Docker Desktop, provides a web UI for deploying models, managing voice libraries, generating speech, and monitoring container health/GPU metrics in real time.

### Features
Bullet list:
- Docker container orchestration (create, start, stop, swap, remove)
- Voice library management (upload reference audio, tag, organize)
- TTS playground with format selection and voice cloning
- Real-time SignalR dashboard (live container status, GPU metrics, log streaming)
- Generation history with per-user scoping
- ASP.NET Identity authentication with mandatory TOTP/MFA
- Role-based access control (Admin / User)
- First-run setup wizard with FQDN configuration
- Automatic HTTPS via Let's Encrypt (optional)
- YARP reverse proxy for TTS API gateway
- Health monitoring with automatic status updates

### Prerequisites
- Windows 10/11 with Docker Desktop
- NVIDIA GPU with CUDA drivers (tested on RTX 3060 12GB)
- .NET 9 SDK
- Fish Speech Docker image: `docker pull fishaudio/fish-speech:server-cuda`

### Quick Start
1. Clone the repository
2. Create data directories at `D:\DockerData\FishAudio\` (Checkpoints, References, Output)
3. Review `appsettings.json` configuration (connection string, data root, Docker endpoint)
4. Run: `dotnet run --project src/FishAudioOrchestrator.Web`
5. Navigate to `https://localhost:5001` — the setup wizard guides first-time configuration
6. Pull and place Fish Speech model checkpoints in the Checkpoints directory

### Configuration Reference
Table of all `appsettings.json` keys:

| Key | Description | Default |
|-----|-------------|---------|
| `ConnectionStrings:Default` | SQLite database path | `D:\DockerData\FishAudio\fishorch.db` |
| `FishOrchestrator:DockerEndpoint` | Docker API endpoint | `npipe://./pipe/docker_engine` |
| `FishOrchestrator:DataRoot` | Root directory for data files | `D:\DockerData\FishAudio` |
| `FishOrchestrator:PortRange:Start` | Start of port range for containers | `9001` |
| `FishOrchestrator:PortRange:End` | End of port range for containers | `9099` |
| `FishOrchestrator:DefaultImageTag` | Default Fish Speech Docker image | `fishaudio/fish-speech:server-cuda` |
| `FishOrchestrator:DockerNetworkName` | Docker bridge network name | `fish-orchestrator` |
| `FishOrchestrator:HealthCheckIntervalSeconds` | Health check frequency | `30` |
| `FishOrchestrator:Domain` | FQDN for Let's Encrypt (blank = localhost) | `""` |
| `FishOrchestrator:AdminUser` | Env-var seed: admin username | `""` |
| `FishOrchestrator:AdminPassword` | Env-var seed: admin password | `""` |
| `LettuceEncrypt:AcceptTermsOfService` | Accept Let's Encrypt ToS | `true` |
| `LettuceEncrypt:DomainNames` | Domain names for certificate | `[]` |
| `LettuceEncrypt:EmailAddress` | Email for Let's Encrypt renewal | `""` |

### Architecture
Brief description: Blazor Server (.NET 9) + SQLite (EF Core) + Docker.DotNet + YARP reverse proxy + SignalR for real-time updates. ASP.NET Identity for auth. Link to design specs in `docs/superpowers/specs/`.

### Development
- Build: `dotnet build`
- Test: `dotnet test`
- Add migration: `cd src/FishAudioOrchestrator.Web && dotnet ef migrations add <Name>`

## CSS Polish

All changes in `wwwroot/css/app.css`. Consolidate the duplicate `wwwroot/app.css` (Blazor default validation styles) into the main theme file.

### Changes

- **Progress bar track** — dark background (`#1a1a2e`) for `.progress` to match page background
- **Toast styling** — `.toast` dark background, consistent border radius, proper text colors
- **Log viewer** — dark scrollbar styling (webkit `::-webkit-scrollbar` + Firefox `scrollbar-color`), consistent monospace `pre` background
- **Table hover** — `.table-hover tbody tr:hover` with subtle dark highlight (`rgba(96, 165, 250, 0.1)`)
- **Alert overrides** — `.alert-info`, `.alert-warning`, `.alert-success`, `.alert-danger` with dark backgrounds and muted borders matching the theme
- **Link colors** — `a` default color set to `#60a5fa` (blue accent) for consistency
- **Focus outlines** — form elements use `#60a5fa` focus ring (already partially done, ensure consistency)
- **Blazor validation styles** — merge from `wwwroot/app.css` into `wwwroot/css/app.css`
- **Remove `wwwroot/app.css`** — delete after merging its contents

### Update App.razor
Change the CSS reference from `css/app.css` to point to the consolidated file (verify current `<link>` tag references).

## GPLv3 License

Standard GPLv3 license text in `LICENSE` at project root. Copyright line: `Copyright (C) 2026 Fish Audio Orchestrator Contributors`.

## .gitignore Improvements

Add to existing `.gitignore`:
```
publish/
.idea/
*.DotSettings.user
node_modules/
dist/
```

Keep all existing entries.

## Testing

No new tests. Existing 74 tests must continue to pass. CSS and documentation changes are verified by build success only.
