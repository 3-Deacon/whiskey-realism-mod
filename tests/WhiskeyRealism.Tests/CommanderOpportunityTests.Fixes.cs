using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Battle.BugFixes;
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
            ("bc13 FIX-STANCE a stance vanilla changed is re-stamped to now + 2 T and skipped until now + T", FixStance),
            ("bc13 FIX-STANCE-WHISKEY a stamp Whiskey wrote after the re-stamp is Whiskey's and is never released by the fix", FixStanceWhiskey),
            ("bc13 FIX-STANCE-PIN vanilla's 4264 comparison and 4275 stamp are unchanged in the real IL", FixStancePin),
            ("bc13 FIX-STANCE-TRACE the delay a re-stamp costs a weak group lands in the histogram", FixStanceTrace),
            ("bc13 FIX-STANCE-FEUD the feud instance and a non-orchestrated side are untouched; every Commander Mode runs it", FixStanceFeud),
            ("bc13 FIX-STANCE-RETREAT an army retreat releases the fix's stamps once and re-stamps nothing", FixStanceRetreat),
            ("bc13 FIX-RENEW the charge doctrine reads timetorenewaichargecheck, so vanilla's dead renew test needs no fix", FixRenew),
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

    // ------------------------------------------------------------------ #119 stance cooldown

    private sealed class FakeStamps : IStanceStampAccess
    {
        public readonly System.Collections.Generic.Dictionary<int, float> Stamps = new System.Collections.Generic.Dictionary<int, float>();
        public float Get(int groupId) => Stamps.TryGetValue(groupId, out float v) ? v : float.NaN;
        public void Set(int groupId, float stamp) => Stamps[groupId] = stamp;
    }

    private static void FixStance()
    {
        AssertNear(12f, StanceCooldownRestamp.Restamp(10f, 1f), 1e-6f, "now + 2 x T");
        AssertNear(10f, StanceCooldownRestamp.Restamp(10f, -3f), 1e-6f, "a bad T restamps to now");
        // Vanilla skips (stamp >= now + T) until now + T, then re-decides.
        AssertTrue(StanceCooldownRestamp.VanillaSkips(12f, 10.5f, 1f), "skipped inside the hour");
        AssertTrue(StanceCooldownRestamp.VanillaSkips(12f, 10.99f, 1f), "and at its end");
        AssertTrue(StanceCooldownRestamp.VanillaSkips(12f, 11.0f, 1f), "the hour itself is still held (the test is stamp < now + T)");
        AssertFalse(StanceCooldownRestamp.VanillaSkips(12f, 11.01f, 1f), "after now + T vanilla may change it again");
        AssertFalse(StanceCooldownRestamp.VanillaSkips(10f, 10.5f, 1f), "an unstamped group (stamp = the change, no fix) is re-decided at once: the bug");

        AssertTrue(StanceCooldownRestamp.VanillaChanged(3, 2, 10f, 10f), "ordered stance changed and stamped with the raw clock: vanilla's change");
        AssertFalse(StanceCooldownRestamp.VanillaChanged(3, 3, 10f, 10f), "same stance");
        AssertFalse(StanceCooldownRestamp.VanillaChanged(3, 2, 11.25f, 10f), "a Whiskey stamp in the future is not vanilla's");
        AssertFalse(StanceCooldownRestamp.VanillaChanged(3, 2, 9.5f, 10f), "nor a change vanilla made in an earlier call");

        var book = new StanceRestampBook();
        book.RecordBefore(1, 3);
        AssertTrue(book.TryBefore(1, out int before) && before == 3, "the Prefix's record");
        for (int k = 0; k < 3; k++)
        {
            book.NoteVanillaChange(1, 10.0 + k * 0.25);
            book.NoteRestamp(1, StanceCooldownRestamp.Restamp(10f + k * 0.25f, 1f));
        }
        AssertEqual(3, book.VanillaChanges, "three changes counted");
        AssertEqual(3, book.Restamped, "and re-stamped");
        AssertEqual(3, book.MaxChangesPerGroupHour, "the most one group changed within an hour");
        book.NoteVanillaChange(1, 12.0);
        AssertEqual(3, book.MaxChangesPerGroupHour, "an hour later the window has moved on");
        AssertTrue(book.TryWritten(1, out float written) && Math.Abs(written - 12.5f) < 1e-6f, "the last written stamp is kept");
    }

    private static void FixStanceWhiskey()
    {
        var book = new StanceRestampBook();
        var stamps = new FakeStamps();
        book.NoteRestamp(1, 12f);
        book.NoteRestamp(2, 12f);
        stamps.Stamps[1] = 12f;                   // still the fix's
        stamps.Stamps[2] = TacticalStanceOwnership.VanillaHoldStamp(10f, TacticalStanceOwnership.HoldHours, 1f);   // Whiskey's writer stamped it since
        int released = book.ReleaseOnRetreat(10.5f, stamps);
        AssertEqual(1, released, "only the fix's own stamp is released");
        AssertNear(TacticalStanceOwnership.ReleaseStamp(10.5f), stamps.Stamps[1], 1e-6f, "to the release stamp");
        AssertNear(TacticalStanceOwnership.VanillaHoldStamp(10f, TacticalStanceOwnership.HoldHours, 1f), stamps.Stamps[2], 1e-6f, "Whiskey's stamp is left to ReleaseForRetreat");
        AssertEqual(0, book.RestampedCount, "the set is cleared either way");
    }

    private static void FixStancePin()
    {
        var method = AccessTools.Method(typeof(AIBattle), "AdjustGroupAIStance");
        AssertTrue(method != null, "AdjustGroupAIStance exists");
        var code = PatchProcessor.GetOriginalInstructions(method);
        int resets = 0, stamps = 0;
        for (int i = 0; i < code.Count; i++)
        {
            if (code[i].opcode == OpCodes.Ldsfld && code[i].operand is FieldInfo f && f.Name == "updatetimeforresettingofaistance")
            {
                resets++;
                AssertTrue(HasWithin(code, i + 1, i + 2, OpCodes.Add), "the comparison adds T to the clock (now + T)");
                AssertTrue(HasClock(code, i - 3, i - 1), "after the raw clock");
            }
            if (code[i].opcode == OpCodes.Stfld && code[i].operand is FieldInfo s && s.Name == "lastaistancechangetime")
            {
                stamps++;
                AssertTrue(HasClock(code, i - 3, i - 1), "vanilla stamps the raw clock on a change");
            }
        }
        AssertEqual(1, resets, "one use of updatetimeforresettingofaistance (the inverted test)");
        AssertEqual(1, stamps, "one stamp write");
    }

    private static bool HasWithin(System.Collections.Generic.IList<CodeInstruction> code, int from, int to, OpCode op)
    {
        for (int k = Math.Max(0, from); k <= to && k < code.Count; k++) if (code[k].opcode == op) return true;
        return false;
    }

    private static bool HasClock(System.Collections.Generic.IList<CodeInstruction> code, int from, int to)
    {
        for (int k = Math.Max(0, from); k <= to && k < code.Count; k++)
            if (code[k].opcode == OpCodes.Ldsfld && code[k].operand is FieldInfo f && f.Name == "currenttimefromstart") return true;
        return false;
    }

    private static void FixStanceTrace()
    {
        var book = new StanceRestampBook();
        book.NoteWant(1, 10.0);
        book.NoteWant(1, 10.4);   // the first hour is kept
        book.ResolveWant(1, 10.1);
        AssertEqual(1, book.DelayUnder025, "0.1 h");
        book.NoteWant(2, 10.0); book.ResolveWant(2, 10.3);
        book.NoteWant(3, 10.0); book.ResolveWant(3, 10.75);
        book.NoteWant(4, 10.0); book.ResolveWant(4, 11.5);
        AssertEqual(1, book.Delay025To05, "0.3 h");
        AssertEqual(1, book.Delay05To1, "0.75 h");
        AssertEqual(1, book.DelayOver1, "1.5 h");
        book.NoteWant(5, 10.0); book.ClearWant(5); book.ResolveWant(5, 10.2);
        AssertEqual(1, book.DelayUnder025, "a cleared want (the group no longer wants the change) records nothing");
        book.ResolveWant(99, 10.0);
        AssertEqual(4, book.DelayUnder025 + book.Delay025To05 + book.Delay05To1 + book.DelayOver1, "and neither does a group that never waited");
    }

    private static string StanceSource() => ReadSrc("Patches/VanillaStanceCooldownFixPatch.cs");

    private static void FixStanceFeud()
    {
        string src = StanceSource();
        AssertContains(src, "TacticalBattleCoordinator.DrivesOrchestrator(battle.isplayeraiorfeud) || TacticalWriteGuard.IsFeudInstance(battle)", "the feud instance is skipped");
        AssertFalse(src.Contains("ModeAllowsWrites"), "no Commander Mode gate: a vanilla bug fix runs in every mode");
        AssertContains(src, "Plugin.VanillaStanceCooldownFixEnabled()", "the rollback flag");
        foreach (string id in new[] { "prefix", "postfix" })
        {
            string full = "tactical.patch.stance-cooldown." + id;
            AssertContains(src, "TelemetryPerf.Scope(\"" + full + "\", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0)", id + " scope");
            AssertContains(src, "PatchFirstFire.Mark(\"" + full + "\");", id + " first-fire mark");
        }
        AssertFalse(src.Contains(".ChangeStance(") || src.Contains("TacticalStanceWriter.Write"), "the fix writes the stamp only, never a stance");
    }

    private static void FixStanceRetreat()
    {
        var book = new StanceRestampBook();
        var stamps = new FakeStamps();
        book.NoteRestamp(1, 12f); stamps.Stamps[1] = 12f;
        AssertEqual(1, book.ReleaseOnRetreat(10.5f, stamps), "the first call with macroai 3 releases");
        AssertNear(TacticalStanceOwnership.ReleaseStamp(10.5f), stamps.Stamps[1], 1e-6f, "to the release stamp");
        book.NoteRestamp(1, 13f); stamps.Stamps[1] = 13f;
        AssertEqual(0, book.ReleaseOnRetreat(10.6f, stamps), "later calls with macroai 3 release nothing");
        AssertNear(13f, stamps.Stamps[1], 1e-6f, "and touch nothing");
        book.NoteNotRetreating();
        book.NoteRestamp(1, 13f);
        AssertEqual(1, book.ReleaseOnRetreat(20f, stamps), "a later retreat releases again");
        AssertEqual(2, book.Released, "released total");
        string src = StanceSource();
        AssertContains(src, "if (!Applies(__instance, out StanceRestampBook book, out int side) || __instance.macroai == 3) return;", "the Postfix re-stamps nothing while macroai is 3");
        AssertContains(src, "book.ReleaseOnRetreat(now, Stamps)", "the Prefix pulls the stamps back before vanilla runs");
    }

    private static void FixRenew()
    {
        string charge = ReadSrc("Tactical/Battle/Producers/ChargeIntentProducer.cs");
        AssertContains(charge, "GamePrefs.timetorenewaichargecheck", "the charge producer reads timetorenewaichargecheck");
        AssertContains(ReadSrc("Tactical/Battle/ChargeDoctrine.cs"), "RefusalBackoffSeconds(float vanillaRenewHours)", "and the doctrine turns it into the refusal back-off");
    }
}
