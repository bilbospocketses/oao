using oao.Web.Data.Entities;

namespace oao.Web.Services;

public interface ITotpService
{
    Task<(string ManualKey, string QrDataUri)> GenerateSetupInfoAsync(AppUser user, string issuer);
    Task<bool> VerifyCodeAsync(AppUser user, string code);
}
