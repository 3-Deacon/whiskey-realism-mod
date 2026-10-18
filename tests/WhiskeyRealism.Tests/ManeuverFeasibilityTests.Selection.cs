using System;
using System.Collections.Generic;
using System.IO;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;
using static BattleMovementHonestyTests;
using static ManeuverFeasibilityFixtures;

// BC14 task 9: the sliced evaluation job, the ledger's whole publication and the selection term (spec § 13.1 S4, S8).
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] SelectionTests() => new (string, Action)[]
    {
        ("bc14 FEAS-SLICE a sliced evaluation stops at its deadline, resumes and publishes the forced result whole", FeasSlice),
        ("bc14 SELECT-FILTER an impossible winner by score is skipped and the next taken", SelectFilter),
        ("bc14 SELECT-JITTER the filter follows the jitter draw: every other candidate scores as without it", SelectJitter),
        ("bc14 SELECT-NEVER-EMPTY when every candidate is impossible the filter is lifted", SelectNeverEmpty),
        ("bc14 SELECT-SIDE the winner's evaluated side is published and the zone assignment projects it", SelectSide),
        ("bc14 FEAS-REGRESS a ledger that knows nothing leaves every selection as it was", FeasRegress),
        ("bc14 FEAS-DEF a Defender is never offered an offensive candidate", FeasDef),
        ("bc14 FEAS-TIME the evaluation's path searches and sampled legs stay inside the counted bounds", FeasTime),
    };

    // A lookup that answers from a table (the ledger's role, without an evaluation).
    sealed class TableLookup : IFeasibilityLookup
    {
        public readonly Dictionary<BattlePlanId, CandidateResult> Results = new Dictionary<BattlePlanId, CandidateResult>();
        public FeasibilityVerdict Default = FeasibilityVerdict.Unknown;
        public float DefaultPenalty;
        public bool AnswerAll;

        public BattlePlanId SelectedPlan => BattlePlanId.Unknown;

        public string BrokenAssumption => null;
        public bool TryFocalPoint(out FocalPoint focal) { focal = default(FocalPoint); return false; }
        public bool WorksHold(out string reason) { reason = null; return false; }
        public bool AssaultsWorks(string childId) => false;
        public bool FacesWorks(string childId) => false;
        public bool PinsWorks(string childId) => false;
        public int WorksHoldHash => 0;
        public float Willingness => -1f;
        public IWorksQuery HeldWorks => null;
        public bool TryStation(out StationPoint station, float[] ownShiftByChild, out int childCount) { station = default(StationPoint); childCount = 0; return false; }
        public string StationChildId(int column) => null;

        // BC14O: no objective table.
        public WhiskeyRealism.Tactical.Battle.ObjectivePressure PressureValue;
        public WhiskeyRealism.Tactical.Battle.ObjectivePressure Pressure => PressureValue;
        /// <summary>BC14O: the objective table this lookup publishes (null: none).</summary>
        public WhiskeyRealism.Tactical.Battle.ObjectiveEstimate[] Rows;
        public int ObjectiveCount => Rows == null ? 0 : Rows.Length;
        public bool HoldsPlayerOwned(string childId) => false;
        public bool TryObjective(int index, out WhiskeyRealism.Tactical.Battle.ObjectiveEstimate estimate)
        {
            bool has = Rows != null && index >= 0 && index < Rows.Length;
            estimate = has ? Rows[index] : default(WhiskeyRealism.Tactical.Battle.ObjectiveEstimate);
            return has;
        }
        public int ThreatCount => 0;
        public int LookRequests(Span<float> xs, Span<float> zs) => 0;
        public double EvaluatedHours => 0.0;
        public bool HasEvaluation => false;
        public bool TryPredictedArrival(string childId, out double arrivalHours, out double marchHours) { arrivalHours = marchHours = 0.0; return false; }
        public bool TryThreat(int index, out CrossingThreat threat, float[] ownShiftByChild, out int childCount) { threat = default(CrossingThreat); childCount = 0; return false; }
        public string ThreatChildId(int column) => null;

        public void CaptureSelection(BattlePlanId plan, bool mirrored) { }

        public bool TryPlannedRelease(string childId, out double releaseHours, out double timeBudgetHours, out float viaX, out float viaZ,
            out float attackViaX, out float attackViaZ)
        {
            releaseHours = timeBudgetHours = 0.0;
            viaX = viaZ = attackViaX = attackViaZ = float.NaN;
            return false;
        }

        public bool TryViaRegions(string childId, out int viaFarRegion, out int attackViaFarRegion)
        {
            viaFarRegion = attackViaFarRegion = -1;
            return false;
        }

        public bool TryGet(BattlePlanId plan, out CandidateResult result)
        {
            if (Results.TryGetValue(plan, out result)) return true;
            if (!AnswerAll) return false;
            result = new CandidateResult { Plan = plan, Verdict = Default, Penalty = DefaultPenalty };
            return true;
        }
    }

    static PlaybookContext LeeContext(IFeasibilityLookup lookup, BattleStance stance = BattleStance.Meeting, float odds = 1.1f, int seed = 7)
        => new PlaybookContext(new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f), TerrainKind.Wooded, odds, 0f, 0, seed,
            allianceId: 0, cavalryFraction: 0f, stance: stance, feasibility: lookup);

    static void SelectFilter()
    {
        TacticalPlaybookCatalog catalog = SeedCatalog.AllHistoricalAndGeneric();
        var report = new SelectionReport();
        TacticalPlaybook plain = catalog.Select(LeeContext(null), report);
        BattlePlanId second = report[1].Plan;
        AssertEqual(BattlePlanId.DoubleEnvelopment, plain.Id, "control: Lee's pick without a ledger");
        AssertEqual(0, report.FeasibilityFiltered, "control: nothing filtered");

        var lookup = new TableLookup();
        lookup.Results[BattlePlanId.DoubleEnvelopment] = new CandidateResult { Plan = BattlePlanId.DoubleEnvelopment, Verdict = FeasibilityVerdict.Impossible, Penalty = 1f, Reason = "wing-unfilled" };
        var filtered = new SelectionReport();
        TacticalPlaybook next = catalog.Select(LeeContext(lookup), filtered);
        AssertEqual(second, next.Id, "the next best is taken");
        AssertEqual(1, filtered.FeasibilityFiltered, "feasibilityFiltered counted");
        bool skippedRow = false;
        for (int i = 0; i < filtered.Count; i++) if (filtered[i].Plan == BattlePlanId.DoubleEnvelopment && filtered[i].Skipped) skippedRow = true;
        AssertTrue(skippedRow, "the report names the skipped candidate");
        AssertFalse(filtered.AllImpossible, "the filter was not lifted");
    }

    static void SelectJitter()
    {
        TacticalPlaybookCatalog catalog = SeedCatalog.AllHistoricalAndGeneric();
        var plain = new SelectionReport();
        catalog.Select(LeeContext(null), plain);
        var lookup = new TableLookup();
        lookup.Results[BattlePlanId.PreparedDefense] = new CandidateResult { Plan = BattlePlanId.PreparedDefense, Verdict = FeasibilityVerdict.Impossible, Penalty = 1f };
        lookup.Results[BattlePlanId.MobileFlankMarch] = new CandidateResult { Plan = BattlePlanId.MobileFlankMarch, Verdict = FeasibilityVerdict.Impossible, Penalty = 1f };
        // Meeting stance reaches every playbook, so two filtered candidates sit before later ones in the catalog's order.
        var filtered = new SelectionReport();
        catalog.Select(LeeContext(lookup), filtered);
        AssertEqual(2, filtered.FeasibilityFiltered, "two skipped");
        int compared = 0;
        for (int i = 0; i < plain.Count; i++)
        {
            BattlePlanId plan = plain[i].Plan;
            if (plan == BattlePlanId.PreparedDefense || plan == BattlePlanId.MobileFlankMarch) continue;
            bool found = false;
            for (int k = 0; k < filtered.Count; k++)
            {
                if (filtered[k].Plan != plan) continue;
                found = true;
                AssertNear(plain[i].BaseScore, filtered[k].BaseScore, 1e-6f, plan + " scores as before: its jitter draw did not move");
            }
            AssertTrue(found, plan + " was scored");
            compared++;
        }
        AssertTrue(compared >= 12, "the whole field was compared (" + compared + ")");
    }

    static void SelectNeverEmpty()
    {
        TacticalPlaybookCatalog catalog = SeedCatalog.AllHistoricalAndGeneric();
        TacticalPlaybook plain = catalog.Select(LeeContext(null));
        var all = new TableLookup { AnswerAll = true, Default = FeasibilityVerdict.Impossible, DefaultPenalty = 1f };
        var report = new SelectionReport();
        TacticalPlaybook lifted = catalog.Select(LeeContext(all), report);
        AssertTrue(lifted != null, "the catalog is never emptied");
        AssertTrue(report.AllImpossible, "allImpossible recorded");
        AssertEqual(0, report.FeasibilityFiltered, "nothing skipped when the filter is lifted");
        AssertEqual(plain.Id, lifted.Id, "every candidate carries the same term, so the shipped winner stands");
        AssertNear(-ManeuverFeasibility.FeasibilityWeight, report[0].Term, 1e-6f, "each candidate is scored with its penalty: -0.4");
        // A Defender keeps its two neutral plays at least.
        var defender = new SelectionReport();
        AssertTrue(catalog.Select(LeeContext(all, BattleStance.Defender), defender) != null, "a Defender is not left without a play either");
    }

    static void SelectSide()
    {
        // The authored side's right wing lies across an unbridged river; the mirrored side stays on the bank (the SIDE-RANK field).
        var f = new FeasField(VerticalBand(225f, 245f));
        f.AddCorps(-200f, 0f, 12000f);
        f.AddCorps(0f, 0f, 11000f);
        f.AddCorps(200f, 0f, 12000f);
        f.AddEnemyLine(25000f);
        FeasibilityLedger ledger = f.Evaluate(BattlePlanId.ManeuverAndFix);
        CandidateResult scored = Result(ledger, BattlePlanId.ManeuverAndFix);
        AssertTrue(scored.Mirrored, "the evaluation prefers the mirrored side");

        var catalog = new TacticalPlaybookCatalog();
        catalog.Register(new ManeuverAndFixPlaybook());
        var army = new ArmyOrchestrator(0, catalog, new PersonalityVector(0.5f, 0f, 0.5f, 0.5f, 0f));
        army.SetFeasibility(ledger);
        army.PickInitialPlan(new ArmyEvidence(1.4f, TerrainKind.Open, 0));
        AssertEqual(BattlePlanId.ManeuverAndFix, army.CurrentPlan.PlanId, "the only playbook is picked");
        AssertTrue(army.CurrentPlan.Mirrored, "and its evaluated side is published with the plan");
        AssertTrue(army.CurrentPlan.WithPhase(BattlePhase.MainEffort).Mirrored, "a phase change keeps it");
        AssertTrue(army.CurrentPlan.WithMainEffortSector(1).Mirrored, "so does a sector change");
        AssertTrue(army.CurrentPlan.WithReserveCommitTriggerOdds(1.1f).Mirrored, "and a reserve trigger change");
        AssertFalse(army.CurrentPlan.WithMirrored(false).Mirrored, "WithMirrored sets it");

        // Without a ledger the plan is authored.
        var plain = new ArmyOrchestrator(0, catalog, new PersonalityVector(0.5f, 0f, 0.5f, 0.5f, 0f));
        plain.PickInitialPlan(new ArmyEvidence(1.4f, TerrainKind.Open, 0));
        AssertFalse(plain.CurrentPlan.Mirrored, "no ledger: not mirrored");

        // The executor projects what the evaluation timed: the wing's child is the one the mirrored layout gives the wing rect.
        FeasibilityJob job = ledger.Job;
        PlayTemplate template = PlayTemplateCatalog.Lookup(BattlePlanId.ManeuverAndFix);
        var corps = new List<CorpsPosition>();
        for (int c = 0; c < job.ChildCount; c++) corps.Add(new CorpsPosition(job.Children[c].Id, job.Children[c].X, job.Children[c].Z));
        IReadOnlyList<WorldPlayRect> rects = PlayProjection.Project(template, job.ArmyX, job.ArmyZ, job.EnemyX, job.EnemyZ, job.Frontage, default(TerrainLine),
            army.CurrentPlan.Mirrored);
        IReadOnlyList<CorpsAssignment> zones = PlayCorpsAssignment.Assign(rects, corps, Array.Empty<ObjectivePosition>());
        string wingChild = null;
        for (int i = 0; i < zones.Count; i++) if (zones[i].Envelops) wingChild = zones[i].ChildId;
        CandidateResult r = Result(ledger, BattlePlanId.ManeuverAndFix);
        string timed = ledger.Bodies(in r)[0].ChildId;
        AssertEqual(timed, wingChild, "the zone assignment gives the wing rect to the child the evaluation timed");

        // The executor passes the plan's side to the projection (source pin: the runtime is not in the harness).
        string executor = File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical/Orchestrator/Plays/PlayExecutorRuntime.cs"));
        AssertContains(executor, "PlayProjection.Project(template, armyX, armyZ, targetX, targetZ, frontage, in crest, mirrored)", "the executor projects the plan's side toward its target");
    }

    static void FeasRegress()
    {
        FeasField f = ThreeCorps();
        FeasibilityInput input = f.Build(AllPlans);
        input.Picture = new TerrainPicture(f.Grid, FeasField.Size, FeasField.Size);          // the map is unknown: every candidate Unknown
        var ledger = new FeasibilityLedger();
        ManeuverFeasibility.Evaluate(in input, ledger);
        for (int i = 0; i < AllPlans.Length; i++)
        {
            CandidateResult r = Result(ledger, AllPlans[i]);
            AssertEqual(FeasibilityVerdict.Unknown, r.Verdict, AllPlans[i] + " is Unknown");
            AssertNear(0f, r.Penalty, 1e-9f, AllPlans[i] + " carries no penalty");
        }
        TacticalPlaybookCatalog catalog = SeedCatalog.AllHistoricalAndGeneric();
        var rng = new Random(14);
        int compared = 0;
        BattleStance[] stances = { BattleStance.Meeting, BattleStance.Attacker, BattleStance.Defender };
        for (int n = 0; n < 200; n++)
        {
            var vector = new PersonalityVector((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, 0.5f, 0f);
            var terrain = (TerrainKind)rng.Next(4);
            float odds = 0.4f + (float)rng.NextDouble() * 2.2f;
            BattleStance stance = stances[rng.Next(3)];
            int seed = rng.Next(1, 100);
            bool pressure = rng.Next(2) == 0, delaying = rng.Next(4) == 0, defensive = rng.Next(4) == 0, crest = rng.Next(3) == 0;
            float cavalry = rng.Next(2) == 0 ? 0f : 0.3f;
            PlaybookContext Make(IFeasibilityLookup l) => new PlaybookContext(vector, terrain, odds, 0f, 0, seed, pressure, 0, cavalry, 1f, 1f, delaying, defensive,
                stance, crest, l);
            TacticalPlaybook a = catalog.Select(Make(null)), b = catalog.Select(Make(ledger));
            AssertEqual(a == null ? BattlePlanId.Unknown : a.Id, b == null ? BattlePlanId.Unknown : b.Id, "case " + n + ": the winner is the shipped one");
            compared++;
        }
        AssertEqual(200, compared, "all compared");
    }

    static void FeasDef()
    {
        TacticalPlaybookCatalog catalog = SeedCatalog.AllHistoricalAndGeneric();
        var plans = new BattlePlanId[24];
        int n = catalog.EligiblePlans(LeeContext(null, BattleStance.Defender), plans);
        AssertTrue(n >= 2, "at least the two neutral plays reach scoring, got " + n);
        bool meeting = false, methodical = false;
        for (int i = 0; i < n; i++)
        {
            AssertTrue(TacticalPlaybookCatalog.StanceOf(plans[i]) != TacticalPlaybookCatalog.PlaybookStance.Offensive, plans[i] + " is not offensive");
            if (plans[i] == BattlePlanId.MeetingEngagement) meeting = true;
            if (plans[i] == BattlePlanId.GenericMethodical) methodical = true;
        }
        AssertTrue(meeting && methodical, "both neutral plays are there");
        int attacker = catalog.EligiblePlans(LeeContext(null, BattleStance.Attacker), plans);
        for (int i = 0; i < attacker; i++) AssertTrue(TacticalPlaybookCatalog.StanceOf(plans[i]) != TacticalPlaybookCatalog.PlaybookStance.Defensive, plans[i] + " is not defensive");
    }

    // A deterministic clock: every reading advances one tick.
    sealed class ScriptedClock
    {
        public long Now;
        public long Read() => ++Now;
    }

    static void FeasSlice()
    {
        FeasField f = ThreeCorps(VerticalBandWithFords(200f, 220f, (210f, 300f)));
        f.AddEnemy(210f, 340f, 400f, heading: 180f, fireRange: 100f);
        var forced = new FeasibilityLedger();
        FeasibilityInput input = f.Build(AllPlans);
        ManeuverFeasibility.Evaluate(in input, forced);
        AssertTrue(forced.HasEvaluation, "the forced evaluation publishes");
        AssertEqual(AllPlans.Length, forced.CandidateCount, "every candidate has a result");

        var sliced = new FeasibilityLedger();
        FeasibilityInput again = f.Build(AllPlans);
        var clock = new ScriptedClock();
        int calls = 0;
        bool published = false;
        while (!published && calls < 100)
        {
            // Each call may run until the clock has advanced twice past its start: the job evaluates at least one candidate, then checks.
            long deadline = clock.Now + 2;
            published = ManeuverFeasibility.EvaluateSlice(in again, sliced.Job, sliced, deadline, clock.Read);
            calls++;
            if (!published)
            {
                AssertFalse(sliced.HasEvaluation, "no reader sees a half-evaluated ledger (call " + calls + ")");
                AssertEqual(0, sliced.CandidateCount, "the front buffer is untouched until the last candidate is done");
                AssertTrue(sliced.Job.Running, "the job is resumable");
            }
        }
        AssertTrue(published, "the evaluation finished");
        AssertTrue(calls > 2, "it took several slices, got " + calls);
        AssertTrue(calls <= AllPlans.Length, "and each slice made progress");
        AssertEqual(forced.CandidateCount, sliced.CandidateCount, "same number of candidates");
        for (int i = 0; i < AllPlans.Length; i++)
        {
            CandidateResult a = Result(forced, AllPlans[i]), b = Result(sliced, AllPlans[i]);
            AssertEqual(a.Verdict, b.Verdict, AllPlans[i] + " verdict");
            AssertEqual(a.Mirrored, b.Mirrored, AllPlans[i] + " side");
            AssertNear(a.Penalty, b.Penalty, 1e-6f, AllPlans[i] + " penalty");
            AssertEqual(a.Reason, b.Reason, AllPlans[i] + " reason");
            AssertEqual(a.BodyCount, b.BodyCount, AllPlans[i] + " bodies");
        }

        // A second evaluation leaves the first published until it finishes.
        FeasibilityInput third = f.Build(AllPlans);
        clock.Now = 0;
        bool done = ManeuverFeasibility.EvaluateSlice(in third, sliced.Job, sliced, clock.Now + 1, clock.Read);
        AssertFalse(done, "a one-tick slice cannot finish eighteen candidates");
        AssertEqual(AllPlans.Length, sliced.CandidateCount, "the previous ledger is still the one readers see");
        AssertTrue(sliced.HasEvaluation, "still published");
    }

    // A vertical river with ten fords, eight corps, a Meeting side and every candidate.
    static void FeasTime()
    {
        var fords = new (float lateral, float depth)[10];
        for (int i = 0; i < fords.Length; i++) fords[i] = (0f, -900f + i * 200f);
        BattleTerrainGrid grid = HorizontalBandWithFordsLateral(150f, 170f, -1000f, 800f, fords.Length);
        var f = new FeasField(grid);
        for (int c = 0; c < 8; c++) f.AddCorps(-420f + c * 120f, 0f, 6000f, regiments: 2);
        f.AddEnemyLine(36000f);
        f.Stance = BattleStance.Meeting;
        var ledger = new FeasibilityLedger();
        FeasibilityInput input = f.Build(AllPlans);
        ManeuverFeasibility.Evaluate(in input, ledger);
        FeasibilityJob job = ledger.Job;
        int sides = 0;
        for (int i = 0; i < AllPlans.Length; i++) sides += job.SidesEvaluated[i];
        AssertEqual(21, sides, "18 candidates, three of them on both sides");
        AssertTrue(job.MaxSearchesPerSide <= 29, "at most 29 path searches for one side, got " + job.MaxSearchesPerSide);
        AssertTrue(job.Graph.Searches <= 609, "at most 609 path searches in all, got " + job.Graph.Searches);
        AssertTrue(job.Graph.LegsSampled <= 2309, "at most 2,309 sampled legs, got " + job.Graph.LegsSampled);
        Console.WriteLine("FEAS-TIME searches=" + job.Graph.Searches + " legs=" + job.Graph.LegsSampled + " maxPerSide=" + job.MaxSearchesPerSide + " cache=" + job.Graph.Cache.Count);
    }

    // A vertical band at lateral [from, to) with fords evenly along depth [depthFrom, depthTo].
    static BattleTerrainGrid HorizontalBandWithFordsLateral(float lateralFrom, float lateralTo, float depthFrom, float depthTo, int fords)
    {
        var points = new (float lateral, float depth)[fords];
        for (int i = 0; i < fords; i++) points[i] = ((lateralFrom + lateralTo) * 0.5f, depthFrom + (depthTo - depthFrom) * (i + 0.5f) / fords);
        return VerticalBandWithFords(lateralFrom, lateralTo, points);
    }
}
