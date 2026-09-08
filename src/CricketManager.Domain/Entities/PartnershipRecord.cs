using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Entities;

/// <summary>
/// One completed (or unbroken) batting partnership, kept with its full context - the TEAM-side
/// counterpart's granular sibling to BattingInningsRecord/BowlingSpellRecord/TeamInningsRecord.
///
/// Written once per stand by the match engine (Phase 4, Slice 13), from
/// <see cref="ValueObjects.InningsState.Partnerships"/> - which was already tracking every stand
/// as it happened and closing it correctly (see <see cref="Services.MatchRecorder"/> for the
/// conversion), but nothing persisted it anywhere a "record partnership" query could read.
///
/// <see cref="WicketNumber"/> needs a word of warning, because it is the one field in this
/// conversion that is easy to get backwards for the LAST partnership of an innings. Every OTHER
/// wicket number is simply "which wicket ended this stand" - straightforward, and exactly what
/// <see cref="ValueObjects.Partnership.WicketNumber"/> already carries. But when a stand is
/// UNBROKEN (the innings ended by overs, target or declaration, not by this pair being
/// separated), <see cref="ValueObjects.Partnership.WicketNumber"/> holds the CURRENT wicket
/// count, which is one short of the conventional label - three wickets down with the not-out
/// pair batting together is an unbroken FOURTH-wicket stand, not a third-wicket one.
/// <see cref="Unbroken"/> exists precisely so a caller doing this conversion applies the +1 only
/// where it belongs, rather than either always or never applying it.
/// </summary>
public sealed class PartnershipRecord
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
    public int Season { get; init; }

    /// <summary>1-4, same convention as TeamInningsRecord - which of the match's innings this partnership was part of.</summary>
    public int InningsNumber { get; init; } = 1;

    /// <summary>The conventional cricket label - "1st wicket", "4th wicket" and so on. See the class doc for why an unbroken stand needs +1 applied at conversion time, not read directly off the live Partnership.</summary>
    public int WicketNumber { get; init; }

    public Guid BatterAId { get; init; }
    public string BatterAName { get; init; } = string.Empty;
    public Guid BatterBId { get; init; }
    public string BatterBName { get; init; } = string.Empty;

    public int Runs { get; init; }
    public int Balls { get; init; }

    /// <summary>True when the innings ended (overs exhausted, target reached, declared) before this pair was separated by a wicket - they went to the pavilion together, not out.</summary>
    public bool Unbroken { get; init; }

    public double RunRate => Balls == 0 ? 0 : (double)Runs / Balls * 6;
}
