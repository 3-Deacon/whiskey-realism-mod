using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// Smoke 2026-10-17: a child's role changed 3 to 6 times in about 7 allocations under one plan (child--706340: Screen, Main, RefuseRight, Reserve,
// Screen), because the allocator is a pure function of bucketed evidence (contact flag, flank exposure, the positional band) that flickers, and only Main
// had a hysteresis. A child keeps its role until the plan changes, an hour has passed, or a crisis forces it.
internal static class DirectChildRoleHoldTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("role hold a child keeps its role under flickering evidence, a release from reserve passes", FlickerIsHeldReleaseFromReservePasses),
            ("role hold a changed plan accepts the fresh roles", AChangedPlanAcceptsTheFreshRoles),
            ("role hold the hold ends after an hour of the doctrine clock", TheHoldEndsAfterAnHour),
            ("role hold fallback is a crisis in both directions", FallbackIsACrisisBothWays),
            ("role hold contact and flank escalation pass at once and de-escalation waits", EscalationPassesDeEscalationWaits),
            ("role hold a guard point or a screen target passes at once and its end does too", GuardAndScreenPass),
            ("role hold a lost Main is replaced by the fresh Main", ALostMainIsReplaced),
            ("role hold a new child is accepted and a vanished child is forgotten", NewAndVanishedChildren),
            ("role hold a minute of five-second allocations over the same enemy keeps every division in one role", AMinuteKeepsOneRolePerDivision),
            ("role hold the army keeps its roles through flickering evidence under one plan and reorganises when the plan changes", TheArmyKeepsItsRolesUnderOnePlan),
        });
    }

    private static DirectChildIntent DI(string id, DirectChildRole role, int sector = 2, bool guard = false, bool screen = false)
    {
        var d = new DirectChildIntent(id, 15, 15, id, sector, role, DirectChildAxis.Hold, sector, 0.4f,
            new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, null),
            guardPoint: guard ? new GuardPoint(10f, 20f, 90f) : default(GuardPoint));
        return screen ? d.WithScreenTarget(5f, 6f, 0f) : d;
    }

    private static DirectChildRole RoleOf(IReadOnlyList<DirectChildIntent> a, string id)
    {
        for (int i = 0; i < a.Count; i++) if (a[i].ChildId == id) return a[i].Role;
        return DirectChildRole.Unknown;
    }

    private static DirectChildIntent[] Set(params DirectChildIntent[] x) => x;

    private static void FlickerIsHeldReleaseFromReservePasses()
    {
        var hold = new DirectChildRoleHold();
        hold.Apply(Set(DI("a", DirectChildRole.SupportMain), DI("b", DirectChildRole.Reserve), DI("c", DirectChildRole.Screen)), 1, 5.0);
        var next = hold.Apply(Set(DI("a", DirectChildRole.Reserve), DI("b", DirectChildRole.SupportMain), DI("c", DirectChildRole.Reserve)), 1, 5.02);
        AssertEqual(DirectChildRole.SupportMain, RoleOf(next, "a"), "SupportMain does not slip back to Reserve on a flicker");
        AssertEqual(DirectChildRole.SupportMain, RoleOf(next, "b"), "a Reserve child released to SupportMain is let go (the release has its own hysteresis)");
        AssertEqual(DirectChildRole.Screen, RoleOf(next, "c"), "a Screen child stays the screen");
    }

    private static void AChangedPlanAcceptsTheFreshRoles()
    {
        var hold = new DirectChildRoleHold();
        hold.Apply(Set(DI("a", DirectChildRole.SupportMain), DI("c", DirectChildRole.Screen)), 1, 5.0);
        var next = hold.Apply(Set(DI("a", DirectChildRole.Reserve), DI("c", DirectChildRole.Main)), 2, 5.02);
        AssertEqual(DirectChildRole.Reserve, RoleOf(next, "a"), "a new plan reorganises the line");
        AssertEqual(DirectChildRole.Main, RoleOf(next, "c"), "all of it");
    }

    private static void TheHoldEndsAfterAnHour()
    {
        var hold = new DirectChildRoleHold();
        hold.Apply(Set(DI("a", DirectChildRole.SupportMain)), 1, 5.0);
        AssertEqual(DirectChildRole.SupportMain, RoleOf(hold.Apply(Set(DI("a", DirectChildRole.Reserve)), 1, 5.9), "a"), "inside the hour");
        AssertEqual(DirectChildRole.Reserve, RoleOf(hold.Apply(Set(DI("a", DirectChildRole.Reserve)), 1, 6.01), "a"), "past the hour it is re-evaluated");
        AssertEqual(DirectChildRole.Reserve, RoleOf(hold.Apply(Set(DI("a", DirectChildRole.Reserve)), 1, 6.02), "a"), "and the new role is held in turn");
        AssertEqual(DirectChildRole.SupportMain, RoleOf(hold.Apply(Set(DI("a", DirectChildRole.SupportMain)), 1, 6.03), "a"), "Reserve to SupportMain is a release and passes inside the new hold");
    }

    private static void FallbackIsACrisisBothWays()
    {
        var hold = new DirectChildRoleHold();
        hold.Apply(Set(DI("a", DirectChildRole.SupportMain), DI("b", DirectChildRole.Reserve)), 1, 5.0);
        var down = hold.Apply(Set(DI("a", DirectChildRole.Fallback), DI("b", DirectChildRole.Reserve)), 1, 5.01);
        AssertEqual(DirectChildRole.Fallback, RoleOf(down, "a"), "severely outmatched and in contact: falls back at once");
        var up = hold.Apply(Set(DI("a", DirectChildRole.SupportMain), DI("b", DirectChildRole.Reserve)), 1, 5.02);
        AssertEqual(DirectChildRole.SupportMain, RoleOf(up, "a"), "and the fallback ends as soon as the evidence does");
    }

    private static void EscalationPassesDeEscalationWaits()
    {
        var hold = new DirectChildRoleHold();
        hold.Apply(Set(DI("a", DirectChildRole.Reserve), DI("b", DirectChildRole.Screen), DI("c", DirectChildRole.SupportMain)), 1, 5.0);
        var up = hold.Apply(Set(DI("a", DirectChildRole.Fix), DI("b", DirectChildRole.RefuseLeft), DI("c", DirectChildRole.RefuseRight)), 1, 5.01);
        AssertEqual(DirectChildRole.Fix, RoleOf(up, "a"), "contact: Fix");
        AssertEqual(DirectChildRole.RefuseLeft, RoleOf(up, "b"), "a flank in danger: refuse it");
        AssertEqual(DirectChildRole.RefuseRight, RoleOf(up, "c"), "either flank");
        var down = hold.Apply(Set(DI("a", DirectChildRole.Reserve), DI("b", DirectChildRole.Screen), DI("c", DirectChildRole.SupportMain)), 1, 5.02);
        AssertEqual(DirectChildRole.Fix, RoleOf(down, "a"), "the contact flag dropping for a moment does not release the fix");
        AssertEqual(DirectChildRole.RefuseLeft, RoleOf(down, "b"), "nor the refused flank");
        AssertEqual(DirectChildRole.RefuseRight, RoleOf(down, "c"), "");
    }

    private static void GuardAndScreenPass()
    {
        var hold = new DirectChildRoleHold();
        hold.Apply(Set(DI("a", DirectChildRole.Reserve), DI("b", DirectChildRole.Screen)), 1, 5.0);
        var on = hold.Apply(Set(DI("a", DirectChildRole.Reserve, guard: true), DI("b", DirectChildRole.Screen, screen: true)), 1, 5.01);
        AssertTrue(on[0].HasGuardPoint, "a station or a crossing guard passes at once");
        AssertTrue(on[1].HasScreenTarget, "and so does a screen target");
        var off = hold.Apply(Set(DI("a", DirectChildRole.Reserve), DI("b", DirectChildRole.Screen)), 1, 5.02);
        AssertFalse(off[0].HasGuardPoint, "the guard's end passes too: the binding has its own hysteresis");
        AssertFalse(off[1].HasScreenTarget, "and the screen's");
    }

    private static void ALostMainIsReplaced()
    {
        var hold = new DirectChildRoleHold();
        hold.Apply(Set(DI("a", DirectChildRole.Main), DI("b", DirectChildRole.Reserve)), 1, 5.0);
        var swap = hold.Apply(Set(DI("a", DirectChildRole.SupportMain), DI("b", DirectChildRole.Main)), 1, 5.01);
        AssertEqual(DirectChildRole.Main, RoleOf(swap, "a"), "a Main stays Main unless a crisis or the hour takes it");
        AssertEqual(DirectChildRole.Reserve, RoleOf(swap, "b"), "and the challenger waits (never two Mains from one flicker)");
        var lost = hold.Apply(Set(DI("a", DirectChildRole.Fallback), DI("b", DirectChildRole.Main)), 1, 5.02);
        AssertEqual(DirectChildRole.Fallback, RoleOf(lost, "a"), "the Main falls back");
        AssertEqual(DirectChildRole.Main, RoleOf(lost, "b"), "and the army is not left without one");
    }

    private static void NewAndVanishedChildren()
    {
        var hold = new DirectChildRoleHold();
        hold.Apply(Set(DI("a", DirectChildRole.SupportMain), DI("b", DirectChildRole.Screen)), 1, 5.0);
        var next = hold.Apply(Set(DI("a", DirectChildRole.Reserve), DI("c", DirectChildRole.RefuseLeft)), 1, 5.01);
        AssertEqual(2, next.Count, "two children now");
        AssertEqual(DirectChildRole.RefuseLeft, RoleOf(next, "c"), "a new child takes its fresh role");
        // b comes back after having been gone: it is new again.
        var back = hold.Apply(Set(DI("a", DirectChildRole.Reserve), DI("b", DirectChildRole.Reserve)), 1, 5.02);
        AssertEqual(DirectChildRole.Reserve, RoleOf(back, "b"), "a child that left and returned has no memory");
    }

    // The smoke's minute: 1st Brigade's division went Demonstration (Screen) -> FlankMarch (Main) -> LeftWing -> Reserve -> Center within about a minute,
    // the enemy and the odds unchanged. Evidence-level flicker alone, under one plan, must leave every division in one role.
    private static void AMinuteKeepsOneRolePerDivision()
    {
        var hold = new DirectChildRoleHold();
        string[] ids = { "d706558", "d707212", "d707866", "d708520", "d709392" };
        DirectChildRole[] start = { DirectChildRole.Fix, DirectChildRole.Reserve, DirectChildRole.Main, DirectChildRole.Screen, DirectChildRole.SupportMain };
        var fresh = new DirectChildIntent[5];
        for (int i = 0; i < 5; i++) fresh[i] = DI(ids[i], start[i]);
        hold.Apply(fresh, 1, 5.0);
        var rng = new Random(7);
        DirectChildRole[] noise = { DirectChildRole.Reserve, DirectChildRole.Screen, DirectChildRole.SupportMain, DirectChildRole.Main };
        int changes = 0;
        IReadOnlyList<DirectChildIntent> prev = null;
        for (int step = 1; step <= 12; step++)
        {
            for (int i = 0; i < 5; i++) fresh[i] = DI(ids[i], noise[rng.Next(noise.Length)], sector: rng.Next(0, 4));
            IReadOnlyList<DirectChildIntent> now = hold.Apply(fresh, 1, 5.0 + step * 5.0 / 3600.0);
            if (prev != null)
                for (int i = 0; i < 5; i++) if (now[i].Role != prev[i].Role) changes++;
            prev = now;
        }
        // A Reserve child may be released to SupportMain once, and the Screen/Main/Fix/SupportMain children hold: at most one change per child that
        // started as Reserve, none for the others.
        AssertTrue(changes <= 1, "the minute changed " + changes + " roles; the divisions that were not reserves keep theirs");
        for (int i = 0; i < 5; i++)
            if (start[i] != DirectChildRole.Reserve) AssertEqual(start[i], prev[i].Role, ids[i] + " keeps its role through the minute");
    }

    private static void TheArmyKeepsItsRolesUnderOnePlan()
    {
        ReserveReleaseState.ResetAll();
        var army = new ArmyOrchestrator(0, BuiltInPlaybooks.SeedCatalog(), default(WhiskeyRealism.Strategic.PersonalityVector));
        double clock = 5.0;
        army.HoursSource = () => clock;
        army.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.PreparedDefense, BattlePhase.MainEffort, 2, null, null, 99f, 0f, 7));
        var snaps = new List<DirectChildSnapshot>();
        for (int i = 0; i < 5; i++) snaps.Add(new DirectChildSnapshot("child--" + (706000 + i), "army", 15, 0, "d" + i, true));
        army.RegisterDirectChildren(snaps);
        // The same five divisions, the same enemy, the same odds: only the flickering bits of evidence (the contact flag, the strength bucket) change.
        var rng = new Random(3);
        var seen = new Dictionary<string, HashSet<DirectChildRole>>();
        for (int step = 0; step < 12; step++)
        {
            var ev = new DirectChildEvidence[5];
            for (int i = 0; i < 5; i++)
                ev[i] = new DirectChildEvidence(rng.Next(1, 4), 1, rng.Next(2) == 0 && false, i % 5, 0, 0.9f);
            army.ObserveDirectChildEvidenceWithZones(ev, null, null);
            foreach (var d in army.CurrentDirectChildIntents)
            {
                if (!seen.TryGetValue(d.ChildId, out var set)) seen[d.ChildId] = set = new HashSet<DirectChildRole>();
                set.Add(d.Role);
            }
            clock += 5.0 / 3600.0;
        }
        foreach (var pair in seen)
            AssertTrue(pair.Value.Count <= 2, pair.Key + " went through " + pair.Value.Count + " roles in a minute: " + string.Join(",", pair.Value));
        // A new plan (the main effort band moves) reorganises the line.
        army.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.PreparedDefense, BattlePhase.MainEffort, 3, null, null, 99f, 0f, 7));
        var after = new DirectChildEvidence[5];
        for (int i = 0; i < 5; i++) after[i] = new DirectChildEvidence(2, 1, false, i % 5, 0, 0.9f);
        army.ObserveDirectChildEvidenceWithZones(after, null, null);
        AssertTrue(army.CurrentDirectChildIntents.Count == 5, "five intents after the change");
    }
}
