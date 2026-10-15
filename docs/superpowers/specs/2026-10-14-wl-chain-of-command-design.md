# W&L Chain of Command — Design

**Status:** approved direction (user, 2026-10-14: "implement your improvements"; disobedience → AI adapts, no penalty). Vanilla reference: `docs/wl-player-model.md`.

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

New `WlCampaignDispatchRuntime` + pure `WlCampaignDispatchPolicy`:

- **Trigger 1 (vanilla moves the player's formation):** Postfix on `AICampaign.MoveUnitTo` (static, 14479). When W&L is active, the player is not CIC, the player is **not** the top-level map group, and the moved unit is the player's campaign group (`BattleUnits.GetCampaignGroup(currentcommand)`) or a map-level ancestor of it, and the move succeeded → emit a dispatch. Type from vanilla context at that moment: unit in `unitsinoffensiveoperations` → 16 Offensive (enemy strength at target) or 5 Redeploy; in `unitsindefensiveoperations` → 7 Engage; in `groupstodefendcapital` → 8 Defend capital; otherwise 5 Redeploy.
- **Trigger 2 (Whiskey's own strategic orders):** `WlStrategicOrderBridge` routes player-chain intents through the same dispatcher instead of the gated vanilla call.
- **Emission:** build `DLC_WL.GivenOrders` with vanilla's ctor (41744), issuer = `DLC_WL.GetSeniorCommander()` (47718) id, zone ≥ `dlcwl_minimumobjectivezone`, text from the same tooltip ids vanilla uses for that type; assign `DLC_WL.givenorder`, bump `givenorderssession`, call `CareerInformationPanel.ShowNewOrder` exactly as vanilla 8648-8664 does. Compliance/prestige then works through vanilla `CheckDLCWLTopics` unchanged.
- **Dedupe/rate-limit (vanilla has none on campaign):** skip when the current order has the same type and target within 110 units, and never re-pop more than once per game day for the same type+target.
- Top-level map-group players and CIC players keep vanilla behaviour (vanilla already dispatches / player commands).

### E. Unreliable executor (no penalty)

`PlayerComplianceLedger` (pure, campaign + battle): records each dispatch (type, target, issue time) and whether the player's top unit entered the zone (`firstunitreached` / zone test) within a grace window (campaign: 3 game days; battle: 0.5 game hours). Exposes `ReliabilityScore` per career (decaying average). Consumers: the campaign planner and the battle role allocator treat the player's formation as an unreliable executor (lower weight for must-succeed roles, keep a covering formation when reliability < 0.5). No relation or prestige change (user decision).

### F. Vanilla bug: CIC player stops enemy fleets and army groups

Prefix/Finalizer pair on `AICampaign.CheckShipConstruction` and `AICampaign.CheckArmyGroupManagement`: when W&L is active, the player is CIC, and the faction being processed is **not** the player's alliance, set a `[ThreadStatic]` suppression flag; Postfix on `DLC_WL.IsCommanderInChief` returns false while the flag is set and `manualcommander == -1`. Finalizer clears the flag. Read both vanilla bodies fully first to confirm no other W&L-sensitive branch changes meaning. Tracked under `docs/bug-fixes/`.

## Components & ownership

| Unit | Kind | Responsibility |
|---|---|---|
| `Tactical/TacticalWriteGuard.cs` | runtime | ownership per §A; `IsAncestorOfPlayerCommand` |
| `Tactical/TacticalChainOrderPlanner.cs` | pure | §B target resolution |
| `Tactical/TacticalDispatchTypeMapper.cs` | pure | §C mapping |
| `Tactical/TacticalPlayerDispatchRuntime.cs` | runtime | §C call into vanilla, once per (ancestor, type, target) change |
| `Strategic/WlCampaignDispatchPolicy.cs` | pure | §D trigger/type/dedupe decisions |
| `Strategic/WlCampaignDispatchRuntime.cs` | runtime | §D GivenOrders emission |
| `Patches/WlCampaignMoveDispatchPatch.cs` | patch | §D trigger 1 (Postfix on `MoveUnitTo`) |
| `Strategic/PlayerComplianceLedger.cs` | pure | §E |
| `Patches/CicFactionAiBugGuardPatch.cs` | patch | §F |

`WlStrategicOrderBridge` loses its direct gated `CheckCurrentOrderUpdate` path (replaced by the dispatcher — no dead code left).

## Testing

Pure units fully harness-tested: chain target resolution (detached / not detached / player at root / player two levels down), dispatch type mapping, campaign dispatch policy (top-level vs subordinate, CIC, dedupe window, context → type), compliance ledger (grace window, decay). Runtime pieces verified by build + smoke (dispatch panel shows superior's name; brigade player receives "corps redeploying" dispatch; player units never moved by Whiskey).

## Out of scope

Relation/prestige consequences for disobedience (user: none). New dispatch text beyond vanilla tooltip ids.
