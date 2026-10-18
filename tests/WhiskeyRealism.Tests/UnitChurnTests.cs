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
            ("unit-churn COLUMN-DEPLOY-APPLY the deploy stops the march, then turns the regiment to the threat after the stop restored its line; a regiment on a bridge is not touched", ColumnDeployAppliesTheHeading),
            ("unit-churn APPROACH-MARCH an approach deploy keeps the regiment's path, so the advance goes on in line; a deploy under fire stops it", ApproachDeployKeepsMarching),
            ("unit-churn APPROACH-FACING an in-march deploy faces the way the regiment walks unless the threat is within 60 degrees of it, so a line never walks backwards", ApproachDeployFacesTheWay),
            ("unit-churn APPROACH-NEEDS-MARK an in-march deploy is submitted only for a marked regiment; an unmarked one is left in vanilla's column, not stopped", ApproachDeployNeedsTheMark),
            ("unit-churn APPROACH-PASS the pass rebuilds the table: a unit it does not look at, or that fails an arm, is unmarked at once", ApproachPassMarksOnlyWhatItSees),
            ("unit-churn APPROACH-FALLBACK a group whose ledger task or effective task is a fall back or a delay is withdrawing, and so is a leaf brigade the override sent back", ApproachFallBackOverrideIsWithdrawing),
            ("unit-churn APPROACH-WITHDREW-MOVE a recent move of a fall back or a delay is a withdrawal whoever issued it", ApproachWithdrawalMoveWhateverTheSource),
            ("unit-churn APPROACH-WALK-MIN a path end within a block's length is no walking direction", ApproachWalkHeadingMinimum),
            ("unit-churn APPROACH-WALK the group a regiment follows is found up to four ancestors away, and a withdrawal in that reach holds it back", ApproachWalkReachesTheGroup),
            ("unit-churn COLUMN-DEPLOY-PLAN the deploy intent is planned for a column regiment and is a duplicate for one already in line", ColumnDeployPlan),
            ("unit-churn COLUMN-DEPLOY-WIRING the executor holds a bridge, stops the march, applies the heading through the host and reports it; the pulse producer submits it behind its flag", ColumnDeployWiring),
            ("unit-churn APPROACH-SKIP every arm of the re-column skip: the flag, Commander Mode Active, the write guard, infantry, a regiment not already in column, a marked regiment", ApproachSkipArms),
            ("unit-churn APPROACH-MARK a regiment is marked only for an AI line advance or hold with the enemy inside the assault range; never a road march, a withdrawal, a player or a feud unit", ApproachMarkArms),
            ("unit-churn APPROACH-ROAD a road march or column wait far from the enemy is never skipped, and one near it is not marked either", ApproachRoadMarchNeverSkipped),
            ("unit-churn APPROACH-TABLE the marks are per alliance, rebuilt every pass, and a table the producer stopped maintaining marks nothing", ApproachTableMarksExpire),
            ("unit-churn APPROACH-STABLE the mark reads the ledger's committed task and the perceived enemy: a rewrite's task flicker or one tick of lost sight does not drop it, a real change does", ApproachMarkHoldsStill),
            ("unit-churn APPROACH-PERCEIVED the closest enemy is the closest one the side perceives, seen or remembered", ApproachPerceivedEnemy),
            ("unit-churn APPROACH-DEPLOY a column regiment marked for the approach deploys into line before the enemy is in rifle range", ApproachDeploysColumn),
            ("unit-churn APPROACH-WIRING the Prefix passes the live flag, mode and write guard to ApproachLine.Skips as whole statements, never throws and allocates nothing; the pulse producer builds the marks behind the approach flag alone", ApproachWiring),
        });
    }

    // ------------------------------------------------------------------ leaf brigades had no local combat response

    // The 2026-10-17 session: Union (side 0) rows of TacticalFacingPulse were 186 Division scope and none at Brigade or Regiment scope; Confederate (side 1)
    // had 317 Brigade and 105 Regiment rows. All eleven Union brigades were leaf brigades under a pool division. PostureIntentProducer.ProduceLeaf never set
    // the brigade's GroupPosture row, and FacingPulseIntentProducer and FireControlIntentProducer read only groups with Has set.
    private static void LeafBrigadeRecordsItsPosture()
    {
        GroupPosture reserve = LeafPosture.Record(DirectChildRole.Reserve, CommandTaskType.ReserveWait, CommandTaskType.ReserveWait, 708956, closeEngaged: false, flankRisk: true, BattleStance.Defender);
        AssertTrue(reserve.Has, "the row exists");
        AssertEqual(CommandEchelonKind.BrigadeLike, reserve.State.Echelon, "a brigade: the pulse's Brigade scope");
        AssertEqual(CommandNodeRole.Reserve, reserve.State.Role, "the leaf role as the ledger names it");
        AssertEqual(CommandTaskType.ReserveWait, reserve.State.Task, "the leaf's task");
        AssertTrue(reserve.FlankRisk && !reserve.CloseEngaged, "the contact flags the pulse reads");
        AssertFalse(reserve.Submitted, "no move yet");
        AssertFalse(reserve.FacingSubmitted, "nor a pulse");
        AssertEqual(CommandNodeRole.Defender, LeafPosture.Record(DirectChildRole.RefuseRight, CommandTaskType.GuardFlank, CommandTaskType.GuardFlank, 1, false, false, BattleStance.Defender).State.Role, "a refused flank defends on the defending side");
        foreach (BattleStance attacking in new[] { BattleStance.Attacker, BattleStance.Meeting })
            AssertEqual(CommandNodeRole.FlankMarch, LeafPosture.Record(DirectChildRole.RefuseRight, CommandTaskType.GuardFlank, CommandTaskType.GuardFlank, 1, false, false, attacking).State.Role,
                "and is a flank guard, never a Defender, on a side that is " + attacking);
        AssertEqual(CommandNodeRole.Unknown, LeafPosture.Record(DirectChildRole.Unknown, CommandTaskType.GuardFlank, CommandTaskType.GuardFlank, 1, false, false, BattleStance.Attacker).State.Role, "an unknown role stays unknown");
        // Record runs for every leaf brigade on every posture tick: the node id is built once per brigade, not concatenated per call.
        AssertEqual("leaf-708956", reserve.State.NodeId, "the node id names the brigade");
        AssertTrue(ReferenceEquals(LeafPosture.NodeId(708956), LeafPosture.NodeId(708956)), "and is the same string on every call");
        AssertTrue(ReferenceEquals(LeafPosture.Record(DirectChildRole.Reserve, CommandTaskType.ReserveWait, CommandTaskType.ReserveWait, 708956, false, false, BattleStance.Attacker).State.NodeId, LeafPosture.NodeId(708956)),
            "Record takes it from the cache");

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
        FlankResponseTests.AssertPinned(leaf, "LeafPosture.Record(leaf.LeafRole, task, leaf.LeafTask, id, closeEngaged, flankRisk, army.CurrentBattleStance);", "the effective task and the committed one (the leaf map's, before every rewrite), with the side's real battle stance, so an attacking side's refused flank is a flank guard");
        int record = leaf.IndexOf("t.Groups[i] = LeafPosture.Record(", StringComparison.Ordinal);
        int decide = leaf.IndexOf("LeafBrigadeMovePolicy.Decide(", StringComparison.Ordinal);
        int exit = leaf.IndexOf("if (!move.Move)", StringComparison.Ordinal);
        AssertTrue(record >= 0 && decide > record && exit > decide, "before the move decision, so a brigade that does not move (a reserve, a hold) is recorded too");
        FlankResponseTests.AssertPinned(leaf, "sink.Submit(PostureIntentPlan.LeafMove(id, task, x, z, slotHeading, inContactWithdrawal: flankRisk));\n            t.Groups[i].Submitted = true;", "and marked when the slot move was submitted, as the pool path marks it (the flank guard's own mark above it is not this one)");
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
        AssertEqual(ColumnDeployReason.FlankRisk, ColumnDeployment.Decide(Column(groupFlankRisk: true)), "its brigade's perceived flank risk alone is not a flank taken: a reason of its own");
        AssertEqual(ColumnDeployReason.Engaged, ColumnDeployment.Decide(Column(groupCloseEngaged: true)), "its brigade in close contact");
        AssertEqual(ColumnDeployReason.FiredOn, ColumnDeployment.Decide(Column(receivedFire: true, fireFromFlank: 1, groupCloseEngaged: true)), "fire is named before the flank and the contact");
        AssertEqual(ColumnDeployReason.Flanked, ColumnDeployment.Decide(Column(fireFromFlank: 1, groupCloseEngaged: true)), "the flank before the contact");
        AssertEqual(ColumnDeployReason.Engaged, ColumnDeployment.Decide(Column(groupFlankRisk: true, groupCloseEngaged: true)), "close contact before a perceived risk");
        AssertEqual(ColumnDeployReason.Flanked, ColumnDeployment.Decide(Column(groupFlankRisk: true, outflanked: 1)), "the regiment's own evidence names the flank, not the risk");
        AssertEqual(ColumnDeployReason.FlankRisk, ColumnDeployment.Decide(new ColumnDeployInput(true, false, 3, false, 0, 0, true, false, false, false, false, true)), "a perceived risk before the approach");

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

    // Vanilla's manual stop (StopRegiment 132498-132511) restores lastformation when allblocksoffbridgeorcrossing; the fake does the same, so the normal case
    // is the one the 2026-10-17 verify round found unhandled: the regiment is out of the column the moment the stop returns.
    private struct FakeDeployHost : IColumnDeployHost
    {
        public bool OffBridge;
        public int LastFormation;
        public int Live;
        public bool RotateAccepted;
        public int Paths;
        public float Walk = -1f;
        public float Final = -1f;
        public readonly System.Collections.Generic.List<string> Calls;

        public FakeDeployHost(bool offBridge, int lastFormation, bool rotateAccepted = true)
        {
            OffBridge = offBridge;
            LastFormation = lastFormation;
            Live = ColumnDeployment.RegimentMarchColumn;
            Paths = 1;
            RotateAccepted = rotateAccepted;
            Calls = new System.Collections.Generic.List<string>();
        }

        public bool OffBridgeOrCrossing => OffBridge;
        public int Formation => Live;
        public float WalkHeading => Walk;
        public float VanillaFinalRotation => Final;

        public void Stop()
        {
            Calls.Add("stop");
            Paths = 0;   // StopRegiment 132397: regimentpaths = 0
            if (OffBridge && Live != LastFormation) Live = LastFormation;
        }

        public void DeployInMarch(float heading)
        {
            Calls.Add("deploy-in-march:" + heading);
            Live = ColumnDeployment.RegimentLine;   // ChangeRegimentFormation keeps the path: RegimentSetPath re-targets its last corner (130791+)
        }

        public void ReformToLine(float heading)
        {
            Calls.Add("reform:" + heading);
            Live = ColumnDeployment.RegimentLine;
        }

        public bool Rotate(float heading)
        {
            Calls.Add("rotate:" + heading);
            return RotateAccepted;
        }
    }

    private static void ColumnDeployAppliesTheHeading()
    {
        // The normal case: the stop restores the line, and the threat heading is applied after it.
        var normal = new FakeDeployHost(offBridge: true, lastFormation: ColumnDeployment.RegimentLine);
        AssertEqual(ColumnDeployApplied.Faced, ColumnDeployment.Apply(ref normal, 135f, keepMarching: false, out _), "stopped, out of the column, turned to the threat");
        AssertEqual("stop,rotate:135", string.Join(",", normal.Calls), "the stop, then the rotation, and no forced re-form");
        AssertEqual(ColumnDeployment.RegimentLine, normal.Live, "in line");

        // The restored formation is the column itself (it was ordered column, not columned by vanilla's distance rule): re-form to line with the heading.
        var ordered = new FakeDeployHost(offBridge: true, lastFormation: ColumnDeployment.RegimentMarchColumn);
        AssertEqual(ColumnDeployApplied.Reformed, ColumnDeployment.Apply(ref ordered, 200f, keepMarching: false, out _), "re-formed to line facing the threat");
        AssertEqual("stop,reform:200", string.Join(",", ordered.Calls), "the stop, then the re-form, and no second rotation");

        // Vanilla's guard: a regiment on a bridge or a crossing is not touched (127669, 127676, 132498-132511).
        var bridge = new FakeDeployHost(offBridge: false, lastFormation: ColumnDeployment.RegimentLine);
        AssertEqual(ColumnDeployApplied.HeldOnBridge, ColumnDeployment.Apply(ref bridge, 135f, keepMarching: false, out _), "on a bridge: held");
        AssertEqual(0, bridge.Calls.Count, "nothing is written to a regiment on a bridge or crossing");
        AssertEqual(ColumnDeployment.RegimentMarchColumn, bridge.Live, "it keeps marching in column until it is off");

        var blind = new FakeDeployHost(offBridge: true, lastFormation: ColumnDeployment.RegimentLine);
        AssertEqual(ColumnDeployApplied.NoBearing, ColumnDeployment.Apply(ref blind, -1f, keepMarching: false, out _), "no threat bearing");
        AssertEqual("stop", string.Join(",", blind.Calls), "the stop alone: the regiment keeps its own facing");

        var refused = new FakeDeployHost(offBridge: true, lastFormation: ColumnDeployment.RegimentLine, rotateAccepted: false);
        AssertEqual(ColumnDeployApplied.KeptFacing, ColumnDeployment.Apply(ref refused, 135f, keepMarching: false, out _), "the rotation was refused or not needed");
        AssertEqual("stop,rotate:135", string.Join(",", refused.Calls), "it was asked after the stop");
    }

    // The approach in line must go on advancing after its deploy. A stop clears the regiment's path (StopRegiment 132397, regimentpaths = 0) and a
    // ChangeRegimentFormation with a manual line and no path does not restore it (RegimentSetPath returns at "target default, modifylastwaypoint, no
    // path", 130791+), so a stopped regiment stands until its group is re-laid; and nothing re-lays the group while it walks: the executor treats the same
    // target as a duplicate while the group has paths and moves (OrderExecutor.IsDuplicate -> CommandWaypointWritePolicy.ShouldSkipDuplicateWaypoint).
    private static void ApproachDeployKeepsMarching()
    {
        var march = new FakeDeployHost(offBridge: true, lastFormation: ColumnDeployment.RegimentLine);
        AssertEqual(1, march.Paths, "the column regiment is marching");
        AssertEqual(ColumnDeployApplied.DeployedInMarch, ColumnDeployment.Apply(ref march, 135f, keepMarching: true, out _), "deployed where it stands");
        AssertEqual("deploy-in-march:135", string.Join(",", march.Calls), "vanilla's own deploy in march, with no stop");
        AssertEqual(ColumnDeployment.RegimentLine, march.Live, "in line");
        AssertEqual(1, march.Paths, "with its path: the advance goes on in line");

        var halt = new FakeDeployHost(offBridge: true, lastFormation: ColumnDeployment.RegimentLine);
        ColumnDeployment.Apply(ref halt, 135f, keepMarching: false, out _);
        AssertEqual(0, halt.Paths, "a deploy under fire stops the regiment: that is its point (fight in place)");

        var bridge = new FakeDeployHost(offBridge: false, lastFormation: ColumnDeployment.RegimentLine);
        AssertEqual(ColumnDeployApplied.HeldOnBridge, ColumnDeployment.Apply(ref bridge, 135f, keepMarching: true, out _), "the bridge guard holds for the approach too");
        AssertEqual(0, bridge.Calls.Count, "and nothing is written to it");

        // The intents: only the approach keeps marching.
        BattleOrderIntent approach = ColumnDeployment.Intent(77, 135f, ColumnDeployReason.Approaching);
        AssertTrue((approach.Flags & IntentFlags.DeployInMarch) != 0 && (approach.Flags & IntentFlags.Deploy) != 0, "the approach deploy is flagged to keep marching");
        foreach (ColumnDeployReason r in new[] { ColumnDeployReason.FiredOn, ColumnDeployReason.Flanked, ColumnDeployReason.Engaged })
        {
            AssertTrue((ColumnDeployment.Intent(77, 135f, r).Flags & IntentFlags.DeployInMarch) == 0, r + " with a bearing stops and fights in place");
            AssertTrue((ColumnDeployment.Intent(77, -1f, r).Flags & IntentFlags.DeployInMarch) != 0, r + " with no bearing keeps marching: a stop would turn it to nothing");
        }
        AssertTrue((ColumnDeployment.Intent(77, 135f, ColumnDeployReason.FlankRisk).Flags & IntentFlags.DeployInMarch) != 0, "a perceived flank risk never stops a regiment");
        AssertEqual("column-deploy:FlankRisk", ColumnDeployment.Intent(77, 135f, ColumnDeployReason.FlankRisk).Reason, "and names itself");
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), null);
        AssertTrue(executor.Plan(ChurnSnapshot(regimentFormation: 3), approach, 1.0).Execute, "the executor plans the approach deploy for a column regiment");

        // Why not stop and re-issue: the group's own move is a duplicate while its other regiments walk, so a stopped regiment would be left behind.
        AssertTrue(CommandWaypointWritePolicy.ShouldSkipDuplicateWaypoint(500f, -500f, 505f, -495f, OrderArbiter.DedupeMeters, pathInterrupted: false,
            regimentPaths: 1, activeMove: true, currentX: 200f, currentZ: -200f), "the same target, the group walking: the re-issue is skipped");
    }

    // A line whose facing is more than 90 degrees off its path walks backwards. Smoke 2026-10-18: the 15th Indiana's deploy in march had targetFacing -1
    // (the column's facing kept) and the user watched it march backwards. The deploy faces the way the regiment walks unless the threat is close to that.
    private static void ApproachDeployFacesTheWay()
    {
        AssertEqual(200f, ColumnDeployment.MarchFacing(-1f, 200f, 10f), "no bearing: the way it walks, not the column's facing");
        AssertEqual(190f, ColumnDeployment.MarchFacing(190f, 200f, 10f), "a threat within 60 degrees of the walk: the threat");
        AssertEqual(200f, ColumnDeployment.MarchFacing(20f, 200f, 10f), "a threat behind it (180 off): the walk, never backwards");
        AssertEqual(200f, ColumnDeployment.MarchFacing(139f, 200f, 10f), "61 degrees off: the walk");
        AssertEqual(140f, ColumnDeployment.MarchFacing(140f, 200f, 10f), "exactly 60 degrees off (below): the threat");
        AssertEqual(260f, ColumnDeployment.MarchFacing(260f, 200f, 10f), "exactly 60 degrees off: the threat");
        AssertEqual(5f, ColumnDeployment.MarchFacing(5f, 350f, 10f), "the angle wraps at 360");
        AssertEqual(135f, ColumnDeployment.MarchFacing(135f, -1f, 10f), "no path: the threat");
        AssertEqual(10f, ColumnDeployment.MarchFacing(-1f, -1f, 10f), "neither: vanilla's own final rotation (127679)");
        AssertEqual(-1f, ColumnDeployment.MarchFacing(-1f, -1f, -1f), "nothing at all keeps the regiment's facing");

        var host = new FakeDeployHost(offBridge: true, lastFormation: ColumnDeployment.RegimentLine) { Walk = 200f, Final = 10f };
        ColumnDeployment.Apply(ref host, -1f, keepMarching: true, out float none);
        AssertEqual("deploy-in-march:200", string.Join(",", host.Calls), "the host is told the walking direction");
        AssertEqual(200f, none, "and the row reports it");
        var behind = new FakeDeployHost(offBridge: true, lastFormation: ColumnDeployment.RegimentLine) { Walk = 200f, Final = 10f };
        ColumnDeployment.Apply(ref behind, 20f, keepMarching: true, out float chosen);
        AssertEqual(200f, chosen, "a threat bearing behind the path does not turn it to walk backwards");
        var pathless = new FakeDeployHost(offBridge: true, lastFormation: ColumnDeployment.RegimentLine) { Final = 10f };
        ColumnDeployment.Apply(ref pathless, -1f, keepMarching: true, out float vanilla);
        AssertEqual(10f, vanilla, "no path and no bearing: vanilla's final rotation");
        var stop = new FakeDeployHost(offBridge: true, lastFormation: ColumnDeployment.RegimentLine) { Walk = 200f };
        ColumnDeployment.Apply(ref stop, 20f, keepMarching: false, out float turned);
        AssertEqual(20f, turned, "a stopping deploy still faces the threat it stopped for");
    }

    // Vanilla looks again at a line regiment with a path over 60 m and no enemy inside 100 m and columns it (CheckFormationChange 127665-127667); only the
    // Prefix, which skips that look for a marked regiment, keeps an in-march deploy in line. An unmarked regiment deployed in march was columned within a
    // second and, after the 20 s cooldown, deployed again: a Line/Column cycle (smoke 2026-10-18: 9 of 38 in-march deploys re-columned within 3 s).
    private static void ApproachDeployNeedsTheMark()
    {
        foreach (ColumnDeployReason r in new[] { ColumnDeployReason.Approaching, ColumnDeployReason.FlankRisk })
        {
            AssertTrue(ColumnDeployment.MayDeploy(r, 135f, marked: true), r + " deploys in march for a marked regiment");
            AssertFalse(ColumnDeployment.MayDeploy(r, 135f, marked: false), r + " submits nothing for an unmarked one: vanilla's column stands, the regiment is not stopped");
        }
        AssertFalse(ColumnDeployment.MayDeploy(ColumnDeployReason.FiredOn, -1f, marked: false), "a deploy with no bearing keeps marching, so it needs the mark too");
        AssertTrue(ColumnDeployment.MayDeploy(ColumnDeployReason.FiredOn, -1f, marked: true), "and holds for a marked regiment");
        foreach (ColumnDeployReason r in new[] { ColumnDeployReason.FiredOn, ColumnDeployReason.Flanked, ColumnDeployReason.Engaged })
            AssertTrue(ColumnDeployment.MayDeploy(r, 135f, marked: false), r + " with a bearing stops the regiment and fights in place: no mark needed, there is no path to re-column");
        // The rule and the intent agree: whatever MayDeploy lets through unmarked is a stop.
        foreach (ColumnDeployReason r in new[] { ColumnDeployReason.FiredOn, ColumnDeployReason.Flanked, ColumnDeployReason.Engaged, ColumnDeployReason.FlankRisk, ColumnDeployReason.Approaching })
            foreach (float heading in new[] { -1f, 90f })
                if (ColumnDeployment.MayDeploy(r, heading, marked: false))
                    AssertTrue((ColumnDeployment.Intent(77, heading, r).Flags & IntentFlags.DeployInMarch) == 0, r + " " + heading + ": an unmarked deploy is never in march");
    }

    // The table is rebuilt every pass (Begin clears the alliance), so a regiment is unmarked at the next pass if it fails any arm or the pass does not look at
    // it (detached, taken over by the player, feuding, mounted, dead, inactive). Nothing outlasts the pass, so there is no release to forget.
    private static void ApproachPassMarksOnlyWhatItSees()
    {
        UnitFlags live = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry;
        var table = new ApproachLineTable();
        double now = 50.0;
        foreach (UnitFlags bad in new[] { UnitFlags.PermanentlyDetached, UnitFlags.PlayerOwned, UnitFlags.Feud, UnitFlags.Mounted, UnitFlags.IsGroup, UnitFlags.Routed, UnitFlags.OnRetreat })
        {
            now += 0.001;
            table.Begin(0, now);
            if (ApproachLine.Marks(Mark())) table.Mark(0, 77);
            AssertTrue(table.Contains(0, 77, now), "marked");
            now += 0.001;
            table.Begin(0, now);
            if (ApproachLine.Marks(Mark(flags: live | bad))) table.Mark(0, 77);
            AssertFalse(table.Contains(0, 77, now), bad + " is unmarked at the next pass, with no hold");
        }
        foreach (UnitFlags missing in new[] { UnitFlags.Alive, UnitFlags.Active, UnitFlags.IsInfantry })
        {
            now += 0.001;
            table.Begin(0, now);
            if (ApproachLine.Marks(Mark(flags: live & ~missing))) table.Mark(0, 77);
            AssertFalse(table.Contains(0, 77, now), "a unit without " + missing + " is not marked");
        }
    }

    // The opportunity override turns a committed Main, SupportMain or Fix to FallBackToLine (TacticalOpportunityTaskOverride 41-57) after the ledger's task is
    // captured, and the withdrawal producer is not the only mover of a falling back group. The approval covers an advance or a hold: a group falling back, by
    // its ledger task or by its effective one, is withdrawing whoever sent it.
    private static void ApproachFallBackOverrideIsWithdrawing()
    {
        UnitRecord[] own = Chain(3);
        bool Withdrawing(CommandTaskType ledger, CommandTaskType effective)
        {
            WalkHost host = Walker(3);
            host.Has[1] = true;
            host.Task[1] = ledger;
            host.Effective = new CommandTaskType[3];
            host.Effective[1] = effective;
            ApproachLine.Walk(own, own.Length, 0, ref host, out _, out bool withdrawing);
            return withdrawing;
        }
        AssertTrue(Withdrawing(CommandTaskType.FixEnemy, CommandTaskType.FallBackToLine), "committed Fix, turned to FallBackToLine by the opportunity override");
        AssertTrue(Withdrawing(CommandTaskType.AttackObjective, CommandTaskType.Delay), "committed attack, effective Delay");
        AssertTrue(Withdrawing(CommandTaskType.FallBackToLine, CommandTaskType.AttackObjective), "committed fall back, effective attack");
        AssertTrue(Withdrawing(CommandTaskType.Delay, CommandTaskType.Delay), "both");
        AssertFalse(Withdrawing(CommandTaskType.FixEnemy, CommandTaskType.Screen), "a rewrite to Screen is no withdrawal");
        AssertFalse(Withdrawing(CommandTaskType.FixEnemy, CommandTaskType.FixEnemy), "neither");

        // The leaf row: the committed task is the leaf map's, taken before the override; the effective one is the rewritten.
        GroupPosture leaf = LeafPosture.Record(DirectChildRole.Fix, CommandTaskType.FallBackToLine, CommandTaskType.FixEnemy, 927726, false, false, BattleStance.Attacker);
        AssertEqual(CommandTaskType.FixEnemy, leaf.LedgerTask, "the leaf's ledger task is the committed one");
        AssertEqual(CommandTaskType.FallBackToLine, leaf.State.Task, "the effective one is what the override made of it");
        WalkHost fromRow = Walker(3);
        fromRow.Has[1] = true;
        fromRow.Task[1] = leaf.LedgerTask;
        fromRow.Effective = new CommandTaskType[3];
        fromRow.Effective[1] = leaf.State.Task;
        ApproachLine.Walk(own, own.Length, 0, ref fromRow, out _, out bool leafWithdrawing);
        AssertTrue(leafWithdrawing, "so a leaf brigade the override sent back is withdrawing");
        AssertFalse(ApproachLine.Marks(Mark(task: leaf.LedgerTask, withdrawing: leafWithdrawing)), "and its regiments are not marked");
    }

    private static void ApproachWithdrawalMoveWhateverTheSource()
    {
        const double window = 0.25;
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), null);
        BattleOrderIntent Move(int id, CommandTaskType task, IntentSource source) => new BattleOrderIntent
        {
            GroupId = id, Channel = OrderChannel.Move, Task = task, Source = source, TargetX = 100f, TargetZ = 100f, Heading = -1f, Priority = IntentPriority.Executor, Reason = "t",
        };
        executor.RecordIssued(Move(1, CommandTaskType.FallBackToLine, IntentSource.PostureExecutor), 10.0, null, 0, null, 0, 0, 0);
        executor.RecordIssued(Move(2, CommandTaskType.Delay, IntentSource.PostureExecutor), 10.0, null, 0, null, 0, 0, 0);
        executor.RecordIssued(Move(3, CommandTaskType.AttackObjective, IntentSource.PostureExecutor), 10.0, null, 0, null, 0, 0, 0);
        executor.RecordIssued(Move(4, CommandTaskType.None, IntentSource.Withdrawal), 10.0, null, 0, null, 0, 0, 0);
        AssertTrue(executor.WithdrewRecently(1, 10.1, window), "a FallBackToLine move from the posture executor");
        AssertTrue(executor.WithdrewRecently(2, 10.1, window), "a Delay move");
        AssertFalse(executor.WithdrewRecently(3, 10.1, window), "an attack move");
        AssertTrue(executor.WithdrewRecently(4, 10.1, window), "the withdrawal producer's move, as before");
        AssertFalse(executor.WithdrewRecently(1, 10.0 + window, window), "past the window");
        AssertFalse(executor.WithdrewRecently(1, 9.0, window), "a stamp from the future (a reload)");
        AssertFalse(executor.WithdrewRecently(99, 10.1, window), "a unit that was never moved");
        AssertFalse(new OrderExecutor().WithdrewRecently(1, 10.1, window), "a fresh executor");
    }

    private static void ApproachWalkHeadingMinimum()
    {
        AssertEqual(20f, ColumnDeployment.WalkHeadingMinMeters, "a block's length");
        AssertEqual(-1f, ColumnDeployment.WalkHeadingTo(100f, 100f, 100f, 119f), "a path end 19 m away is no walking direction");
        AssertEqual(PostureSense.HeadingTo(100f, 100f, 100f, 121f), ColumnDeployment.WalkHeadingTo(100f, 100f, 100f, 121f), "21 m is");
        AssertEqual(PostureSense.HeadingTo(100f, 100f, 100f, 120f), ColumnDeployment.WalkHeadingTo(100f, 100f, 100f, 120f), "exactly 20 m is");
        AssertEqual(-1f, ColumnDeployment.WalkHeadingTo(100f, 100f, 112f, 115f), "the distance is the straight line, not an axis (19.2 m)");
        AssertEqual(PostureSense.HeadingTo(0f, 0f, -300f, -700f), ColumnDeployment.WalkHeadingTo(0f, 0f, -300f, -700f), "a long path end gives its bearing");
        AssertEqual(-1f, ColumnDeployment.WalkHeadingTo(5f, 5f, 5f, 5f), "no distance, no direction");
    }

    // The nearest group above a regiment is found up to MaxGroupHops ancestors away, and a withdrawal anywhere in that reach holds the approach back.
    private struct WalkHost : IGroupWalkHost
    {
        public bool[] Has;
        public CommandTaskType[] Task;
        public bool[] Recent;
        public CommandTaskType[] Effective;
        public bool HasPosture(int index) => Has[index];
        public CommandTaskType TaskOf(int index) => Task[index];
        public CommandTaskType EffectiveTaskOf(int index) => Effective != null ? Effective[index] : Task[index];
        public bool WithdrawalRecent(int index) => Recent[index];
    }

    private static UnitRecord[] Chain(int length)
    {
        var own = new UnitRecord[length];
        for (int i = 0; i < length; i++) own[i] = new UnitRecord { InstanceId = 100 + i, ParentIndex = i + 1 < length ? i + 1 : -1 };
        return own;   // own[0] the regiment, own[1] its brigade, own[2] its division, own[3] the corps, own[4] the army, own[5] above it
    }

    private static WalkHost Walker(int length)
    {
        return new WalkHost { Has = new bool[length], Task = new CommandTaskType[length], Recent = new bool[length] };
    }

    private static void ApproachWalkReachesTheGroup()
    {
        AssertEqual(4, ApproachLine.MaxGroupHops, "four ancestors: brigade, division, corps, army");
        UnitRecord[] own = Chain(7);
        for (int up = 1; up <= 6; up++)
        {
            WalkHost host = Walker(7);
            host.Has[up] = true;
            ApproachLine.Walk(own, own.Length, 0, ref host, out int group, out _);
            if (up <= ApproachLine.MaxGroupHops) AssertEqual(up, group, "the group " + up + " ancestor(s) up is the one the regiment follows");
            else AssertEqual(-1, group, "the group " + up + " ancestors up is out of reach");
        }
        WalkHost two = Walker(7);
        two.Has[2] = true;
        ApproachLine.Walk(own, own.Length, 0, ref two, out int g2, out _);
        AssertEqual(2, g2, "a target group two hops up with no row on the brigade between");
        WalkHost three = Walker(7);
        three.Has[3] = true;
        ApproachLine.Walk(own, own.Length, 0, ref three, out int g3, out _);
        AssertEqual(3, g3, "three hops up");

        WalkHost nearest = Walker(7);
        nearest.Has[1] = nearest.Has[3] = true;
        ApproachLine.Walk(own, own.Length, 0, ref nearest, out int near, out _);
        AssertEqual(1, near, "the nearest wins, not the farthest");

        WalkHost none = Walker(7);
        ApproachLine.Walk(own, own.Length, 0, ref none, out int missing, out bool w0);
        AssertEqual(-1, missing, "no row anywhere: no group");
        AssertFalse(w0, "and nothing withdrawing");

        UnitRecord[] broken = Chain(3);
        broken[0].ParentIndex = 99;
        WalkHost host99 = Walker(3);
        ApproachLine.Walk(broken, broken.Length, 0, ref host99, out int outside, out _);
        AssertEqual(-1, outside, "a parent outside the roster ends the walk");

        // Withdrawal anywhere in the reach.
        WalkHost task = Walker(7);
        task.Has[3] = true;
        task.Task[3] = CommandTaskType.FallBackToLine;
        ApproachLine.Walk(own, own.Length, 0, ref task, out int taskGroup, out bool byTask);
        AssertTrue(byTask && taskGroup == 3, "a fall back recorded three hops up withdraws the regiment");
        WalkHost attack = Walker(7);
        attack.Has[3] = true;
        attack.Task[3] = CommandTaskType.AttackObjective;
        ApproachLine.Walk(own, own.Length, 0, ref attack, out _, out bool byAttack);
        AssertFalse(byAttack, "an attack does not");
        WalkHost recent = Walker(7);
        recent.Recent[3] = true;
        ApproachLine.Walk(own, own.Length, 0, ref recent, out _, out bool byRecent);
        AssertTrue(byRecent, "a recent withdrawal move of an ancestor three hops up");
        WalkHost beyond = Walker(7);
        beyond.Recent[5] = true;
        ApproachLine.Walk(own, own.Length, 0, ref beyond, out _, out bool byBeyond);
        AssertFalse(byBeyond, "one beyond the reach does not");
        WalkHost self = Walker(7);
        self.Recent[0] = true;
        ApproachLine.Walk(own, own.Length, 0, ref self, out _, out bool bySelf);
        AssertTrue(bySelf, "a recent withdrawal move of the regiment itself");
    }

    private static void ColumnDeployPlan()
    {
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), null);
        BattleOrderIntent deploy = ColumnDeployment.Intent(77, 135f, ColumnDeployReason.FiredOn);
        AssertEqual(OrderChannel.Formation, deploy.Channel, "a formation write");
        AssertEqual(ColumnDeployment.RegimentLine, deploy.Value, "to line");
        AssertTrue((deploy.Flags & IntentFlags.Deploy) != 0, "flagged Deploy: the executor stops the march first");
        AssertEqual(135f, deploy.Heading, "facing the threat");
        AssertEqual(IntentSource.FacingPulse, deploy.Source, "the pulse's reaction");
        AssertEqual(IntentPriority.Executor, deploy.Priority, "past the pulse's scorer rung, short of Safety (a withdrawal outranks it)");
        AssertEqual(-1f, ColumnDeployment.Intent(77, -5f, ColumnDeployReason.FiredOn).Heading, "no threat bearing: no facing");
        AssertEqual("column-deploy:FiredOn", deploy.Reason, "the intent names why, so the executor's row can");
        AssertEqual("column-deploy:Approaching", ColumnDeployment.Intent(77, 1f, ColumnDeployReason.Approaching).Reason, "the approach has its own reason");

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
        string deploy = Code(FlankResponseTests.Between(runtime, "private static bool IssueDeploy(", "// The deploy's calls on vanilla, for ColumnDeployment.Apply."));
        FlankResponseTests.AssertPinned(deploy, "if (unit.unittyp != TacticalUnitType.Infantry || unit.isrouted || unit.markedforrout || unit.mounted != 0) return false;", "infantry only, never a routed or mounted regiment");
        FlankResponseTests.AssertPinned(deploy, "if (unit.formation != ColumnDeployment.RegimentMarchColumn) return false;", "the live column test comes first");
        FlankResponseTests.AssertPinned(deploy, "ColumnDeployApplied applied = ColumnDeployment.Apply(ref host, w.Heading, (w.Flags & IntentFlags.DeployInMarch) != 0, out float facing);", "the one pure sequence: the bridge guard, the stop, then the heading");
        FlankResponseTests.AssertPinned(deploy, "EmitColumnDeploy(view, targetIndex, w, applied, facing);", "the row reports what was applied");
        FlankResponseTests.AssertPinned(deploy, "return applied != ColumnDeployApplied.HeldOnBridge;", "and a regiment held on a bridge counts as no write");
        AssertFalse(deploy.Contains("ChangeRegimentFormation"), "no forced re-form outside the host: the stop restores the line");
        string host = Code(FlankResponseTests.Between(runtime, "private readonly struct DeployHost", "private static readonly Dictionary<string, float> _lastColumnDeployRowAt"));
        FlankResponseTests.AssertPinned(host, "public bool OffBridgeOrCrossing => _unit.allblocksoffbridgeorcrossing;", "vanilla's own guard flag");
        FlankResponseTests.AssertPinned(host, "public int Formation => _unit.formation;", "the live formation after the stop");
        FlankResponseTests.AssertPinned(host, "_bunits.PerformActionForGroupForAllUnitsOnLevel(_go, -1, \"StopRegiment\", ManualStop,", "vanilla's manual stop");
        FlankResponseTests.AssertPinned(host, "_unit.ChangeRegimentFormation(GameVars.SetFormationParam(ColumnDeployment.RegimentLine, manualset: true, -1, heading));", "the re-form for a restored column");
        FlankResponseTests.AssertPinned(host, "public bool Rotate(float heading) => RotateInPlace(_unit, heading);", "the heading goes through the executor's facing path");
        FlankResponseTests.AssertPinned(host, "if (_unit.regimentpaths <= 0) return -1f;", "no path, no walking direction");
        FlankResponseTests.AssertPinned(host, "Vector3 end = _unit.GetLastTransmittedPathPos(ignoreorderdelay: true);", "the end of the regiment's own path");
        FlankResponseTests.AssertPinned(host, "return ColumnDeployment.WalkHeadingTo(here.x, here.z, end.x, end.z);", "the bearing to it, through the one pure function");
        FlankResponseTests.AssertPinned(host, "return rotation != null && rotation.Length > 0 && rotation[0] >= 0f ? rotation[0] : -1f;", "vanilla's final rotation (127679)");
        FlankResponseTests.AssertPinned(host, "Regiment lastOrderUnit = _unit.unitwhogavelastmovingorder;", "vanilla's deploy in march keeps the unit that gave the last moving order (127678)");
        FlankResponseTests.AssertPinned(host, "_unit.ChangeRegimentFormation(GameVars.SetFormationParam(ColumnDeployment.RegimentLine, manualset: true, -1, heading));\n                _unit.distancemarchcolumn = false;\n                _unit.unitwhogavelastmovingorder = lastOrderUnit;", "and ends the distance march column (127679-127681)");
        string row = Code(FlankResponseTests.Between(runtime, "private static void EmitColumnDeploy(", "private static bool IssueFormation("));
        FlankResponseTests.AssertPinned(row, ".WithField(\"applied\", appliedName)", "the row carries what was applied");
        FlankResponseTests.AssertPinned(row, ".WithField(\"facing\", facing)", "and the facing it was given");
        FlankResponseTests.AssertPinned(row, "bool marked = ApproachLineTable.Shared.Contains(view.Alliance, rec.InstanceId, view.DoctrineHours);", "whether the table held the regiment as it ran");
        FlankResponseTests.AssertPinned(row, ".WithField(\"marked\", marked)", "so an in-march deploy that vanilla will undo shows");
        FlankResponseTests.AssertPinned(row, "string appliedName = applied.ToString();", "named from the applied value");
        FlankResponseTests.AssertPinned(runtime, "BattleScope.Register(\"order-executor-column-deploy-rows\", () => _lastColumnDeployRowAt.Clear());", "the row throttle is battle-scoped");

        string pulse = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        string produce = FlankResponseTests.Between(pulse, "internal sealed class FacingPulseIntentProducer", "private static string FacingSourceOf(");
        FlankResponseTests.AssertPinned(produce, "bool deployOn = pulseOn && Plugin.EnableColumnDeployment != null && Plugin.EnableColumnDeployment.Value;", "behind its own rollback flag");
        FlankResponseTests.AssertPinned(produce, "ColumnDeployment.Decide(", "the pure decision");
        FlankResponseTests.AssertPinned(produce, "ColumnDeployment.Intent(rec.InstanceId, heading, reason)", "builds the one intent, with its reason");
        FlankResponseTests.AssertPinned(produce, "ApproachLine.Walk(own, s.OwnCount, u, ref walk, out int group, out bool walkWithdrawing);", "and asks the ancestor walk whether the regiment is withdrawing");
        PinStatement(Code(FlankResponseTests.SourceText("Tactical", "Battle", "ApproachLine.cs")), "if (ColumnDeployment.WithdrawalTask(host.TaskOf(a)) || ColumnDeployment.WithdrawalTask(host.EffectiveTaskOf(a))) withdrawing = true;", "and leaves a withdrawing ancestor's regiments alone, by the ledger's task or the effective one");
        FlankResponseTests.AssertPinned(FlankResponseTests.SourceText("Tactical", "Battle", "OrderExecutor.cs"), "last.Source == IntentSource.Withdrawal", "as well as the withdrawal producer's (the executor's WithdrewRecently)");
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

    // ------------------------------------------------------------------ the approach in line (user-approved Prefix-skip, 2026-10-17)

    // Vanilla CheckFormationChange (127625-127682), called from the regular cycle (112277) and on a manual stop (132370), puts a regiment with a path longer
    // than mindistanceformarchcolumn (60 m) and no enemy within 100 m into march column (127662-127668). A regiment Whiskey advances in line therefore
    // marched in column again a moment after every deployment. The Prefix skips that method for the regiments the approach table marks.
    private static bool SkipBase(bool flagOn = true, bool active = true, bool canWrite = true, int unitTyp = TacticalUnitType.Infantry, int formation = 0, bool marked = true)
        => ApproachLine.Skips(flagOn, active, canWrite, unitTyp, formation, marked);

    private static void ApproachSkipArms()
    {
        AssertTrue(SkipBase(), "all arms met: vanilla's re-column is skipped");
        AssertFalse(SkipBase(flagOn: false), "Enable Approach Line Advance off: vanilla runs unchanged");
        AssertFalse(SkipBase(active: false), "Commander Mode not Active: vanilla runs");
        AssertFalse(SkipBase(canWrite: false), "TacticalWriteGuard.CanWrite refuses (a player unit, a feud group, an exposed descendant): vanilla runs");
        AssertFalse(SkipBase(unitTyp: TacticalUnitType.Cavalry), "cavalry: vanilla runs");
        AssertFalse(SkipBase(unitTyp: TacticalUnitType.Artillery), "artillery: vanilla runs");
        AssertFalse(SkipBase(unitTyp: TacticalUnitType.BattleGroupBrigade), "a group object: vanilla runs");
        AssertFalse(SkipBase(formation: ColumnDeployment.RegimentMarchColumn), "a regiment already in column: vanilla runs, its deploy near an enemy (127677) is its own");
        AssertFalse(SkipBase(marked: false), "an unmarked regiment: vanilla runs");
        AssertTrue(SkipBase(formation: 2), "any formation but the column is kept");
    }

    private static ApproachLineInput Mark(UnitFlags flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry, CommandTaskType task = CommandTaskType.AttackObjective,
        bool routeColumn = false, int groupFormation = 0, bool withdrawing = false, bool hasEnemy = true, float distance = 300f, bool groupRecorded = true)
        => new ApproachLineInput(flags, task, routeColumn, groupFormation, withdrawing, hasEnemy, distance, groupRecorded);

    private static void ApproachMarkArms()
    {
        AssertTrue(ApproachLine.Marks(Mark()), "an infantry regiment of a brigade advancing with the enemy at 300 m");
        AssertTrue(ApproachLine.Marks(Mark(distance: ApproachLine.AssaultRangeMeters)), "at the assault range itself");
        AssertFalse(ApproachLine.Marks(Mark(distance: ApproachLine.AssaultRangeMeters + 1f)), "a metre beyond it");
        AssertFalse(ApproachLine.Marks(Mark(hasEnemy: false)), "no perceived enemy");
        AssertFalse(ApproachLine.Marks(Mark(groupRecorded: false)), "no Whiskey intent recorded for the group");
        AssertFalse(ApproachLine.Marks(Mark(flags: UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry | UnitFlags.PlayerOwned)), "a player-owned regiment");
        AssertFalse(ApproachLine.Marks(Mark(flags: UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry | UnitFlags.Feud)), "a feud regiment");
        AssertFalse(ApproachLine.Marks(Mark(flags: UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry | UnitFlags.PermanentlyDetached)), "a detached regiment");
        AssertFalse(ApproachLine.Marks(Mark(flags: UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry | UnitFlags.Routed)), "a routed regiment");
        AssertFalse(ApproachLine.Marks(Mark(flags: UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry | UnitFlags.OnRetreat)), "a retreating regiment");
        AssertFalse(ApproachLine.Marks(Mark(flags: UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsCavalry)), "cavalry");
        AssertFalse(ApproachLine.Marks(Mark(flags: UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry | UnitFlags.IsGroup)), "a group");
        AssertFalse(ApproachLine.Marks(Mark(flags: UnitFlags.Alive | UnitFlags.IsInfantry)), "an inactive regiment");
        AssertFalse(ApproachLine.Marks(Mark(withdrawing: true)), "a withdrawing group's regiment");

        foreach (CommandTaskType keeps in new[] { CommandTaskType.AttackObjective, CommandTaskType.SupportAttack, CommandTaskType.FixEnemy, CommandTaskType.HoldObjective,
            CommandTaskType.HoldChoke, CommandTaskType.GuardFlank, CommandTaskType.Consolidate, CommandTaskType.ReserveWait, CommandTaskType.Counterstroke })
            AssertTrue(ApproachLine.Marks(Mark(task: keeps)), "a line task: " + keeps);
        foreach (CommandTaskType other in new[] { CommandTaskType.None, CommandTaskType.AdvanceToAssembly, CommandTaskType.FormUp, CommandTaskType.FallBackToLine,
            CommandTaskType.Delay, CommandTaskType.Screen, CommandTaskType.Probe, CommandTaskType.Scout, CommandTaskType.ReleaseReserve })
            AssertFalse(ApproachLine.Marks(Mark(task: other)), "not a line advance or hold: " + other);
    }

    private static void ApproachRoadMarchNeverSkipped()
    {
        // The BC10 road march: the posture pipeline put the group in march column (group formation 4) for a route, or is waiting for the column.
        AssertFalse(ApproachLine.Marks(Mark(routeColumn: true)), "a road route or a column wait is not a line advance, whatever the enemy distance");
        AssertFalse(ApproachLine.Marks(Mark(groupFormation: RoadRoutePlanner.ColumnGroupFormation)), "nor a group already in the route's column");
        AssertFalse(ApproachLine.Marks(Mark(routeColumn: true, distance: 100f)), "even with the enemy close");
        AssertFalse(ApproachLine.Marks(Mark(distance: 2000f)), "a march far from the enemy is not marked");
        // Never marked, never skipped.
        AssertFalse(SkipBase(marked: ApproachLine.Marks(Mark(routeColumn: true))), "so vanilla's re-column runs on a road march");
        AssertFalse(SkipBase(marked: ApproachLine.Marks(Mark(distance: 2000f))), "and on a march far from the enemy");
        // A player's unit: the table never holds it, and the guard refuses it a second time.
        AssertFalse(SkipBase(marked: ApproachLine.Marks(Mark(flags: UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry | UnitFlags.PlayerOwned))), "a player unit is never skipped");
        AssertFalse(SkipBase(canWrite: false, marked: true), "even if it were marked");
    }

    private static void ApproachTableMarksExpire()
    {
        var table = new ApproachLineTable();
        table.Begin(0, 10.0);
        table.Mark(0, 77);
        AssertTrue(table.Contains(0, 77, 10.0), "marked");
        AssertFalse(table.Contains(1, 77, 10.0), "the other alliance has no mark");
        AssertFalse(table.Contains(0, 78, 10.0), "nor another regiment");
        AssertEqual(0.1, ApproachLineTable.FreshHours, "six real seconds at the smoke's speed, a minute at 1x: long enough for the arbiter's cadence, short of a stopped producer");
        AssertTrue(table.Contains(0, 77, 10.05), "fresh half a window after the pass");
        AssertTrue(table.Contains(0, 77, 10.099), "and just inside it");
        AssertFalse(table.Contains(0, 77, 10.2), "a table whose last pass is older than that was not maintained (the producer stopped)");
        AssertFalse(table.Contains(0, 77, 20.0), "and stays so");
        AssertFalse(table.Contains(0, 77, 9.0), "a stamp from the future (a reload) is no mark");
        table.Begin(0, 10.1);
        AssertFalse(table.Contains(0, 77, 10.1), "every pass rebuilds the alliance's table: a regiment the pass did not mark is not in it");
        table.Mark(0, 77);
        table.Begin(1, 10.1);
        table.Mark(1, 5);
        AssertTrue(table.Contains(0, 77, 10.1) && table.Contains(1, 5, 10.1), "each alliance keeps its own");
        table.Mark(5, 1);
        AssertFalse(table.Contains(5, 1, 10.1), "an alliance out of range is never marked");
        table.Begin(-1, 10.1);
        AssertFalse(table.Contains(-1, 1, 10.1), "nor read");
        table.Clear();
        AssertFalse(table.Contains(0, 77, 10.1), "Clear (battle end)");
    }

    // The 2026-10-18 smoke: the 4th Brigade's task row went FixEnemy, Screen, FixEnemy, Screen, Probe in seven seconds because the reconnaissance rule read one
    // tick of sight, and the mark, rebuilt from that task and from sight alone, dropped with each flicker: vanilla columned the regiments a second after their
    // approach deploy (9 of 38). No hold fixes that. The mark reads the ledger's committed task (GroupPosture.LedgerTask) and the perceived enemy (seen or
    // remembered), which change when something really changes.
    private static void ApproachMarkHoldsStill()
    {
        var table = new ApproachLineTable();
        double now = 100.0;
        // The posture row as the pipeline records it: LedgerTask the ledger's, State.Task what the per-tick rewrites made of it.
        GroupPosture Row(CommandTaskType ledger, CommandTaskType rewritten)
        {
            GroupPosture row = LeafPosture.Record(DirectChildRole.Fix, ledger, ledger, 927726, false, false, BattleStance.Attacker);
            row.State = row.State.WithRoleAndTask(row.State.Role, rewritten);
            return row;
        }
        bool pass(GroupPosture row, bool hasEnemy = true, float distance = 300f, bool withdrawing = false, bool routeColumn = false)
        {
            now += 0.005;
            table.Begin(0, now);
            if (ApproachLine.Marks(Mark(task: row.LedgerTask, hasEnemy: hasEnemy, distance: distance, withdrawing: withdrawing, routeColumn: routeColumn))) table.Mark(0, 77);
            return table.Contains(0, 77, now);
        }

        AssertTrue(pass(Row(CommandTaskType.FixEnemy, CommandTaskType.FixEnemy)), "marked on the committed FixEnemy");
        foreach (CommandTaskType rewritten in new[] { CommandTaskType.Screen, CommandTaskType.FixEnemy, CommandTaskType.Screen, CommandTaskType.Probe, CommandTaskType.FormUp })
            AssertTrue(pass(Row(CommandTaskType.FixEnemy, rewritten)), "the rewritten task " + rewritten + " is not the ledger's: the mark stands");
        // Sight: the enemy the side perceives does not vanish with a tick of lost sight.
        AssertTrue(pass(Row(CommandTaskType.FixEnemy, CommandTaskType.FixEnemy), hasEnemy: true, distance: 300f), "perceived (remembered) at 300 m while out of sight");

        // A real change ends it at the next pass.
        AssertFalse(pass(Row(CommandTaskType.FormUp, CommandTaskType.FormUp)), "the ledger committed the group to FormUp");
        AssertTrue(pass(Row(CommandTaskType.AttackObjective, CommandTaskType.AttackObjective)), "and an attack marks it again");
        AssertFalse(pass(Row(CommandTaskType.AttackObjective, CommandTaskType.AttackObjective), hasEnemy: false), "no enemy perceived at all");
        AssertFalse(pass(Row(CommandTaskType.AttackObjective, CommandTaskType.AttackObjective), distance: ApproachLine.AssaultRangeMeters + 1f), "the perceived enemy beyond the assault range");
        AssertTrue(pass(Row(CommandTaskType.HoldObjective, CommandTaskType.HoldObjective)), "a hold marks it");
        AssertFalse(pass(Row(CommandTaskType.HoldObjective, CommandTaskType.HoldObjective), withdrawing: true), "withdrawing");
        AssertTrue(pass(Row(CommandTaskType.HoldObjective, CommandTaskType.HoldObjective)), "marked again");
        AssertFalse(pass(Row(CommandTaskType.HoldObjective, CommandTaskType.HoldObjective), routeColumn: true), "a road route or a column wait");
        AssertEqual(CommandTaskType.FixEnemy, Row(CommandTaskType.FixEnemy, CommandTaskType.Probe).LedgerTask, "(the row keeps the ledger's task apart from the rewritten one)");
        AssertEqual(CommandTaskType.Probe, Row(CommandTaskType.FixEnemy, CommandTaskType.Probe).State.Task, "(and the rewritten one as it was)");
    }

    private static BattleSnapshot PerceivedSnapshot(ObservationState state, float enemyX, float enemyZ, bool withEnemy = true)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.SetSliceCount(20);
        s.AddOwn(new UnitRecord { InstanceId = 20, UnitTyp = TacticalUnitType.BattleGroupBrigade, X = 0f, Z = 0f, ParentIndex = -1, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup });
        if (withEnemy)
            s.AddObservation(new EnemyObservation { InstanceId = 900, UnitTyp = TacticalUnitType.Infantry, X = enemyX, Z = enemyZ, Strength = 400, State = state, Confidence01 = 0.9f, LastSeenHours = 0.9 });
        s.IndexCommandTree(null);
        return s;
    }

    private static void ApproachPerceivedEnemy()
    {
        BattleSnapshot visible = PerceivedSnapshot(ObservationState.Visible, 300f, 400f);
        int k = ApproachLine.ClosestPerceivedEnemy(visible, 0, out float d);
        AssertTrue(k >= 0, "a visible enemy");
        AssertNear(500f, d, 0.01f, "at 500 m (3-4-5)");
        BattleSnapshot remembered = PerceivedSnapshot(ObservationState.Remembered, 300f, 400f);
        int r = ApproachLine.ClosestPerceivedEnemy(remembered, 0, out float dr);
        AssertTrue(r >= 0, "the same enemy after it dropped out of sight is still perceived");
        AssertNear(500f, dr, 0.01f, "at the place it was seen");
        AssertEqual(-1, remembered.ClosestVisibleEnemy(0, out _), "while sight alone has lost it: the input the smoke's mark read");
        AssertTrue(ApproachLine.Marks(Mark(distance: dr)) , "so the mark stands through the tick");
        AssertEqual(-1, ApproachLine.ClosestPerceivedEnemy(PerceivedSnapshot(ObservationState.Visible, 0f, 0f, withEnemy: false), 0, out _), "no observation, no enemy");
        AssertEqual(-1, ApproachLine.ClosestPerceivedEnemy(visible, 5, out _), "an index outside the roster");
        AssertEqual(-1, ApproachLine.ClosestPerceivedEnemy(null, 0, out _), "no snapshot");

        // The repo's one fog rule (EnemySamples.Accepts): a memory below 0.5 confidence is not something the side believes in; nor is a reinforcement column
        // or a non-combat arm. Confidence is not a timer: it decays with time unseen (BattlePerception, half-life 20 minutes), 0.5 is about 20 minutes.
        BattleSnapshot Faint(float confidence, bool reinforcement = false, int unitTyp = TacticalUnitType.Infantry)
        {
            var f = new BattleSnapshot();
            f.BeginBuild(1, 0, 0, 1, 1.0);
            f.SetSliceCount(20);
            f.AddOwn(new UnitRecord { InstanceId = 20, UnitTyp = TacticalUnitType.BattleGroupBrigade, X = 0f, Z = 0f, ParentIndex = -1, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup });
            f.AddObservation(new EnemyObservation { InstanceId = 903, UnitTyp = unitTyp, X = 300f, Z = 0f, State = ObservationState.Remembered, Confidence01 = confidence, IsReinforcement = reinforcement });
            f.IndexCommandTree(null);
            return f;
        }
        AssertTrue(ApproachLine.ClosestPerceivedEnemy(Faint(0.5f), 0, out _) >= 0, "remembered at exactly the threshold is perceived");
        AssertTrue(ApproachLine.ClosestPerceivedEnemy(Faint(0.9f), 0, out _) >= 0, "and above it");
        AssertEqual(-1, ApproachLine.ClosestPerceivedEnemy(Faint(0.49f), 0, out _), "a memory just under it is not");
        AssertEqual(-1, ApproachLine.ClosestPerceivedEnemy(Faint(0.05f), 0, out _), "nor one at the floor perception keeps for 90 minutes");
        AssertEqual(-1, ApproachLine.ClosestPerceivedEnemy(Faint(0.9f, reinforcement: true), 0, out _), "nor a reinforcement column still off the field");
        AssertEqual(-1, ApproachLine.ClosestPerceivedEnemy(Faint(0.9f, unitTyp: TacticalUnitType.Skirmisher), 0, out _), "nor a skirmisher (the fog rule counts infantry, cavalry and artillery)");
        AssertEqual(EnemySamples.RememberedMinConfidence, 0.5f, "the threshold is the repo's, not this branch's");

        var two = new BattleSnapshot();
        two.BeginBuild(1, 0, 0, 1, 1.0);
        two.SetSliceCount(20);
        two.AddOwn(new UnitRecord { InstanceId = 20, UnitTyp = TacticalUnitType.BattleGroupBrigade, X = 0f, Z = 0f, ParentIndex = -1, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup });
        two.AddObservation(new EnemyObservation { InstanceId = 901, UnitTyp = TacticalUnitType.Infantry, X = 900f, Z = 0f, State = ObservationState.Visible, Confidence01 = 1f });
        two.AddObservation(new EnemyObservation { InstanceId = 902, UnitTyp = TacticalUnitType.Infantry, X = 0f, Z = -200f, State = ObservationState.Remembered, Confidence01 = 0.5f });
        two.IndexCommandTree(null);
        int near = ApproachLine.ClosestPerceivedEnemy(two, 0, out float dn);
        AssertEqual(902, two.Observations[near].InstanceId, "the nearer one wins, remembered or not");
        AssertNear(200f, dn, 0.01f, "at 200 m");
    }

    private static void ApproachDeploysColumn()
    {
        ColumnDeployInput column(bool approaching, int formation = 3, bool withdrawing = false) =>
            new ColumnDeployInput(true, false, formation, false, 0, 0, false, false, withdrawing, false, false, approaching);
        AssertEqual(ColumnDeployReason.Approaching, ColumnDeployment.Decide(column(true)), "a marked column regiment deploys at the assault range");
        AssertEqual(ColumnDeployReason.None, ColumnDeployment.Decide(column(false)), "an unmarked one marches on");
        AssertEqual(ColumnDeployReason.None, ColumnDeployment.Decide(column(true, formation: 0)), "a regiment already in line has nothing to deploy from");
        AssertEqual(ColumnDeployReason.None, ColumnDeployment.Decide(column(true, withdrawing: true)), "a withdrawing group keeps its column");
        AssertEqual(ColumnDeployReason.FiredOn, ColumnDeployment.Decide(new ColumnDeployInput(true, false, 3, true, 0, 0, false, false, false, false, false, true)), "fire is named first");
        AssertEqual(ColumnDeployReason.Engaged, ColumnDeployment.Decide(new ColumnDeployInput(true, false, 3, false, 0, 0, false, true, false, false, false, true)), "contact before the approach");
    }

    // The source without its comments, so a pinned statement cannot be satisfied by one that was commented out beside a mutated copy.
    internal static string Code(string text)
    {
        text = System.Text.RegularExpressions.Regex.Replace(text, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        return System.Text.RegularExpressions.Regex.Replace(text, @"//[^\n]*", "");
    }

    // A whole statement, present once: a mutation of any of its terms changes the text and fails this.
    internal static void PinStatement(string body, string statement, string label)
    {
        string flat = FlankResponseTests.Flat(body);
        string want = FlankResponseTests.Flat(statement);
        int first = flat.IndexOf(want, StringComparison.Ordinal);
        AssertTrue(first >= 0, label + ": " + statement);
        AssertEqual(-1, flat.IndexOf(want, first + want.Length, StringComparison.Ordinal), label + " (once): " + statement);
    }

    private static void ApproachWiring()
    {
        string patch = FlankResponseTests.SourceText("Patches", "ApproachLineAdvancePatch.cs");
        FlankResponseTests.AssertPinned(patch, "[HarmonyPatch(typeof(Regiment), \"CheckFormationChange\")]", "the vanilla method, by name (127625)");
        string prefix = Code(FlankResponseTests.Between(patch, "internal static bool Prefix(", "private static void Warn("));
        FlankResponseTests.AssertPinned(prefix, "TelemetryPerf.Scope(\"tactical.patch.approach-line-advance\"", "the diagnostic scope");
        FlankResponseTests.AssertPinned(prefix, "PatchFirstFire.Mark(\"tactical.patch.approach-line-advance\");", "the first-fire marker");
        // The live values, whole: the flag as the rollback reads it, the table, the mode, the write guard, and the one verdict.
        PinStatement(prefix, "bool flagOn = Plugin.EnableApproachLineAdvance != null && Plugin.EnableApproachLineAdvance.Value;", "the rollback flag, true when on");
        PinStatement(prefix, "if ((UnityEngine.Object)__instance == null) return true;", "a missing regiment runs vanilla");
        PinStatement(prefix, "int formation = __instance.formation;", "the live formation");
        PinStatement(prefix, "if (!ApproachLine.Eligible(flagOn, __instance.unittyp, formation)) return true;", "the cheap tests, with the live flag");
        PinStatement(prefix, "bool marked = ApproachLineTable.Shared.Contains(__instance.alliance, ((Component)__instance).gameObject.GetInstanceID(), TacticalDoctrineTime.NowBattleHours);", "the table, by alliance and instance, on the doctrine clock");
        PinStatement(prefix, "if (!marked) return true;", "an unmarked regiment runs vanilla");
        PinStatement(prefix, "bool skip = ApproachLine.Skips(flagOn, TacticalWriteGuard.ModeAllowsWrites(), TacticalWriteGuard.CanWrite(__instance), __instance.unittyp, formation, marked);", "the one pure decision, with the live mode and write guard");
        PinStatement(prefix, "return !skip;", "false (skip) only through it");
        AssertFalse(System.Text.RegularExpressions.Regex.IsMatch(prefix, @"Skips\(\s*(true|false)\b"), "no literal stands for a live input");
        FlankResponseTests.AssertPinned(prefix, "catch", "never throws");
        FlankResponseTests.AssertPinned(prefix, "Warn(ex);\n                    return true;", "and a failure runs vanilla");
        AssertTrue(!System.Text.RegularExpressions.Regex.IsMatch(prefix, @"\bnew\b|\$""|string\.Format|\.ToString\("), "allocation-free per call: no new, no interpolation, no format");
        AssertTrue(System.Text.RegularExpressions.Regex.Matches(patch, @"OnceLog\.\w+\(\s*""[a-z0-9:-]+""").Count >= 1, "static OnceLog keys");

        string producerFile = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        string producer = Code(producerFile);
        string pulse = Code(FlankResponseTests.Between(producerFile, "internal sealed class FacingPulseIntentProducer", "private static readonly double DeployCooldownHours"));
        // The table does not hang on the column deployment flag or on the pulse: its own flag, rebuilt before the pulse's early return.
        PinStatement(pulse, "bool approachOn = Plugin.EnableApproachLineAdvance != null && Plugin.EnableApproachLineAdvance.Value;", "its own flag");
        PinStatement(pulse, "bool deployOn = pulseOn && Plugin.EnableColumnDeployment != null && Plugin.EnableColumnDeployment.Value;", "the column deployment keeps its own flag");
        PinStatement(pulse, "if (!pulseOn && !approachOn) return;", "off only when both are off");
        PinStatement(pulse, "if (deployOn || approachOn) SubmitColumnDeployments(s, t, executor, sink, deployOn, approachOn);", "the pass runs for either");
        PinStatement(pulse, "if (!pulseOn) return;", "and the pulse stops after it");
        AssertTrue(pulse.IndexOf("SubmitColumnDeployments(s, t, executor, sink, deployOn, approachOn);", StringComparison.Ordinal) <
            pulse.IndexOf("if (!pulseOn) return;", StringComparison.Ordinal), "the table is rebuilt before the pulse's early return");

        string pass = Code(FlankResponseTests.Between(producerFile, "private static void SubmitColumnDeployments(", "// ApproachLine.Walk over the posture table and the executor's outcomes"));
        PinStatement(pass, "if (approachOn) ApproachLineTable.Shared.Begin(s.Alliance, s.DoctrineHours);", "the pass is stamped while the flag is on");
        AssertFalse(pass.Contains("if (!deployOn && !approachOn) continue;"), "no dead check: the caller runs the pass only for one of them");
        PinStatement(pass, "if ((f & UnitFlags.IsInfantry) == 0 || (f & UnitFlags.IsGroup) != 0) continue;", "a unit the pass does not look at: not infantry, or a group");
        PinStatement(pass, "if ((f & (UnitFlags.Alive | UnitFlags.Active)) != (UnitFlags.Alive | UnitFlags.Active)) continue;", "dead or inactive");
        PinStatement(pass, "if ((f & (UnitFlags.PlayerOwned | UnitFlags.PermanentlyDetached | UnitFlags.Feud | UnitFlags.Mounted)) != 0) continue;", "the player's, detached, feuding or mounted: never marked, and the table was cleared at the top of the pass");
        PinStatement(pass, "if (!ColumnDeployment.MayDeploy(reason, heading, approachOn && ApproachLineTable.Shared.Contains(s.Alliance, rec.InstanceId, s.DoctrineHours))) continue;", "an in-march deploy only for a marked regiment");
        PinStatement(pass, "var walk = new GroupWalkHost(t.Groups, own, executor, s.DoctrineHours);", "the walk over the posture table and the executor's outcomes");
        PinStatement(pass, "ApproachLine.Walk(own, s.OwnCount, u, ref walk, out int group, out bool walkWithdrawing);", "the one ancestor walk");
        PinStatement(pass, "bool withdrawing = (f & UnitFlags.OnRetreat) != 0 || walkWithdrawing;", "a retreating regiment is withdrawing, as is one the walk found withdrawing");
        PinStatement(pass, "bool recorded = group >= 0;", "a group was found");
        PinStatement(pass, "groupFormation = own[group].Formation;", "the group's formation (4 is the route's column)");
        PinStatement(pass, "routeColumn = t.Groups[group].RouteColumn;", "the group's route column is the pipeline's record");
        string walkHost = Code(FlankResponseTests.Between(producerFile, "internal readonly struct GroupWalkHost", "internal static bool WithdrawalRecent("));
        PinStatement(walkHost, "public bool HasPosture(int index) => _groups[index].Has;", "the posture row exists");
        PinStatement(walkHost, "public CommandTaskType TaskOf(int index) => _groups[index].LedgerTask;", "its committed task");
        PinStatement(walkHost, "public CommandTaskType EffectiveTaskOf(int index) => _groups[index].State.Task;", "and the one it carries out after the rewrites");
        PinStatement(Code(FlankResponseTests.Between(producerFile, "internal static bool WithdrawalRecent(", "private static bool")), "return executor != null && executor.WithdrewRecently(unitId, nowHours, WithdrawalWindowHours);", "a recent withdrawal, whoever issued it");
        PinStatement(walkHost, "public bool WithdrawalRecent(int index) => FacingPulseIntentProducer.WithdrawalRecent(_executor, _own[index].InstanceId, _nowHours);", "the executor's last move for that unit");
        string approachFile = Code(FlankResponseTests.SourceText("Tactical", "Battle", "ApproachLine.cs"));
        PinStatement(approachFile, "public const int MaxGroupHops = 4;", "the reach of the walk");
        PinStatement(approachFile, "for (int hops = 0; a >= 0 && a < ownCount && hops < MaxGroupHops; hops++, a = own[a].ParentIndex)", "the one hop cap");
        PinStatement(pass, "if (approachOn) { float enemyDistance = 0f;", "marked only behind the flag");
        PinStatement(pass, "int enemy = recorded ? ApproachLine.ClosestPerceivedEnemy(s, group, out enemyDistance) : -1;", "the enemy the side perceives, seen or remembered: not sight alone");
        PinStatement(pass, "approaching = ApproachLine.Marks(new ApproachLineInput(f, groupTask, routeColumn, groupFormation, withdrawing, enemy >= 0, enemyDistance, recorded));", "the pure marking rule, over the live facts");
        PinStatement(pass, "if (approaching) ApproachLineTable.Shared.Mark(s.Alliance, rec.InstanceId);", "and the table filled from it");
        PinStatement(pass, "groupTask = t.Groups[group].LedgerTask;", "the task the ledger committed, not the pipeline's per-tick rewrite");
        AssertFalse(pass.Contains("State.Task"), "the pass never reads the rewritten task");
        AssertFalse(pass.Contains("ClosestVisibleEnemy(group"), "nor sight alone for the group");
        PinStatement(producer, "if (recordTick) t.Groups[i].RouteColumn = roadRoute.Use || columnWait;", "recorded where the pipeline decides the route");
        FlankResponseTests.AssertPinned(producer, "state = state.WithRoleAndTask(order.Role, order.Task);\n            CommandTaskType ledgerTask = state.Task;", "the ledger's task is taken before every rewrite");
        PinStatement(producer, "g.LedgerTask = ledgerTask;", "and recorded on the row");
        PinStatement(producer, "followed.LedgerTask = ledgerTask;", "also for a follower");
        PinStatement(producer, "contestedGroup.LedgerTask = ledgerTask;", "and for a contested group");
        PinStatement(producer, "g.RouteColumn = false;", "cleared with the rest of the row each tick");
        string posture = FlankResponseTests.SourceText("Tactical", "Battle", "PostureRecords.cs");
        FlankResponseTests.AssertPinned(posture, "public bool RouteColumn;", "a field of the posture row");
        string plugin = FlankResponseTests.SourceText("Plugin.cs");
        FlankResponseTests.AssertPinned(plugin, "\"Enable Approach Line Advance\"", "the flag is declared");
        FlankResponseTests.AssertPinned(plugin, "Independent of Enable Column Deployment and of the contact", "and its description states the independence");

        // One constant for the assault range (BC16's AssaultRangeMeters): defined once, read by the marks and the approach deploy.
        int definitions = 0;
        string src = TestFixtures.FindRepoPath("src/WhiskeyRealism");
        var define = new System.Text.RegularExpressions.Regex(@"const\s+float\s+AssaultRangeMeters\s*=");
        foreach (string path in System.IO.Directory.GetFiles(src, "*.cs", System.IO.SearchOption.AllDirectories))
            if (define.IsMatch(System.IO.File.ReadAllText(path))) definitions++;
        AssertEqual(1, definitions, "AssaultRangeMeters is defined in one place");
        AssertEqual(500f, ApproachLine.AssaultRangeMeters, "BC16's 500 m");
    }
}
