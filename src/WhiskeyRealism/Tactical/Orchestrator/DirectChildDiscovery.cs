using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Discovery for army-direct-child command units. Pure half: takes a
    /// structured RegimentProbe[] and returns DirectChildSnapshot[]. The
    /// vanilla-touching wrapper lives in DirectChildDiscoveryRuntime.cs.
    /// </summary>
    public static partial class DirectChildDiscovery
    {
        public readonly struct RegimentProbe
        {
            public RegimentProbe(int instanceId, int unittyp, string name, bool active, int parentInstanceId, bool isDirectChild)
            {
                InstanceId = instanceId;
                UnitTyp = unittyp;
                Name = name ?? string.Empty;
                Active = active;
                ParentInstanceId = parentInstanceId;
                IsDirectChild = isDirectChild;
            }

            public int InstanceId { get; }
            public int UnitTyp { get; }
            public string Name { get; }
            public bool Active { get; }
            public int ParentInstanceId { get; }
            /// <summary>True when this regiment is a direct child of *some* command-level parent in the input set (precomputed by the runtime wrapper).</summary>
            public bool IsDirectChild { get; }
        }

        public static IReadOnlyList<DirectChildSnapshot> Probe(IReadOnlyList<RegimentProbe> probes, int commandHierarchyShift)
        {
            if (probes == null || probes.Count == 0) return Array.Empty<DirectChildSnapshot>();
            int effectiveCommandMin = ClampShiftedMin(commandHierarchyShift);

            var armyRoots = new List<RegimentProbe>();
            for (int i = 0; i < probes.Count; i++)
            {
                var p = probes[i];
                if (!p.Active) continue;
                if (p.UnitTyp < effectiveCommandMin) continue;
                armyRoots.Add(p);
            }

            int maxUnittyp = -1;
            for (int i = 0; i < armyRoots.Count; i++)
                if (armyRoots[i].UnitTyp > maxUnittyp) maxUnittyp = armyRoots[i].UnitTyp;

            var result = new List<DirectChildSnapshot>();
            for (int a = 0; a < armyRoots.Count; a++)
            {
                if (armyRoots[a].UnitTyp != maxUnittyp) continue;
                var armyRoot = armyRoots[a];
                int childCount = 0;
                for (int c = 0; c < probes.Count; c++)
                {
                    var p = probes[c];
                    if (!p.Active) continue;
                    if (!p.IsDirectChild) continue;
                    if (p.ParentInstanceId != armyRoot.InstanceId) continue;
                    if (p.UnitTyp < effectiveCommandMin) continue;
                    if (p.UnitTyp >= armyRoot.UnitTyp) continue; // do not register the army root as its own child
                    result.Add(new DirectChildSnapshot(
                        childId: "child-" + p.InstanceId,
                        parentArmyId: "army-" + armyRoot.InstanceId,
                        rawUnitTyp: p.UnitTyp,
                        commandHierarchyShift: commandHierarchyShift,
                        displayName: p.Name,
                        active: true));
                    childCount++;
                }
                if (childCount == 0)
                {
                    result.Add(new DirectChildSnapshot(
                        childId: "synth-army-" + armyRoot.InstanceId,
                        parentArmyId: "army-" + armyRoot.InstanceId,
                        rawUnitTyp: armyRoot.UnitTyp,
                        commandHierarchyShift: commandHierarchyShift,
                        displayName: armyRoot.Name,
                        active: true));
                }
            }
            return result;
        }

        private static int ClampShiftedMin(int shift)
        {
            int min = TacticalUnitType.MaxCombat + 1 + shift;
            if (min < 1) min = 1;
            if (min > 18) min = 18;
            return min;
        }
    }
}
