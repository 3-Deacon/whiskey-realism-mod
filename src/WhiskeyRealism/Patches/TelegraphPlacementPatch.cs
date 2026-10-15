using System;
using HarmonyLib;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla CBuilding.Place (96163) instantiates a building. Construction
    // wishes call it twice: AddConstructionWish (97479) places an off-map
    // placeholder and pays (pay: true), and WorkDownConstructionWishes (97562)
    // later places the real building at the first terrain-OK site around the
    // wish origin without paying (newlycreated: true, pay: false).
    //
    // This Postfix sees that second, unpaid telegraph placement and hands it to
    // TelegraphConstructionRuntime: a station Telegraph AI started is checked
    // against its connection/support/safe-rear requirements; a rejected one is
    // removed and its wish charge refunded. Other placements pass untouched.
    // Priority.High: runs before ConstructionObserverPatch's Place postfix so a
    // rejected station is never counted as a construction start.
    [HarmonyPatch(typeof(CBuilding), "Place")]
    internal static class TelegraphPlacementPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.High)]
        internal static void Postfix(
            CBuilding __result,
            int type,
            int owner,
            bool newlycreated,
            bool pay)
        {
            using (TelemetryPerf.Scope("campaign.patch.telegraph-place", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.telegraph-place");
                if (type != CBuilding.id_telegraphstation || !newlycreated || pay) return;

                try
                {
                    TelegraphConstructionRuntime.OnTelegraphPlaced(__result, type, owner);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning(
                        "telegraph-ai:place-postfix",
                        "[TelegraphAI] action=placed-warning reason=postfix-failed message=" + ex.Message);
                }
            }
        }
    }
}
