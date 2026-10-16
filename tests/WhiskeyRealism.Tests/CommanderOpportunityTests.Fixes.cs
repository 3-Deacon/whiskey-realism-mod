using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;

// BC13 tasks 9-12: the vanilla battle bug fixes (spec § 7). Pure halves and source or real-IL pins; the patches themselves need the game.
internal static partial class CommanderOpportunityTests
{
    private static (string, Action)[] FixTests()
    {
        return new (string, Action)[]
        {
            ("bc13 FIX-CAV every refusal reason and the order they are given in", FixCavReasons),
            ("bc13 FIX-CAV an exploit substitute is taken only within vanilla's cavalry reach", FixCavSubstitute),
            ("bc13 FIX-CAV-MAP the charger and the target map to the snapshot by the game object's instance id; the patch fails open", FixCavMap),
            ("bc13 FIX-CAV-PIN AdjustCavalryBehavior calls GetClosestEnemyUnitReg exactly once, with maxdistancecavalryattack alone", FixCavPin),
            ("bc13 FIX-CAV-SCOPE the four entries carry their scopes and first-fire marks; the patch writes nothing", FixCavScope),
        };
    }

    // ------------------------------------------------------------------ #120 cavalry strike discipline

    private static CavalryStrikeInput GoodShaken()
        => new CavalryStrikeInput
        {
            TargetVisible = true, TargetUnitTyp = TacticalUnitType.Infantry, TargetMorale = 0.3f, ShakenMorale = 0.45f,
            Odds = 2f, OddsTrigger = 1.3f,
        };

    private static string CavReason(CavalryStrikeInput i)
    {
        return CavalryStrikeCheck.Allows(in i, out string reason) ? "allowed" : reason;
    }

    private static void FixCavReasons()
    {
        AssertEqual("allowed", CavReason(GoodShaken()), "a visible shaken unit with favourable odds");

        var hidden = GoodShaken(); hidden.TargetVisible = false;
        AssertEqual("hidden", CavReason(hidden), "a target the side has not perceived");
        var square = GoodShaken(); square.TargetInSquare = true;
        AssertEqual("square", CavReason(square), "square");
        var garrisoned = GoodShaken(); garrisoned.TargetGarrisoned = true;
        AssertEqual("garrisoned", CavReason(garrisoned), "garrisoned");
        var steady = GoodShaken(); steady.TargetMorale = 0.45f;
        AssertEqual("steady", CavReason(steady), "morale at the bar is steady (vanilla's test is strictly below)");
        var odds = GoodShaken(); odds.Odds = 1.3f;
        AssertEqual("odds", CavReason(odds), "odds not above the trigger");
        var noTrigger = GoodShaken(); noTrigger.OddsTrigger = 0f; noTrigger.Odds = 0f;
        AssertEqual("allowed", CavReason(noTrigger), "a trigger of 0 gates nothing");
        var outflanked = GoodShaken(); outflanked.ChargerBarredByOutflank = true;
        AssertEqual("outflanked", CavReason(outflanked), "the charger is outflanked");

        var battery = new CavalryStrikeInput { TargetVisible = true, TargetUnitTyp = TacticalUnitType.Artillery, TargetMorale = 0.9f, ShakenMorale = 0.45f };
        AssertEqual("allowed", CavReason(battery), "an unsupported battery is charged whatever its morale");
        battery.TargetSupported = true;
        AssertEqual("supported", CavReason(battery), "a battery with infantry or cavalry within 250 m");
        battery.TargetGarrisoned = true;
        AssertEqual("garrisoned", CavReason(battery), "garrisoned comes before supported");
        AssertEqual(250f, CavalryStrikeCheck.SupportMeters, "the support radius of the unsupported-battery test");

        // the order: hidden, square, garrisoned, kind (supported / steady / odds), outflanked
        var all = GoodShaken();
        all.TargetVisible = false; all.TargetInSquare = true; all.TargetGarrisoned = true; all.TargetMorale = 0.9f; all.Odds = 0f; all.ChargerBarredByOutflank = true;
        AssertEqual("hidden", CavReason(all), "hidden first");
        all.TargetVisible = true;
        AssertEqual("square", CavReason(all), "then square");
        all.TargetInSquare = false;
        AssertEqual("garrisoned", CavReason(all), "then garrisoned");
        all.TargetGarrisoned = false;
        AssertEqual("steady", CavReason(all), "then the target's own kind");
        all.TargetMorale = 0.3f;
        AssertEqual("odds", CavReason(all), "odds");
        all.Odds = 5f;
        AssertEqual("outflanked", CavReason(all), "outflanked last");
        AssertEqual("exploit-target", CavalryStrikeCheck.ExploitTarget, "the telemetry reason of a substituted target");
    }

    private static void FixCavSubstitute()
    {
        AssertTrue(CavalryStrikeCheck.SubstituteInReach(89.9f, 90f), "inside vanilla's reach");
        AssertFalse(CavalryStrikeCheck.SubstituteInReach(90f, 90f), "vanilla's test is strictly under maxdistancecavalryattack");
        AssertFalse(CavalryStrikeCheck.SubstituteInReach(150f, 90f), "beyond it");
        AssertFalse(CavalryStrikeCheck.SubstituteInReach(-1f, 90f), "a distance below zero is no distance");
    }

    private static string CavalrySource() => ReadSrc("Patches/CavalryStrikeDisciplinePatch.cs");

    private static void FixCavMap()
    {
        string src = CavalrySource();
        AssertContains(src, "snapshot.IndexOfOwn(__instance.gameObject.GetInstanceID())", "the charger maps by the game object's instance id");
        AssertContains(src, "int targetId = target.gameObject.GetInstanceID();", "the target maps the same way");
        AssertContains(src, "obs[found].State == ObservationState.Visible", "only a Visible observation keeps the pick");
        AssertContains(src, "if (charger < 0) return;", "a charger missing from the snapshot fails open");
        AssertContains(src, "if (!BattleCore.TryGetSnapshot(__instance.alliance, out BattleSnapshot snapshot) || snapshot == null) return;", "no snapshot fails open");
        AssertContains(src, "Plugin.EnableBattleFogHonestPerception", "perception off fails open");
        AssertContains(src, "catch (Exception ex)", "an exception fails open");
        int postfix = src.IndexOf("internal static void Postfix(Regiment __instance, ref Regiment __result)", StringComparison.Ordinal);
        string body = src.Substring(postfix);
        int guard = body.IndexOf("TacticalWriteGuard.CanWrite(__instance)", StringComparison.Ordinal);
        int feud = body.IndexOf("TacticalWriteGuard.IsFeudInstance(battle)", StringComparison.Ordinal);
        int snap = body.IndexOf("BattleCore.TryGetSnapshot(", StringComparison.Ordinal);
        AssertTrue(guard > 0 && feud > 0 && snap > guard && snap > feud, "the player, feud and mode guards come before the snapshot is read");
        AssertContains(body, "__result = null;", "a refused pick becomes null (vanilla's 5050 test skips the charge)");
    }

    // The real IL: exactly one call to Regiment.GetClosestEnemyUnitReg in AdjustCavalryBehavior, its first argument
    // GamePrefs.maxdistancecavalryattack and its second null (5049), so the patch's depth flag covers that one call only.
    private static void FixCavPin()
    {
        var method = AccessTools.Method(typeof(AIBattle), "AdjustCavalryBehavior");
        AssertTrue(method != null, "AdjustCavalryBehavior exists");
        var target = AccessTools.Method(typeof(Regiment), "GetClosestEnemyUnitReg", new[] { typeof(float), typeof(int[]) });
        AssertTrue(target != null, "GetClosestEnemyUnitReg(float, int[]) exists");
        var code = PatchProcessor.GetOriginalInstructions(method);
        int calls = 0;
        for (int i = 0; i < code.Count; i++)
        {
            if (!(code[i].opcode == OpCodes.Call || code[i].opcode == OpCodes.Callvirt)) continue;
            if (!(code[i].operand is MethodInfo mi) || mi.Name != "GetClosestEnemyUnitReg") continue;
            calls++;
            AssertTrue(i >= 2, "arguments precede the call");
            AssertTrue(code[i - 1].opcode == OpCodes.Ldnull, "the second argument is null");
            AssertTrue(code[i - 2].opcode == OpCodes.Ldsfld && code[i - 2].operand is FieldInfo f && f.Name == "maxdistancecavalryattack",
                "the first argument is GamePrefs.maxdistancecavalryattack alone");
        }
        AssertEqual(1, calls, "exactly one GetClosestEnemyUnitReg call in AdjustCavalryBehavior");
    }

    private static void FixCavScope()
    {
        string src = CavalrySource();
        foreach (string id in new[] { "adjust-prefix", "adjust-postfix", "adjust-finalizer", "target-postfix" })
        {
            string full = "tactical.patch.cavalry-strike." + id;
            AssertContains(src, "TelemetryPerf.Scope(\"" + full + "\", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0)", id + " scope at 2.0 ms");
            AssertContains(src, "PatchFirstFire.Mark(\"" + full + "\");", id + " first-fire mark");
        }
        AssertContains(src, "[HarmonyPriority(Priority.First)]", "the clearing Postfix runs first");
        AssertContains(src, "[HarmonyFinalizer]", "and a Finalizer clears on an exception");
        AssertFalse(src.Contains(".AssignChargeTarget(") || src.Contains(".SetWaypoint(") || src.Contains(".ChangeStance(") || src.Contains(".SetGroupFormation("),
            "Whiskey never charges or orders: a steer of vanilla's own input");
        AssertContains(src, "Plugin.CavalryStrikeDisciplineEnabled()", "the rollback flag is read on every call");
    }
}
