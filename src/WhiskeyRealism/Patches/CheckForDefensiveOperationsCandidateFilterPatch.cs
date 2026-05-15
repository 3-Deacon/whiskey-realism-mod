using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Patch #25 — Defense intent ledger enforcement (Slice 2 surface 1).
    // Vanilla CheckForDefensiveOperations at decompile line 13505 picks one
    // defensive operation per call by iterating aifaction[i].ownunits. We
    // Prefix-snapshot+filter the live list to remove ledger-forbidden
    // candidates for the duration of vanilla's call, then Postfix-restore
    // the snapshot and revert any forbidden cross-map pull vanilla committed
    // despite filtering (defense-in-depth).
    //
    // Vanilla field names confirmed from Assembly-CSharp.decompiled.cs:
    //   AIFaction.ownunits               — List<Regiment> (line 10324)
    //   AIFaction.unitsindefensiveoperations — List<Regiment> (line 10358)
    //   Regiment.theaterposition         — Vector3 strategic marker (line 17065)
    //   AICampaign.MoveUnitTo(Regiment, Vector3, bool movearoundenemyunits, ...) (line 14479)
    //   Private nested AICampaign.DefensiveOperation with static RemoveUnit(Regiment) (line 10097)
    //   DefenseCandidate.UnitInstanceId populated from ((UnityEngine.Object)unit).GetInstanceID()
    //     in DefenseIntentRuntime line 672 — matches our GetInstanceID() lookup here.
    [HarmonyPatch(typeof(AICampaign), "CheckForDefensiveOperations")]
    internal static class CheckForDefensiveOperationsCandidateFilterPatch
    {
        // Per-call snapshots indexed by aifaction index. Static dicts are safe because
        // vanilla CheckForDefensiveOperations is called once per AI faction per update
        // tick and is not re-entrant. Postfix always clears via ClearSnapshots().
        private static readonly Dictionary<int, List<object>> _ownUnitsSnapshot =
            new Dictionary<int, List<object>>();
        private static readonly Dictionary<int, HashSet<int>> _defensiveOpsBefore =
            new Dictionary<int, HashSet<int>>();
        // Maps UnitInstanceId → theaterposition captured before vanilla runs, used
        // to revert any forbidden cross-map move vanilla committed despite filtering.
        private static readonly Dictionary<int, Dictionary<int, Vector3>> _priorTheaterPositionByUnitId =
            new Dictionary<int, Dictionary<int, Vector3>>();
        // Tracks the suppression reason per unit so the revert log is diagnostic.
        private static readonly Dictionary<int, Dictionary<int, string>> _suppressionReasonByUnitId =
            new Dictionary<int, Dictionary<int, string>>();
        // Tracks the threat signature per unit for revert-log dedup keying.
        private static readonly Dictionary<int, Dictionary<int, string>> _threatSigByUnitId =
            new Dictionary<int, Dictionary<int, string>>();
        // Dedup set for revert log lines — key "<allianceId>|<threatSig>|<unitId>".
        // First revert per tuple logs unconditionally; subsequent reverts only under verbose.
        private static readonly HashSet<string> _revertLogged = new HashSet<string>();
        private static readonly Dictionary<Type, FieldInfo> _ownUnitsFields =
            new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> _defensiveOpsFields =
            new Dictionary<Type, FieldInfo>();
        private static MethodInfo _removeDefensiveOperationMethod;
        private static bool _wiredLogged;

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction)
        {
            if (!_wiredLogged)
            {
                _wiredLogged = true;
                EmitDefenseIntentObserver();
            }

            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.EnableDefenseIntentLedger.Value) return;

                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0) return;
                var coordinator = StrategicCoordinator.Instance;
                if (coordinator == null || coordinator.DefenseIntents == null) return;
                if (allianceId >= coordinator.DefenseIntents.Length) return;

                var output = coordinator.DefenseIntents[allianceId];
                if (output == null || output.Responses == null || output.Responses.Count == 0) return;

                var forbidden = CollectForbiddenIds(output, out var reasonByUnitId, out var threatSigByUnitId);
                if (forbidden.Count == 0) return;

                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null) return;

                var ownUnitsField = GetOwnUnitsField(faction.GetType());
                var defOpsField = GetDefensiveOpsField(faction.GetType());
                var ownUnits = ownUnitsField?.GetValue(faction) as IList;
                var defOps = defOpsField?.GetValue(faction) as IList;
                if (ownUnits == null || defOps == null) return;

                // Snapshot ownunits in original order so Postfix can restore exactly.
                var snapshot = new List<object>(ownUnits.Count);
                for (int i = 0; i < ownUnits.Count; i++) snapshot.Add(ownUnits[i]);
                _ownUnitsSnapshot[_aifaction] = snapshot;

                // Snapshot the InstanceIDs already in defensiveops so Postfix can
                // identify units newly added by vanilla during this call.
                var beforeIds = new HashSet<int>();
                for (int i = 0; i < defOps.Count; i++)
                {
                    var unityObj = defOps[i] as UnityEngine.Object;
                    if (unityObj != null) beforeIds.Add(unityObj.GetInstanceID());
                }
                _defensiveOpsBefore[_aifaction] = beforeIds;

                // Snapshot theaterposition of forbidden units BEFORE they are removed
                // from the live list (and before vanilla runs). If vanilla re-picks one
                // through a secondary path, Postfix can call MoveUnitTo to revert.
                var priorPositions = new Dictionary<int, Vector3>();
                var suppressionReasons = new Dictionary<int, string>();
                var threatSigs = new Dictionary<int, string>();
                for (int i = 0; i < ownUnits.Count; i++)
                {
                    var regiment = ownUnits[i] as Regiment;
                    if (regiment == null) continue;
                    var unityObj = regiment as UnityEngine.Object;
                    if (unityObj == null) continue;
                    int id = unityObj.GetInstanceID();
                    if (!forbidden.Contains(id)) continue;
                    // theaterposition is a public Vector3 field on Regiment (decompile line 17065).
                    priorPositions[id] = regiment.theaterposition;
                    if (reasonByUnitId.TryGetValue(id, out var reason))
                        suppressionReasons[id] = reason;
                    if (threatSigByUnitId.TryGetValue(id, out var sig))
                        threatSigs[id] = sig;
                }
                _priorTheaterPositionByUnitId[_aifaction] = priorPositions;
                _suppressionReasonByUnitId[_aifaction] = suppressionReasons;
                _threatSigByUnitId[_aifaction] = threatSigs;

                // Filter the live ownunits in place — remove forbidden references
                // for the duration of vanilla's call. Walk in reverse to keep
                // index stable while removing.
                int removed = 0;
                for (int i = ownUnits.Count - 1; i >= 0; i--)
                {
                    var unityObj = ownUnits[i] as UnityEngine.Object;
                    if (unityObj == null) continue;
                    if (forbidden.Contains(unityObj.GetInstanceID()))
                    {
                        ownUnits.RemoveAt(i);
                        removed++;
                    }
                }

                if (removed > 0 && Plugin.Instance.DefenseIntentVerboseLogging.Value)
                {
                    EmitDefenseIntentFilter(allianceId, removed, forbidden.Count);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defense-intent:filter:prefix",
                    "candidate-filter Prefix failed: " + ex.Message);
                // If we partially filtered before throwing, Postfix still sees the snapshot
                // and restores ownunits. No explicit rollback needed here.
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(int _aifaction)
        {
            try
            {
                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null)
                {
                    ClearSnapshots(_aifaction);
                    return;
                }

                // Restore ownunits FIRST — before any other vanilla code in the same frame
                // iterates the list. This is the primary invariant: ownunits must look
                // unchanged to everything that runs after CheckForDefensiveOperations.
                if (_ownUnitsSnapshot.TryGetValue(_aifaction, out var snapshot))
                {
                    var ownUnitsField = GetOwnUnitsField(faction.GetType());
                    var ownUnits = ownUnitsField?.GetValue(faction) as IList;
                    if (ownUnits != null)
                    {
                        ownUnits.Clear();
                        for (int i = 0; i < snapshot.Count; i++) ownUnits.Add(snapshot[i]);
                    }
                }

                // Defense-in-depth: revert any forbidden units vanilla committed to
                // unitsindefensiveoperations despite our filter. This can happen when
                // vanilla uses a secondary code path (e.g., the attack-operation branch
                // that also adds to unitsindefensiveoperations at decompile line 13769).
                if (_defensiveOpsBefore.TryGetValue(_aifaction, out var beforeIds))
                {
                    int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                    var coordinator = StrategicCoordinator.Instance;
                    if (allianceId >= 0 &&
                        coordinator?.DefenseIntents != null &&
                        allianceId < coordinator.DefenseIntents.Length)
                    {
                        var output = coordinator.DefenseIntents[allianceId];
                        if (output != null)
                        {
                            var forbidden = CollectForbiddenIds(output, out var _unusedReasons, out var _unusedSigs);
                            if (forbidden.Count > 0)
                            {
                                var defOpsField = GetDefensiveOpsField(faction.GetType());
                                var defOps = defOpsField?.GetValue(faction) as IList;
                                _priorTheaterPositionByUnitId.TryGetValue(_aifaction, out var priorPositions);
                                _suppressionReasonByUnitId.TryGetValue(_aifaction, out var suppressionReasons);
                                _threatSigByUnitId.TryGetValue(_aifaction, out var threatSigs);
                                bool verbose = Plugin.Instance != null && Plugin.Instance.DefenseIntentVerboseLogging.Value;

                                if (defOps != null)
                                {
                                    for (int i = defOps.Count - 1; i >= 0; i--)
                                    {
                                        var unityObj = defOps[i] as UnityEngine.Object;
                                        if (unityObj == null) continue;
                                        int id = unityObj.GetInstanceID();
                                        if (beforeIds.Contains(id)) continue;   // was already in ops before vanilla ran
                                        if (!forbidden.Contains(id)) continue;  // not in our forbidden set

                                        var unit = defOps[i] as Regiment;
                                        string reason = "<unknown>";
                                        suppressionReasons?.TryGetValue(id, out reason);
                                        string sig = "<no-sig>";
                                        threatSigs?.TryGetValue(id, out sig);

                                        defOps.RemoveAt(i);
                                        RemoveFromStaticDefensiveOperation(unit);

                                        if (priorPositions != null && priorPositions.TryGetValue(id, out var priorPos))
                                        {
                                            if (ShouldAvoidDirectWlRevert(unit, allianceId))
                                            {
                                                EmitDefenseIntentSkipRevert(allianceId, id, SafeUnitName(unit, id), sig, "player-chain");
                                            }
                                            else
                                            {
                                                SafeMoveUnitTo(unit, priorPos);
                                            }
                                        }

                                        // Fix B: .name throws MissingReferenceException on destroyed objects.
                                        string candidateName;
                                        try { candidateName = unityObj.name; }
                                        catch { candidateName = id.ToString(); }

                                        // Fix A: dedup revert log by (alliance, threat-sig, unit-id).
                                        // First revert per tuple is unconditional; subsequent reverts gated on verbose.
                                        string revertKey = $"{allianceId}|{sig}|{id}";
                                        bool firstRevert = _revertLogged.Add(revertKey);
                                        if (firstRevert || verbose)
                                        {
                                            EmitDefenseIntentRevert(allianceId, id, candidateName, sig, reason);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defense-intent:filter:postfix",
                    "candidate-filter Postfix failed: " + ex.Message);
            }
            finally
            {
                ClearSnapshots(_aifaction);
            }
        }

        // Collect all UnitInstanceIds that are suppressed for one of the three
        // forbidden reasons. Also populates reasonByUnitId and threatSigByUnitId
        // for diagnostic logging and revert-log dedup keying.
        private static HashSet<int> CollectForbiddenIds(
            DefenseIntentLedgerOutput output,
            out Dictionary<int, string> reasonByUnitId,
            out Dictionary<int, string> threatSigByUnitId)
        {
            reasonByUnitId = new Dictionary<int, string>();
            threatSigByUnitId = new Dictionary<int, string>();
            var ids = new HashSet<int>();
            if (output?.Responses == null) return ids;

            foreach (var r in output.Responses)
            {
                if (r?.Suppressed == null) continue;
                string sig = r.Threat?.Signature ?? "<no-sig>";
                foreach (var s in r.Suppressed)
                {
                    if (s == null) continue;
                    if (s.Reason != "forbidden-cross-map" &&
                        s.Reason != "critical-front" &&
                        s.Reason != "asset-proximity-local-only" &&
                        s.Reason != "national-emergency-required" &&
                        s.Reason != "formation-directive" &&
                        s.Reason != "formation-donor" &&
                        s.Reason != "min-hold" &&
                        s.Reason != "critical-sector-budget" &&
                        s.Reason != "player-controlled")
                        continue;
                    ids.Add(s.UnitInstanceId);
                    // Last reason/sig wins when a unit appears in multiple responses.
                    reasonByUnitId[s.UnitInstanceId] = s.Reason;
                    threatSigByUnitId[s.UnitInstanceId] = sig;
                }
            }
            return ids;
        }

        private static void EmitDefenseIntentObserver()
        {
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.State, "DefenseIntent", TelemetrySeverity.Info, ev => ev
                .WithDecision("observer", "candidate-filter", "observer=candidate-filter|patch=25")
                .WithField("observer", "candidate-filter")
                .WithField("patch", 25));
        }

        private static void EmitDefenseIntentFilter(int allianceId, int removed, int forbidden)
        {
            string signature = "alliance=" + allianceId + "|removed=" + removed + "|forbidden=" + forbidden;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Gate, "DefenseIntent", TelemetrySeverity.Info, ev => ev
                .WithAlliance(allianceId)
                .WithDecision("filter-candidates", "forbidden-defense-package", signature)
                .WithField("removed", removed)
                .WithField("forbidden", forbidden));
        }

        private static void EmitDefenseIntentSkipRevert(int allianceId, int unitId, string candidateName, string threatSignature, string reason)
        {
            string safeThreat = string.IsNullOrWhiteSpace(threatSignature) ? "-" : threatSignature;
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "-" : reason;
            string signature = "alliance=" + allianceId + "|unit=" + unitId + "|threat=" + safeThreat + "|reason=" + safeReason;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Gate, "DefenseIntent", TelemetrySeverity.Info, ev => ev
                .WithAlliance(allianceId)
                .WithUnit(candidateName)
                .WithDecision("skip-direct-revert", safeReason, signature)
                .WithField("unitId", unitId)
                .WithField("candidate", candidateName ?? "-")
                .WithField("threat", safeThreat));
        }

        private static void EmitDefenseIntentRevert(int allianceId, int unitId, string candidateName, string threatSignature, string reason)
        {
            string safeThreat = string.IsNullOrWhiteSpace(threatSignature) ? "-" : threatSignature;
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "-" : reason;
            string signature = "alliance=" + allianceId + "|unit=" + unitId + "|threat=" + safeThreat + "|reason=" + safeReason;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Write, "DefenseIntent", TelemetrySeverity.Info, ev => ev
                .WithAlliance(allianceId)
                .WithUnit(candidateName)
                .WithDecision("revert-candidate", safeReason, signature)
                .WithField("unitId", unitId)
                .WithField("candidate", candidateName ?? "-")
                .WithField("threat", safeThreat));
        }

        private static void RemoveFromStaticDefensiveOperation(Regiment unit)
        {
            if (unit == null) return;
            try
            {
                // AICampaign.DefensiveOperation is a private nested class with a static
                // RemoveUnit(Regiment) method (decompile line 10097).
                if (_removeDefensiveOperationMethod == null)
                {
                    var nested = AccessTools.Inner(typeof(AICampaign), "DefensiveOperation");
                    _removeDefensiveOperationMethod = nested != null
                        ? AccessTools.Method(nested, "RemoveUnit", new[] { typeof(Regiment) })
                        : null;
                }
                if (_removeDefensiveOperationMethod != null)
                {
                    _removeDefensiveOperationMethod.Invoke(null, new object[] { unit });
                }
                else
                {
                    OnceLog.Warning("defense-intent:filter:remove-static",
                        "DefensiveOperation.RemoveUnit(Regiment) not found via reflection");
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defense-intent:filter:remove-static",
                    "RemoveUnit on static defensiveoperations failed: " + ex.Message);
            }
        }

        private static void SafeMoveUnitTo(Regiment unit, Vector3 position)
        {
            if (unit == null) return;
            try
            {
                // MoveUnitTo signature: (Regiment unit, Vector3 position,
                // bool movearoundenemyunits = false, bool useseamovementonly = false,
                // bool checkforreadiness = true) — decompile line 14479.
                // We pass movearoundenemyunits=true to match vanilla defensive-op calls.
                AICampaign.MoveUnitTo(unit, position, movearoundenemyunits: true);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defense-intent:filter:moveunitto",
                    "MoveUnitTo on revert failed: " + ex.Message);
            }
        }

        private static bool ShouldAvoidDirectWlRevert(Regiment unit, int allianceId)
        {
            try
            {
                if (unit == null) return false;
                if (!DLC_WL.dlc_scenarioactive) return false;
                if (allianceId != GameVars.playeralliance || unit.alliance != GameVars.playeralliance) return false;
                if (DLC_WL.IsCommanderInChief()) return true;
                if (DLC_WL.IsMovedByPlayer(unit)) return true;
                return unit.dlcw_isundercommander;
            }
            catch
            {
                return false;
            }
        }

        private static string SafeUnitName(Regiment unit, int fallbackId)
        {
            try { return unit != null ? ((UnityEngine.Object)unit).name : fallbackId.ToString(); }
            catch { return fallbackId.ToString(); }
        }

        private static void ClearSnapshots(int aifactionIndex)
        {
            _ownUnitsSnapshot.Remove(aifactionIndex);
            _defensiveOpsBefore.Remove(aifactionIndex);
            _priorTheaterPositionByUnitId.Remove(aifactionIndex);
            _suppressionReasonByUnitId.Remove(aifactionIndex);
            _threatSigByUnitId.Remove(aifactionIndex);
            // _revertLogged is intentionally not cleared — it is the persistent dedup set
            // that prevents the same (alliance, threat-sig, unit) tuple from spamming
            // across ticks. Cross-campaign bleed is accepted as a known limitation
            // (same as _firstAssignmentLogged in CoastalDefenseCustomOrderRunner).
        }

        private static FieldInfo GetOwnUnitsField(Type factionType)
        {
            if (factionType == null) return null;
            if (_ownUnitsFields.TryGetValue(factionType, out var field)) return field;
            field = AccessTools.Field(factionType, "ownunits");
            _ownUnitsFields[factionType] = field;
            return field;
        }

        private static FieldInfo GetDefensiveOpsField(Type factionType)
        {
            if (factionType == null) return null;
            if (_defensiveOpsFields.TryGetValue(factionType, out var field)) return field;
            field = AccessTools.Field(factionType, "unitsindefensiveoperations");
            _defensiveOpsFields[factionType] = field;
            return field;
        }
    }
}
