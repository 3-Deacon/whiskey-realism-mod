using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Orchestrator;

/// <summary>
/// WS T1 (2026-10-14 remediation) regression tests: orchestrator evidence,
/// geometry, triggers, clocks and phase doctrine. Every test pins a vanilla
/// fact read from the decompile (anchors in the source comments).
/// </summary>
internal static class TacticalEvidenceFixTests
{
    public static void Run()
    {
        Run("ammo normalizes infantry from ammo[0]", AmmoInfantryUsesFirstSlot);
        Run("ammo normalizes artillery as mean of three slots", AmmoArtilleryUsesMeanOfSlots);
        Run("ammo falls back on null or empty array", AmmoFallsBackOnMissingArray);
        Run("ammo clamps to unit interval", AmmoClampsToUnitInterval);
        Run("alive combat unit needs strength active and not routed", AliveCombatUnitPredicate);
        Run("scheduled arrival durationleft is already hours", ScheduledArrivalDurationIsHours);
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine("PASS " + name);
    }

    // ---- T1 ammo ----

    private static void AmmoInfantryUsesFirstSlot()
    {
        var ammo = new float[] { 0.25f, 1f, 1f };
        TestHarness.AssertEqual(0.25f, ArmyEvidenceBuilder.NormalizeAmmo01(ammo, unittyp: 0, fallback: 0.9f), "infantry reads ammo[0]");
        TestHarness.AssertEqual(0.25f, ArmyEvidenceBuilder.NormalizeAmmo01(ammo, unittyp: 1, fallback: 0.9f), "cavalry reads ammo[0]");
    }

    private static void AmmoArtilleryUsesMeanOfSlots()
    {
        var ammo = new float[] { 0.3f, 0.6f, 0.9f };
        float v = ArmyEvidenceBuilder.NormalizeAmmo01(ammo, unittyp: 2, fallback: 0.9f);
        TestHarness.AssertTrue(Math.Abs(v - 0.6f) < 1e-5f, "artillery reads SumUp(ammo)/3, got " + v);
    }

    private static void AmmoFallsBackOnMissingArray()
    {
        TestHarness.AssertEqual(0.9f, ArmyEvidenceBuilder.NormalizeAmmo01(null, 0, 0.9f), "null array falls back");
        TestHarness.AssertEqual(0.9f, ArmyEvidenceBuilder.NormalizeAmmo01(new float[0], 0, 0.9f), "empty array falls back");
        TestHarness.AssertEqual(0.9f, ArmyEvidenceBuilder.NormalizeAmmo01(new float[] { float.NaN, 1f, 1f }, 0, 0.9f), "NaN falls back");
    }

    private static void AmmoClampsToUnitInterval()
    {
        TestHarness.AssertEqual(1f, ArmyEvidenceBuilder.NormalizeAmmo01(new float[] { 3f, 0f, 0f }, 0, 0.9f), "above 1 clamps");
        TestHarness.AssertEqual(0f, ArmyEvidenceBuilder.NormalizeAmmo01(new float[] { -1f, 0f, 0f }, 0, 0.9f), "below 0 clamps");
    }

    private static void AliveCombatUnitPredicate()
    {
        TestHarness.AssertTrue(ArmyEvidenceBuilder.IsAliveCombatUnit(strength: 400, activeInHierarchy: true, routed: false), "healthy unit alive");
        TestHarness.AssertFalse(ArmyEvidenceBuilder.IsAliveCombatUnit(0, true, false), "zero strength dead");
        TestHarness.AssertFalse(ArmyEvidenceBuilder.IsAliveCombatUnit(400, false, false), "inactive (not yet arrived / left field) excluded");
        TestHarness.AssertFalse(ArmyEvidenceBuilder.IsAliveCombatUnit(400, true, true), "routed excluded");
    }

    // ---- T2 ETA ----

    private static void ScheduledArrivalDurationIsHours()
    {
        // BattleUnits.CheckReinforcementArrivals (87562-87584): durationleft -=
        // (currenttimefromstart - lastreinforcementarrivalcheck), both in game hours.
        TestHarness.AssertEqual(10f, ArmyEvidenceBuilder.ArrivalHoursFromDurationLeft(10f), "10 game-hours stays 10 hours");
        TestHarness.AssertEqual(0f, ArmyEvidenceBuilder.ArrivalHoursFromDurationLeft(-2f), "negative clamps to 0");
        TestHarness.AssertEqual(0f, ArmyEvidenceBuilder.ArrivalHoursFromDurationLeft(float.NaN), "NaN clamps to 0");
    }
}
