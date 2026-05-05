using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
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

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction)
        {
            OnceLog.Info("defense-intent:filter:wired",
                "CheckForDefensiveOperationsCandidateFilterPatch wired (#25)");

            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.EnableDefenseIntentLedger.Value) return;

                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0) return;

                var output = StrategicCoordinator.Instance?.DefenseIntents?[allianceId];
                if (output == null || output.Responses == null || output.Responses.Count == 0) return;

                var forbidden = CollectForbiddenIds(output, out var reasonByUnitId);
                if (forbidden.Count == 0) return;

                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null) return;

                var ownUnitsField = AccessTools.Field(faction.GetType(), "ownunits");
                var defOpsField = AccessTools.Field(faction.GetType(), "unitsindefensiveoperations");
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
                }
                _priorTheaterPositionByUnitId[_aifaction] = priorPositions;
                _suppressionReasonByUnitId[_aifaction] = suppressionReasons;

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
                    Plugin.Log.LogInfo(
                        $"[DefenseIntent] filtered alliance={allianceId} removed={removed} forbidden={forbidden.Count}");
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
                    var ownUnitsField = AccessTools.Field(faction.GetType(), "ownunits");
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
                    if (allianceId >= 0)
                    {
                        var output = StrategicCoordinator.Instance?.DefenseIntents?[allianceId];
                        if (output != null)
                        {
                            var forbidden = CollectForbiddenIds(output, out var _unusedReasons);
                            if (forbidden.Count > 0)
                            {
                                var defOpsField = AccessTools.Field(faction.GetType(), "unitsindefensiveoperations");
                                var defOps = defOpsField?.GetValue(faction) as IList;
                                _priorTheaterPositionByUnitId.TryGetValue(_aifaction, out var priorPositions);
                                _suppressionReasonByUnitId.TryGetValue(_aifaction, out var suppressionReasons);

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

                                        defOps.RemoveAt(i);
                                        RemoveFromStaticDefensiveOperation(unit);

                                        if (priorPositions != null && priorPositions.TryGetValue(id, out var priorPos))
                                        {
                                            SafeMoveUnitTo(unit, priorPos);
                                        }

                                        Plugin.Log.LogInfo(
                                            $"[DefenseIntent] reverted alliance={allianceId} " +
                                            $"candidate={(unityObj.name ?? id.ToString())} " +
                                            $"reason={reason}");
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
        // forbidden reasons. Also populates reasonByUnitId for diagnostic logging.
        private static HashSet<int> CollectForbiddenIds(
            DefenseIntentLedgerOutput output,
            out Dictionary<int, string> reasonByUnitId)
        {
            reasonByUnitId = new Dictionary<int, string>();
            var ids = new HashSet<int>();
            if (output?.Responses == null) return ids;

            foreach (var r in output.Responses)
            {
                if (r?.Suppressed == null) continue;
                foreach (var s in r.Suppressed)
                {
                    if (s == null) continue;
                    if (s.Reason != "forbidden-cross-map" &&
                        s.Reason != "critical-front" &&
                        s.Reason != "player-controlled")
                        continue;
                    ids.Add(s.UnitInstanceId);
                    // Last reason wins when a unit appears in multiple responses.
                    reasonByUnitId[s.UnitInstanceId] = s.Reason;
                }
            }
            return ids;
        }

        private static void RemoveFromStaticDefensiveOperation(Regiment unit)
        {
            if (unit == null) return;
            try
            {
                // AICampaign.DefensiveOperation is a private nested class with a static
                // RemoveUnit(Regiment) method (decompile line 10097).
                var nested = AccessTools.Inner(typeof(AICampaign), "DefensiveOperation");
                var method = nested != null
                    ? AccessTools.Method(nested, "RemoveUnit", new[] { typeof(Regiment) })
                    : null;
                if (method != null)
                {
                    method.Invoke(null, new object[] { unit });
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

        private static void ClearSnapshots(int aifactionIndex)
        {
            _ownUnitsSnapshot.Remove(aifactionIndex);
            _defensiveOpsBefore.Remove(aifactionIndex);
            _priorTheaterPositionByUnitId.Remove(aifactionIndex);
            _suppressionReasonByUnitId.Remove(aifactionIndex);
        }
    }
}
