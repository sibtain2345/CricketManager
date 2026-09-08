using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// The rest of planning-brief Section A, later extended by the squad-selection-and-playing-XI
/// spec: picking an eleven is a COMBINATORIAL problem, not just "the top 11 by score" - a side
/// of eleven brilliant top-order batters with no bowling attack cannot field a legal XI, and
/// `PlayerSelectionEvaluator.Evaluate` was never meant to answer that question by itself. It
/// already does the individual-value half correctly (SquadStatus-aware via
/// EstablishedProtection, coach-philosophy-weighted); this is the balancing layer on top of it.
/// </summary>
/// <param name="IsSpecialistWicketkeeper">False when Wicketkeeper is an occasional gloveman standing in for a missing specialist - see SelectXi's occasional-keeper fallback.</param>
public sealed record XiSelectionResult(
    IReadOnlyList<Player> BattingOrder,
    IReadOnlyList<Player> Bowlers,
    Player? Wicketkeeper,
    bool IsSpecialistWicketkeeper,
    IReadOnlyList<string> Reasoning);

public sealed class XiSelectionService
{
    private readonly PlayerSelectionEvaluator _evaluator = new();
    private readonly PlayerAvailabilityService _availability = new();
    private readonly FieldingAptitudeService _aptitude = new();

    private static bool CanBowl(Player p) => p.BowlingRole != BowlingRoleType.NotABowler;

    /// <summary>
    /// Legal-over-coverage floor, not a taste preference. `InningsSimulator.MaxOversPerBowler`
    /// caps a limited-overs bowler at totalOvers/5, so five recognised bowling options is the
    /// minimum that can legally bowl the innings out - fewer than that and overs run out with
    /// nobody left who is allowed to send them down. First-class has no such legal cap, so the
    /// floor there is a real-attack-depth judgement rather than a hard rule, and set a shade
    /// lower.
    /// </summary>
    public static int BowlingFloor(MatchFormat format) => format == MatchFormat.Test ? 4 : 5;

    /// <summary>
    /// Which discipline a player is actually being valued for. A specialist bowler is judged on
    /// his BOWLING (not the batting-flavoured score every player used to be ranked on
    /// regardless of role - a real, pre-existing gap this pass closes: PlayerSelectionEvaluator.
    /// Evaluate's format-fit term was always batting suitability, so a genuine strike bowler with
    /// modest batting could rank below a mediocre bowler who happened to bat a bit better, which
    /// is backwards for "who should actually be in the bowling attack"). An allrounder is judged
    /// on his BETTER discipline, since that is genuinely his main asset for a place in the side -
    /// PreferAllrounder below separately checks his OTHER discipline against whichever specialist
    /// he might displace.
    /// </summary>
    private double PrimaryDisciplineScore(Player p, MatchFormat format, SelectionWeighting weights)
    {
        if (p.PrimaryRole is PlayerRole.BattingAllrounder or PlayerRole.BowlingAllrounder)
        {
            double bat = _evaluator.Evaluate(p, format, weights, forBowling: false).TotalScore;
            double bowl = _evaluator.Evaluate(p, format, weights, forBowling: true).TotalScore;
            return Math.Max(bat, bowl);
        }
        bool bowlingPrimary = p.PrimaryRole == PlayerRole.Bowler;
        return _evaluator.Evaluate(p, format, weights, forBowling: bowlingPrimary).TotalScore;
    }

    /// <summary>
    /// Selects a genuine, balanced eleven: availability-filtered and discipline-aware individual
    /// ranking, a mandatory wicketkeeper (with an aptitude-classified occasional fallback), enough
    /// recognised bowling options to actually cover the format's overs, a modest conditions-aware
    /// pace/spin tilt when a ground is supplied, the allrounder-preference rule, and a batting
    /// order built from each player's own derived BattingRole rather than the raw selection score.
    /// </summary>
    /// <param name="announcedSquad">
    /// Optional. When supplied, the XI can ONLY be drawn from players currently eligible under
    /// this announcement (SquadAnnouncement.IsEligibleForMatch) - the hard rule the squad-
    /// selection spec states first: a playing XI can never include anyone outside the announced
    /// squad. Left null, every player in the pool is eligible, which is what every pre-existing
    /// caller of this method still gets.
    /// </param>
    public XiSelectionResult SelectXi(
        IEnumerable<Player> squad,
        MatchFormat format,
        DateOnly matchDate,
        Coach? coach = null,
        Ground? ground = null,
        SquadAnnouncement? announcedSquad = null,
        bool nationalSelection = false,
        // §3.3: the opposition's own batting order, when known - lets the conditions tilt widen
        // into a genuinely opposition-aware nudge (see below). Null (the default) = no opposition
        // read, exactly the pre-existing behaviour.
        IReadOnlyList<Player>? oppositionBattingOrder = null)
    {
        var reasoning = new List<string>();
        var pool = squad.DistinctBy(p => p.Id).ToList();

        if (announcedSquad is not null)
        {
            int before = pool.Count;
            pool = pool.Where(p => announcedSquad.IsEligibleForMatch(p.Id, matchDate)).ToList();
            if (pool.Count < before)
                reasoning.Add($"Restricted to the announced squad ({pool.Count} of {before} candidates eligible for {matchDate:yyyy-MM-dd}) - a playing XI can never include anyone outside it.");
        }

        var weights = coach is null ? SelectionWeighting.Balanced : SelectionWeighting.ForPhilosophy(coach.Philosophy);

        var ranked = pool
            .Select(p => (Player: p, Availability: _availability.GetAvailability(p, matchDate, format, nationalSelection)))
            .Where(x => x.Availability.IsAvailable)
            .Select(x =>
            {
                double score = PrimaryDisciplineScore(x.Player, format, weights);
                if (x.Availability.EffectivenessIfPlayed < 1.0) score *= x.Availability.EffectivenessIfPlayed;
                return (x.Player, Score: score);
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        var rankedPlayers = ranked.Select(x => x.Player).ToList();
        var rankIndex = rankedPlayers.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);

        if (rankedPlayers.Count < 11)
            reasoning.Add($"Only {rankedPlayers.Count} players are available - the side takes the field short of a full eleven.");

        // --- Wicketkeeper: mandatory where a specialist exists, never swapped out for anything
        // below. If none does, the occasional-keeper fallback further down names someone once
        // the rest of the XI is settled - see there for why that has to happen last.
        var specialistKeeper = rankedPlayers.FirstOrDefault(p => p.PrimaryRole == PlayerRole.WicketKeeper);

        var selected = new List<Player>();
        if (specialistKeeper is not null) selected.Add(specialistKeeper);

        var remaining = rankedPlayers.Where(p => p != specialistKeeper).ToList();
        selected.AddRange(remaining.Take(Math.Max(0, 11 - selected.Count)));

        // --- Bowling depth: swap in the best available bowling option for the weakest
        // currently-selected pure batter until the format's legal floor is met. ---
        int bowlingFloor = BowlingFloor(format);
        int bowlingCount = selected.Count(CanBowl);

        while (bowlingCount < bowlingFloor)
        {
            var swapIn = remaining.FirstOrDefault(p => CanBowl(p) && !selected.Contains(p));
            var swapOut = selected.Where(p => p != specialistKeeper && !CanBowl(p))
                .OrderByDescending(p => rankIndex[p.Id]).FirstOrDefault();
            if (swapIn is null || swapOut is null) break; // no further legal swap exists - report the shortfall rather than loop forever

            selected.Remove(swapOut);
            selected.Add(swapIn);
            bowlingCount++;
            reasoning.Add($"Included {swapIn.FullName} ahead of the higher-ranked {swapOut.FullName} - a {format} attack needs at least {bowlingFloor} recognised bowling options to legally cover the innings.");
        }
        if (bowlingCount < bowlingFloor)
            reasoning.Add($"Could only find {bowlingCount} recognised bowling options in the available squad, short of the {bowlingFloor} a {format} attack needs - a genuine squad-depth problem, not something this selection can fix.");

        // --- Conditions tilt: at most one swap each way, and only for a genuinely suitable
        // replacement already in the squad - a nudge toward the conditions, not a rewrite of
        // the ranking. ---
        if (ground is not null)
        {
            bool pitchFavoursSpin = ground.PitchSpinRating >= 65 && ground.PitchSpinRating > ground.PitchPaceRating;
            bool pitchFavoursPace = ground.PitchPaceRating >= 65 && ground.PitchPaceRating > ground.PitchSpinRating;
            int spinCount = selected.Count(p => CanBowl(p) && BallOutcomeModel.IsSpinner(p));

            if (pitchFavoursSpin && spinCount < 2)
            {
                var spinnerIn = remaining.FirstOrDefault(p => BallOutcomeModel.IsSpinner(p) && !selected.Contains(p));
                var paceOut = selected.Where(p => p != specialistKeeper && CanBowl(p) && !BallOutcomeModel.IsSpinner(p))
                    .OrderByDescending(p => rankIndex[p.Id]).FirstOrDefault();
                if (spinnerIn is not null && paceOut is not null)
                {
                    selected.Remove(paceOut);
                    selected.Add(spinnerIn);
                    reasoning.Add($"Swapped in the spinner {spinnerIn.FullName} for {paceOut.FullName} - {ground.Name}'s pitch rates {ground.PitchSpinRating:F0}/100 for spin, well above its pace rating.");
                }
            }
            else if (pitchFavoursPace && spinCount > 1)
            {
                var spinOut = selected.Where(p => p != specialistKeeper && BallOutcomeModel.IsSpinner(p))
                    .OrderByDescending(p => rankIndex[p.Id]).FirstOrDefault();
                var paceIn = remaining.FirstOrDefault(p => CanBowl(p) && !BallOutcomeModel.IsSpinner(p) && !selected.Contains(p));
                if (spinOut is not null && paceIn is not null)
                {
                    selected.Remove(spinOut);
                    selected.Add(paceIn);
                    reasoning.Add($"Swapped in the seamer {paceIn.FullName} for {spinOut.FullName} - {ground.Name}'s pitch rates {ground.PitchPaceRating:F0}/100 for pace, well above its spin rating.");
                }
            }
        }

        // §3.3: an OPPOSITION-aware nudge, on top of the conditions tilt above - at most one swap.
        // A top order heavily loaded one-handed is a real reason to carry a left-arm option for the
        // variety of angle, if a close-ranked one is sitting in the squad already.
        if (oppositionBattingOrder is { Count: >= 5 })
        {
            int rightHandTop = oppositionBattingOrder.Take(6).Count(p => p.BattingHand == BattingHand.Right);
            bool oppositionOneHanded = rightHandTop >= 5;
            bool haveLeftArmOption = selected.Any(p => CanBowl(p) && BallOutcomeModel.IsLeftArm(p.BowlingStyle));
            if (oppositionOneHanded && !haveLeftArmOption)
            {
                var leftArmIn = remaining
                    .Where(p => CanBowl(p) && BallOutcomeModel.IsLeftArm(p.BowlingStyle) && !selected.Contains(p))
                    .OrderBy(p => rankIndex[p.Id]).FirstOrDefault();
                var weakestBowlerOut = selected.Where(p => p != specialistKeeper && CanBowl(p) && !BallOutcomeModel.IsLeftArm(p.BowlingStyle))
                    .OrderByDescending(p => rankIndex[p.Id]).FirstOrDefault();
                // Never break the bowling-depth floor, and only for a genuinely close-ranked option.
                if (leftArmIn is not null && weakestBowlerOut is not null
                    && rankIndex[leftArmIn.Id] - rankIndex[weakestBowlerOut.Id] <= 6)
                {
                    selected.Remove(weakestBowlerOut);
                    selected.Add(leftArmIn);
                    reasoning.Add($"Included the left-armer {leftArmIn.FullName} ahead of {weakestBowlerOut.FullName} - the opposition's top order is almost entirely right-handed, and a genuine change of angle is worth having.");
                }
            }
        }

        // --- Allrounder preference (squad-selection spec Â§5.1): a genuinely complete allrounder,
        // whose skill in the specialist's OWN discipline is at least as good as the weakest
        // specialist occupying that discipline, is preferred - the extra runs/overs he brings are
        // then a free bonus rather than a trade-off. Run LAST-but-one (after the XI is otherwise
        // settled, before the occasional-keeper read), one swap per discipline at most, and never
        // allowed to break the bowling-depth floor it took two earlier steps to satisfy.
        // AllrounderBias (Â§5.2) is the only discount ever applied, and only for the two
        // development-leaning coaching philosophies - see SelectionWeighting's own doc.
        void PreferAllrounder(bool bowlingDiscipline)
        {
            var allrounderRole = bowlingDiscipline ? PlayerRole.BowlingAllrounder : PlayerRole.BattingAllrounder;
            var specialistRole = bowlingDiscipline ? PlayerRole.Bowler : PlayerRole.Batsman;

            var candidate = remaining
                .Where(p => p.PrimaryRole == allrounderRole && !selected.Contains(p))
                .Select(p => (Player: p, Score: _evaluator.Evaluate(p, format, weights, forBowling: bowlingDiscipline).TotalScore))
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();
            if (candidate.Player is null) return;

            var weakestSpecialist = selected
                .Where(p => p != specialistKeeper && p.PrimaryRole == specialistRole)
                .Select(p => (Player: p, Score: _evaluator.Evaluate(p, format, weights, forBowling: bowlingDiscipline).TotalScore))
                .OrderBy(x => x.Score)
                .FirstOrDefault();
            if (weakestSpecialist.Player is null) return;

            // The spec's own threshold condition: "at least as good as," not merely close.
            if (candidate.Score + weights.AllrounderBias < weakestSpecialist.Score) return;

            if (bowlingDiscipline)
            {
                int wouldBeBowlingCount = selected.Count(CanBowl)
                    - (CanBowl(weakestSpecialist.Player) ? 1 : 0) + (CanBowl(candidate.Player) ? 1 : 0);
                if (wouldBeBowlingCount < bowlingFloor) return; // a "free" upgrade must never quietly break the legal floor
            }

            selected.Remove(weakestSpecialist.Player);
            selected.Add(candidate.Player);
            string discipline = bowlingDiscipline ? "bowling" : "batting";
            string bonus = bowlingDiscipline ? "runs lower down the order" : "overs of bowling";
            reasoning.Add($"Preferred the allrounder {candidate.Player.FullName} ({candidate.Score:F0}/100 {discipline}) over the specialist " +
                $"{weakestSpecialist.Player.FullName} ({weakestSpecialist.Score:F0}/100) - his {discipline} is at least as good, so his {bonus} come free.");
        }

        PreferAllrounder(bowlingDiscipline: false); // batting allrounder vs. specialist batter
        PreferAllrounder(bowlingDiscipline: true);  // bowling allrounder vs. specialist bowler

        // --- Phase 11 (§5.9): the XI as a fuller combination problem. Each of these is at most ONE
        // swap, only for a genuinely suitable replacement already in the squad, and never allowed to
        // break the bowling-depth floor or take out the keeper - the same discipline as the
        // conditions tilt above. They plug the specific gaps a pure best-N-by-rating XI leaves.
        void RequireCapability(string label, Func<Player, bool> hasIt, int minWanted, bool bowlingSideOnly, bool limitedOversOnly)
        {
            if (limitedOversOnly && format == MatchFormat.Test) return;
            int have = selected.Count(p => (!bowlingSideOnly || CanBowl(p)) && hasIt(p));
            if (have >= minWanted) return;

            var swapIn = remaining.FirstOrDefault(p => !selected.Contains(p) && (!bowlingSideOnly || CanBowl(p)) && hasIt(p));
            if (swapIn is null) return;

            // Drop the least costly man who does NOT have the capability: a pure batter before a
            // bowler, weakest-ranked first, never the keeper, never if it breaches the bowling floor.
            var swapOut = selected
                .Where(p => p != specialistKeeper && !hasIt(p))
                .Where(p => !(CanBowl(p) && selected.Count(CanBowl) <= bowlingFloor && !CanBowl(swapIn)))
                .OrderBy(p => CanBowl(p) ? 1 : 0)                       // batters first
                .ThenByDescending(p => rankIndex.GetValueOrDefault(p.Id, -1)) // then weakest-ranked
                .FirstOrDefault();
            if (swapOut is null) return;

            selected.Remove(swapOut);
            selected.Add(swapIn);
            reasoning.Add($"Brought in {swapIn.FullName} for {swapOut.FullName} - the XI was short of {label}.");
        }

        RequireCapability("a genuine new-ball pair", p => !BallOutcomeModel.IsSpinner(p) && p.Bowling.NewBallBowling >= 12, minWanted: 2, bowlingSideOnly: true, limitedOversOnly: false);
        RequireCapability("a death-overs specialist", p => p.Bowling.DeathBowling >= 13, minWanted: 1, bowlingSideOnly: true, limitedOversOnly: true);
        RequireCapability("a viable on-field leader", p => p.Mental.Leadership >= 12, minWanted: 1, bowlingSideOnly: false, limitedOversOnly: false);

        // Left-right top-order mix: if the top five in the batting order are all one-handed and a
        // genuinely comparable opposite-hander is available, one nudge - a mixed top order is
        // harder to set a field to and breaks a bowler's line. Never breaks the floor or the keeper.
        {
            var topFive = selected
                .Where(p => p.BattingRole is BattingRole.Opener or BattingRole.TopOrder)
                .OrderBy(p => rankIndex.GetValueOrDefault(p.Id, int.MaxValue))
                .Take(5).ToList();
            if (topFive.Count >= 4 && topFive.Select(p => p.BattingHand).Distinct().Count() == 1)
            {
                var wantHand = topFive[0].BattingHand == BattingHand.Right ? BattingHand.Left : BattingHand.Right;
                var mixIn = remaining.FirstOrDefault(p => !selected.Contains(p) && p.BattingHand == wantHand
                    && p.BattingRole is BattingRole.Opener or BattingRole.TopOrder or BattingRole.MiddleOrder);
                var mixOut = topFive.LastOrDefault(p => p != specialistKeeper && !CanBowl(p));
                if (mixIn is not null && mixOut is not null
                    && _evaluator.Evaluate(mixIn, format, weights).TotalScore >= _evaluator.Evaluate(mixOut, format, weights).TotalScore - 6)
                {
                    selected.Remove(mixOut);
                    selected.Add(mixIn);
                    reasoning.Add($"Brought in the {wantHand.ToString().ToLowerInvariant()}-hander {mixIn.FullName} for {mixOut.FullName} - the top order was entirely one-handed.");
                }
            }
        }

        // --- Contested slots: the squad-selection spec explicitly asks the system to be able to
        // represent a genuine toss-up, not just a fixed nominal XI. A cheap, honest surface for
        // it: when the weakest selected player and the best left-out player in the same broad
        // discipline group are within a small margin, say so, rather than presenting a close call
        // as if it were obvious. ---
        const double ContestedMargin = 4.0;
        foreach (bool bowlingGroup in new[] { false, true })
        {
            var inGroup = selected.Where(p => p != specialistKeeper && CanBowl(p) == bowlingGroup).ToList();
            var outGroup = remaining.Where(p => !selected.Contains(p) && CanBowl(p) == bowlingGroup).ToList();
            if (inGroup.Count == 0 || outGroup.Count == 0) continue;

            var weakestIn = inGroup.OrderByDescending(p => rankIndex.GetValueOrDefault(p.Id, -1)).First();
            var bestOut = outGroup.OrderBy(p => rankIndex.GetValueOrDefault(p.Id, int.MaxValue)).First();

            double inScore = _evaluator.Evaluate(weakestIn, format, weights, forBowling: bowlingGroup).TotalScore;
            double outScore = _evaluator.Evaluate(bestOut, format, weights, forBowling: bowlingGroup).TotalScore;
            if (Math.Abs(inScore - outScore) <= ContestedMargin)
                reasoning.Add($"{weakestIn.FullName} and {bestOut.FullName} were closely contested for the final {(bowlingGroup ? "bowling" : "batting")} spot ({inScore:F0} vs {outScore:F0}) - a genuine toss-up, not a clear-cut call.");
        }

        // --- Occasional keeper: run last, once the XI is actually settled, and classify from
        // among the eleven who just earned their place on batting/bowling merit - not from the
        // wider squad, since an occasional keeper hasn't been picked FOR his gloves, he's the
        // best-equipped hands among players who were already in the side. Reuses
        // FieldingAptitudeService.GetAptitude/BestFor directly - the exact same read
        // AutoFieldSetter already falls back to mid-match when no specialist is on the field -
        // so the XI's named keeper and whoever the match engine actually stands behind the
        // stumps are never two different answers, and "sometimes better equipped, sometimes a
        // real liability" comes from one real attribute-driven number rather than a guess.
        var keeper = specialistKeeper;
        bool isSpecialist = specialistKeeper is not null;
        if (keeper is null)
        {
            var occasional = _aptitude.BestFor(selected, FieldingPosition.WicketKeeper);
            if (occasional is not null)
            {
                double keepingAptitude = _aptitude.GetAptitude(occasional, FieldingPosition.WicketKeeper);
                string quality = keepingAptitude >= 60 ? "a genuinely capable occasional option"
                    : keepingAptitude >= 40 ? "a workable but unremarkable occasional option"
                    : "a real weak point behind the stumps";
                reasoning.Add($"No specialist wicketkeeper is available - {occasional.FullName} takes the gloves as {quality} " +
                    $"(keeping aptitude {keepingAptitude:F0}/100), and will drop and miss more than a genuine specialist would.");
                keeper = occasional;
            }
        }

        // --- Batting order: by each player's own derived BattingRole first (a genuine batting
        // property - see RoleTraitDeriver.DeriveBattingRole - not the blended selection score,
        // which would happily open with a specialist bowler who ranked high on bowling alone),
        // best-ranked first within the same role as the tiebreak. ---
        var battingOrder = selected
            .OrderBy(p => p.BattingRole)
            .ThenBy(p => rankIndex.TryGetValue(p.Id, out var i) ? i : int.MaxValue)
            .ToList();

        var bowlers = battingOrder.Where(CanBowl).ToList();

        return new XiSelectionResult(battingOrder, bowlers, keeper, isSpecialist, reasoning);
    }
}
