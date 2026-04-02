using Docker.DotNet;
using Docker.DotNet.Models;
using OpenAudioOrchestrator.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace OpenAudioOrchestrator.Tests.Services;

public class DockerNetworkServiceTests
{
    private static IConfiguration CreateConfig(string networkName = "oao-network")
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAudioOrchestrator:DockerNetworkName"] = networkName
            })
            .Build();
    }

    [Fact]
    public async Task EnsureNetworkExistsAsync_CreatesNetworkWhenMissing()
    {
        var mockDocker = new Mock<IDockerClient>();
        var mockNetworks = new Mock<INetworkOperations>();

        mockNetworks.Setup(n => n.ListNetworksAsync(
                It.IsAny<NetworksListParameters>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NetworkResponse>());

        mockNetworks.Setup(n => n.CreateNetworkAsync(
                It.IsAny<NetworksCreateParameters>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetworksCreateResponse { ID = "net-123" });

        mockDocker.Setup(d => d.Networks).Returns(mockNetworks.Object);

        var service = new DockerNetworkService(mockDocker.Object, CreateConfig(), NullLogger<DockerNetworkService>.Instance);
        var networkId = await service.EnsureNetworkExistsAsync();

        Assert.Equal("net-123", networkId);
        mockNetworks.Verify(n => n.CreateNetworkAsync(
            It.Is<NetworksCreateParameters>(p =>
                p.Name == "oao-network" && p.Driver == "bridge"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureNetworkExistsAsync_ReusesExistingNetwork()
    {
        var mockDocker = new Mock<IDockerClient>();
        var mockNetworks = new Mock<INetworkOperations>();

        mockNetworks.Setup(n => n.ListNetworksAsync(
                It.IsAny<NetworksListParameters>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NetworkResponse>
            {
                new NetworkResponse { ID = "existing-net", Name = "oao-network" }
            });

        mockDocker.Setup(d => d.Networks).Returns(mockNetworks.Object);

        var service = new DockerNetworkService(mockDocker.Object, CreateConfig(), NullLogger<DockerNetworkService>.Instance);
        var networkId = await service.EnsureNetworkExistsAsync();

        Assert.Equal("existing-net", networkId);
        mockNetworks.Verify(n => n.CreateNetworkAsync(
            It.IsAny<NetworksCreateParameters>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetContainerIpAsync_ReturnsIpFromNetwork()
    {
        var mockDocker = new Mock<IDockerClient>();
        var mockContainers = new Mock<IContainerOperations>();

        mockContainers.Setup(c => c.InspectContainerAsync(
                "c0afe1234567", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInspectResponse
            {
                NetworkSettings = new NetworkSettings
                {
                    Networks = new Dictionary<string, EndpointSettings>
                    {
                        ["oao-network"] = new EndpointSettings
                        {
                            IPAddress = "172.18.0.5"
                        }
                    }
                }
            });

        mockDocker.Setup(d => d.Containers).Returns(mockContainers.Object);

        var service = new DockerNetworkService(mockDocker.Object, CreateConfig(), NullLogger<DockerNetworkService>.Instance);
        var ip = await service.GetContainerIpAsync("c0afe1234567");

        Assert.Equal("172.18.0.5", ip);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-valid-id")]
    [InlineData("ABCDEF1234567890")]  // uppercase not allowed
    [InlineData("short")]             // too short (< 12 hex chars)
    public async Task GetContainerIpAsync_InvalidContainerId_ReturnsNullWithoutCallingDocker(string badId)
    {
        var mockDocker = new Mock<IDockerClient>();
        var mockContainers = new Mock<IContainerOperations>();
        mockDocker.Setup(d => d.Containers).Returns(mockContainers.Object);

        var service = new DockerNetworkService(mockDocker.Object, CreateConfig(), NullLogger<DockerNetworkService>.Instance);
        var ip = await service.GetContainerIpAsync(badId);

        Assert.Null(ip);
        mockContainers.Verify(c => c.InspectContainerAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
