using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Battle.Producers;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// Unit churn (smoke 2026-10-17, the Union 3rd Division): a brigade halted under fire at assault stance, regiments facing every way, a battery whose
// combat behavior flipped 7 <-> 9. The artillery loop is pinned in StanceArtilleryMigrationTests; this class holds the other causes.
internal static class UnitChurnTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("unit-churn LEAF-POSTURE a leaf brigade records the posture it was given, so the facing pulse and fire control see it", LeafBrigadeRecordsItsPosture),
            ("unit-churn LEAF-POSTURE-WIRING ProduceLeaf records before it decides to move, and marks the submitted move", LeafPostureWiring),
            ("unit-churn FIX-ASSAULT a fixing force vanilla escalated to assault is taken back to the attack stance its task maps to", FixingForceAssaultIsDemotedToAttack),
            ("unit-churn FIX-ASSAULT-HOLD the demotion's hold is not given away by the scorer's own claim, so stance 3 and 4 do not alternate", FixDemotionHoldIsNotGivenAway),
            ("unit-churn FIX-ASSAULT-WIRING the coordinator hands the doctrine order's task to the denial", FixAssaultWiring),
            ("unit-churn FIX-ASSAULT-DIVISION a pool division at assault is demoted too: its stance order cascades to the leaf brigades under it, and the stance writer refuses a leaf brigade", FixAssaultReachesTheDivision),
            ("unit-churn COLUMN-DEPLOY a march-column infantry regiment fired on, flanked or engaged deploys at once; a regiment in line, mounted, withdrawing or routed does not", ColumnDeployDecision),
            ("unit-churn COLUMN-DEPLOY-PLAN the deploy intent is planned for a column regiment and is a duplicate for one already in line", ColumnDeployPlan),
            ("unit-churn COLUMN-DEPLOY-WIRING the executor stops the march, then re-forms to line facing the threat; the pulse producer submits it behind its flag", ColumnDeployWiring),
        });
    }

    // ------------------------------------------------------------------ leaf brigades had no local combat response

    // The 2026-10-17 session: Union (side 0) rows of TacticalFacingPulse were 186 Division scope and none at Brigade or Regiment scope; Confederate (side 1)
    // had 317 Brigade and 105 Regiment rows. All eleven Union brigades were leaf brigades under a pool division. PostureIntentProducer.ProduceLeaf never set
    // the brigade's GroupPosture row, and FacingPulseIntentProducer and FireControlIntentProducer read only groups with Has set.
    private static void LeafBrigadeRecordsItsPosture()
    {
        GroupPosture reserve = LeafPosture.Record(DirectChildRole.Reserve, CommandTaskType.ReserveWait, 708956, closeEngaged: false, flankRisk: true);
        AssertTrue(reserve.Has, "the row exists");
        AssertEqual(CommandEchelonKind.BrigadeLike, reserve.State.Echelon, "a brigade: the pulse's Brigade scope");
        AssertEqual(CommandNodeRole.Reserve, reserve.State.Role, "the leaf role as the ledger names it");
        AssertEqual(CommandTaskType.ReserveWait, reserve.State.Task, "the leaf's task");
        AssertTrue(reserve.FlankRisk && !reserve.CloseEngaged, "the contact flags the pulse reads");
        AssertFalse(reserve.Submitted, "no move yet");
        AssertFalse(reserve.FacingSubmitted, "nor a pulse");
        AssertEqual(CommandNodeRole.Defender, LeafPosture.Record(DirectChildRole.RefuseRight, CommandTaskType.GuardFlank, 1, false, false).State.Role, "a refused flank defends");
        AssertEqual(CommandNodeRole.Unknown, LeafPosture.Record(DirectChildRole.Unknown, CommandTaskType.GuardFlank, 1, false, false).State.Role, "an unknown role stays unknown");

        // What the record buys: the pulse's brigade decision for a reserve brigade with a flank under pressure (a held task faces the threat).
        TacticalFacingPulseDecision d = TacticalFacingPulseDoctrine.Decide(new TacticalFacingPulseInput(
            TacticalFacingPulseScope.Brigade, reserve.State.Task, currentFormation: 1, targetFormation: 0, currentFacingDegrees: 10f, targetFacingDegrees: 120f,
            hasThreatBearing: true, TacticalFacingThreatSource.Visible, reserve.CloseEngaged, reserve.FlankRisk, playerProtected: false, routed: false,
            pendingOrder: false, activeMove: false, recentPulse: false, toleranceDegrees: 20f));
        AssertEqual(TacticalFacingPulseAction.CorrectFormationAndFacing, d.Action, "the brigade is turned and re-formed");
    }

    private static void LeafPostureWiring()
    {
        string producer = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        string leaf = FlankResponseTests.Between(producer, "private static void ProduceLeaf(", "// BR-M1: holds a Main-effort brigade");
        FlankResponseTests.AssertPinned(leaf, "t.Groups[i] = LeafPosture.Record(", "the leaf path fills the brigade's posture row");
        int record = leaf.IndexOf("t.Groups[i] = LeafPosture.Record(", StringComparison.Ordinal);
        int decide = leaf.IndexOf("LeafBrigadeMovePolicy.Decide(", StringComparison.Ordinal);
        int exit = leaf.IndexOf("if (!move.Move)", StringComparison.Ordinal);
        AssertTrue(record >= 0 && decide > record && exit > decide, "before the move decision, so a brigade that does not move (a reserve, a hold) is recorded too");
        FlankResponseTests.AssertPinned(leaf, "t.Groups[i].Submitted = true;", "and marked when the move was submitted, as the pool path marks it");
    }

    // ------------------------------------------------------------------ a fixing force at assault

    // Vanilla's AdjustGroupAIStance (4240) escalates a strong line group to assault (4) on its own strength test. The doctrine had an opinion only for
    // an attack (Allow), a reserve, a fall back or a works hold; a FixEnemy brigade got "no-doctrine-opinion" and "vanilla-charge-preserved", so it stood
    // at 4 (the assault placement, cover skipped) with a nine thousand man enemy near. The task maps to the attack stance (3), never to 4.
    private static void FixingForceAssaultIsDemotedToAttack()
    {
        ChargeDenialDecision fix = TacticalChargeDenial.DecideForVanillaAssault(new ChargeDenialInput(true, true, DoctrineConsumerAction.Observe, false, fixEnemyTask: true));
        AssertTrue(fix.Demotes, "a fixing force is not left at assault");
        AssertEqual(TacticalCommandTaskToStanceMapper.Map(CommandTaskType.FixEnemy), fix.DemoteStance, "to the stance its task maps to (attack, 3)");
        AssertEqual("charge-demote-fix", fix.Reason, "with its own reason");

        AssertFalse(TacticalChargeDenial.DecideForVanillaAssault(new ChargeDenialInput(true, true, DoctrineConsumerAction.Observe, false, fixEnemyTask: false)).Demotes,
            "no task, no opinion: vanilla's assault is preserved as before");
        AssertFalse(TacticalChargeDenial.DecideForVanillaAssault(new ChargeDenialInput(false, true, DoctrineConsumerAction.Observe, false, fixEnemyTask: true)).Demotes,
            "Enable Tactical Charge Denial off: preserved");
        ChargeDenialDecision deny = TacticalChargeDenial.DecideForVanillaAssault(new ChargeDenialInput(true, true, DoctrineConsumerAction.Deny, false, fixEnemyTask: true));
        AssertEqual(TacticalChargeDenial.DoctrineDemoteStance, deny.DemoteStance, "a doctrine Deny keeps its own, lower stance");
        AssertFalse(TacticalChargeDenial.DecideForVanillaAssault(new ChargeDenialInput(true, true, DoctrineConsumerAction.Allow, false, fixEnemyTask: true)).Demotes,
            "a doctrine Allow still agrees with the escalation");

        // The producer's intent: a Safety stance 3 for the group at 4.
        var group = new UnitRecord { InstanceId = 21, UnitTyp = TacticalUnitType.BattleGroupBrigade, AiStanceOrdered = 4, AiStance = 4, ParentIndex = -1,
            Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup };
        var resolved = new TacticalResolvedGroupDecision(fix.DemoteStance, TacticalDoctrineDecisionKind.Skip, fix.Reason, false, false, true, ChargeOverrideAction.DemoteToHold);
        AssertTrue(TacticalChargeDenial.TryDemotionIntent(group, resolved, out BattleOrderIntent intent), "the demotion intent");
        AssertEqual(3, intent.Value, "stance 3");
        AssertEqual(IntentPriority.Safety, intent.Priority, "Safety");
    }

    // The scorer claims the ledger's stance (3) every tick. With vanilla's own per-group stance at 4 that claim would Yield (TacticalStanceOwnership
    // YieldsToVanilla(4, 3)): the hold released, vanilla at 4 again, the demotion again. Inside the demotion's hold the claim must be refused instead.
    private static void FixDemotionHoldIsNotGivenAway()
    {
        const float t0 = 10.0f;
        var demote = new StanceWriteInput(true, 3, currentOrderedStance: 4, macroAi: 1, nowHours: t0, record: default(StanceHoldRecord), source: StanceWriteSource.ChargeDemotion,
            vanillaGroupStance: 4);
        StanceWriteDecision written = TacticalStanceOwnership.Decide(demote);
        AssertEqual(StanceWriteAction.Write, written.Action, "the demotion writes");
        StanceHoldRecord hold = TacticalStanceOwnership.Next(written, demote);

        var claim = new StanceWriteInput(true, 3, currentOrderedStance: 3, macroAi: 1, nowHours: t0 + 0.05f, record: hold, source: StanceWriteSource.Scorer, vanillaGroupStance: 4);
        StanceWriteDecision d = TacticalStanceOwnership.Decide(claim);
        AssertTrue(d.Action != StanceWriteAction.Yield, "inside the hold the scorer's claim does not hand the group back to vanilla");
        AssertEqual(StanceWriteAction.Refuse, d.Action, "it is refused: the hold belongs to the higher source");
        AssertEqual("whiskey-hold", d.Reason, "as a hold");

        // Past the hold the old rule stands: vanilla's escalation agrees with an attack target.
        var late = new StanceWriteInput(true, 3, currentOrderedStance: 3, macroAi: 1, nowHours: t0 + TacticalStanceOwnership.HoldHours + 0.01f, record: hold,
            source: StanceWriteSource.Scorer, vanillaGroupStance: 4);
        AssertEqual(StanceWriteAction.Yield, TacticalStanceOwnership.Decide(late).Action, "after the hold lapses the escalation is yielded to, then demoted again: one pair of orders per hold");
    }

    private static void FixAssaultWiring()
    {
        string coordinator = FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalBattleCoordinatorRuntime.cs");
        string resolve = FlankResponseTests.Between(coordinator, "DoctrineConsumerAction doctrineAction = DoctrineConsumerAction.Observe;", "// 2) Charge override without a doctrine opinion.");
        FlankResponseTests.AssertPinned(resolve, "doctrineTask = order.Task;", "the doctrine order's task is kept");
        FlankResponseTests.AssertPinned(resolve, "fixEnemyTask: doctrineTask == CommandTaskType.FixEnemy", "and handed to the denial");
    }

    // ------------------------------------------------------------------ the march column under fire

    // Vanilla puts any regiment with a path longer than mindistanceformarchcolumn (60 m, unitprefs 881) and no enemy within
    // distancetoenemyforinterruption (100 m) into march column (CheckFormationChange 127662-127668), and takes it out only inside that 100 m, at the end of
    // its path, or on a manual stop (132498-132511); it deploys facing lastwaypointrotation, the march heading, not the enemy (127677). A column shot
    // from 300 m, or in the flank, kept marching in column (melee loss modifier 2.5, unitprefs 1453). The brigade object's own formation field reads 3
    // for good (ChangeRegimentFormation 135437 forces 3 for every group), so the decision matrix's "3:MarchColumn" on a brigade is no column state.
    private static ColumnDeployInput Column(int formation = 3, bool receivedFire = false, int fireFromFlank = 0, int outflanked = 0, bool groupFlankRisk = false,
        bool groupCloseEngaged = false, bool infantry = true, bool mounted = false, bool withdrawing = false, bool routed = false, bool recent = false)
    {
        return new ColumnDeployInput(infantry, mounted, formation, receivedFire, fireFromFlank, outflanked, groupFlankRisk, groupCloseEngaged, withdrawing, routed, recent);
    }

    private static void ColumnDeployDecision()
    {
        AssertEqual(ColumnDeployReason.None, ColumnDeployment.Decide(Column()), "a column nobody threatens marches on (vanilla's own rules apply)");
        AssertEqual(ColumnDeployReason.FiredOn, ColumnDeployment.Decide(Column(receivedFire: true)), "fired on: deploy");
        AssertEqual(ColumnDeployReason.Flanked, ColumnDeployment.Decide(Column(fireFromFlank: 1)), "fire from the flank");
        AssertEqual(ColumnDeployReason.Flanked, ColumnDeployment.Decide(Column(outflanked: 2)), "outflanked");
        AssertEqual(ColumnDeployReason.Flanked, ColumnDeployment.Decide(Column(groupFlankRisk: true)), "its brigade's perceived flank risk");
        AssertEqual(ColumnDeployReason.Engaged, ColumnDeployment.Decide(Column(groupCloseEngaged: true)), "its brigade in close contact");
        AssertEqual(ColumnDeployReason.FiredOn, ColumnDeployment.Decide(Column(receivedFire: true, fireFromFlank: 1, groupCloseEngaged: true)), "fire is named before the flank and the contact");
        AssertEqual(ColumnDeployReason.Flanked, ColumnDeployment.Decide(Column(fireFromFlank: 1, groupCloseEngaged: true)), "the flank before the contact");

        AssertEqual(ColumnDeployReason.None, ColumnDeployment.Decide(Column(formation: 0, receivedFire: true)), "already in line");
        AssertEqual(ColumnDeployReason.None, ColumnDeployment.Decide(Column(formation: 2, receivedFire: true)), "any other regiment formation");
        AssertEqual(ColumnDeployReason.None, ColumnDeployment.Decide(Column(infantry: false, receivedFire: true)), "artillery and cavalry limber and mount in formation 3");
        AssertEqual(ColumnDeployReason.None, ColumnDeployment.Decide(Column(mounted: true, receivedFire: true)), "a mounted regiment");
        AssertEqual(ColumnDeployReason.None, ColumnDeployment.Decide(Column(withdrawing: true, receivedFire: true)), "a column falling back under fire keeps falling back");
        AssertEqual(ColumnDeployReason.None, ColumnDeployment.Decide(Column(routed: true, receivedFire: true)), "a routed regiment");
        AssertEqual(ColumnDeployReason.None, ColumnDeployment.Decide(Column(recent: true, receivedFire: true)), "inside the cooldown of its last deployment");

        AssertTrue(ColumnDeployment.WithdrawalTask(CommandTaskType.FallBackToLine), "a fall back is a withdrawal");
        AssertTrue(ColumnDeployment.WithdrawalTask(CommandTaskType.Delay), "so is a delay");
        AssertFalse(ColumnDeployment.WithdrawalTask(CommandTaskType.AttackObjective), "an attack is not");
        AssertFalse(ColumnDeployment.WithdrawalTask(CommandTaskType.ReserveWait), "nor a reserve");
        AssertEqual(3, ColumnDeployment.RegimentMarchColumn, "Regiment.formation 3 (CheckFormationChange 127668)");
        AssertEqual(0, ColumnDeployment.RegimentLine, "Regiment.formation 0, the line CheckFormationChange deploys to (127677)");
    }

    private static void ColumnDeployPlan()
    {
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), null);
        BattleOrderIntent deploy = ColumnDeployment.Intent(77, 135f);
        AssertEqual(OrderChannel.Formation, deploy.Channel, "a formation write");
        AssertEqual(ColumnDeployment.RegimentLine, deploy.Value, "to line");
        AssertTrue((deploy.Flags & IntentFlags.Deploy) != 0, "flagged Deploy: the executor stops the march first");
        AssertEqual(135f, deploy.Heading, "facing the threat");
        AssertEqual(IntentSource.FacingPulse, deploy.Source, "the pulse's reaction");
        AssertEqual(IntentPriority.Executor, deploy.Priority, "past the pulse's scorer rung, short of Safety (a withdrawal outranks it)");
        AssertEqual(-1f, ColumnDeployment.Intent(77, -5f).Heading, "no threat bearing: no facing");

        BattleSnapshot s = ChurnSnapshot(regimentFormation: 3);
        ExecutionPlan plan = executor.Plan(s, deploy, 1.0);
        AssertTrue(plan.Execute, "planned for a regiment in column");
        s = ChurnSnapshot(regimentFormation: 0);
        AssertEqual(SkipReason.Duplicate, executor.Plan(s, deploy, 1.0).Skip, "a regiment already in line has nothing to deploy from");
    }

    private static BattleSnapshot ChurnSnapshot(int regimentFormation)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.SetSliceCount(20);
        s.AddOwn(new UnitRecord
        {
            InstanceId = 77, UnitTyp = TacticalUnitType.Infantry, X = 500f, Z = -500f, Formation = regimentFormation, Strength = 400, ParentIndex = -1,
            RegimentPaths = 1, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry,
        });
        s.IndexCommandTree(null);
        return s;
    }

    private sealed class AllowGuard : IOrderGuard
    {
        public bool CanWrite(in UnitRecord unit, out SkipReason reason)
        {
            reason = SkipReason.None;
            return true;
        }
    }

    private static void ColumnDeployWiring()
    {
        string runtime = FlankResponseTests.SourceText("Tactical", "Battle", "OrderExecutorRuntime.cs");
        FlankResponseTests.AssertPinned(runtime, "(w.Flags & IntentFlags.Deploy) != 0", "the formation case routes the flagged intent to the deploy");
        string deploy = FlankResponseTests.Between(runtime, "private static bool IssueDeploy(", "private static bool IssueFormation(");
        int live = deploy.IndexOf("unit.formation != ColumnDeployment.RegimentMarchColumn", StringComparison.Ordinal);
        int stop = deploy.IndexOf("\"StopRegiment\"", StringComparison.Ordinal);
        int form = deploy.IndexOf("unit.ChangeRegimentFormation(GameVars.SetFormationParam(ColumnDeployment.RegimentLine, manualset: true", StringComparison.Ordinal);
        AssertTrue(live >= 0 && stop > live && form > stop, "the live column test, then the manual stop, then the re-form: a regiment with no path is not re-columned by vanilla (127662-127668)");
        FlankResponseTests.AssertPinned(deploy, "unit.unittyp != TacticalUnitType.Infantry", "infantry only");
        FlankResponseTests.AssertPinned(deploy, "unit.isrouted", "never a routed regiment");

        string pulse = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        string produce = FlankResponseTests.Between(pulse, "internal sealed class FacingPulseIntentProducer", "private static string FacingSourceOf(");
        FlankResponseTests.AssertPinned(produce, "Plugin.EnableColumnDeployment", "behind its own rollback flag");
        FlankResponseTests.AssertPinned(produce, "ColumnDeployment.Decide(", "the pure decision");
        FlankResponseTests.AssertPinned(produce, "ColumnDeployment.Intent(", "builds the one intent");
        FlankResponseTests.AssertPinned(produce, "ColumnDeployment.WithdrawalTask(", "and leaves a withdrawing ancestor's regiments alone");
        FlankResponseTests.AssertPinned(produce, "IntentSource.Withdrawal", "as well as the withdrawal producer's");
        string plugin = FlankResponseTests.SourceText("Plugin.cs");
        FlankResponseTests.AssertPinned(plugin, "\"Enable Column Deployment\"", "the flag is declared");
    }

    // The 2026-10-17 smoke's brigades were leaf brigades: TacticalStanceWriter refuses a group that is not in the AI pool (FindOwningAiBattle: unitsused only,
    // "not-ai-group"), and their stance was their division's, delivered as a stance order that vanilla applies to the advised group and every unit on its
    // level (BattleUnits order processing 125390: PerformActionForGroupForAllUnitsOnLevel "SetAIStance"). The 3rd Division sat at 4 from 18:51 and its brigades
    // took the 4 when the courier order arrived (19:00:09-10). The charge demotion therefore has to reach a pool division; #45's scorer stays brigade-only.
    private static void FixAssaultReachesTheDivision()
    {
        AssertTrue(TacticalDoctrineScorer.AllowsChargeDemotion(TacticalUnitType.BattleGroupBrigade), "a brigade");
        AssertTrue(TacticalDoctrineScorer.AllowsChargeDemotion(TacticalUnitType.BattleGroupDivision), "a division");
        AssertFalse(TacticalDoctrineScorer.AllowsChargeDemotion(TacticalUnitType.BattleGroupArmy), "not the army");
        AssertFalse(TacticalDoctrineScorer.AllowsChargeDemotion(TacticalUnitType.Infantry), "not a regiment");
        AssertFalse(TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(TacticalUnitType.BattleGroupDivision), "the scorer stays brigade-only");

        var division = new UnitRecord { InstanceId = 31, UnitTyp = TacticalUnitType.BattleGroupDivision, AiStanceOrdered = 4, AiStance = 4, ParentIndex = -1,
            Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup };
        var resolved = new TacticalResolvedGroupDecision(3, TacticalDoctrineDecisionKind.Skip, "charge-demote-fix", false, false, true, ChargeOverrideAction.DemoteToHold);
        AssertTrue(TacticalChargeDenial.TryDemotionIntent(division, resolved, out BattleOrderIntent intent), "a division at assault gets the demotion intent");
        AssertEqual(3, intent.Value, "stance 3");
        AssertEqual(IntentSource.ChargeDemotion, intent.Source, "from the charge demotion");
        UnitRecord army = division;
        army.UnitTyp = TacticalUnitType.BattleGroupArmy;
        AssertFalse(TacticalChargeDenial.TryDemotionIntent(army, resolved, out _), "an army does not");
        UnitRecord notAssault = division;
        notAssault.AiStanceOrdered = 3;
        AssertFalse(TacticalChargeDenial.TryDemotionIntent(notAssault, resolved, out _), "a division not at assault does not");

        string coordinator = FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalBattleCoordinatorRuntime.cs");
        string loop = FlankResponseTests.Between(coordinator, "bool localStanceWriter = TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(", "int vanillaOrdered = own[i].AiStanceOrdered;");
        FlankResponseTests.AssertPinned(loop, "TacticalDoctrineScorer.AllowsChargeDemotion(", "a pool division is visited for its charge answer");
        FlankResponseTests.AssertPinned(coordinator, "CacheDivisionChargeAnswer(", "which caches only the charge answer, a Skip");
        string divisionCache = FlankResponseTests.Between(coordinator, "private static void CacheDivisionChargeAnswer(", "private static void PopulateResolvedDecisionsForSide(");
        FlankResponseTests.AssertPinned(divisionCache, "|| order.Task != CommandTaskType.FixEnemy) return;", "for a FixEnemy doctrine order and no other task");
        FlankResponseTests.AssertPinned(divisionCache, "kind: TacticalDoctrineDecisionKind.Skip", "never an Apply: #45 writes no division stance");
        FlankResponseTests.AssertPinned(divisionCache, "chargeAction: ChargeOverrideAction.DemoteToHold", "only the demotion");
        string producer = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "GroupStanceIntentProducer.cs");
        FlankResponseTests.AssertPinned(producer, "!TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(own[i].UnitTyp)", "and the scorer's producer still skips a division");
        string demotion = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "ChargeDemotionIntentProducer.cs");
        int pool = demotion.IndexOf("if (!ArmyPostureInputsRuntime.InAiPool(alliance, own[i].InstanceId)) continue;", StringComparison.Ordinal);
        int resolve = demotion.IndexOf("GroupStanceIntentProducer.TryResolvedDecision(", StringComparison.Ordinal);
        AssertTrue(pool >= 0 && resolve > pool, "the demotion is submitted for a pool group only: the writer refuses a leaf brigade (not-ai-group), whose stance is its ancestor's");
    }
}
