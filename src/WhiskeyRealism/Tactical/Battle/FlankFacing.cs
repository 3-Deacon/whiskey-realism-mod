// BC11 (docs/superpowers/plans/2026-10-15-battle-core.md § BC11): the AI answers a flank threat as a
// formation. Pure: no Unity or vanilla types.
//
// Vanilla anchors (decompile, build 14703111):
//   StopMicroUnitsCloseToEnemyAndRotateTowards 4539-4613: vanilla's only live turning is per
//     regiment toward its own closest enemy (within distancetoturntowardsenemy, or its fire range
//     once it has received fire, 4560-4565; when the angle is at least
//     angletriggerforturningtowardsenemy, plus additionalanglewhenincovermicro in cover, and the
//     enemy is within 1.5 x fire range with no path, 4599-4608). Its group change of front
//     (UpdateDefensivePositions 4615 -> CheckDefensiveFacing) is dead (4617, 5101-5115).
//   SetGroupFormation 91822-92110 (refuseflank 92095-92097) -> SkewedPosition 78109-78168: a refused
//     flank moves each child straight back from its slot by pow(t | 1 - t, skew) x width x depth and
//     turns it to the skewed line.
//   UpdateGroupStrength 120135-120580: flanksthreated adds the GROUP's own outflanked once per
//     counted subordinate (120566-120580); the intended weight is each subordinate's own.

using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// The inputs of vanilla's per-regiment micro turn (4560-4608): 40 m, 25 degrees, the cover pair
    /// (GamePrefs.additionalanglewhenincovermicro / maxcovervalueforgrouprotation) and 1.5 x fire range.
    /// </summary>
    public struct MicroTurnRule
    {
        public float DistanceMeters, AngleDegrees, CoverExtraDegrees, MaxCoverValue, FireRangeFactor;

        public const float FireRangeFactorShipped = 1.5f;

        public static MicroTurnRule From(float distanceMeters, float angleDegrees, float coverExtraDegrees, float maxCoverValue)
        {
            return new MicroTurnRule
            {
                DistanceMeters = distanceMeters, AngleDegrees = angleDegrees, CoverExtraDegrees = coverExtraDegrees,
                MaxCoverValue = maxCoverValue, FireRangeFactor = FireRangeFactorShipped,
            };
        }
    }

    /// <summary>
    /// Per-regiment memory of the in-combat refuse. The micro-turn check (<see cref="FlankFacing.VanillaTurnsBack"/>) judges the
    /// snapshot, which holds only the perceived enemies, while vanilla's own micro turn (4539-4613) reads every enemy: a regiment
    /// can be turned straight back by an enemy the side does not see, and re-rotating it every cooldown would be a tug of war.
    /// This remembers the rotation each regiment was ordered to. A regiment later found settled away from it (vanilla or the
    /// player turned it) is not rotated again for <see cref="BackOffHours"/> of doctrine time, or until the rotation wanted
    /// changes by more than the executor's facing tolerance (the threat moved), whichever comes first. Fixed arrays, no allocation
    /// after construction; an instance is owned by OrderExecutorRuntime and cleared with the battle.
    /// </summary>
    public sealed class RefuseTurnBackMemory
    {
        public const int Capacity = 64;
        /// <summary>Six battle minutes on the doctrine clock.</summary>
        public const double BackOffHours = 0.1;

        private readonly int[] _id = new int[Capacity];
        private readonly float[] _orderedTo = new float[Capacity];
        private readonly float[] _wantedAtBackOff = new float[Capacity];
        private readonly double[] _backOffUntil = new double[Capacity];
        private int _count;
        private int _next;

        public int Count => _count;

        /// <summary>
        /// The executor rotated the regiment to <paramref name="rotation"/>; any earlier back-off ends. A full table makes room
        /// without forgetting an active back-off: an expired back-off goes first, then a remembered rotation that is not backed off
        /// (the oldest), and only when all <see cref="Capacity"/> entries are active the one closest to expiry.
        /// </summary>
        public void NoteRotated(int instanceId, float rotation, double nowHours)
        {
            int slot = Find(instanceId);
            if (slot < 0)
            {
                if (_count < Capacity) slot = _count++;
                else slot = Evict(nowHours);
                _id[slot] = instanceId;
            }
            _orderedTo[slot] = rotation;
            _backOffUntil[slot] = -1.0;
        }

        private int Evict(double nowHours)
        {
            int expired = -1, plain = -1, soonest = -1;
            for (int k = 0; k < _count; k++)
            {
                int i = (_next + k) % _count;
                double until = _backOffUntil[i];
                if (until < 0.0)
                {
                    if (plain < 0) plain = i;
                    continue;
                }
                if (nowHours >= until || nowHours < until - BackOffHours)
                {
                    if (expired < 0) expired = i;
                }
                else if (soonest < 0 || until < _backOffUntil[soonest]) soonest = i;
            }
            int slot = expired >= 0 ? expired : plain >= 0 ? plain : soonest;
            _next = (slot + 1) % _count;
            return slot;
        }

        /// <summary>
        /// True when the regiment must not be rotated now. A regiment settled away from the rotation it was ordered to has been
        /// turned back: it enters a back-off (and is skipped) until <see cref="BackOffHours"/> pass or
        /// <paramref name="wantedRotation"/> moves by more than the facing tolerance from the one it was turned back against.
        /// </summary>
        public bool IsBackedOff(int instanceId, float currentRotation, float wantedRotation, double nowHours)
        {
            int slot = Find(instanceId);
            if (slot < 0) return false;
            if (_backOffUntil[slot] >= 0.0)
            {
                if (nowHours >= _backOffUntil[slot] || nowHours < _backOffUntil[slot] - BackOffHours
                    || FlankFacing.AngleBetween(wantedRotation, _wantedAtBackOff[slot]) > OrderExecutor.FacingToleranceDegrees)
                {
                    Remove(slot);
                    return false;
                }
                return true;
            }
            if (FlankFacing.AngleBetween(currentRotation, _orderedTo[slot]) <= OrderExecutor.FacingToleranceDegrees) return false;
            _backOffUntil[slot] = nowHours + BackOffHours;
            _wantedAtBackOff[slot] = wantedRotation;
            return true;
        }

        public void Clear()
        {
            _count = 0;
            _next = 0;
        }

        private int Find(int instanceId)
        {
            for (int i = 0; i < _count; i++)
                if (_id[i] == instanceId) return i;
            return -1;
        }

        private void Remove(int slot)
        {
            _count--;
            if (slot != _count)
            {
                _id[slot] = _id[_count];
                _orderedTo[slot] = _orderedTo[_count];
                _wantedAtBackOff[slot] = _wantedAtBackOff[_count];
                _backOffUntil[slot] = _backOffUntil[_count];
            }
            if (_next >= _count) _next = 0;
        }
    }

    /// <summary>
    /// The throttle of the in-place refuse row. The row repeats every refuse cooldown for a group that stays in combat, so the
    /// repeat test must not build a string: it compares the numeric tuple (flank, rotated, skipped) with the last row of the
    /// same group and emits again only when the tuple changed or <c>minSeconds</c> passed. Allocation-free after the group's
    /// first row (a dictionary lookup on a long key).
    /// </summary>
    public sealed class RefuseRowThrottle
    {
        private struct Row
        {
            public float At;
            public RefuseFlank Side;
            public int Rotated, Skipped;
        }

        private readonly System.Collections.Generic.Dictionary<long, Row> _rows = new System.Collections.Generic.Dictionary<long, Row>();

        public bool ShouldEmit(int alliance, int instanceId, RefuseFlank side, int rotated, int skipped, float nowSeconds, float minSeconds)
        {
            long key = ((long)alliance << 32) | (uint)instanceId;
            if (_rows.TryGetValue(key, out Row last) && last.Side == side && last.Rotated == rotated && last.Skipped == skipped
                && nowSeconds - last.At < minSeconds && nowSeconds >= last.At)
                return false;
            _rows[key] = new Row { At = nowSeconds, Side = side, Rotated = rotated, Skipped = skipped };
            return true;
        }

        public void Clear() => _rows.Clear();
    }

    public static class FlankFacing
    {
        /// <summary>The window holding the unit's facing keeps it while it has at least this share of the best window (a turn needs 1.25 x).</summary>
        public const float KeepShare = 0.8f;
        public const int WindowBins = 3;
        /// <summary>A regiment is refused only when its setback is at least this share of the refuse depth.</summary>
        public const float RefuseWingShare = 0.1f;
        public const int MaxRefuseRotationsPerTick = 4;

        // ---- pressure facing (decision 1) -------------------------------------------------

        /// <summary>
        /// The bearing a holding group faces: over the unit's slice bins (bin j centred on
        /// j x w + w / 2 degrees, w = SliceCount, so the shipped 20 fills bins 0-17) the 3-bin
        /// window with the most pressure (Visible full, Remembered x 0.5 x confidence), as the
        /// window's pressure-weighted circular mean. Hysteresis: <paramref name="currentFacing"/>
        /// stays while the window centred on it holds at least <see cref="KeepShare"/> of the best
        /// window. False when nothing presses within the slice range (the caller falls back to the
        /// closest visible enemy). <paramref name="windowShare"/> is the current window's share of the best.
        /// </summary>
        public static bool MostPressuredBearing(BattleSnapshot s, int ownIndex, float currentFacing, out float bearing, out float windowShare)
        {
            bearing = -1f;
            windowShare = 0f;
            if (s == null || ownIndex < 0 || ownIndex >= s.OwnCount) return false;
            int slices = s.SliceCount;
            int bins = BinCount(slices);
            if (bins < WindowBins) return false;

            float best = 0f;
            int bestStart = -1;
            for (int start = 0; start < bins; start++)
            {
                float sum = Window(s, ownIndex, start, bins);
                if (sum > best)
                {
                    best = sum;
                    bestStart = start;
                }
            }
            if (bestStart < 0) return false;

            float current = 0f;
            if (IsFinite(currentFacing)) current = Window(s, ownIndex, BinOf(currentFacing, slices, bins) - 1, bins);
            windowShare = Math.Min(1f, current / best);
            if (current >= KeepShare * best)
            {
                bearing = PostureSense.NormalizeHeading(currentFacing);
                return true;
            }

            bearing = WindowMean(s, ownIndex, bestStart, slices, bins);
            return true;
        }

        /// <summary>
        /// The flank with more perceived pressure (BattleSnapshot.FlankPressure left / right); None when
        /// both are 0 or they tie (the bearing test of the closest enemy decides then).
        /// </summary>
        public static RefuseFlank PressuredSide(BattleSnapshot s, int ownIndex)
        {
            if (s == null || ownIndex < 0 || ownIndex >= s.OwnCount) return RefuseFlank.None;
            float left = s.FlankPressure(ownIndex, 0);
            float right = s.FlankPressure(ownIndex, 1);
            if (left <= 0f && right <= 0f) return RefuseFlank.None;
            if (left > right) return RefuseFlank.Left;
            return right > left ? RefuseFlank.Right : RefuseFlank.None;
        }

        /// <summary>
        /// The pressure bearing on one side of the unit's facing: the best 3-bin window whose centre bin
        /// lies left (-180..0) or right (0..180) of <paramref name="facing"/>. The 45-150 degree
        /// geometry test of the refused flank runs on it (RefuseFor). False with no pressure on that side.
        /// </summary>
        public static bool SideBearing(BattleSnapshot s, int ownIndex, float facing, RefuseFlank side, out float bearing)
        {
            bearing = -1f;
            if (s == null || ownIndex < 0 || ownIndex >= s.OwnCount || side == RefuseFlank.None || !IsFinite(facing)) return false;
            int slices = s.SliceCount;
            int bins = BinCount(slices);
            if (bins < WindowBins) return false;

            float best = 0f;
            int bestStart = -1;
            for (int start = 0; start < bins; start++)
            {
                int centre = (start + 1) % bins;
                float delta = SignedDelta(CentreOf(centre, slices), facing);
                if (side == RefuseFlank.Left ? delta >= 0f : delta <= 0f) continue;
                float sum = Window(s, ownIndex, start, bins);
                if (sum > best)
                {
                    best = sum;
                    bestStart = start;
                }
            }
            if (bestStart < 0) return false;
            bearing = WindowMean(s, ownIndex, bestStart, slices, bins);
            return true;
        }

        // ---- refused flank in combat (decision 2) -----------------------------------------

        /// <summary>
        /// SkewedPosition's setback of a regiment at <paramref name="offsetX"/> within a group of
        /// <paramref name="groupWidth"/> (78109-78130): the shipped LeafSlotTarget.RefusedSetback.
        /// </summary>
        public static float Setback(float offsetX, float groupWidth, float depthFactor, float skew, RefuseFlank side)
        {
            return LeafSlotTarget.RefusedSetback(offsetX, groupWidth, depthFactor, skew, side);
        }

        /// <summary>
        /// The rotation SkewedPosition gives a regiment (78131-78167): the bearing of the chord from the
        /// left edge of the unit (offset - width / 2) to its right edge, each pushed back by its own
        /// setback, as GetAngleTerrain (atan2(dx, dz) - 180) + 90 on positions laid out from
        /// <paramref name="groupRotation"/> + 180. Degrees in [0, 360).
        /// </summary>
        public static float SkewedRotation(float groupRotation, float offsetX, float unitWidth, float groupWidth, float refuseDepth,
            RefuseFlank side, float skew)
        {
            if (!(groupWidth > 0f) || !(unitWidth > 0f)) return PostureSense.NormalizeHeading(groupRotation);
            float tLeft = (offsetX - unitWidth / 2f + groupWidth / 2f) / groupWidth;
            float tRight = (offsetX + unitWidth / 2f + groupWidth / 2f) / groupWidth;
            if (side == RefuseFlank.Left)
            {
                tLeft = 1f - tLeft;
                tRight = 1f - tRight;
            }
            float depthLeft = SkewDepth(tLeft, skew, refuseDepth);
            float depthRight = SkewDepth(tRight, skew, refuseDepth);
            double angle = groupRotation + 180.0;
            Lay(angle, -unitWidth / 2.0, -depthLeft, out double lx, out double lz);
            Lay(angle, unitWidth / 2.0, -depthRight, out double rx, out double rz);
            double degrees = Math.Atan2(rx - lx, rz - lz) * (180.0 / Math.PI) - 90.0;
            return PostureSense.NormalizeHeading((float)degrees);
        }

        /// <summary>
        /// The regiments an in-combat refuse rotates in place: the group's rotatable regiments
        /// (PostureSense.CollectRotatableRegiments) whose setback is at least <see cref="RefuseWingShare"/>
        /// of the refuse depth and whose current facing differs from the skewed rotation by more than the
        /// executor's tolerance, minus those vanilla's micro turn would undo (<paramref name="skipped"/>),
        /// and those <paramref name="memory"/> holds backed off after vanilla turned them back, at most <see cref="MaxRefuseRotationsPerTick"/> with the deepest setbacks first. Writes own
        /// indices to <paramref name="regiments"/> and their rotations to <paramref name="headings"/>
        /// (parallel); <paramref name="queue"/> is the walk's scratch. Returns the count. Nothing here
        /// writes: OrderExecutorRuntime rotates each through TacticalWriteGuard.CanWrite.
        /// </summary>
        public static int RefuseRotations(BattleSnapshot s, int groupIndex, RefuseFlank side, float groupWidth, float depthFactor, float skew,
            in MicroTurnRule turn, int[] queue, int[] regiments, float[] headings, out int skipped,
            RefuseTurnBackMemory memory = null, double nowHours = 0.0)
        {
            skipped = 0;
            if (s == null || side == RefuseFlank.None || queue == null || regiments == null || headings == null) return 0;
            if (groupIndex < 0 || groupIndex >= s.OwnCount || !(groupWidth > 0f) || !(depthFactor > 0f)) return 0;
            float refuseDepth = groupWidth * depthFactor;
            float groupRotation = s.Own[groupIndex].RotationY;
            int candidates = PostureSense.CollectRotatableRegiments(s, groupIndex, queue, regiments);
            if (headings.Length < candidates) candidates = headings.Length;

            int written = 0;
            for (int c = 0; c < candidates; c++)
            {
                int index = regiments[c];
                ref UnitRecord rec = ref s.Own[index];
                float offsetX = LayoutOffset(s, groupIndex, index);
                float setback = Setback(offsetX, groupWidth, depthFactor, skew, side);
                if (!(setback >= RefuseWingShare * refuseDepth)) continue;
                float rotation = SkewedRotation(groupRotation, offsetX, UnitWidthOf(s, groupIndex, index, groupWidth), groupWidth, refuseDepth, side, skew);
                if (AngleDifference(rec.RotationY, rotation) <= OrderExecutor.FacingToleranceDegrees) continue;
                if (VanillaTurnsBack(s, index, rotation, in turn))
                {
                    skipped++;
                    continue;
                }
                // Vanilla's micro turn sees every enemy, the snapshot only the perceived ones: a regiment that was turned back
                // since we rotated it is left alone for a back-off (see RefuseTurnBackMemory).
                if (memory != null && memory.IsBackedOff(rec.InstanceId, rec.RotationY, rotation, nowHours))
                {
                    skipped++;
                    continue;
                }
                regiments[written] = index;
                headings[written] = rotation;
                written++;
            }

            // The deepest setbacks first, at most MaxRefuseRotationsPerTick.
            int take = Math.Min(written, MaxRefuseRotationsPerTick);
            for (int pass = 0; pass < take; pass++)
            {
                int pick = pass;
                float pickSetback = Setback(LayoutOffset(s, groupIndex, regiments[pass]), groupWidth, depthFactor, skew, side);
                for (int k = pass + 1; k < written; k++)
                {
                    float candidate = Setback(LayoutOffset(s, groupIndex, regiments[k]), groupWidth, depthFactor, skew, side);
                    if (candidate > pickSetback)
                    {
                        pick = k;
                        pickSetback = candidate;
                    }
                }
                if (pick == pass) continue;
                (regiments[pass], regiments[pick]) = (regiments[pick], regiments[pass]);
                (headings[pass], headings[pick]) = (headings[pick], headings[pass]);
            }
            return take;
        }

        /// <summary>
        /// Would vanilla's micro turn rotate this regiment back from <paramref name="rotation"/>, judged on
        /// the snapshot (4560-4608)? Its closest VISIBLE enemy within the turn reach (distancetoturntowardsenemy,
        /// or the regiment's fire range once it has received fire) at an angle of at least the trigger from the
        /// rotation (plus the cover extra) and within 1.5 x fire range.
        /// </summary>
        public static bool VanillaTurnsBack(BattleSnapshot s, int regimentIndex, float rotation, in MicroTurnRule turn)
        {
            if (s == null || regimentIndex < 0 || regimentIndex >= s.OwnCount) return false;
            ref UnitRecord rec = ref s.Own[regimentIndex];
            int k = s.ClosestVisibleEnemy(regimentIndex, out float distance);
            if (k < 0) return false;
            float reach = rec.ReceivedFire ? rec.FireRange : turn.DistanceMeters;
            if (distance > reach) return false;
            float trigger = turn.AngleDegrees;
            if (rec.CoverValue > turn.MaxCoverValue && rec.CoverObject != 3) trigger += turn.CoverExtraDegrees;
            float bearing = PostureSense.HeadingTo(rec.X, rec.Z, s.Observations[k].X, s.Observations[k].Z);
            return AngleDifference(rotation, bearing) >= trigger && distance < rec.FireRange * turn.FireRangeFactor;
        }

        // The regiment's x within the group's layout (UnitRecord.SlotOffsetX with HasLayoutSlot for a direct
        // child, positive to the right of the group's facing), else its lateral projection on the group axis.
        private static float LayoutOffset(BattleSnapshot s, int groupIndex, int index)
        {
            ref UnitRecord rec = ref s.Own[index];
            if (rec.ParentIndex == groupIndex && (rec.Flags & UnitFlags.HasLayoutSlot) != 0) return rec.SlotOffsetX;
            ref UnitRecord group = ref s.Own[groupIndex];
            double r = group.RotationY * Math.PI / 180.0;
            return (float)((rec.X - group.X) * Math.Cos(r) - (rec.Z - group.Z) * Math.Sin(r));
        }

        // A regiment's own width, else its group slot width, else an even share of the group's frontage.
        private static float UnitWidthOf(BattleSnapshot s, int groupIndex, int index, float groupWidth)
        {
            ref UnitRecord rec = ref s.Own[index];
            if (rec.UnitWidth > 0f) return rec.UnitWidth;
            if (rec.GroupWidth > 0f) return rec.GroupWidth;
            int siblings = 0;
            for (int j = 0; j < s.OwnCount; j++)
                if (s.Own[j].ParentIndex == groupIndex) siblings++;
            return groupWidth / Math.Max(1, siblings);
        }

        // GetAngleTerrainPosXYStatic without the terrain clamp (the shape LeafSlotTarget.OffsetXZ uses):
        // distZ is rearward, distX to the right of a unit facing angle (0 = +z, 90 = +x).
        private static void Lay(double angle, double distX, double distZ, out double x, out double z)
        {
            double a = (angle - 90.0) * Math.PI / 180.0;
            double b = angle * Math.PI / 180.0;
            x = -Math.Cos(a) * distZ - Math.Cos(b) * distX;
            z = Math.Sin(a) * distZ + Math.Sin(b) * distX;
        }

        private static float SkewDepth(float t, float skew, float refuseDepth)
        {
            double depth = Math.Pow(t, skew) * refuseDepth;
            return double.IsNaN(depth) || double.IsInfinity(depth) ? 0f : (float)depth;
        }

        // ---- slice geometry -----------------------------------------------------------------

        // Bins vanilla fills: floor(bearing / slices) of a bearing below 360 (122732).
        private static int BinCount(int slices)
        {
            if (slices <= 0) return 0;
            return Math.Min(slices, (int)Math.Ceiling(360.0 / slices));
        }

        private static float CentreOf(int bin, int slices)
        {
            return bin * (float)slices + slices / 2f;
        }

        private static int BinOf(float bearing, int slices, int bins)
        {
            int bin = (int)Math.Floor(PostureSense.NormalizeHeading(bearing) / slices);
            return bin < 0 ? 0 : bin >= bins ? bins - 1 : bin;
        }

        private static float Window(BattleSnapshot s, int ownIndex, int start, int bins)
        {
            float sum = 0f;
            for (int k = 0; k < WindowBins; k++)
            {
                int bin = (((start + k) % bins) + bins) % bins;
                sum += s.SlicePressure(ownIndex, bin);
            }
            return sum;
        }

        private static float WindowMean(BattleSnapshot s, int ownIndex, int start, int slices, int bins)
        {
            double sin = 0.0, cos = 0.0;
            for (int k = 0; k < WindowBins; k++)
            {
                int bin = (start + k) % bins;
                double weight = s.SlicePressure(ownIndex, bin);
                double centre = CentreOf(bin, slices) * Math.PI / 180.0;
                sin += weight * Math.Sin(centre);
                cos += weight * Math.Cos(centre);
            }
            return PostureSense.NormalizeHeading((float)(Math.Atan2(sin, cos) * (180.0 / Math.PI)));
        }

        // Signed angle from facing to bearing in (-180, 180]: negative is to the left.
        private static float SignedDelta(float bearing, float facing)
        {
            float d = (bearing - facing) % 360f;
            if (d > 180f) d -= 360f;
            if (d <= -180f) d += 360f;
            return d;
        }

        internal static float AngleBetween(float a, float b) => AngleDifference(a, b);

        private static float AngleDifference(float a, float b)
        {
            float d = Math.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        private static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }
    }

    /// <summary>
    /// The flank-threat tally the flank tally patch (FlankThreatTallyFixPatch) writes into
    /// Regiment.flanksthreated (decision 6): the sum, over the subordinates vanilla's loop counts, of the
    /// weight of each subordinate's OWN outflanked code (vanilla adds the group's own code once per
    /// subordinate, 120566-120580, so the HQ standing behind its line usually gives 0).
    /// </summary>
    public static class FlankThreatTally
    {
        /// <summary>The weight points of an outflanked code: 1 and 2 give 1, 3 gives 2, 4-6 give 3, 7 gives 4.</summary>
        public static int Weight(int outflanked)
        {
            switch (outflanked)
            {
                case 1:
                case 2:
                    return 1;
                case 3:
                    return 2;
                case 4:
                case 5:
                case 6:
                    return 3;
                case 7:
                    return 4;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// UpdateGroupStrength returns before the loop when a battle UI exists, its selected unit is another
        /// unit and the call is not an override (120140-120143). True when vanilla reached the loop.
        /// </summary>
        public static bool VanillaReachedLoop(bool battleUiMissing, bool selectedUnitIsThisGroup, bool overrideCalc)
        {
            return battleUiMissing || selectedUnitIsThisGroup || overrideCalc;
        }

        /// <summary>
        /// The loop's filter (120284): a subordinate (unittyp 13 or below) counts when it is active in the
        /// hierarchy, the call ignores the active state, the call's single exception is this group and that
        /// group is inactive, or the subordinate is surrendering.
        /// </summary>
        public static bool Counts(int unitTyp, bool activeInHierarchy, bool ignoreActiveState, bool exceptionIsThisGroupAndInactive, bool surrendering)
        {
            return unitTyp <= 13 && (activeInHierarchy || ignoreActiveState || exceptionIsThisGroupAndInactive || surrendering);
        }
    }

    /// <summary>
    /// BC11 decision 6 measurement: a vanilla FlankMove sets the group's lastaiorder to 4 + direction (117107), and the AI
    /// returns it to 0 when the group is idle again. This watch reports, per snapshot, the groups whose value went from 0
    /// to 4 or 5 since the previous snapshot (a group first seen is only recorded), so the flank-move rate with the
    /// flank tally patch on and off can be compared (smoke item 2.33). Pooled, grow-only.
    /// </summary>
    public sealed class VanillaFlankMoveWatch
    {
        private readonly Dictionary<int, int> _last = new Dictionary<int, int>(64);

        /// <summary>
        /// Notes every group's LastAiOrder; writes the instance ids (and directions, lastaiorder - 4) of the groups that
        /// started a flank move since the last call. Returns how many (up to the buffer length).
        /// </summary>
        public int Observe(BattleSnapshot s, int[] groupIds, int[] directions)
        {
            if (s == null || groupIds == null || directions == null) return 0;
            int found = 0;
            int capacity = Math.Min(groupIds.Length, directions.Length);
            UnitRecord[] own = s.Own;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if ((own[i].Flags & UnitFlags.IsGroup) == 0) continue;
                int id = own[i].InstanceId;
                int now = own[i].LastAiOrder;
                if (_last.TryGetValue(id, out int before) && before == 0 && (now == 4 || now == 5) && found < capacity)
                {
                    groupIds[found] = id;
                    directions[found] = now - 4;
                    found++;
                }
                _last[id] = now;
            }
            return found;
        }

        public void Clear()
        {
            _last.Clear();
        }
    }
}
