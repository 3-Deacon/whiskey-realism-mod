using System;
using System.Collections.Generic;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Tactical.Battle.Producers;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
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
    // band the allocator gave it (DirectChildIntent.AxisSector) (TacticalReserveCommitGate.TryPlanRelease, which
    // reads the BattleSnapshot only). Once the ledger catches up the group is no
    // longer held and the posture executor owns it.
    //
    // The Defender's leaf reserve (verify r2 F7): the same producer also moves a holding command's reserve children. A held reserve stands in its
    // slot behind the command's line (LeafReservePlanner: DivisionSupportMeters back, on the own side of every river, inside the own deployment zone
    // while deploying); a reserve LeafReserveRelease has sent out goes to its trigger point (the crisis band's line, the exposed wing's end, the main
    // effort's band) and returns to its slot when the release ends. Source ReserveRelease, so the crossing gate sees every one of these moves.
    //
    // Built by BattleCoreTick's producer registry; runs inside the arbiter tick.
    // Rollback: Enable Tactical Orchestrator Reserve Commit Gate = false (the gate
    // and its release are one feature).
    internal sealed class ReserveReleaseIntentProducer : IBattleIntentProducer
    {
        public string Name => "ReserveRelease";

        private const float LeafRowSeconds = 30f;
        private static readonly Dictionary<string, float> _lastLeafRowAt = new Dictionary<string, float>();
        private static readonly List<LeafReserveMove> _moves = new List<LeafReserveMove>(8);

        static ReserveReleaseIntentProducer()
        {
            BattleScope.Register("reserve-release-producer", () => { _lastLeafRowAt.Clear(); _moves.Clear(); });
        }

        public void Produce(BattleSnapshot snapshot, IIntentSink sink)
        {
            using (TelemetryPerf.Scope("tactical.producer.reserve-release", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (snapshot == null || sink == null || !Enabled()) return;
                ArmyOrchestrator army = TacticalBattleCoordinator.GetSideOrchestrator(snapshot.Alliance)?.Army;
                if (army == null) return;
                ProduceLeafReserves(snapshot, army, sink);
                var operations = army.CurrentCommandOperations;
                if (operations == null) return;

                for (int i = 0; i < operations.Count; i++)
                {
                    if (!TacticalReserveCommitGate.HeldInReserve(operations[i])) continue;
                    // Ledger node ids are "node-" + the group's GameObject instance id.
                    if (!TacticalPatchIds.TryParseNodeId(operations[i].NodeId, out int id)) continue;
                    int index = snapshot.IndexOfOwn(id);
                    if (index < 0) continue;

                    // No per-tick strings: the direct child's intent is matched by id in place.
                    if (!TacticalReserveCommitGate.TryReleasedDirectChild(army.CurrentDirectChildIntents, id, out int sector)) continue;
                    if (TacticalReserveCommitGate.TryPlanRelease(snapshot, index, sector, out BattleOrderIntent move))
                    {
                        PostureRoutes.AttachIfInColumn(snapshot, index, ref move);
                        sink.Submit(move);
                    }
                }
            }
        }

        // The Defender's holding commands' reserves: slots and releases, then one throttled TacticalLeafReserve row per new order.
        private static void ProduceLeafReserves(BattleSnapshot snapshot, ArmyOrchestrator army, IIntentSink sink)
        {
            var commands = army.CurrentDefenderReserveReport;
            if (commands == null || commands.Count == 0 || army.CurrentBattleStance != BattleStance.Defender) return;
            PostureTick.State tick = PostureTick.For(snapshot);
            BattleUnits bunits = BattleCoreTick.ResolveBattleUnits();
            _moves.Clear();
            LeafReservePlanner.Produce(snapshot, commands, army.LeafRelease.Released, army.LeafRelease.Releases,
                tick?.Descendants, tick?.Moving, FeasibilityRuntime.CrossingPicture(snapshot.Alliance),
                OwnDeploymentZoneRuntime.For(bunits, snapshot.Alliance), sink, _moves);
            for (int i = 0; i < _moves.Count; i++) EmitLeafRow(snapshot.Alliance, _moves[i]);
        }

        private static void EmitLeafRow(int alliance, in LeafReserveMove move)
        {
            try
            {
                string kind = move.Kind == LeafReserveMoveKind.Slot ? "slot" : "release";
                string signature = kind + ":" + (int)(move.X / 50f) + ":" + (int)(move.Z / 50f);
                if (!TacticalTelemetry.ShouldEmit(_lastLeafRowAt, "tactical-leaf-reserve:" + alliance + ":" + move.ChildId, signature,
                        Environment.TickCount * 0.001f, LeafRowSeconds, false))
                    return;
                LeafReserveMove row = move;
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalLeafReserve", TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(alliance)
                        .WithDecision("TacticalLeafReserve", kind, "side=" + alliance + "|child=" + row.ChildId)
                        .WithField("child", "child-" + row.ChildId)
                        .WithField("parent", row.ParentId)
                        .WithField("kind", kind)
                        .WithField("x", row.X)
                        .WithField("z", row.Z)
                        .WithField("distance", row.Distance)
                        .WithField("role", row.Role.ToString())
                        .WithField("sector", row.Sector));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:leaf-reserve-row", "[LeafReserve] row failed: " + ex.GetType().Name);
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
