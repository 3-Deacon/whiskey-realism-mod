using System;

namespace WhiskeyRealism.Tactical.Battle
{
    public enum AssumedEnemySource
    {
        /// <summary>The strength-weighted centroid of the Visible observations (FogGrid, OwnSideWithinFog).</summary>
        Visible,
        /// <summary>The nearest enemy-owned entry point (public scenario data).</summary>
        EntryPoint,
        /// <summary>The map centre bearing DoUnitPositioning already used (87789-87790).</summary>
        MapCentre,
    }

    public enum PlacementKind
    {
        /// <summary>Vanilla's placement stands.</summary>
        Kept,
        /// <summary>Re-placed on the fog-honest objective and facing.</summary>
        Replaced,
        /// <summary>Moved back as part of the deployment reserve echelon.</summary>
        Reserve,
    }

    /// <summary>
    /// One AI group vanilla placed in this DoPlacement call. All positions are in the line-front
    /// frame (the targetpos SetGroupFormation takes, 92044-92058): VanillaX/Z is the group's
    /// placed headquarters converted back to its line front, OldX/Z the headquarters before the call.
    /// </summary>
    public struct DeploymentGroup
    {
        public int GroupId;
        public int DirectChildId;          // the army direct child that holds the group (0 = none)
        public float VanillaX, VanillaZ, VanillaHeading;
        public float OldX, OldZ;
        public float DepthMeters;          // groupposition.groupdepth (later-day front shift, 85795-85803)
        public int Brigades;               // brigadesinaigroup: the spacing radius is 25 m per brigade
        public bool PlayerOwned;           // the group's direct child holds player-owned units: never a reserve
        public bool CavalryOnly, ArtilleryOnly;
        public bool WlCommandAncestor;     // an ancestor of the W&L current command: its child is never a reserve
        // The pass must not touch this group (the W&L command's AI group): it keeps vanilla's
        // placement and still holds the objective vanilla gave it. Its child is never a reserve.
        public bool Locked;
    }

    public struct DeploymentObjective
    {
        public float X, Z;
        public int Owner;                  // -1 neutral
    }

    public struct DeploymentPlacement
    {
        public int GroupId;
        public int ObjectiveIndex;         // -1: vanilla's placement stands without a candidate objective
        public float X, Z, Heading;
        public PlacementKind Kind;
    }

    public sealed class DeploymentPlan
    {
        public DeploymentPlacement[] Placements = new DeploymentPlacement[64];
        public int Count;
        public float AssumedEnemyX, AssumedEnemyZ;
        public AssumedEnemySource Source;

        // The placements before the reserve proposal, and the reserve axis, for the clamp's revert.
        internal DeploymentPlacement[] Base = new DeploymentPlacement[64];
        internal bool[] ObjectiveBlocked = new bool[64];
        internal bool[] ObjectiveUsed = new bool[64];
        internal float LineCentroidX, LineCentroidZ, AxisX, AxisZ;
        internal bool HasReserve;
        internal int ReserveGroupCount;

        internal void EnsureCapacity(int groups, int objectives)
        {
            if (Placements.Length < groups)
            {
                Placements = new DeploymentPlacement[Math.Max(groups, Placements.Length * 2)];
                Base = new DeploymentPlacement[Placements.Length];
            }
            if (ObjectiveBlocked.Length < objectives)
            {
                ObjectiveBlocked = new bool[Math.Max(objectives, ObjectiveBlocked.Length * 2)];
                ObjectiveUsed = new bool[ObjectiveBlocked.Length];
            }
        }
    }

    // Fog-honest AI deployment (BC10 decisions 7 and 8), pure. Vanilla's DoPlacementAIUnits
    // WithinDeploymentzoneNew (85524-85892) places each AI group from the true enemy: the
    // candidate objectives exclude any with an active enemy unit within 50 m, scanned over the
    // whole unit list with no fog (85604-85618); day 1 picks the candidate nearest to any enemy
    // battle-data group (GetSuitableObjectiveForInitialAIPlacement 86260-86281); the facing is
    // the bearing to unitrange.closestenemyunitfar, which has no fog filter (85809-85815,
    // 122658-122663). This plans the same choices from what the side can know:
    //   - the assumed enemy point is the nearest enemy-owned entry point to the group when the
    //     side perceives nothing (RangeOnly: every day-1 deployment, and vanilla's range lists
    //     admit enemies within 150-450 m with no line-of-sight test, 122650), else the
    //     strength-weighted centroid of what it sees, then the entry point, then the map
    //     centre;
    //   - the objective is the nearest candidate to that point on day 1, to the group on later
    //     days (GetSuitableObjectiveForAIPlacement 86285-86301), each objective used once;
    //   - vanilla's placement stands when it already lies within its spacing radius of the same
    //     front and faces within 20 degrees.
    // A deployment reserve echelon then moves a third of the eligible direct children 425 m
    // behind the line (vanilla has no reserve until after state 4, AssignReserves 7017).
    public static class DeploymentDoctrine
    {
        public const float FacingToleranceDegrees = 20f;
        public const float ReserveDepthMeters = 425f;
        public const float MinReserveDepthMeters = 150f;
        public const float ReserveLaneMeters = 90f;
        public const float ObjectiveDetectionMeters = 50f;
        public const float SpacingPerBrigadeMeters = 25f;
        public const int SpacingTries = 8;
        public const float SpacingStepDegrees = 45f;
        public const int MinChildrenForReserve = 3;

        /// <summary>
        /// The point this side's deployment assumes the enemy is at, seen from (gx, gz).
        /// RangeOnly perception: the nearest enemy-owned entry point, else the map centre (no
        /// observation is used). Otherwise: the strength-weighted centroid of the Visible
        /// observations, then the entry point, then the map centre.
        /// </summary>
        public static bool AssumedEnemyPoint(BattleSnapshot s, float gx, float gz,
            ReadOnlySpan<float> enemyEntryX, ReadOnlySpan<float> enemyEntryZ, int entries,
            float mapCentreX, float mapCentreZ, out float x, out float z, out AssumedEnemySource source)
        {
            if (s != null && s.Perception != PerceptionMode.RangeOnly && s.VisibleEnemyStrength > 0f)
            {
                x = s.VisibleEnemyCentroidX;
                z = s.VisibleEnemyCentroidZ;
                source = AssumedEnemySource.Visible;
                return true;
            }
            entries = Math.Min(entries, Math.Min(enemyEntryX.Length, enemyEntryZ.Length));
            int best = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < entries; i++)
            {
                float dx = enemyEntryX[i] - gx;
                float dz = enemyEntryZ[i] - gz;
                float d = dx * dx + dz * dz;
                if (d < bestSq) { bestSq = d; best = i; }
            }
            if (best >= 0)
            {
                x = enemyEntryX[best];
                z = enemyEntryZ[best];
                source = AssumedEnemySource.EntryPoint;
                return true;
            }
            x = mapCentreX;
            z = mapCentreZ;
            source = AssumedEnemySource.MapCentre;
            return true;
        }

        /// <summary>
        /// Plans where each group goes: placements in the order of <paramref name="groups"/>.
        /// Zone matrices are [objective, group] as vanilla's CheckIfWithinZone(objective, alliance,
        /// group) returns them (85771-85774). Returns the number of Replaced placements.
        /// </summary>
        public static int PlanPlacements(BattleSnapshot s, DeploymentGroup[] groups, int groupCount,
            DeploymentObjective[] objectives, int objectiveCount, bool[,] objectiveInOwnZone, bool[,] objectiveInEnemyZone,
            int ownAlliance, bool firstDay, ReadOnlySpan<float> enemyEntryX, ReadOnlySpan<float> enemyEntryZ, int entries,
            float mapCentreX, float mapCentreZ, DeploymentPlan into)
        {
            if (into == null) return 0;
            into.Count = 0;
            into.HasReserve = false;
            into.ReserveGroupCount = 0;
            if (groups == null || groupCount <= 0) return 0;
            objectiveCount = objectives != null ? Math.Min(objectiveCount, objectives.Length) : 0;
            into.EnsureCapacity(groupCount, objectiveCount);

            // The assumed point for the plan's headline (the first group's), per group below.
            AssumedEnemyPoint(s, groups[0].OldX, groups[0].OldZ, enemyEntryX, enemyEntryZ, entries, mapCentreX, mapCentreZ,
                out into.AssumedEnemyX, out into.AssumedEnemyZ, out into.Source);

            // Vanilla's candidate list (list2) is built once: objectives with no enemy within 50 m.
            // Fog-honest: Visible observations under a painted grid, enemy-owned entry points
            // under RangeOnly (85604-85618).
            bool rangeOnly = s == null || s.Perception == PerceptionMode.RangeOnly;
            for (int o = 0; o < objectiveCount; o++)
            {
                into.ObjectiveUsed[o] = false;
                into.ObjectiveBlocked[o] = rangeOnly
                    ? NearEntry(objectives[o].X, objectives[o].Z, enemyEntryX, enemyEntryZ, entries)
                    : NearVisible(s, objectives[o].X, objectives[o].Z);
            }

            int replaced = 0;
            int placed = 0;
            for (int g = 0; g < groupCount; g++)
            {
                ref DeploymentGroup group = ref groups[g];
                if (group.Locked)
                {
                    HoldObjectiveOf(objectives, objectiveCount, into, group);
                    into.Placements[g] = new DeploymentPlacement
                    {
                        GroupId = group.GroupId, ObjectiveIndex = -1, X = group.VanillaX, Z = group.VanillaZ,
                        Heading = group.VanillaHeading, Kind = PlacementKind.Kept,
                    };
                    placed++;
                    continue;
                }
                AssumedEnemyPoint(s, group.OldX, group.OldZ, enemyEntryX, enemyEntryZ, entries, mapCentreX, mapCentreZ,
                    out float ex, out float ez, out AssumedEnemySource source);

                int pick = PickObjective(objectives, objectiveCount, into, objectiveInOwnZone, objectiveInEnemyZone, g,
                    ownAlliance, firstDay, ex, ez, group.OldX, group.OldZ);
                var placement = new DeploymentPlacement
                {
                    GroupId = group.GroupId, ObjectiveIndex = pick, X = group.VanillaX, Z = group.VanillaZ,
                    Heading = group.VanillaHeading, Kind = PlacementKind.Kept,
                };
                if (pick >= 0)
                {
                    into.ObjectiveUsed[pick] = true;
                    float heading = Bearing(objectives[pick].X, objectives[pick].Z, ex, ez);
                    float fx = objectives[pick].X;
                    float fz = objectives[pick].Z;
                    if (!firstDay)
                    {
                        // Later days: the front stands one group depth behind the objective (85795-85803).
                        double rad = heading * Math.PI / 180.0;
                        fx -= (float)Math.Sin(rad) * group.DepthMeters;
                        fz -= (float)Math.Cos(rad) * group.DepthMeters;
                    }

                    float radius = Math.Max(1, group.Brigades) * SpacingPerBrigadeMeters;
                    bool sameFront = Distance(group.VanillaX, group.VanillaZ, fx, fz) <= radius + 1f;
                    bool sameFacing = AngleDifference(group.VanillaHeading, heading) <= FacingToleranceDegrees;
                    if (!(sameFront && sameFacing))
                    {
                        Spaced(into, placed, fx, fz, ex, ez, group.OldX, group.OldZ, radius, out float sx, out float sz);
                        placement.X = sx;
                        placement.Z = sz;
                        placement.Heading = heading;
                        placement.Kind = PlacementKind.Replaced;
                        replaced++;
                    }
                }
                into.Placements[g] = placement;
                placed++;
            }

            into.Count = groupCount;
            Array.Copy(into.Placements, into.Base, groupCount);
            return replaced;
        }

        /// <summary>
        /// The reserve echelon: the floor(N / 3) eligible direct children (N at least 3) whose
        /// placements stand farthest from the assumed enemy go ReserveDepthMeters behind the line
        /// centroid of the other children, on lanes of at most +-90 m, every group of a child moved
        /// by the child's one vector so its groups keep their offsets. Returns the groups proposed
        /// (their Kind becomes Reserve; the coordinates are unclamped).
        /// </summary>
        public static int ProposeReserve(DeploymentPlan plan, DeploymentGroup[] groups, int groupCount)
        {
            if (plan == null || groups == null || groupCount <= 0 || plan.Count < groupCount) return 0;
            plan.HasReserve = false;
            plan.ReserveGroupCount = 0;
            Array.Copy(plan.Base, plan.Placements, groupCount);

            // Eligible children, in first-seen order.
            int[] children = new int[groupCount];
            int childCount = 0;
            for (int g = 0; g < groupCount; g++)
            {
                int id = groups[g].DirectChildId;
                if (id == 0 || IndexOf(children, childCount, id) >= 0) continue;
                if (ChildEligible(groups, groupCount, id)) children[childCount++] = id;
            }
            if (childCount < MinChildrenForReserve) return 0;
            int reserveCount = childCount / MinChildrenForReserve;

            // Farthest from the assumed enemy first.
            float[] distance = new float[childCount];
            for (int c = 0; c < childCount; c++)
            {
                ChildCentroid(plan, groups, groupCount, children[c], out float cx, out float cz);
                distance[c] = Distance(cx, cz, plan.AssumedEnemyX, plan.AssumedEnemyZ);
            }
            Array.Sort(distance, children, 0, childCount);
            Array.Reverse(children, 0, childCount);

            // The line: every placement of a child that is not in the reserve.
            float lineX = 0f, lineZ = 0f;
            int lineGroups = 0;
            for (int g = 0; g < groupCount; g++)
            {
                if (IndexOf(children, reserveCount, groups[g].DirectChildId) >= 0) continue;
                lineX += plan.Placements[g].X;
                lineZ += plan.Placements[g].Z;
                lineGroups++;
            }
            if (lineGroups == 0) return 0;
            lineX /= lineGroups;
            lineZ /= lineGroups;
            float ax = lineX - plan.AssumedEnemyX;
            float az = lineZ - plan.AssumedEnemyZ;
            float axLength = (float)Math.Sqrt(ax * ax + az * az);
            if (!(axLength > 1f)) return 0;
            ax /= axLength;
            az /= axLength;
            plan.LineCentroidX = lineX;
            plan.LineCentroidZ = lineZ;
            plan.AxisX = ax;
            plan.AxisZ = az;

            // Lanes in order of the children's position across the axis, so they do not cross.
            float lx = -az, lz = ax;
            float[] across = new float[reserveCount];
            int[] order = new int[reserveCount];
            for (int r = 0; r < reserveCount; r++)
            {
                ChildCentroid(plan, groups, groupCount, children[r], out float cx, out float cz);
                across[r] = (cx - lineX) * lx + (cz - lineZ) * lz;
                order[r] = children[r];
            }
            Array.Sort(across, order);

            int moved = 0;
            for (int r = 0; r < reserveCount; r++)
            {
                float lane = reserveCount == 1 ? 0f : -ReserveLaneMeters + 2f * ReserveLaneMeters * r / (reserveCount - 1);
                ChildCentroid(plan, groups, groupCount, order[r], out float cx, out float cz);
                float vx = lineX + ax * ReserveDepthMeters + lx * lane - cx;
                float vz = lineZ + az * ReserveDepthMeters + lz * lane - cz;
                for (int g = 0; g < groupCount; g++)
                {
                    if (groups[g].DirectChildId != order[r]) continue;
                    plan.Placements[g].X += vx;
                    plan.Placements[g].Z += vz;
                    plan.Placements[g].Kind = PlacementKind.Reserve;
                    moved++;
                }
            }
            plan.HasReserve = moved > 0;
            plan.ReserveGroupCount = moved;
            return moved;
        }

        /// <summary>
        /// Applies the deployment-zone clamp the runtime computed for each placement
        /// (clampX/clampZ indexed like the placements, GetClosestPointInDeploymentZone 102819): a
        /// reserve child stays in reserve only when every one of its clamped targets still stands at
        /// least MinReserveDepthMeters behind the line centroid along the axis; otherwise its groups
        /// return to their fog-honest placements. Returns the reserve groups that remain.
        /// </summary>
        public static int ApplyReserveClamp(DeploymentPlan plan, DeploymentGroup[] groups, int groupCount, float[] clampX, float[] clampZ)
        {
            if (plan == null || !plan.HasReserve || groups == null || clampX == null || clampZ == null) return 0;
            int remaining = 0;
            for (int g = 0; g < groupCount; g++)
            {
                if (plan.Placements[g].Kind != PlacementKind.Reserve) continue;
                int child = groups[g].DirectChildId;
                bool deep = true;
                for (int h = 0; h < groupCount && deep; h++)
                {
                    if (groups[h].DirectChildId != child || h >= clampX.Length || h >= clampZ.Length) continue;
                    float depth = (clampX[h] - plan.LineCentroidX) * plan.AxisX + (clampZ[h] - plan.LineCentroidZ) * plan.AxisZ;
                    if (!(depth >= MinReserveDepthMeters)) deep = false;
                }
                if (deep)
                {
                    plan.Placements[g].X = clampX[g];
                    plan.Placements[g].Z = clampZ[g];
                    remaining++;
                }
                else
                {
                    plan.Placements[g] = plan.Base[g];
                }
            }
            plan.ReserveGroupCount = remaining;
            plan.HasReserve = remaining > 0;
            return remaining;
        }

        /// <summary>
        /// Whether the executor's last Move of the direct child or of any unit under it is a
        /// deployment-reserve placement it issued (Issued or Moved). The outcome table is
        /// executor bookkeeping (battle-core spec D4); the allocator gives such a child
        /// the Reserve role on its first allocation.
        /// </summary>
        public static bool IsDeployedReserve(BattleSnapshot s, OrderExecutor executor, int childIndex)
        {
            if (s == null || executor == null || childIndex < 0 || childIndex >= s.OwnCount) return false;
            UnitRecord[] own = s.Own;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if (i != childIndex && !s.IsAncestorOf(childIndex, i)) continue;
                if (executor.TryGetLastOutcome(own[i].InstanceId, OrderChannel.Move, out OrderOutcome o) &&
                    o.Source == IntentSource.DeploymentReserve &&
                    (o.Kind == OrderOutcomeKind.Issued || o.Kind == OrderOutcomeKind.Moved))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The instance id of the army direct child the unit at <paramref name="ownIndex"/> belongs to
        /// (the unit itself or its nearest ancestor whose id is in <paramref name="childIds"/>), else 0.
        /// Walks the snapshot's command parent map, never the scene graph.
        /// </summary>
        public static int DirectChildOf(IBattleUnitView s, int ownIndex, int[] childIds, int childCount)
        {
            if (s == null || childIds == null || ownIndex < 0 || ownIndex >= s.OwnCount) return 0;
            childCount = Math.Min(childCount, childIds.Length);
            UnitRecord[] own = s.Own;
            int current = ownIndex;
            for (int hops = 0; current >= 0 && current < s.OwnCount && hops <= s.OwnCount; hops++)
            {
                if (IndexOf(childIds, childCount, own[current].InstanceId) >= 0) return own[current].InstanceId;
                current = own[current].ParentIndex;
            }
            return 0;
        }

        // ------------------------------------------------------------------ helpers

        private static int PickObjective(DeploymentObjective[] objectives, int count, DeploymentPlan plan, bool[,] ownZone, bool[,] enemyZone,
            int groupIndex, int ownAlliance, bool firstDay, float enemyX, float enemyZ, float groupX, float groupZ)
        {
            int best = -1;
            float bestSq = float.MaxValue;
            for (int o = 0; o < count; o++)
            {
                if (plan.ObjectiveBlocked[o] || plan.ObjectiveUsed[o]) continue;
                if (ownZone != null && !ownZone[o, groupIndex]) continue;
                if (enemyZone != null && enemyZone[o, groupIndex]) continue;
                if (objectives[o].Owner != ownAlliance && objectives[o].Owner != -1) continue;
                float rx = firstDay ? enemyX : groupX;
                float rz = firstDay ? enemyZ : groupZ;
                float dx = objectives[o].X - rx;
                float dz = objectives[o].Z - rz;
                float d = dx * dx + dz * dz;
                if (d < bestSq) { bestSq = d; best = o; }
            }
            return best;
        }

        // The objective a locked group stands on (nearest within its spacing radius) counts as taken.
        private static void HoldObjectiveOf(DeploymentObjective[] objectives, int count, DeploymentPlan plan, in DeploymentGroup group)
        {
            float radius = Math.Max(1, group.Brigades) * SpacingPerBrigadeMeters + 1f;
            int best = -1;
            float bestDistance = radius;
            for (int o = 0; o < count; o++)
            {
                float d = Distance(objectives[o].X, objectives[o].Z, group.VanillaX, group.VanillaZ);
                if (d <= bestDistance && !plan.ObjectiveUsed[o]) { bestDistance = d; best = o; }
            }
            if (best >= 0) plan.ObjectiveUsed[best] = true;
        }

        // CheckForSimilarPositions (94132-94232): inside the radius of the enemy point nothing
        // moves; a front with a placed group within half the radius is tried on a ring of that
        // radius around it, up to eight times in 45 degree steps (alternating sides, starting
        // toward the group's old position - vanilla starts toward its rear flank point).
        private static void Spaced(DeploymentPlan plan, int placed, float x, float z, float enemyX, float enemyZ,
            float oldX, float oldZ, float radius, out float outX, out float outZ)
        {
            outX = x;
            outZ = z;
            if (Distance(x, z, enemyX, enemyZ) < radius) return;
            bool occupied = Occupied(plan, placed, x, z, radius * 0.5f);
            float baseAngle = Bearing(x, z, oldX, oldZ);
            float step = 0f, sign = -1f;
            int tries = 0;
            while (occupied && tries < SpacingTries)
            {
                AnglePos(x, z, baseAngle + step * sign, radius, out float cx, out float cz);
                occupied = Occupied(plan, placed, cx, cz, radius * 0.5f);
                if (sign == -1f) sign = 1f;
                else { sign = -1f; step += SpacingStepDegrees; }
                tries++;
                if (!occupied) { outX = cx; outZ = cz; }
            }
        }

        private static bool Occupied(DeploymentPlan plan, int placed, float x, float z, float radius)
        {
            // CheckAIGroupInRange over list5: only the groups this call re-deployed.
            for (int p = 0; p < placed; p++)
                if (plan.Placements[p].Kind == PlacementKind.Replaced &&
                    Distance(plan.Placements[p].X, plan.Placements[p].Z, x, z) < radius) return true;
            return false;
        }

        private static bool ChildEligible(DeploymentGroup[] groups, int count, int childId)
        {
            bool combat = false;
            for (int g = 0; g < count; g++)
            {
                if (groups[g].DirectChildId != childId) continue;
                if (groups[g].PlayerOwned || groups[g].WlCommandAncestor || groups[g].Locked) return false;
                if (!groups[g].CavalryOnly && !groups[g].ArtilleryOnly) combat = true;
            }
            return combat;
        }

        private static void ChildCentroid(DeploymentPlan plan, DeploymentGroup[] groups, int count, int childId, out float x, out float z)
        {
            float sx = 0f, sz = 0f;
            int n = 0;
            for (int g = 0; g < count; g++)
            {
                if (groups[g].DirectChildId != childId) continue;
                sx += plan.Placements[g].X;
                sz += plan.Placements[g].Z;
                n++;
            }
            x = n > 0 ? sx / n : 0f;
            z = n > 0 ? sz / n : 0f;
        }

        private static int IndexOf(int[] values, int count, int value)
        {
            for (int i = 0; i < count; i++)
                if (values[i] == value) return i;
            return -1;
        }

        private static bool NearEntry(float x, float z, ReadOnlySpan<float> ex, ReadOnlySpan<float> ez, int entries)
        {
            entries = Math.Min(entries, Math.Min(ex.Length, ez.Length));
            for (int i = 0; i < entries; i++)
                if (Distance(x, z, ex[i], ez[i]) < ObjectiveDetectionMeters) return true;
            return false;
        }

        private static bool NearVisible(BattleSnapshot s, float x, float z)
        {
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].State != ObservationState.Visible || obs[k].IsReinforcement) continue;
                if (Distance(x, z, obs[k].X, obs[k].Z) < ObjectiveDetectionMeters) return true;
            }
            return false;
        }

        // Tools.GetAngleTerrain(a, b) + 180 (26991): degrees clockwise from +z in [0, 360).
        private static float Bearing(float fromX, float fromZ, float toX, float toZ)
        {
            double degrees = Math.Atan2(toX - fromX, toZ - fromZ) * (180.0 / Math.PI);
            if (degrees < 0.0) degrees += 360.0;
            return (float)(degrees >= 360.0 ? 0.0 : degrees);
        }

        // Tools.GetAnglePos (151207): vanilla facing degrees, 0 = +z, 90 = +x.
        private static void AnglePos(float x, float z, float angle, float distance, out float px, out float pz)
        {
            double radians = (angle - 90.0) * (Math.PI / 180.0);
            px = x + (float)(Math.Cos(radians) * distance);
            pz = z - (float)(Math.Sin(radians) * distance);
        }

        private static float AngleDifference(float a, float b)
        {
            float d = Math.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
