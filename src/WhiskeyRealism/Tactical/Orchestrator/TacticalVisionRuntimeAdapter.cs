using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal static class TacticalVisionRuntimeAdapter
    {
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

                for (int i = 0; i < units.Count; i++)
                {
                    var own = units[i] as Regiment;
                    if (!IsUsableOwnUnit(own, allianceId)) continue;

                    var objective = SafeCurrentSetObjective(own);
                    if (objective != null)
                    {
                        observations.Add(new ObjectiveObservationInput(
                            SafeObjectiveId(objective, observations.Count),
                            TacticalObjectiveType.UnknownVanillaObjective,
                            TacticalObjectiveSource.CurrentSetObjective,
                            SafeObjectivePoint(objective),
                            sourceConfidence: 0.65f,
                            value: 0.5f,
                            typeAnchorVerified: false));
                        statuses.Add(TacticalObjectiveStatus.Scouting);
                        enemyStrengths.Add(EstimateVisibleEnemyStrength(own));
                        friendlyStrengths.Add(SafeStrength(own));
                    }
                }

                if (observations.Count == 0)
                {
                    observations.Add(new ObjectiveObservationInput(
                        "enemy-line-" + allianceId,
                        TacticalObjectiveType.EnemyLine,
                        TacticalObjectiveSource.VisibleEnemyLine,
                        new TacticalMapPoint(0f, 0f),
                        sourceConfidence: 0.25f,
                        value: 0.25f,
                        typeAnchorVerified: true));
                    statuses.Add(TacticalObjectiveStatus.Scouting);
                    enemyStrengths.Add(0f);
                    friendlyStrengths.Add(0f);
                }

                return BuildObjectiveRecords(observations, statuses, enemyStrengths, friendlyStrengths);
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
