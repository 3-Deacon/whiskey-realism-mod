using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
#if !NET8_0
using WhiskeyRealism.Util;
#endif

namespace WhiskeyRealism.Strategic
{
    internal static class CoordinatedOperationRuntime
    {
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

                bool committed = CommitUnit(allianceId, aifactionIndex, ownUnits, offensive, output.LeadStableUnitId, target, targetName, objectiveId, intent, sourceSystem, output.Signature());
                for (int i = 0; i < output.SupportStableUnitIds.Count; i++)
                {
                    var supportIntent = output.Decision == CoordinatedOperationDecision.Reinforce
                        ? WlStrategicIntent.Reinforce
                        : intent;
                    committed |= CommitUnit(allianceId, aifactionIndex, ownUnits, offensive, output.SupportStableUnitIds[i], target, targetName, objectiveId, supportIntent, sourceSystem, output.Signature());
                }
                return committed;
            }
            catch (Exception ex)
            {
                WarnOnce("coordinated-ops:commit", "[CoordinatedOps] commit failed: " + ex.Message);
                return false;
            }
        }

        private static bool CommitUnit(
            int allianceId,
            int aifactionIndex,
            IList ownUnits,
            IList offensive,
            int stableUnitId,
            Vector3 target,
            string targetName,
            int objectiveId,
            WlStrategicIntent intent,
            string sourceSystem,
            string packageSignature)
        {
            var unit = FindUnitById(ownUnits, stableUnitId);
            if (unit == null) return false;
            if (!IsAvailable(aifactionIndex, unit, target))
            {
                LogInfo($"[CoordinatedOps] alliance={allianceId} unit={SafeName(unit)} action=skip reason=availability package={packageSignature}");
                return false;
            }

            var decision = WlStrategicOrderBridge.TryIssue(new WlStrategicOrderRequest
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

            if (decision.Result == WlStrategicOrderResult.IssuedWlCurrentOrder)
            {
                LogInfo($"[CoordinatedOps] alliance={allianceId} unit={SafeName(unit)} action=wl-current-order type={decision.WlOrderType} package={packageSignature}");
                return true;
            }
            if (!decision.MayDirectMove)
            {
                LogInfo($"[CoordinatedOps] alliance={allianceId} unit={SafeName(unit)} action=skip wlResult={decision.Result} reason={decision.Reason} package={packageSignature}");
                return false;
            }
            if (AICampaign.MoveUnitTo(unit, target, true))
            {
                if (!offensive.Contains(unit))
                    offensive.Add(unit);
                LogInfo($"[CoordinatedOps] alliance={allianceId} unit={SafeName(unit)} action=direct-move package={packageSignature}");
                return true;
            }
            return false;
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
                WarnOnce(
                    "coordinated-ops:objective-name",
                    $"[CoordinatedOps] objective name resolve failed for objective ID {objectiveId}: {ex.Message}");
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
                WarnOnce("coordinated-ops:availability", "[CoordinatedOps] availability check failed: " + ex.Message);
                return false;
            }
#else
            return OffensiveAvailabilityWrapper.IsAvailable(aifactionIndex, unit, target);
#endif
        }

        private static void LogInfo(string message)
        {
#if NET8_0
            try
            {
                var pluginType = typeof(CoordinatedOperationRuntime).Assembly.GetType("WhiskeyRealism.Plugin");
                var log = AccessTools.Field(pluginType, "Log")?.GetValue(null);
                AccessTools.Method(log?.GetType(), "LogInfo", new[] { typeof(object) })?.Invoke(log, new object[] { message });
            }
            catch
            {
            }
#else
            Plugin.Log.LogInfo(message);
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
    }
}
