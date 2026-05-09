# Tactical Orchestrator O2 — Intent Inference + Adversarial Loop (Sketch)

> **Status:** sketch only. Promoted to a full implementation plan when O1 ships smoke-verified. Do not execute from this document — it intentionally omits step-level TDD detail.

**Goal:** Each side's `ArmyOrchestrator` builds a `TacticalIntentModel` of the opposing army's plan from visible state and feeds it into both playbook selection (`OpposingCommanderHint`) and replan trigger evaluation (`EnemyMainEffortShiftConfidenceWeighted`). Both sides' orchestrators react to each other; plans become responses to inferred opposing plans.

**Phase from umbrella spec:** O2 — Intent inference + adversarial loop. Reference: `docs/superpowers/specs/2026-05-08-tactical-battle-orchestrator-design.md` §"Adversarial intent inference + personality" and the O2 row in §"Phasing".

## Inputs (depend on O1)

- `ArmyOrchestrator` exists and exposes `EmitArmyIntent`, `CheckReplanTriggers`, `Replan` (O1 Task 7-8).
- `BuiltInPlaybooks.SeedCatalog()` registered with all 14 playbooks (O1 Task 4-6).
- `BattleMacroStrategyPatch` reading orchestrator output (O1 Task 12).
- Existing evidence ledgers (`TacticalSectorLedger`, `TacticalContactLedger`, `TacticalOddsDoctrine`, `TacticalMoraleSnapshotLedger`).

## Files

### New

```
src/WhiskeyRealism/Tactical/Orchestrator/
├── TacticalIntentModel.cs              (the inferred-enemy struct)
├── ArmyIntentInference.cs              (pure scorer: visible state → InferredIntent + confidence)
├── ArmyTickCycle.cs                    (per-tick evidence refresh + replan loop driver)
└── ArmyEvidenceBuilder.cs              (vanilla → ArmyEvidence + ReplanTriggerInput, runtime partial)
```

### Modified

```
src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs       — accept TacticalIntentModel input on Replan
src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs — wire ArmyTickCycle into Tick()
src/WhiskeyRealism/Patches/TacticalObserverPatch.cs                — call coordinator Tick() on every #35 cycle (already does post-O0; confirm under O2 valve)
src/WhiskeyRealism/Plugin.cs                                       — Enable Tactical Orchestrator Intent Inference flag
```

## Architecture (high-level)

`TacticalIntentModel` per opposing echelon (army-only at O2):

```csharp
struct TacticalIntentModel {
    InferredIntent  PrimaryIntent;        // Attack / Defend / Withdraw / Probe / Refuse
    int             InferredMainEffort;
    float           Confidence01;
    float           AgeSeconds;
    EvidenceTag[]   SupportingEvidence;
}
```

`ArmyIntentInference.Build(ArmyEvidence ownSide, EnemyVisibleState enemy)` returns the model. Enemy concentration vs. own concentration in each sector, enemy reserve commit posture, contact zones, and movement drives produce the inference. Confidence floor 0.3 (no sub-floor signals action); above 0.6 triggers replan if shift exceeds personality-modulated threshold.

Personality consumption (already in umbrella §"Personality consumption"):
- High caution → defensive replan triggers at lower confidence
- High aggression → attack triggers at lower confidence
- High audacity → reserve commit triggers at lower flank-exposure confidence

## Smoke gate

- AI-vs-AI battle log shows `[TacticalIntent]` lines on both sides with non-zero confidence.
- One or more `[TacticalReplan] trigger=enemy-intent-shift` events observed across multiple battles.
- Personality-bias visible: McClellan-archetype CO triggers defensive replan at lower confidence than Lee-archetype (run two AI-vs-AI scenarios with Lee-vs-McClellan and confirm replan rates differ).

## Telemetry

```
[TacticalIntent] side=union seesEnemy=lee_envelopment confidence=0.58 evidence=south-concentration,reserve-uncommitted
[TacticalReplan] side=csa trigger=enemy-intent-shift from=lee_envelopment to=jackson_valley_shuffle ageSeconds=83
```

## Risks

- **Intent inference produces wild plans on sparse evidence.** Mitigation: confidence floor 0.3 below which intent is treated as `Unknown`; generic-fallback playbooks always score above zero so replan can't crash.
- **Replan thrash.** Mitigation: O1 already ships `MinReplanSeconds` config (default 60); honor it in `ArmyTickCycle.MaybeReplan`.
- **Stale evidence after mid-battle save/reload.** Mitigation: `ArmyTickCycle` rebuilds from current vanilla state on first tick after reload; `TacticalIntentModel` carries `AgeSeconds` so callers know to discount old inferences.

## Estimated scope

- ~250 LOC new types + scorer
- ~150 LOC tests
- Roughly half the size of O1 — most of the heavy lifting (catalog, playbooks, plan entity) already done.

## Promote to plan when

O1 ships smoke-verified AND at least one AI-vs-AI battle log shows `[TacticalPlan]` and `[TacticalMacroDecision]` orchestrator lines. Then this sketch becomes `2026-MM-DD-tactical-orchestrator-o2-intent.md` with full task-step TDD detail.
