using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestFixtures;
using static TestHarness;
using VanillaMapBounds = WhiskeyRealism.Tactical.Battle.VanillaMapBounds;
using VanillaPathAreaCosts = WhiskeyRealism.Tactical.Battle.VanillaPathAreaCosts;
using TacticalBattleClock = WhiskeyRealism.Tactical.Battle.TacticalBattleClock;
using CommandWaypointWritePolicy = WhiskeyRealism.Tactical.Battle.CommandWaypointWritePolicy;

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
        Pass("stance owner never overwrites vanilla assault except charge demotion", StanceOwnerRespectsVanillaAssault);
        Pass("write and a healthy claim stamp the vanilla hold", WriteAndHealthyClaimStampVanillaHold);
        Pass("vanilla per-group stance ports AdjustGroupAIStance 4239-4252", VanillaPerGroupStancePortsAdjustGroupAIStance);
        Pass("stance owner yields to vanilla weak-group defend and screen", StanceOwnerYieldsToVanillaWeakGroup);
        Pass("stance owner yields to vanilla assault only when Whiskey wants attack", StanceOwnerYieldsToAssaultOnlyForAttack);
        Pass("a lower-priority source cannot yield a higher-priority hold", LowerPrioritySourceCannotYieldHigherHold);
        Pass("stance owner claim without vanilla stance does not stamp", StanceOwnerClaimWithoutVanillaStanceDoesNotStamp);
        Pass("retreat release lets vanilla re-evaluate immediately", RetreatReleaseLetsVanillaReevaluate);
        Pass("group stance scorer uses the shared task mapping", GroupStanceScorerUsesSharedTaskMapping);

        // T-settle
        Pass("settlement gate does not treat a marching unit as settled", SettlementGateMarchingUnitIsNotSettled);
        Pass("settlement gate does not treat an active move as settled", SettlementGateActiveMoveIsNotSettled);

        // B-macro3
        Pass("posture executor stands down in vanilla retreat and end of day", PostureExecutorStandsDownInRetreatAndEndOfDay);
        Pass("posture executor skips the feud instance", PostureExecutorSkipsFeudInstance);

        // BR-H1
        Pass("defensive hold is no opinion for leaf and brigade tasks", DefensiveHoldIsNoOpinionForTasks);
        Pass("defensive hold never turns attack probe or support into hold for any role", DefensiveHoldNeverHoldsAnyRole);
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
        Pass("formation policy never wheels a group with enemies in range", FormationPolicyNeverWheelsWithEnemiesInRange);

        // R9
        Pass("regiment facing pulse leaves artillery to vanilla", RegimentFacingPulseLeavesArtilleryToVanilla);
        Pass("regiment facing pulse requires a threat inside fire range", RegimentFacingPulseRequiresFireRange);
        Pass("regiment facing pulse never stops a moving or withdrawing unit", RegimentFacingPulseNeverStopsMovingUnit);
        Pass("regiment facing pulse with current formation only rotates", RegimentFacingPulseOnlyRotates);

        // B-61clock / M3 retry
        Pass("executor movement uses courier order delay like vanilla AI group moves", ExecutorMovementUsesCourierDelay);
        Pass("in-contact withdrawal skips order delay like CheckIfOutflanked", InContactWithdrawalSkipsOrderDelay);
        Pass("real seconds at 1x convert to battle hours", RealSecondsAtOneXConvertToBattleHours);

        // BR-M1
        Pass("main arrival hold arms once and keeps its start time", MainArrivalHoldArmsOnce);
        Pass("main arrival hold stays released after expiry", MainArrivalHoldStaysReleasedAfterExpiry);
        Pass("main arrival hold runs 900 battle seconds on currenttimefromstart", MainArrivalHoldRunsOnBattleSeconds);

        // B-leaf
        Pass("leaf brigade moves only for movement tasks", LeafBrigadeMovesOnlyForMovementTasks);
        Pass("leaf brigade defers to an active parent order", LeafBrigadeDefersToActiveParentOrder);
        Pass("leaf brigade keeps executor safety gates", LeafBrigadeKeepsExecutorSafetyGates);
        Pass("waypoint clamp mirrors vanilla RegimentSetPath map bounds", WaypointClampMirrorsVanillaMapBounds);

        // B-61state / perf: probe cache keyed on roster, not frame
        Pass("roster signature changes with roster not with frame", RosterSignatureChangesWithRosterOnly);

        // Perf
        Pass("friendly spatial grid matches brute-force front blocker", FriendlySpatialGridMatchesBruteForce);
        Pass("nav plan budget defers and rotates", NavPlanBudgetDefersAndRotates);
        Pass("path-quality probe uses vanilla battle AddPath area costs", PathProbeUsesVanillaAreaCosts);
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
        StanceWriteSource source = StanceWriteSource.Scorer,
        int vanilla = TacticalStanceOwnership.VanillaStanceUnknown)
    {
        return new StanceWriteInput(isAiGroup, target, current, macroAi, now, record, source, vanilla);
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
        AssertEqual(StanceWriteAction.Yield, retreat.Action, "macroai 3 hands the group back to vanilla");
        AssertEqual("vanilla-retreat", retreat.Reason, "macroai 3 reason");
        AssertTrue(TacticalStanceOwnership.ReleasesVanillaHold(retreat), "macroai 3 releases the stamp (4258)");
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
        AssertFalse(TestFixtures.VanillaWouldReevaluateStance(stamp, 10f, reset), "held at write time");
        AssertFalse(TestFixtures.VanillaWouldReevaluateStance(stamp, 10f + TacticalStanceOwnership.HoldHours * 0.9f, reset), "held inside hold");
        AssertTrue(TestFixtures.VanillaWouldReevaluateStance(stamp, 10f + TacticalStanceOwnership.HoldHours * 1.1f, reset), "vanilla resumes after hold");
        AssertTrue(TestFixtures.VanillaWouldReevaluateStance(10f, 10f, reset), "vanilla's own stamp never holds (inverted compare)");
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

    private static void DefensiveHoldNeverHoldsAnyRole()
    {
        // Both #61 call sites (brigade posture: role Main; leaf cascade: the
        // leaf's role) go through TacticalOpportunityTaskOverride.Override.
        foreach (DirectChildRole role in Enum.GetValues(typeof(DirectChildRole)))
        {
            foreach (var task in new[] { CommandTaskType.AttackObjective, CommandTaskType.Probe, CommandTaskType.SupportAttack })
            {
                var result = TacticalOpportunityTaskOverride.Override(task, role, ReinforcementOpportunity.DefensiveHold, out string reason);
                AssertEqual(task, result, "DefensiveHold keeps " + task + " for " + role);
                AssertTrue(result != CommandTaskType.HoldObjective, "never Hold for " + role);
                AssertTrue(reason == null, "no override reason for " + role);
            }
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
        var decision = TacticalDivisionPlayExecutor.Decide(new TacticalDivisionPlayInput("div", new[]
        {
            Sub("holder", CommandNodeRole.MainEffort, true, 120f, CommandTaskType.HoldObjective),
            Sub("attacker", CommandNodeRole.SupportingAttack, true, 300f, CommandTaskType.AttackObjective),
            Sub("guard", CommandNodeRole.FlankMarch, false, 0f, CommandTaskType.GuardFlank),
            Sub("idle", CommandNodeRole.SupportingAttack, false, 0f, CommandTaskType.SupportAttack),
        }));
        AssertEqual("attacker", decision.AnchorNodeId, "a holding child cannot anchor an attack play");
        AssertFalse(PlayOrderFor(decision, "holder").HasOrder, "hold task kept");
        AssertFalse(PlayOrderFor(decision, "guard").HasOrder, "guard flank kept");
        AssertEqual(CommandTaskType.SupportAttack, PlayOrderFor(decision, "idle").Task, "idle sibling supports");
    }

    private static void DivisionPlayStandsDownForDefender()
    {
        var decision = TacticalDivisionPlayExecutor.Decide(new TacticalDivisionPlayInput("div", new[]
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
        float now = 2f, float last = -1f, float enemiesInRange = 0f)
    {
        return new FormationWriteInput(needsFormation, needsFacing, needsRefuse, combat, moving, paths, cover, now, last, enemiesInRange);
    }

    private static void FormationPolicyNeverWheelsWithEnemiesInRange()
    {
        AssertEqual(FormationWriteKind.WheelGroup, CommandFormationWritePolicy.Decide(Fw(needsFacing: true)).Kind, "clear ground wheels");
        var facing = CommandFormationWritePolicy.Decide(Fw(needsFacing: true, enemiesInRange: 0.4f));
        AssertEqual(FormationWriteKind.None, facing.Kind, "wheel re-paths every child (SetGroupFormation modifylastwaypoint)");
        AssertEqual("enemies-in-range", facing.Reason, "reason");
        AssertEqual(FormationWriteKind.None, CommandFormationWritePolicy.Decide(Fw(needsRefuse: true, enemiesInRange: 1f)).Kind, "refuse flank too");
        AssertEqual(FormationWriteKind.VanillaFormation,
            CommandFormationWritePolicy.Decide(Fw(needsFormation: true, enemiesInRange: 1f)).Kind, "courier formation change unaffected");
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

    private static void MainArrivalHoldRunsOnBattleSeconds()
    {
        // currenttimefromstart is game hours in battle (Hour + battlepasseddays * 24, 86379 / 93545).
        AssertEqual(3600f, TacticalBattleClock.HoursToBattleSeconds(1f), "one battle hour");
        AssertEqual(0f, TacticalBattleClock.HoursToBattleSeconds(-1f), "negative clamps");
        AssertEqual(0f, TacticalBattleClock.HoursToBattleSeconds(float.NaN), "NaN clamps");
        AssertEqual(900f, MainArrivalHoldLatch.MaxHoldBattleSeconds, "15 battle-minutes");

        var armed = MainArrivalHoldLatch.Next(default(MainArrivalHoldLatch.State), 2f, holding: true);
        var distant = new[] { new TacticalMainArrivalSynchronizer.SupportMainPosition(2000f) };
        float almost = MainArrivalHoldLatch.ElapsedBattleSeconds(armed, 2f + 0.24f);
        AssertEqual(864f, (float)Math.Round(almost, 1), "elapsed in battle seconds");
        TacticalMainArrivalSynchronizer.ResolveMainAttackTask(DirectChildRole.Main, CommandTaskType.AttackObjective, distant,
            0.3f, almost, TacticalMainArrivalSynchronizer.DefaultSupportProximityThresholdMeters, MainArrivalHoldLatch.MaxHoldBattleSeconds, out bool holding);
        AssertTrue(holding, "still holding at 14.4 battle-minutes");
        float expired = MainArrivalHoldLatch.ElapsedBattleSeconds(armed, 2f + 0.26f);
        TacticalMainArrivalSynchronizer.ResolveMainAttackTask(DirectChildRole.Main, CommandTaskType.AttackObjective, distant,
            0.3f, expired, TacticalMainArrivalSynchronizer.DefaultSupportProximityThresholdMeters, MainArrivalHoldLatch.MaxHoldBattleSeconds, out holding);
        AssertFalse(holding, "released after 15 battle-minutes");
    }

    private static LeafMoveDecision Leaf(CommandTaskType task, bool parentActive = false, bool hasSlot = true, bool closeEngaged = false, bool pending = false, bool activeMove = false)
    {
        return LeafBrigadeMovePolicy.Decide(task, parentActive, hasSlot, closeEngaged, pending, activeMove);
    }

    private static void PathProbeUsesVanillaAreaCosts()
    {
        // Regiment.AddPath in battle, roads allowed, path not interrupted (130364-130394).
        AssertEqual(1f, VanillaPathAreaCosts.Roads, "roads");
        AssertEqual(1f, VanillaPathAreaCosts.Bridges, "bridges");
        AssertEqual(2f, VanillaPathAreaCosts.FencesWalls, "fences/walls");
        AssertEqual(500000f, VanillaPathAreaCosts.Rivers(frozen: false), "rivers impassable");
        AssertEqual(2f, VanillaPathAreaCosts.Rivers(frozen: true), "frozen rivers walkable");
        AssertEqual("FencesWalls, Sea (C)", VanillaPathAreaCosts.FencesWallsAreaName, "area name");
    }

    private static void WaypointClampMirrorsVanillaMapBounds()
    {
        // RegimentSetPath 131054-131071: x in [1, size.x - 2], z in [-(size.z - 2), -1].
        VanillaMapBounds.ClampXZ(500f, -500f, 1000f, 1000f, out float x, out float z);
        AssertEqual(500f, x, "inside x"); AssertEqual(-500f, z, "inside z");
        VanillaMapBounds.ClampXZ(-40f, 20f, 1000f, 1000f, out x, out z);
        AssertEqual(1f, x, "west edge"); AssertEqual(-1f, z, "north edge");
        VanillaMapBounds.ClampXZ(1150f, -1150f, 1000f, 1000f, out x, out z);
        AssertEqual(998f, x, "east edge"); AssertEqual(-998f, z, "south edge");
        VanillaMapBounds.ClampXZ(float.NaN, 3f, 0f, 0f, out x, out z);
        AssertTrue(float.IsNaN(x) && z == 3f, "no terrain size: unchanged");
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

    private static void RosterSignatureChangesWithRosterOnly()
    {
        var a = new ProbeRosterSignature(1, 120, 3, 7);
        var same = new ProbeRosterSignature(1, 120, 3, 7);
        AssertTrue(a.Equals(same), "same roster, any frame");
        AssertFalse(a.Equals(new ProbeRosterSignature(1, 121, 3, 7)), "unit added");
        AssertFalse(a.Equals(new ProbeRosterSignature(1, 120, 4, 7)), "unit routed");
        AssertFalse(a.Equals(new ProbeRosterSignature(1, 120, 3, 8)), "new battle (BattleUnits instance)");
        AssertFalse(a.Equals(new ProbeRosterSignature(0, 120, 3, 7)), "other alliance");
    }

    // Reference: the pre-grid EstimateFriendlyFrontBlocker walk (corridor 30 m
    // ahead to target + 120 m, 120 m lateral, proximity 80..300 m).
    private static float BruteForceBlocker(float ox, float oz, float cx, float cz, float[,] friends)
    {
        float ax = cx - ox, az = cz - oz;
        float len = (float)Math.Sqrt(ax * ax + az * az);
        if (len < 1f) return 0f;
        ax /= len; az /= len;
        float worst = 0f;
        for (int i = 0; i < friends.GetLength(0); i++)
        {
            float rx = friends[i, 0] - ox, rz = friends[i, 1] - oz;
            float forward = rx * ax + rz * az;
            if (forward < 30f || forward > len + 120f) continue;
            float lx = rx - ax * forward, lz = rz - az * forward;
            float lateral = (float)Math.Sqrt(lx * lx + lz * lz);
            if (lateral > 120f) continue;
            float dx = friends[i, 0] - cx, dz = friends[i, 1] - cz;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            float proximity = dist <= 80f ? 1f : Math.Max(0f, 1f - ((dist - 80f) / 220f));
            float lateralBlock = 1f - Math.Min(1f, lateral / 120f);
            worst = Math.Max(worst, Math.Max(proximity, lateralBlock * 0.8f));
        }
        return Math.Min(1f, worst);
    }

    private static void FriendlySpatialGridMatchesBruteForce()
    {
        var rng = new Random(42);
        var friends = new float[200, 2];
        var grid = new TacticalFriendlySpatialGrid();
        for (int i = 0; i < 200; i++)
        {
            friends[i, 0] = (float)(rng.NextDouble() * 3000.0);
            friends[i, 1] = (float)(rng.NextDouble() * -3000.0);
            grid.Add(i + 1, friends[i, 0], friends[i, 1]);
        }
        grid.Add(999, 1500f, -1500f); // the moving group itself
        for (int t = 0; t < 50; t++)
        {
            float ox = (float)(rng.NextDouble() * 3000.0), oz = (float)(rng.NextDouble() * -3000.0);
            float cx = (float)(rng.NextDouble() * 3000.0), cz = (float)(rng.NextDouble() * -3000.0);
            float expected = BruteForceBlocker(ox, oz, cx, cz, friends);
            float actual = grid.EstimateFrontBlocker(999, ox, oz, cx, cz);
            AssertTrue(Math.Abs(expected - actual) < 0.0001f, "grid matches brute force, case " + t);
        }
    }

    private static void NavPlanBudgetDefersAndRotates()
    {
        var budget = new NavPlanBudget(2);
        AssertTrue(budget.TryConsume(), "first");
        AssertTrue(budget.TryConsume(), "second");
        AssertFalse(budget.TryConsume(), "third deferred");
        budget.NextApply(5);
        AssertTrue(budget.TryConsume(), "refilled next apply");
        AssertEqual(2, budget.StartIndex(5), "start rotates by the work done");
        budget.NextApply(5);
        AssertEqual(3, budget.StartIndex(5), "rotates again");
        AssertEqual(0, budget.StartIndex(0), "empty list");
    }

    private static void StanceOwnerRespectsVanillaAssault()
    {
        // Assault (4) is vanilla's strength-triggered escalation (4240); its
        // visible effect is the micro AI's assault placement (5694).
        var exec = TacticalStanceOwnership.Decide(Input(target: 3, current: 4, source: StanceWriteSource.Executor));
        AssertEqual(StanceWriteAction.Refuse, exec.Action, "#61 does not demote a vanilla assault");
        AssertEqual("vanilla-assault", exec.Reason, "reason");
        var demote = TacticalStanceOwnership.Decide(Input(target: 3, current: 4, source: StanceWriteSource.ChargeDemotion));
        AssertEqual(StanceWriteAction.Write, demote.Action, "explicit charge denial may demote");
        // Vanilla only reaches 4 at 4240 on a strong group, so at demotion time
        // its per-group stance is 4 too — the escalation yield must not swallow it.
        var strong = TacticalStanceOwnership.Decide(Input(target: 3, current: 4, source: StanceWriteSource.ChargeDemotion, vanilla: 4));
        AssertEqual(StanceWriteAction.Write, strong.Action, "current 4 + vanilla 4 + charge demotion writes");
        AssertEqual("charge-demotion", strong.Reason, "reason");
        var held = new StanceHoldRecord(3, 1.0f, StanceWriteSource.ChargeDemotion);
        AssertEqual(StanceWriteAction.Write,
            TacticalStanceOwnership.Decide(Input(target: 3, current: 4, now: 1.01f, record: held, source: StanceWriteSource.ChargeDemotion, vanilla: 4)).Action,
            "a re-escalated group is demoted again inside the hold");
    }

    private static void WriteAndHealthyClaimStampVanillaHold()
    {
        var write = new StanceWriteDecision(StanceWriteAction.Write, "write");
        var claim = new StanceWriteDecision(StanceWriteAction.Claim, "held");
        AssertTrue(TacticalStanceOwnership.StampsVanillaHold(write, Input(target: 3, current: 2)), "write holds vanilla off");
        AssertTrue(TacticalStanceOwnership.StampsVanillaHold(claim, Input(target: 3, current: 3, vanilla: 3)),
            "agreement re-stamps so vanilla does not flip the group every 0.25 h (4264)");
        AssertFalse(TacticalStanceOwnership.StampsVanillaHold(new StanceWriteDecision(StanceWriteAction.Refuse, "x"), Input(target: 3)), "refuse");
        AssertFalse(TacticalStanceOwnership.StampsVanillaHold(new StanceWriteDecision(StanceWriteAction.Yield, "x"), Input(target: 3)), "yield");

        // An idle group: GetGroupStrength is 0 out of combat (6032), every > 0 guard
        // fails and vanilla's per-group stance is attack (3) — a healthy claim.
        var idle = Input(target: 3, current: 3, vanilla: TacticalStanceOwnership.VanillaPerGroupStance(Vg(strength: 0f)));
        var d = TacticalStanceOwnership.Decide(idle);
        AssertEqual(StanceWriteAction.Claim, d.Action, "idle group claim");
        AssertTrue(TacticalStanceOwnership.StampsVanillaHold(d, idle), "idle group claim stamps");
    }

    private static VanillaGroupStanceInput Vg(
        float strength, int macroAi = 1, bool lineGroup = true, bool defenderBattle = false,
        float assault = 1.5f, float defend = 0.5f, float screen = 0.7f)
    {
        return new VanillaGroupStanceInput(strength, macroAi, lineGroup, defenderBattle, assault, defend, screen);
    }

    private static void VanillaPerGroupStancePortsAdjustGroupAIStance()
    {
        AssertEqual(3, TacticalStanceOwnership.VanillaPerGroupStance(Vg(0f)), "no combat: every > 0 guard fails");
        AssertEqual(3, TacticalStanceOwnership.VanillaPerGroupStance(Vg(1.0f)), "healthy");
        AssertEqual(4, TacticalStanceOwnership.VanillaPerGroupStance(Vg(2.0f)), "4240 assault for a line group");
        AssertEqual(3, TacticalStanceOwnership.VanillaPerGroupStance(Vg(2.0f, lineGroup: false)), "4240 needs oobunitsymbolid 0");
        AssertEqual(3, TacticalStanceOwnership.VanillaPerGroupStance(Vg(2.0f, macroAi: 2)), "4240 needs macroai 0/1");
        AssertEqual(1, TacticalStanceOwnership.VanillaPerGroupStance(Vg(0.6f)), "4252 screen below the screening trigger");
        AssertEqual(2, TacticalStanceOwnership.VanillaPerGroupStance(Vg(0.4f)), "4244 defend; 4252 skipped because the local stance is 2");
        AssertEqual(1, TacticalStanceOwnership.VanillaPerGroupStance(Vg(0.4f, defenderBattle: true)), "4252 screens a defend group in a defender battle");
        AssertEqual(2, TacticalStanceOwnership.VanillaPerGroupStance(Vg(0.4f, macroAi: 2)), "4244 defend under macroai 2");
        AssertEqual(3, TacticalStanceOwnership.VanillaPerGroupStance(Vg(0.6f, macroAi: 2)),
            "side-level 4248 (macroai 2) is not a per-group valve");
        AssertEqual(3, TacticalStanceOwnership.VanillaPerGroupStance(Vg(1.0f, macroAi: 7)), "trigger index out of range");
        AssertEqual(3, TacticalStanceOwnership.VanillaPerGroupStance(Vg(float.NaN)), "NaN strength");
    }

    private static void StanceOwnerYieldsToVanillaWeakGroup()
    {
        var weak = Input(target: 3, current: 3, vanilla: 2);
        var d = TacticalStanceOwnership.Decide(weak);
        AssertEqual(StanceWriteAction.Yield, d.Action, "weak group: vanilla defends");
        AssertEqual("vanilla-weak-group", d.Reason, "reason");
        AssertFalse(TacticalStanceOwnership.StampsVanillaHold(d, weak), "no stamp");
        AssertTrue(TacticalStanceOwnership.ReleasesVanillaHold(d), "stamp released so vanilla re-decides next cycle");
        AssertFalse(TacticalStanceOwnership.Next(d, weak).HasValue, "ownership dropped");

        // Inside Whiskey's hold vanilla's screen must not be reasserted over.
        var record = new StanceHoldRecord(3, 1.0f, StanceWriteSource.Executor);
        var afterVanilla = TacticalStanceOwnership.Decide(Input(target: 3, current: 1, now: 1.01f, record: record, source: StanceWriteSource.Executor, vanilla: 1));
        AssertEqual(StanceWriteAction.Yield, afterVanilla.Action, "no reassert over vanilla's weak-group screen");

        var demotion = TacticalStanceOwnership.Decide(Input(target: 3, current: 4, source: StanceWriteSource.ChargeDemotion, vanilla: 1));
        AssertEqual(StanceWriteAction.Write, demotion.Action, "an explicit charge denial precedes every yield");

        var agree = TacticalStanceOwnership.Decide(Input(target: 2, current: 2, vanilla: 2));
        AssertEqual(StanceWriteAction.Claim, agree.Action, "Whiskey agrees with vanilla's defend");

    }

    private static void StanceOwnerYieldsToAssaultOnlyForAttack()
    {
        var attack = Input(target: 3, current: 3, vanilla: 4);
        var d = TacticalStanceOwnership.Decide(attack);
        AssertEqual(StanceWriteAction.Yield, d.Action, "Whiskey attack + vanilla assault escalation → yield");
        AssertEqual("vanilla-assault-escalation", d.Reason, "reason");
        AssertFalse(TacticalStanceOwnership.StampsVanillaHold(d, attack), "no stamp");
        AssertFalse(TacticalStanceOwnership.Next(d, attack).HasValue, "record dropped");

        var held = new StanceHoldRecord(3, 1.0f, StanceWriteSource.Executor);
        var inside = TacticalStanceOwnership.Decide(Input(target: 3, current: 3, now: 1.01f, record: held, source: StanceWriteSource.Executor, vanilla: 4));
        AssertEqual(StanceWriteAction.Yield, inside.Action, "escalation yields inside a hold too");

        foreach (int target in new[] { 1, 2 })
        {
            var hold = Input(target: target, current: target, vanilla: 4);
            var h = TacticalStanceOwnership.Decide(hold);
            AssertEqual(StanceWriteAction.Claim, h.Action, "Whiskey " + target + " + vanilla assault → keep holding");
            AssertTrue(TacticalStanceOwnership.StampsVanillaHold(h, hold), "hold re-stamped for " + target);
        }
    }

    private static void LowerPrioritySourceCannotYieldHigherHold()
    {
        // #61 (Executor) wrote screen on a strong group (vanilla per-group = 4).
        // #45 (Scorer) wants attack: escalation would yield, but the hold is not
        // the scorer's to give away.
        var executorHold = new StanceHoldRecord(1, 1.0f, StanceWriteSource.Executor);
        var scorer = Input(target: 3, current: 1, now: 1.01f, record: executorHold, source: StanceWriteSource.Scorer, vanilla: 4);
        var d = TacticalStanceOwnership.Decide(scorer);
        AssertEqual(StanceWriteAction.Refuse, d.Action, "scorer refused");
        AssertEqual("whiskey-hold", d.Reason, "reason");
        AssertFalse(TacticalStanceOwnership.ReleasesVanillaHold(d), "stamp not released");
        AssertFalse(TacticalStanceOwnership.StampsVanillaHold(d, scorer), "stamp not changed");
        var next = TacticalStanceOwnership.Next(d, scorer);
        AssertEqual(StanceWriteSource.Executor, next.Source, "executor record kept");
        AssertEqual(1, next.Stance, "executor stance kept");

        // Same for the weak-group yield.
        var weak = Input(target: 3, current: 1, now: 1.01f, record: executorHold, source: StanceWriteSource.Scorer, vanilla: 2);
        AssertEqual(StanceWriteAction.Refuse, TacticalStanceOwnership.Decide(weak).Action, "weak-group yield refused for lower source");

        // The owner itself (or a higher source) may still yield.
        var owner = Input(target: 3, current: 1, now: 1.01f, record: executorHold, source: StanceWriteSource.Executor, vanilla: 4);
        AssertEqual(StanceWriteAction.Yield, TacticalStanceOwnership.Decide(owner).Action, "owner yields");
        // Outside the hold any source may yield.
        var expired = Input(target: 3, current: 1, now: 1.0f + TacticalStanceOwnership.HoldHours + 0.01f, record: executorHold, source: StanceWriteSource.Scorer, vanilla: 4);
        AssertEqual(StanceWriteAction.Yield, TacticalStanceOwnership.Decide(expired).Action, "expired hold yields");
    }

    private static void StanceOwnerClaimWithoutVanillaStanceDoesNotStamp()
    {
        var unknown = Input(target: 3, current: 3, vanilla: TacticalStanceOwnership.VanillaStanceUnknown);
        var d = TacticalStanceOwnership.Decide(unknown);
        AssertEqual(StanceWriteAction.Claim, d.Action, "claim");
        AssertFalse(TacticalStanceOwnership.StampsVanillaHold(d, unknown), "cannot check vanilla's valves → leave vanilla free");
        var write = Input(target: 3, current: 2, vanilla: TacticalStanceOwnership.VanillaStanceUnknown);
        AssertTrue(TacticalStanceOwnership.StampsVanillaHold(TacticalStanceOwnership.Decide(write), write), "a write still stamps");
    }

    private static void RetreatReleaseLetsVanillaReevaluate()
    {
        float now = 3f;
        float release = TacticalStanceOwnership.ReleaseStamp(now);
        AssertTrue(TestFixtures.VanillaWouldReevaluateStance(release, now, 1f), "vanilla screens the retreating army next cycle (4258)");
        AssertTrue(TestFixtures.VanillaWouldReevaluateStance(release, now, 0f), "even with a zero reset pref");
    }
}
