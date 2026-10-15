# Battle Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Use superpowers:test-driven-development for every pure-logic change. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement roadmap stage D (`docs/superpowers/specs/2026-10-15-battle-core-design.md`, binding; it absorbs §A-§C of `2026-10-14-wl-chain-of-command-design.md`). The stage builds one pooled per-side `BattleSnapshot` per doctrine tick, adds fog-honest AI perception from vanilla's own AI-side fog grid, and introduces one `PlayerOwnership` model. A single `OrderArbiter`/`OrderExecutor` pair becomes the only write path for battle movement, stance, formation, facing and combat behavior. Every mover migrates to intents with no leftover private guards. An infantry charge doctrine fills the vanilla gap.

**Architecture:** New folder `src/WhiskeyRealism/Tactical/Battle/`. Pure contracts, snapshot, perception memory, ownership, chain planner, dispatch mapper, arbiter, validator and charge doctrine sit in the test csproj. Runtime adapters (`*Runtime.cs`, `BattleSnapshotBuilder.cs`, `BattleCoreTick.cs`) do not. Intent producers run inside one arbiter tick on the main thread. Harmony patches either steer vanilla's own inputs, or call the executor inline through the same guarded pipeline. Patches never submit intents to the arbiter and never write doctrine state.

**Tech stack:** C# netstandard2.1, BepInEx 5.4 + HarmonyX, Unity 2021.3.16f1 Mono x64. Console harness `tests/WhiskeyRealism.Tests` (net8.0).

**Base:** branch `feat/battle-core`, cut from `feat/orchestrator-worker-thread` at `08f4a3c` or later. Worktree `.worktrees/feat-battle-core`, then `ln -s ../../refs refs`. Each workstream works on its own branch `feat/battle-core-<ws>` cut from `feat/battle-core` after BC0 merges. Merge back sequentially in the dependency order below.

**Inputs (read before your workstream):** the spec; `docs/vanilla-vision-and-movement.md` (R1-R5); `docs/wl-player-model.md`; `docs/superpowers/specs/2026-10-14-wl-chain-of-command-design.md` §A-§C; root `AGENTS.md`; `src/WhiskeyRealism/{Tactical,Patches}/AGENTS.md`; `tests/WhiskeyRealism.Tests/AGENTS.md`. Vanilla decompile `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (build 14703111). If `/tmp` was wiped, regenerate it per `docs/findings.md`.

## Global Constraints

- **Vanilla first.** Before touching code that reads or writes vanilla state, read the full vanilla body and its callers. Cite anchor lines in the file header comment and in the commit message. Code not grounded in the vanilla body is rejected in review.
- **Never throw from a patch.** Every reflection lookup is cached in a static field, wrapped in try/catch, and logs one bounded `OnceLog.Warning`. Restores go in `[HarmonyFinalizer]`.
- **`TelemetryPerf.Scope` on every patch entry.** Wrap every static Prefix/Postfix/Finalizer body in `using (TelemetryPerf.Scope("tactical.patch.<short-name>", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))`. Use 5.0 for heavy paths: the arbiter patch and the snapshot build.
- **Static OnceLog keys.** Never embed an instance id or name in an OnceLog key (13bfeec).
- **Scoped state.** Every static per-battle cache registers `BattleScope.Register("<name>", Reset)` from its static constructor (`Util/ScopedState.cs`). No hand-wired resets.
- **One doctrine clock.** Every doctrine timer reads `TacticalDoctrineTime.NowBattleHours` / `NowBattleSeconds`, which is night-frozen. There is one exception: vanilla's own `lastaistancechangetime` stamp stays on `GameVars.currenttimefromstart`, because vanilla compares it with that clock (4264). Real time is allowed only for telemetry throttles and CPU budgets.
- **All-on config.** Every bool flag defaults `true`, and descriptions state the real default. Rollback is `Tactical Commander Mode = MonitorOnly/Off`. Only BC0 (adds) and BC7 (removes) edit `Plugin.cs`.
- **No dead code.** `python3 -I tools/orphan-sweep.py .` returns 0 findings and `--include-test-only` lists nothing this slice added. `python3 -I tools/config-audit.py .` returns 0 findings. Both must pass at the end of each workstream for the files it touched, and repo-wide at BC7.
- **Tests.** Each workstream owns one test file `tests/WhiskeyRealism.Tests/<Name>Tests.cs` declaring `internal static class <Name>Tests { public static void Run() }`. Harness auto-discovery (parallel Stage B branch) is assumed, so add no `Program.cs` registration line. Add a `<Compile Include>` for every new pure file and remove the entry for every deleted file. Use `TestHarness.AssertEqual/AssertTrue/AssertFalse`. Allocation tests use `GC.GetAllocatedBytesForCurrentThread()` after a warm-up call (pattern: `TelemetryInfraFixTests.cs:118`). Write the failing test first, see it fail, then implement.
- **Pure means no Unity or vanilla types.** Positions are `float X, Z` (Y only where height matters). `CommandTaskType` (Tactical.Operations, pure) is allowed.
- **Exclusive file ownership.** Each workstream edits only its listed files, plus its own test file and csproj lines. Needing another file means stopping and reporting it.
- **Verify before reporting.** Run `./build.sh` (0 errors, 0 warnings) and `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` (all PASS), and paste the counts. Run `git log --oneline -10 && git status` before each commit. Commit messages follow `feat(battle-core): <summary> [BCn]`, then the anchors, then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **No new Transpilers.** No new Prefix-skips beyond those listed in the BC5 inventory.

## Spec decisions resolved by this plan

| # | Open point | Decision |
|---|---|---|
| D1 | Threading the snapshot to consumers | `BattleCoreTick.EnsureSnapshot(AIBattle, int alliance)` builds at most once per `(battleKey, alliance, Time.frameCount)`. The coordinator calls it first in `TacticalBattleCoordinatorRuntime.Tick`, which runs from the `TacticalObserverPatch.CheckGlobalAIStrategyPostfix` (vanilla 5866). The arbiter patch at 5870 reuses that object in the same `UpdateAITasks` call. Pure consumers take `BattleSnapshot` as a parameter. Patches read `BattleCore.TryGetSnapshot(alliance, out BattleSnapshot)`, a read-only accessor. Each side has two pooled instances (current and previous) so the executor can compare outcomes against the last tick. |
| D2 | Memo per tick | `BattleSnapshot.TickId` increments per build. Derived per-unit values (closest visible enemy, slice pressure) are computed once by the assembler into pooled arrays, never lazily. `TacticalFrameCache` in `TacticalFogOfWarContact` is deleted, because the snapshot is the memo. |
| D3 | Arbiter tick vs vanilla `UpdateAITasks` (5857-5872) | One Postfix on `AIBattle.AdjustGroupFormations` (5870) at `Priority.Last`. This is the hook #61 uses today, and #61's Postfix is deleted. It runs after vanilla's own movers in the same call: `UpdateMovingTargets` 5862, `CheckFlankMoves` 5867, `AdjustGroupAIStance` 5869, `AdjustGroupFormations` 5870. Whiskey's winning orders are therefore the last macro-cycle write, using a snapshot built at 5866 in the same frame. It is gated by `PostureExecutorBattleGate.Allows` (state ≥ 5, macroai ≠ 3 → `ReleaseForRetreat`, eod, feud instance, player instance unless `ai_vs_ai`) and by `TacticalBattleCoordinator.DrivesOrchestrator`. `CheckForFeudGroupActions` (5871) runs after it and is the feud instance only. |
| D4 | Collecting intents without patches writing mod state | (a) **Producers:** every Whiskey-originated order comes from an `IBattleIntentProducer` run inside the arbiter tick over the snapshot. (b) **Inline steering:** patches that must act inside a vanilla micro call (#56 courier parity, #42 feud replay) build a `BattleOrderIntent` and call `OrderExecutor.ExecuteInline(...)`. That call runs the same guard → chain → validation → pre-check → dedupe → call pipeline. It is refused when a higher-priority claim exists for that group and channel in the arbiter's published claim table, which is read-only to patches. Executor bookkeeping (claims, outcomes, cooldown stamps) is write-path state owned by the executor. It is not doctrine/orchestrator state, so the AGENTS.md "patches READ" rule holds. |
| D5 | Command tree source | Each group's `allattachedunits` (all descendants, 119840+) is scanned once per roster change. A child's direct parent is the containing group with the smallest `allattachedunits.Length`. This needs no `transform.parent` (AGENTS.md rule) and no `GetAttachedUnitsReg` allocation (it uses `Array.Resize` per element). It reproduces vanilla's own direct relation, because `GetAttachedUnitsReg(directonly)` tests `transform.parent == this`. Disagreements with vanilla's `Transform.IsChildOf` ancestry (used by `CheckCurrentOrderUpdate` 8272 and `IsUnitUnderCommander` 91203) are counted and logged once per battle as telemetry. |
| D6 | Queued orders vs the spec's "outcome next tick" | Under `useorderdelay`, `SetWaypoint` queues through `AddToOrderQueue` (91483-91510) but still reaches `SetGroupFormation`/`RegimentSetPath` in the same call (91540/91559, `newpath:false`), and `AddPath` adds the path at once (`regimentpaths++` 130519) with `pathstatus` 0 until the courier arrives (131243-131246). So there is no courier wait to observe and no `Pending` outcome: a move is `Moved` when its targets' moving-unit count rose (at the call or by the next tick) or vanilla took the target (waypoint stamp, step 8), else `Failed` at the next tick (a silent exit; the pre-checked ones never reach the call). (Revised in the BC4 review: the first draft assumed `regimentpaths` stayed 0 until delivery.) |
| D7 | Map bounds clamp (spec says `[1, size−2]`) | Vanilla 131057-131068 clamps **x to [1, sizeX−2] and z to [−sizeZ+2, −1]**, because the battle z axis is negative. `WaypointValidator` uses that rule. It reuses `VanillaMapBounds.ClampXZ` (moved from `CommandPostureExecutor.cs:381`) with a parity test against the vanilla lines. |
| D8 | Ladder vs today's division-play override | The spec ladder is binding: Executor (#61) beats Play. Today the division play overrides the ledger task inside #61 (patch L156-172). After the migration, a division-play order only wins for a child where the posture producer submits no intent on that channel. This behavior change is recorded in the BC5a commit and in `docs/tactical-operations-ledger.md`. |
| D9 | §C dispatch vs existing #62 | `PlayerSubordinateOrderPatch` (#62) already issues the player's own order through `CheckCurrentOrderUpdate`. Both #62 and the executor's §C ancestor dispatch call the one emitter, `TacticalPlayerDispatchRuntime.Emit`. It owns the single `PlayerOrderDedupeState`, moved out of #62. That gives one dedupe for (type, target within 110 m) and no double pop-up. |
| D10 | Charge mechanics (spec §4b) | Vanilla charges only when `combatbehavior == 3 && movementmode == 0 && regimentpaths > 0` (122207). Its target is `unitrange.enemyinrangereg[0]` (122224), which is unsorted (122840) and range-only. Its own strength trigger is `GamePrefs.microaitriggerforcharge`, with weights `weightingfactorsformicroaicharge[0..3]` and radius `GamePrefs.maxchargeradius` (122229-122259). Therefore: (1) behavior 3 is written per **regiment**, never on the group, because micro cycle 6 (5617-5638) re-orders a group whose `combatbehaviorordered != 2`; (2) the doctrine fires only when the regiment is moving and `enemyinrangereg[0]` is the visible target; (3) vanilla's own trigger is not duplicated, and Whiskey's margins are added on top. |
| D11 | Snapshot vs `TacticalUnitObservationAggregate` | `BattleSnapshot` implements `IObservationSource`. `TacticalUnitObservationAggregate(.Runtime)` and its `Shared` singleton are deleted. So are the legacy per-unit objective-record path (`TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromBattle`) and its flag `EnableSinglePassObjectiveRecords`. |

## Spec claims the code contradicts (recorded; the plan follows the code)

- **#46 "objective-chain replay"** no longer exists: `BattleObjectiveChainWlGuardPatch` was deleted in `06cc0b6`. Nothing to migrate.
- **`EnableFriendlyFireDetection`, `EnableFriendlyPathBlockingGate`, `EnableRegimentalIntelligenceWrites` and `TacticalRegimentalThreatComputer`** are already gone on `08f4a3c` (Stage B dead-code pass). The arbiter rules are new code, not replacements for live flags.
- **#61 already sets explicit battle NavMesh area costs** before `CalculatePath` (patch L1658-1673 via `VanillaPathAreaCosts`), so R5 is already satisfied. BC5a keeps that code and pins it with a test.
- **Movers missing from the spec's migration list:**
  - `B8CheckUseOfReservesPatch`: `RegimentSetPath` plus direct `combatbehavior`/`combatbehaviorordered` writes at L196-215.
  - `TacticalDeploymentTerrainDisciplinePatch` (#60): `SetGroupFormation` at L349, deployment phase.
  - #61's `BlockedMovingOrder` path (L1038-1041) and `groupsubordinatesmoving` stamps (L1118).
  All three are covered below.
- **The `TacticalStanceWriter` hold record** is on the raw clock (L63), not the doctrine clock. BC4 moves the record to the doctrine clock and keeps only vanilla's stamp raw.

## Dependency order

```
BC0 contracts + config (sequential, lands first)
  ├─ BC1 snapshot ─┐
  ├─ BC2a perception (pure)    ─┐
  ├─ BC3 ownership/chain/dispatch
  ├─ BC4 arbiter/executor/validator/tick
  └─ BC6a charge doctrine (pure)
BC2b evidence consumers → snapshot   (needs BC1)
BC5a posture+plays | BC5b stance+artillery+feud | BC5c reserves+withdrawal   (need BC1-BC4; parallel, disjoint)
BC6b charge producer wiring   (needs BC4, BC5b)
BC7 integration
```

Shared-file exceptions to exclusive ownership. These are sequential by merge order, and each workstream edits disjoint regions:
- `TacticalBattleCoordinatorRuntime.cs`: BC4 changes the `Tick` entry, BC2b the evidence/sector reads, BC5b `PopulateResolvedDecisionsForSide`'s stance mapping.
- `Patches/TacticalObserverPatch.cs`: BC2b changes the perception reads, BC5a the L979 posture re-decide.

Merge order into `feat/battle-core`: BC0, BC1, BC3, BC4, BC2a, BC2b, BC6a, BC5c, BC5b, BC5a, BC6b, BC7.

---

## BC0 — Contracts and config (integrator, sequential)

**Owned files:** create `src/WhiskeyRealism/Tactical/Battle/BattleCoreContracts.cs` (pure); modify `src/WhiskeyRealism/Plugin.cs` (two new flags only); modify the test csproj (one line).

**Interfaces — Produces (every other workstream consumes these):**
```csharp
namespace WhiskeyRealism.Tactical.Battle {
  [System.Flags] public enum UnitFlags : uint { None = 0, Alive = 1, Active = 2, Routed = 4, OnRetreat = 8, InMelee = 16,
    PlayerOwned = 32, AncestorOfPlayerCommand = 64, PlayerCommandDetached = 128, Feud = 256, PermanentlyDetached = 512,
    Garrisoned = 1024, IsGroup = 2048, IsArtillery = 4096, IsCavalry = 8192, IsInfantry = 16384, PlayerCommand = 32768, LiedDown = 65536,
    MarkedForRout = 131072 /* added in BC1: vanilla markedforrout, excluded from the front-axis leaf sample as shipped */ }
  public struct UnitRecord {
    public int InstanceId; public int UnitTyp; public int Alliance; public int Commander;
    public float X, Y, Z, RotationY; public int Strength; public float GroupStrengthDirect;
    public int GroupOwnInRange, GroupStrengthAiGroup; /* added in BC1: the shipped command-tree strength bucket source */
    public float Morale, BattleStartMorale, Ammo01, Fatigue, Experience;
    public int RegimentPaths, QueuedOrders, MovementMode, AiStance, AiStanceOrdered, Formation, CombatBehaviorOrdered;
    public float LastWaypointX, LastWaypointZ; public int ParentIndex; /* -1 root */ public int FirstEnemyInRangeId; /* enemyinrangereg[0] or 0 */
    public UnitFlags Flags;
  }
  public enum PerceptionMode { FogGrid = 0, RangeOnly = 1, OwnSideWithinFog = 2 }
  public enum PerceptionFallbackReason { None, FogDisabled, HqInTheSaddle, GridNotPainted, ConfigOff }
  public enum ObservationState : byte { Visible = 1, Remembered = 2 }
  public struct EnemyObservation { public int InstanceId; public int UnitTyp; public float X, Z; public float Strength;
    public ObservationState State; public double LastSeenHours; public float Confidence01; public int HiddenTicks; public bool IsReinforcement; }
  public enum OrderChannel { Move = 0, Stance = 1, Formation = 2, Facing = 3, CombatBehavior = 4, Stop = 5 }
  public static class OrderChannels { public const int Count = 6; }
  public enum IntentPriority { Scorer = 1, Support = 2, Play = 3, Executor = 4, Safety = 5 }
  public enum IntentSource { PostureExecutor, FacingPulse, FireControl, Play, DivisionPlay, Withdrawal, ArtilleryDoctrine,
    ArtilleryPointBlank, ReserveRelease, ReserveCourierParity, GroupStanceScorer, ChargeDemotion, ChargeDoctrine, FeudReplay }
  [System.Flags] public enum IntentFlags { None = 0, UseOrderDelay = 1, ReliefOrSupport = 2, ArtilleryFirePosition = 4,
    InContactWithdrawal = 8, DoubleQuick = 16, Limber = 32, Unlimber = 64, PerRegiment = 128 }
  public struct BattleOrderIntent {
    public int GroupId; public OrderChannel Channel; public float TargetX, TargetZ; public float Heading; /* -1 none */
    public int Value; /* stance | formation | combat behavior */ public int FireTargetId;
    public IntentPriority Priority; public IntentSource Source; public IntentFlags Flags;
    public WhiskeyRealism.Tactical.Operations.CommandTaskType Task; public string Reason; /* static literal only */
  }
  public interface IIntentSink { void Submit(in BattleOrderIntent intent); }
  public interface IBattleIntentProducer { string Name { get; } void Produce(BattleSnapshot snapshot, IIntentSink sink); }
  public enum OrderOutcomeKind { None = 0, Issued = 1, Moved = 3, Failed = 4, Retried = 5, GaveUp = 6, Skipped = 7, Deduped = 8, Superseded = 9 } /* 2 was Pending, dropped by BC4 (D6); not persisted */
  public enum SkipReason { None, ModeNotActive, PlayerOwned, Feud, NotAiControlled, Routed, OnRetreat, Garrison,
    Inactive, ChainNoTargets, OffMap, Water, FriendlyFootprint, FriendlyFireLine, PointBlank, Cooldown, Duplicate, LowerPriorityThanClaim }
  public struct OrderOutcome { public int GroupId; public OrderChannel Channel; public OrderOutcomeKind Kind; public SkipReason Reason;
    public IntentSource Source; public double AtHours; public int Retries; }
}
```
`BattleSnapshot` itself is declared by BC1. BC0 adds a stub `public sealed partial class BattleSnapshot { }` so the interfaces compile, and BC1 fills it in.

- [ ] Add `Plugin.cs` binds:
  - `EnableBattleFogHonestPerception = Config.Bind("Tactical", "Enable Battle Fog-Honest Perception", true, "Default ON. AI battle perception uses vanilla's own AI-side fog grid (FogOfWar.UnitIsHidden, the arguments vanilla uses at 127225); hidden enemies are remembered and decay. OFF = vanilla range-only perception.")`
  - `EnableInfantryChargeDoctrine = Config.Bind("Tactical", "Enable Infantry Charge Doctrine", true, "Default ON. AI infantry in assault may charge (combat behavior 3) a visible, weaker or disordered target inside vanilla's charge radius. Never on W&L player-owned units. OFF = vanilla (AI infantry reaches melee only by contact).")`
- [ ] Build plus harness. Commit `feat(battle-core): contracts and config [BC0]`.

---

## BC1 — BattleSnapshot and builder

**Owned files:** create `Tactical/Battle/BattleSnapshot.cs` (pure), `Tactical/Battle/BattleSnapshotAssembler.cs` (pure: parent map, flags, derived arrays), `Tactical/Battle/BattleSnapshotBuilder.cs` (runtime); test file `BattleSnapshotTests.cs`.

**Vanilla to read:**
- `Regiment` fields and `allattachedunits` usage: `GetAttachedUnitsReg` (~119840; note the `directonly` transform test), deployment detach 85318-85336, `IsUnitUnderCommander` 91168-91222.
- `UpdateUnitRangeFast` 122545-122937, for `enemyinrangereg` (unsorted, 122840).
- `ArmyEvidenceBuilder.NormalizeAmmo01` (mod) for the T1 ammo rule; `completeunitlist` usage.

**Interfaces — Produces:**
```csharp
public sealed partial class BattleSnapshot : WhiskeyRealism.Tactical.Orchestrator.IObservationSource {
  public int BattleKey { get; } public int Alliance { get; } public int Side { get; } public long TickId { get; } public double DoctrineHours { get; }
  public int OwnCount { get; } public UnitRecord[] Own { get; }           // pooled, valid [0, OwnCount)
  public int EnemyRecordCount { get; } public UnitRecord[] EnemyRecords { get; } // true state; read ONLY by BC2 perception
  public int ObservationCount { get; } public EnemyObservation[] Observations { get; } // filled by BC2
  public PerceptionMode Perception { get; } public PerceptionFallbackReason PerceptionFallback { get; }
  public int PlayerCommandIndex { get; }                                  // -1 when none on this side
  public int IndexOfOwn(int instanceId);                                  // O(1) via pooled Dictionary<int,int>
  public int DirectChildren(int ownIndex, int[] buffer);                  // returns count, no alloc
  public bool IsAncestorOf(int ancestorIndex, int descendantIndex);
  public WhiskeyRealism.Tactical.Orchestrator.TacticalFrontAxis FrontAxis { get; }
  public int SliceCount { get; }
  public float SlicePressure(int ownIndex, int slice);                    // BC2 fills
  public float FlankPressure(int ownIndex, int flank);                    // 0 left, 1 right, 2 rear; BC2 fills
  public int ClosestVisibleEnemy(int ownIndex, out float distance);       // observation index or -1; BC2 fills
  public bool IsVisible(int enemyInstanceId);
  public float VisibleEnemyStrength { get; } public float RememberedEnemyStrength { get; } // remembered is confidence-weighted
  public float VisibleEnemyCentroidX { get; } public float VisibleEnemyCentroidZ { get; }
  public float VisibleReinforcementStrength { get; } public float NearestVisibleReinforcementHours { get; }
  public int AncestryMismatchCount { get; }                               // D5 telemetry
}
internal static class BattleSnapshotAssembler {
  internal static void AssembleCommandTree(UnitRecord[] own, int count, int[][] attachedIds, int[] attachedCounts, BattleSnapshotScratch scratch); // sets ParentIndex
  internal static void ApplyOwnershipFlags(BattleSnapshot s, in PlayerOwnershipInput input); // delegates to BC3 PlayerOwnership.Compute
  internal static WhiskeyRealism.Tactical.Orchestrator.TacticalFrontAxis FitFrontAxis(BattleSnapshot s, WhiskeyRealism.Tactical.Orchestrator.TacticalFrontAxis previous, int commandHierarchyShift); // shift: group fallback sample starts at the shifted command level
}
internal static class BattleSnapshotBuilder { // runtime
  internal static BattleSnapshot Build(AIBattle battle, int alliance, BattleSnapshot reuse, long tickId, double doctrineHours);
}
```
**Consumes:** BC0 contracts; BC3 `PlayerOwnership.Compute` (signature below; until BC3 merges, BC1's tests call the assembler with flags precomputed in fixtures); `TacticalFrontAxis.FromCentroids/Stabilize` (existing).

**Constants (pinned by tests):** `BattleSnapshot.InitialCapacity = 512`. `FrontAxisMinHeadingChangeDegrees = 10f` (existing value from `TacticalFrontAxis.Stabilize` callers; verify and reuse).

**Steps:**
- [ ] Tests first (`BattleSnapshotTests`):
  - `CommandTreeParentIsSmallestContainingGroup`
  - `CommandTreeRootHasNoParent`
  - `DirectChildrenMatchesParentMap`
  - `IsAncestorOfWalksParentChain`
  - `IndexOfOwnReturnsMinusOneForUnknown`
  - `AssemblerRebuildsTreeOnlyOnRosterChange`, using the roster signature: own unit count, sum of instance ids, identity of the vanilla `completeunitlist`, and the sum of `allattachedunits.Length` over own groups. All are field reads with no allocation; `gameObject.name` is loaded only for ids missing from the name cache
  - `RoutDoesNotBumpTreeGeneration`: rout never re-parents (`Rout` returns for groups at 123547 and writes no transform for a leaf), so a leaf rout keeps the parent map and `TreeGeneration`, and `CommandTreeRuntime` serves it from its cache or the `RefreshLiveFlags` path, never a rebuild
  - `SnapshotAssemblyAllocatesNothingAfterWarmup`: a 300-unit fixture, assemble twice, the second call allocates 0 bytes
  - `FrontAxisStabilizedAcrossTicks`
  - `MoveUnitStaleSourceListYieldsToDestination`: after `MoveUnit` a deep source group keeps a stale list that still holds the moved unit; the claimant that was the unit's previous parent yields to the other one, and the move changes the attached-length sum
  - `BeginBuildResetsPerceptionSummary`: `NearestVisibleReinforcementHours` resets to `float.MaxValue` ("none seen", BC2a's sentinel), never 0
  - `FrontAxisGroupFallbackWeightsByAiGroupStrength`: with no combat leaf, groups weigh `max(1, max(0, groupstrengthaigroup))`, as the shipped `ArmyEvidenceBuilder` group samples and `Centroid` did
  - `FrontAxisGroupFallbackFilterMatchesShippedSample`: the group fallback samples units at or above the shifted command level, not routed and not `PlayerOwned` (shipped `ArmyEvidenceBuilder` filter); a player-owned division is excluded
  - `FrontAxisLeafSampleExcludesMarkedForRout`: a leaf with vanilla `markedforrout` (cavalry mounting to rout, `Rout` 123555-123559) is left out of the front-axis sample, as the shipped `isrouted || markedforrout` test did
  - `StrengthBucketReadsAiGroupAndInRangeStrength`: the extended probe keeps the shipped bucket source `max(groupowninrange, groupstrengthaigroup)` (vanilla 120297, 120559-120564), so `ChooseEnvelopmentAnchorIndices` sees unchanged strengths; a non-AI-group division out of contact stays bucket 0
- [ ] Implement `BattleSnapshot` with grow-only pooled arrays. Growth allocates only when the roster exceeds capacity, which the allocation test accepts as warm-up.
- [ ] Implement the runtime builder. It does one pass over `BattleUnits.completeunitlist` (as `IList`, no LINQ). Own units of `alliance` go into `Own` and enemies into `EnemyRecords`. It caches `allattachedunits` id lists only when the roster signature changes. It sets `FirstEnemyInRangeId` from `unitrange.enemyinrangereg[0]`. On ancestry, it counts disagreements between the D5 parent map and `transform.IsChildOf` for the player chain only (cheap), and emits `OnceLog.Info("tactical-battle-core:ancestry-mismatch", …)` once per battle when the count is > 0. The key must start with `tactical` (`OnceLog.TacticalKeyPrefix`): `OnceLog.ResetTactical` releases only those keys at battle end, so a key without the prefix would fire once per process.
- [ ] Runtime fallbacks: a null `bunits` gives an empty snapshot plus `OnceLog.Warning("tactical-battle-core:snapshot-degraded", …)`. Never throw. Every battle-core OnceLog key starts with `tactical-battle-core:` so `OnceLog.ResetTactical` (prefix `tactical`) releases it at battle end.
- [ ] **One parent map.** There are three independent `GetAttachedUnitsReg` walks today, with different tie-breaks:
  - `DirectChildDiscoveryRuntime` at :45 and :147 keeps the first parent found and passes `includenonactiveunits:false`.
  - `CommandTreeRuntime.BuildAttachedParentMap` (:331) keeps the last parent found, and falls back to `parentregiment`, then `transform.parent` (:394).
  - `CommandTreeRuntime.ReadRosterSignature` (:224) and `ArmyPostureInputsRuntime.RosterSignature` (:114) each do a full pass every tick.

  BC1 therefore also owns `Tactical/Orchestrator/DirectChildDiscoveryRuntime.cs` and `Tactical/Orchestrator/CommandTreeRuntime.cs`. Both take the `BattleSnapshot` and read `ParentIndex`/`DirectChildren` (D5 rule). Delete their own walks, roster signatures and `transform.parent` fallbacks. Test: `DirectChildAndCommandTreeAgreeOnParents`.
- [ ] Rewrite the "urgent recovery boundary" header in `TacticalBattleRuntimeSnapshot.cs` (:32-79). The live movement fields it lists (pathinterrupted, regimentpaths, lastsetwaypointposition, formation, rotation, position) are now captured fresh into `UnitRecord` every tick, in the same frame (built at 5866). The header should point at `BattleSnapshot` instead of live reads.
- [ ] Scope: `tactical.snapshot.build` (5.0 ms) around `Build`.
- [ ] **Acceptance:** tests pass; `BattleSnapshotBuilder` is not in the test csproj; the alloc test is green.

PROVE-MODE: harness for this slice; in-game at BC7. Per-slice proof is the console harness: the pure assembler (parent map, stale-source rule, roster signature, probe converters, strength bucket, front axis) including zero allocation over 300 units after warm-up. Runtime proof of `BattleSnapshotBuilder.Build` is the BC7 in-game smoke, because nothing builds the snapshot before BC4 wires `BattleCoreTick.EnsureSnapshot`; until then `CommandTreeRuntime` / `DirectChildDiscoveryRuntime` read no snapshot and warn once per battle (`tactical-battle-core:snapshot-missing`).

**Merge items (BC1):**
- **At the BC3 merge:** `BattleSnapshotBuilder.OwnershipFlags` sets `PlayerOwned` from `dlcw_isundercommander` even outside W&L (verify finding 5). Replace it with BC3's `PlayerOwnership.Compute`, as the seam comment in `OwnershipFlags` says, and add `ApplyChainFlags` after `AssembleCommandTree`. Two consumers read `PlayerOwned` and follow the change without edits: `FitFrontAxis`'s group fallback, which replaces the shipped `dlcw_isundercommander` test, and `ToRegimentProbes(excludePlayerOwned)`.
- **At the BC4 merge:** `ArmyPostureInputsRuntime.ExtendedProbes` (`ArmyPostureInputsRuntime.cs:103-111`) caches the probe list against its own roster signature. Before the first snapshot build, `DirectChildDiscovery.BuildExtendedProbesForAlliance` returns an empty list, which would then be cached until the roster changes. BC4 must build the snapshot (`EnsureSnapshot`) before any reader runs, or the cache must not store an empty probe list taken before the first build. **Done at the BC4 merge:** both — the coordinator builds each side's snapshot before `RefreshPostureInputs`, and `ExtendedProbes` no longer caches an empty list.

**Known limitations (BC1):**
- **Stale `allattachedunits` after `MoveUnit` (93091-93150).** `MoveUnit` refreshes the destination (`UpdateAllAttachedUnits`, 93136) and the new parent chain (`UpdateAllParentUnits` 93141 → 90524-90546). On the source side it refreshes only the highest parent up to unittyp 16 (`GetHighestParentUnit(unittomove, 16)`, read before the move at 93103) and, through `includesubordinategroups`, that parent's direct subordinate groups (93137-93140 → 129619-129627). A source group two or more levels below keeps a stale list that still holds the moved unit.
  - Parent choice: handled. Lists of a consistent tree are nested or disjoint, so two non-nested claimants mean one is stale; the claimant that was the unit's previous parent yields (`ResolveStaleSourceClaims`, test `MoveUnitStaleSourceListYieldsToDestination`). With no previous map (first build of the battle) the smallest group stays.
  - Detection: the destination list always grows, so the attached-length sum changes in the common case (+1 per moved unit and descendant when the stale source and destination sit at the same depth). It nets to zero when the refreshed source groups shrink by as much as the destination chain grows: for example, a regiment moved from a deep brigade to directly under a sibling division of the same top group, where the destination division gains one and the refreshed source division loses one. That move keeps the cached map until a later list refresh changes the sum. The side-stat cycle re-runs `UpdateAllAttachedUnits` on every battle-data group (84647, gated by `sidestatupdatecycle` at 84572; battle-data units are those set at 88391), so a stale battle-data source group self-corrects within one cycle.
  - In-battle reachability: the OOB drag (`PanelHandler.UpdateDragAndDrop` 208051) needs `campaignmainpanel`, which is bound only when `IsCampaign` (201477). `AICampaign.CheckCombinationOfUnits` / `CheckTransferOfUnits` (17158, 17328) and `CampaignMainPanel.DetachUnitToIndependentCorps` (178375) are campaign-only. The one path the battle OOB window can reach (`oob` bound outside the campaign, 201495) is the W&L `REQUESTTransfer` prompt (`OOB.CheckCommandPrompt` 198841, `CanRequestTransfer` 43911), which moves the player's own command. `AncestryMismatchCount` compares the player chain with `Transform.IsChildOf` and logs the `tactical-battle-core:ancestry-mismatch` OnceLog when they disagree.
  - Two moves of the same unit inside one side-stat cycle leave two stale lists. `ResolveStaleSourceClaims` yields only the previous-map parent, so the smallest-first winner can still be the first stale source. Same single in-battle trigger (W&L `REQUESTTransfer`); self-corrects at the next side-stat refresh for battle-data groups (BC1 verify r2 landing note).

---

## BC2 — Perception (BC2a pure + probe; BC2b T1 consumers)

**Owned files:**
- BC2a: create `Tactical/Battle/BattlePerception.cs` (pure memory/decay/fallback decision), `Tactical/Battle/SlicePressureBuilder.cs` (pure), `Tactical/Battle/BattlePerceptionProbe.cs` (runtime).
- BC2b: modify `Tactical/Orchestrator/ArmyEvidenceBuilder.cs`, `ArmyEvidenceBuilder.Pure.cs`, `EnemyVisibleState.cs`, `TacticalEnemyContactLedger.cs`, `TacticalFrontAxis.cs`, `TacticalFogOfWarContact.cs`, `TacticalVisionRuntimeAdapter.cs`, `TacticalBattleSnapshotBuilder.cs`, `TacticalBattleRuntimeSnapshot.cs`, `IObservationSource.cs`; delete `TacticalUnitObservationAggregate.cs`, `TacticalUnitObservationAggregate.Runtime.cs`, `tests/…/TacticalUnitObservationAggregateTests.cs` (port valid assertions to `BattlePerceptionTests`).
- Test file: `BattlePerceptionTests.cs`.

**Vanilla to read:**
- `FogOfWar` 100570-100800: `firstrunfinished` 100648/100690-100694, `InitFOW` 100782-100795.
- `UpdateViewRange` 101274-101365, including the `hqinthesaddle` blank at 101312.
- `UnitIsHidden` 101179-101229.
- `Regiment` visibility tick 127205-127239 (argument order at 127225; melee at 127216/127231).
- `CreateFOWObjects` 116505-116518.
- Slice computation `enemystrengthwithinangle` 122719-122733 and `flankstrength` 122799-122820.
- Hotspots 7975-8004; reinforcement fields.

**Interfaces — Produces:**
```csharp
public static class BattlePerception {
  public const float FogStartupSeconds = 5f;
  public const float RememberedHalfLifeMinutes = 20f;
  public const float RememberedMaxMinutes = 90f;
  public const int HideConfirmTicks = 2;
  public const float RememberedWeightFloor = 0.05f; // confidence floor until the 90-minute cap
  public static PerceptionMode DecideMode(bool configOn, bool useFow, bool hqInTheSaddleBattle, bool gridFirstRunFinished,
      double secondsSinceBattleStart, bool sideIsPlayerAlliance, out PerceptionFallbackReason reason);
  public static void Update(EnemyObservation[] memory, ref int memoryCount, in PerceptionProbeResult probe, double nowHours); // visible→remembered after HideConfirmTicks, decay, drop at 90 min
  public static float Confidence(double sinceSeenHours); // max(0.05, 0.5^(minutes/20)), 0 at ≥ 90
  public static void Publish(BattleSnapshot s, PerceptionMode mode, PerceptionFallbackReason reason,
      EnemyObservation[] memory, int memoryCount, in PerceptionProbeResult probe); // BC1 setters: SetPerception, AddObservation, Set*Summary
  public static string ModeLogKey(PerceptionMode mode);                       // "tactical.battle-perception:mode:<mode>"
  public static float RangeOnlyReach(float lineOfSight, float cancelInterruptionDistance);
  public static bool WithinAnyReach(float[] ownX, float[] ownZ, float[] reach, int count, float x, float z);
}
public struct PerceptionProbeResult { public int Count; public int[] EnemyIds; public bool[] Visible; public float[] X, Z, Strength; public int[] UnitTyp;
  public bool[] IsReinforcement; public bool[] Routed; public float[] ArrivalHours; }
public struct SliceGeometry { public float MaxRangeMeters; public float FlankShiftMeters; } // aidefensivemaxrange, flankpositionshift; slices = s.SliceCount
public static class SlicePressureBuilder {
  public static void Build(BattleSnapshot s, in SliceGeometry g);  // overwrites slice/flank/closest-visible rows from Visible + confidence-weighted Remembered
  public const float RememberedPressureWeight = 0.5f;
}
internal static class BattlePerceptionProbe { // runtime
  internal static void Probe(BattleSnapshot s, AIBattle battle); // once per build, after BattleSnapshotBuilder.Build; ends with RefitFrontAxis
}
```
**As built (BC2a, recorded):**
- Enemy memory lives in a per-side `BattleScope` store in the probe, not in the snapshot: D1 alternates two pooled snapshots, so each tick receives a copy through `Publish`.
- The probe walks `s.EnemyRecords` and takes the live `Regiment` from `BattleSnapshotBuilder.UnitAt` only for `UnitIsHidden`'s cover/regref arguments and `iswithinfog`.
- `FlankPressure` slot 2 is the rear point. Vanilla's slot 2 is the line centre (122907), counts enemies as negative strength × morale and adds 9999999 per friendly (122812, 122816), so vanilla `flankstrength` thresholds do not port unchanged.
- Slice bins follow 122732 literally: width and slot count are both `aidefensiveslices`, so with the shipped 20 only bins 0-17 fill.
- A remembered enemy is dropped at exactly `RememberedMaxMinutes`; `RememberedWeightFloor` is the confidence floor until then.
- Routed enemies: dropped when seen routing. In RangeOnly, where vanilla's lists skip routed units (122644), a routed enemy counts as seen when an own unit's list reach `max(maximumlineofsight[unittyp], distancetoenemytocancelinterruption)` (122508-122513, 122608) covers it.
**Probe rule (R1), exact:**
- AI side not the player alliance, mode FogGrid: an enemy is visible when `!own.fow.UnitIsHidden(enemy.GetPosition(), GameVars.commander[enemy.commander].GetCommanderCunning(), (float)enemy.strength / GamePrefs.unittypesize[enemy.unittyp], enemy.liedown, enemy.coverobject, enemy.covervalue, enemy.unittyp, enemy)`, OR `enemy.inmelee > 0`.
  - `own.fow` is taken from any live own unit (it is `fows[1]` per 116505).
  - Bounds-check `enemy.commander` against `GameVars.commander.Count` and `enemy.unittyp` against `unittypesize.Length`. When out of range, pass `-1` for that argument (vanilla's "unused" sentinel).
- Player-alliance AI side (W&L / `ai_vs_ai`), mode `OwnSideWithinFog`: visible = `!enemy.iswithinfog`, which follows vanilla 127225 semantics.
- Mode RangeOnly: visible = the enemy is in any own unit's `enemyinrangereg`/`enemyinfirerangereg`. That is today's behavior, through one pass that unions the per-unit lists into a pooled `HashSet<int>`.
- `gridFirstRunFinished` is read by cached reflection on the private `FogOfWar.firstrunfinished` (100648). If that read fails, the mode is RangeOnly with reason `GridNotPainted`, plus one OnceLog warning.
- `UseIntelligenceMap` is logged once per battle (open question in the vision doc).
- Reinforcements count only when their entry point is visible, using the same probe at the entry position. This replaces the reads of `sideinformation.reinforcementarrivalswithin24hrs`.

**Steps BC2a:**
- [ ] Tests first:
  - `ModeFallsBackWhenFowOff`
  - `ModeFallsBackWithHqInTheSaddle`
  - `ModeFallsBackBeforeGridPainted`, both first-run-false and t < 5 s
  - `ModeOwnSideUsesWithinFog`
  - `ConfigOffForcesRangeOnly`
  - `VisibleBecomesRememberedAfterTwoHiddenTicks`
  - `HiddenForOneTickStaysVisible`
  - `RememberedConfidenceHalvesAt20Minutes`
  - `RememberedDroppedAt90Minutes`
  - `ReappearingEnemyResetsToVisible`
  - `SlicePressureCountsOnlyVisiblePlusDiscountedRemembered`
  - `SliceBinningMatchesVanillaAngles`: a fixture reproduces 122719-122733
  - `FlankPressureLeftRightRear`
  - `ClosestVisibleEnemyIgnoresHidden`
  - `ReinforcementCountsOnlyWithVisibleEntry`
  - `PerceptionUpdateAllocatesNothingAfterWarmup`
- [ ] Implement. Scope `tactical.perception` (2.0 ms) around probe + update + slice build. Emit telemetry row `[BattlePerception] alliance=<a> mode=<FogGrid|RangeOnly|OwnSideWithinFog> reason=<…> visible=<n> remembered=<n>` once per mode change per side. Use OnceLog key `tactical.battle-perception:mode:<mode>` (static per mode; the `tactical` prefix lets `OnceLog.ResetTactical` re-arm it each battle); the per-side counts go through the `BattlePerception` Decision telemetry row.

PROVE-MODE: harness for this slice; in-game at BC7. Per-slice proof is the console harness (`BattlePerceptionTests`, exit code 0; a failing assertion throws, exit 134): mode decision order, memory hide/remember/decay/drop at 90 min, routed and reinforcement handling, publish summaries, vanilla slice binning, flank and closest-visible pressure, the per-side painted-grid latch, and zero allocation over 300 own × 300 enemy after warm-up. Mutation evidence (verify r2): five plants each turned a named test red with exit 134 — the round-1 0.05-floor drop rule, `HideConfirmTicks = 1`, reinforcements counted in the enemy summary, full remembered pressure weight, and a closest-enemy search ignoring visibility. Runtime proof of `BattlePerceptionProbe.Probe` is the BC7 in-game smoke (the `[once:tactical.battle-perception:mode:…]` lines), because nothing calls the probe before BC4 wires it after `BattleSnapshotBuilder.Build` in `BattleCoreTick.EnsureSnapshot`.

**Steps BC2b (after BC1 merges):**
- [ ] Tests first:
  - `EvidenceSectorStrengthFromObservationsNotVanillaSlices`
  - `EnemyVisibleStateExcludesHiddenEnemies`
  - `ContactLedgerFedFromSnapshotObservations`
  - `ObjectiveRecordsFromSnapshotMatchAggregateFixture`, porting the `ObjectiveRecordsFromAggregateTests` fixtures to a `BattleSnapshot` source
- [ ] `ArmyEvidenceBuilder.Build(AIBattle, int)` becomes `Build(BattleSnapshot snapshot, AIBattle battle)`. All `completeunitlist`, `enemystrengthwithinangle` (L424-429) and `reinforcementarrivalswithin24hrs` (L295) reads are deleted. Sector enemy strength comes from `snapshot.Observations`, and averages from `snapshot.Own`. `PerSideFrameMemo` is deleted (the snapshot is the memo).
- [ ] `TacticalBattleSnapshotBuilder.Build` and `.ExtractCurrentSignature` take the `BattleSnapshot`. The legacy objective-record branch and the `EnableSinglePassObjectiveRecords` read are deleted; the flag is removed in BC7. `TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromAggregate(IObservationSource, …)` receives the snapshot. `BuildObjectiveRecordsFromBattle` is deleted.
- [ ] `TacticalFogOfWarContact` keeps its public method names (`ClosestVisibleEnemy`, `TryClosestVisibleEnemy`, `HasVisibleEnemy`, `VisibleEnemyStrength`). They now filter vanilla per-unit candidates by `BattleCore.TryGetSnapshot(alliance, out s) && s.IsVisible(id)`. Sorting is by true distance, never by `enemyinrangereg[0]` order. `TacticalFrameCache` is deleted.
- [ ] `TacticalEnemyContactLedger` ingests `snapshot.Observations` (visible plus remembered) on the doctrine clock.
- [ ] Switch these to the snapshot as well (BC2b owns them):
  - `TacticalBattleCoordinatorRuntime.cs`: `SumEnemyStrengthWithinAngleSafe` (:2004-2023) and `BuildSectorForGroup` (:1984-1996) → `SlicePressure` / `ClosestVisibleEnemy`.
  - `TacticalBattleCoordinatorRuntime.cs`: per-group reads in `PopulateResolvedDecisionsForSide` → `snapshot.Own`. BC2b lands before BC5b, which only changes the stance mapping there.
  - `Tactical/TacticalTerrainProbe.cs`: enemy bearing (:99-111) → `ClosestVisibleEnemy`.
  - `Tactical/PlayerOrders/PlayerOrderRuntimeAdapter.cs:529`: unfiltered `closestenemyunitfarreg` → `ClosestVisibleEnemy`.
  - `Patches/TacticalObserverPatch.cs`: `enemystrengthwithinangle` (:2021-2026, :2173-2177), `reinforcementarrivalswithin24hrs` (:2529) and `sideinformation.forcebalance` (:2072/:2105, which embeds the enemy's true weighted force) → snapshot visible/remembered strength.
  - `TacticalBattleSnapshotBuilder.ExtractCurrentSignature`: keep only own-side `totalactiveforce`; the enemy term becomes bucketed visible strength.
  - `TacticalVisionRuntimeAdapter`: filter the enemy `scheduledarrival`/`grp` reads (:726-798) to entry points the probe reports visible. Delete the uncached reflection in `SafeStrength` (:1359); strength comes from `UnitRecord`.
- [ ] **Acceptance:** `rg -n "enemystrengthwithinangle|reinforcementarrivalswithin24hrs|flankstrength|closestenemyunitfar|forcebalance" src/WhiskeyRealism/Tactical src/WhiskeyRealism/Patches/TacticalObserverPatch.cs` returns only the perception files (`BattlePerceptionProbe.cs`, and comments in `SlicePressureBuilder.cs`). BC5 removes the remaining patch-side readers (#44, #45, #56, #59, B8).

**As built (BC2b, recorded):**
- `IObservationSource` (Orchestrator) is snapshot-shaped: `OwnCount`, `Own`, `ObservationCount`, `Observations`, `IndexOfOwn`, `ClosestVisibleEnemy`. `BattleSnapshot : IBattleUnitView, IObservationSource` (one-line change in BC1's `BattleSnapshot.cs`; the members already existed). `TacticalUnitObservation(.cs)`, the aggregate (+ `.Runtime`) and both aggregate test files are deleted; their assertions are ported to `BattlePerceptionTests`.
- **D1 amendment (coordinator decision, BC2b):** the snapshot is infrastructure, not doctrine. `TacticalBattleCoordinator.EnsureBattleCoreSnapshots(AIBattle)` builds every AI-run side (`ShouldRunTacticalCommanderForSide`: both under `ai_vs_ai`, which W&L forces, else the non-player alliance), each from its own AIBattle. It is called first in the `CheckGlobalAIStrategy` postfix (vanilla 5866) on every call, whatever `EnableTacticalBattleOrchestrator` (only the plugin's master `Enabled` switch stops it, as it stops `Plugin.Update`), and again first in `Tick` (a no-op the same frame). It also runs in every Commander Mode, Off included: Off only removes writes, while the coordinator Tick (DriveTickCycle, DriveDirectChildCycle, the ledger's Off branch), the observer's decision-matrix telemetry and the `TacticalFogOfWarContact` readers still read, and the build is a write-free per-frame cache. A pre-doctrine deployment hook may build too: #60 `TacticalDeploymentTerrainDisciplinePatch` (Postfix on `DoPlacementAIUnitsWithinDeploymentzoneNew`, 85524) builds its side's snapshot before its visible-enemy bearing read; its own logic is unchanged.
- **#60 on day 1:** vanilla places the AI at battle-init frame 30 (23993-23994), 25 frames after `InitFOW` (frame 5, 23886-23890). The probe's first call is this deployment build, so `FogStartupSeconds` (and usually `firstrunfinished`) put the side in RangeOnly (`GridNotPainted`): visible = vanilla's own range lists (`enemyinrangereg` / `enemyinfirerangereg`), the very candidates #60 already filtered, so day-1 facing input equals the shipped one. Vanilla's own placement (85524-85862) reads no enemy perception at all (grp start/objective positions and the deployment zone). On later days (164879-164880, after the night) the grid is painted and FogGrid applies.
- `ArmyEvidenceBuilder.Build(BattleSnapshot snapshot, AIBattle battle)`; no memo (the snapshot holds the per-tick derived state). With heavy throttling off (non-default) evidence may build up to 3x per side per tick (replan, direct-child, ledger); accepted, no memo.
- **One front axis:** evidence bands on `snapshot.FrontAxis` (BC1's fit, refit on what is seen by BC2a) via `ArmyEvidenceBuilder.EvidenceFrontAxis`. Only while the snapshot axis has no target (nothing seen yet: forward +z, `TargetDistance` 0) does it face the nearest objective the side does not hold, from the snapshot's own centroid, stabilized against the last evidence axis. Evidence's own centroid/bare-axis fit (`BareAxis`, `Centroid`, `_lastBareAxis`) is deleted; the fitted axis is also the coverage frame. After contact is lost the evidence axis keeps the last-seen enemy heading (the snapshot axis keeps its previous target) instead of turning back to the nearest open objective as the shipped axis did: deliberate, fog-honest memory. Note for BC1/BC2a: `FitFrontAxis` takes the enemy extent from every visible observation, reinforcement rows at entry points included. Own averages from `snapshot.Own` (`OwnAverages`), sector enemy strength = visible + confidence-weighted remembered observations (`CollectEnemySamples`), axis target and contact picture = visible observations, engaged = visible and in an own regiment's `enemyinfirerangereg`. The private remembered-position dictionary is gone (the snapshot memory replaces it). Group flank threat = vanilla outflanked weight over perceived flank pressure (`MeanFlankThreatByGroup`, `OutflankedState`), replacing live `outflanked` reads. Own reinforcements = own `scheduledarrival` columns due in (0, `reinforcementmaxtimeforairetreatdecisions`) (vanilla 84764-84767; `ReinforcementsArrivingWithin`). `MapOwnGroupsToSectors` reads the snapshot.
- `TacticalEnemyContactLedger.Observe(BattleSnapshot, engaged)`: largest-seen tracks visible + confidence-weighted remembered (no reinforcement columns) at `snapshot.DoctrineHours`; only a visible enemy (or fire) stamps contact.
- `TacticalBattleSnapshotBuilder.ExtractCurrentSignature(BattleSnapshot, AIBattle, hours)`: own slot = own `totalactiveforce` / own scripted macroai; enemy slot = `KnownEnemyStrengthBucket(VisibleEnemyStrength)` (500-men buckets) and macro -1; contact bucket = own units in melee or whose `FirstEnemyInRangeId` is visible. It no longer counts own `pathinterrupted` units (UnitRecord carries no such flag); that trigger returns once BC5c's `UnitFlags.PathInterrupted` merges (integrator item). `Build(BattleSnapshot, AIBattle, sig, hours)`; `BuildObjectiveRecords(BattleSnapshot, AIBattle)` reads only `currentsetobjective` live through `BattleSnapshotBuilder.UnitAt`.
- `TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromAggregate(IObservationSource, IReadOnlyList<OwnSetObjective>, AIBattle, int)`. Own strength = `max(0, GroupStrengthAiGroup)` (the field the deleted reflected `SafeStrength` resolved, 120176/120297); enemy strength = the closest visible observation's strength (the shipped path read the enemy leaf's `groupstrengthaigroup`, which vanilla leaves 0). Enemy approach avenues: scheduled arrivals only at a visible reinforcement observation (entry point within 1 m), deployment groups (`BattleUnits.grp`) never (`IsAvenueKnown`).
- `TacticalFogOfWarContact` drops out of the test csproj (it now reads `BattleCore`). With no snapshot for a side (the player side outside W&L/`ai_vs_ai`, which no Whiskey writer acts on) its readers and `TacticalTerrainProbe.GetVisibleEnemyBearing` see no enemy.
- Coordinator: `AttachArmyIfActive` calls `BattleCoreTick.EnsureSnapshot` itself (a restart's `OnBattleEnd` resets the builder inside the same postfix, so OnBattleStart cannot rely on the postfix's earlier build). `PopulateResolvedDecisionsForSide` walks `snapshot.Own` (`PlayerOwned` replaces `dlcw_isundercommander`, `AiStanceOrdered`/`InstanceId` from the record; cache key stays the Regiment component id). `BuildSectorForGroup` = slice pressure total, perceived flank pressure, nearest visible enemy.
- Observer (#observer, telemetry/decision matrix): slice pressure replaces the angle-slice reads, a known force balance (own active / (visible + remembered)) replaces `sideinformation.forcebalance` and its enemy-inference fallback, own arrivals replace the 24 h figure. Its context is built right after the postfix's snapshot build, so it reads this frame's perception.
- `EnableSinglePassObjectiveRecords` has no reader now (config-audit UNREAD); BC7 removes it.

PROVE-MODE: harness for this slice; in-game at BC7. Per-slice proof is the console harness (`BattlePerceptionTests` BC2b block, exit 0): snapshot as `IObservationSource` (ported aggregate assertions), objective-record fixtures ported onto a snapshot, sector/visible/known enemy samples from observations, contact ledger fed from observations on the snapshot's doctrine hours, own averages, own reinforcement window, group flank threat from perceived pressure, enemy avenue filter, signature buckets, slice/flank helpers, evidence front axis = snapshot axis with the objective fallback only before any sighting. Mutation evidence: an objective fallback that overrides a seen target turned the front-axis test red; a contact bucket that counts unseen range-list enemies turned `SignatureInputsFromSnapshot` red; largest-seen from visible only, and an avenue filter that admits everything, each turned a named test red (exit 134). Runtime proof (evidence, heavy gate, objective records, `TacticalFogOfWarContact` readers on live battles) is the BC7 in-game smoke.

---

## BC3 — PlayerOwnership, TacticalWriteGuard, chain resolution, dispatch

**Owned files:** create `Tactical/Battle/PlayerOwnership.cs` (pure), `Tactical/Battle/TacticalChainOrderPlanner.cs` (pure), `Tactical/Battle/TacticalDispatchTypeMapper.cs` (pure), `Tactical/Battle/TacticalPlayerDispatchRuntime.cs` (runtime); rewrite `Tactical/TacticalWriteGuard.cs`; modify `Patches/PlayerSubordinateOrderPatch.cs` (dedupe moves to the emitter), `Patches/BattleCommandPostureExecutorPatch.cs` (per-unit `CanWrite` in `RotateRegimentsInPlace` only; assigned by the integrator for the round-2 fix), `Tactical/PlayerOrders/PlayerOrderDedupe.cs` (state type stays, owner changes); create `Tactical/Battle/BattleSnapshot.Ownership.cs` (declares BC1's `Own`/`OwnCount` so BC3 compiles first; deleted at the BC1 merge). `PlayerCommandProbeFilter` (`CommandPostureExecutor.cs:439-453`) is **not** BC3's: its caller `ArmyPostureInputsRuntime.cs:105` and `PostureStanceFixTests.cs:679-686` are BC5a's, so BC5a deletes the class with its call. `Tactical/TacticalWlActionGuard.cs` is BC5b's: it deletes the file with its callers (`BattleFeudActionGatePatch.cs:88`, `BattleGroupStancePatch.cs:814`, `TacticalObserverPatch.cs:1832`). Test file `PlayerOwnershipTests.cs`.

**Vanilla to read:**
- `IsUnitUnderCommander` 91168-91222.
- `IsUnitTakenOver` 45840; `TakeOverUnitBattle` 164444.
- `CheckCurrentOrderUpdate` 8233-8667: flags 8252-8275, battle path 8327-8364, dedupe 8643, pop-up 8648-8664.
- `ChangeStance` 90772-90793; `GetAttachedUnitsReg` exclusion of detached 119857; `PerformAIActionDLCWL` 5101.
- `SetSide` 3408; deployment detach 85318-85336.

**Interfaces — Produces:**
```csharp
public struct PlayerOwnershipInput { public bool WlActive; public bool AiVsAi; public bool DebugAllArmiesAi; public int PlayerAlliance;
  public int PlayerCommandInstanceId; /* 0 none */ public bool PlayerIsCic; }
public static class PlayerOwnership {
  // Per unit: needs dlcw_isundercommander, takenOver, ai_feudstance, permanentlydetached captured by the builder into raw bits.
  public static UnitFlags Compute(in PlayerOwnershipInput input, int alliance, bool dlcwUnderCommander, bool takenOver, int aiFeudStance);
  public static void ApplyChainFlags(BattleSnapshot s, in PlayerOwnershipInput input); // AncestorOfPlayerCommand, PlayerCommandDetached, PlayerCommand
  public static bool NeedsChainFanOut(UnitRecord[] own, int count, int groupIndex);  // a group order would reach an exposed PlayerOwned descendant
  public static bool IsExposedOwned(UnitFlags descendant, bool detachedNodeBetween);
  public static bool DirectWriteProtected(UnitFlags flags, bool exposedOwnedDescendant, out SkipReason reason); // Regiment-overload decision
}
public static class TacticalWriteGuard { // thin reader; same file path
  public static bool ModeAllowsWrites();
  public static bool IsFeudInstance(AIBattle battle);
  public static bool CanWrite(in UnitRecord unit, out SkipReason reason);   // mode, PlayerOwned, Feud, NotAiControlled
  public static bool CanWrite(Regiment unit);                               // writers that cannot fan out (#61 until BC5a, deployment #60, inline paths): mode, PlayerOwned, Feud, exposed owned descendant
  public static bool CanWrite(Regiment unit, bool feudReplay);              // the executor's live per-target re-check; feudReplay (IntentSource.FeudReplay) exempts the Feud refusal, never PlayerOwned
  public static bool IsPlayerProtected(Regiment unit);                      // PlayerOwned, Feud, or a group with an exposed owned descendant; an ancestor of a DETACHED player command is not protected as a group, so any loop writing its descendants must call CanWrite per unit
}
public struct ChainResolution { public int Count; public int[] TargetIndices; public bool FannedOut; public bool PlayerShareNeedsDispatch; }
public static class TacticalChainOrderPlanner {
  public static void ResolveTargets(UnitRecord[] own, int count, int groupIndex, int[] childBuffer, ref ChainResolution result); // the executor passes the snapshot's Own/OwnCount
}
public static class TacticalDispatchTypeMapper {
  public const int None = -1;
  public static int Map(WhiskeyRealism.Tactical.Operations.CommandTaskType task, bool targetEnemyHeld, bool chargeSupported);
}
internal static class TacticalPlayerDispatchRuntime { // runtime, BattleScope-registered
  internal static readonly WhiskeyRealism.Tactical.PlayerOrders.PlayerOrderDedupeState Dedupe;
  internal const float DispatchDedupeMeters = 110f;
  internal static bool Emit(Regiment ancestorOrPlayerCommand, int vanillaType, float x, float z, float rotation, string destinationName);
}
```
**Rules:**
- `PlayerOwned` = (`WlActive` && (`dlcw_isundercommander` OR `takenOver` OR (`PlayerIsCic` && own alliance))) OR (!`WlActive` && `alliance == PlayerAlliance && !AiVsAi && !DebugAllArmiesAi`). The cached dlcw flag is gated on the career because vanilla also sets it outside one, to "alliance == player alliance" (Regiment initialize 113065 → 91170-91176).
- `Feud` = `aiFeudStance >= 0`.
- Ancestors are never `PlayerOwned`.
- When the W&L player is CIC, every own-side unit is owned (91187); `PlayerIsCic` covers the lag before the cached flag refreshes.
- **Exposure:** a `PlayerOwned` descendant is *exposed* to a group order when neither it nor any node between it and the group is `permanentlydetached`. Vanilla `SetGroupFormation` builds every level with `GetAttachedUnitsReg(excludedechainedunits)` (91988-92030, 119865), so a detached node and its whole subtree are skipped, and there is no dlcw filter. This covers the player's attached command and attached take-overs (`TakeOverUnitBattle` detaches, 164455; the player can re-attach).
- **Chain rule:** a group with no exposed owned descendant is its own target (for the player's ancestors this is the detached case). Otherwise it is not ordered as a group: its direct children are ordered, skipping `PlayerOwned` children, and every child that itself has an exposed owned descendant fans out again. `PlayerShareNeedsDispatch` is set only on ancestors of the player's command.
- **Regiment overloads:** until BC5a routes #61 through the executor, both `IsPlayerProtected(Regiment)` and `CanWrite(Regiment)` refuse a group with an exposed owned descendant, because those writers cannot fan out, and both refuse feuding units (#61 gates on `IsPlayerProtected` only).
- **Per-unit guard:** group-level shielding follows vanilla's detached-subtree exclusion; any Whiskey loop that writes per descendant must call `CanWrite` per unit. `allattachedunits` is every transform descendant including the group itself (`UpdateAllAttachedUnits` 129574), so a writable group can hold player-owned regiments, which are never detached themselves in W&L (85316 needs `!ai_vs_ai`). Audited 2026-10-15: #61 `RotateRegimentsInPlace` (fixed), facing pulse (`CanWrite`), fire control (`IsPlayerProtected`), B7/B8 and the two CheckUseOfReserves patches (vanilla-selected units, `PerformAIActionDLCWL` 6129) guard every unit; the other `allattachedunits` walks only read.
- **Dispatch dedupe cost (note for BC4):** vanilla 8643 rejects any repeat of the same order type > 0 whatever the distance; Emit re-arms beyond 110 m, so a rejected Emit costs vanilla's tooltip lookups and a terrain sample but no `Debug.Log` (only on accept, 8666). Today only #62 calls Emit, bounded by its signature check and 120-tick throttle; BC4's executor caller needs its own throttle.
- **Dispatch mapping table (spec §C):**

  | Task | Vanilla order type |
  |---|---|
  | AttackObjective (enemy-held) / Exploit | 0 |
  | Assault / charge-supported attack | 4 |
  | SupportAttack | 2 |
  | HoldObjective / Defend / FallBackToLine | 12 |
  | ReserveWait / ReleaseReserve | 11 |
  | Screen / Probe / GuardFlank | 1 |
  | FixEnemy | 3 |
  | everything else | `None` |

  Types 13, 14 and 15 are never returned (test-pinned).

**Steps:**
- [ ] Tests first:
  - `OwnershipBrigadePlayerOwnsDescendantsOnly`
  - `OwnershipDivisionCorpsArmyPlayer`
  - `OwnershipCicPlayerOwnsAll`
  - `OwnershipTakeoverUnit`
  - `OwnershipFeudUnitFlagged`
  - `OwnershipNonWlPlayerSideOwnedUnlessAiVsAi`
  - `AncestorsNotProtected`
  - `ChainDetachedOrdersGroup`
  - `ChainNotDetachedFansOutSkippingPlayerChild`
  - `ChainPlayerAtRootFansOutNothing`
  - `ChainPlayerTwoLevelsDownFansOutAtEachLevel`
  - `DispatchMapperTable`
  - `DispatchMapperNeverEmits13_14_15`
  - `DispatchDedupeSameTypeWithin110m`
- [ ] Rewrite `TacticalWriteGuard`. `IsWlCurrentCommandOrChain` and `WlOwnershipSafe` are deleted, and every existing caller keeps compiling through the `Regiment` overloads.
- [ ] Move `DedupeState` from `PlayerSubordinateOrderPatch` to `TacticalPlayerDispatchRuntime.Dedupe`, and route #62's `CheckCurrentOrderUpdate` call through `Emit` (same arguments, width 100, depth 50). Scope `tactical.dispatch.emit`.
- PROVE-MODE: per-slice proof is the harness (`PlayerOwnershipTests`). The Unity `Regiment` overloads (`TacticalWriteGuard` `HasExposedOwnedDescendant`/`DetachedNodeBetween`) and #61's per-unit guard are proven in the BC7 in-game smoke: a W&L battle where the player's command is detached and its AI parent is ordered and rotated — expect zero writes to player-owned regiments.
- [ ] **Acceptance:**
  - `rg -n "IsWlCurrentCommandOrChain|WlOwnershipSafe" src/WhiskeyRealism/Tactical/TacticalWriteGuard.cs` gives no hits. `PlayerCommandProbeFilter` (BC5a) and `TacticalWlActionGuard` (BC5b) are removed by those workstreams; BC7 checks both greps repo-wide.
  - Tests pass.

---

## BC4 — OrderArbiter, OrderExecutor, validator, tick hook

**Owned files:** create `Tactical/Battle/OrderArbiter.cs` (pure), `Tactical/Battle/OrderExecutor.cs` (pure pipeline decisions plus bookkeeping), `Tactical/Battle/OrderExecutorRuntime.cs` (runtime vanilla calls), `Tactical/Battle/WaypointValidator.cs` (pure), `Tactical/Battle/WaypointValidatorRuntime.cs` (runtime terrain probes), `Tactical/Battle/SilentExitPrecheck.cs` (pure), `Tactical/Battle/BattleCoreTick.cs` (runtime), `Patches/BattleCoreArbiterPatch.cs`; modify `Util/TacticalStanceWriter.cs` (Stance channel executor, doctrine-clock records), `Util/TacticalCombatBehaviorWriter.cs` (CombatBehavior channel executor, per-regiment support), `Tactical/TacticalStanceOwnership.cs` (record time base), `Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs` (call `BattleCoreTick.EnsureSnapshot` first in `Tick`; pass the snapshot into the heavy path); move `VanillaMapBounds`, `VanillaPathAreaCosts`, `CommandWaypointWritePolicy`, `TacticalBattleClock` from `Tactical/Operations/CommandPostureExecutor.cs` into `Tactical/Battle/OrderExecutor.cs` (BC5a deletes the originals). Test file `OrderArbiterTests.cs`.

**Vanilla to read:**
- `UpdateAITasks` 5857-5872; `CalculateSideStatsAndUpdateAITasks` 84864-84869.
- `SetWaypoint` 91232-91640, including the silent exits at 91300, 91331, 91338-91360 and 91362; queue 91483-91510; groups 91559.
- `SetGroupFormation` 91822-92110; `ChangeCombatBehavior` 90944-90966; `ChangeStance` 90772.
- `RegimentSetPath` bounds 131057-131068 and `CheckIfFinalWaypointIsOnTerrain` 25574; `BattlefieldSetup.CheckTerrainLine` 27638/27650.
- `AddPath` area costs 130364-130394; `AssaultMove` 3643 and `CheckFlankMoves` 6235 (immediate in-contact moves); `AdjustGroupFormations` 5886.

**Interfaces — Produces:**
```csharp
public sealed class OrderArbiter : IIntentSink {
  public const float DedupeMeters = 60f;   // = #61 MinWaypointDistance (2026-05-21 microshift fix, patch :36-43)
  public void BeginTick(BattleSnapshot s);
  public void Submit(in BattleOrderIntent intent);
  public int Resolve(BattleOrderIntent[] winners);                 // one per (group, channel); ties keep the previous tick's source (hysteresis)
  public bool TryGetClaim(int groupId, OrderChannel channel, out BattleOrderIntent claim); // read-only to patches
  public static OrderArbiter ForAlliance(int alliance);            // 0/1 bound-checked; Europe (2) → null
}
public sealed class OrderExecutor {
  public const float RecentOrderBattleSeconds = 180f;                  // = #61 RecentOrderHours 0.05 h (patch :30); 30 s expired inside one 0.015 h macro cycle
  public const float CourierIntervalBattleHours = 0.25f;               // was #61 OutboundCourierIntervalSeconds 900
  public const float FailedWriteBackoffHours = 0.05f;
  public const float RetryBearingDegrees = 30f; public const int MaxRetries = 1;
  // task hold reads Plugin.BrigadeTaskHoldSeconds (existing, default 12) via TacticalBattleClock.RealSecondsAtOneXToHours
  public ExecutionPlan Plan(BattleSnapshot s, in BattleOrderIntent winner, double nowHours);   // pure: guard→chain→validate→precheck→dedupe→cooldown
  public void RecordIssued(in BattleOrderIntent i, double nowHours, IssuedTarget[] targets, int targetCount,
      UnitWaypoint[] waypointsBefore, int waypointCount, int pathsBefore, int pathsAfter); // targets: unit id + its target; waypointsBefore: every target's and descendant's pre-call waypoint
  public void ObserveOutcomes(BattleSnapshot s, double nowHours, OrderOutcome[] outcomes, out int count); // Moved/Failed/Retry/GaveUp
  public static OrderExecutor ForAlliance(int alliance);
}
public struct ExecutionPlan { public bool Execute; public SkipReason Skip; public ChainResolution Targets; public float X, Z; public bool UseOrderDelay; public int DispatchType; }
public static class WaypointValidator {
  public const float FriendlyFireClearanceMeters = 40f;
  public const float FriendlyFootprintRadiusMeters = 60f;           // = #61 MinWaypointDistance; decision recorded
  public const float ArtilleryPointBlankMeters = 60f;               // = ArtillerySetWaypointVetoPatch.PointBlankMeters
  public static SkipReason Validate(BattleSnapshot s, int groupIndex, in BattleOrderIntent i, float sizeX, float sizeZ,
      ITerrainProbe terrain, out float x, out float z);
}
public interface ITerrainProbe { bool IsWaterOrFence(float x, float z); bool LineCrossesWater(float x0, float z0, float x1, float z1); bool StepBackFromRiver(float x0, float z0, ref float x1, ref float z1); }
public static class SilentExitPrecheck { public static SkipReason Check(in UnitRecord group, OrderChannel channel); } // onretreat/isrouted 91362, garrison 91331, inactive
internal static class BattleCoreTick { // runtime
  internal static BattleSnapshot EnsureSnapshot(AIBattle battle, int alliance);
  internal static void RunArbiter(AIBattle battle, int alliance);      // produce → resolve → execute → outcomes
  internal static void RegisterProducer(IBattleIntentProducer producer); // static ctor registration only, fixed order array
}
public static class BattleCore { public static bool TryGetSnapshot(int alliance, out BattleSnapshot snapshot); }
internal static class OrderExecutorRuntime {
  internal static bool ExecuteInline(BattleUnits bunits, Regiment group, in BattleOrderIntent intent, out OrderOutcome outcome); // patches (D4)
}
```
**Pipeline, per winner:**
1. Guard: `TacticalWriteGuard.CanWrite(record)`.
2. Chain: `TacticalChainOrderPlanner.ResolveTargets`.
3. Waypoint validation (Move and Facing):
   - Clamp to x ∈ [1, sizeX−2], z ∈ [−sizeZ+2, −1].
   - Run `BattlefieldSetup.CheckIfFinalWaypointIsOnTerrain(start, target, 4)`; on a water or fence target, step back toward the start in 25 m steps up to 4 times, else skip `Water`.
   - `CheckTerrainLine(start, target, {4, 8}, 2f)` applies only to `InContactWithdrawal` moves (immediate, under fire). Planned moves are not line-checked, because `AddPath` routes them over fords and bridges (130364-130394). The line starts one 2 m step out, because `CheckTerrainLine` tests its start position first (27659-27686) and would otherwise reject every withdrawal of a group standing at a fence or wall. Battle terrain ids come from the battleprefs.txt table (`GamePrefs.loadcontrols` 54325-54330; NavMesh area indices, bake 26596-26623): 4 river, 5 river crossing (ford), 6 bridge, 8 fences/walls. 8 is sea only in the campaign table (campaignprefs.txt; `GrabFleet` 12224, `SetRetreatToDepot` 116051). A ford cell carries 5, not 4 (the bake keeps the crossing area over the river, 26601-26618; 5/6 are the crossing positions, 26628), so a group on a ford never trips `{4, 8}` at its start cell — for fords the start-cell skip is moot; it matters for a group at a fence or wall.
   - Reject a target inside a friendly group's footprint unless `ReliefOrSupport`.
   - For `ArtilleryFirePosition`, reject when the segment from the target to `FireTargetId` passes within 40 m of a friendly group, and reject when a visible enemy is within 60 m.
4. Silent-exit pre-check.
5. Dedupe: same target within 60 m (#61's `CommandWaypointWritePolicy.ShouldSkipDuplicateWaypoint` at `MinWaypointDistance`) while the group's last waypoint matches and it is moving or already standing there, or a pending move has that target; or the same stance, formation or behavior already ordered. #61's interrupted-path radius (`InterruptedReissueDistance` 60 m) equals the base radius, and the snapshot carries no `pathinterrupted`, so it adds nothing.
6. Cooldown: recent-order 180 battle-s (#61's 0.05 h), task hold, courier 0.25 h, failed backoff. `Safety` bypasses recent-order and task-hold only.
7. Vanilla call:
   - Stance runs before Move, and Move before Formation (#61 L768-774, L976-988).
   - Group `SetWaypoint(showmovementoptions:false, useorderdelay:intent has UseOrderDelay && !InContactWithdrawal)`.
   - `PerRegiment` combat behavior goes through `TacticalCombatBehaviorWriter.EnsureBehavior` per regiment.
8. Outcome next tick (D6): `Moved` also when vanilla took the target — a target unit's or a descendant's last waypoint moved onto it within `DedupeMeters` (SetGroupFormation stamps it, 92067; RegimentSetPath 131254/131258) — because a redirected moving group keeps its moving-unit count and `AddToOrderQueue` skips a same-type in-flight order (91490/91502). A unit (target or descendant) whose waypoint was already on the target before the call is ignored; every inspected unit's pre-call waypoint is recorded. `Failed` → one retry at ±30° (first +30°, then −30° if the +30° target fails validation) → `GaveUp`, reported to the source through `OrderExecutor.LastOutcome(groupId, channel)`.
9. W&L dispatch: when the winner is a Move, `PlayerShareNeedsDispatch`, the plan executed for an ancestor, and `TacticalDispatchTypeMapper.Map(...) != None`, call `TacticalPlayerDispatchRuntime.Emit(ancestor, type, x, z, heading, name)` once per executed plan. Move only, because the dispatch carries the move's target. Its throttle is the Move pipeline itself — dedupe, the 180 battle-s recent-order cooldown, the task hold and the courier cadence (Safety skips only the recent order and the task hold), one winner per group per arbiter tick plus at most one retry of a failed move (`MaxRetries` 1) — with Emit's 110 m record and vanilla's own repeat check (8643) behind it.

**Steps:**
- [ ] Tests first:
  - `HigherPriorityWinsPerChannel`
  - `ChannelsResolveIndependently`
  - `TieKeepsPreviousWinner`
  - `DedupeWithin60m`
  - `SafetyBypassesRecentOrderButNotDedupe`
  - `TaskHoldBlocksSameTaskOnly`
  - `ClampMatchesVanillaBoundsNegativeZ`
  - `WaterTargetStepsBack`
  - `WaterTargetRejectedAfterFourSteps`
  - `WithdrawalFromFenceCellSkipsStartCell`
  - `FriendlyFootprintRejectedUnlessRelief`
  - `FriendlyFireLineRejectsArtilleryPosition`
  - `PointBlankRejectsArtilleryTarget`
  - `PrecheckSkipsRoutedOnRetreatGarrison`
  - `CourierMoveCountsAtTheCall`
  - `SilentExitFailsAtNextObservation`
  - `FailedRetriesOnceRotatedThenGivesUp`
  - `StanceBeforeMoveBeforeFormationOrder`
  - `PlayerOwnedNeverExecuted`
  - `FeudNeverExecuted`
  - `AncestorFanOutDispatchesOnce`
  - `InlineIntentRefusedUnderHigherClaim`
  - `ArbiterResolveAllocatesNothingAfterWarmup`
  - `StanceRecordUsesDoctrineClock`, a night-jump fixture
  - `StanceCheapPathNeedsLiveVanillaStamp` (the writer's short-circuit also needs vanilla's raw 4264 stamp live)
  - `RedirectOfMovingGroupIsMoved`, `StaleWaypointNearTargetIsNotATake`, `DescendantAlreadyOnTargetIsNotATake`, `FanOutOutcomeCountsTheOrderedSet`
  - `GatedOffArbiterPublishesNoClaims`
- [ ] Implement `BattleCoreArbiterPatch`:
  - `[HarmonyPatch(typeof(AIBattle), "AdjustGroupFormations")] [HarmonyPostfix][HarmonyPriority(Priority.Last)]`.
  - Scope `tactical.patch.battle-core-arbiter` (5.0).
  - Gate per D3 → `BattleCoreTick.RunArbiter`.
  - Inner scopes `tactical.arbiter.resolve` (0.5) and `tactical.arbiter.execute` (2.0).
- [ ] Telemetry row per side, every 30 s realtime: `[OrderArbiter] alliance=<a> intents=<n> winners=<n> issued=<n> moved=<n> failed=<n> retried=<n> gaveUp=<n> skipped=<n> deduped=<n> superseded=<n> topSkip=<reason>` (`superseded` counts retries whose source no longer holds the claim). The first execution logs OnceLog `tactical-battle-core:arbiter-first-fire`: `[OrderArbiter] first vanilla order issued channel=<channel> source=<source>`.
- [ ] **Acceptance:** tests pass; the alloc test is green; the arbiter patch compiles with zero producers registered (an empty run is a no-op).

PROVE-MODE: harness. Per-slice proof is `OrderArbiterTests` — arbiter resolve (priority, channels, tie hysteresis, execution order, zero allocation after warm-up), executor outcomes (Moved/Failed/Retried/GaveUp, redirect of a moving group, fan-out set), dedupe and cooldowns, ownership skips, and the waypoint validator. Runtime proof is the BC7 in-game smoke: no producer registers before BC5, so the arbiter path cannot run in the game from this slice.

**BC4 merge items:**
All bound at the BC4 merge:
- BC1 merge: `BattleSnapshot : IBattleUnitView` (`ViewOf` deleted), `EnsureSnapshot` calls `BattleSnapshotBuilder.Build`, `BattleCore.TryGetSnapshot` reads `BattleSnapshotBuilder.TryGetCurrent` (one registry), the executor resolves live units with `BattleSnapshotBuilder.UnitAt`. `UnitRecord.QueuedOrders` = own `orderqueue.Count` + units of {self} ∪ `allattachedunits` with `ordertypeactive[0] || [1]` (the in-flight move signal, 124998/124878); a group's `Formation` is `groupformation`.
- `ArmyPostureInputsRuntime.cs:103-111` (`ExtendedProbes`) must not cache an empty probe list before the first snapshot build: an empty result cached under an unchanged roster signature stays for the battle (from the BC1 verify).
- BC3 merge: replace `ModeOrderGuard`, `SelfOnlyChainResolver`, the local `ChainResolution` copy and the null dispatch mapper/emitter with BC3's guard, chain planner, mapper and `TacticalPlayerDispatchRuntime.Emit`. The planner and mapper are called directly (no seam interfaces); the guard stays behind `IOrderGuard` because it reads the Commander Mode.

---

## BC5 — Migration (three parallel, disjoint sub-workstreams)

Every mover's private guard, dedupe, cooldown, waypoint and ownership code is deleted. Its decision logic becomes an `IBattleIntentProducer`, registered from that producer's static constructor through `BattleCoreTick.RegisterProducer`. Producers read the `BattleSnapshot` only. Fixed producer order in `BattleCoreTick`: Withdrawal, ArtilleryDoctrine, ArtilleryPointBlank, GroupStance, ChargeDemotion, Posture, FacingPulse, FireControl, Play, DivisionPlay, ReserveRelease, Charge.

### Prefix-skip / snapshot-restore battle patch inventory (binding decisions)

| Patch | Target | Mechanism | Decision | Vanilla reason |
|---|---|---|---|---|
| #42 `BattleFeudActionGatePatch` | `CheckForFeudGroupActions` | Prefix-skip, replaces body | **Keep the Prefix** (it steers vanilla's own feud decision on the feud instance). Its `SetWaypoint` becomes `OrderExecutorRuntime.ExecuteInline` with `IntentSource.FeudReplay`, `Priority.Support`. Ownership comes from snapshot flags; `TacticalWlActionGuard` is deleted. The guard's `Feud` rule exempts `FeudReplay` (vanilla's own feud move), but never exempts `PlayerOwned`. | Vanilla body 5871 is the only feud mover. |
| #59 `BattleReserveCommitGatePatch` | `CheckUseOfReserves` | Snapshot/restore | **Keep** (vanilla commits reserves inside the micro call, 6170; only a rollback can deny). Merge B8's Deny rollback into it. Reserve "release" decisions (`ReleasedByAllocator`) become `ReserveReleaseIntentProducer` (Support). `SumEnemyStrengthWithinAngle` is replaced by `snapshot.SlicePressure`. | 6062-6170. |
| #52 `B8CheckUseOfReservesPatch` | `CheckUseOfReserves` | Snapshot/restore plus `RegimentSetPath` | **Delete the patch.** Deny rollback moves into #59; the withdrawal fallback becomes `WithdrawalIntentProducer` (Safety). | 6062+. |
| #56 `TacticalReserveOrderDelayGuardPatch` | `CheckUseOfReserves` | Removes the added path, re-issues delayed | **Keep the path removal** (steers vanilla's own commitment to courier parity). The re-issue becomes `ExecuteInline` (`ReserveCourierParity`, Support, `UseOrderDelay`, `DoubleQuick`). `TryClearStalledFallbackOrder` moves to `WithdrawalIntentProducer`. `RemoveAddedPaths` keeps one copy, in #59. | 91483-91510 courier queue. |
| #44 `BattleMacroStrategyPatch` | `CheckGlobalAIStrategy` | Shadow restore of `macroai` | **Keep** (macroai is not an arbiter channel, per spec). Slice and reinforcement reads switch to the snapshot. | 6314-6467. |
| #55 `TacticalHqAutoLinkGuardPatch` | `Regiment.MoveNonAIUnits` | Snapshot/restore `unitlinkedto` | **Keep** (steers vanilla link input; no movement). Ownership switches to `TacticalWriteGuard.CanWrite(Regiment)`. | — |
| #53 `TacticalPathfinderDisciplinePatch` | `Regiment.AddPath` | Result override | **Keep** (vanilla retry loop input). | 130511. |
| #57 `BattleReserveDoctrinePatch` | `AssignReserves` | List mutation | **Keep** (steers vanilla's reserve list). | 7017. |
| #43 `TacticalFallbackRetreatNullGuardPatch` | three micro methods | Finalizer | **Keep** (vanilla NRE guard). | — |
| `ArtillerySetWaypointVetoPatch` | `BattleUnits.SetWaypoint` | Postfix cancel and re-issue | **Delete.** Whiskey targets are rejected by `WaypointValidator` (point-blank). Vanilla's own too-close battery moves are caught next tick by `ArtilleryIntentProducer` (Safety retreat when a battery's last waypoint is within 60 m of a visible enemy). The flag `EnableArtilleryWaypointVeto` is removed. | 91232. |
| #60 `TacticalDeploymentTerrainDisciplinePatch` | `DoPlacementAIUnitsWithinDeploymentzoneNew` | Postfix `SetGroupFormation` | **Keep outside the arbiter.** It runs in deployment (state < 5, before any doctrine tick). Ownership switches to `TacticalWriteGuard.CanWrite(Regiment)`. | Deployment phase. |
| #62 `PlayerSubordinateOrderPatch` | `UpdateDLCPlayerOrders` | Observe + dispatch | **Keep.** Dispatch goes through BC3's emitter (D9). | 6747. |

### BC5a — #61 posture executor, plays, division play

**Owned files:**
- Delete `Patches/BattleCommandPostureExecutorPatch.cs`.
- Create `Tactical/Battle/Producers/PostureIntentProducer.cs` (runtime adapter plus pure decision calls), `Tactical/Battle/Producers/PlayIntentProducer.cs` (runtime).
- Modify `Tactical/Operations/CommandPostureExecutor.cs`: delete `PlayerCommandProbeFilter` and the classes BC4 moved; keep `CommandPostureExecutor.Decide`, `CommandFormationWritePolicy`, `MainArrivalHoldLatch`, `LeafBrigadeMovePolicy`, `LeafFormationSlot`, `PostureExecutorBattleGate`.
- Modify `Tactical/Orchestrator/ArmyPostureInputsRuntime.cs` (roster from the snapshot; use the `PlayerOwned` flag instead of the probe filter), `Tactical/Orchestrator/Plays/PlayExecutorRuntime.cs` (snapshot centroids, doctrine-clock recompute gate), `Tactical/Operations/TacticalScourgeInspiredDoctrine.cs` (division-play input from the snapshot; courier cadence moves to the executor), `Patches/TacticalObserverPatch.cs` (L979 consumer of `CommandPostureExecutor.Decide` reads the executor's `LastOutcome` instead of re-deciding).
- Test file `PostureMigrationTests.cs`.

**Steps:**
- [x] Tests first:
  - `PostureProducerEmitsStanceMoveFormationForLedgerTask`
  - `PostureProducerLeafBrigadeTargetFromCommandParentMap` (replaces `GrabNextParentUnit`/`transform.parent` at patch L2557, L3330, and the spec's :2946)
  - `PostureProducerKeepsBattleNavMeshAreaCosts` (R5 pin: Roads 1, Bridges 1, FencesWalls 2, Rivers 500000/2)
  - `PostureProducerFacingPulseIsFacingChannelScorer`
  - `FireControlIsCombatBehaviorChannelExecutor`
  - `DivisionPlayLosesToExecutorOnSameChannel` (D8)
  - `PlayCentroidsUseVisibleEnemiesOnly`
  - `PlayRecomputeGateOnDoctrineClock`
  - `ArmyPostureInputsExcludePlayerOwnedNotAncestors`
  - `BlockedMovingOrderPathIsSkippedOutcome` (replaced by `AttackMovesOver150mAvoidCoveredCrossings`: the deletion it pinned rested on a false premise, see below)
- [x] Port each #61 decision path into the producer:
  - Task-rewrite chain (patch L156-255): division play, doctrine order, GT recon, cavalry follow, flank emergency, opportunity override.
  - `ResolveMainArrivalHold`, leaf brigades and nav plan.
  - Fire control → `CombatBehavior` intents with `PerRegiment`.
  - Facing pulse → `Facing` intents.
- [x] Delete from #61 without porting:
  - `_lastExecutorOrderAt`, `_writeBackoffUntil`, `_brigadeTaskHoldState`, `_lastCourierAt`, `_lastOutboundOrderSignatureByGroup`, `ShouldSkipDuplicateWaypoint`, `IsSafeWaypoint`, `CanWrite`, `HasRecentExecutorOrder`, `AllowsOutboundCourier` and `AllowsOutboundOrderLedger` (the executor owns these now).
  - The `groupsubordinatesmoving` stamp (L1118). The executor's outcome tracking (D6) replaces it; vanilla recomputes the field itself.
  - ~~The `BlockedMovingOrder` path (L1038): delete it, because vanilla's `UpdateBlockedCrossings` (7917) handles crossings.~~ **Corrected (verify REVISE):** that premise is false. `UpdateBlockedCrossings` (7917-7973) only rebuilds `AIBattle.blockedcrossings` (crossings an enemy covers by fire, plus cover positions). The avoidance is `AIBattle.BlockedMovingOrder` (3054-3150: `NavMeshObstacle`s over those crossings, `SetWaypoint(useorderdelay:false)` two frames later from `UpdateOrders` 3382), which vanilla uses for its own line-group moves over 150 m (6953-6956); a plain `SetWaypoint` never sees the list. Ported as executor option `IntentFlags.AvoidCoveredCrossings` (see As built).
- [x] Scopes `tactical.producer.posture` and `tactical.producer.play` (2.0).
- Scope note (BC3 verify r3): #61 `RotateRegimentsInPlace` rotates AI regiments inside detached AI sub-groups; when migrated to the arbiter it must follow vanilla's group-order exclusion of detached sub-groups (`GetAttachedUnitsReg(excludedechainedunits)` 119865).
- [x] **Acceptance:**
  - `rg -n "SetWaypoint\(|SetGroupFormation\(|RotateRegiment\(|ChangeCombatBehavior\(|EnsureStance" src/WhiskeyRealism/Tactical/Battle/Producers src/WhiskeyRealism/Tactical/Orchestrator` finds nothing.
  - No `GrabNextParentUnit` or `transform.parent` in the moved code.

**As built (BC5a):**
```csharp
namespace WhiskeyRealism.Tactical.Battle.Producers {           // runtime, not in the test csproj
  internal static class PostureProducers { internal static void EnsureRegistered(); } // runs the four static ctors; called from ArmyPostureInputsRuntime.Refresh (coordinator tick, 5866)
  internal sealed class PostureIntentProducer : IBattleIntentProducer      // Name "Posture": AI-pool groups (Executor) + nested leaf brigades
  internal sealed class FacingPulseIntentProducer : IBattleIntentProducer  // Name "FacingPulse": Scorer, Facing per regiment + brigade Formation
  internal sealed class FireControlIntentProducer : IBattleIntentProducer  // Name "FireControl": Executor, CombatBehavior, PerRegiment
  internal sealed class DivisionPlayIntentProducer : IBattleIntentProducer // Name "DivisionPlay": Play priority (D8)
}
namespace WhiskeyRealism.Tactical.Operations {                // pure, CommandPostureExecutor.cs
  public static class PostureSense { CloseContactMeters = 250; AggregateDescendants; CloseEngaged; Routed; ActiveMove; PendingOrder;
    Physical; PathInterrupted; InCover; RefuseFor; IsMoveAway; IsFireControlUnit; HeadingTo; NormalizeHeading; SubtreeAcceptsGroupFacing; CollectRotatableRegiments; CollectDescendants; }
  public struct PostureIntentRequest { ... }  public static class PostureIntentPlan { Submit; SubmitFacing; AvoidsCoveredCrossings; LeafMove; FacingPulse; FacingPulseFormation; FireControl; IssuedWithin; }
  public static class LeafSlotTarget { TryResolve(IBattleUnitView, leafIndex, refuseFlankDepth, refuseFlankSkew, out parent, out x, out z, out heading); RefusedSetback; }
  public static class PostureProbeFilter { WithoutPlayerOwned(probes, IBattleUnitView); }   // replaces PlayerCommandProbeFilter
  public static class PostureNavAreaCosts { AreaNames; Cost(area, frozen); }               // R5
  public static class TacticalDivisionPlayInputs { ParentIndex; FromSnapshot; }            // TacticalScourgeInspiredDoctrine.cs
}
namespace WhiskeyRealism.Tactical.Orchestrator.Plays { public static class PlayAnchor { MinRecomputeHours; RecomputeDue; EnemyAnchor; CollectOwn; } } // pure, new file
// ArmyPostureInputsRuntime: InAiPool(alliance, id) (AIBattle.unitsused captured on the coordinator tick), SideDefending(alliance) (GetBattleType == 2).
// OrderExecutorRuntime (F7): Formation/Facing feed CommandFormationWritePolicy BattleWriterPolicy.SnapshotEnemyStrengthInRange(snapshot,
//   group, max(maximumlineofsight[0..3], distancetoenemytocancelinterruption)) - the group's own reach (120558/120563) - instead of live groupenemiesinrange.
// IntentFlags.AvoidCoveredCrossings = 256 (additive); CommandWaypointWritePolicy.BlockedMoveDistanceMeters = 150,
//   ShouldUseBlockedMovingOrderForExecutorWaypoint(task, distance); PostureIntentPlan.AvoidsCoveredCrossings(task, from, to).
// PostureSense.CollectDescendants(s, root, ref result, ref children); OrderExecutor.TakesBlockedMovingOrder(intent, crossingCount).
// Snapshot (additive, granted to BC5a round 3): UnitRecord.LastWaypointRotation, GroupWidth, GroupDepth, SlotOffsetX/Z +
//   UnitFlags.HasLayoutSlot (524288); BattleSnapshotAssembler.ApplyLayoutSlot(s, groupIndex, childId, x, z);
//   BattleSnapshotBuilder.CaptureLayoutSlots after the command tree is assembled.
```
- No "Play" producer: the geometric play (PlayExecutorRuntime) is not a mover — it feeds the allocator's zone roles, which reach the battle as ledger tasks the Posture producer executes. The ProducerOrder slot stays.
- The group pass covers the side's AI pool (vanilla `unitsused`, captured in `ArmyPostureInputsRuntime.Refresh`), the leaf pass the leaf-map brigades outside it, as #61 did; producers read no live unit field (`PostureSense` proxies).
- Group Facing goes to the group when no descendant is `PlayerOwned` or `PermanentlyDetached` (`SubtreeAcceptsGroupFacing`); otherwise the posture producer faces the reachable regiments one by one (`PostureIntentPlan.SubmitFacing`). The brigade facing pulse sends its group facing on a clean subtree as shipped (the executor rotates in contact or wheels out of it) and keeps the per-regiment pulses (cap 4). `OrderExecutorRuntime.IssueFacing`'s rotate-in-place walk is `CollectRotatableRegiments` over the snapshot: it stops at any detached or player-owned node between the group and the regiment (GetAttachedUnitsReg(excludedechainedunits) 119865), and each regiment still passes `TacticalWriteGuard.CanWrite`.
- Recent-order cooldown per channel (`OrderExecutor.RecentOrderBattleSecondsFor`, #61's shipped values): Move/Stance/Stop 180 battle-s, Facing 30 (5 s at 1x), Formation 180 (CommandFormationWritePolicy 0.05 h; the policy's 0.1 h under fire applies at execution from the group's last formation/facing write), CombatBehavior 270 (45 s at 1x). The lookup is per (channel, source): ArtilleryDoctrine/ArtilleryPointBlank get no executor window on CombatBehavior/Formation/Facing (BC5b holds each battery on the doctrine clock, Plugin.ArtilleryAuditMinHoldSeconds) and ChargeDoctrine none on CombatBehavior (stateful charge/revert, BC6a); Safety still bypasses.
- Refused flank: `BattleOrderIntent.Refuse` (`RefuseFlank` None/Left/Right, additive BC0 contract) from `PostureSense.RefuseFor` (#61 ResolveRefusedFlank); the executor passes it to SetGroupFormation's refuseflank (92095-92097) on the wheel of both the Formation and Facing paths, and a refuse is never a duplicate.
- Doctrine cooldowns (facing pulse 12/5 s at 1x, fire control 45 s at 1x) read the executor's Issued outcome for that source (`PostureIntentPlan.IssuedWithin`), not producer stamps.
- Leaf slot = shipped #61 `TryFormationSlot` (verify round 2, Option A). The snapshot carries what shipped read live: the parent's `lastsetwaypointposition` and `lastsetwaypointrotation` (stored together, 92067-92068), the leaf's own `groupposition.groupdepth`, and its entry in the parent's layout (`attachedunitid[k]` / `unitoffsetposition[k]`, 78370-78382), kept only when the layout's group is the leaf's D5 command parent (`BattleSnapshotAssembler.ApplyLayoutSlot`). `LeafSlotTarget` computes `GetAngleTerrainPosXYStatic(waypoint, rotation, -offset.x, offset.z + leaf groupdepth)` (26937-26949; 92094 places child groups there: `num + groupdepth2/2`), the leaf move faces that rotation (shipped `slotRotation`), and the executor's validator clamps to the map. The parent's commander, which vanilla stands `groupdepth + groupunitdistancetoback` (unitprefs 10) behind the waypoint (92050), never enters it, so a leaf in its vanilla slot gets no move and a leaf left behind is brought back. The earlier round's proxy (the leaf's offset from the parent HQ, turned by the leaf's facing) is gone: it shifted every leaf forward by that setback and misread refused children. Refused flank: vanilla keeps no refuse state (`refuseflank` is only a SetGroupFormation argument), and `SkewedPosition` (78109-78130) moves a child straight back from its slot by `pow(t | 1-t | |t-0.5|*2, refuseflankskew) * width * refuseflankdepth` (unitprefs 4, 0.5) and turns it to the skewed line, so a leaf within 60 m of its slot or of any of the three refused slots holds and keeps its facing (`LeafSlotTarget.RefusedSetback`); one left farther back goes to the unskewed slot, as shipped. Terrain-shape interpolation (92093-92096) is not applied, as shipped.
- Covered crossings (`BlockedMovingOrder`, ported; round 3: only with at least one listed crossing, `OrderExecutor.TakesBlockedMovingOrder` - an empty list keeps the plain SetWaypoint and its courier delay): `PosturePipeline` sets `IntentFlags.AvoidCoveredCrossings` on an Attack/Support/Fix/AdvanceToAssembly group move farther than 150 m (shipped `TryQueueBlockedMovingOrder`; the division play goes through the same pipeline, the leaf move never did). `OrderExecutorRuntime.IssueMove`, after guard/chain/validator/silent-exit/dedupe/cooldown, reads the side's own non-feud `AIBattle.blockedcrossings` (reflection) and calls `new AIBattle.BlockedMovingOrder(unit, list, 150, target)`; a null list (no river crossings, 7919-7920) falls back to plain `SetWaypoint`. `UpdateOrders` runs every `AIBattle.Update` frame before the pause return (3382) and issues the `SetWaypoint` two frames later, long before the next arbiter tick (`CalculateSideStatsAndUpdateAITasks` runs `UpdateAITasks` once per `sidestatupdatecycle`, 0.015 game-hours, 84572-84576), so D6 sees the move by the moving-unit count or the waypoint stamp (92067); nothing moved at the call. Vanilla's override calls (`overridelastupdate: true`: the end-of-battle withdrawal 84021, the overview lines 218963-220164) re-run the cycle at once on the same battle clock; the executor keeps such a move pending while the clock has not advanced since the call (the recorded intent keeps `AvoidCoveredCrossings` only when the move went through `BlockedMovingOrder`), so it is never judged Failed before `UpdateOrders` issued it. Intentional changes from shipped: the recorded move drops `UseOrderDelay` (vanilla's deferred call has `useorderdelay:false`, so no courier-cadence stamp); with an order already queued for the unit (`OrderRunning`) the executor issues nothing and records nothing (shipped counted it as issued; vanilla never dedupes its order list, so a second order would carve a second obstacle set); only a move without heading or double-quick takes this path (the deferred call drops both; posture moves carry neither). Like shipped and vanilla, `blockedcrossings` is built from unfiltered enemy positions (7939-7967), not the side's perception.
- Flank risk is #44's perceived rule (`BattleWriterPolicy.PerceivedFlankRisk` over the snapshot flank pressure and `ArmyEvidenceBuilder.MeanFlankThreatByGroup`, computed once per tick in `PostureTick`), replacing "any flank pressure > 0": it adds a group's regiments' mean outflanked weight to the group's own state. Used by the posture pipeline, the leaf move, the facing pulse and the division-play input.
- A refuse-only formation wheel passes the threat heading (`PostureIntentRequest.ThreatHeading` onto the Formation intent's `Heading`) as #61 did whenever it had a threat facing, not -1 (which SetGroupFormation replaces with `lastsetwaypointrotation`, 91900-91902).
- Fire control walks the group's own subtree (`PostureSense.CollectDescendants` over `BattleSnapshot.DirectChildren`, the set #61 read from `allattachedunits`; the per-unit `PlayerOwned`/`PermanentlyDetached` skip is unchanged). `PostureTick`'s facing buffers are pooled and grow to the roster (no 64 cap).
- Move dedupe passes the snapshot's `PathInterrupted` to `ShouldSkipDuplicateWaypoint` (numerically neutral: `InterruptedReissueDistance` = `DedupeMeters` = 60 m).
- Granted deletions (verify REVISE): `CommandDoctrineOrder.WithDelivery`/`HasConcreteMovementTarget`, `TacticalStanceWriter.EnsureStanceForTask`, `Battle.CommandWaypointWritePolicy.MovementIssued`/`FailedWriteBackoffHours`/`FailedWriteBackoffUntil`/`IsBackedOff`/`ShouldStampGroupMovementForExecutorWaypoint`, and their tests. The Operations `CommandWaypointWritePolicy` forwarder keeps only `IsExecutorMovementActive` (`TacticalObserverPatch:2373`); tests alias the Battle class.
- **Correction (2500 m cap):** only doctrine targets lost the 60-2500 m safe-waypoint swap (`5e2ba26`); the cap itself is not removed. `MaxWaypointMeters` (2500) still gates the objective approach, the fallback-from-objective, the recovery path and the nav-plan band; only doctrine primary/support/fallback points skip it (shipped swapped those for the objective approach outside 60-2500 m, patch L1472-1486).
- **BC7 smoke watch items (snapshot proxies, code-only):** (1) close engagement is melee in the subtree or a visible enemy within 250 m, narrower than shipped `combatstatussubordinates > 0.2 || engagednonarti > 0.2 || groupenemiesinrange > 0` - watch brigades in a firefight at 250-600 m (formation holds, approach moves, `FallBackToLine` courier vs immediate); (2) flank risk weights remembered enemies (0.5 x confidence) with no friendly term - watch for a stale remembered enemy driving `TaskForLocalFlankEmergency`, `RefuseFor` or the division play's `inTrouble`; (3) leaf slots - a leaf brigade after its parent moves or wheels (goes to its layout slot) and after a refuse wheel (holds its skewed slot; a leaf left up to the refuse depth behind an unrefused parent is the accepted blind spot); (4) an AI attack across a ford or bridge the enemy covers (the obstacle path, `[OrderArbiter]` moved, no `tactical-battle-core:blocked-crossings-*` warning).
- The `TacticalBattleClock` forwarder is deleted (its last caller, `BattleGroupStancePatch`, went with BC5b). The `CommandWaypointWritePolicy` forwarder stays in `CommandPostureExecutor.cs` with only `IsExecutorMovementActive`, which `TacticalObserverPatch:2373` names; BC7 deletes it.
- PathInterrupted and cover (BC5c's `UnitFlags.PathInterrupted`, `UnitRecord.CoverValue`/`CoverObject`) feed `PostureSense`: an interrupted path is no active move, `Physical.PathInterrupted` drives Decide's recovery branches, the settlement gate reads it, and fire control's in-cover input uses vanilla's no-cover rule (covervalue <= 0 or coverobject 3, 5167; #61 used > 0.05).

PROVE-MODE: harness. Per-slice proof is `PostureMigrationTests` (35 tests: per-channel recent-order windows and their per-source overrides, refused flank on the intent and from the threat side, the rotation walk under a nested detached node, group-vs-per-regiment facing, intent mapping per decision, leaf target from the parent map, R5 area costs, facing/fire-control channel and priority, D8 against the real arbiter, fog-honest play anchor and doctrine-clock gate, PlayerOwned probe filter, the covered-crossing flag on objective-type group moves over 150 m and never on a leaf move, a covered-crossing move left pending on the issuing clock, an empty crossing list taking the plain path, perceived flank risk incl. a flanked regiment below, the shipped layout slot at the parent's waypoint and rotation (a 90-degree and a 30-degree case with depth and offset, the parent's commander behind its waypoint), a refused leaf holding its skewed slot, the snapshot carrying the layout slot through assembly, the refuse-only wheel's threat heading, the in-contact non-fallback move carrying no withdrawal flag, the fire-control subtree walk with no cap, detached-subtree exclusion, snapshot proxies incl. a moving subordinate, the 60 m move floor (leaf at its slot), fire control never on batteries, fog-honest enemies-in-range, executor-outcome cooldowns). Runtime proof is the BC7 in-game smoke: `[BattleCore] producers count=… order=…Posture,FacingPulse,FireControl,DivisionPlay…`, `[OrderArbiter]` rows with issued/moved for alliance 0/1, `TacticalCommandPosture`/`TacticalLeafCascade` rows with `writeResult=submitted`, and no W&L player unit moved.

### BC5b — #45 stance, charge demotion, artillery (B7, applier, veto), #42 feud

**Owned files:**
- Delete `Patches/BattleGroupStancePatch.cs`, `Patches/ArtillerySetWaypointVetoPatch.cs`, `Patches/B7CheckAIBombardmentPatch.cs`, `Patches/TacticalArtilleryApplier.cs`, `Tactical/TacticalWlActionGuard.cs`.
- Create `Tactical/Battle/Producers/GroupStanceIntentProducer.cs`, `Tactical/Battle/Producers/ArtilleryIntentProducer.cs` (bombard/cease/counter-battery as CombatBehavior 9/7/8; limber, unlimber and face as Formation/Facing with `Limber`/`Unlimber` flags; retreat and point-blank as Safety Move), `Tactical/Battle/Producers/ChargeDemotionIntentProducer.cs`.
- Modify `Patches/BattleFeudActionGatePatch.cs`, `Tactical/TacticalChargeDenial.cs` (unchanged decision; caller changes), `Tactical/BattleWriterPolicy.cs` (delete `ShouldVetoArtilleryWaypoint` and `PlanArtilleryMicroWrite` callers' leftovers), `Patches/BattleMacroStrategyPatch.cs` (snapshot slice/reinforcement reads; delete `EnemyAngleStrength`), `Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs` **only** in `PopulateResolvedDecisionsForSide` (L1765-1954, which feeds the stance producer).
- Test file `StanceArtilleryMigrationTests.cs`.

**Vanilla to read:**
- `AdjustGroupAIStance` 4221-4278; `CheckAIBombardment` 3869-3911.
- `UnlimberArtilleryAIMicro` 3482; `CheckShorteningDistanceOfAri` 3770.
- `AdjustCavalryBehavior` 5036-5056; `CheckForFeudGroupActions` (feud body).

**Steps:**
- [ ] Tests first:
  - `GroupStanceProducerScorerPriority`
  - `ChargeDemotionIsSafetyStance`
  - `ChargeDemotionBeatsExecutorAssault`
  - `ArtilleryCeaseBombardIsCombatBehavior7WithBombardReset`
  - `CounterBatteryReleasedWhenTargetGone` (port)
  - `ArtilleryRetreatTargetValidatedNotStraightLine180m`
  - `PointBlankVanillaMoveTriggersSafetyRetreat`
  - `ArtilleryNeverStanceWrites`
  - `FeudReplayUsesInlineExecutor`
  - `FeudReplayRefusedForPlayerOwned`
  - `MacroUsesSnapshotSlices`
- [ ] Artillery cadence: decisions run on the arbiter tick rather than per micro call. The per-battery hold reads `Plugin.ArtilleryAuditMinHoldSeconds` (kept) on the doctrine clock. `bombardstarttime = 0` happens inside `OrderExecutorRuntime` when the behavior is 7 from `ArtilleryDoctrine` (vanilla 3904 parity).
- [ ] Delete `BattleGroupStanceMinIntervalSeconds`'s only reader. BC7 removes the flag. The arbiter tick is the cadence.
- [ ] `TacticalStanceWriter.ReleaseForRetreat` stays, called from the arbiter patch gate on macroai 3.
- [ ] **Acceptance:** `rg -n "ChangeCombatBehavior\(|MountUnits\(|RegimentSetPath\(|bombardstarttime" src/WhiskeyRealism/Patches` finds nothing. The only vanilla call sites live in `Tactical/Battle/OrderExecutorRuntime.cs` and `Util/Tactical*Writer.cs`.

**Interfaces — as built (BC5b):**
```csharp
namespace WhiskeyRealism.Tactical.Battle.Producers {                 // runtime; registered from static ctors
  internal sealed class GroupStanceIntentProducer : IBattleIntentProducer   // Name "GroupStance"
  { internal static bool TryResolvedDecision(BattleSnapshot s, int index, out TacticalResolvedGroupDecision d); }
  internal sealed class ChargeDemotionIntentProducer : IBattleIntentProducer // Name "ChargeDemotion"
  internal sealed class ArtilleryIntentProducer : IBattleIntentProducer      // two instances: "ArtilleryDoctrine", "ArtilleryPointBlank"
}
namespace WhiskeyRealism.Tactical {                                  // pure, in the test csproj
  internal static class BattleWriterPolicy {
    bool TryGroupStanceIntent(in UnitRecord group, in TacticalResolvedGroupDecision d, out BattleOrderIntent i); // Stance, Scorer, GroupStanceScorer
    bool ArtilleryBatteryEligible(BattleSnapshot s, int index);
    int  ArtilleryDoctrineIntents(BattleSnapshot s, int index, in BattleArtilleryState b, in BattleArtilleryRules r, BattleOrderIntent[] buffer);
    bool TryArtilleryPointBlankIntent(BattleSnapshot s, int index, float fallbackDistance, out BattleOrderIntent i);
    BattleOrderIntent ArtilleryRetreatIntent(in UnitRecord battery, float enemyX, float enemyZ, float fallbackDistance, IntentSource src, string reason);
    bool ArtilleryHoldActive(double now, double lastOutcome, double hold); bool ResetsBombardStart(in BattleOrderIntent i);
    bool TryFeudReplayIntent(BattleSnapshot s, int index, float neededDistance, out BattleOrderIntent i);       // Move, Support, FeudReplay, UseOrderDelay
    float SnapshotPressure(BattleSnapshot s, int ownIndex);                                                      // #44
  }
  internal struct BattleArtilleryState { /* live own-battery fields the snapshot lacks */ }  internal struct BattleArtilleryRules { /* flags + GamePrefs */ }
  public static class TacticalChargeDenial { internal static bool TryDemotionIntent(in UnitRecord g, in TacticalResolvedGroupDecision d, out BattleOrderIntent i); } // Stance, Safety, ChargeDemotion
}
```
**As built (BC5b, recorded):**
- **Producer registration needs a touch.** The four producers register from their static constructors, but nothing references the producer types, so the constructors never run and `BattleCoreTick.HasProducers` stays false (no BC5 slice owns `BattleCoreTick.cs` or `Plugin.cs`). The integrator/BC7 must run the class constructors once (e.g. `RuntimeHelpers.RunClassConstructor` for every `IBattleIntentProducer` type, or a reflection scan, in `BattleCoreTick`'s static constructor) before the first arbiter tick.
- **F6 (integration verify):** `OrderArbiter.Submit` drops `IntentSource.FeudReplay`, so no producer intent, claim or retry carries the Feud exemption. It is reachable only from #42: through `OrderExecutorRuntime.ExecuteInline` (with a snapshot), and through #42's no-snapshot `DirectGuarded` path (`TacticalWriteGuard.CanWrite(Regiment, feudReplay: true)`; PlayerOwned and exposed player-owned descendants still refused).
- **Shared `OrderExecutorRuntime` edits:** `IssueMount` (Formation intents with `Limber`/`Unlimber` → `MountUnits(unit, 1/0)`, artillery only, formation 3 ⇔ mounted 1 per `ChangeRegimentFormation` 135440-135452), `MountUnits(unit, 1)` before a `Limber`-flagged Move, and `bombardstarttime = 0` after a 7 whose source is `ArtilleryDoctrine` (3904).
- **Group stance decision cache key.** `PopulateResolvedDecisionsForSide` keys by `Regiment.GetInstanceID()` (component); the snapshot keys by GameObject id, so `TryResolvedDecision` takes the cache key from the live unit (`BattleSnapshotBuilder.UnitAt`). If BC2b re-keys the cache to `UnitRecord.InstanceId`, switch the lookup to `own[i].InstanceId`. `PopulateResolvedDecisionsForSide` itself is unchanged (its stance mapping already yields `FinalStance`/`ChargeAction` as the producers need). Groups without a cached decision get no stance intent; vanilla's AdjustGroupAIStance decides them (coordinator decision 3). `PrePopulateResolvedDecisionsAtBattleStart` and `TacticalCacheValidator`'s resolved-cache probe are deleted.
- **Stance hold refresh (coordinator decision 1).** A Stance intent is never a Duplicate and a refresh of the already-ordered stance is exempt from the recent-order cooldown (`OrderExecutor.IsDuplicate` / `InCooldown`): `TacticalStanceWriter` decides (cheap skip while vanilla's 4264 stamp is live, claim + re-stamp when it lapsed). The group-stance rule sends an agreeing intent even for an unsettled group, as #45 re-stamped before its friction gate. Test `HeldStanceRefreshReachesTheWriter`.
- **Order friction.** #45's `TacticalOrderSettlementGate` becomes the snapshot rule "no paths and no courier order in flight" for the Scorer stance; the Safety demotion is not held back by a march (a demotion that waits for the march is too late for BC6b).
- **Artillery.** Bombard (9) stays vanilla's (its write carries `bombardposition`/`bombardrange`, 3898-3901). Retreat direction is away from the closest visible enemy (vanilla `retreatangle` is binned from true enemy slice strength, 122717/122830); the Move is immediate, line-checked, `Limber`, `ReliefOrSupport`, validated by `WaypointValidator`; vanilla's `SetMovementMode(4)` / `skipcover` from `CheckArtyFallback` 3525 are not reproduced. The point-blank watchdog does not delete vanilla's queued order: the immediate `SetWaypoint(newpath)` replaces the battery's paths and a later courier delivery only raises the status of the paths present (`SetOrderStatus` 125482), so it re-paths nothing. Limber intents are Safety (the micro doctrine limbers only to escape, so they must pass the 180 battle-s recent-order cooldown); unlimber, face and combat-behavior intents are Executor. The per-battery hold also delays a stationary battery's retreat (parity with the deleted audit); the moving-battery watchdog is not held. Quadrant threat (wheel test) = visible strength in fire range per 90° around the battery's facing; the wheel's Face heading points at the winning quadrant's strongest visible in-range enemy (vanilla micro facing turns toward one enemy unit's position, 4603), or the closest visible enemy when none is in range and it is closer than firerange × 1.5 (vanilla's turn reach, 4606) (verify r1 major; tests `ArtilleryWheelFacesTheWinningQuadrant`, `ArtilleryWheelFacesTheStrongestInTheQuadrant`, `ArtilleryFallbackWheelHonoursVanillaReach`). The deleted B7/audit inputs `TacticalArtilleryInputAdapter.Snapshot`, `IsEligible` and `ToSupportScreenInput` (test-only after the deletion) are gone; `ArtilleryAmmoRatio01` stays. Per-battery hold reads the executor's last outcome (`TryGetLastOutcome`) on the doctrine clock.
- **#44.** Only enemy reads moved to the snapshot (`SnapshotPressure` for sector and inferred strength; `SnapshotEnemyStrengthInRange` — perceived enemies within vanilla's line-of-sight reach of the group's own position (vanilla reads the group's own `unitrange.enemiesinrange`, 120558/120563), visible in full, remembered by confidence, reinforcements never — for the sector's in-range strength instead of `groupenemiesinrange`; the single implementation, BC5a's `PostureSense.EnemyStrengthInRange` calls it at its merge; and `PerceivedFlankRisk` — the group's own outflanked state over snapshot flank pressure, or BC2b's `ArmyEvidenceBuilder.MeanFlankThreatByGroup` > 0 — instead of `flanksthreated`/`outflanked`, 120566-120580); `reinforcementarrivalswithin24hrs` is the side's OWN arrivals (odds input `reinforcementStrength24h` is added to own strength; vanilla reads it for its own retreat test, 6414), so it stays.
- **#42.** Target = the snapshot's closest visible enemy inside `neededdistancefeudgroupmovement`; vanilla still rolls per eligible group first. Fails closed (coordinator decision 4): without a snapshot for the side the move goes out directly (vanilla's SetWaypoint arguments) only where `TacticalWriteGuard.CanWrite(Regiment, feudReplay: true)` allows it (`BattleWriterPolicy.DecideFeudReplayPath`, test `FeudReplayFailsClosedWithoutSnapshot`); vanilla's unguarded body never runs while Commander Mode is Active. That direct `SetWaypoint` is the one Patches-side mover left in #42 — BC7's grep gate must list #42's fallback with #59/#56/#60/#44. `TacticalWlActionGuard` callers removed: #42, the deleted #45, and `TacticalObserverPatch`'s decision-matrix row (→ `TacticalWriteGuard.IsPlayerProtected`).
- **Flags for BC7:** `BattleGroupStanceMinIntervalSeconds` and `EnableArtilleryWaypointVeto` have no reader; `Plugin.TacticalOperationsLedgerAllowsWrites` is orphaned (its only reader was #45's enable gate); descriptions of `EnableArtilleryAllStanceAudit`, `ArtilleryAuditMinHoldSeconds`, `EnableTacticalArtilleryDoctrine`, `EnableWlTacticalChargeGuard` name deleted patches. First-fire ids removed: `group-stance`, `b7-bombardment`, `artillery-waypoint-veto`.

PROVE-MODE: harness for this slice; in-game at BC7. Per-slice proof is `StanceArtilleryMigrationTests` (exit code 0; a failing assertion throws): stance and demotion intent shapes and arbiter precedence, F6 arbiter refusal of FeudReplay, artillery cease/counter-battery/release, limber/unlimber/face, the validated retreat through `OrderExecutor.Plan`, the point-blank watchdog, eligibility/hold, feud replay through the executor pipeline (Feud exempt, PlayerOwned refused, higher claim refuses), and #44 snapshot pressure. Mutation evidence: dropping the Visible filter on enemy batteries, the `InContactWithdrawal` flag on retreats, or the doctrine flag gate on the counter-battery release each turns a named test red. Runtime proof (producers count, `[OrderArbiter]` rows with artillery/stance sources, a #42 feud replay) is the BC7 smoke, after the producer touch above.

### BC5c — Reserves (#59, #56, B8) and withdrawal fallback

**Owned files:**
- Modify `Patches/BattleReserveCommitGatePatch.cs`, `Patches/TacticalReserveOrderDelayGuardPatch.cs`, `Tactical/Orchestrator/TacticalReserveCommitGate.cs`.
- Delete `Patches/B8CheckUseOfReservesPatch.cs`, `Patches/B8CheckLineFallbacksObserverPatch.cs` (observer only, duplicate ledger resolver; first-fire evidence moves to arbiter outcomes).
- Create `Tactical/Battle/Producers/WithdrawalIntentProducer.cs`, `Tactical/Battle/Producers/ReserveReleaseIntentProducer.cs`.
- Test file `ReserveMigrationTests.cs`.

**Vanilla to read:** `CheckUseOfReserves` 6062-6170 (early exits 6084-6113); `CheckLineFallbacks` 5118; `MicroAICheckForRetreats` 4817; `LinkReservesToLineGroup`; `AssignReserves` 7017.

**Steps:**
- [ ] Tests first:
  - `ReserveDenyRollsBackForDoctrineAndRoleReserve` (merged B8/#59 rule)
  - `ReserveCourierParityInlineUsesOrderDelay`
  - `ReserveCourierParitySkippedUnderSafetyClaim`
  - `WithdrawalFallbackIsSafetyMoveValidated`
  - `WithdrawalCombatBehavior2ViaWriter` (replaces direct `combatbehavior` field writes at B8 L214-215)
  - `ReleaseReserveProducerSupportPriority`
  - `StalledFallbackReissueFromProducer`
  - `VanillaMayCommitReserveMirrorsEarlyExits` (port)
- [ ] Keep one `RemoveAddedPaths` (in #59), and replace the 5 copies of `SumEnemyStrengthWithinAngle` with `snapshot.SlicePressure`.
- [ ] **Acceptance:** no `RegimentSetPath(` or `.combatbehavior =` in `src/WhiskeyRealism/Patches`; `CheckUseOfReserves` has exactly two Whiskey patches (#59, #56) plus the observer.

**As built (BC5c):**
```csharp
internal static class TacticalReserveCommitGate { // pure, Tactical/Orchestrator
  public const float ReleaseStandOffMeters = WaypointValidator.FriendlyFootprintRadiusMeters; // 60
  public Input(bool playerControlled, bool reserveReleased);  // #59 admits only Reserve-role groups: Observe if player-owned, Allow if released, else Deny
  public static Decision Decide(Input input);
  public static bool GroupMayCommit(bool activeInHierarchy, int groupUnitTyp);             // 6084
  public static bool VanillaCommittedUnit(int beforePaths, int afterPaths, int beforeQueue, int afterQueue, bool useOrderDelays);
  public static bool HeldInReserve(in CommandNodeOperationalState state);
  public static CommandIntentResolution HeldResolution(IReadOnlyList<CommandNodeOperationalState> ops, int goId, int componentId, CommandIntentResolution allocator);
  public static bool RemoveVanillaPath(CourierParityAction action, in ExecutionPlan plan);  // #56: Convert && plan.Execute
  public static BattleOrderIntent CourierParityIntent(int unitId, float x, float z, float heading); // Support, UseOrderDelay|DoubleQuick|ReliefOrSupport
  public static CourierParityAction PlanCourierParity(OrderArbiter arbiter, in BattleOrderIntent intent, bool unitWritable,
      bool unitInSnapshot, bool conversionApplies, int beforePaths, int afterPaths, int beforeQueue, int afterQueue, bool useOrderDelays); // Leave | Convert
  public static bool TryReleasedDirectChild(IReadOnlyList<DirectChildIntent> intents, int instanceId, out int sector); // re-roled out of Reserve; AxisSector; no allocation
  public static bool TryPlanRelease(BattleSnapshot s, int groupIndex, int targetSector, out BattleOrderIntent intent);
}
internal sealed partial class WithdrawalIntentProducer : IBattleIntentProducer { // Name "Withdrawal"
  // pure half Producers/WithdrawalIntentProducer.Plan.cs (test csproj), runtime half Producers/WithdrawalIntentProducer.cs
  internal readonly struct Settings { FallbackDistance; RetreatDistance; MoraleTriggerByStance; }
  internal static TacticalWithdrawalDoctrine.FallbackKind PlanUnit(BattleSnapshot s, int index, in Settings settings,
      out BattleOrderIntent move, out BattleOrderIntent behavior); // Safety Move InContactWithdrawal (+ CombatBehavior 2 PerRegiment for Fallback)
  internal static bool TryRetreatBearing(BattleSnapshot s, int index, out float bearing);  // away from closest visible enemy, else front axis + 180
  internal static bool TryPlanStalledFallback(BattleSnapshot s, int groupIndex, in CommandDoctrineOrder order, out BattleOrderIntent move); // PathInterrupted; Support, courier
  internal static float FriendlyRoutPressure(float friendlyRoutedNear); // magnitude of the [-1, 0] penalty
}
internal sealed class ReserveReleaseIntentProducer : IBattleIntentProducer { /* Name "ReserveRelease", Support */ }
```
- `OrderExecutorRuntime.ExecuteInline` is now `TryPlanInline` (claim check + `OrderExecutor.Plan`, no writes) followed by `ExecutePlannedInline`; its signature and behaviour are unchanged for #42. #56 screens with `PlanCourierParity`, plans with `TryPlanInline`, and removes vanilla's path only when `RemoveVanillaPath` holds (the plan executes); any refusal (player-owned, no snapshot record, higher claim, validator, silent exit, dedupe, cooldown) leaves vanilla's own move.
- #59 reads no enemy state. The doctrine branch (B8's deny: it needed `order.Role == Reserve`, which the ledger-first resolution already reads) and its `groupenemiesinrange`/`outflanked` inputs are deleted. The gate's non-Reserve arms, odds inputs and already-engaged exemption were unreachable (the Prefix admits only Reserve-role groups; vanilla commits only unengaged units, 6129, and `inengagement` is set only by `FireBullet`, 119050) and are deleted. So are the now-uncalled `DoctrineConsumerDecisions.DecideReserve` and `OperationalReserveDoctrine` / `OperationalReserveInput` / `Decision` / `Mission`. This also removes the last copies of `SumEnemyStrengthWithinAngle` in BC5c's files, so no `SlicePressure` substitute is needed.
- The withdrawal Move is pre-checked in the producer for the silent exits (`SilentExitPrecheck`) and `WaypointValidator`; a move either refuses is not produced and neither is its CombatBehavior 2. A Move the executor skips later (dedupe, failed-write backoff) can still let behaviour 2 through, which is vanilla's default infantry combat behaviour (112945-112947). The parity intent also carries `ReliefOrSupport` (the target sits one regiment width behind the endangered sibling, inside its group's footprint).
- `OrderExecutorRuntime.IssueMove`: an `InContactWithdrawal` Move on an infantry/cavalry regiment ends in `SetMovementMode(2)`, as vanilla's fallbacks do (5178, 4898).
- `UnitRecord` gains own-side fallback inputs (builder field reads, roster signature unchanged): `CoverValue`, `CoverObject`, `FriendlyRoutedNear`, and `UnitFlags.PathInterrupted`. The withdrawal reads cover and friendly routs from them, and the stalled-fallback re-issue requires `PathInterrupted`. Enemy-derived inputs stay perceived: the retreat bearing is vanilla's `retreatangle` convention (GetAngle 151304 / 122833, walked by GetAngleTerrainPosStatic 26897-26906) from the closest visible enemy; outflanked is fog-honest flank pressure (vanilla's code 123145-123175 reads true positions); under fire is a visible first range-list enemy. B8's `lastfeudactiontime` stamp is dropped: its only reader is the feud icon (UpdateUnitIcon 121676, `ai_feudstance >= 0`), and units of a feuding group are never withdrawn.
- Producers register from their static constructors; nothing in `src` touches the types yet, so BC7 adds the touch.

PROVE-MODE: harness. Per-slice proof is `ReserveMigrationTests` (Reserve-role deny and release, courier-parity plan and refusal, withdrawal Safety move/behavior through `OrderExecutor.Plan`, fog-honest retreat bearing, release target and priority, stalled-fallback re-issue, vanilla early-exit parity, own-side snapshot fields). Runtime proof is the BC7 in-game smoke: a reserve commit converted to a courier order under order delays, a Reserve-role rollback (`[TacticalReserveDrift]`), and `[OrderArbiter]` rows with Withdrawal / ReserveRelease sources.

---

## BC6 — Infantry charge doctrine (spec §4b)

**Owned files:** create `Tactical/Battle/ChargeDoctrine.cs` (pure, BC6a) and `Tactical/Battle/Producers/ChargeIntentProducer.cs` (runtime, BC6b) plus its pure half `ChargeIntentProducer.Plan.cs` (BC6b, in the test csproj); test file `ChargeDoctrineTests.cs`. File a bug-fix note in `docs/bug-fixes/` for the dead vanilla `MicroAICheckForCharges` 4910 compare (BC7 docs). BC6b shared edits (integrator-assigned): `BattleCoreContracts.cs` (two `UnitRecord` fields, one `IntentFlags` value), `BattleSnapshotBuilder.Capture` (two field reads), `OrderExecutor.IsDuplicate` and `OrderExecutorRuntime.IssueOther` (the Disarm path), `PostureProducers.EnsureRegistered` (one registration line).

**Vanilla to read:** `UpdateChargeTarget` 122199-122310; `MicroAICheckForCharges` 4905-4940 (dead compare 4910); micro cycle 6 at 5617-5638; `ChangeCombatBehavior` 90944; `AdjustCavalryBehavior` 5036-5056; GamePrefs `maxchargeradius` 53232, `microaitriggerforcharge` 54776, `weightingfactorsformicroaicharge` 54777-54781.

**Interfaces — as built (BC6a + BC6b):**
```csharp
namespace WhiskeyRealism.Tactical.Battle {                       // pure, ChargeDoctrine.cs
  public struct ChargeInput { public bool ConfigOn; public bool PlayerOwned; public bool IsInfantryRegiment; public int GroupAiStance;
    public bool TargetVisible; public bool TargetIsFirstInRange; public float DistanceToTarget; public float VanillaMaxChargeRadius;
    public float OwnMorale, TargetMorale, OwnFatigue; public bool TargetDisordered; public bool TargetFlanked; public bool LaneBlockedByFriendly;
    public bool Moving; public int MovementMode; public bool ChargeResolved;
    public bool WhiskeyArmed;               // BC6b: producer memory (replaces BC6a's CurrentBehaviorOrdered == 3, see As built)
    public bool IsLeading; public bool WhiskeyArmedPendingDisarm; public double SecondsSinceLastAiChargeRefusal;
    public double RefusalBackoffSeconds;    // BC6b: RefusalBackoffSeconds(timetorenewaichargecheck); 120 s floor
    public bool ExcludedByVanillaFilter;    // BC6b: permanentlydetached | isrouted | markedforrout | feud (4917/4922 filters)
    public bool OutflankedBarsCharge; }     // BC6b: nomicroaichargeifunitisoutflanked set and own outflanked or firefromflank != 0
  public enum ChargeAction { None, Charge, Revert, Disarm }
  public static class ChargeDoctrine {
    public const int AssaultStance = 4; public const int ChargeBehavior = 3; public const int RevertBehavior = 2; // vanilla cycle-6 value for stance ≥ 1
    public const float MoraleMargin = 0.15f; public const float OwnMoraleFloor = 0.5f; public const float OwnFatigueCeiling = 0.6f;
    public const int MaxLeadingRegimentsPerGroup = 2; public const double RearmAfterRefusalBattleSeconds = 120.0;
    public const int NormalMovementMode = 0, ArmedMovementMode = 3, AssaultMovementMode = 6;
    public static ChargeAction Decide(in ChargeInput input);
    public static double RefusalBackoffSeconds(float vanillaRenewHours);   // max(120, renew h × 3600)
    public static bool ChargeCycleEnded(int previousMovementMode, int movementMode);
    public static bool PendingDisarmAfter(bool pending, ChargeAction action, int movementMode);
    public static bool IsLeading(ReadOnlySpan<float> distancesToTarget, int index);
  }
  // BattleCoreContracts (additive): UnitRecord.LastAiChargeTime (lastaichargetime 110798), UnitRecord.Outflanked (outflanked 111488),
  //   UnitRecord.FireFromFlank (firefromflank 111492), UnitRecord.AiGroupId (groupaiobject 110820 instance id);
  // PlayerOwnership.ApplyFeudGroups(BattleSnapshot, ref bool[] scratch) (builder, after ApplyChainFlags), PlayerOwnership.DirectFeudFlags(own, aiGroup, snapshotFeud);
  //   IntentFlags.LeaveChargeMode = 512 (Disarm: the executor also calls SetMovementMode(0) on a regiment in mode 3).
}
namespace WhiskeyRealism.Tactical.Battle.Producers {
  internal sealed partial class ChargeIntentProducer : IBattleIntentProducer {   // Name "Charge"; runtime half ChargeIntentProducer.cs
    internal static void Register();                                             // static-ctor touch from PostureProducers.EnsureRegistered
    // pure half ChargeIntentProducer.Plan.cs (test csproj):
    internal readonly struct Settings { Settings(bool configOn, float maxChargeRadius, float vanillaRenewHours, bool noChargeIfOutflanked = false); }
    internal struct RegimentMemory { Seen; PreviousMode; Armed; PendingDisarm; LastAiChargeTime; HasRefusal; RefusalAtHours; Submitted; SubmittedAtHours; SubmittedMode; }
    internal sealed class ChargeMemory { /* per side: ById, pooled scratch; Clear() */ }
    internal static int Plan(BattleSnapshot s, in Settings settings, ChargeMemory memory, OrderExecutor executor, IIntentSink sink); // returns confirmed charges
    internal static BattleOrderIntent Intent(int regimentId, ChargeAction action);  // CombatBehavior, PerRegiment, Executor, ChargeDoctrine
    internal static int GroupStance(in UnitRecord group);                           // 4 only while ai_stance and ai_stanceordered are 4
  }
}
```
**Rule:** Charge when all of the following hold:
- config on; not PlayerOwned; infantry regiment; group stance 4; target visible and `enemyinrangereg[0]` (D10); distance < `GamePrefs.maxchargeradius`;
- (`TargetMorale + MoraleMargin < OwnMorale` OR disordered OR flanked); `OwnMorale ≥ 0.5`; `OwnFatigue ≤ 0.6`; lane not blocked;
- moving (`regimentpaths > 0`) with `movementmode == 0`.

The "leading" regiments are the `MaxLeadingRegimentsPerGroup` regiments closest to the target. Revert to 2 when the charge resolved (`movementmode` left 3/6), when the conditions fail, or when the group stance is no longer 4. Intents go to the `CombatBehavior` channel, `PerRegiment`, `Priority.Executor`, `IntentSource.ChargeDoctrine`. Charge denial (`ChargeDemotion`, Safety, Stance channel) demotes the group stance, which yields Revert next tick.

**Steps:**
- [ ] BC6a tests first:
  - `ChargeFiresOnWeakerVisibleTargetInRadius`
  - `NoChargeWhenTargetNotFirstInRange`
  - `NoChargeWhenHidden`
  - `NoChargeBeyondVanillaRadius`
  - `NoChargeWhenFatiguedOrShaken`
  - `NoChargeWhenLaneBlocked`
  - `NoChargeWhenStationary`
  - `NoChargeOnPlayerOwned`
  - `RevertAfterChargeResolves`
  - `RevertWhenStanceDemoted`
  - `DenialPrecedenceOverCharge` (arbiter fixture)
  - `OnlyTwoLeadingRegimentsCharge`
  - `ConfigOffNeverCharges`
- [x] BC6b: the producer reads vanilla GamePrefs once per battle into the snapshot scratch, and checks the lane through the snapshot's own positions on the segment (40 m corridor, reusing `WaypointValidator.FriendlyFireClearanceMeters`). Scope `tactical.producer.charge`. OnceLog `tactical-battle-core:charge-first-fire`.

**As built (BC6b):**
- **Vanilla correction (124770-124774).** `Regiment.SetMovementMode(3)` sets `combatbehavior = 0` and `combatbehaviorordered = 0` for infantry and cavalry. So the 122214 gate fires once per behavior-3 write, and an armed regiment reads behavior 0 in modes 3/6. BC6a's "armed = `combatbehaviorordered == 3`" was therefore false exactly when a revert matters (red-first test: `ProducerArmedFromMemoryNotOrderedBehavior` failed with "armed regiment gets an intent"). `ChargeInput.CurrentBehaviorOrdered` is replaced by `WhiskeyArmed`, the producer's memory. Two BC6a header claims were wrong and are corrected there: a refusal does not re-arm the gate on the next frame (behavior is already 0), and the volley-before-the-charge branch (122267, needs `combatbehavior == 3` in mode 3) is unreachable for a regiment armed through the gate. After a refusal vanilla leaves the regiment at behavior 0 (short range); the doctrine's Revert restores 2.
- **Armed = executor-confirmed.** A Charge counts as armed only when `OrderExecutor.TryGetLastOutcome(regiment, CombatBehavior)` reports `Issued`, source `ChargeDoctrine`, at or after the submission (read on the next tick, before the executor runs again). With no such outcome (guard, claim, cooldown) nothing is armed (fail closed). A confirmed Revert or Disarm releases the regiment. A behavior 3 seen on a non-player AI regiment re-arms the memory: nothing else on the AI side writes 3 (the player UI 203769 and `AssignChargeTarget(usechargeorder)` 167621 are player-side; `SetAIStance` writes 0-2).
- **Restored stance-drop cancel (decision point).** Under order delays (the default) the charge courier (behavior 3, queued at 90967) can land after the doctrine's Revert stamped ordered 2. The delivery (125401-125403 → `SetCombatBehavior`, 122175-122178) sets behavior and ordered back to 3, and the 122214 gate fires on the next slow tick, so the producer almost never sees the 3; the regiment would sit in charge mode 3 unarmed. `Decide` therefore restores vanilla's own intended cancel, `MicroAICheckForCharges` 4922-4924 (dead behind 4910): for every AI infantry regiment, armed by the doctrine or not, mode 3 while the group stance is not 4 (and ≥ 0) → Disarm. Mode 6 still waits for 6 → 3. Vanilla's 4917/4922 unit filters (`permanentlydetached`, `isrouted`, `markedforrout`, and a feuding AI group, `aigroup.ai_feudstance != -1` outside `isplayeraiorfeud == 2`) bar arming and the cancel (`ExcludedByVanillaFilter`): the feud instance owns feuding units. Feud is the group's state (verify r2): `ActivateFeud` (116613) sets it on the group, whose `AddUnitToPool(overwriteaigroups)` points every descendant's `groupaiobject` at it (7637-7648). The builder therefore propagates `UnitFlags.Feud` to every unit whose command ancestor, AI group (`UnitRecord.AiGroupId`) or that group's ancestor feuds (`PlayerOwnership.ApplyFeudGroups`, sources are only the units' own `ai_feudstance`, order-independent, zero-alloc), and `TacticalWriteGuard`'s Regiment overloads apply the same rule (`DirectFeudFlags`: own, live AI group, or the side's snapshot flag), so every per-regiment writer refuses them, not just the charge. Ancestors of a feuding group are not marked. Only a unit's own `ai_feudstance` is a source: a unit whose AI group G merely inherits a feud from G's own AI group H stays clear, as vanilla's micro AI tests only the unit's own `aigroup` (verify r3 N5; `FeudPropagatesFromAiGroupAndAncestors`). Known over-protection, accepted as the safe side: when a sub-brigade of a feuding group is separated in contact (`SeparateAIGroup`, 117388-117390 → `SeparateAIGroupFromParentGroup` 85358-85385) it is re-pooled in the main AI with its own `groupaiobject`, yet the feud instance still holds it in `allgroupsassigned` (`AddUnitToPool` 7642-7644; `RemoveUnitFromPool` 7617-7619 runs only when the feud ends, 116557) and `CheckForFeudGroupActions` (4940) keeps moving it. The ancestor rule still marks it Feud, so Whiskey refuses those units rather than fight the feud instance over them. Consequence (verify r1): vanilla's follow-up charge after a contact melee — 122302/122326 leave the regiment in mode 3, and in vanilla it stays armed for a further odds-checked charge — is now disarmed whenever its group is out of assault stance, as the dead 4924 intended; in assault stance it is left to run. BC6a's "a mode 3/6 the doctrine did not arm is vanilla's own" now holds in assault stance only, which matches vanilla's intent (4919 arms in stance 4, 4924 cancels outside it). Residuals, accepted: a Revert for a non-stance reason (lane, margins) in assault stance followed by a late courier leaves vanilla's own charge running, with vanilla's odds check still the gate (4919's intent); config off after arming can leave an assault-stance regiment in mode 3, which is vanilla's own post-melee state (OFF = vanilla).
- **Resolved** (Decide reverts) when the regiment leaves the cycle (`ChargeCycleEnded`), when the refusal stamp changed, or when it is back at mode 0 without behavior 3. The last case covers an arm and refusal both inside one tick, and vanilla's cycle-6 cascade: `SetAIStance` writes the group's own behavior (122099-122120), so cycle 6 re-orders the group to 2 and `ChangeCombatBehavior` cascades to every non-detached unit below it (5631-5634, 90944-90968, PerformActionForGroupForAllUnitsOnLevel 92572-92627). A Revert whose behavior 2 is already ordered writes nothing and releases the regiment.
- **Refusal.** A change of `lastaichargetime` since the last tick is a refusal seen now on the doctrine clock. The first value the producer sees is only the baseline, so a fresh unit's 0 (or a stamp from before the producer saw the unit) is never a refusal. Back-off = `ChargeDoctrine.RefusalBackoffSeconds(GamePrefs.timetorenewaichargecheck)` = max(120 battle-s, renew h × 3600). The shipped renew is 0.5 h (battleprefs: "a renewed charge order is not given if it was broken up before due to bad conditions"), so the effective back-off is 1800 battle-s; vanilla's own use (4917) is dead and compares `lastaichargetime < now + renew` (always true). **Integrator decision point:** the plan named both 120 s and the renew interval; this takes vanilla's intended interval with BC6a's constant as the floor.
- **Inputs from the snapshot.** The target is the regiment's own `FirstEnemyInRangeId` (`enemyinrangereg[0]`, 122228), only while it is a Visible observation; nothing is substituted. Target morale, `outflanked` (`TargetFlanked`) and disorder are read from that visible target's own `EnemyRecords` row, following BC5a's `TargetGarrisoned`/`EnemyAdvancing` precedent. `outflanked` is fog-honest in the main: UpdateFlanking (122959-123175) computes it from the target's facing and this side's own units in its flank sectors. Caveat (verify r1): a sector is also cleared when one of the target's own friendlies stands closer in it (owninrangeregs within `distancetoenemyforoutflank`, 123077-123110), and those friendlies may be hidden from this side, so the flag can carry that much unseen information. Disordered = `MarkedForRout`, `OnRetreat` or movement mode 2/4. Distance runs from `Own[ParentIndex]` (the transform parent, D5; vanilla measures from `transform.parent`, 122257), strictly `<`. With no parent there is no charge. Stance = the parent's: 4 only while `ai_stance` and `ai_stanceordered` are both 4. The demotion writes `ai_stanceordered` at once and `ai_stance` after the courier (ChangeStance 90787/90814), so a demotion reverts on the next tick and a pending assault order arms nothing. Lane: an own standing regiment (not a group) whose projection falls strictly between the regiment and the target, within 40 m of the segment. Leading: `IsLeading` over the parent's direct children's distances to their own visible first target.
- **Own flank (verify r1).** battleprefs "No micro ai charge if unit is outflanked or receiving flanking fire" parses to `GamePrefs.nomicroaichargeifunitisoutflanked` (54784; shipped true) and vanilla never reads it. The producer reads it once per battle with the other constants and, when set, arms no regiment whose own `outflanked` (111488) or `firefromflank` (111492: fire it received from a flank sector within `timetolastfireforflank`, 123208-123264; vanilla's flanking-fire morale effect reads it, 128329) is non-zero (`OutflankedBarsCharge`; holding or reverting an armed regiment is unchanged). Own `outflanked` comes from the regiment's own range list (unfiltered, as vanilla's own state); `firefromflank` is fire the regiment actually received. An unreadable pref fails closed (set).
- **Radius.** `GamePrefs.maxchargeradius` is 50 m in the shipped unitprefs ("calculated from next group commander"), measured from the brigade HQ, so charges are rare: the HQ must be within 50 m of the target. It is kept as vanilla's.
- **Disarm through the executor.** `IntentFlags.LeaveChargeMode` (512; 256 is BC5a's `AvoidCoveredCrossings`). `OrderExecutor.IsDuplicate` does not swallow it while the regiment is in mode 3, even with behavior 2 already ordered. `OrderExecutorRuntime.IssueOther` runs `TacticalCombatBehaviorWriter.EnsureBehavior`, then `Regiment.SetMovementMode(0)` on a regiment (unittyp ≤ 13) still in mode 3. That is vanilla's refusal (122290); the 4922-4924 disarm is unreachable. The write is behind the executor's per-target `TacticalWriteGuard.CanWrite(Regiment)`. ChargeDoctrine has no executor recent-order window on CombatBehavior (BC5a, `RecentOrderBattleSecondsFor`).
- **W&L.** `PlayerOwned` regiments are never charged, reverted or disarmed (Decide), the executor refuses them (`OrderExecutor.Plan` step 1), and the runtime re-checks `TacticalWriteGuard.CanWrite` on the live unit.
- **Config.** `Plugin.EnableInfantryChargeDoctrine` is read on every tick. Off still runs the plan, so what the doctrine armed is handed back (Revert/Disarm), and nothing new is armed. The master `Enabled` switch idles the producer.
- **Registration.** `ChargeIntentProducer.Register()` is called from `PostureProducers.EnsureRegistered` (BC5a's temporary touch, run by `ArmyPostureInputsRuntime.Refresh` on the coordinator tick). On this base nothing registers BC5b's `GroupStance`, `ChargeDemotion` and `Artillery*` producers or BC5c's `Withdrawal` and `ReserveRelease`; their static constructors never run. So the charge demotion (and with it `DenialPrecedenceOverCharge`) cannot happen in game until BC7's explicit registry. The doctrine still reverts on any stance change away from 4.
- First fire: `OnceLog.Info("tactical-battle-core:charge-first-fire", "[ChargeDoctrine] first infantry charge armed: combat behavior 3 issued to a leading regiment of an assault group")`, logged on the tick that confirms the executor issued the first Charge (one arbiter tick after the vanilla write).
- For BC7: `BattleWriteGateTests`' writer regex does not list `SetMovementMode(`; the executor's two calls are gated (file checks `TacticalWriteGuard.CanWrite`), but the scanner would not see a new ungated one.

PROVE-MODE: harness for this slice; in-game at BC7. Per-slice proof is `ChargeDoctrineTests` (BC6a pure block plus the BC6b block, exit 0; a failing assertion throws, exit 134): the producer over a `BattleSnapshot` fixture (two leading regiments, intent shape, armed from memory, executor-confirmed arming, a late charge courier disarmed or re-armed, fresh stamp baseline, refusal back-off with and without vanilla's renew interval, parent-group distance strict `<`, stance from ordered and current, lane between only, target state only from a visible record, never PlayerOwned, deferred disarm after a denied assault, release after vanilla's re-order, config off releases, zero allocation after warm-up), the executor's `LeaveChargeMode` dedupe rule, and `DenialPrecedenceOverCharge` through the real `OrderArbiter` (tick 1: Safety demotion wins the Stance channel while the charges keep their CombatBehavior channel; tick 2: Revert for the courier-pending regiment, Disarm for the armed one). Red first: `ProducerArmedFromMemoryNotOrderedBehavior` (against BC6a's `CurrentBehaviorOrdered` contract), `LeaveChargeModeIsNotADuplicateInChargeMode` (before the executor flag), `VanillaStanceDropDisarmsUnarmedChargeMode` (before the restored cancel) and `ProducerLateChargeDeliveryIsDisarmed` (before the behavior-3 re-arm rule). Mutation evidence (each turned a named test red, exit 134): no renew interval in the back-off; a first-seen non-zero stamp taken as a refusal; group stance read from `ai_stance` alone; lane check without the strictly-between test; a confirmed Revert counted as arming; no re-arm on a seen behavior 3; (verify r1) `IssuedSince` without its source check or without its `AtHours` check; the lane counting inactive friendlies; `MarkedForRout` dropped from the unit filter; the outflanked pref ignored, or applied when clear. Red first (verify r1): `ProducerVanillaUnitFilter` (Feud missing from the filter). Verify r2: `FeudPropagatesFromAiGroupAndAncestors` (BattleSnapshotTests; red before the AI group's ancestors were walked), `DirectWriterFeudFollowsAiGroup` (PlayerOwnershipTests), `ProducerExcludesFeudingBrigadeRegiments`, and the flanking-fire half of `ProducerHonoursNoChargeIfOutflanked`; plants that turned them red: no propagation, no AI-group source, ancestors only, a propagated flag used as a source, `DirectFeudFlags` ignoring the AI group, `firefromflank` ignored. Verify r3: the N5 plant (a propagated flag used as a source inside `SourceOnChain`) turns the AI-group-of-an-AI-group case red ("X not marked through a propagated G"). Runtime proof (the `charge-first-fire` line, `[OrderArbiter]` rows with `ChargeDoctrine` issues, the SetMovementMode(0) disarm, no W&L player regiment touched) is the BC7 in-game smoke.

---

## BC7 — Integration (integrator, sequential, last)

**Owned files:** `Plugin.cs`, `docs/*`, `AGENTS.md`, `MEMORY.md`, the Stage B `tools/smoke-check.py` / `tools/smoke-situational.txt` (extend; create only if Stage B has not landed them), the test csproj (final reconciliation).

- [ ] Wiring checks:
  - Every producer is registered: `BattleCoreTick` logs `[once:tactical-battle-core:producers] [BattleCore] producers count=12 order=…` on first tick.
  - `TacticalBattleCoordinatorRuntime.Tick` calls `EnsureSnapshot` before any evidence read.
  - The heavy path receives the snapshot.
  - `TacticalObserverPatch` no longer re-decides posture.
- [ ] `Plugin.cs`:
  - Remove `EnableArtilleryWaypointVeto`, `BattleGroupStanceMinIntervalSeconds`, `EnableSinglePassObjectiveRecords` (binds, fields, every reader).
  - Confirm `EnableBattleFogHonestPerception` and `EnableInfantryChargeDoctrine` are read.
  - Update the descriptions of `EnableTacticalReserveOrderDelayGuard` ("re-issues through the order executor"), `EnableWlTacticalChargeGuard` (#42 feud replay via executor) and `EnableTacticalArtilleryDoctrine` (arbiter producer) to the real behavior.
- [ ] Gates:
  - `python3 -I tools/orphan-sweep.py .` returns 0, and `--include-test-only` has nothing new.
  - `python3 -I tools/config-audit.py .` returns 0.
  - `./build.sh` gives 0 warnings; the harness is all PASS.
- [ ] Grep gates (must return nothing outside `Tactical/Battle/OrderExecutorRuntime.cs`, `Util/TacticalStanceWriter.cs`, `Util/TacticalCombatBehaviorWriter.cs`, #59/#56 restore code, #60 and #44):
  - `rg -n "\.SetWaypoint\(|\.SetGroupFormation\(|\.RegimentSetPath\(|\.RotateRegiment\(|\.ChangeStance\(|\.ChangeCombatBehavior\(|\.MountUnits\(" src/WhiskeyRealism`
  - `rg -n "transform\.parent|GrabNextParentUnit" src/WhiskeyRealism/Tactical src/WhiskeyRealism/Patches` → nothing.
  - `rg -n "Environment\.TickCount|realtimeSinceStartup" src/WhiskeyRealism/Tactical/Battle` → telemetry throttles only.
- [ ] First-fire markers. Use the Stage B helper if present. Otherwise add `Util/PatchFirstFire.cs` with `internal static void Mark(string patchId) => OnceLog.Info("first-fire:" + patchId, "[PatchFirstFire] " + patchId)` (patchId is a static literal), called first thing in every patch body, plus `tools/smoke-check.py <LogOutput.log>`, which lists ids in `tools/smoke-patches.txt` without a `[once:first-fire:<id>]` line and exits 1. Battle ids to list:
  - `battle-core-arbiter`
  - `reserve-commit-gate` (#59)
  - `reserve-order-delay-guard` (#56)
  - `feud-action-gate` (#42)
  - `macro-strategy` (#44)
  - `hq-auto-link-guard` (#55)
  - `pathfinder-discipline` (#53)
  - `reserve-doctrine` (#57)
  - `fallback-retreat-null-guard` (#43)
  - `deployment-terrain-discipline` (#60)
  - `player-subordinate-order` (#62)
  - `observer-check-global-ai-strategy` (coordinator driver)
  Remove the deleted patches from the list: `posture-executor`, `group-stance`, `b7-bombardment`, `b8-check-line-fallbacks`, `b8-use-of-reserves`, `artillery-waypoint-veto`.
- [ ] Expected telemetry in a 20× smoke battle (tuning sidecar plus LogOutput):
  - `[once:tactical-battle-core:producers] [BattleCore] producers count=12 order=…`.
  - `[once:tactical-battle-core:arbiter-first-fire] [OrderArbiter] first vanilla order issued channel=<channel> source=<source>`.
  - `[once:tactical.battle-perception:mode:RangeOnly] [BattlePerception] alliance=<a> mode=RangeOnly reason=GridNotPainted visible=<n> remembered=<n>` at the first perception tick, then `[once:tactical.battle-perception:mode:FogGrid] [BattlePerception] alliance=<a> mode=FogGrid reason=None visible=<n> remembered=<n>` for the enemy AI side within 5 battle-seconds of its first perception tick. With HQ-in-the-saddle, `… mode=RangeOnly reason=HqInTheSaddle …` and no FogGrid line. A W&L / `ai_vs_ai` player-alliance side logs `mode=OwnSideWithinFog reason=None`. Each line fires once per mode per battle; every per-side mode change is the `BattlePerception` Decision event in the tuning sidecar.
  - `[OrderArbiter]` rows with `moved > 0`, `failed/issued < 0.2`, and no `gaveUp` storm (≤ 5 per side per 30 s).
  - Perf: `tactical.snapshot.build` p95 ≤ 1.5 ms per side, `tactical.arbiter.resolve` p95 ≤ 0.5 ms, no `tactical.patch.battle-core-arbiter` row above 5 ms more than 1 % of calls.
  - `[once:tactical-battle-core:charge-first-fire] [ChargeDoctrine] first infantry charge armed: combat behavior 3 issued to a leading regiment of an assault group` in an infantry assault. It is rare by vanilla's design: the brigade HQ must be within `maxchargeradius` (50 m shipped) of the regiment's visible `enemyinrangereg[0]`. Charge demotion only reaches it once BC7 registers the BC5b producers.
  - A W&L brigade career shows: player units never in `[OrderArbiter]` issued counts; a dispatch panel naming the superior when the parent division is moved; `[once:tactical-battle-core:ancestry-mismatch] [BattleSnapshot] parent map and Transform.IsChildOf disagree on the player's chain: alliance=<a> groups=<n>` absent, or explained.
  - BC3 PROVE-MODE check: in a W&L battle with the player's command detached, the AI parent is ordered and rotated (RotateInPlace) and no player-owned regiment receives a Whiskey write (no movement, rotation, stance or formation change on the player's regiments); then, with the AI parent holding a stance (`ai_stanceordered > 0`; `ChangeStance` then sets the parent's own `permanentlydetached`, 90788-90791, which must not shield the re-attached player command below it), press the player's stance-off (re-attach) and confirm the parent is no longer ordered as a group (fan-out, or `TacticalPlayerProtected reason=owned-descendant-attached`).
- [ ] Docs:
  - `docs/patch-catalog.md`: remove #41, #46, #45, #48, #49, #52, #61, veto; add the arbiter; note #59/#56/#42 as executor-inline.
  - `docs/tactical-orchestrator.md` and `docs/tactical-operations-ledger.md`: battle core section, D3/D4/D8.
  - `docs/vanilla-vision-and-movement.md`: R1/R2/R4/R5 status → implemented; z-bounds correction.
  - `docs/wl-player-model.md`: "Rules for Whiskey code" 1-2 now enforced by `PlayerOwnership`/chain planner.
  - `docs/telemetry.md`: new scopes and rows.
  - `docs/bug-fixes/`: `MicroAICheckForCharges` dead compare.
  - `AGENTS.md`: writer list → "battle writes only through OrderExecutor"; soften the `transform.parent` rule to the D5 statement with anchors.
  - `docs/handoff.md`; `MEMORY.md`.
- [ ] Build, deploy and hash-verify per AGENTS.md (BepInEx 5.4.23.5 was reinstalled 2026-10-15 with the user's authorization; deploying the built DLL is authorized), then hand the smoke checklist to the user.
