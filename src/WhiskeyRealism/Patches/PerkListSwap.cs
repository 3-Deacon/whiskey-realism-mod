using System;
using System.Collections.Generic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Prefix-snapshot / Finalizer-restore of Regiment.perks for the perk-id patches (no
    // [HarmonyPatch]). Hide points a unit's perks list at an empty list for the length of one
    // vanilla call, so every HasPerk there answers false; Restore puts each list back, only
    // where the unit still holds the empty list. One instance per patch; main thread only.
    internal sealed class PerkListSwap
    {
        private static readonly List<Regiment.Perk> NoPerks = new List<Regiment.Perk>();

        private readonly string _logKey;
        private readonly List<Regiment> _units = new List<Regiment>();
        private readonly List<List<Regiment.Perk>> _saved = new List<List<Regiment.Perk>>();

        internal PerkListSwap(string logKey)
        {
            _logKey = logKey;
        }

        internal bool Active => _units.Count > 0;

        internal void Hide(Regiment unit)
        {
            _units.Add(unit);
            _saved.Add(unit.perks);
            unit.perks = NoPerks;
        }

        internal void Restore()
        {
            for (int i = 0; i < _units.Count; i++)
            {
                try
                {
                    Regiment unit = _units[i];
                    if ((object)unit != null && ReferenceEquals(unit.perks, NoPerks))
                        unit.perks = _saved[i];
                }
                catch (Exception ex)
                {
                    OnceLog.Warning(_logKey, "[Patch:PerkIds] perks restore failed: " + ex.Message);
                }
            }
            _units.Clear();
            _saved.Clear();
        }
    }
}
