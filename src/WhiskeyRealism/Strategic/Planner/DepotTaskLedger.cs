using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    public enum DepotTaskState { Marching = 0, Wished = 1, Built = 2, Abandoned = 3 }

    public enum DepotWishMoment { Wait = 0, Here = 1, Adopt = 2, Ahead = 3 }

    public struct DepotTaskRecord
    {
        public float X, Z;
        public int SectorId;
        public int BuilderInstanceId;   // in-session lookup only, never persisted
        public string BuilderUnitKey;   // persisted identity
        public DepotTaskState State;
        public int StartedDay;          // the day the current state began: the start, then the wish
        public float WishX, WishZ;      // where the wish was placed (the builder's position, the site for a wish ahead, the adopted depot's): the depot rises within buildingsitemaxradius of it. Meaningful once Wished
        public int EndedDay;            // the day the task became Built or Abandoned, -1 while active (the retry cooldown is a day difference)
    }

    // A state change the runtime logs as `[DepotTask] alliance=<a> site=<x>,<z> builder=<unit> state=<...> reason=<...>`.
    public struct DepotTaskTransition
    {
        public float X, Z;
        public string BuilderUnitKey;
        public DepotTaskState State;
        public string Reason;
    }

    // What the runtime reads for DepotTaskLedger.WishMoment besides the builder: the review day, whether the mover's tracker has the
    // builder marching to the task's site on a move issued at an earlier review (MoveTracker.MarchingToward), and the nearest own
    // depot still rising within the built radius of the site, if any (the one Adopt takes as the task's).
    public struct DepotWishFacts
    {
        public int DaySerial;
        public bool MarchIssuedEarlier;
        public bool OwnUnbuiltDepotAtSite;
        public float AdoptX, AdoptZ;    // that depot's position, read when OwnUnbuiltDepotAtSite
    }

    // A builder the depot ledger holds (plan G5c, verify r3 F7 and the G3 ruling): the one published set both the planner and the
    // allocator read. Every held builder is out of the mass G3 judges. A Wished one (the wish is paid, the depot rises only while the
    // builder stays near) is also pinned by the allocator to its target, the wish position; a Marching one still follows the
    // allocation, its target the site. See DepotTaskLedger.CollectHeldBuilders.
    public struct HeldBuilder
    {
        public int InstanceId;
        public int SectorId;
        public float TargetX, TargetZ;
        public bool Wished;
    }

    // The depot task executor's state machine (plan G5c, spec C10): Marching -> Wished -> Built | Abandoned, one active task per
    // alliance. The planner's depot tasks (TheaterPlan.DepotTasks, sites the supply reach needs) become vanilla construction
    // wishes: the DepotBuilder formation the allocation picked (G4) marches to the site (G5a's mover), and on arrival within its
    // bugle range the runtime places CBuilding.AddConstructionWish(CBuilding.id_supplydepot, ...) once (97479-97536).
    //
    // Why the builder is held: a military building rises only while an own unit stands within that unit's buglerange of it
    // (CBuilding.IncreaseCondition 96626-96655); with none near, the timer climbs back and the building is removed after
    // GamePrefs.timetoremovemilitarybuildings (96645-96669). HoldsBuilder keeps the builder out of vanilla's offensive and defensive
    // pools (CampaignWithholdView) from the start of the march until the depot stands, is abandoned or times out. A builder that
    // stands within command range of its own unfinished depot is flagged VanillaDepotBuilder by the next snapshot (vanilla's job 19
    // lists it, 14670-14690) and TheaterPlanner.Busy keeps it Hold in place, so the allocation does not send it off meanwhile.
    //
    // Pure over the plan, the allocation and the snapshot. Written only inside the review (DepotSteeringRuntime); read by
    // CampaignWithholdView (HoldsBuilder). Tasks are not matched to the plan once wished: the wish exists in vanilla.
    public sealed class DepotTaskLedger
    {
        public const int TimeoutDays = 20;
        public const int MaxActivePerAlliance = 1;
        // The plan's depot site follows the supply frontier from day to day; a marching task within this of a plan site is that site.
        public const float SiteMatchRadius = 25f;
        // The allocator assigns a builder at exactly the task's site (ForceAllocator.AssignDepotBuilders).
        private const float AssignmentEpsilon = 1f;
        // A site that timed out is not tried again at once: a wish that cannot be placed costs treasury each time (Place pays, 96211).
        public const int RetryAfterAbandonDays = 20;
        public const int MaxFinishedRecords = 8;
        // A builder that will reach its site before the next review (one review day, CampaignTempoGate.ReviewGapDays) gets its wish
        // now: see WishMoment.
        public const float AheadDays = 1f;
        // A wish is the CBuilding that AddConstructionWish creates at (-20000, -20000, -20000) (97506) until WorkDownConstructionWishes
        // moves it onto suitable ground; any own unbuilt depot out past this is a placement still waiting its turn.
        private const float PlaceholderBelow = -19000f;

        private readonly List<DepotTaskRecord> _tasks = new List<DepotTaskRecord>();
        private readonly List<DepotTaskTransition> _transitions = new List<DepotTaskTransition>();

        public IReadOnlyList<DepotTaskRecord> Tasks => _tasks;
        internal IReadOnlyList<DepotTaskTransition> Transitions => _transitions;

        // The candidate name the construction ledger gives plan depot task `planTaskIndex` (DepotCandidates.Add).
        public static string CandidateName(int planTaskIndex)
        {
            return "depot-task-" + planTaskIndex;
        }

        // Reconciles the ledger with today's plan and allocation: rebinds builders by unit key after a reload, times tasks out,
        // follows the plan's site while marching, and starts at most MaxActivePerAlliance task. `construction` is the
        // construction ledger's verdict on today's plan tasks (null: none yet, nothing suppressed).
        public void Sync(TheaterPlan plan, Allocation allocation, CampaignSnapshot s, ConstructionOutput construction)
        {
            if (plan == null || allocation == null || s == null) return;
            int day = s.DaySerial;
            for (int t = 0; t < _tasks.Count; t++)
            {
                if (!Active(_tasks[t])) continue;
                if (!BindBuilder(t, s)) continue;
                if (day - _tasks[t].StartedDay >= TimeoutDays) Abandon(t, "timeout", day);
                else if (_tasks[t].State == DepotTaskState.Marching) FollowPlan(t, plan, allocation, s);
            }
            if (ActiveCount() < MaxActivePerAlliance) StartNext(plan, allocation, s, construction, day);
            Prune();
        }

        // True when the marching builder is within its bugle range of the site and the wish has not been placed (97479 wishes once
        // per task; the runtime also skips an IIP that already has a building under construction).
        public bool ShouldWish(int taskIndex, float builderX, float builderZ, float bugleRange)
        {
            if (!InRange(taskIndex) || _tasks[taskIndex].State != DepotTaskState.Marching) return false;
            float dx = builderX - _tasks[taskIndex].X, dz = builderZ - _tasks[taskIndex].Z;
            return dx * dx + dz * dz <= bugleRange * bugleRange;
        }

        // When the executor places the wish (plan G5c, verify F6). Vanilla's depot job (19, CheckSupplyDepotConstruction) walks an idle,
        // low-supply formation to the nearest own town or depot beyond its bugle range (14762-14767) and does not read the availability
        // Postfixes, so a builder that has arrived and stands idle until the next review can be marched away before its wish. The fix
        // is to leave no such window: once a depot started by the alliance stands within the builder's command range, job 19 skips the
        // unit (that sets its `flag`, 14695-14712; the test at 14715 also skips a unit with a path), so:
        //   Here    the builder is within arrivalReach of the site: wish at its position (vanilla's own wish position, 14770);
        //   Adopt   ...and vanilla's job already flagged it VanillaDepotBuilder AND an own unbuilt depot stands at the site
        //           (facts.OwnUnbuiltDepotAtSite: the flag is set by any own-started depot within the builder's command range, 50-100
        //           for a corps or army, which need not be this site's): that is the depot, the task becomes Wished with no second
        //           wish. A flagged builder with no depot at the site gets its own wish (Here);
        //   Ahead   it will be there before the next review (daysToSite <= AheadDays) AND the mover's tracker shows it marching to
        //           this site on a move issued at an earlier review (facts.MarchIssuedEarlier), AND the task did not start today:
        //           wish at the site now, while it still marches. The wish is paid at once (AddConstructionWish -> Place, 97506,
        //           96211-96257) and an unattended depot is removed (96626-96648), so a wish is placed ahead only for a builder that is
        //           on its way (an arrival estimate alone is a lower bound: terrain, a retreat or another order can stop it);
        //   Wait    otherwise.
        public DepotWishMoment WishMoment(int taskIndex, in FormationRecord builder, float arrivalReach, float daysToSite, in DepotWishFacts facts)
        {
            if (!InRange(taskIndex) || _tasks[taskIndex].State != DepotTaskState.Marching) return DepotWishMoment.Wait;
            if (ShouldWish(taskIndex, builder.X, builder.Z, arrivalReach))
                return (builder.Flags & FormationFlags.VanillaDepotBuilder) != 0 && facts.OwnUnbuiltDepotAtSite ? DepotWishMoment.Adopt : DepotWishMoment.Here;
            bool startedEarlier = facts.DaySerial > _tasks[taskIndex].StartedDay;
            return facts.MarchIssuedEarlier && startedEarlier && daysToSite <= AheadDays ? DepotWishMoment.Ahead : DepotWishMoment.Wait;
        }

        // The review's watch over a Wished task: Built when a depot stands within `radius` of where the wish was placed (SiteBuilt);
        // Abandoned as "wish-lost" when, on a later review than the wish, no own depot at all (built or still rising) stands within
        // `radius` of that position. The depot rises on the nearest suitable ground to the wish position, up to buildingsitemaxradius
        // away (97557-97585), and the wish is placed at the builder (up to its bugle range from the site), so the site is not the
        // centre to look around. The wish is a placeholder at -20000 until WorkDownConstructionWishes (once per AI update, 11201)
        // puts the building on the ground or cancels it with no site found (97538-97591), so a review in the wish's own day cannot
        // tell a pending wish from a lost one; a lost wish (cancelled, or the depot removed because no unit stood within bugle
        // range, 96626-96648) releases the builder at once instead of holding it to the timeout.
        public void WatchWish(int taskIndex, CampaignSnapshot s, float radius)
        {
            if (!InRange(taskIndex) || _tasks[taskIndex].State != DepotTaskState.Wished || s == null) return;
            var task = _tasks[taskIndex];
            if (SiteBuilt(s, task, radius)) RecordBuilt(taskIndex, s.DaySerial);
            else if (s.DaySerial > task.StartedDay && !SiteHasOwnDepot(s, task, radius, false) && !PlacementPending(s)) Finish(taskIndex, DepotTaskState.Abandoned, "wish-lost", s.DaySerial);
        }

        // The wish stands (or the vanilla depot was adopted): the task is Wished from `daySerial`, and the depot is looked for within
        // the placement radius of (wishX, wishZ), where the wish was actually placed, not of the site.
        public void RecordWished(int taskIndex, int daySerial, float wishX, float wishZ, string reason = "wish-placed")
        {
            if (!InRange(taskIndex) || _tasks[taskIndex].State != DepotTaskState.Marching) return;
            var t = _tasks[taskIndex];
            t.State = DepotTaskState.Wished;
            t.StartedDay = daySerial;
            t.WishX = wishX;
            t.WishZ = wishZ;
            _tasks[taskIndex] = t;
            Note(t, reason);
        }

        public void RecordBuilt(int taskIndex, int daySerial)
        {
            if (!InRange(taskIndex) || !Active(_tasks[taskIndex])) return;
            Finish(taskIndex, DepotTaskState.Built, "built", daySerial);
        }

        public void Abandon(int taskIndex, string reason, int daySerial)
        {
            if (!InRange(taskIndex) || !Active(_tasks[taskIndex])) return;
            Finish(taskIndex, DepotTaskState.Abandoned, reason, daySerial);
        }

        // The flag was switched off (or the executors were): every active task is released at once, so no builder stays held.
        public void AbandonActive(string reason, int daySerial)
        {
            for (int t = 0; t < _tasks.Count; t++)
                if (Active(_tasks[t])) Finish(t, DepotTaskState.Abandoned, reason, daySerial);
        }

        // True while an own unbuilt depot still stands at the wish placeholder (-20000): a wish waiting in vanilla's queue
        // (verify r3 F9). WorkDownConstructionWishes serves aiplacements[0] only, GamePrefs.amountofconstructionsitesearches terrain
        // probes per AI update, and drops it only when it is placed or has searched out to buildingsitemaxradius (97538-97591), so a
        // wish behind others can be pending across a review. The wait is bounded by Sync's TimeoutDays on every active task.
        private static bool PlacementPending(CampaignSnapshot s)
        {
            for (int i = 0; i < s.Supply.Count; i++)
            {
                var source = s.Supply[i];
                if (source.Kind == SupplySourceKind.Depot && source.Owner == s.Alliance && !source.Built
                    && source.X <= PlaceholderBelow && source.Z <= PlaceholderBelow) return true;
            }
            return false;
        }

        // The centre a task's depot is looked for around: where the wish was placed once it is Wished, the site before.
        private static void Centre(in DepotTaskRecord task, out float x, out float z)
        {
            x = task.State == DepotTaskState.Wished ? task.WishX : task.X;
            z = task.State == DepotTaskState.Wished ? task.WishZ : task.Z;
        }

        // True when a built supply depot of the alliance's (or an ally's: the snapshot lists those) stands within `radius` of the
        // task's centre (Centre). A wish is placed by WorkDownConstructionWishes at the nearest suitable ground around the wish
        // position, up to GamePrefs.buildingsitemaxradius away (97557-97585), so the returned CBuilding (a placeholder at -20000,
        // replaced by a new building) is not tracked: the finished depot is found among the snapshot's supply sources.
        public static bool SiteBuilt(CampaignSnapshot s, in DepotTaskRecord task, float radius)
        {
            if (s == null || radius <= 0f) return false;
            Centre(task, out float cx, out float cz);
            for (int i = 0; i < s.Supply.Count; i++)
            {
                var source = s.Supply[i];
                if (source.Kind != SupplySourceKind.Depot || !source.Built) continue;
                if (Distance(source.X, source.Z, cx, cz) <= radius) return true;
            }
            return false;
        }

        // True when a depot owned by the snapshot's alliance (not an ally's) stands within `radius` of the task's centre; `unbuiltOnly`
        // counts only one still rising.
        public static bool SiteHasOwnDepot(CampaignSnapshot s, in DepotTaskRecord task, float radius, bool unbuiltOnly)
        {
            return NearestOwnDepot(s, task, radius, unbuiltOnly, 0f, 0f, float.PositiveInfinity, out _, out _);
        }

        // The nearest such depot's position: the one Adopt takes as the task's (a flagged builder's command range, 50-100 for a corps
        // or army, reaches depots that are not this site's, so only one within the placement radius of the site counts). A depot rises
        // only while an own unit stands within that unit's bugle range of it (96626-96655), so Adopt also needs it within `builderRange`
        // of the builder at (builderX, builderZ) (verify r3 F8; PositiveInfinity: no such limit).
        public static bool NearestOwnDepot(CampaignSnapshot s, in DepotTaskRecord task, float radius, bool unbuiltOnly, float builderX, float builderZ, float builderRange, out float x, out float z)
        {
            x = 0f;
            z = 0f;
            if (s == null || radius <= 0f) return false;
            Centre(task, out float cx, out float cz);
            float best = radius;
            bool found = false;
            for (int i = 0; i < s.Supply.Count; i++)
            {
                var source = s.Supply[i];
                if (source.Kind != SupplySourceKind.Depot || source.Owner != s.Alliance) continue;
                if (unbuiltOnly && source.Built) continue;
                if (Distance(source.X, source.Z, builderX, builderZ) >= builderRange) continue;
                float d = Distance(source.X, source.Z, cx, cz);
                if (d > best) continue;
                best = d;
                x = source.X;
                z = source.Z;
                found = true;
            }
            return found;
        }

        // The builder of a Marching or Wished task stays out of vanilla's pools.
        public bool HoldsBuilder(int instanceId)
        {
            if (instanceId < 0) return false;
            for (int t = 0; t < _tasks.Count; t++)
                if (Active(_tasks[t]) && _tasks[t].BuilderInstanceId == instanceId) return true;
            return false;
        }

        // The builders the depot ledger holds, for the planner and the allocator (plan G5c, verify r3 F7 and the G3 ruling): the bound
        // builder of every active task. A Wished task's builder is listed at the wish position (the site for an Ahead wish, whose builder
        // may still be marching to it): the wish is paid at once and the depot rises only while the builder stays within its bugle range
        // (96626-96648), so from the wish until Built or wish-lost the allocator keeps it there, whatever the plan does with the task. A
        // Marching task's builder is listed at the site (not Wished): the allocator still re-picks it, but G3 counts it in no mass. Only
        // a bound builder is listed, and Sync binds none the planner may not move (BindBuilder abandons a task whose formation is the W&L
        // player's), so the set never holds one.
        public void CollectHeldBuilders(List<HeldBuilder> into)
        {
            into.Clear();
            for (int t = 0; t < _tasks.Count; t++)
            {
                var task = _tasks[t];
                if (!Active(task) || task.BuilderInstanceId < 0) continue;
                bool wished = task.State == DepotTaskState.Wished;
                into.Add(new HeldBuilder
                {
                    InstanceId = task.BuilderInstanceId, SectorId = task.SectorId, Wished = wished,
                    TargetX = wished ? task.WishX : task.X, TargetZ = wished ? task.WishZ : task.Z
                });
            }
        }

        // The sidecar's records (G6): builders come back by unit key, instance ids are rebound at the next Sync.
        public void Restore(IEnumerable<DepotTaskRecord> tasks)
        {
            _tasks.Clear();
            _transitions.Clear();
            if (tasks == null) return;
            foreach (var t in tasks)
            {
                var r = t;
                r.BuilderInstanceId = -1;
                _tasks.Add(r);
            }
        }

        internal void ClearTransitions()
        {
            _transitions.Clear();
        }

        // A finished task near this site that ended less than RetryAfterAbandonDays ago (Abandoned only; a Built one is a depot).
        private bool CoolingDown(float x, float z, int day)
        {
            for (int t = 0; t < _tasks.Count; t++)
            {
                if (_tasks[t].State != DepotTaskState.Abandoned || _tasks[t].EndedDay < 0) continue;
                if (day - _tasks[t].EndedDay >= RetryAfterAbandonDays) continue;
                if (Distance(_tasks[t].X, _tasks[t].Z, x, z) <= SiteMatchRadius) return true;
            }
            return false;
        }

        // Binds the record's builder to a live formation (by instance id, else by unit key after a restore) and drops a task whose
        // builder is gone or whose formation the planner may not move (never the W&L player's own command). False: abandoned.
        private bool BindBuilder(int t, CampaignSnapshot s)
        {
            var task = _tasks[t];
            int i = task.BuilderInstanceId >= 0 ? s.IndexOfOwn(task.BuilderInstanceId) : -1;
            if (i < 0) i = s.IndexOfKey(task.BuilderUnitKey);
            if (i < 0)
            {
                Abandon(t, "builder-lost", s.DaySerial);
                return false;
            }
            if (!WlChainRules.MayMove(s.Own[i].Chain))
            {
                Abandon(t, "player-chain", s.DaySerial);
                return false;
            }
            task.BuilderInstanceId = s.Own[i].InstanceId;
            task.BuilderUnitKey = s.Own[i].UnitKey;
            _tasks[t] = task;
            return true;
        }

        // A marching task follows the plan: the site moves with the frontier (within SiteMatchRadius) and the builder is whoever the
        // allocation assigns to it now; with no plan task near, the task is dropped.
        private void FollowPlan(int t, TheaterPlan plan, Allocation allocation, CampaignSnapshot s)
        {
            var task = _tasks[t];
            int match = -1;
            float best = SiteMatchRadius;
            for (int k = 0; k < plan.DepotTasks.Count; k++)
            {
                float d = Distance(plan.DepotTasks[k].X, plan.DepotTasks[k].Z, task.X, task.Z);
                if (d > best) continue;
                best = d;
                match = k;
            }
            if (match < 0)
            {
                Abandon(t, "plan-dropped", s.DaySerial);
                return;
            }
            task.X = plan.DepotTasks[match].X;
            task.Z = plan.DepotTasks[match].Z;
            task.SectorId = plan.DepotTasks[match].SectorId;
            int builder = BuilderAssignedAt(allocation, task.X, task.Z);
            if (builder >= 0 && builder != task.BuilderInstanceId)
            {
                int i = s.IndexOfOwn(builder);
                if (i >= 0 && WlChainRules.MayMove(s.Own[i].Chain))
                {
                    task.BuilderInstanceId = builder;
                    task.BuilderUnitKey = s.Own[i].UnitKey;
                }
            }
            _tasks[t] = task;
        }

        // Starts the next task: the construction ledger's TopSupplyDepot when its task can start, else the first plan task
        // (plan order is priority order) that can.
        private void StartNext(TheaterPlan plan, Allocation allocation, CampaignSnapshot s, ConstructionOutput construction, int day)
        {
            int top = TopTaskIndex(construction);
            int pick = -1;
            for (int k = 0; k < plan.DepotTasks.Count; k++)
            {
                if (!CanStart(plan.DepotTasks[k], k, allocation, s, construction, day)) continue;
                if (k == top) { pick = k; break; }
                if (pick < 0) pick = k;
            }
            if (pick < 0) return;
            var site = plan.DepotTasks[pick];
            int builder = BuilderAssignedAt(allocation, site.X, site.Z);
            int i = s.IndexOfOwn(builder);
            var task = new DepotTaskRecord
            {
                X = site.X, Z = site.Z, SectorId = site.SectorId, BuilderInstanceId = builder, BuilderUnitKey = s.Own[i].UnitKey,
                State = DepotTaskState.Marching, StartedDay = day, EndedDay = -1
            };
            _tasks.Add(task);
            Note(task, "task-started");
        }

        private bool CanStart(DepotTask site, int planTaskIndex, Allocation allocation, CampaignSnapshot s, ConstructionOutput construction, int day)
        {
            if (Suppressed(construction, planTaskIndex)) return false;
            int builder = BuilderAssignedAt(allocation, site.X, site.Z);
            if (builder < 0) return false;
            int i = s.IndexOfOwn(builder);
            if (i < 0 || !WlChainRules.MayMove(s.Own[i].Chain) || HoldsBuilder(builder)) return false;
            for (int t = 0; t < _tasks.Count; t++)
                if (Active(_tasks[t]) && Distance(_tasks[t].X, _tasks[t].Z, site.X, site.Z) <= SiteMatchRadius) return false;
            return !CoolingDown(site.X, site.Z, day);
        }

        // The DepotBuilder the allocation put at this site (ForceAllocator.AssignDepotBuilders targets task.X, task.Z), -1 none.
        private static int BuilderAssignedAt(Allocation allocation, float x, float z)
        {
            for (int k = 0; k < allocation.Assignments.Count; k++)
            {
                var a = allocation.Assignments[k];
                if (a.Role == FormationRole.DepotBuilder && Distance(a.TargetX, a.TargetZ, x, z) <= AssignmentEpsilon) return a.InstanceId;
            }
            return -1;
        }

        // The construction ledger suppresses a candidate it cannot let through (ConstructionIntentLedger.IsSuppressed). For a depot
        // that is only VanillaInvalid: the ledger lets depots through every fiscal posture (IsEmergencyAllowed) and has no
        // depot-specific suppression.
        private static bool Suppressed(ConstructionOutput construction, int planTaskIndex)
        {
            if (construction == null || construction.Suppressions == null) return false;
            string name = CandidateName(planTaskIndex);
            for (int i = 0; i < construction.Suppressions.Length; i++)
            {
                var sup = construction.Suppressions[i];
                if (sup.Kind == ConstructionCandidateKind.SupplyDepot && string.Equals(sup.Name, name, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // The plan task index of the ledger's TopSupplyDepot, -1 none.
        private static int TopTaskIndex(ConstructionOutput construction)
        {
            if (construction == null || construction.TopSupplyDepot.Kind != ConstructionCandidateKind.SupplyDepot) return -1;
            string name = construction.TopSupplyDepot.Name;
            string prefix = CandidateName(0).Substring(0, CandidateName(0).Length - 1);
            if (name == null || !name.StartsWith(prefix, StringComparison.Ordinal)) return -1;
            return int.TryParse(name.Substring(prefix.Length), out int index) ? index : -1;
        }

        private void Finish(int t, DepotTaskState state, string reason, int day)
        {
            var task = _tasks[t];
            task.State = state;
            task.EndedDay = day;
            _tasks[t] = task;
            Note(task, reason);
        }

        private void Note(DepotTaskRecord t, string reason)
        {
            _transitions.Add(new DepotTaskTransition { X = t.X, Z = t.Z, BuilderUnitKey = t.BuilderUnitKey, State = t.State, Reason = reason });
        }

        // Finished records are history for the retry cooldown only: the oldest go first.
        private void Prune()
        {
            int finished = 0;
            for (int t = 0; t < _tasks.Count; t++) if (!Active(_tasks[t])) finished++;
            for (int t = 0; t < _tasks.Count && finished > MaxFinishedRecords; )
            {
                if (Active(_tasks[t])) { t++; continue; }
                _tasks.RemoveAt(t);
                finished--;
            }
        }

        private int ActiveCount()
        {
            int n = 0;
            for (int t = 0; t < _tasks.Count; t++) if (Active(_tasks[t])) n++;
            return n;
        }

        private static bool Active(in DepotTaskRecord t)
        {
            return t.State == DepotTaskState.Marching || t.State == DepotTaskState.Wished;
        }

        private bool InRange(int taskIndex)
        {
            return taskIndex >= 0 && taskIndex < _tasks.Count;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    // The per-alliance ledgers: executor bookkeeping, written by DepotSteeringRuntime inside the review and read by the availability
    // patches (CampaignWithholdView.HoldsBuilder). Per campaign.
    public static class DepotTaskLedgers
    {
        private static readonly DepotTaskLedger[] Ledgers = { new DepotTaskLedger(), new DepotTaskLedger() };

        static DepotTaskLedgers()
        {
            CampaignScope.Register("campaign-planner-depot-tasks", Reset);
        }

        private static void Reset()
        {
            for (int a = 0; a < Ledgers.Length; a++) Ledgers[a] = new DepotTaskLedger();
        }

        public static DepotTaskLedger For(int alliance)
        {
            return alliance >= 0 && alliance < Ledgers.Length ? Ledgers[alliance] : null;
        }
    }

    // One SupplyDepot candidate per planner depot task for the construction ledger (plan G5c: ConstructionRuntime.BuildInput wires
    // the ledger's dead SupplyDepot kind to the planner). The candidate's supply pressure is the alliance's, scaled by the task's
    // priority (1, then halved per site further on), so the ledger's TopSupplyDepot is the task nearest the supply frontier.
    public static class DepotCandidates
    {
        // theaterOf: the theater of a map point; ownIipFor: true when an own IIP exists for the wish to call (AddConstructionWish
        // needs one, 97488: without it vanilla's job 19 starts no wish either, 14760-14772).
        public static void Add(ConstructionInput input, TheaterPlan plan, Func<float, float, Theater> theaterOf, Func<float, float, bool> ownIipFor)
        {
            if (input == null || plan == null) return;
            for (int k = 0; k < plan.DepotTasks.Count; k++)
            {
                var task = plan.DepotTasks[k];
                var theater = theaterOf != null ? theaterOf(task.X, task.Z) : Theater.Unknown;
                input.Candidates.Add(new ConstructionCandidate
                {
                    Kind = ConstructionCandidateKind.SupplyDepot,
                    Name = DepotTaskLedger.CandidateName(k),
                    Theater = theater,
                    SupplyPressure = Clamp01(input.SupplyPressure) * Clamp01(task.Priority),
                    TransportPressure = Clamp01(input.TransportPressure) * Clamp01(task.Priority),
                    CapitalThreat = ConstructionThreat.ForTheater(input.SectorThreats, theater),
                    VanillaValid = ownIipFor != null && ownIipFor(task.X, task.Z),
                    SupportsActiveArmyCorridor = true
                });
            }
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }
    }
}
