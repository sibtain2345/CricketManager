# Full project review — honest analysis, gaps, inconsistencies, and a consolidated suggestions list

Author's note: this is a **critical review**, done at the user's request, going system by system.
Where something is genuinely good I say so; where something is missing, half-wired, inconsistent,
or overdue, I say that plainly. It **merges and supersedes** the earlier
`Research_And_Suggestions_InMatch_Auction_Media.md` (that document's 38 suggestions are folded in
below, renumbered, deduplicated, and corrected). It reads CLAUDE.md closely and cross-checks its
claims against the code.

Method: read the solution structure, `GameDataContext`, `WorldClockService`/`WorldState`,
`TacticalPlan`, `BallOutcomeModel` inputs, `InMatchTacticalAI`, the tech-debt log, and a
reference-count sweep of all 123 services to find what is built-but-unwired. CLAUDE.md is used as
the authoritative "what was built" record (it is unusually detailed and self-critical).

> **Status note (added after this review was written):** the user has turned this review into a
> phased plan — see CLAUDE.md → "POST-PHASE-9 PLAN" and the "CONSOLIDATED DEFERRED-ITEMS REGISTER"
> for the authoritative what-goes-where. In short: a **NOW "Wiring & Tech-Debt Pass"** (wire the
> unwired presentation layer + `AiTacticalPlanner`; rewrite the `Rate*` functions; one `CareerStats`
> store; test-harness QoL; small closers), then Phases 10-18. The user deferred **git/VCS entirely**
> and moved the **app + persistence bridge + build hygiene to Phase 17**, the **external-data
> import to Phase 18**, and **re-added the domestic draft (§8.10) to Phase 18**. So this doc's
> "Part 3 — Tier 0/1" framing is superseded by that register; the *analysis* below still stands.

Two things the user asked me to change from the earlier document, applied throughout:

1. **International cricket has first preference over franchise leagues, always.** The earlier idea
   ("a franchise window can force an international series to be cancelled") is **removed**. The
   correct model: an international window *always wins* a clash; the franchise league and the
   domestic season are the ones that lose players / reschedule / play a weakened round. See §12.
2. **The human coach always has final authority, with an explicit option to delegate to staff.**
   This principle is already built (`DecisionAuthority`, `TacticalPlan.ForHumanCoach()`,
   `ManagerPreferences`) for in-match and some out-of-match decisions — the suggestions **extend it
   consistently to every decision surface** (squad, XI, training focus, transfers, contract
   offers, press) rather than introduce anything that overrides him.

Effort tiers: **S** = one focused slice · **M** = 2-4 slices · **L** = a phase-sized piece.

---

# PART 0 — STRUCTURAL / PROJECT-HEALTH FINDINGS (read this first)

These are not "features"; they are things that would bite the moment someone tries to actually
*ship or play* this.

### 0.1 — There is no application. No game loop, no entry point. **(L, and it's the big one)**

`grep` for `static … Main(` across `src/` returns nothing. The Domain and Data projects are
class libraries; only the **test project** is an executable. There is no `Game` class, no
`GameSession`, no CLI, no save/load orchestration you can run. You can run the test suite and
nothing else. CLAUDE.md says every slice "ships as an API + headless default" — true at the *code*
level, but there is no headless *app* that wires those APIs into a playable/simulatable loop.

*Why it matters:* every "verified over N seasons" claim is a test doing `new WorldClockService()
.AdvanceTo(...)` on a hand-built `WorldState`. Nobody has ever loaded a save, advanced it, saved
it, and reloaded it as a real workflow — because that workflow does not exist in code.

*Suggestion:* build a thin `CricketManager.App` (console) that: creates or loads a `GameDataContext`,
hydrates a `WorldState` from it, runs the clock to a target date (or interactively day-by-day),
and persists back. This is the seam everything else has been building toward and it is now the
critical-path blocker for the project being "a game" rather than "a simulation library".

### 0.2 — `WorldState` and `GameDataContext` are not connected. Saving loses most of the world. **(M/L)**

There is **no code anywhere** that converts a `GameDataContext` into a `WorldState` or a
`WorldState` back into a `GameDataContext`. `WorldState` is only ever `new`'d inline in tests.
Worse, even if a bridge existed, **~19 `WorldState` collections have no repository at all**:

`NewsArchive`, `Digests`, `RecordBook`, `HallOfFame`, `Awards`, `MentoringGroups`,
`CaptaincyProfiles`, `SeasonContributions`, `PlannedRosters`, `DevelopmentHistory`,
`MatchesThisSeason`, `MatchesThisSeasonByFormat`, `RecentDevelopmentByPlayer`,
`MatchdayIncomeThisSeason`, `SeasonCrowdFill`, `PendingPressStories`, `CareerStats` (the
string-keyed dict — see 0.3), `YearForm`, `MonthForm`, `MarketIndex`.

So a save/reload would drop: the entire news history, the record book, the Hall of Fame, all
season awards, every mentoring pairing, every captain's accumulated decision-quality profile, the
promotion/relegation planned rosters, the four-year development-snapshot history, the market
index (the whole Phase 9 inflation model resets to 1.0), and every in-season accumulator.

*Suggestion:* (a) add a `WorldStateRepository` (persist `WorldState` itself as one JSON document,
which the JSON-column `SqliteRepository` already supports), OR (b) give each of those 19
collections a repo + wire `SaveAllAsync`. (a) is far less work and is honest about the fact that
`WorldState` *is* the save. Then build the hydrate/dehydrate bridge (0.1's app needs it).

### 0.3 — Two `CareerStats` stores that never sync. **(S)**

`GameDataContext.CareerStats` is `IRepository<PlayerCareerStats>`. `WorldState.CareerStats` is
`Dictionary<string, PlayerCareerStats>` (key = `"{playerId}:{format}"`). `CareerStatsService`
writes only the `WorldState` dict. `CareerStatsAggregationService` / `PlayerProfileService` read
from... whichever they were handed. Nothing keeps them in step. Pick one (the `WorldState` dict is
the live one; the repo is the stale one) and delete or bridge the other.

### 0.4 — The whole "match presentation" layer is built and 0% consumed. **(M)**

Reference-count sweep confirms **zero production callers** for:

| Service | Built in | State |
|---|---|---|
| `CommentaryService` | Slice 11 | ball-by-ball commentary — nothing generates it |
| `PostMatchAnalysisService` | Slice 12 | `FixturePlayService` calls `AnalyzeMatch(result)` / `AnalyzeMultiDayMatch(result)` — but **only reads `.PlayerOfTheMatch` from it**. The full report (headline, top performers, key moments, session narrative, notable passages) is **computed every fixture and thrown away** — worse than unwired, it's wasted work |
| `PreMatchReportService` | Wave 8 | toss-independent conditions report — never called |
| `PlayerDevelopmentReportService` | Post-7/8/9 follow-up (b) | the quarterly snapshot IS taken; the report over it is **never generated for anyone** |
| `ScorecardFormatter` | Post-7/8/9 rectification | dismissal notation formatter — no renderer calls it |
| `AnalystService` | Slice 6e | **completely unwired** — a full staff role with an estimate-vs-truth model that produces nothing in a live match (CLAUDE.md admits this) |

*Why it matters:* these represent a large amount of tested work that delivers **zero player-facing
value** until something consumes it. `AnalystService` in particular means the `Analyst` staff role,
the analyst-quality-scaled report, and `SpecialistStaffService.AnalysisQualityBoost` are all paid
for and do nothing in a real fixture.

*Suggestion:* wire them into `FixturePlayService`'s per-fixture flow: pre-match → `PreMatchReportService`
+ each side's `AnalystService` report folded into the `TacticalPlan` (see §3); post-match →
`PostMatchAnalysisService` report + a few `CommentaryService` highlight lines attached to the
`MatchResult` and surfaced through `NewsEngine`. Even in a headless build these become news items
and a match "story". **This is the single highest realism-per-effort item in the whole review.**

### 0.5 — Tech-debt item 4 is still open, and Phase 4 is "done". **(M)**

CLAUDE.md tech-debt item 4: `PerformanceRecordingService`'s two `Rate*` methods "read
runs/balls/wickets/economy only… **Phase 4 should replace** the two Rate* methods". Phase 4 is
complete; this was not done. Every rating that feeds form, reputation, POTM, player-of-the-series,
awards, and `SeasonContributions` is still the "first approximation". The match engine now *has*
pressure, chase context, match situation, opposition strength, momentum — the exact inputs the
item said a real rating needs. (Item 7 — `Competition.Reputation` movement — is actually closed by
7.2 but the tech-debt list wasn't updated; minor doc rot.)

*Suggestion:* rewrite `RateBattingInnings`/`RateBowlingSpell` to weight: runs vs the match par,
strike rate vs the situation's demand, wickets/economy vs phase, pressure faced (chase position,
`BaseImportance`), and whether the innings/spell *changed the game* (read `MatchMomentum` swing or
the win-probability delta). The pipeline around them is fine.

### 0.6 — No version control, no CI, no lint gate. **(S, but do it now)**

`git status` → "not a git repository". No `.gitignore`, no `.editorconfig`, no
`Directory.Build.props`, no `TreatWarningsAsErrors`, no `.github/workflows`. The "0 warnings" and
"3 consecutive clean runs" discipline is entirely manual and entirely undefended. The CLAUDE.md
"mystery files appeared in the working directory" incident is a **direct, predictable consequence
of having no VCS**.

*Suggestion:* `git init`; `.gitignore` (bin/obj/*.db/scratch); `Directory.Build.props` with
`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` + `<AnalysisLevel>latest</AnalysisLevel>`;
a one-line CI (`dotnet build` + `dotnet run --project tests`). Commit the current green state as
the baseline. Everything else in this document is safer to do once this exists.

### 0.7 — The test harness has outgrown itself. **(S-M)**

`Program.cs` is **16,638 lines** and the suite now takes **~12+ minutes**. `TestRunner`:
- prints only `ex.Message`, **never a stack trace** — a failing assertion 12,000 lines deep is
  hard to locate;
- **no way to run one test or a subset** — every debug cycle is 12 minutes;
- **no per-test timeout** — a hang (e.g. an auction infinite loop, which nearly happened twice
  during this project) hangs the whole run;
- no parallelism, no category tags, no "changed-files-only" mode.

*Suggestion:* keep the hand-rolled runner (it's a deliberate, sound choice) but add: a `--filter
<substring>` arg, a stack trace on failure (`ex`), a per-test `CancellationToken`/`Task.WhenAny`
timeout, and split `Program.cs` into `Tests.MatchEngine.cs`, `Tests.Auction.cs`, etc. (partial
class or just `#region`/separate files calling into the same runner). A `[slow]` tag so a fast
inner-loop run skips the multi-year integration tests.

### 0.8 — Minor: stale `net8.0` build artifacts under `tests/.../obj/Debug/net8.0/`. Leftover from
the sandbox era (the project targets `net10.0` everywhere). Harmless, but `git clean` fodder.

---

# PART 1 — SYSTEM BY SYSTEM: real world · what we do · gaps · suggestions

Each system: a short "how real cricket does it", an honest read of the current implementation, and
suggestions (with the tier). Deliberate deviations you've chosen are respected and noted.

## §1 — Match simulation (ball model, innings, formats)

**Real world:** every delivery is a contest of skill under conditions, situation, pressure and
fatigue; formats are genuinely different games (a chase is not "the innings again"; a Test innings
has no over limit but has a *clock*).

**What we do — strong.** `BallOutcomeModel` computes an *effective skill* per ball and reads a
genuinely rich set of inputs: form, morale, matchup (career + recency + pressure-amplified),
in-match bowler familiarity, mystery-spinner deception, pressure/temperament, momentum, set-state,
confidence, fatigue, rhythm, captain lift, crowd/home edge, pitch (pace/spin/bounce/friendliness,
day-based wear, rough patches from actual bowling, dew), ball age, batting/bowling pair chemistry,
run-out risk from chemistry, field-effect scaling, delivery line/length/variation effects,
spin-direction × handedness, swing/seam handling, over/round-the-wicket angle. `InningsSimulator`,
`MatchSimulator` (real chase), `MultiDayMatchSimulator` (4 innings, declaration, follow-on, draw,
session clock, bad light, over rates, nightwatchman, free hits), `RainService` + DLS +
`LostTimeRecoveryService`. This is a deep, well-layered engine and the "simulate ≠ record" split
(`MatchRecorder`/`MultiDayMatchRecorder`) is clean.

**Gaps / extensions:**

- **1.1 — Ratings are the weak link (tech-debt 4).** See §0.5. **M.**
- **1.2 — Reverse swing is not modelled.** A defining Test/old-ball skill: once the ball is
  genuinely old (>~35 overs, FC / pre-2011-ODI only), a skilled bowler bowls a plan that starts
  wide and tails back — dismissal mix shifts hard to **bowled + LBW**. Every hook exists
  (`DeliveryEffectService.DismissalWeightsFor`, ball age, `Bowling.Swing`/`Seam`, abrasive
  surface). Two-new-ball ODIs correctly get *no* reverse — which itself is a teachable fact about
  that rule. **S-M.**
- **1.3 — Session-level in-match momentum** — `MatchMomentum.OnBreak` is wired at *day*
  transitions only; `TakeBreak(BreakLength.Session)` has been dead code since Slice 8. A Test
  session is the natural momentum unit ("they've had a shocking hour"). Hook `MatchTimeline`'s
  session model into the innings loop. **S.** (CLAUDE.md already flags this as deferred.)
- **1.4 — Impact Player as a real substitution** — currently a flat ±2.5% batting edge. Model it:
  a 12th player named pre-match, subbed in for a non-batter after an early wicket (extra depth) or
  a bowler-for-batter after over 6. It genuinely changed IPL tactics (teams bat deeper,
  all-rounders devalued — a real, contested change) and we should model that trade-off, not paper
  over it. The innings sim has no substitution concept today. **M-L.**
- **1.5 — Two-new-balls ODI fact** — model it explicitly (a spinner in the ODI middle grips less
  with a hard ball; no reverse). Ties to 1.2. **S.**
- **1.6 — Pitch that changes *within* a limited-overs innings from dew** — we model dew for the
  toss and chases but not a within-innings grip loss as the evening session progresses. **S.**
- **1.7 — DRS** (deferred by your explicit call). When you want it: a review budget per innings,
  the umpire's "original decision" as a real anchor, a captain/senior-batter review-quality
  attribute, "umpire's call" retained, and the psychological hit of a wasted review. **M.**
- **1.8 — Concussion substitutes, retired-hurt-then-return, timed-out** — the innings sim has no
  concept of a player leaving and (sometimes) returning. Rare but real. **S each.**

## §2 — In-match tactics & the tactical plan

**Real world:** T20 is a 3-phase game (attack the new ball / dot-ball squeeze / hit the deep
field); ODI is 3 field blocks (2 out / 4 out / 5 out) with the middle overs as the underrated
"consolidation vs pressure" contest; Test is session-by-session intent (win / draw / don't-lose),
declarations weighed against the *weather forecast* and pitch wear, attacking↔defensive fields
that swing on the *match situation* not just the batter.

**What we do — strong for T20, thinner for ODI/Test intent.** `TacticalPlan` is genuinely deep:
team/phase/player batting intent, per-bowler and per-over `BowlingApproach` (line/length/variation),
`FieldAggression` per phase, manual fields, withheld bowlers, `NextOverBowlerId`, matchup targeting
(`TargetBowlerId`, `TargetBowlerType`, `RespectBowlerId`), zone targeting/avoidance,
`ProtectLowerOrderCount`, `RebuildAfterWicket`, toss decision. `PlanFitService` (exploits-weakness ×
bowler-suitability), `BowlerExecutionService` (execute/judge/discipline + `Insistence`),
`DeliveryEffectService`, trap balls (§B), pattern reading (§C), `AutoFieldSetter` + `FieldSettingRules`
(Law 28.4, circle restrictions) + `FieldEffectService`. `MatchAmbitionService` gives Test cricket a
session-by-session win/draw/loss% read and turns it into intent. **The human-coach-final-say layer
(`DecisionAuthority` + `TacticalPlan.ForHumanCoach()`) is well designed** — this is the model to
extend everywhere (see §5, §7).

**Gaps / extensions — the user wants this section VAST, so:**

- **2.1 — Situation-driven field aggression, not just batter-driven.** A defensive field in the
  last over of a chase leaks the winning runs; an attacking field 300 ahead with a day left is
  throwing it away — both are real errors we should let a bad captain make and a good one avoid.
  Derive a `situationAggression` from the chase equation / `MatchAmbition` / Test session state,
  feed it to `AutoFieldSetter`, scale by captaincy competence. **S-M.**
- **2.2 — Left-hander-up-vs-leg-spin (auto).** The classic modern lever — we model *why it matters*
  (`ApplySpinDirection` knows L vs R) but never *make the call*. A sibling to
  `InMatchTacticalAI.ChoosePinchHitter`: on a wicket, if a wrist-spinner is on, a good captain
  promotes the best available left-hander, gated by `DecisionAuthority` + judgement. **S.**
- **2.3 — ODI middle-overs as an explicit contest.** Extend `BattingIntent` with an `Accumulate`
  mode (farm singles, protect wickets, anchor+rotator pairing) and give the fielding captain a
  "contain vs attack" read from ahead/behind par. Research: the middle "dictates terms for the
  death". **M.**
- **2.4 — Declaration as a captain-quality call, not a formula.** `InningsSimulator.ShouldDeclare`
  is "~3.2/over + a cushion, ≥55 overs left". Make it read the captain's `TacticalJudgement`, the
  **weather forecast** (declare early if rain is coming — Bazball's whole point), pitch wear, and
  the opposition's batting depth. A bold captain declares 40 "early"; a cautious one bats too long
  and gifts the draw. **S-M.**
- **2.5 — Follow-on as a real decision.** Currently a rule + 2 factors. Weigh: attack fatigue
  (`BowlerMatchState` aggregate), the surface's 4th-innings danger, the lead margin, the time
  left, and the captain's temperament. Modern captains decline it far more than the rule alone
  suggests. **S.**
- **2.6 — Bowling-plan sequencing *within* an over.** Real bowlers bowl a stock ball, then the
  wicket ball as a weapon; set a field for the in-swinger and keep the out-swinger as the
  surprise. We resolve a *standing* plan per ball. A `BowlerExecutionService` "over shape"
  (2 stock + a probe + a wicket ball) driven by dot-pressure (§B's `ConsecutiveDots` is already
  there). **S-M.**
- **2.7 — Batting response to the bowler's *just-bowled* pattern.** §C does this for the batter
  reading the bowler's line/length; extend it so a batter can decide "see off this spell, then go"
  by reading `BowlerMatchState.Rhythm` (a bowler in rhythm is respected; one who's lost it is
  attacked). **S.**
- **2.8 — Bowling-pair pressure transfer.** Extend `BowlingPairSynergyService`: dots built at one
  end raise the wicket probability at the *other* (the batter risks the "easier" bowler to release
  pressure). It's how new-ball and spin pairs actually strangle. **S.**
- **2.9 — Batting partnership *style* fit (anchor / rotator / aggressor).** Complementary styles
  (Trescothick + Atherton) outperform two of a kind. We track chemistry (history) but not
  *a-priori style fit*. Derive from `BattingTraits`; a complementary pair gets a small
  rate/stability bonus, two blockers or two sloggers a penalty. **S.**
- **2.10 — A `RunningBetweenWickets` / `Calling` attribute.** Research: "calling conventions need
  rehearsal; poor communication = run-outs that cancel the benefit". We have a chemistry-based
  run-out multiplier but no *player attribute* — which is why some brilliant batters are terrible
  runners. Add `Mental.RunningCalling`; it sets a floor under run-out risk chemistry can't fully
  remove. Touches the player model — do it deliberately. **S.**
- **2.11 — Bowling changes because a bowler is *being collared* / bringing on a part-timer when
  specialists are milked.** The long-deferred "AI in-innings tactical decisions" note.
  `InMatchTacticalAI` today only does the pinch-hitter. `SelectBowler` already has economy/rhythm
  signals — surface a captain-quality "take him off" and "try the part-timer to break the stand"
  decision. **M.**
- **2.12 — Powerplay-2 / last-10 acceleration decision (ODI/T20).** When to cut loose, gated by
  wickets in hand and the par equation. **S.**
- **2.13 — Crease-position / guard change vs swing** (Section Z's other half — deferred).
  `ApplySwingAndSeamHandling` + a batter-side `GameAwareness` read that partially covers a
  technical weakness by adjusting guard. **S.**
- **2.14 — Keeper up/back is modelled for stumping; extend to standing-up-to-seam** as a captain
  decision on a slow/turning pitch (more stumping/caught-behind chances, slightly more byes). **S.**
- **2.15 — Bodyline / leg-theory / bouncer barrage as a named plan with a real over-rate and
  code-of-conduct cost** — ties tactics to `DisciplineService`. **S.**
- **2.16 — Batting *for the draw* on the last day** — `MultiDayMatchSimulator` has the shape;
  make it a captain-quality read (a poor side collapses trying to survive; a good one blocks it
  out). **S.**

## §3 — In-match AI (captain, coach, and the missing planner)

**Real world:** the captain makes dozens of live attributable calls; the coach sets the pre-match
plan and the philosophy; a Director of Cricket / analyst feeds the plan.

**What we do:** `CaptaincyService` (decision ownership per decision type, partnership quality
between coach+captain, mistake rate, pressure noise), `CaptaincyProfile` (effective leadership,
tactical judgement, team lift), `CoachCaptainRelationship`, `MatchLeadership` + `MatchSuggestion`,
`InMatchTacticalAI` (pinch-hitter only). Captaincy *learning* is a per-match accumulator
(`CaptaincyGrowthService`, Wave 4).

**Gaps:**

- **3.1 — There is no `AiTacticalPlanner`.** Every AI side passes `null` and the engine derives
  everything ad hoc. Give each AI side a real `TacticalPlan` built from: its analyst report (§0.4),
  its captain's profile, the opposition's known weaknesses, the conditions, and the match
  situation — **and let a poor AI build a bad plan.** This is the foundation §2.1/§2.2/§3.2 and
  the post-match "India got their plans wrong against the left-handers" narrative all lean on. **M.**
- **3.2 — The AI reads its own analyst dossier in-match.** `AnalystService.ApplyToPlan` produces
  `TacticalPlan.MatchupApproaches` and is tested — nothing calls it in a live fixture. Wire it:
  each side's analyst (+ `AnalysisQualityBoost`) builds the report, applies it to the plan; a weak
  analyst's report is confidently wrong and costs the side (already modelled). **M.** *(This is
  §0.4's `AnalystService` gap and §3.1's planner — do them together.)*
- **3.3 — Conditions-and-opposition XI selection** (the deferred half of "AI depth", Slice 6d/6f):
  pick the XI for *this* pitch and *this* opponent (an extra spinner on a turner, a left-arm
  option vs a right-heavy top order, drop a bunny). `XiSelectionService` has a conditions tilt;
  make it a real per-fixture decision reading `PlanFitService`. **M.**
- **3.4 — Using the *whole* attack** — CLAUDE.md's Slice 6f rebuilt bowler selection to
  concentrate overs on the right bowler for the situation; verify AI-vs-AI Tests don't still
  over-bowl two men (the probe that found this once). **S (verify) / M (fix).**
- **3.5 — Coach in-match authority** — CLAUDE.md flags: if `InMatchTacticalAI` ever gives the
  *head coach* live authority, the Wave 4 milestone-only coach-growth cadence must gain a
  match-grain signal. Decide this when §3.1 lands. **note.**
- **3.6 — Batting-order shuffling for the situation** (not just the pinch-hitter) — promote a
  keeper to counterattack, demote a struggling top-order bat, send the all-rounder up in a chase.
  Extend `InMatchTacticalAI`. **S-M.**

## §4 — Coaching (career, market, badges, philosophy, franchise, earnings)

**Real world:** a coaching career spans board jobs, domestic clubs, and the **franchise circuit**;
top coaches run multiple franchise teams (Voges/Flower/Moody) and earn hybrid income >$2m/yr; a
**Director of Cricket** sits above the head coach and owns the auction plan + pathway; coaching
badges (Level 1→3, high-performance) gate jobs.

**What we do — genuinely deep.** `CoachCareerService` (season score vs expectation, board trust,
authority, dismissal, `GrowFromMilestone` campaign growth, specialisation drift, achievement
leniency, "one tenure no title" clock, `AmbitionTier`, `ScrutinyFactor`, `CoachCareerRecord`),
`CoachJobMarketService` + `CoachRecruitmentService` + `JobMarketService`/`JobMarketApplicationService`
(two-directional market, `RoleFitService`, culture fit, relocation comfort),
`CoachCareerService.ProgressLicence` (badge progression, Phase 8), `InterimCoachService`,
`FranchiseCoachService` (campaign-based, window-clash check, national-coach-never-available),
Type A / Type B departures, `EvaluateRivalInterest`/`OfferRetention`, `CoachCaptainRelationship`,
`SelectionWeighting` per philosophy.

**Gaps:**

- **4.1 — No Director of Cricket.** A real, distinct role the franchise world runs on (Sangakkara
  at RR, KP-as-mentor at DC). `StaffRole.DirectorOfCricket`; when present, `FranchiseAuctionService.BuildPlan`
  accuracy + the retention decision improve, and the DoC (not the campaign coach) carries the
  franchise's multi-year identity/archetype (§8.1). Also applies to a domestic club. **M.**
- **4.2 — Coach worldwide earnings ledger + campaign fees.** A franchise stint pays a **campaign
  fee**, not a year's salary; a coach's annual income = board contract + N franchise campaigns.
  `Coach.CareerEarnings` split by `{Domestic, National, Franchise}`; `FranchiseCoachService` pays
  a campaign fee scaled by league prestige + the coach's **circuit record**; a coach who has a big
  franchise year is harder for his domestic club to keep. *(User asked for this explicitly.)* **S-M.**
- **4.3 — A coach's global CV / circuit reputation** distinct from his current-club standing.
  `Coach.CircuitRecord` (franchise titles/finals across all leagues) → drives
  `FranchiseCoachService.FindAvailableCoach` (a proven franchise coach hired first, paid more) and
  makes a domestic club covet a hot franchise coach. **S-M.**
- **4.4 — Assistant/specialist coaches work the circuit too.** Hussey (batting) + Fleming (head)
  as a pair. `FranchiseCoachService` fills 2-3 specialist seats per campaign from off-contract
  staff, campaign fee each. **S.**
- **4.5 — Coaching philosophy is static after creation.** A coach's philosophy should be able to
  *drift* with experience and results (a rigid disciplinarian who keeps losing softens; a
  developer who wins big at a rich club hardens toward results). `CoachCareerService` already has
  specialisation drift — add philosophy drift. **S.**
- **4.6 — A dedicated `CoachingPhilosophy.AllrounderLeaning`** (named gap in the squad-selection
  spec) and **format-specialist coaches** (a white-ball coach, a red-ball coach) — real modern
  split-coaching. **S each.**
- **4.7 — `CareerSatisfaction` / coach-initiated resignation to chase a *dream job*** — an
  ambitious coach at a small club who turns down security to wait for a big vacancy. Partly built
  (`TryResign`); add the "waiting for a specific job" state. **S.**
- **4.8 — Coach–PLAYER relationships** (distinct from the captain-only `CoachCaptainRelationship`)
  — a coach who man-manages a star well gets more out of him; a clash gets a transfer request.
  Sits on the Wave 5 dressing-room foundation. Named as deferred across two passes. **M.**
- **4.9 — Backroom staff poaching between clubs mid-contract** with compensation — the staff
  equivalent of the coach job market's `EvaluateRivalInterest`. **S.**

## §5 — Squad building & selection (pools, XI, announcements, authority) — user wants this VAST

**Real world:** months of planning; a blueprint of roles to strengthen; a selection panel (with
the captain often *on* it) that produces a squad, a series, a tour; a playing XI chosen for *this*
pitch and *this* opponent; workload managed across three formats and a year; the human/management
has final say with staff producing the options.

**What we do — a lot, and it's good.** `SquadStatusService` (8-tier status derived, not defaulted),
`PlayerSelectionEvaluator` (availability-filtered, philosophy-weighted, `forBowling` fix,
established-player protection that fades under a prolonged slump), `SquadManagementService`
(Continue/Roadmap/Rest/Drop, probabilistic), `XiSelectionService` (keeper slot, bowling-depth
floor by format law, conditions tilt, occasional-keeper classifier, batting order from derived
`BattingRole`, contested-slot surfacing, allrounder preference, announced-squad filter),
`SquadSelectionService.ValidateComposition` (hard rules + advisory notes),
`SquadSelectionAuthorityService` (**human coach: always surfaced; AI coach: genuinely evaluates —
a proposal, never a mutation**), `SquadAnnouncement` (bilateral vs tournament, `PlayerCap` +
reserves, mid-series replacement gated by date, `PromoteReserve`), `CaptaincyAppointmentService`
(format-specific patterns Unified/RedBallWhiteBall/LongFormShortForm/ThreeSeparate,
merit-must-earn-an-XI-place, vice-captain), `CaptainSquadInput`, `NationalPoolService`
(coverage-slot-driven, watchlist, `AddException`, `DrawSquad`), `SelectionPanelService`
(politicised-panel distortion), `WorkloadRotationService` (per-fixture, dead-rubber-aware).

**This is one of the deepest parts of the project. The gaps are about *process realism* and
*vastness*, not missing basics:**

- **5.1 — The selection *meeting*.** We have panel distortion but not the meeting. Model it: the
  panel (+ captain, per §5.2) debates each contested slot, produces a one-line **rationale** per
  pick and a **dissent note** when the chairman overrules; a politicised/weak panel's rationale is
  visibly thin. `SquadAnnouncement.SelectionRationale` (per debated slot) → surfaced as news →
  feeds "why isn't X picked" pundit stories. **S-M.**
- **5.2 — The captain formally on the panel** (Australian model) — `CaptaincyProfile` +
  `SquadSelectionAuthorityService` already have the pieces; make the captain's advocacy a real
  weighted input to the panel, and a source of captain–selector friction when overruled. **S.**
- **5.3 — Pre-series planning conversation.** Before a series/tour: the coach + captain + panel
  agree a **plan** — "target their left-handers", "we need a 2nd spinner for these conditions",
  "blood the youngster in the dead rubber" — which then *pre-loads* the XI-selection tilt and the
  `TacticalPlan` for that series. This is the "vast planning" the user wants and it connects
  selection → tactics. **M.**
- **5.4 — Cross-series / cross-format workload contracts.** `WorkloadRotationService` is
  per-fixture. Real selectors rest a quick from a bilateral ODI series to be fresh for a Test tour
  or a franchise final. Read the *next 3 months* of fixtures across all formats + franchise
  commitments and produce a rest plan the panel applies (with the player's and the franchise's
  interests in tension). **M.**
- **5.5 — "Next in line" pipeline signal.** A player told by the selectors he's next in the queue
  develops faster and handles the debut better (feeds `MatchDevelopmentService` + `PressureMoment`).
  `NationalPoolEntry.NextInLine`. **S.**
- **5.6 — Central-contract tiers.** PCB-style tracks (top tier → 1 overseas league, mid → 2,
  lower → uncapped) that gate `ContractKind.Franchise` deals and are *compensated* with a higher
  retainer. This is the mechanism behind the whole board-vs-franchise story (§12). A
  `CentralContract` on the national team with a tier per player. **M.**
- **5.7 — NOC (No Objection Certificate).** A centrally contracted international needs his board's
  NOC to play an overseas league; the board can deny it (national clash, fitness, making a point —
  PCB/Usama Mir). Before the auction / before a franchise signing, roll an NOC decision (yes /
  yes-with-conditions / no) from the fixture clash + the player's importance + the board–player
  relationship; a denial pulls him from the auction pool. Great player-vs-board drama. **M.**
- **5.8 — Format-specific retirement is built** (`RetiredFormats`) — extend the *reverse*: a
  player **coming out of retirement** for a World Cup / a franchise payday (rare, personality- and
  money-gated). **S.**
- **5.9 — Playing XI as a genuine combination problem with more axes.** `XiSelectionService`
  covers keeper + bowling floor + conditions. Add: the **all-round balance** target (5 bat / 5
  bowl / a genuine all-rounder is different from 6+4), a **left-right mix** in the top order, a
  **death-bowling** requirement (T20), a **new-ball pair** requirement, and a **captaincy** check
  (the XI must contain a viable leader). **M.**
- **5.10 — Bench / "drinks carrier" development** — a player kept in the squad but not playing
  still trains (built) but should also lose match-sharpness over time (a real selection tension —
  "he needs game time"). `WorkloadRotationService` / `MatchDevelopmentService` inverse. **S.**
- **5.11 — Announcing a squad is an *event* with a press conference, a captain's comment, one
  "surprise pick" and one "big omission" the media leads on.** We have `SquadAnnouncement` as
  data; make it a `NewsEngine` moment with a `PressConferenceService` storyline. **S.**
- **5.12 — The human coach's authority, extended to selection consistently.** Today
  `SquadSelectionAuthorityService` surfaces the captain's proposal to a human coach and the human
  decides. Extend the **delegate** option (`ManagerPreferences.DelegateSquadSelection` exists) to
  be per-decision like the in-match `DecisionAuthority`: delegate *national squad* selection to the
  panel but keep *XI*; delegate *domestic* selection to an assistant but keep the *derby* XI. One
  consistent authority model across in-match and out-of-match. **S-M.**
- **5.13 — "Role clarity"** — a player who knows his assigned role (finisher, enforcer, powerplay
  bowler) performs closer to his ceiling; a shuffled player underperforms. `Player.AssignedRole`
  set by the coach; a match in-role gets a small confidence/performance bonus, out-of-role a
  penalty. (`SituationalPerformanceModifier` has the natural-role version; this is the
  *coach-assigned* one.) Connects selection, captaincy and development. **S-M.**
- **5.14 — Squad "identity" / culture** — a settled squad with a clear pecking order and low
  churn performs above the sum of its parts; constant chopping and changing costs cohesion
  (`DressingRoomHarmony` exists — tie selection churn to it). **S.**

## §6 — Training & player development

**Real world:** a weekly calendar (match prep / recovery / skill / rest), in-season vs off-season
vs camp modulation, individual programmes, mentoring, role conversion, staff- and facility-driven
quality; development is voluntary and coach-directed, distinct from involuntary ageing.

**What we do — deep.** `TrainingService` (17 `TrainingFocus` values → attribute clusters, two
kinds of headroom, age/intensity/coach/personality scaling, `SuggestFocus`, all-round cap,
role conversion, overtraining injury, rehab-allows-light-work, the rare young-player breakthrough),
monthly cadence (Wave 2), `MatchDevelopmentService` (format-aware learning-by-doing + the
Stokes/Brathwaite pressure-moment arc), `TrainingWeekService` (match/prep/off-season modulation,
weekly), `TrainingCampService` (three tiers, franchise-committed players excused, Slice 9.8),
`MentoringService` + `MentoringGroup` (compatibility-gated, quarterly review, temperament drift),
`SpecialistStaffService.ApplyIndividualWorkWeek` (weekly, per assigned coach),
`PlayerDevelopmentSnapshot` history (quarterly), `RoleTraitDeriver` re-run after every mutation.

**Gaps:**

- **6.1 — The full weekly calendar as an explicit per-player schedule** (deferred, needs a finer
  clock than monthly). `TrainingWeekService` is the seasonal-modulation stand-in. When/if a weekly
  simulation tick lands, build the real per-player weekly plan (prep/recovery/skill/rest, travel
  days blocking training). **M.**
- **6.2 — `PlayerDevelopmentReportService` is unwired** (§0.4). The snapshots exist; nobody reads
  the report. Wire it into a quarterly "development review" the coach sees (and the board reads for
  a youth-leaning judgement). **S.**
- **6.3 — Biomechanical / technical *flaws* as a development target** — a bowler with a stress-
  fracture-prone action, a batter with a trigger-movement fault vs pace. A `TechnicalFlaw` on the
  player that a good coach can (slowly, partially) remediate, and that a scout can spot. **M.**
- **6.4 — Training *load* as its own risk axis** — cumulative training + match load over weeks
  drives a soft-tissue injury curve (separate from the per-match workload injury). Ties to §5.4. **S-M.**
- **6.5 — Skill *regression* from lack of practice** — a player who stops training a skill (or
  only plays one format) slowly loses the edge in the others. `RoleTraitDeriver` + a slow decay on
  unpractised clusters. **S.**
- **6.6 — Youth international cricket** (U19 World Cup) as a development showcase feeding reputation
  and the senior pool. **M.**
- **6.7 — Academy poaching** — a bigger club approaches a smaller club's standout 16-year-old
  (needs the market; the story hook — `ScoutingAccuracyService` + reputation — exists). **S-M.**
- **6.8 — Early-career burnout** as a tracked mechanic — a teenager over-bowled has a shorter
  career (the machinery exists: `WorkloadRotationService`, `PlayerAgeingService`'s quick penalty). **S.**

## §7 — The human coach's decision surface (final authority + delegation)

**This is a principle, not a system — and it's mostly built.** `DecisionAuthority`
(`CoachHasFinalSay` / `Consult` / `Delegated`), `TacticalPlan.ForHumanCoach()`,
`AuthorityByDecision`, `DelegatedPlayers`, `ManagerPreferences` (`DelegateSquadSelection`,
`DelegateStaffAndCaptaincy`, `DelegateAcademy`, `DelegateLoans`, `AlwaysInclude`/`NeverSelect`,
`RotateInDeadRubbers`), `SquadSelectionAuthorityService` (human always surfaced).

**Gaps — make it *one consistent model* everywhere:**

- **7.1 — Unify the authority model.** In-match uses `DecisionAuthority` per `InMatchDecision`;
  out-of-match uses a scattering of `ManagerPreferences` booleans. Build one
  `DelegationProfile` on the coach/team covering **every** decision surface — squad selection
  (national / domestic / franchise, separately), XI, tactical plan, training focus (per player or
  globally), transfer targets, contract offers, loan decisions, academy promotions, press tone,
  board-objective responses, captaincy appointment — each set to `DoItMyself` / `Consult` /
  `Delegate`. **S-M.**
- **7.2 — "Consult" produces a real recommendation the human sees and can accept/reject/modify.**
  Today `Consult` is "behaviourally identical to final-say plus a surfaced suggestion". Make the
  suggestion a first-class object (a `StaffRecommendation` with the *who*, the *what*, the
  *reasoning*, and a confidence) that a headless build logs and a UI shows. **S.**
- **7.3 — Delegation has consequences.** A coach who delegates selection to a weak panel gets
  weak squads; one who delegates transfers to an ambitious DoC gets a churny squad; one who does
  everything himself burns out (a `CoachWorkload` → satisfaction/decision-quality). Delegation
  should be a genuine trade-off, not free. **S-M.**
- **7.4 — The board can *force* delegation.** A board that has lost faith installs a Director of
  Cricket above the coach and strips his transfer authority (a real, humiliating, and common
  event). Ties to §4.1 and §13. **S.**

## §8 — Auction & franchise leagues

**Real world:** months of prep + a blueprint; year-round scouting labs (25-30 people); the
Moneyball franchises avoid bidding wars and target the accelerated round; **name recognition is
the most overvalued commodity** (big names go 30-50% over statistical value); marquee sets →
capped-by-role → uncapped-by-role → accelerated round (submit-a-shortlist) → final segment;
retention (≤6, ≤5 capped, ≤2 uncapped) + RTM (match → one final raise → decide again); mega every
~3 years, mini between; Impact Player; a per-match fee.

**What we do — genuinely deep after the rectification + follow-up.** `FranchiseAuctionService`
(interest-vote shortlist sized to franchise count, base-price self-selection off fixed brackets,
ordered sets, strict bid staircase, `FranchiseAuctionPlan` with priority tiers + per-role budget +
conditions tilt, adaptive bidding, purse-pressure escalation, accelerated round, RTM twist,
mega/mini, `AuctionSummary` read-model), `FranchiseRetentionService` (fixed slabs + RTM cards +
young-uncapped reserve list), `FranchiseAuctionMediaService` (preview + report + press
conferences), `FranchiseFinanceService` (loss-proof central pool + prize money + a reserve floor),
`FranchiseCoachService` (campaign-based).

**Gaps — mostly about *variety and identity*:**

- **8.1 — Franchise auction ARCHETYPES.** Every franchise brain is the same `BuildPlan`/
  `InterestVote`, so two runs feel identical. Give each a persistent identity: *Moneyball*
  (CSK/MI/RR — avoid wars, target value + the accelerated round, heavy scout weighting),
  *Star-hunter* (RCB — overpay for names, name-recognition bias), *Balanced*, *Youth-builder*
  (heavy reserve list + uncapped focus). Derive from `Board.Ambition` + `CulturalIdentity` + the
  DoC (§4.1) + scout quality; it reshapes the plan's tiers/ceilings and the purse-pressure
  response. **Single biggest realism win for the auction. M.**
- **8.2 — Name-recognition overvaluation as a real, exploitable bias.** The most-cited auction
  inefficiency. `InterestVote`/`Ceiling` gets a `reputation` inflation term, scaled UP for a
  Star-hunter and DOWN toward statistical value by the franchise's **scouting-department quality**.
  Lets a smart franchise (or the human) out-trade the AI. **S-M.**
- **8.3 — Scouting-department SIZE as a real differentiator.** MI "as many as needed", KKR 25-30,
  RR 10 analysts. `Team.ScoutingDepartmentSize` (count of `Scout`/`ChiefScout`/`DataAnalyst`)
  reduces `InterestVote` noise + the name bias + widens the covered pool. A meaningful thing for
  the board to spend on. **S.**
- **8.4 — Live transparency the other franchises *use*.** We produce `AuctionSummary` but the
  bidding doesn't react to it mid-auction. Extend `Ceiling`'s purse-pressure term to react to
  *role saturation* (rivals who've filled this role drop out → don't escalate) and a rival's
  *plan progress* (a rival whose must-haves are done is dangerous on an unplanned name). Mostly
  there. **S.**
- **8.5 — Retention as a negotiation.** A player held below market can agitate (built) — add the
  2-3 round offer/counter (the franchise offers a slab, the player counters, they settle or he
  enters the auction). Makes the mega-auction build-up a story and gives the player agency. **S-M.**
- **8.6 — The accelerated round as a genuine value phase.** Currently "unsold, best-first". Real:
  franchises *submit a shortlist* of who comes next; a franchise that saved purse gets first pick
  of a bargain. The Moneyball archetype should *target* this. **S-M.**
- **8.7 — Mega-auction "purse deducted" retention pricing is fixed slabs — good — but add the
  *strategic choice*** the real rules force: retain 5 capped and go into the auction with ~40% of
  your purse and no marquee firepower, or retain 2 and rebuild. The plan should reflect that
  choice, and a bad choice should hurt. **S.**
- **8.8 — Auction media beyond preview/report** (user asked for "everything in detail"): a
  **mock-auction / predictions** piece a week out; a **winners-and-losers** verdict two days
  after that other franchises' fans react to (fan sentiment); a **player-reaction** line for a
  big-money buy or an unsold star. `FranchiseAuctionMediaService` `MockAuction` + `WinnersAndLosers`
  passes. **S.**
- **8.9 — Multi-year dynasty tracking.** A franchise that keeps a core over 3-4 years builds a
  dynasty (CSK); one that rebuilds every mega auction churns. `Team.DynastyRating` (squad
  continuity + trophies) → fan sentiment, sponsor value, a small culture `PerformanceMultiplier`.
  Rewards the long-game archetype and gives the franchise world a memory. **S-M.**
- **8.10 — A domestic draft** (BBL-style, non-auction) as an *alternative* league type — some real
  leagues draft rather than auction. `Competition` flag + a `FranchiseDraftService`. **M.**
- **8.11 — Marquee/designated-player rules** (MLS-style — one player outside the cap) and
  **homegrown quotas** (a matchday squad must contain N products of that franchise's academy —
  once §6.7's franchise academies exist). **S each.**
- **8.12 — Trades / transfers *between* franchises** mid-cycle (a real, growing feature of the
  IPL) — a franchise swaps a fringe player + purse for another's surplus. **M.**

## §9 — Contracts, transfers, agents, loans

**Real world:** Bosman free agency, release clauses, transfer windows, buy/sell-on clauses,
holdouts, agents engineering bidding wars and taking a cut, loan fees + wage splits + options +
obligations.

**What we do — deep.** `PlayerContract` (domestic + franchise dual, release clause, signing bonus,
homegrown, loyalty/testimonial flags), `PlayerContractService` (sign, renewal negotiation both
sides, Bosman expiry), `PlayerValuationService` (contract-length is the dominant lever, age curve,
release-clause ceiling, market index × economic scale), `FreeAgentMarketService` (+ the domestic
associate-slot rule), `TransferMarketService` (windows, board sanction, seller/player agreement,
release-clause trigger, cross-border + hemisphere-keyed + economic-scale-denominated),
`TransferRequestService` (buried players, unsettled players, light agent), `RunAgentBiddingWars`
(follow-up — multi-club contest + agent cut), `LoanService` (fees / wage split / option +
obligation to buy), `WorldState.MarketIndex` inflation.

**Gaps:**

- **9.1 — Buy-back / sell-on clauses.** A selling club retains a first option or a % of the next
  sale — a real, strategic contract term. `PlayerContract.SellOnPercentage` / `BuyBackClause`. **S.**
- **9.2 — Contract holdouts / rebellions** (distinct from a transfer request) — a `MoneyFocused`
  star refuses to play until renegotiated; dents `DressingRoomHarmony`, a real board dilemma. **S.**
- **9.3 — Transfer deadline day** — a flurry of last-window activity, panic buys at inflated fees,
  a loan-with-obligation as a workaround. A `TransferMarketService` end-of-window intensity spike. **S.**
- **9.4 — A player's "dream club"** — an `Ambitious` player has one club he'd take a pay cut to
  join; it overrides the normal valuation. **S.**
- **9.5 — Image rights / commercial value** as a contract line and a revenue stream — a marquee
  player's shirt sales and sponsor pull. Ties to §10 and §5.11. **S-M.**
- **9.6 — Agents as persistent actors** — an agent has a roster of clients, a reputation, a
  relationship with each club; a good agent gets his client a better deal and a worse one poisons
  relations. `RunAgentBiddingWars` is a one-shot; make the agent a small entity. **M.**
- **9.7 — Multi-year sponsor contracts** (deferred from Phase 7) — performance clauses, kit deals,
  stadium naming rights, a title sponsor a competition negotiates and renews. **M.**
- **9.8 — A salary cap** (deferred) — enforced by the board; a club over the cap faces a luxury
  tax or a squad-registration limit. Needs the real wage system (have it). **S-M.**

## §10 — Finance (club, franchise, board, FFP, broadcast, sponsorship)

**Real world:** matchday + broadcast + sponsorship + prize money in; wages + upkeep + transfers +
operating out; FFP and points deductions; a central broadcast pool that funds the franchise
economy.

**What we do — the loop runs.** `SeasonFinanceService` (sponsorship / matchday / upkeep / **real
wages** from contracts / competition income), `BroadcastRevenueService` (competition pool, 60/40
equal/market split), `CompetitionRevenueService` (participation / prize / merit),
`SponsorshipValuationService`, `MatchdayRevenueService` (attendance from standing + venue draw +
contest quality + **fan sentiment**), `TeamFinanceService` (upkeep derived from facilities),
`FinancialFairPlayService` (points deduction + spending embargo, skips franchises),
`FranchiseFinanceService` (loss-proof central pool + reserve floor), `ClubBoard`
(ambition/wealth/patience/ownership/fan-sentiment, season budgets, takeovers), `BoardService`.

**Gaps:**

- **10.1 — `WageBillService` is still a stub for non-contract worlds** — fine, but `SeasonFinanceService`
  should also sum **coach + staff salaries** (they're real numbers on the contracts) into the
  wage bill. Today only player wages are summed. **S.**
- **10.2 — Membership / season-ticket revenue** as its own slow-moving stream (deferred) —
  distinct from matchday; grows with sustained success and a settled squad; `ClubBoard.FanSentiment`
  is the hook. **S.**
- **10.3 — A broadcast *deal* object**, not a per-season formula — a competition sells its rights
  for N years; the deal size scales with `Competition.Reputation` + participant market size;
  renewed with a news event; a competition that has risen in standing lands a bigger deal on
  renewal. Makes the franchise economy an actual negotiated thing. **M.**
- **10.4 — Player-sale profit/loss as a board KPI** — a club that trades well (buy low, sell high)
  is run differently from one that overpays; feeds board confidence and the transfer budget. **S.**
- **10.5 — Currency display** (Section J — deferred, presentation-only) — the `CurrencyLabel` is
  carried; a UI would use it. **note.**
- **10.6 — Prize money for *domestic* and *international* competitions surfaced as news** the way
  franchise prize money now is (follow-up added `FranchisePrizeMoney`; the others are silent). **S.**
- **10.7 — Testimonials / benefit years** are built (Slice 9.7) — extend to a **retirement
  testimonial tour** with multiple matches and a farewell news arc. **S.**
- **10.8 — Financial distress → forced sales** — a club deep in FFP trouble must sell its best
  asset (not just face a points deduction). A real, dramatic consequence. **S.**

## §11 — National teams & international cricket

**Real world:** a board runs the national side; a selection panel; central contracts; the FTP
(Future Tours Programme) of bilateral series + ICC events; rankings; a national coach on the line
every tournament.

**What we do:** `NationalBoard` (chairman-of-selectors quality, panel size, politicisation,
`CoachScrutinyMultiplier`), `NationalPoolService` (per-format pools, coverage slots),
`NationalSelectionService`, `SelectionPanelService`, `WorldSeeder.GenerateInternationalWorld` (a
small multi-country world + one World T20 Championship), club-vs-country
`UnavailabilityReason.InternationalDuty`, `CaptaincyAppointmentService` (format-specific,
international carries higher scrutiny), the follow-up national-coach career track
(`NationalCoachAppointed`/`Dismissed`, 1.6x pay, tournament-judged).

**Gaps — this is the thinnest of the "big" systems:**

- **11.1 — Only ONE international tournament exists.** No bilateral series, no Test series, no
  WTC-style league, no ODI World Cup, no Champions Trophy, no A-tours. The whole international
  calendar is a single biennial World T20. This is the largest single content gap in the game.
  Needs the FTP-shaped calendar (real data or generated patterns). **L.**
- **11.2 — No national-job MARKET.** A national coaching vacancy isn't advertised, isn't applied
  for, isn't negotiated the way `JobMarketService` does for clubs. A national board should run its
  own hire (with the politicisation factor shaping it). **M.**
- **11.3 — No national-board *objectives*** the way `BoardObjective` works for clubs — "reach the
  WTC final", "win a global event this cycle", "beat the No. 1 side away". `CoachCareerService`
  judges the national coach on the one tournament; give the board a structured multi-year target
  and a `ScrutinyFactor`-amplified verdict. **M.**
- **11.4 — Home-and-away, neutral venues, tour acclimatisation** — a touring side is worse early
  in a tour, better later; day-night Tests; a "home advantage" that's bigger for some nations. We
  have a single-match home edge; a *tour* arc is missing. **M.**
- **11.5 — Bilateral rivalries** (Ashes, India-Pakistan) with their own prestige, media heat, and
  a permanent trophy that changes hands. `Rivalry` exists for clubs — extend to nations. **S-M.**
- **11.6 — A `Country` entity** (deferred to Phase 10) — real national talent production, board
  investment in youth, country-specific conditions, a talent-profile that actually produces a
  pipeline. Today `AcademyService.NationTalentProfile` is a light role-mix skew. **L.**
- **11.7 — Associate nations as real teams**, not just a free-agent pool — a qualifier pathway, an
  Associate World Cup, the occasional giant-killing. **M.**
- **11.8 — Player eligibility / nationality switches** (Boyd Rankin, the Ireland/England moves) —
  a residency-qualification mechanic, rare and controversial. **S.**

## §12 — Franchise ↔ international ↔ domestic integration (CORRECTED per your instruction)

**Your rule: international cricket has first preference, ALWAYS. A franchise league never causes an
international series to be cancelled.** The earlier "series cancellation" idea is removed.

**The correct model to build:**

- **12.1 — An international window *always wins* a clash.** When an `International` window overlaps
  a franchise window (or a domestic season), the players in the national pool go on
  `InternationalDuty` and are **unavailable to the franchise / domestic side** — which then plays
  that round **weakened** (its best XI minus its internationals), or the franchise league
  **schedules its marquee fixtures outside the clash** where it can. The franchise/domestic
  competition absorbs the hit; the international fixture is untouched. `WorldClockService.SetInternationalDuty`
  already does the block — extend the franchise/domestic side's XI selection to cope, and let a
  franchise's *results* suffer for a round when its stars are on duty (a real, visible cost of
  building a squad around internationals). **S-M.**
- **12.2 — Central-contract tiers + NOC** (§5.6, §5.7) are the *board's* levers to protect
  international priority: a board can cap how many franchise leagues its contracted players do, and
  deny an NOC when a series is coming. The player weighs board money + international caps vs
  franchise money. **M.**
- **12.3 — The franchise league plans *around* the international calendar.** `FixtureGenerationService`
  for a franchise window should avoid scheduling on top of a known international window for the
  host nation's players, and accept that some overseas players will miss part of the tournament
  (they arrive late / leave early — a real thing). The franchise's auction plan should *value*
  a player partly by his availability (an out-of-contract or lightly-contracted overseas player
  is worth more to a franchise than a heavily-committed international). **M.**
- **12.4 — Domestic cricket is the *lowest* priority in a clash** — a domestic round during an
  international window or a franchise window is played by second-XI-strength sides (which is
  realistic and is *good for youth development* — young players get game time). Tie to §6 and §5.10. **S.**
- **12.5 — Real-world drawbacks to *improve* for the game (your ask):**
  - *Player burnout from year-round cricket* → model it as a genuine career-shortener and a
    selection tension (§6.4, §6.8), and let a board that manages it well keep its stars fit
    longer — a real competitive advantage.
  - *Franchise money distorting the domestic game* → let a domestic competition's standing/
    reputation genuinely *fall* when its best players chase franchise deals, and *rise* when a
    board protects it (central contracts) — a board strategic choice with a real payoff.
  - *The "meaningless bilateral series"* problem → give bilateral series a *context* (a rivalry
    trophy, WTC points, ranking points, a World Cup qualification path) so a dead 5th ODI has
    stakes — the fix real cricket keeps reaching for.
  - *Scheduling chaos* → a proper FTP (§11.1) that the game *respects* removes the chaos and makes
    the calendar a strategic object.

## §13 — Board & governance

**Real world:** owners/associations with ambition, wealth, patience; season budgets; takeovers;
FFP; a board that hires/fires the coach and sets objectives; a national board with a selection
panel and its own politics.

**What we do — deep.** `ClubBoard`, `BoardService` (season budget, fan sentiment, takeover
consideration, financial-health → coach confidence), `BoardRelationshipService` (monthly
`BoardConfidence`, mid-season sacking, job offers to the human, national-coach handling),
`BoardObjective` + `CoachCareerService.EvaluateObjectives` (structured, checkable targets per
contract year), `NationalBoard` + `SelectionPanelService`, `CountryProfile` (board-controlled vs
club-membership ownership).

**Gaps:**

- **13.1 — Ownership *events* over a career** — a benefactor buys in and floods the club with
  money; a penny-pinching board takes over and slashes the budget; an owner loses interest; a
  government/association reshuffle. `BoardService.ConsiderTakeover` exists — make it a richer arc
  with a real before/after. **S-M.**
- **13.2 — Board *factions* / a chairman with an agenda** — a board isn't monolithic; a powerful
  chairman can protect or undermine a coach against the rest. `NationalBoard.Politicisation` hints
  at this — extend to club boards. **S-M.**
- **13.3 — Fan protests / pressure groups** — sustained low `FanSentiment` produces a
  fan-protest event that raises the pressure on the board (and the board on the coach). **S.**
- **13.4 — Board objectives beyond results** — "develop 3 academy graduates", "stay within
  budget", "improve the ground", "reach a cup final for the gate money". A youth-leaning or
  cash-strapped board judges the coach on these too. `BoardObjective` has the shape. **S.**
- **13.5 — The Director-of-Cricket-imposed-by-the-board** move (§7.4) — a board that keeps the
  coach but strips his authority. **S.**
- **13.6 — Governance events** — a board bans a player for indiscipline against the coach's
  wishes; overrules a selection; forces a fire-sale; sacks the *whole panel*. Board–coach friction
  as a real, ongoing dynamic. **S-M.**

## §14 — Media, news, narrative, pundits

**Real world:** fragmented broadcasters each with a commentary team; conflicted ex-player pundits
("obliged to boost the incumbent"); national press with distinct characters (Australian vs Indian
press view each other with suspicion); a tournament *narrative* the media builds (breakout star,
captain under fire, the flop); awards ceremonies with leaderboards.

**What we do:** `NewsEngine` (event → category / prominence / kicker), `WeeklyDigest`,
`PressConferenceService` (storylines, 4 tones, `MediaHandling`, coach-chargeable),
`PunditService` (opinionated reactions to charged events), `PreMatchReportService` (unwired),
`RankingService`, auction preview/report/press conferences (follow-up), `TournamentAward`
(emerging player + find-of-the-auction).

**Gaps — news is currently a *list of events*, not a *story*:**

- **14.1 — A per-nation media-market model.** `MediaMarket` per country (Intensity, HomeBias,
  Volatility, tabloid-vs-broadsheet): scales `NewsEngine` prominence for that nation's teams/
  players, how sharply `PressConferenceService` storylines escalate, and how fast a losing run
  becomes a "sack him" story. Indian press ≠ Australian press ≠ English press. **M.**
- **14.2 — A season/tournament narrative tracker.** Storylines that *build and pay off*:
  "breakout star" (young player on a run → POTM → national call-up), "captain under fire" (losing
  run → hostile pressers → board statement → sack or reprieve), "the flop" (marquee buy failing →
  pundit pile-on → dropped), "the redemption" (§ the `PressureMoment` arc, surfaced as media).
  A `NarrativeService` opens/advances/closes storyline objects from the event stream and emits
  richer, *connected* news that feeds `PressureMoment` / `BoardConfidence`. **M-L. This is what
  "make it feel like a season with a story" means.**
- **14.3 — Live leaderboards as news** — Orange/Purple-Cap-style (most runs, most wickets, most
  sixes, a computed MVP) per active competition, published weekly and at season end with awards +
  morale/reputation bumps. **S-M.**
- **14.4 — Team of the tournament** (a computed best XI, at the awards ceremony). **S.**
- **14.5 — Pundit conflicts of interest** — a pundit is softer on his old club / old nation,
  harder on a rival; `PunditService` opinions carry a `formerAffiliation`. Characterful. **S.**
- **14.6 — A broadcast/rights layer** (§10.3) with prestige as well as money — a competition on a
  big network is followed more, which raises its reputation. **M.**
- **14.7 — Player media personas** — a `Player.MediaProfile` (outspoken / guarded / marketable);
  an outspoken player generates more stories (good and bad), a marketable one more commercial
  value (§9.5). **S.**
- **14.8 — The `PreMatchReportService` and `CommentaryService` wired in** (§0.4) — a pre-match
  conditions/form/head-to-head piece and a handful of match highlight lines, per fixture, as
  news. **M.**
- **14.9 — Fan reaction as a news thread** — social-media-style reaction to a signing, a sacking,
  a shock result, an auction verdict, feeding `FanSentiment` (§10.2, §8.8). **S.**

## §15 — Records, milestones, awards, Hall of Fame

**What we do — deep.** `RecordProgressionService` (marquee records with a previous-holder chain +
"stood for N years"), `MilestoneService` (caps, career runs/wickets, maiden century, career-best),
`AwardsService` (player of the month/year, batter/bowler/breakthrough of the year, team of the
season), `HallOfFameService` (career volume + standing + honours), `CareerStatsService` +
`CareerStatsAggregationService` (Cricinfo-style hierarchy), `GroundRecordsService`,
`PartnershipRecordsService`, honour boards.

**Gaps:**

- **15.1 — Persistence** — `RecordBook`, `HallOfFame`, `Awards` are `WorldState` collections with
  no repository (§0.2). They vanish on save. **(part of 0.2).**
- **15.2 — More record categories** — most sixes in an innings/career, fastest fifty/hundred,
  best economy, most catches by a fielder in a match, most ducks (the unwanted records real
  cricket tracks too). **S.**
- **15.3 — Milestone *ceremonies*** — a 100th Test cap presentation, a farewell guard of honour,
  a stadium-record celebration — as news moments with a morale/reputation effect. **S.**
- **15.4 — Head-to-head records between players** (X has dismissed Y 8 times) surfaced in the
  pre-match report and commentary — we track the matchup confidence; expose the *record*. **S.**
- **15.5 — All-time XI / decade teams** — a periodic "team of the decade" the media picks. **S.**

## §16 — Umpiring & officiating

**What we do — deep for what's built.** `Umpire` (accuracy, consistency, composure, LBW
judgement, `EffectiveJudgement`, `CareerAccuracy`, matches-by-format, controversies, panel
history), `UmpireService` (neutral-panel assignment by occasion, `OutBiasFor` — a weaker/rattled
panel gives more marginal LBWs/caught-behinds, widened by a loud crowd + a tense finish, bounded
1.0-1.22), `SeasonReview` (KPI-driven panel promotion/demotion, retirement).

**Gaps:**

- **16.1 — DRS** (your deferred call) — see §1.7.
- **16.2 — Match referees** — a distinct role from the umpire, running the code-of-conduct
  hearing (`DisciplineService` charges are currently self-resolving). **S-M.**
- **16.3 — Umpire fatigue / standing in consecutive matches** — a real workload the panel manages.
  **S.**
- **16.4 — A "howler" that decides a match** should feed a bigger news/controversy arc and a
  board complaint to the governing body (which can affect that umpire's assignments). **S.**
- **16.5 — Playing-condition variations** — different competitions with different rules
  (over-rate penalties, free-hit scope, boundary-count tiebreakers, Super Overs). Some are
  modelled; make them per-`Competition` config. **S-M.**
- **16.6 — Third-umpire calls for run-outs/stumpings** even without full DRS — a marginal run-out
  goes upstairs and the tight ones are 50/50. **S.**

## §17 — Competitions, fixtures, calendar, promotion/relegation

**What we do:** `Competition`/`CompetitionSeason` split, `CompetitionWindow` (recurrence, anchor
year, cross-year), `FixtureGenerationService` (circle-method round-robin, snake-draft groups,
greedy home/away balance), `PlayoffBracketService` (straight knockout + IPL-style),
`CompetitionProgressionService`, `CompetitionSeasonRunner` (advance a season, reschedule missed
fixtures, cancel after 3 attempts, promotion/relegation between two linked tiers, salvage from a
dead feeder), `CompetitionCalendarService`, `CompetitionRevenueService`, `CompetitionReputationService`.

**Gaps:**

- **17.1 — Only a 2-tier domestic pyramid, and only for one seeded structure.** A real pyramid
  (3-4 tiers, multiple divisions per tier) with promotion/relegation cascading. `WorldSeeder` +
  `Competition.SecondTierCompetitionId` generalised to a chain. **M.**
- **17.2 — A domestic *first-class* championship and a *List A* competition** alongside the T20 —
  the seeded world is T20-heavy. A real domestic season is 3 formats. **M.**
- **17.3 — Knockout cups** (a domestic 50-over cup, a T20 blast with a finals day) — more
  competition *variety*. **S-M.**
- **17.4 — Weather / monsoon windows** shaping the calendar (`CountryProfile.RainRiskMultiplier`
  exists — use it to shape *when* a competition is scheduled, not just how much rain a match
  gets). **S.**
- **17.5 — Expansion / contraction** — a franchise league adds a team; a domestic competition
  merges divisions; a new competition is founded. A living calendar. **M.**
- **17.6 — Real ICC FTP / Cricsheet import** (the external-data-layer job) — the seam is named
  everywhere; nothing real to import against until it's built. **L.**
- **17.7 — Fixture *congestion* as a real thing** — `WorkloadRotationService` reads the next
  fortnight; make a genuinely brutal schedule (3 matches in 5 days) a selection and injury
  pressure, and a board complaint. **S.**

## §18 — The player model (attributes, form, morale, relationships, personality, reputation)

**What we do — very deep.** ~60 attributes across batting/bowling/fielding/mental/physical;
`CurrentAbility`/`PotentialAbility` (1-200) with a hard clamp + 2 legit rise routes;
`FormatSuitability` (batting + bowling, per format, pressure-weighted); `FormState` (rolling
weighted, decays, confidence); `Reputation` (domestic/continental/worldwide tiers);
`PlayerMorale` + `CoachTrust` + `CaptainTrust`; `Matchups` (`MatchupConfidence` — opponent/ground/
bowler/partner/situation, recent+career blend, 3-sample threshold, decay);
`PartnershipChemistry`; `PressureMoment` (Stokes/Brathwaite arc); `PeakAgeOffset`; `PlayerExperience`
(counted, weighted, format-specific); `PersonalityTrait` flags; `PlayerAgeingService` (4 groups, 4
schedules); `SquadStatus` (8 tiers); `Injury` + history + permanent career-threatening cost.

**Gaps:**

- **18.1 — Player-to-player relationships beyond partnerships** — friendships, cliques, a
  senior-junior mentor bond, a genuine feud. `DressingRoom` + `MentoringGroup` are the foundation;
  extend to a small relationship graph that affects `DressingRoomHarmony`, run-out risk between
  two players who don't get on, and a "he'll only sign if his mate is there" transfer effect.
  Named as deferred across two passes. **M.**
- **18.2 — Leadership *material* below the captaincy** — a vice-captain pipeline exists; add
  "senior pro" and "dressing-room lawyer" roles that shape the room independent of the armband. **S.**
- **18.3 — Personality *development*** — a young hothead matures under a good senior/coach; a
  quiet player grows into a leader. `MentoringService` has temperament drift — make it a broader
  arc. **S.**
- **18.4 — Off-field life events** — a player has a child (a short dip then a lift), a family
  bereavement, a visa/legal issue, a religious observance (fasting during a day match — a real,
  respectfully-modelled effect). Rare, characterful, humanising. **S-M.**
- **18.5 — A `Loyalty` / `AmbitionDirection`** beyond the personality flag — where a player *wants*
  his career to go (a one-club legend vs a trophy hunter vs a globetrotter), which drives his
  transfer and contract behaviour coherently over a career. **S.**
- **18.6 — Confidence *contagion*** — a team on a high lifts a struggling player; a dressing room
  in crisis drags a good one down. Partly in `TeamMorale`; make it individual. **S.**
- **18.7 — "Bunny" / hoodoo grounds / a bogey opponent** — the matchup system supports it; surface
  it as a *narrative* ("he averages 12 at this ground") and let a player *overcome* it (a
  redemption sub-arc). **S.**

## §19 — Weather, pitch, conditions

**What we do:** `MatchWeatherService` (per-country climate profiles, seasonal cosine, monsoon,
altitude, `RainRiskMultiplier`), `GroundConditionsService` (par score, day-of-match deterioration,
dew, spin assistance), `Ground` (pace/spin/bounce/friendliness, boundaries, outfield, altitude,
floodlights, dew tendency, `PitchWearRate`), `PitchPreparationService` (a pre-match prep choice,
bounded, doesn't mutate the shared `Ground`), rough patches from actual bowling (§E),
`RainService` + DLS + `LostTimeRecoveryService`.

**Gaps:**

- **19.1 — Post-rain gradual transition** (deferred) — play resumes at the pre-rain baseline
  instantly; a lingering damp-pitch / two-paced difficulty state that fades. **S-M.**
- **19.2 — Ground-specific deterioration *rate* is `PitchWearRate` — good — but "which end / which
  bowler created the rough" is only lightly modelled**; a left-arm spinner exploiting a right-arm
  quick's footmarks on day 4 is a specific, famous thing. **S.**
- **19.3 — Home-team pitch *doctoring*** as a strategic choice with a risk — prepare a raging
  turner to suit your spinners and it can backfire if the toss goes wrong or your batters can't
  play spin either. `PitchPreparationService` has the shape; add the AI *choosing* it and the
  backfire. **S.**
- **19.4 — Drop-in pitches, used pitches, a fresh strip vs a re-used one** — venue-level detail. **S.**
- **19.5 — Extreme heat / a heat rule** (drinks breaks, a forced stoppage) — `MatchWeather` has
  temperature; make 45°C matter. **S.**
- **19.6 — Wind affecting swing and the short/long boundary** — `MatchWeather.WindSpeedKph` exists
  and is barely consumed. **S.**

## §20 — World simulation, longevity, the `Country` entity

**What we do:** `WorldClockService` (daily tick + weekly/monthly/quarterly/annual sub-ticks, each
with a deterministic RNG stream), `WorldSeeder` (fictional multi-country world + academies +
associates + 5 franchise leagues + contracts + country profiles), `RetirementService`
(multi-factor, format-specific), the retiring-player → coach loop.

**Gaps:**

- **20.1 — No `Country` entity** (Phase 10) — real national talent production, board youth
  investment, country conditions, a genuine pipeline. Today the world's player population only
  ages; the academy intake is the only new supply and it's per-club. Over 20+ seasons the world
  thins. **L.**
- **20.2 — Regen *quality* variance by nation and era** — a "golden generation" for one country, a
  fallow decade for another. **M.**
- **20.3 — The world clock has no *weekly* consumer beyond training** — a lot of life happens
  week to week (a mid-week cup match, a press cycle, a training block). `RandomForWeek` exists;
  under-used. **S-M.**
- **20.4 — Historical world state / an era model** — the game starts as a snapshot; a real career
  mode wants "what happened in 2019" as flavour and "the all-time greats" as a benchmark. **M.**
- **20.5 — Save-game longevity as an acceptance test** — CLAUDE.md runs 3-8 season integration
  tests; nothing verifies a 30-year career stays coherent (population, finances, competitiveness).
  Add one (it'll expose §20.1 fast). **S.**

---

# PART 2 — DEFERRED ITEMS FROM CLAUDE.md THAT ARE OVERDUE OR STILL OPEN

Cross-checked against the code. "Overdue" = the phase that was meant to do it is complete.

| Item | Where deferred | Status | This review |
|---|---|---|---|
| **Tech-debt 4 — `PerformanceRecordingService.Rate*` are a "first approximation"** | "Phase 4 should replace" | **OPEN.** Phase 4 done, not replaced. | §0.5 / §1.1 — **M**, do it. |
| **`AnalystService` wired into a live match** | Slice 6e / Post-6 §C / Phase 7 deferral | **OPEN.** Zero production callers. | §0.4 / §3.2 — **M**, highest realism-per-effort. |
| **`CommentaryService` / `PreMatchReportService` / `PostMatchAnalysisService` report / `PlayerDevelopmentReportService` / `ScorecardFormatter` consumed** | Slices 11/12, Wave 8, follow-up (b) | **OPEN.** All built, none consumed. | §0.4 — **M** to wire the set. |
| **`MedicalQualityBoost` / `AnalysisQualityBoost` threaded into consumers** | Post-6 §C, Phase 7/8/9 deferral lists | **PARTIALLY DONE (follow-up).** Medical now threads into the injury path; analysis boost has a param but no in-engine caller (blocked on `AnalystService` wiring). | §0.4 — finish with §3.2. |
| **Session-level in-match momentum** | Post-5 rectification deferral, Phase 7/9 | **OPEN.** `TakeBreak(BreakLength.Session)` dead since Slice 8. | §1.3 — **S**. |
| **Full weekly training calendar + 3-tier camps as an explicit schedule** | Phase 5 Part 1, Post-5, Phase 8/9 | **PARTIALLY DONE.** `TrainingWeekService` (seasonal modulation) + `TrainingCampService` (3 tiers) exist; the explicit per-player weekly schedule does not (blocked on a weekly sim tick). | §6.1 — **M** when the clock supports it. |
| **`Country` entity / real national talent production** | Phase 8, Phase 10 | **OPEN.** `NationTalentProfile` is a light skew. | §11.6 / §20.1 — **L**, Phase 10. |
| **Real ICC FTP / Cricsheet import** | Everywhere | **OPEN.** Seam named; nothing to import against. | §17.6 — **L**, external-data-layer. |
| **DRS** | Phase 7, your explicit call | **DEFERRED (by you).** | §1.7 — **M** when you want it. |
| **Coach-PLAYER relationships (vs captain-only)** | Post-4 survey, Post-5 | **OPEN.** | §4.8 — **M**. |
| **Player-player synergy beyond batting partnerships (bowler-pair *effect*, not just economy)** | Post-4 survey | **PARTIAL.** `BowlingPairSynergyService` does new-ball economy; the "pressure transfer" effect is not there. | §2.8 — **S**. |
| **Bilateral international series / FTP-shaped calendar** | Phase 6, Post-6 | **OPEN.** One tournament only. | §11.1 — **L**, biggest content gap. |
| **National-coach job MARKET + national-board objectives** | Post-6 "Phase 6 remaining", follow-up | **PARTIAL.** The career track exists; the market and structured objectives don't. | §11.2 / §11.3 — **M**. |
| **Over-rate fines to `Team.Finances`** | Slice 6.6 | **DONE (rectification §A).** ✔ | closed. |
| **`RankingService` / `PlayerOfTheSeries` driven by a real season loop** | Post-5, Phase 6 | **DONE.** `CompetitionSeasonRunner` drives both. ✔ | closed. |
| **`FormState.DecayTowardNeutral` / `MatchupConfidenceService.DecayAll` dead** | Morale slice, chemistry slice | **DONE.** Both wired into the annual/monthly ticks. ✔ | closed. |
| **`MultiDayMatchRecorder.Record`'s unseeded `random` default** | Post-Slice-14 note | **MITIGATED.** `FixturePlayService` always passes a seeded rng; a direct caller still gets `new Random()`. | §0 minor — give it a seed from `MatchDate`. **S.** |
| **Weekly `RandomForWeek` had no consumer** | Wave 1 | **DONE (Post-6 §C + follow-up).** Specialist individual coaching + training-week modulation. ✔ | closed. |

**Persistence (not a CLAUDE.md "deferred" but the biggest real gap):** §0.1 / §0.2 — no
`WorldState`↔`GameDataContext` bridge, ~19 collections unpersisted. **This is more urgent than any
feature below.**

---

# PART 3 — CONSOLIDATED PRIORITY VIEW

**Tier 0 — do before anything else (project can't ship/play without these):**
1. `git init` + `.gitignore` + `Directory.Build.props` (warnings-as-errors) + a one-line CI (§0.6).
2. A `WorldState`↔`GameDataContext` bridge + persist `WorldState` (§0.1, §0.2).
3. A thin `CricketManager.App` game loop (§0.1).

**Tier 1 — highest realism-per-effort, mostly wiring built things:**
4. Wire the match-presentation set: `AnalystService` + `AiTacticalPlanner` into the live match,
   `PreMatchReportService` + `PostMatchAnalysisService` report + `CommentaryService` highlights
   into the fixture flow and `NewsEngine` (§0.4, §3.1, §3.2).
5. Rewrite the `Rate*` functions with match-situation/pressure/momentum inputs (§0.5, §1.1).
6. Franchise auction archetypes + name-recognition bias (§8.1, §8.2).
7. Central-contract tiers + NOC — the whole international↔franchise tension in two mechanics
   (§5.6, §5.7, §12.2).
8. Per-nation media market + a narrative tracker — news becomes a story (§14.1, §14.2).
9. Situation-driven field aggression + left-hander-vs-leg-spin + declaration-by-quality (§2.1,
   §2.2, §2.4).
10. Coach worldwide earnings ledger + campaign fees + circuit reputation (§4.2, §4.3) — you asked
    for it, it's small.

**Tier 2 — vast planning/tactical/selection depth (your priority):**
11. `DelegationProfile` — one consistent human-authority model across every decision (§7.1).
12. The selection meeting + rationale + captain-on-panel + pre-series planning conversation (§5.1,
    §5.2, §5.3).
13. Cross-format workload contracts (§5.4).
14. ODI middle-overs contest + partnership style-fit + bowling-pair pressure transfer + reverse
    swing (§2.3, §2.8, §2.9, §1.2).
15. Conditions-and-opposition XI selection + the fuller combination problem (§3.3, §5.9).
16. `InMatchTacticalAI` — bowling changes when collared, part-timer when milced, batting-order
    shuffles (§2.11, §3.6).

**Tier 3 — content and world depth:**
17. Bilateral international series + an FTP-shaped calendar (§11.1) — the biggest content gap.
18. A domestic 3-format season + a fuller pyramid (§17.1, §17.2).
19. `Country` entity + real talent production + a 30-year longevity test (§20.1, §11.6, §20.5).
20. DRS, match referees, third-umpire run-outs (§1.7, §16.2, §16.6).

**Tier 4 — flavour, characterisation, and long-tail realism:**
- Everything else in Part 1: player relationships, off-field life events, ownership arcs, board
  factions, fan protests, dynasty tracking, image rights, buy-back clauses, milestone ceremonies,
  more record categories, pundit conflicts, media personas, weather/pitch detail, associate
  nations as real teams.

---

# PART 4 — HONEST BOTTOM LINE

**What's genuinely impressive:** the match engine's input richness; the tactical-plan override
layer and the human-authority model; the training/development depth; the auction rebuild; the
selection layer; the discipline/umpiring model; and above all the *discipline of the process*
(the tech-debt log, the determinism rules, the "verify before asserting" habit, the honest
"deferred with reasoning" everywhere). CLAUDE.md is the best project-memory document I've seen.

**What's genuinely concerning:**
1. **It is not a playable game or a runnable simulation app** — there is no entry point and no
   save/load. It's a very good library with a test harness.
2. **Saving would lose most of the world** — the persistence layer was never connected to the
   runtime object and ~19 collections have no home.
3. **A large, tested slice of work (the whole presentation layer + the analyst) delivers zero
   value** because nothing consumes it.
4. **The core rating function is still the Phase-2 approximation**, so form/reputation/awards/
   POTM all rest on a number that ignores the situation — in a game that's *about* situations.
5. **No version control** on a project this size, which is how the "mystery files" incident
   happened and will happen again.

None of these are hard to fix and none invalidate the work — but they should be Tier 0/1, ahead of
any new feature, and the roadmap doesn't currently frame them that way.

---

## Sources (research underpinning the real-world comparisons)

Auction: [Ministry of Sport](https://ministryofsport.com/decoding-the-ipl-auction-structure-valuation-and-high-value-acquisitions/) ·
[IPL 2026 team strategy](https://ipl2026india.in/ipl-auction-team-strategy/) ·
[CricMind Moneyball](https://www.cricmind.ai/news/moneyball-approach-ipl-auction-analytics-strategy) ·
[CricTracker – avoid bidding wars](https://www.crictracker.com/cricket-appeal/ipl-auction-strategy-why-smart-teams-avoid-bidding-wars-3934/) ·
[Credable – mega auction tactics](https://credable.in/insights-by-credable/business-insights/ipl-mega-auction-teams-tactics-and-the-economic-showdown/) ·
[Medium – rethinking retentions](https://medium.com/@g.aadityan/the-price-of-loyalty-rethinking-retentions-in-the-ipl-b46d30a15556) ·
[ESPNcricinfo – retention rules 2025](https://www.espncricinfo.com/story/ipl-2025-auction-retention-rules-six-retentions-per-team-right-to-match-returns-impact-player-to-stay-1452396) ·
[Cricket Resolved – accelerated round](https://cricketresolved.com/accelerated-round-in-ipl-auction/) ·
[Olympics.com – IPL scouting lab](https://www.olympics.com/en/news/sharda-ugra-ipl-scouting-process-talent-identification) ·
[Mystery Cricket – how IPL teams scout](https://mysterycricket.com/blogs/cricket/how-do-ipl-teams-scout-talent)
Selection / contracts / NOC: [India selectors – Wikipedia](https://en.wikipedia.org/wiki/India_national_cricket_team_selectors) ·
[Cricket Calculator – selection criteria](https://www.cricketcalculator.info/cricket-rules-and-terms/cricket-selection-criteria.html) ·
[Grokipedia – Captain (cricket)](https://grokipedia.com/page/Captain_(cricket)) ·
[Sportskeeda – workload management](https://www.sportskeeda.com/cricket/the-india-squad-is-selected-solely-based-on-workload-management-selection-committee-chairman-chetan-sharma) ·
[ESPNcricinfo – PCB central-contract criteria](https://www.espncricinfo.com/story/pcb-sets-minimum-international-appearance-criteria-for-new-central-contracts-1541718) ·
[ESPN – ECB central-contract overhaul](https://africa.espn.com/cricket/story/_/id/36083315/ecb-overhaul-central-contracts-system-response-growing-influence-t20-franchise-circuit) ·
[ESPN – PCB deny Usama Mir NOC](https://africa.espn.com/cricket/story/_/id/40240195/usama-mir-t20-blast-deal-pcb-deny-noc) ·
[Franchise impact on int'l schedules](https://coolattitudecaptions.com/the-impact-of-franchise-cricket-on-international-schedules/)
Coaching circuit: [Wisden – most successful franchise T20 coach](https://www.wisden.com/series/ipl-2026/cricket-news/flower-moody-voges-who-is-the-most-successful-head-coach-on-the-franchise-t20-circuit) ·
[ESPNcricinfo – most successful T20 coach](https://www.espncricinfo.com/story/who-is-the-most-successful-coach-in-men-s-t20-today-1450737) ·
[SportzSpark – highest-paid coaches 2026](https://sportzspark.com/highest-paid-cricket-coaches-in-the-world/) ·
[Gulf News – RR name Sangakkara DoC](https://gulfnews.com/sport/cricket/ipl/ipl-2021-rajasthan-royals-name-sangakkara-as-director-of-cricket-1.76705222)
In-match tactics: [Medium – master T20 strategies](https://dammikamahendra.medium.com/conquering-the-cricket-chaos-master-t20-strategies-like-a-pro-a70f058a1e24) ·
[Cricket Scoring – T20 strategy guide](https://cricketscoring.jathans.com/blog/t20-cricket-strategy-guide/) ·
[Sports Analytics @ Berkeley – powerplay](https://sportsanalytics.berkeley.edu/articles/powerplay-in-cricket) ·
[Mystery Cricket – the middle overs](https://mysterycricket.com/blogs/cricket/middle-overs) ·
[CricJosh – why captains declare](https://cricjosh.in/blog/why-captains-declare-test-innings-strategy-explained) ·
[First Monster – declaration strategy](https://firstmonster.co.uk/cricket-declaration-rules-innings-timing-strategy/) ·
[Gray-Nicolls – reverse swing](https://www.gray-nicolls.co.uk/blogs/the-game/what-is-reverse-swing-and-how-do-i-bowl-it) ·
[Britannica – cricket strategy & technique](https://www.britannica.com/sports/cricket-sport/Strategy-and-technique)
Partnerships: [Partnership (cricket) – Wikipedia](https://en.wikipedia.org/wiki/Partnership_(cricket)) ·
[Sportplan – running between wickets](https://www.sportplan.net/s/Cricket/improve-running-between-the-wickets.jsp) ·
[Cricket Bats – batting partnerships](https://cricket-bats.com/batting-partnerships/)
Media: [The Cricket Monthly – Australian vs Indian press](https://www.thecricketmonthly.com/story/1079122/peter-english-on-how-the-australian-and-indian-cricket-press-view-each-other) ·
[888sport – best commentators](https://www.888sport.com/blog/cricket/best-cricket-commentators) ·
[GSBF – cricket media & communications](https://certificates.gsbf.co.uk/guides/5107938/cricket-media-and-communications)
Awards: [ETV Bharat – IPL 2025 awards](https://www.etvbharat.com/en/!sports/ipl-2025-award-winners-list-orange-cap-purple-cap-fairplay-mvp-emerging-player-most-sixes-most-fours-enn25060401065) ·
[IPL.com – Emerging Player winners](https://www.ipl.com/cricket/news/ipl-2025-emerging-player-award-winners-list-from-2008-to-2025/)
