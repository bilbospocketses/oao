using Docker.DotNet.Models;
using oao.Web.Data.Entities;

namespace oao.Web.Services;

public interface IContainerConfigService
{
    CreateContainerParameters BuildCreateParams(ModelProfile profile);
    Task<int> AllocatePortAsync();
}
