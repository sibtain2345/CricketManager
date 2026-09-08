namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Phase 12 (§14.5): a media pundit - an ex-player on the panel or in a column. He has an old
/// allegiance and a club he never got on with, and it colours every take: softer on his old side,
/// harder on the rival. A real cricket media cycle is full of exactly this, and it is what makes a
/// pundit's opinion something to weigh rather than take at face value.
/// </summary>
public sealed record Pundit(
    string Name,
    Guid? FormerTeamId,
    Guid? RivalTeamId,
    string Nationality,
    /// <summary>0-1: how strongly the bias shows. A grudge-bearing former captain sits high; a measured analyst low.</summary>
    double BiasStrength);
