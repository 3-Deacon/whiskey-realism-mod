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
    internal static MarchRules ShippedRules(bool orderDelays = true) => new MarchRules
    {
        BasicUnitSpeed = 1000f, InfantryBase = 1f, CavalryMountedBase = 2f, ArtilleryLimberedBase = 1f, HorseArtilleryFactor = 1.2f,
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
                if (Math.Abs(a.x0 - x0) > 60f || Math.Abs(a.z0 - z0) > 60f || Math.Abs(a.x1 - x1) > 60f || Math.Abs(a.z1 - z1) > 60f) continue;
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

        public int Add(int unitTyp, float x, float z, int parent, UnitFlags extra = 0, float strength = 0f, float morale = 0.9f)
        {
            bool group = unitTyp >= TacticalUnitType.BattleGroupBrigade;
            _own.Add(new UnitRecord
            {
                InstanceId = _id++, UnitTyp = unitTyp, X = x, Z = z, Strength = (int)strength, Morale = morale, GroupMorale = morale,
                Flags = UnitFlags.Alive | UnitFlags.Active | (group ? UnitFlags.IsGroup : 0) | extra, UnitWidth = 100f,
            });
            _parents.Add(parent);
            return _own.Count - 1;
        }

        public int InstanceId(int index) => _own[index].InstanceId;

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
}
