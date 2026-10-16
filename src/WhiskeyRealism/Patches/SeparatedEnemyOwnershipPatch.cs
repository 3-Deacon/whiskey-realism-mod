using System;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Battle.BugFixes;
using WhiskeyRealism.Tactical.Battle.Producers;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #121 SeparatedEnemyOwnershipPatch (a steer, no bug id) - Vanilla AIBattle.CheckForSeparatedEnemy (8056-8215, called per AI group from
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
                    SeparatedEnemySteerInput gate = Facts(__instance, aigroup, side);
                    if (!gate.SideInRange) return;
                    if (!SeparatedEnemySteerGate.Tallied(in gate)) return;
                    __state.Whiskey = SeparatedEnemySteerGate.WhiskeyCommanded(in gate);
                    if (!SeparatedEnemySteerGate.Suppress(in gate)) return;
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

        // The runtime facts the pure gate decides on (SeparatedEnemySteerGate): flags, the side's orchestrator, the AI pool, the ledger and
        // the write guard. A group Whiskey does not command (a vanilla-owned group, the player's units) is left to vanilla.
        private static SeparatedEnemySteerInput Facts(AIBattle battle, Regiment group, int side)
        {
            var input = new SeparatedEnemySteerInput
            {
                HasInstanceAndGroup = true,
                SideInRange = side >= 0 && side < Suppressed.Length,
                FlagOn = Plugin.MasterEnabled() && Plugin.SeparatedEnemySteerEnabled(),
                CommanderModeAllowsWrites = TacticalWriteGuard.ModeAllowsWrites(),
            };
            if (!input.SideInRange) return input;
            input.SideDrivenByOrchestrator = TacticalBattleCoordinator.DrivesOrchestrator(battle.isplayeraiorfeud);
            input.FeudInstance = TacticalWriteGuard.IsFeudInstance(battle);
            if (!input.SideDrivenByOrchestrator || input.FeudInstance) return input;
            int alliance = group.alliance;
            int id = group.gameObject.GetInstanceID();
            TacticalBattleOrchestrator orchestrator = TacticalBattleCoordinator.GetSideOrchestrator(alliance);
            input.InAiPool = orchestrator != null && ArmyPostureInputsRuntime.InAiPool(alliance, id);
            input.HasDoctrineState = input.InAiPool && PostureLedger.TryResolveState(id, orchestrator, out _);
            input.CanWrite = TacticalWriteGuard.CanWrite(group);
            return input;
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
