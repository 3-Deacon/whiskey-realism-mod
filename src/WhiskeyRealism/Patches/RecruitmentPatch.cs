using System;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla ZoneRecruiting picks a weak heatmap area, then AIArea.GetBestRecruitingState
    // chooses a state mostly by local recruit pool and distance. These patches scope
    // recruitment steering to ZoneRecruiting only, then replace the state result only
    // when a strategy-aligned candidate still satisfies vanilla support/recruit gates.
    internal static class RecruitmentPatch
    {
        [ThreadStatic] private static int _zoneDepth;
        [ThreadStatic] private static int _activeAifaction;
        private static readonly RecruitmentLogGate _logGate = new RecruitmentLogGate();

        [HarmonyPatch(typeof(AICampaign), "ZoneRecruiting")]
        internal static class ZoneRecruitingScope
        {
            [HarmonyPrefix]
            internal static void Prefix(int _aifaction)
            {
                _zoneDepth++;
                _activeAifaction = _aifaction;
            }

            [HarmonyFinalizer]
            internal static void Finalizer()
            {
                _zoneDepth--;
                if (_zoneDepth <= 0)
                {
                    _zoneDepth = 0;
                    _activeAifaction = -1;
                }
            }
        }

        [HarmonyPatch]
        internal static class GetBestRecruitingStateSteer
        {
            [HarmonyTargetMethod]
            internal static System.Reflection.MethodBase TargetMethod()
            {
                var t = AccessTools.TypeByName("AIArea");
                return AccessTools.Method(t, "GetBestRecruitingState", new[]
                {
                    typeof(int),
                    typeof(int),
                    typeof(bool),
                    typeof(bool)
                });
            }

            [HarmonyPostfix]
            internal static void Postfix(
                object __instance,
                int _aifaction,
                int strengthneeded,
                bool excludeenemystates,
                bool lookforwiderrecruiting,
                ref int __result)
            {
                try
                {
                    if (_zoneDepth <= 0 || _activeAifaction != _aifaction) return;
                    if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                    if (StrategicCoordinator.Instance == null) return;
                    if (__instance == null) return;
                    if (__result < 0 && !lookforwiderrecruiting) return;

                    int alliance = AICampaignReflect.GetAllianceId(_aifaction);
                    if (alliance < 0 || alliance >= 2) return;
                    if (StrategicCoordinator.IsPlayerCICOf(alliance, GameVars.playeralliance)) return;
                    if (GameVars.nation == null || GameVars.nation.Length == 0) return;

                    var candidates = BuildCandidates(__instance, alliance);
                    if (candidates.Count == 0) return;

                    var intent = BuildIntent(alliance);
                    var decision = RecruitmentIntentLedger.SelectState(
                        intent,
                        candidates,
                        __result,
                        strengthneeded,
                        excludeenemystates);

                    OnceLog.Info("recruitment", "RecruitmentPatch wired (ZoneRecruiting state steering)");

                    if (!decision.ShouldReplace || decision.StateId == __result || decision.StateId < 0) return;

                    int old = __result;
                    __result = decision.StateId;
                    string signature = RecruitmentLogGate.Signature(alliance, old, __result, strengthneeded, intent.PreferredTheater, decision.Reason);
                    if (_logGate.ShouldLog(signature))
                    {
                        Plugin.Log.LogInfo(
                            $"[Patch:Recruitment] alliance={alliance} oldState={old} newState={__result} " +
                            $"needed={strengthneeded} theater={intent.PreferredTheater} reason={decision.Reason}");
                    }
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("recruitment:state-postfix", "[Patch:Recruitment] state postfix failed: " + ex.Message);
                }
            }
        }

        private static RecruitmentIntent BuildIntent(int alliance)
        {
            var intent = new RecruitmentIntent
            {
                AllianceId = alliance,
                PreferredTheater = DefaultRecruitmentTheater(alliance),
                StrengthRatio = SafeStrengthRatio(alliance),
                OwnStateSupportFloor = GamePrefs.statesupportownstate,
                AllowDraftReplacement = IsConscriptionActive(alliance)
            };

            var cic = StrategicCoordinator.Instance?.CICs?[alliance];
            int objectiveId = cic?.ActivePlan?.CurrentPhase?.TargetObjectiveId ?? -1;
            if (ObjectiveCatalog.TryResolve(objectiveId, out var meta) && meta.Theater != Theater.Unknown)
                intent.PreferredTheater = meta.Theater;

            return intent;
        }

        private static float SafeStrengthRatio(int alliance)
        {
            try { return AICampaign.GetStrengthRatio(alliance); }
            catch { return 1f; }
        }

        private static bool IsConscriptionActive(int alliance)
        {
            try
            {
                if (alliance == 0)
                    return Policy.GetStatusByPolicyID(39) || Policy.GetStatusByPolicyID(40);
                return Policy.GetStatusByPolicyID(139) || Policy.GetStatusByPolicyID(140);
            }
            catch { return false; }
        }

        private static List<RecruitmentStateCandidate> BuildCandidates(object area, int alliance)
        {
            var candidates = new List<RecruitmentStateCandidate>();
            var seen = new HashSet<int>();
            AddCandidates(candidates, seen, ReadStateList(area, "states"), alliance, isLocal: true);
            AddCandidates(candidates, seen, ReadStateList(area, "nearbystates"), alliance, isLocal: false);
            return candidates;
        }

        private static List<int> ReadStateList(object area, string fieldName)
        {
            try
            {
                return AccessTools.Field(area.GetType(), fieldName)?.GetValue(area) as List<int>;
            }
            catch { return null; }
        }

        private static void AddCandidates(
            List<RecruitmentStateCandidate> candidates,
            HashSet<int> seen,
            List<int> states,
            int alliance,
            bool isLocal)
        {
            if (states == null) return;
            for (int i = 0; i < states.Count; i++)
            {
                int stateId = states[i];
                if (stateId < 0 || stateId >= GameVars.nation.Length) continue;
                if (!seen.Add(stateId)) continue;

                var state = GameVars.nation[stateId];
                if (state == null || state.volunteers == null || state.drafts == null || state.support == null) continue;
                if (alliance >= state.volunteers.Length || alliance >= state.drafts.Length || alliance >= state.support.Length) continue;

                candidates.Add(new RecruitmentStateCandidate
                {
                    StateId = stateId,
                    Theater = StateTheater(stateId),
                    Volunteers = Math.Max(0, state.volunteers[alliance]),
                    Drafts = Math.Max(0, state.drafts[alliance]),
                    Support = state.support[alliance],
                    IsRecruitable = state.recruitable && state.alliance <= 1,
                    IsEnemyState = state.alliance != alliance,
                    IsLocalArea = isLocal
                });
            }
        }

        private static Theater DefaultRecruitmentTheater(int alliance)
        {
            return Theater.East;
        }

        private static Theater StateTheater(int stateId)
        {
            try
            {
                var ledger = StrategicCoordinator.Instance?.CampaignMap;
                if (ledger != null && ledger.TryGetStateTheater(stateId, out var theater))
                    return theater;

                if (GameVars.nation != null && stateId >= 0 && stateId < GameVars.nation.Length)
                {
                    theater = TheaterClassifier.FromStateName(GameVars.nation[stateId]?.name);
                    if (theater != Theater.Unknown) return theater;
                }
            }
            catch { }

            return Theater.Unknown;
        }
    }
}
