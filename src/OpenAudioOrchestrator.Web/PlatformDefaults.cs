namespace OpenAudioOrchestrator.Web;

public static class PlatformDefaults
{
    public static string DataRoot =>
        OperatingSystem.IsWindows() ? @"C:\MyOpenAudioProj" : "/opt/OpenAudioOrchestrator";

    public static string DbPath =>
        Path.Combine(DataRoot, "AudioOrchestrator.db");

    public static string DockerEndpoint =>
        OperatingSystem.IsWindows()
            ? "npipe://./pipe/docker_engine"
            : "unix:///var/run/docker.sock";

    public static string GitInstallHint =>
        OperatingSystem.IsWindows()
            ? "Git is not installed. Install it from PowerShell:\nwinget install Git.Git\nThen click Retry."
            : "Git is not installed. Install it with your package manager:\n  Debian/Ubuntu: sudo apt install git\n  RHEL/Fedora: sudo dnf install git\n  Alpine: sudo apk add git\nThen click Retry.";

    public static string GitLfsInstallHint =>
        OperatingSystem.IsWindows()
            ? "Git LFS is not installed. Run the following in PowerShell:\ngit lfs install\nThen click Retry."
            : "Git LFS is not installed. Install it with your package manager:\n  Debian/Ubuntu: sudo apt install git-lfs && git lfs install\n  RHEL/Fedora: sudo dnf install git-lfs && git lfs install\n  Alpine: sudo apk add git-lfs && git lfs install\nThen click Retry.";

    /// <summary>
    /// Returns the config value if non-empty, otherwise returns the platform default.
    /// Use this instead of ?? when reading from IConfiguration, since empty strings
    /// are returned as "" (not null) and won't trigger the null-coalescing operator.
    /// </summary>
    public static string ConfigValueOrDefault(string? configValue, string defaultValue) =>
        string.IsNullOrWhiteSpace(configValue) ? defaultValue : configValue;
}
