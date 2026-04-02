using System.Text.RegularExpressions;

namespace OpenAudioOrchestrator.Web.Services;

public static partial class ContainerIdValidator
{
    [GeneratedRegex(@"^[a-f0-9]{12,64}$")]
    private static partial Regex ValidContainerIdRegex();

    public static bool IsValid(string? containerId)
        => containerId is not null && ValidContainerIdRegex().IsMatch(containerId);
}
