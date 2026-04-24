# Printable Schedule Design

**Date:** 2026-04-24
**Status:** Approved

## Goal

Make the generated round-robin schedule print to a clean, branded one-page layout that matches the provided samples (`sample-01.pdf`, `sample-02.pdf`): blue NEPC banner, styled table with header bar, alternating row shading, landscape orientation.

## Approach

Browser-driven print — keep the existing `window.print()` flow and polish the on-screen view so "Save as PDF" from the browser produces output matching the samples. No new dependencies; WYSIWYG (screen view = print output).

Decisions confirmed during brainstorming:

- **Browser print + CSS**, not server-side PDF generation.
- **WYSIWYG**: on-screen Schedule page adopts the branded layout.
- **Landscape** default page orientation for all schedules.

## Scope

Single feature: visual refresh of the Schedule page for both screen display and print output.

**In scope:**

- Rewrite the markup in `PickleballScheduler/Components/Pages/Schedule.razor`.
- Rewrite the schedule-related CSS in `PickleballScheduler/wwwroot/app.css` (screen + print).
- Ensure print output matches the provided samples.

**Out of scope:**

- Changes to data models, `EventService`, or `ScheduleGenerator`.
- Routing / navigation changes.
- Editable branding (logo stays hardcoded NEPC as today).
- Server-side PDF generation, download endpoint, or new dependencies.

## Visual Layout

The Schedule page renders a single `.schedule-sheet` block. Above it, a `.no-print` row holds the Print and Edit Setup buttons.

```
┌─────────────────────────────────────────────────────────────┐
│ [LOGO]   Friendlies - 4/23/2026 7:00 AM - 8:30 AM           │  ← dark blue banner
├─────────────────────────────────────────────────────────────┤
│            April 23, 2026 · 8 players · 2 courts            │  ← pale subtitle row
├───────┬────────────────┬────────────────┬──────────────────┤
│ Round │  Providence    │   Hartford     │      Bye         │  ← pale-blue header row
├───────┼────────────────┼────────────────┼──────────────────┤
│   1   │ Dan & Joanne   │ Brendon & ...  │                  │  ← white
│       │      vs        │      vs        │                  │
│       │ Lauren & Kim   │ Katy & Howard  │                  │
├───────┼────────────────┼────────────────┼──────────────────┤
│   2   │ ...            │ ...            │                  │  ← light gray
│       │      vs        │      vs        │                  │
│       │ ...            │ ...            │                  │
└───────┴────────────────┴────────────────┴──────────────────┘
```

### Color / typography

- Banner background: dark blue, ~`#1E4B8F` (eyeballed from samples; tune to match).
- Banner title: white, ~18pt, centered.
- Subtitle row: pale background (~`#E9EEF5`), small muted text, centered.
- Table header row: pale blue (~`#DCE5F1`), bold dark-blue text.
- Table body: alternating white and `#F0F0F0` rows (zebra via `tbody tr:nth-child(even)`).
- Outer border: 1px solid `#333` around the whole sheet.
- Cell content: Team1 name, `vs` (smaller + muted), Team2 name — stacked, centered, as today.

### Column sizing

- `Round` column: narrow fixed width (~60px).
- Court columns: equal flex width.
- `Bye` column: medium fixed width (~120px).

## Print CSS

```css
@page {
  size: landscape;
  margin: 0.4in;
}

@media print {
  /* hide app chrome */
  .navbar, .no-print { display: none !important; }

  /* keep colors on print */
  .schedule-sheet,
  .schedule-sheet * {
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
  }

  .sheet-table { page-break-inside: avoid; font-size: 9pt; }
}
```

Base screen font size 10pt for the table; print drops to 9pt. `vs` lines are 75% size and muted. One-page fit is preserved for typical events (8–12 players, 2–3 courts, up to ~12 rounds); larger events rely on the browser's "Fit to page" scaling — same fallback as today.

## Markup sketch (Schedule.razor)

```razor
<div class="no-print mb-2">
  <a href="/event/@evt.Id/setup" class="btn btn-outline-secondary btn-sm me-2">Edit Setup</a>
  <button class="btn btn-outline-primary btn-sm" @onclick="Print">Print Schedule</button>
</div>

<div class="schedule-sheet">
  <div class="sheet-banner">
    <img src="/images/NEPC-Logo.svg" class="sheet-logo" alt="NEPC" />
    <h1 class="sheet-title">@evt.Name</h1>
  </div>
  <div class="sheet-subtitle">
    @evt.Date.ToString("MMMM d, yyyy") · @evt.EventPlayers.Count players · @courtNames.Count courts
  </div>
  <table class="sheet-table">
    <thead>
      <tr>
        <th class="round-col">Round</th>
        @foreach (var name in courtNames) { <th>@name</th> }
        <th class="bye-col">Bye</th>
      </tr>
    </thead>
    <tbody>
      @foreach (var round in evt.Rounds.OrderBy(r => r.RoundNumber))
      {
        <tr>
          <td class="round-num">@round.RoundNumber</td>
          @for (int c = 0; c < courtNames.Count; c++)
          {
            var match = round.Matches.FirstOrDefault(m => m.CourtNumber == c + 1);
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
            @if (round.Byes.Any()) { @string.Join(", ", round.Byes.Select(b => b.Player.Name)) }
          </td>
        </tr>
      }
    </tbody>
  </table>
</div>
```

## Files affected

- `PickleballScheduler/Components/Pages/Schedule.razor` — rewrite the view body.
- `PickleballScheduler/wwwroot/app.css` — replace the existing `schedule-table` + `@media print` rules with new `schedule-sheet` rules.

No changes outside those two files.

## Testing

- Load `/event/{id}/schedule` in browser; confirm banner + table match the samples.
- Use browser "Save as PDF" (landscape), confirm output visually matches `sample-01.pdf` (2 courts, 8 players) and `sample-02.pdf` (3 courts, 12 players).
- Confirm navbar and Print/Edit buttons do not appear in the PDF.
- Confirm background colors render in the PDF (print-color-adjust working).
- Regression check: existing `PickleballScheduler.Tests` (ScheduleGenerator tests) still pass — no code-path changes, but run for safety.
