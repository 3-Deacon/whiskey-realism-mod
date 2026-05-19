using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Runtime-only adapter (Task 5). The single place responsible for performing
    /// expensive heavy-path construction of <see cref="TacticalBattleRuntimeSnapshot"/>
    /// (ArmyEvidence + full EnemyVisible sectors + TacticalVision objective records with
    /// approach avenues + CommandTreeSnapshot + DirectChildSnapshots).
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
    /// - compute currentSig = TacticalBattleSnapshotBuilder.ExtractCurrentSignature(...) cheaply
    /// - read per-side lastHeavyHours, lastSig, hasPending, lastPublished
    /// - call TacticalHeavyPathGate.Decide(new Input(...))
    /// - on Decision.ShouldRun: snapshot = TacticalBattleSnapshotBuilder.Build(battle, alliance, currentSig, currentHours)
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
        /// Cheap signature extractor. Reads only coarse totals, macroai, eodcycle, limited
        /// objective names for hash, a bounded scan for pathinterrupted flags, and time bucket.
        /// Does NOT iterate for per-sector visible enemy strength or full objective+avenue records.
        /// Safe to call every vanilla side-stat tick.
        /// </summary>
        public static TacticalBattleStateSignature ExtractCurrentSignature(
            AIBattle battle,
            float currentBattleHours)
        {
            try
            {
                var bunits = ResolveBattleUnits(battle);
                if (bunits == null || bunits.sideinformation == null || bunits.sideinformation.Length < 2)
                {
                    return new TacticalBattleStateSignature(
                        activeUnitCount: 0,
                        side0ActiveForce: 0,
                        side1ActiveForce: 0,
                        side0MacroAI: -1,
                        side1MacroAI: -1,
                        anySideInRetreatOrEOD: false,
                        majorObjectiveAnchorHash: 0,
                        anyInterruptedPathsOrNewContact: false,
                        battleHourBucket: ComputeHourBucket(currentBattleHours));
                }

                var s0 = bunits.sideinformation[0];
                var s1 = bunits.sideinformation[1];

                int side0Force = (int)Math.Max(0f, s0 != null ? s0.totalactiveforce : 0f);
                int side1Force = (int)Math.Max(0f, s1 != null ? s1.totalactiveforce : 0f);
                int activeUnits = side0Force + side1Force; // coarse proxy (sufficient for signature diff)

                int macro0 = s0 != null ? s0.macroai : -1;
                int macro1 = s1 != null ? s1.macroai : -1;

                bool anyRetreatOrEod = (macro0 == 3 || macro1 == 3) || (bunits.eodcycle != 0);

                int objHash = ComputeCheapObjectiveHash(battle);

                bool anyInterrupted = ComputeAnyPathInterruptedOrNewContact(bunits);

                int bucket = ComputeHourBucket(currentBattleHours);

                return new TacticalBattleStateSignature(
                    activeUnitCount: Math.Max(0, activeUnits),
                    side0ActiveForce: Math.Max(0, side0Force),
                    side1ActiveForce: Math.Max(0, side1Force),
                    side0MacroAI: macro0,
                    side1MacroAI: macro1,
                    anySideInRetreatOrEOD: anyRetreatOrEod,
                    majorObjectiveAnchorHash: objHash,
                    anyInterruptedPathsOrNewContact: anyInterrupted,
                    battleHourBucket: bucket);
            }
            catch (Exception e)
            {
                OnceLog.Warning("tactical-orch:snapshot-builder",
                    "[TacticalOrchestrator] TacticalBattleSnapshotBuilder.ExtractCurrentSignature degraded: "
                    + e.GetType().Name + " " + e.Message);
                return new TacticalBattleStateSignature(
                    0, 0, 0, -1, -1, false, 0, false, ComputeHourBucket(currentBattleHours));
            }
        }

        /// <summary>
        /// Heavy snapshot builder. Invoked only when TacticalHeavyPathGate returns Run.
        /// Gathers the full expensive data once and returns an immutable snapshot for reuse
        /// across DriveTickCycle (replan), DriveDirectChildCycle, DriveOperationsLedger,
        /// CommandDoctrineAssignment, etc.
        /// </summary>
        public static TacticalBattleRuntimeSnapshot Build(
            AIBattle battle,
            int allianceId,
            TacticalBattleStateSignature signatureAtBuild,
            float buildBattleHours)
        {
            try
            {
                // Expensive calls — this is the throttled path
                var bundle = ArmyEvidenceBuilder.Build(battle, allianceId);

                var objectives = TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromBattle(battle, allianceId);

                var commandTree = CommandTreeRuntime.Snapshot(allianceId);

                // Direct children discovery (uses completeunitlist + GetAttachedUnitsReg)
                IReadOnlyList<DirectChildSnapshot> directChildren = DirectChildDiscovery.Snapshot(battle);

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
                    bundle.ReinforcementsArrivingDelta,
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

        private static bool ComputeAnyPathInterruptedOrNewContact(BattleUnits bunits)
        {
            try
            {
                if (bunits == null) return false;
                var units = BattleUnits.completeunitlist as IList;
                if (units == null || units.Count == 0) return false;

                // Bounded scan (cheap path) — first 40 units sufficient for change detection
                int limit = Math.Min(units.Count, 40);
                for (int i = 0; i < limit; i++)
                {
                    var reg = units[i] as Regiment;
                    if (reg != null && (reg.pathinterrupted || reg.groupenemiesinrange > 0))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
