using System;
using System.Collections;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #6 — Prefix on AICampaign.CheckAICommanderReplacements
    // (decompile line 17008). When SuccessionScheduler has fired but
    // un-applied events for this alliance, perform the scripted swap and skip
    // vanilla's competence-based pick. Otherwise let vanilla run normally.
    //
    // v0.2.1 swap mechanic (concrete, replacing v0.2.0's gate-only stub):
    //   1. Resolve replacement Commander by combinedname last-token + alliance.
    //   2. Find an army-group-level commander in the same alliance to displace
    //      (Commander.IsArmyGroupCommander() == true with non-null currentcommand).
    //   3. Apply Commander.AssignCommando(displaced.currentcommand, true, true)
    //      and BattleUnits.DoCommanderPromotion(replacementId).
    //   4. Mark applied via SuccessionScheduler.MarkApplied — persisted in sidecar
    //      so we don't re-apply on reload.
    //
    // If the replacement isn't in GameVars.commander[] (rare officer not yet on the
    // historical roster) OR no displaceable army-group commander exists, log and
    // skip — fall through to vanilla's normal pick.
    [HarmonyPatch(typeof(AICampaign), "CheckAICommanderReplacements")]
    internal static class CommanderReplacementPatch
    {
        private static readonly System.Collections.Generic.HashSet<string> LogOnceKeys =
            new System.Collections.Generic.HashSet<string>();

        [HarmonyPrefix]
        internal static bool Prefix(int _aifaction)
        {
            OnceLog.Info("replace", "CommanderReplacementPatch wired (v0.2.1 concrete swap)");
            try
            {
                if (StrategicCoordinator.Instance == null) return true;

                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0) return true;

                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return true;

                // Apply any pending scripted-event swaps. We collect first to
                // avoid mutating the AppliedEventIds set during enumeration.
                var pending = new System.Collections.Generic.List<SuccessionScheduler.Event>();
                foreach (var e in StrategicCoordinator.Instance.Succession.GetPendingApplications(allianceId))
                    pending.Add(e);

                bool anyApplied = false;
                foreach (var e in pending)
                {
                    if (TryApplyScriptedSwap(allianceId, e))
                    {
                        StrategicCoordinator.Instance.Succession.MarkApplied(e.Id);
                        anyApplied = true;
                    }
                    else
                    {
                        // Couldn't apply (no replacement / no displaceable commander).
                        // Don't mark applied — retry next tick. But avoid spam: log once per event id.
                        EmitSuccessionInfoOnce("replace:retry:" + e.Id,
                            $"[Succession:{e.Id}] action=defer alliance={allianceId} reason=preconditions-unmet");
                    }
                }

                // If we applied a scripted swap, skip vanilla's competence-based
                // pick this tick — vanilla would otherwise look at the same
                // worstcommanderidperlevel slots we just consumed.
                return !anyApplied;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Patch:Replace] " + ex.Message);
                return true;
            }
        }

        // Returns true on successful application; false if we couldn't find
        // either the replacement commander or a displaceable army-group commander.
        private static bool TryApplyScriptedSwap(int allianceId, SuccessionScheduler.Event e)
        {
            try
            {
                // GameVars.commander is List<Commander>; reflect it.
                var gvType = AccessTools.TypeByName("GameVars");
                var commandersField = AccessTools.Field(gvType, "commander");
                var commanders = commandersField?.GetValue(null) as IList;
                if (commanders == null) return false;

                int replacementId = FindCommanderIdByLastName(commanders, e.ReplacementName, allianceId);
                if (replacementId < 0)
                {
                    EmitSuccessionInfoOnce($"replace:noreplacement:{e.Id}",
                        $"[Succession:{e.Id}] action=defer alliance={allianceId} reason=no-replacement replacement={e.ReplacementName}");
                    return false;
                }

                int displaceId = FindDisplaceableArmyGroupCommanderId(commanders, allianceId, replacementId);
                if (displaceId < 0 && Plugin.Instance.ForceAllSuccessionEvents.Value)
                {
                    // Test-mode fallback: when no army-group commander exists yet
                    // (typical early in a campaign before BattleUnits.armygroups
                    // gets populated), displace ANY commander with a currentcommand
                    // so the swap mechanic actually exercises end-to-end. NOT used
                    // in normal play.
                    displaceId = FindAnyDisplaceableCommanderId(commanders, allianceId, replacementId);
                    if (displaceId >= 0)
                        TelemetryRouter.LegacyInfo($"[Succession:{e.Id}] action=test-fallback alliance={allianceId} reason=no-agc displaceId={displaceId}", TelemetryLayer.Campaign);
                }
                if (displaceId < 0)
                {
                    // Common in early campaign. OnceLog'd to prevent per-tick spam.
                    EmitSuccessionInfoOnce($"replace:noagc:{e.Id}",
                        $"[Succession:{e.Id}] action=defer alliance={allianceId} reason=no-agc");
                    return false;
                }

                var replacement = commanders[replacementId];
                var displaced   = commanders[displaceId];

                var cmdType = replacement.GetType();
                var ccField = AccessTools.Field(cmdType, "currentcommand");
                var displacedCommand = ccField?.GetValue(displaced);
                if (displacedCommand == null) return false;

                // Resolve Regiment type for AssignCommando lookup.
                var regimentType = AccessTools.TypeByName("Regiment");
                if (regimentType == null) return false;
                var assignCommando = AccessTools.Method(cmdType, "AssignCommando",
                    new[] { regimentType, typeof(bool), typeof(bool) });
                if (assignCommando == null) return false;

                assignCommando.Invoke(replacement, new object[] { displacedCommand, true, true });

                var battleUnitsType = AccessTools.TypeByName("BattleUnits");
                var doPromotion = AccessTools.Method(battleUnitsType, "DoCommanderPromotion", new[] { typeof(int) });
                doPromotion?.Invoke(null, new object[] { replacementId });

                // Vanilla also sets refreshcommand = true post-swap — match the pattern.
                AccessTools.Field(cmdType, "refreshcommand")?.SetValue(replacement, true);

                TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "Succession", TelemetrySeverity.Info, ev => ev
                    .WithAlliance(allianceId)
                    .WithDecision("applied", e.ReplacedRole, "alliance=" + allianceId + "|event=" + e.Id + "|replacement=" + replacementId + "|displaced=" + displaceId)
                    .WithField("eventId", e.Id)
                    .WithField("replacement", e.ReplacementName)
                    .WithField("replacementId", replacementId)
                    .WithField("displacedId", displaceId)
                    .WithField("role", e.ReplacedRole));
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Succession:{e.Id}] swap failed: {ex.Message}");
                return false;
            }
        }

        private static void EmitSuccessionInfoOnce(string key, string line)
        {
            if (!LogOnceKeys.Add(key ?? string.Empty)) return;
            TelemetryRouter.LegacyInfo(line, TelemetryLayer.Campaign);
        }

        private static int FindCommanderIdByLastName(IList commanders, string lastNameLower, int allianceId)
        {
            for (int i = 0; i < commanders.Count; i++)
            {
                var cmd = commanders[i];
                if (cmd == null) continue;
                var cmdType = cmd.GetType();
                var allianceField = AccessTools.Field(cmdType, "alliance");
                if (allianceField == null || (int)allianceField.GetValue(cmd) != allianceId) continue;

                var combined = AccessTools.Field(cmdType, "combinedname")?.GetValue(cmd) as string;
                if (string.IsNullOrWhiteSpace(combined)) continue;

                var trimmed = combined.Trim();
                var space = trimmed.LastIndexOf(' ');
                var last = (space >= 0) ? trimmed.Substring(space + 1) : trimmed;
                if (last.ToLowerInvariant() == lastNameLower)
                    return i;
            }
            return -1;
        }

        private static int FindDisplaceableArmyGroupCommanderId(IList commanders, int allianceId, int skipId)
        {
            for (int i = 0; i < commanders.Count; i++)
            {
                if (i == skipId) continue;
                var cmd = commanders[i];
                if (cmd == null) continue;
                var cmdType = cmd.GetType();
                var allianceField = AccessTools.Field(cmdType, "alliance");
                if (allianceField == null || (int)allianceField.GetValue(cmd) != allianceId) continue;

                var ccField = AccessTools.Field(cmdType, "currentcommand");
                if (ccField?.GetValue(cmd) == null) continue;

                var isAGC = AccessTools.Method(cmdType, "IsArmyGroupCommander");
                if (isAGC == null) continue;
                if ((bool)isAGC.Invoke(cmd, null)) return i;
            }
            return -1;
        }

        // Test-mode-only fallback: any commander in this alliance with a
        // non-null currentcommand. Lets the swap mechanic exercise without
        // waiting for the AI to populate BattleUnits.armygroups.
        private static int FindAnyDisplaceableCommanderId(IList commanders, int allianceId, int skipId)
        {
            for (int i = 0; i < commanders.Count; i++)
            {
                if (i == skipId) continue;
                var cmd = commanders[i];
                if (cmd == null) continue;
                var cmdType = cmd.GetType();
                var allianceField = AccessTools.Field(cmdType, "alliance");
                if (allianceField == null || (int)allianceField.GetValue(cmd) != allianceId) continue;

                var ccField = AccessTools.Field(cmdType, "currentcommand");
                if (ccField?.GetValue(cmd) == null) continue;

                return i;
            }
            return -1;
        }
    }
}
