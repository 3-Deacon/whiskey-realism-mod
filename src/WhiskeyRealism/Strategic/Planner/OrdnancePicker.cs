using System;
using System.Collections.Generic;
using System.Globalization;

namespace WhiskeyRealism.Strategic.Planner
{
    // One weapon model of the lane vanilla picked from, with the facts the score and vanilla's own candidate test need. The runtime
    // fills UnitCost, DeliveryYears and Standardization only for the models NeedsPriceRead names.
    public struct OrdnanceCandidate
    {
        public int Id;
        public bool Researched, Imported, OpenOrder;
        public float ProductionMultiplier;
        // Vanilla's WSF (GetWeaponStrengthFactor 16214), the measure of its candidate test and of the believed enemy lane quality.
        public float Wsf;
        // Rate of fire at full power (BattleValue).
        public float BattleValue;
        // Money per piece (the magnitude of GetProductionFigures(...).cost / quantity) and the delivery time in years (GetDeliveryTime,
        // capped at two); both carry the blockade through vanilla's own import share (64120-64124).
        public float UnitCost, DeliveryYears;
        public float Standardization;
    }

    // Which term group put the winner ahead of the runner-up by the most (VanillaKept: G9 left vanilla's pick).
    public enum PickReason { VanillaKept = 0, Value = 1, Delivery = 2, Standardization = 3, Edge = 4 }

    public struct OrdnanceChoice
    {
        public int Id;
        public float Score;
        public PickReason Reason;
        public float UnitCost, DeliveryYears;
    }

    // The scored weapon purchase (plan G9, decisions 2 and 3). Vanilla's CheckPurchaseWeapons (16259) names a lane (guns or rifles)
    // and picks a model at random from the researched-or-imported models with no open order whose WSF beats the alliance's own lane
    // average. This picks the best of those that the order screen would also allow, by value per cost, delivery, battle value,
    // standardization and an edge over what the alliance believes the enemy fields. Pure: no Unity type, no vanilla read.
    public static class OrdnancePicker
    {
        public const float ValuePerCostWeight = 0.35f;
        public const float DeliveryWeight = 0.20f;
        public const float BattleValueWeight = 0.20f;
        public const float StandardizationWeight = 0.15f;
        public const float EnemyEdgeWeight = 0.10f;
        // GetDeliveryTime caps a delivery at two years (64000).
        private const float MaxDeliveryYears = 2f;

        // Vanilla's candidate test for the lane's list (16296, 16313, 16300, 16317): researched or imported, no open order, WSF above
        // the alliance's own lane average.
        public static bool InVanillaSet(in OrdnanceCandidate c, float ownLaneAverageWsf)
        {
            return (c.Researched || c.Imported) && !c.OpenOrder && c.Wsf > ownLaneAverageWsf;
        }

        // The candidates G9 may pick: vanilla's, and orderable by the player screen's rule (WeaponOrderability).
        private static bool Eligible(in OrdnanceCandidate c, float ownLaneAverageWsf)
        {
            return InVanillaSet(c, ownLaneAverageWsf) && WeaponOrderability.IsOrderable(c.Researched, c.Imported, c.ProductionMultiplier);
        }

        // Whether the runtime must read a price for the candidate: only a model that can be picked costs the vanilla figures call.
        public static bool NeedsPriceRead(in OrdnanceCandidate c, float ownLaneAverageWsf)
        {
            return Eligible(c, ownLaneAverageWsf);
        }

        // Vanilla's WSF terms with the rate of fire at full power, as a battle's reload does (119633: reload is 1 / rpm), where vanilla
        // takes rpm^0.2; an artillery piece keeps vanilla's tube-weight divisor (16219); on WSF's scale (16221). A degenerate stat
        // (a zero tube weight, a missing value) scores 0.
        public static float BattleValue(float rangeYd, float roundsPerMinute, float firepower0, float accuracy, bool isArtillery, float tubeWeightKg)
        {
            float value = rangeYd * roundsPerMinute * (firepower0 / 2f) * (float)Math.Pow(accuracy, 3);
            if (isArtillery) value /= (float)Math.Pow(tubeWeightKg, 0.2) / 10f * 100f;
            value /= 1000f;
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;
        }

        // The pick for one lane: the highest score over vanilla's candidates that are orderable, each term normalized to [0, 1] over
        // them; the lowest model id on a tie. With none left (or no lane), vanilla's own pick stands: without a skip G9 cannot cancel
        // the order. believedEnemyWsf is the alliance's belief about the enemy's lane quality, 0 when it has none (no edge term).
        public static OrdnanceChoice Pick(IReadOnlyList<OrdnanceCandidate> lane, float ownLaneAverageWsf, int vanillaPick, float believedEnemyWsf)
        {
            var eligible = new List<OrdnanceCandidate>();
            if (lane != null)
                for (int i = 0; i < lane.Count; i++)
                    if (Eligible(lane[i], ownLaneAverageWsf)) eligible.Add(lane[i]);
            if (eligible.Count == 0) return new OrdnanceChoice { Id = vanillaPick, Reason = PickReason.VanillaKept };
            eligible.Sort((a, b) => a.Id.CompareTo(b.Id));

            float maxValuePerCost = 0f, maxBattleValue = 0f;
            for (int i = 0; i < eligible.Count; i++)
            {
                maxValuePerCost = Math.Max(maxValuePerCost, ValuePerCost(eligible[i]));
                maxBattleValue = Math.Max(maxBattleValue, Finite(eligible[i].BattleValue));
            }

            int best = -1, second = -1;
            float bestScore = -1f, secondScore = -1f;
            var terms = new Terms[eligible.Count];
            for (int i = 0; i < eligible.Count; i++)
            {
                var c = eligible[i];
                var t = new Terms
                {
                    Value = ValuePerCostWeight * Ratio(ValuePerCost(c), maxValuePerCost) + BattleValueWeight * Ratio(Finite(c.BattleValue), maxBattleValue),
                    Delivery = DeliveryWeight * Clamp01(1f - DeliveryYearsOf(c) / MaxDeliveryYears),
                    Standardization = StandardizationWeight * Clamp01(c.Standardization),
                    Edge = believedEnemyWsf > 0f && c.Wsf > believedEnemyWsf ? EnemyEdgeWeight : 0f
                };
                terms[i] = t;
                float score = t.Value + t.Delivery + t.Standardization + t.Edge;
                if (score > bestScore)
                {
                    second = best;
                    secondScore = bestScore;
                    best = i;
                    bestScore = score;
                }
                else if (score > secondScore)
                {
                    second = i;
                    secondScore = score;
                }
            }

            var winner = eligible[best];
            return new OrdnanceChoice
            {
                Id = winner.Id,
                Score = Clamp01(bestScore),
                Reason = second < 0 ? PickReason.Value : Decisive(terms[best], terms[second]),
                UnitCost = winner.UnitCost,
                DeliveryYears = winner.DeliveryYears
            };
        }

        public static string ReasonName(PickReason reason)
        {
            switch (reason)
            {
                case PickReason.Value: return "value";
                case PickReason.Delivery: return "delivery";
                case PickReason.Standardization: return "std";
                case PickReason.Edge: return "edge";
                default: return "vanilla-kept";
            }
        }

        // [OrdnancePurchase]: one row per order the alliance placed, whether G9 changed the pick or left vanilla's.
        public static string Row(int alliance, bool guns, int vanillaPick, in OrdnanceChoice choice)
        {
            return "[OrdnancePurchase] alliance=" + alliance.ToString(CultureInfo.InvariantCulture) +
                " lane=" + (guns ? "guns" : "rifles") +
                " vanilla=" + vanillaPick.ToString(CultureInfo.InvariantCulture) +
                " picked=" + choice.Id.ToString(CultureInfo.InvariantCulture) +
                " score=" + choice.Score.ToString("0.000", CultureInfo.InvariantCulture) +
                " cost=" + choice.UnitCost.ToString("0.0", CultureInfo.InvariantCulture) +
                " delivery=" + choice.DeliveryYears.ToString("0.00", CultureInfo.InvariantCulture) +
                " reason=" + ReasonName(choice.Reason);
        }

        private struct Terms
        {
            public float Value, Delivery, Standardization, Edge;
        }

        // The group the winner leads by the most; a dead heat (no group led) is credited to value.
        private static PickReason Decisive(in Terms winner, in Terms runnerUp)
        {
            var reason = PickReason.Value;
            float lead = winner.Value - runnerUp.Value;
            Lead(PickReason.Delivery, winner.Delivery - runnerUp.Delivery, ref reason, ref lead);
            Lead(PickReason.Standardization, winner.Standardization - runnerUp.Standardization, ref reason, ref lead);
            Lead(PickReason.Edge, winner.Edge - runnerUp.Edge, ref reason, ref lead);
            return reason;
        }

        private static void Lead(PickReason group, float margin, ref PickReason reason, ref float lead)
        {
            if (margin <= lead) return;
            reason = group;
            lead = margin;
        }

        // Battle value per money spent; a model with no readable price earns no credit for it.
        private static float ValuePerCost(in OrdnanceCandidate c)
        {
            float cost = c.UnitCost;
            if (float.IsNaN(cost) || cost <= 0f) return 0f;
            float ratio = Finite(c.BattleValue) / cost;
            return float.IsInfinity(ratio) ? 0f : ratio;
        }

        // An unreadable delivery time earns no credit: it counts as the two-year cap.
        private static float DeliveryYearsOf(in OrdnanceCandidate c)
        {
            return float.IsNaN(c.DeliveryYears) || float.IsInfinity(c.DeliveryYears) ? MaxDeliveryYears : c.DeliveryYears;
        }

        private static float Ratio(float value, float max)
        {
            return max > 0f ? Clamp01(value / max) : 0f;
        }

        private static float Finite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static float Clamp01(float value)
        {
            return float.IsNaN(value) ? 0f : Math.Max(0f, Math.Min(1f, value));
        }
    }
}
