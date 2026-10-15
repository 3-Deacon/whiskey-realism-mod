using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Patches
{
    // Prefix-snapshot / Finalizer-restore state for EngineerBranchBonusPatch (BUG-AI-014). No
    // [HarmonyPatch]; generic over the commander type so the harness runs it on stand-ins.
    // Built on LogisticsSwaps' FieldMask. Main thread only; one instance per patched method.
    //
    // For one call vanilla's "historicalbranch == 4" test must mean "engineer": navy officers
    // are masked first (4 -> NavyMaskedBranch), then engineers (3 -> 4), so the engineers just
    // written as 4 are not caught by the navy mask. Restore undoes engineers, then navy, each
    // only where the mask value is still there.
    internal sealed class EngineerBranchMasks<TItem> where TItem : class
    {
        private readonly FieldMask<TItem, int> _navy;
        private readonly FieldMask<TItem, int> _engineers;
        private readonly Func<TItem, int> _get;

        internal EngineerBranchMasks(Func<TItem, int> get, Action<TItem, int> set)
        {
            _get = get;
            _navy = new FieldMask<TItem, int>(get, set, TerrainBugFixes.NavyMaskedBranch);
            _engineers = new FieldMask<TItem, int>(get, set, TerrainBugFixes.BranchNavy);
        }

        internal bool Active => _navy.Active || _engineers.Active;

        // Returns the number of engineers now reading as 4.
        internal int Apply(IList<TItem> commanders)
        {
            if (commanders == null) return 0;
            _navy.Mask(commanders, c => _get(c) == TerrainBugFixes.BranchNavy);
            return _engineers.Mask(commanders, c => _get(c) == TerrainBugFixes.BranchEngineer);
        }

        internal string Restore()
        {
            string failure = _engineers.Restore(c => c == null);
            return _navy.Restore(c => c == null) ?? failure;
        }
    }
}
