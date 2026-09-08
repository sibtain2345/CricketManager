# Meeting-Driven Selection ticket — CORRECTIONS pass

> **STATUS: COMPLETE.** All 6 logic slices built, then the full test batch. **680/680, four
> consecutive clean full-suite runs, 0 build warnings.** (673 baseline + 7 net new corrections
> tests; 3 pre-existing tests rewritten for Correction 3's deliberate year-round-franchise-coach
> behaviour change + the `ReadStrength` signature.) `FranchiseAuctionService.NoteLostMustHave`
> promoted `internal static` for a direct test. One transient "Phase 15/16 integration"
> determinism blip in the first run did not reproduce across 3 isolated + 1 group + 4 full runs;
> the name-ordered campaign-assistant pick was hardened anyway. Full writeup: `CLAUDE.md` ->
> "MEETING-DRIVEN SELECTION TICKET - CORRECTIONS PASS". README + phase-status table updated.

Four corrections + one bug fix, all against the just-completed Meeting-Driven Selection ticket
(and one earlier assumption — Post-Phase-7/8/9 Section H — that research shows was wrong).

---

## Research findings (all verified against real sources this pass)

1. **IPL auction set order is fixed and role-driven, NOT priority-driven.** IPL 2025 mega auction:
   two marquee sets (6 players each) → a full round of **capped** players by role in the order
   **batter, all-rounder, wicketkeeper, fast bowler, spinner** → the same full round for
   **uncapped** → an accelerated phase for everyone still unsold. No franchise's own preference
   moves a player earlier. A role can have multiple numbered sub-sets ("Batter Set 1/2") when
   there are many players of that role, but the *overall* structure is one capped round, then one
   uncapped round. Sources: [ESPNcricinfo — IPL auction 2025 marquee sets](https://www.espn.com/cricket/story/_/id/42263362/ipl-auction-2025-date-everything-need-know-espncricinfo), [Outlook — IPL 2025 mega auction procedure](https://www.outlookindia.com/sports/cricket/ipl-2025-procedure-marquee-players-retention-list-rules-all-you-need-to-know-about-the-mega-auction).

2. **Franchises build squads conditions-first, on the record.** RCB coach Simon Katich: "when at
   the auction… we were going to pick a squad well-suited to the Chinnaswamy Stadium, where we
   play seven [home] games" — a batting paradise, so RCB targeted accordingly; RCB also runs a
   real *mock auction* beforehand. CSK built around Chepauk (slow, spin) for ~two decades. A
   venue's tactical answer also evolves as a coaching staff's read matures. Sources: [Gulf News — RCB team picked keeping Chinnaswamy in mind](https://gulfnews.com/sport/cricket/ipl/ipl-2020-in-uae-rcb-team-was-picked-keeping-chinnaswamy-in-mind-says-coach-katich-1.1604825341649), [ESPNcricinfo — RCB, pace alone not the answer at Chinnaswamy](https://africa.espn.com/cricket/story/_/id/40200541/ipl-2024-rcb-andy-flower-pace-alone-not-answer-chinnaswamy), [CricTracker — Hesson/Katich mock auction](https://www.crictracker.com/mike-hesson-simon-katich-reveal-how-rcb-had-conducted-a-mock-auction-for-before-buying-aaron-finch/).

3. **Franchise-league pitch prep is centralised / local-curator-driven — the home franchise does
   NOT dictate character.** IPL 2025-26: BCCI appoints a central curator at every venue
   *specifically to keep franchises out of pitch prep*, targets uniform flat 220+-scoring pitches
   (minimal pace/spin help, uniform grass), 77m max boundaries, playoffs/final under full central
   control — "home advantage has reduced significantly". BBL: CA calls national centralisation
   "inconceivable" (conditions vary too much state-to-state) so prep stays with each venue's local
   curator, not the home franchise. PSL: franchises share ~2-4 venues per season with no fixed
   home ground, so the curator prepares for whoever plays that day. CPL/SA20 not directly
   researched — the three-league pattern makes "no home-franchise pitch lever" the sensible
   default. Sources: [CricketAddictor — BCCI batting-friendly pitch order for IPL](https://cricketaddictor.com/cricket-news/bad-news-for-jasprit-bumrah-bowlers-bcci-issues-strict-batting-friendly-pitches-order-for-ipl-447822/), [Yahoo Sports — why IPL teams feel home advantage has vanished](https://sports.yahoo.com/articles/why-ipl-teams-feel-home-083900637.html), [India.com — BCCI new guidelines IPL 2026](https://www.india.com/sports/bcci-sets-new-guidelines-for-all-franchises-ahead-of-ipl-2026-season-8342499/).

4. **Bilateral / domestic cricket DOES allow dramatic home-pitch shaping — bounded by a quality
   floor, not a percentage cap.** Pakistan v England, October 2024: after a 823-run England
   innings on a Multan road, the PCB selection panel (Aqib Javed) *reused the same Multan square*,
   dried it with industrial fans + sun, then prepared a raw turner at Rawalpindi with fans and
   heaters — a batting graveyard → rank turner inside one series, and Pakistan won both remaining
   Tests (Sajid Khan + Noman Ali took 19 wickets in the decider). The ICC still rated all three
   surfaces "satisfactory". The real limit is the ICC demerit-point scale (unsatisfactory / unfit
   → demerit points → enough of them → a hosting ban), i.e. a fairness/safety floor. Sources:
   [France24 — Pakistan to re-use Multan pitch](https://www.france24.com/en/live-news/20241013-pakistan-to-re-use-multan-pitch-for-second-england-test), [ESPNcricinfo — Multan/Rawalpindi pitches "satisfactory"](https://www.espn.com/cricket/story/_/id/42245681/pak-vs-eng-test-series-multan-rawalpindi-pitches-get-satisfactory-rating-all-three-tests).

5. **Franchise head coaches are genuine multi-year, year-round employees.** Rahul Dravid joined
   Rajasthan Royals on an explicit **multi-year contract**, began immediately, and was central to
   RR's **retention AND auction strategy** ahead of a three-year cycle. He later parted ways
   despite the multi-year contract after a 9th-place finish, and was offered (and declined) a
   broader director-of-cricket-type role. Kumar Sangakkara is RR's real Director of Cricket — the
   title exists in real cricket, but that does not change the user's deliberate design choice to
   keep it out of this game. Sources: [Business Standard — Dravid joins RR on multi-year contract](https://www.business-standard.com/cricket/ipl/ipl-2025-rahul-dravid-joins-rajasthan-royals-on-multi-year-contract-124090600978_1.html), [ESPNcricinfo — Dravid parts ways with RR ahead of IPL 2026](https://www.espncricinfo.com/story/head-coach-dravid-part-ways-with-rajasthan-royals-ahead-of-ipl-2026-1500784).

---

## Codebase verification (exact refs)

- **Auction sets** — `FranchiseAuctionService.cs`:
  - `OrderedSets()` (816-821): `Marquee` → `CappedBatter, CappedWicketkeeper, CappedAllrounder, CappedPace, CappedSpin` → `UncappedBatter, UncappedWicketkeeper, UncappedAllrounder, UncappedPace, UncappedSpin`. **Structure is already right; role order has WK and Allrounder swapped vs real IPL.**
  - `RunAuctionDetailed` (228-241): the Stage-2 `mustHaveIds` term (`(mustHaveIds.Contains(p.Id) ? 100 : 0)`) in the within-set `OrderByDescending`. **This is the misreading to revert** — one term.
  - `Ceiling(Team f)` (362-441): already `PriorityTier`-aware — `target?.Ceiling` (per-target ceiling from `BuildPlan`), `roleWant <= 0 → cut`, `target is { PriorityTier: > 0 } → *0.75`. No "my must-have was lost, promote my plan-B" logic.
  - `Award` (506-526): decrements `plan.TargetsByRole` / `BudgetByRole` **only when this franchise wins**; recomputes `OnTrack`.
  - `BuildPlan` (704-778): builds `plan.Targets` best-first per role (`PriorityTier` = pick index, capped at 2), `BudgetByRole` already has a home-ground pace/spin tilt (742-751).
  - `RoleEmphasis(Team)` (644-686): archetype + `CulturalIdentity` only. Applied in `BuildPlan` to `TargetsByRole` (want) and `BudgetByRole` (weight).
  - `FirstHandKnowledgeService.ReadStrength(WorldState, Team, Player)`: `world.Players.FirstOrDefault(p => p.Id == id)` linear scan (49-51) + a `world.Coaches.FirstOrDefault` for the campaign coach. **The bug.** Called from `Ceiling` inside `RunLot`, which has `playersById` in scope.
  - `FranchiseAuctionPlan` VO: `PlanTarget(PlayerId, RoleGroup, int PriorityTier, double Ceiling, bool Overseas)`; `TargetsByRole`, `BudgetByRole`, `OnTrack`. Doc comment *claims* "a lost target falls back to a plan-B" — not actually implemented.

- **Franchise coaching** — `FranchiseCoachService.cs` (whole file, 162 lines):
  - `AppointForCampaign` (on window-open, *after* the auction in `WorldClockService.cs:1156`): sets `Coach.FranchiseCoachingTeamId` + `Team.CurrentCoachId`, credits a `campaignFee` — **no `CoachingContract` created**. `ReleaseAfterCampaign` (window-close, `WorldClockService.cs:1168`): clears both.
  - `IsAvailableFor` (140-153): national coach → never; domestic coach → only if his league window(s) don't `OverlapsMonths` the franchise window (a hard veto). No same-country carve-outs either way.
  - Franchise teams are skipped by `JobMarketService.cs:44`, `AiClubManagementService.cs:60/278`, `BoardRelationshipService.cs:81/102/139`, `WorldClockService.cs:1355` (`ProcessVacancies`).
  - `CoachingContract` entity: `StartDate/EndDate/AnnualSalary/Status/CompensationIfTerminated` + objective dicts + authority bools. `CoachJobMarketService.Hire(coach, team, date, annualSalary, contractYears=3, compensationIfTerminated=0)` → returns a contract, sets `coach.CurrentTeamId`, adds `CareerTeamIds`.
  - `RunFranchiseAuctionIfNeeded` (`WorldClockService.cs:1266`): runs `_auctionMedia.Preview` → `_auction.RunAuctionDetailed` → `_auctionMedia.Report`. `NarratePreAuctionMeeting`/`BuildPlan` run **inside** `RunAuctionDetailed` — so the coach is not in post for planning under the current order.

- **National panel meeting** — `AiClubManagementService.cs`:
  - `AnnounceSquads` (100-193): per-competition-in-naming-period. `ShouldHoldMeeting` (205-218) gate: human + `!ManagerPreferences.HoldSelectionMeetings` → skip; first squad → always; turnover ≥ 2 → always; else a thoroughness roll (`WorkEthic`/`MatchPreparation`). `SelectionMeetingService.Hold` is RNG-free.
  - `NationalPoolMeetingService.Hold` fired from `WorldClockService.cs:1538` (annual rollover, per national team) and `:1055` (`HoldPreTournamentPoolMeetings`, quarterly, 120-day `IsMajor` lookahead, deduped via `WorldState.PreTournamentPoolMeetingsHeld`).
  - `ManagerPreferences`: standing-preference store, human-team-only, default "let AI handle it"; per-instance choices are done as pre-set fields (`AlwaysInclude`/`NeverSelect`, `DelegateXiSelection`'s "future UI concern"). Press-tone / job-offer pattern: a method returns the choice, human gets a fixed sensible default, AI gets an attribute/probability pick.
  - `Competition.IsMajor => Scope == International && Prestige >= 75`. No ICC-vs-bilateral flag beyond this. `Competition.PlayingConditions`/`EffectiveConditions` VO exists (per-competition config precedent).

---

## Conflicts / decisions

1. **"Cycling repeatedly" (correction 1) vs the real IPL structure.** Research says one capped
   round then one uncapped round (sub-sets by role within), not literal repeated cycling. The
   codebase's `OrderedSets()` already has that shape. **Resolution: do NOT add cycling. Fix is
   just (a) revert the `mustHaveIds` term, (b) swap WK↔Allrounder to `batter, allrounder, wk,
   pace, spin`.** Small, contained — as the ticket anticipated.

2. **`RoleEmphasis` is a `Team`-only static; correction 2 needs `WorldState`/`Ground`/history.**
   `RoleEmphasis(Team)` can't reach the home ground or last season without a signature change.
   **Resolution: add `RoleEmphasis(Team, Ground?, CompetitionSeason? prevSeason, IReadOnlyList<Player> squad)`**
   (keep a `Team`-only overload for the existing test), fold home-conditions + previous-campaign
   weak-point + a small youth-investment lean into the same returned dictionary. `BuildPlan`
   already has `home`/`prevSeason` in scope (prevSeason is fetched in `RunAuctionDetailed` and can
   be threaded). The existing ad-hoc `BudgetByRole` pitch tilt (742-751) folds INTO `RoleEmphasis`
   so there is one combined read, not two.

3. **"Synchronised evaluation" (correction 2).** `RoleEmphasis` (role-level) and `Ceiling`
   (per-player) are already one combined multiplicative read per candidate in practice (`BuildPlan`
   sets per-role want/budget/targets from emphasis; `Ceiling` then applies archetype + scouting +
   first-hand + purse-pressure to the same player). **Resolution: make the composition explicit
   and documented — a single `RoleEmphasis` dictionary that ALL of {archetype, identity, home
   conditions, last-campaign diagnosis, youth lean} feed, consumed once in `BuildPlan`; no new
   parallel pass.** Captain input already exists (`FirstHandKnowledgeService` captain read in
   `Ceiling`); analytics/scouting already exists (`ScoutingDeptQuality`). Nothing new needed there
   beyond confirming they land on the same candidate — they do.

4. **Franchise-league pitch lever (correction 2).** `PitchDoctoringService` is multi-day-only and
   not invoked for franchise T20 at all. **Resolution: no franchise-league pitch-doctoring is
   added.** Instead: add a per-`Competition` `HomePitchInfluence` double (default set by
   `Scope`/`IsMajor` — ~0 for International/`IsMajor` and for `IsFranchiseAuctionLeague`, a real
   value for domestic first-class/bilateral). Gate the *existing* `PitchDoctoringService` /
   `PitchPreparationService` swing by this factor. A CLAUDE.md note dates the franchise finding to
   the 2025-26 season and records IPL/BBL/PSL as directly researched, CPL/SA20 as inferred.

5. **The bilateral pitch quality FLOOR (correction 2).** New: a home side can push a
   bilateral/domestic multi-day pitch hard (a larger swing than the current damped nudge), but a
   too-aggressive prep can produce a genuinely poor surface — a rare "unsatisfactory" outcome that
   costs the board demerit points + reputation + fan sentiment, and (accumulating) risks a
   hosting-credibility hit. Model as a low-probability bad-outcome branch scaled by how far past a
   safe threshold the prep went. Reuses the `DisciplineService` demerit-style consequence shape.

6. **Franchise coach as a year-round `CoachingContract` (correction 3) — the biggest change.**
   `FranchiseCoachService.AppointForCampaign`/`ReleaseAfterCampaign` become
   `EnsureCoachInPost` (hire on a real multi-year `CoachingContract` via `CoachJobMarketService`
   the first time a franchise needs one, or keep the incumbent) and `ReviewAfterCampaign` (judge
   end-of-campaign, possibly sack → immediate re-advertise, NOT wait for next window). The hire
   must run **before** `RunFranchiseAuctionIfNeeded` so the coach is in post for
   `NarratePreAuctionMeeting`/`BuildPlan`. `IsAvailableFor` rules per the ticket:
   - domestic role in *any* country → available if windows don't clash;
   - franchise-vs-domestic genuine overlap → **franchise wins** (not a veto): the coach attends
     franchise planning even during a domestic clash;
   - international coach → exclusive, **except** a franchise league hosted in his *own* country
     (a same-country carve-out check).
   Keep `Coach.FranchiseCoachingTeamId` + `Team.CurrentCoachId` wiring for the match engine.
   `campaignFee` → an annual salary on the contract (still smaller than a domestic head-coach
   salary; circuit-reputation scaling preserved).

7. **National panel meeting agency (correction 4).**
   - **Manual/early trigger:** a newly-appointed national coach (AI or human) calls a pool
     meeting on appointment (hook at the `NationalCoachAppointed` sites) + an AI probabilistic
     "call an early meeting" check on the monthly tick for a thorough coach whose last meeting is
     stale. For a human: a `ManagerPreferences.RequestPoolMeeting` flag consumed on the next tick
     (future-UI shape, like `AlwaysInclude`). All ADDITIVE to the existing annual / 120-day-major
     triggers.
   - **Per-instance skip:** `ManagerPreferences.SkipNextSelectionMeetingFor` (a `HashSet<Guid>` of
     competition ids), consumed (entry removed) by `ShouldHoldMeeting` when it would otherwise
     fire — distinct from the blanket `HoldSelectionMeetings` toggle. Same shape as
     `AlwaysInclude`/`NeverSelect`.
   - Final authority unchanged; `SelectionMeetingService` output unchanged.

8. **`FirstHandKnowledgeService.ReadStrength` bug.** Change signature to
   `ReadStrength(IReadOnlyDictionary<Guid, Player> playersById, Team franchise, Player candidate, Coach? coach)`
   — thread the `playersById` dict already built in `RunAuctionDetailed`, and pass the (now
   genuinely-in-post) coach explicitly rather than a `world.Coaches` scan. Repo-wide grep for
   `world.Players.FirstOrDefault` / `world.Players.Where(...).First...` as a closing check.

---

## Slice order

1. **Bug fix** — `FirstHandKnowledgeService.ReadStrength` signature + thread `playersById`/coach.
   Plus the repo-wide linear-lookup grep. (Smallest, unblocks nothing but cheap and isolated.)
2. **Correction 1** — revert the `mustHaveIds` term; fix `OrderedSets()` role order; add the
   "lost must-have → promote plan-B" re-rank inside `FranchiseAuctionPlan`/`Ceiling`/`RunLot`;
   make `PriorityTier` a more explicit "fight harder for THIS player" lever in `Ceiling`.
3. **Correction 2a** — `RoleEmphasis` extended (home conditions + last-campaign diagnosis +
   youth lean), one combined read, existing pitch tilt folded in; `SquadNeeds.Groups()` floor
   untouched.
4. **Correction 2b** — `Competition.HomePitchInfluence` (per-competition, `Scope`/`IsMajor`/
   `IsFranchiseAuctionLeague`-defaulted); gate `PitchDoctoringService`/`PitchPreparationService`
   swing by it; the bilateral over-prep quality-floor bad-outcome branch.
5. **Correction 3** — franchise coach → year-round `CoachingContract`; hire before the auction;
   immediate re-advertise on a sack; the availability-rule changes (same-country international
   carve-out, franchise-over-domestic priority, domestic-any-country).
6. **Correction 4** — additive manual/early national-meeting trigger + per-instance skip.
7. **Docs** — CLAUDE.md (what each correction changed and why, research cited; Section H
   correction logged honestly; DoC "excluded on purpose, not by omission" note; franchise-pitch
   finding dated 2025-26) + README + this plan file's status.

## Verification

Per slice: `dotnet build` (0 warnings) + targeted new tests (deterministic → direct; probabilistic
→ paired statistical). End of pass: full suite **twice consecutively**, clean. Determinism
discipline throughout — no new `Guid`-ordered iteration, new RNG consumers at tail positions on
their existing per-cadence streams, franchise-coach hiring must NOT reintroduce the
`JobMarketService.GatherApplications` hazard (that path is now fixed, but a national-scope-wide
candidate pool is what surfaced it — keep the franchise hire narrow / name-ordered).

## Deferred (unchanged or newly noted)

- The generic N-tier domestic pyramid's live seeded-world wiring — still deferred (Phase 17,
  determinism).
- A full interactive human decision loop for any of the per-instance choices above — Phase 17;
  the `ManagerPreferences` fields are the pre-Phase-17 shape.
- CPL/SA20 pitch-authority not directly researched — treated as "same as IPL/BBL/PSL" by pattern.
