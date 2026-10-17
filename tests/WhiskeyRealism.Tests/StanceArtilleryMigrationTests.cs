using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// BC5b: #45 group stance, charge demotion, artillery (B7, applier, veto) and the
// #42 feud replay migrated onto the order arbiter / executor
// (docs/superpowers/plans/2026-10-15-battle-core.md § BC5b).
internal static class StanceArtilleryMigrationTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("battle-core GroupStanceProducerScorerPriority", GroupStanceProducerScorerPriority),
            ("battle-core GroupStanceLeavesVanillaAssaultAndUnsettledGroups", GroupStanceLeavesVanillaAssaultAndUnsettledGroups),
            ("battle-core ChargeDemotionIsSafetyStance", ChargeDemotionIsSafetyStance),
            ("battle-core ChargeDemotionBeatsExecutorAssault", ChargeDemotionBeatsExecutorAssault),
            ("battle-core HeldStanceRefreshReachesTheWriter", HeldStanceRefreshReachesTheWriter),
            ("battle-core FeudReplayNeverEntersTheArbiter", FeudReplayNeverEntersTheArbiter),
            ("battle-core FeudReplayUsesInlineExecutor", FeudReplayUsesInlineExecutor),
            ("battle-core FeudReplayRefusedForPlayerOwned", FeudReplayRefusedForPlayerOwned),
            ("battle-core FeudReplayFailsClosedWithoutSnapshot", FeudReplayFailsClosedWithoutSnapshot),
            ("battle-core MacroUsesSnapshotSlices", MacroUsesSnapshotSlices),
            ("battle-core MacroEnemiesInRangeFromSnapshot", MacroEnemiesInRangeFromSnapshot),
            ("battle-core MacroFlankRiskFromPerceivedFlankPressure", MacroFlankRiskFromPerceivedFlankPressure),
            ("battle-core ArtilleryCeaseBombardIsCombatBehavior7WithBombardReset", ArtilleryCeaseBombardIsCombatBehavior7WithBombardReset),
            ("battle-core CounterBatteryReleasedWhenTargetGone", CounterBatteryReleasedWhenTargetGone),
            ("battle-core ArtilleryLimberUnlimberFaceAreFormationAndFacing", ArtilleryLimberUnlimberFaceAreFormationAndFacing),
            ("battle-core ArtilleryRetreatTargetValidatedNotStraightLine180m", ArtilleryRetreatTargetValidatedNotStraightLine180m),
            ("battle-core PointBlankVanillaMoveTriggersSafetyRetreat", PointBlankVanillaMoveTriggersSafetyRetreat),
            ("battle-core ArtilleryNeverStanceWrites", ArtilleryNeverStanceWrites),
            ("battle-core ArtilleryWheelFacesTheWinningQuadrant", ArtilleryWheelFacesTheWinningQuadrant),
            ("battle-core ArtilleryWheelFacesTheStrongestInTheQuadrant", ArtilleryWheelFacesTheStrongestInTheQuadrant),
            ("battle-core ArtilleryFallbackWheelHonoursVanillaReach", ArtilleryFallbackWheelHonoursVanillaReach),
            ("battle-core InterposedSupportCountsOnlyFriendsBetweenBatteryAndEnemy", InterposedSupportCountsOnlyFriendsBetweenBatteryAndEnemy),
            ("battle-core ArtilleryIgnoresFeudPlayerAndHeldBatteries", ArtilleryIgnoresFeudPlayerAndHeldBatteries),
            ("bc14w BOMBARD-START-INTENT a battery vanilla's start test would admit is given the bombard-start intent for the preparation point, and no other", BombardStartIntent),
            ("bc14w FOCAL-PREP-ARTILLERY a battery whose group carries a preparation point keeps bombarding through the doctrine's cancel and counter-battery, except the safety rules", PreparationExemptsBombard),
            ("bc14w BOMBARD-START the two field writes are executor-only behind the gate, in vanilla's order, and the producer notes the table", BombardStartWriter),
        });
    }

    private const int Brigade = TacticalUnitType.BattleGroupBrigade;

    // ------------------------------------------------------------------ #45 group stance

    private static void GroupStanceProducerScorerPriority()
    {
        UnitRecord group = Group(11, Brigade, aiStanceOrdered: 3);
        AssertTrue(BattleWriterPolicy.TryGroupStanceIntent(group, Apply(2, fromLedger: true), out BattleOrderIntent intent), "ledger stance produces an intent");
        AssertEqual(11, intent.GroupId, "group id is the snapshot instance id");
        AssertEqual(OrderChannel.Stance, intent.Channel, "stance channel");
        AssertEqual(2, intent.Value, "resolved final stance");
        AssertEqual(IntentPriority.Scorer, intent.Priority, "the #45 scorer is the lowest rung");
        AssertEqual(IntentSource.GroupStanceScorer, intent.Source, "source");
        AssertEqual(-1f, intent.Heading, "no heading");
        AssertEqual("group-stance-ledger", intent.Reason, "static reason by origin");

        AssertTrue(BattleWriterPolicy.TryGroupStanceIntent(group, Apply(3, fromDoctrine: true), out intent), "doctrine stance");
        AssertEqual("group-stance-doctrine", intent.Reason, "doctrine reason");
        AssertTrue(BattleWriterPolicy.TryGroupStanceIntent(group, Apply(1), out intent), "scorer stance");
        AssertEqual("group-stance-scorer", intent.Reason, "scorer reason");

        AssertFalse(BattleWriterPolicy.TryGroupStanceIntent(group, Skip(), out _), "Skip leaves vanilla alone");
        AssertFalse(BattleWriterPolicy.TryGroupStanceIntent(group, Apply(4), out _), "never writes assault");
        AssertFalse(BattleWriterPolicy.TryGroupStanceIntent(group, Apply(0), out _), "never writes stance 0 (stance-off re-attach)");
        AssertFalse(BattleWriterPolicy.TryGroupStanceIntent(Group(12, TacticalUnitType.BattleGroupDivision, 3), Apply(2), out _), "brigades only");

        var arbiter = new OrderArbiter();
        arbiter.BeginTick();
        BattleWriterPolicy.TryGroupStanceIntent(group, Apply(2), out intent);
        arbiter.Submit(intent);
        arbiter.Submit(new BattleOrderIntent { GroupId = 11, Channel = OrderChannel.Stance, Value = 3, Heading = -1f, Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Reason = "test" });
        var winners = new BattleOrderIntent[4];
        AssertEqual(1, arbiter.Resolve(winners), "one stance winner");
        AssertEqual(IntentSource.PostureExecutor, winners[0].Source, "the posture executor outranks the scorer");
    }

    private static void GroupStanceLeavesVanillaAssaultAndUnsettledGroups()
    {
        AssertFalse(BattleWriterPolicy.TryGroupStanceIntent(Group(11, Brigade, 4), Apply(3, fromDoctrine: true), out _),
            "a vanilla assault is only ever demoted by the charge-demotion producer");
        UnitRecord marching = Group(11, Brigade, 2);
        marching.RegimentPaths = 2;
        AssertFalse(BattleWriterPolicy.TryGroupStanceIntent(marching, Apply(3), out _), "a marching group is not settled");
        UnitRecord queued = Group(11, Brigade, 2);
        queued.QueuedOrders = 1;
        AssertFalse(BattleWriterPolicy.TryGroupStanceIntent(queued, Apply(3), out _), "a courier order in flight is not settled");
        AssertTrue(BattleWriterPolicy.TryGroupStanceIntent(marching, Apply(2), out _), "agreement on a marching group still refreshes the hold");
        UnitRecord owned = Group(11, Brigade, 2);
        owned.Flags |= UnitFlags.PlayerOwned;
        AssertFalse(BattleWriterPolicy.TryGroupStanceIntent(owned, Apply(3), out _), "player-owned group");
    }

    // ------------------------------------------------------------------ charge demotion

    private static void ChargeDemotionIsSafetyStance()
    {
        UnitRecord assault = Group(21, Brigade, aiStanceOrdered: 4);
        AssertTrue(TacticalChargeDenial.TryDemotionIntent(assault, Demote(2), out BattleOrderIntent intent), "doctrine deny demotes the assault");
        AssertEqual(21, intent.GroupId, "group id");
        AssertEqual(OrderChannel.Stance, intent.Channel, "stance channel");
        AssertEqual(IntentPriority.Safety, intent.Priority, "safety rung");
        AssertEqual(IntentSource.ChargeDemotion, intent.Source, "source");
        AssertEqual(2, intent.Value, "demotes to the cached stance");
        AssertEqual("charge-demotion", intent.Reason, "static reason");

        AssertTrue(TacticalChargeDenial.TryDemotionIntent(assault, Demote(-1), out intent), "invalid cached stance");
        AssertEqual(TacticalChargeDenial.LegacyDemoteStance, intent.Value, "falls back to the legacy demotion stance");

        var preserve = new TacticalResolvedGroupDecision(3, TacticalDoctrineDecisionKind.Apply, "doctrine-attack", true, false, true, ChargeOverrideAction.PreserveVanilla);
        AssertFalse(TacticalChargeDenial.TryDemotionIntent(assault, preserve, out _), "a preserved assault is left alone");
        AssertFalse(TacticalChargeDenial.TryDemotionIntent(Group(21, Brigade, 3), Demote(2), out _), "nothing to demote below assault");
        UnitRecord owned = assault;
        owned.Flags |= UnitFlags.PlayerOwned;
        AssertFalse(TacticalChargeDenial.TryDemotionIntent(owned, Demote(2), out _), "player-owned group");
        UnitRecord marching = assault;
        marching.RegimentPaths = 3;
        AssertTrue(TacticalChargeDenial.TryDemotionIntent(marching, Demote(2), out _), "a Safety demotion is not held back by a march");
    }

    private static void ChargeDemotionBeatsExecutorAssault()
    {
        var arbiter = new OrderArbiter();
        arbiter.BeginTick();
        arbiter.Submit(new BattleOrderIntent { GroupId = 21, Channel = OrderChannel.Stance, Value = 4, Heading = -1f, Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Reason = "test" });
        TacticalChargeDenial.TryDemotionIntent(Group(21, Brigade, 4), Demote(2), out BattleOrderIntent demotion);
        arbiter.Submit(demotion);
        arbiter.Submit(new BattleOrderIntent { GroupId = 21, Channel = OrderChannel.Move, TargetX = 10f, TargetZ = -10f, Heading = -1f, Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Reason = "test" });
        var winners = new BattleOrderIntent[4];
        AssertEqual(2, arbiter.Resolve(winners), "stance and move resolve independently");
        AssertEqual(OrderChannel.Move, winners[0].Channel, "move first");
        AssertEqual(OrderChannel.Stance, winners[1].Channel, "stance after the move");
        AssertEqual(IntentSource.ChargeDemotion, winners[1].Source, "the demotion outranks the executor's assault");
        AssertEqual(2, winners[1].Value, "demoted stance");
        AssertTrue(arbiter.TryGetClaim(21, OrderChannel.Stance, out BattleOrderIntent claim) && claim.Priority == IntentPriority.Safety,
            "the published claim is the Safety demotion");
    }

    // ------------------------------------------------------------------ artillery

    private const int Battery = 41;
    private const int ParentBrigade = 40;

    private static void ArtilleryCeaseBombardIsCombatBehavior7WithBombardReset()
    {
        // CheckAIBombardment 3904: cancelling bombardment is behavior 7 plus bombardstarttime = 0.
        BattleSnapshot s = ArtillerySnapshot(cbo: 9, mounted: false, enemyDistance: 600f);
        BattleArtilleryState b = State(ammo: 0.05f);
        var buffer = new BattleOrderIntent[4];
        int n = BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, b, Rules(), buffer);
        AssertEqual(1, n, "one intent");
        AssertEqual(OrderChannel.CombatBehavior, buffer[0].Channel, "combat behavior channel");
        AssertEqual(7, buffer[0].Value, "cease bombardment = fire at will (7)");
        AssertEqual(IntentSource.ArtilleryDoctrine, buffer[0].Source, "source");
        AssertEqual(Battery, buffer[0].GroupId, "the battery itself");
        AssertTrue(BattleWriterPolicy.ResetsBombardStart(buffer[0]), "the executor resets bombardstarttime for it");

        BattleOrderIntent other = buffer[0];
        other.Value = 8;
        AssertFalse(BattleWriterPolicy.ResetsBombardStart(other), "counter-battery keeps the bombard clock");
        other.Value = 7;
        other.Source = IntentSource.FireControl;
        AssertFalse(BattleWriterPolicy.ResetsBombardStart(other), "only the artillery doctrine's 7");

        // Doctrine Deny (friendly danger close) cancels too; Allow keeps the bombardment.
        b = State(ammo: 0.9f, doctrine: new DoctrineArtilleryDecision(DoctrineConsumerAction.Deny, "friendly-close-range"));
        AssertEqual(1, BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, b, Rules(), buffer), "deny cancels");
        AssertEqual(7, buffer[0].Value, "deny cancels to 7");
        AssertEqual(0, BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(ammo: 0.9f), Rules(), buffer), "a supported, supplied bombardment is left to vanilla");
    }

    private static void CounterBatteryReleasedWhenTargetGone()
    {
        // CheckCounterBatteryFire skips units already at 8 (3834), so vanilla never releases it.
        var buffer = new BattleOrderIntent[4];
        BattleSnapshot s = ArtillerySnapshot(cbo: 8, mounted: false, enemyDistance: 600f, enemyUnitTyp: TacticalUnitType.Infantry);
        AssertEqual(1, BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(), Rules(), buffer), "no enemy battery in range");
        AssertEqual(7, buffer[0].Value, "released to 7");
        AssertEqual(OrderChannel.CombatBehavior, buffer[0].Channel, "combat behavior");

        s = ArtillerySnapshot(cbo: 8, mounted: false, enemyDistance: 600f, enemyUnitTyp: TacticalUnitType.Artillery);
        AssertEqual(0, BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(), Rules(), buffer), "visible enemy battery in fire range keeps 8");

        s = ArtillerySnapshot(cbo: 8, mounted: false, enemyDistance: 600f, enemyUnitTyp: TacticalUnitType.Artillery, enemyState: ObservationState.Remembered);
        AssertEqual(1, BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(), Rules(), buffer), "a remembered battery is not a target in range");

        s = ArtillerySnapshot(cbo: 8, mounted: false, enemyDistance: 1500f, enemyUnitTyp: TacticalUnitType.Artillery);
        AssertEqual(1, BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(), Rules(), buffer), "an enemy battery beyond fire range");

        BattleArtilleryRules off = Rules();
        off.DoctrineOn = false;
        AssertEqual(0, BattleWriterPolicy.ArtilleryDoctrineIntents(ArtillerySnapshot(cbo: 8, mounted: false, enemyDistance: 600f), 1, State(), off, buffer),
            "Enable Tactical Artillery Doctrine off: no release");
    }

    private static void ArtilleryLimberUnlimberFaceAreFormationAndFacing()
    {
        var buffer = new BattleOrderIntent[4];
        // Limbered, idle, enemy in fire range: unlimber (formation 0, MountUnits 0) and face it.
        BattleSnapshot s = ArtillerySnapshot(cbo: 7, mounted: true, enemyDistance: 600f);
        int n = BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(mounted: true), Rules(), buffer);
        AssertEqual(2, n, "unlimber + face");
        AssertEqual(OrderChannel.Formation, buffer[0].Channel, "formation channel");
        AssertEqual(0, buffer[0].Value, "unlimbered artillery formation (ChangeRegimentFormation 135450)");
        AssertTrue((buffer[0].Flags & IntentFlags.Unlimber) != 0, "Unlimber flag");
        AssertEqual(OrderChannel.Facing, buffer[1].Channel, "facing channel");
        AssertNear(180f, buffer[1].Heading, 0.01f, "faces the enemy due south (Unity yaw, GetAngle + 180)");

        // Unlimbered, enemy at 50 m: point-blank limber (formation 3, MountUnits 1).
        s = ArtillerySnapshot(cbo: 7, mounted: false, enemyDistance: 50f);
        n = BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(), Rules(), buffer);
        AssertEqual(1, n, "limber only");
        AssertEqual(OrderChannel.Formation, buffer[0].Channel, "formation");
        AssertEqual(3, buffer[0].Value, "limbered artillery formation");
        AssertTrue((buffer[0].Flags & IntentFlags.Limber) != 0, "Limber flag");
        AssertEqual(IntentPriority.Safety, buffer[0].Priority, "limbering is the first half of the escape: Safety, past the recent-order cooldown");
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), null);
        BattleOrderIntent earlier = buffer[0];
        earlier.Value = 0;
        earlier.Priority = IntentPriority.Executor;
        executor.RecordIssued(earlier, 1.0, null, 0, null, 0, 0, 0);
        AssertTrue(executor.Plan(s, buffer[0], 1.01).Execute, "a battery unlimbered a moment ago can still limber");

        // Counter-battery release, unlimber and face in one tick: all three fit.
        s = ArtillerySnapshot(cbo: 8, mounted: true, enemyDistance: 600f);
        var three = new BattleOrderIntent[3];
        AssertEqual(3, BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(mounted: true), Rules(), three), "release + unlimber + face");

        // Unlimbered, enemy only behind (out of the front quadrant, in range): wheel.
        s = ArtillerySnapshot(cbo: 7, mounted: false, enemyDistance: 600f, facing: 0f);
        n = BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(), Rules(), buffer);
        AssertEqual(1, n, "face only");
        AssertEqual(OrderChannel.Facing, buffer[0].Channel, "wheel toward the threat");
        AssertNear(180f, buffer[0].Heading, 0.01f, "heading");

        // Already facing it: nothing (vanilla's turn trigger, 4599-4606).
        s = ArtillerySnapshot(cbo: 7, mounted: false, enemyDistance: 600f, facing: 178f);
        AssertEqual(0, BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(), Rules(), buffer), "inside the turn trigger");

        BattleArtilleryRules off = Rules();
        off.AuditOn = false;
        s = ArtillerySnapshot(cbo: 7, mounted: true, enemyDistance: 600f);
        AssertEqual(0, BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(mounted: true), off, buffer), "Enable Artillery All-Stance Audit off: no micro writes");
    }

    private static void ArtilleryRetreatTargetValidatedNotStraightLine180m()
    {
        var buffer = new BattleOrderIntent[4];
        // Limbered battery at (500, -500), enemy 40 m due south.
        BattleSnapshot s = ArtillerySnapshot(cbo: 7, mounted: true, enemyDistance: 40f);
        int n = BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(mounted: true), Rules(), buffer);
        AssertEqual(1, n, "one retreat");
        BattleOrderIntent retreat = buffer[0];
        AssertEqual(OrderChannel.Move, retreat.Channel, "a Move through the executor, not RegimentSetPath");
        AssertEqual(IntentPriority.Safety, retreat.Priority, "Safety");
        AssertEqual(IntentSource.ArtilleryDoctrine, retreat.Source, "source");
        AssertTrue((retreat.Flags & IntentFlags.InContactWithdrawal) != 0, "immediate, line-checked withdrawal");
        AssertTrue((retreat.Flags & IntentFlags.Limber) != 0, "limbers before moving");
        AssertTrue((retreat.Flags & IntentFlags.UseOrderDelay) == 0, "no courier");
        AssertNear(500f, retreat.TargetX, 0.01f, "straight away from the visible enemy");
        AssertNear(-500f + 180f, retreat.TargetZ, 0.01f, "by the fallback distance");
        AssertNear(180f, retreat.Heading, 0.01f, "ends facing the enemy");

        // The executor validates it: water on the raw 180 m point steps it back.
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), new WaterNorthOf(-500f + 150f));
        executor.SetMapSize(2000f, 2000f);
        ExecutionPlan plan = executor.Plan(s, retreat, 1.0);
        AssertTrue(plan.Execute, "a dry point remains");
        AssertTrue(plan.Z < retreat.TargetZ - 1f, "stepped back off the water");
        AssertTrue(plan.Z <= -500f + 150f, "the validated target is dry");
        AssertTrue(plan.UseOrderDelay == false, "immediate");
    }

    private static void PointBlankVanillaMoveTriggersSafetyRetreat()
    {
        // A battery vanilla sent (regimentpaths > 0) to within 60 m of a visible enemy.
        BattleSnapshot s = ArtillerySnapshot(cbo: 7, mounted: true, enemyDistance: 400f, paths: 1, waypointToEnemy: 30f);
        AssertTrue(BattleWriterPolicy.TryArtilleryPointBlankIntent(s, 1, 180f, out BattleOrderIntent intent), "watchdog fires");
        AssertEqual(IntentSource.ArtilleryPointBlank, intent.Source, "source");
        AssertEqual(IntentPriority.Safety, intent.Priority, "Safety");
        AssertEqual(OrderChannel.Move, intent.Channel, "move");
        AssertTrue((intent.Flags & IntentFlags.InContactWithdrawal) != 0, "immediate withdrawal");
        AssertTrue(intent.TargetZ > -500f, "away from the enemy (north)");

        AssertFalse(BattleWriterPolicy.TryArtilleryPointBlankIntent(
            ArtillerySnapshot(cbo: 7, mounted: true, enemyDistance: 400f, paths: 1, waypointToEnemy: 90f), 1, 180f, out _), "waypoint outside 60 m");
        AssertFalse(BattleWriterPolicy.TryArtilleryPointBlankIntent(
            ArtillerySnapshot(cbo: 7, mounted: true, enemyDistance: 400f, paths: 0, waypointToEnemy: 30f), 1, 180f, out _), "not moving");
        AssertFalse(BattleWriterPolicy.TryArtilleryPointBlankIntent(
            ArtillerySnapshot(cbo: 7, mounted: true, enemyDistance: 400f, paths: 1, waypointToEnemy: 30f, enemyState: ObservationState.Remembered), 1, 180f, out _),
            "a remembered enemy is not a point-blank threat");
    }

    private static void ArtilleryNeverStanceWrites()
    {
        // ChangeStance on a battery sets permanentlydetached (90788-90791) and vanilla
        // artillery AI skips it for good (3789, 3834, 3890, 3928).
        var buffer = new BattleOrderIntent[4];
        float[] distances = { 30f, 50f, 90f, 400f, 600f, 1500f };
        int[] behaviors = { 7, 8, 9 };
        foreach (float d in distances)
            foreach (int cbo in behaviors)
                for (int m = 0; m < 2; m++)
                {
                    BattleSnapshot s = ArtillerySnapshot(cbo, m == 1, d, paths: 0, enemyUnitTyp: TacticalUnitType.Artillery);
                    int n = BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(ammo: 0.05f, mounted: m == 1), Rules(), buffer);
                    for (int i = 0; i < n; i++)
                    {
                        AssertTrue(buffer[i].Channel != OrderChannel.Stance, "no stance intent");
                        AssertEqual(Battery, buffer[i].GroupId, "battery-level intents only");
                    }
                    if (BattleWriterPolicy.TryArtilleryPointBlankIntent(s, 1, 180f, out BattleOrderIntent pb))
                        AssertTrue(pb.Channel != OrderChannel.Stance, "watchdog no stance");
                }
    }

    // The wheel is chosen by quadrant strength (best > current + 1000), so it faces
    // the winning quadrant: its strongest visible enemy in fire range, the same
    // "turn toward one enemy unit's position" convention vanilla's micro facing
    // uses (GetAngleTerrain(own, enemy) + 180, 4603). Never the closest enemy,
    // which is in front exactly when the wheel fires.
    private static void ArtilleryWheelFacesTheWinningQuadrant()
    {
        var buffer = new BattleOrderIntent[4];
        // Battery at (500, -500) facing south (180): 100 men 600 m south, 5000 men 700 m north.
        BattleSnapshot s = TwoEnemyBatterySnapshot(ObservationState.Visible);
        BattleWriterPolicy.QuadrantThreats(s, 500f, -500f, 180f, 1000f, out float current, out float best, out float bestX, out float bestZ);
        AssertEqual(100f, current, "front quadrant: the small enemy");
        AssertEqual(5000f, best, "rear quadrant wins");
        AssertNear(-500f + 700f, bestZ, 0.01f, "the strongest enemy of the winning quadrant");
        int n = BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(), Rules(), buffer);
        AssertEqual(1, n, "one wheel");
        AssertEqual(OrderChannel.Facing, buffer[0].Channel, "facing");
        AssertNear(0f, buffer[0].Heading, 0.01f, "wheels to the rear threat (north), not the closest enemy in front");

        // The rear enemy only remembered: no quadrant threat behind, no wheel.
        s = TwoEnemyBatterySnapshot(ObservationState.Remembered);
        BattleWriterPolicy.QuadrantThreats(s, 500f, -500f, 180f, 1000f, out current, out best, out _, out _);
        AssertEqual(100f, best, "a remembered enemy is not a threat in fire range");
        AssertEqual(0, BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(), Rules(), buffer), "no wheel toward a remembered enemy");
    }

    // Two enemies in the winning (rear) quadrant: the wheel faces the stronger one,
    // whichever order the observations come in.
    private static void ArtilleryWheelFacesTheStrongestInTheQuadrant()
    {
        var buffer = new BattleOrderIntent[4];
        for (int order = 0; order < 2; order++)
        {
            BattleSnapshot s = ArtillerySnapshot(cbo: 7, mounted: false, enemyDistance: 600f);
            s.Observations[0].Strength = 100f;
            var strong = new EnemyObservation { InstanceId = 903, UnitTyp = TacticalUnitType.Infantry, X = 800f, Z = 150f, Strength = 3000f, State = ObservationState.Visible, Confidence01 = 1f };
            var weak = new EnemyObservation { InstanceId = 904, UnitTyp = TacticalUnitType.Infantry, X = 300f, Z = 100f, Strength = 2500f, State = ObservationState.Visible, Confidence01 = 1f };
            s.AddObservation(order == 0 ? strong : weak);
            s.AddObservation(order == 0 ? weak : strong);
            SlicePressureBuilder.Build(s, new SliceGeometry { MaxRangeMeters = 250f, FlankShiftMeters = 150f });
            BattleWriterPolicy.QuadrantThreats(s, 500f, -500f, 180f, 1000f, out _, out float best, out float bx, out float bz);
            AssertEqual(5500f, best, "the quadrant sums both");
            AssertNear(800f, bx, 0.01f, "target is the stronger enemy (order " + order + ")");
            AssertEqual(1, BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(), Rules(), buffer), "one wheel");
            AssertNear(BattleWriterPolicy.HeadingTo(500f, -500f, 800f, 150f), buffer[0].Heading, 0.01f, "faces the stronger enemy (order " + order + ")");
        }
    }

    // With nothing in fire range the wheel falls back to the closest visible enemy,
    // inside vanilla's micro-facing bound closestDistance < firerange * 1.5 (4606).
    private static void ArtilleryFallbackWheelHonoursVanillaReach()
    {
        var buffer = new BattleOrderIntent[4];
        BattleSnapshot s = ArtillerySnapshot(cbo: 7, mounted: false, enemyDistance: 1200f, facing: 0f);
        AssertEqual(1, BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(), Rules(), buffer), "1.2 x fire range: turns toward it");
        AssertEqual(OrderChannel.Facing, buffer[0].Channel, "facing");
        AssertNear(180f, buffer[0].Heading, 0.01f, "toward the closest visible enemy");
        s = ArtillerySnapshot(cbo: 7, mounted: false, enemyDistance: 1600f, facing: 0f);
        AssertEqual(0, BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(), Rules(), buffer), "1.6 x fire range: beyond vanilla's turn reach");
    }

    private static BattleSnapshot TwoEnemyBatterySnapshot(ObservationState rearState)
    {
        BattleSnapshot s = ArtillerySnapshot(cbo: 7, mounted: false, enemyDistance: 600f);
        s.Observations[0].Strength = 100f;
        s.AddObservation(new EnemyObservation { InstanceId = 902, UnitTyp = TacticalUnitType.Infantry, X = 500f, Z = -500f + 700f, Strength = 5000f, State = rearState, Confidence01 = 1f });
        SlicePressureBuilder.Build(s, new SliceGeometry { MaxRangeMeters = 250f, FlankShiftMeters = 150f });
        return s;
    }

    // Port of the audit's CountFriendlyInfantryCavInterposed: a friend shields the
    // battery only on the enemy side of it, short of the enemy, within one frontage.
    private static void InterposedSupportCountsOnlyFriendsBetweenBatteryAndEnemy()
    {
        // Battery at (500, -500), enemy 600 m due south (z = -1100).
        AssertEqual(1, Support(500f, -700f), "a friend between the battery and the enemy");
        AssertEqual(0, Support(500f, -450f), "a friend behind the battery does not shield it");
        AssertEqual(0, Support(500f, -1200f), "a friend past the enemy");
        AssertEqual(0, Support(700f, -700f), "a friend 200 m off the line");
        AssertEqual(1, Support(590f, -700f), "within one frontage of the line");
    }

    private static int Support(float fx, float fz)
    {
        BattleSnapshot s = ArtillerySnapshot(cbo: 7, mounted: false, enemyDistance: 600f);
        var snapshot = new BattleSnapshot();
        snapshot.BeginBuild(1, 0, 0, 1, 1.0);
        snapshot.AddOwn(s.Own[0]);
        snapshot.AddOwn(s.Own[1]);
        snapshot.AddOwn(new UnitRecord { InstanceId = 81, UnitTyp = TacticalUnitType.Infantry, X = fx, Z = fz, ParentIndex = -1, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry });
        snapshot.IndexCommandTree(null);
        return BattleWriterPolicy.CountInterposedSupport(snapshot, 1, true, 500f, -1100f, 600f);
    }

    private static void ArtilleryIgnoresFeudPlayerAndHeldBatteries()
    {
        var buffer = new BattleOrderIntent[4];
        BattleSnapshot s = ArtillerySnapshot(cbo: 9, mounted: false, enemyDistance: 600f, parentFlags: UnitFlags.Feud);
        AssertFalse(BattleWriterPolicy.ArtilleryBatteryEligible(s, 1), "vanilla's feud envelope is the group's (3890)");
        s = ArtillerySnapshot(cbo: 9, mounted: false, enemyDistance: 600f, batteryFlags: UnitFlags.PlayerOwned);
        AssertFalse(BattleWriterPolicy.ArtilleryBatteryEligible(s, 1), "player-owned battery");
        s = ArtillerySnapshot(cbo: 9, mounted: false, enemyDistance: 600f, batteryFlags: UnitFlags.PermanentlyDetached);
        AssertFalse(BattleWriterPolicy.ArtilleryBatteryEligible(s, 1), "detached battery (vanilla artillery AI skips it)");
        s = ArtillerySnapshot(cbo: 9, mounted: false, enemyDistance: 600f);
        AssertTrue(BattleWriterPolicy.ArtilleryBatteryEligible(s, 1), "AI battery");
        AssertFalse(BattleWriterPolicy.ArtilleryBatteryEligible(s, 0), "a group is not a battery");

        BattleArtilleryState held = State(ammo: 0.05f);
        held.InHold = true;
        AssertEqual(0, BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, held, Rules(), buffer), "per-battery hold on the doctrine clock");
        AssertTrue(BattleWriterPolicy.ArtilleryHoldActive(1.0, 0.999, 0.005), "inside the hold");
        AssertFalse(BattleWriterPolicy.ArtilleryHoldActive(1.0, 0.990, 0.005), "hold expired");
        AssertFalse(BattleWriterPolicy.ArtilleryHoldActive(1.0, 1.5, 0.005), "a stamp from the future (reload) has expired");
    }

    // Brigade at index 0, battery at index 1 (500, -500) facing `facing`; one enemy
    // `enemyDistance` due south. The battery's last waypoint sits `waypointToEnemy`
    // north of that enemy when given.
    private static BattleSnapshot ArtillerySnapshot(int cbo, bool mounted, float enemyDistance, int paths = 0, float waypointToEnemy = -1f,
        int enemyUnitTyp = TacticalUnitType.Infantry, ObservationState enemyState = ObservationState.Visible, float facing = 180f,
        UnitFlags parentFlags = UnitFlags.None, UnitFlags batteryFlags = UnitFlags.None)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.SetSliceCount(20);
        var group = Group(ParentBrigade, Brigade, 3);
        group.X = 500f;
        group.Z = -450f;
        group.Flags |= parentFlags;
        s.AddOwn(group);
        float ez = -500f - enemyDistance;
        var battery = new UnitRecord
        {
            InstanceId = Battery, UnitTyp = TacticalUnitType.Artillery, X = 500f, Z = -500f, RotationY = facing,
            CombatBehaviorOrdered = cbo, Formation = mounted ? 3 : 0, RegimentPaths = paths, Strength = 100,
            LastWaypointX = 500f, LastWaypointZ = waypointToEnemy >= 0f ? ez + waypointToEnemy : 0f, ParentIndex = 0,
            Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsArtillery | batteryFlags,
        };
        s.AddOwn(battery);
        s.Own[1].ParentIndex = 0;
        s.IndexCommandTree(null);
        s.AddObservation(new EnemyObservation { InstanceId = 900, UnitTyp = enemyUnitTyp, X = 500f, Z = ez, Strength = 800f, State = enemyState, Confidence01 = 1f });
        SlicePressureBuilder.Build(s, new SliceGeometry { MaxRangeMeters = 250f, FlankShiftMeters = 150f });
        return s;
    }

    private static BattleArtilleryState State(float ammo = 0.9f, bool mounted = false, DoctrineArtilleryDecision doctrine = default(DoctrineArtilleryDecision))
    {
        return new BattleArtilleryState
        {
            Mounted = mounted ? 1 : 0, Guns = 4, FireRange = 1000f, AmmoRatio01 = ammo, CanisterAmmo = 1f, GroupMorale = 0.7f,
            BattleStartMorale = 0.8f, FallbackMoraleThreshold = 0.4f, Doctrine = doctrine,
        };
    }

    private static BattleArtilleryRules Rules()
    {
        return new BattleArtilleryRules
        {
            DoctrineOn = true, AuditOn = true, FallbackDistance = 180f, TurnTriggerDegrees = 20f, MaxCoverValueForRotation = 0.5f, AdditionalAngleInCover = 30f,
        };
    }

    // ------------------------------------------------------------------ BC14W: the bombard start

    private static BattleArtilleryState PrepState(float ammoMean = 0.9f, float ammo = 0.9f, float px = 500f, float pz = -1100f, int guns = 4, float fireRange = 1000f,
        DoctrineArtilleryDecision doctrine = default(DoctrineArtilleryDecision))
    {
        BattleArtilleryState b = State(ammo, false, doctrine);
        b.Guns = guns;
        b.FireRange = fireRange;
        b.HasPreparation = true;
        b.PreparationX = px;
        b.PreparationZ = pz;
        b.AmmoMean = ammoMean;
        return b;
    }

    private static void BombardStartIntent()
    {
        BattleSnapshot s = ArtillerySnapshot(cbo: 7, mounted: false, enemyDistance: 600f);
        AssertTrue(BattleWriterPolicy.TryBombardStartIntent(s, 1, PrepState(), out BattleOrderIntent start), "admitted");
        AssertEqual(OrderChannel.CombatBehavior, start.Channel, "on the combat behavior channel");
        AssertEqual(9, start.Value, "behavior 9, bombardment");
        AssertTrue(start.TargetX == 500f && start.TargetZ == -1100f, "carrying the preparation point");
        AssertTrue((start.Flags & IntentFlags.BombardStart) != 0, "flagged BombardStart");
        AssertEqual(IntentSource.ArtilleryDoctrine, start.Source, "the artillery doctrine's");
        AssertEqual(IntentPriority.Executor, start.Priority, "at executor priority");
        AssertEqual(Battery, start.GroupId, "of the battery itself");

        BattleOrderIntent none;
        AssertFalse(BattleWriterPolicy.TryBombardStartIntent(s, 1, State(), out none), "a battery whose group carries no preparation point");
        AssertFalse(BattleWriterPolicy.TryBombardStartIntent(s, 1, PrepState(pz: -1600f), out none), "a point beyond the fire range (1100 m of 1000)");
        AssertFalse(BattleWriterPolicy.TryBombardStartIntent(s, 1, PrepState(guns: 0), out none), "no guns");
        AssertFalse(BattleWriterPolicy.TryBombardStartIntent(s, 1, PrepState(ammoMean: 0.2f), out none), "mean ammo at 0.2 is not above it");
        AssertTrue(BattleWriterPolicy.TryBombardStartIntent(s, 1, PrepState(ammoMean: 0.21f), out _), "just above it");
        AssertFalse(BattleWriterPolicy.TryBombardStartIntent(ArtillerySnapshot(cbo: 8, mounted: false, enemyDistance: 600f), 1, PrepState(), out none), "already on counter-battery");
        AssertFalse(BattleWriterPolicy.TryBombardStartIntent(ArtillerySnapshot(cbo: 9, mounted: false, enemyDistance: 600f), 1, PrepState(), out none), "already bombarding");
        AssertFalse(BattleWriterPolicy.TryBombardStartIntent(ArtillerySnapshot(cbo: 7, mounted: false, enemyDistance: 600f, paths: 1), 1, PrepState(), out none), "on a path");
        AssertFalse(BattleWriterPolicy.TryBombardStartIntent(ArtillerySnapshot(cbo: 7, mounted: false, enemyDistance: 600f, batteryFlags: UnitFlags.Routed), 1, PrepState(), out none), "routed");
        AssertFalse(BattleWriterPolicy.TryBombardStartIntent(ArtillerySnapshot(cbo: 7, mounted: false, enemyDistance: 600f, batteryFlags: UnitFlags.MarkedForRout), 1, PrepState(), out none), "marked for rout");
        AssertFalse(BattleWriterPolicy.TryBombardStartIntent(ArtillerySnapshot(cbo: 7, mounted: false, enemyDistance: 600f, batteryFlags: UnitFlags.PermanentlyDetached), 1, PrepState(), out none), "permanently detached");
        AssertFalse(BattleWriterPolicy.TryBombardStartIntent(s, 0, PrepState(), out none), "the brigade is no battery");

        // In the doctrine: Enable Tactical Artillery Doctrine on, the start is the behavior channel's one intent; off (the audit alone), it is never made.
        var buffer = new BattleOrderIntent[4];
        int n = BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, PrepState(), Rules(), buffer);
        int starts = 0;
        for (int i = 0; i < n; i++) if ((buffer[i].Flags & IntentFlags.BombardStart) != 0) starts++;
        AssertEqual(1, starts, "the doctrine submits the start");
        BattleArtilleryRules audit = Rules();
        audit.DoctrineOn = false;
        n = BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, PrepState(), audit, buffer);
        for (int i = 0; i < n; i++) AssertTrue((buffer[i].Flags & IntentFlags.BombardStart) == 0, "with the doctrine off the producer runs and starts nothing");
        n = BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, State(), Rules(), buffer);
        for (int i = 0; i < n; i++) AssertTrue((buffer[i].Flags & IntentFlags.BombardStart) == 0, "a battery with no preparation point starts nothing");
        BattleArtilleryState held = PrepState();
        held.InHold = true;
        AssertEqual(0, BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, held, Rules(), buffer), "a battery in its hold is left alone");
    }

    private static int CountCancels(BattleSnapshot s, BattleArtilleryState b, out int counterBattery)
    {
        var buffer = new BattleOrderIntent[4];
        int n = BattleWriterPolicy.ArtilleryDoctrineIntents(s, 1, b, Rules(), buffer);
        int cancels = 0;
        counterBattery = 0;
        for (int i = 0; i < n; i++)
        {
            if (buffer[i].Channel != OrderChannel.CombatBehavior) continue;
            if (buffer[i].Value == 7) cancels++;
            if (buffer[i].Value == 8) counterBattery++;
        }
        return cancels;
    }

    private static void PreparationExemptsBombard()
    {
        var deny = new DoctrineArtilleryDecision(DoctrineConsumerAction.Deny, "doctrine-says-no");
        BattleSnapshot bombarding = ArtillerySnapshot(cbo: 9, mounted: false, enemyDistance: 600f);

        // The doctrine's own Deny cancels a bombardment; with a preparation point it does not.
        AssertEqual(1, CountCancels(bombarding, State(ammo: 0.9f, doctrine: deny), out _), "without a preparation point a Deny cancels");
        AssertEqual(0, CountCancels(bombarding, PrepState(doctrine: deny), out _), "with one it does not: the doctrine does not stop the bombardment it started");

        // Counter-battery: an enemy battery in range switches a bombardment to 8; not under preparation.
        BattleSnapshot enemyGuns = ArtillerySnapshot(cbo: 9, mounted: false, enemyDistance: 600f, enemyUnitTyp: TacticalUnitType.Artillery);
        CountCancels(enemyGuns, State(), out int counter);
        AssertEqual(1, counter, "without a preparation point enemy guns draw counter-battery");
        CountCancels(enemyGuns, PrepState(), out counter);
        AssertEqual(0, counter, "with one the battery keeps bombarding");

        // The safety rules stay: low ammo, an unsupported battery with the enemy close, a friend in danger close.
        AssertEqual(1, CountCancels(bombarding, PrepState(ammo: 0.05f), out _), "ammo below 0.10 cancels");
        BattleSnapshot close = ArtillerySnapshot(cbo: 9, mounted: false, enemyDistance: 150f);
        AssertEqual(1, CountCancels(close, PrepState(), out _), "an unsupported battery with the enemy inside 0.2 of its range cancels");
        BattleSnapshot friendClose = ArtillerySnapshot(cbo: 9, mounted: false, enemyDistance: 600f, parentFlags: UnitFlags.None);
        AddFriendlyInfantry(friendClose, 500f, -450f);
        AssertEqual(1, CountCancels(friendClose, PrepState(doctrine: deny), out _), "friendly infantry in danger close keeps the doctrine's Deny a cancel");
        AssertEqual(0, CountCancels(bombarding, PrepState(doctrine: deny), out _), "the same Deny with no friend close is exempt");

        // The exemption is the point's: a battery ordered to prepare is exempt only while its order carries it.
        AssertEqual(1, CountCancels(bombarding, State(ammo: 0.05f), out _), "a battery without the point is cancelled as before");
    }

    private static void AddFriendlyInfantry(BattleSnapshot s, float x, float z)
    {
        s.AddOwn(new UnitRecord
        {
            InstanceId = 77, UnitTyp = TacticalUnitType.Infantry, X = x, Z = z, Strength = 400,
            Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry,
        });
    }

    private static void BombardStartWriter()
    {
        string runtime = FlankResponseTests.SourceText("Tactical", "Battle", "OrderExecutorRuntime.cs");
        string start = FlankResponseTests.Between(runtime, "private static bool StartBombard(", "// Artillery limber");
        FlankResponseTests.AssertPinned(start, "TacticalWriteGuard.ModeAllowsWrites()", "behind Commander Mode Active");
        FlankResponseTests.AssertPinned(start, "TacticalWriteGuard.CanWrite(unit)", "and the write guard (never a player-owned battery)");
        int behavior = start.IndexOf("TacticalCombatBehaviorWriter.EnsureBehavior(bunits, unit, BattleWriterPolicy.CombatBehaviorBombard, perRegiment: false)", StringComparison.Ordinal);
        int position = start.IndexOf("unit.bombardposition =", StringComparison.Ordinal);
        int range = start.IndexOf("unit.bombardrange = GamePrefs.bombardmentmaxrangesize;", StringComparison.Ordinal);
        AssertTrue(behavior >= 0 && position > behavior && range > position, "the behavior first, only then the position and the range, as vanilla writes them (3897-3900)");
        AssertTrue(start.IndexOf("lastfeudactiontime", StringComparison.Ordinal) < 0, "and not the W&L feud action timer, which Whiskey never acts for");
        FlankResponseTests.AssertPinned(runtime, "(w.Flags & IntentFlags.BombardStart) != 0", "the combat behavior case routes the flagged intent to it");

        // The pattern: the two writes are battle writes the gate scan knows, and nothing else in the source makes them.
        AssertTrue(BattleWriteGateTests.IsUngatedWriter("unit.bombardposition = p;"), "bombardposition = is a vanilla battle write");
        AssertTrue(BattleWriteGateTests.IsUngatedWriter("unit.bombardrange = GamePrefs.bombardmentmaxrangesize;"), "bombardrange = too");
        AssertFalse(BattleWriteGateTests.IsUngatedWriter("if (unit.bombardrange == r) return;"), "a comparison is not a write");
        AssertFalse(BattleWriteGateTests.IsUngatedWriter("if (TacticalWriteGuard.CanWrite(unit)) unit.bombardposition = p;"), "behind a gate it is accepted");
        var writers = new List<string>();
        string src = TestFixtures.FindRepoPath("src/WhiskeyRealism");
        var write = new System.Text.RegularExpressions.Regex(@"\.bombardposition\s*=[^=]|\.bombardrange\s*=[^=]");
        var comment = new System.Text.RegularExpressions.Regex(@"//[^\n]*");
        foreach (string path in System.IO.Directory.GetFiles(src, "*.cs", System.IO.SearchOption.AllDirectories))
            if (write.IsMatch(comment.Replace(System.IO.File.ReadAllText(path), string.Empty))) writers.Add(System.IO.Path.GetFileName(path));
        AssertEqual("OrderExecutorRuntime.cs", string.Join(",", writers), "only the executor's runtime writes the bombard fields");

        // The producer: the preparation point comes from the parent group's doctrine order, the table is noted from the intents the doctrine made.
        string producer = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "ArtilleryIntentProducer.cs");
        FlankResponseTests.AssertPinned(producer, "PreparationTable.ForAlliance(", "the preparation table is the producer's to write");
        FlankResponseTests.AssertPinned(producer, "IntentFlags.BombardStart", "noted when a battery passed the start test");
        FlankResponseTests.AssertPinned(producer, "state.HasPreparation = order.HasPreparation", "the point is the parent order's");
        FlankResponseTests.AssertPinned(producer, "PlayExecutorRuntime.ActivatedHours(", "keyed by the play's activation hour");
    }

    private sealed class AllowGuard : IOrderGuard
    {
        public bool CanWrite(in UnitRecord unit, out SkipReason reason)
        {
            reason = SkipReason.None;
            return true;
        }
    }

    // Water everywhere north of z (the battle z axis is negative; north = larger z).
    private sealed class WaterNorthOf : ITerrainProbe
    {
        private readonly float _z;
        public WaterNorthOf(float z) { _z = z; }
        public bool IsWaterOrFence(float x, float z) => z > _z;
        public bool LineCrossesWater(float x0, float z0, float x1, float z1) => z1 > _z;
        public bool StepBackFromRiver(float x0, float z0, ref float x1, ref float z1) => false;
    }

    // ------------------------------------------------------------------ stance hold refresh

    // Vanilla 4264 re-decides a pool group once lastaistancechangetime lapses.
    // #45 re-stamped it on every pass where Whiskey agreed with the ordered
    // stance; on the arbiter that agreement must still reach
    // TacticalStanceWriter (not dropped as Duplicate, not held back by the
    // recent-order cooldown, which is for new orders), whose cheap path skips a
    // live stamp and whose full pass claims and re-stamps a lapsed one.
    private static void HeldStanceRefreshReachesTheWriter()
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.AddOwn(Group(71, Brigade, aiStanceOrdered: 3));
        s.IndexCommandTree(null);
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), null);
        var held = new BattleOrderIntent { GroupId = 71, Channel = OrderChannel.Stance, Value = 3, Heading = -1f, Priority = IntentPriority.Scorer, Source = IntentSource.GroupStanceScorer, Reason = "test" };

        AssertTrue(executor.Plan(s, held, 1.0).Execute, "an already-ordered stance goes to the writer (hold refresh)");
        executor.RecordIssued(held, 1.0, null, 0, null, 0, 0, 0);
        double soon = 1.0 + 10.0 / 3600.0;
        AssertTrue(executor.Plan(s, held, soon).Execute, "a refresh is not a new order: no recent-order cooldown");
        BattleOrderIntent change = held;
        change.Value = 2;
        AssertEqual(SkipReason.Cooldown, executor.Plan(s, change, soon).Skip, "a stance change still waits out the cooldown");

        // Writer side: the record is fresh (0.05 h) but vanilla's raw stamp lapsed
        // (night jump), so the full pass runs, claims and re-stamps well before
        // HoldHours (0.25 h) pass.
        const float reset = 1f;
        var record = new StanceHoldRecord(3, 10.00f, StanceWriteSource.Scorer);
        float lapsedStamp = TacticalStanceOwnership.VanillaHoldStamp(30.0f, TacticalStanceOwnership.HoldHours, reset);
        float vanillaNow = 39.5f;
        AssertFalse(TacticalStanceOwnership.SteadyStateHolds(record, 3, StanceWriteSource.Scorer, 3, 10.05f, lapsedStamp, vanillaNow, reset),
            "lapsed stamp: no cheap skip");
        var input = new StanceWriteInput(true, 3, 3, 1, 10.05f, record, StanceWriteSource.Scorer, vanillaGroupStance: 3, vanillaNowHours: vanillaNow);
        StanceWriteDecision decision = TacticalStanceOwnership.Decide(input);
        AssertEqual(StanceWriteAction.Claim, decision.Action, "agreement is a claim, not a ChangeStance");
        AssertTrue(TacticalStanceOwnership.StampsVanillaHold(decision, input), "the claim re-stamps");
        float stamp = TacticalStanceOwnership.VanillaHoldStamp(input, reset);
        AssertTrue(TacticalStanceOwnership.SteadyStateHolds(TacticalStanceOwnership.Next(decision, input), 3, StanceWriteSource.Scorer, 3, 10.06f, stamp, vanillaNow + 0.01f, reset),
            "the re-stamped hold is live again");
    }

    // ------------------------------------------------------------------ #42 feud replay

    // The Feud write exemption belongs to #42's inline replay of vanilla's own
    // feud move only (OrderExecutorRuntime.ExecuteInline). A FeudReplay-sourced
    // intent submitted by a registered producer never enters the arbiter, so it
    // can neither execute nor publish a claim (and so no retry of it can run).
    private static void FeudReplayNeverEntersTheArbiter()
    {
        var arbiter = new OrderArbiter();
        arbiter.BeginTick();
        arbiter.Submit(new BattleOrderIntent { GroupId = 31, Channel = OrderChannel.Move, TargetX = 5f, TargetZ = -5f, Heading = -1f, Priority = IntentPriority.Support, Source = IntentSource.FeudReplay, Reason = "test" });
        AssertEqual(0, arbiter.IntentCount, "a producer cannot submit a feud replay");
        var winners = new BattleOrderIntent[2];
        AssertEqual(0, arbiter.Resolve(winners), "nothing to execute");
        AssertFalse(arbiter.TryGetClaim(31, OrderChannel.Move, out _), "no claim published");
    }

    // Vanilla CheckForFeudGroupActions (4931-4960) moves a feuding group toward
    // the closest enemy within neededdistancefeudgroupmovement with a delayed
    // SetWaypoint (useorderdelay: true, 4957). #42 replays it as a FeudReplay
    // intent through the executor pipeline: the Feud refusal is exempt for it.
    private static void FeudReplayUsesInlineExecutor()
    {
        BattleSnapshot s = FeudSnapshot(UnitFlags.Feud, enemyDistance: 300f, enemyState: ObservationState.Visible);
        AssertTrue(BattleWriterPolicy.TryFeudReplayIntent(s, 0, 400f, out BattleOrderIntent intent), "visible enemy inside the feud cap");
        AssertEqual(OrderChannel.Move, intent.Channel, "a move");
        AssertEqual(IntentSource.FeudReplay, intent.Source, "source");
        AssertEqual(IntentPriority.Support, intent.Priority, "Support (plan inventory)");
        AssertTrue((intent.Flags & IntentFlags.UseOrderDelay) != 0, "vanilla's courier delay (4957)");
        AssertNear(-800f, intent.TargetZ, 0.01f, "targets the visible enemy");

        AssertFalse(BattleWriterPolicy.TryFeudReplayIntent(s, 0, 250f, out _), "beyond neededdistancefeudgroupmovement");
        AssertFalse(BattleWriterPolicy.TryFeudReplayIntent(FeudSnapshot(UnitFlags.Feud, 300f, ObservationState.Remembered), 0, 400f, out _),
            "a remembered enemy is not a feud target");

        var executor = new OrderExecutor();
        executor.Bind(new FeudAwareGuard(), null);
        ExecutionPlan plan = executor.Plan(s, intent, 1.0);
        AssertTrue(plan.Execute, "the feud replay passes the Feud refusal");
        AssertTrue(plan.UseOrderDelay, "courier delay kept");

        BattleOrderIntent other = intent;
        other.Source = IntentSource.PostureExecutor;
        AssertEqual(SkipReason.Feud, executor.Plan(s, other, 1.0).Skip, "any other source is refused on a feuding group");

        var arbiter = new OrderArbiter();
        arbiter.BeginTick();
        arbiter.Submit(new BattleOrderIntent { GroupId = intent.GroupId, Channel = OrderChannel.Move, TargetX = 1f, TargetZ = -1f, Heading = -1f, Priority = IntentPriority.Safety, Source = IntentSource.Withdrawal, Reason = "test" });
        arbiter.Resolve(new BattleOrderIntent[2]);
        AssertEqual(SkipReason.LowerPriorityThanClaim, OrderExecutor.InlineClaimCheck(arbiter, intent), "refused under a higher arbiter claim (D4)");
    }

    private static void FeudReplayRefusedForPlayerOwned()
    {
        BattleSnapshot s = FeudSnapshot(UnitFlags.Feud | UnitFlags.PlayerOwned, 300f, ObservationState.Visible);
        AssertTrue(BattleWriterPolicy.TryFeudReplayIntent(s, 0, 400f, out BattleOrderIntent intent), "intent built");
        var executor = new OrderExecutor();
        executor.Bind(new FeudAwareGuard(), null);
        ExecutionPlan plan = executor.Plan(s, intent, 1.0);
        AssertFalse(plan.Execute, "never on a player-owned group");
        AssertEqual(SkipReason.PlayerOwned, plan.Skip, "PlayerOwned is never exempt");
    }

    // Without a battle snapshot for the feud side the executor cannot run; the
    // replay must not hand the body back to vanilla (whose feud move has no
    // ownership check, 4931-4960) but fall back to a direct move behind
    // TacticalWriteGuard.CanWrite(Regiment, feudReplay: true), which refuses
    // player-owned groups and groups whose order would reach an exposed
    // player-owned descendant.
    private static void FeudReplayFailsClosedWithoutSnapshot()
    {
        AssertEqual(FeudReplayPath.Executor, BattleWriterPolicy.DecideFeudReplayPath(hasSnapshot: true, canWriteFeudReplay: false),
            "with a snapshot the executor's guard and chain fan-out decide");
        AssertEqual(FeudReplayPath.Executor, BattleWriterPolicy.DecideFeudReplayPath(hasSnapshot: true, canWriteFeudReplay: true), "executor");
        AssertEqual(FeudReplayPath.DirectGuarded, BattleWriterPolicy.DecideFeudReplayPath(hasSnapshot: false, canWriteFeudReplay: true),
            "no snapshot, AI group: guarded direct replay");
        AssertEqual(FeudReplayPath.Refused, BattleWriterPolicy.DecideFeudReplayPath(hasSnapshot: false, canWriteFeudReplay: false),
            "no snapshot, player-protected group: refused, never vanilla's unguarded move");
    }

    // A feuding brigade at (500, -500) and one enemy `enemyDistance` due south.
    private static BattleSnapshot FeudSnapshot(UnitFlags flags, float enemyDistance, ObservationState enemyState)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.SetSliceCount(20);
        var group = Group(51, Brigade, 3);
        group.X = 500f;
        group.Z = -500f;
        group.Flags |= flags;
        s.AddOwn(group);
        s.IndexCommandTree(null);
        s.AddObservation(new EnemyObservation { InstanceId = 901, UnitTyp = TacticalUnitType.Infantry, X = 500f, Z = -500f - enemyDistance, Strength = 800f, State = enemyState, Confidence01 = 1f });
        SlicePressureBuilder.Build(s, new SliceGeometry { MaxRangeMeters = 250f, FlankShiftMeters = 150f });
        return s;
    }

    // TacticalWriteGuard.CanWrite(in UnitRecord) without the Commander Mode read.
    private sealed class FeudAwareGuard : IOrderGuard
    {
        public bool CanWrite(in UnitRecord unit, out SkipReason reason)
        {
            reason = (unit.Flags & UnitFlags.PlayerOwned) != 0 ? SkipReason.PlayerOwned
                : (unit.Flags & UnitFlags.Feud) != 0 ? SkipReason.Feud : SkipReason.None;
            return reason == SkipReason.None;
        }
    }

    // ------------------------------------------------------------------ #44 macro

    // #44's legacy odds scorer read vanilla's enemystrengthwithinangle (true enemy
    // positions, 122719-122733). It now reads the snapshot's slice pressure:
    // visible enemies in full, remembered ones discounted, unseen ones never.
    private static void MacroUsesSnapshotSlices()
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.SetSliceCount(20);
        var group = Group(61, Brigade, 3);
        group.X = 500f;
        group.Z = -500f;
        s.AddOwn(group);
        s.IndexCommandTree(null);
        s.AddObservation(new EnemyObservation { InstanceId = 1, UnitTyp = TacticalUnitType.Infantry, X = 500f, Z = -600f, Strength = 1000f, State = ObservationState.Visible, Confidence01 = 1f });
        s.AddObservation(new EnemyObservation { InstanceId = 2, UnitTyp = TacticalUnitType.Infantry, X = 600f, Z = -500f, Strength = 400f, State = ObservationState.Remembered, Confidence01 = 0.5f });
        SlicePressureBuilder.Build(s, new SliceGeometry { MaxRangeMeters = 250f, FlankShiftMeters = 150f });

        float expected = 1000f + SlicePressureBuilder.RememberedPressureWeight * 0.5f * 400f;
        AssertNear(expected, BattleWriterPolicy.SnapshotPressure(s, 0), 0.01f, "visible + discounted remembered");
        AssertEqual(0f, BattleWriterPolicy.SnapshotPressure(s, -1), "a group outside the snapshot reads no pressure");
        AssertEqual(0f, BattleWriterPolicy.SnapshotPressure(null, 0), "no snapshot reads no pressure");
    }

    // vanilla groupenemiesinrange (120558-120563) is the max of the subordinates'
    // unitrange.enemiesinrange: enemy strength within the line-of-sight range,
    // never fog-filtered for the AI side (122650-122655). #44 now reads what the
    // side perceives within that range of the group or a descendant: visible in
    // full, remembered by confidence, off-map reinforcements never, each enemy once.
    private static void MacroEnemiesInRangeFromSnapshot()
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        var group = Group(91, Brigade, 3);
        group.X = 0f;
        group.Z = -1000f;
        s.AddOwn(group);
        s.AddOwn(new UnitRecord { InstanceId = 92, UnitTyp = TacticalUnitType.Infantry, X = 0f, Z = -1400f, ParentIndex = 0, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry });
        s.Own[1].ParentIndex = 0;
        s.AddOwn(new UnitRecord { InstanceId = 93, UnitTyp = TacticalUnitType.Infantry, X = 5000f, Z = -1000f, ParentIndex = -1, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry });
        s.IndexCommandTree(null);
        s.AddObservation(new EnemyObservation { InstanceId = 1, X = 0f, Z = -1200f, Strength = 1000f, State = ObservationState.Visible, Confidence01 = 1f });
        s.AddObservation(new EnemyObservation { InstanceId = 2, X = 300f, Z = -1000f, Strength = 400f, State = ObservationState.Remembered, Confidence01 = 0.5f });
        s.AddObservation(new EnemyObservation { InstanceId = 3, X = 5000f, Z = -1100f, Strength = 9000f, State = ObservationState.Visible, Confidence01 = 1f });
        s.AddObservation(new EnemyObservation { InstanceId = 4, X = 0f, Z = -1300f, Strength = 7000f, State = ObservationState.Visible, Confidence01 = 1f, IsReinforcement = true });
        s.AddObservation(new EnemyObservation { InstanceId = 5, X = 0f, Z = -1700f, Strength = 600f, State = ObservationState.Visible, Confidence01 = 1f });

        float inRange = BattleWriterPolicy.SnapshotEnemyStrengthInRange(s, 0, 350f);
        AssertNear(1000f + 0.5f * 400f, inRange, 0.01f,
            "visible in full + remembered by confidence, from the group's own position (vanilla reads the group's own unitrange, "
            + "120558/120563): an enemy near only a descendant, another command's enemy and off-map arrivals never");
        AssertEqual(0f, BattleWriterPolicy.SnapshotEnemyStrengthInRange(s, -1, 350f), "outside the snapshot");
        AssertEqual(0f, BattleWriterPolicy.SnapshotEnemyStrengthInRange(null, 0, 350f), "no snapshot");
    }

    // vanilla flanksthreated / outflanked come from the unfiltered range lists
    // (UpdateUnitRangeFast 122650, 122976+; UpdateGroupStrength 120566-120580). #44's
    // flank risk now uses the perceived flank pressure: the group's own outflanked
    // state, or the mean vanilla outflanked weight of its regiments
    // (ArmyEvidenceBuilder.MeanFlankThreatByGroup, BC2b).
    private static void MacroFlankRiskFromPerceivedFlankPressure()
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.SetSliceCount(20);
        var group = Group(95, Brigade, 3);
        s.AddOwn(group);
        s.AddOwn(new UnitRecord { InstanceId = 96, UnitTyp = TacticalUnitType.Infantry, Strength = 500, X = 0f, Z = -100f, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry });
        s.Own[1].ParentIndex = 0;
        s.IndexCommandTree(null);
        var mean = new float[2];
        var count = new int[2];

        WhiskeyRealism.Tactical.Orchestrator.ArmyEvidenceBuilder.MeanFlankThreatByGroup(s, mean, count);
        AssertFalse(BattleWriterPolicy.PerceivedFlankRisk(s, 0, mean), "nothing perceived on a flank");

        s.SetFlankPressure(1, 0, 800f);
        WhiskeyRealism.Tactical.Orchestrator.ArmyEvidenceBuilder.MeanFlankThreatByGroup(s, mean, count);
        AssertTrue(BattleWriterPolicy.PerceivedFlankRisk(s, 0, mean), "a regiment outflanked on its left");

        s.SetFlankPressure(1, 0, 0f);
        s.SetFlankPressure(0, 2, 300f);
        WhiskeyRealism.Tactical.Orchestrator.ArmyEvidenceBuilder.MeanFlankThreatByGroup(s, mean, count);
        AssertTrue(BattleWriterPolicy.PerceivedFlankRisk(s, 0, mean), "the group's own rear");
        AssertFalse(BattleWriterPolicy.PerceivedFlankRisk(s, -1, mean), "outside the snapshot");
    }

    // ------------------------------------------------------------------ fixtures

    private static UnitRecord Group(int id, int unitTyp, int aiStanceOrdered)
    {
        return new UnitRecord
        {
            InstanceId = id, UnitTyp = unitTyp, AiStanceOrdered = aiStanceOrdered, AiStance = aiStanceOrdered, ParentIndex = -1,
            Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup,
        };
    }

    private static TacticalResolvedGroupDecision Apply(int stance, bool fromDoctrine = false, bool fromLedger = false)
    {
        return new TacticalResolvedGroupDecision(stance, TacticalDoctrineDecisionKind.Apply, "test", fromDoctrine, fromLedger, false, ChargeOverrideAction.None);
    }

    private static TacticalResolvedGroupDecision Skip()
    {
        return new TacticalResolvedGroupDecision(-1, TacticalDoctrineDecisionKind.Skip, "test", false, false, false, ChargeOverrideAction.None);
    }

    private static TacticalResolvedGroupDecision Demote(int stance)
    {
        return new TacticalResolvedGroupDecision(stance, TacticalDoctrineDecisionKind.Skip, "charge-demote-doctrine", false, false, true, ChargeOverrideAction.DemoteToHold);
    }
}
