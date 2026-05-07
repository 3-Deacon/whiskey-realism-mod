# Tactical B3-B5 Odds, Macro, And Sector Doctrine Design

Status: active design for the next Slice B implementation batch.
Scope: B3 tactical odds doctrine, B4 macro stance scorer, and B5 group sector stance. This spec intentionally batches the three coupled pieces so implementation can move without another stop after B3 telemetry, while preserving separate internal gates and rollback points.

## Decision

Implement B3, B4, and B5 as one coordinated tactical doctrine track:

- B3 creates read-only contact, sector, and odds doctrine outputs.
- B4 consumes B3 to bias battle-level `macroai` after vanilla dynamic macro logic runs.
- B5 consumes B3/B4 to bias group-level `ai_stance` by sector mission.

Do not include reserve relief, artillery doctrine, fallback paths, staged withdrawal, or direct tactical movement in this batch. Those remain B6-B8.

## Vanilla Anchors

Current decompile: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

- `AIBattle.CheckGlobalAIStrategy()` line 6314 owns battle-level `macroai`.
  - `macroai` values: `-1 dynamic`, `0 assault`, `1 attack`, `2 defend`, `3 retreat`.
  - It returns early for `bunits.endbattle >= 0`, existing macro retreat, `GameVars.aistrategy >= 0`, and `bunits.sideinformation[sideofai].macroai >= 0`.
  - It uses `BattleUnits.sideinformation[side].forcebalance`, `reinforcementarrivalswithin24hrs`, battle type, loss/casualty ratios, objective chain flanking factor, and commander initiative.
  - It may call `timepanel.SetRetreatTimer(...)` / `bunits.SetEndOfBattle(...)`; B4 must not interfere with those retreat-timer side effects.
- `AIBattle.AdjustGroupAIStance()` line 4221 owns group-level `ai_stance`.
  - It returns when `macroai < 0` or `state < 5`.
  - It loops `unitsused`, calls `PerformAIActionDLCWL(unitsused[i])`, computes `GetGroupStrength(...)`, then writes `ai_stanceordered`, `ai_stance`, and `lastaistancechangetime` through `bunits.ChangeStance(...)`.
  - Vanilla stance choices are strength-centered and macro-driven, not sector-aware.
- `AIBattle.GetGroupStrength(...)` line 6025 returns group own/enemy ratio with optional flank, morale, experience, and enemy-surrounding modifiers.
- `Regiment.UpdateUnitRangeFast(...)` line 122545 populates visible enemy lists, enemy strength in defensive angle slices, own strength in range, retreat angle, flank data, and path-destination contact signals.
- `BattleUnits.sideinformation` includes global battle fields such as `forcebalance`, `totalactiveforce`, `totalpoints`, `totalstrengthinf/cav/ari`, `routedratio`, `casualtyratio`, `strengthtoarrive`, `corpstoarrive`, and `reinforcementarrivalswithin24hrs`.
- `FogOfWar` line 100570 exposes hidden-position and intelligence methods. B3 can read FOW/contact state but must not treat unseen enemy strength as confirmed.

## Doctrine Model

B3 produces three pure, deterministic ledgers from runtime DTOs:

- `TacticalContactLedger`
  - Classifies contact as `None`, `Inferred`, `Recent`, or `Confirmed`.
  - Ages recent contact down instead of making one volley or one sighting permanent.
  - Carries confidence as a bounded 0-1 value.
- `TacticalSectorLedger`
  - Builds sectors from objective-chain groups when available.
  - Falls back to vanilla angle-slice evidence from `unitrange.enemystrengthwithinangle` when objective-chain data is absent or weak.
  - Emits sector mission candidates: `Hold`, `Fix`, `Probe`, `Refuse`, `AttackWeakPoint`, `EconomyOfForce`, and `Preserve`.
- `TacticalOddsDoctrine`
  - Computes `currentGlobalOdds`, `projectedGlobalOdds`, `localSectorOdds`, `decisivePoint`, `economyOfForceSectors`, `inferiorForcePosture`, and confidence buckets.
  - Treats global superiority as permission to seek a decisive sector, not permission for all-sector assault.
  - Treats badly inferior odds with no relief as preservation/retreat pressure, but badly inferior odds with strong terrain or near relief as delay/hold pressure.

B4 converts the B3 output into battle-level macro pressure:

- no reliable contact: prefer `dynamic` or `defend`, not immediate `attack` or `assault`;
- confirmed local advantage with adequate global support: prefer `attack`;
- confirmed weak point with high confidence and safe flanks: permit `assault`;
- inferior force with no relief: prefer `retreat` only after confidence and hysteresis gates pass;
- inferior force with terrain/relief: prefer `defend`.

B5 converts the B3/B4 output into group stance pressure:

- decisive sector: attack pressure, with charge stance excluded from this batch;
- adjacent sector: fix/support pressure;
- strongpoint or low-confidence sector: hold/probe pressure;
- flank-risk sector: refuse/hold pressure;
- economy-of-force sector: hold/screen pressure.

## Patch Boundaries

B3 has no behavior patch. It extends read-only tactical telemetry with `[TacticalOdds]` and richer `[TacticalSector]` signatures.

B4 adds `BattleMacroStrategyPatch` as a Postfix on `AIBattle.CheckGlobalAIStrategy()`:

- default-off config: `Enable Tactical Macro Stance Scorer`;
- skip if `GameVars.aistrategy >= 0`;
- skip if `bunits.sideinformation[sideofai].macroai >= 0`;
- skip if vanilla has entered macro retreat or the battle-end/retreat timer path;
- only write `macroai` when B3 confidence passes threshold and the new macro differs materially;
- log `[TacticalMacroDecision]` only on signature change.

B5 adds `BattleGroupStancePatch` as a Postfix on `AIBattle.AdjustGroupAIStance()`:

- default-off config: `Enable Tactical Group Sector Stance`;
- require B3 sector confidence and B4 macro context;
- preserve W&L ownership by reusing the same player-chain/control gate used by B1/B2 helpers;
- do not write charge stance (`ai_stance == 4`) in this batch;
- do not call `SetWaypoint`, reserve methods, artillery methods, fallback methods, or retreat methods;
- log `[TacticalGroupDecision]` only on signature change.

## Data Flow

Runtime extraction remains inside patch/runtime helpers because Unity types and reflection are not testable in the console harness. Pure classes take DTOs:

1. `TacticalObserverPatch` extracts battle side, macro, sideinformation, objective-chain summary, group/order state, visible contact, angle-slice strength, and reinforcement fields.
2. DTOs feed `TacticalContactLedger`, `TacticalSectorLedger`, and `TacticalOddsDoctrine`.
3. `TacticalDoctrineScorer` translates odds and sector missions into macro and group stance decisions.
4. B4/B5 patches apply only the final bounded decision when config, confidence, ownership, and vanilla-safety gates pass.

## Config

Add two new default-off configs:

- `Enable Tactical Macro Stance Scorer = false`
- `Enable Tactical Group Sector Stance = false`

Existing config files override C# defaults after first plugin load, so smoke instructions must include manual config flips.

## Telemetry

Expected new lines:

- `[TacticalOdds] side=... current=... projected=... contact=... decisive=... inferior=... confidence=...`
- `[TacticalSector] side=... source=... sector=... own=... enemy=... odds=... mission=... confidence=...`
- `[TacticalMacroDecision] old=... vanilla=... whiskey=... reason=... confidence=...`
- `[TacticalGroupDecision] group=... sector=... vanilla=... whiskey=... mission=... reason=...`

All logs must be signature-gated by the existing tactical observer throttle.

## Tests

Pure tests must cover:

- no contact chooses probe/hold pressure, not assault pressure;
- stale contact ages down;
- one strongpoint sighting does not remain permanent confirmed contact;
- global superiority does not create all-sector attack;
- local superiority selects one decisive sector;
- `4,000` versus `12,000` with no relief chooses preservation/retreat pressure;
- `4,000` versus `12,000` with strong terrain and near relief chooses delay/hold pressure;
- `macroai = -1` is dynamic, not attack;
- debug/UI macro override returns skip;
- save-state macro restore returns skip;
- commander aggression shifts thresholds but cannot force impossible assault;
- decisive sector gets attack pressure;
- adjacent sector gets fix/support pressure;
- strongpoint and flank-risk sectors avoid attack/charge pressure;
- low sector confidence leaves vanilla stance unchanged;
- W&L player-subordinate ownership denies group stance override.

## Smoke Gates

Build/deploy/hash verification is required before runtime smoke.

Minimum focused smoke:

1. Enable tactical observer, tactical macro scorer, and tactical group sector stance in `BepInEx/config/dev.kyle.whiskey-realism.cfg`.
2. Start or continue a W&L land battle.
3. Confirm `[TacticalOdds]`, `[TacticalSector]`, `[TacticalMacroDecision]`, and `[TacticalGroupDecision]` appear without repeated exceptions or Harmony failures.
4. Confirm no no-contact instant all-army assault.
5. Confirm no repeated macro flip-flop or all-sector attack from global superiority.
6. Confirm B1 player-subordinate guard still blocks protected W&L control when that path exercises.

## Rollback

If B4 causes macro flip-flop, disable `Enable Tactical Macro Stance Scorer`.

If B5 causes broad all-sector attack or player-subordinate retasking, disable `Enable Tactical Group Sector Stance`.

B3 telemetry can remain enabled independently because it is read-only.

## Not Included

- reserve relief;
- artillery target/bombardment doctrine;
- fallback path generation;
- staged withdrawal execution;
- movement order rewriting;
- battle-resume persistence;
- tactical state sidecar writes.
