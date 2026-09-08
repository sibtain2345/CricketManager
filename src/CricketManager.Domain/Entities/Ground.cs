using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Entities;

/// <summary>
/// A venue's IDENTITY and PHYSICAL CHARACTERISTICS. Deliberately holds no statistics.
///
/// It previously carried cached aggregates (MatchesHosted/HighestTeamScore/
/// AverageFirstInningsScore) updated by a RecordMatchResult() call. Those are gone: real
/// ground statistics are far richer than three numbers (Cricinfo's ground pages carry
/// highest/lowest totals by innings number and format, highest individual scores, best
/// bowling figures, honour boards, chase records, result splits), and caching a handful of
/// them on the entity guarantees the cache and the underlying records drift apart. All of
/// it is now DERIVED from TeamInningsRecord/BattingInningsRecord/BowlingSpellRecord by
/// GroundRecordsService, which has one source of truth and can answer questions the cached
/// fields never could.
///
/// PLAYER records at a ground continue to work through MatchContext.GroundId/Ground on the
/// granular records - player records and ground/team records stay conceptually separate,
/// per the Phase 3 requirement, even though both now read from the same underlying rows.
/// </summary>
public sealed class Ground
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int? EstablishedYear { get; set; }
    public List<Guid> HomeTeamIds { get; set; } = new();
    public List<string> Ends { get; set; } = new(); // bowling ends, e.g. "Pavilion End"

    // Pitch/venue characteristics, 0-100 each - same conventions as player attributes so
    // they combine cleanly with existing 0-100-scale systems (form, matchup multipliers, etc.)
    // once the match engine (Phase 4) consumes them.
    public double PitchPaceRating { get; set; } = 50;
    public double PitchSpinRating { get; set; } = 50;
    public double PitchBounceRating { get; set; } = 50;
    public double PitchBattingFriendliness { get; set; } = 50;

    /// <summary>
    /// 0-100, default 50. How fast THIS surface breaks up across a multi-day match, as its own
    /// per-ground property rather than the single global day-over-day slope the deterioration
    /// model used before this field existed (see GroundConditionsService.EstimateSpinAssistance
    /// and MultiDayMatchSimulator.BuildPitch - both now scale by it). 50 reproduces exactly the
    /// old fixed rate, so every ground seeded before this field existed behaves unchanged. A dry,
    /// dusty subcontinental strip wears fast (turns square by day three); an English or
    /// Australian road built for pace barely changes across five days.
    /// </summary>
    public double PitchWearRate { get; set; } = 50;

    // Physical dimensions and conditions. These are what make two "batting-friendly" grounds
    // play differently: a short square boundary at altitude with a fast outfield is a
    // six-hitting ground, a big square boundary with a slow outfield rewards running.
    // Phase 4's match engine consumes these; nothing reads them today (stated, not implied).
    public int SquareBoundaryMetres { get; set; } = 65;
    public int StraightBoundaryMetres { get; set; } = 70;
    public double OutfieldSpeed { get; set; } = 50;      // 0-100, affects value of a well-timed shot
    public int AltitudeMetres { get; set; }               // carry distance / swing behaviour
    public bool HasFloodlights { get; set; }
    public double DewTendency { get; set; } = 30;         // 0-100, how much evening dew typically affects grip/spin here

    public double Reputation { get; set; } = 50; // 0-100, e.g. Lord's/MCG sit high
    public bool IsNationalStadium { get; set; }

    /// <summary>
    /// Phase 15 (§19.4): a drop-in pitch (grown off-site, dropped into the square). It plays truer
    /// and, crucially, wears MORE EVENLY across a multi-day match than a native strip - it does not
    /// deteriorate into a day-five minefield the way a soil pitch prepared in situ does. Common in
    /// Australia and New Zealand, and at stadiums that also host other sports. Default false - a
    /// native pitch, and every pre-Phase-15 ground behaves exactly as before.
    /// </summary>
    public bool UsesDropInPitch { get; set; }
    public GroundFacilities Facilities { get; set; } = new();
}
