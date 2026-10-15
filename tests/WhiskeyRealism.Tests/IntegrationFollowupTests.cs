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
        TelegraphRefundsOnlyWhiskeyRejections();
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

    // AIBattle micro facing (4630 region): vanilla only calls RotateRegiment
    // when Tools.GetAbsAngleDifference(eulerAngles.y, target) >=
    // GamePrefs.angletriggerforturningtowardsenemy.
    private static void ArtilleryFaceMirrorsVanillaTurnTrigger()
    {
        TestHarness.AssertFalse(BattleWriterPolicy.ShouldRotateTowardTarget(90f, 100f, 20f), "inside trigger");
        TestHarness.AssertTrue(BattleWriterPolicy.ShouldRotateTowardTarget(90f, 110f, 20f), "at trigger");
        TestHarness.AssertFalse(BattleWriterPolicy.ShouldRotateTowardTarget(355f, 5f, 20f), "wraps through 0");
        TestHarness.AssertTrue(BattleWriterPolicy.ShouldRotateTowardTarget(0f, 180f, 20f), "facing away");
        TestHarness.AssertTrue(BattleWriterPolicy.ShouldRotateTowardTarget(-170f, 170f, 0f), "zero trigger rotates on any difference");
        TestHarness.AssertFalse(BattleWriterPolicy.ShouldRotateTowardTarget(10f, float.NaN, 20f), "invalid target never rotates");
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

    // Vanilla's own no-site cancellation (97578-97585) refunds nothing; only a
    // placement vanilla accepted and Whiskey removed gets its charge back.
    private static void TelegraphRefundsOnlyWhiskeyRejections()
    {
        TestHarness.AssertTrue(TelegraphWishCharge.Refunds(TelegraphWishOutcome.RejectedByWhiskey), "whiskey rejection");
        TestHarness.AssertFalse(TelegraphWishCharge.Refunds(TelegraphWishOutcome.Placed), "kept placement");
        TestHarness.AssertFalse(TelegraphWishCharge.Refunds(TelegraphWishOutcome.CancelledByVanilla), "vanilla cancellation");
        Pass("telegraph refunds only Whiskey rejections");
    }

    private static void AssertNear(float expected, float actual, string label)
    {
        if (Math.Abs(expected - actual) > 0.0001f)
            throw new Exception(label + ": expected " + expected + " got " + actual);
    }
}
