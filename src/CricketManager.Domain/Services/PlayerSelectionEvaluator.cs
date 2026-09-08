using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

public sealed record SelectionScore(
    Guid PlayerId,
    string PlayerName,
    double TotalScore,
    double AbilityComponent,
    double FormatFitComponent,
    double FormComponent,
    string Explanation);

/// <summary>
/// How heavily a coach weighs each factor. Section 68 is explicit that selection must not be
/// one fixed formula: an analytics coach leans on current numbers, a traditionalist on proven
/// ability, a youth-focused coach on ceiling. The weights were previously three private
/// constants, so every coach in the world selected identically - which quietly removed one of
/// the main things that is supposed to make AI coaches feel like different people.
///
/// Weights are normalised on construction, so a caller can pass any relative numbers.
/// </summary>
public sealed class SelectionWeighting
{
    public double Ability { get; }
    public double FormatFit { get; }
    public double Form { get; }
    public double Potential { get; }

    /// <summary>
    /// Squad-selection follow-up, planning-brief Â§5.2 ("team philosophy/trade-off modeling"):
    /// how many points of "at least as good" margin (0-100 scale) an allrounder can give up in
    /// the specialist's own discipline and still be preferred - see
    /// XiSelectionService.PreferAllrounder. The spec's literal Â§5.1 rule is "at least as good,"
    /// which is AllrounderBias = 0, the default for every profile below; only the two
    /// development-leaning philosophies (which already weight Potential above, unlike every
    /// other profile) get a small positive bias, since backing a promising allrounder at a
    /// mild discount is a direct extension of what those two philosophies already stand for -
    /// not an arbitrary label bolted onto the other four. There is no dedicated
    /// "allrounder-leaning" CoachingPhilosophy value to hang the Hesson/Pakistan-style example
    /// from yet; adding one is a one-line change if a future brief actually needs it, not
    /// something to invent speculatively here.
    /// </summary>
    public double AllrounderBias { get; }

    public SelectionWeighting(double ability, double formatFit, double form, double potential = 0, double allrounderBias = 0)
    {
        double total = ability + formatFit + form + potential;
        if (total <= 0) throw new ArgumentException("Selection weights must sum to more than zero.");
        Ability = ability / total;
        FormatFit = formatFit / total;
        Form = form / total;
        Potential = potential / total;
        AllrounderBias = allrounderBias;
    }

    /// <summary>The balanced default, unchanged from the original constants so existing behaviour is preserved when no philosophy is supplied.</summary>
    public static SelectionWeighting Balanced => new(0.35, 0.30, 0.35);

    /// <summary>
    /// Section 68's examples, made real. Note that no profile lets Potential dominate:
    /// Section 8 is emphatic that current performance beats raw capability when picking a
    /// side, so even the youth-focused coach weighs form and fit above ceiling.
    /// </summary>
    public static SelectionWeighting ForPhilosophy(CoachingPhilosophy philosophy) => philosophy switch
    {
        CoachingPhilosophy.AnalyticsDriven => new(0.25, 0.35, 0.40),
        CoachingPhilosophy.PerformanceFocused => new(0.25, 0.25, 0.50),
        CoachingPhilosophy.ExperienceFocused => new(0.60, 0.25, 0.15),
        CoachingPhilosophy.YouthDevelopment => new(0.20, 0.25, 0.30, 0.25, allrounderBias: 6),
        CoachingPhilosophy.LongTermDevelopment => new(0.25, 0.25, 0.30, 0.20, allrounderBias: 4),
        CoachingPhilosophy.ShortTermResults => new(0.30, 0.25, 0.45),
        CoachingPhilosophy.AllrounderLeaning => new(0.32, 0.28, 0.40, allrounderBias: 9),
        _ => Balanced
    };
}

/// <summary>
/// Section 8: "Performance must matter more than raw capability."
/// Section 23: format-specific selection - a player's value changes with format.
///
/// This is deliberately NOT: OverallRating > Threshold => Select.
/// It blends underlying ability (capped influence), format suitability, and
/// recent form/performance (Section 9/10/11) so an in-form lesser player can
/// legitimately outscore a higher-potential player who is out of form -
/// exactly the Player A / Player B example in the spec.
/// </summary>
public sealed class PlayerSelectionEvaluator
{
    private readonly PlayerAvailabilityService _availability = new();

    /// <summary>
    /// How much benefit-of-the-doubt each SquadStatus tier can carry into a bad patch of
    /// form - a ceiling, not the whole story (standing/experience still modulate within it,
    /// see EstablishedProtection). FirstChoice gets real protection; Fringe/
    /// EmergencyReplacement get essentially none, matching the brief's "a much lower bar for
    /// replacement" for exactly those tiers. DevelopmentProspect sits in the middle
    /// deliberately - a young player being backed through early failures per the brief, but
    /// backed on potential rather than a proven standing, so the ceiling is moderate rather
    /// than a blank check.
    /// </summary>
    private static readonly Dictionary<SquadStatus, double> EstablishedCeiling = new()
    {
        [SquadStatus.FirstChoice] = 1.0,
        [SquadStatus.ReturningFromInjury] = 0.85,
        [SquadStatus.SecondChoice] = 0.7,
        [SquadStatus.LongTermProject] = 0.6,
        [SquadStatus.DevelopmentProspect] = 0.5,
        [SquadStatus.Backup] = 0.3,
        [SquadStatus.Fringe] = 0.05,
        [SquadStatus.EmergencyReplacement] = 0.0
    };

    /// <summary>
    /// Below this form component, an established player gets some benefit of the doubt; at or
    /// above it he is already reading as roughly average or better and needs none. Set just
    /// above the 50 midpoint - "benefit of the doubt", not "immunity from a bad reading".
    /// </summary>
    private const double LeniencyBaseline = 55;

    /// <summary>How far protection can close the gap to the baseline at its absolute maximum (perfect established-ness, zero decline) - a real shield, never a full erasure of a genuine slump.</summary>
    private const double MaxLeniencyPull = 0.6;

    /// <summary>How many CONSECUTIVE poor performances fully fade established-player protection to zero - the same threshold SquadManagementService uses for "prolonged", so the two systems agree on what that word means.</summary>
    private const int ProlongedDeclineStreak = 8;

    private const double PoorPerformanceThreshold = -25;

    /// <summary>Batting-side format suitability - "how good a batter is this player for this format." See FormatSuitability's own doc for why this is a separate, batting-only number.</summary>
    public static double BattingSuitability(Player player, MatchFormat format) => format switch
    {
        MatchFormat.Test => player.FormatSuitability.TestSuitability,
        MatchFormat.ODI => player.FormatSuitability.OdiSuitability,
        MatchFormat.T20 => player.FormatSuitability.T20Suitability,
        _ => 0
    };

    /// <summary>Bowling-side format suitability - the squad-selection follow-up's counterpart, needed so an allrounder's bowling can be judged on its own terms rather than through his batting-flavoured overall score.</summary>
    public static double BowlingSuitability(Player player, MatchFormat format) => format switch
    {
        MatchFormat.Test => player.FormatSuitability.TestBowlingSuitability,
        MatchFormat.ODI => player.FormatSuitability.OdiBowlingSuitability,
        MatchFormat.T20 => player.FormatSuitability.T20BowlingSuitability,
        _ => 0
    };

    /// <summary>
    /// forBowling switches the format-fit term from batting suitability to bowling suitability -
    /// everything else (ability, form, potential, established-player leniency) stays exactly as
    /// it is for every existing caller, since those are whole-player properties in this
    /// codebase, not discipline-specific ones. Added for the squad-selection follow-up's
    /// allrounder-preference rule (XiSelectionService.PreferAllrounder), which needs to compare
    /// an allrounder against a specialist bowler on genuinely bowling terms - false (the
    /// default) preserves every pre-existing call site's behaviour unchanged.
    /// </summary>
    public SelectionScore Evaluate(Player player, MatchFormat format, SelectionWeighting? weighting = null, bool forBowling = false)
    {
        var weights = weighting ?? SelectionWeighting.Balanced;

        // Ability component: current ability scaled 0-100 via the single shared conversion
        // (Common.AbilityScale) - previously this divided by 2 independently of the attribute-
        // scaling used in Player.RecalculateFormatSuitability, two uncoordinated conversions
        // that happened to land in the same range by coincidence rather than by design.
        double abilityComponent = Common.AbilityScale.CompositeAbilityToHundred(player.CurrentAbility);

        double formatFitComponent = forBowling ? BowlingSuitability(player, format) : BattingSuitability(player, format);

        // Form component: CurrentForm is -100..100, rescale to 0-100, then judged against the
        // player's OWN standing rather than a shared bar - see EstablishedProtection. This is
        // the planning brief's Section A core request: a mainstay shouldn't be discarded for a
        // short bad patch the way a fringe player would be, but that protection has to fade
        // once the patch is no longer short.
        double formComponent = (player.Form.CurrentForm + 100) / 2.0;
        if (formComponent < LeniencyBaseline)
        {
            double protection = EstablishedProtection(player);
            formComponent += (LeniencyBaseline - formComponent) * protection * MaxLeniencyPull;
        }

        // Potential only counts for coaches whose philosophy asks for it (weight 0 otherwise).
        double potentialComponent = Common.AbilityScale.CompositeAbilityToHundred(player.PotentialAbility);

        double total =
            abilityComponent * weights.Ability +
            formatFitComponent * weights.FormatFit +
            formComponent * weights.Form +
            potentialComponent * weights.Potential;

        string explanation = BuildExplanation(player, format, abilityComponent, formatFitComponent, formComponent);

        return new SelectionScore(
            player.Id, player.FullName, Math.Round(total, 1),
            Math.Round(abilityComponent, 1), Math.Round(formatFitComponent, 1), Math.Round(formComponent, 1),
            explanation);
    }

    /// <summary>Ranks a pool of players for a format, best first - the basic building block for XI selection.</summary>
    public IReadOnlyList<SelectionScore> Rank(IEnumerable<Player> players, MatchFormat format, SelectionWeighting? weighting = null) =>
        players.Select(p => Evaluate(p, format, weighting)).OrderByDescending(s => s.TotalScore).ToList();

    /// <summary>
    /// Ranks only the players who can ACTUALLY play on the given date, using the coach's own
    /// weighting. This is the method selection should normally use: ranking a squad without
    /// filtering for availability first will happily put an injured player at the top of the
    /// list, which the previous version did.
    ///
    /// A player carrying a niggle stays in the pool but is scored at their reduced
    /// effectiveness, so the coach sees them slightly down the order rather than either
    /// missing entirely or ranked as if fully fit - which is the real trade-off.
    /// </summary>
    public IReadOnlyList<SelectionScore> RankAvailable(
        IEnumerable<Player> players, MatchFormat format, DateOnly matchDate, Coach? coach = null, bool nationalSelection = false)
    {
        var weights = coach is null ? SelectionWeighting.Balanced : SelectionWeighting.ForPhilosophy(coach.Philosophy);

        return players
            .Select(p => (Player: p, Availability: _availability.GetAvailability(p, matchDate, format, nationalSelection)))
            .Where(x => x.Availability.IsAvailable)
            .Select(x =>
            {
                var score = Evaluate(x.Player, format, weights);
                if (x.Availability.EffectivenessIfPlayed >= 1.0) return score;

                return score with
                {
                    TotalScore = Math.Round(score.TotalScore * x.Availability.EffectivenessIfPlayed, 1),
                    Explanation = score.Explanation + " " + x.Availability.Explanation
                };
            })
            .OrderByDescending(s => s.TotalScore)
            .ToList();
    }

    /// <summary>
    /// 0-1: how much benefit of the doubt this specific player's CURRENT bad patch has earned,
    /// combining three things that must all agree for real protection to apply:
    /// - SquadStatus sets the CEILING (a Fringe player gets essentially none, however
    ///   reputable he once was - status is a coach's own current judgement of standing, and
    ///   that judgement should win over a stale reputation number);
    /// - reputation and experience modulate WITHIN that ceiling, so a nominal FirstChoice with
    ///   genuinely low reputation/experience (a fast-rising rookie just promoted, say) doesn't
    ///   get the full shield a genuine mainstay earns - the label and the standing have to
    ///   agree;
    /// - the streak of consecutive poor performances fades protection to zero as the patch
    ///   stops being short - see ProlongedDeclineStreak. This is what makes the difference
    ///   between "shouldn't be dropped after a short bad run" and "eventually droppable" a
    ///   real, gradual curve rather than two different code paths.
    /// </summary>
    private static double EstablishedProtection(Player player)
    {
        double ceiling = EstablishedCeiling.GetValueOrDefault(player.SquadStatus, 0.3);
        double standingFactor = Math.Clamp(Math.Max(player.Reputation.Domestic, player.Reputation.Continental) / 100.0, 0, 1);
        double experienceFactor = Math.Clamp(player.Experience.Level / 100.0, 0, 1);

        double establishedness = ceiling * (0.5 + 0.3 * standingFactor + 0.2 * experienceFactor);

        int poorStreak = player.Form.ConsecutivePoorPerformances(PoorPerformanceThreshold);
        double declineFade = Math.Clamp(1 - (double)poorStreak / ProlongedDeclineStreak, 0, 1);

        return establishedness * declineFade;
    }

    private static string BuildExplanation(Player p, MatchFormat format, double ability, double formatFit, double form)
    {
        var formDesc = form >= 65 ? "excellent recent form" : form >= 45 ? "steady form" : "poor recent form";
        var fitDesc = formatFit >= 65 ? $"strong {format} suitability" : formatFit >= 45 ? $"average {format} suitability" : $"weak {format} fit";
        return $"{p.FullName}: {formDesc}, {fitDesc}, underlying ability {ability:F0}/100.";
    }
}
