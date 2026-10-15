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

    // Visible: the closest visible enemy. Entry (BC9): with none visible, a screen
    // faces the stalest unwatched enemy-side entry point (BattleRecon.TryEntryBearing),
    // where unseen enemies come from; never urgent.
    public enum TacticalFacingThreatSource
    {
        None,
        Visible,
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
            float toleranceDegrees,
            bool isArtillery = false,
            bool threatWithinFireRange = true)
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
            IsArtillery = isArtillery;
            ThreatWithinFireRange = threatWithinFireRange;
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
        public bool IsArtillery { get; }
        public bool ThreatWithinFireRange { get; }

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

    // R9 (2026-10-14): regiment-scope pulses mirror vanilla
    // UnlimberArtilleryAIMicro (3482-3494): only a unit that is not moving
    // (regimentpaths <= 0) turns, only toward an enemy within its fire range
    // (GetClosestEnemyUnit(firerange)), and the turn is RotateRegiment — no
    // formation change. Batteries are left to vanilla: formation 0 on a
    // limbered battery unlimbers it (ChangeRegimentFormation 135446). A
    // withdrawing unit is never stopped to face (RotateRegiment → StopRegiment).
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
            if (!CommandFormationCorrection.ShouldFaceThreat(input.Task) && !input.CloseEngaged && !input.FlankRisk &&
                !(input.ThreatSource == TacticalFacingThreatSource.Entry && WatchesEntry(input.Task)))
                return TacticalFacingPulseDecision.NoWrite(input.TargetFacingDegrees, "task-does-not-face-threat");
            if (input.Scope == TacticalFacingPulseScope.Regiment)
            {
                if (input.IsArtillery)
                    return TacticalFacingPulseDecision.NoWrite(input.TargetFacingDegrees, "artillery-vanilla");
                if (!input.ThreatWithinFireRange)
                    return TacticalFacingPulseDecision.NoWrite(input.TargetFacingDegrees, "threat-out-of-range");
                if (input.Task == CommandTaskType.FallBackToLine)
                    return TacticalFacingPulseDecision.NoWrite(input.TargetFacingDegrees, "withdrawing");
                if (input.ActiveMove)
                    return TacticalFacingPulseDecision.NoWrite(input.TargetFacingDegrees, "active-move");
            }
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

        // Tasks that watch ground rather than a seen enemy: a screen, a probe and a
        // flank guard face the unwatched entry point when nothing is visible.
        private static bool WatchesEntry(CommandTaskType task)
        {
            return task == CommandTaskType.Screen ||
                task == CommandTaskType.Probe ||
                task == CommandTaskType.GuardFlank;
        }
    }
}
