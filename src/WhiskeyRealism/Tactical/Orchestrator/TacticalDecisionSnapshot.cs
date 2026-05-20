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
    /// All fields are immutable after construction. Each worker cycle constructs
    /// a fresh snapshot and atomically publishes it; the prior snapshot becomes
    /// garbage once no reader holds a reference. This eliminates the mutable-pool
    /// race where a reader's TryGetGroupStance could overlap a worker's Clear().
    ///
    /// Accessibility is internal because it composes internal types
    /// (TacticalSideDecision, CommandTreeSnapshot). All consumers live
    /// in the same assembly.
    /// </summary>
    internal sealed class TacticalDecisionSnapshot
    {
        // Note: _current = Empty evaluates to null at static-init time because the
        // Empty property initializer hasn't run yet; Current.get's `?? Empty` papers
        // over that race window until the static ctor completes.
        private static TacticalDecisionSnapshot _current = Empty;

        // Per-side global state
        public TacticalSideDecision Side0 { get; }
        public TacticalSideDecision Side1 { get; }

        // Cycle metadata
        public int BattleSequence { get; }
        public long CycleId { get; }
        public float CapturedAtBattleHours { get; }
        public float CapturedAtRealtimeSeconds { get; }

        // Per-unit lookup tables — separate dict per alliance for index density.
        // Ownership transferred from constructor caller; never mutated after construction.
        private readonly Dictionary<int, TacticalGroupStanceDecision> _groupStance0;
        private readonly Dictionary<int, TacticalGroupStanceDecision> _groupStance1;

        // Parallel per-unit sector cache. Gap-fill chunk 1: stored so the
        // main-thread apply-cached fast path can drive TryApplyDoctrineStance
        // and TryApplyLedgerTaskStance from the worker-built sector instead of
        // rebuilding it from live Regiment state. Missing entry = no cached
        // sector for that instance id (caller should not consume the
        // doctrine/ledger branches in that case — falls back to the default
        // legacy path). Same alliance-keyed ownership transfer as
        // _groupStanceN; null in the constructor allocates an empty dict.
        private readonly Dictionary<int, TacticalSectorAssessment> _groupSector0;
        private readonly Dictionary<int, TacticalSectorAssessment> _groupSector1;

        /// <summary>
        /// Constructs an immutable snapshot. Caller transfers ownership of the
        /// two dictionaries; they must not be mutated after this constructor returns.
        /// Pass null for either dict to get an empty one allocated internally.
        /// </summary>
        public TacticalDecisionSnapshot(
            int battleSequence,
            long cycleId,
            float capturedAtBattleHours,
            float capturedAtRealtimeSeconds,
            TacticalSideDecision side0,
            TacticalSideDecision side1,
            Dictionary<int, TacticalGroupStanceDecision> groupStance0,
            Dictionary<int, TacticalGroupStanceDecision> groupStance1,
            Dictionary<int, TacticalSectorAssessment> groupSector0 = null,
            Dictionary<int, TacticalSectorAssessment> groupSector1 = null)
        {
            BattleSequence = battleSequence;
            CycleId = cycleId;
            CapturedAtBattleHours = capturedAtBattleHours;
            CapturedAtRealtimeSeconds = capturedAtRealtimeSeconds;
            Side0 = side0;
            Side1 = side1;
            _groupStance0 = groupStance0 ?? new Dictionary<int, TacticalGroupStanceDecision>(0);
            _groupStance1 = groupStance1 ?? new Dictionary<int, TacticalGroupStanceDecision>(0);
            _groupSector0 = groupSector0 ?? new Dictionary<int, TacticalSectorAssessment>(0);
            _groupSector1 = groupSector1 ?? new Dictionary<int, TacticalSectorAssessment>(0);
        }

        public static TacticalDecisionSnapshot Empty { get; } = new TacticalDecisionSnapshot(
            battleSequence: 0,
            cycleId: 0,
            capturedAtBattleHours: 0f,
            capturedAtRealtimeSeconds: 0f,
            side0: TacticalSideDecision.Empty,
            side1: TacticalSideDecision.Empty,
            groupStance0: null,
            groupStance1: null);

        public bool TryGetGroupStance(int allianceId, int instanceId, out TacticalGroupStanceDecision decision)
        {
            var dict = allianceId == 0 ? _groupStance0
                     : allianceId == 1 ? _groupStance1
                     : null;
            if (dict != null && dict.TryGetValue(instanceId, out decision)) return true;
            decision = default;
            return false;
        }

        /// <summary>
        /// Returns the worker-built sector for a given group, when present.
        /// Used by BattleGroupStancePatch's apply-cached fast path to drive
        /// TryApplyDoctrineStance / TryApplyLedgerTaskStance without rebuilding
        /// the sector from live Regiment state.
        /// </summary>
        public bool TryGetGroupSector(int allianceId, int instanceId, out TacticalSectorAssessment sector)
        {
            var dict = allianceId == 0 ? _groupSector0
                     : allianceId == 1 ? _groupSector1
                     : null;
            if (dict != null && dict.TryGetValue(instanceId, out sector)) return true;
            sector = default;
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
        /// (which is now eligible for GC once no reader holds a reference).
        /// </summary>
        internal static TacticalDecisionSnapshot Publish(TacticalDecisionSnapshot next)
        {
            if (next == null) next = Empty;
            return Interlocked.Exchange(ref _current, next);
        }

        /// <summary>
        /// Resets the static Current to Empty. Called from
        /// TacticalBattleCoordinator.ResetRuntimeTickState on battle end.
        /// </summary>
        internal static void ResetForBattleEnd()
        {
            Interlocked.Exchange(ref _current, Empty);
        }
    }
}
