using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-Phase-6 (section A): "how good would this person be in THIS role" - for any coach or any
/// staff member, against any staff role or the head-coach job. The FM idea that any staff member
/// can be evaluated for any position, made real for this codebase's split Coach / StaffMember
/// entities.
///
/// Also owns the two "external factors" the brief threads through the whole job market:
/// <see cref="CultureFit"/> (a candidate's philosophy against a club's cultural identity) and
/// <see cref="RelocationComfort"/> (section G - adaptability against how different a move is).
/// </summary>
public sealed class RoleFitService
{
    // ---------------- head coach ----------------

    public double HeadCoachFit(Coach c)
    {
        var a = c.Attributes;
        double S(int x) => AbilityScale.AttributeToHundred(x);
        double core = S(a.TacticalKnowledge) * 0.22 + S(a.Leadership) * 0.18 + S(a.ManManagement) * 0.16
                      + S(a.DecisionMaking) * 0.14 + S(a.PlayerManagement) * 0.12 + S(a.MatchPreparation) * 0.10
                      + S(a.MediaHandling) * 0.08;
        // A licence and a playing pedigree matter for the top job.
        double licence = c.License switch
        {
            CoachingLicense.InternationalElite => 10, CoachingLicense.Advanced => 7, CoachingLicense.Level3 => 4,
            CoachingLicense.Level2 => 2, _ => 0
        };
        return Math.Clamp(core + licence, 0, 100);
    }

    /// <summary>A staff member has to have real standing before he is a credible head-coach applicant at all - most specialists never are.</summary>
    public bool HeadCoachFitEligible(StaffMember s) =>
        s.Role is StaffRole.AssistantCoach or StaffRole.BattingCoach or StaffRole.BowlingCoach or StaffRole.GeneralManager
        && s.EffectiveWithExperience >= 58 && s.Reputation >= 50 && s.YearsExperience >= 5;

    public double HeadCoachFit(StaffMember s)
    {
        // A specialist stepping up: his experience-weighted quality, plus communication (the
        // people-leadership proxy), plus his own earned reputation. A high but honest bar.
        double comms = AbilityScale.AttributeToHundred(s.Communication);
        return Math.Clamp(s.EffectiveWithExperience * 0.5 + comms * 0.25 + s.Reputation * 0.25 - 6, 0, 100);
    }

    // ---------------- specialist roles ----------------

    public double SpecialistFit(StaffMember s, StaffRole role) => s.EffectivenessFor(role);

    /// <summary>A head coach's suitability for a specialist role - mapping his coaching attributes onto what the role actually demands.</summary>
    public double SpecialistFit(Coach c, StaffRole role)
    {
        var a = c.Attributes;
        double S(int x) => AbilityScale.AttributeToHundred(x);
        return role switch
        {
            StaffRole.BattingCoach => Math.Clamp(S(a.BattingCoaching) * 0.55 + S(a.TechnicalKnowledge) * 0.25 + S(a.PlayerDevelopment) * 0.20, 0, 100),
            StaffRole.BowlingCoach => Math.Clamp(S(a.BowlingCoaching) * 0.55 + S(a.TechnicalKnowledge) * 0.25 + S(a.PlayerDevelopment) * 0.20, 0, 100),
            StaffRole.FieldingCoach => Math.Clamp(S(a.FieldingCoaching) * 0.60 + S(a.TechnicalKnowledge) * 0.25 + S(a.PlayerDevelopment) * 0.15, 0, 100),
            StaffRole.StrengthAndConditioning or StaffRole.SportsScientist => Math.Clamp(S(a.FitnessCoaching) * 0.6 + S(a.TechnicalKnowledge) * 0.4, 0, 100),
            StaffRole.Analyst or StaffRole.DataAnalyst => Math.Clamp(S(a.OppositionAnalysis) * 0.4 + S(a.Statistics) * 0.35 + S(a.TacticalAnalysis) * 0.25, 0, 100),
            StaffRole.Scout or StaffRole.ChiefScout => Math.Clamp(S(a.Scouting) * 0.55 + S(a.OppositionAnalysis) * 0.25 + S(a.TechnicalKnowledge) * 0.20, 0, 100),
            StaffRole.MentalPerformanceCoach => Math.Clamp(S(a.PressureHandling) * 0.4 + S(a.ManManagement) * 0.35 + S(a.PlayerManagement) * 0.25, 0, 100),
            StaffRole.GeneralManager => Math.Clamp(S(a.ManManagement) * 0.35 + S(a.DecisionMaking) * 0.30 + S(a.MediaHandling) * 0.20 + S(a.Statistics) * 0.15, 0, 100),
            StaffRole.AssistantCoach => Math.Clamp(HeadCoachFit(c) * 0.9, 0, 100),
            StaffRole.Mentor => Math.Clamp(S(a.ManManagement) * 0.4 + S(a.PlayerDevelopment) * 0.35 + S(a.Leadership) * 0.25, 0, 100),
            _ => Math.Clamp(S(a.TechnicalKnowledge) * 0.4 + S(a.ManManagement) * 0.3 + S(a.PlayerDevelopment) * 0.3, 0, 100)
        };
    }

    /// <summary>The fit for a vacancy - the head-coach job or a specialist role - dispatched on the advert.</summary>
    public double FitFor(Coach c, StaffRole? role) => role is null ? HeadCoachFit(c) : SpecialistFit(c, role.Value);
    public double FitFor(StaffMember s, StaffRole? role) => role is null ? HeadCoachFit(s) : SpecialistFit(s, role.Value);

    // ---------------- external factors ----------------

    /// <summary>
    /// How well a candidate's own working philosophy sits with a club's cultural identity, 0-100
    /// (50 = neutral / no strong signal). Same philosophy is a strong plus; a genuine clash
    /// (aggressive coach, defensive club) is a real minus; unrelated philosophies are near-neutral.
    /// </summary>
    public double CultureFit(CoachingPhilosophy candidate, CoachingPhilosophy team)
    {
        if (candidate == team) return 85;
        var clashes = new[]
        {
            (CoachingPhilosophy.Aggressive, CoachingPhilosophy.Defensive),
            (CoachingPhilosophy.ShortTermResults, CoachingPhilosophy.LongTermDevelopment),
            (CoachingPhilosophy.ShortTermResults, CoachingPhilosophy.YouthDevelopment),
            (CoachingPhilosophy.ReputationFocused, CoachingPhilosophy.YouthDevelopment),
        };
        foreach (var (x, y) in clashes)
            if ((candidate == x && team == y) || (candidate == y && team == x)) return 25;

        var kin = new[]
        {
            (CoachingPhilosophy.YouthDevelopment, CoachingPhilosophy.LongTermDevelopment),
            (CoachingPhilosophy.AnalyticsDriven, CoachingPhilosophy.TacticalFlexibility),
            (CoachingPhilosophy.PerformanceFocused, CoachingPhilosophy.ShortTermResults),
            (CoachingPhilosophy.ExperienceFocused, CoachingPhilosophy.ReputationFocused),
        };
        foreach (var (x, y) in kin)
            if ((candidate == x && team == y) || (candidate == y && team == x)) return 65;

        return 52;
    }

    /// <summary>
    /// Section G: how comfortable a candidate is with the practical reality of a move. A highly
    /// adaptable person barely cares; a low-adaptability one weighs a move to a very different
    /// cricketing environment (a different country) heavily. Returns a 0..1 comfort factor that
    /// composes with the career-benefit read, it does not replace it.
    /// </summary>
    public double RelocationComfort(int adaptability, bool differentCountry, bool differentContinent = false)
    {
        double adapt = Math.Clamp(AbilityScale.AttributeToHundred(adaptability) / 100.0, 0, 1);
        if (!differentCountry) return Math.Clamp(0.9 + adapt * 0.1, 0, 1);
        double baseComfort = differentContinent ? 0.35 : 0.55;
        return Math.Clamp(baseComfort + adapt * (1 - baseComfort), 0, 1);
    }
}
