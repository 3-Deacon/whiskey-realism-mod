using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public enum InferredIntent
    {
        Unknown = 0,
        Attack,
        Defend,
        Withdraw,
        Probe,
        Refuse
    }

    public enum EvidenceTag
    {
        Unknown = 0,
        SectorConcentration,
        ReserveCommitted,
        ReserveUncommitted,
        ContactSpotted,
        ContactBroken,
        ReceivingFire,
        ForceImbalanceDownward,
        ForceImbalanceUpward,
        ReinforcementsArriving,
        FlankExposure
    }

    /// <summary>
    /// Inferred visible-state enemy intent consumed by ArmyIntentInference,
    /// ArmyOrchestrator, and PlaybookContext. Read-only tactical snapshot.
    /// </summary>
    public readonly struct TacticalIntentModel
    {
        public TacticalIntentModel(
            InferredIntent primaryIntent,
            int inferredMainEffort,
            float confidence01,
            float ageSeconds,
            EvidenceTag[] supportingEvidence)
        {
            PrimaryIntent = primaryIntent;
            InferredMainEffort = inferredMainEffort;
            Confidence01 = Clamp01(confidence01);
            AgeSeconds = SanitizeAge(ageSeconds);
            SupportingEvidence = CopyEvidence(supportingEvidence);
        }

        public InferredIntent PrimaryIntent { get; }
        public int InferredMainEffort { get; }
        public float Confidence01 { get; }
        public float AgeSeconds { get; }

        /// <summary>
        /// Evidence tags supporting the inference. Treat as read-only; this instance
        /// owns a copy so later caller-side mutations do not rewrite the snapshot.
        /// </summary>
        public EvidenceTag[] SupportingEvidence { get; }

        private static EvidenceTag[] CopyEvidence(EvidenceTag[] evidence)
        {
            if (evidence == null || evidence.Length == 0) return Array.Empty<EvidenceTag>();
            var copy = new EvidenceTag[evidence.Length];
            Array.Copy(evidence, copy, evidence.Length);
            return copy;
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static float SanitizeAge(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return v < 0f ? 0f : v;
        }
    }
}
