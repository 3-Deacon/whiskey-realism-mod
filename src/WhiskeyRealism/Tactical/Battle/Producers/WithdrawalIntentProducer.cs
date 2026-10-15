using System;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // Withdrawal producer, runtime half (BC5c). Replaces the deleted B8
    // CheckUseOfReserves Postfix's fallback writes (direct RegimentSetPath and
    // combatbehavior fields) and #56's stalled-fallback SetWaypoint with intents
    // the arbiter resolves and the executor issues. Decisions are in
    // WithdrawalIntentProducer.Plan.cs (pure, reads the BattleSnapshot only); this
    // half supplies vanilla's distances and morale triggers (GamePrefs
    // standardfallbackdistance / standardretreatdistance /
    // moraletriggerforfallbackifenemyclose, the values CheckLineFallbacks 5167/5170 and
    // MicroAICheckForRetreats 4853 use) and the ledger's doctrine orders.
    //
    // Registered from the static constructor (BattleCoreTick.RegisterProducer);
    // runs inside the arbiter tick (BattleCoreArbiterPatch, AdjustGroupFormations
    // 5870 Postfix). Rollback: Enable Tactical Withdrawal Doctrine = false.
    internal sealed partial class WithdrawalIntentProducer : IBattleIntentProducer
    {
        static WithdrawalIntentProducer()
        {
            BattleCoreTick.RegisterProducer(new WithdrawalIntentProducer());
        }

        private WithdrawalIntentProducer()
        {
        }

        public string Name => "Withdrawal";

        public void Produce(BattleSnapshot snapshot, IIntentSink sink)
        {
            using (TelemetryPerf.Scope("tactical.producer.withdrawal", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (snapshot == null || sink == null || !Enabled()) return;

                var settings = new Settings(
                    GamePrefs.standardfallbackdistance,
                    GamePrefs.standardretreatdistance,
                    GamePrefs.moraletriggerforfallbackifenemyclose);
                UnitRecord[] own = snapshot.Own;
                for (int i = 0; i < snapshot.OwnCount; i++)
                {
                    if ((own[i].Flags & UnitFlags.IsGroup) != 0) continue;
                    if (PlanUnit(snapshot, i, settings, out BattleOrderIntent move, out BattleOrderIntent behavior)
                        == TacticalWithdrawalDoctrine.FallbackKind.None)
                        continue;
                    sink.Submit(move);
                    if (behavior.GroupId != 0) sink.Submit(behavior);
                }

                ProduceStalledFallbacks(snapshot, sink);
            }
        }

        private static void ProduceStalledFallbacks(BattleSnapshot snapshot, IIntentSink sink)
        {
            var orders = TacticalBattleCoordinator.GetSideOrchestrator(snapshot.Alliance)?.Army?.CurrentDoctrineOrders;
            if (orders == null) return;
            for (int i = 0; i < orders.Count; i++)
            {
                CommandDoctrineOrder order = orders[i];
                if (order.Task != CommandTaskType.FallBackToLine) continue;
                // Ledger node ids are "node-" + the group's GameObject instance id,
                // the id the snapshot keys own units by (BattleSnapshotAssembler).
                if (!TacticalPatchIds.TryParseNodeId(order.NodeId, out int id)) continue;
                if (TryPlanStalledFallback(snapshot, snapshot.IndexOfOwn(id), order, out BattleOrderIntent move))
                    sink.Submit(move);
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null
                    && Plugin.Instance.Enabled != null
                    && Plugin.Instance.Enabled.Value
                    && Plugin.EnableTacticalWithdrawalDoctrine != null
                    && Plugin.EnableTacticalWithdrawalDoctrine.Value;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:withdrawal-enabled",
                    "[WithdrawalIntentProducer] enable check failed; producer idle: " + ex.Message);
                return false;
            }
        }
    }
}
