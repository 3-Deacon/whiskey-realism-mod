using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// One side's battle-AI tier state (BC15). Two kinds of data:
    /// per group instance id, what the tier pass remembers between ticks (its tier, the hour its producers last ran, the hour a cause last kept
    /// it Full, and the input signature of that last evaluation that BattleLod.Classify compares the current inputs with);
    /// per own index of the current snapshot, this tick's answer (tier, due, the reason a group was promoted), which the producers read through
    /// <see cref="IsDue"/>. Pure; BattleScope-registered through <see cref="ForAlliance"/>.
    /// </summary>
    public sealed class BattleLodState
    {
        private static readonly BattleLodState[] ByAlliance = { new BattleLodState(), new BattleLodState() };

        static BattleLodState()
        {
            BattleScope.Register("battle-lod-state", ResetBothSides);
        }

        /// <summary>Alliances 0 and 1 only; Europe (2) and anything else get null.</summary>
        public static BattleLodState ForAlliance(int alliance)
        {
            return alliance >= 0 && alliance < ByAlliance.Length ? ByAlliance[alliance] : null;
        }

        private static void ResetBothSides()
        {
            for (int a = 0; a < ByAlliance.Length; a++) ByAlliance[a].Clear();
        }

        /// <summary>What the tier pass keeps for a group between ticks; the inputs a producer reads, as they were at its last evaluation.</summary>
        internal struct GroupLod
        {
            public LodTier Tier;
            /// <summary>The doctrine hour the group's producers last ran (a Full group's every tick; a Distant group's due ticks).</summary>
            public double LastEvalHours;
            /// <summary>The last doctrine hour a cause (a promotion trigger or a failed Distant condition) kept the group Full.</summary>
            public double LastCauseHours;
            /// <summary>The group's own index when it entered Distant, mod <see cref="BattleLod.IntervalTicks"/>: spreads the due ticks.</summary>
            public int Stagger;
            public bool NavDeferred;
            public bool HasSignature;
            public LodSignature Signature;
        }

        /// <summary>The inputs of a group's producers that BattleLod.Classify compares tick to tick (triggers b-e).</summary>
        internal struct LodSignature
        {
            public CommandTaskType Task;
            public bool HasTarget;
            public float TargetX, TargetZ, ManeuverHeading;
            public int OrderFormation;
            public bool HasSlot;
            public float SlotX, SlotZ, SlotHeading;
            public bool HasEnemy;
            public float EnemyBearing, EnemyDistance;
            public int CorridorCount, CorridorHash;
        }

        private GroupLod[] _groups = new GroupLod[64];
        private int _groupCount;
        private readonly Dictionary<int, int> _slotById = new Dictionary<int, int>(128);

        // This tick's answer, by own index of the snapshot BattleLod.Classify read.
        private LodTier[] _tier = new LodTier[BattleSnapshot.InitialCapacity];
        private bool[] _due = new bool[BattleSnapshot.InitialCapacity];
        private LodPromotion[] _last = new LodPromotion[BattleSnapshot.InitialCapacity];

        // Classify's scratch (grow-only): the folds over the command tree, the latched and remembered observations.
        internal bool[] Near = new bool[BattleSnapshot.InitialCapacity];
        internal bool[] Hot = new bool[BattleSnapshot.InitialCapacity];
        internal float[] SubtreeRadius = new float[BattleSnapshot.InitialCapacity];
        internal float[] ChangedX = new float[64];
        internal float[] ChangedZ = new float[64];
        internal float[] RememberedX = new float[64];
        internal float[] RememberedZ = new float[64];

        private int _planSignature;
        private bool _hasPlanSignature;
        private readonly int[] _promotionCounts = new int[LodPromotionReasons];

        /// <summary>The number of single <see cref="LodPromotion"/> flags (LodPromotion.PlayerChain is 1 &lt;&lt; 10).</summary>
        public const int LodPromotionReasons = 11;

        /// <summary>Snapshot id of the last Classify; a snapshot whose id differs was not classified (every group is due).</summary>
        public long SnapshotTickId { get; private set; } = -1;
        public int ClassifiedOwnCount { get; private set; }
        public double CycleHours { get; private set; }

        public int FullCount { get; internal set; }
        public int DistantCount { get; internal set; }
        public int DueCount { get; internal set; }
        public int CarriedCount => DistantCount - DueCount;
        public int NavDeferredCount { get; internal set; }

        // ---- the producers' read side ----------------------------------------------------------------

        /// <summary>
        /// Whether the producers evaluate own unit <paramref name="ownIndex"/> this tick. A regiment answers with the tier of its command group.
        /// True for anything the last Classify did not cover (a snapshot it did not read, an index out of range): fail toward full rate.
        /// </summary>
        public bool IsDue(int ownIndex)
        {
            return ownIndex < 0 || ownIndex >= ClassifiedOwnCount || _due[ownIndex];
        }

        public LodTier TierOf(int ownIndex)
        {
            return ownIndex >= 0 && ownIndex < ClassifiedOwnCount ? _tier[ownIndex] : LodTier.Full;
        }

        public LodPromotion LastPromotion(int ownIndex)
        {
            return ownIndex >= 0 && ownIndex < ClassifiedOwnCount ? _last[ownIndex] : LodPromotion.None;
        }

        /// <summary>Whether the last Classify read this snapshot, so <see cref="IsDue"/> speaks for it.</summary>
        public bool Covers(BattleSnapshot s)
        {
            return s != null && s.TickId == SnapshotTickId && s.OwnCount == ClassifiedOwnCount;
        }

        /// <summary>
        /// A producer ahead of this one changed what a group's producers read in this very tick (the opportunity producer launched an exploit on
        /// it): the group is Full for the rest of the tick and for the minimum hold. False when it was already due.
        /// </summary>
        public bool PromoteNow(BattleSnapshot s, int ownIndex, LodPromotion reason, double nowHours)
        {
            if (!Covers(s) || ownIndex < 0 || ownIndex >= ClassifiedOwnCount || _due[ownIndex]) return false;
            UnitRecord[] own = s.Own;
            if (!TryEntry(own[ownIndex].InstanceId, out int slot)) return false;
            ref GroupLod g = ref _groups[slot];
            g.Tier = LodTier.Full;
            g.LastEvalHours = nowHours;
            g.LastCauseHours = nowHours;
            g.HasSignature = false;
            SetUnitAnswer(s, ownIndex, LodTier.Full, true, reason);
            DistantCount--;
            FullCount++;
            DueCount++;
            CountPromotion(reason);
            return true;
        }

        // ---- the nav budget's report -----------------------------------------------------------------

        /// <summary>
        /// The posture producer deferred this group's NavMesh plan (its per-tick budget was spent). The group stays Full until a plan runs for
        /// it (<see cref="MarkNavPlanned"/>): a deferral is an order the group has not got yet.
        /// </summary>
        public void MarkNavDeferred(int groupId)
        {
            ref GroupLod g = ref EntryFor(groupId, 0.0);
            g.NavDeferred = true;
        }

        /// <summary>A plan ran for the group, or its evaluation needed none: the deferral is over.</summary>
        public void MarkNavPlanned(int groupId)
        {
            if (_slotById.TryGetValue(groupId, out int slot)) _groups[slot].NavDeferred = false;
        }

        // ---- reset ----------------------------------------------------------------------------------

        /// <summary>
        /// Every group Full, with no remembered signature, for the minimum hold: a stand-down of the side and a new battle day. The caller drops
        /// the carried claims (a stand-down cleared them already).
        /// </summary>
        public void ResetAllFull(double nowHours)
        {
            for (int g = 0; g < _groupCount; g++)
            {
                _groups[g].Tier = LodTier.Full;
                _groups[g].LastEvalHours = nowHours;
                _groups[g].LastCauseHours = nowHours;
                _groups[g].HasSignature = false;
                _groups[g].NavDeferred = false;
            }
            _hasPlanSignature = false;
            SnapshotTickId = -1;
            ClassifiedOwnCount = 0;
            FullCount = DistantCount = DueCount = NavDeferredCount = 0;
        }

        /// <summary>The battle ended: forget everything.</summary>
        public void Clear()
        {
            _groupCount = 0;
            _slotById.Clear();
            _hasPlanSignature = false;
            SnapshotTickId = -1;
            ClassifiedOwnCount = 0;
            CycleHours = 0.0;
            FullCount = DistantCount = DueCount = NavDeferredCount = 0;
            Array.Clear(_promotionCounts, 0, _promotionCounts.Length);
        }

        // ---- the telemetry window ---------------------------------------------------------------------

        /// <summary>Promotions of previously Distant groups by reason since <see cref="ClearPromotionCounts"/>; <paramref name="reason"/> is one flag.</summary>
        public int PromotionCountOf(LodPromotion reason)
        {
            int bit = BitIndex(reason);
            return bit >= 0 ? _promotionCounts[bit] : 0;
        }

        public void ClearPromotionCounts()
        {
            Array.Clear(_promotionCounts, 0, _promotionCounts.Length);
        }

        // ---- BattleLod.Classify's side ----------------------------------------------------------------

        internal void BeginTick(BattleSnapshot s, double cycleHours)
        {
            int n = s.OwnCount;
            if (_tier.Length < n)
            {
                int size = Math.Max(n, _tier.Length * 2);
                _tier = new LodTier[size];
                _due = new bool[size];
                _last = new LodPromotion[size];
                Near = new bool[size];
                Hot = new bool[size];
                SubtreeRadius = new float[size];
            }
            Array.Clear(_tier, 0, n);
            Array.Clear(Near, 0, n);
            Array.Clear(Hot, 0, n);
            Array.Clear(_last, 0, n);
            for (int i = 0; i < n; i++) _due[i] = true;
            SnapshotTickId = s.TickId;
            ClassifiedOwnCount = n;
            CycleHours = cycleHours;
            FullCount = DistantCount = DueCount = NavDeferredCount = 0;
        }

        /// <summary>The side's plan signature changed since the last tick (BC15 decision 2(f)); false on the first tick and after a reset.</summary>
        internal bool NotePlanSignature(int signature)
        {
            bool changed = _hasPlanSignature && _planSignature != signature;
            _planSignature = signature;
            _hasPlanSignature = true;
            return changed;
        }

        /// <summary>The group's persisted entry; a new group starts Full with a cause at <paramref name="nowHours"/>, so it holds Full for the minimum hold.</summary>
        internal ref GroupLod EntryFor(int groupId, double nowHours)
        {
            if (!_slotById.TryGetValue(groupId, out int slot))
            {
                if (_groupCount == _groups.Length) Array.Resize(ref _groups, _groups.Length * 2);
                slot = _groupCount++;
                _groups[slot] = new GroupLod { Tier = LodTier.Full, LastEvalHours = nowHours, LastCauseHours = nowHours };
                _slotById[groupId] = slot;
            }
            return ref _groups[slot];
        }

        private bool TryEntry(int groupId, out int slot)
        {
            return _slotById.TryGetValue(groupId, out slot);
        }

        internal void SetUnitAnswer(BattleSnapshot s, int ownIndex, LodTier tier, bool due, LodPromotion why)
        {
            _tier[ownIndex] = tier;
            _due[ownIndex] = due;
            _last[ownIndex] = why;
        }

        /// <summary>One classified group into this tick's counts (the TacticalLod row and the first-fire line read them).</summary>
        internal void CountGroup(LodTier tier, bool due, bool navDeferred)
        {
            if (tier == LodTier.Distant)
            {
                DistantCount++;
                if (due) DueCount++;
            }
            else
            {
                FullCount++;
            }
            if (navDeferred) NavDeferredCount++;
        }

        internal void CountPromotion(LodPromotion reasons)
        {
            for (int bit = 0; bit < LodPromotionReasons; bit++)
                if (((int)reasons & (1 << bit)) != 0) _promotionCounts[bit]++;
        }

        internal void EnsureChangedCapacity(int needed)
        {
            if (ChangedX.Length >= needed) return;
            int size = Math.Max(needed, ChangedX.Length * 2);
            Array.Resize(ref ChangedX, size);
            Array.Resize(ref ChangedZ, size);
        }

        internal void EnsureRememberedCapacity(int needed)
        {
            if (RememberedX.Length >= needed) return;
            int size = Math.Max(needed, RememberedX.Length * 2);
            Array.Resize(ref RememberedX, size);
            Array.Resize(ref RememberedZ, size);
        }

        private static int BitIndex(LodPromotion single)
        {
            int v = (int)single;
            if (v <= 0 || (v & (v - 1)) != 0) return -1;
            int bit = 0;
            while ((v >>= 1) != 0) bit++;
            return bit < LodPromotionReasons ? bit : -1;
        }
    }
}
