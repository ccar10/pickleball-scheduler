# Printable Schedule Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refresh the Schedule page (screen + print) so saved-as-PDF output matches the branded NEPC samples: dark-blue banner, subtitle strip, styled table with header row and alternating row shading, landscape by default.

**Architecture:** Pure view/CSS refactor. Rewrite `Components/Pages/Schedule.razor` markup inside a new `.schedule-sheet` wrapper and replace the schedule-related CSS in `wwwroot/app.css` with a single WYSIWYG ruleset (same look on screen and in print). No new dependencies; no changes to models, services, routing, or schedule generation logic.

**Tech Stack:** .NET 8 Blazor Server, Razor, CSS (print media), Bootstrap 5 (CDN, retained for global chrome only).

**Spec:** `docs/superpowers/specs/2026-04-24-printable-schedule-design.md`

**Samples to match:** `sample-01.pdf` (8 players, 2 courts, 11 rounds) and `sample-02.pdf` (12 players, 3 courts, 11 rounds).

**Note on testing:** This is a visual/CSS refactor with no behavior change. The project's existing xUnit tests cover `ScheduleGenerator` only; there is no UI test harness. Verification is therefore visual: build, run the Blazor server, load the Schedule page, and save as PDF in the browser (landscape) to compare against the sample PDFs. The existing test suite must still pass as a regression check.

---

## File Structure

**Modified:**

- `PickleballScheduler/Components/Pages/Schedule.razor` — replace the body markup (everything between `@if (evt == null)` and `@code`) with a new `.schedule-sheet`-based layout. `@code` block, `@page`, `@inject`, parameter, `OnInitializedAsync`, and `Print` method remain unchanged.
- `PickleballScheduler/wwwroot/app.css` — remove the existing `.schedule-table` rules (lines ~20–32) and the entire `@media print` block (lines ~34–92); add a new block of rules scoped to `.schedule-sheet` covering both screen and print.

**Not touched:** `Models/*`, `Services/*`, `Data/*`, `Components/Pages/EventSetup.razor`, `Components/Pages/Home.razor`, `Components/Layout/MainLayout.razor`, `Program.cs`, tests.

---

## Task 1: Replace schedule CSS with branded ruleset

**Files:**

- Modify: `PickleballScheduler/wwwroot/app.css` (replace lines ~20–92: existing `.schedule-table` rules and the `@media print` block).

**Rationale:** Land the CSS first so when Task 2 swaps the markup, the new classes resolve immediately. The old `.schedule-table` / `.round-col` / `.bye-col` selectors disappear; the Schedule page is momentarily unstyled at the table until Task 2 runs, but the app still compiles and other pages are unaffected.

- [ ] **Step 1: Open `PickleballScheduler/wwwroot/app.css` and locate the schedule-related block**

It starts at the comment `/* Schedule table styling */` (around line 20) and ends at the closing brace of the `@media print` block (around line 92). Everything from that comment through the closing brace of `@media print` will be replaced. Leave the `#blazor-error-ui` rules at the top untouched.

- [ ] **Step 2: Replace that block with the new ruleset**

```css
/* Schedule sheet - branded WYSIWYG layout (screen + print) */
.schedule-sheet {
    border: 1px solid #333;
    background: #fff;
    max-width: 11in;
    margin: 0 auto;
    color: #212529;
    font-size: 10pt;
}

.schedule-sheet .sheet-banner {
    background: #1E4B8F;
    color: #fff;
    display: grid;
    grid-template-columns: 160px 1fr 160px;
    align-items: center;
    padding: 10px 14px;
}

.schedule-sheet .sheet-logo {
    height: 40px;
    width: auto;
    background: #fff;
    border-radius: 4px;
    padding: 4px 8px;
    justify-self: start;
}

.schedule-sheet .sheet-title {
    font-size: 18pt;
    font-weight: 400;
    margin: 0;
    text-align: center;
    grid-column: 2;
}

.schedule-sheet .sheet-subtitle {
    background: #E9EEF5;
    text-align: center;
    padding: 4px 8px;
    font-size: 9pt;
    color: #333;
    border-top: 1px solid #333;
    border-bottom: 1px solid #333;
}

.schedule-sheet .sheet-table {
    width: 100%;
    border-collapse: collapse;
    table-layout: fixed;
}

.schedule-sheet .sheet-table thead th {
    background: #DCE5F1;
    color: #1E4B8F;
    font-weight: 700;
    padding: 6px 8px;
    text-align: center;
    border-bottom: 1px solid #333;
}

.schedule-sheet .sheet-table thead th.round-col { text-align: left; padding-left: 14px; }

.schedule-sheet .sheet-table tbody tr:nth-child(even) {
    background: #F0F0F0;
}

.schedule-sheet .sheet-table td {
    padding: 8px 6px;
    text-align: center;
    vertical-align: middle;
    border-top: 1px solid #D0D0D0;
}

.schedule-sheet .sheet-table td.round-num {
    font-weight: 700;
    width: 60px;
}

.schedule-sheet .sheet-table td.bye-cell {
    width: 120px;
    font-style: italic;
    color: #555;
}

.schedule-sheet .sheet-table td.match-cell .vs {
    font-size: 75%;
    color: #777;
    line-height: 1.1;
}

/* Print tuning */
@page {
    size: landscape;
    margin: 0.4in;
}

@media print {
    .navbar,
    .no-print,
    #blazor-error-ui {
        display: none !important;
    }

    body {
        margin: 0;
        padding: 0;
        background: #fff;
    }

    .container {
        max-width: 100% !important;
        padding: 0 !important;
        margin: 0 !important;
    }

    .schedule-sheet,
    .schedule-sheet * {
        -webkit-print-color-adjust: exact;
        print-color-adjust: exact;
    }

    .schedule-sheet {
        max-width: none;
        font-size: 9pt;
        page-break-inside: avoid;
    }

    .schedule-sheet .sheet-table td { padding: 4px 6px; }
    .schedule-sheet .sheet-title { font-size: 16pt; }
    .schedule-sheet .sheet-banner { padding: 8px 12px; }

    a { text-decoration: none !important; color: inherit !important; }
}
```

- [ ] **Step 3: Build to confirm no CSS syntax errors broke the build**

Run from the repo root:

```bash
dotnet build PickleballScheduler/PickleballScheduler.csproj
```

Expected: `Build succeeded` with 0 errors. Warnings are acceptable.

- [ ] **Step 4: Commit**

```bash
git add PickleballScheduler/wwwroot/app.css
git commit -m "style: branded schedule sheet CSS (screen + print)"
```

---

## Task 2: Rewrite Schedule.razor markup

**Files:**

- Modify: `PickleballScheduler/Components/Pages/Schedule.razor` (replace the body inside the `else` branch; keep `@page`, `@inject`, `<PageTitle>`, loading branch, and `@code` block unchanged).

- [ ] **Step 1: Open `PickleballScheduler/Components/Pages/Schedule.razor` and replace the `else { ... }` block**

Replace the entire `else { ... }` block (currently the `schedule-header` div through the `</table>` and closing brace) with the block below. The rest of the file — `@page`, `@inject`, `<PageTitle>`, the loading branch, and the `@code { ... }` block — stays exactly as it is.

```razor
    else
    {
        <div class="no-print mb-3">
            <a href="/event/@evt.Id/setup" class="btn btn-outline-secondary btn-sm me-2">Edit Setup</a>
            <button class="btn btn-outline-primary btn-sm" @onclick="Print">Print Schedule</button>
        </div>

        @if (evt.Rounds.Count == 0)
        {
            <div class="alert alert-info">No schedule generated yet. <a href="/event/@evt.Id/setup">Go to setup</a> to generate one.</div>
        }
        else
        {
            <div class="schedule-sheet">
                <div class="sheet-banner">
                    <img src="/images/NEPC-Logo.svg" class="sheet-logo" alt="NEPC" />
                    <h1 class="sheet-title">@evt.Name</h1>
                </div>
                <div class="sheet-subtitle">
                    @evt.Date.ToString("MMMM d, yyyy") &middot; @evt.EventPlayers.Count players &middot; @courtNames.Count courts
                </div>
                <table class="sheet-table">
                    <thead>
                        <tr>
                            <th class="round-col">Round</th>
                            @foreach (var courtName in courtNames)
                            {
                                <th>@courtName</th>
                            }
                            <th>Bye</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var round in evt.Rounds.OrderBy(r => r.RoundNumber))
                        {
                            <tr>
                                <td class="round-num">@round.RoundNumber</td>
                                @for (int c = 0; c < courtNames.Count; c++)
                                {
                                    var courtNum = c + 1;
                                    var match = round.Matches.FirstOrDefault(m => m.CourtNumber == courtNum);
                                    <td class="match-cell">
                                        @if (match != null)
                                        {
                                            <div>@match.Team1Player1.Name &amp; @match.Team1Player2.Name</div>
                                            <div class="vs">vs</div>
                                            <div>@match.Team2Player1.Name &amp; @match.Team2Player2.Name</div>
                                        }
                                    </td>
                                }
                                <td class="bye-cell">
                                    @if (round.Byes.Any())
                                    {
                                        @string.Join(", ", round.Byes.Select(b => b.Player.Name))
                                    }
                                </td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        }
    }
```

Notes:

- The `<div class="no-print">` block with the buttons is kept so they show on screen but vanish when printing.
- The empty-rounds alert is preserved unchanged.
- The class names (`schedule-sheet`, `sheet-banner`, `sheet-logo`, `sheet-title`, `sheet-subtitle`, `sheet-table`, `round-col`, `round-num`, `match-cell`, `vs`, `bye-cell`) all match the CSS added in Task 1.

- [ ] **Step 2: Build to confirm Razor compiles**

```bash
dotnet build PickleballScheduler/PickleballScheduler.csproj
```

Expected: `Build succeeded` with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add PickleballScheduler/Components/Pages/Schedule.razor
git commit -m "feat: branded schedule sheet markup (logo banner, styled table)"
```

---

## Task 3: Visual verification against samples

**Files:** none modified — this task is manual inspection. The reviewer compares the rendered page and saved-PDF output against `sample-01.pdf` and `sample-02.pdf`.

- [ ] **Step 1: Run the app**

```bash
dotnet run --project PickleballScheduler/PickleballScheduler.csproj
```

The console prints the listening URL (typically `http://localhost:5000` or similar). Leave it running for the next steps.

- [ ] **Step 2: Load a schedule page in the browser**

If an event already exists in the local SQLite DB (`PickleballScheduler/Data/pickleball.db`), browse to `http://localhost:<port>/` and click into an event's schedule. Otherwise:

1. Go to `/event/new`.
2. Fill out an event that matches `sample-01.pdf`: name `Friendlies`, date `4/23/2026`, 2 courts named `Providence` and `Hartford`, 8 players (`Dan, Joanne, Brendon, Bridget, Lauren, Kim, Katy, Howard`), 11 rounds.
3. Generate the schedule — you land on `/event/{id}/schedule`.

- [ ] **Step 3: Compare the on-screen view to `sample-01.pdf`**

Visually verify all of:

- Dark-blue banner with the NEPC logo on the left and event title centered.
- Pale-blue subtitle row with the `"April 23, 2026 · 8 players · 2 courts"` line.
- Header row `Round | Providence | Hartford | Bye` on a pale-blue background with dark-blue text.
- Alternating white / light-gray rows in the table body.
- Each match cell shows Team1 / `vs` (smaller, muted) / Team2, centered.
- `Edit Setup` and `Print Schedule` buttons sit above the sheet, outside the branded block.

If any of these is visibly wrong, edit `wwwroot/app.css` to fix (e.g., adjust color hex, padding, grid columns), rebuild, refresh, and re-compare. Do not move on until the screen view matches the sample's layout.

- [ ] **Step 4: Save as PDF and compare to `sample-01.pdf`**

In the browser's print dialog (Ctrl+P):

1. Destination: `Save as PDF`.
2. Layout: `Landscape` (should already be the default via `@page`).
3. Margins: `Default`.
4. **Options → Background graphics: ON** (required for the blue/gray colors to render; note: users have to enable this the first time — that is the accepted tradeoff for the browser-print approach).
5. Save and open the resulting PDF.

Verify against `sample-01.pdf`:

- One page, landscape.
- Navbar and the Print/Edit buttons are absent.
- Banner, subtitle, table styling all rendered in color.
- All 11 rounds fit on a single page.

- [ ] **Step 5: Repeat for the 3-court case (`sample-02.pdf`)**

Create a second event matching `sample-02.pdf`: `2.5 & 3.0 Mixed`, 12 players, 3 courts (`Providence`, `Hartford`, `Montpelier`), 11 rounds. Load its schedule, save as PDF, and verify a single landscape page with three equal-width court columns.

- [ ] **Step 6: Stop the dev server**

Ctrl+C in the `dotnet run` terminal.

- [ ] **Step 7: If any CSS adjustments were made during verification, commit them**

```bash
git add PickleballScheduler/wwwroot/app.css
git commit -m "style: fine-tune schedule sheet to match samples"
```

If no adjustments were needed, skip this step — nothing to commit.

---

## Task 4: Regression check — run existing tests

**Files:** none modified.

- [ ] **Step 1: Run the test suite**

```bash
dotnet test PickleballScheduler.Tests/PickleballScheduler.Tests.csproj
```

Expected: all tests pass. No test targets the view layer, so a pass confirms the `ScheduleGenerator` and data-layer paths are untouched — the only behavior this plan changes is visual.

- [ ] **Step 2: If tests pass, the feature is complete**

No further commits required. The branch now contains:

1. `style: branded schedule sheet CSS (screen + print)`
2. `feat: branded schedule sheet markup (logo banner, styled table)`
3. (Optional) `style: fine-tune schedule sheet to match samples`

- [ ] **Step 3: If tests fail, stop**

A failure here would be a surprise (this plan touches no code paths the tests cover). Investigate the failure before declaring the feature done. Do not "fix" by editing tests.

---

## Self-Review Notes

- **Spec coverage:** Banner + subtitle (Task 2 markup, Task 1 CSS), styled table header (Task 1 CSS for `thead th`), alternating rows (Task 1 `tr:nth-child(even)`), landscape default (Task 1 `@page`), print-color-adjust (Task 1 `@media print`), unchanged models/services (Tasks only touch `.razor` + `.css`, confirmed in File Structure). All spec sections mapped.
- **Placeholder scan:** No TBDs. All CSS rules and Razor blocks are shown in full. Color hex values are explicit (`#1E4B8F`, `#DCE5F1`, `#E9EEF5`, `#F0F0F0`, `#333`) and flagged as tunable in Task 3 verification.
- **Class-name consistency:** CSS classes in Task 1 (`schedule-sheet`, `sheet-banner`, `sheet-logo`, `sheet-title`, `sheet-subtitle`, `sheet-table`, `round-col`, `round-num`, `match-cell`, `vs`, `bye-cell`) exactly match the ones used in the Task 2 markup.
