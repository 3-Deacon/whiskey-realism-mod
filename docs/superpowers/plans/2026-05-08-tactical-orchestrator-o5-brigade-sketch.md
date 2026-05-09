# Tactical Orchestrator O5 — Brigade Echelon (Sketch)

> **Status:** sketch only. Promoted to a full implementation plan when O4 ships smoke-verified.
>
> **This is the phase that fixes the user-observed symptom:** 8th Brigade engaging at local 1:5 odds and 1st Arkansas advancing without support. Once Brigade decisions feed `BattleGroupStancePatch` and `BattleChargeGatePatch`, brigades trapped in catastrophic local odds get authoritative withdrawal/refuse intent from the cascade rather than getting stuck on the flat doctrine scorer's `Fix` mission.

**Goal:** Add the Brigade echelon — the leaves of the orchestrator hierarchy. Each Brigade receives `DivisionIntent`, evaluates local conditions, and emits a `BrigadeDecision` (line / screen / probe / hold / fallback / charge). Patches #45 (group stance), #41 (charge gate), B8a (line fallbacks), B8b (micro retreats) all rewire to read `BrigadeOrchestrator.GroupStance` / `ChargeDecision` / `FallbackDecision` / `RetreatDecision`.

**Phase from umbrella spec:** O5 — Brigade echelon. Reference: `docs/superpowers/specs/2026-05-08-tactical-battle-orchestrator-design.md` O5 row in §"Phasing", §"Architecture" hierarchy, §"Vanilla integration map".

## Inputs (depend on O1–O4)

- `DivisionOrchestrator.EmitDivisionIntent()` produces `DivisionIntent` carrying group-role-by-brigade + axis + support priority.
- `TacticalDoctrineScorer`, `TacticalChargeViability`, `TacticalLocalReactionScorer` — existing scorers, demoted to evidence inputs (`BrigadeOrchestrator.EvaluateCharge` etc).
- `TacticalCommanderRoster` covers brigade-tier commanders (rank-tier biased by O0).

## Files

### New

```
src/WhiskeyRealism/Tactical/Orchestrator/
├── BrigadeOrchestrator.cs              (concrete echelon under Division — the leaf)
├── BrigadeDecision.cs                  (output struct read by 4 patches)
├── BrigadeStanceDecider.cs             (line/screen/probe/hold based on division intent + local odds)
├── BrigadeChargeDecider.cs             (charge viability + division authorization)
├── BrigadeFallbackDecider.cs           (when local odds + position justify falling back)
└── BrigadeRetreatDecider.cs            (when fallback fails and a real retreat is needed)
```

### Modified

```
src/WhiskeyRealism/Tactical/Orchestrator/DivisionOrchestrator.cs                       — emits to attached brigades
src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs           — discover brigades, attach BrigadeOrchestrator instances
src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs (#45)                             — read BrigadeOrchestrator.GroupStance instead of TacticalDoctrineScorer
src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs (#41)                              — read BrigadeOrchestrator.ChargeDecision instead of TacticalReactionContext.DenyCharge
src/WhiskeyRealism/Patches/B8CheckLineFallbacksObserverPatch.cs (B8a)                  — read BrigadeOrchestrator.FallbackDecision
src/WhiskeyRealism/Patches/B8MicroAICheckForRetreatsObserverPatch.cs (B8b)             — read BrigadeOrchestrator.RetreatDecision
src/WhiskeyRealism/Plugin.cs                                                            — Enable Tactical Orchestrator Brigade flag
```

### Untouched

- B7 (artillery) — owned by Division (O4).
- B8c (morale snapshot writer) — pure observer, orchestrator reads same snapshot.

## Architecture (high-level)

```
DivisionOrchestrator
└── BrigadeOrchestrator [×N per division]   (1 per vanilla brigade — the leaves)
```

Per-tick:
1. Division emits `DivisionIntent` to each attached Brigade with `GroupRoleByBrigade[brigadeId]`.
2. Brigade reads its own local evidence — closest enemy, own flanksthreated, covervalue, fortinrange, receivedfire, group strength, group queue depth.
3. **Stance decider** (`BrigadeStanceDecider.Decide(divisionIntent, localOdds, sector, personality)`):
   - If `GroupRole == Refuse` → stance 2 (refuse line)
   - If `GroupRole == MainEffort` AND `localOdds >= 0.9` → stance 1 (engage)
   - If `GroupRole == Fix` AND `localOdds >= 0.6` → stance 1 (engage/pin)
   - If `GroupRole == Fix` AND `localOdds < 0.6` → stance 2 (hold) — **THIS BREAKS THE 8TH BRIGADE BUG**
   - If `GroupRole == Screen` AND `localOdds < 0.4` → fallback intent → stance 3
   - If `localOdds < 0.3` AND `GroupRole != PreserveAtAllCosts` → withdrawal intent → stance 3

Local odds = `groupowninrange / Math.Max(1f, groupenemiesinrange)`. Same fields the existing scorer uses. The orchestrator's improvement is layering this decision on top of `DivisionIntent.GroupRole` rather than running flat from sector-mission.

4. **Charge decider** (`BrigadeChargeDecider`): vanilla stance-4 (charge) preserved unless `DivisionIntent` says no AND local conditions don't justify (uses existing `TacticalChargeViability` as evidence).
5. **Fallback / retreat deciders**: shift left of vanilla's thresholds when division authorizes withdrawal.
6. Brigade emits `BrigadeDecision`:

```csharp
struct BrigadeDecision {
    int             GroupStance;          // 0..3 (4 reserved for vanilla charge)
    bool            AllowCharge;
    FallbackKind    Fallback;             // None / Local / Withdraw
    bool            RetreatAuthorized;
    int             TargetSectorOverride; // -1 if none
}
```

7. The four patches read `BrigadeDecision` from `BrigadeOrchestrator` for the relevant `Regiment` and apply their existing W&L gates / order-friction gates to it.

## How this fixes the user's reported behavior

- 8th Brigade in your battle had `own=1793 enemy=8535` (`localOdds=0.21`). With Division-assigned `GroupRole=Fix`, current scorer maps Fix → stance=1 (engage). O5's `BrigadeStanceDecider` checks local odds first: at `localOdds < 0.6` with `GroupRole=Fix`, it returns stance=2 (hold). At `localOdds < 0.3`, it requests fallback intent up the chain. Either way, no engage stance.
- 1st Arkansas (regiment within 8th Brigade) has no orchestrator; it follows brigade stance via vanilla regiment AI. With brigade in stance=2 (hold) instead of stance=1 (engage), regiments stop advancing toward the enemy formation.

## Smoke gate

- `[TacticalCascade] division→brigade` lines fire for every brigade.
- Group stance writes flow from BrigadeOrchestrator (`[TacticalBrigadeStance] unit=…  stance=… role=… localOdds=…`).
- Charge gates and fallbacks bounded — no charge spam, no fallback thrash.
- Re-run the battle that produced the 1st Arkansas symptom: 8th Brigade now visibly stops engaging at catastrophic local odds. Verify in log: `[TacticalBrigadeStance] unit=8th_Brigade stance=2 role=fix localOdds=0.21 reason=local-odds-floor`.

## Telemetry

```
[TacticalCascade] side=csa div=anv-1st-1 brigade=8th-bde role=fix localOdds=0.21 stance=2 reason=local-odds-floor
[TacticalBrigadeDecision] side=csa unit=8th_Brigade stance=2 charge=deny fallback=Local reason=role-fix-low-odds
[TacticalBrigadeRetreat] side=csa unit=8th_Brigade authorized=true reason=division-withdrawal-intent
```

## Risks

- **Order friction violations.** Existing `TacticalOrderSettlementGate` from B5 settlement work gates every brigade write — confirm it's invoked before each `bunits.ChangeStance` in the rewired patches.
- **Charge demotion regression.** B6c-era charge denial contract must keep working — `BrigadeChargeDecider` outputs feed both #41 and #45 demote-to-3 logic. Test that the existing `[TacticalChargeDeny]` telemetry still fires under orchestrator authority.
- **W&L player-control.** Player-controlled brigades must NEVER receive orchestrator stance writes. `TacticalGateHelpers.IsPlayerControlled(group)` is checked first in every rewired patch — same guard pattern as today, just reading from a different source.
- **Brigade discovery race** — analogous to division (O4); accept first-tick empty brigade map and discover lazily.

## Estimated scope

- ~500 LOC new deciders (4 deciders, each ~75 LOC + interfaces)
- ~400 LOC tests (per-decider scenarios + cascade integration tests)
- Largest of O3-O5; brigade is where the 4 patches converge.

## Promote to plan when

O4 ships smoke-verified AND `[TacticalCascade] corps→division` lines confirm division-level cascade. Then this sketch becomes `2026-MM-DD-tactical-orchestrator-o5-brigade.md`.

After O5 ships, the original 8th Brigade / 1st Arkansas symptom is structurally addressed.
