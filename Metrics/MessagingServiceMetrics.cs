using System.Diagnostics.Metrics;

namespace MessagingService.Metrics;

public sealed class MessagingServiceMetrics
{
    public const string MeterName = "MessagingService";

    private readonly Counter<long> _messagesSent;
    private readonly Counter<long> _messagesModerated;
    private readonly Histogram<double> _deliveryDuration;
    private readonly Histogram<double> _spamScore;
    private readonly Counter<long> _safetyClassifications;
    private readonly Histogram<double> _safetyLatency;
    private readonly Counter<long> _safetyFallbacks;

    public MessagingServiceMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _messagesSent = meter.CreateCounter<long>("messages_sent_total",
            description: "Total number of messages sent");
        _messagesModerated = meter.CreateCounter<long>("messages_moderated_total",
            description: "Total number of messages moderated/blocked");
        _deliveryDuration = meter.CreateHistogram<double>("message_delivery_duration_ms",
            unit: "ms",
            description: "Duration of message delivery via SignalR in milliseconds");
        _spamScore = meter.CreateHistogram<double>("spam_detection_score",
            description: "Distribution of spam detection scores");
        _safetyClassifications = meter.CreateCounter<long>("safety_classifications_total",
            description: "Total safety agent classifications by level");
        _safetyLatency = meter.CreateHistogram<double>("safety_classification_latency_ms",
            unit: "ms",
            description: "LLM safety classification latency in milliseconds");
        _safetyFallbacks = meter.CreateCounter<long>("safety_fallback_total",
            description: "Times safety agent fell back to static filter");
    }

    public void MessageSent() => _messagesSent.Add(1);
    public void MessageModerated() => _messagesModerated.Add(1);
    public void RecordDeliveryDuration(double ms) => _deliveryDuration.Record(ms);
    public void RecordSpamScore(double score) => _spamScore.Record(score);
    public void SafetyClassified(string level) => _safetyClassifications.Add(1, new KeyValuePair<string, object?>("level", level));
    public void RecordSafetyLatency(double ms) => _safetyLatency.Record(ms);
    public void SafetyFallback() => _safetyFallbacks.Add(1);
}
