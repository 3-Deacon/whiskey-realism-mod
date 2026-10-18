using System;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>What a brigade's placed regiments say about the way round its layout was laid.</summary>
    public enum MirrorVerdict
    {
        /// <summary>No row held two units far enough apart to order them (a lone regiment, a column of single-unit rows, a vote tie): no evidence, not counted.</summary>
        Uninformative = 0,
        /// <summary>Every informative row stands left to right in column order: the placement is vanilla's level-1/2 one (<c>-offset.x</c>).</summary>
        Agree = 1,
        /// <summary>Every informative row stands right to left: the placement is mirrored (the level-3 one, <c>+offset.x</c>).</summary>
        Reversed = 2,
        /// <summary>Rows that disagree with each other. Counted in the brigades, in neither share.</summary>
        Mixed = 3,
    }

    /// <summary>
    /// BC16 decision 9, step 0 (record only): the pure half of the corps-level mirror check. BattleUnits.SetGroupFormation places a regiment of
    /// the ordered group's third level at <c>+offset.x</c> and those of the first two at <c>-offset.x</c> (decompile 92086, 92126, 92176). The
    /// layout's own <c>column</c> grows with <c>offset.x</c> (BuildGroupOffsetPositions 92496-92519), so an order given three levels above the
    /// regiments (a corps) should lay each brigade's regiments right to left against their columns where a brigade or division order lays them
    /// left to right. This classifies what a call actually placed; the rule that turns the counts into a decision is in the plan, § BC16 decision 9.
    /// </summary>
    public static class LayoutMirror
    {
        /// <summary>A pair of regiments whose placed lateral positions differ by less than this orders nothing (terrain bend and path-end snapping move a placed unit a metre or two).</summary>
        public const float MinSeparationMeters = 1f;

        /// <summary>The deepest layout row examined. A brigade lays two or three rows; the bound only keeps a corrupt row index from looping.</summary>
        public const int MaxRows = 64;

        /// <summary>
        /// A position's coordinate along the right of an order facing <paramref name="facingDegrees"/> (Unity yaw, as <c>manualfinalrotation</c>).
        /// BattlefieldSetup.GetAngleTerrainPosXYStatic (26937-26980) moves a point by <c>distancex</c> along (-cos, +sin) of the yaw, the left,
        /// so the right is (cos, -sin); a unit placed at <c>distancex = -offset.x</c> therefore stands <c>offset.x</c> to the right.
        /// </summary>
        public static float Lateral(float x, float z, float facingDegrees)
        {
            float yaw = facingDegrees * (MathF.PI / 180f);
            return x * MathF.Cos(yaw) - z * MathF.Sin(yaw);
        }

        /// <summary>
        /// Compares, row by row, the order of a brigade's placed regiments with the order of their layout columns. Arrays are parallel and may be
        /// in any order. A row's verdict is the majority of its pairs (a pair under <see cref="MinSeparationMeters"/> or in one column does not
        /// vote); rows are never compared with each other, since the second line of a double line stands behind the first. Allocates nothing.
        /// </summary>
        public static MirrorVerdict Classify(ReadOnlySpan<int> row, ReadOnlySpan<int> column, ReadOnlySpan<float> lateral, int count)
        {
            int n = Math.Min(count, Math.Min(row.Length, Math.Min(column.Length, lateral.Length)));
            if (n < 2) return MirrorVerdict.Uninformative;

            int deepest = 0;
            for (int i = 0; i < n; i++)
                if (row[i] > deepest && row[i] < MaxRows) deepest = row[i];

            bool agrees = false, reverses = false;
            for (int r = 0; r <= deepest; r++)
            {
                int agree = 0, reversed = 0;
                for (int i = 0; i < n; i++)
                {
                    if (row[i] != r) continue;
                    for (int j = i + 1; j < n; j++)
                    {
                        if (row[j] != r) continue;
                        int columns = column[j] - column[i];
                        float apart = lateral[j] - lateral[i];
                        if (columns == 0 || MathF.Abs(apart) < MinSeparationMeters) continue;
                        if ((columns > 0) == (apart > 0f)) agree++;
                        else reversed++;
                    }
                }
                if (agree > reversed) agrees = true;
                else if (reversed > agree) reverses = true;
            }

            if (agrees && reverses) return MirrorVerdict.Mixed;
            if (agrees) return MirrorVerdict.Agree;
            return reverses ? MirrorVerdict.Reversed : MirrorVerdict.Uninformative;
        }
    }

    /// <summary>
    /// The brigades one group order laid out, tallied by depth: the ordered group's distance above its regiments (1 a brigade order, 2 a division
    /// order, 3 a corps order). One <c>TacticalLayoutMirror</c> row is written per depth the call touched.
    /// </summary>
    public sealed class LayoutMirrorCall
    {
        /// <summary>The deepest group order vanilla lays out (SetGroupFormation has three loops below the ordered group, 92007-92027).</summary>
        public const int MaxDepth = 3;

        private readonly int[] _brigades = new int[MaxDepth + 1];
        private readonly int[] _agree = new int[MaxDepth + 1];
        private readonly int[] _reversed = new int[MaxDepth + 1];

        public void Clear()
        {
            Array.Clear(_brigades, 0, _brigades.Length);
            Array.Clear(_agree, 0, _agree.Length);
            Array.Clear(_reversed, 0, _reversed.Length);
        }

        /// <summary>Counts one brigade. An uninformative brigade and a depth outside 1-3 are not counted.</summary>
        public void Add(int depth, MirrorVerdict verdict)
        {
            if (depth < 1 || depth > MaxDepth || verdict == MirrorVerdict.Uninformative) return;
            _brigades[depth]++;
            if (verdict == MirrorVerdict.Agree) _agree[depth]++;
            else if (verdict == MirrorVerdict.Reversed) _reversed[depth]++;
        }

        public int Brigades(int depth) => depth >= 1 && depth <= MaxDepth ? _brigades[depth] : 0;

        public int Agree(int depth) => depth >= 1 && depth <= MaxDepth ? _agree[depth] : 0;

        public int Reversed(int depth) => depth >= 1 && depth <= MaxDepth ? _reversed[depth] : 0;
    }

    /// <summary>
    /// How many <c>TacticalLayoutMirror</c> rows a battle may write, per depth. The rule needs at least 10 rows at every depth, and corps orders
    /// are rare next to brigade orders, so one shared cap would fill with depth 1 before depth 3 had been seen.
    /// </summary>
    public sealed class LayoutMirrorBudget
    {
        public const int MaxRowsPerDepth = 128;

        private readonly int[] _taken = new int[LayoutMirrorCall.MaxDepth + 1];

        public bool TryTake(int depth)
        {
            if (depth < 1 || depth > LayoutMirrorCall.MaxDepth || _taken[depth] >= MaxRowsPerDepth) return false;
            _taken[depth]++;
            return true;
        }

        public int Taken(int depth) => depth >= 1 && depth <= LayoutMirrorCall.MaxDepth ? _taken[depth] : 0;

        public void Reset() => Array.Clear(_taken, 0, _taken.Length);
    }
}
