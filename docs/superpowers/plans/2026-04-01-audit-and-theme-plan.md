# Audit & Theme Enhancement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a categorized audit report (security, bugs, code quality), implement selected fixes, and add a per-user light/dark theme system with medium-grey dark theme (no blue) and white light theme.

**Architecture:** Two-phase approach — audit report first (Tasks 1-2), then theme system (Tasks 3-8). Theme uses CSS custom properties on `[data-theme]` attribute, per-user DB persistence via new `AppUser.ThemePreference` column, and a nav bar toggle that switches instantly via JS interop while persisting asynchronously.

**Tech Stack:** .NET 9, Blazor Server, EF Core (SQLite), ASP.NET Identity, CSS custom properties, JS interop

---

### Task 1: Codebase Audit — Produce Categorized Report

**Files:**
- Create: `docs/audit-report.md`

This task is a research-and-report task. Read every source file and produce findings.

- [ ] **Step 1: Audit security**

Review all endpoints, middleware, auth flows, SignalR hub, Docker interactions, and file serving for vulnerabilities. Check for:
- Auth bypass paths (middleware exempt lists, setup guard edge cases)
- Input validation gaps (form inputs, query parameters, SignalR hub methods)
- CSRF coverage (all state-changing endpoints use POST)
- Rate limiting coverage (are non-auth endpoints that should be limited, not?)
- Secrets exposure (config values, error messages leaking internals)
- Docker exec safety (command injection via user-controlled data in exec commands)
- Path traversal (audio endpoints, voice library, setup paths)
- Open redirect (auth endpoints returnUrl handling)
- Session management (cookie settings, token lifetimes, fixation)
- CSP completeness (are all inline styles/scripts accounted for?)
- SignalR authorization (can users access other users' data?)

- [ ] **Step 2: Audit bugs and reliability**

Review all services, background processors, and components for:
- Race conditions (concurrent access to shared state, DB contexts across threads)
- Resource leaks (streams, HTTP clients, Docker connections not disposed)
- Null reference risks (nullable fields accessed without checks)
- Error recovery gaps (what happens when Docker is down, DB is locked, disk is full?)
- TTS job processor edge cases (simultaneous cancel and complete, recovery after partial writes)
- Health monitor edge cases (status flapping, metric collection failures)
- Event bus edge cases (subscriber exceptions, rapid fire events)
- EF Core misuse (tracking issues, context lifetime, concurrent access)
- SignalR lifecycle issues (reconnection, missed events, stale subscriptions)
- Timer/background service shutdown (clean cancellation)

- [ ] **Step 3: Audit code quality and efficiency**

Review for:
- Dead code (unused methods, unreachable branches, vestigial services)
- Duplication (repeated patterns that should be consolidated)
- Naming inconsistencies
- Unnecessary allocations or inefficient patterns
- Error handling inconsistencies (some methods swallow exceptions, others don't)
- Logging gaps (important operations without log entries)
- Configuration validation (are invalid configs caught early or do they cause runtime failures?)
- Missing disposal patterns (IDisposable/IAsyncDisposable)

- [ ] **Step 4: Write the audit report**

Create `docs/audit-report.md` with this structure:

```markdown
# FishAudioOrchestrator Audit Report
**Date:** 2026-04-01

## Critical Findings
[Each finding: description, file:line, recommended fix]

## Medium Findings
[Each finding: description, file:line, recommended fix]

## Low Findings
[Each finding: description, file:line, recommended fix]
```

- [ ] **Step 5: Commit**

```bash
git add docs/audit-report.md
git commit -m "docs: add codebase audit report with categorized findings"
```

---

### Task 2: User Triages Audit Report

**Files:**
- Read: `docs/audit-report.md`

- [ ] **Step 1: Present findings summary to user**

Show the user a concise summary of findings by category count (e.g., "3 Critical, 7 Medium, 12 Low") and ask which to implement. Wait for user response before proceeding.

- [ ] **Step 2: Implement selected fixes**

For each selected finding, fix it in the relevant file. Group related fixes into logical commits. Run tests after each group of fixes:

```bash
cd <repo>
dotnet test tests/FishAudioOrchestrator.Tests/
```

- [ ] **Step 3: Commit fixes**

```bash
git add -A
git commit -m "fix: implement audit findings [describe which categories]"
```

---

### Task 3: Add ThemePreference to AppUser Entity

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Data/Entities/AppUser.cs`
- Test: `tests/FishAudioOrchestrator.Tests/Data/AppDbContextTests.cs`

- [ ] **Step 1: Write the failing test**

Add a test to `tests/FishAudioOrchestrator.Tests/Data/AppDbContextTests.cs`:

```csharp
[Fact]
public async Task AppUser_ThemePreference_DefaultsToDark()
{
    var user = new AppUser
    {
        UserName = "themetest",
        DisplayName = "Theme Test"
    };
    _context.Users.Add(user);
    await _context.SaveChangesAsync();

    var loaded = await _context.Users.OfType<AppUser>().FirstAsync(u => u.UserName == "themetest");
    Assert.Equal("dark", loaded.ThemePreference);
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd <repo>
dotnet test tests/FishAudioOrchestrator.Tests/ --filter "AppUser_ThemePreference_DefaultsToDark"
```

Expected: FAIL — `ThemePreference` property does not exist.

- [ ] **Step 3: Add ThemePreference property to AppUser**

In `src/FishAudioOrchestrator.Web/Data/Entities/AppUser.cs`, add after line 9 (`MustSetupTotp`):

```csharp
public string ThemePreference { get; set; } = "dark";
```

The full file becomes:

```csharp
using Microsoft.AspNetCore.Identity;

namespace FishAudioOrchestrator.Web.Data.Entities;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
    public bool MustSetupTotp { get; set; }
    public string ThemePreference { get; set; } = "dark";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test tests/FishAudioOrchestrator.Tests/ --filter "AppUser_ThemePreference_DefaultsToDark"
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Data/Entities/AppUser.cs tests/FishAudioOrchestrator.Tests/Data/AppDbContextTests.cs
git commit -m "feat: add ThemePreference property to AppUser entity"
```

---

### Task 4: Add EF Core Migration for ThemePreference

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Data/Migrations/[timestamp]_AddThemePreference.cs` (auto-generated)

- [ ] **Step 1: Generate the migration**

```bash
cd <repo>/src/FishAudioOrchestrator.Web
dotnet ef migrations add AddThemePreference
```

- [ ] **Step 2: Verify the migration was generated**

Check that the migration file exists and contains an `AddColumn` for `ThemePreference` on the `AspNetUsers` table with a default value of `"dark"`.

- [ ] **Step 3: Commit**

```bash
cd <repo>
git add src/FishAudioOrchestrator.Web/Data/Migrations/
git commit -m "feat: add EF Core migration for ThemePreference column"
```

---

### Task 5: Add Theme API Endpoint

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Endpoints/AuthEndpoints.cs`
- Create: `tests/FishAudioOrchestrator.Tests/Integration/ThemeEndpointTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/FishAudioOrchestrator.Tests/Integration/ThemeEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;

namespace FishAudioOrchestrator.Tests.Integration;

public class ThemeEndpointTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public ThemeEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;
    public async Task InitializeAsync() => await _factory.SeedTestDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SetTheme_Authenticated_UpdatesPreference()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/auth/theme", new { theme = "light" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetTheme_Unauthenticated_Returns401()
    {
        var client = _factory.CreateNonRedirectClient();
        var response = await client.PostAsJsonAsync("/api/auth/theme", new { theme = "light" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SetTheme_InvalidTheme_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/auth/theme", new { theme = "neon" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTheme_Authenticated_ReturnsPreference()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/auth/theme");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ThemeResponse>();
        Assert.NotNull(body);
        Assert.Contains(body!.Theme, new[] { "dark", "light" });
    }

    private record ThemeResponse(string Theme);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/FishAudioOrchestrator.Tests/ --filter "ThemeEndpointTests"
```

Expected: FAIL — endpoints don't exist.

- [ ] **Step 3: Add theme endpoints to AuthEndpoints.cs**

In `src/FishAudioOrchestrator.Web/Endpoints/AuthEndpoints.cs`, add before the closing `}` of `MapAuthEndpoints` (before the final `}`), after the signout endpoint:

```csharp
app.MapPost("/api/auth/theme", async (
    HttpContext httpContext,
    UserManager<AppUser> userManager) =>
{
    var userId = httpContext.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
    if (userId is null) return Results.Unauthorized();

    var user = await userManager.FindByIdAsync(userId);
    if (user is null) return Results.Unauthorized();

    var form = await httpContext.Request.ReadFromJsonAsync<ThemeRequest>();
    if (form?.Theme is not ("dark" or "light"))
        return Results.BadRequest("Theme must be 'dark' or 'light'.");

    user.ThemePreference = form.Theme;
    await userManager.UpdateAsync(user);
    return Results.Ok();
}).RequireAuthorization();

app.MapGet("/api/auth/theme", async (
    HttpContext httpContext,
    UserManager<AppUser> userManager) =>
{
    var userId = httpContext.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
    if (userId is null) return Results.Unauthorized();

    var user = await userManager.FindByIdAsync(userId);
    if (user is null) return Results.Unauthorized();

    return Results.Ok(new { theme = user.ThemePreference });
}).RequireAuthorization();
```

Add the `ThemeRequest` record at the bottom of the file (inside the namespace, outside the class):

```csharp
public record ThemeRequest(string Theme);
```

Also add the required using at the top of the file:

```csharp
using System.Security.Claims;
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/FishAudioOrchestrator.Tests/ --filter "ThemeEndpointTests"
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Endpoints/AuthEndpoints.cs tests/FishAudioOrchestrator.Tests/Integration/ThemeEndpointTests.cs
git commit -m "feat: add GET/POST /api/auth/theme endpoints for theme persistence"
```

---

### Task 6: Convert CSS to Custom Properties (Theme Variables)

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/wwwroot/css/app.css`

- [ ] **Step 1: Replace app.css with theme variable system**

Rewrite `src/FishAudioOrchestrator.Web/wwwroot/css/app.css`. The complete replacement:

```css
/* =====================================================================
   Fish Audio Orchestrator — theme system
   ===================================================================== */

/* --- Theme Variables ------------------------------------------------- */

[data-theme="dark"] {
    --bg-body: #2a2a2a;
    --bg-surface: #333333;
    --bg-surface-alt: #3a3a3a;
    --bg-input: #2e2e2e;
    --bg-input-disabled: #2a2a2a;
    --bg-navbar: #252525;
    --bg-scrollbar-track: #2e2e2e;
    --text-primary: #e0e0e0;
    --text-secondary: #aaaaaa;
    --text-muted: #888888;
    --text-heading: #e0e0e0;
    --text-link: #a0a0a0;
    --text-link-hover: #cccccc;
    --border-color: #444444;
    --border-light: #555555;
    --focus-shadow: rgba(170, 170, 170, 0.25);
    --table-hover-bg: rgba(255, 255, 255, 0.05);
    --table-striped-bg: rgba(255, 255, 255, 0.03);
    --active-row-bg: rgba(16, 185, 129, 0.1);
    --scrollbar-thumb: #555555;
    --placeholder-color: #6b7280;
    --form-text-color: #9ca3af;
    --checkbox-checked: #10b981;
    --alert-info-bg: rgba(160, 160, 160, 0.12);
    --alert-info-border: rgba(160, 160, 160, 0.3);
    --alert-info-text: #cccccc;
    --alert-success-bg: rgba(16, 185, 129, 0.15);
    --alert-success-border: rgba(16, 185, 129, 0.4);
    --alert-success-text: #6ee7b7;
    --alert-warning-bg: rgba(245, 158, 11, 0.15);
    --alert-warning-border: rgba(245, 158, 11, 0.4);
    --alert-warning-text: #fcd34d;
    --alert-danger-bg: rgba(239, 68, 68, 0.15);
    --alert-danger-border: rgba(239, 68, 68, 0.4);
    --alert-danger-text: #fca5a5;
    --status-created: #aaaaaa;
    --modal-bg: #333333;
    --progress-bg: #3a3a3a;
}

[data-theme="light"] {
    --bg-body: #ffffff;
    --bg-surface: #f5f5f5;
    --bg-surface-alt: #eeeeee;
    --bg-input: #ffffff;
    --bg-input-disabled: #f0f0f0;
    --bg-navbar: #f8f8f8;
    --bg-scrollbar-track: #f0f0f0;
    --text-primary: #1a1a1a;
    --text-secondary: #555555;
    --text-muted: #888888;
    --text-heading: #1a1a1a;
    --text-link: #555555;
    --text-link-hover: #333333;
    --border-color: #dddddd;
    --border-light: #cccccc;
    --focus-shadow: rgba(100, 100, 100, 0.2);
    --table-hover-bg: rgba(0, 0, 0, 0.04);
    --table-striped-bg: rgba(0, 0, 0, 0.02);
    --active-row-bg: rgba(16, 185, 129, 0.08);
    --scrollbar-thumb: #cccccc;
    --placeholder-color: #aaaaaa;
    --form-text-color: #777777;
    --checkbox-checked: #10b981;
    --alert-info-bg: rgba(100, 100, 100, 0.08);
    --alert-info-border: rgba(100, 100, 100, 0.2);
    --alert-info-text: #555555;
    --alert-success-bg: rgba(16, 185, 129, 0.1);
    --alert-success-border: rgba(16, 185, 129, 0.3);
    --alert-success-text: #047857;
    --alert-warning-bg: rgba(245, 158, 11, 0.1);
    --alert-warning-border: rgba(245, 158, 11, 0.3);
    --alert-warning-text: #92400e;
    --alert-danger-bg: rgba(239, 68, 68, 0.1);
    --alert-danger-border: rgba(239, 68, 68, 0.3);
    --alert-danger-text: #b91c1c;
    --status-created: #888888;
    --modal-bg: #ffffff;
    --progress-bg: #e5e5e5;
}

/* --- Base ------------------------------------------------------------ */

body {
    background-color: var(--bg-body);
    color: var(--text-primary);
}

h1:focus {
    outline: none;
}

a {
    color: var(--text-link);
}

a:hover {
    color: var(--text-link-hover);
}

/* --- Navbar ---------------------------------------------------------- */

.navbar {
    background-color: var(--bg-navbar) !important;
    border-bottom: 1px solid var(--border-color);
}

.navbar-brand,
.navbar .nav-link {
    color: var(--text-primary) !important;
}

.navbar .nav-link:hover,
.navbar .nav-link.active {
    color: var(--text-primary) !important;
    opacity: 0.85;
}

/* --- Tables ---------------------------------------------------------- */

.table {
    color: var(--text-primary);
    --bs-table-bg: transparent;
    --bs-table-color: var(--text-primary);
    --bs-table-striped-bg: var(--table-striped-bg);
    --bs-table-hover-bg: var(--table-hover-bg);
    --bs-table-border-color: var(--border-color);
}

.table td,
.table td small,
.table td code {
    color: var(--text-primary);
}

.table th {
    color: var(--text-secondary);
    font-weight: 600;
}

.table-hover tbody tr:hover {
    background-color: var(--table-hover-bg);
}

.text-muted {
    color: var(--text-muted) !important;
}

.table-dark {
    --bs-table-bg: var(--bg-surface);
    --bs-table-striped-bg: var(--table-striped-bg);
    --bs-table-hover-bg: var(--table-hover-bg);
    --bs-table-border-color: var(--border-color);
    --bs-table-color: var(--text-primary);
}

/* --- Cards ----------------------------------------------------------- */

.card {
    background-color: var(--bg-surface);
    border-color: var(--border-color);
}

.card .text-muted,
.card .form-text {
    color: var(--text-secondary) !important;
}

.card .card-title,
.card .form-label {
    color: var(--text-heading);
}

.card-header {
    background-color: var(--bg-surface-alt);
    border-bottom-color: var(--border-color);
    color: var(--text-heading);
}

.card-footer {
    background-color: var(--bg-surface-alt);
    border-top-color: var(--border-color);
}

/* --- Forms ----------------------------------------------------------- */

.form-control,
.form-select {
    background-color: var(--bg-input);
    border-color: var(--border-color);
    color: var(--text-primary);
}

.form-control:focus,
.form-select:focus {
    background-color: var(--bg-input);
    border-color: var(--border-light);
    color: var(--text-primary);
    box-shadow: 0 0 0 0.2rem var(--focus-shadow);
}

.form-control:disabled,
.form-select:disabled {
    background-color: var(--bg-input-disabled);
    border-color: var(--border-color);
    color: var(--text-muted);
    opacity: 1;
}

.form-control::placeholder {
    color: var(--placeholder-color);
}

.form-check-input:checked {
    background-color: var(--checkbox-checked);
    border-color: var(--checkbox-checked);
}

.form-text {
    color: var(--form-text-color);
}

.form-floating > .form-control-plaintext::placeholder,
.form-floating > .form-control::placeholder {
    color: var(--bs-secondary-color);
    text-align: end;
}

.form-floating > .form-control-plaintext:focus::placeholder,
.form-floating > .form-control:focus::placeholder {
    text-align: start;
}

.darker-border-checkbox.form-check-input {
    border-color: #929292;
}

.form-label {
    color: var(--text-primary);
}

/* --- Blazor validation ----------------------------------------------- */

.valid.modified:not([type=checkbox]) {
    outline: 1px solid #26b050;
}

.invalid {
    outline: 1px solid #e50000;
}

.validation-message {
    color: #e50000;
}

.blazor-error-boundary {
    background: url(data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iNTYiIGhlaWdodD0iNDkiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgeG1sbnM6eGxpbms9Imh0dHA6Ly93d3cudzMub3JnLzE5OTkveGxpbmsiIG92ZXJmbG93PSJoaWRkZW4iPjxkZWZzPjxjbGlwUGF0aCBpZD0iY2xpcDAiPjxyZWN0IHg9IjIzNSIgeT0iNTEiIHdpZHRoPSI1NiIgaGVpZ2h0PSI0OSIvPjwvY2xpcFBhdGg+PC9kZWZzPjxnIGNsaXAtcGF0aD0idXJsKCNjbGlwMCkiIHRyYW5zZm9ybT0idHJhbnNsYXRlKC0yMzUgLTUxKSI+PHBhdGggZD0iTTI2My41MDYgNTFDMjY0LjcxNyA1MSAyNjUuODEzIDUxLjQ4MzcgMjY2LjYwNiA1Mi4yNjU4TDI2Ny4wNTIgNTIuNzk4NyAyNjcuNTM5IDUzLjYyODMgMjkwLjE4NSA5Mi4xODMxIDI5MC41NDUgOTIuNzk1IDI5MC42NTYgOTIuOTk2QzI5MC44NzcgOTMuNTEzIDI5MSA5NC4wODE1IDI5MSA5NC42NzgyIDI5MSA5Ny4wNjUxIDI4OS4wMzggOTkgMjg2LjYxNyA5OUwyNDAuMzgzIDk5QzIzNy45NjMgOTkgMjM2IDk3LjA2NTEgMjM2IDk0LjY3ODIgMjM2IDk0LjM3OTkgMjM2LjAzMSA5NC4wODg2IDIzNi4wODkgOTMuODA3MkwyMzYuMzM4IDkzLjAxNjIgMjM2Ljg1OCA5Mi4xMzE0IDI1OS40NzMgNTMuNjI5NCAyNTkuOTYxIDUyLjc5ODUgMjYwLjQwNyA1Mi4yNjU4QzI2MS4yIDUxLjQ4MzcgMjYyLjI5NiA1MSAyNjMuNTA2IDUxWk0yNjMuNTg2IDY2LjAxODNDMjYwLjczNyA2Ni4wMTgzIDI1OS4zMTMgNjcuMTI0NSAyNTkuMzEzIDY5LjMzNyAyNTkuMzEzIDY5LjYxMDIgMjU5LjMzMiA2OS44NjA4IDI1OS4zNzEgNzAuMDg4N0wyNjEuNzk1IDg0LjAxNjEgMjY1LjM4IDg0LjAxNjEgMjY3LjgyMSA2OS43NDc1QzI2Ny44NiA2OS43MzA5IDI2Ny44NzkgNjkuNTg3NyAyNjcuODc5IDY5LjMxNzkgMjY3Ljg3OSA2Ny4xMTgyIDI2Ni40NDggNjYuMDE4MyAyNjMuNTg2IDY2LjAxODNaTTI2My41NzYgODYuMDU0N0MyNjEuMDQ5IDg2LjA1NDcgMjU5Ljc4NiA4Ny4zMDA1IDI1OS43ODYgODkuNzkyMSAyNTkuNzg2IDkyLjI4MzcgMjYxLjA0OSA5My41Mjk1IDI2My41NzYgOTMuNTI5NSAyNjYuMTE2IDkzLjUyOTUgMjY3LjM4NyA5Mi4yODM3IDI2Ny4zODcgODkuNzkyMSAyNjcuMzg3IDg3LjMwMDUgMjY2LjExNiA4Ni4wNTQ3IDI2My41NzYgODYuMDU0N1oiIGZpbGw9IiNGRkU1MDAiIGZpbGwtcnVsZT0iZXZlbm9kZCIvPjwvZz48L3N2Zz4=) no-repeat 1rem/1.8rem, #b32121;
    padding: 1rem 1rem 1rem 3.7rem;
    color: white;
}

.blazor-error-boundary::after {
    content: "An error has occurred."
}

/* --- Status dots ----------------------------------------------------- */

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
    background-color: var(--status-created);
}

/* --- Active (running) table row -------------------------------------- */

.table tbody tr.active-row {
    background-color: var(--active-row-bg);
    border-left: 3px solid #10b981;
}

/* --- Modal ----------------------------------------------------------- */

.modal-content {
    background-color: var(--modal-bg);
    color: var(--text-primary);
}

/* --- Progress -------------------------------------------------------- */

.progress {
    background-color: var(--progress-bg);
}

/* --- Alerts ---------------------------------------------------------- */

.alert-info {
    background-color: var(--alert-info-bg);
    border-color: var(--alert-info-border);
    color: var(--alert-info-text);
}

.alert-success {
    background-color: var(--alert-success-bg);
    border-color: var(--alert-success-border);
    color: var(--alert-success-text);
}

.alert-warning {
    background-color: var(--alert-warning-bg);
    border-color: var(--alert-warning-border);
    color: var(--alert-warning-text);
}

.alert-danger {
    background-color: var(--alert-danger-bg);
    border-color: var(--alert-danger-border);
    color: var(--alert-danger-text);
}

/* --- Toasts ---------------------------------------------------------- */

.toast {
    background-color: var(--bg-surface);
    border-color: var(--border-color);
    border-radius: 0.5rem;
    color: var(--text-primary);
}

.toast-header {
    background-color: var(--bg-surface-alt);
    border-bottom-color: var(--border-color);
    color: var(--text-primary);
}

/* --- Badges ---------------------------------------------------------- */

.badge.bg-success {
    background-color: #10b981 !important;
    color: #fff;
}

.badge.bg-danger {
    background-color: #ef4444 !important;
    color: #fff;
}

/* --- Audio controls -------------------------------------------------- */

audio {
    min-width: 300px;
}

/* --- Scrollbars (pre / overflow containers) -------------------------- */

pre,
.overflow-auto,
.overflow-y-auto,
.overflow-x-auto {
    scrollbar-width: thin;
    scrollbar-color: var(--scrollbar-thumb) var(--bg-scrollbar-track);
}

pre::-webkit-scrollbar,
.overflow-auto::-webkit-scrollbar,
.overflow-y-auto::-webkit-scrollbar,
.overflow-x-auto::-webkit-scrollbar {
    width: 6px;
    height: 6px;
}

pre::-webkit-scrollbar-track,
.overflow-auto::-webkit-scrollbar-track,
.overflow-y-auto::-webkit-scrollbar-track,
.overflow-x-auto::-webkit-scrollbar-track {
    background: var(--bg-scrollbar-track);
}

pre::-webkit-scrollbar-thumb,
.overflow-auto::-webkit-scrollbar-thumb,
.overflow-y-auto::-webkit-scrollbar-thumb,
.overflow-x-auto::-webkit-scrollbar-thumb {
    background-color: var(--scrollbar-thumb);
    border-radius: 3px;
}

/* --- Theme toggle button --------------------------------------------- */

.theme-toggle {
    background: none;
    border: 1px solid var(--border-color);
    border-radius: 4px;
    color: var(--text-secondary);
    cursor: pointer;
    font-size: 0.85rem;
    padding: 2px 8px;
    line-height: 1.5;
    transition: color 0.15s, border-color 0.15s;
}

.theme-toggle:hover {
    color: var(--text-primary);
    border-color: var(--border-light);
}
```

- [ ] **Step 2: Verify the app builds**

```bash
cd <repo>
dotnet build src/FishAudioOrchestrator.Web/
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/FishAudioOrchestrator.Web/wwwroot/css/app.css
git commit -m "feat: convert CSS to custom property theme system with dark and light themes"
```

---

### Task 7: Update Layouts and Components to Support Theming

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Components/App.razor`
- Modify: `src/FishAudioOrchestrator.Web/Components/Layout/MainLayout.razor`
- Modify: `src/FishAudioOrchestrator.Web/Components/Layout/NavMenu.razor`
- Modify: `src/FishAudioOrchestrator.Web/Components/Layout/EmptyLayout.razor`

- [ ] **Step 1: Update App.razor to set data-theme attribute**

Replace `src/FishAudioOrchestrator.Web/Components/App.razor` with:

```html
<!DOCTYPE html>
<html lang="en" data-theme="dark">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Fish Audio Orchestrator</title>
    <base href="/" />
    <link rel="icon" type="image/x-icon" href="favicon.ico" />
    <link rel="icon" type="image/svg+xml" href="favicon.svg" />
    <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css" rel="stylesheet" />
    <link href="css/app.css" rel="stylesheet" />
    <HeadOutlet @rendermode="InteractiveServer" />
</head>
<body>
    <Routes @rendermode="InteractiveServer" />
    <script src="_framework/blazor.web.js"></script>
    <script>
        window.setTheme = function(theme) {
            document.documentElement.setAttribute('data-theme', theme);
        };
        window.getTheme = function() {
            return document.documentElement.getAttribute('data-theme') || 'dark';
        };
    </script>
</body>
</html>
```

- [ ] **Step 2: Update MainLayout.razor to load user's theme and expose toggle**

Replace `src/FishAudioOrchestrator.Web/Components/Layout/MainLayout.razor` with:

```razor
@inherits LayoutComponentBase
@using FishAudioOrchestrator.Web.Data
@using FishAudioOrchestrator.Web.Data.Entities
@using FishAudioOrchestrator.Web.Hubs
@using FishAudioOrchestrator.Web.Services
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.EntityFrameworkCore
@inject OrchestratorEventBus EventBus
@inject IDbContextFactory<AppDbContext> DbFactory
@inject IJSRuntime JS
@implements IDisposable

<NavMenu Theme="@_theme" OnThemeToggle="ToggleTheme" />

<main class="container-fluid py-3">
    @Body
</main>

@if (_toast is not null)
{
    <div class="position-fixed bottom-0 end-0 p-3" style="z-index: 1050;">
        <div class="toast show @(_toast.Success ? "border-success" : "border-danger")" role="alert">
            <div class="toast-header">
                <strong class="me-auto">TTS @(_toast.Success ? "Complete" : "Failed")</strong>
                <button type="button" class="btn-close btn-close-white" @onclick="() => _toast = null"></button>
            </div>
            <div class="toast-body">
                @if (_toast.Success)
                {
                    <text>Generated in @_toast.DurationMs ms</text>
                }
                else
                {
                    <text>@_toast.Error</text>
                }
            </div>
        </div>
    </div>
}

@code {
    private TtsNotificationEvent? _toast;
    private string _theme = "dark";

    [CascadingParameter]
    private Task<AuthenticationState>? AuthState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        EventBus.OnTtsNotification += OnTtsNotificationRaw;

        if (AuthState is not null)
        {
            var state = await AuthState;
            var userId = state.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId is not null)
            {
                await using var db = await DbFactory.CreateDbContextAsync();
                var user = await db.Users.OfType<AppUser>().FirstOrDefaultAsync(u => u.Id == userId);
                if (user is not null)
                {
                    _theme = user.ThemePreference;
                }
            }
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JS.InvokeVoidAsync("setTheme", _theme);
        }
    }

    private async Task ToggleTheme()
    {
        _theme = _theme == "dark" ? "light" : "dark";
        await JS.InvokeVoidAsync("setTheme", _theme);

        // Persist to DB asynchronously
        if (AuthState is not null)
        {
            var state = await AuthState;
            var userId = state.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId is not null)
            {
                await using var db = await DbFactory.CreateDbContextAsync();
                var user = await db.Users.OfType<AppUser>().FirstOrDefaultAsync(u => u.Id == userId);
                if (user is not null)
                {
                    user.ThemePreference = _theme;
                    await db.SaveChangesAsync();
                }
            }
        }
    }

    private void OnTtsNotificationRaw(TtsNotificationEvent notification)
    {
        _ = InvokeAsync(() =>
        {
            _toast = notification;
            StateHasChanged();

            _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                if (_toast == notification)
                {
                    _toast = null;
                    await InvokeAsync(StateHasChanged);
                }
            });
        });
    }

    public void Dispose()
    {
        EventBus.OnTtsNotification -= OnTtsNotificationRaw;
    }
}
```

- [ ] **Step 3: Update NavMenu.razor to include theme toggle**

Replace `src/FishAudioOrchestrator.Web/Components/Layout/NavMenu.razor` with:

```razor
@using Microsoft.AspNetCore.Components.Authorization
@implements IDisposable
@inject GpuMetricsState GpuState

<nav class="navbar navbar-expand px-3">
    <a class="navbar-brand d-flex align-items-center" href="/">
        <img src="logo-nav.png" alt="" width="24" height="24" class="me-2" />
        Fish Audio Orchestrator
    </a>
    <AuthorizeView>
        <Authorized>
            <div class="navbar-nav">
                <NavLink class="nav-link" href="/" Match="NavLinkMatch.All">Dashboard</NavLink>
                <AuthorizeView Roles="Admin" Context="adminCtx1">
                    <NavLink class="nav-link" href="/deploy">Deploy</NavLink>
                </AuthorizeView>
                <NavLink class="nav-link" href="/voices">Voices</NavLink>
                <NavLink class="nav-link" href="/playground">TTS</NavLink>
                <NavLink class="nav-link" href="/history">History</NavLink>
                <NavLink class="nav-link" href="/logs">Logs</NavLink>
                <AuthorizeView Roles="Admin" Context="adminCtx2">
                    <NavLink class="nav-link" href="/admin/users">Users</NavLink>
                    <NavLink class="nav-link" href="/admin/settings">Settings</NavLink>
                </AuthorizeView>
            </div>
            <div class="navbar-nav ms-auto">
                <span class="nav-link text-muted" id="gpu-indicator">@_gpuInfo</span>
                <button class="theme-toggle nav-link" @onclick="OnThemeToggle" title="Toggle theme">
                    @(Theme == "dark" ? "Light" : "Dark")
                </button>
                <NavLink class="nav-link" href="/account">@context.User.Identity?.Name</NavLink>
            </div>
        </Authorized>
    </AuthorizeView>
</nav>

@code {
    private string _gpuInfo = "GPU: loading...";

    [Parameter]
    public string Theme { get; set; } = "dark";

    [Parameter]
    public EventCallback OnThemeToggle { get; set; }

    protected override void OnInitialized()
    {
        GpuState.OnChange += UpdateGpuInfo;
        if (GpuState.MemoryTotalMb > 0)
        {
            UpdateGpuInfo();
        }
    }

    private void UpdateGpuInfo()
    {
        var memPercent = GpuState.MemoryTotalMb > 0
            ? (int)(100.0 * GpuState.MemoryUsedMb / GpuState.MemoryTotalMb)
            : 0;
        _gpuInfo = $"Mem: {memPercent}%  Cores: {GpuState.UtilizationPercent}%";
        InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        GpuState.OnChange -= UpdateGpuInfo;
    }
}
```

- [ ] **Step 4: Update EmptyLayout.razor to use theme variables**

Replace `src/FishAudioOrchestrator.Web/Components/Layout/EmptyLayout.razor` with:

```razor
@inherits LayoutComponentBase

<main class="min-vh-100" style="background-color: var(--bg-body); color: var(--text-primary);">
    @Body
</main>
```

- [ ] **Step 5: Remove hardcoded bg-dark/text-light from Razor components**

Search all `.razor` files for hardcoded `bg-dark` and `text-light` Bootstrap classes used for theming (not for actual dark-background elements like log viewers which should stay dark). The key changes:

In toast section of `MainLayout.razor` — already handled in Step 2 (removed `bg-dark text-light` from toast-header and toast-body).

In pages that use `bg-dark` for form inputs/selects (e.g., `Logs.razor` line 17 `bg-dark` on select), remove the `bg-dark` class since the form styles now use CSS variables. Check these files:
- `Dashboard.razor` — log preview card uses `bg-dark` which is appropriate (log viewer should stay dark)
- `Logs.razor` — the select element has `bg-dark` class, remove it; the log viewer div uses `bg-dark`, keep it (logs are always dark)
- `Login.razor` — card uses `bg-dark`, change to use theme variable styling
- `LoginTotp.razor` — card uses `bg-dark`, change to use theme variable styling
- `Setup.razor` — cards/inputs use `bg-dark`, change to use theme variable styling

For login/setup pages (EmptyLayout), the components render inside `EmptyLayout` which now uses CSS variables. The individual cards should drop their `bg-dark` classes and use the card's default styling (which now comes from CSS variables).

The principle: if `bg-dark` is used for theming, remove it (let CSS variables handle it). If `bg-dark` is used for a specific UI element that should always be dark (like a terminal/log viewer), keep it but override with explicit dark colors.

- [ ] **Step 6: Verify the app builds and all tests pass**

```bash
cd <repo>
dotnet build src/FishAudioOrchestrator.Web/
dotnet test tests/FishAudioOrchestrator.Tests/
```

Expected: Build succeeds. All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/
git commit -m "feat: integrate theme system into layouts and components with toggle"
```

---

### Task 8: Visual Testing and Polish

**Files:**
- Possibly modify: `src/FishAudioOrchestrator.Web/wwwroot/css/app.css`
- Possibly modify: various `.razor` files

- [ ] **Step 1: Run the app and visually test dark theme**

```bash
cd <repo>/src/FishAudioOrchestrator.Web
dotnet run
```

Navigate to the app in a browser. Check every page in dark theme:
- Dashboard (GPU metrics, model table, log preview)
- Deploy page (form, model alert)
- TTS Playground (form, job table)
- Voice Library (table, forms, audio player)
- Generation History (table, audio player)
- Logs (container selector, log viewer)
- Admin pages (Users, Settings)
- Account pages (ManageAccount, ChangePassword, SetupTotp)
- Login and TOTP pages

Verify: no blue tints remain, all text is readable, forms are usable, buttons are visible.

- [ ] **Step 2: Test light theme**

Click the theme toggle. Verify every page in light theme:
- White backgrounds are clean
- Text is dark and readable
- Cards and surfaces have subtle grey differentiation
- Forms are clear with proper borders
- Alerts, badges, and status indicators are visible
- Log viewer/terminal areas stay dark (intentionally)

- [ ] **Step 3: Test theme persistence**

1. Set theme to light
2. Refresh the page — should remain light
3. Log out and log back in — should remain light
4. Switch back to dark — should persist across refresh

- [ ] **Step 4: Fix any visual issues found**

Adjust CSS variables or component markup as needed based on visual testing. Common issues to watch for:
- Insufficient contrast in one theme
- Bootstrap utility classes overriding CSS variables
- Missing variable references (hardcoded colors that were missed)

- [ ] **Step 5: Run full test suite**

```bash
dotnet test tests/FishAudioOrchestrator.Tests/
```

Expected: All tests pass.

- [ ] **Step 6: Commit any polish fixes**

```bash
git add -A
git commit -m "fix: theme visual polish after manual testing"
```

---
