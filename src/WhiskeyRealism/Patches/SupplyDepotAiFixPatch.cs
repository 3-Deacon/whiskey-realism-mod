using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AICampaign.CheckSupplyDepotConstruction(int) (decompile 14659-14779) is job
    // 19 of UpdateUnitAI (11510-11513). Per own unit it first records "builders" of
    // depots the alliance started (14690-14713) and sets a skip flag, then a stationary
    // unit with GetGroupSupplyState < supplystatedepotconstruction walks to the nearest
    // own town or depot (14719-14768) or wishes a depot at its position (14770-14776).
    // Vanilla's own W&L guard on the move (14765) and its order-10 dispatch stay as they are.
    //
    // BUG-LOG-006 and BUG-LOG-007 (Enable Supply Depot Builder Fix): the skip flag is set
    // for any depot the alliance started within commander range, finished or not
    // (14695-14697), so a unit beside its own finished-but-empty or captured depot never
    // moves or wishes; and builders are recorded within commander range (14695) while
    // CBuilding.IncreaseCondition advances construction only with a unit of the starting
    // alliance inside its own bugle range (96626-96655), and the replace-by-smaller dedupe
    // adds the new unit even when it is not smaller (14706-14711). The Prefix hides
    // finished own-started depots from the builder loop for the call (FieldMask on
    // allianceconstructionstarted = -1; nothing else in the job reads it) and the
    // Finalizer restores them even if vanilla throws; the Postfix rebuilds
    // unitsconstructingsupplydepots with the bugle range and one builder per depot, the
    // smallest (SupplyDepotAiRules.SelectBuilders, ListContents.ReplaceIfDifferent).
    //
    // Not changed: the installation test at 14740 (own, allied, or no enemy units in range)
    // admits an undefended enemy depot on purpose, as a capture-for-supply target
    // (BUG-LOG-005); depot placement (14770-14775) is left to the stage G planner.
    internal static class SupplyDepotAiFixPatch
    {
        private static readonly FieldMask<CBuilding, int> FinishedOwnDepots = new FieldMask<CBuilding, int>(
            depot => depot.allianceconstructionstarted,
            (depot, startedBy) => depot.allianceconstructionstarted = startedBy,
            -1);

        [HarmonyPatch(typeof(AICampaign), "CheckSupplyDepotConstruction")]
        internal static class DepotJob
        {
            [HarmonyPrefix]
            internal static void Prefix(int _aifaction, ref int __state)
            {
                using (TelemetryPerf.Scope("campaign.patch.supply-depot-fix.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.supply-depot-fix.prefix");
                    __state = -1;
                    try
                    {
                        if (!Enabled()) return;
                        int alliance = AICampaignReflect.GetAllianceId(_aifaction);
                        if (alliance < 0) return;
                        __state = alliance;
                        int masked = FinishedOwnDepots.Mask(BattleUnits.supplydepot,
                            depot => (UnityEngine.Object)depot != null
                                && SupplyDepotAiRules.ShouldMaskFinishedOwnDepot(depot.allianceconstructionstarted, alliance, depot.constructiontimer));
                        if (masked > 0)
                            OnceLog.Info("supply-depot-fix:masked", "[Patch:SupplyDepotFix] finished own depots no longer stop a nearby low-supply unit from resupplying");
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("supply-depot-fix:prefix", "[Patch:SupplyDepotFix] prefix failed (vanilla depot job unchanged): " + ex.Message);
                    }
                }
            }

            [HarmonyPostfix]
            internal static void Postfix(int _aifaction, int __state)
            {
                using (TelemetryPerf.Scope("campaign.patch.supply-depot-fix.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.supply-depot-fix.postfix");
                    if (__state < 0) return;
                    try
                    {
                        RebuildBuilders(_aifaction, __state);
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("supply-depot-fix:postfix", "[Patch:SupplyDepotFix] builder rebuild failed (vanilla builder list kept): " + ex.Message);
                    }
                }
            }

            [HarmonyFinalizer]
            internal static void Finalizer()
            {
                using (TelemetryPerf.Scope("campaign.patch.supply-depot-fix.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.supply-depot-fix.finalizer");
                    try
                    {
                        string failure = FinishedOwnDepots.Restore(depot => (UnityEngine.Object)depot == null);
                        if (failure != null)
                            OnceLog.Warning("supply-depot-fix:restore", "[Patch:SupplyDepotFix] restoring a depot's starting alliance failed: " + failure);
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("supply-depot-fix:finalizer", "[Patch:SupplyDepotFix] finalizer failed: " + ex.Message);
                    }
                }
            }
        }

        private static void RebuildBuilders(int aifactionIndex, int alliance)
        {
            var faction = AICampaignReflect.GetFaction(aifactionIndex);
            var ownUnits = VanillaFieldCache.Get(faction, "ownunits") as List<Regiment>;
            var builders = VanillaFieldCache.Get(faction, "unitsconstructingsupplydepots") as List<Regiment>;
            var depots = BattleUnits.supplydepot;
            if (ownUnits == null || builders == null || depots == null) return;

            var units = new List<SupplyDepotAiRules.BuilderUnit>(ownUnits.Count);
            for (int i = 0; i < ownUnits.Count; i++)
            {
                var u = ownUnits[i];
                bool present = (UnityEngine.Object)u != null;
                var p = present ? ((Component)u).transform.position : Vector3.zero;
                units.Add(new SupplyDepotAiRules.BuilderUnit
                {
                    Eligible = present && u.regimentpaths <= 0 && (UnityEngine.Object)u.garrisonreference == null,
                    Strength = present ? u.groupstrengthdirect : 0,
                    X = p.x,
                    Z = p.z,
                    BugleRange = present ? u.buglerange : 0f,
                });
            }

            var sites = new List<SupplyDepotAiRules.DepotSite>(depots.Count);
            for (int j = 0; j < depots.Count; j++)
            {
                var d = depots[j];
                bool present = (UnityEngine.Object)d != null;
                var p = present ? ((Component)d).transform.position : Vector3.zero;
                sites.Add(new SupplyDepotAiRules.DepotSite
                {
                    X = p.x,
                    Z = p.z,
                    UnderConstruction = present && d.allianceconstructionstarted == alliance && d.constructiontimer > 0f,
                });
            }

            var selected = SupplyDepotAiRules.SelectBuilders(units, sites);
            var desired = new List<Regiment>(selected.Count);
            for (int k = 0; k < selected.Count; k++)
                desired.Add(ownUnits[selected[k]]);
            if (ListContents.ReplaceIfDifferent(builders, desired))
                OnceLog.Info("supply-depot-fix:builders", "[Patch:SupplyDepotFix] depot builders limited to one unit per depot inside its bugle range");
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableSupplyDepotBuilderFix != null
                    && Plugin.EnableSupplyDepotBuilderFix.Value;
            }
            catch { return false; }
        }
    }
}
