# HANDOFF — CricketManager

**You are picking this project up on a fresh Claude Code account / machine. Read this file first,
then `CLAUDE.md`. This file tells you how to get oriented and how we work; `CLAUDE.md` is the
authoritative record of *what* has been built and *what* is deferred.**

---

## 0. First 10 minutes — orientation, NOT re-verification

1. **Read `CLAUDE.md` from the top.** It is long (~9,900 lines) and it is the source of truth. You
   do **not** have to read every per-slice writeup on the first pass, but you **must** read:
   - the header sections ("What this project is", "Environment constraint…", "Session incident
     log", **"Phase status"** table),
   - **"CONSOLIDATED DEFERRED-ITEMS REGISTER"** (the single index of what is and isn't done),
   - **"Conventions"** (near the end),
   - the recurring-hazard notes: **"Standing hazard: unpopulated defaults read as negative
     signals"**, and every paragraph that mentions **determinism** / `HashCode.Combine` /
     `Guid.NewGuid` / `TimeOnly` wrap / "same seed, same world twice".
2. **Read `README.md`** — the public-facing summary of the same status.
3. `docs/plan-archive/` holds the written plans for the three most recent tickets
   (`melodic-weaving-garden.md` = the Meeting-Driven Selection ticket, `meeting-ticket-corrections.md`,
   `seven-suggestions.md`). Their reasoning is also folded into `CLAUDE.md`; the archive is there so
   you have the raw plan documents too.
4. `Full_Project_Review_Gaps_And_Suggestions.md` is a historical file-by-file review (the `§`
   references throughout `CLAUDE.md`'s deferred register point at it). Skim it only if a register
   row cites a `§` you need context on.
5. **Do a single `dotnet build CricketManager.sln -c Release` to confirm the tree compiles clean
   (0 warnings, 0 errors). That is all the verification you need to start.**
   **DO NOT run the full test suite just to "check the baseline."** It is already verified —
   **691/691 tests, two consecutive clean runs, 0 build warnings** as of this handoff. Running it
   takes ~5 minutes and proves nothing you don't already know. Start work directly.

---

## 1. What this project is

A Football-Manager-depth cricket **coaching / management** simulator (the player is a coach, not a
cricketer). Deep, probabilistic, data-driven simulation underneath; a 2D/textual match presentation
eventually. Built in **C# / .NET 10**, three projects:

| Project | Role |
|---|---|
| `src/CricketManager.Domain` | the entire simulation. **Zero external NuGet dependencies by design** — do not add one without a very strong reason. |
| `src/CricketManager.Data` | `SqliteRepository<T>` (a JSON-column hybrid over SQLite) bundled by `GameDataContext`; `WorldSeeder` (the fictional starting world). |
| `tests/CricketManager.Tests` | a **hand-rolled console `TestRunner`** (NOT xUnit — the early sandbox had no NuGet). One big `Program.cs`, one `TestRunner.Run(name, () => {...})` / `RunAsync` per test, appended before the final `Environment.Exit(TestRunner.Summarize())`. |

There is **no `git` repository and no version control** — this is a deliberate standing decision by
the project owner. Do not run `git init`. Config files like `Directory.Build.props` / `.editorconfig`
are planned to ride along with Phase 17.

### Build & test commands

```
dotnet build CricketManager.sln -c Release                        # must be 0 warnings
dotnet run --project tests/CricketManager.Tests -c Release --no-build
dotnet run --project tests/CricketManager.Tests -c Release --no-build -- --filter "<substring>"   # run matching tests only
dotnet run --project tests/CricketManager.Tests -c Release --no-build -- --trace                  # full stack traces on failure
```

The suite is ~5 minutes. `--filter` takes ONE case-insensitive substring (not a regex / not
pipe-separated). Primary shell is Windows PowerShell; a Bash tool is also available (each with its
own syntax). Repo lives at `e:\CricketManager_Post_Phase_5\CricketManager_Phase4_Slice14\CricketManager`.

---

## 2. How we work on this project — the rules that matter

These are distilled from ~30 sessions of history in `CLAUDE.md`. Follow them.

### 2.1 The Research → Plan → Implement gate (mandatory for any external ticket/directive)

When the owner hands you a ticket, a "suggestions" prompt, or a research document:

1. **Research every factual claim** against real sources (web search) — how real cricket
   administration / tactics / rules actually work. The owner explicitly wants: *"do not implement
   anything literally if research or the existing codebase gives you a better-grounded version —
   propose the best, most fruitful version."* Where you deliberately deviate from reality for the
   game's sake, **say so, with the reasoning** — never silently.
2. **Verify against the actual current codebase** with `file:line` references. Many register rows
   have been found *already built* on inspection (register staleness, not missing functionality) —
   check before you build.
3. **Write a plan** (to a plan file), covering: research findings with source links, codebase
   verification, conflicts found + the recommended resolution for each, a slice order, and a
   "deferred, with reasoning" list. **Present it and wait for the owner's approval before writing
   code.** (Use `EnterPlanMode` if you want the tool support; either way, do not start coding a
   large directive without a reviewed plan.)
4. Then implement in slices.

### 2.2 Implementation cadence for a multi-slice pass

The owner's standing instruction on recent large passes: **build ALL the logic first, then do ALL
the testing at the end** — not test-as-you-go. Tag each slice's edits with a comment so a later
failure traces back to its slice. Keep the Domain project compiling clean after each slice
(`dotnet build src/CricketManager.Domain/... -c Release`).

### 2.3 Determinism discipline — non-negotiable

A career sim that can't be reproduced from a seed can't be debugged. This project's history is
full of determinism bugs; here is the complete rule set:

- **Never seed simulation randomness from `HashCode.Combine`, an object hash code, `DateTime.Now`,
  `Guid.GetHashCode()`, or anything else that varies per process.**
- **Never let a `Guid` decide an order that RNG consumption depends on.** The classic bug:
  `foreach (var x in world.SomeCollection.OrderBy(x => x.Id)) { ...random.NextDouble()... }` — Guids
  differ between two runs of the same seed (`WorldSeeder` does not seed entity Guids), so the
  per-element draws desync. Order by a **stable** key instead: `(LastName, FirstName, DateOfBirth)`
  for players, `.OrderBy(t => t.Name)` for teams, insertion order for a `List<T>`.
- **`Dictionary.Values` / `HashSet` enumeration order is not stable across two runs** (bucket
  layout depends on the actual Guid keys). `world.Teams` is a `Dictionary<Guid,Team>` — iterate it
  as `world.Teams.Values.OrderBy(t => t.Name)` whenever you consume RNG per team, and when you dedup
  (e.g. one national team per country) pick the survivor by `.OrderBy(t => t.Name).First()`, never
  `.OrderBy(t => t.Id).First()` or bare `.First()`.
- **New RNG consumers go at a TAIL position** in their tick, after every pre-existing consumer,
  with the stream local and discarded — so nothing pre-existing shifts.
- **Use the right per-cadence stream.** `GameCalendar` provides `RandomForYear` / `RandomForMonth` /
  `RandomForQuarter` / `RandomForWeek` / `RandomForDay` / `RandomForFixture` / `RandomForAuction`.
  Each has its **own multiplier** so a weekly / monthly / daily / annual roll landing on the same
  calendar date never draws from the identical stream. A service that runs annually but wants its
  own isolated randomness (like `SkillRegressionService`, `RepresentationDriftService`) uses
  `new Random(date.Year * <prime> + <offset>)` — a fixed per-year seed — so it never perturbs the
  shared annual stream.
- New entities added mid-sim (`Guid.NewGuid()` ids) must be **appended** to `world.Coaches` /
  `world.Staff` / etc., never inserted in a Guid order.

### 2.4 Verification — what "done" means

**Before a pass is done: `dotnet build` clean (0 warnings) AND the full suite green across TWO
consecutive clean runs.** For a change that recalibrates a probability / a stochastic ball-model
term, run **3–4** consecutive clean runs (these can pass once by luck). If a determinism-sensitive
exact-count test flakes once, re-run — this codebase has a documented history of a few
fragile exact-count integration checks ("the extended world is still deterministic", "Phase 15/16
integration"). One flake that does not reproduce across isolated + multiple full runs, with **no
shared mutable static introduced and all new RNG consumption deterministic**, is a known fragility,
not your regression. A flake that reproduces is a real bug — hunt it (usually a Guid-ordered
iteration; see 2.3).

### 2.5 Other standing hazards (from `CLAUDE.md`'s own history)

- **"Unpopulated default read as a negative signal."** A system that consumes data a *later* phase
  produces, where the type's default is indistinguishable from a real bad measurement (win-rate 0 =
  "terrible form"; traits all-zero = "bad at everything"; 0 matches = "never picked"). Make the
  input **nullable, treat null as neutral**, never let "unknown" share a representation with "bad".
- **A clean build + green suite does NOT prove the tree is clean.** Dead code with no references
  passes both. Before adding a service, **grep for an existing one that does the same job** — this
  project has twice ended up with two complete parallel systems.
- **A plausible-looking wrong number needs *tracing*, not just a passing test.** Off-by-ones
  (bowler-spell numbering, `TimeOnly` subtraction wraps, partnership wicket-number) don't announce
  themselves — trace the logic against what the code actually does.
- **Isolate the variable you're testing.** Many test bugs here came from a scenario where two
  things varied at once, or a synthetic player whose attributes barely moved a weighted sum.
- **`PlayerSelectionEvaluator.Evaluate` is never purely attribute-driven** — reputation moves the
  score within a `SquadStatus`-gated ceiling. To compare two "identical" synthetic players, set
  both to `SquadStatus.Fringe` to neutralise that ceiling.

### 2.6 Docs — update at the end of every pass

- `CLAUDE.md`: add a full writeup section, add/update the **Phase status table row**, and update
  every affected row in the **CONSOLIDATED DEFERRED-ITEMS REGISTER** (mark items DONE with the pass
  name; correct stale rows).
- `README.md`: add/update the matching phase-table row.
- The plan file's status header.
- End your pass with a summary **in the owner's own register format** (tables: what shipped / what
  is deferred with a target phase for each), then STOP and wait for the next explicit command.

### 2.7 Communicating with the owner

- The owner sometimes writes in a mix of English and Urdu / with terse phrasing — interpret intent
  generously and act; ask only when a decision is genuinely theirs to make and you can't infer it.
- They value: honest scoping (name what's deferred and why), no overselling, "logic first then
  tests", thorough-but-not-padded responses.
- When they say "proceed" after you've asked questions, that is the go-ahead.

---

## 3. Phase status (as of this handoff)

**Everything through Phase 16 is COMPLETE**, plus every rectification / follow-up / sweep pass.
**691/691 tests, 0 warnings.**

| Phase | Status |
|---|---|
| 0 – 3 | Core architecture, player DB, teams/competitions/grounds/finances — **done** |
| 4 | Match Simulation Engine (ball model → multi-day → fixtures/playoffs, slices 1–15) — **done** |
| 5 | Coaching Systems + the 8-wave Post-Phase-5 Rectification Pass — **done** |
| 6 | AI Managers & World Simulation (slices 6.1–6.7) + Post-Phase-6 Rectifications — **done** |
| 7 | Board / Media / Finance depth (slices 7.1–7.9 + follow-ups) — **first pass done** |
| 8 | Training & Player Development (youth academy pipeline etc.) — **done** |
| 9 | Auction / Contracts / Market (slices 9.0–9.8) — **done** |
| Post-7/8/9 | Combined rectification + follow-up pass (4-level ICC code, 5 franchise leagues, the auction rebuilt as a strategic fight, loss-proof franchise finances, …) — **done** |
| Post-Phase-9 Wiring & Tech-Debt | match-presentation layer wired in; `Rate*` rewritten with `MatchSituation`; harness `--filter`/`--trace`; … — **done (588 tests at the time)** |
| 10 | World Simulation & International Cricket (`Country` layer, WTC/ODI Championships, bilateral trophies, central contracts + NOC, tour acclimatisation, 25-year longevity test) — **done** |
| 11 | Depth: Planning, Tactics & Selection (`DelegationProfile` authority model, the selection MEETING, fuller XI combination, full `AiTacticalPlanner`, `RunningCalling`) — **core done** |
| 12 – 14 | Media/Narrative, Market Depth, Player Life & Relationships — **core done** |
| 15 | Match Officiating, Conditions & Rare Events (**DRS**, Super Over, pitch doctoring, concussion protocol, match referees) — **done** |
| 16 | Records, Awards & Historical Flavour (record categories, ceremonies, all-time / team-of-the-era XI, golden generations, testimonial tours) — **core done** |
| Post-16 Completion Pass | 9 waves — tech debt #6/#9, holdouts, day-night Tests, `BroadcastDeal`, `FanReactionService`, salary cap, nationality switches, feud→run-out, … — **done (627 tests)** |
| Post-16-B Deferred-Items Sweep | six passes — nothing deferred against Phases 10–16 carries into Phase 17; the Match-Engine Tactical Pass closed roughly half the ~20 §2.x micro-tactics — **done (646 tests)** |
| Follow-up Passes 1–4 (post-sweep) | coach/board authority, player development & squad depth, market transparency, historical depth + the tactical remainder — **done (661 tests)** |
| Meeting-Driven Selection Ticket + Corrections | DoC & Impact-Player-*rule* removed; selection panel restructured as staff; franchise identity evolution; first-hand-knowledge auction weighting; franchise coaching corrected to year-round multi-year contracts; per-`Competition` `HomePitchInfluence` — **done (680 tests)** |
| Seven-Suggestions Pass + Cricket-Personnel-Careers | ICC revenue + light national finance loop (S5); Full-Membership grant, irrevocable (S7); representation drift with the 3-year stand-down (S6); retiree career pathways (NEW-C); ex-cricketer-only national selectors (NEW-B); multi-role head coaches (NEW-D); national `CoachingStructure` split/reunify (S2); real franchise staff contracts (NEW-A); ownership groups (S1); `Player.CommercialAppeal` (S3); match-fitness gate (S4) — **done (691 tests)** |
| Pre-Phase-17 closeout | NEW-D training-effect wiring; tech-debt list housekeeping (items 4/7/9 were stale — all done) — **done** |
| **17** | **Application, Persistence & UI — PLANNED, not started** |
| **18** | **Real-World Data Import — PLANNED, not started** |

---

## 4. What is deferred (nothing blocks Phase 17)

### 4.1 Phase 17's own scope (start here)

- **`WorldStateStore`** — persist the entire `WorldState` as one JSON document + a `Save(world, dir)`
  / `Load(dir)` bridge. Right now a save loses ~19 `WorldState` collections (news archive, record
  book, Hall of Fame, mentoring groups, captaincy profiles, planned rosters, development history,
  the market index, every in-season accumulator, …). MAY be pulled forward before the rest of
  Phase 17 if a save/resume workflow is wanted sooner.
- **`CricketManager.App`** — a headless console game loop (`new` / `advance` / `status`), the entry
  point the whole project has been building toward. Scriptable; a future UI drives the same code.
- **Build hygiene** — `Directory.Build.props` (warnings-as-errors), `.editorconfig`. (`.gitignore`
  is pointless without a repo; no `git init` — owner's call.) Also: stale `net8.0` artifacts under
  `tests/.../obj` (harmless, clean whenever).
- **A graphical UI** — its own long sub-track after the console app exists. Every prior phase
  shipped as "an API + a headless default" specifically so the test suite and a future UI drive the
  same path — keep that.
- **The live seeded-world domestic pyramid wiring.** The generic N-tier promotion/relegation
  generator (`WorldSeeder.GenerateTieredDomesticStructure`) is **built and unit-tested**, and
  `CompetitionSeasonRunner` already walks an arbitrary-length promotion/relegation chain. Wiring it
  into `GenerateInternationalWorld` (in place of the flat top-flight T20 cup per country) is a
  near-one-line change — **but it is BLOCKED on a determinism root-cause.** It reproducibly
  amplifies a latent, intermittent (~a few % per full-suite run) non-determinism first reachable
  ~2 sim-years in, in an iteration whose order depends on `Guid`s and that only becomes
  order-sensitive once several same-window domestic competitions per country exist. Two genuine
  contributing bugs were found and fixed along the way (`JobMarketService.GatherApplications` `.Id`
  ordering; the `FixturePlayService` / `CompetitionSeasonRunner` same-day fixture sorts gained a
  seed-stable competition-name tiebreak) and the generator now refuses a degenerate 2-club
  division — that narrowed the rate but did not provably close it. **Root-cause the residual
  non-determinism first** (a targeted 2-identical-seed repro harness building a pyramid world and
  running ~4 sim-years, then bisecting), then wire it in.

### 4.2 Small follow-ups that need infrastructure that doesn't exist yet

- **U19 / youth World Cup** — needs a national-youth-tournament concept.
- **The full weekly training calendar as an explicit per-player schedule** (+ 3-tier camps as a
  per-player schedule rather than the current `TrainingCampService` bulk mechanic) — needs a weekly
  sim-tick restructure. `TrainingWeekService`'s seasonal modulation is the honest core today.
- **Dedicated domestic knockout cups** and **domestic-calendar window *timing*** (as opposed to
  regional-weather *risk*, which is built) — a Phase 10 follow-up.
- **Milestone ceremonies as a full UI moment** — the data/event already exists (`CeremonyService`);
  only a UI to stage it is missing. Phase-17-blocked.

### 4.3 Match-engine follow-ups (tracked in the register)

- The honest short remainder of the §2.x in-match micro-tactics (see the Post-16-B sweep's own
  closing list in `CLAUDE.md` — §2.6/§2.11/§2.12/§3.6 were closed in Follow-up Pass 4; a couple
  more are "effectively covered" by pre-existing mechanisms).
- Third-umpire tech nuances, footmark-rough refinements, etc. — all in the register with reasoning.

### 4.4 Phase 18

Real-world data import: real players / teams / competitions / grounds + the ICC Future Tours
Programme + Cricsheet match history, as a DATA swap against seams already named everywhere — not an
engine rewrite. Also the home of the domestic **draft** league type.

---

## 5. EXPLICITLY REMOVED FROM SCOPE — do NOT build these, do NOT put them in a plan

- **Full mid-innings Impact Player substitution** (swapping a specific named player out of the XI
  once an innings is under way). **The project owner has deliberately cancelled this feature — they
  do not want this functionality.** The roster-depth version already in the codebase
  (`FixturePlayService.WithImpactPlayer`, which inserts a genuine 12th squad player into the batting
  order / attack for IPL/BBL-style leagues) **stays as-is and is correct** — that is not the same
  thing. Do not build the live substitution, do not "flag it as deferred", do not re-raise it.
- **"Timed out" (§1.8 dismissal type)** — removed from scope per an earlier owner call. Not built,
  not deferred.
- **`git init` / version control** — the owner's explicit standing decision. Do not add it.
- **A `Country` entity** — the roadmap originally named one; it lives on `CountryProfile` (the
  per-nation record every service already reads) and a parallel entity would just duplicate the key.
  Do not create a `Country` entity.

---

## 6. Architecture pointers (so you know where things live)

- **`WorldState`** is defined in `src/CricketManager.Domain/Services/WorldClockService.cs` (a
  `sealed class`, not its own file). It holds every collection the sim mutates.
- **`WorldClockService.AdvanceDay`** is the daily tick. It fans out to `ProcessWeeklyTick` /
  `ProcessMonthlyTick` / `ProcessQuarterlyTick` / `ProcessAnnualRollover`, plus per-day steps
  (`FixturePlayService`, `CompetitionSeasonRunner.Advance`, contract expiry, vacancies, the news
  archive). `ProcessAnnualRollover` is where ageing / retirement / academy intake / the ICC
  revenue distribution / representation drift / coaching-structure review / etc. all run.
- **`WorldSeeder.GenerateInternationalWorld`** builds the fictional multi-country starting world
  (national teams + pools, per-country domestic 3-format seasons, 5 franchise auction leagues, an
  international calendar with the WTC / ODI Championship / bilateral trophy series / Associate
  Qualifier). `GenerateStarterWorld` is the older single-country MVP path (still used by tests).
- **`FixturePlayService`** is the season engine — it turns a scheduled `Fixture` into a played
  `MatchResult` (weather, XI selection, tactical plan, the sim, the recorder, the Phase-7 hooks).
- **`CompetitionSeasonRunner`** owns the season lifecycle (draw the knockout, complete the season,
  distribute revenue + broadcast + prize money, player-of-the-series, promotion/relegation, create
  next year's editions).
- **Match engine**: `MatchSimulator` / `MultiDayMatchSimulator` (simulate — never mutates the
  world) are kept separate from `MatchRecorder` / `MultiDayMatchRecorder` (record — writes the
  permanent state). `BallOutcomeModel` + `InningsSimulator` are the ball-by-ball core.
- **Persistence**: `SqliteRepository<T>` (one `(Id TEXT PRIMARY KEY, Json TEXT)` table per entity
  type, `System.Text.Json`) bundled by `GameDataContext`. Deliberately a JSON-column hybrid, not a
  relational mapping — the trigger to promote it would be a query that needs a SQL `WHERE` on a
  nested field, and nothing does that today.
- **Deterministic RNG**: `GameCalendar` carries a `WorldSeed` fixed at career creation and provides
  the per-cadence `RandomFor*` streams (see 2.3).
- **The world is a SNAPSHOT AT A DATE** — nothing may read the machine clock. A career starting in
  2026 loads data as of that day and simulates forward.

---

## 7. Recommended first move

Confirm the build is clean, then either:
- **Start Phase 17** with `WorldStateStore` (the highest-leverage single piece — it makes the whole
  thing a saveable game), following the Research → Plan → Implement gate and presenting a plan; or
- if the owner points you at the **domestic-pyramid determinism** first, build the 2-seed repro
  harness, bisect, fix, then wire the pyramid in — and run the suite **twice consecutively clean**
  (that one warrants 3–4 runs given it's a determinism fix).

Whatever you do: update `CLAUDE.md` + `README.md` + the plan file at the end, run the suite twice
clean, and hand back a register-format summary.
