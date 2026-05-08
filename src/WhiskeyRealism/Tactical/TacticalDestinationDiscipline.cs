using System;

namespace WhiskeyRealism.Tactical
{
    public static class TacticalDestinationDiscipline
    {
        public enum Result
        {
            ClearDestination,
            CrowdedSameType,
            CrowdedAdjacent,
            EnemyOnDestination,
            PathRiskUnknown,
        }

        public struct Input
        {
            public int MoverUnitTyp;
            public int PeerUnitTyp;
            public bool PeerHasActivePath;
            public float NearestSameTypePeerDistance;
            public float NearestOtherCombatPeerDistance;
            public float EnemyOnDestinationDistance;
            public float MoverFireRange;
            public float MoverWidth;
            public float VanillaInterruptThreshold;
        }

        public static Result Score(in Input input)
        {
            if (input.MoverFireRange <= 0f) return Result.PathRiskUnknown;

            if (input.EnemyOnDestinationDistance <= input.MoverFireRange)
                return Result.EnemyOnDestination;

            bool peerExempt = input.PeerUnitTyp == 3 && input.PeerHasActivePath;

            float sameTypeTier;
            if (input.MoverUnitTyp == 2)
            {
                sameTypeTier = 5f;
            }
            else
            {
                float scaled = Math.Max(input.MoverWidth, input.MoverFireRange * 0.5f);
                float clampMin = input.VanillaInterruptThreshold;
                float clampMax = 2f * input.MoverFireRange;
                sameTypeTier = Math.Max(clampMin, Math.Min(clampMax, scaled));
            }

            if (!peerExempt && input.NearestSameTypePeerDistance < sameTypeTier)
                return Result.CrowdedSameType;

            float adjacentTier = (input.MoverUnitTyp == 2) ? 5f : sameTypeTier;
            if (!peerExempt && input.NearestOtherCombatPeerDistance < adjacentTier)
                return Result.CrowdedAdjacent;

            return Result.ClearDestination;
        }
    }
}
