using System;
using UnityEngine;

namespace WhiskeyRealism.Tactical.Battle
{
    // BC13 (spec § 5, requirement-perceived-ground): the runtime oracle of "does this side perceive that ground". It answers
    // the same question the snapshot's own probe asked of each enemy (BattlePerceptionProbe), for a point where no enemy is
    // known to stand.
    //
    //  - FogGrid: vanilla's own call on the side's own fog grid, as if an infantry regiment stood there:
    //    fow.UnitIsHidden(point, unittype: 0). The grid index reads only x and z (WorldPositionToFogEntry 101139-101168),
    //    and UnitIsHidden tests PositionIsWithinFog, then the terrain concealment at the point minus the infantry spotting
    //    factor (101179-101229). No cunning, strength, cover-object or perk term enters, because no known unit stands
    //    there, so a reverse slope out of sight, or a wood that conceals, is unperceived. The calls run inside the
    //    BUG-TAC-020 incident mask (W&L intelligence incidents 60, 61 and 64 make UnitIsHidden answer "visible" for every
    //    position, 101183-101186): the Handle applies it once for the whole block of probes and restores it on dispose.
    //  - OwnSideWithinFog (the player-alliance AI side): the same call on the player-alliance grid vanilla reads for that
    //    side (unit.fow is fows[0] for the player alliance, CreateFOWObjects 116505-116518), with no mask, as for the snapshot.
    //  - RangeOnly: a point within an own unit's list reach (BattlePerception.RangeOnlyReach).
    //
    // A throw reads as unperceived. Main thread only.
    internal static class PerceivedGroundRuntime
    {
        private sealed class FogGround : IPerceivedGround
        {
            internal FogOfWar Grid;

            public bool IsPerceived(float x, float z)
            {
                try
                {
                    return (UnityEngine.Object)Grid != null && !Grid.UnitIsHidden(new Vector3(x, 0f, z), unittype: 0);
                }
                catch
                {
                    return false;
                }
            }
        }

        private static readonly FogGround[] Fog = { new FogGround(), new FogGround() };
        private static readonly RangeOnlyPerceivedGround[] Range = { new RangeOnlyPerceivedGround(), new RangeOnlyPerceivedGround() };

        static PerceivedGroundRuntime()
        {
            WhiskeyRealism.Util.BattleScope.Register("perceived-ground-runtime", Reset);
        }

        // A new battle: the sides' fog-grid references and RangeOnly reach arrays belong to the battle that ended.
        private static void Reset()
        {
            for (int i = 0; i < Fog.Length; i++) Fog[i].Grid = null;
            for (int i = 0; i < Range.Length; i++) Range[i].SetReach(new float[0], new float[0], new float[0], 0);
        }

        internal readonly struct Handle : IDisposable
        {
            internal readonly IPerceivedGround Oracle;
            private readonly BattlePerceptionProbe.IncidentMaskScope _mask;

            internal Handle(IPerceivedGround oracle, BattlePerceptionProbe.IncidentMaskScope mask)
            {
                Oracle = oracle;
                _mask = mask;
            }

            public void Dispose() => _mask.Dispose();
        }

        /// <summary>The oracle for the side of this snapshot, in the perception mode the snapshot was built in. Dispose after the probes.</summary>
        internal static Handle Open(BattleSnapshot s)
        {
            int alliance = s != null ? s.Alliance : -1;
            if (alliance < 0 || alliance >= Fog.Length) return new Handle(Range[0], default);
            PerceptionMode mode = s.Perception;
            switch (mode)
            {
                case PerceptionMode.FogGrid:
                {
                    Fog[alliance].Grid = BattlePerceptionProbe.GridOf(s);
                    return new Handle(Fog[alliance], BattlePerceptionProbe.WithIntelIncidentMask(mode));
                }
                case PerceptionMode.OwnSideWithinFog:
                    Fog[alliance].Grid = BattlePerceptionProbe.GridOf(s);
                    return new Handle(Fog[alliance], default);
                default:
                    BattlePerceptionProbe.FillRangeOnlyGround(alliance, Range[alliance]);
                    return new Handle(Range[alliance], default);
            }
        }
    }
}
