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

internal static class StrategicDefenseTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("defense force sizer avoids oversized army for small threat", DefenseForceSizerAvoidsOversizedArmyForSmallThreat),
            ("defense force sizer accepts large force for large threat", DefenseForceSizerAcceptsLargeForceForLargeThreat),
            ("defense posture defaults to not-evaluated", DefensePostureDefaultsToNotEvaluated),
            ("defense threat carries signature and posture", DefenseThreatCarriesSignatureAndPosture),
            ("threat signature for sif uses the belief key", ThreatSignatureForSifUsesBeliefKey),
            ("threat signature for raid uses the belief key and asset", ThreatSignatureForRaidUsesBeliefKeyAndAsset),
            ("threat signature for asset uses sorted top-n enemies", ThreatSignatureForAssetUsesSortedTopN),
            ("threat signature is stable across reordered enemies", ThreatSignatureIsStableAcrossReorderedEnemies),
            ("threat signature for raid handles null asset", ThreatSignatureForRaidHandlesNullAsset),
            ("threat signature for asset handles null name", ThreatSignatureForAssetHandlesNullName),
            ("threat signature for asset clamps topn at one", ThreatSignatureForAssetClampsTopNAtOne),
            ("package aggregator picks smaller adequate over remote oversized", PackageAggregatorPicksSmallerAdequateOverRemoteOversized),
            ("package aggregator stops at overshoot guard", PackageAggregatorStopsAtOvershootGuard),
            ("package aggregator emits understrength flag", PackageAggregatorEmitsUnderstrengthFlag),
            ("package aggregator suppresses overmatch reason", PackageAggregatorSuppressesOvermatchReason),
            ("package aggregator deterministic order on tied scores", PackageAggregatorDeterministicOrderOnTiedScores),
            ("cooldown table extends on threat re-detection", CooldownTableExtendsOnThreatRedetection),
            ("cooldown table decrements once per tick", CooldownTableDecrementsOncePerTick),
            ("cooldown table expires at zero", CooldownTableExpiresAtZero),
            ("defense ledger coastal guard forbids cross-map", DefenseLedgerCoastalGuardForbidsCrossMap),
            ("defense ledger minor raid forbids cross-map", DefenseLedgerMinorRaidForbidsCrossMap),
            ("defense ledger decisive landing allows cross-theater", DefenseLedgerDecisiveLandingAllowsCrossTheater),
            ("defense ledger same-theater adequate beats remote oversized", DefenseLedgerSameTheaterAdequateBeatsRemoteOversized),
            ("defense ledger guard budget caps low-value ports", DefenseLedgerGuardBudgetCapsLowValuePorts),
            ("defense ledger active invasion persists through favorable tick", DefenseLedgerActiveInvasionPersistsThroughFavorableTick),
            ("defense ledger recovered threat releases after cooldown", DefenseLedgerRecoveredThreatReleasesAfterCooldown),
            ("defense ledger player cic short-circuits alliance", DefenseLedgerPlayerCicShortCircuitsAlliance),
            ("defense ledger wl subordinate protects only marked unit", DefenseLedgerWlSubordinateProtectsOnlyMarkedUnit),
            ("defense ledger critical-front candidate rejected unless decisive", DefenseLedgerCriticalFrontCandidateRejectedUnlessDecisive),
            ("defense ledger river harbor detects without sif", DefenseLedgerRiverHarborDetectsWithoutSif),
            ("defense ledger raidforce coverage", DefenseLedgerRaidforceCoverage),
            ("defense ledger debug seainvasionsactive off falls back", DefenseLedgerDebugSeainvasionsactiveOffFallsBack),
            ("defense ledger telemetry signature non-empty and posture-prefixed", DefenseLedgerTelemetrySignaturePopulated),
            ("defense telemetry summary compresses response burst", DefenseTelemetrySummaryCompressesResponseBurst),
            ("defense ledger does not crash on europe alliance index", DefenseLedgerDoesNotCrashOnEuropeAllianceIndex),
            ("defense ledger asset proximity stays local and cannot custom order", DefenseLedgerAssetProximityStaysLocalAndCannotCustomOrder),
            ("defense ledger donor theater budget blocks critical front export", DefenseLedgerDonorTheaterBudgetBlocksCriticalFrontExport),
            ("defense ledger formation directive blocks defense movement", DefenseLedgerFormationDirectiveBlocksDefenseMovement),
            ("defense ledger capital defense package is capped", DefenseLedgerCapitalDefensePackageIsCapped),
        });
    }

    private static void DefenseForceSizerAvoidsOversizedArmyForSmallThreat()
    {
        float division = DefenseForceSizer.ScoreCandidate(
            activeStrength: 6000f,
            morale: 0.9f,
            readinessStep: 2f,
            distance: 220f,
            desiredStrength: 4500f,
            inOffensiveOperation: false,
            caution: 0.2f,
            aggression: 0f);
        float army = DefenseForceSizer.ScoreCandidate(
            activeStrength: 22000f,
            morale: 0.9f,
            readinessStep: 2f,
            distance: 80f,
            desiredStrength: 4500f,
            inOffensiveOperation: false,
            caution: 0.2f,
            aggression: 0f);

        AssertTrue(division < army, "smaller sufficient force should beat nearby oversized army");
    }

    private static void DefenseForceSizerAcceptsLargeForceForLargeThreat()
    {
        float division = DefenseForceSizer.ScoreCandidate(
            activeStrength: 6000f,
            morale: 0.9f,
            readinessStep: 2f,
            distance: 140f,
            desiredStrength: 18000f,
            inOffensiveOperation: false,
            caution: 0.2f,
            aggression: 0f);
        float army = DefenseForceSizer.ScoreCandidate(
            activeStrength: 22000f,
            morale: 0.9f,
            readinessStep: 2f,
            distance: 160f,
            desiredStrength: 18000f,
            inOffensiveOperation: false,
            caution: 0.2f,
            aggression: 0f);

        AssertTrue(army < division, "large threat should allow the larger force");
    }

    private static void DefensePostureDefaultsToNotEvaluated()
    {
        AssertEqual(DefensePosture.NotEvaluated, default(DefensePosture));
        AssertEqual(ThreatScale.None, default(ThreatScale));
    }

    private static void DefenseThreatCarriesSignatureAndPosture()
    {
        var threat = new DefenseThreat
        {
            Signature = "sif:1234:Norfolk:Hampton",
            Posture = DefensePosture.ActiveInvasion,
            Scale = ThreatScale.Landing,
            AssetName = "norfolk-harbor",
            EnemyStrength = 4200f,
            DesiredStrength = 6500f,
            EscalationReason = "landed-port-threat"
        };
        AssertEqual("sif:1234:Norfolk:Hampton", threat.Signature);
        AssertEqual(DefensePosture.ActiveInvasion, threat.Posture);
        AssertEqual(ThreatScale.Landing, threat.Scale);
    }

    // G1: sea invasions are keyed on the believed enemy group's UnitKey (no landing spot or port:
    // that is the enemy's plan state).
    private static void ThreatSignatureForSifUsesBeliefKey()
    {
        var sig = DefenseThreatSignature.ForSeaInvasion("Burnside Expedition:42");
        AssertEqual("sif:Burnside Expedition:42", sig);

        var nullKey = DefenseThreatSignature.ForSeaInvasion(null);
        AssertEqual("sif:<no-key>", nullKey);
    }

    private static void ThreatSignatureForRaidUsesBeliefKeyAndAsset()
    {
        var sig = DefenseThreatSignature.ForRaid(enemyUnitKey: "Stuart:7", nearestAssetName: "wilmington-harbor");
        AssertEqual("raid:Stuart:7:wilmington-harbor", sig);
    }

    private static void ThreatSignatureForAssetUsesSortedTopN()
    {
        var sig = DefenseThreatSignature.ForAsset(
            assetKind: CampaignMapAssetKind.SeaHarbor,
            assetName: "vicksburg-harbor",
            enemyInstanceIds: new[] { 9, 3, 5, 1, 7, 11 },
            topN: 3);
        AssertEqual("asset:SeaHarbor:vicksburg-harbor:1,3,5", sig);
    }

    private static void ThreatSignatureIsStableAcrossReorderedEnemies()
    {
        var a = DefenseThreatSignature.ForAsset(
            CampaignMapAssetKind.RiverHarbor, "memphis-harbor", new[] { 5, 3, 1 }, topN: 5);
        var b = DefenseThreatSignature.ForAsset(
            CampaignMapAssetKind.RiverHarbor, "memphis-harbor", new[] { 1, 5, 3 }, topN: 5);
        AssertEqual(a, b);
    }

    private static void ThreatSignatureForRaidHandlesNullAsset()
    {
        AssertEqual("raid:Stuart:7:<no-asset>", DefenseThreatSignature.ForRaid("Stuart:7", null));
        AssertEqual("raid:Stuart:7:<no-asset>", DefenseThreatSignature.ForRaid("Stuart:7", ""));
    }

    private static void ThreatSignatureForAssetHandlesNullName()
    {
        AssertEqual("asset:Fort:<no-asset>:1,2",
            DefenseThreatSignature.ForAsset(CampaignMapAssetKind.Fort, null, new[] { 2, 1 }, topN: 5));
        AssertEqual("asset:Fort:<no-asset>:<no-enemies>",
            DefenseThreatSignature.ForAsset(CampaignMapAssetKind.Fort, "", null, topN: 5));
        AssertEqual("asset:Fort:<no-asset>:<no-enemies>",
            DefenseThreatSignature.ForAsset(CampaignMapAssetKind.Fort, "", new int[0], topN: 5));
    }

    private static void ThreatSignatureForAssetClampsTopNAtOne()
    {
        // topN <= 0 must NOT collapse to <no-enemies>; must take at least one ID.
        var sig = DefenseThreatSignature.ForAsset(
            CampaignMapAssetKind.SeaHarbor, "norfolk-harbor", new[] { 7, 3, 5 }, topN: 0);
        AssertEqual("asset:SeaHarbor:norfolk-harbor:3", sig);

        var negativeTopN = DefenseThreatSignature.ForAsset(
            CampaignMapAssetKind.SeaHarbor, "norfolk-harbor", new[] { 7, 3, 5 }, topN: -2);
        AssertEqual("asset:SeaHarbor:norfolk-harbor:3", negativeTopN);
    }

    private static void PackageAggregatorPicksSmallerAdequateOverRemoteOversized()
    {
        var local1 = MakeDefenseCandidate(id: 1, str: 3000f, mor: 0.85f, ready: 2f, distance: 50f, tier: CandidateTier.Local);
        var local2 = MakeDefenseCandidate(id: 2, str: 3000f, mor: 0.85f, ready: 2f, distance: 60f, tier: CandidateTier.Local);
        var crossMap = MakeDefenseCandidate(id: 99, str: 20000f, mor: 0.9f, ready: 2f, distance: 800f, tier: CandidateTier.CrossMap);

        var result = DefensePackageAggregator.Select(
            candidates: new[] { local1, local2, crossMap },
            desiredStrength: 4500f,
            caution: 0.2f,
            aggression: 0f);

        AssertTrue(result.SelectedPackage.Count >= 1 && result.SelectedPackage.Count <= 2,
            "should pick 1-2 local candidates, not the cross-map army");
        AssertTrue(result.Adequate, "package should clear adequate threshold");
        AssertTrue(!result.Understrength, "should not be understrength");
        AssertTrue(result.Suppressed.Exists(s => s.UnitInstanceId == 99),
            "cross-map army should be suppressed");
        AssertTrue(!result.SelectedPackage.Exists(c => c.UnitInstanceId == 99),
            "cross-map army must not be in selected package");
    }

    private static void PackageAggregatorStopsAtOvershootGuard()
    {
        var local1 = MakeDefenseCandidate(1, 6000f, 0.9f, 2f, 50f, CandidateTier.Local);
        var local2 = MakeDefenseCandidate(2, 6000f, 0.9f, 2f, 50f, CandidateTier.Local);
        var local3 = MakeDefenseCandidate(3, 6000f, 0.9f, 2f, 50f, CandidateTier.Local);

        var result = DefensePackageAggregator.Select(
            candidates: new[] { local1, local2, local3 },
            desiredStrength: 5000f,
            caution: 0.2f, aggression: 0f);

        AssertEqual(1, result.SelectedPackage.Count);
        AssertTrue(result.Adequate, "single local should clear desired");
    }

    private static void PackageAggregatorEmitsUnderstrengthFlag()
    {
        var local1 = MakeDefenseCandidate(1, 1500f, 0.7f, 1f, 50f, CandidateTier.Local);

        var result = DefensePackageAggregator.Select(
            candidates: new[] { local1 },
            desiredStrength: 6000f,
            caution: 0.2f, aggression: 0f);

        AssertTrue(!result.Adequate, "single understrength brigade should not be adequate");
        AssertTrue(result.Understrength, "should be flagged understrength");
        AssertEqual(1, result.SelectedPackage.Count);
    }

    private static void PackageAggregatorSuppressesOvermatchReason()
    {
        var oversized = MakeDefenseCandidate(1, 30000f, 0.9f, 2f, 50f, CandidateTier.Local);
        var rightSized = MakeDefenseCandidate(2, 2500f, 0.85f, 2f, 60f, CandidateTier.Local);

        var result = DefensePackageAggregator.Select(
            candidates: new[] { oversized, rightSized },
            desiredStrength: 2000f,
            caution: 0.5f, aggression: 0f);

        AssertTrue(result.SelectedPackage.Count >= 1, "should select at least one candidate");
        AssertTrue(result.Suppressed.Exists(s => s.UnitInstanceId == 1 && s.Reason == "overmatch"),
            "oversized army should be suppressed for overmatch");
    }

    private static void PackageAggregatorDeterministicOrderOnTiedScores()
    {
        // Two candidates with identical strength/morale/readiness/distance/tier
        // produce identical scores. The tie-break on UnitInstanceId must put the
        // lower id first regardless of input enumeration order.
        var b = MakeDefenseCandidate(id: 7, str: 3000f, mor: 0.85f, ready: 2f, distance: 50f, tier: CandidateTier.Local);
        var a = MakeDefenseCandidate(id: 3, str: 3000f, mor: 0.85f, ready: 2f, distance: 50f, tier: CandidateTier.Local);

        var resultBA = DefensePackageAggregator.Select(new[] { b, a }, 4500f, 0.2f, 0f);
        var resultAB = DefensePackageAggregator.Select(new[] { a, b }, 4500f, 0.2f, 0f);

        AssertTrue(resultBA.SelectedPackage.Count == resultAB.SelectedPackage.Count,
            "package size must be input-order independent");
        for (int i = 0; i < resultBA.SelectedPackage.Count; i++)
        {
            AssertEqual(resultBA.SelectedPackage[i].UnitInstanceId,
                        resultAB.SelectedPackage[i].UnitInstanceId);
        }
        if (resultBA.SelectedPackage.Count > 0)
            AssertEqual(3, resultBA.SelectedPackage[0].UnitInstanceId);
    }

    private static DefenseCandidate MakeDefenseCandidate(int id, float str, float mor, float ready, float distance, CandidateTier tier)
    {
        return new DefenseCandidate
        {
            UnitInstanceId = id,
            ActiveStrength = str,
            Morale = mor,
            ReadinessStep = ready,
            DistanceToThreat = distance,
            Tier = tier
        };
    }

    private static void CooldownTableExtendsOnThreatRedetection()
    {
        var table = new DefenseCooldownTable();
        table.MarkActive("sif:42:Hampton:Boston", cooldownDays: 3);
        table.Tick();
        table.MarkActive("sif:42:Hampton:Boston", cooldownDays: 3);
        AssertEqual(3, table.RemainingDays("sif:42:Hampton:Boston"));
    }

    private static void CooldownTableDecrementsOncePerTick()
    {
        var table = new DefenseCooldownTable();
        table.MarkRecovered("raid:7:wilmington-harbor", cooldownDays: 4);
        AssertEqual(4, table.RemainingDays("raid:7:wilmington-harbor"));
        table.Tick();
        AssertEqual(3, table.RemainingDays("raid:7:wilmington-harbor"));
        table.Tick();
        AssertEqual(2, table.RemainingDays("raid:7:wilmington-harbor"));
    }

    private static void CooldownTableExpiresAtZero()
    {
        var table = new DefenseCooldownTable();
        table.MarkRecovered("asset:SeaHarbor:wilmington-harbor:1,2,3", cooldownDays: 1);
        table.Tick();
        AssertEqual(0, table.RemainingDays("asset:SeaHarbor:wilmington-harbor:1,2,3"));
        AssertTrue(!table.IsActive("asset:SeaHarbor:wilmington-harbor:1,2,3"),
            "expired entry should report not-active");
    }

    // -----------------------------------------------------------------------
    // DefenseIntentLedger tests (#1-#13)
    // -----------------------------------------------------------------------

    // Test #1: coastal guard response suppresses cross-map candidate.
    private static void DefenseLedgerCoastalGuardForbidsCrossMap()
    {
        var input = MakeDefenseInput(1);
        input.GuardCandidateAssets.Add(new CampaignMapAsset
        {
            Kind = CampaignMapAssetKind.SeaHarbor,
            Name = "wilmington-harbor",
            Owner = 1,
            StrategicRole = AssetStrategicRole.BlockadeRunnerPort
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 99,
            ActiveStrength = 18000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.CrossMap,
            DistanceToThreat = 1200f
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected at least one response");
        var guardResponse = output.Responses.Find(r =>
            r.Threat != null && r.Threat.Posture == DefensePosture.CoastalGuard);
        AssertTrue(guardResponse != null, "expected a CoastalGuard response");
        AssertEqual(0, guardResponse.SelectedPackage.Count);
        var suppressed99 = guardResponse.Suppressed.Find(s => s.UnitInstanceId == 99);
        AssertTrue(suppressed99 != null, "expected id=99 to be suppressed");
        AssertEqual("forbidden-cross-map", suppressed99.Reason);
    }

    // Test #2: minor raid response suppresses cross-map candidate.
    private static void DefenseLedgerMinorRaidForbidsCrossMap()
    {
        var input = MakeDefenseInput(1);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.RaidForce,
            EnemyUnitKey = "Raider:7",
            AssetName = "hampton-harbor",
            EnemyStrength = 1500f
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 99,
            ActiveStrength = 20000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.CrossMap,
            DistanceToThreat = 1200f
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected at least one response");
        var raidResponse = output.Responses[0];
        AssertEqual(DefensePosture.ActiveInvasion, raidResponse.Threat.Posture);
        AssertEqual(ThreatScale.Raid, raidResponse.Threat.Scale);
        var suppressed99 = raidResponse.Suppressed.Find(s => s.UnitInstanceId == 99);
        AssertTrue(suppressed99 != null, "expected id=99 to be suppressed in raid response");
        AssertEqual("forbidden-cross-map", suppressed99.Reason);
    }

    // Test #3: decisive landing allows cross-theater candidates.
    private static void DefenseLedgerDecisiveLandingAllowsCrossTheater()
    {
        var input = MakeDefenseInput(1);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            EnemyUnitKey = "hampton-spot:42",
            LandedSignal = true,
            AssetName = "norfolk-harbor",
            AssetRole = AssetStrategicRole.KeyFort,
            EnemyStrength = 12000f
        });
        // Same-theater understrength division
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            ActiveStrength = 4000f,
            Morale = 0.8f,
            ReadinessStep = 2f,
            Tier = CandidateTier.SameTheater,
            DistanceToThreat = 80f
        });
        // Adjacent-theater strong army
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 2,
            ActiveStrength = 15000f,
            Morale = 0.85f,
            ReadinessStep = 2f,
            Tier = CandidateTier.AdjacentTheater,
            DistanceToThreat = 300f
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected at least one response");
        var response = output.Responses[0];
        AssertEqual(DefensePosture.ActiveInvasion, response.Threat.Posture);
        AssertEqual(ThreatScale.DecisiveLanding, response.Threat.Scale);

        // Adjacent-theater army should be in the selected package.
        bool hasAdjacentArmy = response.SelectedPackage.Find(c => c.UnitInstanceId == 2) != null;
        AssertTrue(hasAdjacentArmy, "expected adjacent-theater army (id=2) in SelectedPackage");

        // Escalation reason or adjacent-theater candidate present.
        bool hasEscalation = response.Threat.EscalationReason != null &&
                             response.Threat.EscalationReason.Contains("cross-theater");
        AssertTrue(hasEscalation, "expected EscalationReason to contain 'cross-theater'");
    }

    // Test #4: same-theater adequate package beats remote oversized cross-map.
    private static void DefenseLedgerSameTheaterAdequateBeatsRemoteOversized()
    {
        var input = MakeDefenseInput(1);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            EnemyUnitKey = "charleston-spot:5",
            LandedSignal = true,
            AssetName = "wilmington-harbor",
            AssetRole = AssetStrategicRole.BlockadeRunnerPort,
            EnemyStrength = 4000f
        });
        // Two same-theater brigades (combined EffectiveStrength ~5400 > desired 6000*0.75=4500)
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            ActiveStrength = 3000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.SameTheater,
            DistanceToThreat = 60f
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 2,
            ActiveStrength = 3000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.SameTheater,
            DistanceToThreat = 70f
        });
        // Cross-map oversized army (should be suppressed: Landing scale → forbidden-cross-map or overmatch)
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 99,
            ActiveStrength = 20000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.CrossMap,
            DistanceToThreat = 1200f
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected at least one response");
        var response = output.Responses[0];

        // Same-theater brigades should be selected.
        bool has1 = response.SelectedPackage.Find(c => c.UnitInstanceId == 1) != null;
        bool has2 = response.SelectedPackage.Find(c => c.UnitInstanceId == 2) != null;
        AssertTrue(has1, "expected brigade 1 in SelectedPackage");
        AssertTrue(has2, "expected brigade 2 in SelectedPackage");

        // Cross-map unit should be suppressed with forbidden-cross-map or overmatch.
        var sup99 = response.Suppressed.Find(s => s.UnitInstanceId == 99);
        AssertTrue(sup99 != null, "expected id=99 to be suppressed");
        AssertTrue(sup99.Reason == "forbidden-cross-map" ||
                   sup99.Reason == "overmatch" ||
                   sup99.Reason == "worse-tier" ||
                   sup99.Reason == "national-emergency-required",
            $"expected reason forbidden-cross-map, overmatch, worse-tier, or national-emergency-required, got {sup99.Reason}");
    }

    // Test #5: guard budget caps low-value ports.
    // NOTE: Candidates are consumed globally; once a candidate is assigned to the first
    // guard response it is not available for subsequent ones. With 6 assets each
    // wanting one local unit at ~2250 effective strength and a budget of 6000, at most
    // floor(6000/2250)=2 units can be assigned before the budget is exhausted.
    private static void DefenseLedgerGuardBudgetCapsLowValuePorts()
    {
        var input = MakeDefenseInput(1);
        input.TotalAllianceEffectiveStrength = 60000f;
        input.GuardBudgetFraction = 0.10f; // budget = 6000

        for (int i = 0; i < 6; i++)
        {
            input.GuardCandidateAssets.Add(new CampaignMapAsset
            {
                Kind = CampaignMapAssetKind.SeaHarbor,
                Name = $"rear-port-{i}",
                Owner = 1,
                StrategicRole = AssetStrategicRole.RearSafePort
            });
            input.Candidates.Add(new DefenseCandidate
            {
                UnitInstanceId = 10 + i,
                ActiveStrength = 2500f,
                Morale = 0.9f,
                ReadinessStep = 2f,
                Tier = CandidateTier.Local,
                DistanceToThreat = 50f
            });
        }

        var output = DefenseIntentLedger.Build(input);

        // Count cap-reached responses.
        int capReachedCount = 0;
        int assignedCount = 0;
        foreach (var r in output.Responses)
        {
            if (r.Threat.Posture != DefensePosture.CoastalGuard) continue;
            if (r.SelectedPackage.Count > 0)
                assignedCount++;
            else
            {
                bool hasCap = r.Suppressed.Find(s => s.Reason == "cap-reached") != null;
                if (hasCap) capReachedCount++;
            }
        }

        // EffectiveStrength per unit ≈ 2500 * 0.9 * 1.0 = 2250; budget = 6000
        // At most floor(6000/2250) = 2 can be assigned; at least 4 must be cap-reached.
        AssertTrue(assignedCount <= 2, $"expected at most 2 assigned packages, got {assignedCount}");
        AssertTrue(capReachedCount >= 4, $"expected at least 4 cap-reached responses, got {capReachedCount}");
    }

    // Test #6: active invasion persists through a favorable tick (strength drop doesn't flip to Recovered).
    private static void DefenseLedgerActiveInvasionPersistsThroughFavorableTick()
    {
        var input = MakeDefenseInput(1);
        var src = new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            EnemyUnitKey = "mobile-spot:10",
            LandedSignal = true,
            AssetName = "mobile-harbor",
            AssetRole = AssetStrategicRole.BlockadeRunnerPort,
            EnemyStrength = 5000f
        };
        input.Threats.Add(src);
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            ActiveStrength = 8000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            DistanceToThreat = 40f
        });

        // Tick 1: first build.
        var out1 = DefenseIntentLedger.Build(input);
        AssertTrue(out1.Responses.Count >= 1, "tick1: expected at least one response");
        AssertEqual(DefensePosture.ActiveInvasion, out1.Responses[0].Threat.Posture);
        string sig = out1.Responses[0].Threat.Signature;
        AssertTrue(input.Cooldown.IsActive(sig), "tick1: cooldown should be active after build");

        // Tick 2: slight strength drop — still not collapsed (LandedSignal=true, not Recovered).
        src.EnemyStrength = 4500f;
        var out2 = DefenseIntentLedger.Build(input);
        AssertTrue(out2.Responses.Count >= 1, "tick2: expected at least one response");
        AssertEqual(DefensePosture.ActiveInvasion, out2.Responses[0].Threat.Posture);
        // Cooldown counter should still be active (not counting down yet since we re-mark each Active tick).
        AssertTrue(input.Cooldown.IsActive(sig), "tick2: cooldown should still be active");
    }

    // Test #7: recovered threat releases after cooldown expires.
    // MarkRecovered is idempotent per Active cycle: the first call sets the
    // counter, subsequent calls while flagged are no-ops, MarkActive resets
    // the flag. The test runs MarkActive then a sequence of MarkRecovered+Tick
    // pairs and asserts the counter ticks down once per Tick, not once per call.
    private static void DefenseLedgerRecoveredThreatReleasesAfterCooldown()
    {
        var input = MakeDefenseInput(1);
        input.CooldownDays = 2;

        var src = new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            EnemyUnitKey = "savannah-spot:20",
            LandedSignal = true,
            SightingLapsed = false,
            AssetName = "savannah-harbor",
            AssetRole = AssetStrategicRole.BlockadeRunnerPort,
            EnemyStrength = 5000f
        };
        input.Threats.Add(src);
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            ActiveStrength = 8000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            DistanceToThreat = 30f
        });

        // Tick 1: Active.
        var out1 = DefenseIntentLedger.Build(input);
        AssertEqual(DefensePosture.ActiveInvasion, out1.Responses[0].Threat.Posture);
        string sig = out1.Responses[0].Threat.Signature;
        AssertTrue(input.Cooldown.IsActive(sig), "tick1: should be active after Active build");
        input.Cooldown.Tick();

        // Tick 2: Recovered (SightingLapsed=true). Builder emits Recovered + calls MarkRecovered.
        src.SightingLapsed = true;
        src.LandedSignal = false;
        var out2 = DefenseIntentLedger.Build(input);
        AssertTrue(out2.Responses.Count >= 1, "tick2: expected response");
        AssertEqual(DefensePosture.Recovered, out2.Responses[0].Threat.Posture);
        // After MarkRecovered(sig, 2), IsActive should be true (r=2).
        AssertTrue(input.Cooldown.IsActive(sig), "tick2: IsActive should be true after MarkRecovered");
        input.Cooldown.Tick(); // r: 2→1

        // Tick 3: Recovered again. Cooldown still active (r=1). Builder should NOT re-mark
        // (so it stays at 1). Assert IsActive=true.
        var out3 = DefenseIntentLedger.Build(input);
        AssertEqual(DefensePosture.Recovered, out3.Responses[0].Threat.Posture);
        AssertTrue(input.Cooldown.IsActive(sig), "tick3: IsActive should still be true");
        input.Cooldown.Tick(); // r: 1→0

        // Tick 4: Recovered, cooldown expired (r=0). Builder emits empty response.
        var out4 = DefenseIntentLedger.Build(input);
        // Either the response is empty (no IsActive) or it's not included.
        AssertTrue(!input.Cooldown.IsActive(sig), "tick4: IsActive should be false (expired)");
        // The builder emits an empty response when cooldown is expired.
        var r4 = out4.Responses.Find(r => r.Threat != null && r.Threat.Signature == sig);
        bool emptyOrAbsent = r4 == null ||
                             (r4.SelectedPackage.Count == 0 && !r4.Adequate);
        AssertTrue(emptyOrAbsent, "tick4: response should be absent or empty when cooldown expired");
    }

    // Test #8: player CIC short-circuits the entire ledger.
    private static void DefenseLedgerPlayerCicShortCircuitsAlliance()
    {
        var input = MakeDefenseInput(0);
        input.PlayerIsCIC = true;
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            EnemyUnitKey = "annapolis-spot:1",
            LandedSignal = true,
            EnemyStrength = 8000f
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            ActiveStrength = 10000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            DistanceToThreat = 50f
        });

        var output = DefenseIntentLedger.Build(input);

        AssertEqual(0, output.Responses.Count);
    }

    // Test #9: player-controlled candidate is suppressed; AI candidate is selected.
    private static void DefenseLedgerWlSubordinateProtectsOnlyMarkedUnit()
    {
        var input = MakeDefenseInput(1);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            EnemyUnitKey = "beaufort-spot:30",
            LandedSignal = true,
            AssetName = "beaufort-harbor",
            AssetRole = AssetStrategicRole.BlockadeRunnerPort,
            EnemyStrength = 6000f
        });
        // Player-controlled candidate — must be suppressed.
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            ActiveStrength = 8000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            DistanceToThreat = 40f,
            PlayerControlled = true
        });
        // AI candidate — should be selected.
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 2,
            ActiveStrength = 8000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            DistanceToThreat = 50f,
            PlayerControlled = false
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected at least one response");
        var response = output.Responses[0];
        AssertEqual(DefensePosture.ActiveInvasion, response.Threat.Posture);

        var sup1 = response.Suppressed.Find(s => s.UnitInstanceId == 1);
        AssertTrue(sup1 != null, "expected id=1 (player) to be suppressed");
        AssertEqual("player-controlled", sup1.Reason);

        bool hasId2 = response.SelectedPackage.Find(c => c.UnitInstanceId == 2) != null;
        AssertTrue(hasId2, "expected AI candidate (id=2) in SelectedPackage");
    }

    // Test #10: critical-front candidate rejected unless decisive (two sub-fixtures).
    private static void DefenseLedgerCriticalFrontCandidateRejectedUnlessDecisive()
    {
        // Sub-fixture A: Landing scale (not decisive) — critical-front suppressed, package empty.
        {
            var input = MakeDefenseInput(1);
            input.Threats.Add(new DefenseThreatSource
            {
                Kind = DefenseThreatSourceKind.SeaInvasion,
                EnemyUnitKey = "port-royal-spot:50",
                LandedSignal = true,
                AssetName = "port-royal-harbor",
                AssetRole = AssetStrategicRole.BlockadeRunnerPort,
                EnemyStrength = 4000f // Landing scale
            });
            input.Candidates.Add(new DefenseCandidate
            {
                UnitInstanceId = 1,
                ActiveStrength = 6000f,
                Morale = 0.9f,
                ReadinessStep = 2f,
                Tier = CandidateTier.SameTheater,
                DistanceToThreat = 100f,
                CriticalFront = true
            });

            var output = DefenseIntentLedger.Build(input);

            AssertTrue(output.Responses.Count >= 1, "fixture-A: expected response");
            var response = output.Responses[0];
            var sup1 = response.Suppressed.Find(s => s.UnitInstanceId == 1);
            AssertTrue(sup1 != null, "fixture-A: expected id=1 (critical-front) suppressed");
            AssertEqual("critical-front", sup1.Reason);
            AssertEqual(0, response.SelectedPackage.Count);
        }

        // Sub-fixture B: DecisiveLanding scale, critical-front is the only candidate — should be selected.
        {
            var input = MakeDefenseInput(1);
            input.Threats.Add(new DefenseThreatSource
            {
                Kind = DefenseThreatSourceKind.SeaInvasion,
                EnemyUnitKey = "richmond-spot:51",
                LandedSignal = true,
                AssetName = "richmond-harbor",
                AssetRole = AssetStrategicRole.CapitalApproach,
                EnemyStrength = 12000f // DecisiveLanding scale
            });
            input.Candidates.Add(new DefenseCandidate
            {
                UnitInstanceId = 1,
                ActiveStrength = 6000f,
                Morale = 0.9f,
                ReadinessStep = 2f,
                Tier = CandidateTier.SameTheater,
                DistanceToThreat = 100f,
                CriticalFront = true
            });

            var output = DefenseIntentLedger.Build(input);

            AssertTrue(output.Responses.Count >= 1, "fixture-B: expected response");
            var response = output.Responses[0];
            bool hasId1 = response.SelectedPackage.Find(c => c.UnitInstanceId == 1) != null;
            AssertTrue(hasId1, "fixture-B: expected critical-front candidate (id=1) in SelectedPackage");
            var sup1 = response.Suppressed.Find(s => s.UnitInstanceId == 1);
            AssertTrue(sup1 == null, "fixture-B: critical-front candidate should NOT be in Suppressed");
        }
    }

    // Test #11: river harbor detected via AssetProximity (no SeaInvasionForce).
    private static void DefenseLedgerRiverHarborDetectsWithoutSif()
    {
        var input = MakeDefenseInput(0);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.AssetProximity,
            AssetKind = CampaignMapAssetKind.RiverHarbor,
            AssetName = "vicksburg-harbor",
            EnemyStrength = 4000f,
            EnemyInstanceIds = new[] { 1, 2, 3 }
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected at least one response");
        var response = output.Responses[0];
        AssertEqual(DefensePosture.ActiveInvasion, response.Threat.Posture);
        AssertTrue(response.Threat.Signature.StartsWith("asset:RiverHarbor:"),
            $"expected signature to start with 'asset:RiverHarbor:', got '{response.Threat.Signature}'");
    }

    // Test #12: RaidForce source produces Raid scale and correct signature prefix.
    private static void DefenseLedgerRaidforceCoverage()
    {
        var input = MakeDefenseInput(1);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.RaidForce,
            EnemyUnitKey = "Raider:7",
            AssetName = "hampton-spot",
            EnemyStrength = 2000f
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected at least one response");
        var response = output.Responses[0];
        AssertEqual(ThreatScale.Raid, response.Threat.Scale);
        AssertTrue(response.Threat.Signature.StartsWith("raid:Raider:7:"),
            $"expected signature to start with 'raid:Raider:7:', got '{response.Threat.Signature}'");
    }

    // Test #13: when SeaInvasion gate is off, AssetProximity fallback works without throw.
    private static void DefenseLedgerDebugSeainvasionsactiveOffFallsBack()
    {
        var input = MakeDefenseInput(1);
        // No SeaInvasion source — runtime fell back to AssetProximity.
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.AssetProximity,
            AssetKind = CampaignMapAssetKind.SeaHarbor,
            AssetName = "norfolk-harbor",
            EnemyStrength = 4000f,
            EnemyInstanceIds = new[] { 5, 6 }
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            ActiveStrength = 6000f,
            Morale = 0.85f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            DistanceToThreat = 50f
        });

        DefenseIntentLedgerOutput output = null;
        Exception caught = null;
        try { output = DefenseIntentLedger.Build(input); }
        catch (Exception ex) { caught = ex; }

        AssertTrue(caught == null, $"expected no exception, got: {caught}");
        AssertTrue(output != null && output.Responses.Count >= 1, "expected at least one response");
        AssertEqual(DefensePosture.ActiveInvasion, output.Responses[0].Threat.Posture);
    }

    // Test: every emitted DefenseResponse has a non-empty TelemetrySignature
    // that starts with the posture name, so Task 14 telemetry has a stable key.
    private static void DefenseLedgerTelemetrySignaturePopulated()
    {
        var input = MakeDefenseInput(1);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            EnemyUnitKey = "charlestown-spot:99",
            LandedSignal = true,
            AssetName = "charlestown-harbor",
            AssetRole = AssetStrategicRole.BlockadeRunnerPort,
            EnemyStrength = 6000f
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            ActiveStrength = 9000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            DistanceToThreat = 40f
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected at least one response");
        foreach (var r in output.Responses)
        {
            AssertTrue(!string.IsNullOrEmpty(r.TelemetrySignature),
                $"TelemetrySignature must be non-empty for sig={r.Threat?.Signature}");
            string expectedPrefix = r.Threat.Posture.ToString();
            AssertTrue(r.TelemetrySignature.StartsWith(expectedPrefix),
                $"TelemetrySignature '{r.TelemetrySignature}' must start with posture '{expectedPrefix}'");
        }
    }

    private static void DefenseTelemetrySummaryCompressesResponseBurst()
    {
        var output = new DefenseIntentLedgerOutput
        {
            Signature = "sig-a"
        };
        output.Responses.Add(new DefenseResponse
        {
            Threat = new DefenseThreat { Posture = DefensePosture.ActiveInvasion },
            SelectedPackage = { new DefenseCandidate { UnitInstanceId = 1 } }
        });
        output.Responses.Add(new DefenseResponse
        {
            Threat = new DefenseThreat { Posture = DefensePosture.CoastalGuard },
            Suppressed = { new DefenseSuppression { UnitInstanceId = 2, Reason = "forbidden-cross-map" } }
        });

        string summary = DefenseIntentTelemetry.Summary(output);

        AssertEqual("responses=2 active=1 guard=1 selected=1 suppressed=1 signature=sig-a", summary);
    }

    // Test: pure builder must accept allianceId=2 (Europe) without throwing.
    // Vanilla AICampaign.aifaction includes alliance 2 for some CSA-side
    // scenarios (decompile lines 9226/9249/9271/9367). The runtime arrays on
    // StrategicCoordinator are length 2 — the bound-check that short-circuits
    // alliance 2+ lives at the coordinator / patch layer. The builder itself
    // is alliance-agnostic and must not throw on any non-negative ID.
    private static void DefenseLedgerDoesNotCrashOnEuropeAllianceIndex()
    {
        var input = MakeDefenseInput(allianceId: 2);
        DefenseIntentLedgerOutput output = null;
        Exception caught = null;
        try { output = DefenseIntentLedger.Build(input); }
        catch (Exception ex) { caught = ex; }

        AssertTrue(caught == null, $"expected no exception for allianceId=2, got: {caught}");
        AssertTrue(output != null, "Build must return a non-null output for allianceId=2");
        AssertTrue(output.Responses != null,
            "Responses list must not be null even for europe alliance");
    }

    private static void DefenseLedgerAssetProximityStaysLocalAndCannotCustomOrder()
    {
        var input = MakeDefenseInput(1);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.AssetProximity,
            AssetKind = CampaignMapAssetKind.SeaHarbor,
            AssetName = "annapolis-port",
            EnemyStrength = 9000f,
            EnemyInstanceIds = new[] { 1, 2, 3 },
            X = 10f,
            Z = 10f
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            ActiveStrength = 4000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            DistanceToThreat = 20f
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 2,
            ActiveStrength = 25000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.AdjacentTheater,
            TransferDonorAllowed = true,
            DirectMovementAllowed = true,
            DefensiveAllowed = true,
            DistanceToThreat = 400f
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected asset-proximity response");
        var response = output.Responses[0];
        AssertEqual(DefenseThreatSourceKind.AssetProximity, response.Threat.SourceKind);
        AssertTrue(response.SelectedPackage.Find(c => c.UnitInstanceId == 1) != null,
            "local garrison should remain eligible");
        AssertTrue(response.SelectedPackage.Find(c => c.UnitInstanceId == 2) == null,
            "asset proximity must not pull adjacent theater army");
        var suppressed = response.Suppressed.Find(s => s.UnitInstanceId == 2);
        AssertTrue(suppressed != null, "adjacent theater army should be suppressed");
        AssertEqual("asset-proximity-local-only", suppressed.Reason);
        AssertTrue(!DefenseCustomOrderPolicy.RequiresCustomOrder(response),
            "asset proximity must never issue custom MoveUnitTo orders");
    }

    private static void DefenseLedgerDonorTheaterBudgetBlocksCriticalFrontExport()
    {
        var input = MakeDefenseInput(1);
        input.FrontLedger = FrontSectorLedger.Build(new[]
        {
            new FrontSectorInput
            {
                SectorKey = "ThreatCoast",
                Theater = Theater.Coast,
                OwnStrength = 3000f,
                EnemyStrength = 9000f,
                StrategicImportance = 0.7f,
                IsCritical = false
            },
            new FrontSectorInput
            {
                SectorKey = "VirginiaCapitalCorridor",
                Theater = Theater.East,
                OwnStrength = 16000f,
                EnemyStrength = 13000f,
                StrategicImportance = 1.0f,
                IsCritical = true,
                AverageMorale = 0.8f,
                AverageReadiness = 0.8f,
                AverageSupply = 0.8f
            }
        }, new FrontLedgerOptions { MinimumHoldRatio = 0.9f, CriticalHoldRatioBonus = 0.25f });
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            EnemyUnitKey = "norfolk-spot:44",
            LandedSignal = true,
            AssetName = "richmond-approach",
            AssetRole = AssetStrategicRole.CapitalApproach,
            EnemyStrength = 12000f,
            X = 150f,
            Z = 200f
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            ActiveStrength = 9000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            SectorKey = "VirginiaCapitalCorridor",
            Tier = CandidateTier.AdjacentTheater,
            TransferDonorAllowed = true,
            DirectMovementAllowed = true,
            DefensiveAllowed = true
        });

        var output = DefenseIntentLedger.Build(input);
        var response = output.Responses[0];

        AssertTrue(response.SelectedPackage.Find(c => c.UnitInstanceId == 1) == null,
            "critical front army must not be exported below hold ratio");
        var suppressed = response.Suppressed.Find(s => s.UnitInstanceId == 1);
        AssertTrue(suppressed != null, "expected donor army to be suppressed");
        AssertTrue(suppressed.Reason == "min-hold" || suppressed.Reason == "critical-sector-budget",
            $"expected min-hold or critical-sector-budget, got {suppressed.Reason}");
    }

    private static void DefenseLedgerFormationDirectiveBlocksDefenseMovement()
    {
        var input = MakeDefenseInput(1);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            EnemyUnitKey = "mobile-spot:55",
            LandedSignal = true,
            AssetName = "mobile",
            AssetRole = AssetStrategicRole.BlockadeRunnerPort,
            EnemyStrength = 4000f
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            ActiveStrength = 9000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            HasFormationDirective = true,
            DefensiveAllowed = false,
            DirectMovementAllowed = true,
            TransferDonorAllowed = true
        });

        var output = DefenseIntentLedger.Build(input);
        var response = output.Responses[0];

        AssertEqual(0, response.SelectedPackage.Count);
        var suppressed = response.Suppressed.Find(s => s.UnitInstanceId == 1);
        AssertTrue(suppressed != null, "expected directive-blocked unit to be suppressed");
        AssertEqual("formation-directive", suppressed.Reason);
    }

    private static void DefenseLedgerCapitalDefensePackageIsCapped()
    {
        var input = MakeDefenseInput(1);
        input.TotalAllianceEffectiveStrength = 60000f;
        input.CapitalDefenseBudgetFraction = 0.18f;
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            EnemyUnitKey = "richmond-spot:66",
            LandedSignal = true,
            AssetName = "richmond",
            AssetRole = AssetStrategicRole.CapitalApproach,
            EnemyStrength = 20000f
        });
        for (int i = 0; i < 5; i++)
        {
            input.Candidates.Add(new DefenseCandidate
            {
                UnitInstanceId = 10 + i,
                ActiveStrength = 7000f,
                Morale = 1f,
                ReadinessStep = 2f,
                Tier = CandidateTier.SameTheater,
                TransferDonorAllowed = true,
                DirectMovementAllowed = true,
                DefensiveAllowed = true
            });
        }

        var output = DefenseIntentLedger.Build(input);
        var response = output.Responses[0];

        float selected = 0f;
        foreach (var candidate in response.SelectedPackage)
            selected += candidate.EffectiveStrength;

        AssertTrue(selected <= 10800f, $"capital package should be capped at 10800 effective strength, got {selected}");
        AssertTrue(response.Suppressed.Find(s => s.Reason == "capital-defense-cap") != null,
            "expected extra capital candidates to be suppressed by cap");
    }

    // -----------------------------------------------------------------------
    // Helper: build a minimal DefenseIntentInput.
    // -----------------------------------------------------------------------

    private static DefenseIntentInput MakeDefenseInput(int allianceId)
    {
        return new DefenseIntentInput
        {
            AllianceId = allianceId,
            PlayerIsCIC = false,
            CICPersonality = default(PersonalityVector),
            TotalAllianceEffectiveStrength = 60000f
        };
    }
}
