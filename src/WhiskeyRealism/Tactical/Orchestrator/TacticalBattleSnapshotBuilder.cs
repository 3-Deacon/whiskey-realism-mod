using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Runtime-only adapter (Task 5). The single place responsible for performing
    /// expensive heavy-path construction of <see cref="TacticalBattleRuntimeSnapshot"/>
    /// (ArmyEvidence + full EnemyVisible sectors + TacticalVision objective records with
    /// approach avenues + CommandTreeSnapshot + DirectChildSnapshots), all derived from the
    /// side's per-tick <see cref="BattleSnapshot"/> (BC2b): enemy figures only from its
    /// visible and remembered observations.
    ///
    /// Also supplies the cheap <see cref="TacticalBattleStateSignature"/> extractor used
    /// by the frequent tick path to feed <see cref="TacticalHeavyPathGate"/> without
    /// paying the heavy cost.
    ///
    /// This file is EXCLUDED from the test csproj (no &lt;Compile Include&gt;). All
    /// vanilla reflection, BattleUnits, AIBattle, Regiment, GameVars, UnityEngine access
    /// lives here. Pure DTOs / gate / signature live elsewhere and remain harness-testable.
    ///
    /// Integration: <see cref="TacticalBattleCoordinatorRuntime"/> (Task 6) will:
    /// - compute currentSig = TacticalBattleSnapshotBuilder.ExtractCurrentSignature(snapshot, battle, hours) cheaply
    /// - read per-side lastHeavyHours, lastSig, hasPending, lastPublished
    /// - call TacticalHeavyPathGate.Decide(new Input(...))
    /// - on Decision.ShouldRun: TacticalBattleSnapshotBuilder.Build(snapshot, battle, currentSig, currentHours)
    ///   then update lasts + publish
    /// - else: reuse lastPublished snapshot for DriveTickCycle / DriveDirectChildCycle / DriveOperationsLedger
    ///
    /// The builder itself does not hold per-battle state (stateless static); the coordinator
    /// owns _lastPublished[2], _lastHeavyHours[2], _lastSig[2], _hasPending[2], and the
    /// battle-level lastsidestatupdate dedup guard using BattleUnits.
    ///
    /// On any reflection/runtime failure the builder degrades gracefully (Empty snapshot or
    /// zeroed signature) and emits a bounded OnceLog.Warning — never throws into Harmony path.
    ///
    /// Authoritative: docs/superpowers/plans/2026-05-17-tactical-tick-optimization-implementation-plan.md
    /// Task 5 + Task 6 wiring expectations + Tactical/AGENTS.md (runtime adapter rules).
    /// </summary>
    internal static class TacticalBattleSnapshotBuilder
    {
        // Cached reflection for AIBattle.bunits (same pattern as ArmyEvidenceBuilder / CommandTreeRuntime)
        private static FieldInfo _bunitsFieldCache;

        /// <summary>
        /// Cheap signature extractor over the side's battle snapshot (D1: built first in the
        /// coordinator tick). Own side: its totalactiveforce and scripted macroai override
        /// (sideinformation[snapshot.Side], CalculateSideStats 84700-84862). Enemy side: only
        /// what is seen, the visible strength in buckets (the enemy's totalactiveforce and
        /// macroai are not knowable). Plus eodcycle, the own objective chain hash, a contact
        /// bucket over own units and the time bucket. Safe to call every side-stat tick.
        /// </summary>
        public static TacticalBattleStateSignature ExtractCurrentSignature(
            BattleSnapshot snapshot,
            AIBattle battle,
            float currentBattleHours)
        {
            try
            {
                var bunits = ResolveBattleUnits(battle);
                int side = snapshot != null ? snapshot.Side : -1;
                if (bunits == null || bunits.sideinformation == null || side < 0 || side > 1 || side >= bunits.sideinformation.Length)
                {
                    return new TacticalBattleStateSignature(
                        activeUnitCount: 0,
                        side0ActiveForce: 0,
                        side1ActiveForce: 0,
                        side0MacroAI: -1,
                        side1MacroAI: -1,
                        anySideInRetreatOrEOD: false,
                        majorObjectiveAnchorHash: 0,
                        interruptedContactBucket: 0,
                        battleHourBucket: ComputeHourBucket(currentBattleHours));
                }

                var own = bunits.sideinformation[side];
                int ownForce = (int)Math.Max(0f, own != null ? own.totalactiveforce : 0f);
                int ownMacro = own != null ? own.macroai : -1;
                int enemyBucket = ArmyEvidenceBuilder.KnownEnemyStrengthBucket(snapshot.VisibleEnemyStrength);

                return new TacticalBattleStateSignature(
                    activeUnitCount: ownForce + enemyBucket, // coarse proxy (sufficient for signature diff)
                    side0ActiveForce: side == 0 ? ownForce : enemyBucket,
                    side1ActiveForce: side == 1 ? ownForce : enemyBucket,
                    side0MacroAI: side == 0 ? ownMacro : -1,
                    side1MacroAI: side == 1 ? ownMacro : -1,
                    anySideInRetreatOrEOD: ownMacro == 3 || bunits.eodcycle != 0,
                    majorObjectiveAnchorHash: ComputeCheapObjectiveHash(battle),
                    interruptedContactBucket: ArmyEvidenceBuilder.ContactBucket(snapshot),
                    battleHourBucket: ComputeHourBucket(currentBattleHours));
            }
            catch (Exception e)
            {
                OnceLog.Warning("tactical-orch:snapshot-builder",
                    "[TacticalOrchestrator] TacticalBattleSnapshotBuilder.ExtractCurrentSignature degraded: "
                    + e.GetType().Name + " " + e.Message);
                return new TacticalBattleStateSignature(
                    0, 0, 0, -1, -1, false, 0, 0, ComputeHourBucket(currentBattleHours));
            }
        }

        /// <summary>
        /// Heavy snapshot builder. Invoked only when TacticalHeavyPathGate returns Run.
        /// Derives the expensive data once from the side's battle snapshot and returns an
        /// immutable snapshot for reuse across DriveTickCycle (replan), DriveDirectChildCycle,
        /// DriveOperationsLedger, CommandDoctrineAssignment, etc.
        /// </summary>
        public static TacticalBattleRuntimeSnapshot Build(
            BattleSnapshot snapshot,
            AIBattle battle,
            TacticalBattleStateSignature signatureAtBuild,
            float buildBattleHours)
        {
            int allianceId = snapshot != null ? snapshot.Alliance : -1;
            try
            {
                if (snapshot == null) return TacticalBattleRuntimeSnapshot.Empty;

                // Per-sub-call scopes split the cost so the tuning sidecar shows which inner
                // derivation dominates the Build wall time.
                ArmyEvidenceBuilder.Bundle bundle;
                using (TelemetryPerf.Scope("tactical.snapshot-build.evidence-bundle", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
                {
                    bundle = ArmyEvidenceBuilder.Build(snapshot, battle);
                }

                ObjectiveRecord[] objectives;
                using (TelemetryPerf.Scope("tactical.snapshot-build.objective-records", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
                {
                    objectives = BuildObjectiveRecords(snapshot, battle);
                }

                var commandTree = CommandTreeRuntime.Snapshot(allianceId);
                IReadOnlyList<DirectChildSnapshot> directChildren = DirectChildDiscovery.Snapshot(allianceId);

                // The snapshot performs its own defensive copies. Readiness
                // fields (fatigue/ammo/reinforcement timing) flow from bundle
                // through the snapshot so the throttled DriveTickCycle path
                // gets the same data as the direct path.
                return new TacticalBattleRuntimeSnapshot(
                    signatureAtBuild,
                    buildBattleHours,
                    bundle.OwnEvidence,
                    bundle.EnemyVisible,
                    bundle.OwnMainEffortStrength,
                    bundle.OwnArmyMorale,
                    bundle.OwnReservesCommittedFraction,
                    bundle.OwnReinforcementsArriving,
                    objectives,
                    commandTree,
                    directChildren,
                    bundle.OwnAvgFatigue01,
                    bundle.OwnAvgAmmo01,
                    bundle.NearestReinforcementHours,
                    bundle.NearestReinforcementStrength);
            }
            catch (Exception e)
            {
                OnceLog.Warning("tactical-orch:snapshot-builder",
                    "[TacticalOrchestrator] TacticalBattleSnapshotBuilder.Build degraded for alliance=" + allianceId
                    + ": " + e.GetType().Name + " " + e.Message);
                // Degrade to Empty (coordinator may still use lastPublished or fall back to vanilla urgent recovery)
                return TacticalBattleRuntimeSnapshot.Empty;
            }
        }

        // Reused per build (main thread only).
        private static readonly List<TacticalVisionRuntimeAdapter.OwnSetObjective> _ownObjectives =
            new List<TacticalVisionRuntimeAdapter.OwnSetObjective>();

        /// <summary>
        /// Objective records over the side's snapshot (plan D11). The one live read is each own
        /// unit's Regiment.currentsetobjective, taken through the snapshot's own index
        /// (BattleSnapshotBuilder.UnitAt) because the snapshot does not carry it.
        /// </summary>
        internal static ObjectiveRecord[] BuildObjectiveRecords(BattleSnapshot snapshot, AIBattle battle)
        {
            if (snapshot == null) return Array.Empty<ObjectiveRecord>();
            _ownObjectives.Clear();
            for (int i = 0; i < snapshot.OwnCount; i++)
            {
                Regiment unit = BattleSnapshotBuilder.UnitAt(snapshot, i, enemy: false);
                if ((UnityEngine.Object)unit == null) continue;
                object objective = TacticalVisionRuntimeAdapter.SafeCurrentSetObjective(unit);
                if (objective == null) continue;
                _ownObjectives.Add(new TacticalVisionRuntimeAdapter.OwnSetObjective(i,
                    TacticalVisionRuntimeAdapter.SafeObjectiveName(objective),
                    TacticalVisionRuntimeAdapter.SafeObjectivePoint(objective)));
            }
            return TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromAggregate(snapshot, _ownObjectives, battle, snapshot.Alliance);
        }

        // ---- Internal cheap helpers (no heavy vision / sector iteration) ----

        private static BattleUnits ResolveBattleUnits(AIBattle battle)
        {
            try
            {
                if (battle == null) return null;
                if (_bunitsFieldCache == null)
                    _bunitsFieldCache = AccessTools.Field(typeof(AIBattle), "bunits");
                return _bunitsFieldCache?.GetValue(battle) as BattleUnits;
            }
            catch
            {
                return null;
            }
        }

        private static int ComputeHourBucket(float hours)
        {
            if (float.IsNaN(hours) || float.IsInfinity(hours) || hours < 0f) return 0;
            // Coarse 0.05 battle-hour (~3 real minutes at 1x) bucket for signature stability.
            // Bucket changes alone never trigger heavy via gate (time gate + SignatureEquals exclusion handle it).
            return (int)Math.Floor(hours / 0.05f);
        }

        private static int ComputeCheapObjectiveHash(AIBattle battle)
        {
            try
            {
                if (battle == null) return 0;
                var chain = battle.objectivechain;
                if (chain == null || chain.Count == 0) return 0;

                int hash = 17;
                int taken = 0;
                foreach (var oc in chain)
                {
                    if (taken >= 2) break;
                    if (oc?.objectives == null || oc.objectives.Count == 0) continue;
                    var first = oc.objectives[0];
                    if (first != null)
                    {
                        string name = first.objectivename ?? first.ToString() ?? string.Empty;
                        hash = hash * 31 + name.GetHashCode();
                        taken++;
                    }
                }
                return hash;
            }
            catch
            {
                return 0;
            }
        }
    }
}
