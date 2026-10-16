using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    public enum DepotTaskState { Marching = 0, Wished = 1, Built = 2, Abandoned = 3 }

    public struct DepotTaskRecord
    {
        public float X, Z;
        public int SectorId;
        public int BuilderInstanceId;   // in-session lookup only, never persisted
        public string BuilderUnitKey;   // persisted identity
        public DepotTaskState State;
        public int StartedDay;          // the day the current state began: the start, then the wish
    }

    // A state change the runtime logs as `[DepotTask] alliance=<a> site=<x>,<z> builder=<unit> state=<...> reason=<...>`.
    public struct DepotTaskTransition
    {
        public float X, Z;
        public string BuilderUnitKey;
        public DepotTaskState State;
        public string Reason;
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

        private readonly List<DepotTaskRecord> _tasks = new List<DepotTaskRecord>();
        private readonly List<DepotTaskTransition> _transitions = new List<DepotTaskTransition>();
        // The day each finished record ended, parallel to _tasks (the retry cooldown); -1 while active.
        private readonly List<int> _endedDay = new List<int>();

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
        public bool ShouldWish(int taskIndex, float builderX, float builderZ, float bugleRange, int daySerial)
        {
            if (!InRange(taskIndex) || _tasks[taskIndex].State != DepotTaskState.Marching) return false;
            float dx = builderX - _tasks[taskIndex].X, dz = builderZ - _tasks[taskIndex].Z;
            return dx * dx + dz * dz <= bugleRange * bugleRange;
        }

        public void RecordWished(int taskIndex, int daySerial)
        {
            if (!InRange(taskIndex) || _tasks[taskIndex].State != DepotTaskState.Marching) return;
            var t = _tasks[taskIndex];
            t.State = DepotTaskState.Wished;
            t.StartedDay = daySerial;
            _tasks[taskIndex] = t;
            Note(t, "wish-placed");
        }

        public void RecordBuilt(int taskIndex)
        {
            if (!InRange(taskIndex) || !Active(_tasks[taskIndex])) return;
            Finish(taskIndex, DepotTaskState.Built, "built", -1);
        }

        // The day matters to the retry cooldown (RetryAfterAbandonDays); the two-argument form is for a caller with no day.
        public void Abandon(int taskIndex, string reason)
        {
            Abandon(taskIndex, reason, -1);
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

        // True when a built supply depot of the alliance's (or an ally's: the snapshot lists those) stands within `radius` of the
        // task's site. A wish is placed by WorkDownConstructionWishes at the nearest suitable ground around the wish position, up
        // to GamePrefs.buildingsitemaxradius away (97557-97585), so the returned CBuilding (a placeholder at -20000, replaced by a
        // new building) is not tracked: the finished depot is found among the snapshot's supply sources.
        public static bool SiteBuilt(CampaignSnapshot s, in DepotTaskRecord task, float radius)
        {
            if (s == null || radius <= 0f) return false;
            for (int i = 0; i < s.Supply.Count; i++)
            {
                var source = s.Supply[i];
                if (source.Kind != SupplySourceKind.Depot || !source.Built) continue;
                if (Distance(source.X, source.Z, task.X, task.Z) <= radius) return true;
            }
            return false;
        }

        // The builder of a Marching or Wished task stays out of vanilla's pools.
        public bool HoldsBuilder(int instanceId)
        {
            if (instanceId < 0) return false;
            for (int t = 0; t < _tasks.Count; t++)
                if (Active(_tasks[t]) && _tasks[t].BuilderInstanceId == instanceId) return true;
            return false;
        }

        // The sidecar's records (G6): builders come back by unit key, instance ids are rebound at the next Sync.
        public void Restore(IEnumerable<DepotTaskRecord> tasks)
        {
            _tasks.Clear();
            _endedDay.Clear();
            _transitions.Clear();
            if (tasks == null) return;
            foreach (var t in tasks)
            {
                var r = t;
                r.BuilderInstanceId = -1;
                _tasks.Add(r);
                _endedDay.Add(-1);
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
                if (_tasks[t].State != DepotTaskState.Abandoned || _endedDay[t] < 0) continue;
                if (day - _endedDay[t] >= RetryAfterAbandonDays) continue;
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
                State = DepotTaskState.Marching, StartedDay = day
            };
            _tasks.Add(task);
            _endedDay.Add(-1);
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
            _tasks[t] = task;
            _endedDay[t] = day >= 0 ? day : _endedDay[t] >= 0 ? _endedDay[t] : task.StartedDay;
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
                _endedDay.RemoveAt(t);
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
