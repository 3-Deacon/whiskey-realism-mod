using System;

namespace WhiskeyRealism.Strategic
{
    [Flags]
    public enum AssetStrategicRole
    {
        None                  = 0,
        BlockadeRunnerPort    = 1 << 0,
        UnionForwardBase      = 1 << 1,
        RiverControlHub       = 1 << 2,
        CapitalApproach       = 1 << 3,
        KeyFort               = 1 << 4,
        SupplyEscapeOnlyPort  = 1 << 5,
        RearSafePort          = 1 << 6
    }
}
