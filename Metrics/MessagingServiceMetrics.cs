using System.Diagnostics.Metrics;

namespace MessagingService.Metrics;

public sealed class MessagingServiceMetrics
{
    public const string MeterName = "MessagingService";

    private readonly Counter<long> _messagesSent;
    private readonly Counter<long> _messagesModerated;
    private readonly Histogram<double> _deliveryDuration;
    private readonly Histogram<double> _spamScore;

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
    }

    public void MessageSent() => _messagesSent.Add(1);
    public void MessageModerated() => _messagesModerated.Add(1);
    public void RecordDeliveryDuration(double ms) => _deliveryDuration.Record(ms);
    public void RecordSpamScore(double score) => _spamScore.Record(score);
}
