using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class ObjectiveAdapter
    {
        internal static ObjectiveMetadata Resolve(object campaignObjective)
        {
            if (campaignObjective == null)
                return ObjectiveMetadata.DefaultDerived(Theater.Unknown, 0f, 0f);

            var idField = AccessTools.Field(campaignObjective.GetType(), "UniqueObjectiveID");
            int id = idField != null ? (int)idField.GetValue(campaignObjective) : -1;

            if (id >= 0 && ObjectiveCatalog.TryResolve(id, out var hit))
                return hit;

            var derived = Derive(campaignObjective);
            OnceLog.Info(
                "objderive:" + id,
                $"[ObjectiveAdapter] geographic fallback for objective ID {id} → theater={derived.Theater} (consider adding to hand-coded table)");
            return derived;
        }

        // Returns true if the CampaignObjective with this ID has its `accomplished` flag set.
        // Returns false on reflection failure (fail-safe: don't replan from a miss).
        internal static bool IsAccomplished(int objectiveId)
        {
            try
            {
                var obj = FindCampaignObjective(objectiveId);
                if (obj == null) return false;
                var acc = AccessTools.Field(obj.GetType(), "accomplished")?.GetValue(obj);
                return acc is bool b && b;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "objective-adapter:accomplished",
                    "[ObjectiveAdapter] IsAccomplished reflection failed: " + ex.Message);
                return false;
            }
        }

        // Returns true if objectiveId appears in the vanilla GetAvailableObjectives list for
        // the given alliance. Returns true on reflection failure (fail-safe: don't replan from
        // a transient miss).
        internal static bool IsAvailable(int objectiveId, int allianceId)
        {
            try
            {
                var t = AccessTools.TypeByName("CampaignObjective");
                if (t == null) return true;
                // Vanilla signature: GetAvailableObjectives(int allianceid, bool includeaccomplished, int mintownobjectives)
                var m = AccessTools.Method(t, "GetAvailableObjectives", new[] { typeof(int), typeof(bool), typeof(int) });
                if (m == null) return true;
                var list = m.Invoke(null, new object[] { allianceId, false, 0 }) as IList;
                if (list == null) return true;
                var idField = AccessTools.Field(t, "UniqueObjectiveID");
                if (idField == null) return true;
                foreach (var obj in list)
                {
                    if (obj == null) continue;
                    if ((int)idField.GetValue(obj) == objectiveId) return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "objective-adapter:available",
                    "[ObjectiveAdapter] IsAvailable reflection failed: " + ex.Message);
                return true;
            }
        }

        internal static Vector3? ResolveObjectivePosition(int objectiveId)
        {
            try
            {
                var obj = FindCampaignObjective(objectiveId);
                if (obj == null) return null;
                var objList = AccessTools.Field(obj.GetType(), "objectives")?.GetValue(obj) as IList;
                if (objList == null || objList.Count == 0) return null;

                foreach (var target in objList)
                {
                    var pos = TryGetWorldPosition(target);
                    if (pos.HasValue) return pos;
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "objpos:" + objectiveId,
                    $"[ObjectiveAdapter] position resolve failed for objective ID {objectiveId}: {ex.Message}");
            }
            return null;
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
                return ObjectiveStrategyTagger.ApplyDefaultTags(
                    ObjectiveMetadata.DefaultDerived(BucketTheaterFromWorldXY(cx, cy), cx, cy));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ObjectiveAdapter] derive failed: {ex.Message}");
                return ObjectiveMetadata.DefaultDerived(Theater.Unknown, 0f, 0f);
            }
        }

        private static object FindCampaignObjective(int objectiveId)
        {
            var coType = AccessTools.TypeByName("CampaignObjective");
            if (coType == null) return null;
            var allObjs = AccessTools.Field(coType, "allcampaignobjectives")?.GetValue(null) as IList;
            if (allObjs == null) return null;
            var idField = AccessTools.Field(coType, "UniqueObjectiveID");
            if (idField == null) return null;

            foreach (var obj in allObjs)
            {
                if (obj == null) continue;
                if ((int)idField.GetValue(obj) == objectiveId) return obj;
            }
            return null;
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
            return TheaterClassifier.FromPosition(x, z);
        }
    }
}
