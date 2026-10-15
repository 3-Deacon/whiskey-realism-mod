using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla FogOfWar.UnitIsHidden (decompile 101179), called from
    // Regiment.CheckRegimentVisibility (127225/127229) on the campaign map and the
    // battlefield, lowers the spotting threshold by perk_expertskirmishersconcealment when
    // regref.HasPerk(3) and perk_expertscoutsconcealment when regref.HasPerk(8)
    // (101205-101214). Those are the battle-table Expert Skirmishers / Expert Scouts ids; on a
    // campaign group (and a battle HQ carrying its campaign perks) 3/8 are Bureau of Military
    // Information / Ambulance Corps and on a fleet Balloon Barge / Blockade Busters, none of
    // which promises concealment (BUG-AI-004). regref is read for nothing else, so this
    // non-skipping Prefix passes null for a unit that is not on the battle perk table; vanilla
    // then computes the same result minus the wrong-table bonus. Battle units are untouched.
    // Rollback: Campaign / Fix Perk Id Mismatches.
    [HarmonyPatch(typeof(FogOfWar), nameof(FogOfWar.UnitIsHidden))]
    internal static class FogPerkConcealmentPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(ref Regiment regref)
        {
            using (TelemetryPerf.Scope("campaign.patch.perk-ids.fog-concealment", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.perk-ids.fog-concealment");
                try
                {
                    if ((UnityEngine.Object)regref == null || !Enabled()) return;
                    if (VanillaPerkTables.UsesBattleTable(regref.unittyp)) return;
                    if (!regref.HasPerk(VanillaPerkTables.BattleExpertSkirmishers) && !regref.HasPerk(VanillaPerkTables.BattleExpertScouts)) return;

                    regref = null;
                    OnceLog.Info("perk-ids:fog-concealment", "[Patch:PerkIds] UnitIsHidden: battle concealment withheld from a non-battle perk id 3/8 (Bureau of Military Information / Ambulance Corps / Balloon Barge / Blockade Busters)");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("perk-ids:fog-concealment:prefix", "[Patch:PerkIds] fog concealment prefix failed, vanilla left as is: " + ex.Message);
                }
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.FixPerkIdMismatches != null && Plugin.FixPerkIdMismatches.Value;
            }
            catch { return false; }
        }
    }
}
