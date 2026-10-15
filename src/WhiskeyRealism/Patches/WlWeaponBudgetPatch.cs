using System;
using HarmonyLib;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // The W&L federal weapon budget (DLC_WL.accumulatedweaponbudget, one static value): it
    // grows from the career player's alliance rating (47496-47518), a CIC can buy more with
    // prestige (IncreaseFiscalBudget 226654), and it gates the player's order screen (226965).
    internal static class WlWeaponBudgetPatch
    {
        // BUG-WL-016. Vanilla WeaponList.PlaceWeaponOrder (227136) subtracts every order's cost
        // from that budget (227157-227161) whatever alliance orders, so the enemy's and Europe's
        // AI orders drain the career player's budget. The Prefix snapshots the budget and the
        // Finalizer puts it back for an order of any other alliance. Nothing else in
        // PlaceWeaponOrder touches the field, so the restore is exact; orders of the player's
        // own alliance (his, or the AI's for his alliance under ai_vs_ai) still spend it.
        // Rollback: W&L / Enable W&L Weapon Budget Scope Fix.
        [HarmonyPatch(typeof(WeaponList), nameof(WeaponList.PlaceWeaponOrder))]
        internal static class OrderScope
        {
            [HarmonyPrefix]
            internal static void Prefix(out float __state)
            {
                using (TelemetryPerf.Scope("campaign.patch.wl-weapon-budget.order.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.wl-weapon-budget.order.prefix");
                    __state = DLC_WL.accumulatedweaponbudget;
                }
            }

            [HarmonyFinalizer]
            internal static void Finalizer(int alliance, float __state)
            {
                using (TelemetryPerf.Scope("campaign.patch.wl-weapon-budget.order.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.wl-weapon-budget.order.finalizer");
                    try
                    {
                        float vanilla = DLC_WL.accumulatedweaponbudget;
                        float kept = ArmsRules.BudgetAfterOrder(ScopeEnabled(), alliance, GameVars.playeralliance, __state, vanilla);
                        if (kept == vanilla) return;
                        DLC_WL.accumulatedweaponbudget = kept;
                        if (DLC_WL.dlc_scenarioactive)
                            OnceLog.Info("wl-weapon-budget:scope", "[Patch:WlWeaponBudget] another alliance's weapon order no longer spends the career player's weapon budget");
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("wl-weapon-budget:order", "[Patch:WlWeaponBudget] budget restore failed: " + ex.Message);
                    }
                }
            }
        }

        // BUG-WL-017. Vanilla WeaponList.IncreaseFiscalBudget (226654) charges
        // dlcwl_weaponbudgetprestigecost (-500) as prestige - 500 (226658, AdjustPrestige type 2
        // 43471) but gates on !(prestige < -500) (226656), so a CIC can buy budget down to
        // -500 prestige. When the prestige cannot pay, the Prefix sets the cost pref to
        // float.MaxValue for the call, so vanilla's own gate refuses; the Finalizer restores
        // it. The charge is left as vanilla. Rollback: W&L / Enable W&L Weapon Budget Prestige Gate Fix.
        [HarmonyPatch(typeof(WeaponList), "IncreaseFiscalBudget")]
        internal static class PurchaseGate
        {
            private static readonly ValueSwap<float> Cost = new ValueSwap<float>(
                () => GamePrefs.dlcwl_weaponbudgetprestigecost,
                value => GamePrefs.dlcwl_weaponbudgetprestigecost = value);

            [HarmonyPrefix]
            internal static void Prefix()
            {
                using (TelemetryPerf.Scope("campaign.patch.wl-weapon-budget.purchase.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.wl-weapon-budget.purchase.prefix");
                    try
                    {
                        if (!GateEnabled()) return;
                        if (!ArmsRules.BudgetPurchaseUnaffordable(DLC_WL.prestige, GamePrefs.dlcwl_weaponbudgetprestigecost)) return;
                        Cost.Apply(float.MaxValue);
                        OnceLog.Info("wl-weapon-budget:gate", "[Patch:WlWeaponBudget] weapon budget purchase refused: not enough prestige to pay for it");
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("wl-weapon-budget:purchase", "[Patch:WlWeaponBudget] prestige gate failed (vanilla gate kept): " + ex.Message);
                    }
                }
            }

            [HarmonyFinalizer]
            internal static void Finalizer()
            {
                using (TelemetryPerf.Scope("campaign.patch.wl-weapon-budget.purchase.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.wl-weapon-budget.purchase.finalizer");
                    string failure = Cost.Restore();
                    if (failure != null)
                        OnceLog.Warning("wl-weapon-budget:restore", "[Patch:WlWeaponBudget] restoring the budget prestige cost failed: " + failure);
                }
            }
        }

        private static bool ScopeEnabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableWlWeaponBudgetScopeFix != null
                    && Plugin.EnableWlWeaponBudgetScopeFix.Value;
            }
            catch { return false; }
        }

        private static bool GateEnabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableWlWeaponBudgetPrestigeGateFix != null
                    && Plugin.EnableWlWeaponBudgetPrestigeGateFix.Value;
            }
            catch { return false; }
        }
    }
}
