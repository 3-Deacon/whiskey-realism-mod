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

internal static class TelemetryPipelineTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("telemetry enum contracts match plan", TelemetryEnumContractsMatchPlan),
            ("telemetry json writes required fields", TelemetryJsonWritesRequiredFields),
            ("telemetry json escapes strings safely", TelemetryJsonEscapesStringsSafely),
            ("telemetry fields sanitize nulls and nonfinite numbers", TelemetryFieldsSanitizeNullsAndNonfiniteNumbers),
            ("telemetry duration marks invalid float", TelemetryDurationMarksInvalidFloat),
            ("telemetry decision requires input signature", TelemetryDecisionRequiresInputSignature),
            ("telemetry budget drops trace first", TelemetryBudgetDropsTraceFirst),
            ("telemetry queue preserves failure under pressure", TelemetryQueuePreservesFailureUnderPressure),
            ("telemetry queue never drops health for lower priority rows", TelemetryQueueNeverDropsHealthForLowerPriorityRows),
            ("telemetry budget applies staged category cuts", TelemetryBudgetAppliesStagedCategoryCuts),
            ("telemetry budget reserve is atomic under total cap", TelemetryBudgetReserveIsAtomicUnderTotalCap),
            ("telemetry queue evicts decision before gate write", TelemetryQueueEvictsDecisionBeforeGateWrite),
            ("telemetry queue preserves protected incoming when protected full", TelemetryQueuePreservesProtectedIncomingWhenProtectedFull),
            ("telemetry budget low priority cuts before high priority", TelemetryBudgetLowPriorityCutsBeforeHighPriority),
            ("telemetry queue protected bypass waits until detail capacity full", TelemetryQueueProtectedBypassWaitsUntilDetailCapacityFull),
            ("telemetry queue bounds protected bursts with overflow counts", TelemetryQueueBoundsProtectedBurstsWithOverflowCounts),
            ("telemetry queue dropped count read is stable", TelemetryQueueDroppedCountReadIsStable),
            ("telemetry budget dropped snapshot is stable", TelemetryBudgetDroppedSnapshotIsStable),
            ("telemetry budget caps raw protected detail but allows summaries", TelemetryBudgetCapsRawProtectedDetailButAllowsSummaries),
            ("telemetry budget bounds protected summary reserve", TelemetryBudgetBoundsProtectedSummaryReserve),
            ("telemetry queue reports coalesced protected overflow", TelemetryQueueReportsCoalescedProtectedOverflow),
            ("telemetry budget caps raw performance but allows summaries", TelemetryBudgetCapsRawPerformanceButAllowsSummaries),
            ("telemetry profile off does not allocate session", TelemetryProfileOffDoesNotAllocateSession),
            ("telemetry router double shutdown is idempotent", TelemetryRouterDoubleShutdownIsIdempotent),
            ("telemetry perf scope emits slow event", TelemetryPerfScopeEmitsSlowEvent),
            ("telemetry perf scope swallows sink failure", TelemetryPerfScopeSwallowsSinkFailure),
            ("telemetry profile parses unknown as off", TelemetryProfileParsesUnknownAsOff),
            ("telemetry tag policy routes tactical matrix to state", TelemetryTagPolicyRoutesTacticalMatrixToState),
            ("telemetry tag policy keeps startup allowlisted", TelemetryTagPolicyKeepsStartupAllowlisted),
            ("telemetry tag policy routes deployment diagnostics sidecar only", TelemetryTagPolicyRoutesDeploymentDiagnosticsSidecarOnly),
            ("telemetry tag policy routes tactical map knowledge sidecar only", TelemetryTagPolicyRoutesTacticalMapKnowledgeSidecarOnly),
            ("telemetry tag policy does not promote missing parents count", TelemetryTagPolicyDoesNotPromoteMissingParentsCount),
            ("telemetry tag policy routes campaign plan and succession sidecar only", TelemetryTagPolicyRoutesCampaignPlanAndSuccessionSidecarOnly),
            ("task7 explicit campaign groups use typed telemetry", Task7ExplicitCampaignGroupsUseTypedTelemetry),
            ("telemetry legacy off suppresses sidecar only but allows serious", TelemetryLegacyOffSuppressesSidecarOnlyButAllowsSerious),
            ("telemetry typed off suppression warns once", TelemetryTypedOffSuppressionWarnsOnce),
            ("telemetry legacy unavailable runtime keeps protected failures visible", TelemetryLegacyUnavailableRuntimeKeepsProtectedFailuresVisible),
            ("deployment legacy helper suppresses off profile tuning rows", DeploymentLegacyHelperSuppressesOffProfileTuningRows),
            ("once log retries when logger unavailable", OnceLogRetriesWhenLoggerUnavailable),
            ("once log concurrent calls keep one time semantics", OnceLogConcurrentCallsKeepOneTimeSemantics),
            ("telemetry legacy parser extracts key values", TelemetryLegacyParserExtractsKeyValues),
            ("telemetry legacy parser creates coarse signature", TelemetryLegacyParserCreatesCoarseSignature),
        });
    }

    private static void TelemetryBudgetDropsTraceFirst()
    {
        var budget = new TelemetryBudget(totalBytes: 1000, rotateBytes: 250);
        AssertTrue(budget.Allow(TelemetryCategory.Trace, 900), "trace allowed before cap");
        budget.RecordBytes(TelemetryCategory.Trace, 900);
        AssertFalse(budget.Allow(TelemetryCategory.Trace, 200), "trace cut first near cap");
        AssertTrue(budget.Allow(TelemetryCategory.Failure, 200, lowPriority: true, protectedSummary: true), "failure summary protected near cap");
    }

    private static void TelemetryQueuePreservesFailureUnderPressure()
    {
        var queue = new TelemetryQueue(capacity: 2);
        queue.TryEnqueue(EventForQueue(TelemetryCategory.Trace, "trace-a"));
        queue.TryEnqueue(EventForQueue(TelemetryCategory.State, "state-a"));
        queue.TryEnqueue(EventForQueue(TelemetryCategory.Failure, "failure-a"));
        var drained = queue.Drain(10);
        // WS P TEL-queue: protected rows use their own reserve and never evict detail rows.
        AssertEqual(3, drained.Count, "queue count");
        AssertTrue(drained.Exists(e => e.Category == TelemetryCategory.Failure), "failure preserved");
        AssertEqual(0L, queue.DroppedCount, "protected row does not evict detail");
    }

    private static TelemetryEvent EventForQueue(TelemetryCategory category, string name)
    {
        return TelemetryEvent.Create("s", TelemetryProfile.FullTuning, TelemetryLayer.System, category, name, TelemetrySeverity.Info);
    }

    private static void TelemetryProfileOffDoesNotAllocateSession()
    {
        string gameRoot = CreateTempDirectory();
        try
        {
            var config = TelemetryRuntimeConfig.Create(
                gameRoot,
                "0.2.2-test",
                "abcdef1234567890",
                TelemetryProfile.Off,
                maxTuningLogMb: 250,
                fileRotateMb: 25,
                retainedSessions: 2,
                emitHumanSummary: true,
                performanceWarnings: true,
                createIssueBundleOnShutdown: false,
                warningCallback: null);

            TelemetryRuntime runtime = TelemetryRuntime.Start(config);
            AssertFalse(runtime.IsRunning, "off profile runtime should not start writer");
            AssertEqual("-", runtime.SessionId, "off profile has no session id");
            AssertFalse(Directory.Exists(TelemetrySession.TuningLogRoot(gameRoot)), "off profile should not create tuning log root");
            runtime.Shutdown("test-off");
            AssertFalse(Directory.Exists(TelemetrySession.TuningLogRoot(gameRoot)), "off shutdown should not create tuning log root");
        }
        finally
        {
            DeleteDirectoryQuietly(gameRoot);
        }
    }

    private static void TelemetryRouterDoubleShutdownIsIdempotent()
    {
        string gameRoot = CreateTempDirectory();
        var warnings = new List<string>();
        try
        {
            var config = TelemetryRuntimeConfig.Create(
                gameRoot,
                "0.2.2-test",
                "abcdef1234567890",
                TelemetryProfile.FullTuning,
                maxTuningLogMb: 4,
                fileRotateMb: 1,
                retainedSessions: 2,
                emitHumanSummary: true,
                performanceWarnings: true,
                createIssueBundleOnShutdown: true,
                warningCallback: warnings.Add);

            TelemetryRuntime runtime = TelemetryRuntime.Start(config);
            string sessionDirectory = Path.Combine(TelemetrySession.TuningLogRoot(gameRoot), runtime.SessionId);
            TelemetryRouter.AttachRuntime(runtime);
            AssertTrue(TelemetryRouter.Emit(TelemetryLayer.System, TelemetryCategory.Health, "RouterHealth", TelemetrySeverity.Info), "router emits before shutdown");

            TelemetryRouter.Shutdown("plugin-destroy");
            TelemetryRouter.Shutdown("application-quit");

            AssertFalse(runtime.IsRunning, "runtime stopped after first router shutdown");
            JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(sessionDirectory, "manifest.json")));
            AssertTrue(manifest["endUtc"].Type != JTokenType.Null, "double shutdown manifest is parseable and final");
            AssertEqual(0L, (long)manifest["droppedCounters"]["sinkFailures"], "double shutdown does not duplicate sink failures");
            AssertEqual(0, warnings.Count, "double shutdown does not duplicate warnings");
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            DeleteDirectoryQuietly(gameRoot);
        }
    }

    private static void TelemetryPerfScopeEmitsSlowEvent()
    {
        string gameRoot = CreateTempDirectory();
        try
        {
            var config = TelemetryRuntimeConfig.Create(
                gameRoot,
                "0.2.2-test",
                "abcdef1234567890",
                TelemetryProfile.FullTuning,
                maxTuningLogMb: 4,
                fileRotateMb: 1,
                retainedSessions: 2,
                emitHumanSummary: true,
                performanceWarnings: true,
                createIssueBundleOnShutdown: false,
                warningCallback: null);

            TelemetryRuntime runtime = TelemetryRuntime.Start(config);
            string sessionDirectory = Path.Combine(TelemetrySession.TuningLogRoot(gameRoot), runtime.SessionId);
            TelemetryRouter.AttachRuntime(runtime);

            using (TelemetryPerf.Scope("unit.perf", TelemetryLayer.Tactical, TelemetryCategory.Performance, 0.0))
            {
                System.Threading.Thread.Sleep(1);
            }

            TelemetryRouter.Shutdown("perf-test");

            string performance = File.ReadAllText(Path.Combine(sessionDirectory, "performance.jsonl"));
            JObject row = ParseJsonLine(performance);
            AssertEqual("Performance", (string)row["event"], "performance event name");
            AssertEqual("Tactical", (string)row["layer"], "performance layer");
            AssertEqual("Performance", (string)row["category"], "performance category");
            AssertContains(performance, "\"scope\":\"unit.perf\"", "scope field");
            AssertContains(performance, "\"slow\":true", "slow field");
            AssertContains(performance, "\"thresholdMs\":0", "threshold field");
            AssertContains(performance, "\"queueDepth\"", "queue depth field");
            AssertContains(performance, "\"eventsEmitted\"", "spec emitted counter field");
            AssertContains(performance, "\"eventsDropped\"", "spec dropped counter field");
            AssertContains(performance, "\"bytesWritten\"", "spec bytes written field");
            AssertContains(performance, "\"emittedCount\"", "emitted counter field");
            AssertContains(performance, "\"droppedCount\"", "dropped counter field");
            AssertTrue(row["fields"]["bytesWritten"] != null, "bytesWritten field exists");
            AssertTrue((double)row["durationMs"] >= 0.0, "duration is finite");
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            DeleteDirectoryQuietly(gameRoot);
        }
    }

    private static void TelemetryPerfScopeSwallowsSinkFailure()
    {
        string gameRoot = CreateTempDirectory();
        var warnings = new List<string>();
        try
        {
            var config = TelemetryRuntimeConfig.Create(
                gameRoot,
                "0.2.2-test",
                "abcdef1234567890",
                TelemetryProfile.FullTuning,
                maxTuningLogMb: 4,
                fileRotateMb: 1,
                retainedSessions: 2,
                emitHumanSummary: true,
                performanceWarnings: true,
                createIssueBundleOnShutdown: false,
                warningCallback: warnings.Add);

            TelemetryRuntime runtime = TelemetryRuntime.Start(config);
            string sessionDirectory = Path.Combine(TelemetrySession.TuningLogRoot(gameRoot), runtime.SessionId);
            TelemetryRouter.AttachRuntime(runtime);
            Directory.Delete(sessionDirectory, recursive: true);

            using (TelemetryPerf.Scope("unit.perf.failure", TelemetryLayer.Tactical, TelemetryCategory.Performance, 0.0))
            {
            }

            TelemetryRouter.Shutdown("perf-sink-failure");

            AssertFalse(runtime.IsRunning, "runtime stops after sink failure");
            AssertTrue(warnings.Count >= 1, "sink failure is reported through bounded warning");
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            DeleteDirectoryQuietly(gameRoot);
        }
    }

    private static void TelemetryProfileParsesUnknownAsOff()
    {
        AssertEqual(TelemetryProfile.Off, TelemetryRouter.ParseProfile(null), "null profile");
        AssertEqual(TelemetryProfile.Off, TelemetryRouter.ParseProfile(""), "blank profile");
        AssertEqual(TelemetryProfile.Off, TelemetryRouter.ParseProfile("noisy"), "unknown profile");
        AssertEqual(TelemetryProfile.TacticalTuning, TelemetryRouter.ParseProfile(" Tactical Tuning "), "tactical spaced profile");
        AssertEqual(TelemetryProfile.CampaignTuning, TelemetryRouter.ParseProfile("campaign-tuning"), "campaign dashed profile");
        AssertEqual(TelemetryProfile.FullTuning, TelemetryRouter.ParseProfile("full_tuning"), "full underscored profile");
    }

    private static void TelemetryTagPolicyRoutesTacticalMatrixToState()
    {
        var route = TelemetryTagPolicy.Route("[once:tactical-matrix] [TacticalDecisionMatrix] battle=alpha side=1 decision=Probe reason=odds");
        AssertEqual("TacticalDecisionMatrix", route.Tag, "tag");
        AssertEqual(TelemetryLayer.Tactical, route.Layer, "layer");
        AssertEqual(TelemetryCategory.State, route.Category, "category");
        AssertEqual("TacticalDecisionMatrix", route.EventName, "event");
        AssertTrue(route.RouteToSidecar, "matrix sidecar route");
        AssertFalse(route.AllowMainLog, "matrix main log");
    }

    private static void TelemetryTagPolicyKeepsStartupAllowlisted()
    {
        var route = TelemetryTagPolicy.Route("[WhiskeyRealism] plugin loaded profile=Off");
        AssertEqual("WhiskeyRealism", route.Tag, "tag");
        AssertEqual(TelemetryLayer.System, route.Layer, "layer");
        AssertEqual(TelemetryCategory.Health, route.Category, "category");
        AssertFalse(route.RouteToSidecar, "startup does not require sidecar");
        AssertTrue(route.AllowMainLog, "startup remains main log allowlisted");
    }

    private static void TelemetryTagPolicyRoutesDeploymentDiagnosticsSidecarOnly()
    {
        AssertDeploymentRoute("[TacDeployObs] surface=DoPlacement phase=initial alliance=1", "TacDeployObs", TelemetryCategory.State);
        AssertDeploymentRoute("[TacDeployObsMove] unit=brigade-1 distance=145.0", "TacDeployObsMove", TelemetryCategory.Write);
        AssertDeploymentRoute("[TacDeployTerrain] unit=brigade-2 decision=accepted", "TacDeployTerrain", TelemetryCategory.State);
        AssertDeploymentRoute("[TacDeployTerrainAdvice] unit=brigade-3 decision=reject-water", "TacDeployTerrainAdvice", TelemetryCategory.Gate);

        var serious = TelemetryTagPolicy.Route("[TacDeployTerrain] unit=brigade-4 failed missing terrain probe");
        AssertEqual(TelemetryLayer.Tactical, serious.Layer, "serious layer");
        AssertEqual(TelemetryCategory.Failure, serious.Category, "serious category");
        AssertTrue(serious.RouteToSidecar, "serious deployment row still routes sidecar");
        AssertTrue(serious.AllowMainLog, "serious deployment row escalates to main log");
    }

    private static void TelemetryTagPolicyRoutesTacticalMapKnowledgeSidecarOnly()
    {
        var route = TelemetryTagPolicy.Route("[TacticalMapKnowledge] surface=inventory objectives=3 entryPoints=2");
        AssertEqual("TacticalMapKnowledge", route.Tag, "tag");
        AssertEqual(TelemetryLayer.Tactical, route.Layer, "layer");
        AssertEqual(TelemetryCategory.Trace, route.Category, "category");
        AssertEqual("TacticalMapKnowledge", route.EventName, "event");
        AssertTrue(route.RouteToSidecar, "map knowledge sidecar route");
        AssertFalse(route.AllowMainLog, "map knowledge main log");
    }

    private static void TelemetryTagPolicyDoesNotPromoteMissingParentsCount()
    {
        var route = TelemetryTagPolicy.Route("[TacticalCommandTree] alliance=1 nodes=9 missingParents=0");
        AssertEqual(TelemetryLayer.Tactical, route.Layer, "layer");
        AssertEqual(TelemetryCategory.State, route.Category, "category");
        AssertTrue(route.RouteToSidecar, "command tree sidecar route");
        AssertFalse(route.AllowMainLog, "benign missingParents count should not hit main log");
    }

    private static void TelemetryTagPolicyRoutesCampaignPlanAndSuccessionSidecarOnly()
    {
        var plan = TelemetryTagPolicy.Route("[Plan:Davis] strategy=Western obj=12 reason=score");
        AssertEqual(TelemetryLayer.Campaign, plan.Layer, "plan layer");
        AssertEqual(TelemetryCategory.Decision, plan.Category, "plan category");
        AssertTrue(plan.RouteToSidecar, "plan sidecar route");
        AssertFalse(plan.AllowMainLog, "plan main log");

        var succession = TelemetryTagPolicy.Route("[Succession:10] Hood replaces Johnston dateOk=True warStateOk=False");
        AssertEqual(TelemetryLayer.Campaign, succession.Layer, "succession layer");
        AssertEqual(TelemetryCategory.Decision, succession.Category, "succession category");
        AssertTrue(succession.RouteToSidecar, "succession sidecar route");
        AssertFalse(succession.AllowMainLog, "succession main log");
    }

    private static void Task7ExplicitCampaignGroupsUseTypedTelemetry()
    {
        string coordinator = ReadRepoFile("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        AssertFalse(coordinator.Contains("EmitCampaignInfo(\n                        $\"[ConstructionIntent]"), "construction intent typed");
        AssertFalse(coordinator.Contains("EmitCampaignInfo(\n                        $\"[ConstructionTelemetry]"), "construction telemetry typed");
        AssertFalse(coordinator.Contains("EmitCampaignInfo(\n                                $\"[DefenseIntent]"), "defense detail typed");
        AssertFalse(coordinator.Contains("EmitCampaignInfo(\n                        $\"[DefenseIntent]"), "defense summary typed");
        AssertFalse(coordinator.Contains("EmitCampaignInfo($\"[FiscalIntent]"), "fiscal intent typed");
        AssertFalse(coordinator.Contains("EmitCampaignInfo($\"[FiscalTelemetry]"), "fiscal telemetry typed");
        AssertFalse(coordinator.Contains("EmitCampaignInfo($\"[Director:trace]"), "director trace typed");
        AssertFalse(coordinator.Contains("EmitCampaignInfo(\n                        $\"[OperationalProbe]"), "operational probe typed");

        string objectivePatch = ReadRepoFile("src/WhiskeyRealism/Patches/PickCampaignObjectivePatch.cs");
        AssertFalse(objectivePatch.Contains("TelemetryRouter.LegacyInfo(\n                            $\"[HistoricalOperation]"), "historical operation patch typed");
        AssertFalse(objectivePatch.Contains("TelemetryRouter.LegacyInfo($\"[Plan]"), "campaign objective plan patch typed");

        string projectPatch = ReadRepoFile("src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs");
        AssertFalse(projectPatch.Contains("TelemetryRouter.LegacyInfo(\"[ProjectDoctrine]"), "project doctrine observer rows typed");
        AssertFalse(projectPatch.Contains("TelemetryRouter.LegacyInfo(\n                                $\"[ProjectDoctrine]"), "project doctrine starved lane typed");

        AssertNoLegacyTask7Tag("src/WhiskeyRealism/Patches/EconomyConstructionPatch.cs", "ConstructionTelemetry");
        AssertNoLegacyTask7Tag("src/WhiskeyRealism/Patches/FinancialAIPatch.cs", "FiscalTelemetry");
        AssertNoLegacyTask7Tag("src/WhiskeyRealism/Patches/PolicySelectionPatch.cs", "FiscalIntent");
        AssertNoLegacyTask7Tag("src/WhiskeyRealism/Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs", "DefenseIntent");
        AssertNoLegacyTask7Tag("src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs", "OperationalProbe");
        AssertNoLegacyTask7Tag("src/WhiskeyRealism/Patches/ImportanceValuesPatch.cs", "Plan");
        AssertNoLegacyTask7Tag("src/WhiskeyRealism/Patches/TransferOfUnitsPatch.cs", "Plan");
        AssertNoLegacyTask7Tag("src/WhiskeyRealism/Patches/CoordinatedOffensiveOperationsPatch.cs", "CoordinatedOps");
        AssertNoLegacyTask7Tag("src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs", "CoordinatedOps");
        AssertNoLegacyTask7Tag("src/WhiskeyRealism/Strategic/CoordinatedOperationRuntime.cs", "CoordinatedOps");
        AssertNoLegacyTask7Tag("src/WhiskeyRealism/Strategic/CIC.cs", "HistoricalOperation");
    }

    private static void AssertNoLegacyTask7Tag(string relativePath, string tag)
    {
        string source = ReadRepoFile(relativePath);
        string[] legacyMarkers =
        {
            "TelemetryRouter.LegacyInfo",
            "EmitCampaignInfo(",
            "EmitCampaignInfoOnce(",
            "LogInfo(",
            "Plugin.Log.LogInfo(",
            "Plugin.Log.LogWarning("
        };

        for (int markerIndex = 0; markerIndex < legacyMarkers.Length; markerIndex++)
        {
            string marker = legacyMarkers[markerIndex];
            int index = 0;
            while (true)
            {
                index = source.IndexOf(marker, index, StringComparison.Ordinal);
                if (index < 0) break;
                int length = Math.Min(400, source.Length - index);
                string segment = source.Substring(index, length);
                AssertFalse(segment.Contains("\"[" + tag + "]") ||
                    segment.Contains("$\"[" + tag + "]") ||
                    segment.Contains("\"[" + tag + ":") ||
                    segment.Contains("$\"[" + tag + ":"),
                    relativePath + " legacy " + tag + " row");
                if (marker == "Plugin.Log.LogInfo(" || marker == "Plugin.Log.LogWarning(")
                {
                    string plainName = RequiredTask7PlainName(tag);
                    AssertFalse(!string.IsNullOrEmpty(plainName) &&
                        segment.IndexOf(plainName, StringComparison.OrdinalIgnoreCase) >= 0,
                        relativePath + " direct plugin log " + plainName + " row");
                }
                index += marker.Length;
            }
        }
    }

    private static string RequiredTask7PlainName(string tag)
    {
        if (tag == "HistoricalOperation") return "Historical operation";
        if (tag == "CoordinatedOps") return "Coordinated operation";
        return null;
    }

    private static void AssertDeploymentRoute(string line, string expectedTag, TelemetryCategory expectedCategory)
    {
        var route = TelemetryTagPolicy.Route(line);
        AssertEqual(expectedTag, route.Tag, expectedTag + " tag");
        AssertEqual(TelemetryLayer.Tactical, route.Layer, expectedTag + " layer");
        AssertEqual(expectedCategory, route.Category, expectedTag + " category");
        AssertEqual(expectedTag, route.EventName, expectedTag + " event");
        AssertTrue(route.RouteToSidecar, expectedTag + " sidecar");
        AssertFalse(route.AllowMainLog, expectedTag + " main log");
    }

    private static void TelemetryLegacyOffSuppressesSidecarOnlyButAllowsSerious()
    {
        TelemetryRouter.AttachRuntime(null);
        Plugin.Log = new TestLog();

        AssertFalse(TelemetryRouter.LegacyInfo("[TacDeployObs] surface=DoPlacement phase=initial alliance=1"),
            "off profile suppresses sidecar-only deployment row from main log");
        AssertTrue(TelemetryRouter.LegacyWarning("[TacDeployTerrain] unit=brigade-4 failed missing terrain probe"),
            "serious deployment row is still allowed to main log");
    }

    private static void TelemetryTypedOffSuppressionWarnsOnce()
    {
        TelemetryRouter.AttachRuntime(null);
        var log = new TestLog();
        Plugin.Log = log;

        AssertFalse(TelemetryRouter.Emit(
            TelemetryLayer.Tactical,
            TelemetryCategory.Decision,
            "TacticalDecisionMatrix",
            TelemetrySeverity.Info,
            ev => ev.WithDecision("battle", "test", "sig")),
            "off profile suppresses typed tactical decision row");
        AssertEqual(1, log.WarningCount, "first typed suppression should warn once");

        AssertFalse(TelemetryRouter.Emit(
            TelemetryLayer.Tactical,
            TelemetryCategory.Decision,
            "TacticalCommandAssignment",
            TelemetrySeverity.Info,
            ev => ev.WithDecision("probe", "test", "sig2")),
            "off profile suppresses second typed tactical decision row");
        AssertEqual(1, log.WarningCount, "second typed suppression should not repeat warning");
    }

    private static void TelemetryLegacyUnavailableRuntimeKeepsProtectedFailuresVisible()
    {
        TelemetryRouter.AttachRuntime(null);
        Plugin.Log = new TestLog();

        AssertTrue(TelemetryRouter.LegacyWarning("[TacticalCommanderUnknown] echelon=division reason=unmapped-commander"),
            "protected failure rows should fall back to main log when sidecar emission is unavailable");
        AssertFalse(TelemetryRouter.LegacyInfo("[TacDeployObs] surface=DoPlacement phase=initial alliance=1"),
            "non-failure sidecar-only tuning rows stay suppressed under Off");

        string gameRoot = CreateTempDirectory();
        try
        {
            var config = TelemetryRuntimeConfig.Create(
                gameRoot,
                "0.2.2-test",
                "abcdef1234567890",
                TelemetryProfile.FullTuning,
                maxTuningLogMb: 4,
                fileRotateMb: 1,
                retainedSessions: 1,
                emitHumanSummary: false,
                performanceWarnings: false,
                createIssueBundleOnShutdown: false,
                warningCallback: null);

            TelemetryRuntime runtime = TelemetryRuntime.Start(config);
            TelemetryRouter.AttachRuntime(runtime);
            runtime.Shutdown("simulate-sidecar-unavailable");

            AssertTrue(TelemetryRouter.LegacyWarning("[TacticalCommanderUnknown] echelon=corps reason=stopped-runtime"),
                "protected failure rows should fall back to main log when sidecar emission fails");
            AssertFalse(TelemetryRouter.LegacyInfo("[TacDeployObs] surface=DoPlacement phase=initial alliance=1"),
                "non-failure sidecar-only tuning rows stay suppressed when stopped runtime cannot emit");
        }
        finally
        {
            TelemetryRouter.AttachRuntime(null);
            DeleteDirectoryQuietly(gameRoot);
        }
    }

    private static void DeploymentLegacyHelperSuppressesOffProfileTuningRows()
    {
        TelemetryRouter.AttachRuntime(null);
        var log = new TestLog();
        Plugin.Log = log;

        TelemetryRouter.LegacyInfoToMainLogIfAllowed(
            "[TacDeployObs] surface=DoPlacement phase=initial alliance=1",
            TelemetryLayer.Tactical);
        AssertEqual(0, log.InfoCount, "deployment tuning row should not leak to main log under Off");

        TelemetryRouter.LegacyInfoToMainLogIfAllowed(
            "[TacDeployTerrain] unit=brigade-4 failed missing terrain probe",
            TelemetryLayer.Tactical);
        AssertEqual(1, log.InfoCount, "serious deployment row should remain visible in main log");

        log.ThrowOnInfo = true;
        TelemetryRouter.LegacyInfoToMainLogIfAllowed(
            "[TacDeployTerrain] unit=brigade-5 failed missing terrain probe",
            TelemetryLayer.Tactical);
    }

    private static void OnceLogRetriesWhenLoggerUnavailable()
    {
        TelemetryRouter.AttachRuntime(null);
        OnceLog.Reset();
        Plugin.Log = null;

        OnceLog.Warning("once-retry-main-log", "[WhiskeyRealism] startup warning");

        var log = new TestLog();
        Plugin.Log = log;
        OnceLog.Warning("once-retry-main-log", "[WhiskeyRealism] startup warning");
        OnceLog.Warning("once-retry-main-log", "[WhiskeyRealism] startup warning");

        AssertEqual(1, log.WarningCount, "retry after null logger should log once");

        log.ThrowOnInfo = true;
        OnceLog.Info("once-retry-throw", "[WhiskeyRealism] startup info");
        log.ThrowOnInfo = false;
        OnceLog.Info("once-retry-throw", "[WhiskeyRealism] startup info");
        OnceLog.Info("once-retry-throw", "[WhiskeyRealism] startup info");

        AssertEqual(1, log.InfoCount, "retry after thrown logger should log once");
    }

    private static void OnceLogConcurrentCallsKeepOneTimeSemantics()
    {
        TelemetryRouter.AttachRuntime(null);
        OnceLog.Reset();
        var log = new TestLog();
        Plugin.Log = log;

        var tasks = new Task[32];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() => OnceLog.Info("once-concurrent-main-log", "[WhiskeyRealism] concurrent startup"));
        }

        Task.WaitAll(tasks);

        AssertEqual(1, log.InfoCount, "concurrent once-log calls should log once");

        OnceLog.Reset();
    }

    private static void TelemetryLegacyParserExtractsKeyValues()
    {
        TelemetryEvent ev;
        bool parsed = TelemetryLegacyParser.TryParse(
            "[TacticalPlayerOrder] battle=Gettysburg side=1 alliance=0 unit=brigade-2 phase=advance decision=Hold reason=player-order objective=\"Cemetery Hill\" active=true score=1.25",
            TelemetryProfile.TacticalTuning,
            "session-a",
            out ev);

        AssertTrue(parsed, "legacy row parsed");
        AssertEqual("session-a", ev.SessionId, "session");
        AssertEqual(TelemetryLayer.Tactical, ev.Layer, "layer");
        AssertEqual(TelemetryCategory.Decision, ev.Category, "category");
        AssertEqual("TacticalPlayerOrder", ev.EventName, "event");
        AssertEqual("Gettysburg", ev.BattleId, "battle");
        AssertEqual(1, ev.Side, "side");
        AssertEqual(0, ev.Alliance, "alliance");
        AssertEqual("brigade-2", ev.Unit, "unit");
        AssertEqual("advance", ev.Phase, "phase");
        AssertEqual("Hold", ev.Decision, "decision");
        AssertEqual("player-order", ev.DecisionReason, "reason");
        AssertEqual("Cemetery Hill", ev.Fields.GetString("objective"), "quoted key value");
        AssertTrue(ev.Fields.GetBool("active"), "bool key value");
        AssertEqual(1.25, ev.Fields.GetDouble("score"), "numeric key value");
    }

    private static void TelemetryLegacyParserCreatesCoarseSignature()
    {
        TelemetryEvent ev;
        bool parsed = TelemetryLegacyParser.TryParse(
            "[DefenseIntent] asset=nashville threat=raid posture=hold ratio=0.75",
            TelemetryProfile.CampaignTuning,
            "session-b",
            out ev);

        AssertTrue(parsed, "legacy row parsed");
        AssertEqual(TelemetryLayer.Campaign, ev.Layer, "layer");
        AssertEqual(TelemetryCategory.State, ev.Category, "category");
        AssertEqual("DefenseIntent", ev.EventName, "event");
        AssertEqual("coarse", ev.Fields.GetString("inputSignatureSource"), "signature source");
        AssertContains(ev.Fields.GetString("inputSignature"), "DefenseIntent", "signature event");
        AssertContains(ev.Fields.GetString("inputSignature"), "asset=nashville", "signature key");
        AssertContains(ev.Fields.GetString("inputSignature"), "posture=hold", "signature sorted key");
    }

    private static void TelemetryQueueNeverDropsHealthForLowerPriorityRows()
    {
        var queue = new TelemetryQueue(capacity: 2);
        queue.TryEnqueue(EventForQueue(TelemetryCategory.Trace, "trace-a"));
        queue.TryEnqueue(EventForQueue(TelemetryCategory.State, "state-a"));
        AssertTrue(queue.TryEnqueue(EventForQueue(TelemetryCategory.Health, "health-a")), "health accepted under pressure");

        var drained = queue.Drain(10);
        AssertEqual(3, drained.Count, "queue count");
        AssertTrue(drained.Exists(e => e.Category == TelemetryCategory.Health), "health preserved");
        AssertTrue(drained.Exists(e => e.EventName == "trace-a"), "health uses protected reserve; trace not evicted");
    }

    private static void TelemetryBudgetAppliesStagedCategoryCuts()
    {
        var traceBudget = new TelemetryBudget(totalBytes: 1000, rotateBytes: 250);
        traceBudget.RecordBytes(TelemetryCategory.Trace, 890);
        AssertFalse(traceBudget.Allow(TelemetryCategory.Trace, 20), "trace cuts above 90 percent");
        AssertTrue(traceBudget.Allow(TelemetryCategory.State, 20), "state survives trace cutoff");

        // Same order as TelemetryQueue.Rank: Performance rows go after Trace and before
        // State/Decision, so a full budget keeps doctrine rows over perf samples.
        var perfBudget = new TelemetryBudget(totalBytes: 1000, rotateBytes: 250);
        perfBudget.RecordBytes(TelemetryCategory.Performance, 925);
        AssertFalse(perfBudget.Allow(TelemetryCategory.Performance, 20), "performance cuts above 93 percent");
        AssertTrue(perfBudget.Allow(TelemetryCategory.State, 20), "state survives performance cutoff");
        AssertTrue(perfBudget.Allow(TelemetryCategory.Decision, 20), "decision survives performance cutoff");
        AssertTrue(perfBudget.Allow(TelemetryCategory.Performance, 20, lowPriority: true, protectedSummary: true), "performance summary stays in the protected reserve");

        var stateBudget = new TelemetryBudget(totalBytes: 1000, rotateBytes: 250);
        stateBudget.RecordBytes(TelemetryCategory.State, 940);
        AssertFalse(stateBudget.Allow(TelemetryCategory.State, 20), "state cuts above 95 percent");
        AssertTrue(stateBudget.Allow(TelemetryCategory.Decision, 20), "decision survives state cutoff");

        var decisionBudget = new TelemetryBudget(totalBytes: 1000, rotateBytes: 250);
        decisionBudget.RecordBytes(TelemetryCategory.Decision, 970);
        AssertFalse(decisionBudget.Allow(TelemetryCategory.Decision, 20), "decision cuts above 98 percent");
        AssertTrue(decisionBudget.Allow(TelemetryCategory.Gate, 20), "gate survives decision cutoff");
        AssertTrue(decisionBudget.Allow(TelemetryCategory.Write, 20), "write survives decision cutoff");

        var gateBudget = new TelemetryBudget(totalBytes: 1000, rotateBytes: 250);
        gateBudget.RecordBytes(TelemetryCategory.Gate, 990);
        AssertFalse(gateBudget.Allow(TelemetryCategory.Gate, 20), "gate cuts above total cap");
        AssertFalse(gateBudget.Allow(TelemetryCategory.Write, 20), "write cuts above total cap");
        AssertTrue(gateBudget.Allow(TelemetryCategory.Failure, 20, lowPriority: true, protectedSummary: true), "failure summary protected above total cap");
        AssertTrue(gateBudget.Allow(TelemetryCategory.Health, 20, lowPriority: true, protectedSummary: true), "health summary protected above total cap");
    }

    private static void TelemetryBudgetReserveIsAtomicUnderTotalCap()
    {
        var budget = new TelemetryBudget(totalBytes: 100, rotateBytes: 250);

        AssertTrue(budget.TryReserve(TelemetryCategory.Write, 60, lowPriority: false), "first reserve allowed");
        AssertFalse(budget.TryReserve(TelemetryCategory.Write, 60, lowPriority: false), "second reserve denied by projected total cap");
        AssertEqual(60L, budget.EmittedBytes, "only allowed reserve recorded emitted bytes");
        AssertEqual(60L, budget.CurrentFileBytes, "only allowed reserve recorded file bytes");
        AssertEqual(1L, budget.DroppedCountFor(TelemetryCategory.Write), "denied reserve counted dropped write");
    }

    private static void TelemetryQueueEvictsDecisionBeforeGateWrite()
    {
        var queue = new TelemetryQueue(capacity: 3);
        queue.TryEnqueue(EventForQueue(TelemetryCategory.Decision, "decision-a"));
        queue.TryEnqueue(EventForQueue(TelemetryCategory.Gate, "gate-a"));
        queue.TryEnqueue(EventForQueue(TelemetryCategory.Write, "write-a"));
        AssertTrue(queue.TryEnqueue(EventForQueue(TelemetryCategory.Failure, "failure-a")), "failure accepted under pressure");

        AssertTrue(queue.TryEnqueue(EventForQueue(TelemetryCategory.Gate, "gate-b")), "gate evicts decision under detail pressure");

        var drained = queue.Drain(10);
        AssertEqual(4, drained.Count, "queue count");
        AssertFalse(drained.Exists(e => e.EventName == "decision-a"), "decision evicted first");
        AssertTrue(drained.Exists(e => e.EventName == "gate-a"), "gate retained");
        AssertTrue(drained.Exists(e => e.EventName == "write-a"), "write retained");
        AssertTrue(drained.Exists(e => e.EventName == "failure-a"), "failure retained");
    }

    private static void TelemetryQueuePreservesProtectedIncomingWhenProtectedFull()
    {
        var queue = new TelemetryQueue(capacity: 2);
        AssertTrue(queue.TryEnqueue(EventForQueue(TelemetryCategory.Health, "health-a")), "initial health accepted");
        AssertTrue(queue.TryEnqueue(EventForQueue(TelemetryCategory.Failure, "failure-a")), "initial failure accepted");
        AssertTrue(queue.TryEnqueue(EventForQueue(TelemetryCategory.Health, "health-b")), "protected health bypasses detail capacity");
        AssertTrue(queue.TryEnqueue(EventForQueue(TelemetryCategory.Failure, "failure-b")), "protected failure bypasses detail capacity");

        var drained = queue.Drain(10);
        AssertEqual(4, drained.Count, "queue count includes protected overflow");
        AssertTrue(drained.Exists(e => e.EventName == "health-a"), "existing health retained");
        AssertTrue(drained.Exists(e => e.EventName == "failure-a"), "existing failure retained");
        AssertTrue(drained.Exists(e => e.EventName == "health-b"), "incoming health retained");
        AssertTrue(drained.Exists(e => e.EventName == "failure-b"), "incoming failure retained");
        AssertEqual(0L, queue.DroppedCount, "protected rows not dropped");
    }

    private static void TelemetryBudgetLowPriorityCutsBeforeHighPriority()
    {
        var decisionBudget = new TelemetryBudget(totalBytes: 1000, rotateBytes: 250);
        decisionBudget.RecordBytes(TelemetryCategory.Decision, 970);
        var overload = typeof(TelemetryBudget).GetMethod(
            "Allow",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(TelemetryCategory), typeof(long), typeof(bool) },
            null);
        AssertTrue(overload != null, "priority overload exists");
        AssertFalse((bool)overload.Invoke(decisionBudget, new object[] { TelemetryCategory.Decision, 20L, true }), "low priority decision cuts at staged threshold");
        AssertTrue((bool)overload.Invoke(decisionBudget, new object[] { TelemetryCategory.Decision, 20L, false }), "high priority decision continues until total cap");

        var writeBudget = new TelemetryBudget(totalBytes: 1000, rotateBytes: 250);
        writeBudget.RecordBytes(TelemetryCategory.Write, 970);
        AssertTrue((bool)overload.Invoke(writeBudget, new object[] { TelemetryCategory.Write, 20L, false }), "high priority write continues under total cap");
        AssertFalse((bool)overload.Invoke(writeBudget, new object[] { TelemetryCategory.Write, 40L, false }), "high priority write cuts over total cap");
    }

    private static void TelemetryQueueProtectedBypassWaitsUntilDetailCapacityFull()
    {
        var notFull = new TelemetryQueue(capacity: 2);
        AssertTrue(notFull.TryEnqueue(EventForQueue(TelemetryCategory.Trace, "trace-a")), "trace accepted");
        AssertTrue(notFull.TryEnqueue(EventForQueue(TelemetryCategory.Health, "health-a")), "health accepted without detail pressure");

        var notFullDrained = notFull.Drain(10);
        AssertEqual(2, notFullDrained.Count, "not-full queue count");
        AssertTrue(notFullDrained.Exists(e => e.EventName == "trace-a"), "trace retained while detail capacity not full");
        AssertTrue(notFullDrained.Exists(e => e.EventName == "health-a"), "health retained while detail capacity not full");
        AssertEqual(0L, notFull.DroppedCount, "not-full queue drops none");
        AssertEqual(0L, notFull.ProtectedOverflowCount, "not-full queue overflows none");

        var full = new TelemetryQueue(capacity: 2);
        AssertTrue(full.TryEnqueue(EventForQueue(TelemetryCategory.Trace, "trace-b")), "trace accepted under detail capacity");
        AssertTrue(full.TryEnqueue(EventForQueue(TelemetryCategory.State, "state-b")), "state accepted under detail capacity");
        AssertTrue(full.TryEnqueue(EventForQueue(TelemetryCategory.Health, "health-b")), "health accepted with full detail capacity");

        var fullDrained = full.Drain(10);
        AssertEqual(3, fullDrained.Count, "full queue keeps detail rows when protected row arrives");
        AssertTrue(fullDrained.Exists(e => e.EventName == "trace-b"), "detail not evicted by protected row");
        AssertTrue(fullDrained.Exists(e => e.EventName == "state-b"), "higher priority detail retained when health arrives");
        AssertTrue(fullDrained.Exists(e => e.EventName == "health-b"), "health retained when detail capacity full");
        AssertEqual(0L, full.DroppedCount, "full queue evicts no detail row for protected");
        AssertEqual(0L, full.DroppedCountFor(TelemetryCategory.Trace), "trace not dropped");
        AssertEqual(0L, full.ProtectedOverflowCount, "full queue has no protected overflow");
    }

    private static void TelemetryQueueBoundsProtectedBurstsWithOverflowCounts()
    {
        var queue = new TelemetryQueue(capacity: 2);
        for (int i = 0; i < 20; i++)
            AssertTrue(queue.Enqueue(EventForQueue(TelemetryCategory.Failure, "failure-" + i.ToString(CultureInfo.InvariantCulture))) != TelemetryQueueResult.Dropped, "protected accepted or coalesced " + i.ToString(CultureInfo.InvariantCulture));

        AssertTrue(queue.ProtectedReserveCapacity >= 4, "reserve has deterministic minimum");
        AssertTrue(queue.Count <= 2 + queue.ProtectedReserveCapacity, "protected burst bounded");
        AssertEqual(20L - queue.ProtectedReserveCapacity, queue.ProtectedOverflowCountFor(TelemetryCategory.Failure), "failure overflow counted");
        AssertEqual(20L - queue.ProtectedReserveCapacity, queue.ProtectedOverflowCount, "aggregate overflow counted");
        AssertEqual(0L, queue.DroppedCount, "protected overflow is not a drop");

        var drained = queue.Drain(100);
        AssertEqual(queue.ProtectedReserveCapacity, drained.Count, "drained bounded reserve");
        AssertTrue(drained.Exists(e => e.Category == TelemetryCategory.Failure), "protected evidence retained");
        AssertEqual(20L - queue.ProtectedReserveCapacity, queue.ProtectedOverflowCountFor(TelemetryCategory.Failure), "overflow survives drain for summary");
    }

    private static void TelemetryQueueDroppedCountReadIsStable()
    {
        var queue = new TelemetryQueue(capacity: 1);
        AssertTrue(queue.TryEnqueue(EventForQueue(TelemetryCategory.Gate, "gate-a")), "gate accepted");
        AssertFalse(queue.TryEnqueue(EventForQueue(TelemetryCategory.Trace, "trace-a")), "trace dropped below gate priority");
        AssertEqual(1L, queue.DroppedCount, "drop count read is locked");
        AssertEqual(1L, queue.DroppedCountFor(TelemetryCategory.Trace), "trace drop counted");
    }

    private static void TelemetryBudgetDroppedSnapshotIsStable()
    {
        var budget = new TelemetryBudget(totalBytes: 1000, rotateBytes: 250);
        budget.RecordDropped(TelemetryCategory.Trace);
        budget.RecordDropped(TelemetryCategory.Trace);
        budget.RecordDropped(TelemetryCategory.State);

        var snapshot = budget.DroppedSnapshot();
        AssertEqual(3L, budget.DroppedCount, "budget drop aggregate");
        AssertEqual(2L, budget.DroppedCountFor(TelemetryCategory.Trace), "trace drop count");
        AssertEqual(1L, budget.DroppedCountFor(TelemetryCategory.State), "state drop count");
        AssertEqual(2L, snapshot["Trace"], "snapshot trace count");
        AssertEqual(1L, snapshot["State"], "snapshot state count");

        budget.RecordDropped(TelemetryCategory.Failure);
        AssertFalse(snapshot.ContainsKey("Failure"), "snapshot is a stable copy");
    }

    private static void TelemetryBudgetCapsRawProtectedDetailButAllowsSummaries()
    {
        var overload = typeof(TelemetryBudget).GetMethod(
            "Allow",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(TelemetryCategory), typeof(long), typeof(bool), typeof(bool) },
            null);
        AssertTrue(overload != null, "protected summary overload exists");

        var failureBudget = new TelemetryBudget(totalBytes: 1000, rotateBytes: 250);
        failureBudget.RecordBytes(TelemetryCategory.Write, 1000);
        AssertFalse(failureBudget.Allow(TelemetryCategory.Failure, 1), "raw failure detail capped by total bytes");
        AssertTrue((bool)overload.Invoke(failureBudget, new object[] { TelemetryCategory.Failure, 1L, true, true }), "failure summary allowed past cap");

        var healthBudget = new TelemetryBudget(totalBytes: 1000, rotateBytes: 250);
        healthBudget.RecordBytes(TelemetryCategory.Write, 1001);
        AssertFalse((bool)overload.Invoke(healthBudget, new object[] { TelemetryCategory.Health, 1L, true, false }), "raw health detail capped past total bytes");
        AssertTrue((bool)overload.Invoke(healthBudget, new object[] { TelemetryCategory.Health, 1L, true, true }), "health summary allowed past cap");
    }

    private static void TelemetryBudgetBoundsProtectedSummaryReserve()
    {
        var budget = new TelemetryBudget(totalBytes: 1024, rotateBytes: 256);
        budget.RecordBytes(TelemetryCategory.Write, 1024);

        AssertFalse(budget.TryReserve(TelemetryCategory.Failure, 1, lowPriority: true, protectedSummary: false), "raw failure detail remains capped");
        AssertTrue(budget.TryReserve(TelemetryCategory.Failure, 900, lowPriority: true, protectedSummary: true), "first protected summary survives consumed detail cap");

        int accepted = 1;
        for (int i = 0; i < 32; i++)
        {
            if (budget.TryReserve(TelemetryCategory.Failure, 900, lowPriority: true, protectedSummary: true))
                accepted++;
        }

        AssertTrue(accepted < 33, "protected summaries eventually hit reserve cap");
        AssertTrue(budget.DroppedCountFor(TelemetryCategory.Failure) >= 1L, "protected summary reserve drops are counted");
        AssertTrue(budget.ProtectedSummaryBytes > 0L, "protected summary bytes tracked separately");
        AssertTrue(budget.ProtectedSummaryBytes <= budget.ProtectedSummaryReserveBytes, "protected summary bytes stay within reserve cap");
    }

    private static void TelemetryQueueReportsCoalescedProtectedOverflow()
    {
        var queue = new TelemetryQueue(capacity: 2);
        TelemetryQueueResult last = TelemetryQueueResult.Enqueued;
        for (int i = 0; i < queue.ProtectedReserveCapacity + 3; i++)
            last = queue.Enqueue(EventForQueue(TelemetryCategory.Failure, "failure-coalesce-" + i.ToString(CultureInfo.InvariantCulture)));

        AssertEqual(TelemetryQueueResult.Coalesced, last, "overflow enqueue result");
        AssertTrue(queue.TryEnqueue(EventForQueue(TelemetryCategory.Failure, "failure-wrapper")) == false, "compat wrapper reports coalesced as not enqueued");

        var snapshot = queue.ProtectedOverflowSnapshot();
        AssertEqual(4L, snapshot["Failure"], "snapshot reports coalesced failure rows");
        AssertEqual(4L, queue.ProtectedOverflowCountFor(TelemetryCategory.Failure), "direct overflow count");
        AssertEqual(4L, queue.ProtectedOverflowCount, "aggregate overflow count");
        AssertTrue(queue.Count <= 2 + queue.ProtectedReserveCapacity, "queue remains bounded");
    }

    private static void TelemetryBudgetCapsRawPerformanceButAllowsSummaries()
    {
        var budget = new TelemetryBudget(totalBytes: 1000, rotateBytes: 250);
        budget.RecordBytes(TelemetryCategory.Write, 1000);
        AssertFalse(budget.Allow(TelemetryCategory.Performance, 1), "raw performance detail capped by total bytes");
        AssertFalse(budget.Allow(TelemetryCategory.Performance, 1, lowPriority: false, protectedSummary: false), "high priority raw performance detail capped by total bytes");
        AssertTrue(budget.Allow(TelemetryCategory.Performance, 1, lowPriority: true, protectedSummary: true), "performance summary allowed past cap");
    }

    private static void TelemetryEnumContractsMatchPlan()
    {
        AssertEnumContract(typeof(TelemetryProfile),
            ("Off", 0),
            ("TacticalTuning", 1),
            ("CampaignTuning", 2),
            ("FullTuning", 3));
        AssertEnumContract(typeof(TelemetryLayer),
            ("System", 0),
            ("Tactical", 1),
            ("Campaign", 2));
        AssertEnumContract(typeof(TelemetryCategory),
            ("Health", 0),
            ("Failure", 1),
            ("Performance", 2),
            ("Decision", 3),
            ("Gate", 4),
            ("Write", 5),
            ("State", 6),
            ("Trace", 7));
        AssertEnumContract(typeof(TelemetrySeverity),
            ("Debug", 0),
            ("Info", 1),
            ("Warning", 2),
            ("Error", 3));
    }

    private static void TelemetryJsonWritesRequiredFields()
    {
        var ev = TelemetryEvent.Create(
            "session-1", TelemetryProfile.TacticalTuning, TelemetryLayer.Tactical,
            TelemetryCategory.Decision, "CommandAssignment", TelemetrySeverity.Info)
            .WithCampaignDate("1861-06-01")
            .WithBattleId("battle-1")
            .WithSide(0)
            .WithAlliance(1)
            .WithDecision("Probe", "support-required", "sig-123");
        string json = TelemetryJson.ToJsonLine(ev);
        AssertContains(json, "\"schema\":\"wr.telemetry.v1\"", "schema");
        AssertContains(json, "\"schema\":\"wr.telemetry.v1\",\"ts\":\"", "ts immediately after schema");
        AssertContains(json, "\"sessionId\":\"session-1\"", "session");
        AssertContains(json, "\"layer\":\"Tactical\"", "layer");
        AssertContains(json, "\"inputSignature\":\"sig-123\"", "input signature");
        AssertTrue(json.EndsWith("\n"), "json line should end with newline");

        var parsed = ParseJsonLine(json);
        foreach (string required in new[]
        {
            "schema", "ts", "sessionId", "profile", "layer", "category", "event", "severity",
            "campaignDate", "battleId", "side", "alliance", "unit", "phase", "durationMs",
            "decision", "reason", "inputSignature", "fields"
        })
        {
            AssertTrue(parsed.Property(required) != null, "missing required telemetry field " + required);
        }

        AssertEqual("wr.telemetry.v1", parsed.Value<string>("schema"), "schema json");
        AssertEqual("session-1", parsed.Value<string>("sessionId"), "session json");
        AssertEqual("TacticalTuning", parsed.Value<string>("profile"), "profile json");
        AssertEqual("Tactical", parsed.Value<string>("layer"), "layer json");
        AssertEqual("Decision", parsed.Value<string>("category"), "category json");
        AssertEqual("CommandAssignment", parsed.Value<string>("event"), "event json");
        AssertEqual("Info", parsed.Value<string>("severity"), "severity json");
        AssertEqual("sig-123", parsed.Value<string>("inputSignature"), "input signature json");

        string ts = parsed.Value<string>("ts");
        var parsedTs = DateTime.ParseExact(ts, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        AssertEqual(DateTimeKind.Utc, parsedTs.Kind, "ts utc kind");
    }

    private static void TelemetryJsonEscapesStringsSafely()
    {
        var ev = TelemetryEvent.Create(
            "s", TelemetryProfile.CampaignTuning, TelemetryLayer.Campaign,
            TelemetryCategory.Failure, "Serializer", TelemetrySeverity.Warning)
            .WithField("message", "quote=\" slash=\\ newline=\n tab=\t");
        string json = TelemetryJson.ToJsonLine(ev);
        AssertContains(json, "quote=\\\"", "quote escaped");
        AssertContains(json, "slash=\\\\", "slash escaped");
        AssertContains(json, "newline=\\n", "newline escaped");
        AssertContains(json, "tab=\\t", "tab escaped");
    }

    private static void TelemetryFieldsSanitizeNullsAndNonfiniteNumbers()
    {
        var fields = new TelemetryFields()
            .Add("missing", (string)null)
            .Add("nan", float.NaN)
            .Add("inf", float.PositiveInfinity)
            .Add("ok", 1.25f);
        AssertEqual("-", fields.GetString("missing"), "null string");
        AssertEqual(0.0, fields.GetDouble("nan"), "nan");
        AssertEqual(0.0, fields.GetDouble("inf"), "inf");
        AssertEqual(1.25, fields.GetDouble("ok"), "finite");
        AssertTrue(fields.GetBool("invalidFloat"), "invalid float marker");
    }

    private static void TelemetryDurationMarksInvalidFloat()
    {
        var ev = TelemetryEvent.Create(
            "s", TelemetryProfile.FullTuning, TelemetryLayer.Tactical,
            TelemetryCategory.Performance, "Duration", TelemetrySeverity.Warning)
            .WithDurationMs(double.PositiveInfinity);

        AssertEqual(0.0, ev.DurationMs, "duration sanitizes infinity");
        AssertTrue(ev.Fields.GetBool("invalidFloat"), "duration invalid float marker");

        var parsed = ParseJsonLine(TelemetryJson.ToJsonLine(ev));
        AssertEqual(0.0, parsed.Value<double>("durationMs"), "json duration sanitizes infinity");
        AssertTrue(parsed["fields"].Value<bool>("invalidFloat"), "json duration invalid float marker");
    }

    private static void TelemetryDecisionRequiresInputSignature()
    {
        // WS P Router: a blank signature is flagged on the row instead of throwing (the throw
        // ran inside TelemetryRouter.Emit's configure callback and silently dropped the row).
        var ev = TelemetryEvent.Create(
            "s", TelemetryProfile.FullTuning, TelemetryLayer.Tactical,
            TelemetryCategory.Decision, "DecisionWithoutSignature", TelemetrySeverity.Info)
            .WithDecision("Attack", "test", "");
        AssertEqual(TelemetryEvent.MissingInputSignature, ev.InputSignature, "decision rows mark blank input signatures");
        AssertTrue(ev.Fields.GetBool("inputSignatureMissing"), "decision rows flag blank input signatures");
    }

    private static void AssertEnumContract(Type enumType, params (string name, int value)[] expected)
    {
        string[] names = Enum.GetNames(enumType);
        AssertEqual(expected.Length, names.Length, enumType.Name + " enum member count");
        for (int i = 0; i < expected.Length; i++)
        {
            AssertEqual(expected[i].name, names[i], enumType.Name + " enum member name " + i);
            int actualValue = (int)Enum.Parse(enumType, expected[i].name);
            AssertEqual(expected[i].value, actualValue, enumType.Name + "." + expected[i].name + " value");
        }
    }
}
