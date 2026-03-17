using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DatingApp.Llm;
using MessagingService.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MessagingService.Tests.Services;

public class SafetyAgentServiceTests
{
    private readonly Mock<ILlmProvider> _mockProvider;
    private readonly Mock<IContentModerationService> _mockStaticFilter;
    private readonly SafetyAgentService _sut;

    public SafetyAgentServiceTests()
    {
        _mockProvider = new Mock<ILlmProvider>();
        _mockProvider.Setup(p => p.ProviderName).Returns("test");
        _mockProvider.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var options = Options.Create(new LlmOptions { PrimaryProvider = "test" });
        var router = new LlmRouter(
            new[] { _mockProvider.Object },
            options,
            NullLogger<LlmRouter>.Instance);

        _mockStaticFilter = new Mock<IContentModerationService>();

        _sut = new SafetyAgentService(
            router,
            _mockStaticFilter.Object,
            NullLogger<SafetyAgentService>.Instance);
    }

    [Fact]
    public async Task ClassifyAsync_SafeMessage_ReturnsSafe()
    {
        _mockProvider.Setup(p => p.GenerateAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse
            {
                Success = true,
                Content = """{"level":"safe","reason":"casual greeting","confidence":0.99}""",
                Provider = "test",
                TokensUsed = 20,
                LatencyMs = 150
            });

        var result = await _sut.ClassifyAsync("Hej! Hur mår du?");

        Assert.Equal(SafetyLevel.Safe, result.Level);
        Assert.True(result.Confidence > 0.9);
    }

    [Fact]
    public async Task ClassifyAsync_WarningMessage_ReturnsWarning()
    {
        _mockProvider.Setup(p => p.GenerateAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse
            {
                Success = true,
                Content = """{"level":"warning","reason":"requesting personal info","confidence":0.85}""",
                Provider = "test",
                TokensUsed = 25,
                LatencyMs = 200
            });

        var result = await _sut.ClassifyAsync("Skicka ditt nummer");

        Assert.Equal(SafetyLevel.Warning, result.Level);
        Assert.Equal("requesting personal info", result.Reason);
    }

    [Fact]
    public async Task ClassifyAsync_BlockMessage_ReturnsBlock()
    {
        _mockProvider.Setup(p => p.GenerateAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse
            {
                Success = true,
                Content = """{"level":"block","reason":"harassment","confidence":0.95}""",
                Provider = "test",
                TokensUsed = 22,
                LatencyMs = 180
            });

        var result = await _sut.ClassifyAsync("Du är ful och ingen vill ha dig");

        Assert.Equal(SafetyLevel.Block, result.Level);
        Assert.Equal("harassment", result.Reason);
    }

    [Fact]
    public async Task ClassifyAsync_LlmFailure_FallsBackToStaticFilter_Safe()
    {
        _mockProvider.Setup(p => p.GenerateAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse
            {
                Success = false,
                Provider = "test",
                Error = "service_unavailable"
            });

        _mockStaticFilter.Setup(f => f.ModerateContentAsync(It.IsAny<string>()))
            .ReturnsAsync(new ModerationResult { IsApproved = true });

        var result = await _sut.ClassifyAsync("Hello there");

        Assert.Equal(SafetyLevel.Safe, result.Level);
        _mockStaticFilter.Verify(f => f.ModerateContentAsync("Hello there"), Times.Once);
    }

    [Fact]
    public async Task ClassifyAsync_LlmFailure_StaticFilterBlocks()
    {
        _mockProvider.Setup(p => p.GenerateAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse
            {
                Success = false,
                Provider = "test",
                Error = "service_unavailable"
            });

        _mockStaticFilter.Setup(f => f.ModerateContentAsync(It.IsAny<string>()))
            .ReturnsAsync(new ModerationResult { IsApproved = false, Reason = "Inappropriate language" });

        var result = await _sut.ClassifyAsync("bad content");

        Assert.Equal(SafetyLevel.Block, result.Level);
    }

    [Fact]
    public async Task ClassifyAsync_MalformedLlmResponse_DefaultsToSafe()
    {
        _mockProvider.Setup(p => p.GenerateAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse
            {
                Success = true,
                Content = "I don't understand what you mean",
                Provider = "test",
                TokensUsed = 30,
                LatencyMs = 300
            });

        var result = await _sut.ClassifyAsync("Hej");

        Assert.Equal(SafetyLevel.Safe, result.Level);
        Assert.Equal("parse_error", result.Reason);
    }

    [Fact]
    public async Task ClassifyAsync_LlmThrowsException_FallsBackToStaticFilter()
    {
        _mockProvider.Setup(p => p.GenerateAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        _mockStaticFilter.Setup(f => f.ModerateContentAsync(It.IsAny<string>()))
            .ReturnsAsync(new ModerationResult { IsApproved = true });

        var result = await _sut.ClassifyAsync("Test message");

        Assert.Equal(SafetyLevel.Safe, result.Level);
        _mockStaticFilter.Verify(f => f.ModerateContentAsync("Test message"), Times.Once);
    }
}
