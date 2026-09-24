# Specification: Yield Statistics Dashboard (v1)

Status: Draft for review · Target release: next feature cycle after queue persistence

## Goal

After an automated wafer scan (hundreds of contacts), answer two questions immediately instead of by manual inspection:

1. **How many** contacted devices work? (yield)
2. **Where** on the wafer are the working devices? (spatial pattern)

## Data Foundation (already exists)

- Per-contact classification via `MemristorCheckService`: `Classification`
  (`Very good candidate` → `Poor / probably noise`, plus `Not evaluable`),
  weighted `Score01` (0..1), `Flags`
- Loaded wafer-scan results: 16×16 cells × 5×5 sub-cells × contacts with
  curve data per contact (`ResultTabView` / `ResultCells`)
- Profile/device tagging on every stored measurement (CSV folder structure
  and MySQL) — basis for cross-batch comparison later

No schema changes required for v1; everything aggregates from the already
loaded result set.

## Scope v1 (MVP)

### 1. Yield Summary Panel

Header numbers for the currently loaded scan:

```
Measured: 214/288   Evaluable: 198   Candidates: 96 (48%)
─────────────────────────────────────────────────────
Very good: 31 | Good: 65 | Possible: 74 | Weak: 21 | Poor: 7 | n/a: 16
```

- "Candidate" = `Good` + `Very good` (+ configurable inclusion of `Possible`)
- Percentages relative to *evaluable* contacts (excluded ones listed separately)

### 2. Spatial Yield Map

Reuse the existing 16×16 grid control; each cell is colored by the share of
candidate contacts within that cell:

- Green ≥ 75 % · Yellow 25–75 % · Red < 25 % · Gray = no data
- Tooltip per cell: `Cell 0713 — 5/6 candidates (83 %)`
- Click → selects that cell in the existing result view

This turns process patterns (edge effects, dead columns, thickness gradients)
into something visible at a glance.

### 3. Score Distribution Histogram

- X: `Score01` (20 bins) · Y: contact count
- Vertical markers at the current `Good/Poor` thresholds
- Uses the active metric weights (already user-adjustable) so the histogram
  updates live when weights change — same recalculation hook as the metrics

## UI Placement

New collapsible **"Yield Overview"** section at the top of the Result tab,
above/beside the existing wafermap. No new top-level tab in v1 to keep
navigation unchanged.

## Architecture

- New pure computation service `YieldStatisticsService`:
  input = enumerable of per-contact classification results (+ cell coordinates),
  output = plain DTOs (`YieldSummary`, `YieldCellStat[]`, `int[] histogramBins`)
  → fully unit-testable, no UI dependency
- ViewModels bind to DTOs; coloring/threshold logic lives in the service
- Recalculation triggered by the same events that refresh metrics today
  (folder load, weight changes)

## Explicitly Out of Scope (later versions)

- Cross-profile / cross-batch comparison view
- Database-wide statistics (v1 covers the loaded scan only)
- Set-voltage / forming-voltage distributions (requires storing extracted
  features per contact first)
- CSV/report export of yield tables
- Cycle-to-cycle stability ranking

## Acceptance Criteria

1. Loading a scan folder shows measured/evaluable/candidate counts matching
   the per-contact classifications shown in the detail view.
2. A cell with zero data renders gray; a cell where all contacts are
   candidates renders green; mixed renders yellow/red accordingly.
3. Changing metric weights updates summary, map, and histogram without
   reloading files.
4. All aggregation math is covered by unit tests (synthetic scans with known
   yields).

## Open Questions

1. Should `Possible candidate` count toward yield by default, or stay a
   separate bucket? (v1 default: separate, toggleable)
2. Sub-cell level coloring needed, or is cell-level enough for v1?
   (v1 default: cell-level)
3. Minimum number of measured contacts before a cell counts as "no data"
   vs. "0 % yield"? (v1 default: ≥ 1 measurement)
