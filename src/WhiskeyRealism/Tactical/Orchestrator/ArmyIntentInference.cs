using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure enemy-intent scorer that turns own-side battle evidence and filtered
    /// visible enemy state into a confidence-gated tactical intent model.
    /// </summary>
    public static class ArmyIntentInference
    {
        public const float ConfidenceFloor = 0.3f;
        public const float ConfidenceActionable = 0.6f;

        public static TacticalIntentModel Build(ArmyEvidence ownEvidence, EnemyVisibleState enemy)
        {
            if (enemy.Sectors.Length == 0)
            {
                return new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>());
            }

            float totalEnemy = 0f;
            float maxEnemyStrength = 0f;
            float ownStrengthAtMaxEnemy = 0f;
            int sectorWithMaxEnemy = -1;
            int recentFireSectorCount = 0;

            for (int i = 0; i < enemy.Sectors.Length; i++)
            {
                var sector = enemy.Sectors[i];
                totalEnemy += sector.EnemyStrength;
                if (sector.EnemyStrength > maxEnemyStrength || sectorWithMaxEnemy < 0)
                {
                    maxEnemyStrength = sector.EnemyStrength;
                    ownStrengthAtMaxEnemy = sector.OwnStrength;
                    sectorWithMaxEnemy = sector.SectorId;
                }

                if (sector.RecentFire)
                {
                    recentFireSectorCount++;
                }
            }

            float concentration = totalEnemy <= 0f ? 0f : maxEnemyStrength / totalEnemy;
            float strengthSignal = Math.Min(1f, totalEnemy / 5000f);
            float evenShare = 1f / Math.Max(1, enemy.Sectors.Length);
            float concentrationSignal = enemy.Sectors.Length <= 1
                ? concentration * strengthSignal
                : Math.Max(0f, (concentration - evenShare) / (1f - evenShare));
            float contactSignal = enemy.AnyContactSpotted ? 1f : (enemy.AnyContactBroken ? 0.4f : 0.2f);
            float confidence = Clamp01(0.5f * strengthSignal + 0.3f * concentrationSignal + 0.2f * contactSignal);
            bool hardWithdrawSignal = enemy.AnyContactBroken && totalEnemy < 2000f && !enemy.AnyContactSpotted;
            if (hardWithdrawSignal)
            {
                confidence = Math.Max(confidence, ConfidenceFloor);
            }

            if (confidence < ConfidenceFloor)
            {
                return new TacticalIntentModel(InferredIntent.Unknown, -1, confidence, 0f, Array.Empty<EvidenceTag>());
            }

            var evidence = new List<EvidenceTag>();
            if (concentration > 0.55f && enemy.Sectors.Length > 1) evidence.Add(EvidenceTag.SectorConcentration);
            if (enemy.EnemyReserveCommitFraction >= 0.5f) evidence.Add(EvidenceTag.ReserveCommitted);
            if (enemy.EnemyReserveCommitFraction <= 0.2f) evidence.Add(EvidenceTag.ReserveUncommitted);
            if (enemy.AnyContactSpotted) evidence.Add(EvidenceTag.ContactSpotted);
            if (enemy.AnyContactBroken) evidence.Add(EvidenceTag.ContactBroken);
            if (recentFireSectorCount > 0) evidence.Add(EvidenceTag.ReceivingFire);
            if (enemy.EnemyReinforcementStrength24h > 1f) evidence.Add(EvidenceTag.ReinforcementsArriving);

            InferredIntent intent;
            if (hardWithdrawSignal)
            {
                intent = InferredIntent.Withdraw;
            }
            else if (recentFireSectorCount >= 2 && enemy.EnemyReserveCommitFraction >= 0.4f)
            {
                intent = InferredIntent.Defend;
            }
            else if (concentration > 0.55f &&
                     enemy.EnemyReserveCommitFraction >= 0.4f &&
                     (enemy.Sectors.Length > 1 || maxEnemyStrength > ownStrengthAtMaxEnemy * 1.2f || recentFireSectorCount > 0))
            {
                intent = InferredIntent.Attack;
            }
            else if (concentration > 0.55f && enemy.EnemyReserveCommitFraction < 0.3f)
            {
                intent = InferredIntent.Refuse;
            }
            else if (enemy.Sectors.Length == 1 && recentFireSectorCount == 0 && maxEnemyStrength <= ownStrengthAtMaxEnemy * 1.2f)
            {
                intent = InferredIntent.Probe;
            }
            else if (concentration < 0.45f && enemy.EnemyReserveCommitFraction < 0.3f)
            {
                intent = InferredIntent.Probe;
            }
            else
            {
                intent = InferredIntent.Defend;
            }

            return new TacticalIntentModel(intent, sectorWithMaxEnemy, confidence, 0f, evidence.ToArray());
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
