using System.Text.Json;

namespace MessagingService.Services;

public interface IMatchValidationService
{
    Task<bool> AreUsersMatchedAsync(string userId1, string userId2);
}

public class MatchValidationService : IMatchValidationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MatchValidationService> _logger;
    private readonly string _swipeServiceBaseUrl;

    public MatchValidationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<MatchValidationService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("SwipeService");
        _logger = logger;
        _swipeServiceBaseUrl = configuration["Services:SwipeService:BaseUrl"] ?? "http://localhost:8087";
    }

    public async Task<bool> AreUsersMatchedAsync(string userId1, string userId2)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_swipeServiceBaseUrl}/api/matches/check/{userId1}/{userId2}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Match check failed with status {StatusCode} for users {User1}/{User2}", 
                    response.StatusCode, userId1, userId2);
                // Return false for security - no match confirmed
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<MatchCheckResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result?.HasMatch ?? false; // Default to false for security
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking match status between {User1} and {User2}", userId1, userId2);
            // Security: Deny message when check fails
            return false;
        }
    }

    private class MatchCheckResponse
    {
        public bool HasMatch { get; set; }
        public string? Reason { get; set; }
    }
}
