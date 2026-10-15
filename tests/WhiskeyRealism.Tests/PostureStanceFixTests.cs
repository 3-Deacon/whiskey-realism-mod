using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
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
}
