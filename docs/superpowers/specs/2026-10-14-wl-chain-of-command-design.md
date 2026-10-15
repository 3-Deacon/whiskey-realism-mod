# W&L Chain of Command — Design

**Status:** §A-§C (battle ownership, chain target resolution, battle dispatches) are implemented by the battle core (`2026-10-15-battle-core-design.md`, OrderArbiter); this spec's own slice covers §D-§F (stage F, implemented on `feat/wl-chain-campaign`; plan `docs/superpowers/plans/2026-10-15-wl-chain-campaign.md`). Approved direction (user, 2026-10-14: "implement your improvements"; disobedience → AI adapts, no penalty). Vanilla reference: `docs/wl-player-model.md`.

## Problem

In a W&L career the player is usually a subordinate and their own side is AI-run. The mod currently:

1. **Battle:** `TacticalWriteGuard` protects the player's whole ancestor chain (`IsWlCurrentCommandOrChain` matches ancestors via `IsChildOf`), so Whiskey's battle orchestrator never commands the player's brigade/division/corps — those fall back to plain vanilla. Vanilla itself commands them and protects only the player's own subtree (deployment detach 85321-85336).
2. **Battle:** when Whiskey moves the player's parent formation, the player gets no dispatch (vanilla emits dispatches only from inside the movement methods Whiskey bypasses).
3. **Campaign:** vanilla sends campaign dispatches only to a player who is the top-level map group (gate 8366). A brigade/division player inside a corps gets nothing when the corps redeploys or attacks; Whiskey's own `WlStrategicOrderBridge` routes through the same gated call, so its orders for those players are silently dropped.
4. **Vanilla bug:** when the W&L player is commander-in-chief, `CheckShipConstruction` (12012) and `CheckArmyGroupManagement` (17723) return for **every** faction (no alliance check) — the enemy stops building ships and managing army groups.

## Design

### A. Ownership (battle and campaign)

`TacticalWriteGuard.IsPlayerProtected(unit)` = the unit is player-owned per vanilla: `unit.dlcw_isundercommander` (player's command + descendants, take-overs, CIC/army-group cases per `IsUnitUnderCommander` 91168) **or** a DLC take-over (`IsUnitTakenOver`), plus non-W&L player-alliance units when not `ai_vs_ai`. Ancestors are **not** protected. Feuding units (`ai_feudstance >= 0`) and the feud AIBattle are not commanded by Whiskey.

New `TacticalWriteGuard.IsAncestorOfPlayerCommand(group)` — true when the player's current command is a scene-graph descendant of `group` (vanilla's own ancestry test: `IsChildOf`, 8272/91203).

### B. Ordering the player's superiors without dragging the player (battle)

Vanilla group orders skip `permanentlydetached` subtrees (`GetAttachedUnitsReg(excludedechainedunits)` 119857). The player's stance-off button legitimately clears that flag (`ChangeStance(…,0)` 90786), so Whiskey must not re-assert it. Rule:

- If `IsAncestorOfPlayerCommand(group)` and the player's command is detached → Whiskey may issue the group order as usual (vanilla skips the player's subtree).
- If the player's command is **not** detached → Whiskey does **not** issue a group-level order on that ancestor; it orders the ancestor's direct child groups individually, skipping the child that contains the player's command (and the player's command itself).
- Every Whiskey battle writer resolves this through one helper, `TacticalChainOrderPlanner.ResolveTargets(group) → IReadOnlyList<Regiment>` (pure decision over a snapshot: group, its direct children, which child contains the player, detach flag).

### C. Dispatches for the player's share (battle)

When Whiskey issues a movement/posture order to an ancestor of the player's command, it calls vanilla `AIBattle.CheckCurrentOrderUpdate(ancestor, type, pos, name, rot, width, depth, calledfromcampaign: false)` — vanilla's battle path (8327-8364) already resolves the parent chain, issuer, zone sizing and dedupe (8643). Task → order type mapping (`TacticalDispatchTypeMapper`, pure):

| Whiskey task | Vanilla order type |
|---|---|
| AttackObjective (enemy-held) / Exploit | 0 Take objective |
| Assault / Charge-supported attack | 4 Assault |
| SupportAttack | 2 Support friendly unit |
| HoldObjective / Defend / FallBackToLine (new line) | 12 Defend location |
| ReserveWait / ReleaseReserve | 11 Reserve |
| Screen / Probe / GuardFlank | 1 Deploy to engage (screen wording) |
| FixEnemy | 3 Attack enemy unit (target = fixed enemy) |
| no movement / no change | none |

Whiskey must not emit types 13/14/15 (vanilla's own `CheckRemovalOfOrders` owns those). Macro-AI writes on the player-side AIBattle already produce vanilla dispatches (2 → type 12; 3 → type 15); the single macroai owner (#44) never writes 3.

### D. Campaign dispatches for subordinate players

*Corrected by the stage F investigation (§ Vanilla and W&L model, campaign side); plan `docs/superpowers/plans/2026-10-15-wl-chain-campaign.md`.*

New pure `WlCampaignDispatchPolicy` + runtime `WlCampaignDispatchRuntime` (the one campaign dispatcher) + patch #69 `WlCampaignMoveDispatchPatch`:

- **Trigger 1 (vanilla moves the player's formation):** Postfix on static `AICampaign.MoveUnitTo` (14479) records a pending move when it returned true, W&L is active, the player is not CIC, has a command, is **not** its own campaign-map group, and the moved unit **is** the player's campaign group (`BattleUnits.GetCampaignGroup(currentcommand)`, 82991), not player-owned, not moved by the player (`IsMovedByPlayer` 45797). Only the campaign group itself: a corps or top division is its own map counter, so an army or army group above it moving does not carry the player (the spec's "or a map-level ancestor" is dropped). The type is **not** decidable inside the Postfix: the offensive (14383-14386) and the attack branch of the defensive operations (13771-13775) add the unit to `unitsinoffensiveoperations` / `unitsindefensiveoperations` only after `MoveUnitTo` returns. So the Postfix stores the move with the unit's list membership before it, and a Postfix on `AICampaign.UpdateUnitAI` (11303; every decision-time `MoveUnitTo` caller runs inside it) classifies and emits after the caller finished: in `unitsindefensiveoperations` → 7 Engage (target = nearest enemy of the faction to the destination, its name); in `groupstodefendcapital` → 8 Defend capital (capital position and `CityName`, as 11710-11711); in `unitsinoffensiveoperations` and already there before → 6 Offensive operation; newly there → 16 Offensive when the destination's AI area has enemy strength (`enemycampaignunitsstrengthclose`, vanilla's `num2` source 14215-14230) else 5 Redeploy; anything else (fort, depot, roll-up, final positions, Whiskey's own moves) → 5 Redeploy.
- **Trigger 2 (Whiskey's own strategic orders):** `WlStrategicOrderBridge` keeps its classification; for a player-owned top-level map group (vanilla's own campaign gate passes, 8366) it now emits through the same dispatcher instead of calling vanilla's gated `CheckCurrentOrderUpdate(calledfromcampaign: true)`; that path and the bridge's `PlayerOrderDedupe` campaign shadow are deleted. The bridge still never moves the subordinate player's parent group (`IsPlayerPartOfUnit` 47520 → ineligible): moving it would carry the player's command (decision recorded; open question to the user). Vanilla moves that group; trigger 1 tells the player.
- **Emission (vanilla-identical, 8394-8664):** `groupunit` = the player's parent unit (`BattleUnits.GetParentUnit`, 93883; vanilla's `unit = parentUnit`, 8400); zone 20×20 widened to that unit's width/depth and group size and to `dlcwl_minimumobjectivezone` (8402-8428); height from `Terrain.activeTerrain.SampleHeight` (8638); headline/content from the type's tooltip id (5 → 1085, 6 → 1087, 7 → 1086, 8 → 1088, 16 → 1203; `VariableContainer(0, destination, issuerUnit, 0)`); `new DLC_WL.GivenOrders(..., rotation + 180 = 179, ..., resetfirstunitreached: true, showzone: true, issuer)`; `givenorderssession++`; `CareerInformationPanel.ShowNewOrder` on the panel vanilla finds (8653-8664). Issuer: trigger 1 = the moving formation's commander (`movedUnit.commander`, the commander who ordered the move; vanilla's battle path also names the ordered ancestor's commander, 8288); trigger 2 = `DLC_WL.GetSeniorCommander()` (47718), as an index into `GameVars.commander` (vanilla's own campaign path names the player himself, because its `unit` is the player's group). Compliance and prestige then run through vanilla `CheckDLCWLTopics` (123468) unchanged.
- **Dedupe/rate limit (vanilla has none on campaign; its dedupe at 8640-8646 is battle-only):** skip when the current given order (same session as the last emission) has the same type and its target within 110 units; skip when the last emission targeted within 110 units less than one game day ago, whatever the type (this also absorbs the second `MoveUnitTo` call of a queued move, 10048); skip a move whose destination is within 20 units of the unit (vanilla's own dispatch zone size, a water/stacking correction such as `CheckFinalPositions` 17928).
- Top-level map-group players and CIC players keep vanilla behaviour (vanilla dispatches them / they command).

### E. Unreliable executor (no penalty)

`PlayerComplianceLedger` (pure, campaign + battle) + `PlayerComplianceRuntime`. The compliance signal is vanilla's own: `CheckDLCWLTopics` sets `givenorder.firstunitreached` when a player-owned unit without a detachment parent stands inside the order zone (123484-123517), and the zone exists only while the order has `showzone` (`AvatarButtonPanel.Update` deletes it otherwise, 160798-160807). The runtime watches `DLC_WL.givenorder` (new `givenorderssession` → a new dispatch; the tracked order's `firstunitreached` → complied; the order replaced or cleared first → neutral, no sample; no compliance within the grace window → missed). Recorded only where the player decides the movement: campaign orders while the player's command is its own map group (a subordinate is carried by its parent group and cannot disobey), battle orders with a zone (types 13/14 have none). Grace: campaign 72 game hours (`GetCampaignFloatingDate`), battle 0.5 battle hours (`TacticalDoctrineTime`, night-frozen). `Reliability` = exponential average of outcomes (α 0.25, prior 0.5). Writers: the campaign dispatch event handler (the `UpdateUnitAI` Postfix of #69) and the battle coordinator tick. The ledger survives battle round-trips (it is keyed by career — commander id + campaign level — and never reset by a campaign reload) and is saved in the sidecar; a load adopts the saved value only when this session has no samples for that career (it measures the human player, so a reload does not roll it back). Consumers: the battle evidence (`ArmyEvidenceBuilder`) counts a player-owned combat regiment's strength × `Reliability` in its sector (today it counts it fully), so sectors held by an unreliable player get less weight for Main/SupportMain and draw reserve relief; the campaign planner (stage G) reads `PlayerComplianceRuntime.Reliability`. No relation or prestige change (user decision). Rollback `Enable Player Compliance Ledger = false` → reliability 1.

### F. Vanilla bug: CIC player stops enemy fleets and army groups

Confirmed (bodies 11998-12090 and 17705-17768). Patch #70 `CicFactionAiBugGuardPatch`: Prefix on `AICampaign.CheckShipConstruction` and `AICampaign.CheckArmyGroupManagement` arms a one-shot flag when W&L is active, the player is CIC and the processed faction's alliance is not the player's; a Postfix on `DLC_WL.IsCommanderInChief` (47475) returns false for the **first** call with `manualcommander == -1` while armed and disarms. In both bodies that first call is the early-return guard (12012, 17723), so nested calls (`GrabFleet`, `RaiseFleet`, `CheckCommanderPromotions`, `CreateShip`; `ArmyGroup.AddUnit`, `CreateNewArmyGroup`) keep the true value — the spec's "return false while set" would have leaked into them. A Postfix (`Priority.First`) and a Finalizer disarm when vanilla returned before the guard. #16 `ArmyGroupManagementPatch` repeated the buggy guard (`ArmyGroupDoctrine.ArmyGroupsAllowed`); it now applies the CIC test only to the player's alliance. Tracked as `BUG-WL-001` (`docs/bug-fixes/vanilla-wl-career.md`).

## Vanilla and W&L model (campaign side; stage F investigation 2026-10-15)

Decompile `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`, build 14703111. Data: `<GTCW>/Config/dlcwl_config.dat`, `tooltips_dlcwl.txt`.

- **Campaign gate (8366).** On `calledfromcampaign` vanilla returns if the player is CIC, or `!flag2` (the player's command is not its own campaign group, 8271-8274), or `flag2 && !flag`, or the ordered unit is not player-owned. `flag` = the player's parent unit is a transform-descendant of the ordered unit (8275-8278); `GetParentUnit` returns the unit itself when it has no parent (93883-93898), so for a top-level player `flag` = "the ordered unit is the player's group or an ancestor". The only passing case is an order to the player's own map group (or a player-owned ancestor). Confirmed: brigade/division players inside a corps never get campaign dispatches.
- **Vanilla's campaign dispatch sites** never move a `dlcw_isundercommander` unit; each issues `CheckCurrentOrderUpdate(..., calledfromcampaign: true)` instead: offensive 5/16 (14449-14456), micro-offensive 6 (14052), engage 7 (13711-13715 queue the move directly, 13769-13775), capital 8 (11860-11864, issued every pass for every capital group, so a top-level player who stays put is re-popped each pass), fort 9 (9656-9674), depot 10 (14765-14775). Vanilla moves an AI group that carries a subordinate player like any other (the `dlcw` tests are on the moved unit only).
- **`MoveUnitTo` (14479-14540)** is `public static bool`. False when the unit is null (14489-14491), the destination state is not at war/allied/own or the unit is in battle (14494), or a queued move for the unit is already running (14519-14521). True after a direct `SetWaypoint` (14514, `movearoundenemyunits: false`) or after queueing a `DefensiveMovingOrder` (14523, `true` — every AI caller). Queued orders are executed by `DefensiveMovingOrder.UpdateOrders` (9965, from `BattleUnits` 79295), which calls `MoveUnitTo(..., false)` again (10048): a queued move passes the Postfix twice. Decision-time callers all run inside `UpdateUnitAI` (11303): offensive round-robin (11319), micro-offensive (11440), defensive ops (11497), capital (11507), depot (11512), forts (11522 → `FortConstructionOrder` ctor 9658), final positions (11593), roll-up (11598).
- **`GetCampaignGroup` (82991)** returns the unit itself for an army group (17) or any unit visible on the campaign map, else the first map-visible ancestor. Map visibility (`GetUnitVisibilityStateOnCampaignMap` 82934): corps (15), top-unit divisions (14 + `istopunit`), `overridesymbol` 15 → 1; armies (16) → 1 with independent sub-units else 2; army groups → 3; brigades and non-top divisions → 0. A player whose command is a corps or top division is a top-level map group; below that, the command has no map counter and moves with its campaign group.
- **Hierarchy and control (campaign map).** 10-13 (regiment, brigade) and 14 non-top: no counter, carried by the campaign group; can still recruit/manage at 14 (`wl-player-model.md`). 14 top / 15 / 16: own counter, the player moves it and receives vanilla's campaign dispatches. 18 (army group / CIC): commands several HQs; the CIC owns every unit of the alliance and vanilla skips the player's faction AI entirely (11620).
- **`GivenOrders` (41744-41770)**: `commanderid` = the `manualcommanderid` or the group unit's commander; `resetfirstunitreached` clears `firstunitreached`. `IsBattleOrder` = types 0-4, 11, 12 (41772). `IsActive` = battle orders in battle scenes, campaign orders on the campaign map (41781).
- **`GetSeniorCommander` (47718-47743)** returns a `GameVars.Commander` (not an id): null for the CIC, the CIC when the player has no command or an army group, the army-group commander when the command is assigned to one, else the commander of `GetParentUnit(currentcommand)` — for a brigade in a division in a corps that is the division commander, not the corps commander whose move it is.
- **`CareerInformationPanel.ShowNewOrder` (182239-182257)**: opens the panel, text, issuer portrait (`commanderid`), shows the zone mark when `showzone` (`AvatarButtonPanel.ShowMark` 160938), stores the go-to position.
- **Dispatch text** (`tooltips_dlcwl.txt`): `[VAR0]` = the player's rank (222865), `[VAR47]` = the `objvar` unit's type name (223167), `[VAR48]` = the player's command type (223177), `[VAR49]` = the destination string (223187). Campaign texts 1085/1086/1087/1088/1203 say "move your [VAR48] to the stated position", so they read correctly for a subordinate.
- **Compliance and prestige (`CheckDLCWLTopics` 123468-123537).** Any player-owned unit with no detachment parent inside the zone, once per order session: battle orders pay `reachingactionzone` (5), campaign 5/6/7 pay 150/100/120 (`dlcwl_config.dat` "prestige gain for reaching offensive operation zone" etc., `EarnPrestige` 46629-46670) and clear the order; 8 pays continuously (500 per year); 16 pays nothing (`BUG-WL-002`). A subordinate's units move with its campaign group, so a subordinate dispatch with a zone is complied with on arrival: **the subordinate earns the same arrival prestige a top-level player earns by moving** (open question to the user, see the plan).
- **CIC bug (`BUG-WL-001`).** `CheckShipConstruction` (12012) and `CheckArmyGroupManagement` (17723) return on `dlc_scenarioactive && IsCommanderInChief()` with no alliance test; `IsCommanderInChief()` (47475) only asks whether the chosen commander is CIC of his own alliance. Both run per faction inside `UpdateUnitAI` (jobs 12 and 32, 11481, 11583). Vanilla gets it right one screen later: the faction loop skips the CIC player's own faction (11620). So the guards only ever stop the other factions. `CheckPurchaseWeapons` (16263) tests `playeralliance == _alliance` correctly.

## Components & ownership

| Unit | Kind | Responsibility |
|---|---|---|
| `Tactical/TacticalWriteGuard.cs` | runtime | ownership per §A; `IsAncestorOfPlayerCommand` |
| `Tactical/TacticalChainOrderPlanner.cs` | pure | §B target resolution |
| `Tactical/TacticalDispatchTypeMapper.cs` | pure | §C mapping |
| `Tactical/TacticalPlayerDispatchRuntime.cs` | runtime | §C call into vanilla, once per (ancestor, type, target) change |
| `Strategic/WlCampaignDispatchPolicy.cs` | pure | §D trigger/type/dedupe decisions |
| `Strategic/WlCampaignDispatchRuntime.cs` | runtime | §D pending moves, classification inputs, GivenOrders emission |
| `Patches/WlCampaignMoveDispatchPatch.cs` | patch #69 | §D trigger 1 (Postfix on `MoveUnitTo`; Postfix on `UpdateUnitAI` flushes) |
| `Strategic/PlayerComplianceLedger.cs` | pure | §E |
| `Strategic/PlayerComplianceRuntime.cs` | runtime | §E observation, career key, sidecar restore, `Reliability` |
| `Strategic/CicFactionAiGuard.cs` | pure | §F arm decision |
| `Patches/CicFactionAiBugGuardPatch.cs` | patch #70 | §F |

`WlStrategicOrderBridge` loses its direct gated `CheckCurrentOrderUpdate` path (replaced by the dispatcher — no dead code left).

## Testing

Pure units fully harness-tested: chain target resolution (detached / not detached / player at root / player two levels down), dispatch type mapping, campaign dispatch policy (`WlCampaignDispatchTests`: trigger preconditions, context → type, dedupe window, queued second call), compliance ledger (`PlayerComplianceTests`: grace windows, neutral outcomes, decay, restore, sidecar round trip), CIC guard (`CicFactionAiGuardTests`). Runtime pieces verified by build + smoke (dispatch panel shows superior's name; brigade player receives "corps redeploying" dispatch; player units never moved by Whiskey).

## Out of scope

Relation/prestige consequences for disobedience (user: none). New dispatch text beyond vanilla tooltip ids.
