using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// Smoke 2026-10-17: the Union, vanilla's Attacker with attack plans and macro attack, spent the battle in Consolidate and its tasks were GuardFlank,
// Screen and FormUp (AttackObjective: 0). Two readings led there. The readiness check priced a probing army against an enemy it had not seen as an
// enemy equal to itself (odds 1.0 = unknown, so enemy strength = own strength, ratio = fatigue x ammo < the required 1.2), and a cautious commander
// then "held fatigued" at the end of the probe budget and consolidated a fresh army. And Consolidate resumed only while the reserve share committed was
// under 0.6, a share that reads 0.85 for any army whose designated reserve is one group of five, so even 5:1 odds did not resume the attack.
internal static class AttackerPhaseProgressionTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("attacker phase a probing army that has not found the enemy commits at the end of the probe budget instead of consolidating", AnUnseenEnemyIsNotAnEqualOne),
            ("attacker phase a genuinely exhausted army still holds at the end of the probe budget", AnExhaustedArmyStillHolds),
            ("attacker phase an army with a known weak enemy still reads its fatigue against him", AKnownEnemyIsStillPricedAgainstTheForce),
            ("attacker phase consolidate resumes the attack at decisive odds whatever share of reserves reads committed", ConsolidateResumesAtDecisiveOdds),
            ("attacker phase consolidate stays when the odds are only fair and the reserves are spent", ConsolidateStaysWhenOddsAreFairAndReservesSpent),
            ("attacker phase a replan keeps the phase the army has reached", AReplanKeepsAnAdvancedPhase),
            ("attacker phase a replan to another plan keeps the phase too", AReplanToAnotherPlanKeepsThePhase),
            ("attacker phase a committed attacker in contact reaches the main effort and attacks with it", AnAttackerInContactReachesTheMainEffort),
            ("attacker phase attack tasks go to the main effort and flank guards only to flank guards", GuardFlankOnlyToFlankGuards),
            ("attacker phase a main effort with nobody supporting it does not assault, whatever the odds, while the army has other nodes", ALoneMainEffortDoesNotAssault),
            ("attacker phase a main effort supported by a neighbour may assault", ASupportedMainEffortMayAssault),
        });
    }

    private static readonly EnemyVisibleState Unseen = new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0.35f, false, false, intelConfidence01: 0f);

    private static ArmyOrchestrator Attacker(BattlePhase phase = BattlePhase.Probe)
    {
        ArmyTickCycle.Reset();
        // Aggression -0.4 -> 0.3: a cautious commander, as the Union's (commanderInit 0.31).
        return TacticalEvidenceFixTests.OrchestratorWithPlan(phase, mainSector: 2, aggression: -0.4f);
    }

    private static void Tick(ArmyOrchestrator orch, float battleSeconds, float odds, EnemyVisibleState enemy, float fatigue = 0.2f, float reserves = 0.35f)
        => ArmyTickCycle.MaybeReplan(orch, 0f, new ArmyEvidence(odds, TerrainKind.Open, 2), enemy, 15000f, 1f, reserves, 0f, 600, fatigue, 0.9f, 0f, 0f,
            battleSeconds, double.NaN, null);

    private static float ProbeBudget => TacticalPhaseProgressionDoctrine.ProbeBudgetForAggression(0.3f);

    private static void AnUnseenEnemyIsNotAnEqualOne()
    {
        var orch = Attacker();
        Tick(orch, ProbeBudget + 60f, odds: 1f, Unseen);
        AssertEqual(BattlePhase.MainEffort, orch.CurrentPlan.Phase, "the probe budget is up and nothing says stop: the attack goes in");
    }

    private static void AnExhaustedArmyStillHolds()
    {
        var orch = Attacker();
        Tick(orch, ProbeBudget + 60f, odds: 1f, Unseen, fatigue: 0.95f);
        AssertEqual(BattlePhase.Consolidate, orch.CurrentPlan.Phase, "an army with a fifth of its strength left is not sent forward");
    }

    private static void AKnownEnemyIsStillPricedAgainstTheForce()
    {
        // The enemy is seen (confidence 0.9) and the odds are poor but not routing (0.95): readiness still reads the army against him.
        var seen = new EnemyVisibleState(new[] { new EnemyVisibleSector(2, 9000f, 9500f, false) }, 0.5f, true, false, intelConfidence01: 0.9f);
        var orch = Attacker();
        Tick(orch, ProbeBudget + 60f, odds: 0.95f, seen);
        AssertEqual(BattlePhase.Consolidate, orch.CurrentPlan.Phase, "an enemy as strong as the army, a cautious commander: the shipped hold");
        var strong = new EnemyVisibleState(new[] { new EnemyVisibleSector(2, 9000f, 1500f, false) }, 0.5f, true, false, intelConfidence01: 0.9f);
        var orch2 = Attacker();
        Tick(orch2, ProbeBudget + 60f, odds: 6f, strong);
        AssertEqual(BattlePhase.MainEffort, orch2.CurrentPlan.Phase, "a known weak enemy: commit");
    }

    private static TacticalPhaseProgressionDoctrine.Input Consolidating(float odds, float reserves)
        => new TacticalPhaseProgressionDoctrine.Input(BattlePhase.Consolidate, 3000f, odds, 1f, odds, 1f, 1f, 0.4f, reserves, 0.3f);

    private static void ConsolidateResumesAtDecisiveOdds()
    {
        AssertEqual(BattlePhase.MainEffort, TacticalPhaseProgressionDoctrine.Decide(Consolidating(5.5f, 0.88f)).NextPhase, "5.5:1 with 88 % of the army 'committed': attack");
        AssertEqual(BattlePhase.MainEffort, TacticalPhaseProgressionDoctrine.Decide(Consolidating(5.5f, 0.99f)).NextPhase, "even with the reserve share spent: the odds are decisive");
        AssertEqual(BattlePhase.MainEffort, TacticalPhaseProgressionDoctrine.Decide(Consolidating(1.6f, 0.55f)).NextPhase, "the shipped rule: 1.6:1 with reserves in hand");
    }

    private static void ConsolidateStaysWhenOddsAreFairAndReservesSpent()
    {
        AssertEqual(BattlePhase.Consolidate, TacticalPhaseProgressionDoctrine.Decide(Consolidating(1.6f, 0.88f)).NextPhase, "1.6:1 with the reserves spent: consolidate");
        AssertEqual(BattlePhase.Consolidate, TacticalPhaseProgressionDoctrine.Decide(Consolidating(1.1f, 0.2f)).NextPhase, "near parity: consolidate");
    }

    private static void AReplanKeepsAnAdvancedPhase()
    {
        var orch = Attacker(BattlePhase.MainEffort);
        Tick(orch, 600f, odds: 1f, Unseen);
        ArmyTickCycle.MaybeReplan(orch, 0f, new ArmyEvidence(1f, TerrainKind.Open, 2), Unseen, 15000f, 0.3f, 0.35f, 0f, 60, 0.2f, 0.9f, 0f, 0f, 600f, double.NaN, null);
        AssertTrue(orch.CurrentPlan.Phase != BattlePhase.Probe, "a replan does not send an advanced army back to Probe: " + orch.CurrentPlan.Phase);
    }

    private static void AReplanToAnotherPlanKeepsThePhase()
    {
        var orch = Attacker(BattlePhase.Exploit);
        orch.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.GenericMethodical, BattlePhase.Exploit, 2, null, null, 1.2f, 0f, 7));
        orch.Replan(new ArmyEvidence(1f, TerrainKind.Open, 2), new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, null), hysteresis: false, "MainEffortSectorLoss");
        AssertEqual(BattlePhase.Exploit, orch.CurrentPlan.Phase, "the new plan starts where the army stands");
        AssertEqual(2, orch.CurrentPlan.MainEffortSector, "and on its main effort band");
    }

    private static void AnAttackerInContactReachesTheMainEffort()
    {
        // The Union at 14.1 h: the enemy is seen (confidence 0.9), odds 5.5, a cautious commander, a fresh army, the probe budget long spent.
        var seen = new EnemyVisibleState(new[] { new EnemyVisibleSector(2, 12000f, 2200f, false) }, 0.5f, true, false, intelConfidence01: 0.9f);
        var orch = Attacker();
        Tick(orch, 600f, odds: 5.5f, seen);
        AssertEqual(BattlePhase.MainEffort, orch.CurrentPlan.Phase, "found the enemy in force: the probe commits within its first review");
        // And a Consolidate army at those odds goes back to the attack at its next review.
        var consolidating = Attacker(BattlePhase.Consolidate);
        Tick(consolidating, 600f, odds: 5.5f, seen, reserves: 0.88f);
        AssertEqual(BattlePhase.MainEffort, consolidating.CurrentPlan.Phase, "the Union's smoke state, 4:1 and above, resumes");
    }

    private static void GuardFlankOnlyToFlankGuards()
    {
        // The main-effort nodes of a committed operation in contact attack; flank guards (the Defender role: RefuseLeft / RefuseRight) guard the flank;
        // nothing else is told to guard it.
        var nodes = new[]
        {
            CommandNodeOperationalState.Create("main", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 0f, 0f, 0f),
            CommandNodeOperationalState.Create("support", CommandEchelonKind.DivisionLike, CommandNodeRole.SupportingAttack, CommandTaskType.FormUp, 100f, 0f, 0f),
            CommandNodeOperationalState.Create("fix", CommandEchelonKind.DivisionLike, CommandNodeRole.FixingForce, CommandTaskType.FormUp, 200f, 0f, 0f),
            CommandNodeOperationalState.Create("reserve", CommandEchelonKind.DivisionLike, CommandNodeRole.Reserve, CommandTaskType.FormUp, 300f, 0f, 0f),
            CommandNodeOperationalState.Create("guard", CommandEchelonKind.DivisionLike, CommandNodeRole.Defender, CommandTaskType.FormUp, 400f, 0f, 0f),
        };
        var operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "enemy-line-0", 900f);
        var picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("enemy-line-0", TacticalObjectiveType.EnemyLine, 2200f, 0.9f, true, 0.4f, 1000f, 600f, 0f, 0f)
        });
        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, 12000f, 100f, 0.3f, BattleStance.Attacker);
        AssertEqual(CommandTaskType.AttackObjective, orders[0].Task, "the main effort attacks");
        AssertEqual(CommandTaskType.SupportAttack, orders[1].Task, "the support supports the attack");
        AssertEqual(CommandTaskType.FixEnemy, orders[2].Task, "the fixing force fixes");
        AssertEqual(CommandTaskType.ReserveWait, orders[3].Task, "the reserve waits");
        for (int i = 0; i < 4; i++) AssertTrue(orders[i].Task != CommandTaskType.GuardFlank, nodes[i].NodeId + " is no flank guard");
    }

    private static CommandDoctrineOrder[] BuildWith(params CommandNodeRole[] roles)
    {
        var nodes = new CommandNodeOperationalState[roles.Length];
        for (int i = 0; i < roles.Length; i++)
            nodes[i] = CommandNodeOperationalState.Create("n" + i, CommandEchelonKind.DivisionLike, roles[i], CommandTaskType.FormUp, i * 100f, 0f, 0f);
        var operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "enemy-line-0", 900f);
        var picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("enemy-line-0", TacticalObjectiveType.EnemyLine, 2200f, 0.9f, true, 0.4f, 1000f, 600f, 0f, 0f)
        });
        return CommandDoctrineAssignment.Build(nodes, operation, picture, 12000f, 100f, 0.3f, BattleStance.Attacker);
    }

    // 5.4 : 1, committed, the enemy line exposed and confidently seen.
    private static void ALoneMainEffortDoesNotAssault()
    {
        CommandDoctrineOrder[] orders = BuildWith(CommandNodeRole.MainEffort, CommandNodeRole.Reserve, CommandNodeRole.ScreeningForce);
        AssertEqual(CommandTaskType.AttackObjective, orders[0].Task, "it keeps its task");
        AssertFalse(orders[0].Sop.AllowsMajorAttack, "but may not make the major attack alone");
        AssertContains(orders[0].Sop.Reason, "support", "and says why");
        CommandDoctrineOrder[] soloArmy = BuildWith(CommandNodeRole.MainEffort);
        AssertTrue(soloArmy[0].Sop.AllowsMajorAttack, "an army of one node has nobody to support it");
    }

    private static void ASupportedMainEffortMayAssault()
    {
        CommandDoctrineOrder[] orders = BuildWith(CommandNodeRole.MainEffort, CommandNodeRole.SupportingAttack, CommandNodeRole.Reserve);
        AssertTrue(orders[0].Sop.AllowsMajorAttack, "with a supporting attack beside it: the assault is on");
    }
}
