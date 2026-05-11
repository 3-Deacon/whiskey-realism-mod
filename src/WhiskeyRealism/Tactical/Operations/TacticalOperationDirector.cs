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
                ShouldSoftAbortCommitted(input))
            {
                return SoftAbort(input, "odds-collapse");
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

        private static bool ShouldSoftAbortCommitted(TacticalOperationDirectorInput input)
        {
            if (!TryFindObjective(input.Objectives, input.Current.PrimaryObjectiveId, out BattlefieldObjectiveEstimate primary))
            {
                return false;
            }

            float odds = Odds(input.OwnStrength, primary.EnemyStrength);
            if (odds < 0.75f)
            {
                return true;
            }

            return input.ReserveFraction < 0.05f && odds < 1.10f;
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

                float score = objective.Value +
                    objective.Confidence01 -
                    objective.TerrainStrength -
                    objective.ApproachDifficulty;
                if (!found || score > bestScore)
                {
                    best = objective;
                    bestScore = score;
                    found = true;
                }
            }

            return found;
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
                if (string.Equals(objective.ObjectiveId, objectiveId, StringComparison.Ordinal))
                {
                    match = objective;
                    return true;
                }
            }

            return false;
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
