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
    // v0.2.2 concrete steering for vanilla AICampaign.CheckTransferOfUnits.
    // Vanilla (decompile 17232) transfers troops from positionsurplus toward
    // positiondeficit, sized by deficit.enemystrength - deficit.ownstrength
    // (num4, 17275). We temporarily replace the deficit with the active CIC
    // plan target, then restore vanilla state after the method runs. The
    // actual MoveUnit call and all vanilla safety gates remain vanilla-owned.
    //
    // 2026-10-14 (C-Transfer): steer only when the plan target's local
    // shortfall is worse than the deficit vanilla found (or vanilla found none),
    // both read from vanilla's painted recruitment heatmaps (ZoneRecruiting
    // 12470-12507 builds positiondeficit from the same grids),
    // so a genuinely threatened zone keeps its transfer; the plan requirement
    // is local (CampaignMilitaryGates.PlanTransferRequiredAtTarget) instead of
    // a share of the whole army. The deficit restore runs in a Finalizer so a
    // vanilla exception cannot leave the plan target stored as positiondeficit.
    //
    // 2026-10-15 (BUG-WL-010): the Postfix reports what vanilla actually did with
    // the unit it appended (DC:17327). Vanilla first drops every entry whose
    // transfertime <= 0 (DC:17255-17262), so the new entry is counted after that
    // purge, and it is reported as transfer-queued only when SetTransfer started a
    // transit (transfertime > 0, DC:93147). In a W&L career BattleUnits.MoveUnit
    // returns unless both units are player-owned (DC:93099; CheckTransferOfUnits
    // passes no skipdlccheck, DC:17328), so the row is transfer-blocked with
    // reason wl-career-moveunit-guard (BUG-WL-003, fixed by stage G).
    [HarmonyPatch(typeof(AICampaign), "CheckTransferOfUnits")]
    internal static class TransferOfUnitsPatch
    {
        static TransferOfUnitsPatch()
        {
            CampaignScope.Register("transfer-of-units", ResetForNewCampaign);
        }

        private static readonly HashSet<string> LogOnceKeys = new HashSet<string>();
        private static readonly Dictionary<int, TransferSteerState> _pending = new Dictionary<int, TransferSteerState>();
        // Verbose transfer-steer rows are emitted only when the alliance's steer signature changes.
        private static readonly Dictionary<int, string> LastLoggedSteer = new Dictionary<int, string>();
        // Non-queued rows (transfer-blocked repeats every vanilla pass in a career) are emitted only when the alliance's signature changes.
        private static readonly Dictionary<int, string> LastLoggedBlocked = new Dictionary<int, string>();

        internal static void ResetForNewCampaign()
        {
            LogOnceKeys.Clear();
            LastLoggedSteer.Clear();
            LastLoggedBlocked.Clear();
        }

        private sealed class TransferSteerState
        {
            public object Faction;
            public FieldInfo DeficitField;
            public object OriginalDeficit;
            public int AllianceId;
            public int ObjectiveId;
            public int BeforeTransferCount;
            public int StaleBeforeCount;
            public string SourceSector;
            public string DestinationSector;
            public TransferBudgetAction BudgetAction;
        }

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction)
        {
            using (TelemetryPerf.Scope("campaign.patch.transfer-of-units.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.transfer-of-units.prefix");
            _pending.Remove(_aifaction);
            OnceLog.Info("transfer", "TransferOfUnitsPatch wired (concrete plan-target steering)");

            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (StrategicCoordinator.Instance == null) return;

                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0 || allianceId >= StrategicCoordinator.Instance.CICs.Length) return;

                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;

                var cic = StrategicCoordinator.Instance.CICs[allianceId];
                var phase = cic?.ActivePlan?.CurrentPhase;
                if (phase == null || phase.TargetObjectiveId < 0) return;

                var target = ObjectiveAdapter.ResolveObjectivePosition(phase.TargetObjectiveId);
                if (!target.HasValue) return;

                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null) return;

                var factionType = faction.GetType();
                var deficitField = VanillaFieldCache.Field(factionType, "positiondeficit");
                var surplusField = VanillaFieldCache.Field(factionType, "positionsurplus");
                if (deficitField == null || surplusField == null)
                {
                    OnceLog.Warning("transfer:field:deficit-surplus", "TransferOfUnitsPatch missing AIFaction positiondeficit/positionsurplus fields");
                    return;
                }

                var surplus = surplusField.GetValue(faction);
                if (surplus == null) return;

                var ownUnits = VanillaFieldCache.List(faction, "ownunits");
                if (ownUnits == null)
                {
                    OnceLog.Warning("transfer:field:unit-lists", "TransferOfUnitsPatch missing AIFaction.ownunits list");
                    return;
                }

                // Same scale vanilla's positiondeficit uses: read the painted
                // recruitment heatmaps at the target (Heatmap.GetValueOnPos(world
                // position, usearrayvalues: false), 33840).
                var ownHeat = VanillaFieldCache.Get(faction, CampaignMilitaryGates.TransferHeatmapFields[0]) as Heatmap;
                var enemyHeat = VanillaFieldCache.Get(faction, CampaignMilitaryGates.TransferHeatmapFields[1]) as Heatmap;
                if (ownHeat == null || enemyHeat == null) return;
                float ownNear = ownHeat.GetValueOnPos(target.Value, usearrayvalues: false);
                float enemyNear = enemyHeat.GetValueOnPos(target.Value, usearrayvalues: false);
                float totalOwn = SumStrength(ownUnits);
                if (totalOwn <= 0f) return;

                float requiredAtTarget = CampaignMilitaryGates.PlanTransferRequiredAtTarget(totalOwn, phase.ForceFractionRequired, enemyNear);
                if (ownNear >= requiredAtTarget && ownNear >= enemyNear) return;

                // The injected TransferData makes vanilla compute num4 = enemyProxy - ownNear;
                // compare that with the deficit vanilla already found.
                float enemyProxy = Math.Max(enemyNear, requiredAtTarget);
                float planShortfall = enemyProxy - ownNear;
                var vanillaDeficit = deficitField.GetValue(faction);
                bool vanillaPresent = vanillaDeficit != null;
                float vanillaEnemy = vanillaPresent ? ReadTransferFloat(vanillaDeficit, "enemystrength") : 0f;
                float vanillaOwn = vanillaPresent ? ReadTransferFloat(vanillaDeficit, "ownstrength") : 0f;
                if (!CampaignMilitaryGates.ShouldSteerTransfer(vanillaPresent, vanillaEnemy, vanillaOwn, planShortfall))
                    return;

                string sourceSector = null;
                string destinationSector = FrontSectorRuntime.SectorKey(target.Value);
                TransferBudgetAction budgetAction = TransferBudgetAction.Allowed;
                var sourcePosition = ReadTransferPosition(surplus);
                if (sourcePosition.HasValue)
                {
                    sourceSector = FrontSectorRuntime.SectorKey(sourcePosition.Value);
                    float strengthToMove = Math.Min(Math.Max(requiredAtTarget - ownNear, 1000f), totalOwn * 0.1f);
                    var ledger = StrategicCoordinator.Instance.Fronts[allianceId];
                    var decision = ledger?.EvaluateTransfer(sourceSector, destinationSector, strengthToMove);
                    if (decision != null)
                    {
                        budgetAction = decision.Action;
                        if (!decision.Allowed)
                        {
                            EmitCampaignInfoOnce(
                                $"transfer-budget:{allianceId}:{sourceSector}:{destinationSector}:{decision.Action}:{decision.Reason}",
                                $"[FormationDirective] alliance={allianceId} from={sourceSector} to={destinationSector} action=blocked reason={decision.Reason}",
                                TelemetryLayer.Campaign);
                            return;
                        }

                        if (decision.Action == TransferBudgetAction.Concession)
                        {
                            EmitCampaignInfoOnce(
                                $"transfer-budget:{allianceId}:{sourceSector}:{destinationSector}:{decision.Action}:{decision.Reason}",
                                $"[FormationDirective] alliance={allianceId} from={sourceSector} to={destinationSector} action=concession reason={decision.Reason}",
                                TelemetryLayer.Campaign);
                        }
                    }
                }
                else
                {
                    OnceLog.Warning("transfer-budget:source-position", "[Patch:TransferBudget] could not read vanilla surplus position; front-budget gate skipped");
                }

                var transferData = CreateTransferData(factionType, target.Value, ownNear, enemyProxy);
                if (transferData == null) return;

                _pending[_aifaction] = new TransferSteerState
                {
                    Faction = faction,
                    DeficitField = deficitField,
                    OriginalDeficit = vanillaDeficit,
                    AllianceId = allianceId,
                    ObjectiveId = phase.TargetObjectiveId,
                    BeforeTransferCount = TransferCount(faction),
                    StaleBeforeCount = StaleTransferCount(faction),
                    SourceSector = sourceSector,
                    DestinationSector = destinationSector,
                    BudgetAction = budgetAction
                };

                deficitField.SetValue(faction, transferData);

                if (Plugin.Instance.VerboseLogging.Value)
                    EmitPlanTransferSteer(
                        allianceId,
                        phase.TargetObjectiveId,
                        ownNear,
                        requiredAtTarget,
                        enemyNear,
                        sourceSector,
                        destinationSector,
                        budgetAction);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("transfer:prefix", "[Patch:Transfer] prefix failed: " + ex.Message);
            }
            } // end using TelemetryPerf.Scope
        }

        [HarmonyPostfix]
        internal static void Postfix(int _aifaction)
        {
            using (TelemetryPerf.Scope("campaign.patch.transfer-of-units.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.transfer-of-units.postfix");
            if (!_pending.TryGetValue(_aifaction, out var state) || state == null) return;

            try
            {
                var list = VanillaFieldCache.List(state.Faction, "unitstobetransfered");
                int after = list?.Count ?? 0;
                int added = WlEconomyFixes.TransferAddedCount(state.BeforeTransferCount, state.StaleBeforeCount, after);
                bool career = DLC_WL.dlc_scenarioactive;
                for (int i = Math.Max(0, after - added); i < after; i++)
                {
                    var unit = list[i] as Regiment;
                    bool transit = (UnityEngine.Object)(object)unit != null && unit.transfertime > 0f;
                    var outcome = WlEconomyFixes.ClassifyTransfer(transit, career);
                    EmitPlanTransferOutcome(state, outcome);
                    if (outcome == WlTransferOutcome.BlockedByCareerGuard)
                        OnceLog.Info("transfer:wl-career-blocked",
                            "[Patch:Transfer] W&L career: vanilla MoveUnit refused the transfer (both units must be player-owned, 93099); reported as transfer-blocked, not queued");
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("transfer:postfix-count", "[Patch:Transfer] postfix count failed: " + ex.Message);
            }
            } // end using TelemetryPerf.Scope
        }

        // Runs after Postfix and also when vanilla threw; restores vanilla's own
        // positiondeficit. A void Finalizer leaves any exception untouched.
        [HarmonyFinalizer]
        internal static void Finalizer(int _aifaction)
        {
            using (TelemetryPerf.Scope("campaign.patch.transfer-of-units.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.transfer-of-units.finalizer");
                if (!_pending.TryGetValue(_aifaction, out var state)) return;
                _pending.Remove(_aifaction);
                if (state == null) return;
                try
                {
                    state.DeficitField?.SetValue(state.Faction, state.OriginalDeficit);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("transfer:restore", "[Patch:Transfer] restore failed: " + ex.Message);
                }
            }
        }

        private static float ReadTransferFloat(object transferData, string fieldName)
        {
            try
            {
                var field = VanillaFieldCache.Field(transferData.GetType(), fieldName);
                return field != null ? Convert.ToSingle(field.GetValue(transferData)) : 0f;
            }
            catch { return 0f; }
        }

        private static ConstructorInfo _transferDataCtor;
        private static bool _transferDataCtorResolved;

        // AIFaction.TransferData(Vector3, float, float) (decompile 10301).
        private static object CreateTransferData(Type factionType, Vector3 position, float ownStrength, float enemyStrength)
        {
            if (!_transferDataCtorResolved)
            {
                _transferDataCtorResolved = true;
                var transferDataType = AccessTools.Inner(factionType, "TransferData");
                _transferDataCtor = transferDataType != null
                    ? AccessTools.Constructor(transferDataType, new[] { typeof(Vector3), typeof(float), typeof(float) })
                    : null;
                if (_transferDataCtor == null)
                    OnceLog.Warning("transfer:ctor:transferdata", "TransferOfUnitsPatch missing AIFaction.TransferData(Vector3,float,float) constructor");
            }
            return _transferDataCtor?.Invoke(new object[] { position, ownStrength, enemyStrength });
        }

        private static int TransferCount(object faction)
        {
            var list = VanillaFieldCache.List(faction, "unitstobetransfered");
            return list?.Count ?? 0;
        }

        // Entries vanilla's purge will drop at the start of the call (DC:17257).
        private static int StaleTransferCount(object faction)
        {
            var list = VanillaFieldCache.List(faction, "unitstobetransfered");
            if (list == null) return 0;
            int stale = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var unit = list[i] as Regiment;
                if ((UnityEngine.Object)(object)unit != null && unit.transfertime <= 0f) stale++;
            }
            return stale;
        }

        private static Vector3? ReadTransferPosition(object transferData)
        {
            if (transferData == null) return null;
            try
            {
                var field = VanillaFieldCache.Field(transferData.GetType(), "position");
                if (field != null) return (Vector3)field.GetValue(transferData);
            }
            catch { }
            return null;
        }

        // Whole-army total on the heatmap's strength field: UpdateHeatmaps paints
        // groupstrengthdirect (12332), and vanilla's own army total
        // (GetStrengthRatio, 12377) sums groupstrengthdirect over ownunits.
        private static float SumStrength(IList units)
        {
            float total = 0f;
            if (units == null) return total;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i] as Regiment;
                if ((UnityEngine.Object)(object)unit != (UnityEngine.Object)null)
                    total += Math.Max(0, unit.groupstrengthdirect);
            }
            return total;
        }

        private static void EmitPlanTransferSteer(
            int allianceId,
            int objectiveId,
            float ownNear,
            float requiredAtTarget,
            float enemyNear,
            string sourceSector,
            string destinationSector,
            TransferBudgetAction budgetAction)
        {
            string safeSource = string.IsNullOrWhiteSpace(sourceSector) ? "-" : sourceSector;
            string safeDestination = string.IsNullOrWhiteSpace(destinationSector) ? "-" : destinationSector;
            string signature = "alliance=" + allianceId +
                "|action=transfer-steer" +
                "|objective=" + objectiveId +
                "|from=" + safeSource +
                "|to=" + safeDestination +
                "|budget=" + budgetAction;
            if (LastLoggedSteer.TryGetValue(allianceId, out string last) && last == signature) return;
            LastLoggedSteer[allianceId] = signature;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "Plan", TelemetrySeverity.Info, ev => ev
                .WithAlliance(allianceId)
                .WithDecision("transfer-steer", budgetAction.ToString(), signature)
                .WithField("objective", objectiveId)
                .WithField("ownNear", ownNear)
                .WithField("required", requiredAtTarget)
                .WithField("enemyNear", enemyNear)
                .WithField("sourceSector", safeSource)
                .WithField("destinationSector", safeDestination)
                .WithField("budgetAction", budgetAction.ToString()));
        }

        // transfer-queued (Write row) only when a transit started; otherwise a
        // Decision row: transfer-blocked (career guard, nothing moved) or
        // transfer-no-transit (re-parented inside its corps, or refused by MoveUnit's type gates).
        private static void EmitPlanTransferOutcome(TransferSteerState state, WlTransferOutcome outcome)
        {
            string safeSource = string.IsNullOrWhiteSpace(state.SourceSector) ? "-" : state.SourceSector;
            string safeDestination = string.IsNullOrWhiteSpace(state.DestinationSector) ? "-" : state.DestinationSector;
            bool queued = outcome == WlTransferOutcome.Queued;
            bool blocked = outcome == WlTransferOutcome.BlockedByCareerGuard;
            string action = queued ? "transfer-queued" : blocked ? "transfer-blocked" : "transfer-no-transit";
            string reason = queued ? state.BudgetAction.ToString() : blocked ? "wl-career-moveunit-guard" : "no-transit-started";
            string signature = "alliance=" + state.AllianceId +
                "|action=" + action +
                "|objective=" + state.ObjectiveId +
                "|from=" + safeSource +
                "|to=" + safeDestination +
                "|reason=" + reason;
            if (!queued)
            {
                if (LastLoggedBlocked.TryGetValue(state.AllianceId, out string last) && last == signature) return;
                LastLoggedBlocked[state.AllianceId] = signature;
            }
            TelemetryRouter.Emit(TelemetryLayer.Campaign, queued ? TelemetryCategory.Write : TelemetryCategory.Decision, "Plan", TelemetrySeverity.Info, ev => ev
                .WithAlliance(state.AllianceId)
                .WithDecision(action, reason, signature)
                .WithField("objective", state.ObjectiveId)
                .WithField("queued", queued ? 1 : 0)
                .WithField("sourceSector", safeSource)
                .WithField("destinationSector", safeDestination)
                .WithField("budgetAction", state.BudgetAction.ToString()));
        }

        private static void EmitCampaignInfoOnce(string key, string line, TelemetryLayer layer)
        {
            if (!LogOnceKeys.Add(key ?? string.Empty)) return;
            TelemetryRouter.LegacyInfo(line, layer);
        }
    }
}
