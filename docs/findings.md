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

Result: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` — 266,134 lines.

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
| `UpdateImportanceValues` | 14906 | Weighted importance per zone (transport capacity, profit, population, capital, objective bonus) |
| `CalculateMostValueableAIZones` | 10965 | Picks single highest-importance zone within distance gate |
| `GetBestRecruitingState` | 10723 | Theater-keyed recruiting state pick |
| `GetClosestStateWithRecruitsPotential` | 10836 | Closest-with-potential variant |
| `GetAllStateWithRecruitsPotential` | 10858 | All-with-potential variant |
| `UpdateSupplyCapacity` | 10773 | Per-zone supply capacity from IIPs + supply depots |
| `CheckSupplyDepotConstruction` | 14660 | AI builds supply depots |
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
| `MicroAICheckForCharges` | 4906 | Charge fires when `aigroup.ai_stance == 4` + cooldown |
| `CheckForFeudGroupActions` | 4953 | **Feud auto-charge — does NOT call `PerformAIActionDLCWL`** (the brigade-magnetization-into-melee bug) |
| `AssignReserves` | 7018 | Reserve assignment driver |
| `AssignReserveToOperationalGroup` | 7154 | Reserve → group |
| `LinkReservesToLineGroup` | 6643 | Reserve linking |
| `CheckLineFallbacks` | 5117 | Line-unit fallback decisions |

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
| `BattlefieldSetup.CheckTerrainLine` | 27638 | Runtime probe checks water-crossing evidence along candidate lines. |
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
- Bridge/ford/road/choke enumeration: no clean typed battle-objective enumeration API found in the current decompile. Partial crossing anchors exist: `BattlefieldSetup.rivercrossingpositions` is declared at line 24723, populated from NavMesh areas including `"Bridges"` and `"River Crossings"` at lines 26614-26637, and consumed by `AIBattle` blocked-crossing sampling at lines 7932-7967. Road hits in this scan are EasyRoads/editor/campaign-railroad surfaces, not a confirmed tactical POI source. Do not use typed Bridge/Ford/RoadJunction/ChokePoint scoring from these anchors alone; first implementation must classify them as generic positions unless a verified scene object/source is added.
- Terrain safety: use existing #58/#60 terrain/deployment sampling until a broader terrain source is verified. `BattlefieldSetup.TerrainShape` exists at lines 24121-24139 and current #58/#60 terrain/facing discipline remains the bounded source for terrain safety.

## Regular Campaign Command Movement Anchors

- Regular campaign formations are still instances of the vanilla `Regiment` component. Do not infer literal battlefield-regiment scope from the C# class name. Campaign command tier is encoded by `unittyp` / `overridesymbol`: `13` brigade, `14` division, `15` corps, `16` army. `CampaignArmyPanel` labels these tiers at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:219088-219100`.
- Manual regular campaign/battle right-click movement is `BattleUI.CheckPathSetting()` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:168980`, which creates `SetWaypointData` at line 169019, then `BattleUI.CheckGroupRotation()` consumes it and calls `BattleUnits.SetWaypoint(...)` at lines 166132 and 166163.
- `BattleUnits.SetWaypoint(Regiment, ...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:91232` is the regular movement-order API. It has explicit `IsCampaign` branches, readiness checks, order-delay handling, and calls `SetGroupFormation(...)` for command nodes where `reg.unittyp > 13` at line 91559.
- `BattleUnits.SetGroupFormation(Regiment, ...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:91822` is the vanilla group/command-node formation API. It returns immediately for `unittyp <= 13`, then walks the attached hierarchy with `GetHierarchyTree(...)` at lines 91988-91999.
- `BattleUnits.GetHierarchyTree(Regiment, ...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:92720` reads direct attached units and builds the vanilla command tree used by group formation movement.
- `AIBattle.CheckCurrentOrderUpdate(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8233` is not a regular campaign movement anchor. It returns immediately unless `DLC_WL.dlc_scenarioactive` is true, then creates `DLC_WL.GivenOrders` and career-panel order messages. Treat it as W&L current-order/message machinery only.

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
