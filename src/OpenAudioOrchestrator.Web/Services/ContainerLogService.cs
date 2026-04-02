using System.Collections.Concurrent;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using OpenAudioOrchestrator.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace OpenAudioOrchestrator.Web.Services;

public class ContainerLogService : IContainerLogService
{
    private readonly IDockerClient _docker;
    private readonly IHubContext<OrchestratorHub> _hub;
    private readonly ILogger<ContainerLogService> _logger;
    private readonly ConcurrentDictionary<string, ContainerLogStream> _streams = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Action<LogLineEvent>>> _callbackSubscribers = new();

    public ContainerLogService(IDockerClient docker, IHubContext<OrchestratorHub> hub, ILogger<ContainerLogService> logger)
    {
        _docker = docker;
        _hub = hub;
        _logger = logger;
    }

    public Task SubscribeAsync(string containerId, string connectionId)
    {
        if (!ContainerIdValidator.IsValid(containerId)) return Task.CompletedTask;

        var stream = _streams.GetOrAdd(containerId, _ => new ContainerLogStream());

        lock (stream.Lock)
        {
            stream.Subscribers.Add(connectionId);

            if (stream.ReaderTask is null || stream.ReaderTask.IsCompleted)
            {
                stream.Cts = new CancellationTokenSource();
                stream.ReaderTask = Task.Run(() => ReadLogStreamAsync(containerId, stream));
            }
        }

        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(string containerId, string connectionId)
    {
        if (!ContainerIdValidator.IsValid(containerId)) return Task.CompletedTask;

        if (!_streams.TryGetValue(containerId, out var stream))
            return Task.CompletedTask;

        bool shouldCancel;
        lock (stream.Lock)
        {
            stream.Subscribers.Remove(connectionId);
            shouldCancel = stream.Subscribers.Count == 0
                && !HasCallbackSubscribers(containerId);
        }

        if (shouldCancel)
        {
            stream.Cts?.Cancel();
            _streams.TryRemove(containerId, out _);
        }

        return Task.CompletedTask;
    }

    public Task UnsubscribeAllAsync(string connectionId)
    {
        foreach (var kvp in _streams)
        {
            bool shouldCancel;
            lock (kvp.Value.Lock)
            {
                kvp.Value.Subscribers.Remove(connectionId);
                shouldCancel = kvp.Value.Subscribers.Count == 0
                    && !HasCallbackSubscribers(kvp.Key);
            }

            if (shouldCancel)
            {
                kvp.Value.Cts?.Cancel();
                _streams.TryRemove(kvp.Key, out _);
            }
        }

        return Task.CompletedTask;
    }

    public bool HasSubscribers(string containerId)
    {
        if (_streams.TryGetValue(containerId, out var stream))
        {
            lock (stream.Lock)
            {
                if (stream.Subscribers.Count > 0) return true;
            }
        }

        return HasCallbackSubscribers(containerId);
    }

    private bool HasCallbackSubscribers(string containerId)
    {
        return _callbackSubscribers.TryGetValue(containerId, out var subs) && !subs.IsEmpty;
    }

    public void SubscribeCallback(string containerId, string subscriberId, Action<LogLineEvent> callback)
    {
        if (!ContainerIdValidator.IsValid(containerId)) return;

        var subs = _callbackSubscribers.GetOrAdd(containerId, _ => new ConcurrentDictionary<string, Action<LogLineEvent>>());
        subs[subscriberId] = callback;

        // Ensure the Docker log reader is running for this container
        var stream = _streams.GetOrAdd(containerId, _ => new ContainerLogStream());
        lock (stream.Lock)
        {
            // Replay recent lines to the new subscriber
            foreach (var line in stream.RecentLines)
            {
                try { callback(line); } catch { }
            }

            if (stream.ReaderTask is null || stream.ReaderTask.IsCompleted)
            {
                stream.Cts = new CancellationTokenSource();
                stream.ReaderTask = Task.Run(() => ReadLogStreamAsync(containerId, stream));
            }
        }
    }

    public void UnsubscribeCallback(string containerId, string subscriberId)
    {
        if (_callbackSubscribers.TryGetValue(containerId, out var subs))
        {
            subs.TryRemove(subscriberId, out _);
            if (subs.IsEmpty)
            {
                _callbackSubscribers.TryRemove(containerId, out _);
            }
        }

        // Check if we should stop the reader — must check both subscriber types under the stream's lock
        if (_streams.TryGetValue(containerId, out var stream))
        {
            bool shouldCancel;
            lock (stream.Lock)
            {
                shouldCancel = stream.Subscribers.Count == 0
                    && !HasCallbackSubscribers(containerId);
            }

            if (shouldCancel)
            {
                stream.Cts?.Cancel();
                _streams.TryRemove(containerId, out _);
            }
        }
    }

    public void UnsubscribeAllCallbacks(string subscriberId)
    {
        foreach (var kvp in _callbackSubscribers)
        {
            kvp.Value.TryRemove(subscriberId, out _);
            if (kvp.Value.IsEmpty)
            {
                _callbackSubscribers.TryRemove(kvp.Key, out _);
            }
        }
    }

    private async Task ReadLogStreamAsync(string containerId, ContainerLogStream logStream)
    {
        try
        {
            var parameters = new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = true,
                Tail = "50",
                Timestamps = true
            };

            using var stream = await _docker.Containers.GetContainerLogsAsync(
                containerId, false, parameters, logStream.Cts!.Token);

            var buffer = new byte[4096];
            while (!logStream.Cts.Token.IsCancellationRequested)
            {
                var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, logStream.Cts.Token);
                if (result.Count == 0) break;

                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    var logEvent = ParseLogLine(containerId, line);

                    lock (logStream.Lock)
                    {
                        logStream.AddLine(logEvent);
                    }

                    string[] subscribers;
                    lock (logStream.Lock)
                    {
                        subscribers = logStream.Subscribers.ToArray();
                    }

                    if (subscribers.Length > 0)
                    {
                        await _hub.Clients.Clients(subscribers)
                            .SendAsync("ReceiveLogLine", logEvent, logStream.Cts.Token);
                    }

                    // Dispatch to in-process callback subscribers
                    if (_callbackSubscribers.TryGetValue(containerId, out var callbackSubs))
                    {
                        foreach (var cb in callbackSubs.Values)
                        {
                            try { cb(logEvent); } catch { }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Log stream for {ContainerId} ended with error", containerId);
        }
    }

    private static LogLineEvent ParseLogLine(string containerId, string rawLine)
    {
        var trimmed = rawLine.Trim();
        if (trimmed.Length > 30 && trimmed[4] == '-' && trimmed[10] == 'T')
        {
            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx > 0 && DateTimeOffset.TryParse(trimmed[..spaceIdx], out var ts))
            {
                return new LogLineEvent(containerId, ts, trimmed[(spaceIdx + 1)..]);
            }
        }

        return new LogLineEvent(containerId, DateTimeOffset.UtcNow, trimmed);
    }

    private class ContainerLogStream
    {
        public readonly object Lock = new();
        public readonly HashSet<string> Subscribers = new();
        public readonly List<LogLineEvent> RecentLines = new();
        public CancellationTokenSource? Cts;
        public Task? ReaderTask;

        public void AddLine(LogLineEvent line)
        {
            RecentLines.Add(line);
            if (RecentLines.Count > 100)
                RecentLines.RemoveRange(0, RecentLines.Count - 100);
        }
    }
}
