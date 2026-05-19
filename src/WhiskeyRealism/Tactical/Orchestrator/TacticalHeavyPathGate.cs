using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure, stateless heavy-path execution gate (Task 3).
    /// Decides Run vs Skip for side-wide heavy snapshot build using cheap signature + min/max battle-hour intervals.
    /// First tick always Run; signature/pending changes respect cycle floor; max-interval forces refresh.
    /// See implementation plan for full semantics.
    /// </summary>
    // ============================================================
    // URGENT RECOVERY SAFETY BOUNDARY (Task 8)
    // ============================================================
    // This gate (plus coordinator's cheap ExtractCurrentSignature + Decide) is the *sole* decider for when
    // heavy TacticalBattleRuntimeSnapshot is built. Urgent recovery paths (#61 + local formation/fallback fixes)
    // MUST NEVER call Build or force Run; they always operate on the *last published* snapshot (or Empty) + live vanilla.
    //
    // Gate callers in frequent path (DriveTickCycle, DriveDirectChildCycle, DriveOperationsLedger) do call
    // ExtractCurrentSignature (cheap, no heavy) + Decide on *every* tick to check for pending/max, but this does
    // not build heavy unless ShouldRun. Urgent #61 patch runs completely outside coordinator (vanilla AdjustGroupFormations
    // postfix) and does not consult the gate at all.
    //
    // When Decide returns Skip (stable-under-max, throttled-pending), urgent recovery still has the prior snapshot
    // for high-level doctrine/ledger + full live vanilla per-group reads (positions, pathinterrupted, groupsubordinatesmoving,
    // local contacts, recent formation/order state, etc. — see snapshot file boundary doc for full list).
    //
    // The gate Input uses only TacticalBattleStateSignature (coarse, cheap) + battle hours + pending flag.
    // No Regiment, no positions, no pathinterrupted per group, no local contacts — those belong exclusively to
    // the urgent recovery live-vanilla reads.
    //
    // Authoritative: plan Task 8 + Task 3/6/7 wiring.
    // ============================================================
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
                bool hasPendingChangeForSide,
                float currentRealtimeSeconds = 0f,
                float lastHeavyRealtimeSecondsForSide = 0f,
                float minRealtimeSeconds = 0f)
            {
                CurrentSignature = currentSignature;
                CurrentBattleHours = currentBattleHours;
                LastHeavyHoursForSide = lastHeavyHoursForSide;
                LastSignatureForSide = lastSignatureForSide;
                CycleHours = cycleHours <= 0f ? 0.003f : cycleHours;
                HasPendingChangeForSide = hasPendingChangeForSide;
                CurrentRealtimeSeconds = SanitizeNonNegative(currentRealtimeSeconds);
                LastHeavyRealtimeSecondsForSide = SanitizeNonNegative(lastHeavyRealtimeSecondsForSide);
                MinRealtimeSeconds = SanitizeNonNegative(minRealtimeSeconds);
            }

            public TacticalBattleStateSignature CurrentSignature { get; }
            public float CurrentBattleHours { get; }
            public float LastHeavyHoursForSide { get; }
            public TacticalBattleStateSignature LastSignatureForSide { get; }
            public float CycleHours { get; }
            public bool HasPendingChangeForSide { get; }
            public float CurrentRealtimeSeconds { get; }
            public float LastHeavyRealtimeSecondsForSide { get; }
            public float MinRealtimeSeconds { get; }
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
            if (IsUnderRealtimeFloor(input))
            {
                return new Decision(Action.Skip, "realtime-floor");
            }

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

        /// <summary>
        /// Scales the base cycle-hours by observed time compression so that
        /// at 5x/20x compression we don't force a heavy rebuild every couple
        /// real seconds. At 1x compression: returns baseCycle unchanged. At
        /// 20x: returns ~5x baseCycle (54 battle-seconds instead of 10.8).
        /// Capped at 20x to prevent runaway scaling on noisy ratios.
        ///
        /// Rationale: the existing realtime-floor (2s default) already throttles
        /// frequency in wallclock terms, but at high compression the cycle
        /// fires the moment the floor lifts because battle-hours elapsed
        /// dominates. Scaling stretches the battle-hours threshold to match
        /// the wallclock pacing the realtime-floor already enforces.
        /// </summary>
        public static float ScaleCycleForCompression(float baseCycleHours, float compressionRatio)
        {
            if (float.IsNaN(baseCycleHours) || float.IsInfinity(baseCycleHours) || baseCycleHours <= 0f)
                return 0.003f;  // safe default
            if (float.IsNaN(compressionRatio) || float.IsInfinity(compressionRatio) || compressionRatio < 1f)
                compressionRatio = 1f;
            if (compressionRatio > 20f) compressionRatio = 20f;
            // Scale factor: 1x -> 1, 5x -> 1.25, 10x -> 2.5, 20x -> 5.
            // Below 4x, the realtime-floor handles throttling alone; above,
            // we need to extend the battle-hours cycle proportionally.
            float scale = compressionRatio / 4f;
            if (scale < 1f) scale = 1f;
            return baseCycleHours * scale;
        }

        private static bool IsUnderRealtimeFloor(Input input)
        {
            float minRealtime = input.MinRealtimeSeconds;
            if (minRealtime <= 0f) return false;

            float lastRealtime = input.LastHeavyRealtimeSecondsForSide;
            if (lastRealtime <= 0f) return false;

            float nowRealtime = input.CurrentRealtimeSeconds;
            if (nowRealtime <= 0f) return false;

            float elapsedRealtime = nowRealtime - lastRealtime;
            if (elapsedRealtime < 0f) elapsedRealtime = 0f;
            return elapsedRealtime < minRealtime;
        }

        private static float SanitizeNonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value;
        }
    }
}
