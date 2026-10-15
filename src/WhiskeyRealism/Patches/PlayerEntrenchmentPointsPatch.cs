using System;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-TAC-021. BattleUnits.ResetEngineeringPoints (87465), battle start and redeployment:
    // the player's pool is engineers x readiness^k (87475), plus
    // entrenchmentlevel x totalstrength x k x entrenchmentlevel (87476), times
    // 1 + (effect - 1) x entrenchmentlevel (87477); the AI's is the same with the entrenchment
    // term linear, totalstrength x k x enemyentrenchmentlevel (87480-87481). battleprefs.txt
    // 622 "Influence of entrenchment level on engineering points (based on unit strength)" is
    // one linear factor, and entrenchment is at most the alliance's military experience
    // (115450-115453), so the extra factor only shrinks the player's term. The Postfix adds
    // the difference to bui.deploymentavailabletime (TerrainBugFixes.LinearEntrenchmentDelta),
    // from the same fields vanilla just used. It composes with #98
    // BattleEntrenchmentAverageFixPatch (BUG-TAC-018), which corrects the AI side's average in
    // CalculateAllUnitsOfAlliance (called at 87469) and so the AI term; this patch touches
    // only the player term. Every side's player-facing pool, W&L included (vanilla's rule).
    // Rollback: Tactical / Fix Player Entrenchment Points.
    [HarmonyPatch(typeof(BattleUnits), nameof(BattleUnits.ResetEngineeringPoints))]
    internal static class PlayerEntrenchmentPointsPatch
    {
        private static Func<object, BattleUI> _bui;
        private static bool _buiResolved;
        private static bool _logged;

        static PlayerEntrenchmentPointsPatch()
        {
            BattleScope.Register("player-entrenchment-points-log", () => _logged = false);
        }

        internal static bool Enabled()
        {
            try { return Plugin.FixPlayerEntrenchmentPoints != null && Plugin.FixPlayerEntrenchmentPoints.Value; }
            catch { return false; }
        }

        [HarmonyPostfix]
        internal static void Postfix(BattleUnits __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.player-entrenchment-points", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-entrenchment-points");
                try
                {
                    if (!Enabled() || (UnityEngine.Object)__instance == null || __instance.allunitsofalliance == null) return;
                    BattleUI bui = Bui(__instance);
                    if ((UnityEngine.Object)bui == null) return;
                    int side = __instance.GetSideFromAlliance(GameVars.playeralliance);
                    if (side < 0 || side >= __instance.allunitsofalliance.Length || __instance.allunitsofalliance[side] == null) return;
                    float delta = TerrainBugFixes.LinearEntrenchmentDelta(__instance.entrenchmentlevel, __instance.allunitsofalliance[side].totalstrength,
                        GamePrefs.influenceofentrenchmentlevelonengineeringpoints, GamePrefs.effectentrenchmentonengineeringpoints);
                    if (delta == 0f) return;
                    bui.deploymentavailabletime += delta;
                    if (_logged) return;
                    _logged = true;
                    OnceLog.Info("player-entrenchment-points", "[Patch:PlayerEntrenchment] player engineering points: entrenchment term linear (+" + delta.ToString("0.#") + ")");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("player-entrenchment-points:postfix", "[Patch:PlayerEntrenchment] ResetEngineeringPoints postfix failed, vanilla points kept: " + ex.Message);
                }
            }
        }

        // BattleUnits.bui is private (78976); read through a compiled getter, resolved once.
        private static BattleUI Bui(BattleUnits units)
        {
            if (!_buiResolved)
            {
                _buiResolved = true;
                FieldInfo field = AccessTools.Field(typeof(BattleUnits), "bui");
                _bui = CompiledFieldGetter.Build<BattleUI>(field);
                if (_bui == null)
                    OnceLog.Warning("player-entrenchment-points:bui", "[Patch:PlayerEntrenchment] BattleUnits.bui not found; vanilla points kept");
            }
            return _bui != null ? _bui(units) : null;
        }
    }
}
