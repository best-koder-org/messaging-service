using System.Text;
using System.Text.Json;
using MessagingService.Data;
using MessagingService.Models;
using Microsoft.EntityFrameworkCore;

namespace MessagingService.Services;

/// <summary>
/// Background service that detects ghosting behavior — users who engage in
/// mutual conversation then stop replying. Penalizes via reputation-service.
///
/// Criteria for a "ghost" incident:
///   1. Both users in the conversation have sent ≥3 messages (mutual engagement)
///   2. The last message in the conversation was sent ≥3 days ago
///   3. The receiver of the last message has not sent any subsequent message
///   4. The receiver of the last message is flagged as the ghost
///   5. This conversation hasn't already been penalized (GhostTracking dedup)
/// </summary>
public class GhostDetectionService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GhostDetectionService> _logger;
    private readonly IConfiguration _config;

    private TimeSpan CheckInterval => TimeSpan.FromHours(
        _config.GetValue("GhostDetection:CheckIntervalHours", 6));
    private int InactivityDays => _config.GetValue("GhostDetection:InactivityThresholdDays", 3);
    private int MinMutualMessages => _config.GetValue("GhostDetection:MinMutualMessages", 3);

    public GhostDetectionService(
        IServiceProvider serviceProvider,
        ILogger<GhostDetectionService> logger,
        IConfiguration config)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GhostDetectionService started (interval={Interval}h, threshold={Days}d, minMsgs={Min})",
            CheckInterval.TotalHours, InactivityDays, MinMutualMessages);

        // Wait a bit at startup to let the service stabilize
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DetectAndReportGhostsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ghost detection cycle failed");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    internal async Task DetectAndReportGhostsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        _logger.LogDebug("Running ghost detection cycle...");

        // 1. Get all non-deleted messages in the scan window
        var cutoff = DateTime.UtcNow.AddDays(-InactivityDays);
        var windowStart = cutoff.AddDays(-7);
        var windowMessages = await db.Messages
            .Where(m => !m.IsDeleted && m.SentAt >= windowStart)
            .OrderBy(m => m.SentAt)
            .ToListAsync(ct);

        // 2. Group by ConversationId in memory (EF InMemory can't do complex GroupBy)
        var conversationGroups = windowMessages
            .GroupBy(m => m.ConversationId)
            .ToList();

        _logger.LogDebug("Found {Count} conversation groups in window", conversationGroups.Count);

        var validConversations = new List<(string ConvId, DateTime LastMsgAt, string LastSender, string LastReceiver)>();

        foreach (var group in conversationGroups)
        {
            var lastMsg = group.OrderByDescending(m => m.SentAt).First();
            if (lastMsg.SentAt >= cutoff)
                continue;

            var distinctSenders = group.Select(m => m.SenderId).Distinct().ToList();
            if (distinctSenders.Count < 2)
                continue;

            var senderCounts = group.GroupBy(m => m.SenderId)
                .ToDictionary(sg => sg.Key, sg => sg.Count());

            if (senderCounts.Values.Any(c => c < MinMutualMessages))
                continue;

            validConversations.Add((
                group.Key,
                lastMsg.SentAt,
                lastMsg.SenderId,
                lastMsg.ReceiverId
            ));
        }

        _logger.LogDebug("{Count} conversations have mutual engagement exceeding inactivity threshold",
            validConversations.Count);

        // 3. Get already-processed ghost incidents (dedup)
        var processedKeys = await db.Set<GhostTracking>()
            .Where(g => g.Reported)
            .Select(g => g.ConversationId)
            .ToListAsync(ct);

        var processedSet = new HashSet<string>(processedKeys);

        // 4. For each valid conversation, report the ghost
        foreach (var (convId, lastMsgAt, lastSender, lastReceiver) in validConversations)
        {
            if (ct.IsCancellationRequested) break;

            // Skip if already processed
            if (processedSet.Contains(convId))
                continue;

            // The ghost is the receiver of the last message
            var ghostUserId = lastReceiver;
            var victimUserId = lastSender;

            if (string.IsNullOrEmpty(ghostUserId) || string.IsNullOrEmpty(victimUserId))
                continue;

            // Double-check: verify ghost hasn't sent anything after the last message
            var ghostReplied = await db.Messages
                .AnyAsync(m =>
                    m.ConversationId == convId &&
                    m.SenderId == ghostUserId &&
                    m.SentAt > lastMsgAt,
                    ct);

            if (ghostReplied)
                continue;

            // Report to reputation-service
            var reported = await ReportGhostAsync(httpClientFactory, ghostUserId, victimUserId, convId, ct);

            // Record in tracking table (whether or not report succeeded)
            db.Set<GhostTracking>().Add(new GhostTracking
            {
                GhostUserId = ghostUserId,
                VictimUserId = victimUserId,
                ConversationId = convId,
                Reported = reported,
            });
            await db.SaveChangesAsync(ct);

            if (reported)
            {
                _logger.LogInformation(
                    "Ghost detected: {Ghost} ghosted {Victim} in conversation {Conv}",
                    ghostUserId, victimUserId, convId);
            }
        }

        _logger.LogDebug("Ghost detection cycle completed");
    }

    internal async Task<bool> ReportGhostAsync(
        IHttpClientFactory httpClientFactory,
        string ghostUserId,
        string victimUserId,
        string conversationId,
        CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("ReputationService");
            var payload = new
            {
                targetKeycloakId = ghostUserId,
                actorKeycloakId = victimUserId,
                reason = $"Ghosted after mutual conversation ({conversationId})",
                status = "Resolved"
            };
            var json = JsonSerializer.Serialize(payload);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/api/reputation/internal/record-ghost", httpContent, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to report ghost {Ghost} to reputation-service", ghostUserId);
            return false;
        }
    }
}
