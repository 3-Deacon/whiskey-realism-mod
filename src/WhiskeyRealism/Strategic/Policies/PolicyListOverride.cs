using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Policies
{
    // Temporary edits PolicySelectionPatch makes to the personality list vanilla
    // Policies.CheckAIPolicyChange walks (decompile 211017-211098), for the duration of one
    // call. The patch snapshots the list before the edit and the Finalizer puts the exact
    // snapshot back (Restore), whichever edit was made.
    //
    //  - Emergency, player's alliance: vanilla removes every in-progress policy whose id is
    //    not on the emergency list (211065-211074). For the player's alliance that cancels
    //    research the player chose — in a W&L career a prestige purchase
    //    (Policy.CheckSelections 211350-211362) the player cannot cancel themselves (211368).
    //    ProtectedIds are appended; they cannot be picked (211082 skips activatedpolicies
    //    members), so the only effect is that the removal spares them.
    //  - Non-emergency fallback: when Whiskey started nothing, vanilla walks the list from the
    //    head (211076-211090), where irreversible acts sit (Union Historical 33/39/40, CSA
    //    139/140). HeldIdsToRemove takes the ones held below their need gate out of the walk.
    public static class PolicyListOverride
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

        // Held irreversible acts on the list vanilla's fallback would walk, in list order, each
        // once. need is the alliance's need vector from the daily review (the plan's input; null
        // before the first review, when vanilla stays untouched). Empty when selection is off
        // or on an emergency personality (vanilla's emergency list is never filtered).
        public static List<int> HeldIdsToRemove(bool selectionEnabled, bool emergency, NeedVector need, IList<int> personalityPolicies)
        {
            var result = new List<int>();
            if (!selectionEnabled || emergency || need == null || personalityPolicies == null) return result;
            for (int i = 0; i < personalityPolicies.Count; i++)
            {
                int id = personalityPolicies[i];
                if (PolicyNeedScorer.IsHeld(id, need) && !result.Contains(id)) result.Add(id);
            }
            return result;
        }

        // Removes every occurrence of ids, keeping the order of the rest.
        public static void Remove(List<int> list, IList<int> ids)
        {
            if (list == null || ids == null || ids.Count == 0) return;
            list.RemoveAll(ids.Contains);
        }

        // Finalizer side: puts back exactly the snapshot taken before the edit. No-op without
        // a snapshot (never clears vanilla's list blind); idempotent.
        public static void Restore(List<int> list, IList<int> snapshot)
        {
            if (list == null || snapshot == null) return;
            list.Clear();
            for (int i = 0; i < snapshot.Count; i++) list.Add(snapshot[i]);
        }
    }
}
