using Xunit;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using MessagingService.Services;

namespace MessagingService.Tests.Services;

public class RateLimitingServiceTests
{
    private RateLimitingService CreateService() =>
        new(Mock.Of<ILogger<RateLimitingService>>());

    [Fact]
    public async Task FirstRequest_Allowed()
    {
        var svc = CreateService();
        Assert.True(await svc.IsAllowedAsync("user1"));
    }

    [Fact]
    public async Task Under20Requests_AllAllowed()
    {
        var svc = CreateService();
        for (int i = 0; i < 19; i++)
            Assert.True(await svc.IsAllowedAsync("user1"));
    }

    [Fact]
    public async Task At20Requests_Blocked()
    {
        var svc = CreateService();
        for (int i = 0; i < 20; i++)
            await svc.IsAllowedAsync("user1");

        Assert.False(await svc.IsAllowedAsync("user1"));
    }

    [Fact]
    public async Task DifferentUsers_IndependentLimits()
    {
        var svc = CreateService();
        for (int i = 0; i < 20; i++)
            await svc.IsAllowedAsync("user1");

        // user1 blocked, user2 still fine
        Assert.False(await svc.IsAllowedAsync("user1"));
        Assert.True(await svc.IsAllowedAsync("user2"));
    }
}
