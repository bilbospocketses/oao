using System.Text.RegularExpressions;

namespace OpenAudioOrchestrator.Web.Services;

/// <summary>
/// Static validation helpers used during setup configuration.
/// </summary>
public static class SetupValidation
{
    public static bool IsValidDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        return Regex.IsMatch(domain, @"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)+$");
    }

    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    }

    public static bool IsValidPort(int port)
    {
        return port >= 1024 && port <= 65535;
    }
}
