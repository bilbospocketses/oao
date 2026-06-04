# Contributing to Open Audio Orchestrator (oao)

Thanks for your interest. This document covers the essentials for getting a
development environment running, the testing bar, and how to land changes.

## Prerequisites

- **.NET 10 SDK** — install from [dotnet.microsoft.com](https://dotnet.microsoft.com/download).
  The projects target `net10.0`, so the .NET 10 SDK is required; the .NET 9
  SDK cannot build them.
- **Docker Desktop** (Windows) or **Docker Engine** (Linux) — required at
  runtime. The app manages Fish Speech containers via the Docker SDK; the
  daemon must be reachable on the OS-default endpoint. The first run will
  pull the Fish Speech image (large; CUDA-enabled).
- **NVIDIA GPU + driver + Container Toolkit** — strongly recommended. Fish
  Speech inference is FP16; CPU-only inference is impractical for real use.
  Verified on an RTX 3060 (12 GB VRAM, single concurrent container).
- **A modern Chromium-based browser** (Chrome, Edge, Brave) for the Blazor
  Server UI. Firefox works but is less tested.

Optional but useful for development:

- **SQLite CLI** — for poking at the local DB outside the app.
- **gh CLI** + **git** with SSH commit signing configured (see
  [Repository workflow](#repository-workflow) below).

## Setup

```bash
git clone https://github.com/bilbospocketses/oao.git
cd oao
dotnet restore oao.sln
dotnet build oao.sln -c Release
dotnet run --project src/oao.Web -c Release
```

The dev server listens on `http://localhost:5206`. First run hits the
Setup wizard at `/setup` — walk through it to create the admin user and
configure DataRoot, then the Dashboard is reachable at `/`.

For Linux setup steps (NVIDIA Container Toolkit, systemd unit, RHEL/Fedora
SELinux notes), see `docs/LINUX-SETUP.md`.

## Development workflow

```bash
dotnet build oao.sln                   # incremental build
dotnet test oao.sln -c Release         # full test suite (xUnit, 210+ tests)
dotnet run --project src/oao.Web -c Release  # dev server on :5206
```

Tests should be all green at all times on `master`. If you find a flake,
log it as a TODO with reproduction notes — don't silently re-run.

The Setup wizard's "Data Storage" step picks the `DataRoot`. Defaults are
`C:\ProgramData\oao` on Windows and `/var/lib/oao` on Linux. You can also
set it via `oao:DataRoot` in `appsettings.json` or `oao__DataRoot` env var.
Integration tests inject a per-instance temp `DataRoot` automatically; you
don't need to configure it for `dotnet test`.

## Repository workflow

`master` is protected by the `Protect master` ruleset. All changes must
land via pull request:

1. **Branch** off `master` (`git switch -c feat/something`).
2. **Commit** locally. Commits must be signed — set up SSH commit signing
   per GitHub's docs and `git config --global commit.gpgsign true`.
3. **Push** your branch to `personal` / `origin`.
4. **Open a pull request** against `master`.
5. **Wait for CI** — the `build-and-test` job (windows-latest, dotnet 10.x)
   plus the `Analyze (csharp)` and `Analyze (actions)` CodeQL jobs must
   all go green before the PR is mergeable. Any new CodeQL alert
   introduced by the PR blocks merge until fixed or dismissed.
6. **Merge** via `gh pr merge --squash --delete-branch <N>` or the GitHub
   web UI's "Squash and merge" button. Squash is the only enabled merge
   method on this repo. The resulting commit is signed by GitHub's
   `web-flow` key (counts as a verified signature toward the
   `required_signatures` rule).

**Do not use `gh pr merge --rebase`** — the rebase API does NOT sign on
your behalf, leaving the merged commit Unverified and rejected by the
ruleset's `required_signatures` rule.

Direct push to `master` is blocked by the `pull_request` rule. Tag
creation/deletion on `v*` tags is restricted by the `Protect release tags`
ruleset (deletion blocked, force-update blocked, `required_signatures`
enforced — future release tags must be SSH-signed by the tagger).

## Reporting security issues

See [`SECURITY.md`](SECURITY.md) — use GitHub's private vulnerability
reporting, not a public issue.

## Reporting bugs and proposing features

Open an issue on the
[GitHub tracker](https://github.com/bilbospocketses/oao/issues) with a
reproduction recipe (for bugs) or a problem statement plus desired
outcome (for features).

## License

Contributions land under the project's GPL-3.0 license (see `LICENSE`).
