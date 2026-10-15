using System;
using WhiskeyRealism.Strategic;

// WS C2a — campaign military write surfaces (2026-10-14 remediation plan).
internal static class CampaignMilitaryFixTests
{
    public static void Run()
    {
        SeaInvasionStrengthResolvesInstanceOverload();
        SeaInvasionLandedSignalUsesEnemyBalance();
        WlCampaignContextSourcesAreRealVanillaFields();
        WlCampaignContextKeyDistinguishesCareersAndSaves();
        ListSnapshotRestoreUndoesVanillaMutation();
        ListSnapshotRestoreIsIdempotent();
        TransferRequirementIsLocalNotTheaterWide();
        TransferSteersOnlyWhenPlanShortfallIsWorse();
        ImportanceRedirectHonoursVanillaCandidateGates();
        ImportanceRedirectStaysInPlanTheater();
    }

    private static void LogPass(string message)
    {
        Console.WriteLine("PASS " + message);
    }

    // C-DefIntent: vanilla SeaInvasion has two GetUnitStrengthInRange overloads
    // (decompile 141061 instance, 141066 static). An untyped AccessTools lookup
    // throws AmbiguousMatchException; the typed lookup must hit the instance one.
    private static void SeaInvasionStrengthResolvesInstanceOverload()
    {
        var method = CampaignMilitaryGates.ResolveSeaInvasionStrengthMethod();
        TestHarness.AssertTrue(method != null, "instance overload resolved");
        TestHarness.AssertFalse(method.IsStatic, "resolved overload is the instance method");
        var parameters = method.GetParameters();
        TestHarness.AssertEqual(3, parameters.Length, "parameter count");
        TestHarness.AssertEqual(typeof(int), parameters[0].ParameterType, "allianceid type");
        TestHarness.AssertEqual(typeof(bool), parameters[1].ParameterType, "returnbalance type");
        TestHarness.AssertEqual(typeof(int), parameters[2].ParameterType, "shipweight type");
        LogPass("c2a sea invasion strength resolves typed instance overload");
    }

    // C-DefIntent: with returnbalance=false vanilla sums OWN units too (141066+),
    // so own garrisons at the spot would read as a landing. Vanilla's own callers
    // (9238, 9433) always use returnbalance=true: enemy minus friendly.
    private static void SeaInvasionLandedSignalUsesEnemyBalance()
    {
        var args = CampaignMilitaryGates.SeaInvasionBalanceArgs(1);
        TestHarness.AssertEqual(1, (int)args[0], "defender alliance passed");
        TestHarness.AssertEqual(true, (bool)args[1], "returnbalance must be true");
        TestHarness.AssertEqual(0, (int)args[2], "ships weigh nothing for a land landing");
        TestHarness.AssertTrue(CampaignMilitaryGates.IsLandedByStrengthBalance(1), "enemy outweighs us -> landed");
        TestHarness.AssertFalse(CampaignMilitaryGates.IsLandedByStrengthBalance(0), "balanced -> not landed");
        TestHarness.AssertFalse(CampaignMilitaryGates.IsLandedByStrengthBalance(-500), "we outweigh -> not landed");
        LogPass("c2a sea invasion landed signal uses enemy-minus-friendly balance");
    }

    // C-DeadRefl-scene: SceneManagement.currentcampaign / currentsave do not
    // exist; the context key collapsed to "campaign:a:f::" for every career.
    private static void WlCampaignContextSourcesAreRealVanillaFields()
    {
        var sources = WlStrategicOrderBridge.CampaignContextSources;
        TestHarness.AssertEqual(4, sources.Length, "scenario, level, sublevel, save folder");
        foreach (var source in sources)
        {
            var field = source.Owner.GetField(source.Field,
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            TestHarness.AssertTrue(field != null, source.Owner.Name + "." + source.Field + " exists");
            TestHarness.AssertEqual(typeof(string), field.FieldType, source.Field + " is a string");
        }
        LogPass("c2a wl campaign context key reads real vanilla fields");
    }

    private static void WlCampaignContextKeyDistinguishesCareersAndSaves()
    {
        string a = WlStrategicOrderBridge.BuildCampaignContextKey(0, 1, "002", "002", "A", "Save");
        string sameAgain = WlStrategicOrderBridge.BuildCampaignContextKey(0, 1, "002", "002", "A", "Save");
        string otherCareer = WlStrategicOrderBridge.BuildCampaignContextKey(0, 1, "002", "002", "B", "Save");
        string otherSave = WlStrategicOrderBridge.BuildCampaignContextKey(0, 1, "002", "002", "A", "Save3");
        string otherScenario = WlStrategicOrderBridge.BuildCampaignContextKey(0, 1, "001", "001", "A", "Save");
        TestHarness.AssertEqual(a, sameAgain, "stable for same context");
        TestHarness.AssertTrue(a != otherCareer, "career sublevel changes key");
        TestHarness.AssertTrue(a != otherSave, "save folder changes key");
        TestHarness.AssertTrue(a != otherScenario, "scenario changes key");
        LogPass("c2a wl campaign context key distinguishes careers and saves");
    }

    // B-46-campaign: the ownunits restore moves into a [HarmonyFinalizer] so it
    // runs even when vanilla CheckForDefensiveOperations throws. The restore
    // itself must be exact and safe to run twice (Postfix + Finalizer paths).
    private static void ListSnapshotRestoreUndoesVanillaMutation()
    {
        var live = new System.Collections.ArrayList { "a", "c" };
        var snapshot = new System.Collections.Generic.List<object> { "a", "b", "c" };
        TestHarness.AssertTrue(CampaignMilitaryGates.RestoreListSnapshot(live, snapshot), "restore reports success");
        TestHarness.AssertEqual(3, live.Count, "restored count");
        TestHarness.AssertEqual("b", (string)live[1], "restored original order");
        LogPass("c2a list snapshot restore undoes vanilla mutation");
    }

    private static void ListSnapshotRestoreIsIdempotent()
    {
        var live = new System.Collections.ArrayList { "x" };
        var snapshot = new System.Collections.Generic.List<object> { "a", "b" };
        CampaignMilitaryGates.RestoreListSnapshot(live, snapshot);
        CampaignMilitaryGates.RestoreListSnapshot(live, snapshot);
        TestHarness.AssertEqual(2, live.Count, "second restore keeps exact contents");
        TestHarness.AssertFalse(CampaignMilitaryGates.RestoreListSnapshot(null, snapshot), "null live list is a no-op");
        TestHarness.AssertFalse(CampaignMilitaryGates.RestoreListSnapshot(live, null), "missing snapshot is a no-op");
        TestHarness.AssertEqual(2, live.Count, "missing snapshot leaves list alone");
        LogPass("c2a list snapshot restore is idempotent and null-safe");
    }

    // C-Transfer: the plan requirement used totalOwn * ForceFractionRequired
    // (up to 0.95 of the whole army), so the plan target always "needed"
    // more than any vanilla deficit and the threatened zone never got troops.
    private static void TransferRequirementIsLocalNotTheaterWide()
    {
        float undefended = CampaignMilitaryGates.PlanTransferRequiredAtTarget(totalOwn: 200000f, forceFraction: 0.8f, enemyNear: 0f);
        TestHarness.AssertEqual(2500f, undefended, "no enemy near target -> token presence");
        float contested = CampaignMilitaryGates.PlanTransferRequiredAtTarget(200000f, 0.8f, 20000f);
        TestHarness.AssertEqual(25000f, contested, "local superiority over enemy near target");
        float capped = CampaignMilitaryGates.PlanTransferRequiredAtTarget(30000f, 0.5f, 40000f);
        TestHarness.AssertEqual(15000f, capped, "never more than the plan's commitment share");
        LogPass("c2a transfer requirement is local, capped by commitment share");
    }

    private static void TransferSteersOnlyWhenPlanShortfallIsWorse()
    {
        // vanilla CheckTransferOfUnits severity = deficit.enemystrength - deficit.ownstrength (17275)
        TestHarness.AssertTrue(CampaignMilitaryGates.ShouldSteerTransfer(false, 0f, 0f, 5000f), "no vanilla deficit -> steer");
        TestHarness.AssertTrue(CampaignMilitaryGates.ShouldSteerTransfer(true, 1000f, 3000f, 5000f), "vanilla deficit not a shortfall -> steer");
        TestHarness.AssertTrue(CampaignMilitaryGates.ShouldSteerTransfer(true, 12000f, 8000f, 5000f), "plan shortfall worse -> steer");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldSteerTransfer(true, 20000f, 5000f, 5000f), "threatened zone worse -> vanilla keeps its deficit");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldSteerTransfer(false, 0f, 0f, 0f), "no plan shortfall -> nothing to steer");
        LogPass("c2a transfer steers only when plan shortfall exceeds vanilla deficit");
    }

    // R7: ImportanceValuesPatch overwrote mostvalueableaiareaclose for EVERY
    // source area, bypassing vanilla's candidate gates in
    // CalculateMostValueableAIZones (decompile 10964-10987).
    private static void ImportanceRedirectHonoursVanillaCandidateGates()
    {
        // baseline: same theater, in range, supply ok, vanilla value clause ok
        TestHarness.AssertTrue(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, Theater.East, Theater.East, 200f, 300f, 10f, 50f, true), "eligible target redirects");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            true, Theater.East, Theater.East, 0f, 300f, 10f, 50f, true), "aiarea[i] != this");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, Theater.East, Theater.East, 301f, 300f, 10f, 50f, true), "distance gate (relocation range x offensive range factor)");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, Theater.East, Theater.East, 200f, 300f, 60f, 50f, true), "supply gate ownpoints <= supplycapacity");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, Theater.East, Theater.East, 200f, 300f, 10f, 50f, false), "vanilla points/distance/importance clause");
        LogPass("c2a importance redirect honours vanilla candidate gates");
    }

    private static void ImportanceRedirectStaysInPlanTheater()
    {
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, Theater.West, Theater.East, 200f, 300f, 10f, 50f, true), "other front keeps vanilla's local target");
        TestHarness.AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(
            false, Theater.Unknown, Theater.East, 200f, 300f, 10f, 50f, true), "unclassified source keeps vanilla target");
        LogPass("c2a importance redirect stays inside the plan theater");
    }
}
