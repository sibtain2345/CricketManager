using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>What an umpiring panel did in one match - the raw material for careers and news.</summary>
public sealed record UmpiringReport(
    int MarginalDecisions,
    int Howlers,
    double Controversy,               // 0-100
    IReadOnlyList<Guid> OnFieldUmpireIds,
    string Summary);

/// <summary>
/// Phase 7, Slice 7.9: umpires, and their careers. No DRS - an on-field call stands - so how
/// good the panel is genuinely matters, to the batting side especially.
///
/// - <see cref="AssignPanel"/> picks a match's officials from the world panel: the right tier
///   for the occasion, the best available for a big match, and NEUTRAL (not from a participating
///   nation) for internationals where possible.
/// - <see cref="OutBiasFor"/> is the deterministic multiplier the ball model reads
///   (MatchSetup.UmpireOutBias): a weaker or more rattled panel gives more marginal decisions
///   out, and leans a shade toward the home side when the crowd is loud.
/// - <see cref="ReviewMatch"/> is the post-hoc career/news layer: it estimates how many marginal
///   calls the match likely contained, rolls how many the panel got wrong, moves each umpire's
///   reputation, and flags a controversy when a howler lands in a big game.
/// - <see cref="SeasonReview"/> promotes and demotes umpires between panels on reputation and
///   retires the old ones.
/// </summary>
public sealed class UmpireService
{
    /// <summary>Picks the on-field umpires for a fixture. The first two returned are on-field; a third, if available, is the TV/reserve.</summary>
    public IReadOnlyList<Umpire> AssignPanel(
        IReadOnlyList<Umpire> panel, bool isInternational, bool isKnockout, double importance,
        string? homeNationality, string? awayNationality, Random random, DateOnly? date = null)
    {
        var available = panel.Where(u => !u.IsRetired).ToList();
        if (available.Count < 2) return available;

        // Tier: the biggest occasions get the elite panel.
        var preferredTiers = (isInternational, isKnockout || importance >= 80) switch
        {
            (true, true) => new[] { UmpirePanel.Elite, UmpirePanel.International },
            (true, false) => new[] { UmpirePanel.Elite, UmpirePanel.International },
            (false, true) => new[] { UmpirePanel.International, UmpirePanel.Domestic },
            _ => new[] { UmpirePanel.Domestic, UmpirePanel.International }
        };

        var pool = available.Where(u => preferredTiers.Contains(u.Panel)).ToList();
        if (pool.Count < 2) pool = available;

        // Phase 15 (§16.4): a genuinely big occasion keeps an umpire who is under a board query off
        // the appointment - as long as there are enough others so a side is still fielded.
        bool bigOccasion = isKnockout || importance >= 75;
        if (bigOccasion && date is { } d)
        {
            var clear = pool.Where(u => !u.IsUnderScrutiny(d)).ToList();
            if (clear.Count >= 2) pool = clear;
        }

        // Neutrality for internationals - prefer officials from neither side's country. Fatigue
        // (§16.3) pushes a name down the order but never out of contention outright.
        double FatiguePenalty(Umpire u) => Math.Clamp((u.MatchesSinceBreak - 8) * 1.5, 0, 20);
        IEnumerable<Umpire> ranked = isInternational && homeNationality is not null && awayNationality is not null
            ? pool.OrderBy(u => u.Nationality == homeNationality || u.Nationality == awayNationality ? 1 : 0)
                  .ThenByDescending(u => u.Reputation - FatiguePenalty(u))
            : pool.OrderByDescending(u => u.Reputation - FatiguePenalty(u));

        // A small deterministic shuffle among the top handful so it is not always the exact same two.
        var top = ranked.Take(Math.Min(pool.Count, 5)).ToList();
        Shuffle(top, random);
        return top.OrderByDescending(u => u.Reputation).Take(3).ToList();
    }

    /// <summary>
    /// Phase 15 (§16.2): the MATCH REFEREE - a distinct role from the on-field umpires, who runs
    /// the code-of-conduct hearing after the game. Modelled lightly: the most senior official not
    /// standing in this match (former umpires genuinely become referees), or a neutral synthetic
    /// when the pool is thin. Returns the referee's name and a 0..1 CONSISTENCY - a high value
    /// means the code is applied firmly and predictably, a low one means an erratic, lenient
    /// hearing. DisciplineService reads the consistency; nothing here consumes RNG beyond the
    /// small tie-break shuffle.
    /// </summary>
    public (string Name, double Consistency) AssignReferee(
        IEnumerable<Umpire> worldPanel, IReadOnlyList<Umpire> onField, bool isInternational)
    {
        var onFieldIds = onField.Select(u => u.Id).ToHashSet();
        // RNG-free: the appointment is deterministic (most senior clear official), so it does not
        // perturb the per-fixture stream the discipline/umpiring rolls draw from.
        var chosen = worldPanel
            .Where(u => !onFieldIds.Contains(u.Id) && (isInternational ? u.MatchesOfficiated >= 40 : u.MatchesOfficiated >= 15))
            .OrderByDescending(u => u.Reputation + u.EffectiveJudgement * 20)
            .ThenBy(u => u.LastName).ThenBy(u => u.FirstName)
            .FirstOrDefault();
        if (chosen is null) return ("the match referee", 0.6);

        double consistency = Math.Clamp(chosen.EffectiveJudgement * 0.6 + chosen.Composure / 20.0 * 0.4, 0.3, 0.95);
        return ($"{chosen.FullName}", consistency);
    }

    /// <summary>
    /// The ball-model multiplier for MatchSetup.UmpireOutBias. 1.0 for a strong, composed neutral
    /// panel; up to ~1.16 for a weak panel under a loud home crowd in a tense game.
    /// </summary>
    public double OutBiasFor(IReadOnlyList<Umpire>? onField, double importance, double homeAdvantage)
    {
        if (onField is null || onField.Count == 0) return 1.0;

        double avgJudgement = onField.Take(2).Average(u => u.EffectiveJudgement);   // 0-1
        double avgComposure = onField.Take(2).Average(u => u.Composure) / 20.0;     // 0-1
        // Phase 15 (§16.3): a tired panel drifts a shade further toward the marginal call.
        double fatigue = onField.Take(2).Average(u => Math.Clamp((u.MatchesSinceBreak - 8) / 20.0, 0, 1));

        // A poorer panel gives more marginal decisions out.
        double weakness = Math.Clamp(0.78 - avgJudgement + fatigue * 0.06, 0, 0.5);
        // Pressure widens the gap for a low-composure panel.
        double pressure = Math.Clamp((importance - 55) / 45.0, 0, 1) * (1 - avgComposure) * 0.10;
        // A loud home crowd nudges the marginal call the home side's way, more so for a low-composure panel.
        double homeLean = Math.Clamp(homeAdvantage, 0, 0.05) * (1 - avgComposure) * 1.6;

        return Math.Clamp(1.0 + weakness * 0.30 + pressure + homeLean, 1.0, 1.22);
    }

    /// <summary>
    /// Post-match: estimates the marginal decisions the match contained, rolls how many the panel
    /// got wrong, moves reputations, and returns a report. `wicketsByType` is the count of
    /// dismissals of each type across the match; `tightFinish` = decided by a small margin.
    /// </summary>
    public UmpiringReport ReviewMatch(
        IReadOnlyList<Umpire> onField, IReadOnlyDictionary<DismissalType, int> wicketsByType,
        bool tightFinish, double importance, Random random, MatchFormat format = MatchFormat.T20, DateOnly? date = null)
    {
        var twoOnField = onField.Take(2).ToList();
        if (twoOnField.Count == 0)
            return new UmpiringReport(0, 0, 0, Array.Empty<Guid>(), "No umpiring modelled.");

        int lbw = wicketsByType.GetValueOrDefault(DismissalType.LBW);
        int caughtBehind = wicketsByType.GetValueOrDefault(DismissalType.CaughtBehind);

        // Roughly a third of LBWs and a fifth of caught-behinds are genuinely marginal, plus a
        // couple more if the finish was tense (everything is scrutinised).
        int marginal = (int)Math.Round(lbw * 0.35 + caughtBehind * 0.22) + (tightFinish ? 2 : 0);

        double avgJudgement = twoOnField.Average(u => u.EffectiveJudgement);
        // Phase 15 (§16.3): a tired panel makes more errors. MatchesSinceBreak past ~8 climbs it.
        double fatigue = twoOnField.Average(u => Math.Clamp((u.MatchesSinceBreak - 8) / 20.0, 0, 1));
        double errorRate = Math.Clamp(0.9 - avgJudgement + fatigue * 0.12, 0.03, 0.65);

        int howlers = 0;
        for (int i = 0; i < marginal; i++)
            if (random.NextDouble() < errorRate) howlers++;

        double controversy = Math.Clamp(howlers * 22 + (marginal > 0 ? marginal * 3 : 0)
                                        + (howlers > 0 ? importance * 0.3 : 0), 0, 100);

        // Careers move: a clean match lifts reputations a touch, a howler-strewn one dents them.
        // Section A: the career record accumulates too - marginal calls faced, howlers, and a
        // notable controversy in a big game, all queryable off the Umpire profile.
        foreach (var u in twoOnField)
        {
            u.MatchesOfficiated++;
            u.MatchesSinceBreak++;
            u.MatchesByFormat[format] = u.MatchesByFormat.GetValueOrDefault(format) + 1;
            u.CareerMarginalCalls += marginal;
            u.CareerHowlers += howlers;
            if (howlers > 0 && controversy >= 45 && importance >= 70) u.NotableControversies++;

            double repDelta = howlers == 0
                ? 0.3 + importance / 100.0 * 0.4
                : -(howlers * (1.2 + importance / 100.0 * 1.5));
            u.Reputation = Math.Clamp(u.Reputation + repDelta, 0, 100);

            // Phase 15 (§16.4): a genuine howler in a decider draws a board query - he is kept off
            // the big appointments for a spell.
            if (howlers > 0 && importance >= 75 && controversy >= 50 && date is { } d)
                u.UnderScrutinyUntil = d.AddMonths(3);
        }

        string summary = howlers == 0
            ? $"A clean, well-controlled match from {twoOnField[0].FullName} and {twoOnField[^1].FullName}."
            : $"{howlers} clear error{(howlers == 1 ? "" : "s")} from the officials overshadowed the cricket.";

        return new UmpiringReport(marginal, howlers, Math.Round(controversy, 0),
            twoOnField.Select(u => u.Id).ToList(), summary);
    }

    /// <summary>Annual: promotes/demotes umpires between panels on reputation, ages the panel and retires the old ones. Consumes the caller's RNG.</summary>
    public IReadOnlyList<GameEvent> SeasonReview(IList<Umpire> panel, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();

        foreach (var u in panel.Where(x => !x.IsRetired).OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToList())
        {
            // Phase 15 (§16.3): the off-season is a genuine break - the fatigue clock resets, and
            // a board query lifts once its window has passed.
            u.MatchesSinceBreak = 0;
            if (u.UnderScrutinyUntil is { } scr && scr <= date) u.UnderScrutinyUntil = null;

            // Reputation drifts back toward the middle of its panel if not reinforced.
            double panelCentre = u.Panel switch
            {
                UmpirePanel.Elite => 82, UmpirePanel.International => 65, UmpirePanel.Domestic => 48, _ => 35
            };
            u.Reputation = Math.Clamp(u.Reputation + (panelCentre - u.Reputation) * 0.08, 0, 100);

            // Post-Phase-7/8/9 rectification (Section A): panel movement is KPI-driven, not age-
            // driven. Career accuracy over a real sample pulls reputation with it, so an umpire
            // cannot coast in a top panel on a stale reputation, and a genuinely accurate one
            // climbs even if his reputation lagged.
            if (u.CareerMarginalCalls >= 20)
                u.Reputation = Math.Clamp(u.Reputation + (u.CareerAccuracy - 82) * 0.10, 0, 100);

            // Promotion / demotion.
            var newPanel = u.Reputation switch
            {
                >= 78 => UmpirePanel.Elite,
                >= 58 => UmpirePanel.International,
                >= 38 => UmpirePanel.Domestic,
                _ => UmpirePanel.Development
            };
            if (newPanel != u.Panel && u.MatchesOfficiated >= 10)
            {
                bool up = (int)newPanel < (int)u.Panel;
                u.Panel = newPanel;
                u.PanelHistory.Add(newPanel);
                events.Add(new GameEvent(date, GameEventType.SelectionPanelNote,
                    $"Umpire {u.FullName} is {(up ? "promoted to" : "moved to")} the {newPanel} panel "
                    + $"(career accuracy {u.CareerAccuracy:F0}%, {u.MatchesOfficiated} matches).", u.Id));
            }

            // Retirement - umpires go on longer than players, but not forever.
            if (u.Age(date) >= 62 && random.NextDouble() < 0.35 + (u.Age(date) - 62) * 0.08)
            {
                u.IsRetired = true;
                events.Add(new GameEvent(date, GameEventType.PlayerRetired,
                    $"Umpire {u.FullName} retires from the panel after {u.MatchesOfficiated} matches.", u.Id));
            }
        }

        return events;
    }

    private static void Shuffle<T>(IList<T> list, Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
