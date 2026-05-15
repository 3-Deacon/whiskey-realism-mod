using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class OperationalProbeRuntime
    {
        private static readonly HashSet<string> LogOnceKeys = new HashSet<string>();

        internal static OperationalProbeInput BuildInput(
            int allianceId,
            CIC cic,
            FrontSectorLedger fronts,
            FormationDirectiveLedger formation,
            OperationalProbeState previous,
            int daySerial,
            EraStage era,
            int policyChapter,
            int campaignMonth,
            PersonalityVector personality,
            IReadOnlyList<BattleHistoryRecord> battleHistory)
        {
            int objectiveId = cic?.ActivePlan?.CurrentPhase?.TargetObjectiveId ?? -1;
            var target = ObjectiveAdapter.ResolveObjectivePosition(objectiveId);
            string areaKey = target.HasValue ? ArmyAreaRuntime.AreaKey(target.Value) : null;
            string sectorKey = target.HasValue ? FrontSectorRuntime.SectorKey(target.Value) : null;
            var targetSector = fronts?.GetSector(sectorKey);

            var input = new OperationalProbeInput
            {
                AllianceId = allianceId,
                DaySerial = daySerial,
                ObjectiveId = objectiveId,
                PlanTargetAreaKey = areaKey,
                Fronts = fronts,
                FormationDirectives = formation,
                Previous = previous,
                CurrentEnemyStrength = targetSector?.EnemyStrength ?? -1f,
                CurrentFriendlyStrength = targetSector?.OwnStrength ?? -1f,
                Options = OperationalTempoDoctrine.For(
                    allianceId,
                    era,
                    policyChapter,
                    campaignMonth,
                    personality)
            };

            var phase = cic?.ActivePlan?.CurrentPhase;
            if (phase != null)
            {
                input.OperationPosture = phase.OperationPosture;
                input.AllowCoordinatedAttack = phase.AllowCoordinatedAttack;
                input.AllowReinforcementPackage = phase.AllowReinforcementPackage;
                input.AllowProbeOnly = phase.AllowProbeOnly;
                if (string.IsNullOrEmpty(cic.ActivePlan.OperationId) &&
                    !input.AllowCoordinatedAttack &&
                    !input.AllowReinforcementPackage &&
                    !input.AllowProbeOnly)
                {
                    input.AllowCoordinatedAttack = true;
                    input.AllowReinforcementPackage = true;
                }
                ApplyOperationPosture(input.Options, input.OperationPosture);
            }

            if (target.HasValue)
            {
                input.TargetX = target.Value.x;
                input.TargetZ = target.Value.z;
                input.HasTargetCoordinates = true;
            }

            if (previous != null && formation != null)
            {
                var assignment = formation.GetAssignment(previous.UnitKey);
                if (assignment != null)
                    input.CurrentFriendlyStrength = assignment.CombatAvailability;
            }

            if (target.HasValue)
            {
                var contactInput = new ContactEvidenceInput
                {
                    ObservingAllianceId = allianceId,
                    TargetPosition = target.Value,
                    CurrentEnemyStrength = input.CurrentEnemyStrength,
                    CurrentFriendlyStrength = input.CurrentFriendlyStrength,
                    PreviousObservedEnemyStrength = previous?.LastObservedEnemyStrength ?? 0f,
                    EnemyReactionMultiplier = input.Options.EnemyReactionMultiplier,
                    EscalateFriendlyRatio = input.Options.EscalateFriendlyRatio,
                    WithdrawFriendlyRatio = input.Options.WithdrawFriendlyRatio,
                    BattleHistory = battleHistory,
                    SpatialMaxDistance = GamePrefs.aimaximumdistancetosearchforunitrelocations,
                    CurrentDaySerial = daySerial
                };
                input.ContactEvidence = ContactEvidenceLedger.Build(contactInput).Evidence;
            }

            return input;
        }

        private static void ApplyOperationPosture(OperationalProbeOptions options, OperationPosture posture)
        {
            if (options == null) return;
            switch (posture)
            {
                case OperationPosture.ProbeAndDevelop:
                    options.MaximumProbeStrengthFraction -= 0.05f;
                    break;
                case OperationPosture.ConcentratedAttack:
                    options.EscalateFriendlyRatio -= 0.15f;
                    options.MaximumProbeStrengthFraction += 0.10f;
                    break;
                case OperationPosture.ExploitBreakthrough:
                    options.MinimumProbeDays -= 1;
                    options.EscalateFriendlyRatio -= 0.20f;
                    break;
            }
            options.MinimumProbeDays = Math.Max(1, options.MinimumProbeDays);
            options.MaximumProbeStrengthFraction = Math.Max(0.20f, Math.Min(0.70f, options.MaximumProbeStrengthFraction));
            options.EscalateFriendlyRatio = Math.Max(1.10f, Math.Min(2.50f, options.EscalateFriendlyRatio));
        }

        internal static void Run(int allianceId, OperationalProbeOutput output, Vector3? target)
        {
            try
            {
                if (allianceId < 0 || allianceId > 1) return;
                if (output == null || string.IsNullOrEmpty(output.SelectedUnitKey)) return;

                int aifactionIndex = ResolveAifactionIndex(allianceId);
                if (aifactionIndex < 0) return;
                var faction = AICampaignReflect.GetFaction(aifactionIndex);
                if (faction == null) return;

                var factionType = faction.GetType();
                var ownUnits = AccessTools.Field(factionType, "ownunits")?.GetValue(faction) as IList;
                var offensive = AccessTools.Field(factionType, "unitsinoffensiveoperations")?.GetValue(faction) as IList;
                if (ownUnits == null || offensive == null) return;

                if (output.Decision == OperationalProbeDecision.Pause ||
                    output.Decision == OperationalProbeDecision.Withdraw)
                {
                    var pausedUnit = FindUnit(ownUnits, output.SelectedUnitKey);
                    if (pausedUnit == null) return;
                    if (offensive.Contains(pausedUnit))
                    {
                        offensive.Remove(pausedUnit);
                        EmitOperationalProbeWrite(allianceId, output, "remove-offensive", pausedUnit, null, null, null);
                    }
                    return;
                }

                if (output.Decision != OperationalProbeDecision.Probe &&
                    output.Decision != OperationalProbeDecision.Escalate)
                    return;
                if (!target.HasValue) return;

                string targetName = CoordinatedOperationRuntime.ResolveTargetName(
                    output.ObjectiveId,
                    output.TargetAreaKey,
                    StrategicCoordinator.Instance?.CampaignMap,
                    target.Value);

                if (output.Package != null &&
                    output.Package.Decision != CoordinatedOperationDecision.None &&
                    output.Package.Decision != CoordinatedOperationDecision.Delay &&
                    output.Package.Decision != CoordinatedOperationDecision.Recover)
                {
                    bool committed = CoordinatedOperationRuntime.CommitPackage(
                        allianceId,
                        aifactionIndex,
                        output.Package,
                        target.Value,
                        targetName,
                        output.ObjectiveId,
                        output.Decision == OperationalProbeDecision.Escalate
                            ? WlStrategicIntent.Offensive
                            : WlStrategicIntent.Probe,
                        "OperationalProbe");
                    if (committed)
                    {
                        EmitCampaignInfo(
                            $"[CoordinatedOps] alliance={allianceId} intent=Probe decision={output.Package.Decision} " +
                            $"target={targetName} ratio={output.Package.Ratio:0.00} " +
                            $"lead={output.Package.LeadDisplayUnitKey} support={output.Package.SupportStableUnitIds.Count} reason={output.Package.Reason}");
                    }
                    else
                    {
                        Plugin.Log.LogWarning(
                            $"[CoordinatedOps] alliance={allianceId} intent=Probe decision={output.Package.Decision} " +
                            $"action=package-no-commit target={targetName} " +
                            $"lead={output.Package.LeadDisplayUnitKey} support={output.Package.SupportStableUnitIds.Count} reason={output.Package.Reason}");
                    }
                    return;
                }

                var unit = FindUnit(ownUnits, output.SelectedUnitKey);
                if (unit == null) return;
                if (!OffensiveAvailabilityWrapper.IsAvailable(aifactionIndex, unit, target.Value))
                {
                    EmitOperationalProbeWriteOnce(
                        "operational-probe:gate-blocked:" + allianceId,
                        allianceId,
                        output,
                        "availability-blocked",
                        unit,
                        targetName,
                        null,
                        "blocked-by-availability");
                    return;
                }

                var intent = output.Decision == OperationalProbeDecision.Escalate
                    ? WlStrategicIntent.Offensive
                    : WlStrategicIntent.Probe;
                var bridgeDecision = WlStrategicOrderBridge.TryIssue(new WlStrategicOrderRequest
                {
                    AllianceId = allianceId,
                    AifactionIndex = aifactionIndex,
                    Unit = unit,
                    TargetPosition = target.Value,
                    TargetName = targetName,
                    ObjectiveId = output.ObjectiveId,
                    Intent = intent,
                    Width = 20f,
                    Depth = 20f,
                    SourceSystem = "OperationalProbe"
                });

                if (bridgeDecision.Result == WlStrategicOrderResult.IssuedWlCurrentOrder)
                {
                    EmitOperationalProbeWrite(allianceId, output, "wl-current-order", unit, targetName, bridgeDecision.WlOrderType.ToString(), null);
                    return;
                }

                if (!bridgeDecision.MayDirectMove)
                {
                    EmitOperationalProbeWriteOnce(
                        $"operational-probe:wl-skip:{allianceId}:{UnitKey(unit)}:{bridgeDecision.Result}",
                        allianceId,
                        output,
                        "skip-direct-move",
                        unit,
                        targetName,
                        bridgeDecision.Result.ToString(),
                        bridgeDecision.Reason);
                    return;
                }

                if (AICampaign.MoveUnitTo(unit, target.Value, true) && !offensive.Contains(unit))
                {
                    offensive.Add(unit);
                    EmitOperationalProbeWrite(allianceId, output, "direct-move", unit, targetName, null, null);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("operational-probe:run", "[OperationalProbe] runner failed: " + ex.Message);
            }
        }

        private static int ResolveAifactionIndex(int allianceId)
        {
            var list = AccessTools.Field(typeof(AICampaign), "aifaction")?.GetValue(null) as IList;
            if (list == null) return -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (AICampaignReflect.GetAllianceId(i) == allianceId) return i;
            }
            return -1;
        }

        private static void EmitCampaignInfo(string line)
        {
            TelemetryRouter.LegacyInfo(line, TelemetryLayer.Campaign);
        }

        private static void EmitCampaignInfoOnce(string key, string line)
        {
            if (!LogOnceKeys.Add(key ?? string.Empty)) return;
            EmitCampaignInfo(line);
        }

        private static void EmitOperationalProbeWriteOnce(
            string key,
            int allianceId,
            OperationalProbeOutput output,
            string action,
            Regiment unit,
            string targetName,
            string result,
            string reasonOverride)
        {
            if (!LogOnceKeys.Add(key ?? string.Empty)) return;
            EmitOperationalProbeWrite(allianceId, output, action, unit, targetName, result, reasonOverride);
        }

        private static void EmitOperationalProbeWrite(
            int allianceId,
            OperationalProbeOutput output,
            string action,
            Regiment unit,
            string targetName,
            string result,
            string reasonOverride)
        {
            string unitName = SafeName(unit);
            string reason = string.IsNullOrWhiteSpace(reasonOverride) ? (output?.Reason ?? "-") : reasonOverride;
            string safeTarget = string.IsNullOrWhiteSpace(targetName) ? (output?.TargetAreaKey ?? "-") : targetName;
            string safeResult = string.IsNullOrWhiteSpace(result) ? "-" : result;
            string signature = "alliance=" + allianceId +
                "|action=" + action +
                "|decision=" + (output != null ? output.Decision.ToString() : "-") +
                "|unit=" + UnitKey(unit) +
                "|target=" + safeTarget +
                "|result=" + safeResult +
                "|reason=" + reason;
            TelemetryCategory category = action == "availability-blocked" || action == "skip-direct-move"
                ? TelemetryCategory.Gate
                : TelemetryCategory.Write;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, category, "OperationalProbe", TelemetrySeverity.Info, ev => ev
                .WithAlliance(allianceId)
                .WithUnit(unitName)
                .WithDecision(action, reason, signature)
                .WithField("probeDecision", output != null ? output.Decision.ToString() : "-")
                .WithField("objective", output != null ? output.ObjectiveId : -1)
                .WithField("probeId", output?.ProbeId ?? "-")
                .WithField("unit", unitName)
                .WithField("unitKey", UnitKey(unit))
                .WithField("target", safeTarget)
                .WithField("result", safeResult)
                .WithField("mass", output != null && output.RequiresMassCommitment));
        }

        private static Regiment FindUnit(IList ownUnits, string unitKey)
        {
            for (int i = 0; i < ownUnits.Count; i++)
            {
                var unit = ownUnits[i] as Regiment;
                if (unit == null) continue;
                if (UnitKey(unit) == unitKey) return unit;
            }
            return null;
        }

        private static string UnitKey(Regiment unit)
        {
            return SafeName(unit) + ":" + ReadInt(unit, "commander").ToString();
        }

        private static string SafeName(UnityEngine.Object obj)
        {
            try { return obj != null ? obj.name : "<unknown>"; }
            catch { return "<unknown>"; }
        }

        private static int ReadInt(object target, string field)
        {
            try
            {
                var f = AccessTools.Field(target.GetType(), field);
                if (f != null) return Convert.ToInt32(f.GetValue(target));
            }
            catch { }
            return -1;
        }

        private static bool ReadBool(object target, string field)
        {
            try
            {
                var f = AccessTools.Field(target.GetType(), field);
                if (f != null) return Convert.ToBoolean(f.GetValue(target));
            }
            catch { }
            return false;
        }

        private static object ReadObject(object target, string field)
        {
            try
            {
                return AccessTools.Field(target.GetType(), field)?.GetValue(target);
            }
            catch
            {
                return null;
            }
        }
    }
}
