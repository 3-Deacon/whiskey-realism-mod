using System.Collections.Generic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical
{
    public sealed class TacticalReactionContext
    {
        static TacticalReactionContext()
        {
            BattleScope.Register("reaction-context", () => Shared.Clear());
        }

        public static readonly TacticalReactionContext Shared = new TacticalReactionContext();

        private readonly Dictionary<int, TacticalLocalReactionDecision> reactions =
            new Dictionary<int, TacticalLocalReactionDecision>();

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

        public void Clear()
        {
            reactions.Clear();
        }
    }
}
