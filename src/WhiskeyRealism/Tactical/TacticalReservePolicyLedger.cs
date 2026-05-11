using System;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalReserveIntent
    {
        None = 0,
        HoldReserve = 1,
        PrepareRelief = 2,
        RelieveBatteredLine = 3,
        FlankGuard = 4,
        ExploitWeakPoint = 5
    }

    public readonly struct TacticalReserveAvailability
    {
        public TacticalReserveAvailability(
            int reserveCount,
            bool hasFlankRisk,
            bool lastReserveIsFlankGuard,
            bool wlOwnershipSafe,
            bool stalenessActive)
        {
            ReserveCount = Math.Max(0, reserveCount);
            HasFlankRisk = hasFlankRisk;
            LastReserveIsFlankGuard = lastReserveIsFlankGuard;
            WlOwnershipSafe = wlOwnershipSafe;
            StalenessActive = stalenessActive;
        }

        public int ReserveCount { get; }
        public bool HasFlankRisk { get; }
        public bool LastReserveIsFlankGuard { get; }
        public bool WlOwnershipSafe { get; }
        public bool StalenessActive { get; }
    }

    public readonly struct TacticalReserveIntentInput
    {
        public TacticalReserveIntentInput(
            TacticalReservePolicy playbookPolicy,
            TacticalLocalReactionDecision[] reactions,
            TacticalReserveAvailability availability)
        {
            PlaybookPolicy = playbookPolicy;
            Reactions = reactions ?? Array.Empty<TacticalLocalReactionDecision>();
            Availability = availability;
        }

        public TacticalReservePolicy PlaybookPolicy { get; }
        public TacticalLocalReactionDecision[] Reactions { get; }
        public TacticalReserveAvailability Availability { get; }
    }

    public readonly struct TacticalReserveIntentDecision
    {
        public TacticalReserveIntentDecision(
            TacticalReserveIntent intent,
            bool allowsRuntimeMutation,
            float confidence,
            string reason)
        {
            Intent = intent;
            AllowsRuntimeMutation = allowsRuntimeMutation;
            Confidence = Clamp01(confidence);
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        public TacticalReserveIntent Intent { get; }
        public bool AllowsRuntimeMutation { get; }
        public float Confidence { get; }
        public string Reason { get; }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    public static class TacticalReservePolicyLedger
    {
        public static bool ShouldRemoveDeniedReserveMovement(
            bool doctrineDeny,
            int beforePaths,
            int afterPaths,
            int beforeQueueCount,
            int afterQueueCount,
            bool useOrderDelays)
        {
            if (!doctrineDeny) return false;

            int safeBeforePaths = Math.Max(0, beforePaths);
            int safeAfterPaths = Math.Max(0, afterPaths);
            int safeBeforeQueue = Math.Max(0, beforeQueueCount);
            int safeAfterQueue = Math.Max(0, afterQueueCount);

            return safeAfterPaths > safeBeforePaths &&
                safeAfterQueue <= safeBeforeQueue;
        }

        public static TacticalReserveIntentDecision Decide(TacticalReserveIntentInput input)
        {
            TacticalReserveAvailability availability = input.Availability;

            if (!availability.WlOwnershipSafe)
                return Decision(TacticalReserveIntent.HoldReserve, false, 0f, "wl-ownership-blocked");
            if (availability.StalenessActive)
                return Decision(TacticalReserveIntent.PrepareRelief, false, 0f, "stale-order");
            if (availability.ReserveCount <= 0)
                return Decision(TacticalReserveIntent.None, false, 0f, "no-reserve");
            if (availability.HasFlankRisk && availability.LastReserveIsFlankGuard)
                return Decision(TacticalReserveIntent.FlankGuard, false, 0.7f, "last-reserve-is-flank-guard");
            if (availability.HasFlankRisk && availability.ReserveCount >= 2)
                return Decision(TacticalReserveIntent.FlankGuard, true, 0.75f, "flank-guard");

            int reliefRequests = CountReliefRequests(input.Reactions);
            if (reliefRequests >= 2 &&
                (input.PlaybookPolicy == TacticalReservePolicy.PrepareRelief ||
                 input.PlaybookPolicy == TacticalReservePolicy.RelieveBatteredLine))
            {
                return Decision(TacticalReserveIntent.RelieveBatteredLine, true, 0.8f, "battered-line");
            }

            if (reliefRequests >= 1)
                return Decision(TacticalReserveIntent.PrepareRelief, false, 0.6f, "prepare-relief");
            if (input.PlaybookPolicy == TacticalReservePolicy.ExploitWeakPoint)
                return Decision(TacticalReserveIntent.ExploitWeakPoint, true, 0.7f, "exploit-weak-point");

            return Decision(TacticalReserveIntent.HoldReserve, false, 0.5f, "hold-reserve");
        }

        private static int CountReliefRequests(TacticalLocalReactionDecision[] reactions)
        {
            int count = 0;
            for (int i = 0; i < reactions.Length; i++)
            {
                if (reactions[i].ReliefRequested || reactions[i].Reaction == LocalReaction.LineReliefRequest)
                    count++;
            }
            return count;
        }

        private static TacticalReserveIntentDecision Decision(
            TacticalReserveIntent intent,
            bool allowsRuntimeMutation,
            float confidence,
            string reason)
        {
            return new TacticalReserveIntentDecision(intent, allowsRuntimeMutation, confidence, reason);
        }
    }
}
