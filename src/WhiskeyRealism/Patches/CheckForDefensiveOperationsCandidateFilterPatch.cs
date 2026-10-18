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
    // Restore ownership (B-46-campaign, 2026-10-14): the ownunits restore and
    // snapshot clearing live in a [HarmonyFinalizer], because a Postfix does not
    // run when vanilla throws and the filtered list would then leak into every
    // later AI job. The Postfix only does the defense-in-depth revert, which
    // needs vanilla to have completed. Vanilla's first branch queues the move as
    // a DefensiveMovingOrder (decompile 13713) executed later by UpdateOrders
    // (79295): the revert cancels that queued order (the unit has not moved);
    // only a direct MoveUnitTo commit (attack branch, 13771) is moved back, and
    // never toward an unset theaterposition.
    //
    // W&L (stage F, C4): a commit of the player's own command (player-owned units,
    // units in his operation, his map group) or of a CIC side is left whole:
    // Whiskey neither cancels nor moves it back (vanilla commits them; 13711/13769
    // skip only the move of a player-owned unit), DefenseIntent skip-revert
    // reason=player-chain. The AI map group carrying a player below map-group level
    // is reverted like any AI unit (WlChainRules.MayMove): a cancelled queued march
    // drops #69's pending dispatch at its flush, and a move back goes through
    // MoveUnitTo, so #69 dispatches it (the pass's last move of the corps).
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
        static CheckForDefensiveOperationsCandidateFilterPatch()
        {
            CampaignScope.Register("defensive-ops-revert-log", () => _revertLogged.Clear());
        }

        // Per-call snapshots indexed by aifaction index. Static dicts are safe because
        // vanilla CheckForDefensiveOperations is called once per AI faction per update
        // tick and is not re-entrant. The Finalizer always clears via ClearSnapshots().
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
        // Lives for one campaign (CampaignScope): keys embed unit ids and threat
        // signatures, so it must not carry over to the next campaign.
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
            using (TelemetryPerf.Scope("campaign.patch.defensive-ops-candidate-filter.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.defensive-ops-candidate-filter.prefix");
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
                bool verbose = Plugin.Instance.DefenseIntentVerboseLogging.Value;
                var removedIds = verbose ? new List<int>() : null;   // only the verbose row names its reasons
                int removed = 0;
                for (int i = ownUnits.Count - 1; i >= 0; i--)
                {
                    var unityObj = ownUnits[i] as UnityEngine.Object;
                    if (unityObj == null) continue;
                    int id = unityObj.GetInstanceID();
                    if (forbidden.Contains(id))
                    {
                        ownUnits.RemoveAt(i);
                        removed++;
                        removedIds?.Add(id);
                    }
                }

                if (removed > 0 && verbose)
                {
                    EmitDefenseIntentFilter(allianceId, removed, forbidden.Count, DefenseFilterTally.Reasons(reasonByUnitId, removedIds));
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defense-intent:filter:prefix",
                    "candidate-filter Prefix failed: " + ex.Message);
                // If we partially filtered before throwing, the Finalizer still sees the
                // snapshot and restores ownunits. No explicit rollback needed here.
            }
            } // end using TelemetryPerf.Scope
        }

        [HarmonyPostfix]
        internal static void Postfix(int _aifaction)
        {
            using (TelemetryPerf.Scope("campaign.patch.defensive-ops-candidate-filter.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.defensive-ops-candidate-filter.postfix");
            try
            {
                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null) return;

                // ownunits restore + snapshot clearing happen in the Finalizer.
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
                                    var chain = WlChainContext.Capture();
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

                                        // The W&L player's own command keeps vanilla's commit whole
                                        // (list and queued march); see WlChainRules.
                                        if (!WlChainRules.MayMove(WlStrategicOrderBridge.ChainClassOf(in chain, allianceId, unit)))
                                        {
                                            EmitDefenseIntentSkipRevert(allianceId, id, SafeUnitName(unit, id), sig, "player-chain");
                                            continue;
                                        }

                                        defOps.RemoveAt(i);
                                        RemoveFromStaticDefensiveOperation(unit);

                                        Vector3 priorPos = default(Vector3);
                                        bool hasPrior = priorPositions != null && priorPositions.TryGetValue(id, out priorPos) &&
                                            priorPos != default(Vector3);
                                        var revert = CampaignMilitaryGates.ResolveForbiddenCommitRevert(QueuedDefensiveMove(unit), hasPrior);
                                        if (revert == ForbiddenCommitRevert.CancelQueuedOrder)
                                        {
                                            CancelQueuedDefensiveMove(unit);
                                        }
                                        else if (revert == ForbiddenCommitRevert.MoveBack)
                                        {
                                            SafeMoveUnitTo(unit, priorPos);
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
            } // end using TelemetryPerf.Scope
        }

        // Runs after Postfix, and also when vanilla (or the Postfix) threw.
        // A void Finalizer leaves any exception untouched.
        [HarmonyFinalizer]
        internal static void Finalizer(int _aifaction)
        {
            using (TelemetryPerf.Scope("campaign.patch.defensive-ops-candidate-filter.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.defensive-ops-candidate-filter.finalizer");
                try
                {
                    var faction = AICampaignReflect.GetFaction(_aifaction);
                    if (faction != null) RestoreOwnUnits(_aifaction, faction);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("defense-intent:filter:finalizer",
                        "candidate-filter Finalizer restore failed: " + ex.Message);
                }
                finally
                {
                    ClearSnapshots(_aifaction);
                }
            }
        }

        private static void RestoreOwnUnits(int aifactionIndex, object faction)
        {
            if (!_ownUnitsSnapshot.TryGetValue(aifactionIndex, out var snapshot)) return;
            var ownUnits = GetOwnUnitsField(faction.GetType())?.GetValue(faction) as IList;
            CampaignMilitaryGates.RestoreListSnapshot(ownUnits, snapshot);
        }

        private static bool QueuedDefensiveMove(Regiment unit)
        {
            if (unit == null) return false;
            try { return AICampaign.DefensiveMovingOrder.OrderRunning(unit); }
            catch { return false; }
        }

        private static void CancelQueuedDefensiveMove(Regiment unit)
        {
            if (unit == null) return;
            try { AICampaign.DefensiveMovingOrder.RemoveOrder(unit); }
            catch (Exception ex)
            {
                OnceLog.Warning("defense-intent:filter:remove-order",
                    "DefensiveMovingOrder.RemoveOrder on revert failed: " + ex.Message);
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

        // reasons: DefenseFilterTally.Reasons over the removed ids, "reason:n,..." (foresight F1: the smoke could only read removed=forbidden).
        private static void EmitDefenseIntentFilter(int allianceId, int removed, int forbidden, string reasons)
        {
            string signature = "alliance=" + allianceId + "|removed=" + removed + "|forbidden=" + forbidden + "|reasons=" + reasons;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Gate, "DefenseIntent", TelemetrySeverity.Info, ev => ev
                .WithAlliance(allianceId)
                .WithDecision("filter-candidates", "forbidden-defense-package", signature)
                .WithField("removed", removed)
                .WithField("forbidden", forbidden)
                .WithField("reasons", reasons));
        }

        private static void EmitDefenseIntentSkipRevert(int allianceId, int unitId, string candidateName, string threatSignature, string reason)
        {
            string safeThreat = string.IsNullOrWhiteSpace(threatSignature) ? "-" : threatSignature;
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "-" : reason;
            string signature = "alliance=" + allianceId + "|unit=" + unitId + "|threat=" + safeThreat + "|reason=" + safeReason;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Gate, "DefenseIntent", TelemetrySeverity.Info, ev => ev
                .WithAlliance(allianceId)
                .WithUnit(candidateName)
                .WithDecision("skip-revert", safeReason, signature)
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
            // _revertLogged is not cleared per call: it dedups the same (alliance,
            // threat-sig, unit) tuple across ticks and is reset per campaign.
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
