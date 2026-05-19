using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // B6a/B6c telemetry observer. Runs as Postfix on AIBattle.AdjustGroupAIStance
    // (decompile 4221), reads vanilla side/macro/objective-chain context, feeds
    // TacticalCommanderIntentResolver and TacticalPlaybookLedger, and emits
    // bounded [TacticalIntent], [TacticalPlaybook], [TacticalLocalReaction],
    // and [TacticalReserveIntent] log lines. Never writes vanilla battle state.
    [HarmonyPatch(typeof(AIBattle), "AdjustGroupAIStance")]
    internal static class BattleCommanderIntentObserverPatch
    {
        private static readonly Dictionary<string, float> _lastEmittedAt = new Dictionary<string, float>();
        private static FieldInfo _macroAiField;
        private static FieldInfo _sideOfAiField;
        private static FieldInfo _objectiveChainField;
        private static FieldInfo _chainCenterField;
        private static FieldInfo _chainLeftUnitsField;
        private static FieldInfo _chainRightUnitsField;
        private static FieldInfo _flankAnchoredField;
        private static FieldInfo _reserveGroupsField;
        private static FieldInfo _unitsUsedField;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.LowerThanNormal)]
        internal static void Postfix(AIBattle __instance)
        {
            RefreshRuntimeState(__instance, emitTelemetry: true);
        }

        internal static bool RefreshRuntimeState(AIBattle battle, bool emitTelemetry)
        {
            if (!Enabled() || battle == null) return false;
            try
            {
                return Apply(battle, emitTelemetry);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-b6a-observer:failed", "BattleCommanderIntentObserverPatch failed: " + ex.Message);
                return false;
            }
        }

        private static bool Apply(AIBattle battle, bool emitTelemetry)
        {
            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
            if (side < 0) return false;

            var intentInput = BuildIntentInput(macro, side, battle);
            var intent = TacticalCommanderIntentResolver.Resolve(intentInput);

            var sectors = BuildPlaybookSectors(battle);
            var playbookInput = new TacticalPlaybookInput(
                intent.Intent,
                decisiveSectorId: ChooseDecisiveSector(sectors),
                sectors: sectors,
                hasReserveAvailable: HasReserveAvailable(battle),
                anchoredFlankLeft: AnchoredFlank(battle, 0),
                anchoredFlankRight: AnchoredFlank(battle, 1),
                stalenessPressure: 0f);
            var playbook = TacticalPlaybookLedger.Decide(playbookInput);

            if (emitTelemetry)
            {
                EmitIntent(side, macro, intentInput, intent);
                EmitPlaybook(side, playbook);
            }

            // O1.13: when the army orchestrator is on (default), it owns reactions.
            // The legacy scorer still computes and emits telemetry for comparison,
            // but skips the TacticalReactionContext.Shared writes so the orchestrator
            // pipeline isn't fighting it. Removed entirely at O7 cleanup.
            bool legacyWritesAllowed = !OrchestratorArmyOn();

            if (legacyWritesAllowed)
                TacticalReactionContext.Shared.Clear();

            if (!Plugin.Instance.EnableTacticalLocalReactionDoctrine.Value)
                return true;

            IList units = SafeList(battle, ref _unitsUsedField, "unitsused");
            var reactions = new List<TacticalLocalReactionDecision>();
            if (units != null)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    var group = units[i] as Regiment;
                    if (group == null || group.unittyp <= 13) continue;

                    TacticalLocalReactionInput reactionInput = BuildReactionInput(group, intent, playbook);
                    TacticalLocalReactionDecision reaction = TacticalLocalReactionScorer.Score(reactionInput);
                    if (legacyWritesAllowed)
                        TacticalReactionContext.Shared.SetReaction(SafeInstanceId(group), reaction);
                    reactions.Add(reaction);
                    if (emitTelemetry)
                        EmitReaction(side, group, reaction);
                }
            }

            bool reserveTelemetryEnabled = Plugin.Instance.EnableTacticalReserveIntentTelemetry.Value;
            if (reserveTelemetryEnabled || Plugin.Instance.EnableTacticalReserveListMutation.Value)
            {
                var availability = BuildReserveAvailability(battle);
                var reserveInput = new TacticalReserveIntentInput(
                    playbook.ReservePolicy,
                    reactions.ToArray(),
                    availability);
                TacticalReserveIntentDecision reserveIntent = TacticalReservePolicyLedger.Decide(reserveInput);
                if (legacyWritesAllowed)
                    TacticalReactionContext.Shared.SetReserveIntent(side, reserveIntent);
                if (emitTelemetry && reserveTelemetryEnabled)
                    EmitReserveIntent(side, reserveIntent);
            }

            return true;
        }

        private static TacticalIntentInput BuildIntentInput(int macro, int side, AIBattle battle)
        {
            // Live commander initiative from the side's commanding officer.
            // Mirrors BattleMacroStrategyPatch.CommanderAggression01: pulls
            // bunits.GetCommandingOfficerFromSide(side) then GameVars.commander[id]
            // .GetCommanderInitiative(), with NaN/range guards. Falls back to 0.5f
            // mid-band only when the lookup fails.
            float commanderInitiative01 = ResolveLiveCommanderInitiative(side, battle);

            // OperationPosture lookup is wired in a future strategic-tactical bridge
            // slice (CIC plan → tactical posture mapping). Inherit triggers
            // ResolveFromMacro which now consults commanderInitiative01 directly
            // for tempo modulation. oddsConfidence stays at 0.5f mid-band — the
            // macro layer owns the live odds doctrine.
            return new TacticalIntentInput(
                operationPosture: OperationPosture.Inherit,
                hasPlan: false,
                vanillaMacro: macro,
                commanderInitiative01: commanderInitiative01,
                oddsConfidence: 0.5f,
                weakPointConfirmed: false);
        }

        private static FieldInfo _bunitsFieldCache;

        private static float ResolveLiveCommanderInitiative(int side, AIBattle battle)
        {
            try
            {
                if (battle == null) return 0.5f;
                if (_bunitsFieldCache == null)
                    _bunitsFieldCache = AccessTools.Field(typeof(AIBattle), "bunits");
                var bunits = _bunitsFieldCache?.GetValue(battle) as BattleUnits;
                if (bunits == null) return 0.5f;
                if (side < 0 || bunits.alliance == null || side >= bunits.alliance.Length) return 0.5f;
                int commanderId = bunits.GetCommandingOfficerFromSide(side);
                if (GameVars.commander == null || commanderId < 0 || commanderId >= GameVars.commander.Count) return 0.5f;
                var commander = GameVars.commander[commanderId];
                if (commander == null) return 0.5f;
                float initiative = commander.GetCommanderInitiative();
                if (float.IsNaN(initiative) || float.IsInfinity(initiative)) return 0.5f;
                if (initiative < 0f) return 0f;
                if (initiative > 1f) return 1f;
                return initiative;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-b6a-observer:initiative-read-failed",
                    "BattleCommanderIntentObserverPatch live commander initiative read failed: " + ex.Message);
                return 0.5f;
            }
        }

        private static TacticalPlaybookSectorView[] BuildPlaybookSectors(AIBattle battle)
        {
            IList chain = ObjectiveChain(battle);
            if (chain == null || chain.Count == 0)
                return Array.Empty<TacticalPlaybookSectorView>();

            var list = new List<TacticalPlaybookSectorView>();
            for (int i = 0; i < chain.Count; i++)
            {
                object entry = chain[i];
                Regiment center = SafeRegimentField(entry, ref _chainCenterField, "linegroup_centerunit");
                float own = 0f;
                float enemy = 0f;
                bool flank = false;
                bool strong = false;
                float shareTotal = 0f;
                int shareCount = 0;
                int unitCount = 0;

                AccumulatePlaybookSectorUnit(center, ref own, ref enemy, ref flank, ref strong, ref shareTotal, ref shareCount, ref unitCount);
                AccumulatePlaybookSectorUnits(SafeList(entry, ref _chainLeftUnitsField, "linegroup_leftunits"), ref own, ref enemy, ref flank, ref strong, ref shareTotal, ref shareCount, ref unitCount);
                AccumulatePlaybookSectorUnits(SafeList(entry, ref _chainRightUnitsField, "linegroup_rightunits"), ref own, ref enemy, ref flank, ref strong, ref shareTotal, ref shareCount, ref unitCount);

                float confidence = enemy > 0f
                    ? 0.6f
                    : (unitCount > 0 ? 0.4f : 0.3f);
                float share = shareCount > 0 ? shareTotal / shareCount : 0f;

                list.Add(new TacticalPlaybookSectorView(
                    sectorId: i,
                    mission: TacticalSectorMission.Hold,
                    position: i == 0 ? TacticalSectorPosition.Left :
                              i == chain.Count - 1 ? TacticalSectorPosition.Right :
                              TacticalSectorPosition.Center,
                    ownStrength: own,
                    enemyStrength: enemy,
                    confidence: confidence,
                    strongPoint: strong,
                    flankRisk: flank,
                    ownerSubordinateShare01: share));
            }
            return list.ToArray();
        }

        private static void AccumulatePlaybookSectorUnits(
            IList units,
            ref float own,
            ref float enemy,
            ref bool flank,
            ref bool strong,
            ref float shareTotal,
            ref int shareCount,
            ref int unitCount)
        {
            if (units == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                AccumulatePlaybookSectorUnit(
                    units[i] as Regiment,
                    ref own,
                    ref enemy,
                    ref flank,
                    ref strong,
                    ref shareTotal,
                    ref shareCount,
                    ref unitCount);
            }
        }

        private static void AccumulatePlaybookSectorUnit(
            Regiment unit,
            ref float own,
            ref float enemy,
            ref bool flank,
            ref bool strong,
            ref float shareTotal,
            ref int shareCount,
            ref int unitCount)
        {
            if (unit == null) return;

            unitCount++;
            own += Math.Max(0f, Math.Max(unit.groupowninrange, unit.groupstrengthaigroup));
            enemy += Math.Max(0f, unit.groupenemiesinrange);
            flank = flank || unit.flanksthreated > 0f || unit.outflanked > 0;
            strong = strong || unit.covervalue > 0.5f || unit.fortinrange;
            shareTotal += AttachedSubordinateShare(unit);
            shareCount++;
        }

        private static int ChooseDecisiveSector(TacticalPlaybookSectorView[] sectors)
        {
            int best = -1;
            float bestScore = 0f;
            for (int i = 0; i < sectors.Length; i++)
            {
                if (sectors[i].EnemyStrength <= 0f) continue;
                float odds = sectors[i].OwnStrength / Math.Max(1f, sectors[i].EnemyStrength);
                float score = odds * sectors[i].Confidence;
                if (sectors[i].StrongPoint) score *= 0.65f;
                if (sectors[i].FlankRisk) score *= 0.55f;
                if (score > bestScore && sectors[i].Confidence >= 0.55f)
                {
                    bestScore = score;
                    best = sectors[i].SectorId;
                }
            }
            return best;
        }

        private static bool HasReserveAvailable(AIBattle battle)
        {
            IList chain = ObjectiveChain(battle);
            if (chain == null) return false;

            bool hasValidReserve = false;
            for (int i = 0; i < chain.Count; i++)
            {
                if (!TryReserveGroups(chain[i], out IList reserves))
                    return false;

                if (!ReserveListAvailableForPlaybook(reserves, ref hasValidReserve))
                    return false;
            }

            return hasValidReserve;
        }

        private static bool ReserveListAvailableForPlaybook(IList reserves, ref bool hasValidReserve)
        {
            try
            {
                for (int i = 0; i < reserves.Count; i++)
                {
                    var reserve = reserves[i] as Regiment;
                    if (reserve == null)
                    {
                        OnceLog.Warning("tactical-b6c-reserve-list:invalid-entry", "Reserve availability saw a null/non-Regiment reservegroups entry; blocking reserve mutation.");
                        return false;
                    }

                    if (!ReserveWlOwnershipSafe(reserve))
                        return false;

                    hasValidReserve = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-b6c-reserve-list:failed", "Reserve availability list inspection failed; blocking reserve mutation: " + ex.Message);
                return false;
            }
        }

        private static bool AnchoredFlank(AIBattle battle, int index)
        {
            IList chain = ObjectiveChain(battle);
            if (chain == null || chain.Count == 0) return false;
            object entry = chain[0];
            if (_flankAnchoredField == null) _flankAnchoredField = AccessTools.Field(entry.GetType(), "anchoredflank");
            if (_flankAnchoredField == null) return false;
            if (_flankAnchoredField.GetValue(entry) is bool[] anchored && anchored.Length > index) return anchored[index];
            return false;
        }

        private static float AttachedSubordinateShare(Regiment center)
        {
            if (center == null || center.allattachedunits == null) return 0f;
            int total = 0, sub = 0;
            for (int i = 0; i < center.allattachedunits.Length; i++)
            {
                var u = center.allattachedunits[i];
                if (u == null) continue;
                total++;
                if (u.dlcw_isundercommander) sub++;
            }
            return total > 0 ? (float)sub / total : 0f;
        }

        private static TacticalLocalReactionInput BuildReactionInput(
            Regiment group,
            TacticalIntentDecision intent,
            TacticalPlaybookDecision playbook)
        {
            float own = Math.Max(0f, group.groupowninrange);
            float enemy = Math.Max(0f, group.groupenemiesinrange);
            float odds = enemy <= 0f ? 0f : own / Math.Max(1f, enemy);
            bool flank = group.flanksthreated > 0f || group.outflanked > 0;

            Regiment target = TacticalFogOfWarContact.ClosestVisibleEnemy(group);
            bool targetVisible = target != null;
            bool targetBroken = target != null && (target.morale < 0.45f || target.markedforrout);
            bool targetStrongPoint = target != null && (target.covervalue > 0.5f || target.fortinrange != null);

            float morale = Mathf.Clamp01(group.morale);
            float ammo = Mathf.Clamp01(group.groupammo);
            float casualties = Mathf.Clamp01(group.grouplosses / Math.Max(1f, group.groupstrength + group.grouplosses));
            bool chargeReady = group.lastaichargetime + GamePrefs.timetorenewaichargecheck <= GameVars.currenttimefromstart;
            bool staleness = group.regimentpaths > 0 && group.pathinterrupted;

            return new TacticalLocalReactionInput(
                intent.Intent,
                playbook.LocalReactionPolicy,
                TacticalSectorMission.Hold,
                odds,
                playbook.Confidence,
                targetVisible,
                targetBroken,
                targetStrongPoint,
                morale,
                ammo,
                casualties,
                flank,
                WlOwnershipSafe(group),
                chargeReady,
                staleness,
                pathRiskActive: false);
        }

        private static bool WlOwnershipSafe(Regiment group)
        {
            return WlOwnershipSafe(group, failClosed: false);
        }

        private static bool ReserveWlOwnershipSafe(Regiment group)
        {
            return WlOwnershipSafe(group, failClosed: true);
        }

        private static bool WlOwnershipSafe(Regiment group, bool failClosed)
        {
            try
            {
                if (!DLC_WL.dlc_scenarioactive) return true;
                if (group == null) return true;
                if (group.dlcw_isundercommander) return false;
                if (group.allattachedunits == null) return true;

                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    if (unit != null && unit.dlcw_isundercommander)
                        return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                if (failClosed)
                    OnceLog.Warning("tactical-b6c-reserve-wl-ownership:failed", "Reserve W&L ownership inspection failed; blocking reserve mutation: " + ex.Message);
                return !failClosed;
            }
        }

        private static TacticalReserveAvailability BuildReserveAvailability(AIBattle battle)
        {
            int reserveCount = 0;
            bool flankRisk = false;
            bool wlOwnershipSafe = true;
            IList chain = ObjectiveChain(battle);

            if (chain != null)
            {
                for (int i = 0; i < chain.Count; i++)
                {
                    object entry = chain[i];
                    if (TryReserveGroups(entry, out IList reserves))
                    {
                        for (int j = 0; j < reserves.Count; j++)
                        {
                            var reserve = reserves[j] as Regiment;
                            if (reserve == null)
                            {
                                wlOwnershipSafe = false;
                                OnceLog.Warning("tactical-b6c-reserve-list:invalid-entry", "Reserve availability saw a null/non-Regiment reservegroups entry; blocking reserve mutation.");
                                continue;
                            }

                            reserveCount++;
                            if (!ReserveWlOwnershipSafe(reserve))
                                wlOwnershipSafe = false;
                        }
                    }
                    else
                    {
                        wlOwnershipSafe = false;
                    }

                    if (_flankAnchoredField == null && entry != null)
                        _flankAnchoredField = AccessTools.Field(entry.GetType(), "anchoredflank");
                    if (_flankAnchoredField == null) continue;

                    try
                    {
                        if (_flankAnchoredField.GetValue(entry) is bool[] anchored)
                        {
                            if (anchored.Length > 0 && !anchored[0]) flankRisk = true;
                            if (anchored.Length > 1 && !anchored[1]) flankRisk = true;
                        }
                    }
                    catch
                    {
                        // Missing flank data should not disable reserve telemetry.
                    }
                }
            }

            return new TacticalReserveAvailability(
                reserveCount,
                flankRisk,
                lastReserveIsFlankGuard: flankRisk && reserveCount <= 1,
                wlOwnershipSafe: wlOwnershipSafe,
                stalenessActive: false);
        }

        private static bool TryReserveGroups(object entry, out IList reserves)
        {
            reserves = null;
            try
            {
                if (entry == null)
                {
                    OnceLog.Warning("tactical-b6c-reserve-list:null-entry", "Reserve availability saw a null objective chain entry; blocking reserve mutation.");
                    return false;
                }

                if (_reserveGroupsField == null)
                    _reserveGroupsField = AccessTools.Field(entry.GetType(), "reservegroups");
                if (_reserveGroupsField == null)
                {
                    OnceLog.Warning("tactical-b6c-reserve-list:missing-field", "Reserve availability could not find objectivechain.reservegroups; blocking reserve mutation.");
                    return false;
                }

                reserves = _reserveGroupsField.GetValue(entry) as IList;
                if (reserves == null)
                {
                    OnceLog.Warning("tactical-b6c-reserve-list:not-list", "Reserve availability could not read objectivechain.reservegroups as a list; blocking reserve mutation.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-b6c-reserve-list:failed", "Reserve availability list inspection failed; blocking reserve mutation: " + ex.Message);
                return false;
            }
        }

        private static void EmitIntent(int side, int macro, TacticalIntentInput input, TacticalIntentDecision intent)
        {
            string signature = side + "|" + macro + "|" + intent.Intent + "|" + intent.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, "b6a-intent", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalIntent] side=" + side +
                " intent=" + intent.Intent +
                " posture=" + input.OperationPosture +
                " commanderInit=" + input.CommanderInitiative01.ToString("0.00") +
                " macro=" + macro +
                " reason=" + intent.Reason +
                " confidence=" + input.OddsConfidence.ToString("0.00"), TelemetryLayer.Tactical);
        }

        private static void EmitPlaybook(int side, TacticalPlaybookDecision decision)
        {
            string signature = side + "|" + decision.Playbook + "|" + decision.MainEffortSectorId + "|" + decision.RefusedFlank + "|" + decision.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, "b6a-playbook", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalPlaybook] side=" + side +
                " playbook=" + decision.Playbook +
                " main=" + decision.MainEffortSectorId +
                " refuse=" + decision.RefusedFlank +
                " probe=" + Join(decision.ProbeSectorIds) +
                " fix=" + Join(decision.FixSectorIds) +
                " hold=" + Join(decision.HoldSectorIds) +
                " reserve=" + decision.ReservePolicy +
                " reason=" + decision.Reason, TelemetryLayer.Tactical);
        }

        private static void EmitReaction(int side, Regiment group, TacticalLocalReactionDecision decision)
        {
            int id = SafeInstanceId(group);
            string signature = side + "|" + id + "|" + decision.Reaction + "|" + decision.ReliefRequested + "|" + decision.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, "b6c-reaction", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalLocalReaction] side=" + side +
                " group=" + SafeName(group) + "#" + id +
                " reaction=" + decision.Reaction +
                " reliefRequested=" + (decision.ReliefRequested ? 1 : 0) +
                " reason=" + decision.Reason +
                " confidence=" + decision.Confidence.ToString("0.00"), TelemetryLayer.Tactical);
        }

        private static void EmitReserveIntent(int side, TacticalReserveIntentDecision decision)
        {
            string signature = side + "|" + decision.Intent + "|" + decision.AllowsRuntimeMutation + "|" + decision.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, "b6c-reserve-intent", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalReserveIntent] side=" + side +
                " intent=" + decision.Intent +
                " allowsMutation=" + (decision.AllowsRuntimeMutation ? 1 : 0) +
                " reason=" + decision.Reason +
                " confidence=" + decision.Confidence.ToString("0.00"), TelemetryLayer.Tactical);
        }

        private static string Join(int[] values)
        {
            if (values == null || values.Length == 0) return "-";
            return string.Join(",", values);
        }

        private static IList ObjectiveChain(AIBattle battle)
        {
            if (_objectiveChainField == null) _objectiveChainField = AccessTools.Field(typeof(AIBattle), "objective" + "chain");
            return _objectiveChainField?.GetValue(battle) as IList;
        }

        private static IList SafeList(object instance, ref FieldInfo cache, string name)
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as IList : null;
            }
            catch
            {
                return null;
            }
        }

        private static Regiment SafeRegimentField(object instance, ref FieldInfo cache, string name)
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as Regiment : null;
            }
            catch
            {
                return null;
            }
        }

        private static int SafeIntField(object instance, ref FieldInfo cache, string name, int fallback)
        {
            try
            {
                if (instance == null) return fallback;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                if (cache == null) return fallback;
                return Convert.ToInt32(cache.GetValue(instance));
            }
            catch
            {
                return fallback;
            }
        }

        private static int SafeInstanceId(UnityEngine.Object obj)
        {
            try
            {
                return obj != null ? obj.GetInstanceID() : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string SafeName(Regiment group)
        {
            try
            {
                if (group == null) return "group";
                string name = group.name;
                return string.IsNullOrEmpty(name) ? "group" : name.Replace(' ', '_');
            }
            catch
            {
                return "group";
            }
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalObserver.Value &&
                Plugin.Instance.EnableTacticalCommanderIntentDoctrine.Value;
        }

        private static bool OrchestratorArmyOn()
        {
            return Plugin.EnableTacticalOrchestratorArmy != null
                && Plugin.EnableTacticalOrchestratorArmy.Value;
        }
    }
}
