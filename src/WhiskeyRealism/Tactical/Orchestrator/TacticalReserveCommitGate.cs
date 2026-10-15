using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // #59 reserve commitment gate (decision half of BattleReserveCommitGatePatch on
    // vanilla AIBattle.CheckUseOfReserves). A Reserve-role group is held back unless
    // the orchestrator released it (T15, 2026-10-14): either its role is no longer
    // Reserve, or the resolved role still says Reserve but the direct-child allocator
    // (T1: Exploit start, reserve-commit trigger odds, neighbouring-sector crisis) has
    // already re-roled it and the operations ledger has not caught up
    // (ReleasedByAllocator). Released reserves let vanilla's own reserve movement
    // through, like any other committed role.
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
                float localOdds,
                bool reserveReleased = false)
            {
                VanillaCommitted = vanillaCommitted;
                Resolution = resolution;
                PlayerControlled = playerControlled;
                CommittedUnitAlreadyEngaged = committedUnitAlreadyEngaged;
                OwnStrengthRatio = SanitizeRatio(ownStrengthRatio, 1f);
                LocalOdds = SanitizeRatio(localOdds, 1f);
                ReserveReleased = reserveReleased;
            }

            public bool VanillaCommitted { get; }
            public CommandIntentResolution Resolution { get; }
            public bool PlayerControlled { get; }
            public bool CommittedUnitAlreadyEngaged { get; }
            public float OwnStrengthRatio { get; }
            public float LocalOdds { get; }
            public bool ReserveReleased { get; }
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
                    return input.ReserveReleased
                        ? Allow("reserve-released", role)
                        : new Decision(Action.Deny, "role-reserve-hold", role);
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

        /// <summary>
        /// The reserve-release producer: <paramref name="held"/> (the resolution the
        /// gate acts on, ledger state first) still says Reserve while
        /// <paramref name="allocator"/> (the command-node intent the direct-child
        /// allocator produced) has re-roled the group to a committed role.
        /// </summary>
        public static bool ReleasedByAllocator(CommandIntentResolution held, CommandIntentResolution allocator)
        {
            if (!held.Found || held.Intent.Role != DirectChildRole.Reserve) return false;
            if (!allocator.Found) return false;
            DirectChildRole role = allocator.Intent.Role;
            return role != DirectChildRole.Reserve && role != DirectChildRole.Unknown;
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
