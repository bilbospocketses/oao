using Docker.DotNet;
using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using OpenAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;

namespace OpenAudioOrchestrator.Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    // Shared in-memory SQLite connection — stays open for the factory lifetime
    private SqliteConnection? _connection;
    private string? _testDataRoot;
    private bool _seeded;

    public const string AdminUsername = "testadmin";
    public const string AdminPassword = "Test123!@#";
    public const string UserUsername = "testuser";
    public const string UserPassword = "Test456!@#";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _testDataRoot = Path.Combine(Path.GetTempPath(), $"oao-integration-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_testDataRoot, "Output"));
        Directory.CreateDirectory(Path.Combine(_testDataRoot, "References"));

        builder.UseEnvironment("Testing");

        // Override configuration before services are built
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAudioOrchestrator:DataRoot"] = _testDataRoot,
                ["ConnectionStrings:Default"] = "Data Source=:memory:",
                ["OpenAudioOrchestrator:Domain"] = ""
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the real DbContext registrations
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<IDbContextFactory<AppDbContext>>();

            // Create a shared in-memory SQLite connection
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(_connection));
            services.AddDbContextFactory<AppDbContext>(options =>
                options.UseSqlite(_connection), ServiceLifetime.Scoped);

            // Replace Docker client with a mock
            services.RemoveAll<IDockerClient>();
            var mockDocker = new Mock<IDockerClient>();
            var mockContainers = new Mock<IContainerOperations>();
            mockDocker.Setup(d => d.Containers).Returns(mockContainers.Object);
            services.AddSingleton(mockDocker.Object);

            // Replace Docker network service with a mock
            services.RemoveAll<IDockerNetworkService>();
            var mockNetwork = new Mock<IDockerNetworkService>();
            mockNetwork.Setup(n => n.EnsureNetworkExistsAsync())
                .ReturnsAsync("test-network-id");
            services.AddSingleton(mockNetwork.Object);

            // Remove background services that need Docker/nvidia-smi
            services.RemoveAll<IHostedService>();

            // Override cookie policy for test (test server uses HTTP, not HTTPS)
            services.ConfigureApplicationCookie(opts =>
            {
                opts.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.None;
            });
        });
    }

    /// <summary>
    /// Seeds the database with roles and test users. Safe to call multiple times.
    /// </summary>
    public async Task SeedTestDataAsync()
    {
        if (_seeded) return;

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "User" })
        {
            if (!await roleMgr.RoleExistsAsync(role))
                await roleMgr.CreateAsync(new IdentityRole(role));
        }

        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        // Create admin user
        if (await userMgr.FindByNameAsync(AdminUsername) is null)
        {
            var admin = new AppUser
            {
                UserName = AdminUsername,
                DisplayName = "Test Admin",
                CreatedAt = DateTimeOffset.UtcNow
            };
            await userMgr.CreateAsync(admin, AdminPassword);
            await userMgr.AddToRoleAsync(admin, "Admin");
        }

        // Create regular user
        if (await userMgr.FindByNameAsync(UserUsername) is null)
        {
            var user = new AppUser
            {
                UserName = UserUsername,
                DisplayName = "Test User",
                CreatedAt = DateTimeOffset.UtcNow
            };
            await userMgr.CreateAsync(user, UserPassword);
            await userMgr.AddToRoleAsync(user, "User");
        }

        _seeded = true;
    }

    public string TestDataRoot => _testDataRoot!;

    /// <summary>
    /// Creates an HttpClient that does NOT follow redirects (for testing redirect responses).
    /// </summary>
    public HttpClient CreateNonRedirectClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    /// <summary>
    /// Creates an authenticated HttpClient by logging in with the given credentials.
    /// Returns the client with auth cookies set.
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(
        string username = AdminUsername, string password = AdminPassword)
    {
        var client = CreateNonRedirectClient();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password
        });

        await client.PostAsync("/api/auth/login", form);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection?.Close();
            _connection?.Dispose();
            if (_testDataRoot is not null && Directory.Exists(_testDataRoot))
            {
                try { Directory.Delete(_testDataRoot, true); } catch { }
            }
        }
    }
}
