using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Battle
{
    internal enum ColumnDeployReason
    {
        None = 0,
        FiredOn,
        Flanked,
        Engaged,
        /// <summary>
        /// Only the group's perceived flank risk (BattleWriterPolicy.PerceivedFlankRisk: the group's outflanked state over the snapshot's flank pressure, or a
        /// positive mean vanilla outflanked weight): the regiment itself took no fire from the flank and is not outflanked. Deployed in march, never stopped.
        /// </summary>
        FlankRisk,
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
            if (i.FireFromFlank != 0 || i.Outflanked != 0) return ColumnDeployReason.Flanked;
            if (i.GroupCloseEngaged) return ColumnDeployReason.Engaged;
            if (i.GroupFlankRisk) return ColumnDeployReason.FlankRisk;
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
                case ColumnDeployReason.FlankRisk: return "column-deploy:FlankRisk";
                case ColumnDeployReason.Approaching: return "column-deploy:Approaching";
                default: return "column-deploy";
            }
        }

        /// <summary>
        /// The one formation intent. A deploy that has a threat to face stops the march and turns to <paramref name="heading"/> in the formation the stop
        /// restores (fire, a flank taken, close contact: fight in place). A deploy that is not an answer to fire (the approach, a perceived flank risk) or has
        /// no bearing to face keeps marching (<see cref="IntentFlags.DeployInMarch"/>): a stop clears the regiment's path (StopRegiment 132397) and the
        /// group's re-issued move is a duplicate while its other regiments walk (CommandWaypointWritePolicy.ShouldSkipDuplicateWaypoint), so a stopped
        /// regiment would be left behind, and with no bearing the stop would turn it to nothing. Smoke 2026-10-18: 45 of 96 Flanked deploys stopped a
        /// regiment with no bearing (KeptFacing 34, NoBearing 11).
        /// </summary>
        public static BattleOrderIntent Intent(int regimentId, float heading, ColumnDeployReason reason)
        {
            return new BattleOrderIntent
            {
                GroupId = regimentId,
                Channel = OrderChannel.Formation,
                Value = RegimentLine,
                Heading = heading >= 0f ? heading : -1f,
                Flags = KeepsMarching(reason, heading) ? IntentFlags.Deploy | IntentFlags.DeployInMarch : IntentFlags.Deploy,
                Priority = IntentPriority.Executor,
                Source = IntentSource.FacingPulse,
                Reason = ReasonLabel(reason),
            };
        }

        /// <summary>The deploy keeps the regiment marching: the approach and a perceived flank risk always, any other reason when there is no bearing to turn to.</summary>
        public static bool KeepsMarching(ColumnDeployReason reason, float heading)
        {
            return reason == ColumnDeployReason.Approaching || reason == ColumnDeployReason.FlankRisk || heading < 0f;
        }

        /// <summary>
        /// A deploy that keeps marching needs the approach mark. Vanilla looks again at a line regiment with a path over 60 m and no enemy inside 100 m and
        /// columns it (CheckFormationChange 127665-127667), and only the Prefix, which skips that look for a marked regiment, keeps it in line; an unmarked
        /// one is columned within a second and the 20 s cooldown then deploys it again (smoke 2026-10-18: 9 of 38 in-march deploys re-columned within 3 s).
        /// So an unmarked regiment gets no in-march deploy (nothing is submitted and it is not stopped); a deploy that stops the regiment needs no mark.
        /// </summary>
        public static bool MayDeploy(ColumnDeployReason reason, float heading, bool marked)
        {
            return marked || !KeepsMarching(reason, heading);
        }

        /// <summary>A path end nearer than this is no walking direction (a block's length).</summary>
        public const float WalkHeadingMinMeters = 20f;

        /// <summary>The bearing from the regiment to the end of its path, or -1 when the end is within <see cref="WalkHeadingMinMeters"/>.</summary>
        public static float WalkHeadingTo(float fromX, float fromZ, float toX, float toZ)
        {
            float dx = toX - fromX, dz = toZ - fromZ;
            if (dx * dx + dz * dz < WalkHeadingMinMeters * WalkHeadingMinMeters) return -1f;
            return PostureSense.HeadingTo(fromX, fromZ, toX, toZ);
        }

        /// <summary>How far off its walking direction a deployed line may face before it faces the way it walks instead.</summary>
        public const float MaxOffWalkDegrees = 60f;

        /// <summary>
        /// The facing of a line deployed in march. A line whose facing is more than 90 degrees off its path walks backwards, so the regiment faces its walking
        /// direction (<paramref name="walk"/>, the bearing to its path's end; negative when it has none) unless the threat bearing is within
        /// <see cref="MaxOffWalkDegrees"/> of it. With no path it faces the threat, and with neither it takes the facing vanilla's own deploy takes
        /// (<paramref name="vanillaFinal"/>, lastwaypointrotation[0], 127679; negative keeps the regiment's facing). Smoke 2026-10-18: a deploy with no bearing
        /// kept the column's facing (targetFacing -1) while the user watched the 15th Indiana march backwards.
        /// </summary>
        public static float MarchFacing(float threat, float walk, float vanillaFinal)
        {
            if (walk >= 0f)
            {
                if (threat >= 0f && AngleBetween(threat, walk) <= MaxOffWalkDegrees) return threat;
                return walk;
            }
            return threat >= 0f ? threat : vanillaFinal;
        }

        private static float AngleBetween(float a, float b)
        {
            float d = System.Math.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
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
        public static ColumnDeployApplied Apply<THost>(ref THost host, float heading, bool keepMarching, out float facing) where THost : struct, IColumnDeployHost
        {
            facing = -1f;
            if (!host.OffBridgeOrCrossing) return ColumnDeployApplied.HeldOnBridge;
            // The approach in line keeps its path: vanilla's own deploy in march (127676-127682), which re-lays the line from the regiment's position
            // and re-targets the last corner of its path (RegimentSetPath, target default with modifylastwaypoint). A stop would clear that path.
            if (keepMarching)
            {
                facing = MarchFacing(heading, host.WalkHeading, host.VanillaFinalRotation);
                host.DeployInMarch(facing);
                return ColumnDeployApplied.DeployedInMarch;
            }
            host.Stop();
            if (host.Formation == RegimentMarchColumn)
            {
                host.ReformToLine(heading);
                facing = heading;
                return ColumnDeployApplied.Reformed;
            }
            if (heading < 0f) return ColumnDeployApplied.NoBearing;
            if (!host.Rotate(heading)) return ColumnDeployApplied.KeptFacing;
            facing = heading;
            return ColumnDeployApplied.Faced;
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
        /// <summary>Not stopped: deployed to line where it stands with its path kept, so it goes on advancing in line (the approach in line).</summary>
        DeployedInMarch,
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
        /// <summary>Vanilla's deploy in march (127677-127682): the same call without the stop, then distancemarchcolumn = false.</summary>
        void DeployInMarch(float heading);
        /// <summary>The bearing from the regiment to the end of its path, in degrees; negative when it has no path or the end is within a block's length.</summary>
        float WalkHeading { get; }
        /// <summary>lastwaypointrotation[0], the final rotation of the regiment's path, which vanilla's own deploy uses (127679); negative when it has none.</summary>
        float VanillaFinalRotation { get; }
        /// <summary>The executor's rotate-in-place; false when it did not turn the regiment.</summary>
        bool Rotate(float heading);
    }
}
