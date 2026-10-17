using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Battle
{
    internal enum ColumnDeployReason
    {
        None = 0,
        FiredOn,
        Flanked,
        Engaged,
        /// <summary>Marked for the approach in line (ApproachLine): the enemy is inside the assault range and the group advances or holds in line.</summary>
        Approaching,
    }

    // What the decision reads about one regiment; everything comes from the snapshot's own-side rows.
    internal readonly struct ColumnDeployInput
    {
        public ColumnDeployInput(bool infantry, bool mounted, int formation, bool receivedFire, int fireFromFlank, int outflanked, bool groupFlankRisk,
            bool groupCloseEngaged, bool withdrawing, bool routed, bool recentDeploy, bool approaching = false)
        {
            Infantry = infantry;
            Mounted = mounted;
            Formation = formation;
            ReceivedFire = receivedFire;
            FireFromFlank = fireFromFlank;
            Outflanked = outflanked;
            GroupFlankRisk = groupFlankRisk;
            GroupCloseEngaged = groupCloseEngaged;
            Withdrawing = withdrawing;
            Routed = routed;
            RecentDeploy = recentDeploy;
            Approaching = approaching;
        }

        public bool Infantry { get; }
        public bool Mounted { get; }
        /// <summary>Regiment.formation (a regiment row's Formation).</summary>
        public int Formation { get; }
        public bool ReceivedFire { get; }
        public int FireFromFlank { get; }
        public int Outflanked { get; }
        public bool GroupFlankRisk { get; }
        public bool GroupCloseEngaged { get; }
        public bool Withdrawing { get; }
        public bool Routed { get; }
        public bool RecentDeploy { get; }
        public bool Approaching { get; }
    }

    // Deploying a march column that is being shot at (unit churn, 2026-10-17).
    //
    // Vanilla anchors (decompile build 14703111):
    //   CheckFormationChange 127662-127668: a regiment with a path whose last corner is further than mindistanceformarchcolumn (60 m, unitprefs 881)
    //     and no enemy within distancetoenemyforinterruption (100 m) takes formation 3 (SetDistanceMarchColumn 127702, distancemarchcolumn = true).
    //   127677-127682: with an enemy inside those 100 m (movementmode 0 or 3) it goes back to formation 0, facing lastwaypointrotation[0], the march
    //     heading and not the enemy: a column deployed from the flank faces the way it was walking.
    //   132498-132511: a manual stop restores lastformation and ends distancemarchcolumn.
    //   ChangeRegimentFormation 135437: a group object (unittyp above 13) is always formation 3, so a brigade's own `formation` is no column state.
    // Nothing in vanilla deploys a column that is fired on from further than 100 m, and Whiskey's formation writes follow a group's groupformation
    // (CommandFormationWritePolicy), which does not move when vanilla columns the regiments. Pure.
    internal static class ColumnDeployment
    {
        /// <summary>Regiment.formation 3, the march column (CheckFormationChange 127668).</summary>
        public const int RegimentMarchColumn = 3;

        /// <summary>Regiment.formation 0, the line vanilla deploys to (127677).</summary>
        public const int RegimentLine = 0;

        /// <summary>Real seconds at 1x between two deployments of one regiment; a new march path re-columns it (vanilla), and fire may find it again.</summary>
        public const float CooldownRealSecondsAtOneX = 20f;

        public static ColumnDeployReason Decide(in ColumnDeployInput i)
        {
            if (!i.Infantry || i.Mounted || i.Formation != RegimentMarchColumn) return ColumnDeployReason.None;
            if (i.Withdrawing || i.Routed || i.RecentDeploy) return ColumnDeployReason.None;
            if (i.ReceivedFire) return ColumnDeployReason.FiredOn;
            if (i.FireFromFlank != 0 || i.Outflanked != 0 || i.GroupFlankRisk) return ColumnDeployReason.Flanked;
            if (i.GroupCloseEngaged) return ColumnDeployReason.Engaged;
            if (i.Approaching) return ColumnDeployReason.Approaching;
            return ColumnDeployReason.None;
        }

        /// <summary>A task that withdraws its group: its regiments are in column to get away, not to be caught.</summary>
        public static bool WithdrawalTask(CommandTaskType task)
        {
            return task == CommandTaskType.FallBackToLine || task == CommandTaskType.Delay;
        }

        /// <summary>The one formation intent: stop the march, re-form to line facing <paramref name="heading"/> (negative: keep the facing).</summary>
        public static BattleOrderIntent Intent(int regimentId, float heading)
        {
            return new BattleOrderIntent
            {
                GroupId = regimentId,
                Channel = OrderChannel.Formation,
                Value = RegimentLine,
                Heading = heading >= 0f ? heading : -1f,
                Flags = IntentFlags.Deploy,
                Priority = IntentPriority.Executor,
                Source = IntentSource.FacingPulse,
                Reason = "column-deploy",
            };
        }
    }
}
