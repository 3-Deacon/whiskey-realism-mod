using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure builder. Given direct-child snapshots, their assigned primary sectors
    /// (resolved by the runtime caller from vanilla position/objective state),
    /// flank-exposure buckets, and the army-level EnemyVisibleState, produces a
    /// parallel DirectChildEvidence[] keyed by snapshot index.
    /// Bucket scheme mirrors the 0.5-ratio buckets used by FrontSectorRuntime
    /// in the strategic Defense Intent Ledger. The optional deployedReserve list (BC10) carries
    /// the executor's deployment-reserve outcome per snapshot.
    /// </summary>
    public static class DirectChildEvidenceBuilder
    {
        public static IReadOnlyList<DirectChildEvidence> BuildAll(
            IReadOnlyList<DirectChildSnapshot> snapshots,
            IReadOnlyList<int> primarySectorPerSnapshot,
            IReadOnlyList<int> flankExposureBucketPerSnapshot,
            EnemyVisibleState enemy,
            IReadOnlyList<bool> deployedReservePerSnapshot = null)
        {
            if (snapshots == null || snapshots.Count == 0) return Array.Empty<DirectChildEvidence>();
            if (primarySectorPerSnapshot == null || primarySectorPerSnapshot.Count != snapshots.Count
                || flankExposureBucketPerSnapshot == null || flankExposureBucketPerSnapshot.Count != snapshots.Count)
            {
                return Array.Empty<DirectChildEvidence>();
            }

            var result = new DirectChildEvidence[snapshots.Count];
            FillInto(result, snapshots, primarySectorPerSnapshot, flankExposureBucketPerSnapshot, enemy, deployedReservePerSnapshot);
            return result;
        }

        /// <summary>The same evidence into a caller's buffer (at least snapshots.Count long): nothing allocated per call. False when the inputs do not line up.</summary>
        public static bool BuildInto(
            DirectChildEvidence[] into,
            IReadOnlyList<DirectChildSnapshot> snapshots,
            IReadOnlyList<int> primarySectorPerSnapshot,
            IReadOnlyList<int> flankExposureBucketPerSnapshot,
            EnemyVisibleState enemy)
        {
            if (into == null || snapshots == null || snapshots.Count == 0 || into.Length < snapshots.Count) return false;
            if (primarySectorPerSnapshot == null || primarySectorPerSnapshot.Count != snapshots.Count
                || flankExposureBucketPerSnapshot == null || flankExposureBucketPerSnapshot.Count != snapshots.Count) return false;
            FillInto(into, snapshots, primarySectorPerSnapshot, flankExposureBucketPerSnapshot, enemy, null);
            return true;
        }

        private static void FillInto(
            DirectChildEvidence[] result,
            IReadOnlyList<DirectChildSnapshot> snapshots,
            IReadOnlyList<int> primarySectorPerSnapshot,
            IReadOnlyList<int> flankExposureBucketPerSnapshot,
            EnemyVisibleState enemy,
            IReadOnlyList<bool> deployedReservePerSnapshot)
        {
            for (int i = 0; i < snapshots.Count; i++)
            {
                int sector = primarySectorPerSnapshot[i];
                EnemyVisibleSector? matched = null;
                for (int j = 0; j < enemy.Sectors.Length; j++)
                {
                    if (enemy.Sectors[j].SectorId == sector) { matched = enemy.Sectors[j]; break; }
                }
                int ownBucket = matched.HasValue ? StrengthBucket(matched.Value.OwnStrength) : 0;
                int enemyBucket = matched.HasValue ? StrengthBucket(matched.Value.EnemyStrength) : 0;
                bool contact = matched.HasValue && matched.Value.RecentFire;
                float confidence = matched.HasValue ? Math.Min(1f, (matched.Value.OwnStrength + matched.Value.EnemyStrength) / 5000f) : 0f;
                result[i] = new DirectChildEvidence(
                    ownStrengthBucket: ownBucket,
                    enemyStrengthBucket: enemyBucket,
                    contactFlag: contact,
                    primarySector: sector,
                    flankExposureBucket: flankExposureBucketPerSnapshot[i],
                    confidence01: confidence,
                    deployedReserve: deployedReservePerSnapshot != null && i < deployedReservePerSnapshot.Count && deployedReservePerSnapshot[i]);
            }
        }

        /// <summary>
        /// 0.5-ratio buckets: 0 ≤ s &lt; 500 → 0; 500 ≤ s &lt; 1500 → 1;
        /// 1500 ≤ s &lt; 3000 → 2; 3000 ≤ s &lt; 5000 → 3; ≥ 5000 → 4.
        /// </summary>
        private static int StrengthBucket(float s)
        {
            if (float.IsNaN(s) || float.IsInfinity(s) || s <= 0f) return 0;
            if (s < 500f) return 0;
            if (s < 1500f) return 1;
            if (s < 3000f) return 2;
            if (s < 5000f) return 3;
            return 4;
        }
    }
}
