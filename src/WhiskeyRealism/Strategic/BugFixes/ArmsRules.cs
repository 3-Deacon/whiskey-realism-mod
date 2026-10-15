namespace WhiskeyRealism.Strategic.BugFixes
{
    // Pure decisions of the armament bug fixes (docs/bug-fixes/vanilla-armament.md).
    internal static class ArmsRules
    {
        // BUG-WL-016. WeaponList.PlaceWeaponOrder (227136) adds every order's cost (negative) to
        // the one static W&L federal weapon budget, DLC_WL.accumulatedweaponbudget (227157-
        // 227161), whichever alliance orders. The budget is the career player's: it grows from
        // his alliance's rating (47496-47518) and gates his order screen (226965, player
        // alliance). Only orders of the player's alliance spend it (the AI ordering for his
        // alliance under ai_vs_ai included: that is the shared federal budget).
        internal static bool OrderSpendsCareerBudget(int orderAlliance, int playerAlliance)
        {
            return orderAlliance == playerAlliance;
        }

        // The budget after a PlaceWeaponOrder call: vanilla's value, or the value before the
        // call for another alliance's order while the fix is on.
        internal static float BudgetAfterOrder(bool fixEnabled, int orderAlliance, int playerAlliance, float before, float vanillaAfter)
        {
            return fixEnabled && !OrderSpendsCareerBudget(orderAlliance, playerAlliance) ? before : vanillaAfter;
        }

        // BUG-WL-017. WeaponList.IncreaseFiscalBudget (226654) charges
        // AdjustPrestige(-cost, 2) = prestige + cost with cost = dlcwl_weaponbudgetprestigecost
        // (-500, dlcwl_config.dat), but gates on !(prestige < cost), so a CIC can buy budget
        // down to -500 prestige. True when vanilla's gate passes but the prestige cannot pay.
        internal static bool BudgetPurchaseUnaffordable(float prestige, float cost)
        {
            if (!(prestige >= cost)) return false;   // vanilla refuses already
            return cost < 0f && prestige < -cost;
        }

        // BUG-AI-018. AICampaign.GetWeaponStrengthFactor(Regiment) (16195) returns the
        // strength-weighted mean weapon factor of a group's armed basic units as num / num2 with
        // no guard; a group whose armed units have no men (or no armed units) returns 0/0 = NaN,
        // which poisons the area means that sum it (13136-13143) and turns the outgunned tests
        // of CheckOffensiveMovements false (14257, 14283, 14299). A group with no armed men has
        // no weapon strength: 0, as the empty-sum numerator says.
        internal static float GroupWeaponFactor(float vanilla)
        {
            return float.IsNaN(vanilla) || float.IsInfinity(vanilla) ? 0f : vanilla;
        }
    }
}
