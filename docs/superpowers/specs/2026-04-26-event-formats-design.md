# Event Formats — Design (DRAFT, IN PROGRESS)

**Status:** Brainstorming paused mid-Section-3. Resume by reviewing Section 3 ("Setup page UX") with the user, then continue to Section 4 (algorithm strategy split), Section 5 (print sheet), Section 6 (testing), and final wrap.

**Last question on the table:** Section 3 looks right? Anything to cut or tweak? (Specifically, gut-check the live "5M + 3F → 1 mixed court" banner.)

---

## Context

Today the app supports one event type: **Doubles round-robin (random partners)**. The user wants to add other play formats. This spec covers expanding to two additional formats while keeping the app's identity as "schedule generator + printable sheet, not a tournament tracker."

User chose to **stay narrow** — round-robin variants only. No brackets, no scoring, no king-of-the-court, no DUPR/skill, no standings.

## Decisions locked in so far

### Scope (Section 1 — approved)

**In scope:** add **Mixed Doubles** and **Fixed Partners** as new formats. Both stay within the round-robin family (rounds × courts grid output).

**Out of scope:**
- Singles round-robin (deferred — no current ask)
- King-of-the-court / brackets / ladders (different identity)
- DUPR, scoring, standings (existing red lines, unchanged)
- Mid-event format switching

**Existing Doubles behavior unchanged** — same algorithm, UI, printout when that format is selected.

### Data model (Section 2 — approved)

- `Event.Format` enum: `Doubles | MixedDoubles | FixedPartners`. Default `Doubles` so existing rows are valid without backfill.
- `EventPlayer.Gender` nullable enum (`M | F`). Null except for `MixedDoubles`.
- `EventPlayer.TeamId` nullable int. Set only for `FixedPartners`; two `EventPlayer`s sharing a `TeamId` are partners.
- New tiny `EventTeam(Id, EventId, Name)` table for fixed-partner team names. Chosen over team-name-on-EventPlayer for cleanliness.
- `Round` / `Match` / `Bye` unchanged.
- One EF Core migration adds the three columns + the new table. Existing events become `Format = Doubles` automatically.

### Setup page UX (Section 3 — proposed, awaiting confirmation)

Single `EventSetup.razor` with a **Format** dropdown at the top. Form rearranges below.

**Doubles:** unchanged (single-column player list, courts, rounds).

**Mixed Doubles:** player list gains an **M / F** toggle next to each name. Banner shows live math: *"5M + 3F → 1 mixed court possible per round; 4 players sit out per round."* Rounds helper text reflects gendered math.

**Fixed Partners:** player list replaced by a **Team list** — two name fields per row + optional team name (auto-suggests `"Paul / Linda"` if blank). "Add Team" button. Rounds helper: *"Max before opponent repeats: N − 1 teams."* Need ≥ `2 × courts` teams.

**Switching format mid-edit:** confirm dialog — *"Switching will clear your data. Continue?"* No cross-format migration of inputs.

**Local-storage form persistence:** keyed per format. Each format remembers its own last entry.

**Copy-from-past-event:** only enabled when source event's format matches the current selection.

### Algorithm organization (Section 8 question, approved early — strategy pattern)

**Strategy pattern** chosen.

- One shared `ScheduleGenerator` keeps the court-pairing + bye-fairness logic Paul vetted.
- `ITeamSource` interface with three implementations:
  - `RandomDoublesTeamSource` — current behavior (avoid repeat partners)
  - `MixedDoublesTeamSource` — same, with "team must be 1 M + 1 F" constraint
  - `FixedTeamsTeamSource` — emits the user-provided teams unchanged
- Existing `ScheduleGenerator` gets refactored into "team source = random doubles" + the shared scheduling part.

Rejected alternatives: parameterized single class (tangles new logic into well-tested existing class); three independent generators (duplicates the court-pairing/bye logic three times — exactly what Paul cares most about).

### Mixed-doubles edge-case behavior (approved)

When M/F counts don't divide cleanly:

- **Auto-reduce courts** to `floor(min(M, F) / 2)` for mixed pairing.
- **Within-gender bye fairness**: surplus players of the larger gender rotate sit-outs *within* their own gender, not mixed across genders. So with 5M + 3F → 1 mixed court, the 3 surplus M rotate byes among themselves; F may have a separate bye queue.
- Show a banner explaining the reduction so users trust the result.

Rejected: hard-refuse to generate (annoying); silent auto-reduce without bye-fairness explanation (Paul cares about fairness); allow same-gender pairs as fallback (breaks the format contract).

### Fixed-partners team input (approved)

Two-column team list — each row is a team, two name fields side-by-side, optional team name. "Add Team" button. Rejected: separate pair-them-up step (extra mental model); implicit adjacency pairing (fragile and invisible on a printout).

### Format selection UX (approved)

Single setup page with a **Format dropdown** at the top that reshapes the form. Rejected: separate buttons on home page (scatters logic, bloats home); hidden checkboxes (hides a real input-shape difference).

## Sections still to design

- **Section 4 — Algorithm details.** What `ITeamSource` looks like exactly; how the shared `ScheduleGenerator` consumes it; how byes-per-gender are wired in for mixed.
- **Section 5 — Schedule view & print sheet.** Per-format cell content (mixed: M/F glyphs? fixed: team name vs. two names?). Same grid layout for all three, vs. minor per-format tweaks.
- **Section 6 — Testing.** New xUnit tests per `ITeamSource`. Existing `ScheduleGenerator` tests stay; refactor target so they still pass against the new shared class.
- **Section 7 — Migration / deployment.** EF migration; default-`Doubles` for existing rows; SQLite quirks if any.
- **Section 8 — Out-of-scope confirmation & risks.** Final pass on what we're not doing.

## Open questions to revisit

1. **Section 3 banner & UX gut-check** (current pause point). Is the live mixed-math banner the right amount of guidance, or too prescriptive?
2. **Print sheet:** does fixed-partners need different cell layout (team name on top, individual names below)? Or just team name? Or just two names like today?
3. **Mixed:** does the print sheet show M/F badges next to names, or stay clean and trust the format label?
4. **Past-events list:** should the format show as a tag/pill on each row?

## Notes for the next session

- **Resume at:** Section 3 confirmation. The user paused before answering whether the live mixed-math banner felt right.
- **Then proceed:** Sections 4 → 5 → 6 → 7 → 8.
- **Process:** brainstorming skill, present sections one at a time, get approval each time, then write final spec, self-review, ask user to review, transition to writing-plans.
