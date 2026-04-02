using OpenAudioOrchestrator.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenAudioOrchestrator.Tests.Services;

public class SetupServiceTests
{
    private static SetupDownloadService CreateDownloadService()
    {
        return new SetupDownloadService(NullLogger<SetupDownloadService>.Instance);
    }

    // --- IsValidDomain ---

    [Theory]
    [InlineData("example.com")]
    [InlineData("sub.domain.co.uk")]
    [InlineData("a-b.com")]
    public void IsValidDomain_ReturnsTrueForValidDomains(string domain)
    {
        Assert.True(SetupValidation.IsValidDomain(domain));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("-bad.com")]
    [InlineData("no_underscores.com")]
    [InlineData(".leading-dot.com")]
    [InlineData("trailing-dot.")]
    [InlineData("spaces in domain")]
    [InlineData("localhost")]
    public void IsValidDomain_ReturnsFalseForInvalidDomains(string? domain)
    {
        Assert.False(SetupValidation.IsValidDomain(domain!));
    }

    // --- IsValidEmail ---

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("a@b.co")]
    public void IsValidEmail_ReturnsTrueForValidEmails(string email)
    {
        Assert.True(SetupValidation.IsValidEmail(email));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("no-at-sign")]
    [InlineData("@no-local")]
    [InlineData("no-domain@")]
    [InlineData("spaces @in.email")]
    public void IsValidEmail_ReturnsFalseForInvalidEmails(string? email)
    {
        Assert.False(SetupValidation.IsValidEmail(email!));
    }

    // --- IsValidPort ---

    [Theory]
    [InlineData(1024)]
    [InlineData(8080)]
    [InlineData(65535)]
    public void IsValidPort_ReturnsTrueForValidPorts(int port)
    {
        Assert.True(SetupValidation.IsValidPort(port));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void IsValidPort_ReturnsFalseForInvalidPorts(int port)
    {
        Assert.False(SetupValidation.IsValidPort(port));
    }

    // --- IsModelPresent ---

    [Fact]
    public void IsModelPresent_ReturnsTrueWhenModelExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"setup-test-{Guid.NewGuid()}");
        var s2ProDir = Path.Combine(tempDir, "s2-pro");
        try
        {
            Directory.CreateDirectory(s2ProDir);
            File.WriteAllText(Path.Combine(s2ProDir, "model.bin"), "fake");

            var service = CreateDownloadService();
            Assert.True(service.IsModelPresent(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void IsModelPresent_ReturnsFalseWhenDirectoryMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"setup-test-{Guid.NewGuid()}");
        var service = CreateDownloadService();

        Assert.False(service.IsModelPresent(tempDir));
    }

    [Fact]
    public void IsModelPresent_ReturnsFalseWhenDirectoryEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"setup-test-{Guid.NewGuid()}");
        var s2ProDir = Path.Combine(tempDir, "s2-pro");
        try
        {
            Directory.CreateDirectory(s2ProDir);

            var service = CreateDownloadService();
            Assert.False(service.IsModelPresent(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // --- Default state ---

    [Fact]
    public void HasActiveDownloads_ReturnsFalse_WhenNoDownloadsStarted()
    {
        var service = CreateDownloadService();
        Assert.False(service.HasActiveDownloads);
    }

    [Fact]
    public void ModelDownloadCompleted_DefaultsFalse()
    {
        var service = CreateDownloadService();
        Assert.False(service.ModelDownloadCompleted);
    }

    [Fact]
    public void DockerPullCompleted_DefaultsFalse()
    {
        var service = CreateDownloadService();
        Assert.False(service.DockerPullCompleted);
    }
}
