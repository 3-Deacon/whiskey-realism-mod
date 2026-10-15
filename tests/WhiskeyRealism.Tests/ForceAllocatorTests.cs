using System;
using System.Collections.Generic;
using System.Text;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Util;

using static TestHarness;
using static TestFixtures;

// Stage G slice G4 (docs/superpowers/plans/2026-10-15-campaign-planner.md § G4): the force
// allocator (roles, withholding, reserve, shortfalls, W&L player classes, recon assignment), the home
// theater prior, the non-compliance counter and the runtime pins. Most cases run the real G3
// TheaterPlanner.Plan first, so the mass, the capital floor and the published hold need the allocator
// reads are the planner's own; the isolated rules use a hand-built plan.
internal static class ForceAllocatorTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("RemainderGoesToMainEffort", RemainderGoesToMainEffort),
            ("MainEffortIsExactlyThePlannedMass", MainEffortIsExactlyThePlannedMass),
            ("EveryFormationGetsExactlyOneAssignment", EveryFormationGetsExactlyOneAssignment),
            ("BusyFormationsKeepHold", BusyFormationsKeepHold),
            ("PlayerCommandNeverAllocatedAMove", PlayerCommandNeverAllocatedAMove),
            ("CapitalGuardFilledFirstUpToFloor", CapitalGuardFilledFirstUpToFloor),
            ("CapitalGuardsAreNeverInThePlannedMass", CapitalGuardsAreNeverInThePlannedMass),
            ("HoldSectorKeepsHoldRatio", HoldSectorKeepsHoldRatio),
            ("HoldNeedIsThePlansHoldNeed", HoldNeedIsThePlansHoldNeed),
            ("TempoHoldModifierRaisesHoldNeed", TempoHoldModifierRaisesHoldNeed),
            ("NullTempoIsNeutral", NullTempoIsNeutral),
            ("EconomyOfForceKeepsMinimumGarrison", EconomyOfForceKeepsMinimumGarrison),
            ("HoldReleasedIntoTheMassMasses", HoldReleasedIntoTheMassMasses),
            ("HoldWithheldWhileSectorAtNeed", HoldWithheldWhileSectorAtNeed),
            ("HoldReleasedAboveNeed", HoldReleasedAboveNeed),
            ("ImmobileFormationsCountButAreNeverWithheld", ImmobileFormationsCountButAreNeverWithheld),
            ("AlwaysWithheldRoles", AlwaysWithheldRoles),
            ("DepotBuilderIsSmallestFormationInSector", DepotBuilderIsSmallestFormationInSector),
            ("DepotBuilderTakenFromOutsideTheMassWhileOneQualifies", DepotBuilderTakenFromOutsideTheMassWhileOneQualifies),
            ("RecoverUsesRawSupplyNotVanillaCap", RecoverUsesRawSupplyNotVanillaCap),
            ("RecoverIsPerFormationAndWalksToSupply", RecoverIsPerFormationAndWalksToSupply),
            ("ReserveIsSurplusOverRequiredRatio", ReserveIsSurplusOverRequiredRatio),
            ("NoReserveWithoutBelievedEnemy", NoReserveWithoutBelievedEnemy),
            ("CarryingGroupIsMovable", CarryingGroupIsMovable),
            ("MapGroupPlayerNeverSoleMustHold", MapGroupPlayerNeverSoleMustHold),
            ("MapGroupPlayerDiscountedByReliability", MapGroupPlayerDiscountedByReliability),
            ("MapGroupPlayerCoveredAfterNonComplianceDays", MapGroupPlayerCoveredAfterNonComplianceDays),
            ("NonComplianceCounterCountsConsecutiveUnreliableDays", NonComplianceCounterCountsConsecutiveUnreliableDays),
            ("CicPlayerYieldsEmptyAllocation", CicPlayerYieldsEmptyAllocation),
            ("HomeTheaterBreaksTies", HomeTheaterBreaksTies),
            ("HomeTheaterPriorMatchesRegistry", HomeTheaterPriorMatchesRegistry),
            ("AllocationDeterministic", AllocationDeterministic),
            ("ReconAssignedBeforeSupportAndMainEffort", ReconAssignedBeforeSupportAndMainEffort),
            ("ReconPrefersCavalryFormations", ReconPrefersCavalryFormations),
            ("ReconPrefersFormationsOutsideTheMassWithinATier", ReconPrefersFormationsOutsideTheMassWithinATier),
            ("ReconNeverStripsMainEffortScreen", ReconNeverStripsMainEffortScreen),
            ("ReconTaskWithoutFormationLeftUnassigned", ReconTaskWithoutFormationLeftUnassigned),
            ("ReconAssignmentCarriesTaskSectorAndHoldPoint", ReconAssignmentCarriesTaskSectorAndHoldPoint),
            ("AllocatorReportsShortfallAgainstRoleNeed", AllocatorReportsShortfallAgainstRoleNeed),
            ("ShortfallsListWorstFirst", ShortfallsListWorstFirst),
            ("SurplusNeverFromMainEffortOrCapitalGuard", SurplusNeverFromMainEffortOrCapitalGuard),
            ("PlayerFormationNeverShortfallTarget", PlayerFormationNeverShortfallTarget),
            ("SupportFollowsTheSupportSectorsTarget", SupportFollowsTheSupportSectorsTarget),
            ("AllianceTwoAndEmptyInputsYieldEmptyAllocation", AllianceTwoAndEmptyInputsYieldEmptyAllocation),
            ("AllocationSummaryNamesTheRoles", AllocationSummaryNamesTheRoles),
            ("NearestSourceIgnoresRangeAndInvalidSources", NearestSourceIgnoresRangeAndInvalidSources),
            ("ReserveRulesAreSharedWithThePlanner", ReserveRulesAreSharedWithThePlanner),
            ("AllocatorIsPureAndFogHonestBySource", AllocatorIsPureAndFogHonestBySource),
            ("AllocationStepPinned", AllocationStepPinned),
            ("AllocationPublishesAtomicallyWithThePlan", AllocationPublishesAtomicallyWithThePlan),
        });
    }

    private static readonly int Day = ReviewCadenceCatchUp.DaySerial(15, 6, 1861);

    // ---- the main effort and the mass ----

    // Three corps and a main target, no believed enemy anywhere: nothing to hold, no reserve to carve,
    // so every movable formation goes to the main effort (spec component 6: "the remainder goes to the main effort").
    private static void RemainderGoesToMainEffort()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(2, 350f, 15000f));
        s.Own.Add(Corps(3, 550f, 10000f));
        var plan = PlanOf(s);
        AssertEqual(98, plan.MainObjectiveId, "main effort at x 100");
        var a = Alloc(plan, s);
        var sectors = Sectors(s);
        foreach (int id in new[] { 1, 2, 3 })
        {
            var x = Of(a, id);
            AssertEqual(FormationRole.MainEffort, x.Role, "corps " + id + " is the main effort");
            AssertEqual(plan.MainSectorId, x.SectorId, "corps " + id + " main sector");
            AssertNear(100f, x.TargetX, 0.01f, "corps " + id + " target x");
            AssertFalse(x.Withheld, "the main effort is not withheld");
            AssertEqual(ForceAllocator.ReasonMass, x.Reason, "reason");
        }
        AssertEqual(sectors.SectorAt(100f, 0f), plan.MainSectorId, "the main sector is the target's");
    }

    // G3 verify r1 F4/F9: the odds were judged on TheaterPlan.MassInstanceIds, so MainEffort is exactly that set.
    private static void MainEffortIsExactlyThePlannedMass()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(2, 350f, 15000f));
        var far = Corps(3, 20000f, 9000f); // beyond MarchHorizonDays: not massed, not a main-effort formation
        far.UseRails = false;
        s.Own.Add(far);
        var busy = Corps(4, 210f, 12000f);
        busy.Flags |= FormationFlags.InBattle;
        s.Own.Add(busy);
        var tired = Corps(5, 220f, 8000f);
        tired.Supply01 = 0.2f;
        s.Own.Add(tired);
        var plan = PlanOf(s);
        var a = Alloc(plan, s);
        var main = new List<int>();
        foreach (var x in a.Assignments)
            if (x.Role == FormationRole.MainEffort) main.Add(x.InstanceId);
        main.Sort();
        var mass = new List<int>(plan.MassInstanceIds);
        mass.Sort();
        AssertEqual("1,2", string.Join(",", mass), "the planner massed corps 1 and 2");
        AssertEqual(string.Join(",", mass), string.Join(",", main), "MainEffort is exactly the planned mass");
        AssertEqual(FormationRole.Hold, Of(a, 3).Role, "the formation beyond the march horizon is not the main effort's");
    }

    private static void EveryFormationGetsExactlyOneAssignment()
    {
        var s = Wide();
        for (int i = 1; i <= 9; i++) s.Own.Add(Corps(i, 50f * i, 6000f + 1000f * i));
        s.Own[3] = WithFlags(s.Own[3], FormationFlags.InBattle);
        s.Beliefs.Add(Belief(600f, 0f, 10000f));
        var plan = PlanOf(s);
        var a = Alloc(plan, s);
        AssertEqual(s.Own.Count, a.Assignments.Count, "one assignment per own formation");
        var seen = new HashSet<int>();
        foreach (var x in a.Assignments)
        {
            AssertTrue(seen.Add(x.InstanceId), "formation " + x.InstanceId + " appears once");
            AssertTrue(x.Reason != null, "every assignment carries a reason");
            if (x.Role != FormationRole.Recon) AssertEqual(-1, x.ReconMissionId, "no recon mission id outside Recon");
        }
        AssertEqual(-1, Of(a, 1).ReconMissionId, "ReconMissionId is explicitly -1 (the struct default 0 is a mission id)");
    }

    // ---- immobile formations ----

    private static void BusyFormationsKeepHold()
    {
        var flags = new[] { FormationFlags.InBattle, FormationFlags.OnRetreat, FormationFlags.RaidForce, FormationFlags.SeaInvasion,
            FormationFlags.Embarked, FormationFlags.FortBuilder, FormationFlags.VanillaDepotBuilder, FormationFlags.InTransfer };
        foreach (var flag in flags)
        {
            var s = Wide();
            s.Own.Add(Corps(1, 200f, 20000f));
            s.Own.Add(WithFlags(Corps(2, 350f, 15000f), flag));
            var plan = PlanOf(s);
            var a = Alloc(plan, s);
            var x = Of(a, 2);
            AssertEqual(FormationRole.Hold, x.Role, flag + " keeps Hold");
            AssertEqual(ForceAllocator.ReasonBusy, x.Reason, flag + " reason");
            AssertNear(350f, x.TargetX, 0.01f, flag + " stays put (target = its own position)");
            AssertFalse(x.Withheld, flag + " is not Whiskey's to steer");
            AssertEqual(Sectors(s).SectorAt(350f, 0f), x.SectorId, flag + " keeps its current sector");
            AssertEqual(FormationRole.MainEffort, Of(a, 1).Role, "the free corps is still the main effort");
        }
        var s2 = Wide();
        var hq = Corps(1, 200f, 3000f);
        hq.Flags = FormationFlags.None; // not a fighting force
        s2.Own.Add(hq);
        s2.Own.Add(Corps(2, 300f, 20000f));
        var a2 = Alloc(PlanOf(s2), s2);
        AssertEqual(ForceAllocator.ReasonNonCombat, Of(a2, 1).Reason, "a non-combat group stays where it is");
        AssertEqual(FormationRole.Hold, Of(a2, 1).Role, "non-combat Hold");
    }

    private static void PlayerCommandNeverAllocatedAMove()
    {
        foreach (var chain in new[] { WlChainClass.PlayerCommand, WlChainClass.CicSide })
        {
            var s = Wide();
            s.Own.Add(Corps(1, 200f, 20000f));
            var mine = Corps(2, 350f, 15000f);
            mine.Chain = chain;
            s.Own.Add(mine);
            s.Beliefs.Add(Belief(360f, 0f, 9000f));
            var plan = PlanOf(s);
            var a = Alloc(plan, s);
            var x = Of(a, 2);
            AssertEqual(FormationRole.Hold, x.Role, chain + ": only Hold");
            AssertEqual(ForceAllocator.ReasonPlayerCommand, x.Reason, chain + ": reason");
            AssertNear(350f, x.TargetX, 0.01f, chain + ": target is its own position");
            AssertFalse(x.Withheld, chain + ": never withheld");
            AssertFalse(a.Shortfalls.Contains(2) || a.Surpluses.Contains(2), chain + ": never a reinforcement or transfer target");
        }
    }

    // ---- capital ----

    // A believed army at x 20 against a capital at x 0: floor = 40000 / 20^0.3 x 0.5 = 8142. The nearest
    // massable formations fill it: corps 3 (3000) then corps 2 (15000); corps 1 stays the main effort.
    private static void CapitalGuardFilledFirstUpToFloor()
    {
        var s = Line(0, 5);
        s.Objectives.Add(Target(99, 300f, 1f));
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(2, 100f, 15000f));
        s.Own.Add(Corps(3, 50f, 3000f));
        s.Beliefs.Add(Belief(20f, 0f, 40000f));
        var plan = PlanOf(s);
        AssertNear(8142f, plan.CapitalFloorStrength, 5f, "capital floor on beliefs");
        var a = Alloc(plan, s);
        foreach (int id in new[] { 3, 2 })
        {
            var x = Of(a, id);
            AssertEqual(FormationRole.CapitalGuard, x.Role, "corps " + id + " guards the capital");
            AssertTrue(x.Withheld, "capital guards are withheld");
            AssertNear(0f, x.TargetX, 0.01f, "held at the capital (x)");
            AssertNear(0f, x.TargetZ, 0.01f, "held at the capital (z)");
            AssertEqual(ForceAllocator.ReasonCapitalFloor, x.Reason, "reason");
        }
        AssertEqual(FormationRole.MainEffort, Of(a, 1).Role, "the rest masses");
    }

    private static void CapitalGuardsAreNeverInThePlannedMass()
    {
        var s = Line(0, 5);
        s.Objectives.Add(Target(99, 300f, 1f));
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(2, 100f, 15000f));
        s.Beliefs.Add(Belief(20f, 0f, 40000f));
        var plan = PlanOf(s);
        var a = Alloc(plan, s);
        foreach (var x in a.Assignments)
            if (x.Role == FormationRole.CapitalGuard)
                AssertFalse(plan.MassInstanceIds.Contains(x.InstanceId), "guard " + x.InstanceId + " is outside the mass: the planner and the allocator pick the same reserve");
    }

    // ---- hold sectors ----

    // Sector 2 (x 500..600) faces a believed 10000 at x 600: HoldNeed 6000. Corps 4 (8000) holds it,
    // withheld; the free corps masses.
    private static void HoldSectorKeepsHoldRatio()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(4, 550f, 8000f));
        s.Beliefs.Add(Belief(600f, 0f, 10000f));
        var plan = PlanOf(s);
        var sectors = Sectors(s);
        int far = sectors.SectorAt(550f, 0f);
        AssertNear(6000f, HoldNeedOf(plan, far), 0.01f, "published hold need (0.6 x 10000)");
        var a = Alloc(plan, s);
        var x = Of(a, 4);
        AssertEqual(FormationRole.Hold, x.Role, "corps 4 holds its sector");
        AssertEqual(far, x.SectorId, "in its own sector");
        AssertEqual(ForceAllocator.ReasonHoldReserve, x.Reason, "reason");
        AssertTrue(x.Withheld, "withheld from vanilla's offensive gather while its sector is at need");
        AssertEqual(FormationRole.MainEffort, Of(a, 1).Role, "the free corps masses");
    }

    // G3 verify F9: the hold need is SectorPlan.HoldNeed as published, never recomputed from beliefs.
    private static void HoldNeedIsThePlansHoldNeed()
    {
        foreach (float published in new[] { 3000f, 9000f })
        {
            var s = Wide();
            s.Own.Add(Corps(7, 560f, 6000f));
            s.Own.Add(Corps(8, 540f, 6000f));
            s.Beliefs.Add(Belief(300f, 0f, 200000f)); // an enormous believed army elsewhere must not matter
            var plan = ManualPlan(s);
            plan.Sectors.Add(new SectorPlan { SectorId = Sectors(s).SectorAt(550f, 0f), Posture = TheaterPosture.Hold, TargetX = 550f, TargetZ = 0f, HoldNeed = published });
            var a = Alloc(plan, s);
            // The sector faces the nearest believed army (x 300, whatever its strength): corps 8 (540) is nearer it than corps 7 (560).
            AssertTrue(Of(a, 8).Withheld, "corps 8 withheld at need " + published);
            if (published < 5000f) AssertFalse(Of(a, 7).Withheld, "need 3000: corps 8 alone keeps the max(need, garrison) 5000, corps 7 is released");
            else
            {
                AssertTrue(Of(a, 7).Withheld, "need 9000: both are needed");
                AssertEqual(ForceAllocator.ReasonHoldReserve, Of(a, 7).Reason, "both reserved by the published need");
            }
        }
    }

    // A sector's hold is released for a target its own threat sits at (attacking that army is holding against
    // it): the main target is in the threatened sector, so the corps that G3's hold reserve picked there masses.
    private static void HoldReleasedIntoTheMassMasses()
    {
        var s = Wide();
        s.Objectives.Clear();
        s.Objectives.Add(Target(98, 100f, 0f));
        s.Objectives.Add(Target(99, 300f, 0f));
        s.Objectives.Add(Target(97, 500f, 1f));
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(4, 550f, 8000f));
        s.Beliefs.Add(Belief(600f, 0f, 10000f));
        var plan = PlanOf(s);
        AssertEqual(97, plan.MainObjectiveId, "the main target stands in the threatened sector");
        AssertTrue(plan.MassInstanceIds.Contains(4), "the planner massed the sector's own hold for its own target");
        var x = Of(Alloc(plan, s), 4);
        AssertEqual(FormationRole.MainEffort, x.Role, "so the allocator makes it the main effort, not Hold");
        AssertFalse(x.Withheld, "and does not withhold it");
    }

    // Raising the director's MinimumHoldRatioModifier raises the plan's HoldNeed (G3) and so the
    // allocator's hold: with 0 the second corps masses, with +0.2 (8000 needed) it holds.
    private static void TempoHoldModifierRaisesHoldNeed()
    {
        int Role8(DirectorPosture tempo)
        {
            var s = Wide();
            s.Reset(0, 0, Day, PlayerCommandClass.NotCareer, -1, true, tempo);
            s.RelocationRange = 150f;
            s.OffensiveDominance = 1.1f;
            s.MinStrengthForMovement = 1000f;
            s.MinMoraleForMovement = 0.3f;
            s.HasCapital = true;
            for (int i = 0; i < 7; i++)
                s.Areas.Add(new AreaRecord { Index = 10 + i, X = i * 100f, Z = 0f, EdgeX = new[] { i * 100f }, EdgeZ = new[] { 0f }, Importance = 1f, SupplyCapacity = 100f });
            s.Supply.Add(Iip(501, 100f, 0));
            s.Supply.Add(Iip(503, 300f, 0));
            s.Supply.Add(Iip(505, 500f, 0));
            s.Objectives.Add(Target(98, 100f, 1f));
            s.Objectives.Add(Target(99, 300f, 0f));
            s.Objectives.Add(Target(97, 500f, 0f));
            s.Own.Add(Corps(1, 200f, 20000f));
            s.Own.Add(Corps(7, 560f, 7000f));
            s.Own.Add(Corps(8, 540f, 3000f));
            s.Beliefs.Add(Belief(600f, 0f, 10000f));
            var plan = PlanOf(s);
            return (int)Of(Alloc(plan, s), 8).Role;
        }
        AssertEqual((int)FormationRole.MainEffort, Role8(new DirectorPosture { MinimumHoldRatioModifier = 0f }), "0.6 x 10000: corps 7 holds alone, corps 8 masses");
        AssertEqual((int)FormationRole.Hold, Role8(new DirectorPosture { MinimumHoldRatioModifier = 0.2f }), "0.8 x 10000: both hold");
    }

    private static void NullTempoIsNeutral()
    {
        var s = Wide();
        AssertTrue(s.Tempo == null, "fixture has no director posture (first review)");
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(2, 350f, 15000f));
        var plan = PlanOf(s);
        var a = Alloc(plan, s);
        AssertEqual(2, a.Assignments.Count, "allocates without a director posture");
        AssertEqual(FormationRole.MainEffort, Of(a, 1).Role, "neutral tempo masses as usual");
    }

    // Three 3000 divisions in sector 2 with no main effort and no believed enemy: the sector keeps
    // max(0, MinGarrisonStrength 5000): the two formations that reach it are withheld, the third is not.
    private static void EconomyOfForceKeepsMinimumGarrison()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 520f, 3000f));
        s.Own.Add(Corps(2, 550f, 3000f));
        s.Own.Add(Corps(3, 580f, 3000f));
        var plan = ManualPlan(s);
        var a = Alloc(plan, s);
        foreach (int id in new[] { 1, 2, 3 })
        {
            AssertEqual(FormationRole.Hold, Of(a, id).Role, "corps " + id + " garrisons its sector");
            AssertEqual(ForceAllocator.ReasonGarrison, Of(a, id).Reason, "garrison reason");
        }
        AssertTrue(Of(a, 2).Withheld, "the formation on the centroid is withheld first");
        AssertTrue(Of(a, 1).Withheld, "the second reaches 5000 (6000)");
        AssertFalse(Of(a, 3).Withheld, "the third is beyond the minimum garrison");
        AssertEqual("3", string.Join(",", a.Surpluses), "an EconomyOfForce formation beyond its garrison is a surplus");
    }

    private static void HoldWithheldWhileSectorAtNeed()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 540f, 4000f));
        s.Own.Add(Corps(2, 560f, 4000f));
        s.Own.Add(Corps(3, 590f, 4000f));
        s.Beliefs.Add(Belief(600f, 0f, 30000f)); // need 18000 but only 12000 stands there
        var plan = ManualPlan(s);
        plan.Sectors.Add(new SectorPlan { SectorId = Sectors(s).SectorAt(550f, 0f), Posture = TheaterPosture.Screen, HoldNeed = 18000f });
        var a = Alloc(plan, s);
        foreach (int id in new[] { 1, 2, 3 })
            AssertTrue(Of(a, id).Withheld, "all three are needed and withheld (corps " + id + ")");
    }

    // Four 5000 corps hold a sector that needs 8000: the prefix is the two nearest the army (10000 >= 8000);
    // the other two are released for vanilla.
    private static void HoldReleasedAboveNeed()
    {
        var s = Wide();
        for (int i = 0; i < 4; i++) s.Own.Add(Corps(i + 1, 560f - 10f * i, 5000f));
        var plan = ManualPlan(s);
        plan.Sectors.Add(new SectorPlan { SectorId = Sectors(s).SectorAt(550f, 0f), Posture = TheaterPosture.Hold, HoldNeed = 8000f });
        s.Beliefs.Add(Belief(600f, 0f, 1000f)); // the army the sector faces: corps nearest it hold
        var a = Alloc(plan, s);
        // Reserve picks the nearest to the army (x 600) until 8000: corps 1 (560), corps 2 (550); corps 3, 4 are the remainder.
        AssertTrue(Of(a, 1).Withheld && Of(a, 2).Withheld, "the two nearest the army are withheld");
        AssertFalse(Of(a, 3).Withheld || Of(a, 4).Withheld, "the rest are not withheld: vanilla may take them");
        AssertEqual(FormationRole.Hold, Of(a, 3).Role, "still Hold in place");
    }

    // What holds the sector anyway (a corps that cannot be massed) counts first and is never withheld:
    // 3000 busy + the first mobile 3000 reach the 5000 garrison; the second mobile corps is released.
    // With 6000 busy, no mobile corps is needed at all.
    private static void ImmobileFormationsCountButAreNeverWithheld()
    {
        var s = Wide();
        s.Own.Add(WithFlags(Corps(1, 550f, 3000f), FormationFlags.InBattle));
        s.Own.Add(Corps(2, 540f, 3000f));
        s.Own.Add(Corps(3, 560f, 3000f));
        var a = Alloc(ManualPlan(s), s);
        AssertFalse(Of(a, 1).Withheld, "the immobile corps is not withheld");
        AssertTrue(Of(a, 2).Withheld, "3000 held anyway + 3000 reaches the garrison");
        AssertFalse(Of(a, 3).Withheld, "...so the second mobile corps is released");

        var t = Wide();
        t.Own.Add(WithFlags(Corps(1, 550f, 6000f), FormationFlags.InBattle));
        t.Own.Add(Corps(2, 540f, 3000f));
        var b = Alloc(ManualPlan(t), t);
        AssertFalse(Of(b, 2).Withheld, "6000 held anyway already exceeds 5000: no mobile corps is needed");
    }

    // ---- withheld roles, depot builders, recover, reserve ----

    private static void AlwaysWithheldRoles()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 50f, 12000f));  // capital guard (the formation nearest the capital)
        s.Own.Add(Corps(2, 350f, 6000f));  // depot builder in sector 1
        var hungry = Corps(3, 420f, 7000f);
        hungry.Supply01 = 0.1f;            // recover
        s.Own.Add(hungry);
        s.Own.Add(Corps(4, 200f, 9000f));  // reserve
        s.Own.Add(Corps(5, 100f, 9000f));  // main body
        s.Own.Add(Corps(6, 250f, 4000f));  // recon
        s.Own.Add(Corps(8, 120f, 9000f));  // main body
        s.Beliefs.Add(Belief(110f, 0f, 10000f));
        var plan = ManualPlan(s);
        plan.CapitalFloorStrength = 1000f;
        int mainSector = Sectors(s).SectorAt(100f, 0f);
        plan.MainSectorId = mainSector;
        plan.MainObjectiveId = 98;
        plan.MainRequiredRatio = 1.1f;
        plan.Sectors.Add(new SectorPlan { SectorId = mainSector, Posture = TheaterPosture.MainEffort, TargetObjectiveId = 98, TargetX = 100f, TargetZ = 0f });
        plan.MassInstanceIds.AddRange(new[] { 4, 5, 8 });
        plan.DepotTasks.Add(new DepotTask { X = 300f, Z = 0f, SectorId = Sectors(s).SectorAt(350f, 0f), Priority = 1f });
        var tasks = new List<ReconTask> { new ReconTask { Kind = ReconKind.Screen, SectorId = mainSector, FromX = 150f, FromZ = 0f, HoldX = 130f, HoldZ = 0f, Priority = 1f, Reason = "t" } };
        var a = Alloc(plan, s, recon: tasks);
        var seen = new HashSet<FormationRole>();
        foreach (var x in a.Assignments)
        {
            switch (x.Role)
            {
                case FormationRole.CapitalGuard:
                case FormationRole.DepotBuilder:
                case FormationRole.Recover:
                case FormationRole.Recon:
                case FormationRole.Reserve:
                    AssertTrue(x.Withheld, x.Role + " is always withheld");
                    seen.Add(x.Role);
                    break;
                case FormationRole.MainEffort:
                case FormationRole.Support:
                    AssertFalse(x.Withheld, x.Role + " is never withheld");
                    break;
            }
        }
        AssertEqual(5, seen.Count, "the scenario exercised all five always-withheld roles: " + string.Join(",", seen));
    }

    private static void DepotBuilderIsSmallestFormationInSector()
    {
        var s = Wide();
        s.Own.Add(Corps(300, 330f, 9000f));
        s.Own.Add(Corps(301, 380f, 4000f));
        s.Own.Add(Corps(302, 350f, 6000f));
        s.Own.Add(Corps(303, 100f, 1500f)); // smaller, but in another sector
        var plan = ManualPlan(s);
        int sector = Sectors(s).SectorAt(350f, 0f);
        plan.DepotTasks.Add(new DepotTask { X = 300f, Z = 0f, SectorId = sector, Priority = 1f });
        var a = Alloc(plan, s);
        var x = Of(a, 301);
        AssertEqual(FormationRole.DepotBuilder, x.Role, "the smallest formation of the task's sector");
        AssertEqual(sector, x.SectorId, "sector");
        AssertNear(300f, x.TargetX, 0.01f, "the depot site");
        AssertTrue(x.Withheld, "withheld");
        AssertEqual(ForceAllocator.ReasonDepotTask, x.Reason, "reason");
        AssertTrue(Of(a, 303).Role != FormationRole.DepotBuilder, "another sector's formation is never the builder");
    }

    private static void DepotBuilderTakenFromOutsideTheMassWhileOneQualifies()
    {
        var s = Wide();
        s.Own.Add(Corps(301, 380f, 4000f));
        s.Own.Add(Corps(302, 350f, 6000f));
        var plan = ManualPlan(s);
        int sector = Sectors(s).SectorAt(350f, 0f);
        int main = Sectors(s).SectorAt(100f, 0f);
        plan.MainSectorId = main;
        plan.Sectors.Add(new SectorPlan { SectorId = main, Posture = TheaterPosture.MainEffort, TargetX = 100f, TargetZ = 0f });
        plan.MassInstanceIds.Add(301);
        plan.DepotTasks.Add(new DepotTask { X = 300f, Z = 0f, SectorId = sector, Priority = 1f });
        var a = Alloc(plan, s);
        AssertEqual(FormationRole.DepotBuilder, Of(a, 302).Role, "302 is outside the mass and qualifies: it builds although 301 is smaller");
        AssertEqual(FormationRole.MainEffort, Of(a, 301).Role, "the massed formation stays in the main effort");

        plan.MassInstanceIds.Add(302);
        var b = Alloc(plan, s);
        AssertEqual(FormationRole.DepotBuilder, Of(b, 301).Role, "with the whole sector in the mass, the smallest builds");
        AssertEqual(FormationRole.MainEffort, Of(b, 302).Role, "the main effort loses only that formation");
    }

    // Raw 0.45 is not Recover (vanilla's halved metric would call it 0.225); raw 0.35 is.
    private static void RecoverUsesRawSupplyNotVanillaCap()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        var fine = Corps(2, 250f, 8000f);
        fine.Supply01 = 0.45f;
        s.Own.Add(fine);
        var starved = Corps(3, 260f, 8000f);
        starved.Supply01 = 0.35f;
        s.Own.Add(starved);
        var plan = PlanOf(s);
        var a = Alloc(plan, s);
        AssertEqual(FormationRole.MainEffort, Of(a, 2).Role, "raw 0.45 is not Recover");
        AssertEqual(FormationRole.Recover, Of(a, 3).Role, "raw 0.35 is Recover");
        AssertEqual(ForceAllocator.ReasonRecover, Of(a, 3).Reason, "reason");

        // The allocator's own test, on formations the mass never counted (beyond the march horizon).
        var t = Wide();
        t.Own.Add(Corps(1, 200f, 20000f));
        var farFine = Corps(2, 20000f, 8000f);
        farFine.Supply01 = 0.45f;
        farFine.UseRails = false;
        var farStarved = Corps(3, 20100f, 8000f);
        farStarved.Supply01 = 0.35f;
        farStarved.UseRails = false;
        t.Own.Add(farFine);
        t.Own.Add(farStarved);
        var b = Alloc(PlanOf(t), t);
        AssertTrue(b.TryGet(2, out var farRole) && farRole.Role != FormationRole.Recover, "raw 0.45 outside the mass is not Recover either");
        AssertEqual(FormationRole.Recover, Of(b, 3).Role, "raw 0.35 outside the mass is");
    }

    private static void RecoverIsPerFormationAndWalksToSupply()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        var demoralised = Corps(2, 350f, 8000f);
        demoralised.Morale = 0.1f; // below aiminimummoraleformovement (0.3)
        s.Own.Add(demoralised);
        var hungry = Corps(3, 520f, 8000f);
        hungry.Supply01 = 0.1f;
        s.Own.Add(hungry);
        var plan = PlanOf(s);
        var a = Alloc(plan, s);
        var low = Of(a, 2);
        AssertEqual(FormationRole.Recover, low.Role, "low morale recovers even in a sector that is not Recover");
        AssertTrue(low.Withheld, "withheld");
        AssertNear(300f, low.TargetX, 0.01f, "walks to the nearest own supply source (IIP at x 300)");
        AssertNear(500f, Of(a, 3).TargetX, 0.01f, "the other walks to its nearest source (IIP at x 500)");
        s.Supply.Clear();
        AssertNear(520f, Of(Alloc(PlanOf(s), s), 3).TargetX, 0.01f, "with no source at all it stays put");
    }

    // Main target at x 100 faces a believed 10000 (required 1.1 x 10000 = 11000, keep 1.3 x = 14300).
    // Three 8000 corps mass: the farthest (350) is surplus (24000 - 8000 >= 14300); the next is not.
    private static void ReserveIsSurplusOverRequiredRatio()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 110f, 8000f));
        s.Own.Add(Corps(2, 200f, 8000f));
        s.Own.Add(Corps(3, 350f, 8000f));
        s.Beliefs.Add(Belief(120f, 0f, 10000f));
        var plan = MainPlan(s, new[] { 1, 2, 3 }, 100f);
        var a = Alloc(plan, s);
        AssertEqual(FormationRole.Reserve, Of(a, 3).Role, "the farthest surplus is Reserve");
        AssertTrue(Of(a, 3).Withheld, "reserve is withheld");
        AssertEqual(ForceAllocator.ReasonReserve, Of(a, 3).Reason, "reason");
        AssertEqual(FormationRole.MainEffort, Of(a, 1).Role, "the nearest stays");
        AssertEqual(FormationRole.MainEffort, Of(a, 2).Role, "16000 left is >= 14300: corps 2 is kept, and 8000 would not be");
        AssertEqual("3", string.Join(",", a.Surpluses), "the reserve is listed as a surplus");
    }

    private static void NoReserveWithoutBelievedEnemy()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 110f, 8000f));
        s.Own.Add(Corps(2, 200f, 8000f));
        s.Own.Add(Corps(3, 350f, 8000f));
        var plan = MainPlan(s, new[] { 1, 2, 3 }, 100f);
        var a = Alloc(plan, s);
        foreach (int id in new[] { 1, 2, 3 })
            AssertEqual(FormationRole.MainEffort, Of(a, id).Role, "an objective with no believed defender takes everything it can (corps " + id + ")");
        s.Beliefs.Add(Belief(500f, 0f, 90000f)); // a big army far from the target is not the target's defender
        foreach (int id in new[] { 1, 2, 3 })
            AssertEqual(FormationRole.MainEffort, Of(Alloc(MainPlan(s, new[] { 1, 2, 3 }, 100f), s), id).Role, "enemy elsewhere: still no reserve (corps " + id + ")");
    }

    // ---- W&L player classes (spec C4) ----

    private static void CarryingGroupIsMovable()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        var carrying = Corps(2, 350f, 15000f);
        carrying.Chain = WlChainClass.CarryingGroup;
        s.Own.Add(carrying);
        var plan = PlanOf(s);
        AssertTrue(plan.MassInstanceIds.Contains(2), "the planner counts the carrying group");
        AssertEqual(FormationRole.MainEffort, Of(Alloc(plan, s), 2).Role, "an ordinary movable formation: F's MoveUnitTo Postfix dispatches its march");
    }

    private static void MapGroupPlayerNeverSoleMustHold()
    {
        var s = Line(0, 5);
        s.Objectives.Add(Target(99, 300f, 1f));
        var player = Corps(1, 50f, 30000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        s.Own.Add(player);
        s.Own.Add(Corps(2, 250f, 12000f));
        s.Beliefs.Add(Belief(20f, 0f, 40000f)); // capital threat: floor 8142
        var plan = PlanOf(s);
        var a = Alloc(plan, s);
        AssertFalse(plan.MassInstanceIds.Contains(1), "the planner never masses the map-group player");
        var x = Of(a, 1);
        AssertTrue(x.Role != FormationRole.CapitalGuard && x.Role != FormationRole.MainEffort, "never a capital guard or main effort on his own");
        AssertEqual(FormationRole.CapitalGuard, Of(a, 2).Role, "the AI corps guards the capital: the floor does not depend on the player");
        AssertFalse(a.Shortfalls.Contains(1) || a.Surpluses.Contains(1), "never a reinforcement target");
    }

    // Reliability 1: the player's 8000 alone holds the 5000 garrison, the AI division is released.
    // Reliability 0.5: he counts 4000, so the division is withheld to reach 5000.
    private static void MapGroupPlayerDiscountedByReliability()
    {
        bool DivisionWithheld(float reliability)
        {
            var s = Wide();
            var player = Corps(1, 550f, 8000f);
            player.Chain = WlChainClass.PlayerMapGroup;
            s.Own.Add(player);
            s.Own.Add(Corps(2, 560f, 3000f));
            var a = Alloc(ManualPlan(s), s, reliability);
            AssertEqual(FormationRole.Hold, Of(a, 1).Role, "the map-group player holds his sector");
            AssertEqual(ForceAllocator.ReasonPlayerMapGroup, Of(a, 1).Reason, "reason");
            return Of(a, 2).Withheld;
        }
        AssertFalse(DivisionWithheld(1f), "a fully reliable player holds on his own");
        AssertTrue(DivisionWithheld(0.5f), "half reliable: 4000 + 3000 reaches the garrison");
    }

    // After NonComplianceDays he counts 0: the sector's AI formation is withheld to cover him and,
    // short of the garrison, is the shortfall carrier.
    private static void MapGroupPlayerCoveredAfterNonComplianceDays()
    {
        Allocation Run(int days)
        {
            var s = Wide();
            var player = Corps(1, 550f, 8000f);
            player.Chain = WlChainClass.PlayerMapGroup;
            s.Own.Add(player);
            s.Own.Add(Corps(2, 560f, 3000f));
            return Alloc(ManualPlan(s), s, 1f, days);
        }
        var before = Run(ForceAllocator.NonComplianceDays - 1);
        AssertFalse(Of(before, 2).Withheld, "four unreliable days: he still counts");
        AssertEqual(0, before.Shortfalls.Count, "no shortfall while he counts");
        var after = Run(ForceAllocator.NonComplianceDays);
        AssertTrue(Of(after, 2).Withheld, "five days: the AI division covers his sector");
        AssertEqual("2", string.Join(",", after.Shortfalls), "and, 3000 short of the garrison, carries the shortfall (not the player)");
    }

    private static void NonComplianceCounterCountsConsecutiveUnreliableDays()
    {
        var c = new ForceAllocator.PlayerNonCompliance();
        c.Observe(100, true, 0.4f);
        c.Observe(100, true, 0.4f);
        AssertEqual(1, c.Days, "the same review day counts once");
        c.Observe(101, true, 0.4f);
        c.Observe(102, true, 0.49f);
        AssertEqual(3, c.Days, "consecutive unreliable days");
        c.Observe(103, true, 0.5f);
        AssertEqual(0, c.Days, "reliability at the threshold (0.5) resets");
        c.Observe(104, true, 0.1f);
        c.Observe(105, false, 0.1f);
        AssertEqual(0, c.Days, "no map-group player this day resets");
        c.Observe(106, true, 0.1f);
        c.Observe(50, true, 0.1f);
        AssertEqual(1, c.Days, "a day that goes backwards (a load) restarts the count");
        c.Reset();
        AssertEqual(0, c.Days, "reset");
    }

    private static void CicPlayerYieldsEmptyAllocation()
    {
        CampaignSnapshot Side(int alliance, WlChainClass chain)
        {
            var s = Wide();
            s.Reset(alliance, alliance, Day, PlayerCommandClass.CommanderInChief, -1, true, null);
            s.RelocationRange = 150f;
            s.OffensiveDominance = 1.1f;
            s.MinStrengthForMovement = 1000f;
            s.MinMoraleForMovement = 0.3f;
            for (int i = 0; i < 7; i++)
                s.Areas.Add(new AreaRecord { Index = 10 + i, X = i * 100f, Z = 0f, EdgeX = new[] { i * 100f }, EdgeZ = new[] { 0f }, Importance = 1f, SupplyCapacity = 100f });
            var f = Corps(1, 200f, 20000f);
            f.Chain = chain;
            s.Own.Add(f);
            return s;
        }
        var own = Side(0, WlChainClass.CicSide);
        AssertEqual(0, ForceAllocator.Allocate(new TheaterPlan { Alliance = 0 }, own, Sectors(own), 1f, 0, null, null, null).Assignments.Count, "the CIC player's own side stands down");
        var enemy = Side(1, WlChainClass.None);
        enemy.Objectives.Add(new ObjectiveRecord { ObjectiveId = 1, X = 100f, Z = 0f, HolderAlliance = 0, Available = true, Importance01 = 1f });
        AssertEqual(1, ForceAllocator.Allocate(new TheaterPlan { Alliance = 1 }, enemy, Sectors(enemy), 1f, 0, null, null, null).Assignments.Count,
            "the enemy side of a CIC career keeps its allocation although the snapshot's Player class is global");
    }

    // ---- home theater ----

    private static void HomeTheaterBreaksTies()
    {
        bool NamedCorpsWithheld(bool useHome)
        {
            var s = Wide();
            var plain = Corps(7, 520f, 6000f);
            plain.Name = "1st Corps";
            var ohio = Corps(8, 580f, 6000f);
            ohio.Name = "Army of the Ohio";
            s.Own.Add(plain);
            s.Own.Add(ohio);
            var a = Alloc(ManualPlan(s), s, 1f, 0, useHome ? HomeTheaterPrior.ForAlliance(0) : null);
            AssertTrue(Of(a, 7).Withheld != Of(a, 8).Withheld, "exactly one of the two reaches the 5000 garrison (useHome " + useHome + ")");
            return Of(a, 8).Withheld;
        }
        AssertFalse(NamedCorpsWithheld(false), "equal distance: the lower instance id is withheld first");
        AssertTrue(NamedCorpsWithheld(true), "the Army of the Ohio (home West) sits in a West sector and is withheld first");
    }

    // Pinned against HistoricalArmyAreaRegistry until G6 deletes it: the home theater is the theater of the
    // doctrine's primary area (ArmyAreaClassifier key -> a point inside it -> TheaterClassifier).
    private static void HomeTheaterPriorMatchesRegistry()
    {
        var areaPoint = new Dictionary<string, float[]>
        {
            ["VirginiaCapitalCorridor"] = new[] { 1300f, -1000f },
            ["ShenandoahValley"] = new[] { 1087f, -580f },
            ["NorthwestVirginia"] = new[] { 575f, -650f },
            ["OhioValley"] = new[] { 0f, 0f },
            ["WashingtonDefenses"] = new[] { 1300f, -500f },
            ["MarylandPennsylvaniaCorridor"] = new[] { 1200f, -300f }
        };
        foreach (var pair in areaPoint)
            AssertEqual(pair.Key, ArmyAreaClassifier.FromPosition(pair.Value[0], pair.Value[1]), "the probe point lies in " + pair.Key);
        var names = new[] { "Army of the Potomac", "Army of Virginia", "Shenandoah Corps", "Army of the Ohio", "Army of Northern Virginia",
            "Army of the Valley", "Army of the Northwest", "Porterfield's Column", "Army of the Tennessee", "3rd Corps", "" };
        foreach (int alliance in new[] { 0, 1 })
            foreach (string name in names)
            {
                var doctrine = HistoricalArmyAreaRegistry.Resolve(alliance, name);
                var expected = doctrine.PrimaryAreaKey == "Unassigned" ? Theater.Unknown
                    : TheaterClassifier.FromPosition(areaPoint[doctrine.PrimaryAreaKey][0], areaPoint[doctrine.PrimaryAreaKey][1]);
                AssertEqual(expected, HomeTheaterPrior.For(alliance, name), "alliance " + alliance + " '" + name + "'");
            }
        AssertEqual(Theater.Unknown, HomeTheaterPrior.For(2, "Army of the Potomac"), "no home for alliance 2");
        AssertEqual(Theater.Unknown, HomeTheaterPrior.ForAlliance(5)("Army of the Potomac"), "no delegate for an unplanned alliance");
        AssertEqual(Theater.Unknown, HomeTheaterPrior.For(0, null), "null name");
    }

    // ---- determinism ----

    private static void AllocationDeterministic()
    {
        string Signature()
        {
            var s = Wide();
            for (int i = 1; i <= 8; i++) s.Own.Add(Corps(i, 40f * i + 100f, 4000f + 700f * i));
            s.Beliefs.Add(Belief(600f, 0f, 12000f));
            s.Beliefs.Add(Belief(30f, 0f, 15000f));
            var plan = PlanOf(s);
            return AllocationSignature(Alloc(plan, s));
        }
        AssertEqual(Signature(), Signature(), "same input, same allocation");
    }

    // ---- reconnaissance ----

    private static void ReconAssignedBeforeSupportAndMainEffort()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        var horse = Corps(2, 250f, 5000f);
        horse.CavalryShare = 0.9f;
        s.Own.Add(horse);
        var plan = MainPlan(s, new[] { 1, 2 }, 100f);
        var task = ScreenTask(250f, 0f, 150f, 0f, plan.MainSectorId);
        var a = Alloc(plan, s, recon: new List<ReconTask> { task });
        var x = Of(a, 2);
        AssertEqual(FormationRole.Recon, x.Role, "the cavalry group screens although the plan massed it");
        AssertEqual(FormationRole.MainEffort, Of(a, 1).Role, "the main effort is the mass less the screen");
        AssertTrue(x.Withheld, "withheld");
        AssertEqual("t", x.Reason, "the task's reason");
    }

    private static void ReconPrefersCavalryFormations()
    {
        var s = Wide();
        var horse = Corps(2, 250f, 5000f);
        horse.CavalryShare = 0.9f;
        s.Own.Add(horse);
        s.Own.Add(Corps(3, 240f, 3000f)); // smaller infantry, outside the mass
        var plan = MainPlan(s, new[] { 2 }, 100f);
        var a = Alloc(plan, s, recon: new List<ReconTask> { ScreenTask(250f, 0f, 150f, 0f, plan.MainSectorId) });
        AssertEqual(FormationRole.Recon, Of(a, 2).Role, "cavalry first, even from the mass and with infantry outside it");
        AssertTrue(Of(a, 3).Role != FormationRole.Recon, "the infantry is not used");
    }

    private static void ReconPrefersFormationsOutsideTheMassWithinATier()
    {
        var s = Wide();
        s.Own.Add(Corps(4, 200f, 8000f));
        s.Own.Add(Corps(5, 220f, 8000f));
        var plan = MainPlan(s, new[] { 4 }, 100f);
        var a = Alloc(plan, s, recon: new List<ReconTask> { ScreenTask(210f, 0f, 150f, 0f, plan.MainSectorId) });
        // ReconPlanner alone would take 4 (equal strength, nearer FromX... lower id): the mass is spared.
        AssertEqual(FormationRole.Recon, Of(a, 5).Role, "5 is outside the mass and in the same tier");
        AssertEqual(FormationRole.MainEffort, Of(a, 4).Role, "the mass is intact");
    }

    // Two screen tasks, the one for another sector first. Only one cavalry group stands in the main body: the
    // first task must not strip it; the main axis task (its own screen) takes it.
    private static void ReconNeverStripsMainEffortScreen()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        var horse = Corps(2, 210f, 5000f);
        horse.CavalryShare = 0.9f;
        s.Own.Add(horse);
        s.Own.Add(Corps(3, 400f, 6000f));
        var plan = MainPlan(s, new[] { 1, 2 }, 100f);
        int other = Sectors(s).SectorAt(350f, 0f);
        var tasks = new List<ReconTask>
        {
            ScreenTask(400f, 0f, 380f, 0f, other),
            ScreenTask(210f, 0f, 140f, 0f, plan.MainSectorId)
        };
        var a = Alloc(plan, s, recon: tasks);
        AssertEqual(FormationRole.Recon, Of(a, 3).Role, "the first task takes a formation that is not the main body's last cavalry");
        AssertNear(380f, Of(a, 3).TargetX, 0.01f, "...with that task's hold point");
        AssertEqual(FormationRole.Recon, Of(a, 2).Role, "the main axis task is the main effort's own screen: it takes the cavalry");
        AssertNear(140f, Of(a, 2).TargetX, 0.01f, "...with its hold point");
        AssertEqual(FormationRole.MainEffort, Of(a, 1).Role, "the main body remains");

        // Reversed order with two cavalry groups in the main body: once the main axis task has its screen
        // (corps 2), the other task may take the remaining cavalry (corps 4): the main effort has one.
        var t = Wide();
        t.Own.Add(Corps(1, 200f, 20000f));
        foreach (int id in new[] { 2, 4 })
        {
            var h = Corps(id, 200f + id, 5000f);
            h.CavalryShare = 0.9f;
            t.Own.Add(h);
        }
        var plan2 = MainPlan(t, new[] { 1, 2, 4 }, 100f);
        var b = Alloc(plan2, t, recon: new List<ReconTask>
        {
            ScreenTask(210f, 0f, 140f, 0f, plan2.MainSectorId),
            ScreenTask(400f, 0f, 380f, 0f, Sectors(t).SectorAt(350f, 0f))
        });
        AssertEqual(FormationRole.Recon, Of(b, 2).Role, "both cavalry groups screen");
        AssertEqual(FormationRole.Recon, Of(b, 4).Role, "the main effort already has its screen: the next task may take the other cavalry group");
        var targets = new List<float> { Of(b, 2).TargetX, Of(b, 4).TargetX };
        targets.Sort();
        AssertEqual("140,380", string.Join(",", targets), "one on each task's hold point");
    }

    private static void ReconTaskWithoutFormationLeftUnassigned()
    {
        var s = Wide();
        s.Own.Add(WithFlags(Corps(1, 200f, 20000f), FormationFlags.InBattle));
        var plan = ManualPlan(s);
        var a = Alloc(plan, s, recon: new List<ReconTask> { ScreenTask(200f, 0f, 150f, 0f, 0) });
        AssertEqual(1, a.Assignments.Count, "one assignment");
        AssertEqual(FormationRole.Hold, Of(a, 1).Role, "nobody could take the task: the busy corps keeps Hold");
    }

    // G1b contract: the Recon assignment copies the task's SectorId and hold point exactly, so
    // ReconMissionLedger.Sync can pair them back and write the mission id.
    private static void ReconAssignmentCarriesTaskSectorAndHoldPoint()
    {
        var s = Wide();
        var horse = Corps(2, 250f, 5000f);
        horse.CavalryShare = 0.9f;
        s.Own.Add(horse);
        var plan = ManualPlan(s);
        var task = ScreenTask(250f, 0f, 153.37f, 12.5f, 1);
        var tasks = new List<ReconTask> { task };
        var ledger = new ReconMissionLedger();
        var a = Alloc(plan, s, recon: tasks, missions: ledger);
        var x = Of(a, 2);
        AssertEqual(FormationRole.Recon, x.Role, "recon");
        AssertEqual(task.SectorId, x.SectorId, "task sector");
        AssertEqual(task.HoldX, x.TargetX, "hold x, bit for bit");
        AssertEqual(task.HoldZ, x.TargetZ, "hold z, bit for bit");
        AssertEqual(-1, x.ReconMissionId, "the mission id is the ledger's to write");
        ledger.Sync(s, tasks, a);
        AssertTrue(Of(a, 2).ReconMissionId > 0, "Sync paired the assignment with its task and wrote the mission id");
        AssertEqual(1, ledger.Missions.Count, "one mission");
    }

    // ---- shortfalls and surpluses ----

    // The main effort holds 25000 against a required 1.5 x 20000 = 30000.
    private static void AllocatorReportsShortfallAgainstRoleNeed()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 110f, 15000f));
        s.Own.Add(Corps(2, 300f, 10000f));
        s.Beliefs.Add(Belief(120f, 0f, 20000f));
        var plan = MainPlan(s, new[] { 1, 2 }, 100f);
        plan.MainRequiredRatio = 1.5f;
        var a = Alloc(plan, s);
        AssertEqual("1", string.Join(",", a.Shortfalls), "the main effort formation nearest the target carries the shortfall");

        var h = Wide();
        h.Own.Add(Corps(5, 560f, 3000f));
        var hold = ManualPlan(h);
        hold.Sectors.Add(new SectorPlan { SectorId = Sectors(h).SectorAt(550f, 0f), Posture = TheaterPosture.Screen, HoldNeed = 6000f });
        AssertEqual("5", string.Join(",", Alloc(hold, h).Shortfalls), "a Hold sector below max(need, garrison) lists its AI formation");

        var c = Line(0, 5);
        c.Own.Add(Corps(6, 50f, 4000f));
        c.Beliefs.Add(Belief(20f, 0f, 70000f)); // floor ~14250 > the only massable 4000
        var capital = ManualPlan(c);
        capital.CapitalFloorStrength = 10000f;
        var ca = Alloc(capital, c);
        AssertEqual(FormationRole.CapitalGuard, Of(ca, 6).Role, "the only formation guards");
        AssertEqual("6", string.Join(",", ca.Shortfalls), "...and is short of the floor");
    }

    private static void ShortfallsListWorstFirst()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 110f, 15000f));
        s.Own.Add(Corps(2, 300f, 10000f));
        s.Own.Add(Corps(5, 560f, 3000f));
        s.Beliefs.Add(Belief(120f, 0f, 20000f));
        var plan = MainPlan(s, new[] { 1, 2 }, 100f);
        plan.MainRequiredRatio = 1.5f;                                     // short 5000 of 30000: 0.167
        plan.Sectors.Add(new SectorPlan { SectorId = Sectors(s).SectorAt(550f, 0f), Posture = TheaterPosture.Screen, HoldNeed = 6000f }); // 3000 of 6000: 0.5
        var a = Alloc(plan, s);
        AssertEqual("5,1", string.Join(",", a.Shortfalls), "the emptier need first, the main effort after");
    }

    private static void SurplusNeverFromMainEffortOrCapitalGuard()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 110f, 8000f));
        s.Own.Add(Corps(2, 200f, 8000f));
        s.Own.Add(Corps(3, 350f, 8000f));
        s.Own.Add(Corps(4, 10f, 5000f));
        s.Own.Add(Corps(5, 520f, 3000f));
        s.Own.Add(Corps(6, 580f, 3000f));
        s.Own.Add(Corps(7, 550f, 3000f));
        s.Beliefs.Add(Belief(120f, 0f, 10000f));
        var plan = MainPlan(s, new[] { 1, 2, 3 }, 100f);
        plan.CapitalFloorStrength = 4000f;
        var a = Alloc(plan, s);
        AssertEqual(FormationRole.CapitalGuard, Of(a, 4).Role, "capital guard");
        AssertTrue(a.Surpluses.Count >= 2, "reserve and released garrison are surpluses");
        foreach (int id in a.Surpluses)
        {
            var x = Of(a, id);
            AssertTrue(x.Role != FormationRole.MainEffort && x.Role != FormationRole.CapitalGuard, "surplus " + id + " is " + x.Role);
        }
    }

    private static void PlayerFormationNeverShortfallTarget()
    {
        var s = Wide();
        var player = Corps(1, 560f, 2000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        s.Own.Add(player);
        var plan = ManualPlan(s);
        plan.Sectors.Add(new SectorPlan { SectorId = Sectors(s).SectorAt(550f, 0f), Posture = TheaterPosture.Screen, HoldNeed = 9000f });
        var a = Alloc(plan, s);
        AssertEqual(0, a.Shortfalls.Count, "the sector is short, but its only formation is the player's: no carrier");
    }

    private static void SupportFollowsTheSupportSectorsTarget()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(2, 350f, 6000f));
        var plan = ManualPlan(s);
        int main = Sectors(s).SectorAt(100f, 0f);
        int support = Sectors(s).SectorAt(350f, 0f);
        plan.MainSectorId = main;
        plan.MainObjectiveId = 98;
        plan.Sectors.Add(new SectorPlan { SectorId = main, Posture = TheaterPosture.MainEffort, TargetObjectiveId = 98, TargetX = 100f, TargetZ = 0f });
        plan.Sectors.Add(new SectorPlan { SectorId = support, Posture = TheaterPosture.Support, TargetObjectiveId = 98, TargetX = 100f, TargetZ = 0f });
        plan.MassInstanceIds.Add(1);
        var a = Alloc(plan, s);
        var x = Of(a, 2);
        AssertEqual(FormationRole.Support, x.Role, "outside the mass, in a Support sector");
        AssertNear(100f, x.TargetX, 0.01f, "toward the main target");
        AssertEqual(support, x.SectorId, "its own sector");
        AssertFalse(x.Withheld, "Support is vanilla's to use");
    }

    private static void AllianceTwoAndEmptyInputsYieldEmptyAllocation()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        var sectors = Sectors(s);
        var plan = ManualPlan(s);
        AssertEqual(0, ForceAllocator.Allocate(null, s, sectors, 1f, 0, null, null, null).Assignments.Count, "no plan");
        AssertEqual(0, ForceAllocator.Allocate(plan, null, sectors, 1f, 0, null, null, null).Assignments.Count, "no snapshot");
        AssertEqual(0, ForceAllocator.Allocate(plan, s, null, 1f, 0, null, null, null).Assignments.Count, "no sectors");
        var two = new CampaignSnapshot();
        two.Reset(2, 2, Day, PlayerCommandClass.NotCareer, -1, true, null);
        two.Own.Add(Corps(1, 200f, 20000f));
        AssertEqual(0, ForceAllocator.Allocate(plan, two, sectors, 1f, 0, null, null, null).Assignments.Count, "alliance 2");
        var none = Wide();
        AssertEqual(0, ForceAllocator.Allocate(plan, none, Sectors(none), 1f, 0, null, null, null).Assignments.Count, "no formations");
    }

    private static void AllocationSummaryNamesTheRoles()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 110f, 8000f));
        s.Own.Add(Corps(2, 200f, 8000f));
        s.Own.Add(Corps(3, 350f, 8000f));
        s.Own.Add(WithFlags(Corps(4, 560f, 4000f), FormationFlags.InBattle));
        s.Beliefs.Add(Belief(120f, 0f, 10000f));
        var plan = MainPlan(s, new[] { 1, 2, 3 }, 100f);
        plan.MassStrength = 24000f;
        var a = Alloc(plan, s);
        string row = ForceAllocator.Summary(a, plan, PlayerCommandClass.MapGroup, 0.756f, 3);
        AssertEqual("[Allocation] alliance=0 main=2 support=0 hold=0 reserve=1 capital=0 depot=0 recover=0 recon=0 busy=1 player=MapGroup reliability=0.76 unreliableDays=3 mass=24000 short=0 surplus=1", row,
            "the [Allocation] row");
    }

    private static void NearestSourceIgnoresRangeAndInvalidSources()
    {
        var s = Line(0, 5);
        s.Supply.Clear();
        s.Supply.Add(Iip(1, 100f, 1));                                   // enemy IIP: not a source
        var invisible = Iip(2, 150f, 0);
        invisible.Invisible = true;                                      // invisible IIP: not a source
        s.Supply.Add(invisible);
        s.Supply.Add(new SupplySourceRecord { Id = 3, Kind = SupplySourceKind.Depot, Owner = 0, X = 160f, Z = 0f, Condition = 0.9f, Built = false }); // unbuilt depot
        s.Supply.Add(new SupplySourceRecord { Id = 4, Kind = SupplySourceKind.Depot, Owner = 0, X = 170f, Z = 0f, Condition = 0.5f, Built = true });  // worn depot
        s.Supply.Add(Iip(5, 4000f, 0));                                  // far away, valid
        s.Supply.Add(new SupplySourceRecord { Id = 6, Kind = SupplySourceKind.Depot, Owner = 0, X = 3000f, Z = 0f, Condition = 0.8f, Built = true });
        AssertTrue(LogisticsGate.NearestSource(s, 0f, 0f, out float x, out float z), "a valid source exists beyond any range");
        AssertNear(3000f, x, 0.01f, "the nearest valid source, whatever the distance");
        AssertNear(0f, z, 0.01f, "z");
        s.Supply.Clear();
        AssertFalse(LogisticsGate.NearestSource(s, 0f, 0f, out _, out _), "none");
    }

    // ---- source pins ----

    // G3 verify r1 F4/F9: the planner subtracts the same capital and hold reserves the allocator hands out.
    private static void ReserveRulesAreSharedWithThePlanner()
    {
        string planner = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/TheaterPlanner.cs");
        string allocator = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ForceAllocator.cs");
        foreach (string call in new[] { "ReserveRules.Capital(", "ReserveRules.Hold(" })
        {
            AssertContains(planner, call, "the planner's reserve uses " + call);
            AssertContains(allocator, call, "the allocator's roles use " + call);
        }
        AssertContains(allocator, "TheaterPlanner.Movable(", "one movable predicate");
    }

    // The allocator is a pure function of the snapshot, plan and sectors: no clock, no randomness, no
    // mutable static, no Unity or vanilla type, and no enemy read except the plan's published numbers
    // and snapshot beliefs (never a destination, an omniscient list or a vanilla aggregate).
    private static void AllocatorIsPureAndFogHonestBySource()
    {
        foreach (string file in new[] { "ForceAllocator.cs", "HomeTheaterPrior.cs", "ReserveRules.cs" })
        {
            string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/" + file);
            foreach (string forbidden in new[] { "System.Random", "new Random(", "DateTime", "Environment.TickCount", "Time.realtimeSinceStartup", "UnityEngine",
                "GetLastTransmittedPathPos", "enemyunits", "closestenemyunitfar", "enemycampaignunitsstrength", "campaignunitlist", "Plugin.", "OnceLog" })
                AssertFalse(src.Contains(forbidden), file + " must not contain '" + forbidden + "'");
            var mutableStatic = new System.Text.RegularExpressions.Regex(@"\bstatic\s+(?!readonly\b|class\b)[\w<>\[\],\. ]+\s+_?\w+\s*(=|;)");
            AssertFalse(mutableStatic.IsMatch(src), file + " keeps no mutable static state");
        }
    }

    // The allocation step of ReviewPlan (plan G4 step 5): after the plan, before the publish that carries
    // it, inside the allocate scope, from the plan's own snapshot and sectors, with the live reliability and
    // the run of unreliable days; the recon tasks and ledger are G1b's runtime step (ReconStepPinned).
    private static void AllocationStepPinned()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string review = BodyOf(src, "internal static void ReviewPlan(");
        int bookkeeping = review.IndexOf("mode == PlannerReviewMode.Bookkeeping) return;", StringComparison.Ordinal);
        int plan = review.IndexOf("TheaterPlanner.Plan(", StringComparison.Ordinal);
        int scope = review.IndexOf("TelemetryPerf.Scope(\"campaign.planner.allocate\"", StringComparison.Ordinal);
        int allocate = review.IndexOf("ForceAllocator.Allocate(", StringComparison.Ordinal);
        int publish = review.IndexOf("CampaignPlanStore.Publish(alliance, plan, allocation, sectors);", StringComparison.Ordinal);
        int emit = review.IndexOf("EmitAllocation(allocation, plan, snapshot.Player, reliability, NonCompliance[alliance].Days);", StringComparison.Ordinal);
        AssertTrue(bookkeeping >= 0 && plan > bookkeeping && scope > plan && allocate > scope && publish > allocate && emit > publish,
            "bookkeeping return, plan, allocate scope, allocate, publish of that allocation, row");
        AssertEqual(1, review.Split(new[] { "ForceAllocator.Allocate(" }, StringSplitOptions.None).Length - 1, "one allocation per review");
        AssertContains(review, "ForceAllocator.Allocate(plan, snapshot, sectors, reliability, NonCompliance[alliance].Days,", "the plan, its snapshot and sectors, the reliability and the unreliable days");
        AssertContains(review, "HomeTheaterPrior.ForAlliance(alliance)", "the alliance's home theaters");
        AssertContains(review, "float reliability = PlayerComplianceRuntime.Reliability;", "stage F's live reliability (1 outside a career)");
        AssertContains(review, "NonCompliance[alliance].Observe(snapshot.DaySerial, HasMapGroupFormation(snapshot), reliability);", "one count per review day, only with a map-group player");
        AssertFalse(review.Contains("new Allocation"), "no empty allocation stands in for the real one");
        int seams = src.Split(new[] { "// G1B SEAM" }, StringSplitOptions.None).Length - 1;
        AssertEqual(0, seams, "G1b's runtime step replaced the seam: the recon tasks and the mission ledger are real");
        string reset = BodyOf(src, "private static void ResetPlanState(");
        AssertContains(reset, "LastAllocationRow[a] = null;", "the row memory is per campaign");
        AssertContains(reset, "NonCompliance[a].Reset();", "the unreliable-day run is per campaign");
        AssertContains(BodyOf(src, "private static bool HasMapGroupFormation("), "if (s.Own[i].Chain == WlChainClass.PlayerMapGroup) return true;", "the run belongs to the side that carries a map-group player");
        string emitBody = BodyOf(src, "private static void EmitAllocation(");
        AssertContains(emitBody, "TelemetryRouter.LegacyInfo(row, TelemetryLayer.Campaign);", "the [Allocation] row, on change");
    }

    // The allocation travels with its plan through the atomic daily publish: a reader between slices gets
    // yesterday's allocation, a reader after the commit today's; the pure allocator never edits a published one.
    private static void AllocationPublishesAtomicallyWithThePlan()
    {
        bool inJob = false;
        CampaignScope.ResetAll();
        DailyReviewView.Attach(() => inJob);
        try
        {
            var s = Wide();
            s.Own.Add(Corps(1, 200f, 20000f));
            var sectors = Sectors(s);
            var plan = PlanOf(s);
            var monday = Alloc(plan, s);
            inJob = true;
            CampaignPlanStore.Publish(0, plan, monday, sectors);
            inJob = false;
            AssertFalse(CampaignPlanStore.TryGetAllocation(0, out _), "nothing committed yet");
            CampaignPlanStore.PreparePublish();
            CampaignPlanStore.SwapPublished();
            AssertTrue(CampaignPlanStore.TryGetAllocation(0, out var seen) && ReferenceEquals(monday, seen), "Monday's allocation committed");

            s.Own.Add(Corps(2, 300f, 9000f));
            var tuesday = Alloc(PlanOf(s), s);
            inJob = true;
            CampaignPlanStore.Publish(0, PlanOf(s), tuesday, sectors);
            AssertTrue(CampaignPlanStore.TryGetAllocation(0, out var working) && ReferenceEquals(tuesday, working), "inside a job: the working allocation");
            inJob = false;
            AssertTrue(CampaignPlanStore.TryGetAllocation(0, out var mid) && ReferenceEquals(monday, mid), "between slices: Monday's allocation");
            AssertEqual(1, mid.Assignments.Count, "and Monday's allocation was not edited by Tuesday's");
            CampaignPlanStore.PreparePublish();
            CampaignPlanStore.SwapPublished();
            AssertTrue(CampaignPlanStore.TryGetAllocation(0, out var after) && ReferenceEquals(tuesday, after), "after the commit: Tuesday's");
        }
        finally
        {
            DailyReviewView.Attach(null);
            CampaignScope.ResetAll();
        }
    }

    // ---- fixtures ----

    private static CampaignSnapshot Line(int alliance, int areas)
    {
        var s = new CampaignSnapshot();
        s.Reset(alliance, alliance, Day, PlayerCommandClass.NotCareer, -1, true, null);
        s.RelocationRange = 150f;
        s.OffensiveDominance = 1.1f;
        s.MinStrengthForMovement = 1000f;
        s.MinMoraleForMovement = 0.3f;
        s.HasCapital = true;
        for (int i = 0; i < areas; i++)
            s.Areas.Add(new AreaRecord { Index = 10 + i, X = i * 100f, Z = 0f, EdgeX = new[] { i * 100f }, EdgeZ = new[] { 0f }, Importance = 1f, SupplyCapacity = 100f });
        s.Supply.Add(Iip(501, 100f, alliance));
        s.Supply.Add(Iip(503, 300f, alliance));
        return s;
    }

    // Seven areas (x 0..600), objectives at 100 / 300 / 500: sectors {0,100,200}, {300,400}, {500,600};
    // own capital at x 0, own IIPs at 100, 300 and 500.
    private static CampaignSnapshot Wide()
    {
        var s = Line(0, 7);
        s.Supply.Add(Iip(505, 500f, 0));
        s.Objectives.Add(Target(98, 100f, 1f));
        s.Objectives.Add(Target(99, 300f, 0f));
        s.Objectives.Add(Target(97, 500f, 0f));
        return s;
    }

    private static ObjectiveRecord Target(int id, float x, float importance)
    {
        return new ObjectiveRecord { ObjectiveId = id, X = x, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = importance };
    }

    private static FormationRecord Corps(int id, float x, float strength)
    {
        return new FormationRecord
        {
            InstanceId = id, UnitKey = "corps" + id + ":" + id, UnitTyp = 15, X = x, Z = 0f, WaypointX = x, WaypointZ = 0f,
            Strength = strength, Morale = 1f, Supply01 = 1f, ReadinessStep = 3, CommandRange = 40f, BugleRange = 5f, CommanderId = id,
            AreaIndex = -1, Flags = FormationFlags.FightingForce, Name = "corps" + id, ParentInstanceId = -1,
            UseRails = true, UseRivers = true, GroupSpeed = 1f
        };
    }

    private static FormationRecord WithFlags(FormationRecord f, FormationFlags flags)
    {
        f.Flags |= flags;
        return f;
    }

    private static SupplySourceRecord Iip(int id, float x, int owner)
    {
        return new SupplySourceRecord { Id = id, Kind = SupplySourceKind.Iip, Owner = owner, X = x, Z = 0f, TransportCapacity = 10f, Condition = 1f, Built = true };
    }

    private static EnemyBelief Belief(float x, float z, float strength)
    {
        return new EnemyBelief { InstanceId = -1, UnitKey = "enemy:" + x, UnitTyp = 15, X = x, Z = z, Strength = strength, Confidence01 = 1f, LastSeenDay = Day };
    }

    // unitbasicspeed[t, 1] = 2 for groups, basicunitspeed 1000, factor 30, costs walk 4 / river 1 / road 2 / rail 1.
    private static MarchSpeedInputs Speed()
    {
        var mounted = new float[19];
        for (int t = 14; t <= 16; t++) mounted[t] = 2f;
        return new MarchSpeedInputs
        {
            MountedBaseSpeed = mounted, BasicUnitSpeed = 1000f, CampaignSpeedFactor = 30f,
            WalkCost = 4f, RiverCost = 1f, RoadCost = 2f, RailCost = 1f,
            RailroadSpeedAdj = 1f, TrainCapacityFree = 0f, TrainCapacityPerMan = 0.001f
        };
    }

    private static SectorModel Sectors(CampaignSnapshot s) => SectorModel.Build(s);

    // The real G3 plan: its mass, capital floor and published hold needs are what the allocator reads.
    private static TheaterPlan PlanOf(CampaignSnapshot s)
    {
        return TheaterPlanner.Plan(s, Sectors(s), null, new PlannerState(), null, null, Speed(), out _);
    }

    // No main effort, no hold needs, no mass: the isolated rules.
    private static TheaterPlan ManualPlan(CampaignSnapshot s)
    {
        return new TheaterPlan { Alliance = s.Alliance, DaySerial = s.DaySerial };
    }

    // A hand-built main effort at objective 98 (sector of x 100) over the given mass.
    private static TheaterPlan MainPlan(CampaignSnapshot s, int[] mass, float targetX)
    {
        var plan = ManualPlan(s);
        int sector = Sectors(s).SectorAt(targetX, 0f);
        plan.MainSectorId = sector;
        plan.MainObjectiveId = 98;
        plan.MainRequiredRatio = 1.1f;
        plan.Sectors.Add(new SectorPlan { SectorId = sector, Posture = TheaterPosture.MainEffort, TargetObjectiveId = 98, TargetX = targetX, TargetZ = 0f, RequiredRatio = 1.1f });
        plan.MassInstanceIds.AddRange(mass);
        return plan;
    }

    private static Allocation Alloc(TheaterPlan plan, CampaignSnapshot s, float reliability = 1f, int nonCompliantDays = 0,
                                    Func<string, Theater> home = null, IReadOnlyList<ReconTask> recon = null, ReconMissionLedger missions = null)
    {
        return ForceAllocator.Allocate(plan, s, Sectors(s), reliability, nonCompliantDays, home, recon, missions);
    }

    private static Assignment Of(Allocation a, int instanceId)
    {
        AssertTrue(a.TryGet(instanceId, out var x), "formation " + instanceId + " has an assignment");
        return x;
    }

    private static float HoldNeedOf(TheaterPlan plan, int sectorId)
    {
        foreach (var sp in plan.Sectors)
            if (sp.SectorId == sectorId) return sp.HoldNeed;
        throw new Exception("no plan for sector " + sectorId);
    }

    private static ReconTask ScreenTask(float fromX, float fromZ, float holdX, float holdZ, int sector)
    {
        return new ReconTask { Kind = ReconKind.Screen, SectorId = sector, FromX = fromX, FromZ = fromZ, HoldX = holdX, HoldZ = holdZ, Priority = 1f, Reason = "t" };
    }
}
