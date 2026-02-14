using Xunit;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using MessagingService.Services;

namespace MessagingService.Tests.Services;

public class SpamDetectionServiceTests
{
    private SpamDetectionService CreateService() =>
        new(Mock.Of<ILogger<SpamDetectionService>>());

    [Fact]
    public async Task FirstMessage_NotSpam()
    {
        var svc = CreateService();
        Assert.False(await svc.IsSpamAsync("user1", "Hello there!"));
    }

    [Fact]
    public async Task UnderMinuteLimit_NotSpam()
    {
        var svc = CreateService();
        for (int i = 0; i < 9; i++)
            await svc.IsSpamAsync("user1", $"Message {i}");

        Assert.False(await svc.IsSpamAsync("user1", "Still fine"));
    }

    [Fact]
    public async Task AtMinuteLimit_IsSpam()
    {
        var svc = CreateService();
        // Send exactly 10 messages (the limit)
        for (int i = 0; i < 10; i++)
            await svc.IsSpamAsync("user1", $"Msg {i}");

        // 11th should be flagged
        Assert.True(await svc.IsSpamAsync("user1", "Too many!"));
    }

    [Fact]
    public async Task RepeatedContent_ThirdTimeIsSpam()
    {
        var svc = CreateService();
        Assert.False(await svc.IsSpamAsync("user1", "buy now"));
        Assert.False(await svc.IsSpamAsync("user1", "buy now"));
        Assert.True(await svc.IsSpamAsync("user1", "buy now"));
    }

    [Fact]
    public async Task RepeatedContent_CaseInsensitive()
    {
        var svc = CreateService();
        Assert.False(await svc.IsSpamAsync("user1", "Hello World"));
        Assert.False(await svc.IsSpamAsync("user1", "hello world"));
        Assert.True(await svc.IsSpamAsync("user1", "HELLO WORLD"));
    }

    [Fact]
    public async Task DifferentUsers_IndependentLimits()
    {
        var svc = CreateService();
        for (int i = 0; i < 10; i++)
            await svc.IsSpamAsync("user1", $"Msg {i}");

        // user1 is at limit, user2 is fine
        Assert.True(await svc.IsSpamAsync("user1", "over limit"));
        Assert.False(await svc.IsSpamAsync("user2", "no problem"));
    }

    [Fact]
    public async Task DifferentContent_NotSpam()
    {
        var svc = CreateService();
        Assert.False(await svc.IsSpamAsync("user1", "Hello"));
        Assert.False(await svc.IsSpamAsync("user1", "How are you?"));
        Assert.False(await svc.IsSpamAsync("user1", "Nice to meet you"));
    }
}
