using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal static class TacticalVisionRuntimeAdapter
    {
        private static FieldInfo _objectiveChainField;
        private static readonly Dictionary<string, FieldInfo> _fieldCache = new Dictionary<string, FieldInfo>();

        public static ObjectiveRecord[] BuildObjectiveRecords(
            IReadOnlyList<ObjectiveObservationInput> observations,
            IReadOnlyList<TacticalObjectiveStatus> statuses,
            IReadOnlyList<float> enemyStrengths,
            IReadOnlyList<float> friendlyAssignedStrengths)
        {
            if (observations == null || observations.Count == 0) return Array.Empty<ObjectiveRecord>();

            var records = new ObjectiveRecord[observations.Count];
            for (int i = 0; i < observations.Count; i++)
            {
                records[i] = new ObjectiveRecord(
                    TacticalObjectiveSourceModel.Normalize(observations[i]),
                    GetOrDefault(statuses, i, TacticalObjectiveStatus.Unknown),
                    GetOrDefault(enemyStrengths, i, 0f),
                    GetOrDefault(friendlyAssignedStrengths, i, 0f));
            }

            return records;
        }

        internal static ObjectiveRecord[] BuildObjectiveRecordsWithFallback(
            IReadOnlyList<ObjectiveObservationInput> observations,
            IReadOnlyList<TacticalObjectiveStatus> statuses,
            IReadOnlyList<float> enemyStrengths,
            IReadOnlyList<float> friendlyAssignedStrengths,
            TacticalMapPoint? visibleEnemyLine,
            float visibleEnemyStrength,
            float visibleFriendlyStrength,
            int allianceId)
        {
            if (observations != null && observations.Count > 0)
            {
                if (!visibleEnemyLine.HasValue ||
                    !IsUsableMapPoint(visibleEnemyLine.Value) ||
                    HasVisibleEnemyLineObservation(observations, allianceId))
                {
                    return BuildObjectiveRecords(observations, statuses, enemyStrengths, friendlyAssignedStrengths);
                }

                var mergedObservations = new List<ObjectiveObservationInput>(observations.Count + 1);
                var mergedStatuses = new List<TacticalObjectiveStatus>(observations.Count + 1);
                var mergedEnemyStrengths = new List<float>(observations.Count + 1);
                var mergedFriendlyStrengths = new List<float>(observations.Count + 1);

                for (int i = 0; i < observations.Count; i++)
                {
                    mergedObservations.Add(observations[i]);
                    mergedStatuses.Add(GetOrDefault(statuses, i, TacticalObjectiveStatus.Unknown));
                    mergedEnemyStrengths.Add(GetOrDefault(enemyStrengths, i, 0f));
                    mergedFriendlyStrengths.Add(GetOrDefault(friendlyAssignedStrengths, i, 0f));
                }

                AddVisibleEnemyLineFallback(
                    mergedObservations,
                    mergedStatuses,
                    mergedEnemyStrengths,
                    mergedFriendlyStrengths,
                    visibleEnemyLine.Value,
                    visibleEnemyStrength,
                    visibleFriendlyStrength,
                    allianceId);

                return BuildObjectiveRecords(mergedObservations, mergedStatuses, mergedEnemyStrengths, mergedFriendlyStrengths);
            }

            if (!visibleEnemyLine.HasValue || !IsUsableMapPoint(visibleEnemyLine.Value))
            {
                return Array.Empty<ObjectiveRecord>();
            }

            var fallbackObservations = new List<ObjectiveObservationInput>(1);
            var fallbackStatuses = new List<TacticalObjectiveStatus>(1);
            var fallbackEnemyStrengths = new List<float>(1);
            var fallbackFriendlyStrengths = new List<float>(1);
            AddVisibleEnemyLineFallback(
                fallbackObservations,
                fallbackStatuses,
                fallbackEnemyStrengths,
                fallbackFriendlyStrengths,
                visibleEnemyLine.Value,
                visibleEnemyStrength,
                visibleFriendlyStrength,
                allianceId);

            return BuildObjectiveRecords(fallbackObservations, fallbackStatuses, fallbackEnemyStrengths, fallbackFriendlyStrengths);
        }

        internal static ObjectiveRecord[] BuildObjectiveRecordsWithMovementFallback(
            IReadOnlyList<ObjectiveObservationInput> observations,
            IReadOnlyList<TacticalObjectiveStatus> statuses,
            IReadOnlyList<float> enemyStrengths,
            IReadOnlyList<float> friendlyAssignedStrengths,
            TacticalMapPoint? visibleEnemyLine,
            float visibleEnemyStrength,
            float visibleFriendlyStrength,
            TacticalMapPoint? movementAnchor,
            float movementAnchorFriendlyStrength,
            int allianceId)
        {
            var records = BuildObjectiveRecordsWithFallback(
                observations,
                statuses,
                enemyStrengths,
                friendlyAssignedStrengths,
                visibleEnemyLine,
                visibleEnemyStrength,
                visibleFriendlyStrength,
                allianceId);

            if (records.Length > 0 ||
                !movementAnchor.HasValue ||
                !IsUsableMapPoint(movementAnchor.Value))
            {
                return records;
            }

            var fallbackObservations = new List<ObjectiveObservationInput>(1);
            var fallbackStatuses = new List<TacticalObjectiveStatus>(1);
            var fallbackEnemyStrengths = new List<float>(1);
            var fallbackFriendlyStrengths = new List<float>(1);
            AddMovementAnchorFallback(
                fallbackObservations,
                fallbackStatuses,
                fallbackEnemyStrengths,
                fallbackFriendlyStrengths,
                movementAnchor.Value,
                movementAnchorFriendlyStrength,
                allianceId);

            return BuildObjectiveRecords(fallbackObservations, fallbackStatuses, fallbackEnemyStrengths, fallbackFriendlyStrengths);
        }

        private static void AddVisibleEnemyLineFallback(
            List<ObjectiveObservationInput> observations,
            List<TacticalObjectiveStatus> statuses,
            List<float> enemyStrengths,
            List<float> friendlyStrengths,
            TacticalMapPoint visibleEnemyLine,
            float visibleEnemyStrength,
            float visibleFriendlyStrength,
            int allianceId)
        {
            float fallbackConfidence = visibleEnemyStrength > 0f && !float.IsNaN(visibleEnemyStrength) && !float.IsInfinity(visibleEnemyStrength)
                ? 0.70f
                : 0.55f;

            observations.Add(new ObjectiveObservationInput(
                "enemy-line-" + allianceId,
                TacticalObjectiveType.EnemyLine,
                TacticalObjectiveSource.VisibleEnemyLine,
                visibleEnemyLine,
                sourceConfidence: fallbackConfidence,
                value: 0.4f,
                typeAnchorVerified: true));
            statuses.Add(TacticalObjectiveStatus.Contested);
            enemyStrengths.Add(visibleEnemyStrength);
            friendlyStrengths.Add(visibleFriendlyStrength);
        }

        private static bool HasVisibleEnemyLineObservation(
            IReadOnlyList<ObjectiveObservationInput> observations,
            int allianceId)
        {
            string fallbackId = "enemy-line-" + allianceId;
            for (int i = 0; i < observations.Count; i++)
            {
                ObjectiveObservationInput observation = observations[i];
                if (observation.Source == TacticalObjectiveSource.VisibleEnemyLine ||
                    string.Equals(observation.ObjectiveId, fallbackId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddMovementAnchorFallback(
            List<ObjectiveObservationInput> observations,
            List<TacticalObjectiveStatus> statuses,
            List<float> enemyStrengths,
            List<float> friendlyStrengths,
            TacticalMapPoint movementAnchor,
            float movementAnchorFriendlyStrength,
            int allianceId)
        {
            observations.Add(new ObjectiveObservationInput(
                "movement-anchor-" + allianceId,
                TacticalObjectiveType.StagingArea,
                TacticalObjectiveSource.FriendlyLineShape,
                movementAnchor,
                sourceConfidence: 0.60f,
                value: 0.35f,
                typeAnchorVerified: false));
            statuses.Add(TacticalObjectiveStatus.Scouting);
            enemyStrengths.Add(0f);
            friendlyStrengths.Add(movementAnchorFriendlyStrength);
        }


        /// <summary>
        /// A current-set objective of an own unit (Regiment.currentsetobjective), read live by
        /// the runtime caller for the snapshot's own index. The snapshot does not carry it.
        /// </summary>
        internal readonly struct OwnSetObjective
        {
            public OwnSetObjective(int ownIndex, string name, TacticalMapPoint point)
            {
                OwnIndex = ownIndex;
                Name = name ?? string.Empty;
                Point = point;
            }

            public int OwnIndex { get; }
            public string Name { get; }
            public TacticalMapPoint Point { get; }
        }

        /// <summary>
        /// Objective records for one side from its battle snapshot (plan D11): own units and
        /// strengths from snapshot.Own, enemy strength only from the side's visible
        /// observations (closest visible enemy per own unit, BC2a). Sources, in order: the
        /// side's objective chain, own units' current-set objectives
        /// (<paramref name="ownObjectives"/>, own-index order = completeunitlist order), map
        /// objectives, then the visible-enemy-line and movement-anchor fallbacks; approach
        /// avenues are matched last, the enemy's only where the side perceives them.
        /// </summary>
        internal static ObjectiveRecord[] BuildObjectiveRecordsFromAggregate(
            IObservationSource source,
            IReadOnlyList<OwnSetObjective> ownObjectives,
            AIBattle battle,
            int allianceId)
        {
            try
            {
                if (source == null) return Array.Empty<ObjectiveRecord>();

                var observations = new List<ObjectiveObservationInput>();
                var statuses = new List<TacticalObjectiveStatus>();
                var enemyStrengths = new List<float>();
                var friendlyStrengths = new List<float>();
                var seenObjectiveIds = new HashSet<string>(StringComparer.Ordinal);

                AddObjectiveChainObservations(
                    battle,
                    source,
                    observations,
                    statuses,
                    enemyStrengths,
                    friendlyStrengths,
                    seenObjectiveIds);

                // Own units with a current-set objective; routed, marked for rout and
                // permanently detached units are skipped. Id formula as shipped:
                //   string.IsNullOrWhiteSpace(name) ? "objective-" + observations.Count : name
                for (int k = 0; ownObjectives != null && k < ownObjectives.Count; k++)
                {
                    OwnSetObjective objective = ownObjectives[k];
                    int idx = objective.OwnIndex;
                    if (idx < 0 || idx >= source.OwnCount || !IsUsableOwnUnit(in source.Own[idx])) continue;
                    if (!IsUsableMapPoint(objective.Point)) continue;

                    string objectiveId = string.IsNullOrWhiteSpace(objective.Name)
                        ? "objective-" + observations.Count
                        : objective.Name;
                    if (!seenObjectiveIds.Add(objectiveId)) continue;

                    observations.Add(new ObjectiveObservationInput(
                        objectiveId,
                        TacticalObjectiveType.UnknownVanillaObjective,
                        TacticalObjectiveSource.CurrentSetObjective,
                        objective.Point,
                        sourceConfidence: 0.65f,
                        value: 0.5f,
                        typeAnchorVerified: false));
                    statuses.Add(TacticalObjectiveStatus.Scouting);
                    enemyStrengths.Add(ClosestVisibleEnemyStrength(source, idx));
                    friendlyStrengths.Add(OwnStrength(in source.Own[idx]));
                }

                AddMapObjectiveObservations(
                    TacticalStableSceneCache.GetOrCaptureMapObjectives(),
                    allianceId,
                    observations,
                    statuses,
                    enemyStrengths,
                    friendlyStrengths,
                    seenObjectiveIds);

                ApplyApproachAvenues(
                    observations,
                    allianceId,
                    TacticalStableSceneCache.GetOrCaptureApproachAvenues(allianceId),
                    source);

                bool haveVisibleEnemy = TryVisibleEnemyLineFromAggregate(
                    source,
                    allianceId,
                    out TacticalMapPoint visibleEnemyPoint,
                    out float visibleEnemyStrength,
                    out float visibleFriendlyStrength);
                TacticalMapPoint? visibleEnemyLine = haveVisibleEnemy
                    ? visibleEnemyPoint
                    : (TacticalMapPoint?)null;

                ApplyTerrainPicture(observations, source);

                bool haveMovementAnchor = TryMovementAnchorLineFromAggregate(
                    source,
                    allianceId,
                    out TacticalMapPoint movementAnchorPoint,
                    out float movementAnchorFriendlyStrength);
                TacticalMapPoint? movementAnchor = haveMovementAnchor
                    ? movementAnchorPoint
                    : (TacticalMapPoint?)null;

                return BuildObjectiveRecordsWithMovementFallback(
                    observations,
                    statuses,
                    enemyStrengths,
                    friendlyStrengths,
                    visibleEnemyLine,
                    visibleEnemyStrength,
                    visibleFriendlyStrength,
                    movementAnchor,
                    movementAnchorFriendlyStrength,
                    allianceId);
            }
            catch (Exception e)
            {
                OnceLog.Warning("tactical-orch:objective-records-aggregate",
                    "BuildObjectiveRecordsFromAggregate degraded: "
                    + e.GetType().Name + " " + e.Message);
                return Array.Empty<ObjectiveRecord>();
            }
        }

        // Map objectives come from TacticalStableSceneCache (vanilla objective references
        // cached per battle; owner and active state read live on every capture, BR-H5)
        // instead of a FindObjectsOfType<Objectives>() scan per heavy build.
        internal static void AddMapObjectiveObservations(
            IReadOnlyList<MapObjectiveCapture> mapObjectives,
            int allianceId,
            List<ObjectiveObservationInput> observations,
            List<TacticalObjectiveStatus> statuses,
            List<float> enemyStrengths,
            List<float> friendlyStrengths,
            HashSet<string> seenObjectiveIds)
        {
            if (mapObjectives == null) return;

            // BC14O (requirement-objective-value): under Enable Objective Doctrine an objective is worth vanilla's own points, normalised over
            // the live objectives; otherwise the fixed 0.45 / 0.80.
            bool pointValues = Plugin.ObjectiveDoctrineEnabled();
            float maxLivePoints = pointValues ? MaxLivePoints(mapObjectives) : 0f;

            for (int i = 0; i < mapObjectives.Count; i++)
            {
                var capture = mapObjectives[i];
                if (!capture.IsActive) continue;
                if (!IsUsableMapPoint(capture.Point)) continue;

                string id = string.IsNullOrWhiteSpace(capture.ObjectiveId)
                    ? "objective-" + observations.Count
                    : capture.ObjectiveId;
                if (!seenObjectiveIds.Add(id)) continue;

                bool friendlyOwned = capture.Owner == allianceId;
                observations.Add(new ObjectiveObservationInput(
                    id,
                    TacticalObjectiveType.VictoryPoint,
                    TacticalObjectiveSource.VerifiedSceneObject,
                    capture.Point,
                    sourceConfidence: 0.90f,
                    value: pointValues
                        ? ObjectiveScoring.Value(ObjectiveScoring.Normalised(capture.Points, maxLivePoints), friendlyOwned)
                        : (friendlyOwned ? 0.45f : 0.80f),
                    typeAnchorVerified: true));
                statuses.Add(friendlyOwned ? TacticalObjectiveStatus.Secured : TacticalObjectiveStatus.Scouting);
                enemyStrengths.Add(0f);
                friendlyStrengths.Add(0f);
            }
        }

        /// <summary>The largest <see cref="MapObjectiveCapture.Points"/> over the live objectives (gameObject.activeInHierarchy, as vanilla tests one); 0 with none.</summary>
        internal static float MaxLivePoints(IReadOnlyList<MapObjectiveCapture> captures)
        {
            float max = 0f;
            for (int i = 0; captures != null && i < captures.Count; i++)
                if (captures[i].LiveInHierarchy && captures[i].Points > max) max = captures[i].Points;
            return max;
        }

        // Main-thread map-objective capture. Scans the scene once and extracts pure
        // data into a MapObjectiveCapture[]. Caller controls cache lifetime; this
        // function always does a fresh scan. Promoted to internal so
        // TacticalStableSceneCache (battle-lifetime cache) can populate from it.
        internal static MapObjectiveCapture[] CaptureMapObjectivesLive()
        {
            try
            {
                var mapObjectives = SceneObjectives();
                if (mapObjectives.Length == 0) return Array.Empty<MapObjectiveCapture>();

                var result = new MapObjectiveCapture[mapObjectives.Length];
                for (int i = 0; i < mapObjectives.Length; i++)
                {
                    Objectives obj = mapObjectives[i];
                    result[i] = ReadObjectiveLive(obj, MapObjectiveId(obj, i), SafeObjectivePoint(obj));
                }
                return result;
            }
            catch
            {
                return Array.Empty<MapObjectiveCapture>();
            }
        }

        /// <summary>
        /// One objective as a capture, the single place both builders (the live scan and the scene cache's) read it: owner and points are managed
        /// fields and are read first; the two liveness flags are native reads, each in a method of its own and guarded on its own, so a failed one
        /// costs only its own flag. The null test is on the reference: a Unity null (a destroyed object) still has its last owner and points, and
        /// its flags read false.
        /// </summary>
        internal static MapObjectiveCapture ReadObjectiveLive(Objectives obj, string id, TacticalMapPoint point)
        {
            if (ReferenceEquals(obj, null)) return new MapObjectiveCapture(id, point, -2, false);
            int owner = -2, points = 0;
            bool active = false, live = false;
            try { owner = obj.owner; points = obj.points; } catch { }
            try { active = IsActiveAndEnabled(obj); } catch { }
            try { live = IsActiveInHierarchy(obj); } catch { }
            return new MapObjectiveCapture(id, point, owner, active, points, live);
        }

        // The native reads, one method each and never inlined: the runtime resolves a native call when the method holding it is compiled, so
        // outside the game (the test harness) the method that holds one cannot even start. Kept apart, the failure is the caller's to catch.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsActiveAndEnabled(Objectives obj) => obj.isActiveAndEnabled;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsActiveInHierarchy(Objectives obj) => obj.gameObject.activeInHierarchy;

        /// <summary>
        /// Vanilla's own objective list: BattleUnits.objectives (static, 78910), built in
        /// InitBattleUnits from Battlemap/Map/Objectives incl. inactive ones; activation
        /// is restored by Objectives.Load (106017) before the AI runs. Falls back to a
        /// scene scan (active objects only) when the static is unset.
        /// </summary>
        internal static Objectives[] SceneObjectives()
        {
            try
            {
                var vanilla = BattleUnits.objectives;
                if (vanilla != null && vanilla.Length > 0) return vanilla;
                return UnityEngine.Object.FindObjectsOfType<Objectives>() ?? Array.Empty<Objectives>();
            }
            catch
            {
                return Array.Empty<Objectives>();
            }
        }

        internal static string MapObjectiveId(Objectives objective, int index)
        {
            return SafeObjectiveId(objective, index);
        }

        /// <summary>
        /// Cheap fingerprint of everything the approach-avenue scan depends on that
        /// vanilla mutates mid-battle (see TacticalAvenueSourceSignature).
        /// </summary>
        internal static TacticalAvenueSourceSignature ReadAvenueSourceSignature(BattleUnits battleUnits)
        {
            try
            {
                if (battleUnits == null) return default(TacticalAvenueSourceSignature);
                int arrivals = battleUnits.scheduledarrival != null ? battleUnits.scheduledarrival.Count : 0;
                int ownerHash = 17;
                var entryPoints = battleUnits.entrypoints;
                if (entryPoints != null)
                {
                    for (int i = 0; i < entryPoints.Length; i++)
                    {
                        unchecked { ownerHash = ownerHash * 31 + (entryPoints[i] != null ? entryPoints[i].owner + 2 : 0); }
                    }
                }
                return new TacticalAvenueSourceSignature(arrivals, ownerHash, battleUnits.battlepasseddays);
            }
            catch
            {
                return default(TacticalAvenueSourceSignature);
            }
        }

        private static T GetOrDefault<T>(IReadOnlyList<T> values, int index, T fallback)
        {
            if (values == null || index < 0 || index >= values.Count) return fallback;
            return values[index];
        }

        // Shipped own-unit filter: not routed, not marked for rout, not permanently detached.
        private static bool IsUsableOwnUnit(in UnitRecord unit)
        {
            return (unit.Flags & (UnitFlags.Routed | UnitFlags.MarkedForRout | UnitFlags.PermanentlyDetached)) == 0;
        }

        /// <summary>
        /// An own unit's strength as the objective records weigh it: groupstrengthaigroup, the
        /// field the shipped reflected read resolved (UpdateGroupStrength 120176/120297; 0 on
        /// regiments, so leaves add nothing and groups are not double counted).
        /// </summary>
        internal static float OwnStrength(in UnitRecord unit)
        {
            return Math.Max(0, unit.GroupStrengthAiGroup);
        }

        // Strength of the own unit's closest visible enemy; 0 when it sees none.
        private static float ClosestVisibleEnemyStrength(IObservationSource source, int ownIndex)
        {
            int k = source.ClosestVisibleEnemy(ownIndex, out _);
            return k >= 0 ? Math.Max(0f, source.Observations[k].Strength) : 0f;
        }

        private static void AddObjectiveChainObservations(
            AIBattle battle,
            IObservationSource source,
            List<ObjectiveObservationInput> observations,
            List<TacticalObjectiveStatus> statuses,
            List<float> enemyStrengths,
            List<float> friendlyStrengths,
            HashSet<string> seenObjectiveIds)
        {
            var chains = SafeObjectiveChains(battle);
            if (chains == null) return;

            for (int i = 0; i < chains.Count; i++)
            {
                object chain = chains[i];
                if (chain == null) continue;
                if (SafeIntField(chain, "usedstrategyid", 0) < 0) continue;

                var objectives = SafeObjectList(chain, "objectives");
                if (objectives == null || objectives.Count == 0) continue;

                for (int j = 0; j < objectives.Count; j++)
                {
                    object objective = objectives[j];
                    if (objective == null) continue;

                    TacticalMapPoint point = SafeObjectivePoint(objective);
                    if (!IsUsableMapPoint(point)) continue;

                    string id = SafeObjectiveId(objective, observations.Count);
                    if (!seenObjectiveIds.Add(id)) continue;

                    observations.Add(new ObjectiveObservationInput(
                        id,
                        TacticalObjectiveType.UnknownVanillaObjective,
                        TacticalObjectiveSource.ObjectiveChain,
                        point,
                        sourceConfidence: 0.75f,
                        value: 0.65f,
                        typeAnchorVerified: false));
                    statuses.Add(TacticalObjectiveStatus.Scouting);
                    enemyStrengths.Add(EstimateChainEnemyStrength(chain, source));
                    friendlyStrengths.Add(EstimateChainFriendlyStrength(chain, source));
                }
            }
        }

        private static void ApplyApproachAvenues(
            List<ObjectiveObservationInput> observations,
            int allianceId,
            TacticalApproachAvenueObservation[] allAvenues,
            IObservationSource source)
        {
            if (observations == null ||
                observations.Count == 0 ||
                allAvenues == null ||
                allAvenues.Length == 0)
            {
                return;
            }

            var known = new List<TacticalApproachAvenueObservation>(allAvenues.Length);
            for (int i = 0; i < allAvenues.Length; i++)
                if (IsAvenueKnown(in allAvenues[i], allianceId, source)) known.Add(allAvenues[i]);
            if (known.Count == 0) return;
            TacticalApproachAvenueObservation[] avenueObservations = known.ToArray();

            int enemyAllianceId = ResolveEnemyAlliance(allianceId);
            for (int i = 0; i < observations.Count; i++)
            {
                ObjectiveObservationInput observation = observations[i];
                var objective = new BattlefieldObjectiveEstimate(
                    observation.ObjectiveId,
                    observation.Type,
                    0f,
                    observation.SourceConfidence,
                    false,
                    observation.Value,
                    observation.Location.X,
                    observation.Location.Z,
                    0f,
                    0f);
                TacticalApproachAvenueEstimate avenue = TacticalApproachAvenuePlanner.SelectBest(
                    objective,
                    allianceId,
                    enemyAllianceId,
                    avenueObservations);
                if (!avenue.HasAvenue) continue;

                observations[i] = observation.WithAvenue(Math.Max(observation.SourceConfidence, avenue.Confidence01 * 0.85f), avenue);
            }
        }

        // BC12 decisions 3-4: the objectives are read against the terrain picture (type, terrain strength, approach difficulty).
        // Each objective has its own axis, from the side's own centroid to that objective, and the directional terms describe the
        // ground the own side has to cross to reach it: the picture is asked with the axis reversed (toward the own side), so
        // the 300 m approach and the water 100-300 m ahead are sampled on the own side of the objective and end at it. An enemy
        // ridge with a river in front of us is hard to take; a river behind the ridge is not our crossing. Nothing without a
        // Ready picture, or with Enable Battle Terrain Picture off (TerrainPicture.CurrentForPlanning).
        internal static void ApplyTerrainPicture(List<ObjectiveObservationInput> observations, IObservationSource source)
        {
            TerrainPicture picture = TerrainPicture.CurrentForPlanning();
            if (picture == null || observations == null || observations.Count == 0 || source == null) return;

            float ownX = 0f, ownZ = 0f;
            int n = 0;
            for (int i = 0; i < source.OwnCount; i++)
            {
                if (!IsUsableOwnUnit(in source.Own[i])) continue;
                ownX += source.Own[i].X;
                ownZ += source.Own[i].Z;
                n++;
            }
            bool haveOwn = n > 0;
            if (haveOwn) { ownX /= n; ownZ /= n; }
            for (int i = 0; i < observations.Count; i++)
            {
                ObjectiveObservationInput observation = observations[i];
                float axisX = 0f, axisZ = 0f;
                if (haveOwn)
                {
                    // From the objective toward the own side: the direction the ground to cross lies in.
                    axisX = ownX - observation.Location.X;
                    axisZ = ownZ - observation.Location.Z;
                }
                observations[i] = TacticalTerrainObjectiveClassifier.Classify(in observation, picture, axisX, axisZ);
            }
        }

        // A reinforcement observation sits at its entry point (BattlePerceptionProbe reads
        // EntryPoint.entrypointposition); avenue origins come from the same field.
        private const float EntryPointMatchMeters = 1f;

        /// <summary>
        /// Whether the side may use an approach avenue. Own and unowned avenues and entry-point
        /// avenues (EntryPoint.owner is map knowledge) always; an enemy column's scheduled
        /// arrival (BattleUnits.scheduledarrival 78960) only while perception sees its entry
        /// point, i.e. the snapshot holds a visible reinforcement observation there; an enemy
        /// deployment group's start/objective (BattleUnits.grp) never, as no perception reveals it.
        /// </summary>
        internal static bool IsAvenueKnown(in TacticalApproachAvenueObservation avenue, int allianceId, IObservationSource source)
        {
            int owner = avenue.OwnerAllianceId;
            if (owner < 0 || owner == allianceId || avenue.Source == TacticalApproachAvenueSource.EntryPoint) return true;
            if (avenue.Source != TacticalApproachAvenueSource.ScheduledArrival || source == null) return false;
            EnemyObservation[] observations = source.Observations;
            for (int i = 0; i < source.ObservationCount; i++)
            {
                ref EnemyObservation o = ref observations[i];
                if (!o.IsReinforcement || o.State != ObservationState.Visible) continue;
                if (Math.Abs(o.X - avenue.Origin.X) <= EntryPointMatchMeters && Math.Abs(o.Z - avenue.Origin.Z) <= EntryPointMatchMeters)
                    return true;
            }
            return false;
        }

        // Promoted to internal so TacticalStableSceneCache can populate from it during
        // battle-start cache initialization. Function body unchanged — single live scan
        // of EntryPoints + scheduledarrival + deployment groups.
        internal static TacticalApproachAvenueObservation[] BuildApproachAvenueObservationsLive(int allianceId)
        {
            try
            {
                return BuildApproachAvenueObservations(UnityEngine.Object.FindObjectOfType<BattleUnits>());
            }
            catch
            {
                return Array.Empty<TacticalApproachAvenueObservation>();
            }
        }

        internal static TacticalApproachAvenueObservation[] BuildApproachAvenueObservations(BattleUnits battleUnits)
        {
            try
            {
                if (battleUnits == null) return Array.Empty<TacticalApproachAvenueObservation>();

                var observations = new List<TacticalApproachAvenueObservation>();
                BattleTerrainGrid grid = BattleTerrainGrid.Current();
                AddEntryPointAvenues(battleUnits, grid, observations);
                AddScheduledArrivalAvenues(battleUnits, grid, observations);
                AddDeploymentGroupAvenues(battleUnits, grid, observations);
                return observations.ToArray();
            }
            catch
            {
                return Array.Empty<TacticalApproachAvenueObservation>();
            }
        }

        private static void AddEntryPointAvenues(
            BattleUnits battleUnits,
            BattleTerrainGrid grid,
            List<TacticalApproachAvenueObservation> observations)
        {
            EntryPoint[] entryPoints = SafeEntryPoints(battleUnits);
            if (entryPoints == null) return;

            for (int i = 0; i < entryPoints.Length; i++)
            {
                EntryPoint entryPoint = entryPoints[i];
                if (entryPoint == null) continue;
                if (!TryEntryPointAvenue(
                    entryPoint,
                    grid,
                    out TacticalMapPoint origin,
                    out TacticalMapPoint target,
                    out bool roadAnchored,
                    out bool crossingAnchored))
                {
                    continue;
                }

                observations.Add(new TacticalApproachAvenueObservation(
                    TacticalApproachAvenueSource.EntryPoint,
                    SafeIntField(entryPoint, "owner", -1),
                    origin.X,
                    origin.Z,
                    target.X,
                    target.Z,
                    0.70f,
                    roadAnchored,
                    crossingAnchored,
                    "entrypoint-" + i));
            }
        }

        private static void AddScheduledArrivalAvenues(
            BattleUnits battleUnits,
            BattleTerrainGrid grid,
            List<TacticalApproachAvenueObservation> observations)
        {
            IList arrivals = SafeObjectList(battleUnits, "scheduledarrival");
            if (arrivals == null) return;

            for (int i = 0; i < arrivals.Count; i++)
            {
                object arrival = arrivals[i];
                if (arrival == null) continue;
                EntryPoint entryPoint = SafeField(arrival, "entrypoint")?.GetValue(arrival) as EntryPoint;
                if (entryPoint == null) continue;
                if (!TryEntryPointAvenue(
                    entryPoint,
                    grid,
                    out TacticalMapPoint origin,
                    out TacticalMapPoint target,
                    out bool roadAnchored,
                    out bool crossingAnchored))
                {
                    continue;
                }

                Regiment regiment = SafeRegimentField(arrival, "regimentrefreg");
                int owner = regiment != null ? regiment.alliance : SafeIntField(entryPoint, "owner", -1);
                observations.Add(new TacticalApproachAvenueObservation(
                    TacticalApproachAvenueSource.ScheduledArrival,
                    owner,
                    origin.X,
                    origin.Z,
                    target.X,
                    target.Z,
                    0.85f,
                    roadAnchored,
                    crossingAnchored,
                    "scheduled-arrival-" + i));
            }
        }

        private static void AddDeploymentGroupAvenues(
            BattleUnits battleUnits,
            BattleTerrainGrid grid,
            List<TacticalApproachAvenueObservation> observations)
        {
            IList groups = SafeObjectList(battleUnits, "grp");
            if (groups == null) return;

            for (int i = 0; i < groups.Count; i++)
            {
                object group = groups[i];
                if (group == null) continue;
                if (!TryVectorField(group, "startposition", out Vector3 start) ||
                    !TryVectorField(group, "objectiveposition", out Vector3 objective))
                {
                    continue;
                }

                var origin = new TacticalMapPoint(start.x, start.z);
                var target = new TacticalMapPoint(objective.x, objective.z);
                if (!IsUsableMapPoint(origin) || !IsUsableMapPoint(target)) continue;

                observations.Add(new TacticalApproachAvenueObservation(
                    TacticalApproachAvenueSource.DeploymentGroup,
                    SafeIntField(group, "alliance", -1),
                    origin.X,
                    origin.Z,
                    target.X,
                    target.Z,
                    0.60f,
                    roadAnchored: false,
                    crossingAnchored: AvenueAnchors.CrossingAnchored(grid, origin.X, origin.Z, target.X, target.Z),
                    "deployment-group-" + i));
            }
        }

        private static bool TryEntryPointAvenue(
            EntryPoint entryPoint,
            BattleTerrainGrid grid,
            out TacticalMapPoint origin,
            out TacticalMapPoint target,
            out bool roadAnchored,
            out bool crossingAnchored)
        {
            origin = default;
            target = default;
            roadAnchored = false;
            crossingAnchored = false;
            if (entryPoint == null) return false;

            if (!TryVectorField(entryPoint, "entrypointposition", out Vector3 originVector) ||
                IsDefaultVector(originVector))
            {
                origin = SafeObjectivePoint(entryPoint);
            }
            else
            {
                origin = new TacticalMapPoint(originVector.x, originVector.z);
            }

            if (TryVectorField(entryPoint, "trooptargetposition", out Vector3 targetVector) &&
                !IsDefaultVector(targetVector))
            {
                target = new TacticalMapPoint(targetVector.x, targetVector.z);
            }
            else
            {
                target = origin;
            }

            if (!IsUsableMapPoint(origin) || !IsUsableMapPoint(target)) return false;

            // Anchors come from the terrain grid (BC10): road samples along the segment, ford or bridge
            // cells or a listed crossing near it. A missing grid anchors nothing.
            roadAnchored = AvenueAnchors.RoadAnchored(grid, origin.X, origin.Z, target.X, target.Z);
            crossingAnchored = AvenueAnchors.CrossingAnchored(grid, origin.X, origin.Z, target.X, target.Z);
            return true;
        }

        private static EntryPoint[] SafeEntryPoints(BattleUnits battleUnits)
        {
            try
            {
                if (battleUnits == null) return null;
                if (battleUnits.entrypoints != null && battleUnits.entrypoints.Length > 0)
                {
                    return battleUnits.entrypoints;
                }

                return UnityEngine.Object.FindObjectsOfType<EntryPoint>();
            }
            catch
            {
                return null;
            }
        }

        private static int ResolveEnemyAlliance(int allianceId)
        {
            if (allianceId == 0) return 1;
            if (allianceId == 1) return 0;
            return -1;
        }

        // Per own unit (routed, marked for rout and permanently detached skipped):
        //   friendlyStrength += OwnStrength(own), for every unit;
        //   the unit's closest visible enemy (snapshot, true XZ distance), when it has one at a
        //   usable point, adds its position weighted by max(1, strength) to the centroid and
        //   its strength to enemyStrength.
        // A per-own-unit closest-visible linkage, never every enemy on the map.
        private static bool TryVisibleEnemyLineFromAggregate(
            IObservationSource source,
            int allianceId,
            out TacticalMapPoint point,
            out float enemyStrength,
            out float friendlyStrength)
        {
            point = default;
            enemyStrength = 0f;
            friendlyStrength = 0f;

            float weightedX = 0f;
            float weightedZ = 0f;
            float weightTotal = 0f;
            UnitRecord[] own = source.Own;
            EnemyObservation[] observations = source.Observations;
            for (int i = 0; i < source.OwnCount; i++)
            {
                if (!IsUsableOwnUnit(in own[i])) continue;
                friendlyStrength += OwnStrength(in own[i]);

                int k = source.ClosestVisibleEnemy(i, out _);
                if (k < 0) continue;
                ref EnemyObservation enemy = ref observations[k];
                if (!IsUsableMapPoint(new TacticalMapPoint(enemy.X, enemy.Z))) continue;

                float strength = Math.Max(0f, enemy.Strength);
                float weight = Math.Max(1f, strength);
                weightedX += enemy.X * weight;
                weightedZ += enemy.Z * weight;
                weightTotal += weight;
                enemyStrength += strength;
            }

            if (weightTotal <= 0f) return false;
            var p = new TacticalMapPoint(weightedX / weightTotal, weightedZ / weightTotal);
            if (!IsUsableMapPoint(p)) return false;
            point = p;
            return true;
        }

        // Strength-weighted centroid of own units' last waypoints (lastsetwaypointposition,
        // captured per tick in UnitRecord); a waypoint unset or within 25 m of the unit is
        // ignored. friendlyStrength sums every usable own unit; weight = max(1, strength).
        private static bool TryMovementAnchorLineFromAggregate(
            IObservationSource source,
            int allianceId,
            out TacticalMapPoint point,
            out float friendlyStrength)
        {
            point = default;
            friendlyStrength = 0f;

            float weightedX = 0f;
            float weightedZ = 0f;
            float weightTotal = 0f;
            UnitRecord[] own = source.Own;
            for (int i = 0; i < source.OwnCount; i++)
            {
                if (!IsUsableOwnUnit(in own[i])) continue;
                float strength = OwnStrength(in own[i]);
                friendlyStrength += strength;
                if (!HasUsableWaypoint(in own[i])) continue;

                float weight = Math.Max(1f, strength);
                weightedX += own[i].LastWaypointX * weight;
                weightedZ += own[i].LastWaypointZ * weight;
                weightTotal += weight;
            }

            if (weightTotal <= 0f) return false;
            var p = new TacticalMapPoint(weightedX / weightTotal, weightedZ / weightTotal);
            if (!IsUsableMapPoint(p)) return false;
            point = p;
            return true;
        }

        private static bool HasUsableWaypoint(in UnitRecord unit)
        {
            if (!IsUsableMapPoint(new TacticalMapPoint(unit.LastWaypointX, unit.LastWaypointZ))) return false;
            float dx = unit.X - unit.LastWaypointX;
            float dz = unit.Z - unit.LastWaypointZ;
            return dx * dx + dz * dz >= 625f;
        }

        private static IList SafeObjectiveChains(AIBattle battle)
        {
            try
            {
                if (battle == null) return null;
                if (_objectiveChainField == null)
                    _objectiveChainField = typeof(AIBattle).GetField(
                        "objectivechain",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return _objectiveChainField?.GetValue(battle) as IList;
            }
            catch
            {
                return null;
            }
        }

        private static IList SafeObjectList(object instance, string fieldName)
        {
            try
            {
                return SafeField(instance, fieldName)?.GetValue(instance) as IList;
            }
            catch
            {
                return null;
            }
        }

        private static int SafeIntField(object instance, string fieldName, int fallback)
        {
            try
            {
                var field = SafeField(instance, fieldName);
                return field == null ? fallback : Convert.ToInt32(field.GetValue(instance));
            }
            catch
            {
                return fallback;
            }
        }

        private static FieldInfo SafeField(object instance, string fieldName)
        {
            if (instance == null || string.IsNullOrEmpty(fieldName)) return null;

            Type type = instance.GetType();
            string key = type.FullName + ":" + fieldName;
            if (_fieldCache.TryGetValue(key, out FieldInfo cached)) return cached;

            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _fieldCache[key] = field;
            return field;
        }

        private static float EstimateChainEnemyStrength(object chain, IObservationSource source)
        {
            float total = 0f;
            AddClosestEnemyStrength(SafeRegimentField(chain, "linegroup_centerunit"), source, ref total);
            AddClosestEnemyStrengthFromList(SafeObjectList(chain, "linegroup_leftunits"), source, ref total);
            AddClosestEnemyStrengthFromList(SafeObjectList(chain, "linegroup_rightunits"), source, ref total);
            return total;
        }

        private static float EstimateChainFriendlyStrength(object chain, IObservationSource source)
        {
            float total = 0f;
            AddFriendlyStrength(SafeRegimentField(chain, "linegroup_centerunit"), source, ref total);
            AddFriendlyStrengthFromList(SafeObjectList(chain, "linegroup_leftunits"), source, ref total);
            AddFriendlyStrengthFromList(SafeObjectList(chain, "linegroup_rightunits"), source, ref total);
            return total;
        }

        private static Regiment SafeRegimentField(object instance, string fieldName)
        {
            try
            {
                return SafeField(instance, fieldName)?.GetValue(instance) as Regiment;
            }
            catch
            {
                return null;
            }
        }

        // Chain line units are own regiments; both strengths come from the snapshot.
        private static int OwnIndexOf(Regiment unit, IObservationSource source)
        {
            try { return unit != null ? source.IndexOfOwn(unit.gameObject.GetInstanceID()) : -1; }
            catch { return -1; }
        }

        private static void AddClosestEnemyStrength(Regiment own, IObservationSource source, ref float total)
        {
            int idx = OwnIndexOf(own, source);
            if (idx >= 0) total += ClosestVisibleEnemyStrength(source, idx);
        }

        private static void AddClosestEnemyStrengthFromList(IList units, IObservationSource source, ref float total)
        {
            if (units == null) return;
            for (int i = 0; i < units.Count; i++) AddClosestEnemyStrength(units[i] as Regiment, source, ref total);
        }

        private static void AddFriendlyStrength(Regiment own, IObservationSource source, ref float total)
        {
            int idx = OwnIndexOf(own, source);
            if (idx >= 0) total += OwnStrength(in source.Own[idx]);
        }

        private static void AddFriendlyStrengthFromList(IList units, IObservationSource source, ref float total)
        {
            if (units == null) return;
            for (int i = 0; i < units.Count; i++) AddFriendlyStrength(units[i] as Regiment, source, ref total);
        }

        internal static object SafeCurrentSetObjective(Regiment own)
        {
            try { return SafeField(own, "currentsetobjective")?.GetValue(own); }
            catch { return null; }
        }

        private static string SafeObjectiveId(object objective, int index)
        {
            try
            {
                var name = SafeField(objective, "objectivename")?.GetValue(objective) as string;
                return string.IsNullOrWhiteSpace(name) ? "objective-" + index : name;
            }
            catch
            {
                return "objective-" + index;
            }
        }

        /// <summary>
        /// Raw objective name for <see cref="OwnSetObjective.Name"/>; empty when the name is
        /// null, whitespace or unreadable. BuildObjectiveRecordsFromAggregate applies the
        /// <c>SafeObjectiveId(obj, observations.Count)</c> fallback at consumption time, where
        /// the running count is known.
        /// </summary>
        internal static string SafeObjectiveName(object objective)
        {
            try
            {
                var name = SafeField(objective, "objectivename")?.GetValue(objective) as string;
                return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
            }
            catch
            {
                return string.Empty;
            }
        }

        internal static TacticalMapPoint SafeObjectivePoint(object objective)
        {
            try
            {
                if (objective is Component component)
                {
                    var position = component.transform.position;
                    return new TacticalMapPoint(position.x, position.z);
                }

                if (objective is GameObject gameObject)
                {
                    var position = gameObject.transform.position;
                    return new TacticalMapPoint(position.x, position.z);
                }

                if (objective is Transform transform)
                {
                    var position = transform.position;
                    return new TacticalMapPoint(position.x, position.z);
                }

                if (TryVectorField(objective, "position", out Vector3 fieldPosition) ||
                    TryVectorProperty(objective, "position", out fieldPosition))
                {
                    return new TacticalMapPoint(fieldPosition.x, fieldPosition.z);
                }

                return new TacticalMapPoint(0f, 0f);
            }
            catch
            {
                return new TacticalMapPoint(0f, 0f);
            }
        }

        private static bool TryVectorField(object instance, string fieldName, out Vector3 value)
        {
            value = default;
            try
            {
                var field = SafeField(instance, fieldName);
                if (field == null || field.FieldType != typeof(Vector3)) return false;
                value = (Vector3)field.GetValue(instance);
                return !IsDefaultVector(value);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryVectorProperty(object instance, string propertyName, out Vector3 value)
        {
            value = default;
            try
            {
                if (instance == null) return false;
                var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null || property.PropertyType != typeof(Vector3) || property.GetIndexParameters().Length != 0) return false;
                value = (Vector3)property.GetValue(instance, null);
                return !IsDefaultVector(value);
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsUsableMapPoint(TacticalMapPoint point)
        {
            return !float.IsNaN(point.X) &&
                !float.IsNaN(point.Z) &&
                !float.IsInfinity(point.X) &&
                !float.IsInfinity(point.Z) &&
                (Math.Abs(point.X) >= 0.01f || Math.Abs(point.Z) >= 0.01f);
        }

        internal static bool IsDefaultVector(Vector3 value)
        {
            return float.IsNaN(value.x) ||
                float.IsNaN(value.y) ||
                float.IsNaN(value.z) ||
                float.IsInfinity(value.x) ||
                float.IsInfinity(value.y) ||
                float.IsInfinity(value.z) ||
                (Math.Abs(value.x) < 0.01f && Math.Abs(value.z) < 0.01f);
        }

        // Test accessors — thin wrappers so harness tests can reach private static
        // aggregate sub-builders without reflection.
        internal static bool TryVisibleEnemyLineFromAggregate_ForTest(
            IObservationSource source, int allianceId,
            out TacticalMapPoint point, out float enemyStrength, out float friendlyStrength)
        {
            return TryVisibleEnemyLineFromAggregate(source, allianceId, out point, out enemyStrength, out friendlyStrength);
        }

        internal static bool TryMovementAnchorLineFromAggregate_ForTest(
            IObservationSource source, int allianceId,
            out TacticalMapPoint point, out float friendlyStrength)
        {
            return TryMovementAnchorLineFromAggregate(source, allianceId, out point, out friendlyStrength);
        }

    }
}
