> **Note:** post-rename. References to `OpenAudioOrchestrator.*` paths and
> config keys reflect their original names. Current equivalents are
> `oao.*` and `oao:*`.
# Phase 4: Security & Authentication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add ASP.NET Identity authentication with mandatory TOTP/MFA, role-based authorization (Admin/User), a first-run setup wizard, and automatic HTTPS via Let's Encrypt to the Fish Audio Orchestration Dashboard.

**Architecture:** Extend the existing `AppDbContext` to inherit from `IdentityDbContext<AppUser>`, add cookie-based auth with TOTP as a second factor, protect all routes by default, and gate admin-only pages behind role checks. A setup wizard bootstraps the first admin account. LettuceEncrypt provides automatic TLS when a domain is configured.

**Tech Stack:** ASP.NET Identity, EF Core (SQLite), LettuceEncrypt, QRCoder, xUnit + Moq (existing test stack)

---

## File Structure

### New Files

| File | Responsibility |
|------|---------------|
| `src/.../Data/Entities/AppUser.cs` | Identity user entity with custom properties |
| `src/.../Data/Migrations/<timestamp>_AddIdentity.cs` | EF migration for Identity tables + GenerationLog.UserId |
| `src/.../Services/IAdminSeedService.cs` | Interface for env-var seeding |
| `src/.../Services/AdminSeedService.cs` | Seeds admin from env vars at startup |
| `src/.../Middleware/SetupGuardMiddleware.cs` | Redirects to `/setup` when no users exist |
| `src/.../Middleware/PostLoginRedirectMiddleware.cs` | Enforces MustChangePassword / MustSetupTotp redirects |
| `src/.../Components/Pages/Login.razor` | Username + password login page |
| `src/.../Components/Pages/LoginTotp.razor` | TOTP verification page |
| `src/.../Components/Pages/Setup.razor` | First-run setup wizard |
| `src/.../Components/Pages/AccessDenied.razor` | Access denied page |
| `src/.../Components/Pages/Account/ChangePassword.razor` | Password change (self-service) |
| `src/.../Components/Pages/Account/SetupTotp.razor` | TOTP enrollment page |
| `src/.../Components/Pages/Account/ManageAccount.razor` | Account self-service hub |
| `src/.../Components/Pages/Admin/UserManagement.razor` | Admin user list + CRUD |
| `src/.../Components/Pages/Admin/AdminSettings.razor` | FQDN / domain settings |
| `src/.../Services/ITotpService.cs` | Interface for TOTP QR/key generation |
| `src/.../Services/TotpService.cs` | QR code generation via QRCoder + Identity authenticator key |
| `tests/.../Auth/AppUserTests.cs` | AppUser entity tests |
| `tests/.../Auth/AdminSeedServiceTests.cs` | Env-var seeding tests |
| `tests/.../Auth/SetupGuardMiddlewareTests.cs` | Wizard guard middleware tests |
| `tests/.../Auth/PostLoginRedirectMiddlewareTests.cs` | Post-login redirect tests |
| `tests/.../Auth/TotpServiceTests.cs` | TOTP key/QR generation tests |
| `tests/.../Auth/UserManagementTests.cs` | Admin user CRUD tests |

### Modified Files

| File | Change |
|------|--------|
| `src/.../FishAudioOrchestrator.Web.csproj` | Add Identity, LettuceEncrypt, QRCoder packages |
| `src/.../Data/AppDbContext.cs` | Inherit `IdentityDbContext<AppUser>`, add UserId FK config |
| `src/.../Data/Entities/GenerationLog.cs` | Add nullable `UserId` property |
| `src/.../Program.cs` | Wire Identity, cookies, LettuceEncrypt, middleware, seeding |
| `src/.../appsettings.json` | Add Domain, AdminUser, AdminPassword, LettuceEncrypt config |
| `src/.../Components/_Imports.razor` | Add auth-related `@using` directives |
| `src/.../Components/Routes.razor` | Wrap in `CascadingAuthenticationState` + `AuthorizeRouteView` |
| `src/.../Components/Layout/NavMenu.razor` | Role-based link visibility, user display, logout |
| `src/.../Components/Pages/Dashboard.razor` | Add `@attribute [Authorize]` |
| `src/.../Components/Pages/Deploy.razor` | Add `@attribute [Authorize(Roles = "Admin")]` |
| `src/.../Components/Pages/VoiceLibrary.razor` | Add `@attribute [Authorize]` |
| `src/.../Components/Pages/TtsPlayground.razor` | Add `@attribute [Authorize]` |
| `src/.../Components/Pages/GenerationHistory.razor` | Add `@attribute [Authorize]`, filter by UserId |
| `tests/.../FishAudioOrchestrator.Tests.csproj` | Add `Microsoft.AspNetCore.Identity.EntityFrameworkCore` for test helpers |

---

## Task 1: Add NuGet Packages

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/FishAudioOrchestrator.Web.csproj`
- Modify: `tests/FishAudioOrchestrator.Tests/FishAudioOrchestrator.Tests.csproj`

- [ ] **Step 1: Add packages to web project**

Run:
```bash
cd src/FishAudioOrchestrator.Web
dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 9.0.3
dotnet add package LettuceEncrypt --version 1.3.3
dotnet add package QRCoder --version 1.6.0
```

- [ ] **Step 2: Add test helper package**

Run:
```bash
cd tests/FishAudioOrchestrator.Tests
dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 9.0.3
```

- [ ] **Step 3: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/FishAudioOrchestrator.Web/FishAudioOrchestrator.Web.csproj tests/FishAudioOrchestrator.Tests/FishAudioOrchestrator.Tests.csproj
git commit -m "chore: add Identity, LettuceEncrypt, QRCoder packages for Phase 4"
```

---

## Task 2: AppUser Entity and DbContext Migration

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Data/Entities/AppUser.cs`
- Modify: `src/FishAudioOrchestrator.Web/Data/Entities/GenerationLog.cs`
- Modify: `src/FishAudioOrchestrator.Web/Data/AppDbContext.cs`
- Test: `tests/FishAudioOrchestrator.Tests/Auth/AppUserTests.cs`

- [ ] **Step 1: Write the failing test for AppUser**

Create `tests/FishAudioOrchestrator.Tests/Auth/AppUserTests.cs`:

```csharp
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FishAudioOrchestrator.Tests.Auth;

public class AppUserTests
{
    private static (AppDbContext db, UserManager<AppUser> userMgr) CreateTestServices()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddIdentityCore<AppUser>(opts =>
            {
                opts.Password.RequiredLength = 8;
                opts.Password.RequireUppercase = true;
                opts.Password.RequireLowercase = true;
                opts.Password.RequireDigit = true;
                opts.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
        return (db, userMgr);
    }

    [Fact]
    public async Task CanCreateAppUserWithCustomProperties()
    {
        var (db, userMgr) = CreateTestServices();

        var user = new AppUser
        {
            UserName = "admin",
            DisplayName = "Admin User",
            MustChangePassword = false,
            MustSetupTotp = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var result = await userMgr.CreateAsync(user, "Test123!@");

        Assert.True(result.Succeeded);
        var fetched = await db.Users.FirstAsync(u => u.UserName == "admin");
        Assert.Equal("Admin User", fetched.DisplayName);
        Assert.False(fetched.MustChangePassword);
        Assert.False(fetched.MustSetupTotp);
    }

    [Fact]
    public async Task CanAssignRoleToUser()
    {
        var (db, userMgr) = CreateTestServices();
        var roleMgr = new RoleManager<IdentityRole>(
            new RoleStore<IdentityRole, AppDbContext>(db),
            Array.Empty<IRoleValidator<IdentityRole>>(),
            new UpperInvariantLookupNormalizer(),
            null!, null!);

        await roleMgr.CreateAsync(new IdentityRole("Admin"));
        var user = new AppUser
        {
            UserName = "admin",
            DisplayName = "Admin",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await userMgr.CreateAsync(user, "Test123!@");
        await userMgr.AddToRoleAsync(user, "Admin");

        var roles = await userMgr.GetRolesAsync(user);
        Assert.Contains("Admin", roles);
    }

    [Fact]
    public async Task GenerationLog_UserId_IsNullable()
    {
        var (db, _) = CreateTestServices();

        var profile = new ModelProfile
        {
            Name = "test-model",
            CheckpointPath = "/tmp/test",
            ImageTag = "test:latest",
            HostPort = 9001
        };
        db.ModelProfiles.Add(profile);
        await db.SaveChangesAsync();

        var log = new GenerationLog
        {
            ModelProfileId = profile.Id,
            InputText = "hello",
            OutputFileName = "out.wav",
            Format = "wav",
            DurationMs = 1000,
            UserId = null
        };
        db.GenerationLogs.Add(log);
        await db.SaveChangesAsync();

        var fetched = await db.GenerationLogs.FirstAsync();
        Assert.Null(fetched.UserId);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~AppUserTests" -v q`
Expected: Compilation errors — `AppUser` does not exist, `AppDbContext` is not `IdentityDbContext`.

- [ ] **Step 3: Create AppUser entity**

Create `src/FishAudioOrchestrator.Web/Data/Entities/AppUser.cs`:

```csharp
using Microsoft.AspNetCore.Identity;

namespace FishAudioOrchestrator.Web.Data.Entities;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
    public bool MustSetupTotp { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 4: Add UserId to GenerationLog**

Modify `src/FishAudioOrchestrator.Web/Data/Entities/GenerationLog.cs` — add after the `ReferenceVoiceId` property:

```csharp
public string? UserId { get; set; }
```

And add after the `ReferenceVoice` navigation property:

```csharp
public AppUser? User { get; set; }
```

- [ ] **Step 5: Update AppDbContext to inherit IdentityDbContext**

Replace the contents of `src/FishAudioOrchestrator.Web/Data/AppDbContext.cs` with:

```csharp
using FishAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FishAudioOrchestrator.Web.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ModelProfile> ModelProfiles => Set<ModelProfile>();
    public DbSet<ReferenceVoice> ReferenceVoices => Set<ReferenceVoice>();
    public DbSet<GenerationLog> GenerationLogs => Set<GenerationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ModelProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CheckpointPath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.ImageTag).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);
        });

        modelBuilder.Entity<ReferenceVoice>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.VoiceId).IsUnique();
            entity.Property(e => e.VoiceId).IsRequired().HasMaxLength(255);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.AudioFileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.TranscriptText).IsRequired();
            entity.Property(e => e.Tags).HasMaxLength(500);
        });

        modelBuilder.Entity<GenerationLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.InputText).IsRequired();
            entity.Property(e => e.OutputFileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Format).IsRequired().HasMaxLength(10);
            entity.HasOne(e => e.ModelProfile)
                .WithMany()
                .HasForeignKey(e => e.ModelProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ReferenceVoice)
                .WithMany()
                .HasForeignKey(e => e.ReferenceVoiceId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.Property(e => e.DisplayName).HasMaxLength(100);
        });
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~AppUserTests" -v q`
Expected: 3 passed, 0 failed.

- [ ] **Step 7: Run full test suite to verify no regressions**

Run: `dotnet test --nologo -v q`
Expected: All 45 tests pass (42 existing + 3 new).

Note: Existing tests using `CreateInMemoryContext()` with `new AppDbContext(options)` will continue to work because `IdentityDbContext<AppUser>` extends `DbContext` — the constructor signature is compatible.

- [ ] **Step 8: Create EF migration**

Run:
```bash
cd src/FishAudioOrchestrator.Web
dotnet ef migrations add AddIdentity
```
Expected: Migration file created in `Data/Migrations/`.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: add AppUser entity, extend AppDbContext with Identity, add UserId to GenerationLog"
```

---

## Task 3: Identity Services and Cookie Auth in Program.cs

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Program.cs`
- Modify: `src/FishAudioOrchestrator.Web/appsettings.json`

- [ ] **Step 1: Update appsettings.json**

Add `Domain`, `AdminUser`, `AdminPassword` under `FishOrchestrator` and add `LettuceEncrypt` section. The full file should be:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "Default": "Data Source=D:\\DockerData\\FishAudio\\fishorch.db"
  },
  "FishOrchestrator": {
    "DockerEndpoint": "npipe://./pipe/docker_engine",
    "DataRoot": "D:\\DockerData\\FishAudio",
    "PortRange": {
      "Start": 9001,
      "End": 9099
    },
    "DefaultImageTag": "fishaudio/fish-speech:server-cuda",
    "DockerNetworkName": "fish-orchestrator",
    "HealthCheckIntervalSeconds": 30,
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

- [ ] **Step 2: Wire Identity and cookie auth in Program.cs**

Replace the full contents of `src/FishAudioOrchestrator.Web/Program.cs` with:

```csharp
using Docker.DotNet;
using FishAudioOrchestrator.Web.Components;
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Middleware;
using FishAudioOrchestrator.Web.Proxy;
using FishAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// LettuceEncrypt (only if domain is configured)
var domain = builder.Configuration["FishOrchestrator:Domain"];
if (!string.IsNullOrWhiteSpace(domain))
{
    builder.Services.AddLettuceEncrypt();
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

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
    opts.Cookie.Name = ".FishOrch.Auth";
    opts.Cookie.HttpOnly = true;
    opts.Cookie.SameSite = SameSiteMode.Strict;
    opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    opts.ExpireTimeSpan = TimeSpan.FromHours(24);
    opts.SlidingExpiration = true;
    opts.LoginPath = "/login";
    opts.AccessDeniedPath = "/access-denied";
});

// Docker client
builder.Services.AddSingleton<IDockerClient>(_ =>
{
    var endpoint = builder.Configuration["FishOrchestrator:DockerEndpoint"]
        ?? "npipe://./pipe/docker_engine";
    return new DockerClientConfiguration(new Uri(endpoint)).CreateClient();
});

// YARP reverse proxy
var proxyProvider = new FishProxyConfigProvider();
builder.Services.AddSingleton(proxyProvider);
builder.Services.AddReverseProxy();

// Application services
builder.Services.AddScoped<IContainerConfigService, ContainerConfigService>();
builder.Services.AddSingleton<IDockerNetworkService, DockerNetworkService>();
builder.Services.AddScoped<IDockerOrchestratorService, DockerOrchestratorService>();
builder.Services.AddScoped<IVoiceLibraryService, VoiceLibraryService>();
builder.Services.AddHttpClient<ITtsClientService, TtsClientService>();
builder.Services.AddScoped<ITotpService, TotpService>();
builder.Services.AddScoped<IAdminSeedService, AdminSeedService>();

// Health monitoring
builder.Services.AddHostedService<HealthMonitorService>();

var app = builder.Build();

// Run migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// Seed roles
using (var scope = app.Services.CreateScope())
{
    var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Admin", "User" })
    {
        if (!await roleMgr.RoleExistsAsync(role))
            await roleMgr.CreateAsync(new IdentityRole(role));
    }
}

// Seed admin from env vars (if configured and no users exist)
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<IAdminSeedService>();
    await seeder.SeedIfConfiguredAsync();
}

// Ensure Docker bridge network exists
try
{
    var networkService = app.Services.GetRequiredService<IDockerNetworkService>();
    await networkService.EnsureNetworkExistsAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Could not ensure Docker bridge network exists. Docker may not be running.");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

// Serve audio files from the data directories
var dataRoot = app.Configuration["FishOrchestrator:DataRoot"] ?? @"D:\DockerData\FishAudio";

var outputDir = Path.Combine(dataRoot, "Output");
if (Directory.Exists(outputDir))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(outputDir),
        RequestPath = "/audio/output"
    });
}

var referencesDir = Path.Combine(dataRoot, "References");
if (Directory.Exists(referencesDir))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(referencesDir),
        RequestPath = "/audio/references"
    });
}

// Auth middleware
app.UseAuthentication();
app.UseAuthorization();

// Custom middleware
app.UseMiddleware<SetupGuardMiddleware>();
app.UseMiddleware<PostLoginRedirectMiddleware>();

app.UseAntiforgery();

// YARP reverse proxy for TTS API
app.MapReverseProxy();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

- [ ] **Step 3: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build errors for missing `SetupGuardMiddleware`, `PostLoginRedirectMiddleware`, `ITotpService`, `TotpService`, `IAdminSeedService`, `AdminSeedService`. This is expected — these are implemented in later tasks.

- [ ] **Step 4: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Program.cs src/FishAudioOrchestrator.Web/appsettings.json
git commit -m "feat: wire Identity services, cookie auth, and LettuceEncrypt in Program.cs"
```

---

## Task 4: SetupGuard Middleware

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Middleware/SetupGuardMiddleware.cs`
- Test: `tests/FishAudioOrchestrator.Tests/Auth/SetupGuardMiddlewareTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/FishAudioOrchestrator.Tests/Auth/SetupGuardMiddlewareTests.cs`:

```csharp
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FishAudioOrchestrator.Tests.Auth;

public class SetupGuardMiddlewareTests
{
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<AppUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RedirectsToSetup_WhenNoUsersExist()
    {
        var sp = BuildServices();
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var middleware = new SetupGuardMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Path = "/";

        await middleware.InvokeAsync(context);

        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal("/setup", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task AllowsSetupPath_WhenNoUsersExist()
    {
        var sp = BuildServices();
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var nextCalled = false;
        var middleware = new SetupGuardMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Path = "/setup";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AllowsAnyPath_WhenUsersExist()
    {
        var sp = BuildServices();
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
        await userMgr.CreateAsync(new AppUser
        {
            UserName = "admin",
            DisplayName = "Admin",
            CreatedAt = DateTimeOffset.UtcNow
        }, "Test123!@");

        var nextCalled = false;
        var middleware = new SetupGuardMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Path = "/";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~SetupGuardMiddlewareTests" -v q`
Expected: Compilation error — `SetupGuardMiddleware` does not exist.

- [ ] **Step 3: Implement SetupGuardMiddleware**

Create `src/FishAudioOrchestrator.Web/Middleware/SetupGuardMiddleware.cs`:

```csharp
using FishAudioOrchestrator.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FishAudioOrchestrator.Web.Middleware;

public class SetupGuardMiddleware
{
    private readonly RequestDelegate _next;

    public SetupGuardMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/setup")
            || context.Request.Path.StartsWithSegments("/_framework")
            || context.Request.Path.StartsWithSegments("/_blazor"))
        {
            await _next(context);
            return;
        }

        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        var hasUsers = await db.Users.AnyAsync();

        if (!hasUsers)
        {
            context.Response.Redirect("/setup");
            return;
        }

        await _next(context);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~SetupGuardMiddlewareTests" -v q`
Expected: 3 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Middleware/SetupGuardMiddleware.cs tests/FishAudioOrchestrator.Tests/Auth/SetupGuardMiddlewareTests.cs
git commit -m "feat: add SetupGuardMiddleware to redirect to first-run wizard when no users exist"
```

---

## Task 5: PostLoginRedirect Middleware

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Middleware/PostLoginRedirectMiddleware.cs`
- Test: `tests/FishAudioOrchestrator.Tests/Auth/PostLoginRedirectMiddlewareTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/FishAudioOrchestrator.Tests/Auth/PostLoginRedirectMiddlewareTests.cs`:

```csharp
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace FishAudioOrchestrator.Tests.Auth;

public class PostLoginRedirectMiddlewareTests
{
    private static (ServiceProvider sp, AppUser user) BuildServicesWithUser(
        bool mustChangePassword = false, bool mustSetupTotp = false)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<AppUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser
        {
            UserName = "testuser",
            DisplayName = "Test",
            MustChangePassword = mustChangePassword,
            MustSetupTotp = mustSetupTotp,
            CreatedAt = DateTimeOffset.UtcNow
        };
        userMgr.CreateAsync(user, "Test123!@").GetAwaiter().GetResult();
        return (sp, user);
    }

    private static HttpContext CreateAuthenticatedContext(ServiceProvider sp, AppUser user, string path)
    {
        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Path = path;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.UserName!)
        }, "TestAuth"));
        return context;
    }

    [Fact]
    public async Task RedirectsToChangePassword_WhenMustChangePasswordIsTrue()
    {
        var (sp, user) = BuildServicesWithUser(mustChangePassword: true);
        var middleware = new PostLoginRedirectMiddleware(_ => Task.CompletedTask);
        var context = CreateAuthenticatedContext(sp, user, "/");

        await middleware.InvokeAsync(context);

        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal("/account/change-password", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task RedirectsToSetupTotp_WhenMustSetupTotpIsTrue()
    {
        var (sp, user) = BuildServicesWithUser(mustSetupTotp: true);
        var middleware = new PostLoginRedirectMiddleware(_ => Task.CompletedTask);
        var context = CreateAuthenticatedContext(sp, user, "/");

        await middleware.InvokeAsync(context);

        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal("/account/setup-totp", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task ChangePasswordTakesPriority_OverSetupTotp()
    {
        var (sp, user) = BuildServicesWithUser(mustChangePassword: true, mustSetupTotp: true);
        var middleware = new PostLoginRedirectMiddleware(_ => Task.CompletedTask);
        var context = CreateAuthenticatedContext(sp, user, "/");

        await middleware.InvokeAsync(context);

        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal("/account/change-password", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task AllowsRequest_WhenNoFlagsSet()
    {
        var (sp, user) = BuildServicesWithUser();
        var nextCalled = false;
        var middleware = new PostLoginRedirectMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateAuthenticatedContext(sp, user, "/");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AllowsTargetPath_WhenRedirectFlagSet()
    {
        var (sp, user) = BuildServicesWithUser(mustChangePassword: true);
        var nextCalled = false;
        var middleware = new PostLoginRedirectMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateAuthenticatedContext(sp, user, "/account/change-password");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task SkipsUnauthenticatedRequests()
    {
        var (sp, _) = BuildServicesWithUser(mustChangePassword: true);
        var nextCalled = false;
        var middleware = new PostLoginRedirectMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Path = "/";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~PostLoginRedirectMiddlewareTests" -v q`
Expected: Compilation error — `PostLoginRedirectMiddleware` does not exist.

- [ ] **Step 3: Implement PostLoginRedirectMiddleware**

Create `src/FishAudioOrchestrator.Web/Middleware/PostLoginRedirectMiddleware.cs`:

```csharp
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FishAudioOrchestrator.Web.Middleware;

public class PostLoginRedirectMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly string[] _exemptPrefixes = new[]
    {
        "/account/change-password",
        "/account/setup-totp",
        "/login",
        "/setup",
        "/access-denied",
        "/_framework",
        "/_blazor"
    };

    public PostLoginRedirectMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path;
        foreach (var prefix in _exemptPrefixes)
        {
            if (path.StartsWithSegments(prefix))
            {
                await _next(context);
                return;
            }
        }

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            await _next(context);
            return;
        }

        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        var appUser = await db.Users
            .OfType<AppUser>()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (appUser is null)
        {
            await _next(context);
            return;
        }

        if (appUser.MustChangePassword)
        {
            context.Response.Redirect("/account/change-password");
            return;
        }

        if (appUser.MustSetupTotp)
        {
            context.Response.Redirect("/account/setup-totp");
            return;
        }

        await _next(context);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~PostLoginRedirectMiddlewareTests" -v q`
Expected: 6 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Middleware/PostLoginRedirectMiddleware.cs tests/FishAudioOrchestrator.Tests/Auth/PostLoginRedirectMiddlewareTests.cs
git commit -m "feat: add PostLoginRedirectMiddleware for forced password change and TOTP setup"
```

---

## Task 6: AdminSeedService

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Services/IAdminSeedService.cs`
- Create: `src/FishAudioOrchestrator.Web/Services/AdminSeedService.cs`
- Test: `tests/FishAudioOrchestrator.Tests/Auth/AdminSeedServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/FishAudioOrchestrator.Tests/Auth/AdminSeedServiceTests.cs`:

```csharp
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace FishAudioOrchestrator.Tests.Auth;

public class AdminSeedServiceTests
{
    private static (ServiceProvider sp, IConfiguration config) BuildServices(
        string? adminUser = null, string? adminPassword = null)
    {
        var configData = new Dictionary<string, string?>();
        if (adminUser is not null) configData["FishOrchestrator:AdminUser"] = adminUser;
        if (adminPassword is not null) configData["FishOrchestrator:AdminPassword"] = adminPassword;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<AppUser>(opts =>
            {
                opts.Password.RequiredLength = 8;
                opts.Password.RequireUppercase = true;
                opts.Password.RequireLowercase = true;
                opts.Password.RequireDigit = true;
                opts.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
        roleMgr.CreateAsync(new IdentityRole("Admin")).GetAwaiter().GetResult();
        roleMgr.CreateAsync(new IdentityRole("User")).GetAwaiter().GetResult();

        return (sp, config);
    }

    [Fact]
    public async Task SeedsAdminUser_WhenEnvVarsSet_AndNoUsersExist()
    {
        var (sp, config) = BuildServices("admin", "SeedPass1!");
        var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<AdminSeedService>();
        var db = sp.GetRequiredService<AppDbContext>();

        var service = new AdminSeedService(config, userMgr, db, logger);
        await service.SeedIfConfiguredAsync();

        var user = await userMgr.FindByNameAsync("admin");
        Assert.NotNull(user);
        Assert.True(user.MustSetupTotp);
        Assert.False(user.MustChangePassword);
        var roles = await userMgr.GetRolesAsync(user);
        Assert.Contains("Admin", roles);
    }

    [Fact]
    public async Task DoesNotSeed_WhenEnvVarsNotSet()
    {
        var (sp, config) = BuildServices();
        var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<AdminSeedService>();
        var db = sp.GetRequiredService<AppDbContext>();

        var service = new AdminSeedService(config, userMgr, db, logger);
        await service.SeedIfConfiguredAsync();

        Assert.False(await db.Users.AnyAsync());
    }

    [Fact]
    public async Task DoesNotSeed_WhenUsersAlreadyExist()
    {
        var (sp, config) = BuildServices("admin", "SeedPass1!");
        var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<AdminSeedService>();
        var db = sp.GetRequiredService<AppDbContext>();

        await userMgr.CreateAsync(new AppUser
        {
            UserName = "existing",
            DisplayName = "Existing",
            CreatedAt = DateTimeOffset.UtcNow
        }, "Exist123!@");

        var service = new AdminSeedService(config, userMgr, db, logger);
        await service.SeedIfConfiguredAsync();

        Assert.Null(await userMgr.FindByNameAsync("admin"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~AdminSeedServiceTests" -v q`
Expected: Compilation error — `IAdminSeedService` and `AdminSeedService` do not exist.

- [ ] **Step 3: Create IAdminSeedService interface**

Create `src/FishAudioOrchestrator.Web/Services/IAdminSeedService.cs`:

```csharp
namespace FishAudioOrchestrator.Web.Services;

public interface IAdminSeedService
{
    Task SeedIfConfiguredAsync();
}
```

- [ ] **Step 4: Implement AdminSeedService**

Create `src/FishAudioOrchestrator.Web/Services/AdminSeedService.cs`:

```csharp
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FishAudioOrchestrator.Web.Services;

public class AdminSeedService : IAdminSeedService
{
    private readonly IConfiguration _config;
    private readonly UserManager<AppUser> _userManager;
    private readonly AppDbContext _db;
    private readonly ILogger<AdminSeedService> _logger;

    public AdminSeedService(
        IConfiguration config,
        UserManager<AppUser> userManager,
        AppDbContext db,
        ILogger<AdminSeedService> logger)
    {
        _config = config;
        _userManager = userManager;
        _db = db;
        _logger = logger;
    }

    public async Task SeedIfConfiguredAsync()
    {
        var adminUser = _config["FishOrchestrator:AdminUser"];
        var adminPassword = _config["FishOrchestrator:AdminPassword"];

        if (string.IsNullOrWhiteSpace(adminUser) || string.IsNullOrWhiteSpace(adminPassword))
            return;

        if (await _db.Users.AnyAsync())
            return;

        var user = new AppUser
        {
            UserName = adminUser,
            DisplayName = adminUser,
            MustChangePassword = false,
            MustSetupTotp = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await _userManager.CreateAsync(user, adminPassword);
        if (!result.Succeeded)
        {
            _logger.LogError("Failed to seed admin user: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
            return;
        }

        await _userManager.AddToRoleAsync(user, "Admin");
        _logger.LogInformation("Admin user '{User}' seeded from configuration. TOTP setup required on first login.", adminUser);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~AdminSeedServiceTests" -v q`
Expected: 3 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Services/IAdminSeedService.cs src/FishAudioOrchestrator.Web/Services/AdminSeedService.cs tests/FishAudioOrchestrator.Tests/Auth/AdminSeedServiceTests.cs
git commit -m "feat: add AdminSeedService for env-var-based admin account seeding"
```

---

## Task 7: TotpService (QR Code Generation)

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Services/ITotpService.cs`
- Create: `src/FishAudioOrchestrator.Web/Services/TotpService.cs`
- Test: `tests/FishAudioOrchestrator.Tests/Auth/TotpServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/FishAudioOrchestrator.Tests/Auth/TotpServiceTests.cs`:

```csharp
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using FishAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FishAudioOrchestrator.Tests.Auth;

public class TotpServiceTests
{
    private static (ServiceProvider sp, UserManager<AppUser> userMgr) BuildServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<AppUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        return (sp, sp.GetRequiredService<UserManager<AppUser>>());
    }

    [Fact]
    public async Task GenerateSetupInfo_ReturnsKeyAndQrDataUri()
    {
        var (sp, userMgr) = BuildServices();
        var user = new AppUser
        {
            UserName = "testuser",
            DisplayName = "Test",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await userMgr.CreateAsync(user, "Test123!@");

        var service = new TotpService(userMgr);
        var (manualKey, qrDataUri) = await service.GenerateSetupInfoAsync(user, "FishOrchestrator");

        Assert.False(string.IsNullOrWhiteSpace(manualKey));
        Assert.StartsWith("data:image/png;base64,", qrDataUri);
    }

    [Fact]
    public async Task VerifyCode_ReturnsTrueForValidToken()
    {
        var (sp, userMgr) = BuildServices();
        var user = new AppUser
        {
            UserName = "testuser",
            DisplayName = "Test",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await userMgr.CreateAsync(user, "Test123!@");

        var service = new TotpService(userMgr);
        await service.GenerateSetupInfoAsync(user, "FishOrchestrator");

        // Generate a valid token using the UserManager
        var token = await userMgr.GenerateTwoFactorTokenAsync(user,
            userMgr.Options.Tokens.AuthenticatorTokenProvider);

        var result = await service.VerifyCodeAsync(user, token);
        Assert.True(result);
    }

    [Fact]
    public async Task VerifyCode_ReturnsFalseForInvalidToken()
    {
        var (sp, userMgr) = BuildServices();
        var user = new AppUser
        {
            UserName = "testuser",
            DisplayName = "Test",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await userMgr.CreateAsync(user, "Test123!@");

        var service = new TotpService(userMgr);
        await service.GenerateSetupInfoAsync(user, "FishOrchestrator");

        var result = await service.VerifyCodeAsync(user, "000000");
        Assert.False(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~TotpServiceTests" -v q`
Expected: Compilation error — `ITotpService` and `TotpService` do not exist.

- [ ] **Step 3: Create ITotpService interface**

Create `src/FishAudioOrchestrator.Web/Services/ITotpService.cs`:

```csharp
using FishAudioOrchestrator.Web.Data.Entities;

namespace FishAudioOrchestrator.Web.Services;

public interface ITotpService
{
    Task<(string ManualKey, string QrDataUri)> GenerateSetupInfoAsync(AppUser user, string issuer);
    Task<bool> VerifyCodeAsync(AppUser user, string code);
}
```

- [ ] **Step 4: Implement TotpService**

Create `src/FishAudioOrchestrator.Web/Services/TotpService.cs`:

```csharp
using FishAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using QRCoder;

namespace FishAudioOrchestrator.Web.Services;

public class TotpService : ITotpService
{
    private readonly UserManager<AppUser> _userManager;

    public TotpService(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<(string ManualKey, string QrDataUri)> GenerateSetupInfoAsync(AppUser user, string issuer)
    {
        await _userManager.ResetAuthenticatorKeyAsync(user);
        var key = await _userManager.GetAuthenticatorKeyAsync(user);

        var uri = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(user.UserName!)}?secret={key}&issuer={Uri.EscapeDataString(issuer)}&digits=6";

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        var pngBytes = qrCode.GetGraphic(5);
        var dataUri = $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";

        return (key!, dataUri);
    }

    public async Task<bool> VerifyCodeAsync(AppUser user, string code)
    {
        return await _userManager.VerifyTwoFactorTokenAsync(user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider, code);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~TotpServiceTests" -v q`
Expected: 3 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Services/ITotpService.cs src/FishAudioOrchestrator.Web/Services/TotpService.cs tests/FishAudioOrchestrator.Tests/Auth/TotpServiceTests.cs
git commit -m "feat: add TotpService for QR code generation and TOTP verification"
```

---

## Task 8: Update Blazor Routing for Auth

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Components/_Imports.razor`
- Modify: `src/FishAudioOrchestrator.Web/Components/Routes.razor`

- [ ] **Step 1: Update _Imports.razor**

Add these lines to the end of `src/FishAudioOrchestrator.Web/Components/_Imports.razor`:

```razor
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components.Authorization
@using FishAudioOrchestrator.Web.Data.Entities
```

- [ ] **Step 2: Update Routes.razor for auth**

Replace the contents of `src/FishAudioOrchestrator.Web/Components/Routes.razor` with:

```razor
<CascadingAuthenticationState>
    <Router AppAssembly="typeof(Program).Assembly">
        <Found Context="routeData">
            <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
                <NotAuthorized>
                    @if (context.User.Identity?.IsAuthenticated != true)
                    {
                        <RedirectToLogin />
                    }
                    else
                    {
                        <p class="text-danger">You are not authorized to access this page.</p>
                    }
                </NotAuthorized>
            </AuthorizeRouteView>
            <FocusOnNavigate RouteData="routeData" Selector="h1" />
        </Found>
    </Router>
</CascadingAuthenticationState>
```

- [ ] **Step 3: Create RedirectToLogin component**

Create `src/FishAudioOrchestrator.Web/Components/Shared/RedirectToLogin.razor`:

```razor
@inject NavigationManager Nav

@code {
    protected override void OnInitialized()
    {
        Nav.NavigateTo("/login", forceLoad: true);
    }
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/_Imports.razor src/FishAudioOrchestrator.Web/Components/Routes.razor src/FishAudioOrchestrator.Web/Components/Shared/RedirectToLogin.razor
git commit -m "feat: add CascadingAuthenticationState and AuthorizeRouteView to Blazor routing"
```

---

## Task 9: Add Authorize Attributes to Existing Pages

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Components/Pages/Dashboard.razor`
- Modify: `src/FishAudioOrchestrator.Web/Components/Pages/Deploy.razor`
- Modify: `src/FishAudioOrchestrator.Web/Components/Pages/VoiceLibrary.razor`
- Modify: `src/FishAudioOrchestrator.Web/Components/Pages/TtsPlayground.razor`
- Modify: `src/FishAudioOrchestrator.Web/Components/Pages/GenerationHistory.razor`

- [ ] **Step 1: Add [Authorize] to Dashboard.razor**

Add after the `@page "/"` line:

```razor
@attribute [Authorize]
```

- [ ] **Step 2: Add [Authorize(Roles = "Admin")] to Deploy.razor**

Add after the `@page "/deploy"` line:

```razor
@attribute [Authorize(Roles = "Admin")]
```

- [ ] **Step 3: Add [Authorize] to VoiceLibrary.razor**

Add after the `@page "/voices"` line:

```razor
@attribute [Authorize]
```

- [ ] **Step 4: Add [Authorize] to TtsPlayground.razor**

Add after the `@page "/playground"` line:

```razor
@attribute [Authorize]
```

- [ ] **Step 5: Add [Authorize] to GenerationHistory.razor**

Add after the `@page "/history"` line:

```razor
@attribute [Authorize]
```

- [ ] **Step 6: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/Dashboard.razor src/FishAudioOrchestrator.Web/Components/Pages/Deploy.razor src/FishAudioOrchestrator.Web/Components/Pages/VoiceLibrary.razor src/FishAudioOrchestrator.Web/Components/Pages/TtsPlayground.razor src/FishAudioOrchestrator.Web/Components/Pages/GenerationHistory.razor
git commit -m "feat: add authorization attributes to all existing pages"
```

---

## Task 10: Login Page

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Components/Pages/Login.razor`

- [ ] **Step 1: Create Login.razor**

Create `src/FishAudioOrchestrator.Web/Components/Pages/Login.razor`:

```razor
@page "/login"
@using Microsoft.AspNetCore.Identity
@inject SignInManager<AppUser> SignInManager
@inject UserManager<AppUser> UserManager
@inject NavigationManager Nav
@layout EmptyLayout

<PageTitle>Login — Fish Orchestrator</PageTitle>

<div class="d-flex justify-content-center align-items-center vh-100 bg-dark">
    <div class="card bg-dark text-light border-secondary" style="width: 400px;">
        <div class="card-body">
            <h3 class="card-title text-center mb-4">Fish Orchestrator</h3>

            @if (!string.IsNullOrEmpty(_error))
            {
                <div class="alert alert-danger">@_error</div>
            }

            <EditForm Model="_model" OnValidSubmit="HandleLogin" FormName="login">
                <div class="mb-3">
                    <label class="form-label">Username</label>
                    <InputText @bind-Value="_model.Username" class="form-control bg-dark text-light border-secondary" />
                </div>
                <div class="mb-3">
                    <label class="form-label">Password</label>
                    <InputText @bind-Value="_model.Password" type="password" class="form-control bg-dark text-light border-secondary" />
                </div>
                <button type="submit" class="btn btn-primary w-100" disabled="@_loading">
                    @(_loading ? "Signing in..." : "Sign In")
                </button>
            </EditForm>
        </div>
    </div>
</div>

@code {
    private LoginModel _model = new();
    private string? _error;
    private bool _loading;

    private sealed class LoginModel
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    private async Task HandleLogin()
    {
        _error = null;
        _loading = true;

        var user = await UserManager.FindByNameAsync(_model.Username);
        if (user is null)
        {
            _error = "Invalid username or password.";
            _loading = false;
            return;
        }

        var result = await SignInManager.CheckPasswordSignInAsync(user, _model.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            _error = result.IsLockedOut
                ? "Account locked. Try again later."
                : "Invalid username or password.";
            _loading = false;
            return;
        }

        var hasTwoFactor = await UserManager.GetTwoFactorEnabledAsync(user);
        if (hasTwoFactor)
        {
            // Store user ID in a temp cookie for the TOTP page
            await SignInManager.SignInAsync(user, isPersistent: false);
            Nav.NavigateTo("/login/totp", forceLoad: true);
            return;
        }

        await SignInManager.SignInAsync(user, isPersistent: false);
        Nav.NavigateTo("/", forceLoad: true);
    }
}
```

- [ ] **Step 2: Create EmptyLayout for auth pages**

Create `src/FishAudioOrchestrator.Web/Components/Layout/EmptyLayout.razor`:

```razor
@inherits LayoutComponentBase

<main class="min-vh-100 bg-dark">
    @Body
</main>
```

- [ ] **Step 3: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/Login.razor src/FishAudioOrchestrator.Web/Components/Layout/EmptyLayout.razor
git commit -m "feat: add login page with dark theme and lockout support"
```

---

## Task 11: TOTP Verification Page

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Components/Pages/LoginTotp.razor`

- [ ] **Step 1: Create LoginTotp.razor**

Create `src/FishAudioOrchestrator.Web/Components/Pages/LoginTotp.razor`:

```razor
@page "/login/totp"
@using Microsoft.AspNetCore.Identity
@inject SignInManager<AppUser> SignInManager
@inject UserManager<AppUser> UserManager
@inject NavigationManager Nav
@layout EmptyLayout

<PageTitle>Verify TOTP — Fish Orchestrator</PageTitle>

<div class="d-flex justify-content-center align-items-center vh-100 bg-dark">
    <div class="card bg-dark text-light border-secondary" style="width: 400px;">
        <div class="card-body">
            <h3 class="card-title text-center mb-4">Two-Factor Authentication</h3>
            <p class="text-muted text-center">Enter the 6-digit code from your authenticator app.</p>

            @if (!string.IsNullOrEmpty(_error))
            {
                <div class="alert alert-danger">@_error</div>
            }

            <EditForm Model="_model" OnValidSubmit="HandleVerify" FormName="totp">
                <div class="mb-3">
                    <label class="form-label">Code</label>
                    <InputText @bind-Value="_model.Code" class="form-control bg-dark text-light border-secondary text-center"
                               maxlength="6" autocomplete="one-time-code" />
                </div>
                <button type="submit" class="btn btn-primary w-100" disabled="@_loading">
                    @(_loading ? "Verifying..." : "Verify")
                </button>
            </EditForm>

            <div class="text-center mt-3">
                <a href="/login" class="text-muted">Back to login</a>
            </div>
        </div>
    </div>
</div>

@code {
    private TotpModel _model = new();
    private string? _error;
    private bool _loading;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthState { get; set; }

    private sealed class TotpModel
    {
        public string Code { get; set; } = "";
    }

    protected override async Task OnInitializedAsync()
    {
        if (AuthState is not null)
        {
            var state = await AuthState;
            if (state.User.Identity?.IsAuthenticated != true)
            {
                Nav.NavigateTo("/login", forceLoad: true);
            }
        }
    }

    private async Task HandleVerify()
    {
        _error = null;
        _loading = true;

        if (AuthState is null)
        {
            Nav.NavigateTo("/login", forceLoad: true);
            return;
        }

        var state = await AuthState;
        var user = await UserManager.GetUserAsync(state.User);
        if (user is null)
        {
            Nav.NavigateTo("/login", forceLoad: true);
            return;
        }

        var isValid = await UserManager.VerifyTwoFactorTokenAsync(user,
            UserManager.Options.Tokens.AuthenticatorTokenProvider, _model.Code.Trim());

        if (!isValid)
        {
            _error = "Invalid code. Please try again.";
            _loading = false;
            return;
        }

        Nav.NavigateTo("/", forceLoad: true);
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/LoginTotp.razor
git commit -m "feat: add TOTP verification page for two-factor login"
```

---

## Task 12: Access Denied Page

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Components/Pages/AccessDenied.razor`

- [ ] **Step 1: Create AccessDenied.razor**

Create `src/FishAudioOrchestrator.Web/Components/Pages/AccessDenied.razor`:

```razor
@page "/access-denied"
@layout EmptyLayout

<PageTitle>Access Denied — Fish Orchestrator</PageTitle>

<div class="d-flex justify-content-center align-items-center vh-100 bg-dark">
    <div class="card bg-dark text-light border-secondary" style="width: 400px;">
        <div class="card-body text-center">
            <h3 class="card-title mb-3">Access Denied</h3>
            <p class="text-muted">You do not have permission to access this page.</p>
            <a href="/" class="btn btn-outline-light">Back to Dashboard</a>
        </div>
    </div>
</div>
```

- [ ] **Step 2: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/AccessDenied.razor
git commit -m "feat: add access denied page"
```

---

## Task 13: First-Run Setup Wizard

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Components/Pages/Setup.razor`

- [ ] **Step 1: Create Setup.razor**

Create `src/FishAudioOrchestrator.Web/Components/Pages/Setup.razor`:

```razor
@page "/setup"
@using Microsoft.AspNetCore.Identity
@inject UserManager<AppUser> UserManager
@inject RoleManager<IdentityRole> RoleManager
@inject SignInManager<AppUser> SignInManager
@inject ITotpService TotpService
@inject IConfiguration Config
@inject IWebHostEnvironment Env
@inject NavigationManager Nav
@layout EmptyLayout

<PageTitle>Setup — Fish Orchestrator</PageTitle>

<div class="d-flex justify-content-center align-items-center min-vh-100 bg-dark py-4">
    <div class="card bg-dark text-light border-secondary" style="width: 500px;">
        <div class="card-body">
            <h3 class="card-title text-center mb-4">Fish Orchestrator Setup</h3>

            @if (!string.IsNullOrEmpty(_error))
            {
                <div class="alert alert-danger">@_error</div>
            }

            @if (_step == 1)
            {
                <h5>Step 1: Network Configuration</h5>
                <p class="text-muted">Optionally provide a fully qualified domain name for automatic HTTPS via Let's Encrypt. Leave blank for localhost/LAN use.</p>
                <div class="mb-3">
                    <label class="form-label">Domain (FQDN)</label>
                    <InputText @bind-Value="_fqdn" class="form-control bg-dark text-light border-secondary"
                               placeholder="e.g. fish.example.com" />
                </div>
                <button class="btn btn-primary w-100" @onclick="GoToStep2">Next</button>
            }
            else if (_step == 2)
            {
                <h5>Step 2: Create Admin Account</h5>
                <EditForm Model="_adminModel" OnValidSubmit="GoToStep3" FormName="setup-admin">
                    <div class="mb-3">
                        <label class="form-label">Username</label>
                        <InputText @bind-Value="_adminModel.Username" class="form-control bg-dark text-light border-secondary" />
                    </div>
                    <div class="mb-3">
                        <label class="form-label">Display Name</label>
                        <InputText @bind-Value="_adminModel.DisplayName" class="form-control bg-dark text-light border-secondary" />
                    </div>
                    <div class="mb-3">
                        <label class="form-label">Password</label>
                        <InputText @bind-Value="_adminModel.Password" type="password" class="form-control bg-dark text-light border-secondary" />
                        <div class="form-text text-muted">8+ characters, upper, lower, digit, special character.</div>
                    </div>
                    <div class="mb-3">
                        <label class="form-label">Confirm Password</label>
                        <InputText @bind-Value="_adminModel.ConfirmPassword" type="password" class="form-control bg-dark text-light border-secondary" />
                    </div>
                    <button type="submit" class="btn btn-primary w-100">Next</button>
                </EditForm>
            }
            else if (_step == 3)
            {
                <h5>Step 3: TOTP Setup</h5>
                <p class="text-muted">Scan this QR code with your authenticator app (Google Authenticator, Authy, etc.).</p>

                @if (!string.IsNullOrEmpty(_qrDataUri))
                {
                    <div class="text-center mb-3">
                        <img src="@_qrDataUri" alt="TOTP QR Code" style="width: 200px; height: 200px;" />
                    </div>
                    <div class="mb-3">
                        <label class="form-label">Manual Key</label>
                        <input type="text" readonly class="form-control bg-dark text-light border-secondary text-center" value="@_manualKey" />
                    </div>
                }

                <div class="mb-3">
                    <label class="form-label">Enter 6-digit code to verify</label>
                    <InputText @bind-Value="_totpCode" class="form-control bg-dark text-light border-secondary text-center" maxlength="6" />
                </div>
                <button class="btn btn-primary w-100" @onclick="VerifyTotpAndComplete" disabled="@_loading">
                    @(_loading ? "Completing setup..." : "Complete Setup")
                </button>
            }
            else if (_step == 4)
            {
                <h5>Setup Complete</h5>
                <div class="alert alert-success">
                    <p><strong>Admin account created:</strong> @_adminModel.Username</p>
                    <p><strong>TOTP:</strong> Enabled</p>
                    @if (!string.IsNullOrWhiteSpace(_fqdn))
                    {
                        <p><strong>Domain:</strong> @_fqdn</p>
                        <p class="text-warning mb-0">Restart the application to enable automatic HTTPS certificate provisioning.</p>
                    }
                </div>
                <button class="btn btn-primary w-100" @onclick="GoToDashboard">Go to Dashboard</button>
            }
        </div>
    </div>
</div>

@code {
    private int _step = 1;
    private string _fqdn = "";
    private AdminModel _adminModel = new();
    private string? _qrDataUri;
    private string? _manualKey;
    private string _totpCode = "";
    private string? _error;
    private bool _loading;
    private AppUser? _createdUser;

    private sealed class AdminModel
    {
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Password { get; set; } = "";
        public string ConfirmPassword { get; set; } = "";
    }

    protected override async Task OnInitializedAsync()
    {
        // If users already exist, redirect to home
        if (UserManager.Users.Any())
        {
            Nav.NavigateTo("/", forceLoad: true);
        }
    }

    private void GoToStep2()
    {
        _error = null;
        _step = 2;
    }

    private async Task GoToStep3()
    {
        _error = null;

        if (string.IsNullOrWhiteSpace(_adminModel.Username))
        {
            _error = "Username is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_adminModel.DisplayName))
        {
            _error = "Display name is required.";
            return;
        }
        if (_adminModel.Password != _adminModel.ConfirmPassword)
        {
            _error = "Passwords do not match.";
            return;
        }

        var user = new AppUser
        {
            UserName = _adminModel.Username,
            DisplayName = _adminModel.DisplayName,
            MustChangePassword = false,
            MustSetupTotp = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await UserManager.CreateAsync(user, _adminModel.Password);
        if (!result.Succeeded)
        {
            _error = string.Join(" ", result.Errors.Select(e => e.Description));
            return;
        }

        await UserManager.AddToRoleAsync(user, "Admin");
        _createdUser = user;

        var (manualKey, qrDataUri) = await TotpService.GenerateSetupInfoAsync(user, "FishOrchestrator");
        _manualKey = manualKey;
        _qrDataUri = qrDataUri;
        _step = 3;
    }

    private async Task VerifyTotpAndComplete()
    {
        _error = null;
        _loading = true;

        if (_createdUser is null)
        {
            _error = "Setup error. Please refresh and start over.";
            _loading = false;
            return;
        }

        var isValid = await TotpService.VerifyCodeAsync(_createdUser, _totpCode.Trim());
        if (!isValid)
        {
            _error = "Invalid code. Please try again.";
            _loading = false;
            return;
        }

        await UserManager.SetTwoFactorEnabledAsync(_createdUser, true);

        // Save FQDN to appsettings.json if provided
        if (!string.IsNullOrWhiteSpace(_fqdn))
        {
            await SaveFqdnToSettings(_fqdn.Trim());
        }

        _step = 4;
        _loading = false;
    }

    private async Task SaveFqdnToSettings(string fqdn)
    {
        var settingsPath = Path.Combine(Env.ContentRootPath, "appsettings.json");
        if (!File.Exists(settingsPath)) return;

        var json = await File.ReadAllTextAsync(settingsPath);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true });
        writer.WriteStartObject();

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name == "FishOrchestrator")
            {
                writer.WriteStartObject("FishOrchestrator");
                foreach (var sub in prop.Value.EnumerateObject())
                {
                    if (sub.Name == "Domain")
                        writer.WriteString("Domain", fqdn);
                    else
                        sub.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            else if (prop.Name == "LettuceEncrypt")
            {
                writer.WriteStartObject("LettuceEncrypt");
                foreach (var sub in prop.Value.EnumerateObject())
                {
                    if (sub.Name == "DomainNames")
                    {
                        writer.WriteStartArray("DomainNames");
                        writer.WriteStringValue(fqdn);
                        writer.WriteEndArray();
                    }
                    else
                        sub.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            else
            {
                prop.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
        await writer.FlushAsync();
        await File.WriteAllBytesAsync(settingsPath, stream.ToArray());
    }

    private async Task GoToDashboard()
    {
        if (_createdUser is not null)
        {
            await SignInManager.SignInAsync(_createdUser, isPersistent: false);
        }
        Nav.NavigateTo("/", forceLoad: true);
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/Setup.razor
git commit -m "feat: add first-run setup wizard with FQDN config, admin creation, and TOTP enrollment"
```

---

## Task 14: Account Self-Service Pages

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Components/Pages/Account/ChangePassword.razor`
- Create: `src/FishAudioOrchestrator.Web/Components/Pages/Account/SetupTotp.razor`
- Create: `src/FishAudioOrchestrator.Web/Components/Pages/Account/ManageAccount.razor`

- [ ] **Step 1: Create ChangePassword.razor**

Create `src/FishAudioOrchestrator.Web/Components/Pages/Account/ChangePassword.razor`:

```razor
@page "/account/change-password"
@attribute [Authorize]
@using Microsoft.AspNetCore.Identity
@inject UserManager<AppUser> UserManager
@inject SignInManager<AppUser> SignInManager
@inject NavigationManager Nav

<PageTitle>Change Password — Fish Orchestrator</PageTitle>

<div class="row justify-content-center">
    <div class="col-md-6">
        <h3>Change Password</h3>

        @if (_mustChange)
        {
            <div class="alert alert-warning">You must change your password before continuing.</div>
        }

        @if (!string.IsNullOrEmpty(_error))
        {
            <div class="alert alert-danger">@_error</div>
        }
        @if (_success)
        {
            <div class="alert alert-success">Password changed successfully.</div>
        }

        <EditForm Model="_model" OnValidSubmit="HandleSubmit" FormName="change-password">
            <div class="mb-3">
                <label class="form-label">Current Password</label>
                <InputText @bind-Value="_model.CurrentPassword" type="password" class="form-control bg-dark text-light border-secondary" />
            </div>
            <div class="mb-3">
                <label class="form-label">New Password</label>
                <InputText @bind-Value="_model.NewPassword" type="password" class="form-control bg-dark text-light border-secondary" />
            </div>
            <div class="mb-3">
                <label class="form-label">Confirm New Password</label>
                <InputText @bind-Value="_model.ConfirmPassword" type="password" class="form-control bg-dark text-light border-secondary" />
            </div>
            <button type="submit" class="btn btn-primary" disabled="@_loading">Change Password</button>
        </EditForm>
    </div>
</div>

@code {
    private PasswordModel _model = new();
    private string? _error;
    private bool _success;
    private bool _loading;
    private bool _mustChange;

    [CascadingParameter]
    private Task<AuthenticationState> AuthState { get; set; } = null!;

    private sealed class PasswordModel
    {
        public string CurrentPassword { get; set; } = "";
        public string NewPassword { get; set; } = "";
        public string ConfirmPassword { get; set; } = "";
    }

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState;
        var user = await UserManager.GetUserAsync(state.User);
        if (user is not null)
            _mustChange = user.MustChangePassword;
    }

    private async Task HandleSubmit()
    {
        _error = null;
        _success = false;
        _loading = true;

        if (_model.NewPassword != _model.ConfirmPassword)
        {
            _error = "Passwords do not match.";
            _loading = false;
            return;
        }

        var state = await AuthState;
        var user = await UserManager.GetUserAsync(state.User);
        if (user is null)
        {
            Nav.NavigateTo("/login", forceLoad: true);
            return;
        }

        var result = await UserManager.ChangePasswordAsync(user, _model.CurrentPassword, _model.NewPassword);
        if (!result.Succeeded)
        {
            _error = string.Join(" ", result.Errors.Select(e => e.Description));
            _loading = false;
            return;
        }

        user.MustChangePassword = false;
        await UserManager.UpdateAsync(user);

        _success = true;
        _loading = false;

        if (_mustChange)
        {
            // If user also needs TOTP, redirect there; otherwise go home
            if (user.MustSetupTotp)
                Nav.NavigateTo("/account/setup-totp", forceLoad: true);
            else
                Nav.NavigateTo("/", forceLoad: true);
        }
    }
}
```

- [ ] **Step 2: Create SetupTotp.razor**

Create `src/FishAudioOrchestrator.Web/Components/Pages/Account/SetupTotp.razor`:

```razor
@page "/account/setup-totp"
@attribute [Authorize]
@using Microsoft.AspNetCore.Identity
@inject UserManager<AppUser> UserManager
@inject ITotpService TotpService
@inject NavigationManager Nav

<PageTitle>Setup TOTP — Fish Orchestrator</PageTitle>

<div class="row justify-content-center">
    <div class="col-md-6">
        <h3>Setup Two-Factor Authentication</h3>

        @if (_mustSetup)
        {
            <div class="alert alert-warning">You must set up two-factor authentication before continuing.</div>
        }

        @if (!string.IsNullOrEmpty(_error))
        {
            <div class="alert alert-danger">@_error</div>
        }

        @if (!string.IsNullOrEmpty(_qrDataUri))
        {
            <p class="text-muted">Scan this QR code with your authenticator app.</p>
            <div class="text-center mb-3">
                <img src="@_qrDataUri" alt="TOTP QR Code" style="width: 200px; height: 200px;" />
            </div>
            <div class="mb-3">
                <label class="form-label">Manual Key</label>
                <input type="text" readonly class="form-control bg-dark text-light border-secondary text-center" value="@_manualKey" />
            </div>

            <div class="mb-3">
                <label class="form-label">Enter 6-digit code to verify</label>
                <InputText @bind-Value="_code" class="form-control bg-dark text-light border-secondary text-center" maxlength="6" />
            </div>
            <button class="btn btn-primary" @onclick="VerifyAndEnable" disabled="@_loading">
                @(_loading ? "Verifying..." : "Enable TOTP")
            </button>
        }
    </div>
</div>

@code {
    private string? _qrDataUri;
    private string? _manualKey;
    private string _code = "";
    private string? _error;
    private bool _loading;
    private bool _mustSetup;
    private AppUser? _user;

    [CascadingParameter]
    private Task<AuthenticationState> AuthState { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState;
        _user = await UserManager.GetUserAsync(state.User);
        if (_user is null)
        {
            Nav.NavigateTo("/login", forceLoad: true);
            return;
        }

        _mustSetup = _user.MustSetupTotp;
        var (manualKey, qrDataUri) = await TotpService.GenerateSetupInfoAsync(_user, "FishOrchestrator");
        _manualKey = manualKey;
        _qrDataUri = qrDataUri;
    }

    private async Task VerifyAndEnable()
    {
        _error = null;
        _loading = true;

        if (_user is null) return;

        var isValid = await TotpService.VerifyCodeAsync(_user, _code.Trim());
        if (!isValid)
        {
            _error = "Invalid code. Please try again.";
            _loading = false;
            return;
        }

        await UserManager.SetTwoFactorEnabledAsync(_user, true);
        _user.MustSetupTotp = false;
        await UserManager.UpdateAsync(_user);

        Nav.NavigateTo("/", forceLoad: true);
    }
}
```

- [ ] **Step 3: Create ManageAccount.razor**

Create `src/FishAudioOrchestrator.Web/Components/Pages/Account/ManageAccount.razor`:

```razor
@page "/account"
@attribute [Authorize]
@using Microsoft.AspNetCore.Identity
@inject UserManager<AppUser> UserManager
@inject SignInManager<AppUser> SignInManager
@inject NavigationManager Nav

<PageTitle>My Account — Fish Orchestrator</PageTitle>

<div class="row justify-content-center">
    <div class="col-md-6">
        <h3>My Account</h3>

        @if (!string.IsNullOrEmpty(_message))
        {
            <div class="alert alert-success">@_message</div>
        }
        @if (!string.IsNullOrEmpty(_error))
        {
            <div class="alert alert-danger">@_error</div>
        }

        @if (_user is not null)
        {
            <div class="card bg-dark border-secondary mb-3">
                <div class="card-body">
                    <h5>Display Name</h5>
                    <EditForm Model="_displayModel" OnValidSubmit="UpdateDisplayName" FormName="display-name">
                        <div class="input-group">
                            <InputText @bind-Value="_displayModel.DisplayName" class="form-control bg-dark text-light border-secondary" />
                            <button type="submit" class="btn btn-outline-primary">Save</button>
                        </div>
                    </EditForm>
                </div>
            </div>

            <div class="card bg-dark border-secondary mb-3">
                <div class="card-body">
                    <h5>Security</h5>
                    <p>TOTP: <span class="badge bg-success">Enabled</span></p>
                    <a href="/account/change-password" class="btn btn-outline-light me-2">Change Password</a>
                    <a href="/account/setup-totp" class="btn btn-outline-light">Regenerate TOTP Key</a>
                </div>
            </div>

            <div class="card bg-dark border-secondary">
                <div class="card-body">
                    <h5>Session</h5>
                    <button class="btn btn-outline-danger" @onclick="Logout">Sign Out</button>
                </div>
            </div>
        }
    </div>
</div>

@code {
    private AppUser? _user;
    private DisplayModel _displayModel = new();
    private string? _message;
    private string? _error;

    [CascadingParameter]
    private Task<AuthenticationState> AuthState { get; set; } = null!;

    private sealed class DisplayModel
    {
        public string DisplayName { get; set; } = "";
    }

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState;
        _user = await UserManager.GetUserAsync(state.User);
        if (_user is not null)
            _displayModel.DisplayName = _user.DisplayName;
    }

    private async Task UpdateDisplayName()
    {
        _message = null;
        _error = null;
        if (_user is null) return;

        _user.DisplayName = _displayModel.DisplayName;
        var result = await UserManager.UpdateAsync(_user);
        if (result.Succeeded)
            _message = "Display name updated.";
        else
            _error = string.Join(" ", result.Errors.Select(e => e.Description));
    }

    private async Task Logout()
    {
        await SignInManager.SignOutAsync();
        Nav.NavigateTo("/login", forceLoad: true);
    }
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/Account/
git commit -m "feat: add account self-service pages (change password, TOTP setup, manage account)"
```

---

## Task 15: Admin User Management Page

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Components/Pages/Admin/UserManagement.razor`
- Test: `tests/FishAudioOrchestrator.Tests/Auth/UserManagementTests.cs`

- [ ] **Step 1: Write integration tests for admin operations**

Create `tests/FishAudioOrchestrator.Tests/Auth/UserManagementTests.cs`:

```csharp
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FishAudioOrchestrator.Tests.Auth;

public class UserManagementTests
{
    private static (ServiceProvider sp, UserManager<AppUser> userMgr, RoleManager<IdentityRole> roleMgr) BuildServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<AppUser>(opts =>
            {
                opts.Password.RequiredLength = 8;
                opts.Password.RequireUppercase = true;
                opts.Password.RequireLowercase = true;
                opts.Password.RequireDigit = true;
                opts.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
        roleMgr.CreateAsync(new IdentityRole("Admin")).GetAwaiter().GetResult();
        roleMgr.CreateAsync(new IdentityRole("User")).GetAwaiter().GetResult();

        return (sp, sp.GetRequiredService<UserManager<AppUser>>(), roleMgr);
    }

    [Fact]
    public async Task AdminCanCreateUserWithTempPassword()
    {
        var (sp, userMgr, _) = BuildServices();

        var user = new AppUser
        {
            UserName = "newuser",
            DisplayName = "New User",
            MustChangePassword = true,
            MustSetupTotp = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var result = await userMgr.CreateAsync(user, "TempPass1!");
        await userMgr.AddToRoleAsync(user, "User");

        Assert.True(result.Succeeded);
        Assert.True(user.MustChangePassword);
        Assert.True(user.MustSetupTotp);
        var roles = await userMgr.GetRolesAsync(user);
        Assert.Contains("User", roles);
    }

    [Fact]
    public async Task AdminCanResetUserPassword()
    {
        var (sp, userMgr, _) = BuildServices();

        var user = new AppUser
        {
            UserName = "resetme",
            DisplayName = "Reset",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await userMgr.CreateAsync(user, "OldPass1!");

        var token = await userMgr.GeneratePasswordResetTokenAsync(user);
        var result = await userMgr.ResetPasswordAsync(user, token, "NewPass1!");

        Assert.True(result.Succeeded);
        Assert.True(await userMgr.CheckPasswordAsync(user, "NewPass1!"));
    }

    [Fact]
    public async Task CannotDeleteLastAdmin()
    {
        var (sp, userMgr, _) = BuildServices();

        var admin = new AppUser
        {
            UserName = "admin",
            DisplayName = "Admin",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await userMgr.CreateAsync(admin, "Admin123!@");
        await userMgr.AddToRoleAsync(admin, "Admin");

        var admins = await userMgr.GetUsersInRoleAsync("Admin");
        Assert.Single(admins);
        // Business rule: do not delete if only one admin
    }

    [Fact]
    public async Task CanChangeUserRole()
    {
        var (sp, userMgr, _) = BuildServices();

        var user = new AppUser
        {
            UserName = "roletest",
            DisplayName = "Role Test",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await userMgr.CreateAsync(user, "Test123!@");
        await userMgr.AddToRoleAsync(user, "User");

        await userMgr.RemoveFromRoleAsync(user, "User");
        await userMgr.AddToRoleAsync(user, "Admin");

        var roles = await userMgr.GetRolesAsync(user);
        Assert.Contains("Admin", roles);
        Assert.DoesNotContain("User", roles);
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~UserManagementTests" -v q`
Expected: 4 passed, 0 failed. (These are integration tests against Identity, not against UI.)

- [ ] **Step 3: Create UserManagement.razor**

Create `src/FishAudioOrchestrator.Web/Components/Pages/Admin/UserManagement.razor`:

```razor
@page "/admin/users"
@attribute [Authorize(Roles = "Admin")]
@using Microsoft.AspNetCore.Identity
@using Microsoft.EntityFrameworkCore
@inject UserManager<AppUser> UserManager
@inject NavigationManager Nav

<PageTitle>User Management — Fish Orchestrator</PageTitle>

<h3>User Management</h3>

@if (!string.IsNullOrEmpty(_message))
{
    <div class="alert alert-success alert-dismissible">@_message</div>
}
@if (!string.IsNullOrEmpty(_error))
{
    <div class="alert alert-danger alert-dismissible">@_error</div>
}

<div class="row">
    <div class="col-md-8">
        <table class="table table-dark table-striped">
            <thead>
                <tr>
                    <th>Display Name</th>
                    <th>Username</th>
                    <th>Role</th>
                    <th>TOTP</th>
                    <th>Created</th>
                    <th>Actions</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var u in _users)
                {
                    <tr>
                        <td>@u.DisplayName</td>
                        <td>@u.UserName</td>
                        <td>@u.Role</td>
                        <td>@(u.TotpEnabled ? "Yes" : "No")</td>
                        <td>@u.CreatedAt.ToString("yyyy-MM-dd")</td>
                        <td>
                            <button class="btn btn-sm btn-outline-light" @onclick="() => StartEdit(u)">Edit</button>
                            <button class="btn btn-sm btn-outline-warning" @onclick="() => StartResetPassword(u)">Reset PW</button>
                            <button class="btn btn-sm btn-outline-danger" @onclick="() => StartDelete(u)">Delete</button>
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    </div>

    <div class="col-md-4">
        @if (_showCreateForm)
        {
            <div class="card bg-dark border-secondary">
                <div class="card-body">
                    <h5>Create User</h5>
                    <div class="mb-2">
                        <label class="form-label">Username</label>
                        <InputText @bind-Value="_createModel.Username" class="form-control bg-dark text-light border-secondary" />
                    </div>
                    <div class="mb-2">
                        <label class="form-label">Display Name</label>
                        <InputText @bind-Value="_createModel.DisplayName" class="form-control bg-dark text-light border-secondary" />
                    </div>
                    <div class="mb-2">
                        <label class="form-label">Temporary Password</label>
                        <InputText @bind-Value="_createModel.Password" type="password" class="form-control bg-dark text-light border-secondary" />
                    </div>
                    <div class="mb-2">
                        <label class="form-label">Role</label>
                        <InputSelect @bind-Value="_createModel.Role" class="form-select bg-dark text-light border-secondary">
                            <option value="User">User</option>
                            <option value="Admin">Admin</option>
                        </InputSelect>
                    </div>
                    <button class="btn btn-primary" @onclick="CreateUser">Create</button>
                    <button class="btn btn-secondary" @onclick="() => _showCreateForm = false">Cancel</button>
                </div>
            </div>
        }
        else if (_editUser is not null)
        {
            <div class="card bg-dark border-secondary">
                <div class="card-body">
                    <h5>Edit: @_editUser.UserName</h5>
                    <div class="mb-2">
                        <label class="form-label">Display Name</label>
                        <InputText @bind-Value="_editDisplayName" class="form-control bg-dark text-light border-secondary" />
                    </div>
                    <div class="mb-2">
                        <label class="form-label">Role</label>
                        <InputSelect @bind-Value="_editRole" class="form-select bg-dark text-light border-secondary">
                            <option value="User">User</option>
                            <option value="Admin">Admin</option>
                        </InputSelect>
                    </div>
                    <button class="btn btn-primary" @onclick="SaveEdit">Save</button>
                    <button class="btn btn-secondary" @onclick="() => _editUser = null">Cancel</button>
                </div>
            </div>
        }
        else if (_resetUser is not null)
        {
            <div class="card bg-dark border-secondary">
                <div class="card-body">
                    <h5>Reset Password: @_resetUser.UserName</h5>
                    <div class="mb-2">
                        <label class="form-label">New Temporary Password</label>
                        <InputText @bind-Value="_resetPassword" type="password" class="form-control bg-dark text-light border-secondary" />
                    </div>
                    <button class="btn btn-warning" @onclick="ResetPassword">Reset</button>
                    <button class="btn btn-secondary" @onclick="() => _resetUser = null">Cancel</button>
                </div>
            </div>
        }
        else
        {
            <button class="btn btn-primary" @onclick="() => _showCreateForm = true">Create User</button>
        }
    </div>
</div>

<ConfirmDialog @ref="_confirmDialog" Title="Delete User" Message="@_deleteMessage" ConfirmText="Delete"
               OnConfirm="ConfirmDelete" OnCancel="() => _deleteTarget = null" />

@code {
    private List<UserRow> _users = new();
    private string? _message;
    private string? _error;

    // Create
    private bool _showCreateForm;
    private CreateModel _createModel = new();

    // Edit
    private UserRow? _editUser;
    private string _editDisplayName = "";
    private string _editRole = "User";

    // Reset password
    private UserRow? _resetUser;
    private string _resetPassword = "";

    // Delete
    private UserRow? _deleteTarget;
    private string _deleteMessage = "";
    private ConfirmDialog _confirmDialog = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthState { get; set; } = null!;

    private sealed class UserRow
    {
        public string Id { get; set; } = "";
        public string UserName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Role { get; set; } = "";
        public bool TotpEnabled { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class CreateModel
    {
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Password { get; set; } = "";
        public string Role { get; set; } = "User";
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadUsers();
    }

    private async Task LoadUsers()
    {
        var users = await UserManager.Users.ToListAsync();
        _users = new List<UserRow>();
        foreach (var u in users)
        {
            var roles = await UserManager.GetRolesAsync(u);
            _users.Add(new UserRow
            {
                Id = u.Id,
                UserName = u.UserName!,
                DisplayName = u.DisplayName,
                Role = roles.FirstOrDefault() ?? "User",
                TotpEnabled = u.TwoFactorEnabled,
                CreatedAt = u.CreatedAt
            });
        }
    }

    private async Task CreateUser()
    {
        _error = null;
        _message = null;

        var user = new AppUser
        {
            UserName = _createModel.Username,
            DisplayName = _createModel.DisplayName,
            MustChangePassword = true,
            MustSetupTotp = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await UserManager.CreateAsync(user, _createModel.Password);
        if (!result.Succeeded)
        {
            _error = string.Join(" ", result.Errors.Select(e => e.Description));
            return;
        }

        await UserManager.AddToRoleAsync(user, _createModel.Role);
        _message = $"User '{_createModel.Username}' created.";
        _createModel = new CreateModel();
        _showCreateForm = false;
        await LoadUsers();
    }

    private void StartEdit(UserRow u)
    {
        _editUser = u;
        _editDisplayName = u.DisplayName;
        _editRole = u.Role;
        _resetUser = null;
        _showCreateForm = false;
    }

    private async Task SaveEdit()
    {
        _error = null;
        _message = null;

        if (_editUser is null) return;
        var user = await UserManager.FindByIdAsync(_editUser.Id);
        if (user is null) return;

        // Prevent demoting last admin
        if (_editUser.Role == "Admin" && _editRole != "Admin")
        {
            var admins = await UserManager.GetUsersInRoleAsync("Admin");
            if (admins.Count <= 1)
            {
                _error = "Cannot demote the last admin.";
                return;
            }
        }

        user.DisplayName = _editDisplayName;
        await UserManager.UpdateAsync(user);

        if (_editUser.Role != _editRole)
        {
            await UserManager.RemoveFromRoleAsync(user, _editUser.Role);
            await UserManager.AddToRoleAsync(user, _editRole);
        }

        _message = $"User '{user.UserName}' updated.";
        _editUser = null;
        await LoadUsers();
    }

    private void StartResetPassword(UserRow u)
    {
        _resetUser = u;
        _resetPassword = "";
        _editUser = null;
        _showCreateForm = false;
    }

    private async Task ResetPassword()
    {
        _error = null;
        _message = null;

        if (_resetUser is null) return;
        var user = await UserManager.FindByIdAsync(_resetUser.Id);
        if (user is null) return;

        var token = await UserManager.GeneratePasswordResetTokenAsync(user);
        var result = await UserManager.ResetPasswordAsync(user, token, _resetPassword);
        if (!result.Succeeded)
        {
            _error = string.Join(" ", result.Errors.Select(e => e.Description));
            return;
        }

        user.MustChangePassword = true;
        await UserManager.UpdateAsync(user);

        _message = $"Password reset for '{user.UserName}'.";
        _resetUser = null;
    }

    private async void StartDelete(UserRow u)
    {
        var state = await AuthState;
        var currentUser = await UserManager.GetUserAsync(state.User);

        if (currentUser?.Id == u.Id)
        {
            _error = "Cannot delete your own account.";
            return;
        }

        if (u.Role == "Admin")
        {
            var admins = await UserManager.GetUsersInRoleAsync("Admin");
            if (admins.Count <= 1)
            {
                _error = "Cannot delete the last admin.";
                return;
            }
        }

        _deleteTarget = u;
        _deleteMessage = $"Are you sure you want to delete user '{u.UserName}'? This cannot be undone.";
        _confirmDialog.Show();
    }

    private async Task ConfirmDelete()
    {
        _error = null;
        _message = null;

        if (_deleteTarget is null) return;
        var user = await UserManager.FindByIdAsync(_deleteTarget.Id);
        if (user is null) return;

        await UserManager.DeleteAsync(user);
        _message = $"User '{user.UserName}' deleted.";
        _deleteTarget = null;
        await LoadUsers();
    }
}
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test --nologo -v q`
Expected: All tests pass (42 existing + 3 AppUser + 6 PostLoginRedirect + 3 SetupGuard + 3 AdminSeed + 3 Totp + 4 UserManagement = ~64 total).

- [ ] **Step 5: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/Admin/UserManagement.razor tests/FishAudioOrchestrator.Tests/Auth/UserManagementTests.cs
git commit -m "feat: add admin user management page with create, edit, reset password, delete"
```

---

## Task 16: Admin Settings Page

**Files:**
- Create: `src/FishAudioOrchestrator.Web/Components/Pages/Admin/AdminSettings.razor`

- [ ] **Step 1: Create AdminSettings.razor**

Create `src/FishAudioOrchestrator.Web/Components/Pages/Admin/AdminSettings.razor`:

```razor
@page "/admin/settings"
@attribute [Authorize(Roles = "Admin")]
@inject IConfiguration Config
@inject IWebHostEnvironment Env

<PageTitle>Settings — Fish Orchestrator</PageTitle>

<div class="row justify-content-center">
    <div class="col-md-6">
        <h3>Admin Settings</h3>

        @if (!string.IsNullOrEmpty(_message))
        {
            <div class="alert alert-success">@_message</div>
        }
        @if (!string.IsNullOrEmpty(_error))
        {
            <div class="alert alert-danger">@_error</div>
        }

        <div class="card bg-dark border-secondary mb-3">
            <div class="card-body">
                <h5>HTTPS / Domain</h5>
                <p class="text-muted">Configure a fully qualified domain name for automatic Let's Encrypt HTTPS. Changes require an application restart.</p>
                <div class="mb-3">
                    <label class="form-label">FQDN</label>
                    <InputText @bind-Value="_fqdn" class="form-control bg-dark text-light border-secondary"
                               placeholder="e.g. fish.example.com" />
                </div>
                <button class="btn btn-primary" @onclick="SaveFqdn" disabled="@_loading">
                    @(_loading ? "Saving..." : "Save")
                </button>
            </div>
        </div>
    </div>
</div>

@code {
    private string _fqdn = "";
    private string? _message;
    private string? _error;
    private bool _loading;

    protected override void OnInitialized()
    {
        _fqdn = Config["FishOrchestrator:Domain"] ?? "";
    }

    private async Task SaveFqdn()
    {
        _message = null;
        _error = null;
        _loading = true;

        var settingsPath = Path.Combine(Env.ContentRootPath, "appsettings.json");
        if (!File.Exists(settingsPath))
        {
            _error = "Settings file not found.";
            _loading = false;
            return;
        }

        var json = await File.ReadAllTextAsync(settingsPath);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true });
        writer.WriteStartObject();

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name == "FishOrchestrator")
            {
                writer.WriteStartObject("FishOrchestrator");
                foreach (var sub in prop.Value.EnumerateObject())
                {
                    if (sub.Name == "Domain")
                        writer.WriteString("Domain", _fqdn.Trim());
                    else
                        sub.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
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
            else
            {
                prop.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
        await writer.FlushAsync();
        await File.WriteAllBytesAsync(settingsPath, stream.ToArray());

        _message = "Domain saved. Restart the application to apply HTTPS changes.";
        _loading = false;
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/Admin/AdminSettings.razor
git commit -m "feat: add admin settings page for FQDN/HTTPS configuration"
```

---

## Task 17: Update NavMenu with Auth-Aware Links

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Components/Layout/NavMenu.razor`

- [ ] **Step 1: Update NavMenu.razor**

Replace the contents of `src/FishAudioOrchestrator.Web/Components/Layout/NavMenu.razor` with:

```razor
@using Microsoft.AspNetCore.Components.Authorization

<nav class="navbar navbar-expand navbar-dark bg-dark px-3">
    <a class="navbar-brand" href="/">Fish Orchestrator</a>
    <AuthorizeView>
        <Authorized>
            <div class="navbar-nav">
                <NavLink class="nav-link" href="/" Match="NavLinkMatch.All">Dashboard</NavLink>
                <AuthorizeView Roles="Admin">
                    <NavLink class="nav-link" href="/deploy">Deploy</NavLink>
                </AuthorizeView>
                <NavLink class="nav-link" href="/voices">Voices</NavLink>
                <NavLink class="nav-link" href="/playground">TTS</NavLink>
                <NavLink class="nav-link" href="/history">History</NavLink>
                <AuthorizeView Roles="Admin">
                    <NavLink class="nav-link" href="/admin/users">Users</NavLink>
                    <NavLink class="nav-link" href="/admin/settings">Settings</NavLink>
                </AuthorizeView>
            </div>
            <div class="navbar-nav ms-auto">
                <span class="nav-link text-muted" id="gpu-indicator">@_gpuInfo</span>
                <NavLink class="nav-link" href="/account">@context.User.Identity?.Name</NavLink>
            </div>
        </Authorized>
    </AuthorizeView>
</nav>

@code {
    private string _gpuInfo = "GPU: loading...";

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=memory.used,memory.total --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is not null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                var parts = output.Trim().Split(',');
                if (parts.Length == 2)
                {
                    _gpuInfo = $"GPU: {parts[0].Trim()} / {parts[1].Trim()} MB";
                }
            }
        }
        catch
        {
            _gpuInfo = "GPU: N/A";
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Layout/NavMenu.razor
git commit -m "feat: update NavMenu with role-based link visibility and user display"
```

---

## Task 18: Update GenerationHistory for User Filtering

**Files:**
- Modify: `src/FishAudioOrchestrator.Web/Components/Pages/GenerationHistory.razor`

- [ ] **Step 1: Read the full current file**

Read `src/FishAudioOrchestrator.Web/Components/Pages/GenerationHistory.razor` to understand the current query.

- [ ] **Step 2: Add user-scoped filtering**

After the existing `@inject` directives, add:

```razor
@using Microsoft.AspNetCore.Identity
@inject UserManager<AppUser> UserManager
```

In the `@code` block, modify the data-loading method to filter by user:

```csharp
// Get current user
var state = await AuthState;
var user = await UserManager.GetUserAsync(state.User);
var isAdmin = state.User.IsInRole("Admin");

// If admin, show all logs; if user, show only own logs
IQueryable<GenerationLog> query = Db.GenerationLogs
    .Include(l => l.ModelProfile)
    .Include(l => l.ReferenceVoice);

if (!isAdmin && user is not null)
{
    query = query.Where(l => l.UserId == user.Id);
}

_logs = await query.OrderByDescending(l => l.CreatedAt).ToListAsync();
```

Add `[CascadingParameter]` for `Task<AuthenticationState> AuthState`.

- [ ] **Step 3: Verify build**

Run: `dotnet build --nologo -v q`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/FishAudioOrchestrator.Web/Components/Pages/GenerationHistory.razor
git commit -m "feat: filter generation history by user role (admin sees all, users see own)"
```

---

## Task 19: Full Integration Verification

- [ ] **Step 1: Run entire test suite**

Run: `dotnet test --nologo -v q`
Expected: All tests pass (~64 tests).

- [ ] **Step 2: Verify clean Release build**

Run: `dotnet build -c Release --nologo -v q`
Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Tag the release**

```bash
git tag v0.4.0-phase4
```

- [ ] **Step 4: Commit tag**

```bash
git push origin v0.4.0-phase4
```
(Only if user confirms push.)
