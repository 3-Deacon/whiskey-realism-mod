using System;
using System.Collections;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-WL-018 (plan 2026-10-15-wl-order-flow.md D9 (b)): vanilla AICampaign.AssignUnitToDefendCapital
    // (private, decompile 11668) moves a non-player group toward the capital (11860-11863), calls
    // CheckCurrentOrderUpdate(group, 8, ...) (11864) and then sets running = true and doublequick = true
    // (11865-11866) on every group in groupstodefendcapital that is not yet at its slot, the W&L
    // player's own group included. The player's group is never moved, so its waypoint test stays true
    // and the forced march is set on every pass; on the campaign map running || doublequick costs
    // condition (113451), and forced march is a paid W&L action for the player (166967).
    //
    // A non-skipping Prefix snapshots the two flags of every player-owned (dlcw_isundercommander) unit
    // of the player's faction; a Finalizer (so it runs even when vanilla throws) writes them back for
    // the units the call kept in groupstodefendcapital. AI groups keep vanilla's march order, and the
    // player's own forced march survives because the snapshot holds whatever he had. CIC: vanilla
    // skips the player's own faction (11616-11625). Coexists with DefensiveOpsPatch's Priority.High
    // Prefix (that one pledges candidates; this one touches only these two flags afterwards).
    // The order itself (the repeated type 8) is dropped by the capture Prefix (WlCampaignCourierRuntime).
    [HarmonyPatch(typeof(AICampaign), "AssignUnitToDefendCapital")]
    internal static class CapitalDefensePlayerSpeedFixPatch
    {
        private static readonly CapitalSpeedSnapshot<Regiment> Snapshot = new CapitalSpeedSnapshot<Regiment>();
        private static readonly Func<Regiment, bool> InCapitalList = InList;
        private static readonly Action<Regiment, bool, bool> Write = WriteBack;
        private static IList _capitalList;

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction)
        {
            using (TelemetryPerf.Scope("campaign.patch.capital-defense-speed.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.capital-defense-speed.prefix");
                try
                {
                    Snapshot.Clear();
                    if (!Applies(_aifaction)) return;
                    object faction = AICampaignReflect.GetFaction(_aifaction);
                    IList own = faction != null ? VanillaFieldCache.List(faction, "ownunits") : null;
                    if (own == null) return;
                    for (int i = 0; i < own.Count; i++)
                    {
                        var unit = own[i] as Regiment;
                        if ((UnityEngine.Object)unit != null && unit.dlcw_isundercommander) Snapshot.Capture(unit, unit.running, unit.doublequick);
                    }
                }
                catch (Exception ex)
                {
                    Snapshot.Clear();
                    OnceLog.Warning("capital-defense-speed:prefix", "[W&LCapitalDefense] snapshot failed: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(int _aifaction, Exception __exception)
        {
            using (TelemetryPerf.Scope("campaign.patch.capital-defense-speed.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.capital-defense-speed.finalizer");
                try
                {
                    if (Snapshot.Count > 0)
                    {
                        object faction = AICampaignReflect.GetFaction(_aifaction);
                        _capitalList = faction != null ? VanillaFieldCache.List(faction, "groupstodefendcapital") : null;
                        Snapshot.Restore(InCapitalList, Write);
                    }
                }
                catch (Exception ex)
                {
                    Snapshot.Clear();
                    OnceLog.Warning("capital-defense-speed:finalizer", "[W&LCapitalDefense] restoring the player's march mode failed: " + ex.GetType().Name + " " + ex.Message);
                }
                finally
                {
                    _capitalList = null;
                }
                return __exception;
            }
        }

        private static bool Applies(int aifaction)
        {
            if (Plugin.Instance == null || Plugin.Instance.Enabled == null || !Plugin.Instance.Enabled.Value) return false;
            bool wl = DLC_WL.dlc_scenarioactive;
            bool flag = Plugin.FixCapitalDefensePlayerOrder != null && Plugin.FixCapitalDefensePlayerOrder.Value;
            if (!wl || !flag) return false;
            bool players = AICampaignReflect.GetAllianceId(aifaction) == GameVars.playeralliance;
            return CapitalDefenseSpeed.Applies(wl, players, DLC_WL.IsCommanderInChief(), flag);
        }

        private static bool InList(Regiment unit)
        {
            return _capitalList != null && _capitalList.Contains(unit);
        }

        private static void WriteBack(Regiment unit, bool running, bool doublequick)
        {
            if ((UnityEngine.Object)unit == null || (unit.running == running && unit.doublequick == doublequick)) return;
            unit.running = running;
            unit.doublequick = doublequick;
            WlCampaignCourierRuntime.NoteSpeedRestore(unit);
        }
    }
}
