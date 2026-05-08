namespace WhiskeyRealism.Tactical
{
    public static class TacticalWithdrawalInputAdapter
    {
        public struct Snapshot
        {
            public float Morale;
            public float BattleStartMorale;
            public bool BattleStartMoraleInitialized;
            public float FallbackThreshold;
            public int Outflanked;
            public float FriendlyRoutedNear;
            public float EnemyRoutedNear;
            public bool ReceivedFireFromClosestFar;
            public float CoverValue;
            public int CoverObject;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
            public float Fatigue;
            public float[] EnemyStrengthWithinAngle;
            public float SliceWidthDegrees;
            public float UnitFacingDegrees;
        }

        public static TacticalMoralePressure.Input ToMoralePressureInput(in Snapshot snapshot)
        {
            return new TacticalMoralePressure.Input
            {
                CurrentMorale = snapshot.Morale,
                BattleStartMorale = snapshot.BattleStartMorale,
                BattleStartMoraleInitialized = snapshot.BattleStartMoraleInitialized,
                FallbackThreshold = snapshot.FallbackThreshold,
                Outflanked = snapshot.Outflanked,
                FriendlyRoutedNear = snapshot.FriendlyRoutedNear,
                EnemyRoutedNear = snapshot.EnemyRoutedNear,
                ReceivedFireFromClosestFar = snapshot.ReceivedFireFromClosestFar,
                CoverValue = snapshot.CoverValue,
                CoverObject = snapshot.CoverObject,
                AiFeudStance = snapshot.AiFeudStance,
                IsPlayerAiOrFeud = snapshot.IsPlayerAiOrFeud,
            };
        }

        public static TacticalQuadrantThreatScorer.Input ToQuadrantInput(in Snapshot snapshot)
        {
            return new TacticalQuadrantThreatScorer.Input
            {
                Slices = snapshot.EnemyStrengthWithinAngle,
                SliceWidthDegrees = snapshot.SliceWidthDegrees,
                UnitFacingDegrees = snapshot.UnitFacingDegrees,
            };
        }
    }
}
