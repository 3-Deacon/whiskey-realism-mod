using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-WL-020 (plan 2026-10-15-wl-order-flow.md D14): the paper-map fog grid left hidden by a Lost
    // Orders incident that does not apply.
    //
    // Vanilla DLC_WL.LaunchIncident (decompile 46272) hides the grid (battlefieldsetupref.ShowFOWLayer(false),
    // 46530) for incidents 60, 61 and 64 before it asks IncidentApplicable (46545); when the answer is no
    // it returns false with the grid still hidden and Incident.End's re-show never runs. The grid returns
    // only when the paper map is entered (29015), so with the paper map open at the roll (deployment
    // placement) it stays missing. A Postfix, after the call: when the incident was rolled directly, did
    // not apply, fog of war is on and the paper map is open, it calls vanilla's own ShowFOWLayer(true)
    // again. A relaunch of an active incident (skipinfos) hides the grid on purpose and is left alone.
    [HarmonyPatch(typeof(DLC_WL), nameof(DLC_WL.LaunchIncident))]
    internal static class LostOrdersFowFixPatch
    {
        private static System.Reflection.FieldInfo _battlefieldSetupField;
        private static bool _fieldMissing;

        [HarmonyPostfix]
        internal static void Postfix(DLC_WL.Incident chosenincident, bool skipinfos, bool launcheddirectly, bool __result)
        {
            using (TelemetryPerf.Scope("campaign.patch.lost-orders-fow", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.lost-orders-fow");
                try
                {
                    if (chosenincident == null) return;
                    if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value || Plugin.FixLostOrdersFogGrid == null || !Plugin.FixLostOrdersFogGrid.Value) return;
                    bool open = PaperMapOpen();
                    if (!LostOrdersFow.ShouldRestore(chosenincident.id, GameVars.usefow, launcheddirectly, skipinfos, __result, open)) return;
                    BattlefieldSetup setup = Setup();
                    if ((UnityEngine.Object)setup == null) return;
                    setup.ShowFOWLayer(true);
                    OnceLog.Info("lost-orders-fow:restored", "[W&LLostOrders] fog grid restored after a non-applicable incident");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("lost-orders-fow:postfix", "[W&LLostOrders] restoring the fog grid failed: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        // The paper map's terrain grid is active only while the paper map is open (28950, 29014, 29023).
        private static bool PaperMapOpen()
        {
            GameObject grid = GameObject.Find("PaperMapTerrainGrid");
            return (UnityEngine.Object)grid != null && grid.activeInHierarchy;
        }

        // DLC_WL keeps its BattlefieldSetup in a private static (42987).
        private static BattlefieldSetup Setup()
        {
            if (_fieldMissing) return null;
            if (_battlefieldSetupField == null)
            {
                _battlefieldSetupField = AccessTools.Field(typeof(DLC_WL), "battlefieldsetupref");
                if (_battlefieldSetupField == null)
                {
                    _fieldMissing = true;
                    OnceLog.Warning("lost-orders-fow:field", "[W&LLostOrders] DLC_WL.battlefieldsetupref not found; the fog grid fix is inactive");
                    return null;
                }
            }
            return _battlefieldSetupField.GetValue(null) as BattlefieldSetup;
        }
    }
}
