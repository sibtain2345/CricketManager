using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 11 (§5.1/§5.2/§5.11): the selection MEETING - the panel (and, for a national side, the
/// captain) don't just hand down a list, they argue the contested slots, produce a per-pick
/// rationale, and a weak or politicised panel's reasoning is visibly thin.
///
/// This does not CHOOSE the squad - AiClubManagementService.PickSquad / the national pool already
/// do - it explains one. The output is a <see cref="SelectionMeetingReport"/> that
/// AiClubManagementService turns into a <see cref="GameEventType.SquadAnnounced"/> news item: the
/// headline, a surprise pick, a big omission, the captain's comment, and (when the meeting was
/// poor) a dissent note.
/// </summary>
public sealed class SelectionMeetingService
{
    private readonly PlayerSelectionEvaluator _evaluator = new();

    /// <summary>
    /// How rigorous the meeting was, 15-95. A national panel's chairman-of-selectors quality
    /// (damped by politicisation); a domestic meeting leans on the head coach's own judgement.
    /// Extracted as its own public method (not just inlined in <see cref="Hold"/>) so
    /// <c>AiClubManagementService.PickSquad</c> can read the SAME number and apply a real,
    /// deterministic reputation-over-merit bias when it's weak - closing the gap where this
    /// service could narrate "picked on reputation" about a squad that was, in fact, picked on
    /// pure merit, because the two were previously entirely decoupled.
    /// </summary>
    public static double PanelQuality(Team team, Coach? coach) => team.IsNational
        ? Math.Clamp((team.NationalBoard?.ChairmanOfSelectorsQuality ?? 55) - (team.NationalBoard?.Politicisation ?? 45) * 0.35, 15, 95)
        : coach is null ? 50
        : Math.Clamp((coach.Attributes.DecisionMaking + coach.Attributes.ManManagement + coach.Attributes.MatchReading) / 3.0 * 5, 20, 92);

    public SelectionMeetingReport Hold(
        Team team, MatchFormat format, IReadOnlyList<Guid> chosenIds, IReadOnlyList<Player> candidatePool,
        DateOnly date, Coach? coach = null, CaptaincyProfile? captainProfile = null, Player? captain = null)
    {
        var chosen = new HashSet<Guid>(chosenIds);
        var ranked = _evaluator.Rank(candidatePool, format);
        var rankOf = ranked.Select((s, i) => (s.PlayerId, i)).ToDictionary(x => x.PlayerId, x => x.i);
        int squadSize = chosenIds.Count;

        double meetingQuality = PanelQuality(team, coach);

        var byId = candidatePool.ToDictionary(p => p.Id);
        string Name(Guid id) => byId.TryGetValue(id, out var p) ? p.FullName : "a player";

        // Surprise pick: someone chosen who ranked well OUTSIDE the squad size (a bolter).
        var surprise = chosenIds
            .Where(id => rankOf.TryGetValue(id, out var r) && r >= squadSize + Math.Max(3, squadSize / 3))
            .OrderByDescending(id => rankOf[id])
            .FirstOrDefault();

        // Big omission: someone NOT chosen who ranked comfortably inside the squad size.
        var omission = ranked
            .Where(s => !chosen.Contains(s.PlayerId))
            .Where(s => rankOf[s.PlayerId] < squadSize - Math.Max(1, squadSize / 4))
            .OrderBy(s => rankOf[s.PlayerId])
            .Select(s => (Guid?)s.PlayerId)
            .FirstOrDefault();

        // Contested slots: the weakest chosen and best omitted within a margin.
        var contested = new List<string>();
        Guid? firstContestedChosen = null;
        SelectionScore? firstContestedRival = null;
        var weakestChosen = chosenIds
            .Where(id => rankOf.ContainsKey(id))
            .OrderByDescending(id => rankOf[id]).Take(3).ToList();
        foreach (var wc in weakestChosen)
        {
            var rival = ranked.FirstOrDefault(s => !chosen.Contains(s.PlayerId));
            if (rival is null) break;
            double gap = _evaluator.Evaluate(byId[wc], format).TotalScore - rival.TotalScore;
            if (Math.Abs(gap) <= 5)
            {
                contested.Add($"{Name(wc)} vs {rival.PlayerName} for the last place");
                firstContestedChosen ??= wc;
                firstContestedRival ??= rival;
            }
        }

        // §5.2: the formal panel VOTE - not just a single dissent note. A panel is more than one
        // person; each seat reads the same contested gap through its own quality (deterministically
        // spread around the meeting's overall quality, no fresh RNG), and a genuine majority against
        // the actual pick is a real "outvoted" story, richer than "someone grumbled". Additive to the
        // dissent note below, which stays the always-present summary line.
        bool outvoted = false;
        int votesForChosen = 0, panelSize = 0;
        if (firstContestedChosen is { } fc && firstContestedRival is { } fr)
        {
            panelSize = team.IsNational ? Math.Clamp(team.NationalBoard?.PanelSize ?? 3, 3, 7) : 3;
            double realGap = _evaluator.Evaluate(byId[fc], format).TotalScore - fr.TotalScore;
            for (int i = 0; i < panelSize; i++)
            {
                // Each seat's own read is centred on the panel's overall quality, spread +-15 points
                // across the seats - a mix of sharper and duller selectors in the same room, not one
                // number repeated. A weaker seat leans toward reputation over the merit gap.
                double seatQuality = Math.Clamp(meetingQuality + (i - (panelSize - 1) / 2.0) * 15, 10, 98);
                double reputationPull = (byId[fc].Reputation.Domestic - byId[fr.PlayerId].Reputation.Domestic) * Math.Clamp((55 - seatQuality) / 55.0, 0, 1) * 0.3;
                if (realGap + reputationPull >= 0) votesForChosen++;
            }
            outvoted = votesForChosen * 2 < panelSize;
        }

        // Captain's comment + friction. A national captain who is formally on the panel gets a
        // weighted say; if the meeting was poor or he was outvoted on a contested slot, that shows.
        double captainStanding = captainProfile is not null && captain is not null
            ? captainProfile.EffectiveLeadership(captain) : 50;
        string captainComment = captain is null
            ? $"{team.Name} did not name a captain's view."
            : meetingQuality >= 60
                ? $"\"We're settled on this group and clear on roles,\" says {captain.FullName}."
                : $"\"A few calls could have gone either way,\" admits {captain.FullName}.";

        // §5.2: a genuine panel-vote outcome outranks the softer dissent notes below - being
        // outvoted by a real majority is a stronger, more specific story than "the rationale was
        // thin" or "someone pushed for a different name".
        string? dissent = null;
        if (outvoted && firstContestedChosen is { } outvotedChosen && firstContestedRival is { } outvotedRival)
            dissent = $"{Name(outvotedChosen)}'s place was carried on a split panel vote ({panelSize - votesForChosen} of {panelSize} favoured {outvotedRival.PlayerName}) - the final call overruled the room.";
        else if (meetingQuality < 42)
            dissent = "The rationale behind one or two selections was notably thin - the panel struggled to articulate a plan.";
        else if (contested.Count > 0 && captainStanding >= 55 && meetingQuality < 65)
            dissent = $"{captain?.FullName ?? "The captain"} is understood to have pushed for a different name in the final slot.";

        string headline = surprise != Guid.Empty
            ? $"{team.Name} spring a surprise, naming {Name(surprise)} for the {format} squad."
            : omission is { } om
                ? $"{team.Name} name their {format} squad - {Name(om)} the most notable absentee."
                : $"{team.Name} name a settled {format} squad.";

        var rationales = new List<(string, string)>();
        foreach (var id in chosenIds.Take(Math.Min(6, chosenIds.Count)))
        {
            if (!byId.TryGetValue(id, out var p)) continue;
            string why = meetingQuality >= 55
                ? RationaleFor(p, format)
                : "picked on reputation and recent runs";
            rationales.Add((p.FullName, why));
        }

        return new SelectionMeetingReport(
            headline,
            rationales,
            contested,
            dissent,
            surprise != Guid.Empty ? Name(surprise) : null,
            omission is { } o ? Name(o) : null,
            captainComment,
            Math.Round(meetingQuality, 0),
            outvoted, votesForChosen, panelSize,
            outvoted ? firstContestedChosen : null,
            outvoted ? firstContestedRival?.PlayerId : null);
    }

    private static string RationaleFor(Player p, MatchFormat format) => p.PrimaryRole switch
    {
        PlayerRole.WicketKeeper => "the gloves and runs in the middle order",
        PlayerRole.Bowler => BallOutcomeModel.IsSpinner(p) ? "spin control and wicket-taking threat" : "pace and new-ball penetration",
        PlayerRole.BowlingAllrounder => "overs and lower-order runs - the balance he gives the side",
        PlayerRole.BattingAllrounder => "top-order runs and a genuine fifth bowling option",
        _ => format == MatchFormat.Test ? "a proven Test technique and appetite for a long innings" : "scoring intent and a match-winning ceiling"
    };
}

/// <summary>Phase 11: the output of a selection meeting - the story behind an announced squad.</summary>
/// <param name="Outvoted">§5.2: true when a genuine panel majority (see SelectionMeetingService.Hold's per-seat vote) preferred a different name for the most contested slot and the final call overruled them.</param>
/// <param name="VotesForChosen">How many of the panel's seats actually agreed with the final pick on that contested slot.</param>
/// <param name="PanelSize">How many seats were modelled - 3 for a domestic panel, the national board's own PanelSize for an international one.</param>
/// <param name="OutvotedChosenPlayerId">Meeting-driven-selection ticket: when Outvoted, the id of the player whose place was carried over the panel majority - the Outvoted-consequences fold-in reads this to dent HIS confidence and the board's trust, without re-parsing DissentNote's text.</param>
/// <param name="OutvotedRivalPlayerId">The id of the player the panel majority actually preferred, when Outvoted.</param>
public sealed record SelectionMeetingReport(
    string Headline,
    IReadOnlyList<(string Player, string Rationale)> SlotRationales,
    IReadOnlyList<string> ContestedSlots,
    string? DissentNote,
    string? SurprisePick,
    string? BigOmission,
    string CaptainComment,
    double MeetingQuality,
    bool Outvoted = false,
    int VotesForChosen = 0,
    int PanelSize = 0,
    Guid? OutvotedChosenPlayerId = null,
    Guid? OutvotedRivalPlayerId = null);
