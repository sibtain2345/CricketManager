namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Post-Phase-5 rectification pass, Wave 2 (point 3): an unresolved high-pressure failure a
/// player is carrying, awaiting a redemption arc.
///
/// The source document names this directly - Ben Stokes conceding 19 off the last over of the
/// 2016 World T20 final, then his 2019 summer; Carlos Brathwaite in the mirror. A big public
/// failure in a match that genuinely mattered is a real, lingering thing, and how a player
/// responds the NEXT time he is put in that seat is heavily personality-modulated - some are
/// defined by the redemption, some never recover.
///
/// Set by <see cref="Services.MatchDevelopmentService"/> when a player fails badly in a
/// high-pressure match, and resolved (redemption) or reinforced (froze again) the next time he
/// plays one. Deliberately an explicit nullable field on <see cref="Entities.Player"/> rather
/// than folded into the Matchups/MatchupConfidence machinery - a redemption ARC is a discrete
/// event with a clear before/after, not a rolling average, and "did he cross back over" is far
/// cleaner to reason about and test as its own small record than as a threshold on a blended EMA.
/// </summary>
/// <param name="FailedOn">When the unresolved failure happened.</param>
/// <param name="Severity">
/// 0-100 magnitude of the failure - the absolute value of the (negative) performance rating that
/// created it. A worse failure takes a bigger, better redemption to clear and hurts more if it
/// repeats.
/// </param>
/// <param name="SubsequentAttempts">
/// How many further high-pressure matches he has played since, without either redeeming himself
/// or failing badly again. Past a few of these the moment simply fades - he has moved on, the
/// crowd has moved on, and it stops being the thing hanging over him.
/// </param>
public sealed record PressureMoment(DateOnly FailedOn, double Severity, int SubsequentAttempts = 0);
