using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal static class TacticalReserveCommitGate
    {
        public enum Action
        {
            Observe = 0,
            Allow = 1,
            Deny = 2,
        }

        public readonly struct Input
        {
            public Input(
                bool vanillaCommitted,
                CommandIntentResolution resolution,
                bool playerControlled,
                bool committedUnitAlreadyEngaged,
                float ownStrengthRatio,
                float localOdds)
            {
                VanillaCommitted = vanillaCommitted;
                Resolution = resolution;
                PlayerControlled = playerControlled;
                CommittedUnitAlreadyEngaged = committedUnitAlreadyEngaged;
                OwnStrengthRatio = SanitizeRatio(ownStrengthRatio, 1f);
                LocalOdds = SanitizeRatio(localOdds, 1f);
            }

            public bool VanillaCommitted { get; }
            public CommandIntentResolution Resolution { get; }
            public bool PlayerControlled { get; }
            public bool CommittedUnitAlreadyEngaged { get; }
            public float OwnStrengthRatio { get; }
            public float LocalOdds { get; }
        }

        public readonly struct Decision
        {
            public Decision(Action action, string reason, DirectChildRole role)
            {
                Action = action;
                Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
                Role = role;
            }

            public Action Action { get; }
            public string Reason { get; }
            public DirectChildRole Role { get; }
        }

        public static Decision Decide(Input input)
        {
            if (!input.VanillaCommitted)
                return Observe("no-vanilla-commit", DirectChildRole.Unknown);
            if (input.PlayerControlled)
                return Observe("player-controlled", DirectChildRole.Unknown);
            if (!input.Resolution.Found)
                return Observe(input.Resolution.Reason, DirectChildRole.Unknown);

            DirectChildRole role = input.Resolution.Intent.Role;
            if (input.CommittedUnitAlreadyEngaged)
                return Allow("already-committed-contact", role);

            switch (role)
            {
                case DirectChildRole.Reserve:
                    return new Decision(Action.Deny, "role-reserve-hold", role);
                case DirectChildRole.Main:
                    return input.OwnStrengthRatio < 0.75f
                        ? Allow("main-understrength-release", role)
                        : Allow("main-vanilla-release", role);
                case DirectChildRole.Fallback:
                    return input.LocalOdds < 0.85f
                        ? Allow("fallback-screen-retreat", role)
                        : Allow("fallback-vanilla-release", role);
                case DirectChildRole.SupportMain:
                case DirectChildRole.Fix:
                case DirectChildRole.Screen:
                case DirectChildRole.RefuseLeft:
                case DirectChildRole.RefuseRight:
                    return Allow("role-vanilla-release", role);
                case DirectChildRole.Unknown:
                default:
                    return Observe("unknown-role", role);
            }
        }

        public static bool PermitReserveListBias(CommandIntentResolution resolution)
        {
            if (!resolution.Found) return true;
            switch (resolution.Intent.Role)
            {
                case DirectChildRole.Reserve:
                    return false;
                default:
                    return true;
            }
        }

        private static Decision Observe(string reason, DirectChildRole role) =>
            new Decision(Action.Observe, reason, role);

        private static Decision Allow(string reason, DirectChildRole role) =>
            new Decision(Action.Allow, reason, role);

        private static float SanitizeRatio(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            if (value < 0f) return 0f;
            if (value > 10f) return 10f;
            return value;
        }
    }
}
