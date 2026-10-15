using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Battle.Producers;
using static TestHarness;

// BC10 decision 3: Regiment.outflanked from perceived units (vanilla UpdateFlanking
// 122959-123175 reads the unfogged enemyinrangereg).
internal static partial class BattleMovementHonestyTests
{
    // GamePrefs.fireangles {300, 60, 225, 135} and distancetoenemyforoutflank 60 (unitprefs.txt).
    private static FlankGeometry ShippedFlank => new FlankGeometry
    {
        FrontFrom = 300f, FrontTo = 60f, LeftFrom = 225f, RightTo = 135f, RangeMeters = 60f,
    };

    // A point at an absolute bearing (0 = +z, 90 = +x) and distance from (cx, cz).
    private static void Polar(float cx, float cz, float bearingDegrees, float distance, out float x, out float z)
    {
        double r = bearingDegrees * Math.PI / 180.0;
        x = cx + (float)Math.Sin(r) * distance;
        z = cz + (float)Math.Cos(r) * distance;
    }

    private static int CodeWith(float facing, bool fallback, float[] threatBearings, float[] threatDistances, float[] screenBearings = null, float[] screenDistances = null)
    {
        var tx = new float[threatBearings.Length];
        var tz = new float[threatBearings.Length];
        for (int i = 0; i < tx.Length; i++) Polar(0f, 0f, threatBearings[i], threatDistances[i], out tx[i], out tz[i]);
        int sn = screenBearings != null ? screenBearings.Length : 0;
        var sx = new float[sn];
        var sz = new float[sn];
        for (int i = 0; i < sn; i++) Polar(0f, 0f, screenBearings[i], screenDistances[i], out sx[i], out sz[i]);
        FlankGeometry g = ShippedFlank;
        return PerceivedFlanking.Code(0f, 0f, facing, fallback, tx, tz, tx.Length, sx, sz, sn, in g);
    }

    private static int CodeWith(float facing, float bearing, float distance = 40f)
    {
        return CodeWith(facing, false, new[] { bearing }, new[] { distance });
    }

    // Facing 0 (+z): front = relative 300..60, left 225..300, right 60..135, rear 135..225
    // (UpdateFlanking 122995-123010, IsWithinAngle on bearing - facing). The code is
    // left 1, right 2, rear 4 or-ed: 1 left, 2 right, 3 both, 4 rear, 5 left+rear,
    // 6 right+rear, 7 all (123140-123170).
    private static void FlankCodesMatchVanillaSectors()
    {
        AssertEqual(0, CodeWith(0f, 0f), "ahead is not a flank");
        AssertEqual(1, CodeWith(0f, 262f), "left");
        AssertEqual(2, CodeWith(0f, 100f), "right");
        AssertEqual(4, CodeWith(0f, 180f), "rear");
        AssertEqual(3, CodeWith(0f, false, new[] { 262f, 100f }, new[] { 40f, 40f }), "left and right");
        AssertEqual(5, CodeWith(0f, false, new[] { 262f, 180f }, new[] { 40f, 40f }), "left and rear");
        AssertEqual(6, CodeWith(0f, false, new[] { 100f, 180f }, new[] { 40f, 40f }), "right and rear");
        AssertEqual(7, CodeWith(0f, false, new[] { 262f, 100f, 180f }, new[] { 40f, 40f, 40f }), "all three");
        // The facing rotates the sectors: a unit facing east (90) has an enemy at bearing 352 (+z-ish) on its left.
        AssertEqual(1, CodeWith(90f, 352f), "the same enemy is on the left of a unit facing east");
        // Distance: strictly closer than distancetoenemyforoutflank (xZDistance < 60).
        AssertEqual(1, CodeWith(0f, 262f, 59.9f), "59.9 m");
        AssertEqual(0, CodeWith(0f, 262f, 60f), "60 m is not close enough");
        // Sector edges: IsWithinAngle is inclusive of both ends.
        AssertTrue((CodeWith(0f, 300.5f) & 1) == 0, "300.5 is front, not left");
        AssertTrue((CodeWith(0f, 299.5f) & 1) != 0, "299.5 is left");
        AssertTrue((CodeWith(0f, 134f) & 2) != 0, "134 is right");
        AssertTrue((CodeWith(0f, 134f) & 4) == 0, "134 is not rear");
        AssertTrue((CodeWith(0f, 136f) & 4) != 0 && (CodeWith(0f, 136f) & 2) == 0, "136 is rear only");
    }

    // 123094-123111: an own regiment nearer than the nearest enemy in a sector clears it.
    private static void FlankFriendlyCloserClearsSector()
    {
        AssertEqual(0, CodeWith(0f, false, new[] { 262f }, new[] { 40f }, new[] { 262f }, new[] { 30f }), "a friend 30 m on the left shields the 40 m enemy");
        AssertEqual(1, CodeWith(0f, false, new[] { 262f }, new[] { 40f }, new[] { 262f }, new[] { 50f }), "a friend farther than the enemy shields nothing");
        AssertEqual(1, CodeWith(0f, false, new[] { 262f }, new[] { 40f }, new[] { 262f }, new[] { 40f }), "equal distance is not strictly closer");
        AssertEqual(1, CodeWith(0f, false, new[] { 262f }, new[] { 40f }, new[] { 100f }, new[] { 10f }), "a friend in another sector does not shield");
        AssertEqual(4, CodeWith(0f, false, new[] { 262f, 180f }, new[] { 40f, 40f }, new[] { 262f }, new[] { 30f }), "the left is shielded, the rear is not");
        // The nearest enemy decides: a nearer enemy than the friend keeps the sector flagged.
        AssertEqual(1, CodeWith(0f, false, new[] { 262f, 262f }, new[] { 20f, 50f }, new[] { 262f }, new[] { 30f }), "a 20 m enemy is nearer than the 30 m friend");
    }

    // movementmode 2 (fallback) adds 180 to the facing (122969-122971).
    private static void FlankFallbackModeReversesFacing()
    {
        AssertEqual(2, CodeWith(0f, false, new[] { 100f }, new[] { 40f }), "right while advancing");
        AssertEqual(1, CodeWith(0f, true, new[] { 100f }, new[] { 40f }), "left while falling back");
        AssertEqual(4, CodeWith(0f, true, new[] { 0f }, new[] { 40f }), "an enemy ahead is behind a retreating unit");
    }

    private static BattleSnapshot FlankFixture()
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.IndexCommandTree(null);
        return s;
    }

    private static int AddOwnRegiment(BattleSnapshot s, int id, float x, float z, float facing = 0f, int unitTyp = TacticalUnitType.Infantry,
        UnitFlags extra = UnitFlags.None, int rawOutflanked = 0, int movementMode = 0)
    {
        s.AddOwn(new UnitRecord
        {
            InstanceId = id, UnitTyp = unitTyp, X = x, Z = z, RotationY = facing, Outflanked = rawOutflanked, MovementMode = movementMode,
            Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry | extra,
        });
        return s.OwnCount - 1;
    }

    private static int AddEnemyRegiment(BattleSnapshot s, int id, float x, float z, bool visible, float facing = 0f, int unitTyp = TacticalUnitType.Infantry,
        UnitFlags extra = UnitFlags.None, int rawOutflanked = 0)
    {
        s.AddEnemy(new UnitRecord
        {
            InstanceId = id, UnitTyp = unitTyp, X = x, Z = z, RotationY = facing, Outflanked = rawOutflanked,
            Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry | extra,
        });
        if (visible)
            s.AddObservation(new EnemyObservation
            {
                InstanceId = id, UnitTyp = unitTyp, X = x, Z = z, Strength = 500f, State = ObservationState.Visible, Confidence01 = 1f,
            });
        return s.EnemyRecordCount - 1;
    }

    // 122993: skirmishers (3), scouts (4) and enemies in melee are not threats.
    private static void FlankIgnoresSkirmisherScoutAndMelee()
    {
        FlankGeometry g = ShippedFlank;
        // The own regiment at the origin faces +z; the candidate stands 40 m on its left (x = -40).
        foreach (var (unitTyp, flags, expected, label) in new[]
        {
            (TacticalUnitType.Infantry, UnitFlags.None, 1, "an infantry regiment flanks"),
            (TacticalUnitType.Skirmisher, UnitFlags.None, 0, "a skirmisher company is skipped"),
            (TacticalUnitType.Scout, UnitFlags.None, 0, "a scout company is skipped"),
            (TacticalUnitType.Infantry, UnitFlags.InMelee, 0, "an enemy in melee is skipped"),
            (TacticalUnitType.Cavalry, UnitFlags.None, 1, "cavalry flanks"),
            (TacticalUnitType.Artillery, UnitFlags.None, 1, "artillery stands in the list too"),
            (14, UnitFlags.IsGroup, 0, "a group headquarters is not a regiment"),
        })
        {
            BattleSnapshot s = FlankFixture();
            int self = AddOwnRegiment(s, 1, 0f, 0f);
            AddEnemyRegiment(s, 2, -40f, 0f, visible: true, unitTyp: unitTyp, extra: flags);
            PerceivedFlanking.Apply(s, in g);
            AssertEqual(expected, s.Own[self].Outflanked, label);
        }
    }

    // The 3 x 3 bucket query returns what a full scan would.
    private static void PerceivedFlankingReadsOnlyNeighbourCells()
    {
        FlankGeometry g = ShippedFlank;
        var rng = new Random(12345);
        BattleSnapshot s = FlankFixture();
        for (int i = 0; i < 120; i++) AddOwnRegiment(s, 100 + i, rng.Next(0, 400), -rng.Next(0, 400), rng.Next(0, 360));
        for (int i = 0; i < 120; i++) AddEnemyRegiment(s, 500 + i, rng.Next(0, 400), -rng.Next(0, 400), visible: rng.Next(4) != 0, facing: rng.Next(0, 360));
        PerceivedFlanking.Apply(s, in g);

        int flagged = 0;
        for (int u = 0; u < s.OwnCount; u++)
        {
            int expected = BruteForceCode(s, u, ownSide: true, in g);
            AssertEqual(expected, s.Own[u].Outflanked, "own unit " + u);
            if (expected != 0) flagged++;
        }
        for (int e = 0; e < s.EnemyRecordCount; e++)
        {
            bool visible = s.IsVisible(s.EnemyRecords[e].InstanceId);
            int expected = visible ? BruteForceCode(s, e, ownSide: false, in g) : 0;
            AssertEqual(expected, s.EnemyRecords[e].Outflanked, "enemy row " + e);
            if (expected != 0) flagged++;
        }
        AssertTrue(flagged > 10, "the random field exercises flanks (" + flagged + ")");
    }

    private static int BruteForceCode(BattleSnapshot s, int index, bool ownSide, in FlankGeometry g)
    {
        UnitRecord u = ownSide ? s.Own[index] : s.EnemyRecords[index];
        var tx = new System.Collections.Generic.List<float>();
        var tz = new System.Collections.Generic.List<float>();
        var sx = new System.Collections.Generic.List<float>();
        var sz = new System.Collections.Generic.List<float>();
        for (int k = 0; k < s.OwnCount; k++)
        {
            if (ownSide && k == index) continue;
            UnitRecord v = s.Own[k];
            if (ownSide) { sx.Add(v.X); sz.Add(v.Z); }
            else if (v.UnitTyp != TacticalUnitType.Skirmisher && v.UnitTyp != TacticalUnitType.Scout && (v.Flags & UnitFlags.InMelee) == 0) { tx.Add(v.X); tz.Add(v.Z); }
        }
        for (int k = 0; k < s.EnemyRecordCount; k++)
        {
            if (!ownSide && k == index) continue;
            UnitRecord v = s.EnemyRecords[k];
            if (!s.IsVisible(v.InstanceId)) continue;
            if (!ownSide) { sx.Add(v.X); sz.Add(v.Z); }
            else if (v.UnitTyp != TacticalUnitType.Skirmisher && v.UnitTyp != TacticalUnitType.Scout && (v.Flags & UnitFlags.InMelee) == 0) { tx.Add(v.X); tz.Add(v.Z); }
        }
        return PerceivedFlanking.Code(u.X, u.Z, u.RotationY, u.MovementMode == 2, tx.ToArray(), tz.ToArray(), tx.Count, sx.ToArray(), sz.ToArray(), sx.Count, in g);
    }

    private static void OwnOutflankedFromVisibleOnly()
    {
        FlankGeometry g = ShippedFlank;
        BattleSnapshot s = FlankFixture();
        int self = AddOwnRegiment(s, 1, 0f, 0f, rawOutflanked: 7);
        AddEnemyRegiment(s, 2, -40f, 0f, visible: false);
        PerceivedFlanking.Apply(s, in g);
        AssertEqual(0, s.Own[self].Outflanked, "an enemy hidden in woods 40 m off the flank is not perceived (vanilla's raw code said 7)");

        s = FlankFixture();
        self = AddOwnRegiment(s, 1, 0f, 0f);
        AddEnemyRegiment(s, 2, -40f, 0f, visible: true);
        PerceivedFlanking.Apply(s, in g);
        AssertEqual(1, s.Own[self].Outflanked, "the same enemy seen flanks on the left");

        // A remembered enemy does not flank: only Visible observations are threats.
        s = FlankFixture();
        self = AddOwnRegiment(s, 1, 0f, 0f);
        s.AddEnemy(new UnitRecord { InstanceId = 2, UnitTyp = 0, X = -40f, Z = 0f, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry });
        s.AddObservation(new EnemyObservation { InstanceId = 2, UnitTyp = 0, X = -40f, Z = 0f, State = ObservationState.Remembered, Confidence01 = 0.9f });
        PerceivedFlanking.Apply(s, in g);
        AssertEqual(0, s.Own[self].Outflanked, "a remembered enemy is not a flanker");

        // Own screens: a nearer friend on the left clears it; an inactive or routed one does not count.
        s = FlankFixture();
        self = AddOwnRegiment(s, 1, 0f, 0f);
        AddOwnRegiment(s, 3, -20f, 0f);
        AddEnemyRegiment(s, 2, -40f, 0f, visible: true);
        PerceivedFlanking.Apply(s, in g);
        AssertEqual(0, s.Own[self].Outflanked, "a friend closer on the left shields");
        s.Own[1].Flags |= UnitFlags.Routed;
        PerceivedFlanking.Apply(s, in g);
        AssertEqual(1, s.Own[self].Outflanked, "a routed friend shields nothing");
    }

    private static void TargetFlankedByOwnRegimentsScreenedByVisibleFriends()
    {
        FlankGeometry g = ShippedFlank;
        // The enemy regiment at the origin faces +z; our regiment stands 40 m on its left.
        BattleSnapshot s = FlankFixture();
        int target = AddEnemyRegiment(s, 2, 0f, 0f, visible: true);
        AddOwnRegiment(s, 1, -40f, 0f);
        PerceivedFlanking.Apply(s, in g);
        AssertEqual(1, s.EnemyRecords[target].Outflanked, "our regiment on its left flanks the visible target");

        // A visible enemy friend nearer on that side screens it.
        AddEnemyRegiment(s, 3, -20f, 0f, visible: true);
        PerceivedFlanking.Apply(s, in g);
        AssertEqual(0, s.EnemyRecords[target].Outflanked, "a visible friend of the target shields its flank");

        // A hidden enemy unit is not a screen the snapshot can know about.
        s = FlankFixture();
        target = AddEnemyRegiment(s, 2, 0f, 0f, visible: true);
        AddOwnRegiment(s, 1, -40f, 0f);
        AddEnemyRegiment(s, 3, -20f, 0f, visible: false);
        PerceivedFlanking.Apply(s, in g);
        AssertEqual(1, s.EnemyRecords[target].Outflanked, "a hidden friend of the target shields nothing");
        AssertEqual(0, s.EnemyRecords[s.EnemyRecordCount - 1].Outflanked, "a hidden enemy row reads 0");

        // Our skirmishers and units in melee do not flank an enemy either (its enemyinrangereg skips them).
        s = FlankFixture();
        target = AddEnemyRegiment(s, 2, 0f, 0f, visible: true);
        AddOwnRegiment(s, 1, -40f, 0f, unitTyp: TacticalUnitType.Skirmisher);
        AddOwnRegiment(s, 4, 40f, 0f, extra: UnitFlags.InMelee);
        PerceivedFlanking.Apply(s, in g);
        AssertEqual(0, s.EnemyRecords[target].Outflanked, "skirmishers and melee do not flank");
    }

    private static void ApplyOverwritesTheRawVanillaCode()
    {
        FlankGeometry g = ShippedFlank;
        BattleSnapshot s = FlankFixture();
        int self = AddOwnRegiment(s, 1, 0f, 0f, rawOutflanked: 5);
        int target = AddEnemyRegiment(s, 2, 500f, 500f, visible: true, rawOutflanked: 3);
        int hidden = AddEnemyRegiment(s, 3, 800f, 800f, visible: false, rawOutflanked: 7);
        PerceivedFlanking.Apply(s, in g);
        AssertEqual(0, s.Own[self].Outflanked, "the builder's raw own code is replaced");
        AssertEqual(0, s.EnemyRecords[target].Outflanked, "and the visible target's");
        AssertEqual(0, s.EnemyRecords[hidden].Outflanked, "a hidden row's raw code is dropped");

        // The builder no longer reads Regiment.outflanked at all.
        string builder = System.IO.File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical/Battle/BattleSnapshotBuilder.cs"));
        AssertFalse(System.Text.RegularExpressions.Regex.IsMatch(StripComments(builder), @"\.outflanked\b"), "BattleSnapshotBuilder does not read reg.outflanked");
    }

    internal static string StripComments(string source)
    {
        return System.Text.RegularExpressions.Regex.Replace(source ?? string.Empty, @"//[^\n]*", string.Empty);
    }

    // The charge producer reads UnitRecord.Outflanked (ChargeIntentProducer.Plan.cs:271, :295),
    // which is now the perceived code: a hidden flanker does not bar it, a seen one does.
    private static void ChargeBarsOnPerceivedOutflankNotRaw()
    {
        FlankGeometry g = ShippedFlank;
        var settings = new ChargeIntentProducer.Settings(true, ChargeDoctrineTests.Radius, 0f, noChargeIfOutflanked: true);

        // Raw vanilla code 7 on the regiment, but nothing perceived on its flank: it charges.
        BattleSnapshot s = ChargeDoctrineTests.Line(1.0);
        ChargeDoctrineTests.Own(s, ChargeDoctrineTests.RegB).Outflanked = 7;
        PerceivedFlanking.Apply(s, in g);
        var h = new ChargeDoctrineTests.Harness();
        h.Plan(s, settings);
        AssertTrue(h.Sink.TryFind(ChargeDoctrineTests.RegB, out _), "a raw code the side cannot see no longer bars the charge");

        // A visible regiment 40 m off its left flank (facing +z, x = 460, same z) bars it.
        s = ChargeDoctrineTests.Line(1.0);
        s.AddEnemy(new UnitRecord { InstanceId = 901, UnitTyp = 0, X = 460f, Z = -520f, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry });
        s.AddObservation(new EnemyObservation { InstanceId = 901, UnitTyp = 0, X = 460f, Z = -520f, Strength = 500f, State = ObservationState.Visible, Confidence01 = 1f });
        PerceivedFlanking.Apply(s, in g);
        AssertEqual(1, ChargeDoctrineTests.Own(s, ChargeDoctrineTests.RegB).Outflanked, "the seen flanker is on the left");
        h = new ChargeDoctrineTests.Harness();
        h.Plan(s, settings);
        AssertFalse(h.Sink.TryFind(ChargeDoctrineTests.RegB, out _), "a seen flank bars the charge (pref set)");
    }

    private static void PerceivedFlankingAllocatesNothingAfterWarmup()
    {
        FlankGeometry g = ShippedFlank;
        var rng = new Random(7);
        BattleSnapshot s = FlankFixture();
        for (int i = 0; i < 300; i++) AddOwnRegiment(s, 100 + i, rng.Next(0, 600), -rng.Next(0, 600), rng.Next(0, 360));
        for (int i = 0; i < 300; i++) AddEnemyRegiment(s, 1000 + i, rng.Next(0, 600), -rng.Next(0, 600), visible: true, facing: rng.Next(0, 360));
        for (int k = 0; k < 3; k++) PerceivedFlanking.Apply(s, in g);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 10; k++) PerceivedFlanking.Apply(s, in g);
        AssertEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before, "no allocation after warm-up (300 own x 300 enemy)");
    }
}
