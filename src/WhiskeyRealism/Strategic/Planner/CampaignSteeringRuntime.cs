using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The steering executors' vanilla side (plan G5a): the campaign objective write (spec C6) and the theater box (spec C8).
    // The decisions are SteeringRules' (pure, tested); this class reads and writes the live fields. Runs only inside
    // ReviewExecute, after the pre-war gate, for the alliance the planner stands in for.
    //
    // Objective: AIFaction.followedcampaignobjective is an offensive importance weight on the objective's towns and IIPs
    // (UpdateImportanceValues 14922/14961) and recruiting reads it (12337); vanilla picks it at random when it is < 0
    // (PickCampaignObjective 17769-17789). The planner writes its main objective and releases a value it wrote when there is
    // no main effort; the value the mod wrote is recorded through PickCampaignObjectivePatch's statics (the coordinator
    // persists them at 1911/1957 until G6 moves the field), and that patch stands down while the executors run (P10: one
    // writer per surface).
    //
    // Theater box: Regiment.theaterposition is the centre of the operations box vanilla tests an offensive target against
    // (IsWithinOperationsTheater 17105; UpdateCampaignTheaters 17034-17110 sets it when default and resets it when the unit
    // stands outside). A MainEffort / Support formation in its sector gets the centre toward its target that keeps it inside the
    // box, a Hold formation is centred on itself, at most twice a week.
    internal static class CampaignSteeringRuntime
    {
        private static readonly TheaterWriteLimiter[] Limiters = { new TheaterWriteLimiter(), new TheaterWriteLimiter() };
        private static readonly List<TheaterWrite> Writes = new List<TheaterWrite>();

        static CampaignSteeringRuntime()
        {
            CampaignScope.Register("campaign-planner-theater-limiter", Reset);
        }

        private static void Reset()
        {
            for (int a = 0; a < Limiters.Length; a++) Limiters[a].Clear();
            Writes.Clear();
        }

        internal static void Execute(int alliance, CampaignSnapshot snapshot)
        {
            // Construction steering (G5c) is a steering executor too: the depot wishes and the rail and fort preferences. It rides
            // here, ahead of the mover and behind the same pre-war and today's-plan gates, because ReviewExecute's own list of
            // executors belongs to the reconnaissance slice (G5d); it has its own scope and flags.
            DepotSteeringRuntime.Execute(alliance, snapshot);
            using (TelemetryPerf.Scope("campaign.planner.steering", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                if (snapshot == null || alliance < 0 || alliance >= Limiters.Length) return;
                if (!CampaignPlanStore.TryGetPlan(alliance, out var plan) || !CampaignPlanStore.TryGetAllocation(alliance, out var allocation)) return;
                object faction = AICampaignReflect.GetFaction(snapshot.FactionIndex);
                if (faction == null) return;
                int objective = WriteObjective(alliance, snapshot, plan, faction);
                int boxes = WriteTheaters(alliance, snapshot, allocation);
                if (objective != CampaignObjectiveSteering.NoWrite || boxes > 0)
                    TelemetryRouter.LegacyInfo("[CampaignSteer] alliance=" + alliance
                        + " objective=" + (objective == CampaignObjectiveSteering.NoWrite ? "kept" : objective < 0 ? "released" : objective.ToString())
                        + " theaters=" + boxes, TelemetryLayer.Campaign);
            }
        }

        private static int WriteObjective(int alliance, CampaignSnapshot snapshot, TheaterPlan plan, object faction)
        {
            var field = VanillaFieldCache.Field(faction.GetType(), "followedcampaignobjective");
            if (field == null)
            {
                OnceLog.Warning("campaign-planner:steer-objective-field", "[CampaignPlanner] AIFaction.followedcampaignobjective not found; objective steering off");
                return CampaignObjectiveSteering.NoWrite;
            }
            int current = Convert.ToInt32(field.GetValue(faction));
            int write = SteeringRules.ObjectiveWrite(plan.MainObjectiveId, ObjectiveAvailable(snapshot, plan.MainObjectiveId), current, PickCampaignObjectivePatch.WrittenByMod(alliance));
            if (write == CampaignObjectiveSteering.NoWrite) return write;
            field.SetValue(faction, write);
            PickCampaignObjectivePatch.NoteWrittenByMod(alliance, write);
            return write;
        }

        // GetAvailableObjectives (178827-178866) as the snapshot recorded it: an objective with a record flagged Available.
        private static bool ObjectiveAvailable(CampaignSnapshot s, int objectiveId)
        {
            if (objectiveId < 0) return false;
            for (int i = 0; i < s.Objectives.Count; i++)
                if (s.Objectives[i].ObjectiveId == objectiveId && s.Objectives[i].Available) return true;
            return false;
        }

        private static int WriteTheaters(int alliance, CampaignSnapshot snapshot, Allocation allocation)
        {
            if (!CampaignPlanStore.TryGetSectors(alliance, out var sectors)) return 0;
            var limiter = Limiters[alliance];
            SteeringRules.PlanTheaterWrites(snapshot, allocation, sectors, limiter, Writes);
            int made = 0;
            for (int k = 0; k < Writes.Count; k++)
            {
                var write = Writes[k];
                var unit = CampaignSnapshotRuntime.OwnUnit(write.InstanceId);
                if (unit == null || unit.alliance != alliance) continue;
                unit.theaterposition = new Vector3(write.X, unit.GetPosition().y, write.Z);
                limiter.Record(write.InstanceId, snapshot.DaySerial);
                made++;
            }
            Writes.Clear();
            return made;
        }
    }
}
