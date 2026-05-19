using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Telemetry;
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

        // Parity window state (Tasks 10/11 — single-pass refactor).
        // Keyed by (battleSequence, allianceId) so each side gets its own 20-clean
        // budget; mismatch on one side does not shut down the other.
        private static readonly Dictionary<ParityKey, int> _parityComparesRemaining
            = new Dictionary<ParityKey, int>();
        private static readonly HashSet<ParityKey> _parityMismatchObserved
            = new HashSet<ParityKey>();
        private const int ParityCompareBudget = 20;

        private readonly struct ParityKey : IEquatable<ParityKey>
        {
            public readonly int BattleSequence;
            public readonly int AllianceId;
            public ParityKey(int battleSequence, int allianceId)
            {
                BattleSequence = battleSequence;
                AllianceId = allianceId;
            }
            public bool Equals(ParityKey other)
            {
                return BattleSequence == other.BattleSequence && AllianceId == other.AllianceId;
            }
            public override bool Equals(object obj)
            {
                return obj is ParityKey other && Equals(other);
            }
            public override int GetHashCode()
            {
                unchecked { return (BattleSequence * 397) ^ AllianceId; }
            }
        }

        /// <summary>
        /// Called from TacticalBattleCoordinator.ResetRuntimeTickState on battle
        /// end so the next battle's parity window starts fresh. Public so the
        /// coordinator can reach it across files.
        /// </summary>
        public static void ClearParityState()
        {
            _parityComparesRemaining.Clear();
            _parityMismatchObserved.Clear();
        }

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
                // Expensive calls — this is the throttled path. Per-sub-call
                // scopes (added 2026-05-19 hot-path diagnostic) split the cost
                // so the tuning sidecar shows which inner walk dominates the
                // 5-16ms p99 Build wall time.
                ArmyEvidenceBuilder.Bundle bundle;
                using (TelemetryPerf.Scope("tactical.snapshot-build.evidence-bundle", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
                {
                    bundle = ArmyEvidenceBuilder.Build(battle, allianceId);
                }

                ObjectiveRecord[] objectives;
                using (TelemetryPerf.Scope("tactical.snapshot-build.objective-records", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
                {
                    objectives = BuildObjectiveRecordsWithParityWindow(battle, allianceId);
                }

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

        // ---- Objective-records parity window (Tasks 10/11 — single-pass refactor) ----

        /// <summary>
        /// Branches on EnableSinglePassObjectiveRecords:
        ///   flag OFF  — legacy path only, .legacy scope reports the cost.
        ///   flag ON, key in window (budget > 0 or mismatch already seen):
        ///              run both, compare, emit TacticalObjectiveRecordsParityMismatch on
        ///              diff, return legacy (safety: never publish drifted output).
        ///   flag ON, key past window (20 clean compares, no mismatch):
        ///              aggregate-only; .aggregate scope is the success-metric scope.
        /// </summary>
        private static ObjectiveRecord[] BuildObjectiveRecordsWithParityWindow(AIBattle battle, int allianceId)
        {
            bool flagOn = Plugin.Instance != null
                && Plugin.EnableSinglePassObjectiveRecords != null
                && Plugin.EnableSinglePassObjectiveRecords.Value;

            if (!flagOn)
            {
                // Rollback path — legacy only, isolated measurement.
                ObjectiveRecord[] legacyOnly;
                using (TelemetryPerf.Scope("tactical.snapshot-build.objective-records.legacy", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
                {
                    legacyOnly = TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromBattle(battle, allianceId);
                }
                return legacyOnly;
            }

            int battleSeq = TacticalBattleCoordinator.BattleSequenceForParity;
            var key = new ParityKey(battleSeq, allianceId);

            bool inWindow = false;
            if (!_parityComparesRemaining.TryGetValue(key, out int remaining)) remaining = ParityCompareBudget;
            if (remaining > 0) inWindow = true;
            if (_parityMismatchObserved.Contains(key)) inWindow = true;

            IObservationSource source;
            using (TelemetryPerf.Scope("tactical.snapshot-build.aggregate-capture", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
            {
                source = TacticalUnitObservationAggregate.Shared.Capture(allianceId);
            }

            if (!inWindow)
            {
                // Post-window steady-state — aggregate only. This is the success-metric scope.
                ObjectiveRecord[] aggregateOnly;
                using (TelemetryPerf.Scope("tactical.snapshot-build.objective-records.aggregate", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
                {
                    aggregateOnly = TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromAggregate(source, battle, allianceId);
                }
                return aggregateOnly;
            }

            // Parity window: run both, compare, emit mismatch on diff. Never poison the
            // outer p99 metric by leaving inner scopes off — both inner paths get their
            // own scopes so the .aggregate scope reflects only aggregate cost.
            ObjectiveRecord[] legacyResult;
            using (TelemetryPerf.Scope("tactical.snapshot-build.objective-records.legacy", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
            {
                legacyResult = TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromBattle(battle, allianceId);
            }
            ObjectiveRecord[] aggregateResult;
            using (TelemetryPerf.Scope("tactical.snapshot-build.objective-records.aggregate", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
            {
                aggregateResult = TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromAggregate(source, battle, allianceId);
            }

            var diff = ObjectiveRecordsEqual(legacyResult, aggregateResult);
            if (!diff.IsEqual)
            {
                _parityMismatchObserved.Add(key);
                EmitParityMismatch(battleSeq, allianceId, legacyResult, aggregateResult, diff);
                return legacyResult;  // safety: never publish drifted output
            }

            _parityComparesRemaining[key] = remaining - 1;
            return aggregateResult;
        }

        /// <summary>
        /// Carries the first-diverging-field detail for a parity comparison.
        /// IsEqual == true means arrays are identical; all other fields are empty/default.
        /// </summary>
        private readonly struct ParityDiff
        {
            public readonly bool IsEqual;
            public readonly int FirstDifferingIndex;     // -1 when equal or count differs
            public readonly string FirstDifferingField;  // "" when equal or count differs
            public readonly string LegacyValue;          // safe-truncated string repr
            public readonly string AggregateValue;       // safe-truncated string repr

            public ParityDiff(bool isEqual, int idx, string field, string legacyVal, string aggregateVal)
            {
                IsEqual = isEqual;
                FirstDifferingIndex = idx;
                FirstDifferingField = field ?? string.Empty;
                LegacyValue = legacyVal ?? string.Empty;
                AggregateValue = aggregateVal ?? string.Empty;
            }

            public static ParityDiff Equal => new ParityDiff(true, -1, string.Empty, string.Empty, string.Empty);
            public static ParityDiff CountDifference => new ParityDiff(false, -1, "Length", string.Empty, string.Empty);
        }

        // Truncates a string to 64 chars for safe telemetry payload.
        private static string Trunc(string s)
        {
            if (s == null) return "(null)";
            return s.Length > 64 ? s.Substring(0, 64) : s;
        }

        private static ParityDiff ObjectiveRecordsEqual(ObjectiveRecord[] a, ObjectiveRecord[] b)
        {
            if (ReferenceEquals(a, b)) return ParityDiff.Equal;
            if (a == null || b == null) return ParityDiff.CountDifference;
            if (a.Length != b.Length) return ParityDiff.CountDifference;
            for (int i = 0; i < a.Length; i++)
            {
                var itemDiff = ObjectiveRecordEqualsItem(a[i], b[i], i);
                if (!itemDiff.IsEqual) return itemDiff;
            }
            return ParityDiff.Equal;
        }

        /// <summary>
        /// Compares every field of two ObjectiveRecord values that could diverge
        /// between the legacy (BuildObjectiveRecordsFromBattle) and aggregate
        /// (BuildObjectiveRecordsFromAggregate) paths. Returns a ParityDiff naming
        /// the first field that differs so smoke sessions can identify the root cause.
        ///
        /// Fields compared (exhaustive per ObjectiveRecord struct definition):
        ///   Observation.ObjectiveId       — string, ordinal
        ///   Observation.Type              — enum
        ///   Observation.Source            — enum
        ///   Observation.Location.X/Z      — float, exact
        ///   Observation.SourceConfidence  — float, exact (both paths read same regiment fields)
        ///   Observation.Value             — float, exact
        ///   Observation.TypeAnchorVerified — bool
        ///   Observation.ApproachAvenue.HasAvenue  — bool (distinguishes None from populated)
        ///   Observation.ApproachAvenue.AxisX/Z    — float, exact (approach bearing)
        ///   Observation.ApproachAvenue.Origin.X/Z — float, exact
        ///   Observation.ApproachAvenue.Objective.X/Z — float, exact
        ///   Observation.ApproachAvenue.Source      — enum
        ///   Observation.ApproachAvenue.Confidence01 — float, exact
        ///   Observation.ApproachAvenue.RoadAnchored   — bool
        ///   Observation.ApproachAvenue.CrossingAnchored — bool
        ///   Observation.ApproachAvenue.Reason — string, ordinal
        ///   Status                        — enum
        ///   EnemyStrength                 — float, exact (sanitized via SanitizeFloorZero; same inputs)
        ///   FriendlyAssignedStrength      — float, exact
        ///   HasUsableStrengthEvidence     — bool (derived from the two floats above)
        /// </summary>
        private static ParityDiff ObjectiveRecordEqualsItem(ObjectiveRecord x, ObjectiveRecord y, int recordIndex)
        {
            // Observation identity
            var xo = x.Observation;
            var yo = y.Observation;
            if (!string.Equals(xo.ObjectiveId, yo.ObjectiveId, StringComparison.Ordinal))
                return new ParityDiff(false, recordIndex, "ObjectiveId", Trunc(xo.ObjectiveId), Trunc(yo.ObjectiveId));
            if (xo.Type != yo.Type)
                return new ParityDiff(false, recordIndex, "Type", xo.Type.ToString(), yo.Type.ToString());
            if (xo.Source != yo.Source)
                return new ParityDiff(false, recordIndex, "Source", xo.Source.ToString(), yo.Source.ToString());
            if (xo.Location.X != yo.Location.X)
                return new ParityDiff(false, recordIndex, "Location.X", xo.Location.X.ToString("R"), yo.Location.X.ToString("R"));
            if (xo.Location.Z != yo.Location.Z)
                return new ParityDiff(false, recordIndex, "Location.Z", xo.Location.Z.ToString("R"), yo.Location.Z.ToString("R"));
            if (xo.SourceConfidence != yo.SourceConfidence)
                return new ParityDiff(false, recordIndex, "SourceConfidence", xo.SourceConfidence.ToString("R"), yo.SourceConfidence.ToString("R"));
            if (xo.Value != yo.Value)
                return new ParityDiff(false, recordIndex, "Value", xo.Value.ToString("R"), yo.Value.ToString("R"));
            if (xo.TypeAnchorVerified != yo.TypeAnchorVerified)
                return new ParityDiff(false, recordIndex, "TypeAnchorVerified", xo.TypeAnchorVerified.ToString(), yo.TypeAnchorVerified.ToString());

            // Approach avenue (nested struct)
            var xa = xo.ApproachAvenue;
            var ya = yo.ApproachAvenue;
            if (xa.HasAvenue != ya.HasAvenue)
                return new ParityDiff(false, recordIndex, "ApproachAvenue.HasAvenue", xa.HasAvenue.ToString(), ya.HasAvenue.ToString());
            if (xa.HasAvenue) // only compare populated fields when both have an avenue
            {
                if (xa.Origin.X != ya.Origin.X)
                    return new ParityDiff(false, recordIndex, "ApproachAvenue.Origin.X", xa.Origin.X.ToString("R"), ya.Origin.X.ToString("R"));
                if (xa.Origin.Z != ya.Origin.Z)
                    return new ParityDiff(false, recordIndex, "ApproachAvenue.Origin.Z", xa.Origin.Z.ToString("R"), ya.Origin.Z.ToString("R"));
                if (xa.Objective.X != ya.Objective.X)
                    return new ParityDiff(false, recordIndex, "ApproachAvenue.Objective.X", xa.Objective.X.ToString("R"), ya.Objective.X.ToString("R"));
                if (xa.Objective.Z != ya.Objective.Z)
                    return new ParityDiff(false, recordIndex, "ApproachAvenue.Objective.Z", xa.Objective.Z.ToString("R"), ya.Objective.Z.ToString("R"));
                if (xa.AxisX != ya.AxisX)
                    return new ParityDiff(false, recordIndex, "ApproachAvenue.AxisX", xa.AxisX.ToString("R"), ya.AxisX.ToString("R"));
                if (xa.AxisZ != ya.AxisZ)
                    return new ParityDiff(false, recordIndex, "ApproachAvenue.AxisZ", xa.AxisZ.ToString("R"), ya.AxisZ.ToString("R"));
                if (xa.Source != ya.Source)
                    return new ParityDiff(false, recordIndex, "ApproachAvenue.Source", xa.Source.ToString(), ya.Source.ToString());
                if (xa.Confidence01 != ya.Confidence01)
                    return new ParityDiff(false, recordIndex, "ApproachAvenue.Confidence01", xa.Confidence01.ToString("R"), ya.Confidence01.ToString("R"));
                if (xa.RoadAnchored != ya.RoadAnchored)
                    return new ParityDiff(false, recordIndex, "ApproachAvenue.RoadAnchored", xa.RoadAnchored.ToString(), ya.RoadAnchored.ToString());
                if (xa.CrossingAnchored != ya.CrossingAnchored)
                    return new ParityDiff(false, recordIndex, "ApproachAvenue.CrossingAnchored", xa.CrossingAnchored.ToString(), ya.CrossingAnchored.ToString());
                if (!string.Equals(xa.Reason, ya.Reason, StringComparison.Ordinal))
                    return new ParityDiff(false, recordIndex, "ApproachAvenue.Reason", Trunc(xa.Reason), Trunc(ya.Reason));
            }

            // Derived fields — most likely to diverge if aggregate misses a unit
            if (x.Status != y.Status)
                return new ParityDiff(false, recordIndex, "Status", x.Status.ToString(), y.Status.ToString());
            if (x.EnemyStrength != y.EnemyStrength)
                return new ParityDiff(false, recordIndex, "EnemyStrength", x.EnemyStrength.ToString("R"), y.EnemyStrength.ToString("R"));
            if (x.FriendlyAssignedStrength != y.FriendlyAssignedStrength)
                return new ParityDiff(false, recordIndex, "FriendlyAssignedStrength", x.FriendlyAssignedStrength.ToString("R"), y.FriendlyAssignedStrength.ToString("R"));
            if (x.HasUsableStrengthEvidence != y.HasUsableStrengthEvidence)
                return new ParityDiff(false, recordIndex, "HasUsableStrengthEvidence", x.HasUsableStrengthEvidence.ToString(), y.HasUsableStrengthEvidence.ToString());

            return ParityDiff.Equal;
        }

        private static void EmitParityMismatch(
            int battleSeq,
            int allianceId,
            ObjectiveRecord[] legacy,
            ObjectiveRecord[] aggregate,
            ParityDiff diff)
        {
            try
            {
                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Gate,
                    "TacticalObjectiveRecordsParityMismatch",
                    TelemetrySeverity.Warning,
                    ev => ev
                        .WithSide(allianceId)
                        .WithDecision("mismatch", "objective-records", "battleSeq=" + battleSeq + "|alliance=" + allianceId)
                        .WithField("legacyCount", legacy?.Length ?? -1)
                        .WithField("aggregateCount", aggregate?.Length ?? -1)
                        .WithField("battleSequence", battleSeq)
                        .WithField("allianceId", allianceId)
                        .WithField("diffIndex", diff.FirstDifferingIndex)
                        .WithField("diffField", diff.FirstDifferingField)
                        .WithField("legacyValue", diff.LegacyValue)
                        .WithField("aggregateValue", diff.AggregateValue));
            }
            catch { }
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
