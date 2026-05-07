using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Patch #38 companion - coordinated offensive package lock guard.
    // Vanilla UpdateMicroMovementInOffensive(int) at decompile line 13968
    // retargets units already in unitsinoffensiveoperations toward a nearest
    // area objective. We temporarily remove package-locked units with active
    // paths so their initial coordinated package move is not overwritten before
    // the path is consumed, then restore the list in Postfix/Finalizer.
    [HarmonyPatch(typeof(AICampaign), "UpdateMicroMovementInOffensive")]
    internal static class CoordinatedOffensiveMicroMovementPatch
    {
        private static readonly Dictionary<int, List<object>> _snapshotByFaction =
            new Dictionary<int, List<object>>();

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction)
        {
            try
            {
                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null) return;

                var offensive = AccessTools.Field(faction.GetType(), "unitsinoffensiveoperations")?.GetValue(faction) as IList;
                if (offensive == null || offensive.Count == 0) return;

                var snapshot = new List<object>(offensive.Count);
                for (int i = 0; i < offensive.Count; i++)
                    snapshot.Add(offensive[i]);
                _snapshotByFaction[_aifaction] = snapshot;

                int removed = 0;
                for (int i = offensive.Count - 1; i >= 0; i--)
                {
                    var unit = offensive[i] as Regiment;
                    if (unit == null) continue;
                    if (CoordinatedOperationRuntime.IsPackageLocked(unit))
                    {
                        offensive.RemoveAt(i);
                        removed++;
                    }
                    else
                    {
                        CoordinatedOperationRuntime.ClearPackageLock(unit);
                    }
                }

                if (removed == 0)
                    _snapshotByFaction.Remove(_aifaction);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("coordinated-ops:micro:prefix",
                    "[CoordinatedOps] micro Prefix failed: " + ex.Message);
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(int _aifaction)
        {
            RestoreSnapshot(_aifaction, "postfix");
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception, int _aifaction)
        {
            RestoreSnapshot(_aifaction, "finalizer");
            return __exception;
        }

        private static void RestoreSnapshot(int aifactionIndex, string source)
        {
            try
            {
                if (!_snapshotByFaction.TryGetValue(aifactionIndex, out var snapshot)) return;

                var faction = AICampaignReflect.GetFaction(aifactionIndex);
                if (faction == null) return;

                var offensive = AccessTools.Field(faction.GetType(), "unitsinoffensiveoperations")?.GetValue(faction) as IList;
                if (offensive == null) return;

                offensive.Clear();
                for (int i = 0; i < snapshot.Count; i++)
                    offensive.Add(snapshot[i]);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("coordinated-ops:micro:restore:" + source,
                    "[CoordinatedOps] micro restore failed: " + ex.Message);
            }
            finally
            {
                _snapshotByFaction.Remove(aifactionIndex);
            }
        }
    }
}
