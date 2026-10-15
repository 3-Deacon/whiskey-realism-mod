using System;
using System.Linq;

namespace WhiskeyRealism.Strategic
{
    public static class DefenseThreatSignature
    {
        // Sea invasions and raids are keyed on the believed enemy group's UnitKey (G1: inferred
        // from seen forces only; never vanilla's landing spot, source port or force instance).
        public static string ForSeaInvasion(string enemyUnitKey)
        {
            return "sif:" + (string.IsNullOrEmpty(enemyUnitKey) ? "<no-key>" : enemyUnitKey);
        }

        public static string ForRaid(string enemyUnitKey, string nearestAssetName)
        {
            string key = string.IsNullOrEmpty(enemyUnitKey) ? "<no-key>" : enemyUnitKey;
            string asset = string.IsNullOrEmpty(nearestAssetName) ? "<no-asset>" : nearestAssetName;
            return $"raid:{key}:{asset}";
        }

        public static string ForAsset(
            CampaignMapAssetKind assetKind, string assetName, int[] enemyInstanceIds, int topN)
        {
            string name = string.IsNullOrEmpty(assetName) ? "<no-asset>" : assetName;
            string ids = enemyInstanceIds == null || enemyInstanceIds.Length == 0
                ? "<no-enemies>"
                : string.Join(",", enemyInstanceIds.OrderBy(x => x).Take(Math.Max(1, topN)));
            return $"asset:{assetKind}:{name}:{ids}";
        }
    }
}
