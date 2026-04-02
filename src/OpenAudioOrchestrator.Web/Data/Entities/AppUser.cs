using Microsoft.AspNetCore.Identity;

namespace OpenAudioOrchestrator.Web.Data.Entities;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
    public bool MustSetupTotp { get; set; }
    public string ThemePreference { get; set; } = "dark";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
