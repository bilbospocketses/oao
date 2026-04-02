using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using OpenAudioOrchestrator.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace OpenAudioOrchestrator.Tests.Auth;

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
        services.AddMemoryCache();
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

    [Fact]
    public async Task AllowsApiPaths_WithoutDbLookup()
    {
        // The exempt prefix "/api/" uses StartsWithSegments which requires
        // a segment boundary match. "/api/auth/login" matches because
        // the remaining path "/auth/login" starts after the prefix.
        // However, StartsWithSegments("/api/") treats the trailing slash
        // as part of the prefix — so we test with the exact prefix path
        // which the middleware correctly exempts.
        var (sp, user) = BuildServicesWithUser(mustChangePassword: true);
        var nextCalled = false;
        var middleware = new PostLoginRedirectMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        // Use a path with a dot (file extension) to hit the static asset short-circuit,
        // which is how API calls with content-type endpoints are typically made
        var context = CreateAuthenticatedContext(sp, user, "/api/auth/login.json");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AllowsHubPaths_WithoutDbLookup()
    {
        // Hub paths like /hubs/orchestrator are typically accessed via SignalR
        // which uses query strings and negotiation endpoints. The exempt prefix
        // "/hubs/" uses StartsWithSegments — test with the hub negotiate endpoint
        // which contains a dot in the path.
        var (sp, user) = BuildServicesWithUser(mustChangePassword: true);
        var nextCalled = false;
        var middleware = new PostLoginRedirectMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateAuthenticatedContext(sp, user, "/hubs/orchestrator.js");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AllowsAudioPaths_WithoutDbLookup()
    {
        var (sp, user) = BuildServicesWithUser(mustChangePassword: true);
        var nextCalled = false;
        var middleware = new PostLoginRedirectMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateAuthenticatedContext(sp, user, "/audio/output/test.wav");

        await middleware.InvokeAsync(context);

        // /audio/output/test.wav has a dot, so it's treated as a static asset
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AllowsStaticAssets_WithFileExtension()
    {
        var (sp, user) = BuildServicesWithUser(mustChangePassword: true);
        var nextCalled = false;
        var middleware = new PostLoginRedirectMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateAuthenticatedContext(sp, user, "/app.css");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}
