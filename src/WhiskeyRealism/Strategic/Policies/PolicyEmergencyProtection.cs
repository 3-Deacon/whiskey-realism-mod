using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Policies
{
    // Vanilla Policies.CheckAIPolicyChange, on an emergency personality, removes every
    // in-progress policy whose id is not on the emergency list (decompile 211065-211074). For
    // the player's alliance that cancels research the player chose — in a W&L career a
    // prestige purchase (Policy.CheckSelections 211350-211362) the player cannot cancel
    // themselves (211368). PolicySelectionPatch appends these ids to the list for the
    // duration of the call; they cannot be picked (211082 skips activatedpolicies members),
    // so the only effect is that the removal spares them.
    public static class PolicyEmergencyProtection
    {
        // Ids still in research (progress < 1), not already on the list, each once.
        public static List<int> ProtectedIds(IList<int> researchIds, IList<float> progress, IList<int> personalityPolicies)
        {
            var result = new List<int>();
            if (researchIds == null || progress == null) return result;
            int count = Math.Min(researchIds.Count, progress.Count);
            for (int i = 0; i < count; i++)
            {
                int id = researchIds[i];
                if (progress[i] >= 1f) continue;
                if (personalityPolicies != null && personalityPolicies.Contains(id)) continue;
                if (result.Contains(id)) continue;
                result.Add(id);
            }
            return result;
        }
    }
}
