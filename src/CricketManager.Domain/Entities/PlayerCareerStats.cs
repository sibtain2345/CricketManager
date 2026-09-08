using System.Text.Json.Serialization;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Entities;

/// <summary>
/// Section 14: one row per (player, format) - a fast, flat aggregate cache for quick lookups.
/// A player has up to 3 of these (Test/ODI/T20), never one blended "career stats" blob -
/// because a player's Test average and T20 strike rate are fundamentally different currencies.
///
/// NOTE: this does NOT model the domestic/international/franchise-league hierarchy
/// (First-Class includes Tests, List A includes ODIs, T20 Career includes T20I +
/// domestic T20 + every franchise league, plus each league also gets its own record).
/// That hierarchy is built on demand from the granular BattingInningsRecord/
/// BowlingSpellRecord data via CareerStatsAggregationService - this entity remains a
/// simple "totals for this format" cache alongside it, not a replacement for it.
/// </summary>
public sealed class PlayerCareerStats
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PlayerId { get; init; }
    public MatchFormat Format { get; init; }

    // Batting
    public int Matches { get; set; }
    public int Innings { get; set; }
    public int Runs { get; set; }
    public int BallsFaced { get; set; }
    public int NotOuts { get; set; }
    public int HighestScore { get; set; }
    public int Fifties { get; set; }
    public int Hundreds { get; set; }
    public int Fours { get; set; }
    public int Sixes { get; set; }

    public double BattingAverage => (Innings - NotOuts) <= 0 ? 0 : (double)Runs / (Innings - NotOuts);
    public double StrikeRate => BallsFaced == 0 ? 0 : (double)Runs / BallsFaced * 100;

    // Bowling
    public int BowlingInnings { get; set; }
    public double OversBowled { get; set; }
    public int RunsConceded { get; set; }
    public int Wickets { get; set; }
    public int FiveWicketHauls { get; set; }
    public string BestBowlingFigures { get; set; } = "0/0";

    public double BowlingAverage => Wickets == 0 ? 0 : (double)RunsConceded / Wickets;
    public double Economy => OversBowled == 0 ? 0 : RunsConceded / OversBowled;
    public double BowlingStrikeRate => Wickets == 0 ? 0 : (OversBowled * 6) / Wickets;

    // Fielding
    public int Catches { get; set; }
    public int RunOuts { get; set; }
    public int Stumpings { get; set; }

    /// <summary>
    /// Section 9: registers one match's contribution to this format's record.
    /// Kept intentionally simple for MVP - richer per-ball data comes with the
    /// match simulation engine (Phase 4).
    /// </summary>
    public void RecordBattingInnings(int runs, int ballsFaced, bool notOut, int fours, int sixes)
    {
        Innings++;
        Runs += runs;
        BallsFaced += ballsFaced;
        Fours += fours;
        Sixes += sixes;
        if (notOut) NotOuts++;
        if (runs > HighestScore) HighestScore = runs;
        if (runs >= 100) Hundreds++;
        else if (runs >= 50) Fifties++;
    }

    // Tracks the raw wickets/runs behind BestBowlingFigures for comparison purposes -
    // not the "figures" themselves, just the components used to update them. [JsonInclude]
    // is required here for the same reason it was needed on FormState/Reputation: without
    // it, these private fields reset to their defaults on every reload, and the very next
    // RecordBowlingInnings call after a reload would incorrectly overwrite a legitimate
    // historical best with whatever spell just happened, since -1/0 looks "beatable" by anything.
    [JsonInclude] private int _bestWickets = -1;
    [JsonInclude] private int _bestRunsAtBestFigures;

    public void RecordBowlingInnings(double overs, int runsConceded, int wickets)
    {
        BowlingInnings++;
        OversBowled += overs;
        RunsConceded += runsConceded;
        Wickets += wickets;
        if (wickets >= 5) FiveWicketHauls++;

        // Best figures = most wickets first, fewest runs as the tiebreaker (standard cricket convention).
        // This was previously never updated - BestBowlingFigures stayed "0/0" for every player regardless
        // of what they actually bowled, unlike HighestScore above which does track correctly.
        if (wickets > _bestWickets || (wickets == _bestWickets && runsConceded < _bestRunsAtBestFigures))
        {
            _bestWickets = wickets;
            _bestRunsAtBestFigures = runsConceded;
            BestBowlingFigures = $"{wickets}/{runsConceded}";
        }
    }
}
