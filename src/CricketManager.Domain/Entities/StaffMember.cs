using CricketManager.Domain.Common;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Entities;

/// <summary>
/// A backroom staff member. Section 33 lists the positions; this is the entity behind them.
///
/// Attributes are deliberately a small, shared set rather than a role-specific one, because the
/// same qualities matter differently per role rather than being different qualities. An analyst
/// lives on Analysis and Statistics; a physio on Technical and Diligence; a bowling coach on
/// Technical and Communication. One set, weighted per role, means a staff member can be good at
/// something other than his job title - which is how backroom teams actually are.
/// </summary>
public sealed class StaffMember
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}";

    public StaffRole Role { get; set; }
    public Guid? TeamId { get; set; }
    public DateOnly DateOfBirth { get; set; }

    /// <summary>Reading an opposition, spotting a pattern nobody else has noticed.</summary>
    public int Analysis { get; set; } = 10;

    /// <summary>Working with data rather than impressions - the difference between a hunch and a finding.</summary>
    public int Statistics { get; set; } = 10;

    /// <summary>Technical understanding of technique and action.</summary>
    public int TechnicalKnowledge { get; set; } = 10;

    /// <summary>
    /// Getting it across. A brilliant analyst who cannot make a plan land in a bowler's head is
    /// worth far less than his analysis alone suggests, and that gap is real in professional sport.
    /// </summary>
    public int Communication { get; set; } = 10;

    /// <summary>Thoroughness. A diligent analyst covers the whole opposition; a lazy one does the famous ones.</summary>
    public int Diligence { get; set; } = 10;

    /// <summary>Years in the job. Sharpens judgement the same way it does for players and coaches.</summary>
    public int YearsExperience { get; set; }

    /// <summary>
    /// Job-market follow-up: 0-100, defaults 70 - the same starting value and the same meaning
    /// Coach.CareerSatisfaction already established (whether HE still wants the job, a distinct
    /// question from whether the club rates him). See StaffCareerService.EvaluateSatisfaction/
    /// TryResign, which mirror CoachCareerService's own methods of the same name.
    /// </summary>
    public double CareerSatisfaction { get; set; } = 70;

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 4: a specialist builds his OWN reputation - "he has
    /// turned three struggling openers into Test regulars" - distinct from raw effectiveness.
    /// 0-100, starts modest. Grown by StaffCareerService from tenure AND, quarterly, from whether
    /// his own cluster of players is genuinely outdeveloping the team baseline (which ties this
    /// to Wave 2's training/match-experience output rather than treating them as unrelated
    /// systems). Read when deciding whether a staff member is a genuine head-coach candidate.
    /// </summary>
    public double Reputation { get; set; } = 30;

    /// <summary>Post-Phase-6 (section A/G): a specialist has a working philosophy the same way a head coach does - it is weighed against a club's cultural identity when he applies for a job.</summary>
    public CoachingPhilosophy Philosophy { get; set; } = CoachingPhilosophy.PerformanceFocused;

    /// <summary>1-20. How readily he adjusts to a new environment - section G's relocation factor reads this against how different a move actually is.</summary>
    public int Adaptability { get; set; } = 10;

    /// <summary>1-20. How much he wants to climb - a factor in whether he applies for a bigger role even at a smaller club.</summary>
    public int Ambition { get; set; } = 10;

    /// <summary>Where he is based - used with Adaptability to judge how big a relocation a move would be. Set from the hiring team's Country.</summary>
    public string BasedInCountry { get; set; } = string.Empty;

    /// <summary>The country this staff member is a national of - set for a retired-player-derived staffer; a national selection panel (NEW-B) picks only nationals of the country.</summary>
    public string Nationality { get; set; } = string.Empty;

    /// <summary>
    /// NEW-C: 0-100. If this staff member is a former player, how substantial his playing career
    /// was (weighted caps, reputation, longevity, leadership). 0 = a career backroom specialist who
    /// never played. Scales his starting Reputation and standing.
    /// </summary>
    public double PlayingCareerWeight { get; set; }

    /// <summary>NEW-C: the Player id this staff member retired from, if he is a former player. Lets a selection panel (NEW-B) read his caps for the ICC-style eligibility threshold.</summary>
    public Guid? FromPlayerId { get; set; }

    /// <summary>NEW-C: the date he retired as a player, if applicable. A national selector must be retired at least 5 years (BCCI rule).</summary>
    public DateOnly? RetiredAsPlayerOn { get; set; }

    /// <summary>Seven-suggestions pass (NEW-A): a franchise this staff member works for, for the length of that league's campaign, on a real <see cref="StaffContract"/> - the staff mirror of <see cref="Coach.FranchiseCoachingTeamId"/>. A staff member may hold more than one across non-clashing leagues; this is the primary one, the rest are in <see cref="AdditionalFranchiseTeamIds"/>.</summary>
    public Guid? FranchiseStaffTeamId { get; set; }

    /// <summary>NEW-A: additional franchise teams this staff member also works for (other leagues, non-clashing windows).</summary>
    public List<Guid> AdditionalFranchiseTeamIds { get; set; } = new();

    public int Age(DateOnly asOf) => asOf.Year - DateOfBirth.Year - (asOf.DayOfYear < DateOfBirth.DayOfYear ? 1 : 0);

    /// <summary>
    /// Post-Phase-6 (section A): how good this person would be at a DIFFERENT role, not just the
    /// one he holds - the same per-role weighting as <see cref="Effectiveness"/>, evaluated for
    /// the target role. Lets any staff member be assessed for any vacancy, FM-style.
    /// </summary>
    public double EffectivenessFor(StaffRole targetRole) =>
        WeightFor(targetRole, Analysis, Statistics, TechnicalKnowledge, Communication, Diligence);

    private static double WeightFor(StaffRole role, int analysis, int stats, int technical, int comms, int diligence) => role switch
    {
        StaffRole.Analyst or StaffRole.DataAnalyst => Blend(analysis, 0.38, stats, 0.30, comms, 0.20, diligence, 0.12),
        StaffRole.Scout or StaffRole.ChiefScout => Blend(analysis, 0.35, diligence, 0.28, technical, 0.22, stats, 0.15),
        // The meeting-driven-selection ticket: a selector tracks domestic form across a whole
        // country's circuit and has to argue his case in the room - reads close to a Scout/Analyst
        // blend (this IS scouting, aimed at the national frame rather than one club's targets) with
        // a touch more weight on getting the case across than either of those needs.
        StaffRole.Selector or StaffRole.ChiefSelector => Blend(analysis, 0.32, stats, 0.28, diligence, 0.25, comms, 0.15),
        StaffRole.BattingCoach or StaffRole.BowlingCoach or StaffRole.FieldingCoach or StaffRole.Mentor
            => Blend(technical, 0.45, comms, 0.32, analysis, 0.13, diligence, 0.10),
        StaffRole.Physiotherapist or StaffRole.HeadPhysiotherapist or StaffRole.StrengthAndConditioning or StaffRole.SportsScientist
            => Blend(technical, 0.42, diligence, 0.33, comms, 0.25),
        StaffRole.MentalPerformanceCoach => Blend(comms, 0.48, analysis, 0.27, diligence, 0.25),
        StaffRole.GeneralManager => Blend(comms, 0.40, analysis, 0.24, diligence, 0.20, stats, 0.16),
        _ => Blend(comms, 0.30, technical, 0.28, analysis, 0.24, diligence, 0.18)
    };

    private static double Blend(params object[] pairs)
    {
        double total = 0;
        for (int i = 0; i < pairs.Length; i += 2)
            total += AbilityScale.AttributeToHundred((int)pairs[i]) * (double)pairs[i + 1];
        return Math.Clamp(total, 0, 100);
    }

    /// <summary>
    /// How good this person is at the job they actually hold, 0-100. Weighted per role, so hiring
    /// an excellent scout as your analyst gets you a mediocre analyst rather than an excellent one.
    /// </summary>
    public double Effectiveness => EffectivenessFor(Role);

    /// <summary>
    /// Experience-adjusted effectiveness. Saturating, like every other experience curve here - the
    /// first few years teach most of it.
    /// </summary>
    public double EffectiveWithExperience
    {
        get
        {
            double experience = (1 - Math.Exp(-YearsExperience / 6.0)) * 100;
            return Math.Clamp(Effectiveness * 0.80 + experience * 0.20, 0, 100);
        }
    }
}
