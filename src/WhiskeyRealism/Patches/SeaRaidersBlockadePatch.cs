using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Regiment.UpdateBlockades (decompile 113776), on every campaign unit, lets
    // HasPerk(6) add trade nodes as raid targets (113790), mark a trade-node raid (113813) and
    // grant sea-raider XP to id 6 (113821). That is fleet 6, Sea Raiders ("Level up by
    // blockading trade nodes"); there is no unit-type guard, so a land unit's 6 (Foot Cavalry,
    // or a regiment's Ace of Spade) does it too (BUG-AI-008). The method reads no other perk,
    // so this Prefix hides the perks of a non-fleet unit holding 6 for the call and the
    // Finalizer restores them even if vanilla throws. Rollback: Campaign / Fix Perk Id Mismatches.
    [HarmonyPatch(typeof(Regiment), "UpdateBlockades")]
    internal static class SeaRaidersBlockadePatch
    {
        private static readonly PerkListSwap<Regiment, Regiment.Perk> Swap = PerkIdIlHooks.NewPerkSwap();

        [HarmonyPrefix]
        internal static void Prefix(Regiment __instance)
        {
            using (TelemetryPerf.Scope("campaign.patch.perk-ids.sea-raiders.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.perk-ids.sea-raiders.prefix");
                try
                {
                    PerkIdIlHooks.LogRestore(Swap.Restore(), "perk-ids:sea-raiders:restore");
                    if (__instance.perks == null) return;
                    if (!VanillaPerkTables.HideSeaRaiders(PerkIdIlHooks.FixIds(), __instance.unittyp, __instance.HasPerk(VanillaPerkTables.FleetSeaRaiders))) return;
                    Swap.Hide(__instance);
                    OnceLog.Info("perk-ids:sea-raiders", "[Patch:PerkIds] UpdateBlockades: Sea Raiders trade-node raiding withheld from a land unit's perk 6 (Foot Cavalry / Ace of Spade)");
                }
                catch (Exception ex)
                {
                    PerkIdIlHooks.LogRestore(Swap.Restore(), "perk-ids:sea-raiders:restore");
                    OnceLog.Warning("perk-ids:sea-raiders:prefix", "[Patch:PerkIds] UpdateBlockades prefix failed, vanilla left as is: " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer()
        {
            using (TelemetryPerf.Scope("campaign.patch.perk-ids.sea-raiders.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.perk-ids.sea-raiders.finalizer");
                if (Swap.Active) PerkIdIlHooks.LogRestore(Swap.Restore(), "perk-ids:sea-raiders:restore");
            }
        }
    }
}
