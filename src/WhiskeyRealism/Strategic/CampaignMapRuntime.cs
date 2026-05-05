using System;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class CampaignMapRuntime
    {
        internal static CampaignMapLedger Build()
        {
            var towns = new List<CampaignMapTown>();
            var assets = new List<CampaignMapAsset>();
            try
            {
                if (BattleUnits.towns == null)
                    BattleUnits.ImportTowns();
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-map:import-towns", "[CampaignMap] town import failed: " + ex.Message);
            }

            try
            {
                if (BattleUnits.towns == null) return CampaignMapLedger.Build(towns);

                for (int i = 0; i < BattleUnits.towns.Count; i++)
                {
                    var town = BattleUnits.towns[i];
                    if (town == null) continue;
                    var position = ((Component)town).transform.position;
                    towns.Add(new CampaignMapTown
                    {
                        CityName = town.CityName,
                        StateId = town.state,
                        StateName = StateName(town.state),
                        StateAbbrev = StateAbbrev(town.state),
                        Owner = town.Owner,
                        OriginalOwner = town.originalowner >= 0f ? Convert.ToInt32(town.originalowner) : -1,
                        IsCapital = town.IsCapital,
                        X = position.x,
                        Z = position.z,
                        CitySize = town.citysize,
                        RepresentingPopulation = town.representingpopulation,
                        AvailableWorkforceLevel = town.availableworkforcelevel,
                        PrivateWealth = town.privatewealth,
                        IncomeTax = town.incometax
                    });
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-map:build", "[CampaignMap] build failed: " + ex.Message);
            }

            try
            {
                AddHarborAssets(assets);
                AddFortAssets(assets);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-map:assets", "[CampaignMap] asset build failed: " + ex.Message);
            }

            var ledger = CampaignMapLedger.Build(towns, assets);
            LogMissingRoles(ledger);
            return ledger;
        }

        internal static string DynamicSignature()
        {
            unchecked
            {
                int hash = 17;
                try
                {
                    if (BattleUnits.towns != null)
                    {
                        for (int i = 0; i < BattleUnits.towns.Count; i++)
                        {
                            var town = BattleUnits.towns[i];
                            if (town == null) continue;
                            hash = hash * 31 + town.Owner;
                            hash = hash * 31 + town.state;
                            hash = hash * 31 + (town.IsCapital ? 1 : 0);
                        }
                    }
                }
                catch { }

                try
                {
                    if (BattleUnits.harbors != null)
                    {
                        for (int i = 0; i < BattleUnits.harbors.Count; i++)
                        {
                            var harbor = BattleUnits.harbors[i];
                            if (harbor == null) continue;
                            hash = hash * 31 + harbor.allianceowner;
                            hash = hash * 31 + harbor.BuildingLevel;
                            hash = hash * 31 + Mathf.RoundToInt(harbor.condition * 10f);
                            hash = hash * 31 + Mathf.RoundToInt(harbor.blocked * 10f);
                        }
                    }
                }
                catch { }

                try
                {
                    if (BattleUnits.fort != null)
                    {
                        for (int i = 0; i < BattleUnits.fort.Count; i++)
                        {
                            var fort = BattleUnits.fort[i];
                            if (fort == null) continue;
                            hash = hash * 31 + fort.Owner;
                            hash = hash * 31 + fort.level;
                            hash = hash * 31 + Mathf.RoundToInt(fort.Condition * 10f);
                        }
                    }
                }
                catch { }

                return hash.ToString("X");
            }
        }

        private static void LogMissingRoles(CampaignMapLedger ledger)
        {
            if (ledger == null || ledger.Assets == null) return;
            foreach (var asset in ledger.Assets)
            {
                if (asset == null || string.IsNullOrEmpty(asset.Name)) continue;
                if (asset.StrategicRole != AssetStrategicRole.None) continue;
                OnceLog.Info("defense-intent:asset-no-role:" + asset.Name,
                    $"[DefenseIntent:asset] missing-role asset={asset.Name} kind={asset.Kind}");
            }
        }

        private static void AddHarborAssets(List<CampaignMapAsset> assets)
        {
            if (BattleUnits.harbors == null) return;
            for (int i = 0; i < BattleUnits.harbors.Count; i++)
            {
                var harbor = BattleUnits.harbors[i];
                if (harbor == null) continue;
                var position = ((Component)harbor).transform.position;
                assets.Add(new CampaignMapAsset
                {
                    Kind = harbor.isriverport ? CampaignMapAssetKind.RiverHarbor : CampaignMapAssetKind.SeaHarbor,
                    Name = harbor.IIPName,
                    StateId = harbor.state,
                    StateName = StateName(harbor.state),
                    StateAbbrev = StateAbbrev(harbor.state),
                    Owner = harbor.allianceowner,
                    X = position.x,
                    Z = position.z,
                    Level = harbor.BuildingLevel,
                    Condition = harbor.condition,
                    Blocked = harbor.blocked,
                    Capacity = SafeTransportCapacity(harbor)
                });
            }
        }

        private static void AddFortAssets(List<CampaignMapAsset> assets)
        {
            if (BattleUnits.fort == null) return;
            for (int i = 0; i < BattleUnits.fort.Count; i++)
            {
                var fort = BattleUnits.fort[i];
                if (fort == null) continue;
                var position = ((Component)fort).transform.position;
                assets.Add(new CampaignMapAsset
                {
                    Kind = CampaignMapAssetKind.Fort,
                    Name = string.IsNullOrEmpty(fort.BuildingName) ? ((UnityEngine.Object)fort).name : fort.BuildingName,
                    StateId = fort.state,
                    StateName = StateName(fort.state),
                    StateAbbrev = StateAbbrev(fort.state),
                    Owner = fort.Owner,
                    X = position.x,
                    Z = position.z,
                    Level = fort.level,
                    Condition = fort.Condition
                });
            }
        }

        private static float SafeTransportCapacity(IIP harbor)
        {
            try { return harbor.GetTransportCapacity(); }
            catch { return 0f; }
        }

        private static string StateName(int stateId)
        {
            try
            {
                if (GameVars.nation == null || stateId < 0 || stateId >= GameVars.nation.Length)
                    return null;
                return GameVars.nation[stateId]?.name;
            }
            catch { return null; }
        }

        private static string StateAbbrev(int stateId)
        {
            try
            {
                if (GameVars.nation == null || stateId < 0 || stateId >= GameVars.nation.Length)
                    return null;
                return GameVars.nation[stateId]?.abbrev;
            }
            catch { return null; }
        }
    }
}
