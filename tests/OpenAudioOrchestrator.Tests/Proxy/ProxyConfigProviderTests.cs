using OpenAudioOrchestrator.Web.Proxy;
using Yarp.ReverseProxy.Configuration;

namespace OpenAudioOrchestrator.Tests.Proxy;

public class ProxyConfigProviderTests
{
    [Fact]
    public void GetConfig_ReturnsRouteForTtsEndpoint()
    {
        var provider = new ProxyConfigProvider();
        var config = provider.GetConfig();

        var route = Assert.Single(config.Routes);
        Assert.Equal("tts-route", route.RouteId);
        Assert.Equal("/api/tts/{**catch-all}", route.Match.Path);
        Assert.Equal("oao-tts-cluster", route.ClusterId);
    }

    [Fact]
    public void GetConfig_InitiallyHasNoDestination()
    {
        var provider = new ProxyConfigProvider();
        var config = provider.GetConfig();

        var cluster = Assert.Single(config.Clusters);
        Assert.Equal("oao-tts-cluster", cluster.ClusterId);
        Assert.Null(cluster.Destinations);
    }

    [Fact]
    public void UpdateDestination_ChangesClusterDestination()
    {
        var provider = new ProxyConfigProvider();
        provider.UpdateDestination("http://172.18.0.2:8080");

        var config = provider.GetConfig();
        var cluster = Assert.Single(config.Clusters);
        Assert.NotNull(cluster.Destinations);
        Assert.True(cluster.Destinations!.ContainsKey("active-model"));
        Assert.Equal("http://172.18.0.2:8080", cluster.Destinations["active-model"].Address);
    }

    [Fact]
    public void UpdateDestination_TriggersConfigChange()
    {
        var provider = new ProxyConfigProvider();
        var initialToken = provider.GetConfig().ChangeToken;

        provider.UpdateDestination("http://localhost:9001");

        Assert.True(initialToken.HasChanged);
    }

    [Fact]
    public void ClearDestination_RemovesClusterDestination()
    {
        var provider = new ProxyConfigProvider();
        provider.UpdateDestination("http://localhost:9001");
        provider.ClearDestination();

        var config = provider.GetConfig();
        var cluster = Assert.Single(config.Clusters);
        Assert.Null(cluster.Destinations);
    }
}
