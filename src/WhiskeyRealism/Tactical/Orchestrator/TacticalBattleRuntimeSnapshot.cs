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
    // ============================================================
    // HEAVY SNAPSHOT vs PER-TICK BATTLE SNAPSHOT
    // ============================================================
    // This heavy snapshot is built ONLY when TacticalHeavyPathGate.Decide returns Run (coordinator
    // Drive* paths). It is the atomic unit: OwnEvidence + EnemyVisible + scalars + Objectives (w/ avenues)
    // + CommandTree + DirectChildSnapshots.
    //
    // Live movement and order state comes from the per-tick battle snapshot, not from vanilla.
    // Tactical/Battle/BattleSnapshot (BC1, plan docs/superpowers/plans/2026-10-15-battle-core.md) is
    // built once per side per doctrine tick from one pass over BattleUnits.completeunitlist, on the main
    // thread inside the coordinator tick (CheckGlobalAIStrategy, UpdateAITasks 5866), in the same frame
    // the arbiter acts in (AdjustGroupFormations 5870). Its UnitRecord carries, per unit and fresh every
    // tick: position and rotation, regimentpaths, queued courier orders, lastsetwaypointposition,
    // formation, ai_stance / ai_stanceordered, combatbehaviorordered, movementmode, strength,
    // morale, fatigue, ammo (T1 rule), routed / onretreat / inmelee / garrison / detached flags,
    // ownership flags, and the command parent from the single parent map (allattachedunits,
    // plan D5). Frequent-cadence readers take that snapshot (BattleCore.TryGetSnapshot) instead of
    // reading live Regiment fields themselves; the movers still reading live fields move onto it in
    // the BC5 migration.
    //
    // What stays here: the throttled, expensive derivations (evidence sectors, objective records with
    // approach avenues, the CommandTreeSnapshot / DirectChildSnapshot views of the parent map). Readers of
    // this struct guard with HasData and degrade to the per-tick BattleSnapshot when it is Empty
    // (heavy throttling off, no orchestrator/ledger yet, Commander Mode Off).
    //
    // INVARIANT: only coordinator gate logic calls the expensive builder (TacticalBattleSnapshotBuilder).
    // ============================================================
    internal readonly struct TacticalBattleRuntimeSnapshot
    {
        public static readonly TacticalBattleRuntimeSnapshot Empty = new TacticalBattleRuntimeSnapshot(
            default(TacticalBattleStateSignature),
            0f,
            default(ArmyEvidence),
            new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false, intelConfidence01: 0f),
            0f,
            0f,
            0f,
            0f,
            Array.Empty<ObjectiveRecord>(),
            CommandTreeSnapshot.Empty,
            Array.Empty<DirectChildSnapshot>(),
            ownAvgFatigue01: 0.2f,
            ownAvgAmmo01: 0.9f,
            nearestReinforcementHours: 0f,
            nearestReinforcementStrength: 0f);

        // Legacy ctor without readiness fields. Defaults to neutral readiness
        // so existing call sites that haven't been updated still produce a
        // PushReady decision (preserves pre-readiness behavior).
        public TacticalBattleRuntimeSnapshot(
            TacticalBattleStateSignature signatureAtBuild,
            float buildBattleHours,
            ArmyEvidence ownEvidence,
            EnemyVisibleState enemyVisible,
            float ownMainEffortStrength,
            float ownArmyMorale,
            float ownReservesCommittedFraction,
            float ownReinforcementsArriving,
            IReadOnlyList<ObjectiveRecord> objectives,
            CommandTreeSnapshot commandTree,
            IReadOnlyList<DirectChildSnapshot> directChildSnapshots)
            : this(signatureAtBuild, buildBattleHours, ownEvidence, enemyVisible,
                   ownMainEffortStrength, ownArmyMorale, ownReservesCommittedFraction,
                   ownReinforcementsArriving, objectives, commandTree, directChildSnapshots,
                   ownAvgFatigue01: 0.2f, ownAvgAmmo01: 0.9f,
                   nearestReinforcementHours: 0f, nearestReinforcementStrength: 0f)
        { }

        public TacticalBattleRuntimeSnapshot(
            TacticalBattleStateSignature signatureAtBuild,
            float buildBattleHours,
            ArmyEvidence ownEvidence,
            EnemyVisibleState enemyVisible,
            float ownMainEffortStrength,
            float ownArmyMorale,
            float ownReservesCommittedFraction,
            float ownReinforcementsArriving,
            IReadOnlyList<ObjectiveRecord> objectives,
            CommandTreeSnapshot commandTree,
            IReadOnlyList<DirectChildSnapshot> directChildSnapshots,
            float ownAvgFatigue01,
            float ownAvgAmmo01,
            float nearestReinforcementHours,
            float nearestReinforcementStrength)
        {
            SignatureAtBuild = signatureAtBuild;
            BuildBattleHours = buildBattleHours < 0f || float.IsNaN(buildBattleHours) || float.IsInfinity(buildBattleHours) ? 0f : buildBattleHours;
            OwnEvidence = ownEvidence;
            EnemyVisible = enemyVisible; // EnemyVisibleState performs its own defensive sector array copy
            OwnMainEffortStrength = SanitizeNonNegative(ownMainEffortStrength);
            OwnArmyMorale = Clamp01(ownArmyMorale);
            OwnReservesCommittedFraction = Clamp01(ownReservesCommittedFraction);
            OwnReinforcementsArriving = SanitizeNonNegative(ownReinforcementsArriving);
            Objectives = CopyObjectives(objectives);
            CommandTree = commandTree ?? CommandTreeSnapshot.Empty;
            DirectChildSnapshots = CopyDirectChildSnapshots(directChildSnapshots);
            OwnAvgFatigue01 = Clamp01(ownAvgFatigue01);
            OwnAvgAmmo01 = Clamp01(ownAvgAmmo01);
            NearestReinforcementHours = SanitizeNonNegative(nearestReinforcementHours);
            NearestReinforcementStrength = SanitizeNonNegative(nearestReinforcementStrength);
        }

        public TacticalBattleStateSignature SignatureAtBuild { get; }
        public float BuildBattleHours { get; }
        public ArmyEvidence OwnEvidence { get; }
        public EnemyVisibleState EnemyVisible { get; }
        public float OwnMainEffortStrength { get; }
        public float OwnArmyMorale { get; }
        public float OwnReservesCommittedFraction { get; }
        /// <summary>
        /// Own reinforcement strength due within 24 h
        /// (sideinformation.reinforcementarrivalswithin24hrs). Own side only:
        /// the enemy's schedule is not known; enemy arrivals count once in
        /// range, through the contact ledger.
        /// </summary>
        public float OwnReinforcementsArriving { get; }
        public IReadOnlyList<ObjectiveRecord> Objectives { get; }
        public CommandTreeSnapshot CommandTree { get; }
        public IReadOnlyList<DirectChildSnapshot> DirectChildSnapshots { get; }
        // Readiness fields (added 2026-05-19). Army-wide averages for now.
        public float OwnAvgFatigue01 { get; }
        public float OwnAvgAmmo01 { get; }
        public float NearestReinforcementHours { get; }
        public float NearestReinforcementStrength { get; }

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
