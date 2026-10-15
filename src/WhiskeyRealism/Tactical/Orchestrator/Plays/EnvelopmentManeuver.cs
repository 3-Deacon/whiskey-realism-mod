// BC11 decision 4 (docs/superpowers/plans/2026-10-15-battle-core.md § BC11): the envelopments the geometric
// plays choose are executed, not only relabelled. Pure: no Unity or vanilla types; the runtime half
// (PlayExecutorRuntime.StepManeuvers) reads the snapshot, the terrain grid and the terrain heights into a
// ManeuverInput and keeps one ManeuverState per child.
//
// Vanilla anchors (decompile, build 14703111):
//   FlankMove 116929-117108: a two-leg hook at +-45 degrees that ends 45 degrees inward; it aborts on river
//     id 4 on either leg (CheckTerrainLine(..., {4}, 2f), 117011-117036) or when the first enemy unit along
//     leg 2 (the leg plus 1.5 x fire range) stands at least heightdifferencetoenemyunitanchored (5 m) above
//     the turn point (117038-117050). CheckFlankMoves 6247-6312 is vanilla's own trigger; this is Whiskey's
//     plan-driven one, with the same two checks.
//   Whiskey's per-child state machine: Hold -> March -> Turn -> Attack, abandoned on a failed check, lost
//     strength, MaxManeuverHours or a plan change. Every timer reads the doctrine clock (NowHours), never
//     real time.

using System;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    public enum ManeuverPhase
    {
        None,
        Hold,
        March,
        Turn,
        Attack,
        Released,   // a start-delay wing back on its allocator role
        Abandoned,
    }

    /// <summary>The plan of a zone assignment that carries a maneuver (DirectChildIntent.Maneuver).</summary>
    public readonly struct ManeuverSpec
    {
        public ManeuverSpec(PlayRectKind kind, float anchorX, float anchorZ, float startOffsetSeconds, bool envelops)
        {
            HasValue = true;
            Kind = kind;
            AnchorX = anchorX;
            AnchorZ = anchorZ;
            StartOffsetSeconds = startOffsetSeconds;
            Envelops = envelops;
        }

        public bool HasValue { get; }
        public PlayRectKind Kind { get; }
        public float AnchorX { get; }
        public float AnchorZ { get; }
        public float StartOffsetSeconds { get; }
        public bool Envelops { get; }
    }

    public struct ManeuverInput
    {
        public PlayRectKind Kind;
        public bool Envelops;
        public float AnchorX, AnchorZ, StartOffsetSeconds;
        public bool TemplateHasCenter;
        public double PlayActivatedHours, CenterContactHours;            // CenterContactHours < 0 until the Center child sees an enemy within ContactMeters
        public float ChildX, ChildZ;
        public int ChildStrengthBucket;
        public bool ChildArrived, ChildInContact;
        public bool HasEnemyExtent;
        public float EnemyEndX, EnemyEndZ, EnemyAnchorX, EnemyAnchorZ, OutflankX, OutflankZ;   // the rect side's perceived line end; DeepRaid's outflank point
        public bool InwardLegHitsRiver, MarchLegHitsRiver;               // BattleTerrainGrid samples (2 m inward, 10 m for the march)
        public float AnchorHeight;
        public int CorridorEnemyCount;                                    // perceived enemies (Visible, or Remembered at >= 0.5) on the inward corridor
        public float MaxCorridorEnemyHeight;
        public float HeightDifferenceAnchored;                            // GamePrefs.heightdifferencetoenemyunitanchored (5 m)
        public bool CanMirror;                                            // a lone enveloping rect: one mirrored try
        public BattleStance Stance;
        public double NowHours;
    }

    public struct ManeuverState
    {
        public ManeuverPhase Phase;
        public double PhaseSinceHours;
        public bool Mirrored;
        public string AbandonReason;
        // Additive: when the march began (MaxManeuverHours counts from it) and whether a DeepRaid reached its outflank point.
        public double ActiveSinceHours;
        public bool OutflankReached;
    }

    public struct ManeuverOrder
    {
        public ManeuverPhase Phase;
        public CommandTaskType Task;
        public float TargetX, TargetZ, Heading;   // Heading -1 = none
        public string Reason;                      // static literal

        /// <summary>The phases that override the child's doctrine order.</summary>
        public bool Overrides => Phase == ManeuverPhase.Hold || Phase == ManeuverPhase.March || Phase == ManeuverPhase.Turn || Phase == ManeuverPhase.Attack;
    }

    /// <summary>One child's maneuver order with the lateral axis its nodes are laid out on.</summary>
    public readonly struct ChildManeuver
    {
        public ChildManeuver(string childId, ManeuverOrder order, float lateralX, float lateralZ)
        {
            ChildId = childId ?? string.Empty;
            Order = order;
            LateralX = lateralX;
            LateralZ = lateralZ;
        }

        public string ChildId { get; }
        public ManeuverOrder Order { get; }
        public float LateralX { get; }
        public float LateralZ { get; }
    }

    public static class EnvelopmentManeuver
    {
        public const float ArrivalMeters = 120f;
        public const float OutflankMarginMeters = 300f;
        public const float LaneMeters = 180f;
        public const float TurnHoldHours = 0.05f;
        public const float MaxManeuverHours = 1.0f;
        public const float ContactMeters = 250f;
        /// <summary>The inward leg's corridor is the leg plus this much, <see cref="CorridorWidthMeters"/> wide.</summary>
        public const float CorridorExtraMeters = 150f;
        public const float CorridorWidthMeters = 100f;
        /// <summary>A remembered enemy counts for the height check only at this confidence (the covered-crossing rule).</summary>
        public const float RememberedConfidence = 0.5f;
        /// <summary>FlankMove's river check step on the inward leg (CheckTerrainLine(..., 2f)).</summary>
        public const float InwardStepMeters = 2f;
        public const float MarchStepMeters = 10f;

        // ---- the state machine ---------------------------------------------------------------

        public static ManeuverOrder Step(ref ManeuverState state, in ManeuverInput input)
        {
            // Offensive playbooks are never selected for a Defender and the maneuver never runs for one.
            if (input.Stance == BattleStance.Defender)
            {
                state = default(ManeuverState);
                return Done(ManeuverPhase.None, "defender");
            }
            if (state.Phase == ManeuverPhase.Abandoned) return Done(ManeuverPhase.Abandoned, state.AbandonReason ?? "abandoned");
            if (state.Phase == ManeuverPhase.Released) return Done(ManeuverPhase.Released, "start-delay-over");

            return input.Envelops ? StepEnvelopment(ref state, in input) : StepStartDelay(ref state, in input);
        }

        // A wing that only has a start delay (HesitatedCommit 600 battle-s, GenericMethodical 300): it holds for that
        // long after the play's activation and then returns to its allocator role. It never marches, turns or attacks.
        private static ManeuverOrder StepStartDelay(ref ManeuverState state, in ManeuverInput input)
        {
            if (!(input.StartOffsetSeconds > 0f))
            {
                state = default(ManeuverState);
                return Done(ManeuverPhase.None, "no-start-delay");
            }
            if (state.Phase == ManeuverPhase.None)
            {
                state.Phase = ManeuverPhase.Hold;
                state.PhaseSinceHours = input.NowHours;
            }
            double elapsedSeconds = (input.NowHours - input.PlayActivatedHours) * TacticalBattleClock.BattleSecondsPerHour;
            if (elapsedSeconds >= input.StartOffsetSeconds)
            {
                state.Phase = ManeuverPhase.Released;
                state.PhaseSinceHours = input.NowHours;
                return Done(ManeuverPhase.Released, "start-delay-over");
            }
            return HoldOrder(in input, "start-delay");
        }

        private static ManeuverOrder StepEnvelopment(ref ManeuverState state, in ManeuverInput input)
        {
            if (state.Phase == ManeuverPhase.None)
            {
                state.Phase = ManeuverPhase.Hold;
                state.PhaseSinceHours = input.NowHours;
            }

            if (state.Phase == ManeuverPhase.Hold)
            {
                if (input.TemplateHasCenter && input.CenterContactHours < 0.0) return HoldOrder(in input, "await-center-contact");
                double reference = input.TemplateHasCenter ? Math.Max(input.PlayActivatedHours, input.CenterContactHours) : input.PlayActivatedHours;
                double goHours = reference + input.StartOffsetSeconds / (double)TacticalBattleClock.BattleSecondsPerHour;
                if (input.NowHours < goHours) return HoldOrder(in input, "hold-offset");

                string failure = FlankMoveCheck(in input);
                if (failure != null)
                {
                    if (input.CanMirror && !state.Mirrored)
                    {
                        // The runtime reads Mirrored and supplies the mirrored side's anchor and checks next tick.
                        state.Mirrored = true;
                        return HoldOrder(in input, "mirror-flank");
                    }
                    return Abandon(ref state, failure);
                }
                state.Phase = ManeuverPhase.March;
                state.PhaseSinceHours = input.NowHours;
                state.ActiveSinceHours = input.NowHours;
            }

            // March, Turn and Attack share the abandon rules.
            if (input.ChildStrengthBucket <= 0) return Abandon(ref state, "lost-strength");
            if (input.NowHours - state.ActiveSinceHours >= MaxManeuverHours) return Abandon(ref state, "timeout");
            if (state.Phase != ManeuverPhase.Attack)
            {
                string failure = FlankMoveCheck(in input);
                if (failure != null)
                {
                    if (input.CanMirror && !state.Mirrored)
                    {
                        state.Mirrored = true;
                        return MarchOrder(ref state, in input);
                    }
                    return Abandon(ref state, failure);
                }
            }

            switch (state.Phase)
            {
                case ManeuverPhase.March:
                {
                    ManeuverOrder march = MarchOrder(ref state, in input);
                    if (input.ChildArrived && (input.Kind != PlayRectKind.DeepRaid || state.OutflankReached))
                    {
                        state.Phase = ManeuverPhase.Turn;
                        state.PhaseSinceHours = input.NowHours;
                        return TurnOrder(in input);
                    }
                    return march;
                }
                case ManeuverPhase.Turn:
                    if (input.ChildInContact || input.NowHours - state.PhaseSinceHours >= TurnHoldHours)
                    {
                        state.Phase = ManeuverPhase.Attack;
                        state.PhaseSinceHours = input.NowHours;
                        return AttackOrder(in input);
                    }
                    return TurnOrder(in input);
                default:
                    return AttackOrder(in input);
            }
        }

        // The march target: the rect anchor; a DeepRaid goes first to its outflank point.
        private static ManeuverOrder MarchOrder(ref ManeuverState state, in ManeuverInput input)
        {
            bool raid = input.Kind == PlayRectKind.DeepRaid;
            if (raid && !state.OutflankReached && Distance(input.ChildX, input.ChildZ, input.OutflankX, input.OutflankZ) <= ArrivalMeters)
                state.OutflankReached = true;
            bool outflank = raid && !state.OutflankReached;
            return new ManeuverOrder
            {
                Phase = ManeuverPhase.March, Task = CommandTaskType.AdvanceToAssembly,
                TargetX = outflank ? input.OutflankX : input.AnchorX, TargetZ = outflank ? input.OutflankZ : input.AnchorZ,
                Heading = -1f, Reason = outflank ? "march-outflank" : "march-anchor",
            };
        }

        // On arrival: face the perceived enemy line end on the rect's side (the play's enemy anchor with no extent).
        private static ManeuverOrder TurnOrder(in ManeuverInput input)
        {
            LineEnd(in input, out float ex, out float ez);
            return new ManeuverOrder
            {
                Phase = ManeuverPhase.Turn, Task = CommandTaskType.AdvanceToAssembly, TargetX = input.AnchorX, TargetZ = input.AnchorZ,
                Heading = PostureSense.HeadingTo(input.ChildX, input.ChildZ, ex, ez), Reason = "turn-to-line-end",
            };
        }

        private static ManeuverOrder AttackOrder(in ManeuverInput input)
        {
            LineEnd(in input, out float ex, out float ez);
            return new ManeuverOrder
            {
                Phase = ManeuverPhase.Attack, Task = CommandTaskType.AttackObjective, TargetX = ex, TargetZ = ez, Heading = -1f,
                Reason = "attack-line-end",
            };
        }

        private static ManeuverOrder HoldOrder(in ManeuverInput input, string reason)
        {
            return new ManeuverOrder
            {
                Phase = ManeuverPhase.Hold, Task = CommandTaskType.FormUp, TargetX = input.ChildX, TargetZ = input.ChildZ, Heading = -1f, Reason = reason,
            };
        }

        private static ManeuverOrder Done(ManeuverPhase phase, string reason)
        {
            return new ManeuverOrder { Phase = phase, Task = CommandTaskType.None, Heading = -1f, Reason = reason };
        }

        private static ManeuverOrder Abandon(ref ManeuverState state, string reason)
        {
            state.Phase = ManeuverPhase.Abandoned;
            state.AbandonReason = reason;
            return Done(ManeuverPhase.Abandoned, reason);
        }

        private static void LineEnd(in ManeuverInput input, out float x, out float z)
        {
            x = input.HasEnemyExtent ? input.EnemyEndX : input.EnemyAnchorX;
            z = input.HasEnemyExtent ? input.EnemyEndZ : input.EnemyAnchorZ;
        }

        // FlankMove's two checks (116929-117108) plus the march leg: a river on either leg, or a perceived enemy on
        // the inward corridor standing at least heightdifferencetoenemyunitanchored above the anchor.
        private static string FlankMoveCheck(in ManeuverInput input)
        {
            if (input.MarchLegHitsRiver) return "river-march";
            if (input.InwardLegHitsRiver) return "river-inward";
            Span<float> heights = stackalloc float[1];
            heights[0] = input.MaxCorridorEnemyHeight;
            if (AnchoredHighGround(input.AnchorHeight, heights, input.CorridorEnemyCount > 0 ? 1 : 0, input.HeightDifferenceAnchored))
                return "high-ground";
            return null;
        }

        // ---- the checks ----------------------------------------------------------------------

        /// <summary>
        /// FlankMove's river check (CheckTerrainLine with {4} at 2 m steps, 117011-117036) over the terrain ids
        /// sampled along a leg: false when any sample is river id 4 (fords and bridges are other ids).
        /// </summary>
        public static bool InwardLegClear(ReadOnlySpan<int> riverSamples, int count)
        {
            int n = Math.Min(Math.Max(count, 0), riverSamples.Length);
            for (int i = 0; i < n; i++)
                if (riverSamples[i] == BattleTerrainGrid.RiverId) return false;
            return true;
        }

        /// <summary>
        /// FlankMove's anchored-enemy check (117038-117050): any perceived enemy height at least
        /// <paramref name="heightDifference"/> above the anchor (the turn point). At exactly the difference it fails.
        /// </summary>
        public static bool AnchoredHighGround(float anchorHeight, ReadOnlySpan<float> enemyHeights, int count, float heightDifference)
        {
            int n = Math.Min(Math.Max(count, 0), enemyHeights.Length);
            for (int i = 0; i < n; i++)
                if (enemyHeights[i] - anchorHeight >= heightDifference) return true;
            return false;
        }

        // ---- geometry (axis frame) -------------------------------------------------------------

        /// <summary>True when the rect anchor lies on the right of the army's centre line (the lateral centre of its extent).</summary>
        public static bool RectIsRight(in TacticalFrontAxis axis, float anchorX, float anchorZ)
        {
            float centre = axis.IsValid ? (axis.LateralMin + axis.LateralMax) * 0.5f : 0f;
            return axis.LateralOf(anchorX, anchorZ) >= centre;
        }

        /// <summary>
        /// The perceived enemy line end on a side, at the enemy's depth: the axis's EnemyLateralMax (right) or
        /// EnemyLateralMin (left) laid out from the origin at the depth of the play's enemy anchor. False with no enemy extent.
        /// </summary>
        public static bool EnemyLineEnd(in TacticalFrontAxis axis, float enemyAnchorX, float enemyAnchorZ, bool rightSide, out float x, out float z)
        {
            x = enemyAnchorX;
            z = enemyAnchorZ;
            if (!axis.IsValid || !axis.HasEnemyExtent) return false;
            float depth = Forward(in axis, enemyAnchorX, enemyAnchorZ);
            float lateral = rightSide ? axis.EnemyLateralMax : axis.EnemyLateralMin;
            Lay(in axis, depth, lateral, out x, out z);
            return true;
        }

        /// <summary>
        /// The DeepRaid's outflank point: OutflankMarginMeters beyond the perceived enemy line end on the
        /// less-pressured side (the enemy half with the smaller perceived strength; a tie keeps the rect's own side), at
        /// <paramref name="depth"/> along the forward axis.
        /// </summary>
        public static bool OutflankPoint(in TacticalFrontAxis axis, float depth, float leftPressure, float rightPressure, bool rectIsRight,
            out float x, out float z)
        {
            x = 0f;
            z = 0f;
            if (!axis.IsValid || !axis.HasEnemyExtent) return false;
            bool right = leftPressure == rightPressure ? rectIsRight : rightPressure < leftPressure;
            float lateral = right ? axis.EnemyLateralMax + OutflankMarginMeters : axis.EnemyLateralMin - OutflankMarginMeters;
            Lay(in axis, depth, lateral, out x, out z);
            return true;
        }

        /// <summary>The perceived enemy strength (Visible full, Remembered x confidence) on each half of the enemy extent.</summary>
        public static void EnemyPressureSplit(BattleSnapshot s, in TacticalFrontAxis axis, out float left, out float right)
        {
            left = 0f;
            right = 0f;
            if (s == null || !axis.IsValid || !axis.HasEnemyExtent) return;
            float mid = (axis.EnemyLateralMin + axis.EnemyLateralMax) * 0.5f;
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].IsReinforcement) continue;
                float weight = obs[k].State == ObservationState.Visible ? obs[k].Strength : obs[k].Strength * obs[k].Confidence01;
                if (axis.LateralOf(obs[k].X, obs[k].Z) < mid) left += weight;
                else right += weight;
            }
        }

        /// <summary>Mirrors a point across the army's centre line (its lateral offset from the origin flips sign).</summary>
        public static void MirrorAcrossAxis(in TacticalFrontAxis axis, float x, float z, out float mirroredX, out float mirroredZ)
        {
            mirroredX = x;
            mirroredZ = z;
            if (!axis.IsValid) return;
            Lay(in axis, Forward(in axis, x, z), -axis.LateralOf(x, z), out mirroredX, out mirroredZ);
        }

        /// <summary>
        /// The perceived enemies on the inward leg's corridor (leg length plus CorridorExtraMeters, CorridorWidthMeters
        /// wide; Visible, or Remembered at RememberedConfidence and over): how many, and the highest terrain height
        /// among them (heightAt is the runtime's BattlefieldSetup.GetTerrainHeight; terrain is public, the enemy position
        /// is perceived). maxHeight is float.MinValue with none.
        /// </summary>
        public static int CorridorEnemies(BattleSnapshot s, float anchorX, float anchorZ, float endX, float endZ,
            Func<float, float, float> heightAt, out float maxHeight)
        {
            maxHeight = float.MinValue;
            if (s == null || heightAt == null) return 0;
            float dx = endX - anchorX, dz = endZ - anchorZ;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 1f) return 0;
            float ux = dx / length, uz = dz / length;
            float reach = length + CorridorExtraMeters;
            float half = CorridorWidthMeters * 0.5f;
            int count = 0;
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].IsReinforcement) continue;
                if (obs[k].State != ObservationState.Visible && obs[k].Confidence01 < RememberedConfidence) continue;
                float px = obs[k].X - anchorX, pz = obs[k].Z - anchorZ;
                float along = px * ux + pz * uz;
                if (along < 0f || along > reach) continue;
                float across = px * -uz + pz * ux;
                if (Math.Abs(across) > half) continue;
                count++;
                float height = heightAt(obs[k].X, obs[k].Z);
                if (height > maxHeight) maxHeight = height;
            }
            return count;
        }

        // ---- lanes and nodes ------------------------------------------------------------------

        /// <summary>The lane offset of node <paramref name="index"/> of <paramref name="count"/> along the rect's lateral axis, centred on the anchor.</summary>
        public static float LaneOffset(int index, int count)
        {
            if (count <= 1 || index < 0) return 0f;
            return (index - (count - 1) * 0.5f) * LaneMeters;
        }

        /// <summary>"child-&lt;id&gt;" (DirectChildDiscovery) to the command-tree node id "node-&lt;id&gt;" (CommandTreeIntentAllocator).</summary>
        public static string NodeIdOfChild(string childId)
        {
            const string childPrefix = "child-";
            if (string.IsNullOrEmpty(childId) || !childId.StartsWith(childPrefix, StringComparison.Ordinal)) return string.Empty;
            return "node-" + childId.Substring(childPrefix.Length);
        }

        /// <summary>
        /// Expands the children's maneuver orders to every node of each child's subtree (a node whose
        /// CommandNodeIntent.SourceNodeId is the child's node): task, lane target on the rect's lateral axis, heading.
        /// A Hold carries no target (the child stands where it is). Phases that do not override add nothing.
        /// </summary>
        internal static ManeuverNodeTable ExpandToNodes(System.Collections.Generic.IReadOnlyList<ChildManeuver> maneuvers,
            System.Collections.Generic.IReadOnlyList<CommandNodeIntent> nodeIntents)
        {
            var table = new ManeuverNodeTable();
            if (maneuvers == null || nodeIntents == null) return table;
            for (int m = 0; m < maneuvers.Count; m++)
            {
                ChildManeuver maneuver = maneuvers[m];
                if (!maneuver.Order.Overrides) continue;
                string source = NodeIdOfChild(maneuver.ChildId);
                if (source.Length == 0) continue;
                int nodes = 0;
                for (int i = 0; i < nodeIntents.Count; i++)
                    if (string.Equals(nodeIntents[i].SourceNodeId, source, StringComparison.Ordinal)) nodes++;
                int lane = 0;
                for (int i = 0; i < nodeIntents.Count; i++)
                {
                    if (!string.Equals(nodeIntents[i].SourceNodeId, source, StringComparison.Ordinal)) continue;
                    bool hold = maneuver.Order.Phase == ManeuverPhase.Hold;
                    float offset = hold ? 0f : LaneOffset(lane, nodes);
                    table.Add(new ManeuverNodeEntry(
                        nodeIntents[i].NodeId, maneuver.Order.Phase, maneuver.Order.Task, !hold,
                        maneuver.Order.TargetX + maneuver.LateralX * offset, maneuver.Order.TargetZ + maneuver.LateralZ * offset,
                        maneuver.Order.Heading, maneuver.Order.Reason));
                    lane++;
                }
            }
            return table;
        }

        // A hash of what the doctrine build reads from the maneuvers, so a phase change triggers a ledger tick.
        internal static int Hash(System.Collections.Generic.IReadOnlyList<ChildManeuver> maneuvers)
        {
            if (maneuvers == null) return 0;
            unchecked
            {
                int h = 17;
                for (int m = 0; m < maneuvers.Count; m++)
                {
                    ChildManeuver c = maneuvers[m];
                    h = h * 31 + (c.ChildId?.GetHashCode() ?? 0);
                    h = h * 31 + (int)c.Order.Phase;
                    h = h * 31 + (int)c.Order.Task;
                    h = h * 31 + (int)(c.Order.TargetX / 50f);
                    h = h * 31 + (int)(c.Order.TargetZ / 50f);
                    h = h * 31 + (int)(c.Order.Heading / 5f);
                }
                return h;
            }
        }

        private static float Forward(in TacticalFrontAxis axis, float x, float z)
        {
            return (x - axis.OriginX) * axis.ForwardX + (z - axis.OriginZ) * axis.ForwardZ;
        }

        private static void Lay(in TacticalFrontAxis axis, float depth, float lateral, out float x, out float z)
        {
            x = axis.OriginX + axis.ForwardX * depth + axis.RightX * lateral;
            z = axis.OriginZ + axis.ForwardZ * depth + axis.RightZ * lateral;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
