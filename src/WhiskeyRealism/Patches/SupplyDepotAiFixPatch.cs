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
    // This file fixes three faults in that job, for every AI faction alike; vanilla's own
    // W&L guard on the move (14765) and its order-10 dispatch stay as they are.
    //
    // BUG-LOG-005 (Enable Supply Depot Friendly Source Fix): the depot search admits a
    // depot that is own, allied, OR merely has no enemy units in range (14740), so an
    // undefended enemy depot, which never supplies the unit (UpdateIIPSupplyLink draws
    // only from own or allied depots, 114296-114306), becomes the place a starving unit
    // walks to or waits beside (flag3, 14745-14753) and blocks its depot wish (flag2,
    // 14757-14760). Inside the job, EnemyHeldDepot makes CBuilding.EnemyUnitsInRange
    // (98023-98045, called in the job only at 14740) answer true for a depot that is
    // neither own nor allied, so only friendly depots pass the test.
    //
    // BUG-LOG-006 and BUG-LOG-007 (Enable Supply Depot Builder Fix): the skip flag is set
    // for any depot the alliance started within commander range, finished or not
    // (14695-14697), so a unit beside its own finished-but-empty or captured depot never
    // moves or wishes; and builders are recorded within commander range (14695) while
    // CBuilding.IncreaseCondition advances construction only with a unit of the starting
    // alliance inside its own bugle range (96626-96655), and the replace-by-smaller dedupe
    // adds the new unit even when it is not smaller (14706-14711). The Prefix hides
    // finished own-started depots from the builder loop for the call (allianceconstruction-
    // started = -1; nothing else in the job reads it) and the Finalizer restores them even
    // if vanilla throws; the Postfix rebuilds unitsconstructingsupplydepots with the bugle
    // range and one builder, the smallest, per depot (SupplyDepotAiRules.SelectBuilders).
    // Depot placement (14770-14775) is left to the stage G planner.
    internal static class SupplyDepotAiFixPatch
    {
        private static int _depth;
        private static bool _friendlySourceOnly;

        internal sealed class Scope
        {
            internal bool Entered;
            internal int Alliance = -1;
            internal readonly List<CBuilding> Masked = new List<CBuilding>();
            internal readonly List<int> MaskedStartedBy = new List<int>();
        }

        [HarmonyPatch(typeof(AICampaign), "CheckSupplyDepotConstruction")]
        internal static class DepotJob
        {
            [HarmonyPrefix]
            internal static void Prefix(int _aifaction, ref Scope __state)
            {
                using (TelemetryPerf.Scope("campaign.patch.supply-depot-fix.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.supply-depot-fix.prefix");
                    __state = null;
                    try
                    {
                        if (!MasterEnabled()) return;
                        int alliance = AICampaignReflect.GetAllianceId(_aifaction);
                        if (alliance < 0) return;
                        __state = new Scope { Entered = true, Alliance = alliance };
                        _depth++;
                        _friendlySourceOnly = FriendlySourceFixEnabled();
                        if (BuilderFixEnabled()) MaskFinishedOwnDepots(alliance, __state);
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("supply-depot-fix:prefix", "[Patch:SupplyDepotFix] prefix failed (vanilla depot job unchanged): " + ex.Message);
                    }
                }
            }

            [HarmonyPostfix]
            internal static void Postfix(int _aifaction, Scope __state)
            {
                using (TelemetryPerf.Scope("campaign.patch.supply-depot-fix.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.supply-depot-fix.postfix");
                    if (__state == null || !BuilderFixEnabled()) return;
                    try
                    {
                        RebuildBuilders(_aifaction, __state.Alliance);
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("supply-depot-fix:postfix", "[Patch:SupplyDepotFix] builder rebuild failed (vanilla builder list kept): " + ex.Message);
                    }
                }
            }

            [HarmonyFinalizer]
            internal static void Finalizer(Scope __state)
            {
                using (TelemetryPerf.Scope("campaign.patch.supply-depot-fix.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.supply-depot-fix.finalizer");
                    if (__state == null || !__state.Entered) return;
                    _depth = Math.Max(0, _depth - 1);
                    if (_depth == 0) _friendlySourceOnly = false;
                    for (int i = 0; i < __state.Masked.Count; i++)
                    {
                        var depot = __state.Masked[i];
                        if ((UnityEngine.Object)depot != null) depot.allianceconstructionstarted = __state.MaskedStartedBy[i];
                    }
                }
            }
        }

        [HarmonyPatch(typeof(CBuilding), "EnemyUnitsInRange")]
        internal static class EnemyHeldDepot
        {
            [HarmonyPostfix]
            internal static void Postfix(CBuilding __instance, int alliancecalling, ref bool __result)
            {
                using (TelemetryPerf.Scope("campaign.patch.supply-depot-fix.enemy-held", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.supply-depot-fix.enemy-held");
                    if (_depth <= 0 || !_friendlySourceOnly || __result) return;
                    try
                    {
                        if ((UnityEngine.Object)__instance == null || __instance.BuildingType != CBuilding.id_supplydepot) return;
                        if (GameVars.alliance == null || alliancecalling < 0 || alliancecalling >= GameVars.alliance.Length) return;
                        int owner = __instance.Owner;
                        bool allied = owner >= 0 && owner < GameVars.alliance.Length && GameVars.alliance[alliancecalling].IsInAllianceWith(owner);
                        if (SupplyDepotAiRules.IsFriendlySupplySource(owner, alliancecalling, allied)) return;
                        __result = true;
                        OnceLog.Info("supply-depot-fix:enemy-depot", "[Patch:SupplyDepotFix] an enemy depot was not taken as a resupply installation");
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("supply-depot-fix:enemy-held", "[Patch:SupplyDepotFix] enemy-depot test failed (vanilla answer kept): " + ex.Message);
                    }
                }
            }
        }

        private static void MaskFinishedOwnDepots(int alliance, Scope scope)
        {
            var depots = BattleUnits.supplydepot;
            if (depots == null) return;
            for (int i = 0; i < depots.Count; i++)
            {
                var depot = depots[i];
                if ((UnityEngine.Object)depot == null) continue;
                if (!SupplyDepotAiRules.ShouldMaskFinishedOwnDepot(depot.allianceconstructionstarted, alliance, depot.constructiontimer)) continue;
                scope.Masked.Add(depot);
                scope.MaskedStartedBy.Add(depot.allianceconstructionstarted);
                depot.allianceconstructionstarted = -1;
            }
            if (scope.Masked.Count > 0)
                OnceLog.Info("supply-depot-fix:masked", "[Patch:SupplyDepotFix] finished own depots no longer stop a nearby low-supply unit from resupplying");
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
            bool same = selected.Count == builders.Count;
            for (int k = 0; same && k < selected.Count; k++)
                same = builders.Contains(ownUnits[selected[k]]);
            if (same) return;

            builders.Clear();
            for (int k = 0; k < selected.Count; k++)
                builders.Add(ownUnits[selected[k]]);
            OnceLog.Info("supply-depot-fix:builders", "[Patch:SupplyDepotFix] depot builders limited to one unit per depot inside its bugle range");
        }

        private static bool MasterEnabled()
        {
            try { return Plugin.Instance != null && Plugin.Instance.Enabled.Value; }
            catch { return false; }
        }

        private static bool FriendlySourceFixEnabled()
        {
            try { return Plugin.EnableSupplyDepotFriendlySourceFix != null && Plugin.EnableSupplyDepotFriendlySourceFix.Value; }
            catch { return false; }
        }

        private static bool BuilderFixEnabled()
        {
            try { return Plugin.EnableSupplyDepotBuilderFix != null && Plugin.EnableSupplyDepotBuilderFix.Value; }
            catch { return false; }
        }
    }
}
