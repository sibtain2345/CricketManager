namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Phase 7, Slice 7.5: a running tally of one player's contributions over a period (a month, a
/// year), so awards can be decided from real accumulated output rather than a single innings.
///
/// Fed by FixturePlayService as matches are played, from the same per-match combined rating
/// PostMatchAnalysisService already produces plus the runs/wickets from the match records.
/// Reset at the start of the period it covers.
/// </summary>
public sealed class PlayerFormTally
{
    public required Guid PlayerId { get; init; }
    public required string PlayerName { get; init; }
    public Guid? TeamId { get; set; }

    public int Matches { get; set; }
    public double RatingSum { get; set; }
    public int Runs { get; set; }
    public int BallsFaced { get; set; }
    public int Wickets { get; set; }
    public double OversBowled { get; set; }
    public int RunsConceded { get; set; }

    /// <summary>Age at the time the tally was opened - so a "breakthrough player" award can be gated on youth.</summary>
    public int Age { get; set; }

    public double AverageRating => Matches == 0 ? 0 : RatingSum / Matches;
    public double BattingAverage => Runs == 0 ? 0 : Runs / (double)Math.Max(1, Matches);
    public double Economy => OversBowled <= 0 ? 0 : RunsConceded / OversBowled;

    /// <summary>A single "how good has his period been" number - the rating average, with volume bonuses so a big body of work outranks a hot cameo.</summary>
    public double PeriodScore => AverageRating + Math.Min(15, Matches * 1.2) + Math.Min(20, Runs / 40.0) + Math.Min(20, Wickets * 1.4);

    public void AddMatch(double rating, int runs, int ballsFaced, int wickets, double oversBowled, int runsConceded)
    {
        Matches++;
        RatingSum += rating;
        Runs += runs;
        BallsFaced += ballsFaced;
        Wickets += wickets;
        OversBowled += oversBowled;
        RunsConceded += runsConceded;
    }
}
