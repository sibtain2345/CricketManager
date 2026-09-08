using System.Text.Json.Serialization;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Entities;

/// <summary>
/// One scheduled match, before (or regardless of whether) it has actually been played -
/// the thing FixtureGenerationService/PlayoffBracketService produce and CompetitionWindow
/// was always described as the seam for (see that class's doc comment). Deliberately a
/// separate concept from MatchResult: a Fixture is "who plays whom, when, where" -
/// scheduling data - and only gains a ResultingMatchId once MatchSimulator/MatchRecorder
/// actually plays it. Building the fixture list and playing the fixtures are different
/// jobs, the same separation of concerns as MatchSimulator vs MatchRecorder itself.
///
/// A knockout fixture's participants are not always known at generation time - "Winner of
/// Semi-Final 1" is a real fixture before that semi-final is played. HomeTeamId/AwayTeamId
/// are therefore nullable, with HomeFeederFixtureId/AwayFeederFixtureId recording which
/// earlier fixture resolves each slot. PlayoffBracketService.RecordFixtureResult is what
/// fills a placeholder in once the feeder fixture completes.
/// </summary>
public sealed class Fixture
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CompetitionId { get; init; }
    public Guid SeasonId { get; init; }

    /// <summary>Human-readable stage label - "Round 3", "Group A", "Semi-Final 1", "Qualifier 1", "Final", etc.</summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>Round-robin round number (1-based) within its stage/group. Not meaningful for a knockout fixture (0).</summary>
    public int RoundNumber { get; set; }

    /// <summary>
    /// Phase 6, Slice 6.1: a stable, generation-order sequence number within a season's fixture
    /// list. FixturePlayService orders each day's due fixtures by this (never by a Guid, per this
    /// project's determinism history) and derives each fixture's RNG stream from it, so playing a
    /// season is reproducible from the seed regardless of what Guid values the entities happened
    /// to get. 0 for a hand-built fixture with no generator assigning one.
    /// </summary>
    public int SequenceNumber { get; set; }

    public Guid? HomeTeamId { get; set; }
    public Guid? AwayTeamId { get; set; }

    /// <summary>Set instead of a concrete id when a knockout slot isn't decided yet, e.g. "Winner of Semi-Final 1". Display-only - RecordFixtureResult is what actually resolves the slot.</summary>
    public string? HomeTeamPlaceholder { get; set; }
    public string? AwayTeamPlaceholder { get; set; }

    public DateOnly ScheduledDate { get; set; }
    public Guid? GroundId { get; set; }

    [JsonInclude] public FixtureStatus Status { get; private set; } = FixtureStatus.Scheduled;

    /// <summary>Set once actually played. Null for a bye (a knockout slot where the other side never had an opponent) - completed without ever becoming a real match.</summary>
    [JsonInclude] public Guid? ResultingMatchId { get; private set; }

    [JsonInclude] public Guid? WinningTeamId { get; private set; }

    /// <summary>The other side of the result once WinningTeamId is set. Null until both HomeTeamId and AwayTeamId are themselves resolved (a bye has no loser).</summary>
    public Guid? LosingTeamId =>
        WinningTeamId is null || HomeTeamId is null || AwayTeamId is null ? null :
        HomeTeamId == WinningTeamId ? AwayTeamId : HomeTeamId;

    /// <summary>Which earlier fixture's winner (or, if *FeederIsLoserSlot, loser) fills the home slot - the bracket-progression link. Null for a fixture whose team is already known at generation time (every round-robin fixture, and a knockout fixture whose seed needed no earlier round).</summary>
    public Guid? HomeFeederFixtureId { get; set; }
    public Guid? AwayFeederFixtureId { get; set; }

    /// <summary>True when this slot is filled by the feeder fixture's LOSER, not its winner - the one case that needs it is an IPL-style Qualifier 2, where the Qualifier 1 loser gets a second chance rather than being eliminated.</summary>
    public bool HomeFeederIsLoserSlot { get; set; }
    public bool AwayFeederIsLoserSlot { get; set; }

    /// <summary>Records a real, played result.</summary>
    public void Complete(Guid matchId, Guid winningTeamId)
    {
        ResultingMatchId = matchId;
        WinningTeamId = winningTeamId;
        Status = FixtureStatus.Completed;
    }

    /// <summary>
    /// Phase 6, Slice 6.1: records a played match that produced no winner - a tied or washed-out
    /// league fixture. Both sides still get their standings row updated by the recorder; there is
    /// simply no WinningTeamId. A knockout fixture should never reach this (a tie there is broken
    /// by the match engine or, failing that, by the caller before completing).
    /// </summary>
    public void CompleteNoResult(Guid matchId)
    {
        ResultingMatchId = matchId;
        WinningTeamId = null;
        Status = FixtureStatus.Completed;
    }

    /// <summary>Records a bye - the present team advances without a match ever being played.</summary>
    public void CompleteAsBye(Guid advancingTeamId)
    {
        ResultingMatchId = null;
        WinningTeamId = advancingTeamId;
        Status = FixtureStatus.Completed;
    }

    public void Cancel() => Status = FixtureStatus.Cancelled;

    /// <summary>
    /// Phase 6, Slice 6.6: moves an unplayed fixture to a new date. Used when a fixture could not
    /// be played on its scheduled day (a knockout slot whose feeder slipped, a ground unavailable,
    /// a side unable to field an XI) - the competition re-fixtures it rather than dropping it.
    /// A completed fixture cannot be rescheduled.
    ///
    /// <paramref name="countsAsAttempt"/> is true for a genuine failure to play (RescheduleCount
    /// climbs toward the abandon threshold). It is FALSE when the fixture simply is not ready yet -
    /// a knockout slot still waiting on an unresolved feeder fixture - because that is not the
    /// fixture failing, it is the bracket not having caught up, and counting it would abandon a
    /// perfectly good semi-final just because the quarter-finals took a while (Post-Phase-6
    /// rectification, Pass 1 bug 2).
    /// </summary>
    public void Reschedule(DateOnly newDate, bool countsAsAttempt = true)
    {
        if (Status == FixtureStatus.Completed)
            throw new InvalidOperationException("A completed fixture cannot be rescheduled.");
        ScheduledDate = newDate;
        Status = FixtureStatus.Scheduled;
        if (countsAsAttempt) RescheduleCount++;
    }

    /// <summary>True when this fixture's participants are not yet known because a feeder fixture it depends on has not produced a result.</summary>
    [JsonIgnore]
    public bool AwaitingFeederResult =>
        (HomeFeederFixtureId is not null && HomeTeamId is null)
        || (AwayFeederFixtureId is not null && AwayTeamId is null);

    [JsonInclude] public int RescheduleCount { get; private set; }
}
