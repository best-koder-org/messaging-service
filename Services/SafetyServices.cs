using System.Text.RegularExpressions;

namespace MessagingService.Services;

public interface IContentModerationService
{
    Task<ModerationResult> ModerateContentAsync(string content);
}

public interface ISpamDetectionService
{
    Task<bool> IsSpamAsync(string userId, string content);
}

public interface IPersonalInfoDetectionService
{
    Task<PersonalInfoResult> DetectPersonalInfoAsync(string content);
}

public interface IRateLimitingService
{
    Task<bool> IsAllowedAsync(string userId);
}

public class ContentModerationService : IContentModerationService
{
    private readonly IPersonalInfoDetectionService _personalInfoDetection;
    private readonly ILogger<ContentModerationService> _logger;
    
    // Prohibited content patterns
    private readonly string[] _prohibitedWords = {
        "damn", "shit", "fuck", "bitch", "ass", "cunt", "whore", "slut", "nigger", "faggot", "retard", "cunt", "pussy", "dick", "cock", "penis", "vagina", "boobs", "tits", "nude", "naked", "sex", "xxx", "porn", "prostitute", "escort", "hookup", "dtf", "nudes", "sexy", "hot", "horny", "kinky", "fetish", "bdsm", "anal", "oral", "blow", "suck", "masturbate", "orgasm", "cum", "jizz", "sperm", "semen", "erection", "hard", "wet", "tight", "loose", "virgin", "slut", "whore", "bitch", "cunt", "asshole", "motherfucker", "bastard", "prick", "douche", "scumbag", "loser", "idiot", "moron", "stupid", "dumb", "ugly", "fat", "skinny", "gross", "disgusting", "hate", "kill", "die", "suicide", "self-harm", "cut", "drugs", "cocaine", "heroin", "meth", "weed", "marijuana", "alcohol", "drunk", "high", "stoned", "money", "cash", "venmo", "paypal", "bitcoin", "crypto", "investment", "business", "mlm", "pyramid", "scheme", "loan", "credit", "debt", "instagram", "snapchat", "facebook", "twitter", "tiktok", "onlyfans", "telegram", "whatsapp", "kik", "skype", "discord", "zoom"
    };

    private readonly string[] _harmfulPatterns = {
        @"\b(?:kill|hurt|harm|violence|abuse|rape|assault)\b",
        @"\b(?:suicide|self-harm|cutting|depression|anxiety)\b",
        @"\b(?:drugs|cocaine|heroin|meth|marijuana|weed)\b",
        @"\b(?:hate|racism|sexism|homophobia|transphobia)\b"
    };

    public ContentModerationService(
        IPersonalInfoDetectionService personalInfoDetection,
        ILogger<ContentModerationService> logger)
    {
        _personalInfoDetection = personalInfoDetection;
        _logger = logger;
    }

    public async Task<ModerationResult> ModerateContentAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ModerationResult { IsApproved = false, Reason = "Empty content" };
        }

        // Check for personal information
        var personalInfoResult = await _personalInfoDetection.DetectPersonalInfoAsync(content);
        if (personalInfoResult.HasPersonalInfo)
        {
            _logger.LogWarning($"Personal information detected: {personalInfoResult.InfoType}");
            return new ModerationResult 
            { 
                IsApproved = false, 
                Reason = "Personal information sharing is not allowed for safety" 
            };
        }

        var lowerContent = content.ToLowerInvariant();

        // Check prohibited words
        foreach (var word in _prohibitedWords)
        {
            if (lowerContent.Contains(word))
            {
                _logger.LogWarning($"Prohibited word detected: {word}");
                return new ModerationResult 
                { 
                    IsApproved = false, 
                    Reason = "Inappropriate language detected" 
                };
            }
        }

        // Check harmful patterns
        foreach (var pattern in _harmfulPatterns)
        {
            if (Regex.IsMatch(lowerContent, pattern, RegexOptions.IgnoreCase))
            {
                _logger.LogWarning($"Harmful pattern detected: {pattern}");
                return new ModerationResult 
                { 
                    IsApproved = false, 
                    Reason = "Content contains potentially harmful material" 
                };
            }
        }

        // Check for excessive caps (shouting)
        var capsCount = content.Count(char.IsUpper);
        if (capsCount > content.Length * 0.7 && content.Length > 10)
        {
            return new ModerationResult 
            { 
                IsApproved = false, 
                Reason = "Excessive use of capital letters" 
            };
        }

        return new ModerationResult { IsApproved = true };
    }
}

public class PersonalInfoDetectionService : IPersonalInfoDetectionService
{
    private readonly ILogger<PersonalInfoDetectionService> _logger;

    // Regex patterns for personal information
    private readonly Regex[] _personalInfoPatterns = {
        new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled), // SSN
        new(@"\b\d{3}-\d{3}-\d{4}\b", RegexOptions.Compiled), // Phone
        new(@"\(\d{3}\)\s*\d{3}-\d{4}", RegexOptions.Compiled), // Phone (XXX) XXX-XXXX
        new(@"\b\d{10}\b", RegexOptions.Compiled), // 10-digit phone
        new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled), // Email
        new(@"\b\d{1,5}\s+\w+\s+(st|street|ave|avenue|rd|road|dr|drive|ln|lane|blvd|boulevard)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), // Address
        new(@"\b\d{5}(-\d{4})?\b", RegexOptions.Compiled), // ZIP code
        new(@"\b\d{4}\s*\d{4}\s*\d{4}\s*\d{4}\b", RegexOptions.Compiled), // Credit card
        new(@"\b(instagram|insta|ig|snapchat|snap|facebook|fb|twitter|tiktok|onlyfans|telegram|whatsapp|kik|skype|discord)[\s:@]*[a-zA-Z0-9._-]+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), // Social media handles
        new(@"\b(paypal|venmo|cashapp|zelle)[\s:@]*[a-zA-Z0-9._-]+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase) // Payment handles
    };

    public PersonalInfoDetectionService(ILogger<PersonalInfoDetectionService> logger)
    {
        _logger = logger;
    }

    public async Task<PersonalInfoResult> DetectPersonalInfoAsync(string content)
    {
        foreach (var pattern in _personalInfoPatterns)
        {
            if (pattern.IsMatch(content))
            {
                var infoType = GetInfoType(pattern);
                _logger.LogWarning($"Personal information detected: {infoType} in content: {content}");
                
                return new PersonalInfoResult 
                { 
                    HasPersonalInfo = true, 
                    InfoType = infoType 
                };
            }
        }

        return new PersonalInfoResult { HasPersonalInfo = false };
    }

    private string GetInfoType(Regex pattern)
    {
        var patternString = pattern.ToString();
        
        if (patternString.Contains("@")) return "Email Address";
        if (patternString.Contains("3}-\\d{2}-\\d{4}")) return "Social Security Number";
        if (patternString.Contains("3}-\\d{3}-\\d{4}")) return "Phone Number";
        if (patternString.Contains("10")) return "Phone Number";
        if (patternString.Contains("street|ave")) return "Physical Address";
        if (patternString.Contains("5}(-\\d{4})")) return "ZIP Code";
        if (patternString.Contains("4}\\s*\\d{4}")) return "Credit Card Number";
        if (patternString.Contains("instagram|snapchat")) return "Social Media Handle";
        if (patternString.Contains("paypal|venmo")) return "Payment Handle";
        
        return "Personal Information";
    }
}

public class SpamDetectionService : ISpamDetectionService
{
    private readonly Dictionary<string, List<DateTime>> _userMessageHistory = new();
    private readonly Dictionary<string, Dictionary<string, int>> _userContentFrequency = new();
    private readonly ILogger<SpamDetectionService> _logger;
    
    private const int MaxMessagesPerMinute = 10;
    private const int MaxMessagesPerHour = 100;
    private const int MaxSameContentCount = 3;

    public SpamDetectionService(ILogger<SpamDetectionService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> IsSpamAsync(string userId, string content)
    {
        var now = DateTime.UtcNow;
        
        // Initialize user history if not exists
        if (!_userMessageHistory.ContainsKey(userId))
        {
            _userMessageHistory[userId] = new List<DateTime>();
            _userContentFrequency[userId] = new Dictionary<string, int>();
        }

        var userHistory = _userMessageHistory[userId];
        var userContent = _userContentFrequency[userId];

        // Clean old entries (older than 1 hour)
        userHistory.RemoveAll(time => (now - time).TotalHours > 1);

        // Check rate limiting
        var messagesLastMinute = userHistory.Count(time => (now - time).TotalMinutes <= 1);
        var messagesLastHour = userHistory.Count;

        if (messagesLastMinute >= MaxMessagesPerMinute)
        {
            _logger.LogWarning($"User {userId} exceeded messages per minute limit: {messagesLastMinute}");
            return true;
        }

        if (messagesLastHour >= MaxMessagesPerHour)
        {
            _logger.LogWarning($"User {userId} exceeded messages per hour limit: {messagesLastHour}");
            return true;
        }

        // Check for repeated content
        var contentKey = content.ToLowerInvariant().Trim();
        if (userContent.ContainsKey(contentKey))
        {
            userContent[contentKey]++;
            if (userContent[contentKey] >= MaxSameContentCount)
            {
                _logger.LogWarning($"User {userId} sending repeated content: {content}");
                return true;
            }
        }
        else
        {
            userContent[contentKey] = 1;
        }

        // Clean old content frequency data
        if (userContent.Count > 100)
        {
            var keysToRemove = userContent.OrderBy(kvp => kvp.Value).Take(50).Select(kvp => kvp.Key).ToList();
            foreach (var key in keysToRemove)
            {
                userContent.Remove(key);
            }
        }

        // Add current message to history
        userHistory.Add(now);

        return false;
    }
}

public class RateLimitingService : IRateLimitingService
{
    private readonly Dictionary<string, List<DateTime>> _userRequests = new();
    private readonly ILogger<RateLimitingService> _logger;
    
    private const int MaxRequestsPerMinute = 20;

    public RateLimitingService(ILogger<RateLimitingService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> IsAllowedAsync(string userId)
    {
        var now = DateTime.UtcNow;
        
        if (!_userRequests.ContainsKey(userId))
        {
            _userRequests[userId] = new List<DateTime>();
        }

        var userRequests = _userRequests[userId];
        
        // Clean requests older than 1 minute
        userRequests.RemoveAll(time => (now - time).TotalMinutes > 1);
        
        if (userRequests.Count >= MaxRequestsPerMinute)
        {
            _logger.LogWarning($"Rate limit exceeded for user {userId}: {userRequests.Count} requests");
            return false;
        }
        
        userRequests.Add(now);
        return true;
    }
}

public class ModerationResult
{
    public bool IsApproved { get; set; }
    public string? Reason { get; set; }
}

public class PersonalInfoResult
{
    public bool HasPersonalInfo { get; set; }
    public string? InfoType { get; set; }
}
