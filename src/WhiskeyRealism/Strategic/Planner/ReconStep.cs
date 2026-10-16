using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The recon mission counts of one review, the `[Recon]` row (plan § G1b): active missions by kind,
    // recalled ones, the planned tasks that exist because an approach is unwatched, and the age in days
    // of the oldest live belief. A struct compared field by field, so a review whose row is unchanged
    // builds no string.
    internal struct ReconCounts : IEquatable<ReconCounts>
    {
        public int Screens, Pickets, AdvanceGuards, Probes, Recalled, UnwatchedApproaches, OldestBeliefDays;

        public bool Equals(ReconCounts other)
        {
            return Screens == other.Screens && Pickets == other.Pickets && AdvanceGuards == other.AdvanceGuards &&
                   Probes == other.Probes && Recalled == other.Recalled && UnwatchedApproaches == other.UnwatchedApproaches &&
                   OldestBeliefDays == other.OldestBeliefDays;
        }

        public string Row(int alliance)
        {
            return "[Recon] alliance=" + alliance + " screens=" + Screens + " pickets=" + Pickets + " advanceGuards=" + AdvanceGuards +
                   " probes=" + Probes + " recalled=" + Recalled + " unwatchedApproaches=" + UnwatchedApproaches +
                   " oldestBeliefDays=" + OldestBeliefDays;
        }
    }

    // The G1b runtime step as pure functions, shared by CampaignPlannerRuntime and the sliced-review model
    // so the harness runs the sequence the runtime runs (plan § G1b "runtime as built"):
    //   ReconMissionLedger.OnReview   (the runtime's AgeRecon: also on a paused day)
    //   ReconStep.Plan                probe verdicts, then ReconPlanner.PlanTasks     (before the allocation)
    //   ForceAllocator.Allocate       picks the formations (ReconPlanner.PickFormation)
    //   ReconMissionLedger.Sync       binds the Recon assignments to missions        (before the publish)
    // Fog-honest: beliefs and the battles this review judges only, never an enemy destination.
    internal static class ReconStep
    {
        // What OperationalProbeOptions carried for a probe's withdraw ratio and reaction multiplier (G6
        // deletes that class); the escalate ratio is the plan's own required ratio for the main effort.
        internal const float ProbeWithdrawRatio = 0.55f;
        internal const float ProbeReactionMultiplier = 1.45f;
        internal const string ReleasedReason = "main-moved"; // the mission's reason when its probe is released

        // `battles` are the battles this review judges (BattleInbox.Take) on the battle history's own day
        // scale, `legacyDaySerial` (BattleHistoryQuery.LegacySerial); the missions ledger's days are
        // the snapshot's calendar serial. The two scales never meet.
        internal static void Plan(CampaignSnapshot s, SectorModel sectors, TheaterPlan plan, IReadOnlyList<BattleHistoryRecord> battles,
                                  int legacyDaySerial, ReconMissionLedger missions, List<ReconTask> into)
        {
            ProbeVerdicts(s, plan, battles, legacyDaySerial, missions);
            ReconPlanner.PlanTasks(s, sectors, plan, missions, into);
        }

        // Each active probe mission's ContactEvidenceLedger verdict on the main objective it screens: the
        // main effort's mass against the enemy G3 judged its odds against (TheaterPlan.MainEnemyStrength: one
        // source for that number), the battles this review judges within the relocation range of it, and how
        // current the beliefs there are (EnemyBeliefQuery.ConfidenceWithin, the stale gate).
        // Escalate clears the mission (the planner's balance uses the fresh belief), withdraw recalls it, and
        // a stale picture cannot escalate (ContactEvidenceLedger.MinEscalationConfidence).
        internal static void ProbeVerdicts(CampaignSnapshot s, TheaterPlan plan, IReadOnlyList<BattleHistoryRecord> battles,
                                           int legacyDaySerial, ReconMissionLedger missions)
        {
            if (s == null || plan == null || missions == null) return;
            var list = missions.Missions;
            float radius = Math.Max(0f, s.RelocationRange);
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m.Kind != ReconKind.Probe || !ReconMissionLedger.IsActive(m.State)) continue;
                // A probe serves the main effort: one that stands on another sector than the plan's main (G3
                // moved it) is released, not judged against the new main's numbers and not recalled.
                if (m.SectorId != plan.MainSectorId)
                {
                    missions.Release(m.InstanceId, ReleasedReason);
                    continue;
                }
                var objective = SectorPlanOf(plan, m.SectorId);
                if (objective == null) continue;
                float enemy = plan.MainEnemyStrength;
                // The remembered enemy belongs to the objective it was seen at; another objective restarts it.
                bool remembered = m.ObservedEnemyStrength >= 0f && m.ObservedObjectiveId == plan.MainObjectiveId;
                var input = new ContactEvidenceInput
                {
                    ObservingAllianceId = s.Alliance,
                    TargetX = objective.TargetX,
                    TargetZ = objective.TargetZ,
                    CurrentEnemyStrength = enemy,
                    CurrentFriendlyStrength = plan.MassStrength,
                    PreviousObservedEnemyStrength = remembered ? m.ObservedEnemyStrength : enemy,
                    EnemyReactionMultiplier = ProbeReactionMultiplier,
                    EscalateFriendlyRatio = ReconPlanner.RequiredRatioOf(s, plan, objective),
                    WithdrawFriendlyRatio = ProbeWithdrawRatio,
                    BattleHistory = battles,
                    SpatialMaxDistance = radius,
                    CurrentDaySerial = legacyDaySerial,
                    BeliefConfidence01 = EnemyBeliefQuery.ConfidenceWithin(s.Beliefs, objective.TargetX, objective.TargetZ, radius)
                };
                missions.OnProbeContact(m.InstanceId, ContactEvidenceLedger.Build(input), s.DaySerial, enemy, plan.MainObjectiveId);
            }
        }

        // The directive ledger's question (FormationDirectiveLedger.Build's onReconMission): is the formation
        // with this unit key on a recon mission? Unit key -> the snapshot's index -> the ledger.
        internal static bool IsReconKey(CampaignSnapshot s, ReconMissionLedger missions, string unitKey)
        {
            if (s == null || missions == null) return false;
            int own = s.IndexOfKey(unitKey);
            return own >= 0 && missions.IsRecon(s.Own[own].InstanceId);
        }

        internal static ReconCounts CountsOf(CampaignSnapshot s, ReconMissionLedger missions, IReadOnlyList<ReconTask> tasks)
        {
            var counts = new ReconCounts();
            if (missions != null)
            {
                var list = missions.Missions;
                for (int i = 0; i < list.Count; i++)
                {
                    var state = list[i].State;
                    if (state == ReconState.Recalled) { counts.Recalled++; continue; }
                    if (!ReconMissionLedger.IsActive(state)) continue;
                    switch (list[i].Kind)
                    {
                        case ReconKind.Screen: counts.Screens++; break;
                        case ReconKind.Picket: counts.Pickets++; break;
                        case ReconKind.AdvanceGuard: counts.AdvanceGuards++; break;
                        case ReconKind.Probe: counts.Probes++; break;
                    }
                }
            }
            if (tasks != null)
            {
                for (int i = 0; i < tasks.Count; i++)
                {
                    string reason = tasks[i].Reason;
                    if (reason == ReconPlanner.ReasonMainAxis || reason == ReconPlanner.ReasonCapitalApproach || reason == ReconPlanner.ReasonFrontApproach)
                        counts.UnwatchedApproaches++;
                }
            }
            if (s != null)
            {
                for (int i = 0; i < s.Beliefs.Count; i++)
                {
                    if (s.Beliefs[i].Confidence01 <= 0f) continue;
                    counts.OldestBeliefDays = Math.Max(counts.OldestBeliefDays, s.DaySerial - s.Beliefs[i].LastSeenDay);
                }
            }
            return counts;
        }

        private static SectorPlan SectorPlanOf(TheaterPlan plan, int sectorId)
        {
            for (int i = 0; i < plan.Sectors.Count; i++)
                if (plan.Sectors[i] != null && plan.Sectors[i].SectorId == sectorId) return plan.Sectors[i];
            return null;
        }
    }
}
