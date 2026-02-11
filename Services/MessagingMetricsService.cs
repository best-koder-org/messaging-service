using System.Diagnostics.Metrics;

namespace MessagingService.Services
{
    /// <summary>
    /// T070: Messaging Metrics
    /// Tracks: SignalR Connections, Message Delivery Rate, Latency, Moderation
    /// </summary>
    public class MessagingMetricsService
    {
        private static readonly Meter _meter = new("MessagingService", "1.0.0");

        // Connection Metrics
        private static long _activeConnections = 0;
        private static readonly ObservableGauge<long> _activeConnectionsGauge = _meter.CreateObservableGauge(
            "messaging_signalr_connections_active",
            () => _activeConnections,
            description: "Current number of active SignalR connections");

        private static readonly Counter<long> _connectionsTotal = _meter.CreateCounter<long>(
            "messaging_signalr_connections_total",
            description: "Total SignalR connections established");

        private static readonly Counter<long> _disconnections = _meter.CreateCounter<long>(
            "messaging_signalr_disconnections_total",
            description: "Total SignalR disconnections");

        // Message Delivery Metrics
        private static readonly Counter<long> _messagesSent = _meter.CreateCounter<long>(
            "messaging_messages_sent_total",
            description: "Total messages sent");

        private static readonly Counter<long> _messagesDelivered = _meter.CreateCounter<long>(
            "messaging_messages_delivered_total",
            description: "Messages successfully delivered");

        private static readonly Counter<long> _messagesFailed = _meter.CreateCounter<long>(
            "messaging_messages_failed_total",
            description: "Messages that failed to deliver");

        private static readonly Histogram<double> _messageDeliveryLatency = _meter.CreateHistogram<double>(
            "messaging_delivery_latency_ms",
            unit: "milliseconds",
            description: "Time from send to delivery confirmation");

        // Message Stats
        private static readonly Histogram<int> _messageLength = _meter.CreateHistogram<int>(
            "messaging_message_length_chars",
            description: "Message content length in characters");

        private static readonly Counter<long> _messagesRead = _meter.CreateCounter<long>(
            "messaging_messages_read_total",
            description: "Total messages marked as read");

        private static readonly Histogram<double> _timeToRead = _meter.CreateHistogram<double>(
            "messaging_time_to_read_seconds",
            unit: "seconds",
            description: "Time from delivery to read");

        // Conversation Metrics
        private static long _activeConversations = 0;
        private static readonly ObservableGauge<long> _activeConversationsGauge = _meter.CreateObservableGauge(
            "messaging_conversations_active",
            () => _activeConversations,
            description: "Number of conversations with recent activity");

        private static readonly Counter<long> _conversationsStarted = _meter.CreateCounter<long>(
            "messaging_conversations_started_total",
            description: "New conversations initiated");

        // Moderation Metrics
        private static readonly Counter<long> _moderationChecks = _meter.CreateCounter<long>(
            "messaging_moderation_checks_total",
            description: "Total moderation checks performed");

        private static readonly Counter<long> _messagesBlocked = _meter.CreateCounter<long>(
            "messaging_messages_blocked_total",
            description: "Messages blocked by moderation");

        private static readonly Histogram<double> _moderationLatency = _meter.CreateHistogram<double>(
            "messaging_moderation_latency_ms",
            unit: "milliseconds",
            description: "Time to complete moderation check");

        // Public methods
        public void RecordConnection()
        {
            Interlocked.Increment(ref _activeConnections);
            _connectionsTotal.Add(1);
        }

        public void RecordDisconnection(string reason = "normal")
        {
            Interlocked.Decrement(ref _activeConnections);
            _disconnections.Add(1, new KeyValuePair<string, object?>("reason", reason));
        }

        public void RecordMessageSent(string messageType, int contentLength)
        {
            var tags = new KeyValuePair<string, object?>("type", messageType);
            _messagesSent.Add(1, tags);
            _messageLength.Record(contentLength);
        }

        public void RecordMessageDelivered(double latencyMs, bool success)
        {
            if (success)
            {
                _messagesDelivered.Add(1);
                _messageDeliveryLatency.Record(latencyMs);
            }
            else
            {
                _messagesFailed.Add(1);
            }
        }

        public void RecordMessageRead(double secondsSinceSent)
        {
            _messagesRead.Add(1);
            _timeToRead.Record(secondsSinceSent);
        }

        public void RecordConversationStarted()
        {
            _conversationsStarted.Add(1);
            Interlocked.Increment(ref _activeConversations);
        }

        public void RecordConversationEnded()
        {
            Interlocked.Decrement(ref _activeConversations);
        }

        public void RecordModerationCheck(bool blocked, double latencyMs, string reason = "")
        {
            _moderationChecks.Add(1);
            _moderationLatency.Record(latencyMs);

            if (blocked)
            {
                var tags = new KeyValuePair<string, object?>("reason", string.IsNullOrEmpty(reason) ? "policy_violation" : reason);
                _messagesBlocked.Add(1, tags);
            }
        }

        public void UpdateActiveConversations(long count)
        {
            Interlocked.Exchange(ref _activeConversations, count);
        }
    }
}
