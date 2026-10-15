# Tactical Brain Evidence And Backlog

Living reference for the pre-orchestrator Slice B tactical-brain design, vanilla verification, and deferred weapons/ammunition doctrine. This file is the current index for durable tactical-brain lessons; the archived specs are point-in-time design artifacts.

## Current Status

The original Slice B tactical brain is no longer the active decision-authority model. Its observer, odds, macro, sector, commander-intent, local-reaction, artillery, withdrawal, and bug-remediation slices shipped as bounded evidence/scorer layers and tactical patch surfaces. Current command authority is the tactical orchestrator and operations-ledger system documented in:

- [`docs/tactical-orchestrator.md`](tactical-orchestrator.md)
- [`docs/tactical-operations-ledger.md`](tactical-operations-ledger.md)
- [`docs/tactical-terrain-facing-discipline.md`](tactical-terrain-facing-discipline.md)
- [`docs/patch-catalog.md`](patch-catalog.md)

Use shipped code and those living docs for current behavior. The Slice B specs are archived for traceability only:

- [`docs/superpowers/specs/archive/2026-05-05-tactical-brain-design.md`](superpowers/specs/archive/2026-05-05-tactical-brain-design.md)
- [`docs/superpowers/specs/archive/2026-05-05-tactical-brain-vanilla-verification.md`](superpowers/specs/archive/2026-05-05-tactical-brain-vanilla-verification.md)
- [`docs/superpowers/specs/archive/2026-05-05-tactical-weapons-ammunition-design.md`](superpowers/specs/archive/2026-05-05-tactical-weapons-ammunition-design.md)

## Preserved Vanilla Boundaries

The vanilla verification pass remains useful because it separates exposed Grand Tactician surfaces from Whiskey doctrine:

- `AIBattle.CheckGlobalAIStrategy` owns battle-level `macroai`; `macroai` and group-level `ai_stance` are separate ladders.
- `AIBattle.AdjustGroupAIStance` owns group stance; `ai_stance == 4` is the group charge stance, not a battle macro value.
- `AIBattle.MicroAICheckForCharges` initiates and cancels charge movement and does not call `PerformAIActionDLCWL`; Whiskey charge guards must preserve vanilla cancellation side effects.
- `AIBattle.CheckForFeudGroupActions` is the W&L auto-advance risk surface; it moves formation-level feud groups with delayed `SetWaypoint`.
- Vanilla exposes hierarchy, courier/order delay, visible enemy range, FOW, reserves, fallback, retreat, artillery, terrain, morale, fatigue, ammo, and reinforcement state, but it does not implement Whiskey's scout-before-commit, local-superiority, sector-mission, strongpoint-avoidance, reserve-relief, staged-withdrawal, or command-tier doctrine.

These boundaries are already consumed by the orchestrator and operations ledger. Do not reopen broad Slice B behavior writes from the umbrella spec without a new reviewed plan.

## Shipped Slice B Evidence

The archived Slice B plan family records the shipped implementation sequence:

- B0 observer and B2 command/order telemetry proved the runtime command, courier, objective, and decision-matrix surfaces.
- B1/#41/#42 added W&L charge and feud guards.
- B3-B5 added odds, macro, and group-sector scorers, later demoted to evidence inputs where the orchestrator now owns command authority.
- B6a/B6b/B6c added commander intent, local-reaction scoring, reserve policy, and runtime context.
- B7/B8 added artillery and withdrawal doctrine consumers.
- Tactical bug remediation added narrow guards such as fallback/retreat null containment, objective-chain W&L protection, and pathfinder discipline.

Current runtime status for these surfaces lives in [`docs/handoff.md`](handoff.md), [`docs/patch-catalog.md`](patch-catalog.md), [`docs/bug-fixes/vanilla-tactical-battlefield.md`](bug-fixes/vanilla-tactical-battlefield.md), and the tactical orchestrator/ledger docs.

## Weapons And Ammunition Backlog

The weapons/ammunition adjunct remains deferred. It should not be implemented from the archived spec directly. If reopened, start with an observer-only slice and a new implementation plan.

Durable design constraints:

- Do not replace `Regiment.FireBullet`, projectile simulation, or vanilla casualty handling wholesale.
- Observe live weapon ids/classes, fire cadence, range bands, smoke/contact context, projectile outcomes, small-arms ammo, artillery solid/shell/canister pools, and autoresolve parity before changing behavior.
- Keep logs bucketed and config-gated; no one-line-per-shot firehose in normal observer mode.
- Treat low ammo, empty ammo, smoke, fatigue, morale, and weapon family as pressure inputs for doctrine, not as deterministic scripts.
- Keep autoresolve parity in scope if live-battle weapon doctrine changes.
- Use vanilla surfaces such as `Regiment.GetFireRange`, `Regiment.FireBullet`, `Regiment.UpdateFiringAndReloading`, `Regiment.SetAmmoVariable`, `AIBattle.CheckAIBombardment`, `AIBattle.CheckCounterBatteryFire`, `AIBattle.CheckLineFallbacks`, and `BattleUnits.SetWaypoint` only after a focused anchor recheck.

The next valid step is a new observer plan, not a behavior patch.

## Source Artifacts

- Slice B umbrella: [`docs/superpowers/specs/archive/2026-05-05-tactical-brain-design.md`](superpowers/specs/archive/2026-05-05-tactical-brain-design.md)
- Vanilla verification: [`docs/superpowers/specs/archive/2026-05-05-tactical-brain-vanilla-verification.md`](superpowers/specs/archive/2026-05-05-tactical-brain-vanilla-verification.md)
- Weapons/ammunition adjunct: [`docs/superpowers/specs/archive/2026-05-05-tactical-weapons-ammunition-design.md`](superpowers/specs/archive/2026-05-05-tactical-weapons-ammunition-design.md)
