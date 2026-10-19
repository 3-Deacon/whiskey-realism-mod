using System;
using System.Collections.Generic;
using System.Linq;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;

using static TestHarness;
using static TestFixtures;

// Stage G strategic foresight, slice F6b-1 (D11 requirements covering-post and covering-size's axis shares; decision 4): the covering
// corridor between the two capitals. Pure geometry: the two points at the relocation range R from each capital on the axis, the own-held town
// each point gives as a post, and the part of each sector's hold need that is held against enemy elements on the axis. No formation is taken;
// the corridor is reported (`[CoveringCorridor]`) so the posts the rule gives are read on a save before F6b-2 moves anyone. Plan:
// docs/superpowers/plans/2026-10-18-strategic-foresight.md F6b-1; cases TC-COV-* in docs/loop/foresight/test-plan.md.
//
// FX-002A (the investigation's 002/A geometry, EnemyPictureTests): Richmond 1263.6,-1010.9; Washington 1349.9,-631.6; relocation range 200.
// The places are the TOWNS (BattleUnits.towns, Town.Owner) of the save's IIPsTowns.dat (Campaigns/002/A/Save, the 2026-10-18 smoke copy), whose
// first number after the name is the type: 7 a town, 0 a small town (IIP.id_smalltowns = 0, decompile 103719, which BattleUnits.smalltowns
// holds apart from BattleUnits.towns, 78938). Towns (type 7): Richmond, Fredericksburg 1247.4,-802.6, Petersburg 1269.7,-1100.1 (CSA);
// Washington, Alexandria 1342.16,-664.53, Winchester 1106.93,-554.96 (Union). Small towns (type 0, never places): Manassas Junction
// 1245.13,-680.76, Fairfax C.H. 1298.3,-660.6, King George C.H. 1326.2,-818.6, Gordonsville, Romney, Yorktown. The owners at 1861-06 are
// ASSUMED (the save holds them on the live Town objects): the CSA holds Richmond, Fredericksburg and Petersburg, the Union the rest. Fort
// Monroe is not in the file as such: it is placed 269 from Richmond on the bearing of the file's Hampton (1499.32,-1154.33), the distance
// D11 states for the Peninsula.
internal static partial class CoveringForceTests
{
    public static void Run()
    {
        TestHarness.RunAll(ForceCases());
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("PointsAreAtTheRelocationRangeFromEachCapital", PointsAreAtTheRelocationRangeFromEachCapital),
            ("PointsClampToTheSegment", PointsClampToTheSegment),
            ("ASectorsRadiusIsTheFarthestAreaFromItsCentroid", ASectorsRadiusIsTheFarthestAreaFromItsCentroid),
            ("PostIsTheNearestOwnHeldTownWithinRange", PostIsTheNearestOwnHeldTownWithinRange),
            ("APostWithinExactlyTheRangeCounts", APostWithinExactlyTheRangeCounts),
            ("ATieGoesToTheLowerPlaceIndex", ATieGoesToTheLowerPlaceIndex),
            ("OneTownForBothPointsIsOnePost", OneTownForBothPointsIsOnePost),
            ("SmallTownsAreNeverPosts", SmallTownsAreNeverPosts),
            ("TheCapitalsAreNeverPosts", TheCapitalsAreNeverPosts),
            ("NoTownWithinRangeGivesNoPost", NoTownWithinRangeGivesNoPost),
            ("AxisShareCountsOnlyElementsOnTheAxis", AxisShareCountsOnlyElementsOnTheAxis),
            ("APeninsulaSectorHasNoAxisShare", APeninsulaSectorHasNoAxisShare),
            ("AnAxisShareNeverExceedsTheSectorsNeed", AnAxisShareNeverExceedsTheSectorsNeed),
            ("AnAxisShareIsTheHoldRatioTimesTheWeightedMassOnTheAxis", AnAxisShareIsTheHoldRatioTimesTheWeightedMassOnTheAxis),
            ("TheCorridorRowNamesTheCandidatesPostsAndAxis", TheCorridorRowNamesTheCandidatesPostsAndAxis),
            ("TheCorridorRowQuotesEveryName", TheCorridorRowQuotesEveryName),
            ("NoCorridorWithoutBothCapitals", NoCorridorWithoutBothCapitals),
            ("TheCorridorRowIsRegisteredInTheTagPolicy", TheCorridorRowIsRegisteredInTheTagPolicy),
            ("TheRuntimeReadsOnlyTownsAndWritesTheRowOnChange", TheRuntimeReadsOnlyTownsAndWritesTheRowOnChange),
        });
    }

    // ---- fixtures ----

    private static PlaceRecord Place(string name, float x, float z, int owner)
    {
        return new PlaceRecord { Name = name, X = x, Z = z, Owner = owner };
    }

    // The towns of FX-002A (the header), in the order BattleUnits.towns lists them: no order is claimed by the save, this one is the test's.
    private static void AddTowns(CampaignSnapshot s)
    {
        s.Places.Add(Place("Richmond", EnemyPictureTests.RichmondX, EnemyPictureTests.RichmondZ, 1));
        s.Places.Add(Place("Washington", EnemyPictureTests.WashingtonX, EnemyPictureTests.WashingtonZ, 0));
        s.Places.Add(Place("Fredericksburg", EnemyPictureTests.FredericksburgX, EnemyPictureTests.FredericksburgZ, 1));
        s.Places.Add(Place("Alexandria", 1342.16f, -664.53f, 0));
        s.Places.Add(Place("Winchester", EnemyPictureTests.WinchesterX, EnemyPictureTests.WinchesterZ, 0));
        s.Places.Add(Place("Petersburg", 1269.7f, -1100.1f, 1));
    }

    private static int IndexOfPlace(CampaignSnapshot s, string name)
    {
        for (int i = 0; i < s.Places.Count; i++)
            if (s.Places[i].Name == name) return i;
        throw new Exception("no place " + name);
    }

    // The CSA's snapshot (EnemyPictureTests.Csa) with the towns.
    private static CampaignSnapshot Csa()
    {
        var s = EnemyPictureTests.Csa(100000f);
        AddTowns(s);
        return s;
    }

    // The Union's snapshot of the same map: its capital Washington, the enemy's Richmond.
    private static CampaignSnapshot Union()
    {
        var s = new CampaignSnapshot();
        s.Reset(0, 0, ExecutorFixtures.Day, PlayerCommandClass.NotCareer, -1, null);
        s.RelocationRange = 200f;
        s.OffensiveDominance = 1.1f;
        s.MinStrengthForMovement = 2000f;
        s.MinMoraleForMovement = 0.3f;
        s.HasCapital = true;
        s.CapitalX = EnemyPictureTests.WashingtonX;
        s.CapitalZ = EnemyPictureTests.WashingtonZ;
        s.HasEnemyCapital = true;
        s.EnemyCapitalX = EnemyPictureTests.RichmondX;
        s.EnemyCapitalZ = EnemyPictureTests.RichmondZ;
        s.EnemyCapitalName = "Richmond";
        AddTowns(s);
        return s;
    }

    private static float Dist(float ax, float az, float bx, float bz)
    {
        float dx = ax - bx, dz = az - bz;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    // ---- the points ----

    private static void PointsAreAtTheRelocationRangeFromEachCapital()
    {
        var s = Csa();
        CoveringForce.Points(s, out float fx, out float fz, out float rx, out float rz);
        float length = Dist(s.CapitalX, s.CapitalZ, s.EnemyCapitalX, s.EnemyCapitalZ);
        AssertNear(389.0f, length, 0.5f, "the axis of 002/A is 389 long");
        AssertNear(200f, Dist(fx, fz, s.EnemyCapitalX, s.EnemyCapitalZ), 0.05f, "the forward point is R from the enemy capital");
        AssertNear(200f, Dist(rx, rz, s.CapitalX, s.CapitalZ), 0.05f, "the rear point is R from the own capital");
        AssertNear(length, Dist(s.CapitalX, s.CapitalZ, fx, fz) + Dist(fx, fz, s.EnemyCapitalX, s.EnemyCapitalZ), 0.05f, "the forward point lies on the axis");
        AssertNear(length, Dist(s.CapitalX, s.CapitalZ, rx, rz) + Dist(rx, rz, s.EnemyCapitalX, s.EnemyCapitalZ), 0.05f, "so does the rear point");
        AssertNear(189f, Dist(fx, fz, s.CapitalX, s.CapitalZ), 0.6f, "an axis under 2R puts the forward point 189 from Richmond, nearer it than the rear point (D11)");
        AssertTrue(Dist(fx, fz, s.CapitalX, s.CapitalZ) < Dist(rx, rz, s.CapitalX, s.CapitalZ), "the forward point is the nearer to the own capital, accepted");
        AssertNear(1306f, fx, 3f, "about (1306, -821), as D11 says");
        AssertNear(-821f, (fz + rz) / 2f, 8f, "of both points");
    }

    private static void PointsClampToTheSegment()
    {
        var s = new CampaignSnapshot();
        s.Reset(1, 1, ExecutorFixtures.Day, PlayerCommandClass.NotCareer, -1, null);
        s.RelocationRange = 200f;
        s.HasCapital = true;
        s.CapitalX = 0f;
        s.CapitalZ = 0f;
        s.HasEnemyCapital = true;
        s.EnemyCapitalX = 100f;
        s.EnemyCapitalZ = 0f;
        CoveringForce.Points(s, out float fx, out float fz, out float rx, out float rz);
        AssertNear(0f, fx, 0.001f, "R from the enemy capital is past the own capital: the forward point stops at the own capital");
        AssertNear(100f, rx, 0.001f, "R from the own capital is past the enemy capital: the rear point stops at the enemy capital");
        AssertEqual(0f, fz + rz, "on the axis");
        s.EnemyCapitalX = 0f;
        CoveringForce.Points(s, out fx, out fz, out rx, out rz);
        AssertTrue(fx == 0f && fz == 0f && rx == 0f && rz == 0f, "capitals on one spot: both points are there, no division by the length");
    }

    // The sector's reach is this radius plus R (the sector facts and the axis shares read it): the farthest an area's centre lies from the centroid.
    private static void ASectorsRadiusIsTheFarthestAreaFromItsCentroid()
    {
        var s = ExecutorFixtures.Wide();   // seven areas on a line at x 0..600, three sectors {0,100,200} {300,400} {500,600}
        var sectors = ExecutorFixtures.Sectors(s);
        AssertEqual(3, sectors.Count, "three sectors");
        AssertNear(100f, sectors.Radius(sectors.SectorAt(100f, 0f)), 0.001f, "centroid 100, the areas 100 either side");
        AssertNear(50f, sectors.Radius(sectors.SectorAt(350f, 0f)), 0.001f, "centroid 350, areas at 300 and 400");
        AssertEqual(0f, sectors.Radius(-1), "no sector, no radius");
        AssertEqual(0f, sectors.Radius(99), "nor one past the last");
    }

    // ---- the posts ----

    private static void PostIsTheNearestOwnHeldTownWithinRange()
    {
        var s = Csa();
        CoveringForce.Points(s, out float fx, out float fz, out float rx, out float rz);
        int fredericksburg = IndexOfPlace(s, "Fredericksburg");
        AssertEqual(fredericksburg, CoveringForce.PostNear(s, fx, fz), "Fredericksburg, 63 from the forward point, is the CSA's forward post");
        AssertEqual(fredericksburg, CoveringForce.PostNear(s, rx, rz), "and its rear post");
        AssertNear(63f, Dist(s.Places[fredericksburg].X, s.Places[fredericksburg].Z, fx, fz), 1f, "(D11: 63 from the point)");
        // Petersburg (CSA) is 276 from the forward point: out of range, and the Union's Alexandria and Winchester are not its own.
        s.Places[fredericksburg] = Place("Fredericksburg", EnemyPictureTests.FredericksburgX, EnemyPictureTests.FredericksburgZ, 0);
        AssertEqual(-1, CoveringForce.PostNear(s, fx, fz), "with Fredericksburg lost the CSA has no town within R of the point");
    }

    private static void APostWithinExactlyTheRangeCounts()
    {
        var s = Csa();
        s.Places.Clear();
        s.Places.Add(Place("Edge", 1000f + 200f, 0f, 1));
        AssertEqual(0, CoveringForce.PostNear(s, 1000f, 0f), "within is <=: a town exactly R away is a post");
        s.Places[0] = Place("Edge", 1000f + 200.5f, 0f, 1);
        AssertEqual(-1, CoveringForce.PostNear(s, 1000f, 0f), "and one half a unit farther is not");
    }

    private static void ATieGoesToTheLowerPlaceIndex()
    {
        var s = Csa();
        s.Places.Clear();
        s.Places.Add(Place("North", 1000f, 50f, 1));
        s.Places.Add(Place("South", 1000f, -50f, 1));
        AssertEqual(0, CoveringForce.PostNear(s, 1000f, 0f), "equidistant towns: the lower place index");
        s.Places.Reverse();
        AssertEqual(0, CoveringForce.PostNear(s, 1000f, 0f), "whichever of them that is");
    }

    // The 002/A axis is about 2R, so both points fall near (1306, -821) and the rule gives one post for each side (D11): Fredericksburg for the
    // CSA, Alexandria for the Union (about 160 from the point).
    private static void OneTownForBothPointsIsOnePost()
    {
        var csa = Csa();
        CoveringForce.Points(csa, out float fx, out float fz, out float rx, out float rz);
        AssertEqual(CoveringForce.PostNear(csa, fx, fz), CoveringForce.PostNear(csa, rx, rz), "the CSA's two points give one town");
        var union = Union();
        CoveringForce.Points(union, out fx, out fz, out rx, out rz);
        int forward = CoveringForce.PostNear(union, fx, fz), rear = CoveringForce.PostNear(union, rx, rz);
        AssertEqual(IndexOfPlace(union, "Alexandria"), forward, "the Union's forward post");
        AssertEqual(forward, rear, "and its rear post: one town");
        AssertNear(160f, Dist(union.Places[forward].X, union.Places[forward].Z, fx, fz), 8f, "about 160 from the point (D11: 161)");
    }

    // The small towns of 002/A (Manassas Junction 116 from Washington, King George C.H. 21 from the point) are IIPs in BattleUnits.smalltowns,
    // which the snapshot does not read: the fixture's places hold none of them, so the nearer King George is not offered and Fredericksburg is
    // the post. The pin below is the other half: the runtime reads BattleUnits.towns and no small-town list.
    private static void SmallTownsAreNeverPosts()
    {
        var s = Csa();
        CoveringForce.Points(s, out float fx, out float fz, out _, out _);
        AssertTrue(Dist(1326.2f, -818.6f, fx, fz) < 25f, "King George C.H. (a small town) is within 25 of the forward point, nearer than Fredericksburg's 63");
        AssertTrue(!s.Places.Any(p => p.Name == "King George C.H." || p.Name == "Manassas Junction"), "yet the snapshot offers no small town");
        AssertEqual(IndexOfPlace(s, "Fredericksburg"), CoveringForce.PostNear(s, fx, fz), "so the post is the town");
        string runtime = FindSource("Strategic/Planner/CampaignSnapshotRuntime.cs");
        string read = BodyOf(runtime, "private static void ReadPlaces(");
        AssertTrue(read.Contains("BattleUnits.towns"), "the places are BattleUnits.towns");
        AssertFalse(read.Contains("smalltowns") || read.Contains("iips") || read.Contains("harbors"), "no small-town, IIP or harbor list is read as a place");
    }

    private static void TheCapitalsAreNeverPosts()
    {
        var s = Csa();
        CoveringForce.Points(s, out float fx, out float fz, out float rx, out float rz);
        AssertTrue(Dist(rx, rz, s.CapitalX, s.CapitalZ) <= s.RelocationRange + 0.1f, "Richmond, the own capital and an own-held town, is within R of the rear point");
        s.Places.RemoveAt(IndexOfPlace(s, "Fredericksburg"));
        AssertEqual(-1, CoveringForce.PostNear(s, rx, rz), "yet it is no post: the capital is excluded");
        AssertEqual(-1, CoveringForce.PostNear(s, fx, fz), "from either point");
        s.Places.Add(Place("Beside Richmond", s.CapitalX + 0.5f, s.CapitalZ, 1));
        AssertEqual(-1, CoveringForce.PostNear(s, rx, rz), "a town within SameTargetRadius of the capital is the capital");
    }

    private static void NoTownWithinRangeGivesNoPost()
    {
        var s = Csa();
        s.Places.Clear();
        s.Places.Add(Place("Far", 5000f, 5000f, 1));
        CoveringForce.Points(s, out float fx, out float fz, out _, out _);
        AssertEqual(-1, CoveringForce.PostNear(s, fx, fz), "nothing within R");
        s.Places.Clear();
        AssertEqual(-1, CoveringForce.PostNear(s, fx, fz), "no places at all");
    }

    // ---- the axis shares ----

    // FX-002A with beliefs: the Union's 2nd Division at Winchester (6653) lies about 254 from the axis segment, off the R band; the Department
    // of Virginia (5214) sits at the Peninsula's Fort Monroe, 269 from Richmond and off the band; the Washington anchor lies on the axis.
    private static void WorldWithBeliefs(out CampaignSnapshot s, out SectorModel sectors, out EnemyPicture picture, out TheaterPlan plan)
    {
        s = Csa();
        s.Areas.Add(new AreaRecord { Index = 7, X = 1493.3f, Z = -1150.7f, EdgeX = new[] { 1493.3f }, EdgeZ = new[] { -1150.7f }, Importance = 1f, SupplyCapacity = 100f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 23, X = 1493.3f, Z = -1150.7f, HolderAlliance = 0, Available = true, Importance01 = 0.3f });
        s.Beliefs.Add(EnemyPictureTests.Belief(EnemyPictureTests.WinchesterX, EnemyPictureTests.WinchesterZ, 6653f));
        s.Beliefs.Add(EnemyPictureTests.Belief(1493.3f, -1150.7f, 5214f));
        sectors = SectorModel.Build(s);
        plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _, out picture);
    }

    private static float[] NeedsOf(TheaterPlan plan, SectorModel sectors)
    {
        var need = new float[sectors.Count];
        foreach (var sp in plan.Sectors) need[sp.SectorId] = sp.HoldNeed;
        return need;
    }

    private static void AxisShareCountsOnlyElementsOnTheAxis()
    {
        WorldWithBeliefs(out var s, out var sectors, out var picture, out var plan);
        var need = NeedsOf(plan, sectors);
        var share = new float[sectors.Count];
        float ratio = TheaterPlanner.HoldRatioNow(s);
        CoveringForce.AxisShares(s, sectors, picture, need, ratio, null, share);
        int washington = sectors.SectorAt(EnemyPictureTests.WashingtonX, EnemyPictureTests.WashingtonZ);
        int winchester = sectors.SectorAt(EnemyPictureTests.WinchesterX, EnemyPictureTests.WinchesterZ);
        AssertTrue(need[washington] > 0f, "Washington's sector has a hold need");
        AssertTrue(share[washington] > 0f, "and holds against the anchor on the axis: it is an axis sector");
        AssertTrue(share[washington] <= need[washington] + 0.01f, "its axis share is part of its need");
        AssertTrue(need[winchester] > 0f && winchester != washington, "Winchester's sector is another, with a need");
        // The 2nd Division at Winchester is 254 from the axis segment (past R); the anchors that sector holds against are Winchester's own and
        // Romney's, also off the band, so its share is 0 whatever its need.
        AssertEqual(0f, share[winchester], "an army off the axis gives its sector no axis share");
        // Put that army on the axis (at Fredericksburg's latitude, between the capitals): the same sector's share is then its weighted mass.
        s.Beliefs.Clear();
        s.Beliefs.Add(EnemyPictureTests.Belief(1306f, -821f, 6653f));
        var movedPlan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _, out var movedPicture);
        var movedNeed = NeedsOf(movedPlan, sectors);
        var movedShare = new float[sectors.Count];
        CoveringForce.AxisShares(s, sectors, movedPicture, movedNeed, ratio, null, movedShare);
        int onAxis = sectors.SectorAt(1306f, -821f);
        AssertTrue(movedShare[onAxis] > 0f, "an army on the axis gives the sector it threatens an axis share");
        AssertTrue(movedShare[onAxis] <= movedNeed[onAxis] + 0.01f, "never above its need");
    }

    private static void APeninsulaSectorHasNoAxisShare()
    {
        WorldWithBeliefs(out var s, out var sectors, out var picture, out var plan);
        var need = NeedsOf(plan, sectors);
        var share = new float[sectors.Count];
        CoveringForce.AxisShares(s, sectors, picture, need, TheaterPlanner.HoldRatioNow(s), null, share);
        int peninsula = sectors.SectorAt(1493.3f, -1150.7f);
        sectors.Centroid(peninsula, out float cx, out float cz);
        CoveringForce.Points(s, out float fx, out float fz, out float rx, out float rz);
        AssertTrue(need[peninsula] > 0f, "the Peninsula sector holds against the Department of Virginia");
        AssertTrue(Dist(cx, cz, s.CapitalX, s.CapitalZ) <= s.RelocationRange + 200f, "its centroid lies close to Richmond");
        AssertEqual(0f, share[peninsula], "yet no element of its need is within R of the axis segment: it has no axis share and gives nothing");
    }

    private static void AnAxisShareNeverExceedsTheSectorsNeed()
    {
        WorldWithBeliefs(out var s, out var sectors, out var picture, out var plan);
        var need = NeedsOf(plan, sectors);
        var tiny = (float[])need.Clone();
        for (int k = 0; k < tiny.Length; k++) tiny[k] = Math.Min(tiny[k], 100f);
        var share = new float[sectors.Count];
        CoveringForce.AxisShares(s, sectors, picture, tiny, TheaterPlanner.HoldRatioNow(s), null, share);
        for (int k = 0; k < share.Length; k++)
            AssertTrue(share[k] <= tiny[k] + 0.001f, "sector " + k + ": the share (" + share[k] + ") is capped by its need (" + tiny[k] + ")");
        share[0] = 123f;
        CoveringForce.AxisShares(s, sectors, picture, new float[sectors.Count], 0.6f, null, share);
        AssertTrue(share.All(v => v == 0f), "with no hold need anywhere every share is 0, and the array is overwritten, not added to");
    }

    // The enemy's total is unknown, so the picture spreads no men over the anchors: the one army, 6653 on the axis at Fredericksburg's latitude,
    // is all the sector holds against. Its weight is min(1, reach / distance to the centroid), reach the sector's radius plus R (no river: the
    // probe is null), and the share 0.6 x 6653 x that: the sector's need is the same 0.6 x the army (the Washington anchor's floor, 4000, is not the larger).
    private static void AnAxisShareIsTheHoldRatioTimesTheWeightedMassOnTheAxis()
    {
        var s = EnemyPictureTests.Csa(float.NaN);
        AddTowns(s);
        s.Beliefs.Add(EnemyPictureTests.Belief(1306f, -821f, 6653f));
        var sectors = SectorModel.Build(s);
        var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _, out var picture);
        var need = NeedsOf(plan, sectors);
        var share = new float[sectors.Count];
        float ratio = TheaterPlanner.HoldRatioNow(s);
        CoveringForce.AxisShares(s, sectors, picture, need, ratio, null, share);
        int k = sectors.SectorAt(1306f, -821f);
        sectors.Centroid(k, out float cx, out float cz);
        float reach = sectors.Radius(k) + s.RelocationRange;
        float distance = Dist(1306f, -821f, cx, cz);
        AssertTrue(distance <= reach, "the army is inside the sector's reach");
        float weight = distance > reach ? reach / distance : 1f;
        float expected = ratio * 6653f * weight;
        AssertTrue(need[k] >= expected - 0.5f, "the sector's need (" + need[k] + ") is the same 0.6 x the army, never below the share");
        AssertNear(expected, share[k], 0.5f, "the share is the hold ratio x the army x its weight");
        for (int j = 0; j < share.Length; j++)
            if (j != k) AssertTrue(share[j] <= need[j] + 0.01f, "no other sector gives more than its need");
    }

    // ---- the row ----

    private static void TheCorridorRowNamesTheCandidatesPostsAndAxis()
    {
        WorldWithBeliefs(out var s, out var sectors, out var picture, out var plan);
        string row = CoveringForce.CorridorRow(s, sectors, picture, plan, null);
        AssertTrue(row != null, "both capitals exist: a row");
        AssertTrue(row.StartsWith("[CoveringCorridor] alliance=1 ", StringComparison.Ordinal), "alliance first: " + row);
        AssertContains(row, " forward=Fredericksburg rear=Fredericksburg ", "one post for the CSA");
        // Fredericksburg: 63 from the forward point, about 70 from the rear point, 209 from Richmond, 199 from Washington (D11's smoke row).
        AssertContains(row, "Fredericksburg:63:", "the candidate carries its distance to the forward point first");
        AssertContains(row, ":209:199", "then its distances to the own and the enemy capital");
        AssertFalse(row.Contains("Winchester") || row.Contains("Alexandria"), "the Union's towns are not candidates");
        AssertFalse(row.Contains("Petersburg"), "nor a town beyond R of both points");
        AssertFalse(row.Contains("Richmond:"), "nor the capital");
        int washington = sectors.SectorAt(EnemyPictureTests.WashingtonX, EnemyPictureTests.WashingtonZ);
        AssertContains(row, " axis=" + washington, "the axis names the sector facing Washington's anchor");
        var union = Union();
        union.Beliefs.Add(EnemyPictureTests.Belief(1300f, -900f, 8000f));
        union.Areas.AddRange(EnemyPictureTests.Csa().Areas);
        var unionSectors = SectorModel.Build(union);
        var unionPlan = TheaterPlanner.Plan(union, unionSectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _, out var unionPicture);
        string unionRow = CoveringForce.CorridorRow(union, unionSectors, unionPicture, unionPlan, null);
        AssertContains(unionRow, "[CoveringCorridor] alliance=0 ", "the Union has a row too");
        AssertContains(unionRow, " forward=Alexandria rear=Alexandria ", "with its own post");
        AssertContains(unionRow, "Alexandria:", "and the candidate");
    }

    // A place with a space in its name is written quoted, in the candidate list and as a post, so the parser keeps it whole.
    private static void TheCorridorRowQuotesEveryName()
    {
        var s = Csa();
        s.Places[IndexOfPlace(s, "Fredericksburg")] = Place("Fort Frederick Town", EnemyPictureTests.FredericksburgX, EnemyPictureTests.FredericksburgZ, 1);
        var sectors = SectorModel.Build(s);
        var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _, out var picture);
        string row = CoveringForce.CorridorRow(s, sectors, picture, plan, null);
        AssertContains(row, " forward=\"Fort Frederick Town\" rear=\"Fort Frederick Town\" ", "the posts are quoted");
        AssertContains(row, "candidates=\"Fort Frederick Town:", "and so is the list that holds the name");
        AssertTrue(TelemetryLegacyParser.TryParse(row + " date=1861-06-23", TelemetryProfile.CampaignTuning, "s", out var ev), "the row parses");
        AssertEqual("Fort Frederick Town", ev.Fields.GetString("forward"), "with the post whole");
    }

    private static void NoCorridorWithoutBothCapitals()
    {
        var s = Csa();
        var sectors = SectorModel.Build(s);
        var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _, out var picture);
        s.HasEnemyCapital = false;
        AssertTrue(CoveringForce.CorridorRow(s, sectors, picture, plan, null) == null, "no enemy capital, no axis, no row");
        s.HasEnemyCapital = true;
        s.HasCapital = false;
        AssertTrue(CoveringForce.CorridorRow(s, sectors, picture, plan, null) == null, "no own capital either");
        s.HasCapital = true;
        s.Places.Clear();
        string none = CoveringForce.CorridorRow(s, sectors, picture, plan, null);
        AssertContains(none, " candidates=- forward=none rear=none ", "both capitals but no town within R of either point: the row says none");
    }

    private static void TheCorridorRowIsRegisteredInTheTagPolicy()
    {
        var routed = TelemetryTagPolicy.Route("[CoveringCorridor] alliance=1 date=1861-06-23 candidates=- forward=none rear=none axis=none");
        AssertEqual("CoveringCorridor", routed.EventName, "the router knows the corridor row");
        AssertEqual(TelemetryCategory.State, routed.Category, "it is State");
        AssertEqual(TelemetryLayer.Campaign, routed.Layer, "on the campaign layer");
    }

    // The places are read in the snapshot (BattleUnits.towns: Name, position, Owner) and the row is written in the Plan job after the plan:
    // on the first review and on change, never twice for one signature.
    private static void TheRuntimeReadsOnlyTownsAndWritesTheRowOnChange()
    {
        string snapshot = FindSource("Strategic/Planner/CampaignSnapshotRuntime.cs");
        string build = BodyOf(snapshot, "private static CampaignSnapshot BuildCore(");
        AssertTrue(build.Contains("ReadPlaces(s);"), "the snapshot build reads the places");
        string read = BodyOf(snapshot, "private static void ReadPlaces(");
        AssertTrue(read.Contains("town.Owner") && read.Contains("town.CityName"), "a place is a town's owner and name");
        string runtime = FindSource("Strategic/Planner/CampaignPlannerRuntime.cs");
        string emit = BodyOf(runtime, "private static void EmitCorridor(");
        AssertTrue(emit.Contains("CoveringForce.CorridorRow(") && emit.Contains("LastCorridorRow[a]"), "the row is built from CoveringForce and compared with the last one");
        AssertTrue(emit.Contains("ReviewCadenceCatchUp.Dated(row, plan.DaySerial)"), "and written dated, after the compare");
        string review = BodyOf(runtime, "internal static void ReviewPlan(");
        AssertTrue(review.IndexOf("EmitPicture(snapshot, plan, picture);", StringComparison.Ordinal) < review.IndexOf("EmitCorridor(", StringComparison.Ordinal)
            && review.IndexOf("EmitCorridor(", StringComparison.Ordinal) < review.IndexOf("EmitAllocation(", StringComparison.Ordinal),
            "in the Plan job, after the picture row and before the allocation row");
        AssertTrue(BodyOf(runtime, "private static void ResetPlanState(").Contains("LastCorridorRow[a] = null;"), "the last row is a campaign's, reset with it");
    }

    private static string FindSource(string relative)
    {
        return System.IO.File.ReadAllText(FindRepoPath("src/WhiskeyRealism/" + relative));
    }
}
