using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WhiskeyRealism;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Util;

using static TestFixtures;
using static TestHarness;

internal static class StrategicOperationsTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("daily cadence fires on first call and day rollover only", DailyCadenceFiresOnFirstCallAndDayRolloverOnly),
            ("daily cadence rejects invalid dates", DailyCadenceRejectsInvalidDates),
            ("strategic cadence alternates formation by alliance", StrategicCadenceAlternatesFormationByAlliance),
            ("strategic cadence refreshes weekly or on source change", StrategicCadenceRefreshesWeeklyOrSourceChange),
            ("strategic cadence stable source can skip downstream rebuild", StrategicCadenceStableSourceSkipsDownstreamRebuild),
            ("strategic battle intent snapshot sanitizes nonfinite pressure", StrategicBattleIntentSnapshotSanitizesNonfinitePressure),
            ("operational startup gate fires once when runtime becomes ready same day", OperationalStartupGateFiresOnceWhenRuntimeBecomesReadySameDay),
            ("formation directive summary changes when assignment changes", FormationDirectiveSummaryChangesWhenAssignmentChanges),
            ("formation directive carries stable id and position", FormationDirectiveCarriesStableIdAndPosition),
            ("formation directive summary changes on stable position", FormationDirectiveSummaryChangesOnStablePosition),
            ("formation directive flags per reason", FormationDirectiveFlagsPerReason),
            ("attached division inherits the parent's defensive flag and donates nothing", AttachedDivisionInheritsDefensiveFlagAndDonatesNothing),
        });
    }

    private static void StrategicCadenceAlternatesFormationByAlliance()
    {
        AssertTrue(StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day: 1, alliance: 1),
            "CSA should run on odd days");
        AssertTrue(!StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day: 1, alliance: 0),
            "Union should skip odd days without a force refresh");
        AssertTrue(StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day: 2, alliance: 0),
            "Union should run on even days");
        AssertTrue(!StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day: 2, alliance: 1),
            "CSA should skip even days without a force refresh");
        AssertTrue(StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day: 2, alliance: 1, forceRefresh: true),
            "force refresh should override alternating cadence");
    }

    private static void StrategicCadenceRefreshesWeeklyOrSourceChange()
    {
        AssertTrue(StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                day: 1,
                currentSourceSignature: "sig-a",
                previousSourceSignature: null,
                forceRefresh: false),
            "first source observation should run");
        AssertTrue(!StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                day: 3,
                currentSourceSignature: "sig-a",
                previousSourceSignature: "sig-a",
                forceRefresh: false),
            "stable source before weekly boundary should skip");
        AssertTrue(StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                day: 3,
                currentSourceSignature: "sig-b",
                previousSourceSignature: "sig-a",
                forceRefresh: false),
            "source change should run immediately");
        AssertTrue(StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                day: 7,
                currentSourceSignature: "sig-a",
                previousSourceSignature: "sig-a",
                forceRefresh: false),
            "weekly boundary should run even when source is stable");
        AssertTrue(StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                day: 3,
                currentSourceSignature: "sig-a",
                previousSourceSignature: "sig-a",
                forceRefresh: true),
            "force refresh should run immediately");
    }

    private static void StrategicCadenceStableSourceSkipsDownstreamRebuild()
    {
        AssertTrue(StrategicCadencePolicy.SourceChanged("front:2", "front:1"),
            "different front signatures should count as changed");
        AssertTrue(!StrategicCadencePolicy.SourceChanged("front:1", "front:1"),
            "same front signatures should be stable");
        AssertTrue(StrategicCadencePolicy.SourceChanged("front:1", null),
            "missing previous signature should force first rebuild");
    }

    private static void StrategicBattleIntentSnapshotSanitizesNonfinitePressure()
    {
        var snapshot = new StrategicBattleIntentSnapshot(
            casualtyPressure: float.PositiveInfinity,
            timePressure: float.NaN,
            theaterIntent: null,
            campaignIntent: "  ",
            allianceId: 3,
            campaignObjectiveId: null,
            theaterPriority: float.PositiveInfinity,
            casualtyTolerance: -2f,
            preserveForceBias: float.NaN,
            commanderPersonality: new PersonalityVector(2f, -2f, float.NaN, 0.5f, 0f));

        AssertEqual(0f, snapshot.CasualtyPressure, "casualty pressure");
        AssertEqual(0f, snapshot.TimePressure, "time pressure");
        AssertEqual(string.Empty, snapshot.TheaterIntent, "theater intent");
        AssertEqual(string.Empty, snapshot.CampaignIntent, "campaign intent");
        AssertEqual(-1, snapshot.AllianceId, "invalid alliance");
        AssertEqual(string.Empty, snapshot.CampaignObjectiveId, "campaign objective id");
        AssertEqual(0f, snapshot.TheaterPriority, "theater priority");
        AssertEqual(-1f, snapshot.CasualtyTolerance, "casualty tolerance");
        AssertEqual(0f, snapshot.PreserveForceBias, "preserve force bias");
        AssertEqual(1f, snapshot.CommanderPersonality.Aggression, "commander aggression");
        AssertEqual(-1f, snapshot.CommanderPersonality.Caution, "commander caution");
        AssertEqual(0f, snapshot.CommanderPersonality.Audacity, "commander audacity");
    }

    private static void DailyCadenceFiresOnFirstCallAndDayRolloverOnly()
    {
        var cadence = new DailyCadence();
        AssertTrue(cadence.ShouldFire(1, 6, 1861), "first call should fire");
        AssertTrue(!cadence.ShouldFire(1, 6, 1861), "same day should not fire again");
        AssertTrue(cadence.ShouldFire(2, 6, 1861), "next day should fire");
        AssertTrue(cadence.ShouldFire(1, 7, 1861), "month rollover should fire");
        AssertTrue(cadence.ShouldFire(1, 1, 1862), "year rollover should fire");
    }

    private static void DailyCadenceRejectsInvalidDates()
    {
        var cadence = new DailyCadence();
        AssertTrue(!cadence.ShouldFire(0, 6, 1861), "day 0 should be ignored");
        AssertTrue(!cadence.ShouldFire(1, 0, 1861), "month 0 should be ignored");
        AssertTrue(!cadence.ShouldFire(1, 6, 0), "year 0 should be ignored");
    }

    private static void OperationalStartupGateFiresOnceWhenRuntimeBecomesReadySameDay()
    {
        var gate = new OperationalStartupGate();

        AssertEqual(true, gate.ShouldNotify(dateChanged: true, runtimeReady: false));
        AssertEqual(false, gate.ShouldNotify(dateChanged: false, runtimeReady: false));
        AssertEqual(true, gate.ShouldNotify(dateChanged: false, runtimeReady: true));
        AssertEqual(false, gate.ShouldNotify(dateChanged: false, runtimeReady: true));
        AssertEqual(true, gate.ShouldNotify(dateChanged: true, runtimeReady: true));
    }

    private static void FormationDirectiveSummaryChangesWhenAssignmentChanges()
    {
        var a = Snapshot("unit", 0, 15, 15000f, 10000f, FormationLevel.Corps, FrontPosture.Hold);
        var b = Snapshot("unit", 0, 15, 15000f, 10000f, FormationLevel.Corps, FrontPosture.Counterstroke);

        string first = FormationDirectiveLedger.Build(new[] { a }, EraStage.Operational1862, null).Summary();
        string second = FormationDirectiveLedger.Build(new[] { b }, EraStage.Operational1862, null).Summary();

        AssertEqual(false, string.Equals(first, second, StringComparison.Ordinal));
    }

    private static void FormationDirectiveCarriesStableIdAndPosition()
    {
        var snap = Snapshot("position-corps", 1, 15, 9000f, 5000f, FormationLevel.Corps, FrontPosture.Counterstroke);
        snap.StableUnitId = 4242;
        snap.X = 123.5f;
        snap.Z = -456.25f;

        var ledger = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Operational1862, null);
        var assignment = ledger.GetAssignment("position-corps");

        AssertEqual(4242, assignment.StableUnitId);
        AssertNear(123.5f, assignment.X, 0.0001f, "assignment X");
        AssertNear(-456.25f, assignment.Z, 0.0001f, "assignment Z");
    }

    private static void FormationDirectiveSummaryChangesOnStablePosition()
    {
        var a = Snapshot("position-corps", 1, 15, 9000f, 5000f, FormationLevel.Corps, FrontPosture.Counterstroke);
        var b = Snapshot("position-corps", 1, 15, 9000f, 5000f, FormationLevel.Corps, FrontPosture.Counterstroke);
        a.StableUnitId = 1;
        b.StableUnitId = 1;
        a.X = 10f;
        a.Z = 10f;
        b.X = 40f;
        b.Z = 10f;

        string first = FormationDirectiveLedger.Build(new[] { a }, EraStage.Operational1862, null).Summary();
        string second = FormationDirectiveLedger.Build(new[] { b }, EraStage.Operational1862, null).Summary();

        AssertTrue(first != second, "summary must change when stable position changes");
    }

    // Every reason the ledger can give a top formation, with the directive and the two flags it sets
    // (DefensiveAllowed: DefenseIntentRuntime may use the formation as a defender; TransferDonorAllowed:
    // StrategicMovementBudget may take men from it). The order of the rows is the order of the rules, so a
    // swapped, forgotten or defaulted flag turns exactly one row red.
    private static void FormationDirectiveFlagsPerReason()
    {
        var rows = new (string reason, FormationDirective directive, bool defensive, bool donor, Func<FormationSnapshot> snap, bool recon)[]
        {
            ("low-morale-readiness", FormationDirective.Recover, false, false, () => { var x = Snapshot("u", 0, 15, 10000f, 0f, FormationLevel.Unknown, FrontPosture.Hold); x.Morale = 0.2f; return x; }, false),
            ("low-ammo-supply", FormationDirective.Recover, false, false, () => { var x = Snapshot("u", 0, 15, 10000f, 0f, FormationLevel.Unknown, FrontPosture.Hold); x.RifleAmmo = 0.1f; return x; }, false),
            ("retreat-skirmish-risk", FormationDirective.Delay, true, false, () => { var x = Snapshot("u", 0, 15, 10000f, 0f, FormationLevel.Unknown, FrontPosture.Hold); x.OnRetreat = true; x.HasActivePath = true; return x; }, false),
            ("recon-mission", FormationDirective.Screen, true, false, () => Snapshot("u", 0, 15, 10000f, 0f, FormationLevel.Unknown, FrontPosture.Hold), true),
            ("division-vs-army-no-support", FormationDirective.Delay, true, false, () => Snapshot("u", 0, 14, 4500f, 50000f, FormationLevel.Army, FrontPosture.Hold), false),
            ("division-vs-corps-delay", FormationDirective.Delay, true, false, () => Snapshot("u", 0, 14, 4500f, 9000f, FormationLevel.Corps, FrontPosture.Hold), false),
            ("front-concede", FormationDirective.Concede, false, true, () => Snapshot("u", 0, 15, 10000f, 0f, FormationLevel.Unknown, FrontPosture.Concede), false),
            ("economy-of-force", FormationDirective.Reserve, true, true, () => Snapshot("u", 0, 15, 10000f, 0f, FormationLevel.Unknown, FrontPosture.EconomyOfForce), false),
            ("army-plan-target", FormationDirective.Mass, true, false, () => { var x = Snapshot("u", 0, 16, 40000f, 0f, FormationLevel.Unknown, FrontPosture.Hold); x.IsPlanTargetArea = true; x.GrandArmyStructureAvailable = true; return x; }, false),
            ("risk-gate-passed", FormationDirective.Counterstroke, true, false, () => Snapshot("u", 0, 15, 10000f, 5000f, FormationLevel.Corps, FrontPosture.Counterstroke), false),
            ("risk-gate-passed", FormationDirective.Mass, true, false, () => Snapshot("u", 0, 15, 10000f, 5000f, FormationLevel.Corps, FrontPosture.Exploit), false),
            ("critical-sector", FormationDirective.Guard, true, false, () => { var x = Snapshot("u", 0, 15, 10000f, 0f, FormationLevel.Unknown, FrontPosture.Hold); x.IsCriticalSector = true; return x; }, false),
            ("army-hold", FormationDirective.Hold, true, false, () => Snapshot("u", 0, 16, 40000f, 0f, FormationLevel.Unknown, FrontPosture.Hold), false),
            (FormationDirectiveLedger.DefaultHoldReason, FormationDirective.Hold, true, false, () => Snapshot("u", 0, 15, 10000f, 0f, FormationLevel.Unknown, FrontPosture.Hold), false),
        };
        foreach (var row in rows)
        {
            var ledger = FormationDirectiveLedger.Build(new[] { row.snap() }, EraStage.Operational1862, null, null, row.recon ? (Func<string, bool>)(_ => true) : null);
            var a = ledger.GetAssignment("u");
            string label = row.reason + "/" + row.directive;
            AssertEqual(row.reason, a.Reason, label + ": reason");
            AssertEqual(row.directive, a.Directive, label + ": directive");
            AssertEqual(row.defensive, a.DefensiveAllowed, label + ": defensive flag");
            AssertEqual(row.donor, a.TransferDonorAllowed, label + ": donor flag");
            AssertTrue(a.DirectMovementAllowed, label + ": a top formation takes a direct movement");
        }
    }

    // An attached division repeats its parent's directive and DefensiveAllowed, is never a transfer donor and takes no
    // direct movement; with no parent in the ledger it defends and says so.
    private static void AttachedDivisionInheritsDefensiveFlagAndDonatesNothing()
    {
        FormationSnapshot Child(string parentKey)
        {
            var c = Snapshot("child", 0, 14, 4000f, 0f, FormationLevel.Unknown, FrontPosture.Hold);
            c.IsTopUnit = false;
            c.ParentUnitKey = parentKey;
            return c;
        }

        var concede = Snapshot("parent", 0, 15, 10000f, 0f, FormationLevel.Unknown, FrontPosture.Concede);
        var under = FormationDirectiveLedger.Build(new[] { concede, Child("parent") }, EraStage.Operational1862, null).GetAssignment("child");
        AssertEqual(FormationDirective.Concede, under.Directive, "the child repeats the parent's directive");
        AssertEqual("attached-inherits-parent", under.Reason, "and says why");
        AssertFalse(under.DefensiveAllowed, "a conceding parent's child does not defend");
        AssertFalse(under.TransferDonorAllowed, "the parent donates (front-concede), the child never does");
        AssertFalse(under.DirectMovementAllowed, "an attached division takes no direct movement");

        var hold = Snapshot("parent", 0, 15, 10000f, 0f, FormationLevel.Unknown, FrontPosture.Hold);
        var underHold = FormationDirectiveLedger.Build(new[] { hold, Child("parent") }, EraStage.Operational1862, null).GetAssignment("child");
        AssertTrue(underHold.DefensiveAllowed, "a holding parent's child defends");
        AssertEqual(FormationDirectiveLedger.DefaultHoldReason, underHold.Reason, "and keeps the default-hold reason, so its perks fall through to the objective role");

        var orphan = FormationDirectiveLedger.Build(new[] { Child("gone") }, EraStage.Operational1862, null).GetAssignment("child");
        AssertEqual("attached-parent-missing", orphan.Reason, "no parent in the ledger");
        AssertEqual(FormationDirective.Hold, orphan.Directive, "holds");
        AssertTrue(orphan.DefensiveAllowed, "and defends");
        AssertFalse(orphan.TransferDonorAllowed, "and donates nothing");
    }

    private static FormationSnapshot ProbeSnapshot(
        string key,
        int alliance,
        int unitType,
        float strength,
        float enemy,
        FormationLevel enemyLevel,
        FrontPosture posture,
        string sectorKey)
    {
        var snapshot = Snapshot(key, alliance, unitType, strength, enemy, enemyLevel, posture);
        snapshot.SectorKey = sectorKey;
        return snapshot;
    }
}
