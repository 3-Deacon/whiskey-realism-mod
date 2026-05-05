using System;
using System.Linq;

namespace WhiskeyRealism.Strategic
{
    public static class DefenseThreatSignature
    {
        public static string ForSeaInvasion(int invasionForceInstanceId, string spotName, string sourcePortName)
        {
            string spot = string.IsNullOrEmpty(spotName) ? "<no-spot>" : spotName;
            string port = string.IsNullOrEmpty(sourcePortName) ? "<no-port>" : sourcePortName;
            return $"sif:{invasionForceInstanceId}:{spot}:{port}";
        }

        public static string ForRaid(int raidGroupInstanceId, string nearestAssetName)
        {
            string asset = string.IsNullOrEmpty(nearestAssetName) ? "<no-asset>" : nearestAssetName;
            return $"raid:{raidGroupInstanceId}:{asset}";
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
