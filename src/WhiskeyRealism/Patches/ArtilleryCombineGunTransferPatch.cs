using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla BattleUnits.CombineUnits moves artillery crew strength from the
    // source to the target, but never adds the source unit's organic guns to the
    // receiving unit. This Postfix preserves the proportional source guns that
    // correspond to the men actually transferred.
    [HarmonyPatch(typeof(BattleUnits), "CombineUnits")]
    internal static class ArtilleryCombineGunTransferPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(Regiment unitsource, Regiment unitto, ref State __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.arty-combine-gun-transfer.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                __state = default;
                try
                {
                    if (!Enabled()) return;
                    if ((UnityEngine.Object)(object)unitsource == (UnityEngine.Object)null ||
                        (UnityEngine.Object)(object)unitto == (UnityEngine.Object)null)
                    {
                        return;
                    }
                    if (unitsource.unittyp != 2 || unitto.unittyp != 2) return;

                    __state = new State(
                        unitsource,
                        unitto,
                        TotalMen(unitsource),
                        TotalMen(unitto),
                        Math.Max(0, unitsource.guns),
                        Math.Max(0, unitto.guns));
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("artillery-combine-guns:prefix", "[Patch:ArtilleryCombine] prefix failed: " + ex.Message);
                }
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(State __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.arty-combine-gun-transfer.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    if (!__state.Valid) return;
                    var target = __state.Target;
                    if ((UnityEngine.Object)(object)target == (UnityEngine.Object)null) return;

                    int transferredMen = TotalMen(target) - __state.TargetMenBefore;
                    int gunsToTransfer = ArtilleryCombineGunTransfer.CalculateGunsToTransfer(
                        isArtillery: true,
                        sourceGuns: __state.SourceGunsBefore,
                        sourceTotalMen: __state.SourceMenBefore,
                        transferredMen: transferredMen);
                    if (gunsToTransfer <= 0) return;

                    int desiredTargetGuns = __state.TargetGunsBefore + gunsToTransfer;
                    if (target.guns >= desiredTargetGuns) return;

                    int delta = desiredTargetGuns - target.guns;
                    target.guns += delta;
                    target.UpdateStatsGuns(adjuststatdata: false);

                    var source = __state.Source;
                    if ((UnityEngine.Object)(object)source != (UnityEngine.Object)null && TotalMen(source) > 0)
                    {
                        source.guns = Math.Max(0, source.guns - delta);
                        source.UpdateStatsGuns(adjuststatdata: false);
                    }

                    OnceLog.Info("artillery-combine-guns", "[Patch:ArtilleryCombine] preserved source artillery guns during CombineUnits");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("artillery-combine-guns:postfix", "[Patch:ArtilleryCombine] postfix failed: " + ex.Message);
                }
            }
        }

        private static int TotalMen(Regiment unit)
        {
            return Math.Max(0, unit.strength) + Math.Max(0, unit.losses[0]) + Math.Max(0, unit.sick);
        }

        private static bool Enabled()
        {
            try { return Plugin.Instance != null && Plugin.Instance.Enabled.Value; }
            catch { return false; }
        }

        internal readonly struct State
        {
            internal readonly Regiment Source;
            internal readonly Regiment Target;
            internal readonly int SourceMenBefore;
            internal readonly int TargetMenBefore;
            internal readonly int SourceGunsBefore;
            internal readonly int TargetGunsBefore;

            internal State(
                Regiment source,
                Regiment target,
                int sourceMenBefore,
                int targetMenBefore,
                int sourceGunsBefore,
                int targetGunsBefore)
            {
                Source = source;
                Target = target;
                SourceMenBefore = sourceMenBefore;
                TargetMenBefore = targetMenBefore;
                SourceGunsBefore = sourceGunsBefore;
                TargetGunsBefore = targetGunsBefore;
            }

            internal bool Valid => SourceMenBefore > 0 && SourceGunsBefore > 0;
        }
    }
}
