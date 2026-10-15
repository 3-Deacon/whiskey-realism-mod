using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #6 — guard on AICampaign.CheckAICommanderReplacements (decompile 17008-17032, vanilla job
    // 24). Vanilla walks GameVars.alliance[a].worstcommanderidperlevel rank by rank and, when the
    // best idle commander of that rank (bestcommanderidperlevel, built by
    // UpdateCommanderRankingsCampaign 84424-84487) beats the worst sitting one by
    // expdifferenceforverticalpromotion on GetAverageAttributeValue, gives the worst one's command
    // to the best (AssignCommando, DoCommanderPromotion, refreshcommand) and sets both ids of
    // that slot to -1 (17027-17028). It skips a slot whose worst id is -1 (17017).
    //
    // Stage G (plan G2, P13, spec C16): the campaign planner's appointees
    // (CommanderAppointmentRuntime, in the daily review) are protected for 60 days. The Prefix
    // sets every slot that holds a protected commander to -1 and records exactly those slots; the
    // Finalizer writes each saved id back into its own slot (only while it is still -1) and
    // touches no other slot, so slots vanilla consumed stay as vanilla left them and vanilla's
    // job 24 runs unchanged for every unprotected commander. No skip, no mod-state write; the
    // restore runs in the Finalizer so it happens even when vanilla throws. The old scripted
    // succession swap (a Prefix-skip that also wrote mod state) is gone: succession events are a
    // prior in the appointment planner. Alliances 0 and 1 only.
    [HarmonyPatch(typeof(AICampaign), "CheckAICommanderReplacements")]
    internal static class CommanderReplacementPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(int _aifaction, out AppointmentGuardState __state)
        {
            __state = null;
            using (TelemetryPerf.Scope("campaign.patch.commander-replacement", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.commander-replacement");
                try
                {
                    if (!CommanderAppointmentRuntime.Enabled()) return;
                    int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                    if (allianceId < 0 || allianceId > 1) return;
                    var alliances = GameVars.alliance;
                    if (alliances == null || allianceId >= alliances.Length) return;
                    __state = AppointmentGuard.Clear(alliances[allianceId].worstcommanderidperlevel,
                        id => CommanderAppointmentRuntime.IsProtected(allianceId, id));
                    if (__state != null)
                        OnceLog.Info("replace:guard", "[Patch:Replace] job 24 skips the campaign planner's protected appointees (P13)");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("replace:prefix", "[Patch:Replace] guard failed, vanilla runs unguarded: " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception, int _aifaction, AppointmentGuardState __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.commander-replacement.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.commander-replacement.finalizer");
                try
                {
                    if (__state == null) return __exception;
                    int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                    var alliances = GameVars.alliance;
                    if (allianceId < 0 || allianceId > 1 || alliances == null || allianceId >= alliances.Length) return __exception;
                    AppointmentGuard.Restore(alliances[allianceId].worstcommanderidperlevel, __state);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("replace:finalizer", "[Patch:Replace] guard restore failed: " + ex.Message);
                }
                return __exception;
            }
        }
    }
}
