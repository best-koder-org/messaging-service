using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using MessagingService.Middleware;

namespace MessagingService.Tests.Middleware;

public class RateLimitingMiddlewareTests
{
    private bool _nextCalled;

    private RateLimitingMiddleware CreateMiddleware()
    {
        _nextCalled = false;
        RequestDelegate next = _ =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        };
        return new RateLimitingMiddleware(next, Mock.Of<ILogger<RateLimitingMiddleware>>());
    }

    private DefaultHttpContext CreateContext(string ip = "192.168.1.1")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return context;
    }

    // ===== Normal Traffic =====

    [Fact]
    public async Task SingleRequest_Passes()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.True(_nextCalled);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task Under60Requests_AllPass()
    {
        var middleware = CreateMiddleware();

        for (int i = 0; i < 60; i++)
        {
            _nextCalled = false;
            var context = CreateContext("10.0.0.1");
            await middleware.InvokeAsync(context);
            Assert.True(_nextCalled, $"Request {i + 1} should have passed");
        }
    }

    // ===== Rate Limit Exceeded =====

    [Fact]
    public async Task Request61_Returns429()
    {
        var middleware = CreateMiddleware();

        // Send 60 good requests
        for (int i = 0; i < 60; i++)
        {
            var ctx = CreateContext("10.0.0.2");
            await middleware.InvokeAsync(ctx);
        }

        // 61st should be rate limited
        _nextCalled = false;
        var limitedContext = CreateContext("10.0.0.2");
        await middleware.InvokeAsync(limitedContext);

        Assert.False(_nextCalled);
        Assert.Equal(429, limitedContext.Response.StatusCode);
    }

    // ===== Per-IP isolation =====

    [Fact]
    public async Task DifferentIPs_IndependentLimits()
    {
        var middleware = CreateMiddleware();

        // IP1 uses 60 requests
        for (int i = 0; i < 60; i++)
        {
            await middleware.InvokeAsync(CreateContext("10.0.0.3"));
        }

        // IP2 should still pass
        _nextCalled = false;
        var context2 = CreateContext("10.0.0.4");
        await middleware.InvokeAsync(context2);

        Assert.True(_nextCalled);
    }

    // ===== X-Forwarded-For header =====

    [Fact]
    public async Task XForwardedFor_UsedForRateLimiting()
    {
        var middleware = CreateMiddleware();

        // Send 60 requests with X-Forwarded-For
        for (int i = 0; i < 60; i++)
        {
            var ctx = CreateContext("127.0.0.1");
            ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.50";
            await middleware.InvokeAsync(ctx);
        }

        // 61st from same forwarded IP should be limited
        _nextCalled = false;
        var limitedCtx = CreateContext("127.0.0.1");
        limitedCtx.Request.Headers["X-Forwarded-For"] = "203.0.113.50";
        await middleware.InvokeAsync(limitedCtx);

        Assert.False(_nextCalled);
        Assert.Equal(429, limitedCtx.Response.StatusCode);
    }

    [Fact]
    public async Task XForwardedFor_MultipleIPs_UsesFirst()
    {
        var middleware = CreateMiddleware();

        // X-Forwarded-For with multiple IPs: "client, proxy1, proxy2"
        for (int i = 0; i < 60; i++)
        {
            var ctx = CreateContext("127.0.0.1");
            ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.60, 10.0.0.1, 10.0.0.2";
            await middleware.InvokeAsync(ctx);
        }

        // This uses a DIFFERENT first IP so should pass
        _nextCalled = false;
        var freshCtx = CreateContext("127.0.0.1");
        freshCtx.Request.Headers["X-Forwarded-For"] = "203.0.113.70, 10.0.0.1, 10.0.0.2";
        await middleware.InvokeAsync(freshCtx);

        Assert.True(_nextCalled);
    }

    // ===== X-Real-IP header =====

    [Fact]
    public async Task XRealIP_UsedWhenNoForwardedFor()
    {
        var middleware = CreateMiddleware();

        for (int i = 0; i < 60; i++)
        {
            var ctx = CreateContext("127.0.0.1");
            ctx.Request.Headers["X-Real-IP"] = "203.0.113.80";
            await middleware.InvokeAsync(ctx);
        }

        _nextCalled = false;
        var limitedCtx = CreateContext("127.0.0.1");
        limitedCtx.Request.Headers["X-Real-IP"] = "203.0.113.80";
        await middleware.InvokeAsync(limitedCtx);

        Assert.Equal(429, limitedCtx.Response.StatusCode);
    }
}
