using System;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Operations
{
    public readonly struct TacticalBattleLinePlan
    {
        public TacticalBattleLinePlan(
            DoctrineTargetPoint primaryTarget,
            DoctrineTargetPoint supportTarget,
            DoctrineTargetPoint fallbackTarget,
            string reason)
            : this(
                primaryTarget,
                supportTarget,
                fallbackTarget,
                DoctrineTargetPoint.None,
                0f,
                reason)
        {
        }

        public TacticalBattleLinePlan(
            DoctrineTargetPoint primaryTarget,
            DoctrineTargetPoint supportTarget,
            DoctrineTargetPoint fallbackTarget,
            DoctrineTargetPoint artilleryLineTarget,
            float echelonDepth,
            string reason)
        {
            PrimaryTarget = primaryTarget;
            SupportTarget = supportTarget;
            FallbackTarget = fallbackTarget;
            ArtilleryLineTarget = artilleryLineTarget;
            EchelonDepth = SanitizeNonNegative(echelonDepth);
            Reason = string.IsNullOrWhiteSpace(reason) ? "battle-line-unspecified" : reason;
        }

        public DoctrineTargetPoint PrimaryTarget { get; }
        public DoctrineTargetPoint SupportTarget { get; }
        public DoctrineTargetPoint FallbackTarget { get; }
        public DoctrineTargetPoint ArtilleryLineTarget { get; }
        public float EchelonDepth { get; }
        public string Reason { get; }

        private static float SanitizeNonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value;
        }
    }

    public static class TacticalBattleLinePlanner
    {
        private const double MainApproachDistance = 75d;
        private const double SupportApproachDistance = 125d;
        private const double FixApproachDistance = 170d;
        private const double ScreenApproachDistance = 235d;
        private const double AssemblyDistance = 210d;
        // The reserve stand-off and lane: also the deployment reserve echelon's (DeploymentDoctrine).
        internal const double ReserveDistance = 425d;
        internal const double ReserveLaneMeters = 90d;
        private const double FallbackDistance = 300d;
        private const double SupportLateral = 180d;
        private const double FixLateral = 160d;
        private const double ScreenLateral = 260d;
        private const double ArtilleryLineDistance = 650d;
        private const double FlankGuardDistance = 155d;
        private const double DepthSlotDistance = 325d;

        public static TacticalBattleLinePlan PlanNode(
            CommandNodeOperationalState node,
            CommandTaskType task,
            BattlefieldObjectiveEstimate objective,
            bool objectiveMatched,
            int commandIndex)
        {
            return PlanNode(
                node,
                task,
                objective,
                objectiveMatched,
                commandIndex,
                default(TacticalBattlefrontSnapshot),
                0);
        }

        public static TacticalBattleLinePlan PlanNode(
            CommandNodeOperationalState node,
            CommandTaskType task,
            BattlefieldObjectiveEstimate objective,
            bool objectiveMatched,
            int commandIndex,
            TacticalBattlefrontSnapshot battlefront,
            int nodeCount)
        {
            TacticalDefensiveLineAnchor anchor = objectiveMatched && !string.Equals(objective.ObjectiveId, "objective-unknown", StringComparison.Ordinal)
                ? TacticalDefensiveLineAnchorPlanner.Plan(objective, battlefront, nodeCount)
                : TacticalDefensiveLineAnchor.None("battle-line-no-objective");
            return PlanNode(node, task, objective, objectiveMatched, commandIndex, battlefront, nodeCount, anchor);
        }

        /// <summary>The same plan over an anchor the caller already planned (CommandDoctrineAssignment plans it once per assignment).</summary>
        public static TacticalBattleLinePlan PlanNode(
            CommandNodeOperationalState node,
            CommandTaskType task,
            BattlefieldObjectiveEstimate objective,
            bool objectiveMatched,
            int commandIndex,
            TacticalBattlefrontSnapshot battlefront,
            int nodeCount,
            TacticalDefensiveLineAnchor defensiveAnchor)
        {
            if (!objectiveMatched || string.Equals(objective.ObjectiveId, "objective-unknown", StringComparison.Ordinal))
            {
                return new TacticalBattleLinePlan(
                    DoctrineTargetPoint.None,
                    DoctrineTargetPoint.None,
                    DoctrineTargetPoint.None,
                    "battle-line-no-objective");
            }

            BattlefieldObjectiveEstimate lineObjective = defensiveAnchor.AsLineObjective(objective);
            // A node the ledger could not place has no position to plan from: the direction, the reserve post and the fallback would all be
            // laid from the map origin (smoke 2026-10-17). An approach avenue gives the direction without the node; nothing else does.
            if (!node.HasPosition && !HasUsableAvenue(lineObjective))
            {
                return new TacticalBattleLinePlan(
                    DoctrineTargetPoint.None,
                    DoctrineTargetPoint.None,
                    DoctrineTargetPoint.None,
                    "battle-line-no-position");
            }

            Direction direction = Direction.FromNodeToObjective(node, lineObjective);
            int side = commandIndex % 2 == 0 ? -1 : 1;
            FrontageGeometry frontage = FrontageGeometry.Build(lineObjective, direction, battlefront, nodeCount, defensiveAnchor);

            switch (task)
            {
                case CommandTaskType.AttackObjective:
                    return new TacticalBattleLinePlan(
                        frontage.Target(MainApproachDistance, frontage.MainLane(commandIndex, nodeCount)),
                        DoctrineTargetPoint.None,
                        Fallback(node, lineObjective, direction, FallbackDistance),
                        frontage.Artillery,
                        (float)MainApproachDistance,
                        "battle-line-main-effort" + frontage.ReasonSuffix);
                case CommandTaskType.SupportAttack:
                    return new TacticalBattleLinePlan(
                        frontage.Target(SupportApproachDistance, frontage.RoleLane(frontage.SupportLane(side), commandIndex, nodeCount)),
                        frontage.Target(MainApproachDistance, 0d),
                        Fallback(node, lineObjective, direction, FallbackDistance),
                        frontage.Artillery,
                        (float)SupportApproachDistance,
                        "battle-line-supporting-attack" + frontage.ReasonSuffix);
                case CommandTaskType.FixEnemy:
                    return new TacticalBattleLinePlan(
                        frontage.Target(FixApproachDistance, frontage.RoleLane(frontage.FixLane(side), commandIndex, nodeCount)),
                        frontage.Target(MainApproachDistance, 0d),
                        Fallback(node, lineObjective, direction, FallbackDistance),
                        frontage.Artillery,
                        (float)FixApproachDistance,
                        "battle-line-fixing-force" + frontage.ReasonSuffix);
                case CommandTaskType.Screen:
                case CommandTaskType.Probe:
                case CommandTaskType.Scout:
                    return new TacticalBattleLinePlan(
                        frontage.Target(ScreenApproachDistance, frontage.RoleLane(frontage.ScreenLane(side), commandIndex, nodeCount)),
                        DoctrineTargetPoint.None,
                        Fallback(node, lineObjective, direction, ScreenApproachDistance),
                        frontage.Artillery,
                        (float)ScreenApproachDistance,
                        "battle-line-screen-probe" + frontage.ReasonSuffix);
                case CommandTaskType.GuardFlank:
                    return new TacticalBattleLinePlan(
                        frontage.FlankGuardTarget(commandIndex, nodeCount),
                        DoctrineTargetPoint.None,
                        Fallback(node, lineObjective, direction, FallbackDistance),
                        frontage.Artillery,
                        (float)FlankGuardDistance,
                        "battle-line-guard-flank" + frontage.ReasonSuffix + ":scourge-slot-flank-guard");
                case CommandTaskType.ReserveWait:
                    TacticalReserveAssemblyDecision reserve = TacticalReserveAssemblyPlanner.ChooseGenerated(
                        node.X,
                        node.Z,
                        node.FacingDegrees,
                        lineObjective.X,
                        lineObjective.Z,
                        commandIndex);
                    return new TacticalBattleLinePlan(
                        direction.UsesApproachAvenue ? BehindObjective(lineObjective, direction, ReserveDistance, ReserveLane(commandIndex)) : reserve.HasTarget ? reserve.Target : frontage.Target(ReserveDistance, 0d),
                        DoctrineTargetPoint.None,
                        Fallback(node, lineObjective, direction, FallbackDistance),
                        frontage.Artillery,
                        (float)ReserveDistance,
                        (reserve.HasTarget ? reserve.Reason : "battle-line-reserve-rally") + frontage.ReasonSuffix);
                case CommandTaskType.FormUp:
                case CommandTaskType.AdvanceToAssembly:
                    return new TacticalBattleLinePlan(
                        frontage.AssemblySlotTarget(commandIndex, nodeCount),
                        DoctrineTargetPoint.None,
                        Fallback(node, lineObjective, direction, FallbackDistance),
                        frontage.Artillery,
                        (float)AssemblyDistance,
                        "battle-line-assembly" + frontage.ReasonSuffix + frontage.AssemblySlotReasonSuffix(commandIndex, nodeCount));
                case CommandTaskType.FallBackToLine:
                    return new TacticalBattleLinePlan(
                        DoctrineTargetPoint.None,
                        DoctrineTargetPoint.None,
                        Fallback(node, lineObjective, direction, FallbackDistance),
                        frontage.Artillery,
                        (float)FallbackDistance,
                        "battle-line-fallback" + frontage.ReasonSuffix);
                default:
                    return new TacticalBattleLinePlan(
                        DoctrineTargetPoint.None,
                        DoctrineTargetPoint.None,
                        DoctrineTargetPoint.None,
                        "battle-line-no-movement");
            }
        }

        private static bool HasUsableAvenue(BattlefieldObjectiveEstimate objective)
        {
            return objective.ApproachAvenue.HasAvenue && objective.ApproachAvenue.Confidence01 >= 0.35f;
        }

        private static DoctrineTargetPoint Approach(
            BattlefieldObjectiveEstimate objective,
            Direction direction,
            double standOff,
            double lateral)
        {
            return DoctrineTargetPoint.From(
                ClampToFloat(objective.X - direction.X * standOff + direction.LateralX * lateral),
                ClampToFloat(objective.Z - direction.Z * standOff + direction.LateralZ * lateral));
        }

        private static DoctrineTargetPoint Fallback(
            CommandNodeOperationalState node,
            BattlefieldObjectiveEstimate objective,
            Direction direction,
            double distance)
        {
            if (direction.UsesApproachAvenue)
            {
                return BehindObjective(objective, direction, distance, 0d);
            }

            return DoctrineTargetPoint.From(
                ClampToFloat(node.X - direction.X * distance),
                ClampToFloat(node.Z - direction.Z * distance));
        }

        private static DoctrineTargetPoint BehindObjective(
            BattlefieldObjectiveEstimate objective,
            Direction direction,
            double distance,
            double lateral)
        {
            return DoctrineTargetPoint.From(
                ClampToFloat(objective.X + direction.X * distance + direction.LateralX * lateral),
                ClampToFloat(objective.Z + direction.Z * distance + direction.LateralZ * lateral));
        }

        private static double ReserveLane(int commandIndex)
        {
            int side = commandIndex % 2 == 0 ? -1 : 1;
            return ReserveLaneMeters * side;
        }

        private static float ClampToFloat(double value)
        {
            if (double.IsNaN(value)) return 0f;
            if (value > float.MaxValue) return float.MaxValue;
            if (value < -float.MaxValue) return -float.MaxValue;
            return (float)value;
        }

        private readonly struct FrontageGeometry
        {
            private readonly BattlefieldObjectiveEstimate _objective;
            private readonly Direction _direction;
            private readonly double _halfWidth;
            private readonly double _laneScale;
            private readonly bool _defensiveSideTargets;
            private readonly double _assemblyStandOff;
            // BC12: the terrain line's own direction (unit, T.L >= cos 30), half length and cos; _lineHalf 0 = no line, lanes run along L.
            private readonly double _lineX, _lineZ, _lineHalf, _lineCos;

            private FrontageGeometry(
                BattlefieldObjectiveEstimate objective,
                Direction direction,
                double halfWidth,
                double laneScale,
                bool defensiveSideTargets,
                double assemblyStandOff,
                double lineX, double lineZ, double lineHalf, double lineCos,
                DoctrineTargetPoint artillery,
                string reasonSuffix)
            {
                _objective = objective;
                _direction = direction;
                _halfWidth = halfWidth;
                _laneScale = laneScale;
                _defensiveSideTargets = defensiveSideTargets;
                _assemblyStandOff = assemblyStandOff;
                _lineX = lineX;
                _lineZ = lineZ;
                _lineHalf = lineHalf;
                _lineCos = lineCos;
                Artillery = artillery;
                ReasonSuffix = reasonSuffix;
            }

            public DoctrineTargetPoint Artillery { get; }
            public string ReasonSuffix { get; }

            public DoctrineTargetPoint Target(double standOff, double fallbackLateral)
            {
                double lateral = _halfWidth > 0d ? fallbackLateral * _laneScale : fallbackLateral;
                return _defensiveSideTargets
                    ? BehindLine(standOff, lateral)
                    : Approach(_objective, _direction, standOff, lateral);
            }

            // A point `standOff` behind the anchor along the approach axis and `lateral` along the line. A terrain line is laid along
            // its own direction (a wall up to 30 degrees off perpendicular bends the lanes with it, so each slot stays on the wall's
            // cover); past the line's ends the lane falls back to the perpendicular through the nearest end, never an extension of the
            // wall into the enemy. Without a terrain line the lane is the perpendicular (a corridor intercept).
            private DoctrineTargetPoint BehindLine(double standOff, double lateral)
            {
                if (!(_lineHalf > 0d))
                    return BehindObjective(_objective, _direction, standOff, lateral);
                double along = lateral / _lineCos;
                double ox, oz;
                if (Math.Abs(along) <= _lineHalf)
                {
                    ox = _lineX * along;
                    oz = _lineZ * along;
                }
                else
                {
                    double sign = along < 0d ? -1d : 1d;
                    double rest = lateral - sign * _lineHalf * _lineCos;
                    ox = _lineX * sign * _lineHalf + _direction.LateralX * rest;
                    oz = _lineZ * sign * _lineHalf + _direction.LateralZ * rest;
                }
                return DoctrineTargetPoint.From(
                    ClampToFloat(_objective.X + _direction.X * standOff + ox),
                    ClampToFloat(_objective.Z + _direction.Z * standOff + oz));
            }

            // The anchor's line direction turned to lie across the approach axis (T.L > 0) and clamped to the line planner's angle limit.
            private static void LineFrame(in TacticalDefensiveLineAnchor anchor, Direction direction, out double lx, out double lz, out double half, out double cos)
            {
                lx = lz = half = cos = 0d;
                if (!(anchor.LineHalfLength > 0f)) return;
                double a = anchor.LineDirX * direction.LateralX + anchor.LineDirZ * direction.LateralZ;
                double b = anchor.LineDirX * direction.X + anchor.LineDirZ * direction.Z;
                if (a < 0d) { a = -a; b = -b; }
                double limit = Math.Tan(TacticalTerrainLinePlanner.MaxAngleDegreesOffPerpendicular * Math.PI / 180d);
                if (Math.Abs(b) > a * limit) b = (b < 0d ? -1d : 1d) * a * limit;
                if (a <= 1e-6d) return;
                double norm = Math.Sqrt(a * a + b * b);
                a /= norm;
                b /= norm;
                lx = a * direction.LateralX + b * direction.X;
                lz = a * direction.LateralZ + b * direction.Z;
                half = anchor.LineHalfLength;
                cos = a;
            }

            public double SupportLane(int side)
            {
                return _halfWidth > 0d ? _halfWidth * 0.45d * side : SupportLateral * side;
            }

            public double FixLane(int side)
            {
                return _halfWidth > 0d ? _halfWidth * 0.55d * side : FixLateral * side;
            }

            public double ScreenLane(int side)
            {
                return _halfWidth > 0d ? _halfWidth * 0.85d * side : ScreenLateral * side;
            }

            public double MainLane(int commandIndex, int nodeCount)
            {
                return DistributedLane(commandIndex, nodeCount, 0.35d);
            }

            public double AssemblyLane(int commandIndex, int nodeCount)
            {
                return DistributedLane(commandIndex, nodeCount, 0.80d);
            }

            public DoctrineTargetPoint AssemblySlotTarget(int commandIndex, int nodeCount)
            {
                if (IsDepthSlot(commandIndex, nodeCount))
                {
                    // Deeper than the line slots, on the node's own side of the objective. With the enemy's approach axis (an avenue, or a defended
                    // anchor) "behind" is along the axis, away from the enemy. With the direction from the node to the objective (no avenue) it is
                    // short of the objective: the slot was BehindLine in both cases, which put it 325 m past the objective, the enemy line, when
                    // the node's direction was the only axis (smoke 2026-10-17).
                    return _defensiveSideTargets || _direction.UsesApproachAvenue
                        ? BehindLine(DepthSlotDistance, DepthLane(commandIndex, nodeCount))
                        : Approach(_objective, _direction, DepthSlotDistance, DepthLane(commandIndex, nodeCount));
                }

                return Target(_assemblyStandOff, AssemblyLane(commandIndex, nodeCount));
            }

            public string AssemblySlotReasonSuffix(int commandIndex, int nodeCount)
            {
                return IsDepthSlot(commandIndex, nodeCount)
                    ? ":scourge-slot-depth"
                    : ":scourge-slot-line";
            }

            public DoctrineTargetPoint FlankGuardTarget(int commandIndex, int nodeCount)
            {
                return Target(FlankGuardDistance, FlankGuardLane(commandIndex, nodeCount));
            }

            public double RoleLane(double baseLane, int commandIndex, int nodeCount)
            {
                if (nodeCount <= 2) return baseLane;
                return baseLane + DistributedLane(commandIndex, nodeCount, 0.16d);
            }

            private double DistributedLane(int commandIndex, int nodeCount, double frontageFraction)
            {
                int safeCount = Math.Max(1, nodeCount);
                if (safeCount <= 1) return 0d;

                int safeIndex = commandIndex < 0 ? 0 : Math.Min(commandIndex, safeCount - 1);
                double normalized = safeIndex / (safeCount - 1d) - 0.5d;
                double fullWidth = _halfWidth > 0d ? _halfWidth * 2d : Math.Max(350d, safeCount * 180d);
                double fraction = Math.Max(0d, Math.Min(1d, frontageFraction));
                return fullWidth * fraction * normalized;
            }

            private double FlankGuardLane(int commandIndex, int nodeCount)
            {
                int safeCount = Math.Max(1, nodeCount);
                int safeIndex = commandIndex < 0 ? 0 : Math.Min(commandIndex, safeCount - 1);
                double side = safeIndex <= (safeCount - 1) * 0.5d ? 1d : -1d;
                double half = _halfWidth > 0d ? _halfWidth : Math.Max(350d, safeCount * 90d);
                return half * 1.45d * side;
            }

            private double DepthLane(int commandIndex, int nodeCount)
            {
                int safeCount = Math.Max(1, nodeCount);
                int safeIndex = commandIndex < 0 ? 0 : Math.Min(commandIndex, safeCount - 1);
                double side = safeIndex <= (safeCount - 1) * 0.5d ? 1d : -1d;
                double half = _halfWidth > 0d ? _halfWidth : Math.Max(350d, safeCount * 90d);
                return half * 0.55d * side;
            }

            private static bool IsDepthSlot(int commandIndex, int nodeCount)
            {
                if (nodeCount < 6) return false;
                int safeIndex = commandIndex < 0 ? 0 : commandIndex;
                return safeIndex == 1 || safeIndex == nodeCount - 2;
            }

            public static FrontageGeometry Build(
                BattlefieldObjectiveEstimate objective,
                Direction direction,
                TacticalBattlefrontSnapshot battlefront,
                int nodeCount,
                TacticalDefensiveLineAnchor defensiveAnchor)
            {
                double width = defensiveAnchor.HasAnchor && defensiveAnchor.FrontageWidth > 0f
                    ? defensiveAnchor.FrontageWidth
                    : battlefront.HasFrontage
                    ? Math.Max(350d, battlefront.FrontageWidth)
                    : Math.Max(350d, Math.Max(1, nodeCount) * 180d);
                if (defensiveAnchor.CorridorAnchor)
                {
                    width = Math.Min(width, Math.Max(350d, defensiveAnchor.FrontageWidth));
                }
                else if (IsChokeObjective(objective))
                {
                    width = Math.Min(width, 450d);
                }
                // A terrain-line anchor (BC12) has its own measured frontage; the unanchored terrain objective gets the 700 m default.
                if (!defensiveAnchor.CorridorAnchor && !defensiveAnchor.OnTerrainLine && IsTerrainAnchor(objective))
                {
                    width = Math.Max(width, 700d);
                }

                double half = width * 0.5d;
                double laneScale = defensiveAnchor.CorridorAnchor || IsChokeObjective(objective) ? 0.65d : 1d;
                // Every defensive anchor (a corridor intercept, a generic approach intercept, a terrain line) is stood on its own side, the
                // side away from the approach; the Approach() geometry (in front of the point, toward the enemy) is for an objective with no
                // anchor. A generic approach intercept used to take Approach() and formed 210 m in front of its own intercept point.
                bool defensiveSideTargets = defensiveAnchor.HasAnchor;
                // A corridor intercept is a line to form up behind (AssemblyDistance back); a terrain line is the line to stand on: the
                // line slots take the anchor itself, which already sits at the crest's forward edge, 80 m behind the crest under
                // ReverseSlopeDefense, or on the wall / river line (inside vanilla's cover snap). Depth slots keep their distance.
                double assemblyStandOff = defensiveAnchor.OnTerrainLine || defensiveAnchor.OnOwnFront ? 0d : AssemblyDistance;
                LineFrame(in defensiveAnchor, direction, out double lineX, out double lineZ, out double lineHalf, out double lineCos);
                DoctrineTargetPoint artillery = defensiveAnchor.HasAnchor && defensiveAnchor.DefensiveArtilleryDepth > 0f
                    ? BehindObjective(objective, direction, defensiveAnchor.DefensiveArtilleryDepth, 0d)
                    : Approach(objective, direction, ArtilleryLineDistance, 0d);
                string suffix = battlefront.HasFrontage ? ":frontage" : ":generated-frontage";
                if (direction.UsesApproachAvenue) suffix += ":approach-avenue";
                if (defensiveAnchor.HasAnchor) suffix += ":" + defensiveAnchor.Reason;
                if (defensiveSideTargets) suffix += defensiveAnchor.OnTerrainLine ? ":defensive-side-terrain-line" : defensiveAnchor.OnOwnFront ? ":defensive-side-own-front" : defensiveAnchor.CorridorAnchor ? ":defensive-side-corridor" : ":defensive-side-intercept";
                if (!defensiveAnchor.CorridorAnchor && !defensiveAnchor.OnTerrainLine && IsTerrainAnchor(objective)) suffix += ":terrain-anchor";
                if (defensiveAnchor.CorridorAnchor || IsChokeObjective(objective)) suffix += ":objective-lane";
                return new FrontageGeometry(objective, direction, half, laneScale, defensiveSideTargets, assemblyStandOff, lineX, lineZ, lineHalf, lineCos, artillery, suffix);
            }

            private static bool IsTerrainAnchor(BattlefieldObjectiveEstimate objective)
            {
                return objective.Type == TacticalObjectiveType.Ridge ||
                    objective.Type == TacticalObjectiveType.Town ||
                    objective.TerrainStrength >= TerrainPicture.AnchorStrength;
            }

            private static bool IsChokeObjective(BattlefieldObjectiveEstimate objective)
            {
                return objective.Type == TacticalObjectiveType.Bridge ||
                    objective.Type == TacticalObjectiveType.Ford ||
                    objective.Type == TacticalObjectiveType.RoadJunction ||
                    objective.Type == TacticalObjectiveType.ChokePoint;
            }
        }

        private readonly struct Direction
        {
            private Direction(double x, double z)
                : this(x, z, false)
            {
            }

            private Direction(double x, double z, bool usesApproachAvenue)
            {
                X = x;
                Z = z;
                LateralX = -z;
                LateralZ = x;
                UsesApproachAvenue = usesApproachAvenue;
            }

            public double X { get; }
            public double Z { get; }
            public double LateralX { get; }
            public double LateralZ { get; }
            public bool UsesApproachAvenue { get; }

            public static Direction FromNodeToObjective(
                CommandNodeOperationalState node,
                BattlefieldObjectiveEstimate objective)
            {
                if (HasUsableAvenue(objective))
                {
                    double ax = objective.ApproachAvenue.AxisX;
                    double az = objective.ApproachAvenue.AxisZ;
                    double avenueLength = Math.Sqrt(ax * ax + az * az);
                    if (!double.IsNaN(avenueLength) &&
                        !double.IsInfinity(avenueLength) &&
                        avenueLength >= 0.001d)
                    {
                        return new Direction(ax / avenueLength, az / avenueLength, true);
                    }
                }

                double dx = (double)objective.X - node.X;
                double dz = (double)objective.Z - node.Z;
                double length = Math.Sqrt(dx * dx + dz * dz);
                if (double.IsNaN(length) || double.IsInfinity(length) || length < 1d)
                {
                    double radians = node.FacingDegrees / 180d * Math.PI;
                    double facingX = Math.Sin(radians);
                    double facingZ = Math.Cos(radians);
                    length = Math.Sqrt(facingX * facingX + facingZ * facingZ);
                    return length < 0.001d
                        ? new Direction(0d, 1d)
                        : new Direction(facingX / length, facingZ / length);
                }

                return new Direction(dx / length, dz / length);
            }
        }
    }
}
