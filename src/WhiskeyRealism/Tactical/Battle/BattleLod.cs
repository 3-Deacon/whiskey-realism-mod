using System;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>The producers evaluate a Full group on every arbiter tick; a Distant group only when it is due (BC15 decision 1).</summary>
    public enum LodTier : byte
    {
        Full = 0,
        Distant = 1,
    }

    /// <summary>
    /// Why a group that was Distant became Full (decision 2), or what kept it Full. The first six are the promotion triggers (a)-(f); the rest
    /// are the Distant conditions of decision 1 that stopped holding.
    /// </summary>
    [Flags]
    public enum LodPromotion
    {
        None = 0,
        /// <summary>(a) an observation within <see cref="BattleLod.PromoteMeters"/> appeared, turned Visible or Remembered, or left memory.</summary>
        Observation = 1,
        /// <summary>(b) the command parent is active or unsettled, or the group's layout slot moved or turned.</summary>
        Parent = 2,
        /// <summary>(c) the order's task, target, maneuver heading or formation changed, or the group gained a latch (an exploit, a hold, a maneuver).</summary>
        Target = 4,
        /// <summary>(d) the closest Visible enemy changed bearing or distance, or was gained or lost.</summary>
        Bearing = 8,
        /// <summary>(e) the friendly units inside the group's path corridor changed.</summary>
        Corridor = 16,
        /// <summary>(f) the side's plan signature changed: every group of the side.</summary>
        Plan = 32,
        /// <summary>A Visible enemy or a Remembered one at or above the blocking confidence came within <see cref="BattleLod.DistantMeters"/>.</summary>
        Distance = 64,
        /// <summary>The group or a unit under it took fire or is in melee.</summary>
        Fire = 128,
        /// <summary>The group or a unit under it has a path or a movement mode.</summary>
        Moving = 256,
        /// <summary>A pending order: queued orders, an unsettled executor state, a NavMesh plan the budget deferred.</summary>
        Pending = 512,
        /// <summary>The W&amp;L player's command or one of its command ancestors.</summary>
        PlayerChain = 1024,
        /// <summary>The #45 cached stance decision for the group changed, or the stance ordered on it did (the group stance producer's inputs).</summary>
        Stance = 2048,
        /// <summary>A claim of a producer that runs every tick, published on one of the group's cells last tick, was not renewed (it lapsed, or another source took the cell): the intent it hid is due.</summary>
        Claim = 4096,
    }

    /// <summary>The task and target the posture pipeline resolves for a group (the runtime adapter reads the ledger, the leaf map and the exploit table).</summary>
    public interface ITaskLookup
    {
        /// <summary>The task the group's posture decision starts from (its ledger task, or its leaf brigade's after the opportunity and stance rewrites).</summary>
        CommandTaskType TaskFor(int groupId);

        /// <summary>
        /// The target the posture pipeline resolves for the group: the order's primary target, else the objective approach of the operation's
        /// primary objective. <paramref name="maneuverHeading"/> is the order's ManeuverHeading (-1 none), <paramref name="orderFormation"/> its
        /// formation (-1 none). False when the group has no target.
        /// </summary>
        bool TryTarget(int groupId, out float x, out float z, out float maneuverHeading, out int orderFormation);

        /// <summary>The group is under something that makes its producers time-driven or stateful: a live exploit, a works hold, a recon probe or hold, a main-arrival hold, a BC11 maneuver.</summary>
        bool HasLatchOrManeuver(int groupId);

        /// <summary>A leaf brigade's layout slot in its command parent's formation (LeafSlotTarget); false for any other group.</summary>
        bool TrySlot(int ownIndex, out float x, out float z, out float heading);

        /// <summary>
        /// The coordinator's cached #45 decision for the group as one value (its kind, final stance and origin), or -1 when it has none: the
        /// input of the group stance producer that is neither a task nor a target. A change of it promotes the group.
        /// </summary>
        int StanceDecision(int ownIndex);
    }

    /// <summary>The friendly units (standing own units up to brigade level) along a path corridor, as a count and an id hash.</summary>
    public interface IFriendlyCorridor
    {
        int Signature(int selfId, float x0, float z0, float x1, float z1, float widthMeters, out int count);
    }

    /// <summary>What <see cref="BattleLod.Classify"/> reads besides the snapshot.</summary>
    public struct LodSideInput
    {
        /// <summary>Vanilla's GamePrefs.sidestatupdatecycle, read once per battle (it differs per battlefield).</summary>
        public double CycleHours;
        /// <summary>A hash of the side's plan (HasPlan, PlanId, Phase, MainEffortSector and the inputs the posture pipeline rewrites by).</summary>
        public int PlanSignature;
        public OrderExecutor Executor;
        public ITaskLookup Tasks;
        public IFriendlyCorridor Friendly;
        /// <summary>PostureTick.State.Moving, per own index: the units in the subtree with a path.</summary>
        public int[] MovingDescendants;
    }

    /// <summary>
    /// Battle AI level of detail (BC15, plan docs/superpowers/plans/2026-10-15-battle-core.md § BC15). Pure.
    /// A group far from every perceived enemy, settled, on a static task, with no pending order and nothing changed in what its producers
    /// read re-evaluates every fourth doctrine tick; every other group evaluates every tick, and a group is promoted on the tick any of its
    /// producers' inputs changes. Every wake-up rule reads perceived and own state only (never the enemy's true unit list, never vanilla's
    /// own interruption status of the group's subordinates), and every interval is a multiple of the side-stat cycle on the doctrine clock.
    /// </summary>
    public static class BattleLod
    {
        public const float DistantMeters = 1500f;
        public const float PromoteMeters = 3000f;
        /// <summary>The fog rule has one definition (EnemySamples): a memory below this confidence does not count.</summary>
        public const float RememberedMinConfidence = EnemySamples.RememberedMinConfidence;
        /// <summary>The executor's own duplicate radius: a target that moved less than this is the order it already carries.</summary>
        public const float TargetMoveMeters = OrderArbiter.DedupeMeters;
        public const float TurnDegrees = OrderExecutor.FacingToleranceDegrees;
        public const float EnemyDistanceChangeMeters = 250f;
        public const float CorridorWidthMeters = 250f;
        public const int IntervalTicks = 4;
        public const int MinFullTicks = 2;

        // Doctrine hours are doubles; two ticks one cycle apart must compare as exactly two cycles.
        private const double ClockEpsilon = 1e-9;

        private const UnitFlags PlayerChainMask =
            UnitFlags.PlayerOwned | UnitFlags.PlayerCommand | UnitFlags.AncestorOfPlayerCommand | UnitFlags.PlayerCommandDetached;

        private const UnitFlags NoProducerMask =
            UnitFlags.Routed | UnitFlags.MarkedForRout | UnitFlags.OnRetreat | UnitFlags.Garrisoned | UnitFlags.PermanentlyDetached;

        /// <summary>A Distant group is due when this much doctrine time passed since its producers last ran: four cycles less a half-tick tolerance, so a jittered fourth tick is due.</summary>
        public static double DistantIntervalHours(double cycleHours)
        {
            return IntervalTicks * cycleHours - cycleHours / 2.0;
        }

        /// <summary>A promotion keeps the group Full this long.</summary>
        public static double MinFullHours(double cycleHours)
        {
            return MinFullTicks * cycleHours;
        }

        /// <summary>
        /// The tasks a settled group can hold with nothing in its producers that is driven by the clock: None, FormUp, HoldObjective, HoldChoke,
        /// GuardFlank, ReserveWait and Consolidate. Every movement, probe, screen and delay task carries a time-driven rule and is excluded.
        /// </summary>
        public static bool IsStaticTask(CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.None:
                case CommandTaskType.FormUp:
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                case CommandTaskType.GuardFlank:
                case CommandTaskType.ReserveWait:
                case CommandTaskType.Consolidate:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The claims a Distant group that is not due keeps are the ones the tiered producers published: the posture producer and the facing
        /// pulse that follows its record, and the #45 group stance scorer. Every other source comes from a producer that runs on every tick and
        /// resubmits (the division play, the artillery doctrine and the fire control are among them: BC15 decision 5 tiers only where a producer's
        /// inputs are all triggers or Distant conditions), so a carried copy of theirs could outlive the intent that a full-rate tick no longer makes.
        /// </summary>
        public static bool IsCarriedSource(IntentSource source)
        {
            switch (source)
            {
                case IntentSource.PostureExecutor:
                case IntentSource.FacingPulse:
                case IntentSource.GroupStanceScorer:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The tier pass, once per arbiter tick on that tick's snapshot and before any producer: sets every own unit's tier, whether its
        /// producers run this tick and, for a group promoted from Distant, why. Allocation-free once the state's buffers fit. The caller clears
        /// the observation latches after it (BattlePerception.ClearChangeLatch) and the executor's refusal set (OrderExecutor.ClearRefusals).
        /// </summary>
        public static void Classify(BattleSnapshot s, BattleLodState state, in LodSideInput side, double nowHours)
        {
            if (s == null || state == null) return;
            state.BeginTick(s, side.CycleHours);
            int n = s.OwnCount;
            UnitRecord[] own = s.Own;
            bool configured = side.CycleHours > 0.0 && side.Tasks != null && side.Executor != null;

            bool planChanged = state.NotePlanSignature(side.PlanSignature);
            if (configured)
            {
                FoldEnemyProximity(s, state);
                FoldUnsettled(s, state, side.Executor);
                int remembered = CollectRemembered(s, state);
                if (remembered > 0) FoldSubtreeRadius(s, state);
                int changed = CollectChanges(s, state, out bool everythingChanged);
                double interval = DistantIntervalHours(side.CycleHours);
                double minFull = MinFullHours(side.CycleHours);
                for (int i = 0; i < n; i++)
                {
                    if ((own[i].Flags & UnitFlags.IsGroup) == 0) continue;
                    ClassifyGroup(s, state, in side, i, remembered, changed, everythingChanged, planChanged, nowHours, interval, minFull);
                }
            }
            else
            {
                for (int i = 0; i < n; i++)
                    if ((own[i].Flags & UnitFlags.IsGroup) != 0) state.CountGroup(LodTier.Full, true, false);
            }

            // A regiment answers with the tier of its nearest command group; one with no group above it is always due.
            for (int i = 0; i < n; i++)
            {
                if ((own[i].Flags & UnitFlags.IsGroup) != 0) continue;
                int group = NearestGroup(own, n, own[i].ParentIndex);
                if (group >= 0 && configured) state.SetUnitAnswer(s, i, state.TierOf(group), state.IsDue(group), state.LastPromotion(group));
            }
        }

        /// <summary>
        /// The late wake-up for a lapsed claim (decision 14), called after every producer submitted and before Resolve. A producer that runs on every
        /// tick (the division play, a withdrawal, a charge) can hold a cell of a Distant group with a claim that outranks the group's own tiered
        /// producers; at full rate the intent it hides surfaces on the tick the claim goes. A Distant group that is not due, whose last published
        /// non-carried claim on a cell of it or of a unit under it was not renewed this tick, is promoted now and the caller runs the tiered producers for it. Returns the
        /// number of groups woken; <see cref="BattleLodState.IsClaimWoken"/> names them.
        /// </summary>
        public static int WakeLapsedClaims(BattleSnapshot s, BattleLodState state, OrderArbiter arbiter, double nowHours)
        {
            if (s == null || state == null || arbiter == null) return 0;
            int lapsed = arbiter.CollectLapsedUncarriedClaims(ref state.LapsedUnits);
            if (lapsed == 0) return 0;
            int n = s.OwnCount;
            UnitRecord[] own = s.Own;
            if (state.LapsedBelow.Length < n) state.LapsedBelow = new bool[Math.Max(n, state.LapsedBelow.Length * 2)];
            Array.Clear(state.LapsedBelow, 0, n);
            // A lapsed claim on a unit counts for every group above it, as an unsettled order does (FoldUnsettled): the regiment and the battery
            // are the Full-only producers' targets as much as the group is.
            for (int k = 0; k < lapsed; k++)
            {
                int a = s.IndexOfOwn(state.LapsedUnits[k]);
                for (int steps = 0; a >= 0 && a < n && steps <= n; steps++)
                {
                    state.LapsedBelow[a] = true;
                    a = own[a].ParentIndex;
                }
            }

            int woken = 0;
            for (int i = 0; i < n; i++)
            {
                // The two tests before PromoteNow only spare it a call for a regiment and a due group; PromoteNow is the rule: it takes a group of a
                // snapshot the pass covered that is Distant and not due, and nothing else.
                if (!state.LapsedBelow[i] || (own[i].Flags & UnitFlags.IsGroup) == 0 || state.IsDue(i)) continue;
                if (!state.PromoteNow(s, i, LodPromotion.Claim, nowHours)) continue;
                state.MarkClaimWoken(i);
                arbiter.DropCarried(own[i].InstanceId);
                woken++;
            }
            return woken;
        }

        private static int NearestGroup(UnitRecord[] own, int n, int from)
        {
            int p = from;
            for (int steps = 0; p >= 0 && p < n && steps <= n; steps++)
            {
                if ((own[p].Flags & UnitFlags.IsGroup) != 0) return p;
                p = own[p].ParentIndex;
            }
            return -1;
        }

        // Near: a Visible enemy within DistantMeters of the unit; Hot: the unit took fire or is in melee. Each marks the unit and all its ancestors.
        private static void FoldEnemyProximity(BattleSnapshot s, BattleLodState state)
        {
            int n = s.OwnCount;
            UnitRecord[] own = s.Own;
            for (int i = 0; i < n; i++)
            {
                bool near = s.ClosestVisibleEnemy(i, out float distance) >= 0 && distance <= DistantMeters;
                bool hot = own[i].ReceivedFire || (own[i].Flags & UnitFlags.InMelee) != 0;
                if (!near && !hot) continue;
                int a = i;
                for (int steps = 0; a >= 0 && a < n && steps <= n; steps++)
                {
                    if (near) state.Near[a] = true;
                    if (hot) state.Hot[a] = true;
                    a = own[a].ParentIndex;
                }
            }
        }

        // Unsettled: the unit, or a unit under it, has an order the executor has not finished with (a pending move, a retry ticket, a refused
        // winner). Found by walking the executor's list once and marking each unit's ancestors through the snapshot's parent map.
        private static void FoldUnsettled(BattleSnapshot s, BattleLodState state, OrderExecutor executor)
        {
            int n = s.OwnCount;
            UnitRecord[] own = s.Own;
            int count = executor.UnsettledCount;
            for (int k = 0; k < count; k++)
            {
                int a = s.IndexOfOwn(executor.UnsettledId(k));
                for (int steps = 0; a >= 0 && a < n && steps <= n; steps++)
                {
                    state.Unsettled[a] = true;
                    a = own[a].ParentIndex;
                }
            }
        }

        // The Remembered observations at or above the blocking confidence (a Visible one is answered by the snapshot's closest-visible rows).
        private static int CollectRemembered(BattleSnapshot s, BattleLodState state)
        {
            EnemyObservation[] obs = s.Observations;
            int count = 0;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].IsReinforcement || obs[k].State != ObservationState.Remembered || obs[k].Confidence01 < RememberedMinConfidence) continue;
                state.EnsureRememberedCapacity(count + 1);
                state.RememberedX[count] = obs[k].X;
                state.RememberedZ[count] = obs[k].Z;
                count++;
            }
            return count;
        }

        // How far the units under a group lie from it: "within DistantMeters of the group or any unit under it" is then "within
        // DistantMeters + radius of the group", a superset (a group may stay Full a little longer than the exact rule), found with one query per group.
        private static void FoldSubtreeRadius(BattleSnapshot s, BattleLodState state)
        {
            int n = s.OwnCount;
            UnitRecord[] own = s.Own;
            Array.Clear(state.SubtreeRadius, 0, n);
            for (int i = 0; i < n; i++)
            {
                int a = own[i].ParentIndex;
                for (int steps = 0; a >= 0 && a < n && steps <= n; steps++)
                {
                    float dx = own[i].X - own[a].X, dz = own[i].Z - own[a].Z;
                    float d = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (d > state.SubtreeRadius[a]) state.SubtreeRadius[a] = d;
                    a = own[a].ParentIndex;
                }
            }
        }

        // Positions of the observations that changed since the last tier pass (a latched entry, or a drop); everything when the drop log overflowed.
        private static int CollectChanges(BattleSnapshot s, BattleLodState state, out bool everything)
        {
            EnemyObservation[] obs = s.Observations;
            int count = 0;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (!obs[k].ChangedSinceLastTier || obs[k].IsReinforcement) continue;
                state.EnsureChangedCapacity(count + 1);
                state.ChangedX[count] = obs[k].X;
                state.ChangedZ[count] = obs[k].Z;
                count++;
            }
            for (int d = 0; d < s.DroppedObservationCount; d++)
            {
                state.EnsureChangedCapacity(count + 1);
                state.ChangedX[count] = s.DroppedObservationX(d);
                state.ChangedZ[count] = s.DroppedObservationZ(d);
                count++;
            }
            everything = s.DroppedObservationsOverflowed;
            return count;
        }

        private static void ClassifyGroup(BattleSnapshot s, BattleLodState st, in LodSideInput side, int i, int remembered, int changed,
            bool everythingChanged, bool planChanged, double now, double interval, double minFull)
        {
            UnitRecord[] own = s.Own;
            ref UnitRecord rec = ref own[i];
            int id = rec.InstanceId;
            ref BattleLodState.GroupLod g = ref st.EntryFor(id, now);
            bool wasDistant = g.Tier == LodTier.Distant;
            UnitFlags flags = rec.Flags;

            // Decision 1, cheapest first. A condition that fails keeps the group Full, and says why when the group was Distant.
            LodPromotion why = LodPromotion.None;
            bool producesNothing = (flags & UnitFlags.Active) == 0 || (flags & NoProducerMask) != 0;
            if ((flags & PlayerChainMask) != 0) why |= LodPromotion.PlayerChain;
            if (planChanged) why |= LodPromotion.Plan;
            if (st.Near[i] || RememberedNear(rec.X, rec.Z, st, i, remembered)) why |= LodPromotion.Distance;
            if (st.Hot[i]) why |= LodPromotion.Fire;
            if (IsMoving(in rec, side.MovingDescendants, i)) why |= LodPromotion.Moving;
            if (PostureSense.PendingOrder(rec, false, false) || st.Unsettled[i] || g.NavDeferred) why |= LodPromotion.Pending;
            if (why == LodPromotion.None && !producesNothing &&
                (!IsStaticTask(side.Tasks.TaskFor(id)) || side.Tasks.HasLatchOrManeuver(id)))
                why |= LodPromotion.Target;

            if (producesNothing || why != LodPromotion.None)
            {
                KeepFull(s, st, i, ref g, wasDistant, why, now, signatureKnown: false);
                return;
            }

            // The group could be Distant. Decision 2: compare what its producers read now with what they read at their last evaluation.
            BuildSignature(s, in side, i, out BattleLodState.LodSignature current);
            LodPromotion fired = LodPromotion.None;
            if (ObservationChangedNear(rec.X, rec.Z, st, changed, everythingChanged)) fired |= LodPromotion.Observation;
            if (ParentActive(s, in side, in rec)) fired |= LodPromotion.Parent;
            if (g.HasSignature) fired |= Compare(in g.Signature, in current);
            if (fired != LodPromotion.None)
            {
                g.Signature = current;
                KeepFull(s, st, i, ref g, wasDistant, fired, now, signatureKnown: true);
                return;
            }

            double cycle = side.CycleHours;
            if (wasDistant)
            {
                bool due = now - g.LastEvalHours >= interval - ClockEpsilon;
                if (due)
                {
                    g.LastEvalHours = now;
                    g.Signature = current;
                }
                st.CountGroup(LodTier.Distant, due, g.NavDeferred);
                st.SetUnitAnswer(s, i, LodTier.Distant, due, LodPromotion.None);
                return;
            }

            // Full and quiet. It enters Distant once it has a signature from an earlier tick and has been quiet for the minimum hold; the
            // entry is staggered so the distant groups spread over the four ticks of an interval.
            bool hadSignature = g.HasSignature;
            if (hadSignature && now - g.LastCauseHours >= minFull - ClockEpsilon)
            {
                g.Tier = LodTier.Distant;
                g.Stagger = i % IntervalTicks;
                g.LastEvalHours = now - g.Stagger * cycle;
                bool due = now - g.LastEvalHours >= interval - ClockEpsilon;
                if (due)
                {
                    g.LastEvalHours = now;
                    g.Signature = current;
                }
                st.CountGroup(LodTier.Distant, due, g.NavDeferred);
                st.SetUnitAnswer(s, i, LodTier.Distant, due, LodPromotion.None);
                return;
            }

            // Still Full: its producers run this tick, so this tick's inputs become the ones to compare with.
            g.Signature = current;
            g.HasSignature = true;
            g.LastEvalHours = now;
            st.CountGroup(LodTier.Full, true, g.NavDeferred);
            st.SetUnitAnswer(s, i, LodTier.Full, true, LodPromotion.None);
        }

        // The group runs its producers this tick for a cause: it is Full, remembers the cause's hour, and (when it was Distant) counts the promotion.
        private static void KeepFull(BattleSnapshot s, BattleLodState st, int i, ref BattleLodState.GroupLod g, bool wasDistant, LodPromotion why,
            double now, bool signatureKnown)
        {
            g.Tier = LodTier.Full;
            g.LastEvalHours = now;
            g.LastCauseHours = now;
            g.HasSignature = signatureKnown;
            if (wasDistant && why != LodPromotion.None) st.CountPromotion(why);
            st.CountGroup(LodTier.Full, true, g.NavDeferred);
            st.SetUnitAnswer(s, i, LodTier.Full, true, why);
        }

        private static bool IsMoving(in UnitRecord rec, int[] movingDescendants, int i)
        {
            if (rec.RegimentPaths > 0 || rec.MovementMode > 0 || PostureSense.PathInterrupted(rec)) return true;
            return movingDescendants != null && i < movingDescendants.Length && movingDescendants[i] > 0;
        }

        private static bool RememberedNear(float x, float z, BattleLodState st, int i, int remembered)
        {
            if (remembered == 0) return false;
            float reach = DistantMeters + st.SubtreeRadius[i];
            float reachSquared = reach * reach;
            for (int r = 0; r < remembered; r++)
            {
                float dx = st.RememberedX[r] - x, dz = st.RememberedZ[r] - z;
                if (dx * dx + dz * dz <= reachSquared) return true;
            }
            return false;
        }

        private static bool ObservationChangedNear(float x, float z, BattleLodState st, int changed, bool everythingChanged)
        {
            if (everythingChanged) return true;
            const float reachSquared = PromoteMeters * PromoteMeters;
            for (int c = 0; c < changed; c++)
            {
                float dx = st.ChangedX[c] - x, dz = st.ChangedZ[c] - z;
                if (dx * dx + dz * dz <= reachSquared) return true;
            }
            return false;
        }

        // Trigger (b), the activity half: the producer's own test for its command parent (PostureIntentProducer.ProduceLeaf), plus the
        // executor's. A parent that is Full for its own reasons (an enemy near it) is not active and does not pass to its children.
        private static bool ParentActive(BattleSnapshot s, in LodSideInput side, in UnitRecord rec)
        {
            int p = rec.ParentIndex;
            if (p < 0 || p >= s.OwnCount) return false;
            ref UnitRecord parent = ref s.Own[p];
            int moving = side.MovingDescendants != null && p < side.MovingDescendants.Length ? side.MovingDescendants[p] : 0;
            bool activeMove = PostureSense.ActiveMove(parent, moving);
            return parent.RegimentPaths > 0 || activeMove || PostureSense.PendingOrder(parent, activeMove, false) ||
                side.Executor.IsUnsettled(parent.InstanceId);
        }

        private static void BuildSignature(BattleSnapshot s, in LodSideInput side, int i, out BattleLodState.LodSignature sig)
        {
            ref UnitRecord rec = ref s.Own[i];
            sig = default(BattleLodState.LodSignature);
            sig.Task = side.Tasks.TaskFor(rec.InstanceId);
            sig.HasTarget = side.Tasks.TryTarget(rec.InstanceId, out sig.TargetX, out sig.TargetZ, out sig.ManeuverHeading, out sig.OrderFormation);
            sig.HasSlot = side.Tasks.TrySlot(i, out sig.SlotX, out sig.SlotZ, out sig.SlotHeading);
            sig.StanceDecision = side.Tasks.StanceDecision(i);
            sig.StanceOrdered = rec.AiStanceOrdered;
            int enemy = s.ClosestVisibleEnemy(i, out float distance);
            if (enemy >= 0)
            {
                sig.HasEnemy = true;
                sig.EnemyBearing = PostureSense.HeadingTo(rec.X, rec.Z, s.Observations[enemy].X, s.Observations[enemy].Z);
                sig.EnemyDistance = distance;
            }
            if (side.Friendly == null) return;
            if (sig.HasTarget)
                sig.CorridorHash = side.Friendly.Signature(rec.InstanceId, rec.X, rec.Z, sig.TargetX, sig.TargetZ, CorridorWidthMeters, out sig.CorridorCount);
            else if (sig.HasSlot)
                sig.CorridorHash = side.Friendly.Signature(rec.InstanceId, rec.X, rec.Z, sig.SlotX, sig.SlotZ, CorridorWidthMeters, out sig.CorridorCount);
        }

        // Triggers (b) slot, (c), (d), (e) and the stance inputs: this tick's inputs against the last evaluation's.
        private static LodPromotion Compare(in BattleLodState.LodSignature old, in BattleLodState.LodSignature now)
        {
            LodPromotion fired = LodPromotion.None;
            if (old.HasSlot != now.HasSlot ||
                (now.HasSlot && (Moved(old.SlotX, old.SlotZ, now.SlotX, now.SlotZ) || Turned(old.SlotHeading, now.SlotHeading) > TurnDegrees)))
                fired |= LodPromotion.Parent;

            if (old.Task != now.Task || old.HasTarget != now.HasTarget || old.OrderFormation != now.OrderFormation ||
                (now.HasTarget && (Moved(old.TargetX, old.TargetZ, now.TargetX, now.TargetZ) || HeadingChanged(old.ManeuverHeading, now.ManeuverHeading))))
                fired |= LodPromotion.Target;

            if (old.HasEnemy != now.HasEnemy ||
                (now.HasEnemy && (Turned(old.EnemyBearing, now.EnemyBearing) >= TurnDegrees ||
                    Math.Abs(old.EnemyDistance - now.EnemyDistance) >= EnemyDistanceChangeMeters)))
                fired |= LodPromotion.Bearing;

            if (old.CorridorCount != now.CorridorCount || old.CorridorHash != now.CorridorHash) fired |= LodPromotion.Corridor;

            if (old.StanceDecision != now.StanceDecision || old.StanceOrdered != now.StanceOrdered) fired |= LodPromotion.Stance;
            return fired;
        }

        private static bool Moved(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return dx * dx + dz * dz > TargetMoveMeters * TargetMoveMeters;
        }

        // The shorter way round between two headings in degrees.
        private static float Turned(float a, float b)
        {
            float d = Math.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        // A heading of -1 means none: gaining or losing one is a change.
        private static bool HeadingChanged(float a, float b)
        {
            if (a < 0f && b < 0f) return false;
            if ((a < 0f) != (b < 0f)) return true;
            return Turned(a, b) > TurnDegrees;
        }
    }
}
