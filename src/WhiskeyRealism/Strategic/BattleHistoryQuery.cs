using System.Collections.Generic;
using UnityEngine;

namespace WhiskeyRealism.Strategic
{
    internal static class BattleHistoryQuery
    {
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
                int recordDay = record.Year * 372 + record.Month * 31 + record.Day;
                if (currentDaySerial - recordDay > withinDays || recordDay > currentDaySerial) continue;
                float dx = record.PositionX - position.x;
                float dz = record.PositionZ - position.z;
                if (dx * dx + dz * dz > maxDistSq) continue;
                yield return record;
            }
        }
    }
}
