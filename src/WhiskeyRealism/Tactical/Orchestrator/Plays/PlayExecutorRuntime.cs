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
            bool mirrored,
            IFeasibilityLookup feasibility = null)
        {
            using (TelemetryPerf.Scope("tactical.play-executor.zone-assignments",
                TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
            {
                return ComputeZoneAssignmentsCore(allianceId, planId, commandHierarchyShift, childCount, frontAxis, directChildIds, mirrored, feasibility);
            }
        }

        private static IReadOnlyList<CorpsAssignment> ComputeZoneAssignmentsCore(
            int allianceId,
            BattlePlanId planId,
            int commandHierarchyShift,
            int childCount,
            TacticalFrontAxis frontAxis,
            IReadOnlyList<string> directChildIds,
            bool mirrored,
            IFeasibilityLookup feasibility)
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
                // BC14W: a play faces its target (FocalAssault: the focal point of the works), through the frame the evaluation's layout takes.
                FocalPoint focal = default(FocalPoint);
                bool hasFocal = planId == BattlePlanId.FocalAssault && feasibility != null && feasibility.TryFocalPoint(out focal);
                PlayAnchor.PlayFrame(planId, hasFocal, focal.X, focal.Z, enemyX, enemyZ, armyX, armyZ, ownXs, ownZs, out float targetX, out float targetZ,
                    out float frontage);

                // ---- Signature-skip ----
                // Build a per-side signature from plan + bucketed centroids +
                // bucketed corps positions. If unchanged since last call, return
                // the cached result without recomputing projection/assignment.
                string sig = BuildSignature(planId, mirrored, armyX, armyZ, targetX, targetZ, corpsPositions);
                string lastSig = _lastSignature[allianceId];
                var cached = _lastResult[allianceId];
                if (cached != null && string.Equals(sig, lastSig, StringComparison.Ordinal))
                {
                    return cached;
                }

                // BC12: ReverseSlopeDefense is anchored to the crest the terrain picture found across the front.
                TerrainLine crest = planId == BattlePlanId.ReverseSlopeDefense
                    ? ArmyEvidenceBuilder.CrestLineAcrossFront(TerrainPicture.CurrentForPlanning(), frontAxis)
                    : default(TerrainLine);
                var worldRects = PlayProjection.Project(template, armyX, armyZ, targetX, targetZ, frontage, in crest, mirrored);

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
            public SideManeuvers(int alliance) { Alliance = alliance; }

            public readonly int Alliance;
            // BC14W: the child that runs the focal assault's AssaultColumn this plan (null with none), whose state the followers' release reads.
            public string ColumnChildId;
            public BattlePlanId Plan = BattlePlanId.Unknown;
            public double ActivatedHours = -1.0;
            public double CenterContactHours = -1.0;
            public readonly Dictionary<string, ManeuverState> States = new Dictionary<string, ManeuverState>(StringComparer.Ordinal);
            public readonly List<ChildManeuver> Orders = new List<ChildManeuver>(8);
            // The last march-leg route check per child, and the one child that gets this step's route plan (the fair pick).
            public readonly Dictionary<string, EnvelopmentManeuver.MarchRouteCheck> MarchChecks =
                new Dictionary<string, EnvelopmentManeuver.MarchRouteCheck>(StringComparer.Ordinal);
            public string PlanChildId;
            // BC14 requirement-wing-abandon-replan: the enveloping children BC11 abandoned under this plan, in the order it did. The ledger
            // keeps one writer (the coordinator's heavy path), so BC11 only queues them here and the heavy path drains the queue into the
            // monitor's facts on its next run.
            public readonly List<string> Abandoned = new List<string>(4);
            // The ledger's predicted arrival and march hours of each enveloping child, latched when its March began (the Turn row compares with them).
            public readonly Dictionary<string, (double arrival, double march)> Predicted = new Dictionary<string, (double, double)>(StringComparer.Ordinal);

            public void Clear()
            {
                Plan = BattlePlanId.Unknown;
                Abandoned.Clear();
                Predicted.Clear();
                ActivatedHours = -1.0;
                CenterContactHours = -1.0;
                States.Clear();
                Orders.Clear();
                MarchChecks.Clear();
                PlanChildId = null;
                ColumnChildId = null;
            }
        }

        // Plan-scoped state per side (BattleScope "play-executor" clears it with the zone-assignment cache).
        private static readonly SideManeuvers[] _maneuvers = { new SideManeuvers(0), new SideManeuvers(1) };
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
            BattleStance stance,
            IFeasibilityLookup feasibility = null)
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
                    for (int r = 0; r < template.Rects.Count; r++)
                        if (template.Rects[r].Kind == PlayRectKind.Center) templateHasCenterRect = true;
                    // Wait for the Center child's contact only when a child really acts as the Center (see CenterChildId).
                    string centerChildId = templateHasCenterRect ? EnvelopmentManeuver.CenterChildId(_lastResult[allianceId], intents) : null;
                    bool templateHasCenter = centerChildId != null;
                    NoteCenterContact(side, snapshot, now, centerChildId);

                    PlayAnchor.EnemyAnchor(snapshot, axis, axis.OriginX, axis.OriginZ, out float enemyX, out float enemyZ);
                    BattleTerrainGrid grid = BattleTerrainGridRuntime.Get();
                    side.Orders.Clear();
                    ExploitTable exploits = ExploitTable.ForAlliance(allianceId);
                    // One route plan per side and step, served fairly (see PickRoutePlanChild).
                    side.PlanChildId = PickRoutePlanChild(side, snapshot, grid, intents, axis, stance, now, planId, feasibility);
                    side.ColumnChildId = null;
                    for (int c = 0; c < intents.Count && side.ColumnChildId == null; c++)
                        if (intents[c].Maneuver.HasValue && intents[c].Maneuver.Kind == PlayRectKind.AssaultColumn) side.ColumnChildId = intents[c].ChildId;
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
                            state.Mirrored, state.OutflankReached, templateHasCenter, PlayExecutorRules.CanMirror(template), side, enemyX, enemyZ, stance, now,
                            planId, feasibility, in state);
                        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, in input);
                        side.States[intents[i].ChildId] = state;
                        side.Orders.Add(new ChildManeuver(intents[i].ChildId, order, axis.RightX, axis.RightZ, spec.Kind));
                        if (state.Phase == ManeuverPhase.Abandoned && before != ManeuverPhase.Abandoned && spec.Envelops) side.Abandoned.Add(intents[i].ChildId);
                        if (state.Phase == ManeuverPhase.March && before != ManeuverPhase.March)
                        {
                            if (feasibility != null && feasibility.TryPredictedArrival(intents[i].ChildId, out double arrival, out double predictedMarch))
                                side.Predicted[intents[i].ChildId] = (arrival, predictedMarch);
                            if (state.HasVia) FeasibilityTelemetry.FirstVia(FeasibilityRuntime.StateAt(allianceId, state.ViaX, state.ViaZ));
                        }
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

        /// <summary>BC14W: the doctrine hour the side's current play was activated (the preparation table's key), -1 with none.</summary>
        internal static double ActivatedHours(int allianceId)
            => allianceId >= 0 && allianceId < SideCount ? _maneuvers[allianceId].ActivatedHours : -1.0;

        /// <summary>BC14: BC11's phase for the side's direct child (None before its maneuver starts), the monitor's read of how far each wing has got.</summary>
        internal static ManeuverPhase PhaseOf(int allianceId, string childId)
        {
            if (allianceId < 0 || allianceId >= SideCount || childId == null) return ManeuverPhase.None;
            return _maneuvers[allianceId].States.TryGetValue(childId, out ManeuverState state) ? state.Phase : ManeuverPhase.None;
        }

        /// <summary>BC14: the child is past its Hold (marching, turning or attacking): its order delay was paid when it was released.</summary>
        internal static bool IsMoving(int allianceId, string childId)
        {
            ManeuverPhase phase = PhaseOf(allianceId, childId);
            return phase == ManeuverPhase.March || phase == ManeuverPhase.Turn || phase == ManeuverPhase.Attack;
        }

        /// <summary>
        /// BC14 requirement-wing-abandon-replan: moves the enveloping children BC11 abandoned since the last call into <paramref name="into"/> (after
        /// <paramref name="count"/> entries, as many as fit) and returns the new count. The ledger's heavy path is the one caller.
        /// </summary>
        internal static int DrainAbandoned(int allianceId, string[] into, int count)
        {
            if (allianceId < 0 || allianceId >= SideCount) return count;
            List<string> queue = _maneuvers[allianceId].Abandoned;
            int taken = 0;
            while (taken < queue.Count && count < into.Length) into[count++] = queue[taken++];
            queue.RemoveRange(0, taken);
            return count;
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
            float enemyX, float enemyZ, BattleStance stance, double now, BattlePlanId planId, IFeasibilityLookup feasibility, in ManeuverState state)
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
                HoldsForRelease = spec.HoldsForRelease,
            };
            input.ChildInContact = snapshot.ClosestVisibleEnemy(index, out float contactDistance) >= 0 && contactDistance <= EnvelopmentManeuver.ContactMeters;
            // SetGroupFormation paths the HQ groupdepth + groupunitdistancetoback behind the waypoint (92044, 92058).
            input.ArrivalSlackMeters = child.GroupDepth + GamePrefs.groupunitdistancetoback;
            input.ChildArrived = EnvelopmentManeuver.Arrived(child.X, child.Z, anchorX, anchorZ, input.ArrivalSlackMeters);
            input.HasEnemyExtent = EnvelopmentManeuver.EnemyLineEnd(in axis, enemyX, enemyZ, right, out float endX, out float endZ);
            input.EnemyEndX = endX;
            input.EnemyEndZ = endZ;
            input.GroupFormation = child.Formation;

            // BC14W (requirement-focal-execution): the focal assault's column strikes the focal point (latched at its March, until then each evaluation's):
            // the inward target of the corridor and river checks, the row's line end, and the Turn's heading and Attack's target in BC11.
            if (spec.Kind == PlayRectKind.AssaultColumn)
            {
                FocalPoint focalPoint = default(FocalPoint);
                bool latched = state.HasFocal;
                if (latched) { focalPoint.X = state.FocalX; focalPoint.Z = state.FocalZ; }
                if (latched || (feasibility != null && feasibility.TryFocalPoint(out focalPoint)))
                {
                    input.HasFocal = true;
                    input.FocalX = focalPoint.X;
                    input.FocalZ = focalPoint.Z;
                    input.HasEnemyExtent = true;
                    input.EnemyEndX = focalPoint.X;
                    input.EnemyEndZ = focalPoint.Z;
                    // The first battery's bombardment plus PreparationHours is the earliest the column goes in (no battery passed: no floor).
                    PreparationTable preparation = PreparationTable.ForAlliance(side.Alliance);
                    if (preparation != null && preparation.TryFloor(planId, side.ActivatedHours, focalPoint.X, focalPoint.Z, out double floor))
                    {
                        input.HasPreparationFloor = true;
                        input.PreparationFloorHours = floor;
                    }
                }
            }

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

            // BC14W (requirement-works-hold): a hopeless works assault is never launched, whichever plan carries it. The body that assaults works
            // and every start delay of the plan stay in Hold, with no planned release published for them (the ledger's release below).
            if (feasibility != null && Plugin.EnemyWorksDoctrineEnabled() && feasibility.WorksHold(out string worksReason))
                input.HoldForWorks = EnvelopmentManeuver.HoldForWorksFor(worksReason, feasibility.AssaultsWorks(childId), !spec.Envelops);

            // BC14: the ledger's release, time budget and vias, for a child of the plan the ledger selected and still on the ledger's side
            // (BC11's own mirrored try, or a plan the ledger does not know, leaves BC11's rule as it is).
            LedgerPlan(feasibility, planId, childId, mirrored, in state, in input, out PlannedLeg planned);
            if (planned.HasRelease && !input.HoldForWorks)
            {
                input.HasPlannedRelease = true;
                input.PlannedReleaseHours = planned.ReleaseHours;
                input.HasTimeBudget = planned.HasBudget;
                input.TimeBudgetHours = planned.TimeBudgetHours;
                input.HasVia = planned.HasVia;
                input.ViaX = planned.ViaX;
                input.ViaZ = planned.ViaZ;
                input.HasAttackVia = planned.HasAttackVia;
                input.AttackViaX = planned.AttackViaX;
                input.AttackViaZ = planned.AttackViaZ;
            }
            input.ViaPassed = planned.ViaPassed;
            input.AttackViaPassed = planned.AttackViaPassed;
            // BC14W: the focal assault's support and demonstrations follow the column, not a published hour: the support goes at its strike plus the
            // echelon delay, the demonstrations at it, and both wait while the column does (the preparation floor, the formation wait and the works hold
            // move them with it). A follower in contact is not held.
            if (planId == BattlePlanId.FocalAssault && spec.HoldsForRelease && (spec.Kind == PlayRectKind.AssaultSupport || spec.Kind == PlayRectKind.Demonstration))
            {
                ManeuverState columnState = default(ManeuverState);
                if (side.ColumnChildId != null) side.States.TryGetValue(side.ColumnChildId, out columnState);
                bool beforeMarch = state.Phase == ManeuverPhase.None || state.Phase == ManeuverPhase.Hold;
                input.HasPlannedRelease = beforeMarch && !input.ChildInContact;
                input.PlannedReleaseHours = EnvelopmentManeuver.FollowerReleaseHours(spec.Kind, side.ColumnChildId != null, columnState.Phase,
                    columnState.PhaseSinceHours, now);
            }

            // FlankMove's checks (116929-117108) on the inward leg (anchor to the line end) and the march leg.
            float inwardX = input.HasFocal ? input.FocalX : (input.HasEnemyExtent ? endX : enemyX);
            float inwardZ = input.HasFocal ? input.FocalZ : (input.HasEnemyExtent ? endZ : enemyZ);
            input.AnchorHeight = HeightAt(anchorX, anchorZ);
            input.CorridorEnemyCount = EnvelopmentManeuver.CorridorEnemies(snapshot, anchorX, anchorZ, inwardX, inwardZ, TerrainHeightAt, out float maxHeight);
            input.MaxCorridorEnemyHeight = maxHeight;
            if (grid != null)
            {
                // With an attack via the inward leg is two sub-legs through it; the via's own footprint is not a river (requirement-crossing-via).
                input.InwardLegHitsRiver = EnvelopmentManeuver.InwardLegHitsRiver(grid, anchorX, anchorZ, planned.CheckAttackVia, planned.CheckAttackViaX,
                    planned.CheckAttackViaZ, inwardX, inwardZ, RiverRegions.CrossingLinkMeters, _riverSamples);
                // A frozen river carries units: nothing to judge. Otherwise the route result (or its absence) decides, see MarchRouteStatus.
                if (grid.Frozen) input.MarchRouteKnown = true;
                else MarchRouteStatus(side, childId, grid, in child, anchorX, anchorZ, planned.CheckVia, planned.CheckViaX, planned.CheckViaZ, now, snapshot.TickId, ref input);
            }
            else input.MarchRouteKnown = true;   // no terrain grid: no march-leg verdict to wait for
            return input;
        }

        // What the ledger says of one child this step (requirement-synchronized-release, requirement-crossing-via): the release, the budget and the
        // vias that start the March, and the vias the checks use now (the latched ones once the March began).
        private struct PlannedLeg
        {
            public bool HasRelease, HasBudget, HasVia, HasAttackVia, ViaPassed, AttackViaPassed;
            public double ReleaseHours, TimeBudgetHours;
            public float ViaX, ViaZ, AttackViaX, AttackViaZ;
            public bool CheckVia, CheckAttackVia;
            public float CheckViaX, CheckViaZ, CheckAttackViaX, CheckAttackViaZ;
        }

        private static void LedgerPlan(IFeasibilityLookup feasibility, BattlePlanId planId, string childId, bool mirrored, in ManeuverState state,
            in ManeuverInput input, out PlannedLeg planned)
        {
            planned = default(PlannedLeg);
            // Once the March began the latched vias are the child's; the ledger's recomputed ones no longer apply.
            bool latched = state.Phase == ManeuverPhase.March || state.Phase == ManeuverPhase.Turn || state.Phase == ManeuverPhase.Attack;
            if (latched)
            {
                planned.CheckVia = state.HasVia;
                planned.CheckViaX = state.ViaX;
                planned.CheckViaZ = state.ViaZ;
                planned.CheckAttackVia = state.HasAttackVia;
                planned.CheckAttackViaX = state.AttackViaX;
                planned.CheckAttackViaZ = state.AttackViaZ;
            }
            if (feasibility != null && !mirrored && feasibility.SelectedPlan == planId
                && feasibility.TryPlannedRelease(childId, out double release, out double budget, out float vx, out float vz, out float avx, out float avz))
            {
                // A fix in contact is never held: it has no planned release (a fix that is engaged is released at once).
                bool fixInContact = input.HoldsForRelease && input.ChildInContact;
                bool beforeMarch = state.Phase == ManeuverPhase.None || state.Phase == ManeuverPhase.Hold;
                planned.HasRelease = !fixInContact && beforeMarch;
                planned.ReleaseHours = release;
                planned.HasBudget = budget > 0.0;
                planned.TimeBudgetHours = budget;
                planned.HasVia = !float.IsNaN(vx) && !float.IsNaN(vz);
                planned.ViaX = vx;
                planned.ViaZ = vz;
                planned.HasAttackVia = !float.IsNaN(avx) && !float.IsNaN(avz);
                planned.AttackViaX = avx;
                planned.AttackViaZ = avz;
                if (!latched)
                {
                    planned.CheckVia = planned.HasVia;
                    planned.CheckViaX = vx;
                    planned.CheckViaZ = vz;
                    planned.CheckAttackVia = planned.HasAttackVia;
                    planned.CheckAttackViaX = avx;
                    planned.CheckAttackViaZ = avz;
                }
            }

            // The child already stands in the far region of a via: the order carries it no more and the checks skip it.
            RiverRegions regions = RiverRegionsRuntime.Ready();
            if (regions != null && feasibility != null && feasibility.TryViaRegions(childId, out int farRegion, out int attackFarRegion))
            {
                int here = regions.RegionAt(input.ChildX, input.ChildZ);
                planned.ViaPassed = farRegion >= 0 && here == farRegion;
                planned.AttackViaPassed = attackFarRegion >= 0 && here == attackFarRegion;
                if (planned.ViaPassed) planned.CheckVia = false;
                if (planned.AttackViaPassed) planned.CheckAttackVia = false;
            }
        }

        // The rect anchor on the side the child is on (EnvelopmentManeuver.AnchorFor: the authored anchor after the one mirrored try of a plan
        // the ledger projected mirrored).
        private static void RectAnchor(in TacticalFrontAxis axis, ManeuverSpec spec, bool mirrored, out float x, out float z)
            => EnvelopmentManeuver.AnchorFor(in axis, in spec, mirrored, out x, out z);

        // Which enveloping child gets this step's single route plan, in the fair order EnvelopmentManeuver.PickRoutePlan defines: a
        // child with no route result for its current anchor goes before any re-check. N enveloping children therefore each have
        // their first route within N steps (DoubleEnvelopment's two wings: by the second step). Children that are done (Attack,
        // Abandoned, Released), a Defender's, and a frozen map's need none.
        private static string PickRoutePlanChild(SideManeuvers side, BattleSnapshot snapshot, BattleTerrainGrid grid, IReadOnlyList<DirectChildIntent> intents,
            TacticalFrontAxis axis, BattleStance stance, double now, BattlePlanId planId, IFeasibilityLookup feasibility)
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
                MarchVia(feasibility, planId, intents[i].ChildId, in state, snapshot, out bool hasVia, out float viaX, out float viaZ);
                side.MarchChecks.TryGetValue(intents[i].ChildId, out EnvelopmentManeuver.MarchRouteCheck last);
                last = EnvelopmentManeuver.CheckForLeg(in last, anchorX, anchorZ, hasVia, viaX, viaZ);
                if (n == _planChecks.Length)
                {
                    Array.Resize(ref _planChecks, n * 2);
                    Array.Resize(ref _planNeeds, n * 2);
                    Array.Resize(ref _planIds, n * 2);
                }
                _planChecks[n] = last;
                _planNeeds[n] = EnvelopmentManeuver.NeedsRouteRecheck(in last, now, anchorX, anchorZ, hasVia, viaX, viaZ);
                _planIds[n] = intents[i].ChildId;
                n++;
            }
            int pick = EnvelopmentManeuver.PickRoutePlan(_planChecks, _planNeeds, n);
            return pick < 0 ? null : _planIds[pick];
        }

        // The via the march leg's route check runs through this step: the latched one once the March began, else the ledger's, and none once the
        // child stands in its far region.
        private static void MarchVia(IFeasibilityLookup feasibility, BattlePlanId planId, string childId, in ManeuverState state, BattleSnapshot snapshot,
            out bool hasVia, out float viaX, out float viaZ)
        {
            var probe = default(ManeuverInput);
            int index = snapshot.IndexOfOwn(ParseChildInstanceId(childId));
            if (index >= 0)
            {
                probe.ChildX = snapshot.Own[index].X;
                probe.ChildZ = snapshot.Own[index].Z;
            }
            LedgerPlan(feasibility, planId, childId, state.Mirrored, in state, in probe, out PlannedLeg planned);
            hasVia = planned.CheckVia;
            viaX = planned.CheckViaX;
            viaZ = planned.CheckViaZ;
        }

        // The march leg's route result for this child: the stored one while it holds for this anchor and via, re-planned when the fair pick
        // named this child. No result for the leg means MarchRouteKnown stays false and the state machine keeps the child where it
        // is (a Hold waits); a straight line is never sampled in its place. A leg through a crossing via is two queries (child to via, via to
        // anchor), one per step on the same one-per-step budget; the leg has a result when both halves have one.
        private static void MarchRouteStatus(SideManeuvers side, string childId, BattleTerrainGrid grid, in UnitRecord child,
            float anchorX, float anchorZ, bool hasVia, float viaX, float viaZ, double now, long tickId, ref ManeuverInput input)
        {
            side.MarchChecks.TryGetValue(childId, out EnvelopmentManeuver.MarchRouteCheck last);
            last = EnvelopmentManeuver.CheckForLeg(in last, anchorX, anchorZ, hasVia, viaX, viaZ);
            if (EnvelopmentManeuver.NeedsRouteRecheck(in last, now, anchorX, anchorZ, hasVia, viaX, viaZ) && string.Equals(side.PlanChildId, childId, StringComparison.Ordinal))
            {
                int half = EnvelopmentManeuver.HalfToPlan(in last, hasVia, viaX, viaZ);
                float fromX = half == 2 ? viaX : child.X, fromZ = half == 2 ? viaZ : child.Z;
                float toX = half == 1 ? viaX : anchorX, toZ = half == 1 ? viaZ : anchorZ;
                PlanRoute(grid, fromX, child.Y, fromZ, toX, toZ, tickId, out bool hitsRiver, out bool noPath);
                last = EnvelopmentManeuver.ApplyHalf(in last, half, hitsRiver, noPath, now, anchorX, anchorZ, hasVia, viaX, viaZ);
                side.MarchChecks[childId] = last;
                side.PlanChildId = null;
            }
            input.MarchRouteKnown = last.Valid;
            input.MarchLegHitsRiver = last.HitsRiver;
            input.MarchRouteMissing = last.NoPath;
        }

        // One half of the march leg judged on the route the child will walk: a NavMesh path, priced like the posture probe, each segment
        // sampled on the BC10 grid (a river crossed by a bridge or ford is not a river). No complete path (or a failed query) is a result
        // too: NoPath.
        private static void PlanRoute(BattleTerrainGrid grid, float fromX, float fromY, float fromZ, float toX, float toZ, long tickId,
            out bool hitsRiver, out bool noPath)
        {
            hitsRiver = false;
            noPath = true;
            try
            {
                if (_routeFilterTick != tickId)
                {
                    _routeFilter = WhiskeyRealism.Tactical.Battle.Producers.PostureTargets.CreateNavFilter();
                    _routeFilterTick = tickId;
                }
                NavMeshPath path = WhiskeyRealism.Tactical.Battle.Producers.PostureTargets.SharedPath();
                if (NavMesh.CalculatePath(new Vector3(fromX, fromY, fromZ), new Vector3(toX, HeightAt(toX, toZ), toZ), _routeFilter, path)
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
                        noPath = false;
                        hitsRiver = EnvelopmentManeuver.RouteHitsRiver(grid, _routeX, _routeZ, count, EnvelopmentManeuver.MarchStepMeters, _riverSamples);
                    }
                }
            }
            catch (Exception e)
            {
                OnceLog.Warning("play-executor-runtime:march-route-exception",
                    "PlayExecutorRuntime march route plan failed, the leg counts as unwalkable: " + e.GetType().Name + " " + e.Message);
            }
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
                // BC14: the doctrine clock at the transition, the ledger's release and budget the maneuver latched, its vias, and on the Turn the
                // arrival against the prediction latched at the March (criterion-arrival-prediction).
                double now = TacticalDoctrineTime.NowBattleHours;
                double plannedRelease = state.HasFirstPlannedRelease ? state.FirstPlannedReleaseHours : -1.0;
                double budget = state.HasBudget ? state.BudgetHours : -1.0;
                bool hasVia = state.HasVia, hasAttackVia = state.HasAttackVia;
                float viaX = hasVia ? state.ViaX : 0f, viaZ = hasVia ? state.ViaZ : 0f;
                float attackViaX = hasAttackVia ? state.AttackViaX : 0f, attackViaZ = hasAttackVia ? state.AttackViaZ : 0f;
                bool turn = state.Phase == ManeuverPhase.Turn;
                double predictedArrival = -1.0, predictedMarch = -1.0;
                if (turn && allianceId >= 0 && allianceId < SideCount && _maneuvers[allianceId].Predicted.TryGetValue(childId, out var predicted))
                {
                    predictedArrival = predicted.arrival;
                    predictedMarch = predicted.march;
                }
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
                        .WithField("targetZ", targetZ)
                        .WithField("doctrineHours", now)
                        .WithField("plannedReleaseHours", plannedRelease)
                        .WithField("timeBudgetHours", budget)
                        .WithField("via", hasVia)
                        .WithField("viaX", viaX)
                        .WithField("viaZ", viaZ)
                        .WithField("attackVia", hasAttackVia)
                        .WithField("attackViaX", attackViaX)
                        .WithField("attackViaZ", attackViaZ)
                        .WithField("arrivedHours", turn ? now : -1.0)
                        .WithField("predictedArrivalHours", predictedArrival)
                        .WithField("predictedMarchHours", predictedMarch));
            }
            catch { }
        }
    }
}
