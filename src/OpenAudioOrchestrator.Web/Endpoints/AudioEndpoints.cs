namespace OpenAudioOrchestrator.Web.Endpoints;

public static class AudioEndpoints
{
    public static void MapAudioEndpoints(this WebApplication app)
    {
        var dataRoot = PlatformDefaults.ConfigValueOrDefault(
            app.Configuration["OpenAudioOrchestrator:DataRoot"], PlatformDefaults.DataRoot);
        var outputRoot = Path.GetFullPath(Path.Combine(dataRoot, "Output"));
        var referencesRoot = Path.GetFullPath(Path.Combine(dataRoot, "References"));

        app.MapGet("/audio/output/{fileName}", (string fileName) =>
        {
            var filePath = Path.GetFullPath(Path.Combine(outputRoot, fileName));
            if (!filePath.StartsWith(outputRoot + Path.DirectorySeparatorChar))
                return Results.NotFound();

            if (!File.Exists(filePath))
                return Results.NotFound();

            return Results.File(filePath, "audio/wav", enableRangeProcessing: true);
        }).RequireAuthorization();

        app.MapGet("/audio/references/{*filePath}", (string filePath) =>
        {
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
