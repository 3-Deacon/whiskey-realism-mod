# W&L Dispatch And Objective Bridge Design

Status: archived after implementation. Current behavior lives in `docs/wl-dispatch-objective-bridge.md`, shipped code, and `docs/patch-catalog.md`.

Created 2026-05-06 after user redirected from tactical prep to campaign-map dispatch/objective behavior.
Scope: campaign strategic layer integration with Whiskey & Lemons career command hierarchy. This spec does not implement code and is not an implementation plan.

## Goal

Use vanilla Whiskey & Lemons campaign orders and campaign objectives as Whiskey Realism's player-facing strategic command surface.

The player may begin as a subordinate inside an AI army, division, or brigade, and later promote into independent division, corps, army, or CIC authority. The strategic layer should respect that role:

- subordinate player: the AI commander still owns strategic decisions, but the player sees intelligible orders from that command chain;
- independent command: the player sees relevant high-command intent without the mod dragging their command by direct movement;
- player CIC: Whiskey does not steer the player's alliance strategy;
- opposing AI: Whiskey strategic doctrine continues to steer normally.

The immediate visible bug to eliminate is the campaign dispatch text that says the command is carrying out orders to `"none"`. That wording comes from the generic dispatch/message system and should be sanitized or suppressed without changing shared stance names.

## Non-Goals

- No custom replacement UI for W&L orders.
- No global behavior patch on `AICampaign.MoveUnitTo(...)`.
- No global change to `GameVars.groupstancename[0]`.
- No AI movement orders for player-controlled W&L commands.
- No mutation of strategic mod state from Harmony patches.
- No player-alliance steering when the player is CIC.
- No implementation from this spec alone; write a focused plan first.

## Source Findings

### Confirmed Vanilla: Campaign Objectives

`CampaignObjective` is a real vanilla objective data model, not just UI text. It stores an ID, name, tooltip, chapter gates, alliance, scenario, date gates, target objects, prerequisite objectives, and newspaper fields: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:178484`.

Objectives are loaded from `config/campaignobjectives.dat`, resolving target names to `Town` or `IIP` references: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:178628`.

Vanilla objective availability is filtered by alliance, scenario, deactivation state, accomplished state, and whether enough target towns/IIPs are enemy-owned: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:178825`.

`AICampaign.PickCampaignObjective(int)` is a small random picker. If the current followed objective is accomplished it clears it, then chooses a random available objective and writes `aifaction[_aifaction].followedcampaignobjective`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17769`.

Vanilla runs `PickCampaignObjective(currentfaction)` at campaign AI scheduler job 33, then checks final positions and rolls enemy objectives in zone on jobs 34 and 35: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:11586`.

The objective UI only shows objectives for `GameVars.playeralliance`, and uses the objective name/tooltip directly: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:178868`.

Objective IDs are mostly treated as `UniqueObjectiveID`, but vanilla has at least one mixed-ID hazard: `IsPartOfObjective(...)` resolves through `GetObjectiveFromID(...)`, while `IsPositionWithinRangeOfTownObjective(...)` indexes `allcampaignobjectives[id]` directly. Any Whiskey bridge should keep using `ObjectiveAdapter` resolution rather than relying on list index equality: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:178729`.

Whiskey already owns the strategic objective picker via `PickCampaignObjectivePatch`, which skips player-CIC alliances and writes the active CIC phase target into `followedcampaignobjective`: `src/WhiskeyRealism/Patches/PickCampaignObjectivePatch.cs:8`.

Whiskey already has an `ObjectiveAdapter` that reflects `CampaignObjective.GetAvailableObjectives(...)`, checks accomplishment/availability, resolves objective positions, and derives strategy metadata: `src/WhiskeyRealism/Strategic/ObjectiveAdapter.cs:29`.

### Confirmed Vanilla: W&L Current Orders

`DLC_WL.GivenOrders` is the current-order DTO. It stores type, headline, content, target position, area rotation/size, commander id, group unit, destination name, first-unit-reached state, and whether to show a zone marker: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:41712`.

`AIBattle.CheckCurrentOrderUpdate(...)` is the main vanilla W&L order bridge. It rejects non-W&L games, missing current command, EOD cycle, null units, and non-player-alliance units: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8233`.

For campaign-origin orders, vanilla only allows the order through a narrow chain check. The `calledfromcampaign` guard returns unless the player is not CIC, the player's current command is its campaign group (`flag2`), that current command's parent is under the ordered unit (`flag`), and the ordered unit is `dlcw_isundercommander`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8263`, `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8366`. Peer formations and sibling formations can be W&L-relevant but still rejected silently by this method.

Campaign order type mappings are built inside `CheckCurrentOrderUpdate(...)`:

| Type | Vanilla headline | Source |
|---:|---|---|
| 5 | Redeploy | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8603` |
| 6 | Offensive Operation | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8613` |
| 7 | Engage Enemy | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8608` |
| 8 | Defend Capital | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8618` |
| 9 | Construct Fort | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8623` |
| 10 | Construct Supply Depot | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8628` |
| 16 | Offensive | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8633` |

The tooltip/config text for these campaign order types lives in `Config/tooltips_dlcwl.txt`: Redeploy/Engage/Offensive Operation/Defend Capital/Fort/Supply Depot at lines 301-318, and Offensive at lines 601-603.

When an order is accepted, vanilla writes `DLC_WL.givenorder`, increments `DLC_WL.GivenOrders.givenorderssession`, shows the career order panel, and marks the zone: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8648`.

The current-order button shows the raw `givenorder.headline` and `givenorder.content`; if no current order exists, it uses tooltip 1180, "No speficic orders or support requests received": `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:160992`.

`CareerInformationPanel.ShowNewOrder(...)` displays the order text, commander picture, continue button, and target zone marker through existing W&L UI: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:182237`.

Vanilla calls W&L prestige/completion handling for arriving in the order zone for types 5, 6, 7, 8, and 16, and clears some completed campaign orders: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:123480`. Caveat: `DLC_WL.EarnPrestige(...)` has explicit switch cases for 5, 6, 7, 8, 9, and 10, but no explicit case 16 in the verified block, so type 16 may function as a visible order without a distinct prestige reward: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:46627`.

### Confirmed Vanilla: Campaign AI Already Uses W&L Orders

Vanilla campaign AI deliberately avoids direct movement for W&L-under units in several call sites and emits W&L current orders instead.

- Fort construction skips `MoveUnitTo` for `dlcw_isundercommander` and emits order type 9: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:9656`.
- Capital defense skips direct movement and emits type 8: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:11860`.
- Defensive response skips the defensive moving order and emits type 7, while still adding the unit to `unitsindefensiveoperations`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13711`.
- Offensive continuation skips direct movement and emits type 6: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14034`.
- Offensive start/redeploy skips direct movement and emits type 5 or 16 depending on enemy strength: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14381`.
- Supply depot construction skips direct movement for W&L-under units and emits type 10 when construction is ordered: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14765`.

This means Whiskey should not invent a separate command UI. It should feed the same order bridge with better strategic intent and objective names.

### Confirmed Vanilla: Generic Dispatch Messages And `"none"`

`Messages.Add(...)` has a W&L filter. In W&L, if the sender exists, vanilla allows the message only when the sender is `dlcw_isundercommander` or, on the campaign map, `DLC_WL.IsPlayerPartOfUnit(sender)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:196752`.

`Messages.Add(...)` constructs message content immediately when the message is added, delayed, or shown: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:196855`. Saved dispatches store literal generated content and do not regenerate message text on load: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:196888`.

The W&L message ignore list excludes bonds, ships, and construction messages; it does not ignore final-waypoint campaign message 56: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:42939`.

Message type 56 is `msg_reachedfinalwaypointcamp`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:196350`.

Message type 56 appends `GameVars.groupstancename[regref.ai_stance]` to the letter body: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:196056`.

`GameVars.groupstancename[0]` is `"none"`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:65061`.

The final-waypoint campaign branch sends message 56 when a player-alliance, permanently detached, campaign-pathing unit reaches its destination. Unlike the non-campaign type-15 branch, it does not require `ai_stance > 0`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:134319`.

`RegimentSetPath(...)` sets `unitwhogavelastmovingorder = this` when no explicit order source is passed, which satisfies the final-waypoint message condition after many campaign `MoveUnitTo(...)` paths: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:130791`.

Therefore the `"none"` text is a generic dispatch rendering bug exposed by W&L's campaign-message filter. It is not proof that `DLC_WL.givenorder` is type "none."

### Confirmed Whiskey: Current Gaps

Whiskey already respects player-CIC at the strategic objective picker and coordinator level: `src/WhiskeyRealism/Patches/PickCampaignObjectivePatch.cs:18`, `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs:1076`.

Whiskey already checks `DLC_WL.IsMovedByPlayer(...)` in some defensive candidate logic: `src/WhiskeyRealism/Patches/DefensiveOpsPatch.cs:294`, `src/WhiskeyRealism/Strategic/DefenseIntentRuntime.cs:641`.

But several Whiskey strategic executors still issue direct campaign movement:

- `ArmyAreaRuntime` writes `theaterposition` and invokes reflected `AICampaign.MoveUnitTo(...)`: `src/WhiskeyRealism/Strategic/ArmyAreaRuntime.cs:145`.
- `OperationalProbeRuntime` directly calls `AICampaign.MoveUnitTo(...)` and adds the unit to `unitsinoffensiveoperations`: `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs:123`.
- `CoastalDefenseCustomOrderRunner` skips only `IsMovedByPlayer(...)`, then directly calls `MoveUnitTo(...)` and adds `unitsindefensiveoperations`: `src/WhiskeyRealism/Strategic/CoastalDefenseCustomOrderRunner.cs:72`.
- `CheckForDefensiveOperationsCandidateFilterPatch` can call `MoveUnitTo(...)` on revert: `src/WhiskeyRealism/Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs:225`.
- `ArmyGroupManagementPatch` avoids appointing the player commander, but grouping/attaching player-commanded formations needs a separate W&L role audit before this bridge is extended to army-group membership changes: `src/WhiskeyRealism/Patches/ArmyGroupManagementPatch.cs:117`.

Those surfaces can bypass vanilla's W&L `dlcw_isundercommander` order-routing pattern and can also trigger final-waypoint dispatches that render stance `0` as `"none"`.

## Design

### Add A Central W&L Strategic Order Bridge

Create a small strategic helper, provisionally `WlStrategicOrderBridge`, under `src/WhiskeyRealism/Strategic/`.

The bridge owns the decision:

```text
strategic intent + unit + alliance + objective/target
-> direct vanilla movement, W&L current order, sanitized report, or skip
```

It should be called by strategic runtimes before any direct `AICampaign.MoveUnitTo(...)` that can affect player-alliance campaign units.

The bridge should not patch `MoveUnitTo` globally because `MoveUnitTo` lacks the strategic context needed to choose between redeploy, offensive, engage, defense, fort, supply, or report-only behavior.

### Pure Classifier And Live Adapter

Split the bridge into a pure classifier plus a thin live adapter. The pure classifier must be testable without Unity objects. The live adapter may derive flags from `Regiment`, `DLC_WL`, `GameVars`, `BattleUnits.GetCampaignGroup(...)`, and `BattleUnits.GetParentUnit(...)`.

```csharp
internal enum WlStrategicIntent
{
    Redeploy,
    Probe,
    Offensive,
    OffensiveContinuation,
    EngageEnemy,
    DefendCapital,
    ConstructFort,
    ConstructSupplyDepot,
    ReportOnly
}

internal readonly struct WlStrategicRoleFacts
{
    public bool WlActive;
    public bool IsPlayerAlliance;
    public bool IsPlayerCic;
    public bool IsMovedByPlayer;
    public bool IsUnderCommander;
    public bool IsPartOfPlayerUnit;
    public bool CurrentCommandIsCampaignGroup;
    public bool CurrentCommandParentIsUnderTargetUnit;
}

internal readonly struct WlStrategicOrderDecision
{
    public WlStrategicOrderResult Result;
    public int WlOrderType;
    public bool MayDirectMove;
    public bool MayMutateOperationList;
    public string Reason;
}
```

The live request can still carry Unity/runtime objects:

```csharp
internal sealed class WlStrategicOrderRequest
{
    public int AllianceId;
    public int AifactionIndex;
    public Regiment Unit;
    public Vector3 TargetPosition;
    public string TargetName;
    public int ObjectiveId;
    public WlStrategicIntent Intent;
    public float Width;
    public float Depth;
    public string SourceSystem;
}
```

`SourceSystem` is required for bounded logs and OnceLog keys, for example `wl-bridge:{SourceSystem}:{Result}:{Reason}`.

`ObjectiveId` is optional but should be provided when the request comes from a CIC plan phase. Runtimes that operate on sectors, army areas, or threats should resolve display text in this order:

1. explicit `TargetName`;
2. current `cic.ActivePlan.CurrentPhase.TargetObjectiveId` when a CIC/plan is available;
3. nearest target name from `CampaignMapLedger` / active town/IIP / asset metadata;
4. `ArmyAreaRuntime` area key or threat signature;
5. `"Objective"` only as the final fallback.

### Bridge Decision Rules

1. If W&L is inactive, fall back to the existing direct movement path.
2. If the alliance is not `GameVars.playeralliance`, fall back to existing direct movement path.
3. If `StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)` is true, skip Whiskey movement/order for that alliance.
4. If `DLC_WL.IsMovedByPlayer(unit)` is true, skip movement. Optional report-only messaging may be added later, but no direct pathing.
5. If `unit.dlcw_isundercommander` is true and the player is not CIC, first run the W&L current-order eligibility check. Only call `AIBattle.CheckCurrentOrderUpdate(..., calledfromcampaign: true)` when the vanilla chain check is expected to pass.
6. If `DLC_WL.IsPlayerPartOfUnit(unit)` is true but `unit.dlcw_isundercommander` is false, do not assume the player controls that unit. The initial slice should leave movement direct but sanitize generic dispatch type 56. A later slice may emit report-only dispatches.
7. If none of the W&L conditions apply, fall back to the existing direct path.

The eligibility check must mirror the relevant vanilla `calledfromcampaign` guard before claiming success:

```text
eligible =
    W&L active
    AND player alliance
    AND NOT player CIC
    AND unit.dlcw_isundercommander
    AND BattleUnits.GetCampaignGroup(player.currentcommand) == player.currentcommand
    AND BattleUnits.GetParentUnit(player.currentcommand).transform.IsChildOf(unit.transform)
```

After calling `CheckCurrentOrderUpdate(...)`, the live adapter should verify that `DLC_WL.GivenOrders.givenorderssession` or `DLC_WL.givenorder` actually changed. If it did not, return `FailedVanillaBridge`, not `IssuedWlCurrentOrder`.

`FailedVanillaBridge` for a W&L player-chain unit means log-and-skip. Do not fall back to direct movement for `dlcw_isundercommander && DLC_WL.dlc_scenarioactive && player alliance`; that would defeat the bridge.

The bridge returns a result enum:

```csharp
internal enum WlStrategicOrderResult
{
    NotWl,
    DirectMovementAllowed,
    IssuedWlCurrentOrder,
    SkippedPlayerControlled,
    SkippedPlayerCic,
    FailedVanillaBridge,
    WlCurrentOrderIneligible,
    ReportOnly
}
```

Operation-list mutation must be explicit per intent:

| Result / intent | Operation-list rule |
|---|---|
| `DirectMovementAllowed` + movement succeeded | caller may keep existing direct-move list mutation. |
| `IssuedWlCurrentOrder` + `Redeploy` / `Probe` / `Offensive` | do not add to `unitsinoffensiveoperations`; vanilla offensive W&L branches at 14381-14456 do not add in the order-only branch. |
| `IssuedWlCurrentOrder` + `OffensiveContinuation` | do not add; the unit should already be in `unitsinoffensiveoperations` if vanilla/Whiskey previously committed it. |
| `IssuedWlCurrentOrder` + `EngageEnemy` / `DefendCapital` | defensive list mutation is allowed only when the caller is intentionally mirroring vanilla defensive assignment semantics; vanilla defensive response still adds at 13717 after the W&L order branch. |
| `SkippedPlayerControlled`, `SkippedPlayerCic`, `FailedVanillaBridge`, `WlCurrentOrderIneligible` | do not mutate movement or operation lists. |

The bridge does not own `theaterposition`, `unitsinoffensiveoperations`, `unitsindefensiveoperations`, `groupstodefendcapital`, or other caller side effects. Caller conversions must invoke the bridge before writing movement-side-effect fields. For `ArmyAreaRuntime`, do not write `theaterposition` unless direct movement is allowed and succeeds, or unless a later plan explicitly documents a non-movement bookkeeping write.

### Intent-To-W&L Type Mapping

| Whiskey intent | W&L type | Notes |
|---|---:|---|
| `Redeploy` | 5 | Army-area return, no known nearby enemy, operational repositioning. |
| `Probe` | 5 | Initial no-contact probe should read as redeploy/recon, not mass offensive. |
| `Offensive` | 16 | Enemy/contact or plan escalation toward active objective. |
| `OffensiveContinuation` | 6 | Unit already committed to offensive operation and continuing toward next objective. |
| `EngageEnemy` | 7 | Defensive intercept, raid/landing response, or known local enemy. |
| `DefendCapital` | 8 | Capital defense only; keep existing #4 ownership. |
| `ConstructFort` | 9 | Future fort work; preserve vanilla construction gates. |
| `ConstructSupplyDepot` | 10 | Future supply-depot work; preserve vanilla construction gates. |
| `ReportOnly` | none | Use generic dispatch only after type-56 sanitization exists. |

### Order Zone Defaults

Callers that do not have a meaningful zone size should not pass `0/0`. Use these defaults:

| Intent | Width | Depth | Notes |
|---|---:|---:|---|
| `Redeploy` | 20f | 20f | Matches vanilla type 5 campaign calls. |
| `Probe` | 20f | 20f | Same as cautious redeploy. |
| `Offensive` | 20f | 20f | Matches vanilla type 16 campaign calls. |
| `OffensiveContinuation` | 20f | 20f | Matches vanilla type 6 continuation calls. |
| `EngageEnemy` | enemy width | enemy depth | Use known enemy dimensions when available; fallback 20f/20f. |
| `DefendCapital` | 20f | 20f | Matches vanilla type 8. |
| `ConstructFort` | 20f | 20f | Matches vanilla type 9. |
| `ConstructSupplyDepot` | 20f | 20f | Matches vanilla type 10. |

Do not add type 11 parent-redirect behavior to this bridge. Vanilla type 11 uses wider dimensions in battle parent-routing contexts and is outside this campaign-order slice.

### Objective-Backed Order Text

Phase 1 should use vanilla order text with the best available `destinationname`:

- resolve `CampaignObjective.ObjectiveName` when the request has `ObjectiveId`;
- fall back to target town/IIP name;
- fall back to area key, asset name, or threat signature for area/sector/defense runtimes;
- fall back to `"Objective"` only if no name is available.

This already improves W&L order text because `CheckCurrentOrderUpdate(...)` passes `destinationname` into tooltip variables.

Do not implement `AIBattle.CheckCurrentOrderUpdate(...)` Postfix decoration in the first bridge. Vanilla calls `CareerInformationPanel.ShowNewOrder(...)` inside `CheckCurrentOrderUpdate(...)`, so a Postfix mutates `DLC_WL.givenorder` after the already-open popup has been built. Transpiler-based pre-display decoration is out of scope, and re-showing the panel after mutation risks duplicate UI behavior. Keep Phase 1 to argument quality (`destinationname`, type, zone), then revisit richer copy only after runtime proof.

### Generic Dispatch Usage

Use generic `Messages` dispatches for reports, not orders:

- "your parent command reached the objective";
- "high command reports enemy contact near the plan objective";
- "the army is regrouping after a failed probe";
- "orders are unchanged."

Before adding report-only dispatches, add the type-56 sanitization patch below. Otherwise Whiskey will be building on top of the same broken wording surface.

## Required Patch Surfaces

### 1. Dispatch Stance Sanitizer

Preferred narrow surface: `Messages.Message.GenerateMessageContent()` Postfix.

Rules:

- only W&L active;
- only player-alliance sender/regref that passes W&L player-chain semantics;
- only when the rendered content contains the known stance-0 order templates, such as `"instructions that are to none"` or `"I will none if no other orders are received"`;
- cover type 56 first, and include type 15 / 57 if they render the same stance-0 text under the same W&L player-chain conditions;
- replace only the bad order sentence with neutral hold/await wording.

Alternative surface: `Messages.Add(...)` Prefix that suppresses affected messages under the same conditions. Suppression is safer for noise, but loses "arrived" information. Sanitization is better if we want to use dispatches as a strategic report layer.

Do not edit `GameVars.groupstancename[0]`.

### 2. Strategic Order Bridge Helper

Add the bridge as pure strategic infrastructure. It may use reflection for W&L methods, but it should catch exceptions and log a bounded warning. It should be testable with a pure classifier where Unity objects are replaced by simple role flags.

### 3. Caller Conversion

Convert call sites one at a time:

1. `OperationalProbeRuntime`: bridge `Probe`, `Offensive`, and `OffensiveContinuation` before direct `MoveUnitTo`.
2. `ArmyAreaRuntime`: bridge `Redeploy` before direct return-area movement.
3. `CoastalDefenseCustomOrderRunner`: bridge `EngageEnemy` before direct custom defensive movement.
4. `CheckForDefensiveOperationsCandidateFilterPatch`: audit reverts separately; avoid direct movement for W&L-under units unless the player is CIC or the unit is outside player-chain semantics.

Each conversion must preserve non-W&L behavior and opposing-AI behavior.

Do not include `ArmyGroupManagementPatch` in the first bridge implementation. Army-group membership affects command hierarchy rather than movement orders, so it needs a separate role matrix for embedded player formations, independent divisions/corps, and CIC promotion states.

### 4. Optional Objective Order Decorator

Deferred. Do not plan this as a Postfix on `CheckCurrentOrderUpdate(...)` unless the implementation also proves how the already-open `CareerInformationPanel` is refreshed without duplicate popups. The bridge should first exhaust safer improvements through `destinationname`, order type, and zone sizing.

### 5. Diagnostic-Only MoveUnitTo Observer

Do not add a global behavior patch on `AICampaign.MoveUnitTo(...)`. A diagnostic-only Postfix observer may be considered later as an audit tool if bridge conversion misses call sites. It must not block or redirect movement; it should emit bounded warnings only for W&L player-alliance `dlcw_isundercommander` units moved by unbridged strategic code.

## Acceptance Criteria

Global acceptance for the full bridge slice:

- Starting as a subordinate in a W&L career does not create a fake command. The current-order button may still show "no specific orders" until a real AI commander order is issued.
- When the AI commander's Whiskey plan issues a campaign order to the player's command chain, the W&L current-order UI shows a valid vanilla order type, target zone, commander portrait, and objective-derived name.
- No W&L player-chain strategic executor directly calls `AICampaign.MoveUnitTo(...)` for `dlcw_isundercommander` units after the C0c call-site conversion ships.
- Generic campaign dispatches no longer say the unit is carrying out orders to `"none"`.
- Player-CIC alliances remain unsteered by Whiskey strategic movement/order bridges.
- Non-W&L campaigns and opposing AI campaign movement remain behaviorally unchanged.
- Logs show bounded bridge decisions and vanilla `checking new player AI order` / `adding new player AI order` lines when orders are emitted.

C0a dispatch-sanitizer acceptance:

- A fresh or live W&L campaign no longer shows `"to none"` / `"I will none"` in generic dispatch content.
- Saved-message behavior is understood: newly generated messages are sanitized; old saved literal content may remain unchanged.
- No current-order bridge behavior is claimed by C0a.

C0b classifier acceptance:

- Pure classifier tests cover W&L inactive, non-player alliance, player CIC, moved-by-player, under-commander eligible, under-commander ineligible, part-of-player-unit-not-under-commander, and failed-bridge outcomes.
- No runtime movement conversion is claimed by C0b.

## Verification Plan

Implementation plan should require:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

For DLL-affecting implementation, deploy and verify the DLL hash:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

The two SHA-256 hashes must match before asking for game smoke.

Runtime smoke:

1. Start a fresh W&L career as a low-ranking subordinate.
2. Let campaign time advance until the AI commander issues movement/defense/offensive orders.
3. Confirm no dispatch contains the `"orders ... none"` wording.
4. Confirm current-order UI shows no order until a real W&L order is emitted.
5. Confirm emitted W&L order has a valid type and objective/target name.
6. Promote or select an independent command if possible and confirm Whiskey still does not direct-move the player-controlled command.
7. Promote to CIC or start CIC state and confirm Whiskey skips player-alliance steering.

Suggested log probes:

```bash
rg -n "checking new player AI order|adding new player AI order|msg_reachedfinalwaypointcamp|to none|WlStrategicOrderBridge|OperationalProbe|Patch:ArmyArea|DefenseIntent" \
  "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

## Not Verified Yet

- The user's observed popup was not reproduced live in this pass. Type 56 is the strongest code-backed cause, but types 15 and 57 have similar stance-text sentences and should be included in the first runtime log/screenshot confirmation.
- Rich order-content decoration is intentionally deferred. A `CheckCurrentOrderUpdate(...)` Postfix is not sufficient by itself because vanilla shows the popup before the Postfix would run.
- Whether vanilla W&L order types 5/6/7/16 always produce the desired prestige and completion semantics for independent division/corps/army player commands needs a focused promotion-state smoke.
- Whether type 16 should remain Whiskey's main "Offensive" bridge type despite lacking an explicit `EarnPrestige` switch case needs runtime confirmation.
- Generic `Messages` report-only dispatches should not be added until the sanitizer has a live smoke pass.

## Documentation Deltas When Implemented

- A sanitizer implementation gets the next patch ordinal in `docs/patch-catalog.md`.
- The bridge helper should be listed in `docs/patch-catalog.md` as a coordinator/helper/runtime row only after a shipped runtime uses it.
- `docs/handoff.md` should mention the active W&L dispatch/objective bridge workstream when implementation starts and record smoke status when it ships.
- `MEMORY.md` should receive only the durable conclusion after runtime proof, not speculative design notes.

## Implementation Slicing

Recommended plan sequence:

1. **C0a Dispatch Sanitizer And Diagnostics**: standalone user-visible fix for stance-0 dispatch text, with bounded diagnostics for message type and sender role.
2. **C0b Strategic Order Bridge Classifier**: add pure classifier/tests and reflection bridge, but convert no movement call sites yet.
3. **C0c Runtime Call-Site Conversion**: convert operational probe, army-area return, and custom defense one at a time.
4. **C0d Objective Text Enrichment**: use objective names in vanilla order requests; optionally decorate current-order content if runtime proof supports it.

C0a should ship first by itself. It is the smallest slice with a direct fix for the reported `"none"` symptom. Do not merge C0b into C0a unless there is a specific reason to take bridge-classifier risk before the user-visible dispatch bug is fixed.
