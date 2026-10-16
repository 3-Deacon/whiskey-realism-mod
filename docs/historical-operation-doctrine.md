# Historical Operation Doctrine

Living reference for how history enters the strategic layer: as a weight on the campaign planner's score, not as a script. Source of truth remains shipped code first, then `docs/patch-catalog.md`, then this note. Build, deploy and smoke status live in [`docs/handoff.md`](handoff.md). The archived 2026-05 design and plan describe the retired phased-plan catalog and are historical context only:

- `docs/superpowers/specs/archive/2026-05-06-historical-operation-doctrine-design.md`
- `docs/superpowers/plans/archive/2026-05-06-historical-operation-doctrine.md`

## What changed (stage G)

The named-operation catalog (`HistoricalOperationCatalog`), its phase targets, dynamic rules, tempo presets, `PhaseTruthLedger`, `OperationDecisionMemory` and the CIC plans they fed were deleted with the phased-plan CIC (G6). The campaign planner (spec `docs/superpowers/specs/2026-10-14-campaign-planner-design.md`) picks one main effort per alliance from live, fog-honest state and applies history as a small, decaying prior. `Enable Historical Operation Doctrine` is gone (no reader); `Plan Trace Logging` now traces the planner's per-candidate scores.

## The prior (`Strategic/Planner/HistoricalPriorTable.cs`)

The catalog's six profiles reduced to `(alliance, objective ids, window, tier)` rows, pinned by `TheaterPlannerTests.PriorTableRows`:

| Id | Alliance | Objective | Window | Tier |
|---|---|---|---|---|
| `union-east-pressure` | Union | 3 (Richmond) | Apr-Dec 1861 | 3 |
| `csa-valley-disruption` | CSA | 32 | Apr-Dec 1861 | 2 |
| `union-coastal-pressure` | Union | 37 | Jan-Dec 1862 | 1 |
| `union-western-pressure` | Union | 29 | Jan-Dec 1862 | 3 |
| `csa-western-depth` | CSA | 30 | Jan-Dec 1862 | 3 |
| `union-late-pressure` | Union | 3 | May 1864-Dec 1865 | 3 |

(Tier 3/2/1 is the catalog's priority 10/15/20, which sorted ascending. `union-late-pressure` starts in May 1864, the month `fix/chapters` moved vanilla's chapter 3 to.)

- **Weight** = 1 inside the window, `0.5^(monthsOutside / 3)` outside (months of the day serial's own mean month length), times `tier / 3`, so it never exceeds 1. The strongest row naming the candidate objective wins.
- **Contribution** to a candidate's score = `PriorLambda (0.08) x weight`, capped at `PriorMaxContribution (0.08)`. Every other score term is normalized to [0, 1] and weighted 0.10-0.30, and the main effort switches only when a challenger beats the current choice by `SwitchMargin (0.12)` for 3 reviews, so a live margin above 0.08 always beats history (`LiveMarginAbovePriorMaxWins`). History breaks ties; it does not decide.
- The row that backed the chosen main effort is `prior=<id>` in the `[CampaignPlan]` row (`TheaterPlan.PriorId`).
- A 1861 objective with no towns in `campaignobjectives.dat` is mapped to its towns by `ObjectiveGround` (stage G3c) before it can be a candidate, still gated by vanilla's chapters and deactivation rules.

## Other places history is a weight

- **Commanders:** `SuccessionScheduler` still fires the ~12 scripted events on their dates; `SuccessionScheduler.PriorFor` turns a fired, unapplied event into a bonus on the named figure's score for the role (Valley, ANV/AoP, Western), and `CommanderAppointmentPlanner` seats the best-scoring available commander on the main-effort commands, at most one a day, protected from vanilla's job 24 for 60 days. History cannot override competence by more than the prior's bonus, and a figure vanilla seated on an auto-created army group is moved off it only when an idle successor reseats it first.
- **Home theater:** `HomeTheaterPrior` (the retired army-area registry's doctrines reduced to name -> `Theater`) is a 5 % distance discount in the allocator's Hold ordering, a tie-break only.

## Not carried

Phase postures, package gates, per-operation tempo presets, dynamic advance/abort/pivot rules and the `HistoricalOperation` telemetry events. The planner's own triggers (capital threat, an accomplished or unavailable objective, a decisive battle near the target, odds falling below the required ratio) replace the rules.

## Verification

Harness: `TheaterPlannerTests` (`PriorTableRows`, `PriorDecaysOutsideWindow`, `LiveMarginAbovePriorMaxWins`, `HysteresisHoldsBelowMargin`), `CommanderPlannerTests` (succession prior). In-game proof (the `prior=` field and a main effort that follows the historical objective in its window) is part of the stage G smoke in [`docs/handoff.md`](handoff.md); do not claim it before the log shows it.
