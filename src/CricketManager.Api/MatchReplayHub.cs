using Microsoft.AspNetCore.SignalR;

namespace CricketManager.Api;

/// <summary>
/// Phase 17 Part 2, S1: the connection scaffold only. The real ball-by-ball replay logic (streaming
/// a completed match's <c>Deliveries</c> log at a controlled pace, with the pitch-map/wagon-wheel
/// projections Track A adds) is S4's job, once that data layer exists - building it now would mean
/// guessing at a payload shape before the data it needs to carry is real. This hub exists so the
/// connection lifecycle (client connects, joins a match "room", disconnects) is proven end to end
/// ahead of that, the same "prove the plumbing first" discipline S1's REST endpoints already follow.
/// </summary>
public sealed class MatchReplayHub : Hub
{
    public async Task JoinMatch(string matchId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, matchId);

    public async Task LeaveMatch(string matchId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, matchId);
}
