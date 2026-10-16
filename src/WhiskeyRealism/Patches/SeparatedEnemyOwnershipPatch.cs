using System;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Battle.Producers;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #121 SeparatedEnemyOwnershipPatch (BUG-TAC-029) - Vanilla AIBattle.CheckForSeparatedEnemy (8056-8215, called per AI group from
    // the macro cycle, 5676) sends a group straight at an isolated enemy cluster it finds from the true battlefield state
    // (SetWaypoint ... useorderdelay: false, 8188), whatever its orders, and the posture pipeline then re-asserts the ledger order:
    // two movers on one group. Whiskey's IsolatedCluster opening (requirement-detect-isolated-cluster) does the same thing
    // fog-honestly, so for a Whiskey-commanded group this vanilla behaviour is switched off (the user decided "Switch it off").
    //
    // A non-skipping Prefix stores aigroup.aiassaultmove and sets it true when the flag is on, Commander Mode is Active, the side
    // is orchestrator-driven, the instance is not the W&L feud instance and the group is Whiskey-commanded (in the AI pool with a
    // doctrine state in the operations ledger). Vanilla then returns at its first test (8078, "... || aigroup.aiassaultmove) return;"),
    // before any write. A Finalizer restores the stored value whatever happens and lets an exception pass through unchanged.
    // Vanilla-owned groups, the player's groups and the feud instance are untouched. The Prefix and the Finalizer also tally the
    // isolated-target launches vanilla makes (aiassaultmoveisolatedtarget false to true) for the TacticalSeparatedEnemySteer row.
    [HarmonyPatch(typeof(AIBattle), "CheckForSeparatedEnemy")]
    internal static class SeparatedEnemyOwnershipPatch
    {
        internal struct SteerState
        {
            public bool Original;
            public bool Forced;
            public bool IsolatedBefore;
            public bool Whiskey;
            public int Side;
        }

        private const double RowIntervalHours = 0.1;
        private static readonly int[] Suppressed = new int[2], LaunchesWhiskey = new int[2], LaunchesVanilla = new int[2];
        private static readonly double[] NextRow = { double.NegativeInfinity, double.NegativeInfinity };

        static SeparatedEnemyOwnershipPatch()
        {
            BattleScope.Register("separated-enemy-steer", Reset);
        }

        [HarmonyPrefix]
        internal static void Prefix(AIBattle __instance, Regiment aigroup, out SteerState __state)
        {
            __state = default(SteerState);
            using (TelemetryPerf.Scope("tactical.patch.separated-enemy.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.separated-enemy.prefix");
                try
                {
                    if ((UnityEngine.Object)__instance == null || (UnityEngine.Object)aigroup == null) return;
                    int side = __instance.sideofai;
                    __state.Side = side;
                    __state.IsolatedBefore = aigroup.aiassaultmoveisolatedtarget;
                    if (side < 0 || side >= Suppressed.Length) return;
                    if (!TacticalBattleCoordinator.DrivesOrchestrator(__instance.isplayeraiorfeud) || TacticalWriteGuard.IsFeudInstance(__instance)) return;
                    __state.Whiskey = WhiskeyCommanded(aigroup);
                    if (!__state.Whiskey || !Plugin.SeparatedEnemySteerEnabled() || !TacticalWriteGuard.ModeAllowsWrites()) return;
                    __state.Original = aigroup.aiassaultmove;
                    __state.Forced = true;
                    aigroup.aiassaultmove = true;
                    Suppressed[side]++;
                    OnceLog.Info("tactical-battle-core:separated-enemy-first-suppressed",
                        "[SeparatedEnemy] first isolated-target move suppressed for a Whiskey-commanded group; its IsolatedCluster opening takes over");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-battle-core:separated-enemy-prefix-failed",
                        "[SeparatedEnemy] prefix failed; vanilla runs unchanged: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer(Regiment aigroup, SteerState __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.separated-enemy.finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.separated-enemy.finalizer");
                try
                {
                    if ((UnityEngine.Object)aigroup == null) return;
                    if (__state.Forced) aigroup.aiassaultmove = __state.Original;
                    int side = __state.Side;
                    if (side < 0 || side >= Suppressed.Length) return;
                    if (!__state.IsolatedBefore && aigroup.aiassaultmoveisolatedtarget)
                    {
                        if (__state.Whiskey) LaunchesWhiskey[side]++;
                        else LaunchesVanilla[side]++;
                    }
                    EmitRowIfDue(side);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-battle-core:separated-enemy-finalizer-failed",
                        "[SeparatedEnemy] finalizer failed: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        // Whiskey-commanded: in the AI pool and the operations ledger holds a doctrine state for it (BC15 A2's "Whiskey-owned" test).
        private static bool WhiskeyCommanded(Regiment group)
        {
            int alliance = group.alliance;
            int id = group.gameObject.GetInstanceID();
            TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(alliance);
            return side != null && ArmyPostureInputsRuntime.InAiPool(alliance, id) && PostureLedger.TryResolveState(id, side, out _);
        }

        private static void EmitRowIfDue(int side)
        {
            double now = TacticalDoctrineTime.NowBattleHours;
            if (now < NextRow[side]) return;
            NextRow[side] = now + RowIntervalHours;
            if (!TelemetryRouter.ShouldEmit(TelemetryRouter.CurrentProfile, TelemetryLayer.Tactical, TelemetryCategory.State)) return;
            bool on = Plugin.SeparatedEnemySteerEnabled();
            int suppressed = Suppressed[side], whiskey = LaunchesWhiskey[side], vanilla = LaunchesVanilla[side];
            TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.State, "TacticalSeparatedEnemySteer", TelemetrySeverity.Info,
                ev => ev
                    .WithSide(side)
                    .WithField("steer", on)
                    .WithField("suppressed", suppressed)
                    .WithField("launchesWhiskey", whiskey)
                    .WithField("launchesVanilla", vanilla)
                    .WithField("doctrineHours", now));
        }

        private static void Reset()
        {
            for (int i = 0; i < Suppressed.Length; i++)
            {
                Suppressed[i] = LaunchesWhiskey[i] = LaunchesVanilla[i] = 0;
                NextRow[i] = double.NegativeInfinity;
            }
        }
    }
}
