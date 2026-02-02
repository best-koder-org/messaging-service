using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MessagingService.Common;

public class InternalApiKeyAuthFilter : IAuthorizationFilter
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<InternalApiKeyAuthFilter> _logger;

    public InternalApiKeyAuthFilter(IConfiguration configuration, ILogger<InternalApiKeyAuthFilter> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var validApiKeys = _configuration["InternalAuth:ValidApiKeys"]?.Split(',') ?? Array.Empty<string>();
        
        if (validApiKeys.Length == 0)
        {
            _logger.LogWarning("No valid internal API keys configured - allowing request (DEV mode)");
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue("X-Internal-API-Key", out var receivedKey))
        {
            _logger.LogWarning("Internal API call missing X-Internal-API-Key header");
            context.Result = new UnauthorizedObjectResult(new { error = "Missing internal API key" });
            return;
        }

        if (!validApiKeys.Contains(receivedKey.ToString()))
        {
            _logger.LogWarning("Invalid internal API key received");
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid internal API key" });
            return;
        }

        _logger.LogDebug("Internal API key validated successfully");
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireInternalApiKeyAttribute : ServiceFilterAttribute
{
    public RequireInternalApiKeyAttribute() : base(typeof(InternalApiKeyAuthFilter))
    {
    }
}
