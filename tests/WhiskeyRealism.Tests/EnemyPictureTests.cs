using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;

using static TestHarness;
using static TestFixtures;

// Stage G strategic foresight, slice F3 (D11 requirements enemy-total, picture-partition, hold-need, hold-release,
// candidate-floor-capacity, capital-window, capital-floor): the enemy picture (every man of the enemy's public troopsfielded placed once:
// believed, faded, or at an anchor), the per-anchor presumption that replaces D2's per-sector switch, the hold need and release that read
// it, and the capital threat by march time with vanilla's baseline. Plan: docs/superpowers/plans/2026-10-18-strategic-foresight.md F3;
// cases TC-PIC-*, TC-HOLD-*, TC-REL-*, TC-ELIG-*, TC-CAP-* in docs/loop/foresight/test-plan.md.
//
// FX-002A: the investigation's 002/A geometry. Richmond (the CSA's capital) 1263.6,-1010.9; Washington 1349.9,-631.6; Manassas Junction
// 1245.1,-680.8; Fredericksburg 1247.4,-802.6; Bowling Green 1285.0,-874.6; Winchester 1107,-555; Romney 982.6,-515.7; relocation range 200.
// Areas sit at those places, objectives seed the sectors (Richmond and Fredericksburg are the CSA's own, Washington, Winchester
// and Romney the Union's); Manassas has no objective of its own, so it grows into the sector of the nearest seed, Washington's. All six places classify as the East theater
// (TheaterClassifier), so a West department is a deliberate transfer (PlannerState.Transfer), as the planner's own is.
internal static partial class EnemyPictureTests
{
    // ---- fixtures ----

    internal const float RichmondX = 1263.6f, RichmondZ = -1010.9f, WashingtonX = 1349.9f, WashingtonZ = -631.6f;
    internal const float ManassasX = 1245.1f, ManassasZ = -680.8f, FredericksburgX = 1247.4f, FredericksburgZ = -802.6f;
    internal const float BowlingGreenX = 1285.0f, BowlingGreenZ = -874.6f, WinchesterX = 1107f, WinchesterZ = -555f, RomneyX = 982.6f, RomneyZ = -515.7f;

    private static AreaRecord Area(int index, float x, float z)
    {
        return new AreaRecord { Index = index, X = x, Z = z, EdgeX = new[] { x }, EdgeZ = new[] { z }, Importance = 1f, SupplyCapacity = 100f };
    }

    private static ObjectiveRecord Objective(int id, float x, float z, int holder, float importance, ObjectiveRole roles = ObjectiveRole.None)
    {
        return new ObjectiveRecord { ObjectiveId = id, X = x, Z = z, HolderAlliance = holder, Available = holder != 1, Importance01 = importance, Roles = roles };
    }

    // The CSA (alliance 1) on 002/A: area and objective indexes 0 Richmond, 1 Bowling Green, 2 Fredericksburg, 3 Manassas, 4 Washington,
    // 5 Winchester, 6 Romney. `total` is the Union's troopsfielded (NaN: unknown).
    internal static CampaignSnapshot Csa(float total = float.NaN)
    {
        var s = new CampaignSnapshot();
        s.Reset(1, 1, ExecutorFixtures.Day, PlayerCommandClass.NotCareer, -1, null);
        s.RelocationRange = 200f;
        s.OffensiveDominance = 1.1f;
        s.MinStrengthForMovement = 2000f;
        s.MinMoraleForMovement = 0.3f;
        s.HasCapital = true;
        s.CapitalX = RichmondX;
        s.CapitalZ = RichmondZ;
        s.HasEnemyCapital = true;
        s.EnemyCapitalX = WashingtonX;
        s.EnemyCapitalZ = WashingtonZ;
        s.EnemyCapitalName = "Washington";
        s.EnemyTroopsFielded = total;
        s.Areas.Add(Area(0, RichmondX, RichmondZ));
        s.Areas.Add(Area(1, BowlingGreenX, BowlingGreenZ));
        s.Areas.Add(Area(2, FredericksburgX, FredericksburgZ));
        s.Areas.Add(Area(3, ManassasX, ManassasZ));
        s.Areas.Add(Area(4, WashingtonX, WashingtonZ));
        s.Areas.Add(Area(5, WinchesterX, WinchesterZ));
        s.Areas.Add(Area(6, RomneyX, RomneyZ));
        s.Objectives.Add(Objective(10, RichmondX, RichmondZ, 1, 1f, ObjectiveRole.Capital));
        s.Objectives.Add(Objective(11, FredericksburgX, FredericksburgZ, 1, 0.4f));
        s.Objectives.Add(Objective(20, WashingtonX, WashingtonZ, 0, 1f, ObjectiveRole.Capital));
        s.Objectives.Add(Objective(21, WinchesterX, WinchesterZ, 0, 0.5f));
        s.Objectives.Add(Objective(22, RomneyX, RomneyZ, 0, 0.3f));
        return s;
    }

    internal static FormationRecord Corps(int id, float x, float z, float strength)
    {
        var f = ExecutorFixtures.Corps(id, x, strength);
        f.Z = z;
        f.WaypointZ = z;
        return f;
    }

    internal static EnemyBelief Belief(float x, float z, float strength, float confidence = 1f, EnemyTraits traits = 0)
    {
        var b = ExecutorFixtures.Belief(x, z, strength);
        b.Confidence01 = confidence;
        b.Traits = traits;
        b.UnitKey = "union:" + (int)x + ":" + (int)z;
        return b;
    }

    // A scouted verdict from a place list: the anchor within 1 unit of one of them is scouted.
    private static Func<float, float, bool> ScoutedAt(params float[] xz)
    {
        return (x, z) =>
        {
            for (int i = 0; i + 1 < xz.Length; i += 2)
                if (Math.Abs(x - xz[i]) <= 1f && Math.Abs(z - xz[i + 1]) <= 1f) return true;
            return false;
        };
    }

    private static readonly Func<float, float, bool> NothingScouted = (x, z) => false;

    private static PictureAnchor AnchorAt(EnemyPicture p, float x, float z)
    {
        var a = p.Anchors.FirstOrDefault(k => Math.Abs(k.X - x) <= 1f && Math.Abs(k.Z - z) <= 1f);
        AssertTrue(a != null, "an anchor stands at " + x + "," + z + ": " + string.Join("; ", p.Anchors.Select(k => k.Label + "@" + k.X + "," + k.Z)));
        return a;
    }

    private static float Sum(EnemyPicture p) => p.Believed + p.Faded + p.Unaccounted + p.Unplaced;

    // ---- the picture ----

    private static void EveryManIsPlacedOnce()
    {
        var s = Csa(100000f);
        s.Beliefs.Add(Belief(WinchesterX, WinchesterZ, 6000f));
        s.Beliefs.Add(Belief(RomneyX, RomneyZ, 4000f, 0.5f));
        s.Beliefs.Add(Belief(1300f, -1300f, 3000f, 1f, EnemyTraits.Embarked));
        var p = EnemyPicture.Build(s, NothingScouted);
        AssertNear(100000f, Sum(p), 0.5f, "believed + faded + unaccounted + unplaced is the total");
        AssertNear(6000f + 2000f + 3000f, p.Believed, 0.01f, "the believed layer is the live strength of every belief, afloat included");
        AssertNear(2000f, p.Faded, 0.01f, "a half-confident belief keeps the other half as a faded mass");
        AssertNear(100000f - 13000f, p.Unaccounted, 0.5f, "the rest is spread at the anchors");
        AssertEqual(0f, p.Unplaced, "with anchors, nothing is unplaced");
        float placed = p.Anchors.Sum(a => a.Share);
        AssertNear(p.Unaccounted, placed, 0.5f, "and the anchors hold exactly that");
    }

    private static void AFadingBeliefKeepsItsStrengthWhereLastSeen()
    {
        var s = Csa(50000f);
        s.Beliefs.Add(Belief(RomneyX, RomneyZ, 8000f, 0.25f));
        var p = EnemyPicture.Build(s, NothingScouted);
        var live = p.Elements.Where(e => e.Kind == PictureElementKind.Live).ToList();
        var faded = p.Elements.Where(e => e.Kind == PictureElementKind.Faded).ToList();
        AssertEqual(1, live.Count, "one live element");
        AssertEqual(1, faded.Count, "one faded element");
        AssertNear(2000f, live[0].Mass, 0.01f, "the live mass is strength x confidence");
        AssertNear(6000f, faded[0].Mass, 0.01f, "the rest of the quantity stays");
        AssertTrue(Math.Abs(faded[0].X - RomneyX) < 0.01f && Math.Abs(faded[0].Z - RomneyZ) < 0.01f, "where it was last seen, not at the anchors");
        AssertNear(6000f, p.Faded, 0.01f, "the row's faded sum");
    }

    private static void AnExpiredBeliefReturnsItsMenToTheAnchors()
    {
        var withBelief = Csa(50000f);
        withBelief.Beliefs.Add(Belief(RomneyX, RomneyZ, 8000f, 0.5f));
        var gone = Csa(50000f);
        gone.Beliefs.Add(Belief(RomneyX, RomneyZ, 8000f, 0f));   // expired: confidence 0 (the model drops it after ExpireDays)
        var a = EnemyPicture.Build(withBelief, NothingScouted);
        var b = EnemyPicture.Build(gone, NothingScouted);
        AssertNear(50000f - 8000f, a.Unaccounted, 0.5f, "a fading belief still accounts for its whole strength");
        AssertNear(50000f, b.Unaccounted, 0.5f, "an expired one returns its men to the anchors");
        AssertEqual(0f, b.Believed + b.Faded, "and adds nothing of its own");
        AssertEqual(0, b.Elements.Count(e => e.Kind != PictureElementKind.Anchor), "it is no element");
    }

    // Washington (the capital) takes 0.4 of the unaccounted men while it is not scouted and an objective anchor can take the rest.
    private static void CapitalTakesItsShareWhenUnscouted()
    {
        var p = EnemyPicture.Build(Csa(100000f), NothingScouted);
        var capital = AnchorAt(p, WashingtonX, WashingtonZ);
        AssertTrue(capital.IsCapital, "Washington is the capital anchor");
        AssertNear(0.4f * 100000f, capital.Share, 0.5f, "0.4 of the unaccounted men");
        AssertNear(0.4f, p.CapitalShareUsed, 0.0001f, "the row's capitalShare");
        AssertEqual(0.4f, EnemyPicture.CapitalShare, "the constant of D11 (question-capital-share)");
    }

    private static void ObjectiveAnchorsShareByImportance()
    {
        var p = EnemyPicture.Build(Csa(100000f), NothingScouted);
        var winchester = AnchorAt(p, WinchesterX, WinchesterZ);
        var romney = AnchorAt(p, RomneyX, RomneyZ);
        AssertNear(0.6f * 100000f * 0.5f / 0.8f, winchester.Share, 0.5f, "the other 0.6 goes by importance: Winchester 0.5 of 0.8");
        AssertNear(0.6f * 100000f * 0.3f / 0.8f, romney.Share, 0.5f, "Romney 0.3 of 0.8");
        AssertEqual(3, p.Anchors.Count, "the capital and the two objectives the Union holds; the CSA's own records are no anchor");
    }

    // An anchor that is scouted holds none of the unaccounted men: its share goes to the others.
    private static void AScoutedAnchorPassesItsShareOn()
    {
        var p = EnemyPicture.Build(Csa(100000f), ScoutedAt(WinchesterX, WinchesterZ));
        var winchester = AnchorAt(p, WinchesterX, WinchesterZ);
        AssertTrue(winchester.Displaced, "a scouted anchor is displaced");
        AssertEqual(0f, winchester.Share, "and holds nothing");
        AssertNear(0.6f * 100000f, AnchorAt(p, RomneyX, RomneyZ).Share, 0.5f, "the other objective takes the whole 0.6");
        AssertNear(0.4f * 100000f, AnchorAt(p, WashingtonX, WashingtonZ).Share, 0.5f, "the capital keeps 0.4");
        AssertEqual(1, p.DisplacedCount, "the row's displaced count");

        var capitalSeen = EnemyPicture.Build(Csa(100000f), ScoutedAt(WashingtonX, WashingtonZ));
        AssertEqual(0f, AnchorAt(capitalSeen, WashingtonX, WashingtonZ).Share, "a scouted capital holds nothing either");
        AssertNear(100000f, AnchorAt(capitalSeen, WinchesterX, WinchesterZ).Share + AnchorAt(capitalSeen, RomneyX, RomneyZ).Share, 0.5f, "the objective anchors take all of it");
    }

    private static void EveryAnchorScoutedLeavesTheRestAtTheCapital()
    {
        var p = EnemyPicture.Build(Csa(100000f), ScoutedAt(WashingtonX, WashingtonZ, WinchesterX, WinchesterZ, RomneyX, RomneyZ));
        AssertEqual(3, p.DisplacedCount, "every anchor is scouted");
        AssertNear(100000f, AnchorAt(p, WashingtonX, WashingtonZ).Share, 0.5f, "the capital takes the rest anyway: where vanilla keeps its capital defenders");
        AssertNear(100000f, Sum(p), 0.5f, "and every man is still placed once");
        // With no objective anchor undisplaced and the capital standing, the capital takes all of it.
        var onlyCapital = EnemyPicture.Build(Csa(100000f), ScoutedAt(WinchesterX, WinchesterZ, RomneyX, RomneyZ));
        AssertNear(100000f, AnchorAt(onlyCapital, WashingtonX, WashingtonZ).Share, 0.5f, "with every objective anchor scouted the unscouted capital takes all");
    }

    private static void UnknownTotalSpreadsNothing()
    {
        var s = Csa(float.NaN);
        s.Beliefs.Add(Belief(RomneyX, RomneyZ, 4000f));
        var p = EnemyPicture.Build(s, NothingScouted);
        AssertTrue(float.IsNaN(p.Total), "the total is unknown");
        AssertEqual(0f, p.Unaccounted, "so nothing is spread");
        AssertEqual(0f, p.Anchors.Sum(a => a.Share), "no anchor holds a man");
        AssertTrue(PictureRowOf(p, s).Contains(" total=unknown "), "and the row says total=unknown: " + PictureRowOf(p, s));
        foreach (float bad in new[] { -5f, float.PositiveInfinity })
        {
            var t = Csa(bad);
            AssertTrue(float.IsNaN(EnemyPicture.Build(t, NothingScouted).Total), "a total of " + bad + " is unknown");
        }
        AssertEqual(0f, EnemyPicture.Build(Csa(0f), NothingScouted).Unaccounted, "a known total of 0 spreads nothing either");
    }

    // The enemy has a total but the side has no anchor to put the men at (no enemy capital, no enemy-held objective): they are
    // unplaced, not unaccounted, so the row still balances.
    private static void NoAnchorMeansUnplacedNotUnaccounted()
    {
        var s = Csa(30000f);
        s.HasEnemyCapital = false;
        s.Objectives.RemoveAll(o => o.HolderAlliance == 0);
        s.Beliefs.Add(Belief(RomneyX, RomneyZ, 5000f));
        var p = EnemyPicture.Build(s, NothingScouted);
        AssertEqual(0, p.Anchors.Count, "no anchor");
        AssertEqual(0f, p.Unaccounted, "nothing is unaccounted");
        AssertNear(25000f, p.Unplaced, 0.5f, "the rest is unplaced");
        AssertNear(30000f, Sum(p), 0.5f, "and the row balances");
    }

    // The enemy is alliance 1 - alliance, for alliances 0 and 1 only; Europe is neither side's enemy total nor an anchor holder.
    private static void AnEuropeHeldObjectiveIsNoAnchor()
    {
        var s = Csa(100000f);
        s.Objectives.Add(Objective(30, 1000f, -900f, 2, 0.9f));
        var p = EnemyPicture.Build(s, NothingScouted);
        AssertEqual(3, p.Anchors.Count, "the record Europe holds is no anchor");
        AssertTrue(p.Anchors.All(a => a.Label != "obj30"), "named or not");
    }

    private static void AnAllianceTwoSnapshotSpreadsNothing()
    {
        var s = Csa(100000f);
        var europe = new CampaignSnapshot();
        europe.Reset(2, 2, ExecutorFixtures.Day, PlayerCommandClass.NotCareer, -1, null);
        europe.EnemyTroopsFielded = 100000f;
        europe.HasEnemyCapital = true;
        europe.EnemyCapitalX = WashingtonX;
        europe.EnemyCapitalZ = WashingtonZ;
        europe.Objectives.Add(Objective(20, WashingtonX, WashingtonZ, 0, 1f));
        var p = EnemyPicture.Build(europe, NothingScouted);
        AssertTrue(float.IsNaN(p.Total), "an alliance 2 snapshot has no enemy total");
        AssertEqual(0, p.Anchors.Count, "and no anchor");
        AssertEqual(0f, p.Unaccounted + p.Unplaced, "nothing is spread");
        AssertTrue(EnemyPicture.Build(s, NothingScouted).Anchors.Count > 0, "the control: the CSA snapshot of the same data spreads");
    }

    // Afloat men are accounted (troopsfielded counts embarked men) but add no land mass anywhere.
    private static void AfloatBeliefsAddNoLandMass()
    {
        var s = Csa(40000f);
        s.Beliefs.Add(Belief(1330f, -1300f, 4000f, 0.5f, EnemyTraits.Embarked));
        var p = EnemyPicture.Build(s, NothingScouted);
        AssertNear(2000f, p.Believed, 0.01f, "the live afloat mass is believed");
        AssertNear(2000f, p.Faded, 0.01f, "its fade is faded");
        AssertEqual(1, p.Elements.Count(e => e.Kind == PictureElementKind.AfloatLive), "kind AfloatLive");
        AssertEqual(1, p.Elements.Count(e => e.Kind == PictureElementKind.AfloatFaded), "kind AfloatFaded");
        AssertEqual(0, p.Elements.Count(e => e.Kind == PictureElementKind.Live || e.Kind == PictureElementKind.Faded), "no land element");
        AssertEqual(0f, p.Presumed(s, 1330f, -1300f, 200f, null), "and the presumption near it counts no afloat mass");
        AssertNear(40000f - 4000f, p.Unaccounted, 0.5f, "its strength is still taken off what is spread");
    }

    private static void CapitalAnchorImportanceIsOne()
    {
        var s = Csa(10000f);
        s.Objectives.RemoveAll(o => o.ObjectiveId == 20);
        s.Objectives.Add(Objective(20, WashingtonX + 0.5f, WashingtonZ, 0, 0.7f));   // the capital's own record, merged into the capital anchor
        var p = EnemyPicture.Build(s, NothingScouted);
        AssertEqual(3, p.Anchors.Count, "the record within 1 unit is merged");
        AssertEqual(1f, AnchorAt(p, WashingtonX, WashingtonZ).Importance01, "the capital's importance is 1 whatever its record says");
        // An objective record merged into an earlier anchor keeps the largest importance.
        var t = Csa(10000f);
        t.Objectives.Add(Objective(23, WinchesterX + 0.4f, WinchesterZ, 0, 0.9f));
        AssertEqual(0.9f, AnchorAt(EnemyPicture.Build(t, NothingScouted), WinchesterX, WinchesterZ).Importance01, "two records at one place keep the larger importance");
    }

    private static void AnchorsAreLabelled()
    {
        var p = EnemyPicture.Build(Csa(10000f), NothingScouted);
        AssertEqual("Washington", AnchorAt(p, WashingtonX, WashingtonZ).Label, "the capital's name");
        AssertEqual("obj21", AnchorAt(p, WinchesterX, WinchesterZ).Label, "an objective anchor is obj<id>");
        AssertEqual("obj22", AnchorAt(p, RomneyX, RomneyZ).Label, "all of them");
    }

    private static void BothCapitalSharesReported()
    {
        var p = EnemyPicture.Build(Csa(100000f), NothingScouted);
        AssertNear(0.4f, p.CapitalShareUsed, 0.0001f, "the share in force");
        // Proportional: the capital at importance 1 among 1 + 0.5 + 0.3.
        AssertNear(1f / 1.8f, p.CapitalShareProp, 0.001f, "the proportional share reported beside it");
        var capital = AnchorAt(p, WashingtonX, WashingtonZ);
        AssertNear(100000f / 1.8f, capital.ShareProp, 0.5f, "the capital's proportional mass");
        AssertNear(100000f, p.Anchors.Sum(a => a.ShareProp), 0.5f, "and the proportional spread places every man too");
        string row = PictureRowOf(p, Csa(100000f));
        AssertContains(row, " capitalShare=0.40 ", "the row reports 0.40");
        AssertTrue(Regex.IsMatch(row, @" capitalShareProp=0\.56 "), "and the proportional share: " + row);
    }

    // ---- the row ----

    private static string PictureRowOf(EnemyPicture p, CampaignSnapshot s) => EnemyPicture.Row(p, s);

    private static void TheEnemyPictureRowHasTheFieldList()
    {
        var s = Csa(100000f);
        s.Beliefs.Add(Belief(WinchesterX, WinchesterZ, 6000f));
        var p = EnemyPicture.Build(s, NothingScouted);
        string row = EnemyPicture.Row(p, s);
        AssertTrue(row.StartsWith("[EnemyPicture] alliance=1 total=100000 believed=6000.0 faded=0.0 unaccounted=94000.0 unplaced=0.0 anchors=3 displaced=0 ", StringComparison.Ordinal), row);
        foreach (string field in new[] { "capitalShare=", "capitalShareProp=", "seen=0", "marks=0", "enemyCapital=Washington", "enemyCapitalEst=", "enemyCapitalEstProp=", "top=" })
            AssertContains(row, field, "the row has " + field);
        AssertTrue(TelemetryLegacyParser.TryParse(row, TelemetryProfile.CampaignTuning, "s", out var ev), "the row parses");
        AssertEqual("Washington", ev.Fields.GetString("enemyCapital"), "the capital's name is one value");
        AssertEqual("EnemyPicture", TelemetryTagPolicy.Route(row).EventName, "the router knows it");
        AssertEqual(TelemetryCategory.State, TelemetryTagPolicy.Route(row).Category, "a State row");
        // top= is the three largest anchors, quoted as one value.
        Match top = Regex.Match(row, " top=(\"[^\"]*\"|\\S+)");
        AssertTrue(top.Success && top.Groups[1].Value.Contains("Washington:37600.0"), "the largest anchor first: " + top.Value);
    }

    public static void Run()
    {
        RunPictureTests();
        RunPlannerTests();
    }

    private static void RunPictureTests()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("EveryManIsPlacedOnce", EveryManIsPlacedOnce),
            ("AFadingBeliefKeepsItsStrengthWhereLastSeen", AFadingBeliefKeepsItsStrengthWhereLastSeen),
            ("AnExpiredBeliefReturnsItsMenToTheAnchors", AnExpiredBeliefReturnsItsMenToTheAnchors),
            ("CapitalTakesItsShareWhenUnscouted", CapitalTakesItsShareWhenUnscouted),
            ("ObjectiveAnchorsShareByImportance", ObjectiveAnchorsShareByImportance),
            ("AScoutedAnchorPassesItsShareOn", AScoutedAnchorPassesItsShareOn),
            ("EveryAnchorScoutedLeavesTheRestAtTheCapital", EveryAnchorScoutedLeavesTheRestAtTheCapital),
            ("UnknownTotalSpreadsNothing", UnknownTotalSpreadsNothing),
            ("NoAnchorMeansUnplacedNotUnaccounted", NoAnchorMeansUnplacedNotUnaccounted),
            ("AnEuropeHeldObjectiveIsNoAnchor", AnEuropeHeldObjectiveIsNoAnchor),
            ("AnAllianceTwoSnapshotSpreadsNothing", AnAllianceTwoSnapshotSpreadsNothing),
            ("AfloatBeliefsAddNoLandMass", AfloatBeliefsAddNoLandMass),
            ("CapitalAnchorImportanceIsOne", CapitalAnchorImportanceIsOne),
            ("AnchorsAreLabelled", AnchorsAreLabelled),
            ("BothCapitalSharesReported", BothCapitalSharesReported),
            ("TheEnemyPictureRowHasTheFieldList", TheEnemyPictureRowHasTheFieldList),
        });
    }
}
