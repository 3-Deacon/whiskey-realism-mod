# Tactical Orchestrator O3 — Corps Echelon (Sketch)

> **Status:** sketch only. Promoted to a full implementation plan when O2 ships smoke-verified.

**Goal:** Add the Corps echelon between Army and (eventual) Division. Each Corps receives `ArmyIntent`, allocates its frontage into main-effort / fixing / screening / refuse sector roles, builds a `CorpsIntent`, and emits down. Personality biases tempo (aggression) and audacity. Corps-level intent inference reads enemy corps within own frontage.

**Phase from umbrella spec:** O3 — Corps echelon. Reference: `docs/superpowers/specs/2026-05-08-tactical-battle-orchestrator-design.md` O3 row in §"Phasing", §"Architecture" hierarchy diagram, §"Per-echelon evidence".

## Inputs (depend on O1, O2)

- `ArmyOrchestrator.EmitArmyIntent()` produces `ArmyIntent` carrying main-effort + fixing + screening sectors.
- `TacticalIntentModel` per opposing army (O2).
- Existing `TacticalRefuseFlankIntent` scorer becomes a `CorpsOrchestrator` evidence input (no longer called directly by patches once corps is wired).

## Files

### New

```
src/WhiskeyRealism/Tactical/Orchestrator/
├── CorpsOrchestrator.cs                (concrete echelon under Army)
├── CorpsIntent.cs                      (intent struct cascaded down)
├── CorpsSectorAllocator.cs             (translates ArmyIntent + own frontage → sector roles)
├── CorpsIntentInference.cs             (visible state in own frontage → inferred enemy corps task)
└── (scorer reuse: TacticalRefuseFlankIntent.cs unchanged)
```

### Modified

```
src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs                 — emits to attached corps
src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs — discover corps COs from vanilla, build CorpsOrchestrator instances
src/WhiskeyRealism/Tactical/Orchestrator/TacticalCommanderRosterRuntime.cs   — corps-tier commander discovery
src/WhiskeyRealism/Patches/BattleObjectiveChainWlGuardPatch.cs (#46)         — re-validate W&L guard under corps authority (no behavior change expected; spec says #46 stays orthogonal)
src/WhiskeyRealism/Patches/BattleFeudActionGatePatch.cs (#42)                — re-validate W&L guard under corps authority
src/WhiskeyRealism/Plugin.cs                                                  — Enable Tactical Orchestrator Corps flag
```

### Untouched

- All non-corps echelons. Brigade-level patches (#41/#45/B7/B8) wait for O5.
- Macro/sector-mission scorers — corps reads them as evidence; they don't change.

## Architecture (high-level)

```
ArmyOrchestrator
└── CorpsOrchestrator [×N per army]      (1 per vanilla corps; N typically ~3)
```

Corps discovery: vanilla `Regiment` units have a `parentcorps` or equivalent reference. The runtime partial walks `BattleUnits.unitsused`, groups by corps id, and creates one `CorpsOrchestrator` per unique id. Each is attached as a child to its parent `ArmyOrchestrator`.

Per-tick:
1. Army emits `ArmyIntent` to each attached Corps.
2. Each Corps reads its own frontage's `TacticalSectorLedger` slice + the corps-level `TacticalIntentModel` (built via `CorpsIntentInference` from visible state in own frontage only).
3. Corps allocates its sectors using `CorpsSectorAllocator(armyIntent, ownFrontageSectors, enemyCorpsIntent, personality)`:
   - main-effort sector(s) take priority based on personality aggression
   - fix-and-pin sectors get assigned to brigades that can hold (but in O3 brigade is not yet built — corps emits intent only)
   - refuse-flank sectors flagged when `TacticalRefuseFlankIntent` scorer says yes
4. Corps emits `CorpsIntent` down (consumed by Division in O4).

```csharp
struct CorpsIntent {
    SectorRole       SectorRoleByDivision;   // map<divisionId, SectorRole>
    AxisOfAdvance    AxisOfAdvance;
    float            SupportPriority;        // [0,1]
    float            AggressionBias01;
}
```

## Smoke gate

- `[TacticalCascade] army→corps` lines fire for every corps with valid sector roles (main-effort + fix + screen sets non-empty per corps's frontage).
- Sector role allocation produces non-empty main-effort/fix/screen sets across at least 2 of 3 corps in a 3-corps battle.
- W&L player-subordinate units never receive corps-issued orders that bypass `TacticalGateHelpers.IsPlayerControlled` (smoke verified by W&L gate-active log lines absent of corps-origin orders for player units).

## Telemetry

```
[TacticalCascade] side=union army=mcclellan_defense corps=ix axis=south role=fix supportPrio=0.4 agg=0.30
[TacticalCorpsIntent] side=csa corps=anv-1st planParent=lee_envelopment roleByDivision={1:main,2:fix,3:screen} ageSeconds=12
[TacticalCommanderUnknown] echelon=corps name=Jubal_Early
```

## Risks

- **Corps discovery from vanilla unreliable.** Mitigation: walk `BattleUnits.unitsused` and group by `Regiment.parentcorps` (or equivalent — re-decompile to confirm field name); on missing parent, attach to "default corps 0" for that army with telemetry warning.
- **Frontage division produces overlapping or empty corps slices.** Mitigation: corps gets all sectors where one of its child units is present; sectors with multiple corps split by majority strength.
- **W&L gates regression risk.** Mitigation: O3 doesn't change W&L gate code paths; the gate validates by passing the existing test cases unchanged plus one new test that confirms a corps-issued intent for a player-subordinate brigade is dropped at the gate.

## Estimated scope

- ~300 LOC new types + allocator + inference
- ~200 LOC tests
- Comparable size to O2; corps-frontage discovery is the trickiest part.

## Promote to plan when

O2 ships smoke-verified AND `[TacticalIntent]` lines confirm visible-state inference works at army echelon. Then this sketch becomes `2026-MM-DD-tactical-orchestrator-o3-corps.md`.
