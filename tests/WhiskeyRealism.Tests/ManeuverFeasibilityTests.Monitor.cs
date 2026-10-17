using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;
using static BattleMovementHonestyTests;
using static ManeuverFeasibilityFixtures;

// BC14 task 12: the assumption monitor and its replan (spec § 5, § 13.1 MON-*, REPLAN-*). The monitor's decision is a pure function of two
// evaluations and the facts, tested on hand-made facts; the ledger half (the snapshot at selection, the re-evaluation, the facts' fill) runs
// on the spec's scenario fixtures with a scripted world.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] MonitorTests() => new (string, Action)[]
    {
        ("bc14 MON-HELD a Clear ford on the selected route turning Held before the wing passes breaks the plan; after it passed, nothing", MonHeld),
        ("bc14 MON-LATE a strike estimate more than an hour past S* breaks the plan", MonLate),
        ("bc14 MON-DETAIL detail rising 0.2 to 0.6 breaks the plan; 0.2 to 0.4 does not", MonDetail),
        ("bc14 MON-ABANDON an abandoned wing breaks the plan only while something depends on it", MonAbandon),
        ("bc14 MON-COMMITTED no trigger once every wing is in its Turn or Attack and every fix is released", MonCommitted),
        ("bc14 MON-ROUTE-MISSING a refused NavMesh leg with no other crossing breaks the plan", MonRouteMissing),
        ("bc14 MON-MAP-KNOWN a plan selected while Unknown is screened once when the map is known", MonMapKnown),
        ("bc14 MON-ORDER several broken assumptions in one tick: the first in the spec's order wins", MonOrder),
        ("bc14 MON-SNAPSHOT the selection snapshot is taken at the pick and a new selection starts the monitor over", MonSnapshot),
        ("bc14 MON-ESTIMATE a marching body has no courier term and meets only the crossings ahead; a held body is measured from its release", MonEstimate),
        ("bc14 MON-SUPPRESSION a broken assumption that does not replan is named: cooldown, margin or preempted", MonSuppression),
        ("bc14 REPLAN-HYSTERESIS the new best within 0.1 of the current leaves it in place; an impossible current plan switches", ReplanHysteresis),
        ("bc14 REPLAN-COOLDOWN two broken assumptions within Min Replan Seconds make one replan", ReplanCooldown),
        ("bc14 REPLAN-KEPT a kept plan keeps its side, phase and age and refreshes the selection snapshot", ReplanKept),
        ("bc14 REPLAN-TRIGGER the broken-assumption trigger comes last and needs a reason", ReplanTriggerOrder),
        ("bc14 MON-WIRED the pick, the replan and BC11's abandons reach the monitor", MonWired),
    };

    // ---- a scripted world -----------------------------------------------------------------------------------

    sealed class FakeWorld : IMonitorWorld
    {
        public readonly Dictionary<string, ManeuverPhase> Phases = new Dictionary<string, ManeuverPhase>();
        public readonly Dictionary<string, int> Regions = new Dictionary<string, int>();
        public readonly HashSet<int> Held = new HashSet<int>();
        public readonly List<string> Abandoned = new List<string>();
        private int _drained;

        public ManeuverPhase PhaseOf(string childId) => Phases.TryGetValue(childId, out ManeuverPhase p) ? p : ManeuverPhase.None;
        public int RegionOf(string childId) => Regions.TryGetValue(childId, out int r) ? r : -1;
        public bool HeldNow(int link) => Held.Contains(link);

        public int DrainAbandoned(string[] into, int count)
        {
            while (_drained < Abandoned.Count && count < into.Length) into[count++] = Abandoned[_drained++];
            return count;
        }
    }

    // ---- hand-made facts ------------------------------------------------------------------------------------

    sealed class M
    {
        public string Id;
        public byte Kind;
        public int Body;
        public ManeuverPhase Phase;
        public bool HasRelease;
        public double Release;
        public float Desired, After;
        public (bool passed, bool held)[] Crossings = new (bool, bool)[0];
    }

    static M Wing(string id, int body, ManeuverPhase phase, float desired = 1.5f, float after = 1.5f)
        => new M { Id = id, Kind = ManeuverFeasibility.MemberEnvelops, Body = body, Phase = phase, Desired = desired, After = after };

    static M Fix(string id, int body, ManeuverPhase phase, bool hasRelease = false, double release = 0.0, float desired = 1.5f, float after = 1.5f)
        => new M { Id = id, Kind = ManeuverFeasibility.MemberFix, Body = body, Phase = phase, HasRelease = hasRelease, Release = release, Desired = desired, After = after };

    static MonitorFacts MakeFacts(double selectedAt, double now, params M[] members)
    {
        MonitorFacts f = MonitorFacts.Create();
        f.SelectedAtHours = selectedAt;
        f.NowHours = now;
        int crossings = 0;
        foreach (M m in members)
        {
            int i = f.MemberCount++;
            f.ChildId[i] = m.Id;
            f.Kind[i] = m.Kind;
            f.Body[i] = m.Body;
            f.Phase[i] = m.Phase;
            f.HasRelease[i] = m.HasRelease;
            f.PlannedReleaseHours[i] = m.Release;
            f.DesiredBefore[i] = m.Desired;
            f.StrikeAfter[i] = m.After;
            f.CrossingStart[i] = crossings;
            foreach (var c in m.Crossings) { f.CrossingPassed[crossings] = c.passed; f.CrossingHeld[crossings] = c.held; crossings++; }
            f.CrossingCount[i] = m.Crossings.Length;
        }
        return f;
    }

    static BodyEstimate WingBody(string id, int body, float detail = 0.2f)
        => new BodyEstimate { Body = body, ChildId = id, Envelops = true, Detail = detail };

    static BodyEstimate MainBody(string id, int body, float detail = 0.1f)
        => new BodyEstimate { Body = body, ChildId = id, Main = true, Detail = detail };

    static CandidateResult Verdict(FeasibilityVerdict v, string reason = null) => new CandidateResult { Plan = BattlePlanId.ManeuverAndFix, Verdict = v, Reason = reason };

    static bool Broken(MonitorFacts facts, BodyEstimate[] before, BodyEstimate[] after, out string reason, CandidateResult? now = null, CandidateResult? atSelection = null)
        => ManeuverFeasibility.TryBrokenAssumption(atSelection ?? Verdict(FeasibilityVerdict.Feasible), now ?? Verdict(FeasibilityVerdict.Feasible), before, after, in facts, out reason);

    // ---- the pure decision ----------------------------------------------------------------------------------

    static void MonLate()
    {
        BodyEstimate[] bodies = { WingBody("child-1", 0) };
        // Selected at 10.0 with S* 1.5 h ahead (11.5). Marching: the estimate is now + the remaining time.
        AssertTrue(Broken(MakeFacts(10.0, 10.0, Wing("child-1", 0, ManeuverPhase.March, desired: 1.5f, after: 2.6f)), bodies, bodies, out string reason), "2.6 h to go: 12.6 against 11.5");
        AssertEqual("late", reason, "reason");
        AssertFalse(Broken(MakeFacts(10.0, 10.0, Wing("child-1", 0, ManeuverPhase.March, desired: 1.5f, after: 2.4f)), bodies, bodies, out _), "0.9 h late is inside the hour");
        AssertFalse(Broken(MakeFacts(10.0, 10.0, Wing("child-1", 0, ManeuverPhase.March, desired: 1.5f, after: 2.5f)), bodies, bodies, out _), "exactly an hour is not past it");
        // A member in contact or absent from the re-evaluation is not judged.
        AssertFalse(Broken(MakeFacts(10.0, 10.0, Wing("child-1", 0, ManeuverPhase.March, desired: 1.5f, after: float.NaN)), bodies, bodies, out _), "not in the current evaluation: no estimate");
        // A wing already committed is not late: the approach is spent.
        AssertFalse(Broken(MakeFacts(10.0, 10.0, Wing("child-1", 0, ManeuverPhase.Attack, desired: 1.5f, after: 5f)), bodies, bodies, out _), "in its Attack");
        // A fix late for the common plan breaks it the same way.
        BodyEstimate[] two = { WingBody("child-1", 0), MainBody("child-2", 1) };
        MonitorFacts facts = MakeFacts(10.0, 10.0, Wing("child-1", 0, ManeuverPhase.March), Fix("child-2", 1, ManeuverPhase.Hold, true, 10.5, desired: 1.5f, after: 2.6f));
        AssertTrue(Broken(facts, two, two, out reason), "a fix released at 10.5 and 2.6 h from striking is past 11.5 by 1.6");
        AssertEqual("late", reason, "reason");
    }

    static void MonDetail()
    {
        BodyEstimate[] before = { WingBody("child-1", 0, 0.2f) };
        MonitorFacts facts = MakeFacts(10.0, 10.0, Wing("child-1", 0, ManeuverPhase.Hold));
        AssertTrue(Broken(facts, before, new[] { WingBody("child-1", 0, 0.6f) }, out string reason), "0.2 to 0.6: past Risky and 0.4 above");
        AssertEqual("detail", reason, "reason");
        AssertFalse(Broken(facts, before, new[] { WingBody("child-1", 0, 0.4f) }, out _), "0.2 to 0.4: 0.2 above, inside the margin");
        AssertFalse(Broken(facts, new[] { WingBody("child-1", 0, 0.05f) }, new[] { WingBody("child-1", 0, 0.34f) }, out _), "a rise of 0.29 under the Risky bar: nothing");
        AssertTrue(Broken(facts, new[] { WingBody("child-1", 0, 0.2f) }, new[] { WingBody("child-1", 0, 0.55f) }, out _), "0.2 to 0.55: 0.35 above, past the margin");
        AssertFalse(Broken(facts, new[] { WingBody("child-1", 0, 0.2f) }, new[] { WingBody("child-1", 0, 0.45f) }, out _), "0.2 to 0.45: 0.25 above, inside it");
        // Bodies are matched by child id: another child's rise is not this body's.
        AssertFalse(Broken(facts, before, new[] { WingBody("child-9", 0, 0.9f) }, out _), "a body the new evaluation does not have");
    }

    static void MonAbandon()
    {
        // DoubleEnvelopment: two wings; one abandoned while the other is still marching.
        BodyEstimate[] bodies = { WingBody("child-1", 0), WingBody("child-2", 1), MainBody("child-3", 2) };
        MonitorFacts facts = MakeFacts(10.0, 10.0, Wing("child-1", 0, ManeuverPhase.Abandoned), Wing("child-2", 1, ManeuverPhase.March),
            Fix("child-3", 2, ManeuverPhase.Released));
        facts.AbandonedChildIds[0] = "child-1";
        facts.AbandonedCount = 1;
        AssertTrue(Broken(facts, bodies, bodies, out string reason), "the other wing depends on it");
        AssertEqual("wing-abandoned", reason, "reason");

        // A lone FlankMarch abandoned after its mirror, with a released fix: nothing depends on it.
        BodyEstimate[] lone = { WingBody("child-1", 0), MainBody("child-2", 1) };
        MonitorFacts none = MakeFacts(10.0, 10.0, Wing("child-1", 0, ManeuverPhase.Abandoned), Fix("child-2", 1, ManeuverPhase.Released));
        none.AbandonedChildIds[0] = "child-1";
        none.AbandonedCount = 1;
        AssertFalse(Broken(none, lone, lone, out _), "a released fix does not wait for the wing");
        // The same with the fix still holding for its release.
        MonitorFacts waiting = MakeFacts(10.0, 10.0, Wing("child-1", 0, ManeuverPhase.Abandoned), Fix("child-2", 1, ManeuverPhase.Hold, true, 10.4));
        waiting.AbandonedChildIds[0] = "child-1";
        waiting.AbandonedCount = 1;
        AssertTrue(Broken(waiting, lone, lone, out reason), "a fix still holding depends on it");
        AssertEqual("wing-abandoned", reason, "reason");
        // A play with the wing alone.
        BodyEstimate[] alone = { WingBody("child-1", 0) };
        MonitorFacts wingOnly = MakeFacts(10.0, 10.0, Wing("child-1", 0, ManeuverPhase.Abandoned));
        wingOnly.AbandonedChildIds[0] = "child-1";
        wingOnly.AbandonedCount = 1;
        AssertFalse(Broken(wingOnly, alone, alone, out _), "nothing else in the plan");
        // An abandoned child the plan does not know is ignored.
        MonitorFacts stranger = MakeFacts(10.0, 10.0, Wing("child-1", 0, ManeuverPhase.March), Fix("child-2", 1, ManeuverPhase.Hold, true, 10.4));
        stranger.AbandonedChildIds[0] = "child-9";
        stranger.AbandonedCount = 1;
        AssertFalse(Broken(stranger, lone, lone, out _), "another plan's wing");
    }

    static void MonCommitted()
    {
        BodyEstimate[] before = { WingBody("child-1", 0), WingBody("child-2", 1), MainBody("child-3", 2, 0.1f) };
        BodyEstimate[] after = { WingBody("child-1", 0), WingBody("child-2", 1), MainBody("child-3", 2, 0.8f) };
        MonitorFacts committed = MakeFacts(10.0, 11.0, Wing("child-1", 0, ManeuverPhase.Turn), Wing("child-2", 1, ManeuverPhase.Attack),
            Fix("child-3", 2, ManeuverPhase.Released));
        AssertFalse(Broken(committed, before, after, out _), "every wing past its approach: even a main body detail of 0.8 is no trigger");
        // The control: the same facts with one wing still marching see the rise (the main body's fix is held for the wing).
        MonitorFacts marching = MakeFacts(10.0, 11.0, Wing("child-1", 0, ManeuverPhase.Turn), Wing("child-2", 1, ManeuverPhase.March, after: 0.5f),
            Fix("child-3", 2, ManeuverPhase.Hold, true, 11.2, after: 0.3f));
        AssertTrue(Broken(marching, before, after, out string reason), "a wing still marching: the exemption is off");
        AssertEqual("detail", reason, "the main body's detail");
        // The suppression is named: the same rise on a fix still holding is a reason the committed plan did not act on.
        MonitorFacts holding = MakeFacts(10.0, 11.0, Wing("child-1", 0, ManeuverPhase.Turn), Wing("child-2", 1, ManeuverPhase.Attack),
            Fix("child-3", 2, ManeuverPhase.Hold, true, 11.2, after: 0.3f));
        CandidateResult ok = Verdict(FeasibilityVerdict.Feasible);
        AssertFalse(ManeuverFeasibility.TryBrokenAssumption(in ok, in ok, before, after, in holding, out string none, out string suppressed), "committed: no trigger");
        AssertEqual(null, none, "no reason");
        AssertEqual("detail", suppressed, "but the reason it would have fired on is reported (the row says committed)");
        AssertFalse(ManeuverFeasibility.TryBrokenAssumption(in ok, in ok, before, before, in holding, out _, out string quiet), "committed, nothing risen");
        AssertEqual(null, quiet, "nothing suppressed");
        AssertTrue(ManeuverFeasibility.TryBrokenAssumption(in ok, in ok, before, after, in marching, out string fired, out string notSuppressed), "not committed: it fires");
        AssertEqual("detail", fired, "as before");
        AssertEqual(null, notSuppressed, "and nothing is suppressed");
        // A crossing turning Held after the commitment is no trigger either, nor a late estimate.
        MonitorFacts spent = MakeFacts(10.0, 11.0, Wing("child-1", 0, ManeuverPhase.Attack, after: 9f), Wing("child-2", 1, ManeuverPhase.Turn),
            Fix("child-3", 2, ManeuverPhase.Released));
        spent.CrossingStart[0] = 0;
        spent.CrossingCount[0] = 1;
        spent.CrossingHeld[0] = true;
        AssertFalse(Broken(spent, before, before, out _), "the attack via stands once the wing is committed");
    }

    static void MonRouteMissing()
    {
        BodyEstimate[] bodies = { WingBody("child-1", 0) };
        MonitorFacts facts = MakeFacts(10.0, 10.0, Wing("child-1", 0, ManeuverPhase.March));
        AssertTrue(Broken(facts, bodies, bodies, out string reason, now: Verdict(FeasibilityVerdict.Impossible, "no-route")), "a refused leg");
        AssertEqual("route-missing", reason, "reason");
        AssertFalse(Broken(facts, bodies, bodies, out _, now: Verdict(FeasibilityVerdict.Impossible, "no-crossing")), "no chain of crossings is not a refused leg");
        AssertFalse(Broken(facts, bodies, bodies, out _, now: Verdict(FeasibilityVerdict.Impossible, "no-route"), atSelection: Verdict(FeasibilityVerdict.Impossible, "no-route")),
            "a plan selected with its leg already refused (the filter was lifted) is not newly broken");

        // On a real evaluation: the wing's march leg is refused by the NavMesh after the plan was selected.
        FeasField f = ThreeCorps();
        f.Legs = ScenarioLegs(f, (m, b) => b.Envelops ? 555f : (Math.Abs(m.TargetX - FeasField.X(0f)) < 1f ? (float?)200f : null), BattlePlanId.ManeuverAndFix);
        FeasibilityLedger ledger = f.Evaluate(BattlePlanId.ManeuverAndFix);
        ledger.CaptureSelection(BattlePlanId.ManeuverAndFix, false);
        CandidateResult maf = Result(ledger, BattlePlanId.ManeuverAndFix);
        MemberEstimate wing = default(MemberEstimate);
        foreach (MemberEstimate m in ledger.Members(in maf).ToArray()) if (m.Kind == ManeuverFeasibility.MemberEnvelops) wing = m;
        var world = new FakeWorld();
        world.Phases[wing.ChildId] = ManeuverPhase.March;
        EvaluateSelectedFully(f.Build(BattlePlanId.ManeuverAndFix), ledger);
        AssertFalse(ledger.Assess(world, f.Now), "control: nothing changed");
        ChildFact child = ledger.Job.Children[ledger.Job.SlotOfChild(wing.ChildId)];
        var refusing = (FakeLegs)f.Legs;
        refusing.Answers.Insert(0, (child.X, child.Z, wing.TargetX, wing.TargetZ, -1, 0f, true));
        EvaluateSelectedFully(f.Build(BattlePlanId.ManeuverAndFix), ledger);
        AssertTrue(ledger.Assess(world, f.Now), "the leg is refused: the plan is broken");
        AssertEqual("route-missing", ledger.BrokenAssumption, "route-missing");
        // The same refusal once the wing is committed: no trigger, and the ledger names the reason it did not act on.
        world.Phases[wing.ChildId] = ManeuverPhase.Turn;
        AssertFalse(ledger.Assess(world, f.Now), "a wing already turning: the approach is spent");
        AssertEqual(null, ledger.BrokenAssumption, "nothing is broken");
        AssertEqual("route-missing", ledger.CommittedReason, "but the committed suppression carries the reason");
        world.Phases[wing.ChildId] = ManeuverPhase.March;
        AssertTrue(ledger.Assess(world, f.Now), "marching again, it breaks");
        AssertEqual(null, ledger.CommittedReason, "and nothing is suppressed");
    }

    static void MonMapKnown()
    {
        BodyEstimate[] none = new BodyEstimate[0];
        MonitorFacts facts = MakeFacts(10.0, 10.0);
        facts.SelectedWhileUnknown = true;
        AssertTrue(Broken(facts, none, none, out string reason), "selected while Unknown, known now");
        AssertEqual("map-known", reason, "reason");
        AssertFalse(Broken(facts, none, none, out _, now: Verdict(FeasibilityVerdict.Unknown)), "still Unknown: nothing yet");
        facts.MapKnownFired = true;
        AssertFalse(Broken(facts, none, none, out _), "it fires once per selection: none at the next evaluation");
        MonitorFacts known = MakeFacts(10.0, 10.0);
        AssertFalse(Broken(known, none, none, out _), "a plan selected on a known verdict has nothing to screen");

        // It applies to a plan with no enveloping body too, and to a committed one.
        BodyEstimate[] main = { MainBody("child-1", 0) };
        MonitorFacts single = MakeFacts(10.0, 10.0, Fix("child-1", 0, ManeuverPhase.Released));
        single.SelectedWhileUnknown = true;
        AssertTrue(Broken(single, main, main, out reason), "a single-body play is screened too");

        // On a ledger: selected while the picture was not ready, then the map is known.
        FeasField f = ThreeCorps();
        FeasibilityInput unready = f.Build(BattlePlanId.ManeuverAndFix);
        unready.Picture = new TerrainPicture(f.Grid, FeasField.Size, FeasField.Size);
        var ledger = new FeasibilityLedger();
        ManeuverFeasibility.Evaluate(in unready, ledger);
        AssertEqual(FeasibilityVerdict.Unknown, Result(ledger, BattlePlanId.ManeuverAndFix).Verdict, "control: the first evaluation is Unknown");
        ledger.CaptureSelection(BattlePlanId.ManeuverAndFix, false);
        var world = new FakeWorld();
        EvaluateSelectedFully(unready, ledger);
        AssertFalse(ledger.Assess(world, f.Now), "still Unknown: nothing to screen");
        EvaluateSelectedFully(f.Build(BattlePlanId.ManeuverAndFix), ledger);
        AssertTrue(ledger.Assess(world, f.Now), "the map is known");
        AssertEqual("map-known", ledger.BrokenAssumption, "map-known");
        AssertTrue(ledger.Assess(world, f.Now), "a cooldown that blocked the replan does not lose it");
        AssertEqual("map-known", ledger.BrokenAssumption, "it stays until the next selection");
        ledger.CaptureSelection(BattlePlanId.ManeuverAndFix, false);
        AssertEqual(null, ledger.BrokenAssumption, "a new selection clears it");
    }

    static void MonOrder()
    {
        // Every reason holds at once: a Held crossing ahead of a wing, a refused leg, a late wing, a detail jump, an abandoned wing with a
        // dependent, and a map that was Unknown at selection.
        BodyEstimate[] before = { WingBody("child-1", 0, 0.1f), WingBody("child-2", 1, 0.1f), MainBody("child-3", 2) };
        BodyEstimate[] after = { WingBody("child-1", 0, 0.8f), WingBody("child-2", 1, 0.1f), MainBody("child-3", 2) };
        CandidateResult refused = Verdict(FeasibilityVerdict.Impossible, "no-route");
        MonitorFacts Build(bool held, bool late, bool abandoned)
        {
            M w1 = Wing("child-1", 0, ManeuverPhase.March, after: late ? 4f : 1.5f);
            w1.Crossings = new[] { (false, held) };
            M w2 = Wing("child-2", 1, abandoned ? ManeuverPhase.Abandoned : ManeuverPhase.March);
            MonitorFacts f = MakeFacts(10.0, 10.0, w1, w2, Fix("child-3", 2, ManeuverPhase.Hold, true, 10.5));
            if (abandoned) { f.AbandonedChildIds[0] = "child-2"; f.AbandonedCount = 1; }
            f.SelectedWhileUnknown = true;
            return f;
        }
        Broken(Build(true, true, true), before, after, out string reason, now: refused);
        AssertEqual("crossing-held", reason, "first");
        Broken(Build(false, true, true), before, after, out reason, now: refused);
        AssertEqual("route-missing", reason, "then a refused leg");
        Broken(Build(false, true, true), before, after, out reason);
        AssertEqual("late", reason, "then a late wing");
        Broken(Build(false, false, true), before, after, out reason);
        AssertEqual("detail", reason, "then detail");
        Broken(Build(false, false, true), before, new[] { WingBody("child-1", 0, 0.1f), WingBody("child-2", 1, 0.1f), MainBody("child-3", 2) }, out reason);
        AssertEqual("wing-abandoned", reason, "then an abandoned wing");
        Broken(Build(false, false, false), before, new[] { WingBody("child-1", 0, 0.1f), WingBody("child-2", 1, 0.1f), MainBody("child-3", 2) }, out reason);
        AssertEqual("map-known", reason, "and the map last");
    }

    // ---- the ledger: snapshot, re-evaluation, facts -------------------------------------------------------------

    static void MonSnapshot()
    {
        FeasField f = ThreeCorps();
        f.Legs = ScenarioLegs(f, (m, b) => b.Envelops ? 555f : (Math.Abs(m.TargetX - FeasField.X(0f)) < 1f ? (float?)200f : null), BattlePlanId.ManeuverAndFix, BattlePlanId.DoubleEnvelopment);
        FeasibilityLedger ledger = f.Evaluate(BattlePlanId.ManeuverAndFix, BattlePlanId.DoubleEnvelopment);
        var world = new FakeWorld();
        AssertFalse(ledger.Assess(world, f.Now), "nothing selected: nothing to assess");
        ledger.CaptureSelection(BattlePlanId.ManeuverAndFix, false);
        AssertNear((float)ledger.EvaluatedHours, (float)ledger.SelectedAtHours, 1e-6f, "the snapshot is the evaluation the pick read");
        AssertFalse(ledger.Assess(world, f.Now), "no re-evaluation of the selected plan yet: nothing to compare");
        EvaluateSelectedFully(f.Build(BattlePlanId.ManeuverAndFix, BattlePlanId.DoubleEnvelopment), ledger);
        AssertEqual(2, ledger.CandidateCount, "the monitor's evaluation does not touch the candidate table");
        AssertFalse(ledger.Assess(world, f.Now), "an unchanged plan breaks nothing");
        // A new selection starts the monitor over: its re-evaluation must be of the new plan.
        ledger.CaptureSelection(BattlePlanId.DoubleEnvelopment, false);
        AssertFalse(ledger.Assess(world, f.Now), "the re-evaluation belongs to the old selection");
        AssertEqual(BattlePlanId.DoubleEnvelopment, ledger.SelectedPlan, "the selection changed");

        // The monitor judges the plan on the side the army executes it, whatever the full evaluation would pick.
        FeasField four = Scenario4(2f);
        FeasibilityLedger crossing = Evaluate4(four);
        CandidateResult maf = Result(crossing, BattlePlanId.ManeuverAndFix);
        AssertTrue(maf.Mirrored, "control: the pin-and-cross side is the mirrored one");
        crossing.CaptureSelection(BattlePlanId.ManeuverAndFix, true);
        EvaluateSelectedFully(four.Build(BattlePlanId.ManeuverAndFix), crossing);
        AssertTrue(crossing.MonitorLedger.TryGet(BattlePlanId.ManeuverAndFix, out CandidateResult monitored), "the monitor has a result");
        AssertTrue(monitored.Mirrored, "on the selected side");
        AssertNear(maf.Penalty, monitored.Penalty, 1e-4f, "the same penalty as the full evaluation, whose inputs did not change");
    }

    static void MonHeld()
    {
        FeasField f = Scenario4(2f);
        FeasibilityLedger ledger = Evaluate4(f);
        CandidateResult maf = Result(ledger, BattlePlanId.ManeuverAndFix);
        ledger.CaptureSelection(BattlePlanId.ManeuverAndFix, maf.Mirrored);
        MemberEstimate wing = default(MemberEstimate);
        foreach (MemberEstimate m in ledger.Members(in maf).ToArray()) if (m.Kind == ManeuverFeasibility.MemberEnvelops) wing = m;
        AssertEqual(1, wing.MarchVia, "control: the wing crosses at the ford (link 1)");
        RouteCrossing[] route = ledger.Crossings(in maf, in wing).ToArray();
        AssertEqual(1, route.Length, "one crossing on its route");
        AssertEqual(1, route[0].Link, "the ford");
        AssertFalse(route[0].HeldAtEvaluation, "Clear when priced");
        AssertTrue(route[0].FarRegion >= 0, "with a region beyond it");

        ChildFact child = ledger.Job.Children[ledger.Job.SlotOfChild(wing.ChildId)];
        var world = new FakeWorld();
        world.Phases[wing.ChildId] = ManeuverPhase.March;
        world.Regions[wing.ChildId] = f.Regions.RegionAt(child.X, child.Z);
        EvaluateSelectedFully(f.Build(BattlePlanId.ManeuverAndFix), ledger);
        AssertFalse(ledger.Assess(world, f.Now), "control: nothing changed");

        world.Held.Add(1);
        AssertTrue(ledger.Assess(world, f.Now), "the ford turned Held before the wing passed");
        AssertEqual("crossing-held", ledger.BrokenAssumption, "crossing-held");

        world.Regions[wing.ChildId] = route[0].FarRegion;
        AssertFalse(ledger.Assess(world, f.Now), "the wing stands beyond it: the crossing is behind it");

        // A crossing Held when it was priced was carried knowingly.
        world.Regions[wing.ChildId] = f.Regions.RegionAt(child.X, child.Z);
        world.Phases[wing.ChildId] = ManeuverPhase.Turn;
        AssertFalse(ledger.Assess(world, f.Now), "a wing in its Turn has nothing ahead to hold it");
    }

    static void MonEstimate()
    {
        // (1) No courier term for a body already marching: the same field, the wing's child flagged as moving.
        FeasField f = ThreeCorps();
        f.Legs = ScenarioLegs(f, (m, b) => b.Envelops ? 555f : (Math.Abs(m.TargetX - FeasField.X(0f)) < 1f ? (float?)200f : null), BattlePlanId.ManeuverAndFix);
        FeasibilityLedger held = f.Evaluate(BattlePlanId.ManeuverAndFix);
        CandidateResult heldResult = Result(held, BattlePlanId.ManeuverAndFix);
        MemberEstimate wing = default(MemberEstimate);
        foreach (MemberEstimate m in held.Members(in heldResult).ToArray()) if (m.Kind == ManeuverFeasibility.MemberEnvelops) wing = m;
        AssertTrue(wing.CourierHours > 0.1f, "control: an order to the wing takes time, " + wing.CourierHours);
        FeasibilityInput moving = f.Build(BattlePlanId.ManeuverAndFix);
        moving.ChildMoving = new bool[moving.ChildCount];
        for (int i = 0; i < moving.ChildCount; i++) moving.ChildMoving[i] = string.Equals(moving.ChildIds[i], wing.ChildId, StringComparison.Ordinal);
        var marching = new FeasibilityLedger();
        ManeuverFeasibility.Evaluate(in moving, marching);
        CandidateResult marchingResult = Result(marching, BattlePlanId.ManeuverAndFix);
        MemberEstimate wingMarching = default(MemberEstimate);
        foreach (MemberEstimate m in marching.Members(in marchingResult).ToArray()) if (m.Kind == ManeuverFeasibility.MemberEnvelops) wingMarching = m;
        AssertNear(wing.StrikeHours - wing.CourierHours, wingMarching.StrikeHours, 1e-4f, "its strike time lost exactly the courier");

        // (2) Only the Held crossings still ahead cost the hour: a mover past the bridge pays nothing for it.
        const int big = 3200;
        BattleTerrainGrid grid = BandMap(big, 1590, 1610, (-800f, Bridge), (-2800f, Ford));
        RiverRegions regions = RegionsOf(grid, big);
        CrossingGraphView graph = GraphOf(grid, regions, HeldBridgeWatch(regions, grid));
        RouteResult before = graph.Route(800f, -800f, 2400f, -800f, 1f, 0f, false);
        RouteResult passed = graph.Route(2000f, -800f, 2400f, -800f, 1f, 0f, false);
        AssertNear(ManeuverFeasibility.HeldCrossingHours, before.HeldHours, 1e-6f, "from the near bank the held bridge costs its hour");
        AssertNear(0f, passed.HeldHours, 1e-6f, "from beyond it, nothing");

        // (3) A held body is measured from its planned release, a marching one from now.
        BodyEstimate[] bodies = { WingBody("child-1", 0) };
        MonitorFacts hold = MakeFacts(10.0, 10.0, Wing("child-1", 0, ManeuverPhase.Hold, desired: 1.5f, after: 1.4f));
        hold.HasRelease[0] = true;
        hold.PlannedReleaseHours[0] = 11.2;
        AssertTrue(Broken(hold, bodies, bodies, out string reason), "released at 11.2 and 1.4 h from striking: 12.6 against 11.5");
        AssertEqual("late", reason, "reason");
        MonitorFacts march = MakeFacts(10.0, 10.0, Wing("child-1", 0, ManeuverPhase.March, desired: 1.5f, after: 1.4f));
        march.HasRelease[0] = true;
        march.PlannedReleaseHours[0] = 11.2;
        AssertFalse(Broken(march, bodies, bodies, out _), "the same numbers for a body already marching: 11.4 against 11.5");
    }

    // ---- the replan -----------------------------------------------------------------------------------------

    sealed class CaptureLookup : IFeasibilityLookup
    {
        public readonly Dictionary<BattlePlanId, CandidateResult> Results = new Dictionary<BattlePlanId, CandidateResult>();
        public readonly List<(BattlePlanId plan, bool mirrored)> Captured = new List<(BattlePlanId, bool)>();
        public BattlePlanId SelectedPlan { get; private set; }
        public string BrokenAssumption => null;
        public bool TryFocalPoint(out FocalPoint focal) { focal = default(FocalPoint); return false; }
        public bool WorksHold(out string reason) { reason = null; return false; }
        public bool AssaultsWorks(string childId) => false;
        public bool FacesWorks(string childId) => false;
        public int WorksHoldHash => 0;
        public int ThreatCount => 0;
        public int LookRequests(Span<float> xs, Span<float> zs) => 0;
        public double EvaluatedHours => 0.0;
        public bool HasEvaluation => false;
        public bool TryPredictedArrival(string childId, out double arrivalHours, out double marchHours) { arrivalHours = marchHours = 0.0; return false; }
        public bool TryThreat(int index, out CrossingThreat threat, float[] ownShiftByChild, out int childCount) { threat = default(CrossingThreat); childCount = 0; return false; }
        public string ThreatChildId(int column) => null;
        public bool TryGet(BattlePlanId plan, out CandidateResult result) => Results.TryGetValue(plan, out result);
        public void CaptureSelection(BattlePlanId plan, bool mirrored) { SelectedPlan = plan; Captured.Add((plan, mirrored)); }
        public bool TryPlannedRelease(string childId, out double releaseHours, out double timeBudgetHours, out float viaX, out float viaZ,
            out float attackViaX, out float attackViaZ)
        {
            releaseHours = timeBudgetHours = 0.0;
            viaX = viaZ = attackViaX = attackViaZ = float.NaN;
            return false;
        }
        public bool TryViaRegions(string childId, out int viaFarRegion, out int attackViaFarRegion) { viaFarRegion = attackViaFarRegion = -1; return false; }
    }

    static PlaybookContext HysteresisContext(IFeasibilityLookup lookup, BattlePlanId current, float margin)
        => new PlaybookContext(new PersonalityVector(0f, 0f, 0f, 0f, 0f), TerrainKind.Open, 1f, 0f, 0, 7, false, 0, 0f, 1f, 1f, false, false,
            BattleStance.Attacker, false, lookup, current, margin);

    static void ReplanHysteresis()
    {
        TacticalPlaybookCatalog catalog = SeedCatalog.AllHistoricalAndGeneric();
        var report = new SelectionReport();
        TacticalPlaybook best = catalog.Select(HysteresisContext(null, BattlePlanId.Unknown, 0f), report);
        BattlePlanId second = report[1].Plan;
        float gap = report[0].Score - report[1].Score;
        AssertTrue(gap > 0.002f && best.Id == report[0].Plan && second != best.Id, "control: a best and a runner-up with a gap, " + gap);

        AssertEqual(second, catalog.Select(HysteresisContext(null, second, gap + 0.05f), null).Id, "the runner-up stays when the best does not beat it by the margin");
        AssertEqual(best.Id, catalog.Select(HysteresisContext(null, second, gap - 0.001f), null).Id, "and the best replaces it past the margin");
        AssertEqual(best.Id, catalog.Select(HysteresisContext(null, BattlePlanId.Unknown, 1f), null).Id, "no plan to keep: an ordinary pick");
        AssertEqual(best.Id, catalog.Select(HysteresisContext(null, best.Id, 0.1f), null).Id, "the best itself is kept");

        // An impossible current plan switches, whatever the margin.
        var lookup = new CaptureLookup();
        lookup.Results[second] = new CandidateResult { Plan = second, Verdict = FeasibilityVerdict.Impossible, Penalty = 1f, Reason = "no-crossing" };
        AssertEqual(best.Id, catalog.Select(HysteresisContext(lookup, second, 10f), null).Id, "the current plan is Impossible: it was never scored, so it cannot be kept");
        // A current plan the catalog does not offer switches too.
        AssertEqual(best.Id, catalog.Select(HysteresisContext(null, BattlePlanId.Unknown, 10f), null).Id, "absent: nothing to keep");

        // The shipped margin is the spec's.
        AssertNear(0.1f, ManeuverFeasibility.ReplanScoreMargin, 1e-6f, "ReplanScoreMargin");
        AssertNear(0.3f, ManeuverFeasibility.ReplanPenaltyMargin, 1e-6f, "ReplanPenaltyMargin");
        AssertNear(1.0f, ManeuverFeasibility.LateHours, 1e-6f, "LateHours");
    }

    static EnemyVisibleState QuietVisible() => new EnemyVisibleState(
        new[] { new EnemyVisibleSector(0, 8000f, 6000f, false) }, 0.5f, anyContactSpotted: false, anyContactBroken: false);

    static ReplanOutcome Tick(ArmyOrchestrator orch, ArmyEvidence evidence, double nowSeconds, string reason, float morale = 1f)
        => ArmyTickCycle.MaybeReplan(orch, 0f, evidence, QuietVisible(), 8000f, morale, 0.2f, 0f, 60, 0.2f, 0.9f, 0f, 0f, -1f, nowSeconds, reason);

    static ArmyOrchestrator Army(IFeasibilityLookup lookup, ArmyEvidence evidence)
    {
        ArmyTickCycle.Reset();
        var army = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), new PersonalityVector(0.2f, 0f, 0f, 0.5f, 0.4f));
        army.SetFeasibility(lookup);
        army.PickInitialPlan(evidence);
        return army;
    }

    static void MonSuppression()
    {
        var evidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);

        // The plan stays when the best does not beat it by the margin: kept, named margin.
        var lookup = new CaptureLookup();
        ArmyOrchestrator army = Army(lookup, evidence);
        BattlePlanId plan = army.CurrentPlan.PlanId;
        ReplanOutcome kept = Tick(army, evidence, 1000.0, "late");
        AssertEqual(ReplanTrigger.ManeuverAssumptionBroken, kept.Trigger, "the broken assumption is the trigger");
        AssertTrue(kept.Kept && !kept.Replanned, "kept");
        AssertEqual("margin", kept.Suppression, "named margin");
        AssertEqual(plan, army.CurrentPlan.PlanId, "the plan is as it was");

        // Within Min Replan Seconds of that: cooldown.
        ReplanOutcome blocked = Tick(army, evidence, 1030.0, "late");
        AssertEqual(ReplanTrigger.ManeuverAssumptionBroken, blocked.Trigger, "the trigger is still evaluated");
        AssertFalse(blocked.Replanned || blocked.Kept, "nothing happened");
        AssertEqual("cooldown", blocked.Suppression, "named cooldown");

        // Another trigger first: preempted. A collapsed morale fires CasualtyThreshold, which replans without hysteresis.
        ArmyOrchestrator second = Army(new CaptureLookup(), evidence);
        ReplanOutcome preempted = Tick(second, evidence, 1000.0, "late", morale: 0.3f);
        AssertEqual(ReplanTrigger.CasualtyThreshold, preempted.Trigger, "the concrete trigger wins");
        AssertTrue(preempted.Replanned, "and replans");
        AssertEqual("preempted", preempted.Suppression, "the assumption is named preempted");

        // A supplied assumption that replans without suppression: the current plan is impossible.
        var impossible = new CaptureLookup();
        ArmyOrchestrator third = Army(impossible, evidence);
        BattlePlanId old = third.CurrentPlan.PlanId;
        impossible.Results[old] = new CandidateResult { Plan = old, Verdict = FeasibilityVerdict.Impossible, Penalty = 1f, Reason = "no-crossing" };
        ReplanOutcome switched = Tick(third, evidence, 1000.0, "crossing-held");
        AssertTrue(switched.Replanned && !switched.Kept, "an impossible current plan is replaced");
        AssertEqual(null, switched.Suppression, "nothing suppressed it");
        AssertTrue(third.CurrentPlan.PlanId != old, "to another plan");

        // No reason supplied: no suppression is ever named.
        ArmyOrchestrator quiet = Army(new CaptureLookup(), evidence);
        ReplanOutcome none = Tick(quiet, evidence, 1000.0, null);
        AssertEqual(ReplanTrigger.None, none.Trigger, "no trigger");
        AssertEqual(null, none.Suppression, "and no suppression");
    }

    static void ReplanCooldown()
    {
        var evidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);
        var lookup = new CaptureLookup();
        ArmyOrchestrator army = Army(lookup, evidence);
        BattlePlanId old = army.CurrentPlan.PlanId;
        lookup.Results[old] = new CandidateResult { Plan = old, Verdict = FeasibilityVerdict.Impossible, Penalty = 1f, Reason = "no-crossing" };
        ReplanOutcome first = Tick(army, evidence, 1000.0, "crossing-held");
        AssertTrue(first.Replanned, "the first replans");
        BattlePlanId next = army.CurrentPlan.PlanId;
        lookup.Results[next] = new CandidateResult { Plan = next, Verdict = FeasibilityVerdict.Impossible, Penalty = 1f, Reason = "no-crossing" };
        ReplanOutcome second = Tick(army, evidence, 1030.0, "crossing-held");
        AssertFalse(second.Replanned || second.Kept, "the second, 30 s later, does not");
        AssertEqual("cooldown", second.Suppression, "cooldown");
        AssertEqual(next, army.CurrentPlan.PlanId, "the plan is unchanged");
        ReplanOutcome third = Tick(army, evidence, 1000.0 + 61.0, "crossing-held");
        AssertTrue(third.Replanned, "after Min Replan Seconds it replans again");
        AssertEqual(2, lookup.Captured.Count - 1, "two replans, each captured (and the initial pick)");
        // The shipped overloads keep their contract: a trigger returned only when a replan happened.
        ReplanTrigger legacy = ArmyTickCycle.MaybeReplan(army, 0f, evidence, QuietVisible(), 8000f, 1f, 0.2f, 0f, 60, 0.2f, 0.9f, 0f, 0f);
        AssertEqual(ReplanTrigger.None, legacy, "the 13-argument overload raises no assumption trigger");
    }

    static void ReplanKept()
    {
        var evidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);
        var lookup = new CaptureLookup();
        ArmyOrchestrator army = Army(lookup, evidence);
        BattlePlanId plan = army.CurrentPlan.PlanId;
        AssertEqual(1, lookup.Captured.Count, "the pick captured the selection");
        AssertEqual(plan, lookup.Captured[0].plan, "of the picked plan");
        army.AdvancePhase(BattlePhase.MainEffort);
        army.AdvancePlanAge(120f);
        TacticalBattlePlan before = army.CurrentPlan;
        bool kept = army.Replan(evidence, new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, null), hysteresis: true);
        AssertTrue(kept, "kept");
        AssertEqual(plan, army.CurrentPlan.PlanId, "the same plan");
        AssertEqual(BattlePhase.MainEffort, army.CurrentPlan.Phase, "in its phase");
        AssertEqual(before.Mirrored, army.CurrentPlan.Mirrored, "on its side");
        AssertNear(120f, army.PlanAgeSeconds, 1e-3f, "and its age");
        AssertEqual(2, lookup.Captured.Count, "the selection snapshot was refreshed");
        // Without the hysteresis a replan re-picks and resets the age.
        bool plain = army.Replan(evidence, new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, null), hysteresis: false);
        AssertFalse(plain, "an ordinary replan is never 'kept'");
        AssertNear(0f, army.PlanAgeSeconds, 1e-3f, "and starts the plan's age over");
        AssertEqual(3, lookup.Captured.Count, "capturing the new selection");
    }

    static void ReplanTriggerOrder()
    {
        ReplanTriggerInput Input(float morale, string reason) => new ReplanTriggerInput(
            planAgeSeconds: 10f, currentPhase: BattlePhase.Probe, mainEffortOwnStrength: 1000f, mainEffortHistoryOwnStrength: 1000f,
            globalOddsCurrent: 1.0f, globalOddsHistory: 1.0f, armyMoraleCurrent: morale, armyMoraleFloor: 0.4f, reservesCommittedFraction: 0.2f,
            ownReinforcementsArriving: 0f, enemyMainEffortShiftConfidenceWeighted: 0f, assumptionReason: reason);
        AssertEqual(ReplanTrigger.None, ArmyReplanTriggers.Evaluate(Input(1f, null)), "nothing fires with no reason");
        AssertEqual(ReplanTrigger.ManeuverAssumptionBroken, ArmyReplanTriggers.Evaluate(Input(1f, "late")), "a reason alone fires the new trigger");
        AssertEqual(ReplanTrigger.CasualtyThreshold, ArmyReplanTriggers.Evaluate(Input(0.3f, "late")), "a concrete trigger comes first");
        AssertTrue((int)ReplanTrigger.ManeuverAssumptionBroken > (int)ReplanTrigger.OpportunityChange, "appended last");
    }

    static void MonWired()
    {
        string army = FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyOrchestrator.cs");
        AssertEqual(3, CountOf(army, "_feasibility?.CaptureSelection(_plan.PlanId, _plan.Mirrored);"), "the first pick, a kept plan and a replan each capture the selection");
        FlankResponseTests.AssertPinned(army, "hysteresisPlan: hysteresis && HasPlan ? _plan.PlanId : BattlePlanId.Unknown,", "the hysteresis names the plan being kept");
        string cycle = FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyTickCycle.cs");
        FlankResponseTests.AssertPinned(cycle, "bool kept = orchestrator.Replan(ownEvidence, intent, hysteresis: brokenAssumption, trigger.ToString());", "only the broken assumption replans with hysteresis");
        FlankResponseTests.AssertPinned(cycle, "assumptionReason: assumptionReason);", "the reason reaches the trigger input");
        string coordinator = FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalBattleCoordinatorRuntime.cs");
        FlankResponseTests.AssertPinned(coordinator, "assumptionReason: side.Army.Feasibility?.BrokenAssumption);", "the coordinator feeds the ledger's finding to the tick");
        string runtime = FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "PlayExecutorRuntime.cs");
        FlankResponseTests.AssertPinned(runtime, "if (state.Phase == ManeuverPhase.Abandoned && before != ManeuverPhase.Abandoned && spec.Envelops) side.Abandoned.Add(intents[i].ChildId);",
            "BC11 queues an abandoned wing for the monitor");
    }

    static int CountOf(string text, string needle)
    {
        string flat = FlankResponseTests.Flat(text), pin = FlankResponseTests.Flat(needle);
        int n = 0, at = 0;
        while ((at = flat.IndexOf(pin, at, StringComparison.Ordinal)) >= 0) { n++; at += pin.Length; }
        return n;
    }
}
