using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// What one side has seen of the enemy army. Fed once per evidence build from the side's
    /// BattleSnapshot observations (BC2b): visible enemies at full strength and remembered
    /// ones weighted by their confidence (BC2a memory, vanilla AI-side fog), plus the part of
    /// the visible force already in fire range. Everything is on doctrine time
    /// (<see cref="TacticalDoctrineTime"/>, game hours; the snapshot carries it).
    ///
    /// Stands in for whole-army reads the side cannot know: the enemy's
    /// <c>sideinformation.totalactiveforce</c>, its arrival schedule and the enemy AIBattle's
    /// <c>objectivechain[].reservegroups</c> designation.
    /// </summary>
    public sealed class TacticalEnemyContactLedger
    {
        /// <summary>Same sentinel ArmyReplanTriggers / phase doctrine treat as "unknown".</summary>
        public const float UnknownReserveCommitFraction = 0.35f;

        /// <summary>Largest-seen estimate fades 10% per battle hour without re-sighting.</summary>
        public const float LargestSeenDecayPerHour = 0.10f;

        /// <summary>No enemy in sight for this long after contact = contact broken.</summary>
        public const float ContactBrokenAfterHours = 0.25f;

        /// <summary>Contact older than this is no longer evidence of anything.</summary>
        public const float ContactMemoryHours = 2f;

        /// <summary>
        /// Weight of the unwatched share of the enemy-side map entry points:
        /// entry factor = this + (1 − this) × watched share. Not watching an
        /// entry point leaves room for unseen arrivals, but entry points are
        /// map knowledge and do not tell whether anything is coming, so they
        /// can halve the confidence, never zero it.
        /// </summary>
        public const float UnwatchedEntryPointWeight = 0.5f;

        private float _largestSeen;
        private float _lastObserveHours = -1f;
        private float _lastContactHours = -1f;
        /// <summary>
        /// Width of the lateral cells observation stamps are kept on. The
        /// coverage bands re-fit every build, so recency lives on the ground
        /// (front-axis lateral position), not on band indices.
        /// </summary>
        public const float CoverageCellMeters = 25f;

        /// <summary>A heading change beyond this re-bases the lateral frame (stamps cleared).</summary>
        public const float CoverageFrameChangeDegrees = 1f;

        private const int MaxCellsPerBand = 4000;

        private readonly Dictionary<int, float> _cellObservedHours = new Dictionary<int, float>();
        private float[] _bandMin = new float[0];
        private float[] _bandMax = new float[0];
        private float _frameHeading = float.NaN;
        private float _entryShare = 1f;
        private bool _hasCoverage;

        // BC9 where-to-look reads: the enemy-side entry points with the doctrine hour an
        // own unit last watched each (-1 never). Pooled, grow-only.
        private int[] _entryIds = new int[8];
        private float[] _entryX = new float[8];
        private float[] _entryZ = new float[8];
        private float[] _entryWatched = new float[8];
        private int[] _prevIds = new int[8];
        private float[] _prevWatched = new float[8];
        private int _entryCount;

        public float EstimatedEnemyStrength => _largestSeen;
        public bool HadContact => _lastContactHours >= 0f;

        public void Reset()
        {
            _largestSeen = 0f;
            _lastObserveHours = -1f;
            _lastContactHours = -1f;
            _cellObservedHours.Clear();
            _bandMin = new float[0];
            _bandMax = new float[0];
            _frameHeading = float.NaN;
            _entryShare = 1f;
            _hasCoverage = false;
            _entryCount = 0;
        }

        /// <summary>
        /// Records what the side observed this tick: which coverage bands (fitted
        /// over own units and known enemy positions,
        /// EnemyVisibleSectorBuilder.CoverageBands) hold live own combat units
        /// whose sight lists were sampled, and how many of the enemy-side map
        /// entry points an own unit watches. Size of the enemy plays no part.
        /// </summary>
        public void ObserveCoverage(float nowHours, float[] bandLateralMin, float[] bandLateralMax, bool[] observedBands,
            int bandCount, int watchedEntryPoints, int totalEntryPoints, float frameHeadingDegrees)
        {
            if (!Finite(nowHours) || bandCount <= 0 || bandLateralMin == null || bandLateralMax == null) return;
            if (bandLateralMin.Length < bandCount || bandLateralMax.Length < bandCount) return;
            if (!float.IsNaN(_frameHeading) && AngleDifference(_frameHeading, frameHeadingDegrees) > CoverageFrameChangeDegrees)
                _cellObservedHours.Clear();
            _frameHeading = frameHeadingDegrees;

            if (_bandMin.Length != bandCount)
            {
                _bandMin = new float[bandCount];
                _bandMax = new float[bandCount];
            }
            for (int b = 0; b < bandCount; b++)
            {
                _bandMin[b] = bandLateralMin[b];
                _bandMax[b] = bandLateralMax[b];
                if (observedBands != null && b < observedBands.Length && observedBands[b])
                    StampCells(_bandMin[b], _bandMax[b], nowHours);
            }

            if (totalEntryPoints <= 0) _entryShare = 1f;
            else
            {
                float share = (float)Math.Max(0, watchedEntryPoints) / totalEntryPoints;
                _entryShare = share > 1f ? 1f : share;
            }
            _hasCoverage = true;
        }

        // Cells whose centre lies in [min, max) belong to the span, so the
        // bands of one fit partition the cells exactly. A span narrower than
        // a cell uses the cell holding its midpoint.
        private static bool CellRange(float min, float max, out int first, out int last)
        {
            first = 0; last = -1;
            if (!Finite(min) || !Finite(max) || max < min) return false;
            first = (int)Math.Ceiling(min / CoverageCellMeters - 0.5f);
            last = (int)Math.Ceiling(max / CoverageCellMeters - 0.5f) - 1;
            if (last < first)
            {
                first = last = (int)Math.Floor((min + max) * 0.5f / CoverageCellMeters);
            }
            if (last - first >= MaxCellsPerBand) last = first + MaxCellsPerBand - 1;
            return true;
        }

        private void StampCells(float min, float max, float nowHours)
        {
            if (!CellRange(min, max, out int first, out int last)) return;
            for (int k = first; k <= last; k++) _cellObservedHours[k] = nowHours;
        }

        private float BandRecency(float min, float max, float nowHours)
        {
            if (!CellRange(min, max, out int first, out int last)) return 0f;
            float sum = 0f;
            for (int k = first; k <= last; k++)
                if (_cellObservedHours.TryGetValue(k, out float stamp)) sum += Recency(nowHours - stamp);
            return sum / (last - first + 1);
        }

        private static float AngleDifference(float a, float b)
        {
            float d = Math.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        /// <summary>
        /// The enemy-side map entry points this build (ids, positions) and which an own
        /// unit watches now (BuildEnemyVisibleState's watched set). A point keeps the hour
        /// it was last watched across builds; one never watched reads -1. No allocation
        /// once the arrays fit.
        /// </summary>
        public void ObserveEntryPoints(float nowHours, int[] ids, float[] xs, float[] zs, bool[] watched, int count)
        {
            if (!Finite(nowHours) || ids == null || xs == null || zs == null || watched == null) return;
            if (count > ids.Length) count = ids.Length;
            if (count > xs.Length) count = xs.Length;
            if (count > zs.Length) count = zs.Length;
            if (count > watched.Length) count = watched.Length;
            if (count < 0) count = 0;

            int previous = _entryCount;
            if (_prevIds.Length < previous)
            {
                _prevIds = new int[_entryIds.Length];
                _prevWatched = new float[_entryIds.Length];
            }
            Array.Copy(_entryIds, _prevIds, previous);
            Array.Copy(_entryWatched, _prevWatched, previous);
            if (_entryIds.Length < count)
            {
                int size = Math.Max(count, _entryIds.Length * 2);
                _entryIds = new int[size];
                _entryX = new float[size];
                _entryZ = new float[size];
                _entryWatched = new float[size];
            }

            for (int k = 0; k < count; k++)
            {
                float stamp = -1f;
                for (int p = 0; p < previous; p++)
                {
                    if (_prevIds[p] != ids[k]) continue;
                    stamp = _prevWatched[p];
                    break;
                }
                if (watched[k]) stamp = nowHours;
                _entryIds[k] = ids[k];
                _entryX[k] = xs[k];
                _entryZ[k] = zs[k];
                _entryWatched[k] = stamp;
            }
            _entryCount = count;
        }

        public int EntryPointCount => _entryCount;

        /// <summary>Entry point <paramref name="index"/>: position and the doctrine hour it was last watched (-1 never).</summary>
        public bool TryGetEntryPoint(int index, out float x, out float z, out float lastWatchedHours)
        {
            x = 0f; z = 0f; lastWatchedHours = -1f;
            if (index < 0 || index >= _entryCount) return false;
            x = _entryX[index];
            z = _entryZ[index];
            lastWatchedHours = _entryWatched[index];
            return true;
        }

        /// <summary>Heading of the lateral frame the coverage cells are kept in (NaN before any coverage).</summary>
        public float FrameHeading => _frameHeading;

        /// <summary>
        /// The current coverage band whose ground was observed least recently: its
        /// lateral centre in the ledger's world-anchored frame (x·right + z·right of
        /// <see cref="FrameHeading"/>) and its recency on [0, 1].
        /// </summary>
        public bool TryStalestBand(float nowHours, out float lateralCenter, out float recency)
        {
            lateralCenter = 0f;
            recency = 1f;
            if (!_hasCoverage || _bandMin.Length == 0 || !Finite(nowHours)) return false;
            int best = -1;
            for (int b = 0; b < _bandMin.Length; b++)
            {
                float r = BandRecency(_bandMin[b], _bandMax[b], nowHours);
                if (best >= 0 && r >= recency) continue;
                best = b;
                recency = r;
            }
            if (best < 0) return false;
            lateralCenter = (_bandMin[best] + _bandMax[best]) * 0.5f;
            return true;
        }

        /// <summary>
        /// One build's sighting from the snapshot: every non-reinforcement observation, visible
        /// at full strength and remembered weighted by confidence, at the snapshot's doctrine
        /// hours. Only a visible enemy (or fire) counts as contact.
        /// </summary>
        public void Observe(BattleSnapshot snapshot, float engagedEnemyStrength)
        {
            if (snapshot == null) return;
            float visible = 0f, remembered = 0f;
            EnemyObservation[] observations = snapshot.Observations;
            for (int i = 0; i < snapshot.ObservationCount; i++)
            {
                if (observations[i].IsReinforcement) continue;
                if (observations[i].State == ObservationState.Visible) visible += observations[i].Strength;
                else remembered += observations[i].Strength * observations[i].Confidence01;
            }
            Observe((float)snapshot.DoctrineHours, visible, engagedEnemyStrength, remembered);
        }

        public void Observe(float nowHours, float visibleEnemyStrength, float engagedEnemyStrength, float rememberedEnemyStrength = 0f)
        {
            if (!Finite(nowHours)) return;
            if (_lastObserveHours >= 0f && nowHours < _lastObserveHours) Reset();

            float known = Sanitize(visibleEnemyStrength) + Sanitize(rememberedEnemyStrength);
            float visible = Sanitize(visibleEnemyStrength);
            if (_lastObserveHours >= 0f && _largestSeen > 0f)
            {
                float dt = nowHours - _lastObserveHours;
                float keep = 1f - LargestSeenDecayPerHour * dt;
                if (keep < 0f) keep = 0f;
                _largestSeen *= keep;
            }
            if (known > _largestSeen) _largestSeen = known;
            if (visible > 0f || Sanitize(engagedEnemyStrength) > 0f) _lastContactHours = nowHours;
            _lastObserveHours = nowHours;
        }

        /// <summary>
        /// Share of the known enemy force already in fire range of our line.
        /// The unseen remainder is the best available proxy for his reserve.
        /// </summary>
        public float ReserveCommitFraction(float engagedEnemyStrength)
        {
            if (_largestSeen <= 0f) return UnknownReserveCommitFraction;
            float f = Sanitize(engagedEnemyStrength) / _largestSeen;
            if (f > 1f) f = 1f;
            // Never emit the exact sentinel for a known value.
            if (Math.Abs(f - UnknownReserveCommitFraction) <= 0.0001f) f += 0.001f;
            return f;
        }

        public bool ContactBroken(float nowHours, float visibleEnemyStrength)
        {
            if (!HadContact || !Finite(nowHours)) return false;
            if (Sanitize(visibleEnemyStrength) > 0f) return false;
            float since = nowHours - _lastContactHours;
            return since >= ContactBrokenAfterHours && since <= ContactMemoryHours;
        }

        /// <summary>
        /// Intel confidence on [0, 1] in the current estimate:
        /// coverage × contact recency, independent of how large the enemy is.
        /// Coverage = mean over the current bands of the observation recency of
        /// the ground each spans (lateral cells; ground never observed, or not
        /// for <see cref="ContactMemoryHours"/>, counts 0) × the entry factor
        /// (<see cref="UnwatchedEntryPointWeight"/> + the rest × the watched
        /// share of enemy-side entry points; 1 when there are none). Contact
        /// recency fades the estimate itself: an old sighting carries no
        /// confidence however fully the front is watched now.
        /// </summary>
        public float Confidence(float nowHours)
        {
            if (!_hasCoverage || !HadContact || _largestSeen <= 0f || !Finite(nowHours)) return 0f;
            float bands = 0f;
            for (int i = 0; i < _bandMin.Length; i++) bands += BandRecency(_bandMin[i], _bandMax[i], nowHours);
            float bandTerm = _bandMin.Length == 0 ? 0f : bands / _bandMin.Length;
            float entryFactor = UnwatchedEntryPointWeight + (1f - UnwatchedEntryPointWeight) * _entryShare;
            return bandTerm * entryFactor * Recency(nowHours - _lastContactHours);
        }

        private static float Recency(float sinceHours)
        {
            float r = 1f - sinceHours / ContactMemoryHours;
            if (r < 0f) return 0f;
            return r > 1f ? 1f : r;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static float Sanitize(float v) => !Finite(v) || v < 0f ? 0f : v;
    }
}
