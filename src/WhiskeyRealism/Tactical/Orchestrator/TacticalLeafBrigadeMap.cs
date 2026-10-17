using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure recursive role cascade: given the full nested command tree (built by
    /// TacticalCommandTreeProbe.BuildTree), a set of top-tier role assignments
    /// (typically from DirectChildAllocator), and the commander's aggression
    /// vector, produce a flat map of (leaf brigade instanceId → LeafBrigadeAssignment).
    ///
    /// Each assignment includes the full cascade chain (parent role at each
    /// tier, top to leaf) so telemetry can show why a specific brigade got
    /// its task.
    ///
    /// This is the depth-agnostic improvement over SoW's rigid per-tier Think
    /// dispatch: the same algorithm walks 1-tier (army → brigades direct),
    /// 2-tier (army → divisions → brigades), or N-tier hierarchies. Sibling
    /// distribution is geometry-aware (left-to-right across the front axis) and
    /// personality-modulated (high-aggression commanders widen the attack band).
    /// </summary>
    /// <summary>
    /// What a holding command's cascade did about a reserve (the Defender's trace): the numbers it had, what it holds back and why.
    /// </summary>
    public readonly struct DefenderReserveRecord
    {
        public const string Reserved = "reserved";
        public const string AlreadyHeld = "already-held";
        public const string ThinNumbers = "thin-numbers";
        public const string NoFreeBrigade = "no-free-brigade";
        public const string ReleasedToFlank = "released-to-flank";

        public DefenderReserveRecord(int parentInstanceId, string parentName, int children, int held, string reason,
            int[] childIds = null, DirectChildRole[] baseRoles = null, int[] flankBuckets = null, float[] worldX = null, float[] worldZ = null, int released = 0)
        {
            ParentInstanceId = parentInstanceId;
            ParentName = parentName ?? string.Empty;
            Children = children;
            Held = held;
            Reason = reason;
            ChildIds = childIds;
            BaseRoles = baseRoles;
            FlankBuckets = flankBuckets;
            WorldX = worldX;
            WorldZ = worldZ;
            Released = released;
        }

        public int ParentInstanceId { get; }
        public string ParentName { get; }
        /// <summary>Sibling commands the parent splits among.</summary>
        public int Children { get; }
        /// <summary>Siblings in the Reserve role after the cascade and after the reserve release.</summary>
        public int Held { get; }
        public string Reason { get; }

        /// <summary>The reserves the release (<see cref="LeafReserveRelease"/>) has sent out of this command: threat, crisis or the stagger.</summary>
        public int Released { get; }

        // What the release works from, per subordinate command in the cascade's left-to-right order: the roles the cascade and the
        // hold-back gave (before any release), the flank exposure bucket and where the command stands. Null for a command that held nothing.
        public int[] ChildIds { get; }
        public DirectChildRole[] BaseRoles { get; }
        public int[] FlankBuckets { get; }
        public float[] WorldX { get; }
        public float[] WorldZ { get; }
        public bool CanRelease => BaseRoles != null && ChildIds != null;
    }

    public static class TacticalLeafBrigadeMap
    {
        public readonly struct LeafAssignment
        {
            public LeafAssignment(
                int instanceId,
                string displayName,
                DirectChildRole leafRole,
                CommandTaskType leafTask,
                IReadOnlyList<DirectChildRole> cascadeChain,
                IReadOnlyList<string> parentNameChain,
                int topLevelChildIndex,
                bool worksHeld = false)
            {
                WorksHeld = worksHeld;
                InstanceId = instanceId;
                DisplayName = displayName ?? string.Empty;
                LeafRole = leafRole;
                LeafTask = leafTask;
                CascadeChain = cascadeChain ?? Array.Empty<DirectChildRole>();
                ParentNameChain = parentNameChain ?? Array.Empty<string>();
                TopLevelChildIndex = topLevelChildIndex;
            }

            /// <summary>The same leaf in another role (its task follows the role and the chain's last entry, the leaf's own, is replaced).</summary>
            public LeafAssignment WithRole(DirectChildRole role)
            {
                var chain = new DirectChildRole[CascadeChain.Count];
                for (int i = 0; i < chain.Length; i++) chain[i] = CascadeChain[i];
                if (chain.Length > 0) chain[chain.Length - 1] = role;
                return new LeafAssignment(InstanceId, DisplayName, role, TacticalRoleCascade.RoleToLeafTask(role), chain, ParentNameChain, TopLevelChildIndex, WorksHeld);
            }

            public int InstanceId { get; }
            public string DisplayName { get; }
            public DirectChildRole LeafRole { get; }
            public CommandTaskType LeafTask { get; }
            public IReadOnlyList<DirectChildRole> CascadeChain { get; }
            public IReadOnlyList<string> ParentNameChain { get; }
            public int TopLevelChildIndex { get; }

            /// <summary>BC14W: the works hold guard turned the leaf's attack into a hold (the task is then HoldObjective); only the guard's own copy sets it.</summary>
            public bool WorksHeld { get; }

            public string CascadeChainString
            {
                get
                {
                    if (CascadeChain.Count == 0) return string.Empty;
                    var parts = new string[CascadeChain.Count];
                    for (int i = 0; i < CascadeChain.Count; i++) parts[i] = CascadeChain[i].ToString();
                    return string.Join("->", parts);
                }
            }
        }

        public readonly struct TopAssignment
        {
            public TopAssignment(int childInstanceId, DirectChildRole role)
            {
                ChildInstanceId = childInstanceId;
                Role = role;
            }
            public int ChildInstanceId { get; }
            public DirectChildRole Role { get; }
        }

        /// <summary>
        /// Maximum recursion depth for the cascade. Real GTCW hierarchies are
        /// 3-4 tiers max (Army → Corps → Division → Brigade); 16 leaves plenty
        /// of headroom while preventing stack overflow on malformed input.
        /// </summary>
        private const int MaxCascadeDepth = 16;

        /// <summary>
        /// Builds the leaf assignment map. Walks each top-tier child via the tree,
        /// cascades the top role down to its descendant leaves, and returns a
        /// dictionary keyed by leaf brigade instanceId.
        ///
        /// <paramref name="frontAxis"/> orders siblings across the front (left =
        /// negative lateral) and locates the army's wings; without it the legacy
        /// world-X order is used. <paramref name="flankThreat"/> is each group's
        /// mean outflanked weight over its regiments (instance id → 0-4), see
        /// <see cref="TacticalFrontAxis.FlankExposureBucket"/>.
        ///
        /// Envelopment is an army-level decision: with three or more eligible
        /// top-level children (not Fallback / Refuse) the wings are the army's
        /// outer corps, ordered across the front. Smaller armies keep the old
        /// behaviour of splitting the Main child's own subordinates.
        ///
        /// <paramref name="defenderReserve"/> (the side is the Defender): a holding command with three
        /// or more subordinate commands keeps <see cref="DeploymentDoctrine.ReserveShare"/> of them back
        /// as Reserve, interior ones first and the rearmost first, never the anchor of the line nor a
        /// flank guard; each such split is written to <paramref name="reserveReport"/>.
        /// </summary>
        public static IReadOnlyDictionary<int, LeafAssignment> BuildMap(
            IReadOnlyDictionary<int, TacticalCommandTreeProbe.ProbeNode> tree,
            IReadOnlyList<TopAssignment> topLevelAssignments,
            float commanderAggression01,
            CascadeEnvelopmentMode envelopmentMode = CascadeEnvelopmentMode.None,
            TacticalFrontAxis frontAxis = default(TacticalFrontAxis),
            IReadOnlyDictionary<int, float> flankThreat = null,
            bool defenderReserve = false,
            List<DefenderReserveRecord> reserveReport = null,
            IReadOnlyDictionary<int, DirectChildRole> released = null,
            Func<float, float, float, float, bool> flankAnchored = null,
            List<MainFlankGuard.GuardAssignment> flankReport = null)
        {
            var result = new Dictionary<int, LeafAssignment>();
            if (tree == null || topLevelAssignments == null) return result;

            var topRoles = new DirectChildRole[topLevelAssignments.Count];
            for (int i = 0; i < topRoles.Length; i++) topRoles[i] = topLevelAssignments[i].Role;

            CascadeEnvelopmentMode deeperMode = envelopmentMode;
            if (envelopmentMode != CascadeEnvelopmentMode.None &&
                ApplyArmyLevelEnvelopment(tree, topLevelAssignments, topRoles, commanderAggression01, envelopmentMode, frontAxis))
            {
                deeperMode = CascadeEnvelopmentMode.None;
            }

            var geometry = new CascadeGeometry(frontAxis, flankThreat);
            for (int i = 0; i < topLevelAssignments.Count; i++)
            {
                var top = topLevelAssignments[i];
                if (!tree.TryGetValue(top.ChildInstanceId, out var topNode)) continue;
                // Visited set guards against parent-cycles in malformed vanilla
                // data (A→B→A). Each top-tier walk gets its own visited set so
                // multiple top assignments don't share state.
                var visited = new HashSet<int>();
                CascadeInto(
                    topNode,
                    topRoles[i],
                    new List<DirectChildRole> { topRoles[i] },
                    new List<string> { topNode.Name },
                    i,
                    tree,
                    commanderAggression01,
                    deeperMode,
                    geometry,
                    visited,
                    depth: 0,
                    result,
                    defenderReserve,
                    reserveReport,
                    released);
            }

            // A side that is not defending does not advance its main effort with an open flank: the exposed side is given a guard from the reserve
            // (MainFlankGuard.AssignGuards); the posture producer holds the main while one stays open.
            if (!defenderReserve) AssignMainFlankGuards(result, tree, frontAxis, flankAnchored, flankReport);
            return result;
        }

        // Each leaf's place across the front and role, the guards assigned in place, the changed leaves written back. flankAnchored answers, for a flank end
        // (x, z) and the direction outward from the line, whether it rests on impassable terrain (null: no terrain picture).
        private static void AssignMainFlankGuards(
            Dictionary<int, LeafAssignment> leaves,
            IReadOnlyDictionary<int, TacticalCommandTreeProbe.ProbeNode> tree,
            TacticalFrontAxis axis,
            Func<float, float, float, float, bool> flankAnchored,
            List<MainFlankGuard.GuardAssignment> report)
        {
            if (leaves.Count < 2) return;
            var ids = new List<int>(leaves.Keys);
            ids.Sort();
            int n = ids.Count;
            var lateral = new float[n];
            var roles = new DirectChildRole[n];
            var x = new float[n];
            var z = new float[n];
            bool hasMain = false;
            for (int i = 0; i < n; i++)
            {
                roles[i] = leaves[ids[i]].LeafRole;
                hasMain |= roles[i] == DirectChildRole.Main;
                if (tree.TryGetValue(ids[i], out var node))
                {
                    x[i] = node.WorldX;
                    z[i] = node.WorldZ;
                }
                lateral[i] = axis.IsValid ? axis.LateralOf(x[i], z[i]) : x[i];
            }
            if (!hasMain) return;

            Func<int, bool, bool> anchored = null;
            if (flankAnchored != null && axis.IsValid)
            {
                anchored = (main, left) =>
                {
                    float side = left ? -1f : 1f;
                    float endX = x[main] + axis.RightX * side * MainFlankGuard.FlankEndMeters;
                    float endZ = z[main] + axis.RightZ * side * MainFlankGuard.FlankEndMeters;
                    return flankAnchored(endX, endZ, axis.RightX * side, axis.RightZ * side);
                };
            }

            var before = (DirectChildRole[])roles.Clone();
            var made = report ?? new List<MainFlankGuard.GuardAssignment>();
            int first = made.Count;
            if (MainFlankGuard.AssignGuards(lateral, roles, anchored, made) == 0) return;
            for (int i = 0; i < n; i++)
                if (roles[i] != before[i]) leaves[ids[i]] = leaves[ids[i]].WithRole(roles[i]);
            // The report names leaves by instance id, not by index.
            for (int k = first; k < made.Count; k++)
                made[k] = new MainFlankGuard.GuardAssignment(ids[made[k].Main], ids[made[k].Guard], made[k].Left, made[k].From);
        }

        private readonly struct CascadeGeometry
        {
            public CascadeGeometry(TacticalFrontAxis axis, IReadOnlyDictionary<int, float> threat)
            {
                Axis = axis;
                Threat = threat;
            }

            public TacticalFrontAxis Axis { get; }
            public IReadOnlyDictionary<int, float> Threat { get; }

            /// <summary>
            /// The outermost sibling holds an army flank when it also sits in
            /// the army's edge band on that side.
            /// </summary>
            public int FlankBucket(TacticalCommandTreeProbe.ProbeNode child, int index, int count)
            {
                float threat = 0f;
                if (Threat != null) Threat.TryGetValue(child.InstanceId, out threat);
                if (!Axis.IsValid || count < 2)
                    return TacticalFrontAxis.FlankExposureBucket(false, false, threat);
                int band = Axis.SectorOf(child.WorldX, child.WorldZ);
                bool leftWing = index == 0 && band == 0;
                bool rightWing = index == count - 1 && band == Axis.BandCount - 1;
                bool overlapped = (leftWing && Axis.EnemyOverlapsLeft) || (rightWing && Axis.EnemyOverlapsRight);
                return TacticalFrontAxis.FlankExposureBucket(leftWing || rightWing, overlapped, threat);
            }
        }

        private static bool ApplyArmyLevelEnvelopment(
            IReadOnlyDictionary<int, TacticalCommandTreeProbe.ProbeNode> tree,
            IReadOnlyList<TopAssignment> tops,
            DirectChildRole[] topRoles,
            float commanderAggression01,
            CascadeEnvelopmentMode mode,
            TacticalFrontAxis axis)
        {
            var eligible = new List<int>();
            for (int i = 0; i < tops.Count; i++)
            {
                var role = topRoles[i];
                if (role == DirectChildRole.Fallback || role == DirectChildRole.RefuseLeft || role == DirectChildRole.RefuseRight)
                    continue;
                if (!tree.TryGetValue(tops[i].ChildInstanceId, out var node) || !node.Active) continue;
                eligible.Add(i);
            }
            if (eligible.Count < 3) return false;

            eligible.Sort((a, b) =>
            {
                var na = tree[tops[a].ChildInstanceId];
                var nb = tree[tops[b].ChildInstanceId];
                float la = axis.IsValid ? axis.LateralOf(na.WorldX, na.WorldZ) : na.WorldX;
                float lb = axis.IsValid ? axis.LateralOf(nb.WorldX, nb.WorldZ) : nb.WorldX;
                int c = la.CompareTo(lb);
                return c != 0 ? c : na.InstanceId.CompareTo(nb.InstanceId);
            });

            var strengths = new int[eligible.Count];
            for (int k = 0; k < eligible.Count; k++) strengths[k] = tree[tops[eligible[k]].ChildInstanceId].StrengthBucket;
            var (leftAnchor, rightAnchor) = TacticalRoleCascade.ChooseEnvelopmentAnchorIndices(strengths);

            for (int k = 0; k < eligible.Count; k++)
            {
                var ctx = new TacticalRoleCascade.CascadeContext(
                    parentRole: DirectChildRole.Main,
                    childIndex: k,
                    childCount: eligible.Count,
                    childStrengthBucket: strengths[k],
                    childFlankExposureBucket: 0,
                    commanderAggression01: commanderAggression01,
                    anchorIndex: leftAnchor,
                    envelopmentMode: mode,
                    secondaryAnchorIndex: rightAnchor);
                topRoles[eligible[k]] = TacticalRoleCascade.DistributeChildRole(ctx);
            }
            return true;
        }

        private static void CascadeInto(
            TacticalCommandTreeProbe.ProbeNode node,
            DirectChildRole nodeRole,
            List<DirectChildRole> chainSoFar,
            List<string> parentNamesSoFar,
            int topLevelChildIndex,
            IReadOnlyDictionary<int, TacticalCommandTreeProbe.ProbeNode> tree,
            float commanderAggression01,
            CascadeEnvelopmentMode envelopmentMode,
            CascadeGeometry geometry,
            HashSet<int> visited,
            int depth,
            Dictionary<int, LeafAssignment> output,
            bool defenderReserve,
            List<DefenderReserveRecord> reserveReport,
            IReadOnlyDictionary<int, DirectChildRole> released)
        {
            if (!node.Active) return;
            if (depth > MaxCascadeDepth) return;
            if (visited != null && !visited.Add(node.InstanceId))
            {
                // Cycle detected — already visited this node on this top-tier
                // walk. Bail to avoid stack overflow on malformed parent links.
                return;
            }

            // Stop the cascade at brigade tier. Regiment-tier children
            // (Infantry/Cavalry/Artillery/Skirmisher) inherit orders from
            // their brigade via vanilla; the cascade must not descend further
            // or the leaf map ends up keyed by combat-unit instance IDs, which
            // the brigade-tier filter in TryApplyNestedLeafBrigades silently
            // misses.
            if (node.UnitTyp == TacticalUnitType.BattleGroupBrigade)
            {
                output[node.InstanceId] = new LeafAssignment(
                    node.InstanceId,
                    node.Name,
                    nodeRole,
                    TacticalRoleCascade.RoleToLeafTask(nodeRole),
                    chainSoFar.ToArray(),
                    parentNamesSoFar.ToArray(),
                    topLevelChildIndex);
                return;
            }

            // Combat-unit nodes (unittyp < BattleGroupBrigade) are skipped
            // entirely — they're regiments inside brigades, not part of the
            // command-tier cascade.
            if (node.UnitTyp < TacticalUnitType.BattleGroupBrigade) return;

            var children = TacticalCommandTreeProbe.EnumerateChildrenLeftToRight(node.InstanceId, tree, geometry.Axis);
            if (children.Count == 0)
            {
                // Above-brigade node with no brigade descendants. Record as a
                // leaf so degenerate hierarchies (a corps with only attached
                // batteries, no brigades) still produce one assignment.
                output[node.InstanceId] = new LeafAssignment(
                    node.InstanceId,
                    node.Name,
                    nodeRole,
                    TacticalRoleCascade.RoleToLeafTask(nodeRole),
                    chainSoFar.ToArray(),
                    parentNamesSoFar.ToArray(),
                    topLevelChildIndex);
                return;
            }

            // Non-leaf: pre-pick the Main anchor index for this sibling group
            // using sibling strength buckets. Passing the same anchor to every
            // sibling's distribution call ensures exactly one Main per non-leaf
            // parent (no multi-self-anchor allocations).
            //
            // Envelopment is a TOP-LEVEL strategic decision: only apply DoubleWing
            // at depth==0 (the army's direct children). Deeper tiers cascade
            // normally so each wing then attacks via standard single-anchor Main
            // toward its own offset objective. Without this constraint a 3-tier
            // hierarchy would multiply wings (4 wings instead of 2).
            int anchorIndex = -1;
            int secondaryAnchorIndex = -1;
            CascadeEnvelopmentMode activeMode = CascadeEnvelopmentMode.None;
            bool envelopmentEligibleHere =
                depth == 0 &&
                (envelopmentMode == CascadeEnvelopmentMode.DoubleWing ||
                 envelopmentMode == CascadeEnvelopmentMode.DoubleWingEchelon) &&
                children.Count >= 3;
            if (nodeRole == DirectChildRole.Main || nodeRole == DirectChildRole.SupportMain)
            {
                var strengths = new int[children.Count];
                for (int s = 0; s < children.Count; s++) strengths[s] = children[s].StrengthBucket;
                if (envelopmentEligibleHere)
                {
                    var (lAnchor, rAnchor) = TacticalRoleCascade.ChooseEnvelopmentAnchorIndices(strengths);
                    anchorIndex = lAnchor;
                    secondaryAnchorIndex = rAnchor;
                    activeMode = envelopmentMode;
                }
                else
                {
                    anchorIndex = TacticalRoleCascade.ChooseMainAnchorIndex(strengths, nearCenterRadius: 1);
                }
            }

            var derivedRoles = new DirectChildRole[children.Count];
            var flankBuckets = new int[children.Count];
            for (int idx = 0; idx < children.Count; idx++)
            {
                var child = children[idx];
                flankBuckets[idx] = geometry.FlankBucket(child, idx, children.Count);
                var ctx = new TacticalRoleCascade.CascadeContext(
                    parentRole: nodeRole,
                    childIndex: idx,
                    childCount: children.Count,
                    childStrengthBucket: child.StrengthBucket,
                    childFlankExposureBucket: flankBuckets[idx],
                    commanderAggression01: commanderAggression01,
                    anchorIndex: anchorIndex,
                    envelopmentMode: activeMode,
                    secondaryAnchorIndex: secondaryAnchorIndex);
                derivedRoles[idx] = TacticalRoleCascade.DistributeChildRole(ctx);
            }

            if (defenderReserve && children.Count >= 2 && nodeRole != DirectChildRole.Reserve && nodeRole != DirectChildRole.Fallback)
                HoldBackDefenderReserve(node, children, derivedRoles, flankBuckets, geometry.Axis, anchorIndex, secondaryAnchorIndex, reserveReport, released);

            for (int idx = 0; idx < children.Count; idx++)
            {
                var child = children[idx];
                var derived = derivedRoles[idx];

                var nextChain = new List<DirectChildRole>(chainSoFar.Count + 1);
                nextChain.AddRange(chainSoFar);
                nextChain.Add(derived);

                var nextNames = new List<string>(parentNamesSoFar.Count + 1);
                nextNames.AddRange(parentNamesSoFar);
                nextNames.Add(child.Name);

                CascadeInto(
                    child,
                    derived,
                    nextChain,
                    nextNames,
                    topLevelChildIndex,
                    tree,
                    commanderAggression01,
                    envelopmentMode,
                    geometry,
                    visited,
                    depth + 1,
                    output,
                    defenderReserve,
                    reserveReport,
                    released);
            }
        }

        // The Defender's reserve inside a holding command. The deployment doctrine's rule (floor(N / 3) from N = 3) sets
        // how many; the candidates are the subordinates whose cascade role is SupportMain or Fix, or a Refuse role that is not
        // at the end of the line (the cascade gives every sibling outside the support band a Refuse role, but only the outermost
        // one guards a flank); never the anchor (nor an envelopment's second anchor), a Screen or an end-of-line flank guard. Interior ones go before the two ends of
        // the line, the rearmost first (smallest depth along the front axis, else the later index). A command that already
        // holds some reserve counts them. A reserve is not spent for nothing: when an end of the line is exposed
        // (flank bucket at FlankExposureRefuseThreshold, the allocator's own bar) the nearest reserve goes to guard that flank
        // (RefuseLeft / RefuseRight, task GuardFlank), the cascade's form of DirectChildAllocator.ReleaseReserveToThreatenedWing.
        private static void HoldBackDefenderReserve(
            TacticalCommandTreeProbe.ProbeNode parent,
            IReadOnlyList<TacticalCommandTreeProbe.ProbeNode> children,
            DirectChildRole[] roles,
            int[] flankBuckets,
            TacticalFrontAxis axis,
            int anchorIndex,
            int secondaryAnchorIndex,
            List<DefenderReserveRecord> report,
            IReadOnlyDictionary<int, DirectChildRole> released)
        {
            int n = children.Count;
            int held = 0;
            for (int i = 0; i < n; i++) if (roles[i] == DirectChildRole.Reserve) held++;
            int wanted = DeploymentDoctrine.ReserveShare(n);
            if (wanted == 0)
            {
                report?.Add(new DefenderReserveRecord(parent.InstanceId, parent.Name, n, held, DefenderReserveRecord.ThinNumbers));
                return;
            }

            string reason = DefenderReserveRecord.AlreadyHeld;
            int needed = wanted - held;
            if (needed > 0)
            {
                var candidates = new List<int>(n);
                for (int i = 0; i < n; i++)
                {
                    // The anchor (and an envelopment's second anchor) is the command's own main effort; it is never held back.
                    if (i == anchorIndex || i == secondaryAnchorIndex) continue;
                    bool end = i == 0 || i == n - 1;
                    bool refuse = roles[i] == DirectChildRole.RefuseLeft || roles[i] == DirectChildRole.RefuseRight;
                    if (roles[i] == DirectChildRole.SupportMain || roles[i] == DirectChildRole.Fix || (refuse && !end)) candidates.Add(i);
                }
                candidates.Sort((a, b) =>
                {
                    bool wingA = a == 0 || a == n - 1, wingB = b == 0 || b == n - 1;
                    if (wingA != wingB) return wingA ? 1 : -1;
                    float da = axis.IsValid ? axis.DepthOf(children[a].WorldX, children[a].WorldZ) : -a;
                    float db = axis.IsValid ? axis.DepthOf(children[b].WorldX, children[b].WorldZ) : -b;
                    int c = da.CompareTo(db);
                    return c != 0 ? c : b.CompareTo(a);
                });
                int take = Math.Min(needed, candidates.Count);
                for (int k = 0; k < take; k++) roles[candidates[k]] = DirectChildRole.Reserve;
                held += take;
                reason = take == needed ? DefenderReserveRecord.Reserved : DefenderReserveRecord.NoFreeBrigade;
            }

            // An exposed end draws the nearest reserve to guard it.
            for (int end = 0; end < 2; end++)
            {
                int endIndex = end == 0 ? 0 : n - 1;
                if (flankBuckets[endIndex] < FlankExposureRefuseThreshold) continue;
                DirectChildRole guard = end == 0 ? DirectChildRole.RefuseLeft : DirectChildRole.RefuseRight;
                int nearest = -1;
                for (int i = 0; i < n; i++)
                {
                    if (roles[i] != DirectChildRole.Reserve) continue;
                    if (nearest < 0 || Math.Abs(i - endIndex) < Math.Abs(nearest - endIndex)) nearest = i;
                }
                if (nearest < 0) continue;
                roles[nearest] = guard;
                held--;
                reason = DefenderReserveRecord.ReleasedToFlank;
            }
            if (report == null && released == null) return;

            // The release (LeafReserveRelease) works from the roles as the hold-back left them; it sends reserves out on a threat, a crisis
            // or the stagger, and the cascade applies what it sent as the subordinate's role from here down.
            var baseRoles = (DirectChildRole[])roles.Clone();
            int sent = 0;
            if (released != null)
            {
                for (int i = 0; i < n; i++)
                {
                    if (roles[i] != DirectChildRole.Reserve || !released.TryGetValue(children[i].InstanceId, out DirectChildRole role)) continue;
                    roles[i] = role;
                    held--;
                    sent++;
                }
            }
            if (report == null) return;
            var ids = new int[n];
            var xs = new float[n];
            var zs = new float[n];
            for (int i = 0; i < n; i++)
            {
                ids[i] = children[i].InstanceId;
                xs[i] = children[i].WorldX;
                zs[i] = children[i].WorldZ;
            }
            report.Add(new DefenderReserveRecord(parent.InstanceId, parent.Name, n, held, reason, ids, baseRoles, (int[])flankBuckets.Clone(), xs, zs, sent));
        }

        // The allocator's bar for locking a brigade onto a flank (DirectChildAllocator.FlankExposureRefuseThreshold).
        private const int FlankExposureRefuseThreshold = 3;
    }
}
