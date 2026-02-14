using Xunit;
using System;
using System.Threading.Tasks;
using MessagingService.Services;

namespace MessagingService.Tests.Services;

public class InMemoryPresenceTrackerTests : IAsyncLifetime
{
    private readonly InMemoryPresenceTracker _tracker = new();
    private readonly string _userId = Guid.NewGuid().ToString();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Clean up: disconnect all connections for our test user
        var connections = await _tracker.GetConnectionsForUser(_userId);
        foreach (var conn in connections)
            await _tracker.UserDisconnected(_userId, conn);
    }

    [Fact]
    public async Task FirstConnection_ReturnsTrue()
    {
        var isFirst = await _tracker.UserConnected(_userId, "conn-1");
        Assert.True(isFirst);
    }

    [Fact]
    public async Task SecondConnection_ReturnsFalse()
    {
        await _tracker.UserConnected(_userId, "conn-1");
        var isFirst = await _tracker.UserConnected(_userId, "conn-2");
        Assert.False(isFirst);
    }

    [Fact]
    public async Task DisconnectLastConnection_ReturnsTrue()
    {
        await _tracker.UserConnected(_userId, "conn-1");
        var isLast = await _tracker.UserDisconnected(_userId, "conn-1");
        Assert.True(isLast);
    }

    [Fact]
    public async Task DisconnectNonLastConnection_ReturnsFalse()
    {
        await _tracker.UserConnected(_userId, "conn-1");
        await _tracker.UserConnected(_userId, "conn-2");
        var isLast = await _tracker.UserDisconnected(_userId, "conn-1");
        Assert.False(isLast);
    }

    [Fact]
    public async Task DisconnectUnknownUser_ReturnsFalse()
    {
        var unknownId = Guid.NewGuid().ToString();
        var isLast = await _tracker.UserDisconnected(unknownId, "conn-1");
        Assert.False(isLast);
    }

    [Fact]
    public async Task ConnectedUser_IsOnline()
    {
        await _tracker.UserConnected(_userId, "conn-1");
        Assert.True(await _tracker.IsOnline(_userId));
    }

    [Fact]
    public async Task DisconnectedUser_IsOffline()
    {
        await _tracker.UserConnected(_userId, "conn-1");
        await _tracker.UserDisconnected(_userId, "conn-1");
        Assert.False(await _tracker.IsOnline(_userId));
    }

    [Fact]
    public async Task GetOnlineUsers_ContainsConnectedUser()
    {
        await _tracker.UserConnected(_userId, "conn-1");
        var online = await _tracker.GetOnlineUsers();
        Assert.Contains(_userId, online);
    }

    [Fact]
    public async Task GetConnectionsForUser_ReturnsAllConnections()
    {
        await _tracker.UserConnected(_userId, "conn-1");
        await _tracker.UserConnected(_userId, "conn-2");
        var connections = await _tracker.GetConnectionsForUser(_userId);
        Assert.Equal(2, connections.Count);
        Assert.Contains("conn-1", connections);
        Assert.Contains("conn-2", connections);
    }

    [Fact]
    public async Task GetConnectionsForUnknownUser_EmptyList()
    {
        var unknownId = Guid.NewGuid().ToString();
        var connections = await _tracker.GetConnectionsForUser(unknownId);
        Assert.Empty(connections);
    }
}
