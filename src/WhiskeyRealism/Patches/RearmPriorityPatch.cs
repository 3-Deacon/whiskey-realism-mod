using System;
using HarmonyLib;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Campaign planner ordnance, re-arming priority (plan G9, decision 1).
    //
    // Vanilla AICampaign.CheckWeaponUpgrades(int alliance, bool isinitialization) (16080), run from the alliance tick every half game
    // day (81813), copies GameVars.alliance[alliance].unitlist (16091, built in scene-hierarchy order), drops the player's units in a
    // career (16137-16144) and gives each unit in list order the best model in stock that beats its own (UpgradeWeaponForUnit 16155,
    // GetBestWeapon 16224): whoever stands first takes the scarce new models.
    //
    // The Prefix reorders that list in place, stably, by the allocation role of each unit's campaign group (RearmPriority: main effort,
    // support, reserve, hold, capital guard, depot builder, recon, garrison, unassigned, then Recover and formations in transit), and
    // saves vanilla's order in __state; the Finalizer writes it back into the same list object even when vanilla threw. Vanilla's
    // per-unit rule, stock checks, cascade and W&L skip run unchanged and no unit is re-armed by Whiskey. No skip.
    // `out __state` in the Prefix and the same type in the Finalizer is the shape of shipped patches: WlWeaponBudgetPatch.OrderScope
    // (a float, on PlaceWeaponOrder) and SeparatedEnemyOwnershipPatch (a struct). No other patch is on CheckWeaponUpgrades.
    //
    // Vanilla runs unchanged for the new-campaign pass (isinitialization), alliances other than 0 and 1, the player's alliance
    // outside ai_vs_ai, the W&L commander-in-chief's own alliance, while vanilla's job machine is paused (11368), with no fresh
    // plan, and with Enable Rearm Priority or the planner executors off.
    [HarmonyPatch(typeof(AICampaign), "CheckWeaponUpgrades")]
    internal static class RearmPriorityPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(int alliance, bool isinitialization, out Regiment[] __state)
        {
            __state = null;
            using (TelemetryPerf.Scope("campaign.patch.ordnance-rearm.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.ordnance-rearm.prefix");
                try
                {
                    __state = OrdnanceRuntime.BeginRearm(alliance, isinitialization);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("ordnance-rearm:prefix", "[Patch:OrdnanceRearm] prefix failed (vanilla's unit order kept): " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer(int alliance, Regiment[] __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.ordnance-rearm.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.ordnance-rearm.finalizer");
                try
                {
                    OrdnanceRuntime.EndRearm(alliance, __state);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("ordnance-rearm:finalizer", "[Patch:OrdnanceRearm] restoring the unit list failed: " + ex.Message);
                }
            }
        }
    }
}
