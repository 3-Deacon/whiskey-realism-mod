using System;
using System.Collections.Generic;
using System.Linq;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalSectorMission
    {
        Hold = 0,
        Fix = 1,
        Probe = 2,
        Refuse = 3,
        AttackWeakPoint = 4,
        EconomyOfForce = 5,
        Preserve = 6
    }

    public readonly struct TacticalSectorAssessment
    {
        public TacticalSectorAssessment(
            int sectorId,
            TacticalSectorSource source,
            float ownStrength,
            float enemyStrength,
            float confidence,
            bool strongPoint,
            bool flankRisk,
            TacticalSectorMission mission)
        {
            SectorId = sectorId;
            Source = source;
            OwnStrength = Sanitize(ownStrength);
            EnemyStrength = Sanitize(enemyStrength);
            Confidence = Clamp01(confidence);
            StrongPoint = strongPoint;
            FlankRisk = flankRisk;
            Mission = mission;
        }

        public int SectorId { get; }
        public TacticalSectorSource Source { get; }
        public float OwnStrength { get; }
        public float EnemyStrength { get; }
        public float Confidence { get; }
        public bool StrongPoint { get; }
        public bool FlankRisk { get; }
        public TacticalSectorMission Mission { get; }
        public float Odds { get { return EnemyStrength <= 0f ? 0f : OwnStrength / Math.Max(1f, EnemyStrength); } }

        public TacticalSectorAssessment WithMission(TacticalSectorMission mission)
        {
            return new TacticalSectorAssessment(
                SectorId,
                Source,
                OwnStrength,
                EnemyStrength,
                Confidence,
                StrongPoint,
                FlankRisk,
                mission);
        }

        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, value);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    public readonly struct TacticalSectorLedgerResult
    {
        public TacticalSectorLedgerResult(
            TacticalSectorAssessment[] sectors,
            int decisiveSectorId,
            int[] economyOfForceSectorIds)
        {
            Sectors = sectors ?? Array.Empty<TacticalSectorAssessment>();
            DecisiveSectorId = decisiveSectorId;
            EconomyOfForceSectorIds = economyOfForceSectorIds ?? Array.Empty<int>();
        }

        public TacticalSectorAssessment[] Sectors { get; }
        public int DecisiveSectorId { get; }
        public int[] EconomyOfForceSectorIds { get; }
    }

    public static class TacticalSectorLedger
    {
        public static TacticalSectorLedgerResult Evaluate(IEnumerable<TacticalSectorAssessment> rawSectors)
        {
            var sectors = (rawSectors ?? Array.Empty<TacticalSectorAssessment>()).ToArray();
            if (sectors.Length == 0)
                return new TacticalSectorLedgerResult(Array.Empty<TacticalSectorAssessment>(), -1, Array.Empty<int>());

            int decisive = -1;
            float bestScore = 0f;
            for (int i = 0; i < sectors.Length; i++)
            {
                if (sectors[i].EnemyStrength <= 0f) continue;
                float score = sectors[i].Odds * sectors[i].Confidence;
                if (sectors[i].StrongPoint) score *= 0.65f;
                if (sectors[i].FlankRisk) score *= 0.55f;
                if (score > bestScore && sectors[i].Confidence >= 0.55f)
                {
                    bestScore = score;
                    decisive = sectors[i].SectorId;
                }
            }

            var resolved = new TacticalSectorAssessment[sectors.Length];
            var economy = new List<int>();
            for (int i = 0; i < sectors.Length; i++)
            {
                TacticalSectorMission mission;
                if (sectors[i].FlankRisk) mission = TacticalSectorMission.Refuse;
                else if (sectors[i].StrongPoint) mission = TacticalSectorMission.Hold;
                else if (sectors[i].SectorId == decisive && sectors[i].Odds >= 1.35f) mission = TacticalSectorMission.AttackWeakPoint;
                else if (sectors[i].Confidence < 0.45f) mission = TacticalSectorMission.Probe;
                else if (decisive >= 0) mission = TacticalSectorMission.Fix;
                else mission = TacticalSectorMission.Hold;

                if (mission == TacticalSectorMission.Fix || mission == TacticalSectorMission.Hold)
                    economy.Add(sectors[i].SectorId);

                resolved[i] = sectors[i].WithMission(mission);
            }

            return new TacticalSectorLedgerResult(resolved, decisive, economy.ToArray());
        }
    }
}
