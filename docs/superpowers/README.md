# `docs/superpowers/`

Slice-level design and execution artifacts. Living-doc state lives in [`docs/handoff.md`](../handoff.md), [`docs/patch-catalog.md`](../patch-catalog.md), [`docs/findings.md`](../findings.md), [`docs/bug-fixes/`](../bug-fixes/), [`docs/fort-construction-governor.md`](../fort-construction-governor.md), [`docs/operational-tempo-doctrine.md`](../operational-tempo-doctrine.md), [`docs/wl-dispatch-objective-bridge.md`](../wl-dispatch-objective-bridge.md), [`docs/coordinated-operation-packages.md`](../coordinated-operation-packages.md), [`docs/historical-operation-doctrine.md`](../historical-operation-doctrine.md), and [`MEMORY.md`](../../MEMORY.md).

## Layout

- [`specs/`](specs/) — **active** design specs (current and upcoming slices).
- [`specs/archive/`](specs/archive/) — design specs whose implementation has shipped. See the archive [README](specs/archive/README.md) for the index.
- [`plans/`](plans/) — **active** implementation plans. Current active sequence: Slice B tactical brain. B0 observer smoke is closed; B1 W&L charge/feud guard, B2 command/order-friction telemetry, B3 odds telemetry, B4/B5 default-off macro/group stance patches, tactical bug-remediation telemetry/#43 guard, #35 `[TacticalDecisionMatrix]` logging, #46 objective-chain W&L guard, and #53 `TacticalPathfinderDisciplinePatch` are implemented and hash-deployed. Current deployed DLL is `b07bbd39eaaf664d81d5930b42d5dc268b64c43dc98244a17015c49e329ce88a` (637952 bytes). B7+B8 runtime wiring is initial-smoke confirmed; the live smoke config currently has `Enable Tactical Artillery Doctrine = true`, `Enable Tactical Withdrawal Doctrine = true`, and `Enable Tactical Pathfinder Discipline = true`, while C# defaults remain false. Confirmed markers include `[once:b7-check-ai-bombardment]`, `[once:b7-counterbattery]`, `[once:b8-check-line-fallbacks]`, `[once:b8-morale-snapshot-sampler]`, `[once:b8-check-reserves]`, and `[once:b8-set-withdrawal]`; `[once:b8-microai-check-retreats]`, conditional `[once:b7-cancel-bombard]`, and enabled `[TacticalPathfinderDiscipline]` smoke are still pending. B4/B5 remain default-off because they write vanilla battle state. W&L dispatch bridge, coordinated operation packages, and historical operation doctrine are archived; current behavior lives in the living docs above.
- [`plans/archive/`](plans/archive/) — implementation plans whose patches have shipped. See the archive [README](plans/archive/README.md) for the index.

Cross-cutting vanilla bug fixes do not live here unless they grow into a real design slice. Track those in [`../bug-fixes/`](../bug-fixes/).

## Lifecycle

1. **Brainstorm** → unstructured notes; usually captured in chat / handoff section, not committed as a spec.
2. **Spec** → committed under `specs/`. Adversarial review pass before plan-writing.
3. **Plan** → committed under `plans/`. Bite-sized tasks for subagent-driven-development.
4. **Implement** → patches land in `src/WhiskeyRealism/`, get an ordinal in `docs/patch-catalog.md`, smoke verified.
5. **Archive** → once the slice ships and is verified, both spec and plan move to `archive/`. Internal cross-refs are rewritten to archive paths.

When a slice's design materially changes after shipping (new findings, new constraint), prefer recording the delta in `docs/handoff.md` "What just shipped" and `MEMORY.md` Load-Bearing Runtime Lessons rather than mutating the archived spec.
