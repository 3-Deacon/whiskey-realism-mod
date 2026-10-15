using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla private Regiment.UpdateIIPSupplyLink (DC:114074) resupplies one
    // campaign-map group per call (it returns for non-map units at
    // DC:114145-114148). In a W&L career it applies the player's logistics effects
    // keyed on the map group's own dlcw_isundercommander: food/ammunition
    // consumption 1/EffectMultiplier(10/11) (DC:114190-114195), supply efficiency
    // EffectMultiplier(9) and camp station 5 "Inspect Logistics"
    // (DC:114346-114351), reinforcement speed EffectMultiplier(14) (DC:114555).
    // One concern, the player's supply effects at this site, two defects:
    //
    // BUG-WL-007 (scope). IsUnitUnderCommander never flags the player's parent
    // (DC:91203-91217), so when the player's command is not itself a map group (a
    // brigade/regiment, or a division inside an AI corps; DC:82935-82950) every
    // one of these effects is inert, although staff.dat makes the Quartermaster
    // appointable from hierarchy level 11 and the army panel shows the bonus
    // (DC:173699-173702). Vanilla's own name for the player's campaign unit is
    // BattleUnits.GetCampaignGroup(currentcommand) (Camp.UpdateUnitRef DC:172032;
    // DLC_WL.IsPlayerPartOfUnit DC:47578; Messages.Add DC:196771-196774). For that
    // one group the Prefix sets dlcw_isundercommander for the call; the Finalizer
    // puts it back. The refresh in Regiment's slow tick (DC:112240) stays vanilla.
    // Supply is one pool per map group, so the AI units inside the player's map
    // group share it, as they share a corps player's; no other group changes.
    //
    // BUG-WL-008 (sign). num15 is the resupply duration (campaignprefs
    // supplyspeeddistance0/2000 "within this time the whole unit is resupplied";
    // supply per tick = need x elapsed / num15, DC:114379; speedofsupply =
    // supplyspeeddistance0 / num15), and vanilla's penalties lengthen it
    // (/= condition, /= tradefactorenemyiips). The supply-efficiency bonus and
    // Inspect Logistics multiply it (DC:114348-114350), so a better Quartermaster
    // or camp result slows resupply, against effectdescriptions.dat "Supply
    // Efficiency", camp.dat "This improves supply efficiency" and the action and
    // staff texts. For an owned group the Prefix sets cumulativeeffectsrunning[9]
    // to 1 / (effect x camp^2), so vanilla's own two multiplications leave
    // num15 / (effect x camp); the Finalizer restores the slot.
    //
    // Non-skipping Prefix, Finalizer restore (runs when vanilla throws).
    // Coupling: with the sign fix off, a group owned only through the scope fix
    // gets effect 9 and camp station 5 cancelled (running[9] = 1 / camp), never
    // vanilla's inverted direction; effects 10/11/14 still apply.
    //
    // Rollback: W&L / Enable W&L Staff Scope Fix, Enable W&L Supply Efficiency
    // Sign Fix.
    [HarmonyPatch(typeof(Regiment), "UpdateIIPSupplyLink")]
    internal static class WlSupplyStaffScopePatch
    {
        internal struct Snapshot
        {
            public bool FlagRaised;
            public float[] Array;
            public float SavedEffect;
            public float WrittenEffect;
            public bool EffectWritten;
        }

        [HarmonyPrefix]
        internal static void Prefix(Regiment __instance, out Snapshot __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.wl-supply-staff-scope.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-supply-staff-scope.prefix");
                __state = default;
                try
                {
                    if (!DLC_WL.dlc_scenarioactive || (UnityEngine.Object)__instance == null) return;
                    if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;

                    bool owned = __instance.dlcw_isundercommander;
                    if (!owned && Flag(Plugin.EnableWlStaffScopeFix)
                        && WlEconomyFixes.TreatMapGroupAsPlayerOwned(true, owned, WlPlayerMapGroup.Is(__instance)))
                    {
                        __instance.dlcw_isundercommander = true;
                        __state.FlagRaised = true;
                        OnceLog.Info("wl-supply-staff-scope:first",
                            "[Patch:WlSupplyStaff] player's supply staff and Inspect Logistics applied to the map group carrying the player's command: group=" + __instance.name);
                    }

                    if (owned || __state.FlagRaised)
                        SetSupplyEfficiencyForCall(owned, ref __state);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-supply-staff-scope:prefix", "[Patch:WlSupplyStaff] prefix failed: " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer(Regiment __instance, Snapshot __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.wl-supply-staff-scope.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-supply-staff-scope.finalizer");
                try
                {
                    if (__state.FlagRaised && (UnityEngine.Object)__instance != null)
                        __instance.dlcw_isundercommander = false;

                    if (__state.EffectWritten)
                    {
                        float[] running = WlEffectArrays.Running();
                        bool same = ReferenceEquals(running, __state.Array);
                        if (WlEconomyFixes.ShouldRestore(same, same ? running[WlEconomyFixes.EffectSupplyEfficiency] : float.NaN, __state.WrittenEffect))
                            running[WlEconomyFixes.EffectSupplyEfficiency] = __state.SavedEffect;
                    }
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-supply-staff-scope:finalizer", "[Patch:WlSupplyStaff] restore failed: " + ex.Message);
                }
            }
        }

        // Sign fix on: corrected direction for every owned group. Sign fix off: a
        // natively owned group keeps vanilla; a group owned only through the scope
        // fix gets effect 9 and camp station 5 cancelled to neutral, so the scope
        // fix never carries vanilla's inverted direction (WlEconomyFixes.SupplyEfficiencyForCall).
        private static void SetSupplyEfficiencyForCall(bool nativelyOwned, ref Snapshot state)
        {
            float[] running = WlEffectArrays.Running();
            if (!WlEffectArrays.TryGet(running, WlEconomyFixes.EffectSupplyEfficiency, out float effect)) return;

            // The same call vanilla makes at DC:114350 (including #29's Postfix).
            float camp = Camp.GetModifier(WlEconomyFixes.CampStationInspectLogistics);
            if (effect == 1f && camp == 1f) return;

            bool signFix = Flag(Plugin.EnableWlSupplyEfficiencySignFix);
            float corrected = WlEconomyFixes.SupplyEfficiencyForCall(nativelyOwned, state.FlagRaised, signFix, effect, camp);
            if (float.IsNaN(corrected) || corrected == effect) return;

            running[WlEconomyFixes.EffectSupplyEfficiency] = corrected;
            state.Array = running;
            state.SavedEffect = effect;
            state.WrittenEffect = corrected;
            state.EffectWritten = true;
            if (!signFix)
            {
                OnceLog.Info("wl-supply-efficiency-neutral:first",
                    "[Patch:WlSupplyStaff] sign fix off: supply efficiency and Inspect Logistics held neutral for the scoped map group");
                return;
            }
            OnceLog.Info("wl-supply-efficiency-sign:first",
                "[Patch:WlSupplyEfficiencySign] supply-efficiency bonus now shortens the resupply duration: effect=" + effect.ToString("0.###")
                + " camp=" + camp.ToString("0.###") + " durationFactor=" + (corrected * camp).ToString("0.###"));
        }

        private static bool Flag(BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.Value; }
            catch { return false; }
        }
    }
}
