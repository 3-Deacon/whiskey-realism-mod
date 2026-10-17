using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static ManeuverFeasibilityFixtures;
using static WorksFixtures;

// BC14W verify round 2: three behaviours the first round's plants showed no test depended on (V01, V13, V15), and the pin of a holding fix.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksTurnTests() => new (string, Action)[]
    {
        ("bc14w TURN-AFTER-DARK a turn whose body strikes after dark is not an available turn, so FocalAssault is not ruled out by it", WTurnAfterDark),
        ("bc14w WORKS-MEETING a Meeting side gets the works doctrine: a focal point, works strike points and the bar of three, as an Attacker does", WMeetingGetsWorks),
        ("bc14w TURN-HOLD-FIX the turn-not-struck hold needs a fix facing works: with the fix off the works an unstruck turn holds nothing", WTurnHoldNeedsFix),
        ("bc14w WORKS-PIN-FADED holders faded to memory 0.4 leave the fix pinned and post a look; the same segment with own regiments standing on it lifts the pin", WPinFaded),
        ("bc14w WORKS-PIN-EXPLOIT a non-breach exploit and its followers aimed at unbroken works hold stance 2 whatever the hold; a breach, a broken-in aim and a recall do not", WPinExploit),
        ("bc14w WORKS-PIN-HOLD a pinned exploit holds at the band's edge on its approach line, keeps its heading, and resumes the strike when the aim is broken in", WPinHold),
        ("bc14w WORKS-PIN-HOLD-GAP with two works segments and a gap, a group inside the first band holds where it stands and is not sent into the second", WPinHoldGap),
        ("bc14w WORKS-PIN-LEDGER the holding fix of an unstruck turn is pinned, the wing is not; the pin lifts when its works are broken in and with the flag off", WPinLedger),
    };

    static void WTurnAfterDark()
    {
        FeasField lit = Scenario6();
        FeasibilityLedger litLedger = lit.Evaluate(WorksPlans);
        AssertEqual("turn-available", Result(litLedger, BattlePlanId.FocalAssault).Reason, "control: in daylight a turn is available");
        AssertTrue(litLedger.Job.WorksActive, "control: the works doctrine is on");

        // The same field with the day almost over: every turn's strike falls after dark (the Daylight term is 1), so none is an available turn.
        FeasField dark = Scenario6();
        dark.DaylightHours = 0.1f;
        FeasibilityLedger darkLedger = dark.Evaluate(WorksPlans);
        AssertNear(1f, FirstWing(BodiesArray(darkLedger, BattlePlanId.ManeuverAndFix)).Daylight, 1e-6f, "the turn's wing strikes after dark");
        CandidateResult focal = Result(darkLedger, BattlePlanId.FocalAssault);
        AssertTrue(!string.Equals(focal.Reason, "turn-available", StringComparison.Ordinal), "FocalAssault is not ruled out by a turn that ends after dark, got " + focal.Reason);
        AssertEqual("after-dark", focal.Reason, "it is judged on its own terms instead (its own strike is after dark too)");
    }

    static void WMeetingGetsWorks()
    {
        FeasField attacker = Scenario7(Aggressive());
        FeasibilityLedger a = attacker.Evaluate(WorksPlans);
        FeasField meeting = Scenario7(Aggressive());
        meeting.Stance = BattleStance.Meeting;
        FeasibilityLedger m = meeting.Evaluate(WorksPlans);
        AssertTrue(m.TryFocalPoint(out FocalPoint focal), "a Meeting side has the enemy's focal point");
        AssertEqual(FocalKind.Junction, focal.Kind, "the junction");
        AssertTrue(m.Job.WorksActive && m.HeldWorks != null, "its evaluation holds the enemy's works");
        BodyEstimate wing = FirstWing(BodiesArray(m, BattlePlanId.FocalAssault));
        BodyEstimate reference = FirstWing(BodiesArray(a, BattlePlanId.FocalAssault));
        AssertTrue(wing.StrikeOnWorks && wing.AssaultsWorks, "its column assaults works");
        AssertNear(reference.BarUsed, wing.BarUsed, 1e-4f, "and is held to the bar of three the Attacker's is");
        AssertNear(reference.Detail, wing.Detail, 1e-4f, "with the same detail");
        BodyEstimate generic = BodiesArray(m, BattlePlanId.GenericAggressive)[0];
        AssertTrue(generic.StrikeOnWorks && generic.AssaultsWorks, "a frontal play pays for the works too");
        AssertEqual(FeasibilityVerdict.Feasible, Result(m, BattlePlanId.FocalAssault).Verdict, "FocalAssault is offered to a Meeting side");
    }

    // Scenario 6 with the centre cluster in the open and the works only on the left: the turn's fix does not face works, so an unstruck turn holds nothing.
    static void WTurnHoldNeedsFix()
    {
        FeasField control = Scenario6();
        FeasibilityLedger on = control.Evaluate(WorksPlans);
        CandidateResult onResult = Result(on, BattlePlanId.ManeuverAndFix);
        BodyEstimate[] onBodies = BodiesArray(on, BattlePlanId.ManeuverAndFix);
        on.CaptureSelection(BattlePlanId.ManeuverAndFix, onResult.Mirrored);
        var hold = new FakeWorld();
        hold.Phases[FirstWing(onBodies).ChildId] = ManeuverPhase.Hold;
        on.RefreshWorksHold(hold);
        AssertTrue(on.WorksHold(out string reason) && reason == "turn-not-struck", "control: the fix on the works holds the unstruck turn");
        AssertTrue(on.FacesWorks(MainOf(onBodies).ChildId), "control: because the fix faces works");

        FeasField open = Scenario6();
        EnemyObservation centre = open.Enemies[1];
        AssertNear(X(0f), centre.X, 1f, "control: the second cluster is the centre one");
        centre.CoverValue = 0f;
        centre.CoverObject = 0;
        centre.Standing = false;
        open.Enemies[1] = centre;
        open.WorksPoints = new Points().Line(-600f, -300f, 50f, l => 600f);
        FeasibilityLedger off = open.Evaluate(WorksPlans);
        AssertTrue(off.HeldWorks != null, "the left of the line is still held works");
        CandidateResult offResult = Result(off, BattlePlanId.ManeuverAndFix);
        BodyEstimate[] offBodies = BodiesArray(off, BattlePlanId.ManeuverAndFix);
        BodyEstimate fix = MainOf(offBodies);
        AssertFalse(fix.StrikeOnWorks, "the fix's strike point is off the works");
        off.CaptureSelection(BattlePlanId.ManeuverAndFix, offResult.Mirrored);
        var wait = new FakeWorld();
        wait.Phases[FirstWing(offBodies).ChildId] = ManeuverPhase.Hold;
        off.RefreshWorksHold(wait);
        AssertFalse(off.WorksHold(out string none), "nothing faces works, so an unstruck turn holds nothing");
        AssertTrue(none == null, "with no reason");
        AssertFalse(off.FacesWorks(fix.ChildId), "the fix faces none");
    }

    // The pin: the fix that holds the works while a wing turns them is pinned at stance 2; the wing is not; a break-in, the flag off and a wing that
    // assaults a different segment each have their say.
    static void WPinLedger()
    {
        FeasField f = Scenario6();
        FeasibilityLedger ledger = f.Evaluate(WorksPlans);
        CandidateResult result = Result(ledger, BattlePlanId.ManeuverAndFix);
        BodyEstimate[] bodies = BodiesArray(ledger, BattlePlanId.ManeuverAndFix);
        BodyEstimate wing = FirstWing(bodies), fix = MainOf(bodies);
        ledger.CaptureSelection(BattlePlanId.ManeuverAndFix, result.Mirrored);
        var world = new FakeWorld();
        world.Phases[wing.ChildId] = ManeuverPhase.Hold;
        ledger.RefreshWorksHold(world);
        AssertTrue(ledger.PinsWorks(fix.ChildId), "the fix facing works is pinned");
        AssertFalse(ledger.PinsWorks(wing.ChildId), "the wing faces no works and is not");
        AssertFalse(ledger.PinsWorks("child-unknown"), "an unknown child is not");
        AssertFalse(ledger.PinsWorks(null), "nor is a missing id");
        ledger.WorksReport(out _, out _, out int assaulting, out int facing, out int pinned);
        AssertTrue(facing >= 1, "control: children face works");
        AssertEqual(facing, pinned, "the works hold row counts every holding fix that faces them as pinned");
        AssertEqual(0, assaulting, "none of which assaults");

        // The pin does not wait for the works hold: the wing reaching its Attack lifts the hold and the fix is pinned still.
        world.Phases[wing.ChildId] = ManeuverPhase.Attack;
        ledger.RefreshWorksHold(world);
        AssertFalse(ledger.WorksHold(out _), "the hold has lifted");
        AssertTrue(ledger.PinsWorks(fix.ChildId), "and the fix is still pinned: the works are unbroken");

        // Broken in at the fix's own strike point: its holders shaken.
        FeasField shaken = Scenario6();
        for (int i = 0; i < shaken.Enemies.Count; i++)
        {
            EnemyObservation e = shaken.Enemies[i];
            if (e.CoverValue > 0f) { e.Morale = 0.3f; shaken.Enemies[i] = e; }
        }
        FeasibilityLedger broken = shaken.Evaluate(WorksPlans);
        BodyEstimate[] brokenBodies = BodiesArray(broken, BattlePlanId.ManeuverAndFix);
        broken.CaptureSelection(BattlePlanId.ManeuverAndFix, Result(broken, BattlePlanId.ManeuverAndFix).Mirrored);
        broken.RefreshWorksHold(world);
        AssertTrue(broken.FacesWorks(MainOf(brokenBodies).ChildId), "control: the fix still faces works");
        AssertFalse(broken.PinsWorks(MainOf(brokenBodies).ChildId), "shaken holders: the works are broken in and the pin lifts");
        broken.WorksReport(out _, out _, out _, out int brokenFacing, out int brokenPinned);
        AssertTrue(brokenFacing >= 1, "control: the children still face works");
        AssertEqual(0, brokenPinned, "and the row counts none pinned");

        // Flag off: no works doctrine, no pin.
        bool saved = WhiskeyRealism.Plugin.EnemyWorksDoctrine;
        try
        {
            WhiskeyRealism.Plugin.EnemyWorksDoctrine = false;
            ledger.RefreshWorksHold(world);
            AssertFalse(ledger.PinsWorks(fix.ChildId), "with Enable Enemy Works Doctrine off nothing is pinned");
        }
        finally { WhiskeyRealism.Plugin.EnemyWorksDoctrine = saved; }
        ledger.RefreshWorksHold(world);
        AssertTrue(ledger.PinsWorks(fix.ChildId), "control: back on, it is");
    }

    // The coordinator's ruling (2026-10-17): holders lost to fog are what entrenched troops look like, so a fade is not a break-in. The centre cluster's
    // holders are memories at confidence 0.4 and nothing else is known of them: the fix stays pinned and the ledger posts a look at the faded strike point;
    // with an own regiment standing on the works there, the pin lifts and no look is posted.
    static void WPinFaded()
    {
        FeasField Faded()
        {
            FeasField f = Scenario6();
            EnemyObservation centre = f.Enemies[1];
            AssertNear(X(0f), centre.X, 1f, "control: the second cluster is the centre one");
            centre.State = ObservationState.Remembered;
            centre.Confidence01 = 0.4f;
            f.Enemies[1] = centre;
            return f;
        }

        FeasField fadedField = Faded();
        FeasibilityLedger faded = fadedField.Evaluate(WorksPlans);
        BodyEstimate[] bodies = BodiesArray(faded, BattlePlanId.ManeuverAndFix);
        BodyEstimate fix = MainOf(bodies);
        faded.CaptureSelection(BattlePlanId.ManeuverAndFix, Result(faded, BattlePlanId.ManeuverAndFix).Mirrored);
        faded.RefreshWorksHold(new FakeWorld());
        AssertTrue(fix.StrikeOnWorks, "the fix's strike point is still on held works: the faded holders hold their segment");
        AssertTrue(faded.FacesWorks(fix.ChildId), "so the fix still faces them");
        AssertTrue(faded.PinsWorks(fix.ChildId), "and stays pinned: nothing perceived says they left");
        var xs = new float[ManeuverFeasibility.MaxLookRequests];
        var zs = new float[ManeuverFeasibility.MaxLookRequests];
        int looks = faded.LookRequests(xs, zs);
        AssertTrue(looks >= 1, "a look is posted");
        bool atStrike = false;
        for (int k = 0; k < looks; k++)
            atStrike |= Math.Abs(xs[k] - fix.StrikeX) < 5f && Math.Abs(zs[k] - (fix.StrikeZ - (WhiskeyRealism.Tactical.Battle.WorksRules.WorksBandMeters + WhiskeyRealism.Tactical.Battle.WorksRules.WorksEndMeters))) < 5f;
        AssertTrue(atStrike, "at the fix's strike point, drawn back outside the works band");
        AssertFalse(faded.HeldWorks.InWorksBand(xs[0], zs[0]), "where the recon producer will not refuse it");

        // The look does not depend on the river regions or the crossing watch (a map with no rivers): the same ledger read with both gone posts it too.
        RiverRegions savedRegions = faded.Job.Input.Regions;
        CrossingWatch savedWatch = faded.Job.Input.Crossings;
        faded.Job.Input.Regions = null;
        faded.Job.Input.Crossings = null;
        int looksNoRivers = faded.LookRequests(xs, zs);
        faded.Job.Input.Regions = savedRegions;
        faded.Job.Input.Crossings = savedWatch;
        AssertTrue(looksNoRivers >= 1, "a faded look is posted with no river regions and no crossing watch");

        // Control: with the centre cluster seen (Visible) there is no fade, so no look, and the pin is the plain one.
        FeasibilityLedger seen = Scenario6().Evaluate(WorksPlans);
        seen.CaptureSelection(BattlePlanId.ManeuverAndFix, Result(seen, BattlePlanId.ManeuverAndFix).Mirrored);
        seen.RefreshWorksHold(new FakeWorld());
        AssertTrue(seen.PinsWorks(MainOf(BodiesArray(seen, BattlePlanId.ManeuverAndFix)).ChildId), "control: pinned when seen");
        AssertEqual(0, seen.LookRequests(new float[2], new float[2]), "control: and no look is posted for it");

        // Own regiments standing on the works at the fix's strike point: positive evidence, the pin lifts and the look ends.
        FeasField held = Faded();
        held.AddPicket(fix.StrikeX - X(0f), fix.StrikeZ + 1200f + 20f, 0f);
        FeasibilityLedger onWorks = held.Evaluate(WorksPlans);
        BodyEstimate[] onBodies = BodiesArray(onWorks, BattlePlanId.ManeuverAndFix);
        onWorks.CaptureSelection(BattlePlanId.ManeuverAndFix, Result(onWorks, BattlePlanId.ManeuverAndFix).Mirrored);
        onWorks.RefreshWorksHold(new FakeWorld());
        AssertTrue(onWorks.FacesWorks(MainOf(onBodies).ChildId), "control: the fix still faces the works");
        AssertFalse(onWorks.PinsWorks(MainOf(onBodies).ChildId), "an own regiment on them: the strike has broken in and the pin lifts");
        AssertEqual(0, onWorks.LookRequests(new float[2], new float[2]), "and no look is posted");
    }

    // The coordinator's ruling (verify round 2): a BC13 exploit does not get around the pin. The verifier's case is scenario 7 with the aggressive
    // commander (ManeuverAndFix, no works hold) and an IsolatedCluster exploit aimed at the works: its order is AttackObjective, stance 3 by the mapper.
    static void WPinExploit()
    {
        FeasibilityLedger agg = Scenario7(Aggressive()).Evaluate(WorksPlans);
        agg.CaptureSelection(BattlePlanId.ManeuverAndFix, Result(agg, BattlePlanId.ManeuverAndFix).Mirrored);
        agg.RefreshWorksHold(new FakeWorld());
        AssertFalse(agg.WorksHold(out _), "control: the hold is off in the aggressive run");
        IWorksQuery works = agg.HeldWorks;
        float aimX = X(10f), aimZ = Z(600f);
        AssertTrue(works.OnHeldWorks(aimX, aimZ), "control: the aim lies on held works");
        AssertFalse(works.StrikeBrokenIn(aimX, aimZ), "and they are unbroken");

        ExploitTable table = ExploitTable.ForAlliance(0);
        try
        {
            table.Clear(ExploitEnd.StandDown);
            ExploitEntry entry(int id, OpeningKind kind, ExploitPhase phase = ExploitPhase.Advance, float x = float.NaN, float z = float.NaN)
                => new ExploitEntry { Id = id, AssetId = 50 + id * 10, OpeningId = id, Kind = kind, Phase = phase, AimX = float.IsNaN(x) ? aimX : x, AimZ = float.IsNaN(z) ? aimZ : z, Heading = 0f };
            AssertTrue(table.Add(entry(1, OpeningKind.IsolatedCluster)), "an isolated-cluster exploit on the works");
            table.SetSubtree(1, new[] { 61 }, new int[0]);
            table.SetWorksView(false, works);
            AssertTrue(table.TryOverride(60, out CommandDoctrineOrder order, out _), "control: its asset has the exploit's order");
            AssertEqual(CommandTaskType.AttackObjective, order.Task, "control: an attack, not a hold (the works hold is off)");
            AssertEqual(TacticalCommandTaskToStanceMapper.StanceAttack, TacticalCommandTaskToStanceMapper.Map(order.Task), "control: stance 3 by the mapper");
            AssertTrue(table.PinsExploit(60), "the asset is pinned");
            AssertTrue(table.PinsExploit(61), "and its follower");
            AssertFalse(table.PinsExploit(99), "another group is not");
            AssertEqual(TacticalCommandTaskToStanceMapper.StanceDefend, TacticalCommandTaskToStanceMapper.Map(order.Task, table.PinsExploit(60)), "so it takes stance 2");
            table.SetWorksView(true, works);
            AssertTrue(table.PinsExploit(60), "and with the works hold on as well");
            table.SetWorksView(false, null);
            AssertFalse(table.PinsExploit(60), "with no works view (the flag off) nothing is pinned");
            table.SetWorksView(false, works);

            // A breach may strike: a Gap and a Penetration are not pinned.
            table.Clear(ExploitEnd.StandDown);
            table.Add(entry(2, OpeningKind.Gap));
            table.Add(entry(3, OpeningKind.Penetration));
            AssertFalse(table.PinsExploit(70), "a Gap exploit is a breach");
            AssertFalse(table.PinsExploit(80), "so is a Penetration");

            // An aim off the works, a recalled exploit and a broken-in aim do not pin.
            table.Clear(ExploitEnd.StandDown);
            table.Add(entry(4, OpeningKind.IsolatedCluster, x: X(0f), z: Z(0f)));
            AssertFalse(table.PinsExploit(90), "an aim off the works is not pinned");
            table.Clear(ExploitEnd.StandDown);
            table.Add(entry(5, OpeningKind.IsolatedCluster, ExploitPhase.Recall));
            AssertFalse(table.PinsExploit(100), "a recalled exploit is not pinned");

            FeasField shaken = Scenario7(Aggressive());
            for (int i = 0; i < shaken.Enemies.Count; i++)
            {
                EnemyObservation e = shaken.Enemies[i];
                e.Morale = 0.3f;
                shaken.Enemies[i] = e;
            }
            FeasibilityLedger broken = shaken.Evaluate(WorksPlans);
            table.Clear(ExploitEnd.StandDown);
            table.Add(entry(6, OpeningKind.IsolatedCluster));
            table.SetWorksView(false, broken.HeldWorks);
            AssertTrue(broken.HeldWorks.OnHeldWorks(aimX, aimZ) && broken.HeldWorks.StrikeBrokenIn(aimX, aimZ), "control: shaken holders break the aim in");
            AssertFalse(table.PinsExploit(110), "a broken-in aim is not pinned: the exploit may strike");
        }
        finally
        {
            table.SetWorksView(false, null);
            table.Clear(ExploitEnd.StandDown);
        }

        string producer = FlankResponseTests.Flat(FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs"));
        AssertTrue(producer.Contains("ExploitTable.ForAlliance(s.Alliance).PinsExploit(rec.InstanceId)) { MoveContest.PinHoldPoint(rec.X, rec.Z, order.PrimaryTarget.X, order.PrimaryTarget.Z, army.Feasibility.HeldWorks, out float pinX, out float pinZ);"),
            "the asset's pin is asked of the exploit table in the posture pipeline and builds its hold at the band's edge");
        AssertTrue(producer.Contains("int followerStance = recordTick && ExploitTable.ForAlliance(s.Alliance).PinsExploit(rec.InstanceId) ? PostureIntentPlan.SubmitWorksPinStance("),
            "and the follower's before its early return");
        string runtime = FlankResponseTests.Flat(FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalBattleCoordinatorRuntime.cs"));
        AssertTrue(runtime.Contains("if (Battle.ExploitTable.ForAlliance(allianceId).PinsExploit(own[i].InstanceId)) { cache[instanceId] = TacticalResolvedGroupDecision.ForWorksHold("),
            "#45 writes none for a pinned exploit group");
    }

    // The coordinator's ruling (verify round 3): a pinned exploit is held outside the band as the fix pin is. The verifier's (-450, 600) aim: unpinned,
    // the exploit's order moves it onto the works; pinned, its order is the works hold with a lane at the band's edge on its approach line, with the
    // exploit's heading kept (it faces and fires at the aim), and after the break-in the table pins nothing and the order is the strike again.
    static void WPinHold()
    {
        FeasibilityLedger agg = Scenario7(Aggressive()).Evaluate(WorksPlans);
        agg.CaptureSelection(BattlePlanId.ManeuverAndFix, Result(agg, BattlePlanId.ManeuverAndFix).Mirrored);
        agg.RefreshWorksHold(new FakeWorld());
        IWorksQuery works = agg.HeldWorks;
        float aimX = X(-450f), aimZ = Z(600f);
        AssertTrue(works.OnHeldWorks(aimX, aimZ), "control: the verifier's aim lies on held works");

        // The hold point: the band's edge on the line from the group, and no further.
        MoveContest.PinHoldPoint(X(-450f), Z(0f), aimX, aimZ, works, out float hx, out float hz);
        AssertNear(X(-450f), hx, 0.5f, "straight ahead: the same lateral");
        AssertNear(Z(600f - WorksRules.WorksBandMeters), hz, 0.5f, "the band's width short of the profile");
        AssertFalse(MoveContest.InBand(works, hx, hz), "outside the band");
        AssertTrue(MoveContest.InBand(works, hx + (aimX - X(-450f)) * 0.01f + 0f, hz + 1f), "and one step on it is in the band");
        MoveContest.PinHoldPoint(X(-150f), Z(100f), aimX, aimZ, works, out float ox, out float oz);
        AssertFalse(MoveContest.InBand(works, ox, oz), "from the side, still outside the band");
        float step = 1f / MoveContest.Distance(X(-150f), Z(100f), aimX, aimZ);
        AssertTrue(MoveContest.InBand(works, ox + (aimX - X(-150f)) * step, oz + (aimZ - Z(100f)) * step), "and within one unit of its edge, on the line to the aim");
        MoveContest.PinHoldPoint(X(-450f), Z(540f), aimX, aimZ, works, out float ix, out float iz);
        AssertTrue(ix == X(-450f) && iz == Z(540f), "a group already in the band holds where it stands");
        MoveContest.PinHoldPoint(X(-450f), Z(0f), aimX, aimZ, null, out float nx, out float nz);
        AssertTrue(nx == X(-450f) && nz == Z(0f), "with no works view it stays");

        // The exploit's order, unpinned and pinned.
        ExploitTable table = ExploitTable.ForAlliance(0);
        try
        {
            table.Clear(ExploitEnd.StandDown);
            table.Add(new ExploitEntry { Id = 1, AssetId = 60, OpeningId = 1, Kind = OpeningKind.IsolatedCluster, Phase = ExploitPhase.Advance, AimX = aimX, AimZ = aimZ, Heading = 20f });
            table.SetWorksView(false, works);
            AssertTrue(table.TryOverride(60, out CommandDoctrineOrder strike, out _), "the asset has the exploit's order");
            AssertEqual(CommandTaskType.AttackObjective, strike.Task, "an attack");
            AssertTrue(MoveContest.InBand(works, strike.PrimaryTarget.X, strike.PrimaryTarget.Z), "control: whose target lies in the band, so the posture executor would march it onto the works");
            var physical = new CommandPhysicalState(false, false, false, 0, false, 0);
            var eligibility = new WriteEligibilitySnapshot(modeAllowsWrites: true, playerProtected: false, routed: false, orderPending: false, recentOrder: false);
            PostureExecutionDecision march = CommandPostureExecutor.Decide(strike, physical, eligibility, 10f, holdLane: false);
            AssertEqual(PostureExecutionAction.SetFormationAndWaypoint, march.Action, "control: unpinned it is a move");

            AssertTrue(table.PinsExploit(60), "pinned");
            CommandDoctrineOrder held = strike.WithWorksHold(strike.Task, null, DoctrineTargetPoint.From(hx, hz));
            AssertEqual(CommandTaskType.HoldObjective, held.Task, "its order is a hold");
            AssertTrue(WorksHoldGuard.Holds(held.Task, held.Reason), "the works hold's, so every reader of the hold reads it");
            AssertEqual(CommandTaskType.AttackObjective, held.PreGuardTask, "over the attack it replaced");
            AssertEqual(strike.ExploitId, held.ExploitId, "still the exploit's");
            AssertNear(strike.ManeuverHeading, held.ManeuverHeading, 1e-4f, "with the exploit's heading toward the aim kept");
            AssertTrue(WorksHoldGuard.HasLane(in held), "it has a lane");
            AssertTrue(WorksHoldGuard.LaneOpen(in held, works), "which is outside the band");
            PostureExecutionDecision lane = CommandPostureExecutor.Decide(held, physical, eligibility, 10f, holdLane: true);
            AssertEqual(PostureExecutionAction.SetFormationAndWaypoint, lane.Action, "so it goes to the lane");
            AssertEqual(PostureExecutionTarget.DoctrinePrimaryTarget, lane.Target, "which is the order's target");
            AssertFalse(MoveContest.InBand(works, held.PrimaryTarget.X, held.PrimaryTarget.Z), "and that target is outside the band: no move onto the works");
            AssertNear(hx, held.PrimaryTarget.X, 1e-3f, "at the hold point");
            // A former attack that is no exploit's still holds in place: the lane is the fix's and the exploit's only.
            CommandDoctrineOrder plain = OrderOf(CommandTaskType.AttackObjective).WithWorksHold(CommandTaskType.AttackObjective);
            AssertFalse(WorksHoldGuard.HasLane(in plain), "a former attack has no lane");

            // After the break-in: shaken holders at the aim, nothing is pinned and the order is the strike again.
            FeasField shaken = Scenario7(Aggressive());
            for (int i = 0; i < shaken.Enemies.Count; i++)
            {
                EnemyObservation e = shaken.Enemies[i];
                e.Morale = 0.3f;
                shaken.Enemies[i] = e;
            }
            FeasibilityLedger after = shaken.Evaluate(WorksPlans);
            table.SetWorksView(false, after.HeldWorks);
            AssertFalse(table.PinsExploit(60), "broken in: no pin");
            AssertTrue(table.TryOverride(60, out CommandDoctrineOrder resumed, out _), "the asset still has the exploit's order");
            AssertEqual(CommandTaskType.AttackObjective, resumed.Task, "the strike resumes");
            AssertNear(aimX, resumed.PrimaryTarget.X, 1e-3f, "on the aim");
        }
        finally
        {
            table.SetWorksView(false, null);
            table.Clear(ExploitEnd.StandDown);
        }

        // The pipeline builds the hold from the table's answer, and a stance alone is no submission (the facing pulse and the fire control still run).
        string run = FlankResponseTests.Flat(FlankResponseTests.Between(FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs"),
            "internal static bool Run(", "private static bool CanBypassStaleQueuedOrder"));
        int convert = run.IndexOf("order = order.WithWorksHold(WorksHoldGuard.Holds(order.Task, order.Reason) ? order.PreGuardTask : order.Task, null, DoctrineTargetPoint.From(pinX, pinZ));", StringComparison.Ordinal);
        int held2 = run.IndexOf("bool worksHeld = WorksHoldGuard.Holds(state.Task, state.Reason)", StringComparison.Ordinal);
        AssertTrue(convert > 0 && held2 > convert, "the conversion precedes the hold's reading, so the converted asset is a held group");
        AssertTrue(run.Contains("&& order.PrimaryTarget.HasValue && army?.Feasibility?.HeldWorks != null"), "it needs the aim and the works");
    }

    // Two held segments with a gap of open ground between them (the verifier's geometry): the band is z >= 500 for x in [0, 300] and x in [600, 900]. Without
    // the in-band guard the bisection of a group inside the first band, aiming at the second, would cross the gap and put its hold in the second band.
    sealed class TwoSegments : IWorksQuery
    {
        public bool OnHeldWorks(float x, float z) => InWorksBand(x, z) && z >= 600f;
        public bool InWorksBand(float x, float z) => z >= 500f && ((x >= 0f && x <= 300f) || (x >= 600f && x <= 900f));
        public bool StrikeBrokenIn(float x, float z) => false;
    }

    static void WPinHoldGap()
    {
        var works = new TwoSegments();
        MoveContest.PinHoldPoint(100f, 550f, 800f, 600f, works, out float hx, out float hz);
        AssertNear(100f, hx, 0.01f, "a group in the first band holds where it stands (x)");
        AssertNear(550f, hz, 0.01f, "and not in the second band (z)");
        MoveContest.PinHoldPoint(100f, 200f, 100f, 600f, works, out hx, out hz);
        AssertTrue(hz < 500f && hz > 495f, "control: from outside it goes up to the first band's edge on the approach line, got " + hz);
        AssertNear(100f, hx, 0.01f, "straight ahead");
    }
}
