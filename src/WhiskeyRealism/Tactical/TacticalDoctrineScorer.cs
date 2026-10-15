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

            // SoW-aligned pre-contact path: when sector confidence sits between
            // the low-confidence floor and the act floor, preserve vanilla's stance
            // choice (Apply with vanillaStance) instead of forcing stance=2 (defensive).
            // The movement order for the brigade still flows through #61 posture
            // executor's role assignment + TacticalNavMeshPlanner — independent of
            // this stance write. SoW's brigade-think marches to objective via
            // movement orders, not stance changes; we mirror that by leaving
            // vanilla's stance unchanged here and letting #61 own the waypoint.
            // The decision is Apply (not Skip) so it surfaces in telemetry as
            // "pre-contact-preserve-vanilla"; with decision.GroupStance ==
            // vanillaOrdered the executor only refreshes vanilla's hold through
            // TacticalStanceWriter (a claim, no ChangeStance) — exactly what we want.
            // Strictly below-floor cases still skip as low-confidence.
            if (input.Sector.Confidence < 0.40f)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaStance, "low-confidence");
            if (input.Sector.Confidence < 0.55f)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, input.VanillaStance, "pre-contact-preserve-vanilla");
            // BC9: Whiskey never issues attack stance for a Defender side (vanilla's own
            // stance cascade may still counterattack): both escalations cap at defend.
            int escalated = input.SideDefending ? 2 : 3;
            if (input.Sector.Mission == TacticalSectorMission.AttackWeakPoint &&
                (input.MacroAi == 0 || input.MacroAi == 1))
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, escalated,
                    input.SideDefending ? "defender-weak-point-hold" : "attack-weak-point");
            if (input.MacroAi == 2 &&
                ShouldCommitVisibleWeakPoint(input.Sector))
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, escalated,
                    input.SideDefending ? "defender-counterstroke-hold" : "defensive-counterstroke");
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
