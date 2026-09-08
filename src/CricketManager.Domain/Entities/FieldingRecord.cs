using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Entities;

/// <summary>
/// One player's fielding contribution in one match.
///
/// This was a straight gap: PlayerCareerStats already had Catches/RunOuts/Stumpings fields,
/// the spec lists fielding as one of the three statistical pillars, and there was no record
/// type that could ever populate them - so every player's fielding record was permanently
/// zero and "most catches at this ground" was unanswerable while the batting and bowling
/// equivalents worked fine.
///
/// Per MATCH rather than per innings, because that's how fielding is reported ("4 catches in
/// the match"), and a fielder isn't cleanly bounded by innings the way a batter or a bowler is.
/// </summary>
public sealed class FieldingRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PlayerId { get; init; }
    public MatchContext Context { get; init; } = new();

    /// <summary>True when the player kept wicket in this match - stumpings and keeper catches only make sense in that light.</summary>
    public bool KeptWicket { get; init; }

    public int Catches { get; init; }
    public int RunOuts { get; init; }
    public int Stumpings { get; init; }

    /// <summary>Dropped catches. Tracked because a fielding record that only counts successes tells a coach nothing about who is actually reliable.</summary>
    public int DroppedCatches { get; init; }

    public int Dismissals => Catches + RunOuts + Stumpings;

    /// <summary>Null when no chance came the player's way - zero chances is not the same as a 0% success rate.</summary>
    public double? CatchSuccessRate =>
        Catches + DroppedCatches == 0 ? null : Math.Round((double)Catches / (Catches + DroppedCatches) * 100, 1);
}
