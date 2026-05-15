> **Note:** post-rename. References to `OpenAudioOrchestrator.*` paths and
> config keys reflect their original names. Current equivalents are
> `oao.*` and `oao:*`.
# Phase 6: Polish & README Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add project documentation (README, LICENSE), consolidate and polish the dark-theme CSS, and clean up .gitignore.

**Architecture:** No new application code. README at project root, GPLv3 LICENSE file, consolidated CSS in `wwwroot/css/app.css`, improved .gitignore. All existing 74 tests must continue to pass.

**Tech Stack:** Markdown, CSS, GPLv3

---

## File Structure

### New Files

| File | Responsibility |
|------|---------------|
| `README.md` | Project documentation |
| `LICENSE` | GPLv3 license text |

### Modified Files

| File | Change |
|------|--------|
| `src/.../wwwroot/css/app.css` | Consolidated dark theme CSS with polish |
| `.gitignore` | Add standard .NET ignores |

### Deleted Files

| File | Reason |
|------|--------|
| `src/.../wwwroot/app.css` | Merged into `wwwroot/css/app.css` |

---

## Task 1: GPLv3 License

**Files:**
- Create: `LICENSE`

- [ ] **Step 1: Create LICENSE file**

Create `LICENSE` at the project root with the standard GPLv3 text. The file should begin with:

```
                    GNU GENERAL PUBLIC LICENSE
                       Version 3, 29 June 2007

 Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 Everyone is permitted to copy and distribute verbatim copies
 of this license document, but changing it is not allowed.
```

Use the full standard GPLv3 text from https://www.gnu.org/licenses/gpl-3.0.txt.

Add a copyright header at the very top before the license text:

```
Fish Audio Orchestrator
Copyright (C) 2026 Fish Audio Orchestrator Contributors

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.

---

```

Then the full GPLv3 text follows.

- [ ] **Step 2: Commit**

```bash
git add LICENSE
git commit -m "chore: add GPLv3 license"
```

---

## Task 2: .gitignore Improvements

**Files:**
- Modify: `.gitignore`

- [ ] **Step 1: Update .gitignore**

Replace `.gitignore` with:

```
bin/
obj/
*.user
*.suo
.vs/
*.db
*.db-shm
*.db-wal
appsettings.Development.json
publish/
.idea/
*.DotSettings.user
node_modules/
dist/
```

- [ ] **Step 2: Commit**

```bash
git add .gitignore
git commit -m "chore: improve .gitignore with standard .NET ignores"
```

---

## Task 3: CSS Consolidation and Polish

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/wwwroot/css/app.css`
- Delete: `src/FishAudioOrchestrator.Web/wwwroot/app.css`

- [ ] **Step 1: Replace css/app.css with consolidated and polished theme**

Replace `src/FishAudioOrchestrator.Web/wwwroot/css/app.css` with:

```css
/* ============================================
   Fish Audio Orchestrator — Dark Theme
   ============================================ */

/* --- Base --- */

body {
    background-color: #1a1a2e;
    color: #e2e8f0;
}

a {
    color: #60a5fa;
}

a:hover {
    color: #93bbfc;
}

h1:focus {
    outline: none;
}

/* --- Navbar --- */

.navbar {
    border-bottom: 1px solid #333;
}

/* --- Cards --- */

.card {
    background-color: #16213e;
    border-color: #333;
}

.card-header {
    background-color: #16213e;
    border-bottom-color: #333;
}

.card-footer {
    background-color: #16213e;
    border-top-color: #333;
}

/* --- Tables --- */

.table {
    color: #e2e8f0;
}

.table-hover tbody tr:hover {
    background-color: rgba(96, 165, 250, 0.08);
    color: #e2e8f0;
}

.table-dark {
    --bs-table-bg: #16213e;
    --bs-table-border-color: #333;
    --bs-table-hover-bg: rgba(96, 165, 250, 0.1);
    --bs-table-striped-bg: rgba(255, 255, 255, 0.03);
}

/* --- Forms --- */

.form-control, .form-select {
    background-color: #0f3460;
    border-color: #444;
    color: #e2e8f0;
}

.form-control:focus, .form-select:focus {
    background-color: #0f3460;
    border-color: #60a5fa;
    color: #e2e8f0;
    box-shadow: 0 0 0 0.2rem rgba(96, 165, 250, 0.25);
}

.form-control::placeholder {
    color: #8892a4;
}

.form-check-input:checked {
    background-color: #60a5fa;
    border-color: #60a5fa;
}

.form-text {
    color: #8892a4 !important;
}

/* --- Alerts --- */

.alert-info {
    background-color: rgba(96, 165, 250, 0.15);
    border-color: rgba(96, 165, 250, 0.3);
    color: #93bbfc;
}

.alert-success {
    background-color: rgba(16, 185, 129, 0.15);
    border-color: rgba(16, 185, 129, 0.3);
    color: #6ee7b7;
}

.alert-warning {
    background-color: rgba(245, 158, 11, 0.15);
    border-color: rgba(245, 158, 11, 0.3);
    color: #fbbf24;
}

.alert-danger {
    background-color: rgba(239, 68, 68, 0.15);
    border-color: rgba(239, 68, 68, 0.3);
    color: #fca5a5;
}

/* --- Modals --- */

.modal-content {
    background-color: #16213e;
    color: #e2e8f0;
}

/* --- Progress Bars --- */

.progress {
    background-color: #0f3460;
}

/* --- Toasts --- */

.toast {
    background-color: #16213e;
    border-radius: 0.5rem;
}

.toast-header {
    border-bottom-color: #333;
}

.toast-body {
    color: #e2e8f0;
}

/* --- Badges --- */

.badge.bg-success {
    background-color: #10b981 !important;
}

.badge.bg-danger {
    background-color: #ef4444 !important;
}

/* --- Status Indicators --- */

.active-banner {
    background-color: rgba(16, 185, 129, 0.1);
    border: 1px solid #10b981;
    border-radius: 8px;
    padding: 1rem;
    margin-bottom: 1rem;
}

.status-dot {
    display: inline-block;
    width: 10px;
    height: 10px;
    border-radius: 50%;
    margin-right: 8px;
}

.status-dot.running {
    background-color: #10b981;
    box-shadow: 0 0 6px #10b981;
}

.status-dot.stopped {
    background-color: #666;
}

.status-dot.error {
    background-color: #ef4444;
    box-shadow: 0 0 6px #ef4444;
}

.status-dot.created {
    background-color: #60a5fa;
}

/* --- Log Viewer --- */

pre {
    scrollbar-width: thin;
    scrollbar-color: #444 #1a1a2e;
}

pre::-webkit-scrollbar {
    width: 8px;
    height: 8px;
}

pre::-webkit-scrollbar-track {
    background: #1a1a2e;
}

pre::-webkit-scrollbar-thumb {
    background-color: #444;
    border-radius: 4px;
}

/* General dark scrollbars for log containers */
.overflow-y-auto, [style*="overflow-y: auto"] {
    scrollbar-width: thin;
    scrollbar-color: #444 #1a1a2e;
}

/* --- Blazor Validation --- */

.valid.modified:not([type=checkbox]) {
    outline: 1px solid #10b981;
}

.invalid {
    outline: 1px solid #ef4444;
}

.validation-message {
    color: #fca5a5;
}

.blazor-error-boundary {
    background: url(data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iNTYiIGhlaWdodD0iNDkiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgeG1sbnM6eGxpbms9Imh0dHA6Ly93d3cudzMub3JnLzE5OTkveGxpbmsiIG92ZXJmbG93PSJoaWRkZW4iPjxkZWZzPjxjbGlwUGF0aCBpZD0iY2xpcDAiPjxyZWN0IHg9IjIzNSIgeT0iNTEiIHdpZHRoPSI1NiIgaGVpZ2h0PSI0OSIvPjwvY2xpcFBhdGg+PC9kZWZzPjxnIGNsaXAtcGF0aD0idXJsKCNjbGlwMCkiIHRyYW5zZm9ybT0idHJhbnNsYXRlKC0yMzUgLTUxKSI+PHBhdGggZD0iTTI2My41MDYgNTFDMjY0LjcxNyA1MSAyNjUuODEzIDUxLjQ4MzcgMjY2LjYwNiA1Mi4yNjU4TDI2Ny4wNTIgNTIuNzk4NyAyNjcuNTM5IDUzLjYyODMgMjkwLjE4NSA5Mi4xODMxIDI5MC41NDUgOTIuNzk1IDI5MC42NTYgOTIuOTk2QzI5MC44NzcgOTMuNTEzIDI5MSA5NC4wODE1IDI5MSA5NC42NzgyIDI5MSA5Ny4wNjUxIDI4OS4wMzggOTkgMjg2LjYxNyA5OUwyNDAuMzgzIDk5QzIzNy45NjMgOTkgMjM2IDk3LjA2NTEgMjM2IDk0LjY3ODIgMjM2IDk0LjM3OTkgMjM2LjAzMSA5NC4wODg2IDIzNi4wODkgOTMuODA3MkwyMzYuMzM4IDkzLjAxNjIgMjM2Ljg1OCA5Mi4xMzE0IDI1OS40NzMgNTMuNjI5NCAyNTkuOTYxIDUyLjc5ODUgMjYwLjQwNyA1Mi4yNjU4QzI2MS4yIDUxLjQ4MzcgMjYyLjI5NiA1MSAyNjMuNTA2IDUxWk0yNjMuNTg2IDY2LjAxODNDMjYwLjczNyA2Ni4wMTgzIDI1OS4zMTMgNjcuMTI0NSAyNTkuMzEzIDY5LjMzNyAyNTkuMzEzIDY5LjYxMDIgMjU5LjMzMiA2OS44NjA4IDI1OS4zNzEgNzAuMDg4N0wyNjEuNzk1IDg0LjAxNjEgMjY1LjM4IDg0LjAxNjEgMjY3LjgyMSA2OS43NDc1QzI2Ny44NiA2OS43MzA5IDI2Ny44NzkgNjkuNTg3NyAyNjcuODc5IDY5LjMxNzkgMjY3Ljg3OSA2Ny4xMTgyIDI2Ni40NDggNjYuMDE4MyAyNjMuNTg2IDY2LjAxODNaTTI2My41NzYgODYuMDU0N0MyNjEuMDQ5IDg2LjA1NDcgMjU5Ljc4NiA4Ny4zMDA1IDI1OS43ODYgODkuNzkyMSAyNTkuNzg2IDkyLjI4MzcgMjYxLjA0OSA5My41Mjk1IDI2My41NzYgOTMuNTI5NSAyNjYuMTE2IDkzLjUyOTUgMjY3LjM4NyA5Mi4yODM3IDI2Ny4zODcgODkuNzkyMSAyNjcuMzg3IDg3LjMwMDUgMjY2LjExNiA4Ni4wNTQ3IDI2My41NzYgODYuMDU0N1oiIGZpbGw9IiNGRkU1MDAiIGZpbGwtcnVsZT0iZXZlbm9kZCIvPjwvZz48L3N2Zz4=) no-repeat 1rem/1.8rem, #b32121;
    padding: 1rem 1rem 1rem 3.7rem;
    color: white;
}

.blazor-error-boundary::after {
    content: "An error has occurred."
}

.darker-border-checkbox.form-check-input {
    border-color: #929292;
}
```

- [ ] **Step 2: Delete the duplicate wwwroot/app.css**

Delete `src/FishAudioOrchestrator.Web/wwwroot/app.css`.

- [ ] **Step 3: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 4: Run tests**

Run: `dotnet test --nologo -v q`
Expected: All 74 tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: consolidate and polish dark-theme CSS, remove duplicate app.css"
```

---

## Task 4: README

**Files:**
- Create: `README.md`

- [ ] **Step 1: Create README.md**

Create `README.md` at the project root:

````markdown
# Fish Audio Orchestrator

A local Blazor Server dashboard for managing Fish Speech Docker containers with voice cloning, TTS generation, and real-time monitoring.

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)

## Overview

Fish Audio Orchestrator runs locally on Windows and manages [Fish Speech](https://github.com/fishaudio/fish-speech) Docker containers via Docker Desktop. It provides a web interface for deploying TTS models, managing a voice reference library, generating speech, and monitoring container health and GPU metrics in real time.

## Features

- **Docker container orchestration** — create, start, stop, swap, and remove Fish Speech containers
- **Voice library** — upload reference audio for voice cloning, tag and organize voices
- **TTS playground** — generate speech with format selection and optional voice cloning
- **Real-time dashboard** — live container status, GPU memory/utilization, and TTS generation notifications via SignalR
- **Container log streaming** — live Docker log viewer with backfill and per-container subscription
- **Generation history** — searchable log of all TTS generations, scoped per user
- **Authentication** — ASP.NET Identity with mandatory TOTP/MFA
- **Role-based access control** — Admin (full access) and User (TTS, voice browsing, own history)
- **First-run setup wizard** — guided admin account creation with TOTP enrollment
- **HTTPS** — optional automatic certificate provisioning via Let's Encrypt
- **API gateway** — YARP reverse proxy for the Fish Speech TTS API
- **Health monitoring** — periodic container health checks with automatic status updates

## Prerequisites

- Windows 10/11 with [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- NVIDIA GPU with CUDA drivers (tested on RTX 3060 12 GB)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Fish Speech Docker image:
  ```bash
  docker pull fishaudio/fish-speech:server-cuda
  ```

## Quick Start

1. **Clone the repository**

   ```bash
   git clone <repository-url>
   cd FishAudioOrchestrator
   ```

2. **Create data directories**

   ```bash
   mkdir -p D:\DockerData\FishAudio\Checkpoints
   mkdir -p D:\DockerData\FishAudio\References
   mkdir -p D:\DockerData\FishAudio\Output
   ```

3. **Review configuration**

   Edit `src/FishAudioOrchestrator.Web/appsettings.json` if you need to change the data root, Docker endpoint, or port range (see [Configuration](#configuration) below).

4. **Run the application**

   ```bash
   dotnet run --project src/FishAudioOrchestrator.Web
   ```

5. **Complete setup**

   Navigate to `https://localhost:5001`. The first-run setup wizard will guide you through:
   - Optional FQDN configuration (for Let's Encrypt HTTPS)
   - Admin account creation
   - TOTP enrollment (scan QR code with your authenticator app)

6. **Deploy a model**

   Place Fish Speech model checkpoints in `D:\DockerData\FishAudio\Checkpoints\`, then use the Deploy page to register and start a model.

## Configuration

All configuration is in `src/FishAudioOrchestrator.Web/appsettings.json`:

| Key | Description | Default |
|-----|-------------|---------|
| `ConnectionStrings:Default` | SQLite database path | `D:\DockerData\FishAudio\fishorch.db` |
| `FishOrchestrator:DockerEndpoint` | Docker API endpoint | `npipe://./pipe/docker_engine` |
| `FishOrchestrator:DataRoot` | Root directory for data files | `D:\DockerData\FishAudio` |
| `FishOrchestrator:PortRange:Start` | Start of container port range | `9001` |
| `FishOrchestrator:PortRange:End` | End of container port range | `9099` |
| `FishOrchestrator:DefaultImageTag` | Default Fish Speech Docker image | `fishaudio/fish-speech:server-cuda` |
| `FishOrchestrator:DockerNetworkName` | Docker bridge network name | `fish-orchestrator` |
| `FishOrchestrator:HealthCheckIntervalSeconds` | Health check frequency (seconds) | `30` |
| `FishOrchestrator:Domain` | FQDN for Let's Encrypt (blank = localhost) | `""` |
| `FishOrchestrator:AdminUser` | Seed admin username (env var override) | `""` |
| `FishOrchestrator:AdminPassword` | Seed admin password (env var override) | `""` |
| `LettuceEncrypt:AcceptTermsOfService` | Accept Let's Encrypt terms | `true` |
| `LettuceEncrypt:DomainNames` | Domain names for certificate | `[]` |
| `LettuceEncrypt:EmailAddress` | Email for certificate renewal notices | `""` |

For automated deployments, set `FishOrchestrator__AdminUser` and `FishOrchestrator__AdminPassword` as environment variables to seed the admin account on first run (TOTP setup required on first login).

## Architecture

- **Blazor Server** (.NET 9) — interactive server-side UI
- **SQLite** (EF Core) — model profiles, voice library, generation logs, Identity tables
- **Docker.DotNet** — container lifecycle management via Docker Desktop
- **YARP** — reverse proxy routing to the active Fish Speech container
- **SignalR** — real-time container status, GPU metrics, TTS notifications, log streaming
- **ASP.NET Identity** — authentication with mandatory TOTP/MFA
- **LettuceEncrypt** — automatic Let's Encrypt HTTPS (optional)

Design specifications are in [`docs/superpowers/specs/`](docs/superpowers/specs/).

## Development

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run in development mode
dotnet run --project src/FishAudioOrchestrator.Web

# Add an EF Core migration
cd src/FishAudioOrchestrator.Web
dotnet ef migrations add <MigrationName>
```

## License

This project is licensed under the GNU General Public License v3.0 — see the [LICENSE](LICENSE) file for details.
````

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add comprehensive README with setup, config, and architecture docs"
```

---

## Task 5: Final Verification

- [ ] **Step 1: Run full test suite**

Run: `dotnet test --nologo -v q`
Expected: All 74 tests pass.

- [ ] **Step 2: Verify Release build**

Run: `dotnet build -c Release --nologo -v q`
Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Tag the release**

```bash
git tag v0.6.0-phase6
```
