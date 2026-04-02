using System.Net;
using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace OpenAudioOrchestrator.Tests.Integration;

public class AuthEndpointTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.SeedTestDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Login_WithValidCredentials_RedirectsToRoot()
    {
        var client = _factory.CreateNonRedirectClient();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = CustomWebApplicationFactory.AdminUsername,
            ["password"] = CustomWebApplicationFactory.AdminPassword
        });

        var response = await client.PostAsync("/api/auth/login", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Login_WithInvalidPassword_RedirectsWithError()
    {
        var client = _factory.CreateNonRedirectClient();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = CustomWebApplicationFactory.AdminUsername,
            ["password"] = "completely-wrong"
        });

        var response = await client.PostAsync("/api/auth/login", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("error=invalid", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Login_WithNonexistentUser_RedirectsWithError()
    {
        var client = _factory.CreateNonRedirectClient();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "nobody",
            ["password"] = "doesnt-matter"
        });

        var response = await client.PostAsync("/api/auth/login", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("error=invalid", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Login_WithTwoFactorUser_RedirectsToTotpWithOpaqueToken()
    {
        // Enable 2FA for the admin user
        using (var scope = _factory.Services.CreateScope())
        {
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var admin = await userMgr.FindByNameAsync(CustomWebApplicationFactory.AdminUsername);
            await userMgr.ResetAuthenticatorKeyAsync(admin!);
            await userMgr.SetTwoFactorEnabledAsync(admin!, true);
        }

        try
        {
            var client = _factory.CreateNonRedirectClient();

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = CustomWebApplicationFactory.AdminUsername,
                ["password"] = CustomWebApplicationFactory.AdminPassword
            });

            var response = await client.PostAsync("/api/auth/login", form);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var location = response.Headers.Location!.OriginalString;
            Assert.StartsWith("/login/totp?tid=", location);
            // Verify it's NOT a raw user ID (GUID format)
            var tid = location.Split("tid=")[1];
            Assert.False(Guid.TryParse(Uri.UnescapeDataString(tid), out _),
                "TOTP redirect should use opaque token, not user ID");
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var admin = await userMgr.FindByNameAsync(CustomWebApplicationFactory.AdminUsername);
            await userMgr.SetTwoFactorEnabledAsync(admin!, false);
        }
    }

    [Fact]
    public async Task Signin_WithoutToken_RedirectsToLoginError()
    {
        var client = _factory.CreateNonRedirectClient();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>());
        var response = await client.PostAsync("/api/auth/signin", form);

        // Empty token should be treated as invalid
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("error=invalid", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Signin_WithInvalidToken_RedirectsToLoginError()
    {
        var client = _factory.CreateNonRedirectClient();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = "bogus-token-value"
        });
        var response = await client.PostAsync("/api/auth/signin", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("error=invalid", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Signin_WithValidToken_SignsInAndRedirects()
    {
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin = await userMgr.FindByNameAsync(CustomWebApplicationFactory.AdminUsername);

        var token = "test-valid-token-12345";
        cache.Set($"totp-verified:{token}", admin!.Id, TimeSpan.FromSeconds(60));

        var client = _factory.CreateNonRedirectClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token
        });
        var response = await client.PostAsync("/api/auth/signin", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Signin_TokenIsConsumed_CannotBeReused()
    {
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin = await userMgr.FindByNameAsync(CustomWebApplicationFactory.AdminUsername);

        var token = "one-time-use-token";
        cache.Set($"totp-verified:{token}", admin!.Id, TimeSpan.FromSeconds(60));

        var client = _factory.CreateNonRedirectClient();

        // First use — should succeed
        var first = await client.PostAsync("/api/auth/signin", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["token"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal("/", first.Headers.Location!.OriginalString);

        // Second use — token consumed, should fail
        var second = await client.PostAsync("/api/auth/signin", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["token"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Contains("error=invalid", second.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Signin_OpenRedirectBlocked()
    {
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin = await userMgr.FindByNameAsync(CustomWebApplicationFactory.AdminUsername);

        var token = "redirect-test-token";
        cache.Set($"totp-verified:{token}", admin!.Id, TimeSpan.FromSeconds(60));

        var client = _factory.CreateNonRedirectClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
            ["returnUrl"] = "https://evil.com"
        });
        var response = await client.PostAsync("/api/auth/signin", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Signin_DoubleSlashRedirectBlocked()
    {
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin = await userMgr.FindByNameAsync(CustomWebApplicationFactory.AdminUsername);

        var token = "double-slash-test-token";
        cache.Set($"totp-verified:{token}", admin!.Id, TimeSpan.FromSeconds(60));

        var client = _factory.CreateNonRedirectClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
            ["returnUrl"] = "//evil.com"
        });
        var response = await client.PostAsync("/api/auth/signin", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Signin_LocalReturnUrlAllowed()
    {
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin = await userMgr.FindByNameAsync(CustomWebApplicationFactory.AdminUsername);

        var token = "local-redirect-token";
        cache.Set($"totp-verified:{token}", admin!.Id, TimeSpan.FromSeconds(60));

        var client = _factory.CreateNonRedirectClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
            ["returnUrl"] = "/playground"
        });
        var response = await client.PostAsync("/api/auth/signin", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/playground", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Signout_RedirectsToLogin()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/auth/signout", null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location!.OriginalString);
    }
}

/// <summary>
/// Separate fixture for the lockout test to avoid rate limiter interference with other tests.
/// </summary>
public class AuthLockoutTests : IAsyncLifetime
{
    private CustomWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new CustomWebApplicationFactory();
        await _factory.SeedTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Login_AccountLockout_AfterFiveFailedAttempts()
    {
        var client = _factory.CreateNonRedirectClient();

        var badForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = CustomWebApplicationFactory.UserUsername,
            ["password"] = "wrong-password"
        });

        // Fail 5 times to trigger lockout
        for (int i = 0; i < 5; i++)
        {
            await client.PostAsync("/api/auth/login", badForm);
        }

        // 6th attempt should be locked out
        var response = await client.PostAsync("/api/auth/login", badForm);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("error=locked", response.Headers.Location!.OriginalString);
    }
}
