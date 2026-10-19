using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Strategic.Planner
{
    // The covering corridor between the two capitals (foresight F6b-1, D11 requirements covering-post and covering-size's axis shares;
    // decision 4): where a force that covers the road between them would stand, and which sectors hold against enemy elements on that road.
    // Pure geometry over the snapshot and the published picture; it takes no formation. The covering force proper (its need, members, verdict
    // and lifecycle) is F6b-2's, built over these functions.
    //
    // The axis is the segment own capital -> enemy capital. Two points on it come from vanilla's own capital-defence radius, the relocation
    // range R (AssignUnitToDefendCapital counts an enemy only inside it, 11719-11727): the forward point at R from the enemy capital, the
    // nearest the force can stand without entering the enemy capital's defence radius, and the rear point at R from the own capital, the edge
    // of the radius inside which vanilla's own capital defence already acts. Each post is the own-held town (BattleUnits.towns, Town.Owner;
    // vanilla's offensive aims at towns first, GetClosestObjectivePosOfArea 14587-14632, so a town is where an advance on the capital is met)
    // nearest its point within R of it, by straight distance, the lower place index on a tie. Small towns (BattleUnits.smalltowns), rail
    // lines and any distance fitted to a town name are no part of the rule: the posts are whatever it gives.
    internal static class CoveringForce
    {
        // Both points clamp to the segment, so an axis under R long puts the forward point at the own capital and the rear point at the
        // enemy capital. An axis between R and 2R puts the forward point nearer the own capital than the rear point (002/A: 189 against 200
        // from Richmond), accepted. Precondition: both capitals exist (s.HasCapital and s.HasEnemyCapital).
        internal static void Points(CampaignSnapshot s, out float fx, out float fz, out float rx, out float rz)
        {
            float dx = s.EnemyCapitalX - s.CapitalX, dz = s.EnemyCapitalZ - s.CapitalZ;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length <= 0f)
            {
                fx = rx = s.CapitalX;
                fz = rz = s.CapitalZ;
                return;
            }
            float forward = SnapshotRules.Clamp01((length - s.RelocationRange) / length);
            float rear = SnapshotRules.Clamp01(s.RelocationRange / length);
            fx = s.CapitalX + dx * forward;
            fz = s.CapitalZ + dz * forward;
            rx = s.CapitalX + dx * rear;
            rz = s.CapitalZ + dz * rear;
        }

        // The own-held town nearest (x, z) within R of it, as an index into s.Places; -1 when there is none. Neither capital is a post (a town
        // within TheaterPlanner.SameTargetRadius of either is the capital), "within" is <=, a tie goes to the lower place index.
        internal static int PostNear(CampaignSnapshot s, float x, float z)
        {
            float reachSq = s.RelocationRange * s.RelocationRange;
            int best = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < s.Places.Count; i++)
            {
                var place = s.Places[i];
                if (place.Owner != s.Alliance || IsCapital(s, in place)) continue;
                float distanceSq = ReserveRules.DistanceSq(place.X, place.Z, x, z);
                if (distanceSq > reachSq || distanceSq >= bestSq) continue;
                bestSq = distanceSq;
                best = i;
            }
            return best;
        }

        private static bool IsCapital(CampaignSnapshot s, in PlaceRecord place)
        {
            return (s.HasCapital && TheaterPlanner.SamePoint(place.X, place.Z, s.CapitalX, s.CapitalZ))
                || (s.HasEnemyCapital && TheaterPlanner.SamePoint(place.X, place.Z, s.EnemyCapitalX, s.EnemyCapitalZ));
        }

        // A sector's axis share: the part of its hold need that is held against enemy elements on the axis, min(HoldNeed, holdRatio x the land
        // elements of its need whose position lies within R of the axis segment). Its believed part is the shipped LandThreatNear of
        // requirement-hold-need restricted to beliefs within R of the axis (a seen army near two axis sectors counts in both shares, which
        // loosens the cap; no sector gives more than its own share), its presumed part the Faded and Anchor elements the picture assigned to the
        // sector, each at the weight the assignment gave it. The sectors with a share above 0 are the axis sectors; a sector holding against an
        // army off the axis (the Peninsula's Fort Monroe) has none, however near its centroid lies. into (one per sector) is overwritten.
        internal static void AxisShares(CampaignSnapshot s, SectorModel sectors, EnemyPicture picture, float[] holdNeed, float holdRatio,
                                        ITerrainLineProbe probe, float[] into)
        {
            for (int k = 0; k < into.Length; k++) into[k] = 0f;
            if (!s.HasCapital || !s.HasEnemyCapital || picture == null) return;
            float band = s.RelocationRange;
            for (int k = 0; k < sectors.Count && k < into.Length && k < holdNeed.Length; k++)
            {
                if (holdNeed[k] <= 0f) continue;
                sectors.Centroid(k, out float cx, out float cz);
                float reach = sectors.Radius(k) + band;
                float onAxis = 0f;
                for (int i = 0; i < s.Beliefs.Count; i++)
                {
                    var b = s.Beliefs[i];
                    float live = ReserveRules.Live(b);
                    if (live <= 0f || ReserveRules.Afloat(b) || RiverLines.Distance(b.X, b.Z, cx, cz) > reach) continue;
                    if (TheaterPlanner.SegmentDistance(b.X, b.Z, s.CapitalX, s.CapitalZ, s.EnemyCapitalX, s.EnemyCapitalZ) > band) continue;
                    float effective = RiverLines.EffectiveDistance(b.X, b.Z, cx, cz, probe, s.Crossings);
                    onAxis += live * (effective > reach ? reach / effective : 1f);
                }
                for (int i = 0; i < picture.Elements.Count; i++)
                {
                    var e = picture.Elements[i];
                    if (e.AssignedSector != k || (e.Kind != PictureElementKind.Faded && e.Kind != PictureElementKind.Anchor)) continue;
                    if (TheaterPlanner.SegmentDistance(e.X, e.Z, s.CapitalX, s.CapitalZ, s.EnemyCapitalX, s.EnemyCapitalZ) > band) continue;
                    onAxis += e.Mass * e.Weight;
                }
                into[k] = Math.Min(holdNeed[k], holdRatio * onAxis);
            }
        }

        // "[CoveringCorridor]" (State, first review and on change; the runtime dates it): every own-held town within R of either point as
        // "name:dForward:dRear:dOwn:dEnemy" (whole distances, straight; to the forward and rear points and to the own and the enemy capital),
        // joined by ';' and quoted as one value, no length cap; the forward and rear posts by name ("none" with no town); and the axis sectors.
        // Null when the side or the enemy has no capital: no axis, no corridor. The posts are whatever the rule gives; nothing is tuned to a name.
        internal static string CorridorRow(CampaignSnapshot s, SectorModel sectors, EnemyPicture picture, TheaterPlan plan, ITerrainLineProbe probe)
        {
            if (!s.HasCapital || !s.HasEnemyCapital) return null;
            var inv = CultureInfo.InvariantCulture;
            Points(s, out float fx, out float fz, out float rx, out float rz);
            float reachSq = s.RelocationRange * s.RelocationRange;
            var candidates = new StringBuilder();
            for (int i = 0; i < s.Places.Count; i++)
            {
                var place = s.Places[i];
                if (place.Owner != s.Alliance || IsCapital(s, in place)) continue;
                if (ReserveRules.DistanceSq(place.X, place.Z, fx, fz) > reachSq && ReserveRules.DistanceSq(place.X, place.Z, rx, rz) > reachSq) continue;
                if (candidates.Length > 0) candidates.Append(';');
                candidates.Append(place.Name).Append(':')
                    .Append(RiverLines.Distance(place.X, place.Z, fx, fz).ToString("0", inv)).Append(':')
                    .Append(RiverLines.Distance(place.X, place.Z, rx, rz).ToString("0", inv)).Append(':')
                    .Append(RiverLines.Distance(place.X, place.Z, s.CapitalX, s.CapitalZ).ToString("0", inv)).Append(':')
                    .Append(RiverLines.Distance(place.X, place.Z, s.EnemyCapitalX, s.EnemyCapitalZ).ToString("0", inv));
            }
            var need = new float[sectors.Count];
            for (int k = 0; k < plan.Sectors.Count; k++)
            {
                var sp = plan.Sectors[k];
                if (sp != null && sp.SectorId >= 0 && sp.SectorId < need.Length) need[sp.SectorId] = sp.HoldNeed;
            }
            var share = new float[sectors.Count];
            AxisShares(s, sectors, picture, need, TheaterPlanner.HoldRatioNow(s), probe, share);
            var axis = new StringBuilder();
            for (int k = 0; k < share.Length; k++)
            {
                if (share[k] <= 0f) continue;
                if (axis.Length > 0) axis.Append(',');
                axis.Append(k);
            }
            return "[CoveringCorridor] alliance=" + s.Alliance
                + " candidates=" + TelemetryLegacyParser.Quote(candidates.ToString())
                + " forward=" + NameOf(s, PostNear(s, fx, fz)) + " rear=" + NameOf(s, PostNear(s, rx, rz))
                + " axis=" + (axis.Length == 0 ? "none" : axis.ToString());
        }

        private static string NameOf(CampaignSnapshot s, int place)
        {
            return place < 0 ? "none" : TelemetryLegacyParser.Quote(s.Places[place].Name);
        }
    }
}
