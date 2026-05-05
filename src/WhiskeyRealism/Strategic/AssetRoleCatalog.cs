using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    // Hand-coded named-anchor role overrides for the strategic-defense doctrine.
    // The catalog contains two key classes of entries:
    //   1. Real GTCW names captured from the Slice 1 smoke (e.g. "Fort McHenry",
    //      "Brooklyn Navy Yard") — the primary resolution path.
    //   2. Legacy best-guess lowercase-hyphenated keys (e.g. "wilmington-harbor") kept
    //      as fallback so a future CSA Western/Trans-Miss campaign that emits different
    //      name forms still resolves without modification.
    // Catalog hits override AssetRoleScorer's profile-derived defaults; misses fall back to scorer.
    public static class AssetRoleCatalog
    {
        private static readonly Dictionary<string, AssetStrategicRole> _entries =
            new Dictionary<string, AssetStrategicRole>(StringComparer.OrdinalIgnoreCase)
        {
            // -----------------------------------------------------------------------
            // Real GTCW names from Slice 1 smoke
            // -----------------------------------------------------------------------

            // CSA blockade-runner ports
            { "Wilmington Port",     AssetStrategicRole.BlockadeRunnerPort },

            // Hampton Roads area (dual-use: CSA capital approach + Union forward)
            { "Hampton",             AssetStrategicRole.CapitalApproach | AssetStrategicRole.UnionForwardBase },
            { "Gosport",             AssetStrategicRole.CapitalApproach },
            { "Gosport ",            AssetStrategicRole.CapitalApproach },  // vanilla trailing-space variant seen in smoke

            // Small NC river/coastal ports — leave role to scorer
            { "Plymouth Port",       AssetStrategicRole.None },
            { "Elizabeth City",      AssetStrategicRole.None },
            { "Morehead City",       AssetStrategicRole.None },
            { "New Bern Port",       AssetStrategicRole.None },

            // CSA capital-approach + key forts (CSA-side)
            { "Fort Norfolk",        AssetStrategicRole.KeyFort | AssetStrategicRole.CapitalApproach },
            { "Fort Caswell",        AssetStrategicRole.KeyFort },          // Cape Fear, NC
            { "Fort Macon",          AssetStrategicRole.KeyFort },          // NC outer banks

            // Union capital approach (Washington / Baltimore / Potomac corridor)
            { "Washington D.C. Port", AssetStrategicRole.CapitalApproach },
            { "Annapolis Port",       AssetStrategicRole.CapitalApproach },
            { "Baltimore Port",       AssetStrategicRole.CapitalApproach },
            { "Alexandria Port",      AssetStrategicRole.CapitalApproach },
            { "Aquia Harbour",        AssetStrategicRole.CapitalApproach },
            { "Port Tobacco",         AssetStrategicRole.None },             // small Potomac crossing
            { "Fort Washington",      AssetStrategicRole.KeyFort | AssetStrategicRole.CapitalApproach },
            { "Fort McHenry",         AssetStrategicRole.KeyFort | AssetStrategicRole.CapitalApproach }, // Baltimore Harbor
            { "Fort Carroll",         AssetStrategicRole.KeyFort | AssetStrategicRole.CapitalApproach },
            { "Fort Aquia Landing",   AssetStrategicRole.KeyFort | AssetStrategicRole.CapitalApproach },

            // Union forward bases / key forts (Hampton Roads + east coast)
            { "Fort Monroe",          AssetStrategicRole.KeyFort | AssetStrategicRole.UnionForwardBase },
            { "Fort Delaware",        AssetStrategicRole.KeyFort },
            { "Fort Mifflin",         AssetStrategicRole.KeyFort },
            { "Brooklyn Navy Yard",   AssetStrategicRole.UnionForwardBase },
            { "Fort Tompkins",        AssetStrategicRole.KeyFort },          // Staten Island, NY harbor

            // Union major logistics / embarkation ports
            { "New York Port",        AssetStrategicRole.UnionForwardBase },
            { "Port of Philadelphia", AssetStrategicRole.UnionForwardBase },

            // River control hubs
            { "Port of Pittsburgh",   AssetStrategicRole.RiverControlHub | AssetStrategicRole.UnionForwardBase },
            { "Wheeling Port",        AssetStrategicRole.RiverControlHub },
            { "Parkersburg Port",     AssetStrategicRole.RiverControlHub },
            { "Harrisburg Port",      AssetStrategicRole.RiverControlHub },  // PA capital, Susquehanna

            // Small Ohio River ports — leave role to scorer
            { "Steubenville Port",    AssetStrategicRole.None },
            { "Portsmouth Port",      AssetStrategicRole.None },
            { "Port of Marietta",     AssetStrategicRole.None },

            // CSA capital river landing + late-war Union forward base
            { "Rocket's Landing",     AssetStrategicRole.CapitalApproach },  // Richmond's port
            { "City Point",           AssetStrategicRole.UnionForwardBase },  // Grant's '64-'65 base; effective late-war only

            // Small VA / Rappahannock ports — leave to scorer
            { "City Dock",            AssetStrategicRole.None },
            { "Port Royal",           AssetStrategicRole.None },              // small Rappahannock port
            { "West Point",           AssetStrategicRole.None },              // VA York River

            // Rear-safe ports (NJ/NY minor — explicitly low-priority)
            { "Atlantic City",        AssetStrategicRole.RearSafePort },
            { "Tom's River",          AssetStrategicRole.RearSafePort },
            { "Trenton Port",         AssetStrategicRole.RearSafePort },
            { "Port of Newark",       AssetStrategicRole.RearSafePort },
            { "Port of Camden",       AssetStrategicRole.RearSafePort },
            { "Jersey City Port",     AssetStrategicRole.RearSafePort },
            { "Port Richmond",        AssetStrategicRole.RearSafePort },      // Staten Island village
            { "Lewes",                AssetStrategicRole.RearSafePort },
            { "Onancock",             AssetStrategicRole.RearSafePort },
            { "Somers Cove",          AssetStrategicRole.RearSafePort },

            // Forts not yet classified — leave to scorer (KeyFort by Level >= 2)
            { "Fort Randol",          AssetStrategicRole.None },
            { "Fort Dibrell",         AssetStrategicRole.None },
            { "Fort Smith Jr.",       AssetStrategicRole.None },

            // -----------------------------------------------------------------------
            // Legacy best-guess lowercase-hyphenated keys (fallback for name variants)
            // Keep in place: cost nothing, allow resolution if vanilla emits these forms
            // in a future CSA Western / Trans-Miss campaign or different game version.
            // -----------------------------------------------------------------------
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
