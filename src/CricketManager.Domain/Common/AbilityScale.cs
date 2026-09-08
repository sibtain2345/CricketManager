namespace CricketManager.Domain.Common;

/// <summary>
/// The game has two coexisting ability scales:
///   - Individual attributes (Batting/Bowling/Fielding/Mental/Physical): 1-20 each (FM convention)
///   - CurrentAbility/PotentialAbility (composite): 1-200 (FM convention)
/// Most derived scores (FormatSuitability, SelectionScore components, trait strengths) want
/// a 0-100 scale to combine cleanly with form/matchup/situational multipliers.
///
/// Before this existed, Player.RecalculateFormatSuitability() and PlayerSelectionEvaluator
/// each had their own private, uncoordinated conversion (a raw x5 on attribute-weighted-sums,
/// and a /2 on CurrentAbility respectively) that happened to land in the same 0-100 range by
/// coincidence, not by design. That's exactly the kind of implicit coupling that breaks
/// silently once Phase 8 (training/aging) starts mutating attributes and CurrentAbility
/// independently - this class is the one place that relationship is now defined, so a future
/// change to one conversion is visibly a decision, not an accidental drift.
///
/// Reference point for Phase 8: a "maxed" player (every attribute at 20) should correspond to
/// a CurrentAbility around 190-200, not the full 20*10=200 (some headroom is expected for
/// composite factors training/aging will add later that individual attributes don't capture).
/// Nothing enforces this relationship yet - documented here so Phase 8 has one place to
/// reconcile it instead of reinventing a mapping per system.
/// </summary>
public static class AbilityScale
{
    public const double AttributeToHundredMultiplier = 5.0;   // 1-20 attribute -> ~0-100
    public const double CompositeAbilityToHundredDivisor = 2.0; // 1-200 composite -> 0-100

    public const int AttributeMax = 20;
    public const int CompositeAbilityMax = 200;
    public const int ExpectedCompositeAbilityAtMaxedAttributes = 190; // Phase 8 reference point, not enforced yet

    /// <summary>
    /// Converts an attribute-weighted sum (terms on the 1-20 scale) to the ~0-100 scale.
    /// Deliberately NOT clamped here - callers (like format-suitability calculations) often
    /// combine this with other additive terms (form nudge, trait nudge) before a single
    /// final clamp, so clamping early here would double-clamp and lose information.
    /// </summary>
    public static double AttributeSumToHundredRaw(double weightedAttributeSum) => weightedAttributeSum * AttributeToHundredMultiplier;

    /// <summary>One 1-20 attribute on the same 0-100 scale everything else speaks. Uses the shared multiplier so it can never drift from the weighted-sum conversion above.</summary>
    public static double AttributeToHundred(int attribute) =>
        Math.Clamp(attribute * AttributeToHundredMultiplier, 0, 100);

    /// <summary>Converts CurrentAbility/PotentialAbility (1-200 composite scale) to a clamped 0-100 scale.</summary>
    public static double CompositeAbilityToHundred(int compositeAbility) =>
        Math.Clamp(compositeAbility / CompositeAbilityToHundredDivisor, 0, 100);
}
