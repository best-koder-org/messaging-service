using MessagingService.Models;

namespace MessagingService.Services;

public interface IReportingService
{
    Task ReportUserAsync(string reporterId, string reportedUserId, string reason);
    Task ReportMessageAsync(string reporterId, int messageId, string reason);
    Task<List<UserReport>> GetUserReportsAsync(string userId);
    Task<bool> IsUserBannedAsync(string userId);
}

public class ReportingService : IReportingService
{
    private readonly ILogger<ReportingService> _logger;
    private readonly Dictionary<string, List<UserReport>> _userReports = new();
    private readonly Dictionary<string, DateTime> _bannedUsers = new();
    private readonly object _lock = new();

    private const int MaxReportsBeforeBan = 5;
    private const int BanDurationHours = 24;

    public ReportingService(ILogger<ReportingService> logger)
    {
        _logger = logger;
    }

    public async Task ReportUserAsync(string reporterId, string reportedUserId, string reason)
    {
        lock (_lock)
        {
            if (!_userReports.ContainsKey(reportedUserId))
            {
                _userReports[reportedUserId] = new List<UserReport>();
            }

            var report = new UserReport
            {
                ReporterId = reporterId,
                ReportedUserId = reportedUserId,
                Reason = reason,
                ReportedAt = DateTime.UtcNow
            };

            _userReports[reportedUserId].Add(report);

            _logger.LogWarning($"User {reportedUserId} reported by {reporterId}: {reason}");

            // Check if user should be banned
            if (_userReports[reportedUserId].Count >= MaxReportsBeforeBan)
            {
                _bannedUsers[reportedUserId] = DateTime.UtcNow.AddHours(BanDurationHours);
                _logger.LogWarning($"User {reportedUserId} has been banned due to multiple reports");
            }
        }
    }

    public async Task ReportMessageAsync(string reporterId, int messageId, string reason)
    {
        _logger.LogWarning($"Message {messageId} reported by {reporterId}: {reason}");
        // In a real implementation, this would flag the message in the database
        // and potentially escalate to human moderators
    }

    public async Task<List<UserReport>> GetUserReportsAsync(string userId)
    {
        lock (_lock)
        {
            return _userReports.GetValueOrDefault(userId, new List<UserReport>());
        }
    }

    public async Task<bool> IsUserBannedAsync(string userId)
    {
        lock (_lock)
        {
            if (_bannedUsers.ContainsKey(userId))
            {
                var banExpiry = _bannedUsers[userId];
                if (DateTime.UtcNow < banExpiry)
                {
                    return true;
                }
                else
                {
                    // Ban has expired, remove from banned list
                    _bannedUsers.Remove(userId);
                }
            }
            return false;
        }
    }
}

public class UserReport
{
    public string ReporterId { get; set; } = string.Empty;
    public string ReportedUserId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime ReportedAt { get; set; }
}
