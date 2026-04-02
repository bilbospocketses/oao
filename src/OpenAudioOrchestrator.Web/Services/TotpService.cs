using OpenAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using QRCoder;

namespace OpenAudioOrchestrator.Web.Services;

public class TotpService : ITotpService
{
    private readonly UserManager<AppUser> _userManager;

    public TotpService(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<(string ManualKey, string QrDataUri)> GenerateSetupInfoAsync(AppUser user, string issuer)
    {
        await _userManager.ResetAuthenticatorKeyAsync(user);
        var key = await _userManager.GetAuthenticatorKeyAsync(user);

        var uri = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(user.UserName!)}?secret={key}&issuer={Uri.EscapeDataString(issuer)}&digits=6";

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        var pngBytes = qrCode.GetGraphic(5);
        var dataUri = $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";

        return (key!, dataUri);
    }

    public async Task<bool> VerifyCodeAsync(AppUser user, string code)
    {
        return await _userManager.VerifyTwoFactorTokenAsync(user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider, code);
    }
}
