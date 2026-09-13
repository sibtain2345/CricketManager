# Cover Point Design-Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the three concretely-specified, user-approved findings from the 2026-09-13
design review — the context switcher not respecting the onboarding career choice, the
Fixtures screen showing only one of the world's three domestic competitions, and two bare
`0.8rem` font-size values that bypass the established type-scale token system.

**Architecture:** No new files, no build step. `docs/ui-mockup/cover-point-mockup.html` is a
single-file static HTML mockup; every change is a standalone, throwaway Python find/replace
script (never the Edit tool on this file directly — it is ~2.8MB, past reliable inline-edit
size), verified by a structural suite, then committed.

**Tech Stack:** Plain HTML/CSS/vanilla JS in one file. Python 3 (`python`, not `python3`, in
this environment) for the edit scripts. Node.js (`"/c/Program Files/nodejs/node"`) for
`--check` syntax validation of the extracted `<script>` block.

**Spec:** `docs/superpowers/specs/2026-09-13-cover-point-design-review.md` — Findings A1,
A2, and the Part F font-size note; Part E records the user's own decisions.

## Global Constraints

- Never edit `docs/ui-mockup/cover-point-mockup.html` with the `Edit` tool directly — always
  a one-shot Python script in the scratchpad directory, using a `must_replace(old, new,
  label)` helper that asserts `old in content` and `content.count(old) == 1`, writing to
  `<path>.tmp` then `os.replace(tmp, path)`.
- After **every** script, before trusting the result: a tag-count parity check
  (`div`/`table`/`tr`/`span`/`svg`/`nav`/`section`/`button`), a stack-based div-nesting scan
  (0 unclosed, 0 extra-closes), a duplicate-`id` scan (must be empty), and `node --check` on
  the extracted trailing `<script>` block.
- Never use a Bash heredoc for a multi-line Python script in this shell (unreliable
  quoting) — always `Write` the `.py` file, then run `python <path>`.
- Every color in new markup must reference an existing `--` custom property from `:root`
  (no new hardcoded hex) unless there is a specific, stated reason (the file currently has
  exactly one deliberate exception, documented in the spec's Part F).
- Every font-size in new markup must reference an existing `--fs-*` token, not a bare
  rem/px value, except a genuine one-off hero moment (also documented in Part F).
- After all tasks in this plan are complete: update `CLAUDE.md` with one dated entry
  covering all three fixes together (this project's established ritual — see
  `docs/ui-mockup/SESSION_HANDOFF.md` for the exact pattern to follow), commit (only the
  mockup file + `CLAUDE.md`), then offer to publish to the Claude Artifact.
- **Explicitly OUT OF SCOPE for this plan**: the Part B/B1 reference-asset backlog (the
  bowling-plan picker, the wagon-wheel shot-type legend, weather-tile variety, a
  `multi_collapsable_box` pattern, an umpire-signal flash, and the CC2014-wording adaptation
  pass). The spec's own self-review already named these as needing their own short design
  note at pickup time, not fake-specified here — each becomes its own plan when picked up.

---

### Task 1: Gate the topbar context switcher by the chosen onboarding career path

**Files:**
- Modify: `docs/ui-mockup/cover-point-mockup.html`
  - The `function setContext(ctx) { ... }` definition (currently starts with
    `CURRENT_CONTEXT = ctx;` and toggles `.tabnav-inner > .tab-btn` visibility by
    `data-context`, but never touches the topbar `.context-btn` pills themselves).
  - The `function finishOnboarding(ctx, label) { setContext(ctx); exitOnboardingMode(); }`
    definition (the single choke point every onboarding path — New Career's three direct
    role choices AND the Unemployed → Job Market → "Take up the post" path — already
    funnels through).
  - The three `<button class="context-btn" ...>` elements in the topbar (`data-context`
    values: `"club"`, `"franchise"`, `"international"`).

**Interfaces:**
- Consumes: nothing new from elsewhere in the file.
- Produces: a new global `var CAREER_ROLES` (array of the context strings the current save's
  coach actually holds — `['club','franchise','international']` by default, matching the
  existing "Continue" demo-save behaviour unchanged). Any later, separate task that adds a
  "the coach picked up a second role mid-career" mechanic should `.push()` onto this array
  and re-run the same hide/show pass `setContext` already does — not invent a second
  variable.

- [ ] **Step 1: Read the current `setContext`/`finishOnboarding` code to confirm line
  numbers haven't shifted since this plan was written**

  Run: `grep -n "function setContext\|function finishOnboarding" "docs/ui-mockup/cover-point-mockup.html"`

  Expected: two matches, close to lines 5744 and 5875 (a small shift is fine — the
  `must_replace` script below matches on the exact surrounding TEXT, not the line number,
  so a shift alone doesn't break it; a genuinely different function body would).

- [ ] **Step 2: Write the edit script**

  Create `<scratchpad>/gate_context_switcher.py`:

  ```python
  # -*- coding: utf-8 -*-
  import os

  PATH = r"docs/ui-mockup/cover-point-mockup.html"
  TMP = PATH + ".tmp"

  with open(PATH, "r", encoding="utf-8") as f:
      content = f.read()

  start_len = len(content)


  def must_replace(old, new, label):
      global content
      assert old in content, "NOT FOUND: " + label
      assert content.count(old) == 1, "NOT UNIQUE (%d): %s" % (content.count(old), label)
      content = content.replace(old, new, 1)


  # 1. A new global tracking which roles the CURRENT save's coach actually holds. Default
  # is all three - the pre-existing "Continue" (resume the full demo save) behaviour is
  # completely unchanged.
  must_replace(
      "  function setContext(ctx) {\n"
      "    CURRENT_CONTEXT = ctx;",
      "  var CAREER_ROLES = ['club', 'franchise', 'international'];\n"
      "  function setContext(ctx) {\n"
      "    CURRENT_CONTEXT = ctx;",
      "add CAREER_ROLES global before setContext",
  )

  # 2. setContext itself now also gates the topbar switcher PILLS (not just the sidebar
  # tabs it already gated) by CAREER_ROLES - a role the coach doesn't hold isn't a
  # clickable identity, matching what onboarding just told them about their own career.
  must_replace(
      '    document.querySelectorAll(".context-btn").forEach(function (b) {\n'
      '      b.setAttribute("aria-selected", b.dataset.context === ctx ? "true" : "false");\n'
      "    });",
      '    document.querySelectorAll(".context-btn").forEach(function (b) {\n'
      '      b.setAttribute("aria-selected", b.dataset.context === ctx ? "true" : "false");\n'
      "      b.hidden = CAREER_ROLES.indexOf(b.dataset.context) === -1;\n"
      "    });",
      "gate .context-btn pills by CAREER_ROLES",
  )

  # 3. finishOnboarding is the single choke point every real career-start path already
  # funnels through (the three direct New Career role choices, AND the Unemployed -> Job
  # Market -> "Take up the post" path via advanceToDecisionDay). Restrict CAREER_ROLES to
  # just the chosen role before setContext runs, so the switcher only ever shows what this
  # specific career actually started as.
  must_replace(
      "  function finishOnboarding(ctx, label) {\n"
      "    setContext(ctx);\n"
      "    exitOnboardingMode();\n"
      "  }",
      "  function finishOnboarding(ctx, label) {\n"
      "    CAREER_ROLES = [ctx];\n"
      "    setContext(ctx);\n"
      "    exitOnboardingMode();\n"
      "  }",
      "restrict CAREER_ROLES in finishOnboarding",
  )

  with open(TMP, "w", encoding="utf-8") as f:
      f.write(content)

  end_len = len(content)
  print("total len change:", end_len - start_len, "/ final size:", end_len)

  os.replace(TMP, PATH)
  ```

- [ ] **Step 2b: Run it**

  Run: `python "<scratchpad>/gate_context_switcher.py"`
  Expected: prints a small positive length change (roughly +260 characters), no
  `AssertionError`.

- [ ] **Step 3: Run the structural verification suite**

  Run the project's existing verification script pattern (tag-count parity, div-nesting
  scan, duplicate-id scan — see any prior `verify_after_*.py` in the scratchpad history for
  the exact reusable code, e.g. `verify_after_cc_authority.py`), then:

  Run: `"/c/Program Files/nodejs/node" --check <scratchpad>/extracted_main.js`
  Expected: no output (success), all four checks clean.

- [ ] **Step 4: Manual logic check (no test framework exists for this file — read the
  result directly)**

  Run: `grep -n "var CAREER_ROLES\|CAREER_ROLES.indexOf\|CAREER_ROLES = \[ctx\]" "docs/ui-mockup/cover-point-mockup.html"`

  Expected: three matches — the new global declaration, the `.hidden =` gate inside
  `setContext`, and the restriction inside `finishOnboarding`. Confirms the edit landed in
  all three places and nowhere else.

- [ ] **Step 5: Commit**

  ```bash
  git add docs/ui-mockup/cover-point-mockup.html
  git commit -m "UI mockup: gate the context switcher by the onboarding career choice

A coach who started as Club Manager could still click Franchise or
International in the topbar switcher and see a fully-populated real
screen, contradicting what onboarding just told them about their own
career. finishOnboarding now restricts CAREER_ROLES to the chosen
path; the Continue (demo save) path is unchanged, still all three.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
  ```

---

### Task 2: Give Fixtures a real competition-tab switcher

**Files:**
- Modify: `docs/ui-mockup/cover-point-mockup.html`
  - `<section class="panel" id="panel-fixtures" ...>` — currently one flat body showing
    only Pakistan T20 Cup content (a league-position table, an opposition-report card, and
    a 10-row season schedule).

**Interfaces:**
- Consumes: nothing new.
- Produces: a `wireTabs('#panel-fixtures .subtab-btn', 'fixtures-')` call (the project's
  own established subtab-wiring helper, already used identically for Squad's First
  Team/Academy split and Club's four subtabs — confirm the exact call shape by reading one
  existing `wireTabs(...)` call before writing this one, since the helper signature must
  match exactly).

- [ ] **Step 1: Confirm the existing `wireTabs` call shape**

  Run: `grep -n "wireTabs('#panel-squad" "docs/ui-mockup/cover-point-mockup.html"`

  Expected: one match, e.g. `wireTabs('#panel-squad .subtab-btn', 'squad-');` — confirms the
  exact two-argument call shape (selector, id-prefix) to replicate for Fixtures.

- [ ] **Step 2: Read the current Fixtures panel in full**

  Run: `grep -n '<section class="panel" id="panel-fixtures"' "docs/ui-mockup/cover-point-mockup.html"`
  to get the current line number, then read that line through the next
  `<section class="panel" id="panel-international"` line (use the `Read` tool with that
  offset/limit, or `sed -n '<start>,<end>p'` with the two real line numbers) to copy the
  EXACT current opening/closing markup for the `must_replace` old-strings below — the file
  may have shifted slightly since this plan was written, so match against the real text,
  not the line number this plan cites.

- [ ] **Step 3: Write the edit script**

  Create `<scratchpad>/fixtures_competition_tabs.py`. This wraps the EXISTING T20 Cup body
  (league table + opposition report + season schedule — unchanged, not rewritten) in a new
  `t20cup` subpanel, and adds two new, genuinely real (if lighter) subpanels for the other
  two domestic competitions the World screen already establishes, reusing the same
  established domestic team names for continuity:

  ```python
  # -*- coding: utf-8 -*-
  import os

  PATH = r"docs/ui-mockup/cover-point-mockup.html"
  TMP = PATH + ".tmp"

  with open(PATH, "r", encoding="utf-8") as f:
      content = f.read()

  start_len = len(content)


  def must_replace(old, new, label):
      global content
      assert old in content, "NOT FOUND: " + label
      assert content.count(old) == 1, "NOT UNIQUE (%d): %s" % (content.count(old), label)
      content = content.replace(old, new, 1)


  # 1. The panel head gains a subnav tab strip, matching the exact pattern Club/Squad
  # already use (a <nav class="subnav"> of .subtab-btn buttons right under panel-head).
  must_replace(
      '''  <section class="panel" id="panel-fixtures" role="tabpanel" hidden>
    <div class="panel-head"><h1>Fixtures</h1><div class="sub">Pakistan T20 Cup &middot; Group stage</div></div>

    <div class="tactics-grid">''',
      '''  <section class="panel" id="panel-fixtures" role="tabpanel" hidden>
    <div class="panel-head"><h1>Fixtures</h1><div class="sub">Every domestic competition the club plays this season</div></div>
    <nav class="subnav" aria-label="Fixtures views">
      <button class="subtab-btn" role="tab" aria-selected="true" aria-controls="fixtures-t20cup" data-subtab="t20cup">Pakistan T20 Cup</button>
      <button class="subtab-btn" role="tab" aria-selected="false" aria-controls="fixtures-fcc" data-subtab="fcc">First-Class Championship</button>
      <button class="subtab-btn" role="tab" aria-selected="false" aria-controls="fixtures-lista" data-subtab="lista">List A Cup</button>
    </nav>

    <div class="subpanel" id="fixtures-t20cup">
    <div class="tactics-grid">''',
      "add the subnav strip + open the t20cup subpanel",
  )

  # 2. Close the t20cup subpanel right where the existing body already ends (the final
  # </section> of this panel), then add the two new subpanels before it.
  must_replace(
      '''      <div class="fixture-row" style="margin-top:10px;padding-top:10px;border-top:1px solid var(--border-soft)"><div class="vs-crest them"></div><div class="fixture-names">Quetta Falcons<small>Away &middot; Fri 9 Apr</small></div><div class="fixture-meta">14 days</div></div>
    </div>
  </section>

  <!-- ================= INTERNATIONAL ================= -->''',
      '''      <div class="fixture-row" style="margin-top:10px;padding-top:10px;border-top:1px solid var(--border-soft)"><div class="vs-crest them"></div><div class="fixture-names">Quetta Falcons<small>Away &middot; Fri 9 Apr</small></div><div class="fixture-meta">14 days</div></div>
    </div>
    </div>

    <div class="subpanel" id="fixtures-fcc" hidden>
    <div class="analysis-card">
      <span class="eyebrow"><svg class="icon sm"><use href="#i-trend-up"/></svg> League position</span>
      <div class="table-card">
        <table class="career">
          <thead><tr><th>Pos</th><th>Team</th><th class="num">Pld</th><th class="num">W</th><th class="num">D</th><th class="num">L</th><th class="num">Pts</th></tr></thead>
          <tbody>
            <tr><td>1</td><td>Lahore Lions</td><td class="num">4</td><td class="num">2</td><td class="num">2</td><td class="num">0</td><td class="num">26</td></tr>
            <tr style="background:var(--turf-dim)"><td style="color:var(--turf-strong);font-weight:700">2</td><td style="color:var(--turf-strong);font-weight:700">Islamabad Icons</td><td class="num" style="color:var(--turf-strong);font-weight:700">4</td><td class="num" style="color:var(--turf-strong);font-weight:700">1</td><td class="num" style="color:var(--turf-strong);font-weight:700">3</td><td class="num" style="color:var(--turf-strong);font-weight:700">0</td><td class="num" style="color:var(--turf-strong);font-weight:700">21</td></tr>
            <tr><td>3</td><td>Karachi Kings</td><td class="num">4</td><td class="num">1</td><td class="num">1</td><td class="num">2</td><td class="num">15</td></tr>
          </tbody>
        </table>
      </div>
      <div class="role-hint" style="margin-top:10px">First-class points: 8 for a win, 2 for a draw, plus batting/bowling bonus points earned inside the first 100 overs of each side&rsquo;s own first innings.</div>
    </div>
    <div class="analysis-card" style="margin-top:16px">
      <span class="eyebrow"><svg class="icon sm"><use href="#i-cal-list"/></svg> Season schedule</span>
      <div class="fixture-row"><div class="vs-crest them"></div><div class="fixture-names">Lahore Lions<small>Home &middot; 4 days from Fri 3 Mar</small></div><div class="fixture-meta"><span class="pill fringe">Drawn</span></div></div>
      <div class="fixture-row" style="margin-top:10px;padding-top:10px;border-top:1px solid var(--border-soft)"><div class="vs-crest them"></div><div class="fixture-names">Karachi Kings<small>Away &middot; 4 days from Sat 18 Mar</small></div><div class="fixture-meta"><span class="pill first">W</span> by 6 wickets</div></div>
      <div class="fixture-row" style="margin-top:10px;padding-top:10px;border-top:1px solid var(--border-soft);background:var(--turf-dim);margin-left:-14px;margin-right:-14px;padding-left:14px;padding-right:14px"><div class="vs-crest them"></div><div class="fixture-names">Quetta Falcons<small>Home &middot; 4 days from Mon 3 Apr</small></div><div class="fixture-meta"><span class="pill fringe"><svg class="icon sm"><use href="#i-clock"/></svg>Next</span></div></div>
    </div>
    </div>

    <div class="subpanel" id="fixtures-lista" hidden>
    <div class="analysis-card">
      <span class="eyebrow"><svg class="icon sm"><use href="#i-trend-up"/></svg> League position</span>
      <div class="table-card">
        <table class="career">
          <thead><tr><th>Pos</th><th>Team</th><th class="num">Pld</th><th class="num">W</th><th class="num">L</th><th class="num">Pts</th></tr></thead>
          <tbody>
            <tr><td>1</td><td>Multan Sultans</td><td class="num">5</td><td class="num">4</td><td class="num">1</td><td class="num">8</td></tr>
            <tr style="background:var(--turf-dim)"><td style="color:var(--turf-strong);font-weight:700">2</td><td style="color:var(--turf-strong);font-weight:700">Islamabad Icons</td><td class="num" style="color:var(--turf-strong);font-weight:700">5</td><td class="num" style="color:var(--turf-strong);font-weight:700">3</td><td class="num" style="color:var(--turf-strong);font-weight:700">2</td><td class="num" style="color:var(--turf-strong);font-weight:700">6</td></tr>
            <tr><td>3</td><td>Peshawar Zalmi</td><td class="num">5</td><td class="num">2</td><td class="num">3</td><td class="num">4</td></tr>
          </tbody>
        </table>
      </div>
      <div class="role-hint" style="margin-top:10px">50-over cricket &mdash; two points for a win, none for a loss, one each for a no-result.</div>
    </div>
    <div class="analysis-card" style="margin-top:16px">
      <span class="eyebrow"><svg class="icon sm"><use href="#i-cal-list"/></svg> Season schedule</span>
      <div class="fixture-row"><div class="vs-crest them"></div><div class="fixture-names">Multan Sultans<small>Away &middot; Sun 21 Mar</small></div><div class="fixture-meta"><span class="pill injury">L</span> by 12 runs</div></div>
      <div class="fixture-row" style="margin-top:10px;padding-top:10px;border-top:1px solid var(--border-soft)"><div class="vs-crest them"></div><div class="fixture-names">Peshawar Zalmi<small>Home &middot; Thu 25 Mar</small></div><div class="fixture-meta"><span class="pill first">W</span> by 34 runs</div></div>
      <div class="fixture-row" style="margin-top:10px;padding-top:10px;border-top:1px solid var(--border-soft);background:var(--turf-dim);margin-left:-14px;margin-right:-14px;padding-left:14px;padding-right:14px"><div class="vs-crest them"></div><div class="fixture-names">Karachi Kings<small>Away &middot; Sun 4 Apr &middot; 09:30</small></div><div class="fixture-meta"><span class="pill fringe"><svg class="icon sm"><use href="#i-clock"/></svg>Next</span></div></div>
    </div>
    </div>
  </section>

  <!-- ================= INTERNATIONAL ================= -->''',
      "close t20cup subpanel, add fcc + lista subpanels",
  )

  # 3. Wire the new subnav via the project's own established subtab helper - one line,
  # dropped right next to the existing wireTabs('#panel-squad ...') call for consistency.
  must_replace(
      "  wireTabs('#panel-squad .subtab-btn', 'squad-');",
      "  wireTabs('#panel-squad .subtab-btn', 'squad-');\n"
      "  wireTabs('#panel-fixtures .subtab-btn', 'fixtures-');",
      "wire the Fixtures subnav",
  )

  with open(TMP, "w", encoding="utf-8") as f:
      f.write(content)

  end_len = len(content)
  print("total len change:", end_len - start_len, "/ final size:", end_len)

  os.replace(TMP, PATH)
  ```

- [ ] **Step 3b: Run it**

  Run: `python "<scratchpad>/fixtures_competition_tabs.py"`
  Expected: prints a length change of roughly +3200 characters, no `AssertionError`. If
  Step 2's copy of the exact current opening/closing markup doesn't match byte-for-byte
  (likely if other work has touched Fixtures since this plan was written), the script fails
  loudly with `NOT FOUND` — fix the `old` string to match the real current text, don't
  loosen the assertion.

- [ ] **Step 4: Run the structural verification suite**

  Same four checks as Task 1 Step 3. Pay particular attention to the div-nesting scan here
  — this script opens/closes two new `.subpanel` divs, the class of bug the nesting scanner
  exists specifically to catch.

- [ ] **Step 5: Manual check**

  Run: `grep -c 'class="subpanel" id="fixtures-' "docs/ui-mockup/cover-point-mockup.html"`
  Expected: `3` (t20cup, fcc, lista).

  Run: `grep -n "wireTabs('#panel-fixtures" "docs/ui-mockup/cover-point-mockup.html"`
  Expected: one match.

- [ ] **Step 6: Commit**

  ```bash
  git add docs/ui-mockup/cover-point-mockup.html
  git commit -m "UI mockup: Fixtures gets a real competition-tab switcher

The World screen has always listed three parallel domestic
competitions (First-Class Championship, List A Cup, Pakistan T20 Cup)
but Fixtures only ever showed T20 Cup fixtures, with nothing to
switch away from it. Wraps the existing T20 Cup body unchanged in a
new subnav (matching the Squad/Club subtab pattern already
established elsewhere) and adds real, if lighter, league-table +
schedule content for the other two.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
  ```

---

### Task 3: Fix the two bare `0.8rem` font-size values

**Files:**
- Modify: `docs/ui-mockup/cover-point-mockup.html`
  - Two `<span style="color:var(--ink-dim);font-size:0.8rem">` occurrences inside Match
    Day's `stage-live` innings-head markup (the live overs-count suffix, e.g.
    `142/3 <span ...>(14.2 ov)</span>`).

**Interfaces:**
- Consumes: the existing `--fs-small` token (0.78rem — already defined in `:root`,
  functionally identical to the bare `0.8rem` this replaces).
- Produces: nothing new; this task removes a token-system bypass, it doesn't add a
  capability.

- [ ] **Step 1: Confirm both occurrences are still identical**

  Run: `grep -n 'font-size:0.8rem' "docs/ui-mockup/cover-point-mockup.html"`
  Expected: exactly 2 matches, both `style="color:var(--ink-dim);font-size:0.8rem"`.

- [ ] **Step 2: Write the edit script**

  Create `<scratchpad>/fix_overs_suffix_token.py`:

  ```python
  # -*- coding: utf-8 -*-
  import os

  PATH = r"docs/ui-mockup/cover-point-mockup.html"
  TMP = PATH + ".tmp"

  with open(PATH, "r", encoding="utf-8") as f:
      content = f.read()

  start_len = len(content)

  OLD = 'style="color:var(--ink-dim);font-size:0.8rem"'
  NEW = 'style="color:var(--ink-dim);font-size:var(--fs-small)"'

  count = content.count(OLD)
  assert count == 2, "expected exactly 2 occurrences, found %d" % count
  content = content.replace(OLD, NEW)

  with open(TMP, "w", encoding="utf-8") as f:
      f.write(content)

  end_len = len(content)
  print("total len change:", end_len - start_len, "/ final size:", end_len)

  os.replace(TMP, PATH)
  ```

- [ ] **Step 2b: Run it**

  Run: `python "<scratchpad>/fix_overs_suffix_token.py"`
  Expected: prints a small positive length change (each replacement is 5 characters longer:
  `var(--fs-small)` vs `0.8rem`), no `AssertionError`.

- [ ] **Step 3: Run the structural verification suite**

  Same four checks. This edit touches no tags, only an attribute value, so this is
  primarily a sanity confirmation the file is still well-formed, not expected to catch
  anything new.

- [ ] **Step 4: Manual check**

  Run: `grep -c 'font-size:0.8rem' "docs/ui-mockup/cover-point-mockup.html"`
  Expected: `0`.

  Run: `grep -c 'font-size:var(--fs-small)' "docs/ui-mockup/cover-point-mockup.html"`
  Expected: at least `2` (the two just changed; there may be other pre-existing uses of the
  same token elsewhere in the file, which is fine and expected).

- [ ] **Step 5: Commit**

  ```bash
  git add docs/ui-mockup/cover-point-mockup.html
  git commit -m "UI mockup: fix two bare font-size values to use the type-scale token

The live scorecard's overs-count suffix used a hardcoded 0.8rem
instead of the established --fs-small token (0.78rem, functionally
identical) - the one genuine token-discipline gap the 2026-09-13
design-craft audit found.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
  ```

---

### Task 4: Document and wrap up

**Files:**
- Modify: `CLAUDE.md` (one new dated entry in the "GRAPHICAL UI MOCKUP" section, following
  this project's own established pattern — read the most recent 2-3 dated entries there
  first to match tone/structure exactly).
- Modify: `docs/ui-mockup/SESSION_HANDOFF.md` (update Part D/Part E of the review's own
  copy there if one exists, or add a short "resolved" note — check whether this file still
  needs updating given the design-review spec now lives in `docs/superpowers/specs/`).

**Interfaces:** none — documentation only.

- [ ] **Step 1: Write the `CLAUDE.md` entry**

  Summarize all three fixes from Tasks 1-3 in one dated entry (`2026-09-14`), in the same
  style as the existing entries in that section — precise, technical, references the real
  commit hashes from Tasks 1/2/3, states the verification result (tag counts, div-nesting,
  duplicate-id, `node --check`) for the LAST script run in the batch (or all three if they
  differ meaningfully).

- [ ] **Step 2: Commit the documentation**

  ```bash
  git add CLAUDE.md
  git commit -m "Docs: record the three design-review fixes (context switcher, Fixtures competitions, font-size token)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
  ```

- [ ] **Step 3: Offer to publish to the Claude Artifact**

  Ask the user whether they want the updated mockup published now (per
  `SESSION_HANDOFF.md`'s own standing rule: don't publish proactively without asking).

---

## Self-review

**1. Spec coverage.** Findings A1, A2, and the Part F font-size note (the three items Part
E records as approved-with-a-decision) each have a task. Part B/B1 (the reference-asset
backlog) is explicitly out of scope per the Global Constraints, matching the spec's own
self-review note that those need their own design pass first — not a gap, a deliberate
scope boundary.

**2. Placeholder scan.** No TBD/TODO. Every code step contains real, copy-pasteable script
content built from the actual current file content read during this planning session, not
invented shapes for functions I haven't seen.

**3. Type consistency.** `CAREER_ROLES` is named and used identically in both places it
appears (declaration in Task 1 Step 2's script, consumption in the same script's `setContext`
and `finishOnboarding` edits — no second name introduced anywhere). `wireTabs(selector,
prefix)`'s two-argument shape in Task 2 is copied from the CONFIRMED existing call
(Task 2 Step 1), not assumed.

**One honest risk, named rather than hidden:** Task 2's `must_replace` old-strings are
copied from a direct read of the file during this planning session (2026-09-14) — if the
mockup changes again before this plan is executed, the exact old-string may no longer match
byte-for-byte (a trailing-whitespace difference, a nearby edit). This is the SAME risk every
prior script in this project's own history has carried and handled the same way: the
assertion fails loudly (`NOT FOUND`) rather than silently corrupting the file, and the fix
is to re-read the current text and adjust the `old` string — never to loosen the assertion
to "make it pass."
