# Coordinated Operation Packages

Living reference for campaign-map coordinated attack/reinforcement packages. Source of truth remains shipped code first, then `docs/patch-catalog.md`, then this note. The archived design and plan are historical context only:

- `docs/superpowers/specs/archive/2026-05-06-coordinated-operation-packages-design.md`
- `docs/superpowers/plans/archive/2026-05-06-coordinated-operation-packages.md`

## Purpose

Vanilla offensive logic can gather nearby forces, but Whiskey's strategic layer needed an explicit package layer so nearby formations can attack together, reinforce a lead, or remain single-lead when doctrine says not to mass. The package layer sits between strategic intent and vanilla movement/current-order execution.

It does not create a separate campaign movement system. It builds a package decision, then commits through existing safe surfaces:

- `CoordinatedOperationRuntime` for package commits;
- `WlStrategicOrderBridge` for eligible W&L player-chain current orders;
- vanilla `AICampaign.MoveUnitTo(...)` only where direct movement is allowed;
- #38 to stop vanilla's selected offensive call from silently broadening after Whiskey has committed the package.

## Implementation

Core files:

- `src/WhiskeyRealism/Strategic/CoordinatedOperationPackageLedger.cs`
- `src/WhiskeyRealism/Strategic/CoordinatedOperationRuntime.cs`
- `src/WhiskeyRealism/Patches/CoordinatedOffensiveOperationsPatch.cs`
- `src/WhiskeyRealism/Patches/CoordinatedOffensiveMicroMovementPatch.cs`
- package integration in `OperationalProbeLedger` and `OperationalProbeRuntime`

Patch catalog entry:

- #38 `CoordinatedOffensiveOperationsPatch` + `CoordinatedOffensiveMicroMovementPatch`

## Invariants

- No hidden fallback. Selected package failures log `package-no-commit`; they do not silently become vanilla random broad movement.
- Package commits preflight selected units before side effects.
- Mixed W&L current-order/direct-move packages are rejected unless the whole package can commit atomically under the active rules.
- Direct-move failures roll back package locks, defensive-moving-order state, and offensive-list state added by the package path.
- W&L current-order units are not added to `unitsinoffensiveoperations`.
- #38 restores `ownunits` snapshots in Postfix/Finalizer. If failure occurs before a snapshot exists, vanilla candidate lists are left unchanged.
- Package-locked units can be temporarily filtered from vanilla offensive micro-movement so continuation logic does not retarget them before their initial package path is consumed.
- Empty-target probes remain single-lead unless explicit operation posture permits massing.

## Runtime Evidence

The first implementation was build/deploy/hash verified as DLL `348d6aed04adeba2848cd24db32221ebcfcd34bde78d717c189e89caf0f60444` (456192 bytes). Current deployed `main` DLL is `25f3e4168d6303c9d75377def4f6eb7dd730486469fae4f3e497fb593f2de474` (886272 bytes; 756 PASS), which includes later historical-operation hardening plus tactical orchestrator, deployment, terrain/facing, and operations-ledger work.

Runtime `[CoordinatedOps]` smoke is still pending a fresh game launch and an actual AI offensive opportunity. Do not claim in-game coordinated package fire until the log shows it.
