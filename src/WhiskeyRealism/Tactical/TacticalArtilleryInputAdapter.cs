namespace WhiskeyRealism.Tactical
{
    public static class TacticalArtilleryInputAdapter
    {
        public struct Snapshot
        {
            public int UnitTyp;
            public int Guns;
            public bool IsRouted;
            public bool MarkedForRout;
            public float AmmoTotalRatio;
            public float CanisterAmmo;
            public float Morale;
            public float BattleStartMorale;
            public bool BattleStartMoraleInitialized;
            public float DangerRadius;
            public float ClosestEnemyDistance;
            public int InfCavScreenCount;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
            public float FallbackThreshold;
            public int CombatBehaviorOrdered;
            public float VolleyDwellRemaining;
        }

        public static bool IsEligible(in Snapshot snapshot)
        {
            if (snapshot.UnitTyp != TacticalUnitType.Artillery) return false;
            if (snapshot.Guns <= 0) return false;
            if (snapshot.IsRouted) return false;
            if (snapshot.MarkedForRout) return false;
            return true;
        }

        public static TacticalSupportScreen.Input ToSupportScreenInput(in Snapshot snapshot)
        {
            return new TacticalSupportScreen.Input
            {
                ProtectedUnitMorale = snapshot.Morale,
                MoraleFallbackThreshold = snapshot.FallbackThreshold,
                BattleStartMorale = snapshot.BattleStartMorale,
                EnemyDistance = snapshot.ClosestEnemyDistance,
                DangerRadius = snapshot.DangerRadius,
                ScreenUnitCount = snapshot.InfCavScreenCount,
                AiFeudStance = snapshot.AiFeudStance,
                IsPlayerAiOrFeud = snapshot.IsPlayerAiOrFeud,
            };
        }
    }
}
