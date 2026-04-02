using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using OpenAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

namespace OpenAudioOrchestrator.Tests.Auth;

public class TotpServiceTests
{
    private static (ServiceProvider sp, UserManager<AppUser> userMgr) BuildServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddDataProtection();
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
        var (manualKey, qrDataUri) = await service.GenerateSetupInfoAsync(user, "OpenAudioOrchestrator");

        Assert.False(string.IsNullOrWhiteSpace(manualKey));
        Assert.StartsWith("data:image/png;base64,", qrDataUri);
    }

    // Compute TOTP code from a base32 key (matches ASP.NET Identity's Rfc6238 implementation)
    private static string ComputeTotp(string base32Key)
    {
        // Base32 decode
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var upper = base32Key.ToUpperInvariant().TrimEnd('=');
        var bits = new System.Text.StringBuilder();
        foreach (var c in upper)
        {
            var idx = alphabet.IndexOf(c);
            if (idx < 0) continue;
            bits.Append(Convert.ToString(idx, 2).PadLeft(5, '0'));
        }
        var bytes = new List<byte>();
        for (int i = 0; i + 8 <= bits.Length; i += 8)
            bytes.Add(Convert.ToByte(bits.ToString(i, 8), 2));
        var keyBytes = bytes.ToArray();

        // TOTP: 30-second window, 6 digits, HMAC-SHA1
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

        using var hmac = new HMACSHA1(keyBytes);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0f;
        var code = ((hash[offset] & 0x7f) << 24)
                 | (hash[offset + 1] << 16)
                 | (hash[offset + 2] << 8)
                 | hash[offset + 3];
        return (code % 1_000_000).ToString("D6");
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
        var (manualKey, _) = await service.GenerateSetupInfoAsync(user, "OpenAudioOrchestrator");

        // Reload so security stamp is current
        var freshUser = await userMgr.FindByNameAsync("testuser");
        Assert.NotNull(freshUser);

        // Generate a valid TOTP code from the key (same algorithm as authenticator apps)
        var token = ComputeTotp(manualKey);

        var result = await service.VerifyCodeAsync(freshUser, token);
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
        await service.GenerateSetupInfoAsync(user, "OpenAudioOrchestrator");

        var result = await service.VerifyCodeAsync(user, "000000");
        Assert.False(result);
    }
}
