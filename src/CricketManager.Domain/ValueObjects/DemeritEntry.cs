namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Post-Phase-7/8/9 rectification (Section A): one dated code-of-conduct charge on a player's (or
/// coach's) record. Points age out of the rolling 24-month window rather than being decayed by a
/// timer - the real ICC rule.
/// </summary>
public sealed record DemeritEntry(DateOnly Date, int Points, int Level, string Reason);
