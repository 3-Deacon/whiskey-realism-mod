using System.Collections.Generic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // One claim per (group, channel) per arbiter tick (plan BC4, spec §4).
    //
    // Producers submit intents inside BattleCoreTick.RunArbiter (Postfix on
    // AIBattle.AdjustGroupFormations, 5870, after vanilla's own movers in the same
    // UpdateAITasks: UpdateMovingTargets 5862, CheckFlankMoves 5867,
    // AdjustGroupAIStance 5869). Resolve keeps the highest IntentPriority per
    // (group, channel); a tie keeps the source that won the previous tick
    // (hysteresis), else the first submitted. Winners come out in execution order
    // Stop → Move → Stance → Formation → Facing → CombatBehavior. A move comes
    // before every stance order, whatever group it is for: vanilla queues each
    // order on the unit's parent behind the latest queued processing time, 0.02 h
    // apart (AddToOrderQueue 124954-124966), so a stance order issued first
    // delays every move issued after it (the 2026-10-16 smoke: eight stance
    // orders put the brigade's move 0.16 h later). A stop comes before a move
    // (it would cancel the new path), and a formation only follows a move that
    // did not go out (#61 L976-988), because vanilla's courier formation order
    // re-lays a moving group at its current position (SetGroupFormation newpath,
    // 91884). Stance before the waypoint (#61 L768-774) ordered nothing that
    // matters: ChangeStance detaches the group at the call (90782-90790) and
    // vanilla's own AdjustGroupAIStance has already run for the pool in the same
    // UpdateAITasks (5869), so a pool group is detached before this tick's moves.
    //
    // The resolved winners are published as the claim table that patches read
    // through TryGetClaim (D4: inline executor calls are refused under a higher
    // claim). Patches never submit.
    //
    // Allocation: the intent buffer and the per-slot tables grow only when the
    // roster or the intent count exceeds every earlier tick; Clear() keeps the
    // dictionary buckets, so a steady battle allocates nothing per tick.
    public sealed class OrderArbiter : IIntentSink
    {
        // = #61 MinWaypointDistance (BattleCommandPostureExecutorPatch.cs:36-43):
        // raised 15 → 60 m on 2026-05-21 because probe targets drift 20-50 m as a
        // group moves and every re-issue made vanilla re-path it (microshift).
        public const float DedupeMeters = 60f;

        private static readonly OrderChannel[] ExecutionOrder =
        {
            OrderChannel.Stop,
            OrderChannel.Move,
            OrderChannel.Stance,
            OrderChannel.Formation,
            OrderChannel.Facing,
            OrderChannel.CombatBehavior,
        };

        private static readonly OrderArbiter[] _byAlliance = { new OrderArbiter(), new OrderArbiter() };

        static OrderArbiter()
        {
            BattleScope.Register("order-arbiter", ResetAll);
        }

        private BattleOrderIntent[] _intents = new BattleOrderIntent[64];
        private int _intentCount;

        // Working table (this tick) and published table (last resolved tick);
        // swapped at the end of Resolve.
        private ClaimTable _working = new ClaimTable();
        private ClaimTable _published = new ClaimTable();

        public int IntentCount => _intentCount;

        /// <summary>Bound-checked: alliances 0 and 1 only; Europe (2) and anything else → null.</summary>
        public static OrderArbiter ForAlliance(int alliance)
        {
            if (alliance < 0 || alliance >= _byAlliance.Length) return null;
            return _byAlliance[alliance];
        }

        public void BeginTick()
        {
            _intentCount = 0;
            _working.Clear();
        }

        public void Submit(in BattleOrderIntent intent)
        {
            if (intent.GroupId == 0) return;
            // FeudReplay is #42's inline replay of vanilla's own feud move and the
            // only source the executor exempts from the Feud refusal; it reaches the
            // executor through OrderExecutorRuntime.ExecuteInline only, never as a
            // producer intent (so no arbiter claim or retry can carry the exemption).
            if (intent.Source == IntentSource.FeudReplay) return;
            int channel = (int)intent.Channel;
            if (channel < 0 || channel >= OrderChannels.Count) return;
            if (_intentCount == _intents.Length)
                System.Array.Resize(ref _intents, _intents.Length * 2);
            _intents[_intentCount++] = intent;
        }

        /// <summary>
        /// Resolves this tick's intents into one winner per (group, channel), writes
        /// them to <paramref name="winners"/> in execution order (up to its length) and
        /// publishes them as the claim table. Returns the number written.
        /// </summary>
        public int Resolve(BattleOrderIntent[] winners)
        {
            for (int i = 0; i < _intentCount; i++)
            {
                ref BattleOrderIntent candidate = ref _intents[i];
                int cell = _working.CellFor(candidate.GroupId, candidate.Channel);
                if (!_working.Has[cell])
                {
                    _working.Set(cell, candidate);
                    continue;
                }

                ref BattleOrderIntent current = ref _working.Winners[cell];
                if (candidate.Priority > current.Priority)
                {
                    _working.Set(cell, candidate);
                    continue;
                }

                if (candidate.Priority == current.Priority &&
                    candidate.Source != current.Source &&
                    _published.TryGet(candidate.GroupId, candidate.Channel, out BattleOrderIntent previous) &&
                    previous.Source == candidate.Source)
                    _working.Set(cell, candidate);
            }

            int written = 0;
            int capacity = winners != null ? winners.Length : 0;
            for (int c = 0; c < ExecutionOrder.Length; c++)
            {
                int channel = (int)ExecutionOrder[c];
                for (int slot = 0; slot < _working.SlotCount && written < capacity; slot++)
                {
                    int cell = slot * OrderChannels.Count + channel;
                    if (_working.Has[cell]) winners[written++] = _working.Winners[cell];
                }
            }

            ClaimTable resolved = _working;
            _working = _published;
            _published = resolved;
            return written;
        }

        /// <summary>The last resolved winner on (group, channel). Read-only for patches (D4).</summary>
        public bool TryGetClaim(int groupId, OrderChannel channel, out BattleOrderIntent claim)
        {
            return _published.TryGet(groupId, channel, out claim);
        }

        /// <summary>
        /// Drops this tick's intents and the published claim table: the arbiter is
        /// gated off (or stood down), so inline intents must not be refused by the
        /// claims of a tick that no longer applies. Also the battle-end reset.
        /// </summary>
        public void ClearClaims()
        {
            _intentCount = 0;
            _working.Clear();
            _published.Clear();
        }

        private static void ResetAll()
        {
            for (int i = 0; i < _byAlliance.Length; i++) _byAlliance[i].ClearClaims();
        }

        // Groups get a slot in first-submission order; each slot holds one cell per channel.
        private sealed class ClaimTable
        {
            private readonly Dictionary<int, int> _slotByGroup = new Dictionary<int, int>(256);
            public BattleOrderIntent[] Winners = new BattleOrderIntent[64 * OrderChannels.Count];
            public bool[] Has = new bool[64 * OrderChannels.Count];
            public int SlotCount;

            public void Clear()
            {
                _slotByGroup.Clear();
                System.Array.Clear(Has, 0, SlotCount * OrderChannels.Count);
                SlotCount = 0;
            }

            public int CellFor(int groupId, OrderChannel channel)
            {
                if (!_slotByGroup.TryGetValue(groupId, out int slot))
                {
                    slot = SlotCount++;
                    if (SlotCount * OrderChannels.Count > Has.Length)
                    {
                        System.Array.Resize(ref Winners, Winners.Length * 2);
                        System.Array.Resize(ref Has, Has.Length * 2);
                    }
                    _slotByGroup.Add(groupId, slot);
                }
                return slot * OrderChannels.Count + (int)channel;
            }

            public void Set(int cell, in BattleOrderIntent intent)
            {
                Winners[cell] = intent;
                Has[cell] = true;
            }

            public bool TryGet(int groupId, OrderChannel channel, out BattleOrderIntent intent)
            {
                int c = (int)channel;
                if (c >= 0 && c < OrderChannels.Count && _slotByGroup.TryGetValue(groupId, out int slot))
                {
                    int cell = slot * OrderChannels.Count + c;
                    if (Has[cell])
                    {
                        intent = Winners[cell];
                        return true;
                    }
                }
                intent = default(BattleOrderIntent);
                return false;
            }
        }
    }
}
