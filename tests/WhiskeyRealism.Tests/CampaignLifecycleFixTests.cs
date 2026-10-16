using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

// WS C1 — campaign lifecycle regression tests: the battle trigger, the planner eligibility gate, sidecar file handling,
// scene resets, era and capital signals, the review cadence (the sidecar's v3 content is PlannerSidecarTests).
internal static class CampaignLifecycleFixTests
{
    public static void Run()
    {
        // R4 — battle resets
        Check("c1 r4 minor out-of-theater battle does not dirty plan", R4MinorOutOfTheaterBattleDoesNotDirtyPlan);
        Check("c1 r4 minor battle at target does not dirty plan", R4MinorBattleAtTargetDoesNotDirtyPlan);
        Check("c1 r4 major battle elsewhere does not dirty plan", R4MajorBattleElsewhereDoesNotDirtyPlan);
        Check("c1 r4 major battle in target theater dirties plan", R4MajorBattleInTargetTheaterDirtiesPlan);
        Check("c1 r4 major battle near target dirties plan", R4MajorBattleNearTargetDirtiesPlan);

        // R16 — director clamp per alliance + AI-controlled alliance gate
        Check("c1 r16 two alliances reviewed in the same instant both publish", R16TwoAlliancesSameInstantBothPublish);
        Check("c1 r16 clamp dedupes same alliance same game day", R16ClampDedupesSameAllianceSameDay);
        Check("c1 r16 player alliance outside w&l gets no cic", R16PlayerAllianceOutsideWlGetsNoCic);
        Check("c1 r16 w&l ai-vs-ai keeps player alliance cic unless player is cic", R16WlAiVsAiKeepsPlayerAllianceCic);
        Check("c1 r16 aifaction list is ground truth once ready", R16AiFactionListIsGroundTruth);

        // PER — sidecar persistence
        Check("c1 per sidecar round trip preserves the era, memory, succession and history", PerSidecarRoundTripPreservesState);
        Check("c1 per missing fields decode without throwing or fabricating posture", PerMissingFieldsDecodeSafely);
        Check("c1 per mod-written campaign objective survives reload", PerModWrittenObjectiveSurvivesReload);
        Check("c1 per runtime counters survive a battle round trip", PerRuntimeCountersSurviveRoundTrip);
        Check("c1 per corrupt json is rejected", PerCorruptJsonIsRejected);
        Check("c1 per newer sidecar version is rejected", PerNewerVersionIsRejected);
        Check("c1 per forced succession ids are not persisted", PerForcedSuccessionIdsAreNotPersisted);
        Check("c1 per atomic write keeps bak and leaves no temp", PerAtomicWriteKeepsBakAndNoTemp);
        Check("c1 per replace fallback survives original already moved to bak", PerReplaceFallbackSurvivesMissingOriginal);
        Check("c1 per load falls back to bak when primary is corrupt", PerLoadFallsBackToBak);
        Check("c1 per load falls back to tmp when primary and bak are missing", PerLoadFallsBackToTmp);
        Check("c1 per corrupt primary prefers the newer tmp over an older bak", PerCorruptPrimaryPrefersNewerTmp);
        Check("c1 per corrupt primary ignores a stale tmp older than bak", PerCorruptPrimaryIgnoresStaleTmp);
        Check("c1 per save sequence round trips", PerSaveSequenceRoundTrips);
        Check("c1 per load reports no sidecar when nothing is usable", PerLoadReportsNothingUsable);

        // R15/H3 — reset between campaigns
        Check("c1 r15 new campaign scene without load resets", R15NewCampaignSceneWithoutLoadResets);
        Check("c1 r15 load before first update keeps loaded state", R15LoadBeforeFirstUpdateKeepsLoadedState);
        Check("c1 r15 same campaign scene never resets twice", R15SameSceneNeverResetsTwice);
        Check("c1 r15 reset reason reflects the scene operation", R15ResetReasonReflectsOperation);
        Check("c1 r15 era derives from date in both directions", R15EraDerivesFromDateBothDirections);
        Check("c1 r15 era transition stays monotonic within a campaign", R15EraTransitionStaysMonotonic);

        // C-HardWired
        Check("c1 hardwired capital threat mirrors vanilla defend-capital score", HardWiredCapitalThreatMirrorsVanilla);
        Check("c1 hardwired capital streak advances once per day and counts skipped days", HardWiredCapitalStreakAdvancesPerDay);
        Check("c1 hardwired days since front change", HardWiredDaysSinceFrontChange);

        // Cadence
        Check("c1 cadence skipped weekly gate detected across fast-forward", CadenceSkippedWeeklyGateDetected);
        Check("c1 cadence skipped weekly gate across month rollover", CadenceSkippedWeeklyGateAcrossMonth);
        Check("c1 cadence consecutive days skip nothing", CadenceConsecutiveDaysSkipNothing);
        Check("c1 cadence skipped alternating day forces both alliances", CadenceSkippedAlternatingDayForcesBothAlliances);
        Check("c1 cadence review only runs once vanilla marks the campaign live", CadenceReviewOnlyWhenCampaignLive);
        Check("c1 cadence day serial counts calendar days", CadenceDaySerialCountsCalendarDays);
    }

    private static void Check(string name, Action test)
    {
        test();
        Console.WriteLine("PASS " + name);
    }

    // ------------------------------------------------------------------
    // Shared builders
    // ------------------------------------------------------------------

    private static int Serial(int day, int month, int year) => year * 372 + month * 31 + day;
    // The review clock (ReviewCadenceCatchUp.DaySerial): calendar days, not the 372-day encoding above.
    private static int Cal(int day, int month, int year) => ReviewCadenceCatchUp.DaySerial(day, month, year);

    // ------------------------------------------------------------------
    // R3
    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    // R4
    // ------------------------------------------------------------------

    private static BattleHistoryRecord Battle(int resultType, float x, float z)
    {
        return new BattleHistoryRecord
        {
            BattleName = "Battle of Test",
            Day = 1, Month = 6, Year = 1862,
            LandOrSea = 0,
            AllianceWon = 0,
            BattleResultType = resultType,
            PositionX = x,
            PositionZ = z,
            Alliance = new[] { 0, 1 }
        };
    }

    private static BattleReplanQuery TargetQuery()
    {
        return new BattleReplanQuery
        {
            HasTarget = true,
            TargetX = 760f,
            TargetZ = 60f,
            TargetTheater = Theater.East,
            Radius = 275f
        };
    }

    private static void R4MinorOutOfTheaterBattleDoesNotDirtyPlan()
    {
        var q = TargetQuery();
        q.BattleTheater = Theater.West;
        TestHarness.AssertFalse(PlanTriggers.BattleInvalidatesPlan(Battle(0, 100f, -300f), q), "minor western battle");
    }

    private static void R4MinorBattleAtTargetDoesNotDirtyPlan()
    {
        var q = TargetQuery();
        q.BattleTheater = Theater.East;
        TestHarness.AssertFalse(PlanTriggers.BattleInvalidatesPlan(Battle(2, 760f, 60f), q), "minor battle never resets an operation");
    }

    private static void R4MajorBattleElsewhereDoesNotDirtyPlan()
    {
        var q = TargetQuery();
        q.BattleTheater = Theater.West;
        TestHarness.AssertFalse(PlanTriggers.BattleInvalidatesPlan(Battle(1, 100f, -300f), q), "major battle in another theater");
    }

    private static void R4MajorBattleInTargetTheaterDirtiesPlan()
    {
        var q = TargetQuery();
        q.BattleTheater = Theater.East;
        TestHarness.AssertTrue(PlanTriggers.BattleInvalidatesPlan(Battle(3, 600f, 400f), q), "major battle in target theater");
    }

    private static void R4MajorBattleNearTargetDirtiesPlan()
    {
        var q = TargetQuery();
        q.BattleTheater = Theater.Unknown;
        TestHarness.AssertTrue(PlanTriggers.BattleInvalidatesPlan(Battle(1, 800f, 100f), q), "major battle within radius");
    }

    // ------------------------------------------------------------------
    // R16
    // ------------------------------------------------------------------

    private static void R16TwoAlliancesSameInstantBothPublish()
    {
        var clamp = new DirectorPublishClamp();
        int day = Serial(3, 6, 1862);
        TestHarness.AssertTrue(clamp.TryPublish(0, day), "alliance 0 publishes");
        TestHarness.AssertTrue(clamp.TryPublish(1, day), "alliance 1 publishes in the same instant");
    }

    private static void R16ClampDedupesSameAllianceSameDay()
    {
        var clamp = new DirectorPublishClamp();
        int day = Serial(3, 6, 1862);
        TestHarness.AssertTrue(clamp.TryPublish(1, day), "first publish");
        TestHarness.AssertFalse(clamp.TryPublish(1, day), "second publish same game day suppressed");
        TestHarness.AssertTrue(clamp.TryPublish(1, day + 1), "next game day publishes");
        TestHarness.AssertFalse(clamp.TryPublish(2, day), "out-of-range alliance rejected");
    }

    private static void R16PlayerAllianceOutsideWlGetsNoCic()
    {
        TestHarness.AssertFalse(PlannerEligibility.ShouldRun(0, factionListReady: false, hasAiFaction: false, playerAlliance: 0, aiVsAi: false, playerIsWlCic: false), "player alliance, no ai_vs_ai");
        TestHarness.AssertTrue(PlannerEligibility.ShouldRun(1, factionListReady: false, hasAiFaction: false, playerAlliance: 0, aiVsAi: false, playerIsWlCic: false), "enemy alliance runs");
    }

    private static void R16WlAiVsAiKeepsPlayerAllianceCic()
    {
        TestHarness.AssertTrue(PlannerEligibility.ShouldRun(0, factionListReady: false, hasAiFaction: false, playerAlliance: 0, aiVsAi: true, playerIsWlCic: false), "w&l subordinate: vanilla AI runs player alliance");
        TestHarness.AssertFalse(PlannerEligibility.ShouldRun(0, factionListReady: true, hasAiFaction: true, playerAlliance: 0, aiVsAi: true, playerIsWlCic: true), "w&l player is CIC");
    }

    private static void R16AiFactionListIsGroundTruth()
    {
        TestHarness.AssertFalse(PlannerEligibility.ShouldRun(1, factionListReady: true, hasAiFaction: false, playerAlliance: 0, aiVsAi: false, playerIsWlCic: false), "no AIFaction => no CIC");
        TestHarness.AssertTrue(PlannerEligibility.ShouldRun(0, factionListReady: true, hasAiFaction: true, playerAlliance: 0, aiVsAi: true, playerIsWlCic: false), "AIFaction present => CIC");
    }

    // ------------------------------------------------------------------
    // R5
    // ------------------------------------------------------------------

    // Mirrors the CampaignObjective fields the CIC reads: UniqueObjectiveID and the
    // town/IIP `objectives` list (empty for abstract objectives 0-28 in campaignobjectives.dat).
    internal sealed class FakeObjective
    {
        public int UniqueObjectiveID;
        public List<object> objectives = new List<object>();
        public FakeObjective(int id, int towns = 0)
        {
            UniqueObjectiveID = id;
            for (int i = 0; i < towns; i++) objectives.Add(new object());
        }
    }

    // ------------------------------------------------------------------
    // PER
    // ------------------------------------------------------------------

    // A snapshot with every non-planner field set (the planner's content is PlannerSidecarTests').
    internal static SidecarSnapshot SampleSnapshot()
    {
        var snap = SidecarSnapshot.Empty();
        snap.Eras[0] = EraStage.Operational1862;
        snap.Eras[1] = EraStage.Operational1862;
        snap.DirectorMemories[0] = new DirectorMemory
        {
            LastPosture = new DirectorPosture { Pace = CampaignPace.TooQuiet, Intent = StrategicIntent.Probe, Risk = CollapseRisk.Elevated, TheaterPriority = Theater.East },
            CapitalDangerStreakDays = 3
        };
        snap.FiscalMemories[1] = new WhiskeyRealism.Strategic.Fiscal.FiscalStateMemory { PreviousPosture = WhiskeyRealism.Strategic.Fiscal.FiscalPosture.CreditDefense, StableTicksAboveEmergency = 4, EmergencyResidue = true };
        snap.FiredEvents.Add(1);
        snap.AppliedEvents.Add(1);
        snap.Battles.Add(Battle(1, 760f, 60f));
        snap.LastSeenMonth = 3;
        snap.LastSeenYear = 1862;
        return snap;
    }

    private static void PerSidecarRoundTripPreservesState()
    {
        var back = RoundTrip(SampleSnapshot());
        TestHarness.AssertEqual(EraStage.Operational1862, back.Eras[1].Value, "era kept");
        TestHarness.AssertEqual(CampaignPace.TooQuiet, back.DirectorMemories[0].LastPosture.Pace, "posture pace");
        TestHarness.AssertEqual(3, back.DirectorMemories[0].CapitalDangerStreakDays, "capital streak");
        TestHarness.AssertEqual(4, back.FiscalMemories[1].StableTicksAboveEmergency, "fiscal stable ticks");
        TestHarness.AssertTrue(back.FiscalMemories[1].EmergencyResidue, "fiscal residue");
        TestHarness.AssertTrue(back.FiredEvents.Contains(1) && back.AppliedEvents.Contains(1), "succession");
        TestHarness.AssertEqual(1, back.Battles.Count, "battle history");
        TestHarness.AssertEqual(3, back.LastSeenMonth, "last seen month restored");
        TestHarness.AssertEqual(1862, back.LastSeenYear, "last seen year restored");
    }

    private static void PerMissingFieldsDecodeSafely()
    {
        const string json = "{\"version\":1,\"factions\":[{\"factionId\":0,\"currentEra\":\"Operational1862\",\"cic\":{\"officerName\":\"Lincoln\"},\"directorMemory\":{\"lastFullRefresh\":5}}],\"succession\":null,\"minorOfficerProfiles\":[{\"commanderId\":5}],\"battleHistory\":null}";
        TestHarness.AssertTrue(SidecarDtoCodec.TryDeserialize(json, out var dto, out var reason), "deserialize: " + reason);
        TestHarness.AssertTrue(SidecarDtoCodec.TryDecode(dto, out var snap, out reason), "decode: " + reason);
        TestHarness.AssertEqual(EraStage.Operational1862, snap.Eras[0].Value, "era restored; the removed cic and minor-officer fields are ignored");
        TestHarness.AssertTrue(snap.DirectorMemories[0].LastPosture == null, "missing pace/intent/risk must not fabricate an all-zero posture");
        TestHarness.AssertEqual(0, snap.FiredEvents.Count, "null succession tolerated");
        TestHarness.AssertEqual(-1, snap.LastSeenMonth, "v1 sidecar has no last-seen month");
        TestHarness.AssertTrue(snap.Planner[0] == null, "a v1 file carries no planner state");
    }

    private static void PerModWrittenObjectiveSurvivesReload()
    {
        var snap = SampleSnapshot();
        snap.ModWrittenObjective[0] = 29;
        var back = RoundTrip(snap);
        TestHarness.AssertEqual(29, back.ModWrittenObjective[0], "mod-written objective restored");
        TestHarness.AssertEqual(-1, back.ModWrittenObjective[1], "none written for alliance 1");
        // After reload, a planless faction still releases the stale id the mod wrote (-1 = vanilla re-picks).
        int write = SteeringRules.ObjectiveWrite(-1, false, current: 29, lastWrittenByMod: back.ModWrittenObjective[0].Value);
        TestHarness.AssertEqual(-1, write, "stale mod objective released after reload");
    }

    private static void PerRuntimeCountersSurviveRoundTrip()
    {
        int day = Serial(1, 6, 1862);
        var snap = SampleSnapshot();
        snap.FrontChangedDay[1] = day - 40;
        snap.CapitalStreakDay[0] = day - 1;
        var back = RoundTrip(snap);
        TestHarness.AssertEqual(day - 40, back.FrontChangedDay[1], "front-change stamp");
        TestHarness.AssertEqual(-1, back.FrontChangedDay[0], "unset stamp stays -1");
        TestHarness.AssertEqual(day - 1, back.CapitalStreakDay[0], "capital streak day");
    }

    private static SidecarSnapshot RoundTrip(SidecarSnapshot snap)
    {
        string json = SidecarDtoCodec.Serialize(SidecarDtoCodec.Encode(snap));
        TestHarness.AssertTrue(SidecarDtoCodec.TryDeserialize(json, out var dto, out var reason), "deserialize: " + reason);
        TestHarness.AssertTrue(SidecarDtoCodec.TryDecode(dto, out var back, out reason), "decode: " + reason);
        return back;
    }

    private static void PerCorruptJsonIsRejected()
    {
        TestHarness.AssertFalse(SidecarDtoCodec.TryDeserialize("{\"version\":1,\"factions\":[{", out _, out _), "truncated json");
        TestHarness.AssertFalse(SidecarDtoCodec.TryDeserialize("", out _, out _), "empty file");
    }

    private static void PerNewerVersionIsRejected()
    {
        TestHarness.AssertFalse(SidecarDtoCodec.TryDeserialize("{\"version\":99}", out _, out var reason), "future version");
        TestHarness.AssertTrue(reason.Contains("version"), "reason names the version: " + reason);
    }

    private static void PerForcedSuccessionIdsAreNotPersisted()
    {
        var snap = SampleSnapshot();
        snap.FiredEvents.Add(5);
        snap.ForcedEvents.Add(5);
        var dto = SidecarDtoCodec.Encode(snap);
        TestHarness.AssertTrue(dto.Succession.FiredEvents.Contains(1), "naturally fired id persisted");
        TestHarness.AssertFalse(dto.Succession.FiredEvents.Contains(5), "test-mode forced id not persisted");
    }

    private static void WithTempDir(Action<string> body)
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wr-c1-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try { body(System.IO.Path.Combine(dir, "whiskeyrealism.json")); }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    private static string ValidSidecarJson(int lastSeenMonth, long saveSequence = 0)
    {
        var snap = SampleSnapshot();
        snap.LastSeenMonth = lastSeenMonth;
        snap.SaveSequence = saveSequence;
        return SidecarDtoCodec.Serialize(SidecarDtoCodec.Encode(snap));
    }

    private static void PerReplaceFallbackSurvivesMissingOriginal()
    {
        WithTempDir(path =>
        {
            // Win32 ReplaceFile can fail after it already renamed the original to .bak.
            System.IO.File.WriteAllText(path + ".bak", "old");
            System.IO.File.WriteAllText(path + ".tmp", "new");
            SidecarDtoCodec.ReplaceByCopy(path + ".tmp", path, path + ".bak");
            TestHarness.AssertEqual("new", System.IO.File.ReadAllText(path), "new content in place");
            TestHarness.AssertEqual("old", System.IO.File.ReadAllText(path + ".bak"), "backup untouched");
            TestHarness.AssertFalse(System.IO.File.Exists(path + ".tmp"), "temp removed");
        });
    }

    private static void PerLoadFallsBackToBak()
    {
        WithTempDir(path =>
        {
            System.IO.File.WriteAllText(path, "{\"version\":2,\"factions\":[{");
            System.IO.File.WriteAllText(path + ".bak", ValidSidecarJson(4));
            TestHarness.AssertTrue(SidecarDtoCodec.TryLoadWithFallback(path, out var snap, out var source, out var reason), "loads: " + reason);
            TestHarness.AssertEqual(path + ".bak", source, "source");
            TestHarness.AssertEqual(4, snap.LastSeenMonth, "bak content");
        });
    }

    private static void PerCorruptPrimaryPrefersNewerTmp()
    {
        WithTempDir(path =>
        {
            // Crash mid File.Copy in ReplaceByCopy: truncated primary, previous save in .bak,
            // complete newer save still in .tmp.
            System.IO.File.WriteAllText(path, "{\"version\":2,\"fac");
            System.IO.File.WriteAllText(path + ".bak", ValidSidecarJson(4, saveSequence: 5));
            System.IO.File.WriteAllText(path + ".tmp", ValidSidecarJson(6, saveSequence: 6));
            TestHarness.AssertTrue(SidecarDtoCodec.TryLoadWithFallback(path, out var snap, out var source, out var reason), "loads: " + reason);
            TestHarness.AssertEqual(path + ".tmp", source, "newest save chosen");
            TestHarness.AssertEqual(6, snap.LastSeenMonth, "tmp content");
        });
    }

    private static void PerCorruptPrimaryIgnoresStaleTmp()
    {
        WithTempDir(path =>
        {
            System.IO.File.WriteAllText(path, "garbage");
            System.IO.File.WriteAllText(path + ".bak", ValidSidecarJson(7, saveSequence: 7));
            System.IO.File.WriteAllText(path + ".tmp", ValidSidecarJson(3, saveSequence: 3));
            TestHarness.AssertTrue(SidecarDtoCodec.TryLoadWithFallback(path, out var snap, out var source, out var reason), "loads: " + reason);
            TestHarness.AssertEqual(path + ".bak", source, "stale tmp ignored");
            TestHarness.AssertEqual(7, snap.LastSeenMonth, "bak content");
        });
    }

    private static void PerSaveSequenceRoundTrips()
    {
        var snap = SampleSnapshot();
        snap.SaveSequence = 42;
        TestHarness.AssertEqual(42L, RoundTrip(snap).SaveSequence, "save sequence");
    }

    private static void PerLoadFallsBackToTmp()
    {
        WithTempDir(path =>
        {
            System.IO.File.WriteAllText(path + ".tmp", ValidSidecarJson(5));
            TestHarness.AssertTrue(SidecarDtoCodec.TryLoadWithFallback(path, out var snap, out var source, out var reason), "loads: " + reason);
            TestHarness.AssertEqual(path + ".tmp", source, "source");
            TestHarness.AssertEqual(5, snap.LastSeenMonth, "tmp content");
        });
    }

    private static void PerLoadReportsNothingUsable()
    {
        WithTempDir(path =>
        {
            TestHarness.AssertFalse(SidecarDtoCodec.TryLoadWithFallback(path, out _, out _, out var reason), "nothing on disk");
            TestHarness.AssertEqual("no-sidecar", reason, "reason");
            System.IO.File.WriteAllText(path, "garbage");
            TestHarness.AssertFalse(SidecarDtoCodec.TryLoadWithFallback(path, out _, out _, out reason), "only a corrupt primary");
            TestHarness.AssertTrue(reason != "no-sidecar", "rejection reason kept: " + reason);
        });
    }

    private static void PerAtomicWriteKeepsBakAndNoTemp()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wr-c1-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            string path = System.IO.Path.Combine(dir, "whiskeyrealism.json");
            SidecarDtoCodec.WriteAtomic(path, "first");
            TestHarness.AssertEqual("first", System.IO.File.ReadAllText(path), "first write");
            SidecarDtoCodec.WriteAtomic(path, "second");
            TestHarness.AssertEqual("second", System.IO.File.ReadAllText(path), "second write");
            TestHarness.AssertEqual("first", System.IO.File.ReadAllText(path + ".bak"), "previous kept as .bak");
            TestHarness.AssertFalse(System.IO.File.Exists(path + ".tmp"), "no temp left behind");
        }
        finally
        {
            System.IO.Directory.Delete(dir, true);
        }
    }

    // ------------------------------------------------------------------
    // R15
    // ------------------------------------------------------------------

    private static void R15NewCampaignSceneWithoutLoadResets()
    {
        var tracker = new CampaignEpochTracker();
        var sceneA = new object();
        var sceneB = new object();
        TestHarness.AssertTrue(tracker.ShouldResetOnUpdate(sceneA), "first campaign scene resets");
        TestHarness.AssertTrue(tracker.ShouldResetOnUpdate(sceneB), "new campaign scene (no AICampaign.Load) resets");
    }

    private static void R15LoadBeforeFirstUpdateKeepsLoadedState()
    {
        var tracker = new CampaignEpochTracker();
        var sceneA = new object();
        tracker.ShouldResetOnUpdate(sceneA);
        var sceneB = new object();
        tracker.NoteLoad(sceneB);
        TestHarness.AssertFalse(tracker.ShouldResetOnUpdate(sceneB), "load already reset + applied for this scene");

        var sceneC = new object();
        tracker.NoteLoad(null);
        TestHarness.AssertFalse(tracker.ShouldResetOnUpdate(sceneC), "load with unknown instance adopts the next scene");
        var sceneD = new object();
        TestHarness.AssertTrue(tracker.ShouldResetOnUpdate(sceneD), "pending marker consumed once");
    }

    private static void R15SameSceneNeverResetsTwice()
    {
        var tracker = new CampaignEpochTracker();
        var scene = new object();
        TestHarness.AssertTrue(tracker.ShouldResetOnUpdate(scene), "first sight");
        TestHarness.AssertFalse(tracker.ShouldResetOnUpdate(scene), "same scene");
        TestHarness.AssertFalse(tracker.ShouldResetOnUpdate(null), "no instance");
    }

    private static void R15ResetReasonReflectsOperation()
    {
        TestHarness.AssertEqual("battle-return-scene", CampaignEpochTracker.ResetReason(5), "op 5");
        TestHarness.AssertEqual("save-load-scene", CampaignEpochTracker.ResetReason(6), "op 6");
        TestHarness.AssertEqual("battle-scene", CampaignEpochTracker.ResetReason(3), "op 3");
        TestHarness.AssertEqual("battle-scene", CampaignEpochTracker.ResetReason(8), "op 8");
        TestHarness.AssertEqual("new-campaign-scene", CampaignEpochTracker.ResetReason(0), "op 0");
        TestHarness.AssertEqual("new-campaign-scene", CampaignEpochTracker.ResetReason(2), "op 2");
    }

    private static void R15EraDerivesFromDateBothDirections()
    {
        TestHarness.AssertEqual(EraStage.TotalWar1864, EraStageManager.ForDate(12, 1864, false, 3), "chapter 3");
        TestHarness.AssertEqual(EraStage.Amateur1861, EraStageManager.ForDate(6, 1861, false, 1), "back to 1861");
        TestHarness.AssertEqual(EraStage.Decisive1863, EraStageManager.ForDate(3, 1863, true, 2), "Vicksburg early keeps 1863");
        var era = new EraStageManager { Stage = EraStage.TotalWar1864 };
        era.ResetToDate(6, 1861, 1);
        TestHarness.AssertEqual(EraStage.Amateur1861, era.Stage, "reset re-derives the era backwards");
    }

    private static void R15EraTransitionStaysMonotonic()
    {
        var era = new EraStageManager { Stage = EraStage.Decisive1863 };
        era.CheckTransition(6, 1862, false, 2);
        TestHarness.AssertEqual(EraStage.Decisive1863, era.Stage, "CheckTransition never regresses");
        era.CheckTransition(11, 1864, false, 3);
        TestHarness.AssertEqual(EraStage.TotalWar1864, era.Stage, "advances");
    }

    // ------------------------------------------------------------------
    // C-Probe
    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    // C-HardWired
    // ------------------------------------------------------------------

    private static void HardWiredCapitalThreatMirrorsVanilla()
    {
        var enemies = new List<CapitalForceSample> { new CapitalForceSample { Distance = 100f, Strength = 20000f, Morale = 0.8f } };
        var own = new List<CapitalForceSample> { new CapitalForceSample { Distance = 50f, Strength = 30000f, Morale = 1f } };
        var none = new List<CapitalForceSample>();
        TestHarness.AssertTrue(CampaignResponsivenessSignals.EnemyThreatensCapital(enemies, none, 275f, 1500f), "uncovered enemy near capital threatens");
        TestHarness.AssertFalse(CampaignResponsivenessSignals.EnemyThreatensCapital(enemies, own, 275f, 1500f), "covered by stronger own forces closer than the enemy");
        var far = new List<CapitalForceSample> { new CapitalForceSample { Distance = 400f, Strength = 50000f, Morale = 1f } };
        TestHarness.AssertFalse(CampaignResponsivenessSignals.EnemyThreatensCapital(far, none, 275f, 1500f), "enemy outside vanilla relocation radius is no threat");
    }

    private static void HardWiredCapitalStreakAdvancesPerDay()
    {
        int day = Serial(1, 6, 1862);
        int streak = CampaignResponsivenessSignals.NextCapitalStreak(0, true, -1, day, out int last);
        TestHarness.AssertEqual(1, streak, "first threatened day");
        streak = CampaignResponsivenessSignals.NextCapitalStreak(streak, true, last, day, out last);
        TestHarness.AssertEqual(1, streak, "same day counted once");
        streak = CampaignResponsivenessSignals.NextCapitalStreak(streak, true, last, day + 3, out last);
        TestHarness.AssertEqual(4, streak, "skipped days count");
        streak = CampaignResponsivenessSignals.NextCapitalStreak(streak, false, last, day + 4, out last);
        TestHarness.AssertEqual(0, streak, "threat cleared");
    }

    private static void HardWiredDaysSinceFrontChange()
    {
        int day = Serial(1, 6, 1862);
        TestHarness.AssertEqual(0, CampaignResponsivenessSignals.DaysSince(-1, day), "never stamped");
        TestHarness.AssertEqual(61, CampaignResponsivenessSignals.DaysSince(day, day + 61), "61 days static");
    }

    // ------------------------------------------------------------------
    // Cadence
    // ------------------------------------------------------------------

    private static void CadenceSkippedWeeklyGateDetected()
    {
        TestHarness.AssertTrue(ReviewCadenceCatchUp.SkippedWeeklyGate(Cal(5, 6, 1862), Cal(9, 6, 1862)), "day 7 skipped by a 4-day jump");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.SkippedWeeklyGate(Cal(8, 6, 1862), Cal(13, 6, 1862)), "no multiple of 7 between 8 and 13");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.SkippedWeeklyGate(Cal(5, 6, 1862), Cal(7, 6, 1862)), "landing on day 7 is the normal gate");
    }

    private static void CadenceSkippedWeeklyGateAcrossMonth()
    {
        TestHarness.AssertTrue(ReviewCadenceCatchUp.SkippedWeeklyGate(Cal(27, 5, 1862), Cal(2, 6, 1862)), "day 28 of May skipped");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.SkippedWeeklyGate(Cal(29, 5, 1862), Cal(2, 6, 1862)), "29..1 has no weekly day");
    }

    private static void CadenceConsecutiveDaysSkipNothing()
    {
        TestHarness.AssertFalse(ReviewCadenceCatchUp.SkippedWeeklyGate(Cal(6, 6, 1862), Cal(7, 6, 1862)), "consecutive");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.SkippedAlternatingGate(Cal(6, 6, 1862), Cal(7, 6, 1862)), "consecutive");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.SkippedWeeklyGate(-1, Cal(9, 6, 1862)), "first review");
    }

    private static void CadenceSkippedAlternatingDayForcesBothAlliances()
    {
        TestHarness.AssertTrue(ReviewCadenceCatchUp.SkippedAlternatingGate(Cal(6, 6, 1862), Cal(8, 6, 1862)), "a whole day skipped");
    }

    // UniStorm's calendar (calendarType 1) rolls months over with Gregorian lengths and leap years
    // (CalculateMonths 157818-157847) and builds UniStormDate = new DateTime(year, month, day)
    // (CalculateDays 157882-157885): the serial is that date's day count.
    private static void CadenceDaySerialCountsCalendarDays()
    {
        TestHarness.AssertEqual(2, Cal(1, 3, 1862) - Cal(27, 2, 1862), "27 Feb -> 1 Mar 1862");
        TestHarness.AssertEqual(3, Cal(1, 3, 1864) - Cal(27, 2, 1864), "1864 is a leap year");
        TestHarness.AssertEqual(1, Cal(1, 1, 1863) - Cal(31, 12, 1862), "31 Dec -> 1 Jan");
        TestHarness.AssertEqual(1, Cal(1, 5, 1862) - Cal(30, 4, 1862), "a 30-day month");
        TestHarness.AssertTrue(Cal(1, 1, 1861) > 0, "positive: -1 stays the 'never' sentinel");
        var origin = new System.DateTime(1861, 1, 1);
        for (var d = origin; d.Year < 1867; d = d.AddDays(1))
            if (Cal(d.Day, d.Month, d.Year) - Cal(1, 1, 1861) != (d - origin).Days)
            {
                TestHarness.AssertEqual((d - origin).Days, Cal(d.Day, d.Month, d.Year) - Cal(1, 1, 1861), "day count at " + d.ToString("yyyy-MM-dd"));
                break;
            }
        TestHarness.AssertFalse(ReviewCadenceCatchUp.SkippedAlternatingGate(Cal(28, 2, 1862), Cal(1, 3, 1862)), "28 Feb -> 1 Mar is consecutive: no phantom skipped day");
        TestHarness.AssertTrue(ReviewCadenceCatchUp.SkippedWeeklyGate(Cal(27, 2, 1862), Cal(2, 3, 1862)), "28 Feb skipped");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.SkippedWeeklyGate(Cal(28, 2, 1862), Cal(3, 3, 1862)), "1-2 Mar has no weekly day");
        TestHarness.AssertTrue(ReviewCadenceCatchUp.SkippedWeeklyGate(Cal(6, 12, 1862), Cal(8, 12, 1862)), "day 7 by the real calendar");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.SkippedWeeklyGate(Cal(29, 12, 1862), Cal(2, 1, 1863)), "30 Dec .. 1 Jan has no weekly day");
    }

    private static void CadenceReviewOnlyWhenCampaignLive()
    {
        TestHarness.AssertTrue(ReviewCadenceCatchUp.CampaignIsLive(2), "currentoperation 2 = campaign running");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.CampaignIsLive(5), "returning from battle, save not applied yet");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.CampaignIsLive(6), "loading a save");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.CampaignIsLive(0), "new campaign before frame 30");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.CampaignIsLive(4), "launching a battle");
    }
}
