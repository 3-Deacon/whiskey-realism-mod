using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalFacingPulseScope
    {
        Unknown,
        Division,
        Brigade,
        Regiment
    }

    public enum TacticalFacingThreatSource
    {
        None,
        Visible,
        RecentFire,
        LastKnown,
        Objective,
        Entry
    }

    public enum TacticalFacingPulseAction
    {
        NoWrite,
        ObserveAxis,
        CorrectFacing,
        CorrectFormationAndFacing
    }

    public readonly struct TacticalFacingPulseInput
    {
        public TacticalFacingPulseInput(
            TacticalFacingPulseScope scope,
            CommandTaskType task,
            int currentFormation,
            int targetFormation,
            float currentFacingDegrees,
            float targetFacingDegrees,
            bool hasThreatBearing,
            TacticalFacingThreatSource threatSource,
            bool closeEngaged,
            bool flankRisk,
            bool playerProtected,
            bool routed,
            bool pendingOrder,
            bool activeMove,
            bool recentPulse,
            float toleranceDegrees)
        {
            Scope = scope;
            Task = task;
            CurrentFormation = currentFormation;
            TargetFormation = targetFormation;
            CurrentFacingDegrees = NormalizeAngle(currentFacingDegrees);
            TargetFacingDegrees = NormalizeAngle(targetFacingDegrees);
            HasThreatBearing = hasThreatBearing;
            ThreatSource = threatSource;
            CloseEngaged = closeEngaged;
            FlankRisk = flankRisk;
            PlayerProtected = playerProtected;
            Routed = routed;
            PendingOrder = pendingOrder;
            ActiveMove = activeMove;
            RecentPulse = recentPulse;
            ToleranceDegrees = Math.Max(0f, Sanitize(toleranceDegrees));
        }

        public TacticalFacingPulseScope Scope { get; }
        public CommandTaskType Task { get; }
        public int CurrentFormation { get; }
        public int TargetFormation { get; }
        public float CurrentFacingDegrees { get; }
        public float TargetFacingDegrees { get; }
        public bool HasThreatBearing { get; }
        public TacticalFacingThreatSource ThreatSource { get; }
        public bool CloseEngaged { get; }
        public bool FlankRisk { get; }
        public bool PlayerProtected { get; }
        public bool Routed { get; }
        public bool PendingOrder { get; }
        public bool ActiveMove { get; }
        public bool RecentPulse { get; }
        public float ToleranceDegrees { get; }

        internal bool HasValidTargetFacing => HasThreatBearing && IsFinite(TargetFacingDegrees);

        private static float NormalizeAngle(float value)
        {
            value = Sanitize(value) % 360f;
            return value < 0f ? value + 360f : value;
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalFacingPulseDecision
    {
        public TacticalFacingPulseDecision(
            TacticalFacingPulseAction action,
            int targetFormation,
            float targetFacingDegrees,
            bool urgent,
            string reason)
        {
            Action = action;
            TargetFormation = targetFormation;
            TargetFacingDegrees = targetFacingDegrees;
            Urgent = urgent;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
        }

        public TacticalFacingPulseAction Action { get; }
        public int TargetFormation { get; }
        public float TargetFacingDegrees { get; }
        public bool Urgent { get; }
        public string Reason { get; }
        public bool ShouldWrite => Action == TacticalFacingPulseAction.CorrectFacing ||
            Action == TacticalFacingPulseAction.CorrectFormationAndFacing;

        public static TacticalFacingPulseDecision NoWrite(float facing, string reason)
        {
            return new TacticalFacingPulseDecision(TacticalFacingPulseAction.NoWrite, -1, facing, false, reason);
        }
    }

    public static class TacticalFacingPulseDoctrine
    {
        public static TacticalFacingPulseDecision Decide(TacticalFacingPulseInput input)
        {
            if (input.Scope == TacticalFacingPulseScope.Division)
            {
                return input.HasValidTargetFacing
                    ? new TacticalFacingPulseDecision(TacticalFacingPulseAction.ObserveAxis, input.TargetFormation, input.TargetFacingDegrees, false, "division-axis-intent")
                    : TacticalFacingPulseDecision.NoWrite(input.TargetFacingDegrees, "no-threat-bearing");
            }

            if (input.Scope == TacticalFacingPulseScope.Unknown)
                return TacticalFacingPulseDecision.NoWrite(input.TargetFacingDegrees, "unknown-scope");
            if (input.PlayerProtected)
                return TacticalFacingPulseDecision.NoWrite(input.TargetFacingDegrees, "player-protected");
            if (input.Routed)
                return TacticalFacingPulseDecision.NoWrite(input.TargetFacingDegrees, "routed");
            if (!input.HasValidTargetFacing || input.ThreatSource == TacticalFacingThreatSource.None)
                return TacticalFacingPulseDecision.NoWrite(input.TargetFacingDegrees, "no-threat-bearing");
            if (!CommandFormationCorrection.ShouldFaceThreat(input.Task) && !input.CloseEngaged && !input.FlankRisk)
                return TacticalFacingPulseDecision.NoWrite(input.TargetFacingDegrees, "task-does-not-face-threat");
            if (input.PendingOrder)
                return TacticalFacingPulseDecision.NoWrite(input.TargetFacingDegrees, "pending-order");
            if (input.ActiveMove && !input.FlankRisk)
                return TacticalFacingPulseDecision.NoWrite(input.TargetFacingDegrees, "active-move");
            if (input.RecentPulse)
                return TacticalFacingPulseDecision.NoWrite(input.TargetFacingDegrees, "recent-pulse");

            bool needsFacing = CommandFormationCorrection.NeedsFacingCorrection(
                input.CurrentFacingDegrees,
                input.TargetFacingDegrees,
                input.ToleranceDegrees);
            bool needsFormation = input.TargetFormation >= 0 &&
                input.CurrentFormation >= 0 &&
                input.CurrentFormation != input.TargetFormation;

            if (!needsFacing && !needsFormation)
                return TacticalFacingPulseDecision.NoWrite(input.TargetFacingDegrees, "already-facing");

            bool urgent = input.Scope == TacticalFacingPulseScope.Regiment &&
                (input.CloseEngaged || input.FlankRisk || input.ThreatSource == TacticalFacingThreatSource.Visible);
            if (needsFormation && needsFacing)
                return new TacticalFacingPulseDecision(TacticalFacingPulseAction.CorrectFormationAndFacing, input.TargetFormation, input.TargetFacingDegrees, urgent, "formation-and-facing");
            if (needsFormation)
                return new TacticalFacingPulseDecision(TacticalFacingPulseAction.CorrectFormationAndFacing, input.TargetFormation, input.TargetFacingDegrees, urgent, "formation");
            return new TacticalFacingPulseDecision(TacticalFacingPulseAction.CorrectFacing, input.TargetFormation, input.TargetFacingDegrees, urgent, "facing");
        }
    }
}
