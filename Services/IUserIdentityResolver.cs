using System.Threading;
using System.Threading.Tasks;

namespace MessagingService.Services;

/// <summary>
/// Resolves user identifiers to Keycloak user IDs. Lets clients that still send
/// numeric profile IDs (e.g. older app builds) interact with messages that are
/// stored under Keycloak IDs.
/// </summary>
public interface IUserIdentityResolver
{
    /// <summary>
    /// Returns the Keycloak user ID for a user identifier. Numeric profile IDs
    /// are resolved via SwipeService's user-mappings; Keycloak UUIDs pass through
    /// unchanged. Falls back to the input when it cannot be resolved.
    /// </summary>
    Task<string> ResolveKeycloakIdAsync(string userIdOrProfileId, CancellationToken ct = default);
}
