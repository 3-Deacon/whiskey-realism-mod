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
    //               The displaced commander is left without a command, as vanilla.
    //   succession reseat: a historical figure a fired event names who sits on another army
    //               group (not pickable, 60770) moves only when CommanderAssignmentGuard
    //               .ResolveReplacementAppointment says ReseatThenAppoint live (free apart from
    //               the group, an idle successor in GetAvailableArmyGroupCommanders 75546 who is
    //               himself CanBePickedForAIReplacement and not the W&L player): his old group is
    //               reseated first (AppointCommander never clears it, 75559), then he is appointed.
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
        // The protection windows and each alliance's last review day (pure, tested).
        internal static AppointmentProtectionBook Book { get; private set; } = new AppointmentProtectionBook();

        static CommanderAppointmentRuntime()
        {
            CampaignScope.Register("campaign-planner-appointments", Reset);
        }

        private static void Reset()
        {
            Book = new AppointmentProtectionBook();
        }

        internal static bool Enabled()
        {
            return FlagOn(Plugin.EnableCampaignPlanner) && FlagOn(Plugin.EnableCampaignPlannerExecutors)
                && FlagOn(Plugin.EnableCommanderAppointmentPlanner);
        }

        // Every review, bookkeeping included: the review day for the #6 guard and the aging of
        // the protection windows.
        internal static void Age(int alliance, int daySerial)
        {
            Book.Age(alliance, daySerial);
        }

        // CanBePickedForAIReplacement (60748-60779) minus its army-group-commander test (main's
        // C-CmdrReplace rule): the snapshot's CommanderRecord.FreeApartFromArmyGroup and the live
        // reseat check.
        internal static bool FreeApartFromArmyGroup(GameVars.Commander commander, int alliance, int commanderId)
        {
            try
            {
                if (commander == null || commander.alliance != alliance) return false;
                if (!commander.IsAvailable() || commander.IsServingUnderPlayer()) return false;
                if (commander.IsMemberOfStaff() || commander.IsMemberOfStaff(1)) return false;
                var alliances = GameVars.alliance;
                return alliances != null && alliance >= 0 && alliance < alliances.Length && alliances[alliance].commanderinchief != commanderId;
            }
            catch { return false; }
        }

        internal static void Execute(int alliance, CampaignSnapshot s)
        {
            using (TelemetryPerf.Scope("campaign.planner.appoint", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    if (!Enabled()) return;
                    var protection = Book.For(alliance);
                    if (protection == null || s == null || s.Alliance != alliance) return;
                    if (!CampaignPlanStore.TryGetPlan(alliance, out var plan) || !CampaignPlanStore.TryGetSectors(alliance, out var sectors)) return;
                    if (plan.DaySerial != s.DaySerial || Book.LastReviewDay(alliance) != s.DaySerial) return;

                    var succession = StrategicCoordinator.Instance != null ? StrategicCoordinator.Instance.Succession : null;
                    SuccessionPrior prior = (int id, string role, out int eventId) =>
                    {
                        eventId = -1;
                        return succession != null ? succession.PriorFor(alliance, NameOf(s, id), role, out eventId) : 0f;
                    };
                    if (!CommanderAppointmentPlanner.TryPlan(plan, s, sectors, prior, protection, i => VanillaAllows(alliance, i), out var intent))
                        return;

                    string unitName = "?", reseated = null;
                    bool done = (intent.ReseatGroupInstanceId < 0 || Reseat(alliance, intent, out reseated)) && (intent.TargetIsArmyGroup
                        ? AppointToArmyGroup(intent, out unitName)
                        : AppointToUnit(alliance, intent, out unitName));
                    if (!done)
                    {
                        OnceLog.Warning("campaign-planner:appointment-failed",
                            "[CampaignPlanner] an appointment vanilla allowed did not take effect; nothing protected or marked applied");
                        // The reseat already moved the figure off his group: record that partial state.
                        // He is idle now, so the next review sees him as an ordinary pool candidate.
                        if (reseated != null) EmitAppointment(s, alliance, intent, unitName, reseated, "appoint-failed-after-reseat");
                        return;
                    }

                    protection.Protect(intent.CommanderId, s.DaySerial);
                    if (intent.SuccessionEventId >= 0 && succession != null) succession.MarkApplied(intent.SuccessionEventId);
                    EmitAppointment(s, alliance, intent, unitName, reseated, "appointed");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-planner:appointment-step-failed",
                        "[CampaignPlanner] appointment step failed, no appointment today: " + ex.Message);
                }
            }
        }

        private static void EmitAppointment(CampaignSnapshot s, int alliance, AppointmentIntent intent, string unitName, string reseated, string outcome)
        {
            TelemetryRouter.LegacyInfo(
                "[Appointment] alliance=" + alliance +
                " commander=" + TelemetryLegacyParser.Quote(NameOf(s, intent.CommanderId)) +
                " unit=" + TelemetryLegacyParser.Quote(unitName) +
                " reason=" + intent.Reason +
                " displaced=" + TelemetryLegacyParser.Quote(NameOf(s, intent.DisplacedId)) +
                (reseated != null ? " reseated=" + TelemetryLegacyParser.Quote(reseated) : "") +
                " outcome=" + outcome,
                TelemetryLayer.Campaign);
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
                if ((UnityEngine.Object)c.currentcommand != null && c.currentcommand.inbattle) return false;
                bool pickable = c.CanBePickedForAIReplacement(alliance, includeofficerswithcommand: true);
                var decision = pickable ? ReplacementAppointment.AppointDirect : ReseatDecision(alliance, intent, c);
                if (decision != (intent.ReseatGroupInstanceId < 0 ? ReplacementAppointment.AppointDirect : ReplacementAppointment.ReseatThenAppoint))
                    return false;

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

        // The live half of the succession reseat: the planner's group is still his, holds nothing
        // of the W&L player's, he is free apart from it and an idle successor exists.
        private static ReplacementAppointment ReseatDecision(int alliance, AppointmentIntent intent, GameVars.Commander c)
        {
            var group = intent.ReseatGroupInstanceId >= 0 ? FindArmyGroup(intent.ReseatGroupInstanceId) : null;
            bool leads = (UnityEngine.Object)group != null && group.GetCommanderID() == intent.CommanderId && !GroupHoldsPlayer(group);
            return CommanderAssignmentGuard.ResolveReplacementAppointment(
                false, leads, FreeApartFromArmyGroup(c, alliance, intent.CommanderId), PickSuccessor(alliance, intent.CommanderId) >= 0);
        }

        private static bool GroupHoldsPlayer(ArmyGroup group)
        {
            if (!DLC_WL.dlc_scenarioactive || group.attachedunits == null) return false;
            if (group.GetCommanderID() == DLC_WL.dlc_chosencommander) return true;
            for (int i = 0; i < group.attachedunits.Count; i++)
            {
                var unit = group.attachedunits[i];
                if ((UnityEngine.Object)unit != null && (unit.dlcw_isundercommander || DLC_WL.IsPlayerPartOfUnit(unit))) return true;
            }
            return false;
        }

        // Best-rated idle officer from vanilla's own army-group pool (GetAvailableArmyGroupCommanders
        // 75546: available, no command, not refreshing, rank suitable for 18), also vanilla's AI
        // pool (CanBePickedForAIReplacement 60748) and never the W&L player.
        private static int PickSuccessor(int alliance, int excludeId)
        {
            var pool = ArmyGroup.GetAvailableArmyGroupCommanders(alliance);
            if (pool == null) return -1;
            int best = -1;
            float bestValue = float.MinValue;
            for (int i = 0; i < pool.Count; i++)
            {
                int id = pool[i];
                if (id == excludeId || id < 0 || id >= GameVars.commander.Count) continue;
                if (DLC_WL.dlc_scenarioactive && id == DLC_WL.dlc_chosencommander) continue;
                var candidate = GameVars.commander[id];
                if (!candidate.CanBePickedForAIReplacement(alliance)) continue;
                float value = candidate.GetAverageAttributeValue();
                if (value > bestValue)
                {
                    bestValue = value;
                    best = id;
                }
            }
            return best;
        }

        private static bool Reseat(int alliance, AppointmentIntent intent, out string reseated)
        {
            reseated = null;
            try
            {
                var group = FindArmyGroup(intent.ReseatGroupInstanceId);
                int successor = PickSuccessor(alliance, intent.CommanderId);
                if ((UnityEngine.Object)group == null || successor < 0) return false;
                ArmyGroup.AppointCommander(group, successor, usefeudmechanism: false, resetcoordinationtime: true, ignoreprestige: true);
                if (group.GetCommanderID() != successor) return false;
                reseated = ((UnityEngine.Object)group).name + ":" + GameVars.commander[successor].combinedname;
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-planner:appointment-reseat-failed", "[CampaignPlanner] succession reseat failed: " + ex.Message);
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

        private static bool AppointToUnit(int alliance, AppointmentIntent intent, out string unitName)
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
                bool taken = target.commander == intent.CommanderId && c.currentcommand == target;
                if (taken)
                {
                    // Both units' commanders changed, so both unit keys did: the planner's home departments follow (PlannerState.RekeyInstance).
                    CampaignPlannerRuntime.RekeyDepartment(alliance, target.GetInstanceID(), ((UnityEngine.Object)target).name, target.commander);
                    if ((UnityEngine.Object)old != null && old != target)
                        CampaignPlannerRuntime.RekeyDepartment(alliance, old.GetInstanceID(), ((UnityEngine.Object)old).name, old.commander);
                }
                return taken;
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
