using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class ObjectiveAdapter
    {
        private static readonly Dictionary<int, ObjectiveMetadata> Table
            = new Dictionary<int, ObjectiveMetadata>();

        internal static ObjectiveMetadata Resolve(object campaignObjective)
        {
            if (campaignObjective == null)
                return ObjectiveMetadata.DefaultDerived(Theater.Unknown, 0f, 0f);

            var idField = AccessTools.Field(campaignObjective.GetType(), "UniqueObjectiveID");
            int id = idField != null ? (int)idField.GetValue(campaignObjective) : -1;

            if (id >= 0 && Table.TryGetValue(id, out var hit))
                return hit;

            var derived = Derive(campaignObjective);
            OnceLog.Info(
                "objderive:" + id,
                $"[ObjectiveAdapter] geographic fallback for objective ID {id} → theater={derived.Theater} (consider adding to hand-coded table)");
            return derived;
        }

        private static ObjectiveMetadata Derive(object campaignObjective)
        {
            try
            {
                var objList = AccessTools.Field(campaignObjective.GetType(), "objectives")
                                ?.GetValue(campaignObjective) as IList;
                if (objList == null || objList.Count == 0)
                    return ObjectiveMetadata.DefaultDerived(Theater.Unknown, 0f, 0f);

                float sumX = 0f, sumY = 0f;
                int count = 0;
                foreach (var target in objList)
                {
                    var pos = TryGetWorldPosition(target);
                    if (pos.HasValue)
                    {
                        sumX += pos.Value.x;
                        sumY += pos.Value.z;
                        count++;
                    }
                }
                if (count == 0)
                    return ObjectiveMetadata.DefaultDerived(Theater.Unknown, 0f, 0f);

                var cx = sumX / count;
                var cy = sumY / count;
                return ObjectiveMetadata.DefaultDerived(BucketTheaterFromWorldXY(cx, cy), cx, cy);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ObjectiveAdapter] derive failed: {ex.Message}");
                return ObjectiveMetadata.DefaultDerived(Theater.Unknown, 0f, 0f);
            }
        }

        private static Vector3? TryGetWorldPosition(object target)
        {
            if (target == null) return null;
            try
            {
                var component = target as Component;
                if (component != null) return component.transform.position;
                var f = AccessTools.Field(target.GetType(), "position");
                if (f != null && f.FieldType == typeof(Vector3))
                    return (Vector3)f.GetValue(target);
            }
            catch { }
            return null;
        }

        private static Theater BucketTheaterFromWorldXY(float x, float z)
        {
            if (x < -200f) return Theater.TransMiss;
            if (x > 800f && z < -100f) return Theater.Coast;
            if (x > 600f) return Theater.East;
            return Theater.West;
        }
    }
}
