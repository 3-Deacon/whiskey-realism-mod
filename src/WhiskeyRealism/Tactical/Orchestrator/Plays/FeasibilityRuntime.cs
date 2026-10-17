using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.AI;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Battle.Producers;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // BC14 runtime half of the maneuver feasibility (requirement-runtime-wiring). Excluded from the test csproj: it reads GamePrefs, the
    // battle clock and the NavMesh, builds the pure evaluation's input from them, and drives the pure pieces in the coordinator's order:
    //   (2) the crossing probes and classification,
    //   (3) the next slice of a running evaluation, or a new one when the last was published and EvaluateIntervalHours has passed
    //       (forced to completion before the initial pick and the night replan),
    //   (4) the monitor, on a new heavy snapshot generation,
    //   one NavMesh confirmation query per side per tick.
    // One ledger per side; the coordinator's heavy path is its only writer. With Enable Maneuver Feasibility off nothing is built and the
    // army's lookup is null. Any exception degrades to a warning and leaves the last published ledger as it was. Never throws.
    internal static class FeasibilityRuntime
    {
        private sealed class SideState
        {
            public readonly int Alliance;
            public readonly FeasibilityLedger Ledger = new FeasibilityLedger();
            public readonly CrossingWatch Crossings = new CrossingWatch();
            public readonly LegCostCache Cache = new LegCostCache();
            public readonly ConfirmedLegs Legs = new ConfirmedLegs();
            public readonly ExploitLookup Exploits;
            public readonly LedgerCrossingPicture GatePicture;
            public readonly MonitorWorld World;
            public readonly string[] ChildIds = new string[ManeuverFeasibility.MaxChildren];
            public readonly bool[] Deployed = new bool[ManeuverFeasibility.MaxChildren];
            public readonly bool[] Moving = new bool[ManeuverFeasibility.MaxChildren];
            public readonly string[] BoundGuards = new string[ManeuverFeasibility.MaxChildren];
            public readonly string[] ScreenChildren = new string[ManeuverFeasibility.MaxChildren];
            public readonly BattlePlanId[] Candidates = new BattlePlanId[FeasibilityLedger.MaxCandidates];
            public int ArmyCommanderIndex = -1;
            public double LastStartHours = double.NegativeInfinity;
            public int Slices;
            public bool Forced;
            public int MonitoredGeneration = -1;
            /// <summary>A new heavy snapshot wants the monitor to re-evaluate the selected plan; cleared when a run starts on it.</summary>
            public bool MonitorWanted;
            public string LastBroken;
            public string LastSuppression;

            public SideState(int alliance)
            {
                Alliance = alliance;
                Exploits = new ExploitLookup(alliance);
                GatePicture = new LedgerCrossingPicture(Crossings, Ledger);
                World = new MonitorWorld(this);
            }

            public void Reset()
            {
                Legs.Clear();
                Cache.Clear();
                Ledger.Job.Abort();
                LastStartHours = double.NegativeInfinity;
                Slices = 0;
                Forced = false;
                MonitoredGeneration = -1;
                MonitorWanted = false;
                LastBroken = null;
                LastSuppression = null;
                ArmyCommanderIndex = -1;
                Array.Clear(ChildIds, 0, ChildIds.Length);
                Array.Clear(BoundGuards, 0, BoundGuards.Length);
                Array.Clear(ScreenChildren, 0, ScreenChildren.Length);
            }
        }

        private static readonly SideState[] Sides = { new SideState(0), new SideState(1) };
        private static bool _rulesRead;
        private static MarchRules _rules;
        private static float[] _reach;
        private static float[] _pathX = new float[64];
        private static float[] _pathZ = new float[64];

        static FeasibilityRuntime()
        {
            BattleScope.Register("feasibility-runtime", Reset);
        }

        private static void Reset()
        {
            for (int i = 0; i < Sides.Length; i++) Sides[i].Reset();
            _rulesRead = false;
            _reach = null;
        }

        private static SideState StateOf(int alliance) => alliance >= 0 && alliance < Sides.Length ? Sides[alliance] : null;

        // ---------------------------------------------------------------------------------------------- the entry points

        /// <summary>
        /// The army exists: its ledger is pushed (null with the flag off). Called once per side, before the initial pick, with the vanilla
        /// commander index of the side's army commander (the personality table's key).
        /// </summary>
        internal static void Attach(ArmyOrchestrator army, int armyCommanderIndex)
        {
            if (army == null) return;
            SideState st = StateOf(army.AllianceId);
            if (st == null) return;
            st.ArmyCommanderIndex = armyCommanderIndex;
            army.SetFeasibility(Plugin.ManeuverFeasibilityEnabled() ? st.Ledger : null);
        }

        /// <summary>
        /// The forced evaluation, to completion: before the initial pick and before the night replan, so the pick reads a ledger of the ground as
        /// the side knows it now (at battle start normally Unknown: the children are not attached and the picture is not ready).
        /// </summary>
        internal static void EvaluateNow(TacticalBattleOrchestrator side, BattleUnits bunits, BattleSnapshot snapshot, ArmyEvidence evidence, TacticalFrontAxis axis)
        {
            Run(side, bunits, snapshot, evidence, axis, forced: true);
        }

        /// <summary>
        /// The side's heavy-path work for this tick: probes and classification, the evaluation slice, the monitor on a new snapshot generation, and
        /// one confirmation query.
        /// </summary>
        internal static void Tick(TacticalBattleOrchestrator side, BattleUnits bunits, BattleSnapshot snapshot, ArmyEvidence evidence, TacticalFrontAxis axis,
            int snapshotGeneration)
        {
            Run(side, bunits, snapshot, evidence, axis, forced: false, snapshotGeneration);
        }

        private static void Run(TacticalBattleOrchestrator side, BattleUnits bunits, BattleSnapshot snapshot, ArmyEvidence evidence, TacticalFrontAxis axis,
            bool forced, int snapshotGeneration = -1)
        {
            ArmyOrchestrator army = side?.Army;
            if (army == null || snapshot == null) return;
            SideState st = StateOf(army.AllianceId);
            if (st == null) return;
            if (!Plugin.ManeuverFeasibilityEnabled())
            {
                if (army.Feasibility != null) army.SetFeasibility(null);
                return;
            }
            if (army.Feasibility == null) army.SetFeasibility(st.Ledger);
            try
            {
                double now = TacticalDoctrineTime.NowBattleHours;
                ReadRulesOnce();
                st.Legs.SetClock(now);
                RiverRegions regions = RiverRegionsRuntime.Ready();

                // One slice budget for the tick, shared by the monitor and the evaluation, the monitor first (FeasibilityTick): a replan the
                // monitor owes is never starved by a wide Defender evaluation; each resumes on the next tick where the budget ran out.
                long deadline = Stopwatch.GetTimestamp() + (long)(ManeuverFeasibility.EvaluateSliceMs * 0.001 * Stopwatch.Frequency);
                using (TelemetryPerf.Scope("tactical.feasibility.evaluate", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
                    Classify(st, snapshot, regions, now);

                if (!forced && (snapshotGeneration < 0 || snapshotGeneration != st.MonitoredGeneration))
                {
                    st.MonitoredGeneration = snapshotGeneration;
                    st.MonitorWanted = true;
                }
                var work = new TickWork
                {
                    St = st, Side = side, Army = army, Bunits = bunits, Snapshot = snapshot, Evidence = evidence, Axis = axis, Regions = regions,
                    Now = now, Forced = forced, Deadline = deadline,
                };
                FeasibilityTick.Run(ref work);

                // BC14W: the works hold follows BC11's phases every heavy tick, not only when the sliced monitor finishes a run.
                st.World.Bind(snapshot, regions);
                st.Ledger.RefreshWorksHold(st.World);
                ExploitTable.ForAlliance(army.AllianceId).SetWorksView(st.Ledger.WorksHold(out _), st.Ledger.HeldWorks);
                if (Plugin.EnemyWorksDoctrineEnabled())
                {
                    FeasibilityTelemetry.EnemyWorks(army.AllianceId, st.Ledger, FortificationPointsRuntime.TableOf(army.AllianceId), now);
                    FeasibilityTelemetry.WorksHold(army.AllianceId, st.Ledger, army.WorksGuarded, now);
                }
                // BC14O: the victory clock's pressure and the main effort the director picked (the army's band and screens as the last allocation left them).
                if (Plugin.ObjectiveDoctrineEnabled())
                {
                    FeasibilityTelemetry.ObjectivePressure(army.AllianceId, st.Ledger, now);
                    if (!army.TryObjectiveSector(out int band)) band = -1;
                    FeasibilityTelemetry.MainEffort(army.AllianceId, st.Ledger, army.CurrentObjectivePick, band, army.SecondaryScreens, now);
                }

                if (forced) return;

                using (TelemetryPerf.Scope("tactical.feasibility.navmesh", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
                    st.Legs.Process(NavQuery);
                EmitCrossingRow(army.AllianceId, now);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:feasibility-tick", "[Feasibility] the heavy path threw, the last ledger stands: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // The tick's two jobs for FeasibilityTick (a struct: no allocation per tick).
        private struct TickWork : IFeasibilityTickWork
        {
            public SideState St;
            public TacticalBattleOrchestrator Side;
            public ArmyOrchestrator Army;
            public BattleUnits Bunits;
            public BattleSnapshot Snapshot;
            public ArmyEvidence Evidence;
            public TacticalFrontAxis Axis;
            public RiverRegions Regions;
            public double Now;
            public bool Forced;
            public long Deadline;

            public bool MonitorPending => !Forced && (St.MonitorWanted || St.Ledger.MonitorRunning);

            public void RunMonitor()
            {
                using (TelemetryPerf.Scope("tactical.feasibility.monitor", TelemetryLayer.Tactical, TelemetryCategory.Performance, 0.5))
                    Monitor(St, Side, Army, Bunits, Snapshot, Evidence, Axis, Regions, Now, Deadline);
            }

            public void RunEvaluation()
            {
                using (TelemetryPerf.Scope("tactical.feasibility.evaluate", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
                    Evaluate(St, Side, Army, Bunits, Snapshot, Evidence, Axis, Regions, Now, Forced, Deadline);
            }
        }

        // ------------------------------------------------------------------------------------------------ probes and the evaluation

        private static void Classify(SideState st, BattleSnapshot snapshot, RiverRegions regions, double now)
        {
            // BC14W: the works probes share the crossing probes' handle, scope and slice (spec § 7A): the side's works pieces, oldest first.
            WorksPointTable works = FortificationPointsRuntime.TableOf(st.Alliance);
            if (regions == null && works == null) return;
            using (PerceivedGroundRuntime.Handle ground = PerceivedGroundRuntime.Open(snapshot))
            {
                if (regions != null) st.Crossings.Classify(snapshot, regions, BattleTerrainGridRuntime.Get(), ground.Oracle, _reach, now);
                works?.ProbeStep(ground.Oracle, WorksRules.MaxWorksProbesPerTick);
            }
        }

        private static void Evaluate(SideState st, TacticalBattleOrchestrator side, ArmyOrchestrator army, BattleUnits bunits, BattleSnapshot snapshot, ArmyEvidence evidence,
            TacticalFrontAxis axis, RiverRegions regions, double now, bool forced, long deadline)
        {
            FeasibilityLedger ledger = st.Ledger;
            FeasibilityJob job = ledger.Job;
            if (forced)
            {
                BuildInput(st, army, bunits, snapshot, evidence, axis, regions, now, withCandidates: true, out FeasibilityInput full);
                ManeuverFeasibility.Evaluate(in full, ledger);
                st.Slices = 1;
                st.Forced = true;
                st.LastStartHours = now;
                Published(st, regions, now);
                return;
            }

            if (!job.Running)
            {
                // A cheap compare every tick: a new evaluation starts only when the last one was published and its interval has passed.
                if (ledger.HasEvaluation && now - st.LastStartHours < ManeuverFeasibility.EvaluateIntervalHours && now >= st.LastStartHours) return;
                st.LastStartHours = now;
                st.Slices = 0;
                st.Forced = false;
            }
            FeasibilityInput input = default;
            if (!job.Running) BuildInput(st, army, bunits, snapshot, evidence, axis, regions, now, withCandidates: true, out input);
            bool done = ManeuverFeasibility.EvaluateSlice(in input, job, ledger, deadline, Stopwatch.GetTimestamp);
            st.Slices++;
            if (done) Published(st, regions, now);
        }

        // A finished evaluation: the legs to confirm, the rows.
        private static void Published(SideState st, RiverRegions regions, double now)
        {
            FeasibilityLedger ledger = st.Ledger;
            ConfirmRequests.Collect(ledger, st.Legs, regions);
            CrossingGraphView graph = ledger.Job.Graph;
            FeasibilityTelemetry.Published(st.Alliance, ledger, st.Slices, st.Forced, graph.Searches, graph.LegsSampled, st.Cache.Overflow, st.Legs.QueuedCount, now);
        }

        // ------------------------------------------------------------------------------------------------------ the monitor

        private static void Monitor(SideState st, TacticalBattleOrchestrator side, ArmyOrchestrator army, BattleUnits bunits, BattleSnapshot snapshot, ArmyEvidence evidence,
            TacticalFrontAxis axis, RiverRegions regions, double now, long deadline)
        {
            FeasibilityLedger ledger = st.Ledger;
            if (!army.HasPlan || !ledger.HasSelection || !ledger.HasEvaluation) { st.MonitorWanted = false; return; }
            // A run in progress continues from its own copy of the input; a new one is built from this tick's snapshot and clears the want.
            FeasibilityInput input = default;
            if (!ledger.MonitorRunning)
            {
                st.MonitorWanted = false;
                BuildInput(st, army, bunits, snapshot, evidence, axis, regions, now, withCandidates: false, out input);
            }
            if (!ManeuverFeasibility.EvaluateSelectedSlice(in input, ledger, deadline, Stopwatch.GetTimestamp)) return;
            st.World.Bind(snapshot, regions);
            ledger.Assess(st.World, now);
            string reason = ledger.BrokenAssumption;
            string committed = ledger.CommittedReason;
            // A monitor row when what the monitor says changed: a broken assumption named, or a committed plan's suppression.
            if (!string.Equals(reason, st.LastBroken, StringComparison.Ordinal) || (committed != null && !string.Equals(committed, st.LastSuppression, StringComparison.Ordinal)))
            {
                if (ledger.TryGet(ledger.SelectedPlan, out CandidateResult selected))
                    FeasibilityTelemetry.Bodies(st.Alliance, ledger, in selected, reason ?? committed, reason == null && committed != null ? ReplanOutcome.SuppressedByCommitment : null, now);
            }
            st.LastBroken = reason;
            st.LastSuppression = committed;
        }

        /// <summary>The coordinator's replan returned a suppression for a broken assumption: its row names why (cooldown, margin, preempted).</summary>
        internal static void NoteSuppression(int alliance, string assumption, string suppression, double now)
        {
            try
            {
                SideState st = StateOf(alliance);
                if (st == null || suppression == null || !st.Ledger.HasSelection) return;
                if (st.Ledger.TryGet(st.Ledger.SelectedPlan, out CandidateResult selected))
                    FeasibilityTelemetry.Bodies(alliance, st.Ledger, in selected, assumption, suppression, now);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:feasibility-tick", "[Feasibility] suppression row threw: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        /// <summary>
        /// The side's picture for the crossing gate (RiverCrossingGateRuntime): BC14's regions with the side's own crossing watch and ledger, so
        /// a refused move posts its look request into the same look mechanism and the selected plan's crossings are never refused. Null for a
        /// side with no state, with the feasibility off, or before the regions are built (the gate then lets the leg through).
        /// </summary>
        internal static ICrossingPicture CrossingPicture(int alliance)
        {
            SideState st = StateOf(alliance);
            RiverRegions regions = RiverRegionsRuntime.Ready();
            if (st == null || regions == null || !Plugin.ManeuverFeasibilityEnabled()) return null;
            st.GatePicture.Bind(regions);
            return st.GatePicture;
        }

        /// <summary>The crossing the maneuver's via sits on, as the side knows it now (the first-via OnceLog).</summary>
        internal static CrossingState StateAt(int alliance, float x, float z)
        {
            SideState st = StateOf(alliance);
            RiverRegions regions = RiverRegionsRuntime.Ready();
            if (st == null || regions == null) return CrossingState.Unknown;
            int best = -1;
            float bestD = float.MaxValue;
            for (int link = 0; link < regions.LinkCount; link++)
            {
                regions.LinkCrossing(link, out float lx, out float lz, out _, out _);
                float d = (lx - x) * (lx - x) + (lz - z) * (lz - z);
                if (d < bestD) { bestD = d; best = link; }
            }
            return best >= 0 ? st.Crossings.StateOf(best) : CrossingState.Unknown;
        }

        /// <summary>The side's crossing picture row (every 0.1 doctrine-h), with the look requests the ledger is publishing.</summary>
        internal static void EmitCrossingRow(int alliance, double now)
        {
            SideState st = StateOf(alliance);
            RiverRegions regions = RiverRegionsRuntime.Ready();
            if (st == null || regions == null || !Plugin.ManeuverFeasibilityEnabled()) return;
            Span<float> xs = stackalloc float[ManeuverFeasibility.MaxLookRequests];
            Span<float> zs = stackalloc float[ManeuverFeasibility.MaxLookRequests];
            FeasibilityTelemetry.CrossingWatchState(alliance, st.Crossings, regions, st.Ledger.LookRequests(xs, zs), now);
        }

        // ------------------------------------------------------------------------------------------------------- the input

        private static void BuildInput(SideState st, ArmyOrchestrator army, BattleUnits bunits, BattleSnapshot snapshot, ArmyEvidence evidence, TacticalFrontAxis axis,
            RiverRegions regions, double now, bool withCandidates, out FeasibilityInput input)
        {
            int children = FillChildren(st, army, snapshot, out int bound);
            // The monitor re-evaluates the selected plan alone: it needs no candidate list (and no catalog pass).
            int candidates = withCandidates ? army.EligiblePlans(evidence, st.Candidates) : 0;
            TacticalEnemyContactLedger contact = ArmyEvidenceBuilder.ContactLedgerFor(st.Alliance);
            bool assumed = DeploymentPlanRuntime.TryAssumedEnemyPoint(st.Alliance, axis.OriginX, axis.OriginZ, out float assumedX, out float assumedZ, out _);
            // BC14W: the side's works pieces, re-read from the battle's ObstacleLines at each new evaluation (null with the flag off); the monitor's run
            // reads the table as that evaluation left it.
            WorksPointTable works = withCandidates
                ? FortificationPointsRuntime.Refresh(bunits, st.Alliance, axis.OriginX, axis.OriginZ)
                : FortificationPointsRuntime.TableOf(st.Alliance);
            input = new FeasibilityInput
            {
                Snapshot = snapshot, Axis = axis, Stance = army.EffectiveStance, Grid = BattleTerrainGridRuntime.Get(), Picture = TerrainPictureRuntime.Get(),
                Regions = regions, Crossings = st.Crossings, Rules = _rules, Army = ArmyPersonality(st, army),
                EnemyEstimate = contact != null ? contact.EstimatedEnemyStrength : 0f, Confidence = contact != null ? contact.Confidence((float)now) : 0f,
                EnemyIntent = army.CurrentIntentModel, NowHours = now, DaylightHours = DaylightHours(bunits),
                CurrentPlan = army.HasPlan ? army.CurrentPlan.PlanId : BattlePlanId.Unknown, HasPlan = army.HasPlan,
                Exploits = st.Exploits, Legs = st.Legs,
                ChildIds = st.ChildIds, ChildCount = children, DeployedReserve = st.Deployed, ChildMoving = st.Moving,
                BoundGuardChildIds = st.BoundGuards, BoundGuardCount = bound,
                Candidates = st.Candidates, CandidateCount = candidates, Crest = evidence.CrestLine, ReachByUnitType = _reach,
                HasAssumedEnemy = assumed, AssumedEnemyX = assumedX, AssumedEnemyZ = assumedZ, Cache = st.Cache, Works = works,
            };
            // BC14O: the live objectives and the victory clock, once per evaluation (the monitor's one-candidate run prices no objectives).
            if (withCandidates)
            {
                // The children that screen a secondary objective at the previous allocation are not in the main body the objectives are priced for.
                var intents = army.CurrentDirectChildIntents;
                int screens = 0;
                for (int c = 0; c < intents.Count && screens < st.ScreenChildren.Length; c++)
                    if (intents[c].HasScreenTarget) st.ScreenChildren[screens++] = intents[c].ChildId;
                input.ScreenChildIds = st.ScreenChildren;
                input.ScreenChildCount = screens;
                ObjectiveRuntime.Fill(bunits, st.Alliance, snapshot, (float)input.DaylightHours, ref input);
            }
        }

        // The direct children in the army's own order (the order the allocator, the threats' per-child times and the ledger's rows share).
        private static int FillChildren(SideState st, ArmyOrchestrator army, BattleSnapshot snapshot, out int bound)
        {
            var intents = army.CurrentDirectChildIntents;
            OrderExecutor executor = OrderExecutor.ForAlliance(st.Alliance);
            int n = Math.Min(intents.Count, ManeuverFeasibility.MaxChildren);
            for (int c = 0; c < n; c++)
            {
                string id = intents[c].ChildId;
                st.ChildIds[c] = id;
                int index = snapshot.IndexOfOwn(DirectChildIds.InstanceId(id));
                st.Deployed[c] = index >= 0 && executor != null && DeploymentDoctrine.IsDeployedReserve(snapshot, executor, index);
                st.Moving[c] = PlayExecutorRuntime.IsMoving(st.Alliance, id);
            }
            ReserveReleaseState state = ReserveReleaseState.ForAlliance(st.Alliance);
            bound = 0;
            for (int b = 0; state != null && b < state.BoundCount && bound < st.BoundGuards.Length; b++) st.BoundGuards[bound++] = state.BindingAt(b).ChildId;
            return n;
        }

        // requirement-bc13-personality: the army commander's entry of the personality table (the roster vector with the per-commander flag off).
        private static OpportunityPersonality ArmyPersonality(SideState st, ArmyOrchestrator army)
        {
            CommanderPersonalityTable table = CommanderPersonalityRuntime.TableFor(st.Alliance);
            PersonalityVector tableVector = default;
            PersonalitySource tableSource = PersonalitySource.Default;
            bool hasEntry = table != null && table.TryGet(st.ArmyCommanderIndex, out tableVector, out tableSource);
            PersonalityVector vector = CommanderPersonalityDerivation.ChooseVector(Plugin.PerCommanderPersonalityEnabled(), hasEntry, in tableVector, tableSource,
                army.CommanderPersonality, out PersonalitySource source);
            return CommanderPersonalityDerivation.ToOpportunity(in vector, source, 1f);
        }

        // requirement-daylight: dayendhours[month - 1] less the battle hour (vanilla's own test); no term when the storm system is unreadable.
        private static double DaylightHours(BattleUnits bunits)
        {
            try
            {
                var storm = bunits != null ? bunits.uniStormSystem : null;
                int[] ends = GamePrefs.dayendhours;
                int month = storm != null ? storm.monthCounter - 1 : -1;
                if (storm != null && ends != null && month >= 0 && month < ends.Length)
                    return ends[month] - (storm.hourCounter + storm.minuteCounter / 60.0);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:feasibility-daylight-missing", "[Feasibility] the storm system threw, no daylight term: " + ex.GetType().Name + " " + ex.Message);
                return double.PositiveInfinity;
            }
            OnceLog.Warning("tactical-battle-core:feasibility-daylight-missing", "[Feasibility] the battle's storm system is missing, no daylight term");
            return double.PositiveInfinity;
        }

        // The vanilla values the timing model reads, once per battle (requirement-march-time, requirement-courier-time, requirement-crossing-state).
        private static void ReadRulesOnce()
        {
            if (_rulesRead) return;
            _rulesRead = true;
            float[,] speed = GamePrefs.unitbasicspeed;
            _rules = new MarchRules
            {
                BasicUnitSpeed = GamePrefs.basicunitspeed,
                InfantryBase = speed[TacticalUnitType.Infantry, 0],
                CavalryMountedBase = speed[TacticalUnitType.Cavalry, 1],
                ArtilleryLimberedBase = speed[TacticalUnitType.Artillery, 1],
                HorseArtilleryFactor = GamePrefs.horseartilleryspeedfactor,
                FatigueSpeedExponent = GamePrefs.fatiguefactorspeed,
                ProcessingHours = GamePrefs.processingtimegroupstandard,
                RouteProcessingHours = GamePrefs.processingtimegrouproute,
                BugleHours = GamePrefs.buglestandardrecognitiontime,
                BugleRange = GamePrefs.standardbuglerange,
                CourierSpeedPerHour = speed[TacticalUnitType.Courier, 1] * GamePrefs.runningspeedfact[TacticalUnitType.Courier] * GamePrefs.basicunitspeed,
                OrderDelays = GameVars.useorderdelays,
            };
            // Where a perceived regiment of each type would see a column: BC9's list reach over the line-of-sight table.
            _reach = new float[TacticalUnitType.MaxCombat + 1];
            float[] sight = GamePrefs.maximumlineofsight;
            for (int t = 0; t < _reach.Length; t++)
                _reach[t] = sight != null && t < sight.Length ? BattlePerception.RangeOnlyReach(sight[t], GamePrefs.distancetoenemytocancelinterruption) : 0f;
        }

        // ------------------------------------------------------------------------------------------------------------ NavMesh

        // One confirmation: a complete path costs its polyline in the route screen's cost units; a path that is not complete is no-path; an
        // exception leaves the leg an estimate.
        private static bool NavQuery(float x0, float z0, float x1, float z1, int crossing, out float cost, out bool noPath)
        {
            cost = 0f;
            noPath = false;
            try
            {
                NavMeshQueryFilter filter = PostureTargets.CreateNavFilter();
                NavMeshPath path = PostureTargets.SharedPath();
                if (NavMesh.CalculatePath(PostureTargets.AtTerrain(x0, z0), PostureTargets.AtTerrain(x1, z1), filter, path) && path.status == NavMeshPathStatus.PathComplete)
                {
                    int count = PostureTargets.ReadCorners(path, out Vector3[] corners);
                    if (_pathX.Length < count)
                    {
                        _pathX = new float[count * 2];
                        _pathZ = new float[count * 2];
                    }
                    for (int c = 0; c < count; c++)
                    {
                        _pathX[c] = corners[c].x;
                        _pathZ[c] = corners[c].z;
                    }
                    cost = RoadRoutePlanner.PolylineCost(BattleTerrainGridRuntime.Get(), _pathX, _pathZ, count);
                    noPath = !(cost > 0f);
                    return true;
                }
                noPath = true;
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:feasibility-nav-failed", "[Feasibility] a NavMesh confirmation threw, the leg stays an estimate: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------------------------------------------------ adapters

        /// <summary>BC13's exploit table as the evaluation sees it: an asset (and what is below it) is not offered, an ancestor stays at half strength.</summary>
        private sealed class ExploitLookup : IExploitLookup
        {
            private readonly int _alliance;
            public ExploitLookup(int alliance) { _alliance = alliance; }
            public bool IsExploiting(int instanceId) => ExploitTable.ForAlliance(_alliance).IsAssetOrBelow(instanceId);
            public bool HasExploitingDescendant(int instanceId) => ExploitTable.ForAlliance(_alliance).IsAboveAsset(instanceId);
        }

        /// <summary>What the monitor reads of BC11 and the crossing watch for the selected plan's children.</summary>
        private sealed class MonitorWorld : IMonitorWorld
        {
            private readonly SideState _side;
            private BattleSnapshot _snapshot;
            private RiverRegions _regions;

            public MonitorWorld(SideState side) { _side = side; }

            public void Bind(BattleSnapshot snapshot, RiverRegions regions)
            {
                _snapshot = snapshot;
                _regions = regions;
            }

            public ManeuverPhase PhaseOf(string childId) => PlayExecutorRuntime.PhaseOf(_side.Alliance, childId);

            public int RegionOf(string childId)
            {
                if (_snapshot == null || _regions == null) return -1;
                int index = _snapshot.IndexOfOwn(DirectChildIds.InstanceId(childId));
                return index >= 0 ? _regions.RegionAt(_snapshot.Own[index].X, _snapshot.Own[index].Z) : -1;
            }

            public bool HeldNow(int link) => _side.Crossings.StateOf(link) == CrossingState.Held;

            public int DrainAbandoned(string[] into, int count) => PlayExecutorRuntime.DrainAbandoned(_side.Alliance, into, count);
        }
    }
}
