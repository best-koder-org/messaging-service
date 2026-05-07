using MessagingService.Controllers;
using MessagingService.Data;
using MessagingService.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Threading.Tasks;
using Xunit;

namespace MessagingService.Tests.Controllers;

public class AdminControllerTests : IDisposable
{
    private readonly MessagingDbContext _context;

    public AdminControllerTests()
    {
        var options = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseInMemoryDatabase($"AdminReset_{Guid.NewGuid()}")
            .Options;
        _context = new MessagingDbContext(options);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private AdminController BuildController(string envName = "Development")
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(envName);
        var logger = Mock.Of<ILogger<AdminController>>();
        var ctrl = new AdminController(_context, env.Object, logger);
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return ctrl;
    }

    [Fact]
    public async Task ResetAllMessages_WipesEverythingInDev()
    {
        _context.Messages.Add(new Message { SenderId = "a", ReceiverId = "b", Content = "hi", SentAt = DateTime.UtcNow });
        _context.Messages.Add(new Message { SenderId = "b", ReceiverId = "a", Content = "yo", SentAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        var result = await BuildController("Development").ResetAllMessages();

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(0, await _context.Messages.CountAsync());
    }

    [Fact]
    public async Task ResetAllMessages_RejectsInProduction()
    {
        var result = await BuildController("Production").ResetAllMessages();
        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
    }
}
