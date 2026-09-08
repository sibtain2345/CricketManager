using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 9: the shared "what is this squad short of, and would this player help" logic the
/// free-agent market, the transfer market and the auction all need. Deliberately a small static
/// helper rather than three near-identical private copies - the same reasoning
/// SpecialistStaffService's own thinnest-group check uses, generalised.
/// </summary>
public static class SquadNeeds
{
    public readonly record struct RoleGroup(string Label, Func<Player, bool> Matches, int TargetDepth);

    private static readonly PlayerSelectionEvaluator Evaluator = new();

    public static IReadOnlyList<RoleGroup> Groups() => new[]
    {
        new RoleGroup("front-line pace", p => p.PrimaryRole == PlayerRole.Bowler && !BallOutcomeModel.IsSpinner(p), 5),
        new RoleGroup("front-line spin", p => p.PrimaryRole == PlayerRole.Bowler && BallOutcomeModel.IsSpinner(p), 3),
        new RoleGroup("top-order batting", p => p.BattingRole is BattingRole.Opener or BattingRole.TopOrder
            && p.PrimaryRole is PlayerRole.Batsman or PlayerRole.BattingAllrounder, 4),
        new RoleGroup("middle-order batting", p => p.BattingRole is BattingRole.MiddleOrder
            && p.PrimaryRole is PlayerRole.Batsman or PlayerRole.BattingAllrounder or PlayerRole.WicketKeeper, 4),
        new RoleGroup("all-round balance", p => p.PrimaryRole is PlayerRole.BattingAllrounder or PlayerRole.BowlingAllrounder, 3),
        new RoleGroup("wicketkeeping", p => p.PrimaryRole == PlayerRole.WicketKeeper, 2),
    };

    /// <summary>
    /// The role group this squad is shortest in RELATIVE to its target depth, or null when every
    /// group is adequately covered. Also returns the weakest current player's score in that group,
    /// which is the bar an incoming signing has to clear to be an upgrade.
    /// </summary>
    public static (RoleGroup Group, double WeakestScore)? WeakestGroup(
        IReadOnlyList<Player> squad, MatchFormat format, double minShortfall = 0)
    {
        (RoleGroup Group, double Deficit, double Weakest)? worst = null;
        foreach (var g in Groups())
        {
            var members = squad.Where(p => !p.IsRetired && g.Matches(p)).ToList();
            double deficit = g.TargetDepth - members.Count;
            if (deficit <= minShortfall) continue;
            double weakest = members.Count == 0 ? 0
                : members.Min(p => ScoreFor(p, format));
            if (worst is null || deficit > worst.Value.Deficit)
                worst = (g, deficit, weakest);
        }
        return worst is null ? null : (worst.Value.Group, worst.Value.Weakest);
    }

    public static double ScoreFor(Player p, MatchFormat format)
    {
        bool bowlingPrimary = p.PrimaryRole == PlayerRole.Bowler
            || (p.PrimaryRole == PlayerRole.BowlingAllrounder);
        double a = Evaluator.Evaluate(p, format, forBowling: bowlingPrimary).TotalScore;
        if (p.PrimaryRole is PlayerRole.BattingAllrounder or PlayerRole.BowlingAllrounder)
            a = Math.Max(a, Evaluator.Evaluate(p, format, forBowling: !bowlingPrimary).TotalScore);
        return a;
    }

    /// <summary>A rough overall quality score for a player, format-agnostic - used to rank an auction/free-agent pool.</summary>
    public static double OverallScore(Player p) =>
        AbilityScale.CompositeAbilityToHundred(p.CurrentAbility)
        + Math.Max(p.Reputation.Domestic, p.Reputation.Continental) * 0.2
        + p.Form.CurrentForm * 0.1;

    /// <summary>Can a club take on this annual wage without breaking the bank - a rough gate on the season budget and the cash in hand.</summary>
    public static bool CanCarryWage(Team team, double annualWage) =>
        annualWage <= Math.Max(team.Board.SeasonBudget * 0.35, 200_000)
        && !team.Board.UnderTransferEmbargo;

    /// <summary>
    /// §9.8: does adding this wage keep the club under its primary competition's salary cap? A
    /// competition with no cap (the default) always returns true. The existing wage bill is read
    /// from the world's contract rows.
    /// </summary>
    public static bool WithinSalaryCap(WorldState world, Team team, double extraAnnualWage)
    {
        var cappedComp = world.Competitions.FirstOrDefault(c => c.SalaryCap is > 0
            && world.CompetitionSeasons.Any(s => s.CompetitionId == c.Id && s.ParticipatingTeamIds.Contains(team.Id)));
        if (cappedComp?.SalaryCap is not { } cap) return true;

        double currentBill = world.PlayerContracts
            .Where(pc => pc.TeamId == team.Id && pc.Status == Enums.ContractStatus.Active && pc.Kind == Enums.ContractKind.Domestic)
            .Sum(pc => pc.AnnualWage);
        return currentBill + extraAnnualWage <= cap;
    }

    /// <summary>Can a club find a transfer fee - the transfer budget share of the season budget, and not deep in the red.</summary>
    public static bool CanAffordFee(Team team, double fee) =>
        !team.Board.UnderTransferEmbargo
        && fee <= team.Board.SeasonBudget * team.Board.TransferBudgetShare * 1.1
        && team.Finances.Budget - fee > -3_000_000;
}
