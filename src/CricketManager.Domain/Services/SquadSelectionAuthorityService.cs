using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>One captain's proposal to add a player to the squad, and what came of it. AcceptedIntoConsideration is true either when a human coach will actually see it (his own call, not this evaluation's) or when an AI coach genuinely judged it reasonable - never when a player is silently added to a squad.</summary>
public sealed record SquadSelectionProposal(Guid ProposedPlayerId, bool AcceptedIntoConsideration, string Explanation);

/// <summary>
/// Squad-selection spec Â§4: final squad-selection authority always rests with the coach, but a
/// captain's read of team need is a real, legitimate signal that has to reach the decision one
/// way or another. Mirrors the human-vs-AI split this codebase already established for in-match
/// decisions (DecisionAuthority/CaptaincyService), applied to the squad-selection moment instead:
/// - **Human coach**: the proposal always reaches him, unweighed - an algorithm silently
///   accepting or rejecting a suggestion on a human coach's behalf would be exactly the
///   coach-authority violation Section 6d's DecisionAuthority work was built to prevent.
/// - **AI coach**: genuinely evaluated - accepted when the proposed player rates within a real
///   margin of the squad's own weakest comparable option, because a captain's read of team need
///   is worth something in its own right, not noise to be overridden by a strict ranking.
///   Rejected when it is not close, however well-intentioned the request.
///
/// Deliberately returns a PROPOSAL, not a mutation - nothing here adds a player to a
/// SquadAnnouncement directly. The caller (whoever owns the actual squad-announcement moment)
/// decides what an accepted proposal means for the final list; conflating "reasonable" with
/// "in the squad" would take the decision away from the coach exactly where the spec insists it
/// has to stay.
/// </summary>
public sealed class SquadSelectionAuthorityService
{
    private readonly PlayerSelectionEvaluator _evaluator = new();

    /// <summary>0-100 scale: how far below the squad's weakest comparable option a captain's request can still be judged reasonable by an AI coach. Deliberately real but modest - a captain's read of team need earns genuine benefit of the doubt, not a free pass for an unreasonable request.</summary>
    private const double ReasonablenessMargin = 8.0;

    public SquadSelectionProposal EvaluateProposal(
        Player proposedPlayer,
        IReadOnlyList<Player> currentSquad,
        MatchFormat format,
        bool coachIsHuman,
        SelectionWeighting? weighting = null)
    {
        var weights = weighting ?? SelectionWeighting.Balanced;
        bool bowlingDiscipline = proposedPlayer.PrimaryRole == PlayerRole.Bowler;
        double proposedScore = _evaluator.Evaluate(proposedPlayer, format, weights, forBowling: bowlingDiscipline).TotalScore;

        if (coachIsHuman)
            return new SquadSelectionProposal(proposedPlayer.Id, true,
                $"{proposedPlayer.FullName} (rated {proposedScore:F0}/100 for {format}) is surfaced to the coach as the captain's suggestion - advisory only, the human coach has final say.");

        var comparableGroup = currentSquad.Where(p => SameBroadRole(p, proposedPlayer)).ToList();
        var weakestComparable = comparableGroup.Count == 0
            ? (double?)null
            : comparableGroup.Min(p => _evaluator.Evaluate(p, format, weights, forBowling: bowlingDiscipline).TotalScore);

        bool reasonable = weakestComparable is not { } weakest || proposedScore >= weakest - ReasonablenessMargin;

        string explanation = weakestComparable is { } w
            ? (reasonable
                ? $"Accepted into consideration - {proposedPlayer.FullName} ({proposedScore:F0}/100) is a reasonable request, close to the squad's own weakest comparable option ({w:F0})."
                : $"Rejected - {proposedPlayer.FullName} ({proposedScore:F0}/100) falls well short of the squad's weakest comparable option ({w:F0}); the coach's final call stands.")
            : $"Accepted into consideration - {proposedPlayer.FullName} ({proposedScore:F0}/100) is the squad's first player in this broad role, so there is nothing to compare him against yet.";

        return new SquadSelectionProposal(proposedPlayer.Id, reasonable, explanation);
    }

    /// <summary>Keeper/keeper, bowling-capable/bowling-capable, pure-batter/pure-batter - the same broad grouping XiSelectionService's own contested-slot check uses, kept consistent rather than inventing a second taxonomy.</summary>
    private static bool SameBroadRole(Player a, Player b)
    {
        bool aKeeper = a.PrimaryRole == PlayerRole.WicketKeeper, bKeeper = b.PrimaryRole == PlayerRole.WicketKeeper;
        if (aKeeper || bKeeper) return aKeeper == bKeeper;

        bool aBowls = a.BowlingRole != BowlingRoleType.NotABowler, bBowls = b.BowlingRole != BowlingRoleType.NotABowler;
        return aBowls == bBowls;
    }
}
