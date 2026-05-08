# Scourge Tactical Adaptation Design

Status: active design supplement. This is a Slice B tactical design artifact, not an implementation plan. Operational/campaign-map companion: [`2026-05-08-scourge-operational-recon-commitment-design.md`](2026-05-08-scourge-operational-recon-commitment-design.md) (Slice C — owns sighting bridge, force-ratio commit, advance guard / picket doctrine, convergent multi-arm phasing, commitment ledger).

Scope: adapt transferable tactical ideas observed in the local Scourge of War Remastered SDK and decompiled binaries into original Whiskey Realism doctrine for Grand Tactician: The Civil War. Scourge is comparative design evidence only. Grand Tactician's decompile and current Whiskey code own all implementation surfaces.

## Decision

Adopt four Scourge-informed concepts because Grand Tactician exposes usable tactical anchors:

- commander arbitration over local impulses;
- artillery support-screen and fallback awareness;
- destination discipline before movement writes;
- staged morale-pressure response and help-request telemetry.

Do not copy Scourge code, tables, constants, strings, assets, SDK structures, or binary output. Whiskey will implement original C# logic against verified Grand Tactician methods and fields.

This spec changes Slice B doctrine inputs. It does not authorize new runtime writes by itself. Existing plans remain the execution boundary:

- B6c owns commander intent/runtime application and local-reaction gating.
- B7 owns artillery bombardment/counterbattery/cancel-bombard decisions.
- B8 owns fallback, withdrawal, rear guard, and full-retreat staging.

## Adversarial Review (2026-05-08)

This section captures corrections applied after re-reading both the Scourge SDK source (`artyai.cpp`, `offcmds.cpp`, `unitai.cpp`, `offai.cpp`) and the Grand Tactician decompile against the original draft. Subsections downstream reflect these changes; this list is a recap so reviewers can audit the deltas without re-diffing the document.

1. **Support screen cannot reuse `UnitRange.closestownunitnonrouted`.** Vanilla populates that field at decompile lines 122757–122774 with any non-routed friendly satisfying `unittyp <= 13 && unittyp != 5`, including other artillery batteries (`unittyp == 2`). Scourge `artyai.cpp:200-208` requires the screening unit to be `eUnitInf` or `eUnitCav` and `CanFight()`. Whiskey must do its own scan and discard non-screen unit types.
2. **Scourge gun panic is gated on crew morale, not just support.** `artyai.cpp:209` panics when `(MorUnitBon == 0 || sup == 0)` AND enemy is inside `panicdistance`. The original Whiskey output collapsed this into a binary screened/unsupported state. The corrected model produces a screened/shaken/unsupported triple that maps to vanilla `Regiment.morale` via existing `GamePrefs.moraletriggerforfallbackifenemyclose[stance]` thresholds.
3. **Destination discipline must tier by unit type.** Scourge uses 5 yards for `CanGunRedeploy`, weapon `LongRangeYds()` for `CanRedeployLine`, and explicitly skips marching skirmishers in line checks (`offcmds.cpp:183-247`). Grand Tactician's `CheckForSimilarPositions(...)` at 8669 uses a single `GamePrefs.distancetoenemytocancelinterruption` threshold. The Whiskey scorer must layer Scourge-style tiering, not just call the vanilla helper.
4. **Morale-delta scoring needs a concrete snapshot ledger.** The original draft acknowledged the missing prior-morale field but did not specify identity, retention, or clear-out semantics. The new `TacticalMoraleSnapshotLedger` subsection pins those.
5. **`outflanked` is a tiered `int`, not a bool.** Field declared at 111488 as `public int outflanked`; vanilla checks `outflanked > 0` (4515) and treats higher values as more flanked. Replace boolean treatment with tier scoring.
6. **`PerformAIActionDLCWL(...)` is `private static` on `AIBattle`** (5101). Whiskey scorers cannot call it directly. The spec now requires Whiskey to replicate the W&L gate via the public `(ai_feudstance == -1) | (isplayeraiorfeud == 2)` idiom plus reflection into the static method only when telemetry needs the exact answer.
7. **Help-request telemetry needed an explicit sink.** Added `TacticalSectorLedger` integration so B6 playbook input is observable, not just a model output.
8. **Ammo ratio idiom.** Vanilla uses both `Tools.SumUp(ammo) / 3f` (3893, 19186, 21038) and `Tools.SumUp(ammo) / (float)ammo.Length` (117486). The corrected B7 input uses `Tools.SumUp(ammo) / (float)ammo.Length` to stay correct if `ammo.Length` ever differs from 3.
9. **Reserve exclusion already in vanilla.** Reserves with `ai_stance == 2` are excluded from movement at 6672. Any default-off Whiskey reserve-relief work must inherit this exclusion.
10. **Tick budget.** Scorer fan-out (units × ledger types) cannot run every micro-tick. Bound to `microaitaskupdatecycle == 5` boundaries (matching vanilla `MicroAICheckForCharges` skirmisher cycle at 5641) plus dirty-key reuse of vanilla `unitrange` snapshots.

### Header pass continuation (2026-05-08)

Reading `SowMod/xunitdef.h` and `SowMod/xunit.h` (which the SDK plugin code references but I had not opened) recovered concrete constants and helper signatures that tighten earlier inferences. None change the spec's slice ownership rules.

- **Morale tier constants** (`xunitdef.h:17-19`): `MORAL_ROUTED = 0`, `MORAL_BROKEN = 1`, `MORAL_CANTWHEEL = 3`. The Scourge predicate `MorUnitBon() == 0` (used in artillery panic) means "morale tier is `MORAL_ROUTED`." The Whiskey `TacticalSupportScreen` `Shaken` band is the Whiskey analog and still keys on `Regiment.morale < GamePrefs.moraletriggerforfallbackifenemyclose[ai_stance]` rather than a discrete tier.
- **`DISTARTYHOLD = 200`** yards (`xunitdef.h:21`). Scourge's artillery hold-distance constant. GT analog is `GamePrefs.artilleryfallbackenemyclosedist` (3525) plus `aritimetowaitbeforemovingcloser` (51228); Whiskey already uses these. No new constant needed.
- **`EArtyAmmo` slot mapping** (`xunitdef.h:507-514`): `eAACan = 0, eAAShell = 1, eAAShrap = 2, eAASolid = 3`. This **corrects** the earlier guess in this spec that `ammo[2]` is canister: in Scourge, canister is index 0. Vanilla GT at 119134 reads `ammo[2]` for the canister-range gate, so GT's slot mapping is **different** from Scourge's. Whiskey must use GT's index, not Scourge's. The B7 input `Tools.SumUp(ammo) / (float)ammo.Length` for the total ratio remains correct; the per-slot canister flag for GT is `ammo[2]`, not `ammo[0]`. Documented to prevent future translation drift.
- **Helper signatures** (`xunit.h`): `bool CanFight() const` (363); `int MorUnitBon() const` (324); `CXUnit EnemyClose() const` (432) and `int EnemyCloseYds() const` (434); `int FollowType(int val=-1)` (322) and `CXUnit FollowTarg() const` (298); `void GetScoutLoc(CXUnit cav, CXVec &loc, bool bNew) const` (514); `int TACOBJRad2() const` (565), `bool TACOBJDone() const` (566), `CXVec TACOBJLoc() const` (567); `bool HasPlay() const` (595), `void RunPlay(CXOff best, CXUnit targ)` (616), `bool PSTATDone(bool bSet, bool val)` (607); `int HelpTime(bool bSet, int ival)` (598). These are read-only interfaces in the SDK header — Whiskey scorers replicate the *behavior* against GT fields; we do not call these.
- **`EFollow` taxonomy** (`xunitdef.h:655-663`): `eFollowNone / eFollowGuard / eFollowScout / eFollowScreen / eFollowRaid`. Scourge's cavalry doctrine roles. GT does not have a single-field equivalent; Whiskey reconstructs role from `unittyp == 1` plus `permanentlydetached`, `groupaiobject`, and the existing `AssetStrategicRole`. The taxonomy is reference-only — useful for reasoning about cavalry plays but not implemented as a vanilla GT field.
- **`ETactType` taxonomy** (`xunitdef.h:169-179`): `eTacReserve / eTacVP / eTacQuad / eTacQuadVP / eTacQuadLoc / eTacLoc / eTacHold`. Scourge's "what the brigade is currently doing" enum. The transitional state `eTacQuadVP` (sector → specific objective) is the most useful addition: Whiskey's existing role/posture vocabulary lacks the "currently transitioning" intermediate state. Future B6 playbook work may extend `TacticalPlaybookLedger` with this state; not authorized by this spec.

These are inputs to the existing scorers, not new scorers themselves. They do not change ownership boundaries between B6c/B7/B8.

## Source Boundary

Reviewed local Scourge install:

- `/mnt/c/Program Files (x86)/Steam/steamapps/common/Scourge Of War - Remastered/sdk/SowAiInf/`
- `/mnt/c/Program Files (x86)/Steam/steamapps/common/Scourge Of War - Remastered/sdk/SowCampAI/`
- `/tmp/sow-ghidra/SowAiInf.decompiled.c`
- `/tmp/sow-ghidra/SowCampAI.decompiled.c`

Scourge is native x64, not managed Unity. The SDK source is readable design evidence; Ghidra output only verified binary/source shape. Whiskey must not redistribute Scourge files or require Scourge to build/run.

## Grand Tactician Anchor Map

Current decompile: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

| Scourge idea | Scourge evidence | Grand Tactician anchor | Whiskey adaptation |
|---|---|---|---|
| Unit action asks commander stance/orders before local behavior. | `SowAiInf/offcmds.cpp:619` `GetInfCommand`; `SowAiInf/artyai.cpp:186-193` artillery asks leader for fallback distance. | `AIBattle.AdjustGroupAIStance()` at 4221 changes group stance; `AIBattle.MicroAICheckForCharges(Regiment,int)` at 4905 reads stance for charge; `Regiment.lastaistancechangetime` and `AIBattle.PerformAIActionDLCWL(...)` guard ownership. | B6c must keep local reactions subordinate to B6 intent. Charge denial remains a stance/permission decision, not a movement rewrite. |
| Artillery should fall back when enemy is close and no friendly screen is closer to the threat. | `SowAiInf/artyai.cpp:172-224` counts friendly inf/cav support inside `EnemyCloseYds`, gates on `(MorUnitBon == 0 \|\| sup == 0)`. | `AIBattle.CheckArtyFallback(Regiment)` at 3499 uses `unitrange.closestownunitnonrouted` (which does **not** filter unittyp; populated at 122757–122774 for any non-routed `unittyp <= 13 && unittyp != 5`, **including other artillery**); `AIBattle.CheckAIBombardment(Regiment)` at 3869 owns bombardment; `AIBattle.CheckCounterBatteryFire(Regiment)` at 3827 owns counterbattery. | B7 adds a pure `TacticalSupportScreen` scorer that does its own inf/cav scan (vanilla field is unsafe as a screen proxy). Movement remains B8 or vanilla fallback, not B7. |
| Avoid stacking multiple units/guns onto the same destination. | `SowAiInf/offcmds.cpp:183-247` uses 5 yards for gun-on-gun (`CanGunRedeploy`, `CanGunRedeployLine`) and weapon `LongRangeYds()` for line-on-line (`CanRedeployLine`); skips marching skirmishers. | `AIBattle.CheckForSimilarPositions(Vector3,Regiment)` at 8669 uses a single `GamePrefs.distancetoenemytocancelinterruption` threshold via `CheckAIGroupInRange`; `AIBattle.CheckExpandingFrontline(Regiment)` around 4308 uses `unitrange.closestownunitdestination` and `closestenemyontargetdest`; `UnitRange.closestownunitdestination` at 109474 and `closestenemyontargetdest` at 109478 expose destination crowding evidence. | `TacticalDestinationDiscipline` layers Scourge-style tiers (gun ≈ 5 m, line ≈ `firerange`-scaled) on top of the vanilla check; skips skirmishers in motion. B8 and any later reserve movement plan must call this scorer before emitting movement writes. |
| Morale drop plus danger should trigger fallback before rout. | `SowAiInf/unitai.cpp:929-1015` stages rout, retreat, fallback, and skirmisher recovery from morale danger. | `Regiment.morale`, `lastmoraleupdate`, and `battlestartmorale` fields at 111146-111148 and 110756; `Regiment` morale update around 128154-128232; `AIBattle.CheckLineFallbacks(Regiment)` at 5118 writes fallback paths/mode based on morale, enemy proximity, outflanked state, cover, and W&L guard; `AIBattle.MicroAICheckForRetreats(Regiment)` at 4817 writes retreat paths/mode. | B8 adds `TacticalMoralePressure`. Exact morale delta is not a vanilla field; Whiskey must snapshot morale over time if it wants true drop detection. Until then, use current morale, `battlestartmorale`, routed-neighbor, fire/contact, and flank evidence. |
| Units in trouble request help upward; higher commander chooses main effort and reserves. | `SowAiInf/offai.cpp:850-944` sends courier orders and help requests; `SowAiInf/offai.cpp:947-1069` selects best engaged subordinate, runs play, then checks reserves. | `AIBattle.MarchToSoundOfGuns(Regiment)` at 3663 moves idle groups toward engaged groups; `AIBattle.CheckUseOfReserves(Regiment)` at 6062 sends an unengaged unit to support an outflanked unit; `ObjectiveChain.reservegroups` at 2972 and line-group fields at 2992-2996 identify center/left/right/reserve. | B6 adds `TacticalHelpRequest` telemetry and playbook input. Do not synthesize courier orders. Reserve movement remains B6c/B8 gated and default-off. |
| Attacker concentrates while defender screens gaps and preserves a central mass. | `SowCampAI/campai.cpp:768-910` splits detachments differently for attacker/defender and avoids artillery-only detachments. | Strategic/tactical bridge exists through B6 `OperationPosture`, `ObjectiveChain` L/C/R fields, `reservegroups`, and B3 sector evidence. No direct Grand Tactician detachment API is verified for battle-level freeform split/merge in this spec. | Use as doctrine for future playbook role assignment only: attacker main effort/fix sectors; defender screen/refuse/held center. No detachment patch from this spec. |
| Personality shifts retreat tolerance. | `SowAiInf/offai.cpp:275-307` adds officer personality into retreat percentage. | B6 already reads commander profile/initiative; GT retreat/fallback methods do not expose a single personality tolerance knob. | Use commander profile as a pure scoring modifier in B8. Do not patch vanilla retreat thresholds globally. |

## Deep Pass — Additional Scourge → Grand Tactician Translations

A second pass through the Scourge SDK (`offai.cpp`, `offcmds.cpp`, `cavai.cpp`, `unitai.cpp`, `campai.cpp`) and the GT decompile surfaced anchors that the original spec did not exploit. Each row below is a NEW translation candidate; do not interpret them as authorizing runtime writes — slice ownership rules from the top of this document still apply.

| Scourge concept | Scourge evidence | Grand Tactician anchor | Whiskey adaptation |
|---|---|---|---|
| Brigade-level "play" state machine. Division `RunPlay` orchestrates assault / screen / pursue / withdraw via `HasPlay() → PSTATDone()`. | `offai.cpp:979-1070` (play selection), `offai.cpp:1115-1318` (army-play distribution by `StartQuad()`). | `Regiment.macroai` (3294, default `-1`); set from `GameVars.aistrategy` (6380), per-side `bunits.sideinformation[sideofai].macroai` (6385/74689); transition timer `Regiment.timertosetnewmacroai` (3296) using `GamePrefs.standardtimetochangemacroai` modulated by `commander.GetCommanderInitiative()` (6445). Inferred values: `0/1` assault tiers (4240 `strengthtriggerforassault_micro`), `2` defensive/screen check (4248), `3` retreat (6358, 6362 starts `timetostartairetreattimer`), `-1` uninitialized. | `TacticalMacroPlayLedger` reads `macroai` per group as the play state. B6 commander intent maps to one of the four states **as a doctrine input only** — Whiskey must not write `macroai` directly until a separate plan task names the patch surface and proves no conflict with vanilla's `timertosetnewmacroai` cadence. |
| Refuse threatened flank; defender preserves a refused wing. | Scourge `campai.cpp:768-910` defender doctrine; `offcmds.cpp` line redeploy. | `BattleUnits.SetGroupFormation(...)` (91815, 91822) accepts `int refuseflank = -1` and applies `SkewedPosition` with `GamePrefs.refuseflankdepth` / `refuseflankskew` (51692/51694, 92097/92137/92184). | B6c **may** emit a refused-flank intent that surfaces as a `refuseflank` parameter on existing group-formation calls in B8. Default-off; named plan task required because `SetGroupFormation` is a movement-write surface. |
| Quadrant-based threat scoring (rear weighted 4×, flanks 2×). | `cavai.cpp:281-306`, `artyai.cpp:386-410`. | `UnitRange.enemystrengthwithinangle[]` (109510) sliced by `GamePrefs.aidefensiveslices` (49310, 122723); aggregated at 4798-4806 against the unit's facing via `Tools.GetAbsAngleDifference`. | `TacticalQuadrantThreatScorer` reads existing slices (no new scan), reports `Front`/`LeftFlank`/`RightFlank`/`Rear` weighted strengths. Pure scorer; B6c uses for charge/fall-back decisions, B8 for rear-pressure detection. |
| Charge prerequisite scoring with multi-factor weighting. | `cavai.cpp:349-400` (`CanCharge` + `DISTCAV1AUTOMELEE` + `IsSquareType`); `cavai.cpp:60-75` `FearCheck >= SABERBREAK`. | Vanilla idiom at `Regiment` 122229-122230: charge score = `strength * (1 + W0) * (1 + Experience * W1) * (1 + morale * W2) * (1 + (1-fatigue) * W3)` with `GamePrefs.weightingfactorsformicroaicharge[0..3]`; gates: `GamePrefs.maxchargeradius` (122257), `GamePrefs.microaitriggerforcharge` (122259), `GamePrefs.maxenemymoraleforcavalrychargenonarty = 0.7f` (49128, used at 5050, 122298), `lastaichargetime + GamePrefs.timetorenewaichargecheck` (4917). | `TacticalChargeViability` enriches the existing #41 charge gate with a `Refuse / Allow / Encourage` triple. Inputs are pure reads; the gate itself remains the existing patched candidate-list filter. No new movement writes from this scorer. |
| Engagement distance threshold (Scourge `STANCE_DANGER_DISTANCE = 150 yds`). | `offai.cpp:37`, `offai.cpp:144-156`. | `GamePrefs.aidefensivemaxrange` used at `Regiment` 122719 to bound the angle-slice scan; `UnitRange.closestenemyunitfardistance` (109500); `GamePrefs.artilleryfallbackenemyclosedist` (3525) for artillery panic radius. | All Whiskey scorers use these vanilla prefs as the danger thresholds. **Do not introduce new danger-distance constants** — track vanilla so future patches re-tune cleanly. |
| Fatigue-modulated decisions (Scourge cohesion / recovery loops). | `unitai.cpp:929-1015` morale staging; charge math uses fatigue. | `Regiment.fatigue` (111494), `Regiment.groupfatigue` (110884), `GamePrefs.terrainfatigue[]` (51370), `moraleeffectonhighfatigue` (51942), `meleeinfluence_fatigue` (52132), `conditionofmeninfluenceonfatigue` (52350). | Add fatigue as an input to `TacticalMoralePressure` (degraded morale tolerance under high fatigue) and `TacticalChargeViability` (vanilla weighting already uses `(1 - fatigue)`). No fatigue patch; pure scoring. |
| Volley discipline / wait-after-firing before re-position. | `unitai.cpp:1516-1578` `eComVolleyOff` and last-volley-before-charge. | `Regiment.lastfiredshottime` (110794, set at 119063, 132345), `GamePrefs.aritimetowaitbeforemovingcloser` (51228, used at 3789), `GamePrefs.distancefactorfiringlastvolleybeforecharge` (122277, last-volley-then-charge sequence). | GT lacks a discrete hold-fire command, but the post-firing dwell timer is a real anchor. `TacticalChargeViability` and any future repositioning scorer must respect `lastfiredshottime + aritimetowaitbeforemovingcloser` to avoid yanking a unit mid-volley. Read-only. |
| Outflanked tier scoring (Scourge quadrant pressure). | `cavai.cpp:281-306` weighted threat per quad. | `Regiment.outflanked` is set across tiers 0-7 at 112813, 122976, 123151-123175. Vanilla checks `outflanked > 0` (4515) but does **not** consume the higher tiers in retreat math. | `TacticalMoralePressure` uses tier 1-2 → `UnderPressure`, 3-5 → `FallbackCandidate`, 6-7 → `WithdrawalCandidate`. Strict read-only mapping; spec's earlier int-vs-bool correction is now operationalized. |
| Ammo type cycling (canister → solid → shell → shrapnel). | `artyai.cpp:700-716` cycles by range and leader override. | `Regiment.ammo` (111498, `float[]` length 3 at 86815: `(ammo[0] + ammo[1] + ammo[2]) / 3f`); init `ammo[0] = 1f` at 112874; `GamePrefs.ammoalertlevel` (21031), `messagelowariammotrigger` (117486), and canister-range gate at 119134 (`ammo[2] > 0f` is canister). | B7 ammo input uses `Tools.SumUp(ammo) / (float)ammo.Length` for total ratio and `ammo[2]` as the canister-presence flag for close-range counterbattery decisions. No write. |
| Reserve commissioning with command-cadence timer. | `offai.cpp:1068-1070` `CheckReserves` after `PSTATDone`. | `Regiment.timertosetnewmacroai` (3296) tied to `standardtimetochangemacroai * (1 - initiative * influencecommanderinitiativeonshorterstancechanges)` (6445). Vanilla already excludes reserves with `ai_stance == 2` (6672). | `TacticalReservePolicyLedger` (existing) reads `timertosetnewmacroai` to schedule reserve checks at vanilla cadence rather than fixed Whiskey ticks. Inherit the `ai_stance == 2` exclusion. No new writer. |
| Help-request timer to throttle escalation. | `offai.cpp:929-944` `HelpTime` 30 minutes. | No direct vanilla anchor; `MarchToSoundOfGuns` (3663) and `CheckUseOfReserves` (6062) fire opportunistically. | `TacticalHelpRequest` writes a Whiskey-side cooldown stamp (in-memory ledger only, see Snapshot Ledger) so B6 doesn't spam reserve calls each `microaitaskupdatecycle`. Default cooldown: `GamePrefs.standardtimetochangemacroai * 0.5f`. |

## New Whiskey Models

### TacticalSupportScreen

Purpose: tell B7/B8 whether a vulnerable unit is covered by a friendly screen using Scourge-quality criteria, not vanilla's unfiltered closest-friend field.

Confirmed GT inputs:

- vulnerable unit `unittyp`, `guns`, `morale`, `isrouted`, `markedforrout`, `regimentpaths`, `pathinterrupted`;
- closest enemy via `Regiment.GetClosestEnemyUnit(...)` (123760/123773) and `UnitRange.closestenemyunitfarreg` plus `closestenemyunitfardistance`;
- enemy fire-range list via `UnitRange.enemyinfirerangereg` for artillery duels where `closestenemyunitfarreg` may be screened by another inf/cav target;
- friendly scan: iterate the unit's nearby friendlies using existing `unitrange.temp_owninrangeregs` evidence (populated alongside `closestownunitnonrouted` near 122744–122797) **filtered to `unittyp == 0 || unittyp == 1`**, excluding `isrouted`, `markedforrout`, `permanentlydetached`. **Do not use `closestownunitnonrouted` directly**: it admits other artillery and is single-element.
- distances via `Tools.GetXZDistance(...)` (151160);
- panic radius reference: `GamePrefs.artilleryfallbackenemyclosedist` (used by `CheckArtyFallback` at 3525) for the "enemy close" tier;
- crew-morale gate: `Regiment.morale` compared to `GamePrefs.moraletriggerforfallbackifenemyclose[ai_stance]` (used by `MicroAICheckForRetreats` at 4515 and `CheckLineFallbacks` at 5047).

Output (tri-state plus unknown):

- `Screened`: at least one inf/cav screen unit (`unittyp <= 1`, not routed/marked, within `artilleryfallbackenemyclosedist` of the protected unit) AND `morale >= moraletriggerforfallbackifenemyclose[stance]`. Equivalent to Scourge `sup > 0 && MorUnitBon > 0`.
- `Shaken`: a screen exists but `morale < moraletriggerforfallbackifenemyclose[stance]`. Vanilla fallback would already trip; B7 should not double-fire.
- `Unsupported`: enemy is inside `artilleryfallbackenemyclosedist` AND no qualifying screen exists.
- `Unknown`: required field access fails (reflection log + benign default) or `battlestartmorale < 0` indicating uninitialized state.

B7 may use `Shaken` and `Unsupported` to cancel or preserve bombardment. B7 must not call `RegimentSetPath(...)`, `SetMovementMode(...)`, or either `SetWithdrawal(...)` overload (92821 static, 116116 instance); B8 or vanilla `CheckArtyFallback` owns artillery movement.

### TacticalDestinationDiscipline

Purpose: prevent Whiskey movement slices from creating stacking, backtracking, or same-destination crowding. Layers Scourge's unittyp-aware tiering on top of vanilla's single-threshold deconflictor.

Confirmed GT inputs:

- `AIBattle.CheckForSimilarPositions(Vector3,Regiment)` at 8669, threshold = `GamePrefs.distancetoenemytocancelinterruption`;
- `UnitRange.closestownunitdestination` (109474) and `closestownunitdestination_temp` (109476);
- `UnitRange.closestenemyontargetdest` (109478) and `closestenemyontargetdest_temp` (109480);
- `Regiment.GetLastTransmittedPathPos(ignoreorderdelay:true)` (127552);
- `Regiment.width`, `depth`, `firerange`, `lastsetwaypointposition` (111096), `lastsetwaypointrotation` (111098), `lastsetwaypointpositionsafetyzone` (111100);
- `Regiment.regimentpaths` (111068) and `pathinterrupted` for in-motion gating; skirmisher skip when `unittyp == 3` and `regimentpaths > 0`;
- `BUG-TAC-010` path-risk boundary from active tactical plans.

Tiered crowding thresholds (mirroring Scourge):

| Mover unittyp | Same-tile peer unittyp | Threshold | Source |
|---|---|---|---|
| 2 (artillery) | 2 (artillery) | 5 m (≈ 5 yd) | `CanGunRedeploy`, `CanGunRedeployLine` |
| 0 / 1 (line) | 0 / 1 (line) | `max(width, mover.firerange * 0.5f)` clamped to `[GamePrefs.distancetoenemytocancelinterruption, 2 * firerange]` | `CanRedeployLine` (LongRangeYds analog) |
| Any | 3 (skirmisher) when `regimentpaths > 0` | skip | `CanRedeployLine` skirmisher exemption |
| Any | 5 / >13 (excluded) | skip | matches vanilla unittyp gates |

Output:

- `ClearDestination`;
- `CrowdedSameType` (peer at same unittyp inside the tiered threshold);
- `CrowdedAdjacent` (peer at a different combat unittyp inside the wider line threshold);
- `EnemyOnDestination` (`closestenemyontargetdest` within mover `firerange`);
- `PathRiskUnknown` (reflection failure or `lastsetwaypointposition` zero before any waypoint set).

Any B8 movement branch or later reserve-relief movement branch must run this scorer before writing `RegimentSetPath(...)` (130791), `BattleUnits.SetWaypoint(Regiment,...)` (91232) or `SetWaypoint(GameObject,...)` (91225), either `SetWithdrawal(...)` overload, or direct `SetMovementMode(...)` (124704).

### TacticalMoralePressure

Purpose: stage fallback/withdrawal from accumulated morale danger instead of snapping to retreat.

Confirmed GT inputs:

- current `Regiment.morale` (111146) and `battlestartmorale` (110758, default `-1f` so guard `>= 0f`);
- `Regiment.lastmoraleupdate` (111148, timestamp — not prior morale);
- `Regiment.friendlyroutednear` (111158) and `enemyroutednear` (111160);
- `Regiment.outflanked` (111488, **`int`, not bool** — vanilla checks `outflanked > 0` and weighs higher tiers as more flanked) and `ownonflank` (111490);
- `Regiment.covervalue` (111404) and `coverobject` (111408);
- `UnitRange.closestenemyunitfarreg` (109496), `closestenemyunitfardistance` (109500), `retreatangle` (109518);
- `UnitRange.enemyinfirerangereg` (109456) for artillery duels (`MicroAICheckForRetreats` at 4515 explicitly skips when `closestenemyunitfarreg.unittyp == 2`, so pure artillery pressure must be detected via `enemyinfirerangereg` filtered to `unittyp == 2`);
- received-fire evidence via `Regiment.ReceivedFireFromUnit(Regiment)` (121507) and `Regiment.CheckReceivedFireOtherUnit(Regiment, float maxdistance = 7f)` (121482) — **methods, not the raw `receivedfire` list (111858)**;
- vanilla side-effect anchors only: `CheckLineFallbacks(...)` (5118), `MicroAICheckForRetreats(...)` (4817). Whiskey reads outcomes; never patches their writes.

Output (use as ordered ladder; pick the highest matched):

- `Stable`: `morale >= battlestartmorale - 0.10f` AND `outflanked == 0` AND `friendlyroutednear == 0`;
- `UnderPressure`: morale dropped 10–20 % from `battlestartmorale`, OR `outflanked >= 1`, OR `friendlyroutednear > 0`;
- `FallbackCandidate`: `morale < GamePrefs.moraletriggerforfallbackifenemyclose[ai_stance] * 1.2f` AND received-fire from `closestenemyunitfarreg` (the same predicate vanilla uses at 5047);
- `WithdrawalCandidate`: `FallbackCandidate` plus `outflanked > 0` with no usable cover (`covervalue <= 0f` or `coverobject == 3`);
- `CollapseCandidate`: `morale < GamePrefs.moraletriggerforfallbackifenemyclose[ai_stance]` matches the vanilla retreat predicate at 4515; vanilla will already act, so B8 must defer.

B8 owns all runtime writes from this model. The scorer must short-circuit when `battlestartmorale < 0f` (uninitialized) or when the W&L gate fails (see Cross-Cutting Gates).

### TacticalMoraleSnapshotLedger

Purpose: provide the prior-morale comparator that vanilla does not expose.

Identity key: `Regiment.GetInstanceID()` plus a stable secondary key derived from `Regiment.name` to survive any object replacement during W&L attach/detach. Whiskey stores both; on lookup, prefer the InstanceID and fall back to name match within the same alliance.

Retention:

- one entry per qualifying regiment (`unittyp <= 13 && unittyp != 5`, not `permanentlydetached`);
- ring buffer of the last N samples where N is bounded (default 4) to keep memory flat;
- timestamps from `GameVars.currenttimefromstart`;
- pruning rule: drop entry on `isrouted == true` for `> GamePrefs.standardretreatdistance` worth of game time, or when the regiment is no longer present in the active battle scan.

Sampling cadence: integrated with `microaitaskupdatecycle == 5` boundary (see Tick Budget). Avoid polling when `lastmoraleupdate == previous sample's timestamp` — vanilla has not changed the value, so no new sample is needed.

Persistence: in-memory only. Snapshot ledger is not written to the JSON sidecar; battles do not survive save/reload mid-fight in vanilla, and reconstructing prior-morale from stale data would mislead scorers worse than absence would.

### TacticalHelpRequest

Purpose: capture "this sector needs help" without creating courier/order writes.

Confirmed GT inputs:

- `AIBattle.CheckUseOfReserves(...)` (6062) outflanked-unit support logic, including the existing exclusion at 6672 of reserves with `ai_stance == 2` (cover/standfast) — Whiskey must inherit this exclusion for any future reserve-relief writer;
- `AIBattle.MarchToSoundOfGuns(...)` (3663) engaged-group help logic;
- `ObjectiveChain.reservegroups` (2972), `linegroup_centerunit` (2992), `linegroup_leftunits` (2994), `linegroup_rightunits` (2996);
- B3 contact/sector odds and B6 playbook role.

Output:

- `RequestReserveScreen`;
- `RequestLineRelief`;
- `RequestArtillerySupport`;
- `RequestMainEffortShift`;
- `NoRequest`.

Telemetry sink: `TacticalSectorLedger` gains a per-sector `HelpRequest` field consumed by `TacticalPlaybookLedger` and `TacticalCommandLedger`. No new sink type is created. Any reserve movement implementation remains default-off and must preserve W&L ownership gates.

### TacticalQuadrantThreatScorer

Purpose: convert the existing `enemystrengthwithinangle[]` slice array into a labelled four-direction view so B6/B6c/B8 can talk about "rear pressure" or "left flank screen" without each consumer re-implementing the angle math.

Confirmed GT inputs:

- `UnitRange.enemystrengthwithinangle` (109510) and `_temp` (109512), filled at `Regiment` 122723–122732 against `GamePrefs.aidefensiveslices` buckets;
- unit facing via `Tools.GetAngle(...)` and `lastsetwaypointrotation` (111098);
- aggregation pattern at `AIBattle` 4798–4806 using `Tools.GetAbsAngleDifference(isfacing, sliceCenterAngle) <= anglerange`.

Output:

- `FrontStrength`, `LeftFlankStrength`, `RightFlankStrength`, `RearStrength` — raw `float` strength sums in the four 90° arcs around the unit's facing;
- `DominantDirection` — the arc with the highest strength;
- `RearPressureFlag` — true when `RearStrength > Front + max(Left, Right)`, mirroring Scourge's 4× rear weighting in qualitative form.

Pure scorer; no writes. Reuse vanilla's filled `enemystrengthwithinangle` rather than re-scanning.

### TacticalChargeViability

Purpose: enrich the existing #41 charge gate with a doctrine-aware tri-state instead of binary allow/deny.

Confirmed GT inputs:

- charge weighting math at `Regiment` 122229–122230 using `GamePrefs.weightingfactorsformicroaicharge[0..3]`;
- distance gate `GamePrefs.maxchargeradius` (122257);
- score threshold `GamePrefs.microaitriggerforcharge` (122259);
- target-morale gate `GamePrefs.maxenemymoraleforcavalrychargenonarty = 0.7f` (49128, 5050, 122298);
- charge cooldown `Regiment.lastaichargetime` (110798) plus `GamePrefs.timetorenewaichargecheck` (4917);
- volley dwell `Regiment.lastfiredshottime` (110794) plus `GamePrefs.aritimetowaitbeforemovingcloser` (51228);
- target/observer `outflanked` tier (0-7) — high tier on the target encourages the charge.

Output:

- `Refuse` — vanilla weighting score below `microaitriggerforcharge`, OR target morale ≥ `maxenemymoraleforcavalrychargenonarty` and target is non-artillery, OR cooldown not elapsed;
- `Allow` — vanilla score meets the threshold but not by a wide margin;
- `Encourage` — vanilla score exceeds threshold by ≥ 25 % AND target `outflanked >= 3` or target morale below half the gate.

B6c may apply `Refuse` as a charge-denial input to the existing #41 candidate filter. B6c MUST NOT bypass `Refuse` to force a charge; doctrine guides denial, not creation.

### TacticalRefuseFlankIntent

Purpose: capture defender refused-flank doctrine as an intent that can be applied through the existing `BattleUnits.SetGroupFormation(...)` `refuseflank` parameter without inventing a new formation surface.

Confirmed GT inputs:

- `BattleUnits.SetGroupFormation(GameObject, ...)` (91815) and `(Regiment, ...)` (91822) accept `int refuseflank = -1` (no refuse) and apply `SkewedPosition` at 92097/92137/92184;
- `GamePrefs.refuseflankdepth` (51692), `GamePrefs.refuseflankskew` (51694);
- B3 sector evidence and `TacticalQuadrantThreatScorer` rear-pressure flag.

Output:

- `NoRefuse` (default, `refuseflank = -1`);
- `RefuseLeft` (`refuseflank = 0` or whichever index vanilla maps to left — pin via empirical smoke; spec assumes 0/1 = left/right and confirms before write);
- `RefuseRight`.

B6c may translate this intent into the next `SetGroupFormation` call B8 emits as part of staged withdrawal. **No standalone formation patch from this spec.** Default-off; one focused smoke required to verify which integer maps to which flank before defaults change.

### TacticalFatigueState

Purpose: surface fatigue as a first-class doctrine input so morale and charge scorers can degrade gracefully under cumulative exhaustion.

Confirmed GT inputs:

- `Regiment.fatigue` (111494), `Regiment.groupfatigue` (110884);
- `GamePrefs.terrainfatigue[]` (51370), `moraleeffectonhighfatigue` (51942), `meleeinfluence_fatigue` (52132), `conditionofmeninfluenceonfatigue` (52350).

Output:

- `Fresh` (`fatigue < 0.25f`);
- `Tiring` (`0.25f <= fatigue < 0.55f`);
- `Spent` (`0.55f <= fatigue < 0.80f`);
- `Exhausted` (`fatigue >= 0.80f`).

`TacticalMoralePressure` lifts its `UnderPressure` band by one when state is `Spent`, two when `Exhausted`. `TacticalChargeViability` collapses `Encourage` → `Allow` when `Spent`, `Allow` → `Refuse` when `Exhausted`.

## Concepts Not Translatable

The deep pass also identified Scourge ideas that have no clean Grand Tactician anchor or that would require new vanilla patches outside the current slice scope. Recording them here so future plans do not re-litigate.

- **Square formation versus cavalry.** Scourge `unitai.cpp:676-740` triggers square via cavalry proximity and `SQUARE_MEN_MINIMAL`. Civil War rifled muskets retired the square; GT decompile has no `square` formation, no `FormSquare` API, and no `IsSquareType` analog (`rg "square|FormSquare"` returns only terrain-data hits). Do not attempt to add square mechanics.
- **Limber state machine for artillery.** Scourge `artyai.cpp` toggles `eLimUnLimbered` / `eLimUnLimbering` / `eLimLimbered`. GT abstracts limber state inside its movement modes (mounted/unmounted artillery uses `mounted == 1` plus `regimentpaths`). Mapping limber explicitly would require patching internal state machines in `Regiment` — out of scope.
- **Volley off / discrete hold-fire command.** Scourge `unitai.cpp` issues `eComVolleyOff` to silence a regiment. GT has no hold-fire setter; the closest anchor is the post-fire dwell timer (`lastfiredshottime + aritimetowaitbeforemovingcloser`). The dwell anchor is reusable; an explicit hold-fire surface is not.
- **Courier object delivery with travel time.** Scourge `offai.cpp:1359-1538` simulates couriers as physical objects with travel and `DIST2COURARRIVE`. GT applies `aiorders` instantly within a microai cycle; courier travel is not modelled. Whiskey can simulate a delay via cooldown timestamps but cannot inject a literal courier entity.
- **Per-sub `lead.Sub(i)` iteration with role tags.** Scourge brigades expose explicit role tags on subs. GT's `Regiment` array `allattachedunits[]` is uniform; role information must be reconstructed from `unittyp`, `groupaiobject`, `permanentlydetached`, and `ObjectiveChain` line/reserve fields. The reconstruction is doable as a Whiskey-side classification; the underlying tagging is not native.
- **Cavalry raid target filtering for isolated infantry.** Scourge `cavai.cpp:570-656` filters via `HasInfOrArtFriends` proximity. GT has no equivalent helper exposed publicly; the closest reuse is iterating `unitrange.temp_owninrangeregs` filtered to inf/art (mirrors the support-screen pattern). Useful as a future scorer; no anchor exists today for a one-call API.
- **Frontal-vs-side artillery target priority by `CLOSESUPPORT = 250 yds`.** Scourge `artyai.cpp:111-137` prefers frontal targets within 250y. GT's counterbattery loop at 3840-3849 iterates `enemyinfirerangereg` without quadrant priority. Adding quadrant priority would patch `CheckCounterBatteryFire` directly — out of B7's "no new movement writes" boundary; defer.

## Tick Budget and Cadence

Tactical scorers fan out as `units * scorers`. Running every micro-tick will dwarf vanilla cost and flood `LogOutput.log`.

- All scorers gate on `microaitaskupdatecycle == 5` (the same skirmisher/charge cadence vanilla uses at 5641). One full sweep per cycle, not per tick.
- Per-regiment results are cached on a Whiskey-side `TacticalScoreCache` and invalidated when any of `lastsetwaypointposition`, `morale` (snapshot delta beyond a small epsilon), `ai_stance`, `outflanked`, or `friendlyroutednear/enemyroutednear` change. Unchanged inputs reuse the cached output.
- Reflection failures degrade to `Unknown` once and log via `OnceLog`; the second and subsequent failures of the same key are silent for the rest of the battle.
- Snapshot ledger sampling skips when `lastmoraleupdate == prior_sample.lastmoraleupdate`.

## Cross-Cutting Gates

Every scorer or scorer caller must apply these gates before producing scores or accepting a model output as authoritative input:

- **Engagement gate.** Scorers run only while a tactical battle is active; outside battle, return `Unknown`/`NoRequest`. Existing tactical-context plumbing already enforces this; the gate is reasserted here for clarity.
- **W&L ownership gate.** `PerformAIActionDLCWL(Regiment, Regiment)` is `private static` on `AIBattle` (5101). Whiskey scorers MUST NOT call it directly. Replicate the public predicate `(aigroup.ai_feudstance == -1) | (isplayeraiorfeud == 2)` (used at 3490, 3789, 3834, 3890, 4080, 4515, 4842, 4917, 4922, 5137, 5249, 5307, 5360, 5400) and treat any failed gate as `Unknown` for that regiment. If exact parity with vanilla is ever required, reflect into the static method via `AccessTools.Method(...)` wrapped in try/catch + `OnceLog`.
- **Alliance bounds gate.** Per-alliance arrays must be bound-checked against `aifaction.Length`. AGENTS.md flags `allianceId == 2` (Europe) as a real value `AICampaignReflect.GetAllianceId(...)` can return; tactical scorers that key on alliance index inherit this trap.
- **Player-subordinate gate.** Treat any unit whose ai_feudstance / DLCWL gate fails as read-only — never feed its scorer output into a movement-write decision in B6c/B8 even when scorers nominally allow.
- **Stance value semantics (pinned).** Decompile evidence supports the following `ai_stance` mapping; new code must use named constants rather than raw integers:
  - `0` advance / approach march;
  - `1` defensive screen (reserves and cavalry charge are gated against this — 4080, 5047, 5060, 6672);
  - `2` defensive in cover / standfast (move-mode lock at 124775; reserves with this stance are excluded from relief at 6672);
  - `3` withdraw / skirmisher chase guard (covervalue exit guard at 128873);
  - `4` emergency / forced action (skirmisher reattach forcing at 5363).
- **Unit type semantics (pinned).** `unittyp` values used by scorers: `0` infantry, `1` cavalry (mounted/dismounted via `mounted` field), `2` artillery, `3` skirmisher detachment (parented via `parentregiment`), `4` officer/general or skirmisher container, `5` excluded from all combat scans, `> 13` formation/group (not regiment).

## Slice Integration

### B6c

Use Scourge only to reinforce B6's existing rule: local action is subordinate to commander intent.

Required B6c spec/plan updates before implementation:

- classify local reactions against B6 playbook role and `TacticalHelpRequest`;
- consume `TacticalMacroPlayLedger` (read-only mapping of `Regiment.macroai`) when interpreting commander intent — assault states `0`/`1` accept aggressive local reactions, screen state `2` rejects them, retreat state `3` collapses local reactions to fallback only;
- consume `TacticalChargeViability` `Refuse / Allow / Encourage` when evaluating charge permission; `Refuse` overrides any local impulse via the existing #41 candidate filter;
- consume `TacticalQuadrantThreatScorer` and `TacticalRefuseFlankIntent` when surfacing playbook role — refused-flank intent is doctrine input only at this layer, not a write;
- treat `TacticalDestinationDiscipline` as a blocker for any reserve or local movement branch;
- keep charge permission on the existing B1/#41 charge gate surface;
- do not synthesize courier orders or call movement APIs from B6c unless a named plan task quotes the exact GT surface and rollback.

### B7

Extend B7 artillery doctrine inputs:

- add `TacticalSupportScreen` result (Screened / Shaken / Unsupported / Unknown);
- add enemy artillery visibility by filtering `UnitRange.enemyinfirerangereg` (109456) to `unittyp == 2` and not routed (matching the vanilla counter-battery gate at 3846);
- add current bombardment/counterbattery state from `Regiment.combatbehaviorordered` (111262); vanilla idioms: `8` = bombarding (set/cleared at 3861, 3890, 3902), `9` = counter-battery (3849, 3894, 3902, 3933);
- add ammo ratio using `Tools.SumUp(ammo) / (float)ammo.Length` rather than `/ 3f`. Both idioms appear in vanilla (see 3893 vs 117486); the slot-count form is correct if the array length ever differs from 3 and matches the message-trigger form vanilla uses for the low-ammo UI;
- add stance gate: skip when `ai_stanceordered == 1` (screen) and the gun is reassigned during stance change (see `lastaistancechangetime` at 4264-4275);
- add W&L ownership-safety gate (replicated, not called — see Cross-Cutting Gates).

B7 decisions remain:

- `PreserveFire`;
- `SuppressStrongpoint`;
- `CounterBattery`;
- `CancelBombard`;
- `DefensiveFallback` telemetry only.

Important correction: Grand Tactician already has `CheckArtyFallback(...)`. B7 should not duplicate it. B7 may only influence bombard/counterbattery/cancel decisions at `CheckAIBombardment(...)` / `CheckCounterBatteryFire(...)`; artillery movement belongs to vanilla or B8.

### B8

Extend B8 staged withdrawal doctrine:

- derive fallback pressure from `TacticalMoralePressure` (now consuming `TacticalFatigueState` and `outflanked` tier evidence);
- use `TacticalSupportScreen` to distinguish covered withdrawal from exposed flight;
- use `TacticalDestinationDiscipline` before any selected withdrawal or waypoint branch;
- use `TacticalQuadrantThreatScorer.RearPressureFlag` to pick withdrawal direction along the lowest-strength arc;
- optionally apply `TacticalRefuseFlankIntent` when emitting `SetGroupFormation(...)` as part of staged withdrawal — default-off behind a config toggle separate from existing B8 surfaces;
- use commander profile as a tolerance modifier, not as a global vanilla threshold patch.

B8 must keep the existing plan boundary: no artillery APIs, no reserve-list mutation, no full-retreat timer except `FullRetreat`.

### Later Strategic/Tactical Bridge

Use Scourge campaign detachment only as high-level doctrine:

- attacker: main effort plus fix/screen sectors;
- defender: screen gaps, refuse threatened flank, preserve central mass;
- artillery-only formations should not be detached as screens.

No detachment or split/merge runtime patch is specified here.

## Non-Goals

This spec does not:

- port Scourge code or data;
- create any dependency on Scourge files;
- patch `RegimentSetPath(...)`, `BattleUnits.SetWaypoint(...)`, or order queues directly;
- bypass `PerformAIActionDLCWL(...)` or player-subordinate ownership gates;
- make B7/B8 default-on;
- add Napoleonic square, limber-state, or Scourge-only mechanics that GT does not expose cleanly;
- change vanilla morale thresholds globally;
- authorize reserve-list mutation beyond existing B6c/B8 plan gates.

## Verification Expectations

Before implementation planning, re-run anchor checks:

```bash
rg -n "private unsafe void CheckArtyFallback\\(|private void CheckCounterBatteryFire\\(|private void CheckAIBombardment\\(|private unsafe void CheckLineFallbacks\\(|private unsafe void MicroAICheckForRetreats\\(|private unsafe void MarchToSoundOfGuns\\(|private unsafe void CheckUseOfReserves\\(|public unsafe Vector3 CheckForSimilarPositions\\(|private static bool PerformAIActionDLCWL\\(|private void AdjustGroupAIStance\\(|private void MicroAICheckForCharges\\(" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
rg -n "public class UnitRange|closestownunitdestination|closestenemyontargetdest|closestenemyunitfardistance|closestownunitnonrouted|enemyinfirerangereg|retreatangle|public float morale|lastmoraleupdate|battlestartmorale|public int outflanked|public int ownonflank|public float covervalue|public int coverobject|public int combatbehaviorordered|public int ai_stance|public int ai_feudstance|public float lastaistancechangetime|public int regimentpaths|public Vector3 lastsetwaypointposition|public float lastsetwaypointrotation|friendlyroutednear|enemyroutednear" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
rg -n "public bool ReceivedFireFromUnit\\(|public bool CheckReceivedFireOtherUnit\\(|public Vector3 GetLastTransmittedPathPos\\(|public.*RegimentSetPath\\(|public.*SetWaypoint\\(|public.*SetWithdrawal\\(|public void SetMovementMode\\(" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
rg -n "public int macroai\\b|public float timertosetnewmacroai|public static float standardtimetochangemacroai|public.*aistrategy\\b|GamePrefs.strengthtriggerforassault_micro|GamePrefs.strengthtriggerforscreening_micro|GamePrefs.weightingfactorsformicroaicharge|GamePrefs.maxchargeradius|GamePrefs.microaitriggerforcharge|GamePrefs.maxenemymoraleforcavalrychargenonarty|GamePrefs.timetorenewaichargecheck|GamePrefs.aritimetowaitbeforemovingcloser|GamePrefs.distancefactorfiringlastvolleybeforecharge|GamePrefs.aidefensivemaxrange|GamePrefs.aidefensiveslices|GamePrefs.refuseflankdepth|GamePrefs.refuseflankskew|GamePrefs.terrainfatigue|GamePrefs.moraleeffectonhighfatigue|GamePrefs.meleeinfluence_fatigue|GamePrefs.conditionofmeninfluenceonfatigue|public float fatigue\\b|public float groupfatigue\\b|public float lastfiredshottime\\b|public float lastaichargetime\\b|public Vector3\\[\\] flankposition|public float\\[\\] enemystrengthwithinangle|public void SetGroupFormation\\(|public void SetFormation\\(" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Pure harness coverage expected when these concepts are implemented:

- support screen detects friendly cover between guns and close enemy;
- unsupported guns cancel/preserve bombardment but do not move in B7;
- destination discipline rejects crowded/enemy-held destinations;
- morale pressure distinguishes fallback candidate from collapse candidate;
- help request is emitted as telemetry without movement writes;
- W&L player-subordinate safety blocks all writes.

Runtime smoke remains the per-slice gate from B6c/B7/B8 plans: default-off config, bounded telemetry, no repeated exceptions, no player-subordinate retasking, and deployed DLL hash match before user smoke.

## Not Verified

- Exact prior-morale storage in vanilla was not found. `lastmoraleupdate` is a timestamp; the `TacticalMoraleSnapshotLedger` above is the explicit replacement.
- Strongpoint detection quality is still owned by B7 and requires a runtime check against GT terrain/fort/cover fields before runtime writes.
- Safe conversion of vanilla reserve help into order-delay-preserving movement is not verified here. Treat reserve movement as a separate default-off plan task; respect the `ai_stance == 2` reserve exclusion at 6672.
- No Grand Tactician equivalent for Scourge courier order synthesis was verified. Do not create courier-like orders from this spec.
- `Regiment.GetInstanceID()` stability across W&L attach/detach is not verified. The snapshot ledger uses InstanceID + name as a defensive double-key; if a future smoke test shows InstanceID rolls during attach/detach, drop to name-key only with a one-line rationale in the change log.
- The Whiskey replication of the W&L ownership gate (`ai_feudstance == -1` / `isplayeraiorfeud == 2`) matches the public predicate vanilla uses at the listed call sites. It is not byte-identical to `PerformAIActionDLCWL(...)`; if future vanilla updates change the private method's body, the replicated predicate will drift. Re-verify on game updates per the AGENTS.md decompile workflow.
- `firerange`-scaled line crowding threshold (`max(width, mover.firerange * 0.5f)`) is a Scourge-inspired heuristic, not a direct vanilla constant. Treat as tunable; fall back to `GamePrefs.distancetoenemytocancelinterruption` if a per-unit firerange read fails.
