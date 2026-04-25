# Replace LettuceEncrypt with Custom ACME Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace LettuceEncrypt + Certes with a custom ACME client using only built-in .NET APIs for automatic Let's Encrypt HTTPS certificate provisioning.

**Architecture:** A new `AcmeCertificateService` hosted service implements the ACME v2 protocol (RFC 8555) using `HttpClient`, `System.Security.Cryptography`, and `System.Text.Json`. A small `AcmeChallengeMiddleware` serves HTTP-01 challenge responses. Kestrel's `ServerCertificateSelector` dynamically returns the current certificate. Zero third-party ACME dependencies.

**Tech Stack:** .NET 9 built-in APIs only (HttpClient, ECDsa, RSA, System.Text.Json)

**Spec:** `docs/superpowers/specs/2026-04-04-acme-replacement-design.md`

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `Services/AcmeCertificateService.cs` | Create | Hosted service: custom ACME client, cert persistence, renewal, challenge state |
| `Middleware/AcmeChallengeMiddleware.cs` | Create | Serves HTTP-01 challenge responses at `/.well-known/acme-challenge/` |
| `Program.cs` | Modify (lines 22-35, after line 221) | Replace LettuceEncrypt registration with new service + middleware |
| `appsettings.json` | Modify (lines 29-33) | Rename `LettuceEncrypt` → `Acme` |
| `Services/SetupSettingsService.cs` | Modify (lines 65-76) | Write `Acme` section instead of `LettuceEncrypt` |
| `Components/Pages/Admin/AdminSettings.razor` | Modify (lines 83-99) | Update config section name |
| `OpenAudioOrchestrator.Web.csproj` | Modify (line 11) | Remove LettuceEncrypt package (no replacement needed) |
| `README.md` | Modify (lines 84-86, 100) | Update config table and architecture section |
| `docs/LINUX-SETUP.md` | Modify (line 121) | Update setup wizard step description |
| `docs/WINDOWS-SETUP.md` | Modify (line 62) | Update setup wizard step description |

---

### Task 1: Remove LettuceEncrypt package

**Files:**
- Modify: `src/OpenAudioOrchestrator.Web/OpenAudioOrchestrator.Web.csproj:11`

- [ ] **Step 1: Remove LettuceEncrypt package reference**

In `src/OpenAudioOrchestrator.Web/OpenAudioOrchestrator.Web.csproj`, delete line 11:

```xml
    <PackageReference Include="LettuceEncrypt" Version="1.3.3" />
```

- [ ] **Step 2: Restore packages**

Run: `dotnet restore src/OpenAudioOrchestrator.Web`
Expected: successful restore. LettuceEncrypt, Certes, Portable.BouncyCastle, and McMaster.AspNetCore.Kestrel.Certificates are no longer resolved.

- [ ] **Step 3: Commit**

```bash
git add src/OpenAudioOrchestrator.Web/OpenAudioOrchestrator.Web.csproj
git commit -m "chore: remove LettuceEncrypt package dependency"
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
/// Intercepts requests to /.well-known/acme-challenge/{token} and returns the
/// key authorization string from the AcmeCertificateService.
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

- [ ] **Step 2: Commit**

```bash
git add src/OpenAudioOrchestrator.Web/Middleware/AcmeChallengeMiddleware.cs
git commit -m "feat: add ACME HTTP-01 challenge middleware"
```

---

### Task 3: Create AcmeCertificateService

**Files:**
- Create: `src/OpenAudioOrchestrator.Web/Services/AcmeCertificateService.cs`

This is the core service. It implements the ACME v2 protocol (RFC 8555) using only built-in .NET APIs. The ACME protocol requires JWS (JSON Web Signature) signed requests using an ES256 (ECDSA P-256) key.

- [ ] **Step 1: Create the service**

Create `src/OpenAudioOrchestrator.Web/Services/AcmeCertificateService.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenAudioOrchestrator.Web.Services;

/// <summary>
/// Hosted service that manages automatic Let's Encrypt certificate provisioning
/// using a custom ACME v2 client (RFC 8555). Uses only built-in .NET APIs —
/// no third-party ACME libraries.
/// </summary>
public sealed class AcmeCertificateService : IHostedService, IDisposable
{
    private readonly IConfiguration _config;
    private readonly ILogger<AcmeCertificateService> _logger;
    private readonly ConcurrentDictionary<string, string> _challengeResponses = new();
    private X509Certificate2? _certificate;
    private Timer? _renewalTimer;
    private CancellationTokenSource? _cts;

    private static readonly Uri LetsEncryptDirectory = new("https://acme-v02.api.letsencrypt.org/directory");
    private const int RenewalCheckHours = 12;
    private const int RenewalThresholdDays = 30;
    private const int RetryDelayMinutes = 60;
    private const int MaxPollAttempts = 30;
    private const int PollDelayMs = 5000;

    public AcmeCertificateService(IConfiguration config, ILogger<AcmeCertificateService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>Returns the current certificate for Kestrel's ServerCertificateSelector.</summary>
    public X509Certificate2? GetCertificate() => _certificate;

    /// <summary>Returns the challenge response for a given token (used by AcmeChallengeMiddleware).</summary>
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
            _logger.LogDebug("Certificate renewal check: valid until {Expiry}", _certificate.NotAfter);
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
                _logger.LogError(ex, "ACME certificate request failed, retrying in {Minutes} minutes",
                    RetryDelayMinutes);
                try { await Task.Delay(TimeSpan.FromMinutes(RetryDelayMinutes), ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    // -----------------------------------------------------------------------
    //  ACME v2 Protocol Implementation (RFC 8555)
    // -----------------------------------------------------------------------

    private async Task RequestCertificateAsync(CancellationToken ct)
    {
        var domain = _config["OpenAudioOrchestrator:Domain"]!;
        var email = _config["Acme:EmailAddress"] ?? "";
        var dataRoot = _config["OpenAudioOrchestrator:DataRoot"]!;

        _logger.LogInformation("Starting ACME certificate request for {Domain}", domain);

        using var http = new HttpClient();

        // 1. Fetch directory
        var dirJson = await http.GetStringAsync(LetsEncryptDirectory, ct);
        var directory = JsonSerializer.Deserialize<AcmeDirectory>(dirJson)!;

        // 2. Get initial nonce
        var nonce = await GetNonceAsync(http, directory.NewNonce, ct);

        // 3. Load or create account
        var accountPath = GetAccountPath(dataRoot);
        ECDsa accountKey;
        string? accountKid = null;

        if (File.Exists(accountPath))
        {
            var saved = JsonSerializer.Deserialize<SavedAccount>(
                await File.ReadAllTextAsync(accountPath, ct))!;
            accountKey = ECDsa.Create();
            accountKey.ImportECPrivateKey(Convert.FromBase64String(saved.PrivateKeyBase64), out _);
            accountKid = saved.AccountUrl;
            _logger.LogInformation("Loaded existing ACME account {Kid}", accountKid);
        }
        else
        {
            accountKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            var contacts = string.IsNullOrWhiteSpace(email)
                ? Array.Empty<string>()
                : new[] { $"mailto:{email}" };

            var accountPayload = JsonSerializer.Serialize(new
            {
                termsOfServiceAgreed = true,
                contact = contacts
            });

            var (accountResponse, accountHeaders, newNonce) = await SignedPostAsync(
                http, directory.NewAccount, accountPayload,
                accountKey, jwkHeader: true, kid: null, nonce, ct);
            nonce = newNonce;

            accountKid = accountHeaders.Location?.ToString()
                ?? throw new InvalidOperationException("Account creation did not return a Location header");

            // Persist account
            var keyBytes = accountKey.ExportECPrivateKey();
            var saved = new SavedAccount
            {
                PrivateKeyBase64 = Convert.ToBase64String(keyBytes),
                AccountUrl = accountKid
            };
            Directory.CreateDirectory(Path.GetDirectoryName(accountPath)!);
            await File.WriteAllTextAsync(accountPath,
                JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }), ct);
            _logger.LogInformation("Created new ACME account {Kid}", accountKid);
        }

        // 4. Create order
        var orderPayload = JsonSerializer.Serialize(new
        {
            identifiers = new[] { new { type = "dns", value = domain } }
        });

        var (orderJson, orderHeaders, orderNonce) = await SignedPostAsync(
            http, directory.NewOrder, orderPayload,
            accountKey, jwkHeader: false, kid: accountKid, nonce, ct);
        nonce = orderNonce;

        var order = JsonSerializer.Deserialize<AcmeOrder>(orderJson)!;
        var orderUrl = orderHeaders.Location?.ToString()
            ?? throw new InvalidOperationException("Order creation did not return a Location header");
        _logger.LogInformation("Created ACME order for {Domain}", domain);

        // 5. Process authorizations
        foreach (var authzUrl in order.Authorizations)
        {
            var (authzJson, _, authzNonce) = await SignedPostAsync(
                http, authzUrl, "", accountKey, jwkHeader: false, kid: accountKid, nonce, ct);
            nonce = authzNonce;

            var authz = JsonSerializer.Deserialize<AcmeAuthorization>(authzJson)!;

            if (authz.Status == "valid")
                continue;

            var challenge = authz.Challenges.FirstOrDefault(c => c.Type == "http-01")
                ?? throw new InvalidOperationException("No http-01 challenge available");

            // Compute key authorization: token + "." + JWK thumbprint
            var thumbprint = ComputeJwkThumbprint(accountKey);
            var keyAuth = $"{challenge.Token}.{thumbprint}";
            _challengeResponses[challenge.Token] = keyAuth;

            _logger.LogInformation("Answering HTTP-01 challenge for {Domain}", domain);

            // Tell Let's Encrypt to validate
            var (_, _, challengeNonce) = await SignedPostAsync(
                http, challenge.Url, "{}", accountKey, jwkHeader: false, kid: accountKid, nonce, ct);
            nonce = challengeNonce;
        }

        // 6. Poll authorizations until valid
        for (var attempt = 0; attempt < MaxPollAttempts; attempt++)
        {
            var allValid = true;
            foreach (var authzUrl in order.Authorizations)
            {
                var (authzJson, _, authzNonce) = await SignedPostAsync(
                    http, authzUrl, "", accountKey, jwkHeader: false, kid: accountKid, nonce, ct);
                nonce = authzNonce;

                var authz = JsonSerializer.Deserialize<AcmeAuthorization>(authzJson)!;
                if (authz.Status == "invalid")
                {
                    var failedChallenge = authz.Challenges.FirstOrDefault(c => c.Error != null);
                    throw new InvalidOperationException(
                        $"Authorization failed for {authz.Identifier.Value}: " +
                        $"{failedChallenge?.Error?.Detail ?? "unknown error"}");
                }
                if (authz.Status != "valid")
                    allValid = false;
            }
            if (allValid) break;
            if (attempt == MaxPollAttempts - 1)
                throw new TimeoutException("Authorization polling timed out");
            await Task.Delay(PollDelayMs, ct);
        }

        _challengeResponses.Clear();

        // 7. Generate RSA key and CSR, finalize order
        using var certKey = RSA.Create(2048);
        var csrBuilder = new CertificateRequest(
            $"CN={domain}", certKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(domain);
        csrBuilder.CertificateExtensions.Add(sanBuilder.Build());
        var csrDer = csrBuilder.CreateSigningRequest();

        var finalizePayload = JsonSerializer.Serialize(new
        {
            csr = Base64UrlEncode(csrDer)
        });

        var (finalizeJson, _, finalizeNonce) = await SignedPostAsync(
            http, order.Finalize, finalizePayload,
            accountKey, jwkHeader: false, kid: accountKid, nonce, ct);
        nonce = finalizeNonce;
        order = JsonSerializer.Deserialize<AcmeOrder>(finalizeJson)!;
        _logger.LogInformation("Finalized ACME order for {Domain}", domain);

        // 8. Poll order until certificate URL is available
        for (var attempt = 0; attempt < MaxPollAttempts; attempt++)
        {
            if (!string.IsNullOrEmpty(order.Certificate))
                break;
            if (order.Status == "invalid")
                throw new InvalidOperationException("Order became invalid after finalization");
            if (attempt == MaxPollAttempts - 1)
                throw new TimeoutException("Order certificate polling timed out");
            await Task.Delay(PollDelayMs, ct);

            var (orderJson2, _, orderNonce2) = await SignedPostAsync(
                http, orderUrl, "", accountKey, jwkHeader: false, kid: accountKid, nonce, ct);
            nonce = orderNonce2;
            order = JsonSerializer.Deserialize<AcmeOrder>(orderJson2)!;
        }

        // 9. Download certificate
        var (certPem, _, _) = await SignedPostAsync(
            http, order.Certificate!, "", accountKey, jwkHeader: false, kid: accountKid, nonce, ct,
            accept: "application/pem-certificate-chain");

        // 10. Build PFX and persist
        var leafCert = X509Certificate2.CreateFromPem(certPem).CopyWithPrivateKey(certKey);
        var certPath = GetCertPath(dataRoot);
        var pfxBytes = leafCert.Export(X509ContentType.Pfx);
        await File.WriteAllBytesAsync(certPath, pfxBytes, ct);

        _certificate = new X509Certificate2(pfxBytes);
        _logger.LogInformation("ACME certificate installed for {Domain}, expires {Expiry}",
            domain, _certificate.NotAfter);

        accountKey.Dispose();
    }

    // -----------------------------------------------------------------------
    //  JWS Signing (RFC 7515) + ACME Request Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sends a JWS-signed POST to an ACME endpoint. Returns the response body,
    /// response headers, and the new replay nonce.
    /// </summary>
    private static async Task<(string body, HttpResponseHeaders headers, string nonce)> SignedPostAsync(
        HttpClient http, string url, string payload,
        ECDsa key, bool jwkHeader, string? kid, string nonce,
        CancellationToken ct, string? accept = null)
    {
        // Build protected header
        var protectedObj = new Dictionary<string, object>
        {
            ["alg"] = "ES256",
            ["nonce"] = nonce,
            ["url"] = url
        };

        if (jwkHeader)
        {
            // First request (account creation): include full JWK
            var ecParams = key.ExportParameters(false);
            protectedObj["jwk"] = new Dictionary<string, string>
            {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["x"] = Base64UrlEncode(ecParams.Q.X!),
                ["y"] = Base64UrlEncode(ecParams.Q.Y!)
            };
        }
        else
        {
            // Subsequent requests: use account KID
            protectedObj["kid"] = kid!;
        }

        var protectedJson = JsonSerializer.Serialize(protectedObj);
        var protectedB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(protectedJson));

        // Payload: empty string = POST-as-GET, otherwise encode the payload
        var payloadB64 = payload == ""
            ? ""
            : Base64UrlEncode(Encoding.UTF8.GetBytes(payload));

        // Sign
        var signingInput = Encoding.ASCII.GetBytes($"{protectedB64}.{payloadB64}");
        var signature = key.SignData(signingInput, HashAlgorithmName.SHA256);
        var signatureB64 = Base64UrlEncode(signature);

        var jws = JsonSerializer.Serialize(new
        {
            @protected = protectedB64,
            payload = payloadB64,
            signature = signatureB64
        });

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jws, Encoding.UTF8, "application/jose+json")
        };
        if (accept is not null)
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        // Extract new nonce
        var newNonce = response.Headers.TryGetValues("Replay-Nonce", out var nonceValues)
            ? nonceValues.First()
            : throw new InvalidOperationException("ACME response missing Replay-Nonce header");

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"ACME request to {url} failed with {response.StatusCode}: {body}");
        }

        return (body, response.Headers, newNonce);
    }

    private static async Task<string> GetNonceAsync(HttpClient http, string nonceUrl, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Head, nonceUrl);
        var response = await http.SendAsync(request, ct);
        return response.Headers.TryGetValues("Replay-Nonce", out var values)
            ? values.First()
            : throw new InvalidOperationException("Failed to get initial nonce");
    }

    /// <summary>
    /// Computes the JWK Thumbprint (RFC 7638) for an EC key.
    /// This is the base64url-encoded SHA-256 hash of the canonical JWK representation.
    /// </summary>
    private static string ComputeJwkThumbprint(ECDsa key)
    {
        var ecParams = key.ExportParameters(false);
        // Canonical JWK: keys must be alphabetically sorted
        var jwk = $"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{Base64UrlEncode(ecParams.Q.X!)}\",\"y\":\"{Base64UrlEncode(ecParams.Q.Y!)}\"}}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(jwk));
        return Base64UrlEncode(hash);
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static X509Certificate2 GenerateSelfSignedCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
    }

    private static string GetCertPath(string dataRoot) => Path.Combine(dataRoot, "acme-cert.pfx");
    private static string GetAccountPath(string dataRoot) => Path.Combine(dataRoot, "acme-account.json");

    // -----------------------------------------------------------------------
    //  ACME JSON Models
    // -----------------------------------------------------------------------

    private sealed record SavedAccount
    {
        public string PrivateKeyBase64 { get; init; } = "";
        public string AccountUrl { get; init; } = "";
    }

    private sealed record AcmeDirectory
    {
        [JsonPropertyName("newNonce")] public string NewNonce { get; init; } = "";
        [JsonPropertyName("newAccount")] public string NewAccount { get; init; } = "";
        [JsonPropertyName("newOrder")] public string NewOrder { get; init; } = "";
    }

    private sealed record AcmeOrder
    {
        [JsonPropertyName("status")] public string Status { get; init; } = "";
        [JsonPropertyName("authorizations")] public string[] Authorizations { get; init; } = [];
        [JsonPropertyName("finalize")] public string Finalize { get; init; } = "";
        [JsonPropertyName("certificate")] public string? Certificate { get; init; }
    }

    private sealed record AcmeAuthorization
    {
        [JsonPropertyName("status")] public string Status { get; init; } = "";
        [JsonPropertyName("identifier")] public AcmeIdentifier Identifier { get; init; } = new();
        [JsonPropertyName("challenges")] public AcmeChallenge[] Challenges { get; init; } = [];
    }

    private sealed record AcmeIdentifier
    {
        [JsonPropertyName("type")] public string Type { get; init; } = "";
        [JsonPropertyName("value")] public string Value { get; init; } = "";
    }

    private sealed record AcmeChallenge
    {
        [JsonPropertyName("type")] public string Type { get; init; } = "";
        [JsonPropertyName("url")] public string Url { get; init; } = "";
        [JsonPropertyName("token")] public string Token { get; init; } = "";
        [JsonPropertyName("status")] public string Status { get; init; } = "";
        [JsonPropertyName("error")] public AcmeError? Error { get; init; }
    }

    private sealed record AcmeError
    {
        [JsonPropertyName("type")] public string Type { get; init; } = "";
        [JsonPropertyName("detail")] public string Detail { get; init; } = "";
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/OpenAudioOrchestrator.Web`
Expected: Build succeeded (Program.cs will have errors from LettuceEncrypt references still — that's expected and fixed in Task 4)

- [ ] **Step 3: Commit**

```bash
git add src/OpenAudioOrchestrator.Web/Services/AcmeCertificateService.cs
git commit -m "feat: add custom ACME certificate service using built-in .NET APIs"
```

---

### Task 4: Update Program.cs to use new ACME service

**Files:**
- Modify: `src/OpenAudioOrchestrator.Web/Program.cs:22-35,221`

- [ ] **Step 1: Replace LettuceEncrypt Kestrel configuration**

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

- [ ] **Step 2: Add ACME challenge middleware**

In `Program.cs`, after line 221 (`app.UseMiddleware<PostLoginRedirectMiddleware>();`), add:

```csharp
if (!string.IsNullOrWhiteSpace(domain))
{
    app.UseMiddleware<AcmeChallengeMiddleware>();
}
```

This must come before `app.UseAntiforgery()` and endpoint mapping so the challenge endpoint is reachable without auth.

- [ ] **Step 3: Verify build**

Run: `dotnet build src/OpenAudioOrchestrator.Web`
Expected: Build succeeded with no LettuceEncrypt references

- [ ] **Step 4: Commit**

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
- **Custom ACME client** — automatic Let's Encrypt HTTPS on ports 80/443 using built-in .NET APIs (optional, enabled when Domain is configured)
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
git commit -m "docs: update LettuceEncrypt references to custom ACME client"
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
