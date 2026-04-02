using OpenAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using System.Security.Cryptography;

namespace OpenAudioOrchestrator.Web.Endpoints;

public record ThemeRequest(string Theme);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (
            HttpContext httpContext,
            SignInManager<AppUser> signInManager,
            UserManager<AppUser> userManager) =>
        {
            var form = await httpContext.Request.ReadFormAsync();
            var username = form["username"].ToString();
            var password = form["password"].ToString();

            var user = await userManager.FindByNameAsync(username);
            if (user is null)
                return Results.Redirect("/login?error=invalid");

            var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                var errorCode = result.IsLockedOut ? "locked" : "invalid";
                return Results.Redirect($"/login?error={errorCode}");
            }

            var hasTwoFactor = await userManager.GetTwoFactorEnabledAsync(user);
            if (hasTwoFactor)
            {
                var cache = httpContext.RequestServices.GetRequiredService<IMemoryCache>();
                var totpToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                cache.Set($"totp-pending:{totpToken}", user.Id, TimeSpan.FromMinutes(2));
                return Results.Redirect($"/login/totp?tid={Uri.EscapeDataString(totpToken)}");
            }

            // Regenerate security stamp to invalidate any prior sessions (session fixation defense)
            await userManager.UpdateSecurityStampAsync(user);
            await signInManager.SignInAsync(user, isPersistent: false);
            return Results.Redirect("/");
        }).RequireRateLimiting("auth");

        app.MapPost("/api/auth/signin", async (
            HttpContext httpContext,
            SignInManager<AppUser> signInManager,
            UserManager<AppUser> userManager,
            IMemoryCache cache) =>
        {
            var form = await httpContext.Request.ReadFormAsync();
            var token = form["token"].ToString();
            var returnUrl = form["returnUrl"].ToString();

            if (string.IsNullOrEmpty(token))
                return Results.Redirect("/login?error=invalid");

            // Validate the one-time TOTP completion token
            var cacheKey = $"totp-verified:{token}";
            if (!cache.TryGetValue(cacheKey, out string? userId) || userId is null)
                return Results.Redirect("/login?error=invalid");

            // Remove token so it can only be used once
            cache.Remove(cacheKey);

            var user = await userManager.FindByIdAsync(userId);
            if (user is null)
                return Results.Redirect("/login");

            // Regenerate security stamp to invalidate any prior sessions (session fixation defense)
            await userManager.UpdateSecurityStampAsync(user);
            await signInManager.SignInAsync(user, isPersistent: false);

            // Prevent open redirect — only allow local paths
            if (!string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//"))
                return Results.Redirect(returnUrl);

            return Results.Redirect("/");
        }).RequireRateLimiting("auth");

        app.MapPost("/api/auth/signout", async (SignInManager<AppUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Redirect("/login");
        });

        app.MapPost("/api/auth/theme", async (
            HttpContext httpContext,
            ThemeRequest body,
            UserManager<AppUser> userManager) =>
        {
            if (body.Theme != "dark" && body.Theme != "light")
                return Results.BadRequest("Theme must be 'dark' or 'light'.");

            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await userManager.FindByIdAsync(userId!);
            if (user is null)
                return Results.Unauthorized();

            user.ThemePreference = body.Theme;
            await userManager.UpdateAsync(user);
            return Results.Ok();
        }).RequireAuthorization();

        app.MapGet("/api/auth/theme", async (
            HttpContext httpContext,
            UserManager<AppUser> userManager) =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await userManager.FindByIdAsync(userId!);
            if (user is null)
                return Results.Unauthorized();

            return Results.Ok(new { theme = user.ThemePreference });
        }).RequireAuthorization();
    }
}
