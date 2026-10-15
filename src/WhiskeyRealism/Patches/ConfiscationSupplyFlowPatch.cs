using System;
using HarmonyLib;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-LOG-012. Vanilla Policies.UpdatePolicyEffects(alliance) (decompile 209616-209653)
    // resets GameVars.alliance[a].supplyenemysoilfactor to 1 (209621) and re-applies every
    // active act; the Confiscation Act (case 25) multiplies it by confiscationsupplyflow
    // (209893; campaignprefs 2788 "Confiscation, supply flow improvement if unit is on
    // enemy soil, factor [>1]", 1.15; manual: "Supply efficiency of armies operating in
    // enemy states +15%"). Its only reader, Regiment.UpdateIIPSupplyLink, multiplies the
    // resupply duration num15 by it on enemy soil (114330-114335), so the act made supply
    // 15% slower instead of faster.
    //
    // The Postfix stores the reciprocal (ConfiscationSupplyFlow.DurationFactor) right after
    // vanilla rebuilt the factor, so the duration shrinks by the configured improvement.
    // The value is rebuilt from 1 on every update, so it never compounds; a save written
    // with the fix holds the reciprocal until the next update rebuilds it.
    // Rollback: Campaign Bug Fixes / Enable Confiscation Supply Flow Fix.
    [HarmonyPatch(typeof(Policies), "UpdatePolicyEffects")]
    internal static class ConfiscationSupplyFlowPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(int alliance)
        {
            using (TelemetryPerf.Scope("campaign.patch.confiscation-supply-flow.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.confiscation-supply-flow.postfix");
                try
                {
                    if (!Enabled() || GameVars.alliance == null || alliance < 0 || alliance >= GameVars.alliance.Length) return;
                    var data = GameVars.alliance[alliance];
                    if (data == null || data.supplyenemysoilfactor == 1f) return;
                    data.supplyenemysoilfactor = ConfiscationSupplyFlow.DurationFactor(data.supplyenemysoilfactor);
                    OnceLog.Info("confiscation-supply-flow:applied", "[Patch:Confiscation] the Confiscation Act now speeds up supply on enemy soil");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("confiscation-supply-flow:postfix", "[Patch:Confiscation] postfix failed (vanilla factor kept): " + ex.Message);
                }
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableConfiscationSupplyFlowFix != null
                    && Plugin.EnableConfiscationSupplyFlowFix.Value;
            }
            catch { return false; }
        }
    }
}
