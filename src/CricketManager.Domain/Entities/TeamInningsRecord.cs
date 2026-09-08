using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Entities;

/// <summary>
/// One completed team innings. This is the TEAM-side counterpart to BattingInningsRecord/
/// BowlingSpellRecord, and the missing piece that made real ground statistics impossible:
/// a team total (351/6 in 50 overs) is not derivable from individual batting records,
/// because extras, retirements and unrecorded tail contributions mean the parts don't sum
/// to the whole. Cricinfo's ground pages are built on exactly this - innings totals, by
/// innings number, by format - so this is what GroundRecordsService reads.
///
/// Written once per innings by the match engine (Phase 4). Nothing populates it today,
/// which is why every records query is written to handle "no data yet" as its own answer
/// rather than returning a fabricated zero.
/// </summary>
public sealed class TeamInningsRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MatchId { get; init; }
    public DateOnly MatchDate { get; init; }

    public Guid GroundId { get; init; }
    public string GroundName { get; init; } = string.Empty;

    public Guid BattingTeamId { get; init; }
    public string BattingTeamName { get; init; } = string.Empty;
    public Guid BowlingTeamId { get; init; }
    public string BowlingTeamName { get; init; } = string.Empty;

    public MatchFormat Format { get; init; }
    public CompetitionScope Scope { get; init; } = CompetitionScope.International;
    public Guid CompetitionId { get; init; }
    public string CompetitionName { get; init; } = string.Empty;
    public int Season { get; init; }

    /// <summary>1-4. Innings 3/4 only occur in multi-day cricket; a chase is innings 2 in limited-overs and innings 4 in first-class.</summary>
    public int InningsNumber { get; init; } = 1;

    public int Runs { get; init; }
    public int Wickets { get; init; }
    public double Overs { get; init; }
    public int Extras { get; init; }

    /// <summary>True when the innings ended by declaration rather than by being bowled out or running out of overs.</summary>
    public bool Declared { get; init; }

    /// <summary>Result of the MATCH from this batting side's perspective - lets "highest successful chase" and "win% batting first at this ground" be answered without a separate match entity.</summary>
    public MatchOutcome MatchResultForBattingTeam { get; init; }

    /// <summary>An innings that ended with all wickets down. Distinguishes a genuine "lowest all-out total" from a side that was 40/2 when rain ended the game.</summary>
    public bool AllOut => Wickets >= 10;

    public double RunRate => Overs <= 0 ? 0 : Runs / Overs;
}
