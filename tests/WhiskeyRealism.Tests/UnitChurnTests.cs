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
}
