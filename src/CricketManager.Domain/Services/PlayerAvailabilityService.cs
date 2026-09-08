using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>Why a player can or can't play, in a form a UI or the selection AI can both use. DaysUntilAvailable is null when they're available now or when the reason has no fixed end date.</summary>
public sealed record AvailabilityResult(bool IsAvailable, UnavailabilityReason Reason, int? DaysUntilAvailable, double EffectivenessIfPlayed, string Explanation);

/// <summary>
/// One place that answers "can this player be picked on this date, and at what cost".
///
/// Previously nothing did: injuries didn't exist as state, and the selection evaluator would
/// happily rank an unavailable player top of the list. Centralising it here matters because
/// availability has several independent sources (injury, suspension, international duty,
/// competition registration) and every one of them has to be checked by every caller -
/// scattering that logic guarantees one caller eventually forgets a case.
///
/// A Niggle is deliberately NOT an automatic block. Playing a slightly injured key player in
/// a final is a real decision a coach makes, so the service reports it as available-but-
/// degraded and lets the coach (or the AI) own the trade-off.
/// </summary>
public sealed class PlayerAvailabilityService
{
    private readonly MedicalEffectivenessService _medical = new();

    /// <param name="format">
    /// Optional. When supplied, also checks Section H's per-format retirement
    /// (Player.RetiredFormats) - a player retired from Test but still playing white-ball reads
    /// as unavailable for a Test selection and fully available for an ODI/T20 one. Left null,
    /// only whole-career retirement (Player.IsRetired) is checked, which is every pre-existing
    /// caller's behaviour, unchanged.
    /// </param>
    public AvailabilityResult GetAvailability(Player player, DateOnly date, MatchFormat? format = null, bool selectingForNationalTeam = false)
    {
        if (player.IsRetired)
            return new AvailabilityResult(false, UnavailabilityReason.Retired, null, 0, $"{player.FullName} has retired.");

        if (format is { } f && player.RetiredFormats.Contains(f))
            return new AvailabilityResult(false, UnavailabilityReason.Retired, null, 0, $"{player.FullName} has retired from {f} cricket.");

        // Slice 6.7: "on international duty" means unavailable for his CLUB, not for the national
        // side that called him up - so a national selection ignores exactly that reason.
        bool blockedByNonInjury = player.NonInjuryUnavailability != UnavailabilityReason.Available
            && !(selectingForNationalTeam && player.NonInjuryUnavailability == UnavailabilityReason.InternationalDuty);
        if (blockedByNonInjury)
            return new AvailabilityResult(false, player.NonInjuryUnavailability, null, 0,
                $"{player.FullName} is unavailable: {Describe(player.NonInjuryUnavailability)}.");

        // Phase 15 (§1.8): the concussion protocol overrides everything else - a mandatory
        // stand-down, whether or not the underlying injury has otherwise "cleared".
        if (player.ConcussionStandDownUntil is { } cleared && cleared > date)
            return new AvailabilityResult(false, UnavailabilityReason.Injured, cleared.DayNumber - date.DayNumber, 0,
                $"{player.FullName} is in the concussion protocol - unavailable until {cleared:d}.");

        // S4: back from a long layoff but not yet match-fit for INTERNATIONAL cricket - he needs a
        // spell of domestic / 'A' cricket first. Only blocks a national selection; club/domestic is fine.
        if (selectingForNationalTeam && player.NeedsMatchFitnessUntil is { } fit && fit > date
            && (player.CurrentInjury is null || !player.CurrentInjury.IsActiveOn(date)))
            return new AvailabilityResult(false, UnavailabilityReason.Injured, fit.DayNumber - date.DayNumber, 0,
                $"{player.FullName} is back in training but needs match fitness - domestic cricket before an international recall (around {fit:d}).");

        var injury = player.CurrentInjury;
        if (injury is null || !injury.IsActiveOn(date))
            return new AvailabilityResult(true, UnavailabilityReason.Available, null, 1.0, $"{player.FullName} is fully available.");

        int daysOut = injury.ExpectedReturnDate.DayNumber - date.DayNumber;

        if (injury.Severity == InjurySeverity.Niggle)
            return new AvailabilityResult(true, UnavailabilityReason.Available, daysOut, injury.PlayingThroughEffectiveness,
                $"{player.FullName} is carrying a {injury.Type} niggle - playable, but around {(1 - injury.PlayingThroughEffectiveness) * 100:F0}% below their best.");

        return new AvailabilityResult(false, UnavailabilityReason.Injured, daysOut, 0,
            $"{player.FullName} is out with a {injury.Severity} {injury.Type}, expected back in {daysOut} day(s).");
    }

    /// <summary>Filters a squad down to who can actually be considered. The selection AI should call this BEFORE ranking, not after.</summary>
    public IReadOnlyList<Player> GetSelectablePlayers(IEnumerable<Player> squad, DateOnly date) =>
        squad.Where(p => GetAvailability(p, date).IsAvailable).ToList();

    /// <summary>
    /// Starts an injury and files it in the player's history. Expected layoff is the
    /// severity's typical range, shortened or lengthened by the player's own Recovery
    /// attribute and the quality of the medical support around them - which is what finally
    /// gives MedicalEffectivenessService and PhysicalAttributes.Recovery something to affect.
    /// </summary>
    public Injury ApplyInjury(Player player, InjuryType type, InjurySeverity severity, DateOnly date, int teamMedicalQuality = 50, GroundFacilities? groundFacilities = null, Random? random = null)
    {
        var rng = random ?? new Random();
        var (minDays, maxDays) = Injury.TypicalLayoff(severity);
        int baseDays = rng.Next(minDays, Math.Max(minDays + 1, maxDays + 1));

        // Effective recovery 0-100 -> layoff multiplier ~1.25x (poor recovery/medical) down
        // to ~0.75x (excellent). Never instant, never unbounded.
        double effectiveRecovery = _medical.GetEffectiveRecoveryRate(player.Physical.Recovery, teamMedicalQuality, groundFacilities);
        double layoffMultiplier = 1.25 - effectiveRecovery / 100.0 * 0.5;
        int days = Math.Max(1, (int)Math.Round(baseDays * layoffMultiplier));

        // Recurrence risk is higher for the injury-prone and for the same injury repeating.
        int priorSameType = player.InjuryHistory.Count(i => i.Type == type);
        double recurrenceRisk = Math.Clamp(player.Physical.InjuryProneness / 20.0 * 40 + priorSameType * 10, 0, 90);

        // Phase 15 (§1.8): a concussion carries a mandatory minimum stand-down (real protocols run
        // ~6 days) whatever the computed layoff and however "fine" the player says he is.
        if (type == InjuryType.Concussion)
        {
            int minStandDown = 6;
            if (days < minStandDown) days = minStandDown;
            player.ConcussionStandDownUntil = date.AddDays(days);
        }

        var injury = new Injury(type, severity, date, days, recurrenceRisk);
        injury.CaptureOnsetState(player.Form.Confidence); // Wave 3: belief on return is read back from here
        player.CurrentInjury = injury;
        player.InjuryHistory.Add(injury);
        return injury;
    }

    /// <summary>
    /// Clears a resolved injury. Kept explicit rather than implicit-on-date so the return is a
    /// real event other systems (news, media, selection) can react to.
    ///
    /// A previously first-team player returning from a layoff is moved to ReturningFromInjury
    /// rather than left at whatever status he already held - this is the planning brief's own
    /// example (Section A): when an injured first-choice player comes back, his ORIGINAL status
    /// should still count for something, so a temporary replacement who happened to be in hot
    /// form while he was out doesn't silently and permanently take his place.
    /// PlayerSelectionEvaluator's EstablishedCeiling protects ReturningFromInjury almost as
    /// strongly as FirstChoice itself for exactly this reason. A coach can move him back to
    /// FirstChoice explicitly once satisfied he's genuinely back to full fitness and form -
    /// this only sets the starting point for that judgement, not a permanent label.
    /// </summary>
    public void MarkRecovered(Player player, DateOnly date)
    {
        var resolved = player.CurrentInjury;
        resolved?.MarkReturned(date);
        player.CurrentInjury = null;

        // Phase 15 (§1.8): the concussion protocol lifts on return.
        if (player.ConcussionStandDownUntil is { } d && d <= date) player.ConcussionStandDownUntil = null;

        if (player.SquadStatus is SquadStatus.FirstChoice or SquadStatus.SecondChoice)
            player.SquadStatus = SquadStatus.ReturningFromInjury;

        // Seven-suggestions pass (S4): after a genuine long layoff (Serious+), a player needs
        // match fitness before an INTERNATIONAL recall - a spell of domestic / 'A' cricket first.
        // National selection (XiSelectionService / NationalSelectionService) will not pick him until
        // this passes; he is free to play domestic cricket the whole time (and MatchDevelopmentService
        // still develops him). Real practice - Stokes, Bumrah, Pant have all returned this way.
        if (resolved is { } lay && lay.Severity >= InjurySeverity.Serious)
            player.NeedsMatchFitnessUntil = date.AddDays(lay.Severity == InjurySeverity.CareerThreatening ? 75 : 45);

        // Wave 3 (point 6): after a genuine layoff, belief is read back from where it was BEFORE
        // the injury - pulled toward it, never all the way (rust), and shaped by temperament and
        // how long he was out. A Professional/BigMatchPlayer picks up close to where he left off;
        // an Inconsistent/InjuryProne player comes back short of belief. A Niggle/Minor knock is
        // not a "layoff" in this sense and is left alone.
        if (resolved is { } inj && inj.Severity >= InjurySeverity.Moderate)
        {
            double preInjury = inj.ConfidenceAtOnset;
            double rust = Math.Clamp(inj.ExpectedDaysOut / 200.0, 0.08, 0.65);
            double resilience = Math.Clamp((MatchDevelopmentService.PersonalityResilience(player) + 0.6) / 1.6, 0, 1); // 0..1
            double target = 50 + (preInjury - 50) * (1 - rust) * (0.55 + resilience * 0.45);
            player.Form.AdjustConfidence(target - player.Form.Confidence);
        }

        // Phase 8, Slice 8.4: a career-threatening injury leaves a permanent mark on recovery, on
        // top of the long layoff - a lost yard of pace, a step slower in the field, less of a
        // spring. Applied once, here, when the injury actually resolves (a player who never comes
        // back never takes the hit - RetirementService may end his career first). RoleTraitDeriver
        // is re-run so a quick who has lost his pace is re-slotted from what he has actually become.
        if (resolved is { Severity: InjurySeverity.CareerThreatening })
        {
            int Dock(int v, int by) => Math.Clamp(v - by, 1, 20);
            player.Physical.Speed = Dock(player.Physical.Speed, 2);
            player.Physical.Fitness = Dock(player.Physical.Fitness, 1);
            player.Physical.Stamina = Dock(player.Physical.Stamina, 1);
            player.Physical.Recovery = Dock(player.Physical.Recovery, 1);
            player.Bowling.Pace = Dock(player.Bowling.Pace, 2);
            player.Fielding.Reflexes = Dock(player.Fielding.Reflexes, 1);
            player.CurrentAbility = Math.Max(1, player.CurrentAbility - 6);
            new RoleTraitDeriver().ApplyTo(player);
            player.RecalculateFormatSuitability();
        }
    }

    /// <summary>
    /// How injury-prone this player has actually proven to be, as opposed to what their
    /// attribute claims. A recruiter should be able to look at the record, not the hidden
    /// number - so this reads history, and returns null when there isn't enough of a career
    /// to judge rather than implying a clean bill of health.
    /// </summary>
    public double? GetInjuryRecordSeverityScore(Player player, DateOnly asOf, int overLastYears = 3)
    {
        var cutoff = asOf.AddYears(-overLastYears);
        var recent = player.InjuryHistory.Where(i => i.StartDate >= cutoff).ToList();
        if (player.InjuryHistory.Count == 0) return null;

        return Math.Round(recent.Sum(i => i.Severity switch
        {
            InjurySeverity.Niggle => 1.0,
            InjurySeverity.Minor => 2.5,
            InjurySeverity.Moderate => 5.0,
            InjurySeverity.Serious => 10.0,
            InjurySeverity.CareerThreatening => 20.0,
            _ => 0
        }), 1);
    }

    private static string Describe(UnavailabilityReason reason) => reason switch
    {
        UnavailabilityReason.Suspended => "suspended",
        UnavailabilityReason.InternationalDuty => "on international duty",
        UnavailabilityReason.Unregistered => "not registered for this competition",
        UnavailabilityReason.Retired => "retired",
        UnavailabilityReason.Injured => "injured",
        UnavailabilityReason.OnDevelopmentAssignment => "on a domestic roadmap back into the side",
        UnavailabilityReason.Resting => "resting, on a deliberate break",
        UnavailabilityReason.PersonalLeave => "on personal leave",
        UnavailabilityReason.ContractHoldout => "holding out for a new contract",
        _ => "available"
    };
}
