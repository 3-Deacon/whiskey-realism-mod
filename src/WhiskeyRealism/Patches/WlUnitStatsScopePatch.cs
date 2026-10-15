using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Regiment.UpdateCampaignUnitStats(groupunit, state) (DC:114965) runs
    // for every regiment of every campaign group (DC:120260, 120409) and, in a W&L
    // career, applies the player's effects to that regiment. One concern, which
    // regiments get the player's effects here, two defects:
    //
    // BUG-WL-005 (too wide). Sickness and desertion multiply by
    // DLC_WL.EffectMultiplier(22) with the default isplayerunit = true
    // (DC:115149-115151, 115196-115198), so the player's attrition effect (Camp
    // Rules x0.5, Provost Marshal-General) lowers attrition for both armies.
    // Medical care, training and readiness scope theirs by
    // groupunit.dlcw_isundercommander (DC:115107, 115243, 115276-115279) and the
    // attrition tooltip is per unit (DC:173552). For a regiment whose group is
    // not the player's, the Prefix sets cumulativeeffectsrunning[22] to 1
    // (vanilla's own "not the player's unit" value, DC:44635-44641).
    //
    // BUG-WL-011 (too narrow). Those group-keyed effects (medical 15, training 20,
    // readiness 21, camp station 11) never reach a player whose command is not
    // itself a map group: groupunit is then the AI parent, which
    // IsUnitUnderCommander never flags (DC:91203-91217). While vanilla works on
    // one of the player's own regiments inside the map group carrying the
    // player's command (WlPlayerMapGroup), the Prefix sets that group's
    // dlcw_isundercommander for the call, so the player's regiment gets every
    // effect vanilla gives a map-group player's regiment (attrition included).
    // AI peers in the group keep vanilla values. Readiness, medical and training
    // multiply rates in the right direction (115107-115108, 115243, 115276), so
    // no sign question arises here (contrast BUG-WL-008).
    //
    // Non-skipping Prefix; the Finalizer restores the flag and the effect slot
    // even if vanilla throws. Rollback: W&L / Enable W&L Attrition Scope Fix,
    // Enable W&L Staff Scope Fix.
    [HarmonyPatch(typeof(Regiment), "UpdateCampaignUnitStats")]
    internal static class WlUnitStatsScopePatch
    {
        internal struct Snapshot
        {
            public Regiment RaisedGroup;
            public float[] Array;
            public float Saved;
            public bool AttritionWritten;
        }

        [HarmonyPrefix]
        internal static void Prefix(Regiment __instance, Regiment groupunit, out Snapshot __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.wl-unit-stats-scope.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-unit-stats-scope.prefix");
                __state = default;
                try
                {
                    if (!DLC_WL.dlc_scenarioactive || (UnityEngine.Object)groupunit == null || (UnityEngine.Object)__instance == null) return;
                    if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;

                    bool groupOwned = groupunit.dlcw_isundercommander;
                    if (Flag(Plugin.EnableWlStaffScopeFix)
                        && WlEconomyFixes.RaiseGroupForPlayerUnit(true, groupOwned, __instance.dlcw_isundercommander, WlPlayerMapGroup.Is(groupunit)))
                    {
                        groupunit.dlcw_isundercommander = true;
                        __state.RaisedGroup = groupunit;
                        groupOwned = true;
                        OnceLog.Info("wl-unit-stats-scope:first",
                            "[Patch:WlUnitStatsScope] player's medical, training, readiness and attrition effects applied to the player's regiment inside group=" + groupunit.name);
                    }

                    if (!WlEconomyFixes.NeutralizeAttritionBonus(true, groupOwned) || !Flag(Plugin.EnableWlAttritionScopeFix)) return;

                    float[] running = WlEffectArrays.Running();
                    if (!WlEffectArrays.TryGet(running, WlEconomyFixes.EffectAttrition, out float value) || value == 1f) return;

                    running[WlEconomyFixes.EffectAttrition] = 1f;
                    __state.Array = running;
                    __state.Saved = value;
                    __state.AttritionWritten = true;
                    OnceLog.Info("wl-attrition-scope:first",
                        "[Patch:WlAttritionScope] player attrition effect x" + value.ToString("0.###") + " withheld from a group the player does not own");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-unit-stats-scope:prefix", "[Patch:WlUnitStatsScope] prefix failed: " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer(Snapshot __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.wl-unit-stats-scope.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-unit-stats-scope.finalizer");
                try
                {
                    if ((UnityEngine.Object)__state.RaisedGroup != null)
                        __state.RaisedGroup.dlcw_isundercommander = false;

                    if (!__state.AttritionWritten) return;
                    float[] running = WlEffectArrays.Running();
                    bool same = ReferenceEquals(running, __state.Array);
                    if (WlEconomyFixes.ShouldRestore(same, same ? running[WlEconomyFixes.EffectAttrition] : float.NaN, 1f))
                        running[WlEconomyFixes.EffectAttrition] = __state.Saved;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-unit-stats-scope:finalizer", "[Patch:WlUnitStatsScope] restore failed: " + ex.Message);
                }
            }
        }

        private static bool Flag(BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.Value; }
            catch { return false; }
        }
    }
}
