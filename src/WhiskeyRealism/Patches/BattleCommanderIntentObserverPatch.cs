using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
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
        private static FieldInfo _flankAnchoredField;
        private static FieldInfo _reserveGroupsField;
        private static FieldInfo _unitsUsedField;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.LowerThanNormal)]
        internal static void Postfix(AIBattle __instance)
        {
            if (!Enabled() || __instance == null) return;

            try
            {
                Apply(__instance);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-b6a-observer:failed", "BattleCommanderIntentObserverPatch failed: " + ex.Message);
            }
        }

        private static void Apply(AIBattle battle)
        {
            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
            if (side < 0) return;

            var intentInput = BuildIntentInput(macro);
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

            EmitIntent(side, macro, intentInput, intent);
            EmitPlaybook(side, playbook);

            if (!Plugin.Instance.EnableTacticalLocalReactionDoctrine.Value)
                return;

            TacticalReactionContext.Shared.Clear();

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
                    TacticalReactionContext.Shared.SetReaction(SafeInstanceId(group), reaction);
                    reactions.Add(reaction);
                    EmitReaction(side, group, reaction);
                }
            }

            if (Plugin.Instance.EnableTacticalReserveIntentTelemetry.Value)
            {
                var availability = BuildReserveAvailability(battle);
                var reserveInput = new TacticalReserveIntentInput(
                    playbook.ReservePolicy,
                    reactions.ToArray(),
                    availability);
                TacticalReserveIntentDecision reserveIntent = TacticalReservePolicyLedger.Decide(reserveInput);
                TacticalReactionContext.Shared.SetReserveIntent(side, reserveIntent);
                EmitReserveIntent(side, reserveIntent);
            }
        }

        private static TacticalIntentInput BuildIntentInput(int macro)
        {
            // OperationPosture lookup is wired in B6c when strategic-side state
            // becomes available per-battle. B6a treats every battle as no-plan
            // and falls back to the vanilla macro mapping.
            return new TacticalIntentInput(
                operationPosture: OperationPosture.Inherit,
                hasPlan: false,
                vanillaMacro: macro,
                commanderInitiative01: 0.5f,
                oddsConfidence: 0.5f,
                weakPointConfirmed: false);
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
                if (center == null) continue;

                float own = Math.Max(0f, center.groupowninrange);
                float enemy = Math.Max(0f, center.groupenemiesinrange);
                bool flank = center.flanksthreated > 0f || center.outflanked > 0;
                bool strong = center.covervalue > 0.5f || center.fortinrange;
                float share = AttachedSubordinateShare(center);

                list.Add(new TacticalPlaybookSectorView(
                    sectorId: i,
                    mission: TacticalSectorMission.Hold,
                    position: i == 0 ? TacticalSectorPosition.Left :
                              i == chain.Count - 1 ? TacticalSectorPosition.Right :
                              TacticalSectorPosition.Center,
                    ownStrength: own,
                    enemyStrength: enemy,
                    confidence: enemy > 0f ? 0.6f : 0.3f,
                    strongPoint: strong,
                    flankRisk: flank,
                    ownerSubordinateShare01: share));
            }
            return list.ToArray();
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
            for (int i = 0; i < chain.Count; i++)
            {
                if (_reserveGroupsField == null) _reserveGroupsField = AccessTools.Field(chain[i].GetType(), "reservegroups");
                if (_reserveGroupsField == null) continue;
                if (_reserveGroupsField.GetValue(chain[i]) is IList reserves && reserves.Count > 0) return true;
            }
            return false;
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

            Regiment target = group.unitrange != null ? group.unitrange.closestenemyunitfarreg : null;
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
            catch
            {
                return true;
            }
        }

        private static TacticalReserveAvailability BuildReserveAvailability(AIBattle battle)
        {
            int reserveCount = 0;
            bool flankRisk = false;
            IList chain = ObjectiveChain(battle);

            if (chain != null)
            {
                for (int i = 0; i < chain.Count; i++)
                {
                    object entry = chain[i];
                    IList reserves = SafeList(entry, ref _reserveGroupsField, "reservegroups");
                    if (reserves != null) reserveCount += reserves.Count;

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
                wlOwnershipSafe: true,
                stalenessActive: false);
        }

        private static void EmitIntent(int side, int macro, TacticalIntentInput input, TacticalIntentDecision intent)
        {
            string signature = side + "|" + macro + "|" + intent.Intent + "|" + intent.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, "b6a-intent", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            Plugin.Log.LogInfo("[TacticalIntent] side=" + side +
                " intent=" + intent.Intent +
                " posture=" + input.OperationPosture +
                " commanderInit=" + input.CommanderInitiative01.ToString("0.00") +
                " macro=" + macro +
                " reason=" + intent.Reason +
                " confidence=" + input.OddsConfidence.ToString("0.00"));
        }

        private static void EmitPlaybook(int side, TacticalPlaybookDecision decision)
        {
            string signature = side + "|" + decision.Playbook + "|" + decision.MainEffortSectorId + "|" + decision.RefusedFlank + "|" + decision.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, "b6a-playbook", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            Plugin.Log.LogInfo("[TacticalPlaybook] side=" + side +
                " playbook=" + decision.Playbook +
                " main=" + decision.MainEffortSectorId +
                " refuse=" + decision.RefusedFlank +
                " probe=" + Join(decision.ProbeSectorIds) +
                " fix=" + Join(decision.FixSectorIds) +
                " hold=" + Join(decision.HoldSectorIds) +
                " reserve=" + decision.ReservePolicy +
                " reason=" + decision.Reason);
        }

        private static void EmitReaction(int side, Regiment group, TacticalLocalReactionDecision decision)
        {
            int id = SafeInstanceId(group);
            string signature = side + "|" + id + "|" + decision.Reaction + "|" + decision.ReliefRequested + "|" + decision.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, "b6c-reaction", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            Plugin.Log.LogInfo("[TacticalLocalReaction] side=" + side +
                " group=" + SafeName(group) + "#" + id +
                " reaction=" + decision.Reaction +
                " reliefRequested=" + (decision.ReliefRequested ? 1 : 0) +
                " reason=" + decision.Reason +
                " confidence=" + decision.Confidence.ToString("0.00"));
        }

        private static void EmitReserveIntent(int side, TacticalReserveIntentDecision decision)
        {
            string signature = side + "|" + decision.Intent + "|" + decision.AllowsRuntimeMutation + "|" + decision.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, "b6c-reserve-intent", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            Plugin.Log.LogInfo("[TacticalReserveIntent] side=" + side +
                " intent=" + decision.Intent +
                " allowsMutation=" + (decision.AllowsRuntimeMutation ? 1 : 0) +
                " reason=" + decision.Reason +
                " confidence=" + decision.Confidence.ToString("0.00"));
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
    }
}
