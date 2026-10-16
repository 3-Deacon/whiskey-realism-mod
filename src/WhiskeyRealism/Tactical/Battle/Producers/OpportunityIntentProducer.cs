using System;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle.Producers
{
    // BC13 (spec § 6, requirement-opportunity-producer): the commander opportunity producer. Per AI side and arbiter tick it runs
    // the pure OpportunityTick (detect the side's openings, step the live exploits, let each due corps and division commander
    // decide, publish the ExploitTable) and submits no intents: the posture producer, next in the tick, executes the table as an
    // override of the asset's doctrine order, so every vanilla write still goes through the order executor.
    //
    // This class supplies what the pure tick cannot know: the army plan and roles, the arbiter's published claims, the executor's
    // outcomes, the recon, probe, maneuver and arrival-hold registries, the commander personality table, the perceived-ground
    // oracle and the telemetry rows. It stands down exactly when the arbiter tick does (BattleCoreTick.StandDown ends the table
    // with stand-down), and when Enable Commander Opportunity is off or the side has no army plan.
    internal sealed class OpportunityIntentProducer : IBattleIntentProducer
    {
        private static readonly OpportunityTick[] Ticks = { new OpportunityTick(), new OpportunityTick() };
        private static readonly World[] Worlds = { new World(0), new World(1) };
        private static readonly Sink[] Sinks = { new Sink(0), new Sink(1) };
        private static IOpeningTerrain _terrain;
        private static BattleTerrainGrid _terrainGrid;
        private static TerrainPicture _terrainPicture;

        static OpportunityIntentProducer()
        {
            BattleScope.Register("opportunity-producer", Reset);
        }

        public string Name => "Opportunity";

        public void Produce(BattleSnapshot s, IIntentSink sink)
        {
            using (TelemetryPerf.Scope("tactical.producer.opportunity", TelemetryLayer.Tactical, TelemetryCategory.Performance, 0.5))
            {
                if (s == null || s.Alliance < 0 || s.Alliance >= Ticks.Length) return;
                int alliance = s.Alliance;
                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(alliance);
                ArmyOrchestrator army = side?.Army;
                if (!Plugin.CommanderOpportunityEnabled())
                {
                    StandDown(alliance);
                    return;
                }
                // Decisions need the army plan (its phase and main-effort sector); live exploits keep their last order meanwhile.
                if (army == null || !army.HasPlan) return;

                World world = Worlds[alliance];
                world.Bind(s, side, army);
                Sinks[alliance].Bind(s, world);
                OpeningRules rules = Rules();
                using (PerceivedGroundRuntime.Handle ground = PerceivedGroundRuntime.Open(s))
                {
                    Ticks[alliance].Run(s, world, ground.Oracle, Terrain(), in rules, ExploitTable.ForAlliance(alliance), Sinks[alliance]);
                }
            }
        }

        /// <summary>The arbiter tick stood down (or the flag is off): every live exploit ends as stand-down and the table is empty.</summary>
        internal static void StandDown(int alliance)
        {
            if (alliance < 0 || alliance >= Ticks.Length) return;
            ExploitTable table = ExploitTable.ForAlliance(alliance);
            if (table.Count == 0) return;
            OpportunityTick.EndAll(table, ExploitEnd.StandDown, TacticalDoctrineTime.NowBattleHours, Sinks[alliance]);
        }

        private static OpeningRules Rules()
        {
            return new OpeningRules
            {
                ShakenMorale = GamePrefs.maxenemymoraleforcavalrychargenonarty,
                AnchorRiverMeters = GamePrefs.distancetocheckforanchoredflanksriver,
                AnchorHillMeters = GamePrefs.distancetocheckforanchoredflankhills,
                PictureReady = TerrainPictureRuntime.Get() != null && TerrainPictureRuntime.Get().Ready,
                CounterstrokeOn = Plugin.DefenderCounterstrokeEnabled(),
            };
        }

        // The BC10 grid and the BC12 picture are battle singletons; the wrapper is rebuilt only when either reference changes.
        private static IOpeningTerrain Terrain()
        {
            BattleTerrainGrid grid = BattleTerrainGridRuntime.Get();
            TerrainPicture picture = TerrainPictureRuntime.Get();
            if (_terrain == null || !ReferenceEquals(grid, _terrainGrid) || !ReferenceEquals(picture, _terrainPicture))
            {
                _terrainGrid = grid;
                _terrainPicture = picture;
                _terrain = new OpeningTerrain(grid, picture);
            }
            return _terrain;
        }

        private static void Reset()
        {
            for (int a = 0; a < Ticks.Length; a++)
            {
                Ticks[a].Reset();
                Sinks[a].Reset();
                Worlds[a].Reset();
            }
            _terrain = null;
            _terrainGrid = null;
            _terrainPicture = null;
        }

        // ---------------------------------------------------------------------------------------------------- the world

        private sealed class World : IOpportunityWorld
        {
            private readonly int _alliance;
            private BattleSnapshot _s;
            private TacticalBattleOrchestrator _side;
            private ArmyOrchestrator _army;
            private OrderExecutor _executor;
            private OrderArbiter _arbiter;
            private readonly int[] _maneuvering = new int[8];
            private int _maneuveringCount;
            private int _scoutIndex = -1;

            public World(int alliance) { _alliance = alliance; }

            public void Bind(BattleSnapshot s, TacticalBattleOrchestrator side, ArmyOrchestrator army)
            {
                _s = s;
                _side = side;
                _army = army;
                _executor = OrderExecutor.ForAlliance(_alliance);
                _arbiter = OrderArbiter.ForAlliance(_alliance);
                int maneuvering = PlayExecutorRuntime.ManeuveringChildren(_alliance, _maneuvering);
                _maneuveringCount = 0;
                for (int i = 0; i < maneuvering; i++)
                {
                    int index = s.IndexOfOwn(_maneuvering[i]);
                    if (index >= 0) _maneuvering[_maneuveringCount++] = index;
                }
                _scoutIndex = s.IndexOfOwn(ReconIntentProducer.MissionScoutId(_alliance));
            }

            public void Reset()
            {
                _s = null;
                _side = null;
                _army = null;
                _executor = null;
                _arbiter = null;
                _maneuveringCount = 0;
                _scoutIndex = -1;
            }

            public BattleStance Stance => _army.CurrentBattleStance;
            public BattlePhase PlanPhase => _army.CurrentPlan.Phase;
            public int MacroAi => BattleCoreTick.MacroAiOf(_alliance);
            public int MainEffortSector => _army.CurrentPlan.MainEffortSector;
            public int BattleDay => TacticalBattleCoordinator.BattleDayEpoch;
            public double SideStatCycleHours => SideStatCycle;
            public float CavalryMoraleBar => OpportunityDoctrine.CavalryMoraleBar(GamePrefs.moraletriggerforfallbackifenemyclose[3]);

            // Vanilla's sidestatupdatecycle (84572), 0.015 game-hours.
            private const double SideStatCycle = 0.015;

            public bool TryPersonality(int commanderIndex, out OpportunityPersonality personality)
            {
                PersonalityVector vector;
                PersonalitySource source;
                CommanderPersonalityTable table = CommanderPersonalityRuntime.TableFor(_alliance);
                if (Plugin.PerCommanderPersonalityEnabled() && table != null && table.TryGet(commanderIndex, out vector, out source))
                {
                    // the table's own vector
                }
                else
                {
                    vector = _army.CommanderPersonality;
                    source = PersonalitySource.Default;
                }
                personality = new OpportunityPersonality
                {
                    Agg01 = CommanderPersonalityDerivation.To01(vector.Aggression),
                    Caution01 = CommanderPersonalityDerivation.To01(vector.Caution),
                    Audacity01 = CommanderPersonalityDerivation.To01(vector.Audacity),
                    CasualtyTolerance01 = CommanderPersonalityDerivation.To01(vector.CasualtyTolerance),
                    Duty = CommanderPersonalityDerivation.Duty(CommanderPersonalityRuntime.Status(commanderIndex), GamePrefs.reductionfactorcommanderoutofduty),
                    Source = source,
                };
                return true;
            }

            public bool DrivenByPosture(int groupId)
            {
                if (ArmyPostureInputsRuntime.InAiPool(_alliance, groupId)) return true;
                var leafMap = _army.CurrentLeafBrigadeMap;
                return leafMap != null && leafMap.TryGetValue(groupId, out TacticalLeafBrigadeMap.LeafAssignment leaf) && leaf.LeafTask != CommandTaskType.None;
            }

            public bool TryRoleTask(int groupId, out CommandNodeRole role, out CommandTaskType task)
            {
                if (PostureLedger.TryResolveState(groupId, _side, out CommandNodeOperationalState state))
                {
                    role = state.Role;
                    task = state.Task;
                    return true;
                }
                var leafMap = _army.CurrentLeafBrigadeMap;
                if (leafMap != null && leafMap.TryGetValue(groupId, out TacticalLeafBrigadeMap.LeafAssignment leaf))
                {
                    role = RoleOf(leaf.LeafRole);
                    task = leaf.LeafTask;
                    return true;
                }
                role = CommandNodeRole.Unknown;
                task = CommandTaskType.None;
                return false;
            }

            private static CommandNodeRole RoleOf(DirectChildRole role)
            {
                switch (role)
                {
                    case DirectChildRole.Main: return CommandNodeRole.MainEffort;
                    case DirectChildRole.SupportMain: return CommandNodeRole.SupportingAttack;
                    case DirectChildRole.Fix: return CommandNodeRole.FixingForce;
                    case DirectChildRole.Screen: return CommandNodeRole.ScreeningForce;
                    case DirectChildRole.Reserve: return CommandNodeRole.Reserve;
                    case DirectChildRole.Fallback: return CommandNodeRole.FallbackGuard;
                    default: return CommandNodeRole.Unknown;
                }
            }

            public bool IsMainEffortChild(int armyChildGroupId)
            {
                var intents = _army.CurrentDirectChildIntents;
                for (int i = 0; i < intents.Count; i++)
                {
                    if (!ChildIdIs(intents[i].ChildId, armyChildGroupId)) continue;
                    return intents[i].Role == DirectChildRole.Main || intents[i].Role == DirectChildRole.SupportMain;
                }
                return false;
            }

            // Child ids read "child-" + the instance id.
            private static bool ChildIdIs(string childId, int instanceId)
            {
                const string prefix = "child-";
                return childId != null && childId.Length > prefix.Length && childId.StartsWith(prefix, StringComparison.Ordinal) &&
                    int.TryParse(childId.AsSpan(prefix.Length), out int parsed) && parsed == instanceId;
            }

            public bool SafetyClaim(int groupId)
            {
                if (_arbiter == null) return false;
                return (_arbiter.TryGetClaim(groupId, OrderChannel.Move, out BattleOrderIntent move) && move.Priority == IntentPriority.Safety) ||
                    (_arbiter.TryGetClaim(groupId, OrderChannel.Stance, out BattleOrderIntent stance) && stance.Priority == IntentPriority.Safety);
            }

            public bool RecentExecutorMove(int groupId, double nowHours)
            {
                if (_executor == null || !_executor.TryGetLastOutcome(groupId, OrderChannel.Move, out OrderOutcome outcome)) return false;
                if (outcome.Kind != OrderOutcomeKind.Issued && outcome.Kind != OrderOutcomeKind.Moved) return false;
                double age = nowHours - outcome.AtHours;
                return age >= 0.0 && age < OrderExecutor.RecentOrderBattleSeconds / (double)TacticalBattleClock.BattleSecondsPerHour;
            }

            public bool UnderManeuver(int groupId)
            {
                int index = _s.IndexOfOwn(groupId);
                if (index < 0) return false;
                for (int i = 0; i < _maneuveringCount; i++)
                    if (Related(index, _maneuvering[i])) return true;
                return false;
            }

            public bool UnderRecon(int groupId)
            {
                int index = _s.IndexOfOwn(groupId);
                return index >= 0 && _scoutIndex >= 0 && Related(index, _scoutIndex);
            }

            public bool UnderProbeHold(int groupId) => AnyRelated(groupId, probe: true);

            public bool UnderArrivalHold(int groupId) => AnyRelated(groupId, probe: false);

            public bool AdvanceGaveUp(int groupId)
            {
                if (_executor == null || !_executor.TryGetLastOutcome(groupId, OrderChannel.Move, out OrderOutcome outcome)) return false;
                if (outcome.Kind != OrderOutcomeKind.GaveUp) return false;
                ExploitTable table = ExploitTable.ForAlliance(_alliance);
                for (int i = 0; i < table.Count; i++)
                    if (table.At(i).AssetId == groupId) return outcome.AtHours >= table.At(i).LaunchHours;
                return false;
            }

            // The allocator's crisis release (a Reserve army child sent to a crisis band) of the army-level ancestor of the asset.
            public bool ParentCrisisRelease(int groupId)
            {
                int index = _s.IndexOfOwn(groupId);
                if (index < 0) return false;
                int a = index;
                for (int steps = 0; steps < _s.OwnCount; steps++)
                {
                    int parent = _s.Own[a].ParentIndex;
                    if (parent < 0 || _s.Own[parent].ParentIndex < 0) break;
                    a = parent;
                }
                int armyChild = _s.Own[a].InstanceId;
                var crisis = _army.ReserveRelease.CrisisReleased;
                for (int i = 0; i < crisis.Count; i++)
                    if (ArmyOrchestrator.ChildInstanceId(crisis[i]) == armyChild) return true;
                return false;
            }

            // The group, one of its ancestors or one of its descendants (own indices).
            private bool Related(int a, int b) => a == b || _s.IsAncestorOf(a, b) || _s.IsAncestorOf(b, a);

            private bool AnyRelated(int groupId, bool probe)
            {
                int index = _s.IndexOfOwn(groupId);
                if (index < 0) return false;
                if (Flagged(groupId, probe)) return true;
                for (int p = _s.Own[index].ParentIndex, steps = 0; p >= 0 && steps < _s.OwnCount; p = _s.Own[p].ParentIndex, steps++)
                    if (Flagged(_s.Own[p].InstanceId, probe)) return true;
                for (int j = 0; j < _s.OwnCount; j++)
                    if ((_s.Own[j].Flags & UnitFlags.IsGroup) != 0 && _s.IsAncestorOf(index, j) && Flagged(_s.Own[j].InstanceId, probe)) return true;
                return false;
            }

            private bool Flagged(int groupId, bool probe)
            {
                return probe ? ReconIntentProducer.HasProbeOrHold(_alliance, groupId) : PostureIntentProducer.UnderArrivalHold(groupId);
            }
        }

        // ---------------------------------------------------------------------------------------------------- telemetry

        private sealed class Sink : IOpportunitySink
        {
            private readonly int _alliance;
            private BattleSnapshot _s;
            private IOpportunityWorld _world;

            public Sink(int alliance) { _alliance = alliance; }

            public void Bind(BattleSnapshot s, IOpportunityWorld world)
            {
                _s = s;
                _world = world;
            }

            public void Reset()
            {
                _s = null;
                _world = null;
            }

            public void OpeningSummary(OpportunityTick tick, ExploitTable table, double nowHours)
            {
                OpeningDetector d = tick.Detector;
                if (d.Count > 0)
                    OnceLog.Info("tactical-battle-core:opportunity-first-opening", "[Opportunity] first opening: kind=" + d.Openings[0].Kind + " side=" + _alliance);
                if (!TelemetryRouter.ShouldEmit(TelemetryRouter.CurrentProfile, TelemetryLayer.Tactical, TelemetryCategory.State)) return;
                int gap = 0, shaken = 0, flank = 0, battery = 0, cluster = 0, penetration = 0, confirmed = 0;
                float best = 0f;
                var open = d.Openings;
                for (int i = 0; i < open.Length; i++)
                {
                    switch (open[i].Kind)
                    {
                        case OpeningKind.Gap: gap++; break;
                        case OpeningKind.ShakenSector: shaken++; break;
                        case OpeningKind.FlankInAir: flank++; break;
                        case OpeningKind.UnsupportedBattery: battery++; break;
                        case OpeningKind.IsolatedCluster: cluster++; break;
                        case OpeningKind.Penetration: penetration++; break;
                    }
                    if (open[i].Confirmed)
                    {
                        confirmed++;
                        if (open[i].Confidence01 > best) best = open[i].Confidence01;
                    }
                }
                int deferred = d.Deferred;
                int suppressed = d.SuppressedUnperceived;
                int live = table.Count;
                int truncated = table.SubtreeTruncated, clears = table.ClearCount, dropped = ExploitFeedback.Dropped, refusals = tick.TableRefusals;
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.State, "TacticalOpening", TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(_alliance)
                        .WithField("gap", gap).WithField("shaken", shaken).WithField("flank", flank).WithField("battery", battery)
                        .WithField("cluster", cluster).WithField("penetration", penetration)
                        .WithField("confirmed", confirmed).WithField("bestConfidence", best)
                        .WithField("suppressedUnperceived", suppressed).WithField("probePoints", tick.WindowProbePoints)
                        .WithField("probeCached", tick.WindowProbeCached).WithField("deferred", deferred)
                        .WithField("latticesStarted", tick.WindowLatticesStarted).WithField("latticesEnded", tick.WindowLatticesEnded)
                        .WithField("deferOverBound", tick.WindowDeferOverBound)
                        .WithField("live", live).WithField("subtreeTruncated", truncated).WithField("tableClears", clears)
                        .WithField("feedbackDropped", dropped).WithField("tableRefusals", refusals)
                        .WithField("doctrineHours", nowHours));
            }

            public void Launched(in ExploitEntry entry, in DecisionResult result, in OpportunityPersonality personality, int live)
            {
                OnceLog.Info("tactical-battle-core:opportunity-first-exploit",
                    "[Opportunity] first exploit launched: kind=" + entry.Kind + " asset=" + entry.AssetId + " commander=" + entry.CommanderIndex +
                    " source=" + personality.Source);
                ExploitEntry e = entry;
                DecisionResult r = result;
                OpportunityPersonality p = personality;
                Row("launch", in e, p.Source, p.CasualtyTolerance01, live, r.Reason, r.Chance, r.Roll, null);
            }

            public void Declined(int nodeId, int commanderIndex, in DecisionResult result, in OpportunityPersonality personality, int live)
            {
                if (!TelemetryRouter.ShouldEmit(TelemetryRouter.CurrentProfile, TelemetryLayer.Tactical, TelemetryCategory.Decision)) return;
                var shell = new ExploitEntry { Id = 0, AssetId = result.AssetId, CommanderIndex = commanderIndex, OpeningId = result.OpeningId, Phase = ExploitPhase.None };
                OpportunityPersonality p = personality;
                DecisionResult r = result;
                Row("decline", in shell, p.Source, p.CasualtyTolerance01, live, r.Reason, r.Chance, r.Roll, null);
            }

            public void PhaseChanged(in ExploitEntry entry, ExploitPhase from, int live)
            {
                ExploitEntry e = entry;
                Row("phase", in e, PersonalitySource.Default, 0f, live, null, 0f, 0f, null);
            }

            public void Ended(in ExploitEntry entry, double nowHours, bool leftSector, int live)
            {
                ExploitEntry e = entry;
                Row("end", in e, PersonalitySource.Default, 0f, live, null, 0f, 0f, new EndDetail { Hours = nowHours - e.LaunchHours, LeftSector = leftSector });
            }

            private struct EndDetail
            {
                public double Hours;
                public bool LeftSector;
            }

            private void Row(string row, in ExploitEntry entry, PersonalitySource source, float tolerance01, int live, string reason, float chance, float roll, EndDetail? end)
            {
                if (!TelemetryRouter.ShouldEmit(TelemetryRouter.CurrentProfile, TelemetryLayer.Tactical, TelemetryCategory.Decision)) return;
                ExploitEntry e = entry;
                string unit = null;
                int index = _s != null ? _s.IndexOfOwn(e.AssetId) : -1;
                if (index >= 0) unit = PostureTelemetry.NameOf(_s, index);
                string stance = _world != null ? _world.Stance.ToString() : "Unknown";
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalExploit", TelemetrySeverity.Info,
                    ev =>
                    {
                        ev.WithSide(_alliance)
                            .WithDecision("TacticalExploit", row, "exploit=" + e.Id + "|asset=" + e.AssetId)
                            .WithField("row", row).WithField("exploit", e.Id).WithField("kind", e.Kind.ToString()).WithField("asset", e.AssetId)
                            .WithField("commander", e.CommanderIndex).WithField("source", source.ToString()).WithField("stance", stance)
                            .WithField("cavalry", e.Cavalry).WithField("phase", e.Phase.ToString()).WithField("live", live)
                            .WithField("casualtyTolerance01", tolerance01);
                        if (unit != null) ev.WithUnit(unit);
                        if (reason != null) ev.WithField("reason", reason).WithField("chance", chance).WithField("roll", roll);
                        if (end.HasValue)
                            ev.WithField("end", e.End == ExploitEnd.Success ? "success" : e.End.ToString()).WithField("durationHours", end.Value.Hours)
                                .WithField("lossDelta", e.LossDelta).WithField("leftSector", end.Value.LeftSector);
                    });
            }
        }
    }
}
