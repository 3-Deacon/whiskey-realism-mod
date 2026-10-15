using UnityEngine;

namespace WhiskeyRealism.Patches
{
    internal static class TacticalPatchIds
    {
        internal static int ComponentInstanceId(Regiment group)
        {
            try { return group != null ? group.GetInstanceID() : 0; }
            catch { return 0; }
        }

        internal static int GameObjectInstanceId(Regiment group)
        {
            try
            {
                GameObject go = group != null ? group.gameObject : null;
                return go != null ? go.GetInstanceID() : 0;
            }
            catch
            {
                return 0;
            }
        }

        // Ledger node ids are "node-" + Unity instance id (instance ids can be negative,
        // giving "node--5120"). Called per group per cycle from ~12 battle patches, so the
        // comparison parses the numeric suffix in place instead of allocating "node-" + id.
        // Only the canonical int.ToString() form matches (no '+', no leading zeros, no "-0").
        internal static bool NodeIdMatches(string nodeId, int gameObjectInstanceId, int componentInstanceId)
        {
            if (gameObjectInstanceId == 0 && componentInstanceId == 0) return false;
            int parsed;
            if (!TryParseNodeId(nodeId, out parsed) || parsed == 0) return false;
            return parsed == gameObjectInstanceId || parsed == componentInstanceId;
        }

        internal static bool TryParseNodeId(string nodeId, out int instanceId)
        {
            instanceId = 0;
            const string prefix = "node-";
            if (nodeId == null || nodeId.Length <= prefix.Length) return false;
            if (string.CompareOrdinal(nodeId, 0, prefix, 0, prefix.Length) != 0) return false;

            int i = prefix.Length;
            bool negative = nodeId[i] == '-';
            if (negative) i++;
            int digits = nodeId.Length - i;
            if (digits <= 0 || digits > 10) return false;
            if (nodeId[i] == '0' && (digits > 1 || negative)) return false;

            long value = 0;
            for (; i < nodeId.Length; i++)
            {
                char c = nodeId[i];
                if (c < '0' || c > '9') return false;
                value = value * 10 + (c - '0');
            }

            if (negative) value = -value;
            if (value < int.MinValue || value > int.MaxValue) return false;
            instanceId = (int)value;
            return true;
        }
    }
}
