using System;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #34 — Vanilla BattleUnits.CombineUnits (decompile 93153) moves artillery
    // crew strength between batteries and only refreshes statsguns
    // (UpdateStatsGuns 135252), leaving `guns` at the pre-combine value until
    // the next Regiment.Initialize recomputes it as
    // GetSollGuns(forinitialization: true) (112681). This Postfix applies that
    // same derivation right after the combine on the campaign map (pure
    // BattleWriterPolicy.GunsAfterCombine), so the UI and the next load agree.
    // The previous proportional "guns += delta" edit was not durable: Initialize
    // overwrote it on load. Rollback: Campaign / Enable Artillery Combine Gun Sync.
    [HarmonyPatch(typeof(BattleUnits), "CombineUnits")]
    internal static class ArtilleryCombineGunTransferPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(Regiment unitsource, Regiment unitto, bool __result)
        {
            using (TelemetryPerf.Scope("campaign.patch.arty-combine-gun-transfer.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    if (!Enabled()) return;
                    if ((UnityEngine.Object)unitto == null || unitto.unittyp != 2) return;

                    bool changed = SyncGuns(unitto);
                    // CombineUnits returns true when it deleted an emptied source.
                    if (!__result && (UnityEngine.Object)unitsource != null && unitsource.unittyp == 2)
                        changed |= SyncGuns(unitsource);

                    if (changed)
                        OnceLog.Info("artillery-combine-guns", "[Patch:ArtilleryCombine] synced battery guns to GetSollGuns after CombineUnits");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("artillery-combine-guns:postfix", "[Patch:ArtilleryCombine] postfix failed: " + ex.Message);
                }
            }
        }

        private static bool SyncGuns(Regiment unit)
        {
            if (!unit.calledfromcampaign) return false;
            int guns = BattleWriterPolicy.GunsAfterCombine(
                isArtillery: true,
                calledFromCampaign: true,
                sollGuns: unit.GetSollGuns(forinitialization: true),
                currentGuns: unit.guns);
            if (guns == unit.guns) return false;

            unit.guns = guns;
            unit.UpdateStatsGuns(adjuststatdata: false);
            return true;
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableArtilleryCombineGunSync != null
                    && Plugin.EnableArtilleryCombineGunSync.Value;
            }
            catch { return false; }
        }
    }
}
