using System.Diagnostics;
using DatingApp.Llm;
using MessagingService.Prompts;

namespace MessagingService.Services;

public enum SafetyLevel
{
    Safe,
    Warning,
    Block
}

public record SafetyClassification(SafetyLevel Level, string Reason, double Confidence, long LatencyMs = 0);

public interface ISafetyAgentService
{
    Task<SafetyClassification> ClassifyAsync(string message, CancellationToken ct = default);
}

/// <summary>
/// LLM-powered message safety classifier. Falls back to static filter on LLM failure.
/// </summary>
public class SafetyAgentService : ISafetyAgentService
{
    private readonly LlmRouter _llmRouter;
    private readonly IContentModerationService _staticFilter;
    private readonly ILogger<SafetyAgentService> _logger;
    private readonly MessagingService.Metrics.MessagingServiceMetrics? _metrics;
    private static readonly TimeSpan LlmTimeout = TimeSpan.FromSeconds(2);

    public SafetyAgentService(
        LlmRouter llmRouter,
        IContentModerationService staticFilter,
        ILogger<SafetyAgentService> logger,
        MessagingService.Metrics.MessagingServiceMetrics? metrics = null)
    {
        _llmRouter = llmRouter;
        _staticFilter = staticFilter;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<SafetyClassification> ClassifyAsync(string message, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(LlmTimeout);

            var request = new LlmRequest
            {
                SystemPrompt = SafetyClassificationPrompt.SystemPrompt,
                Messages = new List<LlmMessage>
                {
                    new("user", message)
                },
                MaxTokens = 60,
                Temperature = 0.1
            };

            var response = await _llmRouter.GenerateAsync(request, timeoutCts.Token);
            sw.Stop();

            if (response.Success)
            {
                var classification = SafetyClassificationPrompt.Parse(response.Content);
                _logger.LogInformation(
                    "Safety classification: {Level} ({Confidence:P0}) for message, {Ms}ms via {Provider}",
                    classification.Level, classification.Confidence, sw.ElapsedMilliseconds, response.Provider);

                _metrics?.SafetyClassified(classification.Level.ToString().ToLowerInvariant());
                _metrics?.RecordSafetyLatency(sw.ElapsedMilliseconds);
                _metrics?.SafetyClassified(classification.Level.ToString().ToLowerInvariant());
                _metrics?.RecordSafetyLatency(sw.ElapsedMilliseconds);
                return classification with { LatencyMs = sw.ElapsedMilliseconds };
            }

            _logger.LogWarning("LLM classification failed ({Error}), falling back to static filter", response.Error);
            return await FallbackToStaticFilter(message, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning("LLM classification timed out after {Ms}ms, falling back to static filter", sw.ElapsedMilliseconds);
            return await FallbackToStaticFilter(message, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Safety agent error, falling back to static filter");
            return await FallbackToStaticFilter(message, sw.ElapsedMilliseconds);
        }
    }

    private async Task<SafetyClassification> FallbackToStaticFilter(string message, long latencyMs)
    {
        _metrics?.SafetyFallback();
        var result = await _staticFilter.ModerateContentAsync(message);
        var level = result.IsApproved ? SafetyLevel.Safe : SafetyLevel.Block;
        return new SafetyClassification(level, result.Reason ?? "static_filter", 1.0, latencyMs);
    }
}
