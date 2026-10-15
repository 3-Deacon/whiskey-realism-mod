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

internal static class WlCampaignTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("wl operation null guard finishes missing operation", WlOperationNullGuardFinishesMissingOperation),
            ("wl career start gate defers until player command is selected", WlCareerStartGateDefersUntilCommandSelected),
            ("wl diary startup gate defers until diary dependencies are ready", WlDiaryStartupGateDefersUntilReady),
            ("wl diary startup gate defers until campaign group lookup is safe", WlDiaryStartupGateDefersUntilCampaignGroupLookupReady),
            ("wl diary startup gate defers until current update cycle is safe", WlDiaryStartupGateDefersUntilCurrentCycleReady),
            ("wl diary capital defense cycle waits for ai faction state", WlDiaryCapitalDefenseCycleWaitsForAiFactionState),
            ("wl start selection retry does not depend on campaign frame", WlStartSelectionRetryDoesNotDependOnCampaignFrame),
            ("wl start selection retry waits for panel before consuming attempt", WlStartSelectionRetryWaitsForPanel),
            ("wl start selection retry waits for vanilla ready frame", WlStartSelectionRetryWaitsForReadyFrame),
            ("wl start selection retry blocks early ready data before frame fifty", WlStartSelectionRetryBlocksEarlyReadyData),
            ("wl dispatch sanitizer fixes type 56 stance none", WlDispatchSanitizerFixesType56StanceNone),
            ("wl dispatch sanitizer fixes type 57 stance none", WlDispatchSanitizerFixesType57StanceNone),
            ("wl dispatch sanitizer fixes type 15 no-orders none", WlDispatchSanitizerFixesType15NoOrdersNone),
            ("wl dispatch sanitizer ignores non-candidate type", WlDispatchSanitizerIgnoresNonCandidateType),
            ("wl dispatch sanitizer handles null content", WlDispatchSanitizerHandlesNullContent),
            ("wl dispatch sanitizer leaves normal content unchanged", WlDispatchSanitizerLeavesNormalContentUnchanged),
            ("wl bridge inactive allows direct movement", WlBridgeInactiveAllowsDirectMovement),
            ("wl bridge null tryissue fails closed", WlBridgeNullTryIssueFailsClosed),
            ("wl bridge null classify fails closed", WlBridgeNullClassifyFailsClosed),
            ("wl bridge non-player alliance allows direct movement", WlBridgeNonPlayerAllianceAllowsDirectMovement),
            ("wl bridge report only under wl player alliance blocks movement", WlBridgeReportOnlyUnderWlPlayerAllianceBlocksMovement),
            ("wl bridge report only inactive stays not wl", WlBridgeReportOnlyInactiveStaysNotWl),
            ("wl bridge report only non-player alliance stays direct", WlBridgeReportOnlyNonPlayerAllianceStaysDirect),
            ("wl bridge player cic skips movement", WlBridgePlayerCicSkipsMovement),
            ("wl bridge moved by player skips movement", WlBridgeMovedByPlayerSkipsMovement),
            ("wl bridge eligible under commander issues current order", WlBridgeEligibleUnderCommanderIssuesCurrentOrder),
            ("wl bridge reinforce maps to redeploy order", WlBridgeReinforceMapsToRedeployOrder),
            ("wl bridge reinforce eligible under commander issues current order", WlBridgeReinforceEligibleUnderCommanderIssuesCurrentOrder),
            ("wl bridge ineligible under commander blocks direct fallback", WlBridgeIneligibleUnderCommanderBlocksDirectFallback),
            ("wl bridge failed vanilla call blocks direct fallback", WlBridgeFailedVanillaCallBlocksDirectFallback),
            ("wl bridge part of player unit blocks direct fallback", WlBridgePartOfPlayerUnitBlocksDirectFallback),
            ("wl chain player below map-group level: own command fixed, carrying group movable", WlChainPlayerBelowMapGroup),
            ("wl chain player whose command is a map group: order-only, never a committed defender", WlChainPlayerMapGroup),
            ("wl chain CIC player: Whiskey stands down for the side", WlChainCicPlayer),
            ("wl chain ordinary units are movable and count", WlChainOrdinaryUnits),
            ("wl bridge moves the AI group carrying a player below map-group level", WlBridgeMovesCarryingGroup),
            ("wl chain classification does no reflection lookups", WlChainClassificationDoesNoReflection),
            ("wl defense ledger never counts a player-controlled candidate (threat and guard pools)", WlDefenseLedgerSkipsPlayerControlled),
            ("wl camp short camp credits normal rest", WlCampShortCampCreditsNormalRest),
            ("wl camp short camp credits wounded rest", WlCampShortCampCreditsWoundedRest),
            ("wl camp short camp credits preserve minimum proportions", WlCampShortCampCreditsPreserveMinimumProportions),
            ("wl camp short camp enough time no correction", WlCampShortCampEnoughTimeNoCorrection),
            ("wl camp short camp zero minimum no correction", WlCampShortCampZeroMinimumNoCorrection),
            ("wl camp responsive bonus weights recent included station", WlCampResponsiveBonusWeightsRecentIncludedStation),
            ("wl camp responsive bonus includes companion recent average", WlCampResponsiveBonusIncludesCompanionRecentAverage),
            ("wl camp responsive bonus partial companion history divides by window", WlCampResponsiveBonusPartialCompanionHistoryDividesByWindow),
            ("wl camp responsive bonus excluded stations stay vanilla", WlCampResponsiveBonusExcludedStationsStayVanilla),
            ("wl camp responsive bonus use average false stays vanilla", WlCampResponsiveBonusUseAverageFalseStaysVanilla),
            ("wl camp responsive bonus nonfinite input stays bounded", WlCampResponsiveBonusNonfiniteInputStaysBounded),
            ("wl camp rest reward cap makes six hours full reward", WlCampRestRewardCapMakesSixHoursFullReward),
            ("wl camp rest reward cap leaves non rest stations vanilla", WlCampRestRewardCapLeavesNonRestStationsVanilla),
            ("wl camp rest reward cap invalid config falls back", WlCampRestRewardCapInvalidConfigFallsBack),
            ("wl camp unit divisor clamps invalid cached counts", WlCampUnitDivisorClampsInvalidCachedCounts),
            ("wl camp unit divisor default power softens four and nine units", WlCampUnitDivisorDefaultPowerSoftensFourAndNineUnits),
            ("wl camp unit modifier clamps negative to zero", WlCampUnitModifierClampsNegativeToZero),
            ("wl camp unit modifier nonfinite input falls back", WlCampUnitModifierNonfiniteInputFallsBack),
            ("wl camp unit power one is vanilla equivalent", WlCampUnitPowerOneIsVanillaEquivalent),
            ("wl camp unit payoff excluded or undivided returns vanilla", WlCampUnitPayoffExcludedOrUndividedReturnsVanilla),
            ("wl camp short camp nonfinite input no correction", WlCampShortCampNonfiniteInputNoCorrection),
        });
    }

    private static void WlOperationNullGuardFinishesMissingOperation()
    {
        AssertTrue(
            WlOperationNullGuard.ShouldFinishMissingOperation(
                modEnabled: true,
                operationExists: true,
                usedTopGroupExists: false),
            "missing operation unit should be finished before vanilla transform read");
        AssertTrue(
            !WlOperationNullGuard.ShouldFinishMissingOperation(
                modEnabled: true,
                operationExists: true,
                usedTopGroupExists: true),
            "valid operation unit should stay on vanilla path");
        AssertTrue(
            !WlOperationNullGuard.ShouldFinishMissingOperation(
                modEnabled: false,
                operationExists: true,
                usedTopGroupExists: false),
            "disabled mod should not patch operation cleanup");
    }

    private static void WlCareerStartGateDefersUntilCommandSelected()
    {
        AssertEqual(false, WlCareerStartGate.ShouldDeferStrategicReview(dlcScenarioActive: false, chosenCommanderId: -1, chosenCommanderHasCommand: false));
        AssertEqual(true, WlCareerStartGate.ShouldDeferStrategicReview(dlcScenarioActive: true, chosenCommanderId: -1, chosenCommanderHasCommand: false));
        AssertEqual(true, WlCareerStartGate.ShouldDeferStrategicReview(dlcScenarioActive: true, chosenCommanderId: 12, chosenCommanderHasCommand: false));
        AssertEqual(false, WlCareerStartGate.ShouldDeferStrategicReview(dlcScenarioActive: true, chosenCommanderId: 12, chosenCommanderHasCommand: true));
    }

    private static void WlDiaryStartupGateDefersUntilReady()
    {
        AssertEqual(
            false,
            WlCareerStartGate.ShouldSkipDiaryEventUpdate(
                dlcScenarioActive: false,
                frame: 50,
                chosenCommanderId: -1,
                chosenCommanderRecordReady: false,
                chosenCommanderHasCommand: false,
                diaryEventsReady: false,
                foodReady: false,
                cardinalPointsReady: false,
                weatherReady: false,
                updateCycle: 0),
            "non-W&L diary updates should remain vanilla-owned");

        AssertEqual(
            true,
            WlCareerStartGate.ShouldSkipDiaryEventUpdate(
                dlcScenarioActive: true,
                frame: 50,
                chosenCommanderId: 12,
                chosenCommanderRecordReady: true,
                chosenCommanderHasCommand: false,
                diaryEventsReady: true,
                foodReady: true,
                cardinalPointsReady: true,
                weatherReady: true,
                updateCycle: 0),
            "W&L diary updates should wait until the player has a command");

        AssertEqual(
            true,
            WlCareerStartGate.ShouldSkipDiaryEventUpdate(
                dlcScenarioActive: true,
                frame: 50,
                chosenCommanderId: 12,
                chosenCommanderRecordReady: true,
                chosenCommanderHasCommand: true,
                diaryEventsReady: false,
                foodReady: true,
                cardinalPointsReady: true,
                weatherReady: true,
                updateCycle: 0),
            "W&L diary updates should wait for imported diary events");

        AssertEqual(
            true,
            WlCareerStartGate.ShouldSkipDiaryEventUpdate(
                dlcScenarioActive: true,
                frame: 50,
                chosenCommanderId: 12,
                chosenCommanderRecordReady: true,
                chosenCommanderHasCommand: true,
                diaryEventsReady: true,
                foodReady: false,
                cardinalPointsReady: true,
                weatherReady: true,
                updateCycle: 3),
            "W&L diary updates should wait for food data before food-quality event cycles");

        AssertEqual(
            true,
            WlCareerStartGate.ShouldSkipDiaryEventUpdate(
                dlcScenarioActive: true,
                frame: 50,
                chosenCommanderId: 12,
                chosenCommanderRecordReady: true,
                chosenCommanderHasCommand: true,
                diaryEventsReady: true,
                foodReady: true,
                cardinalPointsReady: true,
                weatherReady: false,
                updateCycle: 1),
            "weather cycle should wait for WeatherObj");

        AssertEqual(
            false,
            WlCareerStartGate.ShouldSkipDiaryEventUpdate(
                dlcScenarioActive: true,
                frame: 50,
                chosenCommanderId: 12,
                chosenCommanderRecordReady: true,
                chosenCommanderHasCommand: true,
                diaryEventsReady: true,
                foodReady: true,
                cardinalPointsReady: true,
                weatherReady: false,
                updateCycle: 2),
            "non-weather cycles should not require WeatherObj once core dependencies are ready");
    }

    private static void WlDiaryStartupGateDefersUntilCampaignGroupLookupReady()
    {
        AssertEqual(
            true,
            WlCareerStartGate.ShouldSkipDiaryEventUpdate(
                dlcScenarioActive: true,
                frame: 50,
                chosenCommanderId: 12,
                chosenCommanderRecordReady: true,
                chosenCommanderHasCommand: true,
                diaryEventsReady: true,
                foodReady: true,
                cardinalPointsReady: true,
                weatherReady: true,
                updateCycle: 0,
                campaignGroupLookupReady: false),
            "W&L diary updates should wait until BattleUnits.GetCampaignGroup(currentcommand) can run safely");
    }

    private static void WlDiaryStartupGateDefersUntilCurrentCycleReady()
    {
        AssertEqual(
            true,
            WlCareerStartGate.ShouldSkipDiaryEventUpdate(
                dlcScenarioActive: true,
                frame: 50,
                chosenCommanderId: 12,
                chosenCommanderRecordReady: true,
                chosenCommanderHasCommand: true,
                diaryEventsReady: true,
                foodReady: true,
                cardinalPointsReady: true,
                weatherReady: true,
                updateCycle: 0,
                campaignGroupLookupReady: true,
                updateCycleReady: false),
            "W&L diary updates should defer when the active vanilla update cycle would dereference unsafe campaign data");
    }

    private static void WlDiaryCapitalDefenseCycleWaitsForAiFactionState()
    {
        AssertEqual(
            true,
            WlCareerStartGate.ShouldAllowCapitalDefenseDiaryCycle(
                campaignGroupPresent: false,
                aiFactionStateReady: false,
                capitalDefenseListReady: false,
                campaignGroupLookupReady: false),
            "vanilla cycle 7 can clear the diary event when the player has no campaign group");

        AssertEqual(
            false,
            WlCareerStartGate.ShouldAllowCapitalDefenseDiaryCycle(
                campaignGroupPresent: true,
                aiFactionStateReady: false,
                capitalDefenseListReady: false,
                campaignGroupLookupReady: true),
            "cycle 7 should wait until AICampaign.aifaction is built");

        AssertEqual(
            false,
            WlCareerStartGate.ShouldAllowCapitalDefenseDiaryCycle(
                campaignGroupPresent: true,
                aiFactionStateReady: true,
                capitalDefenseListReady: false,
                campaignGroupLookupReady: true),
            "cycle 7 should wait until AIFaction.groupstodefendcapital is built");

        AssertEqual(
            false,
            WlCareerStartGate.ShouldAllowCapitalDefenseDiaryCycle(
                campaignGroupPresent: true,
                aiFactionStateReady: true,
                capitalDefenseListReady: true,
                campaignGroupLookupReady: false),
            "cycle 7 should wait until vanilla's nested campaign-group lookup is safe");

        AssertEqual(
            true,
            WlCareerStartGate.ShouldAllowCapitalDefenseDiaryCycle(
                campaignGroupPresent: true,
                aiFactionStateReady: true,
                capitalDefenseListReady: true,
                campaignGroupLookupReady: true),
            "cycle 7 can run once the vanilla capital-defense membership inputs are ready");
    }

    private static void WlStartSelectionRetryDoesNotDependOnCampaignFrame()
    {
        var gate = new WlStartSelectionRetryGate(maxAttempts: 3, retryEveryUnityFrames: 15);

        AssertEqual(true, gate.ShouldAttempt(pending: true, listVisible: false, unityFrame: 1));
        AssertEqual(false, gate.ShouldAttempt(pending: true, listVisible: false, unityFrame: 10));
        AssertEqual(true, gate.ShouldAttempt(pending: true, listVisible: false, unityFrame: 16));
        AssertEqual(false, gate.ShouldAttempt(pending: true, listVisible: true, unityFrame: 31));
    }

    private static void WlStartSelectionRetryWaitsForPanel()
    {
        var gate = new WlStartSelectionRetryGate(maxAttempts: 3, retryEveryUnityFrames: 15);

        AssertEqual(false, gate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: false, unityFrame: 1));
        AssertEqual(0, gate.Attempts);
        AssertEqual(true, gate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: true, unityFrame: 1));
        AssertEqual(1, gate.Attempts);
    }

    private static void WlStartSelectionRetryWaitsForReadyFrame()
    {
        var gate = new WlStartSelectionRetryGate(maxAttempts: 3, retryEveryUnityFrames: 15, minReadyCampaignFrame: 50);

        AssertEqual(false, gate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: true, campaignFrame: 49, unityFrame: 1));
        AssertEqual(0, gate.Attempts);
        AssertEqual(true, gate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: true, campaignFrame: 50, unityFrame: 2));
        AssertEqual(1, gate.Attempts);
    }

    private static void WlStartSelectionRetryBlocksEarlyReadyData()
    {
        var gate = new WlStartSelectionRetryGate(maxAttempts: 3, retryEveryUnityFrames: 15, minReadyCampaignFrame: 50);

        AssertEqual(false, gate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: true, campaignFrame: 49, startupDataReady: false, unityFrame: 1));
        AssertEqual(0, gate.Attempts);
        AssertEqual(false, gate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: true, campaignFrame: 49, startupDataReady: true, unityFrame: 16));
        AssertEqual(0, gate.Attempts);
        AssertEqual(true, gate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: true, campaignFrame: 50, startupDataReady: true, unityFrame: 31));
        AssertEqual(1, gate.Attempts);
    }

    private static void WlDispatchSanitizerFixesType56StanceNone()
    {
        var result = WlDispatchSanitizer.Sanitize(56, "I will carry on according to your instructions that are to none.");
        AssertEqual("I will hold position and await further instructions.", result.Content);
        AssertEqual(true, result.Changed);
        AssertTrue(!result.Content.Contains("to none"), "type 56 sanitized content must not contain stance none fragment");
    }

    private static void WlDispatchSanitizerFixesType57StanceNone()
    {
        var result = WlDispatchSanitizer.Sanitize(57, "I will carry on according to your instructions that are to none.");
        AssertEqual("I will hold position and await further instructions.", result.Content);
        AssertEqual(true, result.Changed);
        AssertTrue(!result.Content.Contains("to none"), "type 57 sanitized content must not contain stance none fragment");
    }

    private static void WlDispatchSanitizerFixesType15NoOrdersNone()
    {
        AssertEqual(true, WlDispatchSanitizer.IsCandidateType(15));

        var result = WlDispatchSanitizer.Sanitize(15, "I will none if no other orders are received");
        AssertEqual("I will hold position if no other orders are received", result.Content);
        AssertEqual(true, result.Changed);
        AssertTrue(!result.Content.Contains("will none"), "type 15 sanitized content must not contain no-orders none fragment");
    }

    private static void WlDispatchSanitizerIgnoresNonCandidateType()
    {
        const string content = "I will carry on according to your instructions that are to none.";
        AssertEqual(false, WlDispatchSanitizer.IsCandidateType(99));

        var result = WlDispatchSanitizer.Sanitize(99, content);
        AssertEqual(content, result.Content);
        AssertEqual(false, result.Changed);
    }

    private static void WlDispatchSanitizerHandlesNullContent()
    {
        var result = WlDispatchSanitizer.Sanitize(56, null);
        AssertEqual<string>(null, result.Content);
        AssertEqual(false, result.Changed);
    }

    private static void WlDispatchSanitizerLeavesNormalContentUnchanged()
    {
        const string content = "I will advance at once.";
        var result = WlDispatchSanitizer.Sanitize(56, content);
        AssertEqual(content, result.Content);
        AssertEqual(false, result.Changed);
    }

    private static void WlBridgeInactiveAllowsDirectMovement()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.Redeploy,
            new WlStrategicRoleFacts { WlActive = false, IsPlayerAlliance = true });

        AssertEqual(WlStrategicOrderResult.NotWl, decision.Result);
        AssertEqual(5, decision.WlOrderType);
        AssertEqual(true, decision.MayDirectMove);
        AssertEqual(true, decision.MayMutateOperationList);
    }

    private static void WlBridgeNullTryIssueFailsClosed()
    {
        var decision = WlStrategicOrderBridge.TryIssue(null);
        AssertEqual(WlStrategicOrderResult.InvalidRequest, decision.Result);
        AssertEqual(false, decision.MayDirectMove);
        AssertEqual(false, decision.MayMutateOperationList);
    }

    private static void WlBridgeNullClassifyFailsClosed()
    {
        var decision = WlStrategicOrderBridge.ClassifyOnly(null);
        AssertEqual(WlStrategicOrderResult.InvalidRequest, decision.Result);
        AssertEqual(false, decision.MayDirectMove);
        AssertEqual(false, decision.MayMutateOperationList);
    }
    private static void WlBridgeNonPlayerAllianceAllowsDirectMovement()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.Redeploy,
            new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: false));

        AssertEqual(WlStrategicOrderResult.DirectMovementAllowed, decision.Result);
        AssertEqual(true, decision.MayDirectMove);
        AssertEqual(true, decision.MayMutateOperationList);
    }

    private static void WlBridgeReportOnlyUnderWlPlayerAllianceBlocksMovement()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.ReportOnly,
            new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true));

        AssertEqual(WlStrategicOrderResult.ReportOnly, decision.Result);
        AssertEqual(-1, decision.WlOrderType);
        AssertEqual(false, decision.MayDirectMove);
        AssertEqual(false, decision.MayMutateOperationList);
    }

    private static void WlBridgeReportOnlyInactiveStaysNotWl()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.ReportOnly,
            new WlStrategicRoleFacts(wlActive: false, isPlayerAlliance: true));

        AssertEqual(WlStrategicOrderResult.NotWl, decision.Result);
        AssertEqual(true, decision.MayDirectMove);
        AssertEqual(true, decision.MayMutateOperationList);
    }

    private static void WlBridgeReportOnlyNonPlayerAllianceStaysDirect()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.ReportOnly,
            new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: false));

        AssertEqual(WlStrategicOrderResult.DirectMovementAllowed, decision.Result);
        AssertEqual(true, decision.MayDirectMove);
        AssertEqual(true, decision.MayMutateOperationList);
    }

    private static void WlBridgePlayerCicSkipsMovement()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.Offensive,
            new WlStrategicRoleFacts { WlActive = true, IsPlayerAlliance = true, IsPlayerCic = true });

        AssertEqual(WlStrategicOrderResult.SkippedPlayerCic, decision.Result);
        AssertEqual(16, decision.WlOrderType);
        AssertEqual(false, decision.MayDirectMove);
        AssertEqual(false, decision.MayMutateOperationList);
    }

    private static void WlBridgeMovedByPlayerSkipsMovement()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.Probe,
            new WlStrategicRoleFacts { WlActive = true, IsPlayerAlliance = true, IsMovedByPlayer = true });

        AssertEqual(WlStrategicOrderResult.SkippedPlayerControlled, decision.Result);
        AssertEqual(5, decision.WlOrderType);
        AssertEqual(false, decision.MayDirectMove);
        AssertEqual(false, decision.MayMutateOperationList);
    }

    private static void WlBridgeEligibleUnderCommanderIssuesCurrentOrder()
    {
        var facts = new WlStrategicRoleFacts
        {
            WlActive = true,
            IsPlayerAlliance = true,
            IsUnderCommander = true,
            CurrentCommandIsCampaignGroup = true,
            CurrentCommandParentIsUnderTargetUnit = true
        };

        var decision = WlStrategicOrderBridge.Classify(WlStrategicIntent.OffensiveContinuation, facts);

        AssertEqual(WlStrategicOrderResult.IssuedWlCurrentOrder, decision.Result);
        AssertEqual(6, decision.WlOrderType);
        AssertEqual(false, decision.MayDirectMove);
        AssertEqual(false, decision.MayMutateOperationList);
    }

    private static void WlBridgeReinforceMapsToRedeployOrder()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.Reinforce,
            new WlStrategicRoleFacts(wlActive: false, isPlayerAlliance: true));

        AssertEqual(WlStrategicOrderResult.NotWl, decision.Result);
        AssertEqual(5, decision.WlOrderType);
        AssertEqual(true, decision.MayDirectMove);
    }

    private static void WlBridgeReinforceEligibleUnderCommanderIssuesCurrentOrder()
    {
        var facts = new WlStrategicRoleFacts
        {
            WlActive = true,
            IsPlayerAlliance = true,
            IsUnderCommander = true,
            CurrentCommandIsCampaignGroup = true,
            CurrentCommandParentIsUnderTargetUnit = true
        };

        var decision = WlStrategicOrderBridge.Classify(WlStrategicIntent.Reinforce, facts);

        AssertEqual(WlStrategicOrderResult.IssuedWlCurrentOrder, decision.Result);
        AssertEqual(5, decision.WlOrderType);
        AssertEqual(false, decision.MayDirectMove);
    }

    private static void WlBridgeIneligibleUnderCommanderBlocksDirectFallback()
    {
        var facts = new WlStrategicRoleFacts
        {
            WlActive = true,
            IsPlayerAlliance = true,
            IsUnderCommander = true,
            CurrentCommandIsCampaignGroup = false,
            CurrentCommandParentIsUnderTargetUnit = true
        };

        var decision = WlStrategicOrderBridge.Classify(WlStrategicIntent.EngageEnemy, facts);

        AssertEqual(WlStrategicOrderResult.WlCurrentOrderIneligible, decision.Result);
        AssertEqual(7, decision.WlOrderType);
        AssertEqual(false, decision.MayDirectMove);
        AssertEqual(false, decision.MayMutateOperationList);
    }

    private static void WlBridgeFailedVanillaCallBlocksDirectFallback()
    {
        var facts = new WlStrategicRoleFacts
        {
            WlActive = true,
            IsPlayerAlliance = true,
            IsUnderCommander = true,
            CurrentCommandIsCampaignGroup = true,
            CurrentCommandParentIsUnderTargetUnit = true
        };

        var decision = WlStrategicOrderBridge.Classify(WlStrategicIntent.DefendCapital, facts, vanillaBridgeSucceeded: false);

        AssertEqual(WlStrategicOrderResult.FailedVanillaBridge, decision.Result);
        AssertEqual(8, decision.WlOrderType);
        AssertEqual(false, decision.MayDirectMove);
        AssertEqual(false, decision.MayMutateOperationList);
    }

    private static void WlBridgePartOfPlayerUnitBlocksDirectFallback()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.ConstructFort,
            new WlStrategicRoleFacts
            {
                WlActive = true,
                IsPlayerAlliance = true,
                IsPartOfPlayerUnit = true
            });

        AssertEqual(WlStrategicOrderResult.WlCurrentOrderIneligible, decision.Result);
        AssertEqual(9, decision.WlOrderType);
        AssertEqual(false, decision.MayDirectMove);
        AssertEqual(false, decision.MayMutateOperationList);
        AssertTrue(decision.Reason.Contains("part-of-player-unit"), "blocked reason should name part-of-player-unit");
    }

    private static void WlCampShortCampCreditsNormalRest()
    {
        var corrected = new float[1];
        float minimumTotal;
        bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
            2f, new[] { 3f }, corrected, out minimumTotal);
        AssertTrue(changed, "expected correction for 2h actual below 3h minimum");
        AssertNear(3f, minimumTotal, 0.0001f, "minimum total");
        AssertNear(2f, corrected[0], 0.0001f, "rest credit");
    }

    private static void WlCampShortCampCreditsWoundedRest()
    {
        var corrected = new float[1];
        float minimumTotal;
        bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
            2f, new[] { 9f }, corrected, out minimumTotal);
        AssertTrue(changed, "expected correction for 2h actual below 9h wounded rest minimum");
        AssertNear(9f, minimumTotal, 0.0001f, "minimum total");
        AssertNear(2f, corrected[0], 0.0001f, "wounded rest credit");
    }

    private static void WlCampShortCampCreditsPreserveMinimumProportions()
    {
        var corrected = new float[4];
        float minimumTotal;
        bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
            3f, new[] { 3f, 1f, 2f, 0f }, corrected, out minimumTotal);
        AssertTrue(changed, "expected correction for 3h actual below 6h minimum");
        AssertNear(6f, minimumTotal, 0.0001f, "minimum total");
        AssertNear(1.5f, corrected[0], 0.0001f, "station 0");
        AssertNear(0.5f, corrected[1], 0.0001f, "station 1");
        AssertNear(1.0f, corrected[2], 0.0001f, "station 2");
        AssertNear(0f, corrected[3], 0.0001f, "station 3");
        AssertNear(3f, corrected[0] + corrected[1] + corrected[2] + corrected[3], 0.0001f, "sum");
    }

    private static void WlCampShortCampEnoughTimeNoCorrection()
    {
        var corrected = new[] { -99f };
        float minimumTotal;
        bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
            3f, new[] { 3f }, corrected, out minimumTotal);
        AssertTrue(!changed, "expected no correction when actual covers minimum");
        AssertNear(3f, minimumTotal, 0.0001f, "minimum total");
        AssertNear(-99f, corrected[0], 0.0001f, "sentinel unchanged");
    }

    private static void WlCampShortCampZeroMinimumNoCorrection()
    {
        var corrected = new[] { -99f, -88f };
        float minimumTotal;
        bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
            2f, new[] { 0f, 0f }, corrected, out minimumTotal);
        AssertTrue(!changed, "expected no correction when minimum total is zero");
        AssertNear(0f, minimumTotal, 0.0001f, "minimum total");
        AssertNear(-99f, corrected[0], 0.0001f, "sentinel 0 unchanged");
        AssertNear(-88f, corrected[1], 0.0001f, "sentinel 1 unchanged");
    }

    private static void WlCampResponsiveBonusWeightsRecentIncludedStation()
    {
        float vanilla = (56f / 30f - 3f) / 5f;
        float result = WlCampRealism.ComputeResponsiveBonus(
            6, true, vanilla, 56f / 30f, 0f,
            new[] { 8f, 8f, 8f, 8f, 8f, 8f, 8f },
            new float[0],
            3f, 8f, 7, 0.35f);
        AssertTrue(result > vanilla, "responsive bonus should exceed long-average vanilla");
        AssertNear(0.202666f, result, 0.0005f, "responsive bonus");
    }

    private static void WlCampResponsiveBonusIncludesCompanionRecentAverage()
    {
        float vanilla = 0f;
        float result = WlCampRealism.ComputeResponsiveBonus(
            1, true, vanilla, 2f, 0f,
            new[] { 2f, 2f, 2f, 2f, 2f, 2f, 2f },
            new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f },
            2f, 6f, 7, 0.35f);
        AssertTrue(result > vanilla, "recent companion time should lift bonus");
        AssertNear(0.0875f, result, 0.0005f, "companion responsive bonus");
    }

    private static void WlCampResponsiveBonusPartialCompanionHistoryDividesByWindow()
    {
        float result = WlCampRealism.ComputeResponsiveBonus(
            1, true, 0f, 2f, 0f,
            new float[0],
            new[] { 4f },
            2f, 6f, 4, 0.5f);
        AssertNear(-0.125f, result, 0.0005f, "partial companion history responsive bonus");
    }

    private static void WlCampResponsiveBonusExcludedStationsStayVanilla()
    {
        foreach (var stationId in new[] { 2, 5, 9, 12 })
        {
            float result = WlCampRealism.ComputeResponsiveBonus(
                stationId, true, 0.42f, 0f, 0f,
                new[] { 99f, 99f, 99f, 99f, 99f, 99f, 99f },
                new[] { 99f, 99f, 99f, 99f, 99f, 99f, 99f },
                0f, 1f, 7, 0.35f);
            AssertNear(0.42f, result, 0.0001f, "excluded station " + stationId);
        }
    }

    private static void WlCampResponsiveBonusUseAverageFalseStaysVanilla()
    {
        float result = WlCampRealism.ComputeResponsiveBonus(
            6, false, -0.25f, 0f, 0f,
            new[] { 99f, 99f, 99f, 99f, 99f, 99f, 99f },
            new float[0],
            0f, 1f, 7, 0.35f);
        AssertNear(-0.25f, result, 0.0001f, "useaverage=false should stay vanilla");
    }

    private static void WlCampResponsiveBonusNonfiniteInputStaysBounded()
    {
        float result = WlCampRealism.ComputeResponsiveBonus(
            6, true, float.PositiveInfinity, float.NaN, float.NegativeInfinity,
            new[] { 8f, float.NaN, float.PositiveInfinity },
            new[] { float.NegativeInfinity, 4f },
            float.NaN, float.PositiveInfinity, 7, float.NaN);
        AssertNear(0f, result, 0.0001f, "nonfinite responsive bonus fallback");
    }

    private static void WlCampRestRewardCapMakesSixHoursFullReward()
    {
        AssertTrue(WlCampRealism.UsesRestRewardCap(12), "station 12 should be Rest");
        AssertNear(3f, WlCampRealism.DefaultRestNeutralHours, 0.0001f, "default rest neutral");
        AssertNear(6f, WlCampRealism.DefaultRestMaxRewardHours, 0.0001f, "default rest max reward");
        float resultAtSix = WlCampRealism.ComputeRestRewardBonus(
            12, 0f, 6f, 0f,
            6f, 9f, WlCampRealism.DefaultRestNeutralHours, WlCampRealism.DefaultRestMaxRewardHours);
        float resultAtFourAndHalf = WlCampRealism.ComputeRestRewardBonus(
            12, 0f, 4.5f, 0f,
            6f, 9f, WlCampRealism.DefaultRestNeutralHours, WlCampRealism.DefaultRestMaxRewardHours);

        AssertNear(1f, resultAtSix, 0.0001f, "six-hour rest bonus");
        AssertNear(0.5f, resultAtFourAndHalf, 0.0001f, "halfway rest bonus");
    }

    private static void WlCampRestRewardCapLeavesNonRestStationsVanilla()
    {
        AssertTrue(!WlCampRealism.UsesRestRewardCap(6), "station 6 should not be Rest");
        float result = WlCampRealism.ComputeRestRewardBonus(
            6, 0.42f, 5f, 0f,
            1.75f, 5f, 3f, 5f);
        AssertNear(0.42f, result, 0.0001f, "non-rest vanilla fallback");
    }

    private static void WlCampRestRewardCapInvalidConfigFallsBack()
    {
        float result = WlCampRealism.ComputeRestRewardBonus(
            12, -0.333333f, 5f, 0f,
            6f, 9f, 5f, 5f);
        AssertNear(-0.333333f, result, 0.0001f, "invalid neutral/max fallback");
    }

    private static void WlCampUnitDivisorClampsInvalidCachedCounts()
    {
        AssertNear(1f, WlCampRealism.EffectiveCommandedUnitDivisor(-1, 0.5f), 0.0001f, "count -1");
        AssertNear(1f, WlCampRealism.EffectiveCommandedUnitDivisor(0, 0.5f), 0.0001f, "count 0");
        AssertNear(1f, WlCampRealism.EffectiveCommandedUnitDivisor(1, 0.5f), 0.0001f, "count 1");
    }

    private static void WlCampUnitDivisorDefaultPowerSoftensFourAndNineUnits()
    {
        AssertNear(2f, WlCampRealism.EffectiveCommandedUnitDivisor(4, 0.5f), 0.0001f, "count 4 divisor");
        AssertNear(3f, WlCampRealism.EffectiveCommandedUnitDivisor(9, 0.5f), 0.0001f, "count 9 divisor");
        AssertNear(1.5f, WlCampRealism.ComputeUnitPayoffModifier(6, true, 1.25f, 1f, 1f, 4, 0.5f), 0.0001f, "count 4 modifier");
        AssertNear(1.333333f, WlCampRealism.ComputeUnitPayoffModifier(6, true, 1.111f, 1f, 1f, 9, 0.5f), 0.0005f, "count 9 modifier");
    }

    private static void WlCampUnitModifierClampsNegativeToZero()
    {
        float result = WlCampRealism.ComputeUnitPayoffModifier(7, true, 1f, -1f, 1000f, 1, 0.5f);
        AssertNear(0f, result, 0.0001f, "negative modifier clamp");
    }

    private static void WlCampUnitModifierNonfiniteInputFallsBack()
    {
        AssertNear(0.66f, WlCampRealism.ComputeUnitPayoffModifier(7, true, 0.66f, float.NaN, 1f, 4, 0.5f), 0.0001f, "nan bonus fallback");
        AssertNear(0f, WlCampRealism.ComputeUnitPayoffModifier(7, true, float.PositiveInfinity, 1f, 1f, 4, 0.5f), 0.0001f, "nonfinite vanilla fallback");
        AssertNear(0.66f, WlCampRealism.ComputeUnitPayoffModifier(7, true, 0.66f, 1f, float.PositiveInfinity, 4, 0.5f), 0.0001f, "infinite max fallback");
    }

    private static void WlCampUnitPowerOneIsVanillaEquivalent()
    {
        AssertNear(9f, WlCampRealism.EffectiveCommandedUnitDivisor(9, 1.0f), 0.0001f, "power one divisor");
        float result = WlCampRealism.ComputeUnitPayoffModifier(8, true, 0f, 1f, 1f, 9, 1.0f);
        AssertNear(1.111111f, result, 0.0005f, "power one modifier");
    }

    private static void WlCampUnitPayoffExcludedOrUndividedReturnsVanilla()
    {
        AssertNear(0.77f, WlCampRealism.ComputeUnitPayoffModifier(5, true, 0.77f, 1f, 1f, 9, 0.5f), 0.0001f, "station 5 excluded");
        AssertNear(0.77f, WlCampRealism.ComputeUnitPayoffModifier(9, true, 0.77f, 1f, 1f, 9, 0.5f), 0.0001f, "station 9 excluded");
        AssertNear(0.77f, WlCampRealism.ComputeUnitPayoffModifier(12, true, 0.77f, 1f, 1f, 9, 0.5f), 0.0001f, "station 12 excluded");
        AssertNear(0.77f, WlCampRealism.ComputeUnitPayoffModifier(6, false, 0.77f, 1f, 1f, 9, 0.5f), 0.0001f, "undivided included station");
    }

    private static void WlCampShortCampNonfiniteInputNoCorrection()
    {
        var corrected = new[] { -99f, -88f };
        float minimumTotal;
        bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
            float.NaN, new[] { 3f, float.PositiveInfinity }, corrected, out minimumTotal);
        AssertTrue(!changed, "expected no correction for nonfinite actual camp hours");
        AssertNear(3f, minimumTotal, 0.0001f, "finite minimum total");
        AssertNear(-99f, corrected[0], 0.0001f, "sentinel 0 unchanged");
        AssertNear(-88f, corrected[1], 0.0001f, "sentinel 1 unchanged");
    }

    // Stage G decision C4 (stage F extras 1-4): one classification behind the
    // bridge, DefensiveOpsPatch's pre-pledge, #25's revert, the probe stop, the
    // coastal runner and defense coverage.
    private static void WlChainPlayerBelowMapGroup()
    {
        // The player's brigade (player-owned, its command is not a map group).
        var own = new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true, isUnderCommander: true, isPartOfPlayerUnit: true);
        AssertEqual(WlChainClass.PlayerCommand, WlChainRules.Classify(in own));
        AssertFalse(WlChainRules.MayMove(WlChainClass.PlayerCommand), "own command never moved");
        AssertFalse(WlChainRules.CountsAsCommittedDefender(WlChainClass.PlayerCommand), "own command not a defender");
        AssertFalse(WlChainRules.OrderOnly(WlChainClass.PlayerCommand), "vanilla gives it no campaign order (8366)");

        // The AI corps carrying him (IsPlayerPartOfUnit: the campaign group of his command).
        var carrying = new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true, isPartOfPlayerUnit: true, isPlayerCampaignGroup: true);
        AssertEqual(WlChainClass.CarryingGroup, WlChainRules.Classify(in carrying));
        AssertTrue(WlChainRules.MayMove(WlChainClass.CarryingGroup), "movable AI asset, as vanilla moves it");
        AssertTrue(WlChainRules.CountsAsCommittedDefender(WlChainClass.CarryingGroup), "an AI corps counts");

        // A hidden sub-unit inside that corps is still part of the player's chain.
        var inside = new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true, isPartOfPlayerUnit: true);
        AssertEqual(WlChainClass.PlayerCommand, WlChainRules.Classify(in inside));

        // A unit in the player's operation.
        var operation = new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true, isMovedByPlayer: true, isPartOfPlayerUnit: true, isPlayerCampaignGroup: true);
        AssertEqual(WlChainClass.PlayerCommand, WlChainRules.Classify(in operation));
    }

    private static void WlChainPlayerMapGroup()
    {
        var mapGroup = new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true, isUnderCommander: true,
            isPartOfPlayerUnit: true, currentCommandIsCampaignGroup: true, currentCommandParentIsUnderTargetUnit: true, isPlayerCampaignGroup: true);
        AssertEqual(WlChainClass.PlayerMapGroup, WlChainRules.Classify(in mapGroup));
        AssertFalse(WlChainRules.MayMove(WlChainClass.PlayerMapGroup), "never moved");
        AssertTrue(WlChainRules.OrderOnly(WlChainClass.PlayerMapGroup), "addressed by dispatch");
        AssertFalse(WlChainRules.CountsAsCommittedDefender(WlChainClass.PlayerMapGroup), "unreliable executor");
        AssertEqual(WlStrategicOrderResult.IssuedWlCurrentOrder, WlStrategicOrderBridge.Classify(WlStrategicIntent.DefendCapital, mapGroup).Result);

        // Not his own map group: nothing carries him, so no unit is a carrying group.
        var notCarrying = new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true, isPartOfPlayerUnit: true,
            currentCommandIsCampaignGroup: true, isPlayerCampaignGroup: true);
        AssertEqual(WlChainClass.PlayerCommand, WlChainRules.Classify(in notCarrying));
    }

    private static void WlChainCicPlayer()
    {
        var cic = new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true, isPlayerCic: true, isPartOfPlayerUnit: true, isPlayerCampaignGroup: true);
        AssertEqual(WlChainClass.CicSide, WlChainRules.Classify(in cic));
        AssertFalse(WlChainRules.MayMove(WlChainClass.CicSide), "stand down");
        AssertFalse(WlChainRules.CountsAsCommittedDefender(WlChainClass.CicSide), "stand down");
        AssertFalse(WlChainRules.OrderOnly(WlChainClass.CicSide), "no orders either");
    }

    private static void WlChainOrdinaryUnits()
    {
        AssertEqual(WlChainClass.None, WlChainRules.Classify(new WlStrategicRoleFacts(wlActive: false, isPlayerAlliance: true, isUnderCommander: true)));
        AssertEqual(WlChainClass.None, WlChainRules.Classify(new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: false, isPlayerCic: true)));
        AssertEqual(WlChainClass.None, WlChainRules.Classify(new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true)));
        AssertTrue(WlChainRules.MayMove(WlChainClass.None), "AI unit movable");
        AssertTrue(WlChainRules.CountsAsCommittedDefender(WlChainClass.None), "AI unit counts");
    }

    private static void WlBridgeMovesCarryingGroup()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.Offensive,
            new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true, isPartOfPlayerUnit: true, isPlayerCampaignGroup: true));
        AssertEqual(WlStrategicOrderResult.DirectMovementAllowed, decision.Result);
        AssertTrue(decision.MayDirectMove, "vanilla-style move; #69 dispatches it");
        AssertTrue(decision.MayMutateOperationList, "listed like any AI group");
        AssertEqual("carrying-group", decision.Reason);
    }

    // F-extra-4: DefenseIntentRuntime marks the player's command and map group
    // PlayerControlled (WlChainRules.CountsAsCommittedDefender); the ledger keeps
    // them out of both pools (DefenseIntentLedger threat pre-suppression and
    // FilterGuardCandidates), so no must-hold coverage rests on the player.
    private static void WlDefenseLedgerSkipsPlayerControlled()
    {
        var threat = new DefenseIntentInput { AllianceId = 1, TotalAllianceEffectiveStrength = 60000f };
        threat.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion, InvasionForceInstanceId = 30, SpotName = "spot",
            SourcePortName = "port", LandedSignal = true, AssetName = "harbor",
            AssetRole = AssetStrategicRole.BlockadeRunnerPort, EnemyStrength = 6000f
        });
        threat.Candidates.Add(PlayerMapGroupCandidate());
        threat.Candidates.Add(AiCandidate());
        AssertPlayerNeverSelected(DefenseIntentLedger.Build(threat), "threat pool");

        var guard = new DefenseIntentInput { AllianceId = 1, TotalAllianceEffectiveStrength = 60000f };
        guard.GuardCandidateAssets.Add(new CampaignMapAsset
        {
            Kind = CampaignMapAssetKind.SeaHarbor, Name = "harbor", Owner = 1, StrategicRole = AssetStrategicRole.BlockadeRunnerPort
        });
        guard.Candidates.Add(PlayerMapGroupCandidate());
        guard.Candidates.Add(new DefenseCandidate { UnitInstanceId = 2, ActiveStrength = 2500f, Morale = 0.9f, ReadinessStep = 2f, Tier = CandidateTier.Local, DistanceToThreat = 50f });
        var output = DefenseIntentLedger.Build(guard);
        AssertTrue(output.Responses.Exists(r => r.SelectedPackage.Exists(c => c.UnitInstanceId == 2)), "guard pool picks the AI unit");
        AssertPlayerNeverSelected(output, "guard pool");
    }

    private static DefenseCandidate PlayerMapGroupCandidate()
    {
        return new DefenseCandidate { UnitInstanceId = 1, ActiveStrength = 20000f, Morale = 1f, ReadinessStep = 2f, Tier = CandidateTier.Local, DistanceToThreat = 10f, PlayerControlled = true };
    }

    private static DefenseCandidate AiCandidate()
    {
        return new DefenseCandidate { UnitInstanceId = 2, ActiveStrength = 8000f, Morale = 0.9f, ReadinessStep = 2f, Tier = CandidateTier.Local, DistanceToThreat = 50f };
    }

    private static void AssertPlayerNeverSelected(DefenseIntentLedgerOutput output, string pool)
    {
        AssertTrue(output.Responses.Count >= 1, pool + ": a response");
        AssertFalse(output.Responses.Exists(r => r.SelectedPackage.Exists(c => c.UnitInstanceId == 1)), pool + ": the player's map group is no committed defender");
    }

    // F1 (verify round 2): ChainClassOf runs per unit in DefenseIntentRuntime,
    // DefensiveOpsPatch, #25 and the coastal runner. AccessTools.TypeByName misses
    // Type.GetType and scans every loaded type (~6 ms), so neither the bridge nor
    // StrategicCoordinator.IsPlayerCICOf (every Whiskey CIC gate) may reflect, and
    // the loops capture the player-level context once per pass.
    private static void WlChainClassificationDoesNoReflection()
    {
        string bridge = File.ReadAllText(FindRepoPath("src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs"));
        foreach (string banned in new[] { "TypeByName", "GetMethod(", ".Invoke(", "AccessTools", "System.Reflection" })
            AssertFalse(bridge.Contains(banned), "bridge uses " + banned);

        string coordinator = File.ReadAllText(FindRepoPath("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs"));
        int start = coordinator.IndexOf("public static bool IsPlayerCICOf(", StringComparison.Ordinal);
        AssertTrue(start >= 0, "IsPlayerCICOf found");
        int end = coordinator.IndexOf("\n        }\n", start, StringComparison.Ordinal);
        string body = coordinator.Substring(start, end - start);
        foreach (string banned in new[] { "TypeByName", "AccessTools", ".Invoke(" })
            AssertFalse(body.Contains(banned), "IsPlayerCICOf uses " + banned);

        foreach (string loop in new[]
        {
            "src/WhiskeyRealism/Strategic/DefenseIntentRuntime.cs",
            "src/WhiskeyRealism/Patches/DefensiveOpsPatch.cs",
            "src/WhiskeyRealism/Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs",
            "src/WhiskeyRealism/Strategic/CoastalDefenseCustomOrderRunner.cs",
        })
        {
            string text = File.ReadAllText(FindRepoPath(loop));
            AssertTrue(text.Contains("WlChainContext.Capture()"), loop + " captures the context once");
            AssertTrue(text.Contains("ChainClassOf(in "), loop + " classifies with the captured context");
        }
    }
}
