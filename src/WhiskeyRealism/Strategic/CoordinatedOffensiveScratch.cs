using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WhiskeyRealism.Strategic
{
    // Per-call indexes for #38 (CoordinatedOffensiveOperationsPatch). Vanilla calls
    // CheckOffensiveMovements once per UpdateUnitAI pass (AICampaign 11319-11321), up to
    // 7 passes a frame at 50x; #38 used to scan ownunits once per formation assignment
    // and the operation lists three times per assignment. These indexes are built once
    // per call, on the main thread, and cleared before the call returns.

    // Membership with exactly the result of IList.Contains(probe) on a vanilla
    // List<Regiment>, for any non-null probe. List<Regiment>.Contains compares through
    // UnityEngine.Object.Equals -> CompareBaseObjects, which in this game's
    // UnityEngine.CoreModule (Unity 2021.3) returns (object)lhs == rhs when both sides
    // are non-null references, destroyed objects included; a C# null element never
    // matches a non-null probe. So: reference identity over every non-null element.
    internal sealed class ReferenceMembership
    {
        private readonly HashSet<object> _items = new HashSet<object>(ReferenceIdentityComparer.Instance);

        internal void Fill(IList list)
        {
            _items.Clear();
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                object element = list[i];
                if (element != null) _items.Add(element);
            }
        }

        internal bool Contains(object probe)
        {
            return probe != null && _items.Contains(probe);
        }

        internal void Clear()
        {
            _items.Clear();
        }
    }

    internal sealed class ReferenceIdentityComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceIdentityComparer Instance = new ReferenceIdentityComparer();

        public new bool Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }

    // id -> unit with the result of CoordinatedOperationRuntime.FindUnitById(ownUnits, id)
    // for every id: non-Regiment and Unity-null (destroyed) elements are skipped, id 0
    // never matches, the first matching element wins.
    internal sealed class RegimentIdIndex
    {
        private readonly Dictionary<int, Regiment> _byId = new Dictionary<int, Regiment>();

        internal void Index(IList ownUnits)
        {
            _byId.Clear();
            if (ownUnits == null) return;
            for (int i = 0; i < ownUnits.Count; i++)
            {
                var unit = ownUnits[i] as Regiment;
                if (unit == null) continue;
                int id = CoordinatedOperationRuntime.InstanceIdOf(unit);
                if (!_byId.ContainsKey(id)) _byId.Add(id, unit);
            }
        }

        internal Regiment FindUnitById(int stableUnitId)
        {
            if (stableUnitId == 0) return null;
            return _byId.TryGetValue(stableUnitId, out var unit) ? unit : null;
        }

        internal void Clear()
        {
            _byId.Clear();
        }
    }

    // The two per-unit checks the candidate loop asks of vanilla and the W&L bridge.
    internal interface ICoordinatedCandidateGate
    {
        bool IsAvailable(Regiment unit);
        CoordinatedCommitMode CommitMode(Regiment unit);
    }

    internal sealed class CoordinatedOffensiveScratch
    {
        internal readonly RegimentIdIndex Units = new RegimentIdIndex();
        internal readonly ReferenceMembership Offensive = new ReferenceMembership();
        internal readonly ReferenceMembership Defensive = new ReferenceMembership();
        internal readonly ReferenceMembership Depots = new ReferenceMembership();
        internal readonly List<CoordinatedOperationCandidate> Candidates = new List<CoordinatedOperationCandidate>();
        private readonly List<CoordinatedOperationCandidate> _pool = new List<CoordinatedOperationCandidate>();

        // One candidate per assignment whose unit is in ownunits and passes the gate,
        // in assignment order. Units must be indexed and the three operation lists
        // filled first.
        internal void AssembleCandidates(IReadOnlyList<FormationDirectiveAssignment> assignments, ICoordinatedCandidateGate gate)
        {
            Candidates.Clear();
            for (int i = 0; i < assignments.Count; i++)
            {
                var assignment = assignments[i];
                var unit = Units.FindUnitById(assignment.StableUnitId);
                if (unit == null) continue;
                if (!gate.IsAvailable(unit)) continue;
                var commitMode = gate.CommitMode(unit);

                if (Candidates.Count == _pool.Count) _pool.Add(new CoordinatedOperationCandidate());
                Candidates.Add(CoordinatedOperationRuntime.FillCandidateFromAssignment(
                    _pool[Candidates.Count],
                    assignment,
                    Offensive.Contains(unit),
                    Defensive.Contains(unit),
                    Depots.Contains(unit),
                    commitMode));
            }
        }

        // Drops every Regiment reference; pooled candidates hold only values and strings.
        internal void Clear()
        {
            Units.Clear();
            Offensive.Clear();
            Defensive.Clear();
            Depots.Clear();
            Candidates.Clear();
        }
    }
}
