using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Post-Phase-7/8/9 rectification (Sections B + C): the administrative and economic profile of a
/// cricketing nation. Held in WorldState.CountryProfiles keyed by the nationality string.
///
/// The point (Section B): cricket's domestic structure is NOT one model applied everywhere, and a
/// board like Pakistan's restructures it almost every season - so this is the MACHINERY for a
/// per-country model, seeded with a couple of illustrative variants, NOT a hardcoded snapshot of
/// any real board's current rules (that is the external-data-layer's job, and it would be stale
/// within a season regardless).
/// </summary>
public sealed class CountryProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Nationality { get; init; }

    public MembershipStatus Membership { get; set; } = MembershipStatus.FullMember;

    public DomesticStructureModel DomesticStructure { get; set; } = DomesticStructureModel.ClubMembership;

    /// <summary>
    /// Section C - a DELIBERATE deviation from the real world (where most boards restrict domestic
    /// cricket to their own nationals): whether this country's domestic competitions admit foreign
    /// players at all. Data-driven per country so a future real-data layer can switch a specific
    /// country off it without an engine change. Defaults true - the deviation IS "everyone opens up".
    /// </summary>
    public bool AllowsForeignDomesticPlayers { get; set; } = true;

    /// <summary>
    /// Domestic foreign-player rules (the user's "every country benefits from every other" principle -
    /// deliberately DIFFERENT from the franchise-league caps, which are 4-in-XI / 8-in-squad):
    /// <list type="bullet">
    /// <item>at most <see cref="DomesticSquadOverseasLimit"/> overseas players in the whole squad (default 4),</item>
    /// <item>of which at least <see cref="DomesticMinAssociatesInSquad"/> must be from an ASSOCIATE nation
    ///       (default 1) - equivalently, at most <see cref="ForeignDomesticFullMemberMax"/> from other
    ///       full members (default 3),</item>
    /// <item>at most <see cref="DomesticXiOverseasLimit"/> overseas players in the matchday XI (default 2).</item>
    /// </list>
    /// <see cref="ForeignDomesticLimit"/> is kept as an alias for the SQUAD limit for older callers.
    /// </summary>
    public int DomesticSquadOverseasLimit { get; set; } = 4;
    public int DomesticXiOverseasLimit { get; set; } = 2;
    public int DomesticMinAssociatesInSquad { get; set; } = 1;
    public int ForeignDomesticFullMemberMax { get; set; } = 3;

    /// <summary>Back-compat alias: the SQUAD overseas limit (was previously read as an XI limit; the XI limit is now DomesticXiOverseasLimit = 2).</summary>
    public int ForeignDomesticLimit
    {
        get => DomesticSquadOverseasLimit;
        set => DomesticSquadOverseasLimit = value;
    }

    /// <summary>
    /// Follow-up (regional weather): this country's baseline rain-interruption risk multiplier -
    /// a monsoon belt or an English summer loses far more time to weather than a dry-season venue.
    /// 1.0 = the reference. Composes with the month-of-year and venue in MatchWeatherService.
    /// </summary>
    public double RainRiskMultiplier { get; set; } = 1.0;

    /// <summary>
    /// Section C: the country's cricket-economy scale, 1.0 = the reference economy. A valuation or
    /// wage in a smaller cricket economy is genuinely smaller in real terms, not just displayed in
    /// a different currency. Composes with WorldState.MarketIndex (global inflation over time).
    /// </summary>
    public double EconomicScale { get; set; } = 1.0;

    /// <summary>Section C: which half of the year this country's domestic season sits in - drives region-based transfer windows rather than one global date.</summary>
    public Hemisphere Hemisphere { get; set; } = Hemisphere.Northern;

    /// <summary>Section J: the currency LABEL for display (presentation-layer only - the economics is EconomicScale above).</summary>
    public string CurrencyLabel { get; set; } = "$";

    // ---- Phase 10: the "Country" layer - talent production and home conditions ----
    // The roadmap's original Phase 10 named a separate `Country` entity for this; it lives here
    // instead because CountryProfile already IS the per-nation record every service reads, and a
    // parallel entity would duplicate the Nationality key and the persistence. Every field has a
    // neutral default so a world/profile seeded before Phase 10 is unchanged.

    /// <summary>
    /// 0-100, 55 = the reference. How much cricketing talent this nation produces relative to its
    /// size - drives academy intake VOLUME and the average ceiling of a cohort (AcademyService).
    /// A big cricket nation with deep grassroots sits high; a small one, low. Composes with the
    /// club's own youth facilities, so a well-run academy in a small nation still out-produces a
    /// neglected one in a big nation.
    /// </summary>
    public double TalentProduction { get; set; } = 55;

    /// <summary>0-100, 50 = balanced. Above 50 this nation's academies skew toward producing seam/pace bowlers; below 50, toward spin. Read by AcademyService when shaping a cohort's role mix.</summary>
    public double PaceVsSpinTalentBias { get; set; } = 50;

    /// <summary>0-100. How much the board invests in the youth pathway - a small, slow bonus to every academy cohort's development rate in this nation, and a lever the national-board judgment can reward a coach for.</summary>
    public double BoardYouthInvestment { get; set; } = 50;

    /// <summary>
    /// -20..+20, applied to the PitchSeamFriendliness of grounds in this country at seed time. A
    /// green-top nation (England) sits positive; a road/spin nation sits negative or zero. This is
    /// what makes touring a genuinely different challenge - see the tour-acclimatisation arc.
    /// </summary>
    public double HomePitchSeamBias { get; set; }

    /// <summary>-20..+20, applied to the PitchSpinRating of grounds in this country at seed time. The subcontinent sits well positive.</summary>
    public double HomePitchSpinBias { get; set; }

    /// <summary>0-100. Political / administrative stability of the board - a low value means more selection interference, more abrupt coach sackings, a choppier fixture calendar. Blends into NationalBoard.Politicisation effects.</summary>
    public double PoliticalStability { get; set; } = 65;

    /// <summary>
    /// Seven-suggestions pass (S5): a 0-100 proxy for the nation's cricketing HERITAGE / seniority -
    /// how long it has been a Test nation, its historical standing in the game. One of the four
    /// weights in the ICC annual revenue split (alongside recent ICC-event performance, commercial
    /// draw, and an equal Full-Member share), matching the real ICC criteria. Seeded RNG-free per
    /// nation. The founding Test nations sit at the top; a recent Full Member near the bottom; an
    /// associate lower still.
    /// </summary>
    public double CricketHistoryWeight { get; set; } = 30;

    /// <summary>
    /// Seven-suggestions pass (S5): this board's accumulated points toward ICC Full Membership -
    /// qualifier wins, results against Full Members, sustained youth investment. Only meaningful
    /// while <see cref="Membership"/> is Associate; FullMembershipService reads it, and the grant
    /// is irrevocable (ICC Article 2.7) so it stops moving once Full Membership is reached.
    /// </summary>
    public double FullMembershipCredit { get; set; }

    /// <summary>Seven-suggestions pass (S7): the date this associate first became a public Full-Membership candidate. Null until then; a grant requires at least <see cref="Services.FullMembershipService.MinYearsAsCandidate"/> years as a candidate.</summary>
    public DateOnly? FullMembershipCandidateSince { get; set; }

    /// <summary>
    /// Phase 16 (§15.6): the phase (0-1) of this nation's talent-production cycle. Nations go
    /// through golden generations and fallow decades - a slow sine wave, offset per nation - and
    /// this is where on that wave the country sits at world creation. Seeded RNG-free from the
    /// nationality; <see cref="Services.AcademyService.EraQualityFactor"/> reads it.
    /// </summary>
    public double GenerationCyclePhase { get; set; }

    // ---- Phase 12: the per-nation media market ----

    /// <summary>0-100. How loud and relentless this nation's cricket media is - England / India / Pakistan sit high, New Zealand low. Scales news prominence, press-conference heat and how hard a result is scrutinised.</summary>
    public double MediaIntensity { get; set; } = 55;

    /// <summary>0-100. How partial the home media is to the home side - high means a home hero is lionised and an opponent barely covered; low means an even hand.</summary>
    public double MediaHomeBias { get; set; } = 50;

    /// <summary>0-100. How quickly the media narrative swings - high means a hero one week is a villain the next, and a losing run becomes a crisis fast. Amplifies BoardTrust / reputation swings for a coach working in this market.</summary>
    public double MediaVolatility { get; set; } = 50;

    /// <summary>A single 0.7-1.6 multiplier on the amplitude of media-driven effects (news prominence, press heat, board-trust swings) in this market. Derived from Intensity + Volatility.</summary>
    public double MediaPressureFactor => Math.Clamp(0.7 + (MediaIntensity * 0.6 + MediaVolatility * 0.4) / 100.0 * 0.9, 0.7, 1.6);
}

/// <summary>Post-Phase-7/8/9 rectification (Section C): which hemisphere's calendar a country's domestic season follows - roughly an April-September "northern" season and an October-March "southern" one.</summary>
public enum Hemisphere
{
    Northern,
    Southern
}
