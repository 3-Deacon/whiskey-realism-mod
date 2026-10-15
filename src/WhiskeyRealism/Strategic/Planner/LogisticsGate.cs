using System;

namespace WhiskeyRealism.Strategic.Planner
{
    public struct ReachResult
    {
        public bool InReach;
        public float Slack01;                // 1 - distance / range to the nearest source; 0 when out of reach
        public int NearestSourceId;          // -1 none
        public float NearestSourceDistance;  // float.MaxValue none
    }

    // "Don't outrun supply" (spec component 5). A target is in reach for a formation when a supply
    // source lies within the formation's campaign commanderrange of it, widened by the sector
    // commander's administration slack (CommanderProfile.SupplySlack, G2). That is vanilla's own
    // per-unit supply rule (Regiment.UpdateIIPSupplyLink 114272-114306 over the IIPs and buildings
    // within commanderrange, UpdateIIPsInRange 113997-114010):
    //   - an IIP owned by the snapshot's alliance that is not an invisible IIP (vanilla zeroes an
    //     invisible IIP for a field unit, 114281, and an IIP of a non-allied owner, 114277);
    //   - a supply depot of the alliance or an ally (the snapshot lists only those, 114296-114306)
    //     that is built (constructiontimer <= 0) and in condition >= 0.75 (the condition vanilla's
    //     job 19 accepts for a depot, 14762-14768).
    // Inside commanderrange vanilla's 0..2000 supply-speed curve (114317-114345) is flat, so the
    // nearest valid source decides. The target zone's ownpoints <= supplycapacity (vanilla's zone
    // stacking gate, 10983 / SupplyInZoneAvailable 10941-10962) is a stacking check only: a target
    // in a zone over its cap is not in reach. G3 adds AddDepotTasks in LogisticsGate.DepotTasks.cs.
    public static partial class LogisticsGate
    {
        public const float DepotMinCondition = 0.75f;
        private const float ClampStepFraction = 0.25f; // of the reach range
        private const int MaxClampSamples = 256;

        public static ReachResult Evaluate(CampaignSnapshot s, float targetX, float targetZ, float formationCommandRange, float supplySlack)
        {
            var result = new ReachResult { NearestSourceId = -1, NearestSourceDistance = float.MaxValue };
            if (s == null) return result;
            float range = Range(formationCommandRange, supplySlack);
            if (range <= 0f) return result;

            for (int i = 0; i < s.Supply.Count; i++)
            {
                var source = s.Supply[i];
                if (!Supplies(s.Alliance, source)) continue;
                float dx = source.X - targetX;
                float dz = source.Z - targetZ;
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d > range || d >= result.NearestSourceDistance) continue;
                result.NearestSourceDistance = d;
                result.NearestSourceId = source.Id;
            }
            if (result.NearestSourceId < 0 || !ZoneHasStackingRoom(s, targetX, targetZ)) return result;

            result.InReach = true;
            result.Slack01 = SnapshotRules.Clamp01(1f - result.NearestSourceDistance / range);
            return result;
        }

        // Pulls a hold point back along the from -> to axis to the farthest in-reach point, sampled
        // every quarter of the reach range (at most 256 samples). Unchanged and true when the
        // point is already in reach; unchanged and false when no point on the axis is.
        public static bool ClampToReach(CampaignSnapshot s, float fromX, float fromZ, ref float toX, ref float toZ,
                                        float formationCommandRange, float supplySlack)
        {
            if (Evaluate(s, toX, toZ, formationCommandRange, supplySlack).InReach) return true;
            float range = Range(formationCommandRange, supplySlack);
            if (range <= 0f) return false;

            float ax = toX - fromX;
            float az = toZ - fromZ;
            float length = (float)Math.Sqrt(ax * ax + az * az);
            int samples = (int)Math.Ceiling(length / (range * ClampStepFraction));
            if (samples < 1) samples = 1;
            if (samples > MaxClampSamples) samples = MaxClampSamples;
            for (int k = 1; k <= samples; k++)
            {
                float t = 1f - (float)k / samples;
                float px = fromX + ax * t;
                float pz = fromZ + az * t;
                if (!Evaluate(s, px, pz, formationCommandRange, supplySlack).InReach) continue;
                toX = px;
                toZ = pz;
                return true;
            }
            return false;
        }

        private static float Range(float formationCommandRange, float supplySlack)
        {
            return Math.Max(0f, formationCommandRange) * (1f + Math.Max(0f, supplySlack));
        }

        private static bool Supplies(int alliance, in SupplySourceRecord source)
        {
            if (source.Kind == SupplySourceKind.Iip)
                return source.Owner == alliance && !source.Invisible;
            return source.Built && source.Condition >= DepotMinCondition;
        }

        private static bool ZoneHasStackingRoom(CampaignSnapshot s, float x, float z)
        {
            int position = AreaGeometry.AreaAt(s.Areas, x, z);
            if (position < 0) return true;
            var area = s.Areas[position];
            return area.OwnPoints <= area.SupplyCapacity;
        }
    }
}
