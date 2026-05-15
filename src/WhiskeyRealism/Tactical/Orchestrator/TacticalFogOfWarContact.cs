using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal static class TacticalFogOfWarContact
    {
        public static bool AcceptContactEvidence(
            bool inVanillaVisibleRange,
            bool inVanillaFireRange,
            bool receivedFire)
        {
            return receivedFire || inVanillaVisibleRange || inVanillaFireRange;
        }

        public static Regiment ClosestVisibleEnemy(Regiment unit)
        {
            TryClosestVisibleEnemy(unit, out Regiment enemy, out _);
            return enemy;
        }

        public static bool TryClosestVisibleEnemy(Regiment unit, out Regiment enemy, out float distance)
        {
            enemy = null;
            distance = 0f;
            if (unit == null || unit.unitrange == null) return false;

            float bestDistance = float.MaxValue;
            AddNearestVisibleEnemy(unit, unit.unitrange.enemyinfirerangereg, ref enemy, ref bestDistance);
            AddNearestVisibleEnemy(unit, unit.unitrange.enemyinrangereg, ref enemy, ref bestDistance);
            if (enemy == null) return false;

            distance = bestDistance;
            return true;
        }

        public static bool HasVisibleEnemy(Regiment unit)
        {
            return TryClosestVisibleEnemy(unit, out _, out _);
        }

        public static int VisibleEnemyCount(Regiment unit)
        {
            if (unit == null || unit.unitrange == null) return 0;
            var seen = new HashSet<int>();
            AddVisibleEnemyIds(unit.unitrange.enemyinfirerangereg, seen);
            AddVisibleEnemyIds(unit.unitrange.enemyinrangereg, seen);
            return seen.Count;
        }

        public static float VisibleEnemyStrength(Regiment unit)
        {
            if (unit == null || unit.unitrange == null) return 0f;
            var seen = new HashSet<int>();
            return SumVisibleEnemyStrength(unit.unitrange.enemyinfirerangereg, seen) +
                SumVisibleEnemyStrength(unit.unitrange.enemyinrangereg, seen);
        }

        public static float VisibleEnemyStrength(IList units)
        {
            if (units == null) return 0f;

            float total = 0f;
            var seen = new HashSet<int>();
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i] as Regiment;
                if (unit == null || unit.unitrange == null) continue;
                total += SumVisibleEnemyStrength(unit.unitrange.enemyinfirerangereg, seen);
                total += SumVisibleEnemyStrength(unit.unitrange.enemyinrangereg, seen);
            }

            return total;
        }

        private static void AddNearestVisibleEnemy(
            Regiment own,
            IList<Regiment> candidates,
            ref Regiment bestEnemy,
            ref float bestDistance)
        {
            if (own == null || candidates == null) return;

            for (int i = 0; i < candidates.Count; i++)
            {
                Regiment candidate = candidates[i];
                if (!IsValidVisibleEnemy(own, candidate)) continue;

                float distance = SafeDistance(own, candidate);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                bestEnemy = candidate;
            }
        }

        private static bool IsValidVisibleEnemy(Regiment own, Regiment candidate)
        {
            try
            {
                return own != null &&
                    candidate != null &&
                    candidate.alliance != own.alliance &&
                    candidate.unittyp != 5 &&
                    candidate.unittyp <= 13 &&
                    !candidate.isrouted &&
                    candidate.gameObject != null &&
                    candidate.gameObject.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }

        private static float SafeDistance(Regiment own, Regiment enemy)
        {
            try
            {
                return Tools.GetXZDistance(own.GetPosition(), enemy.GetPosition());
            }
            catch
            {
                try { return Vector3.Distance(own.transform.position, enemy.transform.position); }
                catch { return float.MaxValue; }
            }
        }

        private static void AddVisibleEnemyIds(IList<Regiment> units, HashSet<int> seen)
        {
            if (units == null || seen == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                Regiment unit = units[i];
                if (unit == null) continue;
                seen.Add(UnitIdentity(unit));
            }
        }

        private static float SumVisibleEnemyStrength(IList<Regiment> units, HashSet<int> seen)
        {
            if (units == null || seen == null) return 0f;

            float total = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                Regiment unit = units[i];
                if (unit == null) continue;

                int id = UnitIdentity(unit);
                if (!seen.Add(id)) continue;
                total += Math.Max(0f, unit.strength);
            }

            return total;
        }

        private static int UnitIdentity(Regiment unit)
        {
            try { return unit != null ? unit.GetInstanceID() : 0; }
            catch { return 0; }
        }
    }
}
