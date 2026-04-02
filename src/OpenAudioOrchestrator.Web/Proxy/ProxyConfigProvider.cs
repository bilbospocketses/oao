using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace OpenAudioOrchestrator.Web.Proxy;

public class ProxyConfigProvider : IProxyConfigProvider
{
    private volatile ProxyConfig _config;
    private CancellationTokenSource _cts = new();
    private readonly object _lock = new();

    public ProxyConfigProvider()
    {
        _config = new ProxyConfig(null, _cts.Token);
    }

    public IProxyConfig GetConfig() => _config;

    public virtual void UpdateDestination(string destinationUrl)
    {
        CancellationTokenSource oldCts;
        lock (_lock)
        {
            oldCts = _cts;
            _cts = new CancellationTokenSource();

            var destinations = new Dictionary<string, DestinationConfig>
            {
                ["active-model"] = new DestinationConfig { Address = destinationUrl }
            };

            _config = new ProxyConfig(destinations, _cts.Token);
        }
        oldCts.Cancel();
        oldCts.Dispose();
    }

    public virtual void ClearDestination()
    {
        CancellationTokenSource oldCts;
        lock (_lock)
        {
            oldCts = _cts;
            _cts = new CancellationTokenSource();
            _config = new ProxyConfig(null, _cts.Token);
        }
        oldCts.Cancel();
        oldCts.Dispose();
    }

    private class ProxyConfig : IProxyConfig
    {
        private readonly CancellationChangeToken _changeToken;

        public ProxyConfig(
            IReadOnlyDictionary<string, DestinationConfig>? destinations,
            CancellationToken cancellationToken)
        {
            _changeToken = new CancellationChangeToken(cancellationToken);

            Routes = new[]
            {
                new RouteConfig
                {
                    RouteId = "tts-route",
                    ClusterId = "oao-tts-cluster",
                    Match = new RouteMatch { Path = "/api/tts/{**catch-all}" }
                }
            };

            Clusters = new[]
            {
                new ClusterConfig
                {
                    ClusterId = "oao-tts-cluster",
                    Destinations = destinations
                }
            };
        }

        public IReadOnlyList<RouteConfig> Routes { get; }
        public IReadOnlyList<ClusterConfig> Clusters { get; }
        public IChangeToken ChangeToken => _changeToken;
    }
}
