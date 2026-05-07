using System;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalOrderScope
    {
        Unknown = 0,
        DirectUnitAction = 1,
        SubcommandAction = 2,
        BlockDirectRegimentRetask = 3
    }

    public readonly struct TacticalCommandScopeDecision
    {
        public TacticalCommandScopeDecision(TacticalOrderScope scope, string reason)
        {
            Scope = scope;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim();
        }

        public TacticalOrderScope Scope { get; }
        public string Reason { get; }
    }

    public readonly struct TacticalCommandSummary
    {
        public TacticalCommandSummary(
            TacticalCommandTier sourceTier,
            TacticalCommandTier targetTier,
            TacticalOrderScope scope,
            TacticalOrderFrictionState friction,
            bool playerChain,
            bool localInitiativeAllowed,
            float delayPressure,
            string reason)
        {
            SourceTier = sourceTier;
            TargetTier = targetTier;
            Scope = scope;
            Friction = friction;
            PlayerChain = playerChain;
            LocalInitiativeAllowed = localInitiativeAllowed;
            DelayPressure = SanitizeDelayPressure(delayPressure);
            Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim();
        }

        public TacticalCommandTier SourceTier { get; }
        public TacticalCommandTier TargetTier { get; }
        public TacticalOrderScope Scope { get; }
        public TacticalOrderFrictionState Friction { get; }
        public bool PlayerChain { get; }
        public bool LocalInitiativeAllowed { get; }
        public float DelayPressure { get; }
        public string Reason { get; }

        public string Signature()
        {
            return "src=" + SourceTier
                + "|tgt=" + TargetTier
                + "|scope=" + Scope
                + "|friction=" + Friction
                + "|player=" + BoolBucket(PlayerChain)
                + "|local=" + BoolBucket(LocalInitiativeAllowed)
                + "|delay=" + DelayBucket(DelayPressure)
                + "|reason=" + Reason;
        }

        private static string BoolBucket(bool value)
        {
            return value ? "1" : "0";
        }

        private static string DelayBucket(float value)
        {
            if (value <= 0f) return "0";
            if (value < 0.5f) return "lt0.5";
            if (value < 1f) return "lt1";
            if (value < 2f) return "lt2";
            if (value < 4f) return "lt4";
            return "gte4";
        }

        private static float SanitizeDelayPressure(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, value);
        }
    }

    public static class TacticalCommandLedger
    {
        public static TacticalCommandScopeDecision DecideOrderScope(
            TacticalCommanderProfile source,
            TacticalCommanderProfile target)
        {
            if ((source.Tier == TacticalCommandTier.Army || source.Tier == TacticalCommandTier.Corps)
                && target.Tier == TacticalCommandTier.Regiment)
            {
                return new TacticalCommandScopeDecision(
                    TacticalOrderScope.BlockDirectRegimentRetask,
                    "army-corps-intent-must-flow-through-subcommand");
            }

            if (source.Tier == TacticalCommandTier.Division && target.Tier == TacticalCommandTier.Brigade)
            {
                return new TacticalCommandScopeDecision(
                    TacticalOrderScope.SubcommandAction,
                    "division-to-brigade");
            }

            if (source.Tier == TacticalCommandTier.Brigade && target.Tier == TacticalCommandTier.Regiment)
            {
                return new TacticalCommandScopeDecision(
                    TacticalOrderScope.DirectUnitAction,
                    "brigade-to-regiment");
            }

            return new TacticalCommandScopeDecision(
                TacticalOrderScope.DirectUnitAction,
                "local-or-unknown");
        }

        public static TacticalCommandSummary Summarize(
            TacticalCommanderProfile source,
            TacticalCommanderProfile target,
            TacticalOrderFrictionDecision friction)
        {
            var scope = DecideOrderScope(source, target);
            bool playerChain = source.UnderPlayerCommander || target.UnderPlayerCommander;
            bool localInitiativeAllowed = target.Initiative01 >= 0.65f
                && friction.State != TacticalOrderFrictionState.Immediate;

            return new TacticalCommandSummary(
                source.Tier,
                target.Tier,
                scope.Scope,
                friction.State,
                playerChain,
                localInitiativeAllowed,
                friction.DelayPressure,
                scope.Reason);
        }
    }
}
