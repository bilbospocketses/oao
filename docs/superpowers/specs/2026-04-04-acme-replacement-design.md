# Replace LettuceEncrypt with ACMESharpCore

## Problem

LettuceEncrypt 1.3.3 (archived, no future releases) depends on Certes 3.0.4 (unmaintained since 2021). On Fedora 44, the Certes ACME client consistently fails with `InvalidOperationException: An invalid request URI was provided` when downloading the issued certificate. The same flow works on Ubuntu and Windows. Since both libraries are abandoned, the fix is to replace them with a maintained ACME client.

## Solution

Replace LettuceEncrypt + Certes with ACMESharpCore (actively maintained, used by win-acme/Certify). Implement a custom hosted service that manages the full ACME flow with proper retry/polling logic and certificate persistence.

## Config Change

Rename the `LettuceEncrypt` config section to `Acme`:

```json
"Acme": {
  "AcceptTermsOfService": true,
  "DomainNames": [],
  "EmailAddress": ""
}
```

Same keys, cleaner name, no dependency on a specific library.

## New Files

### `Services/AcmeCertificateService.cs`

Registered as a singleton + `IHostedService`. Responsibilities:

- **Startup:** Check for existing PFX certificate on disk. If valid and not expiring within 30 days, load it. Otherwise, trigger ACME flow.
- **ACME flow (using ACMESharpCore):**
  1. Load or create ACME account (persisted to `{DataRoot}/acme-account.json`)
  2. Create order for configured domain
  3. Complete HTTP-01 challenge (stores token/response in a concurrent dictionary read by the challenge middleware)
  4. Poll order status with retry/backoff until valid
  5. Download certificate
  6. Save as PFX to `{DataRoot}/acme-cert.pfx`
  7. Update the in-memory certificate used by Kestrel's `ServerCertificateSelector`
- **Renewal:** Check every 12 hours. Renew if cert expires within 30 days.
- **Error handling:** On ACME failure, log error, retain any existing cert, retry after 1 hour. On first run with no cert, generate a temporary self-signed cert so Kestrel can start HTTPS while ACME completes in the background.

Public interface consumed by Program.cs:

```csharp
public X509Certificate2? GetCertificate();  // Called by ServerCertificateSelector
public void SetChallengeResponse(string token, string response);  // Internal, used during ACME flow
public string? GetChallengeResponse(string token);  // Called by challenge middleware
```

### `Middleware/AcmeChallengeMiddleware.cs`

Intercepts HTTP requests to `/.well-known/acme-challenge/{token}` on port 80. Returns the challenge response from `AcmeCertificateService`. Returns 404 if no active challenge. This replaces the challenge handling that LettuceEncrypt did internally.

## Modified Files

### `Program.cs`

Replace lines 22-35 (LettuceEncrypt block) with:

```csharp
var domain = builder.Configuration["OpenAudioOrchestrator:Domain"];
if (!string.IsNullOrWhiteSpace(domain))
{
    builder.Services.AddSingleton<AcmeCertificateService>();
    builder.Services.AddHostedService<AcmeCertificateService>(sp =>
        sp.GetRequiredService<AcmeCertificateService>());
    builder.WebHost.UseKestrel(kestrel =>
    {
        var acmeService = kestrel.ApplicationServices.GetRequiredService<AcmeCertificateService>();
        kestrel.Listen(IPAddress.Any, 80);
        kestrel.Listen(IPAddress.Any, 443, o => o.UseHttps(h =>
        {
            h.ServerCertificateSelector = (ctx, name) => acmeService.GetCertificate();
        }));
    });
}
```

Add challenge middleware before other middleware:

```csharp
if (!string.IsNullOrWhiteSpace(domain))
{
    app.UseMiddleware<AcmeChallengeMiddleware>();
}
```

### `appsettings.json`

Rename `LettuceEncrypt` section to `Acme`.

### `Services/SetupSettingsService.cs`

Update to write the `Acme` config section instead of `LettuceEncrypt`.

### `Components/Pages/Admin/AdminSettings.razor`

Update the config section name reference from `LettuceEncrypt` to `Acme`.

### `OpenAudioOrchestrator.Web.csproj`

- Remove: `<PackageReference Include="LettuceEncrypt" Version="1.3.3" />`
- Add: `<PackageReference Include="ACMESharpCore" Version="2.2.0.148" />`

## Documentation Updates

After implementation, update all references to LettuceEncrypt:

- `README.md` — update HTTPS/LettuceEncrypt references in Architecture and Configuration sections
- `docs/WINDOWS-SETUP.md` — update any HTTPS/certificate references
- `docs/LINUX-SETUP.md` — update any HTTPS/certificate references

## Certificate and Account Storage

| File | Location | Purpose |
|------|----------|---------|
| ACME account | `{DataRoot}/acme-account.json` | Persisted account key so we reuse the same Let's Encrypt account across restarts |
| Certificate | `{DataRoot}/acme-cert.pfx` | The issued certificate + private key |

Both stored in DataRoot so they're deployment-specific and survive app updates.

## Error Handling

| Scenario | Behavior |
|----------|----------|
| ACME fails on startup, no existing cert | Generate self-signed cert, log warning, retry ACME in 1 hour |
| ACME fails on startup, existing cert valid | Keep using existing cert, log warning, retry in 1 hour |
| ACME fails on renewal | Keep current cert, log warning, retry in 1 hour |
| HuggingFace/network unreachable | No impact (ACME is independent) |
| Domain not configured | ACME service not registered, app runs HTTP on port 5206 as before |

## Testing

- Verify HTTPS works on fresh install with domain configured (Fedora + Ubuntu)
- Verify renewal flow triggers when cert is near expiry
- Verify fallback self-signed cert allows app to start while ACME completes
- Verify HTTP-01 challenge middleware serves correct responses
- Verify existing deployments with LettuceEncrypt config can migrate by renaming the section
