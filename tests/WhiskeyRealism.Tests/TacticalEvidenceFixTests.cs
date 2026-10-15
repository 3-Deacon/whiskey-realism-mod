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
        Run("scheduled arrival strength uses vanilla groupstrength", ScheduledArrivalStrengthUsesGroupStrength);
        Run("front axis right vector matches play projection handedness", FrontAxisHandednessMatchesPlayProjection);
        Run("front axis facing north orders left to right by world x", FrontAxisFacingNorthUsesWorldX);
        Run("front axis facing east orders across the front not by world x", FrontAxisFacingEastIgnoresWorldX);
        Run("front axis facing south swaps world x order", FrontAxisFacingSouthSwapsWorldX);
        Run("front axis bands clamp and center", FrontAxisBandsClampAndCenter);
        Run("front axis degenerate target defaults forward to +z", FrontAxisDegenerateTargetDefaults);
        Run("front axis stabilize keeps heading under threshold", FrontAxisStabilizeKeepsHeading);
        Run("front axis enemy overlap per flank", FrontAxisEnemyOverlap);
        Run("flank exposure bucket combines wing overlap and vanilla threat", FlankExposureBucketCombines);
        Run("band sectors dedupe enemies seen by several own units", BandSectorsDedupeEnemies);
        Run("band sectors aggregate own strength by lateral band", BandSectorsAggregateOwnByBand);
        Run("instance sector map is keyed by instance id and stable when a group routs", InstanceSectorMapStableAcrossRout);
        Run("sector odds ignore pickets below the meaningful-enemy floor", SectorOddsIgnorePickets);
        Run("enemy visible state carries axis and instance sectors", EnemyVisibleStateCarriesGeometry);
        Run("command tree children sort along the front axis", CommandTreeChildrenSortAlongFront);
        Run("default main effort is the center band", DefaultMainEffortIsCenterBand);
        Run("picket in the main-effort band does not zero main-effort odds", PicketDoesNotZeroMainEffortOdds);
        Run("leaf cascade refuses the flanks across the front when facing east", LeafCascadeRefusesAcrossFrontFacingEast);
        Run("leaf cascade feeds real flank exposure to cautious commanders", LeafCascadeUsesRealFlankExposure);
        Run("envelopment splits the army's corps into wings, not one corps's divisions", EnvelopmentSplitsArmyCorps);
        Run("orchestrator keeps the observed front axis for the leaf cascade", OrchestratorKeepsFrontAxis);
        Run("contact ledger before contact is unknown and low confidence", ContactLedgerBeforeContact);
        Run("contact ledger tracks largest seen and engaged share", ContactLedgerTracksContact);
        Run("contact ledger reports broken contact then forgets it", ContactLedgerContactBroken);
        Run("contact ledger picket gives low confidence", ContactLedgerPicketLowConfidence);
        Run("contact ledger resets when the battle clock runs backwards", ContactLedgerResetsOnClockRewind);
        Run("own aibattle resolves by sideofai and skips the feud instance", OwnAiBattleSkipsFeudInstance);
        Run("intent inference strength signal is relative to own force", IntentStrengthSignalRelative);
        Run("casualty trigger fires on the crossing, not while morale stays low", CasualtyTriggerIsEdge);
        Run("reserve exhaustion fires once per crossing", ReserveExhaustionIsEdge);
        Run("reinforcement trigger uses share of own strength and fires once", ReinforcementTriggerIsShareAndEdge);
        Run("enemy intent shift needs a changed intent", EnemyIntentShiftNeedsChange);
        Run("replans preserve the current phase and stop the withdraw-probe loop", ReplanPreservesPhaseNoThrash);
        Run("main effort sector loss uses the main band's own strength history", MainEffortSectorLossFromBandHistory);
        Run("morale is normalized against battle-start morale", MoraleNormalizedToBattleStart);
        Run("phase budgets are civil-war battle minutes", PhaseBudgetsAreBattleMinutes);
        Run("main effort consolidates on stalled progress, not elapsed time", MainEffortConsolidatesOnStallNotClock);
        Run("plan deadline runs on battle time", PlanDeadlineRunsOnBattleTime);
        Run("opportunity hysteresis holds on battle time", OpportunityHysteresisOnBattleTime);
        Run("main arrival hold budget is battle minutes", MainArrivalHoldIsBattleMinutes);
        Run("defensive hold is no opinion and nothing maps to retreat", MacroMappingNoOpinionNoRetreat);
        Run("meeting and attacker stances attack at parity like vanilla", DoctrineAttacksAtParityForMeetingAndAttacker);
        Run("attack threshold formula pinned to its documented values", AttackThresholdPinned);
        Run("stance reconciler keeps attacker on defensive hold", ReconcilerKeepsAttackerOnDefensiveHold);
        Run("registry matches full names, not colliding surnames", RegistryMatchesFullNames);
        Run("registry builds the full name from firstname and name", RegistryUsesFirstAndLastName);
        Run("terrain classifier reads vanilla terrain ids and relief", TerrainClassifierReadsVanillaIds);
        Run("reserve stays reserve without a release condition", ReserveHeldWithoutRelease);
        Run("reserve is released into the exploit phase", ReserveReleasedOnExploit);
        Run("reserve is released at the plan's reserve-commit odds", ReserveReleasedAtCommitOdds);
        Run("reserve reinforces an adjacent sector in crisis", ReserveReinforcesCrisisSector);
        Run("main role needs a challenger to win three cycles running", MainRoleHysteresis);
        Run("replan cooldown is the configured battle seconds, no hidden floor", ReplanCooldownIsConfiguredValue);
        Run("a paused night leaves phase budgets unchanged", PausedNightLeavesPhaseBudgets);
        Run("a paused night leaves contact-ledger decay unchanged", PausedNightLeavesLedgerDecay);
        Run("army odds stay unknown while intel confidence is low", ArmyOddsUnknownUnderLowConfidence);
        Run("picket-only contact keeps the army probing end to end", PicketOnlyContactKeepsProbe);
        Run("morning odds after a paused night are not inflated", MorningOddsNotInflated);
        Run("every registry name is a vanilla commanders.txt name", RegistryNamesExistInVanilla);
        Run("the orchestrator's one observe path releases reserves and holds Main", OrchestratorObservePathAppliesReleaseAndHysteresis);
        Run("evidence is built once per side per frame and shared", EvidenceMemoSharesWithinFrame);
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

    private static void ScheduledArrivalStrengthUsesGroupStrength()
    {
        // Vanilla sums regimentrefreg.groupstrength (after UpdateGroupStrength with
        // ignoreactivestate) into strengthtoarrive (84828-84833). groupstrengthaigroup
        // is 0 for not-yet-active formations.
        TestHarness.AssertEqual(4200f, ArmyEvidenceBuilder.ArrivalStrength(groupStrength: 4200, groupStrengthAiGroup: 0), "groupstrength wins");
        TestHarness.AssertEqual(900f, ArmyEvidenceBuilder.ArrivalStrength(0, 900), "aigroup fallback when groupstrength unset");
        TestHarness.AssertEqual(0f, ArmyEvidenceBuilder.ArrivalStrength(-5, -5), "negatives clamp to 0");
    }

    // ---- T3/T4 geometry ----

    private static bool Near(float a, float b) => Math.Abs(a - b) < 1e-4f;

    private static void FrontAxisHandednessMatchesPlayProjection()
    {
        // PlayProjection.Project: right = (forwardZ, -forwardX). Allocator treats
        // PrimarySector < mainSector as LEFT; cascade puts RefuseLeft at index 0.
        // All three must agree: lower band index = more negative lateral = left.
        var axis = TacticalFrontAxis.FromCentroids(0f, 0f, 30f, 40f);
        TestHarness.AssertTrue(Near(axis.ForwardX, 0.6f) && Near(axis.ForwardZ, 0.8f), "forward normalized");
        TestHarness.AssertTrue(Near(axis.RightX, axis.ForwardZ) && Near(axis.RightZ, -axis.ForwardX), "right = (fz, -fx)");
        TestHarness.AssertTrue(Near(axis.TargetDistance, 50f), "target distance kept for play projection depth");
    }

    private static TacticalFrontAxis Banded(float ownX, float ownZ, float tX, float tZ)
    {
        return TacticalFrontAxis.FromCentroids(ownX, ownZ, tX, tZ).WithOwnExtent(-100f, 100f, 5);
    }

    private static void FrontAxisFacingNorthUsesWorldX()
    {
        var axis = Banded(0f, 0f, 0f, 500f);
        TestHarness.AssertTrue(axis.SectorOf(-80f, 0f) < axis.SectorOf(80f, 0f), "facing +Z: west is left");
        TestHarness.AssertEqual(axis.SectorOf(0f, -50f), axis.SectorOf(0f, 50f), "depth does not change band");
    }

    private static void FrontAxisFacingEastIgnoresWorldX()
    {
        var axis = Banded(0f, 0f, 500f, 0f);
        // Facing +X the left hand points to +Z.
        TestHarness.AssertTrue(axis.SectorOf(0f, 80f) < axis.SectorOf(0f, -80f), "facing east: north is left");
        TestHarness.AssertEqual(axis.SectorOf(-80f, 0f), axis.SectorOf(80f, 0f), "world-X difference is depth, same band");
    }

    private static void FrontAxisFacingSouthSwapsWorldX()
    {
        var axis = Banded(0f, 0f, 0f, -500f);
        TestHarness.AssertTrue(axis.SectorOf(80f, 0f) < axis.SectorOf(-80f, 0f), "facing south: east is left");
    }

    private static void FrontAxisBandsClampAndCenter()
    {
        var axis = Banded(0f, 0f, 0f, 500f);
        TestHarness.AssertEqual(5, axis.BandCount, "band count");
        TestHarness.AssertEqual(2, axis.CenterSector, "center band");
        TestHarness.AssertEqual(0, axis.SectorOf(-100f, 0f), "left edge");
        TestHarness.AssertEqual(2, axis.SectorOf(0f, 0f), "center");
        TestHarness.AssertEqual(4, axis.SectorOf(100f, 0f), "right edge");
        TestHarness.AssertEqual(4, axis.SectorOf(900f, 0f), "beyond right clamps");
        TestHarness.AssertEqual(0, axis.SectorOf(-900f, 0f), "beyond left clamps");
    }

    private static void FrontAxisDegenerateTargetDefaults()
    {
        var axis = TacticalFrontAxis.FromCentroids(10f, 10f, 10f, 10.2f);
        TestHarness.AssertTrue(axis.IsValid, "degenerate still valid");
        TestHarness.AssertTrue(Near(axis.ForwardX, 0f) && Near(axis.ForwardZ, 1f), "forward defaults to +Z like PlayProjection");
        TestHarness.AssertFalse(TacticalFrontAxis.None.IsValid, "None is invalid");
        TestHarness.AssertEqual(0, TacticalFrontAxis.None.SectorOf(5f, 5f), "None maps to sector 0");
    }

    private static void FrontAxisStabilizeKeepsHeading()
    {
        var prev = TacticalFrontAxis.FromCentroids(0f, 0f, 0f, 100f);
        double small = 5.0 * Math.PI / 180.0;
        var nudged = TacticalFrontAxis.FromCentroids(20f, 0f, 20f + (float)(100.0 * Math.Sin(small)), (float)(100.0 * Math.Cos(small)));
        var kept = TacticalFrontAxis.Stabilize(prev, nudged, 10f);
        TestHarness.AssertTrue(Near(kept.ForwardX, prev.ForwardX) && Near(kept.ForwardZ, prev.ForwardZ), "5 deg wobble keeps heading");
        TestHarness.AssertTrue(Near(kept.OriginX, 20f), "origin still follows the army");
        var turned = TacticalFrontAxis.FromCentroids(0f, 0f, 100f, 100f);
        var accepted = TacticalFrontAxis.Stabilize(prev, turned, 10f);
        TestHarness.AssertTrue(Near(accepted.ForwardX, turned.ForwardX), "45 deg turn accepted");
        var first = TacticalFrontAxis.Stabilize(TacticalFrontAxis.None, turned, 10f);
        TestHarness.AssertTrue(Near(first.ForwardX, turned.ForwardX), "no previous -> accept");
    }

    private static void FrontAxisEnemyOverlap()
    {
        var axis = Banded(0f, 0f, 0f, 500f).WithEnemyExtent(-300f, 50f);
        TestHarness.AssertTrue(axis.EnemyOverlapsLeft, "enemy line extends past our left");
        TestHarness.AssertFalse(axis.EnemyOverlapsRight, "enemy line short of our right");
        TestHarness.AssertFalse(Banded(0f, 0f, 0f, 500f).EnemyOverlapsLeft, "no enemy extent -> no overlap");
    }

    private static void FlankExposureBucketCombines()
    {
        TestHarness.AssertEqual(0, TacticalFrontAxis.FlankExposureBucket(false, false, 0f), "interior, quiet");
        TestHarness.AssertEqual(1, TacticalFrontAxis.FlankExposureBucket(true, false, 0f), "wing alone");
        TestHarness.AssertEqual(2, TacticalFrontAxis.FlankExposureBucket(true, true, 0f), "wing + overlapped");
        TestHarness.AssertEqual(3, TacticalFrontAxis.FlankExposureBucket(true, true, 1f), "wing + overlapped + vanilla threat -> refuse");
        TestHarness.AssertEqual(3, TacticalFrontAxis.FlankExposureBucket(true, false, 3f), "wing + severe vanilla outflank -> refuse");
        TestHarness.AssertEqual(2, TacticalFrontAxis.FlankExposureBucket(false, false, 4f), "interior surrounded caps at 2");
    }

    private static FrontUnitSample U(int id, float x, float z, float strength, bool fire = false)
        => new FrontUnitSample(id, x, z, strength, fire);

    private static void BandSectorsDedupeEnemies()
    {
        var axis = Banded(0f, 0f, 0f, 500f);
        var own = new List<FrontUnitSample> { U(1, 0f, 0f, 1000f), U(2, 10f, 0f, 1000f) };
        // Enemy 77 reported by three own regiments (union of enemyinrangereg lists).
        var enemy = new List<FrontUnitSample> { U(77, 0f, 300f, 500f), U(77, 0f, 300f, 500f), U(77, 0f, 300f, 500f) };
        var sectors = EnemyVisibleSectorBuilder.BuildSectors(axis, own, enemy);
        float enemyTotal = 0f;
        for (int i = 0; i < sectors.Length; i++) enemyTotal += sectors[i].EnemyStrength;
        TestHarness.AssertEqual(500f, enemyTotal, "enemy counted once");
    }

    private static void BandSectorsAggregateOwnByBand()
    {
        var axis = Banded(0f, 0f, 0f, 500f);
        var own = new List<FrontUnitSample>
        {
            U(1, -90f, 0f, 600f, fire: true), U(2, -85f, 10f, 400f),
            U(3, 90f, 0f, 2000f),
        };
        var sectors = EnemyVisibleSectorBuilder.BuildSectors(axis, own, new List<FrontUnitSample>());
        TestHarness.AssertEqual(2, sectors.Length, "only populated bands emitted");
        var left = Find(sectors, 0);
        var right = Find(sectors, 4);
        TestHarness.AssertEqual(1000f, left.OwnStrength, "left band sums both regiments");
        TestHarness.AssertTrue(left.RecentFire, "left band under fire");
        TestHarness.AssertEqual(2000f, right.OwnStrength, "right band");
        TestHarness.AssertFalse(right.RecentFire, "right band quiet");
    }

    private static EnemyVisibleSector Find(EnemyVisibleSector[] sectors, int id)
    {
        for (int i = 0; i < sectors.Length; i++) if (sectors[i].SectorId == id) return sectors[i];
        throw new Exception("sector " + id + " missing");
    }

    private static void InstanceSectorMapStableAcrossRout()
    {
        var axis = Banded(0f, 0f, 0f, 500f);
        var groups = new List<FrontUnitSample> { U(10, -90f, 0f, 1f), U(20, 0f, 0f, 1f), U(30, 90f, 0f, 1f) };
        var map = EnemyVisibleSectorBuilder.MapInstances(axis, groups);
        TestHarness.AssertEqual(0, map[10], "left group");
        TestHarness.AssertEqual(2, map[20], "center group");
        TestHarness.AssertEqual(4, map[30], "right group");
        groups.RemoveAt(0); // group 10 routs and drops out of the walk
        var after = EnemyVisibleSectorBuilder.MapInstances(axis, groups);
        TestHarness.AssertEqual(2, after[20], "center group keeps its sector");
        TestHarness.AssertEqual(4, after[30], "right group keeps its sector");
    }

    private static void SectorOddsIgnorePickets()
    {
        var picket = new EnemyVisibleSector(0, 5000f, 50f, false);
        TestHarness.AssertFalse(picket.HasMeaningfulEnemy, "50 vs 5000 is a picket");
        TestHarness.AssertEqual(0f, picket.Odds, "picket odds not meaningful");
        var real = new EnemyVisibleSector(1, 5000f, 1000f, true);
        TestHarness.AssertTrue(real.HasMeaningfulEnemy, "1000 vs 5000 is a real force");
        TestHarness.AssertEqual(5f, real.Odds, "real odds");
        var outnumbered = new EnemyVisibleSector(2, 0f, 800f, true);
        TestHarness.AssertTrue(outnumbered.HasMeaningfulEnemy, "enemy facing an empty band is meaningful");
        TestHarness.AssertEqual(0f, outnumbered.Odds, "no own force -> odds 0");
    }

    private static void EnemyVisibleStateCarriesGeometry()
    {
        var axis = Banded(0f, 0f, 0f, 500f);
        var map = new Dictionary<int, int> { { 10, 0 }, { 20, 4 } };
        var threat = new Dictionary<int, float> { { 20, 3f } };
        var state = new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0.35f, false, false, 0f,
            frontAxis: axis, instanceSectors: map, instanceFlankThreat: threat);
        TestHarness.AssertEqual(3f, state.FlankThreatForInstance(20), "vanilla flanksthreated carried");
        TestHarness.AssertEqual(0f, state.FlankThreatForInstance(10), "missing threat reads 0");
        map[10] = 3; // caller mutation must not leak in
        TestHarness.AssertTrue(state.FrontAxis.IsValid, "axis carried");
        TestHarness.AssertEqual(0, state.SectorForInstance(10, -1), "copied map");
        TestHarness.AssertEqual(4, state.SectorForInstance(20, -1), "copied map right");
        TestHarness.AssertEqual(-1, state.SectorForInstance(99, -1), "unknown id -> fallback");
        var legacy = new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0.35f, false, false, 0f);
        TestHarness.AssertFalse(legacy.FrontAxis.IsValid, "legacy ctor has no axis");
        TestHarness.AssertEqual(7, legacy.SectorForInstance(10, 7), "legacy ctor falls back");
    }

    private static void CommandTreeChildrenSortAlongFront()
    {
        // Facing east: children differ in Z (lateral) and X (depth). World-X
        // order would put the rear child first; lateral order must not.
        var probes = new List<TacticalCommandTreeProbe.ExtendedProbe>
        {
            new TacticalCommandTreeProbe.ExtendedProbe(1, 16, "army", true, 0, false, 0f, 0f, 3),
            new TacticalCommandTreeProbe.ExtendedProbe(2, 15, "north", true, 1, true, 50f, 200f, 2),
            new TacticalCommandTreeProbe.ExtendedProbe(3, 15, "center", true, 1, true, -300f, 0f, 2),
            new TacticalCommandTreeProbe.ExtendedProbe(4, 15, "south", true, 1, true, 10f, -200f, 2),
        };
        var tree = TacticalCommandTreeProbe.BuildTree(probes, 0);
        var axis = TacticalFrontAxis.FromCentroids(0f, 0f, 1000f, 0f);
        var ordered = TacticalCommandTreeProbe.EnumerateChildrenLeftToRight(1, tree, axis);
        TestHarness.AssertEqual("north", ordered[0].Name, "left (north) first when facing east");
        TestHarness.AssertEqual("center", ordered[1].Name, "center second");
        TestHarness.AssertEqual("south", ordered[2].Name, "right (south) last");
        var legacy = TacticalCommandTreeProbe.EnumerateChildrenLeftToRight(1, tree);
        TestHarness.AssertEqual("center", legacy[0].Name, "no axis keeps legacy world-X order");
    }

    private static void DefaultMainEffortIsCenterBand()
    {
        TestHarness.AssertEqual(2, ArmyEvidenceBuilder.DefaultMainEffortSector(TacticalFrontAxis.None), "no axis -> center of default bands");
        TestHarness.AssertEqual(2, ArmyEvidenceBuilder.DefaultMainEffortSector(Banded(0f, 0f, 0f, 100f)), "banded axis center");
    }

    internal static ArmyOrchestrator OrchestratorWithPlan(BattlePhase phase, int mainSector, float aggression = 0f)
    {
        var personality = new WhiskeyRealism.Strategic.PersonalityVector(aggression, 0f, 0f, 0f, 0f);
        var orch = new ArmyOrchestrator(0, BuiltInPlaybooks.SeedCatalog(), personality);
        orch.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.GenericMethodical, phase, mainSector,
            null, null, 1.2f, 0f, 7));
        return orch;
    }

    private static void PicketDoesNotZeroMainEffortOdds()
    {
        ArmyTickCycle.ResetForTest();
        var orch = OrchestratorWithPlan(BattlePhase.Probe, mainSector: 2);
        // Main band (2) faces only a 40-man picket; the army as a whole has 1.3x.
        var sectors = new[] { new EnemyVisibleSector(2, 6000f, 40f, false) };
        var enemy = new EnemyVisibleState(sectors, 0.35f, true, false, 0f);
        ArmyTickCycle.MaybeReplan(orch, 1f, new ArmyEvidence(1.3f, TerrainKind.Open, 2), enemy,
            6000f, 1f, 0.35f, 0f, 60, 0f, 1f, 0f, 0f, battleDeltaSeconds: 1f);
        TestHarness.AssertEqual(BattlePhase.MainEffort, orch.CurrentPlan.Phase,
            "picket band falls back to global odds (1.3) and probe commits");
    }

    private static TacticalCommandTreeProbe.ExtendedProbe P(int id, int typ, string name, int parent, float x, float z, int bucket = 2)
        => new TacticalCommandTreeProbe.ExtendedProbe(id, typ, name, true, parent, parent == 1, x, z, bucket);

    private static void LeafCascadeRefusesAcrossFrontFacingEast()
    {
        // Army 1 -> Main corps 2 -> five brigades spread north..south, with
        // world X scrambled (depth). Facing east: north is LEFT.
        var probes = new List<TacticalCommandTreeProbe.ExtendedProbe>
        {
            P(1, 16, "army", 0, 0f, 0f),
            P(2, 15, "corps", 1, 0f, 0f),
            P(10, 14, "b-north", 2, 90f, 400f),
            P(11, 14, "b-nmid", 2, -60f, 200f),
            P(12, 14, "b-center", 2, 30f, 0f),
            P(13, 14, "b-smid", 2, -90f, -200f),
            P(14, 14, "b-south", 2, 60f, -400f),
        };
        var tree = TacticalCommandTreeProbe.BuildTree(probes, 0);
        var axis = TacticalFrontAxis.FromCentroids(0f, 0f, 2000f, 0f).WithOwnExtent(-400f, 400f, 5);
        var map = TacticalLeafBrigadeMap.BuildMap(tree,
            new[] { new TacticalLeafBrigadeMap.TopAssignment(2, DirectChildRole.Main) },
            0.5f, CascadeEnvelopmentMode.None, axis);
        TestHarness.AssertEqual(DirectChildRole.RefuseLeft, map[10].LeafRole, "north brigade refuses the left");
        TestHarness.AssertEqual(DirectChildRole.Main, map[12].LeafRole, "center brigade is the anchor");
        TestHarness.AssertEqual(DirectChildRole.RefuseRight, map[14].LeafRole, "south brigade refuses the right");
    }

    private static void LeafCascadeUsesRealFlankExposure()
    {
        // Cautious commander: outer children refuse only when the flank is
        // actually exposed, otherwise they are held in reserve.
        var probes = new List<TacticalCommandTreeProbe.ExtendedProbe>
        {
            P(1, 16, "army", 0, 0f, 0f),
            P(2, 15, "corps", 1, 0f, 0f),
            P(10, 14, "b-left", 2, -400f, 0f),
            P(11, 14, "b-lmid", 2, -200f, 0f),
            P(12, 14, "b-center", 2, 0f, 0f),
            P(13, 14, "b-rmid", 2, 200f, 0f),
            P(14, 14, "b-right", 2, 400f, 0f),
        };
        var tree = TacticalCommandTreeProbe.BuildTree(probes, 0);
        // Facing north; visible enemy line overlaps our LEFT only.
        var axis = TacticalFrontAxis.FromCentroids(0f, 0f, 0f, 2000f).WithOwnExtent(-400f, 400f, 5).WithEnemyExtent(-900f, 300f);
        var map = TacticalLeafBrigadeMap.BuildMap(tree,
            new[] { new TacticalLeafBrigadeMap.TopAssignment(2, DirectChildRole.Main) },
            0.2f, CascadeEnvelopmentMode.None, axis);
        TestHarness.AssertEqual(DirectChildRole.RefuseLeft, map[10].LeafRole, "overlapped left wing refuses");
        TestHarness.AssertEqual(DirectChildRole.Reserve, map[14].LeafRole, "quiet right wing held in reserve");
        var threat = new Dictionary<int, float> { { 14, 2f } };
        var threatened = TacticalLeafBrigadeMap.BuildMap(tree,
            new[] { new TacticalLeafBrigadeMap.TopAssignment(2, DirectChildRole.Main) },
            0.2f, CascadeEnvelopmentMode.None, axis, threat);
        TestHarness.AssertEqual(DirectChildRole.RefuseRight, threatened[14].LeafRole, "vanilla flanksthreated on the right wing refuses");
    }

    private static void EnvelopmentSplitsArmyCorps()
    {
        // Army with three corps left/center/right, two brigades each.
        var probes = new List<TacticalCommandTreeProbe.ExtendedProbe>
        {
            P(1, 16, "army", 0, 0f, 0f),
            P(2, 15, "corps-left", 1, -600f, 0f),
            P(3, 15, "corps-center", 1, 0f, 0f),
            P(4, 15, "corps-right", 1, 600f, 0f),
            P(20, 14, "l1", 2, -700f, 0f), P(21, 14, "l2", 2, -500f, 0f),
            P(30, 14, "c1", 3, -100f, 0f), P(31, 14, "c2", 3, 100f, 0f),
            P(40, 14, "r1", 4, 500f, 0f), P(41, 14, "r2", 4, 700f, 0f),
        };
        var tree = TacticalCommandTreeProbe.BuildTree(probes, 0);
        var axis = TacticalFrontAxis.FromCentroids(0f, 0f, 0f, 2000f).WithOwnExtent(-700f, 700f, 5);
        var tops = new[]
        {
            new TacticalLeafBrigadeMap.TopAssignment(3, DirectChildRole.Main),
            new TacticalLeafBrigadeMap.TopAssignment(2, DirectChildRole.SupportMain),
            new TacticalLeafBrigadeMap.TopAssignment(4, DirectChildRole.Reserve),
        };
        var map = TacticalLeafBrigadeMap.BuildMap(tree, tops, 0.8f, CascadeEnvelopmentMode.DoubleWing, axis);
        TestHarness.AssertEqual(DirectChildRole.Main, map[20].CascadeChain[0], "left corps is a wing (Main)");
        TestHarness.AssertEqual(DirectChildRole.Main, map[40].CascadeChain[0], "right corps is a wing (Main)");
        TestHarness.AssertTrue(map[30].CascadeChain[0] != DirectChildRole.Main, "center corps pins/supports, not a wing");
        var echelon = TacticalLeafBrigadeMap.BuildMap(tree, tops, 0.4f, CascadeEnvelopmentMode.DoubleWingEchelon, axis);
        TestHarness.AssertEqual(DirectChildRole.Main, echelon[20].CascadeChain[0], "echelon: primary wing Main");
        TestHarness.AssertEqual(DirectChildRole.SupportMain, echelon[40].CascadeChain[0], "echelon: secondary wing follows as SupportMain");
    }

    private static void OrchestratorKeepsFrontAxis()
    {
        // The #61 patch calls UpdateLeafBrigadeMap(tree) without an axis; the
        // orchestrator must cascade with the axis the tick cycle observed.
        ArmyTickCycle.ResetForTest();
        var orch = OrchestratorWithPlan(BattlePhase.Probe, mainSector: 2);
        var axis = TacticalFrontAxis.FromCentroids(0f, 0f, 2000f, 0f).WithOwnExtent(-400f, 400f, 5);
        var enemy = new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0.35f, false, false, 0f, frontAxis: axis);
        ArmyTickCycle.MaybeReplan(orch, 1f, new ArmyEvidence(1f, TerrainKind.Open, 2), enemy,
            6000f, 1f, 0.35f, 0f, 60, 0f, 1f, 0f, 0f, battleDeltaSeconds: 1f);
        orch.RegisterDirectChildren(new[] { new DirectChildSnapshot("child-2", "army", 15, 0, "corps", true) });
        orch.ObserveDirectChildEvidenceWithZones(new[] { new DirectChildEvidence(3, 0, false, 2, 0, 0.9f) }, null, null);
        var probes = new List<TacticalCommandTreeProbe.ExtendedProbe>
        {
            P(1, 16, "army", 0, 0f, 0f),
            P(2, 15, "corps", 1, 0f, 0f),
            P(10, 14, "b-north", 2, 90f, 400f),
            P(11, 14, "b-nmid", 2, -60f, 200f),
            P(12, 14, "b-center", 2, 30f, 0f),
            P(13, 14, "b-smid", 2, -90f, -200f),
            P(14, 14, "b-south", 2, 60f, -400f),
        };
        orch.UpdateLeafBrigadeMap(TacticalCommandTreeProbe.BuildTree(probes, 0));
        var map = orch.CurrentLeafBrigadeMap;
        TestHarness.AssertEqual(DirectChildRole.Main, map[12].LeafRole, "center brigade anchors");
        TestHarness.AssertEqual(DirectChildRole.RefuseLeft, map[10].LeafRole, "facing east the north brigade is the left flank");
    }

    // ---- T6 / T11 enemy knowledge: range-limited, not fog-limited ----

    private static void ContactLedgerBeforeContact()
    {
        var ledger = new TacticalEnemyContactLedger();
        ledger.Observe(nowHours: 0.5f, visibleEnemyStrength: 0f, engagedEnemyStrength: 0f);
        TestHarness.AssertEqual(0f, ledger.EstimatedEnemyStrength, "nothing seen");
        TestHarness.AssertEqual(TacticalEnemyContactLedger.UnknownReserveCommitFraction, ledger.ReserveCommitFraction(0f), "unknown reserve sentinel");
        TestHarness.AssertTrue(ledger.Confidence(0.5f, ownStrength: 6000f) < 0.5f, "no contact -> below doctrine action gate");
        TestHarness.AssertFalse(ledger.ContactBroken(0.5f, 0f), "never had contact -> not broken");
    }

    private static void ContactLedgerTracksContact()
    {
        var ledger = new TacticalEnemyContactLedger();
        ledger.Observe(1f, 4000f, 1000f);
        TestHarness.AssertEqual(4000f, ledger.EstimatedEnemyStrength, "largest seen");
        TestHarness.AssertTrue(Near(ledger.ReserveCommitFraction(1000f), 0.25f), "1000 of 4000 engaged");
        TestHarness.AssertTrue(Near(ledger.Confidence(1f, 6000f), 1f), "large enemy in sight now -> full confidence");
        ledger.Observe(1.1f, 2500f, 2500f);
        TestHarness.AssertTrue(ledger.EstimatedEnemyStrength > 3900f, "largest seen persists (decays slowly)");
    }

    private static void ContactLedgerContactBroken()
    {
        var ledger = new TacticalEnemyContactLedger();
        ledger.Observe(1f, 4000f, 1000f);
        ledger.Observe(1.1f, 0f, 0f);
        TestHarness.AssertFalse(ledger.ContactBroken(1.1f, 0f), "6 minutes is not broken yet");
        ledger.Observe(1.5f, 0f, 0f);
        TestHarness.AssertTrue(ledger.ContactBroken(1.5f, 0f), "30 battle-minutes without contact -> broken");
        TestHarness.AssertTrue(ledger.EstimatedEnemyStrength < 4000f && ledger.EstimatedEnemyStrength > 3500f, "estimate decays");
        float conf = ledger.Confidence(1.5f, 6000f);
        TestHarness.AssertTrue(conf < 1f && conf > 0.5f, "stale but recent contact -> reduced confidence, got " + conf);
        ledger.Observe(4f, 0f, 0f);
        TestHarness.AssertFalse(ledger.ContactBroken(4f, 0f), "contact memory expired");
        TestHarness.AssertTrue(ledger.Confidence(4f, 6000f) < 0.5f, "old knowledge -> below action gate");
        TestHarness.AssertFalse(ledger.ContactBroken(1.5f, 300f), "something in sight now -> not broken");
    }

    private static void ContactLedgerPicketLowConfidence()
    {
        var ledger = new TacticalEnemyContactLedger();
        ledger.Observe(1f, 500f, 500f);
        TestHarness.AssertTrue(ledger.Confidence(1f, 10000f) < 0.5f, "a 500-man picket says little about a 10,000-man army's opponent");
    }

    private static void ContactLedgerResetsOnClockRewind()
    {
        var ledger = new TacticalEnemyContactLedger();
        ledger.Observe(5f, 4000f, 1000f);
        ledger.Observe(1f, 0f, 0f);
        TestHarness.AssertEqual(0f, ledger.EstimatedEnemyStrength, "reload / new battle clears knowledge");
    }

    private static void OwnAiBattleSkipsFeudInstance()
    {
        // BattleUnits.aibattle[] (85203-85212): [1] = player side, [0] = other side,
        // [2] = feud instance SetSide(0, isfeud) -> isplayeraiorfeud 2 (SetSide 3408).
        var sides = new[] { 1, 0, 0 };
        var kinds = new[] { 0, 1, 2 };
        TestHarness.AssertEqual(1, ArmyEvidenceBuilder.SelectAiBattleIndexForSide(sides, kinds, side: 0), "side 0 -> aibattle[1], not feud [2]");
        TestHarness.AssertEqual(0, ArmyEvidenceBuilder.SelectAiBattleIndexForSide(sides, kinds, side: 1), "side 1 -> aibattle[0]");
        TestHarness.AssertEqual(-1, ArmyEvidenceBuilder.SelectAiBattleIndexForSide(sides, kinds, side: 3), "unknown side");
        TestHarness.AssertEqual(-1, ArmyEvidenceBuilder.SelectAiBattleIndexForSide(new[] { 0 }, new[] { 2 }, side: 0), "feud-only -> none");
    }

    private static void IntentStrengthSignalRelative()
    {
        // Same 600-man enemy concentration: meaningful against a 1,000-man
        // detachment, noise against a 40,000-man army. Old code divided by a
        // fixed 5000.
        var small = new EnemyVisibleState(new[]
        {
            new EnemyVisibleSector(1, 500f, 600f, true),
            new EnemyVisibleSector(3, 500f, 0f, false),
        }, 0.5f, true, false, 0f);
        var big = new EnemyVisibleState(new[]
        {
            new EnemyVisibleSector(1, 20000f, 600f, true),
            new EnemyVisibleSector(3, 20000f, 0f, false),
        }, 0.5f, true, false, 0f);
        var ev = new ArmyEvidence(1f, TerrainKind.Open, 2);
        float cSmall = ArmyIntentInference.Build(ev, small).Confidence01;
        float cBig = ArmyIntentInference.Build(ev, big).Confidence01;
        TestHarness.AssertTrue(cSmall > cBig, "confidence scales with enemy relative to own force: " + cSmall + " vs " + cBig);
    }

    // ---- T7 / T17 triggers ----

    private static ReplanTriggerInput Trig(
        float morale = 1f, float moraleAtLast = 1f,
        float reserves = 0.5f, float reservesAtLast = 0f,
        float reinforcements = 0f, float ownStrength = 20000f, float reinforcementShareAtLast = 0f,
        float enemyShift = 0f, InferredIntent? intentNow = null, InferredIntent intentAtLast = InferredIntent.Unknown)
        => new ReplanTriggerInput(
            planAgeSeconds: 60f, currentPhase: BattlePhase.MainEffort,
            mainEffortOwnStrength: 5000f, mainEffortHistoryOwnStrength: 5000f,
            globalOddsCurrent: 1f, globalOddsHistory: 1f,
            armyMoraleCurrent: morale, armyMoraleFloor: 0.4f,
            reservesCommittedFraction: reserves, reinforcementsArrivingDelta: reinforcements,
            enemyMainEffortShiftConfidenceWeighted: enemyShift,
            armyMoraleAtLastReplan: moraleAtLast,
            reservesCommittedAtLastReplan: reservesAtLast,
            ownStrength: ownStrength,
            reinforcementShareAtLastReplan: reinforcementShareAtLast,
            currentEnemyIntent: intentNow,
            enemyIntentAtLastReplan: intentAtLast);

    private static void CasualtyTriggerIsEdge()
    {
        TestHarness.AssertEqual(ReplanTrigger.CasualtyThreshold, ArmyReplanTriggers.Evaluate(Trig(morale: 0.3f, moraleAtLast: 0.8f)), "crossing below the floor fires");
        TestHarness.AssertEqual(ReplanTrigger.None, ArmyReplanTriggers.Evaluate(Trig(morale: 0.3f, moraleAtLast: 0.35f)), "already below at last replan -> no refire");
    }

    private static void ReserveExhaustionIsEdge()
    {
        TestHarness.AssertEqual(ReplanTrigger.ReserveExhaustion, ArmyReplanTriggers.Evaluate(Trig(reserves: 0.9f, reservesAtLast: 0.5f)), "crossing 0.85 fires");
        TestHarness.AssertEqual(ReplanTrigger.None, ArmyReplanTriggers.Evaluate(Trig(reserves: 0.9f, reservesAtLast: 0.88f)), "already spent at last replan -> no refire");
    }

    private static void ReinforcementTriggerIsShareAndEdge()
    {
        TestHarness.AssertEqual(ReplanTrigger.None, ArmyReplanTriggers.Evaluate(Trig(reinforcements: 500f)), "500 men on a 20,000 army is not a new situation");
        TestHarness.AssertEqual(ReplanTrigger.ReinforcementArrival, ArmyReplanTriggers.Evaluate(Trig(reinforcements: 4000f)), "a 20% swing is");
        TestHarness.AssertEqual(ReplanTrigger.None, ArmyReplanTriggers.Evaluate(Trig(reinforcements: 4000f, reinforcementShareAtLast: 0.2f)), "same swing already planned for");
    }

    private static void EnemyIntentShiftNeedsChange()
    {
        TestHarness.AssertEqual(ReplanTrigger.EnemyIntentShift,
            ArmyReplanTriggers.Evaluate(Trig(enemyShift: 0.8f, intentNow: InferredIntent.Attack, intentAtLast: InferredIntent.Defend)), "intent changed -> fires");
        TestHarness.AssertEqual(ReplanTrigger.None,
            ArmyReplanTriggers.Evaluate(Trig(enemyShift: 0.8f, intentNow: InferredIntent.Attack, intentAtLast: InferredIntent.Attack)), "same intent as at last replan -> no refire");
    }

    private static readonly EnemyVisibleState QuietFront = new EnemyVisibleState(
        new[] { new EnemyVisibleSector(2, 6000f, 6000f, false) }, 0.5f, true, false, 0f);

    private static ReplanTrigger Tick(ArmyOrchestrator orch, float morale, EnemyVisibleState enemy, float battleSeconds = 600f, float odds = 1f)
        => ArmyTickCycle.MaybeReplan(orch, 1f, new ArmyEvidence(odds, TerrainKind.Open, 2), enemy,
            6000f, morale, 0.5f, 0f, 60, 0.2f, 0.9f, 0f, 0f, battleDeltaSeconds: battleSeconds);

    private static void ReplanPreservesPhaseNoThrash()
    {
        ArmyTickCycle.ResetForTest();
        var orch = OrchestratorWithPlan(BattlePhase.MainEffort, mainSector: 2, aggression: 0.5f);
        var first = Tick(orch, morale: 0.3f, QuietFront);
        TestHarness.AssertEqual(ReplanTrigger.CasualtyThreshold, first, "morale break replans once");
        TestHarness.AssertEqual(BattlePhase.Withdraw, orch.CurrentPlan.Phase, "replan keeps the Withdraw phase (no reset to Probe)");
        for (int i = 0; i < 5; i++)
        {
            var again = Tick(orch, morale: 0.3f, QuietFront, battleSeconds: 900f);
            TestHarness.AssertEqual(ReplanTrigger.None, again, "no replan loop while morale stays low (tick " + i + ")");
            TestHarness.AssertEqual(BattlePhase.Withdraw, orch.CurrentPlan.Phase, "Withdraw stays absorbing");
        }
    }

    private static void MainEffortSectorLossFromBandHistory()
    {
        ArmyTickCycle.ResetForTest();
        var orch = OrchestratorWithPlan(BattlePhase.MainEffort, mainSector: 2, aggression: 0.5f);
        Tick(orch, 1f, new EnemyVisibleState(new[] { new EnemyVisibleSector(2, 6000f, 5000f, false) }, 0.5f, true, false, 0f));
        var trigger = Tick(orch, 1f, new EnemyVisibleState(new[] { new EnemyVisibleSector(2, 2000f, 5000f, false) }, 0.5f, true, false, 0f));
        TestHarness.AssertEqual(ReplanTrigger.MainEffortSectorLoss, trigger, "main band lost two thirds of its strength");
    }

    private static void MoraleNormalizedToBattleStart()
    {
        // Regiment.battlestartmorale (110758) is set at first morale update to
        // Max(morale, abovemoraleslowimprovement) (128154-128156); -1 before.
        TestHarness.AssertTrue(Near(ArmyEvidenceBuilder.NormalizedMorale01(sumMorale: 1.5f, sumBattleStartMorale: 3f, fallback: 1f), 0.5f), "half the starting morale");
        TestHarness.AssertEqual(1f, ArmyEvidenceBuilder.NormalizedMorale01(3.3f, 3f, 1f), "above start clamps to 1");
        TestHarness.AssertEqual(1f, ArmyEvidenceBuilder.NormalizedMorale01(0.5f, 0f, 1f), "no start morale yet -> fallback");
    }

    // ---- T8 clocks ----

    private static void PhaseBudgetsAreBattleMinutes()
    {
        TestHarness.AssertEqual(20f * 60f, TacticalPhaseProgressionDoctrine.ProbeBudgetForAggression(1f), "aggressive probes 20 battle-minutes");
        TestHarness.AssertEqual(40f * 60f, TacticalPhaseProgressionDoctrine.ProbeBudgetForAggression(0.5f), "mid probes 40 battle-minutes");
        TestHarness.AssertEqual(60f * 60f, TacticalPhaseProgressionDoctrine.ProbeBudgetForAggression(0f), "cautious probes an hour");
        TestHarness.AssertEqual(60f * 60f, TacticalPhaseProgressionDoctrine.MainEffortBudgetForAggression(0f), "cautious main effort 1 h");
        TestHarness.AssertEqual(180f * 60f, TacticalPhaseProgressionDoctrine.MainEffortBudgetForAggression(1f), "aggressive main effort 3 h");
    }

    private static TacticalPhaseProgressionDoctrine.Input Phase(BattlePhase phase, float ageSeconds, float meNow, float meHistory, float global = 1.2f, float reserves = 0.3f)
        => new TacticalPhaseProgressionDoctrine.Input(phase, ageSeconds, global, 1f, meNow, meHistory, 1f, 0.4f, reserves, 0.5f);

    private static void MainEffortConsolidatesOnStallNotClock()
    {
        float pastBudget = TacticalPhaseProgressionDoctrine.MainEffortBudgetForAggression(0.5f) + 60f;
        var gaining = TacticalPhaseProgressionDoctrine.Decide(Phase(BattlePhase.MainEffort, pastBudget, meNow: 1.25f, meHistory: 1.1f));
        TestHarness.AssertEqual(BattlePhase.MainEffort, gaining.NextPhase, "still gaining past the budget keeps pushing; got " + gaining.Reason);
        var stalled = TacticalPhaseProgressionDoctrine.Decide(Phase(BattlePhase.MainEffort, pastBudget, meNow: 1.0f, meHistory: 1.1f));
        TestHarness.AssertEqual(BattlePhase.Consolidate, stalled.NextPhase, "stalled past the budget consolidates; got " + stalled.Reason);
        var early = TacticalPhaseProgressionDoctrine.Decide(Phase(BattlePhase.MainEffort, 600f, meNow: 1.0f, meHistory: 1.1f));
        TestHarness.AssertEqual(BattlePhase.MainEffort, early.NextPhase, "a stall inside the budget is not yet a verdict");
    }

    private static void PlanDeadlineRunsOnBattleTime()
    {
        ArmyTickCycle.ResetForTest();
        var orch = OrchestratorWithPlan(BattlePhase.Probe, mainSector: 2, aggression: 0.5f);
        var realOnly = ArmyTickCycle.MaybeReplan(orch, 5000f, new ArmyEvidence(1f, TerrainKind.Open, 2), QuietFront,
            6000f, 1f, 0.5f, 0f, 60, 0f, 1f, 0f, 0f, battleDeltaSeconds: 0f);
        TestHarness.AssertTrue(realOnly != ReplanTrigger.PhaseDeadline, "wall-clock time alone does not expire the plan");
        var battle = ArmyTickCycle.MaybeReplan(orch, 1f, new ArmyEvidence(1f, TerrainKind.Open, 2), QuietFront,
            6000f, 1f, 0.5f, 0f, 60, 0f, 1f, 0f, 0f, battleDeltaSeconds: ArmyReplanTriggers.PlanDeadlineBattleSeconds + 1f);
        TestHarness.AssertEqual(ReplanTrigger.PhaseDeadline, battle, "plan deadline after the battle-time budget");
    }

    private static TacticalForceBalanceEvidence Balance(float own, float enemy)
        => new TacticalForceBalanceEvidence(own, enemy, null, null, 0.5f, 0.5f, 1f);

    private static void OpportunityHysteresisOnBattleTime()
    {
        var orch = OrchestratorWithPlan(BattlePhase.Probe, mainSector: 2);
        orch.UpdateReinforcementOpportunity(Balance(8000f, 4000f), nowBattleSeconds: 100f);
        var first = orch.CurrentReinforcementOpportunity.Opportunity;
        orch.UpdateReinforcementOpportunity(Balance(5600f, 8000f), nowBattleSeconds: 100f + 300f);
        TestHarness.AssertEqual(first, orch.CurrentReinforcementOpportunity.Opportunity, "5 battle-minutes later the decision is held");
        orch.UpdateReinforcementOpportunity(Balance(5600f, 8000f), nowBattleSeconds: 100f + TacticalOpportunityHysteresis.DefaultMinHoldBattleSeconds + 1f);
        TestHarness.AssertTrue(first != orch.CurrentReinforcementOpportunity.Opportunity, "after the battle-time hold the new category is accepted");
    }

    private static void MainArrivalHoldIsBattleMinutes()
    {
        var far = new[] { new TacticalMainArrivalSynchronizer.SupportMainPosition(2000f) };
        TestHarness.AssertEqual(15f * 60f, TacticalMainArrivalSynchronizer.DefaultMaxHoldSeconds, "hold budget 15 battle-minutes");
        TestHarness.AssertTrue(TacticalMainArrivalSynchronizer.ShouldHoldMainForSupports(far, 0.5f, 600f, 500f, TacticalMainArrivalSynchronizer.DefaultMaxHoldSeconds), "10 battle-minutes in: still waiting");
        TestHarness.AssertFalse(TacticalMainArrivalSynchronizer.ShouldHoldMainForSupports(far, 0.5f, 1000f, 500f, TacticalMainArrivalSynchronizer.DefaultMaxHoldSeconds), "past 15 battle-minutes: release");
    }

    // ---- BR-H1 stalemate / T-comment ----

    private static void MacroMappingNoOpinionNoRetreat()
    {
        TestHarness.AssertFalse(TacticalVanillaMacroAiMapping.MapOpportunityToMacroAi(
            ReinforcementOpportunity.DefensiveHold, 1f, 0.5f).HasValue, "DefensiveHold -> vanilla decides");
        TestHarness.AssertEqual(TacticalVanillaMacroAiMapping.MacroAiDefend, TacticalVanillaMacroAiMapping.MapOpportunityToMacroAi(
            ReinforcementOpportunity.WithdrawalToFightLater, 0.3f, 0.5f).Value, "withdrawal holds the line; the army retreat stays vanilla's");
        foreach (ReinforcementOpportunity o in Enum.GetValues(typeof(ReinforcementOpportunity)))
        {
            var m = TacticalVanillaMacroAiMapping.MapOpportunityToMacroAi(o, 3f, 0.5f);
            TestHarness.AssertTrue(!m.HasValue || m.Value != 3, o + " must never map to macroai 3 (no startoffallback stamp, 6355/6362)");
        }
        TestHarness.AssertFalse(TacticalVanillaMacroAiMapping.IsLegalMacroAi(3), "3 is not a legal Whiskey write");
    }

    private static TacticalForceBalanceEvidence Stanced(float own, float enemy, BattleStance stance)
        => new TacticalForceBalanceEvidence(own, enemy, null, null, 0.5f, 0.5f, 1f, stance: stance);

    private static void DoctrineAttacksAtParityForMeetingAndAttacker()
    {
        // CheckGlobalAIStrategy 6400-6408: battleType 0 (meeting) -> attack
        // regardless of forcebalance; attack also at forcebalance >= 0.80 /
        // (1 + initiative * 0.7) (battleprefs), i.e. below parity.
        TestHarness.AssertEqual(ReinforcementOpportunity.AttackNow,
            TacticalReinforcementOpportunityDoctrine.Decide(Stanced(5000f, 5000f, BattleStance.Meeting)).Opportunity, "meeting at parity attacks");
        TestHarness.AssertEqual(ReinforcementOpportunity.AttackNow,
            TacticalReinforcementOpportunityDoctrine.Decide(Stanced(5000f, 5000f, BattleStance.Attacker)).Opportunity, "attacker at parity attacks");
        TestHarness.AssertTrue(TacticalReinforcementOpportunityDoctrine.Decide(Stanced(5000f, 5000f, BattleStance.Defender)).Opportunity
            != ReinforcementOpportunity.AttackNow, "defender at parity does not");
        TestHarness.AssertTrue(TacticalReinforcementOpportunityDoctrine.Decide(Stanced(4500f, 5000f, BattleStance.Meeting)).Opportunity
            != ReinforcementOpportunity.AttackNow, "meeting below parity does not");
    }

    private static void AttackThresholdPinned()
    {
        TestHarness.AssertTrue(Near(TacticalReinforcementOpportunityDoctrine.ComputeAttackThreshold(0f), 1.56f), "cautious 1.56");
        TestHarness.AssertTrue(Near(TacticalReinforcementOpportunityDoctrine.ComputeAttackThreshold(0.5f), 1.32f), "mid 1.32");
        TestHarness.AssertTrue(Near(TacticalReinforcementOpportunityDoctrine.ComputeAttackThreshold(1f), 1.08f), "aggressive 1.08");
        TestHarness.AssertTrue(Near(TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold(0.5f, BattleStance.Meeting), 1f), "meeting capped at parity");
        TestHarness.AssertTrue(Near(TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold(0.5f, BattleStance.Defender), 1.32f), "defender keeps the personality threshold");
        TestHarness.AssertEqual(28f, TacticalReinforcementOpportunityDoctrine.ComputeMaxWaitHours(0f), "cautious waits 28 h");
        TestHarness.AssertEqual(18f, TacticalReinforcementOpportunityDoctrine.ComputeMaxWaitHours(1f), "aggressive waits 18 h");
    }

    private static void ReconcilerKeepsAttackerOnDefensiveHold()
    {
        TestHarness.AssertEqual(BattleStance.Attacker, TacticalStanceReconciler.ResolveEffectiveStance(
            BattleStance.Attacker, ReinforcementOpportunity.DefensiveHold), "DefensiveHold is no opinion: keep Attacker");
        TestHarness.AssertEqual(BattleStance.Meeting, TacticalStanceReconciler.ResolveEffectiveStance(
            BattleStance.Meeting, ReinforcementOpportunity.DefensiveHold), "keep Meeting");
        TestHarness.AssertEqual(BattleStance.Defender, TacticalStanceReconciler.ResolveEffectiveStance(
            BattleStance.Attacker, ReinforcementOpportunity.DelayingDefense), "outnumbered DelayingDefense still demotes");
    }

    // ---- T12 inputs ----

    private sealed class FakeCommander
    {
        public string combinedname;
        public string firstname;
        public string name;
    }

    private static bool Historical(string combined, int alliance, string first = null, string last = null)
    {
        var c = new FakeCommander { combinedname = combined, firstname = first, name = last };
        return WhiskeyRealism.Strategic.HistoricalFigureRegistry.Resolve(c, alliance, last).isHistorical;
    }

    private static void RegistryMatchesFullNames()
    {
        // commanders.txt: CSA has 7 Lees, 4 Johnstons, 12 Jacksons, 14 Smiths,
        // 11 Andersons. Only the historical figure the vector describes matches.
        TestHarness.AssertTrue(Historical("Robert E. Lee", 1), "R.E. Lee matches");
        TestHarness.AssertFalse(Historical("Fitzhugh Lee", 1), "Fitzhugh Lee is not R.E. Lee");
        TestHarness.AssertFalse(Historical("Stephen D. Lee", 1), "S.D. Lee is not R.E. Lee");
        TestHarness.AssertTrue(Historical("Thomas J. Jackson", 1), "Stonewall matches");
        TestHarness.AssertFalse(Historical("John K. Jackson", 1), "John K. Jackson does not");
        TestHarness.AssertFalse(Historical("Joseph R. Davis", 1), "brigadier Joseph R. Davis is not the president");
        TestHarness.AssertTrue(Historical("P.G.T. Beauregard", 1), "initials normalized");
        TestHarness.AssertTrue(Historical("Ambrose P. Hill", 1), "A.P. Hill matches");
        TestHarness.AssertFalse(Historical("Daniel H. Hill", 1), "D.H. Hill does not get A.P. Hill's vector");
        TestHarness.AssertTrue(Historical("Ulysses S. Grant", 0), "Union Grant");
        TestHarness.AssertFalse(Historical("Ulysses S. Grant", 1), "alliance still checked");
    }

    private static void RegistryUsesFirstAndLastName()
    {
        TestHarness.AssertTrue(Historical(null, 1, first: "James", last: "Longstreet"), "combinedname unset: firstname + name");
        TestHarness.AssertFalse(Historical(null, 1, first: null, last: "Lee"), "a bare surname is ambiguous -> no match");
    }

    private static void TerrainClassifierReadsVanillaIds()
    {
        // battleprefs terrain specifics: 0 grass, 3 creek, 4 river, 5 river
        // crossing, 7 road, 8 fences, 14 forest, 15 swamp, 16 field.
        TestHarness.AssertEqual(TerrainKind.Open, ArmyEvidenceBuilder.ClassifyTerrain(new[] { 0, 0, 16, 7, 8, 0, 0, 0, 14, 0 }, 4f), "mostly fields");
        TestHarness.AssertEqual(TerrainKind.Wooded, ArmyEvidenceBuilder.ClassifyTerrain(new[] { 14, 14, 15, 0, 14, 0, 16, 14, 0, 0 }, 4f), "half forest/swamp");
        TestHarness.AssertEqual(TerrainKind.River, ArmyEvidenceBuilder.ClassifyTerrain(new[] { 0, 4, 0, 0, 0, 0, 0, 5, 0, 0 }, 4f), "a river through the field");
        TestHarness.AssertEqual(TerrainKind.Mountain, ArmyEvidenceBuilder.ClassifyTerrain(new[] { 0, 14, 0, 0 }, 80f), "high relief");
        TestHarness.AssertEqual(TerrainKind.Open, ArmyEvidenceBuilder.ClassifyTerrain(new int[0], 0f), "no samples -> open");
    }

    // ---- T13 / T15 / T16 roles ----

    private static DirectChildSnapshot Snap(string id) => new DirectChildSnapshot(id, "army", 15, 0, id, true);

    private static DirectChildEvidence Ev(int sector, int own, int enemy = 0, bool contact = false)
        => new DirectChildEvidence(own, enemy, contact, sector, 0, 0.9f);

    private static IReadOnlyList<DirectChildIntent> AllocateRoles(BattlePhase phase, float globalOdds, float commitOdds,
        DirectChildSnapshot[] snaps, DirectChildEvidence[] ev)
    {
        var plan = new TacticalBattlePlan(BattlePlanId.GenericMethodical, phase, 0, null, null, commitOdds, 0f, 7);
        return DirectChildAllocator.AllocateWithZones(plan, default(WhiskeyRealism.Strategic.PersonalityVector),
            snaps, ev, null, null, new ReserveReleaseContext(globalOdds));
    }

    private static DirectChildIntent Role(IReadOnlyList<DirectChildIntent> intents, string id)
    {
        for (int i = 0; i < intents.Count; i++) if (intents[i].ChildId == id) return intents[i];
        throw new Exception("missing " + id);
    }

    private static void ReserveHeldWithoutRelease()
    {
        var intents = AllocateRoles(BattlePhase.MainEffort, 1.0f, 1.4f,
            new[] { Snap("main"), Snap("reserve") }, new[] { Ev(0, 3), Ev(3, 3) });
        TestHarness.AssertEqual(DirectChildRole.Main, Role(intents, "main").Role, "main");
        TestHarness.AssertEqual(DirectChildRole.Reserve, Role(intents, "reserve").Role, "reserve held");
    }

    private static void ReserveReleasedOnExploit()
    {
        var intents = AllocateRoles(BattlePhase.Exploit, 1.0f, 1.4f,
            new[] { Snap("main"), Snap("reserve") }, new[] { Ev(0, 3), Ev(3, 3) });
        TestHarness.AssertEqual(DirectChildRole.SupportMain, Role(intents, "reserve").Role, "exploit commits the reserve behind the main effort");
    }

    private static void ReserveReleasedAtCommitOdds()
    {
        var intents = AllocateRoles(BattlePhase.MainEffort, 1.5f, 1.4f,
            new[] { Snap("main"), Snap("reserve") }, new[] { Ev(0, 3), Ev(3, 3) });
        TestHarness.AssertEqual(DirectChildRole.SupportMain, Role(intents, "reserve").Role, "odds reached ReserveCommitTriggerOdds");
    }

    private static void ReserveReinforcesCrisisSector()
    {
        var intents = AllocateRoles(BattlePhase.MainEffort, 1.0f, 1.4f,
            new[] { Snap("main"), Snap("pressed"), Snap("reserve") },
            new[] { Ev(0, 3), Ev(4, 1, enemy: 4, contact: true), Ev(3, 3) });
        var r = Role(intents, "reserve");
        TestHarness.AssertEqual(DirectChildRole.Fix, r.Role, "reserve moves up to hold the pressed sector");
        TestHarness.AssertEqual(4, r.AxisSector, "toward the crisis band");
    }

    private static DirectChildIntent DI(string id, DirectChildRole role, int sector)
        => new DirectChildIntent(id, 15, 15, id, sector, role, DirectChildAxis.SectorAxis, sector, 0.5f, 0.5f,
            new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>()));

    private static void MainRoleHysteresis()
    {
        var state = new MainRoleHysteresisState();
        var first = DirectChildAllocator.StabilizeMain(new[] { DI("a", DirectChildRole.Main, 2), DI("b", DirectChildRole.SupportMain, 3) }, state);
        TestHarness.AssertEqual(DirectChildRole.Main, Role(first, "a").Role, "first main accepted");
        for (int i = 1; i <= 2; i++)
        {
            var flip = DirectChildAllocator.StabilizeMain(new[] { DI("a", DirectChildRole.SupportMain, 2), DI("b", DirectChildRole.Main, 3) }, state);
            TestHarness.AssertEqual(DirectChildRole.Main, Role(flip, "a").Role, "incumbent keeps Main on challenge " + i);
            TestHarness.AssertEqual(DirectChildRole.SupportMain, Role(flip, "b").Role, "challenger takes the incumbent's role " + i);
            TestHarness.AssertEqual(2, Role(flip, "b").AxisSector, "support follows the incumbent's sector");
        }
        var third = DirectChildAllocator.StabilizeMain(new[] { DI("a", DirectChildRole.SupportMain, 2), DI("b", DirectChildRole.Main, 3) }, state);
        TestHarness.AssertEqual(DirectChildRole.Main, Role(third, "b").Role, "third consecutive win hands over Main");
        var fallback = DirectChildAllocator.StabilizeMain(new[] { DI("a", DirectChildRole.Main, 2), DI("b", DirectChildRole.Fallback, 3) }, state);
        TestHarness.AssertEqual(DirectChildRole.Main, Role(fallback, "a").Role, "incumbent falling back loses Main at once");
    }

    // ---- Review round 2 ----

    private static void ReplanCooldownIsConfiguredValue()
    {
        ArmyTickCycle.ResetForTest();
        var orch = OrchestratorWithPlan(BattlePhase.MainEffort, mainSector: 2, aggression: 0.5f);
        var ev = new ArmyEvidence(1f, TerrainKind.Open, 2);
        var first = ArmyTickCycle.MaybeReplan(orch, 1f, ev, QuietFront, 6000f, 0.3f, 0.5f, 0f, 60, 0.2f, 0.9f, 0f, 0f, battleDeltaSeconds: 10f);
        TestHarness.AssertEqual(ReplanTrigger.CasualtyThreshold, first, "first replan");
        var second = ArmyTickCycle.MaybeReplan(orch, 1f, ev, QuietFront, 6000f, 0.3f, 0.9f, 0f, 60, 0.2f, 0.9f, 0f, 0f, battleDeltaSeconds: 100f);
        TestHarness.AssertEqual(ReplanTrigger.ReserveExhaustion, second, "100 battle-s later a configured 60 s cooldown has passed");
    }

    private static ReplanTrigger DoctrineTick(ArmyOrchestrator orch, double doctrineNow)
        => ArmyTickCycle.MaybeReplan(orch, 1f, new ArmyEvidence(1f, TerrainKind.Open, 2), QuietFront,
            6000f, 1f, 0.5f, 0f, 600, 0.2f, 0.9f, 0f, 0f, battleDeltaSeconds: 0f, doctrineNowSeconds: doctrineNow);

    // Doctrine time does not advance while the coordinator holds the night
    // freeze (eodcycle != 0); these tests feed doctrine time directly: the
    // raw clock's ~9 h overnight jump never reaches the doctrine timers.
    private static void PausedNightLeavesPhaseBudgets()
    {
        ArmyTickCycle.ResetForTest();
        var orch = OrchestratorWithPlan(BattlePhase.Probe, mainSector: 2, aggression: 0.5f);
        double evening = 18 * 3600.0;
        DoctrineTick(orch, evening);
        DoctrineTick(orch, evening + 600.0);
        float ageBefore = orch.PhaseAgeSeconds;
        TestHarness.AssertTrue(Near(ageBefore, 600f), "10 battle-minutes of probing, got " + ageBefore);
        // Night: the doctrine clock is frozen; ticks keep arriving.
        DoctrineTick(orch, evening + 600.0);
        DoctrineTick(orch, evening + 600.0);
        TestHarness.AssertTrue(Near(orch.PhaseAgeSeconds, ageBefore), "night does not burn the probe budget");
        TestHarness.AssertTrue(Near(orch.PlanAgeSeconds, ageBefore), "night does not age the plan");
        // Morning: doctrine time resumes from where it froze.
        DoctrineTick(orch, evening + 1200.0);
        TestHarness.AssertTrue(Near(orch.PhaseAgeSeconds, 1200f), "budget resumes after the night");
    }

    private static void PausedNightLeavesLedgerDecay()
    {
        var ledger = new TacticalEnemyContactLedger();
        float evening = 18f;
        ledger.Observe(evening, 4000f, 1000f);
        ledger.Observe(evening, 0f, 0f); // frozen doctrine hours across the night
        TestHarness.AssertEqual(4000f, ledger.EstimatedEnemyStrength, "no decay across the frozen night");
    }

    private static TacticalEnemyContactLedger Seen(float hours, float visible, float engaged)
    {
        var l = new TacticalEnemyContactLedger();
        l.Observe(hours, visible, engaged);
        return l;
    }

    private static void ArmyOddsUnknownUnderLowConfidence()
    {
        TestHarness.AssertEqual(1f, ArmyEvidenceBuilder.KnownEnemyOdds(6000f, Seen(1f, 40f, 40f), 1f), "40-man vedette: odds unknown");
        TestHarness.AssertEqual(1f, ArmyEvidenceBuilder.KnownEnemyOdds(6000f, new TacticalEnemyContactLedger(), 1f), "no contact: unknown");
        TestHarness.AssertTrue(Near(ArmyEvidenceBuilder.KnownEnemyOdds(6000f, Seen(1f, 4000f, 1000f), 1f), 1.5f), "real contact: own/seen");
    }

    private static void PicketOnlyContactKeepsProbe()
    {
        ArmyTickCycle.ResetForTest();
        var orch = OrchestratorWithPlan(BattlePhase.Probe, mainSector: 2, aggression: 0.5f);
        var ledger = Seen(1f, 40f, 40f);
        float odds = ArmyEvidenceBuilder.KnownEnemyOdds(6000f, ledger, 1f);
        var picketFront = new EnemyVisibleState(new[] { new EnemyVisibleSector(2, 6000f, 40f, true) },
            ledger.ReserveCommitFraction(40f), true, false, 0f);
        var trigger = ArmyTickCycle.MaybeReplan(orch, 1f, new ArmyEvidence(odds, TerrainKind.Open, 2), picketFront,
            6000f, 1f, 0.35f, 0f, 600, 0f, 1f, 0f, 0f, battleDeltaSeconds: 60f);
        TestHarness.AssertEqual(BattlePhase.Probe, orch.CurrentPlan.Phase, "a picket is not a reason to commit the main effort");
        TestHarness.AssertTrue(trigger != ReplanTrigger.ForceImbalanceShift, "no 150:1 force-imbalance replan");
    }

    private static void MorningOddsNotInflated()
    {
        var ledger = new TacticalEnemyContactLedger();
        float evening = 18f, morning = evening + 1f / 60f; // one doctrine minute later
        ledger.Observe(evening, 4000f, 1000f);
        ledger.Observe(morning, 0f, 0f);
        float odds = ArmyEvidenceBuilder.KnownEnemyOdds(6000f, ledger, morning);
        TestHarness.AssertTrue(odds > 1.45f && odds < 1.55f, "morning odds stay ~1.5, got " + odds);
    }

    // Copied from GTCW Config/commanders.txt (build 14703111): the combined
    // name (firstname + " " + name, decompile 59081) of every alliance-0
    // (Union) and alliance-1 (CSA) commander whose surname the historical
    // registry uses. A registry entry with a new surname must add that
    // surname's vanilla names here, spelled exactly as the game spells them.
    private static readonly string[] VanillaUnionNamesForRegistrySurnames =
    {
            "Abner Doubleday", "Albert F. Thomas", "Albert L. Lee", "Alger M. Wheeler", "Ambrose E. Burnside",
            "Andrew A. Humphreys", "Andrew J. Smith", "Andrew Porter", "Benjamin F. Butler",
            "Benjamin F. Davis", "Benjamin F. Smith", "Burr Porter", "Charles F. Smith", "Charles H. Davis",
            "Charles H. Morgan", "Charles H. Smith", "Charles Stewart", "Conrad F. Jackson",
            "Darius N. Couch", "David B. Birney", "David D. Porter", "David Hunter", "David Miles",
            "David Stuart", "Dixon S. Miles", "Don Carlos Buell", "Edmund J. Davis", "Edward O.C. Ord",
            "Edward S. Bragg", "Ezra Taylor", "Fitz J. Porter", "Francis C. Barlow", "Francis T. Sherman",
            "Franz Sigel", "George A. Custer", "George B. McClellan", "George G. Meade", "George H. Thomas",
            "George P. Buell", "George Stoneman Jr.", "George Sykes", "George W. Morgan",
            "George W. Schofield", "George W. Scott", "George W. Taylor", "Gilbert H. Reynolds",
            "Giles A. Smith", "Gouverneur K. Warren", "Henry G. Thomas", "Henry W. Halleck",
            "Henry W. Slocum", "James B. McPherson", "James D. Morgan", "James E. Smith", "James H. Wilson",
            "James S. Jackson", "Jefferson C. Davis", "John A. Logan", "John A. Reynolds", "John Buford",
            "John C. Fremont", "John E. Smith", "John F. Reynolds", "John G. Reynolds", "John M. Schofield",
            "John Newton", "John Pope", "John S. Slocum", "John Sedgwick", "Joseph Hooker",
            "Joseph J. Reynolds", "Joseph L. Kirby Smith", "Joseph Smith", "Judson Kilpatrick", "Lew Wallace",
            "Lewis A. Grant", "Melancton Smith", "Morgan L. Smith", "Morton C. Hunter", "Napoleon B. Buford",
            "Nathaniel J. Jackson", "Nathaniel P. Banks", "Nelson A. Miles", "Nelson Taylor",
            "Oliver O. Howard", "Orland Smith", "Philip H. Sheridan", "Robert Anderson", "Robert B. Hampton",
            "Robert F. Taylor", "Robert K. Scott", "Samuel M. Logan", "Samuel P. Lee", "Samuel W. Price",
            "Sylvester G. Hill", "Thomas Kilby Smith", "Thomas W. Sherman", "Ulysses Doubleday",
            "Ulysses S. Grant", "William A. Jackson", "William Birney", "William D. Porter",
            "William F. Smith", "William H. Reynolds", "William H.L. Wallace", "William Reynolds",
            "William Rosecrans", "William S. Smith", "William T. Sherman", "William W.H. Davis",
            "William Wheeler", "William Wilson", "Winfield S. Hancock", "Winfield Scott",
    };

    private static readonly string[] VanillaCsaNamesForRegistrySurnames =
    {
            "Abraham Buford", "Albert S. Johnston", "Alexander P. Stewart", "Alexander W. Reynolds",
            "Alfred E. Jackson", "Ambrose P. Hill", "Baxter Smith", "Benjamin G. Humphreys", "Benjamin Huger",
            "Braxton Bragg", "Caraway Smith", "Charles D. Anderson", "Claudius C. Wilson", "Daniel H. Hill",
            "Daniel H. Reynolds", "Earl Van Dorn", "Edmund Kirby Smith", "Edward L. Thomas",
            "Edward T.H. Warren", "Edwin J. Anderson", "Edwin M. Anderson", "Fitzhugh Lee", "Frank Huger",
            "French Forrest", "G. W. Scott", "George B. Anderson", "George D. Johnston", "George E. Pickett",
            "George H. Smith", "George T. Anderson", "George W. Anderson Jr.", "Gustavus W. Smith",
            "Henry A. Wise", "Henry R. Jackson", "J.E.B. Stuart", "James A. Smith", "James Jackson",
            "James Longstreet", "James T. Wheeler", "James W. Jackson", "Jeffrey E. Forrest", "John B. Hood",
            "John B. Magruder", "John C. Pemberton", "John D. Taylor", "John F. Smith", "John H. Morgan",
            "John K. Jackson", "John L. Logan", "John S. Scott", "John T. Morgan", "Joseph E. Johnston",
            "Joseph R. Anderson", "Joseph R. Davis", "Joseph W. Anderson", "Joseph Wheeler", "Jubal A. Early",
            "Leon Smith", "Leonidas Polk", "Lucius E. Polk", "Martin L. Smith", "Matthew C. Butler",
            "Melanchton Smith", "Nathan B. Forrest", "P.G.T. Beauregard", "Patrick R. Cleburne",
            "Patton Anderson", "Preston Smith", "Richard H. Anderson", "Richard S. Ewell", "Richard Taylor",
            "Robert D. Johnston", "Robert E. Lee", "Robert H. Anderson", "Samuel R. Anderson",
            "Simon B. Buckner Sr.", "Stephen D. Lee", "Sterling Price", "Sydney S. Lee", "T. Sturgis Davis",
            "Thomas B. Huger", "Thomas B. Smith", "Thomas H. Taylor", "Thomas J. Jackson", "Thomas M. Scott",
            "Thomas Smith", "Wade Hampton", "William A. Morgan", "William B. Butler", "William G.M. Davis",
            "William H. Jackson", "William H. Stephens", "William H.F. Lee", "William J. Hardee",
            "William L. Jackson", "William Mahone", "William R. Miles", "William Smith", "William W. Hunter",
            "William W. Loring",
    };

    private static void RegistryNamesExistInVanilla()
    {
        var union = new System.Collections.Generic.HashSet<string>();
        var csa = new System.Collections.Generic.HashSet<string>();
        foreach (var n in VanillaUnionNamesForRegistrySurnames) union.Add(WhiskeyRealism.Strategic.HistoricalFigureRegistry.NormalizeFullNameForTest(n));
        foreach (var n in VanillaCsaNamesForRegistrySurnames) csa.Add(WhiskeyRealism.Strategic.HistoricalFigureRegistry.NormalizeFullNameForTest(n));
        var missing = new System.Collections.Generic.List<string>();
        foreach (var e in WhiskeyRealism.Strategic.HistoricalFigureRegistry.EntriesForTest)
        {
            var roster = e.AllianceTag == "CSA" ? csa : union;
            foreach (var n in e.FullNames)
                if (!roster.Contains(WhiskeyRealism.Strategic.HistoricalFigureRegistry.NormalizeFullNameForTest(n)))
                    missing.Add(e.AllianceTag + ":" + n);
        }
        TestHarness.AssertTrue(missing.Count == 0, "not in vanilla roster: " + string.Join(", ", missing));
    }

    private static void OrchestratorObservePathAppliesReleaseAndHysteresis()
    {
        var orch = new ArmyOrchestrator(allianceId: 0, BuiltInPlaybooks.SeedCatalog(), default(WhiskeyRealism.Strategic.PersonalityVector));
        orch.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.GenericMethodical, BattlePhase.MainEffort, 0, null, null, 1.4f, 0f, 7));
        orch.RegisterDirectChildren(new[] { Snap("a"), Snap("b"), Snap("reserve") });

        orch.ObserveGlobalOdds(1.0f);
        orch.ObserveDirectChildEvidenceWithZones(new[] { Ev(0, 3), Ev(1, 1), Ev(3, 3) }, null, null);
        TestHarness.AssertEqual(DirectChildRole.Main, orch.GetDirectChildRole("a"), "a takes Main");
        TestHarness.AssertEqual(DirectChildRole.Reserve, orch.GetDirectChildRole("reserve"), "reserve held below commit odds");

        // One tick where b looks better does not hand over Main.
        orch.ObserveDirectChildEvidenceWithZones(new[] { Ev(0, 1), Ev(1, 3), Ev(3, 3) }, null, null);
        TestHarness.AssertEqual(DirectChildRole.Main, orch.GetDirectChildRole("a"), "incumbent keeps Main on a one-tick challenge");

        orch.ObserveGlobalOdds(1.5f);
        orch.ObserveDirectChildEvidenceWithZones(new[] { Ev(0, 3), Ev(1, 1), Ev(3, 3) }, null, null);
        TestHarness.AssertEqual(DirectChildRole.SupportMain, orch.GetDirectChildRole("reserve"), "reserve released at the plan's commit odds");
    }

    private static void EvidenceMemoSharesWithinFrame()
    {
        var memo = new ArmyEvidenceBuilder.PerSideFrameMemo<string>();
        var battleA = new object();
        var battleB = new object();
        TestHarness.AssertFalse(memo.TryGet(battleA, 0, 10, out _), "empty memo misses");
        memo.Store(battleA, 0, 10, "a0@10");
        memo.Store(battleA, 1, 10, "a1@10");
        TestHarness.AssertTrue(memo.TryGet(battleA, 0, 10, out var hit) && hit == "a0@10", "same side, same frame: shared");
        TestHarness.AssertTrue(memo.TryGet(battleA, 1, 10, out var hit1) && hit1 == "a1@10", "sides are separate");
        TestHarness.AssertFalse(memo.TryGet(battleA, 0, 11, out _), "next frame rebuilds");
        TestHarness.AssertFalse(memo.TryGet(battleB, 1, 10, out _), "another battle never reuses");
        memo.Store(battleA, 2, 10, "europe");
        TestHarness.AssertFalse(memo.TryGet(battleA, 2, 10, out _), "only alliances 0/1 are cached");
        TestHarness.AssertFalse(memo.TryGet(battleA, 0, -1, out _), "unknown frame never hits");
        memo.Reset();
        TestHarness.AssertFalse(memo.TryGet(battleA, 1, 10, out _), "reset clears");
    }
}
