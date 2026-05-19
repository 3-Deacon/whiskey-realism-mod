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
    // URGENT RECOVERY SAFETY BOUNDARY (Task 8)
    // ============================================================
    // Heavy snapshot (TacticalBattleRuntimeSnapshot) is built ONLY when TacticalHeavyPathGate.Decide returns Run
    // (in coordinator Drive* paths). It is the atomic unit: OwnEvidence + EnemyVisible + scalars + Objectives (w/ avenues)
    // + CommandTree + DirectChildSnapshots.
    //
    // Urgent recovery (#61 BattleCommandPostureExecutorPatch on AdjustGroupFormations, CommandFormationCorrection
    // local flank/fallback/formation fixes, CommandPostureExecutor Decide + physical, TacticalCommandMonitor idle,
    // RecoverInterruptedOrder, FallbackToLine, charge local logic, etc.) runs on *frequent* vanilla cadence.
    //
    // These paths MAY SAFELY READ (without triggering heavy Build or full vision/evidence walks):
    //   - Live vanilla per-Regiment physical/order state (from bunits.unitsused Regiment objects via safe reflection):
    //     pathinterrupted, groupsubordinatesmoving, groupsubordinatesmovingnotfar, regimentpaths,
    //     lastsetwaypointposition, formation fields (SafeFormation/SafeFormationOrdered/SafeGroupFormation),
    //     rotationY, position, routed, morale, fatigue, ammo, combatbehaviorordered, unittyp, mounted,
    //     allattachedunits, permanentlydetached, HasPendingOrder, HasActiveMoveMakingProgress.
    //   - Local contacts / immediate threats (cheap FOW + proximity, NOT full ArmyEvidence sectors or objective records):
    //     TacticalFogOfWarContact.ClosestVisibleEnemy/HasFowVisibleEnemy, HasCloseEngagement, HasLocalFlankRisk,
    //     SafeEnemyDistance, ClosestEnemy, UnitInCover, EnemyAdvancing, IsAlignedToTarget, blockedcrossings.
    //   - Recent formation/order + cooldown state (patch-local dicts + vanilla timers): HasRecentReceivedFire,
    //     HasRecentExecutorOrder, CanBypassStaleQueuedOrder, RecentOrderCooldownSeconds, visible mismatch,
    //     allowPendingLocalFormation.
    //   - Vanilla battle cheap context (AIBattle/BattleUnits via Safe*): macroai, state, sideofai, isplayeraiorfeud,
    //     bunits.sideinformation force totals, GameVars.* (ai_vs_ai, currenttimefromstart for cooldowns).
    //   - LAST published snapshot ONLY (coordinator _lastPublishedSnapshots[side], when HasData) for high-level:
    //     Objectives (doctrine target resolution), CommandTree/DirectChildSnapshots (runtime play orders),
    //     EnemyVisible/scalars (coarse morale/effort at build time). Guard with snapshot.HasData; degrade when !HasData.
    //
    // MUST DEGRADE to live-vanilla-only (or prior pre-Task6 behavior) when:
    //   - !IsHeavyThrottlingEnabled() (preserve-off exact match).
    //   - No orchestrator/ledger active (TacticalCommanderMode Off or pre-bootstrap).
    //   - Last snapshot == Empty or !HasData (no objectives/nodes).
    //   - In cheap ExtractCurrentSignature path (frequent gate input; bounded scan only for anyInterruptedPathsOrNewContact).
    //   - Reflection failure (Safe* already return defaults + bounded warn; never throw).
    //
    // INVARIANT (Tasks 6/7): ONLY coordinator gate logic ever calls expensive builder. Urgent paths (#61 + local fixes)
    // are independent, always reuse last snapshot (or Empty) + fresh live vanilla per-group reads. This guarantees
    // immediate responsiveness for pathinterrupted recovery, flank emergencies, formation corrections even under heavy throttle.
    //
    // See: TacticalHeavyPathGate.Decide, TacticalBattleCoordinatorRuntime (DriveTick/DirectChild/OperationsLedger gate paths
    // + ExtractCurrentSignature calls), BattleCommandPostureExecutorPatch (Apply/TryApplyGroup/BuildPhysicalState/TryResolveTarget),
    // CommandFormationCorrection (TaskForLocalFlankEmergency, NeedsCorrection, RecentOrderCooldownSeconds, ShouldUseNewPathForFormationCorrection),
    // CommandPostureExecutor (Decide/ShouldSkipDuplicateWaypoint using pathInterrupted), TacticalBattleOrchestrator.TickOperationsLedger,
    // TacticalOperationsLedgerRuntime.Update (HasData guard).
    //
    // Authoritative: plan Task 8 + Tactical/AGENTS.md (runtime safety: try/catch, degrade, bounded logging, no throw).
    // ============================================================
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
            float reinforcementsArrivingDelta,
            IReadOnlyList<ObjectiveRecord> objectives,
            CommandTreeSnapshot commandTree,
            IReadOnlyList<DirectChildSnapshot> directChildSnapshots)
            : this(signatureAtBuild, buildBattleHours, ownEvidence, enemyVisible,
                   ownMainEffortStrength, ownArmyMorale, ownReservesCommittedFraction,
                   reinforcementsArrivingDelta, objectives, commandTree, directChildSnapshots,
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
            float reinforcementsArrivingDelta,
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
            ReinforcementsArrivingDelta = SanitizeNonNegative(reinforcementsArrivingDelta);
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
        public float ReinforcementsArrivingDelta { get; }
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
