using System;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalOrderDelivery
    {
        Unknown = 0,
        Immediate = 1,
        Bugle = 2,
        Courier = 3
    }

    public enum TacticalOrderFrictionState
    {
        Immediate = 0,
        Bugle = 1,
        Courier = 2,
        Pending = 3,
        Delivered = 4,
        Stale = 5,
        Failed = 6
    }

    public readonly struct TacticalOrderFrictionInput
    {
        public TacticalOrderFrictionInput(
            bool orderDelayEnabled,
            bool queueProcessing,
            float queueDelayHours,
            TacticalOrderDelivery delivery,
            float deliveryProcessHours,
            bool courierMissing,
            int orderState,
            int intendedPathId,
            int transmittedPathId,
            bool contactChangedMaterially,
            float commanderInitiative01)
        {
            OrderDelayEnabled = orderDelayEnabled;
            QueueProcessing = queueProcessing;
            QueueDelayHours = SanitizeDelay(queueDelayHours);
            Delivery = delivery;
            DeliveryProcessHours = SanitizeDelay(deliveryProcessHours);
            CourierMissing = courierMissing;
            OrderState = orderState;
            IntendedPathId = intendedPathId;
            TransmittedPathId = transmittedPathId;
            ContactChangedMaterially = contactChangedMaterially;
            CommanderInitiative01 = SanitizeInitiative(commanderInitiative01);
        }

        public bool OrderDelayEnabled { get; }
        public bool QueueProcessing { get; }
        public float QueueDelayHours { get; }
        public TacticalOrderDelivery Delivery { get; }
        public float DeliveryProcessHours { get; }
        public bool CourierMissing { get; }
        public int OrderState { get; }
        public int IntendedPathId { get; }
        public int TransmittedPathId { get; }
        public bool ContactChangedMaterially { get; }
        public float CommanderInitiative01 { get; }

        private static float SanitizeDelay(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, value);
        }

        private static float SanitizeInitiative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0.5f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    public readonly struct TacticalOrderFrictionDecision
    {
        public TacticalOrderFrictionDecision(
            TacticalOrderFrictionState state,
            bool isDelayed,
            bool isDelivered,
            bool transmittedPathDiffers,
            float delayPressure,
            string reason)
        {
            State = state;
            IsDelayed = isDelayed;
            IsDelivered = isDelivered;
            TransmittedPathDiffers = transmittedPathDiffers;
            DelayPressure = delayPressure;
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        public TacticalOrderFrictionState State { get; }
        public bool IsDelayed { get; }
        public bool IsDelivered { get; }
        public bool TransmittedPathDiffers { get; }
        public float DelayPressure { get; }
        public string Reason { get; }
    }

    public static class TacticalOrderFriction
    {
        public static TacticalOrderFrictionDecision Evaluate(TacticalOrderFrictionInput input)
        {
            bool pathLag = input.IntendedPathId != input.TransmittedPathId;

            if (input.CourierMissing || input.OrderState == 3)
                return Delayed(TacticalOrderFrictionState.Failed, input, pathLag, "failed");

            if (!input.OrderDelayEnabled)
                return new TacticalOrderFrictionDecision(
                    TacticalOrderFrictionState.Immediate,
                    isDelayed: false,
                    isDelivered: true,
                    transmittedPathDiffers: pathLag,
                    delayPressure: 0f,
                    reason: "delay-disabled");

            if (input.ContactChangedMaterially && (pathLag || input.OrderState == 1))
                return Delayed(TacticalOrderFrictionState.Stale, input, pathLag, "contact-changed");

            if (input.OrderState == 2 && !pathLag)
                return new TacticalOrderFrictionDecision(
                    TacticalOrderFrictionState.Delivered,
                    isDelayed: false,
                    isDelivered: true,
                    transmittedPathDiffers: false,
                    delayPressure: 0f,
                    reason: "delivered");

            if (pathLag)
                return Delayed(TacticalOrderFrictionState.Pending, input, true, "path-lag");

            if (input.Delivery == TacticalOrderDelivery.Courier)
                return Delayed(TacticalOrderFrictionState.Courier, input, pathLag, "courier");

            if (input.Delivery == TacticalOrderDelivery.Bugle && HasQueueOrProcessDelay(input))
                return Delayed(TacticalOrderFrictionState.Bugle, input, pathLag, "bugle-delay");

            return new TacticalOrderFrictionDecision(
                TacticalOrderFrictionState.Immediate,
                isDelayed: false,
                isDelivered: true,
                transmittedPathDiffers: pathLag,
                delayPressure: 0f,
                reason: "immediate");
        }

        private static TacticalOrderFrictionDecision Delayed(
            TacticalOrderFrictionState state,
            TacticalOrderFrictionInput input,
            bool transmittedPathDiffers,
            string reason)
        {
            return new TacticalOrderFrictionDecision(
                state,
                isDelayed: true,
                isDelivered: false,
                transmittedPathDiffers: transmittedPathDiffers,
                delayPressure: DelayPressure(input),
                reason: reason);
        }

        private static bool HasQueueOrProcessDelay(TacticalOrderFrictionInput input)
        {
            return (input.QueueProcessing && input.QueueDelayHours > 0f) || input.DeliveryProcessHours > 0f;
        }

        private static float DelayPressure(TacticalOrderFrictionInput input)
        {
            float queueHours = input.QueueProcessing ? input.QueueDelayHours : 0f;
            float deliveryHours = Math.Min(input.DeliveryProcessHours, 6f);
            float rawPressure = Math.Max(0.05f, queueHours + deliveryHours);
            float initiativeMultiplier = 1.15f - (input.CommanderInitiative01 * 0.60f);
            return rawPressure * initiativeMultiplier;
        }
    }
}
