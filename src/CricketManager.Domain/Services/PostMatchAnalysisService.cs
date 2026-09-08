using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>How much a moment mattered - lets a caller show the major ones and collapse the rest.</summary>
public enum MomentSeverity { Notable, Major }

/// <summary>One notable passage of play. Day is null for a limited-overs match (no such question
/// there) and, since MatchTimeline started tagging every delivery with a real day (Issue 1),
/// genuinely populated for a multi-day one - derived from the innings' own delivery log rather
/// than guessed.</summary>
public sealed record MatchMoment(int? Day, string Description, MomentSeverity Severity);

/// <summary>
/// One session's worth of play - Issue 5. Runs/wickets are for the WHOLE session regardless of
/// which side was doing what (a session can span an innings ending and a new one starting), while
/// the top batter/bowler are read from who actually contributed most WITHIN that session
/// specifically, not their whole-innings figures.
/// </summary>
public sealed record SessionSummary(
    int Day, SessionType Session, string BattingTeamName,
    int RunsScored, int WicketsLost, int LegalBallsBowled,
    string? TopBatterName, int TopBatterRuns,
    string? TopBowlerName, string? TopBowlerFigures);

/// <summary>One player's rated contribution, batting or bowling (or both, for the combined
/// player-of-the-match entry - see <see cref="PostMatchAnalysisService"/>).</summary>
public sealed record PerformanceHighlight(Guid PlayerId, string PlayerName, string Description, double Rating);

/// <summary>A complete post-match report: the headline, who stood out, what the notable passages
/// of play were, and a handful of factual notes about how the match was shaped by conditions or
/// playing-condition rulings (rain, DLS, the follow-on).</summary>
public sealed record MatchAnalysisReport(
    string Headline,
    PerformanceHighlight? PlayerOfTheMatch,
    IReadOnlyList<PerformanceHighlight> TopPerformers,
    IReadOnlyList<MatchMoment> KeyMoments,
    IReadOnlyList<string> Notes);

/// <summary>
/// Turns a completed <see cref="MatchResult"/> or <see cref="MultiDayMatchResult"/> into a
/// readable post-match report. Spec section 70.
///
/// Layered on top of the match result the same way <see cref="CommentaryService"/> is layered on
/// top of an innings - read-only, called on demand, no effect on anything it reads. The inputs
/// were already sitting there unused: <see cref="MultiDayMatchResult.Assessments"/> (each side's
/// own session-by-session read of the game, in their own words via
/// <see cref="SessionAssessment.Reasoning"/>), <see cref="MultiDayMatchResult.Interruptions"/>,
/// <see cref="MultiDayMatchResult.RecoveryPlan"/>, and the full ball-by-ball
/// <see cref="InningsState.Deliveries"/> log both match types already carry.
///
/// **Ratings reuse <see cref="PerformanceRecordingService"/> rather than inventing a second
/// scoring system.** `RateBattingInnings`/`RateBowlingSpell` already exist, are already tested,
/// and already carry their own "first approximation" caveat - building a parallel rating here
/// would just be a second, less-tested version of the same number. This constructs a throwaway
/// (never persisted) <see cref="BattingInningsRecord"/>/<see cref="BowlingSpellRecord"/> from the
/// live match's <see cref="BatterCard"/>/<see cref="BowlerCard"/> purely to feed those methods -
/// the same trick <see cref="MatchRecorder"/> uses for the real, persisted version, and one thing
/// in that conversion is easy to get backwards: see the comment on
/// <see cref="BowlingRatingFor"/> about which "overs" value is the right one to pass.
///
/// **Deliberately does NOT attempt "what the coach could have done differently."** A genuine
/// counterfactual - would a different declaration, a different bowling change, have won this -
/// needs an AI that can evaluate alternative decisions against the same match state, which is
/// Phase 6 (AI Managers) territory, not a report reading a finished match. Building a fake
/// version of it here would look like insight and not be any. What this DOES surface is
/// observable fact: rain interruptions, a follow-on enforced, a DLS revision, a collapse, a big
/// stand, how hard a chase peaked - the material a human coach would want in front of him to draw
/// his own conclusions, not a conclusion drawn for him.
/// </summary>
public sealed class PostMatchAnalysisService
{
    private readonly PerformanceRecordingService _ratings = new();

    public MatchAnalysisReport AnalyzeMatch(MatchResult result)
    {
        var allInnings = new[] { result.FirstInnings, result.SecondInnings };
        var format = result.Setup.Format;

        var performances = AllPerformances(allInnings, format);
        var topPerformers = TopThree(performances);
        var playerOfTheMatch = PlayerOfTheMatch(performances, result.WinningTeamId);

        var moments = new List<MatchMoment>();
        foreach (var innings in allInnings)
        {
            AddIfPresent(moments, BiggestPartnership(innings));
            AddIfPresent(moments, DetectCollapse(innings));
        }
        AddIfPresent(moments, PeakChasePressure(result.SecondInnings));

        var notes = new List<string>();
        if (result.Interruptions.Count > 0)
            notes.Add($"{result.Interruptions.Count} rain interruption(s) during the match.");
        if (result.DlsRevisedTarget is { } revisedTarget)
            notes.Add($"Target revised to {revisedTarget} under Duckworth-Lewis-Stern after rain" +
                (result.RevisedOvers is { } revisedOvers ? $", off {revisedOvers} overs." : "."));
        if (result.NoResult)
            notes.Add("No result - the match did not reach the minimum overs needed for one.");

        return new MatchAnalysisReport(result.Summary, playerOfTheMatch, topPerformers, moments, notes);
    }

    public MatchAnalysisReport AnalyzeMultiDayMatch(MultiDayMatchResult result)
    {
        var performances = AllPerformances(result.Innings, MatchFormat.Test);
        var topPerformers = TopThree(performances);
        var playerOfTheMatch = PlayerOfTheMatch(performances, result.WinningTeamId);

        var moments = new List<MatchMoment>();
        foreach (var innings in result.Innings)
        {
            AddIfPresent(moments, BiggestPartnership(innings));
            AddIfPresent(moments, DetectCollapse(innings));
        }

        var teamNames = new Dictionary<Guid, string>
        {
            [result.Setup.Home.TeamId] = result.Setup.Home.TeamName,
            [result.Setup.Away.TeamId] = result.Setup.Away.TeamName
        };
        moments.AddRange(AssessmentSwings(result.Assessments, teamNames));

        foreach (var interruption in result.Interruptions)
            moments.Add(new MatchMoment(interruption.Day, interruption.Description,
                interruption.EndedPlayForDay ? MomentSeverity.Major : MomentSeverity.Notable));

        // Chronological where a day is known; the moments this analysis couldn't pin to a day
        // (a partnership, a collapse - see the MatchMoment doc comment) sort to the end rather
        // than being guessed into a slot they don't actually belong in.
        moments = moments.OrderBy(m => m.Day ?? int.MaxValue).ToList();

        var notes = new List<string>();
        if (result.FollowOnEnforced) notes.Add("The follow-on was enforced.");
        if (result.OversLostToRain > 0)
        {
            string recovery = result.RecoveryPlan is { } plan ? $" {plan.Summary}" : "";
            notes.Add($"{result.OversLostToRain} overs lost to rain, {result.OversRecovered} recovered.{recovery}");
        }

        return new MatchAnalysisReport(result.Summary, playerOfTheMatch, topPerformers, moments, notes);
    }

    // ---------------- performances ----------------

    private List<(Guid PlayerId, string PlayerName, double Rating, string Description, Guid TeamId)> AllPerformances(
        IEnumerable<InningsState> allInnings, MatchFormat format)
    {
        var list = new List<(Guid, string, double, string, Guid)>();
        foreach (var innings in allInnings)
        {
            foreach (var card in innings.BatterCards.Where(c => c.HasBatted))
                list.Add((card.PlayerId, card.PlayerName, BattingRatingFor(card, format), DescribeBatting(card), innings.BattingTeamId));
            foreach (var card in innings.BowlerCards.Where(c => c.LegalBallsBowled > 0))
                list.Add((card.PlayerId, card.PlayerName, BowlingRatingFor(card, format), DescribeBowling(card), innings.BowlingTeamId));
        }
        return list;
    }

    private static IReadOnlyList<PerformanceHighlight> TopThree(
        List<(Guid PlayerId, string PlayerName, double Rating, string Description, Guid TeamId)> performances) =>
        performances.OrderByDescending(p => p.Rating).Take(3)
            .Select(p => new PerformanceHighlight(p.PlayerId, p.PlayerName, p.Description, Math.Round(p.Rating, 1)))
            .ToList();

    /// <summary>
    /// Combines a player's batting AND bowling ratings by player id before picking the best, so a
    /// genuine all-rounder's match (say a fifty and a four-wicket haul) is credited as one
    /// performance rather than losing to a single bigger century elsewhere. Both scales are the
    /// same -100..100 range by construction (see PerformanceRecordingService), so summing them is
    /// meaningful rather than adding apples to oranges. This is the shared building block for both
    /// Player of the Match (below) and Player of the Series/Tournament (PlayerOfTheSeries) - the
    /// series award is deliberately the SAME per-match rating, just summed across every match a
    /// player featured in, not a separately invented scoring system.
    /// </summary>
    private static List<(Guid PlayerId, string PlayerName, double Rating, string Description, Guid TeamId)> CombineByPlayer(
        List<(Guid PlayerId, string PlayerName, double Rating, string Description, Guid TeamId)> performances) =>
        performances.GroupBy(p => p.PlayerId)
            .Select(g =>
            {
                var ordered = g.OrderByDescending(x => x.Rating).ToList();
                double combined = ordered[0].Rating;
                for (int i = 1; i < ordered.Count; i++)
                    combined += Math.Max(0, ordered[i].Rating) * 0.5;

                return (
                    PlayerId: g.Key,
                    PlayerName: g.First().PlayerName,
                    Rating: combined,
                    Description: string.Join(" and ", ordered.Select(x => x.Description)),
                    TeamId: g.First().TeamId);
            })
            .ToList();

    /// <summary>
    /// Real award convention: Player of the Match comes from the winning side almost every time.
    /// A losing-side player only takes it when he is so far clear of anyone the winning side
    /// produced that ignoring him would be absurd - a 200 chasing 350 in a losing cause, not merely
    /// "the best individual number was on the other side by a little". Ties/no-results/an unplayed
    /// match (winningTeamId null) fall back to the single best performance regardless of side,
    /// since there is no winning side to prefer.
    /// </summary>
    private const double ExtraordinaryLosingSideMargin = 40.0;

    private static PerformanceHighlight? PlayerOfTheMatch(
        List<(Guid PlayerId, string PlayerName, double Rating, string Description, Guid TeamId)> performances,
        Guid? winningTeamId)
    {
        var combined = CombineByPlayer(performances);
        if (combined.Count == 0) return null;

        var bestOverall = combined.OrderByDescending(c => c.Rating).First();

        if (winningTeamId is not { } winner)
            return ToHighlight(bestOverall);

        var winningSide = combined.Where(c => c.TeamId == winner).OrderByDescending(c => c.Rating).ToList();
        if (winningSide.Count == 0) return ToHighlight(bestOverall); // nobody credited to the winning side - fall back rather than return nothing

        bool losingSideIsExtraordinary = bestOverall.TeamId != winner
            && bestOverall.Rating - winningSide[0].Rating > ExtraordinaryLosingSideMargin;

        return ToHighlight(losingSideIsExtraordinary ? bestOverall : winningSide[0]);
    }

    private static PerformanceHighlight ToHighlight((Guid PlayerId, string PlayerName, double Rating, string Description, Guid TeamId) c) =>
        new(c.PlayerId, c.PlayerName, c.Description, Math.Round(c.Rating, 1));

    /// <summary>One player's combined (batting+bowling) rated contribution to a single match - the building block Player of the Series/Tournament sums across every match a player featured in.</summary>
    public sealed record MatchPlayerContribution(Guid PlayerId, string PlayerName, double Rating);

    /// <summary>A single limited-overs match's contributions, for a caller to accumulate across a series or tournament.</summary>
    public IReadOnlyList<MatchPlayerContribution> PlayerContributions(MatchResult result) =>
        CombineByPlayer(AllPerformances(new[] { result.FirstInnings, result.SecondInnings }, result.Setup.Format))
            .Select(c => new MatchPlayerContribution(c.PlayerId, c.PlayerName, c.Rating))
            .ToList();

    /// <summary>A single multi-day match's contributions, for a caller to accumulate across a series.</summary>
    public IReadOnlyList<MatchPlayerContribution> PlayerContributions(MultiDayMatchResult result) =>
        CombineByPlayer(AllPerformances(result.Innings, MatchFormat.Test))
            .Select(c => new MatchPlayerContribution(c.PlayerId, c.PlayerName, c.Rating))
            .ToList();

    /// <summary>
    /// Player of the Series/Tournament: sums each player's own combined match rating (the exact
    /// same number Player of the Match uses) across every match he featured in, and picks the
    /// highest total - a genuinely separate evaluation from any single match's award, which is
    /// deliberate. A player can win this without being Player of the Match in the final, exactly
    /// as happens in real cricket - a string of good-not-great performances across a tournament can
    /// outweigh one standout final that someone else's side won. The caller collects
    /// PlayerContributions after every match played and passes the accumulated list in here once
    /// the series or tournament is over.
    /// </summary>
    public PerformanceHighlight? PlayerOfTheSeries(IEnumerable<MatchPlayerContribution> allContributions)
    {
        var best = allContributions
            .GroupBy(c => c.PlayerId)
            .Select(g => (PlayerId: g.Key, PlayerName: g.First().PlayerName, Rating: g.Sum(c => c.Rating), Matches: g.Count()))
            .OrderByDescending(g => g.Rating)
            .FirstOrDefault();

        return best.PlayerName is null ? null
            : new PerformanceHighlight(best.PlayerId, best.PlayerName,
                $"{best.PlayerName} - combined rating {Math.Round(best.Rating, 1)} across {best.Matches} match(es)",
                Math.Round(best.Rating, 1));
    }

    /// <summary>
    /// Issue 5: a session-by-session breakdown, unblocked by MatchTimeline tagging every delivery
    /// with a real day and session. Runs/wickets/balls cover the WHOLE session (a session can span
    /// an innings ending and the next one starting); the top batter and bowler are read from who
    /// actually contributed most WITHIN that session specifically - a bowler's session figures, not
    /// his whole-innings ones, which the bowler card alone could never distinguish. Empty for a
    /// limited-overs match, which has no session structure to summarise.
    /// </summary>
    public IReadOnlyList<SessionSummary> SessionSummaries(MultiDayMatchResult result)
    {
        var summaries = new List<SessionSummary>();

        foreach (var innings in result.Innings)
        {
            var batterNames = innings.BatterCards.ToDictionary(c => c.PlayerId, c => c.PlayerName);
            var bowlerNames = innings.BowlerCards.ToDictionary(c => c.PlayerId, c => c.PlayerName);

            var sessions = innings.Deliveries
                .Where(d => d.Day is not null && d.Session is not null)
                .GroupBy(d => (Day: d.Day!.Value, Session: d.Session!.Value));

            foreach (var group in sessions)
            {
                var deliveries = group.ToList();
                int runs = deliveries.Sum(d => d.Outcome.TotalRuns);
                int wickets = deliveries.Count(d => d.Outcome.IsWicket && d.Outcome.Dismissal != DismissalType.Retired);
                int legalBalls = deliveries.Count(d => d.Outcome.IsLegalDelivery);

                var topBatter = deliveries.GroupBy(d => d.StrikerId)
                    .Select(g => (Id: g.Key, Runs: g.Sum(d => d.Outcome.RunsOffBat)))
                    .OrderByDescending(x => x.Runs)
                    .FirstOrDefault();

                var topBowler = deliveries.GroupBy(d => d.BowlerId)
                    .Select(g => (
                        Id: g.Key,
                        Wickets: g.Count(d => d.Outcome.IsWicket && d.Outcome.WicketCreditedToBowler),
                        RunsConceded: g.Sum(d => d.Outcome.Type is DeliveryOutcomeType.Bye or DeliveryOutcomeType.LegBye ? 0 : d.Outcome.TotalRuns)))
                    .OrderByDescending(x => x.Wickets).ThenBy(x => x.RunsConceded)
                    .FirstOrDefault();

                summaries.Add(new SessionSummary(
                    group.Key.Day, group.Key.Session, innings.BattingTeamName,
                    runs, wickets, legalBalls,
                    topBatter.Id != default && batterNames.TryGetValue(topBatter.Id, out var bn) ? bn : null,
                    topBatter.Runs,
                    topBowler.Id != default && bowlerNames.TryGetValue(topBowler.Id, out var wn) ? wn : null,
                    topBowler.Id != default ? $"{topBowler.Wickets}/{topBowler.RunsConceded}" : null));
            }
        }

        return summaries.OrderBy(s => s.Day).ThenBy(s => s.Session).ToList();
    }

    /// <summary>Issue 7: what the scorecard header should show right now - the day and session of the most recent ball bowled. Null for a limited-overs match.</summary>
    public static (int Day, SessionType Session)? CurrentDayAndSession(MultiDayMatchResult result)
    {
        var lastDelivery = result.Innings
            .SelectMany(i => i.Deliveries)
            .Where(d => d.Day is not null && d.Session is not null)
            .OrderByDescending(d => d.Day).ThenByDescending(d => d.Session).ThenByDescending(d => d.BallNumber)
            .FirstOrDefault();

        return lastDelivery is null ? null : (lastDelivery.Day!.Value, lastDelivery.Session!.Value);
    }

    private double BattingRatingFor(BatterCard card, MatchFormat format) =>
        _ratings.RateBattingInnings(new BattingInningsRecord
        {
            PlayerId = card.PlayerId,
            Runs = card.Runs,
            BallsFaced = card.BallsFaced,
            NotOut = !card.IsOut
        }, format);

    /// <summary>
    /// `OversBowled` here MUST be `LegalBallsBowled / 6.0` - true decimal overs - and NOT
    /// `BowlerCard.Overs`, which uses the scorecard "X.Y" display convention where the fractional
    /// part is balls-in-the-over, not tenths (3 balls reads as .3, not .5). RateBowlingSpell
    /// divides RunsConceded by this value to get economy; feeding it the display value would
    /// silently understate every incomplete over's true economy. MatchRecorder makes the same
    /// conversion for the persisted version of this record - this mirrors it exactly.
    /// </summary>
    private double BowlingRatingFor(BowlerCard card, MatchFormat format) =>
        _ratings.RateBowlingSpell(new BowlingSpellRecord
        {
            PlayerId = card.PlayerId,
            Wickets = card.Wickets,
            RunsConceded = card.RunsConceded,
            OversBowled = card.LegalBallsBowled / 6.0
        }, format);

    private static string DescribeBatting(BatterCard card) =>
        $"{card.Runs}{(card.IsOut ? "" : "*")} ({card.BallsFaced}b, {card.Fours}x4, {card.Sixes}x6) for {card.PlayerName}";

    private static string DescribeBowling(BowlerCard card) =>
        $"{card.Figures} in {card.Overs:0.0} overs for {card.PlayerName}";

    // ---------------- moments ----------------

    private static void AddIfPresent(List<MatchMoment> list, MatchMoment? moment)
    {
        if (moment is not null) list.Add(moment);
    }

    /// <summary>
    /// The day a given ball number fell on, read straight from the innings' own delivery log -
    /// Issue 1's fix. Null for a limited-overs innings (MatchTimeline is never threaded through
    /// one, so its deliveries were never tagged - correctly meaning "no such question") and,
    /// before MatchTimeline existed, ALWAYS null for a multi-day one too, which is why every
    /// multi-day key moment used to print "[Day ?]".
    /// </summary>
    private static int? DayOfBall(InningsState innings, int ballNumber) =>
        innings.Deliveries.FirstOrDefault(d => d.BallNumber == ballNumber)?.Day;

    /// <summary>
    /// The most severe run of 3 wickets falling within 24 legal balls (4 overs) - "most severe"
    /// meaning fewest runs conceded across the window, not just the first one found, so a genuine
    /// slide gets reported over an incidental cluster of three wickets in an otherwise steady innings.
    /// </summary>
    private static MatchMoment? DetectCollapse(InningsState innings)
    {
        var fow = innings.FallOfWickets;
        MatchMoment? worst = null;
        int worstRuns = int.MaxValue;

        for (int i = 0; i + 2 < fow.Count; i++)
        {
            if (fow[i + 2].BallNumber - fow[i].BallNumber > 24) continue;

            int runsBefore = i > 0 ? fow[i - 1].Score : 0;
            int runsInWindow = fow[i + 2].Score - runsBefore;
            if (runsInWindow >= worstRuns) continue;

            worstRuns = runsInWindow;
            int overOfFirst = fow[i].BallNumber / 6 + 1;
            worst = new MatchMoment(DayOfBall(innings, fow[i + 2].BallNumber),
                $"A collapse for {innings.BattingTeamName} - 3 wickets for {runsInWindow} runs, starting around over {overOfFirst}.",
                MomentSeverity.Major);
        }

        return worst;
    }

    /// <summary>The largest partnership of the innings, if it's actually substantial - a 12-run
    /// stand isn't a "moment" and shouldn't crowd out ones that are.</summary>
    private static MatchMoment? BiggestPartnership(InningsState innings)
    {
        if (innings.Partnerships.Count == 0) return null;

        var best = innings.Partnerships.OrderByDescending(p => p.Runs).First();
        if (best.Runs < 40) return null;

        string batterA = innings.BatterCards.FirstOrDefault(c => c.PlayerId == best.BatterAId)?.PlayerName ?? "one batter";
        string batterB = innings.BatterCards.FirstOrDefault(c => c.PlayerId == best.BatterBId)?.PlayerName ?? "the other";

        // A broken partnership ends AT the wicket that closes it - look that up by wicket number.
        // An UNBROKEN one (the not-out pair at the close) has no matching FallOfWicket at all, so
        // it is dated to the innings' own last ball instead - the day play actually stopped.
        var endingWicket = innings.FallOfWickets.FirstOrDefault(f => f.WicketNumber == best.WicketNumber);
        int? day = endingWicket is not null
            ? DayOfBall(innings, endingWicket.BallNumber)
            : innings.Deliveries.Count > 0 ? innings.Deliveries[^1].Day : null;

        return new MatchMoment(day,
            $"A {best.Runs}-run stand between {batterA} and {batterB} for {innings.BattingTeamName}, off {best.Balls} balls.",
            best.Runs >= 80 ? MomentSeverity.Major : MomentSeverity.Notable);
    }

    /// <summary>
    /// The toughest point of a run chase - the highest required run rate reached, replaying the
    /// delivery log rather than reading InningsState's own RequiredRunRate, which only reflects
    /// the FINAL ball, not the peak along the way. Null for a first innings (no target) or an
    /// innings that never had a genuine ask (the chase was cruising throughout).
    /// </summary>
    private static MatchMoment? PeakChasePressure(InningsState innings)
    {
        if (innings.Target is not { } target || innings.MaxLegalBalls is not { } maxBalls) return null;

        double peakRate = 0;
        int peakOver = 0;
        int? peakDay = null;
        int legalBallsSoFar = 0;

        foreach (var delivery in innings.Deliveries)
        {
            if (delivery.Outcome.IsLegalDelivery) legalBallsSoFar++;
            int ballsLeft = Math.Max(0, maxBalls - legalBallsSoFar);
            if (ballsLeft == 0) continue;

            int required = Math.Max(0, target - delivery.ScoreAfter);
            double rate = required / (ballsLeft / 6.0);
            if (rate > peakRate) { peakRate = rate; peakOver = delivery.OverNumber; peakDay = delivery.Day; }
        }

        if (peakRate <= 0) return null;

        return new MatchMoment(peakDay,
            $"The chase peaked at {peakRate:0.0} runs an over required, around over {peakOver}.",
            peakRate >= 12 ? MomentSeverity.Major : MomentSeverity.Notable);
    }

    /// <summary>
    /// A large swing in either side's own win-chance read between consecutive sessions - the
    /// closest thing to "the moment the match turned" a multi-day match has, quoted in the
    /// dressing room's own words via SessionAssessment.Reasoning rather than a conclusion this
    /// service draws itself.
    /// </summary>
    private static IEnumerable<MatchMoment> AssessmentSwings(
        IReadOnlyList<SessionAssessment> assessments, IReadOnlyDictionary<Guid, string> teamNames)
    {
        foreach (var group in assessments.GroupBy(a => a.TeamId))
        {
            var ordered = group.OrderBy(a => a.Day).ThenBy(a => a.Session).ToList();
            for (int i = 1; i < ordered.Count; i++)
            {
                double swing = Math.Abs(ordered[i].WinChance - ordered[i - 1].WinChance);
                if (swing < 25) continue;

                string team = teamNames.TryGetValue(ordered[i].TeamId, out var name) ? name : "One side";
                yield return new MatchMoment(ordered[i].Day,
                    $"{team}, day {ordered[i].Day}: {ordered[i].Reasoning}",
                    swing >= 40 ? MomentSeverity.Major : MomentSeverity.Notable);
            }
        }
    }
}
