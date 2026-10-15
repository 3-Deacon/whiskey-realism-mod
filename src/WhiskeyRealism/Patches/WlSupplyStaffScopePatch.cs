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
    // Non-skipping Prefix: each write is stored in __state before it is made
    // (Patches/WlEffectSwaps Prepare/Commit); the Finalizer restores both
    // unconditionally, even if vanilla throws.
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
            public OwnershipRaise<Regiment> Group;
            public EffectSlotWrite Efficiency;
        }

        private static readonly Action<Regiment, bool> SetOwned = (unit, owned) => unit.dlcw_isundercommander = owned;

        // First-fire messages are formatted once per campaign, not per call.
        private static bool _scopeLogged;
        private static bool _signLogged;
        private static bool _neutralLogged;

        static WlSupplyStaffScopePatch()
        {
            CampaignScope.Register("wl-supply-staff-scope-log", () =>
            {
                _scopeLogged = false;
                _signLogged = false;
                _neutralLogged = false;
            });
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
                    if (Flag(Plugin.EnableWlStaffScopeFix)
                        && WlEconomyFixes.TreatMapGroupAsPlayerOwned(true, owned, WlPlayerMapGroup.Is(__instance)))
                    {
                        __state.Group = OwnershipRaise<Regiment>.Prepare(__instance);
                        __state.Group.Commit(SetOwned);
                        if (!_scopeLogged)
                        {
                            _scopeLogged = true;
                            OnceLog.Info("wl-supply-staff-scope:first",
                                "[Patch:WlSupplyStaff] player's supply staff and Inspect Logistics applied to the map group carrying the player's command: group=" + __instance.name);
                        }
                    }

                    if (!owned && !__state.Group.Active) return;
                    if (!WlEffectArrays.TryGet(WlEffectArrays.Running(), WlEconomyFixes.EffectSupplyEfficiency, out float effect)) return;
                    // The same call vanilla makes at DC:114350 (including #29's Postfix).
                    float camp = Camp.GetModifier(WlEconomyFixes.CampStationInspectLogistics);
                    bool signFix = Flag(Plugin.EnableWlSupplyEfficiencySignFix);
                    float value = WlEconomyFixes.SupplyEfficiencyForCall(owned, __state.Group.Active, signFix, effect, camp);
                    if (float.IsNaN(value)) return;

                    __state.Efficiency = EffectSlotWrite.Prepare(WlEffectArrays.Running(), WlEconomyFixes.EffectSupplyEfficiency, value);
                    __state.Efficiency.Commit();
                    if (!__state.Efficiency.Active) return;
                    if (signFix && !_signLogged)
                    {
                        _signLogged = true;
                        OnceLog.Info("wl-supply-efficiency-sign:first",
                            "[Patch:WlSupplyEfficiencySign] supply-efficiency bonus now shortens the resupply duration: effect=" + effect.ToString("0.###")
                            + " camp=" + camp.ToString("0.###") + " durationFactor=" + (value * camp).ToString("0.###"));
                    }
                    else if (!signFix && !_neutralLogged)
                    {
                        _neutralLogged = true;
                        OnceLog.Info("wl-supply-efficiency-neutral:first",
                            "[Patch:WlSupplyStaff] sign fix off: supply efficiency and Inspect Logistics held neutral for the scoped map group");
                    }
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-supply-staff-scope:prefix", "[Patch:WlSupplyStaff] prefix failed: " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer(Snapshot __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.wl-supply-staff-scope.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-supply-staff-scope.finalizer");
                try
                {
                    __state.Group.Restore(SetOwned);
                    __state.Efficiency.Restore(WlEffectArrays.Running());
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-supply-staff-scope:finalizer", "[Patch:WlSupplyStaff] restore failed: " + ex.Message);
                }
            }
        }

        private static bool Flag(BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.Value; }
            catch { return false; }
        }
    }
}
