# Tactical Orchestrator Slice 0 Command-Node Tree Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a read-only dynamic command-node tree beneath each side's tactical orchestrator so later behavior patches can resolve corps, divisions, brigades, and other active command groups without hard-coding unittyp names or collapsing everything into the current O3 direct-child layer.

**Architecture:** Keep the existing O3 direct-child orchestrator as the compatibility layer and default role source. Add pure command-tree contracts, a deterministic tree builder, an intent allocator/resolver that maps O3 roles onto deeper command nodes, and runtime telemetry that snapshots the live vanilla command hierarchy without writing vanilla battle state.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4.x, HarmonyX, Grand Tactician vanilla `Regiment`/`BattleUnits` runtime anchors, console harness tests in `tests/WhiskeyRealism.Tests`.

---

## Source Anchors

- Existing direct-child contracts: `src/WhiskeyRealism/Tactical/Orchestrator/DirectChildContracts.cs`
- Existing pure discovery fallback: `src/WhiskeyRealism/Tactical/Orchestrator/DirectChildDiscovery.cs`
- Existing runtime direct-child attachment: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs`
- Existing army state holder: `src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs`
- Test harness registration: `tests/WhiskeyRealism.Tests/Program.cs`
- Test project explicit compile list: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Slice spec: `docs/superpowers/specs/2026-05-09-tactical-orchestrator-remaining-patches-design.md`

## Non-Goals

- Do not add behavior-writing Harmony patches in Slice 0.
- Do not retask player-side subordinate groups.
- Do not remove or replace O3 direct-child role allocation.
- Do not patch vanilla command hierarchy methods.
- Do not require a fixed corps/division/brigade unittyp mapping.

## Implementation Tasks

### 1. Add Pure Command-Tree Contracts

- [ ] Create `src/WhiskeyRealism/Tactical/Orchestrator/CommandNodeContracts.cs`.
- [ ] Add immutable command-node snapshot, command-tree snapshot, command-node intent, and intent-resolution result types.
- [ ] Keep ids string-compatible with O3:
  - Actual command node id: `node-<regimentInstanceId>`
  - Synthetic side root id: `synth-root-<allianceId>`
  - O3 compatibility id for direct children: `child-<regimentInstanceId>`
- [ ] Reuse `DirectChildRole` and `DirectChildAxis` rather than adding a second tactical role vocabulary.

Use this shape:

```csharp
using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator;

internal readonly struct CommandNodeSnapshot
{
    public CommandNodeSnapshot(
        string nodeId,
        string parentNodeId,
        int instanceId,
        int parentInstanceId,
        int allianceId,
        int rawUnitTyp,
        int commandHierarchyShift,
        string displayName,
        bool active,
        bool synthetic,
        int depth)
    {
        NodeId = string.IsNullOrWhiteSpace(nodeId) ? "node-unknown" : nodeId;
        ParentNodeId = string.IsNullOrWhiteSpace(parentNodeId) ? string.Empty : parentNodeId;
        InstanceId = instanceId;
        ParentInstanceId = parentInstanceId;
        AllianceId = allianceId;
        RawUnitTyp = rawUnitTyp;
        CommandHierarchyShift = commandHierarchyShift;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? NodeId : displayName.Trim();
        Active = active;
        Synthetic = synthetic;
        Depth = Math.Max(0, depth);
    }

    public string NodeId { get; }
    public string ParentNodeId { get; }
    public int InstanceId { get; }
    public int ParentInstanceId { get; }
    public int AllianceId { get; }
    public int RawUnitTyp { get; }
    public int CommandHierarchyShift { get; }
    public string DisplayName { get; }
    public bool Active { get; }
    public bool Synthetic { get; }
    public int Depth { get; }
    public int EffectiveCommandLevel => RawUnitTyp - CommandHierarchyShift;
}

internal sealed class CommandTreeSnapshot
{
    public static readonly CommandTreeSnapshot Empty = new CommandTreeSnapshot(
        -1,
        string.Empty,
        Array.Empty<CommandNodeSnapshot>(),
        0,
        0,
        string.Empty);

    public CommandTreeSnapshot(
        int allianceId,
        string rootNodeId,
        IReadOnlyList<CommandNodeSnapshot> nodes,
        int maxDepth,
        int missingParentCount,
        string rawUnitTypDistribution)
    {
        AllianceId = allianceId;
        RootNodeId = rootNodeId ?? string.Empty;
        Nodes = nodes ?? Array.Empty<CommandNodeSnapshot>();
        MaxDepth = Math.Max(0, maxDepth);
        MissingParentCount = Math.Max(0, missingParentCount);
        RawUnitTypDistribution = rawUnitTypDistribution ?? string.Empty;
    }

    public int AllianceId { get; }
    public string RootNodeId { get; }
    public IReadOnlyList<CommandNodeSnapshot> Nodes { get; }
    public int MaxDepth { get; }
    public int MissingParentCount { get; }
    public string RawUnitTypDistribution { get; }
    public bool HasNodes => Nodes.Count > 0;
}

internal readonly struct CommandNodeIntent
{
    public CommandNodeIntent(
        string nodeId,
        string sourceNodeId,
        DirectChildRole role,
        DirectChildAxis axis,
        int primarySector,
        int supportPriority,
        float aggressionBias01,
        int depth)
    {
        NodeId = string.IsNullOrWhiteSpace(nodeId) ? "node-unknown" : nodeId;
        SourceNodeId = string.IsNullOrWhiteSpace(sourceNodeId) ? NodeId : sourceNodeId;
        Role = role;
        Axis = axis;
        PrimarySector = Math.Max(0, primarySector);
        SupportPriority = Math.Max(0, Math.Min(100, supportPriority));
        AggressionBias01 = float.IsNaN(aggressionBias01) || float.IsInfinity(aggressionBias01)
            ? 0f
            : Math.Max(0f, Math.Min(1f, aggressionBias01));
        Depth = Math.Max(0, depth);
    }

    public string NodeId { get; }
    public string SourceNodeId { get; }
    public DirectChildRole Role { get; }
    public DirectChildAxis Axis { get; }
    public int PrimarySector { get; }
    public int SupportPriority { get; }
    public float AggressionBias01 { get; }
    public int Depth { get; }
}

internal readonly struct CommandIntentResolution
{
    public CommandIntentResolution(bool found, CommandNodeIntent intent, string reason)
    {
        Found = found;
        Intent = intent;
        Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
    }

    public bool Found { get; }
    public CommandNodeIntent Intent { get; }
    public string Reason { get; }
}
```

### 2. Build a Deterministic Pure Command Tree

- [ ] Create `src/WhiskeyRealism/Tactical/Orchestrator/CommandTreeBuilder.cs`.
- [ ] Add `CommandTreeBuilder.CommandProbe` for runtime and harness input.
- [ ] Treat active non-combat command groups as candidates: same threshold as O3, `rawUnitTyp >= TacticalUnitType.MaxCombat + 1 + commandHierarchyShift`, clamped to `1..18`.
- [ ] Build parent-child edges only when the parent is also a candidate and `parent.RawUnitTyp > child.RawUnitTyp`.
- [ ] Count missing command parents when a command node reports a parent id that is absent from the candidate set.
- [ ] Use the highest raw unittyp command as the root when there is one top-level root.
- [ ] Use `synth-root-<allianceId>` when there are zero candidates or multiple top-level roots.
- [ ] Preserve deterministic ordering by `RawUnitTyp` descending, then `InstanceId` ascending.

Core methods:

```csharp
internal static class CommandTreeBuilder
{
    internal readonly struct CommandProbe
    {
        public CommandProbe(
            int instanceId,
            int parentInstanceId,
            int allianceId,
            int rawUnitTyp,
            string displayName,
            bool active,
            bool routed,
            bool markedForRout)
        {
            InstanceId = instanceId;
            ParentInstanceId = parentInstanceId;
            AllianceId = allianceId;
            RawUnitTyp = rawUnitTyp;
            DisplayName = displayName ?? string.Empty;
            Active = active;
            Routed = routed;
            MarkedForRout = markedForRout;
        }

        public int InstanceId { get; }
        public int ParentInstanceId { get; }
        public int AllianceId { get; }
        public int RawUnitTyp { get; }
        public string DisplayName { get; }
        public bool Active { get; }
        public bool Routed { get; }
        public bool MarkedForRout { get; }
    }

    public static CommandTreeSnapshot Build(
        IReadOnlyList<CommandProbe> probes,
        int allianceId,
        int commandHierarchyShift)
    {
        var threshold = ClampShiftedMin(commandHierarchyShift);
        var candidates = FilterCandidates(probes, allianceId, threshold);
        if (candidates.Count == 0)
        {
            var synthetic = new CommandNodeSnapshot(
                $"synth-root-{allianceId}",
                string.Empty,
                -1,
                -1,
                allianceId,
                threshold,
                commandHierarchyShift,
                $"Synthetic side root {allianceId}",
                true,
                true,
                0);

            return new CommandTreeSnapshot(
                allianceId,
                synthetic.NodeId,
                new[] { synthetic },
                0,
                0,
                BuildDistribution(new[] { synthetic }));
        }

        var byInstance = new Dictionary<int, CommandProbe>();
        foreach (var candidate in candidates)
        {
            byInstance[candidate.InstanceId] = candidate;
        }

        var childrenByParent = new Dictionary<int, List<CommandProbe>>();
        var topRoots = new List<CommandProbe>();
        var missingParents = 0;

        foreach (var candidate in candidates)
        {
            if (candidate.ParentInstanceId <= 0 || !byInstance.TryGetValue(candidate.ParentInstanceId, out var parent))
            {
                if (candidate.ParentInstanceId > 0)
                {
                    missingParents++;
                }

                topRoots.Add(candidate);
                continue;
            }

            if (parent.RawUnitTyp <= candidate.RawUnitTyp)
            {
                topRoots.Add(candidate);
                continue;
            }

            if (!childrenByParent.TryGetValue(parent.InstanceId, out var children))
            {
                children = new List<CommandProbe>();
                childrenByParent[parent.InstanceId] = children;
            }

            children.Add(candidate);
        }

        Sort(topRoots);
        foreach (var children in childrenByParent.Values)
        {
            Sort(children);
        }

        return BuildSnapshot(allianceId, commandHierarchyShift, topRoots, childrenByParent, missingParents);
    }

    private static int ClampShiftedMin(int commandHierarchyShift)
    {
        var shifted = TacticalUnitType.MaxCombat + 1 + commandHierarchyShift;
        if (shifted < 1) return 1;
        if (shifted > 18) return 18;
        return shifted;
    }
}
```

Implementation detail: `BuildSnapshot` must create one synthetic root when `topRoots.Count != 1`, walk breadth-first from the root, assign depth, and populate `RawUnitTypDistribution` as a comma-separated sorted string such as `14:3,15:2,17:1`.

### 3. Add Intent Allocation and Resolver

- [ ] Create `src/WhiskeyRealism/Tactical/Orchestrator/CommandTreeIntentAllocator.cs`.
- [ ] Create `src/WhiskeyRealism/Tactical/Orchestrator/CommandIntentResolver.cs`.
- [ ] Allocate command-node intent from O3 direct-child intent first.
- [ ] For deeper command nodes, inherit the nearest ancestor intent.
- [ ] For root-only or missing O3 intent, assign a bounded reserve intent.
- [ ] Return a `CommandIntentResolution` instead of throwing when a node is unknown.

Allocator behavior:

```csharp
internal static class CommandTreeIntentAllocator
{
    public static IReadOnlyList<CommandNodeIntent> Allocate(
        CommandTreeSnapshot tree,
        IReadOnlyList<DirectChildIntent> directChildIntents)
    {
        if (tree == null || !tree.HasNodes)
        {
            return Array.Empty<CommandNodeIntent>();
        }

        var o3ByNodeId = new Dictionary<string, DirectChildIntent>(StringComparer.Ordinal);
        if (directChildIntents != null)
        {
            foreach (var directIntent in directChildIntents)
            {
                var nodeId = DirectChildToNodeId(directIntent.ChildId);
                if (!string.IsNullOrEmpty(nodeId))
                {
                    o3ByNodeId[nodeId] = directIntent;
                }
            }
        }

        var parentByNode = BuildParentMap(tree.Nodes);
        var allocated = new Dictionary<string, CommandNodeIntent>(StringComparer.Ordinal);
        var result = new List<CommandNodeIntent>(tree.Nodes.Count);

        foreach (var node in tree.Nodes)
        {
            var intent = AllocateForNode(node, o3ByNodeId, parentByNode, allocated);
            allocated[node.NodeId] = intent;
            result.Add(intent);
        }

        return result;
    }

    private static string DirectChildToNodeId(string childId)
    {
        const string prefix = "child-";
        if (string.IsNullOrWhiteSpace(childId) || !childId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return "node-" + childId.Substring(prefix.Length);
    }
}
```

Resolver behavior:

```csharp
internal static class CommandIntentResolver
{
    public static CommandIntentResolution ResolveForInstance(
        int instanceId,
        IReadOnlyList<CommandNodeIntent> intents)
    {
        if (instanceId <= 0 || intents == null || intents.Count == 0)
        {
            return new CommandIntentResolution(false, default, "no-command-intent");
        }

        var nodeId = $"node-{instanceId}";
        for (var i = 0; i < intents.Count; i++)
        {
            if (string.Equals(intents[i].NodeId, nodeId, StringComparison.Ordinal))
            {
                return new CommandIntentResolution(true, intents[i], "exact-command-node");
            }
        }

        return new CommandIntentResolution(false, default, "command-node-not-found");
    }
}
```

### 4. Integrate Command Tree State into ArmyOrchestrator

- [ ] Edit `src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs`.
- [ ] Add current command-tree and command-node-intent storage.
- [ ] Register the tree independently from direct-child snapshots.
- [ ] Recompute command-node intents when direct-child intents are refreshed.
- [ ] Expose a read-only resolver method for future behavior patches.
- [ ] Preserve existing `GetDirectChildRole` and `CurrentDirectChildIntents` behavior exactly.

Add members:

```csharp
private CommandTreeSnapshot _commandTree = CommandTreeSnapshot.Empty;
private IReadOnlyList<CommandNodeIntent> _commandNodeIntents = Array.Empty<CommandNodeIntent>();

public CommandTreeSnapshot CurrentCommandTree => _commandTree;

public IReadOnlyList<CommandNodeIntent> CurrentCommandNodeIntents => _commandNodeIntents;

public void RegisterCommandTree(CommandTreeSnapshot tree)
{
    _commandTree = tree ?? CommandTreeSnapshot.Empty;
    _commandNodeIntents = CommandTreeIntentAllocator.Allocate(_commandTree, _directChildIntents);
}

public CommandIntentResolution ResolveCommandIntentForGroup(int regimentInstanceId)
{
    return CommandIntentResolver.ResolveForInstance(regimentInstanceId, _commandNodeIntents);
}
```

At the end of `ObserveDirectChildEvidenceWithIntent`, after `_directChildIntents` is assigned, add:

```csharp
_commandNodeIntents = CommandTreeIntentAllocator.Allocate(_commandTree, _directChildIntents);
```

### 5. Add Runtime Command-Tree Snapshot Builder

- [ ] Create `src/WhiskeyRealism/Tactical/Orchestrator/CommandTreeRuntime.cs`.
- [ ] Keep this file out of the test project because it references vanilla runtime types.
- [ ] Build probes from `BattleUnits.completeunitlist`.
- [ ] Use vanilla parent evidence from `Regiment.parentregiment` and, when available, `Regiment.GetAttachedUnitsReg(true)` to confirm direct parent-child edges.
- [ ] Never write to `Regiment`, `macroai`, `ai_stance`, route, movement, charge, fallback, or artillery state.
- [ ] Catch reflection/runtime failures and return `CommandTreeSnapshot.Empty` plus one bounded warning log.

Runtime outline:

```csharp
internal static class CommandTreeRuntime
{
    public static CommandTreeSnapshot Snapshot(int allianceId, int commandHierarchyShift)
    {
        try
        {
            var probes = BuildProbes(allianceId);
            return CommandTreeBuilder.Build(probes, allianceId, commandHierarchyShift);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[TacticalCommandTree] snapshot failed side={allianceId}: {ex.Message}");
            return CommandTreeSnapshot.Empty;
        }
    }

    private static IReadOnlyList<CommandTreeBuilder.CommandProbe> BuildProbes(int allianceId)
    {
        var probes = new List<CommandTreeBuilder.CommandProbe>();
        var groups = BattleUnits.completeunitlist as System.Collections.IList;
        if (groups == null)
        {
            return probes;
        }

        foreach (var item in groups)
        {
            if (item is not Regiment group || group.alliance != allianceId)
            {
                continue;
            }

            var parent = ResolveParent(group);
            probes.Add(new CommandTreeBuilder.CommandProbe(
                group.gameObject != null ? group.gameObject.GetInstanceID() : 0,
                parent != null && parent.gameObject != null ? parent.gameObject.GetInstanceID() : -1,
                group.alliance,
                group.unittyp,
                group.name,
                group.gameObject != null && group.gameObject.activeInHierarchy,
                group.routed,
                group.markedforrout));
        }

        return probes;
    }
}
```

If vanilla field names differ in the current decompile, adjust only this runtime adapter and keep the pure contract untouched.

### 6. Wire Runtime Snapshot and Bounded Telemetry

- [ ] Edit `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs`.
- [ ] Add `AttachCommandTreeIfReady(TacticalBattleOrchestrator side, Battle battle)`.
- [ ] Call it after `AttachDirectChildrenIfReady(side, battle)` on battle start.
- [ ] Call it once per existing orchestrator tick before `DriveDirectChildCycle` if no tree has been registered for the side.
- [ ] Log only one telemetry line per side per battle unless root/node/depth distribution changes.
- [ ] Telemetry format:

```text
[TacticalCommandTree] side=<allianceId> root=<rootNodeId> nodes=<count> maxDepth=<depth> unittyps=<distribution> missingParents=<count>
```

Runtime method:

```csharp
private static void AttachCommandTreeIfReady(TacticalBattleOrchestrator side, Battle battle)
{
    if (side == null || side.Army == null || battle == null)
    {
        return;
    }

    var tree = CommandTreeRuntime.Snapshot(side.AllianceId, TacticalBattleCoordinator.CommandHierarchyShift);
    side.Army.RegisterCommandTree(tree);

    LogCommandTreeTelemetry(side.AllianceId, tree);
}
```

Add a static cache keyed by battle id and alliance id to suppress duplicate telemetry when the shape is unchanged.

### 7. Add Harness Coverage

- [ ] Edit `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`.
- [ ] Add explicit compile entries:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\CommandNodeContracts.cs" Link="Tactical\Orchestrator\CommandNodeContracts.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\CommandTreeBuilder.cs" Link="Tactical\Orchestrator\CommandTreeBuilder.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\CommandTreeIntentAllocator.cs" Link="Tactical\Orchestrator\CommandTreeIntentAllocator.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\CommandIntentResolver.cs" Link="Tactical\Orchestrator\CommandIntentResolver.cs" />
```

- [ ] Edit `tests/WhiskeyRealism.Tests/Program.cs`.
- [ ] Register 16 new tests near the existing O3 tactical-orchestrator tests:

```csharp
("command node contracts sanitize ids and finite aggression", TestCommandNodeContractsSanitizeInputs),
("command tree builder creates synthetic root when no command candidates exist", TestCommandTreeBuilderSyntheticRootWhenEmpty),
("command tree builder preserves single root hierarchy depth", TestCommandTreeBuilderSingleRootHierarchyDepth),
("command tree builder creates synthetic root for multiple top roots", TestCommandTreeBuilderSyntheticRootForMultipleTopRoots),
("command tree builder filters inactive routed wrong side and combat groups", TestCommandTreeBuilderFiltersInvalidGroups),
("command tree builder counts missing command parents", TestCommandTreeBuilderCountsMissingParents),
("command tree builder honors command hierarchy shift", TestCommandTreeBuilderHonorsCommandHierarchyShift),
("command tree distribution is deterministic", TestCommandTreeDistributionDeterministic),
("command intent allocator maps direct child role onto command node", TestCommandIntentAllocatorMapsDirectChildRole),
("command intent allocator inherits nearest ancestor role", TestCommandIntentAllocatorInheritsNearestAncestorRole),
("command intent allocator assigns bounded reserve for root fallback", TestCommandIntentAllocatorRootFallbackReserve),
("command intent resolver finds exact node by instance", TestCommandIntentResolverFindsExactNode),
("command intent resolver reports missing node without throwing", TestCommandIntentResolverMissingNode),
("army orchestrator registers command tree snapshot", TestArmyOrchestratorRegistersCommandTree),
("army orchestrator preserves O3 direct child role after command tree allocation", TestArmyOrchestratorPreservesDirectChildRoleWithCommandTree),
("army orchestrator resolves command node intent after direct child evidence", TestArmyOrchestratorResolvesCommandNodeIntent),
```

Key assertions:

```csharp
private static void TestArmyOrchestratorPreservesDirectChildRoleWithCommandTree()
{
    var army = NewArmyOrchestratorWithPlan();
    var tree = CommandTreeBuilder.Build(new[]
    {
        new CommandTreeBuilder.CommandProbe(100, -1, 1, 17, "Army", true, false, false),
        new CommandTreeBuilder.CommandProbe(200, 100, 1, 15, "Corps", true, false, false),
    }, 1, 0);

    army.RegisterCommandTree(tree);
    army.ObserveDirectChildEvidenceWithIntent(new[]
    {
        new DirectChildEvidence("child-200", 2, 0.65f, 0.3f, false, false, DirectChildAxis.Center),
    });

    AssertEqual(DirectChildRole.MainEffort, army.GetDirectChildRole("child-200"), "O3 direct child role should remain authoritative");
    var resolution = army.ResolveCommandIntentForGroup(200);
    AssertTrue(resolution.Found, "command resolver should find node-200");
    AssertEqual(DirectChildRole.MainEffort, resolution.Intent.Role, "command node should mirror direct child role");
}
```

### 8. Verify Local Harness and Build

- [ ] Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected result:

```text
679 PASS
0 FAIL
```

- [ ] Run:

```bash
./build.sh
```

Expected result:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### 9. Deploy and Hash-Verify DLL

- [ ] Close Grand Tactician before deployment.
- [ ] Run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected result:

- The two `stat` sizes match.
- The two SHA-256 hashes match exactly.

### 10. Smoke-Test Runtime Telemetry

- [ ] Launch Grand Tactician with BepInEx.
- [ ] Start or load a battle with both sides AI-controlled at battle start.
- [ ] Inspect:

```bash
rg -n "TacticalCommandTree|TacticalDirectChildIntent|Exception|ERROR" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected result:

- At least one side-0 line in the form `[TacticalCommandTree] side=0 root=<rootNodeId> nodes=<count> maxDepth=<depth> unittyps=<distribution> missingParents=<count>` or side-0 synthetic fallback.
- At least one side-1 line in the form `[TacticalCommandTree] side=1 root=<rootNodeId> nodes=<count> maxDepth=<depth> unittyps=<distribution> missingParents=<count>` or side-1 synthetic fallback.
- `nodes` is greater than `1` in at least one normal battle with real command hierarchy.
- `maxDepth` varies by battle shape when corps/division/brigade chains exist.
- Existing `[TacticalDirectChildIntent]` lines still appear.
- No repeated patch exceptions.
- No behavior write logs from Slice 0.

### 11. Documentation Closeout

- [ ] Update `docs/handoff.md` "What just shipped" and active workstream notes after implementation and smoke.
- [ ] Update `docs/patch-catalog.md` runtime/coordinator section with the new command-tree runtime adapter.
- [ ] Update `MEMORY.md` with a terse durable note that Slice 0 adds a read-only command-node tree and preserves O3 compatibility.
- [ ] Leave the Slice 0 plan under `docs/superpowers/plans/` until build, deploy, hash, and smoke pass.
- [ ] After smoke passes, move this plan to `docs/superpowers/plans/archive/` and update archive indexes.

## Rollback

- Remove calls to `AttachCommandTreeIfReady`.
- Keep O3 direct-child files and behavior untouched.
- If runtime parent discovery is noisy, keep the pure command-tree tests and disable only `CommandTreeRuntime.Snapshot` registration until decompile anchors are corrected.

## Acceptance Criteria

- Pure tests pass with 16 new command-tree tests.
- Build succeeds.
- Deployed DLL hash matches `dist/WhiskeyRealism.dll`.
- Runtime emits bounded `[TacticalCommandTree]` telemetry for AI sides.
- O3 direct-child roles remain identical for a root plus direct-child-only tree.
- Slice 0 performs no vanilla battle-state writes.
