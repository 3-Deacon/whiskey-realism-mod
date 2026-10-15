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

    // Every vanilla assignment that makes a campaign formation "busy". Mirrors
    // the exclusions vanilla applies before it moves a unit itself:
    // IsUnitAvailableForOffensiveOperations (decompile 14080),
    // AssignUnitToDefendCapital (11800), CheckForEndOfDefensiveOperations (13860).
    internal struct AreaOrderBusyFlags
    {
        public bool InBattle;
        public bool OnRetreat;
        public bool HasPaths;
        public bool InOffensiveOperation;
        public bool InDefensiveOperation;
        public bool DefendingCapital;      // aifaction.groupstodefendcapital
        public bool BuildingSupplyDepot;   // aifaction.unitsconstructingsupplydepots
        public bool BuildingFort;          // FortConstructionOrder.UnitAlreadyConstructing
        public bool RaidForce;             // RaidForce.IsRaidUnit
        public bool SeaInvasionForce;      // SeaInvasionForce.GetSeaInvasionForceReference != null
        public bool DefensiveOrderQueued;  // DefensiveMovingOrder.OrderRunning
        public bool TakingTown;            // AICampaign.IsUnitTakingTown
    }

    internal enum ForbiddenCommitRevert { None, CancelQueuedOrder, MoveBack }

    internal static class CampaignMilitaryGates
    {
        // ---- C-DefIntent: sea-invasion landed signal -------------------------
        // Vanilla SeaInvasion exposes two overloads (decompile 141061 instance,
        // 141066 static List<Regiment> variant). The untyped lookup throws
        // AmbiguousMatchException, which silently killed landed-signal condition 3.
        internal static MethodInfo ResolveSeaInvasionStrengthMethod()
        {
            return AccessTools.Method(
                typeof(SeaInvasion),
                "GetUnitStrengthInRange",
                new[] { typeof(int), typeof(bool), typeof(int) });
        }

        // returnbalance=true is the only mode vanilla itself uses (9238, 9433):
        // units at war with `allianceid` add, own/allied units subtract. With
        // returnbalance=false own units ALSO add, so our own garrison at the spot
        // reads as a landing. Ships weigh 0: a landing means troops ashore.
        internal static object[] SeaInvasionBalanceArgs(int defenderAllianceId)
        {
            return new object[] { defenderAllianceId, true, 0 };
        }

        internal static bool IsLandedByStrengthBalance(int enemyMinusFriendly)
        {
            return enemyMinusFriendly > 0;
        }

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

        // ---- C-Transfer ------------------------------------------------------
        // Vanilla CheckTransferOfUnits (decompile 17232) sends troops toward
        // positiondeficit sized by deficit.enemystrength - deficit.ownstrength
        // (num4, 17275). The plan target only competes for that slot with a
        // requirement on the same local scale: superiority over the enemy near
        // the target, floored at a token presence, capped by the phase's
        // commitment share of the whole army.
        internal const float PlanTransferMinimumPresence = 2500f;
        internal const float PlanTransferLocalSuperiority = 1.25f;

        internal static float PlanTransferRequiredAtTarget(float totalOwn, float forceFraction, float enemyNear)
        {
            float share = Math.Max(0f, totalOwn) * Clamp(forceFraction, 0.15f, 0.95f);
            float local = Math.Max(PlanTransferMinimumPresence, Math.Max(0f, enemyNear) * PlanTransferLocalSuperiority);
            return Math.Max(PlanTransferMinimumPresence, Math.Min(share, local));
        }

        // Steer only when the plan's shortfall is worse than the deficit vanilla
        // already found; otherwise the actually-threatened zone keeps its transfer.
        internal static bool ShouldSteerTransfer(bool vanillaDeficitPresent, float vanillaDeficitEnemy, float vanillaDeficitOwn, float planShortfall)
        {
            if (planShortfall <= 0f) return false;
            if (!vanillaDeficitPresent) return true;
            float vanillaSeverity = vanillaDeficitEnemy - vanillaDeficitOwn;
            if (vanillaSeverity <= 0f) return true;
            return planShortfall > vanillaSeverity;
        }

        // ---- R7: ImportanceValues --------------------------------------------
        // Vanilla AIArea.CalculateMostValueableAIZones (decompile 10964) only
        // accepts a candidate area that is not the source area, lies within
        // GetClosestAreaDistance <= aimaximumdistancetosearchforunitrelocations *
        // rangefactoroffensivetargets, passes the supply gate
        // ownpoints[f] <= supplycapacity, and satisfies
        // (points >= 0 | distancepoints > source | importance > source) (10984).
        // The plan target may replace vanilla's pick only when it passes the same
        // gates and the source area sits in the plan target's theater, so other
        // fronts keep vanilla's local targets and nothing is pulled cross-map.
        internal static bool ShouldRedirectToPlanTarget(
            bool targetIsSource,
            Theater sourceTheater,
            Theater targetTheater,
            float closestAreaDistance,
            float maxOffensiveDistance,
            float targetOwnPoints,
            float targetSupplyCapacity,
            bool vanillaValueClause)
        {
            if (targetIsSource) return false;
            if (sourceTheater == Theater.Unknown || sourceTheater != targetTheater) return false;
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
            return CapitalDefenseNeed(baseMinimum, searchRadius, enemies, own, null, 1f, 1f);
        }

        // Personality-weighted variant: the base and the enemy pressure scale by the
        // CIC strength-gate and capital-value multipliers, and pledged defenders that
        // are still marching (waypoint not yet inside the closest-enemy ring, so not
        // already subtracted as own units) count at 0.75 of morale-weighted strength.
        internal static float PersonalityCapitalDefenseNeed(float baseMinimum, float searchRadius,
            System.Collections.Generic.IList<CapitalForce> enemies, System.Collections.Generic.IList<CapitalForce> own,
            System.Collections.Generic.IList<CapitalForce> pledged, float strengthMultiplier, float capitalMultiplier)
        {
            return CapitalDefenseNeed(baseMinimum, searchRadius, enemies, own, pledged, strengthMultiplier, capitalMultiplier);
        }

        private static float CapitalDefenseNeed(float baseMinimum, float searchRadius,
            System.Collections.Generic.IList<CapitalForce> enemies, System.Collections.Generic.IList<CapitalForce> own,
            System.Collections.Generic.IList<CapitalForce> pledged, float strengthMultiplier, float capitalMultiplier)
        {
            float closestEnemy = 9999f;
            float need = baseMinimum * strengthMultiplier * capitalMultiplier;
            if (enemies != null)
            {
                for (int i = 0; i < enemies.Count; i++)
                {
                    float d = Math.Max(1f, enemies[i].Distance);
                    if (d < closestEnemy) closestEnemy = d;
                    if (d < searchRadius)
                    {
                        float pressure = Math.Min(enemies[i].Strength, enemies[i].Strength * enemies[i].Morale);
                        need += pressure / (float)Math.Pow(Math.Max(10f, d), 0.3f) * 0.5f * strengthMultiplier;
                    }
                }
            }
            if (own != null)
            {
                for (int i = 0; i < own.Count; i++)
                    if (own[i].Distance < closestEnemy)
                        need -= own[i].Strength * own[i].Morale;
            }
            if (pledged != null)
            {
                for (int i = 0; i < pledged.Count; i++)
                {
                    if (pledged[i].Distance < closestEnemy) continue; // already counted as own
                    need -= pledged[i].Strength * Math.Max(0.25f, pledged[i].Morale) * 0.75f;
                }
            }
            return need;
        }

        // Pledge before vanilla runs only when vanilla's own num2 > 0 too: then
        // vanilla neither strips the pledge (its num2 <= 0 branch removes the
        // farthest defender every call) and its move loop (11829) moves it this
        // call. Personality chooses the candidate and adds it alongside vanilla's.
        internal static bool ShouldPrePledgeCapitalDefender(float vanillaNeed, float personalityNeed)
        {
            return vanillaNeed > 0f && personalityNeed > 0f;
        }

        // ---- C-ArmyArea -----------------------------------------------------
        internal static bool IsIdleForAreaOrder(AreaOrderBusyFlags busy)
        {
            return !busy.InBattle && !busy.OnRetreat && !busy.HasPaths &&
                !busy.InOffensiveOperation && !busy.InDefensiveOperation &&
                !busy.DefendingCapital && !busy.BuildingSupplyDepot && !busy.BuildingFort &&
                !busy.RaidForce && !busy.SeaInvasionForce && !busy.DefensiveOrderQueued &&
                !busy.TakingTown;
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
}
