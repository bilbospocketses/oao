namespace OpenAudioOrchestrator.Web;

public static class StringHelpers
{
    public static string Truncate(string? text, int maxLength) =>
        text is null ? string.Empty :
        text.Length <= maxLength ? text : text[..maxLength] + "...";
}
