using OpenAudioOrchestrator.Web.Hubs;

namespace OpenAudioOrchestrator.Web.Services;

public class GpuMetricsState
{
    private volatile GpuMetricsEvent? _current;

    public int MemoryUsedMb => _current?.MemoryUsedMb ?? 0;
    public int MemoryTotalMb => _current?.MemoryTotalMb ?? 0;
    public int UtilizationPercent => _current?.UtilizationPercent ?? 0;
    public DateTimeOffset LastUpdated { get; private set; }

    public event Action? OnChange;

    public void Update(GpuMetricsEvent metrics)
    {
        _current = metrics;
        LastUpdated = DateTimeOffset.UtcNow;
        OnChange?.Invoke();
    }
}
