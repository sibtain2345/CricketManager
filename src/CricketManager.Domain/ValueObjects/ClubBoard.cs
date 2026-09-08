using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>Phase 12 (§13.2): what the boardroom actually cares about.</summary>
public enum ChairmanAgenda
{
    /// <summary>A steady hand - results and finances weighed evenly.</summary>
    Balanced,
    /// <summary>Silverware now - a generous budget, but a par finish is a failure and patience is short.</summary>
    WinNow,
    /// <summary>Build from within - graduating and blooding academy players is credited, and a lean results year is forgiven if the pipeline is filling.</summary>
    YouthAndAcademy,
    /// <summary>Balance the books - the budget is cut, a profit is rewarded, and a big signing had better pay off.</summary>
    Austerity,
    /// <summary>Standing and profile - the board wants a marquee name, a growing fanbase and a rising reputation.</summary>
    Prestige
}

/// <summary>Phase 12 (§13.1): the multi-year arc a private/corporate owner is on.</summary>
public enum OwnerTrajectory
{
    Stable,
    /// <summary>Fresh money coming in - wealth and budget climb year on year.</summary>
    InvestingHeavily,
    /// <summary>Interest waning - wealth and budget drift down, and a sale becomes likelier.</summary>
    WindingDown,
    /// <summary>Actively on the market - the club will be sold if a credible buyer appears.</summary>
    SeekingSale
}

/// <summary>
/// Phase 7, Slice 7.3: the board as a persistent actor, not just a BoardConfidence number.
///
/// Before this, Team.BoardConfidence moved on results and nothing else. A board is more than
/// its current mood: it has an <see cref="Ambition"/> (how high it sets the bar), a
/// <see cref="Patience"/> (how long it gives a coach before the bar starts to bite), a
/// <see cref="Wealth"/> (how much it can and will spend), an ownership model that shapes all
/// three, and a <see cref="FanSentiment"/> that moves slower than reputation and feeds board
/// patience and takeover interest.
///
/// Every field has a sensible default so every existing Team gets a plausible board with no
/// seeder change - the same "present now, doing something real, stated not implied" discipline
/// the rest of the codebase uses.
/// </summary>
public sealed class ClubBoard
{
    /// <summary>0-100. How high the board sets the bar. High ambition + a par season slowly erodes a coach's standing; low ambition forgives a lot.</summary>
    public double Ambition { get; set; } = 55;

    /// <summary>0-100. How long a coach gets before ambition starts to bite. A patient board rides out a bad season; an impatient one does not.</summary>
    public double Patience { get; set; } = 55;

    /// <summary>0-100. How deep the pockets are - drives the season budget the board hands the coach and how big a wage bill the club can carry.</summary>
    public double Wealth { get; set; } = 50;

    public OwnershipModel Ownership { get; set; } = OwnershipModel.Association;

    /// <summary>
    /// 0-100, 50 neutral. The fanbase's mood - a slower-moving number than Team.Reputation
    /// (standing) or Team.Morale (the dressing room). Rises on results and trophies, falls on a
    /// slump or a sale of a favourite; feeds board patience and whether a takeover looks attractive.
    /// </summary>
    public double FanSentiment { get; set; } = 55;

    /// <summary>
    /// Post-Phase-16 completion pass (§10.2): the club's paid-up membership / season-ticket base -
    /// a number of supporters, roughly. It is a real, fairly stable revenue stream (a per-head
    /// membership fee each year), and it grows or shrinks slowly with fan sentiment. 0 (the
    /// default) = not modelled, so a pre-existing club's finances are unchanged. WorldSeeder sets
    /// it from ground capacity + reputation.
    /// </summary>
    public double MembershipBase { get; set; }

    /// <summary>
    /// The budget the board has set the coach for the coming season - what BoardService.SetSeasonBudget
    /// computed from Wealth, last season's finances and ambition. 0 until a board has set one.
    /// </summary>
    public double SeasonBudget { get; set; }

    /// <summary>How much of the season budget is earmarked for the transfer/recruitment market vs wages. 0.5 = an even split. Phase 9 replaces the stub this feeds.</summary>
    public double TransferBudgetShare { get; set; } = 0.4;

    /// <summary>
    /// Phase 7 (financial fair play): true while the club is under a spending / registration
    /// embargo for running a sustained large deficit. Set and lifted by FinancialFairPlayService.
    /// Phase 9's transfer market reads this to block incoming signings; today it is a flag with a
    /// news/board consequence and no market to bite yet, stated rather than implied.
    /// </summary>
    public bool UnderTransferEmbargo { get; set; }

    /// <summary>The date the embargo is reviewed - lifted early if the books recover.</summary>
    public DateOnly? EmbargoUntil { get; set; }

    /// <summary>
    /// Phase 12 (§13.2): the chairman's agenda - what the boardroom actually cares about, on top of
    /// the raw results a coach delivers. It colours the season budget, how a par finish is judged,
    /// and which structured objectives the board sets. Seeded per club (RNG-free); can change when a
    /// boardroom coup or a takeover installs a new chairman.
    /// </summary>
    public ChairmanAgenda Agenda { get; set; } = ChairmanAgenda.Balanced;

    /// <summary>
    /// Phase 12 (§13.2): 0-100, 100 = a completely united board. A divided board (low unity) acts
    /// erratically - budgets swing, the coach's job security is jumpier - and a sustained split
    /// tends to end in a boardroom change. Moved by results, finances and governance events.
    /// </summary>
    public double BoardUnity { get; set; } = 70;

    /// <summary>
    /// Phase 12 (§13.1): the owner's trajectory - the multi-year arc a private/corporate owner is
    /// on. Drives whether the wealth number and the budget are climbing, holding or being wound
    /// down, and how open the club is to a takeover.
    /// </summary>
    public OwnerTrajectory Trajectory { get; set; } = OwnerTrajectory.Stable;

    public void MoveFanSentiment(double delta) => FanSentiment = Math.Clamp(FanSentiment + delta, 0, 100);

    /// <summary>How quick this board is to act on a collapse of confidence - low patience and a wealthy, results-driven owner make it quicker.</summary>
    public double Impatience()
    {
        double ownerFactor = Ownership switch
        {
            OwnershipModel.Corporate => 1.35,
            OwnershipModel.PrivateOwner => 1.15,
            OwnershipModel.Association => 1.0,
            OwnershipModel.MemberOwned => 0.75,
            _ => 1.0
        };
        return Math.Clamp((1 + (55 - Patience) / 55.0 * 0.5) * ownerFactor, 0.5, 2.2);
    }
}
