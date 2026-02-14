using Xunit;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using MessagingService.Services;

namespace MessagingService.Tests.Services;

public class ReportingServiceTests
{
    private ReportingService CreateService() =>
        new(Mock.Of<ILogger<ReportingService>>());

    [Fact]
    public async Task ReportUser_StoresReport()
    {
        var svc = CreateService();
        await svc.ReportUserAsync("reporter1", "offender1", "harassment");
        var reports = await svc.GetUserReportsAsync("offender1");
        Assert.Single(reports);
        Assert.Equal("reporter1", reports[0].ReporterId);
        Assert.Equal("offender1", reports[0].ReportedUserId);
        Assert.Equal("harassment", reports[0].Reason);
    }

    [Fact]
    public async Task NoReports_EmptyList()
    {
        var svc = CreateService();
        var reports = await svc.GetUserReportsAsync("nobody");
        Assert.Empty(reports);
    }

    [Fact]
    public async Task FiveReports_TriggersBan()
    {
        var svc = CreateService();
        for (int i = 0; i < 5; i++)
            await svc.ReportUserAsync($"reporter{i}", "baduser", "spam");

        Assert.True(await svc.IsUserBannedAsync("baduser"));
    }

    [Fact]
    public async Task FourReports_NotBanned()
    {
        var svc = CreateService();
        for (int i = 0; i < 4; i++)
            await svc.ReportUserAsync($"reporter{i}", "user1", "spam");

        Assert.False(await svc.IsUserBannedAsync("user1"));
    }

    [Fact]
    public async Task UnreportedUser_NotBanned()
    {
        var svc = CreateService();
        Assert.False(await svc.IsUserBannedAsync("cleanuser"));
    }

    [Fact]
    public async Task ReportMessage_DoesNotCrash()
    {
        var svc = CreateService();
        // Just verify it completes without exception
        await svc.ReportMessageAsync("reporter1", 42, "inappropriate");
    }

    [Fact]
    public async Task MultipleReports_Accumulate()
    {
        var svc = CreateService();
        await svc.ReportUserAsync("r1", "target", "spam");
        await svc.ReportUserAsync("r2", "target", "harassment");
        await svc.ReportUserAsync("r3", "target", "scam");

        var reports = await svc.GetUserReportsAsync("target");
        Assert.Equal(3, reports.Count);
    }
}
