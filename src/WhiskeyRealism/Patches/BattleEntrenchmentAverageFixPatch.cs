using System;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-TAC-018. Vanilla BattleUnits.CalculateAllUnitsOfAlliance (DC:87335)
    // sums the readiness and entrenchment level of each side's on-field
    // battledata groups, counting the player side in num and the other side in
    // num2 (DC:87378-87392), then divides: readiness by num, enemyreadiness by
    // num2, entrenchmentlevel by num - and enemyentrenchmentlevel by num as well
    // (DC:87396-87399), a copy-paste slip. enemyentrenchmentlevel picks the AI's
    // battle fortification tier (barricades vs trench level, DC:85632-85642) and
    // scales its engineering time (ResetEngineeringPoints DC:87480-87481), both
    // read after this method returns. With more AI groups than player groups the
    // AI over-builds; with fewer it under-builds.
    //
    // This Postfix recomputes the AI side's average with vanilla's own predicate
    // over its own count. A derived aggregate on BattleUnits, not a battle order
    // (no movement, stance, formation, facing or combat behavior), so it does not
    // go through the battle-core executor and does not depend on Tactical
    // Commander Mode; no per-battle state. Rollback: Tactical / Enable Battle
    // Entrenchment Average Fix.
    [HarmonyPatch(typeof(BattleUnits), "CalculateAllUnitsOfAlliance")]
    internal static class BattleEntrenchmentAverageFixPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(BattleUnits __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.entrenchment-average-fix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.entrenchment-average-fix");
                try
                {
                    if (!Enabled() || (UnityEngine.Object)__instance == null || __instance.grp == null) return;

                    float sum = 0f;
                    int count = 0;
                    var groups = __instance.grp;
                    for (int k = 0; k < groups.Length; k++)
                    {
                        var g = groups[k];
                        if (g == null || (UnityEngine.Object)g.go == null) continue;
                        Regiment component = g.go.GetComponent<Regiment>();
                        if ((UnityEngine.Object)component == null || component.unittyp <= 13) continue;
                        if (!BattleStatFixes.CountsForEnemyAverage(g.reinforcementdelay, g.alliance == GameVars.playeralliance, g.isbattledataunit)) continue;
                        sum += g.entrenchmentlevel;
                        count++;
                    }

                    float corrected = BattleStatFixes.SideAverage(sum, count);
                    float vanilla = __instance.enemyentrenchmentlevel;
                    if (corrected == vanilla) return;
                    __instance.enemyentrenchmentlevel = corrected;
                    OnceLog.Info("tactical.entrenchment-average-fix:first",
                        "[Patch:EntrenchmentAverage] AI-side entrenchment level averaged over its own groups: vanilla=" + vanilla.ToString("0.###")
                        + " corrected=" + corrected.ToString("0.###") + " aiGroups=" + count);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical.entrenchment-average-fix:postfix", "[Patch:EntrenchmentAverage] postfix failed: " + ex.Message);
                }
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableBattleEntrenchmentAverageFix != null && Plugin.EnableBattleEntrenchmentAverageFix.Value;
            }
            catch { return false; }
        }
    }
}
