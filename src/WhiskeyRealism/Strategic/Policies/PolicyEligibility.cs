namespace WhiskeyRealism.Strategic.Policies
{
    // One policy as PolicyRuntime reads it from vanilla for one alliance.
    public struct PolicyCandidateFacts
    {
        public int PolicyId;
        public bool IsGrand;              // member of Policy.grandpolicies
        public bool IsAct;
        public bool ActiveOrDone;         // in activatedpolicies (researching or done) or activatedacts
        public int ReferedChapter;
        public int CurrentChapter;        // Policy.CurrentChapter
        public bool HasPrePolicies;       // Policy.HasPrePolicies (211537)
        public bool Deactivated;          // Policies.IsDeactivated (210957); true when unreadable
        public bool Blocked;              // Policy.blocked
        public bool AvailableInScenario;  // Policy.IsAvailableInScenario
        public bool SlotFree;             // GetActivatedPolicies() < maxpolicies (CanBeActivated 209541)
        public float ResearchDuration;    // years
    }

    // Hard filters for a Whiskey policy pick: vanilla's own candidate test
    // (CheckAIPolicyChange 211082), plus a free slot for non-acts (AddResearch 209554
    // no-ops at the cap, which stalls vanilla on the same pick forever), no grand policy, and
    // no Militia Act I before the war (CheckForChapterUpdate 211606 starts the war with it on
    // 15 Apr 1861). Vanilla's candidate test lets the fallback walk pick it at chapter 0
    // (ReferedChapter 0 <= 0, 211082) when a personality lists it (Union Militaristic and
    // Abolitionist do), behind IsDeactivated's 4 Mar 1861 floor (210957-210964); the same rule
    // takes it out of that walk too (PolicyListOverride.HeldIdsToRemove) so the two paths agree.
    public static class PolicyEligibility
    {
        internal const int ScriptedWarStartPolicyId = 36;

        // Whiskey leaves the war start to vanilla's script while the chapter is 0 (or still -1).
        public static bool HoldsScriptedWarStart(int chapter) => chapter <= 0;

        public static bool IsScriptedWarStart(int policyId, int chapter) =>
            policyId == ScriptedWarStartPolicyId && HoldsScriptedWarStart(chapter);

        public static bool IsEligible(PolicyCandidateFacts f, int alliance, out string reason)
        {
            if (alliance < 0 || alliance > 1) { reason = "alliance"; return false; }
            if (PolicyEffectCatalog.AllianceOf(f.PolicyId) != alliance) { reason = "foreign"; return false; }
            if (f.IsGrand || f.ResearchDuration <= 0f) { reason = "grand"; return false; }
            if (f.ActiveOrDone) { reason = "active"; return false; }
            if (f.ReferedChapter > f.CurrentChapter) { reason = "chapter"; return false; }
            if (!f.HasPrePolicies) { reason = "prerequisites"; return false; }
            if (f.Deactivated) { reason = "deactivated"; return false; }
            if (f.Blocked) { reason = "blocked"; return false; }
            if (!f.AvailableInScenario) { reason = "scenario"; return false; }
            if (!f.IsAct && !f.SlotFree) { reason = "slots"; return false; }
            if (IsScriptedWarStart(f.PolicyId, f.CurrentChapter)) { reason = "scripted-war-start"; return false; }
            reason = "eligible";
            return true;
        }
    }
}
