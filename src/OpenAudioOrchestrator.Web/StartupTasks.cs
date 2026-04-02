using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace OpenAudioOrchestrator.Web;

public static class StartupTasks
{
    // Default DB path used during initial setup before the user picks a location
    private static string DefaultDbPath => PlatformDefaults.DbPath;

    public static async Task RunAsync(WebApplication app)
    {
        await RunMigrationsAsync(app);
        await SeedRolesAsync(app);
        await SeedAdminAsync(app);
        await EnsureDockerNetworkAsync(app);
        RestrictDatabaseFilePermissions(app);
        CleanupOldDatabase(app);
    }

    private static async Task RunMigrationsAsync(WebApplication app)
    {
        // Ensure the database directory exists before SQLite tries to create the file
        var connectionString = PlatformDefaults.ConfigValueOrDefault(
            app.Configuration.GetConnectionString("Default"),
            $"Data Source={PlatformDefaults.DbPath}");
        if (connectionString is not null)
        {
            var prefix = "Data Source=";
            var idx = connectionString.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var pathStart = idx + prefix.Length;
                var semicolonIdx = connectionString.IndexOf(';', pathStart);
                var dbPath = semicolonIdx >= 0 ? connectionString[pathStart..semicolonIdx] : connectionString[pathStart..];
                var dbDir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
                if (dbDir is not null && !Directory.Exists(dbDir))
                    Directory.CreateDirectory(dbDir);
            }
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    private static async Task SeedRolesAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "User" })
        {
            if (!await roleMgr.RoleExistsAsync(role))
                await roleMgr.CreateAsync(new IdentityRole(role));
        }
    }

    private static async Task SeedAdminAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IAdminSeedService>();
        await seeder.SeedIfConfiguredAsync();
    }

    private static async Task EnsureDockerNetworkAsync(WebApplication app)
    {
        try
        {
            var networkService = app.Services.GetRequiredService<IDockerNetworkService>();
            await networkService.EnsureNetworkExistsAsync();
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Could not ensure Docker bridge network exists. Docker may not be running.");
        }
    }

    private static void RestrictDatabaseFilePermissions(WebApplication app)
    {
        var connectionString = PlatformDefaults.ConfigValueOrDefault(
            app.Configuration.GetConnectionString("Default"),
            $"Data Source={PlatformDefaults.DbPath}");
        if (connectionString is null) return;

        // Extract the file path from "Data Source=..."
        var dataSourcePrefix = "Data Source=";
        var idx = connectionString.IndexOf(dataSourcePrefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return;

        var pathStart = idx + dataSourcePrefix.Length;
        var semicolonIdx = connectionString.IndexOf(';', pathStart);
        var dbPath = semicolonIdx >= 0
            ? connectionString[pathStart..semicolonIdx]
            : connectionString[pathStart..];

        if (!File.Exists(dbPath)) return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: restrict to current user only
                var fileInfo = new FileInfo(dbPath);
                var security = fileInfo.GetAccessControl();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

                var currentUser = WindowsIdentity.GetCurrent().Name;
                security.AddAccessRule(new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));

                fileInfo.SetAccessControl(security);
            }
            else
            {
                // Linux/macOS: chmod 600
                File.SetUnixFileMode(dbPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            app.Logger.LogInformation("Database file permissions restricted: {Path}", dbPath);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Could not restrict database file permissions for {Path}", dbPath);
        }
    }

    /// <summary>
    /// If the configured database path differs from the default setup location,
    /// delete the leftover default DB files. This handles the case where the
    /// Setup wizard copied the DB to a user-chosen path but couldn't delete
    /// the original because EF Core held the file lock.
    /// </summary>
    private static void CleanupOldDatabase(WebApplication app)
    {
        var connectionString = PlatformDefaults.ConfigValueOrDefault(
            app.Configuration.GetConnectionString("Default"),
            $"Data Source={PlatformDefaults.DbPath}");
        if (connectionString is null) return;

        var dataSourcePrefix = "Data Source=";
        var idx = connectionString.IndexOf(dataSourcePrefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return;

        var pathStart = idx + dataSourcePrefix.Length;
        var semicolonIdx = connectionString.IndexOf(';', pathStart);
        var configuredPath = Path.GetFullPath(semicolonIdx >= 0
            ? connectionString[pathStart..semicolonIdx]
            : connectionString[pathStart..]);

        var defaultPath = Path.GetFullPath(DefaultDbPath);

        // Only clean up if the configured path is different from the default
        if (string.Equals(configuredPath, defaultPath, StringComparison.OrdinalIgnoreCase))
            return;

        // Delete the default DB and its journal files if they exist
        foreach (var file in new[] { defaultPath, defaultPath + "-wal", defaultPath + "-shm" })
        {
            if (File.Exists(file))
            {
                try
                {
                    File.Delete(file);
                    app.Logger.LogInformation("Cleaned up old database file: {Path}", file);
                }
                catch (Exception ex)
                {
                    app.Logger.LogWarning(ex, "Could not delete old database file: {Path}", file);
                }
            }
        }

        // Remove the default directory if it's now empty
        var defaultDir = Path.GetDirectoryName(defaultPath);
        if (defaultDir is not null && Directory.Exists(defaultDir))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(defaultDir).Any())
                {
                    Directory.Delete(defaultDir);
                    app.Logger.LogInformation("Cleaned up empty default directory: {Path}", defaultDir);
                }
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Could not delete default directory: {Path}", defaultDir);
            }
        }
    }
}
