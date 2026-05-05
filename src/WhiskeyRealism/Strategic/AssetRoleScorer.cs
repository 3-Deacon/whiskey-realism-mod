namespace WhiskeyRealism.Strategic
{
    // Derives AssetStrategicRole flags from the active GrandStrategyProfile and asset geometry.
    // Score() is a pure function — stateless, no side effects, no game-object reads.
    // All gates are explicit: profile tag presence, asset kind, ownership, and geometric distances.
    public static class AssetRoleScorer
    {
        public static AssetStrategicRole Score(
            CampaignMapAsset asset,
            GrandStrategyProfile profile,
            float capitalDistance,
            float frontDistance)
        {
            if (asset == null || profile == null) return AssetStrategicRole.None;
            var role = AssetStrategicRole.None;

            if (asset.Kind == CampaignMapAssetKind.SeaHarbor)
            {
                // CSA sea ports that matter for blockade-running need trade or arms-import strategy weight.
                if (profile.AllianceId == 1 &&
                    (profile.HasTag(StrategyTag.TradeWarfare) || profile.HasTag(StrategyTag.ArmsImports)))
                    role |= AssetStrategicRole.BlockadeRunnerPort;

                // Union sea ports close to the front are forward-projection bases when the profile
                // weights naval blockade or port access and the port is under Union control.
                if (profile.AllianceId == 0 &&
                    (profile.HasTag(StrategyTag.Blockade) || profile.HasTag(StrategyTag.PortAccess)) &&
                    asset.Owner == profile.AllianceId &&
                    frontDistance < 200f)
                    role |= AssetStrategicRole.UnionForwardBase;
            }

            // River harbors are control hubs for whichever side weights river interdiction.
            if (asset.Kind == CampaignMapAssetKind.RiverHarbor &&
                profile.HasTag(StrategyTag.RiverControl))
                role |= AssetStrategicRole.RiverControlHub;

            // Level-2+ forts are key fortifications regardless of profile strategy.
            if (asset.Kind == CampaignMapAssetKind.Fort && asset.Level >= 2)
                role |= AssetStrategicRole.KeyFort;

            // Any asset within 120 km of the faction capital is a capital approach regardless of kind.
            if (capitalDistance < 120f)
                role |= AssetStrategicRole.CapitalApproach;

            return role;
        }

        public static AssetStrategicRole ScoreTown(
            CampaignMapTown town,
            GrandStrategyProfile profile,
            float capitalDistance)
        {
            if (town == null || profile == null) return AssetStrategicRole.None;
            return capitalDistance < 120f ? AssetStrategicRole.CapitalApproach : AssetStrategicRole.None;
        }
    }
}
