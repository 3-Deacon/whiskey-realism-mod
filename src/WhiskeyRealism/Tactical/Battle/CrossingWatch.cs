using System;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>How the side knows a link crossing (BC14 requirement-crossing-state).</summary>
    public enum CrossingState : byte
    {
        /// <summary>Not perceived recently, and no enemy known near it.</summary>
        Unknown = 0,
        /// <summary>Perceived within ClearMemoryHours and no perceived enemy near it.</summary>
        Clear = 1,
        /// <summary>A perceived enemy regiment (artillery included) stands within its own reach of it, not covering it.</summary>
        Watched = 2,
        /// <summary>A perceived enemy regiment covers it by vanilla's rule (a): facing it, within 1.5 x its fire range.</summary>
        Held = 3,
    }

    // BC14 requirement-crossing-state: the per-side picture of the river regions' link crossings, from the side's own observations
    // only (EnemySamples.Accepts is the fog rule; a hidden enemy changes nothing). Pure; one instance per side, owned by the
    // feasibility runtime and reset with the battle. Arrays are sized once to the ceiling, so Classify allocates nothing.
    //
    //   Held     a perceived non-artillery enemy regiment covers the crossing (CoveredCrossings.Covers, the rule BuildPoints uses);
    //            the perceived strength within HoldRadiusMeters is kept with it (Visible full, Remembered x confidence).
    //   Watched  not Held, but a perceived enemy regiment stands within its own reach of it (BattlePerception.RangeOnlyReach per
    //            unit type, handed in by the runtime from the line-of-sight table).
    //   Clear    neither, and the crossing point was perceived (IPerceivedGround) within the last ClearMemoryHours.
    //   Unknown  everything else.
    //
    // Probes: every link is a probe candidate, oldest attempt first, at most MaxCrossingProbesPerTick a call; a perceived probe is
    // stamped with the doctrine hour, so a crossing is never Clear on the strength of a stale probe.
    public sealed class CrossingWatch
    {
        public const float HoldRadiusMeters = 300f, ClearMemoryHours = 0.25f;
        public const int MaxCrossingProbesPerTick = 16;

        private readonly CrossingState[] _state = new CrossingState[RiverRegions.MaxCrossings];
        private readonly float[] _heldStrength = new float[RiverRegions.MaxCrossings];
        private readonly double[] _lastPerceived = new double[RiverRegions.MaxCrossings];
        private readonly double[] _lastProbed = new double[RiverRegions.MaxCrossings];
        private readonly bool[] _pickedThisTick = new bool[RiverRegions.MaxCrossings];
        private int _linkCount;

        public CrossingWatch()
        {
            for (int i = 0; i < _lastPerceived.Length; i++)
            {
                _lastPerceived[i] = double.NegativeInfinity;
                _lastProbed[i] = double.NegativeInfinity;
            }
        }

        /// <summary>Probes made by the last Classify call.</summary>
        public int ProbesLastTick { get; private set; }

        public int LinkCount => _linkCount;

        public CrossingState StateOf(int link) => link >= 0 && link < _linkCount ? _state[link] : CrossingState.Unknown;

        /// <summary>The perceived enemy strength within HoldRadiusMeters of a Held crossing; 0 for any other.</summary>
        public float HeldStrength(int link) => link >= 0 && link < _linkCount ? _heldStrength[link] : 0f;

        /// <summary>The doctrine hour the crossing point was last perceived; negative infinity when never.</summary>
        public double LastPerceivedHours(int link) => link >= 0 && link < _linkCount ? _lastPerceived[link] : double.NegativeInfinity;

        public int CountBy(CrossingState state)
        {
            int n = 0;
            for (int i = 0; i < _linkCount; i++) if (_state[i] == state) n++;
            return n;
        }

        /// <summary>
        /// Probes the oldest-probed links, then classifies every link. <paramref name="reachByUnitType"/> is the line-of-sight reach
        /// a regiment of each unit type sees to (indexed by <c>UnitTyp</c>); a type with no entry watches nothing.
        /// With no Ready regions every state reads Unknown.
        /// </summary>
        public void Classify(BattleSnapshot s, RiverRegions regions, BattleTerrainGrid grid, IPerceivedGround perceived,
            ReadOnlySpan<float> reachByUnitType, double nowHours)
        {
            ProbesLastTick = 0;
            if (s == null || regions == null || !regions.Ready)
            {
                _linkCount = 0;
                return;
            }
            _linkCount = Math.Min(regions.LinkCount, _state.Length);
            if (perceived != null) Probe(regions, perceived, nowHours);
            for (int link = 0; link < _linkCount; link++)
            {
                regions.LinkCrossing(link, out float cx, out float cz, out _, out _);
                _heldStrength[link] = 0f;
                if (IsHeld(s, cx, cz, out float strength))
                {
                    _state[link] = CrossingState.Held;
                    _heldStrength[link] = strength;
                }
                else if (IsWatched(s, cx, cz, reachByUnitType))
                {
                    _state[link] = CrossingState.Watched;
                }
                else
                {
                    _state[link] = nowHours - _lastPerceived[link] <= ClearMemoryHours ? CrossingState.Clear : CrossingState.Unknown;
                }
            }
        }

        // The oldest-probed links first, ties to the lower index; each probe is stamped with the attempt, and with the doctrine hour
        // when the point is perceived.
        private void Probe(RiverRegions regions, IPerceivedGround perceived, double nowHours)
        {
            Array.Clear(_pickedThisTick, 0, _linkCount);
            int probes = Math.Min(MaxCrossingProbesPerTick, _linkCount);
            for (int p = 0; p < probes; p++)
            {
                int pick = -1;
                double oldest = double.PositiveInfinity;
                for (int i = 0; i < _linkCount; i++)
                {
                    if (_pickedThisTick[i] || !(_lastProbed[i] < oldest)) continue;
                    oldest = _lastProbed[i];
                    pick = i;
                }
                if (pick < 0) break;
                _pickedThisTick[pick] = true;
                _lastProbed[pick] = nowHours;
                regions.LinkCrossing(pick, out float cx, out float cz, out _, out _);
                bool seen;
                try { seen = perceived.IsPerceived(cx, cz); }
                catch { seen = false; }     // BC13's rule: a probe that throws is unperceived
                if (seen) _lastPerceived[pick] = nowHours;
                ProbesLastTick++;
            }
        }

        private static bool IsHeld(BattleSnapshot s, float cx, float cz, out float strength)
        {
            strength = 0f;
            bool held = false;
            float r2 = HoldRadiusMeters * HoldRadiusMeters;
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                ref EnemyObservation o = ref obs[k];
                if (CoveredCrossings.Covers(in o, cx, cz)) held = true;
            }
            if (!held) return false;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                ref EnemyObservation o = ref obs[k];
                if (!EnemySamples.Accepts(in o)) continue;
                float dx = o.X - cx, dz = o.Z - cz;
                if (dx * dx + dz * dz > r2) continue;
                strength += o.State == ObservationState.Visible ? o.Strength : o.Strength * o.Confidence01;
            }
            return true;
        }

        private static bool IsWatched(BattleSnapshot s, float cx, float cz, ReadOnlySpan<float> reachByUnitType)
        {
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                ref EnemyObservation o = ref obs[k];
                if (!EnemySamples.Accepts(in o) || o.UnitTyp >= reachByUnitType.Length) continue;
                float reach = reachByUnitType[o.UnitTyp];
                float dx = o.X - cx, dz = o.Z - cz;
                if (dx * dx + dz * dz <= reach * reach) return true;
            }
            return false;
        }
    }
}
