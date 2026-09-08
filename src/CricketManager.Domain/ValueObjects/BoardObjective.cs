namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Phase 5 follow-up: a real, checkable board target - the structured alternative to
/// CoachingContract.ObjectivesByYear's free text ("Finish top 4"), which CoachCareerService's own
/// doc comment explicitly declined to evaluate, because prose written for a human to read cannot
/// be honestly checked off by code without guessing at what satisfies it. A small, closed set of
/// types rather than an open-ended goal system, because each one has to be something this
/// codebase's existing standings data can actually answer without inventing a new measurement.
/// </summary>
public enum BoardObjectiveType
{
    /// <summary>Finish in the top TargetValue positions of the named competition.</summary>
    FinishTopN,
    /// <summary>Finish outside the bottom TargetValue positions - the survival/avoid-relegation ask.</summary>
    AvoidBottomN,
    /// <summary>Be champion of the named competition.</summary>
    WinCompetition,
    /// <summary>Qualify for the playoffs/knockout stage - TargetValue is how many spots that competition awards.</summary>
    ReachPlayoffs,
    /// <summary>Win at least TargetValue percent of matches played, across every competition this year - the one type that needs no specific competition, useful for a bilateral-series-heavy season with no clean table to point at.</summary>
    MinimumWinRate,

    // ---- Phase 12 (§13.4): objectives beyond results ----
    /// <summary>Blood at least TargetValue academy graduates / development prospects (age &lt;= 22) into the senior side this year - the youth-agenda board's ask.</summary>
    DevelopYouth,
    /// <summary>Finish the financial year in the black - the austerity board's ask. TargetValue ignored.</summary>
    BalanceTheBooks,
    /// <summary>Grow the fanbase - fan sentiment at or above TargetValue by year end. The prestige board's ask.</summary>
    GrowFanbase
}

/// <summary>One structured target for one contract year. See BoardObjectiveType for what each field means per type.</summary>
public sealed class BoardObjective
{
    public required BoardObjectiveType Type { get; init; }

    /// <summary>Which competition this target is scored against. Required for every type except MinimumWinRate.</summary>
    public Guid? CompetitionId { get; init; }

    /// <summary>The N in "top N" / "bottom N" / playoff spots, or the percentage for MinimumWinRate.</summary>
    public int TargetValue { get; init; }

    public string Describe() => Type switch
    {
        BoardObjectiveType.FinishTopN => $"Finish in the top {TargetValue}",
        BoardObjectiveType.AvoidBottomN => $"Avoid finishing in the bottom {TargetValue}",
        BoardObjectiveType.WinCompetition => "Win the competition",
        BoardObjectiveType.ReachPlayoffs => $"Qualify for the playoffs (top {TargetValue})",
        BoardObjectiveType.MinimumWinRate => $"Win at least {TargetValue}% of matches",
        BoardObjectiveType.DevelopYouth => $"Blood at least {TargetValue} young players into the senior side",
        BoardObjectiveType.BalanceTheBooks => "Finish the year in the black",
        BoardObjectiveType.GrowFanbase => $"Grow the fanbase (sentiment to {TargetValue}+)",
        _ => "Unspecified objective"
    };
}

/// <summary>Whether one structured objective was actually met this year, and why - the legible read a coach/player can see, rather than only an implicit trust number moving.</summary>
public sealed record BoardObjectiveResult(BoardObjective Objective, bool Met, string Reason);
