using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;

using static TestHarness;
using static TestFixtures;

// The planner half of slice F3: hold need and release, eligibility, the capital reach and floor, the estimate, the flag. Fixtures and
// the picture's own cases are in EnemyPictureTests.cs.
internal static partial class EnemyPictureTests
{
    // ---- fixtures ----

    private static TheaterPlanner.Inputs InputsFor(CampaignSnapshot s, PlannerState state = null, ITerrainLineProbe probe = null)
    {
        var sectors = SectorModel.Build(s);
        state = state ?? new PlannerState();
        var c = new TheaterPlanner.Inputs { S = s, Sectors = sectors, Speed = ExecutorFixtures.Speed(), State = state, Probe = probe };
        TheaterPlanner.AssignDepartments(c, state);
        return c;
    }

    // The sector facts of a pass, kept on the inputs as Evaluate keeps them (the department need and the release read them).
    private static TheaterPlanner.SectorFacts FactsOf(TheaterPlanner.Inputs c)
    {
        c.Facts = TheaterPlanner.BuildSectorFacts(c);
        return c.Facts;
    }

    private static int SectorAtPlace(TheaterPlanner.Inputs c, float x, float z) => c.Sectors.SectorAt(x, z);

    // Runs a test body with the picture flag in a state and puts it back (the planner reads PlannerSettings.ForesightPicture).
    private static void WithFlag(bool on, Action body)
    {
        bool before = PlannerSettings.ForesightPicture;
        PlannerSettings.ForesightPicture = on;
        try { body(); }
        finally { PlannerSettings.ForesightPicture = before; }
    }

    private sealed class BlockingProbe : ITerrainLineProbe
    {
        public bool Blocks(float ax, float az, float bx, float bz) => true;
        public bool RiverNear(float x, float z, float range) => false;
    }

    // ---- hold need ----

    // An anchor is held against in one sector only: the facing sector nearest it by centroid, not every sector whose reach holds it.
    private static void AnAnchorCountsInOneSectorsNeed()
    {
        var s = Csa(100000f);
        var c = InputsFor(s);
        var facts = FactsOf(c);
        int washington = SectorAtPlace(c, WashingtonX, WashingtonZ), fredericksburg = SectorAtPlace(c, FredericksburgX, FredericksburgZ);
        AssertEqual(washington, SectorAtPlace(c, ManassasX, ManassasZ), "Manassas grows into Washington's sector in this fixture");
        AssertTrue(washington != fredericksburg, "and Fredericksburg has its own");
        AssertTrue(facts.Facing[washington] && facts.Facing[fredericksburg],
            "both face Washington's anchor: the anchor is inside Fredericksburg's reach too (" + RiverLinesDistance(facts.CX[fredericksburg], facts.CZ[fredericksburg], WashingtonX, WashingtonZ) + " of " + (facts.Radius[fredericksburg] + 200f) + ")");
        var element = c.Picture.Elements.Single(e => e.Kind == PictureElementKind.Anchor && Math.Abs(e.X - WashingtonX) < 1f);
        AssertEqual(washington, element.AssignedSector, "assigned to the facing sector nearest it, its own");
        float assigned = facts.Assigned.Sum();
        AssertNear(100000f, assigned, 1f, "the presumed sum is conserved: every anchor inside some facing sector's reach lands in exactly one need (weight 1 with no river)");
        AssertEqual(0f, facts.Assigned[fredericksburg], "Fredericksburg holds against none of Washington's men");
    }

    private static float RiverLinesDistance(float ax, float az, float bx, float bz)
    {
        float dx = ax - bx, dz = az - bz;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    // Unseen men are held against at the same ratio as seen ones: HoldRatio x (threat + assigned presumed), where that exceeds the floor.
    private static void HoldNeedHoldsUnseenMenAtTheHoldRatio()
    {
        var s = Csa(400000f);
        var c = InputsFor(s);
        var facts = FactsOf(c);
        int washington = SectorAtPlace(c, WashingtonX, WashingtonZ);
        AssertTrue(facts.Assigned[washington] > 0f, "the sector holds against Washington's anchor");
        AssertNear(TheaterPlanner.HoldRatio * (facts.Threat[washington] + facts.Assigned[washington]), facts.Need[washington], 1f,
            "its need is the hold ratio of what is believed and presumed, not the larger of the two");
        AssertTrue(facts.Need[washington] > TheaterPlanner.Floor(c, facts.CX[washington], facts.CZ[washington], facts.Radius[washington] + 200f), "above the floor here");
        AssertEqual(facts.Assigned[washington], facts.Presumed[washington], "and the facts' Presumed is what was assigned");
        // A small total leaves the floor in force.
        var small = InputsFor(Csa(8000f));
        var smallFacts = FactsOf(small);
        int w = SectorAtPlace(small, WashingtonX, WashingtonZ);
        AssertNear(TheaterPlanner.Floor(small, smallFacts.CX[w], smallFacts.CZ[w], smallFacts.Radius[w] + 200f), smallFacts.Need[w], 1f, "with few unseen men the floor is the need");
    }

    // 002/A: the East keeps its own presumed Washington army once, and a formation its holds do not need is a donor for a short West.
    private static void TheEastKeepsItsHoldWithoutADonorIn002A()
    {
        var s = Csa(60000f);
        s.Own.Add(Corps(1, ManassasX, ManassasZ, 15000f));
        s.Own.Add(Corps(2, FredericksburgX, FredericksburgZ, 15000f));
        s.Own.Add(Corps(3, RichmondX, RichmondZ, 15000f));
        s.Own.Add(Corps(4, BowlingGreenX, BowlingGreenZ, 10000f));   // the formation the West would be given
        var c = InputsFor(s);
        FactsOf(c);
        float need = TheaterPlanner.DepartmentNeed(c, Theater.East);
        AssertNear(TheaterPlanner.HoldRatio * 60000f, need, 5f, "the East's need holds the 60,000 presumed once at the hold ratio (not once per sector that reaches Washington)");
        int excluded = s.IndexOfOwn(4);
        AssertTrue(TheaterPlanner.DepartmentHoldMet(c, Theater.East, excluded), "the East's hold is met without the formation: it is a donor");
        // 75,000 held against 36,000 needed: with 30,000 beside them the East could give a 15,000 corps.
        s.Own[excluded] = Corps(4, BowlingGreenX, BowlingGreenZ, 30000f);
        var thin = InputsFor(s);
        FactsOf(thin);
        AssertTrue(TheaterPlanner.DepartmentHoldMet(thin, Theater.East, s.IndexOfOwn(1)), "with 30,000 more beside them the East could give one corps");
        s.Own[excluded] = Corps(4, BowlingGreenX, BowlingGreenZ, 1000f);
        var short_ = InputsFor(s);
        FactsOf(short_);
        AssertFalse(TheaterPlanner.DepartmentHoldMet(short_, Theater.East, s.IndexOfOwn(1)), "but with 1,000 beside them taking a 15,000 corps leaves the East at 31,000, below 36,000");
    }

    // The D8 secession post competes for no element a sector holds against: its presumed term is the Faded and Anchor elements no facing sector
    // was assigned.
    private static void SecessionPostCountsNoElementASectorHolds()
    {
        var s = Csa(100000f);
        s.Beliefs.Add(Belief(900f, -300f, 6000f, 0.5f));     // a fading army far from every sector: assigned to none
        var c = InputsFor(s);
        FactsOf(c);
        float atManassas = TheaterPlanner.HoldNeedAt(c, ManassasX, ManassasZ, 200f, 0f, TheaterPlanner.HoldRatio, out float presumedThere);
        AssertEqual(0f, presumedThere, "the anchors near Manassas are held against by their sector, so the post adds none of them");
        AssertNear(TheaterPlanner.Floor(c, ManassasX, ManassasZ, 200f), atManassas, 1f, "its need is the floor");
        float far = TheaterPlanner.HoldNeedAt(c, 900f, -300f, 200f, 0f, TheaterPlanner.HoldRatio, out float presumedFar);
        AssertNear(3000f, presumedFar, 1f, "an unassigned fading army near the post is the post's to hold against (the faded half of 6,000)");
        AssertNear(Math.Max(TheaterPlanner.HoldRatio * presumedFar, TheaterPlanner.Floor(c, 900f, -300f, 200f)), far, 1f, "at the hold ratio, or the floor");
    }

    // One division sighted 156+ from Washington does not switch the Washington anchor off, and the sector facing it keeps its hold.
    private static void ASightingAt165DoesNotSwitchOffWashington()
    {
        var s = Csa(80000f);
        s.Beliefs.Add(Belief(WinchesterX, WinchesterZ, 6653f));   // the Union's 2nd Division, seen near Winchester
        var c = InputsFor(s);
        var facts = FactsOf(c);
        var washington = AnchorAt(c.Picture, WashingtonX, WashingtonZ);
        AssertFalse(washington.Displaced, "Winchester is 254 from Washington: beyond the relocation range of the sighting");
        AssertTrue(washington.Share > 0f, "so Washington still holds its men");
        AssertTrue(AnchorAt(c.Picture, WinchesterX, WinchesterZ).Displaced, "while the anchor the division stands at is scouted");
        int sector = SectorAtPlace(c, ManassasX, ManassasZ);
        AssertTrue(facts.Assigned[sector] >= washington.Share - 1f, "the Manassas sector still holds against Washington's men");
        AssertTrue(facts.Need[sector] >= TheaterPlanner.HoldRatio * facts.Assigned[sector] - 1f, "its need is above the hold ratio of its presumption");
        // The earlier rule: the sector's need collapsed to the believed army or the floor while a fresh belief stood within its reach.
        WithFlag(false, () =>
        {
            var old = FactsOf(InputsFor(s));
            AssertTrue(old.Need[sector] < facts.Need[sector], "the superseded per-sector switch gave this sector less: " + old.Need[sector] + " against " + facts.Need[sector]);
        });
    }

    private static void FloorUsesOnlyUndisplacedImportance()
    {
        var s = Csa(100000f);
        var c = InputsFor(s);
        EnsureAnchors(c);
        float min = TheaterPlanner.MinStrength(c);
        AssertNear(min * 2f, TheaterPlanner.Floor(c, WashingtonX, WashingtonZ, 200f), 0.5f, "at Washington: the minimum raised by the capital's importance 1");
        AssertNear(min * 1.5f, TheaterPlanner.Floor(c, WinchesterX, WinchesterZ, 100f), 0.5f, "at Winchester: its importance 0.5");
        AssertNear(min, TheaterPlanner.Floor(c, RichmondX, RichmondZ, 150f), 0.5f, "at a place with no anchor: the plain minimum");
        var seen = EnemyPicture.Build(s, ScoutedAt(WashingtonX, WashingtonZ));
        var d = InputsFor(s);
        d.Picture = seen;
        AssertNear(min, TheaterPlanner.Floor(d, WashingtonX, WashingtonZ, 200f), 0.5f, "a place whose anchors are all scouted floors at the plain minimum");
    }

    private static void EnsureAnchors(TheaterPlanner.Inputs c)
    {
        FactsOf(c);   // builds the picture when none was
    }

    // An anchor alone, with no believed army and no enemy-held objective record near, makes a sector face enemy ground.
    private static void AnAnchorAloneMakesASectorFace()
    {
        var s = Csa(50000f);
        s.Objectives.RemoveAll(o => o.HolderAlliance == 0);   // the capital is an anchor without being one of the side's objective records
        var c = InputsFor(s);
        var facts = FactsOf(c);
        int sector = SectorAtPlace(c, ManassasX, ManassasZ);
        AssertEqual(0f, facts.Threat[sector], "nothing is believed near");
        AssertTrue(facts.Facing[sector], "the capital anchor makes the sector face");
        AssertTrue(facts.Need[sector] > 0f, "and hold");
        // The control: with the capital gone from the picture and no objective record, it faces nothing.
        s.HasEnemyCapital = false;
        var none = FactsOf(InputsFor(s));
        AssertFalse(none.Facing[sector], "no anchor, no facing");
    }

    // Each element counts in one department once: the need of a department is the hold ratio of the land elements assigned to its sectors.
    private static void DepartmentNeedCountsEachElementOnce()
    {
        var s = Csa(100000f);
        s.Beliefs.Add(Belief(WinchesterX, WinchesterZ, 5000f));
        var c = InputsFor(s);
        var facts = FactsOf(c);
        float sum = c.Picture.Elements.Where(e => e.AssignedSector >= 0 && e.Kind != PictureElementKind.AfloatLive && e.Kind != PictureElementKind.AfloatFaded)
            .Sum(e => e.Mass * e.Weight);
        AssertNear(TheaterPlanner.HoldRatio * sum, TheaterPlanner.DepartmentNeed(c, Theater.East), 2f, "the East's need is the hold ratio over every assigned land element, each once");
        float perSector = 0f;
        for (int k = 0; k < c.Sectors.Count; k++) perSector += facts.Need[k];
        AssertTrue(perSector >= TheaterPlanner.DepartmentNeed(c, Theater.East) - 1f, "and never more than the sector needs it replaces");
        WithFlag(false, () => AssertNear(c.Facts.Need.Sum(), TheaterPlanner.DepartmentNeed(c, Theater.East), 1f, "picture off: the sum of the sector needs, as before"));
    }

    // Live, Faded and Anchor elements count in the department need (afloat kinds do not); an element equidistant from two facing sectors goes to
    // the lower id.
    private static void DepartmentNeedLandKindsWeightedTieToLowerSector()
    {
        var s = new CampaignSnapshot();
        s.Reset(1, 1, ExecutorFixtures.Day, PlayerCommandClass.NotCareer, -1, null);
        s.RelocationRange = 200f;
        s.HasCapital = true;
        s.CapitalX = -1000f;
        s.CapitalZ = -1000f;
        s.Areas.Add(Area(0, 0f, 0f));
        s.Areas.Add(Area(1, 100f, 0f));
        s.Objectives.Add(Objective(1, 0f, 0f, 1, 0.5f));
        s.Objectives.Add(Objective(2, 100f, 0f, 1, 0.5f));
        s.HasEnemyCapital = true;
        s.EnemyCapitalX = 50f;               // exactly between the two sector centroids
        s.EnemyCapitalZ = 0f;
        s.EnemyCapitalName = "Midway";
        s.EnemyTroopsFielded = 10000f;
        s.Beliefs.Add(Belief(50f, 0f, 2000f));                                       // live, between them too
        s.Beliefs.Add(Belief(50f, 20f, 1000f, 1f, EnemyTraits.Embarked));            // afloat: no sector's
        var c = InputsFor(s);
        FactsOf(c);
        AssertTrue(c.Sectors.Count >= 2, "two sectors");
        var anchor = c.Picture.Elements.Single(e => e.Kind == PictureElementKind.Anchor);
        var live = c.Picture.Elements.Single(e => e.Kind == PictureElementKind.Live);
        AssertEqual(Math.Min(c.Sectors.SectorAt(0f, 0f), c.Sectors.SectorAt(100f, 0f)), anchor.AssignedSector, "the anchor goes to the lower of two equidistant sectors");
        AssertEqual(anchor.AssignedSector, live.AssignedSector, "so does the live army");
        AssertTrue(c.Picture.Elements.Where(e => e.Kind == PictureElementKind.AfloatLive).All(e => e.AssignedSector == -1), "an afloat element is assigned to none");
        Theater theater = c.Sectors.TheaterOf(anchor.AssignedSector);
        float expected = c.Picture.Elements.Where(e => e.AssignedSector >= 0).Sum(e => e.Mass * e.Weight);
        AssertNear(10000f - 3000f + 2000f, expected, 1f, "the land kinds are counted: the 10,000 less the 3,000 believed (2,000 live and 1,000 afloat) is spread, and the live 2,000 are added");
        AssertTrue(TheaterPlanner.DepartmentNeed(c, theater) >= TheaterPlanner.HoldRatio * expected - 1f, "the department's need is at least the hold ratio of the assigned land mass");
    }

    // ---- release and eligibility ----

    // Romney is 130 from Winchester, so the sighting of the 2nd Division there once released the whole Manassas hold for Romney; with
    // Washington's men presumed 389 from Romney, the sector facing both keeps its hold.
    private static void AHoldIsReleasedOnlyWhenEveryElementItFacesIsAtTheTarget()
    {
        var s = Csa(80000f);
        s.Beliefs.Add(Belief(WinchesterX, WinchesterZ, 6653f));
        var c = InputsFor(s);
        FactsOf(c);
        int sector = SectorAtPlace(c, ManassasX, ManassasZ);
        AssertFalse(TheaterPlanner.HoldThreatAtTarget(c, sector, RomneyX, RomneyZ), "Romney does not release the Manassas hold while the Washington anchor is in its reach");
        WithFlag(false, () =>
        {
            var old = InputsFor(s);
            FactsOf(old);
            AssertTrue(TheaterPlanner.HoldThreatAtTarget(old, sector, RomneyX, RomneyZ), "the superseded nearest-belief test released it");
        });
        // Every element the sector faces at the target: the only army is the division at Winchester and no anchor holds men.
        var quiet = Csa(float.NaN);
        quiet.Beliefs.Add(Belief(WinchesterX, WinchesterZ, 6653f));
        var q = InputsFor(quiet);
        FactsOf(q);
        AssertTrue(TheaterPlanner.HoldThreatAtTarget(q, SectorAtPlace(q, ManassasX, ManassasZ), RomneyX, RomneyZ), "with that one army the whole of what the sector faces, attacking it releases the hold");
    }

    private static void ASectorFacingNothingReleasesNothing()
    {
        var s = Csa(float.NaN);
        var c = InputsFor(s);
        FactsOf(c);
        int sector = SectorAtPlace(c, RichmondX, RichmondZ);
        AssertFalse(TheaterPlanner.HoldThreatAtTarget(c, sector, RomneyX, RomneyZ), "a sector facing no element releases nothing");
    }

    private static TheaterPlanner.Inputs WideInputs(float strength, float minStrength, out List<TheaterPlanner.Candidate> candidates, float enemy = 0f, float dominance = 1.1f)
    {
        var s = ExecutorFixtures.Wide();
        s.OffensiveDominance = dominance;
        s.Own.Add(ExecutorFixtures.Corps(2, 20f, 12000f));      // the capital reserve (the floor is at least the minimum now) takes this one first
        for (int k = 0; k < 3; k++)
        {
            // A busy corps in each front's sector holds it where it stands, so the free corps below is not kept for a hold.
            var holder = ExecutorFixtures.Corps(10 + k, 100f + 200f * k, 12000f);
            holder.Flags |= FormationFlags.InBattle;
            s.Own.Add(holder);
        }
        s.Own.Add(ExecutorFixtures.Corps(1, 350f, strength));
        if (enemy > 0f) s.Beliefs.Add(ExecutorFixtures.Belief(100f, 0f, enemy));
        var sectors = SectorModel.Build(s);
        var c = new TheaterPlanner.Inputs { S = s, Sectors = sectors, Speed = ExecutorFixtures.Speed(), State = new PlannerState(), MinStrength = minStrength };
        TheaterPlanner.AssignDepartments(c, c.State);
        candidates = new List<TheaterPlanner.Candidate>();
        TheaterPlanner.Evaluate(c, candidates);
        return c;
    }

    private static void ACapacityBelowTheLiveMinimumIsIneligible()
    {
        // A bold side (offensive dominance 0.2: the odds it asks are 0.22) would take 3,000 men against the floor's 10,000: the minimum is what refuses.
        var c = WideInputs(3000f, 5000f, out var below, dominance: 0.2f);
        var near = below.Single(x => x.ObjectiveId == 98);
        AssertTrue(near.Mass > 0f && near.Mass < TheaterPlanner.MinStrength(c), "the capacity is below the minimum: " + near.Mass);
        AssertTrue(near.Ratio >= near.Required, "though its odds would do: " + near.Ratio + " against " + near.Required);
        AssertFalse(near.Eligible, "a capacity under the minimum strength is no push");
        WideInputs(3000f, 1000f, out var above, dominance: 0.2f);
        AssertTrue(above.Single(x => x.ObjectiveId == 98).Eligible, "the same force against a lower minimum is eligible");
    }

    private static void OddsRuleUnchangedAboveTheMinimum()
    {
        // Capacity 9,000 against a minimum of 5,000: eligibility is the odds alone.
        WideInputs(9000f, 5000f, out var strong, enemy: 2000f);
        var good = strong.Single(x => x.ObjectiveId == 98);
        AssertTrue(good.Ratio >= good.Required && good.Eligible, "odds met: eligible (" + good.Ratio + " against " + good.Required + ")");
        WideInputs(9000f, 5000f, out var weak, enemy: 40000f);
        var bad = weak.Single(x => x.ObjectiveId == 98);
        AssertTrue(bad.Ratio < bad.Required && !bad.Eligible, "odds missed: ineligible");
    }

    // The F1 deadband re-enables a kept candidate only under the same floor: a capacity under the minimum returns before the odds counter.
    private static void RetainNeverReenablesBelowTheMinimum()
    {
        Func<float, TheaterPlanner.Candidate> candidate = mass => new TheaterPlanner.Candidate { ObjectiveId = 5, Force = 0, Ratio = 1.05f, Required = 1.1f, Mass = mass, Eligible = false };
        var below = new List<TheaterPlanner.Candidate> { candidate(3000f) };
        var state = new PlannerState();
        TheaterPlanner.Retain(below, 5, state, 5000f);
        AssertFalse(below[0].Eligible, "a capacity below the minimum is not re-enabled");
        AssertEqual(0, state.BelowOddsDays, "and the odds counter does not tick");
        var above = new List<TheaterPlanner.Candidate> { candidate(9000f) };
        TheaterPlanner.Retain(above, 5, state, 5000f);
        AssertTrue(above[0].Eligible && above[0].Retained, "the control: the same odds with a capacity above it are re-enabled");
        AssertEqual(1, state.BelowOddsDays, "and tick");
    }

    // ---- capital reach and floor ----

    private static float RoadDays(CampaignSnapshot s, FormationRecord f)
    {
        float road = MarchTime.SpeedPerDay(ExecutorFixtures.Speed(), f.UnitTyp, f.GroupSpeed, ExecutorFixtures.Speed().RoadCost);
        return RiverLinesDistance(f.X, f.Z, s.CapitalX, s.CapitalZ) / road;
    }

    private static float CorpsRate()
    {
        var speed = ExecutorFixtures.Speed();
        return MarchTime.SpeedPerDay(speed, 15, speed.MountedBaseSpeed[15], speed.RoadCost);
    }

    private static CampaignSnapshot CsaWithEast(float total = 60000f)
    {
        var s = Csa(total);
        s.Own.Add(Corps(1, ManassasX, ManassasZ, 9000f));
        s.Own.Add(Corps(2, FredericksburgX, FredericksburgZ, 9000f));
        return s;
    }

    private static void CapitalReachIsTheReliefRace()
    {
        var s = CsaWithEast();
        var c = InputsFor(s);
        TheaterPlanner.CapitalThreat(c, out _, out _);
        float relief = Math.Max(RoadDays(s, s.Own[0]), RoadDays(s, s.Own[1]));
        AssertNear(relief, c.CapitalReliefDays, 0.001f, "the slowest formation of the capital's department sets the race");
        AssertNear(CorpsRate(), c.CapitalCorpsPerDay, 0.001f, "at a corps' road rate");
        AssertNear(Math.Max(200f, relief * CorpsRate()), c.CapitalReach, 0.5f, "the reach is the race");
        AssertTrue(c.CapitalReach > RiverLinesDistance(RichmondX, RichmondZ, WashingtonX, WashingtonZ), "Washington (389 from Richmond) is inside it");
    }

    // The worked example: a corps of the West cannot reach Richmond in time to matter, so it does not stretch the race.
    private static void ReliefRaceExcludesTheWestFromRichmond()
    {
        var s = CsaWithEast();
        s.Own.Add(Corps(3, RomneyX, RomneyZ, 9000f));
        var state = new PlannerState();
        state.Transfer(s.Own[2].UnitKey, Theater.West);
        var c = InputsFor(s, state);
        TheaterPlanner.CapitalThreat(c, out _, out _);
        float east = Math.Max(RoadDays(s, s.Own[0]), RoadDays(s, s.Own[1]));
        AssertNear(east, c.CapitalReliefDays, 0.001f, "the West's corps is not in the race");
        var same = InputsFor(s);   // the same corps, in the East department: it is the slowest
        TheaterPlanner.CapitalThreat(same, out _, out _);
        AssertNear(RoadDays(s, s.Own[2]), same.CapitalReliefDays, 0.001f, "the control: as an East corps it sets the race");
        AssertTrue(same.CapitalReach > c.CapitalReach, "and the reach with it is longer");
    }

    private static void OneFarFormationSetsTheReach()
    {
        var s = CsaWithEast();
        var baseline = InputsFor(s);
        TheaterPlanner.CapitalThreat(baseline, out _, out _);
        s.Own.Add(Corps(3, 700f, -300f, 9000f));
        var c = InputsFor(s);
        TheaterPlanner.CapitalThreat(c, out _, out _);
        AssertNear(RoadDays(s, s.Own[2]), c.CapitalReliefDays, 0.001f, "one far formation of the department sets the race");
        AssertTrue(c.CapitalReach > baseline.CapitalReach, "and stretches the reach");
    }

    private static void AFormationWithNoSpeedIsSkippedInTheRace()
    {
        var s = CsaWithEast();
        var noSpeed = Corps(3, 700f, -300f, 9000f);
        noSpeed.UnitTyp = 3;           // the speed table has no base speed for it: its estimate is float.MaxValue
        s.Own.Add(noSpeed);
        var c = InputsFor(s);
        TheaterPlanner.CapitalThreat(c, out _, out _);
        AssertNear(Math.Max(RoadDays(s, s.Own[0]), RoadDays(s, s.Own[1])), c.CapitalReliefDays, 0.001f, "it is skipped: it does not stretch the race to infinity");
        AssertTrue(c.CapitalReach < 5000f, "the reach stays finite: " + c.CapitalReach);
    }

    private static void MissingSpeedArrayFallsBackToTheRange()
    {
        foreach (float[] table in new float[][] { null, new float[10] })
        {
            var s = CsaWithEast();
            var c = InputsFor(s);
            var speed = ExecutorFixtures.Speed();
            speed.MountedBaseSpeed = table;
            c.Speed = speed;
            TheaterPlanner.CapitalThreat(c, out _, out _);   // must not throw on a short array
            AssertEqual(0f, c.CapitalCorpsPerDay, "no corps rate");
            AssertEqual(200f, c.CapitalReach, "the reach is the relocation range");
        }
    }

    // Rails are credited to neither side: a train that would carry the formation does not shorten the race.
    private static void RailsNotCreditedInTheReach()
    {
        var s = CsaWithEast();
        var withoutRails = InputsFor(s);
        TheaterPlanner.CapitalThreat(withoutRails, out _, out _);
        var railed = CsaWithEast();
        railed.Supply.Add(new SupplySourceRecord { Id = 501, Kind = SupplySourceKind.Iip, Owner = 1, X = ManassasX, Z = ManassasZ, Condition = 1f, Built = true });
        railed.Supply.Add(new SupplySourceRecord { Id = 502, Kind = SupplySourceKind.Iip, Owner = 1, X = RichmondX, Z = RichmondZ, Condition = 1f, Built = true });
        railed.Rails.Add(new RailLineRecord { Index = 1, Progress = 1f, AX = ManassasX, AZ = ManassasZ, BX = RichmondX, BZ = RichmondZ, AIipId = 501, BIipId = 502 });
        var c = InputsFor(railed);
        var speed = ExecutorFixtures.Speed();
        speed.TrainCapacityFree = 1e9f;
        c.Speed = speed;
        var estimate = MarchTime.EstimateDays(speed, railed.Own[0], RichmondX, RichmondZ, RiverLinesDistance(ManassasX, ManassasZ, RichmondX, RichmondZ), railed, null, MarchTime.OwnIips(railed));
        AssertTrue(estimate < RoadDays(railed, railed.Own[0]) - 0.1f, "the control: given the snapshot, the train does shorten the march (" + estimate + ")");
        TheaterPlanner.CapitalThreat(c, out _, out _);
        AssertNear(withoutRails.CapitalReliefDays, c.CapitalReliefDays, 0.001f, "but the relief race is run with no snapshot: no rail for either side");
        AssertNear(withoutRails.CapitalReach, c.CapitalReach, 0.5f, "one fast own train does not shrink the reach back and drop Washington again");
    }

    private static void ReliefRaceCreditsNoRailForEitherSide()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/TheaterPlanner.cs");
        string race = BodyOf(src, "private static void CapitalRace(");
        AssertContains(race, "MarchTime.EstimateDays(c.Speed, f, s.CapitalX, s.CapitalZ, RiverLines.Distance(f.X, f.Z, s.CapitalX, s.CapitalZ), null, null, c.OwnIips)", "no snapshot, no probe: the rail and river legs are skipped");
        string threat = BodyOf(src, "internal static void CapitalThreat(");
        AssertFalse(threat.Contains("RailLeg") || threat.Contains("TrainCovers"), "and the threat reads no train");
    }

    // At the 002/A start the CSA's 5,017 men within 200 of Richmond: the trigger fires above 0.6 of them and not below.
    private static void CapitalTriggerAt002AStart()
    {
        var s = Csa(100000f);
        s.Own.Add(Corps(1, RichmondX, RichmondZ, 5017f));
        s.Own.Add(Corps(2, ManassasX, ManassasZ, 9000f));
        var probe = InputsFor(s);
        TheaterPlanner.CapitalThreat(probe, out _, out float reference);
        AssertTrue(probe.CapitalWeighted > 0f && reference > 0f, "Washington's presumed men weigh on Richmond: " + probe.CapitalWeighted);
        float weightedPerMan = probe.CapitalWeighted / 100000f;
        foreach (var (target, fires) in new[] { (0.7f, true), (0.5f, false) })
        {
            var t = Csa(target * 5017f / weightedPerMan);
            t.Own.Add(Corps(1, RichmondX, RichmondZ, 5017f));
            t.Own.Add(Corps(2, ManassasX, ManassasZ, 9000f));
            var c = InputsFor(t);
            TheaterPlanner.CapitalThreat(c, out _, out float threat);
            AssertNear(target, threat, 0.02f, "threat01 is the weight over the own men within the relocation range of the capital");
            AssertEqual(fires, threat >= TheaterPlanner.CapitalThreatTrigger, "the 0.6 trigger at " + target);
        }
    }

    private static void WashingtonCountsTowardRichmondsFloor()
    {
        var s = CsaWithEast(100000f);
        var c = InputsFor(s);
        TheaterPlanner.CapitalThreat(c, out float floor, out _);
        AssertTrue(c.CapitalWeighted > 0f, "Washington's presumed men are inside the reach and weighed");
        AssertTrue(floor >= TheaterPlanner.MinStrength(c) + TheaterPlanner.CapitalFloorRatio * c.CapitalWeighted - 0.01f, "the floor carries them above the minimum");
        // The race decides it: with the capital's department empty the reach is the relocation range and Washington (389) is outside it.
        var alone = Csa(100000f);
        var d = InputsFor(alone);
        TheaterPlanner.CapitalThreat(d, out float bare, out _);
        AssertEqual(0f, d.CapitalWeighted, "no formation to race: the reach is 200 and nothing is inside it");
        AssertNear(TheaterPlanner.MinStrength(d), bare, 0.01f, "the floor is the baseline alone");
    }

    private static void FloorHasVanillasBaseline()
    {
        var c = InputsFor(Csa(float.NaN));
        TheaterPlanner.CapitalThreat(c, out float floor, out _);
        AssertNear(TheaterPlanner.MinStrength(c), floor, 0.01f, "with no enemy the floor is the minimum strength (vanilla's num2 starts there, 11712)");
        var live = InputsFor(Csa(float.NaN));
        live.MinStrength = 2000f;
        TheaterPlanner.CapitalThreat(live, out float liveFloor, out _);
        AssertNear(2000f, liveFloor, 0.01f, "and follows MinStrength(inputs)");
    }

    // The capital trigger reads the threat without the baseline, so it never fires on an empty map.
    private static void ThreatExcludesTheBaseline()
    {
        var s = Csa(float.NaN);
        s.Own.Add(Corps(1, RichmondX, RichmondZ, 1000f));   // a thin garrison: a baseline in the numerator would trip 0.6
        var c = InputsFor(s);
        TheaterPlanner.CapitalThreat(c, out float floor, out float threat);
        float vanilla = CapitalFloorPolicy.VanillaNeed(TheaterPlanner.MinStrength(c), s.Beliefs, 1f, s.CapitalX, s.CapitalZ, s.RelocationRange, s.Own);
        AssertNear(TheaterPlanner.MinStrength(c) - 1000f, vanilla, 0.01f, "vanilla's num2 is the minimum less the own men nearer than any enemy (none seen)");
        AssertNear(TheaterPlanner.MinStrength(c), floor, 0.01f, "the floor is the minimum plus nothing weighed: the larger of the two");
        AssertEqual(0f, threat, "no enemy: the threat is 0");
        AssertFalse(threat >= TheaterPlanner.CapitalThreatTrigger, "and no trigger");
    }

    // The floor is never below #4's VanillaNeed on the same snapshot, even where the river-weighted weight is smaller than the straight one.
    private static void FloorIsNeverBelowFourVanillaNeed()
    {
        var s = Csa(float.NaN);
        s.Beliefs.Add(Belief(RichmondX + 60f, RichmondZ + 40f, 30000f));   // an army at the gates
        s.Crossings.Add(new CrossingRecord { X = RichmondX + 400f, Z = RichmondZ + 400f });
        var c = InputsFor(s, null, new BlockingProbe());
        TheaterPlanner.CapitalThreat(c, out float floor, out _);
        float vanilla = CapitalFloorPolicy.VanillaNeed(TheaterPlanner.MinStrength(c), s.Beliefs, 1f, s.CapitalX, s.CapitalZ, s.RelocationRange, s.Own);
        AssertTrue(floor >= vanilla - 0.01f, "the floor " + floor + " is not below #4's need " + vanilla);
        AssertTrue(floor >= TheaterPlanner.MinStrength(c) + c.CapitalWeighted - 0.01f, "nor the minimum plus the weighted threat");
    }

    private static void FloorIsNeverBelowVanillaNeedOnTheSnapshot()
    {
        var s = CsaWithEast(80000f);
        s.Beliefs.Add(Belief(RichmondX + 150f, RichmondZ + 10f, 12000f));
        s.Beliefs.Add(Belief(RichmondX - 90f, RichmondZ + 120f, 8000f, 0.5f));
        var c = InputsFor(s);
        TheaterPlanner.CapitalThreat(c, out float floor, out _);
        float vanilla = CapitalFloorPolicy.VanillaNeed(TheaterPlanner.MinStrength(c), s.Beliefs, 1f, s.CapitalX, s.CapitalZ, s.RelocationRange, s.Own);
        AssertTrue(floor >= vanilla - 0.01f, "the floor " + floor + " is not below VanillaNeed on the review's snapshot " + vanilla);
    }

    private static void MinStrengthIsTheInputsField()
    {
        var c = InputsFor(Csa(float.NaN));
        AssertEqual(ForceAllocator.MinGarrisonStrength, TheaterPlanner.MinStrength(c), "until foresight F5 it is the 5000 const");
        c.MinStrength = 2345f;
        AssertEqual(2345f, TheaterPlanner.MinStrength(c), "MinStrength(inputs) is the Inputs field");
        AssertNear(2345f * 2f, TheaterPlanner.Floor(c, WashingtonX, WashingtonZ, 200f), 0.01f, "every floor derives from it");
    }

    // ---- the estimate ----

    // D1's max(believed, presumed) is superseded: the layers are disjoint, so the estimate adds them.
    private static void EstimateIsBelievedPlusPresumed()
    {
        var s = Csa(100000f);
        s.Objectives.Add(Objective(31, 1290f, -555f, 0, 0.5f));      // an anchor 183 east of Winchester
        s.Areas.Add(Area(7, 1290f, -555f));
        s.Beliefs.Add(Belief(917f, -555f, 6000f));                   // a fresh army 190 west of Winchester: scouts it, not the anchor
        var c = InputsFor(s);
        var candidates = new List<TheaterPlanner.Candidate>();
        TheaterPlanner.Evaluate(c, candidates);
        var winchester = candidates.Single(x => x.ObjectiveId == 21);
        float believed = TheaterPlanner.LandThreatNear(c, WinchesterX, WinchesterZ, 200f);
        AssertTrue(winchester.Scouted, "the target is scouted by the fresh belief");
        AssertTrue(believed > 0f && winchester.Presumed > 0f, "and still carries the men presumed at the unscouted anchor near it: " + winchester.Presumed);
        AssertNear(Math.Max(believed + winchester.Presumed, TheaterPlanner.Floor(c, WinchesterX, WinchesterZ, 200f)), winchester.Enemy, 0.5f, "the estimate adds the layers, floored");
        AssertTrue(winchester.Enemy > Math.Max(believed, winchester.Presumed) + 1f, "more than the larger of the two");
    }

    private static void AScoutedTargetStillCarriesUnscoutedAnchorsNearIt()
    {
        var s = Csa(100000f);
        s.Objectives.Add(Objective(31, 1290f, -555f, 0, 0.5f));
        s.Areas.Add(Area(7, 1290f, -555f));
        s.Beliefs.Add(Belief(917f, -555f, 6000f));
        var c = InputsFor(s);
        EnsureAnchors(c);
        var east = AnchorAt(c.Picture, 1290f, -555f);
        AssertFalse(east.Displaced, "the anchor is more than the relocation range from the belief");
        float presumed = c.Picture.Presumed(s, WinchesterX, WinchesterZ, 200f, null);
        AssertTrue(presumed >= east.Share - 0.5f, "a scouted target's presumption carries its share: " + presumed + " of " + east.Share);
    }

    // The balance term ranks targets by the believed layer alone (D1 unchanged there): a presumption changes the odds, not the ranking.
    private static void BalanceStillRanksByTheBelievedLayer()
    {
        var withMen = Csa(200000f);
        var without = Csa(float.NaN);
        foreach (var s in new[] { withMen, without })
        {
            s.Own.Add(Corps(1, ManassasX, ManassasZ, 12000f));
            s.Beliefs.Add(Belief(RomneyX, RomneyZ, 3000f));
        }
        var a = new List<TheaterPlanner.Candidate>();
        var b = new List<TheaterPlanner.Candidate>();
        TheaterPlanner.Evaluate(InputsFor(withMen), a);
        TheaterPlanner.Evaluate(InputsFor(without), b);
        var ra = a.Single(x => x.ObjectiveId == 22);
        var rb = b.Single(x => x.ObjectiveId == 22);
        AssertNear(rb.Balance, ra.Balance, 0.0001f, "the same believed layer, the same balance whatever is presumed");
    }

    // ---- sector assignment details ----

    private static void ElementsCarryTheirAssignedSector()
    {
        var s = Csa(100000f);
        s.Beliefs.Add(Belief(WinchesterX, WinchesterZ, 5000f, 0.5f));
        var c = InputsFor(s);
        FactsOf(c);
        foreach (var e in c.Picture.Elements)
        {
            AssertTrue(e.AssignedSector >= 0 && e.AssignedSector < c.Sectors.Count, e.Kind + " at " + e.X + "," + e.Z + " is assigned a facing sector");
            AssertTrue(e.Weight > 0f && e.Weight <= 1f, "with a weight in (0, 1]");
        }
    }

    // The assignment is overwritten every pass: a pass under another probe re-weights it, a pass with different facing re-assigns it.
    private static void TheAssignmentIsRewrittenEachPass()
    {
        var s = Csa(100000f);
        var c = InputsFor(s);
        FactsOf(c);
        var first = c.Picture.Elements.Select(e => e.AssignedSector).ToList();
        var weights = c.Picture.Elements.Select(e => e.Weight).ToList();
        AssertTrue(weights.All(w => w == 1f), "with no river every weight is 1");
        c.Probe = new BlockingProbe();
        s.Crossings.Add(new CrossingRecord { X = 1000f, Z = -300f });
        FactsOf(c);
        AssertTrue(c.Picture.Elements.Any(e => e.Weight < 1f), "the second pass re-weighted them under its probe");
        s.Beliefs.Clear();
        c.Picture.Elements.Clear();
        FactsOf(c);
        AssertEqual(0, c.Picture.Elements.Count, "a pass over a picture with no elements assigns nothing (the facts' Assigned is reset)");
        AssertEqual(0f, c.Facts == null ? 0f : c.Facts.Assigned.Sum(), "and carries no mass over from the earlier pass");
    }

    private static void NearestByStraightDistanceWeightedByTheProbe()
    {
        var s = Csa(100000f);
        s.Crossings.Add(new CrossingRecord { X = WashingtonX + 600f, Z = WashingtonZ + 600f });
        var straight = InputsFor(s);
        FactsOf(straight);
        var blocked = InputsFor(s, null, new BlockingProbe());
        FactsOf(blocked);
        var a = straight.Picture.Elements.Single(e => e.Kind == PictureElementKind.Anchor && Math.Abs(e.X - WashingtonX) < 1f);
        var b = blocked.Picture.Elements.Single(e => e.Kind == PictureElementKind.Anchor && Math.Abs(e.X - WashingtonX) < 1f);
        AssertEqual(a.AssignedSector, b.AssignedSector, "the nearest facing sector is judged on straight distances, whatever the probe");
        AssertEqual(1f, a.Weight, "weight 1 with no river line");
        AssertTrue(b.Weight < 1f, "the weight follows the pass's probe: " + b.Weight);
    }

    private static void TheProportionalEstimateUsesEveryAnchorsShare()
    {
        var s = Csa(100000f);
        var p = EnemyPicture.Build(s, NothingScouted);
        float at = p.Presumed(s, WashingtonX, WashingtonZ, 100f, null, proportional: false);
        float prop = p.Presumed(s, WashingtonX, WashingtonZ, 100f, null, proportional: true);
        AssertNear(AnchorAt(p, WashingtonX, WashingtonZ).Share, at, 0.5f, "within 100 of Washington only its own anchor: the share in force");
        AssertNear(AnchorAt(p, WashingtonX, WashingtonZ).ShareProp, prop, 0.5f, "and under the proportional rule its proportional share");
        AssertTrue(at != prop, "the two estimates differ");
        float wide = p.Presumed(s, WinchesterX, WinchesterZ, 1000f, null, proportional: true);
        AssertNear(100000f, wide, 1f, "a reach holding every anchor sums every anchor's proportional share");
    }

    private static void EnemyCapitalEstimateReportedUnderBothShares()
    {
        var s = Csa(100000f);
        s.Beliefs.Add(Belief(WashingtonX - 50f, WashingtonZ, 4000f));
        var plan = TheaterPlanner.Plan(s, SectorModel.Build(s), null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _, out EnemyPicture picture);
        AssertTrue(picture != null && picture.HasEnemyCapital, "the plan hands the picture back");
        float believed = 4000f;
        AssertNear(believed + AnchorAt(picture, WashingtonX, WashingtonZ).Share, picture.EnemyCapitalEst, 5f, "the estimate under the share in force: believed plus the capital's share");
        AssertNear(believed + AnchorAt(picture, WashingtonX, WashingtonZ).ShareProp, picture.EnemyCapitalEstProp, 5f, "and under the proportional one");
        AssertTrue(picture.EnemyCapitalEst != picture.EnemyCapitalEstProp, "reported apart");
        AssertContains(EnemyPicture.Row(picture, s), " enemyCapitalEstProp=", "both in the row");
    }

    // ---- the plan and the flag ----

    private static void PictureBuiltOncePerReview()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/TheaterPlanner.cs");
        string plan = BodyOf(src, "internal static TheaterPlan Plan(CampaignSnapshot s, SectorModel sectors, Func<int, CommanderProfile> sectorProfile, PlannerState state,\n                                         IReadOnlyList<BattleHistoryRecord> battles, ITerrainLineSource terrain, in MarchSpeedInputs speed, out string trace,\n                                         out EnemyPicture picture,");
        int build = plan.IndexOf("EnemyPicture.Build(", StringComparison.Ordinal);
        int evaluate = plan.IndexOf("Evaluate(inputs, candidates);", StringComparison.Ordinal);
        AssertTrue(build > 0 && evaluate > build, "the picture is built before the first Evaluate");
        AssertEqual(1, Regex.Matches(plan, @"EnemyPicture\.Build\(").Count, "once in Plan");
        AssertContains(BodyOf(src, "private static void EnsurePicture("), "if (c.Picture == null)", "a bare Evaluate builds it only when none was handed in");
        // Behaviour: a review that fills terrain runs two passes over one picture.
        var s = Csa(100000f);
        TheaterPlanner.Plan(s, SectorModel.Build(s), null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out string trace, out EnemyPicture picture);
        AssertTrue(picture != null && trace.Contains(" anchors="), "the picture comes back and the trace names each candidate's anchors: " + trace);
    }

    private static void ForesightPictureDefaultsOn()
    {
        AssertTrue(PlannerSettings.ForesightPicture, "all-on: the planner setting starts true");
        string plugin = ReadRepoFile("src/WhiskeyRealism/Plugin.cs");
        AssertTrue(Regex.IsMatch(plugin, "\"Campaign Planner\", \"Enable Strategic Foresight Picture\", true,"), "bound in the Campaign Planner section, default on");
        AssertContains(ReadRepoFile("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs"), "PlannerSettings.ForesightPicture = ConfigValue(Plugin.EnableStrategicForesightPicture, true);", "refreshed by the coordinator beside PlanTrace");
    }

    // The picture is still built and its row written with the flag off; only the planner's use of it is switched off.
    private static void PictureOffStillWritesThePictureRow()
    {
        WithFlag(false, () =>
        {
            var s = Csa(100000f);
            var plan = TheaterPlanner.Plan(s, SectorModel.Build(s), null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _, out EnemyPicture picture);
            AssertTrue(picture != null && picture.Anchors.Count == 3, "the picture is built");
            AssertContains(EnemyPicture.Row(picture, s), " total=100000 ", "and its row says what it holds");
            AssertEqual(200f, plan.CapitalReach, "but the capital reach is the relocation range");
            AssertEqual(0f, plan.CapitalReliefDays, "with no relief race");
            AssertEqual(0f, plan.CapitalCorpsPerDay, "and no corps rate");
        });
    }

    // The rollback is real: the earlier rules come back through the flag.
    private static void FlagOffRestoresTheD2Presumption()
    {
        var s = Csa(100000f);
        s.Beliefs.Add(Belief(917f, -555f, 6000f));
        WithFlag(false, () =>
        {
            var c = InputsFor(s);
            var candidates = new List<TheaterPlanner.Candidate>();
            TheaterPlanner.Evaluate(c, candidates);
            var romney = candidates.Single(x => x.ObjectiveId == 22);
            AssertTrue(romney.Scouted, "Romney is scouted by the belief near it");
            AssertEqual(0f, romney.Presumed, "D2: a scouted target's presumption is 0");
            var winchester = candidates.Single(x => x.ObjectiveId == 21);
            AssertEqual(Math.Max(TheaterPlanner.LandThreatNear(c, WinchesterX, WinchesterZ, 200f), winchester.Presumed), winchester.Enemy, "and the enemy is the larger of believed and presumed");
            TheaterPlanner.CapitalThreat(c, out float floor, out _);
            AssertEqual(0f, floor, "P7: no weight, no floor (no baseline)");
            AssertEqual(200f, c.CapitalReach, "the threat is taken over the relocation range");
        });
        WithFlag(true, () =>
        {
            var c = InputsFor(s);
            var candidates = new List<TheaterPlanner.Candidate>();
            TheaterPlanner.Evaluate(c, candidates);
            AssertTrue(candidates.Single(x => x.ObjectiveId == 22).Enemy >= TheaterPlanner.MinStrength(c), "the control: on, the same target is floored at the minimum");
        });
    }

    private static void FlagOffRestoresTheDepartmentSumAndTheOldScoutTest()
    {
        WithFlag(false, () =>
        {
            var c = InputsFor(Csa(100000f));
            var facts = FactsOf(c);
            float east = 0f;
            for (int k = 0; k < c.Sectors.Count; k++)
                if (c.Sectors.TheaterOf(k) == Theater.East) east += facts.Need[k];
            AssertNear(east, TheaterPlanner.DepartmentNeed(c, Theater.East), 0.01f, "the department need is the sum of the sector needs");
        });
        string allocator = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ForceAllocator.cs");
        string test = BodyOf(allocator, "private bool SectorMayGiveScout(int i)");
        AssertContains(test, "PlannerSettings.ForesightPicture", "the belief-driven test reads the flag");
        AssertContains(test, "_believed[k] >= _need[k] - 0.01f", "and keeps the earlier test behind it");
        AssertContains(test, "_believed[k] >= _need[k] - _believed[k]", "beside the new one: believed at least HoldNeed - BelievedHold");
    }

    // D9: a sector whose hold is set by a believed army refuses to give a scout when the draw would leave it short.
    private static void ASectorHoldingAgainstABelievedArmyStillRefusesAScout()
    {
        // The believed part of a sum that includes presumed men: belief-driven when the believed part is at least the presumed part.
        var s = Csa(100000f);
        s.Beliefs.Add(Belief(WinchesterX, WinchesterZ, 40000f));
        var c = InputsFor(s);
        var facts = FactsOf(c);
        int sector = SectorAtPlace(c, WinchesterX, WinchesterZ);
        float believedHold = TheaterPlanner.HoldRatio * facts.Threat[sector];
        AssertTrue(believedHold > TheaterPlanner.MinStrength(c), "the believed army sets a hold above the minimum");
        AssertTrue(facts.Need[sector] >= believedHold - 0.01f, "the need includes it");
        bool beliefDriven = believedHold > ForceAllocator.MinGarrisonStrength && believedHold >= facts.Need[sector] - believedHold;
        AssertEqual(beliefDriven, believedHold >= facts.Assigned[sector] * TheaterPlanner.HoldRatio - 0.01f && believedHold > ForceAllocator.MinGarrisonStrength,
            "belief-driven exactly when the believed part is at least the presumed part of the sum");
    }

    private static void RunPlannerTests()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("AnAnchorCountsInOneSectorsNeed", AnAnchorCountsInOneSectorsNeed),
            ("HoldNeedHoldsUnseenMenAtTheHoldRatio", HoldNeedHoldsUnseenMenAtTheHoldRatio),
            ("TheEastKeepsItsHoldWithoutADonorIn002A", TheEastKeepsItsHoldWithoutADonorIn002A),
            ("SecessionPostCountsNoElementASectorHolds", SecessionPostCountsNoElementASectorHolds),
            ("ASightingAt165DoesNotSwitchOffWashington", ASightingAt165DoesNotSwitchOffWashington),
            ("FloorUsesOnlyUndisplacedImportance", FloorUsesOnlyUndisplacedImportance),
            ("AnAnchorAloneMakesASectorFace", AnAnchorAloneMakesASectorFace),
            ("DepartmentNeedCountsEachElementOnce", DepartmentNeedCountsEachElementOnce),
            ("DepartmentNeedLandKindsWeightedTieToLowerSector", DepartmentNeedLandKindsWeightedTieToLowerSector),
            ("AHoldIsReleasedOnlyWhenEveryElementItFacesIsAtTheTarget", AHoldIsReleasedOnlyWhenEveryElementItFacesIsAtTheTarget),
            ("ASectorFacingNothingReleasesNothing", ASectorFacingNothingReleasesNothing),
            ("ACapacityBelowTheLiveMinimumIsIneligible", ACapacityBelowTheLiveMinimumIsIneligible),
            ("OddsRuleUnchangedAboveTheMinimum", OddsRuleUnchangedAboveTheMinimum),
            ("RetainNeverReenablesBelowTheMinimum", RetainNeverReenablesBelowTheMinimum),
            ("CapitalReachIsTheReliefRace", CapitalReachIsTheReliefRace),
            ("ReliefRaceExcludesTheWestFromRichmond", ReliefRaceExcludesTheWestFromRichmond),
            ("OneFarFormationSetsTheReach", OneFarFormationSetsTheReach),
            ("AFormationWithNoSpeedIsSkippedInTheRace", AFormationWithNoSpeedIsSkippedInTheRace),
            ("MissingSpeedArrayFallsBackToTheRange", MissingSpeedArrayFallsBackToTheRange),
            ("RailsNotCreditedInTheReach", RailsNotCreditedInTheReach),
            ("ReliefRaceCreditsNoRailForEitherSide", ReliefRaceCreditsNoRailForEitherSide),
            ("CapitalTriggerAt002AStart", CapitalTriggerAt002AStart),
            ("WashingtonCountsTowardRichmondsFloor", WashingtonCountsTowardRichmondsFloor),
            ("FloorHasVanillasBaseline", FloorHasVanillasBaseline),
            ("ThreatExcludesTheBaseline", ThreatExcludesTheBaseline),
            ("FloorIsNeverBelowFourVanillaNeed", FloorIsNeverBelowFourVanillaNeed),
            ("FloorIsNeverBelowVanillaNeedOnTheSnapshot", FloorIsNeverBelowVanillaNeedOnTheSnapshot),
            ("MinStrengthIsTheInputsField", MinStrengthIsTheInputsField),
            ("EstimateIsBelievedPlusPresumed", EstimateIsBelievedPlusPresumed),
            ("AScoutedTargetStillCarriesUnscoutedAnchorsNearIt", AScoutedTargetStillCarriesUnscoutedAnchorsNearIt),
            ("BalanceStillRanksByTheBelievedLayer", BalanceStillRanksByTheBelievedLayer),
            ("ElementsCarryTheirAssignedSector", ElementsCarryTheirAssignedSector),
            ("TheAssignmentIsRewrittenEachPass", TheAssignmentIsRewrittenEachPass),
            ("NearestByStraightDistanceWeightedByTheProbe", NearestByStraightDistanceWeightedByTheProbe),
            ("TheProportionalEstimateUsesEveryAnchorsShare", TheProportionalEstimateUsesEveryAnchorsShare),
            ("EnemyCapitalEstimateReportedUnderBothShares", EnemyCapitalEstimateReportedUnderBothShares),
            ("PictureBuiltOncePerReview", PictureBuiltOncePerReview),
            ("ForesightPictureDefaultsOn", ForesightPictureDefaultsOn),
            ("PictureOffStillWritesThePictureRow", PictureOffStillWritesThePictureRow),
            ("FlagOffRestoresTheD2Presumption", FlagOffRestoresTheD2Presumption),
            ("FlagOffRestoresTheDepartmentSumAndTheOldScoutTest", FlagOffRestoresTheDepartmentSumAndTheOldScoutTest),
            ("ASectorHoldingAgainstABelievedArmyStillRefusesAScout", ASectorHoldingAgainstABelievedArmyStillRefusesAScout),
        });
    }
}
