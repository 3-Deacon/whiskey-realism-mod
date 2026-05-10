namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal static class TacticalOrchestratorChargeGate
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
                bool vanillaWouldCharge,
                bool chargeCancellation,
                CommandIntentResolution resolution,
                bool playerControlled,
                float localOdds,
                bool mainEffortSupportAvailable,
                bool screenRoutedTargetVisible)
            {
                VanillaWouldCharge = vanillaWouldCharge;
                ChargeCancellation = chargeCancellation;
                Resolution = resolution;
                PlayerControlled = playerControlled;
                LocalOdds = SanitizeLocalOdds(localOdds);
                MainEffortSupportAvailable = mainEffortSupportAvailable;
                ScreenRoutedTargetVisible = screenRoutedTargetVisible;
            }

            public bool VanillaWouldCharge { get; }
            public bool ChargeCancellation { get; }
            public CommandIntentResolution Resolution { get; }
            public bool PlayerControlled { get; }
            public float LocalOdds { get; }
            public bool MainEffortSupportAvailable { get; }
            public bool ScreenRoutedTargetVisible { get; }
        }

        public readonly struct Decision
        {
            public Decision(Action action, DirectChildRole role, string reason)
            {
                Action = action;
                Role = role;
                Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
            }

            public Action Action { get; }
            public DirectChildRole Role { get; }
            public string Reason { get; }
            public bool AllowsCharge => Action != TacticalOrchestratorChargeGate.Action.Deny;
        }

        public static Decision Decide(Input input)
        {
            if (!input.VanillaWouldCharge)
                return Observe(DirectChildRole.Unknown, "no-vanilla-charge");
            if (input.ChargeCancellation)
                return Allow(DirectChildRole.Unknown, "charge-cancellation");
            if (!input.Resolution.Found)
                return Allow(DirectChildRole.Unknown, "no-command-intent");
            if (input.PlayerControlled)
                return Observe(DirectChildRole.Unknown, "player-controlled");

            DirectChildRole role = input.Resolution.Intent.Role;
            switch (role)
            {
                case DirectChildRole.Main:
                    return input.LocalOdds >= 1.10f
                        ? Allow(role, "main-favorable-odds")
                        : Deny(role, "main-unfavorable-odds");
                case DirectChildRole.SupportMain:
                    return input.MainEffortSupportAvailable
                        ? Allow(role, "support-main-charge-support")
                        : Deny(role, "support-main-no-main-charge");
                case DirectChildRole.Fix:
                    return Deny(role, "role-fix-hold");
                case DirectChildRole.Reserve:
                    return Deny(role, "role-reserve-hold");
                case DirectChildRole.Fallback:
                    return Deny(role, "role-fallback-no-charge");
                case DirectChildRole.RefuseLeft:
                    return Deny(role, "role-refuse-left-no-charge");
                case DirectChildRole.RefuseRight:
                    return Deny(role, "role-refuse-right-no-charge");
                case DirectChildRole.Screen:
                    return input.ScreenRoutedTargetVisible
                        ? Allow(role, "screen-chase-routed-target")
                        : Deny(role, "screen-no-routed-target");
                case DirectChildRole.Unknown:
                default:
                    return Allow(role, "unknown-role");
            }
        }

        private static Decision Observe(DirectChildRole role, string reason) =>
            new Decision(Action.Observe, role, reason);

        private static Decision Allow(DirectChildRole role, string reason) =>
            new Decision(Action.Allow, role, reason);

        private static Decision Deny(DirectChildRole role, string reason) =>
            new Decision(Action.Deny, role, reason);

        private static float SanitizeLocalOdds(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 1f;
            return value < 0f ? 0f : value;
        }
    }
}
