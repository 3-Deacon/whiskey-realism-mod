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
}
