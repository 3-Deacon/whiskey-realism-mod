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
