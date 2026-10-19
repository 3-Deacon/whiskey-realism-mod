using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Util;

using static TestHarness;
using static TestFixtures;

internal static class OwnershipTableTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("F6aReproducesTheShippedRoles", F6aReproducesTheShippedRoles),
            ("TheTakesTableIsTheSpecsTable", TheTakesTableIsTheSpecsTable),
            ("OwnershipRowsTakeOnlyFromTheirListedClaimants", OwnershipRowsTakeOnlyFromTheirListedClaimants),
            ("TheFixtureRegistryDrivesEveryRowCase", TheFixtureRegistryDrivesEveryRowCase),
            ("ASourceMayTakeOnlyItsListedClaimants", ASourceMayTakeOnlyItsListedClaimants),
            ("RowSourcesAreEmptyInF6a", RowSourcesAreEmptyInF6a),
            ("ClaimSourcesReadOneContext", ClaimSourcesReadOneContext),
            ("TheHoldNeverCountsOrReservesCommittedOrDepartmentMove", TheHoldNeverCountsOrReservesCommittedOrDepartmentMove),
            ("APinnedBuilderStaysAReserveBodyAsShipped", APinnedBuilderStaysAReserveBodyAsShipped),
            ("RecoverIsClaimedBeforeTheHolds", RecoverIsClaimedBeforeTheHolds),
            ("CapitalMayTakeCommittedOrDepartmentMove", CapitalMayTakeCommittedOrDepartmentMove),
            ("CapitalOverClaimsTakesOnlyItsListedClaimants", CapitalOverClaimsTakesOnlyItsListedClaimants),
            ("MassTakesOnlyItsListedClaimants", MassTakesOnlyItsListedClaimants),
            ("EveryClaimCarriesItsCountedSectorAndShare", EveryClaimCarriesItsCountedSectorAndShare),
            ("SectorHeldIsTheD9Tally", SectorHeldIsTheD9Tally),
            ("TheMapGroupPlayerIsCountedAnywayInTheHold", TheMapGroupPlayerIsCountedAnywayInTheHold),
            ("AMarchingBuilderIsNeverAHoldCandidate", AMarchingBuilderIsNeverAHoldCandidate),
            ("OneOwnerAtTheEndOfPlan", OneOwnerAtTheEndOfPlan),
            ("ThePlanTakesItsMassIntoTheTableBeforeAnyoneElseReadsIt", ThePlanTakesItsMassIntoTheTableBeforeAnyoneElseReadsIt),
            ("G3AndG4ReadOneOwnershipTable", G3AndG4ReadOneOwnershipTable),
            ("ADepartmentMoveFormationIsInNoMassAndNoReinforcementCandidate", ADepartmentMoveFormationIsInNoMassAndNoReinforcementCandidate),
            ("RoleOfCoversEveryRow", RoleOfCoversEveryRow),
            ("RoleSectorPerRowAsShipped", RoleSectorPerRowAsShipped),
            ("G4LaterStepsRunOverNoneAndMass", G4LaterStepsRunOverNoneAndMass),
            ("NoAllocatorFallbackWithoutAClaimTable", NoAllocatorFallbackWithoutAClaimTable),
            ("ABusyPinnedBuilderIsHoldBusy", ABusyPinnedBuilderIsHoldBusy),
            ("AStrayCommittedClaimHoldsInPlace", AStrayCommittedClaimHoldsInPlace),
            ("RedeployMovePriorityOrder", RedeployMovePriorityOrder),
            ("RedeployNeedsNoTempoRelease", RedeployNeedsNoTempoRelease),
            ("RedeployReasonsWithholdTheRightPools", RedeployReasonsWithholdTheRightPools),
            ("RedeployIsTakenOffVanillasOffensiveList", RedeployIsTakenOffVanillasOffensiveList),
            ("RedeployGiveUpFallsBackToHold", RedeployGiveUpFallsBackToHold),
            ("RedeployIsRankedWithoutThrowing", RedeployIsRankedWithoutThrowing),
            ("RedeployReadersHandleIt", RedeployReadersHandleIt),
            ("PlanGroupsSkipsRedeploy", PlanGroupsSkipsRedeploy),
            ("TheSummaryCountsRedeployAndCoveringMembers", TheSummaryCountsRedeployAndCoveringMembers),
            ("HoldFacingReadsTheChosenBody", HoldFacingReadsTheChosenBody),
            ("HoldFacingIgnoresCountedAnywayBodies", HoldFacingIgnoresCountedAnywayBodies),
            ("HoldFacingCreditsACapitalGuardStandingInTheSector", HoldFacingCreditsACapitalGuardStandingInTheSector),
            ("HoldFacingCreditsNoGuardMarchingToACapitalElsewhere", HoldFacingCreditsNoGuardMarchingToACapitalElsewhere),
        });
    }

    // The golden roles, frozen as literals from the build before F6a (main 507c08ee, run on the Roster below): "id role reason sector withheld target".
    private static readonly string[] Golden =
    {
        // 0 no depot ledger
        "1 Hold hold-reserve 2 True 1245,-680;2 Hold garrison 1 True 1247,-802;3 MainEffort mass 3 False 1285,-874;4 CapitalGuard capital-floor 0 True 1263,-1010;"
        + "5 Hold busy 0 False 1340,-1130;6 Recover recover 0 True 1263,-1010;7 CapitalGuard capital-floor 0 True 1263,-1010;8 Hold player-map-group 2 False 1245,-690;"
        + "9 Hold non-combat 1 False 1250,-820;10 Hold hold-reserve 2 True 1300,-700",
        // 1 the unready Army of Virginia is a Marching held builder (its task not yet at the site). THE ONE INTENDED DIFFERENCE from the build
        // before F6a: "a Marching builder is neither a hold candidate nor counted, and the bodies the hold reserves in its place". The old build
        // counted the unready Army of Virginia (4000) toward its sector's hold, so the Army of Northern Virginia stayed in the main effort's mass
        // ("3 MainEffort mass 3 False" and "2 Hold garrison 1 True"); now the sector is short without it, the Army of Northern Virginia is
        // reserved in its place (a Hold reserve of the sector, withheld), the Army of Virginia is a remainder Hold the allocator's depot step
        // can find (not withheld), and the mass has lost its body. Every other formation is as before.
        "1 Hold hold-reserve 2 True 1245,-680;2 Hold garrison 1 False 1247,-802;3 Hold hold-reserve 1 True 1285,-874;4 CapitalGuard capital-floor 0 True 1263,-1010;"
        + "5 Hold busy 0 False 1340,-1130;6 Recover recover 0 True 1263,-1010;7 CapitalGuard capital-floor 0 True 1263,-1010;8 Hold player-map-group 2 False 1245,-690;"
        + "9 Hold non-combat 1 False 1250,-820;10 Hold hold-reserve 2 True 1300,-700",
        // 2 the Reserve Corps is a Wished builder (pinned), and the planner has no main effort
        "1 Hold hold-reserve 2 True 1245,-680;2 Hold garrison 1 True 1247,-802;3 CapitalGuard capital-floor 0 True 1263,-1010;4 CapitalGuard capital-floor 0 True 1263,-1010;"
        + "5 Hold busy 0 False 1340,-1130;6 Recover recover 0 True 1263,-1010;7 DepotBuilder depot-task-pinned 1 True 1101,-553;8 Hold player-map-group 2 False 1245,-690;"
        + "9 Hold non-combat 1 False 1250,-820;10 Hold hold-reserve 2 True 1300,-700"
    };

    // TC-CLAIM-SHIPPED. The roster is the investigation's 06-23 CSA roster as far as the repository holds it: the unit keys of the sidecar's move
    // records (save2/whiskeyrealism.json, alliance 1: Army of Northern Virginia:199, Army of the Potomac:28, Army of Virginia:1735), the FX-002A
    // places, and the enemy beliefs of 06-23 (the 2nd Division, 6653 near Winchester; the Department of Virginia, 5214 at Fort Monroe). The sidecar
    // holds no strength, position, supply or readiness, so those are ASSUMED here: Potomac 19400 at Manassas, Virginia 4000 at Fredericksburg at
    // readiness step 1 (the investigation's unready screen), the rest invented to reach every claimant (a busy one, a starved one, a player map
    // group, a non-combat unit, cavalry). It is one world; the shipped allocator fixtures (the rest of the harness) are the other.
    private static void F6aReproducesTheShippedRoles()
    {
        for (int scenario = 0; scenario < 3; scenario++)
            AssertEqual(Golden[scenario], RolesOf(scenario), "the roles of scenario " + scenario);
    }

    // The oracle's world for one scenario: 0 no depot ledger, 1 the Army of Virginia a Marching held builder, 2 the Reserve Corps a Wished one.
    private static TheaterPlan OracleWorld(int scenario, out CampaignSnapshot s, out SectorModel sectors, out HeldBuilder[] pins,
                                           IReadOnlyList<IClaimSource> rowSources = null, PlannerState state = null)
    {
        s = EnemyPictureTests.Csa(100000f);
        foreach (var f in Roster()) s.Own.Add(f);
        s.Beliefs.Add(EnemyPictureTests.Belief(EnemyPictureTests.WinchesterX, EnemyPictureTests.WinchesterZ, 6653f));
        s.Beliefs.Add(EnemyPictureTests.Belief(1380f, -1110f, 5214f));
        sectors = SectorModel.Build(s);
        int fred = sectors.SectorAt(EnemyPictureTests.FredericksburgX, EnemyPictureTests.FredericksburgZ);
        pins = scenario == 0 ? null : new[] { new HeldBuilder { InstanceId = scenario == 1 ? 2 : 7, SectorId = fred, TargetX = 1101.3f, TargetZ = -553.8f, SiteX = 1101.3f, SiteZ = -553.8f, Wished = scenario == 2 } };
        return TheaterPlanner.Plan(s, sectors, null, state ?? new PlannerState(), null, null, ExecutorFixtures.Speed(), out _, null, pins, rowSources);
    }

    // The registry of plan-input fixtures (D11 requirement-claims, the F6a/F6b-2 merge seam): a real TheaterPlanner.Plan over a world, with
    // the snapshot and sectors it was made over. OneOwnerAtTheEndOfPlan and G3AndG4ReadOneOwnershipTable run over every entry; F6b-2 added the
    // covering world, a row-4 world and a row-5 world witness the rows F7 and F8 fill. F7 and F8 add theirs here without changing a test body.
    private sealed class PlanInput
    {
        public string Name;
        public Func<TheaterPlan> Make;
        public Claimant Witnesses;   // a claimant the world is built to have in its table (None: no claim is promised)
        public string WitnessReason;  // the reason that claim must carry (null: any): the covering world's Hold claim is a covering-force one
        public CampaignSnapshot S;
        public SectorModel Sectors;
    }

    private static readonly List<PlanInput> PlanInputs = BuildPlanInputs();

    private static List<PlanInput> BuildPlanInputs()
    {
        var list = new List<PlanInput>();
        for (int scenario = 0; scenario < 3; scenario++)
        {
            int sc = scenario;
            var input = new PlanInput { Name = "oracle " + sc };
            input.Make = () => OracleWorld(sc, out input.S, out input.Sectors, out _);
            list.Add(input);
        }
        var committed = new PlanInput { Name = "a row-4 source claims the garrison corps (2)", Witnesses = Claimant.Committed };
        committed.Make = () => OracleWorld(0, out committed.S, out committed.Sectors, out _, new IClaimSource[] { new ClaimsOneFormation(ClaimTable.RowCommitted, Claimant.Committed, 2) });
        list.Add(committed);
        var moving = new PlanInput { Name = "a row-5 source claims the Army of Northern Virginia (3)", Witnesses = Claimant.DepartmentMove };
        moving.Make = () => OracleWorld(0, out moving.S, out moving.Sectors, out _, new IClaimSource[] { new ClaimsOneFormation(ClaimTable.RowDepartmentMove, Claimant.DepartmentMove, 3) });
        list.Add(moving);
        var covering = new PlanInput { Name = "the covering force's world", Witnesses = Claimant.Hold, WitnessReason = ForceAllocator.ReasonCoveringForce };
        covering.Make = () => CoveringForceTests.PlanInputWorld(out covering.S, out covering.Sectors);
        list.Add(covering);
        return list;
    }

    // Whether the plan's table has the claim the world is built to witness: its claimant and, when the world names one, its reason (every world
    // has a Hold claim, so the covering world is witnessed by a covering-force one).
    private static bool WitnessesItsClaim(PlanInput input, TheaterPlan plan)
    {
        return input.Witnesses == Claimant.None
            || plan.Claims.Any(c => c.Claimant == input.Witnesses && (input.WitnessReason == null || string.Equals(c.Reason, input.WitnessReason, StringComparison.Ordinal)));
    }

    private static string RolesOf(int scenario)
    {
        var plan = OracleWorld(scenario, out var s, out var sectors, out var pins);
        var a = ForceAllocator.Allocate(plan, s, sectors, null, null, null, null, pins);
        return string.Join(";", a.Assignments.Select(x => x.InstanceId + " " + x.Role + " " + x.Reason + " " + x.SectorId + " " + x.Withheld
            + " " + (int)x.TargetX + "," + (int)x.TargetZ));
    }

    // ---- the rows (TC-CLAIM-ROWS) ----

    // D11 requirement-claims, the table's "Takes from" column, written out independently of ClaimTable.Takes. Row 7 takes only None: a
    // PinnedBuilder chosen as a reserve body keeps its claim (the hold counts it, the allocator still makes it a depot builder).
    private static readonly Dictionary<int, Claimant[]> SpecTakes = new Dictionary<int, Claimant[]>
    {
        { 1, new[] { Claimant.None } },
        { 2, new[] { Claimant.None, Claimant.PinnedBuilder } },
        { 3, new[] { Claimant.None } },
        { 4, new[] { Claimant.None } },
        { 5, new[] { Claimant.None } },
        { 6, new[] { Claimant.None, Claimant.Committed, Claimant.DepartmentMove } },
        { 7, new[] { Claimant.None } },
        { 8, new[] { Claimant.None, Claimant.Hold, Claimant.Committed } },
        { 10, new[] { Claimant.None } },
    };

    private static void TheTakesTableIsTheSpecsTable()
    {
        foreach (var row in SpecTakes)
            foreach (Claimant c in Enum.GetValues(typeof(Claimant)))
                AssertEqual(row.Value.Contains(c), ClaimTable.Takes(row.Key, c), "row " + row.Key + " takes " + c);
        AssertFalse(ClaimTable.Takes(9, Claimant.None), "row 9 (D8's garrison pledge) is not a claimant");
    }

    // One case per row: a world with one formation (index 0) that the row's producer would claim, the producer, and the claimant it makes.
    private sealed class RowCase
    {
        public int Row;
        public Claimant Produces;
        public Func<CampaignSnapshot> World;
        public Action<CampaignSnapshot, Claim[]> Apply;
    }

    private static CampaignSnapshot OneCorps(float x, float strength, Func<FormationRecord, FormationRecord> tweak = null)
    {
        var s = ExecutorFixtures.Wide();
        var f = ExecutorFixtures.Corps(1, x, strength);
        s.Own.Add(tweak != null ? tweak(f) : f);
        return s;
    }

    private sealed class TakesEverythingSource : IClaimSource
    {
        private readonly int _row;
        private readonly Claimant _claimant;
        public TakesEverythingSource(int row, Claimant claimant) { _row = row; _claimant = claimant; }
        public int Row => _row;
        public void Produce(in ClaimContext ctx, Claim[] claims)
        {
            for (int i = 0; i < claims.Length; i++) claims[i].Claimant = _claimant;
        }
    }

    // A source claims whatever it likes; ClaimTable.ApplySource refuses (throws) a take its row does not list, so a row case for a source row
    // reads "unchanged" when the start is not listed.
    private static void ApplyThroughSource(CampaignSnapshot s, Claim[] claims, int row, Claimant produces)
    {
        Claimant start = claims[0].Claimant;
        try
        {
            ClaimTable.ApplySource(new TakesEverythingSource(row, produces), new ClaimContext(s, ExecutorFixtures.Sectors(s), null, null, null, null), claims);
        }
        catch (InvalidOperationException)
        {
            claims[0].Claimant = start;
        }
    }

    private static readonly List<RowCase> RowCases = new List<RowCase>
    {
        new RowCase { Row = 1, Produces = Claimant.PinnedBuilder, World = () => OneCorps(350f, 5000f),
            Apply = (s, c) => ClaimTable.ClaimPinned(s, c, new[] { new HeldBuilder { InstanceId = 1, Wished = true, SectorId = 1, TargetX = 330f, TargetZ = 0f } }) },
        new RowCase { Row = 2, Produces = Claimant.Immobile, World = () => OneCorps(350f, 5000f, f => { f.Flags |= FormationFlags.InBattle; return f; }),
            Apply = (s, c) => ClaimTable.ClaimImmobile(s, c) },
        new RowCase { Row = 3, Produces = Claimant.Recover, World = () => OneCorps(350f, 5000f, f => { f.Supply01 = 0.1f; return f; }),
            Apply = (s, c) => ClaimTable.ClaimRecover(s, c) },
        new RowCase { Row = 4, Produces = Claimant.Committed, World = () => OneCorps(350f, 5000f),
            Apply = (s, c) => ApplyThroughSource(s, c, 4, Claimant.Committed) },
        new RowCase { Row = 5, Produces = Claimant.DepartmentMove, World = () => OneCorps(350f, 5000f),
            Apply = (s, c) => ApplyThroughSource(s, c, 5, Claimant.DepartmentMove) },
        new RowCase { Row = 6, Produces = Claimant.Capital, World = () => OneCorps(20f, 5000f),
            Apply = (s, c) => ReserveRules.Capital(s, 3000f, c) },
        new RowCase { Row = 7, Produces = Claimant.Hold, World = () => OneCorps(350f, 5000f),
            Apply = (s, c) =>
            {
                var sectors = ExecutorFixtures.Sectors(s);
                var need = new float[sectors.Count];
                need[sectors.SectorAt(350f, 0f)] = 4000f;
                ReserveRules.Hold(s, sectors, need, new float[sectors.Count], new float[sectors.Count], c, null, null, new float[sectors.Count]);
            } },
        // F6b-2's fixture: the same hold pass with a covering input, whose members are chosen from the reserve (row 7 takes only None, covering or not).
        new RowCase { Row = 7, Produces = Claimant.Hold, World = () => OneCorps(350f, 5000f),
            Apply = (s, c) =>
            {
                var sectors = ExecutorFixtures.Sectors(s);
                int k = sectors.SectorAt(350f, 0f);
                var need = new float[sectors.Count];
                need[k] = 4000f;
                var share = new float[sectors.Count];
                share[k] = 4000f;
                var covering = new CoveringInput
                {
                    OrderX = 350f, ForwardX = 350f, Radius = s.RelocationRange, Need = 4000f, AxisShare = share, OnePost = true, PostKey = "P|P",
                    IncumbentSector = new[] { -1 }, IncumbentArea = new[] { -1 }, Department = new Theater[1]
                };
                ReserveRules.Hold(s, sectors, need, new float[sectors.Count], new float[sectors.Count], c, covering, null, new float[sectors.Count]);
            } },
        new RowCase { Row = 8, Produces = Claimant.Mass, World = () => OneCorps(350f, 5000f),
            Apply = (s, c) => ClaimTable.TakeMass(c, s, new[] { 1 }) },
    };

    private static void OwnershipRowsTakeOnlyFromTheirListedClaimants()
    {
        foreach (var rc in RowCases)
            foreach (Claimant start in Enum.GetValues(typeof(Claimant)))
            {
                var s = rc.World();
                var claims = ClaimTable.Fresh(s);
                claims[0].Claimant = start;
                rc.Apply(s, claims);
                Claimant expected = SpecTakes[rc.Row].Contains(start) ? rc.Produces : start;
                AssertEqual(expected, claims[0].Claimant, "row " + rc.Row + " over a formation claimed " + start);
            }
    }

    // F6b-2, F7 and F8 add their per-row fixtures here (a covering input, a last mass, a department move) without changing a test body.
    private static void TheFixtureRegistryDrivesEveryRowCase()
    {
        for (int row = 1; row <= 8; row++)
            AssertTrue(RowCases.Count(rc => rc.Row == row) >= 1, "row " + row + " has its case");
        foreach (var rc in RowCases)
            AssertTrue(SpecTakes.ContainsKey(rc.Row), "a case's row is a row of the table: " + rc.Row);
    }

    private static void ASourceMayTakeOnlyItsListedClaimants()
    {
        var s = OneCorps(350f, 5000f);
        var claims = ClaimTable.Fresh(s);
        claims[0].Claimant = Claimant.Capital;
        AssertThrows(() => ClaimTable.ApplySource(new TakesEverythingSource(4, Claimant.Committed), new ClaimContext(s, ExecutorFixtures.Sectors(s), null, null, null, null), claims),
            "a Capital claim is not row 4's to take");
        claims[0].Claimant = Claimant.None;
        ClaimTable.ApplySource(new TakesEverythingSource(4, Claimant.Committed), new ClaimContext(s, ExecutorFixtures.Sectors(s), null, null, null, null), claims);
        AssertEqual(Claimant.Committed, claims[0].Claimant, "an unclaimed formation is");
    }

    private static void RowSourcesAreEmptyInF6a()
    {
        AssertEqual(0, ClaimTable.RowSources.Count, "rows 4 and 5 have no producer until F7 and F8 fill theirs");
    }

    private sealed class RecordingSource : IClaimSource
    {
        private readonly int _row;
        private readonly List<string> _log;
        public RecordingSource(int row, List<string> log) { _row = row; _log = log; }
        public int Row => _row;
        public CampaignSnapshot Seen;
        public void Produce(in ClaimContext ctx, Claim[] claims)
        {
            Seen = ctx.Snapshot;
            _log.Add("row" + _row + ":" + claims.Length + ":" + (ctx.Held != null ? ctx.Held.Count : -1) + ":" + (ctx.State != null) + ":" + (ctx.GaveUp != null ? ctx.GaveUp.Count : -1));
        }
    }

    private static void ClaimSourcesReadOneContext()
    {
        var s = OneCorps(350f, 5000f);
        var log = new List<string>();
        var four = new RecordingSource(4, log);
        var five = new RecordingSource(5, log);
        var held = new[] { new HeldBuilder { InstanceId = 9 } };
        var gaveUp = new GaveUpMove[0];
        var state = new PlannerState();
        var sectors = ExecutorFixtures.Sectors(s);
        ClaimTable.Build(s, sectors, new float[sectors.Count], new float[sectors.Count], new float[sectors.Count], 0f, held, gaveUp, state, null,
            new IClaimSource[] { five, four }, null, out _);
        AssertEqual("row4:1:1:True:0|row5:1:1:True:0", string.Join("|", log), "each source is called once, in row order, with the snapshot's, the held list, the state and the give-ups of the one context");
        AssertTrue(ReferenceEquals(s, four.Seen) && ReferenceEquals(s, five.Seen), "both read the same snapshot");
    }

    // ---- what the hold counts and reserves (TC-CLAIM-ROWS, TC-CLAIM-COUNTED) ----

    // A sector with a need of `need` (the one of x 350) in the Wide fixture, and the arrays ReserveRules.Hold takes.
    private sealed class Needs
    {
        public SectorModel Sectors;
        public int Sector;
        public float[] Need, NearX, NearZ, Held;
        public Needs(CampaignSnapshot s, float need)
        {
            Sectors = ExecutorFixtures.Sectors(s);
            Sector = Sectors.SectorAt(350f, 0f);
            Need = new float[Sectors.Count];
            NearX = new float[Sectors.Count];
            NearZ = new float[Sectors.Count];
            Held = new float[Sectors.Count];
            Need[Sector] = need;
            NearX[Sector] = 450f;
        }
    }

    private static Claim[] BuildClaims(CampaignSnapshot s, Needs n, float capitalFloor = 0f, IReadOnlyList<HeldBuilder> held = null)
    {
        return ClaimTable.Build(s, n.Sectors, n.Need, n.NearX, n.NearZ, capitalFloor, held, null, null, null, ClaimTable.RowSources, null, out n.Held);
    }

    private static int IndexOf(CampaignSnapshot s, int id) => s.IndexOfOwn(id);

    private static void TheHoldNeverCountsOrReservesCommittedOrDepartmentMove()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 340f, 5000f));
        s.Own.Add(ExecutorFixtures.Corps(2, 350f, 5000f));
        s.Own.Add(ExecutorFixtures.Corps(3, 360f, 5000f));
        var n = new Needs(s, 8000f);
        var claims = ClaimTable.Fresh(s);
        claims[0].Claimant = Claimant.Committed;
        claims[1].Claimant = Claimant.DepartmentMove;
        var held = new float[n.Sectors.Count];
        ReserveRules.Hold(s, n.Sectors, n.Need, n.NearX, n.NearZ, claims, null, null, held);
        AssertEqual(Claimant.Committed, claims[0].Claimant, "a committed formation is not reserved");
        AssertEqual(Claimant.DepartmentMove, claims[1].Claimant, "nor a department move");
        AssertEqual(Claimant.Hold, claims[2].Claimant, "the free formation is the reserve");
        AssertEqual(-1, claims[0].SectorId, "the committed counts in no sector");
        AssertEqual(-1, claims[1].SectorId, "nor the department move");
        AssertNear(5000f, held[n.Sector], 0.01f, "the hold counts only the reserve body, not the two it must leave alone");
    }

    private static void APinnedBuilderStaysAReserveBodyAsShipped()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 350f, 5000f));
        var n = new Needs(s, 4000f);
        var claims = BuildClaims(s, n, 0f, new[] { new HeldBuilder { InstanceId = 1, Wished = true, SectorId = n.Sector, TargetX = 330f, TargetZ = 0f } });
        AssertEqual(Claimant.PinnedBuilder, claims[0].Claimant, "the claim stays the pin's");
        AssertEqual(n.Sector, claims[0].SectorId, "but the hold counted it, as shipped");
        AssertNear(5000f, claims[0].Share, 0.01f, "at its strength");
        AssertNear(5000f, n.Held[n.Sector], 0.01f, "into the sector's tally");
        AssertTrue(ClaimTable.IsReserveBody(claims[0], s.Own[0], s.MinMoraleForMovement), "and it is a chosen reserve body for D8's HoldFacing");
    }

    private static void RecoverIsClaimedBeforeTheHolds()
    {
        var s = ExecutorFixtures.Wide();
        var starved = ExecutorFixtures.Corps(1, 350f, 6000f);
        starved.Supply01 = 0.1f;
        s.Own.Add(starved);
        s.Own.Add(ExecutorFixtures.Corps(2, 360f, 5000f));
        var n = new Needs(s, 4000f);
        var claims = BuildClaims(s, n);
        AssertEqual(Claimant.Recover, claims[0].Claimant, "a starved formation is leaving for supply");
        AssertEqual(-1, claims[0].SectorId, "it holds nothing where it stands");
        AssertEqual(Claimant.Hold, claims[1].Claimant, "so the healthy one is the reserve");
        AssertNear(5000f, n.Held[n.Sector], 0.01f, "and the tally is its strength alone");
    }

    private static void CapitalMayTakeCommittedOrDepartmentMove()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 10f, 4000f));
        s.Own.Add(ExecutorFixtures.Corps(2, 20f, 4000f));
        var claims = ClaimTable.Fresh(s);
        claims[0].Claimant = Claimant.Committed;
        claims[1].Claimant = Claimant.DepartmentMove;
        ReserveRules.Capital(s, 6000f, claims);
        AssertEqual(Claimant.Capital, claims[0].Claimant, "home defence comes first: a committed formation near the capital is a guard");
        AssertEqual(Claimant.Capital, claims[1].Claimant, "so is one on a department move");
        AssertNear(s.CapitalX, claims[0].TargetX, 0.01f, "marching to the capital");
    }

    private static void CapitalOverClaimsTakesOnlyItsListedClaimants()
    {
        var s = ExecutorFixtures.Wide();
        for (int i = 0; i < 5; i++) s.Own.Add(ExecutorFixtures.Corps(i + 1, 10f * (i + 1), 4000f));
        var claims = ClaimTable.Fresh(s);
        claims[0].Claimant = Claimant.PinnedBuilder;
        claims[1].Claimant = Claimant.Hold;
        claims[2].Claimant = Claimant.Recover;
        claims[3].Claimant = Claimant.Committed;
        ReserveRules.Capital(s, 8000f, claims);
        AssertEqual(Claimant.PinnedBuilder, claims[0].Claimant, "a pinned builder is not a guard");
        AssertEqual(Claimant.Hold, claims[1].Claimant, "a reserve is not overwritten");
        AssertEqual(Claimant.Recover, claims[2].Claimant, "nor a formation leaving for supply");
        AssertEqual(Claimant.Capital, claims[3].Claimant, "the committed one is taken");
        AssertEqual(Claimant.Capital, claims[4].Claimant, "and the free one: 8000 meets the floor");
    }

    private static void MassTakesOnlyItsListedClaimants()
    {
        var s = ExecutorFixtures.Wide();
        for (int i = 0; i < 9; i++) s.Own.Add(ExecutorFixtures.Corps(i + 1, 300f + 10f * i, 4000f));
        var claims = ClaimTable.Fresh(s);
        for (int i = 0; i < 9; i++) claims[i].Claimant = (Claimant)i;   // None .. Mass, one each
        ClaimTable.TakeMass(claims, s, s.Own.Select(f => f.InstanceId).ToArray());
        for (int i = 0; i < 9; i++)
        {
            var start = (Claimant)i;
            AssertEqual(SpecTakes[8].Contains(start) ? Claimant.Mass : start, claims[i].Claimant, "the mass over a " + start + " claim");
        }
    }

    private static void EveryClaimCarriesItsCountedSectorAndShare()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 340f, 3000f));
        var busy = ExecutorFixtures.Corps(2, 350f, 2500f);
        busy.Flags |= FormationFlags.InBattle;
        s.Own.Add(busy);
        s.Own.Add(ExecutorFixtures.Corps(3, 20f, 4000f));   // far from the sector
        var n = new Needs(s, 9000f);
        var claims = BuildClaims(s, n);
        AssertEqual(n.Sector, claims[0].SectorId, "the reserve counted in its sector");
        AssertNear(3000f, claims[0].Share, 0.01f, "at its strength");
        AssertEqual(n.Sector, claims[1].SectorId, "the busy formation holds where it stands and counts");
        AssertNear(2500f, claims[1].Share, 0.01f, "at its strength");
        AssertEqual(-1, claims[2].SectorId, "a formation in a sector with no need counts nowhere");
        AssertEqual(0f, claims[2].Share, "and added nothing");
    }

    private static void SectorHeldIsTheD9Tally()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 340f, 3000f));
        var busy = ExecutorFixtures.Corps(2, 350f, 2500f);
        busy.Flags |= FormationFlags.InBattle;
        s.Own.Add(busy);
        var unready = ExecutorFixtures.Corps(3, 360f, 1500f);
        unready.ReadinessStep = 1;
        s.Own.Add(unready);
        var n = new Needs(s, 20000f);
        var claims = BuildClaims(s, n);
        float sum = 0f;
        for (int i = 0; i < claims.Length; i++)
            if (claims[i].SectorId == n.Sector) sum += claims[i].Share;
        AssertNear(7000f, sum, 0.01f, "3000 reserved + 2500 busy + 1500 unready");
        AssertNear(sum, n.Held[n.Sector], 0.01f, "the sector's tally is the sum of what its formations added");
        AssertEqual(0f, n.Held[0], "a sector with no need holds a tally of 0");
    }

    private static void TheMapGroupPlayerIsCountedAnywayInTheHold()
    {
        var s = ExecutorFixtures.Wide();
        var player = ExecutorFixtures.Corps(1, 350f, 5000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        player.Morale = 0.5f;
        s.Own.Add(player);
        var n = new Needs(s, 9000f);
        var claims = BuildClaims(s, n);
        AssertEqual(Claimant.None, claims[0].Claimant, "he is neither reserved nor claimed");
        AssertEqual(n.Sector, claims[0].SectorId, "but he counts where he stands");
        AssertNear(2500f, claims[0].Share, 0.01f, "at strength x morale, as vanilla counts him");
    }

    private static void AMarchingBuilderIsNeverAHoldCandidate()
    {
        var s = ExecutorFixtures.Wide();
        var unready = ExecutorFixtures.Corps(1, 350f, 4000f);
        unready.ReadinessStep = 1;
        s.Own.Add(unready);
        s.Own.Add(ExecutorFixtures.Corps(2, 355f, 5000f));
        var n = new Needs(s, 20000f);
        var marching = new[] { new HeldBuilder { InstanceId = 1, Wished = false, SectorId = n.Sector } };
        var claims = BuildClaims(s, n, 0f, marching);
        AssertEqual(Claimant.None, claims[0].Claimant, "a Marching builder keeps an unclaimed claim, so the depot step finds it");
        AssertEqual(-1, claims[0].SectorId, "and is counted nowhere: it is leaving for its site");
        AssertNear(5000f, n.Held[n.Sector], 0.01f, "the sector's tally is the other formation's");
        var plain = BuildClaims(s, new Needs(s, 20000f), 0f, null);
        AssertEqual(n.Sector, plain[0].SectorId, "control: with no held list the unready formation is counted anyway");

        var ready = ExecutorFixtures.Corps(3, 340f, 3000f);
        s.Own.Add(ready);
        var viaReady = BuildClaims(s, new Needs(s, 20000f), 0f, new[] { new HeldBuilder { InstanceId = 3, Wished = false } });
        AssertEqual(Claimant.None, viaReady[2].Claimant, "a ready Marching builder is no reserve body either");
    }

    // ---- one table for the plan and the allocator (TC-CLAIM-ONE) ----

    private static void OneOwnerAtTheEndOfPlan()
    {
        foreach (var input in PlanInputs)
        {
            var plan = input.Make();
            var s = input.S;
            string label = input.Name + ": ";
            AssertTrue(WitnessesItsClaim(input, plan), label + "the world has the " + input.Witnesses + " claim it is built to witness" + (input.WitnessReason != null ? " (reason " + input.WitnessReason + ")" : ""));
            AssertEqual(s.Own.Count, plan.Claims.Count, label + "one claim per own formation");
            for (int i = 0; i < s.Own.Count; i++)
                AssertEqual(s.Own[i].InstanceId, plan.Claims[i].InstanceId, label + "in snapshot order, formation " + i);
            foreach (int id in plan.MassInstanceIds)
                AssertEqual(Claimant.Mass, plan.Claims[s.IndexOfOwn(id)].Claimant, label + "a mass member is a Mass claim: " + id);
            AssertEqual(plan.MassInstanceIds.Count, plan.Claims.Count(c => c.Claimant == Claimant.Mass), label + "and a Mass claim is a mass member");
            AssertEqual(s.Own.Count, plan.Claims.Select(c => c.InstanceId).Distinct().Count(), label + "no formation is claimed twice");
        }
        AssertTrue(PlanInputs.Count >= 6, "the registry holds the oracle worlds, the row worlds and the covering world");
    }

    // D11 requirement-claims: rows 1 to 7 are built before the candidates (Evaluate), row 8 right after SelectMass, row 9 (D8's pledge) reads
    // the table after that, and the table is published last.
    private static void ThePlanTakesItsMassIntoTheTableBeforeAnyoneElseReadsIt()
    {
        string planner = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/TheaterPlanner.cs");
        string plan = BodyOf(planner, "internal static TheaterPlan Plan(CampaignSnapshot s, SectorModel sectors, Func<int, CommanderProfile> sectorProfile, PlannerState state,\n                                         IReadOnlyList<BattleHistoryRecord> battles, ITerrainLineSource terrain, in MarchSpeedInputs speed, out string trace,\n                                         out EnemyPicture picture");
        int evaluate = plan.IndexOf("Evaluate(inputs, candidates);", StringComparison.Ordinal);
        int select = plan.IndexOf("SelectMass(inputs, inputs.Scratch", StringComparison.Ordinal);
        int take = plan.IndexOf("ClaimTable.TakeMass(inputs.Claims, s, plan.MassInstanceIds);", StringComparison.Ordinal);
        int post = plan.IndexOf("PostFromOutside(inputs, plan);", StringComparison.Ordinal);
        int publish = plan.IndexOf("plan.Claims.AddRange(inputs.Claims);", StringComparison.Ordinal);
        AssertTrue(evaluate >= 0 && evaluate < select && select < take && take < post && post < publish, "rows 1-7 in Evaluate, the mass right after it is selected, the garrison pledge, then the publish");
    }

    // The allocator maps the plan's claims, and nothing else decides a role the table gives: every claim other than None and Mass is its
    // RoleOf in the allocation, and changing a claim of the published table changes the role.
    private static void G3AndG4ReadOneOwnershipTable()
    {
        foreach (var input in PlanInputs)
        {
            var plan = input.Make();
            var s = input.S;
            var sectors = input.Sectors;
            string label = input.Name + ": ";
            AssertTrue(WitnessesItsClaim(input, plan), label + "the world has the " + input.Witnesses + " claim it is built to witness" + (input.WitnessReason != null ? " (reason " + input.WitnessReason + ")" : ""));
            var a = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
            for (int i = 0; i < s.Own.Count; i++)
            {
                var claim = plan.Claims[i];
                if (claim.Claimant == Claimant.None || claim.Claimant == Claimant.Mass) continue;
                ForceAllocator.RoleOf(in claim, s.Own[i], out var role, out var reason, out var withheld);
                a.TryGet(claim.InstanceId, out var x);
                AssertEqual(role, x.Role, label + "formation " + claim.InstanceId + " (" + claim.Claimant + ") has the role of its claim");
                AssertEqual(reason, x.Reason, label + "and its reason");
                // A Hold is also withheld by the sector's prefix (WithholdHoldPrefixes, a later step over the sector's holds), never un-withheld.
                AssertTrue(x.Withheld == withheld || (role == FormationRole.Hold && x.Withheld), label + "and its withholding");
            }
            int hold = plan.Claims.ToList().FindIndex(c => c.Claimant == Claimant.Hold && c.Reason == null);
            AssertTrue(hold >= 0, label + "the world has a hold reserve");
            var changed = plan.Claims[hold];
            changed.Claimant = Claimant.Recover;
            plan.Claims[hold] = changed;
            var again = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
            again.TryGet(changed.InstanceId, out var now);
            AssertEqual(FormationRole.Recover, now.Role, label + "a claim rewritten in the published table is the role the allocator hands out: it recomputes no reserve");
        }
    }

    // D11 requirement-claims row 8: the mass takes only None, Hold and Committed, so a formation a department move owns is in no mass; and it
    // is no candidate for D6's one reinforcement either (that pick runs over the same capacity). The control: with no row-5 source the
    // formation, put in the West department, is the reinforcement the mass takes.
    private static void ADepartmentMoveFormationIsInNoMassAndNoReinforcementCandidate()
    {
        var control = new PlannerState();
        OracleWorld(0, out var probe, out _, out _);
        control.Transfer(probe.Own[probe.IndexOfOwn(3)].UnitKey, Theater.West);
        var free = OracleWorld(0, out _, out _, out _, null, control);
        AssertEqual(3, free.ReinforcementInstanceId, "control: formation 3, a West formation under an East target, is the decision's reinforcement");
        AssertTrue(free.MassInstanceIds.Contains(3), "and so in the mass");
        var moved = new PlannerState();
        moved.Transfer(probe.Own[probe.IndexOfOwn(3)].UnitKey, Theater.West);
        var plan = OracleWorld(0, out var s, out _, out _, new IClaimSource[] { new ClaimsOneFormation(ClaimTable.RowDepartmentMove, Claimant.DepartmentMove, 3) }, moved);
        AssertEqual(Claimant.DepartmentMove, plan.Claims[s.IndexOfOwn(3)].Claimant, "the row-5 source owns formation 3");
        AssertFalse(plan.MassInstanceIds.Contains(3), "it is in no mass: " + string.Join(",", plan.MassInstanceIds));
        AssertTrue(plan.ReinforcementInstanceId != 3, "and is not the reinforcement");
        AssertEqual(plan.MassInstanceIds.Count, plan.Claims.Count(c => c.Claimant == Claimant.Mass), "every mass member is a Mass claim");
        // The garrison's inside pledge skips it as it skips the mass (row 9): source pin, the secession fixtures not reaching a row-5 claim.
        string post = BodyOf(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/TheaterPlanner.cs"), "private static void PostFromOutside(");
        AssertTrue(post.Contains("Claimant.Committed") && post.Contains("Claimant.DepartmentMove"), "PostFromOutside's inside pledge names the two claimants it skips");
    }

    private static void RoleOfCoversEveryRow()
    {
        var f = ExecutorFixtures.Corps(1, 100f, 5000f);
        void Check(string label, Claim claim, in FormationRecord formation, FormationRole role, string reason, bool withheld)
        {
            ForceAllocator.RoleOf(in claim, in formation, out var r, out var why, out var w);
            AssertEqual(role, r, label + ": role");
            AssertEqual(reason, why, label + ": reason");
            AssertEqual(withheld, w, label + ": withheld");
        }
        Claim C(Claimant c, string reason = null, float tx = 100f) => new Claim { InstanceId = 1, Claimant = c, SectorId = 0, TargetX = tx, TargetZ = 0f, Reason = reason };
        Check("row 1", C(Claimant.PinnedBuilder), f, FormationRole.DepotBuilder, ForceAllocator.ReasonPinnedBuilder, true);
        var busy = ExecutorFixtures.Corps(1, 100f, 5000f);
        busy.Flags |= FormationFlags.InBattle;
        Check("row 2 busy", C(Claimant.Immobile), busy, FormationRole.Hold, ForceAllocator.ReasonBusy, false);
        var nonCombat = ExecutorFixtures.Corps(1, 100f, 5000f);
        nonCombat.Flags = 0;
        Check("row 2 non-combat", C(Claimant.Immobile), nonCombat, FormationRole.Hold, ForceAllocator.ReasonNonCombat, false);
        var command = ExecutorFixtures.Corps(1, 100f, 5000f);
        command.Chain = WlChainClass.PlayerCommand;
        Check("row 2 player command", C(Claimant.Immobile), command, FormationRole.Hold, ForceAllocator.ReasonPlayerCommand, false);
        Check("row 3", C(Claimant.Recover), f, FormationRole.Recover, ForceAllocator.ReasonRecover, true);
        Check("row 4", C(Claimant.Committed), f, FormationRole.Hold, ForceAllocator.ReasonCommittedWait, false);
        Check("row 5", C(Claimant.DepartmentMove), f, FormationRole.Redeploy, ForceAllocator.ReasonDepartmentReinforcement, false);
        Check("row 6", C(Claimant.Capital), f, FormationRole.CapitalGuard, ForceAllocator.ReasonCapitalFloor, true);
        Check("row 7", C(Claimant.Hold), f, FormationRole.Hold, ForceAllocator.ReasonHoldReserve, false);
        Check("row 7 covering at its post", C(Claimant.Hold, ForceAllocator.ReasonCoveringForce, 105f), f, FormationRole.Hold, ForceAllocator.ReasonCoveringForce, true);
        Check("row 7 covering on its way", C(Claimant.Hold, ForceAllocator.ReasonCoveringForce, 400f), f, FormationRole.Redeploy, ForceAllocator.ReasonCoveringForce, true);
        AssertThrows(() => { var none = C(Claimant.None); ForceAllocator.RoleOf(in none, in f, out _, out _, out _); }, "None is placed by the later steps");
        AssertThrows(() => { var mass = C(Claimant.Mass); ForceAllocator.RoleOf(in mass, in f, out _, out _, out _); }, "so is the mass");
    }

    // The role sector each row hands out, as the allocator gave it before the table: the pin's for a depot builder, where it stands for the
    // immobile and the recovering, none for a push that ended, the capital's for a guard, the counted sector for a reserve or a covering
    // member, and the sector of the target for a department's reinforcement.
    private static void RoleSectorPerRowAsShipped()
    {
        var s = ExecutorFixtures.Wide();
        var busy = ExecutorFixtures.Corps(2, 350f, 3000f);
        busy.Flags |= FormationFlags.InBattle;
        s.Own.Add(ExecutorFixtures.Corps(1, 100f, 3000f));
        s.Own.Add(busy);
        s.Own.Add(ExecutorFixtures.Corps(3, 550f, 3000f));
        s.Own.Add(ExecutorFixtures.Corps(4, 100f, 3000f));
        s.Own.Add(ExecutorFixtures.Corps(5, 20f, 3000f));
        s.Own.Add(ExecutorFixtures.Corps(6, 20f, 3000f));
        s.Own.Add(ExecutorFixtures.Corps(7, 360f, 3000f));
        s.Own.Add(ExecutorFixtures.Corps(8, 20f, 3000f));
        var sectors = ExecutorFixtures.Sectors(s);
        int west = sectors.SectorAt(100f, 0f), middle = sectors.SectorAt(350f, 0f), east = sectors.SectorAt(550f, 0f);
        var claims = ClaimTable.Fresh(s);
        void Set(int index, Claimant claimant, int sector = -1, float tx = float.NaN, string reason = null)
        {
            claims[index].Claimant = claimant;
            claims[index].SectorId = sector;
            claims[index].Reason = reason;
            if (!float.IsNaN(tx)) claims[index].TargetX = tx;
        }
        Set(0, Claimant.PinnedBuilder, tx: 330f);
        Set(1, Claimant.Immobile);
        Set(2, Claimant.Recover);
        Set(3, Claimant.Committed);
        Set(4, Claimant.DepartmentMove, tx: 520f);
        Set(5, Claimant.Capital, tx: 0f);
        Set(6, Claimant.Hold, middle);
        Set(7, Claimant.Hold, east, 400f, ForceAllocator.ReasonCoveringForce);
        var plan = new TheaterPlan { Alliance = s.Alliance, DaySerial = s.DaySerial };
        plan.Claims.AddRange(claims);
        plan.SectorHeld = new float[sectors.Count];
        var pin = new[] { new HeldBuilder { InstanceId = 1, Wished = true, SectorId = middle, TargetX = 330f, TargetZ = 0f } };
        var a = ForceAllocator.Allocate(plan, s, sectors, null, null, null, null, pin);
        AssertEqual(middle, At(a, 1).SectorId, "the pinned builder: the pin's sector");
        AssertEqual(middle, At(a, 2).SectorId, "the busy formation: where it stands");
        AssertEqual(east, At(a, 3).SectorId, "the starved one: where it stands");
        AssertEqual(-1, At(a, 4).SectorId, "a push that ended: none");
        AssertEqual(sectors.SectorAt(520f, 0f), At(a, 5).SectorId, "a department move: the sector of its target");
        AssertEqual(sectors.SectorAt(0f, 0f), At(a, 6).SectorId, "a capital guard: the capital's");
        AssertEqual(middle, At(a, 7).SectorId, "a reserve: the sector it was counted in");
        AssertEqual(FormationRole.Redeploy, At(a, 8).Role, "a covering member far from its post marches");
        AssertEqual(east, At(a, 8).SectorId, "and keeps its recorded sector");
        AssertEqual(west, sectors.SectorAt(100f, 0f), "(fixture: x 100 is the west)");
    }

    private static Assignment At(Allocation a, int id)
    {
        AssertTrue(a.TryGet(id, out var x), "formation " + id + " has an assignment");
        return x;
    }

    // The later steps (garrisons, depot builders, recon, the reserve split, the posts, the main effort, the remainder) run over the formations
    // claimed None or Mass: a smaller committed formation of the task's sector is not the depot's builder, and a Redeploy claim is not a scout.
    private static void G4LaterStepsRunOverNoneAndMass()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 340f, 5000f));
        s.Own.Add(ExecutorFixtures.Corps(2, 350f, 3000f));
        s.Own.Add(ExecutorFixtures.Corps(3, 360f, 2500f));
        s.Own.Add(ExecutorFixtures.Corps(4, 20f, 9000f));   // a guard of the capital's floor
        s.Own.Add(ExecutorFixtures.Corps(5, 365f, 6000f));  // the sector's surplus: it holds the 2000 target without the builder
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = new TheaterPlan { Alliance = s.Alliance, DaySerial = s.DaySerial, MinGarrisonStrength = 2000f };
        plan.DepotTasks.Add(new DepotTask { X = 300f, Z = 0f, SectorId = sectors.SectorAt(350f, 0f), Priority = 1f });
        var claims = ClaimTable.Fresh(s);
        claims[1].Claimant = Claimant.Committed;
        claims[2].Claimant = Claimant.DepartmentMove;
        claims[3].Claimant = Claimant.Capital;
        plan.Claims.AddRange(claims);
        plan.SectorHeld = new float[sectors.Count];
        var a = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
        AssertEqual(FormationRole.Hold, At(a, 2).Role, "the committed formation, the smallest in the sector, waits");
        AssertEqual(ForceAllocator.ReasonCommittedWait, At(a, 2).Reason, "as committed-wait");
        AssertEqual(FormationRole.Redeploy, At(a, 3).Role, "the department move marches");
        AssertEqual(FormationRole.DepotBuilder, At(a, 1).Role, "so the task's builder is the one formation nobody claimed");
        AssertEqual(ForceAllocator.ReasonDepotTask, At(a, 1).Reason, "a new pick");
    }

    private static void NoAllocatorFallbackWithoutAClaimTable()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 350f, 5000f));
        var sectors = ExecutorFixtures.Sectors(s);
        var handBuilt = new TheaterPlan { Alliance = s.Alliance, DaySerial = s.DaySerial };
        AssertThrows(() => ForceAllocator.Allocate(handBuilt, s, sectors, null, null, null), "a plan with no table is a fault, not a plan to guess a table for");
        var another = new TheaterPlan { Alliance = s.Alliance, DaySerial = s.DaySerial };
        another.Claims.Add(new Claim { InstanceId = 99, SectorId = -1 });
        AssertThrows(() => ForceAllocator.Allocate(another, s, sectors, null, null, null), "nor is a table for another roster");
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ForceAllocator.cs");
        AssertFalse(src.Contains("ClaimTable.Build(") || src.Contains("ReserveRules.Capital(") || src.Contains("ReserveRules.Hold("), "the allocator builds no table of its own");
        AssertTrue(src.Contains("RequireClaims(plan, s);"), "it refuses a plan without one");
    }

    private static string ReadRepoFile(string relative)
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "build.sh"))) dir = System.IO.Path.GetDirectoryName(dir);
        return System.IO.File.ReadAllText(System.IO.Path.Combine(dir, relative));
    }

    // The pinned builder is claimed first (row 1) and overwritten by the immobile row (2): a busy one is Hold busy, as the allocator always did.
    private static void ABusyPinnedBuilderIsHoldBusy()
    {
        var s = ExecutorFixtures.Wide();
        var busy = ExecutorFixtures.Corps(1, 350f, 5000f);
        busy.Flags |= FormationFlags.InBattle;
        s.Own.Add(busy);
        var sectors = ExecutorFixtures.Sectors(s);
        var pin = new[] { new HeldBuilder { InstanceId = 1, Wished = true, SectorId = sectors.SectorAt(350f, 0f), TargetX = 330f, TargetZ = 0f } };
        var a = TestClaims.Allocate(new TheaterPlan { Alliance = s.Alliance, DaySerial = s.DaySerial }, s, sectors, null, null, null, null, pin);
        AssertEqual(FormationRole.Hold, At(a, 1).Role, "a busy pinned builder holds where it fights");
        AssertEqual(ForceAllocator.ReasonBusy, At(a, 1).Reason, "busy");
    }

    private static void AStrayCommittedClaimHoldsInPlace()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 350f, 5000f));
        s.Own.Add(ExecutorFixtures.Corps(2, 360f, 5000f));
        s.Own.Add(ExecutorFixtures.Corps(3, 370f, 5000f));
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = new TheaterPlan { Alliance = s.Alliance, DaySerial = s.DaySerial, MinGarrisonStrength = 2000f };
        var claims = ClaimTable.Fresh(s);
        claims[0].Claimant = Claimant.Committed;
        plan.Claims.AddRange(claims);
        plan.SectorHeld = new float[sectors.Count];
        var a = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
        AssertEqual(FormationRole.Hold, At(a, 1).Role, "a Committed claim that reaches the allocator holds in place");
        AssertEqual(ForceAllocator.ReasonCommittedWait, At(a, 1).Reason, "as committed-wait");
        AssertEqual(-1, At(a, 1).SectorId, "counted in no sector's role");
        AssertFalse(At(a, 1).Withheld, "and not withheld");
        AssertFalse(a.Surpluses.Contains(1), "it is no surplus (a push that ended is no formation to release)");
        AssertTrue(a.Surpluses.Contains(2) || a.Surpluses.Contains(3), "control: an unclaimed formation of a quiet sector beyond its garrison is");
    }

    // ---- the Redeploy role (TC-RED-*) ----

    private static Allocation RedeployAllocation(CampaignSnapshot s, out TheaterPlan plan)
    {
        plan = ExecutorFixtures.HandPlan(s, new[] { 3 });
        var sectors = ExecutorFixtures.Sectors(s);
        var a = new Allocation { Alliance = s.Alliance };
        a.Assignments.Add(new Assignment { InstanceId = 1, Role = FormationRole.CapitalGuard, SectorId = 0, TargetX = 0f, TargetZ = 0f, Reason = "capital-floor", Withheld = true });
        a.Assignments.Add(new Assignment { InstanceId = 2, Role = FormationRole.Redeploy, SectorId = 1, TargetX = 220f, TargetZ = 0f, Reason = ForceAllocator.ReasonCoveringForce, Withheld = true });
        a.Assignments.Add(new Assignment { InstanceId = 3, Role = FormationRole.MainEffort, SectorId = plan.MainSectorId, TargetX = 100f, TargetZ = 0f, Reason = "mass" });
        a.Assignments.Add(new Assignment { InstanceId = 4, Role = FormationRole.Garrison, SectorId = 1, TargetX = 190f, TargetZ = 0f, Reason = "garrison-post", Withheld = true });
        a.Assignments.Add(new Assignment { InstanceId = 5, Role = FormationRole.Redeploy, SectorId = 2, TargetX = 330f, TargetZ = 0f, Reason = ForceAllocator.ReasonDepartmentReinforcement });
        a.Assignments.Add(new Assignment { InstanceId = 6, Role = FormationRole.Recover, SectorId = 2, TargetX = 300f, TargetZ = 0f, Reason = "recover", Withheld = true });
        a.Assignments.Add(new Assignment { InstanceId = 7, Role = FormationRole.DepotBuilder, SectorId = 2, TargetX = 310f, TargetZ = 0f, Reason = "depot-task", Withheld = true });
        a.Assignments.Add(new Assignment { InstanceId = 8, Role = FormationRole.Support, SectorId = 1, TargetX = 100f, TargetZ = 0f, Reason = "reinforcement" });
        return a;
    }

    private static CampaignSnapshot RedeployWorld()
    {
        var s = ExecutorFixtures.Wide();
        for (int id = 1; id <= 8; id++) s.Own.Add(ExecutorFixtures.Corps(id, 560f, 4000f));   // all far from their targets
        return s;
    }

    // Home defence first, then a covering member, the main effort, the garrisons, a department's reinforcement, the recoveries, the depot builders
    // and the Support, under the daily cap (MoveTracker.MaxMovesPerDay) of the planner's roles.
    private static void RedeployMovePriorityOrder()
    {
        var s = RedeployWorld();
        var a = RedeployAllocation(s, out var plan);
        var host = new FakeMoverHost();
        var tally = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo()), host, new MoveTracker(), ExecutorFixtures.State());
        AssertEqual(MoveTracker.MaxMovesPerDay, host.Moves.Count, "the cap is spent");
        AssertEqual("1,2,3,4,5,6", string.Join(",", host.Moves.Select(m => m.id)), "capital guard, covering member, main effort, garrison, department reinforcement, recover");
        AssertTrue(CampaignMover.MovesRole(FormationRole.Redeploy), "the recon executor leaves a Redeploy formation to the mover");
        AssertFalse(CampaignMover.MovesRole(FormationRole.Hold), "a Hold is never sent, a committed-wait one included");
        AssertEqual(6, tally.Issued, "six issued");
    }

    private static void RedeployNeedsNoTempoRelease()
    {
        var s = RedeployWorld();
        var a = RedeployAllocation(s, out var plan);
        var host = new FakeMoverHost();
        CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.ClosedTempo()), host, new MoveTracker(), ExecutorFixtures.State());
        AssertTrue(host.Moves.Exists(m => m.id == 2), "a covering member marches with no release");
        AssertTrue(host.Moves.Exists(m => m.id == 5), "so does a department's reinforcement");
    }

    private static void RedeployReasonsWithholdTheRightPools()
    {
        var s = RedeployWorld();
        var a = RedeployAllocation(s, out var plan);
        var sectors = ExecutorFixtures.Sectors(s);
        plan.DaySerial = ExecutorFixtures.Day;
        PlanFreshness.NoteToday(ExecutorFixtures.Day);
        CampaignPlanStore.Publish(0, plan, a, sectors);
        CampaignPlanStore.PublishExecutorContext(0, new ExecutorContext { Released = true, ExecutorsAllowed = true });
        try
        {
            AssertTrue(CampaignWithholdView.WithheldFromOffense(0, 5), "a department's reinforcement is withheld from vanilla's offence");
            AssertFalse(CampaignWithholdView.WithheldFromDefense(0, 5, 560f, 0f), "but vanilla's defence may use it on its way");
            AssertTrue(CampaignWithholdView.WithheldFromOffense(0, 2), "a covering member is withheld from the offence");
            AssertTrue(CampaignWithholdView.WithheldFromDefense(0, 2, 560f, 0f), "and from the defence (its exception is F6b-2's)");
        }
        finally { CampaignScope.ResetAll(); }
    }

    private static void RedeployIsTakenOffVanillasOffensiveList()
    {
        var s = RedeployWorld();
        var inList = s.Own[4];
        inList.Flags |= FormationFlags.InOffensiveOps;
        s.Own[4] = inList;
        var covering = s.Own[1];
        covering.Flags |= FormationFlags.InOffensiveOps;
        s.Own[1] = covering;
        var a = RedeployAllocation(s, out var plan);
        var host = new FakeMoverHost();
        CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo()), host, new MoveTracker(), ExecutorFixtures.State());
        AssertTrue(host.Listed.Contains((5, false)), "a department's reinforcement leaves unitsinoffensiveoperations");
        AssertTrue(host.Listed.Contains((2, false)), "so does a covering member");
    }

    // A Redeploy formation the tracker gave up on at its post is Hold gave-up, like any role that marches (ForceAllocator.Marches).
    private static void RedeployGiveUpFallsBackToHold()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 560f, 4000f));
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = new TheaterPlan { Alliance = s.Alliance, DaySerial = s.DaySerial };
        var claims = ClaimTable.Fresh(s);
        claims[0].Claimant = Claimant.DepartmentMove;
        claims[0].TargetX = 330f;
        plan.Claims.AddRange(claims);
        plan.SectorHeld = new float[sectors.Count];
        var gaveUp = new[] { new GaveUpMove { InstanceId = 1, TargetX = 330f, TargetZ = 0f, ObjectiveId = -1 } };
        var a = ForceAllocator.Allocate(plan, s, sectors, null, null, null, gaveUp);
        AssertEqual(FormationRole.Hold, At(a, 1).Role, "it holds where it stands");
        AssertEqual(ForceAllocator.ReasonGaveUp, At(a, 1).Reason, "with the give-up's reason");
        var sent = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
        AssertEqual(FormationRole.Redeploy, At(sent, 1).Role, "control: without the give-up it marches");
    }

    private static void RedeployIsRankedWithoutThrowing()
    {
        foreach (FormationRole role in Enum.GetValues(typeof(FormationRole)))
            AssertTrue(RearmPriority.RankOf(role, false) >= 0, "the re-arming order ranks " + role);
        AssertEqual(RearmPriority.RankOf(FormationRole.Garrison, false), RearmPriority.RankOf(FormationRole.Redeploy, false), "a Redeploy formation ranks as a marching garrison");
    }

    private static void RedeployReadersHandleIt()
    {
        AssertEqual(ArmyPerkRole.CapitalDefense, PerkSelectionScorer.RoleOfFormation(FormationRole.Redeploy, 0), "the perk scorer gives it the capital-defence perks");
        var s = RedeployWorld();
        var a = RedeployAllocation(s, out var plan);
        var own = s.Own.ToList();
        AssertEqual(0f, ReinforcementRouting.ShortfallStrength(plan, a, own, 5), "a Redeploy formation carries no shortfall");
        var writes = new List<TheaterWrite>();
        s.TheaterHalfExtentX = 40f;
        s.TheaterHalfExtentZ = 40f;
        SteeringRules.PlanTheaterWrites(s, a, ExecutorFixtures.Sectors(s), new TheaterWriteLimiter(), writes);
        AssertFalse(writes.Exists(w => w.InstanceId == 2 || w.InstanceId == 5), "steering writes no theater box for a formation that marches elsewhere");
    }

    private static void PlanGroupsSkipsRedeploy()
    {
        var a = new Allocation();
        a.Assignments.Add(new Assignment { InstanceId = 1, Role = FormationRole.Hold, SectorId = 1 });
        a.Assignments.Add(new Assignment { InstanceId = 2, Role = FormationRole.Hold, SectorId = 1 });
        a.Assignments.Add(new Assignment { InstanceId = 3, Role = FormationRole.Redeploy, SectorId = 1, Reason = ForceAllocator.ReasonCoveringForce });
        var groups = ArmyGroupDoctrine.PlanGroups(a);
        AssertEqual(1, groups.Count, "the two holds are one group");
        AssertEqual("1,2", string.Join(",", groups[0].InstanceIds), "without the formation that marches elsewhere");
        a.Assignments.RemoveAt(1);
        AssertEqual(0, ArmyGroupDoctrine.PlanGroups(a).Count, "a hold and a redeploying formation are no group");
    }

    private static void TheSummaryCountsRedeployAndCoveringMembers()
    {
        var s = RedeployWorld();
        var a = RedeployAllocation(s, out var plan);
        a.Assignments.Add(new Assignment { InstanceId = 9, Role = FormationRole.Hold, SectorId = 1, Reason = ForceAllocator.ReasonCoveringForce, Withheld = true });
        string row = ForceAllocator.Summary(a, plan, PlayerCommandClass.BelowMapLevel, 1f, 0);
        AssertContains(row, " busy=0 redeploy=2 covering=2 player=BelowMapLevel", "redeploy= counts both reasons, covering= the members of either role; a member at its post is also in hold=");
        AssertContains(row, " hold=2 ", "the covering member at its post counts as a hold, beside the marching garrison the row has always counted there");
    }

    // ---- D8's HoldFacing reads the table (TC-CLAIM-ROWS) ----

    // The secession world of SecessionGarrisonTests, with the capital where a test puts it: eleven areas (x 0..1000, relocation range 150), the
    // town (Louisville, objective 15) at x 300 held by the flag alone with a garrison of 4000, a Union army of `enemy` at x 320, and an
    // own-held objective in the East so the East is a sector of its own. The town's sector is the West. Hold ratio 0.6.
    private static CampaignSnapshot PostWorld(float capitalX, float enemy, params FormationRecord[] corps)
    {
        var s = ExecutorFixtures.Line(0, 11);
        s.MinStrengthForMovement = 5000f;
        s.MinMoraleForMovement = 0.65f;
        s.CapitalX = capitalX;
        for (int i = 1; i <= 9; i++) s.Supply.Add(ExecutorFixtures.Iip(600 + i, 100f * i, 0));
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 15, StateId = 14, X = 300f, Z = 0f, HolderAlliance = 0, Available = false, Importance01 = 0.3f });
        s.Secessions.Add(new SecessionRecord
        {
            ObjectiveId = 15, StateId = 14, StateAlliance = 0, Live = SecessionRules.IsLive(0, 0.6f), HasTown = true, TownX = 300f, TownZ = 0f,
            OwnTroops = true, GarrisonStrength = 4000f
        });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40, StateId = 5, X = 900f, Z = 0f, HolderAlliance = 0, Available = false, Importance01 = 0.1f });
        s.Beliefs.Add(ExecutorFixtures.Belief(320f, 0f, enemy));
        foreach (var c in corps) s.Own.Add(c);
        return s;
    }

    private static FormationRecord Body(int id, float x, float strength, int readiness = 3, FormationFlags extra = 0)
    {
        var f = ExecutorFixtures.Corps(id, x, strength);
        f.SightRadius = 50f;
        f.Morale = 0.9f;
        f.ReadinessStep = readiness;
        f.Flags |= extra;
        return f;
    }

    private static float PostNeed(CampaignSnapshot s)
    {
        var plan = TheaterPlanner.Plan(s, ExecutorFixtures.Sectors(s), null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
        AssertEqual(1, plan.Garrisons.Count, "the flag-held town has its post");
        return plan.Garrisons[0].Need;
    }

    // An army of 45000 near the town is a hold of 27000 on its sector. One West corps of 9000 and the East guard at the capital (x 900): the
    // sector's reserve is the corps, the post is asked for what the corps does not hold: 27000 - 9000 - 4000.
    private static void HoldFacingReadsTheChosenBody()
    {
        var s = PostWorld(900f, 45000f, Body(900, 900f, 12000f), Body(1, 240f, 9000f));
        AssertNear(14000f, PostNeed(s), 1f, "27000 - 9000 held by the chosen reserve body - 4000 garrison");
    }

    // The unready (readiness step 1) and the busy hold the sector anyway; they are not the reserve it chose, and HoldFacing does not credit them.
    private static void HoldFacingIgnoresCountedAnywayBodies()
    {
        var s = PostWorld(900f, 45000f, Body(900, 900f, 12000f), Body(1, 240f, 9000f), Body(2, 250f, 9000f, 1), Body(3, 260f, 9000f, 3, FormationFlags.InBattle));
        var plan = TheaterPlanner.Plan(s, ExecutorFixtures.Sectors(s), null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
        int west = ExecutorFixtures.Sectors(s).SectorAt(300f, 0f);
        AssertEqual(Claimant.None, plan.Claims[s.IndexOfOwn(2)].Claimant, "the unready formation is unclaimed");
        AssertEqual(west, plan.Claims[s.IndexOfOwn(2)].SectorId, "but the hold counts it anyway");
        AssertEqual(west, plan.Claims[s.IndexOfOwn(3)].SectorId, "so does the busy one");
        AssertEqual(Claimant.Hold, plan.Claims[s.IndexOfOwn(1)].Claimant, "the ready corps is the reserve: the two that hold anyway meet 18000 of 27000, and 9000 more is reserved");
        AssertNear(14000f, plan.Garrisons[0].Need, 1f, "the post is asked 27000 - 9000 (the chosen body alone) - 4000: the 18000 that merely stands there is not credited");
    }

    // Integrator decision on phase 1 gap 18 (2026-10-18): the capital guard standing in the town's sector holds it, and is credited through the
    // table. The capital is at x 0 in the town's sector (the West): 27000 - (the guard 9000 + the corps 9000) - 4000.
    private static void HoldFacingCreditsACapitalGuardStandingInTheSector()
    {
        var s = PostWorld(0f, 45000f, Body(900, 0f, 9000f), Body(1, 240f, 9000f));
        AssertNear(5000f, PostNeed(s), 1f, "27000 - 18000 - 4000: the guard really holds the sector");
    }

    // The same rule's other side (the same decision, "the rule F9's StandingHold uses"): a guard that marches to a capital in another sector holds
    // nothing where it stands, so it is not credited. The capital is in the East (x 900); the nearest formation to it is corps 1 at x 240, the
    // guard, and the corps at x 100 is the West's reserve: 27000 - 9000 - 4000.
    private static void HoldFacingCreditsNoGuardMarchingToACapitalElsewhere()
    {
        var s = PostWorld(900f, 45000f, Body(1, 240f, 9000f), Body(2, 100f, 9000f));
        var plan = TheaterPlanner.Plan(s, ExecutorFixtures.Sectors(s), null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
        AssertEqual(Claimant.Capital, plan.Claims[s.IndexOfOwn(1)].Claimant, "corps 1, nearest the capital, is its guard");
        AssertEqual(-1, plan.Claims[s.IndexOfOwn(1)].SectorId, "marching to the capital, it is counted in no sector");
        AssertNear(14000f, plan.Garrisons[0].Need, 1f, "27000 - 9000 (corps 2 alone) - 4000 garrison");
    }

    private static List<FormationRecord> Roster()
    {
        var r = new List<FormationRecord>();
        FormationRecord F(int id, string key, float x, float z, float strength, int readiness = 3)
        {
            var f = EnemyPictureTests.Corps(id, x, z, strength);
            f.UnitKey = key;
            f.Name = key;
            f.ReadinessStep = readiness;
            return f;
        }
        r.Add(F(1, "Army of the Potomac:28", 1245.1f, -680.8f, 19400f));
        r.Add(F(2, "Army of Virginia:1735", 1247.4f, -802.6f, 4000f, 1));
        r.Add(F(3, "Army of Northern Virginia:199", 1285f, -874.6f, 9000f));
        r.Add(F(4, "Army of the Peninsula:1", 1330f, -1060f, 7000f));
        var norfolk = F(5, "Department of Norfolk:2", 1340f, -1130f, 3000f);
        norfolk.Flags = FormationFlags.InBattle | FormationFlags.FightingForce;
        r.Add(norfolk);
        var starved = F(6, "Army of North Carolina:3", 1263.6f, -1010.9f, 6000f);
        starved.Supply01 = 0.1f;
        r.Add(starved);
        r.Add(F(7, "Reserve Corps:4", 1260f, -990f, 5000f));
        var player = F(8, "Player Group:5", 1245f, -690f, 5000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        r.Add(player);
        var depot = F(9, "Supply Train:6", 1250f, -820f, 1500f);
        depot.Flags = 0;   // no FightingForce: a non-combat unit
        r.Add(depot);
        r.Add(F(10, "Cavalry Division:7", 1300f, -700f, 3000f));
        return r;
    }
}
