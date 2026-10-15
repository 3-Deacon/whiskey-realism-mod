using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;

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
    internal sealed class ChargeDemotionIntentProducer : IBattleIntentProducer
    {
        public string Name => "ChargeDemotion";

        public void Produce(BattleSnapshot snapshot, IIntentSink sink)
        {
            using (TelemetryPerf.Scope("tactical.producer.charge-demotion", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (snapshot == null || sink == null || !GroupStanceIntentProducer.PluginEnabled()) return;
                UnitRecord[] own = snapshot.Own;
                for (int i = 0; i < snapshot.OwnCount; i++)
                {
                    if (own[i].AiStanceOrdered != TacticalCommandTaskToStanceMapper.StanceAssault) continue;
                    if (!GroupStanceIntentProducer.TryResolvedDecision(snapshot, i, out TacticalResolvedGroupDecision resolved)) continue;
                    if (TacticalChargeDenial.TryDemotionIntent(own[i], resolved, out BattleOrderIntent intent)) sink.Submit(intent);
                }
            }
        }
    }
}
