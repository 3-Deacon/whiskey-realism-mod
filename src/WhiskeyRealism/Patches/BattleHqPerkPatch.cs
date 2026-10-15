using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Battle HQs (division/corps/army, unittyp 14-16) carry their campaign perks into battle
    // (BattleUnits.import_units 88579-88632), and these Regiment methods read battle ids with no
    // unit-type guard (BUG-AI-009):
    // - CheckAbilityToRun (113252): HasPerk(2) Zouave / HasPerk(11) Stormers let the unit run;
    //   an HQ's 2/11 are Balloon Corps / Land Torpedoes.
    // - Initialize (112435, 113160-113169) and ChoosePerk (123381, 123411): HasPerk(0) /
    //   perk 0 Engineering sets hasengineers in battle; an HQ's 0 is Siege Train.
    // - UpdateFatigue (132636, 132665): Zouave fatigue factor on HasPerk(2).
    // - GetCoverValueFromID (138514): Lightning Brigade 13, Sapper Gunners 18 and Ace of Spade 6
    //   cover factors; an HQ's 13/6 are Bushwhackers / Foot Cavalry.
    // The flags are fields, so Postfixes recompute them as vanilla does without the perk. The
    // two factors are locals, so Prefixes hide the HQ's perks for the call (neither method reads
    // another perk) and Finalizers restore them even if vanilla throws. Battle-table units are
    // never touched. Rollback: Campaign / Fix Perk Id Mismatches.
    internal static class BattleHqPerkPatch
    {
        private static bool NonBattleTable(Regiment unit)
        {
            return (UnityEngine.Object)unit != null && !VanillaPerkTables.UsesBattleTable(unit.unittyp) && PerkIdIlHooks.FixIds();
        }

        [HarmonyPatch(typeof(Regiment), "CheckAbilityToRun")]
        internal static class AbilityToRun
        {
            [HarmonyPostfix]
            internal static void Postfix(Regiment __instance)
            {
                using (TelemetryPerf.Scope("tactical.patch.perk-ids.hq.ability-to-run", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("tactical.patch.perk-ids.hq.ability-to-run");
                    try
                    {
                        if (__instance.calledfromcampaign || !__instance.isabletorun || !NonBattleTable(__instance)) return;
                        if (!__instance.HasPerk(VanillaPerkTables.BattleZouave) && !__instance.HasPerk(VanillaPerkTables.BattleStormers)) return;
                        bool[] ableToRun = GamePrefs.abletorun;
                        int type = __instance.unittyp;
                        bool vanilla = ableToRun != null && type >= 0 && type < ableToRun.Length && ableToRun[type];
                        if (vanilla) return;
                        __instance.isabletorun = false;
                        OnceLog.Info("perk-ids:hq-run", "[Patch:PerkIds] CheckAbilityToRun: running withheld from a battle HQ's campaign perk 2/11 (Balloon Corps / Land Torpedoes)");
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("perk-ids:hq-run:postfix", "[Patch:PerkIds] CheckAbilityToRun postfix failed: " + ex.Message);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Regiment), nameof(Regiment.Initialize), new[] { typeof(bool) })]
        internal static class EngineersOnInitialize
        {
            [HarmonyPostfix]
            internal static void Postfix(Regiment __instance)
            {
                using (TelemetryPerf.Scope("tactical.patch.perk-ids.hq.engineers-init", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("tactical.patch.perk-ids.hq.engineers-init");
                    ClearHqEngineers(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(Regiment), nameof(Regiment.ChoosePerk))]
        internal static class EngineersOnChoosePerk
        {
            [HarmonyPostfix]
            internal static void Postfix(Regiment __instance, int newperk)
            {
                using (TelemetryPerf.Scope("tactical.patch.perk-ids.hq.engineers-choose", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("tactical.patch.perk-ids.hq.engineers-choose");
                    if (newperk == VanillaPerkTables.BattleEngineering) ClearHqEngineers(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(Regiment), "UpdateFatigue")]
        internal static class ZouaveFatigue
        {
            private static readonly PerkListSwap Swap = new PerkListSwap("perk-ids:hq-fatigue:restore");

            [HarmonyPrefix]
            internal static void Prefix(Regiment __instance)
            {
                using (TelemetryPerf.Scope("tactical.patch.perk-ids.hq.fatigue.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("tactical.patch.perk-ids.hq.fatigue.prefix");
                    try
                    {
                        Swap.Restore();
                        if (__instance.perks == null || !NonBattleTable(__instance)) return;
                        if (!__instance.HasPerk(VanillaPerkTables.BattleZouave)) return;
                        Swap.Hide(__instance);
                        OnceLog.Info("perk-ids:hq-fatigue", "[Patch:PerkIds] UpdateFatigue: Zouave fatigue factor withheld from a campaign perk 2 (Balloon Corps)");
                    }
                    catch (Exception ex)
                    {
                        Swap.Restore();
                        OnceLog.Warning("perk-ids:hq-fatigue:prefix", "[Patch:PerkIds] UpdateFatigue prefix failed, vanilla left as is: " + ex.Message);
                    }
                }
            }

            [HarmonyFinalizer]
            internal static void Finalizer()
            {
                using (TelemetryPerf.Scope("tactical.patch.perk-ids.hq.fatigue.finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("tactical.patch.perk-ids.hq.fatigue.finalizer");
                    if (Swap.Active) Swap.Restore();
                }
            }
        }

        [HarmonyPatch(typeof(Regiment), "GetCoverValueFromID")]
        internal static class CoverFactors
        {
            private static readonly PerkListSwap Swap = new PerkListSwap("perk-ids:hq-cover:restore");

            [HarmonyPrefix]
            internal static void Prefix(Regiment __instance)
            {
                using (TelemetryPerf.Scope("tactical.patch.perk-ids.hq.cover.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("tactical.patch.perk-ids.hq.cover.prefix");
                    try
                    {
                        Swap.Restore();
                        if (__instance.perks == null || !NonBattleTable(__instance)) return;
                        if (!__instance.HasPerk(VanillaPerkTables.BattleLightningBrigade)
                            && !__instance.HasPerk(VanillaPerkTables.BattleSapperGunners)
                            && !__instance.HasPerk(VanillaPerkTables.BattleAceOfSpade))
                            return;
                        Swap.Hide(__instance);
                        OnceLog.Info("perk-ids:hq-cover", "[Patch:PerkIds] GetCoverValueFromID: battle cover perks withheld from a campaign perk 6/13 (Foot Cavalry / Bushwhackers)");
                    }
                    catch (Exception ex)
                    {
                        Swap.Restore();
                        OnceLog.Warning("perk-ids:hq-cover:prefix", "[Patch:PerkIds] GetCoverValueFromID prefix failed, vanilla left as is: " + ex.Message);
                    }
                }
            }

            [HarmonyFinalizer]
            internal static void Finalizer()
            {
                using (TelemetryPerf.Scope("tactical.patch.perk-ids.hq.cover.finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("tactical.patch.perk-ids.hq.cover.finalizer");
                    if (Swap.Active) Swap.Restore();
                }
            }
        }

        // Vanilla sets hasengineers from HasPerk(0) only when !calledfromcampaign (113160-113169,
        // 123411); for a battle HQ that 0 is Siege Train.
        private static void ClearHqEngineers(Regiment unit)
        {
            try
            {
                if (unit.calledfromcampaign || !unit.hasengineers || !NonBattleTable(unit)) return;
                if (!unit.HasPerk(VanillaPerkTables.BattleEngineering)) return;
                unit.hasengineers = false;
                OnceLog.Info("perk-ids:hq-engineers", "[Patch:PerkIds] hasengineers withheld from a battle HQ's campaign perk 0 (Siege Train)");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("perk-ids:hq-engineers", "[Patch:PerkIds] hasengineers postfix failed: " + ex.Message);
            }
        }
    }
}
