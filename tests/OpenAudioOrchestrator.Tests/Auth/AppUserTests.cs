using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace OpenAudioOrchestrator.Tests.Auth;

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
