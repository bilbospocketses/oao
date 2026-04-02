namespace OpenAudioOrchestrator.Web.Data.Entities;

public class GenerationLog
{
    public int Id { get; set; }
    public int ModelProfileId { get; set; }
    public int? ReferenceVoiceId { get; set; }
    public string? UserId { get; set; }
    public required string InputText { get; set; }
    public required string OutputFileName { get; set; }
    public required string Format { get; set; }
    public long DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ModelProfile ModelProfile { get; set; } = null!;
    public ReferenceVoice? ReferenceVoice { get; set; }
    public AppUser? User { get; set; }
}
