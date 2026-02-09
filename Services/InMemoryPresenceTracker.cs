using System.Collections.Concurrent;

namespace MessagingService.Services;

/// <summary>
/// In-memory presence tracker using ConcurrentDictionary.
/// Thread-safe for single-server SignalR deployments.
/// For multi-server, replace with Redis-backed implementation.
/// </summary>
public class InMemoryPresenceTracker : IPresenceTracker
{
    private static readonly ConcurrentDictionary<string, HashSet<string>> _onlineUsers = new();
    private static readonly object _lock = new();

    public Task<bool> UserConnected(string userId, string connectionId)
    {
        var isFirstConnection = false;

        lock (_lock)
        {
            if (!_onlineUsers.TryGetValue(userId, out var connections))
            {
                connections = new HashSet<string>();
                _onlineUsers[userId] = connections;
                isFirstConnection = true;
            }

            connections.Add(connectionId);
        }

        return Task.FromResult(isFirstConnection);
    }

    public Task<bool> UserDisconnected(string userId, string connectionId)
    {
        var isLastConnection = false;

        lock (_lock)
        {
            if (_onlineUsers.TryGetValue(userId, out var connections))
            {
                connections.Remove(connectionId);

                if (connections.Count == 0)
                {
                    _onlineUsers.TryRemove(userId, out _);
                    isLastConnection = true;
                }
            }
        }

        return Task.FromResult(isLastConnection);
    }

    public Task<IReadOnlyList<string>> GetOnlineUsers()
    {
        IReadOnlyList<string> users;

        lock (_lock)
        {
            users = _onlineUsers.Keys.ToList().AsReadOnly();
        }

        return Task.FromResult(users);
    }

    public Task<bool> IsOnline(string userId)
    {
        return Task.FromResult(_onlineUsers.ContainsKey(userId));
    }

    public Task<IReadOnlyList<string>> GetConnectionsForUser(string userId)
    {
        IReadOnlyList<string> connections;

        lock (_lock)
        {
            if (_onlineUsers.TryGetValue(userId, out var conns))
            {
                connections = conns.ToList().AsReadOnly();
            }
            else
            {
                connections = Array.Empty<string>();
            }
        }

        return Task.FromResult(connections);
    }
}
