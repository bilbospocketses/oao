using oao.Web.Data.Entities;

namespace oao.Web.Services;

public interface IVoiceLibraryService
{
    Task AddVoiceAsync(string displayName, string voiceId, Stream audioFile, string transcript, string? tags = null);
    Task UpdateVoiceAsync(int id, string displayName, string transcriptText, string? tags);
    Task DeleteVoiceAsync(int id);
    Task<List<ReferenceVoice>> ListVoicesAsync();
    Task SyncVoicesToContainerAsync(string containerBaseUrl);
}
