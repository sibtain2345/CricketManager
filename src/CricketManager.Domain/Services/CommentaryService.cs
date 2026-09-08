using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>What kind of moment a line reacted to, so a caller (a future UI, a post-match report)
/// can filter or highlight without re-parsing the text.</summary>
public enum CommentaryTag
{
    Routine,
    Boundary,
    Six,
    Wicket,
    Milestone,
    PersonalBest,
    GroundRecord,
    Pressure
}

/// <summary>One delivery's commentary line, tagged with what made it notable (if anything).</summary>
public sealed record BallCommentary(int BallNumber, int OverNumber, int BallInOver, string Text, IReadOnlyList<CommentaryTag> Tags);

/// <summary>
/// Optional richer context commentary can react to, beyond what's already on the InningsState
/// itself. Every field is optional and commentary degrades gracefully without it - the same
/// pattern as BallContext.Ground/Field: neutral when absent, sharper when supplied.
/// </summary>
public sealed record CommentaryContext
{
    /// <summary>The venue, for naming and for the ground-record comparison below.</summary>
    public Ground? Ground { get; init; }

    /// <summary>
    /// Each player's career stats AS THEY STOOD BEFORE THIS MATCH, so a "career-best" line means
    /// something. Passing post-match stats would make every fresh score read as a career-best,
    /// because by then this innings has already been folded into the total.
    /// </summary>
    public IReadOnlyDictionary<Guid, PlayerCareerStats>? CareerStatsBeforeMatch { get; init; }

    /// <summary>
    /// Batting innings at this ground from BEFORE this match - the record has to already exist to
    /// be broken. GroundRecordsService derives the actual number from these.
    /// </summary>
    public IEnumerable<BattingInningsRecord>? GroundBattingHistory { get; init; }
}

/// <summary>
/// Turns a completed (or still in-progress) InningsState's ball-by-ball log into readable
/// commentary. Spec section 61-62.
///
/// Deliberately layered ON TOP of the simulation rather than woven into InningsSimulator's ball
/// loop - the same "simulating is not recording" separation MatchRecorder already relies on.
/// This reads InningsState.Deliveries after the fact (or as far as a live match has got), so
/// nothing here can affect an outcome, and the already-tested simulation hot path stays untouched.
///
/// DeliveryRecord only carries the TEAM's score and wicket count, not each player's own running
/// total - so milestone detection replays the log to reconstruct each batter's and bowler's runs
/// and wickets at the moment of every ball. That replay is the only thing this class does with
/// state; everything else is a pure function of one delivery plus the optional context.
///
/// Deterministic like everything else here: phrasing variety comes from the Random the caller
/// passes in, never from one this class owns - so the same match replayed from the same seed
/// (and the same commentary seed) produces the same commentary every time.
/// </summary>
public sealed class CommentaryService
{
    private readonly MatchupConfidenceService _matchups = new();
    private readonly GroundRecordsService _groundRecords = new();

    private static readonly int[] BatterMilestones = { 50, 100, 150, 200 };

    public IReadOnlyList<BallCommentary> GenerateInningsCommentary(
        InningsState innings,
        IReadOnlyDictionary<Guid, Player> players,
        Random random,
        CommentaryContext? context = null)
    {
        context ??= new CommentaryContext();
        var lines = new List<BallCommentary>(innings.Deliveries.Count);

        var runsSoFar = new Dictionary<Guid, int>();
        var wicketsSoFar = new Dictionary<Guid, int>();
        var reportedBatterMilestones = new HashSet<(Guid PlayerId, int Threshold)>();
        var reportedBowlerThreeFor = new HashSet<Guid>();
        var reportedBowlerFiveFor = new HashSet<Guid>();
        var reportedPersonalBest = new HashSet<Guid>();
        var reportedGroundRecord = new HashSet<Guid>();
        int legalBallsSoFar = 0;

        // Computed once, not per ball - the ground record doesn't move during an innings, only
        // between matches.
        int? groundRecordRuns = context.Ground is { } ground && context.GroundBattingHistory is not null
            ? _groundRecords.GetHighestIndividualScore(context.GroundBattingHistory, ground.Id)?.PrimaryValue
            : null;

        foreach (var delivery in innings.Deliveries)
        {
            players.TryGetValue(delivery.StrikerId, out var striker);
            players.TryGetValue(delivery.BowlerId, out var bowler);
            var outcome = delivery.Outcome;

            runsSoFar.TryGetValue(delivery.StrikerId, out int strikerRunsBefore);
            wicketsSoFar.TryGetValue(delivery.BowlerId, out int bowlerWicketsBefore);
            int strikerRunsAfter = strikerRunsBefore + outcome.RunsOffBat;
            int bowlerWicketsAfter = bowlerWicketsBefore + (outcome.IsWicket && outcome.WicketCreditedToBowler ? 1 : 0);
            runsSoFar[delivery.StrikerId] = strikerRunsAfter;
            wicketsSoFar[delivery.BowlerId] = bowlerWicketsAfter;
            if (outcome.IsLegalDelivery) legalBallsSoFar++;

            var tags = new List<CommentaryTag>();
            string text = DescribeOutcome(delivery, striker, bowler, players, random, tags);
            if (tags.Count == 0) tags.Add(CommentaryTag.Routine);

            foreach (int threshold in BatterMilestones)
            {
                if (striker is not null && strikerRunsBefore < threshold && strikerRunsAfter >= threshold
                    && reportedBatterMilestones.Add((delivery.StrikerId, threshold)))
                {
                    text += " " + MilestonePhrase(striker, threshold);
                    tags.Add(CommentaryTag.Milestone);
                }
            }

            if (bowler is not null && bowlerWicketsBefore < 3 && bowlerWicketsAfter >= 3
                && reportedBowlerThreeFor.Add(delivery.BowlerId))
            {
                text += $" Three for {ShortName(bowler)}.";
                tags.Add(CommentaryTag.Milestone);
            }
            if (bowler is not null && bowlerWicketsBefore < 5 && bowlerWicketsAfter >= 5
                && reportedBowlerFiveFor.Add(delivery.BowlerId))
            {
                text += $" Five-wicket haul for {ShortName(bowler)}!";
                tags.Add(CommentaryTag.Milestone);
            }

            if (striker is not null && context.CareerStatsBeforeMatch is { } stats
                && stats.TryGetValue(delivery.StrikerId, out var careerStats) && careerStats.HighestScore > 0
                && strikerRunsBefore < careerStats.HighestScore && strikerRunsAfter >= careerStats.HighestScore
                && reportedPersonalBest.Add(delivery.StrikerId))
            {
                text += $" That's a new career-best for {ShortName(striker)}.";
                tags.Add(CommentaryTag.PersonalBest);
            }

            if (striker is not null && groundRecordRuns is { } record
                && strikerRunsBefore < record && strikerRunsAfter >= record
                && reportedGroundRecord.Add(delivery.StrikerId))
            {
                text += " That's the highest individual score ever made at this ground.";
                tags.Add(CommentaryTag.GroundRecord);
            }

            // Chase pressure, on top of an already-notable ball only - a plain dot in the death
            // overs of a tight chase doesn't need its own sentence, but a boundary or a wicket
            // there changes the game and deserves to say so.
            bool notable = tags.Contains(CommentaryTag.Boundary) || tags.Contains(CommentaryTag.Six) || tags.Contains(CommentaryTag.Wicket);
            if (notable && delivery.Phase == MatchPhase.DeathOvers && innings.Target is { } target)
            {
                int required = Math.Max(0, target - delivery.ScoreAfter);
                int? ballsLeft = innings.MaxLegalBalls is { } max ? Math.Max(0, max - legalBallsSoFar) : null;
                if (ballsLeft is { } left && left > 0 && required > 0)
                {
                    text += tags.Contains(CommentaryTag.Wicket)
                        ? $" The required rate climbs - {required} needed off {left}."
                        : $" That eases things - {required} needed off {left} now.";
                    tags.Add(CommentaryTag.Pressure);
                }
            }

            lines.Add(new BallCommentary(delivery.BallNumber, delivery.OverNumber, delivery.BallInOver, text, tags));
        }

        return lines;
    }

    // ---------------- per-outcome description ----------------

    private string DescribeOutcome(
        DeliveryRecord delivery, Player? striker, Player? bowler, IReadOnlyDictionary<Guid, Player> players,
        Random random, List<CommentaryTag> tags)
    {
        var outcome = delivery.Outcome;
        switch (outcome.Type)
        {
            case DeliveryOutcomeType.DotBall:
                return DotBallLine(bowler, random);
            case DeliveryOutcomeType.RunsOffBat:
                return RunsLine(outcome.RunsOffBat, striker, random);
            case DeliveryOutcomeType.Boundary4:
                tags.Add(CommentaryTag.Boundary);
                return BoundaryLine(striker, random, six: false);
            case DeliveryOutcomeType.Boundary6:
                tags.Add(CommentaryTag.Six);
                return BoundaryLine(striker, random, six: true);
            case DeliveryOutcomeType.Wicket:
                return WicketLine(outcome, striker, bowler, players, tags);
            case DeliveryOutcomeType.Wide:
                return outcome.ExtraRuns > 1
                    ? $"Wide down the leg side, and they run - {outcome.ExtraRuns} total."
                    : "Wide called, one more for the total.";
            case DeliveryOutcomeType.NoBall:
                return outcome.RunsOffBat > 0
                    ? $"No-ball - and {NameOr(striker, "the batter")} finds the gap for {outcome.RunsOffBat}. Free hit next up."
                    : "No-ball. Free hit to come.";
            case DeliveryOutcomeType.Bye:
                return $"Byes - {outcome.ExtraRuns} added, the keeper will want that back.";
            case DeliveryOutcomeType.LegBye:
                return outcome.ExtraRuns > 1
                    ? $"Leg-byes, {outcome.ExtraRuns} runs off the pads."
                    : "Leg-bye, one off the pads.";
            default:
                return "Ball delivered.";
        }
    }

    private static string DotBallLine(Player? bowler, Random random)
    {
        string bowlerName = NameOr(bowler, "the bowler");
        string? trait = bowler is not null ? StrongBowlingTrait(bowler) : null;

        string[] templates = trait switch
        {
            "ContainmentBowler" => new[] { $"{bowlerName} squeezes again - dot ball, exactly the plan.", $"No room at all from {bowlerName}. Dot." },
            "SwingBowler" or "SeamBowler" => new[] { $"Beaten! {bowlerName} finds some movement there, no run.", $"{bowlerName} gets one to move away late - dot ball." },
            "WicketTaker" or "PartnershipBreaker" => new[] { $"{bowlerName} keeps probing, no run this time.", $"Watchful defence needed there against {bowlerName}. Dot." },
            _ => new[] { "Defended, no run.", "Dot ball.", "Nudged straight to a fielder, no run.", $"{bowlerName} lands it on a length, no run." }
        };
        return templates[random.Next(templates.Length)];
    }

    private static string RunsLine(int runs, Player? striker, Random random)
    {
        string name = NameOr(striker, "the batter");
        string[] templates = runs switch
        {
            1 => new[] { $"{name} works it away for a single.", $"Quick single, well run by {name}." },
            2 => new[] { $"{name} picks up two, good running between the wickets.", $"Two runs, placed nicely by {name}." },
            3 => new[] { $"Three runs - hard running from {name}." },
            5 => new[] { $"Five! Overthrows help {name} along." },
            _ => new[] { $"{name} picks up {runs}." }
        };
        return templates[random.Next(templates.Length)];
    }

    private static string BoundaryLine(Player? striker, Random random, bool six)
    {
        string name = NameOr(striker, "the batter");
        string shotWord = six ? "into the stands" : "to the fence";
        string? trait = striker is not null ? StrongBattingTrait(striker) : null;

        string[] templates = (trait, six) switch
        {
            ("PowerHitter", true) or ("Slogger", true) =>
                new[] { $"{name} doesn't need a second invitation - launched {shotWord}.", $"That's the power {name} is known for - gone {shotWord}." },
            ("PowerHitter", false) or ("Slogger", false) =>
                new[] { $"{name} muscles it away {shotWord}.", $"Full power from {name}, races {shotWord}." },
            ("ClassicalBatter", true) or ("Anchor", true) =>
                new[] { $"Not {name}'s usual game, but that's gone all the way {shotWord}.", $"{name} picks his moment and clears the ropes." },
            ("ClassicalBatter", false) or ("Anchor", false) =>
                new[] { $"Classical touch from {name}, timed beautifully {shotWord}.", $"{name} times it, no need to force it - {shotWord}." },
            ("Finisher", true) or ("ChaseSpecialist", true) =>
                new[] { $"{name} knows exactly what's needed here - {shotWord}.", $"Ice cold from {name} - {shotWord}." },
            ("Finisher", false) or ("ChaseSpecialist", false) =>
                new[] { $"{name} finds the gap under pressure - {shotWord}." },
            (_, true) => new[] { $"{name} goes big - {shotWord}!", $"That's gone all the way, {name} finds the six." },
            (_, false) => new[] { $"{name} finds the boundary, races {shotWord}.", $"Cracked away by {name}, four more." }
        };
        return templates[random.Next(templates.Length)];
    }

    private string WicketLine(DeliveryOutcome outcome, Player? striker, Player? bowler, IReadOnlyDictionary<Guid, Player> players, List<CommentaryTag> tags)
    {
        tags.Add(CommentaryTag.Wicket);
        string name = NameOr(striker, "the batter");
        string bowlerName = NameOr(bowler, "the bowler");
        string? fielderName = outcome.FielderId is { } fid && players.TryGetValue(fid, out var fielder) ? ShortName(fielder) : null;

        string line = outcome.Dismissal switch
        {
            DismissalType.Bowled => $"{name} is bowled! {bowlerName} sends the stumps flying.",
            DismissalType.LBW => $"Given lbw! {name} trapped in front by {bowlerName}.",
            DismissalType.Caught => fielderName is not null
                ? $"Caught! {name} holes out, {fielderName} takes a good catch off {bowlerName}."
                : $"Caught! {name} departs, well held off {bowlerName}.",
            DismissalType.CaughtBehind => $"Edged and taken behind the stumps! {name} has to go, {bowlerName} strikes.",
            DismissalType.CaughtAndBowled => $"Caught and bowled! {bowlerName} takes a smart return catch to remove {name}.",
            DismissalType.RunOut => fielderName is not null
                ? $"Run out! A direct hit from {fielderName}, and the batter's gone."
                : "Run out! A mix-up in the middle, and one of them pays for it.",
            DismissalType.Stumped => $"Stumped! {name} beaten in the flight, the keeper does the rest off {bowlerName}.",
            DismissalType.HitWicket => $"Hit wicket! {name} dislodges the bails himself, {bowlerName} the bowler.",
            DismissalType.Retired => $"{name} retires.",
            _ => $"{name} is out."
        };

        // Matchup flavor only when there's a genuine sample behind it - GetMatchupMultiplier
        // already damps low-sample records toward neutral, so this naturally stays quiet on a
        // fresh matchup rather than manufacturing a rivalry off one data point.
        if (striker is not null && bowler is not null)
        {
            var key = MatchupKey.ForBowler(bowler.Id);
            double multiplier = _matchups.GetMatchupMultiplier(striker, key);
            // §15.4: surface the head-to-head in the live call, with the actual weight of history
            // behind it when there is a real sample.
            int meetings = striker.Matchups.TryGetValue(key, out var mc) ? mc.SampleCount : 0;
            if (multiplier <= 0.90)
                line += meetings >= 6
                    ? $" That is a match-up {bowlerName} has owned - he has now had {name} more times than not across {meetings} meetings."
                    : $" {bowlerName} has had the wood on {name} for a while now.";
            else if (multiplier >= 1.10)
                line += meetings >= 6
                    ? $" {name} has generally come out on top in this duel over {meetings} meetings - not today."
                    : $" {name} usually gets the better of {bowlerName} - not today.";
        }

        return line;
    }

    // ---------------- helpers ----------------

    private static string MilestonePhrase(Player striker, int threshold) => threshold switch
    {
        50 => $"That brings up the fifty for {ShortName(striker)}.",
        100 => $"Century! {ShortName(striker)} raises the bat.",
        150 => $"{ShortName(striker)} moves to 150.",
        200 => $"Double century for {ShortName(striker)}!",
        _ => $"{ShortName(striker)} reaches {threshold}."
    };

    private static string ShortName(Player p) => string.IsNullOrWhiteSpace(p.LastName) ? p.FullName : p.LastName;
    private static string NameOr(Player? p, string fallback) => p is not null ? ShortName(p) : fallback;

    /// <summary>
    /// The dominant batting trait, or null if nothing rises meaningfully above neutral (55 on the
    /// 0-100 scale). The floor matters: a player whose traits haven't been through
    /// RoleTraitDeriver yet sits at all zeros, and this codebase has already been bitten three
    /// times by an unpopulated default read as a real signal (see CLAUDE.md). Zero here would
    /// still pick SOME trait as "dominant" (they're all tied at zero) and hand out a false label -
    /// the floor is what keeps a not-yet-derived player reading as neutral instead.
    /// </summary>
    private static string? StrongBattingTrait(Player p)
    {
        var t = p.BattingTraits;
        var scored = new (string Name, int Value)[]
        {
            ("PowerHitter", t.PowerHitter), ("Slogger", t.Slogger), ("Anchor", t.Anchor),
            ("ClassicalBatter", t.ClassicalBatter), ("StrokeMaker", t.StrokeMaker),
            ("Finisher", t.Finisher), ("ChaseSpecialist", t.ChaseSpecialist)
        };
        var best = scored.OrderByDescending(s => s.Value).First();
        return best.Value >= 55 ? best.Name : null;
    }

    /// <summary>Same floor and same reasoning as <see cref="StrongBattingTrait"/>, for bowlers.</summary>
    private static string? StrongBowlingTrait(Player p)
    {
        var t = p.BowlingTraits;
        var scored = new (string Name, int Value)[]
        {
            ("SwingBowler", t.SwingBowler), ("SeamBowler", t.SeamBowler), ("WicketTaker", t.WicketTaker),
            ("PartnershipBreaker", t.PartnershipBreaker), ("DeathSpecialist", t.DeathSpecialist),
            ("ContainmentBowler", t.ContainmentBowler)
        };
        var best = scored.OrderByDescending(s => s.Value).First();
        return best.Value >= 55 ? best.Name : null;
    }
}
