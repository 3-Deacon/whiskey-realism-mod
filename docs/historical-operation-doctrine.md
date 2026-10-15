# Historical Operation Doctrine

Living reference for named historical operation doctrine in the strategic layer. Source of truth remains shipped code first, then `docs/patch-catalog.md`, then this note. Build, deploy and smoke status live in [`docs/handoff.md`](handoff.md). The archived design and plan are historical context only:

- `docs/superpowers/specs/archive/2026-05-06-historical-operation-doctrine-design.md`
- `docs/superpowers/plans/archive/2026-05-06-historical-operation-doctrine.md`

## Purpose

Historical operation doctrine adds named campaign concepts to the existing CIC planning path without making the campaign deterministic. The AI still scores vanilla objectives and reacts to live campaign state, but a selected plan carries operation identity, phase posture, package gates, tempo and dynamic rules.

The catalog (`HistoricalOperationCatalog.BuildProfiles`) holds six profiles:

- `union-east-pressure` (Union Eastern Pressure)
- `csa-valley-disruption` (CSA Valley Disruption)
- `union-coastal-pressure` (Union Coastal Pressure)
- `union-western-pressure` (Union Western Pressure)
- `csa-western-depth` (CSA Western Depth)
- `union-late-pressure` (Union Late Pressure)

The former `csa-capital-defense` and `csa-protraction-defense` operations are retired: every CSA objective with ground is an offensive goal, so steering them only pulled forces north. A saved plan of a retired operation is replanned.

The doctrine is not a separate upstream AI and not a chapter-only script table. It is integrated into `CIC.Replan`, `OperationalPlan`, `PhaseTruthLedger`, `CicReviewRouter` and the probe/package runtime surfaces.

## Implementation

Core files:

- `src/WhiskeyRealism/Strategic/HistoricalOperationModels.cs`
- `src/WhiskeyRealism/Strategic/HistoricalOperationCatalog.cs`
- `src/WhiskeyRealism/Strategic/HistoricalOperationContextBuilder.cs`
- `src/WhiskeyRealism/Strategic/OperationDynamicRuleEvaluator.cs`
- `src/WhiskeyRealism/Strategic/OperationDecisionMemory.cs`
- integration in `CIC`, `CicReviewRouter`, `StrategicCoordinator`, `PhaseTruthLedger`, `OperationalProbeRuntime`, `PersistenceDto.cs`, and `Patches/PickCampaignObjectivePatch`

Config: `[Strategic] Enable Historical Operation Doctrine` defaults to `true`.

Patch catalog entry: unnumbered runtime row `HistoricalOperationCatalog` / `OperationDynamicRuleEvaluator`.

## Planning Rules

- `HistoricalOperationCatalog.Resolve` returns an explicit match or a visible `NoProfile`. A profile whose phase targets are not in vanilla's available set (`CampaignObjective.GetAvailableObjectives`) is rejected, and the CIC rejects a plan whose phase targets have no resolvable position.
- With no profile match, the CIC falls back to the legacy generic plan over objectives that vanilla can pick, that own at least one town/IIP, and whose position resolves. With none, `ActivePlan` stays null. The same generic plan is used when the doctrine is disabled.
- Every operation phase exposes a vanilla `TargetObjectiveId`. Phase area/sector keys are advisory overlays; the objective id remains the canonical target for existing patches.
- Player-CIC factions are not steered.

## Objective Steering

`PickCampaignObjectivePatch` (Prefix on `AICampaign.PickCampaignObjective`) steers vanilla's `followedcampaignobjective` only while the CIC's live phase posture is offensive (`CampaignObjectiveSteering.PhaseSteers`, evaluated after `CicReviewRouter` overrides). Defensive postures (`ReinforceAndHold`, `ScreenAndDelay`, `Recover`) leave the pick to vanilla. With no plan, an invalid phase target or a defensive phase, vanilla's random pick runs; if the current `followedcampaignobjective` is one the mod wrote, it is cleared first so vanilla actually re-picks.

## Dynamic Rules

Dynamic rules are evaluated by `PhaseTruthLedger` through `OperationDynamicRuleEvaluator`. They can:

- advance or complete an operation;
- recover, pause, abort, exploit, counterstroke, or screen/delay;
- pivot only to an explicit alternate operation id the profile allows (otherwise abort);
- change active plan/phase posture and package gates so the operational probe/package layer sees the decision.

Catalog rule examples:

- objective unavailable aborts the operation;
- objective accomplished advances the phase;
- major friendly victory near the target switches to exploit;
- major friendly defeat near the target switches to recovery;
- an empty target switches to screen/delay instead of massing blindly.

## Persistence

The sidecar carries, per plan: operation id/name, tempo and posture, operation started/last-decision day serials, pending retarget flag and reason, and per phase: id/name, target area/objective/area-key/sector-key, posture and package flags, start day serial. Per faction it also carries recent operation replan day serials (`DirectorMemoryDto`) and `modWrittenObjective`, the objective `PickCampaignObjectivePatch` last wrote, so a stale mod-written objective is still recognized after a reload.

## Telemetry

Decisions are `HistoricalOperation` telemetry events (Campaign layer) with actions including `select`, `no-profile`, `no-profile-candidate`, `pivot`, `requested-pivot`, and `vanilla-pick` (reason `no-plan`, `invalid-phase` or `defensive-phase`, from `PickCampaignObjectivePatch`). Do not claim in-game historical-operation behavior without one of these in a fresh post-restart log.
