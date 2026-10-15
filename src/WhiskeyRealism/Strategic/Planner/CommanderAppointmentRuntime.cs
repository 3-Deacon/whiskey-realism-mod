using System;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The appointment executor (plan G2, spec component 7): one appointment per AI alliance per
    // daily review, from CampaignPlannerRuntime.ReviewExecute (full reviews only: never for the
    // W&L CIC player's alliance, never while vanilla's job machine is paused, 11368). Runtime
    // adapter: not in the test csproj; the decision is CommanderAppointmentPlanner's (pure).
    //
    // Vanilla's own rules decide whether the planner's pick may move, re-read live just before
    // acting (VanillaAllows): CanBePickedForAIReplacement(alliance, includeofficerswithcommand:
    // true) (60748-60779), !refreshcommand, CheckSuitableRank (60556-60628; for an army group
    // CheckSuitableRank(18), as GetAvailableArmyGroupCommanders 75551 asks), the target still
    // under the commander the snapshot saw, and neither the target nor the appointee's command in
    // battle. Vanilla's own executors act:
    //   army group: ArmyGroup.AppointCommander(group, id, usefeudmechanism: false,
    //               resetcoordinationtime: true, ignoreprestige: true) (75559-75580), which first
    //               refills the appointee's old command through ReplaceCommanderOfUnit (75575).
    //               A pickable appointee never leads an army group (60770), so no group is left
    //               commanderless; the displaced commander is left without a command, as vanilla.
    //   army/corps: ReplaceCommanderOfUnit(appointee.currentcommand) when he has one (81389: his
    //               best subordinate or GetSuitableCommander takes it, and AssignCommando removes
    //               him from it), then AssignCommando(target) (60343, removes the sitting
    //               commander), DoCommanderPromotion (89077, rank to fit) and refreshcommand = true,
    //               job 24's exact recipe (17024-17026).
    // The result is verified before the appointee is protected (60 days against job 24, through
    // the #6 guard CommanderReplacementPatch), a succession event is marked applied and the
    // [Appointment] row is written.
    internal static class CommanderAppointmentRuntime
    {
        private static readonly AppointmentProtection[] Protections = { new AppointmentProtection(), new AppointmentProtection() };
        private static readonly int[] LastReviewDay = { -1, -1 };

        static CommanderAppointmentRuntime()
        {
            CampaignScope.Register("campaign-planner-appointments", Reset);
        }

        private static void Reset()
        {
            for (int a = 0; a < Protections.Length; a++)
            {
                Protections[a] = new AppointmentProtection();
                LastReviewDay[a] = -1;
            }
        }

        internal static bool Enabled()
        {
            return FlagOn(Plugin.EnableCampaignPlanner) && FlagOn(Plugin.EnableCampaignPlannerExecutors)
                && FlagOn(Plugin.EnableCommanderAppointmentPlanner);
        }

        // Every review, bookkeeping included: today's serial for the #6 guard and the aging of the
        // protection windows.
        internal static void Age(int alliance, int daySerial)
        {
            if (alliance < 0 || alliance >= Protections.Length) return;
            LastReviewDay[alliance] = daySerial;
            Protections[alliance].Prune(daySerial);
        }

        // Read by the #6 guard inside vanilla's job 24 (between reviews: the last review's day).
        internal static bool IsProtected(int alliance, int commanderId)
        {
            if (alliance < 0 || alliance >= Protections.Length || LastReviewDay[alliance] < 0) return false;
            return Protections[alliance].IsProtected(commanderId, LastReviewDay[alliance]);
        }

        internal static void Execute(int alliance, CampaignSnapshot s)
        {
            using (TelemetryPerf.Scope("campaign.planner.appoint", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                if (!Enabled()) return;
                if (alliance < 0 || alliance >= Protections.Length || s == null || s.Alliance != alliance) return;
                if (!CampaignPlanStore.TryGetPlan(alliance, out var plan) || !CampaignPlanStore.TryGetSectors(alliance, out var sectors)) return;
                if (plan.DaySerial != s.DaySerial || LastReviewDay[alliance] != s.DaySerial) return;

                var succession = StrategicCoordinator.Instance != null ? StrategicCoordinator.Instance.Succession : null;
                SuccessionPrior prior = (int id, string role, out int eventId) =>
                {
                    eventId = -1;
                    return succession != null ? succession.PriorFor(alliance, NameOf(s, id), role, out eventId) : 0f;
                };
                var protection = Protections[alliance];
                if (!CommanderAppointmentPlanner.TryPlan(plan, s, sectors, prior, protection, i => VanillaAllows(alliance, i), out var intent))
                    return;

                string unitName;
                bool done = intent.TargetIsArmyGroup
                    ? AppointToArmyGroup(intent, out unitName)
                    : AppointToUnit(intent, out unitName);
                if (!done)
                {
                    OnceLog.Warning("campaign-planner:appointment-failed",
                        "[CampaignPlanner] an appointment vanilla allowed did not take effect; nothing recorded");
                    return;
                }

                protection.Protect(intent.CommanderId, s.DaySerial);
                if (intent.SuccessionEventId >= 0 && succession != null) succession.MarkApplied(intent.SuccessionEventId);
                TelemetryRouter.LegacyInfo(
                    "[Appointment] alliance=" + alliance +
                    " commander=" + NameOf(s, intent.CommanderId) +
                    " unit=" + unitName +
                    " reason=" + intent.Reason +
                    " displaced=" + NameOf(s, intent.DisplacedId),
                    TelemetryLayer.Campaign);
            }
        }

        private static bool VanillaAllows(int alliance, AppointmentIntent intent)
        {
            try
            {
                var commanders = GameVars.commander;
                if (commanders == null || intent.CommanderId < 0 || intent.CommanderId >= commanders.Count) return false;
                if (DLC_WL.dlc_scenarioactive && intent.CommanderId == DLC_WL.dlc_chosencommander) return false;
                var c = commanders[intent.CommanderId];
                if (c == null || c.alliance != alliance || c.refreshcommand) return false;
                if (!c.CanBePickedForAIReplacement(alliance, includeofficerswithcommand: true)) return false;
                if ((UnityEngine.Object)c.currentcommand != null && c.currentcommand.inbattle) return false;

                if (intent.TargetIsArmyGroup)
                {
                    var group = FindArmyGroup(intent.TargetInstanceId);
                    return (UnityEngine.Object)group != null && group.GetCommanderID() == intent.DisplacedId && c.CheckSuitableRank(18);
                }
                var target = FindUnit(intent.TargetInstanceId);
                return (UnityEngine.Object)target != null && target.alliance == alliance && target.commander == intent.DisplacedId
                    && !target.inbattle && !target.dlcw_isundercommander && c.CheckSuitableRank(target.unittyp, target);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-planner:appointment-check-failed", "[CampaignPlanner] appointment check failed, appointee skipped: " + ex.Message);
                return false;
            }
        }

        private static bool AppointToArmyGroup(AppointmentIntent intent, out string unitName)
        {
            unitName = "?";
            try
            {
                var group = FindArmyGroup(intent.TargetInstanceId);
                if ((UnityEngine.Object)group == null) return false;
                unitName = ((UnityEngine.Object)group).name;
                ArmyGroup.AppointCommander(group, intent.CommanderId, usefeudmechanism: false, resetcoordinationtime: true, ignoreprestige: true);
                return group.GetCommanderID() == intent.CommanderId;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-planner:appointment-group-failed", "[CampaignPlanner] army-group appointment failed: " + ex.Message);
                return false;
            }
        }

        private static bool AppointToUnit(AppointmentIntent intent, out string unitName)
        {
            unitName = "?";
            try
            {
                var target = FindUnit(intent.TargetInstanceId);
                if ((UnityEngine.Object)target == null) return false;
                unitName = ((UnityEngine.Object)target).name;
                var c = GameVars.commander[intent.CommanderId];
                var old = c.currentcommand;
                if ((UnityEngine.Object)old != null && old != target) BattleUnits.ReplaceCommanderOfUnit(old);
                c.AssignCommando(target);
                BattleUnits.DoCommanderPromotion(intent.CommanderId);
                c.refreshcommand = true;
                return target.commander == intent.CommanderId && c.currentcommand == target;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-planner:appointment-unit-failed", "[CampaignPlanner] appointment failed: " + ex.Message);
                return false;
            }
        }

        private static Regiment FindUnit(int instanceId)
        {
            var units = BattleUnits.campaignunitlist;
            if (units == null) return null;
            for (int i = 0; i < units.Count; i++)
                if ((UnityEngine.Object)units[i] != null && units[i].GetInstanceID() == instanceId) return units[i];
            return null;
        }

        private static ArmyGroup FindArmyGroup(int instanceId)
        {
            var groups = BattleUnits.armygroups;
            if (groups == null) return null;
            for (int i = 0; i < groups.Count; i++)
                if ((UnityEngine.Object)groups[i] != null && groups[i].GetInstanceID() == instanceId) return groups[i];
            return null;
        }

        private static string NameOf(CampaignSnapshot s, int commanderId)
        {
            for (int i = 0; i < s.Commanders.Count; i++)
                if (s.Commanders[i].Id == commanderId) return s.Commanders[i].Name;
            return commanderId.ToString();
        }

        private static bool FlagOn(BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.Value; }
            catch { return false; }
        }
    }
}
