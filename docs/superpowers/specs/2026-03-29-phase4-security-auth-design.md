> **Note:** post-rename. References to `OpenAudioOrchestrator.*` paths and
> config keys reflect their original names. Current equivalents are
> `oao.*` and `oao:*`.
# Phase 4: Security & Authentication Design

## Overview

Add authentication and authorization to the Fish Audio Orchestration Dashboard using ASP.NET Identity with mandatory TOTP/MFA. The app currently has no auth — all pages are publicly accessible. This phase locks down the app for a small team (2-5 users) with role-based access control.

## Data Model & Identity Integration

Extend `AppDbContext` to inherit from `IdentityDbContext<AppUser, IdentityRole, string>` instead of plain `DbContext`. This adds Identity tables (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserTokens`, etc.) alongside existing tables via a single EF Core migration. No data loss to existing tables.

### AppUser Entity

Extends `IdentityUser`:

| Property | Type | Description |
|----------|------|-------------|
| DisplayName | string (max 100) | Friendly name shown in UI |
| MustChangePassword | bool | True when admin creates account with temp password |
| MustSetupTotp | bool | True until user completes TOTP enrollment |
| CreatedAt | DateTimeOffset | Account creation timestamp |

### Roles

Two seeded roles stored in `AspNetRoles`:

- **Admin** — full access including Docker operations, model management, voice library CRUD, user management, admin settings
- **User** — read-only dashboard, TTS playground, voice library browsing, own generation history

### GenerationLog Change

Add `UserId` (string, nullable, FK to `AspNetUsers`) to `GenerationLog`. Nullable to preserve existing rows from pre-auth phases. Users see their own history; admins see all.

### TOTP Storage

Uses Identity's built-in `AuthenticatorKey` stored in `AspNetUserTokens`. No custom columns needed.

## Authentication Flow & Middleware

### Cookie Configuration

- Cookie name: `.FishOrch.Auth`
- 24-hour sliding expiration
- `HttpOnly`, `SameSite=Strict`, `SecurePolicy=Always`
- Login path: `/login`
- Access denied path: `/access-denied`

### Login Flow

1. Username + password validated by `SignInManager`
2. If TOTP is enabled, redirect to `/login/totp` for 6-digit code
3. On success, issue cookie, redirect to Dashboard

### Post-Login Redirects

- `MustChangePassword = true` → redirect to `/account/change-password`
- `MustSetupTotp = true` → redirect to `/account/setup-totp`
- These redirects are enforced before any other page is accessible

### Global Protection

All pages require authentication by default. Anonymous exceptions:

- `/login`
- `/login/totp`
- `/setup` (first-run wizard)
- `/access-denied`

### Middleware Order in Program.cs

```
UseHttpsRedirection()
UseAuthentication()
UseAuthorization()
UseAntiforgery()
```

## HTTPS & Let's Encrypt

### LettuceEncrypt Integration

Integrates with Kestrel to auto-provision and renew Let's Encrypt certificates.

- Requires a domain name pointing at the machine (no bare IPs or localhost)
- Configuration in `appsettings.json`: domain name, email for renewal notices
- `UseHttpsRedirection()` handles HTTP to HTTPS redirect (already in place)

### Fallback

If no domain is configured, LettuceEncrypt is skipped. App runs with Kestrel's default dev cert for localhost/LAN use.

### Configuration

FQDN is configurable via:

1. First-run setup wizard
2. Admin settings page (requires restart)
3. `appsettings.json` directly
4. Environment variable: `FishOrchestrator__Domain`

Changes to FQDN require an application restart for Kestrel to rebind with the new TLS certificate.

## First-Run Setup Wizard

### Detection

On startup, check if any users exist in `AspNetUsers`. If none, redirect all requests to `/setup`.

### Wizard Steps (Single Page, Sequential Sections)

1. **Network configuration** — optional FQDN input field with helper text
2. **Admin account creation** — username, display name, password (8+ chars, upper, lower, digit, special), confirm password
3. **TOTP enrollment** — QR code + manual key display, verify with 6-digit code
4. **Confirmation** — summary of configuration, "Complete Setup" button

### On Completion

- Admin user created with `Admin` role, `MustChangePassword = false`, `MustSetupTotp = false`
- FQDN written to `appsettings.json` if provided, with restart notice displayed
- Auto-login the admin, redirect to Dashboard

### Environment Variable Seeding

For Docker/automated deployments:

- `FishOrchestrator__AdminUser` — admin username
- `FishOrchestrator__AdminPassword` — admin password
- `FishOrchestrator__Domain` — FQDN (optional)

Checked at startup before wizard check. If present and no users exist, seed the admin account. TOTP is not set up via env vars — admin must complete TOTP enrollment on first login (`MustSetupTotp = true`).

### Wizard Guard Middleware

If no users exist and request path is not `/setup`, return 302 to `/setup`.

## User Management (Admin Only)

### Admin Page at `/admin/users`

Protected by `[Authorize(Roles = "Admin")]`.

### User List View

Table columns: display name, username, role, TOTP enabled (yes/no), created date, last login. Actions per row: edit, reset password, delete.

### Create User

Fields: username, display name, temporary password, role dropdown (Admin/User). On creation: `MustChangePassword = true`, `MustSetupTotp = true`. No email required.

### Edit User

Change display name and role. Admin cannot demote themselves if they are the last admin (lockout prevention).

### Reset Password

Admin sets a new temporary password. Sets `MustChangePassword = true`.

### Delete User

Uses existing `ConfirmDialog` component. Cannot delete yourself. Cannot delete the last admin account.

### No Self-Registration

The `/setup` wizard is the only way to create the first account. All subsequent accounts are admin-created.

## Role-Based Authorization

| Area | Admin | User |
|------|-------|------|
| Dashboard (view) | Yes | Yes |
| TTS Playground | Yes | Yes |
| Voice Library (browse, play) | Yes | Yes |
| Generation History (own) | Yes | Yes |
| Deploy / remove containers | Yes | No |
| Model profile management | Yes | No |
| Voice Library (add, edit, delete) | Yes | No |
| User management | Yes | No |
| Admin settings (FQDN) | Yes | No |

### Implementation

- `[Authorize(Roles = "Admin")]` on admin-only pages
- `[Authorize]` on all other pages
- NavMenu hides links the current user cannot access via `AuthenticationStateProvider`

## Account Self-Service

Available to all authenticated users at `/account`:

- **Change password** — current password + new password + confirm
- **TOTP management** — view status, regenerate key (new QR + verify code). TOTP is mandatory for all users and cannot be disabled. Admins can reset another user's TOTP via user management (sets `MustSetupTotp = true`).
- **Display name** — edit own display name

### No Email / No Password Recovery

If a user forgets their password, an admin resets it.

## Packages

| Package | Purpose |
|---------|---------|
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | Identity + EF Core integration |
| LettuceEncrypt | Automatic Let's Encrypt certificates |
| QRCoder | TOTP enrollment QR code generation |

TOTP validation uses Identity's built-in authenticator token provider. Password hashing is built into Identity (PBKDF2-HMAC-SHA256).

## Configuration Additions

```json
{
  "FishOrchestrator": {
    "Domain": "",
    "AdminUser": "",
    "AdminPassword": ""
  },
  "LettuceEncrypt": {
    "AcceptTermsOfService": true,
    "DomainNames": [],
    "EmailAddress": ""
  }
}
```

## Testing Strategy

- **Unit tests:** Wizard guard middleware, role authorization policies, env-var seeding logic, `MustChangePassword`/`MustSetupTotp` redirect logic
- **Integration tests:** Identity setup (user creation, password validation, TOTP verify), admin user management operations
- Extends existing test suite (~42 tests)
