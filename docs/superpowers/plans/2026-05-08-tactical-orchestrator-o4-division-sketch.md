# Tactical Orchestrator O4 — Division Echelon (Sketch)

> **Status:** sketch only. Promoted to a full implementation plan when O3 ships smoke-verified.

**Goal:** Add the Division echelon between Corps and (eventual) Brigade. Each Division receives `CorpsIntent`, decides group stance allocation per sector, manages reserve commit decisions, and prioritizes artillery support. Reserve / artillery patches (#48, B7, B8d) rewire to read division output instead of running their current scorer aggregators.

**Phase from umbrella spec:** O4 — Division echelon. Reference: `docs/superpowers/specs/2026-05-08-tactical-battle-orchestrator-design.md` O4 row in §"Phasing", §"Architecture" hierarchy, §"Vanilla integration map".

## Inputs (depend on O1, O2, O3)

- `CorpsOrchestrator.EmitCorpsIntent()` produces `CorpsIntent` carrying sector role + axis of advance.
- `TacticalReservePolicyLedger`, `TacticalArtilleryDoctrine`, `TacticalSupportScreen` — existing scorers, demoted to evidence inputs.

## Files

### New

```
src/WhiskeyRealism/Tactical/Orchestrator/
├── DivisionOrchestrator.cs             (concrete echelon under Corps)
├── DivisionIntent.cs                   (intent struct cascaded down to Brigade in O5)
├── DivisionReserveCommitter.cs         (orchestrator's reserve commit decision)
└── DivisionArtilleryPrioritizer.cs     (orchestrator's artillery prio decision)
```

### Modified

```
src/WhiskeyRealism/Tactical/Orchestrator/CorpsOrchestrator.cs                   — emits to attached divisions
src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs    — discover divisions, attach DivisionOrchestrator instances
src/WhiskeyRealism/Patches/BattleReserveDoctrinePatch.cs (#48)                  — read DivisionOrchestrator.ReserveCommit instead of TacticalReservePolicyLedger directly
src/WhiskeyRealism/Patches/B7CheckAIBombardmentPatch.cs (B7)                    — read DivisionOrchestrator.ArtilleryPrio
src/WhiskeyRealism/Patches/B8CheckUseOfReservesPatch.cs (B8d)                   — read ArmyOrchestrator.WithdrawalIntent (army-level) for help-request gating
src/WhiskeyRealism/Plugin.cs                                                     — Enable Tactical Orchestrator Division flag
```

### Untouched

- Brigade-level patches (#41/#45/B8a/B8b) — O5.
- `TacticalReservePolicyLedger`, `TacticalArtilleryDoctrine` source — unchanged; their CALLERS shift.

## Architecture (high-level)

```
CorpsOrchestrator
└── DivisionOrchestrator [×N per corps]   (1 per vanilla division)
```

Per-tick:
1. Corps emits `CorpsIntent` to each attached Division.
2. Division reads its own group-level evidence (`TacticalSectorLedger` slice for own frontage, `TacticalReservePolicyLedger` for own reserves, `TacticalSupportScreen` for screening evidence).
3. `DivisionReserveCommitter(corpsIntent, ownReserves, threats, personality)` decides:
   - **Commit** (reserves move toward main-effort sector)
   - **Hold** (reserves stay until trigger)
   - **Recall** (reserves return to consolidation point)
4. `DivisionArtilleryPrioritizer(corpsIntent, enemyVisible, ownArtilleryReady)` decides per-battery target priorities (counter-battery vs preserve-fire vs cancel-bombard).
5. Division emits `DivisionIntent` down (consumed by Brigade in O5).

```csharp
struct DivisionIntent {
    GroupRole           GroupRoleByBrigade;   // map<brigadeId, GroupRole>
    Vector2             AxisOfAdvance;
    int                 SupportPriorityBrigadeId;
    ReserveCommitDecision ReserveCommit;
    ArtilleryPriority   ArtilleryPrio;
}

enum ReserveCommitDecision { Hold, Commit, Recall }
enum ArtilleryPriority { CounterBattery, PreserveFire, ScreenMaineffort, CancelBombard }
```

## Smoke gate

- `[TacticalCascade] corps→division` lines fire for every division.
- Reserve commits trigger at orchestrator-decided times rather than vanilla — verified by comparing `[TacticalReserveCommit]` markers against vanilla `CheckUseOfReserves` decision trace (existing telemetry).
- Artillery prioritization shifts visibly when corps `axis-of-advance` changes (e.g. enemy main effort spotted in different sector → artillery counter-battery target shifts within ~30 game seconds).

## Telemetry

```
[TacticalCascade] side=union corps=ix div=division-3 role=fix axis=south reserveCommit=Hold artilleryPrio=CounterBattery
[TacticalDivisionIntent] side=csa div=anv-1st-1 brigades=4 supportPrio=2 commit=Commit ageSeconds=42
```

## Risks

- **Reserve commit timing wrong.** Vanilla `CheckUseOfReserves` uses its own logic; if division commits reserves too early, vanilla still tries to commit them via the patch surface — could double-commit. Mitigation: division's commit decision is the SINGLE source of truth when valve on; `BattleReserveDoctrinePatch` Prefix-snapshot/Postfix-restore around the orchestrator write so vanilla can't double-fire.
- **Brigade discovery race.** Division can be created before its child brigades' Regiments are fully populated by vanilla. Mitigation: division discovers brigades lazily on first cascade — accepts that the first tick may have empty brigade map.
- **Artillery patch coupling.** B7 currently has its own input adapter; rewire must preserve the W&L charge-denial contract from B7 (umbrella §"Inventory" row B7).

## Estimated scope

- ~350 LOC new types + reserve/artillery deciders
- ~250 LOC tests (reserve commit + artillery prio scenarios)
- Slightly larger than O3; reserve and artillery decision logic carry the most complexity.

## Promote to plan when

O3 ships smoke-verified AND `[TacticalCascade] army→corps` lines confirm corps-level intent flow. Then this sketch becomes `2026-MM-DD-tactical-orchestrator-o4-division.md`.
