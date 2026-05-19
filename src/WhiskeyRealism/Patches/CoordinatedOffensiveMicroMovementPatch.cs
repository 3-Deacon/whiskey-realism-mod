using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Patch #38 companion - coordinated offensive package lock guard.
    // Vanilla UpdateMicroMovementInOffensive(int) at decompile line 13968
    // retargets units already in unitsinoffensiveoperations toward a nearest
    // area objective. We temporarily remove package-locked units with active
    // paths so their initial coordinated package move is not overwritten before
    // the path is consumed, then reinsert only those removed units in
    // Postfix/Finalizer so vanilla removals of unrelated units survive.
    [HarmonyPatch(typeof(AICampaign), "UpdateMicroMovementInOffensive")]
    internal static class CoordinatedOffensiveMicroMovementPatch
    {
        private sealed class RemovedUnit
        {
            internal int Index;
            internal object Unit;
        }

        private static readonly Dictionary<int, List<RemovedUnit>> _removedByFaction =
            new Dictionary<int, List<RemovedUnit>>();

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction)
        {
            using (TelemetryPerf.Scope("tactical.patch.coord-offensive-micro-move-prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    var faction = AICampaignReflect.GetFaction(_aifaction);
                    if (faction == null) return;

                    var offensive = AccessTools.Field(faction.GetType(), "unitsinoffensiveoperations")?.GetValue(faction) as IList;
                    if (offensive == null || offensive.Count == 0) return;

                    var removed = new List<RemovedUnit>();
                    for (int i = offensive.Count - 1; i >= 0; i--)
                    {
                        var unit = offensive[i] as Regiment;
                        if (unit == null) continue;
                        if (CoordinatedOperationRuntime.IsPackageLocked(unit))
                        {
                            removed.Add(new RemovedUnit { Index = i, Unit = offensive[i] });
                            offensive.RemoveAt(i);
                        }
                        else
                        {
                            CoordinatedOperationRuntime.ClearPackageLock(unit);
                        }
                    }

                    if (removed.Count > 0)
                        _removedByFaction[_aifaction] = removed;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("coordinated-ops:micro:prefix",
                        "[CoordinatedOps] micro Prefix failed: " + ex.Message);
                }
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(int _aifaction)
        {
            using (TelemetryPerf.Scope("tactical.patch.coord-offensive-micro-move", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                RestoreSnapshot(_aifaction, "postfix");
            }
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception, int _aifaction)
        {
            using (TelemetryPerf.Scope("tactical.patch.coord-offensive-micro-move-finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                RestoreSnapshot(_aifaction, "finalizer");
                return __exception;
            }
        }

        private static void RestoreSnapshot(int aifactionIndex, string source)
        {
            try
            {
                if (!_removedByFaction.TryGetValue(aifactionIndex, out var removed)) return;

                var faction = AICampaignReflect.GetFaction(aifactionIndex);
                if (faction == null) return;

                var offensive = AccessTools.Field(faction.GetType(), "unitsinoffensiveoperations")?.GetValue(faction) as IList;
                if (offensive == null) return;

                removed.Sort((a, b) => a.Index.CompareTo(b.Index));
                for (int i = 0; i < removed.Count; i++)
                {
                    var unit = removed[i].Unit;
                    if (unit == null || offensive.Contains(unit)) continue;
                    int index = Math.Max(0, Math.Min(removed[i].Index, offensive.Count));
                    offensive.Insert(index, unit);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("coordinated-ops:micro:restore:" + source,
                    "[CoordinatedOps] micro restore failed: " + ex.Message);
            }
            finally
            {
                _removedByFaction.Remove(aifactionIndex);
            }
        }
    }
}
