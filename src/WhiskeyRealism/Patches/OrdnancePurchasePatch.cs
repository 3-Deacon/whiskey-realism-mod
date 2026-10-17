using System;
using HarmonyLib;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Campaign planner ordnance, scored weapon purchase (plan G9, decision 2): two patches that share one per-call scope.
    //
    // Vanilla AICampaign.CheckPurchaseWeapons(int _alliance) (16259), run from the alliance tick (81757), decides by its own rules
    // when an AI alliance buys and how much, names a lane (guns or rifles) and picks the model at random from the researched-or-imported
    // models with no open order whose WSF beats the alliance's own lane average (16328, 16338); WeaponList.PlaceWeaponOrder
    // (227136) then prices, delivers and charges the order. Cost, delivery time and rate of fire never enter the choice.
    //
    // OrdnancePurchaseScopePatch opens a scope for the alliance whose purchase pass is steered (OrdnanceRuntime.PurchaseScopeOpens:
    // alliances 0 and 1, a fresh plan, the flags on, the job machine running, not the commander-in-chief's own alliance) and its
    // Finalizer closes it, also when vanilla throws. OrdnancePurchasePickPatch edits the model id argument of PlaceWeaponOrder while
    // the scope is open for that alliance: the best of vanilla's own candidates for the lane that the order screen would allow
    // (OrdnancePicker). The amount, the trigger, the rating gate, the one-open-order rule, the capital check and the charge stay
    // vanilla's; with no orderable candidate vanilla's pick stands. Neither patch skips anything.
    [HarmonyPatch(typeof(AICampaign), "CheckPurchaseWeapons")]
    internal static class OrdnancePurchaseScopePatch
    {
        [ThreadStatic] private static bool _open;
        [ThreadStatic] private static int _openAlliance;

        internal static bool IsOpenFor(int alliance)
        {
            return _open && _openAlliance == alliance;
        }

        [HarmonyPrefix]
        internal static void Prefix(int _alliance)
        {
            using (TelemetryPerf.Scope("campaign.patch.ordnance-purchase.scope.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.ordnance-purchase.scope.prefix");
                _open = false;
                try
                {
                    if (!OrdnanceRuntime.PurchaseScopeOpens(_alliance)) return;
                    _open = true;
                    _openAlliance = _alliance;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("ordnance-purchase:scope", "[Patch:OrdnancePurchase] scope failed (vanilla's picks kept): " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer()
        {
            using (TelemetryPerf.Scope("campaign.patch.ordnance-purchase.scope.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.ordnance-purchase.scope.finalizer");
                _open = false;
            }
        }
    }

    [HarmonyPatch(typeof(WeaponList), "PlaceWeaponOrder")]
    internal static class OrdnancePurchasePickPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(int alliance, ref int weaponid, int quantity)
        {
            // PlaceWeaponOrder also serves the player's order screen: only an AI purchase pass the scope opened is steered.
            if (!OrdnancePurchaseScopePatch.IsOpenFor(alliance)) return;
            using (TelemetryPerf.Scope("campaign.patch.ordnance-purchase.pick", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.ordnance-purchase.pick");
                try
                {
                    if (OrdnanceRuntime.TryPick(alliance, weaponid, quantity, out int picked)) weaponid = picked;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("ordnance-purchase:pick", "[Patch:OrdnancePurchase] pick failed (vanilla's model kept): " + ex.Message);
                }
            }
        }
    }
}
