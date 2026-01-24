using Microsoft.AspNetCore.Mvc;

namespace MessagingService.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "Healthy", service = "MessagingService", timestamp = System.DateTime.UtcNow });
}
