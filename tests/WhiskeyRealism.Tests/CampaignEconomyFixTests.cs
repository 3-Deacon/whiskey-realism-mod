using System;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Fiscal;

// WS C2b — campaign economy / admin write-surface regressions (2026-10-14 review).
internal static class CampaignEconomyFixTests
{
    public static void Run()
    {
        RecruitmentDraftsOnlyStateIneligibleWhenStrengthRatioAboveOne();
        RecruitmentDraftsOnlyStateEligibleAtParityUnderConscription();
        RecruitmentWithoutPlanTheaterKeepsVanilla();
        RecruitmentWithoutPlanTheaterStillProtectsThreatenedCapital();

        FiscalBalancedBandLetsVanillaNudgeTaxBothWays();
        FiscalBalancedBandLetsVanillaRaiseSubsidyToFocusCap();
        FiscalCreditDefenseTaxBandIsFloorOnly();
        FiscalCreditDefenseSubsidyBandIsCeilingOnly();
        FiscalEmergencyPinsIndustrySubsidyCut();
        FiscalKingCottonCapsTariffLane();
        FiscalStepTowardBandMovesOneStepIntoBand();
        FiscalCreditDefenseTaxFloorDoesNotRatchetDaily();
        FiscalTaxFloorAnchorRefreshesAfterStressEnds();
        FiscalSingleLowSupplySectorDoesNotBlockExpansion();
        FiscalLowSupplyThirdOfSectorsIsStress();

        ConstructionBiasReordersWithoutInflatingRolledRate();
        ConstructionBiasNormalisesEachSubsidyTypeToItsRawMax();
        ConstructionBiasRestoreSkipsSlotsVanillaConsumed();
        ConstructionRolledWeightsMirrorVanillaNum3();
        ConstructionBiasCapsWeightedRollValue();
        ConstructionZeroMultiplierIsWrittenAndStaysSuppressed();

        ConstructionSupplyCorridorMatchesAreaKeyAgainstAreaKey();
        ConstructionSupplyCorridorMatchesTheaterAgainstTheater();
        ConstructionCapitalThreatIsPerSiteTheater();
        ConstructionCapitalThreatPeakIsNotDilutedByQuietSectors();
        ConstructionEastAtParityIsNotThreatened();
        ConstructionEmptyCriticalSectorIsNotThreatened();
        ConstructionOutmatchedCriticalSectorYieldsDefensiveWorks();
        ConstructionFewLowSupplyFormationsDoNotForceFieldSupply();

        GovernorMatchesVanillaPassesOnCheapFrames();
        GovernorKeepsVanillaProgressionWhenEveryFrameIsOverBudget();
        GovernorGuaranteedPassesIgnoreBudget();
        GovernorRepaysBacklogOnCheapFramesUpToTwiceVanilla();
        GovernorRunsNothingWhenNothingOwed();
        GovernorCatchUpRespectsCombinedFrameBudget();
        GovernorCatchUpHonoursSlowFrameCooldown();

        FortGovernorSkipsScoringWhenFactionAlreadyBuilding();
        FortGovernorSkipsScoringForIneligibleUnit();
        FortGovernorScoresEligibleUnit();
        FortSiteCacheReusesDecisionsWithinJobCycle();
        FortSiteCacheInvalidatesOnNewCycleOrWorldChange();
        FortSiteCacheDetectsCycleRestartOnIneligibleUnit();
        FortGovernorLetsHighThreatExceedSoftCap();

        PerkCandidateWindowMatchesVanillaRandomRange();
        PerkNearTieIsWeightedRandom();
        PerkClearWinnerIgnoresRoll();

        WlRetryDoesNothingWhenNoCommandSelectionPending();
        WlRetryReusesLivePanelWithoutLookup();
        WlRetryThrottlesPanelLookupWhilePending();
    }

    private static void LogPass(string message)
    {
        Console.WriteLine("PASS " + message);
    }

    // ---------------------------------------------------------------- C-Recruit

    private static RecruitmentStateCandidate State(int id, Theater theater, int volunteers, int drafts, float support = 0.9f, string area = null)
    {
        return new RecruitmentStateCandidate
        {
            StateId = id,
            Theater = theater,
            AreaKey = area,
            Volunteers = volunteers,
            Drafts = drafts,
            Support = support,
            IsRecruitable = true,
            IsEnemyState = false,
            IsLocalArea = true
        };
    }

    // Vanilla ZoneRecruiting (decompile 12605-12609): num13 = volunteers < size; if
    // strengthRatio > 1 && num13 == 1 the whole recruitment pass aborts. Steering to a
    // drafts-only state above parity would cancel recruitment, even under conscription.
    private static void RecruitmentDraftsOnlyStateIneligibleWhenStrengthRatioAboveOne()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 0,
            PreferredTheater = Theater.East,
            StrengthRatio = 1.2f,
            OwnStateSupportFloor = 0.5f,
            AllowDraftReplacement = true
        };
        var candidates = new[]
        {
            State(38, Theater.East, volunteers: 100, drafts: 9000),
            State(15, Theater.River, volunteers: 6000, drafts: 0, support: 0.5f)
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 15, strengthNeeded: 5000, excludeEnemyStates: false);

        TestHarness.AssertFalse(decision.ShouldReplace, "drafts-only state must not be chosen above parity");
        TestHarness.AssertEqual(15, decision.StateId, "keeps vanilla volunteer state");
        LogPass("c2b recruitment drafts-only state ineligible when strength ratio above one");
    }

    // Vanilla only aborts on strengthRatio > 1 (strict), so at exactly 1.0 with
    // conscription active the drafts state stays a legal steering target.
    private static void RecruitmentDraftsOnlyStateEligibleAtParityUnderConscription()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 0,
            PreferredTheater = Theater.East,
            StrengthRatio = 1.0f,
            OwnStateSupportFloor = 0.5f,
            AllowDraftReplacement = true
        };
        var candidates = new[]
        {
            State(38, Theater.East, volunteers: 100, drafts: 9000),
            State(15, Theater.River, volunteers: 6000, drafts: 0, support: 0.5f)
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 15, strengthNeeded: 5000, excludeEnemyStates: false);

        TestHarness.AssertTrue(decision.ShouldReplace, "drafts state allowed at parity under conscription");
        TestHarness.AssertEqual(38, decision.StateId, "steers to preferred-theater drafts state");
        LogPass("c2b recruitment drafts-only state eligible at parity under conscription");
    }

    // No active plan => PreferredTheater Unknown. Without a threatened capital area the
    // ledger has no strategic reason to override vanilla's distance/pool choice.
    private static void RecruitmentWithoutPlanTheaterKeepsVanilla()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 1,
            PreferredTheater = Theater.Unknown,
            StrengthRatio = 0.8f,
            OwnStateSupportFloor = 0.5f
        };
        var candidates = new[]
        {
            State(38, Theater.East, volunteers: 9000, drafts: 0, support: 0.95f),
            State(15, Theater.River, volunteers: 6000, drafts: 0, support: 0.55f)
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 15, strengthNeeded: 5000, excludeEnemyStates: false);

        TestHarness.AssertFalse(decision.ShouldReplace, "no plan theater => vanilla");
        TestHarness.AssertEqual(15, decision.StateId, "vanilla state kept");
        LogPass("c2b recruitment without plan theater keeps vanilla");
    }

    private static void RecruitmentWithoutPlanTheaterStillProtectsThreatenedCapital()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 1,
            PreferredTheater = Theater.Unknown,
            StrengthRatio = 0.8f,
            OwnStateSupportFloor = 0.5f,
            ProtectedAreaKey = "VirginiaCapitalCorridor",
            ProtectedAreaThreatLevel = 0.9f
        };
        var candidates = new[]
        {
            State(38, Theater.East, volunteers: 9000, drafts: 0, support: 0.95f, area: "VirginiaCapitalCorridor"),
            State(15, Theater.River, volunteers: 6000, drafts: 0, support: 0.55f, area: "Tennessee")
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 15, strengthNeeded: 5000, excludeEnemyStates: false);

        TestHarness.AssertTrue(decision.ShouldReplace, "threatened capital still steers without plan");
        TestHarness.AssertEqual(38, decision.StateId, "protected-area state");
        LogPass("c2b recruitment without plan theater still protects threatened capital");
    }

    // ---------------------------------------------------------------- C-Fiscal
    // Vanilla AICampaign.UpdateFinancialAI (decompile 15352-15447): balance below
    // trigger => +taxstepsai on a random lane 0-3; above reduce trigger => -step; rating
    // ok for construction => +step on a random subsidy lane (capped at subsidyfocus),
    // else -step when avg tax >= trigger. Our Postfix must only push against the
    // direction the posture forbids, never revert every nudge.

    private const float Step = 0.05f;

    private static FiscalInput Fiscal()
    {
        var input = new FiscalInput
        {
            AllianceId = 0,
            EraStage = EraStage.Amateur1861,
            CurrentChapter = 1,
            CurrentRating = 4,
            RatingNotches = 12,
            EmergencyPolicyFailureRating = 7,
            RecruitmentFailureRating = 8,
            ConstructionFailureRating = 8,
            WeaponFailureRating = 9,
            Treasury = 15000000f,
            Debt = 75000000f,
            AnnualBalance = 500000f,
            SupplyPressure = 0.10f,
            AmmoPressure = 0.05f,
            TransportPressure = 0.10f,
            SectorCount = 6
        };
        input.Taxes[0] = 0.4f; input.Taxes[1] = 0.4f; input.Taxes[2] = 0.3f;
        input.Subsidies[3] = 0.2f; input.Subsidies[4] = 0.2f;
        for (int i = 0; i < input.SubsidyFocus.Length; i++) input.SubsidyFocus[i] = 0.6f;
        return input;
    }

    private static void FiscalBalancedBandLetsVanillaNudgeTaxBothWays()
    {
        var output = FiscalIntentLedger.Compute(Fiscal(), new FiscalOptions());
        TestHarness.AssertEqual(FiscalPosture.BalancedWar, output.Posture, "posture");
        TestHarness.AssertTrue(output.TargetTaxMax[1] >= 0.4f + Step, "vanilla tax raise survives");
        TestHarness.AssertTrue(output.TargetTaxMin[1] <= 0.4f - Step, "vanilla tax cut survives");
        LogPass("c2b fiscal balanced band lets vanilla nudge tax both ways");
    }

    private static void FiscalBalancedBandLetsVanillaRaiseSubsidyToFocusCap()
    {
        var output = FiscalIntentLedger.Compute(Fiscal(), new FiscalOptions());
        TestHarness.AssertEqual(0.6f, output.TargetSubsidyMax[3], "subsidy ceiling is the personality focus cap");
        TestHarness.AssertEqual(0f, output.TargetSubsidyMin[3], "subsidy floor open");
        LogPass("c2b fiscal balanced band lets vanilla raise subsidy to focus cap");
    }

    private static void FiscalCreditDefenseTaxBandIsFloorOnly()
    {
        var input = Fiscal();
        input.CurrentRating = 6;
        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        TestHarness.AssertEqual(FiscalPosture.CreditDefense, output.Posture, "posture");
        TestHarness.AssertTrue(Math.Abs(output.TargetTaxMin[1] - 0.45f) < 1e-5f, "income-lane floor = snapshot + step");
        TestHarness.AssertEqual(1f, output.TargetTaxMax[1], "vanilla may raise further");
        LogPass("c2b fiscal credit defense tax band is floor only");
    }

    private static void FiscalCreditDefenseSubsidyBandIsCeilingOnly()
    {
        var input = Fiscal();
        input.CurrentRating = 6;
        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        TestHarness.AssertTrue(Math.Abs(output.TargetSubsidyMax[3] - 0.15f) < 1e-5f, "industry subsidy ceiling = snapshot - step");
        TestHarness.AssertEqual(0f, output.TargetSubsidyMin[3], "vanilla may cut further");
        LogPass("c2b fiscal credit defense subsidy band is ceiling only");
    }

    private static void FiscalEmergencyPinsIndustrySubsidyCut()
    {
        var input = Fiscal();
        input.CurrentRating = 11;
        input.Treasury = -1000000f;
        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        TestHarness.AssertEqual(FiscalPosture.EmergencySolvency, output.Posture, "posture");
        TestHarness.AssertTrue(Math.Abs(output.TargetSubsidyMax[3] - 0.15f) < 1e-5f, "crisis point band max");
        TestHarness.AssertTrue(Math.Abs(output.TargetSubsidyMin[3] - 0.15f) < 1e-5f, "crisis point band min");
        TestHarness.AssertEqual(1f, output.TargetTaxMax[1], "never cap taxes in a solvency crisis");
        LogPass("c2b fiscal emergency pins industry subsidy cut");
    }

    private static void FiscalKingCottonCapsTariffLane()
    {
        var input = Fiscal();
        input.AllianceId = 1;
        input.HasKingCotton = true;
        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        TestHarness.AssertEqual(0.15f, output.TargetTaxMax[0], "tariff ceiling applied below current 0.4");
        TestHarness.AssertTrue(FiscalIntentLedger.StepTowardBand(0.4f, output.TargetTaxMin[0], output.TargetTaxMax[0], Step) < 0.4f,
            "postfix steps tariff down toward ceiling");
        LogPass("c2b fiscal king cotton caps tariff lane");
    }

    private static void FiscalStepTowardBandMovesOneStepIntoBand()
    {
        TestHarness.AssertTrue(Math.Abs(FiscalIntentLedger.StepTowardBand(0.40f, 0f, 0.15f, Step) - 0.35f) < 1e-5f, "one step down");
        TestHarness.AssertTrue(Math.Abs(FiscalIntentLedger.StepTowardBand(0.10f, 0.45f, 1f, Step) - 0.15f) < 1e-5f, "one step up");
        TestHarness.AssertTrue(Math.Abs(FiscalIntentLedger.StepTowardBand(0.43f, 0.45f, 1f, Step) - 0.45f) < 1e-5f, "never overshoot floor");
        TestHarness.AssertEqual(0.30f, FiscalIntentLedger.StepTowardBand(0.30f, 0f, 1f, Step), "inside band untouched");
        LogPass("c2b fiscal step toward band moves one step into band");
    }

    // The floor is recomputed every daily review. Anchored to the snapshot at posture
    // entry it stays entry + step; anchored to "today" it climbed one step per day to 1.0
    // even while vanilla's own balance trigger (15405) was trying to cut taxes.
    private static void FiscalCreditDefenseTaxFloorDoesNotRatchetDaily()
    {
        var input = Fiscal();
        input.CurrentRating = 6;
        input.Memory.PreviousPosture = FiscalPosture.BalancedWar;
        input.Memory.TaxFloorAnchor = FiscalIntentLedger.NextTaxFloorAnchor(input.Memory.PreviousPosture, input.Memory.TaxFloorAnchor, input.Taxes);
        var day1 = FiscalIntentLedger.Compute(input, new FiscalOptions());
        TestHarness.AssertTrue(Math.Abs(day1.TargetTaxMin[1] - 0.45f) < 1e-5f, "day 1 floor = entry + step");

        // Day 2..30: postfix lifted the lane to the floor; posture persists.
        for (int day = 2; day <= 30; day++)
        {
            input.Taxes[1] = day1.TargetTaxMin[1];
            input.Memory.PreviousPosture = FiscalPosture.CreditDefense;
            input.Memory.TaxFloorAnchor = FiscalIntentLedger.NextTaxFloorAnchor(input.Memory.PreviousPosture, input.Memory.TaxFloorAnchor, input.Taxes);
        }
        var day30 = FiscalIntentLedger.Compute(input, new FiscalOptions());
        TestHarness.AssertTrue(Math.Abs(day30.TargetTaxMin[1] - 0.45f) < 1e-5f, "floor stays entry + step (got " + day30.TargetTaxMin[1] + ")");
        LogPass("c2b fiscal credit defense tax floor does not ratchet daily");
    }

    private static void FiscalTaxFloorAnchorRefreshesAfterStressEnds()
    {
        var anchor = new[] { 0.1f, 0.4f, 0.3f, 0f, 0f };
        var now = new[] { 0.1f, 0.2f, 0.2f, 0f, 0f };
        var next = FiscalIntentLedger.NextTaxFloorAnchor(FiscalPosture.BalancedWar, anchor, now);
        TestHarness.AssertTrue(Math.Abs(next[1] - 0.2f) < 1e-6f, "outside stress the anchor tracks current taxes");
        var held = FiscalIntentLedger.NextTaxFloorAnchor(FiscalPosture.EmergencySolvency, anchor, now);
        TestHarness.AssertTrue(Math.Abs(held[1] - 0.4f) < 1e-6f, "inside stress the entry anchor is held");
        LogPass("c2b fiscal tax floor anchor refreshes after stress ends");
    }

    private static void FiscalSingleLowSupplySectorDoesNotBlockExpansion()
    {
        var input = Fiscal();
        input.AnnualBalance = 2000000f;
        input.LowSupplyFormationCount = 1;
        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        TestHarness.AssertFalse(output.SupplyProtection, "one of six sectors is not systemic stress");
        TestHarness.AssertEqual(FiscalPosture.Expansion, output.Posture, "expansion not blocked");
        LogPass("c2b fiscal single low-supply sector does not block expansion");
    }

    private static void FiscalLowSupplyThirdOfSectorsIsStress()
    {
        var input = Fiscal();
        input.AnnualBalance = 2000000f;
        input.LowSupplyFormationCount = 2;
        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        TestHarness.AssertTrue(output.SupplyProtection, "two of six sectors starving is stress");
        TestHarness.AssertEqual(FiscalPosture.BalancedWar, output.Posture, "expansion held");
        LogPass("c2b fiscal low supply in a third of sectors is stress");
    }

    // ---------------------------------------------------------------- C-EconConstr
    // Vanilla UpdateCompanyFoundations (decompile 15000-15080): non-subsidy candidates
    // compete by prob*num3 and the winner is rolled with Random < prob*timediff*50
    // (15056); UpdateCompanyFoundationList (15253-15261) only replaces a slot when a new
    // site's raw prob beats the STORED prob. Bias must reorder selection only.

    private static void ConstructionBiasReordersWithoutInflatingRolledRate()
    {
        var raw = new[] { 0.40f, 0.30f };
        var mult = new[] { 1.0f, 2.0f };
        var groups = new[] { ConstructionProbabilityBias.RolledGroup, ConstructionProbabilityBias.RolledGroup };
        var biased = ConstructionProbabilityBias.Bias(raw, mult, groups);
        TestHarness.AssertTrue(biased[1] > biased[0], "bias reorders selection toward candidate 1");
        TestHarness.AssertTrue(biased[1] <= 0.40f + 1e-6f, "top biased value never exceeds vanilla's best raw (no rate inflation)");
        LogPass("c2b construction bias reorders without inflating rolled rate");
    }

    // Vanilla does not reset num between the subsidy loop and the rolled loop
    // (15012-15049): the last subsidy type's best prob becomes the rolled loop's entry
    // threshold. Each subsidy type is therefore normalised so its max equals its raw
    // max (argmax unchanged, threshold unchanged).
    private static void ConstructionBiasNormalisesEachSubsidyTypeToItsRawMax()
    {
        var raw = new[] { 0.40f, 0.20f, 0.10f, 0.30f };
        var mult = new[] { 1.0f, 1.0f, 3.0f, 0.5f };
        var groups = new[] { ConstructionProbabilityBias.RolledGroup, 2, 2, 5 };
        var biased = ConstructionProbabilityBias.Bias(raw, mult, groups);
        TestHarness.AssertTrue(biased[2] > biased[1], "bias reorders inside subsidy type 2");
        TestHarness.AssertTrue(Math.Abs(Math.Max(biased[1], biased[2]) - 0.20f) < 1e-6f, "type 2 max stays at raw max 0.20");
        TestHarness.AssertTrue(Math.Abs(biased[3] - 0.30f) < 1e-6f, "single dampened candidate keeps raw max (threshold preserved)");
        TestHarness.AssertTrue(Math.Abs(biased[0] - 0.40f) < 1e-6f, "rolled group untouched by subsidy groups");
        LogPass("c2b construction bias normalises each subsidy type to its raw max");
    }

    private static void ConstructionBiasRestoreSkipsSlotsVanillaConsumed()
    {
        TestHarness.AssertTrue(ConstructionProbabilityBias.ShouldRestore(0.35f, 0.35f, samePlace: true), "untouched slot restored");
        TestHarness.AssertFalse(ConstructionProbabilityBias.ShouldRestore(0f, 0.35f, samePlace: false), "vanilla built it: slot cleared");
        TestHarness.AssertFalse(ConstructionProbabilityBias.ShouldRestore(0.5f, 0.35f, samePlace: true), "vanilla refreshed value: keep");
        LogPass("c2b construction bias restore skips slots vanilla consumed");
    }

    // Vanilla 15043-15055: num3 starts at 1 and, for every rolled-loop candidate in index
    // order, is divided by max(1, 1 + recent foundings of that type); the candidate is
    // compared as prob * num3 and that product is what the build roll uses (15056).
    private static void ConstructionRolledWeightsMirrorVanillaNum3()
    {
        var w = ConstructionProbabilityBias.RolledWeights(new[] { true, true, false, true }, new[] { 0, 1, 5, 2 });
        TestHarness.AssertTrue(Math.Abs(w[0] - 1f) < 1e-6f, "w0");
        TestHarness.AssertTrue(Math.Abs(w[1] - 0.5f) < 1e-6f, "w1 = 1/(1+1)");
        TestHarness.AssertTrue(Math.Abs(w[2] - 0.5f) < 1e-6f, "non-loop slot carries the running num3");
        TestHarness.AssertTrue(Math.Abs(w[3] - 0.5f / 3f) < 1e-6f, "w3 = w1/(1+2)");
        LogPass("c2b construction rolled weights mirror vanilla num3");
    }

    private static void ConstructionBiasCapsWeightedRollValue()
    {
        var raw = new[] { 0.1f, 0.4f };
        var weight = new[] { 1f, 0.5f };
        var mult = new[] { 4f, 1f };
        var groups = new[] { ConstructionProbabilityBias.RolledGroup, ConstructionProbabilityBias.RolledGroup };
        var biased = ConstructionProbabilityBias.Bias(raw, mult, groups, weight);
        float rawTop = Math.Max(raw[0] * weight[0], raw[1] * weight[1]);
        float biasedTop = Math.Max(biased[0] * weight[0], biased[1] * weight[1]);
        TestHarness.AssertTrue(biased[0] * weight[0] > biased[1] * weight[1], "bias still reorders the weighted pick");
        TestHarness.AssertTrue(biasedTop <= rawTop + 1e-6f, "weighted roll value never exceeds vanilla's (" + biasedTop + " vs " + rawTop + ")");
        LogPass("c2b construction bias caps weighted roll value");
    }

    private static void ConstructionZeroMultiplierIsWrittenAndStaysSuppressed()
    {
        var biased = ConstructionProbabilityBias.Bias(
            new[] { 0.4f, 0.3f }, new[] { 0f, 1f },
            new[] { 3, 3 });
        TestHarness.AssertEqual(0f, biased[0], "suppressed candidate stays 0 after normalisation");
        TestHarness.AssertTrue(biased[1] > biased[0], "sibling wins");
        TestHarness.AssertTrue(ConstructionProbabilityBias.ShouldWrite(0.4f, 0f), "a suppression to 0 must be written to the slot");
        TestHarness.AssertFalse(ConstructionProbabilityBias.ShouldWrite(0.4f, 0.4f), "unchanged value not written");
        TestHarness.AssertFalse(ConstructionProbabilityBias.ShouldWrite(0.4f, float.NaN), "invalid never written");
        TestHarness.AssertFalse(ConstructionProbabilityBias.ShouldWrite(0f, 0f), "empty slot untouched");
        LogPass("c2b construction zero multiplier is written and stays suppressed");
    }

    // ---------------------------------------------------------------- C-ConstrRuntime

    private static void ConstructionSupplyCorridorMatchesAreaKeyAgainstAreaKey()
    {
        TestHarness.AssertTrue(
            ConstructionSupplyCorridor.Matches("", "ShenandoahValley", Theater.East, "ShenandoahValley"),
            "area key compared with the site's area key");
        TestHarness.AssertFalse(
            ConstructionSupplyCorridor.Matches("", "ShenandoahValley", Theater.East, "OhioValley"),
            "different area");
        LogPass("c2b construction supply corridor matches area key against area key");
    }

    private static void ConstructionSupplyCorridorMatchesTheaterAgainstTheater()
    {
        TestHarness.AssertTrue(
            ConstructionSupplyCorridor.Matches("West", null, Theater.West, "OhioValley"),
            "fiscal theater name compared with the site's theater");
        TestHarness.AssertFalse(ConstructionSupplyCorridor.Matches("", null, Theater.West, "OhioValley"), "nothing to match");
        LogPass("c2b construction supply corridor matches theater against theater");
    }

    private static readonly ConstructionSectorThreat[] Sectors =
    {
        new ConstructionSectorThreat { Theater = Theater.East, Threat = 0.9f },
        new ConstructionSectorThreat { Theater = Theater.West, Threat = 0f },
        new ConstructionSectorThreat { Theater = Theater.River, Threat = 0f },
        new ConstructionSectorThreat { Theater = Theater.Coast, Threat = 0f },
        new ConstructionSectorThreat { Theater = Theater.TransMiss, Threat = 0f },
        new ConstructionSectorThreat { Theater = Theater.Unknown, Threat = 0f }
    };

    private static void ConstructionCapitalThreatIsPerSiteTheater()
    {
        TestHarness.AssertEqual(0.9f, ConstructionThreat.ForTheater(Sectors, Theater.East), "threatened theater site");
        TestHarness.AssertEqual(0f, ConstructionThreat.ForTheater(Sectors, Theater.West), "quiet theater site");
        LogPass("c2b construction capital threat is per site theater");
    }

    private static void ConstructionCapitalThreatPeakIsNotDilutedByQuietSectors()
    {
        TestHarness.AssertEqual(0.9f, ConstructionThreat.Peak(Sectors), "peak, not 0.9/6");
        LogPass("c2b construction capital threat peak is not diluted by quiet sectors");
    }

    // Built from a real FrontSectorLedger (FrontSectorRuntime: East/River are always
    // critical, importance 0.95/0.9; ChoosePosture defaults to Hold). Threat must come
    // from the hold-ratio deficit, not from criticality alone.
    private static FrontSectorLedger Front(float eastOwn, float eastEnemy)
    {
        return FrontSectorLedger.Build(new[]
        {
            new FrontSectorInput { SectorKey = "East", Theater = Theater.East, OwnStrength = eastOwn, EnemyStrength = eastEnemy, StrategicImportance = 0.95f, IsCritical = true },
            new FrontSectorInput { SectorKey = "River", Theater = Theater.River, OwnStrength = 60000f, EnemyStrength = 60000f, StrategicImportance = 0.9f, IsCritical = true },
            new FrontSectorInput { SectorKey = "West", Theater = Theater.West, OwnStrength = 30000f, EnemyStrength = 30000f, StrategicImportance = 0.7f }
        });
    }

    private static ConstructionInput ExpansionConstructionInput(FrontSectorLedger front)
    {
        var input = new ConstructionInput
        {
            AllianceId = 0,
            FiscalPosture = WhiskeyRealism.Strategic.Fiscal.FiscalPosture.Expansion,
            CurrentRating = 2,
            BondFloorRating = 11
        };
        ConstructionThreat.Apply(input, front);
        return input;
    }

    private static void ConstructionEastAtParityIsNotThreatened()
    {
        var front = Front(100000f, 100000f);
        TestHarness.AssertEqual(FrontPosture.Hold, front.GetSector("East").Posture, "real ledger: East at parity holds");
        var input = ExpansionConstructionInput(front);
        TestHarness.AssertTrue(input.CapitalThreat < 0.35f, "parity is not a capital threat (got " + input.CapitalThreat + ")");
        var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());
        TestHarness.AssertEqual(ConstructionPosture.IndustrialExpansion, output.Posture, "expansion reachable at parity");
        LogPass("c2b construction east at parity is not threatened");
    }

    // FrontSectorRuntime builds every theater sector, empty or not; with no enemy present
    // own/max(1,enemy) is 0 and would read as a total deficit. No enemy, no threat.
    private static void ConstructionEmptyCriticalSectorIsNotThreatened()
    {
        var input = ExpansionConstructionInput(Front(0f, 0f));
        TestHarness.AssertEqual(0f, ConstructionThreat.ForTheater(input.SectorThreats, Theater.East), "empty critical sector");
        var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());
        TestHarness.AssertEqual(ConstructionPosture.IndustrialExpansion, output.Posture, "empty sector does not force defensive works");
        LogPass("c2b construction empty critical sector is not threatened");
    }

    private static void ConstructionOutmatchedCriticalSectorYieldsDefensiveWorks()
    {
        var input = ExpansionConstructionInput(Front(25000f, 100000f));
        TestHarness.AssertTrue(ConstructionThreat.ForTheater(input.SectorThreats, Theater.East) >= 0.65f, "East at 1:4 is threatened");
        TestHarness.AssertTrue(ConstructionThreat.ForTheater(input.SectorThreats, Theater.River) < 0.35f, "River at parity stays below the critical-defense site bar");
        var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());
        TestHarness.AssertEqual(ConstructionPosture.DefensiveWorks, output.Posture, "outmatched critical sector => defensive works");
        LogPass("c2b construction outmatched critical sector yields defensive works");
    }

    // Same share rule as fiscal supply stress: one starving formation out of twenty is
    // local, not a reason to switch the whole construction posture to FieldSupply.
    private static void ConstructionFewLowSupplyFormationsDoNotForceFieldSupply()
    {
        var input = new ConstructionInput
        {
            AllianceId = 0,
            FiscalPosture = WhiskeyRealism.Strategic.Fiscal.FiscalPosture.BalancedWar,
            CurrentRating = 2,
            BondFloorRating = 11,
            SupplyProtection = true,
            LowSupplyFormationCount = 1,
            FormationCount = 20
        };
        var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());
        TestHarness.AssertEqual(ConstructionPosture.Infrastructure, output.Posture, "1/20 starving formations");
        input.LowSupplyFormationCount = 8;
        output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());
        TestHarness.AssertEqual(ConstructionPosture.FieldSupply, output.Posture, "8/20 starving formations");
        LogPass("c2b construction few low-supply formations do not force field supply");
    }

    // ---------------------------------------------------------------- C-Governor
    // Vanilla AICampaign.Update (decompile 11195-11199) runs floor(sqrt(gamespeed))
    // UpdateUnitAI passes per frame; each pass advances one job of the per-faction job
    // table (11372-11629; job 4 and job 21 iterate one area/unit per pass). The governor
    // may smooth that work across frames but must not run fewer passes than vanilla over
    // time (bounded lag).

    private static readonly CampaignAiGovernorOptions Governor = new CampaignAiGovernorOptions
    {
        Enabled = true,
        MaxPassesAt20x = 2,
        MaxPassesAt50x = 3,
        FrameBudgetMs = 3f,
        MaxBacklogFrames = 8
    };

    // costMs = simulated cost of one pass; returns total passes, tracks peak backlog.
    private static int SimulateGovernor(float speed, int frames, float costMs, out float peakOwed)
    {
        float owed = 0f;
        int total = 0;
        peakOwed = 0f;
        for (int f = 0; f < frames; f++)
        {
            owed = FastForwardAiScheduler.AccrueOwedPasses(owed, speed);
            int executed = 0;
            float elapsed = 0f;
            while (FastForwardAiScheduler.ShouldRunGovernedPass(executed, elapsed, owed, speed, Governor))
            {
                executed++;
                owed -= 1f;
                elapsed += costMs;
                if (executed > 1000) throw new Exception("governor loop did not terminate");
            }
            total += executed;
            if (owed > peakOwed) peakOwed = owed;
        }
        return total;
    }

    private static void GovernorMatchesVanillaPassesOnCheapFrames()
    {
        int total = SimulateGovernor(50f, 100, costMs: 0.1f, out _);
        TestHarness.AssertEqual(700, total, "50x cheap frames run exactly vanilla's 7/frame");
        total = SimulateGovernor(20f, 100, costMs: 0.1f, out _);
        TestHarness.AssertEqual(400, total, "20x cheap frames run exactly vanilla's 4/frame");
        LogPass("c2b governor matches vanilla passes on cheap frames");
    }

    private static void GovernorKeepsVanillaProgressionWhenEveryFrameIsOverBudget()
    {
        float peak;
        int total = SimulateGovernor(50f, 100, costMs: 5f, out peak);
        TestHarness.AssertTrue(total >= 700 - 7 * 8, "never more than MaxBacklogFrames of vanilla work behind (got " + total + ")");
        TestHarness.AssertTrue(peak <= 7 * 8 + 1, "backlog bounded (peak " + peak + ")");
        LogPass("c2b governor keeps vanilla progression when every frame is over budget");
    }

    private static void GovernorGuaranteedPassesIgnoreBudget()
    {
        TestHarness.AssertTrue(FastForwardAiScheduler.ShouldRunGovernedPass(0, 99f, 7f, 50f, Governor), "first pass always");
        TestHarness.AssertTrue(FastForwardAiScheduler.ShouldRunGovernedPass(2, 99f, 7f, 50f, Governor), "guaranteed 3 at 50x");
        TestHarness.AssertFalse(FastForwardAiScheduler.ShouldRunGovernedPass(3, 99f, 7f, 50f, Governor), "over budget after guarantee");
        LogPass("c2b governor guaranteed passes ignore budget");
    }

    private static void GovernorRepaysBacklogOnCheapFramesUpToTwiceVanilla()
    {
        TestHarness.AssertTrue(FastForwardAiScheduler.ShouldRunGovernedPass(10, 0.5f, 20f, 50f, Governor), "cheap frame repays backlog past vanilla");
        TestHarness.AssertFalse(FastForwardAiScheduler.ShouldRunGovernedPass(14, 0.5f, 20f, 50f, Governor), "ceiling 2x vanilla per cheap frame");
        TestHarness.AssertTrue(FastForwardAiScheduler.ShouldRunGovernedPass(20, 99f, 7f * 8 + 2, 50f, Governor), "forced above backlog bound");
        LogPass("c2b governor repays backlog on cheap frames up to twice vanilla");
    }

    private static void GovernorRunsNothingWhenNothingOwed()
    {
        TestHarness.AssertFalse(FastForwardAiScheduler.ShouldRunGovernedPass(0, 0f, 0.5f, 50f, Governor), "nothing owed");
        TestHarness.AssertEqual(7f, FastForwardAiScheduler.AccrueOwedPasses(-3f, 50f), "negative owed clamps before accrual");
        LogPass("c2b governor runs nothing when nothing owed");
    }

    private static readonly FastForwardAiOptions CatchUp = new FastForwardAiOptions
    {
        Enabled = true,
        MaxExtraPassesAt50x = 4,
        FrameBudgetMs = 1.5f,
        SlowFrameThresholdMs = 8f,
        SlowFrameCooldownFrames = 180
    };

    private static void GovernorCatchUpRespectsCombinedFrameBudget()
    {
        TestHarness.AssertTrue(
            FastForwardAiScheduler.ShouldRunCatchUpPass(0, totalElapsedMs: 1f, extraElapsedMs: 0f, owedPasses: 0f, gameSpeed: 50f, Governor, CatchUp, unityFrame: 10, cooldownUntilFrame: 0),
            "cheap frame, nothing owed => catch-up");
        TestHarness.AssertFalse(
            FastForwardAiScheduler.ShouldRunCatchUpPass(0, totalElapsedMs: 3.2f, extraElapsedMs: 0f, owedPasses: 0f, gameSpeed: 50f, Governor, CatchUp, unityFrame: 10, cooldownUntilFrame: 0),
            "owed passes already spent the frame budget => no extra pass");
        TestHarness.AssertFalse(
            FastForwardAiScheduler.ShouldRunCatchUpPass(1, totalElapsedMs: 2f, extraElapsedMs: 1.5f, owedPasses: 0f, gameSpeed: 50f, Governor, CatchUp, unityFrame: 10, cooldownUntilFrame: 0),
            "catch-up budget spent");
        TestHarness.AssertFalse(
            FastForwardAiScheduler.ShouldRunCatchUpPass(0, totalElapsedMs: 0f, extraElapsedMs: 0f, owedPasses: 1f, gameSpeed: 50f, Governor, CatchUp, unityFrame: 10, cooldownUntilFrame: 0),
            "vanilla work still owed => no extras");
        LogPass("c2b governor catch-up respects combined frame budget");
    }

    private static void GovernorCatchUpHonoursSlowFrameCooldown()
    {
        TestHarness.AssertFalse(
            FastForwardAiScheduler.ShouldRunCatchUpPass(0, 0f, 0f, 0f, 50f, Governor, CatchUp, unityFrame: 100, cooldownUntilFrame: 280),
            "inside slow-frame cooldown");
        TestHarness.AssertTrue(
            FastForwardAiScheduler.ShouldRunCatchUpPass(0, 0f, 0f, 0f, 50f, Governor, CatchUp, unityFrame: 280, cooldownUntilFrame: 280),
            "cooldown over");
        LogPass("c2b governor catch-up honours slow-frame cooldown");
    }

    // ---------------------------------------------------------------- C-FortGov
    // Vanilla AICampaign.CheckFortConstruction (decompile 16347-16405) is job 21
    // (11520-11527), called once per own unit per pass (currentfortconstructionunitrunthrough).
    // It returns before touching sites when bfs/bunits are null (16360-16367), when the
    // faction already has a fort order (16368, IsFactionAlreadyConstructingFort 9694), or
    // when the current unit fails the idle/supply/op gates (16373-16381).

    private static FortConstructionCallGate OpenGate()
    {
        return new FortConstructionCallGate
        {
            HasBattlefieldSetup = true,
            HasBattleUnits = true,
            FactionAlreadyConstructing = false,
            UnitPresent = true,
            UnitEligible = true
        };
    }

    private static void FortGovernorSkipsScoringWhenFactionAlreadyBuilding()
    {
        var gate = OpenGate();
        gate.FactionAlreadyConstructing = true;
        TestHarness.AssertFalse(FortConstructionGovernor.ShouldEvaluateSites(gate), "one active order per faction");
        LogPass("c2b fort governor skips scoring when faction already building");
    }

    private static void FortGovernorSkipsScoringForIneligibleUnit()
    {
        var gate = OpenGate();
        gate.UnitEligible = false;
        TestHarness.AssertFalse(FortConstructionGovernor.ShouldEvaluateSites(gate), "ineligible unit");
        gate = OpenGate();
        gate.UnitPresent = false;
        TestHarness.AssertFalse(FortConstructionGovernor.ShouldEvaluateSites(gate), "runthrough index past ownunits");
        gate = OpenGate();
        gate.HasBattlefieldSetup = false;
        TestHarness.AssertFalse(FortConstructionGovernor.ShouldEvaluateSites(gate), "no bfs");
        LogPass("c2b fort governor skips scoring for ineligible unit");
    }

    private static void FortGovernorScoresEligibleUnit()
    {
        TestHarness.AssertTrue(FortConstructionGovernor.ShouldEvaluateSites(OpenGate()), "vanilla would scan sites");
        LogPass("c2b fort governor scores eligible unit");
    }

    private static void FortSiteCacheReusesDecisionsWithinJobCycle()
    {
        var cache = new FortSiteDecisionCache();
        long sig = FortSiteDecisionCache.Signature(siteCount: 40, fortCount: 12, orderCount: 0);
        cache.ObserveRunthrough(0);
        cache.BeginCall(sig);
        cache.Store(3, new FortConstructionGovernorDecision { Allow = false, Reason = "hard-cap" });
        cache.ObserveRunthrough(1);
        cache.BeginCall(sig);
        TestHarness.AssertTrue(cache.TryGet(3, out var d) && d.Reason == "hard-cap", "same cycle reuses site decision");
        TestHarness.AssertFalse(cache.TryGet(4, out _), "unscored site misses");
        LogPass("c2b fort site cache reuses decisions within job cycle");
    }

    private static void FortSiteCacheInvalidatesOnNewCycleOrWorldChange()
    {
        var cache = new FortSiteDecisionCache();
        long sig = FortSiteDecisionCache.Signature(40, 12, 0);
        cache.ObserveRunthrough(2);
        cache.BeginCall(sig);
        cache.Store(3, new FortConstructionGovernorDecision { Allow = true, Reason = "allowed" });
        cache.ObserveRunthrough(3);
        cache.BeginCall(FortSiteDecisionCache.Signature(40, 13, 0));
        TestHarness.AssertFalse(cache.TryGet(3, out _), "new fort built => rescore");
        LogPass("c2b fort site cache invalidates on new cycle or world change");
    }

    // Job 21 visits one unit per call; the cycle can restart on a unit that fails the
    // eligibility gate (no BeginCall). The cycle boundary must still be seen.
    private static void FortSiteCacheDetectsCycleRestartOnIneligibleUnit()
    {
        var cache = new FortSiteDecisionCache();
        long sig = FortSiteDecisionCache.Signature(40, 12, 0);
        cache.ObserveRunthrough(4);
        cache.BeginCall(sig);
        cache.Store(3, new FortConstructionGovernorDecision { Allow = false, Reason = "hard-cap" });
        cache.ObserveRunthrough(0); // gate fails for unit 0: no BeginCall
        cache.ObserveRunthrough(1);
        cache.BeginCall(sig);
        TestHarness.AssertFalse(cache.TryGet(3, out _), "previous cycle's decision dropped");
        LogPass("c2b fort site cache detects cycle restart on ineligible unit");
    }

    // The soft cap binds only under low threat (Decide): a threatened local area may
    // build past it up to the hard cap. Regression guard for the plan item.
    private static void FortGovernorLetsHighThreatExceedSoftCap()
    {
        var d = FortConstructionGovernor.Decide(new FortConstructionSiteContext
        {
            ExistingFortCount = 3,
            ActiveOrderCount = 0,
            NearCapital = false,
            ThreatRatio = 0.9f
        });
        TestHarness.AssertTrue(d.Allow, "threatened local area above soft cap 2, below hard cap 4");
        LogPass("c2b fort governor lets high threat exceed soft cap");
    }

    // ---------------------------------------------------------------- C-Perk
    // Vanilla AICampaign.CheckPerkSelection (decompile 11871-11891) calls
    // ChoosePerk(Random.Range(0, Length/3 - 1)); the int overload's upper bound is
    // exclusive, so the AI never picks the last perk (LevelsPerPerk = 3, 208648).
    // With the fix off the candidate window stays vanilla's (BUG-AI-011).

    private static void PerkCandidateWindowMatchesVanillaRandomRange()
    {
        TestHarness.AssertEqual(17, PerkSelectionScorer.CandidateLimit(18, false), "18 army perks => ids 0..16");
        TestHarness.AssertEqual(10, PerkSelectionScorer.CandidateLimit(11, false), "11 fleet perks => ids 0..9");
        TestHarness.AssertEqual(1, PerkSelectionScorer.CandidateLimit(1, false), "Random.Range(0,0) returns 0");
        TestHarness.AssertEqual(0, PerkSelectionScorer.CandidateLimit(0, false), "no perks");
        LogPass("c2b perk candidate window matches vanilla random range");
    }

    private static void PerkNearTieIsWeightedRandom()
    {
        // General role: Siege Train 2.2 vs Sappers and Miners 2.1 => near tie.
        var perks = new[] { 0, 10 };
        int low = PerkSelectionScorer.SelectArmyPerk(0, Theater.West, ArmyPerkRole.General, default(PersonalityVector), perks, roll: 0.05f);
        int high = PerkSelectionScorer.SelectArmyPerk(0, Theater.West, ArmyPerkRole.General, default(PersonalityVector), perks, roll: 0.95f);
        TestHarness.AssertEqual(0, low, "low roll => best");
        TestHarness.AssertEqual(10, high, "high roll => near-equal runner-up");
        LogPass("c2b perk near tie is weighted random");
    }

    private static void PerkClearWinnerIgnoresRoll()
    {
        int selected = PerkSelectionScorer.SelectArmyPerk(0, Theater.East, ArmyPerkRole.Siege, default(PersonalityVector), new[] { 1, 5, 10, 12 }, roll: 0.99f);
        TestHarness.AssertEqual(10, selected, "siege role still takes sappers");
        int fleet = PerkSelectionScorer.SelectFleetPerk(1, FleetPerkRole.Raid, new[] { 1, 5, 6, 8, 10 }, roll: 0.99f);
        TestHarness.AssertEqual(6, fleet, "raid fleet still takes sea raiders");
        LogPass("c2b perk clear winner ignores roll");
    }

    // ---------------------------------------------------------------- P-wlretry
    // Vanilla CampaignController.Update calls careerinformationpanel
    // .ShowStartUnitSelectionList() once at GameVars.frame == 50 (decompile 30145-30155);
    // the panel (182353) only opens for a W&L scenario whose chosen commander has no
    // command. The retry runs on every AICampaign/CampaignController frame, so all
    // lookups must sit behind the pending check and be cached.

    private static void WlRetryDoesNothingWhenNoCommandSelectionPending()
    {
        TestHarness.AssertFalse(
            WlStartSelectionPanelLookup.ShouldFindPanel(pending: false, cachedPanelAlive: false, unityFrame: 1000, nextFindFrame: 0),
            "no W&L command selection pending => no GameObject.Find");
        LogPass("c2b wl retry does nothing when no command selection pending");
    }

    private static void WlRetryReusesLivePanelWithoutLookup()
    {
        TestHarness.AssertFalse(
            WlStartSelectionPanelLookup.ShouldFindPanel(pending: true, cachedPanelAlive: true, unityFrame: 1000, nextFindFrame: 0),
            "cached live panel => no lookup");
        LogPass("c2b wl retry reuses live panel without lookup");
    }

    private static void WlRetryThrottlesPanelLookupWhilePending()
    {
        TestHarness.AssertTrue(
            WlStartSelectionPanelLookup.ShouldFindPanel(pending: true, cachedPanelAlive: false, unityFrame: 100, nextFindFrame: 100),
            "lookup due");
        TestHarness.AssertFalse(
            WlStartSelectionPanelLookup.ShouldFindPanel(pending: true, cachedPanelAlive: false, unityFrame: 101, nextFindFrame: 115),
            "lookup throttled until next window");
        TestHarness.AssertEqual(115, WlStartSelectionPanelLookup.NextFindFrame(100), "15-frame lookup window");
        LogPass("c2b wl retry throttles panel lookup while pending");
    }
}
