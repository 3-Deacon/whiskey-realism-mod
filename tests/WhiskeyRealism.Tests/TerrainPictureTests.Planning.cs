using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestFixtures;
using static TestHarness;

// BC12 consumers: classification (decision 3), strength (4), director (5), lines and anchors (6), inference (7), crest playbooks (8).
// Fixtures: an 800 m map (40 x 40 cells); the threat axis is usually +z (the enemy to the north), the objective at (400, -360).
internal static partial class TerrainPictureTests
{
    private static (string, Action)[] PlanningTests()
    {
        return new (string, Action)[]
        {
            // classification
            ("bc12 ObjectiveOnBridgeIsBridgeAndVerified", ObjectiveOnBridgeIsBridgeAndVerified),
            ("bc12 FordBeatsTownBeatsJunctionBeatsRidge", FordBeatsTownBeatsJunctionBeatsRidge),
            ("bc12 UnverifiedTypeStillDemoted", UnverifiedTypeStillDemoted),
            ("bc12 NoLiteralZeroTerrainFieldsRemain", NoLiteralZeroTerrainFieldsRemain),
            // strength
            ("bc12 TerrainStrengthWallOnCrestIsAnchor", TerrainStrengthWallOnCrestIsAnchor),
            ("bc12 WallOnThreeMetreCrestIsNotAnchor", WallOnThreeMetreCrestIsNotAnchor),
            ("bc12 WallAloneIsNotAnchor", WallAloneIsNotAnchor),
            ("bc12 RiverInFrontAddsPoint4", RiverInFrontAddsPoint4),
            ("bc12 ApproachDifficultyUsesSpeedCosts", ApproachDifficultyUsesSpeedCosts),
            // director
            ("bc12 AttackerAvoidsStrongEnemyHeldGround", AttackerAvoidsStrongEnemyHeldGround),
            ("bc12 DefenderPrefersStrongOwnGround", DefenderPrefersStrongOwnGround),
            // lines
            ("bc12 BestLinePicksCrestAcrossAxis", BestLinePicksCrestAcrossAxis),
            ("bc12 LineRejectedOver30DegreesOff", LineRejectedOver30DegreesOff),
            ("bc12 CrestRunsDoNotJoinAcrossAxes", CrestRunsDoNotJoinAcrossAxes),
            ("bc12 ReverseSlopeLineSetBack80m", ReverseSlopeLineSetBack80m),
            ("bc12 TerrainAnchorReplacesPreservedNone", TerrainAnchorReplacesPreservedNone),
            ("bc12 FlankAnchoredByRiverWithin80m", FlankAnchoredByRiverWithin80m),
            ("bc12 AnchoredSideGetsNoGuardFlankSlot", AnchoredSideGetsNoGuardFlankSlot),
            // inference
            ("bc12 EnemyStandingOnStrongGroundReadsDefend", EnemyStandingOnStrongGroundReadsDefend),
            ("bc12 MovingEnemyOnStrongGroundNotDefend", MovingEnemyOnStrongGroundNotDefend),
            ("bc12 BuildHasNoOwnEvidenceParameter", BuildHasNoOwnEvidenceParameter),
            // playbooks
            ("bc12 ReverseSlopeNeedsCrest", ReverseSlopeNeedsCrest),
            ("bc12 ReverseSlopeRectsBehindCrest", ReverseSlopeRectsBehindCrest),
            ("bc12 HighGroundLabelNeedsCrestShare", HighGroundLabelNeedsCrestShare),
            ("bc12 PlaybookTerrainFromPicture", PlaybookTerrainFromPicture),
            ("bc12 EveryObjectiveTypeAPlannerReadsHasAProducer", EveryObjectiveTypeAPlannerReadsHasAProducer),
        };
    }

    private const int Map = 800;
    private const float ObjX = 400f, ObjZ = -360f;

    private static ObjectiveObservationInput Observation(float x, float z, TacticalObjectiveType type = TacticalObjectiveType.UnknownVanillaObjective, bool verified = false)
    {
        return new ObjectiveObservationInput("objective-1", type, TacticalObjectiveSource.ObjectiveChain, new TacticalMapPoint(x, z), 0.75f, 0.65f, verified);
    }

    // A wall along x at world z = zWorld from x0 to x1.
    private static CoverSegment Wall(float x0, float x1, float zWorld, int cover = CoverWall) =>
        new CoverSegment { X0 = x0, Z0 = zWorld, X1 = x1, Z1 = zWorld, CoverId = cover };

    private static TerrainPicture Variant(bool ford, bool town, bool junction, bool crest)
    {
        // Everything piled on (300, -380): a listed ford, three buildings, a crossroads, a 10 m crest at z -390.
        var crossingX = ford ? new[] { 310f } : new float[0];
        var crossingZ = ford ? new[] { -375f } : new float[0];
        var crossingId = ford ? new[] { Ford } : new int[0];
        BattleTerrainGrid grid = GridWithCrossings(Map, (x, z) => junction && OnRoad(x, z - Map, 300, -380, true, true, 200) ? Road : Grass, crossingX, crossingZ, crossingId);
        Func<float, float, float> height = crest ? RidgeAlongX(30f, 5f, Map) : Flat();
        float[] bx = town ? new[] { 300f, 330f, 310f } : null;
        float[] bz = town ? new[] { -380f, -385f, -420f } : null;
        return Build(grid, Map, height, bx: bx, bz: bz);
    }

    // ------------------------------------------------------------------ decision 3: classification

    private static void ObjectiveOnBridgeIsBridgeAndVerified()
    {
        BattleTerrainGrid grid = GridWithCrossings(Map, (x, z) => Grass, new[] { 300f }, new[] { -300f }, new[] { Bridge });
        TerrainPicture p = Build(grid, Map, Flat());
        ObjectiveObservationInput o = TacticalTerrainObjectiveClassifier.Classify(Observation(330f, -320f), p, 0f, 1f);
        AssertEqual(TacticalObjectiveType.Bridge, o.Type, "an objective within 60 m of a bridge crossing");
        AssertTrue(o.TypeAnchorVerified, "verified by the picture");
        AssertEqual(TacticalObjectiveType.Bridge, TacticalObjectiveSourceModel.Normalize(o).Type, "Normalize keeps a verified specific type");
        ObjectiveObservationInput far = TacticalTerrainObjectiveClassifier.Classify(Observation(380f, -320f), p, 0f, 1f);
        AssertEqual(TacticalObjectiveType.UnknownVanillaObjective, far.Type, "90 m away: unchanged");
        AssertFalse(far.TypeAnchorVerified, "...and unverified");
        // A scene victory point is classified too (and keeps its value and source).
        ObjectiveObservationInput vp = TacticalTerrainObjectiveClassifier.Classify(Observation(320f, -310f, TacticalObjectiveType.VictoryPoint, true), p, 0f, 1f);
        AssertEqual(TacticalObjectiveType.Bridge, vp.Type, "a victory point on a bridge reads as the bridge");
        AssertNear(0.65f, vp.Value, 0.0001f, "value kept");
        // Types the picture does not classify are never touched.
        ObjectiveObservationInput line = TacticalTerrainObjectiveClassifier.Classify(Observation(300f, -300f, TacticalObjectiveType.EnemyLine, true), p, 0f, 1f);
        AssertEqual(TacticalObjectiveType.EnemyLine, line.Type, "an enemy line stays an enemy line");
        AssertTrue(ReferenceEquals(null, null) && TacticalTerrainObjectiveClassifier.Classify(Observation(330f, -320f), null, 0f, 1f).Type == TacticalObjectiveType.UnknownVanillaObjective,
            "no picture: unchanged");
    }

    private static void FordBeatsTownBeatsJunctionBeatsRidge()
    {
        var obs = Observation(300f, -380f);
        AssertEqual(TacticalObjectiveType.Ford, TacticalTerrainObjectiveClassifier.Classify(obs, Variant(true, true, true, true), 0f, 1f).Type, "ford first");
        AssertEqual(TacticalObjectiveType.Town, TacticalTerrainObjectiveClassifier.Classify(obs, Variant(false, true, true, true), 0f, 1f).Type, "then town");
        AssertEqual(TacticalObjectiveType.RoadJunction, TacticalTerrainObjectiveClassifier.Classify(obs, Variant(false, false, true, true), 0f, 1f).Type, "then junction");
        AssertEqual(TacticalObjectiveType.Ridge, TacticalTerrainObjectiveClassifier.Classify(obs, Variant(false, false, false, true), 0f, 1f).Type, "then ridge");
        ObjectiveObservationInput none = TacticalTerrainObjectiveClassifier.Classify(obs, Variant(false, false, false, false), 0f, 1f);
        AssertEqual(TacticalObjectiveType.UnknownVanillaObjective, none.Type, "nothing around: unchanged");
        AssertFalse(none.TypeAnchorVerified, "unchanged stays unverified");
        // A bridge outranks a nearer ford.
        BattleTerrainGrid both = GridWithCrossings(Map, (x, z) => Grass, new[] { 310f, 340f }, new[] { -300f, -300f }, new[] { Ford, Bridge });
        AssertEqual(TacticalObjectiveType.Bridge, TacticalTerrainObjectiveClassifier.Classify(Observation(315f, -300f), Build(both, Map, Flat()), 0f, 1f).Type,
            "a bridge within 60 m beats a nearer ford");
    }

    private static void UnverifiedTypeStillDemoted()
    {
        ObjectiveObservationInput guess = new ObjectiveObservationInput("o", TacticalObjectiveType.Bridge, TacticalObjectiveSource.ObjectiveChain,
            new TacticalMapPoint(1f, 2f), 0.9f, 0.9f, typeAnchorVerified: false, approachAvenue: TacticalApproachAvenueEstimate.None,
            terrainStrength: 0.4f, approachDifficulty: 0.3f);
        ObjectiveObservationInput normalised = TacticalObjectiveSourceModel.Normalize(guess);
        AssertEqual(TacticalObjectiveType.UnknownVanillaObjective, normalised.Type, "a specific type nobody verified is demoted");
        AssertNear(0.35f, normalised.Value, 0.0001f, "and its value capped");
        AssertNear(0.4f, normalised.TerrainStrength, 0.0001f, "the terrain strength survives Normalize");
        AssertNear(0.3f, normalised.ApproachDifficulty, 0.0001f, "the approach difficulty survives Normalize");
    }

    // The acceptance greps: the ledger no longer hard-codes zero terrain numbers; the inference no longer assumes Open.
    private static void NoLiteralZeroTerrainFieldsRemain()
    {
        string ledger = FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalOperationsLedgerRuntime.cs");
        AssertFalse(ledger.Contains("terrainStrength: 0f"), "the ledger passes the observation's terrain strength");
        AssertFalse(ledger.Contains("approachDifficulty: 0f"), "the ledger passes the observation's approach difficulty");
        AssertTrue(ledger.Contains("terrainStrength: observation.TerrainStrength"), "...it does");
        string inference = FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyIntentInference.cs");
        AssertFalse(inference.Contains("TerrainKind.Open"), "no assumed terrain in the intent inference");
    }

    // ------------------------------------------------------------------ decision 4: strength and approach difficulty

    private static TerrainPicture CrestWithWall(float peak, float slope, bool wall)
    {
        BattleTerrainGrid grid = Grid(Map, (x, z) => Grass);
        // RidgeAlongX peaks at world z = -390; the wall runs along it.
        return Build(grid, Map, RidgeAlongX(peak, slope, Map), cover: wall ? new[] { Wall(100f, 700f, -390f) } : null);
    }

    private static void TerrainStrengthWallOnCrestIsAnchor()
    {
        TerrainPicture p = CrestWithWall(30f, 5f, wall: true);
        float s = p.TerrainStrength(400f, -390f, 0f, 1f);
        AssertNear(0.5667f, s, 0.005f, "wall 0.35 on a 10 m crest (h 0.333): 1 - 0.65 x 0.667");
        AssertTrue(s >= TerrainPicture.AnchorStrength, "a wall on a 10 m crest clears the planners' anchor strength 0.55");
    }

    private static void WallOnThreeMetreCrestIsNotAnchor()
    {
        TerrainPicture p = CrestWithWall(20f, 1.5f, wall: true);
        float s = p.TerrainStrength(400f, -390f, 0f, 1f);
        AssertNear(0.415f, s, 0.005f, "wall 0.35 on a 3 m crest (h 0.10): 1 - 0.65 x 0.9");
        AssertTrue(s < TerrainPicture.AnchorStrength, "not an anchor");
        TerrainPicture crestOnly = CrestWithWall(30f, 5f, wall: false);
        AssertNear(0.3333f, crestOnly.TerrainStrength(400f, -390f, 0f, 1f), 0.005f, "a 10 m crest alone is 0.33 (a crest alone is at most 0.5)");
        TerrainPicture tall = CrestWithWall(120f, 30f, wall: false);
        AssertNear(0.5f, tall.TerrainStrength(400f, -390f, 0f, 1f), 0.005f, "prominence is capped at 15 m: h = 0.5");
    }

    private static void WallAloneIsNotAnchor()
    {
        BattleTerrainGrid grid = Grid(Map, (x, z) => Grass);
        TerrainPicture p = Build(grid, Map, Flat(), cover: new[] { Wall(100f, 700f, -390f) });
        AssertNear(0.35f, p.TerrainStrength(400f, -390f, 0f, 1f), 0.0001f, "a wall on flat ground");
        AssertNear(0.35f, p.TerrainStrength(400f, -350f, 0f, 1f), 0.0001f, "the wall counts within 60 m");
        AssertEqual(0f, p.TerrainStrength(400f, -200f, 0f, 1f), "and not 190 m away");
        TerrainPicture sunken = Build(grid, Map, Flat(), cover: new[] { Wall(100f, 700f, -390f, CoverSunken) });
        AssertNear(0.30f, sunken.TerrainStrength(400f, -390f, 0f, 1f), 0.0001f, "a sunken road 0.30");
    }

    private static void RiverInFrontAddsPoint4()
    {
        // A river band across the front: world z -440..-380 for the whole width; the point stands at z -600 facing +z, so the
        // band lies 160-220 m in front, on every transect between 100 and 300 m.
        BattleTerrainGrid river = Grid(Map, (x, z) => (z - Map >= -440 && z - Map < -380) ? River : Grass);
        TerrainPicture p = Build(river, Map, Flat());
        AssertNear(0.4f, p.TerrainStrength(400f, -600f, 0f, 1f), 0.0001f, "water across the front: 0.4");
        AssertEqual(0f, p.TerrainStrength(400f, -600f, 0f, -1f), "facing the other way there is no water in front");
        // A 10 m crest behind a river: 1 - (1 - 0.333)(1 - 0.4) = 0.60.
        TerrainPicture both = Build(Grid(Map, (x, z) => (z - Map >= -560 && z - Map < -500) ? River : Grass), Map, RidgeAlongX(30f, 5f, Map));
        // crest at z -390; the river 110-170 m in front of a point on the crest at -690... the point is the crest cell itself
        // when the river lies 100-300 m ahead of it, so move the river to 100-300 m ahead of z -390 along +z:
        TerrainPicture ahead = Build(Grid(Map, (x, z) => (z - Map >= -300 && z - Map < -230) ? River : Grass), Map, RidgeAlongX(30f, 5f, Map));
        AssertNear(0.6f, ahead.TerrainStrength(400f, -390f, 0f, 1f), 0.005f, "a 10 m crest behind a river: 0.60");
        // Narrow water is not a river line across the front: a creek column 20 m wide covers 1 of 11 lateral points.
        BattleTerrainGrid thin = Grid(Map, (x, z) => (x >= 395 && x < 415) ? River : Grass);
        AssertEqual(0f, Build(thin, Map, Flat()).TerrainStrength(400f, -600f, 0f, 1f), "water along the axis is not across it");
        // A town is 0.7.
        TerrainPicture town = Build(Grid(Map, (x, z) => Grass), Map, Flat(), bx: new[] { 300f, 320f, 340f }, bz: new[] { -300f, -310f, -320f });
        AssertNear(0.7f, town.TerrainStrength(320f, -310f, 0f, 1f), 0.0001f, "building cover 0.70");
        AssertTrue(both.Ready, "fixture built");
    }

    private static void ApproachDifficultyUsesSpeedCosts()
    {
        // The 300 m in front of (400, -600) along +z.
        BattleTerrainGrid grassland = Grid(Map, (x, z) => Grass);
        AssertEqual(0f, Build(grassland, Map, Flat()).ApproachDifficulty(400f, -600f, 0f, 1f), "grass cost 2: no difficulty");
        BattleTerrainGrid swamp = Grid(Map, (x, z) => 15);
        AssertNear(1f, Build(swamp, Map, Flat()).ApproachDifficulty(400f, -600f, 0f, 1f), 0.0001f, "swamp cost 6: full difficulty");
        BattleTerrainGrid forest = Grid(Map, (x, z) => 14);
        AssertNear(0.5f, Build(forest, Map, Flat()).ApproachDifficulty(400f, -600f, 0f, 1f), 0.0001f, "forest cost 4: (4 - 2) / (6 - 2)");
        BattleTerrainGrid half = Grid(Map, (x, z) => (z - Map >= -600 + 150) ? 15 : Grass);
        float mixed = Build(half, Map, Flat()).ApproachDifficulty(400f, -600f, 0f, 1f);
        AssertNear(0.5f, mixed, 0.07f, "half swamp half grass along the approach: about 0.5, got " + mixed);
        AssertEqual(0f, Build(swamp, Map, Flat()).ApproachDifficulty(400f, -600f, 0f, 0f), "no axis, no approach");
    }

    // ------------------------------------------------------------------ decision 5: the director's sign

    private static BattlefieldObjectiveEstimate Estimate(string id, float enemy, float terrain, float approach) =>
        new BattlefieldObjectiveEstimate(id, TacticalObjectiveType.Ridge, enemy, 0.8f, false, 0.5f, 100f, 100f, terrain, approach);

    private static void AttackerAvoidsStrongEnemyHeldGround()
    {
        BattlefieldObjectiveEstimate strongHeld = Estimate("held-strong", 3000f, 0.7f, 0.5f);
        BattlefieldObjectiveEstimate weakHeld = Estimate("held-weak", 3000f, 0.1f, 0.0f);
        foreach (BattleStance stance in new[] { BattleStance.Attacker, BattleStance.Meeting })
        {
            AssertTrue(TacticalOperationDirector.ObjectiveSelectionScore(strongHeld, stance) < TacticalOperationDirector.ObjectiveSelectionScore(weakHeld, stance),
                stance + ": strong enemy-held ground scores lower than weak enemy-held ground");
        }
        // The pick follows the score.
        var current = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Planning, "objective-unknown", 0f);
        var input = TacticalOperationDirectorInput.ForTest(current, 100f, 8000f, 0.2f, 0.6f, 0.4f, new[] { strongHeld, weakHeld }).WithStance(BattleStance.Attacker);
        AssertEqual("held-weak", TacticalOperationDirector.Decide(input).Operation.PrimaryObjectiveId, "the attacker goes for the weaker ground");
    }

    private static void DefenderPrefersStrongOwnGround()
    {
        BattlefieldObjectiveEstimate strongOwn = Estimate("own-strong", 0f, 0.7f, 0.6f);
        BattlefieldObjectiveEstimate weakOwn = Estimate("own-weak", 0f, 0.1f, 0.0f);
        AssertTrue(TacticalOperationDirector.ObjectiveSelectionScore(strongOwn, BattleStance.Defender) > TacticalOperationDirector.ObjectiveSelectionScore(weakOwn, BattleStance.Defender),
            "a Defender values strong own ground");
        AssertTrue(TacticalOperationDirector.ObjectiveSelectionScore(strongOwn, BattleStance.Meeting) > TacticalOperationDirector.ObjectiveSelectionScore(weakOwn, BattleStance.Meeting),
            "so does any side: seize strong ground");
        // The Defender also counts half the enemy's approach difficulty; the others do not.
        float defender = TacticalOperationDirector.ObjectiveSelectionScore(strongOwn, BattleStance.Defender);
        float meeting = TacticalOperationDirector.ObjectiveSelectionScore(strongOwn, BattleStance.Meeting);
        AssertNear(0.5f * 0.6f, defender - meeting, 0.0001f, "0.5 x approach difficulty for a Defender");
        // An enemy-held objective keeps today's sign for a Defender too.
        BattlefieldObjectiveEstimate heldStrong = Estimate("held", 2000f, 0.7f, 0.6f);
        BattlefieldObjectiveEstimate heldWeak = Estimate("held2", 2000f, 0.0f, 0.0f);
        AssertTrue(TacticalOperationDirector.ObjectiveSelectionScore(heldStrong, BattleStance.Defender) < TacticalOperationDirector.ObjectiveSelectionScore(heldWeak, BattleStance.Defender),
            "enemy-held strong ground is still avoided");
    }

    // ------------------------------------------------------------------ decision 6: lines

    private static void BestLinePicksCrestAcrossAxis()
    {
        BattleTerrainGrid grid = Grid(Map, (x, z) => Grass);
        // A 10 m crest along x at z -390 (30 m behind the objective), a fence 60 m further back at z -300... on the own side.
        TerrainPicture p = Build(grid, Map, RidgeAlongX(30f, 5f, Map), cover: new[] { Wall(100f, 700f, -300f, CoverFence) });
        TerrainLine line = TacticalTerrainLinePlanner.BestLine(p, ObjX, ObjZ, 0f, 1f, 600f);
        AssertTrue(line.Has, "a line is found");
        AssertEqual(TerrainLineKind.Crest, line.Kind, "the crest (0.33) beats the fence (0.10) at nearly the same distance");
        AssertTrue(Math.Abs(line.CenterZ - (-390f)) < 11f, "the line runs along the crest, z " + line.CenterZ);
        AssertTrue(Math.Abs(line.CenterX - 400f) < 60f, "centred on the objective's lateral, x " + line.CenterX);
        AssertTrue(line.Length >= 600f, "most of the window is crest, length " + line.Length);
        AssertTrue(line.Strength > 0.3f && line.Strength < 0.4f, "strength of a 10 m crest, " + line.Strength);
        // A wall along the same line outranks the bare crest.
        TerrainPicture walled = Build(grid, Map, RidgeAlongX(30f, 5f, Map), cover: new[] { Wall(100f, 700f, -390f) });
        TerrainLine strong = TacticalTerrainLinePlanner.BestLine(walled, ObjX, ObjZ, 0f, 1f, 600f);
        AssertTrue(strong.Has && strong.Strength > 0.5f, "wall on crest: strength " + strong.Strength);
        AssertFalse(TacticalTerrainLinePlanner.BestLine(null, ObjX, ObjZ, 0f, 1f, 600f).Has, "no picture, no line");
        AssertFalse(TacticalTerrainLinePlanner.BestLine(p, ObjX, ObjZ, 0f, 0f, 600f).Has, "no axis, no line");
        // The crest line alone.
        AssertEqual(TerrainLineKind.Crest, TacticalTerrainLinePlanner.BestCrest(p, ObjX, ObjZ, 0f, 1f, 600f).Kind, "BestCrest returns crest runs only");
        AssertFalse(TacticalTerrainLinePlanner.BestCrest(Build(grid, Map, Flat(), cover: new[] { Wall(100f, 700f, -390f) }), ObjX, ObjZ, 0f, 1f, 600f).Has, "a wall is no crest");
    }

    private static void LineRejectedOver30DegreesOff()
    {
        BattleTerrainGrid grid = Grid(Map, (x, z) => Grass);
        TerrainPicture p = Build(grid, Map, Flat(), cover: new[] { Wall(100f, 700f, -390f) });
        float rad20 = 20f * (float)Math.PI / 180f, rad40 = 40f * (float)Math.PI / 180f;
        AssertTrue(TacticalTerrainLinePlanner.BestLine(p, ObjX, ObjZ, (float)Math.Sin(rad20), (float)Math.Cos(rad20), 600f).Has,
            "a wall 20 degrees off perpendicular is across the threat axis");
        AssertFalse(TacticalTerrainLinePlanner.BestLine(p, ObjX, ObjZ, (float)Math.Sin(rad40), (float)Math.Cos(rad40), 600f).Has,
            "40 degrees off perpendicular is not");
        AssertFalse(TacticalTerrainLinePlanner.BestLine(p, ObjX, ObjZ, 1f, 0f, 600f).Has, "a wall along the axis is no defensive line");
        // Length: 100 m of wall is too short, 220 m is enough.
        TerrainPicture shortWall = Build(grid, Map, Flat(), cover: new[] { Wall(350f, 450f, -390f) });
        AssertFalse(TacticalTerrainLinePlanner.BestLine(shortWall, ObjX, ObjZ, 0f, 1f, 600f).Has, "under MinLineMeters 200");
        TerrainPicture longWall = Build(grid, Map, Flat(), cover: new[] { Wall(290f, 510f, -390f) });
        AssertTrue(TacticalTerrainLinePlanner.BestLine(longWall, ObjX, ObjZ, 0f, 1f, 600f).Has, "220 m of wall");
        // 100 m in front is allowed, 200 m in front is not.
        TerrainPicture ahead100 = Build(grid, Map, Flat(), cover: new[] { Wall(100f, 700f, -270f) });
        AssertTrue(TacticalTerrainLinePlanner.BestLine(ahead100, ObjX, ObjZ, 0f, 1f, 600f).Has, "a wall 90 m in front of the objective");
        TerrainPicture ahead200 = Build(grid, Map, Flat(), cover: new[] { Wall(100f, 700f, -160f) });
        AssertFalse(TacticalTerrainLinePlanner.BestLine(ahead200, ObjX, ObjZ, 0f, 1f, 600f).Has, "a wall 200 m in front is the enemy's side");
    }

    private static void CrestRunsDoNotJoinAcrossAxes()
    {
        // Two 10 m ridges that cross: one along x at world z -390 (across the +z threat axis), one along z at x 410. Joined into one
        // blob they would have no direction; by profile axis they are two runs and the cross-axis one is the line.
        BattleTerrainGrid grid = Grid(Map, (x, z) => Grass);
        Func<float, float, float> alongX = RidgeAlongX(30f, 5f, Map), alongZ = RidgeAlongZ(30f, 5f);
        TerrainPicture p = Build(grid, Map, (x, z) => Math.Max(alongX(x, z), alongZ(x, z)));
        TerrainLine line = TacticalTerrainLinePlanner.BestCrest(p, ObjX, ObjZ, 0f, 1f, 600f);
        AssertTrue(line.Has, "the ridge across the axis is found among the crossing ridges");
        AssertTrue(line.Length >= 500f, "a long run, not a fragment: " + line.Length);
        AssertTrue(Math.Abs(line.CenterZ - (-390f)) < 15f, "it is the ridge along x, z " + line.CenterZ);
        // Along-z only: no line across a +z axis.
        TerrainPicture alongOnly = Build(grid, Map, alongZ);
        AssertFalse(TacticalTerrainLinePlanner.BestCrest(alongOnly, ObjX, ObjZ, 0f, 1f, 600f).Has, "a ridge along the threat axis is no line");
    }

    private static void ReverseSlopeLineSetBack80m()
    {
        BattleTerrainGrid grid = Grid(Map, (x, z) => Grass);
        TerrainPicture p = Build(grid, Map, RidgeAlongX(30f, 5f, Map));
        TerrainLine crest = TacticalTerrainLinePlanner.BestLine(p, ObjX, ObjZ, 0f, 1f, 600f);
        TacticalTerrainLinePlanner.AnchorPoint(in crest, 0f, 1f, false, out float fx, out float fz);
        TacticalTerrainLinePlanner.AnchorPoint(in crest, 0f, 1f, true, out float rx, out float rz);
        AssertNear(crest.CenterZ + TacticalTerrainLinePlanner.ForwardEdgeMeters, fz, 0.01f, "a crest is held at its forward edge");
        AssertNear(crest.CenterZ - 80f, rz, 0.01f, "under ReverseSlopeDefense 80 m behind the crest");
        AssertEqual(80f, TacticalTerrainLinePlanner.ReverseSlopeSetbackMeters, "ReverseSlopeSetbackMeters");
        AssertNear(crest.CenterX, rx, 0.01f, "laterally unchanged");
        // A wall is held on its centre, a river from 150 m behind it.
        var wall = new TerrainLine { Has = true, Kind = TerrainLineKind.Wall, CenterX = 400f, CenterZ = -390f };
        TacticalTerrainLinePlanner.AnchorPoint(in wall, 0f, 1f, true, out float wx, out float wz);
        AssertNear(-390f, wz, 0.01f, "a wall has no reverse slope");
        var riverLine = new TerrainLine { Has = true, Kind = TerrainLineKind.River, CenterX = 400f, CenterZ = -390f };
        TacticalTerrainLinePlanner.AnchorPoint(in riverLine, 0f, 1f, false, out float qx, out float qz);
        AssertNear(-390f - 150f, qz, 0.01f, "a river line is held from 150 m behind it");
    }

    private static TacticalBattlefrontSnapshot NoFront => default(TacticalBattlefrontSnapshot);

    // An objective on a ridge with an approach from the south (the enemy coming north: avenue axis +z means the enemy travels +z,
    // so the threat axis toward the enemy is -z): the crest line must lie across z.
    private static BattlefieldObjectiveEstimate RidgeObjective(float x, float z, float enemyOriginZ)
    {
        return new BattlefieldObjectiveEstimate("ridge-1", TacticalObjectiveType.Ridge, 0f, 0.8f, false, 0.6f, x, z, 0.6f, 0.2f,
            TacticalApproachAvenueEstimate.Create(new TacticalMapPoint(x, enemyOriginZ), new TacticalMapPoint(x, z),
                TacticalApproachAvenueSource.ScheduledArrival, 0.9f, roadAnchored: false, crossingAnchored: false, "test-avenue"));
    }

    private static void TerrainAnchorReplacesPreservedNone()
    {
        BattleTerrainGrid grid = Grid(Map, (x, z) => Grass);
        // The enemy comes from z -760 toward the objective at z -360 (avenue axis +z; the threat axis is -z): the crest at z -390 lies
        // 30 m in front of the objective.
        BattlefieldObjectiveEstimate objective = RidgeObjective(ObjX, ObjZ, -760f);
        TacticalDefensiveLineAnchor preserved = TacticalDefensiveLineAnchorPlanner.Plan(objective, NoFront, 6, (TerrainPicture)null);
        AssertFalse(preserved.HasAnchor, "without a picture the terrain anchor is preserved as today");
        AssertEqual("defensive-line-terrain-anchor-preserved", preserved.Reason, "today's reason");

        TerrainPicture p = Build(grid, Map, RidgeAlongX(30f, 5f, Map));
        TacticalDefensiveLineAnchor anchor = TacticalDefensiveLineAnchorPlanner.Plan(objective, NoFront, 6, p);
        AssertTrue(anchor.HasAnchor, "with a picture the best terrain line becomes the anchor: " + anchor.Reason);
        AssertTrue(anchor.Reason.StartsWith("terrain-line:crest", StringComparison.Ordinal), "reason names the line kind: " + anchor.Reason);
        AssertFalse(anchor.CorridorAnchor, "a terrain line is not a corridor");
        AssertTrue(anchor.FrontageWidth >= 350f && anchor.FrontageWidth <= 760f, "frontage clamped to 350-760, " + anchor.FrontageWidth);
        AssertNear(-400f, anchor.Center.Z, 12f, "centre at the crest's forward edge (the threat axis is -z: forward is lower z)");
        // Under ReverseSlopeDefense the centre sits 80 m behind the crest, on the own side.
        TacticalDefensiveLineAnchor reverse = TacticalDefensiveLineAnchorPlanner.Plan(objective, NoFront, 6, p, reverseSlope: true);
        AssertTrue(reverse.HasAnchor, "reverse slope anchor");
        AssertNear(anchor.Center.Z + 90f, reverse.Center.Z, 12f, "80 m behind the crest instead of at its forward edge");
        // A corridor objective still takes the corridor path; a weak non-terrain objective is not touched.
        BattlefieldObjectiveEstimate weak = new BattlefieldObjectiveEstimate("o", TacticalObjectiveType.UnknownVanillaObjective, 0f, 0.8f, false, 0.6f,
            ObjX, ObjZ, 0.1f, 0.1f, objective.ApproachAvenue);
        AssertTrue(TacticalDefensiveLineAnchorPlanner.Plan(weak, NoFront, 6, p).Reason.StartsWith("approach-intercept", StringComparison.Ordinal),
            "a non-terrain objective keeps the approach intercept");
        // No line near the objective: preserved.
        TerrainPicture flat = Build(grid, Map, Flat());
        AssertEqual("defensive-line-terrain-anchor-preserved", TacticalDefensiveLineAnchorPlanner.Plan(objective, NoFront, 6, flat).Reason, "no line, no change");
        // The three-argument call reads the planning picture through the provider and the flag.
        Func<TerrainPicture> saved = TerrainPicture.Provider;
        try
        {
            TerrainPicture.Provider = () => p;
            TerrainPicture.PlanningEnabled = () => true;
            AssertTrue(TacticalDefensiveLineAnchorPlanner.Plan(objective, NoFront, 6).HasAnchor, "the provider's picture");
            TerrainPicture.PlanningEnabled = () => false;
            AssertFalse(TacticalDefensiveLineAnchorPlanner.Plan(objective, NoFront, 6).HasAnchor, "Enable Battle Terrain Picture off: preserved");
        }
        finally
        {
            TerrainPicture.Provider = saved;
            TerrainPicture.PlanningEnabled = null;
        }
    }

    private static void FlankAnchoredByRiverWithin80m()
    {
        // A vertical river at x 600-640 (all z).
        BattleTerrainGrid grid = Grid(Map, (x, z) => (x >= 600 && x < 640) ? River : Grass);
        TerrainPicture p = Build(grid, Map, Flat());
        AssertTrue(TacticalTerrainLinePlanner.FlankAnchored(p, 560f, -400f, 1f, 0f), "river 40 m sideways of the flank end");
        AssertTrue(TacticalTerrainLinePlanner.FlankAnchored(p, 530f, -400f, 1f, 0f), "70 m is inside the 80 m reach");
        AssertFalse(TacticalTerrainLinePlanner.FlankAnchored(p, 500f, -400f, 1f, 0f), "100 m is outside it");
        AssertFalse(TacticalTerrainLinePlanner.FlankAnchored(p, 560f, -400f, -1f, 0f), "the other way is open ground");
        // A crest within 50 m of the end.
        TerrainPicture crest = Build(Grid(Map, (x, z) => Grass), Map, RidgeAlongX(30f, 5f, Map));
        AssertTrue(TacticalTerrainLinePlanner.FlankAnchored(crest, 400f, -420f, 1f, 0f), "a crest 30 m from the end");
        AssertFalse(TacticalTerrainLinePlanner.FlankAnchored(crest, 400f, -500f, 1f, 0f), "110 m from the crest");
        AssertFalse(TacticalTerrainLinePlanner.FlankAnchored(null, 560f, -400f, 1f, 0f), "no picture");
        AssertEqual(80f, TerrainShapeRules.Shipped.AnchorRiverMeters, "distancetocheckforanchoredflanksriver");
    }

    private static void AnchoredSideGetsNoGuardFlankSlot()
    {
        // The slot rule itself (CommandDoctrineAssignment.ApplyScourgeSlotTask): with six or more nodes the end slots of a
        // forming-up line become flank guards; an anchored side keeps the line task.
        AssertEqual(CommandTaskType.GuardFlank, CommandDoctrineAssignment.ApplyScourgeSlotTask(CommandTaskType.FormUp, CommandNodeRole.MainEffort, 0, 6, false, false),
            "the left end slot guards the flank");
        AssertEqual(CommandTaskType.FormUp, CommandDoctrineAssignment.ApplyScourgeSlotTask(CommandTaskType.FormUp, CommandNodeRole.MainEffort, 0, 6, true, false),
            "...unless the left side is anchored");
        AssertEqual(CommandTaskType.GuardFlank, CommandDoctrineAssignment.ApplyScourgeSlotTask(CommandTaskType.FormUp, CommandNodeRole.MainEffort, 5, 6, true, false),
            "the right end still guards when only the left is anchored");
        AssertEqual(CommandTaskType.FormUp, CommandDoctrineAssignment.ApplyScourgeSlotTask(CommandTaskType.FormUp, CommandNodeRole.MainEffort, 5, 6, false, true),
            "an anchored right side keeps the line task");
        AssertEqual(CommandTaskType.AdvanceToAssembly, CommandDoctrineAssignment.ApplyScourgeSlotTask(CommandTaskType.AdvanceToAssembly, CommandNodeRole.MainEffort, 5, 6, false, true),
            "the assembly task too");
        AssertEqual(CommandTaskType.GuardFlank, CommandDoctrineAssignment.ApplyScourgeSlotTask(CommandTaskType.FormUp, CommandNodeRole.FlankMarch, 3, 6, true, true),
            "a flank-march role guards whatever the anchors say");

        // The anchor planner marks the ends: a wall with a river off its left end.
        BattleTerrainGrid grid = Grid(Map, (x, z) => (x >= 40 && x < 70) ? River : Grass);
        TerrainPicture p = Build(grid, Map, Flat(), cover: new[] { Wall(100f, 700f, -390f) });
        BattlefieldObjectiveEstimate objective = RidgeObjective(ObjX, ObjZ, -760f);
        TacticalDefensiveLineAnchor anchor = TacticalDefensiveLineAnchorPlanner.Plan(objective, NoFront, 6, p);
        AssertTrue(anchor.HasAnchor, "a wall line anchor: " + anchor.Reason);
        AssertTrue(anchor.AnchoredLeft, "the river off the left end anchors the left");
        AssertFalse(anchor.AnchoredRight, "the right end is open");
        AssertTrue(anchor.Reason.EndsWith(":anchored-left", StringComparison.Ordinal), "reason suffix: " + anchor.Reason);
        // The assembly builds the slot task from the anchor.
        string doctrine = FlankResponseTests.SourceText("Tactical", "Operations", "CommandDoctrineAssignment.cs");
        AssertTrue(doctrine.Contains("ApplyScourgeSlotTask(task, node.Role, i, nodes.Length, anchor.AnchoredLeft, anchor.AnchoredRight)"), "Build passes the anchored sides");
    }

    // ------------------------------------------------------------------ decision 7: inference

    private static EnemyVisibleState OneSector(float own, float enemy, bool recentFire, float strongShare)
    {
        return new EnemyVisibleState(new[] { new EnemyVisibleSector(0, own, enemy, recentFire, strongShare) },
            enemyReserveCommitFraction: 0.5f, anyContactSpotted: true, anyContactBroken: false);
    }

    private static void EnemyStandingOnStrongGroundReadsDefend()
    {
        // Control: the same sector without strong ground is a Probe (a single sector, no fire, the enemy not much bigger).
        TacticalIntentModel plain = ArmyIntentInference.Build(OneSector(5000f, 4000f, false, 0f));
        AssertEqual(InferredIntent.Probe, plain.PrimaryIntent, "control: a single quiet sector reads Probe");
        TacticalIntentModel strong = ArmyIntentInference.Build(OneSector(5000f, 4000f, false, 0.8f));
        AssertEqual(InferredIntent.Defend, strong.PrimaryIntent, "an enemy standing on strong ground reads Defend");
        AssertTrue(Array.IndexOf(strong.SupportingEvidence, EvidenceTag.EnemyOnStrongGround) >= 0, "with the EnemyOnStrongGround tag");
        // Receiving fire from the sector: the existing rules decide.
        TacticalIntentModel firing = ArmyIntentInference.Build(OneSector(5000f, 4000f, true, 0.8f));
        AssertTrue(Array.IndexOf(firing.SupportingEvidence, EvidenceTag.EnemyOnStrongGround) < 0 || firing.PrimaryIntent == InferredIntent.Defend,
            "recent fire does not turn the strong-ground rule on");
        AssertFalse(firing.PrimaryIntent == InferredIntent.Defend && Array.IndexOf(firing.SupportingEvidence, EvidenceTag.EnemyOnStrongGround) >= 0,
            "no strong-ground Defend with recent fire from the sector");
        // Under the 0.6 share nothing changes.
        AssertEqual(InferredIntent.Probe, ArmyIntentInference.Build(OneSector(5000f, 4000f, false, 0.59f)).PrimaryIntent, "0.59 is under the 0.6 share");
        AssertEqual(InferredIntent.Defend, ArmyIntentInference.Build(OneSector(5000f, 4000f, false, 0.6f)).PrimaryIntent, "exactly 0.6 counts");
    }

    private static void MovingEnemyOnStrongGroundNotDefend()
    {
        // CollectEnemySamples: the share counts only enemies that stand (no paths) on cells with strength >= 0.35.
        const int size = 800;
        BattleTerrainGrid grid = Grid(size, (x, z) => Grass);
        TerrainPicture p = Build(grid, size, Flat(), cover: new[] { Wall(100f, 700f, -390f), Wall(100f, 700f, -391f) });
        BattleSnapshot s = BattlePerceptionTests.ConsumerSnapshot(10.0, new[] { BattlePerceptionTests.OwnUnit(1, 400f, -700f, strength: 1000) },
            BattlePerceptionTests.Seen(11, 400f, -390f, 800f, standing: true),
            BattlePerceptionTests.Seen(12, 450f, -390f, 800f, standing: false),
            BattlePerceptionTests.Seen(13, 500f, -200f, 800f, standing: true));
        var visible = new List<FrontUnitSample>();
        var weighted = new List<FrontUnitSample>();
        var known = new List<FrontUnitSample>();
        ArmyEvidenceBuilder.CollectEnemySamples(s, new HashSet<int>(), visible, weighted, known, p);
        AssertEqual(3, weighted.Count, "three samples");
        AssertTrue(weighted[0].OnStrongGround && weighted[0].Visible, "the standing enemy on the wall");
        AssertFalse(weighted[1].OnStrongGround, "a marching enemy on the wall is not on strong ground (it has paths)");
        AssertFalse(weighted[2].OnStrongGround, "a standing enemy on open ground neither");
        // The sector's share is strong-ground strength over visible strength.
        var own = new List<FrontUnitSample>();
        TacticalFrontAxis axis = TacticalFrontAxis.FromCentroids(400f, -700f, 400f, -300f).WithOwnExtent(-100f, 100f);
        EnemyVisibleSector[] sectors = EnemyVisibleSectorBuilder.BuildSectors(axis, own, weighted);
        float share = 0f;
        float strongTotal = 0f, all = 0f;
        foreach (EnemyVisibleSector sec in sectors) { strongTotal += sec.EnemyStrongGroundShare * sec.EnemyStrength; all += sec.EnemyStrength; }
        share = all > 0f ? strongTotal / all : 0f;
        AssertNear(800f / 2400f, share, 0.01f, "one of three equal enemies stands on strong ground");
        // No picture: no flag.
        var plainVisible = new List<FrontUnitSample>(); var plainWeighted = new List<FrontUnitSample>(); var plainKnown = new List<FrontUnitSample>();
        ArmyEvidenceBuilder.CollectEnemySamples(s, new HashSet<int>(), plainVisible, plainWeighted, plainKnown);
        AssertFalse(plainWeighted[0].OnStrongGround, "without a picture nothing is strong ground");
        // A Remembered enemy is not counted: only what is Visible stands on known ground.
        BattleSnapshot remembered = BattlePerceptionTests.ConsumerSnapshot(10.0, new[] { BattlePerceptionTests.OwnUnit(1, 400f, -700f, strength: 1000) },
            BattlePerceptionTests.Seen(11, 400f, -390f, 800f, confidence: 0.8f, remembered: true, standing: true));
        var rv = new List<FrontUnitSample>(); var rw = new List<FrontUnitSample>(); var rk = new List<FrontUnitSample>();
        ArmyEvidenceBuilder.CollectEnemySamples(remembered, new HashSet<int>(), rv, rw, rk, p);
        AssertFalse(rw[0].OnStrongGround || rw[0].Visible, "a remembered enemy is not read as standing on strong ground");
    }

    private static void BuildHasNoOwnEvidenceParameter()
    {
        // Compile pin: Build takes the enemy state only; BuildForFrontage still filters by sector.
        TacticalIntentModel m = ArmyIntentInference.Build(OneSector(5000f, 4000f, false, 0f));
        AssertTrue(m.Confidence01 > 0f, "built from the enemy state alone");
        TacticalIntentModel f = ArmyIntentInference.BuildForFrontage(0, OneSector(5000f, 4000f, false, 0.9f));
        AssertEqual(InferredIntent.Defend, f.PrimaryIntent, "the per-child filter carries the strong-ground share");
        string inference = FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyIntentInference.cs");
        AssertFalse(inference.Contains("ArmyEvidence ownEvidence"), "the dead parameter is gone");
    }

    // ------------------------------------------------------------------ decision 8: crest playbooks

    private static PlaybookContext Longstreet(bool crest)
    {
        var longstreet = new PersonalityVector(-0.2f, 0.5f, -0.5f, 0.4f, 0.3f);
        return new PlaybookContext(longstreet, TerrainKind.Mountain, currentOdds: 0.95f, opposingCommanderHint: 0f, defaultMainEffortSector: 0,
            jitterSeed: 18892, hasCrestLine: crest);
    }

    private static void ReverseSlopeNeedsCrest()
    {
        TacticalPlaybookCatalog catalog = SeedCatalog.AllHistoricalAndGeneric();
        AssertEqual(BattlePlanId.ReverseSlopeDefense, catalog.Select(Longstreet(true)).Id, "with a crest line Longstreet's play is the reverse slope");
        AssertTrue(catalog.Select(Longstreet(false)).Id != BattlePlanId.ReverseSlopeDefense, "without one it is never selected");
        AssertTrue(new ReverseSlopeDefensePlaybook().RequiresCrest, "the play says it needs a crest");
        AssertFalse(new PreparedDefensePlaybook().RequiresCrest, "other plays do not");
        // The crest line of the evidence: the best crest run across the front.
        BattleTerrainGrid grid = Grid(Map, (x, z) => Grass);
        TerrainPicture withCrest = Build(grid, Map, RidgeAlongX(30f, 5f, Map));
        TacticalFrontAxis axis = TacticalFrontAxis.FromCentroids(400f, -700f, 400f, -100f).WithOwnExtent(-200f, 200f);
        AssertTrue(ArmyEvidenceBuilder.CrestLineAcrossFront(withCrest, axis).Has, "a crest across the front");
        AssertFalse(ArmyEvidenceBuilder.CrestLineAcrossFront(Build(grid, Map, Flat()), axis).Has, "flat ground: none");
        AssertFalse(ArmyEvidenceBuilder.CrestLineAcrossFront(null, axis).Has, "no picture: none");
        var evidence = new ArmyEvidence(1f, TerrainKind.Open, 0, ArmyEvidenceBuilder.CrestLineAcrossFront(withCrest, axis));
        AssertTrue(evidence.CrestLine.Has, "ArmyEvidence carries the crest line");
    }

    private static void ReverseSlopeRectsBehindCrest()
    {
        PlayTemplate template = PlayTemplateCatalog.Lookup(BattlePlanId.ReverseSlopeDefense);
        var crest = new TerrainLine { Has = true, Kind = TerrainLineKind.Crest, CenterX = 400f, CenterZ = -350f, X0 = 100f, X1 = 700f, Z0 = -350f, Z1 = -350f, Strength = 0.4f };
        IReadOnlyList<WorldPlayRect> plain = PlayProjection.Project(template, 400f, -700f, 400f, -100f, 600f);
        IReadOnlyList<WorldPlayRect> shaped = PlayProjection.Project(template, 400f, -700f, 400f, -100f, 600f, in crest);
        WorldPlayRect Find(IReadOnlyList<WorldPlayRect> rects, PlayRectKind kind)
        {
            foreach (WorldPlayRect r in rects) if (r.Kind == kind) return r;
            throw new Exception("no rect " + kind);
        }
        AssertNear(-350f, Find(shaped, PlayRectKind.ForwardScreen).AnchorZ, 0.01f, "the screen stands on the crest");
        AssertNear(-350f - 80f, Find(shaped, PlayRectKind.CenterHold).AnchorZ, 0.01f, "the centre holds 80 m behind the crest");
        AssertNear(-350f - 80f, Find(shaped, PlayRectKind.LeftFlank).AnchorZ, 0.01f, "so do the flanks");
        AssertNear(-350f - 80f, Find(shaped, PlayRectKind.RightFlank).AnchorZ, 0.01f, "...");
        AssertNear(Find(plain, PlayRectKind.LeftFlank).AnchorX, Find(shaped, PlayRectKind.LeftFlank).AnchorX, 0.01f, "laterally the template's layout");
        AssertNear(Find(plain, PlayRectKind.ReserveLine).AnchorZ, Find(shaped, PlayRectKind.ReserveLine).AnchorZ, 0.01f, "the reserve line is the template's");
        AssertTrue(Math.Abs(Find(plain, PlayRectKind.ForwardScreen).AnchorZ - (-350f)) > 5f, "control: the template alone puts the screen elsewhere");
        // Other templates ignore a crest.
        PlayTemplate prepared = PlayTemplateCatalog.Lookup(BattlePlanId.PreparedDefense);
        AssertNear(Find(PlayProjection.Project(prepared, 400f, -700f, 400f, -100f, 600f), PlayRectKind.CenterHold).AnchorZ,
            Find(PlayProjection.Project(prepared, 400f, -700f, 400f, -100f, 600f, in crest), PlayRectKind.CenterHold).AnchorZ, 0.01f, "other plays do not move");
    }

    private static TacticalPlaybookInput LineHoldInput(float highGroundShare)
    {
        var sectors = new[]
        {
            new TacticalPlaybookSectorView(0, TacticalSectorMission.Hold, TacticalSectorPosition.Left, 1000f, 800f, 0.7f, false, false, 0f),
            new TacticalPlaybookSectorView(1, TacticalSectorMission.Hold, TacticalSectorPosition.Center, 1500f, 1200f, 0.7f, false, false, 0f),
        };
        return new TacticalPlaybookInput(CommanderIntent.HoldToLast, -1, sectors, false, false, false, 0f, highGroundShare);
    }

    private static void HighGroundLabelNeedsCrestShare()
    {
        AssertEqual(TacticalPlaybookKind.HighGroundDefense, TacticalPlaybookLedger.Decide(LineHoldInput(0.5f)).Playbook, "half the held groups on crests: HighGroundDefense");
        AssertEqual(TacticalPlaybookKind.HighGroundDefense, TacticalPlaybookLedger.Decide(LineHoldInput(1f)).Playbook, "all of them");
        AssertEqual(TacticalPlaybookKind.LineHold, TacticalPlaybookLedger.Decide(LineHoldInput(0.49f)).Playbook, "under half: LineHold");
        AssertEqual(TacticalPlaybookKind.LineHold, TacticalPlaybookLedger.Decide(LineHoldInput(0f)).Playbook, "no crest: LineHold");
        string patch = FlankResponseTests.SourceText("Patches", "BattleCommanderIntentObserverPatch.cs");
        AssertTrue(patch.Contains("ownOnHighGroundShare"), "the observer patch computes the share from the picture");
    }

    private static void PlaybookTerrainFromPicture()
    {
        TacticalFrontAxis axis = TacticalFrontAxis.FromCentroids(400f, -700f, 400f, -100f).WithOwnExtent(-300f, 300f);
        AssertEqual(TerrainKind.Open, ArmyEvidenceBuilder.ClassifyFromPicture(null, axis), "no picture: Open (today's constant)");
        BattleTerrainGrid grass = Grid(Map, (x, z) => Grass);
        AssertEqual(TerrainKind.Open, ArmyEvidenceBuilder.ClassifyFromPicture(Build(grass, Map, Flat()), axis), "open ground");
        BattleTerrainGrid river = Grid(Map, (x, z) => (z - Map >= -560 && z - Map < -440) ? River : Grass);
        AssertEqual(TerrainKind.River, ArmyEvidenceBuilder.ClassifyFromPicture(Build(river, Map, Flat()), axis), "a river across the front");
        BattleTerrainGrid forest = Grid(Map, (x, z) => 14);
        AssertEqual(TerrainKind.Wooded, ArmyEvidenceBuilder.ClassifyFromPicture(Build(forest, Map, Flat()), axis), "woods");
        AssertEqual(TerrainKind.Mountain, ArmyEvidenceBuilder.ClassifyFromPicture(Build(grass, Map, (x, z) => (z + Map) * 0.2f), axis), "relief over the front");
        string builder = FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyEvidenceBuilder.cs");
        AssertFalse(builder.Contains("TerrainSamplesLateral"), "the 7 x 5 sampler is gone");
        AssertFalse(builder.Contains("GetCurrentTerrainOnPos(point)"), "no per-sample vanilla terrain read");
    }

    // The acceptance pin: every TacticalObjectiveType a planner branch reads has a producer.
    private static void EveryObjectiveTypeAPlannerReadsHasAProducer()
    {
        string classifier = FlankResponseTests.SourceText("Tactical", "Operations", "TacticalTerrainObjectiveClassifier.cs");
        foreach (string type in new[] { "Bridge", "Ford", "Town", "RoadJunction", "Ridge" })
            AssertTrue(classifier.Contains("TacticalObjectiveType." + type), type + " is produced by the picture's classifier");
        string anchors = FlankResponseTests.SourceText("Tactical", "Operations", "TacticalDefensiveLineAnchorPlanner.cs");
        AssertTrue(anchors.Contains("TacticalObjectiveType.ChokePoint"), "ChokePoint keeps its corridor-anchor producer");
        string adapter = FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalVisionRuntimeAdapter.cs");
        foreach (string type in new[] { "VictoryPoint", "UnknownVanillaObjective", "EnemyLine", "StagingArea" })
            AssertTrue(adapter.Contains("TacticalObjectiveType." + type), type + " is produced by the vision adapter");
        string contracts = FlankResponseTests.SourceText("Tactical", "Operations", "TacticalObjectiveContracts.cs");
        foreach (TacticalObjectiveType t in Enum.GetValues(typeof(TacticalObjectiveType)))
        {
            if (t == TacticalObjectiveType.FallbackLine) continue;       // read by no planner branch
            AssertTrue(contracts.Contains(t.ToString()), t + " exists");
        }
    }
}
