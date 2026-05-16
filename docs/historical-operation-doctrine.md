# Historical Operation Doctrine

Living reference for named historical operation doctrine in the strategic layer. Source of truth remains shipped code first, then `docs/patch-catalog.md`, then this note. The archived design and plan are historical context only:

- `docs/superpowers/specs/archive/2026-05-06-historical-operation-doctrine-design.md`
- `docs/superpowers/plans/archive/2026-05-06-historical-operation-doctrine.md`

## Purpose

Historical operation doctrine adds named campaign concepts to the existing CIC planning path without making the campaign deterministic. The AI still scores vanilla objectives and reacts to live campaign state, but the selected plan now carries operation identity, phase posture, package gates, tempo, and dynamic rules.

Examples of the intended layer:

- Union eastern pressure;
- CSA capital defense;
- CSA Valley disruption;
- Union coastal pressure;
- Union late pressure;
- CSA protraction defense.

The doctrine is not a separate upstream AI and not a chapter-only script table. It is integrated into `CIC.Replan`, `OperationalPlan`, `PhaseTruthLedger`, and the existing probe/package/runtime surfaces.

## Implementation

Core files:

- `src/WhiskeyRealism/Strategic/HistoricalOperationModels.cs`
- `src/WhiskeyRealism/Strategic/HistoricalOperationCatalog.cs`
- `src/WhiskeyRealism/Strategic/HistoricalOperationContextBuilder.cs`
- `src/WhiskeyRealism/Strategic/OperationDynamicRuleEvaluator.cs`
- `src/WhiskeyRealism/Strategic/OperationDecisionMemory.cs`
- integration in `CIC`, `StrategicCoordinator`, `PhaseTruthLedger`, `OperationalProbeRuntime`, `PersistenceDto`

Config:

- `Strategic / Enable Historical Operation Doctrine` defaults to `true`.

Patch catalog entry:

- unnumbered runtime row `HistoricalOperationCatalog` / `OperationDynamicRuleEvaluator`

## Planning Rules

- Catalog match is explicit or visible `NoProfile`.
- No generic fallback plan while historical operation doctrine is enabled.
- `PickCampaignObjectivePatch` blocks vanilla random objective fallback when doctrine is enabled and the CIC has no historical plan, so misses remain visible.
- Legacy generic planning is still available only when the config is disabled.
- Every operation phase must expose a vanilla `TargetObjectiveId`.
- Phase area/sector keys are advisory overlays; objective ID remains the canonical target for existing patches.
- Player-CIC factions remain unsteered by Whiskey.

## Dynamic Rules

Dynamic rules are evaluated by `PhaseTruthLedger` through `OperationDynamicRuleEvaluator`. They can:

- advance or complete an operation;
- recover, pause, abort, exploit, counterstroke, or screen/delay;
- pivot only to an explicit alternate operation ID;
- mutate active plan/phase posture and package gates so the operational probe/package layer sees the decision.

Hard examples:

- objective unavailable can abort through the catalog rule;
- major friendly victory near target can switch the phase into exploit posture;
- major friendly defeat near target can switch to recovery posture;
- empty targets can force screen/probe behavior instead of massing blindly.

## Persistence

The sidecar carries:

- operation ID/name;
- operation tempo/posture;
- operation started/last-decision day serials;
- pending retarget metadata;
- phase ID/name;
- target area/sector keys;
- phase posture and package flags;
- phase start serial;
- recent operation replan memory.

## Runtime Evidence

Historical-operation implementation build/deploy/hash was verified in DLL `c90a5873e23ad1e7c0ac34e9c9b5cbad5554c0a5a2ee3fcc2aef299394366e0b` (481280 bytes). Current deployed `main` DLL:

- `dist/WhiskeyRealism.dll`: `cfdb9018bc0cb7c0fcb7ba1e28acac0b1b119243856ef3a027716f8b9b930e75`
- deployed BepInEx plugin: `cfdb9018bc0cb7c0fcb7ba1e28acac0b1b119243856ef3a027716f8b9b930e75`
- size: 1245184 bytes
- console harness `1075 PASS / 0 FAIL`
- `./build.sh` passed with 0 warnings / 0 errors

Fresh runtime smoke is still pending on the current deployed DLL. Required markers:

- `[HistoricalOperation] action=select operation=...`
- or visible `[HistoricalOperation] action=no-profile ...`
- or `[Patch:PickCampObj] ... skip-vanilla-random ...`

Do not claim in-game historical-operation behavior until a fresh post-restart log shows one of those markers.
