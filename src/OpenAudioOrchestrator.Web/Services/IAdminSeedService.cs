namespace OpenAudioOrchestrator.Web.Services;

public interface IAdminSeedService
{
    Task SeedIfConfiguredAsync();
}
