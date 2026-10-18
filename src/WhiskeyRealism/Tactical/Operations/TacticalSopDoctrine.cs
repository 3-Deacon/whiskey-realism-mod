using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalSopAuthority
    {
        None = 0,
        Stay = 1,
        Hold = 2,
        Defend = 3,
        Scout = 4,
        Probe = 5,
        Screen = 6,
        Attack = 7,
        Assault = 8,
        Fallback = 9,
        Reserve = 10,
        Consolidate = 11
    }

    public readonly struct TacticalSopDecision
    {
        public TacticalSopDecision(
            TacticalSopAuthority authority,
            string reason)
        {
            Authority = authority;
            Reason = string.IsNullOrWhiteSpace(reason) ? "sop-unspecified" : reason.Trim();
        }

        public TacticalSopAuthority Authority { get; }
        public string Reason { get; }

        public static TacticalSopDecision None =>
            new TacticalSopDecision(TacticalSopAuthority.None, "sop-none");

        internal static TacticalSopDecision ForAssignedTask(CommandNodeRole role, CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.Scout:
                    return new TacticalSopDecision(TacticalSopAuthority.Scout, "task-scout");
                case CommandTaskType.Probe:
                    return new TacticalSopDecision(TacticalSopAuthority.Probe, "task-probe");
                case CommandTaskType.Screen:
                case CommandTaskType.GuardFlank:
                    return new TacticalSopDecision(TacticalSopAuthority.Screen, "task-screen");
                case CommandTaskType.AttackObjective:
                    return new TacticalSopDecision(TacticalSopAuthority.Attack, "task-attack");
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                    return new TacticalSopDecision(TacticalSopAuthority.Attack, "task-support");
                case CommandTaskType.FallBackToLine:
                case CommandTaskType.Delay:
                    return new TacticalSopDecision(TacticalSopAuthority.Fallback, "task-fallback");
                case CommandTaskType.ReserveWait:
                case CommandTaskType.ReleaseReserve:
                    return new TacticalSopDecision(TacticalSopAuthority.Reserve, "task-reserve");
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                    return new TacticalSopDecision(TacticalSopAuthority.Hold, "task-hold");
                case CommandTaskType.Consolidate:
                    return new TacticalSopDecision(TacticalSopAuthority.Consolidate, "task-consolidate");
                case CommandTaskType.FormUp:
                case CommandTaskType.AdvanceToAssembly:
                    return new TacticalSopDecision(TacticalSopAuthority.Defend, "task-form");
                default:
                    return role == CommandNodeRole.Unknown
                        ? None
                        : new TacticalSopDecision(TacticalSopAuthority.Stay, "task-stay");
            }
        }

        private static float Clamp01(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public static class TacticalSopDoctrine
    {
        public static TacticalSopDecision Resolve(
            CommandNodeRole role,
            CommandTaskType task,
            TacticalOperationPhase phase,
            BattlefieldObjectiveEstimate objective,
            float ownStrength,
            float reserveFraction)
        {
            if (task == CommandTaskType.None)
            {
                return TacticalSopDecision.None;
            }

            float odds = ResolveOdds(ownStrength, objective.EnemyStrength);
            float reserve = Clamp01(reserveFraction);

            switch (task)
            {
                case CommandTaskType.Scout:
                    return new TacticalSopDecision(TacticalSopAuthority.Scout, "scout-contact");
                case CommandTaskType.Probe:
                    return new TacticalSopDecision(TacticalSopAuthority.Probe, "probe-contact");
                case CommandTaskType.Screen:
                case CommandTaskType.GuardFlank:
                    return new TacticalSopDecision(TacticalSopAuthority.Screen, "screen-contact");
                case CommandTaskType.ReserveWait:
                    return new TacticalSopDecision(TacticalSopAuthority.Reserve, "reserve-held");
                case CommandTaskType.ReleaseReserve:
                    return new TacticalSopDecision(TacticalSopAuthority.Reserve, "reserve-release");
                case CommandTaskType.FallBackToLine:
                case CommandTaskType.Delay:
                    return new TacticalSopDecision(TacticalSopAuthority.Fallback, "fallback");
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                    return new TacticalSopDecision(TacticalSopAuthority.Hold, "hold");
                case CommandTaskType.FormUp:
                case CommandTaskType.AdvanceToAssembly:
                    return new TacticalSopDecision(TacticalSopAuthority.Defend, "form-before-commit");
                case CommandTaskType.Consolidate:
                    return new TacticalSopDecision(TacticalSopAuthority.Consolidate, "consolidate");
            }

            if (task == CommandTaskType.FixEnemy || task == CommandTaskType.SupportAttack)
            {
                return new TacticalSopDecision(TacticalSopAuthority.Attack, task == CommandTaskType.FixEnemy ? "fix-main-line" : "support-main-effort");
            }

            if (task == CommandTaskType.AttackObjective)
            {
                if (!objective.MainLineExposed || objective.Confidence01 < 0.60f)
                {
                    return new TacticalSopDecision(TacticalSopAuthority.Probe, "attack-downgraded-to-probe");
                }

                if (phase == TacticalOperationPhase.Committed &&
                    objective.Confidence01 >= 0.75f &&
                    odds >= 1.65f &&
                    reserve >= 0.10f)
                {
                    return new TacticalSopDecision(TacticalSopAuthority.Assault, "decisive-assault");
                }

                bool needsSupport = reserve < 0.10f || odds < 1.50f;
                return new TacticalSopDecision(TacticalSopAuthority.Attack, needsSupport ? "attack-support-required" : "attack-authorized");
            }

            return TacticalSopDecision.ForAssignedTask(role, task);
        }

        private static float ResolveOdds(float ownStrength, float enemyStrength)
        {
            if (!IsFinite(ownStrength) || ownStrength < 0f) ownStrength = 0f;
            if (!IsFinite(enemyStrength) || enemyStrength < 0f) enemyStrength = 0f;
            return ownStrength / Math.Max(1f, enemyStrength);
        }

        private static float Clamp01(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
