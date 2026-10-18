using System;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;
using static TestFixtures;

// Smoke 2026-10-17: the Union's operation went Forming (enemy-line-0) -> Scouting (Island_Ford) -> Scouting -> Scouting -> SoftAbort -> Committed over 3
// minutes of real time in contact. The ledger resets every operation that is not committed (or later) to Planning on the first objective at each
// update, so a Forming operation lasted one tick, and the visible enemy line it is formed on is visible-only: one tick without a glimpse and the
// side "has not met the enemy" again. A selected operation is held for its own minimum commit window (900 s Forming, 420 s Scouting).
internal static class OperationFormingHoldTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("operation forming is held through a tick without a glimpse of the enemy line", FormingSurvivesAFogTick),
            ("operation forming is released when its window has passed and the enemy is still not seen", FormingEndsAfterItsWindow),
            ("operation scouting is still reselected every update", ScoutingIsReselectedEveryUpdate),
        });
    }

    private static ObjectiveRecord Map(string id) => new ObjectiveRecord(
        new ObjectiveObservationInput(id, TacticalObjectiveType.Ford, TacticalObjectiveSource.VerifiedSceneObject, new TacticalMapPoint(1200f, -1300f), 0.9f, 1.0f, typeAnchorVerified: true),
        TacticalObjectiveStatus.Scouting, 0f, 0f);

    private static ObjectiveRecord EnemyLine(float enemy) => new ObjectiveRecord(
        new ObjectiveObservationInput("enemy-line-0", TacticalObjectiveType.EnemyLine, TacticalObjectiveSource.VisibleEnemyLine, new TacticalMapPoint(900f, -950f), 0.7f, 0.4f, typeAnchorVerified: true),
        TacticalObjectiveStatus.Contested, enemy, 3000f);

    private static readonly StrategicBattleIntentSnapshot Intent = new StrategicBattleIntentSnapshot(0.2f, 0.3f, "theater", "campaign");

    private static CommandNodeOperationalState[] States() => new[]
    {
        CommandNodeOperationalState.Create("node-1", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 0f, 0f, 0f),
    };

    private static OperationRecord Tick(TacticalOperationsLedgerRuntime runtime, float now, params ObjectiveRecord[] objectives)
    {
        runtime.SetRuntimeClock(now);
        runtime.Replace(TacticalCommanderMode.Active, objectives, Intent, new ForceAvailabilitySnapshot(3000f, 0.3f), new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));
        runtime.StoreDoctrineForCommandStates(States(), BattleStance.Attacker);
        return runtime.CurrentOperation;
    }

    private static void FormingSurvivesAFogTick()
    {
        var runtime = new TacticalOperationsLedgerRuntime();
        OperationRecord formed = Tick(runtime, 100f, Map("Island_Ford"), EnemyLine(5200f));
        AssertEqual("enemy-line-0", formed.PrimaryObjectiveId, "the side works the contact it perceives");
        AssertEqual(TacticalOperationPhase.Forming, formed.Phase, "an enemy stronger than the force: forming");
        OperationRecord fog = Tick(runtime, 136f, Map("Island_Ford"));
        AssertEqual("enemy-line-0", fog.PrimaryObjectiveId, "one tick without a glimpse does not send it back to the empty objective");
        AssertEqual(TacticalOperationPhase.Forming, fog.Phase, "still forming");
        OperationRecord seen = Tick(runtime, 172f, Map("Island_Ford"), EnemyLine(5200f));
        AssertEqual(TacticalOperationPhase.Forming, seen.Phase, "and when the line is seen again it is the same operation");
    }

    private static void FormingEndsAfterItsWindow()
    {
        var runtime = new TacticalOperationsLedgerRuntime();
        OperationRecord formed = Tick(runtime, 100f, Map("Island_Ford"), EnemyLine(5200f));
        float window = formed.MinimumCommitSeconds;
        AssertTrue(window > 100f, "forming carries a commit window (" + window + ")");
        OperationRecord later = Tick(runtime, window + 1f, Map("Island_Ford"));
        AssertEqual(TacticalOperationPhase.Scouting, later.Phase, "the enemy has not been seen for the whole window: the side scouts again");
    }

    private static void ScoutingIsReselectedEveryUpdate()
    {
        var runtime = new TacticalOperationsLedgerRuntime();
        OperationRecord scouting = Tick(runtime, 100f, Map("Island_Ford"));
        AssertEqual(TacticalOperationPhase.Scouting, scouting.Phase, "no contact: scouting");
        OperationRecord met = Tick(runtime, 136f, Map("Island_Ford"), EnemyLine(5200f));
        AssertTrue(met.Phase != TacticalOperationPhase.Scouting, "the enemy appears and the very next update leaves Scouting: " + met.Phase);
        AssertEqual("enemy-line-0", met.PrimaryObjectiveId, "for the contact");
    }
}
