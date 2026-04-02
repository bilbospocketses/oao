# Codebase Audit Report — OpenAudioOrchestrator

**Date:** 2026-04-01
**Scope:** All source files in `src/OpenAudioOrchestrator.Web/`
**Baseline:** Post-security-audit commit 874c80d

---

## Critical Findings

### SEC-01: `/api/auth/signin` GET endpoint lacks rate limiting and authorization
**File:** `src/OpenAudioOrchestrator.Web/Endpoints/AuthEndpoints.cs`, lines 47-75
**Description:** The `/api/auth/signin` endpoint (used after TOTP verification) is a GET request that performs a state-changing operation (sign-in + security stamp regeneration). It has no rate limiting policy applied (unlike `/api/auth/login` which has `.RequireRateLimiting("auth")`). An attacker who obtains a TOTP completion token has 60 seconds to use it. More critically, being a GET request means it could be triggered by image tags or link prefetching.
**Recommended fix:** Change to POST, add `.RequireRateLimiting("auth")`, and require the token in the request body rather than a query parameter. At minimum, add rate limiting immediately.

### SEC-02: SignalR hub accepts arbitrary container IDs for log subscription
**File:** `src/OpenAudioOrchestrator.Web/Hubs/OrchestratorHub.cs`, lines 21-28
**Description:** The `SubscribeLogs` method validates that the `containerId` matches a known `ModelProfile.ContainerId`, but the `containerId` parameter is passed directly to `_logService.SubscribeAsync()`, which calls `_docker.Containers.GetContainerLogsAsync()`. If a container ID passes the database check but is later changed (race condition between check and use), or if the validation is bypassed in future refactoring, arbitrary container log access is possible. The containerId format is not validated (unlike TtsJobProcessor which validates hex format).
**Recommended fix:** Validate containerId format with a regex (e.g., `^[a-f0-9]{12,64}$`) before passing to Docker SDK. Consider accepting a `modelId` (integer) instead and looking up the container ID server-side.

### SEC-03: CSP allows `unsafe-inline` for scripts
**File:** `src/OpenAudioOrchestrator.Web/Program.cs`, line 183
**Description:** The Content-Security-Policy header includes `script-src 'self' 'unsafe-inline'`. While Blazor Server requires some inline scripting, `unsafe-inline` significantly weakens XSS protection. Any XSS vector (e.g., through improperly-encoded user content rendered in Razor) can execute arbitrary JavaScript.
**Recommended fix:** Replace `'unsafe-inline'` with nonce-based or hash-based CSP for scripts. Blazor Server's `_framework/blazor.web.js` can use a nonce. If not feasible immediately, document this as an accepted risk.

---

## Medium Findings

### BUG-01: SetupGuardMiddleware race condition on `_setupComplete` flag
**File:** `src/OpenAudioOrchestrator.Web/Middleware/SetupGuardMiddleware.cs`, lines 32-37
**Description:** The `_setupComplete` field is `volatile bool`, but the check-then-set pattern (`if (!_setupComplete) { ... if (await db.Users.AnyAsync()) _setupComplete = true; }`) is not atomic. Under concurrent requests during the transition from setup to normal operation, multiple requests could simultaneously query the database. While this is not a security issue (the flag only transitions from false to true), it causes unnecessary database queries during startup.
**Recommended fix:** Use `Interlocked.CompareExchange` or accept the minor inefficiency with a comment.

### BUG-02: TtsJobProcessor uses static `SemaphoreSlim` -- survives host restart in tests
**File:** `src/OpenAudioOrchestrator.Web/Services/TtsJobProcessor.cs`, line 40
**Description:** `_jobSignal` is `static readonly SemaphoreSlim`. In integration tests using `WebApplicationFactory`, the semaphore persists across test host rebuilds, which could cause test interference. In production, this is fine since there's one process, but it's a testing landmine.
**Recommended fix:** Make the semaphore instance-based (inject via DI as a singleton wrapper) or document the static lifetime constraint.

### BUG-03: Dashboard `RemoveModel` uses two separate DbContext instances creating potential inconsistency
**File:** `src/OpenAudioOrchestrator.Web/Components/Pages/Dashboard.razor`, lines 284-301
**Description:** `RemoveModel` calls `Orchestrator.RemoveModelAsync(model)` which uses the orchestrator's scoped `AppDbContext` to update the model, then creates a new `DbContext` via `DbFactory` to remove the entity. The `model` object passed to the orchestrator was loaded from a third context during `LoadModels`. This detached-entity-across-contexts pattern can cause `DbUpdateConcurrencyException` or duplicate operations.
**Recommended fix:** Load the entity fresh in a single context scope that handles both the orchestrator operation and the removal, or restructure `RemoveModelAsync` to also remove the `ModelProfile` entity.

### BUG-04: `ContainerLogService.UnsubscribeCallback` double-checks subscribers with potential TOCTOU issue
**File:** `src/OpenAudioOrchestrator.Web/Services/ContainerLogService.cs`, lines 127-146
**Description:** `UnsubscribeCallback` calls `HasSubscribers(containerId)` and `HasCallbackSubscribers(containerId)` without holding the stream's lock. Between the check and the `TryRemove`, another thread could add a subscriber, causing the stream to be cancelled prematurely.
**Recommended fix:** Perform the subscriber count check and stream removal atomically under the stream's lock.

### BUG-05: `GpuMetricsParser.CollectAsync` reads stdout before `WaitForExitAsync`
**File:** `src/OpenAudioOrchestrator.Web/Services/GpuMetricsParser.cs`, lines 36-41
**Description:** `ReadToEndAsync()` is called before `WaitForExitAsync()`, and crucially it is called with no cancellation token at all. The timeout CTS is only scoped to the subsequent `WaitForExitAsync` call. This means if nvidia-smi hangs writing to stdout, `ReadToEndAsync` will block indefinitely — the timeout never fires because the CTS-guarded `WaitForExitAsync` is never reached.
**Recommended fix:** Pass the cancellation token to `ReadToEndAsync` as well (e.g., `ReadToEndAsync(cts.Token)`), ensuring the read is also subject to the timeout. Alternatively, restructure to call `WaitForExitAsync(cts.Token)` first and read stdout only after the process exits.

### SEC-05: `VoiceLibraryService.AddVoiceAsync` does not validate `voiceId` for path traversal
**File:** `src/OpenAudioOrchestrator.Web/Services/VoiceLibraryService.cs`, lines 22-45
**Description:** The `voiceId` is used to construct a directory path: `Path.Combine(_referencesPath, voiceId)`. While the Razor component `VoiceLibrary.razor` validates the voiceId with `^[a-zA-Z0-9_-]+$` (line 199), the service itself does not validate. Any caller bypassing the UI (e.g., future API endpoint, test code) could inject path traversal characters like `../`.
**Recommended fix:** Add path traversal validation in the service method: verify that `Path.GetFullPath(voiceDir)` starts with `_referencesPath`.

### SEC-06: `SetupDownloadService.IsDockerImagePresentAsync` interpolates imageTag into shell command
**File:** `src/OpenAudioOrchestrator.Web/Services/SetupDownloadService.cs`, line 106
**Description:** While `ValidateImageTag()` is called to check the format, the imageTag is interpolated into `$"images -q {imageTag}"` as a process argument. The `ValidateImageTag` regex `^[a-zA-Z0-9][a-zA-Z0-9._/:-]*$` is reasonably restrictive, but process argument injection could still be possible on some shells if the value contains spaces or other characters the regex doesn't cover. The regex does allow `/` and `:` which are needed for image tags but could be problematic in other contexts.
**Recommended fix:** The current validation is adequate for Docker image tags, but consider using an argument array instead of string interpolation for the process arguments to eliminate any injection risk entirely.

### BUG-06: `HealthMonitorService` does not handle `OperationCanceledException` from `Task.Delay`
**File:** `src/OpenAudioOrchestrator.Web/Services/HealthMonitorService.cs`, lines 52-58
**Description:** In `RunHealthChecksAsync`, `Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken)` will throw `OperationCanceledException` when the host shuts down. The exception propagates through `ExecuteAsync` to `Task.WhenAll`, which will throw an `AggregateException`. `BackgroundService` handles `OperationCanceledException` from `ExecuteAsync` gracefully, but `AggregateException` wrapping it may not be handled the same way in all .NET versions.
**Recommended fix:** Wrap the `Task.Delay` calls in try-catch for `OperationCanceledException` and return cleanly, similar to how `TtsJobProcessor` handles it.

### SEC-07: Cookie SecurePolicy is `SameAsRequest` instead of `Always`
**File:** `src/OpenAudioOrchestrator.Web/Program.cs`, line 116
**Description:** `CookieSecurePolicy.SameAsRequest` means cookies will be sent without the `Secure` flag over HTTP. Since the app supports HTTPS via Let's Encrypt, the auth cookie should always be marked Secure to prevent interception over HTTP (e.g., before HTTPS redirect completes).
**Recommended fix:** Use `CookieSecurePolicy.Always` when a domain is configured (HTTPS mode), or at minimum document the tradeoff for localhost development.

### BUG-07: `Login.razor` form lacks antiforgery token
**File:** `src/OpenAudioOrchestrator.Web/Components/Pages/Login.razor`, lines 16-26
**Description:** The login form uses a plain HTML `<form method="post">` without Blazor's `EditForm` component or an explicit antiforgery token. Since `UseAntiforgery()` is in the middleware pipeline, POST to `/api/auth/login` should be protected, but the form does not include an `__RequestVerificationToken` hidden field. The antiforgery middleware may reject the POST, or if the endpoint was registered before antiforgery middleware, it may not be validated.
**Recommended fix:** Verify the login POST is actually validated by antiforgery. If not, either add `[ValidateAntiForgeryToken]` to the endpoint or use an `EditForm`/include the antiforgery token hidden field. Note: The auth endpoint is mapped after `UseAntiforgery()` (line 205 vs 199), so it should be covered, but the form still needs to send the token.

### BUG-08: `ManageAccount.razor` signout form may lack antiforgery token
**File:** `src/OpenAudioOrchestrator.Web/Components/Pages/Account/ManageAccount.razor`, lines 49-51
**Description:** Same issue as BUG-07: the signout form is a plain HTML `<form method="post">` without an antiforgery token.
**Recommended fix:** Add the antiforgery token to the form or use `EditForm`.

### BUG-09: `Program.cs` silently falls back to using encrypted DatabaseKey as plaintext password
**File:** `src/OpenAudioOrchestrator.Web/Program.cs`, lines 87-89
**Description:** When Data Protection cannot decrypt the `DatabaseKey` configuration value, the code silently appends the raw (encrypted) value as a plaintext password: `connectionString += $";Password={encryptedDbKey}"`. If DP keys are lost or rotated, the application will silently attempt to connect with a garbled password, making the failure hard to diagnose. Worse, the encrypted blob is logged or exposed at the connection-string level, hinting at the key-management approach.
**Recommended fix:** Log an error-level message on decrypt failure and either fail fast (throw, preventing startup) or clearly surface the fallback so operators are not blindsided by a silent misconfiguration.

### SEC-04: Audio endpoints mapped before `UseAntiforgery()` — future-proofing gap
**File:** `src/OpenAudioOrchestrator.Web/Program.cs`, lines 198-199
**Description:** `app.MapAudioEndpoints()` is called on line 198, but `app.UseAntiforgery()` is called on line 199 — after the audio endpoints are mapped. Because all audio endpoints are currently GET-only (read operations), there is no actual CSRF exposure today. The concern is forward-looking: if any POST endpoints are added to `MapAudioEndpoints` in the future, they would lack CSRF protection by default.
**Recommended fix:** Move `app.UseAntiforgery()` before `app.MapAudioEndpoints()`, or add a comment explaining the ordering is intentional and safe only for GET-only endpoints.

### SEC-08: `DockerOrchestratorService` passes `ContainerId` to Docker API without format validation
**File:** `src/OpenAudioOrchestrator.Web/Services/DockerOrchestratorService.cs`
**Description:** `DockerOrchestratorService` passes `profile.ContainerId` directly to `StopContainerAsync` and `RemoveContainerAsync` without validating that it conforms to the expected Docker container ID format (`^[a-f0-9]{12,64}$`). This contrasts with `TtsJobProcessor`, which validates container IDs with that regex before use. The `ContainerId` originates from the database (seeded from Docker API responses), so the practical risk is low. However, it is a defense-in-depth gap: a corrupted or injected database record could cause unintended Docker operations.
**Recommended fix:** Add container ID format validation in `DockerOrchestratorService` (matching the regex already used in `TtsJobProcessor`) before passing IDs to Docker SDK calls.

---

## Low Findings

### QUAL-01: Duplicate JSON serialization options in `TtsClientService.BuildRequestJson`
**File:** `src/OpenAudioOrchestrator.Web/Services/TtsClientService.cs`, lines 118-139
**Description:** `JsonSerializerOptions` is allocated every time `BuildRequestJson` is called. Since this is a hot path (called per TTS job), the options should be cached as a static field.
**Recommended fix:** Extract `JsonSerializerOptions` to a `private static readonly` field.

### QUAL-02: `StringHelpers.Truncate` does not handle null
**File:** `src/OpenAudioOrchestrator.Web/StringHelpers.cs`, line 5
**Description:** `Truncate` will throw `NullReferenceException` if `text` is null. Multiple Razor components call it with potentially-null values (e.g., `Truncate(log.InputText, 60)` -- `InputText` is `required` but could theoretically be null for deserialized entities).
**Recommended fix:** Add a null check: `if (text is null) return string.Empty;`

### QUAL-03: `App.razor` loads Bootstrap from CDN without SRI hash
**File:** `src/OpenAudioOrchestrator.Web/Components/App.razor`, line 10
**Description:** Bootstrap CSS is loaded from `cdn.jsdelivr.net` without a `integrity` attribute (Subresource Integrity). A CDN compromise could inject malicious CSS/scripts.
**Recommended fix:** Add the `integrity` and `crossorigin` attributes, or bundle Bootstrap locally.

### QUAL-04: `NavMenu.razor` calls `InvokeAsync(StateHasChanged)` without awaiting
**File:** `src/OpenAudioOrchestrator.Web/Components/Layout/NavMenu.razor`, line 52
**Description:** `InvokeAsync(StateHasChanged)` returns a `Task` that is not awaited. If the component is disposed before the task completes, an `ObjectDisposedException` may be thrown. The `WeakEvent` bus catches `ObjectDisposedException`, but this callback bypasses that since it goes through `GpuState.OnChange`.
**Recommended fix:** Change the event handler to async and await the call, or wrap in a try-catch.

### QUAL-05: `MainLayout.razor` spawns fire-and-forget `Task.Run` for toast auto-dismiss
**File:** `src/OpenAudioOrchestrator.Web/Components/Layout/MainLayout.razor`, lines 50-58
**Description:** `Task.Run(async () => { await Task.Delay(5000); ... })` creates a fire-and-forget task. If the component is disposed during the delay, the subsequent `InvokeAsync(StateHasChanged)` will throw. While the `WeakEvent` catches `ObjectDisposedException`, this code path does not.
**Recommended fix:** Use a `CancellationTokenSource` that is cancelled in `Dispose()`, or use a `Timer` with proper disposal.

### QUAL-06: Dead field `_deployDownloadTimer` in `Deploy.razor` not checked for null before Dispose
**File:** `src/OpenAudioOrchestrator.Web/Components/Pages/Deploy.razor`, lines 112, 230-234
**Description:** `_deployDownloadTimer` is only created in `StartModelDownload()` but `DisposeAsync()` always calls `_deployDownloadTimer?.Dispose()`. This is safe due to null-conditional, but the timer callback captures `this` and calls `InvokeAsync`/`StateHasChanged` after disposal is possible.
**Recommended fix:** Add a disposal guard (CancellationTokenSource) as done in `Setup.razor`.

### QUAL-07: `GenerationHistory.razor` deletion does not validate file path
**File:** `src/OpenAudioOrchestrator.Web/Components/Pages/GenerationHistory.razor`, lines 176-194
**Description:** `DeleteEntry` constructs a file path using `log.OutputFileName` from the database and calls `File.Delete`. While the OutputFileName is generated server-side via `TtsClientService.GenerateOutputFileName` (which produces safe names like `gen_20260401_...`), there is no path traversal check. If the database were compromised or a future code path allowed user-controlled filenames, this could delete arbitrary files.
**Recommended fix:** Add a `Path.GetFullPath` check similar to `AudioEndpoints.cs`.

### QUAL-08: `Setup.razor` calls `UserManager.Users.Any()` synchronously (EF Core anti-pattern)
**File:** `src/OpenAudioOrchestrator.Web/Components/Pages/Setup.razor`, line 411
**Description:** `UserManager.Users.Any()` performs a synchronous database query on the Blazor render thread. This should be `await UserManager.Users.AnyAsync()`.
**Recommended fix:** Change to `await UserManager.Users.AnyAsync()` and make the method `async Task`.

### QUAL-09: `TtsPlayground.razor` `CancelJob` does not invoke `TtsJobProcessor.CancelJobAsync`
**File:** `src/OpenAudioOrchestrator.Web/Components/Pages/TtsPlayground.razor`, lines 298-304
**Description:** When a user cancels a queued job, `CancelJob` directly removes the TtsJob from the database. If the job has transitioned to "Processing" between the UI render and the cancel action, the processor will continue running curl inside the container even though the job record is deleted. The processor's polling loop checks for job status and handles the "not found" case, but only on the next poll cycle (up to 5 seconds later).
**Recommended fix:** For queued jobs, direct deletion is fine. But add a status check: if the job is now Processing, call `TtsJobProcessor.CancelJobAsync(job.Id)` instead.

### QUAL-10: `HttpClient` timeout set to `InfiniteTimeSpan` for TtsClientService
**File:** `src/OpenAudioOrchestrator.Web/Program.cs`, line 144
**Description:** The `HttpClient` for `TtsClientService` has `Timeout = Timeout.InfiniteTimeSpan`. While TTS generation can be slow, an infinite timeout means a hung connection will never be cleaned up. The `TtsJobProcessor` uses docker exec with a 2-hour timeout, but the direct HTTP path (`TtsClientService.GenerateAsync`) has no timeout at all.
**Recommended fix:** Set a generous but finite timeout (e.g., 2 hours to match `TtsJobProcessor.JobTimeout`).

### QUAL-11: `ContainerConfigService.BuildCreateParams` does not validate `profile.Name` for Docker naming
**File:** `src/OpenAudioOrchestrator.Web/Services/ContainerConfigService.cs`, line 57
**Description:** Container names are set to `$"oao-{profile.Name}"`. Docker container names must match `[a-zA-Z0-9][a-zA-Z0-9_.-]`. If `profile.Name` contains spaces or special characters, container creation will fail with an unhelpful Docker API error.
**Recommended fix:** Sanitize the container name or validate `profile.Name` at creation time (the Deploy form allows 100 chars but doesn't restrict to Docker-safe characters).

### QUAL-12: `ProxyConfigProvider` CancellationTokenSource disposal race
**File:** `src/OpenAudioOrchestrator.Web/Proxy/ProxyConfigProvider.cs`, lines 18-31
**Description:** `UpdateDestination` and `ClearDestination` read `_cts`, create a new one, swap it, then cancel and dispose the old one. If two calls race, `oldCts` could be the same object for both, leading to double-dispose. While `CancellationTokenSource.Dispose` is documented as safe to call multiple times, the sequence `Cancel()` then `Dispose()` on an already-disposed CTS will throw `ObjectDisposedException`.
**Recommended fix:** Add a lock around the swap-and-cancel sequence, or use `Interlocked.Exchange` and handle the disposal safely.

### QUAL-13: `TtsClientService.GenerateAsync` appears to be dead code
**File:** `src/OpenAudioOrchestrator.Web/Services/TtsClientService.cs`
**Description:** No Razor page or endpoint appears to call `TtsClientService.GenerateAsync`. The TTS Playground submits jobs via `TtsJobProcessor`, which invokes TTS through docker exec rather than through `TtsClientService`. Only `GetHealthAsync` is actively used from `TtsClientService`. The `GenerateAsync` method is a full code path — including the infinite-timeout `HttpClient` noted in QUAL-10 — that may never execute in practice. Its presence can mislead maintainers into thinking it is an active code path.
**Recommended fix:** Confirm whether `GenerateAsync` is intentionally kept for future use or emergency fallback. If dead, remove it to reduce maintenance surface. If kept, mark it with a comment and address the infinite-timeout issue (QUAL-10).

---

## Summary

| Severity | Count |
|----------|-------|
| Critical | 3     |
| Medium   | 10    |
| Low      | 13    |

**Key themes:**
- The prior security audit (874c80d) addressed most obvious vulnerabilities well -- path traversal checks, container ID validation, rate limiting, session fixation defense, and input validation are all present.
- The remaining critical issues center on the GET-based signin endpoint (SEC-01), incomplete CSP (SEC-03), and the container ID validation gap in the SignalR hub (SEC-02).
- Medium issues are primarily race conditions and cross-context EF Core usage in Blazor components.
- Low issues are code quality improvements (null safety, disposal patterns, caching).
