using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace OpenAudioOrchestrator.Web.Services;

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
        var adminUser = _config["OpenAudioOrchestrator:AdminUser"];
        var adminPassword = _config["OpenAudioOrchestrator:AdminPassword"];

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
