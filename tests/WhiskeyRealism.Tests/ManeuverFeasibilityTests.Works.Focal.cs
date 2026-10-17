using System;
using System.Collections.Generic;
using System.Linq;
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
using static WorksFixtures;

// BC14W task 5: the FocalAssault playbook, its stance and anchor, the focal point and the works hold of the ledger (spec § 7A requirement-focal-assault,
// requirement-candidate-set and requirement-hypothetical-layout BC14W cases; § 13.2 FEAS-SCN-6, FEAS-SCN-8, FEAS-SCN-9, FOCAL-OFFER, FOCAL-WILLINGNESS,
// CANDIDATE-CENTRED-W, READER-STANCE, READER-FOCAL-ANCHOR, FEAS-TIME-W).
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksFocalTests() => new (string, Action)[]
    {
        ("bc14w READER-STANCE FocalAssault is an offensive playbook, so a Defender is never offered it, a focal point in its ledger or not", WReaderStance),
        ("bc14w READER-FOCAL-ANCHOR a play is projected toward its target: the focal point for FocalAssault, the enemy anchor for every other play", WReaderFocalAnchor),
        ("bc14w FEAS-SCN-6 an entrenched line with an open flank: the turn is the least penalized, the assaults pay the bar of three, FocalAssault stands down, the hold waits for the wing", WScn6),
        ("bc14w FOCAL-OFFER FocalAssault needs a focal point, is ruled out while a turn will do or a commander declines, and is otherwise priced by the model", WFocalOffer),
        ("bc14w FOCAL-WILLINGNESS willingness is half aggression plus half audacity less half caution", WFocalWillingness),
        ("bc14w CANDIDATE-CENTRED-W FocalAssault is evaluated once, never mirrored", WCentredFocal),
        ("bc14w FEAS-SCN-8 an entrenched salient: the apex is the focal point and an aggressive commander assaults it", WScn8),
        ("bc14w FEAS-SCN-9 a drawn line counts once its pieces are perceived and no further than they reach", WScn9),
        ("bc14w FOCAL-PLAYBOOK the playbook needs a judged focal point, takes the focal band as its main effort and the odds-based shift never moves it", WFocalPlaybook),
        ("bc14w FEAS-TIME-W the evaluation's path searches and sampled legs stay inside the counted bounds with FocalAssault", WFeasTimeFocal),
    };

    // Every candidate with FocalAssault last, for the timing bound.
    static readonly BattlePlanId[] AllPlansWithFocal = AllPlans.Concat(new[] { BattlePlanId.FocalAssault }).ToArray();

    // The candidates of the scenarios: the spec's conventions give the side no cavalry, so the cavalry plays fail their precondition and are not evaluated.
    static readonly BattlePlanId[] WorksPlans = AllPlansWithFocal.Where(p => p != BattlePlanId.CavalryRaid && p != BattlePlanId.CavalryScreeningDelay).ToArray();

    // The aggressive commander of scenario 7 (agg01 0.9, caution01 0.1, audacity01 0.9: Bar 0.68, willingness 0.85) and the cautious one (willingness -0.25).
    static OpportunityPersonality Aggressive() => FeasField.Personality(0.9f, 0.1f, 0.9f);
    static OpportunityPersonality Cautious() => FeasField.Personality(0.2f, 0.9f, 0.2f);

    // The vector the catalog scores personality fit against for the same commanders (the [-1, 1] form of the three 0-1 values).
    static PersonalityVector AggressiveVector() => new PersonalityVector(0.8f, -0.8f, 0.8f, 0f, 0f);

    static PlaybookContext WorksContext(IFeasibilityLookup lookup, float odds, bool pressure, PersonalityVector vector, BattleStance stance = BattleStance.Attacker)
        => new PlaybookContext(vector, TerrainKind.Open, odds, 0f, 0, 7, pressure, 0, 0f, 1f, 1f, false, false, stance, false, lookup);

    static void WReaderStance()
    {
        AssertEqual(TacticalPlaybookCatalog.PlaybookStance.Offensive, TacticalPlaybookCatalog.StanceOf(BattlePlanId.FocalAssault), "FocalAssault is offensive");
        AssertFalse(TacticalPlaybookCatalog.StanceAllowsPlaybook(BattleStance.Defender, BattlePlanId.FocalAssault), "a Defender is not offered it");
        AssertTrue(TacticalPlaybookCatalog.StanceAllowsPlaybook(BattleStance.Attacker, BattlePlanId.FocalAssault), "an Attacker may take it");
        AssertTrue(TacticalPlaybookCatalog.StanceAllowsPlaybook(BattleStance.Meeting, BattlePlanId.FocalAssault), "a Meeting side may take it, like any offensive play");

        // A Defender's candidate set never contains it, whatever focal point its ledger holds.
        FeasField f = Scenario7(Aggressive());
        FeasibilityLedger ledger = f.Evaluate(WorksPlans);
        AssertTrue(ledger.TryFocalPoint(out _), "control: the ledger has a focal point");
        var into = new BattlePlanId[32];
        int n = SeedCatalog.AllHistoricalAndGeneric().EligiblePlans(WorksContext(ledger, 2f, false, AggressiveVector(), BattleStance.Defender), into);
        for (int i = 0; i < n; i++) AssertTrue(into[i] != BattlePlanId.FocalAssault, "a Defender's candidates exclude FocalAssault");
        int attackers = SeedCatalog.AllHistoricalAndGeneric().EligiblePlans(WorksContext(ledger, 2f, false, AggressiveVector()), into);
        AssertTrue(Array.IndexOf(into, BattlePlanId.FocalAssault, 0, attackers) >= 0, "an Attacker's candidates include it once the ledger has a focal point");
    }

    static void WReaderFocalAnchor()
    {
        PlayAnchor.PlayTarget(BattlePlanId.FocalAssault, true, 111f, 222f, 5f, 6f, out float x, out float z);
        AssertNear(111f, x, 1e-4f, "FocalAssault faces the focal point");
        AssertNear(222f, z, 1e-4f, "FocalAssault faces the focal point");
        PlayAnchor.PlayTarget(BattlePlanId.FocalAssault, false, 111f, 222f, 5f, 6f, out x, out z);
        AssertTrue(x == 5f && z == 6f, "with no focal point it faces the enemy anchor");
        PlayAnchor.PlayTarget(BattlePlanId.ManeuverAndFix, true, 111f, 222f, 5f, 6f, out x, out z);
        AssertTrue(x == 5f && z == 6f, "every other play faces the enemy anchor, a focal point or not");

        // The evaluation's layout and the executor's zone assignment ask PlayAnchor.PlayFrame, so their rects are the same projection.
        FeasField f = Scenario7(Aggressive());
        FeasibilityLedger ledger = f.Evaluate(WorksPlans);
        AssertTrue(ledger.TryFocalPoint(out FocalPoint focal), "the ledger has the focal point");
        FeasibilityJob job = ledger.Job;
        var xs = new List<float>();
        var zs = new List<float>();
        BattleSnapshot s = f.Snapshot();
        for (int i = 0; i < s.OwnCount; i++)
        {
            if ((s.Own[i].Flags & UnitFlags.Active) == 0) continue;
            xs.Add(s.Own[i].X);
            zs.Add(s.Own[i].Z);
        }
        PlayAnchor.PlayFrame(BattlePlanId.FocalAssault, true, focal.X, focal.Z, job.EnemyX, job.EnemyZ, job.ArmyX, job.ArmyZ, xs, zs,
            out float tx, out float tz, out float frontage);
        AssertNear(focal.X, tx, 1e-3f, "the frame's target is the focal point");
        PlayTemplate template = PlayTemplateCatalog.Lookup(BattlePlanId.FocalAssault);
        IReadOnlyList<WorldPlayRect> expected = PlayProjection.Project(template, job.ArmyX, job.ArmyZ, tx, tz, frontage, default(TerrainLine), false);
        WorldPlayRect[] actual = job.RectsFor(BattlePlanId.FocalAssault, template.Rects.Count);
        AssertEqual(expected.Count, actual.Length, "the same rects");
        for (int i = 0; i < actual.Length; i++)
        {
            AssertNear(expected[i].AnchorX, actual[i].AnchorX, 0.01f, "rect " + i + " anchor x");
            AssertNear(expected[i].AnchorZ, actual[i].AnchorZ, 0.01f, "rect " + i + " anchor z");
        }

        // Both callers go through the one function (the executor's runtime is not compiled into the harness).
        string executor = System.IO.File.ReadAllText(FindRepoPath("src/WhiskeyRealism/Tactical/Orchestrator/Plays/PlayExecutorRuntime.cs"));
        string layout = System.IO.File.ReadAllText(FindRepoPath("src/WhiskeyRealism/Tactical/Orchestrator/Plays/ManeuverFeasibility.Candidates.cs"));
        string frame = System.IO.File.ReadAllText(FindRepoPath("src/WhiskeyRealism/Tactical/Orchestrator/Plays/ManeuverFeasibility.Job.cs"));
        AssertTrue(executor.Contains("PlayAnchor.PlayFrame("), "the executor's zone assignment calls PlayAnchor.PlayFrame");
        AssertTrue(layout.Contains("job.FrameFor(plan,"), "the evaluation's layout takes its frame from the job");
        AssertTrue(frame.Contains("PlayAnchor.PlayFrame(plan,"), "and the job takes FocalAssault's frame from PlayAnchor.PlayFrame");
    }

    static void WScn6()
    {
        FeasField f = Scenario6();
        FeasibilityLedger ledger = f.Evaluate(WorksPlans);

        // The authored ManeuverAndFix turns the open end (Task 4's numbers).
        BodyEstimate[] maf = BodiesArray(ledger, BattlePlanId.ManeuverAndFix);
        BodyEstimate wing = FirstWing(maf), fix = MainOf(maf);
        AssertNear(7700f, wing.Pin, 120f, "the wing's pinned enemy ~ 7,700");
        AssertNear(0.38f, wing.Detail, 0.05f, "the wing's detail 0.38");
        AssertNear(0f, fix.Detail, 0.02f, "the fix's detail 0, from the enemy outside works");

        // Every works assault pays the bar of three: the mirrored side, DoubleEnvelopment and the single-body plays.
        MafSides(f, out _, out BodyEstimate[] mirrored);
        AssertNear(1f, FirstWing(mirrored).Detail, 0.01f, "the mirrored side's wing strikes the works: detail 1");
        AssertNear(1f, WorstDetail(ledger, BattlePlanId.DoubleEnvelopment), 0.01f, "DoubleEnvelopment's left wing: detail 1");
        foreach (BattlePlanId single in new[] { BattlePlanId.GenericAggressive, BattlePlanId.FrontalShockAssault, BattlePlanId.ForcedFrontalAttack, BattlePlanId.ContinuousAttrition })
        {
            BodyEstimate[] bodies = BodiesArray(ledger, single);
            AssertEqual(1, bodies.Length, single + " is a single-body play");
            AssertTrue(bodies[0].AssaultsWorks, single + "'s main body assaults the works");
            AssertNear(1f, bodies[0].Detail, 0.01f, single + ": detail 1");
        }

        // FocalAssault stands down while a turn will do.
        CandidateResult focal = Result(ledger, BattlePlanId.FocalAssault);
        AssertEqual(FeasibilityVerdict.Impossible, focal.Verdict, "FocalAssault is Impossible");
        AssertEqual("turn-available", focal.Reason, "because a turn is available");

        // The authored ManeuverAndFix is selected.
        TacticalPlaybook winner = SeedCatalog.AllHistoricalAndGeneric().Select(MidContext(ledger, 35000f / 25000f, true));
        AssertEqual(BattlePlanId.ManeuverAndFix, winner.Id, "ManeuverAndFix is selected");
        AssertFalse(Result(ledger, BattlePlanId.ManeuverAndFix).Mirrored, "on its authored side");

        // The works hold waits for the wing: turn-not-struck until it reaches its Attack.
        ledger.CaptureSelection(BattlePlanId.ManeuverAndFix, false);
        var world = new FakeWorld();
        world.Phases[wing.ChildId] = ManeuverPhase.Hold;
        ledger.RefreshWorksHold(world);
        AssertTrue(ledger.WorksHold(out string reason), "the works hold is on while the wing waits");
        AssertEqual("turn-not-struck", reason, "because the turn is not struck");
        AssertTrue(ledger.FacesWorks(fix.ChildId), "the fix faces the works");
        AssertFalse(ledger.AssaultsWorks(fix.ChildId), "and assaults nothing");
        AssertFalse(ledger.FacesWorks(wing.ChildId), "the wing faces no works");
        world.Phases[wing.ChildId] = ManeuverPhase.March;
        ledger.RefreshWorksHold(world);
        AssertTrue(ledger.WorksHold(out _), "still on while it marches");
        world.Phases[wing.ChildId] = ManeuverPhase.Attack;
        ledger.RefreshWorksHold(world);
        AssertFalse(ledger.WorksHold(out reason), "the hold lifts when the wing reaches its Attack");
        AssertTrue(reason == null, "with no reason");
    }

    static void WFocalOffer()
    {
        var catalog = SeedCatalog.AllHistoricalAndGeneric();
        var into = new BattlePlanId[32];

        // No held works: the precondition fails and the selection is exactly as without the playbook.
        var open = new FeasField();
        open.AddCorps(-300f, 0f, 12000f);
        open.AddCorps(0f, 0f, 11000f);
        open.AddCorps(300f, 0f, 12000f);
        open.AddEnemyLine(25000f);
        FeasibilityLedger none = open.Evaluate(AllPlans);
        AssertFalse(none.TryFocalPoint(out _), "no held works, no focal point");
        PlaybookContext ctx = MidContext(none, 1.4f, true);
        int n = catalog.EligiblePlans(ctx, into);
        AssertTrue(Array.IndexOf(into, BattlePlanId.FocalAssault, 0, n) < 0, "FocalAssault fails its precondition with no focal point");
        var withReport = new SelectionReport();
        var withoutReport = new SelectionReport();
        TacticalPlaybookCatalog eighteen = SeedCatalog.AllHistoricalAndGeneric();
        TacticalPlaybook a = catalog.Select(ctx, withReport);
        TacticalPlaybook b = eighteen.Select(ctx, withoutReport);
        AssertEqual(a.Id, b.Id, "the winner is the same");
        AssertEqual(withoutReport.Count, withReport.Count, "and every candidate is scored");
        for (int i = 0; i < withReport.Count; i++) AssertNear(withoutReport[i].Score, withReport[i].Score, 1e-6f, "score " + i + " is unchanged");

        // The flag off: works are priced as open ground and nothing is offered.
        bool saved = WhiskeyRealism.Plugin.EnemyWorksDoctrine;
        try
        {
            WhiskeyRealism.Plugin.EnemyWorksDoctrine = false;
            FeasibilityLedger off = Scenario7(Aggressive()).Evaluate(WorksPlans);
            AssertFalse(off.TryFocalPoint(out _), "with Enable Enemy Works Doctrine off the ledger has no focal point");
            int m = catalog.EligiblePlans(WorksContext(off, 2f, false, AggressiveVector()), into);
            AssertTrue(Array.IndexOf(into, BattlePlanId.FocalAssault, 0, m) < 0, "and FocalAssault is not offered");
        }
        finally { WhiskeyRealism.Plugin.EnemyWorksDoctrine = saved; }

        // A feasible turn: Impossible, turn-available (scenario 6).
        CandidateResult turn = Result(Scenario6().Evaluate(WorksPlans), BattlePlanId.FocalAssault);
        AssertEqual(FeasibilityVerdict.Impossible, turn.Verdict, "with a feasible turn FocalAssault is Impossible");
        AssertEqual("turn-available", turn.Reason, "turn-available");

        // Both ends anchored (scenario 7): no turn will do. A cautious commander declines.
        CandidateResult declined = Result(Scenario7(Cautious()).Evaluate(WorksPlans), BattlePlanId.FocalAssault);
        AssertEqual(FeasibilityVerdict.Impossible, declined.Verdict, "an unwilling commander is Impossible");
        AssertEqual("declined", declined.Reason, "declined");



        // A willing one carries the model's penalty and is never mirrored.
        FeasibilityLedger ledger = Scenario7(Aggressive()).Evaluate(WorksPlans);
        CandidateResult offered = Result(ledger, BattlePlanId.FocalAssault);
        AssertTrue(offered.Verdict == FeasibilityVerdict.Feasible, "a willing commander's assault is Feasible, got " + offered.Verdict + " " + offered.Reason);
        AssertFalse(offered.Mirrored, "never mirrored");
        float worst = 0f;
        foreach (BodyEstimate body in BodiesArray(ledger, BattlePlanId.FocalAssault)) worst = Math.Max(worst, body.Penalty);
        AssertNear(worst, offered.Penalty, 1e-5f, "its penalty is its worst body's");
    }

    static void WFocalPlaybook()
    {
        var playbook = new FocalAssaultPlaybook();
        AssertEqual(BattlePlanId.FocalAssault, playbook.Id, "the playbook's id");
        AssertNear(1.2f, playbook.ReserveCommitTriggerOdds, 1e-6f, "reserves commit at 1.2: Grant at Spotsylvania and Upton");

        // Its main effort is the focal point's band, set at its pick; with no band, the default.
        var withBand = new PlaybookContext(default(PersonalityVector), TerrainKind.Open, 1f, 0f, 4, 7, focalSector: 2, hasFocalPoint: true);
        AssertEqual(2, playbook.Instantiate(withBand).MainEffortSector, "the focal point's band");
        AssertEqual(BattlePhase.Probe, playbook.Instantiate(withBand).Phase, "it opens in Probe");
        var noBand = new PlaybookContext(default(PersonalityVector), TerrainKind.Open, 1f, 0f, 4, 7, hasFocalPoint: true);
        AssertEqual(4, playbook.Instantiate(noBand).MainEffortSector, "with no band the default main effort");

        // The candidate listing scores without the ledger: a focal point published by the last evaluation is enough to list it.
        AssertTrue(playbook.MatchesPreconditions(noBand), "listed with a focal point and no ledger in the context");
        AssertFalse(playbook.MatchesPreconditions(new PlaybookContext(default(PersonalityVector), TerrainKind.Open, 1f, 0f, 4, 7)), "not without one");

        // A selection that scores with the ledger needs the ledger to have judged it: an evaluation made before the works were seen did not list it.
        FeasibilityLedger unjudged = Scenario7(Aggressive()).Evaluate(AllPlans);
        AssertTrue(unjudged.TryFocalPoint(out _), "control: the unjudged ledger holds a focal point");
        AssertFalse(unjudged.TryGet(BattlePlanId.FocalAssault, out _), "control: and no result for FocalAssault");
        var report = new SelectionReport();
        SeedCatalog.AllHistoricalAndGeneric().Select(WorksContext(unjudged, 2f, false, AggressiveVector()), report);
        for (int i = 0; i < report.Count; i++) AssertTrue(report[i].Plan != BattlePlanId.FocalAssault, "an unjudged FocalAssault is not scored");
        AssertTrue(report.PreconditionFiltered > 0, "it is filtered by its precondition");

        // The odds-based main-effort shift never moves a FocalAssault plan's band.
        var army = new ArmyOrchestrator(0, new TacticalPlaybookCatalog(), default(PersonalityVector));
        army.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.FocalAssault, BattlePhase.MainEffort, 2, new int[0], new int[0], 1.2f, 0f, 0));
        AssertFalse(army.ConsiderMainEffortShift(5, 9f, 1f), "no shift for a FocalAssault plan");
        AssertEqual(2, army.CurrentPlan.MainEffortSector, "the band stays");
        army.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort, 2, new int[0], new int[0], 1.2f, 0f, 0));
        AssertTrue(army.ConsiderMainEffortShift(5, 9f, 1f), "control: another plan shifts");
    }

    static void WFocalWillingness()
    {
        AssertNear(0.85f, WorksRules.Willingness(FeasField.Personality(0.9f, 0.1f, 0.9f)), 1e-4f, "agg01 0.9, caution01 0.1, audacity01 0.9");
        AssertNear(0.25f, WorksRules.Willingness(FeasField.Personality(0.5f, 0.5f, 0.5f)), 1e-4f, "the mid commander");
        AssertNear(-0.25f, WorksRules.Willingness(FeasField.Personality(0.2f, 0.9f, 0.2f)), 1e-4f, "agg01 0.2, caution01 0.9, audacity01 0.2");
        AssertNear(0.2f, WorksRules.FocalWillingness, 1e-6f, "the floor is 0.2");
        FeasibilityLedger mid = Scenario7().Evaluate(WorksPlans);
        AssertTrue(Result(mid, BattlePlanId.FocalAssault).Reason != "declined", "the mid commander (0.25) is willing");
    }

    static void WCentredFocal()
    {
        FeasibilityLedger ledger = Scenario7(Aggressive()).Evaluate(WorksPlans);
        int index = Array.IndexOf(WorksPlans, BattlePlanId.FocalAssault);
        AssertEqual(1, ledger.Job.SidesEvaluated[index], "FocalAssault is evaluated on one side");
        AssertFalse(Result(ledger, BattlePlanId.FocalAssault).Mirrored, "and not mirrored");
        AssertFalse(FeasibilityTemplatesEvaluatedOnBothSides(BattlePlanId.FocalAssault), "its template is centred, so it is not two-sided");
    }

    static bool FeasibilityTemplatesEvaluatedOnBothSides(BattlePlanId plan) => FeasibilityTemplates.EvaluatedOnBothSides(plan);

    static void WScn8()
    {
        // The spec's numbers are the mid commander's (N = 27,500); the aggressive commander's lower fog premium moves them a little.
        FeasibilityLedger mid = Scenario8().Evaluate(WorksPlans);
        BodyEstimate[] midBodies = BodiesArray(mid, BattlePlanId.FocalAssault);
        BodyEstimate midAssault = FirstWing(midBodies);
        AssertNear(2.12f, midAssault.IsolatedOdds, 0.1f, "the assault's odds ~ 2.12 (L 3,300, shiftShare 0.69, E ~ 16,000)");
        AssertNear(16000f, midAssault.EnemyAgainst, 600f, "E ~ 16,000");
        AssertNear(0.59f, midAssault.EnemyShiftHours, 0.1f, "tau ~ 0.59");
        AssertNear(1f, midAssault.Detail, 0.05f, "the mid commander's bar of three: detail 1");
        AssertTrue(MainOf(midBodies).DefenseRatio >= WorksRules.WorksDefenseRatio - 1e-3f, "the demonstration pins at the works ratio, the enemy anchor standing behind the apex");

        FeasField f = Scenario8(Aggressive());
        FeasibilityLedger ledger = f.Evaluate(WorksPlans);
        AssertTrue(ledger.TryFocalPoint(out FocalPoint focal), "the salient has a focal point");
        AssertNear(0f, focal.X - FeasField.X(0f), 1f, "at lateral 0");
        AssertNear(350f, focal.Z - FeasField.Z(0f), 1f, "at the apex, depth 350");
        AssertEqual(FocalKind.Salient, focal.Kind, "a salient");
        AssertNear(3.5f, focal.Score, 0.05f, "scoring 3,000 x 1.4 / 600 x 0.5 = 3.5");

        CandidateResult result = Result(ledger, BattlePlanId.FocalAssault);
        BodyEstimate[] bodies = BodiesArray(ledger, BattlePlanId.FocalAssault);
        AssertEqual(2, bodies.Length, "an assault body and the demonstration");
        BodyEstimate assault = FirstWing(bodies);
        AssertNear(34000f, assault.Strength, 1f, "the column and the support form one assault body");
        AssertTrue(assault.AssaultsWorks, "it assaults the works");
        AssertNear(WorksRules.AssaultHours, assault.SupportHours, 1e-4f, "its window is the assault hour, not the support time");
        AssertTrue(assault.ExposureWindowHours >= WorksRules.AssaultHours - 1e-4f, "plus the strike spread");
        AssertNear(0f, assault.Detail, 0.02f, "an aggressive commander's detail is 0");
        AssertEqual(FeasibilityVerdict.Feasible, result.Verdict, "FocalAssault is Feasible");

        TacticalPlaybook winner = SeedCatalog.AllHistoricalAndGeneric().Select(WorksContext(ledger, 2.04f, false, AggressiveVector()));
        AssertEqual(BattlePlanId.FocalAssault, winner.Id, "the aggressive commander selects it");

    }

    static void WScn9()
    {
        // Scenario 6's armies and geometry; the works are a drawn line, one piece every 20 units from -600 to +150 at depth 600.
        var table = new WorksPointTable();
        var pieces = new List<WorksPiece>();
        int id = 100;
        for (float l = -600f; l <= 150.001f; l += 20f)
            pieces.Add(new WorksPiece { InstanceId = id++, X = X(l), Z = Z(600f), Completion = 1f, InOwnZone = false });
        table.Merge(pieces.ToArray(), X(0f), Z(0f));
        var ground = new PieceGround();

        FeasField f = Scenario6();
        f.WorksPoints = table;
        SnapshotPicture(f, out WorksPicture before);
        AssertEqual(2, before.HeldSegmentCount, "before any probe the picture holds the two clusters' samples as two segments");
        AssertEqual(0, before.WorksPointsCounted, "and no piece");

        table.ProbeStep(ground, WorksRules.MaxWorksProbesPerTick);
        SnapshotPicture(f, out WorksPicture one);
        AssertEqual(WorksRules.MaxWorksProbesPerTick, one.WorksPointsCounted, "one tick perceives 16 pieces, oldest first");

        table.ProbeStep(ground, WorksRules.MaxWorksProbesPerTick);
        table.ProbeStep(ground, WorksRules.MaxWorksProbesPerTick);
        SnapshotPicture(f, out WorksPicture after);
        AssertEqual(1, after.HeldSegmentCount, "the perceived pieces chain into one segment");
        AssertEqual(1, after.SegmentCount, "and it is the only one");
        AssertNear(-600f, after.Segment(0).LateralMin, 25f, "from the first piece");
        AssertNear(150f, after.Segment(0).LateralMax, 25f, "to the last piece and no further");

        // The outcome is scenario 6's: the authored ManeuverAndFix turns the open end.
        FeasibilityLedger ledger = f.Evaluate(WorksPlans);
        TacticalPlaybook winner = SeedCatalog.AllHistoricalAndGeneric().Select(MidContext(ledger, 35000f / 25000f, true));
        AssertEqual(BattlePlanId.ManeuverAndFix, winner.Id, "ManeuverAndFix is selected");
        AssertFalse(Result(ledger, BattlePlanId.ManeuverAndFix).Mirrored, "on its authored side");

        // A piece the side has not perceived changes nothing.
        pieces.Add(new WorksPiece { InstanceId = id++, X = X(400f), Z = Z(600f), Completion = 1f, InOwnZone = false });
        table.Merge(pieces.ToArray(), X(0f), Z(0f));
        table.ProbeStep(ground, WorksRules.MaxWorksProbesPerTick);
        SnapshotPicture(f, out WorksPicture unperceived);
        AssertNear(150f, unperceived.Segment(0).LateralMax, 25f, "an unperceived piece beyond the end does not extend the works");
    }

    // The ground the probe asks: everything left of lateral +300 is seen, nothing beyond it (the runtime's PerceivedGroundRuntime).
    sealed class PieceGround : IPerceivedGround
    {
        public bool IsPerceived(float x, float z) => x < X(300f);
    }

    static void SnapshotPicture(FeasField f, out WorksPicture picture)
    {
        BattleSnapshot s = f.Snapshot();
        Bodies(s, out float[] bx, out float[] bz, out int count);
        picture = new WorksPicture();
        picture.BuildEnemy(s, Axis(), bx, bz, count, f.WorksPoints, null);
    }

    static void WFeasTimeFocal()
    {
        var fords = new (float lateral, float depth)[10];
        for (int i = 0; i < fords.Length; i++) fords[i] = (0f, -900f + i * 200f);
        BattleTerrainGrid grid = HorizontalBandWithFordsLateral(150f, 170f, -1000f, 800f, fords.Length);
        var f = new FeasField(grid);
        for (int c = 0; c < 8; c++) f.AddCorps(-420f + c * 120f, 0f, 6000f, regiments: 2);
        f.AddEnemy(-450f, 600f, 12000f, cover: 0.40f, coverObject: 7, standing: true);
        f.AddEnemy(0f, 600f, 12000f, cover: 0.40f, coverObject: 7, standing: true);
        f.AddEnemy(450f, 600f, 12000f, cover: 0.40f, coverObject: 7, standing: true);
        f.WorksPoints = new Points().Line(-600f, 600f, 50f, l => 600f);
        f.Stance = BattleStance.Meeting;
        var ledger = new FeasibilityLedger();
        FeasibilityInput input = f.Build(AllPlansWithFocal);
        ManeuverFeasibility.Evaluate(in input, ledger);
        FeasibilityJob job = ledger.Job;
        AssertTrue(ledger.TryFocalPoint(out _), "control: the fixture has a focal point, so FocalAssault is evaluated");
        int sides = 0;
        for (int i = 0; i < AllPlansWithFocal.Length; i++) sides += job.SidesEvaluated[i];
        AssertEqual(22, sides, "19 candidates, three of them on both sides");
        AssertTrue(job.MaxSearchesPerSide <= 29, "at most 29 path searches for one side, got " + job.MaxSearchesPerSide);
        AssertTrue(job.Graph.Searches <= 638, "at most 638 path searches in all, got " + job.Graph.Searches);
        AssertTrue(job.Graph.LegsSampled <= 2398, "at most 2,398 sampled legs, got " + job.Graph.LegsSampled);
        Console.WriteLine("FEAS-TIME-W searches=" + job.Graph.Searches + " legs=" + job.Graph.LegsSampled + " maxPerSide=" + job.MaxSearchesPerSide);
    }
}
