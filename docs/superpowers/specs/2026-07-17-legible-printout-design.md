# More Legible Printed Schedule — Design

**Date:** 2026-07-17
**Status:** Approved
**Scope:** UI-only. Changes to `Schedule.razor` and `wwwroot/app.css`. No changes to the scheduling algorithm, data model, or database.

## Problem

The only recurring complaint about the printed round-robin schedule is that the font is too small to read easily. The Bye column and a wide Round column consume horizontal space that could go to the match-up columns and larger names.

## Goals

1. Let the user hide the Bye column to reclaim its width for the match columns.
2. Shrink the Round column to free additional width.
3. Make the player names noticeably larger and bolder on both screen and print.

## Non-Goals

- No change to how byes are computed or assigned — only whether the column is displayed.
- No persistence of the toggle to the database.
- No change to headers, the Round number, or the "vs" label sizes.

## Design

All three changes are presentation-only.

### 1. "Show Bye column" toggle (Schedule page toolbar)

- Add an ephemeral field `private bool showByeColumn = true;` to `Schedule.razor`, defaulting to **shown** (matches today's behavior; nothing changes for existing users unless they act).
- Render a checkbox in the existing `.no-print` toolbar, immediately after the "Track scores" label, styled to match it: `☑ Show Bye column`.
- Wrap the Bye column in `@if (showByeColumn)`:
  - the `<th>Bye</th>` header cell, and
  - each round row's `<td class="bye-cell">…</td>`.
- Because the sheet table uses `table-layout: fixed`, removing the Bye column (`width: 120px`) redistributes that width to the match columns automatically — no other layout code required.
- The printed sheet is a direct render of the on-screen `.schedule-sheet`, so the toggle's current state is what prints. No separate print logic is needed. The toggle checkbox itself lives in `.no-print` and never prints.

### 2. Shrink the Round column

- Reduce `.schedule-sheet .sheet-table td.round-num` width from `60px` to `38px`. Cell content is a short round number or the 🏆 emoji, both of which fit comfortably. This hands ~22px to the match columns whether or not the Bye column is hidden.

### 3. Bigger, bolder player names

- Add a rule targeting the team lines inside match cells:
  - Screen: `.schedule-sheet .match-cell .team-line { font-size: 13pt; font-weight: 600; }`
  - Print (inside the existing `@media print` block): override to `font-size: 12pt;` — roughly 30% larger than the current 9pt print base.
- The Round number (`td.round-num`), column headers (`thead th`), and the "vs" label (`.vs`, sized relative to its own `td`) are separate elements and keep their current small sizes.
- The rule also applies to edit-mode player chips (they render inside `.team-line`), keeping edit and view modes visually consistent.

## Trade-off

With names ~30% larger *and* scores turned on, long names on a many-court layout may wrap slightly more than today. This is expected at this size; the reclaimed Round-column space (and the Bye column when hidden) offsets most of it. The user chose "noticeably bigger" (~12pt) over "much bigger" (~14pt) specifically to stay clear of heavy wrapping.

## Testing

Manual verification on the running app (pure presentation change, no automated tests):

1. Toggle "Show Bye column" off and on — confirm the column disappears/reappears and the match columns resize to fill the space.
2. Confirm player names are visibly larger and bolder in view mode.
3. Confirm edit-mode ("Rearrange Players") chips are also larger and remain tappable/selectable.
4. Use Print Preview in both Bye states — confirm the printout reflects the toggle and that the Round column is narrower.
5. Verify the 🏆 championship row still renders correctly with the narrower Round column.
6. With "Track scores" on, confirm the score inputs and larger names still lay out acceptably (some extra wrapping is acceptable).
