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
        // (P4) and the last [CampaignPlan] row (rows on change). Per campaign; the sidecar carries the state (Export/Restore).
        private static readonly PlannerState[] States = { new PlannerState(), new PlannerState() };
        private static readonly BattleInbox Battles = new BattleInbox();
        private static readonly List<BattleHistoryRecord> PendingBattles = new List<BattleHistoryRecord>();
        private static readonly string[] LastPlanRow = new string[2];
        // G4: the allocation's [Allocation] row (rows on change) and the map-group player's run of
        // unreliable days (telemetry: the row reports it; the allocator never counts the player, C4).
        // Per campaign; the sidecar carries the run (Export/Restore).
        private static readonly string[] LastAllocationRow = new string[2];
        // Foresight F3: the [EnemyPicture] and [CapitalThreat] rows' last signatures (rows on change). Per campaign.
        private static readonly string[] LastPictureRow = new string[2];
        private static readonly string[] LastCapitalThreatRow = new string[2];
        // Foresight F6b-1: the [CoveringCorridor] row's last signature (rows on the first review and on change). Per campaign.
        private static readonly string[] LastCorridorRow = new string[2];
        // Foresight F6b-2: the [CoveringForce] row's last signature (rows on change). Per campaign.
        private static readonly string[] LastCoveringRow = new string[2];
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
        // Sidecar (v3): the saved own-unit records (moves, depot tasks, recon missions) wait here, as unit keys, until the first
        // review after a load has a snapshot to rebind them against (PlannerRebind); a save in between writes them back.
        private static readonly PlannerSnapshot[] PendingRebind = new PlannerSnapshot[2];
        private static readonly List<MoveRecord> ReboundMoves = new List<MoveRecord>();
        private static readonly List<DepotTaskRecord> ReboundDepotTasks = new List<DepotTaskRecord>();
        private static readonly List<ReconMission> ReboundMissions = new List<ReconMission>();
        // D6 detail: the allocator's home-department lookup, one delegate per alliance (no closure per review); States[a] is replaced on a
        // campaign reset or a load, so the delegate reads it at call time.
        private static readonly Func<string, Theater>[] DepartmentLookups = { key => States[0].DepartmentOf(key), key => States[1].DepartmentOf(key) };
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
                LastPictureRow[a] = null;
                LastCapitalThreatRow[a] = null;
                LastCorridorRow[a] = null;
                LastCoveringRow[a] = null;
                NonCompliance[a].Reset();
                Recon[a] = new ReconMissionLedger();
                ReconTasks[a].Clear();
                LastReconCounts[a] = default(ReconCounts);
                ReconRowEmitted[a] = false;
            }
            Battles.Clear();
            PendingBattles.Clear();
            for (int a = 0; a < PendingRebind.Length; a++) PendingRebind[a] = null;
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
                    RebindSaved(alliance, snapshot);

                    // The campaign seed is fixed on a campaign's first review, before Detect: the AI side's spotting-mark stream is seeded from it
                    // (foresight F4), in every mode.
                    var state = States[alliance];
                    if (state.CampaignSeed == 0L)
                        state.CampaignSeed = PlannerState.ComputeCampaignSeed(SceneManagement.currentcampaignscenario, year, month, day);
                    // G1: fog-honest detection and the belief update. Bookkeeping: it runs in every
                    // mode, for a W&L CIC player's stand-down alliance and during the pause (no writes).
                    EnemyDetectionRuntime.Detect(snapshot, GameVars.playeralliance, EnemyDetectionRuntime.FogHonestFlagsOn(), States[alliance].CampaignSeed);
                    var beliefs = EnemyDetectionRuntime.ModelFor(alliance);
                    beliefs.Update(snapshot.Detections, snapshot.DaySerial, EnemyDetectionRuntime.DriftFor(alliance), EnemyDetectionRuntime.MarchFor());
                    snapshot.DetectionsSeen = DetectionRules.SeenCount(snapshot.Detections);
                    snapshot.DetectionsMarked = beliefs.AppliedMarkKeys.Count;
                    snapshot.Beliefs.AddRange(beliefs.Beliefs);
                    EnemyDetectionRuntime.Report(snapshot, beliefs);
                    if (mode == PlannerReviewMode.StandDown) return;
                    CommanderAppointmentRuntime.Age(alliance, snapshot.DaySerial);
                    TerrainLineRuntime.PrepareDrain(snapshot);
                    AgeRecon(snapshot);
                    if (mode == PlannerReviewMode.Bookkeeping) return;

                    var sectors = SectorModel.Build(snapshot);
                    // G3 plan step (P3-P5): profiles -> plan with logistics and depot tasks.
                    Battles.Take(alliance, snapshot.DaySerial, PendingBattles);
                    // P8: the moves the tracker gave up on (not yet lapsed). G3 leaves a formation out of the mass of the objective
                    // each move was issued for, G4 takes the role from a formation that gave up on the target it would send it to.
                    var gaveUp = new List<GaveUpMove>();
                    MoveTrackers.For(alliance)?.CollectGaveUp(gaveUp, snapshot.DaySerial);
                    // G5c: the builders the depot ledger holds (it was written at the last review). G3 counts none of them in a mass, G4 pins
                    // a Wished one: one published set, read once.
                    var heldBuilders = new List<HeldBuilder>();
                    DepotTaskLedgers.For(alliance)?.CollectHeldBuilders(heldBuilders);
                    // The planner's minimum strength is the scenario's aiminimumstrengthformovement; a read that is not positive or not finite
                    // falls back to 5000 (TheaterPlanner.PlanMinimum), and the pure planner has no logger, so the review says so, once, whatever the
                    // picture flag is.
                    if (!TheaterPlanner.ReadIsUsable(snapshot.MinStrengthForMovement))
                        OnceLog.Warning("campaign-planner:min-strength-fallback", "[CampaignPlanner] aiminimumstrengthformovement reads " + snapshot.MinStrengthForMovement.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", not a minimum; the planner uses " + TheaterPlanner.MinStrengthFallback.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
                    TheaterPlan plan;
                    string trace;
                    EnemyPicture picture;
                    using (TelemetryPerf.Scope("campaign.planner.plan", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                    {
                        plan = TheaterPlanner.Plan(snapshot, sectors, id => CommanderProfiles.ForSector(snapshot.Commanders, id), state, PendingBattles,
                            TerrainLineRuntime.ForReview(), TerrainLineRuntime.ReadMarchSpeed(alliance), out trace, out picture, gaveUp, heldBuilders);
                    }
                    // G8: the ordinary garrison posts join D8's secession posts in the plan, where the allocation seats both.
                    GarrisonRuntime.PlanPosts(snapshot, plan);
                    // G1b: the recon tasks for this plan (probe verdicts on the battles it judged, then the tasks);
                    // G4's allocation below picks their formations and the sync binds the missions.
                    PlanRecon(snapshot, sectors, plan, year, month, day);
                    // G4: the force allocation over the plan, published with it (the working copy that joins PrepareDay).
                    Allocation allocation;
                    float reliability = PlayerComplianceRuntime.Reliability;
                    using (TelemetryPerf.Scope("campaign.planner.allocate", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                    {
                        NonCompliance[alliance].Observe(snapshot.DaySerial, HasMapGroupFormation(snapshot), reliability);
                        allocation = ForceAllocator.Allocate(plan, snapshot, sectors, DepartmentLookups[alliance], ReconTasks[alliance], Recon[alliance], gaveUp, heldBuilders);
                    }
                    SyncRecon(snapshot, plan, allocation);
                    GarrisonRuntime.Sync(snapshot, plan, allocation);
                    // Yesterday's allocation and plan are still the working copy; Publish overwrites them, and the defence counts of [Reserves]
                    // judge today's snapshot against them (as AgeRecon reads the allocation).
                    CampaignPlanStore.TryGetAllocation(alliance, out var yesterdaysAllocation);
                    CampaignPlanStore.TryGetPlan(alliance, out var yesterdaysPlan);
                    CampaignPlanStore.Publish(alliance, plan, allocation, sectors);
                    CampaignPlanStore.PublishExecutorContext(alliance, ExecutorContextOf(alliance));
                    LogFirstReview(snapshot, sectors, plan);
                    EmitPlan(plan, trace, day, month, year);
                    EmitPicture(snapshot, plan, picture);
                    EmitCorridor(snapshot, sectors, plan, picture);
                    EmitAllocation(allocation, plan, snapshot.Player, reliability, NonCompliance[alliance].Days);
                    EmitCovering(snapshot, plan, state);
                    EmitReserves(snapshot, sectors, plan, allocation, state, yesterdaysAllocation, yesterdaysPlan);
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
                    CampaignPlanStore.PublishExecutorContext(alliance, ExecutorContextOf(alliance));
                    // G5d: the recon missions the Plan job bound this review. Not gated on the recon flag: with it off the Plan job has
                    // ended every mission (ReleaseAll, Recalled ones included), so the pass only takes the Scouting order off their
                    // screens (a rollback must not leave one scouting for good) and moves nothing.
                    ReconExecutorRuntime.Execute(alliance, Snapshots[alliance], Recon[alliance]);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-planner:execute-failed", "[CampaignPlanner] executors failed: " + ex.Message);
                }
            }
        }

        // ---- sidecar v3 (PlannerSnapshot) ----

        // The alliance's planner state as the sidecar writes it. The live ledgers' records, or the saved ones not yet rebound
        // (a save between a load and the first review).
        internal static PlannerSnapshot Export(int alliance)
        {
            if (alliance < 0 || alliance >= States.Length) return null;
            RekeyDepartmentsFromLive(alliance);
            var saved = new PlannerSnapshot { State = States[alliance], UnreliableDays = NonCompliance[alliance].Days, UnreliableLastDay = NonCompliance[alliance].LastDay };
            saved.Beliefs.AddRange(EnemyDetectionRuntime.ModelFor(alliance).Beliefs);
            var protection = CommanderAppointmentRuntime.Book.For(alliance);
            if (protection != null) foreach (var entry in protection.ProtectedUntil) saved.Appointments.Add(entry);
            var garrisons = GarrisonLedgers.For(alliance);
            if (garrisons != null) saved.GarrisonPosts.AddRange(garrisons.Posts);
            var pending = PendingRebind[alliance];
            if (pending != null)
            {
                saved.DepotTasks.AddRange(pending.DepotTasks);
                saved.Moves.AddRange(pending.Moves);
                saved.ReconMissions.AddRange(pending.ReconMissions);
                return saved;
            }
            var depots = DepotTaskLedgers.For(alliance);
            if (depots != null) saved.DepotTasks.AddRange(depots.Tasks);
            var moves = MoveTrackers.For(alliance);
            if (moves != null) saved.Moves.AddRange(moves.Records);
            saved.ReconMissions.AddRange(Recon[alliance].Missions);
            return saved;
        }

        // The sidecar is about to be written: re-key every known formation from its live name and commander (PlannerState.RekeyFromLive).
        private static void RekeyDepartmentsFromLive(int alliance)
        {
            try
            {
                var units = BattleUnits.campaignunitlist;
                if (units == null) return;
                var live = new List<KeyValuePair<int, string>>();
                for (int i = 0; i < units.Count; i++)
                {
                    var reg = units[i];
                    if ((UnityEngine.Object)reg == null || reg.alliance != alliance) continue;
                    live.Add(new KeyValuePair<int, string>(reg.GetInstanceID(), SnapshotRules.UnitKey(((UnityEngine.Object)reg).name, reg.commander)));
                }
                States[alliance].RekeyFromLive(live);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-planner:rekey-failed", "[CampaignPlanner] re-keying the departments before the save failed: " + ex.Message);
            }
        }

        // The appointment executor changed a top formation's commander, so its unit key changed: carry its home department to the new key
        // now, so a save written before the next review keeps it (PlannerState.RekeyInstance).
        internal static void RekeyDepartment(int alliance, int instanceId, string unitName, int commander)
        {
            if (alliance < 0 || alliance >= States.Length) return;
            States[alliance].RekeyInstance(instanceId, SnapshotRules.UnitKey(unitName, commander));
        }

        // A loaded sidecar's state (null: a v1/v2 file, the planner starts fresh). What names no formation is restored now; the
        // records that do wait for RebindSaved.
        internal static void Restore(int alliance, PlannerSnapshot saved)
        {
            if (alliance < 0 || alliance >= States.Length || saved == null) return;
            States[alliance] = saved.State;
            NonCompliance[alliance].Restore(saved.UnreliableDays, saved.UnreliableLastDay);
            EnemyDetectionRuntime.ModelFor(alliance).Restore(saved.Beliefs);
            CommanderAppointmentRuntime.Book.For(alliance)?.Restore(saved.Appointments);
            MoveTrackers.For(alliance)?.Restore(null);
            DepotTaskLedgers.For(alliance)?.Restore(null);
            Recon[alliance].Restore(null);
            GarrisonLedgers.For(alliance)?.Restore(saved.GarrisonPosts);   // by unit key: the first review's PlanPosts finds the holders
            PendingRebind[alliance] = saved;
        }

        // The first review that sees this alliance's formations after a load: the saved moves, depot tasks and recon missions
        // are bound to this session's formations by unit key and handed to their ledgers. A snapshot with no formation yet
        // (the units are not loaded) waits for the next review rather than dropping every record.
        private static void RebindSaved(int alliance, CampaignSnapshot snapshot)
        {
            var saved = PendingRebind[alliance];
            if (saved == null || snapshot.Own.Count == 0) return;
            PendingRebind[alliance] = null;
            var result = PlannerRebind.Resolve(saved, snapshot, ReboundMoves, ReboundDepotTasks, ReboundMissions);
            MoveTrackers.For(alliance)?.Restore(ReboundMoves);
            DepotTaskLedgers.For(alliance)?.Restore(ReboundDepotTasks);
            Recon[alliance].Restore(ReboundMissions);
            TelemetryRouter.LegacyInfo("[CampaignPlanner] saved state rebound alliance=" + alliance
                + " moves=" + result.MovesKept + "/" + (result.MovesKept + result.MovesDropped)
                + " missions=" + result.MissionsKept + "/" + (result.MissionsKept + result.MissionsDropped)
                + " depotTasks=" + result.DepotTasksRebound + "/" + ReboundDepotTasks.Count, TelemetryLayer.Campaign);
        }

        // The alliance's snapshot of today's review (the directive ledger reads the command hierarchy from it,
        // ParentKeyOf); null before the first review, with the planner flag off, or when today's Plan job did not build one.
        internal static CampaignSnapshot SnapshotOf(int alliance)
        {
            if (alliance < 0 || alliance >= Snapshots.Length) return null;
            return PlanFreshness.IsToday(Snapshots[alliance].DaySerial) ? Snapshots[alliance] : null;
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

        // G1b: binds the allocation's Recon assignments to missions before the allocation is published, then the
        // [Recon] row.
        private static void SyncRecon(CampaignSnapshot snapshot, TheaterPlan plan, Allocation allocation)
        {
            using (TelemetryPerf.Scope("campaign.planner.recon", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                if (FlagOn(Plugin.EnableCampaignReconnaissance))
                {
                    Recon[snapshot.Alliance].Sync(snapshot, ReconTasks[snapshot.Alliance], allocation);
                    EmitRecon(snapshot, plan, allocation);
                }
                else
                {
                    // Every mission ends, a Recalled one included (Sync leaves it alone): the formations are vanilla's again where
                    // they stand, and the executor only takes Scouting off the screens (G5d, verify F2).
                    Recon[snapshot.Alliance].ReleaseAll(ReconMissionLedger.ReasonReconOff);
                }
            }
        }

        // [Recon] on change of its counts (one row per alliance per change; no string otherwise).
        private static void EmitRecon(CampaignSnapshot snapshot, TheaterPlan plan, Allocation allocation)
        {
            int a = snapshot.Alliance;
            var counts = ReconStep.CountsOf(snapshot, Recon[a], ReconTasks[a], allocation, plan);
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
        // under Plan Trace Logging (PlannerSettings.PlanTrace).
        private static void EmitPlan(TheaterPlan plan, string trace, int day, int month, int year)
        {
            string row = TheaterPlanner.Summary(plan);
            int a = plan.Alliance;
            if (a < 0 || a >= LastPlanRow.Length || string.Equals(LastPlanRow[a], row, StringComparison.Ordinal)) return;
            LastPlanRow[a] = row;
            TelemetryRouter.LegacyInfo(ReviewCadenceCatchUp.Dated(row, plan.DaySerial), TelemetryLayer.Campaign);
            if (!PlannerSettings.PlanTrace) return;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Trace, "CampaignPlan:trace", TelemetrySeverity.Info, ev => ev
                .WithAlliance(a)
                .WithDecision("trace", plan.SwitchReason ?? "none", row)
                .WithField("date", year.ToString("D4") + "-" + month.ToString("D2") + "-" + day.ToString("D2"))
                .WithField("candidates", trace ?? ""));
        }

        // [EnemyPicture] and [CapitalThreat] on change of their text (one row per alliance per change), dated after the compare. The picture
        // is built and its row written with the picture flag off too, as telemetry.
        // Foresight F6b-1: where a covering force would stand, reported on the first review and on change (the posts are read on a save before
        // F6b-2 moves anyone). Built from the review's snapshot, sectors and picture and the plan's hold needs, with no terrain probe (the share
        // sets an axis sector by whether an element lies on the axis, which no river weight changes).
        private static void EmitCorridor(CampaignSnapshot snapshot, SectorModel sectors, TheaterPlan plan, EnemyPicture picture)
        {
            int a = plan.Alliance;
            if (picture == null || a < 0 || a >= LastCorridorRow.Length) return;
            string row = CoveringForce.CorridorRow(snapshot, sectors, picture, plan, null);
            if (row == null || string.Equals(LastCorridorRow[a], row, StringComparison.Ordinal)) return;
            LastCorridorRow[a] = row;
            TelemetryRouter.LegacyInfo(ReviewCadenceCatchUp.DatedAfterAlliance(row, plan.DaySerial), TelemetryLayer.Campaign);
        }

        // Foresight F6b-2: the covering force of the review (its post, kind or the reason there is none, need, held, members, the defence flag
        // and the verdict's count), on the first review and on change of its text, dated after the compare. Written after the plan has written
        // the covering state, so verdictDays is the count the review stored.
        private static void EmitCovering(CampaignSnapshot snapshot, TheaterPlan plan, PlannerState state)
        {
            int a = plan.Alliance;
            if (a < 0 || a >= LastCoveringRow.Length) return;
            string row = CoveringForce.Row(plan.Covering, snapshot, state);
            if (string.Equals(LastCoveringRow[a], row, StringComparison.Ordinal)) return;
            LastCoveringRow[a] = row;
            TelemetryRouter.LegacyInfo(ReviewCadenceCatchUp.DatedAfterAlliance(row, plan.DaySerial), TelemetryLayer.Campaign);
        }

        private static void EmitPicture(CampaignSnapshot snapshot, TheaterPlan plan, EnemyPicture picture)
        {
            int a = plan.Alliance;
            if (picture == null || a < 0 || a >= LastPictureRow.Length) return;
            string row = EnemyPicture.Row(picture, snapshot);
            if (!string.Equals(LastPictureRow[a], row, StringComparison.Ordinal))
            {
                LastPictureRow[a] = row;
                TelemetryRouter.LegacyInfo(ReviewCadenceCatchUp.Dated(row, plan.DaySerial), TelemetryLayer.Campaign);
            }
            string capital = EnemyPicture.CapitalRow(plan, picture, snapshot);
            if (string.Equals(LastCapitalThreatRow[a], capital, StringComparison.Ordinal)) return;
            LastCapitalThreatRow[a] = capital;
            TelemetryRouter.LegacyInfo(ReviewCadenceCatchUp.Dated(capital, plan.DaySerial), TelemetryLayer.Campaign);
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
            TelemetryRouter.LegacyInfo(ReviewCadenceCatchUp.Dated(row, plan.DaySerial), TelemetryLayer.Campaign);
        }

        // [Reserves] every Full review, with no signature compare: the review is the clock the foresight criteria count on (ReservesRow).
        private static void EmitReserves(CampaignSnapshot snapshot, SectorModel sectors, TheaterPlan plan, Allocation allocation, PlannerState state,
                                         Allocation yesterdaysAllocation, TheaterPlan yesterdaysPlan)
        {
            TelemetryRouter.LegacyInfo(ReservesRow.Build(snapshot, sectors, plan, allocation, state, yesterdaysAllocation, yesterdaysPlan), TelemetryLayer.Campaign);
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
        private static void LogFirstReview(CampaignSnapshot s, SectorModel sectors, TheaterPlan plan)
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
                " minStrength=" + plan.MinGarrisonStrength.ToString("0", System.Globalization.CultureInfo.InvariantCulture) +
                " player=" + s.Player);
        }

        // Both planner flags: the planner runs and its executors may write. The availability and importance patches, the
        // army-group patch and the replacement-flow patch steer only while this holds.
        internal static bool ExecutorsEnabled()
        {
            return FlagOn(Plugin.EnableCampaignPlanner) && FlagOn(Plugin.EnableCampaignPlannerExecutors);
        }

        // What the executors' patches read with the plan (G5a): the decision and its release and the pre-war gate.
        private static ExecutorContext ExecutorContextOf(int alliance)
        {
            return ExecutorContext.From(States[alliance], PlannerChapterRules.ExecutorsAllowed(GamePrefs.leveltoload, Policy.CurrentChapter));
        }

        private static bool FlagOn(BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.Value; }
            catch { return false; }
        }
    }
}
