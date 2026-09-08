# Meeting-Driven Selection/Auction, Franchise Identity Evolution, Cleanup

> **STATUS: COMPLETE (both stages), with requirement I's live wiring DEFERRED.** Stage 1 (H + J
> removals, A selection-panel restructure + the Outvoted-consequences fold-in) and Stage 2 (C
> first-hand knowledge, D/E/F auction meetings + needs-priority bidding, G no-fixed-templates, B
> franchise-identity evolution, I the generic N-tier promotion/relegation generator, + the
> standing-status digest). 673/673 tests, two consecutive clean full-suite runs, 0 build warnings;
> all 15 "same seed, same world twice" determinism tests green.
> **Requirement I:** the generic `GenerateTieredDomesticStructure` generator is built and
> unit-tested, but **wiring it into the live seeded world was reverted** - it reproducibly
> amplified a latent non-determinism over a long multi-year sim that could not be fully
> root-caused. Deferred to Phase 17. Genuine fixes made and kept in the process:
> `JobMarketService.GatherApplications`'s `.Id`-ordered candidate loops (the pre-existing latent
> bug found in Stage 1 - now **actually fixed**, not just routed around); the `FixturePlayService`
> / `CompetitionSeasonRunner` same-day fixture sorts gained a seed-stable competition-name
> tiebreak; the generator refuses a degenerate 2-club division. B was also made fully
> deterministic (threshold + hysteresis clock, no roll). Full writeup: CLAUDE.md ->
> "MEETING-DRIVEN SELECTION TICKET" and its deferred-register rows.

## Context

This replaces the earlier "Phase 10-16 deferred items" plan (all 4 of its passes are already
shipped and verified — 661/661 tests — so that plan is stale and this overwrites it).

The new ask, in the user's own framing: national/franchise decision-making in this engine is
currently either a silent computation (the auction, the national pool) or a single generated
report (`SelectionMeetingReport`) — never a "people in a room, arguing" event with real stakes.
Two systems the user explicitly wants gone (Director of Cricket, Impact Player) turn out, on
inspection, to be cheap and clean to remove — one is a pure side-effect bundle nothing else reads,
the other was already half-dead (superseded by a different mechanism a session ago but never
deleted). And one flagged gap (a 4th domestic tier) turns out to expose a real, pre-existing
problem worth fixing properly rather than compounding: the "3-tier promotion/relegation system"
CLAUDE.md already claims as done is **only ever exercised by tests** — `GenerateThreeTierDomesticStructure`
is never called from the actual seeded world. The user has confirmed (via the two clarifying
questions asked this session): (1) wire the real tiered structure into the seeded world rather
than reproduce the same test-only disconnect at 4 tiers, and (2) pace this as two stages — build
and verify the two removals plus the selection-panel restructure first, stop and report, then
tackle the larger auction-meeting / franchise-identity work as a second stage.

Research performed this session (web search + direct codebase reads, not just the Phase 16 review
doc's own claims — see "Research findings" below) confirms every major design call below against
how real cricket administration actually works, and explicitly departs from strict reality where
it wouldn't serve the game (documented per-item below, not silently).

## Research findings (grounding for the design decisions below)

- **Selection committees**: BCCI-style panels (chief selector + selectors) are advisory; the
  coach and captain attend as ex-officio members (coach for performance insight, captain
  sometimes with a tie-break) — the panel does not have independent binding authority over them.
  This directly validates requirement A's "staff under the coach, never a body with independent
  authority."
- **Squad announcement cadence**: real national squads are announced **per series/tour** (often
  bundling adjacent series into one announcement), not on a separate "build the pool" cadence —
  the "pool" is an emergent product of central contracts + repeated selection, not a separately
  staged real-world meeting. The ticket's two distinct cadences (annual/pre-tournament pool
  meeting vs. discretionary per-series meeting) is a **deliberate, worthwhile game-design
  simplification** of this, not a literal transcription — flagged as such, not silently accepted.
  It maps cleanly onto what the engine already does (`NationalPoolService.ReviewPool` already
  runs annually) plus a genuinely new discretionary layer on top of the existing per-series
  `SelectionMeetingService.Hold`.
- **EOI → auction list**: real IPL practice is exactly the "franchises vote on who converts"
  shape requested in E — the league prunes an initial registrant list (e.g. 1,122 → 578) based on
  which players franchises actually indicated interest in. This maps directly onto
  `FranchiseAuctionService`'s **already-existing** all-franchises `InterestVote` step that builds
  today's shortlist — it just needs a meeting/dialogue wrapper, not a new computation.
- **Pre-auction planning + retention/RTM**: real franchises hold genuine pre-auction strategy
  meetings covering retention, RTM, and target lists — validates D directly, and the existing
  `FranchiseAuctionPlan`/`FranchiseRetentionService` are already the computed substrate.
  Financial directors explicitly warn against over-retention/over-RTM-reliance — a real, quotable
  tension a meeting event can surface as dialogue.
- **First-hand knowledge**: real captains/coaches personally recommend players they've watched
  (a cited real example: a domestic captain recommending a player who then earns a trial) and
  scouts vs. coach/captain "gut feel" genuinely conflict in real recruitment — validates C. There
  is no existing engine mechanism for "person X has personally watched player Y" (the closest,
  `Player.Matchups`, is player-perspective, not third-party-familiarity) — this needs new state.
- **Bidding sequencing — corrected per the user's own review of this plan.** Real overseas-slot
  bidding is "strategic" given scarcity, but there is no universal "always bid overseas first"
  law, and the user explicitly rejected building it as an overseas-specific rule. The real
  principle, and the one actually worth building: **a franchise secures whichever players it most
  needs first — overseas or domestic, whichever the plan ranks highest priority** — not a
  nationality-based ordering. `FranchiseAuctionPlan.Targets` already carries a `PriorityTier` per
  target regardless of `Overseas`; the fix is to sequence bidding by that existing priority
  ranking, not to add a separate overseas-first carve-out.
- **Squad composition**: real recruitment literature confirms role composition is genuinely
  philosophy/data driven per franchise (powerplay pacer vs. death specialist vs. anchor debates
  are real, live disagreements in the profession) — validates G's core claim; see the "no fixed
  templates" resolution below for how this composes with the engine's existing generic
  `SquadNeeds.Groups()` baseline rather than replacing it.

Sources: [ESPNcricinfo — BCCI selection committee](https://www.espncricinfo.com/story/bcci-selection-committee-chetan-sharma-ss-das-salil-ankola-subroto-banerjee-s-sharath-1352651), [BCCI — National Selector applications](https://www.bcci.tv/articles/2025/news/55556251/bcci-invites-applications-for-national-selector-positions), [Outlook — India T20 WC squad announcement](https://www.outlookindia.com/sports/cricket/india-squad-announcement-icc-t20-world-cup-2026-preview-live-streaming-when-where-to-watch-press-conference), [ESPNcricinfo — IPL 2026 auction FAQs](https://www.espncricinfo.com/story/ipl-auction-2026-faqs-when-where-who-and-how-much-1515453), [Gulf News — eight overseas players top bracket](https://www.aljazeera.com/sports/2021/2/12/eight-overseas-players-in-highest-bracket-for-indias-ipl-auction), [Mystery Cricket — IPL Retention vs RTM](https://mysterycricket.com/blogs/cricket/ipl-retention-vs-rtm), [Sportskeeda — IPL 2025 retention/coaching buzz](https://sportskeeda.com/cricket/ipl-2025-buzz-meeting-retentions-dhoni-s-csk-future-talks-changes-coaching-staff), [Mystery Cricket — how IPL teams scout talent](https://mysterycricket.com/blogs/cricket/how-do-ipl-teams-scout-talent), [ESPN Africa — IPL 2024 uncapped picks](https://africa.espn.com/cricket/story/_/id/39123180/ipl-2024-auction-musheer-arshin-kulkarni-shubham-dubey-sameer-rizvi-kushagra-watch-for).

## Codebase verification (direct reads + two Explore agents this session — not the review doc's claims)

Confirmed by quoting the actual current code (file:line references below are exact, verified this
session):

- `SelectionMeetingService.Hold` (`Services/SelectionMeetingService.cs`) **already has** a full
  per-seat panel vote (`Outvoted`, `VotesForChosen`, `PanelSize` on `SelectionMeetingReport`) —
  this is the "meeting report" template every new meeting type below should mirror: a `*Quality`
  scalar driving both content depth and a real dissent/outvoted mechanic, a headline-priority
  chain, named optional fields rather than a free-text blob. `PanelQuality(Team, Coach?)` is
  already `static` and already reused by `AiClubManagementService.PickSquad` to apply a real bias
  when the panel is weak — the coupling this ticket wants ("panel discusses, coach/captain decide,
  a weak panel is visibly worse") is *already real*, not aspirational.
- `DelegationProfile`/`DecisionArea` (`ValueObjects/DelegationProfile.cs`, `Enums/Enums.cs:733-748`)
  is the existing 3-state (`DoItMyself`/`Consult`/`Delegate`) authority model — `StaffHiring` is
  already a `DecisionArea`, so "the coach signs/retains/replaces selectors like any other staff"
  needs **no new authority mechanism**, just a new `StaffRole`.
- `NationalPoolService`/`NationalSelectionService` (`Services/NationalPoolService.cs`) already run
  the "build the pool" logic **annually** (`WorldClockService.cs:1454`, inside `ProcessAnnualRollover`'s
  national-team loop) — coverage-slot-driven (`CoverageSlot`, 11 slots), never position-counted.
  `SelectionPanelService.ApplyPanelInfluence` already runs right after it (line 1460) and already
  models a weak/politicised panel distorting the pool.
- `StaffMember`/`StaffRole` (`Entities/StaffMember.cs`, `Enums/Enums.cs:1163-1195`) is the existing
  "one shared attribute set, weighted per role" pattern (`WeightFor` switch, `Blend` helper) — the
  exact template a `Selector`/`ChiefSelector` role should extend, not duplicate.
- **`DirectorOfCricket` — every reference in `src`, confirmed by grep**: `Team.DirectorOfCricketStaffId`
  (`Entities/Team.cs:144`), `StaffRole.DirectorOfCricket` (`Enums.cs:1194`),
  `GameEventType.DirectorOfCricketAppointed` (`Enums.cs:602`, mapped in `NewsEngine.cs:153`), and
  the **single** install site `BoardRelationshipService.cs:101-127`. Critically:
  `Team.DirectorOfCricketStaffId` is **never read anywhere except its own set/null-guard** in that
  one method — its stated effect ("takes squad selection and transfer authority") is achieved
  entirely through *already-generic* mechanisms it also sets in the same block
  (`DelegatedResponsibilities`, `Delegation.Set(...)`, `StaffingDelegatedToStaffId`, a
  `coach.Authority` nudge). **Removal is a clean, surgical deletion of one block + 3 declarations
  + 1 enum value + 1 news mapping — nothing else in the codebase depends on it.** No test
  references it by name (confirmed by grep across `tests/`).
- **`ImpactPlayer` — every reference in `src`, confirmed by grep**: `Competition.UsesImpactPlayer`
  (`Entities/Competition.cs:100`), the seed flag (`WorldSeeder.cs:1195`), the real mechanism
  `FixturePlayService.WithImpactPlayer` (`FixturePlayService.cs:225-235, 390-419` — inserts a real
  12th roster player into the batting order/attack), and `MatchSetup.ImpactPlayerEdge`
  (`MatchSimulator.cs:65,217`) which is **already dead** — its own doc comment says it was
  "replaced" by `WithImpactPlayer`, and grep confirms it is set nowhere, so it is permanently 0.
  Two tests reference it by name (`Program.cs:16540-16542, 17851, 17859`) and must be
  updated/removed alongside.
- **4th domestic tier**: `CompetitionSeasonRunner.BuildChain`/`TryResolvePromotionRelegation`
  (`Services/CompetitionSeasonRunner.cs:337-403`) already walk an **arbitrary-length**
  `SecondTierCompetitionId` chain generically — no code change needed there for a 4th tier.
  `WorldSeeder.GenerateThreeTierDomesticStructure` (`Data/Seeding/WorldSeeder.cs:400-450`) is
  **hardcoded to exactly 3** (a fixed 3-element `tiers`/`names` array, `for (i<3)` loops) and is
  **never called from `GenerateStarterWorld`/`GenerateInternationalWorld`** — only from tests.
  Confirmed by grep: the only callers of both `GenerateTwoTierDomesticStructure` and
  `GenerateThreeTierDomesticStructure` repo-wide are in `tests/CricketManager.Tests/Program.cs`.
- `Team.FranchiseArchetype` (`Entities/Team.cs:189`) is set **once, at seed time, by slot index**
  (`WorldSeeder.cs:1147-1153`, `i % 4` round-robin) and **never re-evaluated afterward** — the only
  other file touching it is `FranchiseAuctionService.cs` (read-only, the bidding-ceiling switch at
  lines 371-387). `CoachCareerService.GrowFromMilestone` (`CoachCareerService.cs:500-533`) already
  has the exact drift *pattern* to extend (a coach's `Philosophy` drifts toward `Team.CulturalIdentity`
  after 3+ good seasons) — `Team.CulturalIdentity` is itself a `CoachingPhilosophy` value
  (`Team.cs:129`), a genuinely different type from `FranchiseArchetype` (an auction-bidding-only
  enum), so identity evolution needs its own drift function, not a literal reuse of the coach
  one — but the same shape (slow, gated on tenure/results, capped).
- `Team.DynastyRating`/`Team.PlayerTradingPnL` (`Entities/Team.cs:196,203`) are already real,
  already computed (`FranchiseAuctionService.cs:277-294`, `TransferMarketService.cs:345-346`) —
  exactly the two signals the ticket names for identity drift; no new tracked state needed for
  the *results/history* half of requirement B.
- `FranchiseAuctionService.RunLot`'s `Ceiling(Team f)` (`FranchiseAuctionService.cs:319-463`) is
  the exact spot the archetype ceiling switch, purse-pressure (`ApplyPursePressure`, already
  extracted as `internal static` and unit-tested), and `ScoutingDeptQuality` (a **private static
  method living inside `FranchiseAuctionService` itself**, lines 490-502 — duplicates a concept
  `ScoutingAccuracyService` already models elsewhere for a different purpose, worth noting but not
  worth merging given the two serve genuinely different jobs) all already live — this is where
  the first-hand-knowledge modifier (C) plugs in.
- **The Phase-9-flagged linear-lookup issue**: `FranchiseAuctionService` itself is clean (a single
  `playersById` dictionary built once, confirmed by full-file grep) — but
  `FranchiseRetentionService.cs:62-67` has a real, still-present `world.Players.FirstOrDefault(...)`
  inside a per-squad-member `.Select(...)`, i.e. `O(squadSize × |world.Players|)` per franchise,
  run once per franchise per mega auction. Smaller-magnitude than the original Phase 9 finding
  but genuinely the same anti-pattern — worth a one-line fix (reuse a dictionary) while already in
  this file for the identity/retention work.
- `SquadNeeds` (`Services/SquadNeeds.cs`, 97 lines) is a single generic helper already shared by
  transfer/free-agent/auction code — `Groups()` (6 role groups, hardcoded `TargetDepth`) is a
  **legality/depth baseline** ("do we have enough seam bowlers"), not a philosophy template. This
  distinction is the basis of the "no fixed templates" resolution below.
- `NarrativeService`/`PressConferenceService` confirmed as the two existing "real event" precedents:
  `PressConferenceService` (tone selection → stochastic execution → branching dialogue line →
  mechanical consequence, including a genuine discipline-charge downside on a botched answer) is
  the strongest existing model for "genuine dialogue with real stakes" and is reused conceptually
  for the auction meetings' own narrated dialogue.
- `MatchupKey`/`MatchupConfidenceService` (`Services/MatchupConfidenceService.cs`) confirmed as
  player-perspective only (`ForBowler`, `ForGround`, `ForPartner`, ...) — no existing key form
  answers "has person X watched player Y", confirming C needs new state, though it can reuse the
  `MatchupConfidence`-style rolling/decay *shape* if familiarity should fade.
- `Coach.Attributes.Scouting` (`ValueObjects/CoachAttributes.cs:31`) already exists and is
  currently **unused anywhere in `src`** (confirmed unused this session) — this is the natural
  attribute to drive a coach's own first-hand-knowledge weighting in C, closing a second
  previously-dead attribute in the same pass as the DoC/Impact-Player cleanup.
- `IsOverseas(Player, string hostNation)` (`FranchiseAuctionService.cs:652-653`, a plain
  `string.Equals` on `Nationality`) is the established pattern for any nationality-pair
  comparison — the "foreign coach + domestic captain" check in C reuses this exact idiom, not a
  new mechanism.

## Conflicts found, and the recommended resolution for each

1. **G ("no fixed squad-composition templates") vs. the existing `SquadNeeds.Groups()`.** Removing
   `SquadNeeds.Groups()` outright would be a large, risky rewrite touching every consumer
   (transfer market, free-agent market, franchise auction) that currently leans on it for a
   *legality/depth* floor (never fielding zero keepers, never buying 11 batters). **Resolution:
   keep `SquadNeeds.Groups()` exactly as-is** (it answers "can this XI legally function", not "what
   does this coach believe in") **and add a new, archetype/philosophy-driven role-EMPHASIS layer on
   top**, read by the new pre-auction-plan/EOI-conversion meetings (D/E) and by
   `FranchiseAuctionPlan.BuildPlan`: a coach who thinks anchors are obsolete overweights
   aggressive-top-order/finisher-trait candidates at equal `SquadNeeds.OverallScore`; a coach who
   wants two frontline spinners overweights that role beyond the baseline `TargetDepth`. This is
   the real distinction the ticket is actually drawing (composition PREFERENCE vs. composition
   LEGALITY), and it is additive, not a rewrite.
2. **A's "at the head coach's own discretion" for per-series meetings** cannot be literal free
   will pre-Phase-17 (no interactive decision loop exists yet — every other coach-facing choice in
   this engine is already "auto-pick unless a stored `ManagerPreferences` override exists", per
   `ManagerPreferences`'s own doc comment). **Resolution: model discretion as (a) a genuine
   probabilistic AI trigger** — a thorough coach (`Coach.Attributes.WorkEthic`/`MatchPreparation`)
   calls a meeting more often, and ANY coach calls one when the squad genuinely needs revisiting
   (an injury/drop/big form swing since the last one), never on an unchanged squad — **and (b) a
   new `ManagerPreferences` flag for a human coach** (default: hold one, matching the project's
   standing "AI behaviour is the default until the human overrides it" policy), so the mechanism
   is real today and becomes a literal choice the moment a UI exists. Flagged, not silently
   reinterpreted.
3. **The 4th-tier scope question** (resolved by the user this session): wire the generalized
   tier generator into the real seeded world rather than reproduce the existing test-only
   disconnect. This is more invasive than the ticket's literal wording implied — touches
   `WorldSeeder.GenerateStarterWorld`/`GenerateInternationalWorld` and any test asserting today's
   flat single-division domestic structure — but is the only way item I means anything in an
   actual playthrough, per the user's explicit choice.
4. **`ScoutingDeptQuality` (auction name-hype discount) duplicates `ScoutingAccuracyService`'s
   concept** (a noisy potential-ability estimate) without reusing it. Not touched by this ticket's
   requirements directly, but the new first-hand-knowledge modifier (C) sits right next to it in
   the same `Ceiling(Team f)` method — **resolution: keep them as two distinct, clearly-named
   factors** (`scoutDiscount` = department quality discounting name-hype; the new
   `firstHandConfidence` = a specific person's own familiarity) rather than conflating them, since
   they answer genuinely different questions (departmental competence vs. a specific individual's
   direct exposure).

## What's added / modified / deleted, per requirement

**H — Remove Director of Cricket (full deletion, no replacement authority layer).**
Delete: `BoardRelationshipService.cs:101-127` (the whole install block), `Team.DirectorOfCricketStaffId`,
`StaffRole.DirectorOfCricket`, `GameEventType.DirectorOfCricketAppointed` + its `NewsEngine.cs:153`
mapping. Nothing else references any of these (confirmed).

**J — Remove Impact Player (full deletion).**
Delete: `Competition.UsesImpactPlayer`, the `WorldSeeder.cs:1195` seed flag (and whatever `lg.Impact`
tuple field feeds it, cleaned up in the same seed-table literal), `FixturePlayService.WithImpactPlayer`
+ its two call sites (`FixturePlayService.cs:231-235`), and the already-dead
`MatchSetup.ImpactPlayerEdge` + `MatchSimulator.cs:217`'s `batImpact` read. Update/remove the two
tests that reference it by name.

**A — Selection panel restructure (staff, not authority).**
New: `StaffRole.ChiefSelector`, `StaffRole.Selector` (national-team-scoped) with a `WeightFor` blend
in `StaffMember.cs` on `Analysis`/`Statistics`/`Diligence`/`Communication` (mirrors the existing
Scout/Analyst pattern, not a new formula shape); gated into `JobMarketService.AdvertisedRoles` for
`team.IsNational` only. A new **`NationalPoolMeetingService`** (sibling to `SelectionMeetingService`,
reusing its report-record/dissent/vote conventions) wraps the existing `NationalPoolService.BuildPool`/
`ReviewPool` with a genuine meeting narrative — attendees, what gaps were found, panel-quality-driven
rationale depth — triggered on the existing annual cadence (`WorldClockService.cs:1454`) **or** once
ahead of a major tournament window (reusing the existing "major event" detection
`NationalBoardVerdictService` already has). The existing per-series `SelectionMeetingService.Hold`
gets a new discretion gate per Conflict #2 above (a `ShouldHoldMeeting` check before
`AiClubManagementService.AnnounceSquads` calls it, plus a new `ManagerPreferences.HoldSelectionMeetings`-
style flag) — not skipped by default, but genuinely skippable and genuinely conditioned on squad
change + coach thoroughness rather than firing unconditionally every cadence as today.
Final authority is unchanged — `SelectionMeetingService`'s existing coach/captain-decides,
panel-can-be-outvoted-but-not-binding shape already matches A exactly; no rework needed there.

**Suggestion fold-in (from the Phase 16 review, requested alongside A):** wire `Outvoted` into real
consequences — a national board's trust in its chairman of selectors nudges down on a repeated
outvote, the outvoted player's own `Form.Confidence`/morale takes a small hit, and a genuinely
repeated pattern raises a new `NarrativeService` storyline kind ("selectors at war"). Reuses
`NarrativeService`'s existing `Storyline`/`Reinforce` shape, not a new tracker.

**Everything below is Stage 2 (planned in full now, built after Stage 1 is reviewed):**

**D — Pre-auction planning meeting.** A new textual meeting event wrapping the existing
`FranchiseAuctionPlan`/`FranchiseAuctionService.BuildPlan` computation: reviews last season
(`Team.DynastyRating`, `PlayerTradingPnL`, final league position), narrates strengths/weaknesses,
and produces the same `plan.Targets`/`TargetsByRole`/release-list output the auction already
consumes — the meeting explains and (for a human franchise) surfaces the plan as a
`StaffRecommendation`, it does not replace `BuildPlan`'s math.

**E — EOI → auction list conversion meeting.** Wraps the *existing* all-franchises `InterestVote`
shortlist step (`FranchiseAuctionService.cs:145-192`) in a genuine multi-franchise dialogue/vote
narrative (which players got real interest, who was borderline and voted in/out) rather than a
silent filter — same underlying computation, a meeting wrapper and a narrated report around it.

**F — Post-auction squad review + needs-priority bidding order (corrected).** After
`RunAuctionDetailed` completes, each franchise reviews its picks (extends
`FranchiseAuctionMediaService.Report`, or a sibling method) and derives a likely XI via the
existing `XiSelectionService`. Separately, inside `RunLot`'s set-ordering (the sequence lots are
auctioned in), promote a franchise's genuinely highest-priority targets earlier in the bidding
order — using the **existing** `FranchiseAuctionPlan.Targets`' `PriorityTier` ranking as-is,
**regardless of `Overseas`** — rather than an overseas-specific carve-out. A must-have domestic
gap (say, the only genuine wicketkeeper on the plan) is sequenced exactly as early as a must-have
overseas strike bowler when both carry the plan's top priority tier.

**G — No fixed squad-composition templates.** Per Conflict #1: `SquadNeeds.Groups()` stays as the
legality/depth floor; a new role-emphasis weighting (archetype + `CulturalIdentity`/philosophy-
driven) is read by `BuildPlan`/the pre-auction meeting (D) on top of it.

**B — Franchise identity evolution.** `Team.FranchiseArchetype` becomes re-evaluatable (not just
seed-time): a new drift function (its own, not a reuse of `CoachCareerService.GrowFromMilestone`,
since `FranchiseArchetype` and `CoachingPhilosophy` are different types) reads `DynastyRating` +
`PlayerTradingPnL` trend over time **and** a new coach/captain "discuss direction" event fired on
a coaching change for a franchise — genuinely shifting `FranchiseArchetype` when the new coach's
own philosophy and the captain's read of the squad disagree with the current archetype. Same
slow/gated/capped shape as the existing coach-philosophy drift, applied to a different field.

**C — First-hand knowledge in auction bidding.** New state (not a `Player.Matchups` reuse, per
Conflict/verification above): a small "who has personally watched whom" record on `Coach`/
`StaffMember`/captain (built up from shared time at a club — team-mate history for a captain,
`TeamId` history for a coach/staff member), read as a *confidence/ceiling* modifier inside
`FranchiseAuctionService.RunLot`'s `Ceiling(Team f)`, gated off for a genuinely reputation-known
player (`Player.Reputation.Worldwide` above a threshold — "everyone knows him already"). Reuses
`Coach.Attributes.Scouting` (confirmed currently unused anywhere) to scale how much a coach's own
read is trusted, and the existing `IsOverseas`-style nationality-string-compare idiom for the
cross-nationality captain/coach case explicitly named in the ticket.

**I — 4th domestic tier, wired into the real seeded world.** Generalize
`GenerateThreeTierDomesticStructure` into a single N-tier generator (not a 4th hardcoded copy),
call it with 4 from both `GenerateStarterWorld` and `GenerateInternationalWorld` in place of
whatever flat/2-tier domestic structure they build today, and update any test that currently
asserts a flat single-division domestic competition per country. `CompetitionSeasonRunner`
needs no change (already generic over chain length).

## Slice order

**Stage 1 (build + test now, then stop and report):**
1. Remove Director of Cricket (H) — pure deletion, lowest risk, do first.
2. Remove Impact Player (J) — pure deletion, do second.
3. Selection panel restructure (A) — new `Selector`/`ChiefSelector` `StaffRole`, `NationalPoolMeetingService`,
   the per-series discretion gate, plus the `Outvoted`-consequences fold-in suggestion.

**Stage 2 (planned above, executed after Stage 1 is reviewed):**
4. First-hand knowledge (C) — new state is small and self-contained; do before the auction
   meetings so D/E/F can read it if useful.
5. Pre-auction planning meeting (D) + EOI→auction-list conversion meeting (E) — both wrap existing
   `FranchiseAuctionService` computations, natural to build together.
6. Post-auction squad review + needs-priority (not overseas-specific) bidding order (F).
7. No fixed squad templates (G) — the role-emphasis layer D/the auction plan will read.
8. Franchise identity evolution (B) — depends on nothing above, but sequenced after the auction
   work since it's read by the same `FranchiseAuctionService.Ceiling` method as C.
9. 4th domestic tier wired into the real seeded world (I) — fully independent of 1-8, done last.
10. Standing-status digest fold-in suggestion (a periodic "what does my squad/panel/board think"
    digest, distinct from the weekly news digest) — a small presentation addition, done last.

## Verification

Every slice: `dotnet build CricketManager.sln -c Release` (0 warnings expected), new
service-level tests for each new mechanic (direct tests for deterministic pieces, paired
statistical tests at a real sample size for anything probability-driven, following this
codebase's own established discipline), then the full suite via
`dotnet run --project tests/CricketManager.Tests -c Release --no-build` **twice consecutively**
before considering a slice done. `CLAUDE.md`'s phase-status table and deferred-items register are
updated at the end of each stage, the same way every prior phase in this project was closed out.
