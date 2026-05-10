using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public static class TacticalVisionRuntimeAdapter
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

        private static T GetOrDefault<T>(IReadOnlyList<T> values, int index, T fallback)
        {
            if (values == null || index < 0 || index >= values.Count) return fallback;
            return values[index];
        }
    }
}
