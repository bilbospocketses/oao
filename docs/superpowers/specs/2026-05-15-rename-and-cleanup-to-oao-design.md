---
title: oao Rename + Cleanup Design
date: 2026-05-15
status: draft (awaiting user approval)
branch: chore/rename-and-cleanup-to-oao
---

# Project Rename to `oao` + Setup-Wizard Cleanup + Test-Suite Hardening

## Summary

Convert the project's operational identity from `OpenAudioOrchestrator` to `oao` (lowercase) across all code paths, file paths, runtime identifiers, and documentation. Preserve the user-facing branding string "Open Audio Orchestrator" wherever it appears as a human-readable label. Bake in three adjacent cleanups the rename surfaces:

1. Drop the brittle `dotnet run --project src/...` snippet from the Setup wizard's restart card (`Setup.razor:368`).
2. Centralize the SQLite DB filename behind a new `PlatformDefaults.DbFileName` property; refactor the Setup wizard to read from it (`Setup.razor:395`).
3. Neutralize the `auth` rate limiter inside the test fixture to eliminate three pre-existing flaky `AuthEndpointTests`.

**No-upgrade-path policy:** the project is treated as first-shelf release. Existing local installs are not expected to upgrade in place — anyone with a previous local DB clears it and re-runs Setup. This simplifies several decisions (cookie name change, DB filename change, DP application-name change, config-section key change all become non-issues at the user-visibility layer).

## Context

### Current repo state at brainstorm time

- Source: `C:/Users/jscha/source/repos/oao/` (folder already renamed locally).
- Remote: `personal/master` → `github.com/bilbospocketses/oao.git` (GitHub repo already renamed).
- Latest commit on master: `c9016b0 chore: enforce LF line endings via .gitattributes + renormalize`.
- Working tree is dirty with an abandoned `git stash pop` of stash `On master: rollout` — an earlier rename attempt that hit ~14 file conflicts on a stash-pop against a moving master and was walked away from. CHANGELOG.md, README.md, both setup docs, ACME plan+spec, Program.cs, Setup.razor, AdminSettings.razor, AcmeChallengeMiddleware.cs, AcmeCertificateService.cs, Setup{Download,Settings}Service.cs, the project files, and appsettings.json are all in `UU`/`UD` state. Untracked `src/oao.Web/`, `tests/oao.Tests/`, `oao.sln` represent the new tree the abandoned rename was creating.
- `stash@{0}` retains the original rollout, since `git stash pop` keeps the stash on conflict.

### Why redo rather than resume

The abandoned half-state was driven by partial decisions and inherits whatever was wrong about them. Restarting clean from `c9016b0` (per user direction) eliminates the carried staleness and lets the new design execute deterministically. The new untracked `oao.Web/` tree already uses `namespace oao.Web` (lowercase), confirming the past-self also picked all-lowercase casing — this design commits to the same direction.

### Why bundle the cleanups

The original brainstorm scoped a pure rename and tabled three adjacent code smells as TODO entries:

- `Setup.razor:368` hardcoded `dotnet run --project src/OpenAudioOrchestrator.Web` — fragile across path renames AND deployment modes (will be wrong once Velopack and Docker ship).
- `Setup.razor:395` hardcoded `_dbFileName = "AudioOrchestrator.db"` — duplicates the canonical default in `PlatformDefaults.DbPath` and doesn't follow the centralized-defaults pattern the rest of the wizard uses.
- Three pre-existing rate-limit-induced flaky tests in the auth integration suite — listed in `todo_oao.md` as "unrelated" but block a fully-green test run after the rename.

The user direction: bake all three in so the rename ships as a "complete fix" rather than a half-step. Branch name therefore becomes `chore/rename-and-cleanup-to-oao` to reflect dual scope.

## Decisions Locked

| ID | Decision |
|---|---|
| Casing | All lowercase `oao` everywhere — folders, csproj, sln, namespaces, assembly, Docker tag, binary. C# convention violation (PascalCase namespaces) is an accepted cost. |
| Dirty tree | Reset clean from `c9016b0`; drop `stash@{0}` rollout. Lossless safety-net snapshot is optional (see R7). |
| Rename scope (operational) | Config section key, default install paths, cookie name, DB filename — all renamed. |
| C1 + C2 (DP) | Rename Data-Protection `SetApplicationName("OpenAudioOrchestrator")` → `"oao"` (Program.cs:69, 85); rename cert CN `"CN=OpenAudioOrchestrator-DataProtection"` → `"CN=oao-DataProtection"` (Program.cs:263). |
| C3 + C4 (Orchestrator types) | Keep `OrchestratorHub`, `OrchestratorEventBus`, `DockerOrchestratorService`, `IDockerOrchestratorService` and the `/hubs/orchestrator` route. "Orchestrator" describes the role, not project identity. |
| D1 (Windows default DataRoot) | `C:\oao` literal — drop the `MyOpenAudioProj` pattern. |
| D2 (TOTP issuer) | `"Open Audio Orchestrator"` (with spaces) — matches branding, readable in authenticator apps. |
| D3 (historical docs) | Frozen with a top-of-file header note: `Note: post-rename. References to OpenAudioOrchestrator.* paths and config keys reflect their original names. Current equivalents are oao.* and oao:*.` |
| Item 1 (dev-restart snippet) | Drop the `<pre>dotnet run --project src/...</pre>` line entirely from the Setup wizard. Restart guidance relies on the user's existing terminal context. |
| Item 4 (DB filename) | Centralize: add `PlatformDefaults.DbFileName` static property returning `"oao.db"`. `DbPath = Path.Combine(DataRoot, DbFileName)`. Setup wizard reads `_dbFileName = PlatformDefaults.DbFileName`. |
| Item 2 (rate limiter in tests) | Override `RateLimiterOptions` in `CustomWebApplicationFactory` to register a `NoLimiter` partition. Production code untouched. |
| Commit structure | 7 commits, cleanly separated (rename × 3, cleanup × 2, docs × 1, memory × 1 (no git impact)). |
| Branch name | `chore/rename-and-cleanup-to-oao`. |
| Merge style | `--no-ff` with hand-written merge-commit message; keeps the 7 commits as a visible bubble in `git log --graph`. |
| CHANGELOG framing | Keep-a-Changelog `[Unreleased]` with Changed / Removed / Fixed sections. |
| PR workflow | Solo-owned per `feedback_pr_workflow.md`; no PR. Local merge + push. |

## Rename Map

### A. Code/path identifiers (all lowercase `oao`)

| Category | Before | After |
|---|---|---|
| Solution file | `OpenAudioOrchestrator.sln` | `oao.sln` |
| Web project folder | `src/OpenAudioOrchestrator.Web/` | `src/oao.Web/` |
| Web csproj | `OpenAudioOrchestrator.Web.csproj` | `oao.Web.csproj` |
| Web RootNamespace / AssemblyName | default → `OpenAudioOrchestrator.Web` | default → `oao.Web` (no explicit `<RootNamespace>` needed; defaults align with new filename) |
| Test project folder | `tests/OpenAudioOrchestrator.Tests/` | `tests/oao.Tests/` |
| Test csproj | `OpenAudioOrchestrator.Tests.csproj` | `oao.Tests.csproj` |
| Namespace prefix (all `.cs`) | `OpenAudioOrchestrator.Web.*` | `oao.Web.*` |
| Test namespace | `OpenAudioOrchestrator.Tests.*` | `oao.Tests.*` |
| `using` lines (all `.cs`) | `using OpenAudioOrchestrator.Web.*;` | `using oao.Web.*;` |
| `@using` lines (all `.razor`, esp. `_Imports.razor`) | `@using OpenAudioOrchestrator.Web.*` | `@using oao.Web.*` |

### B. Runtime / operational identifiers (all lowercase `oao`)

| Category | Before | After |
|---|---|---|
| Config section key (appsettings.json + every read site) | `"OpenAudioOrchestrator"` | `"oao"` |
| Windows default DataRoot | `C:\MyOpenAudioProj` | `C:\oao` |
| Linux default DataRoot | `/opt/OpenAudioOrchestrator` | `/opt/oao` |
| DB filename | `AudioOrchestrator.db` | `oao.db` |
| Cookie name | `.OAO.Auth` (Program.cs:131) | `.oao.Auth` |
| Data-Protection app name (2 sites: Program.cs:69, 85) | `"OpenAudioOrchestrator"` | `"oao"` |
| Data-Protection cert CN (Program.cs:263) | `"CN=OpenAudioOrchestrator-DataProtection"` | `"CN=oao-DataProtection"` |
| TOTP issuer (Setup.razor:696) | `"OpenAudioOrchestrator"` (no spaces) | `"Open Audio Orchestrator"` (with spaces) |
| Docker network name (already aligned) | `oao-network` | `oao-network` |
| systemd unit name (already aligned) | `oao.service` | `oao.service` |

### C. Class / interface names and hub route — preserved

`OrchestratorHub`, `OrchestratorEventBus`, `DockerOrchestratorService`, `IDockerOrchestratorService`, `OrchestratorHubTests`, `OrchestratorEventBusTests`, `DockerOrchestratorServiceTests` all stay PascalCase. The SignalR hub route stays `/hubs/orchestrator`. Rationale: "Orchestrator" describes what these classes do (the app orchestrates Fish Speech containers), not the project's identity.

### D. User-facing branding strings — preserved

All UI page titles, login page, navigation, footer copyright, README title, LICENSE copyright, LINUX/WINDOWS setup-doc titles, and the TOTP issuer label in authenticator apps remain "Open Audio Orchestrator" (with spaces). LICENSE was already updated in commit `465187a`.

One non-branding edit inside `Setup.razor` is the wizard's literal code snippet at line 368, which is removed entirely by the item-1 cleanup rather than path-edited.

## Cleanup Refactors

### Setup wizard — drop dev-restart snippet (item 1)

The card at `Setup.razor:365-378` currently reads:

> Stop the application (`Ctrl+C` in the terminal) and restart with:
> `dotnet run --project src/OpenAudioOrchestrator.Web`
> Then navigate to **https://`<domain>`** (or **http://localhost:5206** if no domain)

The terminal-command line (`<pre>dotnet run --project src/OpenAudioOrchestrator.Web</pre>`) is removed. The restart-and-navigate flow becomes:

> Restart the application, then navigate to **https://`<domain>`** (or **http://localhost:5206** if no domain).

Rationale: the snippet assumes the user is running from a clone via `dotnet run` — true today, but not once Velopack or Docker ship. The user reached the wizard from a terminal they already control; the snippet doesn't add information they need.

### Setup wizard — centralize DB filename (item 4)

`PlatformDefaults.cs` gains a new static property:

```csharp
public static string DbFileName => "oao.db";

public static string DbPath =>
    Path.Combine(DataRoot, DbFileName);
```

`Setup.razor:395` changes from `private string _dbFileName = "AudioOrchestrator.db";` to:

```csharp
private string _dbFileName = PlatformDefaults.DbFileName;
```

`PlatformDefaultsTests.cs` gains one new assertion:

```csharp
[Fact]
public void DbFileName_IsOaoDb() =>
    Assert.Equal("oao.db", PlatformDefaults.DbFileName);
```

The existing `DbPath_ContainsDataRoot` assertion stays valid; the `Assert.EndsWith("AudioOrchestrator.db", result)` assertion at `PlatformDefaultsTests.cs:20` is updated to `Assert.EndsWith("oao.db", result)` in Commit 3 (runtime defaults rename).

### Rate-limiter neutralization in test factory (item 2)

`CustomWebApplicationFactory.cs` adds a `ConfigureServices` override that replaces the production `RateLimiterOptions` with a no-limiter chain:

```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.ConfigureServices(services =>
    {
        services.Configure<RateLimiterOptions>(opts =>
        {
            opts.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                    RateLimitPartition.GetNoLimiter<string>("unlimited")));
        });
    });

    // ... existing ConfigureWebHost body ...
}
```

Result: every endpoint, including auth-protected ones with `RequireRateLimiting("auth")`, gets unlimited capacity in test runs. Production rate-limiter unchanged.

If a future test needs to explicitly exercise rate-limit behavior, it can opt back in via a dedicated factory subclass that does not apply this override.

## Commit Plan

Branch: `chore/rename-and-cleanup-to-oao`, base `master @ c9016b0`. Merge with `--no-ff`.

### Pre-flight (not a commit)

```
git checkout -- .              # revert UU/UD conflict residue
git clean -fdx                 # remove untracked oao.Web/, oao.Tests/, oao.sln from the abandoned rename
git stash drop stash@{0}       # drop the abandoned 'rollout' stash
git status                     # verify clean tree matching c9016b0
git checkout -b chore/rename-and-cleanup-to-oao
```

Optional safety net (R7): `git stash show -p stash@{0} > /tmp/abandoned-rollout-2026-04.patch` before the drop, to preserve a recoverable snapshot.

### Commit 1 — `chore(rename): project folders, csproj, sln, namespaces`

**File operations (do `git mv` first, content edits second):**

- `git mv src/OpenAudioOrchestrator.Web src/oao.Web`
- `git mv src/oao.Web/OpenAudioOrchestrator.Web.csproj src/oao.Web/oao.Web.csproj`
- `git mv tests/OpenAudioOrchestrator.Tests tests/oao.Tests`
- `git mv tests/oao.Tests/OpenAudioOrchestrator.Tests.csproj tests/oao.Tests/oao.Tests.csproj`
- `git mv OpenAudioOrchestrator.sln oao.sln`

**Content edits:**

- `oao.sln`: two `Project(...) = "OpenAudioOrchestrator.Web", "src\OpenAudioOrchestrator.Web\OpenAudioOrchestrator.Web.csproj", ...` lines → `"oao.Web", "src\oao.Web\oao.Web.csproj", ...`. Same for the tests project. GUIDs unchanged.
- `tests/oao.Tests/oao.Tests.csproj`: `<ProjectReference Include="..\..\src\OpenAudioOrchestrator.Web\OpenAudioOrchestrator.Web.csproj" />` → `..\..\src\oao.Web\oao.Web.csproj`.
- All `.cs` files: `namespace OpenAudioOrchestrator.Web` → `namespace oao.Web`; `using OpenAudioOrchestrator.Web` → `using oao.Web`. Same for `.Tests` namespace + usings.
- All `.razor` files (notably `Components/_Imports.razor`): `@using OpenAudioOrchestrator.Web*` → `@using oao.Web*`.

**Explicitly not in this commit:** config-section key literals (`Configuration["OpenAudioOrchestrator:..."]`), runtime paths, cookie name, DP app names, TOTP issuer — all land in commits 2 + 3.

**Gate 1:** `dotnet build oao.sln` from repo root succeeds. `dotnet test oao.sln` runs; expect 3 known pre-existing rate-limited auth failures (Commit 5 fixes those), everything else green. Any *new* test failure here is rename-induced — fix in this commit before moving on.

### Commit 2 — `chore(rename): config section OpenAudioOrchestrator → oao`

**Source edits:**

- `src/oao.Web/appsettings.json`: top-level key `"OpenAudioOrchestrator"` → `"oao"`.
- `src/oao.Web/Program.cs` lines 23, 64, 78, 149: `Configuration["OpenAudioOrchestrator:..."]` → `Configuration["oao:..."]` (Domain, DataRoot, DatabaseKey, DockerEndpoint).
- `src/oao.Web/Services/AcmeCertificateService.cs` lines 49, 150, 152: same pattern (DataRoot × 2, Domain).
- `src/oao.Web/Services/SetupSettingsService.cs:54`: `root["OpenAudioOrchestrator"]` → `root["oao"]`. (The local variable name `oao` is already cute and stays.)
- `src/oao.Web/Components/Pages/Admin/AdminSettings.razor` lines 46, 71, 73: read-side `Config["OpenAudioOrchestrator:Domain"]` and write-side `if (prop.Name == "OpenAudioOrchestrator")` / `writer.WriteStartObject("OpenAudioOrchestrator")` all update.

**Test edits (9 files):**

| File | Lines | Keys |
|---|---|---|
| `tests/oao.Tests/Auth/AdminSeedServiceTests.cs` | 18, 19 | `AdminUser`, `AdminPassword` |
| `tests/oao.Tests/Integration/CustomWebApplicationFactory.cs` | 43, 45 | `DataRoot`, `Domain` |
| `tests/oao.Tests/Services/ContainerConfigServiceTests.cs` | 27-31 | `PortRange:Start/End`, `DataRoot`, `DefaultImageTag`, `DockerNetworkName` |
| `tests/oao.Tests/Services/DockerNetworkServiceTests.cs` | 17 | `DockerNetworkName` |
| `tests/oao.Tests/Services/HealthMonitorServiceTests.cs` | 33 | `HealthCheckIntervalSeconds` |
| `tests/oao.Tests/Services/TtsJobProcessorTests.cs` | 47 | `DataRoot` |
| `tests/oao.Tests/Services/VoiceLibraryServiceTests.cs` | 30 | `DataRoot` |
| `tests/oao.Tests/SignalR/HealthMonitorHubTests.cs` | 32 | `HealthCheckIntervalSeconds` |

**Pre-commit check:** `rg 'OpenAudioOrchestrator:'` should return zero hits.

**Gate 2:** `dotnet build` + `dotnet test` (expect same 3 known auth failures). Then a runtime smoke: `dotnet run --project src/oao.Web -c Release`, visit `http://localhost:5206`, confirm Admin Settings page binds the config section without falling back to defaults.

### Commit 3 — `chore(rename): runtime defaults (paths, DB filename, cookie, DP)`

**Source edits:**

- `src/oao.Web/PlatformDefaults.cs:6` (Windows DataRoot): `@"C:\MyOpenAudioProj"` → `@"C:\oao"`.
- `src/oao.Web/PlatformDefaults.cs:6` (Linux DataRoot): `"/opt/OpenAudioOrchestrator"` → `"/opt/oao"`.
- `src/oao.Web/PlatformDefaults.cs:9` (DbPath): `"AudioOrchestrator.db"` → `"oao.db"`. (Commit 4 will refactor this to derive from `DbFileName`; this commit just changes the literal so the rename is atomic.)
- `src/oao.Web/Program.cs:131`: `opts.Cookie.Name = ".OAO.Auth"` → `".oao.Auth"`.
- `src/oao.Web/Program.cs:69, 85`: `.SetApplicationName("OpenAudioOrchestrator")` → `.SetApplicationName("oao")` (both Data-Protection registration sites).
- `src/oao.Web/Program.cs:263`: `"CN=OpenAudioOrchestrator-DataProtection"` → `"CN=oao-DataProtection"`.
- `src/oao.Web/Components/Pages/Setup.razor:395`: `_dbFileName = "AudioOrchestrator.db"` → `"oao.db"`. (Commit 4 will refactor this to read `PlatformDefaults.DbFileName`.)
- `src/oao.Web/Components/Pages/Setup.razor:696`: TOTP issuer `"OpenAudioOrchestrator"` → `"Open Audio Orchestrator"`.

**Test edits (2 files):**

- `tests/oao.Tests/Auth/TotpServiceTests.cs:43, 95, 121`: `service.GenerateSetupInfoAsync(user, "OpenAudioOrchestrator")` → `"Open Audio Orchestrator"`.
- `tests/oao.Tests/PlatformDefaultsTests.cs:20`: `Assert.EndsWith("AudioOrchestrator.db", result)` → `Assert.EndsWith("oao.db", result)`.

**Gate 3:** `dotnet build` + `dotnet test` (still expect 3 known auth failures). Smoke: `dotnet run --project src/oao.Web -c Release` + browser. Expect Setup wizard from scratch (the orphaned `C:\MyOpenAudioProj\AudioOrchestrator.db` is no longer found at the new default `C:\oao\oao.db`). Confirm: (a) app starts without DP key panic; (b) login flow works against the new `.oao.Auth` cookie; (c) Setup wizard's default DB filename field displays `oao.db`.

### Commit 4 — `refactor(setup): centralize DB filename, drop dev-restart snippet`

**Source edits:**

- `src/oao.Web/PlatformDefaults.cs`: add `public static string DbFileName => "oao.db";` property. Refactor `DbPath` to `public static string DbPath => Path.Combine(DataRoot, DbFileName);` so the literal lives in exactly one place.
- `src/oao.Web/Components/Pages/Setup.razor:395`: `private string _dbFileName = "oao.db";` → `private string _dbFileName = PlatformDefaults.DbFileName;`.
- `src/oao.Web/Components/Pages/Setup.razor:365-378`: remove the `<pre>dotnet run --project src/oao.Web</pre>` line and the preceding "Stop the application... restart with:" `<p>`. Rewrite to a single sentence: "Restart the application, then navigate to..." followed by the existing branching `<p>` for domain vs localhost.

**Test edits:**

- `tests/oao.Tests/PlatformDefaultsTests.cs`: add a new test method `[Fact] public void DbFileName_IsOaoDb() => Assert.Equal("oao.db", PlatformDefaults.DbFileName);` pinning the new contract. The existing `DbPath_ContainsDataRoot` test (lines 16-21 in the original file) still passes after this refactor because the new `DbPath = Path.Combine(DataRoot, DbFileName)` still starts with `DataRoot` and ends with `"oao.db"` — its `Assert.EndsWith` assertion (already updated to `"oao.db"` in Commit 3) is unchanged.

**Gate 4:** `dotnet build` + `dotnet test` (still 3 known auth failures — Commit 5 fixes those). Smoke: open the Setup wizard final step in a fresh browser tab, confirm the restart card no longer shows the `dotnet run` line and shows only the "navigate to" guidance.

### Commit 5 — `test: disable rate limiter in test factory`

**Source edits:**

- `tests/oao.Tests/Integration/CustomWebApplicationFactory.cs`: inside `ConfigureWebHost`, add `services.Configure<RateLimiterOptions>(...)` block that replaces the global limiter with a `NoLimiter`-partitioned chain (full snippet in the "Cleanup Refactors" section above).
- No production-code changes.

**Gate 5:** `dotnet test`. Expected outcome: **all auth integration tests pass, including the 3 previously-flaky ones.** Total green count goes up by 3. If any auth tests still fail, the failure is no longer rate-limit-induced and should be investigated.

### Commit 6 — `docs: refresh README + setup docs + CHANGELOG; freeze historical`

**README.md edits:**

- Line 2: `<img src="src/OpenAudioOrchestrator.Web/wwwroot/logo.png">` → `src/oao.Web/wwwroot/logo.png`.
- Line 53: `git clone https://github.com/bilbospocketses/OpenAudioOrchestrator.git` → `bilbospocketses/oao.git`.
- Line 54: `cd OpenAudioOrchestrator` → `cd oao`.
- Line 55: `dotnet run --project src/OpenAudioOrchestrator.Web` → `dotnet run --project src/oao.Web`.
- Line 68: `src/OpenAudioOrchestrator.Web/appsettings.json` → `src/oao.Web/appsettings.json`.
- Lines 73-83: config-key table — every `OpenAudioOrchestrator:Key` → `oao:Key`.
- Line 88: env-var prefix `OpenAudioOrchestrator__AdminUser` / `OpenAudioOrchestrator__AdminPassword` → `oao__AdminUser` / `oao__AdminPassword` (.NET hierarchy separator is `__`).
- Defensive sweep: `rg -F 'OpenAudioOrchestrator' README.md` for any other occurrences after the above edits — should be zero before commit.

**docs/LINUX-SETUP.md + docs/WINDOWS-SETUP.md edits:**

Per-file ripgrep + replace for `OpenAudioOrchestrator` (path refs + config-key refs). systemd unit name is already `oao.service`, so no change there. After edits, `rg -F 'OpenAudioOrchestrator' docs/LINUX-SETUP.md docs/WINDOWS-SETUP.md` should return zero hits.

**Historical docs — frozen, header note added:**

Files receiving the D3 note as the first content line below any front-matter (full historical-doc inventory at brainstorm time):

- `docs/superpowers/plans/2026-03-29-phase4-security-auth.md`
- `docs/superpowers/plans/2026-03-29-phase5-signalr-dashboard.md`
- `docs/superpowers/plans/2026-03-30-event-bus-refactor.md`
- `docs/superpowers/plans/2026-03-30-phase6-polish-readme.md`
- `docs/superpowers/plans/2026-03-31-setup-wizard.md`
- `docs/superpowers/plans/2026-03-31-tts-job-queue.md`
- `docs/superpowers/plans/2026-04-01-audit-and-theme-plan.md`
- `docs/superpowers/plans/2026-04-02-linux-compatibility.md`
- `docs/superpowers/plans/2026-04-04-acme-replacement.md`
- `docs/superpowers/specs/2026-03-29-phase4-security-auth-design.md`
- `docs/superpowers/specs/2026-03-29-phase5-signalr-dashboard-design.md`
- `docs/superpowers/specs/2026-03-30-phase6-polish-readme-design.md`
- `docs/superpowers/specs/2026-03-31-setup-wizard-design.md`
- `docs/superpowers/specs/2026-03-31-tts-job-queue-design.md`
- `docs/superpowers/specs/2026-04-01-audit-and-theme-design.md`
- `docs/superpowers/specs/2026-04-02-linux-compatibility-design.md`
- `docs/superpowers/specs/2026-04-04-acme-replacement-design.md`
- `docs/audit-report.md`

The new design doc at `docs/superpowers/specs/2026-05-15-rename-and-cleanup-to-oao-design.md` is *not* historical — it does not receive the note (it documents the rename itself).

Note text:

```
> **Note:** post-rename. References to `OpenAudioOrchestrator.*` paths and
> config keys reflect their original names. Current equivalents are
> `oao.*` and `oao:*`.
```

**CHANGELOG.md edits:**

New `[Unreleased]` section at the top:

```markdown
## [Unreleased]

### Changed
- Project renamed to `oao` everywhere (folders, csproj, sln, namespaces,
  config section, cookie name, DP app name + cert CN, TOTP issuer,
  default paths, DB filename).
- **BREAKING:** existing installs cannot upgrade in place. Delete any
  previous local DB + clear browser cookies; re-run Setup.
- DB filename is now `oao.db` (was `AudioOrchestrator.db`).
- TOTP issuer is now "Open Audio Orchestrator" (was `OpenAudioOrchestrator`);
  existing authenticator entries continue to authenticate (the shared
  secret is unchanged) but display the old label.

### Removed
- Setup wizard's `dotnet run --project src/...` instruction snippet on the
  final-step card. Restart guidance now relies on the user's existing
  terminal context.

### Fixed
- Auth integration tests are now deterministic — `RateLimiter` is
  neutralized inside `CustomWebApplicationFactory`. The 3 pre-existing
  rate-limit-induced flaky failures are eliminated.
```

Existing historical CHANGELOG entries are not rewritten.

**Gate 6:** Visual diff review. No build impact for docs-only.

### Commit 7 — `chore: update memory/todo paths` *(outside the repo, no git commit)*

Memory-store edits at `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/`:

- `todo_oao.md`:
  - Line ~9: source path → `C:/Users/jscha/source/repos/oao/`.
  - Line ~10: remote URL → `bilbospocketses/oao` (already on the remote side; this is the doc catching up).
  - Lines ~17-20: build commands → `cd C:/Users/jscha/source/repos/oao`, `dotnet build oao.sln`, `dotnet test oao.sln`, `dotnet run --project src/oao.Web -c Release`.
  - Frontmatter `name:` / `description:` — minor relabel from "OpenAudioOrchestrator TODOs" → "oao TODOs" (or keep as-is — purely cosmetic).
- `project_oao.md`: ripgrep `OpenAudioOrchestrator` and replace path refs / URL refs. Title and human-readable refs to "Open Audio Orchestrator" stay.
- `project_index.md`: existing entry text already says "OAO (Open Audio Orchestrator)" + paths to `project_oao.md` and `todo_oao.md`, both of which are already correctly named — no changes expected. Verify during commit.

No git commit; memory store is not in a git repo.

## Post-Branch Workflow

```
git checkout master
git merge --no-ff chore/rename-and-cleanup-to-oao -m "$(cat <<'EOF'
Merge branch 'chore/rename-and-cleanup-to-oao'

Project rename to oao + setup-wizard cleanup + test-suite hardening.

- chore(rename) ×3: project structure, config section, runtime defaults
- refactor(setup): centralize DB filename via PlatformDefaults.DbFileName;
  drop dev-restart snippet
- test: neutralize rate limiter in CustomWebApplicationFactory (3
  previously-flaky auth tests now deterministic)
- docs: README + setup docs + Keep-a-Changelog Unreleased entry; freeze
  historical superpowers docs with a top-of-file post-rename note

BREAKING: existing installs cannot upgrade in place. See CHANGELOG.
EOF
)"
git push personal master
git push personal --delete chore/rename-and-cleanup-to-oao
```

Final smoke: clone the repo fresh into a scratch folder, `dotnet run --project src/oao.Web` from clean, walk the Setup wizard, confirm everything works end-to-end.

## Risks + Mitigations

**R1 — Missed namespace ref → build break (Commit 1).** Catch: build gate after Commit 1. Mitigation: pre-commit ripgrep `rg -t cs -t razor 'OpenAudioOrchestrator\.Web'` and `'OpenAudioOrchestrator\.Tests'` should return zero hits.

**R2 — Missed config-key literal → silent fallback to defaults (Commit 2).** Highest-risk commit because .NET config binding fails *silently*. Catch: `ContainerConfigServiceTests.cs`, `HealthMonitorServiceTests.cs`, and others bind config values and assert; missed-rewrite tests will fail. Mitigation: pre-commit `rg 'OpenAudioOrchestrator:'` should return zero hits before commit. Plus the post-build smoke against Admin Settings.

**R3 — Dev DB at old path orphaned (Commit 3).** Expected outcome, not a bug. Per no-upgrade-path policy, this is the intended behavior — user re-runs Setup against the new default path. Old `C:\MyOpenAudioProj\AudioOrchestrator.db` can be manually deleted after validation. No mitigation needed.

**R4 — DP app name change invalidates existing encrypted state (Commit 3).** Affects encrypted column values in any existing dev DB. Per no-upgrade-path policy, acceptable. Documented in CHANGELOG `[Unreleased]` Changed → BREAKING.

**R5 — Cookie name change → existing sessions logged out (Commit 3).** Per no-upgrade-path policy, acceptable. TOTP enrollments are *not* invalidated — the issuer string is a display label, not part of the shared-secret authentication path. Existing authenticator entries continue to authenticate with the old label displayed.

**R6 — `--no-ff` merge auto-generated message reads ugly.** Mitigation: hand-write the merge-commit message per the snippet under "Post-Branch Workflow".

**R7 — Stash drop is irreversible.** Per user decision, content is reproducible from this spec. Optional safety net: `git stash show -p stash@{0} > /tmp/abandoned-rollout-2026-04.patch` before drop. User can confirm at execution time.

**R8 — Refactor surface in Commit 4 has more inertia than a pure rename.** Adding `PlatformDefaults.DbFileName` is a small API addition; refactoring `DbPath` to derive is straightforward. Risk is low. Test additions in the same commit pin the contract.

**R9 — Rate-limiter test override may mask actual rate-limit regressions in future.** Mitigation: documented in the test factory's `ConfigureWebHost` override comment. If a future feature wants to actively test rate-limiting behavior, it spins up its own factory subclass that does not apply this override.

## Rollback Paths

**During the branch, mid-flight:**
- Bad commit → `git reset --hard HEAD~1`, fix, re-commit.
- Branch unsalvageable → `git checkout master && git branch -D chore/rename-and-cleanup-to-oao` and restart.

**After merge to master, problem found locally:**
- `git revert -m 1 <merge-commit-sha>` — single revert undoes the entire 7-commit branch as one unit (`--no-ff` makes this clean). Push the revert.

**After push to remote, problem reported later:**
- Same revert path. No DB migration to roll back — dev-DB orphaning is intentional, not a transformation.

## Test-Side Impact Summary

| Commit | Test files touched | Reason |
|---|---|---|
| 1 | `tests/oao.Tests/oao.Tests.csproj` (1 file) | `<ProjectReference>` path update |
| 2 | 9 test files | `"OpenAudioOrchestrator:Key"` → `"oao:Key"` config-key literals |
| 3 | `TotpServiceTests.cs` (3 sites), `PlatformDefaultsTests.cs` (1 site) | TOTP issuer string + DB filename literal |
| 4 | `PlatformDefaultsTests.cs` (+1 new assertion) | Pin `DbFileName` contract |
| 5 | `CustomWebApplicationFactory.cs` (1 file) | Rate-limiter no-op override |

Total test-file touches: ~13. After all 7 commits, the test suite is expected to be fully green (no known pre-existing failures remain).

## Memory Store Updates (Commit 7 — not in repo)

- `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/todo_oao.md` — source path, remote URL, build commands.
- `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/project_oao.md` — path/URL ref sweep.
- `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/project_index.md` — verify no changes needed.

## Out of Scope (explicitly not addressed in this branch)

- 3 pre-existing rate-limited auth test failures are addressed by Commit 5. (Previously listed as out-of-scope; baked in per user direction.)
- `Setup.razor:368` brittle snippet is addressed by Commit 4. (Previously listed as out-of-scope; baked in.)
- `Setup.razor:395` hardcoded DB filename is addressed by Commit 4. (Previously listed as out-of-scope; baked in.)
- Velopack installer, Docker Hub publish, Linux validation, LE cert status widget, SEC-03 CSP hardening, Playwright E2E — remain as active TODOs in `todo_oao.md` after this branch ships.
- Folder-name pattern decision for the remaining Setup-wizard literals (`"Checkpoints"`, `"References"`, `"Output"`) — these follow the same hardcoded-literal pattern as the original `_dbFileName` but don't have a canonical default in `PlatformDefaults`. Refactoring them is genuinely separate work; left as-is.
