using System;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle.Producers
{
    // Charge demotion on the order arbiter (plan BC5b; was the
    // vanillaOrdered == 4 branch of the deleted BattleGroupStancePatch).
    //
    // Vanilla AdjustGroupAIStance escalates a strong line group to assault
    // (ai_stanceordered 4, 4240) under macroai 0/1. When the coordinator's cached
    // answer for that brigade (TacticalChargeDenial.DecideForVanillaAssault, run
    // in PopulateResolvedDecisionsForSide) is DemoteToHold, this producer submits
    // a Safety intent on the Stance channel (TacticalChargeDenial
    // .TryDemotionIntent). Safety is the top rung, so the demotion beats the
    // posture executor's assault on the same group, and the infantry charge
    // doctrine (BC6b) sees the group leave stance 4 and reverts its charges.
    // Executed through TacticalStanceWriter with StanceWriteSource.ChargeDemotion
    // (TacticalStanceOwnership.SourceFor).
    //
    // Telemetry: a demotion counts once the executor issued it, read back on the next
    // tick (ChargeDemotionTracker). The first per battle logs
    // [once:tactical-battle-core:charge-demotion-first-fire] [ChargeDemotion] to
    // LogOutput.log; each one is a ChargeDemotion Decision row (per group signature,
    // 30 s throttle) and every 30 s real time a side with new demotions writes a
    // ChargeDemotionCount Decision row (window and battle total).
    internal sealed class ChargeDemotionIntentProducer : IBattleIntentProducer
    {
        private const float TelemetrySeconds = 30f;
        private static readonly ChargeDemotionTracker[] _trackers = { new ChargeDemotionTracker(), new ChargeDemotionTracker() };
        private static readonly ChargeDemotionTracker.Demotion[] _confirmed = new ChargeDemotionTracker.Demotion[32];
        private static readonly Dictionary<string, float> _lastRowAt = new Dictionary<string, float>();

        static ChargeDemotionIntentProducer()
        {
            BattleScope.Register("charge-demotion-producer", Reset);
        }

        public string Name => "ChargeDemotion";

        public void Produce(BattleSnapshot snapshot, IIntentSink sink)
        {
            using (TelemetryPerf.Scope("tactical.producer.charge-demotion", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (snapshot == null || sink == null || !GroupStanceIntentProducer.PluginEnabled()) return;
                int alliance = snapshot.Alliance;
                if (alliance < 0 || alliance >= _trackers.Length) return;
                ChargeDemotionTracker tracker = _trackers[alliance];
                Report(alliance, tracker, tracker.Confirm(OrderExecutor.ForAlliance(alliance), _confirmed));

                UnitRecord[] own = snapshot.Own;
                for (int i = 0; i < snapshot.OwnCount; i++)
                {
                    if (own[i].AiStanceOrdered != TacticalCommandTaskToStanceMapper.StanceAssault) continue;
                    // TacticalStanceWriter writes only groups in the AI pool (AIBattle.unitsused) and refuses any other as not-ai-group: a leaf brigade's
                    // stance is its pool ancestor's, delivered by order, so the demotion is submitted for the ancestor and not for the leaf.
                    if (!ArmyPostureInputsRuntime.InAiPool(alliance, own[i].InstanceId)) continue;
                    if (!GroupStanceIntentProducer.TryResolvedDecision(snapshot, i, out TacticalResolvedGroupDecision resolved)) continue;
                    if (!TacticalChargeDenial.TryDemotionIntent(own[i], resolved, out BattleOrderIntent intent)) continue;
                    sink.Submit(intent);
                    tracker.Submitted(intent, snapshot.DoctrineHours);
                }
            }
        }

        private static void Report(int alliance, ChargeDemotionTracker tracker, int confirmed)
        {
            try
            {
                if (confirmed > 0)
                    OnceLog.Info("tactical-battle-core:charge-demotion-first-fire",
                        "[ChargeDemotion] first vanilla assault demoted: stance " + _confirmed[0].Stance
                        + " issued to a group vanilla had escalated to assault (ai_stanceordered 4)");
                float now = Time.realtimeSinceStartup;
                IReadOnlyDictionary<int, string> names = confirmed > 0 ? BattleSnapshotBuilder.NamesFor(alliance) : null;
                for (int k = 0; k < confirmed; k++)
                {
                    ChargeDemotionTracker.Demotion d = _confirmed[k];
                    string signature = alliance + "|" + d.GroupId + "|" + d.Stance;
                    if (!TacticalTelemetry.ShouldEmit(_lastRowAt, "tactical-charge-demotion:" + alliance + ":" + d.GroupId, signature,
                            now, TelemetrySeconds, false))
                        continue;
                    string unit = names != null && names.TryGetValue(d.GroupId, out string name)
                        ? TacticalCurrentOrderSignature.Safe(name) : "#" + d.GroupId;
                    TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "ChargeDemotion", TelemetrySeverity.Info,
                        ev => ev
                            .WithSide(alliance)
                            .WithUnit(unit)
                            .WithDecision("DemoteToHold", "charge-demotion", signature)
                            .WithField("fromStance", TacticalCommandTaskToStanceMapper.StanceAssault)
                            .WithField("toStance", d.Stance)
                            .WithField("submittedAtHours", d.SubmittedAtHours));
                }
                if (tracker.TryTakeWindow(now, TelemetrySeconds, out int window, out int total))
                    TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "ChargeDemotionCount", TelemetrySeverity.Info,
                        ev => ev
                            .WithSide(alliance)
                            .WithField("demoted", window)
                            .WithField("battleTotal", total)
                            .WithField("windowSeconds", (int)TelemetrySeconds));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:charge-demotion-telemetry",
                    "[ChargeDemotion] telemetry threw: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static void Reset()
        {
            for (int a = 0; a < _trackers.Length; a++) _trackers[a].Clear();
            _lastRowAt.Clear();
        }
    }
}
