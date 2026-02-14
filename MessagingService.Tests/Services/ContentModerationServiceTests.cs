using System;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using MessagingService.Services;

namespace MessagingService.Tests.Services;

public class ContentModerationServiceTests
{
    private readonly Mock<IPersonalInfoDetectionService> _personalInfoMock;
    private readonly ContentModerationService _service;

    public ContentModerationServiceTests()
    {
        _personalInfoMock = new Mock<IPersonalInfoDetectionService>();
        _personalInfoMock
            .Setup(x => x.DetectPersonalInfoAsync(It.IsAny<string>()))
            .ReturnsAsync(new PersonalInfoResult { HasPersonalInfo = false });

        _service = new ContentModerationService(
            _personalInfoMock.Object,
            Mock.Of<ILogger<ContentModerationService>>());
    }

    [Fact]
    public async Task EmptyContent_Rejected()
    {
        var result = await _service.ModerateContentAsync("");
        Assert.False(result.IsApproved);
        Assert.Contains("Empty", result.Reason!);
    }

    [Fact]
    public async Task WhitespaceContent_Rejected()
    {
        var result = await _service.ModerateContentAsync("   ");
        Assert.False(result.IsApproved);
    }

    [Fact]
    public async Task NullContent_Rejected()
    {
        var result = await _service.ModerateContentAsync(null!);
        Assert.False(result.IsApproved);
    }

    [Fact]
    public async Task CleanMessage_Approved()
    {
        var result = await _service.ModerateContentAsync("Hello, how are you doing today?");
        Assert.True(result.IsApproved);
    }

    [Fact]
    public async Task ProhibitedWord_Rejected()
    {
        var result = await _service.ModerateContentAsync("you are such a slut");
        Assert.False(result.IsApproved);
        Assert.Contains("Inappropriate", result.Reason!);
    }

    [Fact]
    public async Task SocialMediaHandle_InProhibitedList()
    {
        var result = await _service.ModerateContentAsync("add me on snapchat");
        Assert.False(result.IsApproved);
    }

    [Theory]
    [InlineData("I want to kill you")]
    [InlineData("commit suicide")]
    [InlineData("doing drugs tonight")]
    public async Task HarmfulPatterns_Rejected(string content)
    {
        var result = await _service.ModerateContentAsync(content);
        Assert.False(result.IsApproved);
        Assert.False(result.IsApproved);
    }

    [Fact]
    public async Task ExcessiveCaps_Rejected()
    {
        var result = await _service.ModerateContentAsync("WHY ARE YOU NOT RESPONDING TO ME RIGHT NOW");
        Assert.False(result.IsApproved);
        Assert.Contains("capital", result.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShortAllCaps_Allowed()
    {
        // Under 10 chars, caps rule doesn't apply
        var result = await _service.ModerateContentAsync("HI THERE");
        // Message is < 10 chars but contains prohibited words? Let's check... 
        // "hi there" has no prohibited words. Should pass caps check since length <= 10
        Assert.True(result.IsApproved);
    }

    [Fact]
    public async Task PersonalInfoDetected_Rejected()
    {
        _personalInfoMock
            .Setup(x => x.DetectPersonalInfoAsync(It.IsAny<string>()))
            .ReturnsAsync(new PersonalInfoResult { HasPersonalInfo = true, InfoType = "Phone Number" });

        var result = await _service.ModerateContentAsync("Call me at 555-123-4567");
        Assert.False(result.IsApproved);
        Assert.Contains("Personal information", result.Reason!);
    }
}
