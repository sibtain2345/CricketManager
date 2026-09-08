using System.Text.Json.Serialization;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// One team's points-table row for one CompetitionSeason.
///
/// NetRunRate is still zero until Phase 4's match engine can compute it from real runs/
/// overs, but it is now guarded by SetNetRunRate() like every other mutable field on this
/// class rather than being a bare public setter - the inconsistency was accidental, and a
/// bare setter on a derived statistic is exactly the kind of field that ends up hand-set
/// to a fabricated value somewhere.
/// </summary>
public sealed class CompetitionStanding
{
    public Guid TeamId { get; init; }

    /// <summary>Empty for a plain league table. Set for a GroupStageKnockout season so CompetitionProgressionService.GetGroupQualifiers can rank each group separately - see AssignGroup.</summary>
    [JsonInclude] public string GroupName { get; private set; } = string.Empty;

    // [JsonInclude] on every private setter here - System.Text.Json ignores private
    // setters by default, which silently reset these to 0 on every save/reload without
    // it (this was a real bug found and fixed on FormState/Reputation/PlayerCareerStats
    // earlier - applying the same fix proactively here instead of rediscovering it later).
    [JsonInclude] public int Played { get; private set; }
    [JsonInclude] public int Won { get; private set; }
    [JsonInclude] public int Lost { get; private set; }
    [JsonInclude] public int Drawn { get; private set; }    // first-class matches that ran out of time
    [JsonInclude] public int Tied { get; private set; }     // scores level in a completed match
    [JsonInclude] public int NoResult { get; private set; } // abandoned/washed out
    [JsonInclude] public int Points { get; private set; }
    [JsonInclude] public double NetRunRate { get; private set; }

    /// <summary>
    /// Records one match result. The points system is passed in per call rather than
    /// hardcoded because competitions genuinely differ (limited-overs 2-for-a-win vs a
    /// first-class trophy awarding points for a draw) - see CompetitionPointsSystem.
    /// bonusPoints defaults to 0 so every existing caller is unaffected - it exists for
    /// BonusPointsService's batting/bowling/margin points, computed separately and passed
    /// in here rather than this class reaching for match data itself.
    /// </summary>
    public void RecordResult(MatchOutcome outcome, CompetitionPointsSystem? pointsSystem = null, int bonusPoints = 0)
    {
        var points = pointsSystem ?? CompetitionPointsSystem.LimitedOvers;
        Played++;

        switch (outcome)
        {
            case MatchOutcome.Win:
                Won++;
                Points += points.Win;
                break;
            case MatchOutcome.Loss:
                Lost++;
                Points += points.Loss;
                break;
            case MatchOutcome.Draw:
                Drawn++;
                Points += points.Draw;
                break;
            case MatchOutcome.Tie:
                Tied++;
                Points += points.Tie;
                break;
            case MatchOutcome.NoResult:
                NoResult++;
                Points += points.NoResult;
                break;
        }

        Points += bonusPoints;
    }

    /// <summary>Set by the match engine (Phase 4) once real runs/overs data exists. Zero means "no data yet", not "genuinely 0.00".</summary>
    public void SetNetRunRate(double netRunRate) => NetRunRate = netRunRate;

    /// <summary>Phase 7 (financial fair play): a governing-body points penalty - deducted from this row. Tracked separately so a table page can show "-N (penalty)".</summary>
    [JsonInclude] public int PointsPenalty { get; private set; }

    public void ApplyPointsPenalty(int points)
    {
        int p = Math.Max(0, points);
        PointsPenalty += p;
        Points = Math.Max(0, Points - p);
    }

    /// <summary>Assigns this standing to a group, for a GroupStageKnockout season. Idempotent by design - CompetitionSeason.GetOrCreateStanding calls it every time a group-aware caller asks for a standing, so a team's group can be set the first time without needing a separate "was this already created" branch.</summary>
    public void AssignGroup(string groupName) => GroupName = groupName;
}

/// <summary>
/// A competition's points rules. Exists so different CompetitionStructureTypes can score
/// the same standings table differently without any of them hardcoding numbers - a
/// first-class trophy rewarding a hard-fought draw is normal cricket, not a special case.
///
/// Both bonus flags below are wired in as of Slice 13 (MatchRecorder for the limited-overs
/// margin bonus, MultiDayMatchRecorder for FC batting/bowling bonus points).
/// </summary>
public sealed class CompetitionPointsSystem
{
    public int Win { get; init; } = 2;
    public int Loss { get; init; }
    public int Draw { get; init; }
    public int Tie { get; init; } = 1;
    public int NoResult { get; init; } = 1;

    /// <summary>
    /// Whether a big-margin limited-overs win earns BonusPointsService.LimitedOversMarginBonus
    /// on top of the ordinary win points. Off by default - most leagues don't use this, and
    /// LimitedOvers/FirstClass below stay the plain systems they always were unless a caller
    /// opts in via LimitedOversWithMarginBonus (or a custom system with this set true).
    /// </summary>
    public bool MarginBonusEnabled { get; init; }

    /// <summary>
    /// Whether BonusPointsService's FC batting/bowling bonus points apply on top of the
    /// ordinary result points. On by default - unlike the limited-overs margin bonus above,
    /// real first-class competitions (Ranji Trophy, County Championship) almost universally
    /// use bonus points, so FirstClass below carries them unless a caller explicitly opts out
    /// for a simpler win/loss/draw-only system. Only consulted by MultiDayMatchRecorder - a
    /// LimitedOvers system carrying this flag has no effect, since nothing in that path reads it.
    /// </summary>
    public bool BattingBowlingBonusEnabled { get; init; } = true;

    /// <summary>Standard limited-overs: 2 for a win, 1 for a tie or no-result.</summary>
    public static CompetitionPointsSystem LimitedOvers => new();

    /// <summary>The same system as LimitedOvers, with the margin bonus switched on - for competitions that reward a big win with an extra point.</summary>
    public static CompetitionPointsSystem LimitedOversWithMarginBonus => new() { MarginBonusEnabled = true };

    /// <summary>
    /// Simplified first-class scoring: a win is worth substantially more than a draw, but a
    /// draw is worth real points - which is why FC sides will sometimes play for one. Carries
    /// batting/bowling bonus points by default (see BattingBowlingBonusEnabled) - pass
    /// `with { BattingBowlingBonusEnabled = false }` for a competition that doesn't use them.
    /// </summary>
    public static CompetitionPointsSystem FirstClass => new() { Win = 12, Loss = 0, Draw = 4, Tie = 6, NoResult = 4 };
}
