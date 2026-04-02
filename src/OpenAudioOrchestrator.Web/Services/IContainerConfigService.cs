using Docker.DotNet.Models;
using OpenAudioOrchestrator.Web.Data.Entities;

namespace OpenAudioOrchestrator.Web.Services;

public interface IContainerConfigService
{
    CreateContainerParameters BuildCreateParams(ModelProfile profile);
    Task<int> AllocatePortAsync();
}
