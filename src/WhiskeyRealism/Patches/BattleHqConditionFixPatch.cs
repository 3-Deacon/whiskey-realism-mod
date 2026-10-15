using System;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-TAC-019. Vanilla BattleUnits.AssignConditionToSubordinates (DC:85453),
    // run once at battle frame 26 (DC:23972-23975), carries campaign fatigue into
    // the battle: for each group it takes the per-kind minimum of the group's and
    // its side's condition (array[0] men, [1] horses, [2] guns, DC:85460-85462),
    // clamps every active unit in the group by it (DC:85463-85468), then clamps
    // the group HQ (grp.regref) again - with array[0] for all three kinds
    // (DC:85469-85471). The horse and gun lines are a copy-paste slip: the HQ's
    // horse and gun condition (shown on the group panel, DC:219241-219253, and in
    // the side's gun-condition average, DC:85496) drop to the men value. It then
    // rebuilds the side averages (DC:85473-85504).
    //
    // The Prefix records, before vanilla runs, each HQ's horse/gun condition, each
    // group's horse/gun clamp, and which groups' unit pass reaches each HQ (the
    // same GetComponentsInChildren<Regiment>() call vanilla makes). The Postfix
    // sets each HQ to the value vanilla's per-kind clamps give
    // (BattleStatFixes.HqCondition) and recomputes the side gun average as
    // vanilla's own loop does. Men are untouched. A stat correction on the
    // battle's units, applied equally to both sides and to the W&L player's
    // units like vanilla's own pass; not an order, so it does not go through the
    // battle-core executor; no per-battle static state (__state only).
    // Rollback: Tactical / Enable Battle HQ Condition Fix.
    [HarmonyPatch(typeof(BattleUnits), "AssignConditionToSubordinates")]
    internal static class BattleHqConditionFixPatch
    {
        internal sealed class Snapshot
        {
            public readonly List<Regiment> Hqs = new List<Regiment>();
            public readonly List<float> HorsesBefore = new List<float>();
            public readonly List<float> GunsBefore = new List<float>();
            public readonly List<float> OwnHorseClamp = new List<float>();
            public readonly List<float> OwnGunClamp = new List<float>();
            public readonly List<float> EnclosingHorseClamp = new List<float>();
            public readonly List<float> EnclosingGunClamp = new List<float>();
        }

        [HarmonyPrefix]
        internal static void Prefix(BattleUnits __instance, out Snapshot __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.hq-condition-fix.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
            {
                PatchFirstFire.Mark("tactical.patch.hq-condition-fix.prefix");
                __state = null;
                try
                {
                    if (!Enabled() || (UnityEngine.Object)__instance == null || __instance.grp == null || __instance.sideinformation == null) return;
                    var groups = __instance.grp;
                    var state = new Snapshot();
                    var index = new Dictionary<Regiment, int>();
                    var horseClamp = new float[groups.Length];
                    var gunClamp = new float[groups.Length];

                    for (int i = 0; i < groups.Length; i++)
                    {
                        var g = groups[i];
                        int side = __instance.GetSideFromAlliance(g.alliance);
                        horseClamp[i] = Math.Min(g.conditionhorses, __instance.sideinformation[side].conditionhorses);
                        gunClamp[i] = Math.Min(g.conditionguns, __instance.sideinformation[side].conditionguns);
                        Regiment hq = g.regref;
                        if ((UnityEngine.Object)hq == null || index.ContainsKey(hq)) continue;
                        index[hq] = state.Hqs.Count;
                        state.Hqs.Add(hq);
                        state.HorsesBefore.Add(hq.conditionhorses);
                        state.GunsBefore.Add(hq.conditionguns);
                        state.OwnHorseClamp.Add(horseClamp[i]);
                        state.OwnGunClamp.Add(gunClamp[i]);
                        state.EnclosingHorseClamp.Add(float.PositiveInfinity);
                        state.EnclosingGunClamp.Add(float.PositiveInfinity);
                    }

                    // Which groups' unit pass (DC:85456, 85463-85468) reaches each HQ.
                    for (int i = 0; i < groups.Length; i++)
                    {
                        var reached = groups[i].go.GetComponentsInChildren<Regiment>();
                        for (int j = 0; j < reached.Length; j++)
                        {
                            if (!index.TryGetValue(reached[j], out int h)) continue;
                            state.EnclosingHorseClamp[h] = Math.Min(state.EnclosingHorseClamp[h], horseClamp[i]);
                            state.EnclosingGunClamp[h] = Math.Min(state.EnclosingGunClamp[h], gunClamp[i]);
                        }
                    }
                    __state = state;
                }
                catch (Exception ex)
                {
                    __state = null;
                    OnceLog.Warning("tactical.hq-condition-fix:prefix", "[Patch:HqCondition] prefix failed: " + ex.Message);
                }
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(BattleUnits __instance, Snapshot __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.hq-condition-fix.postfix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
            {
                PatchFirstFire.Mark("tactical.patch.hq-condition-fix.postfix");
                if (__state == null) return;
                try
                {
                    int corrected = 0;
                    for (int h = 0; h < __state.Hqs.Count; h++)
                    {
                        Regiment hq = __state.Hqs[h];
                        if ((UnityEngine.Object)hq == null) continue;
                        float horses = BattleStatFixes.HqCondition(__state.HorsesBefore[h], __state.EnclosingHorseClamp[h], __state.OwnHorseClamp[h]);
                        float guns = BattleStatFixes.HqCondition(__state.GunsBefore[h], __state.EnclosingGunClamp[h], __state.OwnGunClamp[h]);
                        if (hq.conditionhorses != horses || hq.conditionguns != guns) corrected++;
                        hq.conditionhorses = horses;
                        hq.conditionguns = guns;
                    }
                    if (corrected == 0) return;

                    RecomputeSideGunCondition(__instance);
                    OnceLog.Info("tactical.hq-condition-fix:first",
                        "[Patch:HqCondition] group HQ horse/gun condition taken from the horse/gun clamp, not the men clamp: hqsCorrected=" + corrected);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical.hq-condition-fix:postfix", "[Patch:HqCondition] postfix failed: " + ex.Message);
                }
            }
        }

        // Vanilla's side aggregate for guns (DC:85480-85504): strength-weighted over
        // every unit of each battledata group, divided by the basic units' strength.
        private static void RecomputeSideGunCondition(BattleUnits bunits)
        {
            var groups = bunits.grp;
            var sides = bunits.sideinformation;
            if (sides == null || sides.Length < 2) return;
            float[] guns = new float[2];
            float[] basicStrength = new float[2];
            for (int k = 0; k < groups.Length; k++)
            {
                if (!groups[k].isbattledataunit) continue;
                int side = bunits.GetSideFromAlliance(groups[k].alliance);
                if (side < 0 || side > 1) continue;
                var units = groups[k].go.GetComponentsInChildren<Regiment>();
                for (int l = 0; l < units.Length; l++)
                {
                    if (units[l].unittyp <= 13) basicStrength[side] += units[l].strength;
                    guns[side] += units[l].strength * units[l].conditionguns;
                }
            }
            for (int m = 0; m < 2; m++)
                sides[m].conditionguns = guns[m] / Math.Max(1f, basicStrength[m]);
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableBattleHqConditionFix != null && Plugin.EnableBattleHqConditionFix.Value;
            }
            catch { return false; }
        }
    }
}
