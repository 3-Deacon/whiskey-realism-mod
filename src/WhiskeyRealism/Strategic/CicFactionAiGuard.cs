namespace WhiskeyRealism.Strategic
{
    // BUG-WL-001 (chain-of-command spec §F). Pure decision for #70
    // CicFactionAiBugGuardPatch.
    //
    // Vanilla anchors (decompile, build 14703111):
    //   AICampaign.CheckShipConstruction (11998) returns at 12012 and
    //   AICampaign.CheckArmyGroupManagement (17705) at 17723 on
    //   `DLC_WL.dlc_scenarioactive && DLC_WL.IsCommanderInChief()`. IsCommanderInChief
    //   (47475) asks only whether the chosen commander is CIC of his own alliance,
    //   and both methods run for every faction (UpdateUnitAI jobs 12 and 32, 11481,
    //   11583), so a CIC career stops the enemy's fleet building and army-group
    //   management. Vanilla skips the CIC player's own faction correctly at 11620.
    //   In both bodies that IsCommanderInChief() call is the first one, so the patch
    //   answers "not CIC" to exactly one default-argument call and nested calls
    //   (GrabFleet, RaiseFleet, CheckCommanderPromotions, CreateShip, ArmyGroup.AddUnit,
    //   CreateNewArmyGroup) keep the true value.
    internal static class CicFactionAiGuard
    {
        public static bool ShouldArm(bool guardEnabled, bool wlActive, bool playerIsCic, int cicAlliance, int factionAlliance)
        {
            return guardEnabled && wlActive && playerIsCic && factionAlliance >= 0 && factionAlliance != cicAlliance;
        }

        // IsCommanderInChief(manualcommander = -1) is the guard's form; a call naming
        // another commander is a different question. Vanilla replaces -1 with
        // dlc_chosencommander inside the body (47477-47480), so only a Prefix sees the
        // argument as called; the Postfix sees the chosen commander's id.
        public static bool IsDefaultArgumentCall(int manualCommanderAtEntry)
        {
            return manualCommanderAtEntry == -1;
        }

        // Postfix decision from the Prefix's capture, never from the overwritten argument.
        public static bool ConsumeAsNotCic(bool armed, bool calledWithDefaultArgument)
        {
            return armed && calledWithDefaultArgument;
        }

        // Whether vanilla's CIC early return still stops this faction after the guard
        // (for code that repeats vanilla's guard, #16).
        public static bool VanillaGuardBlocks(bool guardEnabled, bool wlActive, bool playerIsCic, int cicAlliance, int factionAlliance)
        {
            return wlActive && playerIsCic && !ShouldArm(guardEnabled, wlActive, playerIsCic, cicAlliance, factionAlliance);
        }
    }
}
