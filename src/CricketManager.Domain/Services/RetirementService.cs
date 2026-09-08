using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>A retirement judgement with its reasoning, so the news system can say WHY a player went rather than just that he did.</summary>
public sealed record RetirementAssessment(Guid PlayerId, bool ShouldRetire, double Probability, string Reason);

/// <summary>
/// Retirement is a decision, not an age limit.
///
/// The thing this deliberately avoids is "everyone retires at 36 or 37". Real careers end at
/// wildly different ages for reasons that have little to do with the birthday: a fast bowler's
/// body gives out at 32, a spinner plays until 41, a batter with a poor season at 34 walks
/// away while a bigger name with the same numbers is picked for another two years, and a
/// fringe player who cannot get a game retires at 29 with plenty left.
///
/// So the model asks the questions a player actually asks:
/// - **Is my body still up to it?** Physical decline measured against the player's own peak, not
///   against a league average.
/// - **Am I still being picked?** Playing time is the single strongest predictor. Nobody carries
///   on for long while not being selected.
/// - **Am I still any good?** Form and current ability relative to what he was.
/// - **Does anyone still want me?** Reputation buys extra seasons: stars are picked on name for
///   longer than journeymen, and they know it.
/// - **What kind of person is he?** An ambitious professional grinds on; someone lazy or
///   money-focused with a poor season leaves earlier.
/// - **What did he do?** A long, decorated career makes walking away easier and more dignified;
///   a player still chasing something hangs on.
///
/// Experience cuts BOTH ways here and that is intentional: it keeps a 36-year-old viable (he is
/// still good enough to be picked, so retirement probability stays low), but a player with a
/// long career behind him and nothing left to prove is also readier to stop than one still
/// building. The net effect is that great players last longer and then retire decisively,
/// rather than every squad emptying out at the same age.
/// </summary>
public sealed class RetirementService
{
    /// <summary>Below this age, only a career-ending injury forces the issue - nobody retires at 26 because of a bad season.</summary>
    public const int MinimumRetirementAge = 28;

    /// <summary>How much less likely a format-specific step-back is than the equivalent whole-career decision, at the same computed probability - see AssessFormatRetirement's own doc comment for why this has to be real, not cosmetic.</summary>
    private const double FormatRetirementDamping = 0.22;

    /// <param name="matchesLastSeason">
    /// How many matches the player got last season, or NULL when no match data exists at all
    /// (i.e. nothing has simulated a season yet). Null means "unknown" and the playing-time term
    /// is skipped entirely.
    ///
    /// This distinction is not pedantic - it was a live bug. Passing 0 for "we have no data"
    /// applied the full not-being-picked penalty (+0.30) to every player in the world every
    /// year, retiring squads far faster than reality. That is the THIRD time this codebase has
    /// read an unpopulated default as a meaningful negative signal (sponsorship read "no matches
    /// played" as "bad form"; trait suitability read "traits not yet derived" as "bad at
    /// everything"). Treat it as a standing hazard in any system that consumes data another
    /// phase is supposed to produce.
    /// </param>
    public RetirementAssessment Assess(Player player, DateOnly asOf, int? matchesLastSeason, Random random)
    {
        if (player.IsRetired)
            return new RetirementAssessment(player.Id, false, 0, "Already retired.");

        int age = player.Age(asOf);

        // A career-threatening injury can end things at any age. This is the one route that
        // bypasses the age floor, because it is the one route that does so in reality.
        var careerThreatening = player.CurrentInjury;
        if (careerThreatening is { Severity: InjurySeverity.CareerThreatening } && age >= 24)
        {
            double injuryProbability = Math.Clamp(0.35 + (age - 24) * 0.04, 0.35, 0.9);
            bool forced = random.NextDouble() < injuryProbability;
            return new RetirementAssessment(player.Id, forced, injuryProbability,
                forced
                    ? $"{player.FullName} has been forced to retire at {age} by a career-threatening {careerThreatening.Type}."
                    : $"{player.FullName} is fighting back from a career-threatening injury.");
        }

        if (age < MinimumRetirementAge)
            return new RetirementAssessment(player.Id, false, 0, $"{player.FullName} is {age} - far too early to be thinking about it.");

        double probability = AdjustForNonAgeFactors(BaseAgeProbability(age, player), player, matchesLastSeason, age);
        double physicalCondition = (player.Physical.Fitness + player.Physical.Stamina + player.Physical.Recovery) / 3.0;
        double standing = Math.Max(player.Reputation.Domestic, player.Reputation.Continental);

        bool retiring = random.NextDouble() < probability;
        return new RetirementAssessment(player.Id, retiring, Math.Round(probability, 3),
            retiring ? BuildRetirementReason(player, age, matchesLastSeason, physicalCondition, standing)
                     : $"{player.FullName} ({age}) intends to play on.");
    }

    /// <summary>
    /// Section H: a player can step back from ONE format - Test cricket, in the well-known real
    /// pattern - while continuing others, rather than retirement being a single all-or-nothing
    /// event. Deliberately does NOT duplicate Assess's non-age factors (playing time, physical
    /// condition, form, reputation, experience, personality, injury history) - AdjustForNonAgeFactors
    /// is the shared body both methods call, since a player's professionalism or current standing
    /// is a whole-player property whether the question is "retire entirely" or "step back from
    /// one format." The only thing that genuinely differs by format is the AGE CURVE itself -
    /// see FormatAgeOffset.
    ///
    /// Does NOT handle career-threatening injury (that ends the whole career at once, via Assess/
    /// Retire, not one format at a time) or the sub-MinimumRetirementAge floor differently per
    /// format - both stay shared, whole-player rules.
    /// </summary>
    public RetirementAssessment AssessFormatRetirement(Player player, MatchFormat format, DateOnly asOf, int? matchesInFormatLastSeason, Random random)
    {
        if (player.IsRetired || player.RetiredFormats.Contains(format))
            return new RetirementAssessment(player.Id, false, 0, $"{player.FullName} has already retired from {format}.");

        int age = player.Age(asOf);
        if (age < MinimumRetirementAge)
            return new RetirementAssessment(player.Id, false, 0, $"{player.FullName} is {age} - far too early to be thinking about {format} retirement.");

        double probability = AdjustForNonAgeFactors(BaseAgeProbability(age + FormatAgeOffset(format), player), player, matchesInFormatLastSeason, age);

        // Dampened relative to Assess's own probability, and deliberately so: stepping back from
        // ONE format while continuing others is a real pattern, but a genuinely rarer, more
        // deliberate decision than "I'm done with cricket" - most players who retire do it once,
        // outright, via Assess. This is checked independently for all three formats every year a
        // player doesn't retire outright, so without a real discount here the THREE extra rolls
        // would inflate population-wide attrition well past what Assess alone was calibrated for
        // (found by a failing population test, not guessed at - two existing "a decade shouldn't
        // empty the world" tests caught it immediately).
        probability = Math.Clamp(probability * FormatRetirementDamping, 0, 0.97);

        bool retiring = random.NextDouble() < probability;
        return new RetirementAssessment(player.Id, retiring, Math.Round(probability, 3),
            retiring
                ? $"{player.FullName} retires from {format} cricket at {age}, continuing to play elsewhere."
                : $"{player.FullName} ({age}) intends to keep playing {format}.");
    }

    /// <summary>
    /// How much earlier or later this format's own career typically ends, relative to the
    /// shared age curve BaseAgeProbability already calibrates - the real-world shape this whole
    /// slice exists to capture: Test cricket is the most technically and physically demanding
    /// format and is usually the first one a player steps back from; T20 careers, especially
    /// franchise ones, routinely run years past a player's international red-ball or one-day
    /// retirement. Expressed as an EFFECTIVE-AGE offset so it reuses the exact same curve (and
    /// the fast-bowler/spinner adjustment already inside it) rather than three separately
    /// calibrated curves that could drift out of step with each other.
    /// </summary>
    private static int FormatAgeOffset(MatchFormat format) => format switch
    {
        MatchFormat.Test => 3,
        MatchFormat.ODI => 0,
        MatchFormat.T20 => -4,
        _ => 0
    };

    /// <summary>
    /// Everything that adjusts a base age-derived probability EXCEPT the age curve itself -
    /// shared between whole-career and format-specific assessment so a player's standing means
    /// the same thing to both questions. Not being picked (in the RELEVANT context - the whole
    /// career for Assess, one format for AssessFormatRetirement) is still the strongest single
    /// driver; everything else (fitness, form, reputation, experience, personality, injury
    /// history) reads the same way it always did.
    /// </summary>
    private static double AdjustForNonAgeFactors(double probability, Player player, int? matchesLastSeason, int age)
    {
        if (matchesLastSeason is { } played)
        {
            if (played == 0) probability += 0.30;
            else if (played <= 3) probability += 0.18;
            else if (played <= 8) probability += 0.07;
            else if (played >= 20) probability -= 0.10;
        }

        double physicalCondition = (player.Physical.Fitness + player.Physical.Stamina + player.Physical.Recovery) / 3.0;
        if (physicalCondition <= 7) probability += 0.20;
        else if (physicalCondition <= 10) probability += 0.10;
        else if (physicalCondition >= 15) probability -= 0.08;

        if (player.Form.CurrentForm <= -40) probability += 0.10;
        else if (player.Form.CurrentForm >= 40) probability -= 0.08;

        double standing = Math.Max(player.Reputation.Domestic, player.Reputation.Continental);
        probability -= Math.Clamp(standing / 100.0, 0, 1) * 0.15;

        // Experience: a long, complete career makes stopping easier. A mild term - it decides
        // WHEN a good player goes, it never drags a young one out early. Uses the REAL age
        // (never the format-offset effective age) since these thresholds are about how much
        // career a player has actually had, not this format's own physical demands.
        if (player.Experience.Level >= 80 && age >= 35) probability += 0.06;
        if (player.Experience.Level < 30 && age < 33) probability -= 0.05;

        if (player.Personality.HasFlag(PersonalityTrait.Ambitious)) probability -= 0.06;
        if (player.Personality.HasFlag(PersonalityTrait.Professional)) probability -= 0.05;
        if (player.Personality.HasFlag(PersonalityTrait.Lazy)) probability += 0.08;
        if (player.Personality.HasFlag(PersonalityTrait.MoneyFocused) && matchesLastSeason is <= 5) probability += 0.05;

        probability += Math.Min(player.InjuryHistory.Count(i => i.Severity >= InjurySeverity.Serious), 4) * 0.04;

        return Math.Clamp(probability, 0, 0.97);
    }

    /// <summary>
    /// Age is the starting point, not the answer - and it is role-dependent. Fast bowlers break
    /// down years before spinners and batters, which is why a single curve for everyone produces
    /// a squad list that looks nothing like a real one.
    /// </summary>
    private static double BaseAgeProbability(int age, Player player)
    {
        bool isFastBowler = player.BowlingRole is BowlingRoleType.OpeningBowler or BowlingRoleType.DeathBowler
                            && player.Bowling.Pace >= 13;
        bool isSpinner = player.BowlingRole == BowlingRoleType.SpecialistSpinner;

        // Effective age: a quick bowler's body is roughly two years older than his passport says;
        // a spinner's is a year younger.
        int effectiveAge = age + (isFastBowler ? 2 : 0) - (isSpinner ? 1 : 0);

        return effectiveAge switch
        {
            <= 29 => 0.01,
            30 => 0.02,
            31 => 0.03,
            32 => 0.05,
            33 => 0.08,
            34 => 0.12,
            35 => 0.18,
            36 => 0.25,
            37 => 0.34,
            38 => 0.45,
            39 => 0.56,
            40 => 0.68,
            41 => 0.78,
            _ => 0.88
        };
    }

    /// <summary>Retires the player and closes the career off. Kept explicit so retirement is an event other systems can react to rather than a flag someone notices later.</summary>
    public void Retire(Player player, DateOnly date)
    {
        player.IsRetired = true;
        player.RetirementDate = date;
        player.CurrentTeamId = null;
        player.NonInjuryUnavailability = UnavailabilityReason.Retired;

        // Whichever path reached full retirement - the direct whole-career Assess/Retire call,
        // or RetireFromFormat completing the last of the three - RetiredFormats should always
        // read as fully covered once IsRetired is true. Without this, a player who retired
        // outright (never stepping through the format-by-format path at all) would report
        // IsEligibleForMatch-style queries inconsistently: fully retired by IsRetired, yet
        // apparently still "active" in every individual format by RetiredFormats.
        player.RetiredFormats.Add(MatchFormat.Test);
        player.RetiredFormats.Add(MatchFormat.ODI);
        player.RetiredFormats.Add(MatchFormat.T20);
    }

    /// <summary>
    /// Steps a player back from ONE format. If this is the last of the three he had left to
    /// play, retiring from it completes his whole career by definition - reuses Retire() itself
    /// rather than duplicating its side effects, so there is exactly one place that closes a
    /// career off.
    /// </summary>
    public void RetireFromFormat(Player player, MatchFormat format, DateOnly date)
    {
        player.RetiredFormats.Add(format);

        bool retiredFromEverything = player.RetiredFormats.Contains(MatchFormat.Test)
            && player.RetiredFormats.Contains(MatchFormat.ODI)
            && player.RetiredFormats.Contains(MatchFormat.T20);

        if (retiredFromEverything && !player.IsRetired)
            Retire(player, date);
    }

    /// <summary>
    /// Post-Phase-16 completion pass (§5.8): the rare comeback - a player who stepped back from
    /// ONE format (but is still playing the others, and is not whole-career retired) is coaxed
    /// back. Only for a genuine name still in his early-to-mid 30s and in real demand (a strong
    /// worldwide reputation), and deliberately rare. Called from the annual rollover.
    /// </summary>
    public bool TryComebackFromFormat(Player player, MatchFormat format, DateOnly asOf, Random random)
    {
        if (player.IsRetired || !player.RetiredFormats.Contains(format)) return false;
        int age = player.Age(asOf);
        if (age > 35) return false;
        double standing = Math.Max(player.Reputation.Worldwide, player.Reputation.Continental);
        if (standing < 55) return false;
        // Physical: he still has to be able to do it, and a quick's body ages fastest.
        double physical = (player.Physical.Fitness + player.Physical.Stamina + player.Physical.Speed) / 3.0;
        if (physical < 11) return false;

        double chance = 0.05 + (standing - 55) / 45.0 * 0.10 + (player.Personality.HasFlag(Enums.PersonalityTrait.Ambitious) ? 0.03 : 0);
        if (random.NextDouble() >= Math.Clamp(chance, 0, 0.2)) return false;

        player.RetiredFormats.Remove(format);
        return true;
    }

    private static string BuildRetirementReason(Player player, int age, int? matchesLastSeason, double physicalCondition, double standing)
    {
        if (matchesLastSeason == 0)
            return $"{player.FullName} retires at {age}, having been unable to force his way back into a side.";
        if (physicalCondition <= 8)
            return $"{player.FullName} retires at {age}, his body no longer able to stand up to the demands.";
        if (standing >= 70 && age >= 36)
            return $"{player.FullName} brings a distinguished career to a close at {age}.";
        if (player.Form.CurrentForm <= -40)
            return $"{player.FullName} retires at {age} after a difficult final season.";
        return $"{player.FullName} calls time on his career at {age}.";
    }
}
