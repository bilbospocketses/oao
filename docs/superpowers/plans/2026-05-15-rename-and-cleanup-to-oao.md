# oao Rename + Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the project's operational identity from `OpenAudioOrchestrator` to `oao` (lowercase) across every code path, file path, runtime identifier, and doc; bake in three adjacent cleanups (centralize `PlatformDefaults.DbFileName`, drop the brittle dev-restart snippet from the Setup wizard, neutralize the auth rate-limiter in the test factory).

**Architecture:** 7 commits on a `chore/rename-and-cleanup-to-oao` branch off master HEAD (`083004d`, which is `c9016b0` + the spec commit). Each commit is its own coherent narrative. Build + test runs between commits gate progress. After all commits land, `--no-ff` merge to master with a hand-written merge message preserves the rename as a visible bubble in `git log --graph`.

**Tech Stack:** .NET 9 Blazor Server + xUnit + EF Core + SQLCipher + ASP.NET Identity + ASP.NET Core RateLimiter. Windows-first dev box (PowerShell 7). Per-file rename for code; mass-replace for namespaces via PowerShell; manual edits for Setup wizard refactor.

**Spec:** `docs/superpowers/specs/2026-05-15-rename-and-cleanup-to-oao-design.md` (committed at `083004d` on master). This plan executes that spec; every decision is locked there.

---

## File Structure

This is a rename + small refactor, not a greenfield build — every file already exists. Mapping by responsibility:

**Renamed (folder/path):**
- `src/OpenAudioOrchestrator.Web/` → `src/oao.Web/` (entire folder tree, ~80 files)
- `tests/OpenAudioOrchestrator.Tests/` → `tests/oao.Tests/` (entire folder tree, ~30 files)
- `OpenAudioOrchestrator.sln` → `oao.sln` (1 file, content also edited)
- `*.csproj` files within renamed folders (2 files, content also edited)

**Modified (content only, no rename):**
- `src/oao.Web/Program.cs` — config-key string literals (4 sites), cookie name, DP app name (×2), cert CN
- `src/oao.Web/PlatformDefaults.cs` — DataRoot defaults, DB filename, new `DbFileName` property
- `src/oao.Web/Components/Pages/Setup.razor` — TOTP issuer, DB filename default, restart-card cleanup
- `src/oao.Web/Components/Pages/Admin/AdminSettings.razor` — config-key string literals (3 sites)
- `src/oao.Web/Services/AcmeCertificateService.cs` — config-key string literals (3 sites)
- `src/oao.Web/Services/SetupSettingsService.cs` — config-key string literal (1 site)
- `src/oao.Web/appsettings.json` — top-level config section key
- `tests/oao.Tests/Integration/CustomWebApplicationFactory.cs` — config-key literals (2 sites), new `RateLimiterOptions` override
- `tests/oao.Tests/**/*.cs` — config-key literals across 8 more test files + TOTP issuer literals + DB filename literal
- `README.md`, `docs/LINUX-SETUP.md`, `docs/WINDOWS-SETUP.md` — full path/key sweep
- `docs/superpowers/plans/*.md`, `docs/superpowers/specs/*.md`, `docs/audit-report.md` — top-of-file frozen-doc note (17 historical files)
- `CHANGELOG.md` — new `[Unreleased]` section

**External (not in repo):**
- `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/todo_oao.md` — source path, remote URL, build commands
- `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/project_oao.md` — path/URL ref sweep

**Created (new):**
- (No new source files. New property `PlatformDefaults.DbFileName` is added inside the existing `PlatformDefaults.cs`. New test method added inside existing `PlatformDefaultsTests.cs`.)

---

## Task 0: Pre-flight — clean reset + branch creation

**Goal:** Get the working tree from "abandoned stash-pop residue" to "clean state matching current master HEAD" + create the rename branch.

**Files:** No file edits. Working-tree state + git plumbing.

- [ ] **Step 1: Verify current state matches assumptions**

```powershell
Set-Location C:\Users\jscha\source\repos\oao
git log -1 --oneline                # expected: 083004d docs: add spec for oao rename + cleanup
git status --short | Measure-Object | Select-Object Count   # expected: ~20+ entries (UU/UD/??/M/D)
git stash list                      # expected: stash@{0}: On master: rollout
```

Expected: HEAD is `083004d`, working tree is dirty with UU/UD/??/M/D entries from the abandoned stash-pop, one stash named `rollout` exists.

If any of these don't match, STOP and re-orient before continuing.

- [ ] **Step 2 (optional, recommended): Snapshot the abandoned stash to a patch file**

```powershell
git stash show -p 'stash@{0}' > "$env:TEMP\abandoned-rollout-2026-04.patch"
Get-Item "$env:TEMP\abandoned-rollout-2026-04.patch" | Select-Object FullName, Length
```

Expected: file exists, size is non-zero (it contains the diff that would be dropped). This is a safety net. If the rename later goes wrong in a way that warrants pulling something from the old rollout, the patch is recoverable from temp.

- [ ] **Step 3: Revert tracked file changes (un-conflicts UU/UD entries)**

```powershell
git checkout -- .
git status --short | Where-Object { $_ -match '^(UU|UD|M )' } | Measure-Object | Select-Object Count
```

Expected: zero UU/UD/M entries after the checkout. Only `??` (untracked) entries remain.

- [ ] **Step 4: Remove untracked residue from the abandoned rename**

```powershell
git clean -fdx
git status --short
```

Expected: empty output (clean working tree).

Note: `git clean -fdx` also removes `bin/`, `obj/`, and `node_modules/` — fine, they regenerate on next build. The spec doc at `docs/superpowers/specs/2026-05-15-rename-and-cleanup-to-oao-design.md` is committed to master so it is NOT untracked and is NOT touched by `clean`.

- [ ] **Step 5: Drop the abandoned stash**

```powershell
git stash drop 'stash@{0}'
git stash list
```

Expected: empty stash list.

- [ ] **Step 6: Verify clean state**

```powershell
git status
git log -1 --oneline
```

Expected: working tree clean, on branch `master`, HEAD = `083004d docs: add spec for oao rename + cleanup`.

- [ ] **Step 7: Create the rename branch**

```powershell
git checkout -b chore/rename-and-cleanup-to-oao
git branch --show-current
```

Expected: `chore/rename-and-cleanup-to-oao`.

- [ ] **Step 8: Run baseline build + tests to capture the starting state**

```powershell
dotnet build OpenAudioOrchestrator.sln
dotnet test OpenAudioOrchestrator.sln --logger "console;verbosity=normal"
```

Expected: build succeeds. Test run has **3 pre-existing rate-limited auth failures** (per `todo_oao.md`); everything else passes. Record the failing test names — they should be the same 3 tests that Task 5 will fix.

Captured baseline failing tests (write down here):

```
1. [test name]
2. [test name]
3. [test name]
```

This baseline gates Task 5: those 3 tests should go from FAIL to PASS after the rate-limiter override.

---

## Task 1: Commit 1 — project folders, csproj, sln, namespaces

**Goal:** Rename the project's structural identity (folders, csproj, sln, namespace prefixes) from `OpenAudioOrchestrator.Web|.Tests` to `oao.Web|.Tests`. No semantic/string changes — pure structural rename.

**Files:**
- Rename: `src/OpenAudioOrchestrator.Web/` → `src/oao.Web/`
- Rename: `tests/OpenAudioOrchestrator.Tests/` → `tests/oao.Tests/`
- Rename: `OpenAudioOrchestrator.sln` → `oao.sln`
- Rename: `src/oao.Web/OpenAudioOrchestrator.Web.csproj` → `src/oao.Web/oao.Web.csproj`
- Rename: `tests/oao.Tests/OpenAudioOrchestrator.Tests.csproj` → `tests/oao.Tests/oao.Tests.csproj`
- Modify (content): `oao.sln` (project lines + paths)
- Modify (content): `tests/oao.Tests/oao.Tests.csproj` (`<ProjectReference>` path)
- Modify (content, mass): all `.cs` files (namespaces, `using` lines)
- Modify (content, mass): all `.razor` files (`@using` lines)

- [ ] **Step 1: Rename folders and project files via `git mv` (so git tracks the renames)**

```powershell
Set-Location C:\Users\jscha\source\repos\oao
git mv src/OpenAudioOrchestrator.Web src/oao.Web
git mv src/oao.Web/OpenAudioOrchestrator.Web.csproj src/oao.Web/oao.Web.csproj
git mv tests/OpenAudioOrchestrator.Tests tests/oao.Tests
git mv tests/oao.Tests/OpenAudioOrchestrator.Tests.csproj tests/oao.Tests/oao.Tests.csproj
git mv OpenAudioOrchestrator.sln oao.sln
git status --short
```

Expected: status shows 5 `R` (rename) entries. No `??` (untracked) or `D` (delete) entries for these paths.

- [ ] **Step 2: Edit `oao.sln` — update Project() lines and inner paths**

Open `oao.sln`. Replace the two Project lines that reference `OpenAudioOrchestrator`:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "OpenAudioOrchestrator.Web", "src\OpenAudioOrchestrator.Web\OpenAudioOrchestrator.Web.csproj", "{A5AF7FB6-6A6B-4D61-B27F-73C504C1E5BA}"
```

becomes:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "oao.Web", "src\oao.Web\oao.Web.csproj", "{A5AF7FB6-6A6B-4D61-B27F-73C504C1E5BA}"
```

And:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "OpenAudioOrchestrator.Tests", "tests\OpenAudioOrchestrator.Tests\OpenAudioOrchestrator.Tests.csproj", "{276E363B-4CF3-4996-9399-E41EFCE6597C}"
```

becomes:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "oao.Tests", "tests\oao.Tests\oao.Tests.csproj", "{276E363B-4CF3-4996-9399-E41EFCE6597C}"
```

GUIDs are unchanged.

Verification:

```powershell
Select-String -Path oao.sln -Pattern 'OpenAudioOrchestrator' -SimpleMatch
```

Expected: no matches.

- [ ] **Step 3: Edit `tests/oao.Tests/oao.Tests.csproj` — update `<ProjectReference>` path**

Inside `tests/oao.Tests/oao.Tests.csproj`, replace:

```xml
<ProjectReference Include="..\..\src\OpenAudioOrchestrator.Web\OpenAudioOrchestrator.Web.csproj" />
```

with:

```xml
<ProjectReference Include="..\..\src\oao.Web\oao.Web.csproj" />
```

Verification:

```powershell
Select-String -Path tests/oao.Tests/oao.Tests.csproj -Pattern 'OpenAudioOrchestrator' -SimpleMatch
```

Expected: no matches.

- [ ] **Step 4: Mass-rewrite namespaces and `using` lines in all `.cs` files**

PowerShell one-shot. This replaces ONLY the two namespace prefixes — `OpenAudioOrchestrator.Web` and `OpenAudioOrchestrator.Tests` — and leaves any other occurrence of the string alone. It uses `-Raw` to preserve line endings (LF per repo `.gitattributes`).

```powershell
$paths = @('src/oao.Web', 'tests/oao.Tests')
foreach ($p in $paths) {
    Get-ChildItem $p -Recurse -Include *.cs -File | ForEach-Object {
        $content = Get-Content $_.FullName -Raw
        $new = $content `
            -replace 'OpenAudioOrchestrator\.Web', 'oao.Web' `
            -replace 'OpenAudioOrchestrator\.Tests', 'oao.Tests'
        if ($new -ne $content) {
            [System.IO.File]::WriteAllText($_.FullName, $new, [System.Text.UTF8Encoding]::new($false))
        }
    }
}
```

Why this regex shape: `OpenAudioOrchestrator.Web` is a strict prefix match. It hits `namespace OpenAudioOrchestrator.Web;`, `using OpenAudioOrchestrator.Web.Data;`, fully-qualified type refs like `OpenAudioOrchestrator.Web.StringHelpers.X`, and so on. It does NOT hit `"OpenAudioOrchestrator"` (the literal string used as config-section key etc.) because those are bare strings without `.Web` or `.Tests` after. Those literals stay for Task 2 and Task 3.

Note: `[System.IO.File]::WriteAllText` with `UTF8Encoding(false)` writes UTF-8 without BOM, matching what `Get-Content -Raw` reads back. Avoids `Set-Content` adding a BOM under PowerShell 5.x.

Verification:

```powershell
Get-ChildItem src/oao.Web, tests/oao.Tests -Recurse -Include *.cs -File |
    Select-String -Pattern 'OpenAudioOrchestrator\.(Web|Tests)' -SimpleMatch:$false |
    Select-Object -First 5
```

Expected: no matches.

- [ ] **Step 5: Mass-rewrite `@using` lines in all `.razor` files**

```powershell
Get-ChildItem src/oao.Web -Recurse -Include *.razor -File | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $new = $content `
        -replace 'OpenAudioOrchestrator\.Web', 'oao.Web' `
        -replace 'OpenAudioOrchestrator\.Tests', 'oao.Tests'
    if ($new -ne $content) {
        [System.IO.File]::WriteAllText($_.FullName, $new, [System.Text.UTF8Encoding]::new($false))
    }
}
```

Same shape as Step 4 but for `.razor` files (which use `@using` instead of `using`).

Verification:

```powershell
Get-ChildItem src/oao.Web -Recurse -Include *.razor -File |
    Select-String -Pattern 'OpenAudioOrchestrator\.(Web|Tests)' -SimpleMatch:$false |
    Select-Object -First 5
```

Expected: no matches.

- [ ] **Step 6: Verify no namespace residue across the entire tree**

```powershell
git grep -E 'OpenAudioOrchestrator\.(Web|Tests)' -- 'src/' 'tests/'
```

Expected: empty output (no hits).

If any hits remain, edit them individually before continuing.

- [ ] **Step 7: Build**

```powershell
dotnet build oao.sln
```

Expected: build succeeds with no errors. Warnings about unused usings or analyzer style nits are fine.

If the build fails — most likely cause is a `.razor.cs` file with a stale namespace ref or a `.razor` file's `@inherits` referencing a stale namespace. Look at the error message, fix that file, rebuild.

- [ ] **Step 8: Run tests**

```powershell
dotnet test oao.sln --logger "console;verbosity=normal"
```

Expected: same 3 pre-existing rate-limited auth failures as the baseline from Task 0 Step 8. Everything else passes.

If new failures appear, they're rename-induced. Most likely cause: an `[InlineData]` attribute or a test that referenced a fully-qualified type name like `OpenAudioOrchestrator.Web.Foo`. Grep + fix + rerun.

- [ ] **Step 9: Stage and commit**

```powershell
git add -A
git status --short
git commit -m "chore(rename): project folders, csproj, sln, namespaces"
git log -1 --stat | Select-Object -First 20
```

Expected: commit succeeds. `git log` shows 5 renames (R100) plus many M (modified) entries for the .cs/.razor files whose namespace lines changed.

---

## Task 2: Commit 2 — config section `OpenAudioOrchestrator` → `oao`

**Goal:** Rename the runtime configuration section key (top-level `"OpenAudioOrchestrator": { ... }` in `appsettings.json` and every code site that reads from it) to `"oao"`. After this commit, the app binds config from the `oao` section; old appsettings overrides keyed on `OpenAudioOrchestrator` will silently miss.

**Files:**
- Modify: `src/oao.Web/appsettings.json` (top-level key)
- Modify: `src/oao.Web/Program.cs` (4 sites)
- Modify: `src/oao.Web/Services/AcmeCertificateService.cs` (3 sites)
- Modify: `src/oao.Web/Services/SetupSettingsService.cs` (1 site)
- Modify: `src/oao.Web/Components/Pages/Admin/AdminSettings.razor` (3 sites)
- Modify (test): `tests/oao.Tests/Auth/AdminSeedServiceTests.cs` (2 sites)
- Modify (test): `tests/oao.Tests/Integration/CustomWebApplicationFactory.cs` (2 sites)
- Modify (test): `tests/oao.Tests/Services/ContainerConfigServiceTests.cs` (5 sites)
- Modify (test): `tests/oao.Tests/Services/DockerNetworkServiceTests.cs` (1 site)
- Modify (test): `tests/oao.Tests/Services/HealthMonitorServiceTests.cs` (1 site)
- Modify (test): `tests/oao.Tests/Services/TtsJobProcessorTests.cs` (1 site)
- Modify (test): `tests/oao.Tests/Services/VoiceLibraryServiceTests.cs` (1 site)
- Modify (test): `tests/oao.Tests/SignalR/HealthMonitorHubTests.cs` (1 site)

- [ ] **Step 1: Edit `src/oao.Web/appsettings.json` — rename top-level key**

Replace:

```json
"OpenAudioOrchestrator": {
```

with:

```json
"oao": {
```

The `"oao"` key value (the nested object) is unchanged. Only the top-level key name changes. The `"DockerNetworkName": "oao-network"` inside that section is already lowercase and untouched.

- [ ] **Step 2: Edit `src/oao.Web/Program.cs` — rewrite 4 config-indexer sites**

Per spec, lines 23, 64, 78, 149 use the pattern `Configuration["OpenAudioOrchestrator:Key"]`. Rewrite each:

- Line 23: `Configuration["OpenAudioOrchestrator:Domain"]` → `Configuration["oao:Domain"]`
- Line 64: `Configuration["OpenAudioOrchestrator:DataRoot"]` → `Configuration["oao:DataRoot"]`
- Line 78: `Configuration["OpenAudioOrchestrator:DatabaseKey"]` → `Configuration["oao:DatabaseKey"]`
- Line 149: `Configuration["OpenAudioOrchestrator:DockerEndpoint"]` → `Configuration["oao:DockerEndpoint"]`

Verification:

```powershell
Select-String -Path src/oao.Web/Program.cs -Pattern 'OpenAudioOrchestrator:' -SimpleMatch
```

Expected: no matches.

- [ ] **Step 3: Edit `src/oao.Web/Services/AcmeCertificateService.cs` — rewrite 3 config-indexer sites**

- Line 49: `_config["OpenAudioOrchestrator:DataRoot"]` → `_config["oao:DataRoot"]`
- Line 150: `_config["OpenAudioOrchestrator:Domain"]!` → `_config["oao:Domain"]!`
- Line 152: `_config["OpenAudioOrchestrator:DataRoot"]!` → `_config["oao:DataRoot"]!`

Verification:

```powershell
Select-String -Path src/oao.Web/Services/AcmeCertificateService.cs -Pattern 'OpenAudioOrchestrator' -SimpleMatch
```

Expected: no matches.

- [ ] **Step 4: Edit `src/oao.Web/Services/SetupSettingsService.cs:54` — rename the JSON-object indexer**

Replace:

```csharp
var oao = root["OpenAudioOrchestrator"]!;
```

with:

```csharp
var oao = root["oao"]!;
```

The local variable name `oao` is unchanged.

- [ ] **Step 5: Edit `src/oao.Web/Components/Pages/Admin/AdminSettings.razor` — rewrite 3 config-related literals**

- Line 46 (read): `_fqdn = Config["OpenAudioOrchestrator:Domain"] ?? "";` → `_fqdn = Config["oao:Domain"] ?? "";`
- Line 71 (write-time string comparison): `if (prop.Name == "OpenAudioOrchestrator")` → `if (prop.Name == "oao")`
- Line 73 (JSON write): `writer.WriteStartObject("OpenAudioOrchestrator");` → `writer.WriteStartObject("oao");`

Verification:

```powershell
Select-String -Path src/oao.Web/Components/Pages/Admin/AdminSettings.razor -Pattern 'OpenAudioOrchestrator' -SimpleMatch
```

Expected: no matches.

- [ ] **Step 6: Rewrite test-config literals across 8 test files**

Each of these test files sets keys like `["OpenAudioOrchestrator:DataRoot"] = ...` inside an in-memory config dictionary. Replace `OpenAudioOrchestrator:` with `oao:` in each. PowerShell one-shot:

```powershell
$testFiles = @(
    'tests/oao.Tests/Auth/AdminSeedServiceTests.cs',
    'tests/oao.Tests/Integration/CustomWebApplicationFactory.cs',
    'tests/oao.Tests/Services/ContainerConfigServiceTests.cs',
    'tests/oao.Tests/Services/DockerNetworkServiceTests.cs',
    'tests/oao.Tests/Services/HealthMonitorServiceTests.cs',
    'tests/oao.Tests/Services/TtsJobProcessorTests.cs',
    'tests/oao.Tests/Services/VoiceLibraryServiceTests.cs',
    'tests/oao.Tests/SignalR/HealthMonitorHubTests.cs'
)
foreach ($f in $testFiles) {
    $content = Get-Content $f -Raw
    $new = $content -replace 'OpenAudioOrchestrator:', 'oao:'
    if ($new -ne $content) {
        [System.IO.File]::WriteAllText($f, $new, [System.Text.UTF8Encoding]::new($false))
    }
}
```

Verification:

```powershell
git grep 'OpenAudioOrchestrator:' -- 'tests/'
```

Expected: empty output.

- [ ] **Step 7: Repo-wide sweep — confirm zero `OpenAudioOrchestrator:` residue**

```powershell
git grep 'OpenAudioOrchestrator:' -- 'src/' 'tests/'
```

Expected: empty output.

If any hits remain (other than in `docs/superpowers/plans/` or `docs/superpowers/specs/` — those are historical and intentionally preserved until Task 6), edit those files individually before continuing.

- [ ] **Step 8: Build**

```powershell
dotnet build oao.sln
```

Expected: build succeeds. Warnings ok.

- [ ] **Step 9: Run tests**

```powershell
dotnet test oao.sln --logger "console;verbosity=normal"
```

Expected: same 3 pre-existing rate-limited auth failures, everything else passes. If anything else fails — most likely cause is a missed `OpenAudioOrchestrator:` literal somewhere; grep + fix + rerun.

- [ ] **Step 10: Smoke-run the app**

```powershell
dotnet run --project src/oao.Web -c Release
```

In another terminal, open `http://localhost:5206` in a browser. Walk to Admin Settings (after logging in if you have a local DB; or accept first-run setup). Confirm:
- Page loads without throwing
- Port range field is populated from config (`9001`-`9099`)
- Health-check interval field shows `30`
- Default Docker image tag field shows `fishaudio/fish-speech:server-cuda-v2.0.0-beta`

These four values are sourced from the `oao:` section in appsettings — if the section rename missed a site, they'd be blank or zero.

Stop the app (`Ctrl+C`).

- [ ] **Step 11: Stage and commit**

```powershell
git add -A
git status --short
git commit -m "chore(rename): config section OpenAudioOrchestrator -> oao"
```

Expected: commit succeeds.

---

## Task 3: Commit 3 — runtime defaults (paths, DB filename, cookie, DP)

**Goal:** Rename the runtime/operational identifier strings: default install paths, DB filename, cookie name, Data-Protection application name (×2 sites), DP cert CN, TOTP issuer (now `"Open Audio Orchestrator"` with spaces).

**Files:**
- Modify: `src/oao.Web/PlatformDefaults.cs` (DataRoot paths, DbPath filename)
- Modify: `src/oao.Web/Program.cs` (cookie name, DP app name ×2, cert CN)
- Modify: `src/oao.Web/Components/Pages/Setup.razor` (DB filename literal, TOTP issuer)
- Modify (test): `tests/oao.Tests/Auth/TotpServiceTests.cs` (TOTP issuer literal ×3)
- Modify (test): `tests/oao.Tests/PlatformDefaultsTests.cs` (DB filename `Assert.EndsWith` literal)

- [ ] **Step 1: Edit `src/oao.Web/PlatformDefaults.cs` — rename DataRoot defaults and DbPath literal**

The full method block currently reads (line 4-6 + the DbPath at lines 8-9):

```csharp
public static string DataRoot =>
    OperatingSystem.IsWindows() ? @"C:\MyOpenAudioProj" : "/opt/OpenAudioOrchestrator";

public static string DbPath =>
    Path.Combine(DataRoot, "AudioOrchestrator.db");
```

Replace with:

```csharp
public static string DataRoot =>
    OperatingSystem.IsWindows() ? @"C:\oao" : "/opt/oao";

public static string DbPath =>
    Path.Combine(DataRoot, "oao.db");
```

Note: `DbPath` will be refactored further in Task 4 to derive from a new `DbFileName` property. Here we just change the literal.

- [ ] **Step 2: Edit `src/oao.Web/Program.cs` — cookie name, DP app name (×2), cert CN**

- Line 131: `opts.Cookie.Name = ".OAO.Auth";` → `opts.Cookie.Name = ".oao.Auth";`
- Line 69: `.SetApplicationName("OpenAudioOrchestrator")` → `.SetApplicationName("oao")`
- Line 85: `.SetApplicationName("OpenAudioOrchestrator")` → `.SetApplicationName("oao")`
- Line 263: `"CN=OpenAudioOrchestrator-DataProtection"` → `"CN=oao-DataProtection"`

Verification:

```powershell
Select-String -Path src/oao.Web/Program.cs -Pattern '(OpenAudioOrchestrator|OAO\.Auth)' -SimpleMatch:$false
```

Expected: no matches.

- [ ] **Step 3: Edit `src/oao.Web/Components/Pages/Setup.razor` — DB filename literal and TOTP issuer**

- Line 395: `private string _dbFileName = "AudioOrchestrator.db";` → `private string _dbFileName = "oao.db";`
- Line 696: `await TotpService.GenerateSetupInfoAsync(user, "OpenAudioOrchestrator");` → `await TotpService.GenerateSetupInfoAsync(user, "Open Audio Orchestrator");`

Note: the TOTP issuer is rendered with spaces (`"Open Audio Orchestrator"`) — this is the user-visible label inside authenticator apps.

The dev-restart code snippet at line 368 will be removed in Task 4. Don't touch it here.

Verification of just these 2 sites:

```powershell
Select-String -Path src/oao.Web/Components/Pages/Setup.razor -Pattern '(AudioOrchestrator\.db|"OpenAudioOrchestrator")' -SimpleMatch:$false |
    Select-Object LineNumber, Line
```

Expected: zero matches.

- [ ] **Step 4: Edit `tests/oao.Tests/Auth/TotpServiceTests.cs` — 3 TOTP issuer literals**

Lines 43, 95, 121 each have:

```csharp
var (manualKey, ...) = await service.GenerateSetupInfoAsync(user, "OpenAudioOrchestrator");
```

Replace `"OpenAudioOrchestrator"` with `"Open Audio Orchestrator"` (with spaces) at all 3 sites.

PowerShell:

```powershell
$file = 'tests/oao.Tests/Auth/TotpServiceTests.cs'
$content = Get-Content $file -Raw
$new = $content -replace '"OpenAudioOrchestrator"', '"Open Audio Orchestrator"'
[System.IO.File]::WriteAllText($file, $new, [System.Text.UTF8Encoding]::new($false))
```

Verification:

```powershell
Select-String -Path 'tests/oao.Tests/Auth/TotpServiceTests.cs' -Pattern '"OpenAudioOrchestrator"' -SimpleMatch
```

Expected: zero matches.

- [ ] **Step 5: Edit `tests/oao.Tests/PlatformDefaultsTests.cs:20` — DB filename literal**

Replace:

```csharp
Assert.EndsWith("AudioOrchestrator.db", result);
```

with:

```csharp
Assert.EndsWith("oao.db", result);
```

This assertion is inside the existing `DbPath_ContainsDataRoot` test method.

- [ ] **Step 6: Build**

```powershell
dotnet build oao.sln
```

Expected: build succeeds.

- [ ] **Step 7: Run tests**

```powershell
dotnet test oao.sln --logger "console;verbosity=normal"
```

Expected: same 3 pre-existing rate-limited auth failures. `PlatformDefaultsTests.DbPath_ContainsDataRoot` passes (now asserts on `"oao.db"`). `TotpServiceTests` tests pass (now use `"Open Audio Orchestrator"`).

- [ ] **Step 8: Smoke-run the app — verify the rename took effect**

```powershell
dotnet run --project src/oao.Web -c Release
```

In a browser:

- The Setup wizard should appear (because the old DB at `C:\MyOpenAudioProj\AudioOrchestrator.db` is no longer found at the new default `C:\oao\oao.db`).
- On Step 1 of the wizard, the "Database filename" field default should display `oao.db`.
- Complete the wizard if you want (creates `C:\oao\oao.db` with a new admin); or just close the browser.

After confirming, stop the app (`Ctrl+C`).

Optional cleanup: `Remove-Item C:\MyOpenAudioProj -Recurse -Force` to delete the orphaned old data dir. Not required.

- [ ] **Step 9: Stage and commit**

```powershell
git add -A
git status --short
git commit -m "chore(rename): runtime defaults (paths, DB filename, cookie, DP)"
```

Expected: commit succeeds.

---

## Task 4: Commit 4 — refactor Setup wizard (centralize DB filename, drop dev-restart snippet)

**Goal:** Two refactors, both targeting `Setup.razor`:
1. Add a new `PlatformDefaults.DbFileName` static property; refactor `DbPath` to derive from it; refactor `Setup.razor` to read its default from the property instead of repeating the literal.
2. Remove the hardcoded `<pre>dotnet run --project src/...</pre>` snippet from the final-step restart card. Restart guidance becomes mode-agnostic.

**Files:**
- Modify: `src/oao.Web/PlatformDefaults.cs` (new property + DbPath refactor)
- Modify: `src/oao.Web/Components/Pages/Setup.razor` (line 395 + lines 365-378)
- Modify (test): `tests/oao.Tests/PlatformDefaultsTests.cs` (add new test method)

- [ ] **Step 1: Write the failing test for the new `DbFileName` property**

Add this new test method to `tests/oao.Tests/PlatformDefaultsTests.cs`, immediately after the existing `DbPath_ContainsDataRoot` test:

```csharp
[Fact]
public void DbFileName_IsOaoDb()
{
    Assert.Equal("oao.db", PlatformDefaults.DbFileName);
}
```

The test file's `using` block should already include `using oao.Web;` (it referenced `PlatformDefaults.DataRoot` and `PlatformDefaults.DbPath` before — same namespace).

- [ ] **Step 2: Run the new test to confirm it fails (property doesn't exist yet)**

```powershell
dotnet test oao.sln --filter "FullyQualifiedName~PlatformDefaultsTests.DbFileName_IsOaoDb"
```

Expected: build fails with "PlatformDefaults does not contain a definition for 'DbFileName'" — that's the failure we want.

- [ ] **Step 3: Add the `DbFileName` property to `PlatformDefaults.cs` and refactor `DbPath` to derive from it**

In `src/oao.Web/PlatformDefaults.cs`, the current block is:

```csharp
public static string DataRoot =>
    OperatingSystem.IsWindows() ? @"C:\oao" : "/opt/oao";

public static string DbPath =>
    Path.Combine(DataRoot, "oao.db");
```

Replace with:

```csharp
public static string DataRoot =>
    OperatingSystem.IsWindows() ? @"C:\oao" : "/opt/oao";

public static string DbFileName => "oao.db";

public static string DbPath =>
    Path.Combine(DataRoot, DbFileName);
```

`DbFileName` is now the single source of truth for the SQLite filename; `DbPath` derives from it.

- [ ] **Step 4: Run the new test (and the existing `DbPath_ContainsDataRoot` test) to confirm they pass**

```powershell
dotnet test oao.sln --filter "FullyQualifiedName~PlatformDefaultsTests"
```

Expected: all `PlatformDefaultsTests` tests pass — including the new `DbFileName_IsOaoDb` and the existing `DbPath_ContainsDataRoot` (whose `Assert.EndsWith("oao.db", result)` still holds because `DbPath = Path.Combine(DataRoot, "oao.db")` after the refactor).

- [ ] **Step 5: Refactor `Setup.razor:395` to read the property**

Replace:

```csharp
private string _dbFileName = "oao.db";
```

with:

```csharp
private string _dbFileName = PlatformDefaults.DbFileName;
```

Verification — the wizard's "Step 1" default-filename behavior is unchanged; only the literal source is centralized.

- [ ] **Step 6: Drop the dev-restart snippet from `Setup.razor:365-378`**

The current card content (line 365-378 — embedded inside the wizard's "you're done" step):

```razor
                    <div class="card border-secondary mb-3">
                        <div class="card-body">
                            <p>Stop the application (<code>Ctrl+C</code> in the terminal) and restart with:</p>
                            <pre class="text-info p-2 rounded">dotnet run --project src/oao.Web</pre>
                            @if (!string.IsNullOrWhiteSpace(_domain))
                            {
                                <p>Then navigate to <strong>https://@_domain</strong></p>
                            }
                            else
                            {
                                <p>Then navigate to <strong>http://localhost:5206</strong></p>
                            }
                        </div>
                    </div>
```

(Note: the `src/oao.Web` part was already updated by Task 1's namespace rewrite sweep, since `OpenAudioOrchestrator.Web` matched the prefix regex inside the literal. If the original was somehow missed, double-check before this step.)

Replace the entire card body with a single sentence that does not show the `dotnet run` command:

```razor
                    <div class="card border-secondary mb-3">
                        <div class="card-body">
                            @if (!string.IsNullOrWhiteSpace(_domain))
                            {
                                <p>Restart the application, then navigate to <strong>https://@_domain</strong>.</p>
                            }
                            else
                            {
                                <p>Restart the application, then navigate to <strong>http://localhost:5206</strong>.</p>
                            }
                        </div>
                    </div>
```

Rationale: this UI is mode-agnostic (works for dev-from-clone, Velopack, Docker — all of which can be "restarted" by the user). The brittle command snippet is gone.

- [ ] **Step 7: Build**

```powershell
dotnet build oao.sln
```

Expected: success.

- [ ] **Step 8: Run all tests**

```powershell
dotnet test oao.sln --logger "console;verbosity=normal"
```

Expected: same 3 pre-existing rate-limited auth failures (Task 5 fixes those). All other tests pass, including the new `DbFileName_IsOaoDb` test.

- [ ] **Step 9: UI smoke**

```powershell
dotnet run --project src/oao.Web -c Release
```

In the browser, walk the Setup wizard to its final step. Confirm:
- "Step 1: Storage" → "Database filename" field defaults to `oao.db` (same as before, but now sourced from `PlatformDefaults.DbFileName` rather than a literal).
- Final step's restart card shows the new "Restart the application, then navigate to..." sentence — NO `dotnet run` command snippet visible.

Stop the app (`Ctrl+C`).

- [ ] **Step 10: Stage and commit**

```powershell
git add -A
git status --short
git commit -m "refactor(setup): centralize DB filename, drop dev-restart snippet"
```

Expected: commit succeeds.

---

## Task 5: Commit 5 — neutralize rate limiter in test factory

**Goal:** Override the production `RateLimiterOptions` inside `CustomWebApplicationFactory` so every endpoint, including the auth endpoints decorated with `RequireRateLimiting("auth")`, gets unlimited capacity in tests. The 3 pre-existing rate-limit-induced flaky tests then become deterministic green.

**Files:**
- Modify: `tests/oao.Tests/Integration/CustomWebApplicationFactory.cs` (add `RateLimiterOptions` override inside `ConfigureWebHost`)

- [ ] **Step 1: Confirm the 3 baseline failures still exist**

```powershell
dotnet test oao.sln --logger "console;verbosity=normal" 2>&1 |
    Select-String -Pattern '(Failed|Passed!|FAIL)' |
    Select-Object -First 10
```

Expected: 3 failures. They should match the baseline captured in Task 0 Step 8. These are the tests we're about to fix.

- [ ] **Step 2: Edit `tests/oao.Tests/Integration/CustomWebApplicationFactory.cs` — add `RateLimiterOptions` override**

Open the file. Find the existing `ConfigureWebHost(IWebHostBuilder builder)` method (or `ConfigureServices`/`ConfigureAppConfiguration` — whichever the factory uses to set up the test host).

At the top of the file, add these `using` statements if not present:

```csharp
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
```

Inside `ConfigureWebHost`, add the following `ConfigureServices` block. If the method already has a `builder.ConfigureServices(...)` call, append this `Configure<RateLimiterOptions>(...)` line inside the same lambda; otherwise add a new `builder.ConfigureServices(...)` invocation:

```csharp
builder.ConfigureServices(services =>
{
    services.Configure<RateLimiterOptions>(opts =>
    {
        opts.GlobalLimiter = PartitionedRateLimiter.CreateChained(
            PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetNoLimiter<string>("unlimited")));
    });
});
```

Add a comment immediately above explaining the override:

```csharp
// Neutralize the production rate limiter for test runs. Auth endpoints
// decorated with RequireRateLimiting("auth") get unlimited capacity here.
// Production code is unchanged. If a future test needs to exercise rate-limit
// behavior, it should subclass this factory and skip applying this override.
```

- [ ] **Step 3: Build**

```powershell
dotnet build oao.sln
```

Expected: success.

- [ ] **Step 4: Run tests — the 3 baseline failures should now be green**

```powershell
dotnet test oao.sln --logger "console;verbosity=normal"
```

Expected: **zero failures**. Total test count is the same as before (we didn't add or remove tests); the 3 that previously failed now pass.

If they still fail — possible causes:
- Wrong override site (verify the `Configure<RateLimiterOptions>` is inside `ConfigureServices` callback of the test builder)
- Production `AddRateLimiter(...)` may set options via a different pattern that overrides `Configure<>` — check Program.cs to confirm
- An `IClassFixture` test fixture variant might bypass `CustomWebApplicationFactory` — find such tests and apply the override there too

- [ ] **Step 5: Stage and commit**

```powershell
git add -A
git status --short
git commit -m "test: disable rate limiter in test factory"
```

Expected: commit succeeds.

---

## Task 6: Commit 6 — docs refresh + freeze historical superpowers docs

**Goal:** Refresh `README.md`, `docs/LINUX-SETUP.md`, `docs/WINDOWS-SETUP.md`, add a new `[Unreleased]` entry to `CHANGELOG.md`, and add a "post-rename" header note to each of 17 historical docs.

**Files:**
- Modify: `README.md` (multiple lines per spec)
- Modify: `docs/LINUX-SETUP.md` (sweep)
- Modify: `docs/WINDOWS-SETUP.md` (sweep)
- Modify: `CHANGELOG.md` (new `[Unreleased]` section)
- Modify: 17 historical docs under `docs/superpowers/` and `docs/audit-report.md` (add header note)

- [ ] **Step 1: Edit `README.md` — full rename sweep**

Apply these exact edits per spec:

- Line 2 (logo img): `src="src/OpenAudioOrchestrator.Web/wwwroot/logo.png"` → `src="src/oao.Web/wwwroot/logo.png"`
- Line 53 (git clone URL): `https://github.com/bilbospocketses/OpenAudioOrchestrator.git` → `https://github.com/bilbospocketses/oao.git`
- Line 54 (cd command): `cd OpenAudioOrchestrator` → `cd oao`
- Line 55 (dotnet run path): `--project src/OpenAudioOrchestrator.Web` → `--project src/oao.Web`
- Line 68 (path mention in prose): `src/OpenAudioOrchestrator.Web/appsettings.json` → `src/oao.Web/appsettings.json`
- Lines 73-83 (config-key table): every `OpenAudioOrchestrator:Key` → `oao:Key` (single mass-replace)
- Line 88 (env-var prose): `OpenAudioOrchestrator__AdminUser` and `OpenAudioOrchestrator__AdminPassword` → `oao__AdminUser` and `oao__AdminPassword`

PowerShell mass-replace (covers all of the above patterns):

```powershell
$content = Get-Content README.md -Raw
$new = $content `
    -replace 'src/OpenAudioOrchestrator\.Web', 'src/oao.Web' `
    -replace 'OpenAudioOrchestrator\.git', 'oao.git' `
    -replace '(?<![\w./])cd OpenAudioOrchestrator(?![\w.])', 'cd oao' `
    -replace 'OpenAudioOrchestrator:', 'oao:' `
    -replace 'OpenAudioOrchestrator__', 'oao__'
[System.IO.File]::WriteAllText('README.md', $new, [System.Text.UTF8Encoding]::new($false))
```

Verification:

```powershell
Select-String -Path README.md -Pattern 'OpenAudioOrchestrator' -SimpleMatch
```

Expected: zero matches. If any remain (e.g., a stray reference in a paragraph), edit manually.

- [ ] **Step 2: Edit `docs/LINUX-SETUP.md` and `docs/WINDOWS-SETUP.md` — full rename sweep**

```powershell
foreach ($f in @('docs/LINUX-SETUP.md', 'docs/WINDOWS-SETUP.md')) {
    $content = Get-Content $f -Raw
    $new = $content `
        -replace 'src/OpenAudioOrchestrator\.Web', 'src/oao.Web' `
        -replace '/opt/OpenAudioOrchestrator', '/opt/oao' `
        -replace 'C:\\MyOpenAudioProj', 'C:\\oao' `
        -replace 'OpenAudioOrchestrator\.git', 'oao.git' `
        -replace '(?<![\w./])cd OpenAudioOrchestrator(?![\w.])', 'cd oao' `
        -replace 'OpenAudioOrchestrator:', 'oao:' `
        -replace 'OpenAudioOrchestrator__', 'oao__' `
        -replace 'OpenAudioOrchestrator\.Web', 'oao.Web'
    [System.IO.File]::WriteAllText($f, $new, [System.Text.UTF8Encoding]::new($false))
}
```

Verification:

```powershell
git grep 'OpenAudioOrchestrator' -- docs/LINUX-SETUP.md docs/WINDOWS-SETUP.md
```

Expected: empty.

Note: User-facing branding "Open Audio Orchestrator" (with spaces) in these docs is preserved by these replacements (the regexes only match the no-space form).

- [ ] **Step 3: Add the `[Unreleased]` section at the top of `CHANGELOG.md`**

Open `CHANGELOG.md`. Add this block at the top, immediately after the file's "# Changelog" header (and any existing `## [Unreleased]` placeholder; if one exists, replace it):

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

Existing historical CHANGELOG entries below this point are NOT rewritten.

- [ ] **Step 4: Add the "post-rename" note to each historical doc**

For each of the 17 historical files below, add this note as the first content line immediately after any front-matter (or at the very top if no front-matter):

```markdown
> **Note:** post-rename. References to `OpenAudioOrchestrator.*` paths and
> config keys reflect their original names. Current equivalents are
> `oao.*` and `oao:*`.
```

Files (full inventory at brainstorm time):

1. `docs/superpowers/plans/2026-03-29-phase4-security-auth.md`
2. `docs/superpowers/plans/2026-03-29-phase5-signalr-dashboard.md`
3. `docs/superpowers/plans/2026-03-30-event-bus-refactor.md`
4. `docs/superpowers/plans/2026-03-30-phase6-polish-readme.md`
5. `docs/superpowers/plans/2026-03-31-setup-wizard.md`
6. `docs/superpowers/plans/2026-03-31-tts-job-queue.md`
7. `docs/superpowers/plans/2026-04-01-audit-and-theme-plan.md`
8. `docs/superpowers/plans/2026-04-02-linux-compatibility.md`
9. `docs/superpowers/plans/2026-04-04-acme-replacement.md`
10. `docs/superpowers/specs/2026-03-29-phase4-security-auth-design.md`
11. `docs/superpowers/specs/2026-03-29-phase5-signalr-dashboard-design.md`
12. `docs/superpowers/specs/2026-03-30-phase6-polish-readme-design.md`
13. `docs/superpowers/specs/2026-03-31-setup-wizard-design.md`
14. `docs/superpowers/specs/2026-03-31-tts-job-queue-design.md`
15. `docs/superpowers/specs/2026-04-01-audit-and-theme-design.md`
16. `docs/superpowers/specs/2026-04-02-linux-compatibility-design.md`
17. `docs/superpowers/specs/2026-04-04-acme-replacement-design.md`

Plus: `docs/audit-report.md`

The new design doc at `docs/superpowers/specs/2026-05-15-rename-and-cleanup-to-oao-design.md` is NOT historical and does NOT receive the note. The new plan doc at `docs/superpowers/plans/2026-05-15-rename-and-cleanup-to-oao.md` likewise does NOT.

PowerShell helper to insert the note after the front-matter (if any), preserving existing content:

```powershell
$note = @"
> **Note:** post-rename. References to ``OpenAudioOrchestrator.*`` paths and
> config keys reflect their original names. Current equivalents are
> ``oao.*`` and ``oao:*``.

"@

$historicalDocs = @(
    'docs/superpowers/plans/2026-03-29-phase4-security-auth.md',
    'docs/superpowers/plans/2026-03-29-phase5-signalr-dashboard.md',
    'docs/superpowers/plans/2026-03-30-event-bus-refactor.md',
    'docs/superpowers/plans/2026-03-30-phase6-polish-readme.md',
    'docs/superpowers/plans/2026-03-31-setup-wizard.md',
    'docs/superpowers/plans/2026-03-31-tts-job-queue.md',
    'docs/superpowers/plans/2026-04-01-audit-and-theme-plan.md',
    'docs/superpowers/plans/2026-04-02-linux-compatibility.md',
    'docs/superpowers/plans/2026-04-04-acme-replacement.md',
    'docs/superpowers/specs/2026-03-29-phase4-security-auth-design.md',
    'docs/superpowers/specs/2026-03-29-phase5-signalr-dashboard-design.md',
    'docs/superpowers/specs/2026-03-30-phase6-polish-readme-design.md',
    'docs/superpowers/specs/2026-03-31-setup-wizard-design.md',
    'docs/superpowers/specs/2026-03-31-tts-job-queue-design.md',
    'docs/superpowers/specs/2026-04-01-audit-and-theme-design.md',
    'docs/superpowers/specs/2026-04-02-linux-compatibility-design.md',
    'docs/superpowers/specs/2026-04-04-acme-replacement-design.md',
    'docs/audit-report.md'
)

foreach ($f in $historicalDocs) {
    $content = Get-Content $f -Raw
    # Skip if note already present
    if ($content -match 'post-rename\. References to ``OpenAudioOrchestrator') {
        Write-Host "SKIP (note already present): $f"
        continue
    }
    # If file starts with front-matter (---), insert after the closing ---
    if ($content -match '^---\r?\n(?:.*\r?\n)*?---\r?\n') {
        $new = $content -replace '^(---\r?\n(?:.*\r?\n)*?---\r?\n)', "`$1`n$note"
    } else {
        $new = $note + $content
    }
    [System.IO.File]::WriteAllText($f, $new, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Added note: $f"
}
```

Verification:

```powershell
foreach ($f in $historicalDocs) {
    $hit = Select-String -Path $f -Pattern 'post-rename' -Quiet
    "$($hit): $f"
}
```

Expected: every entry shows `True`.

- [ ] **Step 5: Visual review**

Open each of: `README.md`, `docs/LINUX-SETUP.md`, `docs/WINDOWS-SETUP.md`, `CHANGELOG.md`, and 2-3 sampled historical docs. Confirm:
- README: no leftover `OpenAudioOrchestrator` strings; git-clone URL points to `bilbospocketses/oao`; config table keys all start with `oao:`.
- LINUX/WINDOWS setup: paths reference `oao` where appropriate; "Open Audio Orchestrator" (with spaces) preserved in prose.
- CHANGELOG: `[Unreleased]` section at the top, with Changed/Removed/Fixed sub-headings; existing entries below intact.
- Historical docs: each has the `post-rename` note at the top.

- [ ] **Step 6: Stage and commit**

```powershell
git add -A
git status --short
git commit -m "docs: README + setup docs + CHANGELOG; freeze historical superpowers docs"
```

Expected: commit succeeds.

---

## Task 7: Commit 7 — update memory store paths *(not a git commit; external to the repo)*

**Goal:** Update path references and build commands inside the memory store at `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/`. No git impact — this is a separate file system.

**Files:**
- Modify: `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/todo_oao.md`
- Modify: `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/project_oao.md`
- Verify (no changes expected): `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/project_index.md`

- [ ] **Step 1: Edit `todo_oao.md`**

Open `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/todo_oao.md`. Apply these edits:

- Line ~9 (source path): `C:/Users/jscha/source/repos/OpenAudioOrchestrator/` → `C:/Users/jscha/source/repos/oao/`
- Line ~10 (remote): `bilbospocketses/OpenAudioOrchestrator` → `bilbospocketses/oao`
- Lines ~17-20 (build commands block):
  - `cd C:/Users/jscha/source/repos/OpenAudioOrchestrator` → `cd C:/Users/jscha/source/repos/oao`
  - `dotnet build OpenAudioOrchestrator.sln` → `dotnet build oao.sln`
  - `dotnet test OpenAudioOrchestrator.sln` → `dotnet test oao.sln`
  - `dotnet run --project src/OpenAudioOrchestrator.Web -c Release` → `dotnet run --project src/oao.Web -c Release`
- Frontmatter `name:` (line 2): `OpenAudioOrchestrator TODOs` → `oao TODOs` (cosmetic)
- Frontmatter `description:` (line 3): minor update if it references the old name (cosmetic)

Verification:

```powershell
$file = 'C:/Users/jscha/.claude/projects/C--Users-jscha/memory/todo_oao.md'
Select-String -Path $file -Pattern 'OpenAudioOrchestrator' -SimpleMatch
```

Expected: zero matches (or matches only inside intentional historical narrative — the spec's CHANGELOG-style "shipped" entries may reference the old name when describing past work; those should remain accurate).

- [ ] **Step 2: Edit `project_oao.md`**

Open `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/project_oao.md`. Sweep for:
- Source path references → update to `oao`
- Remote URL references → update to `oao`
- Build command references → update to `oao.sln`, `src/oao.Web`

Keep human-readable references to "Open Audio Orchestrator" (the project's full name) intact — only operational/path identifiers change.

- [ ] **Step 3: Verify `project_index.md` doesn't need changes**

```powershell
Select-String -Path 'C:/Users/jscha/.claude/projects/C--Users-jscha/memory/project_index.md' -Pattern 'OpenAudioOrchestrator' -SimpleMatch
```

Expected: zero matches (the index entry text already uses "OAO (Open Audio Orchestrator)" and points to `project_oao.md` and `todo_oao.md` — all already correctly named).

If any unexpected matches appear, evaluate whether they're operational (rename) or human-readable (keep).

- [ ] **Step 4: No git commit**

The memory store is not in a git repo. No `git add`/`git commit`. Verify there's no `.git` folder at the memory-store root:

```powershell
Test-Path 'C:/Users/jscha/.claude/projects/C--Users-jscha/memory/.git'
```

Expected: `False`.

---

## Task 8: Post-branch — final validation, merge, push, cleanup

**Goal:** End-to-end validation of the full branch, then `--no-ff` merge to master, push, and delete the feature branch.

**Files:** No file edits. Git plumbing.

- [ ] **Step 1: Verify branch state**

```powershell
Set-Location C:\Users\jscha\source\repos\oao
git branch --show-current
git log --oneline master..HEAD
```

Expected: on `chore/rename-and-cleanup-to-oao`. 6 commits ahead of master (Tasks 1-6 each produced one commit; Task 7 had no git commit).

- [ ] **Step 2: Full clean build + test from scratch**

```powershell
git clean -fdx
dotnet restore oao.sln
dotnet build oao.sln -c Release
dotnet test oao.sln -c Release --logger "console;verbosity=normal"
```

Expected: build succeeds. ALL tests pass (zero failures — the 3 previously-flaky tests were fixed by Task 5).

- [ ] **Step 3: End-to-end smoke**

```powershell
dotnet run --project src/oao.Web -c Release
```

In a fresh browser window (incognito/private to avoid old `.OAO.Auth` cookie):

- Setup wizard appears.
- Step 1 — DataRoot defaults to `C:\oao` (Windows). DB filename defaults to `oao.db`.
- Complete the wizard. New admin login succeeds. TOTP enrollment shows issuer label "Open Audio Orchestrator".
- Dashboard loads. Admin Settings page loads. Config values bind correctly from the `oao:` section.
- Restart the app (Ctrl+C, then `dotnet run` again). Login persists via `.oao.Auth` cookie.
- The final-step Setup card (visible only on first-run setup; you may need to nuke `C:\oao` and re-run to see this) shows the new wording without the `dotnet run` snippet.

Stop the app (`Ctrl+C`).

- [ ] **Step 4: Switch to master and merge with `--no-ff`**

```powershell
git checkout master
git merge --no-ff chore/rename-and-cleanup-to-oao -m @'
Merge branch 'chore/rename-and-cleanup-to-oao'

Project rename to oao + setup-wizard cleanup + test-suite hardening.

- chore(rename) x3: project structure, config section, runtime defaults
- refactor(setup): centralize DB filename via PlatformDefaults.DbFileName;
  drop dev-restart snippet
- test: neutralize rate limiter in CustomWebApplicationFactory (3
  previously-flaky auth tests now deterministic)
- docs: README + setup docs + Keep-a-Changelog Unreleased entry; freeze
  historical superpowers docs with a top-of-file post-rename note

BREAKING: existing installs cannot upgrade in place. See CHANGELOG.
'@
```

Expected: merge succeeds. `git log --graph --oneline -10` shows the 6 branch commits as a visible bubble joining back to master via the merge commit.

- [ ] **Step 5: Final post-merge test pass**

```powershell
dotnet test oao.sln --logger "console;verbosity=normal"
```

Expected: zero failures.

- [ ] **Step 6: Push master**

```powershell
git push personal master
```

Expected: push succeeds.

- [ ] **Step 7: Delete the merged branch (local + remote)**

```powershell
git branch -d chore/rename-and-cleanup-to-oao
git push personal --delete chore/rename-and-cleanup-to-oao 2>$null
```

Note: the remote-delete will no-op silently if the branch was never pushed to `personal` (we worked locally throughout). That's fine.

- [ ] **Step 8: Sweep — ensure no remaining `OpenAudioOrchestrator` operational refs in the repo**

```powershell
git grep -i 'OpenAudioOrchestrator' | Where-Object {
    $_ -notmatch 'docs/superpowers/(plans|specs)/' -and
    $_ -notmatch 'docs/audit-report\.md' -and
    $_ -notmatch 'CHANGELOG\.md'
}
```

Expected: empty output. Hits inside historical docs and in CHANGELOG (where past entries describe historical work using the old name) are intentional and not a concern.

If unexpected hits remain, address them in a follow-up commit (don't amend the merged history).

- [ ] **Step 9: Update memory store status** *(memory-store, not repo)*

After the merge succeeds, update the project-index entry (if needed) at `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/project_index.md` to note the rename's completion date. Optional polish; not load-bearing.

---

## Defensive notes for agent execution

**Line-number drift.** Line numbers in this plan are accurate as of `master @ 083004d`. If the agent finds the file content has drifted (any commit landed on master between plan-write time and execution), do NOT trust the line numbers — find the target by grepping the exact "before" literal shown in the plan, and replace with the exact "after" literal. The pairs in this plan are anchored to literal content, not position. If the literal pattern itself has changed (e.g., the file was refactored), STOP and surface — that's a spec-vs-reality drift and needs the user.

**Mass-replace verification.** After every PowerShell mass-replace (Task 1 Steps 4-5, Task 2 Step 6, Task 6 Steps 1-2, Task 6 Step 4), the agent MUST inspect the diff before staging:

```powershell
git diff --stat       # quick: count files changed, lines +/-
git diff              # full: scroll through every hunk
```

Acceptable hunks for namespace mass-replace (Task 1):
- `namespace OpenAudioOrchestrator.Web` → `namespace oao.Web`
- `using OpenAudioOrchestrator.Web` → `using oao.Web`
- Fully-qualified type references like `OpenAudioOrchestrator.Web.X` (rare but legitimate)
- `@using OpenAudioOrchestrator.Web` → `@using oao.Web` (in .razor)
- Embedded path literals like `src/OpenAudioOrchestrator.Web/foo` inside UI prose (correctly want this renamed)

Unexpected hunks for namespace mass-replace — STOP and inspect:
- Inside a regular `string` literal that LOOKS like a path but is actually a config-binding or display string. (Cross-check against the spec's "branding preserved" section before assuming the rename is correct.)
- Inside an `[Obsolete]` or `[Description]` attribute that intentionally references the old name as historical.
- Inside a test that intentionally pins the old-vs-new-name behavior.

If unexpected hunks appear, do NOT blanket-accept — examine each one and decide per-line whether the rename is what we want.

**Build-and-test gates are non-negotiable.** Each task ends with a `dotnet build` + `dotnet test` step. The agent MUST NOT proceed to the next task if the gate fails. A half-applied rename across a commit boundary is much harder to recover than fixing the current commit before moving on.

**Test-failure-count comparison.** Tasks 1, 2, 3 all expect "same 3 pre-existing rate-limited failures, everything else passes". The agent should compare the failing-test names list against the baseline captured in Task 0 Step 8 — same names? Fine. Different names? Investigate.

## Notes on TDD for renames

Pure renames (Tasks 1, 2, 3) don't fit the classic RED → GREEN → REFACTOR pattern because we aren't introducing new behavior — we're preserving existing behavior under new names. The TDD-equivalent discipline applied here is:

- **Baseline check** (Task 0 Step 8): capture the test result *before* renaming. The 3 pre-existing rate-limited failures become a known-stable baseline.
- **Post-edit check** (each rename task's "Run tests" step): the same 3 failures should appear, and *no others*. Any *new* failure is a rename regression to be fixed immediately.
- **Tests that pin the new name** (Tasks 3 + 4 + 5): the `Assert.EndsWith("oao.db", ...)` change, the new `DbFileName_IsOaoDb` test, and the 3 previously-failing tests' return to green all pin the rename to a specific named outcome.

The refactors in Task 4 + Task 5 follow classic TDD (write failing test → implement → confirm pass → refactor) because they introduce real new behavior (a new property; a test-only override).

## Rollback recipes

If a task's commit causes a regression discovered post-commit but pre-merge:

```powershell
git reset --hard HEAD~1     # discards the bad commit
# fix the underlying issue, re-commit
```

If the branch is unsalvageable mid-flight:

```powershell
git checkout master
git branch -D chore/rename-and-cleanup-to-oao
# re-run pre-flight Task 0 from a clean state
```

If a problem is discovered post-merge to master:

```powershell
git revert -m 1 <merge-commit-sha>    # single revert undoes the entire 6-commit branch
git push personal master
```

`--no-ff` (Task 8 Step 4) is what makes the single-revert path clean.

## Implementation gating

Each task ends with a build + test gate. **Do not move to Task N+1 until Task N's gate passes.** If the gate is stuck, stop and surface the failure before continuing — a half-applied rename is worse than no rename.
