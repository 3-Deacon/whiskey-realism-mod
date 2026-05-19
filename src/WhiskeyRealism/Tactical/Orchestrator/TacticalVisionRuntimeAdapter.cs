using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal static class TacticalVisionRuntimeAdapter
    {
        private static FieldInfo _objectiveChainField;
        private static readonly Dictionary<string, FieldInfo> _fieldCache = new Dictionary<string, FieldInfo>();

        public static EnemyContactReport[] BuildContactReports(
            IEnumerable<ContactObservationInput> observations,
            float staleAfterSeconds)
        {
            if (observations == null) return Array.Empty<EnemyContactReport>();

            var reports = new List<EnemyContactReport>();
            foreach (var observation in observations)
            {
                reports.Add(TacticalVisionModel.BuildContact(observation, staleAfterSeconds));
            }

            return reports.ToArray();
        }

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

        internal static ObjectiveRecord[] BuildObjectiveRecordsWithSceneObjectivesAndMovementFallback(
            IReadOnlyList<ObjectiveObservationInput> observations,
            IReadOnlyList<TacticalObjectiveStatus> statuses,
            IReadOnlyList<float> enemyStrengths,
            IReadOnlyList<float> friendlyAssignedStrengths,
            IReadOnlyList<ObjectiveObservationInput> sceneObjectives,
            IReadOnlyList<TacticalObjectiveStatus> sceneStatuses,
            TacticalMapPoint? visibleEnemyLine,
            float visibleEnemyStrength,
            float visibleFriendlyStrength,
            TacticalMapPoint? movementAnchor,
            float movementAnchorFriendlyStrength,
            int allianceId)
        {
            var mergedObservations = new List<ObjectiveObservationInput>();
            var mergedStatuses = new List<TacticalObjectiveStatus>();
            var mergedEnemyStrengths = new List<float>();
            var mergedFriendlyStrengths = new List<float>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            CopyObjectiveInputs(
                observations,
                statuses,
                enemyStrengths,
                friendlyAssignedStrengths,
                mergedObservations,
                mergedStatuses,
                mergedEnemyStrengths,
                mergedFriendlyStrengths,
                seen);

            CopyObjectiveInputs(
                sceneObjectives,
                sceneStatuses,
                Array.Empty<float>(),
                Array.Empty<float>(),
                mergedObservations,
                mergedStatuses,
                mergedEnemyStrengths,
                mergedFriendlyStrengths,
                seen);

            return BuildObjectiveRecordsWithMovementFallback(
                mergedObservations,
                mergedStatuses,
                mergedEnemyStrengths,
                mergedFriendlyStrengths,
                visibleEnemyLine,
                visibleEnemyStrength,
                visibleFriendlyStrength,
                movementAnchor,
                movementAnchorFriendlyStrength,
                allianceId);
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


        internal static EnemyContactReport[] BuildContactReportsFromBattle(
            AIBattle battle,
            int allianceId,
            float staleAfterSeconds)
        {
            try
            {
                var units = BattleUnits.completeunitlist as IList;
                if (units == null) return Array.Empty<EnemyContactReport>();

                var observations = new List<ContactObservationInput>();
                for (int i = 0; i < units.Count; i++)
                {
                    var own = units[i] as Regiment;
                    if (!IsUsableOwnUnit(own, allianceId)) continue;

                    var visibleEnemy = SafeVisibleEnemy(own);
                    bool visible = visibleEnemy != null;
                    bool recentFire = HasRecentFire(own);
                    if (!visible && !recentFire) continue;

                    observations.Add(new ContactObservationInput(
                        visible ? TacticalContactSource.VisualContact : TacticalContactSource.RecentFire,
                        visible ? SafeStrength(visibleEnemy) : 0f,
                        visible ? 0f : 30f,
                        currentlyVisible: visible,
                        objectiveLinked: false,
                        scoutTaskLinked: false));
                }

                return BuildContactReports(observations, staleAfterSeconds);
            }
            catch
            {
                return Array.Empty<EnemyContactReport>();
            }
        }

        internal static ObjectiveRecord[] BuildObjectiveRecordsFromBattle(AIBattle battle, int allianceId)
        {
            try
            {
                var units = BattleUnits.completeunitlist as IList;
                if (units == null) return Array.Empty<ObjectiveRecord>();

                var observations = new List<ObjectiveObservationInput>();
                var statuses = new List<TacticalObjectiveStatus>();
                var enemyStrengths = new List<float>();
                var friendlyStrengths = new List<float>();
                var seenObjectiveIds = new HashSet<string>(StringComparer.Ordinal);

                AddObjectiveChainObservations(
                    battle,
                    observations,
                    statuses,
                    enemyStrengths,
                    friendlyStrengths,
                    seenObjectiveIds);

                for (int i = 0; i < units.Count; i++)
                {
                    var own = units[i] as Regiment;
                    if (!IsUsableOwnUnit(own, allianceId)) continue;

                    var objective = SafeCurrentSetObjective(own);
                    if (objective != null)
                    {
                        TacticalMapPoint point = SafeObjectivePoint(objective);
                        if (!IsUsableMapPoint(point)) continue;

                        string objectiveId = SafeObjectiveId(objective, observations.Count);
                        if (!seenObjectiveIds.Add(objectiveId)) continue;

                        observations.Add(new ObjectiveObservationInput(
                            objectiveId,
                            TacticalObjectiveType.UnknownVanillaObjective,
                            TacticalObjectiveSource.CurrentSetObjective,
                            point,
                            sourceConfidence: 0.65f,
                            value: 0.5f,
                            typeAnchorVerified: false));
                        statuses.Add(TacticalObjectiveStatus.Scouting);
                        enemyStrengths.Add(EstimateVisibleEnemyStrength(own));
                        friendlyStrengths.Add(SafeStrength(own));
                    }
                }

                AddMapObjectiveObservations(
                    allianceId,
                    observations,
                    statuses,
                    enemyStrengths,
                    friendlyStrengths,
                    seenObjectiveIds);

                ApplyApproachAvenues(
                    observations,
                    allianceId,
                    BuildApproachAvenueObservations(allianceId));

                TacticalMapPoint? visibleEnemyLine = TryVisibleEnemyLine(units, allianceId, out TacticalMapPoint visibleEnemyPoint, out float visibleEnemyStrength, out float visibleFriendlyStrength)
                    ? visibleEnemyPoint
                    : (TacticalMapPoint?)null;

                TacticalMapPoint? movementAnchor = TryMovementAnchorLine(units, allianceId, out TacticalMapPoint movementAnchorPoint, out float movementAnchorFriendlyStrength)
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
            catch
            {
                return Array.Empty<ObjectiveRecord>();
            }
        }

        private static void CopyObjectiveInputs(
            IReadOnlyList<ObjectiveObservationInput> sourceObservations,
            IReadOnlyList<TacticalObjectiveStatus> sourceStatuses,
            IReadOnlyList<float> sourceEnemyStrengths,
            IReadOnlyList<float> sourceFriendlyStrengths,
            List<ObjectiveObservationInput> targetObservations,
            List<TacticalObjectiveStatus> targetStatuses,
            List<float> targetEnemyStrengths,
            List<float> targetFriendlyStrengths,
            HashSet<string> seenObjectiveIds)
        {
            if (sourceObservations == null || sourceObservations.Count == 0) return;

            for (int i = 0; i < sourceObservations.Count; i++)
            {
                ObjectiveObservationInput observation = sourceObservations[i];
                if (!IsUsableMapPoint(observation.Location)) continue;
                if (seenObjectiveIds != null && !seenObjectiveIds.Add(observation.ObjectiveId)) continue;

                targetObservations.Add(observation);
                targetStatuses.Add(GetOrDefault(sourceStatuses, i, TacticalObjectiveStatus.Scouting));
                targetEnemyStrengths.Add(GetOrDefault(sourceEnemyStrengths, i, 0f));
                targetFriendlyStrengths.Add(GetOrDefault(sourceFriendlyStrengths, i, 0f));
            }
        }

        private static T GetOrDefault<T>(IReadOnlyList<T> values, int index, T fallback)
        {
            if (values == null || index < 0 || index >= values.Count) return fallback;
            return values[index];
        }

        private static bool IsUsableOwnUnit(Regiment regiment, int allianceId)
        {
            try
            {
                return regiment != null
                    && regiment.alliance == allianceId
                    && !regiment.isrouted
                    && !regiment.markedforrout
                    && !regiment.permanentlydetached;
            }
            catch
            {
                return false;
            }
        }

        private static void AddMapObjectiveObservations(
            int allianceId,
            List<ObjectiveObservationInput> observations,
            List<TacticalObjectiveStatus> statuses,
            List<float> enemyStrengths,
            List<float> friendlyStrengths,
            HashSet<string> seenObjectiveIds)
        {
            try
            {
                var mapObjectives = UnityEngine.Object.FindObjectsOfType<Objectives>();
                if (mapObjectives == null || mapObjectives.Length == 0) return;

                for (int i = 0; i < mapObjectives.Length; i++)
                {
                    Objectives objective = mapObjectives[i];
                    if (objective == null || !objective.isActiveAndEnabled) continue;

                    TacticalMapPoint point = SafeObjectivePoint(objective);
                    if (!IsUsableMapPoint(point)) continue;

                    string id = SafeObjectiveId(objective, observations.Count);
                    if (!seenObjectiveIds.Add(id)) continue;

                    int owner = SafeIntField(objective, "owner", -2);
                    bool friendlyOwned = owner == allianceId;
                    observations.Add(new ObjectiveObservationInput(
                        id,
                        TacticalObjectiveType.VictoryPoint,
                        TacticalObjectiveSource.VerifiedSceneObject,
                        point,
                        sourceConfidence: 0.90f,
                        value: friendlyOwned ? 0.45f : 0.80f,
                        typeAnchorVerified: true));
                    statuses.Add(friendlyOwned ? TacticalObjectiveStatus.Secured : TacticalObjectiveStatus.Scouting);
                    enemyStrengths.Add(0f);
                    friendlyStrengths.Add(0f);
                }
            }
            catch
            {
                // Runtime objective extraction must never destabilize the battle loop.
            }
        }

        private static void AddObjectiveChainObservations(
            AIBattle battle,
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
                    enemyStrengths.Add(EstimateChainEnemyStrength(chain));
                    friendlyStrengths.Add(EstimateChainFriendlyStrength(chain));
                }
            }
        }

        private static void ApplyApproachAvenues(
            List<ObjectiveObservationInput> observations,
            int allianceId,
            TacticalApproachAvenueObservation[] avenueObservations)
        {
            if (observations == null ||
                observations.Count == 0 ||
                avenueObservations == null ||
                avenueObservations.Length == 0)
            {
                return;
            }

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

                observations[i] = new ObjectiveObservationInput(
                    observation.ObjectiveId,
                    observation.Type,
                    observation.Source,
                    observation.Location,
                    Math.Max(observation.SourceConfidence, avenue.Confidence01 * 0.85f),
                    observation.Value,
                    observation.TypeAnchorVerified,
                    avenue);
            }
        }

        private static TacticalApproachAvenueObservation[] BuildApproachAvenueObservations(int allianceId)
        {
            try
            {
                BattleUnits battleUnits = UnityEngine.Object.FindObjectOfType<BattleUnits>();
                if (battleUnits == null) return Array.Empty<TacticalApproachAvenueObservation>();

                var observations = new List<TacticalApproachAvenueObservation>();
                AddEntryPointAvenues(battleUnits, observations);
                AddScheduledArrivalAvenues(battleUnits, observations);
                AddDeploymentGroupAvenues(battleUnits, observations);
                return observations.ToArray();
            }
            catch
            {
                return Array.Empty<TacticalApproachAvenueObservation>();
            }
        }

        private static void AddEntryPointAvenues(
            BattleUnits battleUnits,
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
                    crossingAnchored: IsCrossingAnchored(origin, target),
                    "deployment-group-" + i));
            }
        }

        private static bool TryEntryPointAvenue(
            EntryPoint entryPoint,
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
                roadAnchored = true;
            }
            else
            {
                target = origin;
            }

            if (!IsUsableMapPoint(origin) || !IsUsableMapPoint(target)) return false;

            crossingAnchored = IsCrossingAnchored(origin, target);
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

        private static bool TryVisibleEnemyLine(
            IList units,
            int allianceId,
            out TacticalMapPoint point,
            out float enemyStrength,
            out float friendlyStrength)
        {
            point = default;
            enemyStrength = 0f;
            friendlyStrength = 0f;

            if (units == null) return false;

            float weightedX = 0f;
            float weightedZ = 0f;
            float weightTotal = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                var own = units[i] as Regiment;
                if (!IsUsableOwnUnit(own, allianceId)) continue;

                friendlyStrength += SafeStrength(own);
                Regiment enemy = SafeVisibleEnemy(own);
                if (enemy == null) continue;

                Vector3 enemyPosition = SafePosition(enemy);
                if (IsDefaultVector(enemyPosition)) continue;

                float weight = Math.Max(1f, SafeStrength(enemy));
                weightedX += enemyPosition.x * weight;
                weightedZ += enemyPosition.z * weight;
                weightTotal += weight;
                enemyStrength += SafeStrength(enemy);
            }

            if (weightTotal <= 0f) return false;
            point = new TacticalMapPoint(weightedX / weightTotal, weightedZ / weightTotal);
            return IsUsableMapPoint(point);
        }

        private static bool TryMovementAnchorLine(
            IList units,
            int allianceId,
            out TacticalMapPoint point,
            out float friendlyStrength)
        {
            point = default;
            friendlyStrength = 0f;

            if (units == null) return false;

            float weightedX = 0f;
            float weightedZ = 0f;
            float weightTotal = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                var own = units[i] as Regiment;
                if (!IsUsableOwnUnit(own, allianceId)) continue;

                float strength = SafeStrength(own);
                friendlyStrength += strength;
                if (!TryLastWaypointPoint(own, out TacticalMapPoint waypoint)) continue;

                float weight = Math.Max(1f, strength);
                weightedX += waypoint.X * weight;
                weightedZ += waypoint.Z * weight;
                weightTotal += weight;
            }

            if (weightTotal <= 0f) return false;
            point = new TacticalMapPoint(weightedX / weightTotal, weightedZ / weightTotal);
            return IsUsableMapPoint(point);
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

        private static float EstimateChainEnemyStrength(object chain)
        {
            float total = 0f;
            AddClosestEnemyStrength(SafeRegimentField(chain, "linegroup_centerunit"), ref total);
            AddClosestEnemyStrengthFromList(SafeObjectList(chain, "linegroup_leftunits"), ref total);
            AddClosestEnemyStrengthFromList(SafeObjectList(chain, "linegroup_rightunits"), ref total);
            return total;
        }

        private static float EstimateChainFriendlyStrength(object chain)
        {
            float total = 0f;
            AddFriendlyStrength(SafeRegimentField(chain, "linegroup_centerunit"), ref total);
            AddFriendlyStrengthFromList(SafeObjectList(chain, "linegroup_leftunits"), ref total);
            AddFriendlyStrengthFromList(SafeObjectList(chain, "linegroup_rightunits"), ref total);
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

        private static void AddClosestEnemyStrength(Regiment own, ref float total)
        {
            if (own == null) return;
            total += SafeStrength(SafeVisibleEnemy(own));
        }

        private static void AddClosestEnemyStrengthFromList(IList units, ref float total)
        {
            if (units == null) return;
            for (int i = 0; i < units.Count; i++) AddClosestEnemyStrength(units[i] as Regiment, ref total);
        }

        private static void AddFriendlyStrength(Regiment own, ref float total)
        {
            total += SafeStrength(own);
        }

        private static void AddFriendlyStrengthFromList(IList units, ref float total)
        {
            if (units == null) return;
            for (int i = 0; i < units.Count; i++) AddFriendlyStrength(units[i] as Regiment, ref total);
        }

        private static Regiment SafeVisibleEnemy(Regiment own)
        {
            try
            {
                return TacticalFogOfWarContact.ClosestVisibleEnemy(own);
            }
            catch
            {
                return null;
            }
        }

        private static bool HasRecentFire(Regiment own)
        {
            try
            {
                return own != null && own.receivedfire != null && own.receivedfire.Count > 0;
            }
            catch
            {
                return false;
            }
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
        /// Returns the raw objective name string for use as
        /// <see cref="TacticalUnitObservation.ObjectiveName"/>. Returns
        /// <see cref="string.Empty"/> when the name is null, empty, whitespace,
        /// or unreadable. Callers apply their own fallback formula at the right
        /// point in the iteration (e.g. Task 8's BuildObjectiveRecordsFromAggregate
        /// replicates the legacy <c>SafeObjectiveId(obj, observations.Count)</c>
        /// formula at consumption time for bit-identical id streams).
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

        internal static float EstimateVisibleEnemyStrength(Regiment own)
        {
            return SafeStrength(SafeVisibleEnemy(own));
        }

        private static Vector3 SafePosition(Regiment regiment)
        {
            try { return regiment != null ? regiment.transform.position : default(Vector3); }
            catch { return default(Vector3); }
        }

        private static bool TryLastWaypointPoint(Regiment regiment, out TacticalMapPoint point)
        {
            point = default;
            try
            {
                if (regiment == null) return false;
                Vector3 waypoint = regiment.lastsetwaypointposition;
                if (IsDefaultVector(waypoint)) return false;

                Vector3 position = SafePosition(regiment);
                if (!IsDefaultVector(position) && DistanceSquaredXZ(position, waypoint) < 625f) return false;

                point = new TacticalMapPoint(waypoint.x, waypoint.z);
                return IsUsableMapPoint(point);
            }
            catch
            {
                return false;
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

        private static bool IsCrossingAnchored(TacticalMapPoint origin, TacticalMapPoint target)
        {
            if (!IsUsableMapPoint(origin) || !IsUsableMapPoint(target)) return false;

            var originVector = new Vector3(origin.X, 0f, origin.Z);
            var targetVector = new Vector3(target.X, 0f, target.Z);
            var midpoint = new Vector3((origin.X + target.X) * 0.5f, 0f, (origin.Z + target.Z) * 0.5f);

            return SafeSearchCrossingTerrain(originVector) ||
                SafeSearchCrossingTerrain(targetVector) ||
                SafeSearchCrossingTerrain(midpoint);
        }

        private static bool SafeSearchCrossingTerrain(Vector3 point)
        {
            try { return BattlefieldSetup.SearchTerrainInRangePos(point, new[] { 4, 8 }, 3) != null; }
            catch { return false; }
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

        private static float DistanceSquaredXZ(Vector3 first, Vector3 second)
        {
            float dx = first.x - second.x;
            float dz = first.z - second.z;
            return dx * dx + dz * dz;
        }

        internal static float SafeStrength(Regiment regiment)
        {
            try
            {
                if (regiment == null) return 0f;
                var field = regiment.GetType().GetField("groupstrengthaigroup");
                if (field == null) field = regiment.GetType().GetField("strength");
                var raw = field?.GetValue(regiment);
                if (raw is int intValue) return Math.Max(0f, intValue);
                if (raw is float floatValue) return Math.Max(0f, floatValue);
                if (raw is double doubleValue) return (float)Math.Max(0d, doubleValue);
                return 0f;
            }
            catch
            {
                return 0f;
            }
        }
    }
}
