using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace OpenAudioOrchestrator.Web.Hubs;

[Authorize]
public class OrchestratorHub : Hub
{
    private readonly IContainerLogService _logService;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public OrchestratorHub(IContainerLogService logService, IDbContextFactory<AppDbContext> dbFactory)
    {
        _logService = logService;
        _dbFactory = dbFactory;
    }

    public async Task SubscribeLogs(string containerId)
    {
        if (!ContainerIdValidator.IsValid(containerId)) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var isKnown = await db.ModelProfiles.AnyAsync(m => m.ContainerId == containerId);
        if (!isKnown) return;

        await _logService.SubscribeAsync(containerId, Context.ConnectionId);
    }

    public async Task UnsubscribeLogs(string containerId)
    {
        if (!ContainerIdValidator.IsValid(containerId)) return;

        await _logService.UnsubscribeAsync(containerId, Context.ConnectionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _logService.UnsubscribeAllAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
