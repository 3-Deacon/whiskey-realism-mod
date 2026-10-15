using System;

namespace WhiskeyRealism.Tactical
{
    public readonly struct TacticalGroupContactInput
    {
        public TacticalGroupContactInput(
            int sectorId,
            float ownStrength,
            float enemiesInRangeStrength,
            float angleEnemyStrength,
            float closestEnemyStrength,
            int closestEnemyUnitType,
            string closestEnemyName,
            bool closestEnemyRouted,
            bool closestEnemyPermanentlyDetached,
            bool flankRisk,
            bool strongPoint,
            float ownExperienceBlend = 1f,
            float enemyExperienceBlend = 1f)
        {
            SectorId = sectorId;
            OwnStrength = NonNegative(ownStrength);
            EnemiesInRangeStrength = NonNegative(enemiesInRangeStrength);
            AngleEnemyStrength = NonNegative(angleEnemyStrength);
            ClosestEnemyStrength = NonNegative(closestEnemyStrength);
            ClosestEnemyUnitType = closestEnemyUnitType;
            ClosestEnemyName = string.IsNullOrEmpty(closestEnemyName) ? string.Empty : closestEnemyName;
            ClosestEnemyRouted = closestEnemyRouted;
            ClosestEnemyPermanentlyDetached = closestEnemyPermanentlyDetached;
            FlankRisk = flankRisk;
            StrongPoint = strongPoint;
            OwnExperienceBlend = Blend(ownExperienceBlend);
            EnemyExperienceBlend = Blend(enemyExperienceBlend);
        }

        public int SectorId { get; }
        public float OwnStrength { get; }
        public float EnemiesInRangeStrength { get; }
        public float AngleEnemyStrength { get; }
        public float ClosestEnemyStrength { get; }
        public int ClosestEnemyUnitType { get; }
        public string ClosestEnemyName { get; }
        public bool ClosestEnemyRouted { get; }
        public bool ClosestEnemyPermanentlyDetached { get; }
        public bool FlankRisk { get; }
        public bool StrongPoint { get; }
        // BC8: vanilla's experience blend (GetGroupStrength 6057, TroopQuality.AiDecisionBlend)
        // for this group and for the enemy it faces; a non-finite or non-positive blend is 1.
        public float OwnExperienceBlend { get; }
        public float EnemyExperienceBlend { get; }

        private static float Blend(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ? 1f : value;
        }

        private static float NonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, value);
        }
    }

    public static class TacticalGroupSectorEstimator
    {
        public static TacticalSectorAssessment BuildSector(TacticalGroupContactInput input)
        {
            bool lineContact = IsVisibleLineContact(input);
            // Troop quality (BC8): both sides weighed by vanilla's experience blend, as
            // AdjustGroupAIStance's GetGroupStrength(includeexp: true) does (4238, 6057).
            float enemyBlend = input.EnemyExperienceBlend;
            float enemy = Math.Max(input.EnemiesInRangeStrength, input.AngleEnemyStrength) * enemyBlend;
            TacticalSectorSource source = TacticalSectorSource.AngleSlice;

            if (lineContact)
            {
                enemy = Math.Max(enemy, input.ClosestEnemyStrength * enemyBlend);
                source = TacticalSectorSource.VisibleLineContact;
            }

            bool hasEnemy = enemy > 0f;
            // SoW-aligned confidence table. SoW's brigade-think (offai.cpp:507-546)
            // issues movement orders to a brigade's tactical objective when no enemy
            // is in range — pre-contact silence is not a SoW pattern. To mirror that,
            // the no-enemy floor must reach the doctrine's 0.55 "act on it" threshold
            // so DecideGroupStance can issue a pre-contact Hold/Approach branch
            // instead of skipping forever.
            float confidence = hasEnemy
                ? (lineContact ? 0.85f : 0.65f)  // line contact / no line contact, with enemy
                : (lineContact ? 0.70f : 0.55f); // pre-contact (lifted from 0.45 -> 0.55)

            var sector = new TacticalSectorAssessment(
                input.SectorId,
                source,
                input.OwnStrength * input.OwnExperienceBlend,
                enemy,
                confidence,
                input.StrongPoint,
                input.FlankRisk,
                TacticalSectorMission.Hold);
            var result = TacticalSectorLedger.Evaluate(new[] { sector });
            return result.Sectors.Length > 0 ? result.Sectors[0] : sector;
        }

        public static bool IsVisibleLineContact(TacticalGroupContactInput input)
        {
            if (input.ClosestEnemyRouted) return false;
            if (input.ClosestEnemyPermanentlyDetached) return false;
            if (input.ClosestEnemyStrength <= 0f) return false;
            if (!IsFormedLineUnitType(input.ClosestEnemyUnitType))
                return false;

            string name = input.ClosestEnemyName ?? string.Empty;
            if (name.IndexOf("skirm", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            return true;
        }

        private static bool IsFormedLineUnitType(int unitType)
        {
            return unitType == TacticalUnitType.Infantry ||
                unitType == TacticalUnitType.Cavalry ||
                unitType == TacticalUnitType.BattleGroupBrigade ||
                unitType == TacticalUnitType.BattleGroupDivision ||
                unitType == TacticalUnitType.BattleGroupArmy;
        }
    }
}
