using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Entities;

/// <summary>
/// Phase 7, Slice 7.9: a match official, with a real profile and a real career. No DRS - that is
/// a separate, later conversation - so an on-field umpire's decision genuinely stands, and how
/// good he is genuinely matters.
///
/// Attributes are on the familiar 1-20 scale. They drive two things:
/// - <b>UmpireService's per-match effect</b>: a poorer, more rattled panel gives more of the
///   marginal LBWs and caught-behinds, and leans a shade toward the home side when the crowd is
///   loud - which is exactly what a batting side fears about an inexperienced umpire.
/// - <b>a career</b>: reputation moves on decision quality over a season, and umpires are
///   promoted and demoted between panels, and eventually retire.
/// </summary>
public sealed class Umpire
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}";
    public string Nationality { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }

    public UmpirePanel Panel { get; set; } = UmpirePanel.Domestic;

    /// <summary>1-20. Raw decision accuracy on a normal delivery - reading an edge, judging a line.</summary>
    public int Accuracy { get; set; } = 12;

    /// <summary>1-20. How consistent that accuracy is - a low-consistency umpire has good days and shockers.</summary>
    public int Consistency { get; set; } = 12;

    /// <summary>1-20. How well he holds up under a big crowd and a tight finish - a low-composure umpire's error rate climbs exactly when it matters, and his home-side lean widens.</summary>
    public int Composure { get; set; } = 12;

    /// <summary>1-20. Specifically his LBW judgement - height, line, whether it pitched. The single most second-guessed call an on-field umpire makes.</summary>
    public int LbwJudgement { get; set; } = 12;

    /// <summary>Matches officiated. Sharpens judgement on a saturating curve, like every other experience curve in this codebase.</summary>
    public int MatchesOfficiated { get; set; }

    /// <summary>0-100, starts modest. His standing - what gets him the elite-panel appointments and the World Cup final.</summary>
    public double Reputation { get; set; } = 40;

    // ---- Section A: a queryable career record, player-adjacent in richness ----

    /// <summary>Matches officiated in each format, so a profile can show "80 Tests, 210 ODIs, 240 T20Is".</summary>
    public Dictionary<MatchFormat, int> MatchesByFormat { get; set; } = new();

    /// <summary>Marginal calls he has faced across his career, and how many he got clearly wrong - the raw material of a career-accuracy trend.</summary>
    public int CareerMarginalCalls { get; set; }
    public int CareerHowlers { get; set; }

    /// <summary>Big-match decisions that drew genuine controversy - the "notable controversies" line on a profile.</summary>
    public int NotableControversies { get; set; }

    /// <summary>The panels he has sat on, in order - a promotion/demotion history.</summary>
    public List<UmpirePanel> PanelHistory { get; set; } = new();

    /// <summary>
    /// Phase 15 (§16.3): matches stood since his last real break. Umpires tire like everyone else -
    /// a run of consecutive matches with no rest sharpens the error rate and widens the home lean.
    /// Reset by <see cref="Services.UmpireService.SeasonReview"/>.
    /// </summary>
    public int MatchesSinceBreak { get; set; }

    /// <summary>
    /// Phase 15 (§16.4): a genuine howler in a decider draws a board query, and he is quietly kept
    /// off the big appointments until this date passes. Null (the default) = not under scrutiny.
    /// </summary>
    public DateOnly? UnderScrutinyUntil { get; set; }

    public bool IsUnderScrutiny(DateOnly asOf) => UnderScrutinyUntil is { } d && d > asOf;

    /// <summary>0-100. His career decision accuracy on marginal calls. Returns a neutral 75 until he has faced a real sample.</summary>
    public double CareerAccuracy => CareerMarginalCalls < 5
        ? 75
        : Math.Round((1 - CareerHowlers / (double)CareerMarginalCalls) * 100, 1);

    public bool IsRetired { get; set; }

    public int Age(DateOnly asOf) => asOf.Year - DateOfBirth.Year - (asOf.DayOfYear < DateOfBirth.DayOfYear ? 1 : 0);

    /// <summary>
    /// 0-1. His effective decision-making quality once experience is folded in. The number
    /// UmpireService actually works from.
    /// </summary>
    public double EffectiveJudgement
    {
        get
        {
            double raw = (Accuracy * 0.35 + LbwJudgement * 0.30 + Consistency * 0.20 + Composure * 0.15) / 20.0;
            double experience = (1 - Math.Exp(-MatchesOfficiated / 60.0));
            return Math.Clamp(raw * 0.82 + experience * 0.18, 0, 1);
        }
    }
}
