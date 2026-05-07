# `docs/superpowers/`

Slice-level design and execution artifacts. Living-doc state lives in [`docs/handoff.md`](../handoff.md), [`docs/patch-catalog.md`](../patch-catalog.md), [`docs/findings.md`](../findings.md), [`docs/bug-fixes/`](../bug-fixes/), [`docs/fort-construction-governor.md`](../fort-construction-governor.md), [`docs/operational-tempo-doctrine.md`](../operational-tempo-doctrine.md), [`docs/wl-dispatch-objective-bridge.md`](../wl-dispatch-objective-bridge.md), [`docs/coordinated-operation-packages.md`](../coordinated-operation-packages.md), [`docs/historical-operation-doctrine.md`](../historical-operation-doctrine.md), and [`MEMORY.md`](../../MEMORY.md).

## Layout

- [`specs/`](specs/) — **active** design specs (current and upcoming slices).
- [`specs/archive/`](specs/archive/) — design specs whose implementation has shipped. See the archive [README](specs/archive/README.md) for the index.
- [`plans/`](plans/) — **active** implementation plans. Current active sequence: Slice B tactical brain. B0 observer smoke is closed; B1 W&L charge/feud guard, B2 command/order-friction telemetry, and tactical bug-remediation telemetry/#43 guard are implemented and hash-deployed. Focused B2/bug smoke confirmed `BUG-TAC-005` objective-chain player-subordinate exposure but did not exercise #43 or prove movement deltas; B3 tactical odds doctrine is next. W&L dispatch bridge, coordinated operation packages, and historical operation doctrine are archived; current behavior lives in the living docs above.
- [`plans/archive/`](plans/archive/) — implementation plans whose patches have shipped. See the archive [README](plans/archive/README.md) for the index.

Cross-cutting vanilla bug fixes do not live here unless they grow into a real design slice. Track those in [`../bug-fixes/`](../bug-fixes/).

## Lifecycle

1. **Brainstorm** → unstructured notes; usually captured in chat / handoff section, not committed as a spec.
2. **Spec** → committed under `specs/`. Adversarial review pass before plan-writing.
3. **Plan** → committed under `plans/`. Bite-sized tasks for subagent-driven-development.
4. **Implement** → patches land in `src/WhiskeyRealism/`, get an ordinal in `docs/patch-catalog.md`, smoke verified.
5. **Archive** → once the slice ships and is verified, both spec and plan move to `archive/`. Internal cross-refs are rewritten to archive paths.

When a slice's design materially changes after shipping (new findings, new constraint), prefer recording the delta in `docs/handoff.md` "What just shipped" and `MEMORY.md` Load-Bearing Runtime Lessons rather than mutating the archived spec.
