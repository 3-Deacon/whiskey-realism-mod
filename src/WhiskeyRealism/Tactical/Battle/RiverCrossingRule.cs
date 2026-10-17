using System;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>The side's picture of the map's river crossings (BC14): the land regions, the links between them, and how it knows each.</summary>
    public interface ICrossingPicture
    {
        bool Ready { get; }
        /// <summary>The land region of a point; -1 on open river, on a crossing block, off the map or before the picture is ready.</summary>
        int RegionAt(float x, float z);
        int LinkCount { get; }
        bool LinkTouches(int link, int region);
        void LinkPoint(int link, out float x, out float z);
        CrossingState StateOf(int link);
    }

    public enum CrossingVerdict
    {
        /// <summary>The leg stays on one bank (or the picture cannot say): nothing to route.</summary>
        SameBank,
        /// <summary>The leg crosses a river and the side knows a crossing for it: go by the crossing point.</summary>
        ViaCrossing,
        /// <summary>The leg crosses a river and the side knows no crossing it may use: no move.</summary>
        NoKnownCrossing,
    }

    /// <summary>
    /// A move whose straight leg crosses a river line takes a crossing the side knows, or is not made (smoke 2026-10-17: posture
    /// Screen and Probe moves and division-play moves were checked for water at their target and, for a withdrawal, along the
    /// line, never for the river they cross). The legs are the BC14 picture's: the start and the target lie in different land
    /// regions (RiverRegions.RegionAt, open river and crossing blocks are not land), and only a link joining both regions can
    /// carry the move. A link is known when the side perceives it Clear or Watched (an enemy near it that does not cover it);
    /// Unknown and Held are not. Of the known links the one with the shortest start-link-target detour is the via.
    /// </summary>
    public static class RiverCrossingRule
    {
        public static CrossingVerdict Decide(ICrossingPicture picture, float x0, float z0, float x1, float z1, out float viaX, out float viaZ)
        {
            viaX = viaZ = 0f;
            if (picture == null || !picture.Ready) return CrossingVerdict.SameBank;
            int from = picture.RegionAt(x0, z0);
            int to = picture.RegionAt(x1, z1);
            if (from < 0 || to < 0 || from == to) return CrossingVerdict.SameBank;

            float best = float.MaxValue;
            bool found = false;
            for (int link = 0; link < picture.LinkCount; link++)
            {
                if (!picture.LinkTouches(link, from) || !picture.LinkTouches(link, to)) continue;
                CrossingState state = picture.StateOf(link);
                if (state != CrossingState.Clear && state != CrossingState.Watched) continue;
                picture.LinkPoint(link, out float lx, out float lz);
                float detour = Distance(x0, z0, lx, lz) + Distance(lx, lz, x1, z1);
                if (detour >= best) continue;
                best = detour;
                viaX = lx;
                viaZ = lz;
                found = true;
            }
            return found ? CrossingVerdict.ViaCrossing : CrossingVerdict.NoKnownCrossing;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>The executor's seam to the picture: one answer per planned Move (the runtime reads the side's own crossing watch).</summary>
    public interface ICrossingGate
    {
        CrossingVerdict Check(int alliance, float x0, float z0, float x1, float z1, out float viaX, out float viaZ);
    }
}
