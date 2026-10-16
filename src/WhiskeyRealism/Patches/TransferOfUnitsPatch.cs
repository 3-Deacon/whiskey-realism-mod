using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // v0.2.2 concrete steering for vanilla AICampaign.CheckTransferOfUnits.
    // Vanilla (decompile 17232) moves strength from AIFaction.positionsurplus toward
    // AIFaction.positiondeficit: the amount is min(surplus.own - surplus.enemy, deficit.enemy -
    // deficit.own) (17275-17290), one brigade-sized grab from a unit near the surplus point that is
    // not already within command range of the deficit point, attached below a division found at the
    // deficit point (GrabExistingGroup 17319; MoveUnit 17328). Both TransferData records come from
    // ZoneRecruiting's painted heatmaps, which paint every enemy on the map.
    //
    // 2026-10-15 (stage G, G5b): the Prefix swaps both records for what the live campaign plan says,
    // and vanilla's Finalizer-restored fields run its own gates and its own move.
    //   1. The allocation names the worst shortfall formation and the largest surplus formation
    //      (ReinforcementRouting.TryTransferPair): the deficit is that formation's hold point, sized by
    //      the need its role reported it for, the surplus is the surplus formation's position and strength.
    //      A player formation can be a shortfall's position marker (spec C4): it contributes its hold
    //      point only, and a source the planner may not move (WlChainRules.MayMove) is never drawn from.
    //      Vanilla picks its donor from the source POINT, the first top group within
    //      aimaximumdistancetosearchforunitrelocations of it (GrabOwnTopUnitsNearPosition 12824, called at
    //      17284; the donor loop is 17291-17300), so a surplus is used only where no MainEffort, withheld,
    //      in-transit or unmovable formation stands inside that radius and is not itself in transit
    //      (ReinforcementRouting.TryTransferPair); with none, no pair is written and the main-target steer
    //      below (2) may still replace the deficit; otherwise vanilla's records stay.
    //   2. Otherwise, when the main target is under-supplied against the army believed there
    //      (ReplacementFlowPolicy), only the deficit is swapped for the main target; vanilla's own
    //      surplus stays.
    // Nothing here reads the heatmaps: the amounts are the plan's, built from fog-honest beliefs. The
    // front ledger's transfer budget (FrontSectorLedger.EvaluateTransfer) still vetoes a transfer that
    // would strip a sector below its minimum hold. In a W&L career the move itself is vanilla's, which
    // refused every AI transfer until WlCareerTransferFixPatch (BUG-WL-013) let the AI's own through.
    //
    // 2026-10-15 (BUG-WL-010): the Postfix reports what vanilla actually did with
    // the unit it appended (DC:17327). Vanilla first drops every entry whose
    // transfertime <= 0 (DC:17255-17262), so the new entry is counted after that
    // purge, and it is reported as transfer-queued only when SetTransfer started a
    // transit (transfertime > 0, DC:93147). A career move that the (fixed or off) vanilla guard
    // refused (DC:93099) is reported transfer-blocked with reason wl-career-moveunit-guard.
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
        private static readonly List<FormationRecord> Own = new List<FormationRecord>();

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
            public FieldInfo SurplusField;
            public object OriginalDeficit;
            public object OriginalSurplus;
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
            OnceLog.Info("transfer", "TransferOfUnitsPatch wired (allocation-driven replacement flow)");

            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (!FlagOn(Plugin.EnableCampaignPlanner) || !FlagOn(Plugin.EnableCampaignPlannerExecutors)) return;
                if (StrategicCoordinator.Instance == null) return;

                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0 || allianceId > 1) return;

                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;

                if (!CampaignPlanStore.TryGetPlan(allianceId, out var plan)) return;

                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null) return;

                var factionType = faction.GetType();
                var deficitField = VanillaFieldCache.Field(factionType, "positiondeficit"); // the holder is swapped for the plan's pair and restored; its enemy figures are never read
                var surplusField = VanillaFieldCache.Field(factionType, "positionsurplus");
                if (deficitField == null || surplusField == null)
                {
                    OnceLog.Warning("transfer:field:deficit-surplus", "TransferOfUnitsPatch missing AIFaction positiondeficit/positionsurplus fields");
                    return;
                }

                var ownUnits = VanillaFieldCache.List(faction, "ownunits");
                if (ownUnits == null)
                {
                    OnceLog.Warning("transfer:field:unit-lists", "TransferOfUnitsPatch missing AIFaction.ownunits list");
                    return;
                }
                LiveFormationReader.Read(ownUnits, allianceId, false, Own);
                float totalOwn = SumStrength(Own);
                if (totalOwn <= 0f) return;

                var vanillaDeficit = deficitField.GetValue(faction);
                var vanillaSurplus = surplusField.GetValue(faction);
                object deficitData;
                object surplusData = null;
                float deficitX, deficitZ, deficitStrength;
                float sourceX, sourceZ;
                bool haveSource;
                string toName;
                string fromName;

                if (CampaignPlanStore.TryGetAllocation(allianceId, out var allocation)
                    && ReinforcementRouting.TryTransferPair(plan, allocation, Own, GamePrefs.aimaximumdistancetosearchforunitrelocations, out var pair))
                {
                    deficitX = pair.DeficitX;
                    deficitZ = pair.DeficitZ;
                    deficitStrength = pair.DeficitStrength;
                    sourceX = pair.SurplusX;
                    sourceZ = pair.SurplusZ;
                    haveSource = true;
                    deficitData = CreateTransferData(factionType, new Vector3(deficitX, 0f, deficitZ), 0f, deficitStrength);
                    surplusData = CreateTransferData(factionType, new Vector3(sourceX, 0f, sourceZ), pair.SurplusStrength, 0f);
                    if (deficitData == null || surplusData == null) return;
                    toName = pair.DeficitInstanceId >= 0 ? LiveFormationReader.NameOf(ownUnits, pair.DeficitInstanceId) : "position-marker";
                    fromName = LiveFormationReader.NameOf(ownUnits, pair.SurplusInstanceId);
                }
                else
                {
                    if (vanillaSurplus == null) return;
                    if (!ReplacementFlowPolicy.TryMainTarget(plan, out float targetX, out float targetZ)) return;
                    float range = GamePrefs.aimaximumdistancetosearchforunitrelocations;
                    float believed = EnemyBeliefQuery.StrengthWithin(EnemyDetectionRuntime.BeliefsFor(allianceId), targetX, targetZ, range);
                    float shortfall = ReplacementFlowPolicy.PlanShortfall(plan, believed, Own, targetX, targetZ, range);
                    if (!ReplacementFlowPolicy.ShouldSteer(plan, believed, shortfall, out deficitX, out deficitZ)) return;
                    deficitStrength = shortfall;
                    var sourcePosition = ReadTransferPosition(vanillaSurplus);
                    haveSource = sourcePosition.HasValue;
                    sourceX = haveSource ? sourcePosition.Value.x : 0f;
                    sourceZ = haveSource ? sourcePosition.Value.z : 0f;
                    deficitData = CreateTransferData(factionType, new Vector3(deficitX, 0f, deficitZ), 0f, deficitStrength);
                    if (deficitData == null) return;
                    toName = "main-target";
                    fromName = "vanilla-surplus";
                }

                string sourceSector = null;
                string destinationSector = FrontSectorRuntime.SectorKey(new Vector3(deficitX, 0f, deficitZ));
                TransferBudgetAction budgetAction = TransferBudgetAction.Allowed;
                if (haveSource)
                {
                    sourceSector = FrontSectorRuntime.SectorKey(new Vector3(sourceX, 0f, sourceZ));
                    float strengthToMove = Math.Min(Math.Max(deficitStrength, 1000f), totalOwn * 0.1f);
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

                _pending[_aifaction] = new TransferSteerState
                {
                    Faction = faction,
                    DeficitField = deficitField,
                    SurplusField = surplusData != null ? surplusField : null,
                    OriginalDeficit = vanillaDeficit,
                    OriginalSurplus = vanillaSurplus,
                    AllianceId = allianceId,
                    ObjectiveId = plan.MainObjectiveId,
                    BeforeTransferCount = TransferCount(faction),
                    StaleBeforeCount = StaleTransferCount(faction),
                    SourceSector = sourceSector,
                    DestinationSector = destinationSector,
                    BudgetAction = budgetAction
                };

                deficitField.SetValue(faction, deficitData);
                if (surplusData != null) surplusField.SetValue(faction, surplusData);

                ReplacementFlowRow.Emit(allianceId, ReplacementFlowRow.KindTransfer, toName, fromName);
                if (Plugin.Instance.VerboseLogging.Value)
                    EmitPlanTransferSteer(allianceId, plan.MainObjectiveId, deficitStrength, sourceSector, destinationSector, budgetAction);
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
                    bool transit = !ReferenceEquals(unit, null) && unit.transfertime > 0f;
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
        // positiondeficit and positionsurplus. A void Finalizer leaves any exception untouched.
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
                    state.SurplusField?.SetValue(state.Faction, state.OriginalSurplus);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("transfer:restore", "[Patch:Transfer] restore failed: " + ex.Message);
                }
            }
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

        // Entries vanilla's purge will drop at the start of the call (DC:17257),
        // counted exactly as the purge counts them.
        private static int StaleTransferCount(object faction)
        {
            var list = VanillaFieldCache.List(faction, "unitstobetransfered");
            if (list == null) return 0;
            int stale = 0;
            for (int i = 0; i < list.Count; i++)
            {
                // A C# reference test, not Unity's ==: vanilla's purge reads the field
                // off a destroyed unit too.
                var unit = list[i] as Regiment;
                if (WlEconomyFixes.CountsAsStaleTransfer(!ReferenceEquals(unit, null), ReferenceEquals(unit, null) ? 0f : unit.transfertime)) stale++;
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

        // The side's whole-army total: vanilla's own army total (GetStrengthRatio, 12377) sums
        // groupstrengthdirect over ownunits, which is what the live records carry.
        private static float SumStrength(List<FormationRecord> units)
        {
            float total = 0f;
            for (int i = 0; i < units.Count; i++)
                total += Math.Max(0f, units[i].Strength);
            return total;
        }

        private static bool FlagOn(BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.Value; }
            catch { return false; }
        }

        private static void EmitPlanTransferSteer(
            int allianceId,
            int objectiveId,
            float deficitStrength,
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
                .WithField("deficit", deficitStrength)
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
