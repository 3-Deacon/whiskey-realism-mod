# Tactical B6 Commander Intent And Local Reaction Design

Status: active design spec. This is a Slice B tactical design artifact, not an implementation plan.

Scope: B6 turns B3-B5 contact, sector, odds, macro, and group-stance outputs into a Scourge-inspired commander-intent layer and local subordinate reaction doctrine. It defines the full B6 doctrine program now: intent, playbooks, command friction, local reaction, charge permission, reserve/line relief, artillery-support intent, and withdrawal pressure. Implementation is ordered into bounded plans because these surfaces write different vanilla battle state. B6 does not make B4/B5 default-on and does not port Scourge code.

## Decision

B6 should own the Scourge-derived idea that stance is commander intent, not just a numeric aggression knob.

The B6 model includes:

- stance-as-intent bands;
- a tactical playbook layer over B3 sectors;
- command-friction extensions over B2;
- local subordinate reaction doctrine for brigades and regiments;
- delayed reserve-release and line-relief intent;
- artillery-support and strongpoint intent for B7 execution;
- staged withdrawal pressure for B8 execution;
- charge permission pressure as a local reaction decision, still constrained by B1 W&L safety gates.

B6 should cover the whole doctrine now, but not as one giant patch. The implementation split should be:

1. `B6a Commander Intent And Playbook Ledger` - pure ledgers, telemetry, no behavior writes.
2. `B6b Local Reaction Scorer` - pure subordinate reaction decisions and tests.
3. `B6c Runtime Application` - stance, charge-denial, reserve-intent, and local-reaction behavior behind explicit config.
4. `B7 Artillery And Strongpoint Runtime` - bombardment/strongpoint execution using B6 intent.
5. `B8 Staged Withdrawal Runtime` - covered fallback/rear-guard execution using B6 intent.

Artillery and withdrawal are immediate same-program execution tracks with separate plans because they patch different vanilla methods:

- `B7 Artillery And Strongpoint Doctrine` owns bombardment, unlimber, counterbattery, and artillery fallback.
- `B8 Staged Withdrawal Doctrine` owns covered fallback, rear guard, and full-retreat escalation.

## Evidence Boundary

Scourge of War is useful as design evidence, not as source code to copy.

The installed SDK license/readme says the SDK is provided under NorbSoftDev rights and that all rights are reserved. Whiskey can use the exposed SDK, manual, and data files to identify concepts and comparable tactical surfaces, but B6 must implement original Whiskey logic against Grand Tactician's vanilla methods.

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

Confirmed tactical surfaces from existing Slice B work:

- `AIBattle.CheckGlobalAIStrategy()` line 6314 owns battle-level `macroai`.
- `AIBattle.AdjustGroupAIStance()` line 4221 owns group-level `ai_stance`.
- `AIBattle.GetGroupStrength(...)` line 6025 returns group own/enemy ratio with flank, morale, experience, and surrounding modifiers.
- `Regiment.UpdateUnitRangeFast(...)` line 122545 populates contact, angle-slice enemy strength, own strength, flank, retreat-angle, and path-destination evidence.
- `AIBattle.CheckUseOfReserves(...)` line 6062 is the reserve-use surface.
- `AIBattle.LinkReservesToLineGroup()` line 6642 and `AIBattle.AssignReserves()` line 7017 are reserve organization surfaces.
- `AIBattle.CheckAIBombardment(...)` line 3869 is the artillery bombardment surface, but B6 only reads artillery support/strongpoint context; B7 owns behavior there.
- `AIBattle.CheckLineFallbacks(...)` line 5118 and `AIBattle.MicroAICheckForRetreats(...)` line 4817 are fallback/retreat surfaces, but B6 only emits local fallback pressure; B8 owns staged withdrawal execution.
- `Regiment.AddOrderCourierline(...)` line 125009 and `Regiment.ProcessOrders()` line 125173 expose courier/order-delay behavior.
- `AIBattle.UpdateMovingTargets()` line 6870 is already guarded by #46 for W&L player-subordinate objective-chain exposure.

## Verified Applicability Check

The Scourge comparison supports B6 as a doctrine/scoring layer, not as a direct behavior port.

| Scourge logic | Grand Tactician surface | B6 applicability |
|---|---|---|
| Stance bands express commander intent from all-out attack through hold-to-last. | `AIBattle.AdjustGroupAIStance()` sets group `ai_stance` through vanilla `ChangeStance(...)` and direct `ai_stance` / `ai_stanceordered` writes. | Apply. B6 can model intent bands and feed only safe B5-style stance pressure. It must not write charge stance 4. |
| `Probe` is a distinct commander intent: occupy, test, and demonstrate without heavy-loss commitment. | B5 already maps explicit Probe and defensive weak-point pressure to vanilla stance 1 screening/probe instead of hold. | Apply strongly. B6 should make `Probe` a first-class intent/playbook role, with tests proving it does not become all-sector attack or charge permission. |
| Division play tables assign multi-spot patterns such as high-ground defense, combined-arms defense, and refused flanks. | B3/B5 sector ledgers can identify sectors and group missions, but currently score groups mostly independently. | Apply. Add `TacticalPlaybookLedger` before local reactions so B5 stance choices become a coherent plan such as refuse-right/probe-center/hold-left. |
| Courier logic lets higher commanders transmit orders to subordinates that lack orders, then subordinates execute with AI judgment. | `Regiment.AddOrderCourierline(...)` and `Regiment.ProcessOrders()` expose bugle/courier delay, order state, and delivery. B2 already reads these as friction telemetry. | Apply now as command constraints. B6 blocks fresh retasks when orders are stale/undelivered and logs request-new-intent; direct courier synthesis needs its own B6c task with vanilla order-path proof. |
| Local infantry AI handles skirmishers, line maintenance, flanking, volley control, charge permission, morale fallback, and skirmisher fallback. | GT has scattered local surfaces: stance adjustment, charge check, fallback/retreat checks, reserve support, and path/order APIs. Many directly write movement paths or reserve lists. | Apply now as scorer coverage. B6a/B6b score every reaction; B6c applies the low-risk subset first and creates explicit runtime tasks for movement, fallback, and reserve mutation. |
| Morale and danger can trigger fallback/retreat from the local unit layer. | `AIBattle.CheckLineFallbacks(...)` writes fallback paths/modes; `MicroAICheckForRetreats(...)` controls retreat movement. | Apply now as `LocalFallbackPressure` and B8 staged-withdrawal inputs. Execution belongs to the B8 runtime plan so fallback does not bypass sector/order/reserve context. |
| Reserve behavior can support threatened or weak points. | `CheckUseOfReserves(...)` can directly path support units; `LinkReservesToLineGroup()` and `AssignReserves()` mutate links and objective-chain reserve membership. | Apply now as `TacticalReserveIntent` and B6c runtime tasks. Reserve-list mutation must quote the exact vanilla mutation shape, snapshot state, and define rollback. |

Conclusion: B6 can use the Scourge logic if it is translated into original Whiskey intent/playbook/reaction scorers. The immediate work is the full doctrine stack: intent, playbook, local reaction, reserve intent, artillery-support intent, withdrawal pressure, and bounded runtime tasks. The order of implementation is a blast-radius control, not a decision to leave features out.

## Preconditions

B6 implementation can start immediately as B6a/B6b pure doctrine and B6c runtime planning. Runtime writes that mutate vanilla battle state must satisfy these gates or explicitly quarantine the unsafe surface in the implementation plan:

- B4/B5 focused runtime smoke confirms bounded `[TacticalMacroDecision]` and `[TacticalGroupDecision]` lines on the current deployed DLL.
- B4/B5 smoke shows no repeated exceptions, Harmony failures, missing anchors, macro flip-flop, all-sector attack from global superiority, charge stance 4 writes, movement/reserve/artillery/fallback side effects, or player-subordinate retasking.
- #46 W&L objective-chain guard denial smoke either proves `[TacticalObjectiveGuard] denied objective-chain advance ... reason=player-subordinate-attached` or the plan states that B6 will not exercise objective-chain behavior until that proof exists.
- `BUG-TAC-010` path-shape behavior remains out of B6 unless the implementation plan first resolves or quarantines path-shape correction. B6 must not build broad movement behavior on top of a known path-backtrack defect.

## Non-Goals

B6 does not:

- make `Enable Tactical Macro Stance Scorer` or `Enable Tactical Group Sector Stance` default-on;
- copy Scourge source code or data tables;
- rewrite `AIBattle.UpdateAITasks`;
- add a broad replacement for `BattleUnits.SetWaypoint`;
- mutate strategic coordinator state from tactical patches;
- fold artillery execution into the B6c stance/reserve patch;
- fold staged withdrawal execution into the B6c stance/reserve patch;
- alter projectile physics, weapon stats, ammunition pools, or autoresolve weapon parity;
- issue player-subordinate W&L orders when B1/#46 ownership gates deny control.

## Commander Intent Model

B6 adds a pure `TacticalCommanderIntent` model. Intent is derived from B3-B5 evidence, not from Scourge values.

Intent bands:

| Intent | Meaning | Allowed local behavior |
|---|---|---|
| `AllOutAttack` | Commit to decisive pressure after high-confidence weakness. | Broad attack pressure, limited charge permission, reserves may reinforce main effort. |
| `Attack` | Advance to improve fire and pressure a selected sector. | Attack decisive sector, fix adjacent sectors, preserve flank guard. |
| `Probe` | Demonstrate, find, and fix without risking heavy losses. | Advance to useful range, skirmish/screen, hold fire discipline, deny major charge. |
| `Defend` | Hold ground while allowing local counterstroke. | Hold sectors, refuse threatened flank, counterattack only weak or exposed enemies. |
| `Hold` | Stay near current position and preserve cohesion. | Local line maintenance, skirmisher screen, fallback only for immediate danger. |
| `HoldToLast` | Hold current position until routed or explicitly released. | No voluntary withdrawal except local survival moves and W&L safety gates. |

`Probe` is load-bearing. It is not a weak `Attack`. It exists to occupy, scout, screen, and test contact without creating all-sector assault or unmanaged melee.

## Tactical Playbook Ledger

B6 adds a pure `TacticalPlaybookLedger` that chooses a coherent multi-sector pattern before local reactions are scored.

Inputs:

- B3 contact state, confidence, odds, decisive sector, economy sectors, and inferior-force posture;
- B5 group-sector missions;
- known strongpoints, high ground, cover, flank risk, gaps, and artillery support;
- B2 command-friction state and stale-order evidence;
- commander profile and current W&L ownership boundary;
- reserve availability, local battered-line signals, and timely reinforcement evidence.

Playbooks:

- `HighGroundDefense`: hold high/covered sectors, refuse exposed flank, probe only where safe.
- `CombinedArmsDefense`: hold line, screen forward, preserve artillery, prepare local counterstroke.
- `RefuseRight`: refuse right flank, defend center, probe/fix opposite sector.
- `RefuseLeft`: refuse left flank, defend center, probe/fix opposite sector.
- `ProbeAndFix`: use one or two low-risk sectors to find and fix; deny major commitment.
- `WeakPointPressure`: commit the decisive sector, fix adjacent sectors, hold economy sectors.
- `ReserveHeldCenter`: keep reserve uncommitted until contact or flank risk clarifies.
- `LineRelief`: preserve the battle line by cycling battered front units out if a safe reserve exists.

Output:

- `TacticalPlaybookDecision`
  - `Playbook`;
  - `MainEffortSectorId`;
  - `RefusedFlank`;
  - `ProbeSectorIds`;
  - `FixSectorIds`;
  - `HoldSectorIds`;
  - `ReservePolicy`;
  - `LocalReactionPolicy`;
  - `Confidence`;
  - `Reason`.

The playbook must not directly issue movement. It is an intent substrate for B6 local reactions, B7 artillery execution, and B8 withdrawal execution.

## Command Friction Extension

B6 extends B2 from read-only interpretation to decision constraints.

Rules:

- Corps/army intent may select playbook and main effort, but may not directly retask regiments.
- Division-level intent maps playbook sectors to brigade-level roles.
- Brigade-level intent may authorize local line maintenance, skirmish deployment, limited fallback, and limited charge permission.
- Regiment-level reaction may preserve cover, frontage, fire discipline, morale, and immediate survival.
- If an order is delayed and contact materially changes before delivery, the subordinate may hold, request new intent, or execute a safer local reaction.
- A fresh order should not be replaced by another order unless contact evidence makes the first order dangerous, impossible, or stale.
- High-initiative commanders may react locally inside intent bounds; they may not create a new battle plan.

B6 should prefer vanilla order-delay/courier surfaces and scorer discipline over direct movement. Runtime behavior must be default-off until smoke proves it does not bypass order delays.

## Local Reaction Doctrine

B6 adds a pure `TacticalLocalReactionScorer`.

Reaction outputs:

- `MaintainLine`: keep frontage/cohesion; adjust stance but do not force path rewrite.
- `Screen`: skirmish/screen inside current intent; avoid close decisive contact.
- `ProbeRange`: advance only to useful fire/scouting range.
- `RefuseFlank`: defensive pressure for flank-risk sector.
- `LimitedCounterstroke`: local attack only when enemy is weak/exposed and sector confidence is high.
- `DenyCharge`: block charge pressure despite aggressive macro/stance.
- `PermitCharge`: allow charge pressure only against broken, close, unsupported, non-strongpoint target.
- `LineReliefRequest`: mark battered frontline for reserve/relief consideration.
- `LocalFallbackPressure`: local survival fallback pressure, not B8 staged withdrawal.
- `HoldFireOrVolleyPressure`: fire-discipline pressure when contact is low-confidence or volley timing matters.

Inputs:

- intent band;
- playbook role for the unit's sector;
- sector odds and confidence;
- contact state and age;
- morale, fatigue, ammo, casualties, routed-neighbor pressure, and flank risk;
- cover/high ground/fort evidence;
- target type and target condition when visible;
- order-friction state;
- W&L player-subordinate ownership.

Hard constraints:

- `HoldToLast` can still allow immediate survival reactions, but not voluntary line abandonment.
- `Probe` cannot produce `PermitCharge`.
- `PermitCharge` requires confirmed contact, high confidence, safe ownership, close broken/exposed target, no strongpoint, and no order-friction denial.
- `LineReliefRequest` does not mutate reserve lists by itself.
- `LocalFallbackPressure` does not call full battle retreat APIs.

## Reserve And Line Relief In B6

B6 may own reserve-release intent and line-relief pressure because these are part of subordinate local reaction.

B6 may directly mutate reserve membership only in a named B6c runtime task that quotes the exact vanilla reserve mutation shape and defines snapshot/rollback.

The immediate implementation order is:

- pure `TacticalReserveIntent` decisions;
- telemetry comparing Whiskey intent to vanilla reserve action;
- default-off runtime bias where vanilla already has a safe reserve candidate and order-friction permits a release;
- explicit reserve-list mutation only after the B6c task records the pre/post vanilla state and rollback path.

Reserve policies:

- `HoldReserve`: preserve last reserve for flank/security.
- `PrepareRelief`: reserve is suitable but order should wait for delivery/friction gates.
- `RelieveBatteredLine`: relief is justified by morale/casualties/ammo and safe path evidence.
- `FlankGuard`: reserve protects exposed flank instead of feeding main effort.
- `ExploitWeakPoint`: reserve supports selected main effort only after contact confidence is high.

## Runtime Application Boundaries

B6 runtime behavior must be default-off behind a new config. Suggested names:

- `Enable Tactical Commander Intent Doctrine = false`
- `Enable Tactical Local Reaction Doctrine = false`

Runtime application may write only after pure scoring, ownership checks, order-friction checks, and smoke gates pass.

Initial runtime targets:

- augment tactical observer with `[TacticalIntent]`, `[TacticalPlaybook]`, and `[TacticalLocalReaction]`;
- apply stance pressure only through existing B5-style `ChangeStance` boundaries when B5 is enabled and safe;
- deny unsafe charge pressure through existing B1 decision logic;
- emit reserve/line-relief intent and apply default-off reserve behavior only through named B6c tasks.

Runtime application must not:

- call broad `SetWaypoint`;
- call artillery bombardment methods;
- call full retreat/end-battle methods;
- directly modify player-subordinate W&L units;
- write charge stance 4;
- mutate `objectivechain.reservegroups` without snapshot/restore proof.

## Telemetry

Expected lines:

- `[TacticalIntent] side=... intent=... macro=... playbook=... reason=... confidence=...`
- `[TacticalPlaybook] side=... playbook=... main=... refuse=... probe=... fix=... hold=... reserve=... reason=...`
- `[TacticalLocalReaction] side=... group=... sector=... intent=... reaction=... allowed=... reason=... confidence=...`
- `[TacticalReserveIntent] side=... reserve=... policy=... targetSector=... reason=... confidence=...`

All logs must be signature-gated and disabled when tactical observer is disabled.

## Tests

Pure tests must cover:

- `Probe` produces screen/probe pressure and denies charge.
- `Attack` with one decisive sector keeps adjacent sectors as fix/hold.
- `Defend` on high ground allows local counterstroke only against weak/exposed enemy.
- `Hold` keeps units near current role and rejects unnecessary retask.
- `HoldToLast` blocks voluntary fallback but allows immediate survival reaction.
- `RefuseRight` and `RefuseLeft` assign one flank to refuse while center holds or fixes.
- `CombinedArmsDefense` holds line and preserves artillery/reserve pressure without assault.
- stale delayed order downgrades attack to hold/request-new-intent when contact changes.
- high initiative permits local reaction inside intent but not new battle plan creation.
- battered frontline creates `LineReliefRequest` only when reserve is safe and not the last flank guard.
- low ammo/fatigue/casualties raise relief/fallback pressure and lower charge permission.
- W&L player-subordinate ownership denies behavior application.
- `BUG-TAC-010` path-risk evidence blocks runtime movement application.

## Implementation Slices

B6 implementation plans should be written now as separate plan files:

- `docs/superpowers/plans/2026-05-07-tactical-b6a-commander-intent-playbook.md`
- `docs/superpowers/plans/2026-05-07-tactical-b6b-local-reaction-scorer.md`
- `docs/superpowers/plans/2026-05-07-tactical-b6c-runtime-application.md`
- `docs/superpowers/plans/2026-05-07-tactical-b7-artillery-strongpoint-runtime.md`
- `docs/superpowers/plans/2026-05-07-tactical-b8-staged-withdrawal-runtime.md`

Each plan must state:

- exact files;
- exact vanilla anchors rechecked before patching;
- test additions;
- config switches;
- runtime smoke markers;
- rollback switch;
- DLL build/deploy/hash verification if code changes affect the plugin.

## Smoke Gates

Minimum B6c focused smoke:

1. Enable tactical observer and B4/B5 scorer switches.
2. Enable B6 intent/local-reaction switches only for the focused run.
3. Start a W&L land battle with player-subordinate attachments present if possible.
4. Confirm `[TacticalIntent]`, `[TacticalPlaybook]`, and `[TacticalLocalReaction]` appear without repeated exceptions, missing anchors, or Harmony failures.
5. Confirm `Probe` does not create all-sector attack or charge stance 4.
6. Confirm W&L player-subordinate groups are denied by ownership gates.
7. Confirm reserve intent logs do not move reserves unless the active B6c task explicitly owns and verifies that behavior.
8. Confirm no `SetWaypoint`, reserve-list, artillery, fallback, or retreat side effects occur unless the specific B6c plan owns and verifies that side effect.

## Rollback

Rollback must be config-first:

- disable `Enable Tactical Local Reaction Doctrine` to remove runtime B6 behavior;
- disable `Enable Tactical Commander Intent Doctrine` to remove intent/playbook behavior;
- leave B3 telemetry enabled independently when useful because it is read-only;
- leave B1/#46 W&L safety guards enabled for focused W&L smoke if they are the subject of the test.

If B6 causes all-sector attack, player-subordinate retasking, repeated logs, macro flip-flop, or unexpected movement, disable B6 before changing B4/B5.

## Not Verified Yet

- Scourge concept evidence does not prove that Grand Tactician can safely execute identical local reactions.
- B4/B5 behavior on the current contact-evidence DLL still needs focused restart smoke before those specific writes become default-on.
- #46 objective-chain denial still needs restart smoke.
- `BUG-TAC-010` path-shape behavior remains unresolved and should block any B6 movement-heavy implementation.
- Runtime reserve behavior remains mostly unexercised; B6 should begin with reserve intent telemetry rather than reserve-list mutation.

## Success Criteria

B6 succeeds when Whiskey has a tested commander-intent and playbook layer that explains why a side is probing, defending, refusing, attacking, or holding, and when local subordinate reactions consume that intent without bypassing order friction or W&L ownership.

The first successful B6 implementation needs to make B5 stance decisions meaningful, bounded, observable, and connected to the B7 artillery and B8 withdrawal runtime tracks.
