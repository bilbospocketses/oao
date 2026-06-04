using Docker.DotNet;
using oao.Web;
using oao.Web.Components;
using oao.Web.Data;
using oao.Web.Data.Entities;
using oao.Web.Endpoints;
using oao.Web.Hubs;
using oao.Web.Middleware;
using oao.Web.Proxy;
using oao.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

// ACME / Let's Encrypt (only if domain is configured)
var domain = builder.Configuration["oao:Domain"];
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

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Rate limiting for auth endpoints
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Data Protection (persistent key storage in DataRoot)
var dataRoot = PlatformDefaults.ConfigValueOrDefault(
    builder.Configuration["oao:DataRoot"], PlatformDefaults.DataRoot);
// Store DP keys alongside the application, not in DataRoot (which can change during setup).
// Tests override this via `oao:DpKeysPath` config to isolate per-WebApplicationFactory and
// avoid a parallel-write race on `dp-key-protection.pfx`.
var dpKeysPath = PlatformDefaults.ConfigValueOrDefault(
    builder.Configuration["oao:DpKeysPath"],
    Path.Combine(builder.Environment.ContentRootPath, ".dp-keys"));
Directory.CreateDirectory(dpKeysPath);
var dpBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("oao")
    .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath));
ConfigurePlatformKeyProtection(dpBuilder, dpKeysPath);


// Build SQLite connection string (with optional SQLCipher encryption)
var connectionString = PlatformDefaults.ConfigValueOrDefault(
    builder.Configuration.GetConnectionString("Default"),
    $"Data Source={PlatformDefaults.DbPath}");
var encryptedDbKey = builder.Configuration["oao:DatabaseKey"];
if (!string.IsNullOrWhiteSpace(encryptedDbKey))
{
    // Decrypt the database key using Data Protection.
    // Build a temporary provider to access the protector before full DI is ready.
    var tempServices = new ServiceCollection();
    var tempDpBuilder = tempServices.AddDataProtection()
        .SetApplicationName("oao")
        .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath));
    ConfigurePlatformKeyProtection(tempDpBuilder, dpKeysPath);
#pragma warning disable ASP0000 // Intentional: need DataProtection before full DI is built
    using var tempProvider = tempServices.BuildServiceProvider();
#pragma warning restore ASP0000
    var protector = tempProvider.GetRequiredService<IDataProtectionProvider>()
        .CreateProtector("DatabaseKey");
    try
    {
        var databaseKey = protector.Unprotect(encryptedDbKey);
        connectionString += $";Password={databaseKey}";
    }
    catch (Exception ex)
    {
        // Fallback: the key may have been stored as plaintext before DP encryption was introduced.
        // Log prominently so operators notice if this happens unexpectedly.
        var message = $"WARNING: Could not decrypt DatabaseKey ({ex.Message}). " +
                      "Using value as plaintext. If the database fails to open, " +
                      "the Data Protection keys may have been lost or rotated.";
        Console.Error.WriteLine(message);
        connectionString += $";Password={encryptedDbKey}";
    }
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(connectionString), ServiceLifetime.Scoped);

// ASP.NET Identity
builder.Services.AddIdentity<AppUser, IdentityRole>(opts =>
    {
        opts.Password.RequiredLength = 8;
        opts.Password.RequireUppercase = true;
        opts.Password.RequireLowercase = true;
        opts.Password.RequireDigit = true;
        opts.Password.RequireNonAlphanumeric = true;
        opts.Lockout.MaxFailedAccessAttempts = 5;
        opts.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(opts =>
{
    opts.Cookie.Name = ".oao.Auth";
    opts.Cookie.HttpOnly = true;
    opts.Cookie.SameSite = SameSiteMode.Strict;
    // Use Always when an HTTPS domain is configured (production); fall back to
    // SameAsRequest for localhost/HTTP development so the cookie still works over HTTP.
    opts.Cookie.SecurePolicy = string.IsNullOrWhiteSpace(domain)
        ? CookieSecurePolicy.SameAsRequest  // Localhost/development: allow HTTP
        : CookieSecurePolicy.Always;        // Production with HTTPS domain
    opts.ExpireTimeSpan = TimeSpan.FromHours(24);
    opts.SlidingExpiration = true;
    opts.LoginPath = "/login";
    opts.AccessDeniedPath = "/access-denied";
});

// Docker client
builder.Services.AddSingleton<IDockerClient>(_ =>
{
    var endpoint = PlatformDefaults.ConfigValueOrDefault(
        builder.Configuration["oao:DockerEndpoint"],
        PlatformDefaults.DockerEndpoint);
    return new DockerClientConfiguration(new Uri(endpoint)).CreateClient();
});

// YARP reverse proxy
var proxyProvider = new ProxyConfigProvider();
builder.Services.AddSingleton(proxyProvider);
builder.Services.AddSingleton<IProxyConfigProvider>(proxyProvider);
builder.Services.AddReverseProxy();

// Application services
builder.Services.AddScoped<IContainerConfigService, ContainerConfigService>();
builder.Services.AddSingleton<IDockerNetworkService, DockerNetworkService>();
builder.Services.AddScoped<IDockerOrchestratorService, DockerOrchestratorService>();
builder.Services.AddScoped<IVoiceLibraryService, VoiceLibraryService>();
builder.Services.AddHttpClient<ITtsClientService, TtsClientService>(client =>
{
    client.Timeout = TimeSpan.FromHours(5); // Match TtsJobProcessor.JobTimeout
});
builder.Services.AddScoped<ITotpService, TotpService>();
builder.Services.AddScoped<IAdminSeedService, AdminSeedService>();

// Health monitoring
builder.Services.AddSingleton<TtsJobSignal>();
builder.Services.AddHostedService<HealthMonitorService>();
builder.Services.AddSingleton<TtsJobProcessor>();
builder.Services.AddHostedService<TtsJobProcessor>(sp => sp.GetRequiredService<TtsJobProcessor>());

// SignalR
builder.Services.AddSignalR();
builder.Services.AddSingleton<IContainerLogService, ContainerLogService>();
builder.Services.AddSingleton<GpuMetricsState>();
builder.Services.AddSingleton<OrchestratorEventBus>();
builder.Services.AddSingleton<SetupDownloadService>();
builder.Services.AddSingleton<SetupSettingsService>();
builder.Services.AddMemoryCache();

var app = builder.Build();

// Run migrations, seed roles/admin, ensure Docker network
await StartupTasks.RunAsync(app);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

// Security headers
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +  // Required: Blazor Server injects inline scripts for circuit management
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " + // Required: Blazor scoped CSS + Bootstrap CDN
        "img-src 'self' data:; " +               // data: needed for TOTP QR codes
        "connect-src 'self'; " +                 // WebSocket (SignalR) + fetch to same origin
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'";
    await next();
});

app.UseRateLimiter();

// Auth middleware
app.UseAuthentication();
app.UseAuthorization();

// Custom middleware
app.UseMiddleware<SetupGuardMiddleware>();
app.UseMiddleware<PostLoginRedirectMiddleware>();
if (!string.IsNullOrWhiteSpace(domain))
{
    app.UseMiddleware<AcmeChallengeMiddleware>();
}

// Endpoint mapping
// UseAntiforgery() MUST come before any endpoint mapping so that all mapped
// endpoints (including future POST endpoints in MapAudioEndpoints) are covered.
app.UseAntiforgery();
app.MapAudioEndpoints();
app.MapReverseProxy().RequireAuthorization();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapHub<OrchestratorHub>("/hubs/orchestrator");
app.MapAuthEndpoints();

app.Run();

// Make Program accessible for WebApplicationFactory integration tests
public partial class Program
{
    internal static void ConfigurePlatformKeyProtection(IDataProtectionBuilder dpBuilder, string dpKeysPath)
    {
        var certPath = Path.Combine(dpKeysPath, "dp-key-protection.pfx");
        dpBuilder.ProtectKeysWithCertificate(LoadOrCreateProtectionCert(certPath));
    }

    // Loads the Data-Protection certificate, creating it on first run. Resilient to
    // transient IO contention: parallel integration-test hosts (one WebApplicationFactory
    // each) and AV/Defender scanners can briefly lock a freshly written `.pfx`, which made
    // CI intermittently fail on `File.WriteAllBytes`. Production builds this once per process.
    private static X509Certificate2 LoadOrCreateProtectionCert(string certPath)
    {
        const int maxAttempts = 8;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(certPath))
                    return X509CertificateLoader.LoadPkcs12FromFile(certPath, null);

                using var rsa = RSA.Create(2048);
                var req = new CertificateRequest(
                    "CN=oao-DataProtection",
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                using var created = req.CreateSelfSigned(
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(10));

                // Atomic publish: write a uniquely-named temp file then move it into place,
                // so a concurrent reader never sees a half-written `.pfx`. A losing racer's
                // Move throws (the target already exists); the next pass loads the winner's file.
                var tmp = certPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(tmp, created.Export(X509ContentType.Pfx));
                try
                {
                    File.Move(tmp, certPath);
                }
                catch
                {
                    try { File.Delete(tmp); } catch { /* best effort */ }
                    throw;
                }

                return X509CertificateLoader.LoadPkcs12FromFile(certPath, null);
            }
            catch (Exception ex) when (
                attempt < maxAttempts &&
                ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                System.Threading.Thread.Sleep(40 * attempt);
            }
        }
    }
}
