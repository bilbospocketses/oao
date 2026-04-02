namespace OpenAudioOrchestrator.Web.Data.Entities;

public class ReferenceVoice
{
    public int Id { get; set; }
    public required string VoiceId { get; set; }
    public required string DisplayName { get; set; }
    public required string AudioFileName { get; set; }
    public required string TranscriptText { get; set; }
    public string? Tags { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
