using System;
using WhiskeyRealism.Tactical.Orchestrator;

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
            BattlefieldObjectiveEstimate[] objectives,
            float operationAssignedStrength = 0f,
            BattleStance stance = BattleStance.Meeting)
        {
            Current = current;
            CurrentTimeSeconds = SanitizeFloorZero(currentTimeSeconds);
            OwnStrength = SanitizeFloorZero(ownStrength);
            ReserveFraction = Clamp01(reserveFraction);
            Aggression01 = Clamp01(aggression01);
            Caution01 = Clamp01(caution01);
            Objectives = objectives ?? Array.Empty<BattlefieldObjectiveEstimate>();
            OperationAssignedStrength = SanitizeFloorZero(operationAssignedStrength);
            Stance = stance;
        }

        public OperationRecord Current { get; }
        public float CurrentTimeSeconds { get; }
        public float OwnStrength { get; }
        public float ReserveFraction { get; }
        public float Aggression01 { get; }
        public float Caution01 { get; }
        public BattlefieldObjectiveEstimate[] Objectives { get; }
        // Live strength of the command nodes the committed operation has tasked to
        // attack/support (summed by the coordinator from vanilla regiments). 0 = no
        // assignment yet (e.g. the first tick after commitment) -> army strength.
        public float OperationAssignedStrength { get; }
        // The side's battle stance (vanilla GetBattleType via
        // ArmyEvidenceBuilder.ReadBattleStance). A Defender is never sent probing.
        public BattleStance Stance { get; }

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

        public TacticalOperationDirectorInput WithOperationAssignedStrength(float assignedStrength)
        {
            return new TacticalOperationDirectorInput(
                Current,
                CurrentTimeSeconds,
                OwnStrength,
                ReserveFraction,
                Aggression01,
                Caution01,
                Objectives,
                assignedStrength,
                Stance);
        }

        public TacticalOperationDirectorInput WithStance(BattleStance stance)
        {
            return new TacticalOperationDirectorInput(
                Current,
                CurrentTimeSeconds,
                OwnStrength,
                ReserveFraction,
                Aggression01,
                Caution01,
                Objectives,
                OperationAssignedStrength,
                stance);
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

    // T14 (2026-10-14): a committed operation no longer flickers Committed <->
    // Scouting on a one-tick confidence dip.
    //   - Emergency exits ignore the commit window: contact lost, odds collapse, and a
    //     contact revealed as a screen (main line no longer exposed).
    //   - Inside the commit window nothing else demotes the operation.
    //   - Enter/exit hysteresis: commitment needs formed main-line evidence
    //     (confidence >= 0.65, TacticalDecisionDoctrine.MainLineConfidenceFloor);
    //     a committed operation is only downgraded once confidence falls below
    //     CommittedExitConfidenceFloor (0.5), and re-selection after the window does
    //     not demote it while that exit evidence still holds.
    //   - Once committed, odds are the operation's assigned force (attack/support nodes,
    //     OperationAssignedStrength) against the objective-local enemy, not the whole
    //     army (totalactiveforce) against a local enemy. Before commitment nothing is
    //     assigned yet, so the commit decision still weighs the available army.
    public static class TacticalOperationDirector
    {
        public const float CommittedExitConfidenceFloor = 0.50f;

        public static TacticalOperationDirectorDecision Decide(TacticalOperationDirectorInput input)
        {
            bool activeOperation = IsActiveOperation(input.Current.Phase);
            bool committed = input.Current.Phase == TacticalOperationPhase.Committed;
            bool insideCommitWindow = committed && input.CurrentTimeSeconds < input.Current.MinimumCommitSeconds;
            if (committed &&
                TryEvaluateCommittedContact(input, insideCommitWindow, out TacticalOperationDirectorDecision committedContactDecision))
            {
                return committedContactDecision;
            }

            if (insideCommitWindow)
            {
                return new TacticalOperationDirectorDecision(input.Current, "commit-window");
            }

            TacticalOperationDirectorDecision selected = SelectOperation(input, activeOperation);
            if (committed && IsDemotionOfSameObjective(input.Current, selected.Operation) && HoldsCommittedExitEvidence(input))
            {
                return new TacticalOperationDirectorDecision(input.Current, "commit-hysteresis");
            }

            return selected;
        }

        private static TacticalOperationDirectorDecision SelectOperation(TacticalOperationDirectorInput input, bool activeOperation)
        {

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

            float operationStrength = OperationStrength(input);
            TacticalObjectiveGate objectiveGate = TacticalDecisionDoctrine.ClassifyObjective(
                primary,
                operationStrength,
                input.ReserveFraction);

            // BC9: no enemy seen at a newly selected objective means scouting, never a
            // commit on own / max(1, 0) odds: the fog-honest AI does not know the
            // objective is empty, so the recon producer and the Probe task go and look.
            // An operation already active on that objective keeps the shipped rules
            // (a committed advance on an empty known map objective continues,
            // ShouldCancelCommittedContact).
            bool noContact = primary.EnemyStrength <= 0f &&
                !(activeOperation && ObjectiveIdMatches(input.Current.PrimaryObjectiveId, primary.ObjectiveId));

            // BC9: a Defender (vanilla GetBattleType 2) scouts without being sent
            // forward: the DefensiveNetwork shape keeps CommandDoctrineAssignment off
            // the Probe tasks, so its screens screen and the recon producer's cavalry
            // looks (information-gathering, not an attack).
            if (input.Stance == BattleStance.Defender &&
                (noContact || objectiveGate == TacticalObjectiveGate.ReconnaissanceContact))
            {
                return new TacticalOperationDirectorDecision(
                    new OperationRecord(
                        TacticalOperationShape.DefensiveNetwork,
                        TacticalOperationPhase.Scouting,
                        primary.ObjectiveId,
                        input.CurrentTimeSeconds + 420f),
                    "defender-screen");
            }

            if (noContact)
            {
                return new TacticalOperationDirectorDecision(
                    new OperationRecord(
                        TacticalOperationShape.FixAndFlank,
                        TacticalOperationPhase.Scouting,
                        primary.ObjectiveId,
                        input.CurrentTimeSeconds + 420f),
                    "no-contact-scout");
            }

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

            float odds = Odds(operationStrength, primary.EnemyStrength);
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

        private static TacticalOperationDirectorDecision Downgrade(TacticalOperationDirectorInput input)
        {
            return new TacticalOperationDirectorDecision(
                new OperationRecord(
                    input.Current.Shape,
                    TacticalOperationPhase.Scouting,
                    input.Current.PrimaryObjectiveId,
                    input.CurrentTimeSeconds + 420f),
                "contact-downgraded");
        }

        private static bool TryEvaluateCommittedContact(
            TacticalOperationDirectorInput input,
            bool insideCommitWindow,
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
                    OperationStrength(input),
                    input.ReserveFraction))
            {
                decision = SoftAbort(input, "odds-collapse");
                return true;
            }

            // The contact turned out to be a screen (enemy present, main line no longer
            // exposed): not a confidence dip, so it may break the commitment at once.
            if (primary.EnemyStrength > 0f && !primary.MainLineExposed)
            {
                decision = Downgrade(input);
                return true;
            }

            if (insideCommitWindow) return false;

            if (primary.EnemyStrength > 0f && primary.Confidence01 < CommittedExitConfidenceFloor)
            {
                decision = Downgrade(input);
                return true;
            }

            return false;
        }

        // Re-selection after the commit window would demote (Scouting / Forming) the
        // same objective.
        private static bool IsDemotionOfSameObjective(OperationRecord current, OperationRecord selected)
        {
            if (selected.Phase != TacticalOperationPhase.Scouting && selected.Phase != TacticalOperationPhase.Forming)
                return false;
            return ObjectiveIdMatches(current.PrimaryObjectiveId, selected.PrimaryObjectiveId);
        }

        // Exit-side evidence for keeping a commitment: main line still exposed, enemy
        // still present, confidence at or above the exit floor. Odds collapse was already
        // handled by the soft-abort branch.
        private static bool HoldsCommittedExitEvidence(TacticalOperationDirectorInput input)
        {
            if (!TryFindObjective(input.Objectives, input.Current.PrimaryObjectiveId, out BattlefieldObjectiveEstimate primary))
                return false;
            return primary.MainLineExposed
                && primary.EnemyStrength > 0f
                && primary.Confidence01 >= CommittedExitConfidenceFloor;
        }

        // Strength actually assigned to a committed operation; the available army when
        // nothing is committed or no node has been tasked yet.
        private static float OperationStrength(TacticalOperationDirectorInput input)
        {
            if (input.Current.Phase == TacticalOperationPhase.Committed && input.OperationAssignedStrength > 0f)
                return input.OperationAssignedStrength;
            return input.OwnStrength;
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
