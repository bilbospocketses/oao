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
