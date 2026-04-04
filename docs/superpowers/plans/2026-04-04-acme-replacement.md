# Replace LettuceEncrypt with ACMESharpCore Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the archived LettuceEncrypt + Certes libraries with ACMESharpCore for automatic Let's Encrypt HTTPS certificate provisioning.

**Architecture:** A new `AcmeCertificateService` hosted service handles the full ACME lifecycle (account creation, HTTP-01 challenge, certificate download, renewal). A small `AcmeChallengeMiddleware` serves challenge responses. Kestrel's `ServerCertificateSelector` dynamically returns the current certificate. Certificate and account data are persisted to `{DataRoot}/`.

**Tech Stack:** ACMESharpCore 2.2.0.148, ASP.NET Core 9, Kestrel ServerCertificateSelector

**Spec:** `docs/superpowers/specs/2026-04-04-acme-replacement-design.md`

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `Services/AcmeCertificateService.cs` | Create | Hosted service: ACME flow, cert persistence, renewal, challenge state |
| `Middleware/AcmeChallengeMiddleware.cs` | Create | Serves HTTP-01 challenge responses at `/.well-known/acme-challenge/` |
| `Program.cs` | Modify (lines 1, 22-35, after line 221) | Replace LettuceEncrypt registration with new service + middleware |
| `appsettings.json` | Modify (lines 29-33) | Rename `LettuceEncrypt` → `Acme` |
| `Services/SetupSettingsService.cs` | Modify (lines 65-76) | Write `Acme` section instead of `LettuceEncrypt` |
| `Components/Pages/Admin/AdminSettings.razor` | Modify (lines 83-99) | Update config section name |
| `OpenAudioOrchestrator.Web.csproj` | Modify (line 11) | Swap package references |
| `README.md` | Modify (lines 84-86, 100) | Update config table and architecture section |
| `docs/LINUX-SETUP.md` | Modify (line 121) | Update setup wizard step description |
| `docs/WINDOWS-SETUP.md` | Modify (line 62) | Update setup wizard step description |

---

### Task 1: Swap NuGet packages

**Files:**
- Modify: `src/OpenAudioOrchestrator.Web/OpenAudioOrchestrator.Web.csproj:11`

- [ ] **Step 1: Remove LettuceEncrypt, add ACMESharpCore**

In `src/OpenAudioOrchestrator.Web/OpenAudioOrchestrator.Web.csproj`, replace:

```xml
    <PackageReference Include="LettuceEncrypt" Version="1.3.3" />
```

with:

```xml
    <PackageReference Include="ACMESharpCore" Version="2.2.0.148" />
```

- [ ] **Step 2: Restore packages**

Run: `dotnet restore src/OpenAudioOrchestrator.Web`
Expected: successful restore, ACMESharpCore downloaded

- [ ] **Step 3: Commit**

```bash
git add src/OpenAudioOrchestrator.Web/OpenAudioOrchestrator.Web.csproj
git commit -m "chore: replace LettuceEncrypt with ACMESharpCore package"
```

---

### Task 2: Create AcmeChallengeMiddleware

**Files:**
- Create: `src/OpenAudioOrchestrator.Web/Middleware/AcmeChallengeMiddleware.cs`

- [ ] **Step 1: Create the middleware**

Create `src/OpenAudioOrchestrator.Web/Middleware/AcmeChallengeMiddleware.cs`:

```csharp
namespace OpenAudioOrchestrator.Web.Middleware;

/// <summary>
/// Serves HTTP-01 ACME challenge responses for Let's Encrypt domain validation.
/// </summary>
public class AcmeChallengeMiddleware
{
    private readonly RequestDelegate _next;

    public AcmeChallengeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        const string prefix = "/.well-known/acme-challenge/";

        if (context.Request.Path.StartsWithSegments("/.well-known/acme-challenge", out var remaining)
            && remaining.HasValue
            && remaining.Value.Length > 1)
        {
            var token = remaining.Value.TrimStart('/');
            var acmeService = context.RequestServices.GetService<Services.AcmeCertificateService>();
            var response = acmeService?.GetChallengeResponse(token);

            if (response is not null)
            {
                context.Response.ContentType = "application/octet-stream";
                await context.Response.WriteAsync(response);
                return;
            }

            context.Response.StatusCode = 404;
            return;
        }

        await _next(context);
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/OpenAudioOrchestrator.Web`
Expected: Build succeeded (warning about missing `AcmeCertificateService` is fine — it's created next)

- [ ] **Step 3: Commit**

```bash
git add src/OpenAudioOrchestrator.Web/Middleware/AcmeChallengeMiddleware.cs
git commit -m "feat: add ACME HTTP-01 challenge middleware"
```

---

### Task 3: Create AcmeCertificateService

**Files:**
- Create: `src/OpenAudioOrchestrator.Web/Services/AcmeCertificateService.cs`

- [ ] **Step 1: Create the service**

Create `src/OpenAudioOrchestrator.Web/Services/AcmeCertificateService.cs`:

```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ACMESharp.Authorizations;
using ACMESharp.Crypto.JOSE;
using ACMESharp.Crypto.JOSE.Impl;
using ACMESharp.Protocol;

namespace OpenAudioOrchestrator.Web.Services;

/// <summary>
/// Hosted service that manages automatic Let's Encrypt certificate provisioning
/// using ACMESharpCore. Handles account creation, HTTP-01 challenges, certificate
/// download, persistence, and renewal.
/// </summary>
public sealed class AcmeCertificateService : IHostedService, IDisposable
{
    private readonly IConfiguration _config;
    private readonly ILogger<AcmeCertificateService> _logger;
    private readonly ConcurrentDictionary<string, string> _challengeResponses = new();
    private X509Certificate2? _certificate;
    private Timer? _renewalTimer;
    private CancellationTokenSource? _cts;

    private static readonly Uri LetsEncryptV2 = new("https://acme-v02.api.letsencrypt.org/");
    private const int RenewalCheckHours = 12;
    private const int RenewalThresholdDays = 30;
    private const int RetryDelayMinutes = 60;

    public AcmeCertificateService(IConfiguration config, ILogger<AcmeCertificateService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Returns the current certificate for Kestrel's ServerCertificateSelector.
    /// </summary>
    public X509Certificate2? GetCertificate() => _certificate;

    /// <summary>
    /// Returns the challenge response for a given token, used by AcmeChallengeMiddleware.
    /// </summary>
    public string? GetChallengeResponse(string token) =>
        _challengeResponses.TryGetValue(token, out var response) ? response : null;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var dataRoot = _config["OpenAudioOrchestrator:DataRoot"];
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            _logger.LogWarning("DataRoot not configured, ACME certificate service will not start");
            return;
        }

        // Try to load existing certificate
        var certPath = GetCertPath(dataRoot);
        if (File.Exists(certPath))
        {
            try
            {
                _certificate = X509CertificateLoader.LoadPkcs12FromFile(certPath, null);
                _logger.LogInformation("Loaded existing certificate for {Subject}, expires {Expiry}",
                    _certificate.Subject, _certificate.NotAfter);

                if (_certificate.NotAfter > DateTime.UtcNow.AddDays(RenewalThresholdDays))
                {
                    ScheduleRenewalCheck();
                    return;
                }

                _logger.LogInformation("Certificate expiring within {Days} days, will renew", RenewalThresholdDays);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load existing certificate, will request new one");
            }
        }

        // Generate self-signed fallback so Kestrel can start HTTPS immediately
        if (_certificate is null)
            _certificate = GenerateSelfSignedCert();

        // Request certificate in background
        _ = RequestCertificateWithRetryAsync(_cts.Token);
        ScheduleRenewalCheck();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _renewalTimer?.Dispose();
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _renewalTimer?.Dispose();
        _cts?.Dispose();
    }

    private void ScheduleRenewalCheck()
    {
        _renewalTimer = new Timer(
            _ => _ = CheckRenewalAsync(),
            null,
            TimeSpan.FromHours(RenewalCheckHours),
            TimeSpan.FromHours(RenewalCheckHours));
    }

    private async Task CheckRenewalAsync()
    {
        if (_certificate is null || _certificate.NotAfter <= DateTime.UtcNow.AddDays(RenewalThresholdDays))
        {
            _logger.LogInformation("Certificate renewal check: renewal needed");
            await RequestCertificateWithRetryAsync(_cts?.Token ?? CancellationToken.None);
        }
        else
        {
            _logger.LogDebug("Certificate renewal check: certificate valid until {Expiry}", _certificate.NotAfter);
        }
    }

    private async Task RequestCertificateWithRetryAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RequestCertificateAsync(ct);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ACME certificate request failed, retrying in {Minutes} minutes", RetryDelayMinutes);
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(RetryDelayMinutes), ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task RequestCertificateAsync(CancellationToken ct)
    {
        var domain = _config["OpenAudioOrchestrator:Domain"]!;
        var email = _config["Acme:EmailAddress"] ?? "";
        var dataRoot = _config["OpenAudioOrchestrator:DataRoot"]!;

        _logger.LogInformation("Starting ACME certificate request for {Domain}", domain);

        using var http = new HttpClient { BaseAddress = LetsEncryptV2 };
        var acme = new AcmeProtocolClient(http, usePostAsGet: true);

        // Initialize directory and nonce
        var dir = await acme.GetDirectoryAsync(ct);
        acme.Directory = dir;
        await acme.GetNonceAsync(ct);

        // Load or create account
        var accountPath = GetAccountPath(dataRoot);
        if (File.Exists(accountPath))
        {
            var saved = JsonSerializer.Deserialize<SavedAccount>(
                await File.ReadAllTextAsync(accountPath, ct))!;
            var signer = new ESJwsTool();
            signer.Init();
            signer.Import(saved.SignerKey);
            acme.Signer = signer;

            acme.Account = await acme.CheckAccountAsync(ct);
            _logger.LogInformation("Loaded existing ACME account");
        }
        else
        {
            var contacts = string.IsNullOrWhiteSpace(email)
                ? Array.Empty<string>()
                : new[] { $"mailto:{email}" };
            acme.Account = await acme.CreateAccountAsync(contacts, termsOfServiceAgreed: true, ct: ct);

            var saved = new SavedAccount { SignerKey = acme.Signer.Export() };
            Directory.CreateDirectory(Path.GetDirectoryName(accountPath)!);
            await File.WriteAllTextAsync(accountPath,
                JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }), ct);
            _logger.LogInformation("Created new ACME account");
        }

        // Create order
        var order = await acme.CreateOrderAsync(new[] { domain }, ct: ct);
        _logger.LogInformation("Created ACME order for {Domain}", domain);

        // Process authorizations
        foreach (var authzUrl in order.Payload.Authorizations)
        {
            var authz = await acme.GetAuthorizationDetailsAsync(authzUrl, ct);

            if (authz.Status == "valid")
                continue;

            var challenge = authz.Challenges.FirstOrDefault(c => c.Type == "http-01")
                ?? throw new InvalidOperationException("No http-01 challenge available");

            var validation = AuthorizationDecoder.DecodeChallengeValidation(authz, "http-01", acme.Signer);
            var httpChallenge = (Http01ChallengeValidationDetails)validation;

            // Store challenge response for middleware
            var token = httpChallenge.HttpResourcePath.Split('/').Last();
            _challengeResponses[token] = httpChallenge.HttpResourceValue;

            _logger.LogInformation("Answering HTTP-01 challenge for {Domain}", domain);
            await acme.AnswerChallengeAsync(challenge.Url, ct);
        }

        // Poll authorizations until valid
        var maxPollAttempts = 30;
        for (var attempt = 0; attempt < maxPollAttempts; attempt++)
        {
            var allValid = true;
            foreach (var authzUrl in order.Payload.Authorizations)
            {
                var authz = await acme.GetAuthorizationDetailsAsync(authzUrl, ct);
                if (authz.Status == "invalid")
                    throw new InvalidOperationException(
                        $"Authorization failed for {authz.Identifier.Value}: {authz.Challenges.FirstOrDefault(c => c.Error != null)?.Error}");
                if (authz.Status != "valid")
                    allValid = false;
            }
            if (allValid) break;
            if (attempt == maxPollAttempts - 1)
                throw new TimeoutException("Authorization polling timed out");
            await Task.Delay(5000, ct);
        }

        _challengeResponses.Clear();

        // Generate key and CSR
        using var certKey = RSA.Create(2048);
        var csrBuilder = new CertificateRequest(
            $"CN={domain}", certKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Add SAN extension
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(domain);
        csrBuilder.CertificateExtensions.Add(sanBuilder.Build());

        var csrDer = csrBuilder.CreateSigningRequest();

        // Finalize order
        order = await acme.FinalizeOrderAsync(order.Payload.Finalize, csrDer, ct);
        _logger.LogInformation("Finalized ACME order for {Domain}", domain);

        // Poll order until certificate URL is available
        for (var attempt = 0; attempt < maxPollAttempts; attempt++)
        {
            if (!string.IsNullOrEmpty(order.Payload.Certificate))
                break;
            if (order.Payload.Status == "invalid")
                throw new InvalidOperationException("Order became invalid after finalization");
            if (attempt == maxPollAttempts - 1)
                throw new TimeoutException("Order certificate polling timed out");
            await Task.Delay(5000, ct);
            order = await acme.GetOrderDetailsAsync(order.OrderUrl, existing: order, ct: ct);
        }

        // Download certificate
        var certPemBytes = await acme.GetOrderCertificateAsync(order, ct: ct);
        var certPem = System.Text.Encoding.UTF8.GetString(certPemBytes);

        // Parse the PEM certificate chain and attach the private key
        var leafCert = X509Certificate2.CreateFromPem(certPem).CopyWithPrivateKey(certKey);

        var certPath = GetCertPath(dataRoot);
        var pfxBytes = leafCert.Export(X509ContentType.Pfx);
        await File.WriteAllBytesAsync(certPath, pfxBytes, ct);

        _certificate = new X509Certificate2(pfxBytes);
        _logger.LogInformation("ACME certificate installed for {Domain}, expires {Expiry}",
            domain, _certificate.NotAfter);
    }

    private static X509Certificate2 GenerateSelfSignedCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
    }

    private static string GetCertPath(string dataRoot) => Path.Combine(dataRoot, "acme-cert.pfx");
    private static string GetAccountPath(string dataRoot) => Path.Combine(dataRoot, "acme-account.json");

    private sealed record SavedAccount
    {
        public string SignerKey { get; init; } = "";
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/OpenAudioOrchestrator.Web`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/OpenAudioOrchestrator.Web/Services/AcmeCertificateService.cs
git commit -m "feat: add AcmeCertificateService using ACMESharpCore"
```

---

### Task 4: Update Program.cs to use new ACME service

**Files:**
- Modify: `src/OpenAudioOrchestrator.Web/Program.cs:1,22-35,221`

- [ ] **Step 1: Remove LettuceEncrypt using, add new using**

In `Program.cs`, there is no explicit `using` for LettuceEncrypt (it uses extension methods discovered at build time). No using changes needed.

- [ ] **Step 2: Replace LettuceEncrypt Kestrel configuration**

In `Program.cs`, replace lines 22-35:

```csharp
// LettuceEncrypt (only if domain is configured)
var domain = builder.Configuration["OpenAudioOrchestrator:Domain"];
if (!string.IsNullOrWhiteSpace(domain))
{
    builder.Services.AddLettuceEncrypt();
    builder.WebHost.UseKestrel(kestrel =>
    {
        kestrel.Listen(IPAddress.Any, 80);
        kestrel.Listen(IPAddress.Any, 443, o => o.UseHttps(h =>
        {
            h.UseLettuceEncrypt(kestrel.ApplicationServices);
        }));
    });
}
```

with:

```csharp
// ACME / Let's Encrypt (only if domain is configured)
var domain = builder.Configuration["OpenAudioOrchestrator:Domain"];
if (!string.IsNullOrWhiteSpace(domain))
{
    builder.Services.AddSingleton<AcmeCertificateService>();
    builder.Services.AddHostedService<AcmeCertificateService>(sp =>
        sp.GetRequiredService<AcmeCertificateService>());
    builder.WebHost.UseKestrel(kestrel =>
    {
        kestrel.Listen(IPAddress.Any, 80);
        kestrel.Listen(IPAddress.Any, 443, o => o.UseHttps(h =>
        {
            h.ServerCertificateSelector = (ctx, name) =>
            {
                var acme = kestrel.ApplicationServices.GetRequiredService<AcmeCertificateService>();
                return acme.GetCertificate();
            };
        }));
    });
}
```

- [ ] **Step 3: Add ACME challenge middleware**

In `Program.cs`, after line 221 (`app.UseMiddleware<PostLoginRedirectMiddleware>();`), add:

```csharp
if (!string.IsNullOrWhiteSpace(domain))
{
    app.UseMiddleware<AcmeChallengeMiddleware>();
}
```

Note: this must come before `app.UseAntiforgery()` and endpoint mapping so the challenge endpoint is reachable without auth.

- [ ] **Step 4: Verify build**

Run: `dotnet build src/OpenAudioOrchestrator.Web`
Expected: Build succeeded with no LettuceEncrypt references

- [ ] **Step 5: Commit**

```bash
git add src/OpenAudioOrchestrator.Web/Program.cs
git commit -m "feat: wire up AcmeCertificateService and challenge middleware in Program.cs"
```

---

### Task 5: Rename config section from LettuceEncrypt to Acme

**Files:**
- Modify: `src/OpenAudioOrchestrator.Web/appsettings.json:29-33`
- Modify: `src/OpenAudioOrchestrator.Web/Services/SetupSettingsService.cs:65-76`
- Modify: `src/OpenAudioOrchestrator.Web/Components/Pages/Admin/AdminSettings.razor:83-99`

- [ ] **Step 1: Update appsettings.json**

In `src/OpenAudioOrchestrator.Web/appsettings.json`, replace:

```json
  "LettuceEncrypt": {
    "AcceptTermsOfService": true,
    "DomainNames": [],
    "EmailAddress": ""
  }
```

with:

```json
  "Acme": {
    "AcceptTermsOfService": true,
    "DomainNames": [],
    "EmailAddress": ""
  }
```

- [ ] **Step 2: Update SetupSettingsService.cs**

In `src/OpenAudioOrchestrator.Web/Services/SetupSettingsService.cs`, replace lines 65-76:

```csharp
        // LettuceEncrypt
        var le = root["LettuceEncrypt"]!;
        if (!string.IsNullOrWhiteSpace(domain))
        {
            le["DomainNames"] = new JsonArray(domain);
            le["EmailAddress"] = email ?? "";
        }
        else
        {
            le["DomainNames"] = new JsonArray();
            le["EmailAddress"] = "";
        }
```

with:

```csharp
        // Acme
        var acme = root["Acme"]!;
        if (!string.IsNullOrWhiteSpace(domain))
        {
            acme["DomainNames"] = new JsonArray(domain);
            acme["EmailAddress"] = email ?? "";
        }
        else
        {
            acme["DomainNames"] = new JsonArray();
            acme["EmailAddress"] = "";
        }
```

- [ ] **Step 3: Update AdminSettings.razor**

In `src/OpenAudioOrchestrator.Web/Components/Pages/Admin/AdminSettings.razor`, replace lines 83-99:

```csharp
            else if (prop.Name == "LettuceEncrypt")
            {
                writer.WriteStartObject("LettuceEncrypt");
                foreach (var sub in prop.Value.EnumerateObject())
                {
                    if (sub.Name == "DomainNames")
                    {
                        writer.WriteStartArray("DomainNames");
                        if (!string.IsNullOrWhiteSpace(_fqdn))
                            writer.WriteStringValue(_fqdn.Trim());
                        writer.WriteEndArray();
                    }
                    else
                        sub.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
```

with:

```csharp
            else if (prop.Name == "Acme")
            {
                writer.WriteStartObject("Acme");
                foreach (var sub in prop.Value.EnumerateObject())
                {
                    if (sub.Name == "DomainNames")
                    {
                        writer.WriteStartArray("DomainNames");
                        if (!string.IsNullOrWhiteSpace(_fqdn))
                            writer.WriteStringValue(_fqdn.Trim());
                        writer.WriteEndArray();
                    }
                    else
                        sub.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
```

- [ ] **Step 4: Verify build**

Run: `dotnet build src/OpenAudioOrchestrator.Web`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/OpenAudioOrchestrator.Web/appsettings.json src/OpenAudioOrchestrator.Web/Services/SetupSettingsService.cs src/OpenAudioOrchestrator.Web/Components/Pages/Admin/AdminSettings.razor
git commit -m "refactor: rename LettuceEncrypt config section to Acme"
```

---

### Task 6: Update documentation

**Files:**
- Modify: `README.md:84-86,100`
- Modify: `docs/LINUX-SETUP.md:121`
- Modify: `docs/WINDOWS-SETUP.md:62`

- [ ] **Step 1: Update README.md configuration table**

In `README.md`, replace lines 84-86:

```markdown
| `LettuceEncrypt:AcceptTermsOfService` | Accept Let's Encrypt terms (default: `true`) |
| `LettuceEncrypt:DomainNames` | Domain names for certificate |
| `LettuceEncrypt:EmailAddress` | Email for certificate renewal notices |
```

with:

```markdown
| `Acme:AcceptTermsOfService` | Accept Let's Encrypt terms (default: `true`) |
| `Acme:DomainNames` | Domain names for certificate |
| `Acme:EmailAddress` | Email for certificate renewal notices |
```

- [ ] **Step 2: Update README.md architecture section**

In `README.md`, replace line 100:

```markdown
- **LettuceEncrypt** — automatic Let's Encrypt HTTPS on ports 80/443 (optional, enabled when Domain is configured)
```

with:

```markdown
- **ACMESharpCore** — automatic Let's Encrypt HTTPS on ports 80/443 (optional, enabled when Domain is configured)
```

- [ ] **Step 3: Update LINUX-SETUP.md**

In `docs/LINUX-SETUP.md`, replace line 121:

```markdown
4. **Server Configuration** — database encryption key, container port range, optional domain + HTTPS via Let's Encrypt
```

with:

```markdown
4. **Server Configuration** — database encryption key, container port range, optional domain + automatic HTTPS via Let's Encrypt
```

- [ ] **Step 4: Update WINDOWS-SETUP.md**

In `docs/WINDOWS-SETUP.md`, replace line 62:

```markdown
4. **Server Configuration** — database encryption key, container port range, optional domain + HTTPS via Let's Encrypt
```

with:

```markdown
4. **Server Configuration** — database encryption key, container port range, optional domain + automatic HTTPS via Let's Encrypt
```

- [ ] **Step 5: Commit**

```bash
git add README.md docs/LINUX-SETUP.md docs/WINDOWS-SETUP.md
git commit -m "docs: update LettuceEncrypt references to ACMESharpCore/Acme"
```

---

### Task 7: Final build verification and push

- [ ] **Step 1: Clean build**

Run: `dotnet build src/OpenAudioOrchestrator.Web`
Expected: Build succeeded, 0 errors, 0 warnings

- [ ] **Step 2: Verify no LettuceEncrypt references remain in source files**

Run: `grep -ri "LettuceEncrypt" src/ --include="*.cs" --include="*.razor" --include="*.csproj" --include="*.json"`
Expected: No matches

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/OpenAudioOrchestrator.Tests`
Expected: All tests pass

- [ ] **Step 4: Push all commits**

Run: `git push`
