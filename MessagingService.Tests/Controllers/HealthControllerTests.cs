using System;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using MessagingService.Controllers;

namespace MessagingService.Tests.Controllers;

public class HealthControllerTests
{
    [Fact]
    public void Get_ReturnsOk_WithHealthyStatus()
    {
        // Arrange
        var controller = new HealthController();

        // Act
        var result = controller.Get();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        Assert.Contains("Healthy", okResult.Value.ToString()!);
    }
}
