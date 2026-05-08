# Tactical B6 Commander Intent And Local Reaction Design

Status: active design spec. This is a Slice B tactical design artifact, not an implementation plan.

Scope: B6 turns B3-B5 contact, sector, odds, macro, and group-stance outputs into a Scourge-inspired commander-intent layer translated into Grand Tactician's vanilla surfaces. It defines the full B6 doctrine program now: intent, playbooks, command friction, local reaction, charge permission, reserve and line-relief intent, and the runtime surfaces those reach. Implementation is ordered into bounded plans because these surfaces write different vanilla battle state. B6 does not make B4/B5 default-on, does not port Scourge code, and does not invent vanilla surfaces that Grand Tactician does not expose.

## Decision

B6 owns the Scourge-derived idea that stance is commander intent, not just a numeric aggression knob, and translates that idea into Grand Tactician's actual vanilla writes:

- stance-as-intent bands derived from B3-B5 evidence and the active strategic `OperationPosture`;
- a tactical playbook layer over B3 sectors, anchored to `ObjectiveChain.linegroup_left/center/rightunits`;
- command-friction extensions over B2 expressed as B6 self-constraints, not as new Prefix gates on vanilla order surfaces;
- local subordinate reaction doctrine for groups (the B5 stance surface) and per-unit charge initiation (the B1 `MicroAICheckForCharges` surface);
- delayed reserve-release and line-relief intent funneled through a per-side reserve policy aggregator;
- charge permission as an explicit surface decision: `PermitCharge` means leave vanilla `ai_stanceordered == 4` alone. The shipped B6b pure scorer does not emit `DenyCharge`; B6c must treat any non-`PermitCharge` local reaction as charge denial when `Enable Tactical Charge Denial` is enabled.

The implementation split is:

1. `B6a Commander Intent And Playbook Ledger` - pure ledgers, telemetry, no behavior writes.
2. `B6b Local Reaction Scorer` - pure subordinate reaction decisions and tests.
3. `B6c Runtime Application` - stance, charge permit/deny, reserve-intent, and local-reaction behavior behind explicit per-reaction config.
4. `B7 Artillery And Strongpoint Runtime` - bombardment/strongpoint execution using B6 intent.
5. `B8 Staged Withdrawal Runtime` - covered fallback/rear-guard execution using B6 intent.

Artillery and withdrawal stay in their own plans because they patch different vanilla methods:

- `B7 Artillery And Strongpoint Doctrine` owns bombardment (`AIBattle.CheckAIBombardment` 3869), unlimber, counterbattery, and artillery fallback.
- `B8 Staged Withdrawal Doctrine` owns covered fallback (`AIBattle.CheckLineFallbacks` 5118), rear guard, and full-retreat escalation (`AIBattle.MicroAICheckForRetreats` 4817).

## Evidence Boundary

Scourge of War is design evidence, not source code to copy.

The installed SDK license/readme says the SDK is provided under NorbSoftDev rights and that all rights are reserved. Whiskey can use the exposed SDK, manual, and data files to identify concepts and comparable tactical surfaces, but B6 must implement original Whiskey logic against Grand Tactician's vanilla methods.

Where Scourge concepts have no vanilla counterpart in Grand Tactician, B6 drops the concept rather than inventing a surface. Concrete drop: Scourge exposes per-unit volley/fire-hold reactions. Grand Tactician does not — `Regiment.combatbehavior` values cover bombardment states (`8`/`9`), forced fallback (`2`), and movement modes; `GamePrefs.waitforfirstvolleytobefinished` and `distancefactorfiringlastvolleybeforecharge` drive timing implicitly with no AI-writeable handle, and `GameVars.debug_firevolleyselectedunit` is debug-only. B6 therefore does not include a `HoldFireOrVolleyPressure` reaction.

Local Scourge references used for this spec:

- Manual stance bands and courier doctrine: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Scourge Of War - Remastered/Base/Layout/Media/Language/EnglishMainHelp.txt`
  - courier-only high-difficulty communication and AI subordinate execution around lines 350-353;
  - stance meanings around lines 455-490.
- SDK tactical playbook evidence: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Scourge Of War - Remastered/sdk/SowAiInf/xtables.inl`
  - `gDivStrat` contains named defensive, high-ground, combined-arms, and refused-flank patterns around lines 20-120.
- SDK officer/courier concept evidence: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Scourge Of War - Remastered/sdk/SowAiInf/offai.cpp`
  - division officer sends courier orders to brigades lacking orders around lines 858-888.
- SDK local reaction concept evidence:
  - `/mnt/c/Program Files (x86)/Steam/steamapps/common/Scourge Of War - Remastered/sdk/SowAiInf/offcmds.cpp` around lines 694-925;
  - `/mnt/c/Program Files (x86)/Steam/steamapps/common/Scourge Of War - Remastered/sdk/SowAiInf/unitai.cpp` around lines 929-985 and 2060-2150.

These are concept anchors only. Do not copy Scourge routines, constants, tables, or code structure into Whiskey.

## Grand Tactician Anchors

Current decompile: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

Confirmed tactical surfaces for B6 (all line numbers verified against the current decompile):

- `AIBattle.CheckGlobalAIStrategy()` line 6314 owns battle-level `macroai`. B4 already wraps this Postfix.
- `AIBattle.AdjustGroupAIStance()` line 4221 owns group-level `ai_stance`. Vanilla writes `ai_stanceordered = 4` (charge) at line 4242 when `groupStrength > strengthtriggerforassault_micro[macroai]` and `macroai ∈ {0,1}` and `oobunitsymbolid == 0`. B5 already wraps this Postfix.
- `AIBattle.MicroAICheckForCharges(Regiment, int)` line 4905 reads `aigroup.ai_stance == 4` then calls `unit.SetMovementMode(3)` at line 4919, gated by `lastaichargetime + GamePrefs.timetorenewaichargecheck` cooldown. This is the per-unit charge initiation surface. B1/#41 `BattleChargeGatePatch` already mirrors this Prefix-replacement.
- `AIBattle.GetGroupStrength(...)` line 6025 returns group own/enemy ratio with flank, morale, experience, and surrounding modifiers.
- `Regiment.UpdateUnitRangeFast(...)` line 122545 populates contact, angle-slice enemy strength, own strength, flank, retreat-angle, and path-destination evidence.
- `AIBattle.objectivechain` (`List<ObjectiveChain>`) declaration at line 3282; `ObjectiveChain.linegroup_centerunit` at 2992, `linegroup_leftunits` at 2994, `linegroup_rightunits` at 2996; `flankpositionobj[0/1]`, `anchoredflank[0/1]`, `flankstrength[0/1]` referenced at 5771-5777. Together these give the per-side L/center/R axis and anchored-flank evidence required for `RefuseLeft` / `RefuseRight` and main-effort/refused-flank assignment.
- `AIBattle.CheckUseOfReserves(...)` line 6062 is the vanilla reserve-use surface.
- `AIBattle.LinkReservesToLineGroup()` line 6642 and `AIBattle.AssignReserves()` line 7017 are reserve organization surfaces. `objectivechain[i].reservegroups` (line 2972 declaration; 3554/3562/3655/5779 mutation/read sites) is the reserve membership list.
- `AIBattle.CheckAIBombardment(...)` line 3869 is the artillery bombardment surface. B6 reads artillery support/strongpoint context only; B7 owns behavior there.
- `AIBattle.CheckLineFallbacks(...)` line 5118 and `AIBattle.MicroAICheckForRetreats(...)` line 4817 are fallback/retreat surfaces. B6 only emits local fallback pressure as intent; B8 owns staged withdrawal execution.
- `Regiment.AddOrderCourierline(...)` line 125009 and `Regiment.ProcessOrders()` line 125173 are vanilla courier/order-delay surfaces.
- `Regiment.AddToOrderQueue(...)` line 124917 is the order-queue entry. `BattleUnits.SetWaypoint(GameObject,...)` line 91225 and `BattleUnits.SetWaypoint(Regiment,...)` line 91232 are the vanilla movement entry points; the `useorderdelay` parameter on the latter is what feeds the courier/bugle delay system. B6 itself never calls either signature; the order-friction rules apply to B6's own outputs.
- `AIBattle.UpdateMovingTargets()` line 6870 is already guarded by #46 `BattleObjectiveChainWlGuardPatch` for W&L player-subordinate objective-chain exposure. B6 reads `objectivechain` only after this guard's Postfix has restored the list, or via the existing #35 observer extraction window — never inside the Prefix-removed window.

## Strategic-To-Tactical Intent Translation

B6 commander intent is derived from three sources, in order of precedence:

1. The active strategic `OperationPosture` for the side's current `OperationalPlan` (`Strategic/HistoricalOperationModels.cs` enum: `ProbeAndDevelop`, `ScreenAndDelay`, `ConcentratedAttack`, `ExploitBreakthrough`, `Counterstroke`, `ReinforceAndHold`, `Recover`, `Inherit`).
2. The vanilla in-battle commanding officer's profile (`GameVars.commander[...]`) and `GetCommanderInitiative()` (line 6327 of the decompile demonstrates vanilla's own initiative read).
3. B3/B4/B5 evidence: contact ledger, sector odds, macro decision.

The translation table is fixed:

| OperationPosture | Default intent band | Notes |
|---|---|---|
| `ConcentratedAttack` | `Attack` | upgrade to `AllOutAttack` only when B3 confirms a high-confidence weak point and commander aggression ≥ 0.6 |
| `ExploitBreakthrough` | `AllOutAttack` | downgrade to `Attack` when B3 confidence < 0.55 |
| `Counterstroke` | `Defend` | upgrade local sector to `Attack` when sector odds ≥ 1.35 with confidence ≥ 0.55 |
| `ProbeAndDevelop` | `Probe` | never upgrade beyond `Attack`; never to `AllOutAttack` |
| `ScreenAndDelay` | `Defend` | downgrade to `Hold` when severely outnumbered |
| `ReinforceAndHold` | `Hold` | upgrade to `Defend` only when contacted |
| `Recover` | `HoldToLast` | downgrade to `Hold` when no enemy in range |
| `Inherit` / no plan | derived from B4 macro alone | `macroai` 0→`Attack`, 1→`Attack`, 2→`Defend`, 3→`HoldToLast` (vanilla owns retreat) |

When no strategic plan exists (player-CIC alliance, or pre-plan startup), B6 derives intent from B4 macro plus commander initiative only. Strategic mod state remains read-only to tactical patches: B6 reads `OperationalPlan.OperationPosture` through the existing read path, never writes.

## Verified Applicability Check

| Scourge logic | Grand Tactician surface | B6 applicability |
|---|---|---|
| Stance bands express commander intent from all-out attack through hold-to-last. | `AIBattle.AdjustGroupAIStance()` (4221) writes `ai_stance` via `bunits.ChangeStance(...)` and direct `ai_stance`/`ai_stanceordered` writes. Vanilla can write `ai_stanceordered = 4` for charge. | Apply. B6 maps intent bands onto B5's existing stance pressure surface, with explicit handling for vanilla's stance-4 charge writes (see Charge Permission Surfaces). |
| `Probe` is a distinct commander intent: occupy, test, demonstrate without heavy-loss commitment. | B5 already maps the per-sector `Probe` mission and defensive weak-point pressure to vanilla stance 1 screening. | Apply strongly. B6 makes `ProbeIntent` a first-class battle-wide intent, with tests proving it does not become all-sector attack or charge permission. |
| Division play tables assign multi-spot patterns such as high-ground defense, combined-arms defense, and refused flanks. | `objectivechain[i].linegroup_centerunit`, `linegroup_leftunits`, `linegroup_rightunits`, plus `flankpositionobj[0/1]` give a per-side L/center/R axis with anchored-flank evidence. | Apply. `TacticalPlaybookLedger` consumes those fields to assign one decisive sector, refused flank, fix sectors, and hold sectors before B5/B6 stance pressure runs. |
| Courier logic lets higher commanders transmit orders to subordinates that lack orders, then subordinates execute with AI judgment. | `Regiment.AddOrderCourierline(...)` (125009), `AddToOrderQueue(...)` (124917), `ProcessOrders()` (125173), `BattleUnits.SetWaypoint(Regiment, ..., useorderdelay)` (91232). B2 already reads delivery state. | Apply as B6 self-constraint. B6 does not Prefix-gate these methods; instead B6's own scoring respects stale-order evidence and refuses to emit reactions that would require a fresh retask. Direct courier synthesis is out of scope for B6. |
| Local infantry AI handles skirmishers, line maintenance, flanking, charge permission, morale fallback, and skirmisher fallback. | GT has scattered local surfaces: stance adjustment (4221), charge check (4905), fallback/retreat (5118/4817), reserve support (6062), and path/order APIs. | Apply selectively: stance adjustment (B5 surface), charge permit/deny (B1/#41 surface), reserve-list bias (named B6c task), local fallback intent (B8 reads). |
| Morale and danger can trigger fallback/retreat from the local unit layer. | `AIBattle.CheckLineFallbacks(...)` (5118) writes fallback paths/modes; `MicroAICheckForRetreats(...)` (4817) controls retreat movement. | Deferred to B8. B6b defines the `LocalFallbackPressure` enum value but does not emit it; B8 must either extend the pure scorer or derive fallback pressure from B6/B3/B2 inputs before executing withdrawal behavior. |
| Reserve behavior can support threatened or weak points. | `CheckUseOfReserves(...)` (6062) paths support units; `LinkReservesToLineGroup()` (6642) and `AssignReserves()` (7017) mutate `objectivechain[i].reservegroups`. | Apply as `TacticalReserveIntent` and a `TacticalReservePolicyLedger` aggregator. Reserve-list mutation requires a named B6c task that quotes the exact vanilla mutation shape, snapshots state, and defines rollback. |
| Per-unit volley/fire-hold reactions. | No vanilla writeable surface (see Evidence Boundary). | **Drop.** Not implemented in B6. |

Conclusion: B6 translates Scourge intent/playbook/reaction concepts into Grand Tactician's actual vanilla writes. Concepts without a vanilla surface are dropped. The implementation order is blast-radius control, not feature reduction.

## Preconditions

B6a/B6b can start immediately as pure doctrine and ledger work. B6c runtime writes that mutate vanilla battle state must satisfy these gates or explicitly quarantine the unsafe surface in the implementation plan:

- B4/B5 focused runtime smoke on the **current deployed DLL** (`docs/handoff.md` "Current shipped version" hash) confirms bounded `[TacticalMacroDecision]` and `[TacticalGroupDecision]` lines, no repeated exceptions, no Harmony failures, no missing anchors, no macro flip-flop, no all-sector attack from global superiority, no charge stance 4 writes by Whiskey, no movement/reserve/artillery/fallback side effects, and no player-subordinate retasking. Counts cited from prior DLLs do not satisfy this gate.
- #46 W&L objective-chain guard denial smoke either proves `[TacticalObjectiveGuard] denied objective-chain advance ... reason=player-subordinate-attached` on the current DLL or the plan states that B6 will not exercise objective-chain behavior until that proof exists.
- `BUG-TAC-010` path-shape behavior is implemented by #53 behind `Enable Tactical Pathfinder Discipline`, but B6c movement-heavy work still requires current-DLL enabled smoke or an explicit quarantine. B6c must not build broad movement behavior on top of an unsmoked path-correction valve.

## Non-Goals

B6 does not:

- make `Enable Tactical Macro Stance Scorer` or `Enable Tactical Group Sector Stance` default-on;
- copy Scourge source code or data tables;
- rewrite `AIBattle.UpdateAITasks`;
- add a broad replacement for `BattleUnits.SetWaypoint`;
- Prefix-gate `Regiment.AddToOrderQueue`, `Regiment.AddOrderCourierline`, or `Regiment.ProcessOrders`;
- mutate strategic coordinator state from tactical patches;
- fold artillery execution into the B6c stance/reserve patch;
- fold staged withdrawal execution into the B6c stance/reserve patch;
- alter projectile physics, weapon stats, ammunition pools, or autoresolve weapon parity;
- add a per-unit volley/fire-hold reaction (no vanilla writeable surface exists);
- issue player-subordinate W&L orders when B1/#46 ownership gates deny control.

## Commander Intent Model

B6 adds a pure `TacticalCommanderIntent` model. Intent is derived as described in Strategic-To-Tactical Intent Translation.

Intent bands:

| Intent | Meaning | Allowed local behavior |
|---|---|---|
| `AllOutAttack` | Commit to decisive pressure after high-confidence weakness. | Broad attack pressure, vanilla charge initiation may be permitted, reserves may reinforce main effort. |
| `Attack` | Advance to improve fire and pressure a selected sector. | Attack decisive sector, fix adjacent sectors, preserve flank guard. Charge permitted only against confirmed-weak/exposed enemy. |
| `ProbeIntent` | Demonstrate, find, fix without risking heavy losses. | Advance to useful range, skirmish/screen, deny charge initiation. |
| `Defend` | Hold ground while allowing local counterstroke. | Hold sectors, refuse threatened flank, counterattack only weak or exposed enemies. Charge denied unless `LimitedCounterstroke` conditions all hold. |
| `Hold` | Stay near current position and preserve cohesion. | Local line maintenance, skirmisher screen, fallback only for immediate danger. Charge denied. |
| `HoldToLast` | Hold current position until routed or explicitly released. | No voluntary withdrawal except W&L safety gates. Charge denied. Survival reaction is intent-only — B8 handles any movement. |

`ProbeIntent` is load-bearing. It is not a weak `Attack`. It exists to occupy, scout, screen, and test contact without creating all-sector assault or unmanaged melee. It is renamed from `Probe` to disambiguate from the existing `TacticalSectorMission.Probe` per-sector mission.

## Tactical Playbook Ledger

B6 adds a pure `TacticalPlaybookLedger` that chooses a coherent multi-sector pattern before local reactions are scored.

Inputs:

- B3 contact state, confidence, odds, decisive sector, economy sectors, and inferior-force posture;
- B5 group-sector missions;
- per-side L/center/R axis derived from `objectivechain[i].linegroup_centerunit/leftunits/rightunits` with `flankpositionobj[0/1]` anchor evidence;
- known strongpoints, high ground, cover, flank risk, gaps, and artillery support;
- B2 command-friction state and stale-order evidence;
- commander profile and per-side W&L ownership share (a sector is ineligible as `MainEffortSectorId` if more than half its `linegroup_*units` are `dlcw_isundercommander`);
- reserve availability, local battered-line signals, and timely reinforcement evidence.

Playbooks:

- `HighGroundDefense`: hold high/covered sectors, refuse exposed flank, probe only where safe.
- `CombinedArmsDefense`: hold line, screen forward, preserve artillery, prepare local counterstroke.
- `RefuseRight`: refuse the right flank (`linegroup_rightunits` plus `flankpositionobj[1]`/`anchoredflank[1]`), defend center, probe/fix opposite sector.
- `RefuseLeft`: refuse the left flank (`linegroup_leftunits` plus `flankpositionobj[0]`/`anchoredflank[0]`), defend center, probe/fix opposite sector.
- `ProbeAndFix`: use one or two low-risk sectors to find and fix; deny major commitment.
- `WeakPointPressure`: commit the decisive sector, fix adjacent sectors, hold economy sectors.
- `ReserveHeldCenter`: keep reserve uncommitted until contact or flank risk clarifies.
- `LineRelief`: preserve the battle line by cycling battered front units out if a safe reserve exists.

Output:

- `TacticalPlaybookDecision`
  - `Playbook`;
  - `MainEffortSectorId`;
  - `RefusedFlank` (`Left` / `Right` / `None`, anchored to `flankpositionobj` index);
  - `ProbeSectorIds`;
  - `FixSectorIds`;
  - `HoldSectorIds`;
  - `ReservePolicy`;
  - `LocalReactionPolicy`;
  - `Confidence`;
  - `Reason`.

The playbook never directly issues movement. It is an intent substrate for B6 local reactions, the B6c reserve task, B7 artillery execution, and B8 withdrawal execution.

The playbook must read `objectivechain` outside the #46 Prefix-removed window. The supported read sites are: the existing #35 `TacticalObserverPatch` Postfix on `AIBattle.UpdateMovingTargets` (entries already restored), and the B5 `BattleGroupStancePatch` Postfix on `AdjustGroupAIStance` (which does not overlap #46's window). Other read sites must explicitly verify ordering before shipping.

## Command Friction Extension

B6 extends B2 from read-only interpretation to **decision constraints on B6's own outputs**. B6 does not Prefix-gate vanilla `AddToOrderQueue`, `AddOrderCourierline`, or `ProcessOrders`. Instead these rules constrain what B6 itself emits:

- Corps/army intent may select playbook and main effort, but B6 never directly retasks regiments through any vanilla order surface.
- Division-level intent maps playbook sectors to brigade-level roles in the playbook decision only.
- Brigade-level intent may authorize stance pressure (B5 surface), charge permit/deny (B1/#41 surface), and limited reserve-list bias (named B6c reserve task).
- Regiment-level reaction may modify stance pressure inputs only.
- If B2 reports an order is delayed and contact has materially changed before delivery, the local-reaction scorer downgrades the unit's reaction to `MaintainLine` or `Screen` and emits `request-new-intent` telemetry. B6 does not write a new order to override the in-flight one.
- A fresh order should not be replaced by another order — since B6 issues no orders, this rule is enforced trivially: B6's reactions never escalate beyond the B5 stance surface and the B1 charge surface.
- High-initiative commanders (`GameVars.commander[...].GetCommanderInitiative() ≥ 0.65`) may unlock stance changes when B2 reports order friction; they may not create a new battle plan.

B6 prefers vanilla order-delay/courier surfaces and scorer discipline over direct movement. Runtime behavior remains default-off until smoke proves it does not bypass order delays or modify reserve linkage outside the named B6c reserve task.

## Local Reaction Doctrine

B6 adds a pure `TacticalLocalReactionScorer`.

Reaction outputs:

- `MaintainLine`: keep frontage/cohesion; adjust stance but do not force path rewrite.
- `Screen`: skirmish/screen inside current intent; avoid close decisive contact (vanilla stance 1).
- `ProbeRange`: advance only to useful fire/scouting range.
- `RefuseFlank`: defensive pressure for flank-risk sector (vanilla stance 2 with refused-flank playbook context).
- `LimitedCounterstroke`: local attack only when enemy is weak/exposed and sector confidence is high.
- `DenyCharge`: retained as a model value for future extension, but not emitted by shipped B6b. B6c denial is based on the absence of `PermitCharge`.
- `PermitCharge`: allow vanilla charge initiation when conditions hold (see Charge Permission Surfaces).
- `LineReliefRequest`: mark battered frontline for reserve/relief consideration.
- `LocalFallbackPressure`: retained as a model value for B8, but not emitted by shipped B6b. B8 owns any future derivation and execution.

Inputs:

- intent band;
- playbook role for the unit's sector;
- sector odds and confidence;
- contact state and age;
- morale, fatigue, ammo, casualties, routed-neighbor pressure, and flank risk;
- cover/high ground/fort evidence (`covervalue`, `fortinrange`, `coverobject`);
- target type and target condition when visible (`closestenemyunitfarreg.unittyp`, broken/morale state, distance);
- order-friction state from B2;
- W&L player-subordinate ownership.

Hard constraints:

- `HoldToLast` blocks voluntary fallback. Survival movement is not B6's responsibility — B8 handles it. `HoldToLast` allows `MaintainLine` and `Screen` but emits no `LocalFallbackPressure`.
- `ProbeIntent` cannot produce `PermitCharge` or `LimitedCounterstroke`.
- `Hold` and `Defend` cannot produce `PermitCharge` unless `LimitedCounterstroke` conditions all hold AND the target is broken/exposed.
- `PermitCharge` requires: confirmed contact (B3 contact age ≤ recent threshold), B3 confidence ≥ 0.55, target visible (`closestenemyunitfarreg != null`), target not a strongpoint (target's group not on cover/fort), target close (within charge range), W&L ownership safe, vanilla charge cooldown not active (`Regiment.lastaichargetime + GamePrefs.timetorenewaichargecheck` window respects current battle time). The replaced phrase "no order-friction denial" was incorrect — vanilla charge initiation does not flow through the courier order queue; the cooldown gate is the actual vanilla constraint (decompile 4917).
- `LineReliefRequest` does not mutate reserve lists by itself. Aggregation is the reserve policy ledger's job.
- B6b does not call vanilla fallback or retreat APIs and does not currently emit `LocalFallbackPressure`.

## Reserve And Line Relief In B6

B6 owns reserve-release intent and line-relief pressure because these are part of subordinate local reaction.

Pipeline (explicit):

1. Per-unit `TacticalLocalReactionScorer` emits `LineReliefRequest` when morale, casualties, ammo, or flank state crosses thresholds.
2. Per-side `TacticalReservePolicyLedger` aggregates `LineReliefRequest` outputs together with reserve availability (`objectivechain[i].reservegroups`), playbook `ReservePolicy`, and B3 confidence into a single `TacticalReserveIntent` per side per cycle.
3. `TacticalReserveIntent` telemetry ships in B6a (read-only).
4. Default-off runtime bias ships in a named B6c reserve task that:
   - quotes the exact vanilla mutation shape used (`AssignOperationalGroupToReserve`-style append, or `objectivechain[i].reservegroups.Add/Remove`);
   - takes a structural snapshot of `objectivechain[i].reservegroups` per chain before the bias;
   - applies the bias only when ownership and order-friction gates pass;
   - restores the snapshot on any exception, mirroring #46's try/finally restore pattern.

Reserve policies:

- `HoldReserve`: preserve last reserve for flank/security.
- `PrepareRelief`: reserve is suitable but order should wait for delivery/friction gates.
- `RelieveBatteredLine`: relief is justified by aggregated `LineReliefRequest` evidence and safe path evidence.
- `FlankGuard`: reserve protects exposed flank instead of feeding main effort.
- `ExploitWeakPoint`: reserve supports selected main effort only after contact confidence is high.

## Charge Permission Surfaces

Vanilla charge flow:

1. `AIBattle.AdjustGroupAIStance()` (4221) writes `aigroup.ai_stanceordered = 4` at line 4242 when group strength, macro, and unit-symbol conditions all hold.
2. `AIBattle.MicroAICheckForCharges(Regiment, int)` (4905) reads `aigroup.ai_stance == 4` and calls `unit.SetMovementMode(3)` at line 4919, gated by the `lastaichargetime + GamePrefs.timetorenewaichargecheck` cooldown.

B5's existing Postfix (`Patches/BattleGroupStancePatch.cs`) currently silently demotes a vanilla `ai_stanceordered == 4` whenever its scorer returns Apply with stance 1/2/3 and the existing values differ. That is a pre-existing behavior bug; B6c must fix it.

B6 charge surface contract:

- **PermitCharge (default)**: B5 Postfix detects `group.ai_stanceordered == 4` post-vanilla and treats it as `Skip` with reason `vanilla-charge-preserved` whenever the per-group local reaction is `PermitCharge` or absent. B1/#41 then runs vanilla's per-unit `SetMovementMode(3)` initiation for that group.
- **No `PermitCharge`**: B5 Postfix detects `group.ai_stanceordered == 4` post-vanilla and the per-group reaction is not `PermitCharge`. It writes stance 3 (Defend) explicitly through `bunits.ChangeStance(...)` and emits `[TacticalChargeDeny] reason=...` when `Enable Tactical Charge Denial` is enabled. Independently, B1/#41 `BattleChargeGatePatch` is extended to read the same per-group reaction state and deny the per-unit `SetMovementMode(3)` when the reaction is not `PermitCharge` (defense in depth). The W&L ownership branch of #41 remains unchanged; the new branch is a second deny condition under `Enable Tactical Charge Denial`.
- **No-Op (`PermitCharge` or charge-denial config off)**: B5 Postfix preserves the vanilla decision. The doctrine layer does not silently demote vanilla charges when charge denial is disabled.

This contract is the resolution to the spec's prior ambiguity and to the pre-existing silent demotion in `Patches/BattleGroupStancePatch.cs:74-79`.

## Runtime Application Boundaries

B6 runtime behavior is default-off behind explicit per-reaction configs:

- `Enable Tactical Commander Intent Doctrine = false` — gates the playbook ledger and intent emission.
- `Enable Tactical Local Reaction Doctrine = false` — gates the local reaction scorer outputs feeding stance pressure.
- `Enable Tactical Charge Denial = false` — gates B5 stance-4 demotion and B1/#41 per-unit charge veto based on non-`PermitCharge` reactions.
- `Enable Tactical Reserve Intent Telemetry = false` — gates `[TacticalReserveIntent]` emission (read-only).
- `Enable Tactical Reserve List Mutation = false` — gates the named B6c reserve-list bias task.

Per-reaction switches keep rollback granular: regressions in reserve-list mutation can be flipped without losing intent telemetry; regressions in charge denial can be flipped without losing stance pressure.

Initial runtime targets:

- augment tactical observer with `[TacticalIntent]`, `[TacticalPlaybook]`, `[TacticalLocalReaction]`, `[TacticalReserveIntent]`, and `[TacticalChargeDeny]`;
- apply stance pressure only through existing B5-style `ChangeStance` boundaries when B5 is enabled and safe, with the new vanilla-stance-4 contract from Charge Permission Surfaces;
- deny unsafe charge pressure through B1/#41 extended to consume `Enable Tactical Charge Denial` plus per-group reaction state (W&L ownership branch unchanged);
- emit reserve/line-relief intent and apply default-off reserve behavior only through the named B6c reserve-list mutation task with snapshot/restore.

Runtime application must not:

- call broad `BattleUnits.SetWaypoint(GameObject,...)` or `(Regiment,...)`;
- call artillery bombardment methods;
- call full retreat/end-battle methods;
- directly modify player-subordinate W&L units;
- write `ai_stanceordered = 4` from B6 (PermitCharge means leave vanilla writes alone, never originate them);
- mutate `objectivechain.reservegroups` outside the named B6c task with proven snapshot/restore;
- Prefix-gate `AddToOrderQueue`, `AddOrderCourierline`, or `ProcessOrders`.

## Telemetry

Expected lines (all signature-gated by the existing tactical observer throttle and disabled when tactical observer is disabled):

- `[TacticalIntent] side=... intent=... posture=... commanderInit=... macro=... reason=... confidence=...`
- `[TacticalPlaybook] side=... playbook=... main=<sectorId> refuse=<Left|Right|None> probe=<sectorId,...> fix=<sectorId,...> hold=<sectorId,...> reserve=<policy> reason=...`
- `[TacticalLocalReaction] side=... group=<gameObjectName#instanceID> sector=<sectorId> intent=... reaction=... allowed=<true|false> reason=... confidence=...`
- `[TacticalReserveIntent] side=... chain=<objectivechainIndex> reserveGroup=<gameObjectName#instanceID|none> policy=... targetSector=<sectorId> reason=... confidence=...`
- `[TacticalChargeDeny] side=... group=<gameObjectName#instanceID> reason=... target=<gameObjectName#instanceID|none> targetCondition=<broken|fresh|strongpoint|none>`

## Tests

Pure tests must cover:

- `ProbeIntent` produces screen/probe pressure and denies charge.
- `Attack` with one decisive sector keeps adjacent sectors as fix/hold.
- `Defend` on high ground allows `LimitedCounterstroke` only against weak/exposed enemy.
- `Hold` keeps units near current role and rejects unnecessary retask; emits no `PermitCharge`.
- `HoldToLast` blocks voluntary fallback intent. Shipped B6b emits no `LocalFallbackPressure`; B8 must add or derive that input before execution.
- `RefuseRight` and `RefuseLeft` assign one flank to refuse using the simulated `linegroup_rightunits` / `linegroup_leftunits` axis while center holds or fixes.
- `CombinedArmsDefense` holds line and preserves artillery/reserve pressure without assault.
- stale delayed order downgrades reaction to `MaintainLine`/`Screen` and emits `request-new-intent`.
- high initiative permits local reaction inside intent but not new battle plan creation.
- battered frontline emits `LineReliefRequest` per unit unconditionally on threshold cross.
- per-side reserve aggregator emits `RelieveBatteredLine` only when reserve is safe and not the last flank guard.
- low ammo/fatigue/casualties raise relief/fallback intent and lower charge permission.
- W&L player-subordinate ownership denies behavior application.
- #53 implements the `BUG-TAC-010` path-risk fix behind a default-off valve; broad runtime movement application stays blocked until enabled smoke proves the correction stable or the implementation quarantines that movement surface.
- **Vanilla stance-4 preservation under PermitCharge**: simulated `ai_stanceordered == 4` post-vanilla with PermitCharge produces Skip with reason `vanilla-charge-preserved` and no overwrite.
- **Vanilla stance-4 demotion when not `PermitCharge`**: simulated `ai_stanceordered == 4` with a non-`PermitCharge` B6b reaction produces explicit Apply stance 3 with `[TacticalChargeDeny]` telemetry, independent of B1/#41.
- **B1/#41 charge-denial defense in depth**: simulated `ai_stance == 4` reaching `MicroAICheckForCharges` with a non-`PermitCharge` reaction under `Enable Tactical Charge Denial` denies `SetMovementMode(3)`.
- **Strategic→tactical translation**: each `OperationPosture` value yields the documented intent band given baseline B3/B4 evidence; missing plan falls back to B4-only mapping.
- **Playbook ownership share**: a sector with > 50% `dlcw_isundercommander` units cannot be selected as `MainEffortSectorId`.
- **Naming disambiguation**: `ProbeIntent` battle-wide and `TacticalSectorMission.Probe` per-sector compose without collision (intent `ProbeIntent` + sector mission `AttackWeakPoint` is rejected; intent `Defend` + sector mission `Probe` is allowed).

## Implementation Slices

B6 implementation plans should be written now as separate plan files:

- `docs/superpowers/plans/2026-05-07-tactical-b6a-commander-intent-playbook.md`
- `docs/superpowers/plans/2026-05-07-tactical-b6b-local-reaction-scorer.md`
- `docs/superpowers/plans/2026-05-07-tactical-b6c-runtime-application.md`
- `docs/superpowers/plans/2026-05-07-tactical-b7-artillery-strongpoint-runtime.md`
- `docs/superpowers/plans/2026-05-07-tactical-b8-staged-withdrawal-runtime.md`

Each plan must state:

- exact files;
- exact vanilla anchors rechecked before patching (this spec's anchors are point-in-time; verify before edits);
- test additions;
- config switches (per the per-reaction list above);
- runtime smoke markers;
- rollback switch;
- DLL build/deploy/hash verification if code changes affect the plugin.

The B6c plan must specifically:

- describe the B5 stance-4 preservation/demotion contract changes to `Patches/BattleGroupStancePatch.cs`;
- describe the B1/#41 extension to consume non-`PermitCharge` reactions under `Enable Tactical Charge Denial`;
- describe the named reserve-list mutation task with snapshot/restore;
- not bundle artillery (B7) or staged withdrawal (B8) execution.

## Smoke Gates

Minimum B6c focused smoke:

1. Confirm the deployed DLL hash matches the local `dist/WhiskeyRealism.dll` per the standing build/deploy/hash discipline.
2. Enable tactical observer, B4/B5 scorer switches, B6 intent/local-reaction switches, and `Enable Tactical Charge Denial` only for the focused run. Leave `Enable Tactical Reserve List Mutation` off unless that is the focus.
3. Start a W&L land battle with player-subordinate attachments present if possible.
4. Confirm `[TacticalIntent]`, `[TacticalPlaybook]`, and `[TacticalLocalReaction]` appear without repeated exceptions, missing anchors, or Harmony failures.
5. Confirm `ProbeIntent` does not create all-sector attack or charge initiation.
6. Confirm W&L player-subordinate groups are denied by ownership gates.
7. Confirm `[TacticalChargeDeny]` fires only when reaction state matches; confirm vanilla charges that should be permitted reach `MicroAICheckForCharges` and produce `SetMovementMode(3)` (observable via existing `[TacticalChargeGuard]` telemetry).
8. Confirm reserve intent logs do not move reserves unless the active B6c reserve task explicitly owns and verifies that behavior.
9. Confirm no `SetWaypoint`, reserve-list, artillery, fallback, or retreat side effects occur unless the specific B6c plan owns and verifies that side effect.

## Rollback

Rollback is config-first and per-reaction:

- disable `Enable Tactical Reserve List Mutation` to remove reserve-list writes (telemetry preserved).
- disable `Enable Tactical Charge Denial` to remove the charge-deny path; vanilla charges resume.
- disable `Enable Tactical Local Reaction Doctrine` to remove local-reaction outputs.
- disable `Enable Tactical Commander Intent Doctrine` to remove intent/playbook behavior.
- leave B3 telemetry enabled independently when useful because it is read-only;
- leave B1/#46 W&L safety guards enabled for focused W&L smoke if they are the subject of the test.

If B6 causes all-sector attack, player-subordinate retasking, repeated logs, macro flip-flop, or unexpected movement, disable the narrowest B6 switch first, then escalate before changing B4/B5.

## Not Verified Yet

- Scourge concept evidence does not prove that Grand Tactician can safely execute identical local reactions.
- B4/B5 behavior smoke on the **current deployed DLL** is the actual precondition; smoke counts on prior DLLs do not satisfy it.
- #46 objective-chain denial still needs restart smoke on the current DLL.
- `BUG-TAC-010` path-shape behavior has a default-off #53 fix; B6 movement-heavy implementation remains blocked until enabled smoke proves the path-correction valve stable or the plan quarantines that surface.
- Runtime reserve behavior remains mostly unexercised; B6 begins with reserve intent telemetry rather than reserve-list mutation.
- The B5 stance-4 silent demotion fix in `Patches/BattleGroupStancePatch.cs` is described here as part of the B6c contract but not yet implemented; B6c must land it before any charge-denial runtime experiment.

## Success Criteria

B6 succeeds when Whiskey has a tested commander-intent and playbook layer that explains why a side is probing, defending, refusing, attacking, or holding, and when local subordinate reactions consume that intent without bypassing order friction or W&L ownership.

The first successful B6 implementation needs to make B5 stance decisions meaningful, bounded, observable, and connected to:

- the strategic `OperationPosture` translation;
- the existing B1/#41 charge surface (`PermitCharge` as the only positive permission; non-`PermitCharge` reactions with telemetry and defense-in-depth);
- the B7 artillery and B8 withdrawal runtime tracks (intent only, no execution coupling).
