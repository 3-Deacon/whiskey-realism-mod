using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #70, BUG-WL-001 (chain-of-command spec §F; docs/bug-fixes/vanilla-wl-career.md).
    //
    // Vanilla AICampaign.CheckShipConstruction(int) (decompile 11998-12090) returns at
    // 12012 and AICampaign.CheckArmyGroupManagement(int) (17705-17768) at 17723 on
    // `DLC_WL.dlc_scenarioactive && DLC_WL.IsCommanderInChief()`. IsCommanderInChief
    // (47475) only asks whether the W&L player is CIC of his own alliance, and both
    // methods run for every faction (UpdateUnitAI jobs 12 and 32, 11481, 11583): in a
    // CIC career the enemy never builds ships or forms army groups. Vanilla skips the
    // CIC player's own faction correctly at 11620.
    //
    // This patch changes nothing else: the Prefix arms a one-shot flag only for a
    // faction that is not the CIC player's; the IsCommanderInChief Postfix answers
    // "not CIC" to the first default-argument call while armed — in both bodies that
    // is the early-return guard — and disarms, so nested calls (GrabFleet, RaiseFleet,
    // CheckCommanderPromotions, CreateShip; ArmyGroup.AddUnit, CreateNewArmyGroup) see
    // the true value. A Priority.First Postfix and a Finalizer disarm when vanilla
    // returned before reaching the guard (12012's earlier tests, 17723's
    // allowarmygroups tests). Not a Prefix-skip.
    //
    // IsCommanderInChief overwrites its own argument (47477-47480: `manualcommander =
    // dlc_chosencommander` when it is -1; IL starg.s), and HarmonyX loads a Postfix's
    // arguments from the same slot after the body ran, so the Postfix sees the chosen
    // commander's id, never -1. A non-blocking Prefix therefore records whether the
    // call came in with the default argument, and the Postfix consumes that __state.
    internal static class CicFactionAiBugGuardPatch
    {
        private static bool _armed;

        internal static void Arm(int aifactionIndex)
        {
            _armed = false;
            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (Plugin.EnableCicFactionAiBugGuard == null || !Plugin.EnableCicFactionAiBugGuard.Value) return;
                if (!DLC_WL.dlc_scenarioactive) return;
                int chosen = DLC_WL.dlc_chosencommander;
                if (chosen < 0 || GameVars.commander == null || chosen >= GameVars.commander.Count) return;
                bool playerIsCic = DLC_WL.IsCommanderInChief();
                _armed = CicFactionAiGuard.ShouldArm(
                    guardEnabled: true,
                    wlActive: true,
                    playerIsCic: playerIsCic,
                    cicAlliance: GameVars.commander[chosen].alliance,
                    factionAlliance: AICampaignReflect.GetAllianceId(aifactionIndex));
            }
            catch (Exception ex)
            {
                _armed = false;
                OnceLog.Warning("cic-faction-ai-guard:arm", "[Patch:CicFactionAiGuard] arm failed: " + ex.Message);
            }
        }

        internal static void Disarm()
        {
            _armed = false;
        }

        internal static void ConsumeIfGuardCall(bool calledWithDefaultArgument, ref bool result)
        {
            if (!CicFactionAiGuard.ConsumeAsNotCic(_armed, calledWithDefaultArgument)) return;
            _armed = false;
            if (!result) return;
            result = false;
            OnceLog.Info("cic-faction-ai-guard:applied",
                "[Patch:CicFactionAiGuard] W&L CIC career: ship construction / army-group AI run for a non-player faction (BUG-WL-001)");
        }

        [HarmonyPatch(typeof(AICampaign), nameof(AICampaign.CheckShipConstruction))]
        internal static class ShipConstruction
        {
            [HarmonyPrefix]
            internal static void Prefix(int _aifaction)
            {
                using (TelemetryPerf.Scope("campaign.patch.cic-faction-ai-guard.ship.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.cic-faction-ai-guard.ship.prefix");
                    Arm(_aifaction);
                }
            }

            [HarmonyPostfix]
            [HarmonyPriority(Priority.First)]
            internal static void Postfix()
            {
                using (TelemetryPerf.Scope("campaign.patch.cic-faction-ai-guard.ship.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.cic-faction-ai-guard.ship.postfix");
                    Disarm();
                }
            }

            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception)
            {
                using (TelemetryPerf.Scope("campaign.patch.cic-faction-ai-guard.ship.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.cic-faction-ai-guard.ship.finalizer");
                    Disarm();
                    return __exception;
                }
            }
        }

        [HarmonyPatch(typeof(AICampaign), "CheckArmyGroupManagement")]
        internal static class ArmyGroupManagement
        {
            [HarmonyPrefix]
            internal static void Prefix(int _aifaction)
            {
                using (TelemetryPerf.Scope("campaign.patch.cic-faction-ai-guard.army-group.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.cic-faction-ai-guard.army-group.prefix");
                    Arm(_aifaction);
                }
            }

            // Before #16 ArmyGroupManagementPatch's Postfix (Priority.Normal), which asks
            // IsCommanderInChief itself.
            [HarmonyPostfix]
            [HarmonyPriority(Priority.First)]
            internal static void Postfix()
            {
                using (TelemetryPerf.Scope("campaign.patch.cic-faction-ai-guard.army-group.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.cic-faction-ai-guard.army-group.postfix");
                    Disarm();
                }
            }

            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception)
            {
                using (TelemetryPerf.Scope("campaign.patch.cic-faction-ai-guard.army-group.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.cic-faction-ai-guard.army-group.finalizer");
                    Disarm();
                    return __exception;
                }
            }
        }

        [HarmonyPatch(typeof(DLC_WL), nameof(DLC_WL.IsCommanderInChief))]
        internal static class IsCommanderInChief
        {
            // Reads the argument before vanilla overwrites it (47477-47480). Never skips
            // vanilla (returns void).
            [HarmonyPrefix]
            internal static void Prefix(int manualcommander, out bool __state)
            {
                __state = false;
                using (TelemetryPerf.Scope("campaign.patch.cic-faction-ai-guard.is-cic.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.cic-faction-ai-guard.is-cic.prefix");
                    __state = CicFactionAiGuard.IsDefaultArgumentCall(manualcommander);
                }
            }

            [HarmonyPostfix]
            internal static void Postfix(bool __state, ref bool __result)
            {
                using (TelemetryPerf.Scope("campaign.patch.cic-faction-ai-guard.is-cic", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.cic-faction-ai-guard.is-cic");
                    try
                    {
                        ConsumeIfGuardCall(__state, ref __result);
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("cic-faction-ai-guard:is-cic", "[Patch:CicFactionAiGuard] IsCommanderInChief postfix failed: " + ex.Message);
                    }
                }
            }
        }
    }
}
