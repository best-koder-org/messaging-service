using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MessagingService.Services;

/// <summary>
/// Resolves numeric profile IDs to Keycloak user IDs using SwipeService's
/// <c>/api/swipes/user-mappings</c> endpoint. The mapping is cached in memory
/// with a short TTL. Keycloak UUIDs are returned unchanged.
/// </summary>
public sealed class UserIdentityResolver : IUserIdentityResolver
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UserIdentityResolver> _logger;
    private readonly string _swipeServiceBaseUrl;
    private readonly ConcurrentDictionary<string, string> _profileToKeycloak = new();
    private DateTime _lastFetch = DateTime.MinValue;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private static readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(5);

    public UserIdentityResolver(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<UserIdentityResolver> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _swipeServiceBaseUrl =
            configuration["Services:SwipeService:BaseUrl"] ?? "http://localhost:8087";
    }

    public async Task<string> ResolveKeycloakIdAsync(string userIdOrProfileId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userIdOrProfileId))
            return userIdOrProfileId ?? string.Empty;

        // Keycloak IDs are UUIDs (contain '-'); numeric strings are profile IDs.
        if (!int.TryParse(userIdOrProfileId, out var profileId))
            return userIdOrProfileId;

        await EnsureMappingsAsync(ct);

        var key = profileId.ToString();
        return _profileToKeycloak.TryGetValue(key, out var kc) && !string.IsNullOrEmpty(kc)
            ? kc
            : userIdOrProfileId;
    }

    private async Task EnsureMappingsAsync(CancellationToken ct)
    {
        if (_lastFetch != DateTime.MinValue && DateTime.UtcNow - _lastFetch < _cacheTtl)
            return;

        await _fetchLock.WaitAsync(ct);
        try
        {
            if (_lastFetch != DateTime.MinValue && DateTime.UtcNow - _lastFetch < _cacheTtl)
                return;

            var client = _httpClientFactory.CreateClient("SwipeService");
            var response = await client.GetAsync($"{_swipeServiceBaseUrl}/api/swipes/user-mappings", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("User identity mappings fetch failed with {Status}", response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("User identity mappings response had unexpected shape");
                return;
            }

            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("profileId", out var pid) || !pid.TryGetInt32(out var p) ||
                    !item.TryGetProperty("keycloakUserId", out var kc))
                    continue;
                _profileToKeycloak[p.ToString()] = kc.GetString() ?? string.Empty;
            }

            _lastFetch = DateTime.UtcNow;
            _logger.LogInformation("Loaded {Count} profile->keycloak identity mappings", _profileToKeycloak.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load user identity mappings");
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    // Test seams — seed/clear mappings without HTTP.
    internal void SeedMappingForTest(int profileId, string keycloakId)
    {
        _profileToKeycloak[profileId.ToString()] = keycloakId;
        _lastFetch = DateTime.UtcNow;
    }

    internal void ClearCacheForTest()
    {
        _profileToKeycloak.Clear();
        _lastFetch = DateTime.MinValue;
    }
}
