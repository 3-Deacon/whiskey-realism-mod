using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // BC13 (spec docs/superpowers/specs/2026-10-15-battle-opportunity-design.md § 6, requirement-staggered-reserve-release):
    // the memory of the army's reserve releases, per side, pure. Exploit and odds releases are staggered: at most one Reserve
    // child per release interval, never below a floor of held reserves, and a released child stays released while its trigger
    // holds. Crisis releases are stateless (recomputed every allocation); the state only lists them for the row and for the
    // exploit lifecycle (a crisis release of an exploit asset's parent line aborts the exploit).
    //
    // Owner: DirectChildAllocator.ReleaseReserves writes it during an allocation; ArmyOrchestrator reads the outcomes to emit
    // the TacticalReserveRelease rows and clears it on a plan change, a new battle day and a stand-down. It lives on the army
    // orchestrator, which is created per battle, so no static state needs a scope reset.
    public sealed class ReserveReleaseState
    {
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

        public int ReleasedCount => _released.Count;

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

        /// <summary>The trigger stopped: released children revert (the crisis list and outcomes are this allocation's facts and stay).</summary>
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
