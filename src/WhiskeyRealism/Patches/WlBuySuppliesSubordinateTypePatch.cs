using System;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-WL-006. Attribute 24 is "average supply of subordinates": Attribute.Get
    // reads DLC_WL.GetAverageSupplySubordinates (DC:42235-42237) and Attribute.Set
    // calls DLC_WL.AddSuppliesSubordinates (DC:42561-42563); action 34 "Buy
    // Supplies" (1000 money, actions.dat: "Purchase enough supplies for five (5)
    // full units, and distribute it among the troops") sets it. Both vanilla
    // loops (DC:47377-47464) admit an attached unit only when
    // `currentcommand.unittyp <= 13` - the player's command type, where the
    // attached unit's type was meant. For a division/corps/army command the test
    // is false for every unit, so the average reads 0 (no shortage) and the
    // purchase fills nothing.
    //
    // Both Postfixes act only for an HQ command (vanilla provably did nothing
    // there) and repeat vanilla's own loop with the attached unit's type. For a
    // basic-unit command every attached unit (allattachedunits includes the unit
    // itself, DC:119840ff) is a basic unit, so vanilla's test already is the
    // per-unit one and the Postfixes stand aside. Rollback: W&L / Enable W&L Buy
    // Supplies Fix.
    internal static class WlBuySuppliesSubordinateTypePatch
    {
        [HarmonyPatch(typeof(DLC_WL), "GetAverageSupplySubordinates")]
        internal static class AverageSupplyPatch
        {
            [HarmonyPostfix]
            internal static void Postfix(ref float __result)
            {
                using (TelemetryPerf.Scope("campaign.patch.wl-buy-supplies.average", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.wl-buy-supplies.average");
                    try
                    {
                        Regiment command = HqCommandNeedingCorrection();
                        if ((UnityEngine.Object)command == null) return;

                        float total = 0f;
                        float weighted = 0f;
                        foreach (Regiment unit in Recipients(command))
                        {
                            float strength = unit.GetStrength(countwounded: true, countsick: true, returnrelativestrength: true);
                            total += strength;
                            weighted += (1f - unit.GetAverageSupplyStock()) * strength;
                        }
                        __result = WlEconomyFixes.AverageShortage(weighted, total);
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("wl-buy-supplies:average", "[Patch:WlBuySupplies] average postfix failed: " + ex.Message);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(DLC_WL), "AddSuppliesSubordinates")]
        internal static class AddSuppliesPatch
        {
            [HarmonyPostfix]
            internal static void Postfix(float amount)
            {
                using (TelemetryPerf.Scope("campaign.patch.wl-buy-supplies.add", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.wl-buy-supplies.add");
                    try
                    {
                        if (!(amount > 0f)) return;
                        Regiment command = HqCommandNeedingCorrection();
                        if ((UnityEngine.Object)command == null) return;

                        // Vanilla's loop (DC:47425-47463) with the attached unit's type.
                        float totalShortage = 0f;
                        var recipients = new List<Regiment>();
                        foreach (Regiment unit in Recipients(command))
                        {
                            float strength = unit.GetStrength(countwounded: true, countsick: true, returnrelativestrength: true);
                            float shortage = (1f - unit.GetAverageSupplyStock()) * strength;
                            totalShortage += shortage;
                            if (shortage > 0f) recipients.Add(unit);
                        }
                        if (totalShortage <= 0f || recipients.Count == 0) return;

                        float ratio = WlEconomyFixes.FillRatio(amount, totalShortage);
                        float left = amount;
                        bool exhausted = false;
                        for (int pass = 0; left > 0f && pass < 100 && !exhausted; pass++)
                        {
                            exhausted = true;
                            for (int i = 0; i < recipients.Count; i++)
                            {
                                float filled = recipients[i].FillUpStock(ratio);
                                left -= filled;
                                if (filled > 0f) exhausted = false;
                            }
                        }
                        OnceLog.Info("wl-buy-supplies:first", "[Patch:WlBuySupplies] HQ command's units supplied (vanilla tested the command type)");
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("wl-buy-supplies:add", "[Patch:WlBuySupplies] add postfix failed: " + ex.Message);
                    }
                }
            }
        }

        // The player's command when vanilla's type test lost every recipient, else null.
        private static Regiment HqCommandNeedingCorrection()
        {
            if (!DLC_WL.dlc_scenarioactive || !Enabled()) return null;
            int chosen = DLC_WL.dlc_chosencommander;
            if (GameVars.commander == null || chosen < 0 || chosen >= GameVars.commander.Count) return null;
            Regiment command = GameVars.commander[chosen].currentcommand;
            if ((UnityEngine.Object)command == null || command.allattachedunits == null) return null;
            return WlEconomyFixes.NeedsSubordinateTypeCorrection(command.unittyp) ? command : null;
        }

        private static IEnumerable<Regiment> Recipients(Regiment command)
        {
            Regiment[] attached = command.allattachedunits;
            for (int i = 0; i < attached.Length; i++)
            {
                Regiment unit = attached[i];
                bool present = (UnityEngine.Object)unit != null;
                if (WlEconomyFixes.IsSupplyRecipient(present, present && unit.dlcw_isundercommander, present ? unit.unittyp : int.MaxValue))
                    yield return unit;
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableWlBuySuppliesFix != null && Plugin.EnableWlBuySuppliesFix.Value;
            }
            catch { return false; }
        }
    }
}
