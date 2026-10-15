namespace WhiskeyRealism.Tactical
{
    public static class TacticalWithdrawalDoctrine
    {
        public enum Decision { HoldLine, Stabilize, Screen, RearGuard, FullRetreat }

        public struct Input
        {
            public TacticalMoralePressure.Result MoralePressure;
            public bool RearPressureFlag;
            public TacticalFatigueState.Result Fatigue;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
        }

        public static Decision Score(in Input input)
        {
            // W&L gate: safe default is HoldLine (no write).
            if (!TacticalGateHelpers.PassesWlOwnership(input.AiFeudStance, input.IsPlayerAiOrFeud))
                return Decision.HoldLine;

            // Base ladder from morale pressure.
            Decision baseDecision;
            switch (input.MoralePressure)
            {
                case TacticalMoralePressure.Result.Stable:
                    baseDecision = Decision.HoldLine;
                    break;
                case TacticalMoralePressure.Result.UnderPressure:
                    baseDecision = Decision.Stabilize;
                    break;
                case TacticalMoralePressure.Result.FallbackCandidate:
                    baseDecision = Decision.Screen;
                    break;
                case TacticalMoralePressure.Result.WithdrawalCandidate:
                    baseDecision = Decision.RearGuard;
                    break;
                case TacticalMoralePressure.Result.CollapseCandidate:
                    baseDecision = Decision.FullRetreat;
                    break;
                default:
                    baseDecision = Decision.HoldLine;
                    break;
            }

            // Rear pressure + tired/spent/exhausted bumps the ladder one step.
            bool tiredOrWorse = input.Fatigue == TacticalFatigueState.Result.Spent
                || input.Fatigue == TacticalFatigueState.Result.Exhausted;
            if (input.RearPressureFlag && tiredOrWorse)
            {
                baseDecision = BumpUpOne(baseDecision);
            }

            return baseDecision;
        }

        public enum FallbackKind { None, Fallback, Retreat }

        public readonly struct FallbackUnitState
        {
            public FallbackUnitState(
                int unitTyp,
                bool routed,
                bool markedForRout,
                bool permanentlyDetached,
                bool onRetreat,
                int regimentPaths,
                int movementMode,
                bool hasChargeTarget,
                float inMelee)
            {
                UnitTyp = unitTyp;
                Routed = routed;
                MarkedForRout = markedForRout;
                PermanentlyDetached = permanentlyDetached;
                OnRetreat = onRetreat;
                RegimentPaths = regimentPaths;
                MovementMode = movementMode;
                HasChargeTarget = hasChargeTarget;
                InMelee = inMelee;
            }

            public int UnitTyp { get; }
            public bool Routed { get; }
            public bool MarkedForRout { get; }
            public bool PermanentlyDetached { get; }
            public bool OnRetreat { get; }
            public int RegimentPaths { get; }
            public int MovementMode { get; }
            public bool HasChargeTarget { get; }
            public float InMelee { get; }
        }

        // B8 write shape. Never the campaign Regiment.SetWithdrawal (116116):
        // it leaves onretreat = true, and BattleUnits.SetWaypoint returns early
        // for onretreat units (91361), so the unit could never be ordered again.
        // Instead mirror vanilla's own tactical fallbacks:
        //   RearGuard   -> AIBattle.CheckLineFallbacks 5118 (fallback, mode 2,
        //                  GamePrefs.standardfallbackdistance);
        //   FullRetreat -> AIBattle.MicroAICheckForRetreats 4825-4898 (path at
        //                  GamePrefs.standardretreatdistance keeping the current
        //                  facing, roads off, then mode 2; SetMovementMode maps 2
        //                  to 4 for mounted units).
        // Eligibility is CheckLineFallbacks' unit filter (5132): infantry or
        // cavalry, movementmode 0, not routed/marked, no charge target, no
        // paths, not in melee; plus not detached and not already onretreat.
        public static FallbackKind PlanTacticalFallback(Decision decision, in FallbackUnitState unit)
        {
            FallbackKind kind;
            switch (decision)
            {
                case Decision.RearGuard: kind = FallbackKind.Fallback; break;
                case Decision.FullRetreat: kind = FallbackKind.Retreat; break;
                default: return FallbackKind.None;
            }

            if (unit.UnitTyp != 0 && unit.UnitTyp != 1) return FallbackKind.None;
            if (unit.Routed || unit.MarkedForRout || unit.PermanentlyDetached || unit.OnRetreat) return FallbackKind.None;
            if (unit.RegimentPaths > 0 || unit.MovementMode != 0) return FallbackKind.None;
            if (unit.HasChargeTarget || unit.InMelee > 0f) return FallbackKind.None;
            return kind;
        }

        private static Decision BumpUpOne(Decision d)
        {
            switch (d)
            {
                case Decision.HoldLine: return Decision.Stabilize;
                case Decision.Stabilize: return Decision.Screen;
                case Decision.Screen: return Decision.RearGuard;
                case Decision.RearGuard: return Decision.FullRetreat;
                case Decision.FullRetreat: return Decision.FullRetreat;
                default: return d;
            }
        }
    }
}
