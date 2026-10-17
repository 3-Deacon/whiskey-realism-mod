using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static BattleMovementHonestyTests;

// BC14 fixtures shared by the three slices (no tests here): the shipped march rules, graph and picture builders, snapshot trees and a
// scripted NavMesh. The vanilla numbers below are the test project's own copy of the shipped game data (battleprefs, unitprefs); the
// BC14 sources read them from GamePrefs through MarchRules and carry none.
internal static class ManeuverFeasibilityFixtures
{
    // unitprefs.txt: basicunitspeed 1000, unitbasicspeed infantry 1 / cavalry mounted 2 / artillery limbered 1, courier 2.33 x 2.2 x 1000,
    // processing 0.02, route 0.02, bugle 0.02 within 75, fatiguefactorspeed 0.2.
    /// <summary>The monitor's evaluation to completion (a forced run: any part-run is dropped first).</summary>
    internal static void EvaluateSelectedFully(in FeasibilityInput input, FeasibilityLedger into)
    {
        if (into.HasSelection) into.MonitorLedger.Job.Abort();
        ManeuverFeasibility.EvaluateSelectedSlice(in input, into, long.MaxValue, null);
    }

    internal static MarchRules ShippedRules(bool orderDelays = true) => new MarchRules
    {
        BasicUnitSpeed = 1000f, InfantryBase = 1f, CavalryMountedBase = 2f, ArtilleryLimberedBase = 1f, HorseArtilleryFactor = 2.5f,
        FatigueSpeedExponent = 0.2f, ProcessingHours = 0.02f, RouteProcessingHours = 0.02f, BugleHours = 0.02f, BugleRange = 75f,
        CourierSpeedPerHour = 2.33f * 2.2f * 1000f, OrderDelays = orderDelays,
    };

    // A ready picture flag over a grid, for tests that need only 'the picture is ready' (the river regions follow it). Its layers are empty.
    internal static TerrainPicture ReadyFlag(BattleTerrainGrid grid, int size)
    {
        var picture = new TerrainPicture(grid, size, size);
        picture.Ready = true;
        return picture;
    }

    internal static RiverRegions RegionsOf(BattleTerrainGrid grid, int size)
    {
        var regions = new RiverRegions();
        if (!regions.Step(ReadyFlag(grid, size), grid, long.MaxValue, () => 0L)) throw new Exception("the river regions did not build");
        return regions;
    }

    internal static CrossingGraphView GraphOf(BattleTerrainGrid grid, RiverRegions regions, CrossingWatch watch = null, IConfirmedLegs legs = null,
        MarchRules? rules = null)
    {
        var graph = new CrossingGraphView
        {
            Grid = grid, Regions = regions, Crossings = watch, Cache = new LegCostCache(), Legs = legs, Rules = rules ?? ShippedRules(),
        };
        return graph;
    }

    // A river band on x in [from, to) of a square map, with crossings listed at the given (z, id) along its middle. Cells under a listed
    // crossing keep the river id (the footprint rule prices them), and the band is the id of open river.
    internal static BattleTerrainGrid BandMap(int size, int from, int to, params (float z, int id)[] crossings)
    {
        float mid = (from + to) * 0.5f;
        var cx = new float[crossings.Length];
        var cz = new float[crossings.Length];
        var cid = new int[crossings.Length];
        for (int i = 0; i < crossings.Length; i++) { cx[i] = mid; cz[i] = crossings[i].z; cid[i] = crossings[i].id; }
        return GridOf(size, size, (x, zi) => x >= from && x < to ? River : Grass, false, cx, cz, cid);
    }

    // A scripted NavMesh: the answers it holds, keyed by the rounded legs.
    internal sealed class FakeLegs : IConfirmedLegs
    {
        public readonly List<(float x0, float z0, float x1, float z1, int crossing, float cost, bool noPath)> Answers =
            new List<(float, float, float, float, int, float, bool)>();
        public readonly List<(float x0, float z0, float x1, float z1, int crossing)> Requested = new List<(float, float, float, float, int)>();

        public bool TryGet(float x0, float z0, float x1, float z1, int crossing, out float cost, out bool noPath)
        {
            for (int i = 0; i < Answers.Count; i++)
            {
                var a = Answers[i];
                if (a.crossing != crossing) continue;
                bool straight = Math.Abs(a.x0 - x0) <= 60f && Math.Abs(a.z0 - z0) <= 60f && Math.Abs(a.x1 - x1) <= 60f && Math.Abs(a.z1 - z1) <= 60f;
                // A path costs the same walked either way: the route search asks for a link leg from the point side or from the crossing side.
                bool reversed = Math.Abs(a.x0 - x1) <= 60f && Math.Abs(a.z0 - z1) <= 60f && Math.Abs(a.x1 - x0) <= 60f && Math.Abs(a.z1 - z0) <= 60f;
                if (!straight && !reversed) continue;
                cost = a.cost;
                noPath = a.noPath;
                return true;
            }
            cost = 0f;
            noPath = false;
            return false;
        }

        public void Request(float x0, float z0, float x1, float z1, int crossing) => Requested.Add((x0, z0, x1, z1, crossing));
    }

    // A command tree in a snapshot: groups and regiments with positions and parents.
    internal sealed class TreeFixture
    {
        private readonly List<UnitRecord> _own = new List<UnitRecord>();
        private readonly List<int> _parents = new List<int>();
        private int _id = 100;

        public int Add(int unitTyp, float x, float z, int parent, UnitFlags extra = 0, float strength = 0f, float morale = 0.9f,
            float fireRange = 0f, float rotationY = 0f, float fatigue = 0f, bool active = true)
        {
            bool group = unitTyp >= TacticalUnitType.BattleGroupBrigade;
            _own.Add(new UnitRecord
            {
                InstanceId = _id++, UnitTyp = unitTyp, X = x, Z = z, Strength = (int)strength, Morale = morale, GroupMorale = morale,
                Flags = UnitFlags.Alive | (active ? UnitFlags.Active : 0) | (group ? UnitFlags.IsGroup : 0) | extra, UnitWidth = 100f,
                FireRange = fireRange, RotationY = rotationY, Fatigue = fatigue,
            });
            _parents.Add(parent);
            return _own.Count - 1;
        }

        public int InstanceId(int index) => _own[index].InstanceId;

        /// <summary>Moves a unit already added (a regiment standing apart from its corps), optionally giving it a fire range.</summary>
        public void Place(int index, float x, float z, float fireRange = -1f)
        {
            UnitRecord r = _own[index];
            r.X = x;
            r.Z = z;
            if (fireRange >= 0f) r.FireRange = fireRange;
            _own[index] = r;
        }

        public int Count => _own.Count;

        public UnitRecord this[int index] => _own[index];

        public BattleSnapshot Build(double hours = 10.0, params EnemyObservation[] observations)
        {
            var s = new BattleSnapshot();
            s.BeginBuild(7, 0, 0, 1, hours);
            for (int i = 0; i < _own.Count; i++)
            {
                UnitRecord r = _own[i];
                s.AddOwn(in r);
                s.Own[i].ParentIndex = _parents[i];
            }
            s.IndexCommandTree(null);
            for (int i = 0; i < observations.Length; i++)
            {
                EnemyObservation o = observations[i];
                s.AddObservation(in o);
            }
            return s;
        }
    }

    // The shipped line-of-sight reach by unit type: infantry and cavalry 200, artillery 400 (the spec's scenario fixtures).
    internal static readonly float[] ShippedReachByType = ReachTable();

    private static float[] ReachTable()
    {
        var reach = new float[14];
        for (int i = 0; i < reach.Length; i++) reach[i] = 200f;
        reach[TacticalUnitType.Artillery] = 400f;
        return reach;
    }

    /// <summary>
    /// A scenario field in the spec's own frame (spec § 6): lateral is the right of the army's heading and depth is forward toward the
    /// enemy. The map is a 2,400-unit square, so world x = lateral + 1,200 and z = depth - 1,200; forward is +z, which makes +x the
    /// right axis. The own side stands on three corps of four regiments (the army headquarters, which is not an active unit of the
    /// layout's centroid, stands behind them), the enemy as Visible infantry samples.
    /// </summary>
    internal sealed class FeasField
    {
        public const int Size = 2400;
        public static float X(float lateral) => lateral + Size * 0.5f;
        public static float Z(float depth) => depth - Size * 0.5f;

        public BattleTerrainGrid Grid;
        public TerrainPicture Picture;
        public RiverRegions Regions;
        public CrossingWatch Crossings = new CrossingWatch();
        public IConfirmedLegs Legs;
        public IExploitLookup Exploits;
        public MarchRules Rules = ShippedRules();
        public readonly TreeFixture Tree = new TreeFixture();
        public readonly List<string> ChildIds = new List<string>();
        public readonly List<bool> DeployedReserve = new List<bool>();
        public readonly List<EnemyObservation> Enemies = new List<EnemyObservation>();
        public BattleStance Stance = BattleStance.Attacker;
        public OpportunityPersonality Army = Personality(0.5f, 0.5f, 0.5f);
        public double Now = 10.0;
        public float DaylightHours = 10f;
        public float Confidence = 0.8f;
        public float? EnemyEstimate;
        public TacticalIntentModel EnemyIntent;
        /// <summary>Probe answer for BC13's perceived ground: everything perceived means every unheld crossing reads Clear.</summary>
        public Func<float, float, bool> Perceived = (x, z) => true;
        public readonly int ArmyIndex;
        public bool HasAssumedEnemy;
        /// <summary>The enemy line's lateral half extent when it is stated by the scenario rather than read from the samples.</summary>
        public float? EnemyHalfExtent;
        /// <summary>Tree indexes of units with a Visible enemy inside the contact distance (the snapshot's perceived contact).</summary>
        public readonly List<int> Contact = new List<int>();
        /// <summary>Enemy unit records that are not observations: units the side does not perceive.</summary>
        public readonly List<UnitRecord> HiddenEnemies = new List<UnitRecord>();
        public float AssumedEnemyLateral, AssumedEnemyDepth;
        /// <summary>BC14W: the works points the side has perceived (null: none).</summary>
        public IFortificationPoints WorksPoints;
        /// <summary>BC14O: the live objectives in the scenario frame and the victory clock (null: no clock, so no pressure).</summary>
        public readonly List<ObjectiveFact> Objectives = new List<ObjectiveFact>();
        public VictoryClock? Clock;
        /// <summary>BC14O: the children that screen a secondary objective at the previous allocation.</summary>
        public readonly List<string> ScreenChildren = new List<string>();

        private static FeasField _flat;

        /// <summary>A field on the same map as <paramref name="donor"/> (its grid, picture and regions are shared, read-only).</summary>
        public FeasField(FeasField donor)
        {
            Grid = donor.Grid;
            Picture = donor.Picture;
            Regions = donor.Regions;
            ArmyIndex = Tree.Add(TacticalUnitType.BattleGroupArmy, X(0f), Z(-700f), -1, active: false);
        }

        public FeasField(BattleTerrainGrid grid = null)
        {
            if (grid == null)
            {
                _flat = _flat ?? new FeasField(GridOf(Size, Size, (x, zi) => Grass));
                Grid = _flat.Grid;
                Picture = _flat.Picture;
                Regions = _flat.Regions;
            }
            else
            {
                // A river map needs the real picture: the regions are labelled over its cell ids.
                Grid = grid;
                Picture = TerrainPictureTests.Build(grid, Size, TerrainPictureTests.Flat());
                Regions = new RiverRegions();
                if (!Regions.Step(Picture, grid, long.MaxValue, () => 0L)) throw new Exception("the river regions did not build");
            }
            ArmyIndex = Tree.Add(TacticalUnitType.BattleGroupArmy, X(0f), Z(-700f), -1, active: false);
        }

        public static OpportunityPersonality Personality(float agg, float caution, float audacity)
            => new OpportunityPersonality { Agg01 = agg, Caution01 = caution, Audacity01 = audacity, CasualtyTolerance01 = 0.5f, Duty = 1f };

        /// <summary>
        /// A corps (a division group) of <paramref name="regiments"/> equal regiments, its brigade within bugle range of the corps. The
        /// regiments stand at the corps and face the enemy. Returns the corps' index in the tree; its child id is appended.
        /// </summary>
        public int AddCorps(float lateral, float depth, float strength, int regiments = 4, UnitFlags regimentFlags = 0, bool deployedReserve = false,
            float fireRange = 100f, float fatigue = 0f, float cover = 0f, int coverObject = 0)
        {
            float x = X(lateral), z = Z(depth);
            int corps = Tree.Add(TacticalUnitType.BattleGroupDivision, x, z, ArmyIndex);
            int brigade = Tree.Add(TacticalUnitType.BattleGroupBrigade, x, z, corps);
            for (int r = 0; r < regiments; r++)
            {
                int regiment = Tree.Add(TacticalUnitType.Infantry, x, z, brigade, UnitFlags.IsInfantry | regimentFlags,
                    strength / regiments, morale: 1f, fireRange: fireRange, rotationY: 0f, fatigue: fatigue);
                if (cover > 0f) _cover[regiment] = (cover, coverObject);
            }
            ChildIds.Add("child-" + Tree.InstanceId(corps));
            DeployedReserve.Add(deployedReserve);
            return corps;
        }

        /// <summary>A corps of artillery batteries only (<paramref name="horse"/>: horse artillery). Returns the corps' tree index; its child id is appended.</summary>
        public int AddBatteryCorps(float lateral, float depth, float strength, bool horse, int batteries = 2, int cavalry = 0)
        {
            float x = X(lateral), z = Z(depth);
            int corps = Tree.Add(TacticalUnitType.BattleGroupDivision, x, z, ArmyIndex);
            int brigade = Tree.Add(TacticalUnitType.BattleGroupBrigade, x, z, corps);
            for (int r = 0; r < cavalry; r++)
                Tree.Add(TacticalUnitType.Cavalry, x, z, brigade, UnitFlags.IsCavalry | UnitFlags.Mounted, strength / (batteries + cavalry), morale: 1f, fireRange: 100f,
                    rotationY: 0f, fatigue: 0f);
            for (int r = 0; r < batteries; r++)
                Tree.Add(TacticalUnitType.Artillery, x, z, brigade, UnitFlags.IsArtillery | (horse ? UnitFlags.HorseArtillery : 0),
                    strength / batteries, morale: 1f, fireRange: 300f, rotationY: 0f, fatigue: 0f);
            ChildIds.Add("child-" + Tree.InstanceId(corps));
            DeployedReserve.Add(false);
            return corps;
        }

        public EnemyObservation AddEnemy(float lateral, float depth, float strength, float heading = 180f, float fireRange = 100f,
            int unitTyp = TacticalUnitType.Infantry, ObservationState state = ObservationState.Visible, float confidence = 1f,
            float cover = 0f, int coverObject = 0, bool standing = false)
        {
            var o = new EnemyObservation
            {
                InstanceId = 5000 + Enemies.Count, UnitTyp = unitTyp, X = X(lateral), Z = Z(depth), Strength = strength, State = state,
                Confidence01 = confidence, Heading = heading, FireRange = fireRange, Morale = 1f,
                CoverValue = cover, CoverObject = coverObject, Standing = standing,
            };
            Enemies.Add(o);
            return o;
        }

        /// <summary>Three equal Visible clusters at lateral -450, 0 and +450 (the scenarios 1-3 enemy) at the given depth.</summary>
        public void AddEnemyLine(float totalStrength, float depth = 600f)
        {
            AddEnemy(-450f, depth, totalStrength / 3f);
            AddEnemy(0f, depth, totalStrength / 3f);
            AddEnemy(450f, depth, totalStrength / 3f);
        }

        // BC14W: own regiments standing in works cover (tree index to the cover value and object), applied to the snapshot's records.
        private readonly Dictionary<int, (float value, int obj)> _cover = new Dictionary<int, (float, int)>();

        public BattleSnapshot Snapshot()
        {
            BattleSnapshot s = Tree.Build(Now, Enemies.ToArray());
            foreach (KeyValuePair<int, (float value, int obj)> c in _cover)
            {
                s.Own[c.Key].CoverValue = c.Value.value;
                s.Own[c.Key].CoverObject = c.Value.obj;
            }
            for (int i = 0; i < Contact.Count; i++) s.SetClosestVisibleEnemy(Contact[i], 0, 100f);
            for (int i = 0; i < HiddenEnemies.Count; i++)
            {
                UnitRecord r = HiddenEnemies[i];
                s.AddEnemy(in r);
            }
            return s;
        }

        /// <summary>BC14O: a live objective at (lateral, depth); enemy-held unless <paramref name="own"/> or <paramref name="neutral"/>.</summary>
        public void AddObjective(string id, float lateral, float depth, float points, bool own = false, bool neutral = false, bool threatened = false)
            => Objectives.Add(new ObjectiveFact { Id = id, X = X(lateral), Z = Z(depth), Points = points, OwnHeld = own, EnemyHeld = !own && !neutral, Threatened = threatened });

        /// <summary>An own regiment under the army that is no direct child's (a picket): it counts in the centroid and covers ground.</summary>
        public int AddPicket(float lateral, float depth, float rotationY, float fireRange = 100f, float strength = 400f)
            => Tree.Add(TacticalUnitType.Infantry, X(lateral), Z(depth), ArmyIndex, UnitFlags.IsInfantry, strength, morale: 1f, fireRange: fireRange, rotationY: rotationY);

        public FeasibilityInput Build(params BattlePlanId[] plans)
        {
            BattleSnapshot s = Snapshot();
            // The own centroid and lateral extent of every active own unit; the enemy anchor of the Visible samples.
            float sx = 0f, sz = 0f;
            int n = 0;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if ((s.Own[i].Flags & UnitFlags.Active) == 0) continue;
                sx += s.Own[i].X;
                sz += s.Own[i].Z;
                n++;
            }
            float cx = n > 0 ? sx / n : X(0f), cz = n > 0 ? sz / n : Z(0f);
            float wx = 0f, wz = 0f, w = 0f, total = 0f;
            for (int i = 0; i < s.ObservationCount; i++)
            {
                total += s.Observations[i].Strength;
                if (s.Observations[i].State != ObservationState.Visible || s.Observations[i].IsReinforcement) continue;
                wx += s.Observations[i].X * s.Observations[i].Strength;
                wz += s.Observations[i].Z * s.Observations[i].Strength;
                w += s.Observations[i].Strength;
            }
            float ex = w > 0f ? wx / w : cx, ez = w > 0f ? wz / w : cz + 600f;
            TacticalFrontAxis axis = TacticalFrontAxis.FromCentroids(cx, cz, ex, ez);
            float lmin = float.MaxValue, lmax = float.MinValue, emin = float.MaxValue, emax = float.MinValue;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if ((s.Own[i].Flags & UnitFlags.Active) == 0) continue;
                float lat = axis.LateralOf(s.Own[i].X, s.Own[i].Z);
                if (lat < lmin) lmin = lat;
                if (lat > lmax) lmax = lat;
            }
            for (int i = 0; i < s.ObservationCount; i++)
            {
                float lat = axis.LateralOf(s.Observations[i].X, s.Observations[i].Z);
                if (lat < emin) emin = lat;
                if (lat > emax) emax = lat;
            }
            if (lmin <= lmax) axis = axis.WithOwnExtent(lmin, lmax);
            if (EnemyHalfExtent.HasValue) axis = axis.WithEnemyExtent(-EnemyHalfExtent.Value, EnemyHalfExtent.Value);
            else if (emin <= emax) axis = axis.WithEnemyExtent(emin, emax);

            Crossings.Classify(s, Regions, Grid, new FnProbe(Perceived), ShippedReachByType, Now);
            return new FeasibilityInput
            {
                Snapshot = s, Axis = axis, Stance = Stance, Grid = Grid, Picture = Picture, Regions = Regions, Crossings = Crossings,
                Rules = Rules, Army = Army, EnemyEstimate = EnemyEstimate ?? total, Confidence = Confidence, EnemyIntent = EnemyIntent,
                NowHours = Now, DaylightHours = DaylightHours, Exploits = Exploits, Legs = Legs,
                ChildIds = ChildIds.ToArray(), ChildCount = ChildIds.Count, DeployedReserve = DeployedReserve.ToArray(),
                Candidates = plans, CandidateCount = plans.Length, ReachByUnitType = ShippedReachByType, HasAssumedEnemy = HasAssumedEnemy,
                AssumedEnemyX = X(AssumedEnemyLateral), AssumedEnemyZ = Z(AssumedEnemyDepth),
                Cache = new LegCostCache(), Works = WorksPoints,
                Objectives = Objectives.ToArray(), ObjectiveCount = Objectives.Count, Clock = Clock ?? default(VictoryClock), HasClock = Clock.HasValue,
                ScreenChildIds = ScreenChildren.ToArray(), ScreenChildCount = ScreenChildren.Count,
            };
        }

        public FeasibilityLedger Evaluate(params BattlePlanId[] plans)
        {
            var ledger = new FeasibilityLedger();
            ManeuverFeasibility.Evaluate(Build(plans), ledger);
            return ledger;
        }
    }

    internal sealed class FnProbe : IPerceivedGround
    {
        private readonly Func<float, float, bool> _fn;
        public FnProbe(Func<float, float, bool> fn) { _fn = fn; }
        public bool IsPerceived(float x, float z) => _fn(x, z);
    }
}
