using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The vanilla campaign speed terms the march-time estimate needs (plan P6 chain without the
    // bound's maxima; G0 landing B), read once per review by TerrainLineRuntime.ReadMarchSpeed.
    public struct MarchSpeedInputs
    {
        public float[] MountedBaseSpeed;  // GamePrefs.unitbasicspeed[t, 1] by unittyp (groups always use the mounted column, 133350-133354)
        public float BasicUnitSpeed;      // GamePrefs.basicunitspeed (133535)
        public float CampaignSpeedFactor; // GamePrefs.generalcampaignunitspeedfactor (134083; campaignprefs 215-216)
        public float WalkCost;            // terrainspeccost[0]
        public float RiverCost;           // terrainspeccost[4] (Rivers)
        public float RoadCost;            // terrainspeccost[7] (Roads; a land unit off the train pays it on rails too, 132888-132900)
        public float RailCost;            // terrainspeccost[9] (Railroad static)
        public float RailroadSpeedAdj;    // alliance railroadspeedadj (213217), on a train only (133401-133404)
        public float TrainCapacityFree;   // alliance transportcapacity[0] - transportcapacityused[0]
        public float TrainCapacityPerMan; // GamePrefs.usedtransportcapacitypermenstrength[0]
    }

    // The planner's march-time estimate (G0 landing B): XZ distance over vanilla's campaign speed
    // num33 = dt x num11 x ... x generalcampaignunitspeedfactor (134100), with
    // num11 = unitbasicspeed[t, 1] (133350-133354) x basicunitspeed / terrain cost (133535,
    // GetTerrainSpeedAdjustment 132888-132900) x groupspeed (133991), dt in floating years
    // (campaign time 30171), so a day moves num33 / 365.25. Roads are the land rate (cost
    // terrainspeccost[7]); a river or rail corridor is credited at its cost-1 rate:
    //   river: the formation uses rivers (Regiment.userivers) and a river cell lies within its
    //     commanderrange from its own position onward along the line (sampled every
    //     commanderrange, at most 16 samples): the leading run of such samples is the river leg
    //     (terrainspeccost[4]);
    //   rail: the formation uses rails (Regiment.userails), a completed line whose two endpoint
    //     IIPs are own-owned has an endpoint within commanderrange of the formation and its other
    //     endpoint nearer the target, and the alliance's free train capacity covers the formation
    //     (vanilla's entrain test 116434-116441: free >= usedtransportcapacitypermenstrength[0] x
    //     strength / railroadspeedadj / terrainspeccost[0] x terrainspeccost[9]); the rail leg is
    //     the distance the line gains toward the target, at x railroadspeedadj / terrainspeccost[9].
    // The two cost-1 modes exclude each other (a unit is on a train or afloat, P6): the larger
    // time saving is taken. Everything that only slows a unit (weather, readiness, support,
    // wounded, the night stop) is left out: the estimate ranks candidates, it does not schedule.
    public static class MarchTime
    {
        public const float DaysPerYear = 365.25f;
        public const float RiverSampleFraction = 1f; // of commanderrange
        public const int MaxRiverSamples = 16;

        // Map units per game day at a terrain cost (the land rate is RoadCost).
        public static float SpeedPerDay(in MarchSpeedInputs m, int unitTyp, float groupSpeed, float terrainCost)
        {
            float baseSpeed = m.MountedBaseSpeed != null && unitTyp >= 0 && unitTyp < m.MountedBaseSpeed.Length
                ? m.MountedBaseSpeed[unitTyp] : 0f;
            if (baseSpeed <= 0f || groupSpeed <= 0f || terrainCost <= 0f) return 0f;
            return baseSpeed * groupSpeed * m.BasicUnitSpeed * m.CampaignSpeedFactor / terrainCost / DaysPerYear;
        }

        public static bool TrainCovers(in MarchSpeedInputs m, float strength)
        {
            if (m.RailroadSpeedAdj <= 0f || m.WalkCost <= 0f) return false;
            float need = m.TrainCapacityPerMan * Math.Max(0f, strength) / m.RailroadSpeedAdj / m.WalkCost * m.RailCost;
            return m.TrainCapacityFree - need >= 0f;
        }

        // Days for the formation to cover effectiveDistance toward (toX, toZ); float.MaxValue
        // when the inputs give no speed.
        public static float EstimateDays(in MarchSpeedInputs m, in FormationRecord f, float toX, float toZ, float effectiveDistance,
                                         CampaignSnapshot s, ITerrainLineProbe probe)
        {
            float road = SpeedPerDay(m, f.UnitTyp, f.GroupSpeed, m.RoadCost);
            if (road <= 0f) return float.MaxValue;
            float distance = Math.Max(0f, effectiveDistance);
            float roadDays = distance / road;

            float bestDays = roadDays;
            if (f.UseRivers && probe != null)
            {
                float river = SpeedPerDay(m, f.UnitTyp, f.GroupSpeed, m.RiverCost);
                float leg = Math.Min(distance, RiverLeg(f, toX, toZ, probe));
                if (river > 0f && leg > 0f) bestDays = Math.Min(bestDays, (distance - leg) / road + leg / river);
            }
            if (f.UseRails && s != null && TrainCovers(m, f.Strength))
            {
                float rail = SpeedPerDay(m, f.UnitTyp, f.GroupSpeed, m.RailCost) * m.RailroadSpeedAdj;
                float leg = Math.Min(distance, RailLeg(s, f, toX, toZ));
                if (rail > 0f && leg > 0f) bestDays = Math.Min(bestDays, (distance - leg) / road + leg / rail);
            }
            return bestDays;
        }

        // The leading run of line samples, from the formation's own position, that have a river
        // cell within commanderrange.
        internal static float RiverLeg(in FormationRecord f, float toX, float toZ, ITerrainLineProbe probe)
        {
            float range = Math.Max(0f, f.CommandRange);
            if (range <= 0f) return 0f;
            float length = RiverLines.Distance(f.X, f.Z, toX, toZ);
            if (length <= 0f) return 0f;
            float spacing = range * RiverSampleFraction;
            int samples = Math.Min(MaxRiverSamples, (int)Math.Ceiling(length / spacing));
            float covered = 0f;
            for (int k = 0; k <= samples; k++)
            {
                float t = Math.Min(1f, k * spacing / length);
                float px = f.X + (toX - f.X) * t;
                float pz = f.Z + (toZ - f.Z) * t;
                if (!probe.RiverNear(px, pz, range)) break;
                covered = t * length;
                if (t >= 1f) break;
            }
            return covered;
        }

        // The best completed own line with one endpoint within commanderrange: the distance its far
        // endpoint gains toward the target, capped at the line's own length.
        internal static float RailLeg(CampaignSnapshot s, in FormationRecord f, float toX, float toZ)
        {
            float range = Math.Max(0f, f.CommandRange);
            float fromTarget = RiverLines.Distance(f.X, f.Z, toX, toZ);
            float best = 0f;
            HashSet<int> own = null;
            for (int i = 0; i < s.Rails.Count; i++)
            {
                var line = s.Rails[i];
                if (line.Progress < 1f) continue;
                if (own == null) own = OwnIips(s);
                if (!own.Contains(line.AIipId) || !own.Contains(line.BIipId)) continue;
                float lineLength = RiverLines.Distance(line.AX, line.AZ, line.BX, line.BZ);
                best = Math.Max(best, Gain(f, range, fromTarget, line.AX, line.AZ, line.BX, line.BZ, toX, toZ, lineLength));
                best = Math.Max(best, Gain(f, range, fromTarget, line.BX, line.BZ, line.AX, line.AZ, toX, toZ, lineLength));
            }
            return best;
        }

        private static float Gain(in FormationRecord f, float range, float fromTarget, float nearX, float nearZ, float farX, float farZ,
                                  float toX, float toZ, float lineLength)
        {
            if (RiverLines.Distance(f.X, f.Z, nearX, nearZ) > range) return 0f;
            float gain = fromTarget - RiverLines.Distance(farX, farZ, toX, toZ);
            return gain > 0f ? Math.Min(gain, lineLength) : 0f;
        }

        private static HashSet<int> OwnIips(CampaignSnapshot s)
        {
            var own = new HashSet<int>();
            for (int i = 0; i < s.Supply.Count; i++)
            {
                var source = s.Supply[i];
                if (source.Kind == SupplySourceKind.Iip && source.Owner == s.Alliance) own.Add(source.Id);
            }
            return own;
        }
    }
}
