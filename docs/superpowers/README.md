# `docs/superpowers/`

Slice-level design and execution artifacts. Living-doc state lives in [`docs/handoff.md`](../handoff.md), [`docs/patch-catalog.md`](../patch-catalog.md), [`docs/tactical-orchestrator.md`](../tactical-orchestrator.md), [`docs/tactical-operations-ledger.md`](../tactical-operations-ledger.md), [`docs/tactical-terrain-facing-discipline.md`](../tactical-terrain-facing-discipline.md), [`docs/findings.md`](../findings.md), [`docs/bug-fixes/`](../bug-fixes/), [`docs/fort-construction-governor.md`](../fort-construction-governor.md), [`docs/operational-tempo-doctrine.md`](../operational-tempo-doctrine.md), [`docs/wl-dispatch-objective-bridge.md`](../wl-dispatch-objective-bridge.md), [`docs/coordinated-operation-packages.md`](../coordinated-operation-packages.md), [`docs/historical-operation-doctrine.md`](../historical-operation-doctrine.md), and [`MEMORY.md`](../../MEMORY.md).

## Layout

- [`specs/`](specs/) — **active** design specs (current and upcoming slices).
- [`specs/archive/`](specs/archive/) — design specs whose implementation has shipped. See the archive [README](specs/archive/README.md) for the index.
- [`plans/`](plans/) — **active** implementation plans. Current tactical execution artifacts are implementation-complete and remain active only for smoke/archive closeout: Slice 1 reserve commitment, Slice 3 charge gate, #60 terrain/facing discipline, full-spectrum command doctrine, W&L player-order doctrine, and [`2026-05-10-tactical-operations-ledger-command-system-implementation-plan.md`](plans/2026-05-10-tactical-operations-ledger-command-system-implementation-plan.md). Runtime behavior, config, smoke checklist, and rollback live in [`../tactical-orchestrator.md`](../tactical-orchestrator.md), [`../tactical-operations-ledger.md`](../tactical-operations-ledger.md), and [`../tactical-terrain-facing-discipline.md`](../tactical-terrain-facing-discipline.md). Current `main` is build/deploy/hash verified at SHA-256 `f2e7705b96c55ea371ca08a3a56d28ebf324bfc114618c184ccba375d17ee1f1` (1027072 bytes), with fresh Active operations-ledger smoke pending. Do not mine active plans for current operational state once a living doc exists; use plans for traceability and unfinished checklist context only.
- [`plans/archive/`](plans/archive/) — implementation plans whose patches have shipped. See the archive [README](plans/archive/README.md) for the index.

Cross-cutting vanilla bug fixes do not live here unless they grow into a real design slice. Track those in [`../bug-fixes/`](../bug-fixes/).

## Lifecycle

1. **Brainstorm** → unstructured notes; usually captured in chat / handoff section, not committed as a spec.
2. **Spec** → committed under `specs/`. Adversarial review pass before plan-writing.
3. **Plan** → committed under `plans/`. Bite-sized tasks for subagent-driven-development.
4. **Implement** → patches land in `src/WhiskeyRealism/`, get an ordinal in `docs/patch-catalog.md`, smoke verified.
5. **Archive** → once the slice ships and is verified, both spec and plan move to `archive/`. Internal cross-refs are rewritten to archive paths.

When a slice's design materially changes after shipping (new findings, new constraint), prefer recording the delta in `docs/handoff.md` "What just shipped" and `MEMORY.md` Load-Bearing Runtime Lessons rather than mutating the archived spec.
