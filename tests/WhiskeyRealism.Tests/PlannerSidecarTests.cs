using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestFixtures;
using static TestHarness;

// Stage G slice G6, sidecar v3 (plan § G6 "Persistence"): the campaign planner's state across a save. A v1/v2 file decodes
// with no planner state (its saved operational plans counted as dropped); a v3 file round-trips every planner field the
// as-built sections owe the sidecar; every formation is its unit key, never a Unity instance id, and is rebound to this
// session's formation on the first review after a load; the order flow's campaign couriers, the stage E policy fields and
// the W&L compliance estimate keep round-tripping through the same document.
internal static class PlannerSidecarTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("V1FileLoadsWithNoPlannerState", V1FileLoadsWithNoPlannerState),
            ("V2FileLoadsWithNoPlannerState", V2FileLoadsWithNoPlannerState),
            ("MigrationCountsPlansPerFaction", MigrationCountsPlansPerFaction),
            ("CommanderIdZeroAppointmentRoundTrips", CommanderIdZeroAppointmentRoundTrips),
            ("V3FileDropsNoPlans", V3FileDropsNoPlans),
            ("V3RoundTripsThePlannerState", V3RoundTripsThePlannerState),
            ("V3RoundTripsBeliefsAndAppointments", V3RoundTripsBeliefsAndAppointments),
            ("MoveObjectiveIdRoundTrips", MoveObjectiveIdRoundTrips),
            ("TempoReleasedDecisionAndLastFullReviewDayRoundTrip", TempoReleasedDecisionAndLastFullReviewDayRoundTrip),
            ("PlannerFieldsAbsentFromAV3FileDecodeToTheirDefaults", PlannerFieldsAbsentFromAV3FileDecodeToTheirDefaults),
            ("DepotTasksKeepEndedDayAndWishPosition", DepotTasksKeepEndedDayAndWishPosition),
            ("ReconMissionsKeepStandDownDueAndClearedDay", ReconMissionsKeepStandDownDueAndClearedDay),
            ("V3ReloadReboundsBeliefsByStableKey", V3ReloadReboundsBeliefsByStableKey),
            ("RecordWithoutAUnitKeyIsNotPersistedOrRestored", RecordWithoutAUnitKeyIsNotPersistedOrRestored),
            ("UnknownEnumNamesDecodeToTheDefault", UnknownEnumNamesDecodeToTheDefault),
            ("PlannerIsPerAlliance", PlannerIsPerAlliance),
            ("ModWrittenObjectiveNotesBoundsAndRestores", ModWrittenObjectiveNotesBoundsAndRestores),
            ("OnlyPlannerBudgetMovesArePersisted", OnlyPlannerBudgetMovesArePersisted),
            ("RebindResolvesByUnitKey", RebindResolvesByUnitKey),
            ("RebindFallsBackToTheUniqueName", RebindFallsBackToTheUniqueName),
            ("RebindDropsAMoveWhoseFormationIsGone", RebindDropsAMoveWhoseFormationIsGone),
            ("RebindKeepsADepotTaskWhoseBuilderIsGone", RebindKeepsADepotTaskWhoseBuilderIsGone),
            ("RebindDropsAMissionWithItsStandDownDebt", RebindDropsAMissionWithItsStandDownDebt),
            ("FirstReviewAfterALoadPinsTheDepotBuilder", FirstReviewAfterALoadPinsTheDepotBuilder),
            ("RebindClearsWhatItFills", RebindClearsWhatItFills),
            ("RebindOfNothingIsEmpty", RebindOfNothingIsEmpty),
            ("IndexOfKeyOrNameRefusesAnAmbiguousName", IndexOfKeyOrNameRefusesAnAmbiguousName),
            ("NoDtoCarriesInstanceId", NoDtoCarriesInstanceId),
            ("EncodedFileNamesNoInstanceId", EncodedFileNamesNoInstanceId),
            ("FutureVersionRejected", FutureVersionRejected),
            ("VersionZeroRejected", VersionZeroRejected),
            ("CouriersRoundTrip", CouriersRoundTrip),
            ("StageEPolicyFieldsRoundTrip", StageEPolicyFieldsRoundTrip),
            ("PlayerComplianceRoundTrips", PlayerComplianceRoundTrips),
            ("NonCompliancePlayerRunRestoresWithItsLastDay", NonCompliancePlayerRunRestoresWithItsLastDay),
            ("CoordinatorSavesAndLoadsThePlanner", CoordinatorSavesAndLoadsThePlanner),
            ("RuntimeRebindsOnTheFirstReviewWithFormations", RuntimeRebindsOnTheFirstReviewWithFormations),
            ("RuntimeExportsThePendingRecordsWhenNoReviewRanSinceTheLoad", RuntimeExportsThePendingRecordsWhenNoReviewRanSinceTheLoad),
            ("MigrationLineIsLoggedOnce", MigrationLineIsLoggedOnce),
        });
    }

    // ---- fixtures ----

    private static SidecarSnapshot WithPlanner(PlannerSnapshot union, PlannerSnapshot csa = null)
    {
        var snap = CampaignLifecycleFixTests.SampleSnapshot();
        snap.Planner[0] = union;
        snap.Planner[1] = csa;
        return snap;
    }

    private static SidecarSnapshot RoundTrip(SidecarSnapshot snap)
    {
        string json = SidecarDtoCodec.Serialize(SidecarDtoCodec.Encode(snap));
        AssertTrue(SidecarDtoCodec.TryDeserialize(json, out var dto, out var reason), "deserialize: " + reason);
        AssertTrue(SidecarDtoCodec.TryDecode(dto, out var back, out reason), "decode: " + reason);
        return back;
    }

    private static PlannerSnapshot Decoded(PlannerSnapshot planner)
    {
        return RoundTrip(WithPlanner(planner)).Planner[0];
    }

    // Every planner field set to a value that is neither its default nor another field's.
    private static PlannerSnapshot SamplePlanner()
    {
        var p = new PlannerSnapshot { UnreliableDays = 4, UnreliableLastDay = 812 };
        p.State.Restore(mainSector: 3, mainObjective: 29, challengerObjective: 30, challengerDays: 2, decisionSerial: 7,
            campaignSeed: 0x1234567890ABCDEFL, belowOddsDays: 5, capitalThreatLatched: true, tempoReleasedDecision: 6, lastFullReviewDay: 811);
        p.Beliefs.Add(new EnemyBelief
        {
            InstanceId = -1, UnitKey = "Army of Northern Virginia:12", UnitTyp = 16, X = 120.5f, Z = -40.25f, Strength = 18000f, Guns = 40f,
            LastSeenDay = 805, Confidence01 = 0.75f, UncertaintyRadius = 33f, LastSource = DetectionSource.ObjectiveGarrison,
            Traits = EnemyTraits.CavalryMajority | EnemyTraits.OnOwnSoil, LastEmbarkedDay = 790
        });
        p.Appointments.Add(new KeyValuePair<int, int>(17, 840));
        p.DepotTasks.Add(new DepotTaskRecord
        {
            X = 300.5f, Z = 71.25f, SectorId = 2, BuilderInstanceId = -1, BuilderUnitKey = "Pioneers:41", State = DepotTaskState.Wished,
            StartedDay = 800, EndedDay = 809, WishX = 298f, WishZ = 70f
        });
        p.Moves.Add(new MoveRecord { InstanceId = -1, UnitKey = "1st Corps:7", TargetX = 410f, TargetZ = -20f, IssuedDay = 808, Retries = 1, ObjectiveId = 29 });
        p.ReconMissions.Add(new ReconMission
        {
            MissionId = 9, Kind = ReconKind.Probe, InstanceId = -1, UnitKey = "Cavalry Corps:3", HoldX = 150f, HoldZ = 12f, SectorId = 4, State = ReconState.Cleared,
            IssuedDay = 790, LastContactDay = 801, Reason = "probe-main", ObservedEnemyStrength = 22000f, ObservedObjectiveId = 29, StandDownDue = true, ClearedDay = 806
        });
        return p;
    }

    private static string V3Json(string plannerJson)
    {
        return "{\"version\":3,\"factions\":[{\"factionId\":0,\"currentEra\":\"Operational1862\",\"planner\":" + plannerJson + "}]}";
    }

    private static SidecarSnapshot Decode(string json)
    {
        AssertTrue(SidecarDtoCodec.TryDeserialize(json, out var dto, out var reason), "deserialize: " + reason);
        AssertTrue(SidecarDtoCodec.TryDecode(dto, out var snap, out reason), "decode: " + reason);
        return snap;
    }

    // ---- V1 / V2 load, V3 round trips ----

    private static void V1FileLoadsWithNoPlannerState()
    {
        const string json = "{\"version\":1,\"factions\":["
            + "{\"factionId\":0,\"currentEra\":\"Decisive1863\",\"cic\":{\"officerName\":\"Lincoln\",\"activePlan\":{\"operationId\":\"union-east-pressure\",\"currentPhaseIndex\":1}}},"
            + "{\"factionId\":1,\"currentEra\":\"Operational1862\",\"cic\":{\"officerName\":\"Davis\"}}]}";
        var snap = Decode(json);
        AssertEqual(1, snap.SourceVersion, "the file's version");
        AssertEqual(1, snap.PlansDropped, "the one saved operational plan is counted as dropped");
        AssertTrue(snap.Planner[0] == null && snap.Planner[1] == null, "no planner state: the planner starts fresh");
        AssertEqual(EraStage.Decisive1863, snap.Eras[0].Value, "the era still loads");
        AssertEqual(EraStage.Operational1862, snap.Eras[1].Value, "for both alliances");
    }

    private static void V2FileLoadsWithNoPlannerState()
    {
        const string json = "{\"version\":2,\"saveSequence\":9,\"lastSeenMonth\":5,\"lastSeenYear\":1862,\"factions\":["
            + "{\"factionId\":0,\"currentEra\":\"Operational1862\",\"cic\":{\"officerName\":\"Lincoln\",\"activePlan\":{\"operationId\":\"a\"}},"
            + "\"probeState\":{\"probeId\":\"p-1\"},\"recentReplans\":[1,2],\"fiscalMemory\":{\"previousPosture\":\"CreditDefense\",\"stableTicksAboveEmergency\":4,\"emergencyResidue\":true},\"modWrittenObjective\":29},"
            + "{\"factionId\":1,\"currentEra\":\"Operational1862\",\"cic\":{\"officerName\":\"Davis\",\"activePlan\":{\"operationId\":\"b\"}}}],"
            + "\"minorOfficerProfiles\":[{\"commanderId\":5}],\"succession\":{\"firedEvents\":[1],\"appliedEvents\":[1]}}";
        var snap = Decode(json);
        AssertEqual(2, snap.SourceVersion, "the file's version");
        AssertEqual(2, snap.PlansDropped, "both saved plans are counted");
        AssertTrue(snap.Planner[0] == null && snap.Planner[1] == null, "no planner state");
        AssertEqual(29, snap.ModWrittenObjective[0], "the v2 mod-written objective survives");
        AssertEqual(4, snap.FiscalMemories[0].StableTicksAboveEmergency, "so do the fiscal memory");
        AssertTrue(snap.FiredEvents.Contains(1), "and the succession record");
        AssertEqual(5, snap.LastSeenMonth, "and the last seen month");
        AssertEqual(9L, snap.SaveSequence, "and the save sequence");
    }

    // A faction whose "cic" is null (or missing, or has a null activePlan) holds no plan and must not zero the count of
    // the factions after it.
    private static void MigrationCountsPlansPerFaction()
    {
        const string json = "{\"version\":2,\"factions\":["
            + "{\"factionId\":0,\"currentEra\":\"Operational1862\",\"cic\":null},"
            + "{\"factionId\":1,\"currentEra\":\"Operational1862\",\"cic\":{\"officerName\":\"Davis\",\"activePlan\":{\"operationId\":\"b\"}}}]}";
        AssertEqual(1, Decode(json).PlansDropped, "the second faction's plan is counted past a null cic");

        const string mixed = "{\"version\":1,\"factions\":["
            + "{\"factionId\":0,\"currentEra\":\"Operational1862\"},"
            + "{\"factionId\":1,\"currentEra\":\"Operational1862\",\"cic\":{\"activePlan\":null}},"
            + "{\"factionId\":2,\"currentEra\":\"Operational1862\",\"cic\":{\"activePlan\":{\"operationId\":\"c\"}}}]}";
        AssertEqual(1, Decode(mixed).PlansDropped, "a missing cic and a JSON-null activePlan are not plans");
    }

    // PlannerFromDto refuses only a negative commander id; commander 0 is a real commander.
    private static void CommanderIdZeroAppointmentRoundTrips()
    {
        var p = new PlannerSnapshot();
        p.Appointments.Add(new KeyValuePair<int, int>(0, 812));
        var back = Decoded(p);
        AssertEqual(1, back.Appointments.Count, "the commander-0 protection survives");
        AssertEqual(0, back.Appointments[0].Key, "commander id 0");
        AssertEqual(812, back.Appointments[0].Value, "protected until");
    }

    private static void V3FileDropsNoPlans()
    {
        var snap = RoundTrip(WithPlanner(SamplePlanner()));
        AssertEqual(SidecarDto.CurrentVersion, snap.SourceVersion, "a v3 file");
        AssertEqual(3, SidecarDto.CurrentVersion, "the current version is 3");
        AssertEqual(0, snap.PlansDropped, "nothing dropped");
    }

    private static void V3RoundTripsThePlannerState()
    {
        var back = Decoded(SamplePlanner());
        AssertTrue(back != null, "the planner state is present");
        var s = back.State;
        AssertEqual(3, s.CurrentMainSector, "current main sector");
        AssertEqual(29, s.CurrentMainObjective, "current main objective");
        AssertEqual(30, s.ChallengerObjective, "challenger objective");
        AssertEqual(2, s.ChallengerDays, "challenger days");
        AssertEqual(7, s.DecisionSerial, "decision serial");
        AssertEqual(0x1234567890ABCDEFL, s.CampaignSeed, "the 64-bit campaign seed");
        AssertEqual(5, s.BelowOddsDays, "below-odds days");
        AssertTrue(s.CapitalThreatLatched, "capital threat latch");
        AssertEqual(4, back.UnreliableDays, "the map-group player's unreliable days");
        AssertEqual(812, back.UnreliableLastDay, "and the last day counted");
    }

    private static void V3RoundTripsBeliefsAndAppointments()
    {
        var back = Decoded(SamplePlanner());
        AssertEqual(1, back.Beliefs.Count, "one belief");
        var b = back.Beliefs[0];
        AssertEqual("Army of Northern Virginia:12", b.UnitKey, "belief unit key");
        AssertEqual(-1, b.InstanceId, "no instance id is restored");
        AssertEqual(16, b.UnitTyp, "unit type");
        AssertNear(120.5f, b.X, 1e-4f, "x");
        AssertNear(-40.25f, b.Z, 1e-4f, "z");
        AssertNear(18000f, b.Strength, 1e-2f, "strength");
        AssertNear(40f, b.Guns, 1e-4f, "guns");
        AssertEqual(805, b.LastSeenDay, "last seen day");
        AssertNear(0.75f, b.Confidence01, 1e-4f, "confidence");
        AssertNear(33f, b.UncertaintyRadius, 1e-4f, "uncertainty radius");
        AssertEqual(DetectionSource.ObjectiveGarrison, b.LastSource, "source");
        AssertEqual(EnemyTraits.CavalryMajority | EnemyTraits.OnOwnSoil, b.Traits, "traits");
        AssertEqual(790, b.LastEmbarkedDay, "last embarked day");
        AssertEqual(1, back.Appointments.Count, "one appointment protection");
        AssertEqual(17, back.Appointments[0].Key, "commander id");
        AssertEqual(840, back.Appointments[0].Value, "protected until");
    }

    private static void MoveObjectiveIdRoundTrips()
    {
        var back = Decoded(SamplePlanner());
        AssertEqual(1, back.Moves.Count, "one move");
        var m = back.Moves[0];
        AssertEqual("1st Corps:7", m.UnitKey, "unit key");
        AssertEqual(-1, m.InstanceId, "no instance id");
        AssertNear(410f, m.TargetX, 1e-4f, "target x");
        AssertNear(-20f, m.TargetZ, 1e-4f, "target z");
        AssertEqual(808, m.IssuedDay, "issued day");
        AssertEqual(1, m.Retries, "retries");
        AssertEqual(29, m.ObjectiveId, "the objective the move went toward");

        var none = new PlannerSnapshot();
        none.Moves.Add(new MoveRecord { UnitKey = "2nd Corps:8", ObjectiveId = -1 });
        AssertEqual(-1, Decoded(none).Moves[0].ObjectiveId, "a move toward no objective stays -1");
        const string oldMove = "{\"moves\":[{\"unitKey\":\"2nd Corps:8\",\"tx\":1,\"tz\":2,\"issuedDay\":3,\"retries\":0}]}";
        AssertEqual(-1, Decode(V3Json(oldMove)).Planner[0].Moves[0].ObjectiveId, "a move saved without the field decodes to -1, not objective 0");
    }

    private static void TempoReleasedDecisionAndLastFullReviewDayRoundTrip()
    {
        var back = Decoded(SamplePlanner());
        AssertEqual(6, back.State.TempoReleasedDecision, "the decision whose launch the tempo gate released");
        AssertEqual(811, back.State.LastFullReviewDay, "the tempo clock's reference day");

        var fresh = Decoded(new PlannerSnapshot());
        AssertEqual(-1, fresh.State.TempoReleasedDecision, "unset stays -1 (never reads as released)");
        AssertEqual(-1, fresh.State.LastFullReviewDay, "unset stays -1");
        AssertEqual(-1, fresh.State.CurrentMainSector, "an empty state keeps its sentinels");
        AssertEqual(-1, fresh.State.CurrentMainObjective, "objective");
        AssertEqual(-1, fresh.State.ChallengerObjective, "challenger");
    }

    private static void PlannerFieldsAbsentFromAV3FileDecodeToTheirDefaults()
    {
        var back = Decode(V3Json("{\"currentMainObjective\":29}")).Planner[0];
        AssertEqual(29, back.State.CurrentMainObjective, "the field that is there");
        AssertEqual(-1, back.State.CurrentMainSector, "main sector defaults to none");
        AssertEqual(-1, back.State.TempoReleasedDecision, "tempo release defaults to none");
        AssertEqual(-1, back.State.LastFullReviewDay, "the tempo clock defaults to none");
        AssertEqual(int.MinValue, back.UnreliableLastDay, "no unreliable day counted");
        AssertEqual(0, back.Beliefs.Count + back.Appointments.Count + back.DepotTasks.Count + back.Moves.Count + back.ReconMissions.Count, "no records");
    }

    private static void DepotTasksKeepEndedDayAndWishPosition()
    {
        var back = Decoded(SamplePlanner());
        AssertEqual(1, back.DepotTasks.Count, "one depot task");
        var t = back.DepotTasks[0];
        AssertEqual("Pioneers:41", t.BuilderUnitKey, "builder unit key");
        AssertEqual(-1, t.BuilderInstanceId, "no instance id");
        AssertEqual(DepotTaskState.Wished, t.State, "state");
        AssertNear(300.5f, t.X, 1e-4f, "site x");
        AssertNear(71.25f, t.Z, 1e-4f, "site z");
        AssertEqual(2, t.SectorId, "sector");
        AssertEqual(800, t.StartedDay, "started day");
        AssertEqual(809, t.EndedDay, "the day the task ended (the retry cooldown is a difference of days)");
        AssertNear(298f, t.WishX, 1e-4f, "wish x: the depot rises within the site radius of it");
        AssertNear(70f, t.WishZ, 1e-4f, "wish z");

        var active = new PlannerSnapshot();
        active.DepotTasks.Add(new DepotTaskRecord { BuilderUnitKey = "Pioneers:41", State = DepotTaskState.Marching, EndedDay = -1 });
        AssertEqual(-1, Decoded(active).DepotTasks[0].EndedDay, "an active task has no end");
        const string oldTask = "{\"depotTasks\":[{\"x\":1,\"z\":2,\"sectorId\":3,\"builderUnitKey\":\"P:1\",\"state\":\"Marching\",\"startedDay\":4}]}";
        AssertEqual(-1, Decode(V3Json(oldTask)).Planner[0].DepotTasks[0].EndedDay, "a task saved without the field decodes active, not ended on day 0");
    }

    private static void ReconMissionsKeepStandDownDueAndClearedDay()
    {
        var back = Decoded(SamplePlanner());
        AssertEqual(1, back.ReconMissions.Count, "one mission");
        var m = back.ReconMissions[0];
        AssertEqual(9, m.MissionId, "mission id");
        AssertEqual(ReconKind.Probe, m.Kind, "kind");
        AssertEqual("Cavalry Corps:3", m.UnitKey, "unit key");
        AssertEqual(-1, m.InstanceId, "no instance id");
        AssertNear(150f, m.HoldX, 1e-4f, "hold x");
        AssertNear(12f, m.HoldZ, 1e-4f, "hold z");
        AssertEqual(4, m.SectorId, "sector");
        AssertEqual(ReconState.Cleared, m.State, "state");
        AssertEqual(790, m.IssuedDay, "issued day");
        AssertEqual(801, m.LastContactDay, "last contact day");
        AssertEqual("probe-main", m.Reason, "reason");
        AssertNear(22000f, m.ObservedEnemyStrength, 1e-2f, "the probe's reference enemy strength");
        AssertEqual(29, m.ObservedObjectiveId, "the objective it was seen at");
        AssertTrue(m.StandDownDue, "the executor's stand-down debt survives the save");
        AssertEqual(806, m.ClearedDay, "the day the mission was seen Cleared");

        const string oldMission = "{\"reconMissions\":[{\"missionId\":1,\"kind\":\"Screen\",\"unitKey\":\"C:1\",\"state\":\"Holding\"}]}";
        var old = Decode(V3Json(oldMission)).Planner[0].ReconMissions[0];
        AssertFalse(old.StandDownDue, "a mission saved without the debt owes none");
        AssertEqual(-1, old.ClearedDay, "and was not cleared");
        AssertEqual(-1, old.LastContactDay, "no contact");
        AssertNear(-1f, old.ObservedEnemyStrength, 1e-6f, "no look yet");
        AssertEqual(-1, old.ObservedObjectiveId, "no objective seen");
    }

    // A restored belief has no instance id; the next detection under its unit key rebinds it: one belief, not two, and the
    // strength within reach is counted once.
    private static void V3ReloadReboundsBeliefsByStableKey()
    {
        var saved = Decoded(SamplePlanner());
        var model = new EnemyArmyModel();
        model.Restore(saved.Beliefs);
        float before = EnemyBeliefQuery.StrengthWithin(model.Beliefs, 120.5f, -40.25f, 100f);
        AssertEqual(1, model.Beliefs.Count, "the saved belief is restored");
        AssertEqual(-1, model.Beliefs[0].InstanceId, "with no instance id");
        var seen = new List<EnemyDetection>
        {
            new EnemyDetection { InstanceId = 4242, UnitKey = "Army of Northern Virginia:12", UnitTyp = 16, X = 120.5f, Z = -40.25f, Strength = 18000f, Guns = 40f, Source = DetectionSource.FogGrid }
        };
        model.Update(seen, 806, _ => 1f, _ => 0f);
        AssertEqual(1, model.Beliefs.Count, "the next detection under that key is the same army, not a second one");
        AssertEqual(4242, model.Beliefs[0].InstanceId, "rebound to this session's instance id");
        AssertNear(13500f, before, 1e-2f, "before the sighting the saved belief counts at its saved confidence (18000 x 0.75)");
        AssertNear(18000f, EnemyBeliefQuery.StrengthWithin(model.Beliefs, 120.5f, -40.25f, 100f), 1e-2f, "re-seen it counts once, at full confidence: no double count (not 31500)");
    }

    private static void RecordWithoutAUnitKeyIsNotPersistedOrRestored()
    {
        var p = new PlannerSnapshot();
        p.Moves.Add(new MoveRecord { UnitKey = null, ObjectiveId = 3 });
        p.Moves.Add(new MoveRecord { UnitKey = "", ObjectiveId = 4 });
        p.ReconMissions.Add(new ReconMission { UnitKey = null, MissionId = 1 });
        p.Beliefs.Add(new EnemyBelief { UnitKey = null, Strength = 5f });
        var dto = SidecarDtoCodec.PlannerToDto(p);
        AssertEqual(0, dto.Moves.Count + dto.ReconMissions.Count + dto.Beliefs.Count, "a record that names no formation is not written");

        var raw = new PlannerStateDto();
        raw.Moves.Add(new MoveDto { UnitKey = null });
        raw.Moves.Add(null);
        raw.ReconMissions.Add(new ReconMissionDto { UnitKey = "" });
        raw.Beliefs.Add(new EnemyBeliefDto { UnitKey = null });
        raw.Appointments.Add(new AppointmentDto { CommanderId = -1 });
        var back = SidecarDtoCodec.PlannerFromDto(raw);
        AssertEqual(0, back.Moves.Count + back.ReconMissions.Count + back.Beliefs.Count + back.Appointments.Count, "nor is one read back");
    }

    private static void UnknownEnumNamesDecodeToTheDefault()
    {
        const string json = "{\"depotTasks\":[{\"builderUnitKey\":\"P:1\",\"state\":\"Vanished\"}],\"reconMissions\":[{\"unitKey\":\"C:1\",\"kind\":\"Nonesuch\",\"state\":\"Nonesuch\"}]}";
        var back = Decode(V3Json(json)).Planner[0];
        AssertEqual(DepotTaskState.Marching, back.DepotTasks[0].State, "an unknown depot state is the first state");
        AssertEqual(ReconKind.Screen, back.ReconMissions[0].Kind, "an unknown recon kind is Screen");
        AssertEqual(ReconState.Assigned, back.ReconMissions[0].State, "an unknown recon state is Assigned");
    }

    private static void PlannerIsPerAlliance()
    {
        var csa = new PlannerSnapshot();
        csa.State.Restore(1, 31, -1, 0, 3, 99L);
        var back = RoundTrip(WithPlanner(SamplePlanner(), csa));
        AssertEqual(29, back.Planner[0].State.CurrentMainObjective, "alliance 0 keeps its own");
        AssertEqual(31, back.Planner[1].State.CurrentMainObjective, "alliance 1 keeps its own");
        AssertEqual(99L, back.Planner[1].State.CampaignSeed, "and its own seed");
        var oneSided = RoundTrip(WithPlanner(SamplePlanner(), null));
        AssertTrue(oneSided.Planner[1] == null, "an alliance with no planner state stays null (fresh)");
    }

    private static void ModWrittenObjectiveNotesBoundsAndRestores()
    {
        WhiskeyRealism.Util.CampaignScope.ResetAll();
        AssertEqual(-1, ModWrittenObjective.Get(0), "nothing written yet");
        ModWrittenObjective.Note(1, 29);
        AssertEqual(29, ModWrittenObjective.Get(1), "the executor's write is remembered per alliance");
        AssertEqual(-1, ModWrittenObjective.Get(0), "alliance 0 is not alliance 1");
        ModWrittenObjective.Note(2, 5);
        ModWrittenObjective.Note(-1, 5);
        AssertEqual(-1, ModWrittenObjective.Get(2), "Europe (alliance 2) has no slot and is not written");
        AssertEqual(-1, ModWrittenObjective.Get(-1), "nor a negative alliance");
        ModWrittenObjective.Restore(new int?[] { 3, null });
        AssertEqual(3, ModWrittenObjective.Get(0), "a persisted value is restored");
        AssertEqual(-1, ModWrittenObjective.Get(1), "an unknown one restores to none, not to the previous session's value");
        ModWrittenObjective.Restore(null);
        AssertEqual(-1, ModWrittenObjective.Get(0), "a file without the field restores none");
        ModWrittenObjective.Restore(new int?[] { 7 });
        AssertEqual(7, ModWrittenObjective.Get(0), "a short array restores what it has");
        AssertEqual(-1, ModWrittenObjective.Get(1), "and none for the rest");
        WhiskeyRealism.Util.CampaignScope.ResetAll();
        AssertEqual(-1, ModWrittenObjective.Get(0), "a new campaign forgets it");
    }

    private static void OnlyPlannerBudgetMovesArePersisted()
    {
        var tracker = new MoveTracker();
        var planner = ExecutorFixtures.Corps(1, 0f, 9000f);
        var recon = ExecutorFixtures.Corps(2, 0f, 9000f);
        var host = new FakeReconHost();
        AssertTrue(MoveIssue.Issue(host, tracker, in planner, 300f, 0f, 29, 100, false, MoveBudget.Planner, out _), "a planner move");
        AssertTrue(MoveIssue.Issue(host, tracker, in recon, 310f, 0f, -1, 100, false, MoveBudget.Recon, out _), "a recon move");
        var records = tracker.Records.ToList();
        AssertEqual(1, records.Count, "recon tracker records are not persisted: a load restarts the scouts' moves");
        AssertEqual("corps1:1", records[0].UnitKey, "the planner's");
        AssertEqual(29, records[0].ObjectiveId, "with its objective");
    }

    // ---- rebind by unitKey ----

    private static CampaignSnapshot Session(params (int id, string key, string name)[] formations)
    {
        var s = ExecutorFixtures.Wide();
        foreach (var f in formations)
        {
            var r = ExecutorFixtures.Corps(f.id, 100f * f.id, 5000f);
            r.UnitKey = f.key;
            r.Name = f.name;
            s.Own.Add(r);
        }
        return s;
    }

    private static PlannerSnapshot Saved(string moveKey = null, string depotKey = null, string missionKey = null)
    {
        var p = new PlannerSnapshot();
        if (moveKey != null) p.Moves.Add(new MoveRecord { InstanceId = -1, UnitKey = moveKey, TargetX = 1f, TargetZ = 2f, IssuedDay = 5, Retries = 1, ObjectiveId = 29 });
        if (depotKey != null) p.DepotTasks.Add(new DepotTaskRecord { BuilderInstanceId = -1, BuilderUnitKey = depotKey, State = DepotTaskState.Marching, EndedDay = -1 });
        if (missionKey != null) p.ReconMissions.Add(new ReconMission { MissionId = 4, InstanceId = -1, UnitKey = missionKey, State = ReconState.Holding, StandDownDue = true, ClearedDay = 6 });
        return p;
    }

    private static PlannerRebind.Result Rebind(PlannerSnapshot saved, CampaignSnapshot session, out List<MoveRecord> moves, out List<DepotTaskRecord> depots, out List<ReconMission> missions)
    {
        moves = new List<MoveRecord>();
        depots = new List<DepotTaskRecord>();
        missions = new List<ReconMission>();
        return PlannerRebind.Resolve(saved, session, moves, depots, missions);
    }

    private static void RebindResolvesByUnitKey()
    {
        var session = Session((11, "1st Corps:7", "1st Corps"), (12, "2nd Corps:8", "2nd Corps"), (13, "Cavalry:9", "Cavalry"));
        var saved = Saved(moveKey: "2nd Corps:8", depotKey: "1st Corps:7", missionKey: "Cavalry:9");
        var result = Rebind(saved, session, out var moves, out var depots, out var missions);
        AssertEqual(1, moves.Count, "the move is kept");
        AssertEqual(12, moves[0].InstanceId, "bound to this session's instance id of 2nd Corps");
        AssertEqual("2nd Corps:8", moves[0].UnitKey, "under its key");
        AssertEqual(29, moves[0].ObjectiveId, "everything else is the saved record");
        AssertEqual(1, moves[0].Retries, "retries");
        AssertEqual(1, depots.Count, "the depot task is kept");
        AssertEqual("1st Corps:7", depots[0].BuilderUnitKey, "its builder's key");
        AssertEqual(11, depots[0].BuilderInstanceId, "and the builder is bound to this session's instance at once (the allocation pins a held builder before the ledger's Sync)");
        AssertEqual(1, missions.Count, "the mission is kept");
        AssertEqual(13, missions[0].InstanceId, "bound to the cavalry's instance id");
        AssertTrue(missions[0].StandDownDue && missions[0].ClearedDay == 6, "with its stand-down debt");
        AssertEqual(1, result.MovesKept, "moves kept");
        AssertEqual(0, result.MovesDropped, "none dropped");
        AssertEqual(1, result.MissionsKept, "missions kept");
        AssertEqual(1, result.DepotTasksRebound, "depot tasks rebound");
    }

    private static void RebindFallsBackToTheUniqueName()
    {
        // The commander changed since the save (a succession event): the key's name part still names one formation.
        var session = Session((21, "1st Corps:99", "1st Corps"), (22, "2nd Corps:8", "2nd Corps"));
        var saved = Saved(moveKey: "1st Corps:7", depotKey: "2nd Corps:5", missionKey: "1st Corps:7");
        Rebind(saved, session, out var moves, out var depots, out var missions);
        AssertEqual(1, moves.Count, "the move is kept by name");
        AssertEqual(21, moves[0].InstanceId, "bound to the one 1st Corps");
        AssertEqual("1st Corps:99", moves[0].UnitKey, "under the key it has now");
        AssertEqual("2nd Corps:8", depots[0].BuilderUnitKey, "the depot builder is rekeyed too");
        AssertEqual(22, depots[0].BuilderInstanceId, "and bound");
        AssertTrue(session.IndexOfKey(depots[0].BuilderUnitKey) >= 0, "the depot ledger's Sync, which finds a builder by the exact key, finds it");
        AssertEqual("1st Corps:99", missions[0].UnitKey, "and the mission");
    }

    private static void RebindDropsAMoveWhoseFormationIsGone()
    {
        var session = Session((11, "1st Corps:7", "1st Corps"));
        var saved = Saved(moveKey: "Disbanded:3");
        saved.Moves.Add(new MoveRecord { InstanceId = -1, UnitKey = "1st Corps:7", TargetX = 5f });
        var result = Rebind(saved, session, out var moves, out _, out _);
        AssertEqual(1, moves.Count, "only the move of a formation that is still there");
        AssertEqual("1st Corps:7", moves[0].UnitKey, "which one");
        AssertEqual(1, result.MovesDropped, "one dropped");
        AssertEqual(1, result.MovesKept, "one kept");
    }

    private static void RebindKeepsADepotTaskWhoseBuilderIsGone()
    {
        var session = Session((11, "1st Corps:7", "1st Corps"));
        var saved = Saved(depotKey: "Pioneers:41");
        var result = Rebind(saved, session, out _, out var depots, out _);
        AssertEqual(1, depots.Count, "the record stays: the depot ledger's own sync abandons an active one whose builder is gone, so the site cools down");
        AssertEqual("Pioneers:41", depots[0].BuilderUnitKey, "under the saved key");
        AssertEqual(-1, depots[0].BuilderInstanceId, "unbound: the ledger's Sync abandons it");
        AssertEqual(0, result.DepotTasksRebound, "it was not rebound");
    }

    private static void RebindDropsAMissionWithItsStandDownDebt()
    {
        var session = Session((11, "1st Corps:7", "1st Corps"));
        var saved = Saved(missionKey: "Scouts:2");
        var result = Rebind(saved, session, out _, out _, out var missions);
        AssertEqual(0, missions.Count, "there is no screen left to stand down");
        AssertEqual(1, result.MissionsDropped, "counted");
        AssertEqual(0, result.MissionsKept, "none kept");
    }

    // The chain the first review after a load runs, with the real pure steps: the file, the rebind against this session's
    // formations, the depot ledger's restore, the held-builder collection and the allocation. A Wished task's builder must be
    // the DepotBuilder and must not be moved, on a cold load and on a return from battle (new instance ids and, there, a
    // commander lost in the fight, so the key's name part is all that still matches).
    private static void FirstReviewAfterALoadPinsTheDepotBuilder()
    {
        foreach (bool battleReturn in new[] { false, true })
        {
            string label = battleReturn ? "return from battle: " : "cold load: ";
            int builderId = battleReturn ? 901 : 77;
            var session = ExecutorFixtures.Wide(1);
            var builder = ExecutorFixtures.Corps(builderId, 250f, 9000f);
            builder.Z = 70f;
            builder.WaypointX = 250f;
            builder.WaypointZ = 70f;
            builder.Name = "Pioneers";
            builder.UnitKey = battleReturn ? "Pioneers:42" : "Pioneers:41";
            session.Own.Add(builder);
            session.Own.Add(ExecutorFixtures.Corps(2, 300f, 15000f));
            var sectors = ExecutorFixtures.Sectors(session);
            int sector = sectors.SectorAt(250f, 70f);

            // the last session's ledger, written to the file
            var before = new DepotTaskLedger();
            before.Restore(new[]
            {
                new DepotTaskRecord { X = 250f, Z = 0f, SectorId = sector, BuilderInstanceId = 5, BuilderUnitKey = "Pioneers:41", State = DepotTaskState.Wished, StartedDay = 10, EndedDay = -1, WishX = 250f, WishZ = 70f }
            });
            var written = new PlannerSnapshot();
            written.DepotTasks.AddRange(before.Tasks);
            var saved = RoundTrip(WithPlanner(written)).Planner[0];
            AssertEqual(-1, saved.DepotTasks[0].BuilderInstanceId, label + "the file carries no instance id");

            // the first review: rebind, ledger restore, held builders, allocation
            var moves = new List<MoveRecord>();
            var depots = new List<DepotTaskRecord>();
            var missions = new List<ReconMission>();
            PlannerRebind.Resolve(saved, session, moves, depots, missions);
            var ledger = new DepotTaskLedger();
            ledger.Restore(depots);
            var held = new List<HeldBuilder>();
            ledger.CollectHeldBuilders(held);
            AssertEqual(1, held.Count, label + "the builder is held on the first review, before the ledger's Sync has run");
            AssertEqual(builderId, held[0].InstanceId, label + "this session's formation");
            AssertTrue(held[0].Wished && held[0].TargetX == 250f && held[0].TargetZ == 70f, label + "at its wish position");

            var plan = ExecutorFixtures.HandPlan(session, new[] { 2 }); // the plan lists no task: only the pin keeps the builder
            var allocation = ForceAllocator.Allocate(plan, session, sectors, null, null, null, null, held);
            AssertTrue(allocation.TryGet(builderId, out var assigned), label + "the builder is allocated");
            AssertEqual(FormationRole.DepotBuilder, assigned.Role, label + "it is the DepotBuilder");
            AssertTrue(assigned.Withheld && assigned.TargetX == 250f && assigned.TargetZ == 70f, label + "withheld at the wish position");
            var host = new FakeMoverHost();
            CampaignMover.Review(ExecutorFixtures.Inputs(session, plan, allocation, ExecutorFixtures.OpenTempo()), host, new MoveTracker(), ExecutorFixtures.State());
            AssertFalse(host.Moves.Exists(m => m.id == builderId), label + "the mover does not move it");

            // control: a ledger restored without the binding (the reset this replaced) pins nothing and loses the role
            var unbound = new List<DepotTaskRecord>();
            foreach (var d in depots)
            {
                var r = d;
                r.BuilderInstanceId = -1;
                unbound.Add(r);
            }
            var loose = new DepotTaskLedger();
            loose.Restore(unbound);
            var none = new List<HeldBuilder>();
            loose.CollectHeldBuilders(none);
            AssertEqual(0, none.Count, label + "control: unbound, nothing is held");
            var free = ForceAllocator.Allocate(plan, session, sectors, null, null, null, null, none);
            AssertTrue(free.TryGet(builderId, out var freed) && freed.Role != FormationRole.DepotBuilder, label + "control: and the allocation re-tasks the builder (" + freed.Role + ")");
        }
    }

    private static void RebindClearsWhatItFills()
    {
        var session = Session((11, "1st Corps:7", "1st Corps"));
        var moves = new List<MoveRecord> { new MoveRecord { UnitKey = "stale:1" } };
        var depots = new List<DepotTaskRecord> { new DepotTaskRecord { BuilderUnitKey = "stale:1" } };
        var missions = new List<ReconMission> { new ReconMission { UnitKey = "stale:1" } };
        PlannerRebind.Resolve(Saved(moveKey: "1st Corps:7"), session, moves, depots, missions);
        AssertEqual(1, moves.Count, "the lists hold only this rebind's records");
        AssertEqual(0, depots.Count, "the previous rebind's depot task is gone");
        AssertEqual(0, missions.Count, "and its mission");
    }

    private static void RebindOfNothingIsEmpty()
    {
        var moves = new List<MoveRecord> { new MoveRecord { UnitKey = "stale:1" } };
        var depots = new List<DepotTaskRecord>();
        var missions = new List<ReconMission>();
        var result = PlannerRebind.Resolve(null, Session((11, "1st Corps:7", "1st Corps")), moves, depots, missions);
        AssertEqual(0, moves.Count, "no saved state: the lists are emptied");
        AssertEqual(0, result.MovesKept + result.MovesDropped + result.MissionsKept + result.MissionsDropped + result.DepotTasksRebound, "and nothing is counted");
        PlannerRebind.Resolve(Saved(moveKey: "1st Corps:7"), null, moves, depots, missions);
        AssertEqual(0, moves.Count, "no snapshot: nothing is bound");
    }

    private static void IndexOfKeyOrNameRefusesAnAmbiguousName()
    {
        var session = Session((31, "Reserve:1", "Reserve"), (32, "Reserve:2", "Reserve"), (33, "Army:3", "Army"));
        AssertEqual(0, session.IndexOfKeyOrName("Reserve:1"), "the exact key wins whatever the names");
        AssertEqual(-1, session.IndexOfKeyOrName("Reserve:77"), "two formations share the name: there is no safe binding");
        AssertEqual(2, session.IndexOfKeyOrName("Army:77"), "the one formation of that name");
        AssertEqual(-1, session.IndexOfKeyOrName("Nobody:1"), "no such name");
        AssertEqual(-1, session.IndexOfKeyOrName(null), "null");
        AssertEqual(-1, session.IndexOfKeyOrName(""), "empty");
    }

    // ---- no instance ids in the file ----

    private static void NoDtoCarriesInstanceId()
    {
        var dtoTypes = typeof(SidecarDto).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(SidecarDto).Namespace && (t.Name.EndsWith("Dto", StringComparison.Ordinal) || t.Name == nameof(PlannerStateDto)))
            .ToList();
        AssertTrue(dtoTypes.Count >= 12, "the DTO types were found: " + dtoTypes.Count);
        AssertTrue(dtoTypes.Contains(typeof(PlannerStateDto)) && dtoTypes.Contains(typeof(MoveDto)) && dtoTypes.Contains(typeof(ReconMissionDto)) && dtoTypes.Contains(typeof(DepotTaskDto)),
            "the planner's DTOs are among them");
        var offenders = new List<string>();
        foreach (var t in dtoTypes)
            foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property))
                if (m.Name.IndexOf("InstanceId", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("InstanceID", StringComparison.OrdinalIgnoreCase) >= 0)
                    offenders.Add(t.Name + "." + m.Name);
        AssertEqual("", string.Join(",", offenders), "no DTO member is an instance id");
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/PersistenceDto.cs");
        AssertFalse(src.IndexOf("InstanceId", StringComparison.OrdinalIgnoreCase) >= 0, "and the DTO file never names one");
    }

    private static void EncodedFileNamesNoInstanceId()
    {
        var p = SamplePlanner();
        var move = p.Moves[0];
        move.InstanceId = 123456;
        p.Moves[0] = move;
        var mission = p.ReconMissions[0];
        mission.InstanceId = 654321;
        p.ReconMissions[0] = mission;
        var task = p.DepotTasks[0];
        task.BuilderInstanceId = 777777;
        p.DepotTasks[0] = task;
        string json = SidecarDtoCodec.Serialize(SidecarDtoCodec.Encode(WithPlanner(p)));
        AssertFalse(json.IndexOf("instanceid", StringComparison.OrdinalIgnoreCase) >= 0, "no instance id field in the file");
        AssertFalse(json.Contains("123456") || json.Contains("654321") || json.Contains("777777"), "and none of the session's instance ids is written");
        AssertTrue(json.Contains("\"unitKey\": \"1st Corps:7\""), "a move is its unit key");
        AssertTrue(json.Contains("\"unitKey\": \"Cavalry Corps:3\""), "a mission is its unit key");
        AssertTrue(json.Contains("\"builderUnitKey\": \"Pioneers:41\""), "a depot task's builder is its unit key");
        AssertFalse(json.Contains("minorOfficerProfiles") || json.Contains("\"cic\"") || json.Contains("probeState") || json.Contains("probeHysteresis") || json.Contains("recentReplans"),
            "the retired layers' state is no longer written (dead officer profiles, the CIC, the old probe)");
    }

    // ---- versions ----

    private static void FutureVersionRejected()
    {
        string json = "{\"version\":" + (SidecarDto.CurrentVersion + 1) + ",\"factions\":[]}";
        AssertFalse(SidecarDtoCodec.TryDeserialize(json, out var dto, out var reason), "the next version is not read by this one");
        AssertTrue(dto == null, "no half-read document");
        AssertTrue(reason.Contains("unsupported-version " + (SidecarDto.CurrentVersion + 1)), "the reason names the version: " + reason);
        AssertTrue(reason.Contains("1.." + SidecarDto.CurrentVersion), "and the supported range: " + reason);
        AssertTrue(SidecarDtoCodec.TryDeserialize("{\"version\":" + SidecarDto.CurrentVersion + "}", out _, out _), "the current version is read");
        AssertTrue(SidecarDtoCodec.TryDeserialize("{\"version\":1}", out _, out _), "version 1 is read");
        AssertTrue(SidecarDtoCodec.TryDeserialize("{\"version\":2}", out _, out _), "version 2 is read");
    }

    private static void VersionZeroRejected()
    {
        AssertFalse(SidecarDtoCodec.TryDeserialize("{\"version\":0}", out _, out var reason), "version 0");
        AssertTrue(reason.Contains("unsupported-version 0"), reason);
        AssertFalse(SidecarDtoCodec.TryDeserialize("{\"version\":-1}", out _, out _), "a negative version");
    }

    // ---- the fields that were already in the file ----

    private static void CouriersRoundTrip()
    {
        var snap = WithPlanner(SamplePlanner());
        snap.CampaignCouriers = new List<CampaignCourierDto>
        {
            new CampaignCourierDto
            {
                Unit = "1st Corps", UnitCommander = 7, Type = 5, X = 10.5f, Y = 1f, Z = -20.25f, Name = "Order", Rotation = 0.5f, Width = 3f, Depth = 4f,
                Origin = "campaign-planner", Issuer = 17, ShowZone = true, PlayerCommand = "1st Corps#7", IssuedAt = 2.5, DueAt = 2.75
            },
            new CampaignCourierDto { Unit = "2nd Corps", UnitCommander = 8, Type = 1, PlayerCommand = null, IssuedAt = 3.0, DueAt = 3.1 }
        };
        var back = RoundTrip(snap);
        AssertEqual(2, back.CampaignCouriers.Count, "both pending orders");
        var c = back.CampaignCouriers[0];
        AssertEqual("1st Corps", c.Unit, "unit");
        AssertEqual(7, c.UnitCommander, "unit commander");
        AssertEqual(5, c.Type, "type");
        AssertNear(10.5f, c.X, 1e-4f, "x");
        AssertNear(-20.25f, c.Z, 1e-4f, "z");
        AssertEqual("Order", c.Name, "name");
        AssertEqual("campaign-planner", c.Origin, "origin");
        AssertEqual(17, c.Issuer, "issuer");
        AssertTrue(c.ShowZone, "show zone");
        AssertEqual("1st Corps#7", c.PlayerCommand, "the player's command");
        AssertNear(2.5f, (float)c.IssuedAt, 1e-6f, "issued at");
        AssertNear(2.75f, (float)c.DueAt, 1e-6f, "due at");
        AssertTrue(back.CampaignCouriers[1].PlayerCommand == null, "a sidecar written before the identity existed");
        snap.CampaignCouriers = new List<CampaignCourierDto>();
        AssertTrue(RoundTrip(snap).CampaignCouriers == null, "an empty queue is written as none");
    }

    private static void StageEPolicyFieldsRoundTrip()
    {
        var snap = WithPlanner(SamplePlanner());
        snap.PolicyNeeds[0] = new[] { 0.1f, 0.9f, 0.3f };
        snap.PolicyPicks[0] = 2;
        snap.PolicyPicks[1] = -1;
        var back = RoundTrip(snap);
        AssertEqual(3, back.PolicyNeeds[0].Length, "the smoothed needs");
        AssertNear(0.9f, back.PolicyNeeds[0][1], 1e-6f, "need 1");
        AssertEqual(2, back.PolicyPicks[0], "the ledger's pick");
        AssertEqual(-1, back.PolicyPicks[1], "no pick stays -1");
        AssertTrue(back.PolicyNeeds[1] == null, "no needs yet stays none");
    }

    private static void PlayerComplianceRoundTrips()
    {
        var snap = WithPlanner(SamplePlanner());
        snap.PlayerCompliance = new PlayerComplianceDto { CareerKey = "Lee:1862", Reliability = 0.4f, Samples = 11 };
        var back = RoundTrip(snap);
        AssertEqual("Lee:1862", back.PlayerCompliance.CareerKey, "career key");
        AssertNear(0.4f, back.PlayerCompliance.Reliability, 1e-6f, "reliability");
        AssertEqual(11, back.PlayerCompliance.Samples, "samples");
        snap.PlayerCompliance = null;
        AssertTrue(RoundTrip(snap).PlayerCompliance == null, "none stays none");
    }

    private static void NonCompliancePlayerRunRestoresWithItsLastDay()
    {
        var run = new ForceAllocator.PlayerNonCompliance();
        run.Restore(4, 812);
        AssertEqual(4, run.Days, "the run");
        AssertEqual(812, run.LastDay, "and the day it last counted");
        run.Observe(812, true, 0f);
        AssertEqual(4, run.Days, "the first review after a load does not count that day twice");
        run.Observe(813, true, 0f);
        AssertEqual(5, run.Days, "the next day extends the run");
        run.Observe(100, true, 0f);
        AssertEqual(1, run.Days, "a day that goes backwards restarts the run");
        run.Restore(-3, 5);
        AssertEqual(0, run.Days, "a negative saved run is no run");
    }

    // ---- the coordinator and the runtime (pinned in source: both are MonoBehaviour / vanilla bound) ----

    private static void CoordinatorSavesAndLoadsThePlanner()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        string build = BodyOf(src, "private SidecarSnapshot BuildSnapshot(");
        AssertTrue(HasLine(build, "snap.Planner[alliance] = CampaignPlannerRuntime.Export(alliance);"), "the save exports each alliance's planner state");
        AssertTrue(HasLine(build, "snap.ModWrittenObjective[alliance] = ModWrittenObjective.Get(alliance);"), "and the objective the mod wrote");
        AssertTrue(HasLine(build, "snap.CampaignCouriers = WlCampaignCourierRuntime.Export();"), "the order flow's courier queue stays in the file");
        AssertTrue(HasLine(build, "snap.PlayerCompliance = PlayerComplianceRuntime.Export();"), "and the compliance estimate");
        AssertTrue(HasLine(build, "snap.PolicyPicks[alliance] = _work.PolicyPlans[alliance].CurrentPick;"), "and the stage E pick");
        string apply = BodyOf(src, "private void ApplySnapshot(");
        AssertTrue(HasLine(apply, "CampaignPlannerRuntime.Restore(alliance, snap.Planner[alliance]);"), "the load restores each alliance's planner state");
        AssertTrue(HasLine(apply, "ModWrittenObjective.Restore(snap.ModWrittenObjective);"), "and the mod-written objective");
        AssertTrue(HasLine(apply, "WlCampaignCourierRuntime.Restore(snap.CampaignCouriers);"), "and the courier queue");
        AssertTrue(HasLine(apply, "PlayerComplianceRuntime.Restore(snap.PlayerCompliance);"), "and the compliance estimate");
        AssertTrue(HasLine(apply, "_work.PolicyPlans[alliance].Restore(snap.PolicyPicks[alliance]);"), "and the stage E pick");
        AssertFalse(src.Contains("snap.Cics") || src.Contains("snap.ProbeStates") || src.Contains("snap.RecentReplans") || src.Contains("snap.MinorOfficers"), "no retired layer is saved or loaded");
    }

    private static void RuntimeRebindsOnTheFirstReviewWithFormations()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string review = BodyOf(src, "internal static void ReviewPlan(");
        int build = review.IndexOf("CampaignSnapshotRuntime.Build(", StringComparison.Ordinal);
        int rebind = review.IndexOf("RebindSaved(alliance, snapshot);", StringComparison.Ordinal);
        int detect = review.IndexOf("EnemyDetectionRuntime.Detect(", StringComparison.Ordinal);
        AssertTrue(build >= 0 && rebind > build && detect > rebind, "the saved records are bound right after the snapshot is built, before any ledger reads them");
        int collect = review.IndexOf("CollectHeldBuilders(heldBuilders);", StringComparison.Ordinal);
        AssertTrue(collect > rebind, "and before the held builders are collected: one order, rebind then collect");
        AssertEqual(1, System.Text.RegularExpressions.Regex.Matches(src, @"CollectHeldBuilders\(").Count, "the runtime collects them once, in the review");
        string bind = BodyOf(src, "private static void RebindSaved(");
        AssertTrue(HasLine(bind, "if (saved == null || snapshot.Own.Count == 0) return;"), "a snapshot with no formation waits for the next review rather than dropping every record");
        int cleared = bind.IndexOf("PendingRebind[alliance] = null;", StringComparison.Ordinal);
        int resolve = bind.IndexOf("PlannerRebind.Resolve(saved, snapshot, ReboundMoves, ReboundDepotTasks, ReboundMissions);", StringComparison.Ordinal);
        AssertTrue(cleared >= 0 && resolve > cleared, "once, then resolved");
        AssertTrue(bind.Contains("\" depotTasks=\" + result.DepotTasksRebound + \"/\" + ReboundDepotTasks.Count"), "the rebind row reports how many depot builders were found (the result's only production reader)");
        AssertTrue(HasLine(bind, "MoveTrackers.For(alliance)?.Restore(ReboundMoves);"), "the mover's tracker");
        AssertTrue(HasLine(bind, "DepotTaskLedgers.For(alliance)?.Restore(ReboundDepotTasks);"), "the depot ledger");
        AssertTrue(HasLine(bind, "Recon[alliance].Restore(ReboundMissions);"), "the recon mission ledger");
        string restore = BodyOf(src, "internal static void Restore(int alliance, PlannerSnapshot saved)");
        AssertTrue(HasLine(restore, "States[alliance] = saved.State;"), "the hysteresis and seed state is restored at once");
        AssertTrue(HasLine(restore, "PendingRebind[alliance] = saved;"), "the unit-keyed records wait for the rebind");
        AssertTrue(HasLine(restore, "EnemyDetectionRuntime.ModelFor(alliance).Restore(saved.Beliefs);"), "the belief picture at once (a belief names an enemy, not a formation)");
        AssertTrue(HasLine(restore, "CommanderAppointmentRuntime.Book.For(alliance)?.Restore(saved.Appointments);"), "the appointment protection windows");
        AssertTrue(HasLine(restore, "NonCompliance[alliance].Restore(saved.UnreliableDays, saved.UnreliableLastDay);"), "the unreliable-days run");
        AssertTrue(HasLine(BodyOf(src, "private static void ResetPlanState()"), "for (int a = 0; a < PendingRebind.Length; a++) PendingRebind[a] = null;"),
            "a new campaign forgets a pending rebind");
    }

    private static void RuntimeExportsThePendingRecordsWhenNoReviewRanSinceTheLoad()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string export = BodyOf(src, "internal static PlannerSnapshot Export(");
        int pending = export.IndexOf("var pending = PendingRebind[alliance];", StringComparison.Ordinal);
        int live = export.IndexOf("DepotTaskLedgers.For(alliance)", StringComparison.Ordinal);
        AssertTrue(pending >= 0 && live > pending, "a save before the first review writes the saved records back, not the empty live ledgers");
        AssertTrue(HasLine(export, "saved.Moves.AddRange(pending.Moves);") && HasLine(export, "saved.ReconMissions.AddRange(pending.ReconMissions);")
            && HasLine(export, "saved.DepotTasks.AddRange(pending.DepotTasks);"), "all three");
        AssertTrue(HasLine(export, "if (alliance < 0 || alliance >= States.Length) return null;"), "alliance 2 (Europe) has no planner state");
    }

    private static void MigrationLineIsLoggedOnce()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        AssertTrue(src.Contains("if (snapshot.SourceVersion < SidecarDto.CurrentVersion)"), "only a file older than this version logs a migration");
        AssertTrue(src.Contains("sidecar v{snapshot.SourceVersion} migrated to v{SidecarDto.CurrentVersion}: plans dropped={snapshot.PlansDropped}"), "naming both versions and the plans dropped");
    }
}
