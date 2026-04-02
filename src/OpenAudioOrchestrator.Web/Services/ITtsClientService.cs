namespace OpenAudioOrchestrator.Web.Services;

public class TtsRequest
{
    public required string Text { get; set; }
    public string? ReferenceId { get; set; }
    public string Format { get; set; } = "wav";
}

public interface ITtsClientService
{
    Task<bool> GetHealthAsync(string baseUrl);
}
