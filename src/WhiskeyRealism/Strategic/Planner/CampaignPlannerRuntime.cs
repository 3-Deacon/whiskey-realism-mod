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
    //                 G1 detection and beliefs -> (G2 profiles, G3 plan, G1b recon tasks, G4 allocation, G1b
    //                 recon mission sync) -> CampaignPlanStore.Publish.
    //   ReviewExecute the Execute job, after the defense job: G2 appointments (CommanderAppointmentRuntime, on
    //                 this review's snapshot and plan), then the G5b, G5a, G5c, G5d executors.
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
        // G1B SEAM: the recon tasks and the mission ledger come with G1b's runtime step (PlanTasks before the
        // allocation, ReconMissionLedger.Sync after it, plan G1b "Split"); until then no task is placed.
        private static readonly List<ReconTask> NoReconTasks = new List<ReconTask>();

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

                    var snapshot = CampaignSnapshotRuntime.Build(alliance, faction, ReviewCadenceCatchUp.DaySerial(day, month, year),
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
                    if (mode == PlannerReviewMode.Bookkeeping) return;

                    var sectors = SectorModel.Build(snapshot);
                    // G3 plan step (P3-P5): profiles -> plan with logistics and depot tasks.
                    var state = States[alliance];
                    if (state.CampaignSeed == 0L)
                        state.CampaignSeed = PlannerState.ComputeCampaignSeed(SceneManagement.currentcampaignscenario, year, month, day);
                    Battles.Take(alliance, snapshot.DaySerial, PendingBattles);
                    TheaterPlan plan;
                    string trace;
                    using (TelemetryPerf.Scope("campaign.planner.plan", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                    {
                        plan = TheaterPlanner.Plan(snapshot, sectors, id => CommanderProfiles.ForSector(snapshot.Commanders, id), state, PendingBattles,
                            TerrainLineRuntime.ForReview(), TerrainLineRuntime.ReadMarchSpeed(alliance), out trace);
                    }
                    // G4: the force allocation over the plan, published with it (the working copy that joins PrepareDay).
                    Allocation allocation;
                    float reliability = PlayerComplianceRuntime.Reliability;
                    using (TelemetryPerf.Scope("campaign.planner.allocate", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                    {
                        NonCompliance[alliance].Observe(snapshot.DaySerial, HasMapGroupFormation(snapshot), reliability);
                        allocation = ForceAllocator.Allocate(plan, snapshot, sectors, HomeTheaterPrior.ForAlliance(alliance), NoReconTasks, null);
                    }
                    CampaignPlanStore.Publish(alliance, plan, allocation, sectors);
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
                    if (!FlagOn(Plugin.EnableCampaignPlanner) || !FlagOn(Plugin.EnableCampaignPlannerExecutors)) return;
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
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-planner:execute-failed", "[CampaignPlanner] executors failed: " + ex.Message);
                }
            }
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
                " supply=" + s.Supply.Count +
                " rails=" + s.Rails.Count +
                " commanders=" + s.Commanders.Count +
                " crossings=" + s.Crossings.Count +
                " useRails=" + rails + " useRivers=" + rivers +
                " player=" + s.Player);
        }

        private static bool FlagOn(BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.Value; }
            catch { return false; }
        }
    }
}
