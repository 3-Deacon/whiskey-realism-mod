using System;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // The battle-core arbiter tick (plan BC4, D3).
    //
    // Vanilla AIBattle.UpdateAITasks (5857-5872), per AI instance, runs
    // AssignReserves, UpdateMovingTargets (5862), LinkReservesToLineGroup,
    // CalculateFlankData, CheckIfFlanksAreAnchored, CheckGlobalAIStrategy (5866,
    // where the coordinator ticks and builds the battle snapshot), CheckFlankMoves
    // (5867), CheckEndOfActions, AdjustGroupAIStance (5869), AdjustGroupFormations
    // (5870, this Postfix) and CheckForFeudGroupActions (5871, feud instance only).
    // AdjustGroupFormations itself re-forms settled groups (5875-5935).
    //
    // This Postfix runs at Priority.Last after vanilla's own movers in the same
    // call, so Whiskey's winning orders are the last macro-cycle write, against a
    // snapshot built at 5866 in the same frame. It changes nothing in vanilla's
    // method: it runs the producers, resolves one claim per (group, channel) and
    // issues the winners through OrderExecutor. With no producer registered it
    // returns right after the first-fire mark.
    //
    // Gate (D3): DrivesOrchestrator, then PostureExecutorBattleGate.ArbiterAction:
    // the master Enabled switch (runtime-toggleable) and Tactical Commander Mode
    // Active first, then Allows (state >= 5, macroai >= 0, macroai 3 → hand
    // Whiskey-held stances back to vanilla, no end-of-day, no feud instance, no
    // player instance unless ai_vs_ai). A side gated off drops its published claims
    // (BattleCoreTick.StandDown) so InlineClaimCheck never reads a stale tick.
    [HarmonyPatch(typeof(AIBattle), "AdjustGroupFormations")]
    internal static class BattleCoreArbiterPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix(AIBattle __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.battle-core-arbiter", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
            {
                PatchFirstFire.Mark("tactical.patch.battle-core-arbiter");
                if ((UnityEngine.Object)__instance == null || !BattleCoreTick.HasProducers) return;
                try
                {
                    Run(__instance);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-battle-core:arbiter-failed",
                        "[OrderArbiter] arbiter tick threw: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        private static void Run(AIBattle battle)
        {
            if (!TacticalBattleCoordinator.DrivesOrchestrator(battle.isplayeraiorfeud)) return;
            BattleUnits bunits = BattleCoreTick.ResolveBattleUnits();
            if ((UnityEngine.Object)bunits == null) return;

            int side = battle.sideofai;
            int[] alliances = bunits.alliance;
            if (alliances == null || side < 0 || side >= alliances.Length) return;
            int alliance = alliances[side];

            // Gated off: no claims stay published for inline intents to hit.
            ArbiterTickAction action = PostureExecutorBattleGate.ArbiterAction(PluginEnabled(), TacticalWriteGuard.ModeAllowsWrites(),
                battle.state, battle.macroai, bunits.eodcycle, battle.isplayeraiorfeud, GameVars.ai_vs_ai);
            if (action != ArbiterTickAction.Run)
            {
                if (action == ArbiterTickAction.StandDownAndReleaseRetreat) TacticalStanceWriter.ReleaseForRetreat(battle);
                BattleCoreTick.StandDown(alliance);
                return;
            }

            BattleCoreTick.RunArbiter(battle, alliance);
        }

        private static bool PluginEnabled()
        {
            Plugin plugin = Plugin.Instance;
            return plugin != null && plugin.Enabled != null && plugin.Enabled.Value;
        }
    }
}
