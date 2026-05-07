using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Patch #38 - coordinated offensive operation steering.
    // Vanilla CheckOffensiveMovements(int, Regiment, float) at decompile line 14166
    // builds and commits offensive operation packages by iterating
    // aifaction[i].ownunits. We cache the Whiskey package decision per
    // faction/lead/signature, filter ownunits only for the active vanilla call,
    // then restore the list exactly in Postfix.
    [HarmonyPatch(typeof(AICampaign), "CheckOffensiveMovements")]
    internal static class CoordinatedOffensiveOperationsPatch
    {
        private sealed class Snapshot
        {
            internal readonly List<object> OwnUnits = new List<object>();
            internal string Signature;
            internal Stopwatch Watch;
        }

        private sealed class PackageFilterDecision
        {
            internal bool PackageSelected;
            internal readonly HashSet<int> AllowedUnitIds = new HashSet<int>();
            internal string PackageSignature;
        }

        private static readonly Dictionary<int, Snapshot> _snapshots = new Dictionary<int, Snapshot>();
        private static readonly Dictionary<string, PackageFilterDecision> _allowedBySignature =
            new Dictionary<string, PackageFilterDecision>();
        private static readonly Dictionary<Type, FieldInfo> _ownUnitsFields =
            new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> _offensiveFields =
            new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> _defensiveFields =
            new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> _depotFields =
            new Dictionary<Type, FieldInfo>();

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction, Regiment unit, float timediff)
        {
            OnceLog.Info("coordinated-ops:offensive:wired",
                "CoordinatedOffensiveOperationsPatch wired (#38)");

            try
            {
                if (unit == null || timediff <= 0f) return;

                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0 || allianceId > 1) return;
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, GameVars.playeralliance)) return;

                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null) return;

                var ownUnits = GetOwnUnits(faction);
                if (ownUnits == null || ownUnits.Count == 0) return;

                string signature = BuildSignature(allianceId, _aifaction, unit, faction);
                if (!_allowedBySignature.TryGetValue(signature, out var decision))
                {
                    decision = BuildAllowedSet(allianceId, _aifaction, ownUnits, unit, faction);
                    if (_allowedBySignature.Count > 128)
                        _allowedBySignature.Clear();
                    _allowedBySignature[signature] = decision;
                }

                if (!decision.PackageSelected) return;

                var snapshot = new Snapshot
                {
                    Signature = signature,
                    Watch = Stopwatch.StartNew()
                };
                for (int i = 0; i < ownUnits.Count; i++)
                    snapshot.OwnUnits.Add(ownUnits[i]);
                _snapshots[_aifaction] = snapshot;

                for (int i = ownUnits.Count - 1; i >= 0; i--)
                {
                    var obj = ownUnits[i] as UnityEngine.Object;
                    if (obj == null) continue;
                    if (!decision.AllowedUnitIds.Contains(obj.GetInstanceID()))
                        ownUnits.RemoveAt(i);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("coordinated-ops:offensive:prefix",
                    "[CoordinatedOps] offensive Prefix failed: " + ex.Message);
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(int _aifaction)
        {
            try
            {
                if (!_snapshots.TryGetValue(_aifaction, out var snapshot)) return;

                var faction = AICampaignReflect.GetFaction(_aifaction);
                var ownUnits = faction != null ? GetOwnUnits(faction) : null;
                if (ownUnits != null)
                {
                    ownUnits.Clear();
                    for (int i = 0; i < snapshot.OwnUnits.Count; i++)
                        ownUnits.Add(snapshot.OwnUnits[i]);
                }

                snapshot.Watch?.Stop();
                if (snapshot.Watch != null && snapshot.Watch.ElapsedMilliseconds > 5L)
                {
                    Plugin.Log.LogInfo(
                        $"[CoordinatedOps:Perf] offensiveFilterMs={snapshot.Watch.ElapsedMilliseconds} sig={snapshot.Signature}");
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("coordinated-ops:offensive:postfix",
                    "[CoordinatedOps] offensive Postfix failed: " + ex.Message);
            }
            finally
            {
                _snapshots.Remove(_aifaction);
            }
        }

        private static string BuildSignature(int allianceId, int aifactionIndex, Regiment lead, object faction)
        {
            string formationSig = "-";
            var coordinator = StrategicCoordinator.Instance;
            if (coordinator?.FormationDirectives != null && allianceId < coordinator.FormationDirectives.Length)
                formationSig = coordinator.FormationDirectives[allianceId]?.Summary() ?? "-";

            return allianceId + "|" +
                aifactionIndex + "|" +
                StableId(lead) + "|" +
                lead.theaterposition + "|" +
                ListSignature(GetList(faction, "unitsinoffensiveoperations", _offensiveFields)) + "|" +
                ListSignature(GetList(faction, "unitsindefensiveoperations", _defensiveFields)) + "|" +
                ListSignature(GetList(faction, "unitsconstructingsupplydepots", _depotFields)) + "|" +
                formationSig;
        }

        private static PackageFilterDecision BuildAllowedSet(
            int allianceId,
            int aifactionIndex,
            IList ownUnits,
            Regiment lead,
            object faction)
        {
            var decision = new PackageFilterDecision();
            var coordinator = StrategicCoordinator.Instance;
            if (coordinator?.FormationDirectives == null || allianceId >= coordinator.FormationDirectives.Length)
                return decision;

            var ledger = coordinator.FormationDirectives[allianceId];
            if (ledger == null || ledger.Assignments == null || ledger.Assignments.Count == 0)
                return decision;

            var candidates = new List<CoordinatedOperationCandidate>();
            var offensive = GetList(faction, "unitsinoffensiveoperations", _offensiveFields);
            var defensive = GetList(faction, "unitsindefensiveoperations", _defensiveFields);
            var depots = GetList(faction, "unitsconstructingsupplydepots", _depotFields);

            for (int i = 0; i < ledger.Assignments.Count; i++)
            {
                var assignment = ledger.Assignments[i];
                var unit = CoordinatedOperationRuntime.FindUnitById(ownUnits, assignment.StableUnitId);
                if (unit == null) continue;

                var bridgeDecision = WlStrategicOrderBridge.ClassifyOnly(new WlStrategicOrderRequest
                {
                    AllianceId = allianceId,
                    AifactionIndex = aifactionIndex,
                    Unit = unit,
                    TargetPosition = lead.transform.position,
                    TargetName = "Objective",
                    ObjectiveId = -1,
                    Intent = WlStrategicIntent.Offensive,
                    Width = 20f,
                    Depth = 20f,
                    SourceSystem = "CoordinatedOffensive"
                });

                candidates.Add(CoordinatedOperationRuntime.CandidateFromAssignment(
                    assignment,
                    ListContains(offensive, unit),
                    ListContains(defensive, unit),
                    ListContains(depots, unit),
                    CoordinatedOperationRuntime.CommitModeFromBridge(bridgeDecision)));
            }

            var leadAssignment = ledger.GetAssignment(UnitKey(lead));
            float targetStrength = Math.Max(1f, leadAssignment?.LocalEnemyStrength ?? lead.groupstrengthactive);
            var input = new CoordinatedOperationInput
            {
                AllianceId = allianceId,
                IsPlayerCic = false,
                Intent = CoordinatedOperationIntent.Attack,
                TargetName = "Objective",
                TargetAreaKey = leadAssignment?.AreaKey,
                TargetSectorKey = leadAssignment?.SectorKey,
                TargetX = lead.transform.position.x,
                TargetZ = lead.transform.position.z,
                TargetEnemyStrength = targetStrength,
                PreferredLeadStableUnitId = StableId(lead),
                Options = CoordinatedOperationOptions.StableDefaults(targetStrength),
                Candidates = candidates
            };

            var output = CoordinatedOperationPackageLedger.Build(input);
            if (output.Decision == CoordinatedOperationDecision.None ||
                output.Decision == CoordinatedOperationDecision.Delay ||
                output.Decision == CoordinatedOperationDecision.Recover)
                return decision;

            decision.PackageSelected = true;
            decision.PackageSignature = output.Signature();
            AddIfResolved(decision.AllowedUnitIds, ownUnits, output.LeadStableUnitId);
            for (int i = 0; i < output.SupportStableUnitIds.Count; i++)
                AddIfResolved(decision.AllowedUnitIds, ownUnits, output.SupportStableUnitIds[i]);

            if (decision.AllowedUnitIds.Count == 0)
            {
                Plugin.Log.LogWarning(
                    $"[CoordinatedOps] alliance={allianceId} intent=VanillaOffensive decision={output.Decision} " +
                    $"action=package-no-apply target={output.TargetName ?? input.TargetAreaKey ?? "Objective"} " +
                    $"lead={output.LeadDisplayUnitKey} support={output.SupportStableUnitIds.Count} reason=allowed-units-unresolved package={decision.PackageSignature}");
                return decision;
            }

            Plugin.Log.LogInfo(
                $"[CoordinatedOps] alliance={allianceId} intent=VanillaOffensive decision={output.Decision} " +
                $"target={output.TargetName ?? input.TargetAreaKey ?? "Objective"} ratio={output.Ratio:0.00} " +
                $"lead={output.LeadDisplayUnitKey} support={output.SupportStableUnitIds.Count} reason={output.Reason}");
            return decision;
        }

        private static void AddIfResolved(HashSet<int> allowed, IList ownUnits, int stableUnitId)
        {
            var unit = CoordinatedOperationRuntime.FindUnitById(ownUnits, stableUnitId);
            if (unit != null)
                allowed.Add(StableId(unit));
        }

        private static IList GetOwnUnits(object faction)
        {
            return GetList(faction, "ownunits", _ownUnitsFields);
        }

        private static IList GetList(object faction, string fieldName, Dictionary<Type, FieldInfo> cache)
        {
            if (faction == null) return null;
            var type = faction.GetType();
            if (!cache.TryGetValue(type, out var field))
            {
                field = AccessTools.Field(type, fieldName);
                cache[type] = field;
            }
            return field?.GetValue(faction) as IList;
        }

        private static bool ListContains(IList list, Regiment unit)
        {
            return list != null && unit != null && list.Contains(unit);
        }

        private static string ListSignature(IList list)
        {
            if (list == null || list.Count == 0) return "-";
            var ids = new List<int>();
            for (int i = 0; i < list.Count; i++)
            {
                var obj = list[i] as UnityEngine.Object;
                if (obj != null)
                    ids.Add(obj.GetInstanceID());
            }
            ids.Sort();
            return string.Join(",", ids);
        }

        private static int StableId(UnityEngine.Object obj)
        {
            return obj != null ? obj.GetInstanceID() : 0;
        }

        private static string UnitKey(Regiment unit)
        {
            return SafeName(unit) + ":" + ReadInt(unit, "commander");
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
            catch
            {
            }
            return -1;
        }
    }
}
