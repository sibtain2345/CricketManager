namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Phase 7, Slice 7.8: the national-board layer, distinct from a club board (<see cref="ClubBoard"/>).
///
/// A national side is not run like a club. Selection goes through a panel with a chairman of
/// selectors; the board's expectations are framed around global events (a World Cup, a WTC
/// final) rather than a domestic table; and the whole thing is more political - a national
/// coach is under far more scrutiny than a domestic one for the same results.
///
/// Only meaningful on a Team with IsNational = true. Every field has a plausible default so a
/// national team seeded before this existed still behaves sensibly.
/// </summary>
public sealed class NationalBoard
{
    /// <summary>0-100. How good the chairman of selectors is - a sharp panel reads current form and conditions well; a weak one picks on reputation and old runs. Feeds SelectionPanelService's estimate-vs-truth on the national pool.</summary>
    public double ChairmanOfSelectorsQuality { get; set; } = 55;

    /// <summary>How many selectors sit on the panel. A bigger panel is steadier (less prone to one selector's blind spot) but slower to back a bolt from the blue.</summary>
    public int PanelSize { get; set; } = 3;

    /// <summary>
    /// 0-100. How much politics, region and reputation weigh against pure merit in national
    /// selection - and how quickly the board reaches for the coach after a tournament exit.
    /// A high number is a board that sacks a coach for a quarter-final loss and picks a
    /// crowd-favourite past his best.
    /// </summary>
    public double Politicisation { get; set; } = 45;

    /// <summary>
    /// The scrutiny multiplier a national coaching job carries over a domestic one - applied on top
    /// of CoachCareerService.ScrutinyFactor. A national coach's every result is a headline.
    /// </summary>
    public double CoachScrutinyMultiplier() => Math.Clamp(1.25 + Politicisation / 100.0 * 0.5, 1.1, 1.9);

    /// <summary>
    /// Post-Phase-16 completion pass (§11.3): the board's ambition - the STRUCTURED objective the
    /// coach is judged against at a major. ~85 (a powerhouse expects the final / the trophy), ~55
    /// (a settled side expects a semi), ~35 (a developing side expects to be competitive). The
    /// NationalBoardVerdictService bar scales with it, and it names the target in the verdict.
    /// </summary>
    public double Ambition { get; set; } = 55;

    /// <summary>The placing (1 = won, 0 = last) the board expects at a MAJOR this year, from its ambition.</summary>
    public double ExpectedMajorPlacing => Math.Clamp(0.35 + Ambition / 100.0 * 0.55, 0.3, 0.92);

    /// <summary>Plain-English name for the target, for the verdict / an objectives-set news line.</summary>
    public string MajorTargetDescription => Ambition switch
    {
        >= 80 => "win it, or at the very least reach the final",
        >= 60 => "reach the final",
        >= 45 => "reach the semi-finals",
        _ => "be genuinely competitive and get out of the group"
    };

    /// <summary>
    /// Meeting-driven-selection ticket, the Outvoted-consequences fold-in: how many SelectionMeeting
    /// meetings IN A ROW have seen the panel majority overruled on a contested slot. Reset to 0 the
    /// moment a meeting is NOT outvoted. Crossing a threshold is a real trust cost for the chairman
    /// and a real story - see AiClubManagementService's handling of SelectionMeetingReport.Outvoted.
    /// </summary>
    public int ConsecutiveOutvotes { get; set; }
}
