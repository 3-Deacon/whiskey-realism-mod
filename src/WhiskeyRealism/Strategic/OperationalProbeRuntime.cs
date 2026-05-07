using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class OperationalProbeRuntime
    {
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
                        Plugin.Log.LogInfo(
                            $"[OperationalProbe] alliance={allianceId} decision={output.Decision} " +
                            $"unit={SafeName(pausedUnit)} reason={output.Reason}");
                    }
                    return;
                }

                if (output.Decision != OperationalProbeDecision.Probe &&
                    output.Decision != OperationalProbeDecision.Escalate)
                    return;
                if (!target.HasValue) return;

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
                        string.IsNullOrEmpty(output.TargetAreaKey) ? "Objective" : output.TargetAreaKey,
                        output.Decision == OperationalProbeDecision.Escalate
                            ? WlStrategicIntent.Offensive
                            : WlStrategicIntent.Probe,
                        "OperationalProbe");
                    if (committed)
                    {
                        Plugin.Log.LogInfo(
                            $"[CoordinatedOps] alliance={allianceId} intent=Probe decision={output.Package.Decision} " +
                            $"target={output.Package.TargetName ?? output.TargetAreaKey} ratio={output.Package.Ratio:0.00} " +
                            $"lead={output.Package.LeadDisplayUnitKey} support={output.Package.SupportStableUnitIds.Count} reason={output.Package.Reason}");
                        return;
                    }
                }

                var unit = FindUnit(ownUnits, output.SelectedUnitKey);
                if (unit == null) return;
                if (!OffensiveAvailabilityWrapper.IsAvailable(aifactionIndex, unit, target.Value))
                {
                    OnceLog.Info("operational-probe:gate-blocked:" + allianceId,
                        $"[OperationalProbe] alliance={allianceId} unit={SafeName(unit)} blocked-by-availability");
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
                    TargetName = string.IsNullOrEmpty(output.TargetAreaKey) ? "Objective" : output.TargetAreaKey,
                    ObjectiveId = -1,
                    Intent = intent,
                    Width = 20f,
                    Depth = 20f,
                    SourceSystem = "OperationalProbe"
                });

                if (bridgeDecision.Result == WlStrategicOrderResult.IssuedWlCurrentOrder)
                {
                    Plugin.Log.LogInfo(
                        $"[OperationalProbe] alliance={allianceId} decision={output.Decision} " +
                        $"unit={SafeName(unit)} action=wl-current-order type={bridgeDecision.WlOrderType} reason={output.Reason}");
                    return;
                }

                if (!bridgeDecision.MayDirectMove)
                {
                    OnceLog.Info(
                        $"operational-probe:wl-skip:{allianceId}:{UnitKey(unit)}:{bridgeDecision.Result}",
                        $"[OperationalProbe] alliance={allianceId} unit={SafeName(unit)} action=skip-direct-move wlResult={bridgeDecision.Result} reason={bridgeDecision.Reason}");
                    return;
                }

                if (AICampaign.MoveUnitTo(unit, target.Value, true) && !offensive.Contains(unit))
                {
                    offensive.Add(unit);
                    Plugin.Log.LogInfo(
                        $"[OperationalProbe] alliance={allianceId} decision={output.Decision} " +
                        $"unit={SafeName(unit)} target={output.TargetAreaKey} reason={output.Reason}");
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
