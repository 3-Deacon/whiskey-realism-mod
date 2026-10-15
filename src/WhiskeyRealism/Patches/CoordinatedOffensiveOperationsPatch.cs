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
    // aifaction[i].ownunits. When Whiskey selects AND commits a coordinated
    // package, this Prefix empties ownunits only for that vanilla call so vanilla
    // cannot broaden or silently retarget it. The Finalizer (runs on success and on
    // exceptions) restores the list.
    //
    // 2026-10-14 (C-CoordOff), aligned with vanilla:
    //   - vanilla's early return (14200) is mirrored first, incl.
    //     !recruitingzonesupdated (<= 1 offensive commit per faction job cycle),
    //     before any signature/classify work;
    //   - candidates must pass IsUnitAvailableForOffensiveOperations (14080) for
    //     the vanilla target (OffensiveAvailabilityWrapper), so packages are not
    //     built from units the commit preflight then rejects;
    //   - vanilla's tempo roll (chapter aggression, faction aggression, winter,
    //     idle time, W&L multiplier, scripted forcing; 14316-14350) gates commits
    //     into enemy-held states with enemy present;
    //   - ownunits is emptied only when the package actually committed; a failed
    //     or deferred package leaves vanilla's own offensive logic untouched.
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
            internal bool EnemyPresentAtTarget;
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
                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0 || allianceId > 1) return;
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, GameVars.playeralliance)) return;

                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null) return;

                ownUnits = GetOwnUnits(faction);
                if (!VanillaWouldEvaluate(faction, ownUnits, unit, timediff)) return;

                var decision = BuildAllowedSet(allianceId, _aifaction, ownUnits, unit, faction);
                if (!decision.PackageSelected) return;

                // Fails most calls by design (vanilla's own roll does too); stay
                // silent here - this runs per unit per AI job cycle.
                if (!PassesVanillaTempoGate(allianceId, decision, ownUnits, timediff)) return;

                var snapshot = new Snapshot
                {
                    Signature = BuildSignature(allianceId, _aifaction, unit, faction),
                    Watch = Stopwatch.StartNew()
                };
                for (int i = 0; i < ownUnits.Count; i++)
                    snapshot.OwnUnits.Add(ownUnits[i]);

                bool committed = CoordinatedOperationRuntime.CommitPackage(
                    allianceId,
                    _aifaction,
                    decision.Output,
                    decision.Target,
                    decision.TargetName,
                    -1,
                    WlStrategicIntent.Offensive,
                    "VanillaOffensive");

                if (!committed)
                {
                    // Vanilla keeps the full candidate list and runs its own logic.
                    EmitCoordinatedOffensivePackage(
                        allianceId,
                        "package-no-commit",
                        TelemetryCategory.Gate,
                        TelemetrySeverity.Warning,
                        decision,
                        "commit-failed");
                    OnceLog.Warning(
                        "coordinated-ops:offensive:package-no-commit",
                        "Coordinated operation package did not commit alliance=" + allianceId +
                        " reason=commit-failed package=" + (decision?.PackageSignature ?? "-"));
                    return;
                }

                _snapshots[_aifaction] = snapshot;
                snapshotRegistered = true;
                // Vanilla clears this flag after its own offensive pass (14458).
                SetBool(faction, "recruitingzonesupdated", false);
                EmitCoordinatedOffensivePackage(
                    allianceId,
                    "commit-package",
                    TelemetryCategory.Write,
                    TelemetrySeverity.Info,
                    decision,
                    null);

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
                if (snapshotRegistered && _snapshots.TryGetValue(_aifaction, out var registered))
                {
                    try
                    {
                        registered.Restore(ownUnits);
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

        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception, int _aifaction)
        {
            using (TelemetryPerf.Scope("tactical.patch.coord-offensive-ops-finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                RestoreSnapshot(_aifaction);
                return __exception;
            }
        }

        private static void RestoreSnapshot(int aifactionIndex)
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
                    "aifaction=" + aifactionIndex);
                OnceLog.Warning("coordinated-ops:offensive:restore",
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
                // Same eligibility vanilla applies to every unit it adds (14246, 14268).
                if (!OffensiveAvailabilityWrapper.IsAvailable(aifactionIndex, unit, vanillaTarget.Value)) continue;

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
            decision.EnemyPresentAtTarget = (leadAssignment?.LocalEnemyStrength ?? 0f) > 0f;
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

        private static bool _closestObjectivePosResolved;
        private static MethodInfo _closestObjectivePos;
        private static bool _recruitingZonesFieldResolved;
        private static FieldInfo _recruitingZonesField;
        private static bool _bunitsFieldResolved;
        private static FieldInfo _bunitsField;

        // Mirrors vanilla's target choice (14204-14227): the lead's AIArea picks
        // mostvaluealbeaiareaclosedistanceonly with no enemy in the area,
        // otherwise mostvalueableaiareaclose; then GetClosestObjectivePosOfArea.
        private static Vector3? ResolveVanillaOffensiveTarget(int aifactionIndex, Regiment lead)
        {
            try
            {
                if (lead == null || AICampaign.aiareas == null) return null;
                var aiArea = AICampaign.AIArea.GetAIArea(AICampaign.aiareas.GetColorOnPos(lead.GetPosition()));
                if (aiArea == null) return null;
                if (!InRange(aiArea.enemycampaignunitsstrengthclose, aifactionIndex) ||
                    !InRange(aiArea.avgenemytroopmoraleinzone, aifactionIndex))
                    return null;

                float enemyStrength = aiArea.enemycampaignunitsstrengthclose[aifactionIndex] *
                    aiArea.avgenemytroopmoraleinzone[aifactionIndex] *
                    GamePrefs.aiimportanceenemyunits;
                var picks = enemyStrength == 0f ? aiArea.mostvaluealbeaiareaclosedistanceonly : aiArea.mostvalueableaiareaclose;
                if (picks == null || aifactionIndex < 0 || aifactionIndex >= picks.Length) return null;
                var targetArea = picks[aifactionIndex];
                if (targetArea == null) return null;

                if (!_closestObjectivePosResolved)
                {
                    _closestObjectivePosResolved = true;
                    _closestObjectivePos = AccessTools.Method(
                        typeof(AICampaign),
                        "GetClosestObjectivePosOfArea",
                        new[] { typeof(Vector3), typeof(AICampaign.AIArea), typeof(int), typeof(bool) });
                    if (_closestObjectivePos == null)
                        OnceLog.Warning("coordinated-ops:offensive:target-method",
                            "Patch CoordinatedOps: AICampaign.GetClosestObjectivePosOfArea not found");
                }
                if (_closestObjectivePos == null) return null;

                var result = _closestObjectivePos.Invoke(null, new object[] { lead.GetPosition(), targetArea, aifactionIndex, true });
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

        private static bool InRange(float[] values, int index)
        {
            return values != null && index >= 0 && index < values.Length;
        }

        private static bool VanillaWouldEvaluate(object faction, IList ownUnits, Regiment lead, float timediff)
        {
            bool leadPresent = (UnityEngine.Object)(object)lead != (UnityEngine.Object)null;
            bool leadActive = leadPresent && ((Component)lead).gameObject.activeInHierarchy;
            return CoordinatedOffensiveVanillaGates.VanillaWouldEvaluate(
                ownUnits?.Count ?? 0,
                timediff,
                leadPresent,
                leadActive,
                ReadRecruitingZonesUpdated(faction),
                leadPresent && ListContains(GetList(faction, "unitsinoffensiveoperations", _offensiveFields), lead),
                leadPresent && ListContains(GetList(faction, "unitsindefensiveoperations", _defensiveFields), lead),
                leadPresent && ListContains(GetList(faction, "unitsconstructingsupplydepots", _depotFields), lead));
        }

        private static bool ReadRecruitingZonesUpdated(object faction)
        {
            if (!_recruitingZonesFieldResolved)
            {
                _recruitingZonesFieldResolved = true;
                _recruitingZonesField = AccessTools.Field(faction.GetType(), "recruitingzonesupdated");
                if (_recruitingZonesField == null)
                    OnceLog.Warning("coordinated-ops:offensive:recruitingzones",
                        "Patch CoordinatedOps: AIFaction.recruitingzonesupdated not found; packages disabled");
            }
            if (_recruitingZonesField == null) return false;
            try { return Convert.ToBoolean(_recruitingZonesField.GetValue(faction)); }
            catch { return false; }
        }

        // Vanilla tempo roll (14316-14350), applied when the target lies in a state
        // owned by another alliance and enemy strength is present at the target.
        private static bool PassesVanillaTempoGate(int allianceId, PackageFilterDecision decision, IList ownUnits, float timediff)
        {
            try
            {
                if (!decision.EnemyPresentAtTarget) return true;
                int state = BattlefieldSetup.GetStateOfField(decision.Target, usecloseststate: true);
                if (state < 0 || GameVars.nation == null || state >= GameVars.nation.Length) return true;
                if (GameVars.nation[state].alliance == allianceId) return true;

                float date = GameVars.GetCampaignFloatingDate();
                bool forced = GameVars.debug_permanentaimovement ||
                    (GamePrefs.leveltoload == "001" && !CampaignObjective.GetObjectiveAccomplishment(6) &&
                     allianceId == 0 && date > 1861.513f);

                int chapter = Policy.CurrentChapter;
                float chapterAggression = GamePrefs.aiagressiveness != null && chapter >= 0 && chapter < GamePrefs.aiagressiveness.Length
                    ? GamePrefs.aiagressiveness[chapter] : 0f;
                float factionAggression = GamePrefs.aiagressivenessoffensiveoperationfaction != null &&
                    allianceId < GamePrefs.aiagressivenessoffensiveoperationfaction.Length
                    ? GamePrefs.aiagressivenessoffensiveoperationfaction[allianceId] : 1f;
                float wlMultiplier = DLC_WL.dlc_scenarioactive ? GamePrefs.dlcwl_careerincreasedaggressiveness : 1f;

                float tval = CoordinatedOffensiveVanillaGates.TempoProbability(
                    chapterAggression,
                    timediff,
                    factionAggression,
                    GameVars.usedcampaignagressiveness,
                    WinterModifier(),
                    date - AverageTimeOfNotMoving(decision.Output, ownUnits),
                    wlMultiplier);
                return CoordinatedOffensiveVanillaGates.TempoPasses(UnityEngine.Random.Range(0f, 1f), tval, forced);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("coordinated-ops:offensive:tempo",
                    "Patch CoordinatedOps tempo gate failed (package held back): " + ex.Message);
                return false;
            }
        }

        private static float WinterModifier()
        {
            if (!_bunitsFieldResolved)
            {
                _bunitsFieldResolved = true;
                _bunitsField = AccessTools.Field(typeof(AICampaign), "bunits");
            }
            var bunits = _bunitsField?.GetValue(null) as BattleUnits;
            var storm = bunits != null ? bunits.uniStormSystem : null;
            if (storm == null) return 1f;
            int month = storm.monthCounter;
            bool winter = month <= GamePrefs.winterquartersmonthuntil || month >= GamePrefs.winterquartersmonthfrom;
            return winter ? GamePrefs.winterdefensiveoperationsmodifier : 1f;
        }

        private static float AverageTimeOfNotMoving(CoordinatedOperationOutput output, IList ownUnits)
        {
            float sum = 0f;
            int count = 0;
            var lead = CoordinatedOperationRuntime.FindUnitById(ownUnits, output.LeadStableUnitId);
            if (lead != null) { sum += lead.timeofnotmoving; count++; }
            for (int i = 0; i < output.SupportStableUnitIds.Count; i++)
            {
                var support = CoordinatedOperationRuntime.FindUnitById(ownUnits, output.SupportStableUnitIds[i]);
                if (support == null) continue;
                sum += support.timeofnotmoving;
                count++;
            }
            return sum / Math.Max(1, count);
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
