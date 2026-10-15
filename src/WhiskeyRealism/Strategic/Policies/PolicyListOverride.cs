using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Policies
{
    // What PolicySelectionPatch does in one vanilla Policies.CheckAIPolicyChange call
    // (decompile 211017-211098), decided here so the order is covered by the harness.
    public enum PolicyCallAction
    {
        Vanilla,                // leave vanilla's call and list untouched
        ProtectPlayerResearch,  // emergency, player's alliance: append in-progress ids (BUG-WL-003)
        Pick,                   // Whiskey started a research; vanilla is skipped
        HoldFallback,           // vanilla's fallback walk runs without the held irreversible acts
    }

    // The facts the decision reads, named so a call site cannot swap them.
    public struct PolicyCallFacts
    {
        public bool Emergency;          // GetAIPersonality returned an emergency personality
        public bool PlayerAlliance;     // the call is for GameVars.playeralliance
        public bool ResearchInProgress; // anything below progress 1 (211061)
        public bool SelectionEnabled;   // Enable Need-Driven Policy Selection
        public bool HasNeed;            // the daily review has a need vector for the alliance
    }

    // Temporary edits PolicySelectionPatch makes to the personality list vanilla walks, for
    // the duration of one call. Each edit takes its snapshot and publishes it through the out
    // parameter (the patch passes Harmony's __state) before touching the list; the Finalizer
    // puts the exact snapshot back (Restore).
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
        // Vanilla's personality list and its contents before this call's edit.
        public sealed class Snapshot
        {
            public readonly List<int> List;
            public readonly int[] Original;

            internal Snapshot(List<int> list)
            {
                List = list;
                Original = list.ToArray();
            }
        }

        // The call's action, in vanilla's own order: the emergency branch first (vanilla never
        // filtered there, and for an AI alliance it stays vanilla's), then the one-at-a-time
        // return while anything is in research (211061), then the flag. findPick is asked only
        // past those gates; startPick reports whether vanilla's AddResearch took the pick. With
        // no started pick, the fallback walk is held only once the review has a need vector.
        // Missing delegates: vanilla.
        public static PolicyCallAction Decide(PolicyCallFacts facts, Func<int> findPick, Func<int, bool> startPick)
        {
            if (facts.Emergency)
                return facts.PlayerAlliance ? PolicyCallAction.ProtectPlayerResearch : PolicyCallAction.Vanilla;
            if (facts.ResearchInProgress || !facts.SelectionEnabled) return PolicyCallAction.Vanilla;
            if (findPick == null || startPick == null) return PolicyCallAction.Vanilla;
            int pick = findPick();
            if (pick >= 0 && startPick(pick)) return PolicyCallAction.Pick;
            return facts.HasNeed ? PolicyCallAction.HoldFallback : PolicyCallAction.Vanilla;
        }

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

        // Held irreversible acts (PolicyNeedScorer.IsHeld under the review's need vector) on
        // the list vanilla's fallback would walk, in list order, each once. Decide gates the
        // call (never on an emergency, never with selection off or no need vector).
        public static List<int> HeldIdsToRemove(NeedVector need, IList<int> personalityPolicies)
        {
            var result = new List<int>();
            if (need == null || personalityPolicies == null) return result;
            for (int i = 0; i < personalityPolicies.Count; i++)
            {
                int id = personalityPolicies[i];
                if (PolicyNeedScorer.IsHeld(id, need) && !result.Contains(id)) result.Add(id);
            }
            return result;
        }

        // Appends ids; snapshot published first. Nothing to append: no snapshot, list untouched.
        public static void Append(List<int> list, IList<int> ids, out Snapshot snapshot)
        {
            snapshot = null;
            if (list == null || ids == null || ids.Count == 0) return;
            snapshot = new Snapshot(list);
            list.AddRange(ids);
        }

        // Removes every occurrence of ids, keeping the order of the rest; snapshot published
        // first. Nothing to remove: no snapshot, list untouched.
        public static void Remove(List<int> list, IList<int> ids, out Snapshot snapshot)
        {
            snapshot = null;
            if (list == null || ids == null || ids.Count == 0) return;
            snapshot = new Snapshot(list);
            list.RemoveAll(ids.Contains);
        }

        // Finalizer side: puts back exactly the snapshot (Clear + AddRange, contents and
        // order). Returns the restored length, or -1 without a snapshot (vanilla's list is
        // never cleared blind). Idempotent.
        public static int Restore(Snapshot snapshot)
        {
            if (snapshot == null || snapshot.List == null) return -1;
            snapshot.List.Clear();
            snapshot.List.AddRange(snapshot.Original);
            return snapshot.Original.Length;
        }
    }
}
