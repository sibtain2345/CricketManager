using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>A single partnership performance, in reportable form - the "record" answer shape, matching GroundPerformanceEntry's role for ground records.</summary>
public sealed record PartnershipEntry(
    Guid MatchId, DateOnly Date, string BattingTeamName, string BowlingTeamName,
    Guid BatterAId, string BatterAName, Guid BatterBId, string BatterBName,
    int WicketNumber, int Runs, int Balls, bool Unbroken, MatchFormat Format, int Season)
{
    public string Description => $"{Runs}{(Unbroken ? "*" : "")} ({Balls}b) - {BatterAName} & {BatterBName}, {Ordinal(WicketNumber)} wicket";

    private static string Ordinal(int n) => n switch
    {
        1 => "1st", 2 => "2nd", 3 => "3rd",
        _ => $"{n}th"
    };
}

/// <summary>
/// Partnership records, derived on demand from <see cref="PartnershipRecord"/> rather than cached
/// anywhere - same reasoning as <see cref="GroundRecordsService"/>: a cache and the underlying
/// records inevitably drift apart, and every method here returns null/empty for "no data", never
/// a fabricated zero.
/// </summary>
public sealed class PartnershipRecordsService
{
    /// <summary>The best partnership matching the given filters. Pass wicketNumber for "highest 3rd-wicket stand"; leave it null for the best stand at any wicket.</summary>
    public PartnershipEntry? GetHighestPartnership(
        IEnumerable<PartnershipRecord> partnerships, int? wicketNumber = null, MatchFormat? format = null) =>
        Filtered(partnerships, wicketNumber, format)
            .OrderByDescending(p => p.Runs).ThenBy(p => p.Balls)
            .Select(ToEntry).FirstOrDefault();

    public PartnershipEntry? GetHighestPartnershipAtGround(
        IEnumerable<PartnershipRecord> partnerships, Guid groundId, int? wicketNumber = null, MatchFormat? format = null) =>
        Filtered(partnerships.Where(p => p.GroundId == groundId), wicketNumber, format)
            .OrderByDescending(p => p.Runs).ThenBy(p => p.Balls)
            .Select(ToEntry).FirstOrDefault();

    /// <summary>The best stand a specific pair of players have put together, regardless of which of them is "A" or "B" in any given record.</summary>
    public PartnershipEntry? GetHighestPartnershipBetween(
        IEnumerable<PartnershipRecord> partnerships, Guid playerAId, Guid playerBId, MatchFormat? format = null) =>
        partnerships
            .Where(p => format is null || p.Format == format)
            .Where(p => (p.BatterAId == playerAId && p.BatterBId == playerBId)
                     || (p.BatterAId == playerBId && p.BatterBId == playerAId))
            .OrderByDescending(p => p.Runs).ThenBy(p => p.Balls)
            .Select(ToEntry).FirstOrDefault();

    /// <summary>The leading stands for one specific wicket - an "opening partnership records" table.</summary>
    public IReadOnlyList<PartnershipEntry> GetLeadingPartnerships(
        IEnumerable<PartnershipRecord> partnerships, int wicketNumber, int take = 10, MatchFormat? format = null) =>
        Filtered(partnerships, wicketNumber, format)
            .OrderByDescending(p => p.Runs).ThenBy(p => p.Balls)
            .Take(take).Select(ToEntry).ToList();

    private static IEnumerable<PartnershipRecord> Filtered(
        IEnumerable<PartnershipRecord> partnerships, int? wicketNumber, MatchFormat? format) =>
        partnerships.Where(p =>
            (wicketNumber is null || p.WicketNumber == wicketNumber) &&
            (format is null || p.Format == format));

    private static PartnershipEntry ToEntry(PartnershipRecord p) => new(
        p.MatchId, p.MatchDate, p.BattingTeamName, p.BowlingTeamName,
        p.BatterAId, p.BatterAName, p.BatterBId, p.BatterBName,
        p.WicketNumber, p.Runs, p.Balls, p.Unbroken, p.Format, p.Season);
}
