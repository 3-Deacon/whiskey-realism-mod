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

        /// <summary>Enemy in sight equal to this share of own strength = full strength signal.</summary>
        public const float StrengthSignalOwnFraction = 0.5f;

        /// <summary>Contact broken and less than this share of own strength still in sight = withdrawal.</summary>
        public const float WithdrawResidualOwnFraction = 0.25f;

        /// <summary>Share of the busiest sector's visible enemy that must stand on strong ground to read as defending it.</summary>
        public const float StrongGroundShare = 0.6f;

        public static TacticalIntentModel Build(EnemyVisibleState enemy)
        {
            if (enemy.Sectors.Length == 0)
            {
                return new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>());
            }

            float totalEnemy = 0f;
            float totalOwn = 0f;
            float maxEnemyStrength = 0f;
            float ownStrengthAtMaxEnemy = 0f;
            int sectorWithMaxEnemy = -1;
            int recentFireSectorCount = 0;
            float strongShareAtMaxEnemy = 0f;
            bool fireAtMaxEnemy = false;

            for (int i = 0; i < enemy.Sectors.Length; i++)
            {
                var sector = enemy.Sectors[i];
                totalEnemy += sector.EnemyStrength;
                totalOwn += sector.OwnStrength;
                if (sector.EnemyStrength > maxEnemyStrength || sectorWithMaxEnemy < 0)
                {
                    maxEnemyStrength = sector.EnemyStrength;
                    ownStrengthAtMaxEnemy = sector.OwnStrength;
                    sectorWithMaxEnemy = sector.SectorId;
                    strongShareAtMaxEnemy = sector.EnemyStrongGroundShare;
                    fireAtMaxEnemy = sector.RecentFire;
                }

                if (sector.RecentFire)
                {
                    recentFireSectorCount++;
                }
            }

            float concentration = totalEnemy <= 0f ? 0f : maxEnemyStrength / totalEnemy;
            // Relative to our own force (an enemy half our size in sight = full
            // signal). The old fixed /5000 made a 600-man contact the same
            // evidence for a cavalry detachment and a 40,000-man army.
            float strengthSignal = Math.Min(1f, totalEnemy / Math.Max(1f, StrengthSignalOwnFraction * totalOwn));
            float evenShare = 1f / Math.Max(1, enemy.Sectors.Length);
            float concentrationSignal = enemy.Sectors.Length <= 1
                ? concentration * strengthSignal
                : Math.Max(0f, (concentration - evenShare) / (1f - evenShare));
            float contactSignal = enemy.AnyContactSpotted ? 1f : (enemy.AnyContactBroken ? 0.4f : 0.2f);
            float confidence = Clamp01(0.5f * strengthSignal + 0.3f * concentrationSignal + 0.2f * contactSignal);
            bool hardWithdrawSignal = enemy.AnyContactBroken && totalEnemy < WithdrawResidualOwnFraction * totalOwn && !enemy.AnyContactSpotted;
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
            else if (strongShareAtMaxEnemy >= StrongGroundShare && !fireAtMaxEnemy)
            {
                // BC12: the busiest sector's enemy stands on strong ground and is quiet: it is holding it, not about to refuse a
                // flank or probe. A marching enemy never counts (the share is of standing units).
                intent = InferredIntent.Defend;
                evidence.Add(EvidenceTag.EnemyOnStrongGround);
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

        /// <summary>
        /// Frontage-filtered overload used by O3 to compute per-direct-child enemy
        /// intent. Filters EnemyVisibleState.Sectors to a single sector mask and
        /// reuses the existing Build path.
        /// </summary>
        public static TacticalIntentModel BuildForFrontage(int primarySector, EnemyVisibleState enemy)
        {
            EnemyVisibleSector? matched = null;
            for (int i = 0; i < enemy.Sectors.Length; i++)
            {
                if (enemy.Sectors[i].SectorId == primarySector)
                {
                    matched = enemy.Sectors[i];
                    break;
                }
            }

            if (!matched.HasValue)
            {
                return new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>());
            }

            var filtered = new EnemyVisibleState(
                sectors: new[] { matched.Value },
                enemyReserveCommitFraction: enemy.EnemyReserveCommitFraction,
                anyContactSpotted: enemy.AnyContactSpotted,
                anyContactBroken: enemy.AnyContactBroken,
                intelConfidence01: enemy.IntelConfidence01);

            return Build(filtered);
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
