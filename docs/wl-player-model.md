# W&L Player Model (vanilla reference)

How the player works in a Whiskey & Lemons career, from the vanilla decompile (`/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`, build 14703111, mapped 2026-10-14). All line numbers refer to that file. Living doc: update when the game patches or when a new vanilla path is mapped. **Any Whiskey feature that touches campaign or battle AI must be checked against this model** — in W&L the player is usually a subordinate and their own side is run by AI.

## Core facts

- A career is `DLC_WL.dlc_scenarioactive`; the player is `GameVars.commander[DLC_WL.dlc_chosencommander]`; their unit is `.currentcommand` (null before start selection or after losing the unit).
- **W&L forces `GameVars.ai_vs_ai = true`** (campaign 29787-29793; battle 23755-23768). Consequences: AICampaign builds an AIFaction for the player's own alliance (11359); `aibattle[1]` (player side, `isplayeraiorfeud = 1`) runs the full macro AI (`UpdateAITasks` 5857); policies are always AI-run (`CheckAIPolicyChange` 211019).
- **Player-owned units** = `Regiment.dlcw_isundercommander`, cached from `BattleUnits.IsUnitUnderCommander` (91168-91222): the player's command or a transform-descendant of it; units whose detachment parent is under the player; battle take-overs (`IsUnitTakenOver` 45840); every unit of the alliance if the player is CIC; units of the player's army group. Refreshed in the Regiment slow tick (112240), init, load, `UpdateUnitUnderCommanderAll` (91157).
- **Echelon** = `DLC_WL.GetPlayerCommandHierarchy()` (44397): −1 no command; else `max(10, overridesymbol + commandhierarchyshift)`; 18 for army-group commander or CIC. Level texts (via `CheckHierarchyChange` 47692): 10-13 unit moves with its parent HQ (player advises with prestige); 14 can manage/recruit but moves with higher HQ; 15 movable on the map; 16 with movable independent sub-HQs; 18 several HQs; CIC = all armies and fleets + weapon procurement.
- **The player cannot die or be captured**: any KIA/POW roll becomes wounded + injury (126777-126801). The player is exempt from AI replacement, promotion and the best-commander pool (80022, 80030, 80091, 17886, 84449).

## Career lifecycle (campaign)

- **Start:** CampaignController frame 50 → `ShowStartUnitSelectionList` (182353) offering combat units and division HQs within starting prestige (`GetUnitListForStartSelection` 44092) → `AppointCommand(unit, resetrank:true)`.
- **`AppointCommand`** (44213-44271): prestige cost `GetCommandPrestigeCost` (44034); relation hit with the displaced commander (cause 0); old unit to an AI commander; old `dlcw_isundercommander = false`, new `= true`.
- **Changing command:** apply for a command (`CanTakeoverCommand` 43860, prestige); request transfer to the next-higher HQ (`CanRequestTransfer` 43911, unittyp exactly one level up, own < 16); command offers (`OfferNewCommand` 48015 — exactly one level up, free on accept; reason 0 newly raised units, reason 1 an AI commander who fell is offered to the player first).
- **Promotion:** `rankprogression` from attribute/fame/exp gains (`UpdateRankProgression` 44981); promoted at 1.0 only if `IsPromotable() == 0` (47745: 2 = senior's rank ≤ player's; 3 = next rank exceeds max for current unit type → needs a bigger command). Can buy progress with prestige (`PromoteByPrestige` 161614).
- **CIC:** `BattleUnits.UpdateCommanderInChief` (81315) picks the highest campaign ranking (command level × 100000 + rank, exp, fame); the player competes equally. Losing CIC sets prestige to 0 and defames.
- **Losing the unit:** with no command and no army group/CIC, hierarchy = −1 and **vanilla campaign AI pauses for every faction** (`UpdateUnitAI` 11368) until a new command; only the offensive round-robin (11311-11336) keeps running.

## What the player controls

| Echelon | Player controls | AI controls on the player's side |
|---|---|---|
| 10-14 (regiment/brigade/division) | own unit (moves with parent HQ on the map), staff, prestige actions | parent HQs, peers, army plan, recruiting, weapons, policies |
| 15-16 (corps/army) | own HQ movement on the map; sub-HQs at 16 | army groups, other armies, CIC plan, weapons, policies |
| 18 army group | several HQs | CIC plan, weapons, policies |
| CIC | all armies and fleets, weapon procurement (`CheckPurchaseWeapons` 16263), fiscal budget (`IncreaseFiscalBudget` 226654), fleets (12012), army groups (17723) | **policies** (always AI; the player can only buy the *start* of a research with prestige, 211352) |

- **Policies (stage E investigation).** Vanilla `CheckAIPolicyChange` (211019) has no CIC exemption, so the AI chooses policies for the player's alliance at every rank; the policies-panel automanage checkbox is inert under the forced `ai_vs_ai`. A purchase pays `CalculatePrestigeCostPolicy` (46081: `ResearchDuration / rank^dlcwl_prestigepolicycostmodifierexp × dlcwl_prestigepolicycostmodifier`, × incidents 21, 23, 44/47) and calls `AddResearch` (`CheckSelections` 211350-211362, any rank); a career cannot cancel research (211368). Neither vanilla nor Whiskey starts research while anything is in research, so a purchase is never replaced; vanilla's emergency cancel (211065-211074) did remove it (BUG-WL-003, fixed by #19's protection). Whiskey's need-driven pick runs for the player's alliance at every rank, CIC included (user decision 2026-10-15). Lt Gen Act (42) promotes the alliance's current CIC to rank 8 (210348-210350) — possibly the player. `dlcwl_incidentradicalizationpolicycost` is loaded (58625) but never read.
- Recruiting caps by echelon/rank: `GetSingleUnitLimitation` (47905).
- **Prestige economy:** moving a unit not under the player's command → "Request movement" (91663, cost `GetPrestigeCostTakeover` 45899) creating an Operation; forced march, fort/depot construction (`ConstructBuilding` 45977) and weapon upgrades (43949) cost prestige.
- **Operations** (`CommanderRelations.Operation` 41029-41226) are *player-paid moves/take-overs of another commander's unit*, not AI operations. On finish, the original commander's relation changes (cause 4) by how the unit fared (rout time, condition, supply, attrition). Only capital defense respects `IsMovedByPlayer` (11753, 11791).
- **Relations** (`CommanderRelations` 41027-41528): 11 causes, clamped [−1, 1], decay toward 0. Low relation with own subordinate commanders drives **feuds** (`Regiment.CalculateFeud` 116547; feuding units run in `aibattle[2]`, `isplayeraiorfeud = 2`).
- **Effect multipliers** (`EffectMultiplier` 44633) from staff/companions/items/camp/incidents apply only to player-commanded units — incl. order delays (ids 12, 13), supply, morale, speed. Player units perform differently from identical AI units.

## Orders from superiors (dispatches)

`AIBattle.CheckCurrentOrderUpdate(unit, type, pos, name, rot, w, d, calledfromcampaign)` (8233-8667) turns an AI decision about `unit` into `DLC_WL.givenorder` (`GivenOrders` 41744) shown on the career panel.

| Type | Order | Layer | Issued from |
|---|---|---|---|
| 0 | Take objective / vanguard | Battle | `UpdateMovingTargets` enemy-held objective (6998) |
| 1 | Deploy to engage | Battle | `Regiment.CheckEnemyContact` (117383) |
| 2 | Support friendly unit | Battle | `MarchToSoundOfGuns` (3766) |
| 3 | Attack isolated enemy | Battle | `CheckForSeparatedEnemy` (8210) |
| 4 | Assault | Battle | `AssaultMove` (3646) |
| 5 | Redeploy | Campaign | `CheckOffensiveMovements`, empty target (14451) |
| 6 | Offensive operation | Campaign | `UpdateMicroMovementInOffensive` (14052) |
| 7 | Engage enemy | Campaign | `CheckForDefensiveOperations` (13715, 13775) |
| 8 | Defend capital | Campaign | `AssignUnitToDefendCapital` (11864) |
| 9 | Construct fort | Campaign | fort orders (9674 — unreachable for player units, 9753) |
| 10 | Construct supply depot | Campaign | `CheckSupplyDepotConstruction` (14775) |
| 11 | Reserve | Battle | `CheckReserveOrder` (6841), recursion (8335) |
| 12 | Defend location/objective | Battle | `UpdateMovingTargets` own objective (7002); `CheckGlobalAIStrategy` defend (6463) |
| 13 | Engage at will | Battle | `CheckRemovalOfOrders` (6804) |
| 14 | Enemy vanguard | Battle | `CheckRemovalOfOrders` (6808) |
| 15 | Retreat | Battle | `CheckRemovalOfOrders` when `macroai == 3` (6798) |
| 16 | Offensive | Campaign | `CheckOffensiveMovements`, defended target (14455) |

- **Campaign gate (8366):** returns if the player is CIC or is **not the top-level campaign-map group** (`flag2`). ⇒ Brigade/division/sub-HQ players **never get campaign dispatches**; the AI just moves their parent corps. Campaign orders always overwrite and re-pop the panel (no dedupe); `givenorder.commanderid` is the player's own id on that path.
- **Battle path:** returns if the player is the battle's commanding officer (8329); only orders to the player's parent chain (same `groupaiobject`) pass; type-11 relayed via `unitlinkedto`; issuer remapped to the parent's commander. **Dedupe** (8643): same type kept (within 110 m for type 0), 15 (retreat) terminal, 13 sticky, 12 never replaces an attack order.
- **Compliance is reward-only:** `Regiment.CheckDLCWLTopics` (123468-123537) checks the player's top unit inside the order zone (`InsideCombatZone` 160966): battle orders +prestige on first entry; types 5/6/7/16 pay and clear; type 8 pays continuously; 9/10 pay on building completion. **No penalty for disobeying** (no timeout, relation or prestige loss). Type 16 pays 0 (`EarnPrestige` has no case 16).
- **Clearing:** non-Reserve orders cleared at battle end-of-day (`CheckClearingBattleOrdersEOD` 47985); scene load clears at frame 2 (23862, 29878). Saved in `DLC_WL.dat` / `DLC_WLb.dat` (45098, 45449).

## How the campaign AI treats the player

The AI decides; the player executes via an order. **Vanilla never moves or stops a `dlcw_isundercommander` unit on the campaign map** — at every site it issues an order instead (11860, 13711, 13769, 14034, 14381-14456, 14765) and skips `StopRegiment` (13840, 10202). It also skips player units for perks (11879), campaign stance (11903), recruitment attachment (`RemovePlayerUnits` 12808), weapon upgrades (16139), linking/corps combination/town recapture/brigade merge/final positions (16952, 17134, 17448, 17633, 17908).

It **prefers** the player's group: defensive ops treat player units within 150 as distance 0 (13628); offensives put the player's group first and multiply the go-threshold by `dlcwl_careerincreasedaggressiveness` (2.0) **for every faction in a career**, and again if the player's group is in the list (14341-14347).

## How the battle AI treats the player

- **Three AIBattles** (85203-85212): `[0]` enemy, `[1]` player side, `[2]` feud (player's own feuding subordinates).
- **Detachment is the protection.** At deployment (85321-85336) vanilla `ChangeStance(cmd, cmd.ai_stance, immediate)`, sets `cmd.permanentlydetached = true`, and re-points the command's descendants' `groupaiobject` to the player's command. Group orders to superiors skip detached subtrees (`GetAttachedUnitsReg(excludedechainedunits)` drops `permanentlydetached` children, 119857; `PerformActionForGroupForAllUnitsOnLevel` 92588-92616). **There is no `dlcw` filter in `SetWaypoint`/`SetGroupFormation`** — if `permanentlydetached` is cleared (the player's stance-off buttons call `ChangeStance(…, 0)` which sets it false, 90786-90793; order type 23 toggles it, 125415), an AI group order to a superior **drags the player's units**. Nothing re-asserts it per tick.
- **Micro-AI gate** `PerformAIActionDLCWL(unit, group)` (5101): blocks AI actions on player units unless the player's own group has `ai_stance >= 1` (the player delegated it to the AI). Ungated vanilla tasks still reach player units in `allgroupsassigned`: `UnlimberArtilleryAIMicro` (3482), `CheckShorteningDistanceOfAri` (3784), `CheckForRallying` (5522), micro cycle 6 combat behavior (5615).
- **Pool leak:** `AddUnitToPool` adds all descendant groups to `allgroupsassigned` (7641) but `RemoveUnitFromPool` removes only the unit passed (7585-7620) → the player's sub-groups stay in `aibattle[1].allgroupsassigned`.
- **UI control:** `CheckDLCWLControl` (140684) refuses orders to non-player units ("Unit not under your control"); called when `SetWaypoint(showmovementoptions: true)` (91292) and from right-click/fire/construction paths. AI calls use `showmovementoptions: false`.
- **Battle take-over:** the player can pay prestige to take over a unit in battle (`TakeOverUnitBattle` 164444): it becomes player-owned, stance 0, detached.
- **Player-side retreat in W&L is decided by `aibattle[1].CheckGlobalAIStrategy`** (6314-6467): victory-bar or force-balance → `macroai = 3` → type-15 retreat orders → `SetEndOfBattle(8)` after `timetostartairetreattimer`. `CheckAINightWithdrawal` (83491) includes the player's side under ai_vs_ai.

## Rules for Whiskey code

1. Never move, stop, re-stance, re-form, or change combat behavior of a `dlcw_isundercommander` unit (use `TacticalWriteGuard` in battle; mirror vanilla's order-instead-of-move on the campaign map). **Battle: enforced by the battle core** (below).
2. Superiors and peers of the player are AI and may be commanded, but a group order on an ancestor reaches every non-detached descendant. **Battle: enforced by the battle core's chain planner** (below) — it never re-asserts `permanentlydetached`; it orders the ancestor only when the player's command is detached from it, otherwise it orders the ancestor's children instead, and it delivers the player's share of the move through `AIBattle.CheckCurrentOrderUpdate` with the matching type.
3. If Whiskey replaces a vanilla movement path that normally emits a dispatch (`UpdateMovingTargets`, `AssaultMove`, `MarchToSoundOfGuns`, `CheckForSeparatedEnemy`, campaign offensives/defensive ops/capital defense), it must emit the same dispatch.
4. Writing `macroai` on the player-side AIBattle in W&L produces dispatches: 2 → type-12 defend orders; 3 → type-15 retreat and a battle-ending retreat timer.
5. Filter `dlcw_isundercommander` units out of every enumeration of `allgroupsassigned` / `allattachedunits` used for AI decisions (pool leak). Battle: the snapshot carries `UnitFlags.PlayerOwned`; the posture inputs drop player-owned probes (`PostureProbeFilter`) and every per-regiment walk checks the flag or `TacticalWriteGuard.CanWrite`.
6. Campaign dispatches have no vanilla dedupe — rate-limit and dedupe any Whiskey-issued campaign orders (type + target + ~110 m).
7. Treat the player's group as an unreliable executor (disobedience is free); don't put it in must-succeed roles without a fallback.
8. Gate Whiskey campaign writers on vanilla's hierarchy −1 pause (11368), or accept the asymmetry deliberately.
9. A CIC player is a different regime: Whiskey's CIC for that side stands down; every unit on the player's side is player-owned.
10. Respect `DLC_WL.IsMovedByPlayer` (player Operations) everywhere, not just capital defense.

### Battle-core enforcement (2026-10-15)

- **`PlayerOwnership`** (pure, `Tactical/Battle/`) computes `UnitFlags.PlayerOwned` per snapshot unit: in a career `dlcw_isundercommander`, a battle take-over, or a CIC player's whole alliance (covering the lag before the cached flag refreshes); outside a career the player alliance unless `ai_vs_ai`/debug all-armies-AI. Ancestors are never owned. `ApplyChainFlags` marks the player's command, its detached state and its ancestors.
- **Feud is a group state.** `ActivateFeud` (116613) sets `ai_feudstance` on the group, and `AddUnitToPool(overwriteaigroups)` points every descendant's `groupaiobject` at it (7637-7648). The snapshot marks `UnitFlags.Feud` on a unit whose own state, AI group or command ancestor feuds (`PlayerOwnership.ApplyFeudGroups`; only a unit's own `ai_feudstance` is a source), and `TacticalWriteGuard`'s `Regiment` overloads apply the same rule. Whiskey never writes a feuding unit, except #42's replay of vanilla's own feud move (never on a player-owned unit).
- **Chain rule** (`TacticalChainOrderPlanner`): a player-owned descendant is *exposed* to a group order when neither it nor any node between it and the group is `permanentlydetached` (vanilla's group orders skip a detached subtree, 119865). A group with no exposed owned descendant is ordered itself; otherwise its direct children are ordered (owned children skipped, children with exposed owned descendants fanned out again). In-place rotations walk the same exclusion (`PostureSense.CollectRotatableRegiments`) and re-check `CanWrite` per regiment.
- **Dispatch:** when the executor moves an ancestor of the player's command, `TacticalPlayerDispatchRuntime.Emit` sends one `CheckCurrentOrderUpdate` per executed plan (`TacticalDispatchTypeMapper`: attack/exploit 0, assault 4, support 2, hold/defend 12, reserve 11, screen/probe/guard 1, fix 3; never 13-15), sharing #62's dedupe (same type within 110 m).
- **Reconnaissance (BC9).** Vanilla's per-unit AI actions (skirmisher detach/reattach `CheckSkirmishing` 5343-5406, cavalry behaviour, perks) run through `PerformAIActionDLCWL` (5101-5116): a unit under the player's command is skipped unless the player's own group has an AI stance (≥ 1) and the unit is in that AI group (5107-5109), so a W&L player in stance mode gets vanilla's auto-skirmishers. A detached skirmisher/scout company copies its regiment's `dlcw_isundercommander` (`DetachCompany` 90477), so it is `PlayerOwned` like its regiment. The recon producer never takes a player-owned scout or a scout under a player-owned brigade, never nominates a probe group with a player-owned unit below it, and `SkirmisherScreenDistancePatch` never pushes a player-owned company (`TacticalWriteGuard.CanWrite`). Under `ai_vs_ai` the player's alliance's AI side scouts too, with the player's command left out.
- **Guard:** the executor refuses `PlayerOwned` at plan time (`OrderExecutor.Plan` step 1) and re-checks every live target with `TacticalWriteGuard.CanWrite`; writers that cannot fan out (#59, #60, #42's fallback) refuse a group with an exposed owned descendant.

## Candidate vanilla bugs (not yet tracked)

- `CheckShipConstruction` (12012) and `CheckArmyGroupManagement` (17723) skip **every** faction when the W&L player is CIC (no alliance check) → the enemy stops building ships and managing army groups.
- Possible prestige sign bugs: `EarnPrestige` passes negative gains with `AdjustPrestige` type 2 (subtract) (46733-46738); `IncreaseFiscalBudget` passes `0 − cost` with type 2 (226658). Code-read only, not reproduced.
- Type-16 "Offensive" orders pay no prestige (`EarnPrestige` 46627 has no case 16).
- Policies panel greys out affordable policies after the first unaffordable one (`UpdateActivations` 209248-209258; `flag` never resets) — BUG-WL-004, display only.
- Likely NRE at 96704-96706 (type-9 completion nulls `givenorder` then reads `.type`; only reachable via debug key).

## Not verified in-game

Issuer portrait on campaign dispatches; the prestige sign bugs; whether queued (`useorderdelay`) `SetWaypoint` re-checks `permanentlydetached` at execution; unittyp 14-16 ↔ brigade/division/corps mapping in every campaign shift; per-battlefield prefs overriding the global ones.
