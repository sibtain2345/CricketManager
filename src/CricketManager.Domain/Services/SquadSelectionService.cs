using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>Whether an announced squad is fit to travel, and everything worth knowing about why - never just a pass/fail bit, since most of what Â§3 asks for is a genuine judgement call, not a hard rule.</summary>
public sealed record SquadCompositionReport(bool IsValid, IReadOnlyList<string> Notes);

/// <summary>
/// Squad-selection spec Â§3: a squad has to be balanced, not just "the best N individuals" - this
/// validates an announced squad against the concrete rules the spec states, and separately flags
/// the softer "role redundancy" principle it asks for.
/// </summary>
public sealed class SquadSelectionService
{
    private readonly PlayerSelectionEvaluator _evaluator = new();

    /// <summary>
    /// A player rates as a "credible backup" in a role once he clears this bar on the SAME
    /// discipline-aware scoring XiSelectionService itself uses (Evaluate(forBowling:...)) -
    /// reused rather than reinvented. 55/100 is deliberately modest: a credible backup does not
    /// have to be nearly as good as the incumbent, only good enough that his side does not fall
    /// apart if he has to play - the Scott Boland case the spec names explicitly.
    /// </summary>
    private const double CredibleBackupThreshold = 55;

    public SquadCompositionReport ValidateComposition(
        SquadAnnouncement squad,
        IReadOnlyDictionary<Guid, Player> playersById,
        MatchFormat format,
        bool isAwayOrTournament)
    {
        var notes = new List<string>();
        bool isValid = true;

        var players = squad.PlayerIds.Where(playersById.ContainsKey).Select(id => playersById[id]).ToList();

        // --- Tournament cap: a hard rule, not a judgement call. ---
        if (squad.PlayerCap is int cap && players.Count > cap)
        {
            notes.Add($"Squad has {players.Count} players, over its {cap}-player tournament cap - not a legal squad.");
            isValid = false;
        }

        // --- Wicketkeeper (Â§3): at least one specialist is the real floor everywhere. Away/
        // tournament squads additionally WANT a second, occasional-capable option as a backup -
        // that is a soft recommendation (a coach can reasonably travel with one and lean on the
        // mid-tour reserve system if it goes wrong), never a hard violation. ---
        int keeperCount = players.Count(p => p.PrimaryRole == PlayerRole.WicketKeeper);
        if (keeperCount == 0)
        {
            notes.Add("No specialist wicketkeeper in the squad at all - every squad needs at least one.");
            isValid = false;
        }
        else if (keeperCount == 1 && isAwayOrTournament)
        {
            notes.Add("Only one specialist wicketkeeper for an away series/tournament - travelling without cover for a keeper injury is a real risk, though not itself illegal.");
        }

        // --- Bowling depth for the whole squad (not just one XI): a squad needs genuine
        // rotation headroom above the per-match floor XiSelectionService already enforces, or
        // the same attack is bowling every single match of the series/tour. ---
        int squadBowlingFloor = XiSelectionService.BowlingFloor(format) + 2;
        int bowlingCapable = players.Count(p => p.BowlingRole != BowlingRoleType.NotABowler);
        if (bowlingCapable < squadBowlingFloor)
            notes.Add($"Only {bowlingCapable} bowling-capable players in the squad, short of the {squadBowlingFloor} that give real rotation headroom across a series - the same attack will be bowled into the ground.");

        // --- Role redundancy (Â§3): for each broad role group actually represented in the
        // squad, is there more than one CREDIBLE option - not a clone, just capable. Flags a
        // genuine single point of failure without pretending to model exact position-by-
        // position combinations. ---
        var weights = SelectionWeighting.Balanced;
        double Score(Player p, bool bowling) => _evaluator.Evaluate(p, format, weights, forBowling: bowling).TotalScore;

        var battersAndAllrounders = players.Where(p => p.PrimaryRole is PlayerRole.Batsman or PlayerRole.BattingAllrounder or PlayerRole.WicketKeeper).ToList();
        int credibleBatters = battersAndAllrounders.Count(p => Score(p, bowling: false) >= CredibleBackupThreshold);
        if (battersAndAllrounders.Count > 0 && credibleBatters <= 1)
            notes.Add("Only one credible batting option in the whole squad - a single injury or loss of form leaves no real backup, not just a weaker one.");

        var bowlersAndAllrounders = players.Where(p => p.BowlingRole != BowlingRoleType.NotABowler).ToList();
        int credibleBowlers = bowlersAndAllrounders.Count(p => Score(p, bowling: true) >= CredibleBackupThreshold);
        if (bowlersAndAllrounders.Count > 0 && credibleBowlers <= 1)
            notes.Add("Only one credible bowling option in the whole squad - the same single point of failure, on the bowling side.");

        int credibleSpinners = bowlersAndAllrounders.Count(p => BallOutcomeModel.IsSpinner(p) && Score(p, bowling: true) >= CredibleBackupThreshold);
        if (bowlersAndAllrounders.Any(BallOutcomeModel.IsSpinner) && credibleSpinners <= 1)
            notes.Add("Only one credible spin option - no cover if conditions demand two, or if the frontline spinner breaks down.");

        return new SquadCompositionReport(isValid, notes);
    }
}
