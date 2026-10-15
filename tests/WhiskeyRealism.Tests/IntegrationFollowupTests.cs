using System;
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

    private static void AssertNear(float expected, float actual, string label)
    {
        if (Math.Abs(expected - actual) > 0.0001f)
            throw new Exception(label + ": expected " + expected + " got " + actual);
    }
}
