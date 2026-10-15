using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
        private static readonly List<Regiment.Perk> NoPerks = new List<Regiment.Perk>();
        private static readonly List<Regiment> _hidden = new List<Regiment>();
        private static readonly List<List<Regiment.Perk>> _saved = new List<List<Regiment.Perk>>();

        private static bool _resolved;
        private static FieldInfo _unitsField;
        private static AccessTools.FieldRef<object, Regiment> _unitReg;

        [HarmonyPrefix]
        internal static void Prefix(FogOfWar __instance)
        {
            using (TelemetryPerf.Scope("campaign.patch.perk-ids.fog-sight.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.perk-ids.fog-sight.prefix");
                try
                {
                    Restore();
                    if (!Enabled() || !GameVars.usefow || !Resolve()) return;
                    if (!(_unitsField.GetValue(__instance) is IList units)) return;

                    for (int i = 0; i < units.Count; i++)
                    {
                        object unit = units[i];
                        if (unit == null) continue;
                        Regiment reg = _unitReg(unit);
                        if ((UnityEngine.Object)reg == null || reg.perks == null) continue;

                        bool hasEight = reg.HasPerk(VanillaPerkTables.BattleExpertScouts);
                        if (!hasEight || VanillaPerkTables.ExpertScoutsSightApplies(reg.unittyp, hasEight)) continue;

                        _hidden.Add(reg);
                        _saved.Add(reg.perks);
                        reg.perks = NoPerks;
                    }

                    if (_hidden.Count > 0)
                        OnceLog.Info("perk-ids:fog-sight", "[Patch:PerkIds] UpdateViewRange: Expert Scouts sight withheld from a non-battle perk id 8 (Ambulance Corps / Blockade Busters)");
                }
                catch (Exception ex)
                {
                    Restore();
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
                Restore();
            }
        }

        private static void Restore()
        {
            for (int i = 0; i < _hidden.Count; i++)
            {
                try
                {
                    Regiment reg = _hidden[i];
                    if ((object)reg != null && ReferenceEquals(reg.perks, NoPerks))
                        reg.perks = _saved[i];
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("perk-ids:fog-sight:restore", "[Patch:PerkIds] fog sight restore failed: " + ex.Message);
                }
            }
            _hidden.Clear();
            _saved.Clear();
        }

        // FogOfWar.units is a private List<FOWUnit>; FOWUnit (100572) is a private nested class
        // with a public Regiment reg. A rename leaves vanilla running unpatched.
        private static bool Resolve()
        {
            if (_resolved) return _unitsField != null && _unitReg != null;
            _resolved = true;
            try
            {
                _unitsField = AccessTools.Field(typeof(FogOfWar), "units");
                Type fowUnit = AccessTools.Inner(typeof(FogOfWar), "FOWUnit");
                if (_unitsField != null && fowUnit != null && AccessTools.Field(fowUnit, "reg") != null)
                    _unitReg = AccessTools.FieldRefAccess<Regiment>(fowUnit, "reg");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("perk-ids:fog-sight:reflect", "[Patch:PerkIds] FogOfWar.units / FOWUnit.reg lookup failed: " + ex.Message);
                _unitsField = null;
                _unitReg = null;
            }
            if (_unitsField == null || _unitReg == null)
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
