using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public readonly struct TacticalOperationDirectorInput
    {
        private TacticalOperationDirectorInput(
            OperationRecord current,
            float currentTimeSeconds,
            float ownStrength,
            float reserveFraction,
            float aggression01,
            float caution01,
            BattlefieldObjectiveEstimate[] objectives)
        {
            Current = current;
            CurrentTimeSeconds = SanitizeFloorZero(currentTimeSeconds);
            OwnStrength = SanitizeFloorZero(ownStrength);
            ReserveFraction = Clamp01(reserveFraction);
            Aggression01 = Clamp01(aggression01);
            Caution01 = Clamp01(caution01);
            Objectives = objectives ?? Array.Empty<BattlefieldObjectiveEstimate>();
        }

        public OperationRecord Current { get; }
        public float CurrentTimeSeconds { get; }
        public float OwnStrength { get; }
        public float ReserveFraction { get; }
        public float Aggression01 { get; }
        public float Caution01 { get; }
        public BattlefieldObjectiveEstimate[] Objectives { get; }

        public static TacticalOperationDirectorInput ForTest(
            OperationRecord current,
            float currentTimeSeconds,
            float ownStrength,
            float reserveFraction,
            float aggression01,
            float caution01,
            BattlefieldObjectiveEstimate[] objectives)
        {
            return new TacticalOperationDirectorInput(
                current,
                currentTimeSeconds,
                ownStrength,
                reserveFraction,
                aggression01,
                caution01,
                objectives);
        }

        public TacticalOperationDirectorInput WithOwnStrength(float ownStrength)
        {
            return new TacticalOperationDirectorInput(
                Current,
                CurrentTimeSeconds,
                ownStrength,
                ReserveFraction,
                Aggression01,
                Caution01,
                Objectives);
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
    }

    public readonly struct TacticalOperationDirectorDecision
    {
        public TacticalOperationDirectorDecision(OperationRecord operation, string reason)
        {
            Operation = operation;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
        }

        public OperationRecord Operation { get; }
        public string Reason { get; }
    }

    public static class TacticalOperationDirector
    {
        public static TacticalOperationDirectorDecision Decide(TacticalOperationDirectorInput input)
        {
            bool activeOperation = IsActiveOperation(input.Current.Phase);
            if (input.Current.Phase == TacticalOperationPhase.Committed &&
                TryEvaluateCommittedContact(input, out TacticalOperationDirectorDecision committedContactDecision))
            {
                return committedContactDecision;
            }

            if (input.Current.Phase == TacticalOperationPhase.Committed &&
                input.CurrentTimeSeconds < input.Current.MinimumCommitSeconds)
            {
                return new TacticalOperationDirectorDecision(input.Current, "commit-window");
            }

            if (activeOperation &&
                !TryFindObjective(input.Objectives, input.Current.PrimaryObjectiveId, out _))
            {
                return new TacticalOperationDirectorDecision(input.Current, "objective-picture-missing");
            }

            if (!TryPickBestObjective(input.Objectives, out BattlefieldObjectiveEstimate primary))
            {
                return activeOperation
                    ? new TacticalOperationDirectorDecision(input.Current, "objective-picture-missing")
                    : new TacticalOperationDirectorDecision(OperationRecord.Noop, "no-objective");
            }

            TacticalObjectiveGate objectiveGate = TacticalDecisionDoctrine.ClassifyObjective(
                primary,
                input.OwnStrength,
                input.ReserveFraction);

            if (objectiveGate == TacticalObjectiveGate.ReconnaissanceContact)
            {
                return new TacticalOperationDirectorDecision(
                    new OperationRecord(
                        TacticalOperationShape.FixAndFlank,
                        TacticalOperationPhase.Scouting,
                        primary.ObjectiveId,
                        input.CurrentTimeSeconds + 420f),
                    "recon-contact");
            }

            if (CanParallelAttack(input))
            {
                return new TacticalOperationDirectorDecision(
                    new OperationRecord(
                        TacticalOperationShape.ParallelObjectives,
                        TacticalOperationPhase.Committed,
                        primary.ObjectiveId,
                        input.CurrentTimeSeconds + 1200f),
                    "parallel-advantage");
            }

            if (objectiveGate == TacticalObjectiveGate.ExposedWeakPoint)
            {
                return new TacticalOperationDirectorDecision(
                    new OperationRecord(
                        TacticalOperationShape.FixAndFlank,
                        TacticalOperationPhase.Committed,
                        primary.ObjectiveId,
                        input.CurrentTimeSeconds + 900f),
                    "fix-and-flank");
            }

            float odds = Odds(input.OwnStrength, primary.EnemyStrength);
            TacticalOperationShape shape = odds >= 1.35f
                ? TacticalOperationShape.SingleMainEffort
                : TacticalOperationShape.DefensiveNetwork;
            TacticalOperationPhase phase = odds >= 1.35f
                ? TacticalOperationPhase.Committed
                : TacticalOperationPhase.Forming;

            return new TacticalOperationDirectorDecision(
                new OperationRecord(shape, phase, primary.ObjectiveId, input.CurrentTimeSeconds + 900f),
                "selected");
        }

        private static bool TryEvaluateCommittedContact(
            TacticalOperationDirectorInput input,
            out TacticalOperationDirectorDecision decision)
        {
            decision = default(TacticalOperationDirectorDecision);
            if (!TryFindObjective(input.Objectives, input.Current.PrimaryObjectiveId, out BattlefieldObjectiveEstimate primary))
            {
                return false;
            }

            if (TacticalDecisionDoctrine.ShouldCancelCommittedContact(primary))
            {
                decision = SoftAbort(input, "contact-lost");
                return true;
            }

            if (TacticalDecisionDoctrine.ShouldSoftAbortCommitted(
                    primary,
                    input.OwnStrength,
                    input.ReserveFraction))
            {
                decision = SoftAbort(input, "odds-collapse");
                return true;
            }

            if (TacticalDecisionDoctrine.ShouldDowngradeCommittedContact(primary))
            {
                decision = new TacticalOperationDirectorDecision(
                    new OperationRecord(
                        input.Current.Shape,
                        TacticalOperationPhase.Scouting,
                        input.Current.PrimaryObjectiveId,
                        input.CurrentTimeSeconds + 420f),
                    "contact-downgraded");
                return true;
            }

            return false;
        }

        private static TacticalOperationDirectorDecision SoftAbort(
            TacticalOperationDirectorInput input,
            string reason)
        {
            return new TacticalOperationDirectorDecision(
                new OperationRecord(
                    input.Current.Shape,
                    TacticalOperationPhase.SoftAbort,
                    input.Current.PrimaryObjectiveId,
                    input.Current.MinimumCommitSeconds),
                reason);
        }

        private static bool IsActiveOperation(TacticalOperationPhase phase)
        {
            return phase == TacticalOperationPhase.Committed ||
                phase == TacticalOperationPhase.Forming ||
                phase == TacticalOperationPhase.Exploiting ||
                phase == TacticalOperationPhase.Consolidating ||
                phase == TacticalOperationPhase.SoftAbort;
        }

        private static bool CanParallelAttack(TacticalOperationDirectorInput input)
        {
            if (input.Objectives.Length < 2 || input.ReserveFraction < 0.15f)
            {
                return false;
            }

            float requiredOdds = 1.65f + input.Caution01 * 0.2f;
            if (input.Aggression01 >= 0.7f)
            {
                requiredOdds -= 0.15f;
            }

            float enemyStrength = 0f;
            int usable = 0;
            for (int i = 0; i < input.Objectives.Length; i++)
            {
                BattlefieldObjectiveEstimate objective = input.Objectives[i];
                if (!IsUsableObjective(objective) ||
                    !objective.MainLineExposed ||
                    objective.Confidence01 < 0.7f)
                {
                    continue;
                }

                enemyStrength += Max(1f, objective.EnemyStrength);
                usable++;
            }

            return usable >= 2 && Odds(input.OwnStrength, enemyStrength) >= requiredOdds;
        }

        private static bool TryPickBestObjective(
            BattlefieldObjectiveEstimate[] objectives,
            out BattlefieldObjectiveEstimate best)
        {
            best = default(BattlefieldObjectiveEstimate);
            float bestScore = float.NegativeInfinity;
            bool found = false;

            for (int i = 0; i < objectives.Length; i++)
            {
                BattlefieldObjectiveEstimate objective = objectives[i];
                if (!IsUsableObjective(objective))
                {
                    continue;
                }

                float score = ObjectiveSelectionScore(objective);
                if (!found || score > bestScore)
                {
                    best = objective;
                    bestScore = score;
                    found = true;
                }
            }

            return found;
        }

        private static float ObjectiveSelectionScore(BattlefieldObjectiveEstimate objective)
        {
            float score = objective.Value +
                objective.Confidence01 -
                objective.TerrainStrength -
                objective.ApproachDifficulty;

            if (objective.MainLineExposed)
            {
                score += 0.35f;
            }

            if (objective.Type == TacticalObjectiveType.EnemyLine)
            {
                score += 0.20f;
            }

            if (objective.Type == TacticalObjectiveType.UnknownVanillaObjective &&
                !objective.MainLineExposed &&
                objective.EnemyStrength <= 0f)
            {
                score -= 0.25f;
            }

            return score;
        }

        private static bool TryFindObjective(
            BattlefieldObjectiveEstimate[] objectives,
            string objectiveId,
            out BattlefieldObjectiveEstimate match)
        {
            match = default(BattlefieldObjectiveEstimate);
            if (string.IsNullOrWhiteSpace(objectiveId))
            {
                return false;
            }

            for (int i = 0; i < objectives.Length; i++)
            {
                BattlefieldObjectiveEstimate objective = objectives[i];
                if (ObjectiveIdMatches(objective.ObjectiveId, objectiveId))
                {
                    match = objective;
                    return true;
                }
            }

            return false;
        }

        private static bool ObjectiveIdMatches(string left, string right)
        {
            if (string.Equals(left, right, StringComparison.Ordinal)) return true;
            return string.Equals(NormalizeObjectiveId(left), NormalizeObjectiveId(right), StringComparison.Ordinal);
        }

        private static string NormalizeObjectiveId(string objectiveId)
        {
            if (string.IsNullOrWhiteSpace(objectiveId)) return string.Empty;
            char[] chars = objectiveId.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsWhiteSpace(chars[i]) || chars[i] == '|')
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        private static bool IsUsableObjective(BattlefieldObjectiveEstimate objective)
        {
            return !string.IsNullOrWhiteSpace(objective.ObjectiveId) &&
                objective.Confidence01 > 0f;
        }

        private static float Odds(float ownStrength, float enemyStrength)
        {
            return ownStrength / Max(1f, enemyStrength);
        }

        private static float Max(float left, float right)
        {
            return left > right ? left : right;
        }
    }
}
