using System;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The planner's two calls per AI alliance in the daily strategic review (plan P1), from
    // StrategicCoordinator.RunStrategicReviewCore inside `if (operationalRuntimeReady)`:
    //   ReviewPlan    after the fiscal and policy steps, before the front ledger: snapshot ->
    //                 (G1 beliefs, G2 profiles, G3 plan, G1b recon tasks, G4 allocation, G1b
    //                 recon mission sync) -> CampaignPlanStore.Publish.
    //   ReviewExecute after the defense intent: (G2 appointments, then the G5b, G5a, G5c, G5d
    //                 executors). Executors write vanilla state only from here.
    // Both return for alliances other than 0 and 1, before AICampaign.aifaction holds the
    // alliance's faction, and for the W&L CIC player's own alliance (vanilla job 36 skips it,
    // 11620-11628). While vanilla's job machine is paused (UpdateUnitAI 11368: a W&L player with
    // no command) every faction's jobs are frozen, so ReviewExecute returns for every alliance and
    // ReviewPlan keeps only its bookkeeping (snapshot; G1 detection/beliefs and ledger aging insert
    // before that return): no replan, no publish, and the last published plan ages out (spec C5).
    internal static class CampaignPlannerRuntime
    {
        private static readonly CampaignSnapshot[] Snapshots = { new CampaignSnapshot(), new CampaignSnapshot() };

        internal static void ReviewPlan(int alliance, int day, int month, int year, DirectorPosture tempo)
        {
            using (TelemetryPerf.Scope("campaign.planner.review", TelemetryLayer.Campaign, TelemetryCategory.Performance, 4.0))
            {
                try
                {
                    if (!FlagOn(Plugin.EnableCampaignPlanner) || alliance < 0 || alliance >= Snapshots.Length) return;
                    var chain = WlChainContext.Capture();
                    PlayerCommandClass player = ReadPlayerClass(in chain);
                    var mode = PlayerCommandClassifier.ReviewMode(player, GameVars.playeralliance, alliance);
                    if (mode == PlannerReviewMode.StandDown) return;
                    int faction = FactionIndexOf(alliance);
                    if (faction < 0) return;

                    var snapshot = CampaignSnapshotRuntime.Build(alliance, faction, ReviewCadenceCatchUp.DaySerial(day, month, year),
                        player, in chain, tempo, Snapshots[alliance]);
                    if (mode == PlannerReviewMode.Bookkeeping) return;

                    var sectors = SectorModel.Build(snapshot);
                    CampaignPlanStore.Publish(alliance,
                        new TheaterPlan { Alliance = alliance, DaySerial = snapshot.DaySerial },
                        new Allocation { Alliance = alliance },
                        sectors);
                    LogFirstReview(snapshot, sectors);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-planner:review-failed", "[CampaignPlanner] review failed, nothing published: " + ex.Message);
                }
            }
        }

        internal static void ReviewExecute(int alliance)
        {
            using (TelemetryPerf.Scope("campaign.planner.execute", TelemetryLayer.Campaign, TelemetryCategory.Performance, 4.0))
            {
                try
                {
                    if (!FlagOn(Plugin.EnableCampaignPlanner) || !FlagOn(Plugin.EnableCampaignPlannerExecutors)) return;
                    if (alliance < 0 || alliance >= Snapshots.Length) return;
                    var chain = WlChainContext.Capture();
                    if (PlayerCommandClassifier.ReviewMode(ReadPlayerClass(in chain), GameVars.playeralliance, alliance) != PlannerReviewMode.Full) return;
                    if (FactionIndexOf(alliance) < 0) return;
                    // Executors (G2 appointments, G5b, G5a, G5c, G5d) run here in that order.
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-planner:execute-failed", "[CampaignPlanner] executors failed: " + ex.Message);
                }
            }
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
