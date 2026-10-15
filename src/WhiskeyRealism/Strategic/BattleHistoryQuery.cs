using System.Collections.Generic;
using UnityEngine;

namespace WhiskeyRealism.Strategic
{
    internal static class BattleHistoryQuery
    {
        // The battle history's day scale (year x 372 + month x 31 + day), the scale BattleHistoryRecord
        // dates are compared on; never mix it with ReviewCadenceCatchUp.DaySerial (calendar days).
        internal static int LegacySerial(int year, int month, int day)
        {
            return year * 372 + month * 31 + day;
        }

        public static IEnumerable<BattleHistoryRecord> Near(
            IReadOnlyList<BattleHistoryRecord> history,
            Vector3 position,
            float maxDistance,
            int currentDaySerial,
            int withinDays)
        {
            if (history == null || history.Count == 0) yield break;
            float maxDistSq = maxDistance * maxDistance;
            for (int i = 0; i < history.Count; i++)
            {
                var record = history[i];
                if (record == null) continue;
                int recordDay = LegacySerial(record.Year, record.Month, record.Day);
                if (currentDaySerial - recordDay > withinDays || recordDay > currentDaySerial) continue;
                float dx = record.PositionX - position.x;
                float dz = record.PositionZ - position.z;
                if (dx * dx + dz * dz > maxDistSq) continue;
                yield return record;
            }
        }
    }
}
