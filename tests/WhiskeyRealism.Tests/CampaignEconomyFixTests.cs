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
        FiscalSingleLowSupplySectorDoesNotBlockExpansion();
        FiscalLowSupplyThirdOfSectorsIsStress();
        PolicySelectorDefersToVanillaWhenOnlyOptionPenalised();
        PolicySelectorSwapsPenalisedFirstForNonSuppressed();
        PolicySelectorPicksClearScoreWin();

        ConstructionBiasReordersWithoutInflatingRolledRate();
        ConstructionBiasNormalisesEachSubsidyTypeToItsRawMax();
        ConstructionBiasRestoreSkipsSlotsVanillaConsumed();

        ConstructionSupplyCorridorMatchesAreaKeyAgainstAreaKey();
        ConstructionSupplyCorridorMatchesTheaterAgainstTheater();
        ConstructionCapitalThreatIsPerSiteTheater();
        ConstructionCapitalThreatPeakIsNotDilutedByQuietSectors();

        GovernorMatchesVanillaPassesOnCheapFrames();
        GovernorKeepsVanillaProgressionWhenEveryFrameIsOverBudget();
        GovernorGuaranteedPassesIgnoreBudget();
        GovernorRepaysBacklogOnCheapFramesUpToTwiceVanilla();
        GovernorRunsNothingWhenNothingOwed();
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

    // Vanilla Policies.CheckAIPolicyChange (decompile 211076-211086) researches the
    // first available personality policy. Blocking it when no alternative exists
    // stalls policy research until the penalty clears.
    private static void PolicySelectorDefersToVanillaWhenOnlyOptionPenalised()
    {
        var result = FiscalPolicySelector.Select(new[] { 136 }, new[] { -0.8f });
        TestHarness.AssertEqual(-1, result.Index, "no override");
        TestHarness.AssertEqual("vanilla", result.Reason, "defer to vanilla");
        LogPass("c2b policy selector defers to vanilla when only option penalised");
    }

    private static void PolicySelectorSwapsPenalisedFirstForNonSuppressed()
    {
        var result = FiscalPolicySelector.Select(new[] { 136, 40, 41 }, new[] { -0.8f, 0f, 0.2f });
        TestHarness.AssertEqual(1, result.Index, "first non-suppressed");
        TestHarness.AssertEqual("suppress-force-growth", result.Reason, "reason");
        LogPass("c2b policy selector swaps penalised first for non-suppressed");
    }

    private static void PolicySelectorPicksClearScoreWin()
    {
        var result = FiscalPolicySelector.Select(new[] { 10, 22 }, new[] { 0f, 1.5f });
        TestHarness.AssertEqual(1, result.Index, "score win");
        TestHarness.AssertEqual("score-win", result.Reason, "reason");
        var none = FiscalPolicySelector.Select(new[] { 10, 22 }, new[] { 0.6f, 0.8f });
        TestHarness.AssertEqual(-1, none.Index, "below min score / margin => vanilla");
        LogPass("c2b policy selector picks clear score win");
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
}
