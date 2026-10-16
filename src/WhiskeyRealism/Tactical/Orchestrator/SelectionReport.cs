using System;
using WhiskeyRealism.Tactical.Orchestrator.Plays;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// One selection's candidates, ranked, with the feasibility term each carried (BC14): the pooled record the TacticalPlanSelection row
    /// reads. The catalog fills it while it scores; it allocates nothing after construction.
    /// </summary>
    public sealed class SelectionReport
    {
        public const int MaxEntries = 32;

        public struct Entry
        {
            public BattlePlanId Plan;
            /// <summary>The score before the feasibility term, and the term itself (zero or negative).</summary>
            public float BaseScore, Term;
            public float Score => BaseScore + Term;
            public FeasibilityVerdict Verdict;
            public bool Mirrored;
            public string Reason;
            /// <summary>The feasibility filter skipped it (Impossible).</summary>
            public bool Skipped;
        }

        private readonly Entry[] _entries = new Entry[MaxEntries];

        public int Count { get; private set; }

        public int PreconditionFiltered, FeasibilityFiltered;

        /// <summary>Every candidate was Impossible, so the filter was lifted for this selection.</summary>
        public bool AllImpossible;

        public ref readonly Entry this[int rank] => ref _entries[rank];

        public void Clear()
        {
            Count = 0;
            PreconditionFiltered = 0;
            FeasibilityFiltered = 0;
            AllImpossible = false;
        }

        internal void Add(BattlePlanId plan, float baseScore, float term, FeasibilityVerdict verdict, bool mirrored, string reason, bool skipped)
        {
            if (Count >= MaxEntries) return;
            _entries[Count++] = new Entry { Plan = plan, BaseScore = baseScore, Term = term, Verdict = verdict, Mirrored = mirrored, Reason = reason, Skipped = skipped };
        }

        /// <summary>Orders the scored candidates by final score, best first (a stable insertion sort); the skipped ones follow.</summary>
        internal void Rank()
        {
            for (int i = 1; i < Count; i++)
            {
                Entry e = _entries[i];
                int at = i;
                while (at > 0 && Before(in e, in _entries[at - 1])) { _entries[at] = _entries[at - 1]; at--; }
                _entries[at] = e;
            }
        }

        private static bool Before(in Entry a, in Entry b)
        {
            if (a.Skipped != b.Skipped) return !a.Skipped;
            return a.Score > b.Score;
        }
    }
}
