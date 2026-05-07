using System;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalContactState
    {
        None = 0,
        Inferred = 1,
        Recent = 2,
        Confirmed = 3
    }

    public readonly struct TacticalContactInput
    {
        public TacticalContactInput(
            float visibleEnemyStrength,
            float recentEnemyStrength,
            float inferredEnemyStrength,
            float secondsSinceLastConfirmed,
            bool receivedFire,
            bool inFog)
        {
            VisibleEnemyStrength = Sanitize(visibleEnemyStrength);
            RecentEnemyStrength = Sanitize(recentEnemyStrength);
            InferredEnemyStrength = Sanitize(inferredEnemyStrength);
            SecondsSinceLastConfirmed = Sanitize(secondsSinceLastConfirmed);
            ReceivedFire = receivedFire;
            InFog = inFog;
        }

        public float VisibleEnemyStrength { get; }
        public float RecentEnemyStrength { get; }
        public float InferredEnemyStrength { get; }
        public float SecondsSinceLastConfirmed { get; }
        public bool ReceivedFire { get; }
        public bool InFog { get; }

        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, value);
        }
    }

    public readonly struct TacticalContactAssessment
    {
        public TacticalContactAssessment(
            TacticalContactState state,
            float confidence,
            float estimatedEnemyStrength,
            string reason)
        {
            State = state;
            Confidence = Clamp01(confidence);
            EstimatedEnemyStrength = Sanitize(estimatedEnemyStrength);
            Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim();
        }

        public TacticalContactState State { get; }
        public float Confidence { get; }
        public float EstimatedEnemyStrength { get; }
        public string Reason { get; }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, value);
        }
    }

    public static class TacticalContactLedger
    {
        public static TacticalContactAssessment Classify(TacticalContactInput input)
        {
            if (input.VisibleEnemyStrength > 0f || input.ReceivedFire)
            {
                return new TacticalContactAssessment(
                    TacticalContactState.Confirmed,
                    input.ReceivedFire ? 0.95f : 0.9f,
                    Math.Max(input.VisibleEnemyStrength, input.RecentEnemyStrength),
                    input.ReceivedFire ? "received-fire" : "visible");
            }

            if (input.RecentEnemyStrength > 0f && input.SecondsSinceLastConfirmed <= 300f)
            {
                float confidence = 0.75f * (1f - input.SecondsSinceLastConfirmed / 600f);
                return new TacticalContactAssessment(
                    TacticalContactState.Recent,
                    confidence,
                    input.RecentEnemyStrength,
                    "recent");
            }

            if (input.InferredEnemyStrength > 0f ||
                (input.RecentEnemyStrength > 0f && input.SecondsSinceLastConfirmed <= 1200f))
            {
                float estimated = Math.Max(input.InferredEnemyStrength, input.RecentEnemyStrength * 0.5f);
                return new TacticalContactAssessment(
                    TacticalContactState.Inferred,
                    input.InFog ? 0.35f : 0.45f,
                    estimated,
                    "inferred");
            }

            return new TacticalContactAssessment(TacticalContactState.None, 0f, 0f, "none");
        }
    }
}
