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
            ("player position: corps standing at the second threat covers that one, not the first in order", CorpsCoversNearestThreatNotFirstInOrder),
            ("player position: corps between a threat and a guard asset covers only the nearer", CorpsCoversNearerOfThreatAndAsset),
            ("player position: equidistant targets go to the lowest index", EquidistantTargetsGoToLowestIndex),
            ("player position: a recovered threat does not take the corps", RecoveredThreatDoesNotTakeCorps),
            ("player position: an asset a threat already answers takes no cover", AssetAnsweredByThreatTakesNoCover),
            ("player position: guard asset covered by position needs no guard budget", GuardCoveredByPositionNeedsNoBudget),
            ("player position: decisive landing counts held strength alongside its critical-front unit", DecisiveLandingCountsHeldWithCriticalFront),
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
        AssertTrue(HasLine(pledge, "if (!WlChainRules.MayMove(WlStrategicOrderBridge.ChainClassOf(in chain, allianceId, unit))) continue;"),
            "#4 pre-pledge asks may-commit");
        AssertFalse(pledge.Contains("CountsTowardCoverage"), "#4 pre-pledge never asks the coverage question");
        AssertFalse(pledge.Contains("CountsAsCommittedDefender"), "the ambiguous name is gone");

        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/DefenseIntentRuntime.cs");
        AssertFalse(runtime.Contains("CountsAsCommittedDefender"), "the ambiguous name is gone");
        AssertTrue(HasLine(runtime, "bool playerControlled = regiment != null && !WlChainRules.MayMove(chainClass);"),
            "candidates the AI may not commit are marked player-controlled");
        AssertTrue(HasLine(runtime, "bool holdsPosition = regiment != null && playerControlled && WlChainRules.CountsTowardCoverage(chainClass);"),
            "coverage-only candidates hold their position");
        AssertTrue(HasLine(runtime, "Vector3 holdPos = holdsPosition ? regiment.GetLastWaypointPos() : unitPos;"),
            "the position held is the last waypoint, vanilla's basis");
        AssertTrue(HasLine(runtime, "input.HoldCoverRadius = defOpsRadius;"), "cover radius is the defensive-operation radius");
        AssertTrue(HasLine(runtime, "if (regiment.inbattle && !holdsPosition) continue;"),
            "an in-battle holder still counts, an in-battle AI group is skipped");
        AssertTrue(HasLine(runtime, "if (ReadBool(unit, \"inbattle\", false)) continue;"), "non-regiment units in battle are skipped");

        string ledger = ReadRepoFile("src/WhiskeyRealism/Strategic/DefenseIntentLedger.cs");
        AssertFalse(ledger.Contains("CountsAsCommittedDefender"), "the ambiguous name is gone");
        string rules = ReadRepoFile("src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs");
        AssertFalse(rules.Contains("CountsAsCommittedDefender"), "the ambiguous name is gone");
    }


    private static void CorpsCoversNearestThreatNotFirstInOrder()
    {
        // Both threats lie inside the cover radius of the corps; it stands at the second one.
        // The first in processing order must not take the credit: that would leave the corps's
        // own spot uncovered and send AI units to it.
        var input = new DefenseIntentInput { AllianceId = 1, TotalAllianceEffectiveStrength = 60000f, HoldCoverRadius = Radius };
        input.Threats.Add(Threat(30, ThreatX));
        input.Threats.Add(Threat(31, ThreatX + 200f));
        input.Candidates.Add(Player(ThreatX + 200f, ThreatZ));
        input.Candidates.Add(Ai(AiId, 8000f));
        var output = DefenseIntentLedger.Build(input);
        AssertEqual(2, output.Responses.Count, "two responses");
        AssertNear(0f, output.Responses[0].HeldStrength, 0.01f, "the first threat is 200 away: not the corps's");
        AssertTrue(output.Responses[0].SelectedPackage.Exists(c => c.UnitInstanceId == AiId), "the first threat gets the AI package");
        AssertNear(20000f, output.Responses[1].HeldStrength, 0.01f, "the corps covers the threat it stands at");
        AssertEqual(0, output.Responses[1].SelectedPackage.Count, "the covered threat gets no package");
    }

    private static void CorpsCoversNearerOfThreatAndAsset()
    {
        // Threat 30 at ThreatX; unguarded asset 200 east of it. One corps, in range of both.
        var atAsset = ThreatAndGuardInput(Player(ThreatX + 200f, ThreatZ), Ai(AiId, 8000f), Ai(3, 2500f));
        var a = DefenseIntentLedger.Build(atAsset);
        AssertEqual(2, a.Responses.Count, "a threat response and a guard response");
        AssertNear(0f, a.Responses[0].HeldStrength, 0.01f, "standing at the asset: the threat is farther");
        AssertNear(20000f, a.Responses[1].HeldStrength, 0.01f, "the asset it stands at is covered");
        AssertTrue(a.Responses[1].Adequate, "guard slot covered");

        var atThreat = ThreatAndGuardInput(Player(ThreatX, ThreatZ), Ai(3, 2500f));
        var t = DefenseIntentLedger.Build(atThreat);
        AssertEqual(2, t.Responses.Count, "a threat response and a guard response");
        AssertNear(20000f, t.Responses[0].HeldStrength, 0.01f, "standing at the threat: it is covered");
        AssertNear(0f, t.Responses[1].HeldStrength, 0.01f, "the asset 200 away gets nothing: one corps, one place");
        AssertTrue(t.Responses[1].SelectedPackage.Count > 0, "the asset gets an AI guard instead");
        AssertFalse(t.Responses[1].SelectedPackage.Exists(c => c.UnitInstanceId == PlayerId), "never the player");
    }

    private static void EquidistantTargetsGoToLowestIndex()
    {
        var input = new DefenseIntentInput { AllianceId = 1, TotalAllianceEffectiveStrength = 60000f, HoldCoverRadius = Radius };
        input.Threats.Add(Threat(30, ThreatX - 100f));
        input.Threats.Add(Threat(31, ThreatX + 100f));
        input.Candidates.Add(Player(ThreatX, ThreatZ));
        input.Candidates.Add(Ai(AiId, 8000f));
        var output = DefenseIntentLedger.Build(input);
        AssertNear(20000f, output.Responses[0].HeldStrength, 0.01f, "the tie goes to the first threat in order");
        AssertNear(0f, output.Responses[1].HeldStrength, 0.01f, "the second gets nothing");
    }

    private static void RecoveredThreatDoesNotTakeCorps()
    {
        // A recovered threat emits an empty response and spends no cover; the corps, standing
        // nearest to it, must still cover the live threat in range.
        var recovered = Threat(30, ThreatX);
        recovered.SightingLapsed = true; // the belief no longer qualifies (this branch's replacement for vanilla's collapsed force)
        var input = new DefenseIntentInput { AllianceId = 1, TotalAllianceEffectiveStrength = 60000f, HoldCoverRadius = Radius };
        input.Threats.Add(recovered);
        input.Threats.Add(Threat(31, ThreatX + 200f));
        input.Candidates.Add(Player(ThreatX, ThreatZ));
        input.Candidates.Add(Ai(AiId, 8000f));
        var output = DefenseIntentLedger.Build(input);
        DefenseResponse live = output.Responses.Find(r => r.Threat.Posture != DefensePosture.Recovered);
        AssertTrue(live != null, "a live response");
        AssertNear(20000f, live.HeldStrength, 0.01f, "the corps covers the live threat in range");
    }

    private static void AssetAnsweredByThreatTakesNoCover()
    {
        // The guard asset "harbor30" is the threat's own asset, so its guard pass is skipped.
        // A corps standing exactly at that asset, 100 from the threat, must cover the threat:
        // crediting the skipped guard slot would lose the cover.
        var input = new DefenseIntentInput { AllianceId = 1, TotalAllianceEffectiveStrength = 60000f, HoldCoverRadius = Radius };
        input.Threats.Add(Threat(30, ThreatX));
        input.GuardCandidateAssets.Add(new CampaignMapAsset
        {
            Kind = CampaignMapAssetKind.SeaHarbor, Name = "harbor30", Owner = 1,
            StrategicRole = AssetStrategicRole.BlockadeRunnerPort, X = ThreatX + 100f, Z = ThreatZ
        });
        input.Candidates.Add(Player(ThreatX + 100f, ThreatZ));
        input.Candidates.Add(Ai(AiId, 8000f));
        var output = DefenseIntentLedger.Build(input);
        AssertEqual(1, output.Responses.Count, "the threat answers the asset: no guard response");
        AssertNear(20000f, output.Responses[0].HeldStrength, 0.01f, "the corps covers the threat");
        AssertEqual(0, output.Responses[0].SelectedPackage.Count, "no package sent");
    }

    private static void GuardCoveredByPositionNeedsNoBudget()
    {
        // A guard budget of zero turns away every AI guard, yet a corps standing at the asset
        // still covers it: cover spends no budget.
        var held = GuardInput(Player(ThreatX, ThreatZ), Ai(AiId, 2500f));
        held.GuardBudgetFraction = 0f;
        var covered = Only(DefenseIntentLedger.Build(held));
        AssertTrue(covered.Adequate, "covered by position with the budget exhausted");
        AssertNear(20000f, covered.HeldStrength, 0.01f, "held recorded");

        var away = GuardInput(Player(ThreatX + 3000f, ThreatZ), Ai(AiId, 2500f));
        away.GuardBudgetFraction = 0f;
        var uncovered = Only(DefenseIntentLedger.Build(away));
        AssertFalse(uncovered.Adequate, "corps away and no budget: the slot stays open");
        AssertTrue(uncovered.Understrength, "understrength");
    }

    private static void DecisiveLandingCountsHeldWithCriticalFront()
    {
        // Decisive landing, desired 18000 (adequate at 13500). A decisive landing admits the
        // critical-front unit to the one aggregator pass. The corps holds 6000, the pool offers
        // 3000 + 8000: adequate only if the held 6000 counts (11000 alone is short).
        var threat = Threat(30, ThreatX, strength: 8000f);
        var input = new DefenseIntentInput { AllianceId = 1, TotalAllianceEffectiveStrength = 200000f, HoldCoverRadius = Radius };
        input.Threats.Add(threat);
        input.Candidates.Add(Player(ThreatX, ThreatZ, strength: 6000f));
        input.Candidates.Add(Ai(AiId, 3000f));
        var critical = Ai(3, 8000f);
        critical.CriticalFront = true;
        input.Candidates.Add(critical);
        var response = Only(DefenseIntentLedger.Build(input));
        AssertEqual(ThreatScale.DecisiveLanding, response.Threat.Scale, "decisive");
        AssertNear(6000f, response.HeldStrength, 0.01f, "held");
        AssertTrue(response.SelectedPackage.Exists(c => c.UnitInstanceId == 3), "the critical-front unit was admitted");
        AssertTrue(response.Adequate, "held strength counts toward the decisive landing");
    }

    // ------------------------------------------------------------------ fixtures

    private static DefenseThreatSource Threat(int forceId, float x = ThreatX, float strength = 6000f)
    {
        return new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            EnemyUnitKey = "force" + forceId,
            LandedSignal = true,
            AssetName = "harbor" + forceId,
            AssetRole = AssetStrategicRole.BlockadeRunnerPort,
            X = x,
            Z = ThreatZ,
            EnemyStrength = strength
        };
    }

    private static DefenseIntentInput ThreatInput(params DefenseCandidate[] candidates)
    {
        var input = new DefenseIntentInput { AllianceId = 1, TotalAllianceEffectiveStrength = 60000f, HoldCoverRadius = Radius };
        input.Threats.Add(Threat(30));
        input.Candidates.AddRange(candidates);
        return input;
    }

    // Threat 30 at the origin spot plus an unguarded asset 200 east of it, so one corps can stand in range of both.
    private static DefenseIntentInput ThreatAndGuardInput(params DefenseCandidate[] candidates)
    {
        var input = ThreatInput(candidates);
        input.GuardCandidateAssets.Add(new CampaignMapAsset
        {
            Kind = CampaignMapAssetKind.SeaHarbor, Name = "east-harbor", Owner = 1,
            StrategicRole = AssetStrategicRole.BlockadeRunnerPort, X = ThreatX + 200f, Z = ThreatZ
        });
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
