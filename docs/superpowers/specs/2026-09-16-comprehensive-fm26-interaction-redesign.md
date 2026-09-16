# Comprehensive FM26-Referenced Interaction & Layout Redesign (2026-09-16)

A full, evidence-based audit of the "Cover Point" UI mockup's interaction layer and
navigation structure, done at the user's explicit direction: "reaudit comprehensive... each
and every micro, macro interaction... put fm26 ui ux as a reference... interaction layer is
very weak... layout structure is not optimised." This supersedes the narrower
`2026-09-13-cover-point-design-review.md` (which audited visual CRAFT - palette, type scale,
spacing - and found that clean) with a much deeper INTERACTION and STRUCTURE audit, which
found real, serious problems the craft review's lens could never have caught.

**Nothing in this document has been implemented yet.** It is the design/plan artifact itself,
written before any code changes, per the brainstorming skill's own gate for work at this scale.

## Method

Three parallel research passes, each independently verified against the live file/code
(not recited from memory or from prior CLAUDE.md summaries):

1. **Exhaustive interaction audit** of the entire ~3MB mockup - every screen, every subtab,
   every one of 528 `onclick` handlers, every `<select>`/`<input>`, every modal - tracing
   the actual JS function bodies rather than assuming markup implies function.
2. **Deep FM26 research** - reading Sports Interactive's own manual and detailed guides
   (not surface summaries) on the real interaction mechanics: contract negotiation,
   player conversations, tactics layout, scouting, press conferences, dynamics, board
   interaction, and FM26's own stated UI redesign philosophy.
3. **C# domain cross-reference** - confirmed real, built game mechanics in
   `src/CricketManager.Domain/Services/` with zero representation anywhere in the mockup
   (27 confirmed items), plus a direct check for incomplete/stubbed LOGIC in the domain
   itself (not just missing UI) - see Part G.

Also re-read the prior `2026-09-13` review and its own implementation plan
(`2026-09-14-cover-point-review-fixes.md`) - and confirmed, by grepping the live file, that
**neither of its two concrete fixes (A1: context-switcher/onboarding mismatch, A2: Fixtures'
single-competition gap) was ever actually implemented**, despite being written up and marked
approved. Both are folded back into this plan (Part F) rather than left stale a second time.

---

## Part A - Critical correctness bugs (foundation; block other work)

These are not design choices - the mockup currently contradicts itself or silently no-ops.
Everything in Parts C-H assumes these are fixed first, since several new features (Part H)
would be built on top of the same broken Player Profile mechanism otherwise.

### A1. Player Profile only ever shows Hamza Malik's data below the header

`openPlayerFromRow`/`openPlayerByName` update the name, breadcrumb, and role-line for any
player. `applyPlayerProfileData(name)` additionally patches header vitals (CA/PA stars,
value, wage, status/interest pills, fitness) - but **only for the 10 names hand-entered in
`PLAYER_PROFILES`**. For every other player in the game - the entire rest of the ~40+ name
roster, every academy prospect, every opposition and international player - the header falls
back to a generic "Squad player / Domestic" reading (a real, deliberate, working fallback -
see `applyPlayerProfileData`'s own `if (!p) { ... return; }` branch), which is honest as far
as it goes.

**But the profile BODY - Position & Role Fit ladder, all ~21 attribute rows, the radar chart,
Pros & Cons, Playing style notes, all four Career Stats tables, the Contract tab - never
updates for ANY player, including the 10 who DO have header data.** Every profile, for every
player in the game, shows Hamza Malik's own 18 Technique / elite-against-spin attribute
block and his exact career figures underneath whatever header just changed. This is the
single most consequential bug found - it is also why several of Part H's new features
(personality development, technical-flaw remediation, skill regression, matchup confidence)
have nowhere honest to live until it's fixed.

**The fix.** Generalize `PLAYER_PROFILES` from a 10-entry hand-authored table into a real
per-player data model covering every named player in the game (attributes, career stats by
format, contract terms, bio/personality notes) - reusing the exact SHAPE Staff Profile
already proves out (`renderStaffAttributes`, `STAFF_ROLE_ATTR_FOCUS`): procedurally derive
a full, plausible, internally-consistent attribute/stats set from a name-seeded hash plus
each player's own already-established headline facts (squad status, role, central-contract
tier) rather than hand-authoring dozens more entries by hand. `applyPlayerProfileData` (or a
new `applyFullPlayerProfileData`) then re-renders every section of the body from that
generated record, not just the header. Staff Profile is the correct existing template for
this - it is the one "profile" screen in the whole file that is genuinely dynamic per-person.

### A2. `openNegotiation('contract')` is hard-wired to "Hamza Malik"

The contract-offer modal's subtitle is a literal hard-coded string
(`'Hamza Malik · Top-order batter...'`), regardless of which player's Actions menu opened it.
Fix: read the currently-open profile's name/role (the same data A1 makes reliable) at the
point `openNegotiation` is called, for all its sub-modes (contract/transfer/role/praise/
criticize) - confirmed only `contract` was checked directly, but the same fix should cover
every sub-mode since they all likely share the same hard-coded assumption.

### A3. Squad's own roster doesn't reconcile with itself

Squad's panel header claims "18 players - 2 unavailable," but the First Team table renders
**4 rows** (Hamza Malik, Rizwan Sheikh, Umar Baig, Danish Raza). Tactics' "Full squad" table
- which should be the identical 18-man squad - lists **14** different names (11 XI + 3
reserves), agreeing with Match Day's lineup-check table but not with Squad. The Academy
subtab adds a third, non-overlapping small roster (Danish Iqbal, Bilal Nasir, Hamza Sultan).
None of these three lists sum to, or agree on, "18."

**The fix.** Define ONE real, canonical 18-name Islamabad Icons senior squad (data, not
markup - a `SQUAD_ROSTER` array, reusing every name already established across the file so
nothing already-referenced elsewhere becomes orphaned) and render Squad's First Team table,
Tactics' Full Squad table, and Match Day's lineup-check table all FROM that one array, each
applying its own view-specific columns/sort - never three independently hand-authored tables
that can drift apart, which is exactly how they drifted apart the first time.

### A4. Board confidence disagrees across screens (71% vs 78%)

Portal's ring/meter and Club > Boardroom's "Confidence in you" meter present themselves as
the same underlying number and show two different values. Fix: one shared
`BOARD_CONFIDENCE` value (a JS variable, not a value baked separately into two SVG
`stroke-dashoffset`/width calculations), with both displays computed from it.

### A5. The Compare modal is hard-coded to two fixed players

`openCompare()` just toggles the modal open with no data injection; its own body text
promises "pick a different player from Squad to compare against instead" with no control
that does that anywhere in the modal. Fix: `openCompare(playerName)` called with the
CURRENTLY open profile's name as the first player, a real `<select>` (populated from the
same canonical roster A3 introduces) for the second, re-rendering both columns from the
per-player data A1 introduces.

---

## Part B - Layout & navigation structure (the "not optimised" complaint)

### Current state, confirmed directly

11 flat top-level sidebar/navbar buttons (Portal, Squad, Training, Tactics, Recruitment,
Club, Dynamics, Franchise, Fixtures, International, Records, World), visually clustered into
4 thin-divider groups (Overview / Squad / Club Affairs / Competition) but with **no actual
dropdown/flyout behaviour** - every button is always rendered, all in one row. Subtab depth
per screen varies widely: Tactics and Fixtures have none; Squad and Club Boardroom-cluster
have 2-4; Recruitment and International both have 5.

### What FM26 actually does (from this session's deep research)

FM26's real navigation is NOT a deep mega-menu system - it is close to what this mockup
already attempts: a compact set of primary destinations, each opening its own row of
subtabs specific to that destination. FM26's own stated redesign philosophy for its newest
UI is **"headline number immediately, real depth exactly one click away"** (the Tile → Card
system) - the problem to fix here is not the NAVIGATION PATTERN itself, it is (a) too many
same-weight top-level destinations sitting in one flat row with no grouping payoff beyond a
visual divider, and (b) uneven subtab depth that makes some destinations feel thin (0
subtabs) and others feel like their own sub-app (5 subtabs).

### Recommendation (my call, reasoning below - open to adjustment)

**Merge Dynamics into Squad as a third subtab** (First Team / Academy & Youth / Dynamics).
Reasoning: Dynamics is fundamentally about the SQUAD's own psychology (hierarchy,
relationships, issues) - it manages the same 18 players Squad already manages, just a
different lens on them. This also directly closes Dynamics' own "shallow relative to the
rest of the file" finding (Part D) by giving it a richer shared context instead of trying to
pad it out in isolation. Net: 11 top-level destinations -> 10.

**Leave Training as its own top-level destination**, not nested further under Squad -
Training already has 3 subtabs of its own (Focus/Individual/Camps); nesting it two levels
deep under Squad (Squad > Training > Focus) adds real navigation depth for no clarity gain,
and FM26 itself keeps training as its own primary area.

**Open question, not yet decided**: whether Records should become a fourth subtab of World
(Competitions/Nations/Clubs/Records), since both screens organize competition/club history.
I'm NOT recommending this one as confidently as the Dynamics merge - Records also carries
genuinely personal content (Hall of Fame, career leaderboards) that reads more like a
"player" concept than a "world" one, so collapsing it might make World feel overloaded
rather than World feeling more complete. Flagging this explicitly as a real open call rather
than deciding it unilaterally.

**Deepen, don't flatten, the two 5-subtab screens** (Recruitment, International) - both
already follow real category boundaries (Recruitment: browse/shortlist/plan; International:
overview/squad/contracts/staff/fixtures), and FM26 itself has areas this dense (its own
Transfers hub has a comparable number of distinct sub-areas). The fix here is depth-quality
inside each subtab (Parts D/F), not fewer subtabs.

---

## Part C - Interaction design principles (the "weak interaction layer" complaint)

Rather than treat every finding in Parts D-F as an independent one-off fix, these are the
STANDING RULES this mockup should hold itself to from here on - the actual fix for "the
interaction layer is weak" is having a real, consistently-enforced design system for
interaction, not a growing list of individually-patched screens that will drift again the
moment a new screen is added. Every fix in this plan is an INSTANCE of one of these six
principles; stating the principles here is what stops the next screen from reintroducing the
same problems.

1. **Every real entity name is a real link.** A player, staff member, team, nation, or club
   name that has actual data behind it must use the established clickable convention
   (`.staff-row.clickable`/`openPlayerFromRow`, `.who.clickable`/`openPlayerByName`, or the
   world-profile equivalent). A bare text mention of a real entity is always a bug, not a
   style choice - confirmed by how consistently this convention already IS followed
   everywhere except the specific gaps in Part E.
2. **A settable thing needs a real control, not just a label.** A tier/level/status badge
   that the coach can genuinely change in the real domain (training emphasis, a workload
   priority, a squad-size preference) must have a real, wired control. If the real domain
   mechanism is genuinely automatic (confirmed, not assumed - e.g. `TrainingCampService`),
   the honest fix is to STATE that plainly, never to fake a control that does nothing.
3. **A list past ~5 items earns a filter or sort, never just a longer scroll.** Every
   existing filter/search in the file (Player Database, transfer history, ICC rankings,
   World's nation/club search, record categories) already proves this pattern out - it
   should be the default expectation for any list that grows, not an occasional add-on.
4. **Every negotiation-shaped interaction follows the same shape**: an opening position, a
   response that COUNTERS rather than flatly refuses (this session's FM26 research: "every
   AI response pattern is 'no, but here's a counter,' never a flat rejection" - true of
   player interactions, contract talks, AND board budget requests in the real game), and a
   resolution. The file already does this well for facility/budget requests and the target-
   offer modal; Part G extends it to the contract-negotiation modal and incoming transfer
   offers, which currently break the pattern.
5. **Every modal closes the same way.** Backdrop-click-to-close is the established
   convention; one exception (the Fixture Summary modal) breaks it and should be fixed as a
   matter of consistency, not treated as a design choice.
6. **A "profile" screen is genuinely per-entity, or it says it's illustrative.** Staff
   Profile is the standard to hold every other profile-style screen to (Player Profile,
   Manager Profile, the Compare modal) - Manager Profile is a legitimate, stated exception
   (there is only one manager), but Player Profile and Compare are not.

---

## Part D - Dead/decorative controls (real interaction promised, nothing behind it)

Confirmed by tracing the actual JS, not assumed from markup:

- Recruitment > Transfer Activity: incoming transfer-offer accept/reject buttons have a
  `title` only, no `onclick` - contrast with the fully-wired outgoing flow.
- Match Day Live: the entire playback bar (prev/pause/skip, scrub track, autoplay-speed
  select) and the "Show me: Fours/Sixes/Wickets/..." filter checkboxes - no handlers, not
  referenced anywhere in script.
- Match Day Live + Tactics (same dead button in two places): the per-player "Change" button
  on the batting-plan rail rows - no `onclick`.
- Training > Team Focus: the 5 emphasis tier badges have no click-to-change control at all,
  on a screen whose entire purpose is setting emphasis; the delegate checkbox has no
  `onchange` (contrast with the near-identical, REAL delegate toggle on Player Profile).
- International > Pool & Squad: the "preferred squad size" number input and "hold selection
  meetings" checkbox - no handlers.
- Player Profile: the workload-priority `<select>` - no `onchange`.
- Portal's "Needs your attention" task list (3 items, e.g. "Renew Hamza Malik's contract") -
  none of the three items are actionable from where they're shown, despite each one
  describing a concrete action a real control elsewhere in the file already performs.
- The Fixture Summary modal's backdrop does not close on outside-click (Principle 5 above).
- Onboarding's Manager Profile stage: name inputs and the nationality/playing-background
  selects are all decorative (no `id`, values never read).

---

## Part E - Convention-consistency gaps

Places the established clickable-name convention (Principle 1) is inconsistently applied:

- Squad's "Squad depth by role" card lists real player names as plain text, unlike the
  near-identical "pecking order" card on Dynamics, which DOES wrap names correctly.
- World > Competitions' league table - plain text, while the Nations/Clubs subtabs one click
  away, and the Competitions list right above the table, are fully linked.
- Records' flat "Career leaderboards" table - plain text, while the record-detail MODAL's own
  rows (reached from the same screen) are correctly clickable.
- International > Fixtures & Trophies - fixture rows aren't clickable, unlike the domestic
  Fixtures screen's `openFixtureSummary` pattern.
- Records' Hall of Fame entries (Club and World) - static, no click-through, despite naming a
  specific player.
- Player Profile's Contract tab "Interested clubs" text - not linked via the established
  `openWorldProfile('club', ...)` convention used one tab over on World.

---

## Part F - Carry-forward from the never-executed 2026-09-14 plan

Confirmed still missing by grepping the live file directly - not assumed from the old spec:

- **The context switcher doesn't respect the onboarding career choice.** `finishOnboarding`
  still just calls `setContext(ctx)`; the topbar's Club/Franchise/International pills remain
  a free three-way toggle regardless of which path Onboarding said was chosen. Fix as
  originally decided: gate the switcher's visible pills by the chosen path; "Continue" (the
  full demo save) keeps all three.
- **Fixtures shows only the T20 Cup**, despite the World screen's own directory establishing
  three parallel domestic competitions (First-Class Championship, List A Cup, T20 Cup) plus
  the franchise league and the Crescent Trophy. Fix as originally decided: a competition-tab
  strip at the top of Fixtures (reusing the existing `.subtab-btn` pattern), defaulting to
  whichever is in season.
- Two bare `0.8rem` font-size declarations on the live scorecard (should be `var(--fs-small)`)
  - trivial, bundle into whichever other edit touches that area.

---

## Part G - FM26-informed mechanic upgrades

1. **Rebuild the contract-negotiation modal as a real staged flow** (once A1/A2 fix its
   per-player data): fee/term -> wage, with a genuinely separate AGENT-FEE lever (FM26's own
   pattern - raising the agent's cut can close a deal without raising the player's actual
   take-home pay, since the agent has his own incentive to push it through) -> clauses. The
   existing slider+clause-toggle mechanism is the right foundation; this is a restructuring
   of its steps and one new lever, not a rebuild from scratch.
2. **Every negotiation-shaped interaction counters instead of flatly refusing** (Principle 4)
   - extend the facility/budget-request pattern's "counter, don't just refuse" shape to the
   contract-negotiation modal and to incoming transfer offers once D's dead buttons are wired.
3. **A "Don't Judge" board mechanic** (genuinely new, not in the file today) - the coach can
   proactively ask the board not to hold a specific competition/objective against him,
   trading upside (no credit if it goes well) for downside protection (no confidence hit if
   it goes badly). A real, distinct strategic choice on Club > Boardroom, alongside the
   existing budget-request pattern.
4. **Team Talk shows the assistant's own tone recommendation inline**, next to the 3 tone
   buttons - FM26's own pattern, and a small, cheap addition to an already-real mechanism.

---

## Part H - New features from confirmed domain gaps (27 items, grouped by natural home)

Every entry below is a REAL, already-built C# domain mechanic (confirmed via direct code
read, not inferred) with zero UI representation anywhere. Grouped by where it naturally
belongs so related additions land together rather than as 27 disconnected one-offs. Priority
order follows: cheapest/most natural first, larger/new-screen items last.

**Player Profile additions** (all depend on Part A1's per-player rebuild landing first):
`ConfidenceContagionService` (a morale ripple from being in a squad on a high/in crisis),
`PersonalityDevelopmentService` (a trait genuinely evolving over a career),
`FlawRemediationService` (a scouted technical flaw a specialist coach works on),
`SkillRegressionService` (a one-format specialist's technique in the OTHER format fading),
`MatchupConfidenceService` (a real "confidence vs this opponent" read),
`RepresentationDriftService` (international-allegiance eligibility/switch status).

**Match/news-feed additions**: `RecordProgressionService`'s live "record broken" framing for
a result just played (not just the static Records browse screens); `NarrativeService`'s
building storylines as something the user can actually see accumulate, not a single
throwaway reference; `PunditService`'s opinion columns layered on factual news;
`UmpireService`'s controversy/reputation arc as a visible moment after a big-match howler.

**Club/Board additions**: `ForcedSaleService` (a genuine distress fire-sale, the FFP banner
is the natural anchor), `FanReactionService`/board takeover (a protest under bad results +
low sentiment, a club changing hands), `InterimCoachService` (an assistant stepping up as
caretaker, possibly earning permanent promotion).

**Franchise/Auction additions**: `FirstHandKnowledgeService` (a coach/captain/teammate's real
insider read on an auction target, distinct from reputation/stats), `FranchiseTradeService`
(confirm whether Trade Centre already covers this fully or is a different, thinner mechanic
- verify before building, don't assume a gap that's already closed).

**Captaincy/leadership**: `CaptaincyGrowthService` (a captain's own tactical-call quality
growing from matches actually captained, bounded by his ceiling) - belongs on whichever
player IS captain's own profile, once A1 lands; the vice-captain succession pipeline already
exists as a static role tag and should gain the same growth arc.

**International additions**: `FullMembershipService` (an associate nation's real path to Test
status - World > Nations is the natural home), `CoachingStructureService`'s split/reunify arc
(International > Staff & Board).

**Board/market additions**: `JobMarketApplicationService`'s candidate-initiated half (any
coach/staff can apply to a vacancy - Job Centre already has the coach-side view; this is the
"who else applied" half), `CompetitionLifecycleService` (a domestic league's shape expanding/
contracting over years - World's Competition Profile), `IccRevenueService` (the ICC's annual
distribution to member boards - a National Board finance view, doesn't exist yet).

**Match-day/conditions additions** (lower priority - this mockup's frozen match is a single
T20, so multi-day-specific mechanics have less natural surface here): `MatchAmbitionService`
(session-by-session ambition in a MULTI-DAY match), `LostTimeRecoveryService` (rain-lost-over
recovery), `PitchDoctoringService`'s backfire risk as a live match event (the pre-match
REQUEST control already exists on Tactics; this is the "it back-fired" follow-through),
`PreMatchReportService`'s toss-independent conditions/par-score briefing.

**Records addition**: `AllTimeXiService` (media periodically names a "Team of the Era").

---

## Part I - C# domain logic gaps

A direct check of the domain code itself for incomplete/stubbed logic (as distinct from Part
H's "built but not shown" gaps) - grepped `src/CricketManager.Domain`, `.Data`, `.Api`, `.App`
for `NotImplementedException`/`TODO`/`FIXME`/`HACK`/suspicious placeholder returns, and spot-
checked 8 services' actual method bodies against their own doc-comment claims.

**Result: no genuine incomplete or stubbed logic found anywhere in the domain.** Every
`return null`/`return 0` early-return found is a legitimate, commented guard clause (e.g.
"nobody eligible," "not newsworthy," "already carrying an injury"), not unfinished work. The
domain's own documentation discipline (dated, reasoned deferrals throughout CLAUDE.md) held
up under direct verification - this is a genuinely complete, correctly-behaving codebase, not
a claim taken on faith.

**One real finding: two stale rows in CLAUDE.md's own CONSOLIDATED DEFERRED-ITEMS REGISTER.**
Both the franchise "Impact Player" roster mechanic and the "Director of Cricket" role are
marked **DONE** in that register - but both were later **deliberately removed** from the
domain entirely (confirmed: zero occurrences of either in `src`), per CLAUDE.md's own later
"Meeting-Driven Selection Ticket" section, which documents the removal in detail. The
register's older rows were simply never pruned after that later deletion pass. **Confirmed
this does NOT leak into the mockup** - grepped `cover-point-mockup.html` directly for both
terms, zero hits, so there is no UI-vs-domain inconsistency to fix, only a documentation
housekeeping item: correct the two register rows to reflect the removal, folded into this
plan's execution as a small, standalone fix (no code change, no testing needed beyond a
grep-confirms-clean check).

---

## Prioritized execution order

1. **Part A** - the five foundation bugs. Nothing else should be built on top of a Player
   Profile that shows the wrong person's data.
2. **Part F** - the two carry-forward items from the never-executed prior plan, since they're
   already fully specified and approved once; closing them now stops a second stale cycle.
3. **Part B** - the Dynamics-into-Squad merge (the one confident recommendation).
4. **Part D** - wire the dead controls.
5. **Part E** - the convention-consistency pass.
6. **Part G** - the FM26-informed mechanic upgrades (negotiation restructure, Don't Judge,
   tone recommendations).
7. **Part H** - the new domain-grounded features, in the grouped order given above.

Each wave gets its own structural verification (tag-count parity, div-nesting scan,
duplicate-id scan, `node --check`) and live Playwright verification before being documented
in `CLAUDE.md` and committed - the same discipline every prior pass in this project has used.

## Open questions for the user

1. **Records into World?** (Part B) - not recommended with confidence either way; a real
   call to make explicitly rather than deciding unilaterally.
2. **Franchise Trade Centre vs `FranchiseTradeService`** (Part H) - needs a direct check
   before assuming a gap; may already be closed.

## Self-review

- **Placeholder scan**: Part I is the one deliberately incomplete section, marked as such
  and will be filled before this doc is treated as final.
- **Internal consistency**: the execution order in the final section matches each Part's own
  stated dependency (A before everything; F is independent and cheap, pulled forward; B/D/E
  are all independent of each other and of G/H).
- **Scope check**: this is a genuinely large initiative - each numbered wave is its own
  implementation pass with its own testing, not one giant edit.
- **Ambiguity check**: every fix states what "done" looks like concretely; the two Open
  Questions are the only genuinely undecided items, and they're named as such rather than
  silently assumed.
