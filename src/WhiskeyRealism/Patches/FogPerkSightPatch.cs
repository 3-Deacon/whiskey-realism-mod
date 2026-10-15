using System;
using System.Collections;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla FogOfWar.UpdateViewRange (decompile 101274) paints each unit's sight on
    // the fog grid: maximumlineofsight[unittyp], times perk_expertscoutsvisibilityrangefactor
    // when reg.HasPerk(8) (101298-101301), on the campaign map and the battlefield alike.
    // Id 8 is Expert Scouts only on the battle perk table; a campaign group (and a battle HQ,
    // which carries its campaign perks) has Ambulance Corps there and a fleet Blockade Busters
    // (BUG-AI-003, VanillaPerkTables). The factor is a local inside the per-unit loop, so no
    // Postfix can correct it: this Prefix points the perks list of each non-battle-table unit
    // holding id 8 at an empty list for the length of the call, and the Finalizer restores
    // every list even if vanilla throws. UpdateViewRange reads nothing else from perks.
    // Battle-table units keep the bonus; the W&L incident-63 range and hqinthesaddle rules are
    // untouched. Rollback: Campaign / Fix Perk Id Mismatches.
    [HarmonyPatch(typeof(FogOfWar), "UpdateViewRange")]
    internal static class FogPerkSightPatch
    {
        private static readonly PerkListSwap<Regiment, Regiment.Perk> Swap = PerkIdIlHooks.NewPerkSwap();

        // Compiled once (CompiledFieldGetter): no FieldInfo.GetValue per call.
        private static bool _resolved;
        private static Func<object, IList> _units;
        private static Func<object, Regiment> _unitReg;

        [HarmonyPrefix]
        internal static void Prefix(FogOfWar __instance)
        {
            using (TelemetryPerf.Scope("campaign.patch.perk-ids.fog-sight.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.perk-ids.fog-sight.prefix");
                try
                {
                    PerkIdIlHooks.LogRestore(Swap.Restore(), "perk-ids:fog-sight:restore");
                    if (!Enabled() || !GameVars.usefow || !Resolve()) return;
                    IList units = _units(__instance);
                    if (units == null) return;

                    for (int i = 0; i < units.Count; i++)
                    {
                        object unit = units[i];
                        if (unit == null) continue;
                        Regiment reg = _unitReg(unit);
                        if ((UnityEngine.Object)reg == null || reg.perks == null) continue;

                        bool hasEight = reg.HasPerk(VanillaPerkTables.BattleExpertScouts);
                        if (VanillaPerkTables.HideExpertScoutsSight(PerkIdIlHooks.FixIds(), reg.unittyp, hasEight))
                            Swap.Hide(reg);
                    }

                    if (Swap.Active)
                        OnceLog.Info("perk-ids:fog-sight", "[Patch:PerkIds] UpdateViewRange: Expert Scouts sight withheld from a non-battle perk id 8 (Ambulance Corps / Blockade Busters)");
                }
                catch (Exception ex)
                {
                    PerkIdIlHooks.LogRestore(Swap.Restore(), "perk-ids:fog-sight:restore");
                    OnceLog.Warning("perk-ids:fog-sight:prefix", "[Patch:PerkIds] fog sight prefix failed, vanilla left as is: " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer()
        {
            using (TelemetryPerf.Scope("campaign.patch.perk-ids.fog-sight.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.perk-ids.fog-sight.finalizer");
                if (Swap.Active) PerkIdIlHooks.LogRestore(Swap.Restore(), "perk-ids:fog-sight:restore");
            }
        }

        // FogOfWar.units is a private List<FOWUnit>; FOWUnit (100572) is a private nested class
        // with a public Regiment reg. A rename leaves vanilla running unpatched.
        private static bool Resolve()
        {
            if (_resolved) return _units != null && _unitReg != null;
            _resolved = true;
            try
            {
                Type fowUnit = AccessTools.Inner(typeof(FogOfWar), "FOWUnit");
                _units = CompiledFieldGetter.Build<IList>(AccessTools.Field(typeof(FogOfWar), "units"));
                _unitReg = fowUnit == null ? null : CompiledFieldGetter.Build<Regiment>(AccessTools.Field(fowUnit, "reg"));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("perk-ids:fog-sight:reflect", "[Patch:PerkIds] FogOfWar.units / FOWUnit.reg lookup failed: " + ex.Message);
                _units = null;
                _unitReg = null;
            }
            if (_units == null || _unitReg == null)
            {
                OnceLog.Warning("perk-ids:fog-sight:missing", "[Patch:PerkIds] FogOfWar.units / FOWUnit.reg not found; Expert Scouts sight left as vanilla");
                return false;
            }
            return true;
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
