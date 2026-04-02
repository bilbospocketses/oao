using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using OpenAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OpenAudioOrchestrator.Tests.Auth;

public class AdminSeedServiceTests
{
    private static (ServiceProvider sp, IConfiguration config) BuildServices(
        string? adminUser = null, string? adminPassword = null)
    {
        var configData = new Dictionary<string, string?>();
        if (adminUser is not null) configData["OpenAudioOrchestrator:AdminUser"] = adminUser;
        if (adminPassword is not null) configData["OpenAudioOrchestrator:AdminPassword"] = adminPassword;

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
