using System.Text.RegularExpressions;

namespace oao.Web.Endpoints;

public static class AudioEndpoints
{
    private static readonly Regex SafeOutputFileNameRegex =
        new(@"^[a-zA-Z0-9 ._\-]+$", RegexOptions.Compiled);

    private static readonly Regex SafeReferencePathRegex =
        new(@"^[a-zA-Z0-9 ._\-/]+$", RegexOptions.Compiled);

    public static void MapAudioEndpoints(this WebApplication app)
    {
        var dataRoot = PlatformDefaults.ConfigValueOrDefault(
            app.Configuration["oao:DataRoot"], PlatformDefaults.DataRoot);
        var outputRoot = Path.GetFullPath(Path.Combine(dataRoot, "Output"));
        var referencesRoot = Path.GetFullPath(Path.Combine(dataRoot, "References"));

        app.MapGet("/audio/output/{fileName}", (string fileName) =>
        {
            if (fileName.Contains("..") || !SafeOutputFileNameRegex.IsMatch(fileName))
                return Results.NotFound();

            var filePath = Path.GetFullPath(Path.Combine(outputRoot, fileName));
            if (!filePath.StartsWith(outputRoot + Path.DirectorySeparatorChar))
                return Results.NotFound();

            if (!File.Exists(filePath))
                return Results.NotFound();

            return Results.File(filePath, "audio/wav", enableRangeProcessing: true);
        }).RequireAuthorization();

        app.MapGet("/audio/references/{*filePath}", (string filePath) =>
        {
            if (filePath.Contains("..") || !SafeReferencePathRegex.IsMatch(filePath))
                return Results.NotFound();

            var fullPath = Path.GetFullPath(Path.Combine(referencesRoot, filePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(referencesRoot + Path.DirectorySeparatorChar))
                return Results.NotFound();

            if (!File.Exists(fullPath))
                return Results.NotFound();

            var contentType = Path.GetExtension(fullPath).ToLowerInvariant() switch
            {
                ".wav" => "audio/wav",
                ".mp3" => "audio/mpeg",
                ".flac" => "audio/flac",
                ".ogg" => "audio/ogg",
                _ => "application/octet-stream"
            };
            return Results.File(fullPath, contentType, enableRangeProcessing: true);
        }).RequireAuthorization();
    }
}
