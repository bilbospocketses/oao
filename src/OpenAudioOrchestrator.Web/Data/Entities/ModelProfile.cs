namespace OpenAudioOrchestrator.Web.Data.Entities;

public enum ModelStatus
{
    Created,
    Running,
    Stopped,
    Error
}

public class ModelProfile
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string CheckpointPath { get; set; }
    public required string ImageTag { get; set; }
    public int HostPort { get; set; }
    public bool EnableHalf { get; set; } = true;
    public string? CudaAllocConf { get; set; }
    public string? ContainerId { get; set; }
    public ModelStatus Status { get; set; } = ModelStatus.Created;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastStartedAt { get; set; }
}
