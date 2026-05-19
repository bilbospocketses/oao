# Security Policy

## Reporting a vulnerability

Please report security vulnerabilities **privately** via GitHub's private
vulnerability reporting:

1. Go to <https://github.com/bilbospocketses/oao/security>
2. Click **Report a vulnerability**
3. Fill in a short description, affected version (commit SHA or tag), and
   reproduction steps if you have them

This routes the report directly to the maintainer with no public exposure.

**Please do not** open a public GitHub issue, comment on a commit, or post
on a public discussion forum for security-affecting findings. Public
disclosure before a fix is in place puts other users at risk.

If you cannot use GitHub's private reporting flow for any reason, contact
the maintainer at the email address on their GitHub profile.

## Supported versions

oao is pre-stable — there is no released version yet. Security fixes are
applied to the tip of `master`. Phase tags `v0.1.0-phase1` through
`v0.6.0-phase6` are historical milestones, not maintained release branches;
do not run them in any setting where security matters.

| Version | Supported          |
| ------- | ------------------ |
| `master` (HEAD) | Yes — security fixes land here |
| Phase tags (`v0.x.0-phaseN`) | No — historical only |

## Response expectations

This is a personal open-source project maintained by one person. Best-effort
response targets:

- **Acknowledgement**: within 7 days of report
- **Initial triage**: within 14 days
- **Fix or mitigation guidance**: within 30 days for high-severity issues,
  longer for low-severity

If you'd like credit in the fix's release notes, say so in your report.

## In-scope

- The oao web application source code in this repository
- Default configuration shipped with the app (auth, CSP, rate limiting,
  Data Protection key storage, cookie settings, antiforgery handling)
- The Setup wizard flow
- The Let's Encrypt / ACME v2 client implementation
- The Docker container orchestration surface (how the app spawns and
  controls Fish Speech containers)

## Out of scope

- Vulnerabilities in upstream dependencies (.NET, Fish Speech, Docker,
  YARP, SQLite, etc.) — report those to the upstream project. If a CVE
  affects how oao uses an upstream component in a non-default way, that
  IS in scope.
- Operator misconfiguration (e.g., running oao with an insecure reverse
  proxy in front, disabling the auth wizard) unless oao's defaults or
  documentation actively steer the operator toward an unsafe choice.
- Self-XSS, social-engineering against the operator's own machine, or
  attacks requiring local privileged access to the host running oao.

## Existing security posture

The codebase has been through a comprehensive security audit (29/30
findings shipped — `docs/audit-report.md`) covering session fixation,
TOTP/MFA, antiforgery tokens, CSP headers (with one accepted
constraint — Blazor Server's `script-src 'unsafe-inline'` requirement,
tracked for follow-up), rate limiting, audio-file authorization, path
traversal prevention, and authenticated-only access to sensitive
endpoints. The Let's Encrypt integration uses an in-house ACME v2 client
(not the unmaintained LettuceEncrypt / Certes libraries).

Repository-side: Dependabot alerts + automated security updates, secret
scanning + push protection, CodeQL default setup (`csharp`), and private
vulnerability reporting are all enabled.
