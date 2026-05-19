# `docs/superpowers/`

Slice-level design and execution artifacts. Living-doc state lives in [`docs/handoff.md`](../handoff.md), [`docs/patch-catalog.md`](../patch-catalog.md), [`docs/tactical-orchestrator.md`](../tactical-orchestrator.md), [`docs/tactical-operations-ledger.md`](../tactical-operations-ledger.md), [`docs/tactical-terrain-facing-discipline.md`](../tactical-terrain-facing-discipline.md), [`docs/tactical-brain.md`](../tactical-brain.md), [`docs/strategic-recon-commitment.md`](../strategic-recon-commitment.md), [`docs/telemetry.md`](../telemetry.md), [`docs/findings.md`](../findings.md), [`docs/bug-fixes/`](../bug-fixes/), [`docs/fort-construction-governor.md`](../fort-construction-governor.md), [`docs/operational-tempo-doctrine.md`](../operational-tempo-doctrine.md), [`docs/wl-dispatch-objective-bridge.md`](../wl-dispatch-objective-bridge.md), [`docs/wl-player-order-doctrine.md`](../wl-player-order-doctrine.md), [`docs/coordinated-operation-packages.md`](../coordinated-operation-packages.md), [`docs/historical-operation-doctrine.md`](../historical-operation-doctrine.md), and [`MEMORY.md`](../../MEMORY.md).

## Layout

- [`specs/`](specs/) — reserved for new approved design specs. It is intentionally empty after the 2026-05-17 closeout; current and backlog state has living-doc homes.
- [`specs/archive/`](specs/archive/) — frozen design specs and historical design supplements. See the archive [README](specs/archive/README.md) for the index.
- [`plans/`](plans/) — reserved for new approved implementation plans. It is intentionally empty after the 2026-05-17 closeout; current smoke/checklist state lives in living docs.
- [`plans/archive/`](plans/archive/) — frozen implementation plans and traceability artifacts. See the archive [README](plans/archive/README.md) for the index.

Current runtime behavior, config, smoke checklist, rollback, and remaining proof gates live in the living docs listed above. Current `main` is build/deploy/hash verified at SHA-256 `562a61b5cd0cbbedc6d6002a349cd3d68ebf50ea1d60c941e3a5a9deeaafc57a` (1327104 bytes; 1110 PASS), with fresh Active operations-ledger smoke and telemetry profile smoke still pending after a game restart onto that DLL. Do not mine archived plans/specs for current operational state once a living doc exists; use them for traceability only.

Cross-cutting vanilla bug fixes do not live here unless they grow into a real design slice. Track those in [`../bug-fixes/`](../bug-fixes/).

## Lifecycle

1. **Brainstorm** → unstructured notes; usually captured in chat / handoff section, not committed as a spec.
2. **Spec** → committed under `specs/`. Adversarial review pass before plan-writing.
3. **Plan** → committed under `plans/`. Bite-sized tasks for subagent-driven-development.
4. **Implement** → patches land in `src/WhiskeyRealism/`, get an ordinal in `docs/patch-catalog.md`, smoke verified.
5. **Archive** → once the slice has a living-doc home, both spec and plan move to `archive/`. Internal cross-refs are rewritten to archive paths. Runtime smoke gaps stay in the living docs until proven.

When a slice's design materially changes after shipping (new findings, new constraint), prefer recording the delta in `docs/handoff.md` "What just shipped" and `MEMORY.md` Load-Bearing Runtime Lessons rather than mutating the archived spec.
