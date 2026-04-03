using Docker.DotNet;
using OpenAudioOrchestrator.Web;
using OpenAudioOrchestrator.Web.Components;
using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using OpenAudioOrchestrator.Web.Endpoints;
using OpenAudioOrchestrator.Web.Hubs;
using OpenAudioOrchestrator.Web.Middleware;
using OpenAudioOrchestrator.Web.Proxy;
using OpenAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

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
    builder.Configuration["OpenAudioOrchestrator:DataRoot"], PlatformDefaults.DataRoot);
// Store DP keys alongside the application, not in DataRoot (which can change during setup)
var dpKeysPath = Path.Combine(builder.Environment.ContentRootPath, ".dp-keys");
Directory.CreateDirectory(dpKeysPath);
var dpBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("OpenAudioOrchestrator")
    .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath));
ConfigurePlatformKeyProtection(dpBuilder, dpKeysPath);


// Build SQLite connection string (with optional SQLCipher encryption)
var connectionString = PlatformDefaults.ConfigValueOrDefault(
    builder.Configuration.GetConnectionString("Default"),
    $"Data Source={PlatformDefaults.DbPath}");
var encryptedDbKey = builder.Configuration["OpenAudioOrchestrator:DatabaseKey"];
if (!string.IsNullOrWhiteSpace(encryptedDbKey))
{
    // Decrypt the database key using Data Protection.
    // Build a temporary provider to access the protector before full DI is ready.
    var tempServices = new ServiceCollection();
    var tempDpBuilder = tempServices.AddDataProtection()
        .SetApplicationName("OpenAudioOrchestrator")
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
    opts.Cookie.Name = ".OAO.Auth";
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
        builder.Configuration["OpenAudioOrchestrator:DockerEndpoint"],
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
        if (OperatingSystem.IsWindows())
        {
            dpBuilder.ProtectKeysWithDpapi();
        }
        else
        {
            // On Linux, use a self-signed certificate instead of DPAPI for key encryption.
            var certPath = Path.Combine(dpKeysPath, "dp-key-protection.pfx");
            X509Certificate2 cert;
            if (File.Exists(certPath))
            {
                cert = new X509Certificate2(certPath);
            }
            else
            {
                using var rsa = RSA.Create(2048);
                var req = new CertificateRequest(
                    "CN=OpenAudioOrchestrator-DataProtection",
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(10));
                File.WriteAllBytes(certPath, cert.Export(X509ContentType.Pfx));
            }
            dpBuilder.ProtectKeysWithCertificate(cert);
        }
    }
}
