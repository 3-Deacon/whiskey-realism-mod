using System;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;

// Integration-pass follow-ups. Each test pins a pure decision extracted from a
// Harmony patch to the vanilla behavior it must respect.
internal static class IntegrationFollowupTests
{
    public static void Run()
    {
        ArtilleryAmmoRatioIsMeanOfSlots();
        ArtilleryAmmoRatioTreatsMissingDataAsFull();
        ArtilleryFullBatteryIsNotConserveAmmo();
        ArtilleryFaceMirrorsVanillaTurnTrigger();
        TelegraphWishChargeMeasuresWhatPlaceTook();
        TelegraphWishChargeWithoutSpendIsNoCharge();
        ConstructionObserverSkipsRemovedPlacement();
        ChargeDenialDemotesDoctrineDeniedAssaultWithArmyOn();
        ChargeDenialPreservesAssaultDoctrineAgreesWith();
        ChargeDenialLegacyReactionOnlyWithArmyOff();
        ChargeDenialFlagOffPreservesEverything();
    }

    private static void Pass(string name)
    {
        Console.WriteLine("PASS " + name);
    }

    // Regiment.ammo (111498) holds three 0..1 slots (solid/explosive/canister);
    // vanilla GetROF reads artillery as Tools.SumUp(ammo) / 3 (21038).
    private static void ArtilleryAmmoRatioIsMeanOfSlots()
    {
        AssertNear(1f, TacticalArtilleryInputAdapter.ArtilleryAmmoRatio01(new[] { 1f, 1f, 1f }), "full limbers");
        AssertNear(0.5f, TacticalArtilleryInputAdapter.ArtilleryAmmoRatio01(new[] { 1f, 0.5f, 0f }), "mean of slots");
        AssertNear(0.05f, TacticalArtilleryInputAdapter.ArtilleryAmmoRatio01(new[] { 0.05f, 0.05f, 0.05f }), "nearly empty");
        AssertNear(0f, TacticalArtilleryInputAdapter.ArtilleryAmmoRatio01(new[] { -1f, 0f, 0f }), "negative clamps");
        AssertNear(1f, TacticalArtilleryInputAdapter.ArtilleryAmmoRatio01(new[] { 2f, 2f, 2f }), "over-full clamps");
        AssertNear(0.5f, TacticalArtilleryInputAdapter.ArtilleryAmmoRatio01(new[] { 1f, float.NaN, 0f }), "non-finite slot skipped");
        Pass("artillery ammo ratio is the mean of the 0-1 slots");
    }

    private static void ArtilleryAmmoRatioTreatsMissingDataAsFull()
    {
        AssertNear(1f, TacticalArtilleryInputAdapter.ArtilleryAmmoRatio01(null), "null array");
        AssertNear(1f, TacticalArtilleryInputAdapter.ArtilleryAmmoRatio01(new float[0]), "empty array");
        AssertNear(1f, TacticalArtilleryInputAdapter.ArtilleryAmmoRatio01(new[] { float.NaN }), "no finite slot");
        Pass("artillery ammo ratio treats unreadable ammo as full");
    }

    // The all-stance audit regression: a full battery used to read ~0.0025
    // (sum / guns*200) and every AI bombardment was cancelled as ConserveAmmo.
    private static void ArtilleryFullBatteryIsNotConserveAmmo()
    {
        float ratio = TacticalArtilleryInputAdapter.ArtilleryAmmoRatio01(new[] { 1f, 1f, 1f });
        var decision = TacticalArtilleryMicroDoctrine.Decide(new TacticalArtilleryMicroInput(
            hasEnemy: true,
            enemyCloseDistance: 600f,
            panicDistance: 100f,
            supportCount: 0,
            moraleBonus: true,
            enemyRoutedOrRetreating: false,
            limberState: TacticalArtilleryLimberState.Unlimbered,
            ammoRatio01: ratio,
            targetDistance: 600f,
            maxRange: 1200f,
            currentQuadrantThreat: 1f,
            bestQuadrantThreat: 1f,
            canWheel: true,
            frontalTargetInRange: true));
        TestHarness.AssertEqual(TacticalArtilleryMicroAction.Fire, decision.Action, "full battery in range fires");

        var write = BattleWriterPolicy.PlanArtilleryMicroWrite(decision.Action, 0, 0, 0, 9, enemyInFireRange: true);
        TestHarness.AssertEqual(ArtilleryMicroWrite.None, write, "bombardment (9) is left alone");
        Pass("full battery keeps its bombardment");
    }

    // AIBattle micro facing (4599-4606): vanilla only calls RotateRegiment
    // when Tools.GetAbsAngleDifference(eulerAngles.y, target) >=
    // angletriggerforturningtowardsenemy + (covervalue >
    // maxcovervalueforgrouprotation && coverobject != 3 ?
    // additionalanglewhenincovermicro : 0).
    private static bool Rotate(float current, float target, float coverValue = 0f, int coverObject = 0)
    {
        return BattleWriterPolicy.ShouldRotateTowardTarget(
            current, target,
            turnTriggerDegrees: 20f,
            coverValue: coverValue,
            coverObject: coverObject,
            maxCoverValueForRotation: 0.5f,
            additionalAngleInCover: 30f);
    }

    private static void ArtilleryFaceMirrorsVanillaTurnTrigger()
    {
        TestHarness.AssertFalse(Rotate(90f, 100f), "inside trigger");
        TestHarness.AssertTrue(Rotate(90f, 110f), "at trigger");
        TestHarness.AssertFalse(Rotate(355f, 5f), "wraps through 0");
        TestHarness.AssertTrue(Rotate(0f, 180f), "facing away");
        TestHarness.AssertFalse(Rotate(10f, float.NaN), "invalid target never rotates");
        TestHarness.AssertTrue(BattleWriterPolicy.ShouldRotateTowardTarget(-170f, 170f, 0f, 0f, 0, 0.5f, 30f), "zero trigger rotates on any difference");

        // In cover the trigger grows by additionalanglewhenincovermicro (20 + 30).
        TestHarness.AssertFalse(Rotate(0f, 40f, coverValue: 0.8f), "in cover below enlarged trigger");
        TestHarness.AssertTrue(Rotate(0f, 50f, coverValue: 0.8f), "in cover at enlarged trigger");
        TestHarness.AssertTrue(Rotate(0f, 40f, coverValue: 0.5f), "cover not above the threshold");
        TestHarness.AssertTrue(Rotate(0f, 40f, coverValue: 0.8f, coverObject: 3), "cover object 3 excluded");

        TestHarness.AssertEqual(20f, BattleWriterPolicy.AbsAngleDifference(350f, 10f), "vanilla wrap");
        TestHarness.AssertEqual(180f, BattleWriterPolicy.AbsAngleDifference(0f, 180f), "half turn");
        Pass("artillery face mirrors the vanilla turn trigger");
    }

    // AddConstructionWish -> Place(pay: true) (96215-96246): subsidy type >= 0
    // moves subsidyfunding + cumulatedspending_construction, then the treasury
    // pays the missing-subsidy cost; AdjustTreasury shrinks an AI's charge, so
    // the measured delta (not the nominal cost) is what gets reversed.
    private static void TelegraphWishChargeMeasuresWhatPlaceTook()
    {
        var before = new AllianceSpendSnapshot(1000f, new[] { 50f, 20f }, -300f, -400f);
        var after = new AllianceSpendSnapshot(990f, new[] { 50f, 0f }, -320f, -410f);
        var charge = TelegraphWishCharge.Measure(before, after);
        AssertNear(-10f, charge.TreasuryDelta, "treasury delta");
        AssertNear(0f, charge.SubsidyFundingDelta[0], "untouched subsidy");
        AssertNear(-20f, charge.SubsidyFundingDelta[1], "subsidy delta");
        AssertNear(-20f, charge.CumulatedSpendingConstructionDelta, "cumulated spending delta");
        AssertNear(-10f, charge.ConstructionCostsDelta, "construction costs delta");
        TestHarness.AssertTrue(charge.HasCharge, "has charge");

        // The snapshot copies the array: later vanilla writes to the live array
        // must not change what was measured.
        var live = new[] { 5f };
        var snap = new AllianceSpendSnapshot(0f, live, 0f, 0f);
        live[0] = 99f;
        AssertNear(5f, snap.SubsidyFunding[0], "snapshot copies subsidy funding");
        Pass("telegraph wish charge measures what Place took");
    }

    private static void TelegraphWishChargeWithoutSpendIsNoCharge()
    {
        var same = new AllianceSpendSnapshot(1000f, new[] { 1f }, 0f, 0f);
        TestHarness.AssertFalse(TelegraphWishCharge.Measure(same, same).HasCharge, "no movement");
        var nullSubsidy = new AllianceSpendSnapshot(1000f, null, 0f, 0f);
        TestHarness.AssertFalse(TelegraphWishCharge.Measure(nullSubsidy, nullSubsidy).HasCharge, "missing subsidy array");
        var nan = new AllianceSpendSnapshot(float.NaN, null, 0f, 0f);
        TestHarness.AssertFalse(TelegraphWishCharge.Measure(nullSubsidy, nan).HasCharge, "non-finite delta ignored");
        TestHarness.AssertFalse(default(TelegraphWishCharge).HasCharge, "default has no charge");
        Pass("telegraph wish charge without spend is no charge");
    }

    // TelegraphPlacementPatch (Priority.High) runs before the construction
    // observer's Place postfix; a station it rejects is already out of
    // BattleUnits.cbuildings (RemoveBuilding 98334) and must not be counted.
    private static void ConstructionObserverSkipsRemovedPlacement()
    {
        TestHarness.AssertTrue(ConstructionStartFilter.CountsBuildingStart(true, 0, false, true), "real start counts");
        TestHarness.AssertFalse(ConstructionStartFilter.CountsBuildingStart(true, 0, false, false), "removed before observed");
        TestHarness.AssertFalse(ConstructionStartFilter.CountsBuildingStart(true, 1, true, true), "wish placeholder");
        TestHarness.AssertFalse(ConstructionStartFilter.CountsBuildingStart(false, 1, false, true), "not newly created");
        TestHarness.AssertFalse(ConstructionStartFilter.CountsBuildingStart(true, 2, false, true), "Europe");
        TestHarness.AssertFalse(ConstructionStartFilter.CountsBuildingStart(true, -1, false, true), "no owner");
        Pass("construction observer skips a placement removed before it is observed");
    }

    private static ChargeDenialDecision Deny(
        bool enabled = true,
        bool armyOn = true,
        DoctrineConsumerAction doctrine = DoctrineConsumerAction.Observe,
        bool legacyDeny = false)
    {
        return TacticalChargeDenial.DecideForVanillaAssault(new ChargeDenialInput(enabled, armyOn, doctrine, legacyDeny));
    }

    // Vanilla assault (ai_stance 4, AdjustGroupAIStance 4240) differs from
    // attack (3) by the assault micro placement (5694-5728: skipcover) and
    // skirmisher reattachment (5363/5374); cavalry charges (AdjustCavalryBehavior
    // 5047) need ai_stance != 1 && != 2. The doctrine denies attack for
    // reserve-held / fallback groups (DecideStance -> stance 2), and
    // ChargeDemotion is the only source TacticalStanceOwnership lets replace a 4.
    private static void ChargeDenialDemotesDoctrineDeniedAssaultWithArmyOn()
    {
        var d = Deny(doctrine: DoctrineConsumerAction.Deny);
        TestHarness.AssertTrue(d.Demotes, "doctrine deny demotes");
        TestHarness.AssertEqual(2, d.DemoteStance, "demotes to the doctrine's defend stance");
        TestHarness.AssertEqual("charge-demote-doctrine", d.Reason, "reason");

        var armyOff = Deny(armyOn: false, doctrine: DoctrineConsumerAction.Deny);
        TestHarness.AssertTrue(armyOff.Demotes, "doctrine deny also demotes with the army off");

        var stance = TacticalStanceOwnership.Decide(new StanceWriteInput(
            isAiGroup: true,
            targetStance: d.DemoteStance,
            currentOrderedStance: 4,
            macroAi: 0,
            nowHours: 1f,
            record: default(StanceHoldRecord),
            source: StanceWriteSource.ChargeDemotion,
            vanillaGroupStance: 4));
        TestHarness.AssertEqual(StanceWriteAction.Write, stance.Action, "ownership lets the demotion replace the assault");
        Pass("charge denial demotes a doctrine-denied assault with the army on");
    }

    private static void ChargeDenialPreservesAssaultDoctrineAgreesWith()
    {
        var allow = Deny(doctrine: DoctrineConsumerAction.Allow, legacyDeny: true);
        TestHarness.AssertFalse(allow.Demotes, "doctrine attack keeps the assault");
        TestHarness.AssertEqual("doctrine-attack-agrees", allow.Reason, "allow reason");

        var observe = Deny();
        TestHarness.AssertFalse(observe.Demotes, "no doctrine opinion keeps the assault");
        TestHarness.AssertEqual("vanilla-charge-preserved", observe.Reason, "observe reason");
        Pass("charge denial preserves an assault the doctrine agrees with");
    }

    // The legacy local-reaction producer (BattleCommanderIntentObserverPatch)
    // only writes TacticalReactionContext while the orchestrator army is off.
    private static void ChargeDenialLegacyReactionOnlyWithArmyOff()
    {
        var legacy = Deny(armyOn: false, legacyDeny: true);
        TestHarness.AssertTrue(legacy.Demotes, "legacy DenyCharge demotes with the army off");
        TestHarness.AssertEqual(3, legacy.DemoteStance, "legacy demotion keeps stance 3");
        TestHarness.AssertEqual("charge-demote", legacy.Reason, "legacy reason");

        TestHarness.AssertFalse(Deny(armyOn: true, legacyDeny: true).Demotes, "army on ignores legacy reactions");
        Pass("charge denial uses legacy reactions only with the army off");
    }

    private static void ChargeDenialFlagOffPreservesEverything()
    {
        var off = Deny(enabled: false, doctrine: DoctrineConsumerAction.Deny, armyOn: false, legacyDeny: true);
        TestHarness.AssertFalse(off.Demotes, "flag off");
        TestHarness.AssertEqual("vanilla-charge-preserved", off.Reason, "flag off reason");
        Pass("charge denial flag off preserves every assault");
    }

    private static void AssertNear(float expected, float actual, string label)
    {
        if (Math.Abs(expected - actual) > 0.0001f)
            throw new Exception(label + ": expected " + expected + " got " + actual);
    }
}
