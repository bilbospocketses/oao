# Changelog

All notable changes and post-plan decisions for Open Audio Orchestrator (formerly Fish Audio Orchestrator).

Design specs and implementation plans in `superpowers/specs/` and `superpowers/plans/` reflect decisions made at the time they were written. This changelog captures adjustments made after those documents were finalized.

---

## 2026-04-01 — Codebase Audit, Theme System & Wizard Improvements

### Theme System
- **Light/dark theme toggle** — per-user preference stored in database via new `ThemePreference` column on `AppUser`
- **CSS custom properties** — entire stylesheet converted from hardcoded colors to `[data-theme]` variable system; dark theme uses neutral greys (all blue removed), light theme is clean white
- **Navbar toggle** — instant theme switch via JS interop with async DB persistence; correct theme rendered on first paint (no flash)
- **GET/POST `/api/auth/theme`** — API endpoints for theme preference

### Audit Fixes (29 findings)

#### Critical
- **`/api/auth/signin` converted from GET to POST** with rate limiting; token moved from query param to form body; LoginTotp uses dedicated JS function instead of `eval()`
- **SignalR hub container ID validation** — shared `ContainerIdValidator` regex applied in hub, log service, and orchestrator service
- **CSP tightened** — added `connect-src`, `frame-ancestors`, `base-uri`, `form-action`; `cdn.jsdelivr.net` added to `style-src` for Bootstrap CDN; documented `unsafe-inline` requirement for Blazor

#### Medium
- **SetupGuardMiddleware** — benign race condition fixed with `Interlocked.Exchange`
- **TtsJobProcessor** — static `SemaphoreSlim` replaced with injectable `TtsJobSignal` singleton for test isolation
- **Dashboard RemoveModel** — fixed detached entity across DbContexts; orchestrator now loads fresh from tracked context
- **ContainerLogService** — TOCTOU race in unsubscribe methods fixed; both subscriber types checked atomically under lock
- **GpuMetricsParser** — cancellation token now applied to `ReadToEndAsync` (was missing)
- **HealthMonitorService** — `OperationCanceledException` from `Task.Delay` now caught for clean shutdown
- **Antiforgery tokens** — added `<AntiforgeryToken />` to login, signout, and TOTP signin forms
- **DatabaseKey fallback** — improved logging with `Console.Error.WriteLine` for plaintext fallback
- **Audio endpoint ordering** — `UseAntiforgery()` moved before `MapAudioEndpoints()`
- **VoiceLibraryService** — path traversal validation added to `AddVoiceAsync` and `DeleteVoiceAsync`
- **SetupDownloadService** — refactored to use `ProcessStartInfo.ArgumentList` (fixed latent bugs in git lfs and docker version commands)
- **Cookie SecurePolicy** — conditional `Always` when HTTPS domain is configured, `SameAsRequest` for localhost
- **DockerOrchestratorService** — container ID format validation added via shared `ContainerIdValidator`

#### Low
- **JsonSerializerOptions** cached as static readonly in `TtsClientService`
- **StringHelpers.Truncate** — null handling added
- **Bootstrap CDN** — SRI integrity hash added
- **NavMenu** — `StateHasChanged` guarded against `ObjectDisposedException`
- **MainLayout toast** — fire-and-forget `Task.Run` replaced with `CancellationTokenSource` disposal guard
- **Deploy.razor timer** — disposal guard added
- **GenerationHistory deletion** — path traversal validation added
- **Setup.razor** — synchronous `Users.Any()` replaced with `AnyAsync()`
- **TtsPlayground CancelJob** — now checks fresh status and delegates to `TtsJobProcessor.CancelJobAsync` for processing jobs
- **HttpClient timeout** — set to 5 hours (was infinite)
- **ContainerConfigService** — Docker container name regex validation on `profile.Name`
- **ProxyConfigProvider** — lock added to prevent CTS double-dispose race
- **TtsClientService.GenerateAsync** — dead code removed (only `GetHealthAsync` retained)

### Setup Wizard Improvements
- **Database file path** — user can now choose where to store the database (previously derived implicitly)
- **WAL checkpoint** — flush before copying DB to user-chosen path (prevents empty-DB-on-restart)
- **Data Protection keys** — stored in app directory (`ContentRootPath/.dp-keys`) instead of `DataRoot` (prevents key loss when DataRoot changes)
- **DPAPI encryption** — DP keys encrypted at rest on Windows
- **Database encryption** — uses copy+`sqlcipher_export` approach to avoid EF Core file lock
- **Cleanup** — leftover default-location DB files deleted on next startup
- **DB directory creation** — ensured before EF Core migrations run
- **`StateHasChanged`** — added after async pre-checks so "already present" status renders

### Other
- **TTS timeout** — increased from 2 hours to 5 hours (curl `--max-time`, `JobTimeout`, HttpClient)
- **Default paths** — renamed from `MyFishAudioProj` to `MyOpenAudioProj`
- **File input styling** — `::file-selector-button` themed for dark/light
- **`btn-outline-light`** — restyled with theme variables for both themes
- **SQLite DateTimeOffset** — `OrderBy`/`Where` on `CreatedAt` replaced with `Id` for EF Core 9 compatibility
- **README** — independence disclaimer added

---

## 2026-04-01 — Security & Code Audit (Prior Pass)

### Critical Fixes
- **Command injection eliminated (TtsJobProcessor):** Replaced all `Process.Start("docker", ...)` shell-outs with Docker.DotNet SDK exec API (`ExecCreateContainerAsync` + `StartAndAttachContainerExecAsync`). Added container ID validation regex (`^[a-f0-9]{12,64}$`) as defense-in-depth.
- **Command injection eliminated (SetupService):** Added input validation for Docker image tags and filesystem paths before passing to `Process.Start` in model download and Docker pull operations.
- **YARP reverse proxy secured:** Added `.RequireAuthorization()` to the YARP proxy mapping — `/api/tts/*` was previously accessible without authentication.

### High-Severity Fixes
- **SignalR hub container ID validated:** `OrchestratorHub.SubscribeLogs` now verifies the container ID belongs to a known `ModelProfile` before subscribing, preventing log access to arbitrary Docker containers on the host.
- **Generation history delete authorization:** Non-admin users can now only delete their own generation entries, not other users' entries.
- **Path traversal hardened:** Audio file endpoints (`/audio/output/`, `/audio/references/`) now use `Path.GetFullPath` canonical prefix checking instead of simple `..` string checks.
- **Cookie policy fixed for HTTP deployments:** Changed `CookieSecurePolicy.Always` to `CookieSecurePolicy.SameAsRequest` so auth cookies work on the default HTTP deployment (port 5206).

### Medium-Severity Fixes
- **TOTP brute-force prevented:** TOTP verification page now limits to 5 attempts per pending token, with a visible countdown timer (2 minutes). Pending token expiry reduced from 5 minutes to 2 minutes.
- **Signout CSRF fixed:** Changed `/api/auth/signout` from GET to POST to prevent cross-site logout attacks.
- **Checkpoint path restricted:** Deploy page now validates that checkpoint paths are within the configured `Checkpoints` directory.
- **Session fixation defense:** Security stamp is regenerated on login (`UpdateSecurityStampAsync`), invalidating all prior sessions.

### Bug Fixes
- **DbContext lifetime mismatch (Dashboard.RemoveModel):** Fixed by fetching entity from the new context instead of using a detached entity from a disposed context.
- **Stale entity reference (TtsPlayground.RetryJob):** Replaced `db.Attach(job)` with a fresh entity fetch from the new context.
- **Race condition (job cancel vs. poll):** Poll loop now also kills curl in the container when detecting cancellation, making cleanup idempotent from both sides.
- **Thread safety (HealthMonitorService):** `_consecutiveFailures` now uses `Interlocked` operations instead of plain increment/assignment.
- **Timer callbacks after disposal (Setup.razor):** Added `CancellationTokenSource` guard to prevent `ObjectDisposedException` when timer callbacks fire after Blazor circuit disconnect.
- **GPU metrics parser timeout:** Added 5-second timeout to `nvidia-smi` process to prevent hang if the process stalls.

### Security Hardening
- **Security headers added:** `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, and `Content-Security-Policy` header.
- **Container ports bound to localhost:** TTS containers now bind to `127.0.0.1` instead of `0.0.0.0`.
- **SQLite database encryption:** Added SQLCipher at-rest encryption via `SQLitePCLRaw.bundle_e_sqlcipher`. Setup wizard collects encryption key with mandatory backup confirmation checkbox. Key protected by ASP.NET Data Protection API before storage in appsettings.json.
- **Database file permissions restricted:** On startup, the database file ACL is restricted to the current user (Windows) or `chmod 600` (Linux).

### Performance & Efficiency
- **N+1 query eliminated (UserManagement):** Replaced per-user `GetRolesAsync` loop with a single join query.
- **Generation history paginated:** Added cursor-based pagination (50 per page) with "Load More" button.
- **TTS job processor idle optimization:** Replaced fixed 2-second polling with `SemaphoreSlim` signaling from the UI, falling back to 30-second poll.
- **PostLoginRedirect middleware cached:** Setup status check now uses `IMemoryCache` with 60-second TTL and explicit eviction on state change, eliminating per-request DB queries for most users.

### Architecture & Refactoring
- **Program.cs extracted:** Auth endpoints → `Endpoints/AuthEndpoints.cs`, audio endpoints → `Endpoints/AudioEndpoints.cs`, startup tasks → `StartupTasks.cs`. Program.cs reduced from 303 to 170 lines.
- **SetupService split:** Separated into `SetupDownloadService` (pre-checks, background downloads), `SetupSettingsService` (settings I/O, DB encryption), and `SetupValidation` (static validation helpers).
- **Event bus weak references:** `OrchestratorEventBus` events now use `WeakEvent<T>` with `WeakReference` to subscriber targets. Dead subscribers are automatically pruned on invocation. `ObjectDisposedException` from disposed subscribers is silently caught; other exceptions are logged.

---

## 2026-04-01

### Security: TOTP Sign-In Endpoint Hardened
- **Before:** `/api/auth/signin?userId=<guid>` signed in any user by ID with no verification
- **After:** Endpoint requires a cryptographic one-time token generated after successful TOTP verification; token expires after 60 seconds and is single-use
- **Also fixed:** `returnUrl` parameter validated as local path to prevent open redirects
- **Affected files:** `Program.cs`, `LoginTotp.razor`

### Security: User ID Removed from TOTP Query String
- **Before:** `/login/totp?uid=<user-guid>` exposed internal user IDs in the URL
- **After:** Login flow uses opaque `totp-pending` tokens stored in `IMemoryCache`; user IDs never appear in URLs
- **Affected files:** `Program.cs`, `LoginTotp.razor`

### Security: Audio Files Now Require Authentication
- **Before:** `UseStaticFiles` served audio output and reference files to anyone who knew the filename
- **After:** Audio files served via `MapGet` endpoints with `.RequireAuthorization()` and path traversal validation
- **Affected files:** `Program.cs`

### Security: Setup Page Blocked After Completion
- **Before:** `SetupGuardMiddleware` always allowed `/setup` through; only a client-side Blazor redirect prevented access after setup
- **After:** Middleware returns server-side redirect to `/` when users already exist, preventing rogue admin creation
- **Affected files:** `SetupGuardMiddleware.cs`

### Security: Rate Limiting on Login Endpoint
- **Added:** Fixed-window rate limiter (10 requests/minute per IP) on `/api/auth/login`
- **Affected files:** `Program.cs`

### Security: Voice ID Path Traversal Prevention
- **Added:** Regex validation (`^[a-zA-Z0-9_-]+$`) on voice IDs before use as directory names
- **Affected files:** `VoiceLibrary.razor`

### Fix: Test Suite Compilation Restored
- **Before:** 12 compilation errors — `OrchestratorEventBus` parameter added to service constructors without updating tests; `HealthMonitorService` constructor changed from `ITtsClientService` to `IDockerClient`
- **After:** All 4 broken test files fixed; `HealthMonitorServiceTests` also corrected to call `CheckHealthAsync` 5 times (matching consecutive-failure threshold). Two pre-existing `ContainerConfigServiceTests` assertions fixed (volume bind mount path, `--half` flag moved from env var to Cmd).
- **Result:** 74/74 tests passing
- **Affected files:** `DockerOrchestratorServiceTests.cs`, `HealthMonitorServiceTests.cs`, `TtsClientServiceTests.cs`, `HealthMonitorHubTests.cs`, `ContainerConfigServiceTests.cs`, `VoiceLibraryServiceTests.cs`

### Fix: Process Handle Leaks in TtsJobProcessor
- **Before:** `Process.Start()` returned objects that were never disposed, leaking OS handles per TTS job
- **After:** All `Process` objects wrapped in `using` statements
- **Affected files:** `TtsJobProcessor.cs`

### Fix: Graceful Shutdown Blocked by Task.Delay
- **Before:** `Task.Delay(PollInterval, CancellationToken.None)` in poll loop ignored shutdown signal
- **After:** Uses `stoppingToken` for cancellation-aware delay
- **Affected files:** `TtsJobProcessor.cs`

### Fix: Stderr Pipe Deadlock Risk
- **Before:** `StandardError.ReadToEndAsync()` called only after process exit; if stderr buffer filled, process would hang indefinitely
- **After:** Stderr read started immediately as a `Task<string>` and passed through to the poll loop
- **Affected files:** `TtsJobProcessor.cs`

### Fix: Blazor Pages Migrated to IDbContextFactory
- **Before:** 5 pages injected scoped `AppDbContext`, which in Blazor Server lives for the entire circuit — causing stale tracked entities, memory growth, and concurrent access risk from event handlers
- **After:** All 5 pages use `IDbContextFactory<AppDbContext>` with short-lived `using var db` per operation
- **Affected files:** `Dashboard.razor`, `TtsPlayground.razor`, `GenerationHistory.razor`, `Logs.razor`, `Deploy.razor`, `Program.cs`

### Fix: Timer Disposal in Setup and Deploy Pages
- **Before:** `Setup.razor` (3 timers) and `Deploy.razor` (1 timer) created `Timer` objects without implementing `IAsyncDisposable`; navigating away left timers firing on a disposed circuit
- **After:** Both components implement `IAsyncDisposable` and dispose all timers
- **Affected files:** `Setup.razor`, `Deploy.razor`

### Fix: Process Disposal in SetupService
- **Before:** Background processes (`_modelDownloadProcess`, `_dockerPullProcess`) stored in singleton but never disposed
- **After:** Process disposed in `Exited` event handler
- **Affected files:** `SetupService.cs`

### Fix: Socket Exhaustion in VoiceLibraryService
- **Before:** `new HttpClient()` created per `SyncVoicesToContainerAsync` call, causing TIME_WAIT socket exhaustion
- **After:** Uses `IHttpClientFactory` for proper connection pooling
- **Affected files:** `VoiceLibraryService.cs`, `Program.cs`

### Fix: CancellationTokenSource Leaks in ProxyConfigProvider
- **Before:** Old CTS cancelled but never disposed on model swaps
- **After:** `oldCts.Dispose()` called after cancel
- **Affected files:** `ProxyConfigProvider.cs`

### Fix: Tags Not Saved When Adding Voice
- **Before:** Tags field collected in UI but never passed to `AddVoiceAsync` (method didn't accept the parameter)
- **After:** `AddVoiceAsync` accepts optional `tags` parameter; VoiceLibrary passes `_addTags` through
- **Affected files:** `VoiceLibrary.razor`, `VoiceLibraryService.cs`, `IVoiceLibraryService.cs`

### Fix: Missing Config Fallback in TtsClientService
- **Before:** `config["FishOrchestrator:DataRoot"]!` with null-forgiving operator crashed DI if config missing
- **After:** Falls back to default path like other services
- **Affected files:** `TtsClientService.cs`

### Improvement: SetupGuardMiddleware Caches DB Query
- **Before:** `db.Users.AnyAsync()` ran on every non-static request
- **After:** Result cached in `volatile bool _setupComplete` — once users exist, never queries again
- **Affected files:** `SetupGuardMiddleware.cs`

### Improvement: PostLoginRedirectMiddleware Reduced DB Queries
- **Added:** Static assets, API routes, SignalR hubs, and audio paths bypass the DB lookup
- **Affected files:** `PostLoginRedirectMiddleware.cs`

### Improvement: GPU Metrics Exception Handling
- **Before:** Unhandled exception in `CollectGpuMetricsAsync` could kill the entire `HealthMonitorService` (both health and GPU loops)
- **After:** Exceptions caught and logged; loop continues
- **Affected files:** `HealthMonitorService.cs`

### Improvement: Duplicated Truncate Helper Extracted
- **Before:** Identical `Truncate` method defined in 3 separate Razor components
- **After:** Shared `StringHelpers.Truncate` via `@using static` in `_Imports.razor`
- **Affected files:** `StringHelpers.cs` (new), `_Imports.razor`, `TtsPlayground.razor`, `GenerationHistory.razor`, `VoiceLibrary.razor`

### Improvement: DateTimeOffset Standardized Across Entities
- **Before:** `AppUser` used `DateTimeOffset`; all other entities (`ModelProfile`, `ReferenceVoice`, `GenerationLog`, `TtsJob`) used `DateTime`
- **After:** All entities, hub events, and service assignments use `DateTimeOffset` consistently
- **Note:** No migration needed — SQLite stores both types as TEXT; existing data is compatible
- **Affected files:** `ModelProfile.cs`, `ReferenceVoice.cs`, `GenerationLog.cs`, `TtsJob.cs`, `HubEvents.cs`, `GpuMetricsState.cs`, `DockerOrchestratorService.cs`, `VoiceLibraryService.cs`, `TtsJobProcessor.cs`, `ContainerLogService.cs`

---

## 2026-03-31

### Default Data Path Changed
- **Before:** `D:\DockerData\FishAudio` (and subdirectories Checkpoints, References, Output)
- **After:** `C:\MyFishAudioProj` (and subdirectories)
- **Reason:** Removed PII-adjacent path from the codebase before open-source release
- **Affected files:** `appsettings.json`, `Program.cs`, `TtsJobProcessor.cs`, `Deploy.razor`, `GenerationHistory.razor`, `Setup.razor`, `README.md`
- **Note:** The setup wizard now allows users to choose their own directories on first run, so the default path is just an initial suggestion

### Default Docker Image Tag Changed
- **Before:** `fishaudio/fish-speech:server-cuda`
- **After:** `fishaudio/fish-speech:server-cuda-v2.0.0-beta`
- **Reason:** The latest `server-cuda` tag ships with torchaudio 2.9 which removed `list_audio_backends()`, causing a startup crash ([fishaudio/fish-speech#1118](https://github.com/fishaudio/fish-speech/issues/1118)). The beta tag uses torchaudio 2.8 which still has the function.
- **Affected files:** `appsettings.json`, `README.md`

### TTS Generation Method Changed
- **Before:** Blazor app makes HTTP POST to container's `/v1/tts` endpoint, receives audio bytes in the response, writes file to disk
- **After:** App runs `docker exec curl` inside the container, which writes the output file directly to the mounted output volume
- **Reason:** The HTTP approach failed when the app restarted during generation (response lost, audio lost, job had to be re-processed). With `docker exec curl`, the curl process survives app restarts because it runs inside the container. On recovery, the app checks if the output file exists.
- **Affected files:** `TtsJobProcessor.cs`

### Health Check Behavior During TTS Generation
- **Before:** Health monitor always hit the container's HTTP `/v1/health` endpoint every 30 seconds
- **After:** When TTS jobs are queued or processing, health monitor checks Docker container running status instead of the HTTP endpoint
- **Reason:** The Fish Speech container doesn't respond to HTTP health checks while actively generating audio, causing false "Error" status on the dashboard
- **Affected files:** `HealthMonitorService.cs`

### GPU Metrics Refresh Rate
- **Before:** GPU metrics collected every 30 seconds (tied to health check interval)
- **After:** GPU metrics collected every 5 seconds on a separate loop
- **Reason:** 30-second updates felt stale in the navbar display
- **Affected files:** `HealthMonitorService.cs`, `NavMenu.razor`

### SignalR Hub Authorization and Event Bus
- **Before:** Blazor components created client-side `HubConnection` to the SignalR hub for real-time updates
- **After:** Components subscribe to an in-process `OrchestratorEventBus` singleton. The SignalR hub retains `[Authorize]` and is reserved for future external clients.
- **Reason:** Blazor Server components can't reliably forward auth cookies to client-side hub connections, causing 401 errors. Since components already run on the server, an in-process event bus is simpler and more reliable.
- **Affected files:** `OrchestratorEventBus.cs`, `OrchestratorHub.cs`, `Dashboard.razor`, `Logs.razor`, `TtsPlayground.razor`, `ContainerLogService.cs`

### Cookie Authentication Moved to API Endpoints
- **Before:** Blazor components called `SignInManager.SignInAsync()` and `SignOutAsync()` directly
- **After:** Authentication operations use `/api/auth/login` (POST), `/api/auth/signin` (GET), and `/api/auth/signout` (GET) endpoints
- **Reason:** Blazor Server can't set cookies after the response has started streaming over the SignalR circuit. API endpoints handle cookies via normal HTTP requests.
- **Affected files:** `Program.cs`, `Login.razor`, `LoginTotp.razor`, `Setup.razor`, `ManageAccount.razor`

### Fish Audio Attribution Added
- **NOTICE file** added to root with Fish Audio attribution as required by the Research License
- **"Built with Fish Audio"** displayed on setup wizard download pages and TTS Playground
- **Non-commercial research disclaimer** added to wizard and README
- **Reason:** Compliance with the Fish Audio Research License (Section II/III prominent display requirement)
- **Affected files:** `NOTICE`, `Setup.razor`, `TtsPlayground.razor`, `README.md`
