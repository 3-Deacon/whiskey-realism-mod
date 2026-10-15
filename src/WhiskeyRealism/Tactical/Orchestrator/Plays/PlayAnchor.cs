using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    /// <summary>
    /// Pure inputs of <see cref="PlayExecutorRuntime"/> from the battle snapshot
    /// (plan BC5a, spec §2): the own centroid and command-level positions come from
    /// the snapshot's own records, the enemy anchor from the enemies this side
    /// actually sees — never every enemy's true position — and the recompute gate
    /// runs on the doctrine clock (battle hours, frozen across vanilla's night
    /// end-of-day cycle) instead of Environment.TickCount.
    /// </summary>
    public static class PlayAnchor
    {
        /// <summary>The former 3 real seconds at 1x, on the doctrine clock.</summary>
        public static readonly double MinRecomputeHours = TacticalBattleClock.RealSecondsAtOneXToHours(3f);

        /// <summary>
        /// True when the cached assignment must be recomputed: no cache, another
        /// plan, the window elapsed, or the clock went back (reload, new battle).
        /// A clock frozen overnight keeps the cache.
        /// </summary>
        public static bool RecomputeDue(bool hasCache, bool samePlan, double lastHours, double nowHours)
        {
            if (!hasCache || !samePlan) return true;
            double age = nowHours - lastHours;
            return age < 0.0 || age >= MinRecomputeHours;
        }

        /// <summary>
        /// The point the play faces: the strength-weighted centroid of the visible
        /// on-map enemies; with nothing visible, the side's front axis projected
        /// its target distance from the army; with no axis either, straight ahead
        /// along +z (the old fallback).
        /// </summary>
        public static void EnemyAnchor(IBattleUnitView s, TacticalFrontAxis axis, float armyX, float armyZ, out float x, out float z)
        {
            if (TryVisibleEnemyCentroid(s, out x, out z)) return;
            float engagement = axis.IsValid && axis.TargetDistance >= 1f
                ? axis.TargetDistance
                : PlayProjection.DefaultEngagementDistanceMeters;
            x = armyX + (axis.IsValid ? axis.ForwardX : 0f) * engagement;
            z = armyZ + (axis.IsValid ? axis.ForwardZ : 1f) * engagement;
        }

        private static bool TryVisibleEnemyCentroid(IBattleUnitView s, out float x, out float z)
        {
            x = 0f;
            z = 0f;
            if (s == null) return false;
            EnemyObservation[] obs = s.Observations;
            float weight = 0f, sumX = 0f, sumZ = 0f;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].State != ObservationState.Visible || obs[k].IsReinforcement) continue;
                float w = obs[k].Strength > 0f ? obs[k].Strength : 1f;
                weight += w;
                sumX += obs[k].X * w;
                sumZ += obs[k].Z * w;
            }
            if (weight <= 0f) return false;
            x = sumX / weight;
            z = sumZ / weight;
            return true;
        }

        /// <summary>
        /// Fills the own-centroid samples (every active own unit) and the
        /// command-level candidates with DirectChildDiscovery's "child-&lt;id&gt;" ids. A candidate is a unit at or above
        /// <paramref name="commandMin"/> that is one of the side's direct children (<paramref name="directChildIds"/>,
        /// BC11): the army HQ and grandchild groups used to be offered too, so a rect could go to a unit the
        /// allocator never matches (zoneByChildId). Returns the candidate count.
        /// </summary>
        public static int CollectOwn(IBattleUnitView s, int commandMin, List<float> xs, List<float> zs, List<CorpsPosition> corps,
            ISet<string> directChildIds)
        {
            if (s == null || xs == null || zs == null || corps == null || directChildIds == null) return 0;
            UnitRecord[] own = s.Own;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if ((own[i].Flags & UnitFlags.Active) == 0) continue;
                float px = own[i].X;
                float pz = own[i].Z;
                if (float.IsNaN(px) || float.IsInfinity(px) || float.IsNaN(pz) || float.IsInfinity(pz)) continue;
                xs.Add(px);
                zs.Add(pz);
                if (own[i].UnitTyp < commandMin) continue;
                string childId = "child-" + own[i].InstanceId;
                if (directChildIds.Contains(childId)) corps.Add(new CorpsPosition(childId, px, pz));
            }
            return corps.Count;
        }
    }
}
