using System;
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

        internal static bool NodeIdMatches(string nodeId, int gameObjectInstanceId, int componentInstanceId)
        {
            if (string.IsNullOrEmpty(nodeId)) return false;
            if (gameObjectInstanceId != 0 && string.Equals(nodeId, "node-" + gameObjectInstanceId, StringComparison.Ordinal))
                return true;
            return componentInstanceId != 0 && string.Equals(nodeId, "node-" + componentInstanceId, StringComparison.Ordinal);
        }
    }
}
