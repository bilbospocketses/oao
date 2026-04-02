using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;

namespace OpenAudioOrchestrator.Web.Services;

/// <summary>
/// Handles writing application settings and database encryption during setup.
/// </summary>
public class SetupSettingsService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SetupSettingsService> _logger;
    private readonly IDataProtectionProvider _dataProtection;

    public SetupSettingsService(
        IWebHostEnvironment env,
        ILogger<SetupSettingsService> logger,
        IDataProtectionProvider dataProtection)
    {
        _env = env;
        _logger = logger;
        _dataProtection = dataProtection;
    }

    public static string GenerateDatabaseKey()
    {
        return Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    }

    public async Task SaveSettingsAsync(
        string databasePath,
        string checkpointsDir,
        string referencesDir,
        string outputDir,
        int portStart,
        int portEnd,
        string? domain,
        string? email,
        string? databaseKey = null)
    {
        var settingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
        var json = await File.ReadAllTextAsync(settingsPath);
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!;

        // Derive DataRoot as the common parent of the three directories
        var dataRoot = Path.GetDirectoryName(checkpointsDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                       ?? checkpointsDir;

        // ConnectionStrings — use the user-specified database path
        root["ConnectionStrings"]!["Default"] = $"Data Source={Path.GetFullPath(databasePath)}";

        // OpenAudioOrchestrator
        var oao = root["OpenAudioOrchestrator"]!;
        oao["DataRoot"] = dataRoot;
        oao["PortRange"]!["Start"] = portStart;
        oao["PortRange"]!["End"] = portEnd;
        oao["Domain"] = domain ?? "";
        if (databaseKey is not null)
        {
            var protector = _dataProtection.CreateProtector("DatabaseKey");
            oao["DatabaseKey"] = protector.Protect(databaseKey);
        }

        // LettuceEncrypt
        var le = root["LettuceEncrypt"]!;
        if (!string.IsNullOrWhiteSpace(domain))
        {
            le["DomainNames"] = new JsonArray(domain);
            le["EmailAddress"] = email ?? "";
        }
        else
        {
            le["DomainNames"] = new JsonArray();
            le["EmailAddress"] = "";
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        var output = root.ToJsonString(options);
        await File.WriteAllTextAsync(settingsPath, output);

        _logger.LogInformation("Settings saved to {Path}", settingsPath);
    }

    /// <summary>
    /// Encrypts an existing unencrypted SQLite database using SQLCipher.
    /// Creates an encrypted copy, then replaces the original.
    /// </summary>
    /// <summary>
    /// Encrypts an unencrypted SQLite database file.
    /// Accepts a pre-made unencrypted copy so the live DB file (held by EF Core) is not touched.
    /// If <paramref name="unlockedCopyPath"/> is null, the method copies <paramref name="dbPath"/> itself.
    /// </summary>
    public async Task EncryptDatabaseAsync(string dbPath, string key, string? unlockedCopyPath = null)
    {
        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("Database file not found at {Path}, skipping encryption", dbPath);
            return;
        }

        var sourcePath = unlockedCopyPath ?? dbPath;
        var encryptedPath = dbPath + ".encrypted";
        try
        {
            // If no unlocked copy was provided, make one so we don't fight EF Core's file lock
            if (unlockedCopyPath is null)
            {
                sourcePath = dbPath + ".unencrypted-copy";
                // Clear pools first so the copy gets a consistent snapshot
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                await Task.Delay(500);
                File.Copy(dbPath, sourcePath, overwrite: true);
            }

            // Open the unencrypted copy and export to an encrypted file
            // using SQLCipher's ATTACH + sqlcipher_export approach.
            using var source = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={sourcePath}");
            await source.OpenAsync();

            using (var cmd = source.CreateCommand())
            {
                cmd.CommandText = $"ATTACH DATABASE '{encryptedPath.Replace("'", "''")}' AS encrypted KEY '{key.Replace("'", "''")}';";
                await cmd.ExecuteNonQueryAsync();
            }

            using (var cmd = source.CreateCommand())
            {
                cmd.CommandText = "SELECT sqlcipher_export('encrypted');";
                await cmd.ExecuteNonQueryAsync();
            }

            using (var cmd = source.CreateCommand())
            {
                cmd.CommandText = "DETACH DATABASE encrypted;";
                await cmd.ExecuteNonQueryAsync();
            }

            source.Close();

            // Now swap: clear pools again so EF Core releases the live file, then replace it
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            await Task.Delay(500);

            File.Move(encryptedPath, dbPath, overwrite: true);

            // Remove WAL/SHM journal files
            var walPath = dbPath + "-wal";
            var shmPath = dbPath + "-shm";
            if (File.Exists(walPath)) File.Delete(walPath);
            if (File.Exists(shmPath)) File.Delete(shmPath);

            _logger.LogInformation("Database encrypted successfully at {Path}", dbPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt database at {Path}", dbPath);
            throw;
        }
        finally
        {
            // Clean up temp files
            if (unlockedCopyPath is null && sourcePath != dbPath)
                try { if (File.Exists(sourcePath)) File.Delete(sourcePath); } catch { }
            try { if (File.Exists(encryptedPath)) File.Delete(encryptedPath); } catch { }
        }
    }
}
