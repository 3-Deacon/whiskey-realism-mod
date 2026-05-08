namespace WhiskeyRealism.Tactical
{
    public static class TacticalArtilleryDoctrine
    {
        public enum Decision
        {
            PreserveFire,
            SuppressStrongpoint,
            CounterBattery,
            CancelBombard,
            DefensiveFallback,
        }

        public struct Input
        {
            public TacticalSupportScreen.Result ScreenResult;
            public float AmmoTotalRatio;
            public float CanisterAmmo;
            public float ClosestEnemyDistance;
            public float UnitFireRange;
            public bool EnemyArtilleryVisible;
            public int CombatBehaviorOrdered;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
        }

        public static Decision Score(in Input input)
        {
            // W&L gate: safe default is PreserveFire (no write).
            if (!TacticalGateHelpers.PassesWlOwnership(input.AiFeudStance, input.IsPlayerAiOrFeud))
                return Decision.PreserveFire;

            // Low ammo cancels regardless of screen.
            if (input.AmmoTotalRatio < 0.10f)
                return Decision.CancelBombard;

            // Unsupported with close enemy -> cancel.
            bool enemyClose = input.ClosestEnemyDistance <= input.UnitFireRange * 0.20f;
            if (input.ScreenResult == TacticalSupportScreen.Result.Unsupported && enemyClose)
                return Decision.CancelBombard;

            // Shaken close-enemy -> defensive fallback telemetry (vanilla writes movement).
            if (input.ScreenResult == TacticalSupportScreen.Result.Shaken && enemyClose)
                return Decision.DefensiveFallback;

            // Counterbattery if enemy artillery is visible and we are in fire range.
            if (input.EnemyArtilleryVisible && input.ClosestEnemyDistance <= input.UnitFireRange)
                return Decision.CounterBattery;

            // Default: preserve fire.
            return Decision.PreserveFire;
        }
    }
}
