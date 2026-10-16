using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;
using static ManeuverFeasibilityFixtures;

// BC14 task 10: BC11's half of the synchronized plan: the Hold released on the ledger's common plan, a time budget, the crossing vias, the
// fix's hold, the mirrored fallback, and the node that carries the via to the doctrine order. The pure state machine runs on the BC11 fixture
// (FlankResponseTests.Maneuver()): a FlankMarch wing at (0,0), its rect at (1000,500), the Center child's contact never seen.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] ManeuverTests() => new (string, Action)[]
    {
        ("bc14 FEAS-SYNC the Hold ends at the planned release, not at the Center's contact plus the offset", FeasSync),
        ("bc14 SYNC-UNSET with no ledger entry BC11's own rule governs the Hold", SyncUnset),
        ("bc14 SYNC-PIN-AT-RELEASE the vias and the budget are latched at the March and never change after it", SyncPinAtRelease),
        ("bc14 BUDGET the time budget is 1.5 x (courier + march) + the turn hold, within [1 h, daylight]", Budget),
        ("bc14 CEILING-LATCH the Hold ceiling is anchored to the first planned release", CeilingLatch),
        ("bc14 FIX-HOLD a fixing child with a release ahead holds through the start-delay path; one in contact or with no entry never does", FixHold),
        ("bc14 MIRROR-FALLBACK the one mirrored try goes to the authored anchor of a mirrored projection and drops the ledger's plan", MirrorFallback),
        ("bc14 STATE-FLAGS a default ManeuverState has no first planned release, no budget and no via, whatever the hours hold", StateFlags),
        ("bc14 VIA-PASSED once the child stands past the via the March and Attack orders carry none", ViaPassed),
        ("bc14 VIA-ROUTE-CHECK the march leg's route check runs as two half-queries through the via", ViaRouteCheck),
        ("bc14 INWARD-VIA-BC11 the inward leg is sampled as two sub-legs through the attack via and the via's own footprint is not river", InwardViaBc11),
        ("bc14 READER-NODE-COPY a maneuver order's via reaches the node entry, the doctrine order and the maneuver hash", ReaderNodeCopy),
        ("bc14 LEDGER-CHILD-TABLE the selected plan's per-child table gives each wing its release, budget and via and a fix its release", LedgerChildTable),
        ("bc14 MANEUVER-WIRED BC11's runtime reads the ledger's plan only for the selected plan and the ledger's side", ManeuverWired),
    };

    // The fixture's wing with a planned release at the given hour (absolute doctrine hours; the play activated at 10.0).
    static ManeuverInput WithRelease(double releaseHours)
    {
        ManeuverInput input = FlankResponseTests.Maneuver();
        input.HasPlannedRelease = true;
        input.PlannedReleaseHours = releaseHours;
        return input;
    }

    static void FeasSync()
    {
        // The Center child never makes contact: BC11 alone would hold until the ceiling. The planned release 0.4 h after activation ends it.
        ManeuverInput input = WithRelease(10.4);
        var state = default(ManeuverState);
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.0));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "holds at the start");
        AssertEqual("await-planned-release", order.Reason, "for the planned release");
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.39)).Phase, "still holding just before it");
        order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.4));
        AssertEqual(ManeuverPhase.March, order.Phase, "marches at the planned release");
        AssertEqual("planned-release", state.ProceedReason, "and says why");

        // The Center's contact at 10.1 plus the 900 s offset would release at 10.35: the plan releases at 10.2, earlier, and at 10.8, later.
        ManeuverInput early = WithRelease(10.2);
        early.CenterContactHours = 10.1;
        var earlyState = default(ManeuverState);
        EnvelopmentManeuver.Step(ref earlyState, FlankResponseTests.After(early, 10.0));
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref earlyState, FlankResponseTests.After(early, 10.2)).Phase, "the plan releases before contact + offset");
        ManeuverInput late = WithRelease(10.8);
        late.CenterContactHours = 10.1;
        var lateState = default(ManeuverState);
        EnvelopmentManeuver.Step(ref lateState, FlankResponseTests.After(late, 10.0));
        ManeuverOrder waiting = EnvelopmentManeuver.Step(ref lateState, FlankResponseTests.After(late, 10.5));
        AssertEqual(ManeuverPhase.Hold, waiting.Phase, "and BC11's own go time no longer releases the wing");
        AssertEqual("await-planned-release", waiting.Reason, "it waits for the plan");
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref lateState, FlankResponseTests.After(late, 10.8)).Phase, "and leaves at it");

        // A template with no Center follows the plan too.
        ManeuverInput noCenter = WithRelease(10.3);
        noCenter.TemplateHasCenter = false;
        noCenter.StartOffsetSeconds = 0f;
        var noCenterState = default(ManeuverState);
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref noCenterState, FlankResponseTests.After(noCenter, 10.2)).Phase, "no offset to wait for, the plan still holds");
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref noCenterState, FlankResponseTests.After(noCenter, 10.3)).Phase, "and releases");
    }

    static void SyncUnset()
    {
        // No ledger entry: today's rule. The Center's contact is awaited, then the 900 s offset (0.25 h) counted from it.
        ManeuverInput input = FlankResponseTests.Maneuver();
        var state = default(ManeuverState);
        AssertEqual("await-center-contact", EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.5)).Reason, "waits for the Center's contact");
        input.CenterContactHours = 10.5;
        AssertEqual("hold-offset", EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.74)).Reason, "then for the offset");
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.75)).Phase, "marches at contact + offset");
        AssertEqual("center-contact", state.ProceedReason, "BC11's reason");
        AssertFalse(state.HasBudget || state.HasVia || state.HasAttackVia, "nothing latched without a ledger");

        ManeuverInput noCenter = FlankResponseTests.Maneuver();
        noCenter.TemplateHasCenter = false;
        var noCenterState = default(ManeuverState);
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref noCenterState, FlankResponseTests.After(noCenter, 10.2)).Phase, "offset not yet elapsed");
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref noCenterState, FlankResponseTests.After(noCenter, 10.25)).Phase, "activation + 900 battle-s");
        AssertEqual("offset", noCenterState.ProceedReason, "and the offset's reason");

        // The start-delay wing of a non-enveloping play: held for its offset, then released, as before.
        ManeuverInput delay = FlankResponseTests.Maneuver();
        delay.Envelops = false;
        delay.StartOffsetSeconds = 600f;
        var delayState = default(ManeuverState);
        AssertEqual("start-delay", EnvelopmentManeuver.Step(ref delayState, FlankResponseTests.After(delay, 10.1)).Reason, "start-delay holds");
        AssertEqual(ManeuverPhase.Released, EnvelopmentManeuver.Step(ref delayState, FlankResponseTests.After(delay, 10.17)).Phase, "released after 600 battle-s");
    }

    static ManeuverInput WithBudgetAndVias(double release)
    {
        ManeuverInput input = WithRelease(release);
        input.HasTimeBudget = true;
        input.TimeBudgetHours = 2.0;
        input.HasVia = true;
        input.ViaX = 100f;
        input.ViaZ = 200f;
        input.HasAttackVia = true;
        input.AttackViaX = 300f;
        input.AttackViaZ = 400f;
        return input;
    }

    static void SyncPinAtRelease()
    {
        ManeuverInput input = WithBudgetAndVias(10.4);
        var state = default(ManeuverState);
        EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.0));
        ManeuverOrder march = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.4));
        AssertEqual(ManeuverPhase.March, march.Phase, "marching");
        AssertTrue(march.HasVia && march.ViaX == 100f && march.ViaZ == 200f, "the March order carries the march via");
        AssertTrue(state.HasBudget && state.BudgetHours == 2.0, "the budget is latched");
        AssertTrue(state.HasVia && state.HasAttackVia, "both vias are latched");

        // The ledger recomputes everything and moves it all: nothing changes for a child already marching.
        input.TimeBudgetHours = 0.1;
        input.ViaX = 999f;
        input.ViaZ = 999f;
        input.AttackViaX = 888f;
        input.AttackViaZ = 888f;
        input.HasVia = false;
        march = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.5));
        AssertEqual(ManeuverPhase.March, march.Phase, "a shrunken budget does not abandon the march");
        AssertTrue(march.HasVia && march.ViaX == 100f && march.ViaZ == 200f, "the via stays the latched one");
        AssertEqual(2.0, state.BudgetHours, "the budget stays");
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 12.39)).Phase, "2 h after the March the budget is not yet spent");
        ManeuverOrder spent = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 12.41));
        AssertEqual(ManeuverPhase.Abandoned, spent.Phase, "and when it is the march times out");
        AssertEqual("timeout", spent.Reason, "reason");

        // The attack via is the latched one too.
        ManeuverInput attack = WithBudgetAndVias(10.4);
        var attackState = default(ManeuverState);
        EnvelopmentManeuver.Step(ref attackState, FlankResponseTests.After(attack, 10.0));
        EnvelopmentManeuver.Step(ref attackState, FlankResponseTests.After(attack, 10.4));
        attack.ChildArrived = true;
        attack.AttackViaX = 1f;
        attack.AttackViaZ = 2f;
        AssertEqual(ManeuverPhase.Turn, EnvelopmentManeuver.Step(ref attackState, FlankResponseTests.After(attack, 10.9)).Phase, "turns on arrival");
        ManeuverOrder go = EnvelopmentManeuver.Step(ref attackState, FlankResponseTests.After(attack, 10.96));
        AssertEqual(ManeuverPhase.Attack, go.Phase, "attacks");
        AssertTrue(go.HasVia && go.ViaX == 300f && go.ViaZ == 400f, "through the latched attack via");
        AssertEqual(800f, go.TargetX, "the attack's target is still the line end");
        AssertEqual(1500f, go.TargetZ, "the line end");
    }

    static void Budget()
    {
        AssertNear(3.05f, ManeuverFeasibility.TimeBudget(0.1f, 1.9f, 10f), 1e-4f, "1.5 x 2.0 + 0.05");
        AssertNear(1.0f, ManeuverFeasibility.TimeBudget(0f, 0.2f, 10f), 1e-4f, "never below today's hour with daylight to spare");
        AssertNear(0.6f, ManeuverFeasibility.TimeBudget(0.1f, 1.9f, 0.6f), 1e-4f, "with 0.6 h of daylight left, 0.6");
        AssertNear(EnvelopmentManeuver.TurnHoldHours, ManeuverFeasibility.TimeBudget(0.1f, 1.9f, 0f), 1e-4f, "and never less than the turn hold");

        // Latched at the March: the timeout counts the budget from it, not today's hour.
        ManeuverInput input = WithRelease(10.4);
        input.HasTimeBudget = true;
        input.TimeBudgetHours = 3.05;
        var state = default(ManeuverState);
        EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.0));
        EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.4));
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 11.5)).Phase, "past today's hour, inside the budget");
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 13.44)).Phase, "3.04 h after the March");
        AssertEqual("timeout", EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 13.46)).Reason, "3.06 h after the March");

        ManeuverInput plain = WithRelease(10.4);
        var plainState = default(ManeuverState);
        EnvelopmentManeuver.Step(ref plainState, FlankResponseTests.After(plain, 10.0));
        EnvelopmentManeuver.Step(ref plainState, FlankResponseTests.After(plain, 10.4));
        AssertEqual("timeout", EnvelopmentManeuver.Step(ref plainState, FlankResponseTests.After(plain, 11.41)).Reason, "no budget: today's hour");
    }

    static void CeilingLatch()
    {
        // A release that recedes with every tick (always half an hour ahead): the first one planned, 10.5, anchors the ceiling at 11.5.
        ManeuverInput input = WithRelease(10.5);
        var state = default(ManeuverState);
        double now = 10.0;
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, now));
        while (now < 11.39)
        {
            now += 0.1;
            input.PlannedReleaseHours = now + 0.5;
            order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, now));
            AssertEqual(ManeuverPhase.Hold, order.Phase, "still holding at " + now);
        }
        input.PlannedReleaseHours = 11.4 + 0.5;
        order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 11.5));
        AssertEqual(ManeuverPhase.March, order.Phase, "the ceiling ends the Hold at the first release + 1 h");
        AssertEqual("hold-ceiling", state.ProceedReason, "and says so");
        AssertTrue(state.HasFirstPlannedRelease && state.FirstPlannedReleaseHours == 10.5, "the first release is the latched one");

        // A fast wing waiting for a slow one: released 2.1 h after activation, the ceiling of 1 h from activation must not launch it first.
        ManeuverInput fast = WithRelease(12.1);
        var fastState = default(ManeuverState);
        EnvelopmentManeuver.Step(ref fastState, FlankResponseTests.After(fast, 10.0));
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref fastState, FlankResponseTests.After(fast, 11.05)).Phase, "past the play's own hour, still waiting for the plan");
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref fastState, FlankResponseTests.After(fast, 12.0)).Phase, "and at 2 h");
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref fastState, FlankResponseTests.After(fast, 12.1)).Phase, "it leaves at its release");
        AssertEqual("planned-release", fastState.ProceedReason, "not by the ceiling");

        // Without a plan the ceiling is today's: an hour from the activation.
        ManeuverInput none = FlankResponseTests.Maneuver();
        var noneState = default(ManeuverState);
        EnvelopmentManeuver.Step(ref noneState, FlankResponseTests.After(none, 10.0));
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref noneState, FlankResponseTests.After(none, 11.0)).Phase, "BC11's ceiling");
        AssertEqual("hold-ceiling", noneState.ProceedReason, "as before");
    }

    static void FixHold()
    {
        // The spec a Center or Demonstration child gets in a template with an enveloping rect.
        foreach (PlayRectKind kind in new[] { PlayRectKind.Center, PlayRectKind.Demonstration })
        {
            var fixes = new CorpsAssignment("child-1", kind, 0, 0f, 600f, 0f, envelops: false, templateEnvelops: true);
            ManeuverSpec spec = DirectChildAllocator.ManeuverOf(fixes);
            AssertTrue(spec.HasValue && spec.HoldsForRelease && !spec.Envelops, kind + " of an enveloping template holds for the release");
            var plain = new CorpsAssignment("child-1", kind, 0, 0f, 600f, 0f, envelops: false, templateEnvelops: false);
            AssertFalse(DirectChildAllocator.ManeuverOf(plain).HasValue, kind + " of a template with no enveloping rect has no maneuver");
        }
        var wing = new CorpsAssignment("child-2", PlayRectKind.LeftWing, 1, -300f, 0f, 600f, envelops: false, templateEnvelops: true);
        ManeuverSpec wingSpec = DirectChildAllocator.ManeuverOf(wing);
        AssertTrue(wingSpec.HasValue && !wingSpec.HoldsForRelease, "a start-delay wing keeps its own offset");

        // The fix holds at its position through the start-delay path and is Released at its planned hour.
        ManeuverInput input = FlankResponseTests.Maneuver();
        input.Kind = PlayRectKind.Center;
        input.Envelops = false;
        input.HoldsForRelease = true;
        input.StartOffsetSeconds = 0f;
        input.ChildX = 12f;
        input.ChildZ = 34f;
        input.HasPlannedRelease = true;
        input.PlannedReleaseHours = 10.6;
        var state = default(ManeuverState);
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.0));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "holds");
        AssertEqual(CommandTaskType.FormUp, order.Task, "FormUp");
        AssertEqual(12f, order.TargetX, "at its own position");
        AssertEqual(34f, order.TargetZ, "z");
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.59)).Phase, "until the hour");
        order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.6));
        AssertEqual(ManeuverPhase.Released, order.Phase, "Released at its planned hour");
        AssertEqual("planned-release", order.Reason, "reason");
        AssertEqual(ManeuverPhase.Released, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.7)).Phase, "and it stays released");

        // A fix in contact, a deployed reserve or a child with no ledger entry has no planned release: never held.
        ManeuverInput noEntry = input;
        noEntry.HasPlannedRelease = false;
        var noEntryState = default(ManeuverState);
        ManeuverOrder none = EnvelopmentManeuver.Step(ref noEntryState, FlankResponseTests.After(noEntry, 10.0));
        AssertEqual(ManeuverPhase.None, none.Phase, "no release: no hold");
        AssertFalse(none.Overrides, "and no override");
        ManeuverInput past = input;
        past.PlannedReleaseHours = 9.9;
        var pastState = default(ManeuverState);
        AssertEqual(ManeuverPhase.None, EnvelopmentManeuver.Step(ref pastState, FlankResponseTests.After(past, 10.0)).Phase, "a release already behind: not held");

        // The wings' ceiling stands for the fix too: a release that keeps receding does not hold it for good.
        ManeuverInput receding = input;
        var recedingState = default(ManeuverState);
        receding.PlannedReleaseHours = 10.5;
        EnvelopmentManeuver.Step(ref recedingState, FlankResponseTests.After(receding, 10.0));
        receding.PlannedReleaseHours = 12.0;
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref recedingState, FlankResponseTests.After(receding, 11.4)).Phase, "held while the ceiling is ahead (11.4 < 11.5)");
        receding.PlannedReleaseHours = 12.4;
        order = EnvelopmentManeuver.Step(ref recedingState, FlankResponseTests.After(receding, 11.5));
        AssertEqual(ManeuverPhase.Released, order.Phase, "released by the ceiling");
        AssertEqual("hold-ceiling", order.Reason, "reason");

        // A Defender never gets it.
        ManeuverInput defender = input;
        defender.Stance = BattleStance.Defender;
        var defenderState = default(ManeuverState);
        AssertEqual(ManeuverPhase.None, EnvelopmentManeuver.Step(ref defenderState, FlankResponseTests.After(defender, 10.0)).Phase, "no maneuver for a Defender");
    }

    static void MirrorFallback()
    {
        TacticalFrontAxis axis = TacticalFrontAxis.FromCentroids(0f, 0f, 0f, 1500f).WithOwnExtent(-300f, 300f);
        // A plain spec mirrors across the axis; a spec projected mirrored goes back to its authored anchor.
        var plain = new ManeuverSpec(PlayRectKind.FlankMarch, 1000f, 500f, 900f, envelops: true);
        EnvelopmentManeuver.AnchorFor(in axis, in plain, false, out float x, out float z);
        AssertEqual(1000f, x, "unflipped plain x");
        EnvelopmentManeuver.AnchorFor(in axis, in plain, true, out x, out z);
        AssertNear(-1000f, x, 0.01f, "flipped plain: mirrored across the axis");
        AssertNear(500f, z, 0.01f, "at the same depth");
        var projected = new ManeuverSpec(PlayRectKind.FlankMarch, -255f, 300f, 900f, envelops: true, projectedMirrored: true, authoredAnchorX: 255f, authoredAnchorZ: 300f);
        EnvelopmentManeuver.AnchorFor(in axis, in projected, false, out x, out z);
        AssertEqual(-255f, x, "a mirrored projection's anchor is its own");
        EnvelopmentManeuver.AnchorFor(in axis, in projected, true, out x, out z);
        AssertEqual(255f, x, "its one mirrored try goes to the authored anchor");
        AssertEqual(300f, z, "authored z");

        // BC11's own mirrored try drops the ledger's plan for the child: the release, the budget and both vias were computed for the other side.
        ManeuverInput input = WithBudgetAndVias(10.5);
        input.CanMirror = true;
        var state = default(ManeuverState);
        EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.0));
        AssertTrue(state.HasFirstPlannedRelease, "the first release was latched in the Hold");
        input.InwardLegHitsRiver = true;
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.5));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "the failed check at the release mirrors back to Hold");
        AssertEqual("mirror-flank", order.Reason, "reason");
        AssertTrue(state.Mirrored, "on the mirrored side");
        AssertFalse(state.HasFirstPlannedRelease, "the first planned release is dropped");
        AssertFalse(state.HasBudget || state.HasVia || state.HasAttackVia, "and no budget or via survives the flip");

        // The runtime offers no ledger plan after the flip, and BC11's rule governs the child.
        input.HasPlannedRelease = false;
        input.HasTimeBudget = false;
        input.HasVia = false;
        input.HasAttackVia = false;
        input.InwardLegHitsRiver = false;
        AssertEqual("await-center-contact", EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.6)).Reason, "waits for the Center's contact as BC11 does");
        input.CenterContactHours = 10.6;
        AssertEqual("hold-offset", EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.84)).Reason, "then for the offset");
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.86)).Phase, "and marches at contact + offset");
        AssertFalse(state.HasBudget || state.HasVia, "with no budget or via");
        // A second failure is final.
        input.InwardLegHitsRiver = true;
        AssertEqual(ManeuverPhase.Abandoned, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.9)).Phase, "the second failure is final");
    }

    static void StateFlags()
    {
        var state = default(ManeuverState);
        AssertFalse(state.HasFirstPlannedRelease, "no first planned release");
        AssertFalse(state.HasBudget, "no budget");
        AssertFalse(state.HasVia, "no via");
        AssertFalse(state.HasAttackVia, "no attack via");
        AssertEqual(0.0, state.FirstPlannedReleaseHours, "the hour fields hold 0");

        // 0 is a valid hour: a release at hour 0.0 is a release, not the absence of one.
        ManeuverInput input = FlankResponseTests.Maneuver(0.0);
        input.PlayActivatedHours = 0.0;
        input.HasPlannedRelease = true;
        input.PlannedReleaseHours = 0.0;
        input.HasTimeBudget = true;
        input.TimeBudgetHours = 0.5;
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 0.0));
        AssertEqual(ManeuverPhase.March, order.Phase, "a release at hour 0 releases at hour 0");
        AssertEqual("planned-release", state.ProceedReason, "by the plan");
        AssertTrue(state.HasFirstPlannedRelease && state.HasBudget, "latched with their flags");
        AssertEqual(0.5, state.BudgetHours, "the budget is latched");
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 0.49)).Phase, "inside it");
        AssertEqual("timeout", EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 0.51)).Reason, "and the march times out on it, counted from hour 0");

        // The state is reset where a Defender is: the flags go with it.
        input.Stance = BattleStance.Defender;
        EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 0.02));
        AssertFalse(state.HasFirstPlannedRelease || state.HasBudget || state.HasVia || state.HasAttackVia, "reset to default: every flag false");
    }

    static void ViaPassed()
    {
        ManeuverInput input = WithBudgetAndVias(10.4);
        var state = default(ManeuverState);
        EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.0));
        ManeuverOrder crossing = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.4));
        AssertTrue(crossing.HasVia && crossing.ViaX == 100f, "marching through the via");
        var withVia = new[] { new ChildManeuver("child-7", crossing, 1f, 0f) };
        int hashWith = EnvelopmentManeuver.Hash(withVia);
        input.ViaPassed = true;
        ManeuverOrder past = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.5));
        AssertEqual(ManeuverPhase.March, past.Phase, "still marching");
        AssertFalse(past.HasVia, "past the via the March order carries none");
        AssertEqual(1000f, past.TargetX, "to the anchor");
        AssertTrue(hashWith != EnvelopmentManeuver.Hash(new[] { new ChildManeuver("child-7", past, 1f, 0f) }), "and the cleared via re-stores the orders");
        input.ViaPassed = false;
        AssertTrue(EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.6)).HasVia, "the via rides again while the child is not past it");

        // The attack via is dropped the same way.
        input.ChildArrived = true;
        EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.9));
        ManeuverOrder attack = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.96));
        AssertTrue(attack.Phase == ManeuverPhase.Attack && attack.HasVia && attack.ViaX == 300f, "the attack goes through the attack via");
        input.AttackViaPassed = true;
        ManeuverOrder pastAttack = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 11.0));
        AssertEqual(ManeuverPhase.Attack, pastAttack.Phase, "still attacking");
        AssertFalse(pastAttack.HasVia, "past the attack via the order carries none");
        AssertEqual(800f, pastAttack.TargetX, "to the line end");
    }

    static void ViaRouteCheck()
    {
        // Without a via the leg is one query.
        var none = default(EnvelopmentManeuver.MarchRouteCheck);
        AssertEqual(0, EnvelopmentManeuver.HalfToPlan(in none, false, 0f, 0f), "no via: the whole leg");
        EnvelopmentManeuver.MarchRouteCheck whole = EnvelopmentManeuver.ApplyHalf(in none, 0, true, false, 10.0, 1000f, 500f, false, 0f, 0f);
        AssertTrue(whole.Valid && whole.HitsRiver && !whole.NoPath, "a whole leg is the result");

        // With a via, the first query is child to via, the second via to anchor.
        AssertEqual(1, EnvelopmentManeuver.HalfToPlan(in none, true, 400f, 250f), "with a via: the first half first");
        EnvelopmentManeuver.MarchRouteCheck staged = EnvelopmentManeuver.ApplyHalf(in none, 1, false, false, 10.0, 1000f, 500f, true, 400f, 250f);
        AssertFalse(staged.Valid, "after one half the leg has no result yet");
        AssertEqual(2, EnvelopmentManeuver.HalfToPlan(in staged, true, 400f, 250f), "the second half is next");
        AssertTrue(EnvelopmentManeuver.NeedsRouteRecheck(in staged, 10.0, 1000f, 500f, true, 400f, 250f), "and it is planned on the next step, not after the interval");
        EnvelopmentManeuver.MarchRouteCheck done = EnvelopmentManeuver.ApplyHalf(in staged, 2, false, false, 10.05, 1000f, 500f, true, 400f, 250f);
        AssertTrue(done.Valid && !done.HitsRiver && !done.NoPath, "both halves clear: a clear leg");
        AssertFalse(done.FirstDone, "the staging is cleared");
        AssertFalse(EnvelopmentManeuver.NeedsRouteRecheck(in done, 10.06, 1000f, 500f, true, 400f, 250f), "no recheck inside the interval");
        AssertEqual(1, EnvelopmentManeuver.HalfToPlan(in done, true, 400f, 250f), "a recheck starts again from the first half");

        // A river or a missing path on either half is the leg's.
        EnvelopmentManeuver.MarchRouteCheck firstRiver = EnvelopmentManeuver.ApplyHalf(in none, 1, true, false, 10.0, 1000f, 500f, true, 400f, 250f);
        AssertTrue(EnvelopmentManeuver.ApplyHalf(in firstRiver, 2, false, false, 10.05, 1000f, 500f, true, 400f, 250f).HitsRiver, "a river on the first half");
        EnvelopmentManeuver.MarchRouteCheck firstNoPath = EnvelopmentManeuver.ApplyHalf(in none, 1, false, true, 10.0, 1000f, 500f, true, 400f, 250f);
        AssertTrue(EnvelopmentManeuver.ApplyHalf(in firstNoPath, 2, false, false, 10.05, 1000f, 500f, true, 400f, 250f).NoPath, "no path on the first half");
        AssertTrue(EnvelopmentManeuver.ApplyHalf(in staged, 2, true, false, 10.05, 1000f, 500f, true, 400f, 250f).HitsRiver, "a river on the second half");

        // A result for another via (moved on by more than 50 m) is no result, and so is one for a leg that gained or lost its via.
        AssertFalse(EnvelopmentManeuver.CheckForLeg(in done, 1000f, 500f, true, 500f, 250f).Valid, "the via moved on: no result");
        AssertTrue(EnvelopmentManeuver.CheckForLeg(in done, 1000f, 500f, true, 430f, 250f).Valid, "the via drifted 30 m: the same leg");
        AssertFalse(EnvelopmentManeuver.CheckForLeg(in done, 1000f, 500f, false, 0f, 0f).Valid, "the via is gone: a different leg");
        AssertFalse(EnvelopmentManeuver.CheckForLeg(in whole, 1000f, 500f, true, 400f, 250f).Valid, "and one that gained a via");
        AssertTrue(EnvelopmentManeuver.NeedsRouteRecheck(in done, 10.06, 1000f, 500f, true, 500f, 250f), "a moved via is a new leg to plan");
    }

    // A grid with a river (id 4) on x in [400, 440) and a second on [600, 640) over the whole map, world z = iz - 1200.
    static BattleTerrainGrid TwoRiversGrid()
    {
        var ids = new int[1201, 1201];
        for (int iz = 0; iz < 1201; iz++)
        {
            for (int ix = 400; ix < 440; ix++) ids[ix, iz] = BattleTerrainGrid.RiverId;
            for (int ix = 600; ix < 640; ix++) ids[ix, iz] = BattleTerrainGrid.RiverId;
        }
        return new BattleTerrainGrid(ids, 1200f, 1200f, null, false, null, null, null, 0);
    }

    static void InwardViaBc11()
    {
        BattleTerrainGrid grid = TwoRiversGrid();
        var scratch = new int[2048];
        float footprint = RiverRegions.CrossingLinkMeters;
        // The anchor is on the near bank at (200, -600) and the line end at (560, -600), beyond the first river only; the ford is at (420, -600).
        AssertTrue(EnvelopmentManeuver.InwardLegHitsRiver(grid, 200f, -600f, false, 0f, 0f, 560f, -600f, footprint, scratch), "without a via the straight leg crosses the river");
        AssertFalse(EnvelopmentManeuver.InwardLegHitsRiver(grid, 200f, -600f, true, 420f, -600f, 560f, -600f, footprint, scratch),
            "with the via at the ford the sub-legs touch river only inside its footprint");
        // A sub-leg across open river elsewhere still counts: the line end beyond the second river.
        AssertTrue(EnvelopmentManeuver.InwardLegHitsRiver(grid, 200f, -600f, true, 420f, -600f, 800f, -600f, footprint, scratch),
            "the second river, far from the via, is open river");
        // A via that is not on the river does not excuse the crossing.
        AssertTrue(EnvelopmentManeuver.InwardLegHitsRiver(grid, 200f, -600f, true, 250f, -600f, 560f, -600f, footprint, scratch),
            "a via away from the river excuses nothing");
        AssertFalse(EnvelopmentManeuver.InwardLegHitsRiver(null, 200f, -600f, true, 420f, -600f, 560f, -600f, footprint, scratch), "no grid: no verdict");
        AssertFalse(EnvelopmentManeuver.InwardLegHitsRiver(grid, 200f, -600f, false, 0f, 0f, 300f, -600f, footprint, scratch), "a leg on the near bank is clear");

        // BC11 abandons the wing on the straight check and marches it, then attacks through the via, on the sub-leg check.
        bool straight = EnvelopmentManeuver.InwardLegHitsRiver(grid, 200f, -600f, false, 0f, 0f, 560f, -600f, footprint, scratch);
        bool subLegs = EnvelopmentManeuver.InwardLegHitsRiver(grid, 200f, -600f, true, 420f, -600f, 560f, -600f, footprint, scratch);
        ManeuverInput input = WithRelease(10.0);
        input.HasAttackVia = true;
        input.AttackViaX = 420f;
        input.AttackViaZ = -600f;
        input.InwardLegHitsRiver = straight;
        var abandonedState = default(ManeuverState);
        ManeuverOrder abandoned = EnvelopmentManeuver.Step(ref abandonedState, FlankResponseTests.After(input, 10.0));
        AssertEqual(ManeuverPhase.Abandoned, abandoned.Phase, "the straight check abandons the wing");
        AssertEqual("river-on-leg", abandoned.Reason, "river-on-leg");
        input.InwardLegHitsRiver = subLegs;
        var state = default(ManeuverState);
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.0)).Phase, "the sub-leg check lets the wing go");
        input.ChildArrived = true;
        EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.2));
        ManeuverOrder attack = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.26));
        AssertEqual(ManeuverPhase.Attack, attack.Phase, "attacks");
        AssertTrue(attack.HasVia && attack.ViaX == 420f && attack.ViaZ == -600f, "and its Attack order carries the via");
    }

    static void ReaderNodeCopy()
    {
        var nodes = new[]
        {
            new CommandNodeOperationalState("node-7", CommandEchelonKind.CorpsLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, CommandTaskState.Planning),
            new CommandNodeOperationalState("node-70", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, CommandTaskState.Planning),
            new CommandNodeOperationalState("node-71", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, CommandTaskState.Planning),
        };
        var intents = new[]
        {
            FlankResponseTests.NodeIntent("node-7", "node-7"), FlankResponseTests.NodeIntent("node-70", "node-7"), FlankResponseTests.NodeIntent("node-71", "node-7"),
        };
        var march = new ManeuverOrder
        {
            Phase = ManeuverPhase.March, Task = CommandTaskType.AdvanceToAssembly, TargetX = 1000f, TargetZ = 500f, Heading = -1f, Reason = "march-anchor",
            HasVia = true, ViaX = 100f, ViaZ = 200f,
        };
        var withVia = new[] { new ChildManeuver("child-7", march, 1f, 0f) };
        ManeuverNodeTable table = EnvelopmentManeuver.ExpandToNodes(withVia, intents);
        foreach (string id in new[] { "node-7", "node-70", "node-71" })
        {
            AssertTrue(table.TryGet(id, out ManeuverNodeEntry entry), id + " has an entry");
            AssertTrue(entry.HasVia && entry.ViaX == 100f && entry.ViaZ == 200f, id + " carries the via");
        }
        CommandDoctrineOrder[] orders = FlankResponseTests.DoctrineOrders(nodes, BattleStance.Meeting, table);
        for (int i = 0; i < orders.Length; i++)
        {
            AssertTrue(orders[i].HasVia, "node " + i + "'s doctrine order has the via");
            AssertEqual(100f, orders[i].ViaX, "via x");
            AssertEqual(200f, orders[i].ViaZ, "via z");
        }

        var cleared = march;
        cleared.HasVia = false;
        cleared.ViaX = 0f;
        cleared.ViaZ = 0f;
        var withoutVia = new[] { new ChildManeuver("child-7", cleared, 1f, 0f) };
        ManeuverNodeTable clearedTable = EnvelopmentManeuver.ExpandToNodes(withoutVia, intents);
        clearedTable.TryGet("node-70", out ManeuverNodeEntry none);
        AssertFalse(none.HasVia, "a cleared via is none on the entry");
        foreach (CommandDoctrineOrder order in FlankResponseTests.DoctrineOrders(nodes, BattleStance.Meeting, clearedTable)) AssertFalse(order.HasVia, "and on the order");

        // The hash sees a set, a moved and a cleared via, so the orders are re-stored; a drift inside a 50 m bucket is no change.
        int hasVia = EnvelopmentManeuver.Hash(withVia);
        AssertTrue(hasVia != EnvelopmentManeuver.Hash(withoutVia), "a cleared via changes the hash");
        var moved = march;
        moved.ViaX = 300f;
        AssertTrue(hasVia != EnvelopmentManeuver.Hash(new[] { new ChildManeuver("child-7", moved, 1f, 0f) }), "a moved via changes the hash");
        var drifted = march;
        drifted.ViaX = 110f;
        AssertEqual(hasVia, EnvelopmentManeuver.Hash(new[] { new ChildManeuver("child-7", drifted, 1f, 0f) }), "a 10 m drift in the same bucket does not");

        // An Attack order's via travels the same way.
        var attack = new ManeuverOrder
        {
            Phase = ManeuverPhase.Attack, Task = CommandTaskType.AttackObjective, TargetX = 800f, TargetZ = 1500f, Heading = -1f, Reason = "attack-line-end",
            HasVia = true, ViaX = 300f, ViaZ = 400f,
        };
        ManeuverNodeTable attackTable = EnvelopmentManeuver.ExpandToNodes(new[] { new ChildManeuver("child-7", attack, 1f, 0f) }, intents);
        attackTable.TryGet("node-7", out ManeuverNodeEntry attackEntry);
        AssertTrue(attackEntry.HasVia && attackEntry.ViaX == 300f, "the attack via reaches the entry");
    }

    static void LedgerChildTable()
    {
        // Scenario 1: the wing leaves at once, the fix waits; the table is the selected plan's.
        FeasField f = ThreeCorps();
        f.Legs = ScenarioLegs(f, (m, b) => b.Envelops ? 555f : (Math.Abs(m.TargetX - FeasField.X(0f)) < 1f ? (float?)200f : null), BattlePlanId.ManeuverAndFix);
        FeasibilityLedger ledger = f.Evaluate(BattlePlanId.ManeuverAndFix);
        AssertFalse(ledger.TryPlannedRelease(f.ChildIds[0], out _, out _, out _, out _, out _, out _), "no selection: no table");
        ledger.CaptureSelection(BattlePlanId.ManeuverAndFix, false);
        AssertEqual(BattlePlanId.ManeuverAndFix, ledger.SelectedPlan, "the selection is kept");
        CandidateResult maf = Result(ledger, BattlePlanId.ManeuverAndFix);
        MemberEstimate[] members = ledger.Members(in maf).ToArray();
        int wings = 0, fixes = 0;
        foreach (MemberEstimate m in members)
        {
            if (m.Kind == ManeuverFeasibility.MemberOther) continue;
            AssertTrue(ledger.TryPlannedRelease(m.ChildId, out double release, out double budget, out float vx, out float vz, out float avx, out float avz),
                m.ChildId + " has a release");
            AssertNear((float)(f.Now + m.ReleaseHours), (float)release, 1e-3f, m.ChildId + ": the release is the evaluation hour plus the member's");
            AssertTrue(float.IsNaN(vx) && float.IsNaN(vz) && float.IsNaN(avx) && float.IsNaN(avz), "no river, no via");
            if (m.Kind == ManeuverFeasibility.MemberEnvelops)
            {
                wings++;
                AssertNear(0f, m.ReleaseHours, 1e-4f, "the wing leaves at once");
                AssertNear(ManeuverFeasibility.TimeBudget(m.CourierHours, m.MarchHours, f.DaylightHours), (float)budget, 1e-3f, "its budget is the model's");
                AssertTrue(budget >= EnvelopmentManeuver.MaxManeuverHours - 1e-4, "never below today's hour with daylight to spare");
            }
            else
            {
                fixes++;
                AssertEqual(0.0, budget, "a fix has no budget");
                AssertNear(0.63f, m.ReleaseHours, 0.05f, "the fix waits for the wing");
            }
        }
        AssertTrue(wings == 1 && fixes >= 1, "one wing and its fix are in the table");
        AssertFalse(ledger.TryPlannedRelease("child-nobody", out _, out _, out _, out _, out _, out _), "a child with no entry has none");

        // A deployed reserve is not in the plan: it has no release.
        FeasField g = ThreeCorps();
        g.AddCorps(600f, -300f, 6000f, deployedReserve: true);
        g.Legs = ScenarioLegs(g, (m, b) => b.Envelops ? 555f : (float?)200f, BattlePlanId.ManeuverAndFix);
        FeasibilityLedger withReserve = g.Evaluate(BattlePlanId.ManeuverAndFix);
        withReserve.CaptureSelection(BattlePlanId.ManeuverAndFix, false);
        AssertFalse(withReserve.TryPlannedRelease(g.ChildIds[g.ChildIds.Count - 1], out _, out _, out _, out _, out _, out _), "the deployed reserve is never held");

        // Scenario 4 at two to one: the wing crosses at the ford (link 1); the via is that crossing and its far region the wing's rect.
        FeasField four = Scenario4(2f);
        FeasibilityLedger crossing = Evaluate4(four);
        CandidateResult r4 = Result(crossing, BattlePlanId.ManeuverAndFix);
        crossing.CaptureSelection(BattlePlanId.ManeuverAndFix, r4.Mirrored);
        MemberEstimate wing = default(MemberEstimate);
        foreach (MemberEstimate m in crossing.Members(in r4).ToArray()) if (m.Kind == ManeuverFeasibility.MemberEnvelops) wing = m;
        AssertEqual(1, wing.MarchVia, "the wing crosses at link 1");
        AssertTrue(crossing.TryPlannedRelease(wing.ChildId, out _, out double budget4, out float viaX, out float viaZ, out float attackX, out float attackZ), "has a release");
        four.Regions.LinkCrossing(1, out float lx, out float lz, out _, out _);
        AssertNear(lx, viaX, 1e-3f, "the via is the link's crossing: x");
        AssertNear(lz, viaZ, 1e-3f, "z");
        AssertTrue(float.IsNaN(attackX) && float.IsNaN(attackZ), "and the inward leg has none");
        AssertTrue(budget4 >= EnvelopmentManeuver.MaxManeuverHours - 1e-4, "budget");
        AssertTrue(crossing.TryViaRegions(wing.ChildId, out int viaFar, out int attackFar), "the far regions are known");
        AssertEqual(four.Regions.RegionAt(wing.TargetX, wing.TargetZ), viaFar, "the via leads into the region of the wing's rect");
        AssertEqual(-1, attackFar, "no attack via, no far region");
        AssertFalse(crossing.TryViaRegions("child-nobody", out _, out _), "a child with no entry has none");
    }

    static void ManeuverWired()
    {
        string runtime = FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "PlayExecutorRuntime.cs");
        FlankResponseTests.AssertPinned(runtime, "if (feasibility != null && !mirrored && feasibility.SelectedPlan == planId", "the ledger's plan is read for the selected plan and BC11's unflipped side only");
        FlankResponseTests.AssertPinned(runtime, "bool fixInContact = input.HoldsForRelease && input.ChildInContact;", "a fix in contact is never held");
        FlankResponseTests.AssertPinned(runtime, "planned.HasRelease = !fixInContact && beforeMarch;", "and the release is read before the March only");
        FlankResponseTests.AssertPinned(runtime, "float fromX = half == 2 ? viaX : child.X, fromZ = half == 2 ? viaZ : child.Z;", "the march leg's first half starts at the child, its second at the via");
        FlankResponseTests.AssertPinned(runtime, "float toX = half == 1 ? viaX : anchorX, toZ = half == 1 ? viaZ : anchorZ;", "and the first ends at the via");
        FlankResponseTests.AssertPinned(runtime, "EnvelopmentManeuver.AnchorFor(in axis, in spec, mirrored, out x, out z)", "the anchor after the flip is AnchorFor's");
        FlankResponseTests.AssertPinned(runtime, "HoldsForRelease = spec.HoldsForRelease,", "the spec's fix hold reaches the input");
    }
}
