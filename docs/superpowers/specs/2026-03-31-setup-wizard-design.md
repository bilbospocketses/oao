> **Note:** post-rename. References to `OpenAudioOrchestrator.*` paths and
> config keys reflect their original names. Current equivalents are
> `oao.*` and `oao:*`.
# Enhanced Setup Wizard — Design Spec

## Goal

Replace the existing 3-step setup wizard with a comprehensive guided installer that walks new users through data directory configuration, model download, Docker image pull, server configuration, and admin account creation — without requiring manual file edits or command-line knowledge.

## Wizard Flow

| Step | Title | Purpose |
|------|-------|---------|
| 1 | Data Storage | Choose Checkpoints, References, Output directories |
| 2 | Download Fish Speech Model | Clone s2-pro from HuggingFace (~11 GB) |
| 3 | Download Docker Image | Pull Fish Speech Docker image (~12 GB) |
| 4 | Server Configuration | Port range, optional domain + HTTPS |
| 5 | Create Admin Account | Username, display name, password |
| 6 | TOTP Setup | QR code scan + verification |
| 7 | Setup Complete | Summary, restart instructions, new URL |

All settings from steps 1-4 are written to `appsettings.json` in a single operation after step 4 completes. The file is read into a `JsonNode`, modified, and written back.

Steps 5-6 are the existing user creation and TOTP flow (minor renumbering, no logic changes).

The `SetupGuardMiddleware` already allows `/setup` through without auth. The wizard detects if users exist and redirects to login if setup is already complete.

---

## Step 1: Data Storage

**Title:** "Data Storage"

**Description:** "Fish Orchestrator stores model checkpoints, reference voice samples, and generated audio files in separate directories. Choose where to store each. The directories will be created if they don't exist."

**Fields:**
- Checkpoints Directory (default: `D:\DockerData\FishAudio\Checkpoints`)
- References Directory (default: `D:\DockerData\FishAudio\References`)
- Output Directory (default: `D:\DockerData\FishAudio\Output`)

All three are text inputs (no browse button — Blazor Server can't access host filesystem for folder browsing).

**Validation:**
- All three required
- No two directories may be the same path (case-insensitive comparison) — show error: "Each directory must be unique."
- On "Next": app attempts to create directories if they don't exist. If creation fails (permission error, invalid path), show the error message.

---

## Step 2: Download Fish Speech Model

**Title:** "Download Fish Speech Model"

**Description:** "Fish Orchestrator requires the Fish Audio s2-pro model. This is a large download (~11 GB) from HuggingFace. Git with Git LFS support must be installed."

**Pre-checks (run on page load):**
1. Check if `s2-pro` folder already exists in the Checkpoints directory with files inside → show "Model already downloaded" with a green checkmark. Next button enabled.
2. Check if `git` is available (`git --version`) → if not, show error: "Git is not installed. Install it from PowerShell: `winget install Git.Git`, then click Retry."
3. Check if `git lfs` is available (`git lfs version`) → if not, show error: "Git LFS is not installed. Install it from PowerShell: `git lfs install`, then click Retry."
4. Show a "Retry" button to re-run pre-checks after the user installs the missing tool.

**Download flow:**
- "Download Model" button starts `git clone https://huggingface.co/fishaudio/s2-pro <checkpoints>/s2-pro` as a background process
- Page shows a progress area with live streaming output from the process
- "Next" button is enabled immediately so the user can continue while the download runs in the background
- "Skip — I'll download it manually later" link also available
- If the user navigates away and returns, the page checks if the process is still running and shows status accordingly
- On completion: green checkmark, "Model downloaded successfully"

---

## Step 3: Download Docker Image

**Title:** "Download Docker Image"

**Description:** "Fish Orchestrator runs the TTS model inside a Docker container. The required image is approximately 12 GB."

**Pre-checks (run on page load):**
1. Check if `docker` command is available → if not, show error: "Docker is not installed or not in PATH. Install Docker Desktop from docker.com and ensure it's running."
2. Check if the image already exists locally (`docker images -q fishaudio/fish-speech:server-cuda-v2.0.0-beta`) → show "Docker image already available" with green checkmark. Next button enabled.

**Download flow:**
- "Download Image" button starts `docker pull fishaudio/fish-speech:server-cuda-v2.0.0-beta` as a background process
- Page shows a progress area with live streaming output from the process
- "Next" button is enabled immediately so the user can continue while the download runs
- If the user navigates away and returns, check if image exists to determine completion
- On completion: green checkmark, "Docker image downloaded successfully"

---

## Step 4: Server Configuration

**Title:** "Server Configuration"

**Description:** "Configure how the application listens for connections and how Docker containers are assigned ports."

### Port Range Section

**Description:** "When deploying models, each container is assigned a port from this range. Most users need only 1-2 ports unless running multiple models on a high-end GPU."

**Fields:**
- Start Port (default: 9001)
- End Port (default: 9099)

**Validation:**
- Both required, numeric
- Range: 1024-65535
- Start must be less than End
- Error messages: "Ports must be between 1024 and 65535", "Start port must be less than end port"

### Domain & HTTPS Section

**Description:** "By default, the app runs on port 5206 over HTTP. If you have a domain name pointing to this server and want automatic HTTPS via Let's Encrypt, enter it below. Otherwise, leave blank to use the default."

**Fields:**
- Domain (optional, placeholder: "e.g. fish.example.com")
- Email Address — hidden unless Domain has a value. Required when Domain is set.

**Validation:**
- Domain: regex for valid domain format — letters, numbers, dots, hyphens. Must contain at least one dot. No spaces or special characters. Examples: `fish.example.com`, `tts.myserver.org`
- Email: regex for basic email format — `something@something.something`

**Conditional notes (shown below the fields based on state):**
- If domain is entered: "After setup, restart the app and navigate to **https://yourdomain.com**. The domain must have a DNS A record pointing to this server's public IP address. Ports 80 and 443 must be accessible from the internet for Let's Encrypt to validate domain ownership and provision the certificate. If you are unfamiliar with DNS configuration and certificate provisioning, consult an AI assistant or your DNS provider's documentation before proceeding."
- If domain is blank: "After setup, restart the app and navigate to **http://localhost:5206**."

### Settings Write

On "Next": all settings from steps 1-4 are written to `appsettings.json`:
- `ConnectionStrings:Default` → SQLite path in the Output directory's parent (or a sibling `fishorch.db`)
- `FishOrchestrator:DataRoot` → parent of the three directories (or derive from Checkpoints path)
- `FishOrchestrator:PortRange:Start` and `End`
- `FishOrchestrator:Domain`
- `LettuceEncrypt:DomainNames` → array with the domain (or empty)
- `LettuceEncrypt:EmailAddress`

The write uses `JsonNode` — deserialize, modify specific fields, re-serialize. Preserves all other settings and formatting.

---

## Step 5: Create Admin Account

Existing step 2 from the current wizard, renumbered. No logic changes.

**Title:** "Create Admin Account"

**Fields:** Username, Display Name, Password, Confirm Password

**Validation:** Same as current — required fields, password match, Identity password rules.

---

## Step 6: TOTP Setup

Existing step 3 from the current wizard, renumbered. No logic changes.

**Title:** "TOTP Setup"

**Description:** "Scan this QR code with your authenticator app (Google Authenticator, Authy, etc.)."

**Fields:** QR code display, manual key, 6-digit verification code.

---

## Step 7: Setup Complete

**Title:** "Setup Complete"

**Important:** Downloads started in steps 2 and 3 are child processes of the app. If the app is stopped before they finish, the downloads are killed and must restart from scratch. This step gates the restart instructions behind download completion.

**Behavior:**
- If any background downloads (model or Docker image) are still in progress:
  - Show progress meters for each active download with streaming output
  - Show message: "Downloads are still in progress. Please wait for them to complete before restarting the application. Stopping the app now will cancel the downloads and they will need to start over."
  - The restart instructions and summary are hidden until all downloads complete
- Once all downloads are complete (or were skipped/already present):
  - Show summary panel:
    - Data directories configured
    - Model download status (downloaded / skipped)
    - Docker image status (downloaded / skipped)
    - Port range
    - Domain (if configured)
    - Admin account created
    - TOTP enabled
  - Show restart instructions:
    - "Stop the application (Ctrl+C in the terminal) and restart with:"
    - `dotnet run --project src/FishAudioOrchestrator.Web`
    - If domain was set: "Then navigate to **https://yourdomain.com**"
    - If no domain: "Then navigate to **http://localhost:5206**"

---

## Deploy Page Enhancement

When the Deploy page loads and no `s2-pro` folder exists in Checkpoints:
- Show a banner above the deploy form: "No model found in the Checkpoints directory."
- "Download Model" button triggers the same `git clone` operation as the wizard
- Progress area with streaming output
- Once complete, the deploy form becomes usable

---

## Architecture Notes

**Settings write:** Read `appsettings.json` into `System.Text.Json.Nodes.JsonNode`, modify specific properties, write back. One utility method used by the wizard.

**Background processes (steps 2 & 3):** Use `Process.Start` with `RedirectStandardOutput/Error`. Output is streamed to the page via a timer or event. The process runs independently of the page — navigating away doesn't kill it. On return, the page checks the result (folder exists, image exists).

**Existing Setup.razor:** Will be rewritten with the expanded step flow. The existing `SaveFqdnToSettings` method is replaced by the new generic settings writer.

**Files affected:**
- Rewrite: `Components/Pages/Setup.razor` (7-step wizard)
- Modify: `Components/Pages/Deploy.razor` (model download banner)
- Create: `Services/SetupService.cs` (settings writer, process runner, pre-check helpers)
