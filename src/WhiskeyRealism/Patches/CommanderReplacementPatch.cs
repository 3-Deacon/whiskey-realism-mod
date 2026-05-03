using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    [HarmonyPatch(typeof(AICampaign), "CheckAICommanderReplacements")]
    internal static class CommanderReplacementPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(int _aifaction)
        {
            OnceLog.Info("replace", "CommanderReplacementPatch wired (gate-only this slice)");
            try
            {
                if (StrategicCoordinator.Instance == null) return true;

                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0) return true;
                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return true;

                if (Plugin.Instance.VerboseLogging.Value)
                    Plugin.Log.LogInfo($"[Patch:Replace] alliance={allianceId} (vanilla path)");

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Patch:Replace] " + ex.Message);
                return true;
            }
        }
    }
}
