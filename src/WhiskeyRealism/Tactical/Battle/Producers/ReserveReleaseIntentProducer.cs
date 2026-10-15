using System;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // Reserve-release producer (BC5c, spec §4 ladder: "#59 reserve release" at
    // Support). Runtime only.
    //
    // The direct-child allocator releases a Reserve-role group as a role change
    // (DirectChildAllocator.ReleaseReserves, T15: crisis Fix on a neighbouring band,
    // Exploit, or the plan's reserve-commit odds) before the operations ledger
    // re-tasks it; until then the ledger still says Reserve / ReserveWait and #61
    // keeps it waiting. #59 already lets vanilla's own reserve commit through for
    // such a group (TacticalReserveCommitGate.ReleasedByAllocator); this producer
    // also moves the group: a courier Move to stand behind its own line in the
    // band the allocator gave it (TacticalReserveCommitGate.TryPlanRelease, which
    // reads the BattleSnapshot only). Once the ledger catches up the group is no
    // longer held and the posture executor owns it.
    //
    // Registered from the static constructor (BattleCoreTick.RegisterProducer);
    // runs inside the arbiter tick. Rollback: Enable Tactical Orchestrator Reserve
    // Commit Gate = false (the gate and its release are one feature).
    internal sealed class ReserveReleaseIntentProducer : IBattleIntentProducer
    {
        static ReserveReleaseIntentProducer()
        {
            BattleCoreTick.RegisterProducer(new ReserveReleaseIntentProducer());
        }

        private ReserveReleaseIntentProducer()
        {
        }

        public string Name => "ReserveRelease";

        public void Produce(BattleSnapshot snapshot, IIntentSink sink)
        {
            using (TelemetryPerf.Scope("tactical.producer.reserve-release", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (snapshot == null || sink == null || !Enabled()) return;
                ArmyOrchestrator army = TacticalBattleCoordinator.GetSideOrchestrator(snapshot.Alliance)?.Army;
                var operations = army?.CurrentCommandOperations;
                if (operations == null) return;

                for (int i = 0; i < operations.Count; i++)
                {
                    if (!TacticalReserveCommitGate.HeldInReserve(operations[i])) continue;
                    // Ledger node ids are "node-" + the group's GameObject instance id.
                    if (!TacticalPatchIds.TryParseNodeId(operations[i].NodeId, out int id)) continue;
                    int index = snapshot.IndexOfOwn(id);
                    if (index < 0) continue;

                    CommandIntentResolution allocator = army.ResolveCommandIntentForGroup(0, id);
                    CommandIntentResolution held = TacticalReserveCommitGate.HeldResolution(operations, id, 0, allocator);
                    if (!TacticalReserveCommitGate.ReleasedByAllocator(held, allocator)) continue;
                    if (TacticalReserveCommitGate.TryPlanRelease(snapshot, index, allocator.Intent.PrimarySector, out BattleOrderIntent move))
                        sink.Submit(move);
                }
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null
                    && Plugin.Instance.Enabled != null
                    && Plugin.Instance.Enabled.Value
                    && Plugin.EnableTacticalBattleOrchestrator != null
                    && Plugin.EnableTacticalBattleOrchestrator.Value
                    && Plugin.Instance.EnableTacticalOrchestratorReserveCommitGate != null
                    && Plugin.Instance.EnableTacticalOrchestratorReserveCommitGate.Value;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:reserve-release-enabled",
                    "[ReserveReleaseIntentProducer] enable check failed; producer idle: " + ex.Message);
                return false;
            }
        }
    }
}
