using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Immutable input the main thread fills and passes to the worker.
    /// Contains everything the orchestrator analysis needs from live vanilla
    /// state. Reference-shared with the worker; only main thread populates,
    /// only worker reads. Worker never touches Plugin.Instance, Time.*,
    /// BattleUnits.*, Regiment.*, or any Unity API.
    ///
    /// Accessibility is internal because it composes internal types
    /// (TacticalBattleRuntimeSnapshot, ArmyEvidenceBuilder.Bundle,
    /// CommandTreeSnapshot). All consumers live in the same assembly.
    /// Matches the accessibility precedent set by TacticalDecisionSnapshot.
    ///
    /// Field set determined by Task 7's worker-input audit
    /// (see audit commit message for the full live-vanilla touch table).
    /// Heavy-gate scratch state, telemetry emission, and telemetry-label
    /// resolution intentionally stay on main and are NOT in this container.
    /// </summary>
    internal sealed class TacticalOrchestratorWorkerInput
    {
        public int BattleSequence { get; }
        public int PlayerAllianceId { get; }
        public bool AiVsAi { get; }
        public float BattleHours { get; }
        public float RealtimeSeconds { get; }
        public float DeltaSeconds { get; }
        public float BattleDeltaSeconds { get; }

        // Per-side heavy snapshots — populated only for sides we actually drive
        // (suppressed side stays default). Each is the output of
        // TacticalBattleSnapshotBuilder.Build for that side.
        public TacticalBattleRuntimeSnapshot Side0Snapshot { get; }
        public TacticalBattleRuntimeSnapshot Side1Snapshot { get; }

        // Per-side evidence bundles (ArmyEvidenceBuilder.Bundle is a readonly
        // struct — value-typed). When the heavy-gate OFF branch is taken, main
        // pre-builds these via ArmyEvidenceBuilder.Build; when the gate ON
        // branch dispatches a fresh tick, main still builds them so the worker
        // has a single, consistent source.
        public ArmyEvidenceBuilder.Bundle Side0Bundle { get; }
        public ArmyEvidenceBuilder.Bundle Side1Bundle { get; }

        // Per-side command tree + direct-child snapshots. Pre-built on main via
        // CommandTreeRuntime.Snapshot / DirectChildDiscovery.Snapshot for the
        // Attach* paths that call them independently of the heavy snapshot.
        public CommandTreeSnapshot Side0CommandTree { get; }
        public CommandTreeSnapshot Side1CommandTree { get; }
        public IReadOnlyList<DirectChildSnapshot> Side0DirectChildren { get; }
        public IReadOnlyList<DirectChildSnapshot> Side1DirectChildren { get; }

        // Per-side commander metadata (read once on main from the roster).
        // Null when no commander is attached (e.g., suppressed side).
        public CommanderRosterEntry Side0CommanderEntry { get; }
        public CommanderRosterEntry Side1CommanderEntry { get; }

        // Per-side reg-instance-id → sector-index map, pre-built on main via
        // TacticalBattleCoordinatorRuntime.BuildInstanceToSectorIndexMap.
        // Consumed by DriveDirectChildCycle; not represented anywhere in the
        // snapshot/bundle/tree/children types, so it lives here.
        public IReadOnlyDictionary<int, int> Side0InstanceToSectorMap { get; }
        public IReadOnlyDictionary<int, int> Side1InstanceToSectorMap { get; }

        // Gap-fill chunk 1: per-side observation arrays for worker-thread
        // group-stance decisions. Snapshotted from TacticalUnitObservationAggregate.Shared
        // immediately after each side's heavy snapshot Build (which is the
        // singleton's last write for that alliance). Worker reads observation
        // fields (GroupOwnInRange/GroupAiGroup/GroupEnemiesInRange/GroupAngleEnemyStrength/
        // GroupFlankRisk/GroupStrongPoint/GroupVanillaStanceOrdered/VisibleEnemy*) to
        // rebuild TacticalGroupContactInput without touching live vanilla state.
        // Arrays (not pooled list refs) so the worker is immune to next-cycle
        // singleton overwrites.
        public IReadOnlyList<TacticalUnitObservation> Side0Observations { get; }
        public IReadOnlyList<TacticalUnitObservation> Side1Observations { get; }

        // Authoritative alliance ids — captured from bunits.alliance[0/1] at
        // dispatch time. Worker uses these to route per-side group-stance
        // decisions into the alliance-keyed dicts on TacticalDecisionSnapshot.
        public int Side0AllianceId { get; }
        public int Side1AllianceId { get; }

        // Per-side AIBattle.macroai at dispatch time. Sourced from
        // bunits.sideinformation[side].macroai (vanilla state for vanilla side 0/1).
        // Worker feeds these into TacticalDoctrineScorer.DecideGroupStance so the
        // captured-data decision stream matches the patch's live macro read.
        public int Side0MacroAi { get; }
        public int Side1MacroAi { get; }

        // Frozen config-flag snapshot captured at dispatch time. Worker reads
        // these instead of Plugin.Instance.* to avoid races against runtime
        // config-flag mutation.
        public TacticalOrchestratorWorkerConfigSnapshot Config { get; }

        public TacticalOrchestratorWorkerInput(
            int battleSequence,
            int playerAllianceId,
            bool aiVsAi,
            float battleHours,
            float realtimeSeconds,
            float deltaSeconds,
            float battleDeltaSeconds,
            TacticalBattleRuntimeSnapshot side0Snapshot,
            TacticalBattleRuntimeSnapshot side1Snapshot,
            ArmyEvidenceBuilder.Bundle side0Bundle,
            ArmyEvidenceBuilder.Bundle side1Bundle,
            CommandTreeSnapshot side0CommandTree,
            CommandTreeSnapshot side1CommandTree,
            IReadOnlyList<DirectChildSnapshot> side0DirectChildren,
            IReadOnlyList<DirectChildSnapshot> side1DirectChildren,
            CommanderRosterEntry side0CommanderEntry,
            CommanderRosterEntry side1CommanderEntry,
            IReadOnlyDictionary<int, int> side0InstanceToSectorMap,
            IReadOnlyDictionary<int, int> side1InstanceToSectorMap,
            TacticalOrchestratorWorkerConfigSnapshot config,
            IReadOnlyList<TacticalUnitObservation> side0Observations = null,
            IReadOnlyList<TacticalUnitObservation> side1Observations = null,
            int side0AllianceId = -1,
            int side1AllianceId = -1,
            int side0MacroAi = -1,
            int side1MacroAi = -1)
        {
            BattleSequence = battleSequence;
            PlayerAllianceId = playerAllianceId;
            AiVsAi = aiVsAi;
            BattleHours = battleHours;
            RealtimeSeconds = realtimeSeconds;
            DeltaSeconds = deltaSeconds;
            BattleDeltaSeconds = battleDeltaSeconds;
            Side0Snapshot = side0Snapshot;
            Side1Snapshot = side1Snapshot;
            Side0Bundle = side0Bundle;
            Side1Bundle = side1Bundle;
            Side0CommandTree = side0CommandTree ?? CommandTreeSnapshot.Empty;
            Side1CommandTree = side1CommandTree ?? CommandTreeSnapshot.Empty;
            Side0DirectChildren = side0DirectChildren ?? Array.Empty<DirectChildSnapshot>();
            Side1DirectChildren = side1DirectChildren ?? Array.Empty<DirectChildSnapshot>();
            Side0CommanderEntry = side0CommanderEntry;
            Side1CommanderEntry = side1CommanderEntry;
            Side0InstanceToSectorMap = side0InstanceToSectorMap ?? EmptyInstanceToSectorMap;
            Side1InstanceToSectorMap = side1InstanceToSectorMap ?? EmptyInstanceToSectorMap;
            Config = config ?? TacticalOrchestratorWorkerConfigSnapshot.Empty;
            Side0Observations = side0Observations ?? Array.Empty<TacticalUnitObservation>();
            Side1Observations = side1Observations ?? Array.Empty<TacticalUnitObservation>();
            Side0AllianceId = side0AllianceId;
            Side1AllianceId = side1AllianceId;
            Side0MacroAi = side0MacroAi;
            Side1MacroAi = side1MacroAi;
        }

        private static readonly IReadOnlyDictionary<int, int> EmptyInstanceToSectorMap =
            new Dictionary<int, int>(0);
    }

    /// <summary>
    /// Frozen config-flag snapshot captured at worker-dispatch time. Worker
    /// reads these instead of Plugin.Instance.* to avoid races against runtime
    /// config changes (e.g., user toggles a flag while the worker is mid-cycle).
    ///
    /// Underlying types match the live Plugin properties:
    ///   - EnableTacticalHeavyPathThrottling     : bool   (Plugin.Instance.EnableTacticalHeavyPathThrottling.Value)
    ///   - EnableTacticalOrchestratorIntentInference : bool (static ConfigEntry&lt;bool&gt;)
    ///   - HeavyReviewMinRealtimeSeconds         : float  (Plugin.Instance.HeavyReviewMinRealtimeSeconds)
    ///   - HeavyReviewCycleHours                 : float  (Plugin.Instance.HeavyReviewCycleHours)
    ///   - MinReplanSeconds                      : int    (Plugin.TacticalOrchestratorMinReplanSeconds.Value — kept as int)
    ///   - EnableSinglePassObjectiveRecords      : bool   (static ConfigEntry&lt;bool&gt;)
    ///   - CommanderMode                         : TacticalCommanderMode (Plugin.Instance.TacticalCommanderModeValue)
    /// </summary>
    internal sealed class TacticalOrchestratorWorkerConfigSnapshot
    {
        public bool EnableHeavyPathThrottling { get; }
        public bool EnableIntentInference { get; }
        public float HeavyReviewMinRealtimeSeconds { get; }
        public float HeavyReviewCycleHours { get; }
        public int MinReplanSeconds { get; }
        public bool EnableSinglePassObjectiveRecords { get; }
        public TacticalCommanderMode CommanderMode { get; }

        public TacticalOrchestratorWorkerConfigSnapshot(
            bool enableHeavyPathThrottling,
            bool enableIntentInference,
            float heavyReviewMinRealtimeSeconds,
            float heavyReviewCycleHours,
            int minReplanSeconds,
            bool enableSinglePassObjectiveRecords,
            TacticalCommanderMode commanderMode)
        {
            EnableHeavyPathThrottling = enableHeavyPathThrottling;
            EnableIntentInference = enableIntentInference;
            HeavyReviewMinRealtimeSeconds = heavyReviewMinRealtimeSeconds;
            HeavyReviewCycleHours = heavyReviewCycleHours;
            MinReplanSeconds = minReplanSeconds;
            EnableSinglePassObjectiveRecords = enableSinglePassObjectiveRecords;
            CommanderMode = commanderMode;
        }

        public static TacticalOrchestratorWorkerConfigSnapshot Empty { get; } =
            new TacticalOrchestratorWorkerConfigSnapshot(
                enableHeavyPathThrottling: false,
                enableIntentInference: false,
                heavyReviewMinRealtimeSeconds: 0f,
                heavyReviewCycleHours: 0f,
                minReplanSeconds: 0,
                enableSinglePassObjectiveRecords: false,
                commanderMode: default);
    }
}
