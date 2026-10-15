using System;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
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
    // uses the campaign bypass from this patch. The call and the dedupe state go
    // through TacticalPlayerDispatchRuntime (battle-core D9), the emitter the
    // executor's ancestor dispatches share, so one dedupe covers both.
    [HarmonyPatch(typeof(AIBattle), "UpdateDLCPlayerOrders")]
    internal static class PlayerSubordinateOrderPatch
    {
        private static readonly PlayerOrderRuntimeAdapter Runtime = new PlayerOrderRuntimeAdapter(TacticalPlayerDispatchRuntime.Dedupe);
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
            using (TelemetryPerf.Scope("tactical.patch.player-subordinate-order-prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-subordinate-order-prefix");
                __state = default(PatchState);
                if (!MasterEnabled()) return;

                try
                {
                    __state = new PatchState(Runtime.Snapshot(__instance, forceVanillaProvenance: false));
                }
                catch (Exception ex)
                {
                    Warn("player-order-patch:prefix", "prefix", ex);
                }
            }
        }

        private static void Postfix(AIBattle __instance, PatchState __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.player-subordinate-order", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-subordinate-order");
            try
            {
                if (!MasterEnabled()) return;

                bool diagnosticsEnabled = Plugin.EnablePlayerOrderDoctrineDiagnostics == null ||
                    Plugin.EnablePlayerOrderDoctrineDiagnostics.Value;
                bool doctrineEnabled = Plugin.EnablePlayerOrderDoctrine != null &&
                    Plugin.EnablePlayerOrderDoctrine.Value;
                bool modeAllowsWrites = TacticalWriteGuard.ModeAllowsWrites();
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
                    TacticalPlayerDispatchRuntime.Dedupe,
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

                bool queued = TacticalPlayerDispatchRuntime.Emit(
                    evaluation.CurrentCommand,
                    candidate.VanillaType,
                    candidate.TargetPoint.X,
                    candidate.TargetPoint.Z,
                    candidate.Rotation,
                    DestinationName(candidate),
                    OrderOrigin.P62);

                // The throttle and the signature suppress a repeat while the order is on its way.
                TacticalPlayerDispatchRuntime.Dedupe.RecordAttempt(candidate, evaluation.Tick);
                if (diagnosticsEnabled)
                    Log(queued ? "issue" : "suppress", queued ? decision.Reason : "queue-refused", candidate, evaluation.ActiveOrder);
            }
            catch (Exception ex)
            {
                Warn("player-order-patch:postfix", "postfix", ex);
            }
            } // end using TelemetryPerf.Scope
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
                TacticalPlayerDispatchRuntime.Dedupe.ClearForPlayerCommandChange();
                TacticalPlayerDispatchRuntime.Dedupe.ClearForBattleBoundary(snapshot.BattleIdentity);
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

        private static void Warn(string onceKey, string stage, Exception ex)
        {
            try
            {
                OnceLog.Warning(
                    onceKey,
                    "[PlayerOrderIntent] patch " + stage + " failed: " + ex.GetType().Name + " " + ex.Message);
            }
            catch { }
        }
    }
}
