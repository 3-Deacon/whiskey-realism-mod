// BC3 → BC1 seam (docs/superpowers/plans/2026-10-15-battle-core.md, BC1
// "Interfaces — Produces"). BC3's snapshot overloads
// (PlayerOwnership.ApplyChainFlags(BattleSnapshot, …) and
// TacticalChainOrderPlanner.ResolveTargets(BattleSnapshot, …)) read only the two
// members below, with BC1's exact signatures. BC1's BattleSnapshot.cs declares and
// fills them; when BC1 and BC3 meet on feat/battle-core the compiler reports a
// duplicate member (CS0102) here — delete this file at that merge.

namespace WhiskeyRealism.Tactical.Battle
{
    public sealed partial class BattleSnapshot
    {
        public UnitRecord[] Own { get; }
        public int OwnCount { get; }
    }
}
