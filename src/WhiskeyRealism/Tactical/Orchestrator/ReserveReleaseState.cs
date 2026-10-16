using System;
using System.Collections.Generic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // BC13 (spec docs/superpowers/specs/2026-10-15-battle-opportunity-design.md § 6, requirement-staggered-reserve-release):
    // the memory of the army's reserve releases, per side, pure. Exploit and odds releases are staggered: at most one Reserve
    // child per release interval, never below a floor of held reserves, and a released child stays released while its trigger
    // holds. Crisis releases are stateless (recomputed every allocation); the state only lists them for the row and for the
    // exploit lifecycle (a crisis release of an exploit asset's parent line aborts the exploit).
    //
    // BC14 adds the crossing bindings: a reserve released to guard a crossing the enemy can reach first stays released while the
    // threat holds (requirement-defender-crossing-release). They sit in their own table, apart from the stagger's map.
    //
    // Owner: one instance per alliance (ForAlliance), registered with BattleScope, so a battle that builds more than one army for
    // an alliance shares one state and the next battle starts clean. DirectChildAllocator.ReleaseReserves writes it during an
    // allocation (the stagger's records) and ReleaseReserveToCrossings writes the bindings; ArmyOrchestrator reads the outcomes to
    // emit the TacticalReserveRelease rows and clears it on a plan change, a new battle day and a stand-down.
    /// <summary>The direct-child id ("child-" + the regiment's instance id, which may be negative) read without allocating.</summary>
    public static class DirectChildIds
    {
        private const string Prefix = "child-";

        /// <summary>The instance id, or 0 when the text is not a child id.</summary>
        public static int InstanceId(string childId)
        {
            if (childId == null || childId.Length <= Prefix.Length) return 0;
            for (int k = 0; k < Prefix.Length; k++)
                if (childId[k] != Prefix[k]) return 0;
            int at = Prefix.Length;
            bool negative = false;
            if (childId[at] == '-')
            {
                negative = true;
                at++;
                if (at == childId.Length) return 0;
            }
            long value = 0;
            for (; at < childId.Length; at++)
            {
                char c = childId[at];
                if (c < '0' || c > '9') return 0;
                value = value * 10 + (c - '0');
                if (value > 2147483648L) return 0;
            }
            if (negative) value = -value;
            return value < int.MinValue || value > int.MaxValue ? 0 : (int)value;
        }
    }

    /// <summary>A reserve child released to guard a crossing: the link, the own shift when it was released (the threat's hold bound reads it) and the hour.</summary>
    public struct CrossingBinding
    {
        public string ChildId;
        public int Link;
        public double OwnShiftAtRelease, ReleasedAtHours;
        /// <summary>The reserves still held after this release and the floor they were held against (the release row's <c>held</c> and <c>floor</c>).</summary>
        public int Held, Floor;
    }

    public sealed class ReserveReleaseState
    {
        public const int MaxCrossingGuards = 8;

        private static readonly ReserveReleaseState[] ByAlliance = { new ReserveReleaseState(), new ReserveReleaseState() };

        static ReserveReleaseState()
        {
            BattleScope.Register("reserve-release-state", ResetAll);
        }

        /// <summary>The alliance's state (0 or 1); null for any other.</summary>
        public static ReserveReleaseState ForAlliance(int alliance) => alliance == 0 || alliance == 1 ? ByAlliance[alliance] : null;

        /// <summary>Battle end: both alliances' states start clean.</summary>
        public static void ResetAll()
        {
            for (int i = 0; i < ByAlliance.Length; i++) ByAlliance[i].Clear();
        }

        /// <summary>One release of the last allocation, for its TacticalReserveRelease row.</summary>
        public struct Outcome
        {
            public string ChildId;
            /// <summary>true: a crisis release; false: a stagger (Exploit phase or odds) release.</summary>
            public bool Crisis;
            /// <summary>Reserves still held after this release, and the floor they were measured against.</summary>
            public int Held, Floor;
            /// <summary>A crisis release that left Held below Floor (defence first).</summary>
            public bool FloorBreach;
        }

        private readonly Dictionary<string, double> _released = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly List<string> _crisis = new List<string>(4);
        private readonly List<Outcome> _outcomes = new List<Outcome>(4);

        /// <summary>R0: the Reserve children when the current trigger first held; 0 while no trigger holds.</summary>
        public int InitialReserves;

        /// <summary>The doctrine hour of the last stagger release (a crisis release does not move it); negative infinity before the first.</summary>
        public double LastReleaseHours = double.NegativeInfinity;

        private readonly CrossingBinding[] _bound = new CrossingBinding[MaxCrossingGuards];
        private int _boundCount;

        public int ReleasedCount => _released.Count;

        /// <summary>The children bound to guard a crossing.</summary>
        public int BoundCount => _boundCount;

        public CrossingBinding BindingAt(int index) => _bound[index];

        /// <summary>Binds a child to a crossing; false when the table is full or the child is already bound.</summary>
        public bool BindCrossing(in CrossingBinding binding)
        {
            if (binding.ChildId == null || _boundCount >= MaxCrossingGuards || TryCrossingOf(binding.ChildId, out _)) return false;
            _bound[_boundCount++] = binding;
            return true;
        }

        public bool TryCrossingOf(string childId, out CrossingBinding binding)
        {
            for (int i = 0; i < _boundCount; i++)
            {
                if (!string.Equals(_bound[i].ChildId, childId, StringComparison.Ordinal)) continue;
                binding = _bound[i];
                return true;
            }
            binding = default(CrossingBinding);
            return false;
        }

        /// <summary>Ends the child's binding (it reverts to Reserve on the next allocation); false when it had none.</summary>
        public bool UnbindCrossing(string childId)
        {
            for (int i = 0; i < _boundCount; i++)
            {
                if (!string.Equals(_bound[i].ChildId, childId, StringComparison.Ordinal)) continue;
                _bound[i] = _bound[--_boundCount];
                _bound[_boundCount] = default(CrossingBinding);
                return true;
            }
            return false;
        }

        /// <summary>The children the last allocation released for a crisis, in release order.</summary>
        public IReadOnlyList<string> CrisisReleased => _crisis;

        /// <summary>The releases of the last allocation.</summary>
        public IReadOnlyList<Outcome> Outcomes => _outcomes;

        public bool IsReleased(string childId) => childId != null && _released.ContainsKey(childId);

        public void Record(string childId, double hours)
        {
            if (childId == null) return;
            _released[childId] = hours;
            LastReleaseHours = hours;
        }

        /// <summary>The trigger stopped: released children revert (the crisis list and outcomes are this allocation's facts and stay). The crossing bindings are not the stagger's and stay.</summary>
        public void ClearTrigger()
        {
            _released.Clear();
            InitialReserves = 0;
            LastReleaseHours = double.NegativeInfinity;
        }

        /// <summary>A plan change, a new battle day or a stand-down: everything is forgotten.</summary>
        public void Clear()
        {
            ClearTrigger();
            Array.Clear(_bound, 0, _boundCount);
            _boundCount = 0;
            _crisis.Clear();
            _outcomes.Clear();
        }

        /// <summary>Called by the allocator at the start of each allocation: last allocation's crisis list and outcomes are history.</summary>
        internal void BeginAllocation()
        {
            _crisis.Clear();
            _outcomes.Clear();
        }

        internal void NoteCrisis(string childId) => _crisis.Add(childId);

        internal void NoteOutcome(in Outcome outcome) => _outcomes.Add(outcome);
    }
}
