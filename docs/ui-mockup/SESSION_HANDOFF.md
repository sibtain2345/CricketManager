# Cover Point UI Mockup — Session Handoff (2026-09-13)

Paste this whole file as your first message in the new session (on the other account) so
Claude Code picks up exactly where this one left off. It is written the way this project's
own compacted-session summaries are written — read it the way you'd read one of those.

## What this project is

`docs/ui-mockup/cover-point-mockup.html` is a single-file HTML graphical-UI mockup for
"Cover Point" — a cricket-management sim (the deep C# simulation project documented in the
project's own `CLAUDE.md`, which the mockup is the UI layer for). This is the Phase 17 UI
sub-track. The file is large (~2.76MB) and is edited exclusively through one-shot,
throwaway Python find/replace scripts — **never** the Edit tool directly (the file is well
past any editor's practical read/patch size for reliable inline edits).

**Always read `CLAUDE.md` at the project root first** — it is the single source of truth
for both the C# domain project's phase history AND this UI mockup's own build history. The
mockup's own history lives in dated entries near the end of that file (search for "GRAPHICAL
UI MOCKUP" and read forward from there — it's a long, detailed, chronological log of every
screen/feature built, every correction the user made, and the reasoning behind each).

## The live artifact

Published at: `https://claude.ai/code/artifact/0e9296b7-4f1d-4fb7-8904-e40b644ddb50`
(currently **Version 61** as of this handoff). In the new session, before publishing any
further update, **read this artifact URL first** (`Artifact` tool, `action: "read"`) — the
publish flow refuses an update from a session that hasn't read the current live version.
Then republish the same local file path to the same URL to push any further edit.

## The editing discipline (follow this exactly — it's load-bearing)

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

## Borrow-and-adapt discipline (Principle 1/2 — CORRECTED 2026-09-13, see CLAUDE.md's UI-mockup section)

Two real reference games were extracted and catalogued (`docs/external_game_reference/`,
with its own `GUIDE.md`): Cricket Coach 2014 (CC2014) for pixel-level UI chrome/icons, and
Football Manager 23 (FM23) for real layout-XML structural patterns and a design-vocabulary.

**The rule as of the end of this session (the user's own correction, verbatim):**
"content borrow kr skte ho layout kr skte ho saara mechanism kr skte ho but usko modify
cricket k hisaab se krna hai like game k hisaab se" — you CAN borrow content, layout, and
the whole mechanism from CC2014/FM23. The requirement is that it gets genuinely **modified
and adapted for cricket** — reshaped into this project's own cricket terms, vocabulary,
attributes and domain events — not left as football content, not pasted verbatim. A
borrowed FM interaction pattern or a CC2014 wording style is a legitimate starting point;
shipping it un-adapted, or leaving anything recognizably football-shaped, is not.

This **supersedes** the earlier, stricter reading that operated through most of this
sub-track's build history ("no CC2014/FM23 text, narrative, or game logic is ever
borrowed, only three specific visual things are") — do not apply that older, stricter rule
in the new session; it's kept in CLAUDE.md's dated history only as a record of what was
true up to this point, not as the current standing rule.

**What does NOT change**: the separate, still fully strict **Phase-18 exclusion list** —
real player names, real historical records/statistics, real per-country name pools, real
venue names. That restriction is unrelated to the borrowing question above; it exists
because that specific data is a real-world factual claim that goes stale, not because of
whose IP it originally is. Keep treating it exactly as strictly as before.

FM23 still supplies UI *structure* (layout patterns, tab/subnav conventions, information
density) and CC2014 still supplies the pixel *finish* (buttons, panel chrome, icons,
backgrounds) — that split (the old "Principle 2") is unchanged and is now simply one
legitimate instance of the broader borrow-and-adapt rule above, not the only sanctioned
form of borrowing. Still verify any borrowed mechanic against the real C# domain services
named in CLAUDE.md's Phase write-ups before building a UI affordance for it — "adapted for
cricket" means grounded in what the actual simulation does, not just cricket-flavoured copy
over an unmodified football mechanic.

## Domain-grounding discipline — the pattern that matters most for this handoff

This project's standing practice: **every UI mechanic is traced to what the real C# domain
actually supports before being built.** When the user's own request conflicts with what the
real domain model does, the correct move is to build the *honest, domain-accurate* version
and explicitly report the correction — not silently comply with a request that would
misrepresent the underlying simulation, and not silently ignore the user either. Several
past sessions (documented at length in CLAUDE.md, e.g. "MEETING-DRIVEN SELECTION TICKET —
CORRECTIONS PASS") are exactly this pattern playing out. **The very last piece of work in
this session is itself an example of the reverse case** — see below — where the user
*wanted* a real deviation from the shipped domain model (board-decided central contracts)
toward a different, simpler design (coach-controlled), and the correct move was to build
that deviation faithfully, not defend the original domain-accurate design.

## What happened this session, in order

1. Picked up from a prior compacted session that had just fixed a duplicate-id bug and
   closed out 8 "what's remaining" items (Faisal Nadeem name collision, attribute
   click-through, Records click-through, World Nation/Club profile redirect conversion, a
   second/third franchise league, and — **critically, and wrongly** — a first attempt at
   an "annual central-contract review" feature.
2. That first attempt built central-contract tier review as a **passive board-decided
   diff modal** — the coach could only view a fait-accompli list of promotions/demotions/
   inclusions/drops the "National Board" had already decided, with a separate, unrelated
   "petition the board" mechanic as the only lever.
3. **The user rejected this design immediately and explicitly**, across four follow-up
   messages, each refining the correction:
   - Msg 1: "promotion demotion removal inclusion — sab coach k pass hai authority, ye
     ham khud kren ge" (all of this is the coach's own authority — we decide it ourselves).
   - Msg 2: "NOC wagera bhi saari cheezen coach k pas hain, wohi decide kre ga" (NOC —
     the No Objection Certificate grant/deny for releasing a centrally-contracted player
     to a clashing franchise season — is also the coach's own call).
   - Msg 3: "wo bhi gated call hogi central contract announcement ki, review ka option
     poora saal coach k pass hona ha, wo kisi time us ko lge to change bhi kr skta hai but
     add nae kr skta bahir se players" — the **announcement itself** stays a gated,
     once-a-year event (matching the mockup's own standing convention for calendar-gated
     moments like the franchise auction/squad announcement); but **reviewing** an
     already-contracted player's tier/NOC is available **year-round**, and that year-round
     review can **never induct a brand-new outside player**.
   - Msg 4: "jb announcement hai wo bhi coach kre ga, us time add/remove bhi kr skta —
     lekin review ki baat ho rahi hai" — clarified that at the **announcement moment
     itself**, the coach genuinely CAN add/remove/promote/demote anyone from the whole
     domestic pool (full redraft authority); it's only the *separate, always-available
     review* capability that's restricted to tier/NOC-only among already-listed players.
4. **Rebuilt accordingly, in three pieces** (all now shipped, verified, committed as
   `d4cba4a`, published as Artifact Version 61):
   - The Central Contracts intro card (International → Staff & Board → Central
     contracts) now states both mechanics plainly and correctly: "Last announced ... you
     drew up the list" / "Next announcement due in around 5 months", with a role-hint
     spelling out the two-tier authority model.
   - **All 29 Tier A/B/C rows** (6 Tier A + 9 Tier B + 14 Tier C) got a real,
     always-available inline control — the 15 Tier A/B rows' static NOC badges became
     clickable (`.tier-badge.noc-badge`, opens `openCentralContractManage(this)`); the 14
     Tier C rows (which never carried NOC at all) got a new small `.cc-manage-btn`
     ("Manage") for tier-only reassignment. Built via **one Python regex pass** over the
     whole block (not 29 individual edits), asserted against an exact `n_rows == 29`
     count. The popup itself (`#central-contract-manage-backdrop`) offers ONLY tier-move
     buttons + (Tier A/B only) Grant/Deny/Leave-pending NOC buttons — structurally, not
     just by copy, there is no outsider-picker anywhere in this popup, which is what
     actually enforces "never from outside" for the year-round path.
   - The announcement modal (`#contract-review-backdrop`, opened via the relabelled
     "This year's announcement" button) was rebuilt from a static diff table into a real
     **Approve / Override** decision surface (`decideAnnouncementMove(btn, decision)`) for
     each of the four example moves, PLUS a genuine "Bring in another player…" select +
     **Add at Tier C** action (`addOutsiderToAnnouncement()`) — the one place in the whole
     screen that IS allowed to induct an outsider, deliberately scoped to the three Tier C
     names already shown elsewhere on the same screen rather than an unbounded picker. A
     closing **Confirm this year's announcement** button locks it in with a logged
     confirmation line.
5. Verified clean (tag-count parity `div` 2245/2245, `table` 31/31, `tr` 169/169, `span`
   1368/1368, `svg` 360/360, `nav` 14/14, `section` 21/21, `button` 342/342; div-nesting
   scan 0 unclosed/0 extra-closes; duplicate-id scan clean; `node --check` on the extracted
   script clean, braces/parens balanced at 0). Full detail is written up in CLAUDE.md under
   the dated entry **"2026-09-13 (continued): central-contract annual review corrected to
   real coach authority"**.
6. Updated `CLAUDE.md` with that dated entry, committed (staging only `CLAUDE.md` and the
   mockup file — `CricketManager.sln`, `docs/external_game_reference/`, and
   `src/CricketManager.Api/` are pre-existing, deliberately untouched, unrelated changes
   already sitting in the working tree from before this session — do **not** stage or
   touch these three unless the user explicitly asks), and published to the artifact.

## Current state / what's NOT yet done

Nothing is currently broken or half-finished — the correction above is complete, verified,
committed, and published. There is no outstanding task queued from this session. When you
pick this up in the new session, the natural next step is simply **to ask the user what
they want to work on next** — do not assume there's a specific pending item, and do not
re-litigate the central-contract design (it's settled and correct per the four
clarifications above).

If you want a sense of what remains open on this mockup more broadly (deferred features,
honest scope gaps, things explicitly not yet built), search CLAUDE.md's UI-mockup section
for phrases like "Deliberately still open", "deliberately deferred", or "Still open,
deliberately" — there are several honestly-scoped gaps recorded that way throughout the
mockup's build history (e.g. franchise-specific screens for other leagues beyond PSL, the
full RTM/EOI-meeting screens for other franchise leagues, some Records click-through rows
that intentionally stay non-interactive because there's no real profile behind those
names). None of these are urgent or assumed — just background if the user asks "what's
left".

## Verbatim reference — the user's own words this session (Roman Urdu/English, kept exact)

These four are worth having verbatim in the new session's context since they define the
whole design correction:

1. "nae jo annual review hai promtion demotion removal inclusion ye sb coach k pass hai
   authroity to ye ham khud kren ge ui is trh banana ha"
2. "aaur noc wagera ye saari cheezen coach k pas hain wohi decide kre ga"
3. "wo bhi gated call hogi central contract announcemetn ki hn review ka ption poora saal
   coach k pass hona ha wo kisi time us ko lge to cahgne bhi rk skta hai but add nae kr
   skta bahir se players"
4. "nae lkin jb announcemetn hai wo bhi coach kre ga to us time add ro remove bhi kr skta
   lkin review ki baat horhi ha"

## House style reminders for the new session

- Respond in the same Roman Urdu/English mix the user writes in when reporting completed
  work back to them (this session did so throughout).
- Keep responses concise; do not over-explain implementation details unless asked — a
  short summary of what changed and why is the norm (see how each dated CLAUDE.md entry
  is written: precise, technical, but not padded).
- Every meaningful batch of mockup work ends with: verify (the full suite above) → update
  CLAUDE.md with a dated entry → commit (only the two relevant files) → publish to the
  artifact (same URL) → report back to the user. Follow this ritual every time, not just
  for large batches.
