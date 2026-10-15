using System;
using System.Linq;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalInferiorForcePosture
    {
        None = 0,
        ProbeOrHold = 1,
        DelayOnStrongGround = 2,
        PreserveOrRetreat = 3
    }

    public readonly struct TacticalOddsInput
    {
        public TacticalOddsInput(
            float ownStrength,
            float enemyStrengthConfirmed,
            float enemyStrengthRecent,
            float enemyStrengthInferred,
            float reinforcementStrength24h,
            float terrainAdvantage,
            TacticalContactAssessment contact,
            TacticalSectorAssessment[] sectors,
            float ownExperienceBlend = 1f,
            float enemyExperienceBlend = 1f)
        {
            OwnStrength = Sanitize(ownStrength);
            EnemyStrengthConfirmed = Sanitize(enemyStrengthConfirmed);
            EnemyStrengthRecent = Sanitize(enemyStrengthRecent);
            EnemyStrengthInferred = Sanitize(enemyStrengthInferred);
            ReinforcementStrength24h = Sanitize(reinforcementStrength24h);
            TerrainAdvantage = Clamp01(terrainAdvantage);
            Contact = contact;
            Sectors = sectors ?? Array.Empty<TacticalSectorAssessment>();
            OwnExperienceBlend = Blend(ownExperienceBlend);
            EnemyExperienceBlend = Blend(enemyExperienceBlend);
        }

        public float OwnStrength { get; }
        public float EnemyStrengthConfirmed { get; }
        public float EnemyStrengthRecent { get; }
        public float EnemyStrengthInferred { get; }
        public float ReinforcementStrength24h { get; }
        public float TerrainAdvantage { get; }
        public TacticalContactAssessment Contact { get; }
        public TacticalSectorAssessment[] Sectors { get; }
        // BC8: vanilla's experience blend (6057) for the own side and the perceived enemy
        // (TroopQuality.SideBlends); a non-finite or non-positive blend is 1.
        public float OwnExperienceBlend { get; }
        public float EnemyExperienceBlend { get; }

        private static float Blend(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ? 1f : value;
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

    public readonly struct TacticalOddsAssessment
    {
        public TacticalOddsAssessment(
            float currentGlobalOdds,
            float projectedGlobalOdds,
            int decisiveSectorId,
            int[] economyOfForceSectorIds,
            TacticalInferiorForcePosture inferiorForcePosture,
            float confidence,
            bool allowAssault)
        {
            CurrentGlobalOdds = Sanitize(currentGlobalOdds);
            ProjectedGlobalOdds = Sanitize(projectedGlobalOdds);
            DecisiveSectorId = decisiveSectorId;
            EconomyOfForceSectorIds = economyOfForceSectorIds ?? Array.Empty<int>();
            InferiorForcePosture = inferiorForcePosture;
            Confidence = Clamp01(confidence);
            AllowAssault = allowAssault;
        }

        public float CurrentGlobalOdds { get; }
        public float ProjectedGlobalOdds { get; }
        public int DecisiveSectorId { get; }
        public int[] EconomyOfForceSectorIds { get; }
        public TacticalInferiorForcePosture InferiorForcePosture { get; }
        public float Confidence { get; }
        public bool AllowAssault { get; }

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

    public static class TacticalOddsDoctrine
    {
        public static TacticalOddsAssessment Evaluate(TacticalOddsInput input)
        {
            var sectorLedger = TacticalSectorLedger.Evaluate(input.Sectors);
            float enemyCurrent = Math.Max(
                input.EnemyStrengthConfirmed,
                Math.Max(input.EnemyStrengthRecent * 0.75f, input.EnemyStrengthInferred * 0.5f));
            bool noContact = input.Contact.State == TacticalContactState.None || enemyCurrent <= 0f;
            float ownBlend = input.OwnExperienceBlend;
            float enemyWeighted = Math.Max(1f, enemyCurrent * input.EnemyExperienceBlend);
            float current = noContact ? 0f : input.OwnStrength * ownBlend / enemyWeighted;
            float projected = noContact ? 0f : (input.OwnStrength + input.ReinforcementStrength24h) * ownBlend / enemyWeighted;
            TacticalInferiorForcePosture posture = TacticalInferiorForcePosture.None;

            if (noContact)
            {
                posture = TacticalInferiorForcePosture.ProbeOrHold;
            }
            else if (projected < 0.55f && input.TerrainAdvantage < 0.5f)
            {
                posture = TacticalInferiorForcePosture.PreserveOrRetreat;
            }
            else if (current < 0.6f &&
                (input.ReinforcementStrength24h > input.OwnStrength * 0.5f || input.TerrainAdvantage >= 0.5f))
            {
                posture = TacticalInferiorForcePosture.DelayOnStrongGround;
            }

            bool allowAssault = input.Contact.State == TacticalContactState.Confirmed
                && input.Contact.Confidence >= 0.8f
                && current >= 1.75f
                && sectorLedger.DecisiveSectorId >= 0
                && sectorLedger.Sectors.Any(s =>
                    s.SectorId == sectorLedger.DecisiveSectorId &&
                    !s.StrongPoint &&
                    !s.FlankRisk);

            return new TacticalOddsAssessment(
                current,
                projected,
                sectorLedger.DecisiveSectorId,
                sectorLedger.EconomyOfForceSectorIds,
                posture,
                input.Contact.Confidence,
                allowAssault);
        }
    }
}
