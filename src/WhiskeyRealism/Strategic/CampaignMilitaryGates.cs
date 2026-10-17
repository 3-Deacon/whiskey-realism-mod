using System;
using System.Reflection;
using HarmonyLib;

namespace WhiskeyRealism.Strategic
{
    // Pure decision helpers for the campaign military write surfaces (WS C2a).
    // Runtime adapters and Harmony patches gather vanilla state and call these;
    // nothing here touches Unity objects, so the console harness covers them.
    internal readonly struct CapitalForce
    {
        internal readonly float Distance;
        internal readonly float Strength;
        internal readonly float Morale;

        internal CapitalForce(float distance, float strength, float morale)
        {
            Distance = distance;
            Strength = strength;
            Morale = morale;
        }
    }

    internal enum ForbiddenCommitRevert { None, CancelQueuedOrder, MoveBack }

    internal static class CampaignMilitaryGates
    {
        // ---- B-46-campaign: snapshot restore --------------------------------
        // Exact in-place restore of a vanilla list from a Prefix snapshot.
        // Idempotent and null-safe (callers run it from Finalizers).
        internal static bool RestoreListSnapshot(System.Collections.IList live, System.Collections.Generic.IList<object> snapshot)
        {
            if (live == null || snapshot == null) return false;
            live.Clear();
            for (int i = 0; i < snapshot.Count; i++)
                live.Add(snapshot[i]);
            return true;
        }

        // Revert of a ledger-forbidden unit vanilla CheckForDefensiveOperations
        // committed anyway. The defensive branch only queues a
        // DefensiveMovingOrder (13713; UpdateOrders runs it later, 79295), so the
        // unit has not moved and cancelling the order is the whole revert. The
        // attack branch moves directly (MoveUnitTo, 13771), so the unit is sent
        // back to its prior theater position when one exists.
        internal static ForbiddenCommitRevert ResolveForbiddenCommitRevert(bool queuedOrderRunning, bool hasPriorTheaterPosition)
        {
            if (queuedOrderRunning) return ForbiddenCommitRevert.CancelQueuedOrder;
            return hasPriorTheaterPosition ? ForbiddenCommitRevert.MoveBack : ForbiddenCommitRevert.None;
        }

        // ---- R7: ImportanceValues --------------------------------------------
        // Vanilla AIArea.CalculateMostValueableAIZones (decompile 10964) only
        // accepts a candidate area that is not the source area, lies within
        // GetClosestAreaDistance <= aimaximumdistancetosearchforunitrelocations *
        // rangefactoroffensivetargets, passes the supply gate
        // ownpoints[f] <= supplycapacity, and satisfies
        // (points >= 0 | distancepoints > source | importance > source) (10984).
        // The plan target may replace vanilla's pick only when it passes the same
        // gates and the source area lies in a sector the plan steers (a MainEffort
        // sector, SteeringRules.ImportanceSteers (the main sector only, never while staged); G5a re-keyed the old
        // same-theater clause on the plan), so other fronts keep vanilla's local
        // targets and nothing is pulled cross-map.
        internal static bool ShouldRedirectToPlanTarget(
            bool targetIsSource,
            bool sourceInSteeredSector,
            float closestAreaDistance,
            float maxOffensiveDistance,
            float targetOwnPoints,
            float targetSupplyCapacity,
            bool vanillaValueClause)
        {
            if (targetIsSource) return false;
            if (!sourceInSteeredSector) return false;
            if (closestAreaDistance > maxOffensiveDistance) return false;
            if (targetOwnPoints > targetSupplyCapacity) return false;
            return vanillaValueClause;
        }

        // ---- C-DefOps -------------------------------------------------------
        // Vanilla AICampaign.AssignUnitToDefendCapital (decompile 11668):
        //   num3 = closest enemy distance (each max(1, d)), start 9999
        //   num2 = aiminimumstrengthformovement
        //        + sum over enemies with d < aimaximumdistancetosearchforunitrelocations of
        //          min(str, str*morale) / max(10, d)^0.3 * 0.5
        //        - sum over own units with |lastwaypoint - capital| < num3 of str*morale
        // Enemy.Distance = distance to the capital; own.Distance = distance from the
        // unit's last waypoint to the capital.
        internal static float VanillaCapitalDefenseNeed(float baseMinimum, float searchRadius,
            System.Collections.Generic.IList<CapitalForce> enemies, System.Collections.Generic.IList<CapitalForce> own)
        {
            float closestEnemy = 9999f;
            float need = baseMinimum;
            if (enemies != null)
            {
                for (int i = 0; i < enemies.Count; i++)
                {
                    float d = Math.Max(1f, enemies[i].Distance);
                    if (d < closestEnemy) closestEnemy = d;
                    if (d < searchRadius)
                    {
                        float pressure = Math.Min(enemies[i].Strength, enemies[i].Strength * enemies[i].Morale);
                        need += pressure / (float)Math.Pow(Math.Max(10f, d), 0.3f) * 0.5f;
                    }
                }
            }
            if (own != null)
            {
                for (int i = 0; i < own.Count; i++)
                    if (own[i].Distance < closestEnemy)
                        need -= own[i].Strength * own[i].Morale;
            }
            return need;
        }

        // ---- C-Coastal ------------------------------------------------------
        // Vanilla CheckForEndOfDefensiveOperations (decompile 13836) keeps a
        // defensive-operation unit only while some enemy (or enemy depot) is
        // within the unit's buglerange of its last transmitted path position;
        // otherwise it StopRegiment()s and drops it. The guard therefore stands on
        // the anchor (asset) only if the enemy is already within reach of it;
        // otherwise between anchor and enemy, inside 0.8 x buglerange of the enemy.
        internal const float CoastalInterceptBugleFraction = 0.8f;

        internal static void CoastalInterceptPoint(float anchorX, float anchorZ, float enemyX, float enemyZ, float bugleRange, out float x, out float z)
        {
            x = anchorX;
            z = anchorZ;
            float reach = Math.Max(0f, bugleRange) * CoastalInterceptBugleFraction;
            float dx = anchorX - enemyX;
            float dz = anchorZ - enemyZ;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (reach <= 0f || d <= reach) return;
            x = enemyX + dx / d * reach;
            z = enemyZ + dz / d * reach;
        }

        internal static void CarryForwardCustomOrders(
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<int>> prior,
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<int>> thisTick,
            Func<int, bool> stillHeld)
        {
            // Units committed on an earlier tick are skipped by the assignment loop
            // (already in unitsindefensiveoperations), so they must be carried into
            // this tick's map for every signature that is still live; otherwise the
            // runner forgets them on tick 2 and can never release them.
            if (prior == null || thisTick == null) return;
            foreach (var kv in prior)
            {
                if (kv.Value == null || !thisTick.TryGetValue(kv.Key, out var current)) continue;
                foreach (var id in kv.Value)
                    if (stillHeld == null || stillHeld(id))
                        current.Add(id);
            }
        }

        internal static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    // BUG-WL-013 (spec C1): vanilla's AI transfer in a W&L career never runs. CheckTransferOfUnits
    // (17232) calls BattleUnits.MoveUnit(unit, below) without skipdlccheck (17328) and MoveUnit
    // returns before moving anything unless BOTH units are player-owned (dlcw_isundercommander,
    // 93099), so in a career an AI formation's transfer is refused for every AI faction while the
    // unit still sits in unitstobetransfered. The fix is argument-only: skipdlccheck = true for the
    // calls CheckTransferOfUnits makes, and only when neither unit is player-owned. A unit the
    // player owns keeps vanilla's refusal, so Whiskey never re-parents it.
    internal static class WlTransferFixDecision
    {
        internal static bool Allow(bool insideCheckTransferOfUnits, bool careerActive, bool sourceOwnedByPlayer, bool destinationOwnedByPlayer)
        {
            return insideCheckTransferOfUnits && careerActive && !sourceOwnedByPlayer && !destinationOwnedByPlayer;
        }
    }
}
