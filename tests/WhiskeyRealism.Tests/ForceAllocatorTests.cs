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
            ("OnlyTheNeededMassGoesToTheMainEffort", OnlyTheNeededMassGoesToTheMainEffort),
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
            ("ReserveKeepsTheMarginAboveRequired", ReserveKeepsTheMarginAboveRequired),
            ("ReserveMeasuredAtTheMainObjectiveNotTheFrontier", ReserveMeasuredAtTheMainObjectiveNotTheFrontier),
            ("CarryingGroupIsMovable", CarryingGroupIsMovable),
            ("MapGroupPlayerNeverSoleMustHold", MapGroupPlayerNeverSoleMustHold),
            ("MapGroupPlayerCountsInAHoldSumAtStrengthTimesMorale", MapGroupPlayerCountsInAHoldSumAtStrengthTimesMorale),
            ("PlayerStandingInASectorReleasesItsAIHolderIntoTheMass", PlayerStandingInASectorReleasesItsAIHolderIntoTheMass),
            ("PlayerMarchingAwayLeavesTheSectorToAnAIHolder", PlayerMarchingAwayLeavesTheSectorToAnAIHolder),
            ("PlayerIsNeverInTheMassOrAMustHold", PlayerIsNeverInTheMassOrAMustHold),
            ("MapGroupPlayerIsNeverPickedAsAPicket", MapGroupPlayerIsNeverPickedAsAPicket),
            ("MixedSectorRealPlannerNeverPicketsThePlayer", MixedSectorRealPlannerNeverPicketsThePlayer),
            ("StarvingPlayerAloneInAThreatenedSectorIsNotRecover", StarvingPlayerAloneInAThreatenedSectorIsNotRecover),
            ("StarvingAICorpsBesideAHealthyMapGroupPlayerStillMakesTheSectorRecover", StarvingAICorpsBesideAHealthyMapGroupPlayerStillMakesTheSectorRecover),
            ("ScreenSectorHeldOnlyByThePlayerGetsAnAIScreen", ScreenSectorHeldOnlyByThePlayerGetsAnAIScreen),
            ("PlayerBoundForAnEmptyThreatenedSectorMakesItHold", PlayerBoundForAnEmptyThreatenedSectorMakesItHold),
            ("HoldSectorOfCreditsTheWaypointWhateverThePosition", HoldSectorOfCreditsTheWaypointWhateverThePosition),
            ("SectorFactsCreditTheHoldSectorBeforeAnyPhysicalCheck", SectorFactsCreditTheHoldSectorBeforeAnyPhysicalCheck),
            ("CicStandDownPrecedesEveryHoldSum", CicStandDownPrecedesEveryHoldSum),
            ("RecoveringFormationsDoNotHoldTheirSector", RecoveringFormationsDoNotHoldTheirSector),
            ("ReserveRulesHoldIgnoresRecoveringFormations", ReserveRulesHoldIgnoresRecoveringFormations),
            ("StarvedMapGroupPlayerIsNeverRecover", StarvedMapGroupPlayerIsNeverRecover),
            ("SectorHeldOnlyByThePlayerIsHeldUntilHeIsShort", SectorHeldOnlyByThePlayerIsHeldUntilHeIsShort),
            ("MapGroupPlayerKeepsHisFrontsRoleAndIsNeverCounted", MapGroupPlayerKeepsHisFrontsRoleAndIsNeverCounted),
            ("PlannerCountsThePlayersGroupWhereItsWaypointLies", PlannerCountsThePlayersGroupWhereItsWaypointLies),
            ("ReserveRulesHoldCountsTheMapGroupPlayerNotHisCommand", ReserveRulesHoldCountsTheMapGroupPlayerNotHisCommand),
            ("NonComplianceCounterCountsConsecutiveUnreliableDays", NonComplianceCounterCountsConsecutiveUnreliableDays),
            ("CicPlayerYieldsEmptyAllocation", CicPlayerYieldsEmptyAllocation),
            ("HomeDepartmentBreaksTies", HomeDepartmentBreaksTies),
            ("HomeDepartmentOutweighsADistanceMargin", HomeDepartmentOutweighsADistanceMargin),
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
            ("SupportIsTheReinforcementAndNothingElse", SupportIsTheReinforcementAndNothingElse),
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

    // D3 (smoke amendments 2026-10-16): the mass is the force the objective needs, not everything movable. Three corps and a main target
    // nobody has scouted: the presumed enemy is 5000 x (1 + 1.0, the objective's importance) = 10000, the need 10000 x 1.1 x 1.25 = 13750, and
    // corps 1 (20000, nearest the target) is all of it. The other two keep their own front's role: they are not "the remainder to the main
    // effort". The staged main effort gathers at its start line, the position of its forward-most member.
    private static void OnlyTheNeededMassGoesToTheMainEffort()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(2, 350f, 15000f));
        s.Own.Add(Corps(3, 550f, 10000f));
        var plan = PlanOf(s);
        AssertEqual(98, plan.MainObjectiveId, "main effort at x 100");
        AssertNear(13750f, plan.MainNeedStrength, 1f, "the need: presumed 10000 x required 1.1 x (1 + 0.25)");
        AssertEqual("1", string.Join(",", plan.MassInstanceIds), "the mass is the nearest corps, which is enough");
        var a = Alloc(plan, s);
        var sectors = Sectors(s);
        var one = Of(a, 1);
        AssertEqual(FormationRole.MainEffort, one.Role, "corps 1 is the main effort");
        AssertEqual(plan.MainSectorId, one.SectorId, "main sector");
        AssertTrue(plan.MainStaged, "unscouted: staged");
        AssertNear(200f, one.TargetX, 0.01f, "it gathers at the start line (its own position), not at the objective");
        AssertFalse(one.Withheld, "the main effort is not withheld");
        AssertEqual(ForceAllocator.ReasonMass, one.Reason, "reason");
        foreach (int id in new[] { 2, 3 })
        {
            var x = Of(a, id);
            AssertEqual(FormationRole.Hold, x.Role, "corps " + id + " keeps its own front's role");
            AssertTrue(x.Role != FormationRole.Support, "never 'the remainder to main'");
            AssertNear(id == 2 ? 350f : 550f, x.TargetX, 0.01f, "corps " + id + " stays where it stands");
        }
        AssertEqual(sectors.SectorAt(100f, 0f), plan.MainSectorId, "the main sector is the target's");
    }

    // G3 verify r1 F4/F9: the odds were judged on TheaterPlan.MassInstanceIds, so MainEffort is exactly that set.
    private static void MainEffortIsExactlyThePlannedMass()
    {
        var s = Wide();
        s.Beliefs.Add(Belief(150f, 0f, 30000f)); // scouted (50 from the objective, 150 from the capital: outside its range), need 30000 x 1.1 x 1.25 > both corps
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
        }
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
        hq.Flags = default(FormationFlags); // not a fighting force
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
        AssertNear(550f, x.TargetX, 0.01f, "a hold reserve stays where it stands (x)");
        AssertNear(0f, x.TargetZ, 0.01f, "...(z)");
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
        string Role8(DirectorPosture tempo)
        {
            var s = Wide();
            s.Reset(0, 0, Day, PlayerCommandClass.NotCareer, -1, tempo);
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
            return Of(Alloc(plan, s), 8).Reason;
        }
        // D3 sizes the push to its need, so corps 8 is no longer "massed"; what the hold modifier changes is whether the hold reserve keeps it.
        AssertEqual(ForceAllocator.ReasonGarrison, Role8(new DirectorPosture { MinimumHoldRatioModifier = 0f }), "0.6 x 10000: corps 7 holds alone, corps 8 is free");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Role8(new DirectorPosture { MinimumHoldRatioModifier = 0.2f }), "0.8 x 10000: both hold");
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
        plan.MainEnemyStrength = 10000f;
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
        s.Beliefs.Add(Belief(100f, 0f, 25000f)); // a scouted enemy whose need (34375) takes every corps in reach into the mass
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

    // M3: between the margins. Mass 7000 + 7000 + 8000 against a believed 10000 (required 11000): dropping the
    // farthest leaves 14000, above 1.0 x but below 1.3 x (14300): no Reserve.
    private static void ReserveKeepsTheMarginAboveRequired()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 110f, 7000f));
        s.Own.Add(Corps(2, 200f, 7000f));
        s.Own.Add(Corps(3, 350f, 8000f));
        s.Beliefs.Add(Belief(120f, 0f, 10000f));
        var a = Alloc(MainPlan(s, new[] { 1, 2, 3 }, 100f), s);
        foreach (int id in new[] { 1, 2, 3 })
            AssertEqual(FormationRole.MainEffort, Of(a, id).Role, "14000 left is under 1.3 x 11000: corps " + id + " is kept");
    }

    // F2 (G4 verify r1): objective 97 at x 600 is out of supply reach (sources at 100 and 300, range 40), so the
    // gather point is clamped back; a small believed force stands near it, the 20000 defender at the objective.
    // The reserve is measured against the enemy G3 judged at the objective, not against the frontier's.
    private static void ReserveMeasuredAtTheMainObjectiveNotTheFrontier()
    {
        var s = Line(0, 7);
        s.Objectives.Add(Target(97, 600f, 1f));
        for (int i = 1; i <= 5; i++) s.Own.Add(Corps(i, 100f + 40f * i, 8000f));
        s.Beliefs.Add(Belief(600f, 0f, 20000f));
        s.Beliefs.Add(Belief(345f, 0f, 3000f));
        var plan = PlanOf(s);
        AssertEqual(97, plan.MainObjectiveId, "the out-of-reach objective is the main effort");
        AssertNear(20000f, plan.MainEnemyStrength, 1f, "the enemy G3 judged the odds against, at the objective");
        AssertTrue(MainTargetX(plan) < 500f, "the gather point is clamped back toward supply: " + MainTargetX(plan));
        var a = Alloc(plan, s);
        foreach (var x in a.Assignments)
            AssertTrue(x.Role != FormationRole.Reserve, "no reserve is carved against the frontier's 3000 (corps " + x.InstanceId + " is " + x.Role + ")");
        AssertEqual(0, a.Surpluses.Count, "and no surplus is listed for G5b");
    }

    private static float MainTargetX(TheaterPlan plan)
    {
        foreach (var sp in plan.Sectors)
            if (sp.SectorId == plan.MainSectorId) return sp.TargetX;
        throw new Exception("no main sector plan");
    }

    // ---- W&L player classes (spec C4) ----

    private static void CarryingGroupIsMovable()
    {
        var s = Wide();
        s.Beliefs.Add(Belief(100f, 0f, 25000f)); // need 34375: both corps
        s.Own.Add(Corps(1, 200f, 20000f));
        var carrying = Corps(2, 350f, 15000f);
        carrying.Chain = WlChainClass.CarryingGroup;
        s.Own.Add(carrying);
        var plan = PlanOf(s);
        AssertTrue(plan.MassInstanceIds.Contains(2), "the planner counts the carrying group");
        AssertEqual(FormationRole.MainEffort, Of(Alloc(plan, s), 2).Role, "an ordinary movable formation: the player it carries is sent no order");
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
        AssertFalse(x.Withheld, "he is never withheld");
        AssertFalse(a.Surpluses.Contains(1), "never a surplus");
    }

    // The map-group player's group counts toward the garrison at strength x morale, vanilla's own measure
    // (11733-11740): 8000 at full morale covers the 5000 garrison, so the AI division is not withheld and no
    // shortfall is reported; at morale 0.5 it counts 4000 and the AI division is withheld to make up the rest.
    // The player's own command (immobile) counts nothing: the AI division is withheld and, 3000 short, carries
    // the shortfall. The player is never marked Withheld himself.
    private static void MapGroupPlayerCountsInAHoldSumAtStrengthTimesMorale()
    {
        foreach (float morale in new[] { 1f, 0.5f })
        {
            var s = Wide();
            var player = Corps(1, 550f, 8000f);
            player.Chain = WlChainClass.PlayerMapGroup;
            player.Morale = morale;
            s.Own.Add(player);
            s.Own.Add(Corps(2, 560f, 3000f));
            var a = Alloc(ManualPlan(s), s);
            AssertEqual(FormationRole.Hold, Of(a, 1).Role, "morale " + morale + ": he stands in his sector as a position");
            AssertFalse(Of(a, 1).Withheld, "morale " + morale + ": a map-group player is never marked Withheld");
            AssertEqual(morale < 1f, Of(a, 2).Withheld, "morale " + morale + ": the AI division is withheld only when his count falls short of 5000");
            AssertEqual(0, a.Shortfalls.Count, "morale " + morale + ": 4000-8000 plus 3000 reaches the garrison: no shortfall");
        }

        var c = Wide();
        var command = Corps(1, 550f, 8000f);
        command.Chain = WlChainClass.PlayerCommand;
        c.Own.Add(command);
        c.Own.Add(Corps(2, 560f, 3000f));
        var b = Alloc(ManualPlan(c), c);
        AssertFalse(Of(b, 1).Withheld, "his own command is not marked Withheld");
        AssertTrue(Of(b, 2).Withheld, "his own command counts nothing: the AI division is withheld");
        AssertEqual("2", string.Join(",", b.Shortfalls), "and, 2000 short of the 5000 garrison, carries the shortfall");
    }

    // Integrator decision 2026-10-15, through the real planner: the map-group player's group counts toward the
    // hold of the sector it stands in (vanilla counts it, 11733-11740), so with him there at 20000 the AI corps
    // that would hold the sector (need about 6000 facing an army at x 600) is released into the mass; without
    // him it holds, withheld.
    private static void PlayerStandingInASectorReleasesItsAIHolderIntoTheMass()
    {
        foreach (bool withPlayer in new[] { false, true })
        {
            var s = Wide();
            s.Own.Add(Corps(1, 200f, 20000f));
            s.Own.Add(Corps(4, 560f, 8000f));
            if (withPlayer)
            {
                var player = Corps(5, 550f, 20000f);
                player.Chain = WlChainClass.PlayerMapGroup;
                s.Own.Add(player);
            }
            s.Beliefs.Add(Belief(600f, 0f, 10000f));
            var plan = PlanOf(s);
            var a = Alloc(plan, s);
            var x = Of(a, 4);
            if (withPlayer)
            {
                // D3: the push is sized to its need (corps 1 alone), so corps 4 is not massed; what the player's presence changes is
                // that corps 4 is no longer the sector's hold reserve and is not withheld from vanilla.
                AssertEqual(FormationRole.Hold, x.Role, "corps 4 is not needed by the push");
                AssertTrue(x.Reason != ForceAllocator.ReasonHoldReserve, "with the player there corps 4 is released from the hold reserve");
                AssertFalse(x.Withheld, "and is not withheld: the player's group covers the sector");
                AssertFalse(Of(a, 5).Withheld, "the player's own group is never withheld");
                AssertEqual(FormationRole.Hold, Of(a, 5).Role, "and holds in place");
            }
            else
            {
                AssertFalse(plan.MassInstanceIds.Contains(4), "without him the planner keeps corps 4 out of the mass");
                AssertEqual(FormationRole.Hold, x.Role, "corps 4 holds");
                AssertTrue(x.Withheld, "and is withheld");
                AssertEqual(ForceAllocator.ReasonHoldReserve, x.Reason, "reason");
            }
        }
    }

    // The player marches away: his last waypoint is at x 100 (the main objective), so the sector at x 550 no
    // longer counts him. The next review sees the gap: the AI corps keeps (and a second corps of the sector gets) the hold,
    // and the sector reports its shortfall at the AI holder. He is counted in the sector he is bound for.
    private static void PlayerMarchingAwayLeavesTheSectorToAnAIHolder()
    {
        FormationRecord Player(float waypointX)
        {
            var player = Corps(5, 550f, 20000f);
            player.Chain = WlChainClass.PlayerMapGroup;
            player.WaypointX = waypointX;
            return player;
        }
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(4, 560f, 3000f));
        s.Own.Add(Corps(8, 540f, 6000f));
        s.Own.Add(Player(100f));
        s.Beliefs.Add(Belief(600f, 0f, 10000f));
        var plan = PlanOf(s);
        var a = Alloc(plan, s);
        AssertFalse(plan.MassInstanceIds.Contains(4), "corps 4 holds the sector the player is leaving");
        AssertEqual(FormationRole.Hold, Of(a, 4).Role, "corps 4 keeps the hold");
        AssertTrue(Of(a, 4).Withheld, "withheld");
        AssertEqual(FormationRole.Hold, Of(a, 8).Role, "a second corps gets the hold: 3000 does not reach the need");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(a, 8).Reason, "as the sector's hold reserve");
        int far = Sectors(s).SectorAt(550f, 0f);
        AssertEqual(far, Of(a, 8).SectorId, "for the sector he is leaving");

        var home = Wide();
        home.Own.Add(Corps(1, 200f, 20000f));
        home.Own.Add(Corps(4, 560f, 3000f));
        home.Own.Add(Corps(8, 540f, 6000f));
        home.Own.Add(Player(550f));
        home.Beliefs.Add(Belief(600f, 0f, 10000f));
        var homePlan = PlanOf(home);
        var b = Alloc(homePlan, home);
        AssertTrue(Of(b, 4).Reason != ForceAllocator.ReasonHoldReserve && Of(b, 8).Reason != ForceAllocator.ReasonHoldReserve, "with him staying, both AI corps are released from the hold reserve");

        // Too weak a holder after he leaves: the sector reports its shortfall at the AI holder.
        var weak = Wide();
        weak.Own.Add(Corps(1, 200f, 20000f));
        weak.Own.Add(Corps(4, 560f, 2000f));
        weak.Own.Add(Player(100f));
        weak.Beliefs.Add(Belief(600f, 0f, 10000f));
        var weakAllocation = Alloc(PlanOf(weak), weak);
        AssertTrue(weakAllocation.Shortfalls.Contains(4), "the sector reports its shortfall at its AI holder");
        AssertFalse(weakAllocation.Shortfalls.Contains(5), "never at the player's group");
    }

    // The player's group counts, but never as mass, never pledged, never given a must-hold role: whatever the
    // posture, his formation is Hold-in-place or a dispatch-only Support order, never withheld.
    private static void PlayerIsNeverInTheMassOrAMustHold()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        var player = Corps(5, 350f, 30000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        s.Own.Add(player);
        s.Beliefs.Add(Belief(100f, 0f, 10000f));
        var plan = PlanOf(s);
        var a = Alloc(plan, s);
        AssertFalse(plan.MassInstanceIds.Contains(5), "never in the mass");
        var x = Of(a, 5);
        AssertTrue(x.Role == FormationRole.Hold || x.Role == FormationRole.Support, "an order of his sector's role only");
        AssertFalse(x.Withheld, "never withheld: a W&L dispatch moves him, not vanilla's pools");
        AssertTrue(!a.Surpluses.Contains(5), "never a surplus");
        AssertTrue(x.Role != FormationRole.MainEffort && x.Role != FormationRole.CapitalGuard && x.Role != FormationRole.Reserve && x.Role != FormationRole.Recover, "never mass, guard, reserve or recover");
    }

    // N2 (G4 verify r2, integrator decision): a formation assigned Recover is leaving for supply, so it does
    // not hold its sector. Two starved corps standing in a threatened sector no longer release the healthy
    // corps that should hold it (verify r2 seed 6: sector need 15842 "held" by Recover corps of 16974 and
    // 29399 while the four healthy corps massed). Through the real planner.
    private static void RecoveringFormationsDoNotHoldTheirSector()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(4, 560f, 8000f));
        var starved = Corps(6, 555f, 16974f);
        starved.Supply01 = 0.1f;
        var demoralised = Corps(7, 565f, 29399f);
        demoralised.Morale = 0.1f; // below aiminimummoraleformovement (0.3)
        s.Own.Add(starved);
        s.Own.Add(demoralised);
        s.Beliefs.Add(Belief(600f, 0f, 10000f));
        var plan = PlanOf(s);
        var a = Alloc(plan, s);
        AssertEqual(FormationRole.Recover, Of(a, 6).Role, "the starved corps recovers");
        AssertEqual(FormationRole.Recover, Of(a, 7).Role, "so does the demoralised one");
        AssertFalse(plan.MassInstanceIds.Contains(4), "the planner keeps the healthy corps out of the mass: the recovering ones do not hold the sector");
        var holder = Of(a, 4);
        AssertEqual(FormationRole.Hold, holder.Role, "the healthy corps holds");
        AssertEqual(ForceAllocator.ReasonHoldReserve, holder.Reason, "as the sector's hold reserve");
        AssertTrue(holder.Withheld, "and is withheld");

        // Too weak a holder: the sector reports its shortfall at the healthy corps, whatever walks away to recover.
        var weak = Wide();
        weak.Own.Add(Corps(1, 200f, 20000f));
        weak.Own.Add(Corps(4, 560f, 3000f));
        weak.Own.Add(starved);
        weak.Own.Add(demoralised);
        weak.Beliefs.Add(Belief(600f, 0f, 30000f));
        var weakAllocation = Alloc(PlanOf(weak), weak);
        AssertEqual(FormationRole.Hold, Of(weakAllocation, 4).Role, "the weak healthy corps still holds");
        AssertTrue(weakAllocation.Shortfalls.Contains(4), "and the sector reports its shortfall there");
        AssertFalse(weakAllocation.Shortfalls.Contains(6) || weakAllocation.Shortfalls.Contains(7), "never at a corps that is leaving");
    }

    // The AI cannot walk the map-group player's group to a depot: starved or demoralised he keeps his sector's
    // order (Hold in place), never Recover (spec C4, plan G4 rules).
    private static void StarvedMapGroupPlayerIsNeverRecover()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        var player = Corps(5, 550f, 20000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        player.Supply01 = 0.1f;
        player.Morale = 0.1f;
        s.Own.Add(player);
        var a = Alloc(ManualPlan(s), s);
        AssertEqual(FormationRole.Hold, Of(a, 5).Role, "the starved player stays Hold");
        AssertFalse(Of(a, 5).Withheld, "and is not withheld");
    }

    // The rule itself, on ReserveRules.Hold: a formation below the supply or morale floor does not count
    // toward what holds a sector anyway; a busy formation of the same strength still does (it stays).
    private static void ReserveRulesHoldIgnoresRecoveringFormations()
    {
        var s = Wide();
        s.Own.Add(Corps(4, 560f, 8000f));
        var starved = Corps(6, 555f, 20000f);
        starved.Supply01 = 0.1f;
        s.Own.Add(starved);
        var busy = WithFlags(Corps(7, 565f, 20000f), TheaterPlanner.Busy);
        busy.Supply01 = 0.1f; // starved but in battle: it cannot walk away, so it still holds
        var sectors = Sectors(s);
        int k = sectors.SectorAt(560f, 0f);
        var need = new float[sectors.Count];
        var nearX = new float[sectors.Count];
        var nearZ = new float[sectors.Count];
        need[k] = 15000f;
        nearX[k] = 600f;
        var reserved = new bool[s.Own.Count];
        var holdReserved = new int[s.Own.Count];
        ReserveRules.Hold(s, sectors, need, nearX, nearZ, reserved, holdReserved);
        AssertEqual(k, holdReserved[0], "the starved corps' 20000 does not count: the healthy corps is reserved");

        s.Own.Add(busy);
        reserved = new bool[s.Own.Count];
        holdReserved = new int[s.Own.Count];
        ReserveRules.Hold(s, sectors, need, nearX, nearZ, reserved, holdReserved);
        AssertEqual(-1, holdReserved[0], "a busy formation stays, starved or not, and does count: nothing more is reserved");
    }

    // G4 verify r3 F1, through the real ReconPlanner.PlanTasks: an own town at x 400 in a sector the map-group
    // player alone stands in (x 450), the enemy at x 250 or elsewhere. A picket is a pre-pledge, so the player is
    // never picked: the town gets an AI picket or none, and no Recon role, no Withheld, ever lands on him.
    private static void MapGroupPlayerIsNeverPickedAsAPicket()
    {
        foreach (float bx in new[] { 250f, 300f, 620f, 650f })
            foreach (float strength in new[] { 2000f, 20000f })
            {
                var s = Wide();
                var guard = Corps(1, 100f, 30000f);
                guard.SightRadius = 30f;
                s.Own.Add(guard);
                var player = Corps(9, 450f, strength);
                player.SightRadius = 30f;
                player.Chain = WlChainClass.PlayerMapGroup;
                s.Own.Add(player);
                s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 50, X = 400f, Z = 0f, HolderAlliance = 0, Available = false, Importance01 = 1f });
                s.Beliefs.Add(Belief(bx, 0f, 10000f));
                var sectors = Sectors(s);
                var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, Speed(), out _);
                var missions = new ReconMissionLedger();
                var tasks = new List<ReconTask>();
                ReconPlanner.PlanTasks(s, sectors, plan, missions, tasks);
                var a = ForceAllocator.Allocate(plan, s, sectors, null, tasks, missions);
                var x = Of(a, 9);
                string label = "bx " + bx + " strength " + strength + ": ";
                AssertTrue(x.Role != FormationRole.Recon, label + "the player is never a picket or any recon post");
                AssertFalse(x.Withheld, label + "and never withheld");
                foreach (var task in tasks)
                    AssertTrue(ReconPlanner.PickFormation(s, sectors, plan, missions, task, null, false) != 9, label + "no task " + task.Kind + "@" + task.HoldX + " picks him");
            }
    }

    // G4 verify r4 V1, through the real planner, PlanTasks and Allocate: a town whose sector holds an AI corps of 50000 and
    // the map-group player's 5000 against a hold need of 18000. The AI corps cannot spare itself and the player is
    // never picked (the picket sum alone does not keep him out in a mixed sector: only the MayMove guard does).
    private static void MixedSectorRealPlannerNeverPicketsThePlayer()
    {
        var s = Wide();
        var guard = Corps(1, 100f, 30000f);
        guard.SightRadius = 30f;
        s.Own.Add(guard);
        var ai = Corps(2, 420f, 50000f);
        ai.SightRadius = 30f;
        s.Own.Add(ai);
        var player = Corps(9, 410f, 5000f);
        player.SightRadius = 30f;
        player.Chain = WlChainClass.PlayerMapGroup;
        s.Own.Add(player);
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 50, X = 400f, Z = 0f, HolderAlliance = 0, Available = false, Importance01 = 1f });
        s.Beliefs.Add(Belief(450f, 0f, 30000f));
        var sectors = Sectors(s);
        var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, Speed(), out _);
        var missions = new ReconMissionLedger();
        var tasks = new List<ReconTask>();
        ReconPlanner.PlanTasks(s, sectors, plan, missions, tasks);
        var a = Alloc(plan, s, null, tasks, missions);
        AssertTrue(Of(a, 9).Role != FormationRole.Recon, "the player is never a recon post");
        AssertFalse(Of(a, 9).Withheld, "and never withheld");
        foreach (var task in tasks)
            AssertTrue(ReconPlanner.PickFormation(s, sectors, plan, missions, task, null, false) != 9, "no task " + task.Kind + "@" + task.HoldX + " picks him");
        var direct = new ReconTask { Kind = ReconKind.Picket, SectorId = sectors.SectorAt(400f, 0f), FromX = 400f, FromZ = 0f, HoldX = 400f, HoldZ = 0f, Reason = "picket-town" };
        AssertTrue(ReconPlanner.PickFormation(s, sectors, plan, missions, direct, null, false) != 9, "nor a picket built by hand for the town's sector");
    }

    // G4 verify r4 V2: a sector's Recover posture is decided by the formations the planner can order. A map-group
    // player (or the player's own command) below the supply floor, alone in a threatened sector, counts toward its
    // hold but cannot turn it to Recover: it stays Hold or Screen (a Recover sector gets no screen or picket). An AI corps in the same
    // state still does.
    private static void StarvingPlayerAloneInAThreatenedSectorIsNotRecover()
    {
        foreach (var chain in new[] { WlChainClass.PlayerMapGroup, WlChainClass.PlayerCommand, WlChainClass.None })
            foreach (float strength in new[] { 2000f, 20000f })
            {
                var s = Wide();
                var guard = Corps(1, 100f, 30000f);
                guard.SightRadius = 30f;
                s.Own.Add(guard);
                var lone = Corps(9, 550f, strength);
                lone.Chain = chain;
                lone.Supply01 = 0.1f;
                lone.Morale = 0.1f;
                s.Own.Add(lone);
                s.Beliefs.Add(Belief(600f, 0f, 10000f));
                var sectors = Sectors(s);
                int k = sectors.SectorAt(550f, 0f);
                var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, Speed(), out _);
                var posture = PostureOfSector(plan, k);
                string label = chain + " " + strength + ": ";
                if (chain == WlChainClass.None)
                {
                    AssertEqual(TheaterPosture.Recover, posture, label + "an AI corps below the floor sends its sector to Recover");
                    continue;
                }
                AssertEqual(TheaterPosture.Screen, posture, label + "the player's supply and morale do not; at morale 0.1 his strength counts too little (2000 at most against 6000) to hold");
            }
    }

    // G4 verify r5 W1: the Recover average is taken over the formations the planner can order, not the player's. A
    // starving AI corps (supply 0.3) beside a healthy map-group player (supply 1) still sends its sector to
    // Recover: summing every formation's supply while dividing by the orderable count would read 1.3 and miss it.
    private static void StarvingAICorpsBesideAHealthyMapGroupPlayerStillMakesTheSectorRecover()
    {
        foreach (bool bySupply in new[] { true, false })
        {
            var s = Wide();
            var guard = Corps(1, 100f, 30000f);
            guard.SightRadius = 30f;
            s.Own.Add(guard);
            var hungry = Corps(8, 540f, 5000f);
            if (bySupply) hungry.Supply01 = 0.3f; else hungry.Morale = 0.2f; // below the movement minimum 0.3
            s.Own.Add(hungry);
            var player = Corps(9, 550f, 20000f);
            player.Chain = WlChainClass.PlayerMapGroup;
            s.Own.Add(player);
            s.Beliefs.Add(Belief(600f, 0f, 10000f));
            var sectors = Sectors(s);
            var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, Speed(), out _);
            AssertEqual(TheaterPosture.Recover, PostureOfSector(plan, sectors.SectorAt(550f, 0f)),
                (bySupply ? "supply" : "morale") + ": the AI corps decides, not the healthy player's");
        }
    }

    // G4 verify r5 W2, through the real planner and PlanTasks: a Screen sector held only by a starving map-group player
    // gets a screen-posture task from an AI formation, never from him, starving or healthy (the healthy case is what shows the MayMove guard, since a starving formation fails CanMove anyway). The post (x 576) must be within command range
    // (40) of an own IIP, else LogisticsGate.ClampToReach makes no task at all: here an own IIP at x 560.
    private static void ScreenSectorHeldOnlyByThePlayerGetsAnAIScreen()
    {
        foreach (bool starving in new[] { true, false })
        {
            var s = Wide();
            s.Supply.Add(Iip(506, 560f, 0));
            var guard = Corps(1, 100f, 30000f);
            guard.SightRadius = 30f;
            s.Own.Add(guard);
            var lone = Corps(9, 550f, 2000f);
            lone.Chain = WlChainClass.PlayerMapGroup;
            if (starving)
            {
                lone.Supply01 = 0.1f;
                lone.Morale = 0.1f;
            }
            s.Own.Add(lone);
            s.Beliefs.Add(Belief(600f, 0f, 10000f));
            var sectors = Sectors(s);
            int k = sectors.SectorAt(550f, 0f);
            var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, Speed(), out _);
            AssertEqual(TheaterPosture.Screen, PostureOfSector(plan, k), "his strength x morale does not hold the sector, and his supply does not make it Recover");
            var missions = new ReconMissionLedger();
            var tasks = new List<ReconTask>();
            ReconPlanner.PlanTasks(s, sectors, plan, missions, tasks);
            ReconTask screen = default;
            bool found = false;
            foreach (var task in tasks)
                if (task.SectorId == k && task.Reason == "screen-posture") { screen = task; found = true; }
            AssertTrue(found, "the Screen sector keeps its screen task");
            int holder = ReconPlanner.PickFormation(s, sectors, plan, missions, screen, null, false);
            AssertEqual(1, holder, "an AI formation takes it, never the player");
            var a = Alloc(plan, s, null, tasks, missions);
            AssertTrue(Of(a, 9).Role != FormationRole.Recon && !Of(a, 9).Withheld, "and he stays out of the allocation's recon posts");
        }
    }

    // G4 verify r3 F2, through the real planner and ReconPlanner.PlanTasks: the player stands at x 310 (sector 1)
    // with his last waypoint at x 500, an empty threatened sector (need 6000, nothing of ours physically there).
    // He counts there at 20000, so the planner calls it Hold and plans no screen for it; with the waypoint at x 310
    // nothing holds it and it is Screen.
    private static void PlayerBoundForAnEmptyThreatenedSectorMakesItHold()
    {
        foreach (float waypointX in new[] { 310f, 500f })
        {
            var s = Wide();
            var guard = Corps(1, 100f, 30000f);
            guard.SightRadius = 30f;
            s.Own.Add(guard);
            s.Own.Add(Corps(3, 290f, 3000f));
            var player = Corps(9, 310f, 20000f);
            player.Chain = WlChainClass.PlayerMapGroup;
            player.WaypointX = waypointX;
            s.Own.Add(player);
            s.Beliefs.Add(Belief(300f, 0f, 10000f));
            s.Beliefs.Add(Belief(500f, 0f, 10000f));
            var sectors = Sectors(s);
            int empty = sectors.SectorAt(500f, 0f);
            var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, Speed(), out _);
            var tasks = new List<ReconTask>();
            ReconPlanner.PlanTasks(s, sectors, plan, new ReconMissionLedger(), tasks);
            bool screen = false;
            foreach (var task in tasks)
                if (task.SectorId == empty && task.Reason == "screen-posture") screen = true;
            if (waypointX > 400f)
            {
                AssertEqual(TheaterPosture.Hold, PostureOfSector(plan, empty), "bound for x 500 he holds the empty threatened sector");
                AssertFalse(screen, "so the planner plans no screen for it");
                AssertEqual("3", string.Join(",", Alloc(plan, s).Shortfalls), "and the allocator reports only the sector he left (corps 3), none for the one he holds");
            }
            else
            {
                AssertEqual(TheaterPosture.Screen, PostureOfSector(plan, empty), "with the waypoint at his position nothing holds the sector");
                AssertTrue(screen, "and it gets a screen");
            }
        }
    }

    // G4 verify r3 F6: the hold sector is the waypoint's whatever the physical position says, and the position only
    // when the waypoint is not a usable point (NaN).
    private static void HoldSectorOfCreditsTheWaypointWhateverThePosition()
    {
        var s = Wide();
        var sectors = Sectors(s);
        int at500 = sectors.SectorAt(500f, 0f);
        int at300 = sectors.SectorAt(300f, 0f);
        var player = Corps(9, 300f, 20000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        player.WaypointX = 500f;
        AssertEqual(at500, ReserveRules.HoldSectorOf(sectors, player), "the waypoint's sector");
        player.X = float.NaN;
        AssertEqual(at500, ReserveRules.HoldSectorOf(sectors, player), "even with no usable position");
        player.WaypointX = float.NaN;
        player.X = 300f;
        AssertEqual(at300, ReserveRules.HoldSectorOf(sectors, player), "no usable waypoint: the position");
        var ai = Corps(2, 300f, 20000f);
        ai.WaypointX = 500f;
        AssertEqual(at300, ReserveRules.HoldSectorOf(sectors, ai), "an AI formation holds where it stands, whatever its order");
    }

    // F6 and F2 in the planner's sector facts: the loop must key every fact on HoldSectorOf and never skip a
    // formation on its physical sector first (the credit follows the waypoint, whatever the position).
    private static void SectorFactsCreditTheHoldSectorBeforeAnyPhysicalCheck()
    {
        string body = BodyOf(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/TheaterPlanner.cs"), "private static SectorFacts BuildSectorFacts(");
        int hold = body.IndexOf("int sector = ReserveRules.HoldSectorOf(c.Sectors, f);", StringComparison.Ordinal);
        int own = body.IndexOf("x.Own[sector] += ReserveRules.HoldStrength(f);", StringComparison.Ordinal);
        int count = body.IndexOf("x.Count[sector]++;", StringComparison.Ordinal);
        AssertTrue(hold >= 0 && own > hold && count > own, "one sector per formation, from HoldSectorOf: Own, then Count");
        AssertFalse(body.Contains("= SectorOf(c.Sectors, f)") || body.Contains("ReserveRules.SectorOf("), "no second, physical lookup in the facts loop");
    }

    // G4 verify r3 F4: a CIC alliance's side stands down before any hold sum is computed. The player's CicSide
    // chain is classified only on the player's own alliance, which the planner never plans, so the predicate is
    // pinned as well: CountsTowardCoverage is false for it.
    private static void CicStandDownPrecedesEveryHoldSum()
    {
        AssertFalse(WlChainRules.CountsTowardCoverage(WlChainClass.CicSide), "a CIC side holds nothing");
        AssertFalse(WlChainRules.CountsTowardCoverage(WlChainClass.PlayerCommand), "nor his own command");
        AssertTrue(WlChainRules.CountsTowardCoverage(WlChainClass.PlayerMapGroup), "the map-group player does");
        string review = BodyOf(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs"), "internal static void ReviewPlan(");
        int standDown = review.IndexOf("if (mode == PlannerReviewMode.StandDown) return;", StringComparison.Ordinal);
        int sectorBuild = review.IndexOf("SectorModel.Build(snapshot)", StringComparison.Ordinal);
        int plan = review.IndexOf("TheaterPlanner.Plan(", StringComparison.Ordinal);
        int allocate = review.IndexOf("ForceAllocator.Allocate(", StringComparison.Ordinal);
        AssertTrue(standDown >= 0 && sectorBuild > standDown && plan > standDown && allocate > standDown,
            "the CIC stand-down return precedes the sector model, the plan and the allocation");
        string allocator = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ForceAllocator.cs");
        string allocate2 = BodyOf(allocator, "public static Allocation Allocate(");
        AssertTrue(allocate2.IndexOf("if (StandsDown(s)) return allocation;", StringComparison.Ordinal) >= 0
            && allocate2.IndexOf("if (StandsDown(s)) return allocation;", StringComparison.Ordinal) < allocate2.IndexOf("new Pass(", StringComparison.Ordinal),
            "and the allocator's own stand-down precedes its pass");
    }

    // A threatened sector only the player stands in is Hold for the planner when he covers the need (20000
    // against about 6000) and reports no shortfall; a group too weak for it (2000) leaves the sector Screen and
    // reports the shortfall at his formation, which marks the position (G5b never routes to a player formation).
    private static void SectorHeldOnlyByThePlayerIsHeldUntilHeIsShort()
    {
        FormationRecord Group(float strength)
        {
            var player = Corps(5, 550f, strength);
            player.Chain = WlChainClass.PlayerMapGroup;
            return player;
        }
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Group(20000f));
        s.Beliefs.Add(Belief(600f, 0f, 10000f));
        var plan = PlanOf(s);
        int far = Sectors(s).SectorAt(550f, 0f);
        AssertEqual(TheaterPosture.Hold, PostureOfSector(plan, far), "he covers the need: the sector is held");
        var a = Alloc(plan, s);
        AssertEqual(0, a.Shortfalls.Count, "no shortfall");
        AssertEqual(FormationRole.Hold, Of(a, 5).Role, "he holds in place");
        AssertFalse(Of(a, 5).Withheld, "never withheld");
        AssertFalse(a.Surpluses.Contains(5), "never a surplus");
        AssertEqual(FormationRole.MainEffort, Of(a, 1).Role, "the free corps masses");

        var weak = Wide();
        weak.Own.Add(Corps(1, 200f, 20000f));
        weak.Own.Add(Group(2000f));
        weak.Beliefs.Add(Belief(600f, 0f, 10000f));
        var weakPlan = PlanOf(weak);
        AssertEqual(TheaterPosture.Screen, PostureOfSector(weakPlan, far), "2000 does not hold the sector");
        var b = Alloc(weakPlan, weak);
        AssertEqual("5", string.Join(",", b.Shortfalls), "the shortfall is reported at his formation");
        AssertFalse(Of(b, 5).Withheld, "still never withheld");
        AssertFalse(b.Surpluses.Contains(5), "never a surplus");

        var quiet = Wide();
        quiet.Own.Add(Group(20000f));
        var quietAllocation = Alloc(ManualPlan(quiet), quiet);
        AssertEqual(0, quietAllocation.Shortfalls.Count, "a quiet sector he alone stands in is no shortfall (no hold need to report)");
        AssertEqual(0, quietAllocation.Surpluses.Count, "and his unwithheld formation in a quiet sector is not a surplus either");
    }

    // M1 (D3, smoke amendments 2026-10-16): outside the mass the map-group player's group keeps its own front's role like any
    // formation: Hold in place. He is never ordered toward the main target, never withheld, never a reinforcement target or a surplus.
    private static void MapGroupPlayerKeepsHisFrontsRoleAndIsNeverCounted()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        var player = Corps(2, 350f, 6000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        s.Own.Add(player);
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
        AssertEqual(FormationRole.Hold, x.Role, "in place: no order toward the main target");
        AssertEqual(ForceAllocator.ReasonPlayerMapGroup, x.Reason, "reason");
        AssertNear(350f, x.TargetX, 0.01f, "at his own position");
        AssertFalse(x.Withheld, "never withheld");
        AssertTrue(!a.Shortfalls.Contains(2) && !a.Surpluses.Contains(2), "never a reinforcement target or a surplus");
        AssertFalse(plan.MassInstanceIds.Contains(2), "and never in the mass");
    }

    // G3 half of the decision: the planner's sector facts and the capital threat count the map-group player's
    // group at strength x morale where its last waypoint lies; his own command counts nothing.
    private static void PlannerCountsThePlayersGroupWhereItsWaypointLies()
    {
        TheaterPosture Posture(bool aiCorps, float waypointX)
        {
            var s = Wide();
            s.Own.Add(Corps(1, 200f, 20000f));
            var player = Corps(5, 550f, 20000f);
            player.Chain = WlChainClass.PlayerMapGroup;
            player.WaypointX = waypointX;
            s.Own.Add(player);
            if (aiCorps) s.Own.Add(Corps(4, 560f, 8000f));
            s.Beliefs.Add(Belief(600f, 0f, 10000f));
            return PostureOfSector(PlanOf(s), Sectors(s).SectorAt(550f, 0f));
        }
        AssertEqual(TheaterPosture.Hold, Posture(false, 550f), "his 20000 makes the sector he stands in Hold");
        AssertEqual(TheaterPosture.Screen, Posture(false, 100f), "bound for x 100 he no longer covers it");
        AssertEqual(TheaterPosture.Hold, Posture(true, 100f), "an AI corps that covers the hold need does");

        float CapitalTermWith(WlChainClass chain, float waypointX)
        {
            var s = Line(0, 5);
            s.Objectives.Add(Target(99, 100f, 1f));
            var defender = Corps(1, 50f, 12000f);
            defender.Chain = chain;
            defender.WaypointX = waypointX;
            s.Own.Add(defender);
            s.Own.Add(Corps(2, 250f, 20000f));
            s.Beliefs.Add(Belief(20f, 0f, 40000f));
            var inputs = new TheaterPlanner.Inputs { S = s, Sectors = Sectors(s), Speed = Speed() };
            var list = new List<TheaterPlanner.Candidate>();
            TheaterPlanner.Evaluate(inputs, list);
            return list[0].Capital;
        }
        float ai = CapitalTermWith(WlChainClass.None, 50f);
        AssertNear(ai, CapitalTermWith(WlChainClass.PlayerMapGroup, 50f), 0.0001f, "the map-group player near the capital defends it like an AI corps (vanilla counts him)");
        AssertTrue(CapitalTermWith(WlChainClass.PlayerMapGroup, 4000f) > ai + 0.01f, "marched away (waypoint beyond the relocation range) he no longer does");
        AssertTrue(CapitalTermWith(WlChainClass.PlayerCommand, 50f) > ai + 0.01f, "the player's own command never does");
    }

    // M4: pins the held sum of the shared hold reserve. Need 7000 in sector 2 facing an army at x 600: an AI
    // corps in battle (3000) counts; the map-group player's group (5000) counts at strength x morale (2500 at
    // morale 0.5, 5000 at 1) in the sector its last waypoint lies in; his own command (4000) counts nothing. At
    // morale 0.5 the held 5500 is short, so the nearest massable corps (4000, x 560) is picked and the next
    // (x 540) is not; at morale 1 the held 8000 meets the need and nothing is picked; with the group bound for
    // another sector the held 3000 is short again. A capital-reserved corps (4000, counts) meets the need too.
    private static void ReserveRulesHoldCountsTheMapGroupPlayerNotHisCommand()
    {
        int[] Run(float morale, float waypointX, bool capitalReservedCorps, out int sector)
        {
            var s = Wide();
            s.Own.Add(WithFlags(Corps(1, 530f, 3000f), FormationFlags.InBattle));
            var group = Corps(2, 520f, 5000f);
            group.Chain = WlChainClass.PlayerMapGroup;
            group.Morale = morale;
            group.WaypointX = waypointX;
            s.Own.Add(group);
            var command = Corps(3, 510f, 4000f);
            command.Chain = WlChainClass.PlayerCommand;
            s.Own.Add(command);
            s.Own.Add(Corps(4, 560f, 4000f));
            s.Own.Add(Corps(5, 540f, 4000f));
            var sectors = Sectors(s);
            sector = sectors.SectorAt(550f, 0f);
            var need = new float[sectors.Count];
            var nearX = new float[sectors.Count];
            var nearZ = new float[sectors.Count];
            need[sector] = 7000f;
            nearX[sector] = 600f;
            var capital = new bool[s.Own.Count];
            capital[4] = capitalReservedCorps;
            var held = new int[s.Own.Count];
            ReserveRules.Hold(s, sectors, need, nearX, nearZ, capital, held);
            return held;
        }
        var half = Run(0.5f, 520f, false, out int k);
        AssertEqual(k, half[3], "the nearest massable corps is reserved (3000 + 2500 held, 4000 more)");
        AssertEqual(-1, half[4], "the next is not");
        for (int i = 0; i < 3; i++) AssertEqual(-1, half[i], "formation " + (i + 1) + " is never reserved");

        var full = Run(1f, 520f, false, out _);
        for (int i = 0; i < 5; i++) AssertEqual(-1, full[i], "the group at full morale (5000 + 3000) meets the need: nothing is reserved");

        var away = Run(1f, 100f, false, out _);
        AssertEqual(k, away[3], "bound for another sector, his 5000 does not count here: the nearest corps is reserved");
        AssertEqual(-1, away[1], "and he is never reserved himself");

        var guarded = Run(0.5f, 520f, true, out _);
        AssertEqual(-1, guarded[3], "a capital-reserved corps holds anyway (3000 + 2500 + 4000): nothing more is reserved");
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
            s.Reset(alliance, alliance, Day, PlayerCommandClass.CommanderInChief, -1, null);
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
        AssertEqual(0, ForceAllocator.Allocate(new TheaterPlan { Alliance = 0 }, own, Sectors(own), null, null, null).Assignments.Count, "the CIC player's own side stands down");
        var enemy = Side(1, WlChainClass.None);
        enemy.Objectives.Add(new ObjectiveRecord { ObjectiveId = 1, X = 100f, Z = 0f, HolderAlliance = 0, Available = true, Importance01 = 1f });
        AssertEqual(1, ForceAllocator.Allocate(new TheaterPlan { Alliance = 1 }, enemy, Sectors(enemy), null, null, null).Assignments.Count,
            "the enemy side of a CIC career keeps its allocation although the snapshot's Player class is global");
    }

    // ---- home department ----

    // D6 detail (c): the allocator's lookup is the planner state's home department by unit key; a formation whose department is the
    // sector's theater is treated as 25% closer to the sector's army when the hold is ordered. Equal distances: the lower id first.
    private static void HomeDepartmentBreaksTies()
    {
        bool DepartmentCorpsWithheld(bool useHome)
        {
            var s = Wide();
            var plain = Corps(7, 520f, 6000f);
            var west = Corps(8, 580f, 6000f);
            s.Own.Add(plain);
            s.Own.Add(west);
            var departments = new Dictionary<string, Theater> { { west.UnitKey, Theater.West } };
            var a = Alloc(ManualPlan(s), s, useHome ? (Func<string, Theater>)(key => departments.TryGetValue(key, out var d) ? d : Theater.Unknown) : null);
            AssertTrue(Of(a, 7).Withheld != Of(a, 8).Withheld, "exactly one of the two reaches the 5000 garrison (useHome " + useHome + ")");
            return Of(a, 8).Withheld;
        }
        AssertFalse(DepartmentCorpsWithheld(false), "equal distance: the lower instance id is withheld first");
        AssertTrue(DepartmentCorpsWithheld(true), "corps 8 (home department West) sits in a West sector and is withheld first");
    }

    // The home discount is a real preference, not a tie-break: a corps 20% farther from the sector's army is still withheld first when
    // it belongs to the sector's department.
    private static void HomeDepartmentOutweighsADistanceMargin()
    {
        var s = Wide();
        var near = Corps(7, 505f, 6000f);        // 45 from the sector's centroid (550)
        var home = Corps(8, 600f, 6000f);        // 50 from it, 11% farther, but the sector's own department: 50 x 0.75 = 37.5
        s.Own.Add(near);
        s.Own.Add(home);
        var departments = new Dictionary<string, Theater> { { home.UnitKey, Theater.West }, { near.UnitKey, Theater.East } };
        var a = Alloc(ManualPlan(s), s, key => departments.TryGetValue(key, out var d) ? d : Theater.Unknown);
        AssertTrue(Of(a, 8).Withheld && !Of(a, 7).Withheld, "the sector's own department is kept to hold it");
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
        ledger.Sync(s, tasks, a);
        AssertEqual(2, ledger.Missions[0].InstanceId, "Sync paired the assignment with its task and opened the mission");
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

    // D6: Support is the one reinforcement the mass took from another theater, marching to the same point as the mass; a formation
    // outside the mass in a Support sector is not Support (D3), it holds where it stands.
    private static void SupportIsTheReinforcementAndNothingElse()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(2, 350f, 6000f));
        s.Own.Add(Corps(3, 360f, 6000f));
        var plan = ManualPlan(s);
        int main = Sectors(s).SectorAt(100f, 0f);
        int support = Sectors(s).SectorAt(350f, 0f);
        plan.MainSectorId = main;
        plan.MainObjectiveId = 98;
        plan.Sectors.Add(new SectorPlan { SectorId = main, Posture = TheaterPosture.MainEffort, TargetObjectiveId = 98, TargetX = 100f, TargetZ = 0f });
        plan.Sectors.Add(new SectorPlan { SectorId = support, Posture = TheaterPosture.Support, TargetObjectiveId = 98, TargetX = 100f, TargetZ = 0f });
        plan.MassInstanceIds.AddRange(new[] { 1, 2 });
        plan.ReinforcementInstanceId = 2;
        var a = Alloc(plan, s);
        var x = Of(a, 2);
        AssertEqual(FormationRole.Support, x.Role, "the reinforcement is the push's Support");
        AssertNear(100f, x.TargetX, 0.01f, "toward the main target, as the mass");
        AssertEqual(main, x.SectorId, "its role sector is the main sector (the objective it was brought for)");
        AssertFalse(x.Withheld, "Support is vanilla's to use");
        AssertEqual(FormationRole.MainEffort, Of(a, 1).Role, "the rest of the mass is MainEffort");
        var other = Of(a, 3);
        AssertEqual(FormationRole.Hold, other.Role, "a formation outside the mass in the same Support sector holds");
        AssertNear(360f, other.TargetX, 0.01f, "where it stands");

        // Staged: the reinforcement joins the mass at the start line.
        plan.MainStaged = true;
        plan.StartLineX = 200f;
        var staged = Alloc(plan, s);
        AssertNear(200f, Of(staged, 2).TargetX, 0.01f, "staged: the start line");
        AssertNear(200f, Of(staged, 1).TargetX, 0.01f, "for the whole mass");
    }

    private static void AllianceTwoAndEmptyInputsYieldEmptyAllocation()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        var sectors = Sectors(s);
        var plan = ManualPlan(s);
        AssertEqual(0, ForceAllocator.Allocate(null, s, sectors, null, null, null).Assignments.Count, "no plan");
        AssertEqual(0, ForceAllocator.Allocate(plan, null, sectors, null, null, null).Assignments.Count, "no snapshot");
        AssertEqual(0, ForceAllocator.Allocate(plan, s, null, null, null, null).Assignments.Count, "no sectors");
        var two = new CampaignSnapshot();
        two.Reset(2, 2, Day, PlayerCommandClass.NotCareer, -1, null);
        two.Own.Add(Corps(1, 200f, 20000f));
        AssertEqual(0, ForceAllocator.Allocate(plan, two, sectors, null, null, null).Assignments.Count, "alliance 2");
        var none = Wide();
        AssertEqual(0, ForceAllocator.Allocate(plan, none, Sectors(none), null, null, null).Assignments.Count, "no formations");
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
        AssertEqual("[Allocation] alliance=0 main=2 support=0 hold=0 reserve=1 capital=0 depot=0 recover=0 recon=0 busy=1 player=MapGroup reliability=0.76 unreliableDays=3 mass=24000 need=0 cap=0 staged=no garrison=none short=0 surplus=1", row,
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
        foreach (string file in new[] { "ForceAllocator.cs", "ReserveRules.cs" })
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
        AssertContains(review, "ForceAllocator.Allocate(plan, snapshot, sectors, DepartmentLookups[alliance], ReconTasks[alliance], Recon[alliance], gaveUp, heldBuilders);", "the plan, its snapshot and sectors, the home theaters, this alliance's recon tasks and ledger; reliability and the unreliable days are telemetry only");
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
        s.Reset(alliance, alliance, Day, PlayerCommandClass.NotCareer, -1, null);
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
        return new SupplySourceRecord { Id = id, Kind = SupplySourceKind.Iip, Owner = owner, X = x, Z = 0f, Condition = 1f, Built = true };
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
        plan.MainEnemyStrength = EnemyBeliefQuery.StrengthWithin(s.Beliefs, targetX, 0f, s.RelocationRange); // what G3 judged at the objective
        plan.Sectors.Add(new SectorPlan { SectorId = sector, Posture = TheaterPosture.MainEffort, TargetObjectiveId = 98, TargetX = targetX, TargetZ = 0f, RequiredRatio = 1.1f });
        plan.MassInstanceIds.AddRange(mass);
        return plan;
    }

    private static Allocation Alloc(TheaterPlan plan, CampaignSnapshot s,
                                    Func<string, Theater> home = null, IReadOnlyList<ReconTask> recon = null, ReconMissionLedger missions = null)
    {
        return ForceAllocator.Allocate(plan, s, Sectors(s), home, recon, missions);
    }

    private static Assignment Of(Allocation a, int instanceId)
    {
        AssertTrue(a.TryGet(instanceId, out var x), "formation " + instanceId + " has an assignment");
        return x;
    }

    private static TheaterPosture PostureOfSector(TheaterPlan plan, int sectorId)
    {
        foreach (var sp in plan.Sectors)
            if (sp.SectorId == sectorId) return sp.Posture;
        throw new Exception("no plan for sector " + sectorId);
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
