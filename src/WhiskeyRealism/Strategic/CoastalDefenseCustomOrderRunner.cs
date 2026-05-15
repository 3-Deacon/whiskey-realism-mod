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
    // Coordinator-driven custom defensive movement order runner.
    //
    // Runs after DefenseIntentLedger.Build emits its responses for an
    // alliance. Issues MoveUnitTo + unitsindefensiveoperations.Add for
    // selected packages where vanilla CheckForDefensiveOperations would
    // not (rate-limited landings, relaxed-filter CoastalGuard candidates
    // vanilla's downstream gates would reject). Does NOT touch the static
    // AICampaign.defensiveoperations list — that surface is owned by
    // vanilla's commit path. Release happens on a future tick when the
    // builder transitions to Recovered + cooldown expires.
    internal static class CoastalDefenseCustomOrderRunner
    {
        // Per-alliance dedup of "first assignment per (threat, unit) tuple"
        // log lines — keys are "<allianceId>|<threatSignature>|<unitInstanceId>".
        private static readonly HashSet<string> _firstAssignmentLogged = new HashSet<string>();

        // Tracks which unit IDs were assigned per (allianceId, threatSignature) on the
        // prior tick. Used to detect when a threat signature disappears from the current
        // tick's output and release the previously-assigned units from defensiveoperations.
        // Key: allianceId → threatSignature → set of UnitInstanceIds.
        // Cross-campaign bleed is accepted as a known limitation (same as _firstAssignmentLogged).
        private static readonly Dictionary<int, Dictionary<string, HashSet<int>>> _assignedByThreat =
            new Dictionary<int, Dictionary<string, HashSet<int>>>();
        private static readonly HashSet<string> _wlCurrentOrderAttempts = new HashSet<string>();
        private static readonly HashSet<string> _logOnceKeys = new HashSet<string>();

        internal static void Run(int allianceId, DefenseIntentLedgerOutput output)
        {
            try
            {
                // Alliance 2+ (Europe, neutral observers) should never receive custom
                // defensive movement orders. Our per-alliance state structures are sized
                // for Union=0 and CSA=1 only; reject anything outside that range.
                if (allianceId < 0 || allianceId > 1) return;
                if (output == null || output.Responses == null || output.Responses.Count == 0) return;

                int aifactionIndex = ResolveAifactionIndex(allianceId);
                if (aifactionIndex < 0) return;

                var faction = AICampaignReflect.GetFaction(aifactionIndex);
                if (faction == null) return;

                var defOpsField = AccessTools.Field(faction.GetType(), "unitsindefensiveoperations");
                var ownUnitsField = AccessTools.Field(faction.GetType(), "ownunits");
                var defOps = defOpsField?.GetValue(faction) as IList;
                var ownUnits = ownUnitsField?.GetValue(faction) as IList;
                if (defOps == null || ownUnits == null) return;

                bool verbose = Plugin.Instance != null && Plugin.Instance.DefenseIntentVerboseLogging.Value;

                // Build this tick's assignment map: signature → set of assigned unit IDs.
                // Populated during the assignment loop below; used afterwards for release tracking.
                var thisTick = new Dictionary<string, HashSet<int>>();

                foreach (var response in output.Responses)
                {
                    if (response?.SelectedPackage == null || response.SelectedPackage.Count == 0) continue;
                    if (!DefenseCustomOrderPolicy.RequiresCustomOrder(response)) continue;

                    string sig = response.Threat?.Signature ?? "<no-sig>";

                    foreach (var candidate in response.SelectedPackage)
                    {
                        var unit = FindUnitByInstanceId(ownUnits, candidate.UnitInstanceId);
                        if (unit == null) continue;
                        if (defOps.Contains(unit)) continue;
                        if (IsPlayerControlled(unit)) continue;
                        string wlCurrentOrderKey = $"{allianceId}|{sig}|{candidate.UnitInstanceId}";
                        if (_wlCurrentOrderAttempts.Contains(wlCurrentOrderKey))
                        {
                            AddThisTick(thisTick, sig, candidate.UnitInstanceId);
                            continue;
                        }

                        var anchor = new Vector3(response.Threat.X, 0f, response.Threat.Z);
                        var bridgeDecision = WlStrategicOrderBridge.TryIssue(new WlStrategicOrderRequest
                        {
                            AllianceId = allianceId,
                            AifactionIndex = aifactionIndex,
                            Unit = unit,
                            TargetPosition = anchor,
                            TargetName = sig,
                            ObjectiveId = -1,
                            Intent = WlStrategicIntent.EngageEnemy,
                            Width = 20f,
                            Depth = 20f,
                            SourceSystem = "CoastalDefense"
                        });

                        if (bridgeDecision.Result == WlStrategicOrderResult.IssuedWlCurrentOrder)
                        {
                            AddThisTick(thisTick, sig, candidate.UnitInstanceId);
                            _wlCurrentOrderAttempts.Add(wlCurrentOrderKey);
                            TelemetryRouter.LegacyInfo(
                                $"[DefenseIntent] source=CoastalDefense alliance={allianceId} action=wl-current-order unit={SafeName(unit, candidate.UnitInstanceId)} reason={bridgeDecision.Reason}");
                            continue;
                        }
                        else if (bridgeDecision.MayDirectMove)
                        {
                            if (!SafeMoveUnitTo(unit, anchor))
                            {
                                EmitCampaignInfoOnce(
                                    $"defense-intent:custom-order:move-skip:{allianceId}:{sig}:{candidate.UnitInstanceId}",
                                    $"[DefenseIntent] source=CoastalDefense alliance={allianceId} action=skip-direct-move unit={SafeName(unit, candidate.UnitInstanceId)} reason=moveunitto-failed");
                                continue;
                            }

                            if (!defOps.Contains(unit)) defOps.Add(unit);
                        }
                        else
                        {
                            EmitCampaignInfoOnce(
                                $"defense-intent:custom-order:wl-skip:{allianceId}:{sig}:{candidate.UnitInstanceId}:{bridgeDecision.Result}",
                                $"[DefenseIntent] source=CoastalDefense alliance={allianceId} action=skip-direct-move unit={SafeName(unit, candidate.UnitInstanceId)} wlResult={bridgeDecision.Result} reason={bridgeDecision.Reason}");
                            continue;
                        }

                        AddThisTick(thisTick, sig, candidate.UnitInstanceId);

                        string logKey = $"{allianceId}|{sig}|{candidate.UnitInstanceId}";
                        bool firstAssignment = _firstAssignmentLogged.Add(logKey);
                        if (firstAssignment || verbose)
                        {
                            string unitName = SafeName(unit, candidate.UnitInstanceId);
                            TelemetryRouter.LegacyInfo(
                                $"[DefenseIntent] custom-order alliance={allianceId} " +
                                $"threat={sig} " +
                                $"unit={unitName} " +
                                $"posture={response.Threat.Posture}",
                                TelemetryLayer.Campaign);
                        }
                    }
                }

                // Also record threat signatures present this tick even when no
                // custom order was issued (Recovered responses with empty SelectedPackage
                // keep units assigned — do NOT release while the threat is still in output).
                foreach (var response in output.Responses)
                {
                    string sig = response?.Threat?.Signature;
                    if (string.IsNullOrEmpty(sig)) continue;
                    if (!thisTick.ContainsKey(sig))
                        thisTick[sig] = new HashSet<int>(); // empty sentinel — signature is still active
                }

                // Release units for threat signatures that have fully disappeared
                // from this tick's output (not present in thisTick at all).
                if (_assignedByThreat.TryGetValue(allianceId, out var prior))
                {
                    foreach (var kv in prior)
                    {
                        if (thisTick.ContainsKey(kv.Key)) continue; // signature still active — keep units assigned
                        foreach (var unitId in kv.Value)
                        {
                            var unit = FindUnitByInstanceId(ownUnits, unitId);
                            _wlCurrentOrderAttempts.Remove($"{allianceId}|{kv.Key}|{unitId}");
                            if (unit == null) continue;
                            if (!defOps.Contains(unit)) continue;
                            defOps.Remove(unit);
                            TelemetryRouter.LegacyInfo(
                                $"[DefenseIntent] released alliance={allianceId} " +
                                $"threat={kv.Key} " +
                                $"unit={SafeName(unit, unitId)}",
                                TelemetryLayer.Campaign);
                        }
                    }
                }

                // Replace prior tick's map with this tick's — anything not in thisTick
                // has been released above and will not fire again unless reassigned.
                _assignedByThreat[allianceId] = thisTick;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defense-intent:custom-order:run",
                    "CoastalDefenseCustomOrderRunner.Run failed: " + ex.Message);
            }
        }

        // Safe name accessor — guards against MissingReferenceException on destroyed objects.
        private static string SafeName(Regiment unit, int fallbackId)
        {
            try { return ((UnityEngine.Object)unit).name; }
            catch { return fallbackId.ToString(); }
        }

        private static Regiment FindUnitByInstanceId(IList ownUnits, int instanceId)
        {
            for (int i = 0; i < ownUnits.Count; i++)
            {
                var u = ownUnits[i] as Regiment;
                if (u == null) continue;
                if (((UnityEngine.Object)u).GetInstanceID() == instanceId) return u;
            }
            return null;
        }

        private static bool IsPlayerControlled(Regiment unit)
        {
            try
            {
                var dlcType = AccessTools.TypeByName("DLC_WL");
                if (dlcType == null) return false;
                var method = AccessTools.Method(dlcType, "IsMovedByPlayer", new[] { typeof(Regiment) });
                if (method == null) return false;
                return (bool)method.Invoke(null, new object[] { unit });
            }
            catch { return false; }
        }

        private static void AddThisTick(
            Dictionary<string, HashSet<int>> thisTick,
            string threatSignature,
            int unitInstanceId)
        {
            if (!thisTick.TryGetValue(threatSignature, out var tickSet))
            {
                tickSet = new HashSet<int>();
                thisTick[threatSignature] = tickSet;
            }
            tickSet.Add(unitInstanceId);
        }

        private static bool SafeMoveUnitTo(Regiment unit, Vector3 position)
        {
            if (unit == null) return false;
            try { return AICampaign.MoveUnitTo(unit, position, true); }
            catch (Exception ex)
            {
                OnceLog.Warning("defense-intent:custom-order:moveunitto",
                    "CoastalDefenseCustomOrderRunner MoveUnitTo failed: " + ex.Message);
                return false;
            }
        }

        private static int ResolveAifactionIndex(int allianceId)
        {
            try
            {
                var aicType = AccessTools.TypeByName("AICampaign");
                var list = AccessTools.Field(aicType, "aifaction")?.GetValue(null) as IList;
                if (list == null) return -1;
                for (int i = 0; i < list.Count; i++)
                {
                    int aid = AICampaignReflect.GetAllianceId(i);
                    if (aid == allianceId) return i;
                }
            }
            catch { }
            return -1;
        }

        private static void EmitCampaignInfoOnce(string key, string line)
        {
            if (!_logOnceKeys.Add(key ?? string.Empty)) return;
            TelemetryRouter.LegacyInfo(line, TelemetryLayer.Campaign);
        }

    }
}
