using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Runtime wrapper for DirectChildDiscovery. Reads the side's current
    /// <see cref="BattleSnapshot"/> (one completeunitlist pass per tick, filtered by
    /// alliance, so each side discovers its own command groups whichever AIBattle fired)
    /// and its single command-parent map (BattleSnapshotAssembler, plan D5: smallest group
    /// whose allattachedunits contains the unit). No GetAttachedUnitsReg walk, no
    /// scene-graph parent read, no roster pass of its own. IsDirectChild reproduces vanilla's
    /// GetAttachedUnitsReg(directonly, includenonactiveunits:false) filters (119840-119880)
    /// from the snapshot flags.
    /// </summary>
    public static partial class DirectChildDiscovery
    {
        static DirectChildDiscovery()
        {
            BattleScope.Register("direct-child-discovery", ResetForBattleEnd);
        }

        private static FieldInfo _commandHierarchyShiftField;

        public static IReadOnlyList<DirectChildSnapshot> Snapshot(int allianceId)
        {
            try
            {
                if (!BattleSnapshotBuilder.TryGetCurrentOrWarn(allianceId, out BattleSnapshot battle))
                    return Array.Empty<DirectChildSnapshot>();
                int shift = ReadCommandHierarchyShift();
                var probes = new List<RegimentProbe>(battle.OwnCount);
                // The W&L player's command subtree is never an AI direct child: no role
                // is allocated to it (same exclusion as ArmyPostureInputsRuntime's tree).
                BattleSnapshotAssembler.ToRegimentProbes(battle, BattleSnapshotBuilder.NamesFor(allianceId), shift,
                    excludePlayerOwned: DLC_WL.dlc_scenarioactive, probes);
                return Probe(probes, shift);
            }
            catch (Exception e)
            {
                OnceLog.Warning("o3-direct-child-discovery:exception",
                    "DirectChildDiscovery.Snapshot(allianceId) failed: " + e.GetType().Name + " " + e.Message);
                return Array.Empty<DirectChildSnapshot>();
            }
        }

        /// <summary>
        /// Same parent map as <see cref="Snapshot(int)"/>, with world X/Z and a strength
        /// bucket, for TacticalCommandTreeProbe (role cascade / leaf-brigade map).
        /// </summary>
        public static IReadOnlyList<TacticalCommandTreeProbe.ExtendedProbe> BuildExtendedProbesForAlliance(int allianceId)
        {
            try
            {
                if (!BattleSnapshotBuilder.TryGetCurrentOrWarn(allianceId, out BattleSnapshot battle))
                    return Array.Empty<TacticalCommandTreeProbe.ExtendedProbe>();
                var result = new List<TacticalCommandTreeProbe.ExtendedProbe>(battle.OwnCount);
                BattleSnapshotAssembler.ToExtendedProbes(battle, BattleSnapshotBuilder.NamesFor(allianceId),
                    ReadCommandHierarchyShift(), result);
                int withParent = 0;
                for (int i = 0; i < result.Count; i++)
                    if (result[i].ParentInstanceId != 0) withParent++;
                EmitProbeHealthTelemetry(allianceId, withParent, result.Count - withParent);
                return result;
            }
            catch (Exception e)
            {
                OnceLog.Warning("o3-extended-probes:exception",
                    "DirectChildDiscovery.BuildExtendedProbesForAlliance failed: "
                    + e.GetType().Name + " " + e.Message);
                return Array.Empty<TacticalCommandTreeProbe.ExtendedProbe>();
            }
        }

        /// <summary>
        /// Signature-deduped health event: how many probes have a command parent in the
        /// snapshot's parent map and how many are roots (no containing group).
        /// </summary>
        private static void EmitProbeHealthTelemetry(int allianceId, int withParent, int noParent)
        {
            // Environment.TickCount (mscorlib) instead of UnityEngine.Time.realtimeSinceStartup
            // — Unity Time is an ECall whose JIT-time verification throws
            // SecurityException in pure-.NET test environments. Telemetry throttle only.
            float nowSeconds = System.Environment.TickCount * 0.001f;
            try
            {
                string sig = "alliance=" + allianceId
                    + "|parent=" + withParent
                    + "|noParent=" + noParent;
                string key = "tactical-probe-health:" + allianceId + ":" + sig;
                if (!TacticalTelemetry.ShouldEmit(_lastProbeHealthTelemetryAt, key, sig, nowSeconds, ProbeHealthTelemetrySeconds, false))
                    return;
                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Health,
                    "TacticalCommandTreeProbeHealth",
                    TelemetrySeverity.Info,
                    ev => ev
                        .WithDecision("TacticalCommandTreeProbeHealth", "summary", sig)
                        .WithField("alliance", allianceId)
                        .WithField("parent", withParent)
                        .WithField("noParent", noParent));
            }
            catch { }
        }

        private static readonly Dictionary<string, float> _lastProbeHealthTelemetryAt = new Dictionary<string, float>();

        // Keys embed per-battle distributions; cleared at battle end so the dedupe
        // dictionary does not grow across battles.
        internal static void ResetForBattleEnd()
        {
            _lastProbeHealthTelemetryAt.Clear();
        }
        private const float ProbeHealthTelemetrySeconds = 30f;

        private static int ReadCommandHierarchyShift()
        {
            try
            {
                if (_commandHierarchyShiftField == null)
                    _commandHierarchyShiftField = AccessTools.Field(typeof(GamePrefs), "commandhierarchyshift");
                if (_commandHierarchyShiftField == null) return 0;
                var v = _commandHierarchyShiftField.GetValue(null);
                if (v is int shift) return shift;
                return 0;
            }
            catch
            {
                return 0;
            }
        }

    }
}
