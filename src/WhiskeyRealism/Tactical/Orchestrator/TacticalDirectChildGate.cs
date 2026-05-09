using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public readonly struct DirectChildGateDecision
    {
        public DirectChildGateDecision(bool allow, string reason, DirectChildRole role)
        {
            Allow = allow;
            Reason = reason ?? string.Empty;
            Role = role;
        }
        public bool Allow { get; }
        public string Reason { get; }
        public DirectChildRole Role { get; }
    }

    /// <summary>
    /// Pure decision helper consulted by BattleFeudActionGatePatch (#42) between
    /// the W&L decision and the SetWaypoint call. No Unity types.
    /// </summary>
    public static class TacticalDirectChildGate
    {
        public const float OnAxisToleranceRadians = (float)(Math.PI / 3.0); // ±60°
        public const float FixDistanceRatio = 0.7f;
        public const float FallbackAwayToleranceRadians = (float)(Math.PI / 2.0); // ±90°

        public readonly struct Input
        {
            public Input(
                bool gateEnabled,
                bool sideIsAi,
                DirectChildRole role,
                int axisSector,
                int primarySector,
                float groupBearingFromOriginRadians,
                float intendedTargetBearingFromOriginRadians,
                float intendedTargetDistanceFromGroup,
                float nearestEnemyBearingFromGroupRadians,
                float feudMaxDistance,
                int intendedTargetSector = -1)
            {
                GateEnabled = gateEnabled;
                SideIsAi = sideIsAi;
                Role = role;
                AxisSector = axisSector;
                PrimarySector = primarySector;
                GroupBearingFromOriginRadians = groupBearingFromOriginRadians;
                IntendedTargetBearingFromOriginRadians = intendedTargetBearingFromOriginRadians;
                IntendedTargetDistanceFromGroup = intendedTargetDistanceFromGroup;
                NearestEnemyBearingFromGroupRadians = nearestEnemyBearingFromGroupRadians;
                FeudMaxDistance = feudMaxDistance;
                IntendedTargetSector = intendedTargetSector < 0 ? primarySector : intendedTargetSector;
            }

            public bool GateEnabled { get; }
            public bool SideIsAi { get; }
            public DirectChildRole Role { get; }
            public int AxisSector { get; }
            public int PrimarySector { get; }
            public float GroupBearingFromOriginRadians { get; }
            public float IntendedTargetBearingFromOriginRadians { get; }
            public float IntendedTargetDistanceFromGroup { get; }
            public float NearestEnemyBearingFromGroupRadians { get; }
            public float FeudMaxDistance { get; }
            public int IntendedTargetSector { get; }

            public Input WithIntendedTargetSector(int sector) => new Input(
                GateEnabled, SideIsAi, Role, AxisSector, PrimarySector,
                GroupBearingFromOriginRadians, IntendedTargetBearingFromOriginRadians,
                IntendedTargetDistanceFromGroup, NearestEnemyBearingFromGroupRadians,
                FeudMaxDistance, sector);
        }

        public static DirectChildGateDecision Decide(Input input)
        {
            if (!input.GateEnabled)
                return new DirectChildGateDecision(true, "gate-disabled", input.Role);
            if (!input.SideIsAi)
                return new DirectChildGateDecision(true, "player-side", input.Role);

            switch (input.Role)
            {
                case DirectChildRole.Unknown:
                    return new DirectChildGateDecision(true, "role-unknown", input.Role);
                case DirectChildRole.Reserve:
                    return new DirectChildGateDecision(false, "reserve-not-committed", input.Role);
                case DirectChildRole.Main:
                case DirectChildRole.SupportMain:
                    return DecideAxis(input);
                case DirectChildRole.Fix:
                    return DecideFix(input);
                case DirectChildRole.Screen:
                    return DecideScreen(input);
                case DirectChildRole.Fallback:
                    return DecideFallback(input);
                case DirectChildRole.RefuseLeft:
                case DirectChildRole.RefuseRight:
                    return DecideRefuse(input);
                default:
                    return new DirectChildGateDecision(true, "role-unknown", input.Role);
            }
        }

        private static DirectChildGateDecision DecideAxis(Input input)
        {
            float deltaToTarget = AbsAngleDelta(input.IntendedTargetBearingFromOriginRadians, input.GroupBearingFromOriginRadians);
            return deltaToTarget <= OnAxisToleranceRadians
                ? new DirectChildGateDecision(true, "on-axis", input.Role)
                : new DirectChildGateDecision(false, "off-axis", input.Role);
        }

        private static DirectChildGateDecision DecideFix(Input input)
        {
            float threshold = input.FeudMaxDistance * FixDistanceRatio;
            return input.IntendedTargetDistanceFromGroup <= threshold
                ? new DirectChildGateDecision(true, "short-pressure", input.Role)
                : new DirectChildGateDecision(false, "fix-no-wide", input.Role);
        }

        private static DirectChildGateDecision DecideScreen(Input input)
        {
            return input.IntendedTargetSector == input.PrimarySector
                ? new DirectChildGateDecision(true, "in-sector", input.Role)
                : new DirectChildGateDecision(false, "screen-out-of-sector", input.Role);
        }

        private static DirectChildGateDecision DecideFallback(Input input)
        {
            // Allow when intended bearing is within ±90° of the *opposite* of the enemy bearing.
            float awayBearing = WrapPi(input.NearestEnemyBearingFromGroupRadians + (float)Math.PI);
            float delta = AbsAngleDelta(input.IntendedTargetBearingFromOriginRadians, awayBearing);
            return delta <= FallbackAwayToleranceRadians
                ? new DirectChildGateDecision(true, "withdraw-bearing", input.Role)
                : new DirectChildGateDecision(false, "fallback-not-withdraw", input.Role);
        }

        private static DirectChildGateDecision DecideRefuse(Input input)
        {
            return input.IntendedTargetSector == input.PrimarySector
                ? new DirectChildGateDecision(true, "in-flank-sector", input.Role)
                : new DirectChildGateDecision(false, "refuse-out-of-sector", input.Role);
        }

        private static float AbsAngleDelta(float a, float b)
        {
            float delta = WrapPi(a - b);
            return Math.Abs(delta);
        }

        private static float WrapPi(float v)
        {
            const float twoPi = (float)(2.0 * Math.PI);
            while (v > Math.PI) v -= twoPi;
            while (v < -Math.PI) v += twoPi;
            return v;
        }
    }
}
