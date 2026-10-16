using System;

namespace WhiskeyRealism.Strategic.Planner
{
    // Scores one predefined railroad line for the steering Prefix (plan G5c, spec C11). A line is judged by what it connects: its
    // two farthest-apart IIPs (RailLineRecord.AX..BZ, AIipId/BIipId; ConstructionSymbol.IIPsForTransportCapacityUpdate 188134), not
    // by its average point, which is the same for a line that links supply to the front and for one that ends nowhere useful.
    // Each term is in [0, 1] and the weights sum to 1:
    //   own supply   the share of ends that are an own supply source (LogisticsGate.Supplies: an IIP the alliance owns, visible);
    //   axis         the mean closeness of the ends to the main axis (the segment from the main-effort mass to the main target),
    //                1 on the axis, 0 a relocation range or more away;
    //   threat       the best end's sector is a Hold or Screen sector (a sector with believed enemy on it, TheaterPlanner posture);
    //   reach gain   a line from an own supply source to a non-source end brings supply nearer the main target by the fraction of
    //                the source's distance to it that the far end gains (zero for a line that points away).
    // Without a main effort the axis and reach terms are zero; the own supply and threat terms still rank the lines.
    public static class RailLineScorer
    {
        public const float OwnSupplyWeight = 0.35f;
        public const float AxisWeight = 0.30f;
        public const float ThreatWeight = 0.15f;
        public const float ReachGainWeight = 0.20f;

        public static float Score(in RailLineRecord r, CampaignSnapshot s, TheaterPlan plan, SectorModel sectors)
        {
            if (s == null) return 0f;
            bool hasA = r.AIipId >= 0, hasB = r.BIipId >= 0;
            if (!hasA && !hasB) return 0f;
            bool ownA = hasA && IsOwnSource(s, r.AIipId), ownB = hasB && IsOwnSource(s, r.BIipId);
            int ends = (hasA ? 1 : 0) + (hasB ? 1 : 0);
            float own = ((ownA ? 1 : 0) + (ownB ? 1 : 0)) / (float)ends;

            float axis = 0f, gain = 0f;
            if (MainAxis(s, plan, out float fromX, out float fromZ, out float toX, out float toZ))
            {
                float reach = s.RelocationRange;
                float closeness = 0f;
                if (hasA) closeness += Closeness(r.AX, r.AZ, fromX, fromZ, toX, toZ, reach);
                if (hasB) closeness += Closeness(r.BX, r.BZ, fromX, fromZ, toX, toZ, reach);
                axis = closeness / ends;
                if (hasA && hasB && ownA != ownB)
                {
                    float sx = ownA ? r.AX : r.BX, sz = ownA ? r.AZ : r.BZ;
                    float ex = ownA ? r.BX : r.AX, ez = ownA ? r.BZ : r.AZ;
                    float fromSource = Distance(sx, sz, toX, toZ);
                    gain = Clamp01((fromSource - Distance(ex, ez, toX, toZ)) / Math.Max(1f, fromSource));
                }
            }

            float threat = 0f;
            if (plan != null && sectors != null)
            {
                if (hasA && Threatened(plan, sectors, r.AX, r.AZ)) threat = 1f;
                if (hasB && Threatened(plan, sectors, r.BX, r.BZ)) threat = 1f;
            }
            return OwnSupplyWeight * own + AxisWeight * axis + ThreatWeight * threat + ReachGainWeight * gain;
        }

        private static bool IsOwnSource(CampaignSnapshot s, int iipId)
        {
            for (int i = 0; i < s.Supply.Count; i++)
            {
                var source = s.Supply[i];
                if (source.Kind == SupplySourceKind.Iip && source.Id == iipId) return LogisticsGate.Supplies(s.Alliance, source);
            }
            return false;
        }

        // The main axis: from the mean position of the planned mass (the capital when none of it is in the snapshot) to the main
        // sector's target.
        internal static bool MainAxis(CampaignSnapshot s, TheaterPlan plan, out float fromX, out float fromZ, out float toX, out float toZ)
        {
            fromX = fromZ = toX = toZ = 0f;
            if (plan == null || plan.MainSectorId < 0) return false;
            SectorPlan main = null;
            for (int k = 0; k < plan.Sectors.Count; k++)
                if (plan.Sectors[k] != null && plan.Sectors[k].SectorId == plan.MainSectorId && plan.Sectors[k].Posture == TheaterPosture.MainEffort)
                    main = plan.Sectors[k];
            if (main == null) return false;
            toX = main.TargetX;
            toZ = main.TargetZ;
            int counted = 0;
            float sumX = 0f, sumZ = 0f;
            for (int k = 0; k < plan.MassInstanceIds.Count; k++)
            {
                int i = s.IndexOfOwn(plan.MassInstanceIds[k]);
                if (i < 0) continue;
                sumX += s.Own[i].X;
                sumZ += s.Own[i].Z;
                counted++;
            }
            if (counted > 0)
            {
                fromX = sumX / counted;
                fromZ = sumZ / counted;
                return true;
            }
            if (!s.HasCapital) return false;
            fromX = s.CapitalX;
            fromZ = s.CapitalZ;
            return true;
        }

        // 1 on the segment, falling to 0 at `reach` away from it.
        internal static float Closeness(float x, float z, float ax, float az, float bx, float bz, float reach)
        {
            if (reach <= 0f) return 0f;
            return 1f - Math.Min(1f, DistanceToSegment(x, z, ax, az, bx, bz) / reach);
        }

        internal static float DistanceToSegment(float x, float z, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az;
            float lengthSq = dx * dx + dz * dz;
            float t = lengthSq <= 0f ? 0f : Clamp01(((x - ax) * dx + (z - az) * dz) / lengthSq);
            return Distance(x, z, ax + dx * t, az + dz * t);
        }

        // A Hold or Screen sector is one with believed enemy strength on it (TheaterPlanner.Plan sets those postures only when the
        // sector's threat is above zero).
        internal static bool Threatened(TheaterPlan plan, SectorModel sectors, float x, float z)
        {
            int sector = sectors.SectorAt(x, z);
            if (sector < 0) return false;
            for (int k = 0; k < plan.Sectors.Count; k++)
            {
                var p = plan.Sectors[k];
                if (p != null && p.SectorId == sector && (p.Posture == TheaterPosture.Hold || p.Posture == TheaterPosture.Screen)) return true;
            }
            return false;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }
    }
}
