using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Patch #38 - coordinated offensive operation steering.
    // Vanilla CheckOffensiveMovements(int, Regiment, float) at decompile line 14166
    // builds and commits offensive operation packages by iterating
    // aifaction[i].ownunits. When Whiskey selects a coordinated package, this
    // Prefix commits it through CoordinatedOperationRuntime, then empties ownunits
    // only for that vanilla call so vanilla cannot broaden or silently retarget it.
    // Postfix/Finalizer restore the list exactly.
    [HarmonyPatch(typeof(AICampaign), "CheckOffensiveMovements")]
    internal static class CoordinatedOffensiveOperationsPatch
    {
        private sealed class Snapshot
        {
            internal readonly List<object> OwnUnits = new List<object>();
            internal string Signature;
            internal Stopwatch Watch;

            internal void Restore(IList ownUnits)
            {
                if (ownUnits == null) return;
                ownUnits.Clear();
                for (int i = 0; i < OwnUnits.Count; i++)
                    ownUnits.Add(OwnUnits[i]);
            }
        }

        private sealed class PackageFilterDecision
        {
            internal bool PackageSelected;
            internal CoordinatedOperationOutput Output;
            internal Vector3 Target;
            internal string TargetName;
            internal string PackageSignature;
        }

        private static readonly Dictionary<int, Snapshot> _snapshots = new Dictionary<int, Snapshot>();
        private static readonly Dictionary<Type, FieldInfo> _ownUnitsFields =
            new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> _offensiveFields =
            new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> _defensiveFields =
            new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> _depotFields =
            new Dictionary<Type, FieldInfo>();
        private static bool _wiredLogged;

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction, Regiment unit, float timediff)
        {
            using (TelemetryPerf.Scope("tactical.patch.coord-offensive-ops-prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
            if (!_wiredLogged)
            {
                _wiredLogged = true;
                CoordinatedOperationRuntime.EmitCoordinatedOps(
                    TelemetryCategory.State,
                    TelemetrySeverity.Info,
                    -1,
                    "observer",
                    "offensive-patch",
                    "patch=38",
                    ev => ev
                        .WithField("observer", "offensive")
                        .WithField("patch", 38));
            }

            IList ownUnits = null;
            bool snapshotRegistered = false;
            try
            {
                if (unit == null || timediff <= 0f) return;

                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0 || allianceId > 1) return;
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, GameVars.playeralliance)) return;

                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null) return;

                ownUnits = GetOwnUnits(faction);
                if (ownUnits == null || ownUnits.Count == 0) return;

                string signature = BuildSignature(allianceId, _aifaction, unit, faction);
                var decision = BuildAllowedSet(allianceId, _aifaction, ownUnits, unit, faction);

                if (!decision.PackageSelected) return;

                var snapshot = new Snapshot
                {
                    Signature = signature,
                    Watch = Stopwatch.StartNew()
                };
                for (int i = 0; i < ownUnits.Count; i++)
                    snapshot.OwnUnits.Add(ownUnits[i]);
                _snapshots[_aifaction] = snapshot;
                snapshotRegistered = true;

                bool committed = CoordinatedOperationRuntime.CommitPackage(
                    allianceId,
                    _aifaction,
                    decision.Output,
                    decision.Target,
                    decision.TargetName,
                    -1,
                    WlStrategicIntent.Offensive,
                    "VanillaOffensive");

                if (committed)
                {
                    SetBool(faction, "recruitingzonesupdated", false);
                    EmitCoordinatedOffensivePackage(
                        allianceId,
                        "commit-package",
                        TelemetryCategory.Write,
                        TelemetrySeverity.Info,
                        decision,
                        null);
                }
                else
                {
                    EmitCoordinatedOffensivePackage(
                        allianceId,
                        "package-no-commit",
                        TelemetryCategory.Gate,
                        TelemetrySeverity.Warning,
                        decision,
                        "commit-failed");
                    OnceLog.Warning(
                        "coordinated-ops:offensive:package-no-commit:" + allianceId + ":" + (decision?.PackageSignature ?? "-"),
                        "Coordinated operation package did not commit alliance=" + allianceId +
                        " reason=commit-failed package=" + (decision?.PackageSignature ?? "-"));
                }

                ownUnits.Clear();
            }
            catch (Exception ex)
            {
                CoordinatedOperationRuntime.EmitCoordinatedOps(
                    TelemetryCategory.Failure,
                    TelemetrySeverity.Warning,
                    -1,
                    "prefix-failed",
                    ex.Message,
                    "aifaction=" + _aifaction);
                OnceLog.Warning("coordinated-ops:offensive:prefix",
                    "Patch CoordinatedOps offensive Prefix failed: " + ex.Message);
                if (snapshotRegistered && _snapshots.TryGetValue(_aifaction, out var snapshot))
                {
                    try
                    {
                        snapshot.Restore(ownUnits);
                        _snapshots.Remove(_aifaction);
                    }
                    catch
                    {
                    }
                }
                else
                {
                    CoordinatedOperationRuntime.EmitCoordinatedOps(
                        TelemetryCategory.Failure,
                        TelemetrySeverity.Warning,
                        -1,
                        "prefix-failed-before-snapshot",
                        "candidate-list-unchanged",
                        "aifaction=" + _aifaction);
                    OnceLog.Warning(
                        "coord-offensive:no-snapshot:" + _aifaction,
                        "Patch CoordinatedOps prefix failed before snapshot; vanilla candidate list left unchanged");
                }
            }
            } // end using TelemetryPerf.Scope
        }

        [HarmonyPostfix]
        internal static void Postfix(int _aifaction)
        {
            using (TelemetryPerf.Scope("tactical.patch.coord-offensive-ops", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                RestoreSnapshot(_aifaction, "postfix");
            }
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception, int _aifaction)
        {
            using (TelemetryPerf.Scope("tactical.patch.coord-offensive-ops-finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                RestoreSnapshot(_aifaction, "finalizer");
                return __exception;
            }
        }

        private static void RestoreSnapshot(int aifactionIndex, string source)
        {
            try
            {
                if (!_snapshots.TryGetValue(aifactionIndex, out var snapshot)) return;

                var faction = AICampaignReflect.GetFaction(aifactionIndex);
                var ownUnits = faction != null ? GetOwnUnits(faction) : null;
                if (ownUnits != null)
                    snapshot.Restore(ownUnits);

                snapshot.Watch?.Stop();
                if (snapshot.Watch != null && snapshot.Watch.ElapsedMilliseconds > 5L)
                {
                    CoordinatedOperationRuntime.EmitCoordinatedOps(
                        TelemetryCategory.Performance,
                        TelemetrySeverity.Info,
                        -1,
                        "offensive-filter",
                        "slow-filter",
                        snapshot.Signature,
                        ev => ev
                            .WithDurationMs(snapshot.Watch.ElapsedMilliseconds)
                            .WithField("offensiveFilterMs", (int)snapshot.Watch.ElapsedMilliseconds)
                            .WithField("signature", snapshot.Signature ?? "-"));
                }
            }
            catch (Exception ex)
            {
                CoordinatedOperationRuntime.EmitCoordinatedOps(
                    TelemetryCategory.Failure,
                    TelemetrySeverity.Warning,
                    -1,
                    "restore-failed",
                    ex.Message,
                    "aifaction=" + aifactionIndex + "|source=" + source);
                OnceLog.Warning("coordinated-ops:offensive:restore:" + source,
                    "Patch CoordinatedOps offensive restore failed: " + ex.Message);
            }
            finally
            {
                _snapshots.Remove(aifactionIndex);
            }
        }

        private static string BuildSignature(int allianceId, int aifactionIndex, Regiment lead, object faction)
        {
            string formationSig = "-";
            string formationDataSig = "-";
            var coordinator = StrategicCoordinator.Instance;
            if (coordinator?.FormationDirectives != null && allianceId < coordinator.FormationDirectives.Length)
            {
                var ledger = coordinator.FormationDirectives[allianceId];
                formationSig = ledger?.Summary() ?? "-";
                formationDataSig = AssignmentDataSignature(ledger);
            }

            return allianceId + "|" +
                aifactionIndex + "|" +
                StableId(lead) + "|" +
                lead.theaterposition + "|" +
                ListSignature(GetList(faction, "unitsinoffensiveoperations", _offensiveFields)) + "|" +
                ListSignature(GetList(faction, "unitsindefensiveoperations", _defensiveFields)) + "|" +
                ListSignature(GetList(faction, "unitsconstructingsupplydepots", _depotFields)) + "|" +
                formationSig + "|" +
                formationDataSig + "|" +
                WlChainSignature();
        }

        private static void EmitCoordinatedOffensivePackage(
            int allianceId,
            string action,
            TelemetryCategory category,
            TelemetrySeverity severity,
            PackageFilterDecision decision,
            string reasonOverride)
        {
            string reason = string.IsNullOrWhiteSpace(reasonOverride)
                ? (decision?.Output?.Reason ?? "-")
                : reasonOverride;
            string packageSignature = decision?.PackageSignature ?? decision?.Output?.Signature() ?? "-";
            CoordinatedOperationRuntime.EmitCoordinatedOps(
                category,
                severity,
                allianceId,
                action,
                reason,
                packageSignature,
                ev => ev
                    .WithField("intent", "VanillaOffensive")
                    .WithField("decision", decision?.Output != null ? decision.Output.Decision.ToString() : "-")
                    .WithField("target", decision?.TargetName ?? "-")
                    .WithField("ratio", decision?.Output != null ? decision.Output.Ratio : 0.0)
                    .WithField("lead", decision?.Output?.LeadDisplayUnitKey ?? "-")
                    .WithField("support", decision?.Output?.SupportStableUnitIds != null ? decision.Output.SupportStableUnitIds.Count : 0));
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
            var leadAssignment = ledger.GetAssignment(UnitKey(lead));
            var vanillaTarget = ResolveVanillaOffensiveTarget(aifactionIndex, lead);
            if (!vanillaTarget.HasValue)
                return decision;
            string targetName = CoordinatedOperationRuntime.ResolveTargetName(
                -1,
                leadAssignment?.AreaKey,
                coordinator.CampaignMap,
                vanillaTarget.Value);

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
                    TargetPosition = vanillaTarget.Value,
                    TargetName = targetName,
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

            float targetStrength = Math.Max(1f, leadAssignment?.LocalEnemyStrength ?? lead.groupstrengthactive);
            var input = new CoordinatedOperationInput
            {
                AllianceId = allianceId,
                IsPlayerCic = false,
                Intent = CoordinatedOperationIntent.Attack,
                TargetName = targetName,
                TargetAreaKey = leadAssignment?.AreaKey,
                TargetSectorKey = leadAssignment?.SectorKey,
                TargetX = vanillaTarget.Value.x,
                TargetZ = vanillaTarget.Value.z,
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
            decision.Output = output;
            decision.Target = vanillaTarget.Value;
            decision.TargetName = targetName;
            decision.PackageSignature = output.Signature();
            return decision;
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

        private static string AssignmentDataSignature(FormationDirectiveLedger ledger)
        {
            if (ledger?.Assignments == null || ledger.Assignments.Count == 0) return "-";
            var parts = new List<string>();
            foreach (var assignment in ledger.Assignments)
            {
                if (assignment == null) continue;
                parts.Add(
                    assignment.StableUnitId + ":" +
                    Round(assignment.CombatAvailability) + ":" +
                    Round(assignment.ExchangePressure) + ":" +
                    Round(assignment.LocalEnemyStrength) + ":" +
                    Round(assignment.Readiness) + ":" +
                    Round(assignment.Morale) + ":" +
                    Round(assignment.Ammo) + ":" +
                    Round(assignment.Supply) + ":" +
                    Round(assignment.Fatigue) + ":" +
                    (assignment.OffensiveAllowed ? "O" : "-") +
                    (assignment.DefensiveAllowed ? "D" : "-") +
                    (assignment.TransferDonorAllowed ? "T" : "-") +
                    (assignment.DirectMovementAllowed ? "M" : "-"));
            }
            parts.Sort(StringComparer.Ordinal);
            return string.Join(",", parts);
        }

        private static Vector3? ResolveVanillaOffensiveTarget(int aifactionIndex, Regiment lead)
        {
            try
            {
                if (lead == null) return null;
                var aiArea = ResolveAIArea(lead.GetPosition());
                if (aiArea == null) return null;

                float enemyStrength = ReadIndexedFloat(aiArea, "enemycampaignunitsstrengthclose", aifactionIndex) *
                    ReadIndexedFloat(aiArea, "avgenemytroopmoraleinzone", aifactionIndex) *
                    GamePrefs.aiimportanceenemyunits;
                object targetArea = enemyStrength == 0f
                    ? ReadIndexedObject(aiArea, "mostvaluealbeaiareaclosedistanceonly", aifactionIndex)
                    : ReadIndexedObject(aiArea, "mostvalueableaiareaclose", aifactionIndex);
                if (targetArea == null) return null;

                var method = AccessTools.Method(
                    typeof(AICampaign),
                    "GetClosestObjectivePosOfArea",
                    new[] { typeof(Vector3), AccessTools.TypeByName("AIArea"), typeof(int), typeof(bool) });
                if (method == null) return null;

                var result = method.Invoke(null, new object[] { lead.GetPosition(), targetArea, aifactionIndex, true });
                if (!(result is Vector3 target) || target == default(Vector3)) return null;
                return target;
            }
            catch (Exception ex)
            {
                CoordinatedOperationRuntime.EmitCoordinatedOps(
                    TelemetryCategory.Failure,
                    TelemetrySeverity.Warning,
                    -1,
                    "target-resolve-failed",
                    ex.Message,
                    "aifaction=" + aifactionIndex);
                OnceLog.Warning("coordinated-ops:offensive:target",
                    "Patch CoordinatedOps vanilla target resolve failed: " + ex.Message);
                return null;
            }
        }

        private static object ResolveAIArea(Vector3 position)
        {
            var aiareas = AccessTools.Field(typeof(AICampaign), "aiareas")?.GetValue(null);
            if (aiareas == null) return null;
            var getColorOnPos = AccessTools.Method(aiareas.GetType(), "GetColorOnPos", new[] { typeof(Vector3), typeof(float) });
            var color = getColorOnPos?.Invoke(aiareas, new object[] { position, -1f });
            if (color == null) return null;
            var getAIArea = AccessTools.Method(AccessTools.TypeByName("AIArea"), "GetAIArea", new[] { typeof(Color) });
            return getAIArea?.Invoke(null, new[] { color });
        }

        private static float ReadIndexedFloat(object target, string fieldName, int index)
        {
            var value = AccessTools.Field(target.GetType(), fieldName)?.GetValue(target);
            if (value is Array arr && index >= 0 && index < arr.Length)
                return Convert.ToSingle(arr.GetValue(index));
            return 0f;
        }

        private static object ReadIndexedObject(object target, string fieldName, int index)
        {
            var value = AccessTools.Field(target.GetType(), fieldName)?.GetValue(target);
            if (value is Array arr && index >= 0 && index < arr.Length)
                return arr.GetValue(index);
            return null;
        }

        private static string WlChainSignature()
        {
            try
            {
                if (!DLC_WL.dlc_scenarioactive) return "wl-off";
                return GameVars.playeralliance + ":" +
                    DLC_WL.dlc_chosencommander + ":" +
                    DLC_WL.IsCommanderInChief();
            }
            catch
            {
                return "wl-unknown";
            }
        }

        private static string Round(float value)
        {
            return Math.Round(value, 1).ToString(CultureInfo.InvariantCulture);
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

        private static void SetBool(object target, string field, bool value)
        {
            try
            {
                AccessTools.Field(target.GetType(), field)?.SetValue(target, value);
            }
            catch
            {
            }
        }
    }
}
