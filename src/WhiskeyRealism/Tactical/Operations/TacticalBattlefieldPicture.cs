using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalContactKind
    {
        Unknown = 0,
        SkirmisherScreen = 1,
        FormedLine = 2,
        Artillery = 3,
        CavalryScreen = 4
    }

    public readonly struct BattlefieldContactInput
    {
        public string ContactId { get; }
        public string ObjectiveId { get; }
        public TacticalContactKind Kind { get; }
        public float EstimatedStrength { get; }
        public float LastSeenSeconds { get; }
        public bool Visible { get; }
        public bool RecentlyFired { get; }
        public float X { get; }
        public float Z { get; }

        public BattlefieldContactInput(
            string contactId,
            string objectiveId,
            TacticalContactKind kind,
            float estimatedStrength,
            float lastSeenSeconds,
            bool visible,
            bool recentlyFired,
            float x,
            float z)
        {
            ContactId = string.IsNullOrWhiteSpace(contactId) ? "contact-unknown" : contactId;
            ObjectiveId = string.IsNullOrWhiteSpace(objectiveId) ? "objective-unknown" : objectiveId;
            Kind = kind;
            EstimatedStrength = SanitizeFloorZero(estimatedStrength);
            LastSeenSeconds = SanitizeFloorZero(lastSeenSeconds);
            Visible = visible;
            RecentlyFired = recentlyFired;
            X = SanitizeFinite(x);
            Z = SanitizeFinite(z);
        }

        private static float SanitizeFloorZero(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }

        private static float SanitizeFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }

    public readonly struct BattlefieldObjectiveInput
    {
        public string ObjectiveId { get; }
        public TacticalObjectiveType Type { get; }
        public float Value { get; }
        public float X { get; }
        public float Z { get; }
        public float TerrainStrength { get; }
        public float ApproachDifficulty { get; }
        public float SourceConfidence01 { get; }

        public BattlefieldObjectiveInput(
            string objectiveId,
            TacticalObjectiveType type,
            float value,
            float x,
            float z,
            float terrainStrength,
            float approachDifficulty,
            float sourceConfidence)
        {
            ObjectiveId = string.IsNullOrWhiteSpace(objectiveId) ? "objective-unknown" : objectiveId;
            Type = type;
            Value = Clamp01(value);
            X = SanitizeFinite(x);
            Z = SanitizeFinite(z);
            TerrainStrength = Clamp01(terrainStrength);
            ApproachDifficulty = Clamp01(approachDifficulty);
            SourceConfidence01 = Clamp01(sourceConfidence);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static float SanitizeFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }

    public readonly struct BattlefieldObjectiveEstimate
    {
        public string ObjectiveId { get; }
        public TacticalObjectiveType Type { get; }
        public float EnemyStrength { get; }
        public float Confidence01 { get; }
        public bool MainLineExposed { get; }
        public float Value { get; }
        public float X { get; }
        public float Z { get; }
        public float TerrainStrength { get; }
        public float ApproachDifficulty { get; }
        public TacticalApproachAvenueEstimate ApproachAvenue { get; }

        public BattlefieldObjectiveEstimate(
            string objectiveId,
            TacticalObjectiveType type,
            float enemyStrength,
            float confidence01,
            bool mainLineExposed,
            float value,
            float x,
            float z,
            float terrainStrength,
            float approachDifficulty)
            : this(
                objectiveId,
                type,
                enemyStrength,
                confidence01,
                mainLineExposed,
                value,
                x,
                z,
                terrainStrength,
                approachDifficulty,
                TacticalApproachAvenueEstimate.None)
        {
        }

        public BattlefieldObjectiveEstimate(
            string objectiveId,
            TacticalObjectiveType type,
            float enemyStrength,
            float confidence01,
            bool mainLineExposed,
            float value,
            float x,
            float z,
            float terrainStrength,
            float approachDifficulty,
            TacticalApproachAvenueEstimate approachAvenue)
        {
            ObjectiveId = string.IsNullOrWhiteSpace(objectiveId) ? "objective-unknown" : objectiveId;
            Type = type;
            EnemyStrength = SanitizeFloorZero(enemyStrength);
            Confidence01 = Clamp01(confidence01);
            MainLineExposed = mainLineExposed;
            Value = Clamp01(value);
            X = SanitizeFinite(x);
            Z = SanitizeFinite(z);
            TerrainStrength = Clamp01(terrainStrength);
            ApproachDifficulty = Clamp01(approachDifficulty);
            ApproachAvenue = approachAvenue.HasAvenue ? approachAvenue : TacticalApproachAvenueEstimate.None;
        }

        private static float SanitizeFloorZero(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static float SanitizeFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }

    public readonly struct BattlefieldPictureSnapshot
    {
        public BattlefieldObjectiveEstimate[] Objectives { get; }

        public BattlefieldPictureSnapshot(BattlefieldObjectiveEstimate[] objectives)
        {
            Objectives = objectives ?? Array.Empty<BattlefieldObjectiveEstimate>();
        }
    }

    public static class TacticalBattlefieldPicture
    {
        public static BattlefieldPictureSnapshot Build(
            BattlefieldContactInput[] contacts,
            BattlefieldObjectiveInput[] objectives,
            float nowSeconds)
        {
            contacts = contacts ?? Array.Empty<BattlefieldContactInput>();
            objectives = objectives ?? Array.Empty<BattlefieldObjectiveInput>();
            nowSeconds = SanitizeObservationTime(nowSeconds);

            var estimates = new BattlefieldObjectiveEstimate[objectives.Length];
            for (int objectiveIndex = 0; objectiveIndex < objectives.Length; objectiveIndex++)
            {
                BattlefieldObjectiveInput objective = objectives[objectiveIndex];
                float confidence = objective.SourceConfidence01;
                float enemyStrength = 0f;
                bool mainLineExposed = false;

                for (int contactIndex = 0; contactIndex < contacts.Length; contactIndex++)
                {
                    BattlefieldContactInput contact = contacts[contactIndex];
                    if (contact.ObjectiveId != objective.ObjectiveId) continue;
                    if (!IsBestContactObservation(contacts, contactIndex, objective.ObjectiveId, nowSeconds)) continue;

                    float ageSeconds = nowSeconds - contact.LastSeenSeconds;
                    if (ageSeconds < 0f) ageSeconds = 0f;

                    float freshness = FreshnessFactor(ageSeconds);
                    float observation = ObservationFactor(contact);
                    float evidenceFactor = freshness * observation;
                    float strength = contact.EstimatedStrength * StrengthWeight(contact.Kind) * evidenceFactor;
                    enemyStrength += strength;

                    float contactConfidence = ConfidenceWeight(contact.Kind) * evidenceFactor;
                    confidence = Max(confidence, contactConfidence);

                    if (contact.Kind == TacticalContactKind.FormedLine &&
                        contact.Visible &&
                        ageSeconds <= 60f &&
                        contact.EstimatedStrength > 0f)
                    {
                        mainLineExposed = true;
                    }
                }

                estimates[objectiveIndex] = new BattlefieldObjectiveEstimate(
                    objective.ObjectiveId,
                    objective.Type,
                    enemyStrength,
                    confidence,
                    mainLineExposed,
                    objective.Value,
                    objective.X,
                    objective.Z,
                    objective.TerrainStrength,
                    objective.ApproachDifficulty,
                    TacticalApproachAvenueEstimate.None);
            }

            return new BattlefieldPictureSnapshot(estimates);
        }

        private static float FreshnessFactor(float ageSeconds)
        {
            if (ageSeconds <= 60f) return 1f;
            if (ageSeconds <= 300f)
            {
                float staleFraction = (ageSeconds - 60f) / 240f;
                return 1f - (staleFraction * 0.75f);
            }

            if (ageSeconds <= 900f)
            {
                float staleFraction = (ageSeconds - 300f) / 600f;
                return 0.25f - (staleFraction * 0.15f);
            }

            return 0.10f;
        }

        private static float ObservationFactor(BattlefieldContactInput contact)
        {
            if (contact.Visible) return 1f;
            return contact.RecentlyFired ? 0.70f : 0.45f;
        }

        private static float StrengthWeight(TacticalContactKind kind)
        {
            if (kind == TacticalContactKind.FormedLine) return 1f;
            if (kind == TacticalContactKind.Artillery) return 0.70f;
            if (kind == TacticalContactKind.CavalryScreen) return 0.45f;
            if (kind == TacticalContactKind.SkirmisherScreen) return 0.35f;
            return 0.15f;
        }

        private static float ConfidenceWeight(TacticalContactKind kind)
        {
            if (kind == TacticalContactKind.FormedLine) return 0.80f;
            if (kind == TacticalContactKind.Artillery) return 0.60f;
            if (kind == TacticalContactKind.CavalryScreen) return 0.38f;
            if (kind == TacticalContactKind.SkirmisherScreen) return 0.35f;
            return 0.10f;
        }

        private static bool IsBestContactObservation(
            BattlefieldContactInput[] contacts,
            int candidateIndex,
            string objectiveId,
            float nowSeconds)
        {
            BattlefieldContactInput candidate = contacts[candidateIndex];
            float candidateScore = ContactEvidenceScore(candidate, nowSeconds);

            for (int i = 0; i < contacts.Length; i++)
            {
                if (i == candidateIndex) continue;

                BattlefieldContactInput other = contacts[i];
                if (other.ObjectiveId != objectiveId) continue;
                if (other.ContactId != candidate.ContactId) continue;

                float otherScore = ContactEvidenceScore(other, nowSeconds);
                if (otherScore > candidateScore) return false;
                if (otherScore == candidateScore && i < candidateIndex) return false;
            }

            return true;
        }

        private static float ContactEvidenceScore(BattlefieldContactInput contact, float nowSeconds)
        {
            float ageSeconds = nowSeconds - contact.LastSeenSeconds;
            if (ageSeconds < 0f) ageSeconds = 0f;
            float evidenceFactor = FreshnessFactor(ageSeconds) * ObservationFactor(contact);
            float strength = contact.EstimatedStrength * StrengthWeight(contact.Kind) * evidenceFactor;
            float confidence = ConfidenceWeight(contact.Kind) * evidenceFactor;
            return strength + (confidence * 1000f);
        }

        private static float Max(float left, float right)
        {
            return left > right ? left : right;
        }

        private static float SanitizeFloorZero(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }

        private static float SanitizeObservationTime(float value)
        {
            if (float.IsNaN(value) || float.IsPositiveInfinity(value)) return float.MaxValue;
            if (float.IsNegativeInfinity(value) || value < 0f) return 0f;
            return value;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
