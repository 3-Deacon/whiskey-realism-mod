using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // Visible-contact reads for patches. Candidates are vanilla's per-unit range lists
    // (unitrange.enemyinfirerangereg / enemyinrangereg, UpdateUnitRangeFast 122649 /
    // 122692), which are range-only and unsorted (122840). A candidate counts only when the
    // side's battle snapshot sees it (BattleCore.TryGetSnapshot + IsVisible: vanilla's
    // AI-side fog, BC2a), and the nearest is chosen by true XZ distance, never by list
    // order. With no snapshot for the side (no coordinator tick yet, or a side Whiskey does
    // not run) nothing is visible. The snapshot is the per-tick memo (plan D2).
    internal static class TacticalFogOfWarContact
    {
        public static Regiment ClosestVisibleEnemy(Regiment unit)
        {
            TryClosestVisibleEnemy(unit, out Regiment enemy, out _);
            return enemy;
        }

        public static bool TryClosestVisibleEnemy(Regiment unit, out Regiment enemy, out float distance)
        {
            enemy = null;
            distance = 0f;
            if (!TrySideSnapshot(unit, out BattleSnapshot snapshot)) return false;

            float bestDistance = float.MaxValue;
            AddNearestVisibleEnemy(unit, unit.unitrange.enemyinfirerangereg, snapshot, ref enemy, ref bestDistance);
            AddNearestVisibleEnemy(unit, unit.unitrange.enemyinrangereg, snapshot, ref enemy, ref bestDistance);
            if (enemy == null) return false;

            distance = bestDistance;
            return true;
        }

        public static bool HasVisibleEnemy(Regiment unit)
        {
            if (!TrySideSnapshot(unit, out BattleSnapshot snapshot)) return false;
            return AnyValidVisibleEnemy(unit, unit.unitrange.enemyinfirerangereg, snapshot)
                || AnyValidVisibleEnemy(unit, unit.unitrange.enemyinrangereg, snapshot);
        }

        public static float VisibleEnemyStrength(IList units)
        {
            if (units == null) return 0f;

            float total = 0f;
            var seen = new HashSet<int>();
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i] as Regiment;
                if (!TrySideSnapshot(unit, out BattleSnapshot snapshot)) continue;
                total += SumVisibleEnemyStrength(unit, unit.unitrange.enemyinfirerangereg, snapshot, seen);
                total += SumVisibleEnemyStrength(unit, unit.unitrange.enemyinrangereg, snapshot, seen);
            }

            return total;
        }

        private static bool TrySideSnapshot(Regiment unit, out BattleSnapshot snapshot)
        {
            snapshot = null;
            try
            {
                return unit != null && unit.unitrange != null && BattleCore.TryGetSnapshot(unit.alliance, out snapshot);
            }
            catch
            {
                return false;
            }
        }

        private static bool AnyValidVisibleEnemy(Regiment own, IList<Regiment> candidates, BattleSnapshot snapshot)
        {
            if (candidates == null) return false;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (IsValidVisibleEnemy(own, candidates[i], snapshot)) return true;
            }
            return false;
        }

        private static void AddNearestVisibleEnemy(
            Regiment own,
            IList<Regiment> candidates,
            BattleSnapshot snapshot,
            ref Regiment bestEnemy,
            ref float bestDistance)
        {
            if (own == null || candidates == null) return;

            for (int i = 0; i < candidates.Count; i++)
            {
                Regiment candidate = candidates[i];
                if (!IsValidVisibleEnemy(own, candidate, snapshot)) continue;

                float distance = SafeDistance(own, candidate);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                bestEnemy = candidate;
            }
        }

        // Vanilla's pressure filter (active, unittyp <= 13, not 5, not routed: 122644) plus
        // the side's perception.
        private static bool IsValidVisibleEnemy(Regiment own, Regiment candidate, BattleSnapshot snapshot)
        {
            try
            {
                if (own == null || candidate == null) return false;
                if (candidate.alliance == own.alliance || candidate.unittyp == 5 || candidate.unittyp > 13 || candidate.isrouted)
                    return false;
                GameObject go = candidate.gameObject;
                return go != null && go.activeInHierarchy && snapshot.IsVisible(go.GetInstanceID());
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
                return float.MaxValue;
            }
        }

        private static float SumVisibleEnemyStrength(Regiment own, IList<Regiment> candidates, BattleSnapshot snapshot, HashSet<int> seen)
        {
            if (candidates == null) return 0f;

            float total = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                Regiment candidate = candidates[i];
                if (!IsValidVisibleEnemy(own, candidate, snapshot)) continue;
                if (!seen.Add(candidate.gameObject.GetInstanceID())) continue;
                total += Math.Max(0f, candidate.strength);
            }

            return total;
        }
    }
}
