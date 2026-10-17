using System;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// BC14O objective doctrine (spec § 7B), the pure rules. An objective's value comes from vanilla's own points, normalised across the
    /// live objectives; everything here is a function of those numbers and of nothing else (no Unity or vanilla types).
    /// </summary>
    public static class ObjectiveScoring
    {
        /// <summary>Value = ValueFloor + ValueSpan x v, so the best objective keeps the 0.80 the director has always given an enemy-held one.</summary>
        public const float ValueFloor = 0.2f, ValueSpan = 0.6f;

        /// <summary>An own-held objective is worth this much of the same value: today's 0.45 / 0.80.</summary>
        public const float OwnHeldFactor = 0.5625f;

        /// <summary>
        /// <c>v</c>: an objective's points over the largest of the live objectives' points. With every live objective at 0 points
        /// (<paramref name="maxPoints"/> not above 0) nothing tells them apart and every <c>v</c> is 0.5.
        /// </summary>
        public static float Normalised(float points, float maxPoints)
        {
            if (!(maxPoints > 0f)) return 0.5f;
            float v = points / maxPoints;
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>The objective's value for the side: <see cref="ValueFloor"/> + <see cref="ValueSpan"/> x v, times <see cref="OwnHeldFactor"/> for one it holds.</summary>
        public static float Value(float v, bool ownHeld)
        {
            float value = ValueFloor + ValueSpan * v;
            return ownHeld ? value * OwnHeldFactor : value;
        }
    }
}
