using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// BC13 task 8: the staggered reserve release (spec § 6, requirement-staggered-reserve-release). One reserve per release interval
// above a floor of held reserves for the Exploit phase and the odds trigger; one reserve per crisis sector, defence first.
internal static partial class CommanderOpportunityTests
{
    private static (string, Action)[] ReserveTests()
    {
        return new (string, Action)[]
        {
            ("bc13 RES-STAGGER an Exploit-phase release frees one reserve per release interval", ResStagger),
            ("bc13 RES-FLOOR a stagger release never takes the held reserves below max(1, ceil(R0 x floorShare))", ResFloor),
            ("bc13 RES-SINGLE a lone reserve may go", ResSingle),
            ("bc13 RES-CRISIS-ONE a crisis releases one reserve per crisis sector, the nearest", ResCrisisOne),
            ("bc13 RES-CRISIS-LATENCY a crisis releases on the first allocation that sees it, below the floor, without moving the interval", ResCrisisLatency),
            ("bc13 RES-CRISIS-HELD a crisis-released reserve counts as not held, so the floor can block the stagger", ResCrisisHeld),
            ("bc13 RES-PERSIST a released reserve stays released while the trigger holds", ResPersist),
            ("bc13 RES-REVERT the released reserves revert and the state clears when the trigger stops", ResRevert),
            ("bc13 RES-EXPLOITING-SKIPPED a reserve with a live exploit is skipped for the next nearest", ResExploitingSkipped),
            ("bc13 RES-TIE the nearest by sector wins, then the lower index", ResTie),
            ("bc13 RES-OFF without the state every reserve releases as before", ResOff),
            ("bc13 RES-OUTCOMES the outcomes carry held, floor and the crisis floor breach", ResOutcomes),
        };
    }

    internal static TacticalBattlePlan ResPlan(BattlePhase phase = BattlePhase.Exploit)
        => new TacticalBattlePlan(BattlePlanId.GenericAggressive, phase, mainEffortSector: 2, fixingSectors: new int[0], screeningSectors: new int[0],
            reserveCommitTriggerOdds: 50f, ageSeconds: 0f, jitterSeed: 0);

    // c0 the main effort (sector 2, in contact); the rest are deployed reserves at the given sectors.
    internal static DirectChildEvidence[] ResEvidence(params int[] reserveSectors)
    {
        var ev = new List<DirectChildEvidence> { new DirectChildEvidence(3, 1, true, 2, 0, 0.8f) };
        foreach (int sector in reserveSectors) ev.Add(new DirectChildEvidence(3, 0, false, sector, 0, 0.7f, deployedReserve: true));
        return ev.ToArray();
    }

    internal static DirectChildEvidence Crisis(int sector) => new DirectChildEvidence(1, 3, true, sector, 0, 0.8f);

    internal static DirectChildSnapshot[] ResChildren(int n)
    {
        var snaps = new DirectChildSnapshot[n];
        for (int i = 0; i < n; i++) snaps[i] = new DirectChildSnapshot("c" + i, "a", 15, 0, "Corps" + i, true);
        return snaps;
    }

    private static DirectChildRole[] ResAllocate(DirectChildEvidence[] evidence, ReserveReleaseState state, double now, TacticalBattlePlan? plan = null,
        float agg01 = 1f, float caution01 = 0.5f, float tolerance01 = 0.5f, bool[] exploiting = null, float globalOdds = 0.5f)
    {
        var personality = new PersonalityVector(0.2f, 0f, 0f, 0f, 0f);
        var context = new ReserveReleaseContext(globalOdds, false, state, exploiting, now, agg01, caution01, tolerance01);
        IReadOnlyList<DirectChildIntent> intents = DirectChildAllocator.AllocateWithZones(plan ?? ResPlan(), personality, ResChildren(evidence.Length), evidence, null, null, context);
        var roles = new DirectChildRole[intents.Count];
        for (int i = 0; i < roles.Length; i++) roles[i] = intents[i].Role;
        return roles;
    }

    private static int Count(DirectChildRole[] roles, DirectChildRole role)
    {
        int n = 0;
        foreach (DirectChildRole r in roles) if (r == role) n++;
        return n;
    }

    private static void ResStagger()
    {
        var state = new ReserveReleaseState();
        DirectChildEvidence[] ev = ResEvidence(1, 3, 4, 0);   // four reserves, R0 = 4
        DirectChildRole[] roles = ResAllocate(ev, state, 10.0);
        AssertEqual(DirectChildRole.SupportMain, roles[1], "the first allocation releases the nearest reserve (sector 1, the lower index of two equals)");
        AssertEqual(1, Count(roles, DirectChildRole.SupportMain) - (roles[0] == DirectChildRole.SupportMain ? 1 : 0), "and only one");
        AssertEqual(DirectChildRole.Reserve, roles[2], "the second stays");

        roles = ResAllocate(ev, state, 10.0);
        AssertEqual(DirectChildRole.Reserve, roles[2], "a re-run at the same hour releases no second");
        roles = ResAllocate(ev, state, 10.03125);
        AssertEqual(DirectChildRole.Reserve, roles[2], "nor before the interval (0.15 - 0.1 x agg01 = 0.05 h) has passed");
        roles = ResAllocate(ev, state, 10.0625);
        AssertEqual(DirectChildRole.SupportMain, roles[2], "after the interval the next nearest is released");
        AssertEqual(DirectChildRole.SupportMain, roles[1], "and the first stays released");
        AssertEqual(4, state.InitialReserves, "R0 is the reserves when the trigger first held");
        AssertEqual(10.0625, state.LastReleaseHours, "the last release hour");
    }

    private static void ResFloor()
    {
        var state = new ReserveReleaseState();
        DirectChildEvidence[] ev = ResEvidence(1, 3, 4, 0);   // R0 = 4, floor share 0.30 -> ceil(1.2) = 2
        ResAllocate(ev, state, 10.0);
        ResAllocate(ev, state, 10.0625);
        DirectChildRole[] roles = ResAllocate(ev, state, 10.125);
        AssertEqual(2, Count(roles, DirectChildRole.Reserve), "two held reserves are the floor: the third release is refused");
        roles = ResAllocate(ev, state, 10.5);
        AssertEqual(2, Count(roles, DirectChildRole.Reserve), "however long it waits");

        // A bolder commander (caution 0, tolerance 1: share 0.15) holds fewer: ceil(0.6) = 1.
        var bold = new ReserveReleaseState();
        ResAllocate(ev, bold, 10.0, caution01: 0f, tolerance01: 1f);
        ResAllocate(ev, bold, 10.0625, caution01: 0f, tolerance01: 1f);
        roles = ResAllocate(ev, bold, 10.125, caution01: 0f, tolerance01: 1f);
        AssertEqual(1, Count(roles, DirectChildRole.Reserve), "share 0.15 floors at one held reserve");
        roles = ResAllocate(ev, bold, 10.1875, caution01: 0f, tolerance01: 1f);
        AssertEqual(1, Count(roles, DirectChildRole.Reserve), "and never below it");
    }

    private static void ResSingle()
    {
        var state = new ReserveReleaseState();
        DirectChildRole[] roles = ResAllocate(ResEvidence(1), state, 10.0);
        AssertEqual(DirectChildRole.SupportMain, roles[1], "R0 = 1: the single reserve may go");
        AssertEqual(1, state.InitialReserves, "R0");
    }

    private static void ResCrisisOne()
    {
        var state = new ReserveReleaseState();
        // A wing at sector 0 is outmatched; reserves at sectors 0 and 1 are both adjacent to it.
        var ev = new List<DirectChildEvidence>(ResEvidence(0, 1)) { Crisis(0) }.ToArray();
        DirectChildRole[] roles = ResAllocate(ev, state, 10.0, plan: ResPlan(BattlePhase.MainEffort));
        AssertEqual(DirectChildRole.Fix, roles[1], "the nearest reserve (sector 0) takes the crisis band");
        AssertEqual(DirectChildRole.Reserve, roles[2], "the other adjacent reserve stays: one per crisis sector");
        AssertEqual(1, state.CrisisReleased.Count, "listed once");
        AssertEqual("c1", state.CrisisReleased[0], "by child id");

        // Two crisis sectors, one reserve each.
        var two = new List<DirectChildEvidence>(ResEvidence(0, 4)) { Crisis(0), Crisis(4) }.ToArray();
        var state2 = new ReserveReleaseState();
        roles = ResAllocate(two, state2, 10.0, plan: ResPlan(BattlePhase.MainEffort));
        AssertEqual(DirectChildRole.Fix, roles[1], "the reserve at sector 0 for the crisis at 0");
        AssertEqual(DirectChildRole.Fix, roles[2], "the reserve at sector 4 for the crisis at 4");
    }

    private static void ResCrisisLatency()
    {
        var state = new ReserveReleaseState();
        var ev = new List<DirectChildEvidence>(ResEvidence(0, 1, 3)) { Crisis(0) }.ToArray();
        // First allocation that sees the crisis, at hour 10, with the stagger trigger holding as well.
        DirectChildRole[] roles = ResAllocate(ev, state, 10.0);
        AssertEqual(DirectChildRole.Fix, roles[1], "the crisis release is on the very first allocation");
        AssertEqual(10.0, state.LastReleaseHours, "the stagger release of that allocation moved the interval");
        AssertEqual(1, state.CrisisReleased.Count, "and the crisis release is listed");

        // A crisis alone does not move the interval.
        var alone = new ReserveReleaseState();
        ResAllocate(new List<DirectChildEvidence>(ResEvidence(0, 1)) { Crisis(0) }.ToArray(), alone, 10.0, plan: ResPlan(BattlePhase.MainEffort));
        AssertEqual(double.NegativeInfinity, alone.LastReleaseHours, "a crisis release does not move LastReleaseHours");

        // Defence first: the crisis release goes even when it takes the held reserves below the floor.
        var breach = new ReserveReleaseState();
        DirectChildRole[] low = ResAllocate(new List<DirectChildEvidence>(ResEvidence(0, 4)) { Crisis(0), Crisis(4) }.ToArray(), breach, 10.0, plan: ResPlan(BattlePhase.MainEffort));
        AssertEqual(0, Count(low, DirectChildRole.Reserve), "both reserves went to their crises");
    }

    private static void ResCrisisHeld()
    {
        var state = new ReserveReleaseState();
        // R0 = 4: the crisis takes the reserve at sector 0; held 3. Floor 2.
        var ev = new List<DirectChildEvidence>(ResEvidence(1, 3, 4, 0)) { Crisis(0) }.ToArray();
        DirectChildRole[] roles = ResAllocate(ev, state, 10.0);
        AssertEqual(DirectChildRole.Fix, roles[4], "crisis release of the reserve at sector 0");
        AssertEqual(DirectChildRole.SupportMain, roles[1], "the stagger release goes (held 3 -> 2, the floor)");
        roles = ResAllocate(ev, state, 10.0625);
        AssertEqual(DirectChildRole.Reserve, roles[2], "while the crisis holds the crisis child is not held: the floor blocks the next stagger release");
        AssertEqual(DirectChildRole.Fix, roles[4], "and the crisis release stays (stateless, recomputed)");
    }

    private static void ResPersist()
    {
        var state = new ReserveReleaseState();
        DirectChildEvidence[] ev = ResEvidence(1, 3, 4, 0);
        ResAllocate(ev, state, 10.0);
        for (double t = 10.00390625; t < 10.03; t += 0.00390625)
        {
            DirectChildRole[] roles = ResAllocate(ev, state, t);
            AssertEqual(DirectChildRole.SupportMain, roles[1], "the released reserve stays released while the trigger holds (t=" + t + ")");
            AssertEqual(DirectChildRole.Reserve, roles[2], "and no other follows inside the interval");
        }
        AssertTrue(state.IsReleased("c1"), "the state names it");
        AssertFalse(state.IsReleased("c2"), "and only it");
    }

    private static void ResRevert()
    {
        var state = new ReserveReleaseState();
        DirectChildEvidence[] ev = ResEvidence(1, 3, 4, 0);
        ResAllocate(ev, state, 10.0);
        ResAllocate(ev, state, 10.0625);
        AssertEqual(2, state.ReleasedCount, "two released");
        DirectChildRole[] roles = ResAllocate(ev, state, 10.125, plan: ResPlan(BattlePhase.MainEffort), globalOdds: 0.5f);
        AssertEqual(4, Count(roles, DirectChildRole.Reserve), "the trigger stopped: every reserve is a Reserve again, as today");
        AssertEqual(0, state.ReleasedCount, "the state cleared");
        AssertEqual(0, state.InitialReserves, "R0 forgotten");
        AssertEqual(double.NegativeInfinity, state.LastReleaseHours, "and the interval");

        // Odds trigger, then a plan change clears it.
        DirectChildRole[] odds = ResAllocate(ev, state, 20.0, plan: ResPlan(BattlePhase.MainEffort), globalOdds: 60f);
        AssertEqual(DirectChildRole.SupportMain, odds[1], "the odds trigger releases the same way");
        state.Clear();
        AssertEqual(0, state.ReleasedCount, "Clear (plan change, new day, stand-down) forgets everything");
    }

    private static void ResExploitingSkipped()
    {
        var state = new ReserveReleaseState();
        DirectChildEvidence[] ev = ResEvidence(1, 3, 4, 0);
        var exploiting = new[] { false, true, false, false, false };   // c1 (the nearest) has a live exploit
        DirectChildRole[] roles = ResAllocate(ev, state, 10.0, exploiting: exploiting);
        AssertEqual(DirectChildRole.Reserve, roles[1], "the exploiting reserve is not released");
        AssertEqual(DirectChildRole.SupportMain, roles[2], "the next nearest is");
    }

    private static void ResTie()
    {
        var state = new ReserveReleaseState();
        // Sectors 4 (c1) and 3 (c2): the main effort is at 2, so c2 is nearer despite its higher index.
        DirectChildRole[] roles = ResAllocate(ResEvidence(4, 3, 4, 4), state, 10.0);
        AssertEqual(DirectChildRole.SupportMain, roles[2], "nearest by sector wins over the lower index");
        // Equal distances: sectors 1 and 3 around the main effort at 2: the lower index.
        var tie = new ReserveReleaseState();
        roles = ResAllocate(ResEvidence(3, 1, 4, 4), tie, 10.0);
        AssertEqual(DirectChildRole.SupportMain, roles[1], "equal distance: the lower child index");
    }

    private static void ResOff()
    {
        var personality = new PersonalityVector(0.2f, 0f, 0f, 0f, 0f);
        DirectChildEvidence[] ev = ResEvidence(1, 3, 4, 0);
        IReadOnlyList<DirectChildIntent> intents = DirectChildAllocator.AllocateWithZones(ResPlan(), personality, ResChildren(ev.Length), ev, null, null,
            new ReserveReleaseContext(0.5f, false));
        int released = 0;
        for (int i = 1; i < intents.Count; i++) if (intents[i].Role == DirectChildRole.SupportMain) released++;
        AssertEqual(4, released, "with no state (the flag off) the Exploit phase releases every reserve at once");
    }

    private static void ResOutcomes()
    {
        var state = new ReserveReleaseState();
        DirectChildEvidence[] ev = ResEvidence(1, 3, 4, 0);
        ResAllocate(ev, state, 10.0);
        AssertEqual(1, state.Outcomes.Count, "one release");
        ReserveReleaseState.Outcome o = state.Outcomes[0];
        AssertFalse(o.Crisis, "a stagger release");
        AssertEqual("c1", o.ChildId, "which child");
        AssertEqual(3, o.Held, "held after the release");
        AssertEqual(2, o.Floor, "against floor 2");
        AssertFalse(o.FloorBreach, "no breach");

        // Share 0.45 (caution 1, tolerance 0), R0 = 3 -> floor ceil(1.35) = 2: two crisis releases leave 1 held.
        var breach = new ReserveReleaseState();
        ResAllocate(new List<DirectChildEvidence>(ResEvidence(0, 4, 3)) { Crisis(0), Crisis(4) }.ToArray(), breach, 10.0,
            plan: ResPlan(BattlePhase.MainEffort), caution01: 1f, tolerance01: 0f);
        AssertEqual(2, breach.Outcomes.Count, "two crisis releases");
        AssertTrue(breach.Outcomes[0].Crisis && breach.Outcomes[1].Crisis, "both crisis");
        AssertFalse(breach.Outcomes[0].FloorBreach, "the first leaves 2 held: at the floor");
        AssertTrue(breach.Outcomes[1].FloorBreach, "the second leaves 1 held: below the floor, defence first (floorBreach=crisis)");
    }
}
