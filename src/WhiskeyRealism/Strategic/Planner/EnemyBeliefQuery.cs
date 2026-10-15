using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The one way pure planner code sums or locates believed enemy armies (G1 migrated ledgers,
    // G3 balance, G1b recon, G5b capital need, G8 posts). Beliefs only, never vanilla's
    // omniscient enemy lists. A belief with Confidence01 <= 0 is expired: it counts nowhere.
    public static class EnemyBeliefQuery
    {
        // Sum of Strength x Confidence01 over the live beliefs whose last-seen position lies
        // within radius of (x, z).
        public static float StrengthWithin(IReadOnlyList<EnemyBelief> b, float x, float z, float radius)
        {
            if (b == null || radius < 0f) return 0f;
            float radiusSq = radius * radius;
            float total = 0f;
            for (int i = 0; i < b.Count; i++)
            {
                var belief = b[i];
                if (!IsLive(belief)) continue;
                float dx = belief.X - x;
                float dz = belief.Z - z;
                if (dx * dx + dz * dz > radiusSq) continue;
                total += Math.Max(0f, belief.Strength) * Math.Min(1f, belief.Confidence01);
            }
            return total;
        }

        // Index of the nearest live belief, -1 (distance float.MaxValue) when there is none.
        public static int Nearest(IReadOnlyList<EnemyBelief> b, float x, float z, out float distance)
        {
            distance = float.MaxValue;
            if (b == null) return -1;
            int best = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < b.Count; i++)
            {
                var belief = b[i];
                if (!IsLive(belief)) continue;
                float dx = belief.X - x;
                float dz = belief.Z - z;
                float dSq = dx * dx + dz * dz;
                if (dSq >= bestSq) continue;
                bestSq = dSq;
                best = i;
            }
            if (best >= 0) distance = (float)Math.Sqrt(bestSq);
            return best;
        }

        private static bool IsLive(in EnemyBelief belief)
        {
            return belief.Confidence01 > 0f;
        }
    }
}
