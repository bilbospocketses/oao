namespace OpenAudioOrchestrator.Web.Services;

public class TtsJobSignal
{
    private readonly SemaphoreSlim _semaphore = new(0);

    public void Signal() => _semaphore.Release();

    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => _semaphore.WaitAsync(timeout, cancellationToken);
}
