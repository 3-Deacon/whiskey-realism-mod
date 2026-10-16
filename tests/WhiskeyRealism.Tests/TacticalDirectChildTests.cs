using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WhiskeyRealism;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Util;

using static TestFixtures;
using static TestHarness;

internal static class TacticalDirectChildTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("allocator with zones overrides evidence", AllocatorWithZonesOverridesEvidence),
            ("allocator without zones uses existing evidence path", AllocatorWithoutZonesUsesExistingEvidencePath),
            ("attached division is not directly controllable", AttachedDivisionIsNotDirectlyControllable),
            ("division refuses enemy army without support", DivisionRefusesEnemyArmyWithoutSupport),
            ("direct child intent sanitizes nonfinite floats", DirectChildIntentSanitizesNonfiniteFloats),
            ("direct child intent clamps support priority", DirectChildIntentClampsSupportPriority),
            ("direct child evidence buckets are non negative", DirectChildEvidenceBucketsAreNonNegative),
            ("direct child evidence equals same buckets", DirectChildEvidenceEqualsSameBuckets),
            ("direct child snapshot stores raw and effective unittyp", DirectChildSnapshotStoresRawAndEffectiveUnittyp),
            ("direct child allocator assigns main on main effort sector with strength", DirectChildAllocatorAssignsMainOnMainEffortSectorWithStrength),
            ("direct child allocator rejects locally outnumbered main for cautious commander", DirectChildAllocatorRejectsLocallyOutnumberedMainForCautiousCommander),
            ("direct child allocator accepts locally outnumbered main for aggressive commander", DirectChildAllocatorAcceptsLocallyOutnumberedMainForAggressiveCommander),
            ("direct child allocator parity brigade gets main under cautious commander", DirectChildAllocatorLocallyEvenBrigadeStillGetsMainUnderCautiousCommander),
            ("direct child allocator weak distant brigade becomes reserve not unknown", DirectChildAllocatorWeakDistantBrigadeBecomesReserveNotUnknown),
            ("direct child allocator assigns support main to adjacent strong child", DirectChildAllocatorAssignsSupportMainToAdjacentStrongChild),
            ("direct child allocator assigns fix on fixing sector with contact", DirectChildAllocatorAssignsFixOnFixingSectorWithContact),
            ("direct child allocator falls back before fixing under severe overmatch", DirectChildAllocatorFallbackBeatsFixUnderSevereOvermatch),
            ("direct child allocator falls back before main under severe overmatch", DirectChildAllocatorFallbackBeatsMainUnderSevereOvermatch),
            ("direct child allocator outnumbered but not in contact stays in line", DirectChildAllocatorOutnumberedButNotInContactStaysInLine),
            ("direct child allocator outnumbered with static enemy holds line", DirectChildAllocatorOutnumberedAndInContactWithStaticEnemyHolds),
            ("direct child allocator assigns reserve to uncommitted strong child", DirectChildAllocatorAssignsReserveToUncommittedStrongChild),
            ("direct child allocator assigns fallback on adverse odds and attack", DirectChildAllocatorAssignsFallbackOnAdverseOddsAndAttack),
            ("direct child allocator allocates refuse to flank with exposure", DirectChildAllocatorAllocatesRefuseToFlankWithExposure),
            ("direct child allocator deterministic on registration order tie", DirectChildAllocatorDeterministicOnRegistrationOrderTie),
            ("direct child allocator falls back to strongest main when plan sector is empty", DirectChildAllocatorFallsBackToStrongestMainWhenPlanSectorEmpty),
            ("direct child allocator keeps aggressive probe moving when planned sector is empty", DirectChildAllocatorKeepsAggressiveProbeMovingWhenPlannedSectorEmpty),
            ("direct child allocator reserve when no plan main effort match", DirectChildAllocatorReserveWhenNoPlanMainEffortMatch),
            ("direct child allocator assigns screen on screening sector with low strengths", DirectChildAllocatorAssignsScreenOnScreeningSectorWithLowStrengths),
            ("direct child allocator handles mismatched per child intent length", DirectChildAllocatorHandlesMismatchedPerChildIntentLength),
            ("direct child discovery probe handles empty unitsused", DirectChildDiscoveryProbeHandlesEmptyUnitsused),
            ("direct child discovery probe filters below effective command min", DirectChildDiscoveryProbeFiltersBelowEffectiveCommandMin),
            ("direct child discovery probe selects highest unittyp as army root", DirectChildDiscoveryProbeSelectsHighestUnittypAsArmyRoot),
            ("direct child discovery probe handles negative command hierarchy shift", DirectChildDiscoveryProbeHandlesNegativeCommandHierarchyShift),
            ("direct child discovery probe synthesizes when zero direct children", DirectChildDiscoveryProbeSynthesizesWhenZeroDirectChildren),
            ("direct child discovery probe iterates each army root for multi army side", DirectChildDiscoveryProbeIteratesEachArmyRootForMultiArmySide),
            ("direct child evidence builder buckets strength using 0.5 ratio", DirectChildEvidenceBuilderBucketsStrengthUsing05Ratio),
            ("direct child evidence builder propagates contact flag", DirectChildEvidenceBuilderPropagatesContactFlag),
            ("direct child evidence builder zero own when sector missing", DirectChildEvidenceBuilderZeroOwnWhenSectorMissing),
            ("direct child gate disabled allows all", DirectChildGateDisabledAllowsAll),
            ("direct child gate player side allows all", DirectChildGatePlayerSideAllowsAll),
            ("direct child gate unknown role allows", DirectChildGateUnknownRoleAllows),
            ("direct child gate reserve denies", DirectChildGateReserveDenies),
            ("direct child gate main allows on axis denies off axis", DirectChildGateMainAllowsOnAxisDeniesOffAxis),
            ("direct child gate fix allows short denies wide", DirectChildGateFixAllowsShortDeniesWide),
            ("direct child gate screen allows in sector denies out of sector", DirectChildGateScreenAllowsInSectorDeniesOutOfSector),
            ("direct child gate fallback allows away from enemy denies toward enemy", DirectChildGateFallbackAllowsAwayDeniesToward),
            ("direct child gate refuse left allows in sector denies out", DirectChildGateRefuseLeftAllowsInSectorDeniesOut),
            ("direct child gate negative target sector coerces to primary", DirectChildGateNegativeTargetSectorCoercesToPrimary),
            ("parse instance id child positive", ParseInstanceIdChildPositive),
            ("parse instance id child negative", ParseInstanceIdChildNegative),
            ("parse instance id synth army positive", ParseInstanceIdSynthArmyPositive),
            ("parse instance id synth army negative", ParseInstanceIdSynthArmyNegative),
        });
    }

    private static void AttachedDivisionIsNotDirectlyControllable()
    {
        var snap = new FormationSnapshot
        {
            UnitKey = "attached:1",
            ParentUnitKey = "corps:1",
            UnitType = 14,
            IsTopUnit = false
        };

        AssertEqual(FormationLevel.Division, snap.Level);
        AssertEqual(true, snap.IsAttachedDivision);
        AssertEqual(false, snap.CanReceiveDirectDirective);
    }

    private static void DivisionRefusesEnemyArmyWithoutSupport()
    {
        var snap = Snapshot("division", 1, 14, 4500f, 50000f, FormationLevel.Army, FrontPosture.Hold);
        var ledger = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Amateur1861, null);
        var assignment = ledger.GetAssignment("division");

        AssertEqual(FormationDirective.Screen, assignment.Directive);
        AssertEqual(false, assignment.OffensiveAllowed);
    }

    private static void DirectChildIntentSanitizesNonfiniteFloats()
    {
        var intent = new DirectChildIntent(
            childId: "c1",
            rawUnitTyp: 15,
            effectiveCommandLevel: 16,
            displayName: "1st Corps",
            primarySector: 2,
            role: DirectChildRole.Main,
            axis: DirectChildAxis.SectorAxis,
            axisSector: 2,
            supportPriority01: float.NaN,
            enemyIntent: new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>()));
        AssertEqual(0f, intent.SupportPriority01, "NaN sanitized to 0");
    }

    private static void DirectChildIntentClampsSupportPriority()
    {
        var intent = new DirectChildIntent(
            "c1", 15, 16, "1st", 0, DirectChildRole.SupportMain, DirectChildAxis.SectorAxis, 0,
            supportPriority01: 1.5f,
            enemyIntent: new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>()));
        AssertEqual(1f, intent.SupportPriority01);
    }

    private static void DirectChildEvidenceBucketsAreNonNegative()
    {
        var ev = new DirectChildEvidence(
            ownStrengthBucket: -3,
            enemyStrengthBucket: -1,
            contactFlag: false,
            primarySector: 0,
            flankExposureBucket: -2,
            confidence01: float.NaN);
        AssertEqual(0, ev.OwnStrengthBucket);
        AssertEqual(0, ev.EnemyStrengthBucket);
        AssertEqual(0, ev.FlankExposureBucket);
        AssertEqual(0f, ev.Confidence01);
    }

    private static void DirectChildEvidenceEqualsSameBuckets()
    {
        var a = new DirectChildEvidence(2, 1, true, 3, 1, 0.7f);
        var b = new DirectChildEvidence(2, 1, true, 3, 1, 0.7f);
        AssertTrue(a.SignatureEquals(b), "signature equals when buckets+flag+sector match");
        var c = new DirectChildEvidence(2, 1, false, 3, 1, 0.7f); // contact flag flipped
        AssertTrue(!a.SignatureEquals(c), "signature differs when contact flag changes");
    }

    private static void DirectChildSnapshotStoresRawAndEffectiveUnittyp()
    {
        var snap = new DirectChildSnapshot(
            childId: "child-99",
            parentArmyId: "army-1",
            rawUnitTyp: 15,
            commandHierarchyShift: -1,
            displayName: "Jackson's Corps",
            active: true);
        AssertEqual(15, snap.RawUnitTyp);
        AssertEqual(16, snap.EffectiveCommandLevel); // 15 - (-1) = 16 = unshifted Army
        AssertEqual("child-99", snap.ChildId);
        AssertEqual("army-1", snap.ParentArmyId);
        AssertTrue(snap.Active, "active flag preserved");
    }

    private static void DirectChildAllocatorAssignsMainOnMainEffortSectorWithStrength()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            mainEffortSector: 2, fixingSectors: new[] { 0 }, screeningSectors: new[] { 4 },
            reserveCommitTriggerOdds: 1.2f, ageSeconds: 0f, jitterSeed: 0);
        var snapshots = new[]
        {
            new DirectChildSnapshot("c0", "a", 15, 0, "First Corps", true),
            new DirectChildSnapshot("c1", "a", 15, 0, "Second Corps", true),
            new DirectChildSnapshot("c2", "a", 15, 0, "Third Corps", true),
        };
        var evidence = new[]
        {
            new DirectChildEvidence(1, 1, false, 0, 0, 0.5f),
            new DirectChildEvidence(3, 1, true,  2, 0, 0.7f),
            new DirectChildEvidence(1, 1, false, 4, 0, 0.5f),
        };
        var personality = new PersonalityVector(0.2f, 0.0f, 0.0f, 0.0f, 0f);
        var intents = DirectChildAllocator.AllocateWithZones(plan, personality, snapshots, evidence, null, null);
        AssertEqual(3, intents.Count);
        AssertEqual(DirectChildRole.Main, intents[1].Role, "main on sector 2");
        AssertEqual(2, intents[1].PrimarySector);
        AssertEqual(DirectChildAxis.SectorAxis, intents[1].Axis);
        AssertEqual(2, intents[1].AxisSector);
    }

    private static void DirectChildAllocatorRejectsLocallyOutnumberedMainForCautiousCommander()
    {
        // Cautious commander (aggression -0.6 → 0.20 normalized) with the
        // planned MainEffortSector brigade locally outnumbered by 1 bucket.
        // New local-odds gate must reject this brigade for Main.
        // Brigades in OTHER sectors with parity should pick up Main via
        // PickFallbackMainEffort (the safe-fallback tier already enforces
        // local-odds parity).
        var plan = new TacticalBattlePlan(
            BattlePlanId.PreparedDefense, BattlePhase.MainEffort,
            mainEffortSector: 2, fixingSectors: new int[0], screeningSectors: new int[0],
            reserveCommitTriggerOdds: 1.6f, ageSeconds: 0f, jitterSeed: 0);
        var snapshots = new[]
        {
            new DirectChildSnapshot("c0", "a", 15, 0, "First", true),
            new DirectChildSnapshot("c1", "a", 15, 0, "Second", true),
            new DirectChildSnapshot("c2", "a", 15, 0, "Third", true),
        };
        // c1 (sector 2 = planned main effort) is outnumbered own=1, enemy=2.
        // c0 (sector 1, adjacent) is strong own=3, enemy=1 — should win Main.
        var evidence = new[]
        {
            new DirectChildEvidence(3, 1, true, 1, 0, 0.8f),
            new DirectChildEvidence(1, 2, true, 2, 0, 0.7f),
            new DirectChildEvidence(2, 2, false, 3, 0, 0.5f),
        };
        var personality = new PersonalityVector(-0.6f, 0.0f, 0.0f, 0.0f, 0f);
        var intents = DirectChildAllocator.AllocateWithZones(plan, personality, snapshots, evidence, null, null);
        AssertTrue(intents[1].Role != DirectChildRole.Main,
            "locally outnumbered brigade in MainEffortSector must NOT be Main under cautious commander; got " + intents[1].Role);
        // Some other sector should have picked up Main via fallback.
        bool anyMain = false;
        for (int i = 0; i < intents.Count; i++)
            if (intents[i].Role == DirectChildRole.Main) { anyMain = true; break; }
        AssertTrue(anyMain, "PickFallbackMainEffort should have found a parity sector for Main");
    }

    private static void DirectChildAllocatorAcceptsLocallyOutnumberedMainForAggressiveCommander()
    {
        // Aggressive commander (aggression 0.6 → 0.80 normalized) bypasses
        // the local-odds gate. The planned MainEffortSector brigade gets Main
        // even when locally outnumbered by 1 bucket — Hood's frontal-assault /
        // Jackson's Chancellorsville pattern.
        var plan = new TacticalBattlePlan(
            BattlePlanId.FrontalShockAssault, BattlePhase.MainEffort,
            mainEffortSector: 2, fixingSectors: new int[0], screeningSectors: new int[0],
            reserveCommitTriggerOdds: 1.0f, ageSeconds: 0f, jitterSeed: 0);
        var snapshots = new[]
        {
            new DirectChildSnapshot("c0", "a", 15, 0, "First", true),
            new DirectChildSnapshot("c1", "a", 15, 0, "Second", true),
            new DirectChildSnapshot("c2", "a", 15, 0, "Third", true),
        };
        var evidence = new[]
        {
            new DirectChildEvidence(1, 1, false, 1, 0, 0.5f),
            new DirectChildEvidence(1, 2, true, 2, 0, 0.7f),  // outnumbered but planned main
            new DirectChildEvidence(1, 1, false, 3, 0, 0.5f),
        };
        var personality = new PersonalityVector(0.6f, 0.0f, 0.0f, 0.0f, 0f);
        var intents = DirectChildAllocator.AllocateWithZones(plan, personality, snapshots, evidence, null, null);
        AssertEqual(DirectChildRole.Main, intents[1].Role,
            "aggressive commander bypasses local-odds gate and assigns Main to planned brigade");
    }

    private static void DirectChildAllocatorLocallyEvenBrigadeStillGetsMainUnderCautiousCommander()
    {
        // Regression guard: when local odds are EVEN (own=enemy), the cautious
        // commander gate must still allow Main assignment. The gate only blocks
        // ENEMY > OWN. Equal buckets are acceptable for Main.
        var plan = new TacticalBattlePlan(
            BattlePlanId.PreparedDefense, BattlePhase.MainEffort,
            mainEffortSector: 2, fixingSectors: new int[0], screeningSectors: new int[0],
            reserveCommitTriggerOdds: 1.6f, ageSeconds: 0f, jitterSeed: 0);
        var snapshots = new[]
        {
            new DirectChildSnapshot("c0", "a", 15, 0, "First", true),
        };
        var evidence = new[]
        {
            new DirectChildEvidence(2, 2, true, 2, 0, 0.7f),
        };
        var personality = new PersonalityVector(-0.6f, 0.0f, 0.0f, 0.0f, 0f);
        var intents = DirectChildAllocator.AllocateWithZones(plan, personality, snapshots, evidence, null, null);
        AssertEqual(DirectChildRole.Main, intents[0].Role,
            "parity (own == enemy) is acceptable for Main even under cautious commander");
    }

    private static void DirectChildAllocatorWeakDistantBrigadeBecomesReserveNotUnknown()
    {
        // 2026-05-23 G3 fix: brigades that can't fit Main/SupportMain/Fix/
        // Screen/Refuse/Fallback (weak strength OR distant sector AND
        // SupportMain budget exhausted) now default to Reserve, not Unknown.
        // Unknown maps to FormUp task which produces incoherent behavior;
        // Reserve maps to ReserveWait + vanilla's macroai cascade.
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            mainEffortSector: 0, fixingSectors: new int[0], screeningSectors: new int[0],
            reserveCommitTriggerOdds: 1.2f, ageSeconds: 0f, jitterSeed: 0);
        var snapshots = new[]
        {
            new DirectChildSnapshot("c0", "a", 15, 0, "MainBde", true),
            new DirectChildSnapshot("c1", "a", 15, 0, "WeakDistantA", true),
            new DirectChildSnapshot("c2", "a", 15, 0, "WeakDistantB", true),
            new DirectChildSnapshot("c3", "a", 15, 0, "WeakDistantC", true),
            new DirectChildSnapshot("c4", "a", 15, 0, "WeakDistantD", true),
        };
        // Main at sector 0; weak (own=0) brigades at distant sectors 7/8/9/10.
        // None qualify for primary SupportMain (sector >2 from main) and the
        // fall-through SupportMain requires OwnStrengthBucket>=1. They should
        // become Reserve, not Unknown.
        var evidence = new[]
        {
            new DirectChildEvidence(2, 1, true, 0, 0, 0.8f),
            new DirectChildEvidence(0, 0, false, 7, 0, 0.4f),
            new DirectChildEvidence(0, 0, false, 8, 0, 0.4f),
            new DirectChildEvidence(0, 0, false, 9, 0, 0.4f),
            new DirectChildEvidence(0, 0, false, 10, 0, 0.4f),
        };
        var personality = new PersonalityVector(0.2f, 0f, 0f, 0f, 0f);
        var intents = DirectChildAllocator.AllocateWithZones(plan, personality, snapshots, evidence, null, null);
        AssertEqual(DirectChildRole.Main, intents[0].Role, "c0 is Main");
        for (int i = 1; i < intents.Count; i++)
        {
            AssertTrue(intents[i].Role != DirectChildRole.Unknown,
                "weak distant brigade c" + i + " must NOT be Unknown — got " + intents[i].Role);
        }
    }

    private static void DirectChildAllocatorAssignsSupportMainToAdjacentStrongChild()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            2, new[] { 0 }, new int[0], 1.2f, 0f, 0);
        var snapshots = new[]
        {
            new DirectChildSnapshot("c0", "a", 15, 0, "First", true),
            new DirectChildSnapshot("c1", "a", 15, 0, "Second", true),
            new DirectChildSnapshot("c2", "a", 15, 0, "Third", true),
        };
        var evidence = new[]
        {
            new DirectChildEvidence(2, 1, false, 1, 0, 0.5f),
            new DirectChildEvidence(3, 1, true,  2, 0, 0.7f),
            new DirectChildEvidence(2, 1, false, 3, 0, 0.5f),
        };
        var personality = new PersonalityVector(0.2f, 0.0f, 0.0f, 0.0f, 0f);
        var intents = DirectChildAllocator.AllocateWithZones(plan, personality, snapshots, evidence, null, null);
        AssertEqual(DirectChildRole.SupportMain, intents[0].Role);
        AssertEqual(DirectChildRole.Main, intents[1].Role);
        AssertEqual(DirectChildRole.SupportMain, intents[2].Role);
    }

    private static void DirectChildAllocatorAssignsFixOnFixingSectorWithContact()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            2, new[] { 0 }, new[] { 4 }, 1.2f, 0f, 0);
        var snapshots = new[] { new DirectChildSnapshot("c0", "a", 15, 0, "Pinning", true) };
        var evidence = new[] { new DirectChildEvidence(2, 2, true, 0, 0, 0.6f) };
        var personality = new PersonalityVector(0f, 0f, 0f, 0f, 0f);
        var intents = DirectChildAllocator.AllocateWithZones(plan, personality, snapshots, evidence, null, null);
        AssertEqual(DirectChildRole.Fix, intents[0].Role);
    }

    private static void DirectChildAllocatorFallbackBeatsFixUnderSevereOvermatch()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            2, new[] { 7 }, new[] { 4 }, 1.2f, 0f, 0);
        var snapshots = new[] { new DirectChildSnapshot("c0", "a", 15, 0, "Pressed Fixing Force", true) };
        var evidence = new[] { new DirectChildEvidence(2, 4, true, 7, 0, 0.95f) };
        var enemyAttack = new TacticalIntentModel(InferredIntent.Attack, 7, 0.9f, 0f, Array.Empty<EvidenceTag>());
        var personality = new PersonalityVector(0f, 0f, 0f, 0f, 0f);

        var intents = DirectChildAllocator.AllocateWithZones(plan, personality, snapshots, evidence, new[] { enemyAttack }, null);

        AssertEqual(DirectChildRole.Fallback, intents[0].Role);
        AssertEqual(DirectChildAxis.Withdraw, intents[0].Axis);
    }

    private static void DirectChildAllocatorFallbackBeatsMainUnderSevereOvermatch()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            7, new[] { 2 }, new[] { 4 }, 1.2f, 0f, 0);
        var snapshots = new[] { new DirectChildSnapshot("c0", "a", 15, 0, "Overmatched Main Effort", true) };
        var evidence = new[] { new DirectChildEvidence(2, 4, true, 7, 0, 0.95f) };
        var enemyAttack = new TacticalIntentModel(InferredIntent.Attack, 7, 0.9f, 0f, Array.Empty<EvidenceTag>());
        var personality = new PersonalityVector(0.2f, 0f, 0f, 0f, 0f);

        var intents = DirectChildAllocator.AllocateWithZones(plan, personality, snapshots, evidence, new[] { enemyAttack }, null);

        AssertEqual(DirectChildRole.Fallback, intents[0].Role);
        AssertEqual(DirectChildAxis.Withdraw, intents[0].Axis);
    }

    private static void DirectChildAllocatorAssignsReserveToUncommittedStrongChild()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            2, new int[0], new int[0], 1.2f, 0f, 0);
        var snapshots = new[]
        {
            new DirectChildSnapshot("c0", "a", 15, 0, "Main", true),
            new DirectChildSnapshot("c1", "a", 15, 0, "Reserve", true),
        };
        var evidence = new[]
        {
            new DirectChildEvidence(3, 2, true, 2, 0, 0.7f),
            new DirectChildEvidence(3, 0, false, 5, 0, 0.5f),
        };
        var personality = new PersonalityVector(0f, 0f, 0f, 0f, 0f);
        var intents = DirectChildAllocator.AllocateWithZones(plan, personality, snapshots, evidence, null, null);
        AssertEqual(DirectChildRole.Main, intents[0].Role);
        AssertEqual(DirectChildRole.Reserve, intents[1].Role);
    }

    private static void DirectChildAllocatorOutnumberedButNotInContactStaysInLine()
    {
        // 2026-05-22 line-of-battle fix: a brigade severely outnumbered by
        // bucket math but NOT in contact stays in the line as Reserve /
        // RefuseLeft/Right / fall-through SupportMain — NOT preemptive Fallback.
        // Previous behavior was scattering the army the moment the enemy was
        // visible.
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            mainEffortSector: 2, fixingSectors: new int[0], screeningSectors: new int[0],
            reserveCommitTriggerOdds: 1.2f, ageSeconds: 0f, jitterSeed: 0);
        var snapshots = new[] { new DirectChildSnapshot("c0", "a", 15, 0, "Distant Brigade", true) };
        var enemyAttack = new TacticalIntentModel(InferredIntent.Attack, 5, 0.9f, 0f, Array.Empty<EvidenceTag>());
        var personality = new PersonalityVector(0f, 0f, 0f, 0f, 0f);
        var intents = DirectChildAllocator.AllocateWithZones(
            plan, personality, snapshots,
            new[] { new DirectChildEvidence(1, 4, contactFlag: false, primarySector: 5, flankExposureBucket: 0, confidence01: 0.95f) },
            new[] { enemyAttack }, null);
        AssertTrue(intents[0].Role != DirectChildRole.Fallback,
            "outnumbered but not in contact must NOT be Fallback; got " + intents[0].Role);
    }

    private static void DirectChildAllocatorOutnumberedAndInContactWithStaticEnemyHolds()
    {
        // High visibility (confidence ≥ 0.9) used to count as "active pressure"
        // even when enemy intent wasn't Attack. After 2026-05-22 tightening,
        // visibility alone no longer triggers Fallback — enemy intent must
        // actually be Attack.
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            2, new int[0], new int[0], 1.2f, 0f, 0);
        var snapshots = new[] { new DirectChildSnapshot("c0", "a", 15, 0, "Holding Brigade", true) };
        // Enemy is visible (high confidence) AND outnumbering, but its intent
        // is NOT Attack (e.g., Defend). Should NOT trigger Fallback.
        var enemyHold = new TacticalIntentModel(InferredIntent.Defend, 5, 0.9f, 0f, Array.Empty<EvidenceTag>());
        var personality = new PersonalityVector(0f, 0f, 0f, 0f, 0f);
        var intents = DirectChildAllocator.AllocateWithZones(
            plan, personality, snapshots,
            new[] { new DirectChildEvidence(2, 4, contactFlag: true, primarySector: 5, flankExposureBucket: 0, confidence01: 0.95f) },
            new[] { enemyHold }, null);
        AssertTrue(intents[0].Role != DirectChildRole.Fallback,
            "outnumbered but enemy-not-attacking must hold the line; got " + intents[0].Role);
    }

    private static void DirectChildAllocatorAssignsFallbackOnAdverseOddsAndAttack()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            2, new int[0], new int[0], 1.2f, 0f, 0);
        var snapshots = new[] { new DirectChildSnapshot("c0", "a", 15, 0, "Pressed", true) };
        var enemyAttack = new TacticalIntentModel(InferredIntent.Attack, 0, 0.8f, 0f, Array.Empty<EvidenceTag>());
        var personality = new PersonalityVector(0f, 0f, 0f, 0f, 0f);
        var intents = DirectChildAllocator.AllocateWithZones(
            plan, personality, snapshots,
            new[] { new DirectChildEvidence(1, 3, true, 0, 0, 0.7f) },
            new[] { enemyAttack }, null);
        AssertEqual(DirectChildRole.Fallback, intents[0].Role);
    }

    private static void DirectChildAllocatorAllocatesRefuseToFlankWithExposure()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            2, new int[0], new int[0], 1.2f, 0f, 0);
        var snapshots = new[]
        {
            new DirectChildSnapshot("c0", "a", 15, 0, "Left", true),
            new DirectChildSnapshot("c1", "a", 15, 0, "Right", true),
        };
        var evidence = new[]
        {
            new DirectChildEvidence(2, 2, false, 0, 3, 0.5f),
            new DirectChildEvidence(2, 2, false, 4, 3, 0.5f),
        };
        var personality = new PersonalityVector(0f, 0f, 0f, 0f, 0f);
        var intents = DirectChildAllocator.AllocateWithZones(plan, personality, snapshots, evidence, null, null);
        AssertEqual(DirectChildRole.RefuseLeft, intents[0].Role);
        AssertEqual(DirectChildRole.RefuseRight, intents[1].Role);
    }

    private static void DirectChildAllocatorDeterministicOnRegistrationOrderTie()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            2, new int[0], new int[0], 1.2f, 0f, 0);
        var snapshots = new[]
        {
            new DirectChildSnapshot("z-late", "a", 15, 0, "Z", true),
            new DirectChildSnapshot("a-early", "a", 15, 0, "A", true),
        };
        var evidence = new[]
        {
            new DirectChildEvidence(2, 1, true, 2, 0, 0.5f),
            new DirectChildEvidence(2, 1, true, 2, 0, 0.5f),
        };
        var personality = new PersonalityVector(0f, 0f, 0f, 0f, 0f);
        var intents = DirectChildAllocator.AllocateWithZones(plan, personality, snapshots, evidence, null, null);
        AssertEqual(DirectChildRole.Main, intents[0].Role, "first registered wins ties");
        AssertTrue(intents[1].Role != DirectChildRole.Main, "second registered did not also become Main");
    }

    private static void DirectChildAllocatorFallsBackToStrongestMainWhenPlanSectorEmpty()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            99, new int[0], new int[0], 1.2f, 0f, 0);
        var snapshots = new[]
        {
            new DirectChildSnapshot("c0", "a", 15, 0, "Skirmish Contact", true),
            new DirectChildSnapshot("c1", "a", 15, 0, "Strong Contact", true),
            new DirectChildSnapshot("c2", "a", 15, 0, "Nearby Support", true),
        };
        var evidence = new[]
        {
            new DirectChildEvidence(2, 2, true, 1, 0, 0.6f),
            new DirectChildEvidence(3, 1, true, 4, 0, 0.75f),
            new DirectChildEvidence(2, 1, false, 5, 0, 0.55f),
        };
        var personality = new PersonalityVector(0f, 0f, 0f, 0f, 0f);

        var intents = DirectChildAllocator.AllocateWithZones(plan, personality, snapshots, evidence, null, null);

        AssertEqual(DirectChildRole.Main, intents[1].Role, "strongest contact becomes fallback main effort");
        AssertEqual(4, intents[1].AxisSector, "fallback main effort sector");
        AssertEqual(DirectChildRole.SupportMain, intents[2].Role, "adjacent force masses on fallback main effort");
    }

    private static void DirectChildAllocatorKeepsAggressiveProbeMovingWhenPlannedSectorEmpty()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.FrontalShockAssault, BattlePhase.Probe,
            0, new int[0], new int[0], 1.0f, 0f, 0);
        var snapshots = new[]
        {
            new DirectChildSnapshot("c0", "a", 14, -1, "Left Wing", true),
            new DirectChildSnapshot("c1", "a", 14, -1, "Right Wing", true),
        };
        var evidence = new[]
        {
            new DirectChildEvidence(2, 1, false, 1, 3, 0.05f),
            new DirectChildEvidence(2, 1, false, 3, 3, 0.05f),
        };
        var personality = new PersonalityVector(0.8f, -0.6f, 0.5f, 0f, 0f);

        var intents = DirectChildAllocator.AllocateWithZones(plan, personality, snapshots, evidence, null, null);

        AssertEqual(DirectChildRole.Main, intents[0].Role, "first aggressive wing becomes provisional main");
        AssertEqual(1, intents[0].AxisSector, "provisional main axis follows observed child sector");
        // Severe flank exposure (bucket 3) — opposite wing still must lock to
        // flank-guard. Was bucket=2 with old FlankExposureRefuseThreshold=2;
        // 2026-05-22 raised threshold to 3 (only SEVERE exposure triggers
        // refuse), so the test now uses bucket=3 to assert the same
        // doctrinal intent (severe exposure → lock flank guard).
        AssertEqual(DirectChildRole.RefuseRight, intents[1].Role, "severely exposed wing still guards the flank");
    }

    private static void DirectChildAllocatorReserveWhenNoPlanMainEffortMatch()
    {
        // 2026-05-23 G3: when no brigade matches the planned main effort sector
        // AND no fallback can be picked, the lonely brigade defaults to Reserve
        // (was Unknown before). Reserve produces ReserveWait task; combined
        // with vanilla's macroai-driven AdjustGroupAIStance (F sync), the
        // brigade gets a coherent stance instead of falling to FormUp from
        // Unknown role.
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            99, new int[0], new int[0], 1.2f, 0f, 0);
        var snapshots = new[] { new DirectChildSnapshot("c0", "a", 15, 0, "Lonely", true) };
        var evidence = new[] { new DirectChildEvidence(1, 1, false, 0, 0, 0.3f) };
        var personality = new PersonalityVector(0f, 0f, 0f, 0f, 0f);
        var intents = DirectChildAllocator.AllocateWithZones(plan, personality, snapshots, evidence, null, null);
        AssertEqual(DirectChildRole.Reserve, intents[0].Role);
    }

    private static void DirectChildAllocatorAssignsScreenOnScreeningSectorWithLowStrengths()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            2, new int[0], new[] { 4 }, 1.2f, 0f, 0);
        var snapshots = new[] { new DirectChildSnapshot("c0", "a", 15, 0, "Screening", true) };
        var evidence = new[] { new DirectChildEvidence(1, 1, false, 4, 0, 0.3f) };
        var personality = new PersonalityVector(0f, 0f, 0f, 0f, 0f);
        var intents = DirectChildAllocator.AllocateWithZones(plan, personality, snapshots, evidence, null, null);
        AssertEqual(DirectChildRole.Screen, intents[0].Role);
        AssertEqual(DirectChildAxis.Hold, intents[0].Axis);
    }

    private static void DirectChildAllocatorHandlesMismatchedPerChildIntentLength()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            2, new int[0], new int[0], 1.2f, 0f, 0);
        var snapshots = new[] { new DirectChildSnapshot("c0", "a", 15, 0, "Pressed", true) };
        var personality = new PersonalityVector(0f, 0f, 0f, 0f, 0f);
        // perChildEnemyIntent length 0 != snapshots length 1; allocator must rebuild internally.
        var intents = DirectChildAllocator.AllocateWithZones(
            plan, personality, snapshots,
            new[] { new DirectChildEvidence(1, 3, true, 0, 0, 0.7f) },
            Array.Empty<TacticalIntentModel>(), null);
        // With Unknown enemy intent, Fallback rule (which needs Attack intent) cannot fire.
        // Adverse-odds child with no other rule match used to land Unknown; with
        // the 2026-05-23 G3 fall-through default → Reserve (so vanilla's macroai
        // cascade can give the brigade a coherent stance via F sync).
        AssertEqual(1, intents.Count);
        AssertEqual(DirectChildRole.Reserve, intents[0].Role);
        AssertEqual(InferredIntent.Unknown, intents[0].EnemyIntent.PrimaryIntent);
    }

    private static void DirectChildDiscoveryProbeHandlesEmptyUnitsused()
    {
        var snaps = DirectChildDiscovery.Probe(Array.Empty<DirectChildDiscovery.RegimentProbe>(), commandHierarchyShift: 0);
        AssertEqual(0, snaps.Count);
    }

    private static void DirectChildDiscoveryProbeFiltersBelowEffectiveCommandMin()
    {
        var probes = new[]
        {
            new DirectChildDiscovery.RegimentProbe(instanceId: 100, unittyp: 13, name: "Skirmisher", active: true, parentInstanceId: 0, isDirectChild: false),
            new DirectChildDiscovery.RegimentProbe(instanceId: 200, unittyp: 16, name: "Army A", active: true, parentInstanceId: 0, isDirectChild: false),
            new DirectChildDiscovery.RegimentProbe(instanceId: 300, unittyp: 15, name: "Corps A", active: true, parentInstanceId: 200, isDirectChild: true),
        };
        var snaps = DirectChildDiscovery.Probe(probes, commandHierarchyShift: 0);
        AssertEqual(1, snaps.Count);
        AssertEqual("child-300", snaps[0].ChildId);
        AssertEqual("army-200", snaps[0].ParentArmyId);
    }

    private static void DirectChildDiscoveryProbeSelectsHighestUnittypAsArmyRoot()
    {
        var probes = new[]
        {
            new DirectChildDiscovery.RegimentProbe(100, 16, "Army", true, 0, false),
            new DirectChildDiscovery.RegimentProbe(200, 15, "Corps Direct", true, 100, true),
            new DirectChildDiscovery.RegimentProbe(300, 15, "Corps Independent", true, 999 /* not under army */, false),
        };
        var snaps = DirectChildDiscovery.Probe(probes, commandHierarchyShift: 0);
        AssertEqual(1, snaps.Count);
        AssertEqual("child-200", snaps[0].ChildId);
    }

    private static void DirectChildDiscoveryProbeHandlesNegativeCommandHierarchyShift()
    {
        // shift = -1: army root unittyp == 15 (vanilla "division" label), child unittyp == 14
        var probes = new[]
        {
            new DirectChildDiscovery.RegimentProbe(100, 15, "Early-war Army", true, 0, false),
            new DirectChildDiscovery.RegimentProbe(200, 14, "Early-war Corps", true, 100, true),
        };
        var snaps = DirectChildDiscovery.Probe(probes, commandHierarchyShift: -1);
        AssertEqual(1, snaps.Count);
        AssertEqual(14, snaps[0].RawUnitTyp);
        AssertEqual(15, snaps[0].EffectiveCommandLevel); // 14 - (-1) = 15 = unshifted-Corps
    }

    private static void DirectChildDiscoveryProbeSynthesizesWhenZeroDirectChildren()
    {
        var probes = new[]
        {
            new DirectChildDiscovery.RegimentProbe(100, 16, "Lonely Army", true, 0, false),
            // no children attached
        };
        var snaps = DirectChildDiscovery.Probe(probes, commandHierarchyShift: 0);
        AssertEqual(1, snaps.Count);
        AssertEqual("synth-army-100", snaps[0].ChildId);
        AssertEqual("army-100", snaps[0].ParentArmyId);
        AssertEqual(16, snaps[0].RawUnitTyp);
    }

    private static void DirectChildDiscoveryProbeIteratesEachArmyRootForMultiArmySide()
    {
        var probes = new[]
        {
            new DirectChildDiscovery.RegimentProbe(100, 16, "ArmyA", true, 0, false),
            new DirectChildDiscovery.RegimentProbe(200, 16, "ArmyB", true, 0, false),
            new DirectChildDiscovery.RegimentProbe(300, 15, "Corps under A", true, 100, true),
            new DirectChildDiscovery.RegimentProbe(400, 15, "Corps under B", true, 200, true),
        };
        var snaps = DirectChildDiscovery.Probe(probes, commandHierarchyShift: 0);
        AssertEqual(2, snaps.Count);
        AssertEqual("army-100", snaps[0].ParentArmyId);
        AssertEqual("army-200", snaps[1].ParentArmyId);
    }

    private static void DirectChildEvidenceBuilderBucketsStrengthUsing05Ratio()
    {
        // NOTE: plan's verbatim test asserted OwnStrengthBucket=1 with comment "ratio 0.6 → bucket 1",
        // but the verbatim bucket spec (and self-review checklist) state 1500 ≤ s < 3000 → 2.
        // Spec/checklist take precedence over the test author's stale ratio-bucket comment;
        // expectations adjusted to match the canonical strength-bucket scheme.
        var enemy = new EnemyVisibleState(
            new[]
            {
                new EnemyVisibleSector(0,  100f,  100f, false),
                new EnemyVisibleSector(1, 1500f, 2500f, false), // own 1500 → bucket 2, enemy 2500 → bucket 2
            },
            enemyReserveCommitFraction: 0.4f,
            anyContactSpotted: false,
            anyContactBroken: false);

        var evidence = DirectChildEvidenceBuilder.BuildAll(
            snapshots: new[]
            {
                new DirectChildSnapshot("c0", "a", 15, 0, "First", true),
            },
            primarySectorPerSnapshot: new[] { 1 },
            flankExposureBucketPerSnapshot: new[] { 0 },
            enemy);

        AssertEqual(1, evidence.Count);
        AssertEqual(1, evidence[0].PrimarySector);
        AssertEqual(2, evidence[0].OwnStrengthBucket);
        AssertEqual(2, evidence[0].EnemyStrengthBucket);
    }

    private static void DirectChildEvidenceBuilderPropagatesContactFlag()
    {
        var enemy = new EnemyVisibleState(
            new[] { new EnemyVisibleSector(2, 500f, 500f, recentFire: true) },
            0.3f, true, false);
        var evidence = DirectChildEvidenceBuilder.BuildAll(
            new[] { new DirectChildSnapshot("c0", "a", 15, 0, "First", true) },
            new[] { 2 },
            new[] { 0 },
            enemy);
        AssertTrue(evidence[0].ContactFlag, "recent fire propagates as ContactFlag");
    }

    private static void DirectChildEvidenceBuilderZeroOwnWhenSectorMissing()
    {
        var enemy = new EnemyVisibleState(
            new[] { new EnemyVisibleSector(0, 1000f, 0f, false) },
            0f, false, false);
        var evidence = DirectChildEvidenceBuilder.BuildAll(
            new[] { new DirectChildSnapshot("c0", "a", 15, 0, "First", true) },
            new[] { 99 /* sector not present in EnemyVisibleState */ },
            new[] { 0 },
            enemy);
        AssertEqual(0, evidence[0].OwnStrengthBucket);
        AssertEqual(0, evidence[0].EnemyStrengthBucket);
        AssertTrue(!evidence[0].ContactFlag, "missing sector → no contact");
    }

    private static void DirectChildGateDisabledAllowsAll()
    {
        var input = new TacticalDirectChildGate.Input(
            gateEnabled: false, sideIsAi: true,
            role: DirectChildRole.Reserve, axisSector: 2, primarySector: 2,
            intendedTargetBearingFromGroupRadians: (float)Math.PI,
            intendedTargetDistanceFromGroup: 100f,
            nearestEnemyBearingFromGroupRadians: (float)Math.PI,
            feudMaxDistance: 2000f);
        var d = TacticalDirectChildGate.Decide(input);
        AssertTrue(d.Allow, "gate disabled allows");
        AssertContains(d.Reason, "gate-disabled", "reason mentions gate-disabled");
    }

    private static void DirectChildGatePlayerSideAllowsAll()
    {
        var input = new TacticalDirectChildGate.Input(
            true, false, DirectChildRole.Reserve, 2, 2,
            (float)Math.PI, 100f, 0f, 2000f);
        var d = TacticalDirectChildGate.Decide(input);
        AssertTrue(d.Allow, "player side allows");
        AssertContains(d.Reason, "player-side", "reason mentions player-side");
    }

    private static void DirectChildGateUnknownRoleAllows()
    {
        var input = new TacticalDirectChildGate.Input(
            true, true, DirectChildRole.Unknown, 2, 2,
            (float)Math.PI, 100f, 0f, 2000f);
        var d = TacticalDirectChildGate.Decide(input);
        AssertTrue(d.Allow, "Unknown role yields no opinion");
        AssertContains(d.Reason, "role-unknown", "reason mentions role-unknown");
    }

    private static void DirectChildGateReserveDenies()
    {
        var input = new TacticalDirectChildGate.Input(
            true, true, DirectChildRole.Reserve, 2, 2,
            0f, 100f, 0f, 2000f);
        var d = TacticalDirectChildGate.Decide(input);
        AssertTrue(!d.Allow, "Reserve denies movement");
        AssertContains(d.Reason, "reserve-not-committed", "reason");
    }

    private static void DirectChildGateMainAllowsOnAxisDeniesOffAxis()
    {
        // Main: AxisSector=2, PrimarySector=2. IntendedTargetSector=2 (matches AxisSector) → allow.
        var inputAllowAxis = new TacticalDirectChildGate.Input(
            true, true, DirectChildRole.Main, axisSector: 2, primarySector: 2,
            intendedTargetBearingFromGroupRadians: 0f,
            intendedTargetDistanceFromGroup: 500f,
            nearestEnemyBearingFromGroupRadians: 0f,
            feudMaxDistance: 2000f).WithIntendedTargetSector(2);
        var dAllowAxis = TacticalDirectChildGate.Decide(inputAllowAxis);
        AssertTrue(dAllowAxis.Allow, "Main allows movement toward AxisSector");

        // SupportMain: AxisSector=2 (army's main effort), PrimarySector=3 (own sector).
        // IntendedTargetSector=3 matches PrimarySector → allow (SupportMain may reinforce its own frontage).
        var inputAllowOwn = new TacticalDirectChildGate.Input(
            true, true, DirectChildRole.SupportMain, axisSector: 2, primarySector: 3,
            0f, 500f, 0f, 2000f).WithIntendedTargetSector(3);
        var dAllowOwn = TacticalDirectChildGate.Decide(inputAllowOwn);
        AssertTrue(dAllowOwn.Allow, "SupportMain allows movement toward own PrimarySector");

        // Main: AxisSector=2, PrimarySector=2. IntendedTargetSector=5 (off-axis) → deny.
        var inputDeny = new TacticalDirectChildGate.Input(
            true, true, DirectChildRole.Main, axisSector: 2, primarySector: 2,
            0f, 500f, 0f, 2000f).WithIntendedTargetSector(5);
        var dDeny = TacticalDirectChildGate.Decide(inputDeny);
        AssertTrue(!dDeny.Allow, "Main denies movement to a sector matching neither AxisSector nor PrimarySector");
        AssertContains(dDeny.Reason, "off-axis", "reason mentions off-axis");
    }

    private static void DirectChildGateFixAllowsShortDeniesWide()
    {
        var inputAllow = new TacticalDirectChildGate.Input(
            true, true, DirectChildRole.Fix, 2, 2,
            0f,
            intendedTargetDistanceFromGroup: 1000f, // < 0.7 * feudMax
            nearestEnemyBearingFromGroupRadians: 0f,
            feudMaxDistance: 2000f);
        var dAllow = TacticalDirectChildGate.Decide(inputAllow);
        AssertTrue(dAllow.Allow, "Fix allows short pressure movement");
        var inputDeny = new TacticalDirectChildGate.Input(
            true, true, DirectChildRole.Fix, 2, 2,
            0f,
            intendedTargetDistanceFromGroup: 1900f, // > 0.7 * feudMax
            nearestEnemyBearingFromGroupRadians: 0f,
            feudMaxDistance: 2000f);
        var dDeny = TacticalDirectChildGate.Decide(inputDeny);
        AssertTrue(!dDeny.Allow, "Fix denies wide lateral");
        AssertContains(dDeny.Reason, "fix-no-wide", "reason");
    }

    private static void DirectChildGateScreenAllowsInSectorDeniesOutOfSector()
    {
        var inputAllow = new TacticalDirectChildGate.Input(
            true, true, DirectChildRole.Screen, axisSector: 0, primarySector: 4,
            0f, 500f, 0f, 2000f);
        inputAllow = inputAllow.WithIntendedTargetSector(4);
        var dAllow = TacticalDirectChildGate.Decide(inputAllow);
        AssertTrue(dAllow.Allow, "Screen allows in-sector");
        var inputDeny = inputAllow.WithIntendedTargetSector(2);
        var dDeny = TacticalDirectChildGate.Decide(inputDeny);
        AssertTrue(!dDeny.Allow, "Screen denies out-of-sector");
        AssertContains(dDeny.Reason, "screen-out-of-sector", "reason");
    }

    private static void DirectChildGateFallbackAllowsAwayDeniesToward()
    {
        // Enemy is north (bearing PI/2). Withdrawal is south.
        var inputAllow = new TacticalDirectChildGate.Input(
            true, true, DirectChildRole.Fallback, 0, 0,
            intendedTargetBearingFromGroupRadians: (float)(-Math.PI / 2.0), // south
            intendedTargetDistanceFromGroup: 500f,
            nearestEnemyBearingFromGroupRadians: (float)(Math.PI / 2.0),     // north
            feudMaxDistance: 2000f);
        var dAllow = TacticalDirectChildGate.Decide(inputAllow);
        AssertTrue(dAllow.Allow, "Fallback allows withdrawal-bearing");

        var inputDeny = new TacticalDirectChildGate.Input(
            true, true, DirectChildRole.Fallback, 0, 0,
            intendedTargetBearingFromGroupRadians: (float)(Math.PI / 2.0),  // toward enemy
            intendedTargetDistanceFromGroup: 500f,
            nearestEnemyBearingFromGroupRadians: (float)(Math.PI / 2.0),
            feudMaxDistance: 2000f);
        var dDeny = TacticalDirectChildGate.Decide(inputDeny);
        AssertTrue(!dDeny.Allow, "Fallback denies toward-enemy");
        AssertContains(dDeny.Reason, "fallback-not-withdraw", "reason");
    }

    private static void DirectChildGateRefuseLeftAllowsInSectorDeniesOut()
    {
        var inputAllow = new TacticalDirectChildGate.Input(
            true, true, DirectChildRole.RefuseLeft, axisSector: 0, primarySector: 0,
            0f, 500f, 0f, 2000f).WithIntendedTargetSector(0);
        var dAllow = TacticalDirectChildGate.Decide(inputAllow);
        AssertTrue(dAllow.Allow, "RefuseLeft allows in flank sector");
        var inputDeny = inputAllow.WithIntendedTargetSector(3);
        var dDeny = TacticalDirectChildGate.Decide(inputDeny);
        AssertTrue(!dDeny.Allow, "RefuseLeft denies out of flank sector");
        AssertContains(dDeny.Reason, "refuse-out-of-sector", "reason");
    }

    private static void ParseInstanceIdChildPositive()
    {
        AssertEqual(12345, TacticalBattleCoordinator.ParseInstanceIdFromChildId("child-12345"));
    }

    private static void ParseInstanceIdChildNegative()
    {
        // Unity GameObject InstanceIDs are routinely negative; the parser must NOT
        // treat the sign character as the prefix delimiter (which a LastIndexOf('-')
        // strategy would). Smoke-driven regression from commit a17ee9c.
        AssertEqual(-26786, TacticalBattleCoordinator.ParseInstanceIdFromChildId("child--26786"));
    }

    private static void ParseInstanceIdSynthArmyPositive()
    {
        AssertEqual(98765, TacticalBattleCoordinator.ParseInstanceIdFromChildId("synth-army-98765"));
    }

    private static void ParseInstanceIdSynthArmyNegative()
    {
        // Same negative-id contract as child--{id}: the prefix is "synth-army-" and
        // the suffix retains its leading sign.
        AssertEqual(-26350, TacticalBattleCoordinator.ParseInstanceIdFromChildId("synth-army--26350"));
    }

    private static void DirectChildGateNegativeTargetSectorCoercesToPrimary()
    {
        // The Input ctor's intendedTargetSector < 0 sentinel must coerce to primarySector
        // so callers that can't resolve the target sector (e.g. ResolveTargetSector returning -1)
        // get an in-sector decision for Screen/Refuse roles by default.
        var input = new TacticalDirectChildGate.Input(
            gateEnabled: true, sideIsAi: true,
            role: DirectChildRole.Screen, axisSector: 0, primarySector: 3,
            intendedTargetBearingFromGroupRadians: 0f,
            intendedTargetDistanceFromGroup: 100f,
            nearestEnemyBearingFromGroupRadians: 0f,
            feudMaxDistance: 2000f,
            intendedTargetSector: -1);
        AssertEqual(3, input.IntendedTargetSector);
        var d = TacticalDirectChildGate.Decide(input);
        AssertTrue(d.Allow, "Screen with -1 target sector coerces to primary and allows in-sector");

        var refuseInput = new TacticalDirectChildGate.Input(
            gateEnabled: true, sideIsAi: true,
            role: DirectChildRole.RefuseLeft, axisSector: 0, primarySector: 5,
            intendedTargetBearingFromGroupRadians: 0f,
            intendedTargetDistanceFromGroup: 100f,
            nearestEnemyBearingFromGroupRadians: 0f,
            feudMaxDistance: 2000f,
            intendedTargetSector: -1);
        AssertEqual(5, refuseInput.IntendedTargetSector);
        var dRefuse = TacticalDirectChildGate.Decide(refuseInput);
        AssertTrue(dRefuse.Allow, "RefuseLeft with -1 target sector coerces to primary and allows in-flank-sector");
    }

    private static void AllocatorWithZonesOverridesEvidence()
    {
        // Zone assignment says c0=LeftWing(Main), c1=Center(Fix). Evidence
        // alone would have produced different roles (Reserve / Unknown).
        // Zone-aware allocator must honor the zone.
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            mainEffortSector: 5, fixingSectors: new int[0], screeningSectors: new int[0],
            reserveCommitTriggerOdds: 1.2f, ageSeconds: 0f, jitterSeed: 0);
        var snapshots = new[]
        {
            new DirectChildSnapshot("c0", "a", 15, 0, "WestCorps", true),
            new DirectChildSnapshot("c1", "a", 15, 0, "CenterCorps", true),
        };
        // Evidence has both brigades at sectors NOT matching plan.MainEffortSector
        // and weak strength — without zones, both would end up Reserve/Unknown.
        var evidence = new[]
        {
            new DirectChildEvidence(1, 0, false, 1, 0, 0.5f),
            new DirectChildEvidence(1, 0, false, 2, 0, 0.5f),
        };
        var perChildIntent = new[]
        {
            new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, System.Array.Empty<EvidenceTag>()),
            new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, System.Array.Empty<EvidenceTag>()),
        };
        var zones = new[]
        {
            new WhiskeyRealism.Tactical.Orchestrator.Plays.CorpsAssignment(
                "c0", WhiskeyRealism.Tactical.Orchestrator.Plays.PlayRectKind.LeftWing, 0, -500f, 0f, 0f),
            new WhiskeyRealism.Tactical.Orchestrator.Plays.CorpsAssignment(
                "c1", WhiskeyRealism.Tactical.Orchestrator.Plays.PlayRectKind.Center, 1, 0f, 0f, 0f),
        };
        var personality = new PersonalityVector(0.5f, 0f, 0f, 0f, 0f);
        var intents = DirectChildAllocator.AllocateWithZones(
            plan, personality, snapshots, evidence, perChildIntent, zones);
        AssertEqual(DirectChildRole.Main, intents[0].Role, "c0 LeftWing → Main");
        AssertEqual(DirectChildRole.Fix, intents[1].Role, "c1 Center → Fix");
    }

    private static void AllocatorWithoutZonesUsesExistingEvidencePath()
    {
        // Empty zones → falls through to existing evidence-based allocation.
        // Existing behavior must be preserved.
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            mainEffortSector: 2, fixingSectors: new int[0], screeningSectors: new int[0],
            reserveCommitTriggerOdds: 1.2f, ageSeconds: 0f, jitterSeed: 0);
        var snapshots = new[]
        {
            new DirectChildSnapshot("c0", "a", 15, 0, "MainCandidate", true),
        };
        var evidence = new[]
        {
            new DirectChildEvidence(3, 1, true, 2, 0, 0.8f),
        };
        var personality = new PersonalityVector(0.5f, 0f, 0f, 0f, 0f);
        var intentsNoZone = DirectChildAllocator.AllocateWithZones(
            plan, personality, snapshots, evidence, null, null);
        AssertEqual(DirectChildRole.Main, intentsNoZone[0].Role,
            "without zones, evidence-based path picks brigade as Main");
    }
}
