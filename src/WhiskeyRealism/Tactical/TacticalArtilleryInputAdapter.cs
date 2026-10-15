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

        // Remaining artillery ammunition, 0..1. Regiment.ammo (111498) holds one
        // 0..1 fill level per slot (solid 0, explosive 1, canister 2); vanilla
        // reads a battery's supply as the mean of its slots
        // (GetROF: Tools.SumUp(ammo) / 3, 21038). Non-finite slots are skipped;
        // an unreadable array counts as full so missing data never makes a
        // battery cease fire.
        public static float ArtilleryAmmoRatio01(float[] ammo)
        {
            if (ammo == null) return 1f;
            float total = 0f;
            int count = 0;
            for (int i = 0; i < ammo.Length; i++)
            {
                float slot = ammo[i];
                if (float.IsNaN(slot) || float.IsInfinity(slot)) continue;
                total += slot < 0f ? 0f : slot;
                count++;
            }
            if (count == 0) return 1f;
            float ratio = total / count;
            return ratio > 1f ? 1f : ratio;
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
