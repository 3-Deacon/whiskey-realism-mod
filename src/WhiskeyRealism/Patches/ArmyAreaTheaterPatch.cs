using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla UpdateCampaignTheaters derives each top unit's theaterposition
    // from nearby enemies and later uses that position to gate offensive and
    // defensive operations. This Postfix keeps idle AI armies/corps from
    // wandering permanently outside their historical operating area by nudging
    // vanilla theater intent and issuing one vanilla movement order back to the
    // assigned area anchor.
    [HarmonyPatch(typeof(AICampaign), "UpdateCampaignTheaters")]
    internal static class ArmyAreaTheaterPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(int _aifaction)
        {
            OnceLog.Info("army-area", "ArmyAreaTheaterPatch wired (historical operating-area steering)");

            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (StrategicCoordinator.Instance == null) return;

                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0 || allianceId >= StrategicCoordinator.Instance.ArmyAreas.Length) return;

                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;

                ArmyAreaRuntime.ApplyHistoricalAreaOrders(_aifaction, allianceId);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("army-area:postfix", "[Patch:ArmyArea] postfix failed: " + ex.Message);
            }
        }
    }
}
