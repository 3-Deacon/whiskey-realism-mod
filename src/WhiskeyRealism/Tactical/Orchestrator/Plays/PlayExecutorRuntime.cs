using System;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    /// <summary>
    /// Runtime wrapper: reads vanilla positions, computes army+enemy centroids,
    /// projects the current play template, assigns corps to rects. Returns
    /// the resulting <see cref="CorpsAssignment"/> list ready to feed
    /// <see cref="ArmyOrchestrator.ObserveDirectChildEvidenceWithZones"/>.
    ///
    /// Excluded from the test csproj — touches BattleUnits / Regiment / Unity
    /// types. Pure logic lives in <see cref="PlayProjection"/> and
    /// <see cref="PlayCorpsAssignment"/>.
    ///
    /// Performance: per-side signature cache. Skips full recompute when the
    /// plan id + corps positions (bucketed to 50m) + enemy centroid bucket
    /// haven't changed. Reuses pooled per-side scratch lists to avoid
    /// per-tick allocation. Wrapped in TelemetryPerf.Scope per AGENTS.md
    /// diagnostic-schema policy.
    /// </summary>
    public static class PlayExecutorRuntime
    {
        // ---- Per-side cache (skip recompute when input signature unchanged) ----
        private const int SideCount = 2;
        private static readonly string[] _lastSignature = new string[SideCount];
        private static readonly IReadOnlyList<CorpsAssignment>[] _lastResult = new IReadOnlyList<CorpsAssignment>[SideCount];
        private static readonly float[] _lastComputeRealtimeSeconds = { -1f, -1f };
        private static readonly BattlePlanId[] _lastComputePlan = { BattlePlanId.Unknown, BattlePlanId.Unknown };
        /// <summary>Minimum real seconds between full recomputes on the same plan.</summary>
        private const float MinRecomputeIntervalSeconds = 3f;

        // ---- Pooled scratch buffers (per-side; cleared per call) ----
        private static readonly List<float>[] _ownXsScratch = { new List<float>(64), new List<float>(64) };
        private static readonly List<float>[] _ownZsScratch = { new List<float>(64), new List<float>(64) };
        private static readonly List<CorpsPosition>[] _corpsPosScratch = { new List<CorpsPosition>(16), new List<CorpsPosition>(16) };

        /// <summary>Position-bucket size (meters) for signature comparison.</summary>
        private const float PositionBucketMeters = 50f;

        /// <summary>
        /// Computes zone assignments for the given side. Returns null when:
        ///   - No play template for the current plan
        ///   - No own forces visible to compute a centroid
        ///   - Battle state unavailable
        /// Caller should fall through to non-zone allocation in that case.
        ///
        /// Skips full recompute when the per-side input signature is
        /// unchanged (cache hit). Wrapped in TelemetryPerf.Scope.
        /// </summary>
        /// <param name="frontAxis">The side's front axis from
        /// <see cref="EnemyVisibleState.FrontAxis"/>. The play faces the same
        /// way the sector bands do, toward what the side can SEE — never the
        /// omniscient centroid of every enemy command group on the map.</param>
        public static IReadOnlyList<CorpsAssignment> ComputeZoneAssignments(
            int allianceId,
            BattlePlanId planId,
            int commandHierarchyShift,
            int childCount,
            TacticalFrontAxis frontAxis)
        {
            using (TelemetryPerf.Scope("tactical.play-executor.zone-assignments",
                TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
            {
                return ComputeZoneAssignmentsCore(allianceId, planId, commandHierarchyShift, childCount, frontAxis);
            }
        }

        private static IReadOnlyList<CorpsAssignment> ComputeZoneAssignmentsCore(
            int allianceId,
            BattlePlanId planId,
            int commandHierarchyShift,
            int childCount,
            TacticalFrontAxis frontAxis)
        {
            try
            {
                if (planId == BattlePlanId.Unknown) return null;
                if (allianceId < 0 || allianceId >= SideCount) return null;
                var template = PlayTemplateCatalog.Lookup(planId);
                if (template == null) return null;

                // CHEAP pre-gate: if we have a cached result for this plan
                // AND we computed within the last MinRecomputeIntervalSeconds,
                // return the cache without walking BattleUnits. Armies don't
                // move dramatically in <3s and the signature check below would
                // very likely produce the same answer.
                float nowReal = System.Environment.TickCount * 0.001f;
                var cachedEarly = _lastResult[allianceId];
                if (cachedEarly != null
                    && _lastComputePlan[allianceId] == planId
                    && _lastComputeRealtimeSeconds[allianceId] > 0f
                    && nowReal - _lastComputeRealtimeSeconds[allianceId] < MinRecomputeIntervalSeconds)
                {
                    return cachedEarly;
                }

                var units = BattleUnits.completeunitlist as System.Collections.IList;
                if (units == null || units.Count == 0) return null;
                int effectiveCommandMin = TacticalUnitType.MaxCombat + 1 + commandHierarchyShift;
                if (effectiveCommandMin < 1) effectiveCommandMin = 1;
                if (effectiveCommandMin > 18) effectiveCommandMin = 18;

                // Pooled scratch — cleared per call instead of allocated.
                var ownXs = _ownXsScratch[allianceId]; ownXs.Clear();
                var ownZs = _ownZsScratch[allianceId]; ownZs.Clear();
                var corpsPositions = _corpsPosScratch[allianceId]; corpsPositions.Clear();

                for (int i = 0; i < units.Count; i++)
                {
                    var reg = units[i] as Regiment;
                    if (reg == null) continue;
                    var regGo = ((Component)reg).gameObject;
                    if (regGo == null) continue;
                    if (!regGo.activeInHierarchy) continue;
                    float x = 0f, z = 0f;
                    try
                    {
                        var pos = regGo.transform != null ? regGo.transform.position : default(Vector3);
                        x = pos.x;
                        z = pos.z;
                    }
                    catch { continue; }
                    if (float.IsNaN(x) || float.IsInfinity(x)) continue;
                    if (float.IsNaN(z) || float.IsInfinity(z)) continue;

                    if (reg.alliance == allianceId)
                    {
                        // Own forces: collect all active brigades for centroid;
                        // also collect command-level (corps/army) instances as
                        // candidates for rect assignment.
                        ownXs.Add(x);
                        ownZs.Add(z);
                        if (reg.unittyp >= effectiveCommandMin)
                        {
                            corpsPositions.Add(new CorpsPosition(
                                "child-" + regGo.GetInstanceID(), x, z));
                        }
                    }
                }

                if (corpsPositions.Count == 0)
                {
                    return null;
                }

                PlayProjection.ComputeCentroid(ownXs, ownZs, out float armyX, out float armyZ, out int ownN);
                if (ownN == 0) return null;

                // Face the side's front axis (visible enemy, else nearest open
                // objective, else last heading). Without an axis, anchor the
                // target forward along +Z so the projection stays coherent.
                float engagement = frontAxis.IsValid && frontAxis.TargetDistance >= 1f
                    ? frontAxis.TargetDistance
                    : PlayProjection.DefaultEngagementDistanceMeters;
                float enemyX = armyX + (frontAxis.IsValid ? frontAxis.ForwardX : 0f) * engagement;
                float enemyZ = armyZ + (frontAxis.IsValid ? frontAxis.ForwardZ : 1f) * engagement;

                // ---- Signature-skip ----
                // Build a per-side signature from plan + bucketed centroids +
                // bucketed corps positions. If unchanged since last call, return
                // the cached result without recomputing projection/assignment.
                string sig = BuildSignature(planId, armyX, armyZ, enemyX, enemyZ, corpsPositions);
                string lastSig = _lastSignature[allianceId];
                var cached = _lastResult[allianceId];
                if (cached != null && string.Equals(sig, lastSig, StringComparison.Ordinal))
                {
                    return cached;
                }

                float dx = enemyX - armyX;
                float dz = enemyZ - armyZ;
                float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                float forwardX = dist > 0.001f ? dx / dist : 0f;
                float forwardZ = dist > 0.001f ? dz / dist : 1f;

                float frontage = PlayProjection.EstimateFrontage(ownXs, ownZs, forwardX, forwardZ);

                var worldRects = PlayProjection.Project(template, armyX, armyZ, enemyX, enemyZ, frontage);

                // Objectives are not currently surfaced to this layer; pass
                // empty so PlayCorpsAssignment falls back to closest-corps.
                // Stage 2.1 follow-up: thread BattlefieldObjectiveEstimate +
                // ObjectivePosition for VP-override behavior.
                var objectives = Array.Empty<ObjectivePosition>();
                var assignments = PlayCorpsAssignment.Assign(worldRects, corpsPositions, objectives);

                _lastSignature[allianceId] = sig;
                _lastResult[allianceId] = assignments;
                _lastComputeRealtimeSeconds[allianceId] = nowReal;
                _lastComputePlan[allianceId] = planId;
                EmitPlayExecutionTelemetry(allianceId, planId, template, assignments, armyX, armyZ, enemyX, enemyZ, frontage);
                return assignments;
            }
            catch (Exception e)
            {
                OnceLog.Warning("play-executor-runtime:exception",
                    "PlayExecutorRuntime.ComputeZoneAssignments failed: "
                    + e.GetType().Name + " " + e.Message);
                return null;
            }
        }

        private static string BuildSignature(
            BattlePlanId planId,
            float armyX, float armyZ,
            float enemyX, float enemyZ,
            List<CorpsPosition> corpsPositions)
        {
            // Bucket positions to PositionBucketMeters so jitter doesn't
            // invalidate the cache. Stable ordering: sort corps by ChildId.
            var sb = new System.Text.StringBuilder();
            sb.Append((int)planId).Append('|');
            sb.Append((int)(armyX / PositionBucketMeters)).Append(',').Append((int)(armyZ / PositionBucketMeters)).Append('|');
            sb.Append((int)(enemyX / PositionBucketMeters)).Append(',').Append((int)(enemyZ / PositionBucketMeters)).Append('|');
            // Sort corps for stable ordering.
            corpsPositions.Sort((a, b) => string.CompareOrdinal(a.ChildId, b.ChildId));
            for (int i = 0; i < corpsPositions.Count; i++)
            {
                var p = corpsPositions[i];
                sb.Append(p.ChildId).Append(':')
                  .Append((int)(p.WorldX / PositionBucketMeters)).Append(',')
                  .Append((int)(p.WorldZ / PositionBucketMeters)).Append(';');
            }
            return sb.ToString();
        }

        // ---- TacticalPlayExecution telemetry ----
        // Emits when zone assignments change (signature-skip already gates
        // calls; this fires once per assignment change). Per-side debounce.
        private static readonly Dictionary<string, float> _lastPlayExecutionTelemetryAt = new Dictionary<string, float>();
        private const float PlayExecutionTelemetrySeconds = 10f;

        private static void EmitPlayExecutionTelemetry(
            int allianceId,
            BattlePlanId planId,
            PlayTemplate template,
            IReadOnlyList<CorpsAssignment> assignments,
            float armyX, float armyZ,
            float enemyX, float enemyZ,
            float frontage)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < assignments.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(assignments[i].ChildId).Append('=').Append(assignments[i].RectKind);
                }
                string sig = "side=" + allianceId + "|plan=" + planId + "|tpl=" + template.Name
                    + "|assigned=" + assignments.Count + "/" + template.Rects.Count
                    + "|" + sb;
                float nowSeconds = System.Environment.TickCount * 0.001f;
                string key = "play-execution:" + allianceId + ":" + planId;
                if (!WhiskeyRealism.Tactical.TacticalTelemetry.ShouldEmit(
                        _lastPlayExecutionTelemetryAt, key, sig, nowSeconds,
                        PlayExecutionTelemetrySeconds, false))
                    return;

                string detail = sb.ToString();
                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Decision,
                    "TacticalPlayExecution",
                    TelemetrySeverity.Info,
                    ev => ev
                        .WithDecision("TacticalPlayExecution", "summary", sig)
                        .WithField("side", allianceId)
                        .WithField("plan", planId.ToString())
                        .WithField("template", template.Name)
                        .WithField("rectCount", template.Rects.Count)
                        .WithField("assignedCount", assignments.Count)
                        .WithField("armyCentroidX", armyX)
                        .WithField("armyCentroidZ", armyZ)
                        .WithField("enemyCentroidX", enemyX)
                        .WithField("enemyCentroidZ", enemyZ)
                        .WithField("frontage", frontage)
                        .WithField("assignments", detail));
            }
            catch { }
        }

        /// <summary>Test seam: clears per-side caches.</summary>
        public static void ResetCacheForTest()
        {
            for (int i = 0; i < SideCount; i++)
            {
                _lastSignature[i] = null;
                _lastResult[i] = null;
                _lastComputeRealtimeSeconds[i] = -1f;
                _lastComputePlan[i] = BattlePlanId.Unknown;
            }
        }
    }
}
