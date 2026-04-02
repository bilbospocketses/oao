using System.Net;
using OpenAudioOrchestrator.Web.Data.Entities;
using OpenAudioOrchestrator.Web.Middleware;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace OpenAudioOrchestrator.Tests.Integration;

public class MiddlewarePipelineTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public MiddlewarePipelineTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.SeedTestDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SetupPage_WhenUsersExist_RedirectsToRoot()
    {
        var client = _factory.CreateNonRedirectClient();

        var response = await client.GetAsync("/setup");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ProtectedPage_Unauthenticated_RedirectsToLogin()
    {
        var client = _factory.CreateNonRedirectClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ProtectedPage_Authenticated_Returns200()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MustChangePassword_RedirectsToChangePassword()
    {
        // Flag the user as needing password change and evict middleware cache
        using (var scope = _factory.Services.CreateScope())
        {
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userMgr.FindByNameAsync(CustomWebApplicationFactory.UserUsername);
            user!.MustChangePassword = true;
            await userMgr.UpdateAsync(user);
            var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
            PostLoginRedirectMiddleware.EvictUserCache(cache, user.Id);
        }

        try
        {
            var client = await _factory.CreateAuthenticatedClientAsync(
                CustomWebApplicationFactory.UserUsername, CustomWebApplicationFactory.UserPassword);

            var response = await client.GetAsync("/");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/account/change-password", response.Headers.Location!.OriginalString);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userMgr.FindByNameAsync(CustomWebApplicationFactory.UserUsername);
            user!.MustChangePassword = false;
            await userMgr.UpdateAsync(user);
            var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
            PostLoginRedirectMiddleware.EvictUserCache(cache, user.Id);
        }
    }

    [Fact]
    public async Task MustSetupTotp_RedirectsToSetupTotp()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userMgr.FindByNameAsync(CustomWebApplicationFactory.UserUsername);
            user!.MustSetupTotp = true;
            await userMgr.UpdateAsync(user);
            var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
            PostLoginRedirectMiddleware.EvictUserCache(cache, user.Id);
        }

        try
        {
            var client = await _factory.CreateAuthenticatedClientAsync(
                CustomWebApplicationFactory.UserUsername, CustomWebApplicationFactory.UserPassword);

            var response = await client.GetAsync("/");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/account/setup-totp", response.Headers.Location!.OriginalString);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userMgr.FindByNameAsync(CustomWebApplicationFactory.UserUsername);
            user!.MustSetupTotp = false;
            await userMgr.UpdateAsync(user);
            var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
            PostLoginRedirectMiddleware.EvictUserCache(cache, user.Id);
        }
    }

    [Fact]
    public async Task Signout_ThenAccessProtectedPage_RedirectsToLogin()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Verify authenticated first
        var authed = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, authed.StatusCode);

        // Sign out
        await client.PostAsync("/api/auth/signout", null);

        // Try to access protected page
        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task SecurityHeaders_ContainStrictCsp()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/");

        var csp = response.Headers.GetValues("Content-Security-Policy").FirstOrDefault();
        Assert.NotNull(csp);
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("script-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("base-uri 'self'", csp);
        Assert.Contains("form-action 'self'", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
    }
}
