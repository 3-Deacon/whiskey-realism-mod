using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// WS T4 (2026-10-14 remediation): #61 posture executor + single ai_stance owner.
internal static class PostureStanceFixTests
{
    public static void Run()
    {
        // B-stance3 / BR-M4 / BR-M5 — single stance owner
        Pass("stance owner refuses non-group sub-units", StanceOwnerRefusesNonGroupSubUnits);
        Pass("stance owner never writes none or assault", StanceOwnerNeverWritesNoneOrAssault);
        Pass("stance owner yields to vanilla retreat and inactive macro", StanceOwnerYieldsToVanillaRetreatAndInactiveMacro);
        Pass("stance owner dedups on ai_stanceordered", StanceOwnerDedupsOnOrderedStance);
        Pass("stance owner lower-priority opinion refused inside hold", StanceOwnerLowerPriorityRefusedInsideHold);
        Pass("stance owner executor overrides scorer inside hold", StanceOwnerExecutorOverridesScorerInsideHold);
        Pass("stance owner same-source change needs min dwell", StanceOwnerSameSourceChangeNeedsMinDwell);
        Pass("stance owner hold expires on battle time", StanceOwnerHoldExpiresOnBattleTime);
        Pass("stance owner clock going backwards expires hold", StanceOwnerClockBackwardsExpiresHold);
        Pass("stance owner reasserts after vanilla revert inside hold", StanceOwnerReassertsAfterRevertInsideHold);
        Pass("stance owner record update keeps highest source", StanceOwnerRecordUpdateKeepsHighestSource);
        Pass("vanilla hold stamp suppresses AdjustGroupAIStance until hold ends", VanillaHoldStampSuppressesAdjustGroupAIStance);
        Pass("task stance mapping matches vanilla stance semantics", TaskStanceMappingMatchesVanillaSemantics);
        Pass("group stance scorer uses the shared task mapping", GroupStanceScorerUsesSharedTaskMapping);

        // T-settle
        Pass("settlement gate does not treat a marching unit as settled", SettlementGateMarchingUnitIsNotSettled);
        Pass("settlement gate does not treat an active move as settled", SettlementGateActiveMoveIsNotSettled);

        // B-macro3
        Pass("posture executor stands down in vanilla retreat and end of day", PostureExecutorStandsDownInRetreatAndEndOfDay);
        Pass("posture executor skips the feud instance", PostureExecutorSkipsFeudInstance);

        // BR-H1
        Pass("defensive hold is no opinion for leaf and brigade tasks", DefensiveHoldIsNoOpinionForTasks);
        Pass("opportunity overrides keep withdrawal consolidate and attack-now rules", OpportunityOverridesKeepOtherRules);

        // B-divplay
        Pass("division play keeps ledger hold tasks", DivisionPlayKeepsLedgerHoldTasks);
        Pass("division play stands down for a defending side", DivisionPlayStandsDownForDefender);

        // R8
        Pass("formation policy never re-lays a group in combat", FormationPolicyNeverRelaysInCombat);
        Pass("formation policy changes formation the vanilla way out of combat", FormationPolicyUsesVanillaOutOfCombat);
        Pass("formation policy waits while moving or in cover", FormationPolicyWaitsWhileMovingOrInCover);
        Pass("formation policy cooldown is longer under fire", FormationPolicyCooldownLongerUnderFire);
        Pass("vanilla SetFormation index round-trips group formations", VanillaSetFormationIndexRoundTrips);

        // R9
        Pass("regiment facing pulse leaves artillery to vanilla", RegimentFacingPulseLeavesArtilleryToVanilla);
        Pass("regiment facing pulse requires a threat inside fire range", RegimentFacingPulseRequiresFireRange);
        Pass("regiment facing pulse never stops a moving or withdrawing unit", RegimentFacingPulseNeverStopsMovingUnit);
        Pass("regiment facing pulse with current formation only rotates", RegimentFacingPulseOnlyRotates);

        // B-61clock / M3 retry
        Pass("executor movement uses courier order delay like vanilla AI group moves", ExecutorMovementUsesCourierDelay);
        Pass("in-contact withdrawal skips order delay like CheckIfOutflanked", InContactWithdrawalSkipsOrderDelay);
        Pass("queued courier order counts as an issued move", QueuedCourierOrderCountsAsIssuedMove);
        Pass("failed write backs off on battle time", FailedWriteBacksOffOnBattleTime);
        Pass("real seconds at 1x convert to battle hours", RealSecondsAtOneXConvertToBattleHours);

        // BR-M1
        Pass("main arrival hold arms once and keeps its start time", MainArrivalHoldArmsOnce);
        Pass("main arrival hold stays released after expiry", MainArrivalHoldStaysReleasedAfterExpiry);

        // B-leaf
        Pass("leaf brigade moves only for movement tasks", LeafBrigadeMovesOnlyForMovementTasks);
        Pass("leaf brigade defers to an active parent order", LeafBrigadeDefersToActiveParentOrder);
        Pass("leaf brigade keeps executor safety gates", LeafBrigadeKeepsExecutorSafetyGates);
        Pass("formation slot mirrors vanilla GetAngleTerrainPosXY math", FormationSlotMirrorsVanillaMath);

        // B-61state / perf: probe cache keyed on roster, not frame
        Pass("roster signature changes with roster not with frame", RosterSignatureChangesWithRosterOnly);
    }

    private static void Pass(string name, Action test)
    {
        test();
        Console.WriteLine("PASS " + name);
    }

    private static StanceWriteInput Input(
        int target,
        int current = 2,
        bool isAiGroup = true,
        int macroAi = 1,
        float now = 1.0f,
        StanceHoldRecord record = default,
        StanceWriteSource source = StanceWriteSource.Scorer)
    {
        return new StanceWriteInput(isAiGroup, target, current, macroAi, now, record, source);
    }

    private static void StanceOwnerRefusesNonGroupSubUnits()
    {
        var d = TacticalStanceOwnership.Decide(Input(target: 3, isAiGroup: false));
        AssertEqual(StanceWriteAction.Refuse, d.Action, "sub-unit action");
        AssertEqual("not-ai-group", d.Reason, "sub-unit reason");
    }

    private static void StanceOwnerNeverWritesNoneOrAssault()
    {
        AssertEqual(StanceWriteAction.Refuse, TacticalStanceOwnership.Decide(Input(target: 0)).Action, "stance 0 is no opinion");
        AssertEqual(StanceWriteAction.Refuse, TacticalStanceOwnership.Decide(Input(target: 4)).Action, "assault stays vanilla's strength trigger");
        AssertEqual(StanceWriteAction.Refuse, TacticalStanceOwnership.Decide(Input(target: 7)).Action, "out of range");
    }

    private static void StanceOwnerYieldsToVanillaRetreatAndInactiveMacro()
    {
        var retreat = TacticalStanceOwnership.Decide(Input(target: 3, macroAi: 3));
        AssertEqual(StanceWriteAction.Refuse, retreat.Action, "macroai 3 action");
        AssertEqual("vanilla-retreat", retreat.Reason, "macroai 3 reason");
        var inactive = TacticalStanceOwnership.Decide(Input(target: 3, macroAi: -1));
        AssertEqual(StanceWriteAction.Refuse, inactive.Action, "macroai -1 action");
    }

    private static void StanceOwnerDedupsOnOrderedStance()
    {
        var d = TacticalStanceOwnership.Decide(Input(target: 2, current: 2));
        AssertEqual(StanceWriteAction.Claim, d.Action, "already ordered is a claim, not a ChangeStance");
        var w = TacticalStanceOwnership.Decide(Input(target: 3, current: 2));
        AssertEqual(StanceWriteAction.Write, w.Action, "differs from ordered → write");
    }

    private static void StanceOwnerLowerPriorityRefusedInsideHold()
    {
        var record = new StanceHoldRecord(3, 1.0f, StanceWriteSource.Executor);
        var d = TacticalStanceOwnership.Decide(Input(target: 2, current: 3, now: 1.05f, record: record, source: StanceWriteSource.Scorer));
        AssertEqual(StanceWriteAction.Refuse, d.Action, "#45 must not flip a stance #61 just set");
        AssertEqual("whiskey-hold", d.Reason, "reason");
    }

    private static void StanceOwnerExecutorOverridesScorerInsideHold()
    {
        var record = new StanceHoldRecord(3, 1.0f, StanceWriteSource.Scorer);
        var d = TacticalStanceOwnership.Decide(Input(target: 2, current: 3, now: 1.001f, record: record, source: StanceWriteSource.Executor));
        AssertEqual(StanceWriteAction.Write, d.Action, "executor outranks scorer");
        AssertEqual("priority-override", d.Reason, "reason");
    }

    private static void StanceOwnerSameSourceChangeNeedsMinDwell()
    {
        var record = new StanceHoldRecord(3, 1.0f, StanceWriteSource.Executor);
        var early = TacticalStanceOwnership.Decide(Input(target: 2, current: 3, now: 1.0f + TacticalStanceOwnership.MinSelfChangeHours * 0.5f, record: record, source: StanceWriteSource.Executor));
        AssertEqual(StanceWriteAction.Refuse, early.Action, "same-source flip inside min dwell");
        var later = TacticalStanceOwnership.Decide(Input(target: 2, current: 3, now: 1.0f + TacticalStanceOwnership.MinSelfChangeHours * 1.5f, record: record, source: StanceWriteSource.Executor));
        AssertEqual(StanceWriteAction.Write, later.Action, "same-source task change after min dwell");
    }

    private static void StanceOwnerHoldExpiresOnBattleTime()
    {
        var record = new StanceHoldRecord(3, 1.0f, StanceWriteSource.Executor);
        var d = TacticalStanceOwnership.Decide(Input(target: 2, current: 3, now: 1.0f + TacticalStanceOwnership.HoldHours + 0.01f, record: record, source: StanceWriteSource.Scorer));
        AssertEqual(StanceWriteAction.Write, d.Action, "hold elapsed in battle hours");
    }

    private static void StanceOwnerClockBackwardsExpiresHold()
    {
        var record = new StanceHoldRecord(3, 5.0f, StanceWriteSource.Executor);
        var d = TacticalStanceOwnership.Decide(Input(target: 2, current: 3, now: 0.5f, record: record, source: StanceWriteSource.Scorer));
        AssertEqual(StanceWriteAction.Write, d.Action, "reload / new battle invalidates the hold");
    }

    private static void StanceOwnerReassertsAfterRevertInsideHold()
    {
        var record = new StanceHoldRecord(3, 1.0f, StanceWriteSource.Scorer);
        var d = TacticalStanceOwnership.Decide(Input(target: 3, current: 2, now: 1.01f, record: record, source: StanceWriteSource.Scorer));
        AssertEqual(StanceWriteAction.Write, d.Action, "same opinion, stance changed underneath");
        AssertEqual("reassert", d.Reason, "reason");
    }

    private static void StanceOwnerRecordUpdateKeepsHighestSource()
    {
        var record = new StanceHoldRecord(3, 1.0f, StanceWriteSource.Executor);
        var input = Input(target: 3, current: 3, now: 1.02f, record: record, source: StanceWriteSource.Scorer);
        var d = TacticalStanceOwnership.Decide(input);
        AssertEqual(StanceWriteAction.Claim, d.Action, "agreeing opinion claims");
        var next = TacticalStanceOwnership.Next(d, input);
        AssertEqual(StanceWriteSource.Executor, next.Source, "scorer claim keeps executor ownership");
        AssertEqual(1.0f, next.WrittenAtHours, "lower-priority claim does not extend executor hold");

        var refused = TacticalStanceOwnership.Next(new StanceWriteDecision(StanceWriteAction.Refuse, "x"), input);
        AssertEqual(record.WrittenAtHours, refused.WrittenAtHours, "refuse leaves record");

        var write = TacticalStanceOwnership.Next(new StanceWriteDecision(StanceWriteAction.Write, "write"), Input(target: 2, now: 2f, source: StanceWriteSource.Scorer));
        AssertTrue(write.HasValue, "write creates record");
        AssertEqual(2, write.Stance, "write stance");
        AssertEqual(2f, write.WrittenAtHours, "write time");
    }

    private static void VanillaHoldStampSuppressesAdjustGroupAIStance()
    {
        // Vanilla 4264: re-evaluates when lastaistancechangetime < now + updatetimeforresettingofaistance.
        float reset = 1.0f; // Config/battleprefs.txt "Time (game hrs) to wait for resetting of new ai stances" = 1
        float stamp = TacticalStanceOwnership.VanillaHoldStamp(10f, TacticalStanceOwnership.HoldHours, reset);
        AssertFalse(TacticalStanceOwnership.VanillaWouldReevaluate(stamp, 10f, reset), "held at write time");
        AssertFalse(TacticalStanceOwnership.VanillaWouldReevaluate(stamp, 10f + TacticalStanceOwnership.HoldHours * 0.9f, reset), "held inside hold");
        AssertTrue(TacticalStanceOwnership.VanillaWouldReevaluate(stamp, 10f + TacticalStanceOwnership.HoldHours * 1.1f, reset), "vanilla resumes after hold");
        AssertTrue(TacticalStanceOwnership.VanillaWouldReevaluate(10f, 10f, reset), "vanilla's own stamp never holds (inverted compare)");
        float negativeReset = TacticalStanceOwnership.VanillaHoldStamp(10f, 0.25f, -5f);
        AssertEqual(10.25f, negativeReset, "negative reset clamps to 0");
    }

    private static void TaskStanceMappingMatchesVanillaSemantics()
    {
        // Screen/Probe/Scout → screen (1): vanilla screen places at 0.9 × weapon range with
        // movementmode 4 and no cavalry charges (5047, 5701).
        AssertEqual(1, TacticalCommandTaskToStanceMapper.Map(CommandTaskType.Screen), "Screen");
        AssertEqual(1, TacticalCommandTaskToStanceMapper.Map(CommandTaskType.Probe), "Probe");
        AssertEqual(1, TacticalCommandTaskToStanceMapper.Map(CommandTaskType.Scout), "Scout");
        // FixEnemy → attack (3): defend (2) disables the micro advance (5694), so a fixing force would never close.
        AssertEqual(3, TacticalCommandTaskToStanceMapper.Map(CommandTaskType.FixEnemy), "FixEnemy");
        for (int t = 0; t <= 20; t++)
        {
            if (!Enum.IsDefined(typeof(CommandTaskType), t)) continue;
            int s = TacticalCommandTaskToStanceMapper.Map((CommandTaskType)t);
            AssertTrue(s >= 0 && s <= 3, "mapper never yields assault for " + (CommandTaskType)t);
        }
    }

    private static void GroupStanceScorerUsesSharedTaskMapping()
    {
        AssertEqual(3, TacticalCommandTaskToStanceMapper.MapForGroupStanceScorer(CommandTaskType.AttackObjective, false), "attack");
        AssertEqual(3, TacticalCommandTaskToStanceMapper.MapForGroupStanceScorer(CommandTaskType.SupportAttack, false), "support");
        AssertEqual(TacticalCommandTaskToStanceMapper.Map(CommandTaskType.FixEnemy),
            TacticalCommandTaskToStanceMapper.MapForGroupStanceScorer(CommandTaskType.FixEnemy, false), "fix agrees with #61");
        AssertEqual(2, TacticalCommandTaskToStanceMapper.MapForGroupStanceScorer(CommandTaskType.HoldObjective, false), "hold");
        AssertEqual(3, TacticalCommandTaskToStanceMapper.MapForGroupStanceScorer(CommandTaskType.HoldObjective, true), "hold commits on visible weak point");
        AssertEqual(2, TacticalCommandTaskToStanceMapper.MapForGroupStanceScorer(CommandTaskType.HoldChoke, false), "hold choke");
        AssertEqual(2, TacticalCommandTaskToStanceMapper.MapForGroupStanceScorer(CommandTaskType.ReserveWait, false), "reserve");
        AssertEqual(2, TacticalCommandTaskToStanceMapper.MapForGroupStanceScorer(CommandTaskType.FallBackToLine, false), "fallback");
        AssertEqual(-1, TacticalCommandTaskToStanceMapper.MapForGroupStanceScorer(CommandTaskType.Screen, false), "screen is the executor's call");
        AssertEqual(-1, TacticalCommandTaskToStanceMapper.MapForGroupStanceScorer(CommandTaskType.None, true), "none");
    }

    private static void SettlementGateMarchingUnitIsNotSettled()
    {
        var input = new TacticalOrderSettlementGate.Input
        {
            OrderQueueCount = 0,
            OrderState = 0,
            RegimentPaths = 2,
            PathInterrupted = false,
            MovementMode = 0,
            ActiveMove = false
        };
        var d = TacticalOrderSettlementGate.Evaluate(input);
        AssertFalse(d.AllowChange, "a unit following a path is not settled");
        AssertEqual("marching", d.Reason, "reason");
    }

    private static void SettlementGateActiveMoveIsNotSettled()
    {
        var d = TacticalOrderSettlementGate.Evaluate(new TacticalOrderSettlementGate.Input
        {
            OrderQueueCount = 0,
            OrderState = 0,
            RegimentPaths = 0,
            PathInterrupted = false,
            MovementMode = 0,
            ActiveMove = true
        });
        AssertFalse(d.AllowChange, "active move order is not settled");
        AssertEqual("active-move", d.Reason, "reason");
    }

    private static void PostureExecutorStandsDownInRetreatAndEndOfDay()
    {
        AssertTrue(PostureExecutorBattleGate.Allows(5, 1, 0, 0, false, out _), "normal AI battle runs");
        AssertFalse(PostureExecutorBattleGate.Allows(5, 3, 0, 0, false, out string retreat), "macroai 3");
        AssertEqual("vanilla-retreat", retreat, "retreat reason");
        AssertFalse(PostureExecutorBattleGate.Allows(5, 1, 1, 0, false, out string eod), "eodcycle 1");
        AssertEqual("end-of-day", eod, "eod reason");
        AssertFalse(PostureExecutorBattleGate.Allows(5, 1, 2, 0, false, out _), "eodcycle 2");
        AssertFalse(PostureExecutorBattleGate.Allows(4, 1, 0, 0, false, out _), "state < 5");
        AssertFalse(PostureExecutorBattleGate.Allows(5, -1, 0, 0, false, out _), "macroai inactive");
    }

    private static void PostureExecutorSkipsFeudInstance()
    {
        AssertFalse(PostureExecutorBattleGate.Allows(5, 1, 0, 2, true, out string feud), "feud instance under W&L");
        AssertEqual("feud-instance", feud, "feud reason");
        AssertFalse(PostureExecutorBattleGate.Allows(5, 1, 0, 1, false, out _), "player instance outside ai_vs_ai");
        AssertTrue(PostureExecutorBattleGate.Allows(5, 1, 0, 1, true, out _), "player instance under ai_vs_ai (W&L) runs; per-unit guard protects");
    }

    private static void DefensiveHoldIsNoOpinionForTasks()
    {
        foreach (var task in new[] { CommandTaskType.AttackObjective, CommandTaskType.Probe, CommandTaskType.SupportAttack })
        {
            var result = TacticalOpportunityTaskOverride.Override(task, DirectChildRole.Main, ReinforcementOpportunity.DefensiveHold, out string reason);
            AssertEqual(task, result, "DefensiveHold keeps " + task);
            AssertTrue(reason == null, "no override reason for " + task);
        }
    }

    private static void OpportunityOverridesKeepOtherRules()
    {
        AssertEqual(CommandTaskType.FallBackToLine,
            TacticalOpportunityTaskOverride.Override(CommandTaskType.AttackObjective, DirectChildRole.Main, ReinforcementOpportunity.WithdrawalToFightLater, out _),
            "withdrawal pulls the main effort back");
        AssertEqual(CommandTaskType.Screen,
            TacticalOpportunityTaskOverride.Override(CommandTaskType.Screen, DirectChildRole.Screen, ReinforcementOpportunity.WithdrawalToFightLater, out _),
            "screen keeps covering the withdrawal");
        AssertEqual(CommandTaskType.Consolidate,
            TacticalOpportunityTaskOverride.Override(CommandTaskType.AttackObjective, DirectChildRole.Main, ReinforcementOpportunity.WaitAndConsolidate, out _),
            "wait for relief");
        AssertEqual(CommandTaskType.AttackObjective,
            TacticalOpportunityTaskOverride.Override(CommandTaskType.HoldObjective, DirectChildRole.SupportMain, ReinforcementOpportunity.AttackNow, out _),
            "attack now commits held main/support");
        AssertEqual(CommandTaskType.HoldObjective,
            TacticalOpportunityTaskOverride.Override(CommandTaskType.HoldObjective, DirectChildRole.RefuseLeft, ReinforcementOpportunity.AttackNow, out _),
            "flank guard stays");
        AssertEqual(CommandTaskType.Probe,
            TacticalOpportunityTaskOverride.Override(CommandTaskType.Probe, DirectChildRole.Main, ReinforcementOpportunity.NoOpportunity, out _),
            "no opportunity no change");
    }

    private static TacticalDivisionPlaySubordinate Sub(string id, CommandNodeRole role, bool engaged, float distance, CommandTaskType task)
    {
        return new TacticalDivisionPlaySubordinate(id, role, engaged, engaged, false, false, false, distance, false, task);
    }

    private static void DivisionPlayKeepsLedgerHoldTasks()
    {
        var decision = TacticalDivisionPlayExecutor.Decide(new TacticalDivisionPlayInput("div", 1f, new[]
        {
            Sub("holder", CommandNodeRole.MainEffort, true, 120f, CommandTaskType.HoldObjective),
            Sub("attacker", CommandNodeRole.SupportingAttack, true, 300f, CommandTaskType.AttackObjective),
            Sub("guard", CommandNodeRole.FlankMarch, false, 0f, CommandTaskType.GuardFlank),
            Sub("idle", CommandNodeRole.SupportingAttack, false, 0f, CommandTaskType.SupportAttack),
        }));
        AssertEqual("attacker", decision.AnchorNodeId, "a holding child cannot anchor an attack play");
        AssertFalse(decision.OrderFor("holder").HasOrder, "hold task kept");
        AssertFalse(decision.OrderFor("guard").HasOrder, "guard flank kept");
        AssertEqual(CommandTaskType.SupportAttack, decision.OrderFor("idle").Task, "idle sibling supports");
    }

    private static void DivisionPlayStandsDownForDefender()
    {
        var decision = TacticalDivisionPlayExecutor.Decide(new TacticalDivisionPlayInput("div", 1f, new[]
        {
            Sub("a", CommandNodeRole.MainEffort, true, 120f, CommandTaskType.AttackObjective),
            Sub("b", CommandNodeRole.SupportingAttack, false, 0f, CommandTaskType.SupportAttack),
        }, sideDefending: true));
        AssertFalse(decision.HasAnchor, "defending side keeps its ledger tasks");
        AssertEqual(0, decision.Orders.Count, "no play orders");
    }

    private static FormationWriteInput Fw(
        bool needsFormation = false, bool needsFacing = false, bool needsRefuse = false,
        float combat = 0f, float moving = 0f, int paths = 0, float cover = 0f,
        float now = 2f, float last = -1f)
    {
        return new FormationWriteInput(needsFormation, needsFacing, needsRefuse, combat, moving, paths, cover, now, last);
    }

    private static void FormationPolicyNeverRelaysInCombat()
    {
        var formation = CommandFormationWritePolicy.Decide(Fw(needsFormation: true, combat: 0.5f));
        AssertEqual(FormationWriteKind.None, formation.Kind, "no formation change in contact (AdjustGroupFormations 5891)");
        var facing = CommandFormationWritePolicy.Decide(Fw(needsFormation: true, needsFacing: true, combat: 0.5f));
        AssertEqual(FormationWriteKind.RotateInPlace, facing.Kind, "in contact only rotate in place");
        var refuse = CommandFormationWritePolicy.Decide(Fw(needsRefuse: true, combat: 0.5f));
        AssertEqual(FormationWriteKind.None, refuse.Kind, "no skewed re-lay in contact");
    }

    private static void FormationPolicyUsesVanillaOutOfCombat()
    {
        AssertEqual(FormationWriteKind.VanillaFormation, CommandFormationWritePolicy.Decide(Fw(needsFormation: true)).Kind, "formation via SetFormation");
        AssertEqual(FormationWriteKind.WheelGroup, CommandFormationWritePolicy.Decide(Fw(needsFacing: true)).Kind, "facing wheel");
        AssertEqual(FormationWriteKind.WheelGroup, CommandFormationWritePolicy.Decide(Fw(needsRefuse: true)).Kind, "refuse wheel");
        AssertEqual(FormationWriteKind.None, CommandFormationWritePolicy.Decide(Fw()).Kind, "nothing needed");
    }

    private static void FormationPolicyWaitsWhileMovingOrInCover()
    {
        AssertEqual(FormationWriteKind.None, CommandFormationWritePolicy.Decide(Fw(needsFormation: true, paths: 2)).Kind, "moving group");
        AssertEqual(FormationWriteKind.None, CommandFormationWritePolicy.Decide(Fw(needsFacing: true, moving: 0.5f)).Kind, "subordinates moving");
        AssertEqual(FormationWriteKind.None, CommandFormationWritePolicy.Decide(Fw(needsFormation: true, cover: 1f)).Kind, "in cover keeps formation (5925)");
        AssertEqual(FormationWriteKind.WheelGroup, CommandFormationWritePolicy.Decide(Fw(needsFormation: true, needsFacing: true, cover: 1f)).Kind, "in cover may still face");
    }

    private static void FormationPolicyCooldownLongerUnderFire()
    {
        float now = 2f;
        var calm = CommandFormationWritePolicy.Decide(Fw(needsFacing: true, now: now, last: now - CommandFormationWritePolicy.CooldownHours * 1.1f));
        AssertEqual(FormationWriteKind.WheelGroup, calm.Kind, "calm cooldown elapsed");
        var underFire = CommandFormationWritePolicy.Decide(Fw(needsFacing: true, combat: 0.5f, now: now, last: now - CommandFormationWritePolicy.CooldownHours * 1.1f));
        AssertEqual(FormationWriteKind.None, underFire.Kind, "under fire uses the longer cooldown");
        AssertTrue(CommandFormationWritePolicy.UnderFireCooldownHours >= CommandFormationWritePolicy.CooldownHours, "never shorter under fire");
        var reloaded = CommandFormationWritePolicy.Decide(Fw(needsFacing: true, now: 1f, last: 5f));
        AssertEqual(FormationWriteKind.WheelGroup, reloaded.Kind, "clock reset clears cooldown");
    }

    // Vanilla BattleUnits.SetFormation (90971) remap for unittyp >= 14.
    private static int VanillaGroupRemap(int index)
    {
        switch (index)
        {
            case 0: return 3;
            case 1: return 1;
            case 2: return 1;
            case 3: return 2;
            case 4: return 4;
            case 5: return 0;
            default: return index;
        }
    }

    private static void VanillaSetFormationIndexRoundTrips()
    {
        for (int f = 0; f <= 4; f++)
            AssertEqual(f, VanillaGroupRemap(CommandFormationWritePolicy.VanillaSetFormationIndexForGroup(f)), "group formation " + f);
        AssertEqual(-1, CommandFormationWritePolicy.VanillaSetFormationIndexForGroup(7), "invalid");
    }

    private static TacticalFacingPulseDecision Pulse(
        CommandTaskType task = CommandTaskType.HoldObjective,
        int current = 3, int target = 3,
        bool flankRisk = false, bool activeMove = false,
        bool isArtillery = false, bool inRange = true)
    {
        return TacticalFacingPulseDoctrine.Decide(new TacticalFacingPulseInput(
            TacticalFacingPulseScope.Regiment, task, current, target,
            currentFacingDegrees: 139f, targetFacingDegrees: 45f,
            hasThreatBearing: true, threatSource: TacticalFacingThreatSource.Visible,
            closeEngaged: true, flankRisk: flankRisk, playerProtected: false, routed: false,
            pendingOrder: false, activeMove: activeMove, recentPulse: false, toleranceDegrees: 15f,
            isArtillery: isArtillery, threatWithinFireRange: inRange));
    }

    private static void RegimentFacingPulseLeavesArtilleryToVanilla()
    {
        var d = Pulse(current: 3, target: 0, isArtillery: true);
        AssertFalse(d.ShouldWrite, "never formation 0 on a battery (unlimbers, 135446)");
        AssertEqual("artillery-vanilla", d.Reason, "reason");
    }

    private static void RegimentFacingPulseRequiresFireRange()
    {
        var d = Pulse(inRange: false);
        AssertFalse(d.ShouldWrite, "vanilla only turns to an enemy within fire range (3487)");
        AssertEqual("threat-out-of-range", d.Reason, "reason");
    }

    private static void RegimentFacingPulseNeverStopsMovingUnit()
    {
        AssertFalse(Pulse(activeMove: true, flankRisk: true).ShouldWrite, "RotateRegiment stops a moving unit");
        AssertFalse(Pulse(task: CommandTaskType.FallBackToLine).ShouldWrite, "a withdrawing unit keeps moving");
    }

    private static void RegimentFacingPulseOnlyRotates()
    {
        var d = Pulse(current: 3, target: 3);
        AssertEqual(TacticalFacingPulseAction.CorrectFacing, d.Action, "rotate only");
        AssertEqual(3, d.TargetFormation, "formation untouched");
    }

    private static void ExecutorMovementUsesCourierDelay()
    {
        foreach (var task in new[] { CommandTaskType.AttackObjective, CommandTaskType.Probe, CommandTaskType.Scout, CommandTaskType.FallBackToLine, CommandTaskType.AdvanceToAssembly })
            AssertTrue(CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(task, battleActive: true, inContactWithdrawal: false),
                task + " goes through the courier queue (UpdateMovingTargets 6959, RepositionAIGroupMicroTowardsEnemy 4146)");
    }

    private static void InContactWithdrawalSkipsOrderDelay()
    {
        AssertFalse(CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(CommandTaskType.FallBackToLine, battleActive: true, inContactWithdrawal: true),
            "in-contact fallback is immediate like CheckIfOutflanked 6235");
        AssertTrue(CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(CommandTaskType.AttackObjective, battleActive: true, inContactWithdrawal: true),
            "only withdrawals skip the courier");
    }

    private static void QueuedCourierOrderCountsAsIssuedMove()
    {
        AssertTrue(CommandWaypointWritePolicy.MovementIssued(0, 2, 0, 0), "paths appeared");
        AssertTrue(CommandWaypointWritePolicy.MovementIssued(0, 0, 0, 1), "courier queued");
        AssertFalse(CommandWaypointWritePolicy.MovementIssued(1, 1, 2, 2), "nothing changed");
    }

    private static void FailedWriteBacksOffOnBattleTime()
    {
        float until = CommandWaypointWritePolicy.FailedWriteBackoffUntil(2f);
        AssertTrue(until > 2f, "backoff in the future");
        AssertTrue(CommandWaypointWritePolicy.IsBackedOff(2.01f, until), "inside backoff");
        AssertFalse(CommandWaypointWritePolicy.IsBackedOff(until + 0.001f, until), "after backoff");
        AssertFalse(CommandWaypointWritePolicy.IsBackedOff(0.5f, until), "clock reset clears backoff");
    }

    private static void RealSecondsAtOneXConvertToBattleHours()
    {
        // Macro cycle 0.015 game-hours ≈ 9 s real at 1x ⇒ 1 game-hour ≈ 600 s real.
        AssertEqual(0.015f, TacticalBattleClock.RealSecondsAtOneXToHours(9f), "macro cycle");
        AssertEqual(0f, TacticalBattleClock.RealSecondsAtOneXToHours(-3f), "negative clamps");
    }

    private static void MainArrivalHoldArmsOnce()
    {
        var none = default(MainArrivalHoldLatch.State);
        AssertTrue(MainArrivalHoldLatch.ShouldEvaluate(none), "fresh state evaluates");
        AssertEqual(0f, MainArrivalHoldLatch.ElapsedHours(none, 1f), "no elapsed before arming");
        var armed = MainArrivalHoldLatch.Next(none, 1f, holding: true);
        AssertTrue(armed.Active, "armed");
        var later = MainArrivalHoldLatch.Next(armed, 1.02f, holding: true);
        AssertEqual(1f, later.StartedAtHours, "start time kept");
        AssertEqual(0.02f, (float)Math.Round(MainArrivalHoldLatch.ElapsedHours(later, 1.02f), 4), "elapsed on battle time");
    }

    private static void MainArrivalHoldStaysReleasedAfterExpiry()
    {
        var armed = MainArrivalHoldLatch.Next(default(MainArrivalHoldLatch.State), 1f, holding: true);
        var released = MainArrivalHoldLatch.Next(armed, 1.1f, holding: false);
        AssertTrue(released.Released, "release latched");
        AssertFalse(MainArrivalHoldLatch.ShouldEvaluate(released), "no re-arm until role/task changes (BR-M1)");
        var stillReleased = MainArrivalHoldLatch.Next(released, 1.2f, holding: true);
        AssertTrue(stillReleased.Released, "hold request ignored once released");
        AssertEqual(0f, MainArrivalHoldLatch.ElapsedHours(armed, 0.5f), "clock reset gives no negative elapsed");
    }

    private static LeafMoveDecision Leaf(CommandTaskType task, bool parentActive = false, bool hasSlot = true, bool closeEngaged = false, bool pending = false, bool activeMove = false)
    {
        return LeafBrigadeMovePolicy.Decide(task, parentActive, hasSlot, closeEngaged, pending, activeMove);
    }

    private static void LeafBrigadeMovesOnlyForMovementTasks()
    {
        AssertTrue(Leaf(CommandTaskType.AttackObjective).Move, "attack moves");
        AssertFalse(Leaf(CommandTaskType.HoldObjective).Move, "hold stays in its slot");
        AssertFalse(Leaf(CommandTaskType.ReserveWait).Move, "reserve stays");
        AssertEqual("non-movement-task", Leaf(CommandTaskType.GuardFlank).Reason, "reason");
    }

    private static void LeafBrigadeDefersToActiveParentOrder()
    {
        var d = Leaf(CommandTaskType.AttackObjective, parentActive: true);
        AssertFalse(d.Move, "vanilla group cascade places the brigade (SetGroupFormation 92060)");
        AssertEqual("parent-ordered", d.Reason, "reason");
        AssertFalse(Leaf(CommandTaskType.AttackObjective, hasSlot: false).Move, "no slot, no move");
    }

    private static void LeafBrigadeKeepsExecutorSafetyGates()
    {
        AssertFalse(Leaf(CommandTaskType.AttackObjective, closeEngaged: true).Move, "in contact");
        AssertFalse(Leaf(CommandTaskType.AttackObjective, pending: true).Move, "pending order");
        AssertFalse(Leaf(CommandTaskType.AttackObjective, activeMove: true).Move, "already moving");
    }

    private static void FormationSlotMirrorsVanillaMath()
    {
        // Vanilla 26945-26949: angle -= 90; x' = x - cos(a)*dz - cos(a+90)*dx; z' = z + sin(a)*dz + sin(a+90)*dx.
        double a = (30.0 - 90.0) * Math.PI / 180.0;
        double b = 30.0 * Math.PI / 180.0;
        float ex = (float)(100.0 - Math.Cos(a) * 40.0 - Math.Cos(b) * (-25.0));
        float ez = (float)(-200.0 + Math.Sin(a) * 40.0 + Math.Sin(b) * (-25.0));
        LeafFormationSlot.OffsetXZ(100f, -200f, 30f, -25f, 40f, out float x, out float z);
        AssertTrue(Math.Abs(ex - x) < 0.001f, "x");
        AssertTrue(Math.Abs(ez - z) < 0.001f, "z");
        LeafFormationSlot.OffsetXZ(100f, -200f, 77f, 0f, 0f, out float x0, out float z0);
        AssertEqual(100f, x0, "zero offset x");
        AssertEqual(-200f, z0, "zero offset z");
    }

    private static void RosterSignatureChangesWithRosterOnly()
    {
        var a = new TacticalRosterSignature(1, 120, 3, 7);
        var same = new TacticalRosterSignature(1, 120, 3, 7);
        AssertTrue(a.Equals(same), "same roster, any frame");
        AssertFalse(a.Equals(new TacticalRosterSignature(1, 121, 3, 7)), "unit added");
        AssertFalse(a.Equals(new TacticalRosterSignature(1, 120, 4, 7)), "unit routed");
        AssertFalse(a.Equals(new TacticalRosterSignature(1, 120, 3, 8)), "new battle (BattleUnits instance)");
        AssertFalse(a.Equals(new TacticalRosterSignature(0, 120, 3, 7)), "other alliance");
    }
}
