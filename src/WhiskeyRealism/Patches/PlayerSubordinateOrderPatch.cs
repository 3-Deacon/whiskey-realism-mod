using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.UpdateDLCPlayerOrders runs on tactical micro-AI cycle 28
    // and may self-issue W&L current-order transitions through types 13, 14,
    // and 15 during CheckRemovalOfOrders. Whiskey snapshots before/after that
    // vanilla cadence, yields to vanilla transitions, and only asks the static
    // AIBattle.CheckCurrentOrderUpdate bridge for tactical orders; it never
    // uses the campaign bypass from this patch.
    [HarmonyPatch(typeof(AIBattle), "UpdateDLCPlayerOrders")]
    internal static class PlayerSubordinateOrderPatch
    {
        private static readonly PlayerOrderDedupeState DedupeState = new PlayerOrderDedupeState();
        private static readonly PlayerOrderRuntimeAdapter Runtime = new PlayerOrderRuntimeAdapter(DedupeState);
        private static readonly PlayerOrderDiagnostics Diagnostics = new PlayerOrderDiagnostics(128);
        private static string _lastBattleIdentity = string.Empty;
        private static string _lastCurrentCommandKey = string.Empty;
        private static bool _lastCicStatus;
        private static bool _hasBoundary;

        internal readonly struct PatchState
        {
            public PatchState(PlayerOrderRuntimeAdapter.RuntimeSnapshot before)
            {
                Before = before;
            }

            public PlayerOrderRuntimeAdapter.RuntimeSnapshot Before { get; }
        }

        private static void Prefix(AIBattle __instance, out PatchState __state)
        {
            __state = default(PatchState);
            if (!MasterEnabled()) return;

            try
            {
                __state = new PatchState(Runtime.Snapshot(__instance, forceVanillaProvenance: false));
            }
            catch (Exception ex)
            {
                Warn("prefix", ex);
            }
        }

        private static void Postfix(AIBattle __instance, PatchState __state)
        {
            try
            {
                if (!MasterEnabled()) return;

                bool diagnosticsEnabled = Plugin.EnablePlayerOrderDoctrineDiagnostics == null ||
                    Plugin.EnablePlayerOrderDoctrineDiagnostics.Value;
                bool doctrineEnabled = Plugin.EnablePlayerOrderDoctrine != null &&
                    Plugin.EnablePlayerOrderDoctrine.Value;
                bool modeAllowsWrites = TacticalCommanderModePolicy.AllowsWrites(Plugin.Instance.TacticalCommanderModeValue);
                bool writesEnabled = doctrineEnabled && modeAllowsWrites;
                if (!diagnosticsEnabled && !writesEnabled) return;

                var afterProbe = Runtime.Snapshot(__instance, forceVanillaProvenance: false);
                bool vanillaChanged = ActiveChanged(__state.Before.ActiveOrder, afterProbe.ActiveOrder);
                ApplyBoundary(afterProbe);

                var evaluation = Runtime.Evaluate(__instance, forceVanillaProvenance: vanillaChanged);
                if (!evaluation.CanEvaluate)
                {
                    if (diagnosticsEnabled) Log("skip", evaluation.SkipReason, evaluation.Candidate, evaluation.ActiveOrder);
                    return;
                }

                PlayerOrderCandidate candidate = evaluation.Candidate;
                if (!candidate.HasCandidate)
                {
                    if (diagnosticsEnabled) Log("skip", "no-candidate", candidate, evaluation.ActiveOrder);
                    return;
                }

                PlayerOrderDedupeDecision decision = PlayerOrderDedupe.Preview(
                    candidate,
                    evaluation.ActiveOrder,
                    DedupeState,
                    throttleTicks: 120,
                    evaluation.Tick);

                if (!decision.ShouldIssue)
                {
                    if (diagnosticsEnabled) Log("suppress", decision.Reason, candidate, evaluation.ActiveOrder);
                    return;
                }

                if (!writesEnabled)
                {
                    if (diagnosticsEnabled) Log("suppress", "writes-disabled", candidate, evaluation.ActiveOrder);
                    return;
                }

                AIBattle.CheckCurrentOrderUpdate(
                    evaluation.CurrentCommand,
                    candidate.VanillaType,
                    new Vector3(candidate.TargetPoint.X, 0f, candidate.TargetPoint.Z),
                    DestinationName(candidate),
                    candidate.Rotation,
                    100f,
                    50f);

                var afterIssue = Runtime.Snapshot(__instance, forceVanillaProvenance: false).ActiveOrder;
                if (Runtime.ActiveMatchesIssued(candidate, evaluation.ActiveOrder, afterIssue))
                {
                    DedupeState.RecordAccepted(
                        Runtime.WithSession(candidate, afterIssue.GivenOrderSession),
                        Runtime.WithAcceptedOrder(candidate, afterIssue),
                        evaluation.Tick);
                    if (diagnosticsEnabled) Log("issue", decision.Reason, candidate, afterIssue);
                }
                else if (diagnosticsEnabled)
                {
                    DedupeState.RecordAttempt(candidate, evaluation.Tick);
                    Log("suppress", "vanilla-bridge-no-match", candidate, afterIssue);
                }
            }
            catch (Exception ex)
            {
                Warn("postfix", ex);
            }
        }

        private static bool MasterEnabled()
        {
            return Plugin.Instance != null && Plugin.Instance.Enabled != null && Plugin.Instance.Enabled.Value;
        }

        private static void ApplyBoundary(PlayerOrderRuntimeAdapter.RuntimeSnapshot snapshot)
        {
            if (!_hasBoundary)
            {
                _hasBoundary = true;
                _lastBattleIdentity = snapshot.BattleIdentity;
                _lastCurrentCommandKey = snapshot.CurrentCommandKey;
                _lastCicStatus = snapshot.IsCommanderInChief;
                return;
            }

            if (!string.Equals(_lastBattleIdentity, snapshot.BattleIdentity, StringComparison.Ordinal) ||
                !string.Equals(_lastCurrentCommandKey, snapshot.CurrentCommandKey, StringComparison.Ordinal) ||
                _lastCicStatus != snapshot.IsCommanderInChief)
            {
                DedupeState.ClearForPlayerCommandChange();
                DedupeState.ClearForBattleBoundary(snapshot.BattleIdentity);
                _lastBattleIdentity = snapshot.BattleIdentity;
                _lastCurrentCommandKey = snapshot.CurrentCommandKey;
                _lastCicStatus = snapshot.IsCommanderInChief;
            }
        }

        private static bool ActiveChanged(PlayerOrderActiveSnapshot before, PlayerOrderActiveSnapshot after)
        {
            if (before.HasActiveOrder != after.HasActiveOrder) return true;
            if (!before.HasActiveOrder && !after.HasActiveOrder) return false;
            if (before.VanillaType != after.VanillaType) return true;
            if (before.GivenOrderSession != after.GivenOrderSession) return true;
            if (!string.Equals(before.UnitKey, after.UnitKey, StringComparison.Ordinal)) return true;
            if (Math.Abs(before.TargetPoint.X - after.TargetPoint.X) > 0.1f) return true;
            return Math.Abs(before.TargetPoint.Z - after.TargetPoint.Z) > 0.1f;
        }

        private static string DestinationName(PlayerOrderCandidate candidate)
        {
            if (candidate.Intent == PlayerOrderIntent.RetreatToExit) return string.Empty;
            return string.IsNullOrWhiteSpace(candidate.ObjectiveKey) ? "Objective" : candidate.ObjectiveKey;
        }

        private static void Log(
            string action,
            string reason,
            PlayerOrderCandidate candidate,
            PlayerOrderActiveSnapshot active)
        {
            try
            {
                string unit = candidate.HasCandidate ? candidate.UnitKey : active.UnitKey;
                string signature = action + "|" + reason + "|" + unit + "|" +
                    (candidate.HasCandidate ? candidate.VanillaType : active.VanillaType).ToString() + "|" +
                    (candidate.HasCandidate ? candidate.Intent.ToString() : "none");
                if (!Diagnostics.ShouldLog(unit, signature)) return;

                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Decision,
                    "TacticalPlayerOrder",
                    TelemetrySeverity.Info,
                    ev => ev
                        .WithUnit(string.IsNullOrEmpty(unit) ? "unknown" : unit)
                        .WithDecision(action, reason, signature)
                        .WithField("scope", "tactical")
                        .WithField("confidence", 1.0)
                        .WithField("score", candidate.HasCandidate ? candidate.Priority : active.Priority)
                        .WithField("selectedTarget", Target(candidate))
                        .WithField("gateResult", action)
                        .WithField("gateReason", reason)
                        .WithField("writeAction", "CheckCurrentOrderUpdate")
                        .WithField("writeResult", action)
                        .WithField("type", (candidate.HasCandidate ? candidate.VanillaType : active.VanillaType).ToString())
                        .WithField("intent", candidate.HasCandidate ? candidate.Intent.ToString() : "None")
                        .WithField("priority", candidate.HasCandidate ? candidate.Priority : active.Priority)
                        .WithField("target", Target(candidate)));
            }
            catch { }
        }

        private static string Target(PlayerOrderCandidate candidate)
        {
            if (!candidate.HasCandidate) return "none";
            return candidate.TargetPoint.X.ToString("F0") + "," + candidate.TargetPoint.Z.ToString("F0");
        }

        private static void Warn(string key, Exception ex)
        {
            try
            {
                OnceLog.Warning(
                    "player-order-patch:" + key,
                    "[PlayerOrderIntent] patch " + key + " failed: " + ex.GetType().Name + " " + ex.Message);
            }
            catch { }
        }
    }
}
