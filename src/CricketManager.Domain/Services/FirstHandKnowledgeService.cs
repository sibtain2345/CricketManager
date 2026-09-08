using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Meeting-driven-selection ticket, requirement C: first-hand knowledge in auction bidding.
///
/// A coach, captain or team-mate who has genuinely shared a dressing room with a player has a real
/// read on him that pure stats and reputation do not carry - the cited real example being a
/// domestic captain recommending an uncapped countryman he has played the domestic circuit with.
/// This is deliberately a CONFIDENCE lever on top of <c>ScoutingDeptQuality</c>'s existing
/// name-hype discount, not a second valuation system: it lets a franchise back a lesser-known
/// player it actually knows, harder than the numbers alone would justify.
///
/// It reads <see cref="Player.CareerTeamIds"/> / <see cref="Coach.CareerTeamIds"/> - the set of
/// teams each has genuinely been part of - and asks whether they overlap. It is gated OFF for a
/// globally reputation-known player: everyone "knows" him, so nobody's first-hand read is an edge.
///
/// Cross-nationality falls out for free: a foreign campaign coach whose own career never crossed
/// the candidate's contributes nothing, while a retained domestic core that has shared the
/// candidate's national pool contributes a real read - which is exactly the asymmetry the ticket
/// asks for.
///
/// Fully deterministic - HashSet overlap and counts only, no RNG, no ordering.
/// </summary>
public sealed class FirstHandKnowledgeService
{
    /// <summary>Above this worldwide reputation, a first-hand read is no edge - the whole auction already knows him.</summary>
    public const double GlobalFameThreshold = 55;

    /// <summary>
    /// 0..1 - how strong a first-hand read <paramref name="franchise"/> has on
    /// <paramref name="candidate"/>, from the shared career history of its retained core, its
    /// captain, and its head coach (who, since the corrections pass, is a genuine year-round
    /// appointment in post well before the auction). Returns 0 for a globally-known player.
    ///
    /// Takes a <paramref name="playersById"/> lookup rather than the raw <see cref="WorldState"/> -
    /// resolving the retained core against the whole player list with a linear scan per squad
    /// member was the same anti-pattern this session's ticket fixed elsewhere. The auction service
    /// builds this dictionary once and threads it through.
    /// </summary>
    public double ReadStrength(IReadOnlyDictionary<Guid, Player> playersById, Team franchise, Player candidate, Coach? coach,
        IReadOnlyCollection<Guid>? sisterFranchiseTeamIds = null)
    {
        if (candidate.Reputation.Worldwide >= GlobalFameThreshold) return 0;
        if (candidate.CareerTeamIds.Count == 0) return 0;

        var known = candidate.CareerTeamIds;

        // S1: a SISTER FRANCHISE in the same ownership group has watched him too - a group runs
        // shared scouting and development, so a player the group already knows carries a real,
        // if lesser, read into any of its auctions.
        double sisterRead = sisterFranchiseTeamIds is { Count: > 0 } && known.Overlaps(sisterFranchiseTeamIds)
            ? 0.22 : 0;

        // The retained core (plus anyone bought so far this auction) - the "team-mates who've
        // played with him" signal. A core that has genuinely crossed paths with the candidate,
        // in a club or a national pool, is a real read.
        var core = franchise.SquadPlayerIds
            .Select(id => playersById.GetValueOrDefault(id))
            .Where(p => p is not null).Select(p => p!)
            .ToList();

        int overlapping = core.Count(p => p.CareerTeamIds.Overlaps(known));
        double coreRead = core.Count == 0 ? 0 : Math.Clamp(overlapping / (double)Math.Max(3, core.Count) * 1.6, 0, 0.85);

        // The captain's word carries more weight than a squad player's.
        double captainRead = 0;
        var captainId = franchise.GetCaptain(MatchFormat.T20);
        if (captainId is { } cid && core.FirstOrDefault(p => p.Id == cid) is { } captain
            && captain.CareerTeamIds.Overlaps(known))
            captainRead = 0.35;

        // The head coach's own read - scaled by how good a judge of a player he is.
        double coachRead = 0;
        if (coach is not null && !coach.IsRetired && coach.CareerTeamIds.Overlaps(known))
            coachRead = 0.30 * Common.AbilityScale.AttributeToHundred(coach.Attributes.Scouting) / 100.0;

        return Math.Clamp(coreRead + captainRead + coachRead + sisterRead, 0, 1);
    }
}
