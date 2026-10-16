using System;
using System.Collections.Generic;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The planner's two calls per AI alliance in the daily strategic review (plan P1), from the Plan and
    // Execute jobs of StrategicCoordinator.RunReviewJob (time-sliced since the perf merge), each inside
    // `if (ready)` with the pause the day captured at its start (BeginReviewDay, spec C5):
    //   ReviewPlan    the Plan job, after the fiscal and policy jobs, before the front job: snapshot ->
    //                 G1 detection and beliefs -> G1b recon mission aging -> (G2 profiles, G3 plan, G1b recon
    //                 tasks, G4 allocation, G1b recon mission sync) -> CampaignPlanStore.Publish.
    //   ReviewExecute the Execute job, after the defense job: G2 appointments (CommanderAppointmentRuntime, on
    //                 this review's snapshot and plan), then the G5b, G5a, G5c, G5d executors. G5a's two run
    //                 behind the pre-war gate (PlannerChapterRules, decision 8): the steering executors
    //                 (objective, theater boxes) and the mover.
    //                 Executors write vanilla state only from here.
    // Both return for alliances other than 0 and 1 and before AICampaign.aifaction holds the
    // alliance's faction. For the W&L CIC player's own alliance (vanilla job 36 skips it,
    // 11620-11628) ReviewPlan keeps only the snapshot and the G1 detection/beliefs (bookkeeping,
    // integrator decision 2026-10-15) and ReviewExecute returns. While vanilla's job machine is
    // paused (UpdateUnitAI 11368: a W&L player with no command) every faction's jobs are frozen,
    // so ReviewExecute returns for every alliance and ReviewPlan keeps only its bookkeeping
    // (snapshot, G1 detection and beliefs, the appointment protection windows' aging; the other
    // ledger aging inserts before that return): no replan, no publish, and the last published
    // plan ages out (spec C5). The commander profiles are G3's step: TheaterPlanner.Plan reads
    // CommanderProfiles.ForSector.
    internal static class CampaignPlannerRuntime
    {
        private static readonly CampaignSnapshot[] Snapshots = { new CampaignSnapshot(), new CampaignSnapshot() };
        // G3: each alliance's hysteresis/seed state, the battles each review still has to judge
        // (P4) and the last [CampaignPlan] row (rows on change). Per campaign; G6 persists the state.
        private static readonly PlannerState[] States = { new PlannerState(), new PlannerState() };
        private static readonly BattleInbox Battles = new BattleInbox();
        private static readonly List<BattleHistoryRecord> PendingBattles = new List<BattleHistoryRecord>();
        private static readonly string[] LastPlanRow = new string[2];
        // G4: the allocation's [Allocation] row (rows on change) and the map-group player's run of
        // unreliable days (telemetry: the row reports it; the allocator never counts the player, C4).
        // Per campaign, in memory (G6 adds it to the sidecar's planner state).
        private static readonly string[] LastAllocationRow = new string[2];
        private static readonly ForceAllocator.PlayerNonCompliance[] NonCompliance =
            { new ForceAllocator.PlayerNonCompliance(), new ForceAllocator.PlayerNonCompliance() };
        // G1b: each alliance's recon missions and this review's tasks (pooled), the last [Recon] row's counts
        // (rows on change) and the directive ledger's Screen test (FormationDirectiveRuntime). Per campaign.
        // Everything that reads the ledger runs inside a review job (the Plan, Formation and Execute jobs), so
        // it does not join the atomic publish; a patch that reads it must (plan § G1b runtime as built).
        private static readonly ReconMissionLedger[] Recon = { new ReconMissionLedger(), new ReconMissionLedger() };
        private static readonly List<ReconTask>[] ReconTasks = { new List<ReconTask>(), new List<ReconTask>() };
        private static readonly ReconCounts[] LastReconCounts = new ReconCounts[2];
        private static readonly bool[] ReconRowEmitted = new bool[2];
        private static readonly Func<string, bool>[] ReconFilters = { unitKey => IsReconUnit(0, unitKey), unitKey => IsReconUnit(1, unitKey) };

        static CampaignPlannerRuntime()
        {
            CampaignScope.Register("campaign-planner-plan-state", ResetPlanState);
        }

        private static void ResetPlanState()
        {
            for (int a = 0; a < States.Length; a++)
            {
                States[a] = new PlannerState();
                LastPlanRow[a] = null;
                LastAllocationRow[a] = null;
                NonCompliance[a].Reset();
                Recon[a] = new ReconMissionLedger();
                ReconTasks[a].Clear();
                LastReconCounts[a] = default(ReconCounts);
                ReconRowEmitted[a] = false;
            }
            Battles.Clear();
            PendingBattles.Clear();
        }

        internal static void ReviewPlan(int alliance, int day, int month, int year, DirectorPosture tempo, bool jobMachinePaused)
        {
            using (TelemetryPerf.Scope("campaign.planner.review", TelemetryLayer.Campaign, TelemetryCategory.Performance, 4.0))
            {
                try
                {
                    if (!FlagOn(Plugin.EnableCampaignPlanner) || alliance < 0 || alliance >= Snapshots.Length) return;
                    var chain = WlChainContext.Capture();
                    PlayerCommandClass player = ReadPlayerClass(in chain);
                    var mode = PlayerCommandClassifier.ReviewMode(player, GameVars.playeralliance, alliance, jobMachinePaused);
                    int faction = FactionIndexOf(alliance);
                    if (faction < 0) return;
                    int daySerial = ReviewCadenceCatchUp.DaySerial(day, month, year);
                    // The clock the patches test a published plan's age against (PlanFreshness): noted in every mode, so a
                    // paused alliance's plan ages out (spec C5).
                    PlanFreshness.NoteToday(daySerial);

                    var snapshot = CampaignSnapshotRuntime.Build(alliance, faction, daySerial,
                        player, in chain, tempo, Snapshots[alliance]);

                    // G1: fog-honest detection and the belief update. Bookkeeping: it runs in every
                    // mode, for a W&L CIC player's stand-down alliance and during the pause (no writes).
                    EnemyDetectionRuntime.Detect(snapshot, GameVars.playeralliance, EnemyDetectionRuntime.FogHonestFlagsOn());
                    var beliefs = EnemyDetectionRuntime.ModelFor(alliance);
                    beliefs.Update(snapshot.Detections, snapshot.DaySerial, EnemyDetectionRuntime.DriftFor(alliance), EnemyDetectionRuntime.MarchFor());
                    snapshot.Beliefs.AddRange(beliefs.Beliefs);
                    EnemyDetectionRuntime.Report(snapshot, beliefs);
                    if (mode == PlannerReviewMode.StandDown) return;
                    CommanderAppointmentRuntime.Age(alliance, snapshot.DaySerial);
                    TerrainLineRuntime.PrepareDrain(snapshot);
                    AgeRecon(snapshot);
                    if (mode == PlannerReviewMode.Bookkeeping) return;

                    var sectors = SectorModel.Build(snapshot);
                    // G3 plan step (P3-P5): profiles -> plan with logistics and depot tasks.
                    var state = States[alliance];
                    if (state.CampaignSeed == 0L)
                        state.CampaignSeed = PlannerState.ComputeCampaignSeed(SceneManagement.currentcampaignscenario, year, month, day);
                    Battles.Take(alliance, snapshot.DaySerial, PendingBattles);
                    // P8: the moves the tracker gave up on (not yet lapsed). G3 leaves a formation out of the mass of the objective
                    // it gave up on, G4 takes the role from a formation that gave up on the target it would send it to.
                    var gaveUp = new List<GaveUpMove>();
                    MoveTrackers.For(alliance)?.CollectGaveUp(gaveUp, snapshot.DaySerial);
                    var gaveUpObjectives = new List<GaveUpObjective>();
                    if (gaveUp.Count > 0 && CampaignPlanStore.TryGetPlan(alliance, out var previousPlan)) GaveUpObjectives.Resolve(gaveUp, previousPlan, gaveUpObjectives);
                    TheaterPlan plan;
                    string trace;
                    using (TelemetryPerf.Scope("campaign.planner.plan", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                    {
                        plan = TheaterPlanner.Plan(snapshot, sectors, id => CommanderProfiles.ForSector(snapshot.Commanders, id), state, PendingBattles,
                            TerrainLineRuntime.ForReview(), TerrainLineRuntime.ReadMarchSpeed(alliance), out trace, gaveUpObjectives);
                    }
                    // G1b: the recon tasks for this plan (probe verdicts on the battles it judged, then the tasks);
                    // G4's allocation below picks their formations and the sync binds the missions.
                    PlanRecon(snapshot, sectors, plan, year, month, day);
                    // G4: the force allocation over the plan, published with it (the working copy that joins PrepareDay).
                    Allocation allocation;
                    float reliability = PlayerComplianceRuntime.Reliability;
                    using (TelemetryPerf.Scope("campaign.planner.allocate", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                    {
                        NonCompliance[alliance].Observe(snapshot.DaySerial, HasMapGroupFormation(snapshot), reliability);
                        allocation = ForceAllocator.Allocate(plan, snapshot, sectors, HomeTheaterPrior.ForAlliance(alliance), ReconTasks[alliance], Recon[alliance], gaveUp);
                    }
                    SyncRecon(snapshot, allocation);
                    CampaignPlanStore.Publish(alliance, plan, allocation, sectors);
                    CampaignPlanStore.PublishExecutorContext(alliance, ExecutorContextOf(alliance, snapshot));
                    LogFirstReview(snapshot, sectors);
                    EmitPlan(plan, trace, day, month, year);
                    EmitAllocation(allocation, plan, snapshot.Player, reliability, NonCompliance[alliance].Days);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-planner:review-failed", "[CampaignPlanner] review failed, nothing published: " + ex.Message);
                }
            }
        }

        internal static void ReviewExecute(int alliance, bool jobMachinePaused)
        {
            using (TelemetryPerf.Scope("campaign.planner.execute", TelemetryLayer.Campaign, TelemetryCategory.Performance, 4.0))
            {
                try
                {
                    if (!ExecutorsEnabled()) return;
                    if (alliance < 0 || alliance >= Snapshots.Length) return;
                    var chain = WlChainContext.Capture();
                    // The day's captured pause decides; the live predicate is read again as defence in depth, so an
                    // executor never writes while either says vanilla's job machine is frozen.
                    var live = ReadPlayerClass(in chain);
                    if (PlayerCommandClassifier.ReviewMode(live, GameVars.playeralliance, alliance, jobMachinePaused) != PlannerReviewMode.Full) return;
                    if (PlayerCommandClassifier.ReviewMode(live, GameVars.playeralliance, alliance) != PlannerReviewMode.Full) return;
                    if (FactionIndexOf(alliance) < 0) return;
                    // Executors in plan order: G2 appointments, then G5b, G5a, G5c, G5d.
                    CommanderAppointmentRuntime.Execute(alliance, Snapshots[alliance]);
                    // G5a pre-war gate (decision 8): vanilla refuses every campaign move before scenario 001's war starts
                    // (SetWaypoint 91366), so no move, order or steering executor runs, and the withhold view and the pointer
                    // redirect read the same rule. Appointments move no unit and ran above.
                    if (!PlannerChapterRules.ExecutorsAllowed(GamePrefs.leveltoload, Policy.CurrentChapter))
                    {
                        OnceLog.Info("campaign-planner:pre-war-idle", "[CampaignPlanner] scenario 001 pre-war (chapter " + Policy.CurrentChapter + "): executors idle until the war starts");
                        return;
                    }
                    // Today's plan and today's snapshot only: a Plan job that failed before publishing leaves yesterday's.
                    if (!CampaignPlanStore.TryGetPlan(alliance, out var todaysPlan) || !PlanFreshness.IsToday(todaysPlan.DaySerial) || !PlanFreshness.IsToday(Snapshots[alliance].DaySerial))
                    {
                        OnceLog.Warning("campaign-planner:execute-no-todays-plan", "[CampaignPlanner] no plan was built today: executors idle for this review");
                        return;
                    }
                    CampaignSteeringRuntime.Execute(alliance, Snapshots[alliance]);
                    CampaignMoverRuntime.Execute(alliance, Snapshots[alliance], States[alliance]);
                    // The mover may have released the decision's launch: the patches read it with the plan.
                    CampaignPlanStore.PublishExecutorContext(alliance, ExecutorContextOf(alliance, Snapshots[alliance]));
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-planner:execute-failed", "[CampaignPlanner] executors failed: " + ex.Message);
                }
            }
        }

        // G1b recon mission aging (plan § G1b, ReconMissionLedger.OnReview): the missions' transitions from
        // today's snapshot, before the plan. Bookkeeping, so it also runs on a paused day and while the flag
        // is off (missions then drain: the sync below clears what no task holds). The recall point reads the
        // last allocation, which inside the Plan job is the previous review's (null on the first day).
        private static void AgeRecon(CampaignSnapshot snapshot)
        {
            using (TelemetryPerf.Scope("campaign.planner.recon", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                CampaignPlanStore.TryGetAllocation(snapshot.Alliance, out var previous);
                Recon[snapshot.Alliance].OnReview(snapshot, previous);
            }
        }

        // G1b recon tasks for this review's plan (before the allocation that picks their formations).
        // Flag off: no tasks, and SyncRecon then clears whatever missions stand.
        private static void PlanRecon(CampaignSnapshot snapshot, SectorModel sectors, TheaterPlan plan, int year, int month, int day)
        {
            int alliance = snapshot.Alliance;
            var tasks = ReconTasks[alliance];
            using (TelemetryPerf.Scope("campaign.planner.recon", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                if (!FlagOn(Plugin.EnableCampaignReconnaissance)) { tasks.Clear(); return; }
                ReconStep.Plan(snapshot, sectors, plan, PendingBattles, BattleHistoryQuery.LegacySerial(year, month, day), Recon[alliance], tasks);
            }
        }

        // G1b: binds the allocation's Recon assignments to missions (writing each mission id back into its
        // assignment) before the allocation is published, then the [Recon] row.
        private static void SyncRecon(CampaignSnapshot snapshot, Allocation allocation)
        {
            using (TelemetryPerf.Scope("campaign.planner.recon", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                Recon[snapshot.Alliance].Sync(snapshot, ReconTasks[snapshot.Alliance], allocation);
                if (FlagOn(Plugin.EnableCampaignReconnaissance)) EmitRecon(snapshot);
            }
        }

        // [Recon] on change of its counts (one row per alliance per change; no string otherwise).
        private static void EmitRecon(CampaignSnapshot snapshot)
        {
            int a = snapshot.Alliance;
            var counts = ReconStep.CountsOf(snapshot, Recon[a], ReconTasks[a]);
            if (ReconRowEmitted[a] && counts.Equals(LastReconCounts[a])) return;
            ReconRowEmitted[a] = true;
            LastReconCounts[a] = counts;
            TelemetryRouter.LegacyInfo(counts.Row(a), TelemetryLayer.Campaign);
        }

        // The directive ledger's Screen test (FormationDirectiveLedger.Build's onReconMission), one delegate per
        // alliance built once. Read from the Formation job, after today's Plan job rebuilt the snapshot.
        internal static Func<string, bool> ReconFilterFor(int alliance)
        {
            return alliance >= 0 && alliance < ReconFilters.Length ? ReconFilters[alliance] : null;
        }

        private static bool IsReconUnit(int alliance, string unitKey)
        {
            return FlagOn(Plugin.EnableCampaignReconnaissance) && ReconStep.IsReconKey(Snapshots[alliance], Recon[alliance], unitKey);
        }

        // A finished battle (StrategicCoordinator.RecordBattleOutcome, after BattleHistory.Add): both
        // alliances judge it at their next plan (P4's decisive-battle trigger).
        internal static void OnBattleRecorded(BattleHistoryRecord record)
        {
            Battles.Add(record);
        }

        // [CampaignPlan] on change (one row per alliance per decision); the per-candidate trace
        // under Plan Trace Logging (CicSettings.PlanTrace until G6 moves it to the planner).
        private static void EmitPlan(TheaterPlan plan, string trace, int day, int month, int year)
        {
            string row = TheaterPlanner.Summary(plan);
            int a = plan.Alliance;
            if (a < 0 || a >= LastPlanRow.Length || string.Equals(LastPlanRow[a], row, StringComparison.Ordinal)) return;
            LastPlanRow[a] = row;
            TelemetryRouter.LegacyInfo(row, TelemetryLayer.Campaign);
            if (!CicSettings.PlanTrace) return;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Trace, "CampaignPlan:trace", TelemetrySeverity.Info, ev => ev
                .WithAlliance(a)
                .WithDecision("trace", plan.SwitchReason ?? "none", row)
                .WithField("date", year.ToString("D4") + "-" + month.ToString("D2") + "-" + day.ToString("D2"))
                .WithField("candidates", trace ?? ""));
        }

        // A map-group player (stage F's PlayerMapGroup) stands in this alliance's formations only on the
        // player's own side: the run of unreliable days belongs to that alliance.
        private static bool HasMapGroupFormation(CampaignSnapshot s)
        {
            for (int i = 0; i < s.Own.Count; i++)
                if (s.Own[i].Chain == WlChainClass.PlayerMapGroup) return true;
            return false;
        }

        // [Allocation] on change of its text (one row per alliance per change of roles or needs).
        private static void EmitAllocation(Allocation allocation, TheaterPlan plan, PlayerCommandClass player, float reliability, int unreliableDays)
        {
            int a = allocation.Alliance;
            if (a < 0 || a >= LastAllocationRow.Length) return;
            string row = ForceAllocator.Summary(allocation, plan, player, reliability, unreliableDays);
            if (string.Equals(LastAllocationRow[a], row, StringComparison.Ordinal)) return;
            LastAllocationRow[a] = row;
            TelemetryRouter.LegacyInfo(row, TelemetryLayer.Campaign);
        }

        // DLC_WL reads as WlChainContext.Capture does: dlc_scenarioactive, IsCommanderInChief
        // (47475), GetPlayerCommandHierarchy (44397-44427), the chosen commander's currentcommand
        // and the map-group test GetCampaignGroup(cmd) == cmd (8271).
        private static PlayerCommandClass ReadPlayerClass(in WlChainContext chain)
        {
            if (!chain.WlActive) return PlayerCommandClass.NotCareer;
            return PlayerCommandClassifier.Classify(true, DLC_WL.dlc_chosencommander, chain.PlayerIsCic,
                DLC_WL.GetPlayerCommandHierarchy(), (UnityEngine.Object)chain.Command != null, chain.CommandIsCampaignGroup);
        }

        // AIFaction.GetAIFactionIDFromAllianceID (10413): faction index != alliance id.
        private static int FactionIndexOf(int alliance)
        {
            for (int i = 0; ; i++)
            {
                if (AICampaignReflect.GetFaction(i) == null) return -1;
                if (AICampaignReflect.GetAllianceId(i) == alliance) return i;
            }
        }

        // The transport-mode counts are the smoke proof of G0's W&L-side finding: vanilla gives
        // rails and rivers to non-player alliances only (112862-112867).
        private static void LogFirstReview(CampaignSnapshot s, SectorModel sectors)
        {
            int rails = 0, rivers = 0;
            for (int i = 0; i < s.Own.Count; i++)
            {
                if (s.Own[i].UseRails) rails++;
                if (s.Own[i].UseRivers) rivers++;
            }
            OnceLog.Info(s.Alliance == 0 ? "campaign-planner:first-review:0" : "campaign-planner:first-review:1",
                "[CampaignPlanner] first review alliance=" + s.Alliance +
                " formations=" + s.Own.Count +
                " areas=" + s.Areas.Count +
                " sectors=" + sectors.Count +
                " objectives=" + s.Objectives.Count +
                " mapped=" + s.MappedObjectiveCount +
                " supply=" + s.Supply.Count +
                " rails=" + s.Rails.Count +
                " commanders=" + s.Commanders.Count +
                " crossings=" + s.Crossings.Count +
                " useRails=" + rails + " useRivers=" + rivers +
                " player=" + s.Player);
        }

        // Both planner flags: the planner runs and its executors may write. PickCampaignObjectivePatch (P10) and the
        // availability and importance patches stand down or read the plan only while this holds.
        internal static bool ExecutorsEnabled()
        {
            return FlagOn(Plugin.EnableCampaignPlanner) && FlagOn(Plugin.EnableCampaignPlannerExecutors);
        }

        // What the executors' patches read with the plan (G5a): the decision and its release, the pre-war gate and the map-group
        // player's formation (the deferred-Support clause never withholds him).
        private static ExecutorContext ExecutorContextOf(int alliance, CampaignSnapshot snapshot)
        {
            int mapGroup = -1;
            for (int i = 0; i < snapshot.Own.Count; i++)
                if (snapshot.Own[i].Chain == WlChainClass.PlayerMapGroup) mapGroup = snapshot.Own[i].InstanceId;
            return ExecutorContext.From(States[alliance], PlannerChapterRules.ExecutorsAllowed(GamePrefs.leveltoload, Policy.CurrentChapter), mapGroup);
        }

        private static bool FlagOn(BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.Value; }
            catch { return false; }
        }
    }
}
