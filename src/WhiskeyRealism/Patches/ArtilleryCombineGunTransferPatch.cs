using System;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Strategic.BugFixes;
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
    //
    // BUG-BAT-003, same call: vanilla's weapon-stock bookkeeping in CombineUnits (93205-93241)
    // returns a battery's MEN to gun stock when the target's model is short (93232), returns
    // a partly merged source's whole gun count (93213), draws the source's whole gun count
    // for the target (93222-93228), and re-arms a target that already carries the standard
    // weapon "for free" after adding its whole holding to stock. The Prefix snapshots both
    // units' weapons and pieces and the stock of the models involved; the Postfix sets those
    // stock cells (and the target's weapon) to the pure CombineWeaponLedger outcome. Every
    // early return of vanilla moves nothing and settles to the snapshot, which vanilla left
    // as is. weaponunitsequipped is rebuilt every alliance tick (81520) and is left alone.
    // Same rule for every side, W&L player merges included (vanilla's own merge rules).
    // Rollback: Campaign Bug Fixes / Enable Combine Weapon Stock Fix.
    [HarmonyPatch(typeof(BattleUnits), "CombineUnits")]
    internal static class ArtilleryCombineGunTransferPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(Regiment unitsource, Regiment unitto, out CombineArming __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.arty-combine-gun-transfer.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.arty-combine-gun-transfer.prefix");
                __state = null;
                try
                {
                    if (StockFixEnabled()) __state = Snapshot(unitsource, unitto);
                }
                catch (Exception ex)
                {
                    __state = null;
                    OnceLog.Warning("combine-weapon-stock:prefix", "[Patch:CombineWeaponStock] snapshot failed; this merge keeps vanilla's stock: " + ex.Message);
                }
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(Regiment unitsource, Regiment unitto, bool __result, CombineArming __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.arty-combine-gun-transfer.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.arty-combine-gun-transfer.postfix");
                try
                {
                    if (Enabled() && (UnityEngine.Object)unitto != null && unitto.unittyp == 2)
                    {
                        bool changed = SyncGuns(unitto);
                        // CombineUnits returns true when it deleted an emptied source.
                        if (!__result && (UnityEngine.Object)unitsource != null && unitsource.unittyp == 2)
                            changed |= SyncGuns(unitsource);

                        if (changed)
                            OnceLog.Info("artillery-combine-guns", "[Patch:ArtilleryCombine] synced battery guns to GetSollGuns after CombineUnits");
                    }
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("artillery-combine-guns:postfix", "[Patch:ArtilleryCombine] postfix failed: " + ex.Message);
                }

                try
                {
                    if (__state != null) SettleStock(unitsource, unitto, __result, __state);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("combine-weapon-stock:postfix", "[Patch:CombineWeaponStock] stock correction failed: " + ex.Message);
                }
            }
        }

        // Null (vanilla stock kept) unless both units are basic units of one alliance.
        private static CombineArming Snapshot(Regiment source, Regiment target)
        {
            if ((UnityEngine.Object)source == null || (UnityEngine.Object)target == null) return null;
            if (source.unittyp != target.unittyp || source.unittyp > 13) return null;
            if (source.alliance != target.alliance)
            {
                OnceLog.Warning("combine-weapon-stock:alliance", "[Patch:CombineWeaponStock] merge across alliances; vanilla's stock kept");
                return null;
            }
            var alliance = AllianceOf(target);
            if (alliance == null || alliance.weaponstock == null || alliance.standardweapons == null) return null;

            bool artillery = target.unittyp == 2;
            var arming = new CombineArming
            {
                IsArtillery = artillery,
                SourceWeapon = source.weapon,
                TargetWeapon = target.weapon,
                StandardWeapon = target.unittyp < alliance.standardweapons.Length ? alliance.standardweapons[target.unittyp] : -1,
                SourcePiecesBefore = Pieces(source, artillery),
                TargetPiecesBefore = Pieces(target, artillery)
            };
            foreach (int id in new[] { arming.SourceWeapon, arming.TargetWeapon, arming.StandardWeapon })
            {
                if (id < 0 || arming.StockBefore.ContainsKey(id)) continue;
                if (id >= alliance.weaponstock.Length) return null;
                arming.StockBefore[id] = alliance.weaponstock[id];
            }
            return arming;
        }

        private static void SettleStock(Regiment source, Regiment target, bool sourceDeleted, CombineArming before)
        {
            if ((UnityEngine.Object)target == null) return;
            var alliance = AllianceOf(target);
            if (alliance == null || alliance.weaponstock == null) return;

            bool sourceAlive = !sourceDeleted && (UnityEngine.Object)source != null;
            int sourceAfter = sourceAlive ? Pieces(source, before.IsArtillery) : 0;
            var settled = CombineWeaponLedger.Settle(before, sourceAfter, Pieces(target, before.IsArtillery));

            bool changed = false;
            foreach (KeyValuePair<int, float> cell in settled.Stock)
            {
                if (alliance.weaponstock[cell.Key] == cell.Value) continue;
                alliance.weaponstock[cell.Key] = cell.Value;
                changed = true;
            }
            if (target.weapon != settled.TargetWeapon)
            {
                target.weapon = settled.TargetWeapon;
                changed = true;
            }
            if (changed)
                OnceLog.Info("combine-weapon-stock", "[Patch:CombineWeaponStock] corrected weapon stock after CombineUnits (guns counted as guns, model kept where stock covers the merge)");
        }

        private static int Pieces(Regiment unit, bool artillery)
        {
            return CombineWeaponLedger.Pieces(artillery, unit.strength, unit.losses != null && unit.losses.Length > 0 ? unit.losses[0] : 0, unit.sick, unit.statsguns);
        }

        private static GameVars.Alliance AllianceOf(Regiment unit)
        {
            var alliances = GameVars.alliance;
            if (alliances == null || unit.alliance < 0 || unit.alliance >= alliances.Length) return null;
            return alliances[unit.alliance];
        }

        private static bool StockFixEnabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableCombineWeaponStockFix != null
                    && Plugin.EnableCombineWeaponStockFix.Value;
            }
            catch { return false; }
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
