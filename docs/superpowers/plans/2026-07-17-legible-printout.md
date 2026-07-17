# More Legible Printed Schedule — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the printed round-robin schedule more readable by letting the user hide the Bye column, shrinking the Round column, and enlarging the player names.

**Architecture:** Presentation-only changes to one Blazor page (`Schedule.razor`) and the global stylesheet (`wwwroot/app.css`). No scheduling algorithm, data model, or database changes. The Bye toggle is ephemeral component state (like the existing "Track scores" toggle), so nothing is persisted.

**Tech Stack:** Blazor Server (.NET 8), Razor components, plain CSS. Existing xUnit test project (`PickleballScheduler.Tests`) covers services/models only — there is no bUnit component-test harness, and adding one is out of scope. These tasks are verified by `dotnet build` plus manual browser + Print Preview checks, as specified in the design.

## Global Constraints

- No changes to the scheduling algorithm, `Event`/`Round`/`Match` models, or the database schema.
- The Bye toggle must NOT persist to the database — it is ephemeral component state.
- Bye column defaults to **shown** (`showByeColumn = true`), preserving today's behavior.
- Names grow ~30% (13pt screen / 12pt print); Round number, column headers, and the "vs" label keep their current small sizes.
- The sheet table keeps `table-layout: fixed` — column width is redistributed automatically when a column is removed.

---

### Task 1: Bye-column toggle + narrower Round column

**Files:**
- Modify: `PickleballScheduler/Components/Pages/Schedule.razor` (toolbar markup ~lines 25-40; table header ~lines 124-131; bye `<td>` ~lines 167-177; `@code` fields ~lines 216-231)
- Modify: `PickleballScheduler/wwwroot/app.css` (`td.round-num` rule at lines 96-99)

**Interfaces:**
- Consumes: nothing new.
- Produces: `private bool showByeColumn` field on `Schedule.razor`; the Bye `<th>` and `.bye-cell` `<td>` become conditional on it.

- [ ] **Step 1: Add the `showByeColumn` field**

In the `@code` block of `Schedule.razor`, alongside the other ephemeral UI fields (near `private bool trackScores;` at line 226), add:

```csharp
    private bool showByeColumn = true;
```

- [ ] **Step 2: Add the toggle checkbox to the toolbar**

In the `.no-print mb-3` toolbar, immediately AFTER the existing "Track scores" `<label>` block (which ends at line 27, before the `@if (trackScores)` block at line 28), add a matching label:

```razor
        <label class="me-2 small align-middle" style="cursor: pointer; user-select: none;">
            <input type="checkbox" class="form-check-input me-1" @bind="showByeColumn" /> Show Bye column
        </label>
```

- [ ] **Step 3: Make the Bye header cell conditional**

In the `<thead>`, wrap the Bye header (line 130) in a conditional:

```razor
                        @if (showByeColumn)
                        {
                            <th>Bye</th>
                        }
```

- [ ] **Step 4: Make the Bye data cell conditional**

In the `<tbody>` round loop, wrap the entire `<td class="bye-cell">…</td>` block (lines 167-177) in a conditional:

```razor
                            @if (showByeColumn)
                            {
                                <td class="bye-cell">
                                    @{ var byeList = round.Byes.ToList(); }
                                    @for (int b = 0; b < byeList.Count; b++)
                                    {
                                        if (b > 0)
                                        {
                                            <span>, </span>
                                        }
                                        @PlayerSpan(round.Id, byeList[b].Player)
                                    }
                                </td>
                            }
```

- [ ] **Step 5: Shrink the Round column**

In `wwwroot/app.css`, change the `td.round-num` width (line 98) from `60px` to `38px`:

```css
.schedule-sheet .sheet-table td.round-num {
    font-weight: 700;
    width: 38px;
}
```

- [ ] **Step 6: Build to verify it compiles**

Run: `dotnet build PickleballScheduler/PickleballScheduler.csproj`
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 7: Manual verification**

Run the app (`dotnet run --project PickleballScheduler`), open an event's Schedule page, and confirm:
- The "Show Bye column" checkbox appears next to "Track scores" and is checked by default.
- Unchecking it removes the Bye column (header + all cells) and the match columns widen to fill the space.
- Re-checking it restores the column.
- The Round column is visibly narrower; the 🏆 championship row still renders correctly.
- Print Preview (landscape) reflects the current toggle state in both on and off states.

- [ ] **Step 8: Commit**

```bash
git add PickleballScheduler/Components/Pages/Schedule.razor PickleballScheduler/wwwroot/app.css
git commit -m "feat(schedule): add Show Bye column toggle and shrink Round column"
```

---

### Task 2: Larger, bolder player names

**Files:**
- Modify: `PickleballScheduler/wwwroot/app.css` (add a `.match-cell .team-line` screen rule near lines 107-111; add a print override inside the `@media print` block near lines 195-197)

**Interfaces:**
- Consumes: the `.match-cell` / `.team-line` structure already rendered by `Schedule.razor`.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Add the screen font rule**

In `wwwroot/app.css`, after the `.match-cell .vs` rule (ends at line 111), add:

```css
.schedule-sheet .sheet-table td.match-cell .team-line {
    font-size: 13pt;
    font-weight: 600;
}
```

- [ ] **Step 2: Add the print override**

Inside the existing `@media print` block, after the `.schedule-sheet .sheet-table td { padding: 4px 6px; }` line (line 195), add:

```css
    .schedule-sheet .sheet-table td.match-cell .team-line { font-size: 12pt; }
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build PickleballScheduler/PickleballScheduler.csproj`
Expected: `Build succeeded` with 0 errors. (CSS is static content; this confirms nothing else broke.)

- [ ] **Step 4: Manual verification**

Run the app and on the Schedule page confirm:
- Player names are visibly larger and bolder than the Round number, headers, and "vs".
- Edit mode ("Rearrange Players") chips are also larger and still tappable/selectable.
- Print Preview shows the larger names at ~12pt.
- With "Track scores" on, names and score inputs still lay out acceptably (minor extra wrapping on long names is acceptable).

- [ ] **Step 5: Commit**

```bash
git add PickleballScheduler/wwwroot/app.css
git commit -m "feat(schedule): enlarge and embolden player names on screen and print"
```

---

## Self-Review

**Spec coverage:**
- Goal 1 (hide Bye column, reclaim width) → Task 1 Steps 1-4 (toggle + conditional rendering); `table-layout: fixed` redistributes width.
- Goal 2 (shrink Round column) → Task 1 Step 5.
- Goal 3 (larger, bolder names) → Task 2 Steps 1-2.
- Non-goal "no persistence" → honored: `showByeColumn` is a plain ephemeral field, no DB touch.
- Non-goal "headers/round/vs unchanged" → honored: font rule targets only `.match-cell .team-line`.
- Testing section → mapped to build + manual Print Preview steps in both tasks.

**Placeholder scan:** No TBD/TODO/"handle edge cases"/vague steps — every code step shows exact content.

**Type consistency:** Field name `showByeColumn` used identically in the field declaration, checkbox `@bind`, and both `@if` conditions. CSS selectors match the existing `.schedule-sheet .sheet-table td.match-cell .team-line` / `td.round-num` structure in `Schedule.razor` and `app.css`.
