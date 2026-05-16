using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure immutable DTO (Task 4). Holds the expensive heavy-path data built once when
    /// TacticalHeavyPathGate allows: ArmyEvidenceBuilder output (evidence + visible state + scalars)
    /// + TacticalVisionRuntimeAdapter objectives (with embedded approach avenues) + CommandTreeSnapshot
    /// + DirectChildSnapshot inputs at heavy build time.
    ///
    /// Reused across:
    /// - DriveTickCycle / ArmyTickCycle.MaybeReplan (ArmyEvidence + EnemyVisibleState + scalars)
    /// - DriveDirectChildCycle (bundle for DirectChildEvidenceBuilder + ArmyIntentInference)
    /// - DriveOperationsLedger / TickOperationsLedger (objectives for director + CommandDoctrineAssignment)
    /// - Urgent recovery (#61 CommandPostureExecutor, local formation fixes) against last published snapshot + live vanilla.
    ///
    /// Pure contract: System.* + other pure Whiskey Orchestrator/Operations types only.
    /// No UnityEngine, no vanilla reflection or AIBattle/Regiment/GameVars/BattleUnits.
    /// Builder (TacticalBattleSnapshotBuilder, Task 5) is the only runtime adapter that constructs instances.
    ///
    /// Immutable: ctor performs sanitization + defensive copies of all list/array inputs.
    /// Empty singleton provided for degraded/no-plan cases (matches StrategicBattleIntentSnapshot.Empty pattern).
    ///
    /// Follows patterns from TacticalBattleStateSignature, StrategicBattleIntentSnapshot, CommandTreeSnapshot,
    /// EnemyVisibleState, DirectChildSnapshot, etc.
    ///
    /// Authoritative: docs/superpowers/plans/2026-05-17-tactical-tick-optimization-implementation-plan.md Task 4
    /// and Tactical/AGENTS.md (pure DTO in Orchestrator/, explicit Compile Include for tests).
    /// </summary>
    internal readonly struct TacticalBattleRuntimeSnapshot
    {
        public static readonly TacticalBattleRuntimeSnapshot Empty = new TacticalBattleRuntimeSnapshot(
            default(TacticalBattleStateSignature),
            0f,
            default(ArmyEvidence),
            new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false, 0f),
            0f,
            0f,
            0f,
            0f,
            Array.Empty<ObjectiveRecord>(),
            CommandTreeSnapshot.Empty,
            Array.Empty<DirectChildSnapshot>());

        public TacticalBattleRuntimeSnapshot(
            TacticalBattleStateSignature signatureAtBuild,
            float buildBattleHours,
            ArmyEvidence ownEvidence,
            EnemyVisibleState enemyVisible,
            float ownMainEffortStrength,
            float ownArmyMorale,
            float ownReservesCommittedFraction,
            float reinforcementsArrivingDelta,
            IReadOnlyList<ObjectiveRecord> objectives,
            CommandTreeSnapshot commandTree,
            IReadOnlyList<DirectChildSnapshot> directChildSnapshots)
        {
            SignatureAtBuild = signatureAtBuild;
            BuildBattleHours = buildBattleHours < 0f || float.IsNaN(buildBattleHours) || float.IsInfinity(buildBattleHours) ? 0f : buildBattleHours;
            OwnEvidence = ownEvidence;
            EnemyVisible = enemyVisible; // EnemyVisibleState performs its own defensive sector array copy
            OwnMainEffortStrength = SanitizeNonNegative(ownMainEffortStrength);
            OwnArmyMorale = Clamp01(ownArmyMorale);
            OwnReservesCommittedFraction = Clamp01(ownReservesCommittedFraction);
            ReinforcementsArrivingDelta = SanitizeNonNegative(reinforcementsArrivingDelta);
            Objectives = CopyObjectives(objectives);
            CommandTree = commandTree ?? CommandTreeSnapshot.Empty;
            DirectChildSnapshots = CopyDirectChildSnapshots(directChildSnapshots);
        }

        public TacticalBattleStateSignature SignatureAtBuild { get; }
        public float BuildBattleHours { get; }
        public ArmyEvidence OwnEvidence { get; }
        public EnemyVisibleState EnemyVisible { get; }
        public float OwnMainEffortStrength { get; }
        public float OwnArmyMorale { get; }
        public float OwnReservesCommittedFraction { get; }
        public float ReinforcementsArrivingDelta { get; }
        public IReadOnlyList<ObjectiveRecord> Objectives { get; }
        public CommandTreeSnapshot CommandTree { get; }
        public IReadOnlyList<DirectChildSnapshot> DirectChildSnapshots { get; }

        /// <summary>
        /// True if this snapshot carries usable heavy data (objectives, command tree nodes, or direct children).
        /// Used by callers to decide whether to fall back to vanilla-only urgent recovery.
        /// </summary>
        public bool HasData => Objectives.Count > 0 || CommandTree.HasNodes || DirectChildSnapshots.Count > 0;

        private static float SanitizeNonNegative(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return v < 0f ? 0f : v;
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            if (v < 0f) return 0f;
            return v > 1f ? 1f : v;
        }

        private static IReadOnlyList<ObjectiveRecord> CopyObjectives(IReadOnlyList<ObjectiveRecord> src)
        {
            if (src == null || src.Count == 0) return Array.Empty<ObjectiveRecord>();
            var copy = new ObjectiveRecord[src.Count];
            for (int i = 0; i < src.Count; i++) copy[i] = src[i];
            return copy;
        }

        private static IReadOnlyList<DirectChildSnapshot> CopyDirectChildSnapshots(IReadOnlyList<DirectChildSnapshot> src)
        {
            if (src == null || src.Count == 0) return Array.Empty<DirectChildSnapshot>();
            var copy = new DirectChildSnapshot[src.Count];
            for (int i = 0; i < src.Count; i++) copy[i] = src[i];
            return copy;
        }
    }
}
