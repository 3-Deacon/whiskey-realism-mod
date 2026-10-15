using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-LOG-003. Vanilla AICampaign.UpdateImportanceValues (decompile 14905-14998) runs
    // once per UpdateUnitAI faction-job pass (11394) and walks one BattleUnits.iips, one cbuildings
    // and one towns entry per call (cursors currentiip/currentcbuild/currenttown). It adds
    // each to the AIArea under its position, AIArea.GetAIArea(aiareas.GetColorOnPos(pos)),
    // but only while airuns <= 1 (14927, 14946, 14965). Buildings created later (supply
    // depots, forts, companies) never join their area: AIArea.UpdateSupplyCapacity
    // (10772-10801, job 4 at 11415) never counts a new depot, the area supply gate
    // ownpoints <= supplycapacity (10983) stays at its early value, and
    // GetClosestObjectivePosOfArea (14542-14658) never targets the new building.
    //
    // The Prefix reads the three cursors; the Postfix, once vanilla has stopped
    // registering (airuns > 1), adds the entries vanilla just walked to their areas with
    // the same rule and no duplicates, and at the end of each full walk (vanilla returns
    // true) drops members that were destroyed (Unity-null; vanilla's own objective pick
    // already skips null buildings, 14627). The area lists are owner-blind, as in vanilla.
    // Rollback: Campaign Bug Fixes / Enable AI Area Membership Refresh.
    [HarmonyPatch(typeof(AICampaign), "UpdateImportanceValues")]
    internal static class AIAreaMembershipRefreshPatch
    {
        internal struct Cursor
        {
            internal bool Valid;
            internal int Iip;
            internal int Building;
            internal int Town;
        }

        private static FieldInfo _iipCursor;
        private static FieldInfo _buildingCursor;
        private static FieldInfo _townCursor;
        private static FieldInfo _aiRuns;
        private static bool _resolved;

        [HarmonyPrefix]
        internal static void Prefix(AICampaign __instance, ref Cursor __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.area-membership.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.area-membership.prefix");
                __state = default(Cursor);
                try
                {
                    if (__instance == null || !Resolve()) return;
                    if (!AIAreaMembership.ShouldRegister(Enabled(), (int)_aiRuns.GetValue(null))) return;
                    __state.Iip = (int)_iipCursor.GetValue(__instance);
                    __state.Building = (int)_buildingCursor.GetValue(__instance);
                    __state.Town = (int)_townCursor.GetValue(__instance);
                    __state.Valid = true;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("area-membership:prefix", "[Patch:AreaMembership] prefix failed (vanilla membership kept): " + ex.Message);
                }
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(bool __result, Cursor __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.area-membership.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.area-membership.postfix");
                if (!__state.Valid) return;
                try
                {
                    var iips = BattleUnits.iips;
                    if (iips != null && __state.Iip >= 0 && __state.Iip < iips.Count && (UnityEngine.Object)iips[__state.Iip] != null)
                    {
                        var area = AreaAt(((Component)iips[__state.Iip]).gameObject.transform.position);
                        if (area != null && AIAreaMembership.AddIfMissing(area.iips, iips[__state.Iip]))
                            OnceLog.Info("area-membership:iip", "[Patch:AreaMembership] registered an IIP into its AI area after vanilla's second AI run");
                    }

                    var buildings = BattleUnits.cbuildings;
                    if (buildings != null && __state.Building >= 0 && __state.Building < buildings.Count && (UnityEngine.Object)buildings[__state.Building] != null)
                    {
                        var area = AreaAt(((Component)buildings[__state.Building]).gameObject.transform.position);
                        if (area != null && AIAreaMembership.AddIfMissing(area.cbuildings, buildings[__state.Building]))
                            OnceLog.Info("area-membership:building", "[Patch:AreaMembership] registered a building created after vanilla's second AI run into its AI area");
                    }

                    var towns = BattleUnits.towns;
                    if (towns != null && __state.Town >= 0 && __state.Town < towns.Count && (UnityEngine.Object)towns[__state.Town] != null)
                    {
                        var area = AreaAt(((Component)towns[__state.Town]).gameObject.transform.position);
                        if (area != null && AIAreaMembership.AddIfMissing(area.towns, towns[__state.Town]))
                            OnceLog.Info("area-membership:town", "[Patch:AreaMembership] registered a town into its AI area after vanilla's second AI run");
                    }

                    if (__result) PruneDestroyed();
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("area-membership:postfix", "[Patch:AreaMembership] postfix failed: " + ex.Message);
                }
            }
        }

        private static void PruneDestroyed()
        {
            var areas = AICampaign.aiarea;
            if (areas == null) return;
            int removed = 0;
            for (int i = 0; i < areas.Count; i++)
            {
                var area = areas[i];
                if (area == null) continue;
                removed += AIAreaMembership.RemoveWhere(area.cbuildings, b => (UnityEngine.Object)b == null);
                removed += AIAreaMembership.RemoveWhere(area.iips, p => (UnityEngine.Object)p == null);
                removed += AIAreaMembership.RemoveWhere(area.towns, t => (UnityEngine.Object)t == null);
            }
            if (removed > 0)
                OnceLog.Info("area-membership:pruned", "[Patch:AreaMembership] dropped destroyed buildings from their AI areas");
        }

        // Vanilla's membership rule (14915, 14935, 14954).
        private static AICampaign.AIArea AreaAt(Vector3 position)
        {
            if (AICampaign.aiareas == null) return null;
            return AICampaign.AIArea.GetAIArea(AICampaign.aiareas.GetColorOnPos(position));
        }

        private static bool Resolve()
        {
            if (_resolved) return _iipCursor != null && _buildingCursor != null && _townCursor != null && _aiRuns != null;
            _resolved = true;
            _iipCursor = AccessTools.Field(typeof(AICampaign), "currentiip");
            _buildingCursor = AccessTools.Field(typeof(AICampaign), "currentcbuild");
            _townCursor = AccessTools.Field(typeof(AICampaign), "currenttown");
            _aiRuns = AccessTools.Field(typeof(AICampaign), "airuns");
            bool ok = _iipCursor != null && _buildingCursor != null && _townCursor != null && _aiRuns != null;
            if (!ok)
                OnceLog.Warning("area-membership:reflect", "[Patch:AreaMembership] AICampaign cursor/airuns fields not found; area membership stays vanilla");
            return ok;
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableAiAreaMembershipRefresh != null
                    && Plugin.EnableAiAreaMembershipRefresh.Value;
            }
            catch { return false; }
        }
    }
}
