# Tactical Brain Evidence And Backlog

Living reference for the pre-orchestrator Slice B tactical-brain design, vanilla verification, and deferred weapons/ammunition doctrine. This file is the current index for durable tactical-brain lessons; the archived specs are point-in-time design artifacts. Build, deploy and harness state live in [`docs/handoff.md`](handoff.md); the numbered patch map is [`docs/patch-catalog.md`](patch-catalog.md).

## Current Status

The original Slice B tactical brain is no longer the active decision-authority model. Its observer, odds, macro, sector, commander-intent, local-reaction, artillery, withdrawal, and bug-remediation slices shipped as bounded evidence/scorer layers and tactical patch surfaces; the pure scorers and ledgers remain under `src/WhiskeyRealism/Tactical/` (`TacticalOddsDoctrine`, `TacticalGroupSectorEstimator`, `TacticalSectorLedger`, `TacticalCommanderIntent`, `TacticalLocalReactionScorer`, `TacticalReservePolicyLedger`, `TacticalArtilleryDoctrine`, `TacticalWithdrawalDoctrine`). Current command authority is the tactical orchestrator and operations-ledger system documented in:

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
- `AIBattle.MicroAICheckForCharges` (4905) is dead in vanilla: it compares `unit.groupaiobject` (a `GameObject`) with `aigroup` (a `Regiment`) at 4910, so the charge start and cancel branches never run (`BUG-TAC-016` in [`docs/bug-fixes/vanilla-tactical-battlefield.md`](bug-fixes/vanilla-tactical-battlefield.md)). The #41 charge-gate Prefix that mirrored it was deleted; charge denial is `TacticalChargeDenial` through #45, and new charge initiation belongs to the battle core spec, not a patch on this method.
- `AIBattle.CheckForFeudGroupActions` is the W&L auto-advance risk surface; it moves formation-level feud groups with delayed `SetWaypoint`. #42 `BattleFeudActionGatePatch` mirrors it and gates it.
- Vanilla exposes hierarchy, courier/order delay, visible enemy range, FOW, reserves, fallback, retreat, artillery, terrain, morale, fatigue, ammo, and reinforcement state, but it does not implement Whiskey's scout-before-commit, local-superiority, sector-mission, strongpoint-avoidance, reserve-relief, staged-withdrawal, or command-tier doctrine.

These boundaries are already consumed by the orchestrator and operations ledger. Do not reopen broad Slice B behavior writes from the umbrella spec without a new reviewed plan.

## Shipped Slice B Evidence

The archived Slice B plan family records the shipped implementation sequence:

- B0 observer and B2 command/order telemetry (#35 `TacticalObserverPatch`) proved the runtime command, courier, objective, and decision-matrix surfaces.
- B1 added W&L charge and feud guards; only the feud gate (#42) survives. #41 `BattleChargeGatePatch` was deleted (e7214b6, dead vanilla compare).
- B3-B5 added odds, macro, and group-sector scorers. They are now evidence inputs or fallbacks: #44 `BattleMacroStrategyPatch` is the single `AIBattle.macroai` owner and #45 `BattleGroupStancePatch` writes stance through `TacticalStanceWriter`, both fed by the orchestrator and operations ledger.
- B6a/B6b/B6c added commander intent, local-reaction scoring, reserve policy, and runtime context; #47 `BattleCommanderIntentObserverPatch` and #57 `BattleReserveDoctrinePatch` are telemetry only.
- B7/B8 added artillery (#48 `B7CheckAIBombardmentPatch`) and withdrawal/reserve (#52 `B8CheckUseOfReservesPatch`, observers #49/#50) doctrine consumers.
- Tactical bug remediation added narrow guards: fallback/retreat null containment (#43), pathfinder discipline (#53), W&L operation null guard (#54) and HQ link guard (#55). The objective-chain W&L guard (#46) was deleted (06cc0b6): the behaviour it targeted is intended vanilla (`BUG-TAC-005`).

Current runtime status for these surfaces lives in [`docs/patch-catalog.md`](patch-catalog.md), [`docs/bug-fixes/vanilla-tactical-battlefield.md`](bug-fixes/vanilla-tactical-battlefield.md), and the tactical orchestrator/ledger docs; shipped state and smoke status are in [`docs/handoff.md`](handoff.md).

## Weapons And Ammunition Backlog

The weapons/ammunition adjunct remains deferred: no weapon-class, projectile or ammo-pool observer exists in `src/`. Only incidental reads ship: army ammo fraction feeds `ArmyEvidenceBuilder` effective strength (sector readiness), and #61 reads `Regiment.GetFireRange` for fire control. Do not implement from the archived spec directly. If reopened, start with an observer-only slice and a new implementation plan.

Durable design constraints:

- Do not replace `Regiment.FireBullet`, projectile simulation, or vanilla casualty handling wholesale.
- Observe live weapon ids/classes, fire cadence, range bands, smoke/contact context, projectile outcomes, small-arms ammo, artillery solid/shell/canister pools, and autoresolve parity before changing behavior.
- Keep logs bucketed and bounded (OnceLog / time-bucketed / the telemetry sink cap) behind a rollback flag; every flag defaults on, so default-on logging must be provably bounded. No one-line-per-shot firehose.
- Treat low ammo, empty ammo, smoke, fatigue, morale, and weapon family as pressure inputs for doctrine, not as deterministic scripts.
- Keep autoresolve parity in scope if live-battle weapon doctrine changes.
- Use vanilla surfaces such as `Regiment.GetFireRange`, `Regiment.FireBullet`, `Regiment.UpdateFiringAndReloading`, `Regiment.SetAmmoVariable`, `AIBattle.CheckAIBombardment`, `AIBattle.CheckCounterBatteryFire`, `AIBattle.CheckLineFallbacks`, and `BattleUnits.SetWaypoint` only after a focused anchor recheck.

The next valid step is a new observer plan, not a behavior patch. Any new writer passes `TacticalWriteGuard` and follows [`docs/patch-catalog.md`](patch-catalog.md) conventions.

## Source Artifacts

- Slice B umbrella: [`docs/superpowers/specs/archive/2026-05-05-tactical-brain-design.md`](superpowers/specs/archive/2026-05-05-tactical-brain-design.md)
- Vanilla verification: [`docs/superpowers/specs/archive/2026-05-05-tactical-brain-vanilla-verification.md`](superpowers/specs/archive/2026-05-05-tactical-brain-vanilla-verification.md)
- Weapons/ammunition adjunct: [`docs/superpowers/specs/archive/2026-05-05-tactical-weapons-ammunition-design.md`](superpowers/specs/archive/2026-05-05-tactical-weapons-ammunition-design.md)
