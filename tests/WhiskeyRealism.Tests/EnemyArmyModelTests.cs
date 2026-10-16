using System;
using System.Collections.Generic;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Util;

using static TestHarness;
using static TestFixtures;

// Stage G slice G1 (docs/superpowers/plans/2026-10-15-campaign-planner.md § G1): the fog-honest
// enemy picture. EnemyArmyModel (P6 decay and drift, identity by UnitKey), the detection decision
// (spec component 2 rules a-d, the viewer check C14, the W&L incident mask), the flags-only
// omniscient fallback, and the pure halves of every ledger migrated to beliefs (C25).
internal static class EnemyArmyModelTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("EuropeanPaintedCellWithoutViewerStaysUnseen", EuropeanPaintedCellWithoutViewerStaysUnseen),
            ("SeenEnemyGetsFullConfidence", SeenEnemyGetsFullConfidence),
            ("UnseenConfidenceHalvesEverySevenDays", UnseenConfidenceHalvesEverySevenDays),
            ("UnseenRadiusGrowsByDrift", UnseenRadiusGrowsByDrift),
            ("DriftIsCampaignSpeedUpperBound", DriftIsCampaignSpeedUpperBound),
            ("ExpiresAtThirtyDaysOrBelowMinConfidence", ExpiresAtThirtyDaysOrBelowMinConfidence),
            ("ReappearingEnemyRefreshes", ReappearingEnemyRefreshes),
            ("NoFogMarksEverythingSeen", NoFogMarksEverythingSeen),
            ("ContactAlwaysReveals", ContactAlwaysReveals),
            ("PlayerAllianceSideReadsIswithinfog", PlayerAllianceSideReadsIswithinfog),
            ("GarrisonBeliefOnlyWhenGarrisonSeen", GarrisonBeliefOnlyWhenGarrisonSeen),
            ("GarrisonBeliefDoesNotDrift", GarrisonBeliefDoesNotDrift),
            ("PlayerIntelIncidentsNeverRevealToAiSide", PlayerIntelIncidentsNeverRevealToAiSide),
            ("PlayerIntelIncidentsHaveOneOwner", PlayerIntelIncidentsHaveOneOwner),
            ("ModelUpdateIsIdempotentForSameDay", ModelUpdateIsIdempotentForSameDay),
            ("EmptyDetectionsKeepsMemory", EmptyDetectionsKeepsMemory),
            ("BeliefsKeyedByUnitKeyNotInstanceId", BeliefsKeyedByUnitKeyNotInstanceId),
            ("CommanderChangeReKeysBeliefNotDuplicates", CommanderChangeReKeysBeliefNotDuplicates),
            ("DuplicateUnitKeyNeverMergesTwoArmies", DuplicateUnitKeyNeverMergesTwoArmies),
            ("RenamedGroupRetiresOldBelief", RenamedGroupRetiresOldBelief),
            ("BeliefsSurviveReloadWithoutDoubleCount", BeliefsSurviveReloadWithoutDoubleCount),
            ("CapitalThreatNeverOmniscientWithFlagsOn", CapitalThreatNeverOmniscientWithFlagsOn),
            ("ContactWithinEitherBugleRange", ContactWithinEitherBugleRange),
            ("TraitsFromWhatASightingShows", TraitsFromWhatASightingShows),
            ("FrontSectorFromBeliefsExcludesUnseen", FrontSectorFromBeliefsExcludesUnseen),
            ("LocalPressureFromBeliefsExcludesUnseenEnemy", LocalPressureFromBeliefsExcludesUnseenEnemy),
            ("TelegraphSiteNotBlockedByUnseenEnemy", TelegraphSiteNotBlockedByUnseenEnemy),
            ("SeaInvasionThreatOnlyFromSeenEmbarkedForce", SeaInvasionThreatOnlyFromSeenEmbarkedForce),
            ("LandingInferredFromSeenForceOnOwnCoast", LandingInferredFromSeenForceOnOwnCoast),
            ("RaidThreatFromSeenCavalryOnOwnSoil", RaidThreatFromSeenCavalryOnOwnSoil),
            ("NoThreatFromUnseenInvasionForce", NoThreatFromUnseenInvasionForce),
            ("ExpiredSeaInvasionBeliefCollapsesThreat", ExpiredSeaInvasionBeliefCollapsesThreat),
            ("StrengthRatioFromBeliefs", StrengthRatioFromBeliefs),
            ("CapitalSamplesFromBeliefsCarryNoMorale", CapitalSamplesFromBeliefsCarryNoMorale),
            ("SameNameBeyondPlausibleMarchIsAnotherGroup", SameNameBeyondPlausibleMarchIsAnotherGroup),
            ("NewGroupFarAwayDoesNotRetireRememberedOne", NewGroupFarAwayDoesNotRetireRememberedOne),
            ("RoadMarchIsTheNormalDayBelowTheBound", RoadMarchIsTheNormalDayBelowTheBound),
            ("LandedInvasionNeedsRecentSighting", LandedInvasionNeedsRecentSighting),
            ("RaidLeavingSightRecoversThenReleases", RaidLeavingSightRecoversThenReleases),
            ("DetectionModeFailsClosed", DetectionModeFailsClosed),
            ("GridAnswerFailsClosed", GridAnswerFailsClosed),
            ("AlliedGroupsAreViewers", AlliedGroupsAreViewers),
            ("GarrisonGateRequiresSighting", GarrisonGateRequiresSighting),
            ("StandDownAllianceStillDetects", StandDownAllianceStillDetects),
            ("SameDayIdentityUsesOneDayOfMarch", SameDayIdentityUsesOneDayOfMarch),
            ("GarrisonIdentityHasNoMarchRadius", GarrisonIdentityHasNoMarchRadius),
            ("BeliefAgeCountsCalendarDays", BeliefAgeCountsCalendarDays),
            ("LapsedThreatLeavesAssetToProximity", LapsedThreatLeavesAssetToProximity),
            ("DetectionWiringPinned", DetectionWiringPinned),
            ("ReviewDayScalesNeverMix", ReviewDayScalesNeverMix),
            ("MigratedReadersReadBeliefsOnly", MigratedReadersReadBeliefsOnly),
            ("ReaderOutsideReviewSeesCommittedBeliefs", ReaderOutsideReviewSeesCommittedBeliefs),
            ("BeliefCommitAllocatesNothingOnceWarm", BeliefCommitAllocatesNothingOnceWarm),
        });
    }

    private const float NoDrift = 0f;

    // Integrator decision after G3 verify r2: a reader outside a review job (a patch between the
    // slices of a time-sliced review) sees the beliefs of the last committed day; inside a job,
    // the model; after the commit, today's beliefs.
    private static void ReaderOutsideReviewSeesCommittedBeliefs()
    {
        bool inJob = false;
        CampaignScope.ResetAll();
        DailyReviewView.Attach(() => inJob);
        try
        {
            var model = EnemyBeliefStore.ModelFor(0);
            inJob = true;
            model.Update(List(Seen("Army of Northern Virginia:7", 1, 0f, 0f, 60000f)), 3, Drift(1f), NoMarch);
            inJob = false;
            AssertEqual(0, EnemyBeliefStore.Select(0, true, true, null).Count, "before any commit: nothing published");
            EnemyBeliefStore.PreparePublish();
            EnemyBeliefStore.SwapPublished();
            AssertEqual(1, EnemyBeliefStore.Select(0, true, true, null).Count, "day 3 committed");

            // Day 4 mid-flight: the Plan job adds a second army.
            inJob = true;
            model.Update(List(Seen("Army of Northern Virginia:7", 1, 0f, 0f, 60000f), Seen("Army of Tennessee:9", 2, 500f, 0f, 40000f)), 4, Drift(1f), NoMarch);
            AssertEqual(2, EnemyBeliefStore.Select(0, true, true, null).Count, "inside a job: the model");
            inJob = false;
            AssertEqual(1, EnemyBeliefStore.Select(0, true, true, null).Count, "between slices: yesterday's beliefs");
            EnemyBeliefStore.PreparePublish();
            AssertEqual(1, EnemyBeliefStore.Select(0, true, true, null).Count, "prepared but not swapped: still yesterday");
            EnemyBeliefStore.SwapPublished();
            AssertEqual(2, EnemyBeliefStore.Select(0, true, true, null).Count, "after the commit: today's beliefs");
            EnemyBeliefStore.SwapPublished();
            AssertEqual(2, EnemyBeliefStore.Select(0, true, true, null).Count, "a swap without a prepare changes nothing");

            CampaignScope.ResetAll();
            AssertEqual(0, EnemyBeliefStore.Select(0, true, true, null).Count, "reset clears the published beliefs");
        }
        finally
        {
            DailyReviewView.Attach(null);
            CampaignScope.ResetAll();
        }
    }

    // The reader's path and the commit reuse their lists after warm-up.
    private static void BeliefCommitAllocatesNothingOnceWarm()
    {
        bool inJob = false;
        CampaignScope.ResetAll();
        Func<bool> view = () => inJob;
        DailyReviewView.Attach(view);
        try
        {
            var detections = new List<EnemyDetection>();
            for (int i = 0; i < 40; i++) detections.Add(Seen("army:" + i, i + 1, i * 50f, 0f, 10000f));
            Func<int, float> drift = Drift(1f);
            EnemyBeliefStore.ModelFor(0).Update(detections, 3, drift, NoMarch);
            Action commit = () =>
            {
                EnemyBeliefStore.PreparePublish();
                EnemyBeliefStore.SwapPublished();
                var seen = EnemyBeliefStore.Select(0, true, true, null);
                if (seen.Count != 40) throw new InvalidOperationException("count " + seen.Count);
            };
            for (int i = 0; i < 16; i++) commit();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 500; i++) commit();
            AssertEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before, "belief commit and published read bytes");
        }
        finally
        {
            DailyReviewView.Attach(null);
            CampaignScope.ResetAll();
        }
    }

    private static EnemyDetection Seen(string key, int instanceId, float x, float z, float strength,
                                       int unitTyp = 15, DetectionSource source = DetectionSource.FogGrid,
                                       EnemyTraits traits = default(EnemyTraits))
    {
        return new EnemyDetection
        {
            InstanceId = instanceId, UnitKey = key, UnitTyp = unitTyp, X = x, Z = z,
            Strength = strength, Source = source, Traits = traits
        };
    }

    private static List<EnemyDetection> List(params EnemyDetection[] d) => new List<EnemyDetection>(d);

    private static Func<int, float> Drift(float perDay) => _ => perDay;
    private static Func<int, float> March(float perDay) => _ => perDay;
    private static readonly Func<int, float> NoMarch = _ => 0f;

    private static EnemyBelief Only(EnemyArmyModel m, string label)
    {
        AssertEqual(1, m.Beliefs.Count, label + ": one belief");
        return m.Beliefs[0];
    }

    // ---- Detection decision (spec component 2, C14) ----

    // A European unit paints the shared fows[1] (116513-116516): the grid alone says "visible",
    // but no own or allied viewer has the enemy inside its own sight radius, so it stays unseen.
    private static void EuropeanPaintedCellWithoutViewerStaysUnseen()
    {
        float[] vx = { 0f }, vz = { 0f }, vr = { 50f };
        bool covers = ViewerCoverage.Covers(200f, 0f, vx, vz, vr, 1);
        AssertFalse(covers, "no own viewer within its radius");
        AssertFalse(DetectionRules.NeedsGridProbe(noFog: false, contact: false, sideIsPlayerAlliance: false, viewerCovers: covers),
            "no grid probe without a covering viewer");
        AssertFalse(DetectionRules.TryDecide(false, false, false, false, covers, gridVisible: true, out _),
            "grid-visible but uncovered stays unseen");

        bool near = ViewerCoverage.Covers(30f, 0f, vx, vz, vr, 1);
        AssertTrue(DetectionRules.NeedsGridProbe(false, false, false, near), "covered: ask the grid");
        AssertTrue(DetectionRules.TryDecide(false, false, false, false, near, gridVisible: true, out var source), "covered and grid-visible");
        AssertEqual(DetectionSource.FogGrid, source, "FogGrid");
        AssertFalse(DetectionRules.TryDecide(false, false, false, false, near, gridVisible: false, out _), "hidden by concealment");
    }

    private static void SeenEnemyGetsFullConfidence()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("Army of the Potomac:12", 101, 10f, 20f, 50000f)), 100, Drift(5f), NoMarch);
        var b = Only(m, "seen");
        AssertNear(1f, b.Confidence01, 1e-6f, "confidence 1");
        AssertNear(EnemyArmyModel.SeenRadius, b.UncertaintyRadius, 1e-6f, "seen radius");
        AssertEqual(100, b.LastSeenDay, "last seen today");
        AssertNear(10f, b.X, 1e-6f, "x");
        AssertNear(50000f, b.Strength, 1e-6f, "strength");
        AssertEqual(DetectionSource.FogGrid, b.LastSource, "source");
    }

    private static void UnseenConfidenceHalvesEverySevenDays()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("A:1", 1, 0f, 0f, 1000f)), 10, Drift(1f), NoMarch);
        m.Update(List(), 17, Drift(1f), NoMarch);
        AssertNear(0.5f, Only(m, "7d").Confidence01, 1e-4f, "half after 7 days");
        m.Update(List(), 24, Drift(1f), NoMarch);
        AssertNear(0.25f, Only(m, "14d").Confidence01, 1e-4f, "quarter after 14 days");
        m.Update(List(), 13, Drift(1f), NoMarch);
        AssertNear((float)Math.Pow(0.5, 3.0 / 7.0), Only(m, "3d").Confidence01, 1e-4f, "absolute from last seen, not compounded");
    }

    private static void UnseenRadiusGrowsByDrift()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("A:1", 1, 0f, 0f, 1000f, unitTyp: 16)), 10, t => t == 16 ? 4f : 99f, NoMarch);
        m.Update(List(), 15, t => t == 16 ? 4f : 99f, NoMarch);
        AssertNear(EnemyArmyModel.SeenRadius + 20f, Only(m, "5d").UncertaintyRadius, 1e-4f, "radius = seen + drift(unittyp) x days");
        AssertNear(0f, Only(m, "pos").X, 1e-6f, "the last-seen position does not move");
    }

    // P6 (corrected at the G0 landing, extended in G1): the bound is the product of every term's
    // maximum (RegimentMove 133340-133545, 134013, 134050-134100). Each factor moves it; a road
    // march without perks is strictly below it.
    private static void DriftIsCampaignSpeedUpperBound()
    {
        CampaignSpeedPrefs p = SpeedFixture();
        float bound = CampaignSpeedBound.DriftPerDay(in p, 15);
        float expected = 2f * 2.5f * 1.2f * 1.2f * 1.25f * 1.25f * 1.5f * 1.3f * 1.3f * Math.Max(2f * 2f, 1.6f)
                         * 0.05f * (1f / 1f) * 30f * 2f * Math.Max(Math.Max(1f, 1.5f), 1.2f * 1.75f) * Math.Max(1f, 1.5f) / 365.25f;
        AssertNear(expected, bound, expected * 1e-4f, "product of every term's maximum");

        void Moves(string label, SpeedChange change)
        {
            var q = SpeedFixture();
            change(ref q);
            AssertTrue(CampaignSpeedBound.DriftPerDay(in q, 15) != bound, label + " moves the bound");
        }
        Moves("unit basic speed", (ref CampaignSpeedPrefs q) => q.UnitBasicSpeedMounted[15] = 3f);
        Moves("group speed ceiling", (ref CampaignSpeedPrefs q) => q.GroupSpeedCeiling = 3f);
        Moves("cartographers maximum", (ref CampaignSpeedPrefs q) => q.SkilledCartographers[2] = 1.4f);
        Moves("flying column maximum", (ref CampaignSpeedPrefs q) => q.FlyingColumn[2] = 1.4f);
        Moves("forced march", (ref CampaignSpeedPrefs q) => q.Running[15] = 1.9f);
        Moves("foot cavalry maximum", (ref CampaignSpeedPrefs q) => q.FootCavalry[2] = 1.5f);
        Moves("river expedition", (ref CampaignSpeedPrefs q) => q.RiverExpedition[2] = 2.5f);
        Moves("basic unit speed", (ref CampaignSpeedPrefs q) => q.BasicUnitSpeed = 0.06f);
        Moves("terrain cost 1", (ref CampaignSpeedPrefs q) => q.TerrainSpecCost[3] = 0.5f);
        Moves("general campaign factor", (ref CampaignSpeedPrefs q) => q.GeneralCampaignFactor = 20f);
        Moves("retreat", (ref CampaignSpeedPrefs q) => q.SpeedFactorRetreat = 2f);
        Moves("fallback cheat", (ref CampaignSpeedPrefs q) => q.FallbackCheat = 3f);

        var rail = SpeedFixture();
        rail.RailroadSpeedAdj = 5f;
        AssertNear(bound / 4f * 5f, CampaignSpeedBound.DriftPerDay(in rail, 15), bound * 1e-4f, "rail replaces river when faster (they exclude each other)");

        var same = SpeedFixture();
        AssertNear(bound, CampaignSpeedBound.DriftPerDay(in same, 15), bound * 1e-6f, "deterministic");

        // A road march with no perks, no forced march, no retreat: unitbasic x groupspeed x basic /
        // cost[7] x general / 365.25 - strictly below the bound.
        float road = 2f * 2.5f * 0.05f / 2f * 30f / 365.25f;
        AssertTrue(road < bound, "road march below the bound");
        AssertTrue(CampaignSpeedBound.DriftPerDay(in p, -1) == 0f && CampaignSpeedBound.DriftPerDay(in p, 99) == 0f, "unknown unittyp drifts 0");
    }

    private delegate void SpeedChange(ref CampaignSpeedPrefs p);

    private static CampaignSpeedPrefs SpeedFixture()
    {
        var mounted = new float[19];
        mounted[15] = 2f;
        mounted[16] = 1.8f;
        var dq = new float[19];
        var run = new float[19];
        dq[15] = 1.3f;
        run[15] = 1.5f;
        return new CampaignSpeedPrefs
        {
            UnitBasicSpeedMounted = mounted,
            GroupSpeedCeiling = 2.5f,
            SkilledCartographers = new[] { 1f, 1.1f, 1.2f },
            FlyingColumn = new[] { 1f, 1.15f, 1.25f },
            DoubleQuick = dq,
            Running = run,
            FootCavalry = new[] { 1f, 1.2f, 1.3f },
            RiverExpedition = new[] { 1f, 1.5f, 2f },
            RailroadSpeedAdj = 1.6f,
            BasicUnitSpeed = 0.05f,
            TerrainSpecCost = new[] { 4f, 3f, 3f, 1f, 1f, 1f, 2f, 2f, 1f, 1f, 1f },
            GeneralCampaignFactor = 30f,
            SpeedFactorRetreat = 1.5f,
            SpeedFactorFallback = 1.2f,
            FallbackCheat = 1.75f
        };
    }

    private static void ExpiresAtThirtyDaysOrBelowMinConfidence()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("A:1", 1, 0f, 0f, 1000f)), 0, Drift(1f), NoMarch);
        m.Update(List(), EnemyArmyModel.ExpireDays - 1, Drift(1f), NoMarch);
        AssertEqual(1, m.Beliefs.Count, "alive at 29 days");
        AssertTrue(m.Beliefs[0].Confidence01 >= EnemyArmyModel.MinConfidence, "above the floor at 29 days");
        AssertEqual(0, m.ExpiredKeys.Count, "nothing expired yet");
        m.Update(List(), EnemyArmyModel.ExpireDays, Drift(1f), NoMarch);
        AssertEqual(0, m.Beliefs.Count, "expired at 30 days");
        AssertEqual("A:1", m.ExpiredKeys.Count == 1 ? m.ExpiredKeys[0] : null, "expired key reported");
        AssertTrue(EnemyArmyModel.Confidence(31) < EnemyArmyModel.MinConfidence, "below the floor after 30 days anyway");
        m.Update(List(), EnemyArmyModel.ExpireDays + 1, Drift(1f), NoMarch);
        AssertEqual(0, m.ExpiredKeys.Count, "expired keys are per update");
    }

    private static void ReappearingEnemyRefreshes()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("A:1", 1, 0f, 0f, 1000f)), 0, Drift(2f), NoMarch);
        m.Update(List(), 10, Drift(2f), NoMarch);
        m.Update(List(Seen("A:1", 1, 50f, 60f, 900f)), 12, Drift(2f), NoMarch);
        var b = Only(m, "refreshed");
        AssertNear(1f, b.Confidence01, 1e-6f, "full confidence again");
        AssertNear(50f, b.X, 1e-6f, "new position");
        AssertNear(900f, b.Strength, 1e-6f, "new strength");
        AssertNear(EnemyArmyModel.SeenRadius, b.UncertaintyRadius, 1e-6f, "radius reset");
        AssertEqual(12, b.LastSeenDay, "last seen");
    }

    private static void NoFogMarksEverythingSeen()
    {
        AssertTrue(DetectionRules.TryDecide(noFog: true, contact: false, sideIsPlayerAlliance: false, ownSideVisible: false,
            viewerCovers: false, gridVisible: false, out var source), "fog off: everything seen");
        AssertEqual(DetectionSource.NoFog, source, "NoFog");
        AssertFalse(DetectionRules.NeedsGridProbe(true, false, false, true), "no grid probe with fog off (usefow NRE, 101188)");
        AssertTrue(DetectionRules.TryDecide(true, true, true, false, false, false, out source), "fog off wins over the other rules");
        AssertEqual(DetectionSource.NoFog, source, "NoFog even in contact");
    }

    private static void ContactAlwaysReveals()
    {
        AssertTrue(DetectionRules.TryDecide(false, contact: true, sideIsPlayerAlliance: false, ownSideVisible: false,
            viewerCovers: false, gridVisible: false, out var source), "contact reveals without a viewer");
        AssertEqual(DetectionSource.Contact, source, "Contact");
        AssertFalse(DetectionRules.NeedsGridProbe(false, true, false, true), "no grid probe needed in contact");
    }

    private static void PlayerAllianceSideReadsIswithinfog()
    {
        AssertTrue(DetectionRules.TryDecide(false, false, sideIsPlayerAlliance: true, ownSideVisible: true,
            viewerCovers: false, gridVisible: false, out var source), "W&L player's AI side: !iswithinfog");
        AssertEqual(DetectionSource.OwnSideWithinFog, source, "OwnSideWithinFog");
        AssertFalse(DetectionRules.TryDecide(false, false, true, ownSideVisible: false, viewerCovers: true, gridVisible: true, out _),
            "the player-alliance side never asks fows[1]");
        AssertFalse(DetectionRules.NeedsGridProbe(false, false, true, true), "no grid probe for the player-alliance side");
    }

    // G0 landing F2: an enemy fort garrison is known only once seen or contacted.
    private static void GarrisonBeliefOnlyWhenGarrisonSeen()
    {
        var m = new EnemyArmyModel();
        var seen = new List<EnemyDetection>();
        if (DetectionRules.TryDecide(false, false, false, false, viewerCovers: false, gridVisible: false, out _))
            seen.Add(DetectionRules.ObjectiveGarrison(7, "Fort Pulaski garrison:300", 14, 100f, 200f, 1200f));
        m.Update(seen, 5, Drift(3f), NoMarch);
        AssertEqual(0, m.Beliefs.Count, "unseen garrison: no belief");

        seen.Clear();
        if (DetectionRules.TryDecide(false, false, false, false, viewerCovers: true, gridVisible: true, out _))
            seen.Add(DetectionRules.ObjectiveGarrison(7, "Fort Pulaski garrison:300", 14, 100f, 200f, 1200f));
        m.Update(seen, 6, Drift(3f), NoMarch);
        var b = Only(m, "seen garrison");
        AssertNear(1200f, b.Strength, 1e-6f, "garrison strength");
        AssertNear(100f, b.X, 1e-6f, "at the town");
        AssertEqual(DetectionSource.ObjectiveGarrison, b.LastSource, "ObjectiveGarrison source");
    }

    private static void GarrisonBeliefDoesNotDrift()
    {
        var m = new EnemyArmyModel();
        m.Update(List(DetectionRules.ObjectiveGarrison(7, "G:1", 14, 0f, 0f, 500f)), 0, Drift(9f), NoMarch);
        m.Update(List(), 6, Drift(9f), NoMarch);
        AssertNear(EnemyArmyModel.SeenRadius, Only(m, "garrison").UncertaintyRadius, 1e-6f, "a fort does not move");
        AssertTrue(Only(m, "garrison").Confidence01 < 1f, "but its picture still ages");
    }

    private sealed class FakeIncident
    {
        public float LaunchedTime;
        public bool Gone;
    }

    // The battle perception and the campaign detection mask the same three incidents by the same rule; the ids, the "not launched"
    // value, the launched test and the mask rule live in Util/PlayerIntelIncidents.cs and nowhere else (G1 verify M4, deduped at G7).
    private static void PlayerIntelIncidentsHaveOneOwner()
    {
        string root = System.IO.Path.GetFullPath(FindRepoPath("src/WhiskeyRealism"));
        foreach (string file in System.IO.Directory.GetFiles(root, "*.cs", System.IO.SearchOption.AllDirectories))
        {
            string name = System.IO.Path.GetFileName(file);
            if (name == "PlayerIntelIncidents.cs") continue;
            string code = string.Join("\n", PlannerReadCensusTests.CodeLines(System.IO.File.ReadAllText(file)));
            AssertFalse(System.Text.RegularExpressions.Regex.IsMatch(code, @"\{\s*60\s*,\s*61\s*,\s*64\s*\}"), name + " keeps its own copy of the incident ids");
            AssertFalse(code.Contains("IncidentNotLaunched") || code.Contains("IncidentLaunched("), name + " keeps its own launched test");
        }
        string battle = ReadRepoFile("src/WhiskeyRealism/Tactical/Battle/BattlePerception.cs");
        AssertContains(battle, "return PlayerIntelIncidents.Masks(mode == PerceptionMode.FogGrid, dlcScenarioActive);", "the battle rule is the shared one with the FogGrid mapping");
        AssertContains(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs"), "PlayerIntelIncidents.Masks(mode == DetectionMode.FogGrid, DLC_WL.dlc_scenarioactive)", "so is the campaign's");
        AssertContains(ReadRepoFile("src/WhiskeyRealism/Tactical/Battle/BattlePerceptionProbe.cs"), "PlayerIntelIncidents.Launched(incident.launchedtime)", "the battle probe's mask tests the shared predicate");
    }

    // 101183-101186: incidents 60/61/64 make UnitIsHidden say "visible" everywhere. The AI side's
    // fog-grid probe runs with them masked (not launched) and restores them, also when it throws.
    private static void PlayerIntelIncidentsNeverRevealToAiSide()
    {
        AssertEqual("60,61,64", string.Join(",", PlayerIntelIncidents.Ids), "Lost Orders and Scouting Ahead (101183)");
        AssertTrue(PlayerIntelIncidents.Masks(asksAnAiSidesGrid: true, dlcScenarioActive: true), "rule (b) masks");
        AssertFalse(PlayerIntelIncidents.Masks(asksAnAiSidesGrid: false, dlcScenarioActive: true), "rule (c) keeps the incident: the player's own side");
        AssertFalse(PlayerIntelIncidents.Masks(true, false), "no W&L scenario");

        var lostOrders = new FakeIncident { LaunchedTime = 3.5f };
        var idle = new FakeIncident { LaunchedTime = PlayerIntelIncidents.NotLaunched };
        var incidents = new List<FakeIncident> { lostOrders, idle, null };
        var mask = new FieldMask<FakeIncident, float>(i => i.LaunchedTime, (i, v) => i.LaunchedTime = v, PlayerIntelIncidents.NotLaunched);

        // Vanilla's shortcut: any launched incident -> nothing is hidden. Else the grid hides the enemy.
        bool visible = true;
        DetectionRules.RunMasked(mask, incidents, apply: true, i => i.LaunchedTime, i => i == null || i.Gone,
            () => { visible = AnyLaunched(incidents) || false; }, out string failure);
        AssertFalse(visible, "masked incident: the unseen enemy stays hidden");
        AssertEqual(null, failure, "clean restore");
        AssertNear(3.5f, lostOrders.LaunchedTime, 1e-6f, "incident restored");
        AssertFalse(mask.Active, "mask released");

        bool threw = false;
        try
        {
            DetectionRules.RunMasked(mask, incidents, apply: true, i => i.LaunchedTime, i => i == null || i.Gone,
                () => throw new InvalidOperationException("probe"), out _);
        }
        catch (InvalidOperationException) { threw = true; }
        AssertTrue(threw, "the probe's exception propagates");
        AssertNear(3.5f, lostOrders.LaunchedTime, 1e-6f, "restored when the probe throws");

        DetectionRules.RunMasked(mask, incidents, apply: false, i => i.LaunchedTime, i => i == null || i.Gone,
            () => { visible = AnyLaunched(incidents); }, out _);
        AssertTrue(visible, "rule (c) side: unmasked, vanilla's own verdict stands");
    }

    private static bool AnyLaunched(List<FakeIncident> incidents)
    {
        foreach (var i in incidents)
            if (i != null && PlayerIntelIncidents.Launched(i.LaunchedTime)) return true;
        return false;
    }

    private static void ModelUpdateIsIdempotentForSameDay()
    {
        var m = new EnemyArmyModel();
        var seen = List(Seen("A:1", 1, 0f, 0f, 1000f), Seen("B:2", 2, 40f, 0f, 2000f));
        m.Update(seen, 0, Drift(2f), NoMarch);
        m.Update(List(Seen("A:1", 1, 5f, 0f, 1000f)), 4, Drift(2f), NoMarch);
        string first = Dump(m);
        m.Update(List(Seen("A:1", 1, 5f, 0f, 1000f)), 4, Drift(2f), NoMarch);
        AssertEqual(first, Dump(m), "same day, same detections: same state");
        AssertEqual(2, m.Beliefs.Count, "no duplicate");
    }

    private static string Dump(EnemyArmyModel m)
    {
        var parts = new List<string>();
        foreach (var b in m.Beliefs)
            parts.Add(b.UnitKey + "|" + b.X + "|" + b.Confidence01.ToString("R") + "|" + b.UncertaintyRadius.ToString("R") + "|" + b.LastSeenDay);
        parts.Sort(StringComparer.Ordinal);
        return string.Join(";", parts);
    }

    private static void EmptyDetectionsKeepsMemory()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("A:1", 1, 0f, 0f, 1000f), Seen("B:2", 2, 9f, 9f, 500f)), 0, Drift(1f), NoMarch);
        m.Update(List(), 3, Drift(1f), NoMarch);
        AssertEqual(2, m.Beliefs.Count, "remembered");
        foreach (var b in m.Beliefs) AssertTrue(b.Confidence01 < 1f && b.Confidence01 > 0.5f, "decayed, not dropped");
    }

    private static void BeliefsKeyedByUnitKeyNotInstanceId()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("Army of Northern Virginia:7", 111, 0f, 0f, 60000f)), 0, Drift(1f), NoMarch);
        m.Update(List(Seen("Army of Northern Virginia:7", 999, 3f, 0f, 61000f)), 1, Drift(1f), NoMarch);
        var b = Only(m, "one belief across instance ids");
        AssertEqual(999, b.InstanceId, "instance id rebound to this session's");
        AssertNear(61000f, b.Strength, 1e-6f, "refreshed");
    }

    // Re-key (commander changed): same unit name and type within the plausibility radius (road
    // march 30/day x 2 days = 60). It continues the belief (LastEmbarkedDay carries over), which a
    // rename-retire would not.
    private static void CommanderChangeReKeysBeliefNotDuplicates()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("Army of the Potomac:12", 5, 0f, 0f, 80000f, unitTyp: 16, traits: EnemyTraits.Embarked)), 0, Drift(500f), March(30f));
        m.Update(List(Seen("Army of the Potomac:31", 5, 50f, 0f, 80000f, unitTyp: 16)), 2, Drift(500f), March(30f));
        var b = Only(m, "re-keyed");
        AssertEqual("Army of the Potomac:31", b.UnitKey, "new commander key");
        AssertEqual(0, b.LastEmbarkedDay, "the same belief continues (not retired and recreated)");

        // Same name, different unit type: a different group, not a re-key.
        m.Update(List(Seen("Army of the Potomac:31", 5, 50f, 0f, 80000f, unitTyp: 16), Seen("Army of the Potomac:44", 6, 900f, 0f, 3000f, unitTyp: 14)), 3, Drift(500f), March(30f));
        AssertEqual(2, m.Beliefs.Count, "a matched belief is never re-keyed away");
    }

    // F2: a group with the same display name far beyond a normal march (inside P6's loose radius)
    // is another group: both beliefs are kept.
    private static void SameNameBeyondPlausibleMarchIsAnotherGroup()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("1st Corps:12", 5, 0f, 0f, 20000f, unitTyp: 15)), 0, Drift(500f), March(30f));
        m.Update(List(Seen("1st Corps:31", 9, 400f, 0f, 15000f, unitTyp: 15)), 2, Drift(500f), March(30f));
        AssertEqual(2, m.Beliefs.Count, "400 units in 2 days is no road march (60): not a re-key");
    }

    private static void DuplicateUnitKeyNeverMergesTwoArmies()
    {
        OnceLog.Reset();
        int before = OnceLog.FiredCount;
        var m = new EnemyArmyModel();
        m.Update(List(Seen("1st Corps:4", 10, 0f, 0f, 9000f), Seen("1st Corps:4", 11, 300f, 0f, 8000f)), 0, Drift(1f), NoMarch);
        AssertEqual(2, m.Beliefs.Count, "two armies, two beliefs");
        AssertNear(17000f, EnemyBeliefQuery.StrengthWithin(m.Beliefs, 150f, 0f, 1000f), 1e-3f, "both counted");
        AssertEqual(before + 1, OnceLog.FiredCount, "one warning");
        var keys = new List<string> { m.Beliefs[0].UnitKey, m.Beliefs[1].UnitKey };
        keys.Sort(StringComparer.Ordinal);
        AssertEqual("1st Corps:4#10|1st Corps:4#11", string.Join("|", keys), "composite keys");

        m.Update(List(Seen("1st Corps:4", 10, 5f, 0f, 9000f), Seen("1st Corps:4", 11, 305f, 0f, 8000f)), 1, Drift(1f), NoMarch);
        AssertEqual(2, m.Beliefs.Count, "stable across reviews");
        AssertEqual(before + 1, OnceLog.FiredCount, "the warning stays once");
    }

    private static void RenamedGroupRetiresOldBelief()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("Old Name:3", 1, 0f, 0f, 9000f, unitTyp: 15)), 0, Drift(500f), March(10f));
        // Two days unseen: plausibility 2 x 10 = 20. A renamed corps appears 12 units away.
        m.Update(List(Seen("Grand Corps:3", 2, 12f, 0f, 9500f, unitTyp: 15)), 2, Drift(500f), March(10f));
        var b = Only(m, "renamed: one belief");
        AssertEqual("Grand Corps:3", b.UnitKey, "the detection is the fresh belief");

        // A new key outside every unmatched belief's plausibility radius is simply a new army.
        m.Update(List(Seen("Grand Corps:3", 2, 12f, 0f, 9500f, unitTyp: 15), Seen("Far Corps:8", 3, 900f, 0f, 4000f, unitTyp: 15)), 3, Drift(500f), March(10f));
        AssertEqual(2, m.Beliefs.Count, "two seen armies are never merged");
    }

    // F2: the first sighting of a new group far from a remembered one (inside the P6 uncertainty,
    // outside a normal march) does not retire it; a group of another unit type never does.
    private static void NewGroupFarAwayDoesNotRetireRememberedOne()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("Corps A:3", 1, 0f, 0f, 9000f, unitTyp: 15)), 0, Drift(500f), March(10f));
        m.Update(List(Seen("Corps B:4", 2, 150f, 0f, 7000f, unitTyp: 15)), 2, Drift(500f), March(10f));
        AssertEqual(2, m.Beliefs.Count, "150 units in 2 days: inside the P6 radius (1000), outside a road march (20)");
        AssertNear(1000f, m.Beliefs[0].UnitKey == "Corps A:3" ? m.Beliefs[0].UncertaintyRadius : m.Beliefs[1].UncertaintyRadius, 1e-3f,
            "the P6 radius stays the planning uncertainty");

        var n = new EnemyArmyModel();
        n.Update(List(Seen("Old Division:3", 1, 0f, 0f, 4000f, unitTyp: 14)), 0, Drift(500f), March(10f));
        n.Update(List(Seen("New Corps:5", 2, 5f, 0f, 9000f, unitTyp: 15)), 2, Drift(500f), March(10f));
        AssertEqual(2, n.Beliefs.Count, "a new group of another unit type never retires a belief");
    }

    private static void RoadMarchIsTheNormalDayBelowTheBound()
    {
        CampaignSpeedPrefs p = SpeedFixture();
        float road = 2f * 2.5f * 0.05f / 2f * 30f / 365.25f;
        AssertNear(road, CampaignSpeedBound.RoadMarchPerDay(in p, 15), road * 1e-4f, "unitbasic x groupspeed x basic / road cost x general / 365.25");
        AssertTrue(CampaignSpeedBound.RoadMarchPerDay(in p, 15) < CampaignSpeedBound.DriftPerDay(in p, 15), "below P6's loose bound");
        p.TerrainSpecCost[CampaignSpeedBound.RoadTerrain] = 4f;
        AssertNear(road / 2f, CampaignSpeedBound.RoadMarchPerDay(in p, 15), road * 1e-4f, "reads the road cost (index 7)");
        AssertEqual(0f, CampaignSpeedBound.RoadMarchPerDay(in p, 99), "unknown unittyp");
        AssertEqual(7, EnemyArmyModel.PlausibleDaysCap, "a week of march at most");

        var m = new EnemyArmyModel();
        m.Update(List(Seen("Corps A:3", 1, 0f, 0f, 9000f, unitTyp: 15)), 0, Drift(500f), March(10f));
        m.Update(List(Seen("Corps B:4", 2, 75f, 0f, 7000f, unitTyp: 15)), 20, Drift(500f), March(10f));
        AssertEqual(2, m.Beliefs.Count, "20 days unseen: plausibility capped at 7 days (70), so 75 away is another group");
    }

    private static void BeliefsSurviveReloadWithoutDoubleCount()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("Army of Tennessee:9", 77, 10f, 10f, 40000f, unitTyp: 16)), 20, Drift(1f), NoMarch);
        var saved = new List<EnemyBelief>(m.Beliefs);
        float before = EnemyBeliefQuery.StrengthWithin(m.Beliefs, 10f, 10f, 100f);

        var reloaded = new EnemyArmyModel();
        var dto = saved[0];
        dto.InstanceId = 0; // the sidecar stores no instance id
        reloaded.Restore(new[] { dto });
        reloaded.Update(List(Seen("Army of Tennessee:9", 4242, 10f, 10f, 40000f, unitTyp: 16)), 20, Drift(1f), NoMarch);
        AssertEqual(1, reloaded.Beliefs.Count, "one belief after reload");
        AssertEqual(4242, reloaded.Beliefs[0].InstanceId, "rebound");
        AssertNear(before, EnemyBeliefQuery.StrengthWithin(reloaded.Beliefs, 10f, 10f, 100f), 1e-3f, "StrengthWithin unchanged");
    }

    // Plan G1 fallback: decided by the two flags only. With both on the readers get the model as
    // the last review left it (empty before the first review), never vanilla's omniscient list.
    private static void CapitalThreatNeverOmniscientWithFlagsOn()
    {
        CampaignScope.ResetAll();
        int omniscientCalls = 0;
        var omniscientList = new List<EnemyBelief> { new EnemyBelief { UnitKey = "everyone:1", Strength = 1e6f, Confidence01 = 1f } };
        Func<int, IReadOnlyList<EnemyBelief>> omniscient = a => { omniscientCalls++; return omniscientList; };

        var before = EnemyBeliefStore.Select(1, plannerOn: true, fogHonestOn: true, omniscient);
        AssertEqual(0, before.Count, "before the first review: empty model, not omniscient");
        EnemyBeliefStore.ModelFor(1).Update(List(Seen("A:1", 1, 0f, 0f, 5000f)), 3, Drift(1f), NoMarch);
        var after = EnemyBeliefStore.Select(1, true, true, omniscient);
        AssertEqual(1, after.Count, "after the review: the model");
        AssertEqual(0, omniscientCalls, "omniscient branch never taken with both flags on");

        AssertEqual(1, EnemyBeliefStore.Select(1, plannerOn: false, fogHonestOn: true, omniscient).Count, "planner off: rollback list");
        AssertEqual(1, EnemyBeliefStore.Select(1, plannerOn: true, fogHonestOn: false, omniscient).Count, "beliefs off: rollback list");
        AssertEqual(2, omniscientCalls, "rollback only with a flag off");
        AssertEqual(0, EnemyBeliefStore.Select(2, true, true, omniscient).Count, "alliances 2-4: nothing");
        AssertEqual(null, EnemyBeliefStore.ModelFor(2), "no model for Europe");

        CampaignScope.ResetAll();
        AssertEqual(0, EnemyBeliefStore.Select(1, true, true, omniscient).Count, "campaign reset clears the model");
    }

    private static void ContactWithinEitherBugleRange()
    {
        float[] ox = { 0f }, oz = { 0f }, ob = { 10f };
        AssertTrue(DetectionRules.InBugleContact(8f, 0f, 1f, ox, oz, ob, 1), "inside the own formation's bugle range");
        AssertTrue(DetectionRules.InBugleContact(14f, 0f, 15f, ox, oz, ob, 1), "inside the enemy's bugle range (CheckEnemyContactCampaign 115634-115637)");
        AssertFalse(DetectionRules.InBugleContact(30f, 0f, 15f, ox, oz, ob, 1), "out of both");
        AssertFalse(DetectionRules.InBugleContact(0f, 0f, 15f, ox, oz, ob, 0), "no own formation");
    }

    private static void TraitsFromWhatASightingShows()
    {
        var t = DetectionRules.Traits(embarked: true, cavalryShare: 0.6f, onOwnSoil: true, nearOwnCoast: true);
        AssertEqual(EnemyTraits.Embarked | EnemyTraits.CavalryMajority | EnemyTraits.OnOwnSoil | EnemyTraits.NearOwnCoast, t, "all traits");
        AssertEqual(default(EnemyTraits), DetectionRules.Traits(false, 0.5f, false, false), "half cavalry is no majority");
    }

    // ---- Migrated readers (C25) ----

    private static EnemyArmyModel SeenAndUnseen(out float seenStrength)
    {
        // The planner side saw one army; a second one marched past outside every viewer and was
        // never detected, so it is not in the model at all.
        var m = new EnemyArmyModel();
        seenStrength = 30000f;
        m.Update(List(Seen("Seen Army:1", 1, 10f, 0f, seenStrength, unitTyp: 16)), 0, Drift(1f), NoMarch);
        return m;
    }

    private static void FrontSectorFromBeliefsExcludesUnseen()
    {
        var m = SeenAndUnseen(out float seen);
        var byTheater = new Dictionary<Theater, float>();
        EnemyBeliefReadings.SumByKey(m.Beliefs, (x, z) => x < 100f ? Theater.East : Theater.West, byTheater);
        byTheater.TryGetValue(Theater.East, out float east);
        AssertNear(seen, east, 1e-3f, "seen army only");

        var ledger = FrontSectorLedger.Build(new List<FrontSectorInput>
        {
            new FrontSectorInput { SectorKey = "East", Theater = Theater.East, OwnStrength = 20000f, EnemyStrength = east, StrategicImportance = 0.9f },
        }, new FrontLedgerOptions { MinimumHoldRatio = 0.9f, ConcessionRatio = 0.55f, CriticalHoldRatioBonus = 0.2f });
        AssertNear(seen, ledger.GetSector("East").EnemyStrength, 1e-3f, "the front ledger sees only the believed army");

        m.Update(List(), 7, Drift(1f), NoMarch);
        byTheater.Clear();
        EnemyBeliefReadings.SumByKey(m.Beliefs, (x, z) => Theater.East, byTheater);
        AssertNear(seen * 0.5f, byTheater[Theater.East], 1e-1f, "confidence-weighted once unseen");
    }

    private static void LocalPressureFromBeliefsExcludesUnseenEnemy()
    {
        var m = SeenAndUnseen(out float seen);
        EnemyBeliefReadings.LocalPressure(m.Beliefs, 0f, 0f, 50f, out float strength, out FormationLevel level);
        AssertNear(seen, strength, 1e-3f, "believed army within range");
        AssertEqual(FormationLevel.Army, level, "visible enemy level from the belief's unit type");
        EnemyBeliefReadings.LocalPressure(m.Beliefs, 500f, 0f, 50f, out strength, out level);
        AssertNear(0f, strength, 1e-6f, "nothing believed there (the unseen army is invisible)");
        AssertEqual(FormationLevel.Unknown, level, "no visible enemy");
    }

    private static void TelegraphSiteNotBlockedByUnseenEnemy()
    {
        var m = SeenAndUnseen(out _);
        AssertFalse(EnemyBeliefReadings.AnyWithin(m.Beliefs, 500f, 0f, 20f), "an unseen enemy at the site blocks nothing");
        AssertTrue(EnemyBeliefReadings.AnyWithin(m.Beliefs, 15f, 0f, 20f), "a believed enemy within bugle range blocks");
        AssertFalse(EnemyBeliefReadings.AnyWithin(m.Beliefs, 15f, 0f, 0f), "zero range");
    }

    private static void SeaInvasionThreatOnlyFromSeenEmbarkedForce()
    {
        var afloat = new EnemyBelief { UnitKey = "Burnside Expedition:9", Strength = 12000f, Confidence01 = 1f, LastSeenDay = 10, LastEmbarkedDay = 10,
            Traits = EnemyTraits.Embarked | EnemyTraits.NearOwnCoast };
        AssertTrue(EnemyBeliefReadings.IsSeaInvasionThreat(in afloat, 10, out bool landed), "embarked off our coast");
        AssertFalse(landed, "still afloat: invasion watch");

        var farOut = afloat;
        farOut.Traits = EnemyTraits.Embarked;
        AssertFalse(EnemyBeliefReadings.IsSeaInvasionThreat(in farOut, 10, out _), "afloat far from our coast");

        var ashoreAtHome = new EnemyBelief { Traits = EnemyTraits.OnOwnSoil, LastSeenDay = 10, LastEmbarkedDay = -1, Confidence01 = 1f, Strength = 5000f };
        AssertFalse(EnemyBeliefReadings.IsSeaInvasionThreat(in ashoreAtHome, 10, out _), "an army that never embarked is no invasion");
    }

    private static void LandingInferredFromSeenForceOnOwnCoast()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("Expedition:9", 1, 0f, 0f, 12000f, traits: EnemyTraits.Embarked | EnemyTraits.NearOwnCoast)), 10, Drift(1f), NoMarch);
        m.Update(List(Seen("Expedition:9", 1, 2f, 0f, 12000f, traits: EnemyTraits.OnOwnSoil | EnemyTraits.NearOwnCoast)), 12, Drift(1f), NoMarch);
        var b = Only(m, "landed");
        AssertEqual(10, b.LastEmbarkedDay, "the day it was last seen afloat");
        AssertTrue(EnemyBeliefReadings.IsSeaInvasionThreat(in b, 12, out bool landed) && landed, "landed within 3 days of being seen afloat");

        m.Update(List(Seen("Expedition:9", 1, 9f, 0f, 12000f, traits: EnemyTraits.OnOwnSoil)), 14, Drift(1f), NoMarch);
        AssertFalse(EnemyBeliefReadings.IsSeaInvasionThreat(Only(m, "inland"), 14, out _), "4 days ashore: an army like any other");
    }

    // F1: a landing raises orders only from a recent sighting.
    private static void LandedInvasionNeedsRecentSighting()
    {
        var landed = new EnemyBelief { UnitKey = "Expedition:9", Strength = 9000f, Confidence01 = 0.8f, LastSeenDay = 12, LastEmbarkedDay = 11,
            Traits = EnemyTraits.OnOwnSoil };
        AssertTrue(EnemyBeliefReadings.IsSeaInvasionThreat(in landed, 12 + EnemyBeliefReadings.RecentSightingDays, out bool l) && l, "seen 3 days ago: still landed");
        AssertFalse(EnemyBeliefReadings.IsSeaInvasionThreat(in landed, 13 + EnemyBeliefReadings.RecentSightingDays, out _), "unseen 4 days: no longer raises orders");
        AssertEqual(EnemyBeliefReadings.LandingWindowDays, EnemyBeliefReadings.RecentSightingDays, "the recency is the landing window");
    }

    private static void RaidThreatFromSeenCavalryOnOwnSoil()
    {
        var raid = new EnemyBelief { Traits = EnemyTraits.CavalryMajority | EnemyTraits.OnOwnSoil, Confidence01 = 1f, Strength = 2500f, LastSeenDay = 5 };
        AssertTrue(EnemyBeliefReadings.IsRaidThreat(in raid, 5), "cavalry on our soil");
        var home = raid;
        home.Traits = EnemyTraits.CavalryMajority;
        AssertFalse(EnemyBeliefReadings.IsRaidThreat(in home, 5), "cavalry on its own soil");
        var infantry = raid;
        infantry.Traits = EnemyTraits.OnOwnSoil;
        AssertFalse(EnemyBeliefReadings.IsRaidThreat(in infantry, 5), "infantry is no raid");
        var expired = raid;
        expired.Confidence01 = 0f;
        AssertFalse(EnemyBeliefReadings.IsRaidThreat(in expired, 5), "an expired belief is no threat");
        var afloat = raid;
        afloat.Traits |= EnemyTraits.Embarked;
        AssertFalse(EnemyBeliefReadings.IsRaidThreat(in afloat, 5), "cavalry carried by ships is a sea move, not a raid");
        AssertTrue(EnemyBeliefReadings.IsRaidThreat(in raid, 8), "seen 3 days ago: still a raid");
        AssertFalse(EnemyBeliefReadings.IsRaidThreat(in raid, 9), "unseen 4 days: a remembered raid raises no orders");
    }

    private static void NoThreatFromUnseenInvasionForce()
    {
        // An invasion force vanilla has loaded (seainvasionforce) but no viewer has seen: not in the
        // model, so no belief can produce a threat.
        var m = new EnemyArmyModel();
        m.Update(List(Seen("Home Guard:2", 2, 0f, 0f, 3000f, traits: EnemyTraits.OnOwnSoil)), 1, Drift(1f), NoMarch);
        int threats = 0;
        foreach (var b in m.Beliefs)
        {
            var copy = b;
            if (EnemyBeliefReadings.IsSeaInvasionThreat(in copy, 1, out _) || EnemyBeliefReadings.IsRaidThreat(in copy, 1)) threats++;
        }
        AssertEqual(0, threats, "no threat without a seen force");
    }

    // F1: a raid leaves sight. Days 0-3 it raises a custom defensive order (ActiveInvasion); day 4
    // its signature is reported once as Recovered (no new order, units held for the cooldown);
    // day 5 the signature is gone from the output, so the coastal runner releases its units. A
    // stale belief never keeps the order alive.
    private static void RaidLeavingSightRecoversThenReleases()
    {
        var m = new EnemyArmyModel();
        var tracker = new LapsedThreatTracker();
        var cooldown = new DefenseCooldownTable();
        Func<DefenseThreatSource, string> sig = DefenseIntentLedger.BuildSignature;
        m.Update(List(Seen("Stuart:7", 3, 0f, 0f, 2500f, unitTyp: 15, traits: EnemyTraits.CavalryMajority | EnemyTraits.OnOwnSoil)), 0, Drift(5f), March(5f));
        for (int day = 0; day <= 5; day++)
        {
            if (day > 0) m.Update(List(), day, Drift(5f), March(5f));
            var input = new DefenseIntentInput { Cooldown = cooldown, CooldownDays = 4 };
            foreach (var b in m.Beliefs)
            {
                var copy = b;
                if (EnemyBeliefReadings.IsRaidThreat(in copy, day))
                    input.Threats.Add(new DefenseThreatSource { Kind = DefenseThreatSourceKind.RaidForce, EnemyUnitKey = b.UnitKey, AssetName = "wilmington", EnemyStrength = EnemyBeliefReadings.Weighted(in copy) });
            }
            tracker.Close(input.Threats, sig);
            var output = DefenseIntentLedger.Build(input);
            var r = output.Responses.Find(x => x.Threat != null && x.Threat.Signature == "raid:Stuart:7:wilmington");
            if (day <= 3)
            {
                AssertTrue(r != null && r.Threat.Posture == DefensePosture.ActiveInvasion, "day " + day + ": active raid");
                AssertTrue(DefenseCustomOrderPolicy.RequiresCustomOrder(r), "day " + day + ": orders defenders");
            }
            else if (day == 4)
            {
                AssertTrue(r != null && r.Threat.Posture == DefensePosture.Recovered, "day 4: Recovered once");
                AssertFalse(DefenseCustomOrderPolicy.RequiresCustomOrder(r), "day 4: no new order");
            }
            else
            {
                AssertTrue(r == null, "day 5: signature gone, units released");
            }
            cooldown.Tick();
        }
    }

    // A sea-invasion belief that expires is reported once as Recovered on the same signature.
    private static void ExpiredSeaInvasionBeliefCollapsesThreat()
    {
        var tracker = new LapsedThreatTracker();
        Func<DefenseThreatSource, string> sig = DefenseIntentLedger.BuildSignature;
        var first = new List<DefenseThreatSource>
        {
            new DefenseThreatSource { Kind = DefenseThreatSourceKind.SeaInvasion, EnemyUnitKey = "Expedition:9", LandedSignal = true },
            new DefenseThreatSource { Kind = DefenseThreatSourceKind.AssetProximity, AssetName = "fort" }
        };
        tracker.Close(first, sig);
        AssertEqual(2, first.Count, "nothing lapsed on the first review");

        var second = new List<DefenseThreatSource>();
        tracker.Close(second, sig);
        AssertEqual(1, second.Count, "only the sea invasion is tracked");
        AssertTrue(second[0].SightingLapsed && second[0].EnemyUnitKey == "Expedition:9", "lapsed copy");
        var input = new DefenseIntentInput();
        input.Threats.AddRange(second);
        var output = DefenseIntentLedger.Build(input);
        AssertEqual(DefensePosture.Recovered, output.Responses[0].Threat.Posture, "Recovered");
        AssertEqual("sif:Expedition:9", output.Responses[0].Threat.Signature, "signature keyed on the belief's UnitKey");

        var third = new List<DefenseThreatSource>();
        tracker.Close(third, sig);
        AssertEqual(0, third.Count, "reported once");
    }

    // Vanilla GetStrengthRatio (12366-12404) on beliefs: own strength from own knowledge, enemy
    // from confidence-weighted beliefs (no enemy recruitment pool, no chapter-0 neutrals).
    private static void StrengthRatioFromBeliefs()
    {
        var m = SeenAndUnseen(out float seen);
        AssertNear(60000f / seen, EnemyBeliefReadings.StrengthRatio(60000f, m.Beliefs), 1e-4f, "own / believed");
        AssertNear(5000f, EnemyBeliefReadings.StrengthRatio(5000f, new List<EnemyBelief>()), 1e-3f, "nothing believed: own / 1 (vanilla's Max(1, enemy))");
        m.Update(List(), 7, Drift(1f), NoMarch);
        AssertNear(60000f / (seen * 0.5f), EnemyBeliefReadings.StrengthRatio(60000f, m.Beliefs), 1e-2f, "confidence-weighted");
    }

    private static void CapitalSamplesFromBeliefsCarryNoMorale()
    {
        var m = SeenAndUnseen(out float seen);
        var samples = new List<CapitalForceSample>();
        EnemyBeliefReadings.CapitalSamples(m.Beliefs, 10f, 30f, samples);
        AssertEqual(1, samples.Count, "one believed army");
        AssertNear(30f, samples[0].Distance, 1e-4f, "distance from the believed position");
        AssertNear(seen, samples[0].Strength, 1e-3f, "full confidence: its strength");
        AssertNear(1f, samples[0].Morale, 1e-6f, "a sighting shows no morale: min(str, str x 1) = str");
        AssertTrue(CampaignResponsivenessSignals.EnemyThreatensCapital(samples, new List<CapitalForceSample>(), 100f, 1000f),
            "a believed army near the capital threatens it");

        m.Update(List(), 7, Drift(1f), NoMarch);
        samples.Clear();
        EnemyBeliefReadings.CapitalSamples(m.Beliefs, 10f, 30f, samples);
        AssertNear(seen * 0.5f, samples[0].Strength, 1e-1f, "unseen a week: confidence-weighted");
    }

    // ---- Detection decisions that must fail closed (verify-g1 F3) ----

    private static void DetectionModeFailsClosed()
    {
        AssertEqual(DetectionMode.ConfigOff, DetectionRules.Mode(false, true, false), "flag off");
        AssertEqual(DetectionMode.NoFog, DetectionRules.Mode(true, false, false), "fog off");
        AssertEqual(DetectionMode.OwnSideWithinFog, DetectionRules.Mode(true, true, true), "player-alliance side");
        AssertEqual(DetectionMode.FogGrid, DetectionRules.Mode(true, true, false), "AI side");
        AssertTrue(DetectionRules.IsOmniscient(DetectionMode.NoFog) && DetectionRules.IsOmniscient(DetectionMode.ConfigOff), "rollback modes");
        AssertFalse(DetectionRules.IsOmniscient(DetectionMode.FogGrid) || DetectionRules.IsOmniscient(DetectionMode.OwnSideWithinFog), "fog modes");
        AssertTrue(DetectionRules.GridUnavailable(DetectionMode.FogGrid, gridPresent: false), "FogGrid without the grid: contact only");
        AssertFalse(DetectionRules.GridUnavailable(DetectionMode.FogGrid, true) || DetectionRules.GridUnavailable(DetectionMode.OwnSideWithinFog, false), "grid present or not needed");
    }

    private static void GridAnswerFailsClosed()
    {
        int calls = 0;
        Func<int, bool> hidden = u => { calls++; return u == 1; };
        AssertTrue(DetectionRules.GridVisible(true, true, hidden, 0, out bool threw) && !threw, "usable grid, not hidden: visible");
        AssertFalse(DetectionRules.GridVisible(true, true, hidden, 1, out _), "hidden");
        calls = 0;
        AssertFalse(DetectionRules.GridVisible(false, true, hidden, 0, out _), "fog off: never asked (101188 NRE)");
        AssertFalse(DetectionRules.GridVisible(true, false, hidden, 0, out _), "no usable grid: not visible");
        AssertEqual(0, calls, "the grid is not asked in either case");
        AssertFalse(DetectionRules.GridVisible<int>(true, true, u => throw new NullReferenceException(), 0, out threw), "a probe exception: not visible");
        AssertTrue(threw, "and reported, so the review continues contact only");
    }

    private static void AlliedGroupsAreViewers()
    {
        AssertEqual(UnitRelation.Viewer, DetectionRules.RelationOf(1, 1, false, false, false, false), "own group");
        AssertEqual(UnitRelation.Viewer, DetectionRules.RelationOf(1, 3, alliedWithSide: true, atWarWithSide: false, isFleet: false, isGarrison: false), "allied group paints fows[1] too");
        AssertEqual(UnitRelation.Viewer, DetectionRules.RelationOf(1, 1, false, false, isFleet: true, isGarrison: false), "own fleet");
        AssertEqual(UnitRelation.Viewer, DetectionRules.RelationOf(1, 1, false, false, false, isGarrison: true), "own garrison");
        AssertEqual(UnitRelation.Enemy, DetectionRules.RelationOf(1, 0, false, true, false, false), "enemy army");
        AssertEqual(UnitRelation.Ignore, DetectionRules.RelationOf(1, 0, false, true, isFleet: true, isGarrison: false), "enemy fleet");
        AssertEqual(UnitRelation.Ignore, DetectionRules.RelationOf(1, 0, false, true, false, isGarrison: true), "enemy garrison: only via objective forts");
        AssertEqual(UnitRelation.Ignore, DetectionRules.RelationOf(1, 2, false, false, false, false), "neutral");
    }

    private static void GarrisonGateRequiresSighting()
    {
        AssertFalse(DetectionRules.TryObjectiveGarrison(false, 7, "G:1", 14, 1f, 2f, 900f, out var none), "unseen garrison: no detection");
        AssertEqual(null, none.UnitKey, "nothing built");
        AssertTrue(DetectionRules.TryObjectiveGarrison(true, 7, "G:1", 14, 1f, 2f, 900f, out var seen), "seen garrison");
        AssertEqual(DetectionSource.ObjectiveGarrison, seen.Source, "ObjectiveGarrison");
        AssertNear(900f, seen.Strength, 1e-6f, "its strength");
    }

    // Integrator decision: detection is bookkeeping and runs for every alliance, including a W&L
    // CIC player's stand-down alliance and during the pause.
    private static void StandDownAllianceStillDetects()
    {
        string planner = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string review = MethodBody(planner, "internal static void ReviewPlan(");
        int detect = review.IndexOf("EnemyDetectionRuntime.Detect(", StringComparison.Ordinal);
        int standDown = review.IndexOf("mode == PlannerReviewMode.StandDown) return;", StringComparison.Ordinal);
        AssertTrue(detect >= 0 && standDown > detect, "the stand-down return comes after the belief step");
        string beforeDetect = review.Substring(0, detect);
        AssertFalse(beforeDetect.Contains("PlannerReviewMode.StandDown") || beforeDetect.Contains("PlannerReviewMode.Bookkeeping"), "no mode return before detection");

        // Sliced review (perf merge): the head job marks the CIC player's alliance PlayerCic, and that
        // mode's Plan job runs the bookkeeping review.
        string coordinator = ReadRepoFile("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        string cicJob = MethodBody(coordinator, "private void RunHeadReviewJob(");
        int cicBranch = cicJob.IndexOf("if (operationalRuntimeReady && IsPlayerCICOf(alliance, d.PlayerAlliance))", StringComparison.Ordinal);
        AssertTrue(cicBranch >= 0 && cicJob.IndexOf("a.Mode = AllianceReviewMode.PlayerCic;", cicBranch, StringComparison.Ordinal) > cicBranch, "the CIC player's alliance is reviewed as PlayerCic");
        string job = MethodBody(coordinator, "private void RunReviewJob(");
        int playerCic = job.IndexOf("if (a.Mode == AllianceReviewMode.PlayerCic)", StringComparison.Ordinal);
        int next = job.IndexOf("return;", playerCic, StringComparison.Ordinal);
        AssertTrue(playerCic >= 0 && job.IndexOf("CampaignPlannerRuntime.ReviewPlan(", playerCic, StringComparison.Ordinal) is int call && call > playerCic && call < next,
            "the CIC player's alliance gets its bookkeeping review");

        CampaignScope.ResetAll();
        EnemyBeliefStore.ModelFor(0).Update(List(Seen("Army of Northern Virginia:7", 1, 0f, 0f, 60000f)), 3, Drift(1f), NoMarch);
        AssertEqual(1, EnemyBeliefStore.Select(0, true, true, null).Count, "BeliefsFor serves the stand-down alliance's model");
        CampaignScope.ResetAll();
    }

    // verify-g1 r2 (N7): a same-day second sighting still gets one day of road march, so a re-key
    // or a rename on the review day of the last sighting is recognised.
    private static void SameDayIdentityUsesOneDayOfMarch()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("Old Name:3", 1, 0f, 0f, 9000f, unitTyp: 15)), 10, Drift(500f), March(10f));
        m.Update(List(Seen("Grand Corps:3", 2, 8f, 0f, 9500f, unitTyp: 15)), 10, Drift(500f), March(10f));
        AssertEqual("Grand Corps:3", Only(m, "same-day rename within one day's march (10)").UnitKey, "old belief retired");

        var n = new EnemyArmyModel();
        n.Update(List(Seen("Army of the James:12", 5, 0f, 0f, 30000f, unitTyp: 16)), 10, Drift(500f), March(10f));
        n.Update(List(Seen("Army of the James:40", 5, 6f, 0f, 30000f, unitTyp: 16)), 10, Drift(500f), March(10f));
        AssertEqual("Army of the James:40", Only(n, "same-day commander change").UnitKey, "re-keyed");
    }

    // verify-g1 r2 (N8): a fort garrison does not march; its identity radius is 0 at any age.
    private static void GarrisonIdentityHasNoMarchRadius()
    {
        var m = new EnemyArmyModel();
        m.Update(List(DetectionRules.ObjectiveGarrison(7, "Fort Pulaski garrison:300", 14, 0f, 0f, 1200f)), 0, Drift(9f), March(10f));
        m.Update(List(Seen("Raiders:5", 8, 5f, 0f, 900f, unitTyp: 14)), 3, Drift(9f), March(10f));
        AssertEqual(2, m.Beliefs.Count, "a new group 5 units from a fort (inside 3 days of march) never retires its garrison");

        m.Update(List(DetectionRules.ObjectiveGarrison(7, "Fort Pulaski garrison:301", 14, 0f, 0f, 1300f)), 4, Drift(9f), March(10f));
        bool rekeyed = false;
        foreach (var b in m.Beliefs) if (b.UnitKey == "Fort Pulaski garrison:301") rekeyed = true;
        AssertTrue(rekeyed, "the same garrison at the same town under a new commander is re-keyed");
        AssertEqual(2, m.Beliefs.Count, "re-keyed, not duplicated");
    }

    // R2-3: the belief clock counts calendar days (UniStorm's Gregorian rollover, 157818), so a
    // month end adds no phantom days to an age.
    private static void BeliefAgeCountsCalendarDays()
    {
        int feb27 = ReviewCadenceCatchUp.DaySerial(27, 2, 1862);
        int mar1 = ReviewCadenceCatchUp.DaySerial(1, 3, 1862);
        AssertEqual(2, mar1 - feb27, "27 Feb -> 1 Mar 1862: two days");
        var raid = new EnemyBelief { UnitKey = "Stuart:7", Confidence01 = 1f, LastSeenDay = feb27, LastEmbarkedDay = -1,
                                     Traits = EnemyTraits.CavalryMajority | EnemyTraits.OnOwnSoil };
        AssertTrue(EnemyBeliefReadings.IsRaidThreat(in raid, mar1), "a raid seen 27 Feb is still recent on 1 Mar");

        var m = new EnemyArmyModel();
        m.Update(List(Seen("A:1", 1, 0f, 0f, 1000f)), feb27, Drift(2f), NoMarch);
        m.Update(List(), mar1, Drift(2f), NoMarch);
        AssertNear((float)Math.Pow(0.5, 2.0 / 7.0), Only(m, "aged").Confidence01, 1e-6f, "decays two days");

        int dec31 = ReviewCadenceCatchUp.DaySerial(31, 12, 1862);
        int jan1 = ReviewCadenceCatchUp.DaySerial(1, 1, 1863);
        AssertEqual(1, jan1 - dec31, "31 Dec -> 1 Jan: one day");
        var n = new EnemyArmyModel();
        n.Update(List(Seen("A:1", 1, 0f, 0f, 1000f)), dec31, Drift(2f), NoMarch);
        n.Update(List(), jan1, Drift(2f), NoMarch);
        AssertNear(2f, Only(n, "new year").UncertaintyRadius, 1e-4f, "drifts one day");
    }

    // R2-4: a lapsed copy carries its asset name for the signature but covers no asset, so the
    // proximity pass still raises a threat at that asset.
    private static void LapsedThreatLeavesAssetToProximity()
    {
        var tracker = new LapsedThreatTracker();
        Func<DefenseThreatSource, string> sig = DefenseIntentLedger.BuildSignature;
        var threats = new List<DefenseThreatSource>
        {
            new DefenseThreatSource { Kind = DefenseThreatSourceKind.RaidForce, EnemyUnitKey = "Stuart:7", AssetName = "Wilmington" }
        };
        tracker.Close(threats, sig);
        AssertTrue(LapsedThreatTracker.CoversAsset(threats[0]), "an active raid covers its asset");
        var next = new List<DefenseThreatSource>();
        tracker.Close(next, sig);
        AssertEqual(1, next.Count, "lapsed once");
        AssertEqual("raid:Stuart:7:Wilmington", sig(next[0]), "the lapsed copy keeps the signature (the runner releases on it)");
        AssertFalse(LapsedThreatTracker.CoversAsset(next[0]), "a lapsed copy covers no asset");
        AssertFalse(LapsedThreatTracker.CoversAsset(new DefenseThreatSource { Kind = DefenseThreatSourceKind.RaidForce }), "no asset, nothing covered");
        AssertFalse(LapsedThreatTracker.CoversAsset(null), "null");
    }

    // ---- Source pins (BattleWriteGateTests style) ----

    private static void DetectionWiringPinned()
    {
        string planner = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string review = MethodBody(planner, "internal static void ReviewPlan(");
        int detect = review.IndexOf("EnemyDetectionRuntime.Detect(snapshot, GameVars.playeralliance, EnemyDetectionRuntime.FogHonestFlagsOn())", StringComparison.Ordinal);
        int update = review.IndexOf(".Update(snapshot.Detections, snapshot.DaySerial, EnemyDetectionRuntime.DriftFor(alliance), EnemyDetectionRuntime.MarchFor())", StringComparison.Ordinal);
        int copy = review.IndexOf("snapshot.Beliefs.AddRange(", StringComparison.Ordinal);
        int report = review.IndexOf("EnemyDetectionRuntime.Report(snapshot, beliefs);", StringComparison.Ordinal);
        int bookkeeping = review.IndexOf("mode == PlannerReviewMode.Bookkeeping) return;", StringComparison.Ordinal);
        AssertTrue(detect >= 0 && update > detect && copy > update && report > copy && bookkeeping > report,
            "detect -> update -> copy -> [EnemyBeliefs], before the bookkeeping return (beliefs and their rows continue during the pause)");

        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs");
        string detectBody = MethodBody(runtime, "internal static void Detect(");
        AssertContains(detectBody, "DetectionMode mode = DetectionRules.Mode(fogHonest, GameVars.usefow, sidePlayer);", "mode from the pure rule");
        AssertContains(detectBody, "_probeNoFog = DetectionRules.IsOmniscient(mode);", "omniscient only in the rollback modes");
        AssertContains(detectBody, "_gridBroken = DetectionRules.GridUnavailable(mode, (UnityEngine.Object)_probeGrid != null);", "a missing grid fails closed");
        AssertContains(detectBody, "DetectionRules.RunMasked(", "the grid probes run inside the incident mask");
        AssertContains(detectBody, "PlayerIntelIncidents.Masks(mode == DetectionMode.FogGrid, DLC_WL.dlc_scenarioactive)", "mask only for rule (b)");
        AssertContains(MethodBody(runtime, "private static void CollectUnits("),
            "DetectionRules.RelationOf(side, other, sideState.IsInAllianceWith(other), sideState.IsAtWarWith(other), reg.unittyp == 17, (UnityEngine.Object)reg.garrisonreference != null)",
            "own and allied viewers, enemy armies by the pure rule");
        AssertContains(MethodBody(runtime, "private static bool GridVisible("),
            "DetectionRules.GridVisible(GameVars.usefow, !_gridBroken && (UnityEngine.Object)_probeGrid != null, HiddenProbe, e, out bool threw)",
            "the grid answer through the fail-closed rule");
        AssertContains(MethodBody(runtime, "private static bool GridVisible("), "_gridBroken = true;", "a probe exception ends grid probing for the review");
        int hiddenCalls = 0;
        for (int at = runtime.IndexOf("UnitIsHidden(", StringComparison.Ordinal); at >= 0; at = runtime.IndexOf("UnitIsHidden(", at + 1, StringComparison.Ordinal)) hiddenCalls++;
        AssertEqual(1, hiddenCalls, "one UnitIsHidden call");
        AssertContains(MethodBody(runtime, "private static bool IsHiddenFromGrid("), "UnitIsHidden(", "inside the probe DetectionRules.GridVisible calls");
        AssertContains(runtime, "private static readonly Func<Regiment, bool> HiddenProbe = IsHiddenFromGrid;", "the probe is only a delegate");
        int probeRefs = 0;
        for (int at = runtime.IndexOf("IsHiddenFromGrid", StringComparison.Ordinal); at >= 0; at = runtime.IndexOf("IsHiddenFromGrid", at + 1, StringComparison.Ordinal)) probeRefs++;
        AssertEqual(2, probeRefs, "never called directly (delegate + definition only)");
        AssertContains(MethodBody(runtime, "private static void ProbeObjectiveGarrisons("), "DetectionRules.TryObjectiveGarrison(Sees(s, garrison, out _),", "a garrison only once seen or contacted");
        AssertContains(MethodBody(runtime, "internal static IReadOnlyList<EnemyBelief> BeliefsFor("),
            "EnemyBeliefStore.Select(alliance, FlagOn(Plugin.EnableCampaignPlanner), FlagOn(Plugin.EnableCampaignFogHonestBeliefs), OmniscientDelegate);",
            "fallback decided by the two flags, as they are");
        string code = CodeOnly(runtime);
        AssertFalse(code.Contains("GetLastTransmittedPathPos"), "never an enemy destination");
        AssertFalse(code.Contains("seainvasionforce") || code.Contains("raidforces"), "never enemy plan state");

        // The only omniscient read is the flags-off rollback, rebuilt once per campaign day.
        int reads = 0;
        for (int at = runtime.IndexOf("\"enemyunits\"", StringComparison.Ordinal); at >= 0; at = runtime.IndexOf("\"enemyunits\"", at + 1, StringComparison.Ordinal)) reads++;
        AssertEqual(1, reads, "one enemyunits read");
        string rollback = MethodBody(runtime, "private static IReadOnlyList<EnemyBelief> OmniscientBeliefs(");
        AssertContains(rollback, "\"enemyunits\"", "inside the rollback builder");
        AssertTrue(rollback.IndexOf("if (OmniscientCacheDay[alliance] == campaignDay) return list;", StringComparison.Ordinal) >= 0
                   && rollback.IndexOf("if (OmniscientCacheDay[alliance] == campaignDay) return list;", StringComparison.Ordinal) < rollback.IndexOf("list.Clear();", StringComparison.Ordinal),
            "the rollback list is cached per campaign day");

        AssertContains(MethodBody(runtime, "internal static int ReviewDayFor("),
            "return alliance >= 0 && alliance < Alliances ? LastDaySerial[alliance] : -1;", "the recency clock is the alliance's last review day");
        AssertContains(detectBody, "LastDaySerial[side] = s.DaySerial;", "stamped by each detection");
        AssertContains(planner, "int daySerial = ReviewCadenceCatchUp.DaySerial(day, month, year);", "the review day is the calendar day serial");
        AssertContains(planner, "CampaignSnapshotRuntime.Build(alliance, faction, daySerial,", "the snapshot day is the calendar day serial");
        string coordinator = ReadRepoFile("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        // Sliced review (perf merge): the calendar day is captured on the date-change frame and
        // stamped by the first job (Map), before any alliance's jobs, whatever the readiness.
        AssertContains(MethodBody(coordinator, "private bool BeginReviewDay("), "int calendarDay = ReviewCadenceCatchUp.DaySerial(day, month, year);", "calendar day captured with the day's inputs");
        string reviewJob = MethodBody(coordinator, "private void RunReviewJob(");
        int mapAt = reviewJob.IndexOf("if (job.Kind == DailyReviewJobKind.Map)", StringComparison.Ordinal);
        int stampAt = reviewJob.IndexOf("EnemyDetectionRuntime.StampReviewDay(d.CalendarDay);", StringComparison.Ordinal);
        int readyAt = reviewJob.IndexOf("if (d.OperationalRuntimeReady)", StringComparison.Ordinal);
        AssertTrue(mapAt >= 0 && stampAt > mapAt && readyAt > stampAt, "the calendar day is stamped once per review by the map job, before any alliance (also with the planner off)");
        AssertEqual(DailyReviewJobKind.Map, DailyReviewJobList.Ordered[0].Kind, "the map job runs first");
        AssertEqual(1, coordinator.Split(new[] { "EnemyDetectionRuntime.StampReviewDay(" }, StringSplitOptions.None).Length - 1, "one stamp");
        AssertContains(rollbackBodyOf(runtime), "int campaignDay = _reviewDay;", "the rollback is keyed by the calendar stamp");
        AssertContains(rollbackBodyOf(runtime), "int day = campaignDay;", "and dated by it");
        AssertFalse(CodeOnly(runtime).Contains("currenttimefromstart"), "no day from currenttimefromstart (30171: months are 1/12 year)");
        foreach (string file in System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(FindRepoPath("src/WhiskeyRealism/Strategic/Planner/EnemyArmyModel.cs")), "*.cs"))
            AssertFalse(CodeOnly(System.IO.File.ReadAllText(file)).Contains("372"), "no legacy 372-day serial in " + System.IO.Path.GetFileName(file));

        // Order-raising threats use the recency clock and the lapse tracker.
        string defense = ReadRepoFile("src/WhiskeyRealism/Strategic/DefenseIntentRuntime.cs");
        AssertContains(defense, "int today = EnemyDetectionRuntime.ReviewDayFor(allianceId);", "recency clock");
        AssertContains(defense, "EnemyBeliefReadings.IsRaidThreat(in belief, today)", "raids need a recent sighting");
        AssertContains(defense, "EnemyBeliefReadings.IsSeaInvasionThreat(in belief, today, out bool landed)", "landings need a recent sighting");
        AssertContains(defense, "LapseTrackers[allianceId].Close(input.Threats, LapseSignature);", "lapsed threats reported once");
        AssertContains(MethodBody(defense, "private static void ExtractAssetProximityThreats("),
            "if (LapsedThreatTracker.CoversAsset(threat)) coveredAssets.Add(threat.AssetName);", "a lapsed copy leaves its asset to the proximity pass");
    }

    // verify-g1 r3 R3-1: the review has two integer day scales in scope, the calendar day
    // (ReviewCadenceCatchUp.DaySerial, in memory only) and the legacy year*372 + month*31 + day
    // serial (persisted in the sidecar: streaks, LastFullRefreshDay, replans, operations). Each
    // call site is pinned to its scale so a mix cannot be written silently.
    private static void ReviewDayScalesNeverMix()
    {
        string coordinator = ReadRepoFile("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        // Sliced review (perf merge): the day's inputs, the head job and the other jobs.
        string core = MethodBody(coordinator, "private bool BeginReviewDay(") + MethodBody(coordinator, "private void RunHeadReviewJob(")
            + MethodBody(coordinator, "private void RunReviewJob(");
        AssertContains(core, "int calendarDay = ReviewCadenceCatchUp.DaySerial(day, month, year);", "the calendar day");
        AssertContains(core, "int legacySerial = year * 372 + month * 31 + day;", "the legacy serial");
        AssertContains(core, "UpdateCapitalThreat(alliance, legacySerial);", "capital threat streak: legacy (persisted)");
        AssertContains(core, "PublishDirectorPosture(alliance, day, month, year, legacySerial);", "director LastFullRefreshDay: legacy (persisted)");
        AssertContains(core, "a.LegacySerial = legacySerial;", "the head job hands the legacy serial on");
        AssertContains(core, "_frontChangedDay[alliance] = a.LegacySerial;", "front change day: legacy");
        AssertContains(core, "CalendarDay = calendarDay,", "the day's calendar day");
        AssertEqual(2, core.Split(new[] { "UpdatePolicyPlan(alliance, a.Era.Stage, d.Day, d.Month, d.Year, d.CalendarDay)" }, StringSplitOptions.None).Length - 1, "CIC player and AI policy steps: calendar");
        AssertContains(core, "ReviewCadenceCatchUp.SkippedWeeklyGate(_lastReviewCalendarDay, calendarDay)", "cadence: calendar");
        AssertContains(core, "ReviewCadenceCatchUp.SkippedAlternatingGate(_lastReviewCalendarDay, calendarDay)", "cadence: calendar");
        AssertContains(MethodBody(coordinator, "private void UpdatePolicyPlanCore("),
            "int days = _needCalendarDay[alliance] >= 0 ? calendarDay - _needCalendarDay[alliance] : 1;", "need step gap in calendar days");
        AssertContains(coordinator, "private void UpdateCapitalThreat(int alliance, int legacySerial)", "named by scale");
        AssertContains(coordinator, "private void PublishDirectorPosture(int alliance, int day, int month, int year, int legacySerial)", "named by scale");
        int calendarProducers = 0;
        for (int at = coordinator.IndexOf("ReviewCadenceCatchUp.DaySerial(", StringComparison.Ordinal); at >= 0; at = coordinator.IndexOf("ReviewCadenceCatchUp.DaySerial(", at + 1, StringComparison.Ordinal)) calendarProducers++;
        AssertEqual(1, calendarProducers, "one calendar producer in the coordinator (calendarDay)");
        AssertFalse(System.Text.RegularExpressions.Regex.IsMatch(CodeOnly(coordinator), @"\b(daySerial|reviewSerial)\s*="),
            "no local of unnamed scale");
    }

    private static string rollbackBodyOf(string runtime) => MethodBody(runtime, "private static IReadOnlyList<EnemyBelief> OmniscientBeliefs(");

    private static void MigratedReadersReadBeliefsOnly()
    {
        var forbidden = new System.Text.RegularExpressions.Regex(
            @"\b(enemyunits|seainvasionforce|raidforces|GetClosestEnemyUnitReg|GetStrengthRatio|GetUnitStrengthInRange|GetLastTransmittedPathPos)\b");
        foreach (string path in new[]
        {
            "src/WhiskeyRealism/Strategic/FrontSectorRuntime.cs",
            "src/WhiskeyRealism/Strategic/DefenseIntentRuntime.cs",
            "src/WhiskeyRealism/Strategic/CoastalDefenseCustomOrderRunner.cs",
            "src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs",
            "src/WhiskeyRealism/Patches/FortConstructionGovernorPatch.cs",
            "src/WhiskeyRealism/Patches/RecruitmentPatch.cs",
        })
        {
            foreach (string line in ReadRepoFile(path).Split('\n'))
            {
                string code = line.TrimStart();
                if (code.StartsWith("//")) continue;
                AssertFalse(forbidden.IsMatch(code), path + " reads an omniscient enemy source: " + code.Trim());
            }
            AssertContains(ReadRepoFile(path), "EnemyDetectionRuntime.BeliefsFor(", path + " reads beliefs");
        }

        string telegraph = ReadRepoFile("src/WhiskeyRealism/Strategic/Construction/TelegraphConstructionRuntime.cs");
        AssertFalse(CodeOnly(telegraph).Contains("GetClosestEnemyUnitReg"), "telegraph enemy check on beliefs");
        AssertContains(MethodBody(telegraph, "private static bool EnemyNearby("), "EnemyDetectionRuntime.BeliefsFor(", "EnemyNearby on beliefs");
        AssertContains(MethodBody(telegraph, "private static bool EnemyNearPosition("), "EnemyDetectionRuntime.BeliefsFor(", "EnemyNearPosition on beliefs");
        AssertFalse(MethodBody(telegraph, "private static bool EnemyNearPosition(").Contains("campaignunitlist"), "no other-alliance scan");

        string index = MethodBody(ReadRepoFile("src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs"), "private static Dictionary<string, List<Regiment>> BuildAreaIndex(");
        AssertContains(index, "u.alliance != allianceId", "the area index holds own formations only");

        string coordinator = ReadRepoFile("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        string capital = MethodBody(coordinator, "private void UpdateCapitalThreat(");
        AssertFalse(capital.Contains("enemyunits") || capital.Contains("_enemyUnitsField"), "capital threat on beliefs");
        AssertContains(capital, "EnemyDetectionRuntime.BeliefsFor(alliance)", "capital threat reads beliefs");
    }

    private static string CodeOnly(string src)
    {
        var lines = new List<string>();
        foreach (string line in src.Split('\n'))
            if (!line.TrimStart().StartsWith("//")) lines.Add(line);
        return string.Join("\n", lines);
    }

    private static string MethodBody(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        AssertTrue(start >= 0, "method present: " + signature);
        int open = src.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(start, i - start + 1);
        }
        return src.Substring(start);
    }
}
