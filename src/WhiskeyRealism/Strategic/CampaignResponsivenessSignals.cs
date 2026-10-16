using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    internal struct CapitalForceSample
    {
        public float Distance;
        public float Strength;
        public float Morale;
    }

    // Responsiveness signals that used to be hard-wired off (C-HardWired).
    internal static class CampaignResponsivenessSignals
    {
        // Mirrors vanilla AICampaign.AssignUnitToDefendCapital (decompile 11668-11760):
        // need = aiminimumstrengthformovement
        //      + sum over enemies within aimaximumdistancetosearchforunitrelocations of
        //        min(str, str*morale) / max(10, dist)^0.3 * 0.5
        //      - sum over own units whose last waypoint is closer than the nearest enemy of
        //        str*morale.
        // Vanilla adds capital defenders while need > 0; we call the capital threatened when an
        // enemy is inside that radius and need is still positive.
        internal static bool EnemyThreatensCapital(
            IList<CapitalForceSample> enemies,
            IList<CapitalForceSample> own,
            float relocationRadius,
            float minimumStrengthForMovement)
        {
            if (enemies == null || enemies.Count == 0) return false;
            float nearestEnemy = 9999f;
            float need = minimumStrengthForMovement;
            bool enemyInRange = false;
            for (int i = 0; i < enemies.Count; i++)
            {
                float d = Math.Max(1f, enemies[i].Distance);
                if (d < nearestEnemy) nearestEnemy = d;
                if (d < relocationRadius)
                {
                    enemyInRange = true;
                    float s = Math.Max(0f, enemies[i].Strength);
                    need += Math.Min(s, s * enemies[i].Morale) / (float)Math.Pow(Math.Max(10f, d), 0.3) * 0.5f;
                }
            }
            if (!enemyInRange) return false;
            if (own != null)
            {
                for (int i = 0; i < own.Count; i++)
                {
                    if (own[i].Distance < nearestEnemy)
                        need -= Math.Max(0f, own[i].Strength) * own[i].Morale;
                }
            }
            return need > 0f;
        }

        // Consecutive threatened days, advanced at most once per game day; days skipped by
        // fast-forward between reviews count toward the streak.
        internal static int NextCapitalStreak(int streak, bool threatened, int lastDaySerial, int todaySerial, out int newLastDaySerial)
        {
            if (lastDaySerial >= 0 && todaySerial == lastDaySerial)
            {
                newLastDaySerial = lastDaySerial;
                return streak;
            }
            newLastDaySerial = todaySerial;
            if (!threatened) return 0;
            int elapsed = lastDaySerial >= 0 ? Math.Max(1, todaySerial - lastDaySerial) : 1;
            return Math.Max(0, streak) + elapsed;
        }

        internal static int DaysSince(int stampDaySerial, int todaySerial)
        {
            if (stampDaySerial < 0) return 0;
            return Math.Max(0, todaySerial - stampDaySerial);
        }
    }
}
