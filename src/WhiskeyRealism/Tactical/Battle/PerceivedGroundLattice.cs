using System;

namespace WhiskeyRealism.Tactical.Battle
{
    // BC13 (spec § 5, requirement-perceived-ground): the incremental probe lattice. An opening that asserts the absence
    // of enemy (a gap, a flank in the air, an unsupported battery) is emitted only when every point of the ground it
    // asserts is perceived. Probing is bounded: at most OpeningDetector.MaxProbePointsPerTick points per side per tick,
    // served oldest lattice first, each candidate's result cached in its lattice so a settled lattice costs nothing.
    // Pure: the oracle (IPerceivedGround) is the only link to the game.

    public enum LatticeState : byte
    {
        Incomplete = 0,
        Perceived = 1,
        Unperceived = 2,
    }

    /// <summary>The perception a lattice began in: the mode, the fallback reason and the doctrine-time bucket floor(now / 0.05 h).</summary>
    public struct ProbeEpoch
    {
        public PerceptionMode Mode;
        public int Fallback;
        public long Bucket;

        public static long BucketOf(double nowHours) => (long)Math.Floor(nowHours / OpeningDetector.ProbeEpochHours);
    }

    /// <summary>What one Serve call did, for the TacticalOpening row.</summary>
    public struct LatticeTickStats
    {
        /// <summary>Probe points evaluated this call.</summary>
        public int PointsProbed;
        /// <summary>Point results reused from earlier ticks (a settled lattice's whole box, an incomplete one's cache).</summary>
        public int PointsCached;
        /// <summary>Lattices that reached their start (the first tick on which one is the side's oldest incomplete lattice).</summary>
        public int Started;
        /// <summary>Lattices that completed or ended unperceived.</summary>
        public int Ended;
        /// <summary>Lattices that ended more than ceil(points / MaxProbePointsPerTick) ticks after their start (0 expected).</summary>
        public int OverBound;
    }

    /// <summary>
    /// One candidate's probe state: the box it proves, the epoch it began in, how far it got and what it found. A
    /// lattice in progress completes against the epoch it began in; only a change of its geometry (an edge moved by
    /// more than half of the probe spacing), of the perception mode or of the fallback reason clears and restarts it.
    /// A completed lattice is re-probed when the time bucket turns, keeping its last complete result emitted meanwhile.
    /// </summary>
    public sealed class PerceivedGroundLattice
    {
        public bool InUse;
        /// <summary>Set by the detector for a lattice a candidate asked for this tick.</summary>
        public bool Claimed;
        public int Slot;
        public OpeningKind Kind;
        public ProbeBox Box;
        public ProbeEpoch Epoch;
        public LatticeState State;
        /// <summary>The last COMPLETE result was Perceived: kept while a re-probe runs, cleared by a geometry, mode or fallback restart.</summary>
        public bool LastResultPerceived;
        /// <summary>Serving order: lattices are served in the order they began; a restarted lattice joins the back.</summary>
        public long Order;
        /// <summary>The first tick on which this lattice was its side's oldest incomplete one; -1 until then.</summary>
        public long StartTick = -1;
        /// <summary>Points evaluated and perceived so far; the cache.</summary>
        public int Next;

        public int ProbePoints => Box.PointCount;
        public int CachedPoints => Next;

        public void Begin(int candidateSlot, OpeningKind kind, in ProbeBox box, in ProbeEpoch epoch, bool keepLastResult, long order)
        {
            Slot = candidateSlot;
            Kind = kind;
            Box = box;
            Epoch = epoch;
            State = LatticeState.Incomplete;
            LastResultPerceived = InUse && keepLastResult && LastResultPerceived;
            InUse = true;
            Order = order;
            StartTick = -1;
            Next = 0;
        }

        /// <summary>
        /// Whether the lattice must begin again for this box and epoch: a geometry change (30 m moves an edge past the
        /// half-spacing tolerance), a perception mode or fallback change always; a time-bucket turn only once the
        /// lattice is complete. <paramref name="keepLastResult"/> is true only for the bucket turn.
        /// </summary>
        public bool NeedsRestart(in ProbeBox box, in ProbeEpoch now, out bool keepLastResult)
        {
            keepLastResult = false;
            if (!InUse) return true;
            if (Epoch.Mode != now.Mode || Epoch.Fallback != now.Fallback || Box.GeometryDiffers(in box, OpeningDetector.ProbeSpacingMeters * 0.5f)) return true;
            if (State != LatticeState.Incomplete && Epoch.Bucket < now.Bucket)
            {
                keepLastResult = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Evaluates points in order until the budget is spent, the lattice is complete or a point is unperceived (which
        /// ends it at once). Each evaluated point costs one of the budget. A throwing oracle reads as unperceived.
        /// </summary>
        public LatticeState Step(IPerceivedGround ground, ref int budget)
        {
            if (State != LatticeState.Incomplete) return State;
            int count = Box.PointCount;
            while (Next < count && budget > 0)
            {
                Box.PointAt(Next, out float x, out float z);
                budget--;
                bool seen;
                try { seen = ground.IsPerceived(x, z); }
                catch { seen = false; }
                if (!seen)
                {
                    State = LatticeState.Unperceived;
                    LastResultPerceived = false;
                    return State;
                }
                Next++;
            }
            if (Next >= count)
            {
                State = LatticeState.Perceived;
                LastResultPerceived = true;
            }
            return State;
        }

        private static int[] _order = new int[16];

        /// <summary>
        /// One tick of the cap over the claimed lattices of a side: the oldest incomplete lattice starts (and so receives
        /// the whole budget), then each incomplete one in order takes what is left. A lattice that completes or ends
        /// unperceived without ever having been the oldest starts and ends in that tick.
        /// </summary>
        public static void Serve(PerceivedGroundLattice[] pool, int poolSize, IPerceivedGround ground, int budget, long tick, ref LatticeTickStats stats)
        {
            if (_order.Length < poolSize) _order = new int[Math.Max(poolSize, _order.Length * 2)];
            int m = 0;
            for (int i = 0; i < poolSize; i++)
            {
                PerceivedGroundLattice lattice = pool[i];
                if (lattice == null || !lattice.InUse || !lattice.Claimed) continue;
                stats.PointsCached += lattice.State == LatticeState.Incomplete ? lattice.CachedPoints : lattice.ProbePoints;
                if (lattice.State != LatticeState.Incomplete) continue;
                int at = m++;
                while (at > 0 && pool[_order[at - 1]].Order > lattice.Order)
                {
                    _order[at] = _order[at - 1];
                    at--;
                }
                _order[at] = i;
            }
            if (m == 0) return;

            PerceivedGroundLattice oldest = pool[_order[0]];
            if (oldest.StartTick < 0)
            {
                oldest.StartTick = tick;
                stats.Started++;
            }
            int startBudget = budget;
            for (int k = 0; k < m && budget > 0; k++)
            {
                PerceivedGroundLattice lattice = pool[_order[k]];
                if (lattice.Step(ground, ref budget) == LatticeState.Incomplete) continue;
                stats.Ended++;
                if (lattice.StartTick < 0)
                {
                    lattice.StartTick = tick;
                    stats.Started++;
                }
                int bound = (lattice.ProbePoints + OpeningDetector.MaxProbePointsPerTick - 1) / OpeningDetector.MaxProbePointsPerTick;
                if (tick - lattice.StartTick + 1 > Math.Max(1, bound)) stats.OverBound++;
            }
            stats.PointsProbed += startBudget - budget;
        }
    }

    /// <summary>
    /// RangeOnly perception (fog off, HQ in the saddle, grid not painted, perception flag off): a point is perceived when
    /// it lies within an own unit's reach (BattlePerception.RangeOnlyReach), the union of the lists vanilla's AI enemy
    /// lookups use. The runtime hands in the arrays the perception probe already holds.
    /// </summary>
    public sealed class RangeOnlyPerceivedGround : IPerceivedGround
    {
        private float[] _x = Array.Empty<float>(), _z = Array.Empty<float>(), _reach = Array.Empty<float>();
        private int _count;

        public void SetReach(float[] ownX, float[] ownZ, float[] reach, int count)
        {
            _x = ownX ?? Array.Empty<float>();
            _z = ownZ ?? Array.Empty<float>();
            _reach = reach ?? Array.Empty<float>();
            _count = Math.Max(0, Math.Min(count, Math.Min(_x.Length, Math.Min(_z.Length, _reach.Length))));
        }

        public bool IsPerceived(float x, float z) => BattlePerception.WithinAnyReach(_x, _z, _reach, _count, x, z);
    }
}
