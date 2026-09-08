using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Entities;

/// <summary>
/// The persistent identity of a competition (e.g. "Pakistan Super League", "ICC World
/// Cup"). Deliberately separate from CompetitionSeason (one year's instance - teams,
/// standings, champion) so multi-season history is just "all CompetitionSeason rows for
/// this CompetitionId", not something bolted on afterward.
/// </summary>
public sealed class Competition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public CompetitionStructureType StructureType { get; set; }
    public CompetitionScope Scope { get; set; }
    public MatchFormat Format { get; set; }
    public string Country { get; set; } = string.Empty; // empty = global/international competition

    // Prestige: fixed/structural importance (a World Cup doesn't become less prestigious
    // because of one bad season) - this is what PerformanceValuationService.AdjustForPrestige
    // actually reads. 0-100.
    public double Prestige { get; set; } = 50;

    // Reputation: dynamic, earned/lost over time (a competition's standing/media profile
    // can genuinely rise or fall - e.g. a domestic T20 league that consistently attracts
    // strong overseas players and big crowds can grow in reputation beyond its starting
    // prestige). Deliberately a separate number from Prestige, not a duplicate of it.
    // Consumed today by SponsorshipValuationService (commercial pull) and
    // MatchdayRevenueService (how big a draw the fixture is). It is NOT yet moved by
    // anything - nothing simulates a competition's media profile rising or falling until
    // Phase 4 produces seasons of results and Phase 7 adds the media/news system. Until
    // then it is a settable starting value, not a dynamic one, and that is stated rather
    // than implied.
    public double Reputation { get; set; } = 50;

    /// <summary>
    /// When this competition is staged and how often. Defaults to an annual all-year window so
    /// existing competitions keep working, but the whole point is that they shouldn't stay that
    /// way: a World Cup is a cycle, a domestic season is a slot. See CompetitionCalendarService.
    /// </summary>
    public CompetitionWindow Window { get; set; } = new();

    /// <summary>
    /// Only meaningful when StructureType is LeagueWithPlayoffs. Defaults to StraightKnockout
    /// so every existing/seeded competition keeps working without needing this set explicitly -
    /// PlayoffBracketService reads it when generating the playoff stage.
    /// </summary>
    public PlayoffFormat PlayoffFormat { get; set; } = PlayoffFormat.StraightKnockout;

    /// <summary>
    /// Only meaningful for a LeagueWithPlayoffs / GroupStageKnockout competition: how many teams
    /// go through to the playoff stage. Defaults 0, which the season runner reads as "the sensible
    /// default for the structure" (4 for a league playoff, 2 per group for a group stage).
    /// </summary>
    public int PlayoffQualifierCount { get; set; }

    /// <summary>
    /// Phase 6, Slice 6.3: the division directly below this one, when the domestic structure is a
    /// two-tier one with promotion and relegation. Null (the default) means this competition is
    /// standalone - no promotion/relegation, which is every pre-Phase-6 competition.
    /// </summary>
    public Guid? SecondTierCompetitionId { get; set; }

    /// <summary>How many teams swap between this division and the one below it at the end of each season. 0 (the default) means none, even if SecondTierCompetitionId is set.</summary>
    public int PromotionRelegationCount { get; set; }

    /// <summary>
    /// Phase 9, Slice 9.5: the maximum number of OVERSEAS players (nationality != the competition's
    /// country, or != the franchise's home nation) allowed in a matchday XI. 0 (the default) = no
    /// limit, which is every pre-Phase-9 competition. A franchise league sets this to ~4.
    /// OverseasRegistrationService enforces it; the players over the cap read as
    /// UnavailabilityReason.Unregistered for that fixture.
    /// </summary>
    public int OverseasPlayerLimit { get; set; }

    /// <summary>
    /// Phase 9, Slice 9.5: true for a franchise league whose squads are assembled by AUCTION from a
    /// world-wide player pool rather than from a domestic club's roster. FranchiseAuctionService
    /// runs an auction ahead of each season; players sign short ContractKind.Franchise deals.
    /// </summary>
    public bool IsFranchiseAuctionLeague { get; set; }

    /// <summary>
    /// Post-Phase-7/8/9 rectification (Section G): the year the last MEGA auction (full squad reset,
    /// up to 6 retentions) was held. A mega runs every ~3 years; the years between are MINI auctions
    /// (carry-over + gap-fill, smaller purses). 0 = never held one yet, so the next auction is a mega.
    /// </summary>
    public int LastMegaAuctionYear { get; set; }

    /// <summary>Section E: the maximum overseas players allowed in a franchise SQUAD (the roster). OverseasPlayerLimit is the tighter playing-XI cap. Default 8.</summary>
    public int OverseasSquadLimit { get; set; } = 8;

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 7: a "major" - a World-Cup / WTC-tier competition,
    /// the kind that defines a coaching career and against which a board's multi-year judgment is
    /// really measured. Derived (an international competition of genuine prestige) rather than a
    /// stored flag, so it can never disagree with the competition's own standing.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsMajor => Scope == CompetitionScope.International && Prestige >= 75;

    /// <summary>
    /// Phase 15 (§16.5): this competition's playing conditions - DRS reviews, over-rate penalty
    /// severity, tie-break rule. Null (the default) means "use the sensible default for my scope
    /// and format" (<see cref="ValueObjects.PlayingConditions.For"/>), which is no DRS and standard
    /// penalties for every pre-Phase-15 competition. WorldSeeder sets a real one on the WTC / ODI
    /// Championship / top franchise leagues.
    /// </summary>
    public ValueObjects.PlayingConditions? PlayingConditions { get; set; }

    /// <summary>
    /// Tech-debt item 9: this limited-overs competition awards a bonus point for a big-margin win
    /// (a real feature of several domestic T20 leagues). Default false - every pre-existing
    /// competition scores the plain way. `MatchRecorder.UpdateStandings` reads it.
    /// </summary>
    public bool UsesLimitedOversMarginBonus { get; set; }

    /// <summary>
    /// Post-Phase-16 completion pass (§10.3): the competition's current broadcast-rights deal - a
    /// multi-year contract with a fixed annual value, renegotiated when it expires against the
    /// competition's standing at that point (a competition that has grown gets a bigger deal; one
    /// that has slipped gets less). Null (the default) = no fixed deal, and `BroadcastRevenueService`
    /// falls back to computing the pool from prestige/reputation each season, exactly as before.
    /// </summary>
    public ValueObjects.BroadcastDeal? BroadcastDeal { get; set; }

    /// <summary>
    /// Post-Phase-16 completion pass (§9.8): a hard salary cap on a participating club's total
    /// domestic player wage bill (some leagues run one). Null (the default) = no cap. Enforced by
    /// SquadNeeds.WithinSalaryCap, which the free-agent and transfer markets check before a signing.
    /// </summary>
    public double? SalaryCap { get; set; }

    /// <summary>
    /// Phase 10 (§17.5): consecutive years this competition's reputation has been notably high
    /// (positive) or notably low (negative). Reset when it crosses back through the middle band.
    /// CompetitionLifecycleService moves it; at +/-3 the competition expands or contracts.
    /// </summary>
    public int ReputationStreakYears { get; set; }

    /// <summary>
    /// Phase 10: extra promotion places the tier below gets for the coming season (a THRIVING
    /// league expands) - positive - or extra relegations with no matching promotion (a FADING
    /// league contracts) - negative. Consumed once by CompetitionSeasonRunner's P/R resolution.
    /// </summary>
    public int PendingExpansion { get; set; }

    /// <summary>The effective playing conditions - the configured set, or the scope/format default.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ValueObjects.PlayingConditions EffectiveConditions =>
        PlayingConditions ?? ValueObjects.PlayingConditions.For(Scope, Format);

    /// <summary>
    /// Corrections pass (correction 2): how much the HOME side can shape a match's pitch character
    /// to suit itself, 0..1. Null (the default) uses <see cref="EffectiveHomePitchInfluence"/>.
    /// Verified against real cricket, September 2026:
    /// - <b>ICC events</b> (<see cref="IsMajor"/>) use ICC-appointed pitch preparation -> near-zero.
    /// - <b>Bilateral / domestic first-class</b> genuinely allows a large, deliberate swing (PAK v
    ///   ENG, Oct 2024: the same Multan square reused and dried out, a raw Rawalpindi turner
    ///   prepared for the decider - all rated "satisfactory") -> full.
    /// - <b>Franchise T20 leagues</b> (IPL/BBL/PSL directly researched; CPL/SA20 inferred by the
    ///   three-league pattern) - the home FRANCHISE does not dictate pitch character (central
    ///   curators, local-curator control, or shared venues) -> near-zero. IPL's own rule tightened
    ///   in exactly this direction for 2025-26, so this is a per-competition value, not a constant.
    /// </summary>
    public double? HomePitchInfluence { get; set; }

    /// <summary>The effective home-pitch-influence factor - the configured value, or the scope-driven default.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double EffectiveHomePitchInfluence => HomePitchInfluence ?? (
        IsMajor ? 0.15 :
        IsFranchiseAuctionLeague ? 0.0 :
        1.0);

    public void AdjustReputation(double delta) => Reputation = Math.Clamp(Reputation + delta, 0, 100);
}
