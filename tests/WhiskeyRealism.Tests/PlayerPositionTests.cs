using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using static TestFixtures;
using static TestHarness;

// The W&L map-group player counts by position (integrator decision 2026-10-15, docs/wl-player-model.md
// rule 7): his group covers or holds the area where its last waypoint lies at strength x morale, as
// vanilla's capital need counts it (AICampaign.AssignUnitToDefendCapital 11733-11740), but is never
// pledged, assigned or packaged; marching away shows as a gap at the next review.
internal static class PlayerPositionTests
{
    private const float ThreatX = 1000f, ThreatZ = 1000f, Radius = 250f;
    private const int PlayerId = 1, AiId = 2;

    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("player position: chain rules split may-commit from counts-toward-coverage", ChainRulesSplitTheTwoMeanings),
            ("player position: aggregator held strength shrinks the package, full cover sends none", AggregatorCountsHeldStrength),
            ("player position: corps standing at a threatened asset covers it, no AI package is sent", CorpsAtThreatCoversIt),
            ("player position: held strength is strength x morale", HeldStrengthIsStrengthTimesMorale),
            ("player position: corps marching away leaves the asset uncovered next review", CorpsMarchingAwayLeavesGap),
            ("player position: partial cover shrinks the AI package", PartialCoverShrinksAiPackage),
            ("player position: one corps covers one threat once", CorpsCoversOneThreatOnce),
            ("player position: corps standing at an unguarded asset covers its guard slot", CorpsAtGuardAssetCoversIt),
            ("player position: no cover radius means no position cover", NoRadiusNoCover),
            ("player position: capital need counts the player's group, yet it is never pre-pledged", CapitalNeedCountsPlayerNeverPledged),
            ("player position: runtime wiring uses the right meaning per caller", RuntimeWiring),
        });
    }

    private static void ChainRulesSplitTheTwoMeanings()
    {
        // (a) may be committed / pledged / assigned / moved.
        AssertTrue(WlChainRules.MayMove(WlChainClass.None), "AI unit");
        AssertTrue(WlChainRules.MayMove(WlChainClass.CarryingGroup), "AI group carrying a subordinate player");
        AssertFalse(WlChainRules.MayMove(WlChainClass.PlayerMapGroup), "map-group player is never pledged");
        AssertFalse(WlChainRules.MayMove(WlChainClass.PlayerCommand), "own command");
        AssertFalse(WlChainRules.MayMove(WlChainClass.CicSide), "CIC side");

        // (b) counts toward existing coverage by position.
        AssertTrue(WlChainRules.CountsTowardCoverage(WlChainClass.None), "AI unit");
        AssertTrue(WlChainRules.CountsTowardCoverage(WlChainClass.CarryingGroup), "carrying group");
        AssertTrue(WlChainRules.CountsTowardCoverage(WlChainClass.PlayerMapGroup), "map-group player holds where he is");
        AssertFalse(WlChainRules.CountsTowardCoverage(WlChainClass.PlayerCommand), "own command is inside a group that counts");
        AssertFalse(WlChainRules.CountsTowardCoverage(WlChainClass.CicSide), "CIC side: Whiskey stands down");
    }

    private static void AggregatorCountsHeldStrength()
    {
        var pool = new List<DefenseCandidate> { Ai(AiId, 5000f), Ai(3, 5000f) };

        var none = DefensePackageAggregator.Select(pool, 10000f, 0f, 0f);
        AssertEqual(2, none.SelectedPackage.Count, "no held strength: both needed");

        var some = DefensePackageAggregator.Select(pool, 10000f, 0f, 0f, heldStrength: 6000f);
        AssertEqual(1, some.SelectedPackage.Count, "6000 held leaves one unit to send");
        AssertNear(5000f, some.CumulativeEffective, 0.01f, "CumulativeEffective is the package alone");
        AssertTrue(some.Adequate, "held plus package covers the target");

        var full = DefensePackageAggregator.Select(pool, 10000f, 0f, 0f, heldStrength: 7500f);
        AssertEqual(0, full.SelectedPackage.Count, "held at the adequate ratio sends nothing");
        AssertTrue(full.Adequate, "adequate");
        AssertFalse(full.Understrength, "not understrength");
        AssertEqual(0, full.Suppressed.Count, "no candidate is turned away: none was needed");

        var short7 = DefensePackageAggregator.Select(pool, 10000f, 0f, 0f, heldStrength: 7499f);
        AssertEqual(1, short7.SelectedPackage.Count, "just under the adequate ratio still sends help");
    }

    private static void CorpsAtThreatCoversIt()
    {
        var input = ThreatInput(Player(ThreatX, ThreatZ), Ai(AiId, 8000f));
        var response = Only(DefenseIntentLedger.Build(input));
        AssertTrue(response.Adequate, "player's corps at the asset covers it");
        AssertFalse(response.Understrength, "not understrength");
        AssertEqual(0, response.SelectedPackage.Count, "no AI package is sent");
        AssertNear(20000f, response.HeldStrength, 0.01f, "held strength recorded");
        AssertFalse(response.SelectedPackage.Exists(c => c.UnitInstanceId == PlayerId), "the player is never a package member");
    }

    private static void HeldStrengthIsStrengthTimesMorale()
    {
        var corps = Player(ThreatX, ThreatZ, strength: 20000f);
        corps.Morale = 0.8f;
        var response = Only(DefenseIntentLedger.Build(ThreatInput(corps, Ai(AiId, 8000f))));
        AssertNear(16000f, response.HeldStrength, 0.01f, "strength x morale, as vanilla subtracts it");
    }

    private static void CorpsMarchingAwayLeavesGap()
    {
        // Same corps, its last waypoint now far from the asset.
        var input = ThreatInput(Player(ThreatX + 3000f, ThreatZ), Ai(AiId, 8000f));
        var response = Only(DefenseIntentLedger.Build(input));
        AssertNear(0f, response.HeldStrength, 0.01f, "nothing held once the waypoint leaves");
        AssertTrue(response.SelectedPackage.Exists(c => c.UnitInstanceId == AiId), "the next review sends the AI unit");
        AssertFalse(response.SelectedPackage.Exists(c => c.UnitInstanceId == PlayerId), "still never the player");
    }

    private static void PartialCoverShrinksAiPackage()
    {
        // Landing of 6000 -> desired 9000. A 6000 corps holds two thirds (short of adequate), so the AI sends fewer units.
        var weakCorps = Player(ThreatX, ThreatZ, strength: 6000f);
        var input = ThreatInput(weakCorps, Ai(AiId, 5000f), Ai(3, 5000f));
        var response = Only(DefenseIntentLedger.Build(input));
        AssertNear(6000f, response.HeldStrength, 0.01f, "held");

        var awayInput = ThreatInput(Player(ThreatX + 3000f, ThreatZ, strength: 6000f), Ai(AiId, 5000f), Ai(3, 5000f));
        var away = Only(DefenseIntentLedger.Build(awayInput));
        AssertEqual(1, response.SelectedPackage.Count, "one AI unit finishes the cover");
        AssertEqual(2, away.SelectedPackage.Count, "corps away: both AI units");
        AssertTrue(response.SelectedPackage.Count < away.SelectedPackage.Count,
            "holding corps means fewer AI units sent: " + response.SelectedPackage.Count + " vs " + away.SelectedPackage.Count);
    }

    private static void CorpsCoversOneThreatOnce()
    {
        // Two threats at the same spot (distinct signatures): the corps is credited to the first only.
        var input = new DefenseIntentInput { AllianceId = 1, TotalAllianceEffectiveStrength = 60000f, HoldCoverRadius = Radius };
        input.Threats.Add(Threat(30));
        input.Threats.Add(Threat(31));
        input.Candidates.Add(Player(ThreatX, ThreatZ));
        input.Candidates.Add(Ai(AiId, 8000f));
        var output = DefenseIntentLedger.Build(input);
        AssertEqual(2, output.Responses.Count, "two responses");
        int credited = 0;
        foreach (var r in output.Responses) if (r.HeldStrength > 0f) credited++;
        AssertEqual(1, credited, "one corps, one credit");
    }

    private static void CorpsAtGuardAssetCoversIt()
    {
        var holding = GuardInput(Player(ThreatX, ThreatZ), Ai(AiId, 2500f));
        var covered = Only(DefenseIntentLedger.Build(holding));
        AssertTrue(covered.Adequate, "guard slot covered by the corps standing there");
        AssertEqual(0, covered.SelectedPackage.Count, "no AI guard sent");

        var away = GuardInput(Player(ThreatX + 3000f, ThreatZ), Ai(AiId, 2500f));
        var uncovered = Only(DefenseIntentLedger.Build(away));
        AssertTrue(uncovered.SelectedPackage.Exists(c => c.UnitInstanceId == AiId), "corps away: AI guard sent");
        AssertFalse(uncovered.SelectedPackage.Exists(c => c.UnitInstanceId == PlayerId), "never the player");
    }

    private static void NoRadiusNoCover()
    {
        var input = ThreatInput(Player(ThreatX, ThreatZ), Ai(AiId, 8000f));
        input.HoldCoverRadius = 0f;
        var response = Only(DefenseIntentLedger.Build(input));
        AssertNear(0f, response.HeldStrength, 0.01f, "a zero radius covers nothing");
        AssertTrue(response.SelectedPackage.Exists(c => c.UnitInstanceId == AiId), "AI unit sent");
    }

    private static void CapitalNeedCountsPlayerNeverPledged()
    {
        // Vanilla's capital need subtracts every own group whose last waypoint is nearer the
        // capital than the nearest enemy, the player's included (11733-11740); #4 passes the
        // unfiltered own list, so the player's group standing at the capital already satisfies it.
        var enemies = new[] { new CapitalForce(400f, 9000f, 1f) };
        var withoutPlayer = new[] { new CapitalForce(300f, 500f, 1f) };
        var withPlayer = new[] { new CapitalForce(300f, 500f, 1f), new CapitalForce(5f, 20000f, 1f) };

        float needWithout = CampaignMilitaryGates.VanillaCapitalDefenseNeed(500f, 650f, enemies, withoutPlayer);
        float needWith = CampaignMilitaryGates.VanillaCapitalDefenseNeed(500f, 650f, enemies, withPlayer);
        AssertTrue(needWithout > 0f, "AI alone leaves the capital short: " + needWithout);
        AssertTrue(needWith <= 0f, "the player's group at the capital covers it: " + needWith);
        AssertFalse(CampaignMilitaryGates.ShouldPrePledgeCapitalDefender(needWith, needWith), "no pledge when the player covers");

        // And when it does not cover, the pick still never takes him.
        AssertFalse(WlChainRules.MayMove(WlChainClass.PlayerMapGroup), "pre-pledge uses may-commit: never the map-group player");
    }

    private static void RuntimeWiring()
    {
        string pledge = ReadRepoFile("src/WhiskeyRealism/Patches/DefensiveOpsPatch.cs");
        AssertTrue(pledge.Contains("if (!WlChainRules.MayMove(WlStrategicOrderBridge.ChainClassOf(in chain, allianceId, unit))) continue;"),
            "#4 pre-pledge asks may-commit");
        AssertFalse(pledge.Contains("CountsTowardCoverage"), "#4 pre-pledge never asks the coverage question");
        AssertFalse(pledge.Contains("CountsAsCommittedDefender"), "the ambiguous name is gone");

        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/DefenseIntentRuntime.cs");
        AssertFalse(runtime.Contains("CountsAsCommittedDefender"), "the ambiguous name is gone");
        AssertTrue(runtime.Contains("bool playerControlled = regiment != null && !WlChainRules.MayMove(chainClass);"),
            "candidates the AI may not commit are marked player-controlled");
        AssertTrue(runtime.Contains("bool holdsPosition = regiment != null && playerControlled && WlChainRules.CountsTowardCoverage(chainClass);"),
            "coverage-only candidates hold their position");
        AssertTrue(runtime.Contains("regiment.GetLastWaypointPos()"), "the position held is the last waypoint, vanilla's basis");
        AssertTrue(runtime.Contains("HoldCoverRadius = defOpsRadius"), "cover radius is the defensive-operation radius");

        string ledger = ReadRepoFile("src/WhiskeyRealism/Strategic/DefenseIntentLedger.cs");
        AssertFalse(ledger.Contains("CountsAsCommittedDefender"), "the ambiguous name is gone");
        string rules = ReadRepoFile("src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs");
        AssertFalse(rules.Contains("CountsAsCommittedDefender"), "the ambiguous name is gone");
    }

    // ------------------------------------------------------------------ fixtures

    private static DefenseThreatSource Threat(int forceId)
    {
        return new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            InvasionForceInstanceId = forceId,
            SpotName = "spot" + forceId,
            SourcePortName = "port",
            LandedSignal = true,
            AssetName = "harbor" + forceId,
            AssetRole = AssetStrategicRole.BlockadeRunnerPort,
            X = ThreatX,
            Z = ThreatZ,
            EnemyStrength = 6000f
        };
    }

    private static DefenseIntentInput ThreatInput(params DefenseCandidate[] candidates)
    {
        var input = new DefenseIntentInput { AllianceId = 1, TotalAllianceEffectiveStrength = 60000f, HoldCoverRadius = Radius };
        input.Threats.Add(Threat(30));
        input.Candidates.AddRange(candidates);
        return input;
    }

    private static DefenseIntentInput GuardInput(params DefenseCandidate[] candidates)
    {
        var input = new DefenseIntentInput { AllianceId = 1, TotalAllianceEffectiveStrength = 60000f, HoldCoverRadius = Radius };
        input.GuardCandidateAssets.Add(new CampaignMapAsset
        {
            Kind = CampaignMapAssetKind.SeaHarbor, Name = "harbor", Owner = 1,
            StrategicRole = AssetStrategicRole.BlockadeRunnerPort, X = ThreatX, Z = ThreatZ
        });
        input.Candidates.AddRange(candidates);
        return input;
    }

    // The W&L map-group player's group: never assignable, holds the position of its last waypoint.
    private static DefenseCandidate Player(float holdX, float holdZ, float strength = 20000f)
    {
        return new DefenseCandidate
        {
            UnitInstanceId = PlayerId, ActiveStrength = strength, Morale = 1f, ReadinessStep = 2f,
            Tier = CandidateTier.Local, DistanceToThreat = 10f,
            PlayerControlled = true, HoldsPosition = true, HoldX = holdX, HoldZ = holdZ
        };
    }

    private static DefenseCandidate Ai(int id, float strength)
    {
        return new DefenseCandidate
        {
            UnitInstanceId = id, ActiveStrength = strength, Morale = 1f, ReadinessStep = 2f,
            Tier = CandidateTier.Local, DistanceToThreat = 50f
        };
    }

    private static DefenseResponse Only(DefenseIntentLedgerOutput output)
    {
        AssertTrue(output.Responses.Count >= 1, "a response");
        return output.Responses[0];
    }
}
