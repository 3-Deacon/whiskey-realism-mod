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

        /// <summary>The intent's reason: static strings (a row names the cause without building one per submit).</summary>
        public static string ReasonLabel(ColumnDeployReason reason)
        {
            switch (reason)
            {
                case ColumnDeployReason.FiredOn: return "column-deploy:FiredOn";
                case ColumnDeployReason.Flanked: return "column-deploy:Flanked";
                case ColumnDeployReason.Engaged: return "column-deploy:Engaged";
                case ColumnDeployReason.Approaching: return "column-deploy:Approaching";
                default: return "column-deploy";
            }
        }

        /// <summary>The one formation intent: stop the march, then face <paramref name="heading"/> (negative: keep the facing) in the formation the stop restores.</summary>
        public static BattleOrderIntent Intent(int regimentId, float heading, ColumnDeployReason reason)
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
                Reason = ReasonLabel(reason),
            };
        }

        /// <summary>
        /// The deploy, against the regiment (<see cref="IColumnDeployHost"/>; the executor's host wraps vanilla's calls). Vanilla's own guard comes first:
        /// every re-form of CheckFormationChange (127669, 127676) and of a manual stop (132498, 132502, 132507) needs allblocksoffbridgeorcrossing, so a
        /// regiment on a bridge or a crossing is not touched. The manual stop restores the formation the column replaced (132502-132506) and ends
        /// distancemarchcolumn; the line it lands in is the normal case, and the threat heading is applied after it through the executor's facing path.
        /// Only a regiment whose restored formation is the column itself is re-formed to line.
        /// Why the rotate that follows cannot put the regiment back in column: ChangeRegimentFormation sets lastformation to the formation it is leaving
        /// (135470), or, for a manualset call such as the stop's restore (132504), to the formation it is entering (135471-135473). After the restore
        /// formation and lastformation are both the restored line, so RotateRegiment (StopRegiment, then ChangeRegimentFormation(lastformation), 130165-130167)
        /// re-forms the line it already has. (SetDistanceMarchColumn, 127702, entered the column with a non-manual call, which is how lastformation came to hold the line.)
        /// </summary>
        public static ColumnDeployApplied Apply<THost>(ref THost host, float heading) where THost : struct, IColumnDeployHost
        {
            if (!host.OffBridgeOrCrossing) return ColumnDeployApplied.HeldOnBridge;
            host.Stop();
            if (host.Formation == RegimentMarchColumn)
            {
                host.ReformToLine(heading);
                return ColumnDeployApplied.Reformed;
            }
            if (heading < 0f) return ColumnDeployApplied.NoBearing;
            return host.Rotate(heading) ? ColumnDeployApplied.Faced : ColumnDeployApplied.KeptFacing;
        }
    }

    /// <summary>What the deploy did to the regiment; the TacticalColumnDeploy row's <c>applied</c> field.</summary>
    internal enum ColumnDeployApplied
    {
        /// <summary>The regiment is on a bridge or crossing (allblocksoffbridgeorcrossing false): vanilla's guard, nothing was written.</summary>
        HeldOnBridge = 0,
        /// <summary>Stopped; the formation the stop restored was the column itself, so the regiment was re-formed to line facing the heading.</summary>
        Reformed,
        /// <summary>Stopped, out of the column, and turned to the threat heading.</summary>
        Faced,
        /// <summary>Stopped, out of the column; the facing was already within tolerance or the rotation was refused (moving, rotating, charging).</summary>
        KeptFacing,
        /// <summary>Stopped, out of the column; no threat bearing was known, so the facing is the regiment's own.</summary>
        NoBearing,
    }

    /// <summary>The calls the deploy makes on one regiment (OrderExecutorRuntime wraps vanilla's; a test wraps a fake).</summary>
    internal interface IColumnDeployHost
    {
        /// <summary>Regiment.allblocksoffbridgeorcrossing.</summary>
        bool OffBridgeOrCrossing { get; }
        /// <summary>Regiment.formation, read live.</summary>
        int Formation { get; }
        /// <summary>Vanilla's manual stop (StopRegiment, 132498-132511).</summary>
        void Stop();
        /// <summary>ChangeRegimentFormation to line with a manual final rotation (the heading may be negative: keep the facing).</summary>
        void ReformToLine(float heading);
        /// <summary>The executor's rotate-in-place; false when it did not turn the regiment.</summary>
        bool Rotate(float heading);
    }
}
