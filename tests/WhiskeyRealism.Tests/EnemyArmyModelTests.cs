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
            ("LocalPressureExchangeUsesBeliefGuns", LocalPressureExchangeUsesBeliefGuns),
            ("TelegraphSiteNotBlockedByUnseenEnemy", TelegraphSiteNotBlockedByUnseenEnemy),
            ("SeaInvasionThreatOnlyFromSeenEmbarkedForce", SeaInvasionThreatOnlyFromSeenEmbarkedForce),
            ("LandingInferredFromSeenForceOnOwnCoast", LandingInferredFromSeenForceOnOwnCoast),
            ("RaidThreatFromSeenCavalryOnOwnSoil", RaidThreatFromSeenCavalryOnOwnSoil),
            ("NoThreatFromUnseenInvasionForce", NoThreatFromUnseenInvasionForce),
            ("ExpiredSeaInvasionBeliefCollapsesThreat", ExpiredSeaInvasionBeliefCollapsesThreat),
            ("StrengthRatioFromBeliefs", StrengthRatioFromBeliefs),
            ("CapitalSamplesFromBeliefsCarryNoMorale", CapitalSamplesFromBeliefsCarryNoMorale),
            ("DetectionWiringPinned", DetectionWiringPinned),
            ("MigratedReadersReadBeliefsOnly", MigratedReadersReadBeliefsOnly),
        });
    }

    private const float NoDrift = 0f;

    private static EnemyDetection Seen(string key, int instanceId, float x, float z, float strength,
                                       int unitTyp = 15, DetectionSource source = DetectionSource.FogGrid,
                                       EnemyTraits traits = EnemyTraits.None, float guns = 0f)
    {
        return new EnemyDetection
        {
            InstanceId = instanceId, UnitKey = key, UnitTyp = unitTyp, X = x, Z = z,
            Strength = strength, Guns = guns, Source = source, Traits = traits
        };
    }

    private static List<EnemyDetection> List(params EnemyDetection[] d) => new List<EnemyDetection>(d);

    private static Func<int, float> Drift(float perDay) => _ => perDay;

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
        m.Update(List(Seen("Army of the Potomac:12", 101, 10f, 20f, 50000f)), 100, Drift(5f));
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
        m.Update(List(Seen("A:1", 1, 0f, 0f, 1000f)), 10, Drift(1f));
        m.Update(List(), 17, Drift(1f));
        AssertNear(0.5f, Only(m, "7d").Confidence01, 1e-4f, "half after 7 days");
        m.Update(List(), 24, Drift(1f));
        AssertNear(0.25f, Only(m, "14d").Confidence01, 1e-4f, "quarter after 14 days");
        m.Update(List(), 13, Drift(1f));
        AssertNear((float)Math.Pow(0.5, 3.0 / 7.0), Only(m, "3d").Confidence01, 1e-4f, "absolute from last seen, not compounded");
    }

    private static void UnseenRadiusGrowsByDrift()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("A:1", 1, 0f, 0f, 1000f, unitTyp: 16)), 10, t => t == 16 ? 4f : 99f);
        m.Update(List(), 15, t => t == 16 ? 4f : 99f);
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
        m.Update(List(Seen("A:1", 1, 0f, 0f, 1000f)), 0, Drift(1f));
        m.Update(List(), EnemyArmyModel.ExpireDays - 1, Drift(1f));
        AssertEqual(1, m.Beliefs.Count, "alive at 29 days");
        AssertTrue(m.Beliefs[0].Confidence01 >= EnemyArmyModel.MinConfidence, "above the floor at 29 days");
        AssertEqual(0, m.ExpiredKeys.Count, "nothing expired yet");
        m.Update(List(), EnemyArmyModel.ExpireDays, Drift(1f));
        AssertEqual(0, m.Beliefs.Count, "expired at 30 days");
        AssertEqual("A:1", m.ExpiredKeys.Count == 1 ? m.ExpiredKeys[0] : null, "expired key reported");
        AssertTrue(EnemyArmyModel.Confidence(31) < EnemyArmyModel.MinConfidence, "below the floor after 30 days anyway");
        m.Update(List(), EnemyArmyModel.ExpireDays + 1, Drift(1f));
        AssertEqual(0, m.ExpiredKeys.Count, "expired keys are per update");
    }

    private static void ReappearingEnemyRefreshes()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("A:1", 1, 0f, 0f, 1000f)), 0, Drift(2f));
        m.Update(List(), 10, Drift(2f));
        m.Update(List(Seen("A:1", 1, 50f, 60f, 900f)), 12, Drift(2f));
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
        m.Update(seen, 5, Drift(3f));
        AssertEqual(0, m.Beliefs.Count, "unseen garrison: no belief");

        seen.Clear();
        if (DetectionRules.TryDecide(false, false, false, false, viewerCovers: true, gridVisible: true, out _))
            seen.Add(DetectionRules.ObjectiveGarrison(7, "Fort Pulaski garrison:300", 14, 100f, 200f, 1200f));
        m.Update(seen, 6, Drift(3f));
        var b = Only(m, "seen garrison");
        AssertNear(1200f, b.Strength, 1e-6f, "garrison strength");
        AssertNear(100f, b.X, 1e-6f, "at the town");
        AssertEqual(DetectionSource.ObjectiveGarrison, b.LastSource, "ObjectiveGarrison source");
    }

    private static void GarrisonBeliefDoesNotDrift()
    {
        var m = new EnemyArmyModel();
        m.Update(List(DetectionRules.ObjectiveGarrison(7, "G:1", 14, 0f, 0f, 500f)), 0, Drift(9f));
        m.Update(List(), 6, Drift(9f));
        AssertNear(EnemyArmyModel.SeenRadius, Only(m, "garrison").UncertaintyRadius, 1e-6f, "a fort does not move");
        AssertTrue(Only(m, "garrison").Confidence01 < 1f, "but its picture still ages");
    }

    private sealed class FakeIncident
    {
        public float LaunchedTime;
        public bool Gone;
    }

    // 101183-101186: incidents 60/61/64 make UnitIsHidden say "visible" everywhere. The AI side's
    // fog-grid probe runs with them masked (not launched) and restores them, also when it throws.
    private static void PlayerIntelIncidentsNeverRevealToAiSide()
    {
        AssertEqual("60,61,64", string.Join(",", DetectionRules.PlayerIntelIncidents), "Lost Orders and Scouting Ahead (101183)");
        AssertTrue(DetectionRules.MasksPlayerIntelIncidents(fogGridRule: true, dlcScenarioActive: true), "rule (b) masks");
        AssertFalse(DetectionRules.MasksPlayerIntelIncidents(fogGridRule: false, dlcScenarioActive: true), "rule (c) keeps the incident: the player's own side");
        AssertFalse(DetectionRules.MasksPlayerIntelIncidents(true, false), "no W&L scenario");

        var lostOrders = new FakeIncident { LaunchedTime = 3.5f };
        var idle = new FakeIncident { LaunchedTime = DetectionRules.IncidentNotLaunched };
        var incidents = new List<FakeIncident> { lostOrders, idle, null };
        var mask = new FieldMask<FakeIncident, float>(i => i.LaunchedTime, (i, v) => i.LaunchedTime = v, DetectionRules.IncidentNotLaunched);

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
            if (i != null && DetectionRules.IncidentLaunched(i.LaunchedTime)) return true;
        return false;
    }

    private static void ModelUpdateIsIdempotentForSameDay()
    {
        var m = new EnemyArmyModel();
        var seen = List(Seen("A:1", 1, 0f, 0f, 1000f), Seen("B:2", 2, 40f, 0f, 2000f));
        m.Update(seen, 0, Drift(2f));
        m.Update(List(Seen("A:1", 1, 5f, 0f, 1000f)), 4, Drift(2f));
        string first = Dump(m);
        m.Update(List(Seen("A:1", 1, 5f, 0f, 1000f)), 4, Drift(2f));
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
        m.Update(List(Seen("A:1", 1, 0f, 0f, 1000f), Seen("B:2", 2, 9f, 9f, 500f)), 0, Drift(1f));
        m.Update(List(), 3, Drift(1f));
        AssertEqual(2, m.Beliefs.Count, "remembered");
        foreach (var b in m.Beliefs) AssertTrue(b.Confidence01 < 1f && b.Confidence01 > 0.5f, "decayed, not dropped");
    }

    private static void BeliefsKeyedByUnitKeyNotInstanceId()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("Army of Northern Virginia:7", 111, 0f, 0f, 60000f)), 0, Drift(1f));
        m.Update(List(Seen("Army of Northern Virginia:7", 999, 3f, 0f, 61000f)), 1, Drift(1f));
        var b = Only(m, "one belief across instance ids");
        AssertEqual(999, b.InstanceId, "instance id rebound to this session's");
        AssertNear(61000f, b.Strength, 1e-6f, "refreshed");
    }

    private static void CommanderChangeReKeysBeliefNotDuplicates()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("Army of the Potomac:12", 5, 0f, 0f, 80000f, unitTyp: 16)), 0, Drift(1f));
        // Seen again 50 units away (outside the 2-unit radius, so the rename rule cannot apply)
        // under a new commander: the same army by name and type.
        m.Update(List(Seen("Army of the Potomac:31", 5, 50f, 0f, 80000f, unitTyp: 16)), 2, Drift(1f));
        var b = Only(m, "re-keyed");
        AssertEqual("Army of the Potomac:31", b.UnitKey, "new commander key");

        // Same name, different unit type: a different group, not a re-key.
        m.Update(List(Seen("Army of the Potomac:31", 5, 50f, 0f, 80000f, unitTyp: 16), Seen("Army of the Potomac:44", 6, 900f, 0f, 3000f, unitTyp: 14)), 3, Drift(1f));
        AssertEqual(2, m.Beliefs.Count, "a matched belief is never re-keyed away");
    }

    private static void DuplicateUnitKeyNeverMergesTwoArmies()
    {
        OnceLog.Reset();
        int before = OnceLog.FiredCount;
        var m = new EnemyArmyModel();
        m.Update(List(Seen("1st Corps:4", 10, 0f, 0f, 9000f), Seen("1st Corps:4", 11, 300f, 0f, 8000f)), 0, Drift(1f));
        AssertEqual(2, m.Beliefs.Count, "two armies, two beliefs");
        AssertNear(17000f, EnemyBeliefQuery.StrengthWithin(m.Beliefs, 150f, 0f, 1000f), 1e-3f, "both counted");
        AssertEqual(before + 1, OnceLog.FiredCount, "one warning");
        var keys = new List<string> { m.Beliefs[0].UnitKey, m.Beliefs[1].UnitKey };
        keys.Sort(StringComparer.Ordinal);
        AssertEqual("1st Corps:4#10|1st Corps:4#11", string.Join("|", keys), "composite keys");

        m.Update(List(Seen("1st Corps:4", 10, 5f, 0f, 9000f), Seen("1st Corps:4", 11, 305f, 0f, 8000f)), 1, Drift(1f));
        AssertEqual(2, m.Beliefs.Count, "stable across reviews");
        AssertEqual(before + 1, OnceLog.FiredCount, "the warning stays once");
    }

    private static void RenamedGroupRetiresOldBelief()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("Old Name:3", 1, 0f, 0f, 9000f, unitTyp: 15)), 0, Drift(10f));
        // Two days unseen: radius 20. A renamed corps appears 12 units away under a new key.
        m.Update(List(Seen("Grand Corps:3", 2, 12f, 0f, 9500f, unitTyp: 15)), 2, Drift(10f));
        var b = Only(m, "renamed: one belief");
        AssertEqual("Grand Corps:3", b.UnitKey, "the detection is the fresh belief");

        // A new key outside every unmatched belief's radius is simply a new army.
        m.Update(List(Seen("Grand Corps:3", 2, 12f, 0f, 9500f, unitTyp: 15), Seen("Far Corps:8", 3, 900f, 0f, 4000f, unitTyp: 15)), 3, Drift(10f));
        AssertEqual(2, m.Beliefs.Count, "two seen armies are never merged");
    }

    private static void BeliefsSurviveReloadWithoutDoubleCount()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("Army of Tennessee:9", 77, 10f, 10f, 40000f, unitTyp: 16)), 20, Drift(1f));
        var saved = new List<EnemyBelief>(m.Beliefs);
        float before = EnemyBeliefQuery.StrengthWithin(m.Beliefs, 10f, 10f, 100f);

        var reloaded = new EnemyArmyModel();
        var dto = saved[0];
        dto.InstanceId = 0; // the sidecar stores no instance id
        reloaded.Restore(new[] { dto });
        reloaded.Update(List(Seen("Army of Tennessee:9", 4242, 10f, 10f, 40000f, unitTyp: 16)), 20, Drift(1f));
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
        EnemyBeliefStore.ModelFor(1).Update(List(Seen("A:1", 1, 0f, 0f, 5000f)), 3, Drift(1f));
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
        AssertTrue(DetectionRules.InBugleContact(14f, 0f, 15f, ox, oz, ob, 1), "inside the enemy's bugle range (127231)");
        AssertFalse(DetectionRules.InBugleContact(30f, 0f, 15f, ox, oz, ob, 1), "out of both");
        AssertFalse(DetectionRules.InBugleContact(0f, 0f, 15f, ox, oz, ob, 0), "no own formation");
    }

    private static void TraitsFromWhatASightingShows()
    {
        var t = DetectionRules.Traits(embarked: true, cavalryShare: 0.6f, onOwnSoil: true, nearOwnCoast: true);
        AssertEqual(EnemyTraits.Embarked | EnemyTraits.CavalryMajority | EnemyTraits.OnOwnSoil | EnemyTraits.NearOwnCoast, t, "all traits");
        AssertEqual(EnemyTraits.None, DetectionRules.Traits(false, 0.5f, false, false), "half cavalry is no majority");
    }

    // ---- Migrated readers (C25) ----

    private static EnemyArmyModel SeenAndUnseen(out float seenStrength)
    {
        // The planner side saw one army; a second one marched past outside every viewer and was
        // never detected, so it is not in the model at all.
        var m = new EnemyArmyModel();
        seenStrength = 30000f;
        m.Update(List(Seen("Seen Army:1", 1, 10f, 0f, seenStrength, unitTyp: 16, guns: 40f)), 0, Drift(1f));
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

        m.Update(List(), 7, Drift(1f));
        byTheater.Clear();
        EnemyBeliefReadings.SumByKey(m.Beliefs, (x, z) => Theater.East, byTheater);
        AssertNear(seen * 0.5f, byTheater[Theater.East], 1e-1f, "confidence-weighted once unseen");
    }

    private static void LocalPressureFromBeliefsExcludesUnseenEnemy()
    {
        var m = SeenAndUnseen(out float seen);
        EnemyBeliefReadings.LocalPressure(m.Beliefs, 0f, 0f, 50f, out float strength, out _, out FormationLevel level);
        AssertNear(seen, strength, 1e-3f, "believed army within range");
        AssertEqual(FormationLevel.Army, level, "visible enemy level from the belief's unit type");
        EnemyBeliefReadings.LocalPressure(m.Beliefs, 500f, 0f, 50f, out strength, out _, out level);
        AssertNear(0f, strength, 1e-6f, "nothing believed there (the unseen army is invisible)");
        AssertEqual(FormationLevel.Unknown, level, "no visible enemy");
    }

    private static void LocalPressureExchangeUsesBeliefGuns()
    {
        var m = SeenAndUnseen(out float seen);
        EnemyBeliefReadings.LocalPressure(m.Beliefs, 0f, 0f, 50f, out _, out float exchange, out _);
        AssertNear(seen + 40f * EnemyBeliefReadings.GunExchangeWeight, exchange, 1e-2f, "strength + guns x 65 (FormationDirectiveRuntime 402-407)");
        AssertEqual(65f, EnemyBeliefReadings.GunExchangeWeight, "vanilla-scale gun weight kept");
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
        AssertTrue(EnemyBeliefReadings.IsSeaInvasionThreat(in afloat, out bool landed), "embarked off our coast");
        AssertFalse(landed, "still afloat: invasion watch");

        var farOut = afloat;
        farOut.Traits = EnemyTraits.Embarked;
        AssertFalse(EnemyBeliefReadings.IsSeaInvasionThreat(in farOut, out _), "afloat far from our coast");

        var ashoreAtHome = new EnemyBelief { Traits = EnemyTraits.OnOwnSoil, LastSeenDay = 10, LastEmbarkedDay = -1, Confidence01 = 1f, Strength = 5000f };
        AssertFalse(EnemyBeliefReadings.IsSeaInvasionThreat(in ashoreAtHome, out _), "an army that never embarked is no invasion");
    }

    private static void LandingInferredFromSeenForceOnOwnCoast()
    {
        var m = new EnemyArmyModel();
        m.Update(List(Seen("Expedition:9", 1, 0f, 0f, 12000f, traits: EnemyTraits.Embarked | EnemyTraits.NearOwnCoast)), 10, Drift(1f));
        m.Update(List(Seen("Expedition:9", 1, 2f, 0f, 12000f, traits: EnemyTraits.OnOwnSoil | EnemyTraits.NearOwnCoast)), 12, Drift(1f));
        var b = Only(m, "landed");
        AssertEqual(10, b.LastEmbarkedDay, "the day it was last seen afloat");
        AssertTrue(EnemyBeliefReadings.IsSeaInvasionThreat(in b, out bool landed) && landed, "landed within 3 days of being seen afloat");

        m.Update(List(Seen("Expedition:9", 1, 9f, 0f, 12000f, traits: EnemyTraits.OnOwnSoil)), 14, Drift(1f));
        AssertFalse(EnemyBeliefReadings.IsSeaInvasionThreat(Only(m, "inland"), out _), "4 days ashore: an army like any other");
    }

    private static void RaidThreatFromSeenCavalryOnOwnSoil()
    {
        var raid = new EnemyBelief { Traits = EnemyTraits.CavalryMajority | EnemyTraits.OnOwnSoil, Confidence01 = 1f, Strength = 2500f };
        AssertTrue(EnemyBeliefReadings.IsRaidThreat(in raid), "cavalry on our soil");
        var home = raid;
        home.Traits = EnemyTraits.CavalryMajority;
        AssertFalse(EnemyBeliefReadings.IsRaidThreat(in home), "cavalry on its own soil");
        var infantry = raid;
        infantry.Traits = EnemyTraits.OnOwnSoil;
        AssertFalse(EnemyBeliefReadings.IsRaidThreat(in infantry), "infantry is no raid");
        var expired = raid;
        expired.Confidence01 = 0f;
        AssertFalse(EnemyBeliefReadings.IsRaidThreat(in expired), "an expired belief is no threat");
    }

    private static void NoThreatFromUnseenInvasionForce()
    {
        // An invasion force vanilla has loaded (seainvasionforce) but no viewer has seen: not in the
        // model, so no belief can produce a threat.
        var m = new EnemyArmyModel();
        m.Update(List(Seen("Home Guard:2", 2, 0f, 0f, 3000f, traits: EnemyTraits.OnOwnSoil)), 1, Drift(1f));
        int threats = 0;
        foreach (var b in m.Beliefs)
        {
            var copy = b;
            if (EnemyBeliefReadings.IsSeaInvasionThreat(in copy, out _) || EnemyBeliefReadings.IsRaidThreat(in copy)) threats++;
        }
        AssertEqual(0, threats, "no threat without a seen force");
    }

    // The "vanilla collapsed" signal becomes belief expiry: a sea-invasion belief that expired
    // this review collapses the threat it produced the review before (Recovered posture).
    private static void ExpiredSeaInvasionBeliefCollapsesThreat()
    {
        var previous = new HashSet<string> { DefenseThreatSignature.ForSeaInvasion("Expedition:9") };
        var into = new List<string>();
        EnemyBeliefReadings.CollapsedSeaInvasionKeys(new[] { "Expedition:9", "Raider:3" }, previous, into);
        AssertEqual("Expedition:9", string.Join(",", into), "only the expired key that produced a threat");

        var src = new DefenseThreatSource { Kind = DefenseThreatSourceKind.SeaInvasion, EnemyUnitKey = "Expedition:9", BeliefExpired = true };
        var input = new DefenseIntentInput { AllianceId = 1 };
        input.Threats.Add(src);
        var output = DefenseIntentLedger.Build(input);
        AssertEqual(DefensePosture.Recovered, output.Responses[0].Threat.Posture, "Recovered");
        AssertEqual("sif:Expedition:9", output.Responses[0].Threat.Signature, "signature keyed on the belief's UnitKey");
    }

    // Vanilla GetStrengthRatio (12366-12404) on beliefs: own strength from own knowledge, enemy
    // from confidence-weighted beliefs (no enemy recruitment pool, no chapter-0 neutrals).
    private static void StrengthRatioFromBeliefs()
    {
        var m = SeenAndUnseen(out float seen);
        AssertNear(60000f / seen, EnemyBeliefReadings.StrengthRatio(60000f, m.Beliefs), 1e-4f, "own / believed");
        AssertNear(5000f, EnemyBeliefReadings.StrengthRatio(5000f, new List<EnemyBelief>()), 1e-3f, "nothing believed: own / 1 (vanilla's Max(1, enemy))");
        m.Update(List(), 7, Drift(1f));
        AssertNear(60000f / (seen * 0.5f), EnemyBeliefReadings.StrengthRatio(60000f, m.Beliefs), 1e-2f, "confidence-weighted");
    }

    private static void CapitalSamplesFromBeliefsCarryNoMorale()
    {
        var m = SeenAndUnseen(out float seen);
        var samples = new List<CapitalForceSample>();
        EnemyBeliefReadings.CapitalSamples(m.Beliefs, 10f, 30f, samples);
        AssertEqual(1, samples.Count, "one believed army");
        AssertNear(30f, samples[0].Distance, 1e-4f, "distance from the believed position");
        AssertNear(seen, samples[0].Strength, 1e-3f, "confidence-weighted strength");
        AssertNear(1f, samples[0].Morale, 1e-6f, "a sighting shows no morale: min(str, str x 1) = str");
        AssertTrue(CampaignResponsivenessSignals.EnemyThreatensCapital(samples, new List<CapitalForceSample>(), 100f, 1000f),
            "a believed army near the capital threatens it");
    }

    // ---- Source pins (BattleWriteGateTests style) ----

    private static void DetectionWiringPinned()
    {
        string planner = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string review = MethodBody(planner, "internal static void ReviewPlan(");
        int detect = review.IndexOf("EnemyDetectionRuntime.Detect(snapshot, GameVars.playeralliance, EnemyDetectionRuntime.FogHonestFlagsOn())", StringComparison.Ordinal);
        int update = review.IndexOf(".Update(snapshot.Detections, snapshot.DaySerial, EnemyDetectionRuntime.DriftFor(alliance))", StringComparison.Ordinal);
        int copy = review.IndexOf("snapshot.Beliefs.AddRange(", StringComparison.Ordinal);
        int report = review.IndexOf("EnemyDetectionRuntime.Report(snapshot, beliefs);", StringComparison.Ordinal);
        int bookkeeping = review.IndexOf("mode == PlannerReviewMode.Bookkeeping) return;", StringComparison.Ordinal);
        AssertTrue(detect >= 0 && update > detect && copy > update && report > copy && bookkeeping > report,
            "detect -> update -> copy -> [EnemyBeliefs], before the bookkeeping return (beliefs and their rows continue during the pause)");

        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs");
        string probe = MethodBody(runtime, "private static bool GridVisible(");
        AssertTrue(probe.IndexOf("GameVars.usefow", StringComparison.Ordinal) >= 0
                   && probe.IndexOf("GameVars.usefow", StringComparison.Ordinal) < probe.IndexOf("UnitIsHidden(", StringComparison.Ordinal),
            "usefow gated before UnitIsHidden (101188 NRE)");
        string detectBody = MethodBody(runtime, "internal static void Detect(");
        AssertContains(detectBody, "DetectionRules.RunMasked(", "the grid probes run inside the incident mask");
        AssertContains(detectBody, "DetectionRules.MasksPlayerIntelIncidents(", "mask only for rule (b)");
        AssertContains(runtime, "DetectionRules.ObjectiveGarrison(", "garrison detections only through the shared rule");
        string code = CodeOnly(runtime);
        AssertFalse(code.Contains("GetLastTransmittedPathPos"), "never an enemy destination");
        AssertFalse(code.Contains("seainvasionforce") || code.Contains("raidforces"), "never enemy plan state");

        // The only omniscient read is the flags-off rollback.
        int reads = 0;
        for (int at = runtime.IndexOf("\"enemyunits\"", StringComparison.Ordinal); at >= 0; at = runtime.IndexOf("\"enemyunits\"", at + 1, StringComparison.Ordinal)) reads++;
        AssertEqual(1, reads, "one enemyunits read");
        AssertContains(MethodBody(runtime, "private static IReadOnlyList<EnemyBelief> OmniscientBeliefs("), "\"enemyunits\"", "inside the rollback builder");
        AssertContains(MethodBody(runtime, "internal static IReadOnlyList<EnemyBelief> BeliefsFor("), "Plugin.EnableCampaignFogHonestBeliefs", "fallback decided by the flags");
    }

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
