# Live Theater Campaign Planner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Use superpowers:test-driven-development for every pure-logic change (red first). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement roadmap stage G (`docs/superpowers/specs/2026-10-14-campaign-planner-design.md`, binding, including its "Vanilla and W&L model" and "Corrections" sections). Each AI alliance gets a daily, fog-honest, per-theater campaign plan built from live vanilla state: believed enemy armies from vanilla's own campaign fog grid, commander profiles from the live vanilla commander records, sub-theater scoring with a time-decayed historical prior and hysteresis, a supply-reach gate, a force allocation over every AI formation, competence-plus-prior commander appointments, and idempotent executors that steer vanilla's own campaign AI (offensive target pointers, `followedcampaignobjective`, `theaterposition`, availability, capital pledges, transfers, `MoveUnitTo`, depots, forts, railroads). The phased-plan CIC, the probe/package layer, the army-area anchors and their patches are deleted.

**Architecture:** New folder `src/WhiskeyRealism/Strategic/Planner/`. Pure contracts, snapshot, belief model, profiles, sectors, planner, logistics gate, allocator, appointment planner, mover/depot/railroad state machines are in the test csproj; runtime readers and executors (`*Runtime.cs`) are not. Two calls per alliance in the daily strategic review (`CampaignPlannerRuntime.ReviewPlan` builds the snapshot and runs beliefs → profiles → plan → allocation; `CampaignPlannerRuntime.ReviewExecute` runs appointments and executors); executors write vanilla state only from that review. Harmony patches read the published `CampaignPlanStore` and steer vanilla's own inputs (Postfix pointers, availability Postfixes, snapshot-restore filters, argument edits); the only new Prefix-skip is the railroad line choice, which needs the user's approval (spec C11).

**Tech stack:** C# netstandard2.1, BepInEx 5.4 + HarmonyX, Unity 2021.3.16f1 Mono x64. Console harness `tests/WhiskeyRealism.Tests` (net8.0, auto-discovered `internal static class *Tests { public static void Run() }`).

**Base:** branch `feat/campaign-planner` (worktree `.worktrees/campaign-planner`, `refs` symlinked), cut from `main` `163e681`. Before G0 starts: rebase onto `main` after stage E (need-driven policy) merges; G5's W&L executor work starts after stage F (`feat/wl-chain-campaign`) merges to `main`. Each workstream works on `feat/campaign-planner-<gN>` cut from `feat/campaign-planner` after G0 merges, and merges back in the order below.

**Inputs (read before your workstream):** the spec (all of it; the "Vanilla and W&L model" section is the anchor set); `docs/wl-player-model.md`; `docs/vanilla-vision-and-movement.md` § Campaign vision / Campaign movement; `docs/superpowers/specs/2026-10-14-wl-chain-of-command-design.md` §D-§E; root `AGENTS.md`; `src/WhiskeyRealism/{Strategic,Patches}/AGENTS.md`; `tests/WhiskeyRealism.Tests/AGENTS.md`. Decompile `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (build 14703111; regenerate per `docs/findings.md` if `/tmp` was wiped).

## Global Constraints

- **Vanilla first.** Before code that reads or writes vanilla state, read the full vanilla body and its callers. Cite anchor lines in the file header comment and in the commit message. Every task below names the anchors it rests on; code not grounded in them is rejected in review.
- **Never throw from a patch.** Reflection is cached in static fields, wrapped in try/catch, and logged once (`OnceLog.Warning("campaign-planner:<static key>", …)`). Snapshot-restore filters restore in a `[HarmonyFinalizer]`.
- **`TelemetryPerf.Scope` + `PatchFirstFire.Mark` on every patch entry.** `using (TelemetryPerf.Scope("campaign.patch.<short-name>", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))` and `PatchFirstFire.Mark("campaign.patch.<short-name>")` with a literal id (`tools/smoke-check.py` collects literals only). Situational ids go into `tools/smoke-situational.txt` with a reason.
- **Static OnceLog keys** (no instance id or name in a key).
- **Scoped state.** Every static per-campaign cache registers `CampaignScope.Register("<name>", Reset)` from its owner's static constructor (`Util/ScopedState.cs`). No hand-wired resets in `StrategicCoordinator.ResetAll`.
- **Patches read, the review writes.** Planner state (beliefs, plan, allocation, task ledgers) is written only inside `CampaignPlannerRuntime.ReviewPlan`/`ReviewExecute` (daily review) and the battle-result event handler. Patches read `CampaignPlanStore` through read-only accessors. Executor bookkeeping (issued-move tracker, depot task state) is written by the executors inside the review.
- **W&L.** Never move, stop, re-stance or re-parent a `dlcw_isundercommander` unit or a unit with `DLC_WL.IsMovedByPlayer` (45797); never appoint/displace `dlc_chosencommander` or a commander with `IsServingUnderPlayer()` (60695). Classify the player with the map-group test (`GetCampaignGroup(cmd) == cmd`, 8271; spec C4). CIC player → the planner stands down for that alliance; hierarchy −1 → the planner's review and executors pause (spec C5).
- **Alliance bounds.** The planner runs for alliances 0 and 1 only. Faction index ≠ alliance id: map through `AIFaction.GetAIFactionIDFromAllianceID` (10413) by reflection, bound-check every per-faction array (`AIArea` arrays are sized by alliance count but indexed by faction index, 10679-10692).
- **Fog-honest.** Only `EnemyArmyModel` beliefs (via `CampaignSnapshot.Beliefs`) feed planner, allocator and the migrated ledgers. Only `EnemyDetectionRuntime` reads raw enemy positions. Gate `UnitIsHidden` on `GameVars.usefow` (101188 NRE).
- **All-on config.** Every new bool flag defaults `true` with a truthful description. A flag is bound in the slice that first reads it (config-audit's UNREAD finding is a gate) and removed only in G6; `Plugin.cs` edits are minimal and integrator-merged. `python3 -I tools/config-audit.py .` → 0.
- **No dead code.** `python3 -I tools/orphan-sweep.py .` → 0 and `--include-test-only` lists nothing this stage added, at the end of every workstream for its files and repo-wide at G7.
- **Tests.** Each workstream owns one test file `tests/WhiskeyRealism.Tests/<Name>Tests.cs` (`internal static class <Name>Tests { public static void Run() }`), adds a `<Compile Include="..\..\src\WhiskeyRealism\Strategic\Planner\<File>.cs" Link="<File>.cs" />` per new pure file and removes the entry of every deleted file. Assertions use `TestHarness.AssertEqual/AssertTrue/AssertFalse`. Write the failing test first, see it fail (exit 134 on the first failing assertion), then implement.
- **Pure means no Unity or vanilla types.** Positions are `float X, Z`. `EraStage`, `Theater`, `DirectorPosture`, `PersonalityVector`, `BattleHistoryRecord` (pure strategic types) are allowed.
- **Exclusive file ownership.** Each workstream edits only its listed files plus its own test file and csproj lines. Shared files (`StrategicCoordinator.cs`, `PersistenceDto.cs`, `SidecarDtoCodec.cs`, `Plugin.cs`, `Telemetry/TelemetryTagPolicy.cs`, `docs/patch-catalog.md`, `docs/handoff.md`) are edited only where this plan says so, minimally; stages E and F edit some of them in parallel and the integrator merges.
- **Gates before each commit:** `./build.sh` 0 warnings; test build 0 warnings; `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` exit 0 + PASS count; `python3 -I tools/config-audit.py .` 0; `python3 -I tools/orphan-sweep.py .` 0 (`--include-test-only` adds nothing); `python3 -I tools/test_smoke_check.py` OK. `git log --oneline -10 && git status` before each commit. Messages `<type>(campaign-planner): <summary> [G<n>]`, then the anchors, then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **No new Transpilers.** No new Prefix-skip other than `RailroadLineSteeringPatch` (spec C11, only after the user approves).
- **Patch ordinals** are assigned by the integrator at G7 in merge order (stages E/F take ordinals in parallel; next free on `main` today is #69). Workstreams refer to new patches by class name.

## Plan decisions (resolving the spec's open points)

| # | Point | Decision |
|---|---|---|
| P1 | Where the planner runs | Two calls per alliance per review in `RunStrategicReviewCore`, both inside `if (operationalRuntimeReady)`: `CampaignPlannerRuntime.ReviewPlan(int alliance, int day, int month, int year, EraStage era, DirectorPosture tempo)` right after `UpdateFiscalIntent` (~407) and before `UpdateFrontLedger` (~410), so the front and defense ledgers read today's beliefs; `CampaignPlannerRuntime.ReviewExecute(int alliance)` right after `UpdateDefenseIntent` (~414), so the executors see units the defense runner just committed as busy. Both return immediately for the CIC player's alliance, on hierarchy −1, before `aifaction` exists, and for alliances ≥ 2. Until G6 the old CIC/probe path runs alongside (P10). Scopes `campaign.planner.review` and `campaign.planner.execute` (4.0 ms). |
| P2 | Snapshot cost | One `CampaignSnapshot` per alliance per review, built from one pass over `BattleUnits.campaignunitlist` (79974-79994) plus the faction's lists and `aiarea`. Pooled lists, rebuilt in place. Budget ≤ 2 ms per alliance at 20x (perf row). If the review exceeds 4 ms twice in a row, the second alliance runs on the next frame (`PlannerBudget.ShouldDefer`). |
| P3 | Score scale | Every planner score term is normalized to [0, 1] before weighting; the total is a weighted sum with weights as `public const` in `TheaterPlanner` (pinned by tests): `ValueWeight 0.30`, `BalanceWeight 0.25`, `ReachWeight 0.15`, `DistanceWeight 0.10`, `ExposureWeight 0.10`, `CapitalThreatWeight 0.10`. Prior: `PriorLambda 0.08`, so the prior's contribution is ≤ `PriorMaxContribution = 0.08` and any live margin above 0.08 wins (harness-tested). |
| P4 | Hysteresis | Switch main effort only when `best − current > SwitchMargin (0.12)` for `SwitchDays (3)` consecutive reviews, or immediately on a trigger: decisive battle in/near the current target (existing `BattleReplanQuery` rule), believed capital threat ≥ `CapitalThreatTrigger (0.6)`, current objective accomplished or unavailable. |
| P5 | Randomness | Softmax over candidates within `Epsilon (0.04)` of the best, temperature `0.02`, `System.Random(seed)` with `seed = CampaignSeed ^ (alliance * 7919) ^ DecisionSerial`; `CampaignSeed` = hash of the save folder name captured once at campaign start and persisted; `DecisionSerial` increments per main-effort decision and is persisted. |
| P6 | Belief dynamics | `EnemyArmyModel`: visible → confidence 1; unseen → `confidence = 0.5^(days / 7)`; uncertainty radius `= seenRadius + DriftPerDay × days` with `DriftPerDay` = vanilla's campaign march distance per day for that unit type, derived in G1 from the campaign speed fields (read and cite the field) — not guessed; expire at 30 days or when confidence < 0.05; an enemy-held objective town implies a garrison belief of the town's garrison strength at the town (known to every side: towns are public). |
| P7 | Capital floor | Floor = max(vanilla's own requirement, `CapitalFloorRatio 1.0 ×` believed enemy strength within relocation range weighted as vanilla's 11712-11741 formula). Vanilla-need part: rewritten #4 pre-pledges into `groupstodefendcapital` only while vanilla's `num2 > 0` (unchanged rule). Excess: `CapitalGuard` role, held by the mover and withheld by the availability Postfixes (spec C9). |
| P8 | Write budget | Per alliance per review: ≤ `MaxMovesPerDay 6` `MoveUnitTo` calls, ≤ 1 appointment, ≤ 1 depot wish, ≤ 2 `theaterposition` writes per formation per week. A move is confirmed on the next review when the formation's last waypoint changed toward the target or it moved ≥ 25% of the way; else `Failed`, one retry at the next review, then the allocator reassigns the role (`GaveUp`). |
| P9 | Player reliability | `ForceAllocator` takes `float playerReliability01`. Source: stage F's `PlayerComplianceLedger.ReliabilityScore` (spec §E of the chain-of-command design). Before F merges, the runtime passes `1.0` only for the below-map-level and no-player cases and the map-group case is treated as `0.5` (unreliable) — a deliberate conservative value replaced by F's ledger in G5a; no stub type is created. Thresholds: `UnreliableBelow 0.5`, `NonComplianceDays 5`. |
| P10 | Running beside the old path until G6 | G0 adds `Enable Campaign Planner` (planner review + telemetry) and `Enable Campaign Planner Executors` (all planner writes). Until G6 deletes the old path, G5's executors check that the old writer for the same surface is gone or stand down for that surface: G5 rewires #2, #3, #4 in place (one writer per surface), and the mover refuses formations held by `CoordinatedOperationRuntime.IsPackageLocked` until G6 deletes it. G6 removes the second flag's stand-down branches. Both flags stay as rollbacks after G7 (planner off = vanilla campaign AI plus the kept ledgers). |
| P11 | Sub-theater sectors (spec C15 default) | The planner's own `SectorModel`: AI areas grouped into sectors by a deterministic flood fill over area adjacency (`GetClosestAreaDistance` ≤ `aimaximumdistancetosearchforunitrelocations`, 14835) seeded at each live objective, each sector tagged with the existing `Theater` of its centroid (`TheaterClassifier.FromPosition`). `FrontSectorLedger` stays 6-bucket and switches to beliefs (G1). Option (b), re-keying the ledger, is out of this plan unless the user picks it. |
| P12 | Historical prior | `HistoricalPriorTable` (pure) is `HistoricalOperationCatalog`'s profiles reduced to `(alliance, objectiveIds[], windowStart, windowEnd, priority)`; weight 1 inside the window, `0.5^(monthsOutside / 3)` outside, × `priority / 3`. The per-phase machinery (phases, dynamic rules, tempo presets) is deleted. `HistoricalArmyAreaRegistry` → `HomeTheaterPrior` (formation/commander name → home `Theater`), used by the allocator as a 0.05 tie-break toward the home theater. |
| P13 | Appointment protection | A planner appointment is protected for `ProtectionDays 60`: #6's Prefix sets `alliance.worstcommanderidperlevel[r] = -1` while that slot holds a protected commander, its Finalizer restores the array (snapshot-restore pattern). Vertical replacement (17662) and the CIC re-pick (81315) are not blocked (spec C16, C18). |
| P14 | Railroad steering (spec C11) | Built in G5c only after the user approves option (a). If the user declines, G5c drops the railroad task and `Enable Railroad Steering` is not added. |
| P15 | W&L career transfer fix (spec C1) | Built in G5b only after the user approves; if declined, the replacement-flow allocator role is suppressed in careers (`ReplacementFlow.AvailableInCareer = false`) and the decision is documented. |

## User decisions this plan needs (defaults in brackets; the slice that depends on each is gated)

1. **C1 — fix vanilla's dead W&L-career transfers** with a `MoveUnit` argument edit scoped to `CheckTransferOfUnits` [yes] → G5b.
2. **C11 — railroad line steering** via a Prefix-skip on `UpdateRailroadConstruction` that keeps vanilla's roll count and timing [yes] → G5c.
3. **C15 — sub-theater sectors** private to the planner, `FrontSectorLedger` stays 6-bucket [yes] → G3.

## Dependency order and parallelism

```
G0 contracts + snapshot + flags + review hook (integrator, sequential; after stage E merges)
  ├─ G1 EnemyArmyModel + detection + ledger migration to beliefs
  ├─ G2 CommanderProfiles + appointments + #6 guard
  ├─ G3 SectorModel + LogisticsGate + HistoricalPriorTable + TheaterPlanner
  └─ G4 ForceAllocator
        (G1-G4 build against G0 contracts; disjoint files; run in parallel)
G5a mover + objective/importance/theater/availability executors (+ W&L via F)   ┐ after G1-G4 merge;
G5b capital floor + replacement flow (+ career transfer fix, user-gated)          ├ parallel, disjoint;
G5c construction steering: depots, forts, railroads (user-gated)                  ┘ G5a after stage F merges
G6 removals, migrations, sidecar v3, coordinator rewiring (integrator, sequential)
G7 integration, docs, smoke (integrator, last)
```

Merge order into `feat/campaign-planner`: G0, G1, G3, G4, G2, G5b, G5c, G5a, G6, G7.

Shared-file exceptions (sequential by merge order, disjoint regions):
- `StrategicCoordinator.cs`: G0 adds the `CampaignPlannerRuntime.ReviewPlan`/`ReviewExecute` calls and the battle-event forward (`RecordBattleOutcome` → `CampaignPlannerRuntime.OnBattleRecorded`); G1 rewrites `UpdateCapitalThreat`'s enemy read only; G6 does the removals.
- `Patches/FortConstructionGovernorPatch.cs`: G1 switches its enemy read (`:301-304`) to beliefs; G5c adds the axis site preference.
- `Patches/DefensiveOpsPatch.cs`: G5b rewrites it (its enemy read moves to beliefs there, not in G1).
- `Plugin.cs`: each slice binds the flags it reads (G0, G1, G2, G5b, G5c), G6 removes. `TelemetryTagPolicy.cs`: G0 registers every new event name.

---

## G0 — Contracts, snapshot, flags, review hook (integrator, sequential)

**Owned files:** create `Strategic/Planner/CampaignPlannerContracts.cs` (pure), `Strategic/Planner/CampaignSnapshot.cs` (pure), `Strategic/Planner/PlayerCommandClassifier.cs` (pure), `Strategic/Planner/CampaignPlanStore.cs` (pure, CampaignScope), `Strategic/Planner/CampaignSnapshotRuntime.cs` (runtime), `Strategic/Planner/CampaignPlannerRuntime.cs` (runtime); modify `Plugin.cs` (flags), `Telemetry/TelemetryTagPolicy.cs` (event names), `StrategicCoordinator.cs` (two call sites, P1); test `CampaignSnapshotTests.cs`.

**Vanilla to read:** `SeparateUnits` 10458-10515; `campaignunitlist` 79974-79994; `GetUnitVisibilityStateOnCampaignMap` 82934-82974; `GetCampaignGroup` 82991-83020; `CheckCurrentOrderUpdate` 8251-8275 (`flag2` 8271); `GetPlayerCommandHierarchy` 44397-44427; `IsCommanderInChief` (47475); `IsMovedByPlayer` 45797; `IsUnitUnderCommander` 91168-91223; `GetAIFactionIDFromAllianceID` 10413; `AIArea` fields 10679-10692 and `UpdateAreaPoints` 10517-10609; `UpdateSupplyCapacity` 10772-10802; `IsUnitAvailableForOffensiveOperations` 14080-14158 (busy lists); `FortConstructionOrder.UnitAlreadyConstructing`, `SeaInvasionForce.GetSeaInvasionForceReference`, `RaidForce.IsRaidUnit`; `groupstrengthdirect` 120391-120407; `GetGroupSupplyState` 14063-14066; campaign `commanderrange` 125830-125838; `CampaignObjective.objectives` / `GetAvailableObjectives` / `IDAccomplished` (read the bodies; the spec marks `GetAvailableObjectives` internals as not yet read); `Railroad` fields and `StartConstruction(int, bool checkonly)` 77818-77845; IIP `GetTransportCapacity` 104652-104668, `allianceowner`; `CBuilding` supply depot fields (`Condition`, `constructiontimer`, `Owner`).

**Interfaces — Produces (every later slice consumes these):**
```csharp
namespace WhiskeyRealism.Strategic.Planner {
  public enum PlayerCommandClass { NotCareer = 0, NoCommand = 1, CommanderInChief = 2, MapGroup = 3, BelowMapLevel = 4 }
  [System.Flags] public enum FormationFlags : uint { None = 0, InBattle = 1, OnRetreat = 2, PlayerOwned = 4, PlayerCommand = 8,
    PlayerParentGroup = 16, MovedByPlayer = 32, InOffensiveOps = 64, InDefensiveOps = 128, CapitalDefense = 256,
    VanillaDepotBuilder = 512, FortBuilder = 1024, SeaInvasion = 2048, RaidForce = 4096, InTransfer = 8192,
    Embarked = 16384, FightingForce = 32768, HasPath = 65536, MoveQueued = 131072 }
  public struct FormationRecord { public int InstanceId; public int UnitTyp; public float X, Z, WaypointX, WaypointZ;
    public float Strength; /* groupstrengthdirect */ public float Morale; public float Supply01; /* GetGroupSupplyState */
    public int ReadinessStep; public float CommandRange; public float BugleRange; public int CommanderId;
    public bool HasTheater; public float TheaterX, TheaterZ; public int AreaIndex; public FormationFlags Flags; public string Name; }
  public struct AreaRecord { public int Index; public float X, Z; public float Importance, Points, DistancePoints, OwnPoints, SupplyCapacity;
    public float OwnStrengthClose; public int OwnerAlliance; /* state owner at averageposition, -1 none */ }
  [System.Flags] public enum ObjectiveRole { None = 0, Capital = 1, RailHub = 2, Port = 4, Industry = 8 }
  public struct ObjectiveRecord { public int ObjectiveId; public float X, Z; public int HolderAlliance; public bool Available, Accomplished, HasGround;
    public ObjectiveRole Roles; public float Importance01; public float GarrisonStrength; }
  public enum SupplySourceKind { Iip = 0, Depot = 1 }
  public struct SupplySourceRecord { public int Id; public SupplySourceKind Kind; public int Owner; public float X, Z;
    public float TransportCapacity, Condition; public bool Built; public bool Invisible; }
  public struct RailLineRecord { public int Index; public bool Startable; /* StartConstruction(a, checkonly:true) */
    public bool UnderConstruction; public float Progress; public float AX, AZ, BX, BZ; }
  public enum DetectionSource { FogGrid = 0, OwnSideWithinFog = 1, Contact = 2, NoFog = 3, ObjectiveGarrison = 4 }
  public struct EnemyDetection { public int InstanceId; public int UnitTyp; public float X, Z, Strength; public DetectionSource Source; }
  public struct EnemyBelief { public int InstanceId; public int UnitTyp; public float X, Z, Strength; public int LastSeenDay;
    public float Confidence01, UncertaintyRadius; public DetectionSource LastSource; }
  public struct CommanderRecord { public int Id; public int Rank; public float Initiative, Leadership, Administration, Cunning, Exp, Fame, Average;
    public bool Available, Navy, Dummy, RefreshCommand, ServingUnderPlayer, IsPlayer; public int CommandInstanceId; public int CommandUnitTyp; public string Name; }
  public enum TheaterPosture { Hold = 0, MainEffort = 1, Support = 2, EconomyOfForce = 3, Screen = 4, Recover = 5 }
  public enum FormationRole { Hold = 0, MainEffort = 1, Support = 2, Reserve = 3, CapitalGuard = 4, DepotBuilder = 5, Recover = 6 }
  public sealed class SectorPlan { public int SectorId; public WhiskeyRealism.Strategic.Theater Theater; public TheaterPosture Posture;
    public int TargetObjectiveId; public float TargetX, TargetZ; public float RequiredRatio; public float AxisFromX, AxisFromZ; }
  public sealed class DepotTask { public float X, Z; public int SectorId; public float Priority; }
  public sealed class TheaterPlan { public int Alliance; public int DaySerial; public int MainSectorId = -1; public int MainObjectiveId = -1;
    public float MainRequiredRatio; public System.Collections.Generic.List<SectorPlan> Sectors = new System.Collections.Generic.List<SectorPlan>();
    public float CapitalFloorStrength; public System.Collections.Generic.List<DepotTask> DepotTasks = new System.Collections.Generic.List<DepotTask>();
    public string SwitchReason; public string PriorId; }
  public struct CommanderProfile { public int CommanderId; public float RequiredOddsShift; /* (initiative - 0.5) * -0.5: bold commanders accept worse odds */
    public float AttackBias; /* initiative - leadership*0.5, clamped [-1,1] */ public float RiskTolerance; /* cunning - 0.5 */
    public float SupplySlack; /* administration * 0.25 of commandrange */ }
  public struct RankBand { public int UnitTyp; public int MinRank, MaxRank; } // GetMin/MaxRankForUnittyp (67003-67058) per unittyp for this alliance
  public struct Assignment { public int InstanceId; public FormationRole Role; public int SectorId; public float TargetX, TargetZ; public string Reason; }
  public sealed class Allocation { public int Alliance; public System.Collections.Generic.List<Assignment> Assignments = new System.Collections.Generic.List<Assignment>();
    public bool TryGet(int instanceId, out Assignment a); }
}
public sealed class CampaignSnapshot {
  public int Alliance { get; } public int FactionIndex { get; } public int DaySerial { get; } public int Year, Month, Day;
  public PlayerCommandClass Player { get; } public int PlayerHierarchy { get; } public int PlayerCommandInstanceId { get; } public int PlayerMapGroupInstanceId { get; }
  public bool UseFog { get; } public EraStage Era { get; } public int PolicyChapter { get; } public DirectorPosture Tempo { get; }
  public float CapitalX, CapitalZ; public bool HasCapital; public float EnemyCapitalX, EnemyCapitalZ;
  public float RelocationRange; /* GamePrefs.aimaximumdistancetosearchforunitrelocations */ public float TheaterHalfExtent; /* GamePrefs.theatersize */
  public System.Collections.Generic.List<FormationRecord> Own { get; } public System.Collections.Generic.List<AreaRecord> Areas { get; }
  public System.Collections.Generic.List<ObjectiveRecord> Objectives { get; } public System.Collections.Generic.List<SupplySourceRecord> Supply { get; }
  public System.Collections.Generic.List<RailLineRecord> Rails { get; } public System.Collections.Generic.List<CommanderRecord> Commanders { get; }
  public System.Collections.Generic.List<RankBand> RankBands { get; }       // 10..18, for CheckSuitableRank-style checks in pure code
  public System.Collections.Generic.List<EnemyDetection> Detections { get; } // filled by G1's EnemyDetectionRuntime; empty until then
  public System.Collections.Generic.List<EnemyBelief> Beliefs { get; }       // filled by G1's EnemyArmyModel
  public int IndexOfOwn(int instanceId);
}
public static class PlayerCommandClassifier {
  public static PlayerCommandClass Classify(bool scenarioActive, int chosenCommander, bool isCommanderInChief, int hierarchy, bool commandIsMapGroup);
  public static bool PlannerStandsDown(PlayerCommandClass c, int playerAlliance, int alliance); // CIC of this alliance
  public static bool PlannerPaused(PlayerCommandClass c);                                       // NoCommand (hierarchy -1)
}
public static class CampaignPlanStore { // per-campaign, read-only to patches
  public static bool TryGetPlan(int alliance, out TheaterPlan plan);
  public static bool TryGetAllocation(int alliance, out Allocation allocation);
  internal static void Publish(int alliance, TheaterPlan plan, Allocation allocation); // review only
}
internal static class CampaignPlannerRuntime {
  internal static void ReviewPlan(int alliance, int day, int month, int year, EraStage era, DirectorPosture tempo); // snapshot → beliefs → profiles → plan → allocation → publish
  internal static void ReviewExecute(int alliance);                                                               // appointments → executors
  internal static void OnBattleRecorded(BattleHistoryRecord record);
}
```
`DirectorPosture`, `EraStage`, `Theater`, `BattleHistoryRecord` are existing pure types.

**Flags (all `true`, section `[Campaign Planner]`; bound in the slice that reads them):**
- `Enable Campaign Planner` — "Default ON. Runs the live theater campaign planner in the daily strategic review for each AI alliance (snapshot, fog-honest enemy beliefs, commander profiles, theater plan, allocation) and logs it. OFF = no planner; vanilla campaign AI and the kept strategic ledgers only."
- `Enable Campaign Planner Executors` — "Default ON. Lets the planner steer vanilla: offensive target pointers, campaign objective, theater boxes, availability, capital pledges, transfers, MoveUnitTo, depots, forts, railroads. Never moves the W&L player's own command. OFF = plan and telemetry only."
- `Enable Campaign Fog-Honest Beliefs` — "Default ON. Enemy armies are known only when vanilla's campaign fog shows them to the side (FogOfWar.UnitIsHidden, 127225 arguments; fows[1] for the non-player AI, iswithinfog for the W&L player's AI side) or by contact; unseen armies are remembered and decay. OFF = vanilla's omniscient enemy lists."
- `Enable Commander Appointment Planner` — "Default ON. Each AI alliance puts its best available commanders (vanilla competence: GetAverageAttributeValue and the live attribute getters) on its main-effort commands, at most one appointment a day; the historical succession events add a bonus when their conditions hold; appointees are shielded from vanilla's job-24 swap for 60 days. OFF = vanilla job 24 only and no succession swaps."
- `Enable Supply Depot Steering` — "Default ON. When the planned advance would outrun supply, a small AI formation marches to a depot site on the axis, places a vanilla supply-depot construction wish there and stays until it is built (or 20 days pass). Vanilla's own depot logic is unchanged. OFF = vanilla depots only."
- `Enable Fort Steering` — "Default ON. Vanilla fort construction prefers scenario fort sites on planned axes and threatened sectors when one is eligible; vanilla's one-fort-at-a-time rule, builders and placement are unchanged. OFF = vanilla site choice (the fort governor's saturation caps still apply)." 
- `Enable Railroad Steering` — added only if the user approves C11 (P14).
- `Enable W&L Career Transfer Fix` — added only if the user approves C1 (P15).
G0 binds only `Enable Campaign Planner` (read by `ReviewPlan`) and `Enable Campaign Planner Executors` (read by `ReviewExecute` as its gate); `Enable Campaign Fog-Honest Beliefs` is bound in G1, `Enable Commander Appointment Planner` in G2, the steering flags in G5b/G5c, each in the slice whose code reads it.

**Steps:**
- [ ] Tests first (`CampaignSnapshotTests`): `ClassifierNotCareerWhenScenarioInactive`, `ClassifierCicBeatsEverything`, `ClassifierNoCommandOnHierarchyMinusOne`, `ClassifierMapGroupWhenCommandIsItsOwnCampaignGroup` (a top-unit division, hierarchy 14), `ClassifierBelowMapLevelForBrigadeInCorps`, `StandsDownOnlyForCicPlayersOwnAlliance`, `PausedOnNoCommand`, `IndexOfOwnMinusOneForUnknown`, `SnapshotListsResetOnReuse`, `AllocationTryGetFindsAssignment`, `PlanStorePublishReplacesAndResetClears` (CampaignScope reset).
- [ ] Implement the pure files. `CampaignPlanStore` registers `CampaignScope.Register("campaign-planner-store", Reset)`.
- [ ] Implement `CampaignSnapshotRuntime.Build(int alliance, CampaignSnapshot reuse)`: one pass over `campaignunitlist` for own formations (alliance match, active, visibility state ≠ 0, not fleets/garrisons — mirror `SeparateUnits` 10469-10499), flags from the vanilla busy lists, `MoveQueued` from `DefensiveMovingOrder.OrderRunning` (9933) for the unit; areas from `aiarea` with faction-indexed fields; objectives from `CampaignObjective.objectives` (live positions only); supply sources from `BattleUnits.iips` / supply-depot `cbuildings`; rails from `BattleUnits.railroad` with `StartConstruction(alliance, checkonly: true)`; commanders of the alliance (live getters with default arguments, as vanilla 14250 calls them; `GetAverageAttributeValue()`); rank bands from `GameVars.GetMin/MaxRankForUnittyp(unittyp, alliance)` for unittyp 10-18 (67003-67058); player class from the classifier. Raw enemy groups are **not** read here (G1 owns that). Reflection for private `AIFaction` members cached; failure → empty snapshot + `OnceLog.Warning("campaign-planner:snapshot-degraded", …)`.
- [ ] Implement `CampaignPlannerRuntime.ReviewPlan` / `ReviewExecute`: flag gate → `PlayerCommandClassifier` gates (P1) → build snapshot → (later slices insert their steps in a fixed order: `ReviewPlan` = beliefs G1, profiles G2, plan G3, allocation G4, then `CampaignPlanStore.Publish`; `ReviewExecute` = appointments G2, then executors G5b, G5c, G5a). Scopes `campaign.planner.review` / `campaign.planner.execute` (4.0). First run per session and alliance: `OnceLog.Info(alliance == 0 ? "campaign-planner:first-review:0" : "campaign-planner:first-review:1", "[CampaignPlanner] first review alliance=<a> formations=<n> areas=<n> objectives=<n> supply=<n> rails=<n> commanders=<n> player=<class>")`.
- [ ] `StrategicCoordinator.RunStrategicReviewCore`: add `if (operationalRuntimeReady) CampaignPlannerRuntime.ReviewPlan(alliance, day, month, year, era.Stage, DirectorMemories[alliance]?.LastPosture);` after `fiscalMs += …UpdateFiscalIntent…` (~407) and `if (operationalRuntimeReady) CampaignPlannerRuntime.ReviewExecute(alliance);` after `defenseMs += …UpdateDefenseIntent…` (~414). Stage E inserts its need/policy step after the fiscal intent too: keep both, E's first. `RecordBattleOutcome`: after `BattleHistory.Add`, call `CampaignPlannerRuntime.OnBattleRecorded(record)`.
- [ ] Register telemetry events (`AddCampaign`): `CampaignPlanner` (State), `CampaignPlan` (Decision), `EnemyBeliefs` (State), `CommanderProfile` (State), `Appointment` (Write), `Allocation` (Decision), `CampaignMover` (Write), `DepotTask` (Write), `RailroadSteer` (Write), `CapitalFloor` (Write), `ReplacementFlow` (Write).
- [ ] Gates; commit `feat(campaign-planner): contracts, snapshot, flags and review hook [G0]`.

PROVE-MODE: harness (`CampaignSnapshotTests`) for the classifier, store and snapshot reuse; in-game at G7 (the `first-review` line with plausible counts for both alliances; `[CampaignPlanner]` absent for the W&L CIC player's alliance).

---

## G1 — EnemyArmyModel, detection, ledgers on beliefs

**Owned files:** create `Strategic/Planner/EnemyArmyModel.cs` (pure), `Strategic/Planner/EnemyBeliefQuery.cs` (pure: strength within radius, nearest, centroid), `Strategic/Planner/EnemyDetectionRuntime.cs` (runtime); modify `Strategic/FrontSectorRuntime.cs` (`:37` enemy read), `Strategic/DefenseIntentRuntime.cs` (`:140`, `:456`), `Patches/FortConstructionGovernorPatch.cs` (`:301-304` enemy read only), `Strategic/CoastalDefenseCustomOrderRunner.cs` (`:102`), `StrategicCoordinator.UpdateCapitalThreat` (`:643` enemy loop only), `Strategic/Planner/CampaignPlannerRuntime.cs` (insert step 1); test `EnemyArmyModelTests.cs`.

**Vanilla to read:** `CreateFOWObjects` 116505-116517; `AssignUnitToFOW` 116520-116534; `UpdateViewRange` 101274-101365 (campaign radius 101296-101307); `DrawVisibility` 101057-101132 (campaign concealment 101116-101128); `UnitIsHidden` 101179-101229; `CheckRegimentVisibility` 127205-127239 (argument order 127225); `InitFOW` 100755-100795 (`usefow`); `PositionIsWithinFog` 101246-101263; `SeparateUnits` 10458-10515; `CheckEnemyContactCampaign` 115620+ (bugle-range contact); campaign march speed fields (find the per-unittyp campaign speed used by `Regiment` movement and cite it for `DriftPerDay`, P6); `IsAtWarWith`; `EuropeanUnits` 15761-15800.

**Interfaces — Produces:**
```csharp
public sealed class EnemyArmyModel {
  public const int ExpireDays = 30; public const float HalfLifeDays = 7f; public const float MinConfidence = 0.05f;
  public void Update(System.Collections.Generic.IReadOnlyList<EnemyDetection> seen, int daySerial,
                     System.Func<int, float> driftPerDay /* by unittyp */);          // review only
  public System.Collections.Generic.IReadOnlyList<EnemyBelief> Beliefs { get; }
  public void Restore(System.Collections.Generic.IEnumerable<EnemyBelief> beliefs); // sidecar (G6)
}
public static class EnemyBeliefQuery {
  public static float StrengthWithin(System.Collections.Generic.IReadOnlyList<EnemyBelief> b, float x, float z, float radius); // confidence-weighted
  public static int Nearest(System.Collections.Generic.IReadOnlyList<EnemyBelief> b, float x, float z, out float distance);
}
public static class ViewerCoverage { // pure: an enemy counts as seen on the shared fows[1] only near an own or allied viewer (spec C14)
  public static bool Covers(float enemyX, float enemyZ, float[] viewerX, float[] viewerZ, float[] viewerRadius, int count);
}
internal static class EnemyDetectionRuntime {
  internal static void Detect(CampaignSnapshot s, int playerAlliance, bool fogHonest);  // fills s.Detections
  internal static EnemyArmyModel ModelFor(int alliance);                               // CampaignScope store
  internal static System.Collections.Generic.IReadOnlyList<EnemyBelief> BeliefsFor(int alliance); // for migrated ledgers
}
```

**Detection rule (exact; spec component 2):** for each enemy campaign group `e` (alliance at war with the side, active, visibility state ≠ 0, not a fleet), visible when:
- `!GameVars.usefow` or `!fogHonest` → `NoFog` (omniscient, telemetry `mode=NoFog`/`ConfigOff`);
- else side ≠ `playeralliance`: `!ownUnit.fow.UnitIsHidden(e.GetPosition(), GameVars.commander[e.commander].GetCommanderCunning(), (float)e.strength / GamePrefs.unittypesize[e.unittyp], e.liedown, e.coverobject, e.covervalue, e.unittyp, e)` (bound-check `commander` and `unittyp`, pass −1 when out of range, as the battle core does) **and** some own or allied viewer (visibility state ≠ 0, active, not routed) lies within its campaign radius (101296-101302, computed with vanilla's own terms) of `e` → `FogGrid`;
- else side == `playeralliance` (W&L): `!e.iswithinfog` → `OwnSideWithinFog`;
- always: `e.inbattle` against the side, or `e` within an own formation's `buglerange` → `Contact`;
- every enemy-held objective town → one `ObjectiveGarrison` detection at the town (public knowledge).
Strength = `groupstrengthdirect`, position = `GetPosition()` (never `GetLastTransmittedPathPos`).

**Steps:**
- [ ] Plugin: bind `Enable Campaign Fog-Honest Beliefs` (G0 text above), read by `EnemyDetectionRuntime.Detect`.
- [ ] Tests first (`EnemyArmyModelTests`): `EuropeanPaintedCellWithoutViewerStaysUnseen` (`ViewerCoverage.Covers` false with no viewer in radius), `ViewerCoverageUsesEachViewersOwnRadius`, `SeenEnemyGetsFullConfidence`, `UnseenConfidenceHalvesEverySevenDays`, `UnseenRadiusGrowsByDrift`, `ExpiresAtThirtyDaysOrBelowMinConfidence`, `ReappearingEnemyRefreshes`, `NoFogMarksEverythingSeen`, `GarrisonBeliefFromObjectiveTown`, `StrengthWithinIsConfidenceWeighted`, `NearestIgnoresExpired`, `ModelUpdateIsIdempotentForSameDay`, `EmptyDetectionsKeepsMemory`.
- [ ] Implement the pure model and query.
- [ ] Implement `EnemyDetectionRuntime` (scope `campaign.planner.detect`, 2.0) with the rule above; `usefow` gated before any `UnitIsHidden` call; viewer-radius check uses a per-review pooled list of own/allied viewer positions and radii. Telemetry row `EnemyBeliefs` on change: `[EnemyBeliefs] alliance=<a> mode=<FogGrid|OwnSideWithinFog|NoFog|ConfigOff> seen=<n> remembered=<n> expired=<n> contact=<n>` (signature-deduped per alliance).
- [ ] Migrate readers to `EnemyDetectionRuntime.BeliefsFor(alliance)` (confidence-weighted strength, belief position): `FrontSectorRuntime.cs:37`, `DefenseIntentRuntime.cs:140,456`, `FortConstructionGovernorPatch.cs:301-304`, `CoastalDefenseCustomOrderRunner.cs:102`, `StrategicCoordinator.UpdateCapitalThreat` (`:643-657`; `CapitalForceSample` from beliefs). `ReadLocalForceBalance` (`:713`) is not migrated: G6 deletes it with phase truth. When the planner did not run for that alliance today (`Enable Campaign Planner` or `Enable Campaign Fog-Honest Beliefs` off), `BeliefsFor` builds full-confidence beliefs from vanilla's omniscient `enemyunits` on demand (the documented rollback), so the ledgers behave as today; that branch is the only other `enemyunits` read.
- [ ] Tests for the migrated pure parts where they exist (`DefenseIntentLedger` fixtures keep passing; add `FrontSectorFromBeliefsExcludesUnseen` in `EnemyArmyModelTests` against `FrontSectorLedger.Build`).
- [ ] Gates; commit `feat(campaign-planner): fog-honest enemy beliefs and ledger migration [G1]`.

PROVE-MODE: harness for the model/query and the front-sector fixture; in-game at G7 (`[EnemyBeliefs]` rows with `mode=FogGrid` for the non-player AI and `mode=OwnSideWithinFog` in a W&L career; `remembered > 0` after an enemy army leaves sight). Mutation plant: drop the viewer-radius check (`Covers` returns true) → `EuropeanPaintedCellWithoutViewerStaysUnseen` goes red.

---

## G2 — CommanderProfiles, appointments, #6 guard

**Owned files:** create `Strategic/Planner/CommanderProfiles.cs` (pure), `Strategic/Planner/CommanderAppointmentPlanner.cs` (pure), `Strategic/Planner/AppointmentProtection.cs` (pure, persisted), `Strategic/Planner/CommanderAppointmentRuntime.cs` (runtime executor); rewrite `Patches/CommanderReplacementPatch.cs` (#6 → guard, P13); modify `Strategic/SuccessionScheduler.cs` (add `PriorFor`, see Steps; `MarkApplied` is called from the review's executor only), `Strategic/Planner/CampaignPlannerRuntime.cs` (insert step 2 and the appointment executor step); test `CommanderPlannerTests.cs`. Plugin: bind `Enable Commander Appointment Planner`.

**Vanilla to read:** `GameVars.Commander` 58926-59026; getters 61134-61251 and `parentInfluence` 61043-61103 (the `commander < 0` → 0 case 61068-61070); `GetAverageAttributeValue` 59175; `CanBePickedForAIReplacement` 60748-60779; `CheckSuitableRank` 60556-60628; `IsServingUnderPlayer` 60695; `CheckAICommanderReplacements` 17008-17032; `UpdateCommanderRankingsCampaign` 84424-84487; `ReplaceCommanderOfUnit` 81389-81486; `AssignCommando` 60343 (+ Whiskey #30); `DoCommanderPromotion` 89077-89110; `ArmyGroup.AppointCommander` 75559-75580; `GetAvailableArmyGroupCommanders` 75546; `UpdateCommanderInChief` 81315-81387; `CheckVerticalReplacement` 17662-17687.

**Interfaces — Produces:**
```csharp
// CommanderProfile itself is declared in G0's contracts (G3 consumes it before G2 merges).
public static class CommanderProfiles {
  public static CommanderProfile From(in CommanderRecord c);
  public static CommanderProfile Neutral { get; }
  public static CommanderProfile ForSector(System.Collections.Generic.IReadOnlyList<CommanderRecord> cs, int commanderId); // Neutral when unknown
}
public struct AppointmentIntent { public int CommanderId; public int TargetInstanceId; public bool TargetIsArmyGroup; public int DisplacedId; public string Reason; /* "competence" | "succession:<id>" */ }
public static class CommanderAppointmentPlanner {
  public const float MinGain = 0.10f; /* = vanilla expdifferenceforverticalpromotion, so job 24 would agree */ public const float SuccessionPriorBonus = 0.25f;
  public static bool TryPlan(TheaterPlan plan, CampaignSnapshot s, System.Func<int, string, float> successionPrior /* (commanderId, role) → bonus */,
                             AppointmentProtection protection, out AppointmentIntent intent); // at most one per review (P8)
}
public sealed class AppointmentProtection { public const int ProtectionDays = 60;
  public void Protect(int commanderId, int daySerial); public bool IsProtected(int commanderId, int daySerial);
  public System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int, int>> ProtectedUntil { get; } // commanderId → last protected day serial
  public void Restore(System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int, int>> protectedUntil); }
```
Appointment rule: candidate commands = the main-effort sector's top formations (army group, army, corps) in importance order; for each, the best available commander (`Available && !Navy && !Dummy && !RefreshCommand && !ServingUnderPlayer && !IsPlayer`, rank within the target unittyp's `RankBand` from the snapshot, the rank part of `CheckSuitableRank` 60556-60628; the remaining `CheckSuitableRank` terms are re-checked live by the executor before it acts) whose `Average + successionPrior − current.Average ≥ MinGain` displaces the current commander; never a player-owned command; never when the current commander is protected.

**Executor recipe (runtime, daily review):** army group → `ArmyGroup.AppointCommander(group, id, usefeudmechanism:false, resetcoordinationtime:true, ignoreprestige:true)` after reseating the appointee's old group from `GetAvailableArmyGroupCommanders` (as today's #6 does); corps/army HQ → `ReplaceCommanderOfUnit(appointee.currentcommand)` when he has one, `AssignCommando(target)`, `DoCommanderPromotion(id)` (clears `refreshcommand`, 89110). Then `AppointmentProtection.Protect`, `SuccessionScheduler.MarkApplied` for a succession reason, telemetry `[Appointment] alliance=<a> commander=<name> unit=<name> reason=<competence|succession:<id>> displaced=<name>`. Verify the result (`group.GetCommanderID()` / `target.commander`) before recording.

**#6 rewrite (guard):** Prefix on `CheckAICommanderReplacements(int _aifaction)` (17008): for the faction's alliance (0/1), snapshot `GameVars.alliance[a].worstcommanderidperlevel` and set every slot holding a protected commander to −1; Finalizer restores the snapshot. No skip, no state write. `PatchFirstFire.Mark("campaign.patch.commander-replacement")` (id kept), scope `campaign.patch.commander-replacement`.

**Steps:**
- [ ] Tests first: `ProfileHighInitiativeLowersRequiredOdds`, `ProfileNeutralForUnknownCommander`, `AppointmentNeedsMinGain`, `SuccessionPriorTipsANearTie`, `SuccessionPriorCannotBeatALargeCompetenceGap`, `NeverAppointsPlayerOrPlayerSubordinate`, `NeverTargetsPlayerOwnedCommand`, `ProtectedCommanderNotDisplaced`, `ProtectionExpiresAfterSixtyDays`, `OneAppointmentPerReview`, `AlliancesTwoToFourIgnored`.
- [ ] Implement pure files; `SuccessionScheduler` gains `float PriorFor(int alliance, string combinedName, string role, WarStateView w)` (fired-not-applied events give `SuccessionPriorBonus`) — the event list and gates stay; `CheckEvents` is unchanged.
- [ ] Implement the executor and the #6 guard; delete #6's swap path (`TryApplyScriptedSwap`, `FindReplacementId`, `PickSuccessor` move into `CommanderAppointmentRuntime`; `AllianceGroups`, `BuildCandidates`, `IsPlayerGroup`, `GroupCommandedBy`, `FreeApartFromArmyGroup` move with them; `CommanderAssignmentGuard` keeps only what the executor and #30 call — orphan-sweep proves it). `Force All Succession Events` still marks all events fired (coordinator) and the appointment planner then treats them as fired-not-applied.
- [ ] Gates; commit `feat(campaign-planner): commander profiles, appointments, job-24 guard [G2]`.

PROVE-MODE: harness (`CommanderPlannerTests`); in-game at G7 (`[Appointment]` rows; a protected appointee is not replaced by vanilla job 24 within 60 days — `[first-fire] campaign.patch.commander-replacement` and no vanilla "AI: replacing commander <appointee>" debug line for him).

---

## G3 — SectorModel, LogisticsGate, HistoricalPriorTable, TheaterPlanner

**Owned files:** create `Strategic/Planner/SectorModel.cs`, `Strategic/Planner/LogisticsGate.cs`, `Strategic/Planner/HistoricalPriorTable.cs`, `Strategic/Planner/TheaterPlanner.cs`, `Strategic/Planner/PlannerState.cs` (hysteresis + RNG serial, persisted) (all pure); modify `Strategic/Planner/CampaignPlannerRuntime.cs` (steps 3-4); test `TheaterPlannerTests.cs`. Reads (no edit): `HistoricalOperationCatalog.BuildProfiles` (source data for the prior table; G6 deletes the catalog after copying the table), `ContactEvidenceLedger` (adopted here as the sector contact class), `BattleHistoryQuery`, `TheaterClassifier.FromPosition`.

**Vanilla to read:** `CalculateMostValueableAIZones` 10964-11009 (value terms); `GetClosestAreaDistance` 14835; `UpdateIIPSupplyLink` 114074-114500 (supply reach; 114272-114306); `GetGroupSupplyState` 14063-14066; `SupplyInZoneAvailable` 10941-10962; `AssignUnitToDefendCapital` 11712-11741 (capital need formula); `IDAccomplished` / `GetAvailableObjectives`; `CheckIfFinalWaypointIsPassable` 25608-25671; `IsTerrainPositionRestricted` 25755-25806.

**Interfaces — Produces:**
```csharp
public sealed class SectorModel { public static SectorModel Build(CampaignSnapshot s); // P11 flood fill, deterministic
  public int SectorOfArea(int areaIndex); public int SectorAt(float x, float z); public int Count { get; }
  public WhiskeyRealism.Strategic.Theater TheaterOf(int sector); public void Centroid(int sector, out float x, out float z); }
public struct ReachResult { public bool InReach; public float Slack01; public int NearestSourceId; public float NearestSourceDistance; }
public static class LogisticsGate {
  public static ReachResult Evaluate(CampaignSnapshot s, float targetX, float targetZ, float formationCommandRange, float supplySlack);
  public static void AddDepotTasks(CampaignSnapshot s, TheaterPlan plan, SectorModel sectors); // along the main axis, ≤ 2 per plan
}
public static class HistoricalPriorTable { public struct Entry { public string Id; public int Alliance; public int[] ObjectiveIds; public int StartSerial, EndSerial; public int Priority; }
  public static System.Collections.Generic.IReadOnlyList<Entry> Entries { get; }
  public static float Weight(in Entry e, int daySerial); // 1 inside, 0.5^(monthsOutside/3) outside, × Priority/3
  public static float PriorFor(int alliance, int objectiveId, int daySerial, out string id); }
public sealed class PlannerState { public int CurrentMainSector = -1; public int CurrentMainObjective = -1; public int ChallengerObjective = -1; public int ChallengerDays;
  public int DecisionSerial; public long CampaignSeed;
  public void Restore(int mainSector, int mainObjective, int challengerObjective, int challengerDays, int decisionSerial, long campaignSeed); }
public static class TheaterPlanner {
  public const float ValueWeight = 0.30f, BalanceWeight = 0.25f, ReachWeight = 0.15f, DistanceWeight = 0.10f, ExposureWeight = 0.10f, CapitalThreatWeight = 0.10f;
  public const float PriorLambda = 0.08f, PriorMaxContribution = 0.08f, SwitchMargin = 0.12f, Epsilon = 0.04f, Temperature = 0.02f, CapitalThreatTrigger = 0.6f;
  public const int SwitchDays = 3; public const float HoldRatio = 0.6f;
  public static TheaterPlan Plan(CampaignSnapshot s, SectorModel sectors, System.Func<int, CommanderProfile> sectorProfile,
                                 PlannerState state, System.Collections.Generic.IReadOnlyList<BattleHistoryRecord> battles, out string trace);
}
```
Score per available objective (one per sector candidate): value (objective importance + role bonus), believed balance near it (own strength within relocation range ÷ (own + `EnemyBeliefQuery.StrengthWithin`)), reach (`LogisticsGate.Evaluate` for the strongest candidate formation; out of reach → score 0 for advance, depot task instead), distance (1 − normalized march distance of the candidate force), exposure (believed enemy strength near the axis flank), capital threat (own capital: believed enemy strength within relocation range ÷ own floor; enemy capital: bonus), + prior. Personality: the sector commander's `RequiredOddsShift` lowers `RequiredRatio` (base `aidominanceforoffensiveoperations`, read into the snapshot) and `AttackBias` scales the balance term. Postures: MainEffort sector; Support = sectors adjacent to the main axis; Hold = sectors with believed enemy and own formations; EconomyOfForce = sectors with own formations and no believed threat; Screen = sectors facing a believed enemy stronger than `HoldRatio`× own; Recover = sectors whose formations average supply < 0.4 or morale below `aiminimummoraleformovement`.

**Steps:**
- [ ] Tests first: `SectorsDeterministicAcrossRebuilds`, `SectorTheaterFromCentroid`, `ReachTrueWithOwnIipInCommandRange`, `ReachFalseBeyondSourcesYieldsDepotTask`, `EnemyOwnedIipDoesNotSupply`, `PriorInsideWindowIsFull`, `PriorDecaysOutsideWindow`, `LiveMarginAbovePriorMaxWins`, `PriorBreaksNearTie`, `HysteresisHoldsBelowMargin`, `HysteresisSwitchesAfterThreeDays`, `CapitalThreatSwitchesImmediately`, `AccomplishedObjectiveSwitchesImmediately`, `SeededChoiceReproducible`, `RandomOnlyWithinEpsilon`, `BoldCommanderAcceptsWorseOdds`, `UnseenEnemyDoesNotLowerBalance`, `EmptySnapshotProducesNoMainEffort`, `AllianceTwoReturnsEmptyPlan`, `PostureAssignmentCoversEverySectorWithOwnFormations`.
- [ ] Implement; scope `campaign.planner.plan` (2.0). Telemetry `CampaignPlan` on change: `[CampaignPlan] alliance=<a> main=<theater>/<objectiveId> ratio=<r> sectors=<main:1,support:n,hold:n,eof:n,screen:n,recover:n> switch=<reason|hold> prior=<id|none> depots=<n>`.
- [ ] Gates; commit `feat(campaign-planner): sectors, logistics gate, prior and theater planner [G3]`.

PROVE-MODE: harness; mutation plants: prior weight 0.5 → `LiveMarginAbovePriorMaxWins` red; `SwitchDays = 0` → `HysteresisHoldsBelowMargin` red; omniscient balance → `UnseenEnemyDoesNotLowerBalance` red. In-game at G7: one `[CampaignPlan]` row per alliance on the first review and rows only on change after that.

---

## G4 — ForceAllocator

**Owned files:** create `Strategic/Planner/ForceAllocator.cs` (pure), `Strategic/Planner/HomeTheaterPrior.cs` (pure; data from `HistoricalArmyAreaRegistry`, which G6 deletes); modify `Strategic/Planner/CampaignPlannerRuntime.cs` (step 5); test `ForceAllocatorTests.cs`.

**Vanilla to read:** `IsUnitAvailableForOffensiveOperations` 14080-14158; `AssignUnitToDefendCapital` 11668-11869 (need, pruning 11751-11781); `UnitIsFightingForce` 16867; `CheckTransferOfUnits` 17232-17332 (what replacement flow can do); W&L: `GetCampaignGroup` 82991-83020, 8271, `IsMovedByPlayer` 45797.

**Interfaces — Produces:**
```csharp
public static class ForceAllocator {
  public const float MinGarrisonStrength = 5000f; public const float UnreliableBelow = 0.5f; public const int NonComplianceDays = 5;
  public const float HomeTheaterTieBreak = 0.05f;
  public static Allocation Allocate(TheaterPlan plan, CampaignSnapshot s, SectorModel sectors, float playerReliability01,
                                    int playerNonCompliantDays, System.Func<string, WhiskeyRealism.Strategic.Theater> homeTheater);
}
```
Rules: busy formations (in battle, retreat, raid, sea invasion, fort builder, vanilla depot builder, `MovedByPlayer`, `PlayerOwned`, `PlayerCommand`, transfer) keep `Hold` in their current sector and are never moved; CapitalGuard first up to `plan.CapitalFloorStrength` (closest fighting formations; never a map-group player formation when the floor would depend on it alone); Hold sectors get `max(HoldRatio × believed enemy, MinGarrisonStrength)` from formations already there, then the nearest; DepotBuilder one small formation per depot task (lowest strength fighting formation within the sector); Recover formations as assigned by the plan; the rest MainEffort (main sector) / Support (support sectors) by distance with the home-theater tie-break; Reserve = what exceeds `RequiredRatio × believed enemy` at the main target by more than 30%. Player: `BelowMapLevel` → the player's parent map group is an ordinary formation (but `PlayerParentGroup` is recorded so G5a lets F's `MoveUnitTo` Postfix dispatch); `MapGroup` → its formation is discounted by `playerReliability01` in every strength sum, never the sole CapitalGuard or sole MainEffort holder, and after `NonComplianceDays` its role is covered by another formation; `CommanderInChief` → empty allocation (stand down).

**Steps:**
- [ ] Tests first: `BusyFormationsKeepHold`, `PlayerCommandNeverAllocatedAMove`, `CapitalGuardFilledFirstUpToFloor`, `HoldSectorKeepsHoldRatio`, `EconomyOfForceKeepsMinimumGarrison`, `RemainderGoesToMainEffort`, `ReserveIsSurplusOverRequiredRatio`, `DepotBuilderIsSmallestFormationInSector`, `MapGroupPlayerDiscountedByReliability`, `MapGroupPlayerNeverSoleMustHold`, `MapGroupPlayerCoveredAfterNonComplianceDays`, `BelowMapLevelParentIsMovable`, `CicPlayerYieldsEmptyAllocation`, `HomeTheaterBreaksTies`, `AllocationDeterministic`.
- [ ] Implement; scope `campaign.planner.allocate` (2.0). Telemetry `Allocation` on change: `[Allocation] alliance=<a> main=<n> support=<n> hold=<n> reserve=<n> capital=<n> depot=<n> recover=<n> busy=<n> player=<class> reliability=<r>`.
- [ ] Gates; commit `feat(campaign-planner): force allocator [G4]`.

PROVE-MODE: harness; in-game at G7 (`[Allocation]` rows; in a W&L corps career `player=MapGroup` and the player's corps never listed as the only `capital` holder).

---

## G5a — Mover and steering executors (after G1-G4 merge; W&L part after stage F merges)

**Owned files:** create `Strategic/Planner/MoveTracker.cs` (pure: issued/confirmed/failed/retry/give-up), `Strategic/Planner/CampaignMoverRuntime.cs` (runtime), `Strategic/Planner/CampaignSteeringRuntime.cs` (runtime: objective write, `theaterposition` writer), `Patches/CampaignAvailabilityFilterPatch.cs` (new; two classes); rewrite `Patches/ImportanceValuesPatch.cs` (#2); modify `Patches/PickCampaignObjectivePatch.cs` (transitional only: the executor-flag stand-down at the top of `Prefix`; its `SnapshotWrittenByMod`/`RestoreWrittenByMod` statics stay and the objective executor writes the mod-written value through them, so the coordinator's sidecar calls at 1817/1859 keep persisting it until G6 moves the field); modify `Strategic/Planner/CampaignPlannerRuntime.cs` (executor step); test `CampaignExecutorTests.cs`.

**Vanilla to read:** `MoveUnitTo` 14479-14540 and the queue 9933, 9965-10052, 79295; `CheckIfFinalWaypointIsPassable` 25608; `IsTerrainPositionRestricted` 25755; `unitsinoffensiveoperations` handling 14383-14385, `UpdateMicroMovementInOffensive` 13968-14061, `AdjustOffensiveOperationsSpeed` (11350), `CheckStanceSetting` 11893-11930; `CheckForEndOfDefensiveOperations` 13782-13848 (why never the defensive list); `IsUnitAvailableForOffensiveOperations` 14080-14158 (Postfix target, public static); `IsUnitAvailableForDefensiveOperation` 13460 (private static, Postfix by name with the full parameter list); `CalculateMostValueableAIZones` 10964-11009 and the readers 14206-14228; `PickCampaignObjective` 17769-17789 and 14922/14961/12337; `UpdateCampaignTheaters` 17034-17110 (`theaterposition`, box 17090-17110); W&L: 8251-8275, 8366, 14381-14456 (order-instead-of-move), stage F's `WlCampaignDispatchRuntime` and its `MoveUnitTo` Postfix.

**Interfaces — Consumes:** G0 store/snapshot, G4 `Allocation`, G3 `TheaterPlan`; stage F: `PlayerComplianceLedger.ReliabilityScore` (pure, `Strategic/PlayerComplianceLedger.cs`) and the campaign dispatcher API F publishes for a map-group player's order (expected shape `WlCampaignDispatchRuntime.TryIssue(Regiment group, int type, Vector3 target, string reason)`; adapt to F's merged signature and record it here under "As built").
**Produces:**
```csharp
public enum MoveOutcome { None = 0, Issued = 1, Confirmed = 2, Failed = 3, Retried = 4, GaveUp = 5, SkippedPlayer = 6, SkippedBusy = 7, Queued = 8 }
public sealed class MoveTracker { public const int MaxMovesPerDay = 6; public const float ConfirmFraction = 0.25f;
  public bool ShouldIssue(int instanceId, float tx, float tz, int daySerial);
  public void RecordIssued(int instanceId, float fromX, float fromZ, float tx, float tz, int daySerial, bool queued);
  public MoveOutcome Judge(int instanceId, float nowX, float nowZ, float waypointX, float waypointZ, int daySerial);
  public bool IsWithheld(int instanceId); /* Reserve, CapitalGuard, DepotBuilder, in transit */ }
public static class CampaignWithholdView { public static bool IsWithheld(int alliance, int instanceId); } // read by the availability Postfixes
```

**Executors (inside the review, executor flag on):**
- **Objective (spec C6):** write `aifaction[f].followedcampaignobjective = plan.MainObjectiveId` when it differs and the objective is available; with no main effort, reset a mod-written value to −1 so vanilla re-picks (17780-17786). Records the mod-written value through `PickCampaignObjectivePatch`'s existing written-by-mod statics (still persisted by the coordinator at 1817/1859); G6 moves that state into the executor and deletes the patch.
- **Mover:** for each MainEffort/Support/CapitalGuard/Recover/DepotBuilder assignment whose formation is not where its role needs it: validate the target (`CheckIfFinalWaypointIsPassable` semantics; skip a restricted target), then `AICampaign.MoveUnitTo(unit, target, movearoundenemyunits: true)`; on `true` add MainEffort/Support units to `unitsinoffensiveoperations` (never `unitsindefensiveoperations`); `false` with a running order → `Queued`, retried next review. Judged next review (P8). W&L: `PlayerCommand`/`PlayerOwned`/`MovedByPlayer` never moved (`SkippedPlayer`); a map-group player's assignment goes to F's dispatcher (order type 16 for MainEffort into enemy territory, 5 otherwise, 8 CapitalGuard, 10 DepotBuilder), deduped by F; the below-map-level parent is moved normally and F's `MoveUnitTo` Postfix dispatches. Telemetry `[CampaignMover] alliance=<a> issued=<n> confirmed=<n> failed=<n> retried=<n> gaveUp=<n> queued=<n> skippedPlayer=<n>` once per review when any count > 0.
- **Theater box (spec C8):** for a MainEffort/Support formation already inside its sector, set `theaterposition` to the point toward its target that keeps the formation inside the box (`|unit − center| < theatersize` on both axes); for Hold formations, center on the formation. At most twice per formation per week.
- **Importance pointers (#2 rewrite, spec C7):** Postfix on `AIArea.CalculateMostValueableAIZones(int aifaction)`: if `this` area lies in a MainEffort or Support sector of that faction's alliance (0/1, bound-checked), point **both** `mostvalueableaiareaclose[f]` and `mostvaluealbeaiareaclosedistanceonly[f]` at the area containing the sector target, only when that area passes vanilla's own gates from `this` (the distance and `ownpoints <= supplycapacity` checks of 10984; reuse `CampaignMilitaryGates.ShouldRedirectToPlanTarget`, re-keyed on the plan). Reads `CampaignPlanStore` only. First-fire id kept: `campaign.patch.importance-values`.
- **Availability (new `CampaignAvailabilityFilterPatch`):** Postfix on `IsUnitAvailableForOffensiveOperations` and on `IsUnitAvailableForDefensiveOperation`: `__result = false` when `CampaignWithholdView.IsWithheld(alliance of the faction, unit)` (Reserve, CapitalGuard, DepotBuilder, in-transit). Never sets `true`. First-fire ids `campaign.patch.planner-availability.offensive`, `campaign.patch.planner-availability.defensive`. Rollback: `Enable Campaign Planner Executors`.

**Steps:**
- [ ] Tests first (`CampaignExecutorTests`): `MoveCapRespectsMaxMovesPerDay`, `MoveConfirmedWhenWaypointChangesTowardTarget`, `MoveFailedThenRetriedThenGaveUp`, `QueuedMoveIsNotAFailure`, `PlayerCommandNeverMoved`, `MovedByPlayerNeverMoved`, `MapGroupPlayerGetsOrderNotMove`, `WithheldRolesAreExactlyReserveCapitalDepotTransit`, `TheaterBoxKeepsUnitInside`, `TheaterBoxRateLimited`, `ObjectiveWriteOnlyWhenChanged`, `ObjectiveResetOnlyWhenModWritten`, `ImportanceRedirectOnlyForMainOrSupportSectors`, `ImportanceRedirectRequiresVanillaGates`.
- [ ] Implement pure tracker + views; runtime executors; the patches with scopes `campaign.patch.importance-values`, `campaign.patch.planner-availability.offensive|defensive` (2.0).
- [ ] Until G6, the mover refuses formations `CoordinatedOperationRuntime.IsPackageLocked` reports and #1 is short-circuited by the objective executor's flag (P10): when `Enable Campaign Planner Executors` is on, `PickCampaignObjectivePatch.Prefix` returns `true` at once (G6 deletes the patch).
- [ ] Gates; commit `feat(campaign-planner): mover, objective, theater and availability executors [G5a]`.

PROVE-MODE: harness; in-game at G7 (`[CampaignMover]` with `confirmed > 0`; `[first-fire]` for both availability ids and `campaign.patch.importance-values`; a W&L brigade career: the parent corps moves, a dispatch panel appears (F), the player's brigade is never moved by itself).

---

## G5b — Capital floor and replacement flow (+ career transfer fix, user-gated)

**Owned files:** rewrite `Patches/DefensiveOpsPatch.cs` (#4 → capital floor; reads the allocation's CapitalGuard choice and beliefs), rewrite `Patches/TransferOfUnitsPatch.cs` (#3 → reads the plan's main-effort target and beliefs), create `Patches/WlCareerTransferFixPatch.cs` (new, two classes; only if C1 approved), `Strategic/Planner/CapitalFloorPolicy.cs` (pure), `Strategic/Planner/ReplacementFlowPolicy.cs` (pure); modify `Strategic/CampaignMilitaryGates.cs` (capital/transfer helpers re-keyed on the plan); test `CapitalAndReplacementTests.cs`. Plugin: bind `Enable W&L Career Transfer Fix` (if approved).

**Vanilla to read:** `AssignUnitToDefendCapital` 11668-11869 (need 11712-11741; prune 11751-11781; add 11791-11817; move 11819-11867); `ZoneRecruiting` 12405 / heatmap deficit 12467-12520; `CheckTransferOfUnits` 17232-17332 (17327-17328); `BattleUnits.MoveUnit` 93091-93150 (guard 93099; `SetTransfer` 93147); `GrabExistingGroup`; `CheckCombinationOfUnits` 17158 (`skipdlccheck: true` precedent).

**Interfaces — Produces:**
```csharp
public static class CapitalFloorPolicy {
  // 11712-11741 with beliefs in place of enemyunits: minStrength + Σ min(str, str×morale)/max(10,d)^0.3×0.5 over beliefs within range − own str×morale closer than the nearest enemy
  public static float VanillaNeed(float minStrengthForMovement, System.Collections.Generic.IReadOnlyList<EnemyBelief> beliefs, float believedMorale,
                                  float capitalX, float capitalZ, float relocationRange, System.Collections.Generic.IReadOnlyList<FormationRecord> own);
  public static int PickPledge(CampaignSnapshot s, Allocation a, float vanillaNeed); /* instanceId or -1 */ }
public static class ReplacementFlowPolicy { public static bool ShouldSteer(TheaterPlan plan, float deficitEnemy, float planShortfall, out float targetX, out float targetZ); }
```

**#4 rewrite (Prefix, Priority.High, non-skip; unchanged rule):** pre-pledge the allocation's nearest CapitalGuard formation into `groupstodefendcapital` only while vanilla's own need (`num2`, recomputed by `CapitalFloorPolicy.VanillaNeed` from beliefs, the 11712-11741 formula) is > 0 and the formation passes vanilla's 11791 filters; never a player-owned or custom-ordered (`CoastalDefenseCustomOrderRunner.IsCustomOrdered`) unit. The excess floor stays CapitalGuard (G5a mover + availability). First-fire id kept `campaign.patch.defensive-ops`. Telemetry `[CapitalFloor] alliance=<a> vanillaNeed=<x> floor=<y> pledged=<unit|none>` on change.

**#3 rewrite (Prefix/Postfix/Finalizer, snapshot-restore, unchanged mechanism):** swap `positiondeficit` for the main-effort target when `ReplacementFlowPolicy.ShouldSteer` (plan shortfall at the target, computed from beliefs, worse than vanilla's deficit); Finalizer restores. Ids kept `campaign.patch.transfer-of-units.prefix|postfix|finalizer`.

**Career transfer fix (C1, only if approved):** `WlCareerTransferScopePatch` Prefix/Finalizer on `CheckTransferOfUnits` sets/clears a `[ThreadStatic]` scope when `DLC_WL.dlc_scenarioactive`; `WlCareerTransferMoveUnitPatch` Prefix on `BattleUnits.MoveUnit(GameObject, GameObject, ref bool skipdlccheck)` sets `skipdlccheck = true` while the scope is set **and neither unit is `dlcw_isundercommander`** (never re-parents a player unit). No skip; vanilla's body runs. First-fire ids `campaign.patch.wl-career-transfer.scope`, `campaign.patch.wl-career-transfer.move-unit` (situational: careers only). `OnceLog.Info("campaign-planner:wl-transfer-fix-first", "[WlTransferFix] career transfer allowed unit=<name> to=<name>")`. If declined: `ReplacementFlowPolicy.AvailableInCareer = false` and the steering Prefix stands down in careers.

**Steps:**
- [ ] Tests first: `PledgeOnlyWhileVanillaNeedPositive`, `PledgeNeverPlayerOrCustomOrdered`, `VanillaNeedMatchesFormulaFixture` (fixture reproducing 11712-11741), `ExcessFloorStaysCapitalGuard`, `SteerOnlyWhenPlanShortfallWorse`, `NoSteerWithoutMainEffort`, `CareerFixNeverForPlayerUnits` (pure decision `WlTransferFixDecision.Allow(bool scope, bool career, bool srcOwned, bool dstOwned)`).
- [ ] Implement; gates; commit `feat(campaign-planner): capital floor, replacement flow, career transfer fix [G5b]`.

PROVE-MODE: harness; in-game at G7 (`[CapitalFloor]` rows; in a W&L career `[once:campaign-planner:wl-transfer-fix-first]` and vanilla's transfer debug line followed by the unit actually changing parent).

---

## G5c — Construction steering: depots, forts, railroads (railroads user-gated)

**Owned files:** create `Strategic/Planner/DepotTaskLedger.cs` (pure: Marching → Wished → Built | Abandoned), `Strategic/Planner/DepotSteeringRuntime.cs` (runtime), `Strategic/Planner/RailLineScorer.cs` (pure), `Patches/RailroadLineSteeringPatch.cs` (new, only if C11 approved); modify `Patches/FortConstructionGovernorPatch.cs` (axis site preference; enemy read already on beliefs from G1), `Strategic/Construction/FortConstructionGovernor.cs` (pure site-preference rule); test `ConstructionSteeringTests.cs`. Plugin: bind `Enable Supply Depot Steering`, `Enable Fort Steering`, `Enable Railroad Steering` (if approved).

**Vanilla to read:** `CBuilding.AddConstructionWish` 97479-97536; `Place` 96163-96257 (cost); `WorkDownConstructionWishes` 97538-97591; `IncreaseCondition` 96607-96738 (builder in bugle range 96626-96655); building ids 95083-95121; `CheckSupplyDepotConstruction` 14659-14779; `CheckFortConstruction` 16347-16405, `FortConstructionOrder` 9636-9675, 9814-9865, `fortconstructionsites` 81931-81943; `UpdateRailroadConstruction` 16052-16074; `Railroad.StartConstruction` 77818-77845; `UpdateConstruction` 77859-77893.

**Interfaces — Produces:**
```csharp
public enum DepotTaskState { Marching = 0, Wished = 1, Built = 2, Abandoned = 3 }
public struct DepotTaskRecord { public float X, Z; public int SectorId; public int BuilderInstanceId; public DepotTaskState State; public int StartedDay; }
public sealed class DepotTaskLedger { public const int TimeoutDays = 20; public const int MaxActivePerAlliance = 1;
  public void Sync(TheaterPlan plan, Allocation a, int daySerial);
  public bool ShouldWish(int taskIndex, float builderX, float builderZ, float bugleRange, int daySerial);
  public void RecordWished(int taskIndex, int daySerial); public void RecordBuilt(int taskIndex); public void Abandon(int taskIndex, string reason);
  public bool HoldsBuilder(int instanceId);
  public System.Collections.Generic.IReadOnlyList<DepotTaskRecord> Tasks { get; }
  public void Restore(System.Collections.Generic.IEnumerable<DepotTaskRecord> tasks); }
public static class RailLineScorer { public static float Score(in RailLineRecord r, TheaterPlan plan, SectorModel sectors); } // axis distance, threatened sector, main-effort supply reach gain
```
Depot executor: the DepotBuilder (G4) marches (G5a mover); on arrival within its `buglerange` of the task site, `CBuilding.AddConstructionWish(CBuilding.id_supplydepot, site, nearestOwnIip, alliance)` once (P8), skipped when the IIP already has `currentlyunderconstruction` (97479) or the treasury cannot pay (read the `Place` cost path first); the builder stays withheld (G5a `IsWithheld` via `HoldsBuilder`) until the depot's `constructiontimer <= 0` (`Built`) or `TimeoutDays` (`Abandoned`). Telemetry `[DepotTask] alliance=<a> site=<x>,<z> builder=<unit> state=<Marching|Wished|Built|Abandoned> reason=<…>`. Vanilla's job 19 is unchanged.
Fort steering: #27's filter additionally hides, for the call, `fortconstructionsites` farther than `rangefortconstructionsite` from every MainEffort axis / Hold-or-Screen sector boundary **when at least one axis/threatened site is otherwise eligible** (snapshot-restore as today); vanilla's one-order-per-alliance rule and builder logic are untouched.
Railroad steering (C11 option a, approved only): Prefix (bool) on `UpdateRailroadConstruction(int alliance, float timediff)`: returns `true` (vanilla) for `alliance == playeralliance && !ai_vs_ai`, `alliance >= 2`, the W&L CIC player's alliance, no plan, or the flag off; otherwise replicates 16058-16066 exactly, rolls `Random.Range(0f, 1f) <= num * timediff` once per line `j` with `constructionprogress <= 0` exactly as 16069, and for each success starts the best-scored line `k` with `StartConstruction(alliance, checkonly: true)` true and progress ≤ 0 (`k = j` when none scores higher), each line at most once per call; returns `false`. Header comment carries the spec's written justification. First-fire `campaign.patch.railroad-steering`. Telemetry `[RailroadSteer] alliance=<a> rolled=<j> started=<k> score=<s>`.

**Steps:**
- [ ] Tests first: `DepotWishOnlyOnArrivalWithinBugleRange`, `DepotWishOncePerTask`, `DepotBuilderHeldUntilBuilt`, `DepotAbandonedAfterTimeout`, `OneActiveDepotTaskPerAlliance`, `FortPreferenceOnlyWhenAxisSiteEligible`, `FortPreferenceKeepsVanillaSitesOtherwise`, `RailScorePrefersMainAxis`, `RailSteeringPreservesSuccessCount` (pure replica of the roll loop with a seeded RNG: same number of starts as vanilla's loop), `RailSteeringStandsDownForPlayerAndEurope`.
- [ ] Implement; gates; commit `feat(campaign-planner): depot, fort and railroad steering [G5c]`.

PROVE-MODE: harness; in-game at G7 (`[DepotTask]` Marching → Wished → Built for one task; `[RailroadSteer]` rows only for AI alliances; #27 `[Patch:FortGovernor]` lines unchanged in shape).

---

## G6 — Removals, migrations, sidecar v3, coordinator rewiring (integrator, sequential)

**Owned files:** `StrategicCoordinator.cs`, `PersistenceDto.cs`, `SidecarDtoCodec.cs`, `Plugin.cs` (removals; also rewrite the `Override Vanilla Settings` description, `Plugin.cs:776`, which says the locked difficulty feeds the CIC personality), `Patches/MonthlyTickHookPatch.cs` (its `CIC` reference, `:24`), `Strategic/DefenseIntentRuntime.cs` (`BuildInput(… cic: …)` at `:53`, `PlayerIsCIC = cic == null` at `:72` → `PlannerEligibility`), `Strategic/DifficultyPersonalityModifier.cs`, `Telemetry/TelemetryTagPolicy.cs` (removals), `Patches/RecruitmentPatch.cs`, `Patches/PerkSelectionPatch.cs`, `Patches/ArmyGroupManagementPatch.cs`, `Strategic/ArmyGroupDoctrine.cs`, `Strategic/FormationDirectiveLedger.cs`, `Strategic/FormationDirectiveRuntime.cs`, `Strategic/DefenseIntentRuntime.cs` (ArmyArea/FrontSector key reads only), `Strategic/Construction/ConstructionRuntime.cs` (`ArmyAreaClassifier` read), `tests/…/TelemetryPipelineTests.cs` (source-path list), the test csproj, every file listed for deletion, and the existing test files that test deleted code (`CampaignLifecycleFixTests.cs`, `StrategicOperationsTests.cs`, `StrategicMapTests.cs`, `CampaignMilitaryFixTests.cs`, `StrategicDirectorTests.cs`; port still-valid assertions to the planner test files).

**Relocate first (each into its own small file, same namespace; tests follow):**
- `OperationPosture` (from `HistoricalOperationModels.cs`; read by `Tactical/TacticalCommanderIntent.cs:19-92`, #47 `BattleCommanderIntentObserverPatch.cs:141,513`, `TacticalB6Tests`) → `Strategic/OperationPosture.cs`.
- `CicSettings` (CIC.cs:12-18): `OverrideVanillaSettings`/`LockedDifficulty`/`HistoricalOperationDoctrine` lose their only reader (`CIC.Effective`/`Replan`) and are deleted with the coordinator's `RefreshStrategicSettings` lines 528-532; `PlanTrace` moves to `Strategic/Planner/PlannerSettings.cs` (read by `TheaterPlanner`'s trace emission). `DifficultyPersonalityModifier` (read by `CIC.Effective` and the coordinator) is deleted when orphan-sweep shows no production reader after `CIC` goes (the survey lists `TacticalPlaybookTests` as a test-only reader: port or drop those assertions); otherwise kept with its reader named in the commit.
- `CicEligibility` (CIC.cs:26-40) → `Strategic/Planner/PlannerEligibility.cs` (renamed `PlannerEligibility.ShouldRun`), used by `StrategicCoordinator.ShouldRunCicFor` (renamed `ShouldRunPlannerFor`).
- `BattleReplanQuery` + `BattleInvalidatesPlan` (CicReviewRouter.cs:14, 183) → `Strategic/Planner/PlanTriggers.cs` (G3's trigger reads it; adjust G3's import in this slice).
- `CampaignResponsivenessSignals` + `CapitalForceSample` (HistoricalOperationContextBuilder.cs:20-91) → `Strategic/CampaignResponsivenessSignals.cs`.
- `VanillaFieldCache` (CampaignMilitaryGates.cs:296+) → `Strategic/VanillaFieldCache.cs`.
- `CampaignObjectiveSteering.InferModWritten` → G5a's objective executor (sidecar restore of the mod-written objective).

**Delete (files):** `ObjectiveScoring.cs`, `ObjectiveCatalog.cs`, `ObjectiveStrategyTagger.cs`, `ObjectiveMetadata.cs` (after `PerkSelectionPatch`/`RecruitmentPatch` migrate), `CIC.cs`, `Phase.cs`, `PhaseTruthLedger.cs`, `OperationDynamicRuleEvaluator.cs`, `CicReviewRouter.cs`, `OperationDecisionMemory.cs` (incl. `ProbeHysteresis`), `HistoricalOperationCatalog.cs`, `HistoricalOperationModels.cs` (after the `OperationPosture` move), `HistoricalOperationContextBuilder.cs` (after the move), `HistoricalArmyAreaRegistry.cs`, `DynamicArmyAreaDoctrine.cs`, `ArmyAreaRuntime.cs`, `ArmyAreaLedger.cs`, `ArmyAreaClassifier.cs`, `ArmyAreaDoctrine.cs` (after `ArmyGroupDoctrine` migrates), `OperationalProbeLedger.cs`, `OperationalProbeRuntime.cs`, `OperationalTempoDoctrine.cs`, `CoordinatedOperationPackageLedger.cs` (incl. `TempoVerdict`/`CoordinatedOffensiveVanillaGates`, whose only reader #38 goes), `CoordinatedOperationRuntime.cs`, `OffensiveAvailabilityWrapper.cs` (G5a's Postfix replaces the mirror), `Patches/PickCampaignObjectivePatch.cs` (#1), `Patches/ArmyAreaTheaterPatch.cs` (#15), `Patches/CoordinatedOffensiveOperationsPatch.cs` + `Patches/CoordinatedOffensiveMicroMovementPatch.cs` (#38). Kept: `WarStateObserver.cs` (succession gates), `StrategicMovementBudget.cs` (`DefenseIntentLedger:335,355` still reads it; its `ArmyAreaRuntime:139` caller goes). Each deleted pure file's csproj line goes; each deleted test goes or is ported.
**Delete (members):** `StrategicCoordinator`: `CICs`, `BuildCICForAlliance` (hardcoded Lincoln/Davis), `BuildPhaseTruth`, `ReadLocalForceBalance`/`SumStrengthWithin`, `UpdateArmyAreaLedger`, `UpdateOperationalProbe`, `ArmyAreas`, `OperationalProbes`, `OperationMemories`, `_operationalProbe*`, `_probeHysteresis`, `_armyArea*`, `ArmyAreaSourceSignature`, `DirtyPlanIfBattleInvalidates`/`OnEventTrigger` (battle triggers go through `CampaignPlannerRuntime.OnBattleRecorded`), `MinorOfficerProfiles` (dead), the `[Heartbeat]` `cic=`/`plan=` fields (replaced by `main=<theater>/<objective>`); `StrategicResilienceDirector.ApplyTo`; `FormationDirectiveLedger.ApplyOperationalProbe`; `SuccessionScheduler` stays (prior). `FrontSectorRuntime.BuildForAlliance(alliance, targetObjectiveId)` takes the plan's main objective instead of the CIC phase target.
**Migrate:** `RecruitmentPatch.BuildIntent` (124-141) and `PerkSelectionPatch` (118-127) read `CampaignPlanStore` (main-effort sector theater/target, formation role) instead of `CIC.ActivePlan` + `ObjectiveCatalog`; `ArmyGroupManagementPatch` (#16) + `ArmyGroupDoctrine.PlanGroups` group top formations by the allocation's sector instead of `ArmyAreaLedger`; `FormationDirectiveRuntime` area keys from `SectorModel` instead of `ArmyAreaRuntime.AreaKey`; `DefenseIntentRuntime:652` same; `ConstructionRuntime:293` same; `TransferOfUnitsPatch` (already G5b).
**Flags:** delete `Enable Historical Operation Doctrine` (no reader); keep `Plan Trace Logging` and repoint it to the planner's per-candidate trace (`TheaterPlanner.Plan(..., out trace)` emitted as a `CampaignPlan` Trace row; description rewritten); remove P10 stand-down branches. Telemetry tags removed: `HistoricalOperation`, `OperationalProbe`, `CoordinatedOps`, `ArmyArea`, `Plan*` (verify each has no emitter left).

**Sidecar v3 (`PersistenceDto.cs`, `SidecarDtoCodec.cs`):**
- `SidecarDto.CurrentVersion = 3`; readers accept 1..3. History comment: `3 = campaign planner: plan state, beliefs, task ledgers; CIC plans dropped`.
- `FactionDto` loses `cic`, `probeState`, `probeHysteresis`, `directorMemory.recentOperationReplanDaySerials`; keeps `currentEra`, `directorMemory` (pace/risk/streak), `fiscalMemory`, `frontChangedDay`, `capitalStreakDay`, `modWrittenObjective` (now written by G5a's objective executor); gains `planner` (`PlannerStateDto`: `currentMainSector`, `currentMainObjective`, `challengerObjective`, `challengerDays`, `decisionSerial`, `campaignSeed`), `beliefs` (`List<EnemyBeliefDto>`: instanceId, unitTyp, x, z, strength, lastSeenDay, confidence, radius, source), `appointments` (`List<AppointmentDto>`: commanderId, protectedUntilDay), `depotTasks` (`List<DepotTaskDto>`: x, z, sectorId, builderId, state, startedDay), `moves` (`List<MoveDto>`: instanceId, tx, tz, issuedDay, retries).
- `SidecarDto` loses `minorOfficerProfiles` (dead state).
- **Migration from v1/v2:** Newtonsoft ignores the removed members, so a v1/v2 file decodes with no planner state: plans dropped, beliefs empty (rebuilt on the next review), hysteresis fresh, `campaignSeed` derived from the save folder name on first review. `PlanFromDto`/`MatchesCatalog` are deleted with the catalog; `InferModWritten` keeps working off `modWrittenObjective` and the live `followedcampaignobjective`. Load logs `[Coordinator] sidecar v<n> migrated to v3: plans dropped=<n>` once.
- Stage E adds need/policy fields to `FactionDto` in parallel; they are additive and version-agnostic — keep them when rebasing.
- Tests (`SidecarMigrationTests` in `CampaignLifecycleFixTests.cs` or a new `PlannerSidecarTests.cs`): `V2SidecarWithActivePlanLoadsWithoutPlan`, `V1SidecarLoads`, `V3RoundTripsPlannerState`, `V3RoundTripsBeliefsAndTasks`, `FutureVersionRejected`, `MinorOfficerProfilesNoLongerWritten`.

**Steps:**
- [ ] Relocations first (one commit, harness green). Then migrations (one commit). Then deletions + sidecar (one commit). Then flags/tags.
- [ ] `TelemetryPipelineTests.cs:347-375`: drop the deleted source paths, add `Strategic/Planner/CampaignPlannerRuntime.cs`, `CampaignMoverRuntime.cs`, `Patches/CampaignAvailabilityFilterPatch.cs` (and the new patches) to whatever invariant that test asserts (read it first).
- [ ] Grep gates (paste in the report): `rg -n "enemyunits" src/WhiskeyRealism/Strategic src/WhiskeyRealism/Patches` → only `Strategic/Planner/EnemyDetectionRuntime.cs` and the beliefs rollback branch; `rg -n "\"Lincoln\"|\"Davis\"|ActivePlan|CurrentPhase|ObjectiveCatalog|ArmyAreaRuntime|OperationalProbe|CoordinatedOperation" src` → nothing; `rg -n "MoveUnitTo\(" src` → `CampaignMoverRuntime.cs`, `CoastalDefenseCustomOrderRunner.cs`, `CheckForDefensiveOperationsCandidateFilterPatch.cs` (revert path) only; `rg -n "followedcampaignobjective" src` → `CampaignSteeringRuntime.cs` and the coordinator's sidecar restore only.
- [ ] Gates (repo-wide orphan-sweep 0); commit `refactor(campaign-planner): remove phased plans, probe and army-area layers; sidecar v3 [G6]`.

PROVE-MODE: harness (all suites green after deletions; sidecar migration tests red first); in-game at G7 (load a pre-G6 save: the migration line, no `[Coordinator] sidecar load rejected`).

---

## G7 — Integration, docs, smoke (integrator, last)

**Owned files:** `docs/*`, `AGENTS.md` (locked choices #3-#6 rewritten per the spec; architecture paragraph; campaign write owners), `MEMORY.md`, `docs/patch-catalog.md` (ordinals for new patches in merge order; removed rows for #1, #15, #38; rewritten rows #2, #3, #4, #6, #27; runtime rows for `CampaignPlannerRuntime`/`CampaignMoverRuntime`/`DepotSteeringRuntime`/`CommanderAppointmentRuntime`; `WlStrategicOrderBridge` row as F leaves it), `docs/handoff.md`, `docs/telemetry.md` (new events), `docs/historical-operation-doctrine.md` (prior semantics), `docs/strategic-recon-commitment.md` (beliefs shipped), `docs/wl-dispatch-objective-bridge.md` + `docs/wl-player-order-doctrine.md` (planner invariants: map-group player = order-only, below-map-level parent movable, CIC stand-down), `docs/bug-fixes/README.md` (BUG-WL-003, BUG-ECO-004/005 status), `tools/smoke-situational.txt`, the test csproj (final reconciliation).

- [ ] Wiring checks: every review runs snapshot → detect → beliefs → profiles → plan → logistics → allocation → appointments → executors in that order (`CampaignPlannerRuntime` source pin test `PipelineOrderPinned` in `CampaignExecutorTests`); every planner patch reads only `CampaignPlanStore`/`CampaignWithholdView`.
- [ ] Gates: build 0 warnings, harness all PASS, config-audit 0, orphan-sweep 0 (and `--include-test-only` nothing new), `test_smoke_check.py` OK.
- [ ] Build, deploy, hash-verify (AGENTS.md); hand the smoke checklist to the user. After the smoke: archive this plan and the spec, rewrite cross-references, record the result in `docs/handoff.md`.

### G7 smoke checklist (in order)

`LOG` = `<GTCW>/BepInEx/LogOutput.log`; `SESSION` = newest `<GTCW>/BepInEx/WhiskeyRealism/tuning-logs/<session>/` (campaign rows in `SESSION/campaign*.jsonl`).

0. **Precondition.** Deployed DLL hash = `dist/WhiskeyRealism.dll`; cfg `[Campaign Planner]` flags all `true`.
1. **Session A — normal campaign (player Union, CSA AI), 20x, 3 in-game months, one battle.**
   1. `python3 -I tools/smoke-check.py "$LOG"` exit 0; `[first-fire]` for `campaign.patch.importance-values`, `campaign.patch.planner-availability.offensive`, `campaign.patch.planner-availability.defensive`, `campaign.patch.defensive-ops`, `campaign.patch.transfer-of-units.prefix`, `campaign.patch.commander-replacement`, `campaign.patch.fort-governor.prefix`, and `campaign.patch.railroad-steering` (if approved).
   2. `[once:campaign-planner:first-review:1] [CampaignPlanner] first review alliance=1 formations=<n> areas=<n> objectives=<n> supply=<n> rails=<n> commanders=<n> player=NotCareer` (no line for alliance 0 outside `ai_vs_ai`).
   3. `[EnemyBeliefs] alliance=1 mode=FogGrid seen=<n> remembered=<n> expired=<n> contact=<n>` with `seen` below the Union's total army count early in the war, `remembered > 0` later.
   4. `[CampaignPlan] alliance=1 main=<theater>/<objectiveId> …` on the first review, then only on change; no flapping (≤ 1 main-effort change per 3 days unless `switch=capital-threat|accomplished|battle`).
   5. `[Allocation] alliance=1 …` with `main > 0` and `capital > 0`; `[CampaignMover] alliance=1 issued=<n> confirmed=<n> …` with `confirmed/issued ≥ 0.5` over the session and `gaveUp` small.
   6. `[Appointment]` rows at most one per day; `[DepotTask]` reaching `Built` at least once in 3 months (or `Abandoned` with a reason); `[RailroadSteer]` rows (if approved).
   7. Performance (`SESSION/performance*.jsonl`): `campaign.planner.review` meanMs ≤ 4 at 20x, `campaign.planner.detect` ≤ 1, `campaign.daily-review` not worse than before G0 by more than 3 ms mean.
   8. **Absent:** `campaign-planner:snapshot-degraded`, `campaign-planner:*-failed`, any Harmony exception, `[Coordinator] sidecar load rejected`.
   9. Save, reload: `[Coordinator] sidecar loaded`, the next `[CampaignPlan]` keeps the same `main=` (hysteresis state persisted).
2. **Session B — W&L brigade career.** `[CampaignPlanner] … player=BelowMapLevel`; `[EnemyBeliefs] alliance=<player alliance> mode=OwnSideWithinFog`; the player's parent corps moves under the planner and a dispatch panel names the superior (F); the player's brigade never moves by itself; `[once:campaign-planner:wl-transfer-fix-first]` (if approved). With the player commandless (hierarchy −1) no `[CampaignMover]` rows until a new command.
3. **Session C — W&L corps career.** `player=MapGroup`; the corps receives orders (dispatch panel), is never moved by Whiskey, and is never the only `capital` holder; after 5 ignored days its role is covered (`[Allocation]` change).
4. **Session D — W&L CIC career.** No `[CampaignPlanner]`/`[CampaignPlan]` for the player's alliance; the enemy side's rows continue.
5. **Rollback spot check.** `Enable Campaign Planner Executors = false`: plan/allocation rows continue, no `[CampaignMover]`, `[Appointment]`, `[DepotTask]`, `[RailroadSteer]`, `[CapitalFloor]` rows. `Enable Campaign Planner = false`: no planner rows at all; vanilla `PickCampaignObjective` picks again.

---

## Self-review (spec coverage)

| Spec item | Slice |
|---|---|
| Snapshot, live objective positions, catalog coordinates removed | G0, G6 |
| `EnemyArmyModel` on vanilla campaign fog (fows[1] / iswithinfog / contact / no-fog), decay, garrisons | G1 |
| Ledgers on beliefs (incl. C13 list) | G1 (+ G5b #4, G6 deletion of `ReadLocalForceBalance`) |
| `CommanderProfiles` from live vanilla commanders; hardcoded CIC removed | G2, G6 |
| Sub-theater sectors, live scoring, prior, hysteresis, seeded randomness, personality thresholds | G3 |
| `LogisticsGate` + depot tasks | G3, G5c |
| `ForceAllocator` incl. W&L classes and reliability | G4, G5a |
| `CommanderAppointmentPlanner` competence + succession prior; #6 guard | G2 |
| Executors: objective, importance pointers, theater box, availability, mover, construction (depots/forts/railroads), capital floor, replacement flow, W&L dispatch | G5a, G5b, G5c |
| Removals/folds/relocations, sidecar migration | G6 |
| Hierarchy −1 pause, CIC stand-down | G0 (gates), G7 smoke |
| Docs in-slice (AGENTS.md locked choices, doctrine docs, W&L docs, catalog) | G7 |
| Stage H needs (theater axes, coastal objective) | `SectorPlan.AxisFrom*` and `TheaterPlan` via `CampaignPlanStore` (G0/G3) |
