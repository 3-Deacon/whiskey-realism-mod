using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Util;
using static TestHarness;

// BC13 task 6: the exploit lifecycle, the exploit table and the flank-emergency feedback (spec § 6,
// requirement-exploit-lifecycle, requirement-lifecycle-contract, requirement-defender-counterstroke).
// Frame: forward +z, lateral x. Times are binary-exact doctrine hours (multiples of 1/64) wherever a test sits on a
// boundary, so the comparison under test is the code's and not floating-point rounding's.
internal static partial class CommanderOpportunityTests
{
    private static (string, Action)[] LifeTests()
    {
        return new (string, Action)[]
        {
            ("bc13 LIFE-PHASES Advance, Strike, Consolidate, Recall on a gap fixture, one transition per step", LifePhases),
            ("bc13 LIFE-PHASES contact within 250 m opens Strike; being held needs the aim and an empty ground", LifeStrikeEntry),
            ("bc13 LIFE-PHASES the order carries the phase task and reason literal", LifeOrderShape),
            ("bc13 LIFE-CAV-STANDOFF the cavalry override target is 60 m short of the aim on the approach bearing", LifeCavStandOff),
            ("bc13 LIFE-AIM the aim follows a perceived target, a Gap and a FlankInAir aim is frozen", LifeAim),
            ("bc13 LIFE-PURSUE cavalry with audacity 0.6 pursues a routed target at most 400 m, infantry never", LifePursue),
            ("bc13 LIFE-ABORTS one case per ExploitEnd reason", LifeAborts),
            ("bc13 LIFE-ABORTS the odds, loss and target-lost boundaries", LifeAbortBoundaries),
            ("bc13 LIFE-PRECEDENCE two aborts on one tick resolve by the listed order", LifePrecedence),
            ("bc13 LIFE-PRECEDENCE opening-lost never fires after Strike, odds only in Advance and Strike, timeout never in Consolidate", LifePhaseScopedAborts),
            ("bc13 LIFE-CLOCK a frozen doctrine clock neither times out nor consolidates; every elapsed test is >=", LifeClock),
            ("bc13 LIFE-COUNTER-AIM a penetrating sample falling back across the own trace freezes the aim there", LifeCounterAim),
            ("bc13 COUNTER-BOUNDS a Penetration exploit never pursues, stops following past the bound, and Recall always follows", LifeCounterBounds),
            ("bc13 LIFE-TABLE the table overrides the asset, follows its subtree and clears", LifeTable),
            ("bc13 OPP-CLAIM-RELEASE the claim is held while the entry lives, released at Recall, hesitation only after an abort", LifeClaimRelease),
            ("bc13 LIFE-FEEDBACK the flank emergency is set by the posture producer and taken once", LifeFeedback),
        };
    }

    // ------------------------------------------------------------------ fixtures

    private const double T0 = 16.0;

    private static ExploitStepInput Calm(double now, float assetX, float assetZ)
    {
        return new ExploitStepInput
        {
            NowHours = now, Audacity01 = 0.5f, CasualtyTolerance01 = 0.5f, ShakenMorale = 0.45f,
            AssetAlive = true, AssetX = assetX, AssetZ = assetZ, AssetLoss = 0.05f, AssetMorale = 0.8f,
            OpeningLive = true, LocalOdds = float.PositiveInfinity,
            NearestVisibleEnemyToAsset = 5000f, NearestEnemyToAim = 5000f,
        };
    }

    // The step input of an entry that tracks a target: the target is perceived where the aim is.
    private static ExploitStepInput Tracking(double now, float assetX, float assetZ, float targetX, float targetZ)
    {
        ExploitStepInput s = Calm(now, assetX, assetZ);
        s.HasTarget = true; s.TargetPerceived = true; s.TargetX = targetX; s.TargetZ = targetZ;
        s.NearestEnemyToAim = 0f;
        return s;
    }

    private static ExploitEntry LaunchOf(OpeningKind kind, bool cavalry, float aimX, float aimZ, int target = -1,
        float assetX = 0f, float assetZ = 0f, double now = T0, float required = 1f, float heading = 0f)
    {
        var asset = new ExploitAsset { Id = 100, OwnIndex = 3, X = assetX, Z = assetZ, Cavalry = cavalry, LossFraction = 0.05f };
        var opening = new Opening { Id = 5, Kind = kind, AimX = aimX, AimZ = aimZ, Heading = heading, TargetObservation = target };
        return ExploitLifecycle.Launch(1, 7, 50, in asset, in opening, required, now);
    }

    private static ExploitPhase StepTo(ref ExploitEntry e, in ExploitStepInput input)
    {
        ExploitLifecycle.Step(ref e, in input);
        return e.Phase;
    }

    // ------------------------------------------------------------------ phases

    private static void LifePhases()
    {
        ExploitEntry e = LaunchOf(OpeningKind.Gap, false, 0f, 600f);
        AssertEqual(ExploitPhase.Advance, e.Phase, "a launch starts in Advance");
        AssertEqual(ExploitEnd.None, e.End, "no end yet");
        AssertEqual(T0, e.LaunchHours, "launch hour");
        AssertNear(0.05f, e.LaunchLoss, 1e-6f, "the asset's loss at launch is kept");

        ExploitStepInput s = Calm(T0 + 0.015625, 0f, 300f);
        AssertEqual(ExploitPhase.Advance, StepTo(ref e, in s), "300 m out is still Advance");

        s = Calm(T0 + 0.03125, 0f, 470f);
        AssertEqual(ExploitPhase.Strike, StepTo(ref e, in s), "130 m from the aim is Strike");
        AssertEqual(T0 + 0.03125, e.PhaseSinceHours, "the phase hour is the transition's");

        s = Calm(T0 + 0.046875, 0f, 590f);
        AssertEqual(ExploitPhase.Consolidate, StepTo(ref e, in s), "within strike distance and no enemy within 150 m of the aim is held");
        AssertEqual(T0 + 0.046875, e.PhaseSinceHours, "consolidate hour");

        s = Calm(T0 + 0.046875 + 0.09375, 0f, 600f);
        AssertEqual(ExploitPhase.Consolidate, StepTo(ref e, in s), "0.09375 h is under ConsolidateHours");
        AssertEqual(ExploitEnd.None, e.End, "no end while consolidating");

        s = Calm(T0 + 0.046875 + 0.125, 0f, 600f);
        AssertEqual(ExploitPhase.Recall, StepTo(ref e, in s), "ConsolidateHours elapsed is Recall");
        AssertEqual(ExploitEnd.Success, e.End, "after a completed Consolidate the end is Success");

        ExploitLifecycle.Step(ref e, Calm(T0 + 5.0, 0f, 600f));
        AssertEqual(ExploitEnd.Success, e.End, "Recall is terminal");
        AssertEqual(ExploitPhase.Recall, e.Phase, "and stays");
    }

    private static void LifeStrikeEntry()
    {
        ExploitEntry e = LaunchOf(OpeningKind.ShakenSector, false, 0f, 600f, target: 9);
        ExploitStepInput s = Calm(T0 + 0.015625, 0f, 100f);
        s.HasTarget = true; s.TargetPerceived = true; s.TargetX = 0f; s.TargetZ = 600f;
        s.NearestVisibleEnemyToAsset = 260f;
        AssertEqual(ExploitPhase.Advance, StepTo(ref e, in s), "a visible enemy 260 m from the asset is not contact");
        s.NowHours = T0 + 0.03125;
        s.NearestVisibleEnemyToAsset = 250f;
        AssertEqual(ExploitPhase.Strike, StepTo(ref e, in s), "first visible contact within 250 m opens Strike, far from the aim");

        // Strike holds while the target is perceived, steady and near the aim: the asset is at the aim but an enemy stands 100 m off.
        s = Calm(T0 + 0.046875, 0f, 590f);
        s.HasTarget = true; s.TargetPerceived = true; s.TargetX = 0f; s.TargetZ = 600f;
        s.NearestEnemyToAim = 100f;
        AssertEqual(ExploitPhase.Strike, StepTo(ref e, in s), "an enemy within 150 m of the aim is not held");

        // Held needs the asset within its strike distance as well: the aim is empty but the asset is 200 m short.
        ExploitEntry g = LaunchOf(OpeningKind.Gap, false, 0f, 600f);
        ExploitStepInput c = Calm(T0 + 0.015625, 0f, 480f);
        AssertEqual(ExploitPhase.Strike, StepTo(ref g, in c), "to Strike");
        c = Calm(T0 + 0.03125, 0f, 400f);
        AssertEqual(ExploitPhase.Strike, StepTo(ref g, in c), "200 m short of an empty aim is not held");
    }

    private static void LifeOrderShape()
    {
        ExploitEntry e = LaunchOf(OpeningKind.UnsupportedBattery, false, 10f, 600f, target: 4);
        AssertEqual(CommandTaskType.AttackObjective, ExploitLifecycle.TaskFor(in e), "Advance");
        AssertEqual("exploit-advance", ExploitLifecycle.ReasonFor(in e), "Advance reason");
        e.Phase = ExploitPhase.Strike;
        AssertEqual(CommandTaskType.AttackObjective, ExploitLifecycle.TaskFor(in e), "Strike");
        AssertEqual("exploit-strike", ExploitLifecycle.ReasonFor(in e), "Strike reason");
        e.Phase = ExploitPhase.Consolidate;
        AssertEqual(CommandTaskType.HoldObjective, ExploitLifecycle.TaskFor(in e), "Consolidate holds");
        AssertEqual("exploit-consolidate", ExploitLifecycle.ReasonFor(in e), "Consolidate reason");
        e.Phase = ExploitPhase.Pursue;
        AssertEqual(CommandTaskType.AttackObjective, ExploitLifecycle.TaskFor(in e), "Pursue");
        AssertEqual("exploit-pursue", ExploitLifecycle.ReasonFor(in e), "Pursue reason");

        ExploitEntry p = LaunchOf(OpeningKind.Penetration, false, 100f, -150f, target: 9);
        AssertEqual(CommandTaskType.Counterstroke, ExploitLifecycle.TaskFor(in p), "a Penetration exploit carries the Counterstroke task in Advance");
        p.Phase = ExploitPhase.Strike;
        AssertEqual(CommandTaskType.Counterstroke, ExploitLifecycle.TaskFor(in p), "and in Strike");
        p.Phase = ExploitPhase.Consolidate;
        AssertEqual(CommandTaskType.HoldObjective, ExploitLifecycle.TaskFor(in p), "Consolidate holds like any other");
    }

    // ------------------------------------------------------------------ aim and stand-off

    private static void LifeCavStandOff()
    {
        ExploitEntry e = LaunchOf(OpeningKind.UnsupportedBattery, true, 0f, 1000f, target: 4);
        ExploitLifecycle.OverrideTarget(in e, out float x, out float z);
        AssertNear(0f, x, 0.01f, "straight ahead: lateral");
        AssertNear(940f, z, 0.01f, "60 m short of the aim");

        ExploitEntry d = LaunchOf(OpeningKind.UnsupportedBattery, true, 600f, 800f, target: 4);
        ExploitLifecycle.OverrideTarget(in d, out x, out z);
        AssertNear(564f, x, 0.01f, "diagonal approach (0.6, 0.8): 600 - 36");
        AssertNear(752f, z, 0.01f, "800 - 48");

        ExploitEntry inf = LaunchOf(OpeningKind.UnsupportedBattery, false, 600f, 800f, target: 4);
        ExploitLifecycle.OverrideTarget(in inf, out x, out z);
        AssertNear(600f, x, 0.01f, "infantry goes to the aim itself");
        AssertNear(800f, z, 0.01f, "infantry z");

        // The stand-off still holds in Strike, and a cavalry asset 70 m from the aim (inside its 90 m strike distance) is in Strike.
        ExploitStepInput s = Tracking(T0 + 0.015625, 0f, 930f, 0f, 1000f);
        ExploitLifecycle.Step(ref e, in s);
        AssertEqual(ExploitPhase.Strike, e.Phase, "cavalry strikes within 90 m of the aim");
        ExploitLifecycle.OverrideTarget(in e, out x, out z);
        AssertNear(940f, z, 0.01f, "Strike keeps the stand-off");

        ExploitEntry c = LaunchOf(OpeningKind.UnsupportedBattery, true, 0f, 1000f, target: 4);
        s = Tracking(T0 + 0.015625, 0f, 880f, 0f, 1000f);
        AssertEqual(ExploitPhase.Advance, StepTo(ref c, in s), "120 m short is outside the cavalry strike distance (90 m)");
        s = Tracking(T0 + 0.03125, 0f, 912f, 0f, 1000f);
        AssertEqual(ExploitPhase.Strike, StepTo(ref c, in s), "88 m is inside it");

        // The approach bearing is fixed at launch; the heading of a non-Gap kind is the bearing asset to aim.
        AssertNear(0f, e.Heading, 0.01f, "straight ahead is bearing 0");
        AssertNear(36.87f, d.Heading, 0.05f, "(600,800) is 36.87 degrees");
    }

    private static void LifeAim()
    {
        ExploitEntry e = LaunchOf(OpeningKind.UnsupportedBattery, false, 0f, 600f, target: 9, assetZ: 100f);
        ExploitStepInput s = Calm(T0 + 0.015625, 0f, 200f);
        s.HasTarget = true; s.TargetPerceived = true; s.TargetX = 40f; s.TargetZ = 640f;
        ExploitLifecycle.Step(ref e, in s);
        AssertNear(40f, e.AimX, 0.001f, "the aim follows a perceived target: x");
        AssertNear(640f, e.AimZ, 0.001f, "z");

        s = Calm(T0 + 0.03125, 0f, 250f);
        s.HasTarget = true; s.TargetPerceived = true; s.TargetX = 60f; s.TargetZ = 700f;
        ExploitLifecycle.Step(ref e, in s);
        AssertNear(60f, e.AimX, 0.001f, "and keeps following");

        // A target no longer perceived leaves the aim where it was (the asset is within 150 m: Consolidate, not an abort).
        ExploitEntry n = LaunchOf(OpeningKind.UnsupportedBattery, false, 0f, 600f, target: 9);
        s = Calm(T0 + 0.015625, 0f, 500f);
        s.HasTarget = true; s.TargetPerceived = false; s.TargetX = 300f; s.TargetZ = 900f;
        ExploitLifecycle.Step(ref n, in s);
        AssertNear(0f, n.AimX, 0.001f, "an unperceived target does not move the aim");
        AssertNear(600f, n.AimZ, 0.001f, "z");
        AssertEqual(ExploitPhase.Consolidate, n.Phase, "a target lost within 150 m of the aim in Advance goes to Consolidate");
        AssertEqual(ExploitEnd.None, n.End, "not an abort");

        // The consolidating aim does not chase a fleeing target.
        s = Calm(T0 + 0.03125, 0f, 600f);
        s.HasTarget = true; s.TargetPerceived = true; s.TargetRouted = true; s.TargetX = 0f; s.TargetZ = 900f;
        ExploitLifecycle.Step(ref n, in s);
        AssertNear(600f, n.AimZ, 0.001f, "Consolidate freezes the aim");

        // A Gap's aim is frozen at launch, whatever the input carries.
        ExploitEntry g = LaunchOf(OpeningKind.Gap, false, 0f, 600f);
        s = Calm(T0 + 0.015625, 0f, 100f);
        s.HasTarget = true; s.TargetPerceived = true; s.TargetX = 200f; s.TargetZ = 900f;
        ExploitLifecycle.Step(ref g, in s);
        AssertNear(0f, g.AimX, 0.001f, "Gap aim frozen");
        AssertNear(600f, g.AimZ, 0.001f, "Gap aim frozen z");
        AssertEqual(-1, g.TargetObservation, "a Gap carries no target");

        ExploitEntry f = LaunchOf(OpeningKind.FlankInAir, false, 500f, 400f, heading: 270f);
        s = Calm(T0 + 0.015625, 0f, 100f);
        s.HasTarget = true; s.TargetPerceived = true; s.TargetX = 700f; s.TargetZ = 400f;
        ExploitLifecycle.Step(ref f, in s);
        AssertNear(500f, f.AimX, 0.001f, "FlankInAir aim frozen");
        AssertNear(270f, f.Heading, 0.001f, "and a Gap or FlankInAir keeps the opening's own heading");
        AssertEqual(-1, f.TargetObservation, "a FlankInAir carries no target");

        // The cavalry stand-off follows the followed aim along the launch approach bearing.
        ExploitEntry c = LaunchOf(OpeningKind.UnsupportedBattery, true, 0f, 1000f, target: 4);
        s = Calm(T0 + 0.015625, 0f, 100f);
        s.HasTarget = true; s.TargetPerceived = true; s.TargetX = 100f; s.TargetZ = 1000f;
        ExploitLifecycle.Step(ref c, in s);
        ExploitLifecycle.OverrideTarget(in c, out float ox, out float oz);
        AssertNear(100f, ox, 0.01f, "the cavalry stand-off moves with the aim: x");
        AssertNear(940f, oz, 0.01f, "60 m short along the launch bearing: z");
    }

    private static void LifePursue()
    {
        ExploitEntry e = LaunchOf(OpeningKind.ShakenSector, true, 0f, 800f, target: 4);
        ExploitStepInput s = Tracking(T0 + 0.015625, 0f, 740f, 0f, 800f);
        s.Audacity01 = 0.6f;
        AssertEqual(ExploitPhase.Strike, StepTo(ref e, in s), "to Strike at the stand-off");

        s = Calm(T0 + 0.03125, 0f, 745f);
        s.Audacity01 = 0.6f;
        s.HasTarget = true; s.TargetPerceived = true; s.TargetRouted = true; s.TargetX = 0f; s.TargetZ = 800f;
        AssertEqual(ExploitPhase.Pursue, StepTo(ref e, in s), "a routed target and audacity 0.6 pursue");

        s = Calm(T0 + 0.046875, 0f, 800f);
        s.Audacity01 = 0.6f;
        s.HasTarget = true; s.TargetPerceived = true; s.TargetRouted = true; s.TargetX = 20f; s.TargetZ = 1000f;
        AssertEqual(ExploitPhase.Pursue, StepTo(ref e, in s), "200 m from the aim: still Pursue");
        AssertNear(800f, e.AimZ, 0.001f, "in Pursue the aim stays where the strike was");
        ExploitLifecycle.OverrideTarget(in e, out float x, out float z);
        AssertNear(20f, x, 0.01f, "the pursuit target is the routed sample: x");
        AssertNear(1000f, z, 0.01f, "z");

        s = Calm(T0 + 0.0625, 0f, 900f);
        s.Audacity01 = 0.6f;
        s.HasTarget = true; s.TargetPerceived = true; s.TargetRouted = true; s.TargetX = 0f; s.TargetZ = 1250f;
        AssertEqual(ExploitPhase.Consolidate, StepTo(ref e, in s), "450 m from the aim ends the pursuit");

        // Audacity just below the bar, infantry, and a target not routed never pursue.
        ExploitEntry low = LaunchOf(OpeningKind.ShakenSector, true, 0f, 800f, target: 4);
        s = Tracking(T0 + 0.015625, 0f, 745f, 0f, 800f);
        s.Audacity01 = 0.59f;
        StepTo(ref low, in s);
        s.NowHours = T0 + 0.03125;
        s.HasTarget = true; s.TargetPerceived = true; s.TargetRouted = true; s.TargetX = 0f; s.TargetZ = 800f;
        AssertEqual(ExploitPhase.Consolidate, StepTo(ref low, in s), "audacity 0.59 does not pursue");

        ExploitEntry inf = LaunchOf(OpeningKind.ShakenSector, false, 0f, 800f, target: 4);
        s = Tracking(T0 + 0.015625, 0f, 700f, 0f, 800f);
        s.Audacity01 = 1f;
        StepTo(ref inf, in s);
        s.NowHours = T0 + 0.03125;
        s.HasTarget = true; s.TargetPerceived = true; s.TargetRouted = true; s.TargetX = 0f; s.TargetZ = 800f;
        AssertEqual(ExploitPhase.Consolidate, StepTo(ref inf, in s), "infantry never pursues");

        // Pursue ends in Consolidate when the target is no longer routed, or no longer perceived.
        foreach (int how in new[] { 0, 1 })
        {
            ExploitEntry p = LaunchOf(OpeningKind.ShakenSector, true, 0f, 800f, target: 4);
            s = Tracking(T0 + 0.015625, 0f, 745f, 0f, 800f);
            s.Audacity01 = 0.9f;
            StepTo(ref p, in s);
            s.NowHours = T0 + 0.03125;
            s.HasTarget = true; s.TargetPerceived = true; s.TargetRouted = true; s.TargetX = 0f; s.TargetZ = 800f;
            AssertEqual(ExploitPhase.Pursue, StepTo(ref p, in s), "pursuing");
            s.NowHours = T0 + 0.046875;
            s.TargetRouted = how != 0;
            s.TargetPerceived = how != 1;
            AssertEqual(ExploitPhase.Consolidate, StepTo(ref p, in s), how == 0 ? "no longer routed" : "no longer perceived");
        }
    }

    // ------------------------------------------------------------------ aborts

    private delegate void Mutate(ref ExploitStepInput input);

    private static ExploitEntry AdvanceEntry() => LaunchOf(OpeningKind.Gap, false, 0f, 600f);

    private static ExploitEnd AbortOf(ExploitEntry e, double now, Mutate mutate, ExploitPhase expectPhase = ExploitPhase.Recall)
    {
        ExploitStepInput s = Calm(now, 0f, 100f);
        mutate(ref s);
        ExploitLifecycle.Step(ref e, in s);
        AssertEqual(expectPhase, e.Phase, "phase after the step");
        return e.End;
    }

    private static void LifeAborts()
    {
        double now = T0 + 0.015625;
        AssertEqual(ExploitEnd.StandDown, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => i.StandDown = true), "stand-down");
        AssertEqual(ExploitEnd.WithdrawPhase, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => i.PlanWithdraw = true), "withdraw-phase");
        AssertEqual(ExploitEnd.Safety, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => i.SafetyClaim = true), "safety");
        AssertEqual(ExploitEnd.FlankThreat, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => i.FlankEmergency = true), "flank-threat");
        AssertEqual(ExploitEnd.LineCrisis, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => i.LineIntegrityFails = true), "line-crisis (line integrity)");
        AssertEqual(ExploitEnd.LineCrisis, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => i.ParentCrisisRelease = true), "line-crisis (parent line crisis release)");
        AssertEqual(ExploitEnd.AssetLosses, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => i.AssetAlive = false), "asset-losses (the asset is gone)");
        AssertEqual(ExploitEnd.AssetLosses, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => i.AssetLoss = 0.05f + 0.16f), "asset-losses (loss up past 0.10 + 0.10 x tolerance)");
        AssertEqual(ExploitEnd.AssetLosses, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => i.AssetMorale = 0.44f), "asset-losses (morale under ShakenMorale)");
        AssertEqual(ExploitEnd.OddsCollapsed, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => i.LocalOdds = 0.59f), "odds-collapsed");
        AssertEqual(ExploitEnd.OpeningLost, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => i.OpeningLive = false), "opening-lost (the opening is gone)");
        AssertEqual(ExploitEnd.OpeningLost, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => i.AdvanceGaveUp = true), "opening-lost (the executor gave up the Advance Move)");
        AssertEqual(ExploitEnd.Timeout, AbortOf(AdvanceEntry(), T0 + 0.5, (ref ExploitStepInput i) => { }), "timeout at exactly MaxExploitHours");

        // The target lost more than 150 m from the aim.
        ExploitEntry battery = LaunchOf(OpeningKind.UnsupportedBattery, false, 0f, 600f, target: 4);
        AssertEqual(ExploitEnd.OpeningLost, AbortOf(battery, now, (ref ExploitStepInput i) =>
        {
            i.AssetZ = 400f; i.HasTarget = true; i.TargetPerceived = false;
        }), "opening-lost (target lost 200 m from the aim)");

        // Every abort leaves the entry in Recall for good, and the end is first-write: a later step cannot change it.
        ExploitEntry e = AdvanceEntry();
        ExploitStepInput c = Calm(now, 0f, 100f);
        c.SafetyClaim = true;
        ExploitLifecycle.Step(ref e, in c);
        c.StandDown = true;
        ExploitLifecycle.Step(ref e, in c);
        AssertEqual(ExploitEnd.Safety, e.End, "an ended entry keeps its first end");
    }

    private static void LifeAbortBoundaries()
    {
        double now = T0 + 0.015625;

        // Losses: the threshold is 0.10 + 0.10 x casualtyTolerance01 above the launch loss (0.05).
        AssertEqual(ExploitEnd.None, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => i.AssetLoss = 0.05f + 0.14f, ExploitPhase.Advance), "0.14 up at tolerance 0.5 holds");
        AssertEqual(ExploitEnd.AssetLosses, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => { i.CasualtyTolerance01 = 0f; i.AssetLoss = 0.05f + 0.12f; }), "0.12 up at tolerance 0 aborts");
        AssertEqual(ExploitEnd.None, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => { i.CasualtyTolerance01 = 1f; i.AssetLoss = 0.05f + 0.18f; }, ExploitPhase.Advance), "0.18 up at tolerance 1 holds (0.20)");
        AssertEqual(ExploitEnd.None, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => i.AssetMorale = 0.45f, ExploitPhase.Advance), "morale at ShakenMorale is not under it");

        // Odds: strictly under 0.6 x required.
        ExploitEntry high = LaunchOf(OpeningKind.Gap, false, 0f, 600f, required: 2f);
        AssertEqual(ExploitEnd.OddsCollapsed, AbortOf(high, now, (ref ExploitStepInput i) => i.LocalOdds = 1.19f), "1.19 < 0.6 x 2");
        AssertEqual(ExploitEnd.None, AbortOf(LaunchOf(OpeningKind.Gap, false, 0f, 600f, required: 2f), now, (ref ExploitStepInput i) => i.LocalOdds = 1.21f, ExploitPhase.Advance), "1.21 holds");
        AssertEqual(ExploitEnd.None, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => i.LocalOdds = float.PositiveInfinity, ExploitPhase.Advance), "no enemy near the aim is infinite odds");
        AssertEqual(ExploitEnd.None, AbortOf(AdvanceEntry(), now, (ref ExploitStepInput i) => i.LocalOdds = float.NaN, ExploitPhase.Advance), "an unknown odds is no collapse");

        // Timeout one tick short.
        AssertEqual(ExploitEnd.None, AbortOf(AdvanceEntry(), T0 + 0.484375, (ref ExploitStepInput i) => { }, ExploitPhase.Advance), "0.484 h holds");

        // A target lost within 150 m of the aim in Advance consolidates (LIFE-AIM); lost beyond it aborts at exactly 150.1.
        ExploitEntry b = LaunchOf(OpeningKind.UnsupportedBattery, false, 0f, 600f, target: 4);
        ExploitStepInput s = Calm(now, 0f, 450f);
        s.HasTarget = true; s.TargetPerceived = false;
        ExploitLifecycle.Step(ref b, in s);
        AssertEqual(ExploitPhase.Consolidate, b.Phase, "exactly 150 m is within");
        ExploitEntry b2 = LaunchOf(OpeningKind.UnsupportedBattery, false, 0f, 600f, target: 4);
        s = Calm(now, 0f, 449f);
        s.HasTarget = true; s.TargetPerceived = false;
        ExploitLifecycle.Step(ref b2, in s);
        AssertEqual(ExploitEnd.OpeningLost, b2.End, "151 m is beyond");
    }

    private static readonly string[] PrecedenceNames =
    {
        "stand-down", "withdraw-phase", "safety", "flank-threat", "line-crisis", "asset-losses", "odds-collapsed", "opening-lost", "timeout",
    };

    private static void LifePrecedence()
    {
        ExploitEnd[] order =
        {
            ExploitEnd.StandDown, ExploitEnd.WithdrawPhase, ExploitEnd.Safety, ExploitEnd.FlankThreat, ExploitEnd.LineCrisis,
            ExploitEnd.AssetLosses, ExploitEnd.OddsCollapsed, ExploitEnd.OpeningLost, ExploitEnd.Timeout,
        };
        bool[] on = new bool[order.Length];
        for (int i = 0; i < on.Length; i++) on[i] = true;
        for (int winner = 0; winner < order.Length; winner++)
        {
            ExploitEntry e = AdvanceEntry();
            ExploitStepInput s = Calm(T0 + (on[8] ? 0.5 : 0.015625), 0f, 100f);
            s.StandDown = on[0]; s.PlanWithdraw = on[1]; s.SafetyClaim = on[2]; s.FlankEmergency = on[3]; s.LineIntegrityFails = on[4];
            if (on[5]) s.AssetAlive = false;
            if (on[6]) s.LocalOdds = 0.1f;
            if (on[7]) s.OpeningLive = false;
            ExploitLifecycle.Step(ref e, in s);
            AssertEqual(order[winner], e.End, "with " + PrecedenceNames[winner] + " and everything below it set, " + PrecedenceNames[winner] + " wins");
            on[winner] = false;
        }
    }

    private static void LifePhaseScopedAborts()
    {
        // Strike entry reached through contact.
        ExploitEntry Strike()
        {
            ExploitEntry e = LaunchOf(OpeningKind.Gap, false, 0f, 600f);
            ExploitStepInput s = Calm(T0 + 0.015625, 0f, 470f);
            ExploitLifecycle.Step(ref e, in s);
            AssertEqual(ExploitPhase.Strike, e.Phase, "setup: Strike");
            return e;
        }

        ExploitEntry strike = Strike();
        ExploitStepInput n = Calm(T0 + 0.03125, 0f, 480f);
        n.OpeningLive = false; n.AdvanceGaveUp = true; n.NearestEnemyToAim = 50f;
        ExploitLifecycle.Step(ref strike, in n);
        AssertEqual(ExploitEnd.None, strike.End, "opening-lost never fires after Strike");
        AssertEqual(ExploitPhase.Strike, strike.Phase, "the entry runs on its own state");

        strike = Strike();
        n = Calm(T0 + 0.03125, 0f, 480f);
        n.LocalOdds = 0.1f; n.NearestEnemyToAim = 50f;
        ExploitLifecycle.Step(ref strike, in n);
        AssertEqual(ExploitEnd.OddsCollapsed, strike.End, "odds-collapsed fires in Strike");

        // Consolidate: no odds abort, no opening-lost, no timeout.
        ExploitEntry e = LaunchOf(OpeningKind.Gap, false, 0f, 600f);
        ExploitLifecycle.Step(ref e, Calm(T0 + 0.375, 0f, 470f));
        ExploitLifecycle.Step(ref e, Calm(T0 + 0.40625, 0f, 590f));
        AssertEqual(ExploitPhase.Consolidate, e.Phase, "setup: Consolidate at 16.40625");
        ExploitStepInput c = Calm(T0 + 0.5, 0f, 600f);
        c.LocalOdds = 0.01f; c.OpeningLive = false; c.AdvanceGaveUp = true;
        ExploitLifecycle.Step(ref e, in c);
        AssertEqual(ExploitEnd.None, e.End, "no odds, opening or timeout abort in Consolidate (timeout hour reached)");
        AssertEqual(ExploitPhase.Consolidate, e.Phase, "still consolidating");
        ExploitLifecycle.Step(ref e, Calm(T0 + 0.53125, 0f, 600f));
        AssertEqual(ExploitEnd.Success, e.End, "and then Success");

        // The abort that does apply in Consolidate.
        ExploitEntry f = LaunchOf(OpeningKind.Gap, false, 0f, 600f);
        ExploitLifecycle.Step(ref f, Calm(T0 + 0.015625, 0f, 470f));
        ExploitLifecycle.Step(ref f, Calm(T0 + 0.03125, 0f, 590f));
        ExploitStepInput sf = Calm(T0 + 0.046875, 0f, 600f);
        sf.SafetyClaim = true;
        ExploitLifecycle.Step(ref f, in sf);
        AssertEqual(ExploitEnd.Safety, f.End, "a Safety claim aborts a consolidating entry");

        // Timeout applies in Pursue.
        ExploitEntry p = LaunchOf(OpeningKind.ShakenSector, true, 0f, 800f, target: 4);
        ExploitStepInput ps = Tracking(T0 + 0.015625, 0f, 745f, 0f, 800f);
        ps.Audacity01 = 0.9f;
        ExploitLifecycle.Step(ref p, in ps);
        ps = Calm(T0 + 0.03125, 0f, 745f);
        ps.Audacity01 = 0.9f; ps.HasTarget = true; ps.TargetPerceived = true; ps.TargetRouted = true; ps.TargetX = 0f; ps.TargetZ = 800f;
        ExploitLifecycle.Step(ref p, in ps);
        AssertEqual(ExploitPhase.Pursue, p.Phase, "setup: Pursue");
        ps.NowHours = T0 + 0.5; ps.TargetZ = 900f;
        ExploitLifecycle.Step(ref p, in ps);
        AssertEqual(ExploitEnd.Timeout, p.End, "timeout fires in Pursue");
    }

    private static void LifeClock()
    {
        // The doctrine clock is frozen overnight: the same hour repeated is no elapsed time.
        double night = 20.0;
        ExploitEntry e = LaunchOf(OpeningKind.Gap, false, 0f, 600f, now: night);
        for (int i = 0; i < 500; i++)
            ExploitLifecycle.Step(ref e, Calm(night, 0f, 100f));
        AssertEqual(ExploitPhase.Advance, e.Phase, "500 steps at one frozen hour: still Advance");
        AssertEqual(ExploitEnd.None, e.End, "no timeout on a frozen clock");

        ExploitLifecycle.Step(ref e, Calm(night, 0f, 470f));
        ExploitLifecycle.Step(ref e, Calm(night, 0f, 590f));
        AssertEqual(ExploitPhase.Consolidate, e.Phase, "setup: Consolidate");
        for (int i = 0; i < 500; i++)
            ExploitLifecycle.Step(ref e, Calm(night, 0f, 600f));
        AssertEqual(ExploitPhase.Consolidate, e.Phase, "Consolidate does not elapse on a frozen clock");

        // Every elapsed comparison is >=: exactly ConsolidateHours (since 0.0 is binary-exact for the constant) and exactly MaxExploitHours.
        ExploitEntry z = LaunchOf(OpeningKind.Gap, false, 0f, 600f, now: 0.0);
        ExploitLifecycle.Step(ref z, Calm(0.0, 0f, 470f));
        ExploitLifecycle.Step(ref z, Calm(0.0, 0f, 590f));
        AssertEqual(ExploitPhase.Consolidate, z.Phase, "setup: Consolidate at hour 0");
        ExploitLifecycle.Step(ref z, Calm(ExploitLifecycle.ConsolidateHours, 0f, 600f));
        AssertEqual(ExploitEnd.Success, z.End, "exactly ConsolidateHours elapsed is Success");

        ExploitEntry t = LaunchOf(OpeningKind.Gap, false, 0f, 600f, now: 0.0);
        ExploitLifecycle.Step(ref t, Calm(ExploitLifecycle.MaxExploitHours, 0f, 100f));
        AssertEqual(ExploitEnd.Timeout, t.End, "exactly MaxExploitHours elapsed is Timeout");
        AssertEqual(0.5, ExploitLifecycle.MaxExploitHours, "MaxExploitHours");
        AssertEqual(0.1, ExploitLifecycle.ConsolidateHours, "ConsolidateHours is a double, not 0.1f widened");
    }

    // ------------------------------------------------------------------ the Defender counterstroke

    // The Defender frame: the own trace runs along z = 0, the enemy has broken in and stands behind it at negative z.
    private static ExploitStepInput PenInput(double now, float assetX, float assetZ, float targetX, float targetZ, float behindTrace)
    {
        ExploitStepInput s = Calm(now, assetX, assetZ);
        s.HasTarget = true; s.TargetPerceived = true; s.TargetX = targetX; s.TargetZ = targetZ;
        s.HasTrace = true; s.TargetBehindTraceMeters = behindTrace; s.TraceX = targetX; s.TraceZ = 0f;
        s.NearestEnemyToAim = 0f;
        return s;
    }

    private static void LifeCounterAim()
    {
        ExploitEntry e = LaunchOf(OpeningKind.Penetration, false, 100f, -150f, target: 9, assetX: 100f, assetZ: -500f);
        ExploitStepInput s = PenInput(T0 + 0.015625, 100f, -240f, 100f, -150f, 150f);
        AssertEqual(ExploitPhase.Strike, StepTo(ref e, in s), "within 150 m: Strike");

        s = PenInput(T0 + 0.03125, 100f, -200f, 100f, -120f, 120f);
        AssertEqual(ExploitPhase.Strike, StepTo(ref e, in s), "the target is behind the trace: Strike holds");
        AssertNear(-120f, e.AimZ, 0.001f, "and the aim follows it while it is behind the trace");

        // The target falls back across the own trace (unrouted): the aim freezes at the trace point.
        s = PenInput(T0 + 0.046875, 100f, -150f, 100f, 20f, -20f);
        AssertEqual(ExploitPhase.Consolidate, StepTo(ref e, in s), "the target reached the trace: Consolidate there");
        AssertNear(100f, e.AimX, 0.001f, "the aim is the trace point: x");
        AssertNear(0f, e.AimZ, 0.001f, "z, never in front of the line");

        // The enemy now stands well in front of the own line. The aim never follows.
        s = PenInput(T0 + 0.0625, 100f, -20f, 100f, 300f, -300f);
        ExploitLifecycle.Step(ref e, in s);
        AssertNear(0f, e.AimZ, 0.001f, "a consolidating counterstroke does not follow an enemy in front of the line");
        AssertEqual(CommandTaskType.HoldObjective, ExploitLifecycle.TaskFor(in e), "it holds at the trace");

        // Reaching the trace during Advance consolidates there too, and the trace point is reached by holding.
        ExploitEntry a = LaunchOf(OpeningKind.Penetration, false, 100f, -150f, target: 9, assetX: 100f, assetZ: -500f);
        s = PenInput(T0 + 0.015625, 100f, -450f, 100f, 10f, -10f);
        AssertEqual(ExploitPhase.Consolidate, StepTo(ref a, in s), "in Advance as well");
        AssertNear(0f, a.AimZ, 0.001f, "frozen at the trace");
    }

    private static void LifeCounterBounds()
    {
        // No Pursue: even a (hypothetical) cavalry Penetration exploit with high audacity and a routed target consolidates.
        ExploitEntry cav = LaunchOf(OpeningKind.Penetration, true, 100f, -150f, target: 9, assetX: 100f, assetZ: -240f);
        ExploitStepInput s = PenInput(T0 + 0.015625, 100f, -240f, 100f, -150f, 150f);
        s.Audacity01 = 1f;
        ExploitLifecycle.Step(ref cav, in s);
        AssertEqual(ExploitPhase.Strike, cav.Phase, "setup: Strike");
        s = PenInput(T0 + 0.03125, 100f, -200f, 100f, -150f, 150f);
        s.Audacity01 = 1f; s.TargetRouted = true;
        AssertEqual(ExploitPhase.Consolidate, StepTo(ref cav, in s), "a routed Penetration target is not pursued");

        // The aim stops following beyond the 400 m bound; the asset arrives at an empty aim and consolidates.
        ExploitEntry b = LaunchOf(OpeningKind.Penetration, false, 100f, -380f, target: 9, assetX: 100f, assetZ: -500f);
        s = PenInput(T0 + 0.015625, 100f, -450f, 100f, -450f, 450f);
        ExploitLifecycle.Step(ref b, in s);
        AssertNear(-380f, b.AimZ, 0.001f, "a target 450 m behind the trace is past CounterstrokeMaxMeters: the aim stays");
        s = PenInput(T0 + 0.03125, 100f, -420f, 100f, -399f, 399f);
        ExploitLifecycle.Step(ref b, in s);
        AssertNear(-399f, b.AimZ, 0.001f, "399 m behind is within the bound: the aim follows");

        // Recall always follows: Success after Consolidate, and each abort.
        ExploitEntry done = LaunchOf(OpeningKind.Penetration, false, 100f, -150f, target: 9, assetX: 100f, assetZ: -240f);
        ExploitLifecycle.Step(ref done, PenInput(T0 + 0.015625, 100f, -240f, 100f, -150f, 150f));
        ExploitLifecycle.Step(ref done, PenInput(T0 + 0.03125, 100f, -150f, 100f, 20f, -20f));
        AssertEqual(ExploitPhase.Consolidate, done.Phase, "setup: Consolidate");
        ExploitLifecycle.Step(ref done, PenInput(T0 + 0.1875, 100f, -10f, 100f, 100f, -100f));
        AssertEqual(ExploitPhase.Recall, done.Phase, "Consolidate ends in Recall");
        AssertEqual(ExploitEnd.Success, done.End, "with Success");

        ExploitEntry lost = LaunchOf(OpeningKind.Penetration, false, 100f, -150f, target: 9, assetX: 100f, assetZ: -500f);
        s = PenInput(T0 + 0.015625, 100f, -450f, 100f, -150f, 150f);
        s.OpeningLive = false;
        ExploitLifecycle.Step(ref lost, in s);
        AssertEqual(ExploitPhase.Recall, lost.Phase, "an opening lost before Strike (the penetration was repelled by others) recalls");
        AssertEqual(ExploitEnd.OpeningLost, lost.End, "as opening-lost");
    }

    // ------------------------------------------------------------------ the table

    private static void LifeTable()
    {
        BattleScope.ResetAll();
        ExploitTable t = ExploitTable.ForAlliance(0);
        AssertTrue(!ReferenceEquals(t, ExploitTable.ForAlliance(1)), "one table per alliance");
        AssertTrue(ReferenceEquals(t, ExploitTable.ForAlliance(0)), "and it is the same table each time");
        ExploitTable inert = ExploitTable.ForAlliance(2);
        ExploitEntry stray = LaunchOf(OpeningKind.Gap, false, 0f, 600f);
        AssertFalse(inert.Add(in stray), "an out-of-range alliance has an inert table");
        AssertEqual(0, inert.Count, "that stores nothing");

        ExploitEntry e = LaunchOf(OpeningKind.UnsupportedBattery, false, 10f, 600f, target: 4);
        AssertTrue(t.Add(in e), "add");
        t.SetSubtree(e.Id, new[] { 101, 102 }, new[] { 50, 10 });
        AssertEqual(1, t.Count, "one live entry");

        AssertTrue(t.TryOverride(100, out CommandDoctrineOrder order, out CommandNodeOperationalState state), "the asset is overridden");
        AssertEqual("node-100", order.NodeId, "node id");
        AssertEqual("node-100", state.NodeId, "state node id");
        AssertEqual(CommandTaskType.AttackObjective, order.Task, "task");
        AssertEqual(CommandTaskType.AttackObjective, state.Task, "state task");
        AssertEqual("exploit-advance", order.Reason, "reason literal");
        AssertTrue(order.PrimaryTarget.HasValue, "a target");
        AssertNear(10f, order.PrimaryTarget.X, 0.001f, "aim x");
        AssertNear(600f, order.PrimaryTarget.Z, 0.001f, "aim z");
        AssertNear(e.Heading, order.ManeuverHeading, 0.01f, "the heading rides the order");
        AssertEqual(e.Id, order.ExploitId, "the order names its exploit");
        AssertEqual(-1, CommandDoctrineOrder.Create("node-1", CommandNodeRole.Reserve, CommandTaskType.ReserveWait, "o", DoctrineTargetPoint.None,
            DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineAllowedIdleReason.None, 0f, 0f, 1f, "r").ExploitId, "an ordinary order has no exploit id (-1)");

        AssertTrue(t.TryOverride(101, out order, out state), "a descendant is overridden");
        AssertEqual("exploit-follow", order.Reason, "with the follow literal");
        AssertTrue(ExploitTable.IsFollow(in order), "IsFollow reads it");
        AssertFalse(order.PrimaryTarget.HasValue, "a follow order has no target to plan a route to");
        AssertEqual(e.Id, order.ExploitId, "and the same exploit");
        AssertTrue(t.TryOverride(102, out _, out _), "both descendants");
        AssertFalse(t.TryOverride(50, out _, out _), "an ancestor is not overridden");
        AssertFalse(t.TryOverride(999, out _, out _), "nor a stranger");

        AssertTrue(t.IsExploiting(100), "the asset");
        AssertTrue(t.IsExploiting(101), "a descendant");
        AssertTrue(t.IsExploiting(50), "an ancestor");
        AssertTrue(t.IsExploiting(10), "all ancestors");
        AssertFalse(t.IsExploiting(999), "not a stranger");

        // The cavalry order targets the stand-off.
        ExploitTable other = ExploitTable.ForAlliance(1);
        var asset = new ExploitAsset { Id = 200, X = 0f, Z = 0f, Cavalry = true };
        var opening = new Opening { Id = 6, Kind = OpeningKind.UnsupportedBattery, AimX = 0f, AimZ = 1000f, TargetObservation = 8 };
        ExploitEntry c = ExploitLifecycle.Launch(2, 8, 60, in asset, in opening, 0.5f, T0);
        AssertTrue(other.Add(in c), "the other side's entry");
        AssertTrue(other.TryOverride(200, out order, out _), "cavalry asset");
        AssertNear(940f, order.PrimaryTarget.Z, 0.01f, "the cavalry order targets the stand-off");
        AssertFalse(t.TryOverride(200, out _, out _), "the tables are independent");

        // A Penetration asset carries the Counterstroke task and an attack SOP; its Consolidate holds.
        ExploitTable defender = t;
        var pa = new ExploitAsset { Id = 300, X = 100f, Z = -500f };
        var po = new Opening { Id = 7, Kind = OpeningKind.Penetration, AimX = 100f, AimZ = -150f, TargetObservation = 9 };
        ExploitEntry p = ExploitLifecycle.Launch(3, 9, 70, in pa, in po, 0.8f, T0);
        AssertTrue(defender.Add(in p), "penetration entry");
        AssertTrue(defender.TryOverride(300, out order, out state), "override");
        AssertEqual(CommandTaskType.Counterstroke, order.Task, "the exploit order carries the Counterstroke task");
        AssertEqual(CommandTaskType.Counterstroke, state.Task, "state too");
        AssertEqual(TacticalSopAuthority.Attack, order.Sop.Authority, "with the attack SOP, not the stay default");

        // Table state follows the entries: a step through the table's own access, then Clear.
        t.Clear(ExploitEnd.StandDown);
        AssertEqual(0, t.Count, "Clear empties the table");
        AssertFalse(t.TryOverride(100, out _, out _), "no override after a stand-down");
        AssertFalse(t.IsExploiting(101), "nor a subtree");

        // BattleScope.ResetAll clears every table.
        AssertEqual(1, other.Count, "the other side still holds its entry");
        BattleScope.ResetAll();
        AssertEqual(0, other.Count, "ResetAll clears it");

        // Capacity: at most MaxEntries live; further adds are refused (the producer caps launches at 3 per side long before).
        int added = 0;
        for (int i = 0; i < ExploitTable.MaxEntries + 4; i++)
        {
            ExploitEntry x = LaunchOf(OpeningKind.Gap, false, 0f, 600f);
            x.Id = 10 + i; x.AssetId = 400 + i;
            if (t.Add(in x)) added++;
        }
        AssertEqual(ExploitTable.MaxEntries, added, "the table is bounded");
        BattleScope.ResetAll();
    }

    private static void LifeClaimRelease()
    {
        BattleScope.ResetAll();
        ExploitTable t = ExploitTable.ForAlliance(0);
        var hes = new HesitationStore();

        // A real opening from the detector: the claim is derived onto the detector's own openings.
        var d = new OpeningDetector();
        Settle(d, GapLine());
        Opening gap = First(d, OpeningKind.Gap);
        AssertFalse(gap.Claimed, "unclaimed before any exploit");

        var asset = new ExploitAsset { Id = 100, OwnIndex = 3, X = 0f, Z = 0f, LossFraction = 0.05f };
        ExploitEntry e = ExploitLifecycle.Launch(1, 7, 50, in asset, in gap, 1f, T0);
        t.Add(in e);
        t.DeriveClaims(d);
        AssertTrue(First(d, OpeningKind.Gap).Claimed, "the claim is held while the entry lives");

        // A stale claim is cleared: the claim is derived, never stored.
        d.SetClaimed(gap.Id);
        t.Clear(ExploitEnd.StandDown);
        t.DeriveClaims(d);
        AssertFalse(First(d, OpeningKind.Gap).Claimed, "with no entry the flag is cleared");
        t.Add(in e);
        t.DeriveClaims(d);

        // Past Strike the entry keeps running on its own state when the detector drops the opening, and the claim stays with it.
        ref ExploitEntry live = ref t.At(0);
        float aimX = live.AimX, aimZ = live.AimZ;
        ExploitLifecycle.Step(ref live, Calm(T0 + 0.015625, aimX, aimZ - 100f));
        AssertEqual(ExploitPhase.Strike, live.Phase, "setup: Strike");
        ExploitStepInput lostOpening = Calm(T0 + 0.03125, aimX, aimZ - 100f);
        lostOpening.OpeningLive = false; lostOpening.NearestEnemyToAim = 40f;
        ExploitLifecycle.Step(ref live, in lostOpening);
        AssertEqual(ExploitPhase.Strike, live.Phase, "still running");

        // Success: Consolidate completes, Recall; the entry still claims until it is collected.
        ExploitLifecycle.Step(ref live, Calm(T0 + 0.046875, aimX, aimZ - 5f));
        AssertEqual(ExploitPhase.Consolidate, live.Phase, "setup: Consolidate");
        ExploitLifecycle.Step(ref live, Calm(T0 + 0.25, aimX, aimZ));
        AssertEqual(ExploitEnd.Success, live.End, "Success");
        t.DeriveClaims(d);
        AssertTrue(First(d, OpeningKind.Gap).Claimed, "an ended entry still in the table still claims until it is collected");
        var ended = new ExploitEntry[4];
        int n = t.CollectEnded(ended, T0 + 0.25);
        AssertEqual(1, n, "one ended entry collected");
        AssertEqual(ExploitEnd.Success, ended[0].End, "its end rides out");
        AssertEqual(0, t.Count, "and it left the table");
        t.DeriveClaims(d);
        AssertFalse(First(d, OpeningKind.Gap).Claimed, "the claim is released at Recall");
        ExploitLifecycle.MarkHesitation(hes, in ended[0], T0 + 0.25);
        AssertFalse(hes.IsHesitating(7, gap.Id, T0 + 0.25), "a Success marks nobody hesitant");

        // The asset's cooldown stays in the table's per-asset store.
        AssertTrue(t.InCooldown(100, T0 + 0.25), "the asset is cooling down");
        AssertTrue(t.InCooldown(100, T0 + 0.25 + 0.2), "0.2 h later still");
        AssertFalse(t.InCooldown(100, T0 + 0.25 + 0.25), "the cooldown is AssetCooldownHours");
        AssertFalse(t.InCooldown(101, T0 + 0.25), "another asset is not");

        // An abort: Recall, the claim goes, the aborting commander is hesitant for that opening, another commander is not.
        ExploitEntry a = ExploitLifecycle.Launch(2, 7, 50, in asset, in gap, 1f, T0 + 1.0);
        t.Add(in a);
        t.DeriveClaims(d);
        AssertTrue(First(d, OpeningKind.Gap).Claimed, "claimed again");
        ExploitStepInput safety = Calm(T0 + 1.015625, 0f, 100f);
        safety.SafetyClaim = true;
        ExploitLifecycle.Step(ref t.At(0), in safety);
        AssertEqual(ExploitEnd.Safety, t.At(0).End, "aborted");
        n = t.CollectEnded(ended, T0 + 1.015625);
        AssertEqual(1, n, "collected");
        t.DeriveClaims(d);
        AssertFalse(First(d, OpeningKind.Gap).Claimed, "the abort released the claim");
        ExploitLifecycle.MarkHesitation(hes, in ended[0], T0 + 1.015625);
        AssertTrue(hes.IsHesitating(7, gap.Id, T0 + 1.015625 + 0.05), "the aborting commander is hesitant for that opening");
        AssertFalse(hes.IsHesitating(8, gap.Id, T0 + 1.015625 + 0.05), "another commander may take it on his next decision");
        AssertFalse(hes.IsHesitating(7, gap.Id, T0 + 1.015625 + 0.2), "and the mark runs out after HesitationHours");
        BattleScope.ResetAll();
    }

    private static void LifeFeedback()
    {
        BattleScope.ResetAll();
        AssertFalse(ExploitFeedback.TakeFlankEmergency(100), "nothing set");
        ExploitFeedback.SetFlankEmergency(100);
        ExploitFeedback.SetFlankEmergency(100);
        AssertTrue(ExploitFeedback.TakeFlankEmergency(100), "set, taken");
        AssertFalse(ExploitFeedback.TakeFlankEmergency(100), "taken once: the next opportunity tick reads it once");
        ExploitFeedback.SetFlankEmergency(101);
        AssertFalse(ExploitFeedback.TakeFlankEmergency(102), "per asset");
        for (int i = 0; i < 100; i++) ExploitFeedback.SetFlankEmergency(1000 + i);   // bounded: never throws
        BattleScope.ResetAll();
        AssertFalse(ExploitFeedback.TakeFlankEmergency(101), "ResetAll clears it");
    }
}
