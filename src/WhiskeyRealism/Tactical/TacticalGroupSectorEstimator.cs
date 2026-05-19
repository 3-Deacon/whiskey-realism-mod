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
            bool strongPoint)
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
            float enemy = Math.Max(input.EnemiesInRangeStrength, input.AngleEnemyStrength);
            TacticalSectorSource source = TacticalSectorSource.AngleSlice;

            if (lineContact)
            {
                enemy = Math.Max(enemy, input.ClosestEnemyStrength);
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
                input.OwnStrength,
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
