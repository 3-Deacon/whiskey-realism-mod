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
            bool allowsMajorAttack,
            bool requiresSupportBeforeMajorAttack,
            bool requiresFallbackIfPressed,
            float riskBudget01,
            float reacquireSeconds,
            string reason)
        {
            Authority = authority;
            AllowsMajorAttack = allowsMajorAttack;
            RequiresSupportBeforeMajorAttack = requiresSupportBeforeMajorAttack;
            RequiresFallbackIfPressed = requiresFallbackIfPressed;
            RiskBudget01 = Clamp01(riskBudget01);
            ReacquireSeconds = SanitizePositive(reacquireSeconds, 90f);
            Reason = string.IsNullOrWhiteSpace(reason) ? "sop-unspecified" : reason.Trim();
        }

        public TacticalSopAuthority Authority { get; }
        public bool AllowsMajorAttack { get; }
        public bool RequiresSupportBeforeMajorAttack { get; }
        public bool RequiresFallbackIfPressed { get; }
        public float RiskBudget01 { get; }
        public float ReacquireSeconds { get; }
        public string Reason { get; }

        public static TacticalSopDecision None =>
            new TacticalSopDecision(TacticalSopAuthority.None, false, false, false, 0f, 120f, "sop-none");

        internal static TacticalSopDecision ForAssignedTask(CommandNodeRole role, CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.Scout:
                    return new TacticalSopDecision(TacticalSopAuthority.Scout, false, false, true, 0.20f, 45f, "task-scout");
                case CommandTaskType.Probe:
                    return new TacticalSopDecision(TacticalSopAuthority.Probe, false, false, true, 0.30f, 45f, "task-probe");
                case CommandTaskType.Screen:
                case CommandTaskType.GuardFlank:
                    return new TacticalSopDecision(TacticalSopAuthority.Screen, false, false, true, 0.25f, 60f, "task-screen");
                case CommandTaskType.AttackObjective:
                    return new TacticalSopDecision(TacticalSopAuthority.Attack, true, false, false, 0.65f, 45f, "task-attack");
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                    return new TacticalSopDecision(TacticalSopAuthority.Attack, true, false, true, 0.50f, 45f, "task-support");
                case CommandTaskType.FallBackToLine:
                case CommandTaskType.Delay:
                    return new TacticalSopDecision(TacticalSopAuthority.Fallback, false, false, true, 0.20f, 60f, "task-fallback");
                case CommandTaskType.ReserveWait:
                case CommandTaskType.ReleaseReserve:
                    return new TacticalSopDecision(TacticalSopAuthority.Reserve, false, false, false, 0.15f, 90f, "task-reserve");
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                    return new TacticalSopDecision(TacticalSopAuthority.Hold, false, false, true, 0.25f, 90f, "task-hold");
                case CommandTaskType.Consolidate:
                    return new TacticalSopDecision(TacticalSopAuthority.Consolidate, false, false, false, 0.20f, 120f, "task-consolidate");
                case CommandTaskType.FormUp:
                case CommandTaskType.AdvanceToAssembly:
                    return new TacticalSopDecision(TacticalSopAuthority.Defend, false, false, false, 0.20f, 90f, "task-form");
                default:
                    return role == CommandNodeRole.Unknown
                        ? None
                        : new TacticalSopDecision(TacticalSopAuthority.Stay, false, false, false, 0.10f, 120f, "task-stay");
            }
        }

        private static float Clamp01(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static float SanitizePositive(float value, float fallback)
        {
            if (!IsFinite(value) || value <= 0f) return fallback;
            return value;
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
                    return new TacticalSopDecision(TacticalSopAuthority.Scout, false, false, true, 0.20f, 45f, "scout-contact");
                case CommandTaskType.Probe:
                    return new TacticalSopDecision(TacticalSopAuthority.Probe, false, false, true, 0.30f, 45f, "probe-contact");
                case CommandTaskType.Screen:
                case CommandTaskType.GuardFlank:
                    return new TacticalSopDecision(TacticalSopAuthority.Screen, false, false, true, 0.25f, 60f, "screen-contact");
                case CommandTaskType.ReserveWait:
                    return new TacticalSopDecision(TacticalSopAuthority.Reserve, false, false, false, 0.15f, 90f, "reserve-held");
                case CommandTaskType.ReleaseReserve:
                    return new TacticalSopDecision(TacticalSopAuthority.Reserve, false, false, true, 0.35f, 45f, "reserve-release");
                case CommandTaskType.FallBackToLine:
                case CommandTaskType.Delay:
                    return new TacticalSopDecision(TacticalSopAuthority.Fallback, false, false, true, 0.20f, 60f, "fallback");
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                    return new TacticalSopDecision(TacticalSopAuthority.Hold, false, false, true, 0.25f, 90f, "hold");
                case CommandTaskType.FormUp:
                case CommandTaskType.AdvanceToAssembly:
                    return new TacticalSopDecision(TacticalSopAuthority.Defend, false, false, false, 0.20f, 90f, "form-before-commit");
                case CommandTaskType.Consolidate:
                    return new TacticalSopDecision(TacticalSopAuthority.Consolidate, false, false, false, 0.20f, 120f, "consolidate");
            }

            if (task == CommandTaskType.FixEnemy || task == CommandTaskType.SupportAttack)
            {
                return new TacticalSopDecision(
                    TacticalSopAuthority.Attack,
                    objective.MainLineExposed,
                    false,
                    true,
                    0.50f,
                    45f,
                    task == CommandTaskType.FixEnemy ? "fix-main-line" : "support-main-effort");
            }

            if (task == CommandTaskType.AttackObjective)
            {
                if (!objective.MainLineExposed || objective.Confidence01 < 0.60f)
                {
                    return new TacticalSopDecision(TacticalSopAuthority.Probe, false, false, true, 0.30f, 45f, "attack-downgraded-to-probe");
                }

                if (phase == TacticalOperationPhase.Committed &&
                    objective.Confidence01 >= 0.75f &&
                    odds >= 1.65f &&
                    reserve >= 0.10f)
                {
                    return new TacticalSopDecision(TacticalSopAuthority.Assault, true, false, false, 0.75f, 30f, "decisive-assault");
                }

                bool needsSupport = reserve < 0.10f || odds < 1.50f;
                return new TacticalSopDecision(
                    TacticalSopAuthority.Attack,
                    !needsSupport,
                    needsSupport,
                    needsSupport,
                    needsSupport ? 0.45f : 0.65f,
                    45f,
                    needsSupport ? "attack-support-required" : "attack-authorized");
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
