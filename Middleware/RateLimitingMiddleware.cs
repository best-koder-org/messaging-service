namespace MessagingService.Middleware;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly Dictionary<string, List<DateTime>> _requestHistory = new();
    private readonly object _lock = new();
    
    private const int MaxRequestsPerMinute = 60;

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientIp = GetClientIpAddress(context);
        
        if (!IsRequestAllowed(clientIp))
        {
            context.Response.StatusCode = 429; // Too Many Requests
            await context.Response.WriteAsync("Rate limit exceeded. Please try again later.");
            return;
        }

        await _next(context);
    }

    private bool IsRequestAllowed(string clientIp)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            
            if (!_requestHistory.ContainsKey(clientIp))
            {
                _requestHistory[clientIp] = new List<DateTime>();
            }

            var requests = _requestHistory[clientIp];
            
            // Remove requests older than 1 minute
            requests.RemoveAll(time => (now - time).TotalMinutes > 1);
            
            if (requests.Count >= MaxRequestsPerMinute)
            {
                _logger.LogWarning($"Rate limit exceeded for IP: {clientIp}");
                return false;
            }
            
            requests.Add(now);
            return true;
        }
    }

    private string GetClientIpAddress(HttpContext context)
    {
        // Check for X-Forwarded-For header first (for reverse proxy scenarios)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        // Check for X-Real-IP header
        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        // Fall back to remote IP address
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
