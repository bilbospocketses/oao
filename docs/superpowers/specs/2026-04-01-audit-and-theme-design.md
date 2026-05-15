> **Note:** post-rename. References to `OpenAudioOrchestrator.*` paths and
> config keys reflect their original names. Current equivalents are
> `oao.*` and `oao:*`.
# Audit & Theme Enhancement — Design Spec

**Date:** 2026-04-01
**Scope:** Full codebase audit (security, bugs, code quality) + light/dark theme system

---

## Part 1: Codebase Audit

### Objective

Outside auditor-style review of the entire FishAudioOrchestrator codebase. Findings categorized by severity and delivered as a report for the user to triage before implementation.

### Audit Areas

**Security**
- Authentication edge cases (login flow, TOTP, session handling)
- Input validation gaps (all user-facing endpoints, Razor components, SignalR hub)
- CSRF protections
- Security header completeness
- Secrets handling (connection strings, database key, admin credentials)
- SignalR authorization and container ID validation
- Docker API safety (exec commands, container creation parameters)
- File serving path traversal (audio endpoints)
- Rate limiting coverage

**Bugs & Reliability**
- Race conditions (concurrent job processing, container state changes, event bus)
- Resource leaks (HTTP clients, Docker streams, file handles, DB contexts)
- Null reference handling and edge cases
- TTS job processor edge cases (recovery, cancellation, timeout)
- Health monitor edge cases (failure counting, status transitions)
- SignalR connection lifecycle (subscribe/unsubscribe, reconnection)
- EF Core usage (tracking, concurrency, disposal)

**Code Quality & Efficiency**
- Dead code (e.g., `TtsClientService.GenerateAsync` — appears unused from UI)
- Duplication across services or components
- Naming consistency
- Unnecessary allocations or inefficient patterns
- Error handling consistency
- Logging coverage and quality
- Configuration validation

### Deliverable

Categorized report:
- **Critical** — security vulnerabilities, data loss risks, correctness bugs
- **Medium** — reliability issues, resource leaks, non-trivial code quality problems
- **Low** — minor improvements, style, dead code, efficiency tweaks

Each finding includes: description, file path and line reference, and recommended fix.

User triages the report and selects which findings to implement.

---

## Part 2: Theme System

### Architecture

#### Data Layer

- Add `ThemePreference` string column to `AppUser` entity
  - Default value: `"dark"`
  - Allowed values: `"dark"`, `"light"`
- EF Core migration: `AddThemePreference`

#### CSS Strategy

Two sets of CSS custom properties on `<html>` via `data-theme` attribute:

```css
[data-theme="dark"] {
  --bg-primary: #2a2a2a;
  --bg-surface: #333333;
  --bg-surface-hover: #3a3a3a;
  --bg-input: #2e2e2e;
  --text-primary: #e0e0e0;
  --text-secondary: #aaaaaa;
  --text-muted: #888888;
  --border-color: #444444;
  --accent: /* neutral non-blue, determined during implementation */;
  --shadow: rgba(0, 0, 0, 0.3);
}

[data-theme="light"] {
  --bg-primary: #ffffff;
  --bg-surface: #f5f5f5;
  --bg-surface-hover: #eeeeee;
  --bg-input: #ffffff;
  --text-primary: #1a1a1a;
  --text-secondary: #555555;
  --text-muted: #888888;
  --border-color: #dddddd;
  --accent: /* neutral non-blue, determined during implementation */;
  --shadow: rgba(0, 0, 0, 0.08);
}
```

All existing CSS converted from hard-coded colors to `var(--token-name)` references. All blue tints removed from the dark theme.

#### Server-Side Rendering

- `MainLayout.razor` resolves the authenticated user's `ThemePreference` on load
- Sets `data-theme` attribute on `<html>` element during server render
- No flash of wrong theme — correct theme rendered on first paint

#### Theme Toggle Component

- Small toggle in the nav bar (sun/moon icon or similar text label)
- On click:
  1. JavaScript immediately sets `data-theme` on `<html>` for instant visual switch
  2. Blazor component calls an API endpoint to persist the preference
- Endpoint: `POST /api/auth/theme` (authorized, sets `AppUser.ThemePreference`)

#### Dark Theme (Default)

- Medium grey base, zero blue
- Backgrounds: `#2a2a2a` (page), `#333`–`#3a3a3a` (cards/surfaces)
- Text: `#e0e0e0` primary, `#aaa` secondary
- Borders: `#444`
- Accent: warm neutral (grey-green or amber — to be decided during implementation based on existing UI feel)

#### Light Theme

- White primary: `#ffffff`
- Surfaces: `#f5f5f5` cards, `#fafafa` subtle backgrounds
- Text: `#1a1a1a` primary, `#555` secondary
- Borders: `#ddd`
- Clean, minimal, high readability

---

## Implementation Order

1. Audit — produce categorized report
2. User triages findings
3. Implement selected audit fixes
4. Theme system — migration, CSS variables, toggle, both themes

---

## Out of Scope

- More than two themes (dark/light only)
- User-configurable individual colors
- Per-page or per-component theme overrides
- Theme sync across devices (each login session uses the DB value)
