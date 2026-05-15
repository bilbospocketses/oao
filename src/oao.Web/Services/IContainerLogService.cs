using oao.Web.Hubs;

namespace oao.Web.Services;

public interface IContainerLogService
{
    Task SubscribeAsync(string containerId, string connectionId);
    Task UnsubscribeAsync(string containerId, string connectionId);
    Task UnsubscribeAllAsync(string connectionId);
    bool HasSubscribers(string containerId);

    void SubscribeCallback(string containerId, string subscriberId, Action<LogLineEvent> callback);
    void UnsubscribeCallback(string containerId, string subscriberId);
    void UnsubscribeAllCallbacks(string subscriberId);
}
