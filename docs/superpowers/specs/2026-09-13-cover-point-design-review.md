# Cover Point — Full Project Design Review (2026-09-13)

A ground-up design audit of the "Cover Point" C# cricket-management sim and its HTML UI
mockup, done at the user's own explicit direction ("poora project, code, mockup har cheez
ko detail se dekho... skills use karo... design mein kya cheezein incorrect hain... plan
banao aur decision lo"). Everything below was verified live against the actual
`docs/ui-mockup/cover-point-mockup.html` file and `CLAUDE.md`'s own dated history in this
session — not recited from memory. Where I have made a call rather than asking, I say so
and give the reasoning; a short list of genuine open decisions is at the end.

## Scope and method

- Re-read `CLAUDE.md`'s FM26 reference-pass findings (breadcrumb trails, the Player Report
  layout, dual Combined/In-Possession/Out-of-Possession toggles, the National Shortlist/Pool
  funnel split, dense grid-dashboard club screens — commits `722f31b` through `d587d91`).
- Re-read the full reference-asset catalogue (`docs/external_game_reference/
  extracted_combined/GUIDE.md`) and cross-checked its "already live"/"not used yet" claims
  against the actual file with `grep` (found genuinely stale in one place — see Finding 6).
- Spot-read six screens directly (Portal, Fixtures, Match Day's Analysis sub-tab, the
  Tactics bowling-plan control, Onboarding, and the central-contract authority work from
  earlier today) to catch issues invisible from documentation alone.
- Grepped for every reference-asset category the guide names to confirm current usage
  precisely (a repeat of, and extension to, the audit already in `SESSION_HANDOFF.md`).
- Invoked the `frontend-design` skill's own critique lens (palette coherence, typographic
  hierarchy, spacing/border-radius discipline, the checklist of common AI-generated-design
  tells) and applied it as an audit against the mockup's actual CSS — reading the `:root`
  token block, then grepping for every one of the skill's named red flags (hardcoded hex
  bypassing the token system, raw font-sizes bypassing the type scale, uniform border-radius,
  decorative arrow-appended CTAs, monospace misused for non-tabular text) to check whether
  they genuinely apply here or were checked and found not to. See Part F.

## Part A — Design inconsistencies and things built the wrong way

These are genuine structural/logic issues, not missing polish. Ranked by how much they
undermine the honesty of the demo.

### A1. The context switcher doesn't respect the onboarding career choice

**The bug.** Onboarding's "Career Setup" screen is explicit and correct: Club Manager /
Franchise Manager / International Manager / Unemployed are **mutually exclusive** starting
paths, each described as genuinely different jobs ("no transfer market, no academy" for
International; "no academy, no transfer market" for Franchise). `finishOnboarding(ctx,
label)` calls `setContext(ctx)` — but `setContext()` only changes which sidebar TABS are
visible for the *current* selection; it does not touch the topbar's own Club/Franchise/
International context-**switcher** pills, which remain a free three-way toggle regardless
of which path was chosen. A coach who explicitly started as "Club Manager" (no franchise
job, per the onboarding screen's own copy) can still click "Franchise" in the switcher and
see a fully-populated, real-looking Franchise menu — auction history, campaign coaching, a
squad — which directly contradicts what onboarding just told them about their own career.

**Why it happened.** The switcher was built later (the "multi-domain context switcher"
session) as a way to *demonstrate* all three identity views without needing real per-viewer
role-state — a reasonable shortcut for a static demo save. Onboarding was built even later
and was never reconciled with it.

**The fix, recommended**: gate the switcher's visible pills by which roles the CURRENT
save's coach actually holds, defaulting to all three only for the "Continue" (resume the
existing full demo save) path, where showing every identity is the whole point of the demo.
A path chosen via "New Career" should start with only its own pill visible; a second role
(a club coach later also taking a franchise job) is exactly the kind of moment the Job
Market / Recruitment screens already model and could genuinely ADD a pill to the switcher
when it happens — closing the loop between onboarding's own promise and what the switcher
actually shows. Small, contained fix (a few lines in `finishOnboarding`/`setContext` plus
one new function for "a role was added mid-career").

### A2. Fixtures shows only one of the world's three domestic competitions

**The bug.** The World screen's own competition directory establishes (and always has)
three parallel domestic competitions for the club's country — the First-Class Championship,
the List A Cup, and the Pakistan T20 Cup — plus the Franchise league and the Crescent
Trophy. The Fixtures screen, though, only ever shows T20 Cup fixtures (a league table, an
opposition report, and a 10-row schedule), with no competition switcher at all. A real club
plays several competitions across a season; Fixtures currently pretends it plays one.

**The fix, recommended**: a light competition-tab strip at the top of Fixtures (reusing the
`.subtab-btn` pattern already used everywhere else — Squad's First Team/Academy split, the
Recruitment hub, Club's four subtabs), defaulting to whichever competition is actually in
season. Genuinely additive, no redesign of the existing table/report/schedule cards needed
— they just need to read from whichever competition is selected instead of always T20 Cup.

### A3. No FM-style "dual toggle" for tactical planning has ever been built

FM26's real pattern (noted in the original reference pass, never revisited since) is a
Combined / In-Possession / Out-of-Possession three-way toggle over the WHOLE tactics
screen. The cricket-native translation isn't a literal copy (football's possession states
don't exist in cricket) — but Tactics DOES already have a genuinely close cricket-native
equivalent: the New Ball / Middle Overs / Death Overs phase toggle (built in an earlier
pass, confirmed live), which reshapes the bowling AND batting plan cards together exactly
the way FM's toggle reshapes attacking/defensive instructions together. **This is not a
gap — it's already the right adaptation**, just never explicitly connected back to the
FM26 finding that inspired it. Worth stating plainly in `CLAUDE.md` so a future session
doesn't "rediscover" this as missing.

### A4. Two different "manage a player's status" interaction shapes, and that's fine — but should be stated as a deliberate split, not an accident

The central-contract Tier/NOC "Manage" control (built today) is a small, focused popup;
the National Squad call-up/drop flow is a large, inline, whole-screen builder. Reviewed
directly against each other: this split is actually correct, not inconsistent — a
Manage popup is a quick, single-player, always-available adjustment; a squad announcement
is a genuine multi-player, date-gated, once-per-window event. The shapes should differ
because the REAL actions differ in scope and cadence. No change recommended — flagged only
because the user asked specifically whether anything reads as "should be structured
differently," and this was worth checking rather than assuming.

## Part B — Genuine gaps (should exist, doesn't)

Distinct from Part A: these are missing pieces, not built wrong.

### B1. Reference-asset gaps (verified live via grep this session)

| Asset | Status | Where it belongs |
|---|---|---|
| `cc2014/icons_shots/` (13 real shot-type icons) | 0 occurrences | The Match Day Analysis wagon-wheel, which is real and well-built (colour-coded scoring rays from the crease, confirmed at line ~1670) but has no shot-TYPE legend — a natural addition, not a rebuild. |
| `cc2014/icons_balltypes/length/` + `.../movement/` | 0 occurrences | The Tactics bowling-plan control — confirmed still a plain `<select>` (`#bowling-plan-select`, line 2444), exactly matching the guide's own long-standing recommendation (real icon content + FM23's real carousel/picker layout pattern). |
| `cc2014/umpire_signals/` (12 real animations) | 0 occurrences | A big-moment flash on Match Day (a wicket, a six) — doesn't exist in any form today. |
| `cc2014/icons_weather/` (8 real tiles) | only "sunny" live | Match Day's header always shows the identical weather regardless of fixture/season. |
| FM23 `ui_icons_skins/` sport-agnostic set | not evaluated | An open aesthetic call, not a gap — see Part C. |
| CC2014's real wording data (`bowling_plans.csv`, `field_setting_names_and_descriptions.txt`, `injury_descriptions.txt`, `sample_news_wording.txt`) | never adapted in | Legitimate now under the corrected borrow-and-adapt policy; the mockup's own self-authored copy for these exact things has never been revisited with this newer freedom. |
| FM23 `multi_collapsable_box` pattern | never built | The mockup has NO collapsible-section pattern anywhere — confirmed by code search. Franchise, International's Staff & Board subtab, and Recruitment have all grown dense enough that this is worth adding as a real, reusable component rather than continuing to grow flat cards. |

### B2. The panel-background and icon-style decisions are still genuinely open

Named in the guide since the original extraction pass, never made: CC2014's flat navy +
real corner art (live everywhere today) vs FM23's real gradient panel texture
(`skin_0017.png`); the current custom SVG icon sprite vs FM23's real, clean sport-agnostic
icon set. These are real aesthetic calls with no "correct" answer from the domain side —
see the Decisions section.

### B3. The two newer franchise leagues have no auction/retention/trade cycle of their own

Indian Masters League and Southern Blaze League exist as directory entries + a profile
only (deliberately scoped light when added). The Pakistan Premier League has the full
cycle (War Room, EOI, Pre-Auction, live bidding, Trade Centre). A real, still-open scope
question — see Decisions.

## Part C — What the current screen inventory actually covers (and what's genuinely done)

21 screens total, verified by grep this session (11 persistent sidebar tabs + 10 off-nav
click-through/takeover screens — the full list is already recorded in `SESSION_HANDOFF.md`
and won't be repeated here). **No new screen is missing from the inventory** — every major
game-management surface named across this whole project's build history (squad, tactics,
transfers, club finances/board/staff, franchise auction cycle, fixtures, international
duty/contracts/coaching, records, world/competitions, inbox, match day, onboarding,
calendar) has a real screen. The work remaining is depth and polish inside what exists
(Parts A/B above), not new top-level screens — worth stating plainly since the user asked
directly "screens kitni rehti hain" (how many screens remain): **the answer is zero new
top-level screens are needed; the remaining work is entirely inside the 21 that exist.**

## Part F — Professional design-craft audit (applying `frontend-design`'s own critique lens)

The honest verdict, checked line by line against the skill's own checklist rather than
asserted: **the existing token system is genuinely well-composed, and it has held up under
real build pressure across many later sessions** — not the generic-AI-design outcome the
skill exists to catch. Evidence, not opinion:

- **Palette**: every base colour is grounded in the actual subject (deep floodlit-stadium
  greens for ground/surface, warm off-white ink rather than pure white, gold as the trophy/
  honours-board accent, ball-red warmed toward leather maroon rather than a generic alert
  red, teal as the secondary interactive tone) — none of the skill's five named
  AI-design clusters apply (not cream+terracotta, not near-black+single-neon-accent, not
  broadsheet hairlines, and — checked specifically below — not the SaaS-card-kit or the
  template-chrome cluster either).
- **Type scale**: a real 9-step system (`--fs-micro` 0.64rem through `--fs-hero` 2.1rem),
  three named font ROLES (display/body/mono) rather than one face doing everything, and
  monospace is used **only** for genuinely tabular data (scores, overs, timers, fees,
  dates) — confirmed by grepping every `font-family: var(--font-mono)` use-site (20+
  checked, zero misuse for ordinary labels). This is the textbook CORRECT use of a
  monospace face, not the skill's "monospace for small data labels as decoration" tell.
- **Border-radius**: genuine hierarchy (3px on tight chips up through 16px on hero cards,
  999px on pills) — the opposite of the skill's "SaaS-card-kit: one radius on everything"
  tell, confirmed by sampling 40 declarations across the file.
- **Token-system discipline across newer screens**: grepped the ENTIRE file for any inline
  `style="..."` containing a hardcoded hex colour that bypasses the token system — found
  exactly **one**, across ~2.8MB and dozens of screens built over many sessions
  (`background:#0c1424` on a pitch-map delivery-outcome dot, almost certainly a deliberate
  neutral marker colour rather than drift). Grepped every raw `font-size:` declaration
  bypassing the `--fs-*` scale - 14 total, all but two are SVG chart-label pixel sizes
  (correctly pixel-precise inside a fixed viewBox, not a token violation) or the Onboarding
  splash screen's own oversized wordmark (a deliberate, earned hero-moment exception, the
  skill's own "spend your boldness in one place" principle in action). **The two genuine,
  minor exceptions**: the live scorecard's overs-count suffix uses a bare `0.8rem` twice
  where `var(--fs-small)` (0.78rem, functionally identical) belongs — cosmetic, one-line
  fix, not urgent.
- **The "template chrome" checklist, checked specifically, not assumed**: `.eyebrow`
  (tracked-out ALL-CAPS micro-labels) and middle-dot meta strings (`A &middot; B`) ARE used
  pervasively — literally the skill's own named red flags. **Judged NOT to be a flaw here**:
  both are the real, confirmed FM23/CC2014 convention (broadcast-graphic category labels
  and meta strings are genuinely how this exact subject matter is presented in the real
  reference games this project is deliberately emulating), not a generic default reached
  for on any brief — the skill's own text explicitly carves out this exception ("legitimate
  for some briefs... follow the brief's own direction where it's pinned down"). Arrow usage
  is minimal and functional (4 `&rarr;` total, all genuine state-transition labels like
  "Tier C &rarr; Tier A", never a decorative appended CTA arrow) - not the tell either.

**One real, if minor, craft note**: the `0.8rem` instances above are the only concrete
token-discipline fix this audit found. Everything else checked came back clean. This is
worth stating plainly to the user rather than manufacturing findings to fill out a
checklist — the base design system does not need a rework; it needs the functional fixes
in Parts A/B and the reference-asset adoption in Part B/C, not a restyle.

## Part D — Prioritized plan

1. **A1 (context switcher / onboarding mismatch)** — highest priority; it's the one finding
   that makes the demo actively say something false about the coach's own career. Small,
   contained fix.
2. **A2 (Fixtures single-competition gap)** — second priority; also small, and closes a
   real, visible inconsistency with the World screen's own established competition list.
3. **The bowling-plan picker rebuild** (B1) — highest-leverage reference-asset work; touches
   a screen already core to the app, and is the guide's own single most-recommended
   combination (CC2014 icons + FM23 layout pattern).
4. **The wagon-wheel shot-type legend, weather variety, and the `multi_collapsable_box`
   pattern for dense screens** (B1) — real but lower-urgency enhancements.
5. **A big-moment umpire-signal flash on Match Day** (B1) — a genuine "delight" addition,
   lowest priority of the reference-asset items since nothing is currently wrong without it.
6. **Revisit self-authored copy against CC2014's real wording data** (B1) — an ongoing
   enhancement pass, not a one-time task; do opportunistically alongside other work.
7. **Make the two open aesthetic calls** (B2) — needs the user's decision first (see below),
   then a single, deliberate pass rather than per-screen drift.
8. **Decide the two newer franchise leagues' depth** (B3) — needs the user's decision first.
9. **The two bare `0.8rem` declarations** (Part F) — trivial, bundle into whichever other
   Match Day edit happens next rather than a standalone pass.

## Part E — Open decisions (need the user's call, not mine to make silently)

1. **A1's fix scope**: gate the context switcher by onboarding choice (recommended), or
   leave it as an always-available three-way demo toggle and just note the inconsistency
   in `CLAUDE.md` as an accepted simplification?
2. **Panel-background style**: keep CC2014's flat navy + real corner art (current, live
   everywhere), or trial FM23's real gradient texture (`skin_0017.png`)?
3. **Icon style**: keep the current custom SVG sprite, or adopt FM23's real clean
   sport-agnostic icon set for some/all status chips?
4. **The two newer franchise leagues**: build them a full auction/retention/trade cycle
   like PPL, or keep them profile-only by design (a real, permanent scope decision, not a
   deferral)?

## Self-review (per the skill's own checklist)

- **Placeholder scan**: no TBD/TODO left in this document.
- **Internal consistency**: Part D's priority order matches Part A/B's own severity framing
  (inconsistencies before enhancements; cheap high-leverage items before expensive
  low-leverage ones).
- **Scope check**: this is a review-and-decide document, not an implementation spec for any
  one item — once decisions land, the specific fix each item needs (A1, A2 especially) is
  small enough to skip a further design round and go straight to implementation via the
  established mockup-editing discipline; B1's larger items (the bowling-plan picker) may
  warrant their own short design note when picked up, not because this review is
  insufficient but because the exact icon-picker interaction is worth a quick sketch first.
- **Ambiguity check**: every recommendation states what "done" looks like concretely enough
  to implement without re-asking.
