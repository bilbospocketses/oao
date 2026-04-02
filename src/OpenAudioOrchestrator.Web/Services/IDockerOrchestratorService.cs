using OpenAudioOrchestrator.Web.Data.Entities;

namespace OpenAudioOrchestrator.Web.Services;

public interface IDockerOrchestratorService
{
    Task CreateAndStartModelAsync(ModelProfile profile);
    Task StopModelAsync(ModelProfile profile);
    Task RemoveModelAsync(ModelProfile profile);
    Task SwapModelAsync(ModelProfile newModel);
    Task<string> GetContainerStatusAsync(string containerId);
}
