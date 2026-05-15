using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Telemetry;
#if !NET8_0
using WhiskeyRealism.Util;
#endif

namespace WhiskeyRealism.Strategic
{
    internal static class CoordinatedOperationRuntime
    {
        private sealed class UnitCommitPlan
        {
            internal int StableUnitId;
            internal Regiment Unit;
            internal WlStrategicIntent Intent;
            internal WlStrategicOrderDecision Decision;
        }

        private sealed class DirectCommitRecord
        {
            internal Regiment Unit;
            internal int StableUnitId;
            internal bool WasInOffensive;
            internal bool HadDefensiveMovingOrder;
            internal int RegimentPathsBefore;
        }

        private static readonly Dictionary<int, string> _packageLockByUnitId =
            new Dictionary<int, string>();

        internal static void MarkPackageLocked(int stableUnitId, string packageSignature)
        {
            if (stableUnitId == 0 || string.IsNullOrEmpty(packageSignature)) return;
            _packageLockByUnitId[stableUnitId] = packageSignature;
        }

        internal static bool IsPackageLocked(Regiment unit)
        {
            if (unit == null) return false;
            int id = ((UnityEngine.Object)unit).GetInstanceID();
            return _packageLockByUnitId.ContainsKey(id) && unit.regimentpaths > 0;
        }

        internal static void ClearPackageLock(Regiment unit)
        {
            if (unit == null) return;
            _packageLockByUnitId.Remove(((UnityEngine.Object)unit).GetInstanceID());
        }

        internal static CoordinatedCommitMode CommitModeFromBridge(WlStrategicOrderDecision decision)
        {
            if (decision.Result == WlStrategicOrderResult.IssuedWlCurrentOrder)
                return CoordinatedCommitMode.WlCurrentOrder;
            if (decision.MayDirectMove)
                return CoordinatedCommitMode.DirectMovement;
            return CoordinatedCommitMode.BlockedWlPlayerChain;
        }

        internal static CoordinatedOperationCandidate CandidateFromAssignment(
            FormationDirectiveAssignment assignment,
            bool inOffensive,
            bool inDefensive,
            bool constructingSupplyDepot,
            CoordinatedCommitMode commitMode)
        {
            if (assignment == null) return null;
            return new CoordinatedOperationCandidate
            {
                StableUnitId = assignment.StableUnitId,
                DisplayUnitKey = assignment.UnitKey,
                AllianceId = assignment.AllianceId,
                Level = assignment.Level,
                Directive = assignment.Directive,
                AreaKey = assignment.AreaKey,
                SectorKey = assignment.SectorKey,
                X = assignment.X,
                Z = assignment.Z,
                CombatAvailability = assignment.CombatAvailability,
                ExchangePressure = assignment.ExchangePressure,
                LocalFriendlySupport = assignment.LocalFriendlySupport,
                LocalEnemyStrength = assignment.LocalEnemyStrength,
                Readiness = assignment.Readiness,
                Morale = assignment.Morale,
                Ammo = assignment.Ammo,
                Supply = assignment.Supply,
                Fatigue = assignment.Fatigue,
                OffensiveAllowed = assignment.OffensiveAllowed ||
                    assignment.Directive == FormationDirective.Probe ||
                    assignment.Directive == FormationDirective.Counterstroke,
                DefensiveAllowed = assignment.DefensiveAllowed,
                TransferDonorAllowed = assignment.TransferDonorAllowed,
                DirectMovementAllowed = assignment.DirectMovementAllowed,
                InheritsFromParent = assignment.InheritsFromParent,
                CriticalSector = false,
                FrontPosture = FrontPosture.Counterstroke,
                InOffensiveOperation = inOffensive,
                InDefensiveOperation = inDefensive,
                ConstructingSupplyDepot = constructingSupplyDepot,
                CommitMode = commitMode
            };
        }

        internal static string ResolveTargetName(int objectiveId, string fallbackAreaKey, CampaignMapLedger map, Vector3 target)
        {
            string objectiveName = ResolveObjectiveName(objectiveId);
            if (!string.IsNullOrEmpty(objectiveName)) return objectiveName;
            string nearest = NearestMapName(map, target);
            if (!string.IsNullOrEmpty(nearest)) return nearest;
            if (!string.IsNullOrEmpty(fallbackAreaKey)) return fallbackAreaKey;
            return "Objective";
        }

        internal static string NearestMapName(CampaignMapLedger map, Vector3 target)
        {
            if (map == null) return null;
            string bestName = null;
            float best = float.MaxValue;
            foreach (var town in map.Towns)
                Consider(town.CityName, town.X, town.Z, target, ref bestName, ref best);
            foreach (var asset in map.Assets)
                Consider(asset.Name, asset.X, asset.Z, target, ref bestName, ref best);
            return bestName;
        }

        internal static bool CommitPackage(
            int allianceId,
            int aifactionIndex,
            CoordinatedOperationOutput output,
            Vector3 target,
            string targetName,
            int objectiveId,
            WlStrategicIntent intent,
            string sourceSystem)
        {
            using (TelemetryPerf.Scope("campaign.coordinated-operations", TelemetryLayer.Campaign, TelemetryCategory.Performance, 4.0))
            {
                try
                {
                    if (output == null ||
                        output.Decision == CoordinatedOperationDecision.None ||
                        output.Decision == CoordinatedOperationDecision.Delay ||
                        output.Decision == CoordinatedOperationDecision.Recover)
                        return false;
                    var faction = AICampaignReflect.GetFaction(aifactionIndex);
                    if (faction == null) return false;
                    var ownUnits = AccessTools.Field(faction.GetType(), "ownunits")?.GetValue(faction) as IList;
                    var offensive = AccessTools.Field(faction.GetType(), "unitsinoffensiveoperations")?.GetValue(faction) as IList;
                    if (ownUnits == null || offensive == null) return false;

                    var plans = BuildCommitPlans(
                        allianceId,
                        aifactionIndex,
                        ownUnits,
                        output,
                        target,
                        targetName,
                        objectiveId,
                        intent,
                        sourceSystem);
                    if (plans == null || plans.Count == 0) return false;

                    int committedCount = 0;
                    var directRecords = new List<DirectCommitRecord>();
                    for (int i = 0; i < plans.Count; i++)
                    {
                        if (CommitUnit(allianceId, aifactionIndex, offensive, plans[i], target, targetName, objectiveId, sourceSystem, output.Signature(), directRecords))
                            committedCount++;
                    }
                    if (committedCount > 0 && committedCount < plans.Count)
                    {
                        RollBackDirectCommits(offensive, directRecords, output.Signature());
                        EmitCoordinatedOps(
                            TelemetryCategory.Write,
                            TelemetrySeverity.Warning,
                            allianceId,
                            "package-partial-rollback",
                            "partial-commit",
                            output.Signature(),
                            ev => ev
                                .WithField("committed", committedCount)
                                .WithField("planned", plans.Count));
                    }
                    return committedCount == plans.Count;
                }
                catch (Exception ex)
                {
                    EmitCoordinatedOps(
                        TelemetryCategory.Failure,
                        TelemetrySeverity.Warning,
                        allianceId,
                        "commit-failed",
                        ex.Message,
                        output?.Signature() ?? "-");
                    WarnOnce("coordinated-ops:commit", "Runtime CoordinatedOps commit failed: " + ex.Message);
                    return false;
                }
            }
        }

        private static List<UnitCommitPlan> BuildCommitPlans(
            int allianceId,
            int aifactionIndex,
            IList ownUnits,
            CoordinatedOperationOutput output,
            Vector3 target,
            string targetName,
            int objectiveId,
            WlStrategicIntent intent,
            string sourceSystem)
        {
            var plans = new List<UnitCommitPlan>();
            if (!AddCommitPlan(
                    allianceId,
                    aifactionIndex,
                    ownUnits,
                    plans,
                    output.LeadStableUnitId,
                    target,
                    targetName,
                    objectiveId,
                    intent,
                    sourceSystem,
                    output.Signature()))
                return null;

            for (int i = 0; i < output.SupportStableUnitIds.Count; i++)
            {
                var supportIntent = output.Decision == CoordinatedOperationDecision.Reinforce
                    ? WlStrategicIntent.Reinforce
                    : intent;
                if (!AddCommitPlan(
                        allianceId,
                        aifactionIndex,
                        ownUnits,
                        plans,
                        output.SupportStableUnitIds[i],
                        target,
                        targetName,
                        objectiveId,
                        supportIntent,
                        sourceSystem,
                        output.Signature()))
                    return null;
            }
            int wlCurrentOrders = 0;
            for (int i = 0; i < plans.Count; i++)
            {
                if (plans[i].Decision.Result == WlStrategicOrderResult.IssuedWlCurrentOrder)
                    wlCurrentOrders++;
            }
            if (wlCurrentOrders > 1 || (wlCurrentOrders == 1 && plans.Count > 1))
            {
                EmitCoordinatedOps(
                    TelemetryCategory.Gate,
                    TelemetrySeverity.Warning,
                    allianceId,
                    "preflight-failed",
                    "wl-current-order-not-atomic",
                    output.Signature());
                return null;
            }
            return plans;
        }

        private static bool AddCommitPlan(
            int allianceId,
            int aifactionIndex,
            IList ownUnits,
            List<UnitCommitPlan> plans,
            int stableUnitId,
            Vector3 target,
            string targetName,
            int objectiveId,
            WlStrategicIntent intent,
            string sourceSystem,
            string packageSignature)
        {
            var unit = FindUnitById(ownUnits, stableUnitId);
            if (unit == null)
            {
                EmitCoordinatedOps(
                    TelemetryCategory.Gate,
                    TelemetrySeverity.Warning,
                    allianceId,
                    "preflight-failed",
                    "unit-unresolved",
                    packageSignature,
                    ev => ev.WithField("unitId", stableUnitId));
                return false;
            }
            if (!IsAvailable(aifactionIndex, unit, target))
            {
                EmitCoordinatedOps(
                    TelemetryCategory.Gate,
                    TelemetrySeverity.Warning,
                    allianceId,
                    "preflight-failed",
                    "availability",
                    packageSignature,
                    ev => ev
                        .WithUnit(SafeName(unit))
                        .WithField("unit", SafeName(unit)));
                return false;
            }

            var decision = WlStrategicOrderBridge.ClassifyOnly(new WlStrategicOrderRequest
            {
                AllianceId = allianceId,
                AifactionIndex = aifactionIndex,
                Unit = unit,
                TargetPosition = target,
                TargetName = string.IsNullOrEmpty(targetName) ? "Objective" : targetName,
                ObjectiveId = objectiveId,
                Intent = intent,
                Width = 20f,
                Depth = 20f,
                SourceSystem = sourceSystem
            });
            if (decision.Result != WlStrategicOrderResult.IssuedWlCurrentOrder && !decision.MayDirectMove)
            {
                EmitCoordinatedOps(
                    TelemetryCategory.Gate,
                    TelemetrySeverity.Warning,
                    allianceId,
                    "preflight-failed",
                    decision.Reason,
                    packageSignature,
                    ev => ev
                        .WithUnit(SafeName(unit))
                        .WithField("unit", SafeName(unit))
                        .WithField("wlResult", decision.Result.ToString()));
                return false;
            }

            plans.Add(new UnitCommitPlan
            {
                StableUnitId = stableUnitId,
                Unit = unit,
                Intent = intent,
                Decision = decision
            });
            return true;
        }

        private static bool CommitUnit(
            int allianceId,
            int aifactionIndex,
            IList offensive,
            UnitCommitPlan plan,
            Vector3 target,
            string targetName,
            int objectiveId,
            string sourceSystem,
            string packageSignature,
            List<DirectCommitRecord> directRecords)
        {
            var unit = plan.Unit;
            if (unit == null) return false;

            if (plan.Decision.Result == WlStrategicOrderResult.IssuedWlCurrentOrder)
            {
                var decision = WlStrategicOrderBridge.TryIssue(new WlStrategicOrderRequest
                {
                    AllianceId = allianceId,
                    AifactionIndex = aifactionIndex,
                    Unit = unit,
                    TargetPosition = target,
                    TargetName = string.IsNullOrEmpty(targetName) ? "Objective" : targetName,
                    ObjectiveId = objectiveId,
                    Intent = plan.Intent,
                    Width = 20f,
                    Depth = 20f,
                    SourceSystem = sourceSystem
                });
                if (decision.Result != WlStrategicOrderResult.IssuedWlCurrentOrder)
                {
                    EmitCoordinatedOps(
                        TelemetryCategory.Gate,
                        TelemetrySeverity.Warning,
                        allianceId,
                        "skip",
                        decision.Reason,
                        packageSignature,
                        ev => ev
                            .WithUnit(SafeName(unit))
                            .WithField("unit", SafeName(unit))
                            .WithField("wlResult", decision.Result.ToString()));
                    return false;
                }
                MarkPackageLocked(plan.StableUnitId, packageSignature);
                EmitCoordinatedOps(
                    TelemetryCategory.Write,
                    TelemetrySeverity.Info,
                    allianceId,
                    "wl-current-order",
                    decision.WlOrderType.ToString(),
                    packageSignature,
                    ev => ev
                        .WithUnit(SafeName(unit))
                        .WithField("unit", SafeName(unit))
                        .WithField("type", decision.WlOrderType.ToString()));
                return true;
            }

            bool wasInOffensive = offensive.Contains(unit);
            bool hadDefensiveMovingOrder = AICampaign.DefensiveMovingOrder.OrderRunning(unit);
            int regimentPathsBefore = unit.regimentpaths;
            if (AICampaign.MoveUnitTo(unit, target, true))
            {
                if (!offensive.Contains(unit))
                    offensive.Add(unit);
                directRecords?.Add(new DirectCommitRecord
                {
                    Unit = unit,
                    StableUnitId = plan.StableUnitId,
                    WasInOffensive = wasInOffensive,
                    HadDefensiveMovingOrder = hadDefensiveMovingOrder,
                    RegimentPathsBefore = regimentPathsBefore
                });
                MarkPackageLocked(plan.StableUnitId, packageSignature);
                EmitCoordinatedOps(
                    TelemetryCategory.Write,
                    TelemetrySeverity.Info,
                    allianceId,
                    "direct-move",
                    "move-issued",
                    packageSignature,
                    ev => ev
                        .WithUnit(SafeName(unit))
                        .WithField("unit", SafeName(unit)));
                return true;
            }

            EmitCoordinatedOps(
                TelemetryCategory.Gate,
                TelemetrySeverity.Warning,
                allianceId,
                "skip",
                "move-failed",
                packageSignature,
                ev => ev
                    .WithUnit(SafeName(unit))
                    .WithField("unit", SafeName(unit)));
            return false;
        }

        private static void RollBackDirectCommits(IList offensive, List<DirectCommitRecord> records, string packageSignature)
        {
            if (offensive == null || records == null || records.Count == 0) return;
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                ClearPackageLock(record.Unit);
                if (!record.HadDefensiveMovingOrder)
                    AICampaign.DefensiveMovingOrder.RemoveOrder(record.Unit);
                if (record.RegimentPathsBefore <= 0 && record.Unit != null && record.Unit.regimentpaths > 0)
                    record.Unit.StopRegiment(skipfinalrotation: true, manualstop: true);
                if (!record.WasInOffensive)
                    offensive.Remove(record.Unit);
                EmitCoordinatedOps(
                    TelemetryCategory.Write,
                    TelemetrySeverity.Warning,
                    -1,
                    "direct-rollback",
                    "partial-commit-rollback",
                    packageSignature,
                    ev => ev
                        .WithUnit(SafeName(record.Unit))
                        .WithField("unit", SafeName(record.Unit))
                        .WithField("unitId", record.StableUnitId));
            }
        }

        internal static Regiment FindUnitById(IList ownUnits, int stableUnitId)
        {
            if (ownUnits == null || stableUnitId == 0) return null;
            for (int i = 0; i < ownUnits.Count; i++)
            {
                var unit = ownUnits[i] as Regiment;
                if (unit == null) continue;
                if (((UnityEngine.Object)unit).GetInstanceID() == stableUnitId) return unit;
            }
            return null;
        }

        private static void Consider(string name, float x, float z, Vector3 target, ref string bestName, ref float best)
        {
            if (string.IsNullOrEmpty(name)) return;
            float dx = x - target.x;
            float dz = z - target.z;
            float d = dx * dx + dz * dz;
            if (d < best)
            {
                best = d;
                bestName = name;
            }
        }

        private static string SafeName(UnityEngine.Object obj)
        {
            try { return obj != null ? obj.name : "<unknown>"; }
            catch { return "<unknown>"; }
        }

        private static string ResolveObjectiveName(int objectiveId)
        {
#if NET8_0
            try
            {
                var type = typeof(CoordinatedOperationRuntime).Assembly.GetType("WhiskeyRealism.Strategic.ObjectiveAdapter");
                var method = AccessTools.Method(type, "ResolveObjectiveName", new[] { typeof(int) });
                return method?.Invoke(null, new object[] { objectiveId }) as string;
            }
            catch (Exception ex)
            {
                EmitCoordinatedOps(
                    TelemetryCategory.Failure,
                    TelemetrySeverity.Warning,
                    -1,
                    "objective-name-failed",
                    ex.Message,
                    "objective=" + objectiveId);
                WarnOnce(
                    "coordinated-ops:objective-name",
                    $"Runtime CoordinatedOps objective name resolve failed for objective ID {objectiveId}: {ex.Message}");
                return null;
            }
#else
            return ObjectiveAdapter.ResolveObjectiveName(objectiveId);
#endif
        }

        private static bool IsAvailable(int aifactionIndex, Regiment unit, Vector3 target)
        {
#if NET8_0
            try
            {
                var type = typeof(CoordinatedOperationRuntime).Assembly.GetType("WhiskeyRealism.Strategic.OffensiveAvailabilityWrapper");
                var method = AccessTools.Method(type, "IsAvailable", new[] { typeof(int), typeof(Regiment), typeof(Vector3) });
                if (method == null) return false;
                return Convert.ToBoolean(method.Invoke(null, new object[] { aifactionIndex, unit, target }));
            }
            catch (Exception ex)
            {
                EmitCoordinatedOps(
                    TelemetryCategory.Failure,
                    TelemetrySeverity.Warning,
                    -1,
                    "availability-check-failed",
                    ex.Message,
                    "aifaction=" + aifactionIndex);
                WarnOnce("coordinated-ops:availability", "Runtime CoordinatedOps availability check failed: " + ex.Message);
                return false;
            }
#else
            return OffensiveAvailabilityWrapper.IsAvailable(aifactionIndex, unit, target);
#endif
        }

        private static void WarnOnce(string key, string message)
        {
#if NET8_0
            try
            {
                var type = typeof(CoordinatedOperationRuntime).Assembly.GetType("WhiskeyRealism.Util.OnceLog");
                var method = AccessTools.Method(type, "Warning", new[] { typeof(string), typeof(string) });
                if (method != null)
                {
                    method.Invoke(null, new object[] { key, message });
                    return;
                }
            }
            catch
            {
            }

            try
            {
                var pluginType = typeof(CoordinatedOperationRuntime).Assembly.GetType("WhiskeyRealism.Plugin");
                var log = AccessTools.Field(pluginType, "Log")?.GetValue(null);
                AccessTools.Method(log?.GetType(), "LogWarning", new[] { typeof(object) })?.Invoke(log, new object[] { message });
            }
            catch
            {
            }
#else
            OnceLog.Warning(key, message);
#endif
        }

        internal static void EmitCoordinatedOps(
            TelemetryCategory category,
            TelemetrySeverity severity,
            int allianceId,
            string action,
            string reason,
            string packageSignature,
            Action<TelemetryEvent> configure = null)
        {
            string safeAction = string.IsNullOrWhiteSpace(action) ? "-" : action;
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "-" : reason;
            string safePackage = string.IsNullOrWhiteSpace(packageSignature) ? "-" : packageSignature;
            string signature = "alliance=" + allianceId +
                "|action=" + safeAction +
                "|reason=" + safeReason +
                "|package=" + safePackage;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, category, "CoordinatedOps", severity, ev =>
            {
                ev.WithAlliance(allianceId)
                    .WithDecision(safeAction, safeReason, signature)
                    .WithField("action", safeAction)
                    .WithField("reason", safeReason)
                    .WithField("package", safePackage);
                configure?.Invoke(ev);
            });
        }
    }
}
