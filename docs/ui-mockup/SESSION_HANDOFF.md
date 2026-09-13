# Cover Point — Full Project Status, Gap Review & Handoff (2026-09-13)

Paste this whole file as your first message in the new session (on the other account) so
Claude Code picks up exactly where this one left off. It is written the way this project's
own compacted-session summaries are written — read it the way you'd read one of those. This
version was substantially expanded, on the user's own direct instruction, into a real
**full-project status + gap review + forward plan** — not just a procedural "how we work"
note. Everything verified below was checked live in this session (a real `dotnet build`,
grep-based screen/asset inventories, reading actual source files), not just recited from
`CLAUDE.md`'s own claims.

## What this project is

Two things, not one:

1. **`CricketManager` — the C# domain/simulation project** (root of this repo). A deep,
   probabilistic cricket-management simulation. `CLAUDE.md` at the project root is its
   single source of truth — every phase's design decisions, every bug found and fixed,
   every tech-debt item, all recorded chronologically and in enormous, load-bearing detail.
   **Read it before touching any C# code.**
2. **`docs/ui-mockup/cover-point-mockup.html`** — a single-file HTML graphical-UI mockup
   for the game, under active development as Phase 17's UI sub-track. Large (~2.76MB),
   edited exclusively through one-shot throwaway Python find/replace scripts, never the
   Edit tool directly. Its own build history lives in dated entries inside `CLAUDE.md`
   (search "GRAPHICAL UI MOCKUP" and read forward).

Both live in the same `CLAUDE.md` file — the mockup's history is simply appended after the
C# domain project's own phase history, in the same document.

---

## PART 1 — C# domain project: verified current status

**Build**: `dotnet build CricketManager.sln -c Release` run live this session —
**succeeded, 0 warnings, 0 errors**, all five projects compiled
(`CricketManager.Domain`, `CricketManager.Data`, `CricketManager.App`,
`CricketManager.Api`, `CricketManager.Tests`).

**Tests**: `CLAUDE.md` records 699/699 passing as of the end of Phase 17 Part 1. A full
live re-run was kicked off in this session (`dotnet run --project tests/CricketManager.Tests
-c Release --no-build`) to confirm this is still true — **check whether that run's result
was captured before this handoff was finalized; if not, re-run it yourself before trusting
the count** (it takes a few minutes — the suite includes multi-year/multi-decade
integration and determinism tests).

**Phase status** (per `CLAUDE.md`'s own phase table, cross-checked against the actual
project references in `CricketManager.sln` this session):
- Phases 0 through 17 Part 1 (`WorldStateStore` + `CricketManager.App`, the headless
  console game loop): **DONE**.
- The graphical UI (the mockup, Part 1 above): **IN PROGRESS** — this is the actively
  worked-on part of the project right now.
- Phase 18 (Real-World Data Import — real players/teams/competitions/grounds, the ICC FTP,
  Cricsheet match history): **PLANNED, not started.**

### A genuine, significant finding: `src/CricketManager.Api/` — undocumented, uncommitted

This is important and needs **your decision**, not a silent assumption either way.

`git status` shows `src/CricketManager.Api/` as untracked, and `CricketManager.sln` as
modified (a real `dotnet sln add` operation added the project — confirmed via `git diff`,
not just a stray folder). **A full grep of `CLAUDE.md` for "CricketManager.Api" or "Phase
17 Part 2" returns zero results** — this project does not exist anywhere in the project's
own documented history, despite being real, working, well-designed code that **builds
clean** as part of the solution.

**What it actually is**, confirmed by reading the source (584 lines across 7 files):
- A real ASP.NET Core Minimal API + SignalR backend. `Program.cs` wires CORS for
  `http://localhost:5173` (a Vite dev-server port — implies a real JS frontend, built or
  planned, that isn't this HTML mockup).
- Self-documented via XML doc comments as **"Phase 17 Part 2"**, further broken into named
  slices (`S1`, `S4`, `"Track A"` are referenced directly in `MatchReplayHub.cs`'s own doc
  comment) — implying a real, thought-through plan existed for this work. **No matching
  plan file was found** in this repo's `docs/plan-archive/` or in `~/.claude/plans/` on
  this machine — if such a plan exists, it's not here.
- `GameSession.cs` / `GameSessionStore.cs`: a resident, in-memory game session for a UI
  (distinct from `CricketManager.App.AppCommands`' per-invocation CLI shape) — genuinely
  reuses the same primitives (`WorldStateStore`, `WorldSeeder`, `WorldClockService`) rather
  than reinventing them. New game / load game / advance, all real.
- `Dtos.cs`: a rich, **already-aligned-with-the-mockup** set of response contracts —
  `SquadRowDto` and `PlayerProfileDto` in particular map almost field-for-field onto what
  the HTML mockup's Squad table and Player Profile screen currently hardcode as static
  illustrative JS data (ability/form/sharpness as a **tier label + bar percent**, exactly
  the mockup's own `.facility-tier-badge`-style convention; grouped attribute arrays by
  discipline; a `CareerStatsRowDto` shaped like the mockup's own `table.career`).
- `QualitativeProjectionService.cs`: a real, well-reasoned design decision (its own doc
  comment: **"design decision 3c"**) — raw `PotentialAbility`/`CurrentAbility` numbers are
  **never** served directly; everything routes through a scout-uncertainty projection
  (reusing the domain's own `ScoutingAccuracyService.EstimatePotentialAbility`) so a
  frontend structurally cannot leak the ground-truth number a real manager wouldn't know.
  This is a genuinely good, considered piece of design.
- `PlayerProjection.cs`: builds the DTOs from real `Player`/`WorldState` domain entities —
  not stubbed, not fake data.
- `MatchReplayHub.cs`: a SignalR hub, deliberately scoped to just the connection lifecycle
  (join/leave a match "room") — its own comment states the real ball-by-ball streaming is
  a later slice ("S4"), waiting on a data layer that doesn't exist yet. Honest, not
  over-built.
- **Live endpoints** (`Program.cs`, confirmed by grep): `POST /api/games` (new game),
  `POST /api/games/load`, `GET /api/games/{id}`, `POST /api/games/{id}/advance`,
  `GET /api/games/{id}/search`, `GET /api/games/{id}/teams`,
  `GET /api/games/{id}/fixtures`, `GET /api/games/{id}/teams/{teamId}/squad`,
  `GET /api/games/{id}/players/{playerId}`. That's game lifecycle + global search +
  team/fixture lists + **one genuinely deep screen** (Squad + Player Profile) — an honest
  first vertical slice, not a half-built sprawl. Tactics, Recruitment, Club, Franchise,
  International, Records, World, Inbox, live Match Day simulation, and the Auction Room
  have no endpoints yet — expected for a first slice, not a defect.

**Why this matters for the plan below**: this is not abandoned or accidental code — it is
a genuine, working start on turning the mockup's illustrative screens into a real
data-backed application, and its DTO shapes already assume the mockup's own qualitative
(tier + bar) display conventions. **Per this project's own standing incident-log
discipline** ("investigate before deleting or overwriting, as it may represent in-progress
work" — `CLAUDE.md`'s own "Session incident log" section), it was deliberately left
untouched and uncommitted throughout this session, exactly as the prior session's own
summary already flagged it ("pre-existing unrelated changes"). **Do not commit, delete, or
silently start building on top of this in the new session without asking the user
directly**: is this real, current work (maybe from a session on the account being switched
away from, or the new one?) that should now be reviewed, documented in `CLAUDE.md` as a
real "Phase 17 Part 2," and committed? Or is there a reason it's been left out that isn't
visible from the code alone?

---

## PART 2 — UI mockup: verified current screen inventory

Confirmed by grepping the actual file this session (not recited from memory). **21 total
`<section class="panel" id="panel-...">` screens**:

**11 persistent sidebar tabs** (context-aware — `data-context="club"` / `"franchise"` /
`"international"` tabs only show when that identity is active via the topbar context
switcher): Portal, Squad, Tactics, Recruitment, Club, Franchise, Fixtures, International,
Records, World, Inbox.

**10 off-nav screens**, reached only by click-through, a takeover, or a topbar action —
never a persistent sidebar destination: Match Day (the full immersive live-match takeover),
Auction Room, Trade Centre, EOI Meeting, Pre-Auction Meeting (the last four are the
franchise-league auction-cycle screens, reached from the Franchise tab's own dated
timeline), World-Profile (a Nation/Club profile redirect from World), Player Profile (a
redirect reached only by clicking a name anywhere in the app — there is deliberately no
standalone sidebar "Player" tab), Staff Profile (same pattern, for backroom staff),
Onboarding (New Game / Load Game / manager creation / job market — the very first screen,
reached by clicking the crest), Calendar (a real month-grid, reached from the topbar date).

This is a genuinely deep, mature mockup — see `CLAUDE.md`'s own dated build history for the
full detail of what's interactive inside each screen (recent highlights: a real per-player
NOC/tier authority popup on every Tier A/B/C central-contract row; a real timer-based
auction bidding flow with a live per-franchise purse/status table; a real national
squad-announcement flow with a call-up/drop diff; a real context switcher that reshapes the
whole sidebar by coaching identity).

---

## PART 3 — Reference-game asset audit: what's live, what's genuinely still unused

`docs/external_game_reference/extracted_combined/GUIDE.md` catalogues everything pulled
from Cricket Coach 2014 (CC2014) and Football Manager 23 (FM23). **That guide's own
"already live" / "not used yet" notes are STALE in places** — confirmed this session: it
claims flags are "not used yet," but a live grep found `.flag-icon` styled and used **20
times** across the file (the International screen's ICC rankings, duty roster, Nation
Profile all use real flag PNGs). Treat the guide as a raw asset catalogue, not a current
usage log — verify with grep before trusting any "not yet used" claim in it.

**Re-verified this session via direct grep — genuinely still unused, real opportunities**
(now that the borrow-and-adapt policy correction below makes richer reuse legitimate):

1. **`cc2014/icons_shots/`** (~13 real HD shot-type icons: cover drive, pull, hook, leave,
   leg glance, straight drive, back defence, on drive) — zero occurrences in the file. The
   Match Day Analysis tab's wagon-wheel/commentary legend currently uses plain coloured
   dots; these are the ready, real, cricket-specific replacement.
2. **`cc2014/icons_balltypes/length/` + `.../movement/`** (bouncer/yorker/good-length/
   short; inswing/outswing/off-break/leg-break/slower-ball/etc.) — zero occurrences. The
   Tactics screen's bowling-plan "Change" dropdown is plain text; the guide's own
   suggested combination (CC2014's real icon content + FM23's real carousel/picker layout
   pattern from `layout_xml`) is still untried.
3. **`cc2014/umpire_signals/`** (12 real umpire figure + signal animations: out, four, six,
   no-ball, wide) — zero occurrences. A natural fit for a big-moment flash on Match Day
   (a wicket, a six) that doesn't exist yet.
4. **`cc2014/icons_weather/`** — only "sunny" is live (confirmed by the guide and by this
   session's own read of the Match Day header). The cloud-stage/hot/tropical/3-rain-
   intensity tiles are real and ready; Match Day always shows the same weather regardless
   of fixture.
5. **FM23's `ui_icons_skins/`** sport-agnostic clean icon set (checkmark/X/star/lock/
   stopwatch/briefcase/pencil/newspaper/globe/minus/chevrons) — not evaluated against the
   current custom SVG sprite. A real, named "overlap decision" the guide itself flags and
   nobody has made yet: keep the current custom icon style, or adopt this cleaner set for
   some or all status chips.
6. **The panel-background "overlap decision"** (the guide's own §"Overlap decisions" item
   1) — CC2014's flat navy + real corner art is currently live everywhere; FM23's real
   `skin_0017.png` (a deep purple-to-blue gradient with a particle/crowd effect) was never
   tried. A real, one-time design call, not a per-screen one.
7. **CC2014's real wording data** — `bowling_plans.csv` (357 real plan names +
   descriptions), `field_setting_names_and_descriptions.txt`, `injury_descriptions.txt`,
   `sample_news_wording.txt`. Under the **old**, stricter borrowing rule this was
   off-limits as literal copy; under the **corrected** rule (see below), this is now a
   legitimate source to adapt from — genuinely not yet revisited with that new freedom.
8. **FM23's `layout_xml/sitoolkit_design_vocabulary.txt`** — the real 45-widget-class
   vocabulary, in particular `multi_collapsable_box` (a real collapsible-section pattern)
   — worth checking against the mockup's denser screens (Franchise, International's Staff
   & Board subtab, Recruitment) now that several have grown genuinely dense.

**What's already confirmed live** (don't re-do): the real app background/header/footer,
glossy button pieces (normal + hover), the 4 real dialog-corner pieces (on every card), the
green/grey nav tab end-caps, the injury-cross/international-plane status badges, the
batsman/bowler/allrounder/keeper role icons, a partial ball-outcome icon set (dot/four/
single), the "sunny" weather tile, the real pitch-map length-band texture colouring the
Analysis tab, ~150 real national flags (confirmed live, contra the stale guide note), the
real fielding-position marker convention and position names, and the real CC2014 font
site-wide.

---

## PART 4 — Borrow-and-adapt policy (CORRECTED this session — supersedes the old rule)

**The user's own direct correction, given twice, worth having verbatim:**

> "content borrow kr skte ho layout kr skte ho saara mechanism kr skte ho but usko modify
> cricket k hisaab se krna hai like game k hisaab se" — you can borrow content, layout,
> the whole mechanism, but it has to be modified for cricket, for the game it actually is.

And the follow-up that prompted this whole review-and-plan document:

> "bhai handoff prompt ka mtlb hai ke full code status kaha pohnche hai kya kr rhe hain
> code kya hai like first full project ko review aur analyse kre with design as well and
> first identify gaps aur phir agay ka plan kre kya rhta ha konsa phase hai kya krna hai
> abhi kaise kre ge code ko bhi dekhna ha aur extracted layout, features, ui, ux reference
> games se kaise copy krna hai functionality enhance honi chahiye reduce nae"
>
> ("Handoff prompt should mean: full code status — where have we reached, what are we
> doing, what is the code. Do a full project review and analysis, including design, first
> identify gaps, then plan what's next — which phase, what to do, how. Also look at the
> code, and at the extracted layout/features/UI/UX from the reference games and how to
> copy from them — functionality should be ENHANCED, not reduced.")

**The rule now**: borrowing CC2014/FM23 content, layout, AND mechanism is fine — the bar
is genuine cricket-adaptation (reshaped into this project's own terms/vocabulary/domain
events), never football content left untouched or pasted verbatim. This **supersedes** the
much stricter rule that operated through most of this sub-track's build history ("no
CC2014/FM23 text, narrative, or game logic is ever borrowed, only three narrow visual
exceptions") — do not apply that older rule in the new session.

**What does NOT change**: the separate Phase-18 real-world-data exclusion list (real
player names, real historical records/stats, real per-country name pools, real venue
names) stays exactly as strict as before — that's about factual staleness, not
IP-borrowing, and the two rules are unrelated.

Both `CLAUDE.md` (search "corrected as of 2026-09-13") and the earlier version of this
handoff doc already carry this correction — restated here for a new session's convenience.

---

## PART 5 — Gap analysis and forward plan

Given everything above, here is the honest gap picture and a concrete next-step plan, in
priority order. **This is a recommendation, not a queued task list** — confirm direction
with the user before starting any of it in the new session.

### Immediate: a decision, not code

1. **Resolve the `CricketManager.Api` question first.** Show the user this section of the
   handoff, ask directly whether that work is intentional/current and should be folded
   into the project's real history (documented in `CLAUDE.md`, committed to git) or left
   alone for now. This blocks nothing else, but leaving it silently uncommitted forever
   while continuing to build the mockup risks the same "unexplained state" problem
   `CLAUDE.md`'s own incident log warns about.

### Near-term UI mockup work (highest leverage, now unblocked by the policy correction)

2. **The bowling-plan picker** — CC2014's real length/movement icons + FM23's real
   carousel/picker layout pattern, replacing the Tactics screen's plain dropdown. This is
   the guide's own single most-recommended combination and touches a screen already core
   to the app.
3. **The wagon-wheel/shot legend** on Match Day Analysis — swap the plain colour-dot
   legend for CC2014's real shot-type icons.
4. **Weather-state variety** — wire the remaining CC2014 weather tiles so different
   fixtures/seasons genuinely show different conditions instead of always "sunny."
5. **Make the panel-background and icon-style "overlap decisions"** once, deliberately
   (CC2014 navy vs FM23 gradient; current custom SVG sprite vs FM23's clean icon set) —
   present both to the user as a real visual choice rather than defaulting silently.
6. **Revisit existing self-authored copy** (bowling-plan descriptions, field-setting
   labels, injury narration, news wording) against CC2014's real equivalent text now that
   adapting it is legitimate — an enhancement pass, not urgent, and never a wholesale
   replacement (the adaptation requirement still applies).
7. **Close the one explicitly acknowledged mockup gap**: the Franchise action button only
   distinguishes "Franchise active vs not" — it doesn't yet cycle through the real
   EOI → Pre-Auction → Auction → Trade-Centre states by actual due date, which was the
   user's original ask. `CLAUDE.md` flags this itself as unresolved.

### Medium-term

8. **Decide the two newer franchise leagues' depth** — Indian Masters League and Southern
   Blaze League currently exist only as a directory + profile (deliberately scoped light
   when added). Build them a full auction/retention/trade cycle like the Pakistan Premier
   League has, or keep them profile-only by design? A real, undecided scope call.

### Longer-term — the actual "next phase" trajectory

9. **Wire real screens to `CricketManager.Api`** (once the decision in step 1 is made).
   The Squad table and Player Profile screen already have matching DTOs
   (`SquadRowDto`/`PlayerProfileDto`) built for exactly this purpose — this is the most
   natural, lowest-friction starting point for turning the illustrative HTML mockup into a
   genuinely data-backed application, which is the project's own long-stated destination
   ("a graphical UI — its own long sub-track after the console app exists"). This is a
   materially different kind of work from the mockup scripting discipline above (a real
   frontend framework, not throwaway Python over static HTML) — treat it as its own
   sub-track, not a continuation of the current editing discipline.

---

## The editing discipline for the HTML mockup (follow this exactly — it's load-bearing)

1. Every edit is a standalone Python script written to the scratchpad directory, never
   inline. Each script:
   - Reads the file, defines a `must_replace(old, new, label)` helper that asserts
     `old in content` AND `content.count(old) == 1` before replacing (so a script fails
     loudly on a missing or ambiguous match rather than silently corrupting the file).
   - For bulk near-identical replacements, use `must_replace_all` / a `re.subn(...,
     count=N)` with an asserted exact match count (see `rebuild_central_contract_authority
     .py` in this session's own scratchpad history for the pattern — a single regex pass
     over 29 near-identical rows, asserted `n_rows == 29`, rather than 29 individual
     `must_replace` calls).
   - Writes to `<path>.tmp`, then `os.replace(tmp, path)` — never writes the real path
     directly mid-script.
2. **After every single script**, run the full structural verification suite before
   trusting the result:
   - A Python regex tag-count check across `div`/`table`/`tr`/`span`/`svg`/`nav`/
     `section`/`button` (open count must equal close count, self-closed tags excluded).
   - A stack-based div-nesting scanner (regex `<(/?)div\b[^>]*?(/?)>`, LIFO stack,
     self-closed divs skipped) reporting unclosed-at-EOF and extra-closes — both must be
     zero. This catches nesting bugs the flat tag-count check alone cannot (equal counts
     can still mean wrong nesting).
   - A duplicate-`id="..."` scan via `Counter` — must come back empty.
   - Extract the trailing `<script>...</script>` block, write it to a scratchpad `.js`
     file, and run `"/c/Program Files/nodejs/node" --check <path>` — must print no error.
     Node is at that exact path in this environment (confirmed working this session).
3. Only after all of the above pass clean do you commit, then publish to the artifact.
4. **Never** use a Bash heredoc (`<<'EOF'`) for a large multi-line Python script in this
   shell — it has proven unreliable for big scripts in past sessions (quoting failures).
   Always use the `Write` tool to create the `.py` file, then run it with
   `python <path>` (not `python3` — this environment's Python is invoked as `python`).

## The live artifact — NOTE: this does NOT carry over to the new account

Was published at `https://claude.ai/code/artifact/0e9296b7-4f1d-4fb7-8904-e40b644ddb50`
(Version 61 as of this handoff), owned by the account this session ran under. **Confirmed
after the first version of this handoff doc was written**: a background notification
reported the artifact watch stopped because "no such artifact for this account" —
artifacts are per-account, so the new account cannot read or update that URL at all. This
is expected, not a problem — nothing is lost, since the mockup's actual source of truth is
the committed HTML file in this git repo, and the artifact was always just a published copy
of it.

**In the new session**: do not attempt to read or republish to the old URL — it will fail.
Instead, publish a **brand-new** artifact (`Artifact` tool, `file_path` only, no `url`
param) from the current committed file. Ask the user first whether they want a fresh
artifact published now or only once there's a new change to show — don't publish
proactively just to "restore" the old one, since publishing is a visible, shareable action.
Once a new artifact exists, follow the normal update discipline from there (read before
each future republish, same URL each time).

## Domain-grounding discipline — the pattern that matters most for this handoff

This project's standing practice: **every UI mechanic is traced to what the real C# domain
actually supports before being built.** When the user's own request conflicts with what the
real domain model does, the correct move is to build the *honest, domain-accurate* version
and explicitly report the correction — not silently comply with a request that would
misrepresent the underlying simulation, and not silently ignore the user either. Several
past sessions (documented at length in `CLAUDE.md`, e.g. "MEETING-DRIVEN SELECTION TICKET —
CORRECTIONS PASS") are exactly this pattern playing out. **The central-contract-authority
correction earlier in this same session is itself an example of the reverse case** — the
user *wanted* a real deviation from the shipped domain model (board-decided central
contracts) toward a different, simpler design (coach-controlled), and the correct move was
to build that deviation faithfully, not defend the original domain-accurate design.

## What happened this session, in order (condensed — see `CLAUDE.md` for full detail)

1. Picked up from a prior compacted session; found and fixed a real duplicate-id bug, then
   closed out 8 "what's remaining" mockup items.
2. One of those items — an "annual central-contract review" feature — was built wrong
   (a passive, board-decided diff modal). The user rejected this across four messages,
   clarifying: promotion/demotion/inclusion/NOC is the coach's own authority; the
   announcement itself stays a gated once-a-year event with full redraft power; a separate,
   always-available year-round review lets the coach adjust an already-listed player's
   tier/NOC but can never induct an outsider. Rebuilt accordingly (commit `d4cba4a`),
   verified clean, published as Artifact Version 61.
3. Wrote an initial session-handoff doc for the account switch (commit `6bc4cc3`).
4. The user corrected the borrowing policy (see Part 4 above) — updated `CLAUDE.md` and
   this doc (commits `8cebc92`, then `a979795` once a background notification confirmed
   the artifact doesn't carry over accounts).
5. The user then clarified what a real "handoff prompt" should actually contain — a full
   project review, gap analysis, and forward plan, not just a procedural note. This
   produced Parts 1-5 above: a live `dotnet build` (clean), a live test-suite re-run
   (kicked off, confirm the result before trusting the exact count), a real screen
   inventory via grep, the `CricketManager.Api` finding, and the reference-asset audit.

## House style reminders for the new session

- Respond in the same Roman Urdu/English mix the user writes in when reporting completed
  work back to them (this session did so throughout).
- Keep responses concise; do not over-explain implementation details unless asked — a
  short summary of what changed and why is the norm (see how each dated `CLAUDE.md` entry
  is written: precise, technical, but not padded).
- Every meaningful batch of mockup work ends with: verify (the full suite above) → update
  `CLAUDE.md` with a dated entry → commit (only the relevant files — see the
  `CricketManager.Api` note above for what to leave alone) → publish to the artifact →
  report back to the user. Follow this ritual every time, not just for large batches.
- When the user asks for a "full review"/"status"/"gap analysis" again, don't just recite
  `CLAUDE.md` — verify live (build, grep, read actual files) the way this session did,
  since documentation can go stale relative to the real state (proven twice this session:
  the flags claim in the reference-asset guide, and the entire undocumented `Api` project).
