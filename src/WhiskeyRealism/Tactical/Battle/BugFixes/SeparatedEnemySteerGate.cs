namespace WhiskeyRealism.Tactical.Battle.BugFixes
{
    /// <summary>What the #121 Prefix knows about one CheckForSeparatedEnemy call (the facts are read at runtime; the decisions are here).</summary>
    public struct SeparatedEnemySteerInput
    {
        public bool HasInstanceAndGroup;
        /// <summary>The side DrivesOrchestrator (an AI-run side whose orchestrator is on).</summary>
        public bool SideDrivenByOrchestrator;
        /// <summary>The instance is the W&amp;L feud duplicate (its stances and moves are W&amp;L's).</summary>
        public bool FeudInstance;
        public bool SideInRange;
        /// <summary>The group is in the side's AI pool (ArmyPostureInputsRuntime.InAiPool).</summary>
        public bool InAiPool;
        /// <summary>The operations ledger holds a doctrine state for the group (PostureLedger.TryResolveState).</summary>
        public bool HasDoctrineState;
        /// <summary>TacticalWriteGuard.CanWrite(group): Commander Mode Active, AI-controlled, not player-owned, no exposed player descendant.</summary>
        public bool CanWrite;
        public bool FlagOn;
        public bool CommanderModeAllowsWrites;
    }

    /// <summary>
    /// BC13 requirement-fix-separated-enemy-steer: vanilla's omniscient isolated-target move also sends Whiskey-commanded groups
    /// (CheckForSeparatedEnemy 8188). The Prefix sets aiassaultmove (so vanilla returns at 8078 before any write) only for a group
    /// that is Whiskey-commanded; a vanilla-owned group and the player's groups are untouched.
    /// </summary>
    public static class SeparatedEnemySteerGate
    {
        /// <summary>Whiskey-commanded: in the AI pool, the ledger commands it, and the write guard lets Whiskey write to it.</summary>
        public static bool WhiskeyCommanded(in SeparatedEnemySteerInput i)
        {
            return i.InAiPool && i.HasDoctrineState && i.CanWrite;
        }

        /// <summary>Whether the call is the instance's to count at all (an orchestrated, non-feud side): vanilla launches are tallied by it.</summary>
        public static bool Tallied(in SeparatedEnemySteerInput i)
        {
            return i.HasInstanceAndGroup && i.SideInRange && i.SideDrivenByOrchestrator && !i.FeudInstance;
        }

        /// <summary>The Prefix sets aiassaultmove for this call.</summary>
        public static bool Suppress(in SeparatedEnemySteerInput i)
        {
            return Tallied(in i) && i.FlagOn && i.CommanderModeAllowsWrites && WhiskeyCommanded(in i);
        }
    }
}
