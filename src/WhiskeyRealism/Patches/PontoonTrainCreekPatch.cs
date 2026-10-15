using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-AI-013. Campaign perk 9 Pontoon Train promises "Movement speed across small rivers
    // +25%/+50%/+75%" (PerkTooltipsArmyCampaign.txt 114/118/122). Vanilla:
    // - Regiment.AddPath (130419-130431) multiplies the "Small Rivers" (creek, area 3) path cost
    //   by movementcostpontoontrain[level] (campaignprefs.txt 2540-2545: 0.75/0.5/0.25, labelled
    //   "Movement cost crossing creeks"), the parent's level replacing the unit's own;
    // - Regiment.GetTerrainSpeedAdjustment (132888-132900), the speed term RegimentMove applies
    //   (133535), returns 1/terrainspeccost[3] = 1/50 on a creek with no perk term, and no other
    //   speed factor reads perk 9.
    // So the perk makes the pathfinder prefer creeks the army still crawls through.
    // Speed: a Postfix multiplies GetTerrainSpeedAdjustment's result on a creek cell by the
    //   tooltip factor (TerrainBugFixes.PontoonCreekSpeedMultiplier) for a campaign-table group
    //   holding the perk, by AddPath's precedence. The method runs per unit per frame (also in
    //   battle), so the Postfix returns on its first field compare unless the unit stands on a
    //   creek on the campaign map; only then does it open its perf scope.
    // Route: vanilla moves at 1/cost and prices at cost (55843, 132900); a Prefix points
    //   movementcostpontoontrain at 1/factor for a campaign-table group's AddPath call so routes
    //   are priced at their real travel time, and a Finalizer restores it.
    // Every side, the W&L player's command included (the tooltip rule). Rollback: Campaign /
    // Fix Pontoon Train Creek Speed.
    internal static class PontoonTrainCreekPatch
    {
        internal static bool Enabled()
        {
            try { return Plugin.FixPontoonTrainCreekSpeed != null && Plugin.FixPontoonTrainCreekSpeed.Value; }
            catch { return false; }
        }

        [HarmonyPatch(typeof(Regiment), nameof(Regiment.GetTerrainSpeedAdjustment))]
        internal static class CreekSpeed
        {
            // The proof line is formatted once per campaign, not per call (per frame).
            private static bool _logged;

            static CreekSpeed()
            {
                CampaignScope.Register("pontoon-creek-speed-log", () => _logged = false);
            }

            [HarmonyPostfix]
            internal static void Postfix(Regiment __instance, ref float __result)
            {
                // Per unit per frame: one field compare before anything else.
                if (__instance.currentterrain != TerrainBugFixes.CreekArea || !__instance.calledfromcampaign) return;
                using (TelemetryPerf.Scope("campaign.patch.pontoon-creek.speed", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.pontoon-creek.speed");
                    try
                    {
                        if (!TerrainBugFixes.PontoonApplies(Enabled(), true, __instance.unittyp)) return;
                        int level = TerrainBugFixes.PontoonLevel(
                            __instance.HasPerk(TerrainBugFixes.CampaignPontoonTrain), __instance.GetPerkLevel(TerrainBugFixes.CampaignPontoonTrain),
                            __instance.HasPerkParent(TerrainBugFixes.CampaignPontoonTrain), __instance.GetPerkLevelParent(TerrainBugFixes.CampaignPontoonTrain));
                        float factor = TerrainBugFixes.PontoonCreekSpeedMultiplier(true, true, __instance.unittyp, __instance.currentterrain, level);
                        if (factor == 1f) return;
                        __result *= factor;
                        if (!_logged)
                        {
                            _logged = true;
                            OnceLog.Info("pontoon-creek:speed", "[Patch:Pontoon] creek crossing speed x" + factor + " for a Pontoon Train group (level " + (level + 1) + ")");
                        }
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("pontoon-creek:speed:postfix", "[Patch:Pontoon] creek speed postfix failed, vanilla speed kept: " + ex.Message);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Regiment), "AddPath")]
        internal static class CreekPathCost
        {
            private static readonly float[] PathCost = NewPathCost();
            private static readonly StaticRefSwap<float[]> Swap = new StaticRefSwap<float[]>(
                () => GamePrefs.movementcostpontoontrain, v => GamePrefs.movementcostpontoontrain = v);

            private static float[] NewPathCost()
            {
                var cost = new float[TerrainBugFixes.PontoonLevels];
                TerrainBugFixes.FillPontoonPathCost(cost);
                return cost;
            }

            [HarmonyPrefix]
            internal static void Prefix(Regiment __instance)
            {
                using (TelemetryPerf.Scope("campaign.patch.pontoon-creek.path.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.pontoon-creek.path.prefix");
                    try
                    {
                        LogRestore(Swap.Restore());
                        if (!TerrainBugFixes.PontoonApplies(Enabled(), __instance.calledfromcampaign, __instance.unittyp)) return;
                        float[] vanilla = GamePrefs.movementcostpontoontrain;
                        if (vanilla == null || vanilla.Length != PathCost.Length) return;
                        Swap.Set(PathCost);
                        OnceLog.Info("pontoon-creek:path", "[Patch:Pontoon] creek path cost for Pontoon Train groups priced at 1/speed");
                    }
                    catch (Exception ex)
                    {
                        LogRestore(Swap.Restore());
                        OnceLog.Warning("pontoon-creek:path:prefix", "[Patch:Pontoon] AddPath prefix failed, vanilla path cost kept: " + ex.Message);
                    }
                }
            }

            [HarmonyFinalizer]
            internal static void Finalizer()
            {
                using (TelemetryPerf.Scope("campaign.patch.pontoon-creek.path.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.pontoon-creek.path.finalizer");
                    if (Swap.Active) LogRestore(Swap.Restore());
                }
            }

            private static void LogRestore(string failure)
            {
                if (failure != null)
                    OnceLog.Warning("pontoon-creek:path:restore", "[Patch:Pontoon] path cost restore failed: " + failure);
            }
        }
    }
}
