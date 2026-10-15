using System;
using System.Collections.Generic;
using System.Text;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Strategic.Policies;

using static TestHarness;

// The time-sliced daily strategic review (perf slice, M2). DailyReviewScheduler runs
// DailyReviewJobList in order across frames; StrategicCoordinator publishes the working
// StrategicLedgerSet when the day completes. The coordinator is a MonoBehaviour over vanilla
// state, so the determinism tests drive the same scheduler, job list and ledger sets with a
// model review whose jobs read and write the working set through the coordinator's View rule
// (working inside a job, published outside), using the real pure ledgers. Its jobs reproduce
// the coordinator's dependency web: same-day reads of earlier jobs (posture -> front ->
// formation -> probe), previous-day reads of later jobs (fiscal and defense read yesterday's
// front / formation), and the in-place mutations (CIC plan, era, director memory, policy plan,
// the probe overlay on a formation ledger that was not rebuilt today).
internal static class DailyReviewSchedulerTests
{
    public static void Run()
    {
        RunAll(new (string name, Action run)[]
        {
            ("daily review job list is map then per alliance cic through report", JobListOrder),
            ("daily review zero budget runs exactly one job per pump", ZeroBudgetOneJobPerPump),
            ("daily review slice is at most budget plus one job", SliceBoundedByBudgetPlusOneJob),
            ("daily review drain finishes the day in one call", DrainFinishesDay),
            ("daily review job fault skips the rest and completes the day", FaultSkipsRestAndCompletes),
            ("daily review cannot begin a day while one is in flight", BeginWhileInFlightThrows),
            ("daily review clear inside a job abandons the day", ClearInsideJobAbandons),
            ("daily review running job flag is set only inside a job", RunningJobOnlyInsideJob),
            ("daily review sliced and unsliced publish identical days", SlicedMatchesUnsliced),
            ("daily review reader between slices sees the previous day", ReaderBetweenSlicesSeesPreviousDay),
            ("daily review new day mid-flight drains the old day first", NewDayMidFlightDrainsFirst),
            ("daily review coordinator routes every published field through the view", CoordinatorRoutesThroughView),
            ("daily review coordinator drains before new day and save, clears on reset", CoordinatorDrainAndResetHooks),
            ("daily review patches pump each frame and drain before vanilla saves", PatchesPumpAndDrain),
            ("daily review commit keeps the previous day published when the copy throws", CommitKeepsPreviousDayWhenCopyThrows),
            ("daily review commit applies deferred battles even when the publish throws", CommitAppliesDeferredWhenPublishThrows),
            ("daily review commit keeps both failures when publish and deferred battles throw", CommitKeepsBothFailures),
            ("daily review jobs hand off what the single-frame review did", JobHandOffsPinned),
            ("daily review published day swaps only a complete copy", PublishedDaySwapsOnlyCompleteCopy),
            ("daily review commit prepares every copy before swapping any", CommitPreparesEveryCopyBeforeSwapping),
        });
    }

    private static long Ticks(long[] clock) => clock[0];

    private static void JobListOrder()
    {
        var jobs = DailyReviewJobList.Ordered;
        var expected = new List<string> { "Map:-1" };
        string[] perAlliance = { "Cic", "Fiscal", "Policy", "Plan", "Front", "Defense", "Execute", "Formation", "Probe", "ArmyArea", "Construction", "Report" };
        for (int a = 0; a < 2; a++)
            foreach (var kind in perAlliance)
                expected.Add(kind + ":" + a);
        AssertEqual(expected.Count, jobs.Length, "25 jobs");
        for (int i = 0; i < jobs.Length; i++)
            AssertEqual(expected[i], jobs[i].Kind + ":" + jobs[i].Alliance, "job " + i);
    }

    private static void ZeroBudgetOneJobPerPump()
    {
        var s = new DailyReviewScheduler();
        var ran = new List<int>();
        var clock = new long[1];
        s.Begin(5);
        for (int pump = 0; pump < 5; pump++)
        {
            bool done = s.Pump(0, () => Ticks(clock), j => { ran.Add(j); clock[0] += 3; }, null);
            AssertEqual(pump + 1, ran.Count, "one job on pump " + pump);
            AssertEqual(pump == 4, done, "completes on the last pump only");
        }
        AssertFalse(s.InFlight, "idle after the day");
        AssertEqual(5, s.Slices, "five slices");
        AssertFalse(s.Pump(0, () => Ticks(clock), j => ran.Add(j), null), "idle pump does nothing");
        AssertEqual(5, ran.Count, "no job after completion");
    }

    private static void SliceBoundedByBudgetPlusOneJob()
    {
        var rng = new Random(11);
        for (int trial = 0; trial < 200; trial++)
        {
            var s = new DailyReviewScheduler();
            var clock = new long[1];
            int jobs = 1 + rng.Next(30);
            var costs = new long[jobs];
            long maxJob = 0;
            for (int i = 0; i < jobs; i++)
            {
                costs[i] = 1 + rng.Next(9);
                maxJob = Math.Max(maxJob, costs[i]);
            }
            long budget = rng.Next(25);
            s.Begin(jobs);
            int ran = 0;
            int pumps = 0;
            while (s.InFlight)
            {
                long start = clock[0];
                int before = ran;
                s.Pump(budget, () => Ticks(clock), j => { AssertEqual(ran, j, "in order"); ran++; clock[0] += costs[j]; }, null);
                pumps++;
                long slice = clock[0] - start;
                AssertTrue(ran - before >= 1, "at least one job per pump");
                AssertTrue(slice <= budget + maxJob, "slice " + slice + " <= budget " + budget + " + max job " + maxJob);
                AssertTrue(pumps <= jobs, "never more pumps than jobs");
            }
            AssertEqual(jobs, ran, "every job ran once");
        }
    }

    private static void DrainFinishesDay()
    {
        var s = new DailyReviewScheduler();
        var ran = new List<int>();
        var clock = new long[1];
        s.Begin(7);
        AssertFalse(s.Pump(0, () => Ticks(clock), j => { ran.Add(j); clock[0]++; }, null), "first slice");
        AssertTrue(s.Drain(() => Ticks(clock), j => { ran.Add(j); clock[0]++; }, null), "drain completes");
        AssertEqual("0,1,2,3,4,5,6", string.Join(",", ran), "all jobs once, in order");
        AssertFalse(s.Drain(() => Ticks(clock), j => ran.Add(j), null), "drain when idle is a no-op");
    }

    private static void FaultSkipsRestAndCompletes()
    {
        var s = new DailyReviewScheduler();
        var ran = new List<int>();
        var faults = new List<int>();
        s.Begin(6);
        bool done = s.Drain(() => 0, j =>
        {
            ran.Add(j);
            if (j == 2) throw new InvalidOperationException("boom");
        }, (j, ex) => faults.Add(j));
        AssertTrue(done, "a faulted day completes so the caller publishes");
        AssertTrue(s.Faulted, "faulted");
        AssertEqual("0,1,2", string.Join(",", ran), "jobs after the fault are skipped");
        AssertEqual("2", string.Join(",", faults), "fault reported once with its job");
        AssertFalse(s.RunningJob, "flag cleared after a fault");
        s.Begin(1);
        AssertFalse(s.Faulted, "a new day starts clean");
    }

    private static void BeginWhileInFlightThrows()
    {
        var s = new DailyReviewScheduler();
        s.Begin(3);
        AssertThrows(() => s.Begin(3), "begin while in flight");
        s.Drain(() => 0, j => { }, null);
        s.Begin(3);
        AssertTrue(s.InFlight, "begins after drain");
    }

    private static void ClearInsideJobAbandons()
    {
        var s = new DailyReviewScheduler();
        var ran = new List<int>();
        s.Begin(5);
        bool done = s.Drain(() => 0, j =>
        {
            ran.Add(j);
            if (j == 1) s.Clear();
        }, null);
        AssertFalse(done, "an abandoned day does not complete");
        AssertFalse(s.InFlight, "idle after clear");
        AssertEqual("0,1", string.Join(",", ran), "nothing runs after clear");
    }

    private static void RunningJobOnlyInsideJob()
    {
        var s = new DailyReviewScheduler();
        var inside = new List<bool>();
        bool nested = true;
        s.Begin(3);
        AssertFalse(s.RunningJob, "not before");
        s.Drain(() => 0, j =>
        {
            inside.Add(s.RunningJob);
            nested = s.Pump(long.MaxValue, () => 0, k => inside.Add(false), null);
        }, null);
        AssertFalse(s.RunningJob, "not after");
        AssertEqual("True,True,True", string.Join(",", inside), "set in every job, nested pump ran nothing");
        AssertFalse(nested, "a pump from inside a job does nothing");
    }

    // ---- determinism -----------------------------------------------------------------------

    private const int Days = 40;

    private static void SlicedMatchesUnsliced()
    {
        var unsliced = new ModelReview();
        for (int day = 1; day <= Days; day++)
        {
            unsliced.BeginDay(day);
            AssertTrue(unsliced.Drain(), "unsliced day completes in one call");
        }

        var oneJob = new ModelReview();
        for (int day = 1; day <= Days; day++)
        {
            oneJob.BeginDay(day);
            int pumps = 0;
            while (!oneJob.Pump(0))
                AssertTrue(++pumps < DailyReviewJobList.Ordered.Length, "day " + day + " finishes within one pump per job");
            AssertEqual(DailyReviewJobList.Ordered.Length - 1, pumps, "one job per frame");
        }

        for (int seed = 1; seed <= 25; seed++)
        {
            var rng = new Random(seed);
            var random = new ModelReview(seed);
            for (int day = 1; day <= Days; day++)
            {
                random.BeginDay(day);
                int pumps = 0;
                while (!random.Pump(rng.Next(0, 40)))
                    AssertTrue(++pumps < DailyReviewJobList.Ordered.Length, "day " + day + " finishes within one pump per job");
            }
            AssertDaysEqual(unsliced, random, "random budgets seed " + seed);
        }

        AssertDaysEqual(unsliced, oneJob, "one job per frame");
        AssertTrue(unsliced.Committed[Days - 1] != unsliced.Committed[Days - 2], "the model changes state from day to day");
    }

    private static void ReaderBetweenSlicesSeesPreviousDay()
    {
        var review = new ModelReview();
        string previous = Signature(review.Published);
        int halfDays = 0;
        for (int day = 1; day <= Days; day++)
        {
            review.BeginDay(day);
            for (int pump = 0; ; pump++)
            {
                AssertTrue(pump < DailyReviewJobList.Ordered.Length, "day " + day + " finishes within one pump per job");
                bool done = review.Pump(0);
                // A patch reading between slices (RunningJob false) gets the published set.
                string reader = Signature(review.ReaderView);
                if (done)
                {
                    AssertEqual(review.Committed[day - 1], reader, "day " + day + " published at once");
                    break;
                }
                AssertEqual(previous, reader, "day " + day + " mid-review reader sees the previous day");
                if (Signature(review.Work) != previous) halfDays++;
            }
            previous = review.Committed[day - 1];
        }
        AssertTrue(halfDays > Days, "the working set really is half-updated between slices");
        AssertTrue(review.InJobReadsSawWorking > 0 && review.InJobReadsSawPublished == 0, "jobs read the working set");
    }

    private static void NewDayMidFlightDrainsFirst()
    {
        var unsliced = new ModelReview();
        for (int day = 1; day <= Days; day++)
        {
            unsliced.BeginDay(day);
            unsliced.Drain();
        }

        var rng = new Random(5);
        var hurried = new ModelReview(5);
        for (int day = 1; day <= Days; day++)
        {
            // The coordinator's NotifyDateAdvanced: finish the old day, then begin the new one.
            hurried.Drain();
            hurried.BeginDay(day);
            int frames = rng.Next(0, 4);
            for (int f = 0; f < frames && !hurried.Pump(0); f++) { }
        }
        hurried.Drain();
        AssertDaysEqual(unsliced, hurried, "a day cut short by the next date change");
    }

    private static void AssertDaysEqual(ModelReview expected, ModelReview actual, string label)
    {
        AssertEqual(expected.Committed.Count, actual.Committed.Count, label + ": day count");
        for (int i = 0; i < expected.Committed.Count; i++)
        {
            AssertEqual(expected.Committed[i], actual.Committed[i], label + ": day " + (i + 1));
            AssertEqual(expected.CommittedPlanner[i], actual.CommittedPlanner[i], label + ": planner, day " + (i + 1));
        }
        AssertEqual(Signature(expected.Work), Signature(actual.Work), label + ": final working state");
    }

    // The model review. Inputs are a pure function of (day, alliance, job), standing in for
    // the vanilla state a job reads; everything else flows through the ledger sets.
    private sealed class ModelReview
    {
        internal readonly StrategicLedgerSet Work = new StrategicLedgerSet();
        internal readonly StrategicLedgerSet Published = new StrategicLedgerSet();
        internal readonly DailyReviewScheduler Scheduler = new DailyReviewScheduler();
        internal readonly List<string> Committed = new List<string>();
        internal readonly List<string> CommittedPlanner = new List<string>();
        // The campaign planner's state (CampaignPlannerRuntime: plan store, beliefs, executors)
        // lives outside the ledger set; the model carries it so the sliced review is checked
        // with the planner's jobs too.
        private readonly string[] _plans = new string[2];
        private readonly string[] _executed = new string[2];
        internal int InJobReadsSawWorking;
        internal int InJobReadsSawPublished;
        private readonly Random _cost;
        private long _clock;
        private int _day;

        internal ModelReview(int costSeed = 0)
        {
            _cost = new Random(costSeed);
        }

        // StrategicCoordinator.View.
        private StrategicLedgerSet View => Scheduler.RunningJob ? Work : Published;

        internal StrategicLedgerSet ReaderView => View;

        internal void BeginDay(int day)
        {
            _day = day;
            Scheduler.Begin(DailyReviewJobList.Ordered.Length);
        }

        internal bool Pump(long budget) => Finish(Scheduler.Pump(budget, () => _clock, RunJob, Fail));

        internal bool Drain() => Finish(Scheduler.Drain(() => _clock, RunJob, Fail));

        private bool Finish(bool completed)
        {
            if (completed)
            {
                Published.PublishFrom(Work);
                Committed.Add(Signature(Published));
                CommittedPlanner.Add("plans:" + _plans[0] + "," + _plans[1] + "|exec:" + _executed[0] + "," + _executed[1]);
            }
            return completed;
        }

        private static void Fail(int job, Exception ex) => throw new InvalidOperationException("model job " + job + " failed", ex);

        private void RunJob(int index)
        {
            _clock += 1 + _cost.Next(9);
            var job = DailyReviewJobList.Ordered[index];
            if (ReferenceEquals(View, Work)) InJobReadsSawWorking++; else InJobReadsSawPublished++;
            int a = job.Alliance;
            var rng = new Random(_day * 7919 + (a + 1) * 104729 + (int)job.Kind * 31);
            switch (job.Kind)
            {
                case DailyReviewJobKind.Map:
                    if (_day % 7 == 1)
                    {
                        var towns = new List<CampaignMapTown>();
                        for (int i = 0; i < 4; i++)
                            towns.Add(new CampaignMapTown { CityName = "T" + i, StateId = i, Owner = rng.Next(2), X = i * 100f, Z = i * 50f });
                        Work.CampaignMap = CampaignMapLedger.Build(towns);
                    }
                    break;

                case DailyReviewJobKind.Cic:
                {
                    if (View.CICs[a] == null || _day % 10 == 0)
                    {
                        Work.CICs[a] = Work.CICs[a] ?? new CIC { AllianceId = a, OfficerName = "cic" + a };
                        Work.CICs[a].ActivePlan = new OperationalPlan
                        {
                            OperationId = "op" + _day,
                            Phases = { new Phase { TargetObjectiveId = 10 + a }, new Phase { TargetObjectiveId = 20 + rng.Next(5) } }
                        };
                    }
                    var plan = View.CICs[a].ActivePlan;
                    plan.CurrentPhaseIndex = (_day / 3) % plan.Phases.Count;
                    plan.IsDirty = rng.Next(2) == 0;
                    plan.OperationLastDecisionDaySerial = _day;
                    plan.Phases[plan.CurrentPhaseIndex].PhaseStartedDaySerial = _day;

                    Work.Eras[a] = Work.Eras[a] ?? new EraStageManager();
                    View.Eras[a].CheckTransition(1 + _day % 12, 1861 + _day / 12, false, false);

                    // Posture from yesterday's front (the front job runs later today).
                    var memory = View.DirectorMemories[a];
                    string front = View.Fronts[a]?.Signature() ?? "none";
                    memory.LastPosture = new DirectorPosture
                    {
                        AllianceId = a,
                        Pace = (CampaignPace)(Math.Abs(front.GetHashCode()) % 3),
                        Reason = "d" + _day,
                        MinimumHoldRatioModifier = (rng.Next(7) - 3) * 0.01f
                    };
                    memory.CapitalDangerStreakDays = rng.Next(2) == 0 ? memory.CapitalDangerStreakDays + 1 : 0;
                    memory.LastFullRefreshDay = _day;
                    memory.RecentEventSummaries.Add("d" + _day);
                    if (memory.RecentEventSummaries.Count > 3) memory.RecentEventSummaries.RemoveAt(0);
                    break;
                }

                case DailyReviewJobKind.Fiscal:
                    Work.FiscalIntents[a] = new FiscalOutput
                    {
                        Posture = (FiscalPosture)((View.Fronts[a]?.Signature() ?? "").Length % 4),
                        TheaterSupplyPriority = "fiscal" + _day,
                        Signature = "f" + _day + "|" + (View.Fronts[a]?.Signature() ?? "-")
                    };
                    break;

                case DailyReviewJobKind.Policy:
                {
                    var scored = new List<PolicyScore>();
                    for (int i = 0; i < 6; i++)
                        scored.Add(new PolicyScore { PolicyId = 100 + i, Total = (float)rng.NextDouble() * (1 + (int)View.FiscalIntents[a].Posture) });
                    var values = new float[PolicyEffectCatalog.NeedKindCount];
                    for (int i = 0; i < values.Length; i++) values[i] = (float)rng.NextDouble();
                    var need = NeedVector.FromArray(values);
                    View.PolicyPlans[a].Update(scored, need, View.Needs[a]);
                    Work.Needs[a] = need;
                    Work.NeedSnapshots[a] = new NeedSnapshot { Alliance = a, AnnualBalance = _day };
                    break;
                }

                case DailyReviewJobKind.Plan:
                    // Today's posture (CIC) and policy, yesterday's front (the front runs later).
                    _plans[a] = "p" + _day + ":" + View.DirectorMemories[a].LastPosture.Pace + "/" + View.PolicyPlans[a].CurrentPick
                        + "/" + (View.Fronts[a]?.Signature() ?? "-") + "/" + rng.Next(100);
                    break;

                case DailyReviewJobKind.Execute:
                    // Today's plan and defense intent.
                    _executed[a] = _plans[a] + "|" + View.DefenseIntents[a].Signature;
                    break;

                case DailyReviewJobKind.Front:
                {
                    var posture = View.DirectorMemories[a].LastPosture;
                    var inputs = new List<FrontSectorInput>();
                    foreach (var theater in new[] { Theater.East, Theater.West, Theater.River })
                    {
                        inputs.Add(new FrontSectorInput
                        {
                            SectorKey = theater.ToString(),
                            Theater = theater,
                            OwnStrength = 1000f + rng.Next(20000),
                            EnemyStrength = 1000f + rng.Next(20000),
                            StrategicImportance = 0.5f,
                            AverageMorale = 0.8f,
                            AverageSupply = (float)rng.NextDouble(),
                            AverageReadiness = 0.8f
                        });
                    }
                    Work.Fronts[a] = FrontSectorLedger.Build(inputs, new FrontLedgerOptions { MinimumHoldRatio = 0.9f + posture.MinimumHoldRatioModifier });
                    break;
                }

                case DailyReviewJobKind.Defense:
                    // Today's front, yesterday's formations (formation runs later).
                    Work.DefenseIntents[a] = new DefenseIntentLedgerOutput
                    {
                        AllianceId = a,
                        Signature = View.Fronts[a].Signature() + "|" + (View.FormationDirectives[a]?.Summary() ?? "-")
                    };
                    break;

                case DailyReviewJobKind.Formation:
                    if (_day % 2 == a % 2 || View.FormationDirectives[a] == null)
                    {
                        var snapshots = new List<FormationSnapshot>();
                        for (int i = 0; i < 3; i++)
                        {
                            var sector = View.Fronts[a].GetSector(i == 0 ? "East" : "River");
                            snapshots.Add(new FormationSnapshot
                            {
                                UnitKey = "u" + a + i,
                                AllianceId = a,
                                UnitType = 14 + i,
                                IsTopUnit = true,
                                GroupStrengthActive = 1000f + rng.Next(9000),
                                Morale = (float)rng.NextDouble(),
                                Readiness = 0.8f,
                                RifleAmmo = (float)rng.NextDouble(),
                                ArtilleryAmmo = 0.8f,
                                Supply = (float)rng.NextDouble(),
                                WeaponFirepower = 1f,
                                AreaKey = "A" + i,
                                SectorKey = sector?.SectorKey,
                                FrontPosture = sector != null ? sector.Posture : FrontPosture.Hold,
                                LocalEnemyStrength = rng.Next(8000)
                            });
                        }
                        Work.FormationDirectives[a] = FormationDirectiveLedger.Build(snapshots, View.Eras[a].Stage, "A1");
                    }
                    break;

                case DailyReviewJobKind.Probe:
                {
                    // On days the formations were not rebuilt this mutates yesterday's ledger.
                    var decisions = new[] { OperationalProbeDecision.Probe, OperationalProbeDecision.Pause, OperationalProbeDecision.Withdraw, OperationalProbeDecision.Escalate };
                    View.FormationDirectives[a].ApplyOperationalProbe(new OperationalProbeOutput
                    {
                        SelectedUnitKey = "u" + a + (_day % 3),
                        Decision = decisions[rng.Next(decisions.Length)],
                        Reason = "probe" + _day
                    });
                    break;
                }

                case DailyReviewJobKind.ArmyArea:
                    if (_day % 7 == 3)
                    {
                        var inputs = new List<ArmyAreaInput>();
                        for (int i = 0; i < 3; i++)
                            inputs.Add(new ArmyAreaInput { UnitKey = "u" + a + i, AllianceId = a, CurrentAreaKey = "A" + rng.Next(3), Strength = rng.Next(9000) });
                        Work.ArmyAreas[a] = ArmyAreaLedger.Build(inputs, "A1");
                    }
                    break;

                case DailyReviewJobKind.Construction:
                    Work.ConstructionIntents[a] = new ConstructionOutput
                    {
                        Posture = (ConstructionPosture)(View.FormationDirectives[a].Pressure.LowSupplyCount % 4),
                        TopConstructionTheater = View.FiscalIntents[a].TheaterSupplyPriority,
                        Signature = "c" + _day + "|" + View.FormationDirectives[a].Summary()
                    };
                    break;

                case DailyReviewJobKind.Report:
                    break;
            }
        }
    }

    internal static string Signature(StrategicLedgerSet set)
    {
        var sb = new StringBuilder();
        for (int a = 0; a < 2; a++)
        {
            sb.Append("|A").Append(a);
            var cic = set.CICs[a];
            if (cic == null) sb.Append("|cic:-");
            else
            {
                sb.Append("|cic:").Append(cic.AllianceId).Append(',').Append(cic.OfficerName);
                var plan = cic.ActivePlan;
                if (plan != null)
                {
                    sb.Append(',').Append(plan.OperationId).Append(',').Append(plan.CurrentPhaseIndex).Append(',').Append(plan.IsDirty)
                      .Append(',').Append(plan.OperationLastDecisionDaySerial);
                    foreach (var p in plan.Phases) sb.Append(",p").Append(p.TargetObjectiveId).Append('@').Append(p.PhaseStartedDaySerial);
                }
            }
            sb.Append("|era:").Append(set.Eras[a]?.Stage.ToString() ?? "-");
            sb.Append("|front:").Append(set.Fronts[a]?.Signature() ?? "-");
            sb.Append("|area:").Append(set.ArmyAreas[a]?.Summary() ?? "-");
            var f = set.FormationDirectives[a];
            sb.Append("|form:").Append(f == null ? "-" : f.Summary() + "/" + f.Pressure.LowSupplyCount + "/" + f.Pressure.MassCount + "/" + f.Pressure.RecoverCount + "/" + f.Pressure.TopSupplyAreaKey);
            sb.Append("|fiscal:").Append(set.FiscalIntents[a]?.Signature ?? "-");
            sb.Append("|cons:").Append(set.ConstructionIntents[a]?.Signature ?? "-");
            sb.Append("|def:").Append(set.DefenseIntents[a]?.Signature ?? "-");
            sb.Append("|need:").Append(set.Needs[a] == null ? "-" : string.Join(",", set.Needs[a].ToArray()));
            sb.Append("|snap:").Append(set.NeedSnapshots[a]?.AnnualBalance.ToString() ?? "-");
            sb.Append("|policy:").Append(set.PolicyPlans[a].CurrentPick).Append(',').Append(set.PolicyPlans[a].DescribeTop(10));
            var m = set.DirectorMemories[a];
            sb.Append("|mem:").Append(m.LastPosture?.Pace.ToString() ?? "-").Append(',').Append(m.LastPosture?.Reason ?? "-")
              .Append(',').Append(m.CapitalDangerStreakDays).Append(',').Append(m.LastFullRefreshDay)
              .Append(',').Append(string.Join(";", m.RecentEventSummaries));
        }
        sb.Append("|map:").Append(set.CampaignMap?.Signature ?? "-");
        return sb.ToString();
    }

    // ---- coordinator / patch wiring (runtime-only, pinned from source) -----------------------

    private static string Coordinator() => TestFixtures.ReadRepoFile("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");

    private static string Body(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        AssertTrue(start >= 0, "found " + signature);
        int open = source.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        throw new InvalidOperationException("unbalanced " + signature);
    }

    private static void AssertBefore(string text, string first, string second, string label)
    {
        int i = text.IndexOf(first, StringComparison.Ordinal);
        int j = text.IndexOf(second, StringComparison.Ordinal);
        AssertTrue(i >= 0 && j >= 0 && i < j, label);
    }

    private static void CoordinatorRoutesThroughView()
    {
        string src = Coordinator();
        AssertContains(src, "private StrategicLedgerSet View => _review.RunningJob ? _work : _published;", "view rule");
        string[] fields =
        {
            "CIC[] CICs", "EraStageManager[] Eras", "FrontSectorLedger[] Fronts", "ArmyAreaLedger[] ArmyAreas",
            "FormationDirectiveLedger[] FormationDirectives", "FiscalOutput[] FiscalIntents", "ConstructionOutput[] ConstructionIntents",
            "DefenseIntentLedgerOutput[] DefenseIntents", "NeedVector[] Needs", "NeedSnapshot[] NeedSnapshots",
            "PolicyPlanLedger[] PolicyPlans", "DirectorMemory[] DirectorMemories", "CampaignMapLedger CampaignMap"
        };
        foreach (var field in fields)
        {
            string name = field.Substring(field.LastIndexOf(' ') + 1);
            AssertContains(src, field + " => View." + name + ";", name + " goes through the view");
        }
        string commit = Body(src, "private void CommitReviewDay(");
        AssertContains(commit, "ReviewDayCommit.Run(() => PublishAndReportReviewDay(d), ApplyDeferredBattleOutcomes);", "publish, then deferred battles in a finally");
        AssertContains(Body(src, "private void PublishAndReportReviewDay("), "PublishStrategicState();", "the day is published on commit");
        AssertContains(Body(src, "private void ApplyDeferredBattleOutcomes("), "RecordBattleOutcome(deferred[i]);", "deferred battles recorded");
        string publish = Body(src, "private void PublishStrategicState(");
        AssertContains(publish, "ReviewDayCommit.Publish(ref _published, _work, PrepareDay);", "published replaced by a complete copy");
        AssertContains(Body(src, "private static StrategicLedgerSet PrepareDay("), "StrategicLedgerSet.PublishedCopyOf(work);", "the ledger copy");
        AssertEqual(1, src.Split(new[] { "_published = " }, StringSplitOptions.None).Length - 1, "published assigned only at its declaration");
        AssertContains(src, "private StrategicLedgerSet _published = new StrategicLedgerSet();", "declaration");
    }

    private static void CoordinatorDrainAndResetHooks()
    {
        string src = Coordinator();
        AssertBefore(Body(src, "public void NotifyDateAdvanced("), "DrainDailyReview();", "InitializeFromGameState();", "old day drains before the new one");
        AssertBefore(Body(src, "public void SaveSidecar("), "DrainDailyReviewBeforeSave();", "BuildSnapshot()", "sidecar save drains first");
        string beforeSave = Body(src, "internal void DrainDailyReviewBeforeSave(");
        AssertBefore(beforeSave, "if (operation != 2 && operation != 4) return;", "DrainDailyReview();", "only campaign saves drain");
        string reset = Body(src, "public void ResetAll(");
        AssertContains(reset, "_review.Clear();", "reset abandons the in-flight day");
        AssertContains(reset, "_deferredBattleOutcomes.Clear();", "reset drops deferred battles");
        AssertBefore(reset, "PublishStrategicState();", "CampaignScope.ResetAll();", "reset publishes the empty state");
        AssertContains(Body(src, "private void ApplySnapshot("), "PublishStrategicState();", "a loaded sidecar is published");
        AssertContains(Body(src, "public void InitializeFromGameState("), "PublishStrategicState();", "init is published");
        string record = Body(src, "internal void RecordBattleOutcome(");
        AssertBefore(record, "if (_review.InFlight)", "BattleHistory.Add(record);", "mid-review battles are deferred");
        string budget = Body(src, "private static long ReviewSliceBudgetTicks(");
        AssertContains(budget, "!ConfigValue(plugin.TimeSliceDailyReview)) return long.MaxValue;", "flag off runs the whole day");
        AssertContains(budget, "budgetMs <= 0f", "budget guarded");
        string job = Body(src, "private void RunReviewJob(");
        AssertContains(job, "DailyReviewJobList.Ordered[jobIndex]", "jobs come from the ordered list");
    }

    private static StrategicLedgerSet WorkingWithCic(string officer)
    {
        var working = new StrategicLedgerSet();
        working.CICs[0] = new CIC { AllianceId = 0, OfficerName = officer };
        working.Eras[0] = new EraStageManager { Stage = EraStage.Amateur1861 };
        return working;
    }

    private static void CommitKeepsPreviousDayWhenCopyThrows()
    {
        StrategicLedgerSet published = StrategicLedgerSet.PublishedCopyOf(WorkingWithCic("Monday"));
        StrategicLedgerSet monday = published;
        var tuesday = WorkingWithCic("Tuesday");
        bool threw = false;
        try
        {
            // PublishFrom runs on the new set, then the copy fails.
            ReviewDayCommit.Publish(ref published, tuesday, w =>
            {
                var next = new StrategicLedgerSet();
                next.PublishFrom(w);
                throw new InvalidOperationException("copy failed");
            });
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        AssertTrue(threw, "the failure reaches the caller");
        AssertTrue(ReferenceEquals(monday, published), "previous day still published");
        AssertEqual("Monday", published.CICs[0].OfficerName, "previous day whole");

        ReviewDayCommit.Publish(ref published, tuesday, StrategicLedgerSet.PublishedCopyOf);
        AssertFalse(ReferenceEquals(monday, published), "a completed copy replaces the set");
        AssertEqual("Tuesday", published.CICs[0].OfficerName, "new day published");
        AssertEqual("Monday", monday.CICs[0].OfficerName, "a replaced set is never written again");
        AssertFalse(ReferenceEquals(tuesday.CICs[0], published.CICs[0]), "published CIC is a copy");
    }

    private static void CommitAppliesDeferredWhenPublishThrows()
    {
        int applied = 0;
        bool threw = false;
        try
        {
            ReviewDayCommit.Run(() => throw new InvalidOperationException("publish failed"), () => applied++);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        AssertTrue(threw, "the failure reaches the caller (the pump logs it)");
        AssertEqual(1, applied, "deferred battles applied anyway");
        var order = new List<string>();
        ReviewDayCommit.Run(() => order.Add("publish"), () => order.Add("deferred"));
        AssertEqual("publish,deferred", string.Join(",", order), "deferred battles after the publish");
    }

    private static void CommitKeepsBothFailures()
    {
        var publish = new InvalidOperationException("copy failed");
        var deferred = new ArgumentException("battle record failed");
        AggregateException both = null;
        try
        {
            ReviewDayCommit.Run(() => throw publish, () => throw deferred);
        }
        catch (AggregateException ex)
        {
            both = ex;
        }
        AssertTrue(both != null, "one exception carries both failures");
        AssertEqual(2, both.InnerExceptions.Count, "two inner failures");
        AssertTrue(ReferenceEquals(publish, both.InnerExceptions[0]), "the publish failure first");
        AssertTrue(ReferenceEquals(deferred, both.InnerExceptions[1]), "the deferred failure second");
        AssertContains(both.Message, "copy failed", "the logged message names the publish failure");
        AssertContains(both.Message, "battle record failed", "the logged message names the deferred failure");

        Exception only = null;
        try
        {
            ReviewDayCommit.Run(() => { }, () => throw deferred);
        }
        catch (Exception ex)
        {
            only = ex;
        }
        AssertTrue(ReferenceEquals(deferred, only), "a deferred failure alone propagates as itself");

        only = null;
        try
        {
            ReviewDayCommit.Run(() => throw publish, () => { });
        }
        catch (Exception ex)
        {
            only = ex;
        }
        AssertTrue(ReferenceEquals(publish, only), "a publish failure alone propagates as itself");
    }

    // The coordinator's job bodies hand each other the single-frame review's locals; these
    // are the hand-offs (verify round 1, A1). Order is pinned by JobListOrder.
    private static void JobHandOffsPinned()
    {
        string job = System.Text.RegularExpressions.Regex.Replace(Body(Coordinator(), "private void RunReviewJob("), @"\s+", " ");
        string[] pins =
        {
            "UpdateCampaignMapLedger(d.Day, d.LogHeartbeat, d.WeeklyCatchUp)",
            "UpdateFiscalIntent(alliance, a.Era.Stage, d.Day, d.Month, d.Year, d.LogHeartbeat, publish: false)",
            "UpdateFiscalIntent(alliance, a.Era.Stage, d.Day, d.Month, d.Year, d.LogHeartbeat, publish: true)",
            "UpdatePolicyPlan(alliance, a.Era.Stage, d.CalendarDay)",
            "CampaignPlannerRuntime.ReviewPlan(alliance, d.Day, d.Month, d.Year, _work.DirectorMemories[alliance]?.LastPosture)",
            "case DailyReviewJobKind.Execute: if (ready && !paused) d.PlannerMs += Measure(() => CampaignPlannerRuntime.ReviewExecute(alliance));",
            "case DailyReviewJobKind.Front: { a.FrontChanged = false; if (paused) break;",
            "case DailyReviewJobKind.Defense: if (!paused)",
            "if (!ready || paused) break;",
            "case DailyReviewJobKind.Probe: if (ready && !paused)",
            "case DailyReviewJobKind.ArmyArea: if (ready && !paused)",
            "EnemyDetectionRuntime.StampReviewDay(d.CalendarDay);",
            "frontChanged = UpdateFrontLedger(alliance, a.Cic)",
            "if (frontChanged || _frontChangedDay[alliance] < 0) _frontChangedDay[alliance] = a.LegacySerial;",
            "a.FrontChanged = frontChanged;",
            "UpdateDefenseIntent(alliance, a.Era.Stage, d.Day, d.Month, d.Year)",
            "a.FormationChanged = false;",
            "StrategicCadencePolicy.ShouldRunAlternatingByAlliance(d.Day, alliance, d.AlternatingCatchUp) || StrategicCadencePolicy.SourceChanged(formationSource, _formationSourceSignatures[alliance]) || a.FrontChanged;",
            "a.FormationChanged = UpdateFormationDirectiveLedger(alliance, a.Cic, a.Era)",
            "if (UpdateOperationalProbe(alliance, a.Cic, d.Day, d.Month, d.Year)) a.FormationChanged = true;",
            "StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(d.Day, armyAreaSource, _armyAreaSourceSignatures[alliance], a.FormationChanged || d.WeeklyCatchUp)",
            "UpdateArmyAreaLedger(alliance, a.Cic)",
            "StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(d.Day, constructionSource, _constructionSourceSignatures[alliance], d.WeeklyCatchUp)",
            "UpdateConstructionIntent(alliance, a.Era.Stage, d.LogHeartbeat, paused)",
        };
        foreach (string pin in pins)
            AssertContains(job, pin, "hand-off: " + pin);
        AssertBefore(job, "a.FormationChanged = false;", "a.FormationChanged = UpdateFormationDirectiveLedger(", "formation flag reset before the ledger runs");
        AssertBefore(job, "EnemyDetectionRuntime.StampReviewDay(d.CalendarDay);", "if (d.OperationalRuntimeReady)", "the belief day is stamped by the first job, before any reader");
        string cic = System.Text.RegularExpressions.Regex.Replace(Body(Coordinator(), "private void RunCicReviewJob("), @"\s+", " ");
        foreach (string pin in new[] { "a.Mode = AllianceReviewMode.Ai;", "a.Era = era;", "a.Cic = cic;", "a.LegacySerial = legacySerial;", "a.Mode = AllianceReviewMode.PlayerCic;", "a.Mode = AllianceReviewMode.Skipped;",
            "if (!d.JobMachinePaused) { var fired = Succession.CheckEvents(" })
            AssertContains(cic, pin, "cic hand-off: " + pin);
        // The planner reads today's director posture (CIC job) and runs before the front and its
        // executors after the defense ledger, as in the single-frame review.
        string list = string.Join(",", Array.ConvertAll(DailyReviewJobList.Ordered, j => j.Kind + ":" + j.Alliance));
        AssertBefore(list, "Cic:0", "Plan:0", "plan after today's CIC review");
        AssertBefore(list, "Policy:0", "Plan:0", "plan after policy");
        AssertBefore(list, "Plan:0", "Front:0", "plan before the front ledger");
        AssertBefore(list, "Defense:0", "Execute:0", "executors after the defense ledger");
        AssertBefore(list, "Execute:0", "Formation:0", "executors before the formation directives");
    }

    // The planner's reader-facing copies (beliefs, plan store): a fill that throws keeps the
    // previous day, and a swap only follows a complete fill.
    private static void PublishedDaySwapsOnlyCompleteCopy()
    {
        var day = new PublishedDay<List<string>>(new List<string> { "monday" }, new List<string>());
        day.Prepare(c => { c.Clear(); c.Add("tuesday"); });
        AssertEqual("monday", day.Published[0], "prepared, not yet swapped");
        day.Swap();
        AssertEqual("tuesday", day.Published[0], "swapped");
        bool threw = false;
        try { day.Prepare(c => { c.Clear(); c.Add("half"); throw new InvalidOperationException("copy failed"); }); }
        catch (InvalidOperationException) { threw = true; }
        AssertTrue(threw, "the fill's exception reaches the commit");
        day.Swap();
        AssertEqual("tuesday", day.Published[0], "a throwing copy keeps the previous day");
        day.Swap();
        AssertEqual("tuesday", day.Published[0], "no swap without a complete prepare");
        day.Reset(c => c.Clear());
        AssertEqual(0, day.Published.Count, "reset clears");
    }

    // StrategicCoordinator.PublishStrategicState: every copy (ledgers, beliefs, plan store) is
    // made inside the ledger copy's function, so any throw leaves the whole previous day
    // published; the reference swaps follow only a complete set. The coordinator attaches the
    // stores' view to its scheduler.
    private static void CommitPreparesEveryCopyBeforeSwapping()
    {
        string src = Coordinator();
        string prepare = Body(src, "private static StrategicLedgerSet PrepareDay(");
        AssertBefore(prepare, "StrategicLedgerSet.PublishedCopyOf(work);", "EnemyBeliefStore.PreparePublish();", "ledgers, then beliefs");
        AssertBefore(prepare, "EnemyBeliefStore.PreparePublish();", "CampaignPlanStore.PreparePublish();", "beliefs, then the plan store");
        AssertBefore(prepare, "CampaignPlanStore.PreparePublish();", "return ledgers;", "all prepared before the ledger swap");
        string publish = Body(src, "private void PublishStrategicState(");
        AssertBefore(publish, "ReviewDayCommit.Publish(ref _published, _work, PrepareDay);", "EnemyBeliefStore.SwapPublished();", "stores swap after the ledgers");
        AssertBefore(publish, "EnemyBeliefStore.SwapPublished();", "CampaignPlanStore.SwapPublished();", "both stores swap");
        AssertContains(Body(src, "public static void Bootstrap("), "DailyReviewView.Attach(Instance.InReviewJob);", "stores follow the scheduler's view");
        AssertContains(Body(src, "private bool InReviewJob("), "return _review.RunningJob;", "the same rule as View");

        // A throwing copy, end to end on the stores: nothing of the new day is published.
        bool inJob = false;
        WhiskeyRealism.Util.CampaignScope.ResetAll();
        DailyReviewView.Attach(() => inJob);
        try
        {
            var plan = new TheaterPlan { Alliance = 0, DaySerial = 1 };
            inJob = true;
            CampaignPlanStore.Publish(0, plan, new Allocation(), null);
            inJob = false;
            StrategicLedgerSet published = new StrategicLedgerSet();
            bool threw = false;
            try
            {
                ReviewDayCommit.Publish(ref published, new StrategicLedgerSet(), w =>
                {
                    EnemyBeliefStore.PreparePublish();
                    CampaignPlanStore.PreparePublish();
                    throw new InvalidOperationException("ledger copy failed");
                });
                EnemyBeliefStore.SwapPublished();
                CampaignPlanStore.SwapPublished();
            }
            catch (InvalidOperationException) { threw = true; }
            AssertTrue(threw, "the copy threw");
            AssertFalse(CampaignPlanStore.TryGetPlan(0, out _), "the plan of a failed commit is not published");
        }
        finally
        {
            DailyReviewView.Attach(null);
            WhiskeyRealism.Util.CampaignScope.ResetAll();
        }
    }

    private static void PatchesPumpAndDrain()
    {
        string tick = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Patches/MonthlyTickHookPatch.cs");
        AssertBefore(tick, "if (!_startupGate.ShouldNotify(dateChanged, runtimeReady))", "StrategicCoordinator.Instance?.PumpDailyReview();", "frames without a date change pump");
        // The campaign planner's terrain drain is a client of the same frame pump: only on a
        // frame with no review day in flight.
        string pump = System.Text.RegularExpressions.Regex.Replace(Body(Coordinator(), "internal void PumpDailyReview("), @"\s+", " ");
        AssertContains(pump, "if (!_review.InFlight) { TerrainLineRuntime.DrainTick(); return; }", "drain only when no day is in flight");
        AssertBefore(tick, "CampaignIsLive(SceneManagement.currentoperation)", "PumpDailyReview()", "no pumping while the campaign is not live");
        string drain = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Patches/DailyReviewSaveDrainPatch.cs");
        AssertContains(drain, "[HarmonyPatch(typeof(SceneManagement), \"SaveAll\")]", "drain patch targets SaveAll");
        AssertContains(drain, "[HarmonyPrefix]", "drains before vanilla saves");
        AssertContains(drain, "internal static void Prefix()", "non-skipping prefix");
        AssertContains(drain, "StrategicCoordinator.Instance?.DrainDailyReviewBeforeSave();", "drains the review");
    }
}
