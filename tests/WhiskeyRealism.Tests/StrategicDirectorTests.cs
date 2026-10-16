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

internal static class StrategicDirectorTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("hold source blocks transfer", HoldSourceBlocksTransfer),
            ("economy source allows concession transfer", EconomySourceAllowsConcessionTransfer),
            ("economy alliance data guard suppresses only null references", EconomyAllianceDataGuardSuppressesOnlyNullReferences),
            ("fast forward scheduler keeps 5x vanilla only", FastForwardSchedulerKeepsFiveXVanillaOnly),
            ("fast forward scheduler boosts high speeds within cap", FastForwardSchedulerBoostsHighSpeedsWithinCap),
            ("fast forward scheduler disables cleanly", FastForwardSchedulerDisablesCleanly),
            ("fast forward scheduler stops when frame budget is spent", FastForwardSchedulerStopsWhenFrameBudgetIsSpent),
            ("fast forward scheduler throttles after slow frames", FastForwardSchedulerThrottlesAfterSlowFrames),
            ("fast forward scheduler cooldown expires by frame", FastForwardSchedulerCooldownExpiresByFrame),
            ("campaign ai governor skips paused vanilla update", CampaignAiGovernorSkipsPausedVanillaUpdate),
            ("commander assignment guard clears stale previous command", CommanderAssignmentGuardClearsStalePreviousCommand),
            ("campaign filter map guard bounds repeated no progress", CampaignFilterMapGuardBoundsRepeatedNoProgress),
            ("campaign filter map guard detects assign-filters bootstrap needs", CampaignFilterMapGuardDetectsAssignFiltersBootstrapNeeds),
            ("state handover guard requires decisive support", StateHandoverGuardRequiresDecisiveSupport),
            ("fleet patrol guard resets completed AI patrol", FleetPatrolGuardResetsCompletedAiPatrol),
            ("fast forward log gate suppresses repeated samples", FastForwardLogGateSuppressesRepeatedSamples),
            ("campaign pace too fast collapse on early national morale crash", CampaignPaceTooFastCollapseOnEarlyMoraleCrash),
            ("campaign pace late war pressure on chapter three", CampaignPaceLateWarPressureOnChapterThree),
            ("campaign pace union in 1864 before chapter three is not late war", CampaignPaceUnion1864BeforeChapterThreeIsNotLateWar),
            ("campaign pace csa collapse floor stays on the calendar after a lost election", CampaignPaceCsaFloorIsCalendarAtChapterTwo),
            ("campaign pace too fast collapse is calendar not chapter", CampaignPaceTooFastCollapseIsCalendarNotChapter),
            ("campaign pace overheated on heavy 14-day battle volume", CampaignPaceOverheatedOnHeavy14DayBattles),
            ("campaign pace too quiet only outside chapter one winter", CampaignPaceTooQuietSuppressedInChapterOneWinter),
            ("campaign pace stalemated when chapter two front static", CampaignPaceStalematedWhenChapterTwoFrontStatic),
            ("campaign pace stable default", CampaignPaceStableDefault),
            ("director cannot publish preserve for late csa under elevated risk", DirectorCannotPublishPreserveForLateCsaUnderElevatedRisk),
            ("campaign pace publishes theater priority from highest pressure theater", CampaignPacePublishesTheaterPriorityFromHighestPressureTheater),
            ("director maps overheated pace to recover-leaning intent", DirectorMapsOverheatedToRecoverLeaning),
            ("director blocks preserve intent for late csa under elevated risk", DirectorBlocksPreserveForLateCsaUnderElevatedRisk),
            ("director memory round trips through dto", DirectorMemoryRoundTripsThroughDto),
            ("director publish clamp suppresses second publish on the same game day", DirectorPublishClampSuppressesSecondPublishOnSameGameDay),
            ("director raises csa hold ratio under too fast collapse", DirectorRaisesCsaHoldRatioUnderTooFastCollapse),
            ("director raises recover floor under overheated", DirectorRaisesRecoverFloorUnderOverheated),
            ("director relaxes union mass ratio under late war pressure", DirectorRelaxesUnionMassRatioUnderLateWarPressure),
            ("director critical risk strongly favors supply construction", DirectorCriticalRiskFavorsSupplyConstruction),
            ("director too quiet healthy fiscal favors logistics", DirectorTooQuietFavorsLogistics),
            ("director too fast collapse damps expansion", DirectorTooFastCollapseDampsExpansion),
            ("director lowers union guard budget under late war pressure", DirectorLowersUnionGuardUnderLateWarPressure),
        });
    }

    private static void HoldSourceBlocksTransfer()
    {
        var ledger = BuildLedger();
        var decision = ledger.EvaluateTransfer("Richmond", "Vicksburg", 2000f);
        AssertEqual(false, decision.Allowed);
        AssertEqual(TransferBudgetAction.Blocked, decision.Action);
    }

    private static void EconomySourceAllowsConcessionTransfer()
    {
        var ledger = BuildLedger();
        var decision = ledger.EvaluateTransfer("Coast", "Vicksburg", 1000f);
        AssertEqual(true, decision.Allowed);
        AssertEqual(TransferBudgetAction.Concession, decision.Action);
    }

    private static void EconomyAllianceDataGuardSuppressesOnlyNullReferences()
    {
        AssertEqual(true, EconomyAllianceDataGuard.ShouldSuppress(new NullReferenceException()));
        AssertEqual(false, EconomyAllianceDataGuard.ShouldSuppress(new InvalidOperationException()));
        AssertEqual(false, EconomyAllianceDataGuard.ShouldSuppress(null));
    }

    private static void FastForwardSchedulerKeepsFiveXVanillaOnly()
    {
        var options = new FastForwardAiOptions();
        AssertEqual(1, FastForwardAiScheduler.VanillaPasses(1f));
        AssertEqual(2, FastForwardAiScheduler.VanillaPasses(5f));
        AssertEqual(0, FastForwardAiScheduler.MaxExtraPasses(5f, options));
    }

    private static void FastForwardSchedulerBoostsHighSpeedsWithinCap()
    {
        var options = new FastForwardAiOptions();
        AssertEqual(4, FastForwardAiScheduler.VanillaPasses(20f));
        AssertEqual(7, FastForwardAiScheduler.VanillaPasses(50f));
        AssertEqual(2, FastForwardAiScheduler.MaxExtraPasses(20f, options));
        AssertEqual(4, FastForwardAiScheduler.MaxExtraPasses(50f, options));
    }

    private static void FastForwardSchedulerDisablesCleanly()
    {
        var options = new FastForwardAiOptions { Enabled = false };
        AssertEqual(0, FastForwardAiScheduler.MaxExtraPasses(50f, options));
        AssertEqual(false, FastForwardAiScheduler.ShouldRunExtraPass(0, 0.1f, 50f, options));
    }

    private static void FastForwardSchedulerStopsWhenFrameBudgetIsSpent()
    {
        var options = new FastForwardAiOptions { MaxExtraPassesAt50x = 4, FrameBudgetMs = 1.5f };
        AssertEqual(true, FastForwardAiScheduler.ShouldRunExtraPass(3, 1.49f, 50f, options));
        AssertEqual(false, FastForwardAiScheduler.ShouldRunExtraPass(4, 1.49f, 50f, options));
        AssertEqual(false, FastForwardAiScheduler.ShouldRunExtraPass(3, 1.5f, 50f, options));
    }

    private static void FastForwardSchedulerThrottlesAfterSlowFrames()
    {
        var options = new FastForwardAiOptions
        {
            SlowFrameThresholdMs = 8f,
            SlowFrameCooldownFrames = 180
        };

        AssertEqual(false, FastForwardAiScheduler.ShouldThrottleAfterFrame(3.5f, 0f, options));
        AssertEqual(true, FastForwardAiScheduler.ShouldThrottleAfterFrame(8f, 0f, options));
        AssertEqual(true, FastForwardAiScheduler.ShouldThrottleAfterFrame(1f, 9f, options));
        AssertEqual(1180, FastForwardAiScheduler.CooldownUntilFrame(1000, options));
    }

    private static void FastForwardSchedulerCooldownExpiresByFrame()
    {
        AssertEqual(true, FastForwardAiScheduler.InCooldown(1179, 1180));
        AssertEqual(false, FastForwardAiScheduler.InCooldown(1180, 1180));
        AssertEqual(false, FastForwardAiScheduler.InCooldown(1181, 1180));
    }

    private static void CampaignAiGovernorSkipsPausedVanillaUpdate()
    {
        AssertEqual(true, FastForwardAiScheduler.ShouldSkipCampaignAiUpdate(gamePaused: true, gameSpeed: 1f));
        AssertEqual(true, FastForwardAiScheduler.ShouldSkipCampaignAiUpdate(gamePaused: false, gameSpeed: 0f));
        AssertEqual(false, FastForwardAiScheduler.ShouldSkipCampaignAiUpdate(gamePaused: false, gameSpeed: 1f));
    }

    private static void FastForwardLogGateSuppressesRepeatedSamples()
    {
        var gate = new FastForwardAiLogGate();
        string first = FastForwardAiScheduler.LogSignature(50f, 7, 4, 4, budgetExhausted: true);
        string repeat = FastForwardAiScheduler.LogSignature(50f, 7, 2, 4, budgetExhausted: true);
        string changed = FastForwardAiScheduler.LogSignature(20f, 4, 2, 2, budgetExhausted: false);

        AssertEqual(true, gate.ShouldLog(first));
        AssertEqual(false, gate.ShouldLog(repeat));
        AssertEqual(true, gate.ShouldLog(changed));
        AssertEqual(false, gate.ShouldLog(first));
    }

    private static void CommanderAssignmentGuardClearsStalePreviousCommand()
    {
        AssertTrue(
            CommanderAssignmentGuard.ShouldClearPreviousCommand(
                priorCommandExists: true,
                priorIsAssignedTarget: false,
                priorCommandCommanderId: 7,
                assignedCommanderId: 7),
            "same commander on a different previous unit should be cleared");

        AssertTrue(
            !CommanderAssignmentGuard.ShouldClearPreviousCommand(
                priorCommandExists: true,
                priorIsAssignedTarget: true,
                priorCommandCommanderId: 7,
                assignedCommanderId: 7),
            "the target unit should not be cleared");

        AssertTrue(
            !CommanderAssignmentGuard.ShouldClearPreviousCommand(
                priorCommandExists: true,
                priorIsAssignedTarget: false,
                priorCommandCommanderId: 3,
                assignedCommanderId: 7,
                vanillaReplacementWillReadPriorCommander: false),
            "a previous unit already reassigned to another commander should not be touched");

        AssertTrue(
            !CommanderAssignmentGuard.ShouldClearPreviousCommand(
                priorCommandExists: true,
                priorIsAssignedTarget: false,
                priorCommandCommanderId: 7,
                assignedCommanderId: 7,
                vanillaReplacementWillReadPriorCommander: true),
            "vanilla ReplaceCommanderOfUnit still reads the subordinate commander id after AssignCommando");
    }

    private static void CampaignFilterMapGuardBoundsRepeatedNoProgress()
    {
        var guard = new CampaignFilterMapInitializationGuard(maxRepeatedNoProgressReturns: 3);
        var stuck = new CampaignFilterMapState(0, 0, 0, 3, 2, 2, 4);
        var advanced = new CampaignFilterMapState(1, 1, 0, 3, 2, 2, 4);

        AssertTrue(
            !guard.Observe(initialization: true, result: false, stuck, advanced).ForceComplete,
            "normal iterator progress should not be forced complete");

        guard = new CampaignFilterMapInitializationGuard(maxRepeatedNoProgressReturns: 3);
        AssertTrue(!guard.Observe(initialization: true, result: false, stuck, stuck).ForceComplete, "first stuck false should wait");
        AssertTrue(!guard.Observe(initialization: true, result: false, stuck, stuck).ForceComplete, "second stuck false should wait");
        var decision = guard.Observe(initialization: true, result: false, stuck, stuck);
        AssertTrue(decision.ForceComplete, "third repeated no-progress false should force completion");
        AssertEqual("no-progress", decision.Reason);

        decision = guard.ObserveException(initialization: true, new InvalidOperationException("boom"), stuck);
        AssertTrue(decision.ForceComplete, "initialization exception should force completion");

        decision = guard.ObserveException(initialization: false, new NullReferenceException("boom"), stuck);
        AssertTrue(decision.ForceComplete, "runtime null reference should advance one iterator slot and suppress");
        AssertEqual("runtime-exception:NullReferenceException", decision.Reason);

        decision = guard.ObserveException(initialization: false, new InvalidOperationException("boom"), stuck);
        AssertTrue(!decision.ForceComplete, "non-null runtime exceptions should fall through");

        guard = new CampaignFilterMapInitializationGuard(
            maxRepeatedNoProgressReturns: 3,
            maxRuntimeExceptionSuppressionsPerSignature: 1);
        decision = guard.ObserveException(initialization: false, new NullReferenceException("boom"), stuck);
        AssertTrue(decision.ForceComplete, "first runtime null for a cursor signature should suppress");
        decision = guard.ObserveException(initialization: false, new NullReferenceException("boom"), stuck);
        AssertTrue(!decision.ForceComplete, "repeated runtime null at the same cursor signature should surface after cap");

        AssertTrue(
            CampaignFilterMapInitializationGuard.TryAdvanceRuntimeCursor(stuck, out var runtimeAdvanced),
            "plausible runtime cursor should advance");
        AssertEqual(new CampaignFilterMapState(1, 1, 0, 3, 2, 2, 4).Signature(), runtimeAdvanced.Signature());

        AssertTrue(
            CampaignFilterMapInitializationGuard.TryAdvanceRuntimeCursor(
                new CampaignFilterMapState(2, 1, 3, 3, 2, 2, 4),
                out var reset),
            "complete runtime cursor should reset like vanilla");
        AssertEqual(new CampaignFilterMapState(0, 0, -1, 3, 2, 2, 4).Signature(), reset.Signature());

        AssertTrue(
            !CampaignFilterMapInitializationGuard.TryAdvanceRuntimeCursor(
                new CampaignFilterMapState(0, 0, 0, 3, -1, 2, 4),
                out _),
            "runtime cursor without smalltown data should not claim safe advancement");

        string diagnostic = CampaignFilterMapInitializationGuard.BuildRuntimeDiagnostic(
            stuck,
            runtimeAdvanced,
            "lists towns=3 smalltowns=2 iips=2 cbuildings=4");
        AssertContains(diagnostic, "cursor=0/3:0/2:0/2:0/4", "diagnostic should include failing cursor");
        AssertContains(diagnostic, "next=1/3:1/2:1/2:0/4", "diagnostic should include advanced cursor");
        AssertContains(diagnostic, "lists towns=3", "diagnostic should include runtime probe summary");
    }

    private static void CampaignFilterMapGuardDetectsAssignFiltersBootstrapNeeds()
    {
        string[] ready = CampaignFilterMapInitializationGuard.GetMissingAssignFiltersMapNames(
            availableWorkforceReady: true,
            slaveryReady: true,
            tradeAndSupplyReady: true,
            supplyReady: true,
            availableCapitalReady: true,
            transportBottlenecksReady: true,
            marketCapacityReady: true,
            hospitalsReady: true);
        AssertEqual(0, ready.Length);

        string[] missing = CampaignFilterMapInitializationGuard.GetMissingAssignFiltersMapNames(
            availableWorkforceReady: false,
            slaveryReady: true,
            tradeAndSupplyReady: false,
            supplyReady: true,
            availableCapitalReady: true,
            transportBottlenecksReady: false,
            marketCapacityReady: true,
            hospitalsReady: false);
        string joined = string.Join(",", missing);
        AssertContains(joined, "availableworkforce", "assign-filters readiness should name missing workforce map");
        AssertContains(joined, "tradeandsupply", "assign-filters readiness should name missing trade map");
        AssertContains(joined, "transportbottlenecks", "assign-filters readiness should name missing bottleneck map");
        AssertContains(joined, "hospitals", "assign-filters readiness should name missing hospital map");
    }

    private static void StateHandoverGuardRequiresDecisiveSupport()
    {
        AssertTrue(StateHandoverGuard.AllowsHandover(0, 0.70f, 0.30f), "decisive Union support should allow Union handover");
        AssertTrue(StateHandoverGuard.AllowsHandover(1, 0.30f, 0.70f), "decisive CSA support should allow CSA handover");
        AssertTrue(!StateHandoverGuard.AllowsHandover(0, 0.60f, 0.40f), "simple Union majority should not flip state");
        AssertTrue(!StateHandoverGuard.AllowsHandover(1, 0.45f, 0.55f), "simple CSA majority should not flip state");
        AssertTrue(!StateHandoverGuard.AllowsHandover(2, 0.80f, 0.20f), "non-USA/CSA alliance should not be handled");
    }

    private static void FleetPatrolGuardResetsCompletedAiPatrol()
    {
        AssertTrue(
            FleetPatrolResetGuard.ShouldResetAiPatrolToIdle(
                isPlayerFleet: false,
                unitType: 17,
                fleetOrders: 2,
                regimentPaths: 0,
                distanceToStart: 0.5f,
                completionRadius: 1f,
                isRouted: false,
                onRetreat: false,
                inBattle: false,
                withinRotationProcess: false),
            "idle AI fleet inside completion radius should reset to normal orders");

        AssertTrue(
            !FleetPatrolResetGuard.ShouldResetAiPatrolToIdle(
                isPlayerFleet: true,
                unitType: 17,
                fleetOrders: 2,
                regimentPaths: 0,
                distanceToStart: 0.5f,
                completionRadius: 1f,
                isRouted: false,
                onRetreat: false,
                inBattle: false,
                withinRotationProcess: false),
            "player patrol orders should not be changed");

        AssertTrue(
            !FleetPatrolResetGuard.ShouldResetAiPatrolToIdle(
                isPlayerFleet: false,
                unitType: 17,
                fleetOrders: 2,
                regimentPaths: 1,
                distanceToStart: 0.5f,
                completionRadius: 1f,
                isRouted: false,
                onRetreat: false,
                inBattle: false,
                withinRotationProcess: false),
            "moving patrol should not be reset");
    }

    private static CampaignPaceInput BuildPaceInput(
        int allianceId, int year, int month, int chapter,
        float ownNationalMorale,
        int battlesIn14Days, int majorBattlesIn14Days,
        int capitalStreak, int daysSinceFrontChange,
        bool winter)
    {
        return new CampaignPaceInput
        {
            AllianceId = allianceId,
            Year = year, Month = month,
            PolicyChapter = chapter,
            OwnNationalMorale = ownNationalMorale,
            BreakMoraleTrigger = 30f, // arbitrary stable test value
            MinNationalMoraleSurrender = 18f,
            BattlesIn14Days = battlesIn14Days,
            MajorBattlesIn14Days = majorBattlesIn14Days,
            CapitalDangerStreakDays = capitalStreak,
            DaysSinceFrontSignatureChange = daysSinceFrontChange,
            IsWinter = winter
        };
    }

    private static void CampaignPaceTooFastCollapseOnEarlyMoraleCrash()
    {
        var input = BuildPaceInput(allianceId: 1, year: 1862, month: 6, chapter: 2,
            ownNationalMorale: 30f * 1.10f,
            battlesIn14Days: 0, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 5, winter: false);
        var output = CampaignPaceLedger.Build(input);
        AssertEqual(CampaignPace.TooFastCollapse, output.Pace);
        AssertEqual(CollapseRisk.Critical, output.Risk);
    }

    private static void CampaignPaceLateWarPressureOnChapterThree()
    {
        var input = BuildPaceInput(allianceId: 0, year: 1864, month: 6, chapter: 3,
            ownNationalMorale: 80f,
            battlesIn14Days: 0, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 60, winter: false);
        var output = CampaignPaceLedger.Build(input);
        AssertEqual(CampaignPace.LateWarPressure, output.Pace);
    }

    private static void CampaignPaceUnion1864BeforeChapterThreeIsNotLateWar()
    {
        // The Union runway clause escalated on the calendar (year >= 1864 with the CSA still
        // fighting). The late-war stage is vanilla's chapter 3 (the election), so June 1864 in
        // chapter 2 is an ordinary quiet-or-stable month.
        var input = BuildPaceInput(allianceId: 0, year: 1864, month: 6, chapter: 2,
            ownNationalMorale: 80f,
            battlesIn14Days: 1, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 5, winter: false);
        var output = CampaignPaceLedger.Build(input);
        AssertEqual(CampaignPace.Stable, output.Pace);
        AssertEqual("default", output.Reason);
    }

    // Integrator decision D13: these two CSA rules are historical timing and stay on the calendar,
    // so they are pinned where the calendar and the chapter disagree.
    private static void CampaignPaceCsaFloorIsCalendarAtChapterTwo()
    {
        var input = BuildPaceInput(allianceId: 1, year: 1864, month: 6, chapter: 2,
            ownNationalMorale: 30f * 1.40f,
            battlesIn14Days: 1, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 5, winter: false);
        var output = CampaignPaceLedger.Build(input);
        AssertTrue(output.Risk >= CollapseRisk.Elevated, "elevated risk expected");
        AssertTrue(output.IntentBlockedFromPreserve, "1864, chapter 2 (election pending or lost): the CSA still may not Preserve");
        var earlier = BuildPaceInput(allianceId: 1, year: 1863, month: 6, chapter: 3,
            ownNationalMorale: 30f * 1.40f,
            battlesIn14Days: 1, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 5, winter: false);
        AssertFalse(CampaignPaceLedger.Build(earlier).IntentBlockedFromPreserve, "1863 is before the floor whatever the chapter");
    }

    private static void CampaignPaceTooFastCollapseIsCalendarNotChapter()
    {
        var critical1863 = BuildPaceInput(allianceId: 1, year: 1863, month: 6, chapter: 2,
            ownNationalMorale: 30f * 1.10f,
            battlesIn14Days: 1, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 5, winter: false);
        AssertEqual(CampaignPace.TooFastCollapse, CampaignPaceLedger.Build(critical1863).Pace);
        var critical1863Chapter3 = BuildPaceInput(allianceId: 1, year: 1863, month: 6, chapter: 3,
            ownNationalMorale: 30f * 1.10f,
            battlesIn14Days: 1, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 5, winter: false);
        AssertEqual(CampaignPace.TooFastCollapse, CampaignPaceLedger.Build(critical1863Chapter3).Pace);
        var critical1864 = BuildPaceInput(allianceId: 1, year: 1864, month: 6, chapter: 2,
            ownNationalMorale: 30f * 1.10f,
            battlesIn14Days: 1, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 5, winter: false);
        var output = CampaignPaceLedger.Build(critical1864);
        AssertEqual(CollapseRisk.Critical, output.Risk);
        AssertTrue(output.Pace != CampaignPace.TooFastCollapse, "from 1864 a critical CSA is not 'too fast', chapter 2 or not");
    }

    private static void CampaignPaceOverheatedOnHeavy14DayBattles()
    {
        var input = BuildPaceInput(allianceId: 0, year: 1862, month: 8, chapter: 2,
            ownNationalMorale: 80f,
            battlesIn14Days: 6, majorBattlesIn14Days: 4,
            capitalStreak: 0, daysSinceFrontChange: 5, winter: false);
        var output = CampaignPaceLedger.Build(input);
        AssertEqual(CampaignPace.Overheated, output.Pace);
    }

    private static void CampaignPaceTooQuietSuppressedInChapterOneWinter()
    {
        var input = BuildPaceInput(allianceId: 0, year: 1861, month: 12, chapter: 1,
            ownNationalMorale: 95f,
            battlesIn14Days: 0, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 30, winter: true);
        var output = CampaignPaceLedger.Build(input);
        AssertTrue(output.Pace != CampaignPace.TooQuiet,
            "chapter 1 winter is the historically correct quiet state and must not be flagged");
    }

    private static void CampaignPaceStalematedWhenChapterTwoFrontStatic()
    {
        var input = BuildPaceInput(allianceId: 0, year: 1862, month: 6, chapter: 2,
            ownNationalMorale: 80f,
            battlesIn14Days: 1, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 75, winter: false);
        var output = CampaignPaceLedger.Build(input);
        AssertEqual(CampaignPace.Stalemated, output.Pace);
    }

    private static void CampaignPaceStableDefault()
    {
        var input = BuildPaceInput(allianceId: 0, year: 1862, month: 6, chapter: 2,
            ownNationalMorale: 80f,
            battlesIn14Days: 2, majorBattlesIn14Days: 1,
            capitalStreak: 0, daysSinceFrontChange: 10, winter: false);
        var output = CampaignPaceLedger.Build(input);
        AssertEqual(CampaignPace.Stable, output.Pace);
    }

    private static void DirectorCannotPublishPreserveForLateCsaUnderElevatedRisk()
    {
        var input = BuildPaceInput(allianceId: 1, year: 1864, month: 6, chapter: 3,
            ownNationalMorale: 30f * 1.40f,
            battlesIn14Days: 1, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 5, winter: false);
        var output = CampaignPaceLedger.Build(input);
        AssertTrue(output.Risk >= CollapseRisk.Elevated, "elevated risk expected");
        AssertTrue(output.IntentBlockedFromPreserve,
            "1864 CSA under elevated risk must not publish StrategicIntent.Preserve");
    }

    private static void CampaignPacePublishesTheaterPriorityFromHighestPressureTheater()
    {
        var view = new TheaterPressureView();
        view.OwnStrengthByTheater[Theater.East] = 10000f;
        view.EnemyStrengthByTheater[Theater.East] = 4000f;
        view.OwnStrengthByTheater[Theater.West] = 4000f;
        view.EnemyStrengthByTheater[Theater.West] = 8000f; // West is the hot theater for us
        var input = BuildPaceInput(allianceId: 0, year: 1862, month: 6, chapter: 2,
            ownNationalMorale: 80f,
            battlesIn14Days: 2, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 10, winter: false);
        input.TheaterPressure = view;
        var output = CampaignPaceLedger.Build(input);
        AssertEqual(Theater.West, output.TheaterPriority);
    }

    private static void DirectorMapsOverheatedToRecoverLeaning()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 0,
            pace: new CampaignPaceOutput { Pace = CampaignPace.Overheated, Risk = CollapseRisk.Low, IntentBlockedFromPreserve = false });
        AssertTrue(posture.Intent == StrategicIntent.Recover || posture.Intent == StrategicIntent.Delay,
            "overheated pace should propose recover/delay intent");
    }

    private static void DirectorBlocksPreserveForLateCsaUnderElevatedRisk()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 1,
            pace: new CampaignPaceOutput { Pace = CampaignPace.LateWarPressure, Risk = CollapseRisk.Elevated, IntentBlockedFromPreserve = true });
        AssertTrue(posture.Intent != StrategicIntent.Preserve,
            "1864 CSA under elevated risk cannot publish Preserve");
    }

    // Task 14: MemoryToDto/MemoryFromDto preserve all key fields including LastPosture enum values
    // and RecentEventSummaries list. Null DTO input must yield a fresh DirectorMemory.
    private static void DirectorMemoryRoundTripsThroughDto()
    {
        var memory = new DirectorMemory
        {
            LastFullRefreshDay = 12345,
            CapitalDangerStreakDays = 3,
            DaysSinceLastBattle = 7,
            LastSourceSignature = "sig-abc",
            LastPosture = new DirectorPosture
            {
                AllianceId = 0,
                Pace = CampaignPace.LateWarPressure,
                Intent = StrategicIntent.Concentrate,
                Risk = CollapseRisk.Low,
                TheaterPriority = Theater.East
            }
        };
        memory.RecentEventSummaries.Add("battle:east:1864-06-15");

        var dto = StrategicResilienceDirector.MemoryToDto(memory);
        var rebuilt = StrategicResilienceDirector.MemoryFromDto(dto);

        AssertEqual(memory.LastFullRefreshDay, rebuilt.LastFullRefreshDay);
        AssertEqual(memory.CapitalDangerStreakDays, rebuilt.CapitalDangerStreakDays);
        AssertEqual(memory.LastSourceSignature, rebuilt.LastSourceSignature);
        AssertEqual(memory.LastPosture.Pace, rebuilt.LastPosture.Pace);
        AssertEqual(memory.LastPosture.Intent, rebuilt.LastPosture.Intent);
        AssertEqual(memory.LastPosture.Risk, rebuilt.LastPosture.Risk);
        AssertEqual(memory.LastPosture.TheaterPriority, rebuilt.LastPosture.TheaterPriority);
        AssertEqual(1, rebuilt.RecentEventSummaries.Count);
    }

    // R16: the clamp is per alliance per game day (no wall clock).
    private static void DirectorPublishClampSuppressesSecondPublishOnSameGameDay()
    {
        var clamp = new DirectorPublishClamp();
        int day = 1862 * 372 + 5 * 31 + 5;
        AssertTrue(clamp.TryPublish(0, day), "first publish on the day should succeed");
        AssertTrue(!clamp.TryPublish(0, day), "second publish same alliance same day should be suppressed");
        AssertTrue(clamp.TryPublish(0, day + 1), "publish on the next game day should succeed");
    }

    private static void DirectorRaisesCsaHoldRatioUnderTooFastCollapse()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 1,
            pace: new CampaignPaceOutput { Pace = CampaignPace.TooFastCollapse, Risk = CollapseRisk.Critical, IntentBlockedFromPreserve = false });
        AssertTrue(posture.MinimumHoldRatioModifier > 0f,
            "TooFastCollapse for CSA must raise MinimumHoldRatio — was " + posture.MinimumHoldRatioModifier);
        AssertTrue(posture.MinimumHoldRatioModifier <= 0.10f,
            "MinimumHoldRatioModifier capped at +0.10");
    }

    private static void DirectorRaisesRecoverFloorUnderOverheated()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 0,
            pace: new CampaignPaceOutput { Pace = CampaignPace.Overheated, Risk = CollapseRisk.Low });
        AssertTrue(posture.RecoverFloorModifier > 0f, "overheated must raise recover floor");
    }

    private static void DirectorRelaxesUnionMassRatioUnderLateWarPressure()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 0,
            pace: new CampaignPaceOutput { Pace = CampaignPace.LateWarPressure, Risk = CollapseRisk.Low });
        AssertTrue(posture.MassRatioModifier < 0f, "Union late-war pressure must lower mass ratio gate");
    }

    private static void DirectorCriticalRiskFavorsSupplyConstruction()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 1,
            pace: new CampaignPaceOutput { Pace = CampaignPace.TooFastCollapse, Risk = CollapseRisk.Critical });
        AssertTrue(posture.SupplyConstructionBias >= 0.30f,
            "Critical risk must strongly favor supply — was " + posture.SupplyConstructionBias);
    }

    private static void DirectorTooQuietFavorsLogistics()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 0,
            pace: new CampaignPaceOutput { Pace = CampaignPace.TooQuiet, Risk = CollapseRisk.Low });
        AssertTrue(posture.LogisticsBias >= 0.20f, "TooQuiet must favor logistics");
    }

    private static void DirectorTooFastCollapseDampsExpansion()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 1,
            pace: new CampaignPaceOutput { Pace = CampaignPace.TooFastCollapse, Risk = CollapseRisk.Critical });
        AssertTrue(posture.ExpansionDamper >= 0.30f, "TooFastCollapse must damp expansion");
    }

    private static void DirectorLowersUnionGuardUnderLateWarPressure()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 0,
            pace: new CampaignPaceOutput { Pace = CampaignPace.LateWarPressure, Risk = CollapseRisk.Low });
        AssertTrue(posture.GuardBudgetFractionModifier <= 0f,
            "Union late-war pressure can slightly lower guard for source-sector concentration");
    }
}
