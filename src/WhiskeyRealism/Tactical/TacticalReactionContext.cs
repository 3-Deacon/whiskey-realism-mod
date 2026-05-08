using System.Collections.Generic;

namespace WhiskeyRealism.Tactical
{
    public sealed class TacticalReactionContext
    {
        public static readonly TacticalReactionContext Shared = new TacticalReactionContext();

        private readonly Dictionary<int, TacticalLocalReactionDecision> reactions =
            new Dictionary<int, TacticalLocalReactionDecision>();

        private readonly Dictionary<int, TacticalReserveIntentDecision> reserveIntents =
            new Dictionary<int, TacticalReserveIntentDecision>();

        public void SetReaction(int groupInstanceId, TacticalLocalReactionDecision decision)
        {
            reactions[groupInstanceId] = decision;
        }

        public TacticalLocalReactionDecision GetReaction(int groupInstanceId)
        {
            return reactions.TryGetValue(groupInstanceId, out TacticalLocalReactionDecision decision)
                ? decision
                : new TacticalLocalReactionDecision(LocalReaction.MaintainLine, false, 0f, "no-decision");
        }

        public void SetReserveIntent(int side, TacticalReserveIntentDecision decision)
        {
            reserveIntents[side] = decision;
        }

        public TacticalReserveIntentDecision GetReserveIntent(int side)
        {
            return reserveIntents.TryGetValue(side, out TacticalReserveIntentDecision decision)
                ? decision
                : new TacticalReserveIntentDecision(TacticalReserveIntent.None, false, 0f, "no-decision");
        }

        public void Clear()
        {
            reactions.Clear();
            reserveIntents.Clear();
        }
    }
}
