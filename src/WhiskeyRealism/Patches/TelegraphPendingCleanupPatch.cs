using System;
using HarmonyLib;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla CBuilding.WorkDownConstructionWishes (97538) works on the head of
    // CBuilding.aiplacements: it places the building at the first terrain-OK
    // site, or drops the wish (removing its placeholder, no refund) once the
    // search radius passes buildingsitemaxradius (97578-97585).
    //
    // This Postfix lets TelegraphConstructionRuntime forget pending Telegraph AI
    // stations whose placement vanilla dropped and release their IIP
    // reservation. It changes no vanilla state besides that reservation.
    [HarmonyPatch(typeof(CBuilding), "WorkDownConstructionWishes")]
    internal static class TelegraphPendingCleanupPatch
    {
        [HarmonyPostfix]
        internal static void Postfix()
        {
            using (TelemetryPerf.Scope("campaign.patch.telegraph-workdown", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.telegraph-workdown");
                try
                {
                    TelegraphConstructionRuntime.OnConstructionWishesWorkedDown();
                }
                catch (Exception ex)
                {
                    OnceLog.Warning(
                        "telegraph-ai:pending-cleanup-failed",
                        "[TelegraphAI] action=pending-cleanup-warning reason=postfix-failed message=" + ex.Message);
                }
            }
        }
    }
}
