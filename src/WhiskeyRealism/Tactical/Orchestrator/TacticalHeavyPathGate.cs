using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure, stateless heavy-path execution gate (Task 3).
    /// See full docs and semantics in the implementation plan.
    /// This is the STUB version for initial TDD RED phase.
    /// </summary>
    public static class TacticalHeavyPathGate
    {
        public enum Action
        {
            Skip = 0,
            Run = 1
        }

        public readonly struct Input
        {
            public Input(
                TacticalBattleStateSignature currentSignature,
                float currentBattleHours,
                float lastHeavyHoursForSide,
                TacticalBattleStateSignature lastSignatureForSide,
                float cycleHours,
                bool hasPendingChangeForSide)
            {
                CurrentSignature = currentSignature;
                CurrentBattleHours = currentBattleHours;
                LastHeavyHoursForSide = lastHeavyHoursForSide;
                LastSignatureForSide = lastSignatureForSide;
                CycleHours = cycleHours <= 0f ? 0.003f : cycleHours;
                HasPendingChangeForSide = hasPendingChangeForSide;
            }

            public TacticalBattleStateSignature CurrentSignature { get; }
            public float CurrentBattleHours { get; }
            public float LastHeavyHoursForSide { get; }
            public TacticalBattleStateSignature LastSignatureForSide { get; }
            public float CycleHours { get; }
            public bool HasPendingChangeForSide { get; }
        }

        public readonly struct Decision
        {
            public Decision(Action action, string reason)
            {
                Action = action;
                Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
            }

            public Action Action { get; }
            public string Reason { get; }
            public bool ShouldRun => Action == Action.Run;
        }

        public static Decision Decide(Input input)
        {
            float last = input.LastHeavyHoursForSide;
            float now = input.CurrentBattleHours;
            float cycle = input.CycleHours;
            bool hasPending = input.HasPendingChangeForSide;
            var curr = input.CurrentSignature;
            var lastSig = input.LastSignatureForSide;

            // First tick: no prior heavy run (last <=0 or uninitialized) -> always execute
            if (last <= 0f || float.IsNaN(last) || float.IsInfinity(last))
            {
                return new Decision(Action.Run, "first-tick");
            }

            float elapsed = now - last;
            if (elapsed < 0f) elapsed = 0f;

            bool sigDiff = !curr.SignatureEquals(lastSig);
            bool effectiveChange = sigDiff || hasPending;

            // Signature change or pending: run only once min floor (cycleHours) passed.
            // Does not bypass floor; pending remembered by caller until then.
            if (effectiveChange && elapsed >= cycle)
            {
                string reason = sigDiff ? "signature-change" : "pending-change";
                return new Decision(Action.Run, reason);
            }

            // Hard max interval: even on identical signature (no pending), force refresh to prevent starvation.
            // cycleHours serves as both the responsiveness floor and the max-period guarantee.
            if (!effectiveChange && elapsed >= cycle)
            {
                return new Decision(Action.Run, "max-interval-force");
            }

            // Skip cases (cheap frequent path)
            if (effectiveChange)
            {
                return new Decision(Action.Skip, "throttled-pending");
            }

            return new Decision(Action.Skip, "stable-under-max");
        }
    }
}
