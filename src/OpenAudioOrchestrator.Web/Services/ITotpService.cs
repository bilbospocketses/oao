using OpenAudioOrchestrator.Web.Data.Entities;

namespace OpenAudioOrchestrator.Web.Services;

public interface ITotpService
{
    Task<(string ManualKey, string QrDataUri)> GenerateSetupInfoAsync(AppUser user, string issuer);
    Task<bool> VerifyCodeAsync(AppUser user, string code);
}
