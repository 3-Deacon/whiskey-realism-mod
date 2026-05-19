# Hierarchy-Depth-Agnostic Role Cascade — Implementation Plan

> **Status:** Active. Use `superpowers:executing-plans` (inline) for execution.

**Goal:** Translate Scourge of War's per-tier `Think` dispatch into a depth-agnostic role-cascade for Whiskey, then beat SoW on five axes: depth-agnostic, geometry-aware sibling distribution, strength-weighted role placement, personality-modulated cascade, single-pass leaf-mapping with full cascade-chain telemetry.

**Why this exists:** Live battle telemetry showed Union AI (top = `3rd_Division`, with nested `5th_Division`, `2nd_Division`) never moving — `DirectChildDiscovery.Probe` stops at one level under the army root, so brigades nested inside Union divisions never get posture decisions. CSA top is `Army_of_the_Potomac` (flat army→brigade), works correctly. SoW solves this with rigid `CUnitDivThink → SubBrigade iteration` per-tier dispatch. We can do better.

---

## SoW reference (from `offai.cpp`)

- `CUnitSideThink` — minimal (officer scared, rally)
- `CUnitArmyThink` — picks `ArmyPlay` from grand-tactics table, assigns rectangles to corps via `RunArmyPlay(corps, rect)`
- `CUnitCorpThink` — minimal (ReEvalTactics + OffScared)
- `CUnitDivThink` — per-brigade courier orders via `SendOrdersByCour`; picks anchor + L/R play slots
- `CUnitBrigThink` — autonomous brigade behavior (`offai.cpp:507-546` brigade-to-objective march)

**Pattern:** Engine dispatches per-tier `Think` based on `Rank`. Each tier iterates `NumSubs()` and acts on direct children only. Responsibility cascades via separate dispatch calls per tick.

**Limits SoW design has:**
1. Hierarchy must fit exactly 5 tiers (Side/Army/Corps/Div/Brig)
2. Mid-tier skipping (e.g., Army → Brigade direct, no Corps/Div) breaks the dispatch model
3. Distribution from static `PLAY*` rectangles → not adaptive to live positions or terrain
4. Per-tier dispatch is multi-tick (army-think one tick, div-think later) → coordination lag
5. No personality-driven distribution
6. No telemetry chain to debug

## Whiskey improvements

1. **Depth-agnostic recursive cascade** — single algorithm handles 1-tier (flat) through N-tier; auto-adapts to GTCW's `commandhierarchyshift`-driven variable depth
2. **Single-pass leaf mapping** — top-tier role → leaf brigade roles computed in one pass; no multi-tick coordination lag
3. **Geometry-aware sibling distribution** — sort siblings by world position, assign Main to center child, SupportMain to adjacent, Refuse/Reserve to outer; far better than SoW's fixed rect table
4. **Strength-weighted within geometry** — strongest brigade among center candidates gets Main; weakest siblings get Reserve
5. **Personality-modulated cascade** — high-aggression commander (Hood/Jackson) → more brigades inherit attack roles, fewer Reserve; cautious commander (McClellan) → more Reserve/Screen, fewer attack
6. **Full cascade-chain telemetry** — every leaf brigade emits `(rootRole → midTierRole → leafRole)` so we can diagnose why any specific brigade got its task

---

## File structure

| File | Action | Responsibility |
|---|---|---|
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalRoleCascade.cs` | **Create** | Pure: parent role + geometry + strength + personality → child role |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalCommandTreeProbe.cs` | **Create** | Pure: expand `RegimentProbe[]` into a full nested tree structure with parent/child links |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalLeafBrigadeMap.cs` | **Create** | Pure: recursive cascade from top role assignments down to leaf brigades; returns `Dictionary<int, LeafBrigadeAssignment>` keyed by brigade instance ID |
| `src/WhiskeyRealism/Tactical/Orchestrator/DirectChildDiscoveryRuntime.cs` | **Modify** | Emit nested probes for grandchildren+; expose helper to walk `Regiment.allattachedunits` recursively |
| `src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs` | **Modify** | New `GetLeafBrigadeAssignment(instanceId)` method backed by `TacticalLeafBrigadeMap` |
| `src/WhiskeyRealism/Patches/BattleCommandPostureExecutorPatch.cs` | **Modify** | Second iteration pass over `BattleUnits.completeunitlist` for leaf brigades not in `unitsused`; uses leaf map for cascaded role/task |
| `src/WhiskeyRealism/Telemetry/TelemetryTagPolicy.cs` | **Modify** | Register new `TacticalLeafCascade` event |
| `tests/WhiskeyRealism.Tests/Program.cs` | **Modify** | ~10 new tests covering cascade rules + leaf map + tier-depth scenarios |
| `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` | **Modify** | Add new pure files to compile list |

---

## Task 1 — TacticalRoleCascade (pure logic)

**File:** `src/WhiskeyRealism/Tactical/Orchestrator/TacticalRoleCascade.cs`

- [ ] Define `TacticalCascadeContext` struct: `parentRole`, `childIndex`, `childCount`, `childPositionFromCenter01`, `childStrengthBucket`, `childFlankExposureBucket`, `commanderAggression01`
- [ ] Define `DistributeChildRole(TacticalCascadeContext) → DirectChildRole`
- [ ] Rules per parent role:
  - `Main`: center child → Main; adjacent → SupportMain; outer → RefuseLeft/Right by position. Aggressive commander (aggression>0.7) widens the Main+SupportMain band to include 1 more sibling each side.
  - `SupportMain`: strongest child → SupportMain; weak → Reserve. Aggressive widens.
  - `Fix`: all → Fix.
  - `Screen`: center → Screen; outer → Probe (forward scouts).
  - `RefuseLeft/Right`: all inherit; outermost → GuardFlank-equivalent (anchor); inner → Reserve echelon.
  - `Reserve`: all → Reserve.
  - `Fallback`: all → Fallback.
  - `Unknown`: all → Unknown.
- [ ] Write 8 unit tests (one per parent role) covering distribution
- [ ] Verify 1125 baseline + 8 new = 1133

## Task 2 — TacticalCommandTreeProbe (pure tree builder)

**File:** `src/WhiskeyRealism/Tactical/Orchestrator/TacticalCommandTreeProbe.cs`

- [ ] Define `CommandTreeProbeNode` struct: instanceId, unittyp, parentInstanceId, displayName, active, **plus** new fields: `childInstanceIds[]`, `worldX`, `worldZ`, `strengthBucket`
- [ ] Static method `BuildTree(IReadOnlyList<RegimentProbe>, int commandHierarchyShift) → IReadOnlyDictionary<int, CommandTreeProbeNode>` — returns a map of instanceId → node, linked
- [ ] Static method `EnumerateLeaves(rootInstanceId, IReadOnlyDictionary<int, CommandTreeProbeNode>) → IReadOnlyList<CommandTreeProbeNode>` — walks down to leaf-tier (raw unittyp == 14 = brigade)
- [ ] Static method `EnumerateChildren(parentInstanceId, IReadOnlyDictionary<int, CommandTreeProbeNode>) → IReadOnlyList<CommandTreeProbeNode>` — direct children only
- [ ] 4 unit tests: flat tree, 2-tier (army→brigades), 3-tier (army→divs→brigades), null-resilience
- [ ] Add to test csproj

## Task 3 — TacticalLeafBrigadeMap (recursive cascade)

**File:** `src/WhiskeyRealism/Tactical/Orchestrator/TacticalLeafBrigadeMap.cs`

- [ ] Define `LeafBrigadeAssignment` struct: instanceId, role, derived task, cascade chain (List<DirectChildRole>), parentChain (List<string>)
- [ ] Static method `BuildMap(rootInstanceId, IReadOnlyDictionary<int, CommandTreeProbeNode> tree, IReadOnlyList<DirectChildIntent> topLevelAssignments, float commanderAggression01)` → `IReadOnlyDictionary<int, LeafBrigadeAssignment>`
- [ ] Algorithm:
  1. Build childInstanceId → DirectChildRole map from topLevelAssignments
  2. For each top-level child, recurse: if child is leaf (unittyp 14), record assignment; if non-leaf (15+), distribute via TacticalRoleCascade per immediate child, then recurse with cascaded role as new parentRole
  3. Sort siblings by worldX (left-to-right) before distributing so geometry-awareness works
  4. Map role → CommandTaskType (Main → AttackObjective, SupportMain → SupportAttack, Fix → FixEnemy, Screen → Screen, RefuseLeft/Right → GuardFlank, Reserve → ReserveWait, Fallback → FallBackToLine)
- [ ] 6 unit tests:
  - Flat army with 4 brigades, top role=Main → center 2 brigades Main+SupportMain, outer 2 Refuse
  - 3-tier hierarchy (army → 2 divisions → 2 brigades each), top role=Main on one division → that division's brigades cascade to Main/Support
  - All Reserve cascades to all leaves Reserve
  - Aggressive commander widens Main+Support band
  - Cautious commander widens Reserve band
  - Empty tree returns empty map (null-safe)

## Task 4 — Runtime tree probe + ArmyOrchestrator wiring

**Files:**
- `src/WhiskeyRealism/Tactical/Orchestrator/DirectChildDiscoveryRuntime.cs` (modify)
- `src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs` (modify)

- [ ] Add helper `BuildFullProbeList(IList<Regiment>, int commandHierarchyShift)` that captures ALL active groups under the alliance, not just direct children. Each Regiment also captures worldX/Z from `transform.position`.
- [ ] Extend `ArmyOrchestrator`:
  - Cache the leaf brigade map after each registration / replan
  - New public `GetLeafAssignment(int instanceId)` returning `LeafBrigadeAssignment?` for that brigade
  - Rebuild map when `RegisterDirectChildrenIfChanged` produces a different snapshot signature

## Task 5 — Posture executor walks leaf brigades

**File:** `src/WhiskeyRealism/Patches/BattleCommandPostureExecutorPatch.cs`

- [ ] In `Apply(AIBattle, bool)`, after the existing `bunits.unitsused` iteration:
  - Get `ArmyOrchestrator` for the side
  - Walk `BattleUnits.completeunitlist` filtered to brigade-tier (unittyp == 14), alliance matches, active in hierarchy
  - For each brigade NOT already iterated in step 1, look up its leaf assignment from the orchestrator
  - If assignment exists, synthesize `CommandNodeOperationalState` from the assignment's role+task and call `TryApplyGroup`-equivalent (split into a method `TryApplyLeafBrigade` that doesn't require ledger registration)
- [ ] Telemetry: emit `TacticalLeafCascade` per write with cascade chain (rootRole → midTierRole → leafRole)

## Task 6 — TelemetryTagPolicy registration

**File:** `src/WhiskeyRealism/Telemetry/TelemetryTagPolicy.cs`

- [ ] Add `AddTactical("TacticalLeafCascade", TelemetryCategory.Decision);`

## Task 7 — Build, deploy, hash-verify, doc

- [ ] `./build.sh` → 0 warnings, 0 errors
- [ ] Test harness → 1125 baseline + ~25 new = ~1150 PASS, 0 FAIL
- [ ] Deploy to `<GTCW>/BepInEx/plugins/`, verify dist/ and deployed match by SHA-256
- [ ] Update `docs/handoff.md` "What just shipped" entry
- [ ] Update `docs/scourge-of-war-ai-anchors.md` to record the cascade improvement
- [ ] Update `docs/tactical-orchestrator.md` with cascade architecture section
- [ ] Commit each task incrementally with co-author trailer

## Smoke expectations

After deploy, fresh GTCW launch + battle:
- Union side `3rd_Division` (top) should produce `TacticalLeafCascade` rows for nested brigades inside `5th_Division` and `2nd_Division`
- Brigades nested inside Union divisions should appear as `unit` in `TacticalCommandPosture` events with `SetFormationAndWaypoint` decisions, reasons like `attack-objective`, `support-attack`, `guard-flank`, etc.
- Union side actually attacks/moves like the CSA side did
- No `Exception` / `ERROR` / Harmony failure
- No spam: leaf cascade telemetry should be bounded by signature dedup
- Cascade chain in telemetry shows e.g. `chain=[Main, Main, Main]` for the strongest brigade under the strongest division under the Main-axis top

## Rollback boundary

If smoke shows pathfinding chaos or unintended player-subordinate writes:
- Set new config `Enable Tactical Nested Brigade Cascade = false` (rollback flag, will add)
- Or `Tactical Commander Mode = Off` for full rollback
