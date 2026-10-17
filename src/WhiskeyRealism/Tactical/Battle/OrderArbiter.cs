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
    // BC15 carried claims: a Distant group that is not due this tick (BattleLod) submits nothing, so CarryForward copies the claims it published
    // last tick into this tick's table, marked carried. Resolve treats a carried cell like a submitted intent of the same priority and source:
    // another producer's intent beats or loses to it by the usual rule, a tie keeps the carried source (the hysteresis, which reads the same
    // published cell), and a fresh intent of the carried source replaces it (fresh data over the old). A carried cell is published as this
    // tick's claim (D4 refusals, the tie hysteresis and the retry test read it) and is never written to the winners: it was executed when it
    // was submitted, and the executor would plan it as a Duplicate again. It lives until the group is due or promoted; there is no carry
    // count, so uneven tick spacing never leaves a non-due tick without its claim.
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

        private BattleOrderIntent[] _carriedOut = new BattleOrderIntent[64];
        private int _carriedOutCount;

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

                // A fresh intent of the carried cell's own source is that producer's new answer: it replaces the old one.
                if (candidate.Priority == current.Priority && candidate.Source == current.Source && _working.Carried[cell])
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
            _carriedOutCount = 0;
            for (int c = 0; c < ExecutionOrder.Length; c++)
            {
                int channel = (int)ExecutionOrder[c];
                for (int slot = 0; slot < _working.SlotCount; slot++)
                {
                    int cell = slot * OrderChannels.Count + channel;
                    if (!_working.Has[cell]) continue;
                    if (_working.Carried[cell])
                    {
                        if (_carriedOutCount == _carriedOut.Length) System.Array.Resize(ref _carriedOut, _carriedOut.Length * 2);
                        _carriedOut[_carriedOutCount++] = _working.Winners[cell];
                    }
                    else if (written < capacity)
                    {
                        winners[written++] = _working.Winners[cell];
                    }
                }
            }

            ClaimTable resolved = _working;
            _working = _published;
            _published = resolved;
            return written;
        }

        /// <summary>
        /// BC15: copies the claims group <paramref name="groupId"/> published last tick into this tick's table as carried cells, for the sources
        /// of the tiered producers only (<see cref="BattleLod.IsCarriedSource"/>: the others run every tick and resubmit). Call after
        /// <see cref="BeginTick"/> and before any producer. Returns the number of cells carried.
        /// </summary>
        public int CarryForward(int groupId)
        {
            int carried = 0;
            for (int c = 0; c < OrderChannels.Count; c++)
            {
                var channel = (OrderChannel)c;
                if (!_published.TryGet(groupId, channel, out BattleOrderIntent claim) || !BattleLod.IsCarriedSource(claim.Source)) continue;
                int cell = _working.CellFor(groupId, channel);
                if (_working.Has[cell]) continue;
                _working.SetCarried(cell, claim);
                carried++;
            }
            return carried;
        }

        /// <summary>
        /// BC15: a group's carried cells end (its producers are about to run after all, a late wake-up this tick). Submitted intents are not
        /// in the working table until <see cref="Resolve"/>, so before it every cell of the group is a carried one. Returns the number dropped.
        /// </summary>
        public int DropCarried(int groupId)
        {
            return _working.DropCarried(groupId);
        }

        /// <summary>
        /// BC15 decision 14: whether a claim a producer that runs every tick published on one of the group's cells last tick (a source
        /// <see cref="BattleLod.IsCarriedSource"/> does not carry) has not been renewed by that source this tick. Call after the producers
        /// submitted and before <see cref="Resolve"/>: this tick's intents are the submissions. A lapse and a change of source both count; the
        /// intent such a claim outranked has not been seen since it was submitted.
        /// </summary>
        public bool HasLapsedUncarriedClaim(int groupId)
        {
            for (int c = 0; c < OrderChannels.Count; c++)
            {
                if (!_published.TryGet(groupId, (OrderChannel)c, out BattleOrderIntent claim) || BattleLod.IsCarriedSource(claim.Source)) continue;
                bool renewed = false;
                for (int i = 0; i < _intentCount && !renewed; i++)
                    renewed = _intents[i].GroupId == groupId && (int)_intents[i].Channel == c && _intents[i].Source == claim.Source;
                if (!renewed) return true;
            }
            return false;
        }

        /// <summary>
        /// BC15: this tick's carried cells, after <see cref="Resolve"/>, in execution-channel order. They are claims (published) and are not
        /// winners; the executor reads their Move cells for the chain planner's shield (OrderExecutor.BeginTickClaims).
        /// </summary>
        public int CarriedWinners(BattleOrderIntent[] into)
        {
            int n = into != null ? System.Math.Min(_carriedOutCount, into.Length) : 0;
            for (int i = 0; i < n; i++) into[i] = _carriedOut[i];
            return n;
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
            _carriedOutCount = 0;
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
            // BC15: the cell was copied from last tick's published claim for a Distant group that is not due, not submitted this tick.
            public bool[] Carried = new bool[64 * OrderChannels.Count];
            public int SlotCount;

            public void Clear()
            {
                _slotByGroup.Clear();
                System.Array.Clear(Has, 0, SlotCount * OrderChannels.Count);
                System.Array.Clear(Carried, 0, SlotCount * OrderChannels.Count);
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
                        System.Array.Resize(ref Carried, Carried.Length * 2);
                    }
                    _slotByGroup.Add(groupId, slot);
                }
                return slot * OrderChannels.Count + (int)channel;
            }

            public void Set(int cell, in BattleOrderIntent intent)
            {
                Winners[cell] = intent;
                Has[cell] = true;
                Carried[cell] = false;
            }

            public void SetCarried(int cell, in BattleOrderIntent intent)
            {
                Winners[cell] = intent;
                Has[cell] = true;
                Carried[cell] = true;
            }

            public int DropCarried(int groupId)
            {
                if (!_slotByGroup.TryGetValue(groupId, out int slot)) return 0;
                int dropped = 0;
                for (int c = 0; c < OrderChannels.Count; c++)
                {
                    int cell = slot * OrderChannels.Count + c;
                    if (!Has[cell]) continue;
                    Has[cell] = false;
                    Carried[cell] = false;
                    dropped++;
                }
                return dropped;
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
