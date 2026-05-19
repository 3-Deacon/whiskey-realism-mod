namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalObjectiveGate
    {
        NoUsableContact = 0,
        ReconnaissanceContact = 1,
        ExposedMainLine = 2,
        ExposedWeakPoint = 3,
        UnreliableContact = 4
    }

    public static class TacticalDecisionDoctrine
    {
        private const float ReconConfidenceFloor = 0.25f;
        private const float MainLineConfidenceFloor = 0.65f;
        private const float FixAndFlankOdds = 1.25f;
        private const float MinimumManeuverReserve = 0.10f;
        private const float SoftAbortOdds = 0.75f;
        private const float ReserveDepletedFraction = 0.05f;
        private const float ReserveDepletedOdds = 1.10f;

        public static TacticalObjectiveGate ClassifyObjective(
            BattlefieldObjectiveEstimate objective,
            float ownStrength,
            float reserveFraction)
        {
            if (objective.EnemyStrength <= 0f)
            {
                return objective.Confidence01 > 0f
                    ? TacticalObjectiveGate.UnreliableContact
                    : TacticalObjectiveGate.NoUsableContact;
            }

            if (IsReconnaissanceContact(objective))
            {
                return TacticalObjectiveGate.ReconnaissanceContact;
            }

            if (ShouldCommitFixAndFlank(objective, ownStrength, reserveFraction))
            {
                return TacticalObjectiveGate.ExposedWeakPoint;
            }

            if (HasFormedMainLineEvidence(objective))
            {
                return TacticalObjectiveGate.ExposedMainLine;
            }

            return TacticalObjectiveGate.UnreliableContact;
        }

        public static bool IsReconnaissanceContact(BattlefieldObjectiveEstimate objective)
        {
            return objective.EnemyStrength > 0f &&
                !HasFormedMainLineEvidence(objective) &&
                objective.Confidence01 >= ReconConfidenceFloor;
        }

        public static bool ShouldCancelCommittedContact(BattlefieldObjectiveEstimate objective)
        {
            if (IsKnownMapObjective(objective.Type))
            {
                return false;
            }

            return objective.EnemyStrength <= 0f;
        }

        public static bool ShouldDowngradeCommittedContact(BattlefieldObjectiveEstimate objective)
        {
            return objective.EnemyStrength > 0f &&
                !HasFormedMainLineEvidence(objective);
        }

        public static bool ShouldCommitFixAndFlank(
            BattlefieldObjectiveEstimate objective,
            float ownStrength,
            float reserveFraction)
        {
            if (!HasFormedMainLineEvidence(objective)) return false;
            if (reserveFraction < MinimumManeuverReserve) return false;
            return Odds(ownStrength, objective.EnemyStrength) >= FixAndFlankOdds;
        }

        public static bool ShouldSoftAbortCommitted(
            BattlefieldObjectiveEstimate objective,
            float ownStrength,
            float reserveFraction)
        {
            float odds = Odds(ownStrength, objective.EnemyStrength);
            if (odds < SoftAbortOdds) return true;
            return reserveFraction < ReserveDepletedFraction && odds < ReserveDepletedOdds;
        }

        public static bool HasFormedMainLineEvidence(BattlefieldObjectiveEstimate objective)
        {
            return objective.MainLineExposed &&
                objective.Confidence01 >= MainLineConfidenceFloor &&
                objective.EnemyStrength > 0f;
        }

        public static bool HasAttackableLineEvidence(
            OperationRecord operation,
            BattlefieldObjectiveEstimate objective)
        {
            if (!objective.MainLineExposed || objective.EnemyStrength <= 0f) return false;
            if (objective.Confidence01 >= MainLineConfidenceFloor) return true;
            return operation.Phase == TacticalOperationPhase.Committed &&
                objective.Type == TacticalObjectiveType.EnemyLine &&
                objective.Confidence01 >= 0.50f;
        }

        public static bool UsesReconBreakoffTarget(CommandTaskType task)
        {
            return task == CommandTaskType.Scout ||
                task == CommandTaskType.Probe ||
                task == CommandTaskType.Screen;
        }

        public static bool ShouldBreakOffRecon(
            CommandTaskType task,
            bool closeEngaged,
            bool hasFallbackTarget)
        {
            return closeEngaged &&
                hasFallbackTarget &&
                UsesReconBreakoffTarget(task);
        }

        public static CommandTaskType ReconnaissanceTaskFor(CommandNodeRole role)
        {
            switch (role)
            {
                case CommandNodeRole.MainEffort:
                case CommandNodeRole.Probe:
                    return CommandTaskType.Probe;
                case CommandNodeRole.SupportingAttack:
                case CommandNodeRole.FixingForce:
                case CommandNodeRole.ScreeningForce:
                    return CommandTaskType.Screen;
                case CommandNodeRole.Defender:
                    return CommandTaskType.HoldObjective;
                default:
                    return CommandTaskType.FormUp;
            }
        }

        private static float Odds(float ownStrength, float enemyStrength)
        {
            return ownStrength / Max(1f, enemyStrength);
        }

        private static bool IsKnownMapObjective(TacticalObjectiveType type)
        {
            return type == TacticalObjectiveType.VictoryPoint ||
                type == TacticalObjectiveType.Bridge ||
                type == TacticalObjectiveType.Ford ||
                type == TacticalObjectiveType.RoadJunction ||
                type == TacticalObjectiveType.Town ||
                type == TacticalObjectiveType.Ridge ||
                type == TacticalObjectiveType.ChokePoint;
        }

        private static float Max(float left, float right)
        {
            return left > right ? left : right;
        }
    }
}
