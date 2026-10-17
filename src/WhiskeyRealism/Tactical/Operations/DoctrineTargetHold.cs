using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Operations
{
    /// <summary>
    /// A node's assigned points (primary, support, fallback) stand while the fresh ones stay within <see cref="HoldMeters"/> of them. The battle line
    /// planner derives each point from the direction node to objective (so it turns as the node walks) and from the perceived enemy line (which jitters with
    /// the fog), and the assignment is rebuilt every ledger tick; the point of a brigade whose role was stable moved 25 to 100 m a tick (smoke 2026-10-17:
    /// 1146,-874 to 1087,-860 to 1065,-849 to 1104,-863 to 1050,-838 to 1138,-866 to 1042,-815), and every new point is a new order. A point moves when
    /// the line, the rect or the contact it belongs to has really moved (the fresh one is beyond the threshold), and when the node's task or objective
    /// changes. One instance per side's operations ledger; keyed by node id; a node absent from a build is forgotten (<see cref="EndBuild"/>).
    /// </summary>
    public sealed class DoctrineTargetHold
    {
        /// <summary>The distance past which a fresh point replaces the held one: a tick's drift of the line is smaller, a real move of the line is not.</summary>
        public const float HoldMeters = 125f;

        private struct Entry
        {
            public CommandTaskType Task;
            public string ObjectiveId;
            public DoctrineTargetPoint Primary, Support, Fallback;
            public int Build;
        }

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly List<string> _stale = new List<string>();
        private int _build;

        public void Clear() => _entries.Clear();

        /// <summary>Starts a build: the nodes <see cref="Apply"/> sees until <see cref="EndBuild"/> are the live ones.</summary>
        public void BeginBuild() => _build++;

        /// <summary>Forgets the nodes the build did not mention (a routed or re-organised node starts over if it returns).</summary>
        public void EndBuild()
        {
            _stale.Clear();
            foreach (var pair in _entries)
                if (pair.Value.Build != _build) _stale.Add(pair.Key);
            for (int i = 0; i < _stale.Count; i++) _entries.Remove(_stale[i]);
        }

        /// <summary>The line plan with each of its points replaced by the held one while the fresh one is inside the threshold.</summary>
        public TacticalBattleLinePlan Apply(string nodeId, CommandTaskType task, string objectiveId, TacticalBattleLinePlan fresh)
        {
            if (nodeId == null) return fresh;
            bool same = _entries.TryGetValue(nodeId, out Entry held) && held.Task == task && string.Equals(held.ObjectiveId, objectiveId, StringComparison.Ordinal);
            DoctrineTargetPoint primary = same ? Hold(held.Primary, fresh.PrimaryTarget) : fresh.PrimaryTarget;
            DoctrineTargetPoint support = same ? Hold(held.Support, fresh.SupportTarget) : fresh.SupportTarget;
            DoctrineTargetPoint fallback = same ? Hold(held.Fallback, fresh.FallbackTarget) : fresh.FallbackTarget;
            _entries[nodeId] = new Entry { Task = task, ObjectiveId = objectiveId, Primary = primary, Support = support, Fallback = fallback, Build = _build };
            return new TacticalBattleLinePlan(primary, support, fallback, fresh.ArtilleryLineTarget, fresh.EchelonDepth, fresh.Reason);
        }

        private static DoctrineTargetPoint Hold(DoctrineTargetPoint held, DoctrineTargetPoint fresh)
        {
            if (!held.HasValue || !fresh.HasValue) return fresh;
            float dx = fresh.X - held.X, dz = fresh.Z - held.Z;
            return dx * dx + dz * dz < HoldMeters * HoldMeters ? held : fresh;
        }
    }
}
