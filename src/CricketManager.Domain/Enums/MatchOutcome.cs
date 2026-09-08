namespace CricketManager.Domain.Enums;

/// <summary>
/// The complete set of results a cricket match can produce. Draw and Tie are genuinely
/// distinct outcomes and are NOT the same as NoResult:
/// - Draw: a multi-day/first-class match that ran out of time without a result. Only
///   possible in FirstClassChampionship-style competitions, and real FC points systems
///   award points for it (typically fewer than a win).
/// - Tie: scores level at the end of a completed match. Rare but real in all formats.
/// - NoResult: abandoned/washed out - the match was never completed at all.
/// The previous three-boolean RecordResult signature could not express Draw or Tie, so a
/// drawn first-class match silently incremented Played and nothing else, despite
/// FirstClassChampionship already existing as a CompetitionStructureType.
/// </summary>
public enum MatchOutcome
{
    Win,
    Loss,
    Draw,
    Tie,
    NoResult
}
