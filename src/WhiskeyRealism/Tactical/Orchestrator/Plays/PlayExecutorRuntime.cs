using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    /// <summary>
    /// Runtime wrapper: reads the side's battle snapshot, computes the army
    /// centroid and the enemy anchor, projects the current play template and
    /// assigns corps to rects. Returns the resulting <see cref="CorpsAssignment"/>
    /// list ready to feed <see cref="ArmyOrchestrator.ObserveDirectChildEvidenceWithZones"/>.
    /// The play is not a mover: its effect reaches the battle as ledger roles
    /// and tasks, which the posture producer executes (plan BC5a).
    ///
    /// Excluded from the test csproj (it reads the snapshot registry and the
    /// doctrine clock). Pure logic lives in <see cref="PlayAnchor"/>,
    /// <see cref="PlayProjection"/> and <see cref="PlayCorpsAssignment"/>.
    /// BC5a: own positions come from the snapshot's own records instead of
    /// BattleUnits.completeunitlist, the enemy anchor from the enemies the side
    /// sees (fog-honest, spec §2), and the recompute gate runs on the doctrine
    /// clock instead of Environment.TickCount.
    ///
    /// Performance: per-side signature cache. Skips full recompute when the
    /// plan id + corps positions (bucketed to 50m) + enemy anchor bucket
    /// haven't changed. Reuses pooled per-side scratch lists to avoid
    /// per-tick allocation. Wrapped in TelemetryPerf.Scope per AGENTS.md
    /// diagnostic-schema policy.
    /// </summary>
    public static class PlayExecutorRuntime
    {
        static PlayExecutorRuntime()
        {
            BattleScope.Register("play-executor", ResetCache);
        }

        // ---- Per-side cache (skip recompute when input signature unchanged) ----
        private const int SideCount = 2;
        private static readonly string[] _lastSignature = new string[SideCount];
        private static readonly IReadOnlyList<CorpsAssignment>[] _lastResult = new IReadOnlyList<CorpsAssignment>[SideCount];
        private static readonly double[] _lastComputeHours = { -1.0, -1.0 };
        private static readonly BattlePlanId[] _lastComputePlan = { BattlePlanId.Unknown, BattlePlanId.Unknown };
        private static readonly bool[] _lastComputeMirrored = new bool[SideCount];

        // ---- Pooled scratch buffers (per-side; cleared per call) ----
        private static readonly List<float>[] _ownXsScratch = { new List<float>(64), new List<float>(64) };
        private static readonly List<float>[] _ownZsScratch = { new List<float>(64), new List<float>(64) };
        private static readonly List<CorpsPosition>[] _corpsPosScratch = { new List<CorpsPosition>(16), new List<CorpsPosition>(16) };
        private static readonly HashSet<string>[] _childIdScratch =
            { new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal) };

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
        /// <param name="frontAxis">The side's front axis, the play's heading when
        /// the side sees no enemy (PlayAnchor.EnemyAnchor).</param>
        /// <param name="directChildIds">The side's direct children ("child-&lt;id&gt;"): only they are offered a rect (BC11).</param>
        /// <param name="mirrored">BC14 requirement-selected-side: the plan's published side. The template is projected mirrored, in the frame the
        /// feasibility evaluation used, so each rect goes to the child the evaluation timed.</param>
        public static IReadOnlyList<CorpsAssignment> ComputeZoneAssignments(
            int allianceId,
            BattlePlanId planId,
            int commandHierarchyShift,
            int childCount,
            TacticalFrontAxis frontAxis,
            IReadOnlyList<string> directChildIds,
            bool mirrored)
        {
            using (TelemetryPerf.Scope("tactical.play-executor.zone-assignments",
                TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
            {
                return ComputeZoneAssignmentsCore(allianceId, planId, commandHierarchyShift, childCount, frontAxis, directChildIds, mirrored);
            }
        }

        private static IReadOnlyList<CorpsAssignment> ComputeZoneAssignmentsCore(
            int allianceId,
            BattlePlanId planId,
            int commandHierarchyShift,
            int childCount,
            TacticalFrontAxis frontAxis,
            IReadOnlyList<string> directChildIds,
            bool mirrored)
        {
            try
            {
                if (planId == BattlePlanId.Unknown) return null;
                if (allianceId < 0 || allianceId >= SideCount) return null;
                var template = PlayTemplateCatalog.Lookup(planId);
                if (template == null) return null;

                // Cheap pre-gate on the doctrine clock: inside the window on the
                // same plan, reuse the cache without reading the snapshot.
                double now = TacticalDoctrineTime.NowBattleHours;
                var cachedEarly = _lastResult[allianceId];
                if (!PlayAnchor.RecomputeDue(cachedEarly != null, _lastComputePlan[allianceId] == planId && _lastComputeMirrored[allianceId] == mirrored,
                        _lastComputeHours[allianceId], now))
                    return cachedEarly;

                if (!BattleCore.TryGetSnapshot(allianceId, out BattleSnapshot snapshot)) return null;
                int effectiveCommandMin = TacticalUnitType.MaxCombat + 1 + commandHierarchyShift;
                if (effectiveCommandMin < 1) effectiveCommandMin = 1;
                if (effectiveCommandMin > 18) effectiveCommandMin = 18;

                // Pooled scratch — cleared per call instead of allocated.
                var ownXs = _ownXsScratch[allianceId]; ownXs.Clear();
                var ownZs = _ownZsScratch[allianceId]; ownZs.Clear();
                var corpsPositions = _corpsPosScratch[allianceId]; corpsPositions.Clear();
                var childIdSet = _childIdScratch[allianceId];
                childIdSet.Clear();
                if (directChildIds != null)
                    for (int c = 0; c < directChildIds.Count; c++)
                        if (!string.IsNullOrEmpty(directChildIds[c])) childIdSet.Add(directChildIds[c]);
                if (PlayAnchor.CollectOwn(snapshot, effectiveCommandMin, ownXs, ownZs, corpsPositions, childIdSet) == 0) return null;

                PlayProjection.ComputeCentroid(ownXs, ownZs, out float armyX, out float armyZ, out int ownN);
                if (ownN == 0) return null;
                PlayAnchor.EnemyAnchor(snapshot, frontAxis, armyX, armyZ, out float enemyX, out float enemyZ);

                // ---- Signature-skip ----
                // Build a per-side signature from plan + bucketed centroids +
                // bucketed corps positions. If unchanged since last call, return
                // the cached result without recomputing projection/assignment.
                string sig = BuildSignature(planId, mirrored, armyX, armyZ, enemyX, enemyZ, corpsPositions);
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

                // BC12: ReverseSlopeDefense is anchored to the crest the terrain picture found across the front.
                TerrainLine crest = planId == BattlePlanId.ReverseSlopeDefense
                    ? ArmyEvidenceBuilder.CrestLineAcrossFront(TerrainPicture.CurrentForPlanning(), frontAxis)
                    : default(TerrainLine);
                var worldRects = PlayProjection.Project(template, armyX, armyZ, enemyX, enemyZ, frontage, in crest, mirrored);

                // Objectives are not currently surfaced to this layer; pass
                // empty so PlayCorpsAssignment falls back to closest-corps.
                // Stage 2.1 follow-up: thread BattlefieldObjectiveEstimate +
                // ObjectivePosition for VP-override behavior.
                var objectives = Array.Empty<ObjectivePosition>();
                var assignments = PlayCorpsAssignment.Assign(worldRects, corpsPositions, objectives);

                _lastSignature[allianceId] = sig;
                _lastResult[allianceId] = assignments;
                _lastComputeHours[allianceId] = now;
                _lastComputePlan[allianceId] = planId;
                _lastComputeMirrored[allianceId] = mirrored;
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
            bool mirrored,
            float armyX, float armyZ,
            float enemyX, float enemyZ,
            List<CorpsPosition> corpsPositions)
        {
            // Bucket positions to PositionBucketMeters so jitter doesn't
            // invalidate the cache. Stable ordering: sort corps by ChildId.
            var sb = new System.Text.StringBuilder();
            sb.Append((int)planId).Append(mirrored ? 'M' : 'A').Append('|');
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

        /// <summary>Battle end: clears the per-side caches.</summary>
        public static void ResetCache()
        {
            for (int i = 0; i < SideCount; i++)
            {
                _lastSignature[i] = null;
                _lastResult[i] = null;
                _lastComputeHours[i] = -1.0;
                _lastComputePlan[i] = BattlePlanId.Unknown;
                _lastComputeMirrored[i] = false;
            }
            _lastPlayExecutionTelemetryAt.Clear();
            for (int i = 0; i < SideCount; i++) _maneuvers[i].Clear();
            _firstFlankMarchLogged = false;
        }

        // ---- BC11 decision 4: envelopment maneuvers --------------------------------------------

        private sealed class SideManeuvers
        {
            public BattlePlanId Plan = BattlePlanId.Unknown;
            public double ActivatedHours = -1.0;
            public double CenterContactHours = -1.0;
            public readonly Dictionary<string, ManeuverState> States = new Dictionary<string, ManeuverState>(StringComparer.Ordinal);
            public readonly List<ChildManeuver> Orders = new List<ChildManeuver>(8);
            // The last march-leg route check per child, and the one child that gets this step's route plan (the fair pick).
            public readonly Dictionary<string, EnvelopmentManeuver.MarchRouteCheck> MarchChecks =
                new Dictionary<string, EnvelopmentManeuver.MarchRouteCheck>(StringComparer.Ordinal);
            public string PlanChildId;

            public void Clear()
            {
                Plan = BattlePlanId.Unknown;
                ActivatedHours = -1.0;
                CenterContactHours = -1.0;
                States.Clear();
                Orders.Clear();
                MarchChecks.Clear();
                PlanChildId = null;
            }
        }

        // Plan-scoped state per side (BattleScope "play-executor" clears it with the zone-assignment cache).
        private static readonly SideManeuvers[] _maneuvers = { new SideManeuvers(), new SideManeuvers() };
        private static readonly int[] _riverSamples = new int[4096];
        // The march-leg route: the NavMesh corners of the planned path, copied to flat arrays for the grid sampling.
        private static float[] _routeX = new float[64];
        private static float[] _routeZ = new float[64];
        // Scratch of the fair route-plan pick (grow-only; one entry per enveloping child of the step).
        private static EnvelopmentManeuver.MarchRouteCheck[] _planChecks = new EnvelopmentManeuver.MarchRouteCheck[8];
        private static bool[] _planNeeds = new bool[8];
        private static string[] _planIds = new string[8];
        private static NavMeshQueryFilter _routeFilter;
        private static long _routeFilterTick = -1L;
        private static bool _firstFlankMarchLogged;
        private static readonly Func<float, float, float> TerrainHeightAt = HeightAt;

        /// <summary>
        /// Steps every maneuvering child's state machine (EnvelopmentManeuver.Step) from what the side perceives and returns
        /// the orders; the army turns them into doctrine orders for each node of the child (CommandDoctrineAssignment).
        /// Fog-honest (observations only) and on the doctrine clock. Empty when Enable Flank Response And Envelopment is off,
        /// no play is active or the side is a Defender. The child's evidence supplies its strength bucket.
        /// </summary>
        public static IReadOnlyList<ChildManeuver> StepManeuvers(
            int allianceId,
            BattlePlanId planId,
            IReadOnlyList<DirectChildIntent> intents,
            IReadOnlyList<DirectChildEvidence> evidence,
            TacticalFrontAxis axis,
            BattleStance stance)
        {
            using (TelemetryPerf.Scope("tactical.envelopment", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1.0))
            {
                if (allianceId < 0 || allianceId >= SideCount) return Array.Empty<ChildManeuver>();
                SideManeuvers side = _maneuvers[allianceId];
                try
                {
                    if (!Plugin.FlankResponseEnabled() || planId == BattlePlanId.Unknown || intents == null || !axis.IsValid)
                    {
                        side.Clear();
                        return side.Orders;
                    }
                    PlayTemplate template = PlayTemplateCatalog.Lookup(planId);
                    if (template == null || !BattleCore.TryGetSnapshot(allianceId, out BattleSnapshot snapshot) || snapshot == null)
                    {
                        side.Clear();
                        return side.Orders;
                    }

                    double now = TacticalDoctrineTime.NowBattleHours;
                    // A new plan, or the clock went back (reload, new battle): the maneuvers start over.
                    if (side.Plan != planId || now < side.ActivatedHours)
                    {
                        EmitPlanChanged(allianceId, side);
                        side.Clear();
                        side.Plan = planId;
                        side.ActivatedHours = now;
                    }

                    bool templateHasCenterRect = false;
                    int envelopRects = 0;
                    for (int r = 0; r < template.Rects.Count; r++)
                    {
                        if (template.Rects[r].Kind == PlayRectKind.Center) templateHasCenterRect = true;
                        if (template.Rects[r].Envelops) envelopRects++;
                    }
                    // Wait for the Center child's contact only when a child really acts as the Center (see CenterChildId).
                    string centerChildId = templateHasCenterRect ? EnvelopmentManeuver.CenterChildId(_lastResult[allianceId], intents) : null;
                    bool templateHasCenter = centerChildId != null;
                    NoteCenterContact(side, snapshot, now, centerChildId);

                    PlayAnchor.EnemyAnchor(snapshot, axis, axis.OriginX, axis.OriginZ, out float enemyX, out float enemyZ);
                    BattleTerrainGrid grid = BattleTerrainGridRuntime.Get();
                    side.Orders.Clear();
                    ExploitTable exploits = ExploitTable.ForAlliance(allianceId);
                    // One route plan per side and step, served fairly (see PickRoutePlanChild).
                    side.PlanChildId = PickRoutePlanChild(side, snapshot, grid, intents, axis, stance, now);
                    for (int i = 0; i < intents.Count; i++)
                    {
                        ManeuverSpec spec = intents[i].Maneuver;
                        if (!spec.HasValue) continue;
                        int index = snapshot.IndexOfOwn(ParseChildInstanceId(intents[i].ChildId));
                        if (index < 0) continue;
                        // BC13: a group with a live exploit (or whose subtree has one) takes no zone maneuver; the exploit order
                        // is the posture producer's, and the two are mutually exclusive (OVR-MANEUVER).
                        if (exploits.IsExploiting(snapshot.Own[index].InstanceId)) continue;
                        side.States.TryGetValue(intents[i].ChildId, out ManeuverState state);
                        ManeuverPhase before = state.Phase;
                        bool wasMirrored = state.Mirrored;

                        ManeuverInput input = BuildInput(snapshot, grid, axis, spec, intents[i].ChildId, index, i < evidence.Count ? evidence[i].OwnStrengthBucket : 1,
                            state.Mirrored, state.OutflankReached, templateHasCenter, envelopRects == 1, side, enemyX, enemyZ, stance, now);
                        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, in input);
                        side.States[intents[i].ChildId] = state;
                        side.Orders.Add(new ChildManeuver(intents[i].ChildId, order, axis.RightX, axis.RightZ));
                        if (state.Phase != before || state.Mirrored != wasMirrored) EmitEnvelopment(allianceId, planId, intents[i].ChildId, order, state, input);
                    }
                    return side.Orders;
                }
                catch (Exception e)
                {
                    OnceLog.Warning("play-executor-runtime:maneuver-exception",
                        "PlayExecutorRuntime.StepManeuvers failed: " + e.GetType().Name + " " + e.Message);
                    side.Orders.Clear();
                    return side.Orders;
                }
            }
        }

        /// <summary>
        /// BC13: the instance ids of the side's children in an active maneuver phase (Hold, March, Turn or Attack), written to
        /// <paramref name="into"/>; returns how many. An exploit never picks one of them, an ancestor or a descendant.
        /// </summary>
        internal static int ManeuveringChildren(int allianceId, int[] into)
        {
            if (allianceId < 0 || allianceId >= SideCount) return 0;
            int n = 0;
            foreach (KeyValuePair<string, ManeuverState> pair in _maneuvers[allianceId].States)
            {
                ManeuverPhase phase = pair.Value.Phase;
                if (phase != ManeuverPhase.Hold && phase != ManeuverPhase.March && phase != ManeuverPhase.Turn && phase != ManeuverPhase.Attack) continue;
                int id = ParseChildInstanceId(pair.Key);
                if (id != 0 && n < into.Length) into[n++] = id;
            }
            return n;
        }

        // Children still maneuvering when their plan changed: one Abandoned row each (smoke item 2.28, reason plan-changed).
        private static void EmitPlanChanged(int allianceId, SideManeuvers side)
        {
            if (side.Plan == BattlePlanId.Unknown) return;
            foreach (KeyValuePair<string, ManeuverState> pair in side.States)
            {
                ManeuverPhase phase = pair.Value.Phase;
                if (phase != ManeuverPhase.Hold && phase != ManeuverPhase.March && phase != ManeuverPhase.Turn && phase != ManeuverPhase.Attack) continue;
                var abandoned = pair.Value;
                abandoned.Phase = ManeuverPhase.Abandoned;
                abandoned.AbandonReason = "plan-changed";
                var order = new ManeuverOrder { Phase = ManeuverPhase.Abandoned, Heading = -1f, Reason = "plan-changed" };
                EmitEnvelopment(allianceId, side.Plan, pair.Key, in order, in abandoned, default(ManeuverInput));
            }
        }

        // The Center rect's child sees an enemy within ContactMeters: the first doctrine hour that happens is kept.
        private static void NoteCenterContact(SideManeuvers side, BattleSnapshot snapshot, double now, string centerChildId)
        {
            if (centerChildId == null || side.CenterContactHours >= 0.0) return;
            int index = snapshot.IndexOfOwn(ParseChildInstanceId(centerChildId));
            if (index >= 0 && snapshot.ClosestVisibleEnemy(index, out float distance) >= 0 && distance <= EnvelopmentManeuver.ContactMeters)
                side.CenterContactHours = now;
        }

        private static ManeuverInput BuildInput(BattleSnapshot snapshot, BattleTerrainGrid grid, TacticalFrontAxis axis, ManeuverSpec spec, string childId, int index,
            int strengthBucket, bool mirrored, bool outflankReached, bool templateHasCenter, bool canMirror, SideManeuvers side,
            float enemyX, float enemyZ, BattleStance stance, double now)
        {
            ref UnitRecord child = ref snapshot.Own[index];
            RectAnchor(in axis, spec, mirrored, out float anchorX, out float anchorZ);
            bool right = EnvelopmentManeuver.RectIsRight(in axis, anchorX, anchorZ);

            var input = new ManeuverInput
            {
                Kind = spec.Kind, Envelops = spec.Envelops, AnchorX = anchorX, AnchorZ = anchorZ, StartOffsetSeconds = spec.StartOffsetSeconds,
                TemplateHasCenter = templateHasCenter, PlayActivatedHours = side.ActivatedHours, CenterContactHours = side.CenterContactHours,
                ChildX = child.X, ChildZ = child.Z, ChildStrengthBucket = strengthBucket,
                EnemyAnchorX = enemyX, EnemyAnchorZ = enemyZ, CanMirror = canMirror,
                HeightDifferenceAnchored = GamePrefs.heightdifferencetoenemyunitanchored, Stance = stance, NowHours = now,
            };
            input.ChildInContact = snapshot.ClosestVisibleEnemy(index, out float contactDistance) >= 0 && contactDistance <= EnvelopmentManeuver.ContactMeters;
            // SetGroupFormation paths the HQ groupdepth + groupunitdistancetoback behind the waypoint (92044, 92058).
            input.ArrivalSlackMeters = child.GroupDepth + GamePrefs.groupunitdistancetoback;
            input.ChildArrived = EnvelopmentManeuver.Arrived(child.X, child.Z, anchorX, anchorZ, input.ArrivalSlackMeters);
            input.HasEnemyExtent = EnvelopmentManeuver.EnemyLineEnd(in axis, enemyX, enemyZ, right, out float endX, out float endZ);
            input.EnemyEndX = endX;
            input.EnemyEndZ = endZ;

            if (spec.Kind == PlayRectKind.DeepRaid)
            {
                EnvelopmentManeuver.EnemyPressureSplit(snapshot, in axis, out float left, out float rightPressure);
                float depth = (anchorX - axis.OriginX) * axis.ForwardX + (anchorZ - axis.OriginZ) * axis.ForwardZ;
                if (EnvelopmentManeuver.OutflankPoint(in axis, depth, left, rightPressure, right, out float ox, out float oz))
                {
                    input.OutflankX = ox;
                    input.OutflankZ = oz;
                }
                else
                {
                    input.OutflankX = anchorX;
                    input.OutflankZ = anchorZ;
                }
            }

            // FlankMove's checks (116929-117108) on the inward leg (anchor to the line end) and the march leg.
            float inwardX = input.HasEnemyExtent ? endX : enemyX, inwardZ = input.HasEnemyExtent ? endZ : enemyZ;
            input.AnchorHeight = HeightAt(anchorX, anchorZ);
            input.CorridorEnemyCount = EnvelopmentManeuver.CorridorEnemies(snapshot, anchorX, anchorZ, inwardX, inwardZ, TerrainHeightAt, out float maxHeight);
            input.MaxCorridorEnemyHeight = maxHeight;
            if (grid != null)
            {
                int n = grid.SampleLine(anchorX, anchorZ, inwardX, inwardZ, EnvelopmentManeuver.InwardStepMeters, _riverSamples);
                input.InwardLegHitsRiver = !EnvelopmentManeuver.InwardLegClear(_riverSamples, n);
                // A frozen river carries units: nothing to judge. Otherwise the route result (or its absence) decides, see MarchRouteStatus.
                if (grid.Frozen) input.MarchRouteKnown = true;
                else MarchRouteStatus(side, childId, grid, in child, anchorX, anchorZ, now, snapshot.TickId, ref input);
            }
            else input.MarchRouteKnown = true;   // no terrain grid: no march-leg verdict to wait for
            return input;
        }

        private static void RectAnchor(in TacticalFrontAxis axis, ManeuverSpec spec, bool mirrored, out float x, out float z)
        {
            x = spec.AnchorX;
            z = spec.AnchorZ;
            if (mirrored) EnvelopmentManeuver.MirrorAcrossAxis(in axis, spec.AnchorX, spec.AnchorZ, out x, out z);
        }

        // Which enveloping child gets this step's single route plan, in the fair order EnvelopmentManeuver.PickRoutePlan defines: a
        // child with no route result for its current anchor goes before any re-check. N enveloping children therefore each have
        // their first route within N steps (DoubleEnvelopment's two wings: by the second step). Children that are done (Attack,
        // Abandoned, Released), a Defender's, and a frozen map's need none.
        private static string PickRoutePlanChild(SideManeuvers side, BattleSnapshot snapshot, BattleTerrainGrid grid, IReadOnlyList<DirectChildIntent> intents,
            TacticalFrontAxis axis, BattleStance stance, double now)
        {
            if (grid == null || grid.Frozen || stance == BattleStance.Defender) return null;
            int n = 0;
            for (int i = 0; i < intents.Count; i++)
            {
                ManeuverSpec spec = intents[i].Maneuver;
                if (!spec.HasValue || !spec.Envelops) continue;
                if (snapshot.IndexOfOwn(ParseChildInstanceId(intents[i].ChildId)) < 0) continue;
                side.States.TryGetValue(intents[i].ChildId, out ManeuverState state);
                if (state.Phase == ManeuverPhase.Attack || state.Phase == ManeuverPhase.Abandoned || state.Phase == ManeuverPhase.Released) continue;
                RectAnchor(in axis, spec, state.Mirrored, out float anchorX, out float anchorZ);
                side.MarchChecks.TryGetValue(intents[i].ChildId, out EnvelopmentManeuver.MarchRouteCheck last);
                last = EnvelopmentManeuver.CheckForAnchor(in last, anchorX, anchorZ);
                if (n == _planChecks.Length)
                {
                    Array.Resize(ref _planChecks, n * 2);
                    Array.Resize(ref _planNeeds, n * 2);
                    Array.Resize(ref _planIds, n * 2);
                }
                _planChecks[n] = last;
                _planNeeds[n] = EnvelopmentManeuver.NeedsRouteRecheck(in last, now, anchorX, anchorZ);
                _planIds[n] = intents[i].ChildId;
                n++;
            }
            int pick = EnvelopmentManeuver.PickRoutePlan(_planChecks, _planNeeds, n);
            return pick < 0 ? null : _planIds[pick];
        }

        // The march leg's route result for this child: the stored one while it holds for this anchor, re-planned when the fair pick
        // named this child. No result for the anchor means MarchRouteKnown stays false and the state machine keeps the child where it
        // is (a Hold waits); a straight line is never sampled in its place.
        private static void MarchRouteStatus(SideManeuvers side, string childId, BattleTerrainGrid grid, in UnitRecord child,
            float anchorX, float anchorZ, double now, long tickId, ref ManeuverInput input)
        {
            side.MarchChecks.TryGetValue(childId, out EnvelopmentManeuver.MarchRouteCheck last);
            last = EnvelopmentManeuver.CheckForAnchor(in last, anchorX, anchorZ);
            if (EnvelopmentManeuver.NeedsRouteRecheck(in last, now, anchorX, anchorZ) && string.Equals(side.PlanChildId, childId, StringComparison.Ordinal))
            {
                last = PlanRoute(grid, in child, anchorX, anchorZ, now, tickId);
                side.MarchChecks[childId] = last;
                side.PlanChildId = null;
            }
            input.MarchRouteKnown = last.Valid;
            input.MarchLegHitsRiver = last.HitsRiver;
            input.MarchRouteMissing = last.NoPath;
        }

        // The march leg judged on the route the child will walk: a NavMesh path from its position to the anchor, priced like the
        // posture probe, each segment sampled on the BC10 grid (a river crossed by a bridge or ford is not a river). No complete path
        // (or a failed query) is a result too: NoPath.
        private static EnvelopmentManeuver.MarchRouteCheck PlanRoute(BattleTerrainGrid grid, in UnitRecord child, float anchorX, float anchorZ,
            double now, long tickId)
        {
            var check = new EnvelopmentManeuver.MarchRouteCheck { Valid = true, CheckedHours = now, AnchorX = anchorX, AnchorZ = anchorZ, NoPath = true };
            try
            {
                if (_routeFilterTick != tickId)
                {
                    _routeFilter = WhiskeyRealism.Tactical.Battle.Producers.PostureTargets.CreateNavFilter();
                    _routeFilterTick = tickId;
                }
                NavMeshPath path = WhiskeyRealism.Tactical.Battle.Producers.PostureTargets.SharedPath();
                if (NavMesh.CalculatePath(new Vector3(child.X, child.Y, child.Z), new Vector3(anchorX, HeightAt(anchorX, anchorZ), anchorZ), _routeFilter, path)
                    && path.status == NavMeshPathStatus.PathComplete)
                {
                    int count = WhiskeyRealism.Tactical.Battle.Producers.PostureTargets.ReadCorners(path, out Vector3[] corners);
                    if (_routeX.Length < count)
                    {
                        _routeX = new float[count * 2];
                        _routeZ = new float[count * 2];
                    }
                    for (int c = 0; c < count; c++)
                    {
                        _routeX[c] = corners[c].x;
                        _routeZ[c] = corners[c].z;
                    }
                    if (count > 0)
                    {
                        check.NoPath = false;
                        check.HitsRiver = EnvelopmentManeuver.RouteHitsRiver(grid, _routeX, _routeZ, count, EnvelopmentManeuver.MarchStepMeters, _riverSamples);
                    }
                }
            }
            catch (Exception e)
            {
                OnceLog.Warning("play-executor-runtime:march-route-exception",
                    "PlayExecutorRuntime march route plan failed, the leg counts as unwalkable: " + e.GetType().Name + " " + e.Message);
            }
            return check;
        }

        private static float HeightAt(float x, float z)
        {
            return TacticalTerrainProbe.WithTerrainHeight(new Vector3(x, 0f, z)).y;
        }

        private static int ParseChildInstanceId(string childId) => DirectChildIds.InstanceId(childId);

        // One TacticalEnvelopment row per phase change (or mirror), so the stream is bounded by the state machine.
        private static void EmitEnvelopment(int allianceId, BattlePlanId planId, string childId, in ManeuverOrder order, in ManeuverState state,
            in ManeuverInput input)
        {
            try
            {
                string kind = input.Kind.ToString();
                string phase = state.Phase.ToString();
                if (state.Phase == ManeuverPhase.March && !_firstFlankMarchLogged)
                {
                    _firstFlankMarchLogged = true;
                    OnceLog.Info("tactical-battle-core:envelopment-first-fire",
                        "[Envelopment] first flank march: plan=" + planId + " rect=" + kind + " child=" + childId);
                }
                string reason = state.Phase == ManeuverPhase.Abandoned ? (state.AbandonReason ?? order.Reason) : order.Reason;
                bool mirrored = state.Mirrored;
                string proceed = state.ProceedReason ?? string.Empty;
                float targetX = order.TargetX, targetZ = order.TargetZ, anchorX = input.AnchorX, anchorZ = input.AnchorZ;
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalEnvelopment", TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(allianceId)
                        .WithDecision("TacticalEnvelopment", phase, "side=" + allianceId + "|child=" + childId + "|phase=" + phase)
                        .WithField("plan", planId.ToString())
                        .WithField("child", childId)
                        .WithField("kind", kind)
                        .WithField("phase", phase)
                        .WithField("reason", reason)
                        .WithField("mirrored", mirrored)
                        .WithField("proceed", proceed)
                        .WithField("anchorX", anchorX)
                        .WithField("anchorZ", anchorZ)
                        .WithField("targetX", targetX)
                        .WithField("targetZ", targetZ));
            }
            catch { }
        }
    }
}
