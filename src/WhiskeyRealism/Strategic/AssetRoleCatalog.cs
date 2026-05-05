using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    // Hand-coded named-anchor role overrides for the strategic-defense doctrine.
    // Keys are best-guess lowercase GTCW asset names; Task 7 will log unmapped names
    // so the catalog can be refined from real in-game asset names.
    // Catalog hits override AssetRoleScorer's profile-derived defaults; misses fall back to scorer.
    public static class AssetRoleCatalog
    {
        private static readonly Dictionary<string, AssetStrategicRole> _entries =
            new Dictionary<string, AssetStrategicRole>(StringComparer.OrdinalIgnoreCase)
        {
            { "wilmington-harbor",  AssetStrategicRole.BlockadeRunnerPort },
            { "charleston-harbor",  AssetStrategicRole.BlockadeRunnerPort | AssetStrategicRole.KeyFort },
            { "mobile-harbor",      AssetStrategicRole.BlockadeRunnerPort | AssetStrategicRole.KeyFort },
            { "galveston-harbor",   AssetStrategicRole.BlockadeRunnerPort | AssetStrategicRole.SupplyEscapeOnlyPort },
            { "sabine-pass",        AssetStrategicRole.SupplyEscapeOnlyPort },

            { "norfolk-harbor",     AssetStrategicRole.CapitalApproach | AssetStrategicRole.UnionForwardBase },
            { "hampton-roads",      AssetStrategicRole.CapitalApproach | AssetStrategicRole.UnionForwardBase },

            { "beaufort-harbor",    AssetStrategicRole.UnionForwardBase },
            { "annapolis-harbor",   AssetStrategicRole.CapitalApproach },

            { "new-orleans-harbor", AssetStrategicRole.RiverControlHub | AssetStrategicRole.BlockadeRunnerPort },
            { "vicksburg-harbor",   AssetStrategicRole.RiverControlHub | AssetStrategicRole.KeyFort },
            { "memphis-harbor",     AssetStrategicRole.RiverControlHub },
            { "st-louis-harbor",    AssetStrategicRole.RiverControlHub },
            { "baton-rouge-harbor", AssetStrategicRole.RiverControlHub },
            { "cairo-harbor",       AssetStrategicRole.RiverControlHub | AssetStrategicRole.UnionForwardBase },
        };

        public static AssetStrategicRole Lookup(string name)
        {
            if (string.IsNullOrEmpty(name)) return AssetStrategicRole.None;
            return _entries.TryGetValue(name, out var role) ? role : AssetStrategicRole.None;
        }
    }
}
