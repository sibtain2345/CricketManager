using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 7, Slice 7.6, reworked in the Post-Phase-7/8/9 rectification pass (Section A) against the
/// ACTUAL ICC Code of Conduct structure:
///
/// - <b>Four levels of offence.</b> Level 1 (dissent, excessive appealing, obscenity, equipment
///   abuse): reprimand or a fine up to 50% of the match fee, 1-2 demerit points. Level 2 (serious
///   dissent, deliberate contact, intimidating conduct): a fine of 50-100% of the match fee, 3-4
///   demerit points. Level 3 (intimidating an umpire, threatening behaviour): 100% match fee and
///   suspension points. Level 4 (assault, threatening an umpire, hate speech): a ban outright,
///   6-8 demerit points.
/// - <b>Fines are a percentage of the match fee</b>, not a flat number - the match fee itself is
///   derived from the player's contract wage.
/// - <b>Demerit points age out of a rolling 24-month window</b> (Player.DemeritLog /
///   DemeritPointsIn), not a "lose one every six months" timer. <b>4 points in that window
///   converts to a suspension</b> - the real trigger, corrected from the old value of 8.
/// - <b>Team management is covered by the same code</b> - a coach can be charged, and it happens
///   at the press conference (PressConferenceService wires a confrontational, badly-handled answer
///   into ChargeCoach), not on the field.
///
/// Over-rate penalties (a real, separate mechanic - a seam-heavy attack cannot bowl its overs in
/// time) are unchanged in shape and pushed at the club, with the captain picking up demerit points
/// for a repeat offence.
/// </summary>
public sealed class DisciplineService
{
    /// <summary>Demerit points within the rolling 24-month window that convert to a suspension - the actual ICC threshold (was 8).</summary>
    public const int SuspensionThreshold = 4;

    public IReadOnlyList<GameEvent> ReviewMatch(
        WorldState world, DateOnly date, MatchFormat format,
        Team homeTeam, Team awayTeam,
        IReadOnlyList<Player> homeBowlers, IReadOnlyList<Player> awayBowlers,
        IReadOnlyList<Player> homeXi, IReadOnlyList<Player> awayXi,
        Guid? homeCaptainId, Guid? awayCaptainId, double importance, Random random,
        double overRateSeverity = 1.0, double refereeConsistency = 0.6, string refereeName = "the match referee")
    {
        var events = new List<GameEvent>();

        events.AddRange(ReviewOverRate(world, date, format, homeTeam, homeBowlers, homeCaptainId, homeXi, random, overRateSeverity));
        events.AddRange(ReviewOverRate(world, date, format, awayTeam, awayBowlers, awayCaptainId, awayXi, random, overRateSeverity));

        events.AddRange(ReviewConduct(world, date, format, homeTeam, homeXi, importance, random, refereeConsistency, refereeName));
        events.AddRange(ReviewConduct(world, date, format, awayTeam, awayXi, importance, random, refereeConsistency, refereeName));

        return events;
    }

    // ---------------- over rate ----------------

    private IEnumerable<GameEvent> ReviewOverRate(
        WorldState world, DateOnly date, MatchFormat format, Team team,
        IReadOnlyList<Player> bowlers, Guid? captainId, IReadOnlyList<Player> xi, Random random,
        double severity = 1.0)
    {
        if (bowlers.Count == 0) yield break;

        int quicks = bowlers.Count(p => p.BowlingRole != BowlingRoleType.NotABowler && p.Bowling.Pace >= 13);
        double paceShare = quicks / (double)bowlers.Count;

        double shortfallRisk = Math.Clamp((paceShare - 0.55) * 1.4, 0, 0.85);
        if (format == MatchFormat.Test) shortfallRisk *= 1.2;

        // Phase 15 (§16.5): a competition's own playing conditions scale how hard this is punished -
        // a lenient domestic competition, a strict flagship league.
        shortfallRisk *= Math.Clamp(severity, 0, 2);

        if (random.NextDouble() >= shortfallRisk * 0.5) yield break;

        double fine = Math.Round((4_000 + random.NextDouble() * 8_000) * Math.Clamp(severity, 0.3, 2), 0);
        team.Finances.Budget -= fine;

        var captain = captainId is { } cid ? xi.FirstOrDefault(p => p.Id == cid) : null;
        string tail = "";
        if (captain is not null && shortfallRisk > 0.5 && random.NextDouble() < 0.35)
        {
            var ban = AddDemerit(captain, date, format, level: 1, points: 1, "a slow over-rate");
            tail = ban is null
                ? $" {captain.FullName} is warned over the slow over-rate."
                : $" {captain.FullName} {ban}";
        }

        yield return new GameEvent(date, GameEventType.DisciplinaryAction,
            $"{team.Name} are fined {fine:N0} for a slow over-rate.{tail}", team.Id, captain?.Id);
    }

    // ---------------- code of conduct: four levels ----------------

    private IEnumerable<GameEvent> ReviewConduct(
        WorldState world, DateOnly date, MatchFormat format, Team team, IReadOnlyList<Player> xi, double importance, Random random,
        double refereeConsistency = 0.6, string refereeName = "the match referee")
    {
        double occasionFactor = 0.7 + Math.Clamp((importance - 50) / 50.0, 0, 1) * 0.6;

        foreach (var player in xi.OrderBy(p => p.LastName).ThenBy(p => p.FirstName))
        {
            double professionalism = AbilityScale.AttributeToHundred(player.Mental.Professionalism);
            double baseRate = Math.Clamp((55 - professionalism) / 55.0, 0, 1) * 0.02;
            if (player.Personality.HasFlag(PersonalityTrait.Aggressive)) baseRate += 0.012;
            if (player.Personality.HasFlag(PersonalityTrait.RiskTaker)) baseRate += 0.004;
            if (player.Personality.HasFlag(PersonalityTrait.Professional)) baseRate *= 0.4;

            if (random.NextDouble() >= baseRate * occasionFactor) continue;

            double matchFee = MatchFee(world, player, format);

            // Level distribution: mostly Level 1, occasionally Level 2, rarely 3, very rarely 4.
            double roll = random.NextDouble();
            int level = roll < 0.62 ? 1 : roll < 0.90 ? 2 : roll < 0.985 ? 3 : 4;

            var (points, finePctLo, finePctHi, chargeText) = level switch
            {
                1 => (1 + (random.NextDouble() < 0.4 ? 1 : 0), 0.10, 0.50, "dissent at an umpire's decision"),
                2 => (3 + (random.NextDouble() < 0.5 ? 1 : 0), 0.50, 1.00, "conduct that brought the game into disrepute"),
                3 => (5 + (random.NextDouble() < 0.5 ? 1 : 0), 1.00, 1.00, "intimidating an official"),
                _ => (6 + random.Next(0, 3), 1.00, 1.00, "a serious breach of the code of conduct"),
            };

            // §16.2: the match referee runs the hearing. A firm, consistent referee applies the
            // code at the upper end and never quietly drops a charge; a lenient one is erratic -
            // the fine is lighter and a first-offence Level 1 is sometimes reduced to a reprimand.
            double finePct = finePctLo + random.NextDouble() * (finePctHi - finePctLo);
            finePct = Math.Clamp(finePct * Math.Clamp(0.55 + refereeConsistency * 0.75, 0.55, 1.25), 0, 1.25);
            // A lenient referee reduces a genuine first-offence Level 1 to a reprimand. Deterministic
            // (first offence + lenient referee), so it does not perturb the per-fixture RNG stream.
            bool reprimandOnly = level == 1 && refereeConsistency < 0.45 && player.DemeritPointsIn(date) == 0;
            if (reprimandOnly) { finePct = 0; points = Math.Min(points, 1); }
            double fine = Math.Round(matchFee * finePct, 0);
            if (fine > 0) Fine(team, player, fine);

            // Level 3+ carries a ban regardless of the points total; Level 1-2 only bans on
            // crossing the rolling threshold.
            string outcome = AddDemerit(player, date, format, level, points, chargeText) ?? "";
            if (level >= 3 && string.IsNullOrEmpty(outcome))
            {
                Suspend(player, date, DirectBanDays(format, level));
                outcome = $"is banned for {(level == 3 ? "one match" : "several matches")}";
            }

            string tail = string.IsNullOrEmpty(outcome) ? "" : $" - he {outcome}";
            string sanction = reprimandOnly
                ? $"reprimanded by {refereeName} for {chargeText}"
                : $"charged (Level {level}) by {refereeName} for {chargeText} and fined {fine:N0}";
            yield return new GameEvent(date, GameEventType.DisciplinaryAction,
                $"{player.FullName} ({team.Name}) is {sanction}{tail}.",
                player.Id, team.Id);
        }
    }

    /// <summary>
    /// Section A: a HEAD COACH is charged under the same code - wired from PressConferenceService
    /// when a confrontational answer from a poor media-handler crosses the line. Fine + demerit
    /// points on the coach; a repeat offender loses real board trust (a touchline ban has no
    /// selection effect, so it lands on standing, not availability).
    /// </summary>
    public GameEvent? ChargeCoach(Coach coach, Team team, DateOnly date, int level, string reason, Random random)
    {
        int points = level switch { 1 => 1 + (random.NextDouble() < 0.4 ? 1 : 0), 2 => 3, _ => 4 };
        double coachFee = Math.Max(6_000, (team.Board.SeasonBudget > 0 ? team.Board.SeasonBudget : 200_000) / 60.0);
        double fine = Math.Round(coachFee * (0.25 + random.NextDouble() * 0.75), 0);

        coach.CareerFines += fine;
        team.Finances.Budget -= fine * 0.4;
        coach.DemeritPoints += points;
        coach.CareerSatisfaction = Math.Clamp(coach.CareerSatisfaction - 2, 0, 100);

        string tail = "";
        if (coach.DemeritPoints >= SuspensionThreshold)
        {
            coach.BoardTrust = Math.Clamp(coach.BoardTrust - 8, 0, 100);
            coach.DemeritPoints = 0;
            tail = " It earns him a touchline ban and a hard word from the board.";
        }

        return new GameEvent(date, GameEventType.CoachCharged,
            $"{coach.FullName} ({team.Name}) is charged under the code of conduct for {reason} - fined {fine:N0}.{tail}",
            coach.Id, team.Id);
    }

    /// <summary>
    /// §2.15: leg theory - a sustained barrage of short, into-the-body bowling - carries a real
    /// conduct cost, not just a tactical one. Real, observable and rules-based (a genuine barrage
    /// in one innings, not a single over of short stuff), so it is judged deterministically
    /// rather than a probability roll - it never perturbs the shared match RNG stream, the same
    /// discipline the referee's first-offence reprimand above already follows. A Level 2 charge:
    /// serious, but short of intimidating an official.
    /// </summary>
    public GameEvent? ReviewIntimidatoryBowling(WorldState world, DateOnly date, MatchFormat format, Team team, Player bowler, int legTheoryDeliveries)
    {
        const int Threshold = 18; // three-plus overs' worth of it in a single innings
        if (legTheoryDeliveries < Threshold) return null;

        double matchFee = MatchFee(world, bowler, format);
        double fine = Math.Round(matchFee * 0.65, 0);
        Fine(team, bowler, fine);
        string outcome = AddDemerit(bowler, date, format, level: 2, points: 3, "sustained intimidatory bowling") ?? "";
        string tail = string.IsNullOrEmpty(outcome) ? "" : $" - he {outcome}";
        return new GameEvent(date, GameEventType.DisciplinaryAction,
            $"{bowler.FullName} ({team.Name}) is charged (Level 2) for a sustained barrage of short, into-the-body bowling and fined {fine:N0}{tail}.",
            bowler.Id, team.Id);
    }

    // ---------------- monthly review: expiry, not decay ----------------

    /// <summary>Monthly: clears expired bans. Demerit points now age out of the 24-month window on their own (DemeritLog) rather than being decayed here - the cache is just refreshed.</summary>
    public IReadOnlyList<GameEvent> ReviewSuspensions(WorldState world, DateOnly date)
    {
        var events = new List<GameEvent>();
        foreach (var player in world.Players.Where(p => !p.IsRetired))
        {
            if (player.SuspendedUntil is { } until && until <= date)
            {
                player.SuspendedUntil = null;
                if (player.NonInjuryUnavailability == UnavailabilityReason.Suspended)
                    player.NonInjuryUnavailability = UnavailabilityReason.Available;
                events.Add(new GameEvent(date, GameEventType.DisciplinaryAction,
                    $"{player.FullName} is available again after serving his ban.", player.Id));
            }

            // Refresh the cached rolling total, and prune log entries older than the window.
            if (player.DemeritLog.Count > 0)
            {
                var cutoff = date.AddMonths(-24);
                player.DemeritLog.RemoveAll(e => e.Date <= cutoff);
                player.DemeritPoints = player.DemeritPointsIn(date);
            }
        }
        return events;
    }

    // ---------------- helpers ----------------

    /// <summary>
    /// Appends a dated charge to the player's record, refreshes his rolling total, and - if the
    /// charge takes him across a suspension boundary (every 4 points in the 24-month window) -
    /// issues a ban. Returns a short outcome phrase, or null when no ban resulted.
    /// </summary>
    private static string? AddDemerit(Player player, DateOnly date, MatchFormat format, int level, int points, string reason)
    {
        int before = player.DemeritPointsIn(date);
        player.DemeritLog.Add(new DemeritEntry(date, points, level, reason));
        int after = player.DemeritPointsIn(date);
        player.DemeritPoints = after;

        // Crossed a multiple-of-4 boundary?
        if (after / SuspensionThreshold > before / SuspensionThreshold)
        {
            Suspend(player, date, DirectBanDays(format, Math.Max(1, level)));
            return $"reaches {after} demerit points in 24 months - he is suspended for the next match";
        }
        return null;
    }

    private static int DirectBanDays(MatchFormat format, int level)
    {
        // 2 suspension points = 1 Test OR 2 white-ball matches. Approximated as calendar days off
        // in a schedule the engine does not track match-by-match here.
        int baseDays = format == MatchFormat.Test ? 14 : 10;
        return baseDays * Math.Clamp(level, 1, 4);
    }

    private static double MatchFee(WorldState world, Player player, MatchFormat format)
    {
        var contract = PlayerContractService.ActiveDomesticContract(world, player.Id);
        double wage = contract?.AnnualWage ?? new WageBillService().PlayerWage(player);
        double perMatch = wage / 16.0; // ~a season's worth of matches
        double formatFactor = format == MatchFormat.Test ? 1.3 : 1.0;
        return Math.Max(2_000, perMatch * formatFactor);
    }

    private static void Fine(Team team, Player player, double amount)
    {
        team.Finances.Budget -= amount * 0.3;
        player.CareerFines += amount;
    }

    private static void Suspend(Player player, DateOnly date, int days)
    {
        var until = date.AddDays(days);
        if (player.SuspendedUntil is null || player.SuspendedUntil < until)
            player.SuspendedUntil = until;
        player.NonInjuryUnavailability = UnavailabilityReason.Suspended;
    }
}
