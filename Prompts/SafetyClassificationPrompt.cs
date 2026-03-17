using MessagingService.Services;

namespace MessagingService.Prompts;

/// <summary>
/// System prompt and parsing for LLM-based safety classification of chat messages.
/// </summary>
public static class SafetyClassificationPrompt
{
    public const string SystemPrompt = """
You are a safety classifier for a dating app. Classify each message as safe, warning, or block.

Categories to check:
- Harassment, threats, bullying
- Sexual content or solicitation
- Spam, scam, financial requests
- Personal info sharing (phone, email, address, social media handles)
- Catfishing signals (requests for money, urgency)
- Hate speech, discrimination

Respond ONLY with a JSON object, no other text:
{"level":"safe|warning|block","reason":"brief reason","confidence":0.95}

Rules:
- "safe": normal dating conversation, flirting, compliments, plans to meet
- "warning": borderline content, mild language, possible but unclear intent
- "block": clear harassment, threats, explicit content, scam patterns, hate speech

Examples:
User: "Vill du fika nån gång?"
{"level":"safe","reason":"date invitation","confidence":0.99}

User: "Skicka ditt nummer så kan vi prata"
{"level":"warning","reason":"requesting personal info","confidence":0.85}

User: "Du är så ful, ingen vill ha dig"
{"level":"block","reason":"harassment and bullying","confidence":0.95}

User: "I need you to send me $200 urgently"
{"level":"block","reason":"financial scam pattern","confidence":0.92}

User: "Hej! Gillar du att vandra?"
{"level":"safe","reason":"casual conversation","confidence":0.99}

User: "Ge mig din Instagram"
{"level":"warning","reason":"requesting social media handle","confidence":0.80}
""";

    /// <summary>
    /// Parse the LLM JSON response into a SafetyClassification.
    /// Returns a safe default if parsing fails.
    /// </summary>
    public static SafetyClassification Parse(string llmResponse)
    {
        try
        {
            // Strip markdown code fences if present
            var json = llmResponse.Trim();
            if (json.StartsWith("```"))
            {
                var firstNewline = json.IndexOf('\n');
                var lastFence = json.LastIndexOf("```");
                if (firstNewline > 0 && lastFence > firstNewline)
                    json = json[(firstNewline + 1)..lastFence].Trim();
            }

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var levelStr = root.GetProperty("level").GetString() ?? "safe";
            var reason = root.GetProperty("reason").GetString() ?? "";
            var confidence = root.TryGetProperty("confidence", out var conf)
                ? conf.GetDouble()
                : 0.5;

            var level = levelStr.ToLowerInvariant() switch
            {
                "warning" => SafetyLevel.Warning,
                "block" => SafetyLevel.Block,
                _ => SafetyLevel.Safe
            };

            return new SafetyClassification(level, reason, confidence);
        }
        catch
        {
            // If we can't parse the LLM output, default to safe (let static filter handle it)
            return new SafetyClassification(SafetyLevel.Safe, "parse_error", 0.0);
        }
    }
}
