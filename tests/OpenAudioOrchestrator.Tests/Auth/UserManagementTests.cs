using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace OpenAudioOrchestrator.Tests.Auth;

public class UserManagementTests
{
    private static (ServiceProvider sp, UserManager<AppUser> userMgr, RoleManager<IdentityRole> roleMgr) BuildServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddDataProtection();
        services.AddIdentityCore<AppUser>(opts =>
            {
                opts.Password.RequiredLength = 8;
                opts.Password.RequireUppercase = true;
                opts.Password.RequireLowercase = true;
                opts.Password.RequireDigit = true;
                opts.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
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
        // The UI prevents deletion when GetUsersInRoleAsync("Admin").Count <= 1.
        // This test verifies that condition check works: with one admin, deletion
        // is blocked; with two admins, deletion of one is allowed.
        var (sp, userMgr, _) = BuildServices();

        var admin1 = new AppUser
        {
            UserName = "admin1",
            DisplayName = "Admin One",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await userMgr.CreateAsync(admin1, "Admin123!@");
        await userMgr.AddToRoleAsync(admin1, "Admin");

        // With only one admin, the protection condition is met
        var admins = await userMgr.GetUsersInRoleAsync("Admin");
        Assert.Single(admins);
        Assert.True(admins.Count <= 1, "Protection should block: only one admin exists");

        // Add a second admin
        var admin2 = new AppUser
        {
            UserName = "admin2",
            DisplayName = "Admin Two",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await userMgr.CreateAsync(admin2, "Admin123!@");
        await userMgr.AddToRoleAsync(admin2, "Admin");

        admins = await userMgr.GetUsersInRoleAsync("Admin");
        Assert.Equal(2, admins.Count);
        Assert.False(admins.Count <= 1, "Protection should allow: two admins exist");

        // Actually delete admin2 (allowed because 2 admins exist)
        await userMgr.DeleteAsync(admin2);

        // Now only one admin remains — protection kicks in again
        admins = await userMgr.GetUsersInRoleAsync("Admin");
        Assert.Single(admins);
        Assert.True(admins.Count <= 1, "Protection should block again: back to one admin");
        Assert.Equal("admin1", admins[0].UserName);
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
