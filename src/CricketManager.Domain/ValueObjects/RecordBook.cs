using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Phase 7, Slice 7.7: one all-time record and its history. Spec Section 15 - deferred since
/// Phase 3b because it is its own feature: a record page shows not just who holds it now but
/// who held it before and how long theirs stood.
///
/// <see cref="Holder"/> fields describe the current holder; <see cref="PreviousHolders"/> keeps
/// the chain, most recent first. A team record's HolderPlayerId is null; a player record's
/// HolderTeamId is the team he did it for.
/// </summary>
public sealed record RecordEntry(
    RecordCategory Category,
    string Scope,                 // "" for the global all-time record; a competition name / ground name for a scoped one
    double Value,
    string HolderName,
    Guid? HolderPlayerId,
    Guid? HolderTeamId,
    DateOnly SetOn,
    IReadOnlyList<PastRecordHolder> PreviousHolders)
{
    public string Key => $"{Category}|{Scope}";
}

/// <summary>A former holder of a record - what they held it at, and for how long.</summary>
public sealed record PastRecordHolder(string HolderName, double Value, DateOnly SetOn, DateOnly LostOn)
{
    public int DaysHeld => Math.Max(0, LostOn.DayNumber - SetOn.DayNumber);
}

/// <summary>
/// Phase 7, Slice 7.7: a Hall of Fame induction. Decided at retirement by HallOfFameService from
/// a player's career weight (runs/wickets, honours, reputation, ranking history) against a bar
/// that is deliberately high - this is for genuine greats, not every long career.
/// </summary>
public sealed record HallOfFameInductee(
    Guid PlayerId,
    string PlayerName,
    string Nationality,
    DateOnly InductedOn,
    double CareerScore,
    string Citation);

/// <summary>Phase 7, Slice 7.5: one end-of-period award and who won it.</summary>
public sealed record AwardResult(
    AwardType Type,
    int Year,
    int? Month,
    Guid? PlayerId,
    Guid? TeamId,
    string WinnerName,
    string Citation);
