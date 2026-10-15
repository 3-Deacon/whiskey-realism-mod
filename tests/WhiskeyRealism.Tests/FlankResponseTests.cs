using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using static TestHarness;

// BC11 (docs/superpowers/plans/2026-10-15-battle-core.md § BC11): flank response and
// envelopment execution.
internal static partial class FlankResponseTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            // refuse reaches vanilla (the combined red-first tests)
            ("bc11 RefuseReachesVanillaOutOfCombat", RefuseReachesVanillaOutOfCombat),
            ("bc11 RefuseReachesVanillaInCombat", RefuseReachesVanillaInCombat),
        });
    }

    private const int Brigade = 100, Left1 = 1, Left2 = 2, Right1 = 3, Right2 = 4, EnemyId = 900;

    private sealed class RecordingSink : IIntentSink
    {
        public readonly List<BattleOrderIntent> Intents = new List<BattleOrderIntent>();
        public void Submit(in BattleOrderIntent intent) { Intents.Add(intent); }
    }

    // A brigade at the origin facing +z (0 = +z, 90 = +x), four regiments in a line across it
    // (x = -90, -30, 30, 90), one visible enemy regiment at 200 m on its left (-x), perception built.
    private static BattleSnapshot BrigadeFixture(float enemyX = -200f, float enemyZ = 0f, float enemyStrength = 500f)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.SetSliceCount(20);
        UnitFlags live = UnitFlags.Alive | UnitFlags.Active;
        s.AddOwn(new UnitRecord
        {
            InstanceId = Brigade, UnitTyp = TacticalUnitType.BattleGroupBrigade, X = 0f, Z = 0f, RotationY = 0f,
            Formation = 0, GroupWidth = 240f, GroupDepth = 60f, Flags = live | UnitFlags.IsGroup,
        });
        int[] ids = { Left1, Left2, Right1, Right2 };
        float[] xs = { -90f, -30f, 30f, 90f };
        for (int i = 0; i < ids.Length; i++)
        {
            s.AddOwn(new UnitRecord
            {
                InstanceId = ids[i], UnitTyp = TacticalUnitType.Infantry, X = xs[i], Z = 0f, RotationY = 0f, FireRange = 150f,
                SlotOffsetX = xs[i], Flags = live | UnitFlags.IsInfantry | UnitFlags.HasLayoutSlot,
            });
            s.Own[i + 1].ParentIndex = 0;
        }
        s.IndexCommandTree(null);
        s.AddObservation(new EnemyObservation
        {
            InstanceId = EnemyId, UnitTyp = TacticalUnitType.Infantry, X = enemyX, Z = enemyZ, Strength = enemyStrength,
            State = ObservationState.Visible, Confidence01 = 1f,
        });
        SlicePressureBuilder.Build(s, new SliceGeometry { MaxRangeMeters = 250f, FlankShiftMeters = 150f });
        return s;
    }

    // RefuseFor -> PostureIntentPlan.Submit -> the formation intent, as the posture pipeline builds it.
    private static BattleOrderIntent RefuseFormationIntent(BattleSnapshot s, CommandTaskType task)
    {
        bool closeEngaged = PostureSense.CloseEngaged(s, 0, UnitFlags.None);
        RefuseFlank refuse = PostureSense.RefuseFor(s, 0, task, closeEngaged, flankRisk: true);
        var sink = new RecordingSink();
        PostureIntentPlan.Submit(sink, new PostureIntentRequest
        {
            GroupId = Brigade, Action = PostureExecutionAction.SetFormation, Task = task,
            TargetFormation = CommandFormationCorrection.TargetFormationForTask(task, 0), NeedsFormation = false,
            Refuse = refuse, Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Reason = "posture-executor",
        });
        AssertEqual(1, sink.Intents.Count, "one formation intent");
        return sink.Intents[0];
    }

    // The executor's formation verdict for the group (OrderExecutor.DecideFormation, what OrderExecutorRuntime.IssueFormation issues).
    private static FormationWriteDecision DecideFormation(BattleSnapshot s, in BattleOrderIntent w, float combatStatusSubordinates)
    {
        // OrderExecutorRuntime.GroupRangeReach is runtime-only; vanilla's reach is at least the 250 m close-contact range.
        float enemiesInRange = BattleWriterPolicy.SnapshotEnemyStrengthInRange(s, 0, 250f);
        return OrderExecutor.DecideFormation(w, s.Own[0].Formation, combatStatusSubordinates, 0f, 0, 0f, 2.0f, -1f, enemiesInRange);
    }

    // Out of combat (combatstatussubordinates 0) with a visible enemy 200 m off the left flank: the refused flank
    // reaches vanilla as the wheel with refuseflank 0 (left). Red on the shipped policy: the 200 m enemy counts
    // as "enemies in range", so the policy returns None and the plan carries -1.
    private static void RefuseReachesVanillaOutOfCombat()
    {
        BattleSnapshot s = BrigadeFixture();
        BattleOrderIntent w = RefuseFormationIntent(s, CommandTaskType.HoldObjective);
        AssertEqual(RefuseFlank.Left, w.Refuse, "the posture sense refuses the left flank");
        AssertEqual(0, OrderExecutor.VanillaRefuseFlank(w.Refuse), "refuseflank 0 (left)");
        FormationWriteDecision d = DecideFormation(s, w, combatStatusSubordinates: 0f);
        AssertEqual("WheelGroup", d.Kind.ToString(), "the refuse wheel is issued whatever the enemies in reach (" + d.Reason + ")");
    }

    // In combat the refuse is the limited in-place rotation (never a re-lay). Red on the shipped policy:
    // combat-no-relay for a refuse-only write.
    private static void RefuseReachesVanillaInCombat()
    {
        BattleSnapshot s = BrigadeFixture();
        BattleOrderIntent w = RefuseFormationIntent(s, CommandTaskType.HoldObjective);
        FormationWriteDecision d = DecideFormation(s, w, combatStatusSubordinates: 1f);
        AssertEqual("RefuseInPlace", d.Kind.ToString(), "in combat the refuse rotates the wing in place (" + d.Reason + ")");
    }
}
