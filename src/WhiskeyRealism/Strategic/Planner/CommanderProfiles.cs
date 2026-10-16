using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // Personality as a modifier on live scoring (spec component 3, C2), from the live vanilla
    // commander record: GetCommanderInitiative/Leadership/Administration (61134-61251, experience
    // multiplier and parent influence included). Vanilla has no aggression or trait field
    // (58926-59026). Initiative is vanilla's own offensive term (the 14249-14250 dominance factor
    // 1 + (1 - initiative) x commanderinfluenceinitiativeforoperations), so a bold commander
    // accepts worse odds; leadership tempers the attack bias; administration widens supply reach.
    // Cunning has no planner output (vanilla's campaign AI uses it only to burn depots, 17381).
    // G3's TheaterPlanner reads the profile of each sector's commander through ForSector.
    public static class CommanderProfiles
    {
        // Initiative 0.5, no attack bias, no extra supply slack: what an unknown commander implies.
        public static CommanderProfile Neutral => new CommanderProfile();

        public static CommanderProfile From(in CommanderRecord c)
        {
            return new CommanderProfile
            {
                RequiredOddsShift = (c.Initiative - 0.5f) * -0.5f,
                AttackBias = Math.Max(-1f, Math.Min(1f, c.Initiative - c.Leadership * 0.5f)),
                SupplySlack = c.Administration * 0.25f
            };
        }

        public static CommanderProfile ForSector(IReadOnlyList<CommanderRecord> cs, int commanderId)
        {
            if (cs == null || commanderId < 0) return Neutral;
            for (int i = 0; i < cs.Count; i++)
                if (cs[i].Id == commanderId) return From(cs[i]);
            return Neutral;
        }
    }
}
