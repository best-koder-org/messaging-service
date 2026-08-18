using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using MessagingService.Services;

namespace MessagingService.Tests.Services;

public class UserIdentityResolverTests
{
    private readonly UserIdentityResolver _resolver;

    public UserIdentityResolverTests()
    {
        var httpFactory = new Mock<IHttpClientFactory>();
        // Base URL empty -> any relative request fails fast without network.
        httpFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient());

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string>
            {
                ["Services:SwipeService:BaseUrl"] = string.Empty
            })
            .Build();

        _resolver = new UserIdentityResolver(
            httpFactory.Object,
            config,
            Mock.Of<ILogger<UserIdentityResolver>>());
    }

    [Fact]
    public async Task ResolveKeycloakId_Uuid_PassesThroughUnchanged()
    {
        const string kcId = "905e1412-dfd3-4b97-a3e4-f755ed696384";
        var result = await _resolver.ResolveKeycloakIdAsync(kcId);
        Assert.Equal(kcId, result);
    }

    [Fact]
    public async Task ResolveKeycloakId_ProfileIdWithMapping_ReturnsKeycloakId()
    {
        _resolver.SeedMappingForTest(16, "905e1412-dfd3-4b97-a3e4-f755ed696384");
        var result = await _resolver.ResolveKeycloakIdAsync("16");
        Assert.Equal("905e1412-dfd3-4b97-a3e4-f755ed696384", result);
    }

    [Fact]
    public async Task ResolveKeycloakId_ProfileIdWithoutMapping_FallsBackToInput()
    {
        _resolver.ClearCacheForTest();
        var result = await _resolver.ResolveKeycloakIdAsync("99");
        Assert.Equal("99", result);
    }

    [Fact]
    public async Task ResolveKeycloakId_EmptyOrNull_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, await _resolver.ResolveKeycloakIdAsync(string.Empty));
        Assert.Equal(string.Empty, await _resolver.ResolveKeycloakIdAsync(null!));
    }
}
