using System.Collections.Generic;
using System.Threading;
using WhiskeyRealism.Tactical;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Per-cycle immutable snapshot of all per-side and per-unit decisions
    /// produced by the worker thread. Read by main-thread Harmony patches
    /// via <see cref="Current"/>. Atomic publish via Interlocked.Exchange.
    ///
    /// Pooled at the worker layer (TacticalOrchestratorWorker) to bound
    /// per-cycle allocations. The previously-published snapshot stays in
    /// _current until the worker swaps in the next one.
    ///
    /// Accessibility is internal because it composes internal types
    /// (TacticalSideDecision, CommandTreeSnapshot). All consumers live
    /// in the same assembly.
    /// </summary>
    internal sealed class TacticalDecisionSnapshot
    {
        private static TacticalDecisionSnapshot _current = Empty;

        // Per-side global state
        public TacticalSideDecision Side0 { get; private set; }
        public TacticalSideDecision Side1 { get; private set; }

        // Cycle metadata
        public int BattleSequence { get; private set; }
        public long CycleId { get; private set; }
        public float CapturedAtBattleHours { get; private set; }
        public float CapturedAtRealtimeSeconds { get; private set; }

        // Per-unit lookup tables — separate dict per alliance for index density.
        private readonly Dictionary<int, TacticalGroupStanceDecision> _groupStance0;
        private readonly Dictionary<int, TacticalGroupStanceDecision> _groupStance1;

        private TacticalDecisionSnapshot(int initialCapacity)
        {
            _groupStance0 = new Dictionary<int, TacticalGroupStanceDecision>(initialCapacity);
            _groupStance1 = new Dictionary<int, TacticalGroupStanceDecision>(initialCapacity);
        }

        public static TacticalDecisionSnapshot Empty { get; } = new TacticalDecisionSnapshot(0);

        /// <summary>
        /// Worker-thread-only: clear + repopulate this snapshot's contents from
        /// fresh decisions. Followed by <see cref="Publish"/> to make it current.
        /// </summary>
        internal void PopulateForWorker(
            int battleSequence,
            long cycleId,
            float capturedAtBattleHours,
            float capturedAtRealtimeSeconds,
            TacticalSideDecision side0,
            TacticalSideDecision side1)
        {
            BattleSequence = battleSequence;
            CycleId = cycleId;
            CapturedAtBattleHours = capturedAtBattleHours;
            CapturedAtRealtimeSeconds = capturedAtRealtimeSeconds;
            Side0 = side0;
            Side1 = side1;
            _groupStance0.Clear();
            _groupStance1.Clear();
        }

        internal void SetGroupStance(int allianceId, int instanceId, TacticalGroupStanceDecision decision)
        {
            if (allianceId == 0) _groupStance0[instanceId] = decision;
            else if (allianceId == 1) _groupStance1[instanceId] = decision;
        }

        public bool TryGetGroupStance(int allianceId, int instanceId, out TacticalGroupStanceDecision decision)
        {
            var dict = allianceId == 0 ? _groupStance0
                     : allianceId == 1 ? _groupStance1
                     : null;
            if (dict != null && dict.TryGetValue(instanceId, out decision)) return true;
            decision = default;
            return false;
        }

        public TacticalSideDecision GetSide(int allianceId)
        {
            return allianceId == 0 ? Side0
                 : allianceId == 1 ? Side1
                 : TacticalSideDecision.Empty;
        }

        public static TacticalDecisionSnapshot Current
        {
            get { return Volatile.Read(ref _current) ?? Empty; }
        }

        /// <summary>
        /// Atomic publish. Called from worker thread. Returns the prior snapshot
        /// so the worker can put it back into the pool for reuse.
        /// </summary>
        internal static TacticalDecisionSnapshot Publish(TacticalDecisionSnapshot next)
        {
            if (next == null) next = Empty;
            return Interlocked.Exchange(ref _current, next);
        }

        /// <summary>
        /// Test-and-worker constructor — bypasses Empty to allow instantiation.
        /// </summary>
        internal static TacticalDecisionSnapshot CreateForPool(int initialCapacity)
        {
            return new TacticalDecisionSnapshot(initialCapacity);
        }

        /// <summary>
        /// Resets the static Current to Empty. Called from
        /// TacticalBattleCoordinator.ResetRuntimeTickState on battle end.
        /// </summary>
        public static void ResetForBattleEnd()
        {
            Interlocked.Exchange(ref _current, Empty);
        }
    }
}
