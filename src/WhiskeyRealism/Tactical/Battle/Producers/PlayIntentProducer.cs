using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle.Producers
{
    // Plays as battle-core intent producers (plan BC5a).
    //
    // The geometric play (PlayExecutorRuntime, 18 templates) is not a mover in
    // the shipped code: it assigns corps to template rects that feed the direct
    // child allocator (ObserveDirectChildEvidenceWithZones), so its effect
    // reaches the battle as ledger roles and tasks, which the Posture producer
    // executes. It has no orders to submit, so no "Play" producer is registered;
    // the BattleCoreTick order keeps the slot for one.
    //
    // The division play (#61 BuildRuntimeDivisionPlayOrders, B-divplay) is
    // registered as "DivisionPlay" at Priority.Play. D8: the spec ladder is
    // binding — the posture executor (Priority.Executor) beats the play on the
    // same channel, so a play order now wins only on a channel where the
    // Posture producer submitted nothing for that group. #61 instead replaced the
    // ledger task with the play task before deciding (patch L156-172).
    internal sealed class DivisionPlayIntentProducer : IBattleIntentProducer
    {
        private static readonly DivisionPlayIntentProducer Instance = new DivisionPlayIntentProducer();

        static DivisionPlayIntentProducer()
        {
            BattleScope.Register("division-play-producer", Reset);
            BattleCoreTick.RegisterProducer(Instance);
        }

        internal static void Register() { }

        public string Name => "DivisionPlay";

        private sealed class Child
        {
            public int Index;
            public CommandNodeOperationalState State;
            public bool HasOrder;
            public CommandDoctrineOrder Order;
            public TacticalDivisionPlaySubordinate Subordinate;
        }

        // Pooled per tick: children grouped by their command parent's own index.
        private static readonly Dictionary<int, List<Child>> _byParent = new Dictionary<int, List<Child>>();
        private static readonly List<Child> _pool = new List<Child>(64);
        private static readonly List<int> _parents = new List<int>(16);
        private static readonly List<TacticalDivisionPlaySubordinate> _subordinates = new List<TacticalDivisionPlaySubordinate>(16);
        private static int _poolUsed;

        public void Produce(BattleSnapshot s, IIntentSink sink)
        {
            using (TelemetryPerf.Scope("tactical.producer.play", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PostureTick.State t = PostureTick.Current(s);
                TacticalBattleOrchestrator side = s != null ? TacticalBattleCoordinator.GetSideOrchestrator(s.Alliance) : null;
                ArmyOrchestrator army = side?.Army;
                if (t == null || army == null || sink == null) return;

                Collect(s, t, side, army);
                bool defending = ArmyPostureInputsRuntime.SideDefending(s.Alliance);
                for (int p = 0; p < _parents.Count; p++)
                {
                    List<Child> children = _byParent[_parents[p]];
                    if (children.Count < 2) continue;
                    _subordinates.Clear();
                    for (int c = 0; c < children.Count; c++) _subordinates.Add(children[c].Subordinate);

                    TacticalDivisionPlayDecision decision = TacticalDivisionPlayExecutor.Decide(
                        new TacticalDivisionPlayInput("parent-go:" + s.Own[_parents[p]].InstanceId, _subordinates, defending));
                    if (!decision.HasAnchor) continue;

                    for (int o = 0; o < decision.Orders.Count; o++)
                    {
                        TacticalDivisionPlayOrder play = decision.Orders[o];
                        if (!play.HasOrder) continue;
                        Child child = Find(children, play.NodeId);
                        if (child == null) continue;
                        CommandNodeOperationalState state = PosturePipeline.WithRoleAndTask(child.State, child.State.Role, play.Task);
                        CommandDoctrineOrder order = child.HasOrder ? child.Order.WithTask(play.Task, play.Reason) : child.Order;
                        PosturePipeline.Run(s, t, child.Index, state, child.HasOrder, order, side, sink,
                            IntentPriority.Play, IntentSource.DivisionPlay, PostureIntentPlan.DivisionPlayReason,
                            recordTick: false);
                    }
                }
            }
        }

        // The AI pool groups with a ledger state and a command parent (B-divplay:
        // a group directly under the units root has no division to play with).
        private static void Collect(BattleSnapshot s, PostureTick.State t, TacticalBattleOrchestrator side, ArmyOrchestrator army)
        {
            foreach (var list in _byParent.Values) list.Clear();
            _parents.Clear();
            _poolUsed = 0;
            UnitRecord[] own = s.Own;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if (!PostureIntentProducer.IsCommandGroup(own[i])) continue;
                if (!ArmyPostureInputsRuntime.InAiPool(s.Alliance, own[i].InstanceId)) continue;
                int parent = TacticalDivisionPlayInputs.ParentIndex(s, i);
                if (parent < 0) continue;
                if (!PostureLedger.TryResolveState(own[i].InstanceId, side, out CommandNodeOperationalState state)) continue;

                Child child = Next();
                child.Index = i;
                child.State = state;
                child.HasOrder = PostureLedger.TryResolveDoctrineOrder(own[i].InstanceId, army, state.NodeId, out child.Order);
                child.Subordinate = TacticalDivisionPlayInputs.FromSnapshot(
                    s, i, t.Descendants[i], t.Moving[i], state.NodeId, state.Role, state.Task);
                if (!_byParent.TryGetValue(parent, out List<Child> children))
                {
                    children = new List<Child>(4);
                    _byParent[parent] = children;
                }
                if (children.Count == 0) _parents.Add(parent);
                children.Add(child);
            }
        }

        private static Child Next()
        {
            if (_poolUsed == _pool.Count) _pool.Add(new Child());
            return _pool[_poolUsed++];
        }

        private static Child Find(List<Child> children, string nodeId)
        {
            for (int c = 0; c < children.Count; c++)
                if (string.Equals(children[c].State.NodeId, nodeId, System.StringComparison.Ordinal)) return children[c];
            return null;
        }

        private static void Reset()
        {
            _byParent.Clear();
            _parents.Clear();
            _poolUsed = 0;
        }
    }
}
