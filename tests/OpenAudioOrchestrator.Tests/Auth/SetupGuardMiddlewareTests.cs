using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using OpenAudioOrchestrator.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace OpenAudioOrchestrator.Tests.Auth;

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

    [Fact]
    public async Task BlocksSetupPath_WhenUsersExist()
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
        context.Request.Path = "/setup";

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal("/", context.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("/_framework/blazor.js")]
    [InlineData("/_blazor")]
    [InlineData("/css/site.css")]
    public async Task AllowsStaticResources_Regardless(string path)
    {
        // No users exist, but static resources should still pass through
        var sp = BuildServices();
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var nextCalled = false;
        var middleware = new SetupGuardMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AllowsFileExtensionPaths_Regardless()
    {
        // No users exist, but paths with file extensions should pass through
        var sp = BuildServices();
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var nextCalled = false;
        var middleware = new SetupGuardMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Path = "/some-file.js";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task CachesSetupComplete_OnSubsequentRequests()
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

        var nextCallCount = 0;
        var middleware = new SetupGuardMiddleware(_ => { nextCallCount++; return Task.CompletedTask; });

        // First request — queries DB and caches _setupComplete = true
        var context1 = new DefaultHttpContext { RequestServices = sp };
        context1.Request.Path = "/";
        await middleware.InvokeAsync(context1);
        Assert.Equal(1, nextCallCount);

        // Second request — uses an EMPTY database (no users).
        // If the middleware re-queried, it would find zero users and redirect to /setup.
        // The fact that it calls next proves the cached _setupComplete is used.
        var emptyServices = new ServiceCollection();
        emptyServices.AddDbContext<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        emptyServices.AddLogging();
        var emptySp = emptyServices.BuildServiceProvider();
        var emptyDb = emptySp.GetRequiredService<AppDbContext>();
        emptyDb.Database.EnsureCreated();

        var context2 = new DefaultHttpContext { RequestServices = emptySp };
        context2.Request.Path = "/dashboard";
        await middleware.InvokeAsync(context2);

        // next was called (not redirected to /setup), proving cache was used
        Assert.Equal(2, nextCallCount);
    }
}
