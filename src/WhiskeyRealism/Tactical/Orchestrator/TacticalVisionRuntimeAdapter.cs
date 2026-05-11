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
                return BuildObjectiveRecords(observations, statuses, enemyStrengths, friendlyAssignedStrengths);
            }

            if (!visibleEnemyLine.HasValue || !IsUsableMapPoint(visibleEnemyLine.Value))
            {
                return Array.Empty<ObjectiveRecord>();
            }

            return BuildObjectiveRecords(
                new[]
                {
                    new ObjectiveObservationInput(
                        "enemy-line-" + allianceId,
                        TacticalObjectiveType.EnemyLine,
                        TacticalObjectiveSource.VisibleEnemyLine,
                        visibleEnemyLine.Value,
                        sourceConfidence: 0.55f,
                        value: 0.4f,
                        typeAnchorVerified: true)
                },
                new[] { TacticalObjectiveStatus.Contested },
                new[] { visibleEnemyStrength },
                new[] { visibleFriendlyStrength });
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

                    var closestEnemy = SafeClosestEnemy(own);
                    bool visible = closestEnemy != null;
                    bool recentFire = HasRecentFire(own);
                    if (!visible && !recentFire) continue;

                    observations.Add(new ContactObservationInput(
                        visible ? TacticalContactSource.VisualContact : TacticalContactSource.RecentFire,
                        visible ? SafeStrength(closestEnemy) : 0f,
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

                TacticalMapPoint? visibleEnemyLine = TryVisibleEnemyLine(units, allianceId, out TacticalMapPoint visibleEnemyPoint, out float visibleEnemyStrength, out float visibleFriendlyStrength)
                    ? visibleEnemyPoint
                    : (TacticalMapPoint?)null;

                return BuildObjectiveRecordsWithFallback(
                    observations,
                    statuses,
                    enemyStrengths,
                    friendlyStrengths,
                    visibleEnemyLine,
                    visibleEnemyStrength,
                    visibleFriendlyStrength,
                    allianceId);
            }
            catch
            {
                return Array.Empty<ObjectiveRecord>();
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
                Regiment enemy = SafeClosestEnemy(own);
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
            total += SafeStrength(SafeClosestEnemy(own));
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

        private static Regiment SafeClosestEnemy(Regiment own)
        {
            try
            {
                var go = own?.GetClosestEnemyUnit(9999f);
                return go == null ? null : go.GetComponent<Regiment>();
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

        private static object SafeCurrentSetObjective(Regiment own)
        {
            try { return own?.GetType().GetField("currentsetobjective")?.GetValue(own); }
            catch { return null; }
        }

        private static string SafeObjectiveId(object objective, int index)
        {
            try
            {
                var name = objective.GetType().GetField("objectivename")?.GetValue(objective) as string;
                return string.IsNullOrWhiteSpace(name) ? "objective-" + index : name;
            }
            catch
            {
                return "objective-" + index;
            }
        }

        private static TacticalMapPoint SafeObjectivePoint(object objective)
        {
            try
            {
                var component = objective as Component;
                if (component == null) return new TacticalMapPoint(0f, 0f);
                var position = component.transform.position;
                return new TacticalMapPoint(position.x, position.z);
            }
            catch
            {
                return new TacticalMapPoint(0f, 0f);
            }
        }

        private static float EstimateVisibleEnemyStrength(Regiment own)
        {
            return SafeStrength(SafeClosestEnemy(own));
        }

        private static Vector3 SafePosition(Regiment regiment)
        {
            try { return regiment != null ? regiment.transform.position : default(Vector3); }
            catch { return default(Vector3); }
        }

        private static bool IsUsableMapPoint(TacticalMapPoint point)
        {
            return !float.IsNaN(point.X) &&
                !float.IsNaN(point.Z) &&
                !float.IsInfinity(point.X) &&
                !float.IsInfinity(point.Z) &&
                (Math.Abs(point.X) >= 0.01f || Math.Abs(point.Z) >= 0.01f);
        }

        private static bool IsDefaultVector(Vector3 value)
        {
            return float.IsNaN(value.x) ||
                float.IsNaN(value.y) ||
                float.IsNaN(value.z) ||
                float.IsInfinity(value.x) ||
                float.IsInfinity(value.y) ||
                float.IsInfinity(value.z) ||
                (Math.Abs(value.x) < 0.01f && Math.Abs(value.z) < 0.01f);
        }

        private static float SafeStrength(Regiment regiment)
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
