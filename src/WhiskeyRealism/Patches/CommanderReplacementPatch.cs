using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #6 — Prefix on AICampaign.CheckAICommanderReplacements
    // (decompile line 17008). When SuccessionScheduler has fired but
    // un-applied events for this alliance, perform the scripted swap and skip
    // vanilla's competence-based pick for this tick. Otherwise vanilla runs.
    //
    // 2026-10-14 (C-CmdrReplace) swap mechanic, aligned with vanilla:
    //   1. Replacement = the catalogued historical figure (exact combinedname for
    //      ambiguous surnames, ArmyGroupDoctrine.CommanderNameMatches), filtered by
    //      Commander.CanBePickedForAIReplacement(alliance, includeofficerswithcommand:
    //      true) (60748) and never the W&L avatar. Vanilla often seats him on an
    //      auto-created group first (CreateNewArmyGroup 75653), which that filter
    //      rejects; such a commander moves only if otherwise free and his old group
    //      is reseated from GetAvailableArmyGroupCommanders (75546) beforehand.
    //   2. Target army group = resolved from Event.ReplacedRole + theater
    //      (CommanderAssignmentGuard.ResolveRoleTargetGroup), never the W&L
    //      player's group. GeneralInChief is GameVars.alliance.commanderinchief,
    //      not an army group, and stays deferred.
    //   3. Swap via ArmyGroup.AppointCommander (75559): sets ArmyGroup.commanderid
    //      and first refills the replacement's old command through
    //      BattleUnits.ReplaceCommanderOfUnit. The old code called
    //      Commander.AssignCommando on the displaced commander's currentcommand,
    //      which never changed commanderid and required currentcommand != null,
    //      a state vanilla's AppointCommander -> RemoveFromCommand (60830) clears.
    //   4. Mark applied via SuccessionScheduler.MarkApplied (persisted in sidecar).
    [HarmonyPatch(typeof(AICampaign), "CheckAICommanderReplacements")]
    internal static class CommanderReplacementPatch
    {
        private static readonly HashSet<string> LogOnceKeys = new HashSet<string>();

        [HarmonyPrefix]
        internal static bool Prefix(int _aifaction)
        {
            using (TelemetryPerf.Scope("campaign.patch.commander-replacement", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.commander-replacement");
                OnceLog.Info("replace", "CommanderReplacementPatch wired (role-resolved AppointCommander swap)");
                try
                {
                    if (StrategicCoordinator.Instance == null) return true;

                    int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                    if (allianceId < 0 || allianceId > 1) return true;

                    int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                    if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return true;

                    // Collect first to avoid mutating the applied set during enumeration.
                    var pending = new List<SuccessionScheduler.Event>();
                    foreach (var e in StrategicCoordinator.Instance.Succession.GetPendingApplications(allianceId))
                        pending.Add(e);
                    if (pending.Count == 0) return true;

                    bool anyApplied = false;
                    foreach (var e in pending)
                    {
                        if (TryApplyScriptedSwap(allianceId, e))
                        {
                            StrategicCoordinator.Instance.Succession.MarkApplied(e.Id);
                            anyApplied = true;
                        }
                    }

                    // After a scripted swap, skip vanilla's competence-based pick this
                    // tick so it does not immediately second-guess the appointment.
                    return !anyApplied;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("replace:prefix", "[Patch:Replace] " + ex.Message);
                    return true;
                }
            }
        }

        private static bool TryApplyScriptedSwap(int allianceId, SuccessionScheduler.Event e)
        {
            try
            {
                if (!CommanderAssignmentGuard.RoleIsArmyGroupCommand(e.ReplacedRole))
                {
                    Defer(e, allianceId, "role-not-army-group", "role=" + e.ReplacedRole);
                    return false;
                }

                var commanders = GameVars.commander;
                if (commanders == null || BattleUnits.armygroups == null) return false;

                var groups = AllianceGroups(allianceId);
                int targetIndex = CommanderAssignmentGuard.ResolveRoleTargetGroup(e.ReplacedRole, BuildCandidates(groups));
                if (targetIndex < 0 && IsForceAllSuccessionEnabled())
                {
                    // Test mode: exercise the same AppointCommander path on any AI
                    // army group of this alliance when the role's theater has none.
                    targetIndex = AnyAiGroupIndex(groups);
                    if (targetIndex >= 0)
                        TelemetryRouter.LegacyInfo($"[Succession:{e.Id}] action=test-fallback alliance={allianceId} reason=no-role-group", TelemetryLayer.Campaign);
                }
                if (targetIndex < 0)
                {
                    Defer(e, allianceId, "no-role-group", "role=" + e.ReplacedRole);
                    return false;
                }

                var group = groups[targetIndex];
                int displacedId = group.GetCommanderID();

                // Already in command of the role's army: nothing to swap.
                if (displacedId >= 0 && displacedId < commanders.Count &&
                    ArmyGroupDoctrine.CommanderNameMatches(allianceId, e.ReplacementName, commanders[displacedId]?.combinedname))
                    return true;

                int replacementId = FindReplacementId(commanders, allianceId, e.ReplacementName, out var plan, out var oldGroup, out int successorId);
                if (replacementId < 0 || plan == ReplacementAppointment.Defer)
                {
                    Defer(e, allianceId, "no-available-replacement", "replacement=" + e.ReplacementName);
                    return false;
                }

                if (plan == ReplacementAppointment.ReseatThenAppoint)
                {
                    // Vanilla AppointCommander does not clear the old group's commanderid;
                    // reseat it first from vanilla's own pool so nobody commands two groups.
                    ArmyGroup.AppointCommander(oldGroup, successorId, usefeudmechanism: false, resetcoordinationtime: true, ignoreprestige: true);
                    if (oldGroup.GetCommanderID() != successorId) return false;
                }

                ArmyGroup.AppointCommander(group, replacementId, usefeudmechanism: false, resetcoordinationtime: true, ignoreprestige: true);
                if (group.GetCommanderID() != replacementId) return false;

                TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "Succession", TelemetrySeverity.Info, ev => ev
                    .WithAlliance(allianceId)
                    .WithDecision("applied", e.ReplacedRole, "alliance=" + allianceId + "|event=" + e.Id + "|replacement=" + replacementId + "|displaced=" + displacedId)
                    .WithField("eventId", e.Id)
                    .WithField("replacement", e.ReplacementName)
                    .WithField("replacementId", replacementId)
                    .WithField("displacedId", displacedId)
                    .WithField("armyGroup", SafeName(group))
                    .WithField("role", e.ReplacedRole));
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("replace:swap", $"[Succession] swap failed: {ex.Message}");
                return false;
            }
        }

        private static List<ArmyGroup> AllianceGroups(int allianceId)
        {
            var result = new List<ArmyGroup>();
            var all = BattleUnits.armygroups;
            for (int i = 0; i < all.Count; i++)
            {
                var group = all[i];
                if ((UnityEngine.Object)(object)group == (UnityEngine.Object)null) continue;
                if (group.GetAlliance() != allianceId) continue;
                result.Add(group);
            }
            return result;
        }

        private static List<ArmyGroupRoleCandidate> BuildCandidates(List<ArmyGroup> groups)
        {
            var candidates = new List<ArmyGroupRoleCandidate>(groups.Count);
            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                Vector3 position = default(Vector3);
                float strength = 0f;
                int count = 0;
                if (group.attachedunits != null)
                {
                    for (int u = 0; u < group.attachedunits.Count; u++)
                    {
                        var unit = group.attachedunits[u];
                        if ((UnityEngine.Object)(object)unit == (UnityEngine.Object)null) continue;
                        position += ((Component)unit).transform.position;
                        strength += unit.groupstrengthactive;
                        count++;
                    }
                }
                if (count > 0) position /= count;
                Theater theater = count > 0 ? TheaterClassifier.FromPosition(position.x, position.z) : Theater.Unknown;
                string area = count > 0 ? ArmyAreaRuntime.AreaKey(position) : null;
                candidates.Add(new ArmyGroupRoleCandidate(i, theater, area, strength, IsPlayerGroup(group)));
            }
            return candidates;
        }

        private static int AnyAiGroupIndex(List<ArmyGroup> groups)
        {
            for (int i = 0; i < groups.Count; i++)
                if (!IsPlayerGroup(groups[i])) return i;
            return -1;
        }

        private static bool IsPlayerGroup(ArmyGroup group)
        {
            return DLC_WL.dlc_scenarioactive && group.GetCommanderID() == DLC_WL.dlc_chosencommander;
        }

        private static int FindReplacementId(
            List<GameVars.Commander> commanders,
            int allianceId,
            string replacementName,
            out ReplacementAppointment plan,
            out ArmyGroup oldGroup,
            out int successorId)
        {
            plan = ReplacementAppointment.Defer;
            oldGroup = null;
            successorId = -1;
            for (int i = 0; i < commanders.Count; i++)
            {
                var commander = commanders[i];
                if (commander == null || commander.alliance != allianceId) continue;
                if (!ArmyGroupDoctrine.CommanderNameMatches(allianceId, replacementName, commander.combinedname)) continue;
                if (DLC_WL.dlc_scenarioactive && i == DLC_WL.dlc_chosencommander) continue;

                bool pickable;
                try { pickable = commander.CanBePickedForAIReplacement(allianceId, includeofficerswithcommand: true); }
                catch { pickable = false; }

                ArmyGroup current = pickable ? null : GroupCommandedBy(i);
                int successor = (UnityEngine.Object)(object)current != (UnityEngine.Object)null
                    ? PickSuccessor(allianceId, i)
                    : -1;

                var decision = CommanderAssignmentGuard.ResolveReplacementAppointment(
                    pickable,
                    (UnityEngine.Object)(object)current != (UnityEngine.Object)null,
                    FreeApartFromArmyGroup(commander, allianceId, i),
                    successor >= 0);
                if (decision == ReplacementAppointment.Defer) continue;

                plan = decision;
                oldGroup = current;
                successorId = successor;
                return i;
            }
            return -1;
        }

        // CanBePickedForAIReplacement (60748) minus its army-group-commander test.
        private static bool FreeApartFromArmyGroup(GameVars.Commander commander, int allianceId, int commanderId)
        {
            try
            {
                if (!commander.IsAvailable()) return false;
                if (commander.IsServingUnderPlayer()) return false;
                if (commander.IsMemberOfStaff() || commander.IsMemberOfStaff(1)) return false;
                var alliance = GameVars.alliance[allianceId];
                return alliance.commanderinchief != commanderId;
            }
            catch { return false; }
        }

        private static ArmyGroup GroupCommandedBy(int commanderId)
        {
            var all = BattleUnits.armygroups;
            if (all == null) return null;
            for (int i = 0; i < all.Count; i++)
            {
                var group = all[i];
                if ((UnityEngine.Object)(object)group != (UnityEngine.Object)null && group.GetCommanderID() == commanderId)
                    return group;
            }
            return null;
        }

        // Best-rated officer from vanilla's own army-group pool (75546: available,
        // no command, not refreshing, rank suitable for level 18).
        private static int PickSuccessor(int allianceId, int excludeId)
        {
            List<int> pool;
            try { pool = ArmyGroup.GetAvailableArmyGroupCommanders(allianceId); }
            catch { return -1; }
            if (pool == null) return -1;

            int best = -1;
            float bestValue = float.MinValue;
            for (int i = 0; i < pool.Count; i++)
            {
                int id = pool[i];
                if (id == excludeId || id < 0 || id >= GameVars.commander.Count) continue;
                if (DLC_WL.dlc_scenarioactive && id == DLC_WL.dlc_chosencommander) continue;
                float value = GameVars.commander[id].GetAverageAttributeValue();
                if (value > bestValue)
                {
                    bestValue = value;
                    best = id;
                }
            }
            return best;
        }

        private static bool IsForceAllSuccessionEnabled()
        {
            try { return Plugin.Instance != null && Plugin.Instance.ForceAllSuccessionEvents.Value; }
            catch { return false; }
        }

        private static void Defer(SuccessionScheduler.Event e, int allianceId, string reason, string detail)
        {
            string key = "replace:defer:" + e.Id + ":" + reason;
            if (!LogOnceKeys.Add(key)) return;
            TelemetryRouter.LegacyInfo(
                $"[Succession:{e.Id}] action=defer alliance={allianceId} reason={reason} {detail}",
                TelemetryLayer.Campaign);
        }

        private static string SafeName(UnityEngine.Object obj)
        {
            try { return obj != null ? obj.name : "<unknown>"; }
            catch { return "<unknown>"; }
        }
    }
}
