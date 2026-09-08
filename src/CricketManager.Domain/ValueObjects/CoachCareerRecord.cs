using System.Text.Json.Serialization;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Post-Phase-5 rectification pass, Wave 7 (point 20): a coach's multi-year track record, which
/// is what a real board actually judges him on - not just last season.
///
/// Part 4 gave the board structured objectives for ONE contract year. This is the layer above:
/// a coach with titles in the bank has earned rope a first-timer has not; a coach who has gone
/// a whole tenure at a big club without a trophy is under a different kind of pressure however
/// respectable each individual season looked; and what counts as success is team-appropriate -
/// a developing nation reaching a World Cup quarter-final is a triumph, the same run for a
/// powerhouse is a failure.
///
/// Accumulated by CoachCareerService.UpdateCareerRecord at each annual rollover, read by
/// EffectiveDismissalFloor and the board-judgment logic in EvaluateSeason.
/// </summary>
public sealed class CoachCareerRecord
{
    /// <summary>Trophies won across the whole career, any competition.</summary>
    [JsonInclude] public int TitlesWon { get; private set; }

    /// <summary>Finals of a MAJOR (World-Cup / WTC-tier) competition reached - Competition.IsMajor.</summary>
    [JsonInclude] public int MajorFinalsReached { get; private set; }

    /// <summary>Major titles specifically - the ones that define a coaching career.</summary>
    [JsonInclude] public int MajorTitlesWon { get; private set; }

    [JsonInclude] public int SeasonsCoached { get; private set; }

    /// <summary>Running sum of ScoreSeason readings - a coach who consistently gets more out of his sides than they should carries real credit even without silverware.</summary>
    [JsonInclude] public double CumulativeOverperformance { get; private set; }

    /// <summary>Seasons at the CURRENT club, and how many of those have passed since the last trophy there - the "one tenure, no title" clock.</summary>
    [JsonInclude] public int SeasonsAtCurrentClub { get; private set; }
    [JsonInclude] public int SeasonsSinceLastTitle { get; private set; }

    public void RecordSeason(double performanceScore, bool wonTitle, bool wonMajorTitle, bool reachedMajorFinal, bool sameClubAsLastSeason)
    {
        SeasonsCoached++;
        CumulativeOverperformance += performanceScore;

        SeasonsAtCurrentClub = sameClubAsLastSeason ? SeasonsAtCurrentClub + 1 : 1;

        if (wonTitle) TitlesWon++;
        if (wonMajorTitle) MajorTitlesWon++;
        if (reachedMajorFinal) MajorFinalsReached++;

        SeasonsSinceLastTitle = wonTitle ? 0 : SeasonsSinceLastTitle + 1;
    }

    /// <summary>Called by CoachJobMarketService.Hire / InterimCoachService promotion - a new job resets the club-specific clocks, never the career totals.</summary>
    public void OnNewAppointment()
    {
        SeasonsAtCurrentClub = 0;
        SeasonsSinceLastTitle = 0;
    }
}
