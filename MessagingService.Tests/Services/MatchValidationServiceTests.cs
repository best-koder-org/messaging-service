using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;
using MessagingService.Services;

namespace MessagingService.Tests.Services;

public class MatchValidationServiceTests
{
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly IConfiguration _configuration;
    private readonly MatchValidationService _service;

    public MatchValidationServiceTests()
    {
        _httpHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(_httpHandlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:8087")
        };

        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _httpClientFactoryMock.Setup(f => f.CreateClient("SwipeService"))
            .Returns(httpClient);

        var configData = new Dictionary<string, string?>
        {
            { "Services:SwipeService:BaseUrl", "http://localhost:8087" }
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        _service = new MatchValidationService(
            _httpClientFactoryMock.Object,
            _configuration,
            Mock.Of<ILogger<MatchValidationService>>());
    }

    private void SetupResponse(HttpStatusCode statusCode, object? body = null)
    {
        var content = body != null
            ? new StringContent(JsonSerializer.Serialize(body))
            : new StringContent("");

        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = content
            });
    }

    // ===== Matched Users =====

    [Fact]
    public async Task AreUsersMatched_HasMatchTrue_ReturnsTrue()
    {
        SetupResponse(HttpStatusCode.OK, new { hasMatch = true, reason = "Users are matched" });

        var result = await _service.AreUsersMatchedAsync("user1", "user2");

        Assert.True(result);
    }

    [Fact]
    public async Task AreUsersMatched_HasMatchFalse_ReturnsFalse()
    {
        SetupResponse(HttpStatusCode.OK, new { hasMatch = false, reason = "No match" });

        var result = await _service.AreUsersMatchedAsync("user1", "user2");

        Assert.False(result);
    }

    // ===== Fail-Closed =====

    [Fact]
    public async Task AreUsersMatched_HttpError_ReturnsFalse()
    {
        SetupResponse(HttpStatusCode.InternalServerError);

        var result = await _service.AreUsersMatchedAsync("user1", "user2");

        Assert.False(result);
    }

    [Fact]
    public async Task AreUsersMatched_NetworkException_ReturnsFalse()
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _service.AreUsersMatchedAsync("user1", "user2");

        Assert.False(result);
    }

    [Fact]
    public async Task AreUsersMatched_Timeout_ReturnsFalse()
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("Request timed out"));

        var result = await _service.AreUsersMatchedAsync("user1", "user2");

        Assert.False(result);
    }

    [Fact]
    public async Task AreUsersMatched_MalformedJson_ReturnsFalse()
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("not valid json {{{")
            });

        var result = await _service.AreUsersMatchedAsync("user1", "user2");

        Assert.False(result);
    }

    [Fact]
    public async Task AreUsersMatched_NullBody_ReturnsFalse()
    {
        SetupResponse(HttpStatusCode.OK, null);

        var result = await _service.AreUsersMatchedAsync("user1", "user2");

        Assert.False(result);
    }

    [Fact]
    public async Task AreUsersMatched_NotFound_ReturnsFalse()
    {
        SetupResponse(HttpStatusCode.NotFound);

        var result = await _service.AreUsersMatchedAsync("user1", "user2");

        Assert.False(result);
    }
}
