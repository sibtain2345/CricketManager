using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>One recommended plan for one of our bowlers against one of their batters, as it would appear in a report.</summary>
public sealed record MatchupRecommendation(
    Guid BowlerId,
    string BowlerName,
    BowlingApproach Approach,
    double ExpectedValue,
    string Reasoning);

/// <summary>
/// What the analyst believes about one opposition batter. Everything here is an ESTIMATE, and a
/// poor analyst's estimates are wrong - see AnalystService for why that matters.
/// </summary>
public sealed record BatterDossier(
    Guid PlayerId,
    string PlayerName,
    BattingZoneStrengths EstimatedZones,
    IReadOnlyList<IdentifiedWeakness> Weaknesses,
    IReadOnlyList<MatchupRecommendation> Recommendations,
    /// <summary>0-100. How much the analyst himself trusts this dossier. Low confidence is honest; it is not the same as being wrong.</summary>
    double Confidence,
    string Summary);

/// <summary>What the opposition will try to do to one of our own players. A good analyst tells you where you are exposed, not only where they are.</summary>
public sealed record OwnPlayerExposure(
    Guid PlayerId,
    string PlayerName,
    IReadOnlyList<IdentifiedWeakness> LikelyTargets,
    string Advice);

/// <summary>The pre-match analysis, as delivered to the coach.</summary>
public sealed record AnalystReport(
    Guid? AnalystId,
    string AnalystName,
    IReadOnlyList<BatterDossier> Opposition,
    IReadOnlyList<OwnPlayerExposure> OurExposures,
    /// <summary>0-100. Overall quality of the work. Drives how much of the truth made it onto the page.</summary>
    double ReportQuality,
    string Headline);

/// <summary>
/// The analyst's desk. Section 34 asks for analytics that genuinely help decisions; this is the
/// layer that produces them, and the staff member who produces them decides how good they are.
///
/// The design principle that makes this interesting rather than a lookup table: **an analyst report
/// is an ESTIMATE of the truth, not the truth.** A weak analyst produces a report that is
/// confidently wrong - he reports a weakness the batter does not have, misses the one he does, and
/// recommends a plan that plays to the man's strength. Following it costs you the match, and you
/// have no way of knowing that in advance beyond the confidence he attaches to it. That is exactly
/// what a bad analyst is worth in real sport, and it is the same modelling choice already made for
/// scouting (ScoutingAccuracyService), for the same reason.
///
/// Coverage matters too: a diligent analyst does the whole opposition, a lazy one does the famous
/// names and leaves the tail as guesswork.
/// </summary>
public sealed class AnalystService
{
    private readonly PlanFitService _planFit = new();

    /// <summary>
    /// Prepares a pre-match report. With no analyst on the staff, the coach gets a thin report
    /// assembled from his own eyes - which is what a side without an analyst actually has.
    /// </summary>
    public AnalystReport PrepareReport(
        StaffMember? analyst,
        IReadOnlyList<Player> ourBowlers,
        IReadOnlyList<Player> oppositionBatters,
        IReadOnlyList<Player> ourBatters,
        IReadOnlyList<Player> oppositionBowlers,
        PitchConditions pitch,
        MatchFormat format,
        Random random,
        double staffAnalysisBoost = 0)
    {
        // Follow-up: a hired DATA analyst / a deeper analysis department (SpecialistStaffService.
        // AnalysisQualityBoost, 0..14) lifts the report's quality on top of the lead analyst's own
        // rating. Default 0 keeps every existing caller identical.
        double quality = Math.Clamp((analyst?.EffectiveWithExperience ?? 25) + staffAnalysisBoost, 0, 100);
        string name = analyst?.FullName ?? "No analyst";

        // Diligence decides how much of the opposition actually gets covered. The rest is guesswork
        // dressed up as analysis, which is why the tail so often surprises a badly prepared side.
        double coverage = analyst is null ? 0.4 : 0.45 + AbilityScale.AttributeToHundred(analyst.Diligence) / 100.0 * 0.55;
        int covered = Math.Max(1, (int)Math.Round(oppositionBatters.Count * coverage));

        var dossiers = oppositionBatters
            .OrderByDescending(p => AbilityScale.CompositeAbilityToHundred(p.CurrentAbility)) // the famous names get done first
            .Take(covered)
            .Select(batter => BuildDossier(batter, ourBowlers, pitch, format, quality, analyst, random))
            .ToList();

        var exposures = ourBatters
            .Select(batter => BuildExposure(batter, oppositionBowlers, pitch, quality, random))
            .Where(e => e.LikelyTargets.Count > 0)
            .ToList();

        string headline = quality switch
        {
            >= 75 => $"{name} has the opposition worked out in detail.",
            >= 50 => $"{name} has done solid work on the opposition.",
            >= 30 => $"{name}'s report covers the basics.",
            _ => $"{name}'s report is thin, and some of it looks like guesswork."
        };

        return new AnalystReport(analyst?.Id, name, dossiers, exposures, Math.Round(quality, 1), headline);
    }

    private BatterDossier BuildDossier(
        Player batter, IReadOnlyList<Player> ourBowlers, PitchConditions pitch, MatchFormat format,
        double quality, StaffMember? analyst, Random random)
    {
        // The truth, then the analyst's reading of it. Everything downstream uses the ESTIMATE.
        var trueZones = BattingZoneStrengths.FromPlayer(batter);
        var estimatedZones = EstimateZones(trueZones, quality, random);

        var weaknesses = IdentifyWeaknesses(batter, quality, random);

        // Recommendations are built from the ESTIMATED picture, so a wrong reading produces a wrong
        // plan - which is the whole point. PlanFitService still applies its real logic on top, so a
        // plan the bowler cannot bowl is still rejected even when the analysis is confident.
        var recommendations = ourBowlers
            .Select(bowler =>
            {
                var recommendation = _planFit.RecommendPlan(bowler, batter, pitch, MatchPhase.MiddleOvers, format);
                var approach = recommendation.Approach with
                {
                    TargetWeakZone = Enum.GetValues<ShotZone>().OrderBy(z => estimatedZones[z]).First()
                };

                return new MatchupRecommendation(bowler.Id, bowler.FullName, approach,
                    Math.Round(recommendation.Score, 2), recommendation.Reasoning);
            })
            .OrderByDescending(r => r.ExpectedValue)
            .ToList();

        // Communication decides how much of the work survives contact with the dressing room.
        double communication = analyst is null ? 40 : AbilityScale.AttributeToHundred(analyst.Communication);
        double confidence = Math.Clamp(quality * 0.75 + communication * 0.25, 0, 100);

        string summary = weaknesses.Count == 0
            ? $"{batter.FullName}: no clear weakness identified."
            : $"{batter.FullName}: {string.Join(", ", weaknesses)}. Best option is {recommendations.FirstOrDefault()?.BowlerName ?? "unclear"}.";

        return new BatterDossier(batter.Id, batter.FullName, estimatedZones, weaknesses, recommendations,
            Math.Round(confidence, 1), summary);
    }

    /// <summary>
    /// The analyst's reading of where a batter scores. Noise is inversely proportional to his
    /// quality, so a poor analyst's wagon wheel points at the wrong areas and a good one's is close
    /// to the truth. Never exact, even at the top - nobody has perfect information.
    /// </summary>
    public BattingZoneStrengths EstimateZones(BattingZoneStrengths truth, double analystQuality, Random random)
    {
        double error = 35 - Math.Clamp(analystQuality, 0, 100) / 100.0 * 30; // +/-5 at the top, +/-35 at the bottom
        var estimate = new BattingZoneStrengths();

        foreach (var zone in Enum.GetValues<ShotZone>())
            estimate[zone] = Math.Clamp(truth[zone] + (random.NextDouble() * 2 - 1) * error, 0, 100);

        return estimate;
    }

    /// <summary>
    /// What the analyst believes the batter struggles with.
    ///
    /// Two independent failure modes, and both are real: a poor analyst MISSES genuine weaknesses,
    /// and separately he INVENTS ones that are not there. The second is the dangerous one, because
    /// acting on it means bowling to a strength while believing you are attacking a flaw.
    /// </summary>
    public IReadOnlyList<IdentifiedWeakness> IdentifyWeaknesses(Player batter, double analystQuality, Random random)
    {
        var found = new List<IdentifiedWeakness>();
        double accuracy = Math.Clamp(analystQuality, 0, 100) / 100.0;

        void Consider(int attribute, IdentifiedWeakness weakness)
        {
            double level = AbilityScale.AttributeToHundred(attribute);
            bool genuine = level < 45;

            if (genuine)
            {
                // Spotting a real weakness: near-certain for a top analyst, coin-flip for a poor one.
                if (random.NextDouble() < 0.35 + accuracy * 0.6) found.Add(weakness);
            }
            else if (level > 65)
            {
                // Inventing one that isn't there. Rare for a good analyst, common for a poor one -
                // and this is the failure that actually loses matches.
                if (random.NextDouble() < (1 - accuracy) * 0.28) found.Add(weakness);
            }
        }

        Consider(batter.Batting.ShortBallAbility, IdentifiedWeakness.ShortBall);
        Consider(batter.Batting.Technique, IdentifiedWeakness.FullAndStraight);
        Consider(batter.Batting.ShotSelection, IdentifiedWeakness.ShotSelection);
        Consider(batter.Batting.AgainstSpin, IdentifiedWeakness.AgainstSpin);
        Consider(batter.Batting.AgainstPace, IdentifiedWeakness.AgainstPace);
        Consider(batter.Batting.Timing, IdentifiedWeakness.Timing);
        Consider(batter.Batting.StrikeRotation, IdentifiedWeakness.LegSideRestriction);

        return found;
    }

    /// <summary>
    /// Where our own players are exposed. A good analyst tells you what the opposition will try to
    /// do to you, not only what you can do to them - and a coach who knows can pre-empt it.
    /// </summary>
    private OwnPlayerExposure BuildExposure(
        Player ourBatter, IReadOnlyList<Player> oppositionBowlers, PitchConditions pitch, double quality, Random random)
    {
        var targets = IdentifyWeaknesses(ourBatter, quality, random);

        string advice = targets.Count == 0
            ? $"{ourBatter.FullName} has no obvious hole for them to bowl at."
            : oppositionBowlers.Count == 0
                ? $"{ourBatter.FullName} is vulnerable to {targets[0]}."
                : $"{ourBatter.FullName} is vulnerable to {targets[0]} - expect them to use "
                  + $"{BestExploiterOf(ourBatter, oppositionBowlers, pitch)?.FullName ?? "their quicks"}.";

        return new OwnPlayerExposure(ourBatter.Id, ourBatter.FullName, targets, advice);
    }

    private Player? BestExploiterOf(Player ourBatter, IReadOnlyList<Player> oppositionBowlers, PitchConditions pitch) =>
        oppositionBowlers
            .OrderByDescending(b => _planFit.RecommendPlan(b, ourBatter, pitch, MatchPhase.MiddleOvers, MatchFormat.Test).Score)
            .FirstOrDefault();

    /// <summary>
    /// Turns a report into an actual plan. Without this the analysis is a screen the coach reads
    /// and forgets; with it, the report becomes the matchup instructions his bowlers carry out.
    ///
    /// Only recommendations the analyst is reasonably confident about are applied - a coach acting
    /// on a dossier the analyst himself doubts is making the analyst's mistake his own.
    /// </summary>
    public int ApplyToPlan(AnalystReport report, TacticalPlan plan, double minimumConfidence = 45)
    {
        int applied = 0;

        foreach (var dossier in report.Opposition.Where(d => d.Confidence >= minimumConfidence))
        {
            // The single best matchup per batter. Loading every bowler-batter pair would produce a
            // plan nobody could read and would override the captain's judgement everywhere.
            var best = dossier.Recommendations.FirstOrDefault();
            if (best is null) continue;

            plan.MatchupApproaches[(best.BowlerId, dossier.PlayerId)] = best.Approach;
            applied++;
        }

        return applied;
    }
}
