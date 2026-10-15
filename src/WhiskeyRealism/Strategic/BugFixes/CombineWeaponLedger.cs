using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.BugFixes
{
    // BUG-BAT-003. Vanilla BattleUnits.CombineUnits (93153) moves weapons through the
    // alliance's per-model stock (Alliance.weaponstock, a running total; 62244):
    // - the source returns `num5` pieces: for artillery its whole statsguns even when only
    //   part of the crew moves (93213), so the guns it keeps are counted twice;
    // - the target takes `num5` pieces of its own model (93222-93228); for artillery that is
    //   the source's whole gun count, not the guns the target actually gains;
    // - when the target's model is short it returns the target's holding and re-arms it with
    //   the free standard weapon (93229-93240), but returns its MEN (`sick + strength +
    //   losses[0]`, 93232) where artillery stock counts guns (statsguns, as DeleteUnit 90631
    //   and the alliance tick 81586 do): ~100-240 phantom guns per merge. A target that
    //   already carries the standard weapon is "re-armed" with the same model, so its whole
    //   holding is added to stock for nothing.
    //
    // The fixed ledger counts pieces the way vanilla does everywhere else (statsguns for
    // artillery, men for the rest), moves only what changes hands, and keeps vanilla's rule
    // that a target short of its model's stock is re-armed with the standard weapon. The
    // model is kept where vanilla means to keep it: when the stock covers the pieces the
    // target actually gains, when both units carry the same model (the source's pieces stay
    // with the merged unit) and when the target already carries the standard weapon (new
    // men get it free, as recruits do, CreateUnitDefault 89508). weaponunitsequipped is not
    // corrected: the alliance tick rebuilds it from scratch (81520).
    internal sealed class CombineArming
    {
        internal bool IsArtillery;
        internal int SourceWeapon = -1;
        internal int TargetWeapon = -1;
        internal int StandardWeapon = -1;
        internal int SourcePiecesBefore;
        internal int TargetPiecesBefore;
        // weaponstock before the call, for every id above that is >= 0.
        internal readonly Dictionary<int, float> StockBefore = new Dictionary<int, float>();
    }

    internal sealed class CombineSettlement
    {
        internal int TargetWeapon;
        internal bool Downgraded;
        // weaponstock after the call, for every id in CombineArming.StockBefore.
        internal readonly Dictionary<int, float> Stock = new Dictionary<int, float>();
    }

    internal static class CombineWeaponLedger
    {
        // The pieces a unit holds: guns for a battery, men (strength + wounded + sick) otherwise.
        internal static int Pieces(bool isArtillery, int strength, int wounded, int sick, int statsGuns)
        {
            return isArtillery ? statsGuns : strength + wounded + sick;
        }

        // The correct outcome of one CombineUnits call, from the units' pieces before and after
        // it. sourcePiecesAfter is 0 when vanilla deleted the emptied source. A call that moved
        // nothing settles to the state before it.
        internal static CombineSettlement Settle(CombineArming before, int sourcePiecesAfter, int targetPiecesAfter)
        {
            var result = new CombineSettlement { TargetWeapon = before != null ? before.TargetWeapon : -1 };
            if (before == null) return result;
            foreach (var kv in before.StockBefore) result.Stock[kv.Key] = kv.Value;

            int given = Math.Max(0, before.SourcePiecesBefore - Math.Max(0, sourcePiecesAfter));
            int gained = Math.Max(0, targetPiecesAfter - before.TargetPiecesBefore);
            int source = before.SourceWeapon;
            int target = before.TargetWeapon;

            if (source >= 0) Add(result.Stock, source, given);
            if (target < 0) return Clamped(result);

            bool sameModel = source == target;
            bool covered = Get(result.Stock, target) >= gained;
            if (sameModel || covered)
            {
                Add(result.Stock, target, -gained);
            }
            else if (target != before.StandardWeapon)
            {
                Add(result.Stock, target, before.TargetPiecesBefore);
                result.TargetWeapon = before.StandardWeapon;
                result.Downgraded = true;
            }
            return Clamped(result);
        }

        private static CombineSettlement Clamped(CombineSettlement result)
        {
            var keys = new List<int>(result.Stock.Keys);
            foreach (int id in keys)
                if (result.Stock[id] < 0f) result.Stock[id] = 0f;
            return result;
        }

        private static float Get(Dictionary<int, float> stock, int id)
        {
            return stock.TryGetValue(id, out float value) ? value : 0f;
        }

        private static void Add(Dictionary<int, float> stock, int id, float amount)
        {
            stock[id] = Get(stock, id) + amount;
        }
    }
}
