# Findings — Grand Tactician: The Civil War

Decompile-time discoveries, key code coordinates, and reflection gotchas. Source of truth for "where does X live" questions.

## Game architecture

- **Engine:** Unity **2021.3.16f1** Mono, **64-bit** (`UnityPlayer.dll`, `mono-2.0-bdwgc.dll`, `The Civil War (1861-1865).exe` are all PE32+ x86-64).
- **Trap:** `Assembly-CSharp.dll` shows as `PE32 Intel 80386` per `file(1)` because .NET MSIL is marked any-CPU. The runtime is x64 — use BepInEx **x64** UnityMono.
- **No IL2CPP, no obfuscation.** Method/field names intact and readable.
- **Two modding surfaces both already exposed:**
  - **Data layer** — `Modding/ModdingTool_1.11.xlsm` (Excel editor for units/resources/buildings/weapons) + 14 plain-text `Config/*.txt` + `Config/*.dat` files. `aistrategies.dat` covers 283 battlefields. `battleprefs.txt` and `campaignprefs.txt` are key/value-on-alternating-lines.
  - **Code layer** — `Assembly-CSharp.dll` (3.5 MB). Decompile + Harmony.

## Decompilation

```bash
mkdir -p /tmp/gt_src
cp "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/The Civil War (1861-1865)_Data/Managed/Assembly-CSharp.dll" /tmp/gt_src/
cd /tmp/gt_src
dotnet /tmp/ilspy/tools/net8.0/any/ilspycmd.dll Assembly-CSharp.dll -o ./asm
```

Result: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` — 266,136 lines for Steam build 14703111 (the 2026-10-14 update). Line numbers in this doc, `docs/vanilla-vision-and-movement.md` and `docs/wl-player-model.md` refer to that build.

`/tmp` is volatile — re-run when the host is rebooted or `/tmp` cleaned.

## Class map of the AI

All line numbers are in `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

| Class | Lines | Range | Purpose |
|---|---|---|---|
| `AICampaign` | 20,904 | 8775 – 29679 | strategic / campaign brain — recruitment, supply, theaters, invasions, perk pick, officer replacement |
| `AIBattle` | 6,120 | 2655 – 8775 | battlefield brain — micro AI, charges, reserves, flanks |
| `AI_Unit` | 672 | 1983 – 2655 | per-unit AI state |
| `AI_Manager` | 31 | 1952 – 1983 | top-level coordinator |
| `DLC_WL` | 7,996 | 40337 – 48333 | Whiskey & Lemons DLC subsystem |
| `GamePrefs` | 10,477 | 48334 – 58811 | static config — ~all AI thresholds load from `Config/*.txt` |
| `CampaignStrategy` | — | 179640+ | strategy persistence |

## Critical decompile coordinates

### Strategic decision points (AICampaign)

| Method | Line | Notes |
|---|---|---|
| `PickCampaignObjective` | **17770** | **Picks objective with `Random.Range`** — root cause of "AI has one tactic"; replace with weighted scorer |
| `UpdateImportanceValues` | 14905 | Weighted importance per zone (transport capacity, profit, population, capital, objective bonus) |
| `CalculateMostValueableAIZones` | 10965 | Picks single highest-importance zone within distance gate |
| `GetBestRecruitingState` | 10723 | Theater-keyed recruiting state pick |
| `GetClosestStateWithRecruitsPotential` | 10836 | Closest-with-potential variant |
| `GetAllStateWithRecruitsPotential` | 10858 | All-with-potential variant |
| `UpdateSupplyCapacity` | 10772 | Per-zone supply capacity from IIPs + supply depots |
| `CheckSupplyDepotConstruction` | 14659 | AI builds supply depots |
| `CheckFortConstruction` | 16347 | AI fort construction; one active order per faction but no area cap once orders finish |
| `CheckTransferOfUnits` | 17232 | Force consolidation gate |
| `CheckArmyGroupManagement` | 17706 | High-level army organization |
| `AssignUnitToDefendCapital` | 11668 / gate at 11791 | Capital-defense assignment; older notes called this `CheckPickDefensiveOps`, but that method name does not exist in current decompile |
| `CheckForDefensiveOperations` | 13505 | Field defensive operations trigger and unit grouping |
| `CheckPerkSelection` | 11871 | AI commander perk picks |
| `CheckSelectionSingleBrigadePerk` | 17690 | Single-unit perk picks |
| `CheckAICommanderReplacements` | 17009 | Fallen-officer replacement |
| `CheckForSeaInvasion` | 16408 | Amphibious-op triggers |
| `AddUnitToInvasionForce` | 9358 | Unit assignment to invasion force |
| `AdjustOffensiveOperationsSpeed` | 16973 | Tempo of offensive operations |

### Tactical decision points (AIBattle)

| Method | Line | Notes |
|---|---|---|
| `CheckGlobalAIStrategy` | 6314 | Macro-AI stance set; preset from `aistrategies.dat`; has 1% casualty hardcode |
| `AdjustGroupAIStance` | 4222 | Group stance ladder (none/screen/defend/attack/assault) |
| `MicroAICheckForCharges` | 4905 | Meant to arm a charge at `aigroup.ai_stance == 4`; dead (4910 compare, `BUG-TAC-016`) |
| `CheckForFeudGroupActions` | 4931 | **Feud auto-charge — does NOT call `PerformAIActionDLCWL`** (the brigade-magnetization-into-melee bug) |
| `AssignReserves` | 7018 | Reserve assignment driver |
| `AssignReserveToOperationalGroup` | 7154 | Reserve → group |
| `LinkReservesToLineGroup` | 6643 | Reserve linking |
| `CheckLineFallbacks` | 5117 | Line-unit fallback decisions |

### Tactical doctrine command/writer anchors

These anchors back the operations-ledger and full-spectrum doctrine consumers. Current behavior lives in `docs/tactical-operations-ledger.md`; bridge/ford/road/town typed tactical POI enumeration remains unverified and must not be treated as a clean vanilla API.

| Symbol | Line | Notes |
|---|---|---|
| `AIBattle.CheckAIBombardment` | 3869 | Vanilla bombardment for groups at stance 3/4; the battle core's ArtilleryDoctrine producer cancels/switches a running bombardment (9) for every stance and leaves the bombard order itself to vanilla. |
| `AIBattle.AdjustGroupAIStance` | 4222 | Vanilla stance ladder, still the immediate stance owner; the GroupStance producer runs right after it in the same `UpdateAITasks` (5869 -> 5870). |
| `AIBattle.MicroAICheckForCharges` | 4905 | Dead in vanilla: the 4910 compare never matches, so it initiates no charge (`docs/bug-fixes/vanilla-tactical-battlefield.md` `BUG-TAC-016`). The former #41 charge gate on it was deleted (e7214b6). |
| `AIBattle.AdjustGroupFormations` | 5875 | #68 battle-core arbiter Postfix (called at 5870, after vanilla's movers); the posture producers run here. |
| `AIBattle.CheckUseOfReserves` | 6062 | #59 reserve gate and #56 courier parity; the direct reserve move is at 6170. |
| `AIBattle.CheckGlobalAIStrategy` | 6314 | Live vanilla macro-AI strategy writer; Whiskey operation selection must have an explicit boundary here rather than racing another macro writer. #44 `BattleMacroStrategyPatch` is the single owner: a value-restoring Prefix plus a Postfix that writes the AIBattle instance `macroai` (never 3), which `UpdateAITasks` (5857) then feeds to `AdjustGroupAIStance`. The former `BattleMacroAIOverridePatch`, which wrote the persisted `sideinformation[side].macroai`, was deleted (8b09cf3). See "Vanilla anchors used in the 2026-10-14/15 remediation" for 6378-6438. |
| `BattleUnits.GetBattleType` | 86095 | Vanilla scenario-stance signal. Returns 0 (meeting) / 1 (we attack) / 2 (we defend) computed from `sideinformation[side].objectivepointsheld` ratio vs `GamePrefs.objectiveratiodefend`. Stable across the battle unless objective ownership flips. **L (2026-05-23) reads this** via `ArmyEvidenceBuilder.ReadBattleStance` → `TacticalForceBalanceEvidence.Stance` (`BattleStance` enum) for the doctrine + playbook hard gate. Mirrors Scourge `OpenType()` + `Rank(eRankSide)` pattern (`sdk/SowAiInf/offai.cpp:1151-1159`). |
| `AIBattle.AssignReserves` | 7018 | Vanilla reserve grouping surface; reserve-list mutation must snapshot/restore and never invent membership. |
| `BattleUnits.ChangeStance` | 90772 | Vanilla stance API behind `TacticalStanceWriter` (the battle-core executor's Stance channel). |
| `BattleUnits.SetWaypoint(Regiment, ...)` | 91232 | Vanilla movement-order API the battle-core executor uses (producers, #42/#56 inline). |
| `BattleUnits.SetGroupFormation(Regiment, ...)` | 91822 | Vanilla command/group-formation API; returns immediately for `unittyp <= 13`. |
| `Regiment.RegimentSetPath` | 130966 | Calls `StopRegiment()` before creating a new path. Reserve-deny rollback must restore movement state when this path was created by vanilla. |
| `Regiment.RegimentSetPath` | 131098 | Writes `unitwhogavelastmovingorder`; rollback must avoid leaving a stale order owner. |
| `Regiment.RegimentSetPath` | 131130 | Updates cover/target bookkeeping before waypoint creation. |
| `Regiment.RegimentSetPath` | 131250 | Writes final waypoint position/rotation. |
| `Regiment.RegimentSetPath` | 131267 | Updates movement bookkeeping after path creation. |

### Tactical deployment, pathing, terrain, and facing

These anchors back #53 pathfinder discipline, #58 deployment terrain/facing telemetry, and #60 deployment terrain discipline. Current runtime behavior lives in `docs/tactical-terrain-facing-discipline.md`.

| Symbol | Line | Notes |
|---|---|---|
| `BattleUI.CheckPathSetting` | 168982 | Manual tactical pathing raycasts `NavTarget` and creates `SetWaypointData(... manualfinalrotation: -1f ...)`. |
| `BattleUnits.SetWaypoint` | 91304 | Group deployment mode clamps move targets through `frontline2.GetClosestPointInDeploymentZone(...)`, then calls `RegimentSetPath`. |
| `BattleUnits.SetWaypoint` | 91453 | Deployment mode directly positions land units or calls `SetGroupFormation(...)`, then checks water and deployment-zone state. |
| `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew` | 85524 | Native AI deployment placement loop used by #58 telemetry and #60 terrain discipline. |
| `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew` | 85873 | Vanilla final cleanup clears `lastsetwaypointposition` and `immediateunitplacement` on active units. #60 mirrors only these fields after a correction. |
| `BattleUnits.SetGroupFormation(GameObject, ...)` | 91815 | Public formation placement overload used by #60 instead of custom positioning. |
| `BattleUnits.SetGroupFormation` | 92056 | Immediate placement writes `lastsetwaypointposition` / `lastsetwaypointrotation` and walks attached units. |
| `Regiment.AddPath` | 130259 | Native NavMesh area-cost and path calculation surface guarded by #53. |
| `Regiment.RegimentSetPath` | 131073 | Final tactical target terrain-id-4 clamp and target height. |
| `Regiment.RegimentSetPath` | 131167 | Retry loop can step away from target via `Vector3.MoveTowards(..., -0.5f)`. |
| `Regiment.RegimentSetPath` | 131211 | Final waypoint rotation uses supplied `manualfinalrotation`, otherwise `GetLastWaypointAngle()`. |
| `Regiment.SetNewPosition` | 131303 | Position/facing setter called by vanilla formation placement. |
| `Regiment.CheckIfPositionIsOnWater*` | 131432 | Vanilla water correction primarily checks terrain id `4`. |
| `BattlefieldSetup.CheckTerrainLine` | 27638 | Terrain-id line test; Whiskey's only caller is `WaypointValidatorRuntime` (in-contact withdrawal line, `{4, 8}`, 2 m steps). |
| `Regiment.UpdateUnitRangeFast` | 122545 | Visible enemy lists are fog-checked; closest-enemy fields alone are not enough for Whiskey facing. |
| `Regiment.UpdateFlanking` | 122989 | Filters scouts/cavalry/melee-style contacts for comparable enemy/flank reasoning. |
| `BattleUnits.Grp.IsApplicableForDeployment` | 78275 | Vanilla deployment eligibility guard mirrored by #60. |

### W&L gates

| Symbol | Line | Notes |
|---|---|---|
| `DLC_WL` class | 40337 | 7,996-line subsystem |
| `DLC_WL.dlc_scenarioactive` | static field | Master gate for all W&L behavior |
| `DLC_WL.dlc_chosencommander` | static field | Player's chosen officer index |
| `DLC_WL.IsCommanderInChief()` | method | Whether player is the CIC (vs subordinate) |
| `DLC_WL.IsMovedByPlayer(unit)` | method | Whether unit was player-commanded |
| `DLC_WL.givenorder` | static field | Player's last given order (struct `GivenOrders`) |
| `unit.dlcw_isundercommander` | bool field | Per-unit "under player's commander" flag |
| `PerformAIActionDLCWL(unit, group)` | **5101** | Per-unit gate — returns false to skip AI action when `dlc_scenarioactive && dlcw_isundercommander && group.ai_stance < 1` |

### Resupply / supply

| Symbol | Line | Notes |
|---|---|---|
| `ResupplyAllUnits` | 86735 | The actual resupply algorithm |
| Caller (EOD) | 86429 | End-of-day cycle resupply |
| Caller (battle init) | 86655 | Full-supply override at battle start |
| `IsNightTime()` | 115383 | Day/night gate |
| `supplystate = 0f` (running/doublequick) | 114264 | Suppresses resupply when unit moving |
| `supplystate = 0f` (no IIP/depot in range) | 114311 | Suppresses resupply when out of range |

### Game-prefs config files

These plain-text key/value files at `<GTCW>/Config/` populate `GamePrefs` static fields at runtime:

- `campaignprefs.txt` — ~all `aiimportance*`, `nonwarparticipants*`, `aitownweightfactorobjective`, `zoneimportancefactornoenemy`
- `battleprefs.txt` — ~all `strengthtriggerfor*_micro[]`, `probfeudgroupmovement`, `chanceoffeuds*`, `neededdistancefeudgroupmovement`, `timetorenewaichargecheck`
- `aistrategies.dat` — 283 battlefield-keyed objective sequences (per-battle macro-AI preset)
- `unitprefs.txt`, `prefs.dat` — unit and global tuning

**Key insight:** ~30% of community-flagged "AI bugs" are tunable from these text files without touching the DLL. The `GamePrefs` class is line 48334 in the decompile and contains ~10,477 lines of static fields.

### Stance enum

`GameVars.groupstancename` (line 65061):
```csharp
new string[5] { "none", "screen", "defend", "attack", "assault" };
//                0        1         2         3        4
```

`macroai` (battle-level):
- 0 / 1 = offensive variants — can assault & screen
- 2 = defensive only
- 3 = retreat (set by `VictoryBarBelowTrigger`)

### Perk id tables

`Regiment.HasPerk` (132765) / `GetPerkLevel` (132719) compare raw ids; the table is chosen by unit type (`PerkRow.GetPerksShown` 208802): `unittyp <= 13` battle (`PerkBattleTooltips.txt`, 1 slot), 17 fleet (`PerkFleetsTooltips.txt`), every other type campaign (`PerkTooltipsArmyCampaign.txt`, 4 slots). Battle HQs keep their campaign perks (`import_units` 88579-88632). `HasPerkParent` (132748) answers only for a campaign corps (15) asking its army.

| Id | Battle | Campaign | Fleet |
|---|---|---|---|
| 0 | Engineering | Siege Train | Torpedoes |
| 1 | Sharpshooters | Field Telegraph | Mortar Boats |
| 2 | Zouave | Balloon Corps | Supply Colliers |
| 3 | Expert Skirmishers | Bureau of Military Information | Balloon Barge |
| 4 | Spirited Charge | Skilled Cartographers | Limelights |
| 5 | Deadly Volley | Flying Column | Blockading Squadron |
| 6 | Ace of Spade | Foot Cavalry | Sea Raiders |
| 7 | Iron Discipline | Engineers & Mechanics | Amphibious Attack |
| 8 | Expert Scouts | Ambulance Corps | Blockade Busters |
| 9 | Cold Steel | Pontoon Train | Battery Runner |
| 10 | Mounted Rifles | Sappers and Miners | Port Defender |
| 11 | Stormers | Land Torpedoes | |
| 12 | Vanguard | Partisan Brigades | |
| 13 | Lightning Brigade | Bushwhackers | |
| 14 | Close Support | Embedded Reporters | |
| 15 | Skilled Laying | Limelights | |
| 16 | Grand Battery | Expert Scouts | |
| 17 | Flying Battery | River Expedition | |
| 18 | Sapper Gunners | | |

Full call-site audit and the wrong-table reads: [bug-fixes/vanilla-perk-ids.md](bug-fixes/vanilla-perk-ids.md). Pure rule: `Strategic/VanillaPerkTables.cs`. The perk-id Transpilers' IL edits are in `Patches/PerkIdIlRewrites.cs` and the swap state in `Patches/PerkSwaps.cs` (both compiled into the harness); the harness (`PerkIdIlTests`) runs them on the real `refs/Assembly-CSharp.dll` IL, so a game update that moves a site fails a test instead of silently patching the wrong call. `PatchProcessor.ReadMethodBody` works on .NET 8 for reading IL; applying a HarmonyX 2.10 patch under .NET 8 segfaults (and its emitter fails to initialise), so patched methods cannot be JIT-checked there. Instead the edits computed by `PerkIdIlRewrites` on the real IL were applied at the same instruction positions to a Mono.Cecil copy of `Assembly-CSharp.dll` calling the real `PerkIdIlHooks`, and the five methods passed ILVerify 8.0.0 (`dotnet tool install --tool-path <dir> dotnet-ilverify --version 8.0.0`; reference the game's `Managed/*.dll` minus `Assembly-CSharp.dll`, plus `dist/WhiskeyRealism.dll`; name the edited file `Assembly-CSharp.dll`). A deliberately wrong hook was flagged (StackUnderflow), so the check has teeth. HarmonyX `AccessTools.FieldRefAccess` also fails under .NET 8 (same emitter), so `FogPerkSightPatch` uses expression-compiled getters (`CompiledFieldGetter`), which the harness can run.

## Reflection gotchas

Collected during v0.2.0 / v0.2.1 / v0.2.1.1 smoke-testing. Pattern: many vanilla methods have default-valued tail parameters; `AccessTools.Method` lookup must include them or returns null.

| Method | Wrong (single-arg) | Correct |
|---|---|---|
| `DLC_WL.IsCommanderInChief` | `()` | `(int manualcommander = -1)` — pass `new object[] { -1 }` |
| `CheckBox.Check` | `(bool)` | `(bool newstate = true, bool manuallyset = false)` |
| `Town.GetTownFromName` | `(string)` | `(string name, string statename = "")` |
| `CampaignObjective.GetAvailableObjectives` | `(int)` | `(int allianceid, bool includeaccomplished = false, int mintownobjectives = 1)` — pass `mintownobjectives=0` to allow abstract objectives |
| `FilterMap.GetColorOnPos` | `(Vector3)` | `(Vector3 position, float overridealpha = -1f)` |
| `BattlefieldSetup.GetStateOfField` | `(Vector3)` when using reflection with explicit args | `(Vector3 position, bool usecloseststate = false)` — pass `true` for campaign-map commander-position checks |

**State-location gotchas** (where vanilla actually stores things):

| What | NOT here | Actually here |
|---|---|---|
| Current campaign year | `GameVars.year` (declared but **never assigned**) | `BattleUnits.year` (instance field, set on scenario load at line 25326). Resolve via `GameObject.Find("GameController").GetComponent<BattleUnits>().year`. |
| Current campaign month | `GameVars.currentmonth` (sometimes set, sometimes 0) | `bunits.uniStormSystem.monthCounter` (1-based) |
| Save folder location | `Application.persistentDataPath` | **Game install dir (CWD)**. Vanilla `SceneManagement.SaveAll` calls `Directory.CreateDirectory("Campaigns/<level>/<sublevel>/<save>/")` with a relative path that .NET resolves against CWD. Sidecar should use the same convention. |
| `Policy.CurrentChapter` initial state | Set by scenario start | Initialized to `-1` (line 29857). Updated by `Policy.CheckForChapterUpdate()` (line 211604) which runs from a per-day cycle. **Must invoke manually** if patch fires before per-day cycle has ticked (typical on fresh-campaign first frame). For W&L scenario "002", CheckForChapterUpdate unconditionally sets `CurrentChapter = 1`. |
| `BattleUnits.armygroups` populated | At campaign start | Only after vanilla AI promotes a commander to army-group rank (typically meaningful game time). `Commander.IsArmyGroupCommander() == true` requires this. |
| Vanilla CampaignObjectives for W&L "002" | All 38 from `Config/campaignobjectives.dat` | Only the subset whose `ObjectiveScenario` list contains "002" (~19 of 38). Many are abstract win-conditions; default `mintownobjectives=1` filter excludes them — pass `0`. |

**Operational-tempo anchors (added 2026-05-05):** vanilla already spreads strategic operations through `AICampaign.UpdateUnitAI()` (line 11304), maintains `unitsinoffensiveoperations`, clears failing operations in `RelieveUnitsInOperations(int)` (line 14068), gates candidates in `IsUnitAvailableForOffensiveOperations(...)` (line 14080), and commits offensive movement in `CheckOffensiveMovements(int, Regiment, float)` (line 14166). Its offensive probability formula uses `GamePrefs.aiagressiveness[Policy.CurrentChapter]` at line 14339 along with `timediff`, faction aggression, campaign aggression, winter modifier, and `timeofnotmoving`. Whiskey's `OperationalTempoDoctrine` intentionally layers on that substrate instead of replacing it; see `docs/operational-tempo-doctrine.md`.

**Fort-construction gotcha (added 2026-05-05):** vanilla `AICampaign.CheckFortConstruction(int)` (line 16347) prevents more than one active fort order per faction via `FortConstructionOrder.IsFactionAlreadyConstructingFort`, and checks existing forts by `GamePrefs.maxdistancetootherfort`. That is not an area cap. Once a fort finishes, the next static `fortconstructionsites` candidate can be used. Capital-defense units use a looser `GamePrefs.maxdistancetootherfort * 0.6f` spacing multiplier, which can produce dense Washington/Richmond fort clusters over repeated completed orders. Whiskey #27 `FortConstructionGovernorPatch` filters saturated `fortconstructionsites` for the current vanilla call, then restores the list; see `docs/fort-construction-governor.md`.

**Army-group steering note (added v0.2.2):** vanilla creates/attaches army groups in `AICampaign.CheckArmyGroupManagement` (line 17705) from top units with non-default `Regiment.theaterposition`. Whiskey #16 `ArmyGroupManagementPatch` now runs after vanilla and uses vanilla `ArmyGroup.AddUnit`, `ArmyGroup.CreateNewArmyGroup`, and `ArmyGroup.AppointCommander` only after the weekly `ArmyAreaLedger` identifies at least two eligible top formations in the same historical operating command. Preferred commanders are appointed only when already attached to that group or unassigned; the patch does not yank unrelated commands across the map.

**BepInEx-specific gotcha:** `Config.Bind("[General]", ...)` throws `ArgumentException`. BepInEx 5.4 forbids `[` `]` in section names; it adds the brackets when writing the .cfg file. Pass `"General"`, not `"[General]"`. Plugin Awake exceptions land in Unity's `Player.log` (`<persistentDataPath>/Player.log`), NOT `BepInEx/LogOutput.log` — always check both when diagnosing silent failures.

## Tactical Operations Objective Anchors

- `AIBattle.objectivechain`: confirmed readable as `public List<ObjectiveChain> objectivechain` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:3282`; `ObjectiveChain.objectives` is a `List<Objectives>` at lines 2966-2970. Current implementation should treat this as an initial generic objective source only. Existing #35-adjacent read paths already reflect the objective-chain field (`src/WhiskeyRealism/Tactical/Orchestrator/ArmyEvidenceBuilder.cs`) and observe objective movement without mutation.
- `Regiment.currentsetobjective`: confirmed readable as `public Objectives currentsetobjective` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:111076`; vanilla assigns it from `objectivechain[i].objectives[0]` at line 6963 and uses the objective name for current-order updates at lines 6994-7002. Existing #35 reflection reads the field in `TacticalObserverPatch.SafeCurrentObjectiveId`, mapping command groups to vanilla objective references when present.
- `Objectives : MonoBehaviour`: confirmed at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:105210`; it exposes `owner` and `objectivename`, and its transform is the movement target vanilla uses from `objectivechain[i].objectives[0]` at lines 6940-7002. `TacticalVisionRuntimeAdapter` may scan live scene `Objectives` components as verified map-objective anchors before using any movement-anchor fallback.
- `EntryPoint` route prediction: confirmed at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:98847`; it exposes `entryangle`, `entrypointposition`, `trooptargetposition`, `owner`, `closestdistancefordeployment`, and `entrypointname`. `EntryPoint.CalculateTransform` at lines 98969-98976 snaps the entry point and troop target against nearby `Road` / `Railroad` tags. `BattleUnits.entrypoints`, `BattleUnits.scheduledarrival`, and `BattleUnits.grp` are public instance fields at lines 78944-78974; scheduled arrivals are assigned an `EntryPoint` at lines 87834-87841 and `SetInitialPathForEntryPoint` moves units from `entrypointposition` toward `trooptargetposition` at line 87967. Whiskey uses these as approach-avenue anchors for battle-line orientation; no vanilla single "predict enemy route" API was found.
- Bridge/ford/road/choke enumeration: no clean typed battle-objective enumeration API found in the current decompile. Partial crossing anchors exist: `BattlefieldSetup.rivercrossingpositions` is declared at line 24723, populated from NavMesh areas including `"Bridges"` and `"River Crossings"` at lines 26614-26637, and consumed by `AIBattle` blocked-crossing sampling at lines 7932-7967. Road hits in this scan are EasyRoads/editor/campaign-railroad surfaces, not a confirmed tactical POI source. Do not use typed Bridge/Ford/RoadJunction/ChokePoint scoring from these anchors alone; first implementation must classify them as generic positions unless a verified scene object/source is added. The 2026-05-15 defensive corridor fix uses only bounded crossing evidence from `BattlefieldSetup.SearchTerrainInRangePos(..., new[] { 4, 8 }, ...)` around an already-selected approach avenue to narrow/shift the defensive line; it still does not claim a typed bridge/ford objective API.
- Terrain safety: use existing #58/#60 terrain/deployment sampling until a broader terrain source is verified. `BattlefieldSetup.TerrainShape` exists at lines 24121-24139 and current #58/#60 terrain/facing discipline remains the bounded source for terrain safety.

## Regular Campaign Command Movement Anchors

- Regular campaign formations are still instances of the vanilla `Regiment` component. Do not infer literal battlefield-regiment scope from the C# class name. `CampaignArmyPanel` labels `overridesymbol` values at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:219088-219100`: `13` Brigade, `14` Division, `15` Corps, `16` Army. `unittyp > 13` is the confirmed `SetGroupFormation` command/group-formation guard, not a complete echelon classifier by itself. Effective command tier also depends on hierarchy depth, `GamePrefs.commandhierarchyshift`, `overridesymbol`, and attached-unit structure.
- Manual regular campaign/battle right-click movement is `BattleUI.CheckPathSetting()` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:168980`, which creates `SetWaypointData` at line 169019, then `BattleUI.CheckGroupRotation()` consumes it and calls `BattleUnits.SetWaypoint(...)` at lines 166132 and 166163.
- `BattleUnits.SetWaypoint(Regiment, ...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:91232` is the regular movement-order API. It has explicit `IsCampaign` branches, readiness checks, order-delay handling, and calls `SetGroupFormation(...)` for command nodes where `reg.unittyp > 13` at line 91559.
- `BattleUnits.SetGroupFormation(Regiment, ...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:91822` is the vanilla group/command-node formation API. It returns immediately for `unittyp <= 13`, then walks the attached hierarchy with `GetHierarchyTree(...)` at lines 91988-91999.
- `BattleUnits.GetHierarchyTree(Regiment, ...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:92720` reads direct attached units and builds the vanilla command tree used by group formation movement.
- `AIBattle.CheckCurrentOrderUpdate(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8233` is not a regular campaign movement anchor. It returns immediately unless `DLC_WL.dlc_scenarioactive` is true, then creates `DLC_WL.GivenOrders` and career-panel order messages. Treat it as W&L current-order/message machinery only.

## Vanilla anchors used in the 2026-10-14/15 remediation

Anchors cited by the remediation commits (`git log 262a50d..`) and checked against build 14703111. Vision, fog, pathing and campaign movement live in [`vanilla-vision-and-movement.md`](vanilla-vision-and-movement.md); the W&L player, dispatches, the three AIBattle instances (85203-85212), `UpdateAITasks` (5857), `ai_vs_ai` (29787-29793), the hierarchy −1 pause (11368) and `permanentlydetached` live in [`wl-player-model.md`](wl-player-model.md). They are not repeated here.

| Area | Symbol | Line | Why it matters |
|---|---|---|---|
| Battle clock | `BattleUnits.SetTimeVariables` | 93545 | `currenttimefromstart = Hour + battlepasseddays * 24`; the doctrine clock is derived from it. |
| Battle clock | `BattleUnits.CheckTimeIssues` | 86369; 86382-86384 | End-of-day `eodcycle` machine; `battlepasseddays++` happens mid-night, so the raw clock jumps ~9 h. |
| Battle clock | morning deployment panel close | 164874-164882 | Clears `deploymentmodeactive` and resets `eodcycle = 0`. |
| Battle clock | `BattleUnits.IsCampaign` | 78782 | Campaign map uses the same component; its clock is not battle time. |
| Battle lifecycle | `AIBattle.bunits` / `AIBattle.Start` | 3306 / 3354 | Static `BattleUnits` cache, set per battle in `Start`. |
| Battle lifecycle | `AIBattle.SetSide` | 3408 | Sets the instance side and the feud flag (`isplayeraiorfeud == 2`). |
| Battle lifecycle | `BattleUnits.OnDestroy`; `UpdateUnitList`; `BattleUnits.objectives` | 79207; 79974; 78910 | Battle teardown and per-battle unit/objective rebuild; caches must key on battle identity. |
| Battle lifecycle | `BattleUnits.CalculateSideStatsAndUpdateAITasks` | 84570 (84866) | `aibattle[0].UpdateAITasks()` runs first. |
| Macro AI | `AIBattle.CheckGlobalAIStrategy` | 6378-6386 | `GameVars.aistrategy >= 0` and `sideinformation[side].macroai >= 0` are scripted overrides that return early. |
| Macro AI | same | 6388; 6438 | Timer revert of `macroai` to `newmacroaitoset` each cycle, and the timer re-stamp while `newmacroaitoset >= 0 && macroai <= 0`; why #44 restores vanilla's value before vanilla runs. |
| Macro AI | `sideinformation.macroai` save / load / scenario | 74819 / 74689 / 88254 | Persisted and scenario-scripted; a value Whiskey wrote cannot be told apart from a scripted one. |
| Stance | `AIBattle.AdjustGroupAIStance` | 4240; 4264 | Strength-triggered assault escalation (stance 4) and the per-unit stance re-evaluation gate. |
| Stance | `AIBattle.GetGroupStrength` | 6025 | Group strength used by the stance ladder. |
| Charges | `MicroAICheckForCharges` / `UpdateChargeTarget` / `AssignChargeTarget` | 4905 / 122199 / 126024 | AI charge paths; see `BUG-TAC-016` and `BUG-TAC-017` in `bug-fixes/vanilla-tactical-battlefield.md`. |
| Facing | `StopMicroUnitsCloseToEnemyAndRotateTowards` facing rule | 4599-4606 | Turn threshold `angletriggerforturningtowardsenemy`, enlarged in cover (4600-4603). |
| Facing | `Regiment.RotateRegiment` | 130159 (stop at 130165) | Rotating stops the unit, so re-issuing it every hold window freezes a battery. |
| Artillery | `Regiment.ammo`; `Autocalc.GetROF` | 111498; 21025 (mean at 21038) | Ammo is per-slot 0..1; vanilla reads a battery as `SumUp(ammo) / 3`. |
| Hierarchy | `BattleUnits.GrabNextParentUnit` | 86886 | Vanilla command-parent lookup (use this, never `transform.parent`). |
| Hierarchy | `Regiment.UpdateAllAttachedUnits` | 129572 (129574) | `allattachedunits = GetComponentsInChildren<Regiment>(true)`: the unit itself and every descendant. |
| Movement | `BattleUnits.SetGroupFormation` `ClearTargets` gate | 91960 | `newpath:false, modifylastwaypoint:true` skips `ClearTargets`. |
| Campaign objectives | `AICampaign.PickCampaignObjective` | 17769; 17780-17785 | Re-picks with `Random.Range` whenever `followedcampaignobjective < 0`. |
| Campaign objectives | `CampaignObjective.GetAvailableObjectives`; `InitializeObjectives` | 178827; 178630 | Default `mintownobjectives = 1`; objective towns/IIPs come from `campaignobjectives.dat`. |
| Campaign objectives | `AICampaign.UpdateImportanceValues` | 14922; 14961 | Objective weight is applied to IIPs and towns of `followedcampaignobjective`, the only steering channel. |
| Recruitment | `ZoneRecruiting`; `UpdateHeatmaps`; `Heatmap.PaintObject`; `Heatmap.GetValueOnPos` | 12405 (12470-12507); 12318; 34117; 33840 | Vanilla recruitment heatmap and `positiondeficit`; Whiskey reads the same grids. |
| Campaign movement | `DefensiveMovingOrder` queue; `UpdateOrders` | 13713; 79295 | Defensive moves are queued and executed later, not immediately. |
| Campaign movement | `AICampaign.CheckOffensiveMovements` | 14166; 14200; 14229 | Early return on empty `ownunits`; area strength term of the tempo gate. |
| Scene | `SceneManagement.ExitCampaignBattleAndRebuildCampaign` | 36802 | Leaving a battle rebuilds the campaign scene; strategic caches must not survive it unchecked. |
| Fiscal | `AICampaign.UpdateFinancialAI` | 15352-15447; 15405; 15443 | Random tax/subsidy lane nudges; balance trigger; subsidy cap at `subsidyfocus`. |
| Fiscal | `Economy.UpdateMacroEconomy` | 32541; 32594-32597 | Re-rolls every alliance's interest with `Random.Range`; never call it to apply a refund. |
| Policy | `Policies.UpdateSubsidyEffect` | 209505-209539 | Sets `maxpolicies` from the politics subsidy; evicts non-acts only while over the cap. |
| Policy | `Policies.CanBeActivated` / `AddResearch` / `CheckAIPolicyChange` | 209541 / 209554 / 211017-211099 | Slot-cap stall; see `BUG-POL-002` in `bug-fixes/vanilla-ai-economy.md`. |
| Construction | `CBuilding.AddConstructionWish` | 97479 (placeholder `Place` 97506) | The wish is paid when made (`Place` pay branch 96215). |
| Construction | `WorkDownConstructionWishes` | 97562; 97578-97585 | Final placement uses `pay: false`; the no-site cancellation removes the building without a refund. |
| Construction | `CBuilding.Place` / `RemoveBuilding` | 96163 / 98334 | Add / remove the building in `BattleUnits.cbuildings`. |
| Army groups | `ArmyGroup.AppointCommander`; `GetArmyGroupInRange`; `CanAddFurtherUnits` | 75559; 75631; 75770 | Vanilla army-group APIs; `CanAddFurtherUnits` gates attaching only. |
| Commanders | `Commander.combinedname` | 59081 | `firstname + " " + name`; registry names must match `Config/commanders.txt` on this. |
| W&L CIC | `AICampaign.UpdateUnitAI` faction skip | 11620 | Correctly skips only the CIC player's own faction; contrast `BUG-WL-001`. |
| W&L prestige | `DLC_WL.EarnPrestige` | 46601 (switch 46627, threshold 46702) | No case for order type 16; see `BUG-WL-002`. |

## Campaign logistics anchors (BUG-LOG-001..014)

Re-read 2026-10-15 for the campaign logistics bug fixes ([`bug-fixes/vanilla-campaign-logistics.md`](bug-fixes/vanilla-campaign-logistics.md)). `cp:n` = `Config/campaignprefs.txt` line.

| Area | Symbol | Line | Why it matters |
|---|---|---|---|
| Supply metric | `AICampaign.GetGroupSupplyState` | 14063-14066 | `max(avg(groupsupplystate) × 0.5, supplystate × 0.5)`, never above 0.5. Its thresholds: raid search 0.75 (9135, cp:1411), depot trigger 0.25 (14715, cp:1520), fort builders 0.25 (9724, 16381), raid abort 0.25 hard-coded (9092), dead micro-move gate (14009-14019). Any Whiskey reader of this value must rescale (`RaidSupplyThreshold.GroupSupplyStateScale`). |
| Raids | `RaidForce.UpdateRaidForces` / `SearchObjective` / `CheckAbort` | 9021 / 9130 / 9057 | Raid state machine 1 raising → 2 raised → 3 moving → 4 at target → 5 abort → 6 return; runs every `BattleUnits.Update` while `debug_raidgroupsactive` (true, 64459). Raised by `CheckCreationOfRaidForces` (17568, job 11573), which the job machine skips for a CIC player's faction (11620-11628). |
| Camps | `Regiment.UpdateCampsAndEntrenchments` | 115392-115470 | Camp only when `!iswithinfog` (115408), destroyed when fogged (115426), entrenchment model only when `!iswithinfog` (115455); bugle at 115411-115414. Called at 112224 (regular cycle, after `UpdateUnitVisuals`) and 79240. |
| Camps | `Regiment.CheckRegimentVisibility` | 127159; 127209-127229 | Campaign `iswithinfog` for a non-player unit = hidden from the player (`oppositefow.UnitIsHidden`); player-alliance units are never fogged on the campaign map (127211-127215). |
| Camps | `campbuilding` / `entrenchment` consumers | 132917-132922, 115297, 115078-115086, 127881-127887, 125041-125046 | Winter weather reset, readiness, attrition, morale, order delay; all test the object reference only. |
| Camps | `CBuilding.Place` (not newly created) | 96163-96284 | Camps are not added to `BattleUnits.cbuildings`; `CBuilding` has no fog code. `Regiment.OnDestroy` (127111-127146) does not remove the camp (`BUG-LOG-014`). |
| Embarkation | `Regiment.CheckModelReplacement` | 116372-116503 | Boat/train model and `currentmodeltype` only while `!iswithinfog` (116383, 116399, 116446; removed at 116461). The model is the embarked state: rail speed 133401, rail terrain cost 132891, night halt 133958, transport capacity 116471-116501, locomotive/ship use 133900-133915, embarkation delay 116418, ration factor 114186, no reinforcements 114501 (`BUG-LOG-002a`). The removal branch re-runs `CheckRegimentVisibility` (116467). |
| Embarkation | `Ship.Initialize` → `SoundEngine.AddPurposeArray` | 141255; 146747-146756 | Each transport model appends 20 ship-firing sound objects to lists that never shrink; `AnimateFire` (callers 19058, 19283, 19672) uses only fleet vessel models (18230), so a land transport's array is never played. |
| AI areas | `AICampaign.UpdateImportanceValues` registration | 14927, 14946, 14965 | Area members added only while `airuns <= 1`; area rule `AIArea.GetAIArea(aiareas.GetColorOnPos(pos))` (14915, 14935, 14954). |
| AI areas | `AIArea.UpdateSupplyCapacity`; supply gate; objective pick | 10772-10801; 10983; 14542-14658 | Owner-blind IIP capacity × 2 + depot stock; `ownpoints <= supplycapacity`; objective pick null-checks buildings (14627). |
| Offensive roll | `CheckOffensiveMovements` winter factor | 14310-14312; 14339-14340 | Scales only the timediff term (14339); the idle term `min(1, idle³)` (14340) is not scaled. Uses `winterdefensiveoperationsmodifier`; `winteroffensiveoperationsmodifier` loaded at 56381, never read (cp:1545-1548). Roll only for objectives in another alliance's state (14336). |
| Offensive trim | `CheckOffensiveMovements` weapon blend | 14239, 14274 vs 14294 | Add loops use `w = blend`, the trim loop `w *= blend`. |
| Depot job | `AICampaign.CheckSupplyDepotConstruction` | 14659-14779 | Builder loop 14690-14713 (skip flag 14697, dedupe 14706-14711); trigger 14715; installation search 14719-14761 (source test 14740: own, allied, or no enemy units in range, i.e. capturable; an undefended enemy depot becomes the unit's own via frontline 87208-87250 → `CheckNewOwner` 95810 → `ChangeOwnership` 95402-95452 with its stock kept, `BUG-LOG-005` not a bug); move 14762-14768; wish 14770-14776. Builders skipped at 14122, 14200, 9741, 13610, 13658, 13744, 16381. |
| Depot build | `CBuilding.IncreaseCondition` | 96590; 96626-96655 | A depot under construction advances only with a unit of the starting alliance inside its own bugle range; otherwise it regresses and is removed after `timetoremovemilitarybuildings`. |
| Transfers | `BattleUnits.GrabSubordinateByStrength` | 93549-93597 (93594) | `num = num5` instead of `num += num5`; caller `CheckTransferOfUnits` 17295. |
| Random picks | `Random.Range(0, Count - 1)` | 12146, 12241, 12302, 12793, 12801, 12928, 13040, 17611 | Unity's int `Range` excludes the upper bound; the last candidate is never picked. |
| Policy | `Policies.UpdatePolicyEffects` / Confiscation | 209616-209653 (reset 209621); 209893; 114330-114335 | Factor 1.15 multiplies a resupply duration (cp:2787-2788). |

## Policy selection (stage E)

Full model and W&L notes: [`superpowers/specs/2026-10-14-need-driven-policy-design.md`](superpowers/specs/2026-10-14-need-driven-policy-design.md) § "Vanilla and W&L model".

| Symbol | Line | Why it matters |
|---|---|---|
| `CampaignController.UpdateAllianceData` → `CheckAIPolicyChange` | 81488 → 81736 | Per-alliance cadence (`updaterhythmalliancedata`); runs before `UpdateProjects` and `UpdatePolicyResearch`. |
| `Policies.CheckAIPolicyChange` | 211017-211098 | Player gate 211019 (no CIC exemption); emergency newspaper + cancel 211026-211074; one-at-a-time return 211061; personality walk 211076-211091. |
| `GameVars.Alliance.GetAIPersonality` | 62716 | Emergency credit (101) / recruiting (100) personalities replace `usedaipersonality`. |
| `GetActivatedPolicies` | 62803 | `researchedonly` counts every policy below progress 1 (incl. a player purchase); the default form counts non-act, non-grand slots. |
| `Policies.LaunchPolicyEffect` | 209681-210955 | Every policy effect, hard-coded per id; magnitudes from `campaignprefs.txt` "Policies & Subsidies". |
| `Policies.IsDeactivated` (private) | 210957 | Union 36 before 4 Mar 1861; CSA 170 while Virginia is Union. |
| `Policies.UpdatePolicyResearch` | 209455-209482 | Speed ∝ `averagesupport / n^1.1 × (1 + 0.05 × project 95) / ResearchDuration`. |
| `Policy.InitializeStatics` / `UpdateGrandStrategyIcons` | 211285 / 209337 | `grandpolicies` from the "GrandPolicies" folder; `markedasgrandpolicy` only once the panel opens. |
| `Policy.GetAllianceFromPolicy` | 211392 | Folder name; ids < 100 Union, 100-199 CSA. |
| `Policy.CheckSelections` | 211350-211368 | W&L prestige purchase (`CalculatePrestigeCostPolicy` 46081) starts research; career cannot cancel. |
| `Policies.UpdateActivations` | 209248-209258 | W&L display bug (`BUG-WL-004`). |
| `GameVars.Alliance.GetInterventionProbability` / `AdjustInterventionLevel` | 62822 / 62887 | CSA level − Union level; positive policy adjustments help the actor on either side; W&L effect 34 shifts it for the career's side. |
| `BattleUnits.GetAverageBlockadeRatio` | 93766 | Blockade of the alliance's **own** harbors. |
| `AIFaction.SeparateUnits` / `AICampaign.GetStrengthRatio` | 10462 / 12366-12402 | Not fog-honest: no fog filter on the enemy list, and the ratio adds the enemy's `troopsinrecruitment`. Stage E does not call it; `GetAIPersonality` reaches it, and it indexes `aifaction` (null until factions exist, 11097). |
| `GetAIPersonality` recruiting emergency → `EmergencyManpower` | 62733 | Accepted: `PolicyRuntime.ReadPersonalityList` mirrors vanilla's own emergency personality, whose test includes `GetStrengthRatio < 0.8`. Vanilla announces the emergency publicly (newspaper event, `CheckAIPolicyChange` 211026-211058), so forcing Manpower = 1 from it leaks nothing a player cannot read. Keep it; add no other strength read. |
| `CheckAIPolicyChange` fallback walk | 211076-211090 | Walks `personality.policies` from the head and starts the first available entry; Union Historical lists 33/39/40 and CSA Historical 139/140 early. #19 removes the held irreversible acts from the list for the call (snapshot restored in its Finalizer), never on an emergency personality. |
| `Config/policies.dat` / `aipersonalities.dat` | `ImportPolicyData` 211717 / import 66144 | Record layouts (the harness fixture copies the 117 policy ids). |

## W&L player-economy and battle-stat anchors (2026-10-15)

Anchors behind `BUG-WL-005..010` and `BUG-TAC-018/019` (plan `superpowers/plans/2026-10-15-wl-economy-bugs.md`). The player-side model is in [`wl-player-model.md`](wl-player-model.md) § Player economy effects.

| Area | Symbol | Line | Why it matters |
|---|---|---|---|
| W&L effects | `DLC_WL.cumulativeeffectsrunning` / `cumulativeeffectsonce` | 43199-43201 (private static, `float[100]`) | Running multipliers vs once values; read by reflection (`Patches/WlEffectArrays`). |
| W&L effects | `DLC_WL.EffectMultiplier(id, type, isplayerunit)` | 44633 | Returns 1/0 when not in a career or `isplayerunit` is false; the default is `true`. |
| W&L effects | `DLC_WL.UpdateCumulativeEffects` | 44662 (reset 44671-44675, actions 44753-44773) | Rebuilds both arrays from scratch each call; type 1/2 → once, 3/4 → running, type 0 ignored. Called from `UpdateAllianceData` (81821), `AppointStaffMember` (44475), `UpdateStaffAvailability` (44537). |
| W&L attrition | `Regiment.UpdateCampaignUnitStats` | 114965 (sickness 115149-115151, desertion 115196-115198; medical 115107, training 115243, readiness 115276) | Runs for every regiment (120260, 120409); effect 22 read unscoped. |
| W&L supply | `Regiment.UpdateIIPSupplyLink` (private) | 114074 (map-group return 114145; effects 10/11 114190-114195; `num15` 114320-114351; resupply 114379; effect 14 114555; camp 8 114575) | Per map group; `num15` is the resupply duration (campaignprefs `supplyspeeddistance0/2000`). |
| W&L supply | `BattleUnits.GetCampaignGroup` | 82991 | The player's map group; vanilla's camp (172032) and `IsPlayerPartOfUnit` (47578) use it. |
| W&L camp | `Camp.GetModifier` | 172524 | `1 + bonus × maxbonusmalus [/ commanded units]`, clamped at 0; station 5 (Inspect Logistics) not divided. |
| W&L Buy Supplies | `DLC_WL.GetAverageSupplySubordinates` / `AddSuppliesSubordinates` | 47377 / 47411 (type test 47392, 47428) | Attribute 24 Get/Set (42235-42237, 42561-42563). |
| W&L transfers | `AICampaign.CheckTransferOfUnits` / `BattleUnits.MoveUnit` | 17232 (purge 17255-17262, add 17327, move 17328) / 93091 (career guard 93099, `SetTransfer` 93147) | In a career the appended unit never moves; `transfertime > 0` marks a real transit. |
| W&L morale | `Regiment.UpdateMoraleCampaign` (private) | 127816 (effect 16 on `parentcampaignunit` 127874, camp 7 on the regiment 127875-127877, rally 127891; per-regiment caller 120375) | Morale effect keyed on the campaign group (`BUG-WL-011`). |
| W&L once readers | effects 17 / 19 / 27 | 116687, 116738 / 113593 / 44559-44583 | Fighting spirit, re-enlistment ratio, staff-skill growth rate: running only, so type-1 action qualities on them were dead (`BUG-WL-009`). |
| W&L order delays | effects 13 / 12 in `Regiment` order-delay code | 125069 / 125136 | Campaign and battle order delay `num2 *= EffectMultiplier(13/12, 1, owned)`: running only, so Signal Corps' type-2 qualities were dead (`BUG-WL-009`). |
| W&L incidents | `DLC_WL.CheckForIncidents` | 46139 | `1 + EffectMultiplier(4, 0)`: incident chance read only as once, so Gamble's type-3 cost was dead (`BUG-WL-009`). |
| W&L prestige | `DLC_WL.AdjustPrestige` | 43455 | Effect 0 (prestige generation) read only as running; item, injury and event-100 type-1/2 values on it stay dead (left vanilla, `BUG-WL-009`). |
| W&L panel | `CampaignArmyPanel.UpdateStats` | 173470 (unscoped 173699-173702, 173742-173747; scoped 173552, 173637) | Lists the player's staff effects on any group (`BUG-WL-012`). |
| Battle stats | `BattleUnits.CalculateAllUnitsOfAlliance` | 87335 (averages 87396-87399) | `enemyentrenchmentlevel` divided by the player count; read by 85632-85642 and 87480-87481. |
| Battle stats | `BattleUnits.AssignConditionToSubordinates` | 85453 (HQ lines 85469-85471, side averages 85473-85504; frame 26 at 23972-23975) | HQ horses/guns clamped by the men value. |

## Game-update re-decompile inventory

When GTCW patches:
1. Re-run the decompile recipe above.
2. Diff `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` against the previous version's checked-in line numbers in this doc.
3. For every method named in the table above, verify the line number still hits the same method body.
4. Update `[HarmonyPatch(...)]` attribute strings if classes were renamed.
5. Update this doc with new line numbers.

## Saves location (player-side) — corrected 2026-05-03

**Vanilla saves go to the GAME INSTALL DIRECTORY, not `Application.persistentDataPath`.**

`SceneManagement.SaveAll` calls `Directory.CreateDirectory("Campaigns/<level>/<sublevel>/<saveFolder>/")` with a relative path. .NET resolves it against CWD which Unity sets to the game install dir. So actual save paths look like:

```
<game install>/Campaigns/001/A/Save/scenario.txt          (vanilla — main campaign)
<game install>/Campaigns/002/A/Save/scenario.txt          (vanilla — W&L)
<game install>/Campaigns/002/A/Save/whiskeyrealism.json   (mod sidecar)
```

Player.log (Unity engine log, where unhandled mod exceptions land) DOES live at `Application.persistentDataPath`:

```
<persistentDataPath>/Player.log   = %USERPROFILE%\AppData\LocalLow\Grand Tactician\The Civil War (1861-1865)\Player.log
```

Don't conflate the two. Mod's `AICampaignSaveLoadPatch` uses `Path.Combine(folder, "whiskeyrealism.json")` with the relative `folder` arg — let .NET's CWD resolution handle the rest.
