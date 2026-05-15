using oao.Web.Data.Entities;

namespace oao.Web.Services;

public interface IDockerOrchestratorService
{
    Task CreateAndStartModelAsync(ModelProfile profile);
    Task StopModelAsync(ModelProfile profile);
    Task RemoveModelAsync(ModelProfile profile);
    Task SwapModelAsync(ModelProfile newModel);
    Task<string> GetContainerStatusAsync(string containerId);
}
