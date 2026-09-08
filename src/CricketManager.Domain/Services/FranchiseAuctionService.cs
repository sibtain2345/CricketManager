using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-Phase-7/8/9 rectification (Sections F + G): the franchise auction rebuilt as a genuine
/// strategic fight, not a price-only mechanism. Grounded against the real IPL 2025/2026 cycle
/// (numbers confirmed by research and recorded in CLAUDE.md so they do not silently drift):
///
/// - <b>Mega vs mini.</b> A MEGA auction (full squad reset, up to 6 retentions + RTM) runs every
///   ~3 years; the years between are MINI auctions - the squad carries over and only released,
///   expired or retired slots go under the hammer, with smaller purses.
/// - <b>Retention + RTM</b> (FranchiseRetentionService) resolves before the auction proper.
/// - <b>Registration -> interest voting -> a shortlist.</b> Every eligible player registers; every
///   franchise casts real interest per player (reputation, recent form, potential, squad need,
///   role fit, in-country record); a player nobody wants drops out. The shortlist is sized to the
///   number of franchises and structurally leans domestic (overseas slots are scarcer).
/// - <b>Base-price self-selection.</b> Each shortlisted player picks a bracket off the capped or
///   uncapped ladder; a fading player self-selects a lower one out of fear of going unsold.
/// - <b>Ordered sets</b> - marquee, then capped by role, then uncapped by role, then an
///   accelerated round for the unsold.
/// - <b>A strict bid staircase</b> - a bid is only ever the current price plus the correct
///   increment for that price band.
/// - <b>Pre-auction plans</b> (FranchiseAuctionPlan) the service consults, and <b>adaptive
///   bidding</b> - a lost target falls back to a plan-B, a filled role stops drawing spend, a plan
///   that is working frees spend for an unplanned standout, and escalation is sensitive to how
///   much purse RIVALS still have for that role (purse-pressure / fear of missing out).
///
/// Transparency (Section F): RunAuction returns an <see cref="AuctionSummary"/> - the full
/// shortlist, every player's final status, and every franchise's remaining purse - so a UI can
/// show the whole picture live, not just one franchise's own view.
/// </summary>
public sealed class FranchiseAuctionService
{
    private readonly PlayerContractService _contracts = new();
    private readonly PlayerValuationService _valuation = new();
    private readonly FranchiseRetentionService _retention = new();
    private readonly FirstHandKnowledgeService _firstHand = new(); // requirement C
    private readonly FranchiseIdentityService _identity = new(); // requirement B

    private const int MegaAuctionCycleYears = 3;
    private const int SquadTarget = 15;
    private const int SquadMinimum = 12;

    // ---------------- public results ----------------

    public sealed record AuctionLot(Guid PlayerId, string PlayerName, AuctionSet Set, bool Overseas,
        double BasePrice, double FinalPrice, Guid? WinningFranchiseId, bool ViaRtm);

    public sealed record AuctionSummary(
        Guid CompetitionId, AuctionMode Mode, int Year,
        IReadOnlyList<Guid> ShortlistPlayerIds,
        IReadOnlyList<AuctionLot> Lots,
        IReadOnlyDictionary<Guid, double> RemainingPurseByFranchise,
        IReadOnlyList<GameEvent> Events);

    // ---------------- entry point ----------------

    public IEnumerable<GameEvent> RunAuction(WorldState world, Competition competition, CompetitionSeason season, DateOnly date, Random random) =>
        RunAuctionDetailed(world, competition, season, date, random).Events;

    public AuctionSummary RunAuctionDetailed(WorldState world, Competition competition, CompetitionSeason season, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();
        var franchises = season.ParticipatingTeamIds
            .Select(id => world.Teams.TryGetValue(id, out var t) ? t : null)
            .Where(t => t is not null).Select(t => t!)
            .OrderBy(t => t.Name)
            .ToList();
        var empty = new AuctionSummary(competition.Id, AuctionMode.Mini, date.Year, Array.Empty<Guid>(),
            Array.Empty<AuctionLot>(), new Dictionary<Guid, double>(), events);
        if (franchises.Count < 2) return empty;

        var playersById = world.Players.ToDictionary(p => p.Id);
        var order = franchises.Select((f, i) => (f.Id, i)).ToDictionary(x => x.Id, x => x.i);

        bool mega = competition.LastMegaAuctionYear == 0 || date.Year - competition.LastMegaAuctionYear >= MegaAuctionCycleYears;
        var mode = mega ? AuctionMode.Mega : AuctionMode.Mini;

        // Currency: IPL ~120 crore purse -> our crore-equivalent unit. The purse basis is shared
        // with FranchiseFinanceService so the auction spend and the central TV pool that funds it
        // move together (a bigger market -> a bigger purse AND a bigger broadcast deal).
        double fullPurse = FranchiseFinanceService.PurseBasis(world, competition);
        double miniPurse = Math.Round(fullPurse * 0.30, 0);
        double croreEquiv = fullPurse / 120.0;

        var purses = new Dictionary<Guid, double>();
        var overseasSquadCount = franchises.ToDictionary(f => f.Id, _ => 0);
        var plans = new Dictionary<Guid, FranchiseAuctionPlan>();

        // ---- retention (mega only) ----
        var retainedIds = new HashSet<Guid>();
        var rtmByFranchise = new Dictionary<Guid, (int Cards, HashSet<Guid> Eligible)>();
        if (mega)
        {
            var outcomes = new List<FranchiseRetentionService.RetentionOutcome>();
            foreach (var f in franchises)
                outcomes.Add(_retention.RunRetention(world, competition, f, date, croreEquiv, fullPurse, random));

            // Clear squads + expire the old franchise contracts AFTER every retention is decided.
            foreach (var f in franchises) f.SquadPlayerIds.Clear();
            foreach (var c in world.PlayerContracts.Where(c => c.Kind == ContractKind.Franchise && c.CompetitionId == competition.Id && c.Status == ContractStatus.Active))
                c.Status = ContractStatus.Expired;

            foreach (var oc in outcomes)
            {
                var f = franchises.First(x => x.Id == oc.FranchiseId);
                purses[f.Id] = fullPurse - oc.PurseSpent;
                rtmByFranchise[f.Id] = (oc.RtmCards, oc.RtmEligible.ToHashSet());
                events.AddRange(oc.Events);
                double slabWage = oc.Retained.Count > 0 ? Math.Round(oc.PurseSpent / oc.Retained.Count, 0) : Math.Round(croreEquiv, 0);
                foreach (var pid in oc.Retained.Concat(oc.ReserveRetained))
                {
                    if (!playersById.TryGetValue(pid, out var rp)) continue;
                    retainedIds.Add(pid);
                    f.SquadPlayerIds.Add(pid);
                    if (IsOverseas(rp, competition.Country)) overseasSquadCount[f.Id]++;
                    double wage = oc.ReserveRetained.Contains(pid) ? Math.Round(croreEquiv * 0.30, 0) : slabWage;
                    _contracts.Sign(world, rp, f, date, annualWage: wage, years: 1, kind: ContractKind.Franchise, competitionId: competition.Id);
                }
            }
            competition.LastMegaAuctionYear = date.Year;
        }
        else
        {
            // Mini: prune retired / no-longer-eligible players; the rest carry over.
            foreach (var f in franchises)
            {
                f.SquadPlayerIds.RemoveAll(id => !playersById.TryGetValue(id, out var p) || p.IsRetired || p.RetiredFormats.Contains(MatchFormat.T20));
                foreach (var id in f.SquadPlayerIds) retainedIds.Add(id);
                purses[f.Id] = miniPurse;
                rtmByFranchise[f.Id] = (0, new HashSet<Guid>());
                overseasSquadCount[f.Id] = f.SquadPlayerIds.Count(id => playersById.TryGetValue(id, out var p) && IsOverseas(p!, competition.Country));
            }
            // Re-sign the carried-over players on fresh one-season franchise contracts.
            foreach (var c in world.PlayerContracts.Where(c => c.Kind == ContractKind.Franchise && c.CompetitionId == competition.Id && c.Status == ContractStatus.Active))
                c.Status = ContractStatus.Expired;
            foreach (var f in franchises)
                foreach (var id in f.SquadPlayerIds)
                    if (playersById.TryGetValue(id, out var p))
                        _contracts.Sign(world, p!, f, date, annualWage: Math.Round(croreEquiv, 0), years: 1, kind: ContractKind.Franchise, competitionId: competition.Id);
        }

        // ---- registration + interest voting -> shortlist ----
        var windowStart = new CompetitionCalendarService().GetStaging(competition, season.Year)?.StartDate
            ?? new DateOnly(season.Year, competition.Window.StartMonth, competition.Window.StartDay);

        var registrants = world.Players
            .Where(p => !p.IsRetired && p.AcademyTeamId is null && !retainedIds.Contains(p.Id)
                        && (p.LoanReturnDate is null || p.LoanReturnDate.Value <= windowStart)
                        // Phase 10: a player whose board has denied him an NOC is out of this auction.
                        && (p.NocWithheldUntil is null || p.NocWithheldUntil.Value <= windowStart)
                        && !p.RetiredFormats.Contains(MatchFormat.T20)
                        && AbilityScale.CompositeAbilityToHundred(p.CurrentAbility) >= 32)
            .OrderByDescending(SquadNeeds.OverallScore)
            .ToList();

        // Slots each franchise still needs.
        var slotsNeeded = franchises.ToDictionary(f => f.Id, f => Math.Max(0, SquadTarget - f.SquadPlayerIds.Count));
        int totalSlots = slotsNeeded.Values.Sum();
        int wantShortlist = totalSlots + franchises.Count * 3 + 10;
        int shortlistSize = Math.Min(Math.Max(wantShortlist, Math.Min(12, registrants.Count)), registrants.Count);

        var interest = new Dictionary<Guid, int>();
        foreach (var p in registrants)
        {
            int total = 0;
            foreach (var f in franchises)
            {
                if (slotsNeeded[f.Id] == 0) continue;
                total += InterestVote(world, f, p, competition, playersById);
            }
            if (total > 0) interest[p.Id] = total;
        }

        // The shortlist: everyone a franchise voted real interest in, best-first; then topped up
        // with the best remaining registrants so there are always enough bodies to fill every
        // squad (a franchise still has to field an XI even from a thin pool).
        var wanted = registrants
            .Where(p => interest.ContainsKey(p.Id))
            .OrderByDescending(p => interest[p.Id] + SquadNeeds.OverallScore(p) * 0.2)
            .ToList();
        var shortlist = wanted.Take(shortlistSize).ToList();
        if (shortlist.Count < shortlistSize)
        {
            var have = shortlist.Select(p => p.Id).ToHashSet();
            shortlist.AddRange(registrants
                .Where(p => !have.Contains(p.Id))
                .OrderByDescending(SquadNeeds.OverallScore)
                .Take(shortlistSize - shortlist.Count));
        }

        // Meeting-driven-selection ticket (E): the EOI -> auction-list conversion, narrated as the
        // all-franchises meeting it really is (the coaches, staff and captains in one room deciding
        // who converts) rather than a silent filter. RNG-free - a wrap of the interest vote above.
        events.Add(NarrateEoiConversion(competition, date, registrants.Count, interest, wanted, shortlist));

        // ---- base-price self-selection ----
        var basePriceById = shortlist.ToDictionary(p => p.Id, p => SelfSelectBasePrice(p, croreEquiv, date, random));

        // ---- pre-auction plans ----
        var prevSeason = world.CompetitionSeasons
            .Where(s => s.CompetitionId == competition.Id && s.Year < season.Year && s.IsCompleted)
            .OrderByDescending(s => s.Year).FirstOrDefault();
        foreach (var f in franchises)
        {
            plans[f.Id] = BuildPlan(world, f, competition, shortlist, basePriceById, interest, slotsNeeded[f.Id],
                purses[f.Id], competition.OverseasSquadLimit - overseasSquadCount[f.Id],
                rtmByFranchise[f.Id].Cards, rtmByFranchise[f.Id].Eligible, playersById, croreEquiv, date, prevSeason);

            // Meeting-driven-selection ticket (D): the pre-auction planning meeting - last season
            // reviewed, strengths and gaps named, the plan the meeting produced surfaced. RNG-free.
            events.Add(NarratePreAuctionMeeting(f, competition, date, prevSeason, plans[f.Id], playersById, croreEquiv));
        }

        // Phase 13 (§8.9): the retained core going INTO the auction - the dynasty-continuity signal.
        var coreById = franchises.ToDictionary(f => f.Id, f => f.SquadPlayerIds.Count);

        // ---- the sets ----
        // Corrections pass (correction 1): the set order is PURELY role/tier-driven - marquee,
        // then a full round of capped players by role (batter, all-rounder, wicketkeeper, fast
        // bowler, spinner), then the same round for uncapped, then the accelerated round. This is
        // the real IPL structure (verified) and NO franchise's own priority moves a player earlier:
        // a marquee name is auctioned early because he is IN the marquee set, not because anyone
        // wants him. Priority feeds BIDDING (how hard a franchise fights, and its plan-B pivot when
        // a target is lost - see Ceiling and the lost-must-have re-rank below), never sequencing.
        // Within a set, best-first by interest + overall score (the real "Set 1 = the top players").
        var lots = new List<AuctionLot>();
        var unsold = new List<Player>();
        foreach (var set in OrderedSets())
        {
            var setPlayers = shortlist
                .Where(p => ClassifySet(p) == set)
                .OrderByDescending(p => interest.GetValueOrDefault(p.Id) + SquadNeeds.OverallScore(p) * 0.25)
                .ToList();

            foreach (var player in setPlayers)
            {
                var lot = RunLot(world, competition, player, set, franchises, order, plans, purses,
                    overseasSquadCount, basePriceById[player.Id], croreEquiv, date, playersById, random, events);
                lots.Add(lot);
                if (lot.WinningFranchiseId is null) unsold.Add(player);
                else NoteLostMustHave(lot, plans);
            }
        }

        // ---- accelerated / leftover round: the VALUE phase (§8.6). A player who went unsold in his
        // ordered set comes back at his ORIGINAL base price (the fixed brackets are sacrosanct) -
        // but with most squads now near-full there are fewer bidders, so he tends to go for close
        // to that base. A franchise with purse still in hand and a genuine gap picks up real value
        // here - which is exactly what the accelerated round is for (RunLot lifts its interest a
        // notch for a franchise that still needs the role). ----
        foreach (var player in unsold.OrderByDescending(p => SquadNeeds.OverallScore(p)).ToList())
        {
            if (franchises.All(f => f.SquadPlayerIds.Count >= SquadTarget)) break;
            var lot = RunLot(world, competition, player, AuctionSet.Accelerated, franchises, order, plans, purses,
                overseasSquadCount, basePriceById[player.Id], croreEquiv, date, playersById, random, events);
            // Replace the earlier unsold record.
            lots[lots.FindIndex(l => l.PlayerId == player.Id)] = lot;
            if (lot.WinningFranchiseId is not null) NoteLostMustHave(lot, plans);
        }

        // ---- top-up: every franchise must be able to field an XI ----
        var soldIds = lots.Where(l => l.WinningFranchiseId is not null).Select(l => l.PlayerId).ToHashSet();
        var leftovers = registrants.Where(p => !soldIds.Contains(p.Id) && interest.ContainsKey(p.Id) == false && AbilityScale.CompositeAbilityToHundred(p.CurrentAbility) >= 30)
            .Concat(shortlist.Where(p => !soldIds.Contains(p.Id)))
            .DistinctBy(p => p.Id)
            .OrderByDescending(SquadNeeds.OverallScore)
            .ToList();
        foreach (var f in franchises.Where(f => f.SquadPlayerIds.Count < SquadMinimum))
        {
            int cursor = 0;
            while (f.SquadPlayerIds.Count < SquadMinimum && cursor < leftovers.Count)
            {
                var p = leftovers[cursor++];
                if (f.SquadPlayerIds.Contains(p.Id)) continue;
                bool overseas = IsOverseas(p, competition.Country);
                // Respect the overseas squad cap - unless it is the ONLY way to reach a legal
                // minimum squad (a franchise hosted in a nation with a thin domestic base).
                if (overseas && overseasSquadCount[f.Id] >= competition.OverseasSquadLimit)
                {
                    bool domesticLeftExists = leftovers.Skip(cursor).Any(x => !IsOverseas(x, competition.Country) && !f.SquadPlayerIds.Contains(x.Id));
                    if (domesticLeftExists) continue;
                }
                double fee = Math.Max(croreEquiv * 0.25, Math.Round(_valuation.EstimateValue(p, null, date, world.MarketIndex) * 0.15, 0));
                f.SquadPlayerIds.Add(p.Id);
                if (overseas) overseasSquadCount[f.Id]++;
                _contracts.Sign(world, p, f, date, annualWage: fee, years: 1, kind: ContractKind.Franchise, competitionId: competition.Id);
                f.Finances.Budget -= fee;
                purses[f.Id] = Math.Max(0, purses[f.Id] - fee);
            }
        }

        int filled = franchises.Sum(f => f.SquadPlayerIds.Count);
        int sold = lots.Count(l => l.WinningFranchiseId is not null);
        events.Add(new GameEvent(date, GameEventType.PlayerAuctioned,
            $"The {competition.Name} {mode.ToString().ToLowerInvariant()} auction is complete - {sold} players sold from a shortlist of {shortlist.Count}, {filled} across {franchises.Count} franchises.",
            competition.Id));

        // Phase 13 (§8.9): rebuild each franchise's DYNASTY rating - squad continuity (retained
        // core) plus recent titles in this league. A settled, winning franchise builds a real aura.
        int windowYears = 5;
        foreach (var f in franchises)
        {
            int retained = coreById.GetValueOrDefault(f.Id, 0);
            double continuity = Math.Clamp(retained / 6.0, 0, 1);
            int titles = world.CompetitionSeasons.Count(s => s.CompetitionId == competition.Id
                && s.IsCompleted && s.ChampionTeamId == f.Id && s.Year > date.Year - windowYears);
            double target = Math.Clamp(30 + continuity * 40 + titles * 12, 0, 100);
            f.DynastyRating = Math.Round(f.DynastyRating + (target - f.DynastyRating) * 0.4, 1);
            // The aura pays: a small lift to fan sentiment and to the on-field culture.
            if (f.DynastyRating >= 65)
            {
                f.Board.FanSentiment = Math.Clamp(f.Board.FanSentiment + 2, 0, 100);
                f.DressingRoomHarmony = Math.Clamp(f.DressingRoomHarmony + 1.5, 0, 100);
            }

            // Requirement B, driver 1: sustained results / a bad trading year can shift the
            // franchise's whole recruitment identity. Deterministic - only a clear signal, and not
            // again for HysteresisYears.
            var shift = _identity.DriftFromResults(f, continuity, titles, date);
            if (shift is not null) events.Add(shift);
        }

        return new AuctionSummary(competition.Id, mode, date.Year, shortlist.Select(p => p.Id).ToList(), lots, purses, events);
    }

    // ---------------- one lot ----------------

    /// <summary>
    /// §8.4 (Follow-up Pass 3): the pure purse-pressure escalation every franchise applies to its
    /// own bidding ceiling every lot, reading every rival's remaining purse live - a rival sitting
    /// on a deep purse for a role I also need is a real fear-of-missing-out signal (escalate up to
    /// 28% harder); being the only one left with real money for a role lets me relax (a 8% pull
    /// back). Extracted from RunLot as a pure function purely so this specific mechanism is
    /// directly, deterministically unit-testable - the formula itself is unchanged from what
    /// shipped in the original strategic-auction pass.
    /// </summary>
    internal static double ApplyPursePressure(double baseCeiling, double myPurse, double rivalMax)
    {
        if (rivalMax > myPurse * 0.6)
            return baseCeiling * (1 + Math.Clamp(rivalMax / Math.Max(1, myPurse) * 0.12, 0, 0.28));
        if (rivalMax < myPurse * 0.3)
            return baseCeiling * 0.92;
        return baseCeiling;
    }

    private AuctionLot RunLot(
        WorldState world, Competition competition, Player player, AuctionSet set,
        IReadOnlyList<Team> franchises, IReadOnlyDictionary<Guid, int> order,
        Dictionary<Guid, FranchiseAuctionPlan> plans, Dictionary<Guid, double> purses,
        Dictionary<Guid, int> overseasSquadCount, double basePrice, double croreEquiv,
        DateOnly date, IReadOnlyDictionary<Guid, Player> playersById, Random random, List<GameEvent> events)
    {
        bool overseas = IsOverseas(player, competition.Country);
        string roleGroup = RoleGroupOf(player);

        // Each franchise's head coach, resolved once (he is a genuine year-round appointment in
        // post before the auction since the corrections pass).
        var coachByFranchise = franchises.ToDictionary(
            f => f.Id,
            f => f.CurrentCoachId is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null);

        // S1: each franchise's SISTER franchises (same ownership group, other leagues included).
        var sistersByFranchise = franchises.ToDictionary(
            f => f.Id,
            f => f.OwnershipGroupId is { } gid
                ? world.Teams.Values.Where(t => t.Id != f.Id && t.OwnershipGroupId == gid).Select(t => t.Id).ToHashSet()
                : new HashSet<Guid>());

        double Ceiling(Team f)
        {
            var plan = plans[f.Id];
            if (f.SquadPlayerIds.Count >= SquadTarget) return 0;
            if (f.SquadPlayerIds.Contains(player.Id)) return 0;
            if (overseas && overseasSquadCount[f.Id] >= competition.OverseasSquadLimit) return 0;

            var target = plan.Targets.FirstOrDefault(t => t.PlayerId == player.Id);
            double baseCeiling = target?.Ceiling
                ?? _valuation.EstimateValue(player, null, date, world.MarketIndex) * 0.32 * (0.7 + SquadNeeds.OverallScore(player) / 130.0);

            // Role already filled in the plan? Do not stack redundant depth - unless he is a
            // genuine standout AND the plan is on track (then a measured splash is fine).
            int roleWant = plan.TargetsByRole.GetValueOrDefault(roleGroup, 0);
            if (roleWant <= 0)
            {
                bool standout = SquadNeeds.OverallScore(player) >= 78;
                baseCeiling *= standout && plan.OnTrack ? 0.7 : 0.18;
            }

            // Corrections pass (correction 1): priority feeds how hard the franchise FIGHTS.
            // - A genuine must-have (Tier 0) the franchise still needs: go a notch harder.
            // - A fallback (Tier > 0): normally held back while a higher-priority target of the
            //   same role is still to come - UNLESS the must-have for that role has already gone
            //   to a rival, in which case the plan pivots and the fallback becomes plan A.
            if (target is { PriorityTier: 0 } && !plan.OnTrack)
                baseCeiling *= 1.10;
            else if (target is { PriorityTier: > 0 })
                baseCeiling *= plan.RolesWithLostMustHave.Contains(target.RoleGroup) ? 1.12 : 0.75;

            // §8.6: the accelerated round is a value phase - a franchise that still has a genuine
            // gap and purse in hand is more willing to pounce here, where prices are soft.
            if (set == AuctionSet.Accelerated && roleWant > 0 && purses[f.Id] > basePrice * 4)
                baseCeiling *= 1.2;

            // Purse pressure: if rivals who also need this role are sitting on deep purses relative
            // to mine, I escalate harder (fear of missing out). If I am the last one with real
            // money for this role, I can relax. §8.4: this IS the "live in-auction transparency
            // rivals use" mechanism - every franchise reads every other's remaining purse live,
            // every lot. Extracted to ApplyPursePressure (a pure function, identical formula) so
            // the escalation itself is directly, deterministically testable without needing to
            // reconstruct a whole auction.
            double myPurse = purses[f.Id];
            double rivalMax = franchises.Where(x => x.Id != f.Id && plans[x.Id].TargetsByRole.GetValueOrDefault(roleGroup, 0) > 0)
                .Select(x => purses[x.Id]).DefaultIfEmpty(0).Max();
            baseCeiling = ApplyPursePressure(baseCeiling, myPurse, rivalMax);

            // Phase 13 (§8.1): the franchise's ARCHETYPE reshapes how hard it goes.
            double fairValue = _valuation.EstimateValue(player, null, date, world.MarketIndex) * 0.32;
            int age = player.Age(date);
            switch (f.FranchiseArchetype)
            {
                case FranchiseArchetype.Moneyball:
                    // Chase value - never much above fair, and cut hard when the price is silly.
                    baseCeiling = Math.Min(baseCeiling * 0.9, fairValue * 1.05);
                    break;
                case FranchiseArchetype.StarHunter:
                    if (player.Reputation.Worldwide >= 55 || SquadNeeds.OverallScore(player) >= 76) baseCeiling *= 1.28;
                    break;
                case FranchiseArchetype.YouthBuilder:
                    if (age <= 24) baseCeiling *= 1.32;
                    else if (age >= 31) baseCeiling *= 0.68;
                    break;
            }

            // Phase 13 (§8.2/§8.3): name-recognition overvaluation. A big worldwide reputation
            // inflates the PERCEIVED value - and a franchise with a real scouting department sees
            // through it. A canny, well-scouted side lets the hype merchants overpay elsewhere.
            double nameHype = Math.Clamp((player.Reputation.Worldwide - 40) / 100.0, 0, 0.6);
            double scoutDiscount = ScoutingDeptQuality(world, f) / 100.0; // 0..~1
            baseCeiling *= 1 + nameHype * 0.55 * (1 - scoutDiscount);

            // Requirement C: first-hand knowledge. A franchise whose retained core / captain has
            // genuinely shared a dressing room with this (not-globally-known) player backs him
            // harder than the numbers alone justify - the confidence of actually knowing the man.
            // A modest, bounded lever (up to ~+22%) on top of, not instead of, the scouting read
            // above. Deterministic (career-history set overlap, no RNG).
            double firstHandRead = _firstHand.ReadStrength(playersById, f, player, coachByFranchise.GetValueOrDefault(f.Id),
                sistersByFranchise.GetValueOrDefault(f.Id));
            baseCeiling *= 1 + firstHandRead * 0.22;

            // Never bid past a sane reserve for the slots still to fill.
            int slotsLeft = SquadTarget - f.SquadPlayerIds.Count;
            double reserve = Math.Max(0, (slotsLeft - 1)) * croreEquiv * 0.35;
            return Math.Max(0, Math.Min(baseCeiling, myPurse - reserve));
        }

        // Bidders who want him at the base price.
        var bidders = franchises.Where(f => Ceiling(f) >= basePrice).OrderBy(f => order[f.Id]).ToList();
        if (bidders.Count == 0)
            return new AuctionLot(player.Id, player.FullName, set, overseas, basePrice, 0, null, false);

        // Strict staircase: raise the price one increment at a time; the highest-order franchise
        // still willing at the new price is the standing bidder.
        double price = basePrice;
        Team standing = bidders[0];
        while (true)
        {
            double next = price + Increment(price, croreEquiv);
            var stillIn = bidders.Where(f => Ceiling(f) >= next).OrderBy(f => order[f.Id]).ToList();
            if (stillIn.Count == 0) break;
            if (stillIn.Count == 1 && stillIn[0].Id == standing.Id) break; // only the standing bidder left willing to go higher
            price = next;
            standing = stillIn.First(f => f.Id != standing.Id || stillIn.Count == 1);
            bidders = stillIn;
        }

        var winner = standing;
        double finalPrice = Math.Round(Math.Min(price, purses[winner.Id]), 0);
        bool viaRtm = false;

        // ---- RTM (mega only) ----
        var rtmHolder = franchises.FirstOrDefault(f =>
            f.Id != winner.Id
            && plans[f.Id].RtmCards > 0
            && plans[f.Id].RtmEligiblePlayerIds.Contains(player.Id)
            && purses[f.Id] >= finalPrice);
        if (rtmHolder is not null)
        {
            // The RTM twist: the winning franchise gets ONE uncapped raise; the original club then
            // decides at the new price.
            double raised = finalPrice;
            double winnerRaiseCeiling = Ceiling(winner);
            if (winnerRaiseCeiling > finalPrice * 1.1 && random.NextDouble() < 0.6)
                raised = Math.Round(Math.Min(winnerRaiseCeiling, finalPrice * (1.15 + random.NextDouble() * 0.4)), 0);

            double rtmCeiling = _valuation.EstimateValue(player, null, date, world.MarketIndex) * 0.4;
            if (purses[rtmHolder.Id] >= raised && rtmCeiling >= raised * 0.9)
            {
                // RTM exercised.
                plans[rtmHolder.Id].RtmCards--;
                Award(world, competition, player, rtmHolder, raised, set, date, purses, overseasSquadCount, plans, overseas, roleGroup);
                events.Add(new GameEvent(date, GameEventType.RtmExercised,
                    $"{rtmHolder.Name} use a Right-to-Match to keep {player.FullName} for {raised:N0}"
                    + (raised > finalPrice ? $" (after {winner.Name} raised the bid)" : "") + ".",
                    player.Id, rtmHolder.Id));
                return new AuctionLot(player.Id, player.FullName, set, overseas, basePrice, raised, rtmHolder.Id, true);
            }
            finalPrice = raised; // winner keeps him at the raised price
        }

        Award(world, competition, player, winner, finalPrice, set, date, purses, overseasSquadCount, plans, overseas, roleGroup);
        if (finalPrice >= basePrice * 3 || SquadNeeds.OverallScore(player) >= 76 || set == AuctionSet.Marquee)
            events.Add(new GameEvent(date, GameEventType.PlayerAuctioned,
                $"{winner.Name} land {player.FullName} for {finalPrice:N0} at the {competition.Name} auction.",
                player.Id, winner.Id));

        return new AuctionLot(player.Id, player.FullName, set, overseas, basePrice, finalPrice, winner.Id, viaRtm);
    }

    private void Award(
        WorldState world, Competition competition, Player player, Team franchise, double price, AuctionSet set,
        DateOnly date, Dictionary<Guid, double> purses, Dictionary<Guid, int> overseasSquadCount,
        Dictionary<Guid, FranchiseAuctionPlan> plans, bool overseas, string roleGroup)
    {
        franchise.SquadPlayerIds.Add(player.Id);
        purses[franchise.Id] = Math.Max(0, purses[franchise.Id] - price);
        franchise.Finances.Budget -= price;
        if (overseas) overseasSquadCount[franchise.Id]++;
        _contracts.Sign(world, player, franchise, date, annualWage: price, years: 1, kind: ContractKind.Franchise, competitionId: competition.Id);

        var plan = plans[franchise.Id];
        plan.RemainingPurse = purses[franchise.Id];
        plan.SlotsToFill = Math.Max(0, plan.SlotsToFill - 1);
        if (overseas) plan.OverseasSlotsLeft = Math.Max(0, plan.OverseasSlotsLeft - 1);
        plan.TargetsByRole[roleGroup] = plan.TargetsByRole.GetValueOrDefault(roleGroup, 0) - 1;
        plan.BudgetByRole[roleGroup] = plan.BudgetByRole.GetValueOrDefault(roleGroup, 0) - price;
        // Plan is on track once the franchise has secured most of its priority targets.
        int mustHavesLeft = plan.Targets.Count(t => t.PriorityTier == 0 && !franchise.SquadPlayerIds.Contains(t.PlayerId));
        plan.OnTrack = mustHavesLeft <= 1;
    }

    // ---------------- planning + interest ----------------

    /// <summary>A franchise's interest vote in a player (0 = none, 3 = a genuine target). Structurally leans domestic because a franchise has fewer overseas slots to spend.</summary>
    /// <summary>Phase 13 (§8.3): how good this franchise's scouting/analysis department is - a real name-bias filter. 0..~100.</summary>
    private static double ScoutingDeptQuality(WorldState world, Team franchise)
    {
        var staff = world.Staff.Where(s => franchise.StaffIds.Contains(s.Id)).ToList();
        double q = 0;
        if (staff.Any(s => s.Role == StaffRole.ChiefScout)) q += 40;
        if (staff.Any(s => s.Role == StaffRole.Scout)) q += 25;
        if (staff.Any(s => s.Role == StaffRole.DataAnalyst)) q += 25;
        if (staff.Any(s => s.Role == StaffRole.Analyst)) q += 18;
        // A well-run franchise (high board wealth/ambition) also just does better homework.
        q += Math.Clamp((franchise.Board.Wealth + franchise.Board.Ambition) / 2 - 55, 0, 25) * 0.4;
        return Math.Clamp(q, 0, 100);
    }

    /// <summary>
    /// Meeting-driven-selection ticket (E): the EOI register becomes the auction list in a room,
    /// not a spreadsheet. Narrates who converted on genuine franchise interest, who was topped up
    /// to make the numbers, and the notable names who registered but nobody wanted.
    /// </summary>
    private static GameEvent NarrateEoiConversion(
        Competition competition, DateOnly date, int registrantCount,
        IReadOnlyDictionary<Guid, int> interest, IReadOnlyList<Player> wanted, IReadOnlyList<Player> shortlist)
    {
        var shortlistIds = shortlist.Select(p => p.Id).ToHashSet();
        int onInterest = wanted.Count(p => shortlistIds.Contains(p.Id));
        int toppedUp = shortlist.Count - onInterest;

        var marquee = shortlist
            .Where(p => interest.GetValueOrDefault(p.Id) >= 12)
            .OrderByDescending(p => interest.GetValueOrDefault(p.Id))
            .Take(3).Select(p => p.FullName).ToList();

        // A genuinely rated player (registered, real ability) that not one franchise voted for.
        var overlooked = wanted.Concat(shortlist).Distinct()
            .Where(p => !interest.ContainsKey(p.Id) && SquadNeeds.OverallScore(p) >= 58)
            .OrderByDescending(SquadNeeds.OverallScore)
            .Select(p => p.FullName).FirstOrDefault();

        string text = $"The {competition.Name} franchises meet to turn {registrantCount} registrations into a {shortlist.Count}-strong auction list. "
            + $"{onInterest} names go through on genuine interest"
            + (toppedUp > 0 ? $", {toppedUp} more added to make up the numbers." : ".")
            + (marquee.Count > 0 ? $" Most wanted in the room: {string.Join(", ", marquee)}." : "")
            + (overlooked is not null ? $" {overlooked} registered but drew no interest at all - a surprise omission." : "");

        return new GameEvent(date, GameEventType.EoiConversionMeeting, text, competition.Id);
    }

    /// <summary>
    /// Meeting-driven-selection ticket (D): a franchise's pre-auction war-room meeting. Reviews
    /// last season (finishing position, whether it won it, its dynasty standing and trading P&amp;L),
    /// names its biggest gap, and states the plan the meeting produced. RNG-free - a wrap of
    /// BuildPlan's own output.
    /// </summary>
    private static GameEvent NarratePreAuctionMeeting(
        Team franchise, Competition competition, DateOnly date, CompetitionSeason? prevSeason,
        FranchiseAuctionPlan plan, IReadOnlyDictionary<Guid, Player> playersById, double croreEquiv)
    {
        string lastSeason;
        if (prevSeason is null)
            lastSeason = "This is a first campaign - a clean sheet in the room.";
        else
        {
            var ranked = prevSeason.Standings
                .OrderByDescending(s => s.Points).ThenByDescending(s => s.NetRunRate).ToList();
            int pos = ranked.FindIndex(s => s.TeamId == franchise.Id) + 1;
            bool champions = prevSeason.ChampionTeamId == franchise.Id;
            lastSeason = champions
                ? $"Last season ended with the trophy - the meeting is about keeping a winning group together."
                : pos > 0
                    ? $"Last season finished {Ordinal(pos)} of {ranked.Count} - the review is blunt about why."
                    : "Last season is reviewed - a mixed campaign.";
        }

        string dynasty = franchise.DynastyRating >= 65
            ? " A settled, successful group - continuity is the plan."
            : franchise.DynastyRating <= 35
                ? " A group in transition - this auction is a rebuild."
                : "";

        string trading = franchise.PlayerTradingPnL > 500_000
            ? " The trading has gone well and there is money to spend."
            : franchise.PlayerTradingPnL < -500_000
                ? " The books took a hit last year and the purse is tighter than the board would like."
                : "";

        var topGap = plan.TargetsByRole.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).FirstOrDefault();
        string need = topGap.Value > 0
            ? $" The clear priority: {topGap.Key} ({topGap.Value} to find, {plan.SlotsToFill} slots overall)."
            : $" A near-complete squad - {plan.SlotsToFill} slots to top up.";

        var mustHaves = plan.Targets
            .Where(t => t.PriorityTier == 0 && playersById.ContainsKey(t.PlayerId))
            .Select(t => playersById[t.PlayerId].FullName)
            .Take(3).ToList();
        string targets = mustHaves.Count > 0 ? $" Names on the board: {string.Join(", ", mustHaves)}." : "";

        return new GameEvent(date, GameEventType.PreAuctionMeeting,
            $"{franchise.Name} sit down before the {competition.Name} auction. {lastSeason}{dynasty}{trading}{need}{targets}",
            franchise.Id, competition.Id);
    }

    private static string Ordinal(int n) => n switch
    {
        1 => "1st", 2 => "2nd", 3 => "3rd",
        _ => $"{n}th"
    };

    /// <summary>
    /// Corrections pass (correction 1): a lot has just gone to a franchise. Any OTHER franchise
    /// that had this player as a genuine must-have (PriorityTier 0) has lost plan A - mark that
    /// role so Ceiling promotes its fallback for the rest of the auction.
    /// </summary>
    internal static void NoteLostMustHave(AuctionLot lot, IReadOnlyDictionary<Guid, FranchiseAuctionPlan> plans)
    {
        if (lot.WinningFranchiseId is not { } winnerId) return;
        foreach (var (fid, plan) in plans)
        {
            if (fid == winnerId) continue;
            var mustHave = plan.Targets.FirstOrDefault(t => t.PlayerId == lot.PlayerId && t.PriorityTier == 0);
            if (mustHave is not null) plan.RolesWithLostMustHave.Add(mustHave.RoleGroup);
        }
    }

    /// <summary>The archetype/identity-only emphasis - kept for callers that have no home ground or previous season to hand.</summary>
    internal static Dictionary<string, double> RoleEmphasis(Team franchise) =>
        RoleEmphasis(franchise, home: null, prevSeason: null, squad: Array.Empty<Player>());

    /// <summary>
    /// Meeting-driven-selection ticket (G) + corrections pass (correction 2): a franchise's own
    /// view of what its squad should look like, as ONE combined per-SquadNeeds-label multiplier -
    /// its archetype, its cultural identity, its HOME-GROUND CONDITIONS, and the DIAGNOSIS from its
    /// previous campaign, all feeding the same dictionary, consumed once in BuildPlan.
    /// SquadNeeds.Groups() stays the untouched legality/depth FLOOR; this shifts emphasis above it.
    /// 1.0 = the baseline. Real franchises build this way on the record - RCB targeted a
    /// Chinnaswamy-suited squad AT the auction; going in "needing a death option after last year's
    /// collapse" is standard auction-preview framing. Deterministic - fixed lookups + a couple of
    /// thresholds, no RNG.
    /// </summary>
    internal static Dictionary<string, double> RoleEmphasis(
        Team franchise, Ground? home, CompetitionSeason? prevSeason, IReadOnlyList<Player> squad)
    {
        var e = new Dictionary<string, double>
        {
            ["front-line pace"] = 1.0, ["front-line spin"] = 1.0,
            ["top-order batting"] = 1.0, ["middle-order batting"] = 1.0,
            ["all-round balance"] = 1.0, ["wicketkeeping"] = 1.0,
        };
        void Mul(string k, double m) { if (e.ContainsKey(k)) e[k] *= m; }

        switch (franchise.FranchiseArchetype)
        {
            case FranchiseArchetype.StarHunter:
                Mul("top-order batting", 1.25); Mul("front-line pace", 1.15); Mul("all-round balance", 0.85);
                break;
            case FranchiseArchetype.Moneyball:
                Mul("all-round balance", 1.30); Mul("front-line spin", 1.20); Mul("top-order batting", 0.85);
                break;
            case FranchiseArchetype.YouthBuilder:
                Mul("all-round balance", 1.20); Mul("front-line spin", 1.10);
                break;
        }

        switch (franchise.CulturalIdentity)
        {
            case CoachingPhilosophy.AllrounderLeaning:
                Mul("all-round balance", 1.45); Mul("middle-order batting", 0.80);
                break;
            case CoachingPhilosophy.Aggressive:
            case CoachingPhilosophy.ShortTermResults:
                // "the anchor is obsolete in T20" - load the top order, shed the pure accumulator.
                Mul("top-order batting", 1.25); Mul("middle-order batting", 0.80);
                break;
            case CoachingPhilosophy.AnalyticsDriven:
                Mul("front-line spin", 1.20); Mul("all-round balance", 1.15);
                break;
            case CoachingPhilosophy.Defensive:
            case CoachingPhilosophy.ExperienceFocused:
                Mul("middle-order batting", 1.20); Mul("front-line spin", 1.10);
                break;
        }

        // Correction 2: home-ground conditions. A spin-friendly, lower-scoring home venue shifts a
        // franchise toward quality spin and middle-order batting that copes with turn; a pace deck
        // toward seam; a short-boundary / high-scoring venue toward top-order power and depth.
        if (home is not null)
        {
            if (home.PitchSpinRating > home.PitchPaceRating + 8)
            { Mul("front-line spin", 1.30); Mul("middle-order batting", 1.10); Mul("top-order batting", 0.92); }
            else if (home.PitchPaceRating > home.PitchSpinRating + 8)
            { Mul("front-line pace", 1.30); }

            double avgBoundary = (home.SquareBoundaryMetres + home.StraightBoundaryMetres) / 2.0;
            if (home.PitchBattingFriendliness >= 62 || avgBoundary <= 63)
            { Mul("top-order batting", 1.18); Mul("front-line pace", 1.10); Mul("all-round balance", 1.05); }
        }

        // Correction 2: the previous-campaign diagnosis. A poor finish PLUS a genuine, still-thin
        // role group = a named priority going into THIS auction - the exact "we finished bottom
        // half and our death bowling was the reason" read the pre-auction meeting narrates.
        if (prevSeason is not null && squad.Count > 0)
        {
            var ranked = prevSeason.Standings
                .OrderByDescending(s => s.Points).ThenByDescending(s => s.NetRunRate).ToList();
            int pos = ranked.FindIndex(s => s.TeamId == franchise.Id);
            bool poorFinish = pos >= 0 && ranked.Count >= 4 && pos + 1 > ranked.Count / 2.0;
            if (poorFinish && SquadNeeds.WeakestGroup(squad, MatchFormat.T20, minShortfall: -1) is { } weak)
                Mul(weak.Group.Label, 1.35);
        }

        return e;
    }

    private int InterestVote(WorldState world, Team franchise, Player player, Competition competition, IReadOnlyDictionary<Guid, Player> playersById)
    {
        var fSquad = franchise.SquadPlayerIds.Select(id => playersById.GetValueOrDefault(id)).Where(p => p is not null).Select(p => p!).ToList();
        var need = SquadNeeds.WeakestGroup(fSquad, MatchFormat.T20, minShortfall: -1);
        double quality = SquadNeeds.OverallScore(player);
        double form = player.Form.CurrentForm;
        double potential = AbilityScale.CompositeAbilityToHundred(player.PotentialAbility);

        double score = quality * 0.5 + form * 0.15 + potential * 0.15;
        if (need is { } n && n.Group.Matches(player)) score += 18;                 // team need
        if (HasInCountryRecord(player, competition.Country)) score += 6;            // proven in these conditions
        if (IsOverseas(player, competition.Country)) score -= 8;                    // scarcer slots -> higher bar

        return score >= 68 ? 3 : score >= 54 ? 2 : score >= 42 ? 1 : 0;
    }

    private FranchiseAuctionPlan BuildPlan(
        WorldState world, Team franchise, Competition competition, IReadOnlyList<Player> shortlist,
        IReadOnlyDictionary<Guid, double> basePriceById, IReadOnlyDictionary<Guid, int> interest,
        int slotsToFill, double purse, int overseasSlotsLeft, int rtmCards, HashSet<Guid> rtmEligible,
        IReadOnlyDictionary<Guid, Player> playersById, double croreEquiv, DateOnly date,
        CompetitionSeason? prevSeason)
    {
        var plan = new FranchiseAuctionPlan
        {
            FranchiseId = franchise.Id,
            RemainingPurse = purse,
            SlotsToFill = slotsToFill,
            OverseasSlotsLeft = overseasSlotsLeft,
            RtmCards = rtmCards,
        };
        foreach (var id in rtmEligible) plan.RtmEligiblePlayerIds.Add(id);

        var fSquad = franchise.SquadPlayerIds.Select(id => playersById.GetValueOrDefault(id)).Where(p => p is not null).Select(p => p!).ToList();
        var home = franchise.HomeGroundId is { } gid && world.Grounds.TryGetValue(gid, out var g2) ? g2 : null;

        // Meeting-driven-selection ticket (G) + corrections pass (correction 2): no hardcoded
        // template - SquadNeeds.Groups() TargetDepth stays the legality/depth FLOOR, and this ONE
        // combined per-franchise emphasis (archetype + cultural identity + home-ground conditions +
        // last-campaign diagnosis) reshapes composition on top of it. All those signals feed the
        // same dictionary, read once here - not sequential passes. Deterministic.
        var emphasis = RoleEmphasis(franchise, home, prevSeason, fSquad);
        bool spinDeck = home is not null && home.PitchSpinRating > home.PitchPaceRating + 8;
        bool youthBuilder = franchise.FranchiseArchetype == FranchiseArchetype.YouthBuilder;

        // How many of each role group the franchise still wants, and a rough budget split.
        foreach (var g in SquadNeeds.Groups())
        {
            int have = fSquad.Count(g.Matches);
            int floorWant = Math.Max(0, g.TargetDepth - have);
            double m = emphasis.GetValueOrDefault(g.Label, 1.0);
            int want = floorWant == 0 && m > 1.2 && have < g.TargetDepth + 1
                ? 1                                                  // a strong emphasis can add ONE target beyond the floor
                : (int)Math.Round(floorWant * m);
            plan.TargetsByRole[g.Label] = Math.Clamp(want, 0, g.TargetDepth + 1);
        }
        int totalWant = Math.Max(1, plan.TargetsByRole.Values.Sum());
        foreach (var (label, want) in plan.TargetsByRole)
        {
            // The conditions tilt is now folded into `emphasis` above (correction 2) - one read,
            // not a second ad-hoc pass here.
            double weight = want / (double)totalWant * emphasis.GetValueOrDefault(label, 1.0);
            plan.BudgetByRole[label] = purse * 0.85 * weight;
        }

        // Prioritised targets: the shortlisted players who fill a wanted role, best-first, capped
        // per role so the plan is realistic.
        foreach (var g in SquadNeeds.Groups())
        {
            int want = plan.TargetsByRole.GetValueOrDefault(g.Label, 0);
            if (want == 0) continue;
            var picks = shortlist
                .Where(p => g.Matches(p) && (plan.OverseasSlotsLeft > 0 || !IsOverseas(p, competition.Country)))
                .OrderByDescending(p => interest.GetValueOrDefault(p.Id) * 10 + SquadNeeds.OverallScore(p)
                    // Correction 2: on a turning home deck, a batter who genuinely plays spin is
                    // worth targeting ahead of a bigger name who does not; a YouthBuilder weighs a
                    // young player's ceiling as a real, minor factor (a long-term investment pick).
                    + (spinDeck && g.Label.Contains("batting") ? (AbilityScale.AttributeToHundred(p.Batting.AgainstSpin) - 50) * 0.25 : 0)
                    + (youthBuilder && p.Age(date) <= 23 ? (AbilityScale.CompositeAbilityToHundred(p.PotentialAbility) - AbilityScale.CompositeAbilityToHundred(p.CurrentAbility)) * 0.20 : 0))
                .Take(want + 2)
                .ToList();
            for (int i = 0; i < picks.Count; i++)
            {
                var p = picks[i];
                double roleBudget = plan.BudgetByRole.GetValueOrDefault(g.Label, purse * 0.2);
                double ceiling = Math.Min(roleBudget * (i == 0 ? 0.6 : i == 1 ? 0.35 : 0.2),
                    _valuation.EstimateValue(p, null, date, world.MarketIndex) * 0.42);
                ceiling = Math.Max(ceiling, basePriceById.GetValueOrDefault(p.Id, croreEquiv) * 1.2);
                plan.Targets.Add(new FranchiseAuctionPlan.PlanTarget(p.Id, g.Label, Math.Min(i, 2), Math.Round(ceiling, 0), IsOverseas(p, competition.Country)));
            }
        }

        return plan;
    }

    // ---------------- economics ----------------

    /// <summary>The strict bid increment for a given current price, in game currency (from the IPL staircase, scaled by the crore-equivalent unit).</summary>
    public static double Increment(double price, double croreEquiv)
    {
        double c = price / croreEquiv;
        double stepCr = c < 1 ? 0.05 : c < 2 ? 0.10 : c < 5 ? 0.20 : 0.25;
        return Math.Round(stepCr * croreEquiv, 0);
    }

    private static readonly double[] CappedLadder = { 0.75, 1.0, 1.25, 1.5, 2.0 };   // crore-equiv
    private static readonly double[] UncappedLadder = { 0.30, 0.40, 0.50 };

    private static double SelfSelectBasePrice(Player p, double croreEquiv, DateOnly date, Random random)
    {
        bool capped = FranchiseRetentionService.IsCapped(p);
        var ladder = capped ? CappedLadder : UncappedLadder;
        double quality = SquadNeeds.OverallScore(p);

        // Where on the ladder his profile places him.
        int idx = capped
            ? (quality >= 82 ? 4 : quality >= 72 ? 3 : quality >= 62 ? 2 : quality >= 52 ? 1 : 0)
            : (quality >= 55 ? 2 : quality >= 45 ? 1 : 0);

        // A fading player - poor recent form, or an ageing name whose game has tailed off -
        // self-selects a lower bracket out of fear of going unsold (the Iyer-picks-1cr-not-2cr
        // scenario). Confidence stands in for "reputation trend": it sags as a career winds down.
        bool fading = p.Form.CurrentForm < -15
            || (p.Age(date) >= 33 && p.Form.CurrentForm < 8)
            || p.Form.Confidence < 35;
        if (p.Form.CurrentForm < -25) idx = Math.Max(0, idx - 2);
        else if (fading) idx = Math.Max(0, idx - 1);

        return Math.Round(ladder[idx] * croreEquiv, 0);
    }

    // Corrections pass (correction 1): the real IPL role order within each round is
    // batter -> all-rounder -> wicketkeeper -> fast bowler -> spinner (verified against the
    // IPL 2025 mega auction). A full capped round, then a full uncapped round, then accelerated.
    private static IEnumerable<AuctionSet> OrderedSets() => new[]
    {
        AuctionSet.Marquee,
        AuctionSet.CappedBatter, AuctionSet.CappedAllrounder, AuctionSet.CappedWicketkeeper, AuctionSet.CappedPace, AuctionSet.CappedSpin,
        AuctionSet.UncappedBatter, AuctionSet.UncappedAllrounder, AuctionSet.UncappedWicketkeeper, AuctionSet.UncappedPace, AuctionSet.UncappedSpin,
    };

    private static AuctionSet ClassifySet(Player p)
    {
        bool capped = FranchiseRetentionService.IsCapped(p);
        if (capped && SquadNeeds.OverallScore(p) >= 80) return AuctionSet.Marquee;
        bool spin = BallOutcomeModel.IsSpinner(p);
        return (p.PrimaryRole, capped) switch
        {
            (PlayerRole.WicketKeeper, true) => AuctionSet.CappedWicketkeeper,
            (PlayerRole.WicketKeeper, false) => AuctionSet.UncappedWicketkeeper,
            (PlayerRole.BattingAllrounder or PlayerRole.BowlingAllrounder, true) => AuctionSet.CappedAllrounder,
            (PlayerRole.BattingAllrounder or PlayerRole.BowlingAllrounder, false) => AuctionSet.UncappedAllrounder,
            (PlayerRole.Bowler, true) => spin ? AuctionSet.CappedSpin : AuctionSet.CappedPace,
            (PlayerRole.Bowler, false) => spin ? AuctionSet.UncappedSpin : AuctionSet.UncappedPace,
            (_, true) => AuctionSet.CappedBatter,
            _ => AuctionSet.UncappedBatter,
        };
    }

    private static string RoleGroupOf(Player p)
    {
        foreach (var g in SquadNeeds.Groups())
            if (g.Matches(p)) return g.Label;
        return "middle-order batting";
    }

    private static bool IsOverseas(Player p, string hostNation) =>
        !string.Equals(p.Nationality, hostNation, StringComparison.OrdinalIgnoreCase);

    private static bool HasInCountryRecord(Player p, string country) =>
        string.Equals(p.Nationality, country, StringComparison.OrdinalIgnoreCase)
        || p.Experience.InternationalMatches >= 30; // a well-travelled international has played most conditions
}
