namespace WhiskeyRealism.Tactical
{
    public static class TacticalDoctrineScorer
    {
        public static bool AllowsLocalGroupStanceWriter(int unitType)
        {
            return unitType == TacticalUnitType.BattleGroupBrigade;
        }

        public static TacticalMacroDecision DecideMacro(TacticalMacroDecisionInput input)
        {
            if (input.DebugOverrideActive)
                return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaMacro, "debug-override");
            if (input.SaveRestoreMacroActive)
                return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaMacro, "save-restore");
            if (input.VanillaRetreatActive || input.VanillaMacro == 3)
                return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaMacro, "vanilla-retreat");

            if (input.Odds.InferiorForcePosture == TacticalInferiorForcePosture.PreserveOrRetreat &&
                input.Odds.Confidence >= 0.7f)
                return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Apply, 3, "inferior-no-relief");

            if (input.Odds.InferiorForcePosture == TacticalInferiorForcePosture.DelayOnStrongGround)
                return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Apply, 2, "inferior-delay");

            if (input.Odds.Confidence < 0.35f ||
                input.Odds.InferiorForcePosture == TacticalInferiorForcePosture.ProbeOrHold)
                return new TacticalMacroDecision(
                    TacticalDoctrineDecisionKind.Apply,
                    input.VanillaMacro < 0 ? -1 : 2,
                    "no-reliable-contact");

            if (input.Odds.AllowAssault && input.CommanderAggression01 >= 0.45f)
                return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Apply, 0, "confirmed-weak-point");

            float attackThreshold = 1.25f - input.CommanderAggression01 * 0.15f;
            if (input.Odds.DecisiveSectorId >= 0 && input.Odds.CurrentGlobalOdds >= attackThreshold)
                return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Apply, 1, "decisive-sector");

            return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Apply, 2, "hold");
        }

        public static TacticalGroupStanceDecision DecideGroupStance(TacticalGroupStanceDecisionInput input)
        {
            if (!input.WlAllowsControl)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaStance, "wl-control");
            if (!input.OrderFrictionAllowsChange)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaStance, "order-friction");
            if (input.MacroAi < 0)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaStance, "dynamic-macro");
            if (input.MacroAi == 3)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaStance, "vanilla-retreat");

            if (input.Sector.FlankRisk || input.Sector.Mission == TacticalSectorMission.Refuse)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 2, "refuse");
            if (input.Sector.StrongPoint)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 2, "strongpoint");
            if (input.Sector.Mission == TacticalSectorMission.Probe && input.Sector.Confidence >= 0.35f)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 1, "probe");
            if (input.Sector.Confidence < 0.55f)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaStance, "low-confidence");
            if (input.Sector.Mission == TacticalSectorMission.AttackWeakPoint &&
                (input.MacroAi == 0 || input.MacroAi == 1))
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 3, "attack-weak-point");
            if (input.MacroAi == 2 &&
                ShouldCommitVisibleWeakPoint(input.Sector))
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 3, "defensive-counterstroke");
            if (input.MacroAi == 2 &&
                (input.Sector.Mission == TacticalSectorMission.AttackWeakPoint ||
                 input.Sector.Mission == TacticalSectorMission.Fix ||
                 input.Sector.Mission == TacticalSectorMission.EconomyOfForce))
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 2, "defend-hold");
            if (input.Sector.Mission == TacticalSectorMission.AttackWeakPoint)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 1, "probe-weak-point");
            if (input.Sector.Mission == TacticalSectorMission.Fix ||
                input.Sector.Mission == TacticalSectorMission.EconomyOfForce)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 1, "fix");

            return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 2, "hold");
        }

        public static bool ShouldCommitVisibleWeakPoint(TacticalSectorAssessment sector)
        {
            return sector.Source == TacticalSectorSource.VisibleLineContact &&
                sector.Mission == TacticalSectorMission.AttackWeakPoint &&
                sector.Confidence >= 0.75f &&
                sector.Odds >= 1.75f &&
                !sector.StrongPoint &&
                !sector.FlankRisk;
        }
    }
}
