using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    /// <summary>
    /// The one NavMesh query of a confirmed leg (requirement-navmesh-confirm): a complete path gives its polyline cost in cost units, a path
    /// that is not complete gives <paramref name="noPath"/>. It returns false when the query itself failed: the leg then stays an estimate.
    /// The runtime supplies it; a test scripts it.
    /// </summary>
    public delegate bool LegQuery(float x0, float z0, float x1, float z1, int crossing, out float cost, out bool noPath);

    /// <summary>
    /// BC14 requirement-navmesh-confirm: the NavMesh results the route screen reads in place of its sampled estimates, one query per side per
    /// tick. Legs are requested in priority order (the selected plan's first, then the best candidates'); each tick answers the front of the queue,
    /// and an answer stands for <see cref="ConfirmCacheHours"/> doctrine hours, keyed by the origin's and the target's 50-unit buckets and the
    /// crossing (the route plan cache's precedent). A leg is a cost in the units of <see cref="LegSampler"/>, the same for either direction,
    /// so the two ends of a key are ordered. Pooled: the queue and the cache are sized once and never grow.
    /// </summary>
    public sealed class ConfirmedLegs : IConfirmedLegs
    {
        public const int ConfirmQueriesPerTick = 1;
        public const float ConfirmCacheHours = 0.25f;
        public const int MaxQueued = 64, MaxCached = 256;

        private struct Entry
        {
            public float Cost;
            public bool NoPath;
            public double AtHours;
        }

        private struct Pending
        {
            public float X0, Z0, X1, Z1;
            public int Crossing;
            public (int, int, int) Key;
        }

        private readonly Dictionary<(int, int, int), Entry> _cache = new Dictionary<(int, int, int), Entry>(MaxCached);
        private readonly Pending[] _queue = new Pending[MaxQueued];
        private int _head, _count;
        private double _now;

        /// <summary>Queries made since construction (the test counts them against the per-tick budget).</summary>
        public int Queries { get; private set; }

        /// <summary>Legs waiting for a query.</summary>
        public int QueuedCount => _count;

        /// <summary>Sets the doctrine hour the freshness of an answer is judged against.</summary>
        public void SetClock(double nowHours) => _now = nowHours;

        private static (int, int, int) KeyOf(float x0, float z0, float x1, float z1, int crossing)
        {
            int a = LegCostCache.BucketKey(x0, z0), b = LegCostCache.BucketKey(x1, z1);
            return a <= b ? (a, b, crossing) : (b, a, crossing);
        }

        private bool Fresh(in (int, int, int) key, out Entry entry)
            => _cache.TryGetValue(key, out entry) && _now - entry.AtHours < ConfirmCacheHours && _now >= entry.AtHours;

        /// <inheritdoc />
        public bool TryGet(float x0, float z0, float x1, float z1, int crossing, out float cost, out bool noPath)
        {
            var key = KeyOf(x0, z0, x1, z1, crossing);
            if (Fresh(in key, out Entry entry))
            {
                cost = entry.Cost;
                noPath = entry.NoPath;
                return true;
            }
            cost = 0f;
            noPath = false;
            return false;
        }

        /// <summary>Empties the queue (not the cache): a new evaluation's requests replace the old ones, in their own priority order.</summary>
        public void ClearQueue()
        {
            _head = 0;
            _count = 0;
        }

        /// <summary>Queues a leg unless it has a fresh answer or is queued already; a full queue drops it (the next evaluation asks again).</summary>
        public void Request(float x0, float z0, float x1, float z1, int crossing)
        {
            var key = KeyOf(x0, z0, x1, z1, crossing);
            if (Fresh(in key, out _) || _count >= MaxQueued) return;
            for (int i = 0; i < _count; i++)
                if (_queue[(_head + i) % MaxQueued].Key.Equals(key)) return;
            _queue[(_head + _count) % MaxQueued] = new Pending { X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, Crossing = crossing, Key = key };
            _count++;
        }

        /// <summary>
        /// One tick's confirmation: answers at most <see cref="ConfirmQueriesPerTick"/> queued legs through <paramref name="query"/>, skipping those
        /// that gained a fresh answer meanwhile; a query that fails (returns false or throws) caches nothing. Returns how many queries it made.
        /// </summary>
        public int Process(LegQuery query)
        {
            int made = 0;
            while (_count > 0 && made < ConfirmQueriesPerTick)
            {
                Pending leg = _queue[_head];
                _head = (_head + 1) % MaxQueued;
                _count--;
                if (Fresh(in leg.Key, out _)) continue;
                float cost = 0f;
                bool noPath = false, answered;
                try { answered = query(leg.X0, leg.Z0, leg.X1, leg.Z1, leg.Crossing, out cost, out noPath); }
                catch { answered = false; }
                Queries++;
                made++;
                if (!answered) continue;
                if (_cache.Count >= MaxCached && !_cache.ContainsKey(leg.Key)) Evict();
                _cache[leg.Key] = new Entry { Cost = cost, NoPath = noPath, AtHours = _now };
            }
            return made;
        }

        // The table is full: the stalest answers go first (an answer past its hours is dead anyway).
        private void Evict()
        {
            (int, int, int) oldest = default;
            double oldestAt = double.PositiveInfinity;
            foreach (KeyValuePair<(int, int, int), Entry> pair in _cache)
            {
                if (pair.Value.AtHours >= oldestAt) continue;
                oldestAt = pair.Value.AtHours;
                oldest = pair.Key;
            }
            _cache.Remove(oldest);
        }

        public void Clear()
        {
            ClearQueue();
            _cache.Clear();
            Queries = 0;
        }
    }

    /// <summary>
    /// The legs to confirm for a published evaluation (requirement-navmesh-confirm): the routes of the selected plan's members first, then those
    /// of the best-scored other candidates (<see cref="ManeuverFeasibility.ConfirmCandidates"/> in all, the ledger's candidate order), each
    /// leg through its chosen crossing: the member's origin to the crossing, its target to the crossing, as the route screen asks for them.
    /// </summary>
    internal static class ConfirmRequests
    {
        /// <summary>Clears the legs' queue and queues the routes of the ledger's published candidates, the selected plan's first.</summary>
        public static void Collect(FeasibilityLedger ledger, ConfirmedLegs legs, RiverRegions regions)
        {
            legs.ClearQueue();
            if (ledger == null || !ledger.HasEvaluation || regions == null || !regions.Ready) return;
            if (ledger.HasSelection && ledger.TryGet(ledger.SelectedPlan, out CandidateResult selected)) Add(ledger, in selected, legs, regions);
            for (int rank = 0; rank < ManeuverFeasibility.ConfirmCandidates && rank < ledger.CandidateCount; rank++)
            {
                if (!ledger.TryGetAt(rank, out CandidateResult candidate)) break;
                if (ledger.HasSelection && candidate.Plan == ledger.SelectedPlan) continue;
                Add(ledger, in candidate, legs, regions);
            }
        }

        private static void Add(FeasibilityLedger ledger, in CandidateResult result, ConfirmedLegs legs, RiverRegions regions)
        {
            ReadOnlySpan<MemberEstimate> members = ledger.Members(in result);
            ReadOnlySpan<BodyEstimate> bodies = ledger.Bodies(in result);
            for (int m = 0; m < members.Length; m++)
            {
                MemberEstimate member = members[m];
                if (!ledger.TryChildPosition(member.ChildId, out float originX, out float originZ)) continue;
                ReadOnlySpan<RouteCrossing> crossings = ledger.Crossings(in result, in member);
                SplitLegs(in member, crossings, out int marchCount);
                Leg(legs, regions, originX, originZ, member.TargetX, member.TargetZ, crossings, 0, marchCount);
                if (member.Body < 0 || member.Body >= bodies.Length) continue;
                BodyEstimate body = bodies[member.Body];
                Leg(legs, regions, member.TargetX, member.TargetZ, body.StrikeX, body.StrikeZ, crossings, marchCount, crossings.Length - marchCount);
            }
        }

        // The crossings are the march leg's, then the strike leg's. The strike leg starts at the first later crossing on the member's attack
        // link; with no attack via every crossing is the march leg's, with no march via every one is the strike leg's.
        private static void SplitLegs(in MemberEstimate member, ReadOnlySpan<RouteCrossing> crossings, out int marchCount)
        {
            if (member.AttackVia < 0) { marchCount = crossings.Length; return; }
            if (member.MarchVia < 0) { marchCount = 0; return; }
            marchCount = Math.Max(1, crossings.Length / 2);
            for (int c = 1; c < crossings.Length; c++)
            {
                if (crossings[c].Link != member.AttackVia) continue;
                marchCount = c;
                break;
            }
        }

        // One leg: same region, a point leg; through crossings, the leg from each end to the crossing next to it.
        private static void Leg(ConfirmedLegs legs, RiverRegions regions, float fromX, float fromZ, float toX, float toZ,
            ReadOnlySpan<RouteCrossing> crossings, int start, int count)
        {
            if (count <= 0)
            {
                legs.Request(fromX, fromZ, toX, toZ, -1);
                return;
            }
            Toward(legs, regions, fromX, fromZ, crossings[start].Link);
            Toward(legs, regions, toX, toZ, crossings[start + count - 1].Link);
        }

        private static void Toward(ConfirmedLegs legs, RiverRegions regions, float px, float pz, int link)
        {
            regions.LinkCrossing(link, out float lx, out float lz, out int gridIndex, out _);
            legs.Request(px, pz, lx, lz, gridIndex);
        }
    }
}
