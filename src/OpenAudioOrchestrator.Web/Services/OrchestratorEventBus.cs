using OpenAudioOrchestrator.Web.Hubs;
using Microsoft.Extensions.Logging;

namespace OpenAudioOrchestrator.Web.Services;

public class OrchestratorEventBus
{
    private readonly ILogger<OrchestratorEventBus> _logger;

    public OrchestratorEventBus(ILogger<OrchestratorEventBus> logger)
    {
        _logger = logger;
    }

    private readonly WeakEvent<List<ContainerStatusEvent>> _containerStatus = new();
    private readonly WeakEvent<TtsNotificationEvent> _ttsNotification = new();
    private readonly WeakEvent<LogLineEvent> _logLine = new();
    private readonly WeakEvent<GpuMetricsEvent> _gpuMetrics = new();
    private readonly WeakEvent<TtsJobStatusEvent> _ttsJobStatus = new();

    public event Action<List<ContainerStatusEvent>>? OnContainerStatus
    {
        add => _containerStatus.Add(value);
        remove => _containerStatus.Remove(value);
    }

    public event Action<TtsNotificationEvent>? OnTtsNotification
    {
        add => _ttsNotification.Add(value);
        remove => _ttsNotification.Remove(value);
    }

    public event Action<LogLineEvent>? OnLogLine
    {
        add => _logLine.Add(value);
        remove => _logLine.Remove(value);
    }

    public event Action<GpuMetricsEvent>? OnGpuMetrics
    {
        add => _gpuMetrics.Add(value);
        remove => _gpuMetrics.Remove(value);
    }

    public event Action<TtsJobStatusEvent>? OnTtsJobStatus
    {
        add => _ttsJobStatus.Add(value);
        remove => _ttsJobStatus.Remove(value);
    }

    public void RaiseContainerStatus(List<ContainerStatusEvent> events)
        => _containerStatus.Invoke(events, _logger);

    public void RaiseTtsNotification(TtsNotificationEvent notification)
        => _ttsNotification.Invoke(notification, _logger);

    public void RaiseLogLine(LogLineEvent logLine)
        => _logLine.Invoke(logLine, _logger);

    public void RaiseGpuMetrics(GpuMetricsEvent metrics)
        => _gpuMetrics.Invoke(metrics, _logger);

    public void RaiseTtsJobStatus(TtsJobStatusEvent evt)
        => _ttsJobStatus.Invoke(evt, _logger);
}

/// <summary>
/// A thread-safe event implementation that holds weak references to subscriber targets.
/// Static/lambda delegates (with no target) are stored as strong references since there
/// is nothing to prevent from being collected.
/// Collected subscribers are automatically pruned on each invocation.
/// </summary>
internal class WeakEvent<T>
{
    private readonly List<Subscription> _subscriptions = new();
    private readonly object _lock = new();

    public void Add(Action<T>? handler)
    {
        if (handler is null) return;
        lock (_lock)
        {
            _subscriptions.Add(new Subscription(handler));
        }
    }

    public void Remove(Action<T>? handler)
    {
        if (handler is null) return;
        lock (_lock)
        {
            for (int i = _subscriptions.Count - 1; i >= 0; i--)
            {
                if (_subscriptions[i].Matches(handler))
                {
                    _subscriptions.RemoveAt(i);
                    return;
                }
            }
        }
    }

    public void Invoke(T arg, ILogger? logger = null)
    {
        List<Action<T>> toInvoke;
        lock (_lock)
        {
            toInvoke = new List<Action<T>>(_subscriptions.Count);
            for (int i = _subscriptions.Count - 1; i >= 0; i--)
            {
                var handler = _subscriptions[i].GetHandler();
                if (handler is not null)
                {
                    toInvoke.Add(handler);
                }
                else
                {
                    // Target was garbage collected — prune
                    _subscriptions.RemoveAt(i);
                }
            }
        }

        foreach (var handler in toInvoke)
        {
            try
            {
                handler(arg);
            }
            catch (ObjectDisposedException)
            {
                // Subscriber was disposed between the check and invocation — ignore
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Event subscriber threw an exception");
            }
        }
    }

    private class Subscription
    {
        // For instance methods: weak ref to the target object + the MethodInfo
        private readonly WeakReference<object>? _weakTarget;
        private readonly System.Reflection.MethodInfo? _method;

        // For static/lambda delegates (no target): store the delegate directly
        private readonly Action<T>? _strongHandler;

        public Subscription(Action<T> handler)
        {
            if (handler.Target is not null)
            {
                _weakTarget = new WeakReference<object>(handler.Target);
                _method = handler.Method;
            }
            else
            {
                // Static method or lambda with no captures — must hold strongly
                _strongHandler = handler;
            }
        }

        public Action<T>? GetHandler()
        {
            if (_strongHandler is not null)
                return _strongHandler;

            if (_weakTarget is not null && _weakTarget.TryGetTarget(out var target))
                return (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), target, _method!);

            return null; // Target was collected
        }

        public bool Matches(Action<T> handler)
        {
            if (_strongHandler is not null)
                return _strongHandler == handler;

            if (_weakTarget is not null && _method == handler.Method &&
                _weakTarget.TryGetTarget(out var target))
                return ReferenceEquals(target, handler.Target);

            return false;
        }
    }
}
