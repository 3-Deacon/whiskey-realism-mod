using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    // Coordinator-driven custom defensive movement order runner.
    //
    // Runs after DefenseIntentLedger.Build emits its responses for an
    // alliance. Commits selected packages where vanilla CheckForDefensiveOperations
    // would not (rate-limited landings, relaxed-filter CoastalGuard candidates
    // vanilla's downstream gates would reject). Does NOT touch the static
    // AICampaign.defensiveoperations list — that surface is owned by
    // vanilla's commit path. Release happens on a future tick when the
    // builder transitions to Recovered + cooldown expires.
    //
    // 2026-10-14 (C-Coastal): commits now mirror vanilla's defensive-operation
    // commit (decompile 13713-13719): queue an AICampaign.DefensiveMovingOrder
    // (executed by UpdateOrders, 79295), AIBattle.CheckCurrentOrderUpdate type 7,
    // remove from unitsinoffensiveoperations, add to unitsindefensiveoperations.
    // The destination keeps the nearest enemy inside 0.8 x buglerange, because
    // vanilla CheckForEndOfDefensiveOperations (13836) stops and drops any
    // defensive-op unit with no enemy within buglerange of its destination - the
    // old asset-anchored MoveUnitTo produced a daily stop/go. Release mirrors
    // vanilla's end of operation (RemoveOrder, StopRegiment, remove).
    internal static class CoastalDefenseCustomOrderRunner
    {
        static CoastalDefenseCustomOrderRunner()
        {
            CampaignScope.Register("coastal-defense", ResetForNewCampaign);
        }

        // Per-alliance dedup of "first assignment per (threat, unit) tuple"
        // log lines — keys are "<allianceId>|<threatSignature>|<unitInstanceId>".
        private static readonly HashSet<string> _firstAssignmentLogged = new HashSet<string>();

        // Tracks which unit IDs were assigned per (allianceId, threatSignature) on the
        // prior tick. Used to detect when a threat signature disappears from the current
        // tick's output and release the previously-assigned units from defensiveoperations.
        // Key: allianceId → threatSignature → set of UnitInstanceIds.
        // Cleared per campaign (CampaignScope).
        private static readonly Dictionary<int, Dictionary<string, HashSet<int>>> _assignedByThreat =
            new Dictionary<int, Dictionary<string, HashSet<int>>>();
        private static readonly HashSet<string> _wlCurrentOrderAttempts = new HashSet<string>();
        private static readonly HashSet<string> _logOnceKeys = new HashSet<string>();

        internal static void ResetForNewCampaign()
        {
            _firstAssignmentLogged.Clear();
            _assignedByThreat.Clear();
            _wlCurrentOrderAttempts.Clear();
            _logOnceKeys.Clear();
        }

        // True while this runner holds `unitInstanceId` on a custom defensive
        // order for any live threat signature of `allianceId`. Other steering
        // (capital pledges) must not pull such units away.
        internal static bool IsCustomOrdered(int allianceId, int unitInstanceId)
        {
            if (!_assignedByThreat.TryGetValue(allianceId, out var bySignature)) return false;
            foreach (var units in bySignature.Values)
                if (units != null && units.Contains(unitInstanceId)) return true;
            return false;
        }

        private static bool HasAssignments(int allianceId)
        {
            if (!_assignedByThreat.TryGetValue(allianceId, out var bySignature)) return false;
            foreach (var units in bySignature.Values)
                if (units != null && units.Count > 0) return true;
            return false;
        }

        internal static void Run(int allianceId, DefenseIntentLedgerOutput output)
        {
            try
            {
                // Alliance 2+ (Europe, neutral observers) should never receive custom
                // defensive movement orders. Our per-alliance state structures are sized
                // for Union=0 and CSA=1 only; reject anything outside that range.
                if (allianceId < 0 || allianceId > 1) return;
                var responses = output?.Responses ?? new List<DefenseResponse>();
                // With no responses there is nothing to issue, but units held from
                // earlier ticks must still be released below.
                if (responses.Count == 0 && !HasAssignments(allianceId)) return;

                int aifactionIndex = ResolveAifactionIndex(allianceId);
                if (aifactionIndex < 0) return;

                var faction = AICampaignReflect.GetFaction(aifactionIndex);
                if (faction == null) return;

                var defOps = VanillaFieldCache.List(faction, "unitsindefensiveoperations");
                var ownUnits = VanillaFieldCache.List(faction, "ownunits");
                if (defOps == null || ownUnits == null) return;
                var offensive = VanillaFieldCache.Get(faction, "unitsinoffensiveoperations") as IList;
                var beliefs = EnemyDetectionRuntime.BeliefsFor(allianceId);

                bool verbose = Plugin.Instance != null && Plugin.Instance.DefenseIntentVerboseLogging.Value;

                // Build this tick's assignment map: signature → set of assigned unit IDs.
                // Populated during the assignment loop below; used afterwards for release tracking.
                var thisTick = new Dictionary<string, HashSet<int>>();

                var chainContext = WlChainContext.Capture();
                foreach (var response in responses)
                {
                    if (response?.SelectedPackage == null || response.SelectedPackage.Count == 0) continue;
                    if (!DefenseCustomOrderPolicy.RequiresCustomOrder(response)) continue;

                    string sig = response.Threat?.Signature ?? "<no-sig>";

                    foreach (var candidate in response.SelectedPackage)
                    {
                        var unit = FindUnitByInstanceId(ownUnits, candidate.UnitInstanceId);
                        if (unit == null) continue;
                        if (defOps.Contains(unit)) continue;
                        // WlChainRules: the player's own command and a CIC side are never
                        // tasked; his map group gets the order as a dispatch (bridge);
                        // the AI group carrying him is queued like any AI unit.
                        var chain = WlStrategicOrderBridge.ChainClassOf(in chainContext, allianceId, unit);
                        if (!WlChainRules.MayMove(chain) && !WlChainRules.OrderOnly(chain)) continue;
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
                            Unit = unit,
                            TargetPosition = anchor,
                            TargetName = sig,
                            Intent = WlStrategicIntent.EngageEnemy,
                            Width = 20f,
                            Depth = 20f
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
                            var destination = InterceptDestination(beliefs, anchor, unit);
                            string orderName = string.IsNullOrEmpty(response.Threat.AssetName) ? "Coastal defense" : response.Threat.AssetName;
                            if (!QueueVanillaDefensiveMove(unit, destination, orderName))
                            {
                                EmitCampaignInfoOnce(
                                    $"defense-intent:custom-order:move-skip:{allianceId}:{sig}:{candidate.UnitInstanceId}",
                                    $"[DefenseIntent] source=CoastalDefense alliance={allianceId} action=skip-direct-move unit={SafeName(unit, candidate.UnitInstanceId)} reason=defensive-order-failed");
                                continue;
                            }

                            offensive?.Remove(unit);
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
                foreach (var response in responses)
                {
                    string sig = response?.Threat?.Signature;
                    if (string.IsNullOrEmpty(sig)) continue;
                    if (!thisTick.ContainsKey(sig))
                        thisTick[sig] = new HashSet<int>(); // empty sentinel — signature is still active
                }

                // Carry units committed on earlier ticks (still in defensive ops) under
                // their live signatures, then release units for threat signatures that
                // have fully disappeared from this tick's output.
                if (_assignedByThreat.TryGetValue(allianceId, out var prior))
                {
                    CampaignMilitaryGates.CarryForwardCustomOrders(prior, thisTick, id =>
                    {
                        var held = FindUnitByInstanceId(ownUnits, id);
                        return held != null && defOps.Contains(held);
                    });

                    foreach (var kv in prior)
                    {
                        if (thisTick.ContainsKey(kv.Key)) continue; // signature still active — keep units assigned
                        foreach (var unitId in kv.Value)
                        {
                            var unit = FindUnitByInstanceId(ownUnits, unitId);
                            _wlCurrentOrderAttempts.Remove($"{allianceId}|{kv.Key}|{unitId}");
                            if (unit == null) continue;
                            if (!defOps.Contains(unit)) continue;
                            EndDefensiveOperation(unit, defOps);
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
            try { return TelemetryLegacyParser.Quote(((UnityEngine.Object)unit).name); }   // a row value: quoted when the name has a space
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

        // Vanilla defensive-operation commit (decompile 13713-13719).
        private static bool QueueVanillaDefensiveMove(Regiment unit, Vector3 destination, string orderName)
        {
            if (unit == null) return false;
            try
            {
                if (!unit.dlcw_isundercommander || !DLC_WL.dlc_scenarioactive)
                {
                    AICampaign.DefensiveMovingOrder.RemoveOrder(unit);
                    // The ctor creates AICampaign.orders when null (9911) but does not enqueue itself.
                    var order = new AICampaign.DefensiveMovingOrder(
                        unit, destination, 30f, (float)unit.groupstrengthactive * unit.groupmorale, _alwaysblockforts: false);
                    AICampaign.orders.Add(order);
                }
                AIBattle.CheckCurrentOrderUpdate(unit, 7, destination, orderName, -1f, 20f, 20f, calledfromcampaign: true);
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defense-intent:custom-order:queue",
                    "CoastalDefenseCustomOrderRunner defensive order failed: " + ex.Message);
                return false;
            }
        }

        // Vanilla CheckForEndOfDefensiveOperations (13836): units already stopped,
        // retreating or in battle are just dropped; moving ones are stopped first.
        private static void EndDefensiveOperation(Regiment unit, IList defOps)
        {
            try
            {
                AICampaign.DefensiveMovingOrder.RemoveOrder(unit);
                bool moving = unit.regimentpaths > 0 && !unit.onretreat && !unit.inbattle;
                if (moving && (!unit.dlcw_isundercommander || !DLC_WL.dlc_scenarioactive))
                    unit.StopRegiment(skipfinalrotation: true, manualstop: true);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defense-intent:custom-order:release",
                    "CoastalDefenseCustomOrderRunner release stop failed: " + ex.Message);
            }
            defOps.Remove(unit);
        }

        // The nearest believed enemy to the threat anchor (G1: fog-honest, its last-seen position).
        private static Vector3 InterceptDestination(IReadOnlyList<EnemyBelief> beliefs, Vector3 anchor, Regiment unit)
        {
            Vector3 enemy = anchor;
            int nearest = EnemyBeliefQuery.Nearest(beliefs, anchor.x, anchor.z, out float distance);
            if (nearest >= 0) enemy = new Vector3(beliefs[nearest].X, anchor.y, beliefs[nearest].Z);
            float best = nearest >= 0 ? distance * distance : float.MaxValue;
            float searchRadius = GamePrefs.maxdistancedefensiveoperations;
            if (best > searchRadius * searchRadius) enemy = anchor;

            CampaignMilitaryGates.CoastalInterceptPoint(anchor.x, anchor.z, enemy.x, enemy.z, unit.buglerange, out float x, out float z);
            return new Vector3(x, anchor.y, z);
        }

        private const int AifactionScanLimit = 8;

        private static int ResolveAifactionIndex(int allianceId)
        {
            // AICampaign.aifaction holds one entry per alliance (incl. Europe = 2).
            for (int i = 0; i < AifactionScanLimit; i++)
                if (AICampaignReflect.GetAllianceId(i) == allianceId) return i;
            return -1;
        }

        private static void EmitCampaignInfoOnce(string key, string line)
        {
            if (!_logOnceKeys.Add(key ?? string.Empty)) return;
            TelemetryRouter.LegacyInfo(line, TelemetryLayer.Campaign);
        }

    }
}
