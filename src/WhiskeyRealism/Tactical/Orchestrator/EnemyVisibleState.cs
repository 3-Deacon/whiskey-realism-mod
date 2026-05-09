using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Per-sector own-vs-enemy visible strength plus recent-fire flag.
    /// Built by ArmyEvidenceBuilder and consumed by ArmyIntentInference.Build.
    /// </summary>
    public readonly struct EnemyVisibleSector
    {
        public EnemyVisibleSector(int sectorId, float ownStrength, float enemyStrength, bool recentFire)
        {
            SectorId = sectorId;
            OwnStrength = SanitizeStrength(ownStrength);
            EnemyStrength = SanitizeStrength(enemyStrength);
            RecentFire = recentFire;
        }

        public int SectorId { get; }
        public float OwnStrength { get; }
        public float EnemyStrength { get; }
        public bool RecentFire { get; }

        public float Odds => EnemyStrength <= 0f ? 0f : OwnStrength / Math.Max(1f, EnemyStrength);

        private static float SanitizeStrength(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }
    }

    /// <summary>
    /// Visible enemy state bundle with no omniscient reads. Built by ArmyEvidenceBuilder
    /// and consumed by ArmyIntentInference.Build.
    /// </summary>
    public readonly struct EnemyVisibleState
    {
        public EnemyVisibleState(
            EnemyVisibleSector[] sectors,
            float enemyReserveCommitFraction,
            bool anyContactSpotted,
            bool anyContactBroken,
            float enemyReinforcementStrength24h)
        {
            Sectors = CopySectors(sectors);
            EnemyReserveCommitFraction = Clamp01(enemyReserveCommitFraction);
            AnyContactSpotted = anyContactSpotted;
            AnyContactBroken = anyContactBroken;
            EnemyReinforcementStrength24h = SanitizeStrength(enemyReinforcementStrength24h);
        }

        public EnemyVisibleSector[] Sectors { get; }
        public float EnemyReserveCommitFraction { get; }
        public bool AnyContactSpotted { get; }
        public bool AnyContactBroken { get; }
        public float EnemyReinforcementStrength24h { get; }

        private static EnemyVisibleSector[] CopySectors(EnemyVisibleSector[] sectors)
        {
            if (sectors == null || sectors.Length == 0) return Array.Empty<EnemyVisibleSector>();
            var copy = new EnemyVisibleSector[sectors.Length];
            Array.Copy(sectors, copy, sectors.Length);
            return copy;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static float SanitizeStrength(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }
    }
}
