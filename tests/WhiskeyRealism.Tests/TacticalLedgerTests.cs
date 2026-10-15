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

internal static class TacticalLedgerTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("tactical telemetry maps macro names", TacticalTelemetryMapsMacroNames),
            ("tactical telemetry summary handles null", TacticalTelemetrySummaryHandlesNull),
            ("tactical telemetry maps player-order prefix", TacticalTelemetryMapsPlayerOrderPrefix),
            ("tactical telemetry maps command prefix", TacticalTelemetryMapsCommandPrefix),
            ("tactical telemetry maps odds prefix", TacticalTelemetryMapsOddsPrefix),
            ("tactical telemetry signature changes on material fields", TacticalTelemetrySignatureChangesOnMaterialFields),
            ("tactical telemetry signature changes on command signature", TacticalTelemetrySignatureChangesOnCommandSignature),
            ("tactical telemetry decision input signature is stable", TacticalTelemetryDecisionInputSignatureIsStable),
            ("tactical telemetry throttle suppresses repeated signature", TacticalTelemetryThrottleSuppressesRepeatedSignature),
            ("tactical telemetry delta formats before after counts", TacticalTelemetryDeltaFormatsBeforeAfterCounts),
            ("tactical objective unverified bridge downgrades to generic", TacticalObjectiveUnverifiedBridgeDowngrades),
            ("tactical objective verified bridge drives typed scoring", TacticalObjectiveVerifiedBridgeDrivesTypedScoring),
            ("tactical objective input sanitizes nonfinite values", TacticalObjectiveInputSanitizesNonfiniteValues),
            ("tactical decision doctrine classifies contact gates", TacticalDecisionDoctrineClassifiesContactGates),
            ("tactical decision doctrine gates soft abort", TacticalDecisionDoctrineGatesSoftAbort),
            ("tactical defensive line anchor shifts road corridor forward", TacticalDefensiveLineAnchorShiftsRoadCorridorForward),
            ("tactical scouting avenue sends screen forward and reserve rear", TacticalScoutingAvenueSendsScreenForwardAndReserveRear),
            ("tactical massing cycle waits for support before commit", TacticalMassingCycleWaitsForSupportBeforeCommit),
            ("tactical massing cycle blocks commit when endurance needs relief", TacticalMassingCycleBlocksCommitWhenEnduranceNeedsRelief),
            ("tactical fallback ladder separates fallback from retreat", TacticalFallbackLadderSeparatesFallbackFromRetreat),
            ("tactical artillery mission planner gates support by ammo range and danger", TacticalArtilleryMissionPlannerGatesSupportByAmmoRangeAndDanger),
            ("tactical artillery command assigns weak point ammo and reposition", TacticalArtilleryCommandAssignsWeakPointAmmoAndReposition),
            ("tactical contact no sighting is none", TacticalContactNoSightingIsNone),
            ("tactical contact stale sighting ages down", TacticalContactStaleSightingAgesDown),
            ("tactical odds no contact avoids assault", TacticalOddsNoContactAvoidsAssault),
            ("tactical odds global superiority selects one decisive sector", TacticalOddsGlobalSuperioritySelectsOneDecisiveSector),
            ("tactical odds inferior no relief preserves force", TacticalOddsInferiorNoReliefPreservesForce),
            ("tactical odds inferior with relief delays", TacticalOddsInferiorWithReliefDelays),
            ("tactical group visible line contact drives weak point", TacticalGroupVisibleLineContactDrivesWeakPoint),
            ("tactical group named detachment line contact drives weak point", TacticalGroupNamedDetachmentLineContactDrivesWeakPoint),
            ("tactical group battle command contact drives weak point", TacticalGroupBattleCommandContactDrivesWeakPoint),
            ("tactical group screen contact does not drive weak point", TacticalGroupScreenContactDoesNotDriveWeakPoint),
            ("tactical macro dynamic is not attack", TacticalMacroDynamicIsNotAttack),
            ("tactical macro debug override skips", TacticalMacroDebugOverrideSkips),
            ("tactical macro inferior no relief retreats", TacticalMacroInferiorNoReliefRetreats),
            ("tactical group decisive sector attacks without charge", TacticalGroupDecisiveSectorAttacksWithoutCharge),
            ("tactical group defensive visible weak point counterattacks", TacticalGroupDefensiveVisibleWeakPointCounterattacks),
            ("tactical group weak point under defend holds", TacticalGroupWeakPointUnderDefendHolds),
            ("tactical group fix under defend holds", TacticalGroupFixUnderDefendHolds),
            ("tactical group local stance writer only controls brigades", TacticalGroupLocalStanceWriterOnlyControlsBrigades),
            ("tactical group retreat macro keeps vanilla", TacticalGroupRetreatMacroKeepsVanilla),
            ("tactical group explicit probe bypasses low confidence skip", TacticalGroupExplicitProbeBypassesLowConfidenceSkip),
            ("tactical group low confidence keeps vanilla", TacticalGroupLowConfidenceKeepsVanilla),
            ("tactical group sector estimator pre-contact reaches act floor", TacticalGroupSectorEstimatorPreContactReachesActFloor),
            ("tactical group doctrine issues pre-contact hold when no enemy visible", TacticalGroupDoctrineIssuesPreContactHoldWhenNoEnemyVisible),
            ("tactical group doctrine pre-contact preserves any vanilla stance", TacticalGroupDoctrinePreContactPreservesAnyVanillaStance),
            ("tactical group doctrine still rejects below low-confidence floor", TacticalGroupDoctrineStillRejectsBelowLowConfidenceFloor),
            ("tactical group doctrine line contact still reaches attack-weak-point", TacticalGroupDoctrineLineContactStillReachesAttackWeakPoint),
            ("tactical group wl player subordinate skips", TacticalGroupWlPlayerSubordinateSkips),
            ("tactical reserve doctrine deny removal ignores order delays", TacticalReserveDoctrineDenyRemovalIgnoresOrderDelays),
            ("tactical reserve delay guard does not consume doctrine deny movement", TacticalReserveDelayGuardDoesNotConsumeDoctrineDenyMovement),
            ("tactical gate helpers W&L ownership", TacticalGateHelpersWlOwnership),
            ("tactical support screen supported and steady", TacticalSupportScreenSupportedAndSteady),
            ("tactical support screen shaken with screen", TacticalSupportScreenShakenWithScreen),
            ("tactical support screen unsupported no screen", TacticalSupportScreenUnsupportedNoScreen),
            ("tactical support screen unknown on uninitialized", TacticalSupportScreenUnknownOnUninitialized),
            ("tactical support screen W&L gate blocks", TacticalSupportScreenWlGateBlocks),
            ("tactical artillery doctrine preserves fire when screened and ammo ok", TacticalArtilleryDoctrinePreservesFireWhenScreenedAndAmmoOk),
            ("tactical artillery doctrine counterbattery when enemy art visible", TacticalArtilleryDoctrineCounterBatteryWhenEnemyArtVisible),
            ("tactical artillery doctrine cancel bombard when unsupported", TacticalArtilleryDoctrineCancelBombardWhenUnsupported),
            ("tactical artillery doctrine defensive fallback when shaken and unsupported", TacticalArtilleryDoctrineDefensiveFallbackWhenShakenAndUnsupported),
            ("tactical artillery doctrine cancel bombard on low ammo", TacticalArtilleryDoctrineCancelBombardOnLowAmmo),
            ("tactical artillery doctrine W&L gate blocks", TacticalArtilleryDoctrineWlGateBlocks),
            ("tactical artillery doctrine friendly close deny cancels bombard", TacticalArtilleryDoctrineFriendlyCloseDenyCancelsBombard),
            ("tactical artillery doctrine support main effort suppresses strongpoint", TacticalArtilleryDoctrineSupportMainEffortSuppressesStrongpoint),
            ("tactical morale pressure stable", TacticalMoralePressureStable),
            ("tactical morale pressure under pressure from outflanked tier", TacticalMoralePressureUnderPressureFromOutflankedTier),
            ("tactical morale pressure fallback candidate", TacticalMoralePressureFallbackCandidate),
            ("tactical morale pressure withdrawal candidate flank no cover", TacticalMoralePressureWithdrawalCandidateFlankNoCover),
            ("tactical morale pressure collapse candidate", TacticalMoralePressureCollapseCandidate),
            ("tactical morale pressure stable on uninitialized defer to caller", TacticalMoralePressureStableOnUninitializedDeferToCaller),
            ("tactical withdrawal input adapter to morale pressure input", TacticalWithdrawalInputAdapterToMoralePressureInput),
            ("tactical withdrawal doctrine hold line when stable", TacticalWithdrawalDoctrineHoldLineWhenStable),
            ("tactical withdrawal doctrine stabilize under pressure", TacticalWithdrawalDoctrineStabilizeUnderPressure),
            ("tactical withdrawal doctrine screen for fallback candidate", TacticalWithdrawalDoctrineScreenForFallbackCandidate),
            ("tactical withdrawal doctrine rear guard for withdrawal candidate", TacticalWithdrawalDoctrineRearGuardForWithdrawalCandidate),
            ("tactical withdrawal doctrine full retreat on collapse", TacticalWithdrawalDoctrineFullRetreatOnCollapse),
            ("tactical withdrawal doctrine rear pressure bumps ladder", TacticalWithdrawalDoctrineRearPressureBumpsLadder),
            ("tactical withdrawal doctrine W&L gate blocks", TacticalWithdrawalDoctrineWlGateBlocks),
            ("tactical support screen quiet when no enemy and no screen", TacticalSupportScreenQuietWhenNoEnemyAndNoScreen),
            ("tactical unit type constants match vanilla unittyp", TacticalUnitTypeConstantsMatchVanillaUnittyp),
            ("tactical diagnostics detect campaign current order replacement risk", TacticalDiagnosticsDetectCampaignCurrentOrderReplacementRisk),
            ("tactical diagnostics detect delayed waypoint drift", TacticalDiagnosticsDetectDelayedWaypointDrift),
            ("tactical diagnostics detect secondary courier queue mismatch risk", TacticalDiagnosticsDetectSecondaryCourierQueueMismatchRisk),
            ("tactical diagnostics detect objective chain player subordinate risk", TacticalDiagnosticsDetectObjectiveChainPlayerSubordinateRisk),
            ("tactical diagnostics detect objective chain movement mutation proof", TacticalDiagnosticsDetectObjectiveChainMovementMutationProof),
            ("tactical diagnostics detect reserve direct path delay bypass", TacticalDiagnosticsDetectReserveDirectPathDelayBypass),
            ("tactical diagnostics detect pathfinder backtrack shape", TacticalDiagnosticsDetectPathfinderBacktrackShape),
            ("tactical diagnostics suppress only tactical null fallback exceptions", TacticalDiagnosticsSuppressOnlyTacticalNullFallbackExceptions),
            ("tactical diagnostics handle empty null and sanitized values", TacticalDiagnosticsHandleEmptyNullAndSanitizedValues),
            ("tactical withdrawal input adapter to quadrant input", TacticalWithdrawalInputAdapterToQuadrantInput),
            ("tactical fatigue state bands", TacticalFatigueStateBands),
            ("tactical fatigue state clamps below", TacticalFatigueStateClampsBelow),
            ("tactical fatigue state clamps above", TacticalFatigueStateClampsAbove),
            ("tactical intent model records all fields", TacticalIntentModelRecordsAllFields),
            ("tactical intent model clamps confidence and age", TacticalIntentModelClampsConfidenceAndAge),
            ("tactical intent model unknown primary intent sentinel", TacticalIntentModelUnknownPrimaryIntentSentinel),
            ("tactical reserve commit gate observes when vanilla did not move", TacticalReserveCommitGateObservesWhenNoVanillaMove),
            ("tactical reserve commit gate denies reserve role movement", TacticalReserveCommitGateDeniesReserveRoleMovement),
            ("tactical reserve commit gate allows main understrength movement", TacticalReserveCommitGateAllowsMainUnderstrengthMovement),
            ("tactical reserve commit gate allows fallback screen movement", TacticalReserveCommitGateAllowsFallbackScreenMovement),
            ("tactical reserve commit gate observes player controlled group", TacticalReserveCommitGateObservesPlayerControlledGroup),
            ("tactical reserve commit gate allows already engaged reserve", TacticalReserveCommitGateAllowsAlreadyEngagedReserve),
        });
    }

    private static void TacticalTelemetryMapsMacroNames()
    {
        AssertEqual("dynamic", TacticalTelemetry.MacroName(-1), "macro -1");
        AssertEqual("assault", TacticalTelemetry.MacroName(0), "macro 0");
        AssertEqual("attack", TacticalTelemetry.MacroName(1), "macro 1");
        AssertEqual("defend", TacticalTelemetry.MacroName(2), "macro 2");
        AssertEqual("retreat", TacticalTelemetry.MacroName(3), "macro 3");
        AssertEqual("unknown", TacticalTelemetry.MacroName(99), "macro unknown");
    }

    private static void TacticalTelemetrySummaryHandlesNull()
    {
        string summary = TacticalTelemetry.Summary(TacticalObservedEvent.Macro, null);
        AssertContains(summary, "[TacticalMacro]", "prefix");
        AssertContains(summary, "side=-1", "empty side");
        AssertContains(summary, "macro=unknown", "empty macro");
        AssertContains(summary, "sectorSource=none", "empty sector source");
    }

    private static void TacticalTelemetryMapsPlayerOrderPrefix()
    {
        string summary = TacticalTelemetry.Summary(TacticalObservedEvent.PlayerOrder, TacticalBattleContext.Empty());
        AssertContains(summary, "[TacticalPlayerOrder]", "player-order prefix");
    }

    private static void TacticalTelemetryMapsCommandPrefix()
    {
        var context = TacticalBattleContext.Empty();
        context.CommandSignature = "src=Division,tgt=Brigade,scope=SubcommandAction";

        string summary = TacticalTelemetry.Summary(TacticalObservedEvent.Command, context);

        AssertTrue(summary.StartsWith("[TacticalCommand]"), "command prefix");
        AssertContains(summary, "commandSig=src=Division,tgt=Brigade,scope=SubcommandAction", "command signature");
    }

    private static void TacticalTelemetryMapsOddsPrefix()
    {
        var context = TacticalBattleContext.Empty();
        context.OddsSummary = "posture=probe";

        string summary = TacticalTelemetry.Summary(TacticalObservedEvent.Odds, context);

        AssertTrue(summary.StartsWith("[TacticalOdds]"), "odds prefix");
        AssertContains(summary, "odds=posture=probe", "odds summary");
    }

    private static void TacticalTelemetrySignatureChangesOnMaterialFields()
    {
        var baseline = new TacticalBattleContext
        {
            Side = 1,
            Alliance = 0,
            MacroAi = -1,
            GroupCount = 4,
            SectorSource = TacticalSectorSource.ObjectiveChain,
            SectorSignature = "chains=2"
        };
        var changed = new TacticalBattleContext
        {
            Side = 1,
            Alliance = 0,
            MacroAi = 1,
            GroupCount = 4,
            SectorSource = TacticalSectorSource.ObjectiveChain,
            SectorSignature = "chains=2"
        };

        string a = TacticalTelemetry.Signature(TacticalObservedEvent.Macro, baseline);
        string b = TacticalTelemetry.Signature(TacticalObservedEvent.Macro, changed);
        if (a == b) throw new Exception("expected tactical signature to change when macro changes");
    }

    private static void TacticalTelemetrySignatureChangesOnCommandSignature()
    {
        var first = TacticalBattleContext.Empty();
        first.CommandSignature = "src=Division,tgt=Brigade,scope=SubcommandAction";
        var second = TacticalBattleContext.Empty();
        second.CommandSignature = "src=Corps,tgt=Brigade,scope=BlockDirectRegimentRetask";

        string a = TacticalTelemetry.Signature(TacticalObservedEvent.Command, first);
        string b = TacticalTelemetry.Signature(TacticalObservedEvent.Command, second);
        if (a == b) throw new Exception("expected tactical signature to change when command signature changes");
    }

    private static void TacticalTelemetryDecisionInputSignatureIsStable()
    {
        string first = TacticalTelemetry.DecisionInputSignature("ChargeGate", 1, "unit-7", 1.21f, null, true);
        string second = TacticalTelemetry.DecisionInputSignature("ChargeGate", 1, "unit-7", 1.24f, string.Empty, true);
        string changed = TacticalTelemetry.DecisionInputSignature("ChargeGate", 1, "unit-7", 1.76f, null, true);

        AssertEqual("ChargeGate|1|unit-7|1.0|-|True", first, "stable signature text");
        AssertEqual(first, second, "bucketed float and empty/null match");
        if (first == changed) throw new Exception("expected material float bucket to change signature");
    }

    private static void TacticalTelemetryThrottleSuppressesRepeatedSignature()
    {
        var emitted = new Dictionary<string, float>();
        bool first = TacticalTelemetry.ShouldEmit(emitted, "macro", "sig", 10f, 30f, verbose: false);
        bool second = TacticalTelemetry.ShouldEmit(emitted, "macro", "sig", 20f, 30f, verbose: false);
        bool third = TacticalTelemetry.ShouldEmit(emitted, "macro", "sig", 41f, 30f, verbose: false);
        if (!first) throw new Exception("expected first tactical signature emit");
        if (second) throw new Exception("expected repeated tactical signature to be throttled");
        if (!third) throw new Exception("expected tactical signature to emit after throttle window");

        // Verbose shortens the window to 1 s but still throttles same-second repeats.
        var verboseEmitted = new Dictionary<string, float>();
        bool vFirst = TacticalTelemetry.ShouldEmit(verboseEmitted, "macro", "sig", 10f, 30f, verbose: true);
        bool vSameSecond = TacticalTelemetry.ShouldEmit(verboseEmitted, "macro", "sig", 10.5f, 30f, verbose: true);
        bool vNextSecond = TacticalTelemetry.ShouldEmit(verboseEmitted, "macro", "sig", 11f, 30f, verbose: true);
        if (!vFirst) throw new Exception("expected first verbose tactical signature emit");
        if (vSameSecond) throw new Exception("expected verbose repeat within 1 s to be throttled");
        if (!vNextSecond) throw new Exception("expected verbose repeat after 1 s to emit");
    }

    private static void TacticalTelemetryDeltaFormatsBeforeAfterCounts()
    {
        string delta = TacticalTelemetry.Delta(
            new TacticalObserverSnapshot { GroupCount = 2, ChargingCount = 0, ReserveGroupCount = 1 },
            new TacticalObserverSnapshot { GroupCount = 2, ChargingCount = 1, ReserveGroupCount = 2 });

        AssertContains(delta, "groups=2->2", "group delta");
        AssertContains(delta, "charging=0->1", "charging delta");
        AssertContains(delta, "reserves=1->2", "reserve delta");
    }

    private static void TacticalObjectiveUnverifiedBridgeDowngrades()
    {
        var input = new ObjectiveObservationInput(
            "bridge-a",
            TacticalObjectiveType.Bridge,
            TacticalObjectiveSource.ObjectiveChain,
            new TacticalMapPoint(10f, 20f),
            0.8f,
            1.0f,
            typeAnchorVerified: false);

        var result = TacticalObjectiveSourceModel.Normalize(input);

        AssertEqual(TacticalObjectiveType.UnknownVanillaObjective, result.Type, "type");
        AssertTrue(result.Value <= 0.35f, "unverified POI value capped");
    }

    private static void TacticalObjectiveVerifiedBridgeDrivesTypedScoring()
    {
        var input = new ObjectiveObservationInput(
            "bridge-a",
            TacticalObjectiveType.Bridge,
            TacticalObjectiveSource.VerifiedSceneObject,
            new TacticalMapPoint(10f, 20f),
            0.9f,
            1.0f,
            typeAnchorVerified: true);

        var result = TacticalObjectiveSourceModel.Normalize(input);

        AssertEqual(TacticalObjectiveType.Bridge, result.Type, "type");
    }

    private static void TacticalObjectiveInputSanitizesNonfiniteValues()
    {
        var input = new ObjectiveObservationInput(
            "",
            TacticalObjectiveType.EnemyLine,
            TacticalObjectiveSource.VisibleEnemyLine,
            new TacticalMapPoint(float.NaN, float.PositiveInfinity),
            float.PositiveInfinity,
            float.NaN,
            typeAnchorVerified: true);

        AssertEqual("objective-unknown", input.ObjectiveId, "id");
        AssertEqual(0f, input.Location.X, "x");
        AssertEqual(0f, input.Location.Z, "z");
        AssertEqual(0f, input.SourceConfidence, "confidence");
        AssertEqual(0f, input.Value, "value");
    }

    private static void TacticalDecisionDoctrineClassifiesContactGates()
    {
        var screen = new BattlefieldObjectiveEstimate("screen-contact", TacticalObjectiveType.EnemyLine, 180f, 0.45f, false, 0.55f, 300f, 400f, 0.1f, 0.1f);
        var highConfidenceScreen = new BattlefieldObjectiveEstimate("high-screen", TacticalObjectiveType.EnemyLine, 180f, 0.80f, false, 0.55f, 300f, 400f, 0.1f, 0.1f);
        var formedLine = new BattlefieldObjectiveEstimate("formed-line", TacticalObjectiveType.EnemyLine, 1100f, 0.80f, true, 0.65f, 300f, 400f, 0.1f, 0.1f);
        var stale = new BattlefieldObjectiveEstimate("stale", TacticalObjectiveType.EnemyLine, 0f, 0.30f, false, 0.65f, 300f, 400f, 0.1f, 0.1f);

        AssertEqual(
            TacticalObjectiveGate.ReconnaissanceContact,
            TacticalDecisionDoctrine.ClassifyObjective(screen, ownStrength: 5000f, reserveFraction: 0.30f),
            "screen contact");
        AssertEqual(
            TacticalObjectiveGate.ReconnaissanceContact,
            TacticalDecisionDoctrine.ClassifyObjective(highConfidenceScreen, ownStrength: 5000f, reserveFraction: 0.30f),
            "high confidence screen still lacks formed line");
        AssertEqual(
            TacticalObjectiveGate.ExposedWeakPoint,
            TacticalDecisionDoctrine.ClassifyObjective(formedLine, ownStrength: 2200f, reserveFraction: 0.25f),
            "formed weak point");
        AssertEqual(
            TacticalObjectiveGate.UnreliableContact,
            TacticalDecisionDoctrine.ClassifyObjective(stale, ownStrength: 5000f, reserveFraction: 0.30f),
            "stale contact");
        AssertFalse(
            TacticalDecisionDoctrine.ShouldCommitFixAndFlank(formedLine, ownStrength: 2200f, reserveFraction: 0.05f),
            "reserve floor blocks fix and flank");
    }

    private static void TacticalDecisionDoctrineGatesSoftAbort()
    {
        var objective = new BattlefieldObjectiveEstimate("enemy-line", TacticalObjectiveType.EnemyLine, 2000f, 0.80f, true, 0.65f, 300f, 400f, 0.1f, 0.1f);

        AssertTrue(
            TacticalDecisionDoctrine.ShouldSoftAbortCommitted(objective, ownStrength: 1300f, reserveFraction: 0.30f),
            "odds collapse");
        AssertTrue(
            TacticalDecisionDoctrine.ShouldSoftAbortCommitted(objective, ownStrength: 2100f, reserveFraction: 0.03f),
            "reserve depleted with marginal odds");
        AssertFalse(
            TacticalDecisionDoctrine.ShouldSoftAbortCommitted(objective, ownStrength: 2600f, reserveFraction: 0.18f),
            "enough odds and reserve");
    }

    private static void TacticalDefensiveLineAnchorShiftsRoadCorridorForward()
    {
        var objective = new BattlefieldObjectiveEstimate(
            "stafford-road",
            TacticalObjectiveType.VictoryPoint,
            0f,
            0.82f,
            false,
            0.8f,
            1000f,
            1000f,
            0.25f,
            0.4f,
            TacticalApproachAvenueEstimate.Create(
                new TacticalMapPoint(1000f, 0f),
                new TacticalMapPoint(1000f, 1000f),
                TacticalApproachAvenueSource.ScheduledArrival,
                0.92f,
                roadAnchored: true,
                crossingAnchored: true,
                "road-between-rivers"));

        TacticalDefensiveLineAnchor anchor = TacticalDefensiveLineAnchorPlanner.Plan(
            objective,
            default(TacticalBattlefrontSnapshot),
            nodeCount: 8);

        AssertTrue(anchor.HasAnchor, "anchor");
        AssertTrue(anchor.Center.HasValue, "center");
        AssertTrue(anchor.Center.Z < objective.Z - 250f, "line should shift toward likely enemy road approach");
        AssertTrue(anchor.FrontageWidth <= 575f, "crossing corridor should narrow frontage");
        AssertTrue(anchor.DefensiveArtilleryDepth > 0f, "artillery depth");
        AssertContains(anchor.Reason, "approach-intercept", "intercept reason");
        AssertContains(anchor.Reason, "crossing", "crossing reason");
    }

    private static void TacticalScoutingAvenueSendsScreenForwardAndReserveRear()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("screen", CommandEchelonKind.BrigadeLike, CommandNodeRole.ScreeningForce, CommandTaskType.FormUp, 980f, 1300f, 180f),
            CommandNodeOperationalState.Create("reserve", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.FormUp, 1020f, 1400f, 180f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Scouting, "ridge-a", 600f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate(
                "ridge-a",
                TacticalObjectiveType.Ridge,
                0f,
                0.8f,
                false,
                0.8f,
                1000f,
                1000f,
                0.4f,
                0.2f,
                TacticalApproachAvenueEstimate.Create(
                    new TacticalMapPoint(1000f, 0f),
                    new TacticalMapPoint(1000f, 1000f),
                    TacticalApproachAvenueSource.EntryPoint,
                    0.85f,
                    roadAnchored: true,
                    crossingAnchored: false,
                    "entrypoint-axis"))
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 1500f, nowSeconds: 100f);

        AssertEqual(CommandTaskType.Screen, orders[0].Task, "screen task");
        AssertTrue(orders[0].PrimaryTarget.HasValue, "screen target");
        AssertTrue(orders[0].PrimaryTarget.Z < picture.Objectives[0].Z, "screen should delay forward on predicted route");
        AssertEqual(CommandTaskType.ReserveWait, orders[1].Task, "reserve task");
        AssertTrue(orders[1].PrimaryTarget.HasValue, "reserve target");
        AssertTrue(orders[1].PrimaryTarget.Z > picture.Objectives[0].Z, "reserve should wait behind objective");
    }

    private static void TacticalMassingCycleWaitsForSupportBeforeCommit()
    {
        TacticalMassingDecision decision = TacticalMassingCycle.Evaluate(new TacticalMassingInput(
            operationPhase: TacticalOperationPhase.Committed,
            mainEffortCount: 1,
            supportCount: 0,
            reserveCount: 1,
            artilleryReady: false,
            objectiveExposed: true,
            objectiveConfidence01: 0.85f,
            ownStrength: 1500f,
            enemyStrength: 1000f,
            reserveFraction: 0.20f));

        AssertEqual(TacticalMassingPhase.MassSupport, decision.Phase, "phase");
        AssertFalse(decision.MayCommitAssault, "commit gate");
        AssertContains(decision.Reason, "support", "reason");
    }

    private static void TacticalMassingCycleBlocksCommitWhenEnduranceNeedsRelief()
    {
        TacticalMassingDecision decision = TacticalMassingCycle.Evaluate(new TacticalMassingInput(
            operationPhase: TacticalOperationPhase.Committed,
            mainEffortCount: 1,
            supportCount: 1,
            reserveCount: 1,
            artilleryReady: true,
            objectiveExposed: true,
            objectiveConfidence01: 0.9f,
            ownStrength: 2200f,
            enemyStrength: 1000f,
            reserveFraction: 0.22f,
            endurance: new TacticalEnduranceDecision(
                canAssault: false,
                canHold: true,
                canFallback: true,
                needsRelief: true,
                reason: "relief-needed")));

        AssertEqual(TacticalMassingPhase.MassSupport, decision.Phase, "phase");
        AssertFalse(decision.MayCommitAssault, "commit gate");
        AssertContains(decision.Reason, "endurance", "reason");
    }

    private static void TacticalFallbackLadderSeparatesFallbackFromRetreat()
    {
        TacticalFallbackDecision fallback = TacticalFallbackLadder.Decide(new TacticalFallbackInput(
            odds: 0.8f,
            morale01: 0.45f,
            fatigue01: 0.55f,
            flankThreat01: 0.65f,
            rearThreat: false,
            hasFallbackTarget: true,
            reserveReliefAvailable: true,
            wlGateAllows: true));
        TacticalFallbackDecision retreat = TacticalFallbackLadder.Decide(new TacticalFallbackInput(
            odds: 0.35f,
            morale01: 0.15f,
            fatigue01: 0.9f,
            flankThreat01: 0.9f,
            rearThreat: true,
            hasFallbackTarget: true,
            reserveReliefAvailable: false,
            wlGateAllows: true));

        AssertEqual(TacticalFallbackStep.FallbackByBounds, fallback.Step, "fallback step");
        AssertEqual(TacticalFallbackStep.FullRetreat, retreat.Step, "retreat step");
        AssertTrue(fallback.RequiresOrderedFallback, "fallback order");
        AssertTrue(retreat.PreservesForce, "retreat preserves force");
    }

    private static void TacticalArtilleryMissionPlannerGatesSupportByAmmoRangeAndDanger()
    {
        TacticalArtilleryMissionDecision support = TacticalArtilleryMissionPlanner.Decide(new TacticalArtilleryMissionInput(
            requestedSupport: true,
            enemyArtilleryVisible: false,
            ammoRatio01: 0.7f,
            targetDistance: 700f,
            optimalRange: 800f,
            maxRange: 1200f,
            friendlyDangerClose: false,
            threatenedByCloseEnemy: false,
            canDisplace: true));
        TacticalArtilleryMissionDecision danger = TacticalArtilleryMissionPlanner.Decide(new TacticalArtilleryMissionInput(
            requestedSupport: true,
            enemyArtilleryVisible: false,
            ammoRatio01: 0.7f,
            targetDistance: 700f,
            optimalRange: 800f,
            maxRange: 1200f,
            friendlyDangerClose: true,
            threatenedByCloseEnemy: false,
            canDisplace: true));

        AssertEqual(TacticalArtilleryMission.SupportMainEffort, support.Mission, "support mission");
        AssertEqual(TacticalArtilleryMission.HoldFireDangerClose, danger.Mission, "danger mission");
        AssertFalse(danger.AllowsFire, "danger close fire gate");
    }

    private static void TacticalArtilleryCommandAssignsWeakPointAmmoAndReposition()
    {
        TacticalArtilleryMissionDecision support = TacticalArtilleryMissionPlanner.Decide(new TacticalArtilleryMissionInput(
            requestedSupport: true,
            enemyArtilleryVisible: false,
            ammoRatio01: 0.7f,
            targetDistance: 900f,
            optimalRange: 750f,
            maxRange: 1300f,
            friendlyDangerClose: false,
            threatenedByCloseEnemy: false,
            canDisplace: true,
            fieldOfFireClear: true,
            hasWeakPointTarget: true,
            weakPointX: 400f,
            weakPointZ: 900f,
            hasSafeRepositionTarget: false,
            safeRepositionX: 0f,
            safeRepositionZ: 0f));
        TacticalArtilleryMissionDecision displace = TacticalArtilleryMissionPlanner.Decide(new TacticalArtilleryMissionInput(
            requestedSupport: true,
            enemyArtilleryVisible: false,
            ammoRatio01: 0.7f,
            targetDistance: 900f,
            optimalRange: 750f,
            maxRange: 1300f,
            friendlyDangerClose: false,
            threatenedByCloseEnemy: false,
            canDisplace: true,
            fieldOfFireClear: false,
            hasWeakPointTarget: true,
            weakPointX: 400f,
            weakPointZ: 900f,
            hasSafeRepositionTarget: true,
            safeRepositionX: 100f,
            safeRepositionZ: 250f));

        AssertEqual(TacticalArtilleryMission.SupportMainEffort, support.Mission, "support mission");
        AssertEqual(TacticalArtilleryAmmoMission.Shell, support.AmmoMission, "support ammo");
        AssertTrue(support.AssignmentTarget.HasValue, "support target");
        AssertNear(400f, support.AssignmentTarget.X, 0.01f, "weak point x");
        AssertEqual(TacticalArtilleryMission.Displace, displace.Mission, "reposition mission");
        AssertTrue(displace.ShouldReposition, "should reposition");
        AssertTrue(displace.RepositionTarget.HasValue, "reposition target");
        AssertEqual(TacticalArtilleryAmmoMission.Preserve, displace.AmmoMission, "displace ammo");
    }

    private static void TacticalContactNoSightingIsNone()
    {
        var contact = TacticalContactLedger.Classify(new TacticalContactInput(
            visibleEnemyStrength: 0f,
            recentEnemyStrength: 0f,
            inferredEnemyStrength: 0f,
            secondsSinceLastConfirmed: 9999f,
            receivedFire: false,
            inFog: true));

        AssertEqual(TacticalContactState.None, contact.State, "state");
        AssertTrue(contact.Confidence < 0.2f, "confidence should be low without contact");
    }

    private static void TacticalContactStaleSightingAgesDown()
    {
        var recent = TacticalContactLedger.Classify(new TacticalContactInput(
            visibleEnemyStrength: 1000f,
            recentEnemyStrength: 1000f,
            inferredEnemyStrength: 0f,
            secondsSinceLastConfirmed: 5f,
            receivedFire: false,
            inFog: false));
        var stale = TacticalContactLedger.Classify(new TacticalContactInput(
            visibleEnemyStrength: 0f,
            recentEnemyStrength: 1000f,
            inferredEnemyStrength: 0f,
            secondsSinceLastConfirmed: 900f,
            receivedFire: false,
            inFog: true));

        AssertEqual(TacticalContactState.Confirmed, recent.State, "recent state");
        AssertTrue(stale.State == TacticalContactState.Inferred || stale.State == TacticalContactState.None, "stale state");
        AssertTrue(stale.Confidence < recent.Confidence, "stale confidence should decay");
    }

    private static void TacticalOddsNoContactAvoidsAssault()
    {
        var output = TacticalOddsDoctrine.Evaluate(new TacticalOddsInput(
            ownStrength: 12000f,
            enemyStrengthConfirmed: 0f,
            enemyStrengthRecent: 0f,
            enemyStrengthInferred: 0f,
            reinforcementStrength24h: 0f,
            terrainAdvantage: 0f,
            contact: new TacticalContactAssessment(TacticalContactState.None, 0f, 0f, "none"),
            sectors: Array.Empty<TacticalSectorAssessment>()));

        AssertEqual(TacticalInferiorForcePosture.ProbeOrHold, output.InferiorForcePosture, "posture");
        AssertEqual(0f, output.CurrentGlobalOdds, "no contact current odds");
        AssertEqual(0f, output.ProjectedGlobalOdds, "no contact projected odds");
        AssertTrue(!output.AllowAssault, "no contact should not permit assault");
    }

    private static void TacticalOddsGlobalSuperioritySelectsOneDecisiveSector()
    {
        var sectors = new[]
        {
            new TacticalSectorAssessment(0, TacticalSectorSource.AngleSlice, 3000f, 2500f, 0.7f, false, false, TacticalSectorMission.Hold),
            new TacticalSectorAssessment(1, TacticalSectorSource.AngleSlice, 5000f, 1800f, 0.9f, false, false, TacticalSectorMission.Probe),
            new TacticalSectorAssessment(2, TacticalSectorSource.AngleSlice, 4000f, 3200f, 0.8f, false, false, TacticalSectorMission.Hold)
        };

        var output = TacticalOddsDoctrine.Evaluate(new TacticalOddsInput(
            ownStrength: 12000f,
            enemyStrengthConfirmed: 7500f,
            enemyStrengthRecent: 7500f,
            enemyStrengthInferred: 7500f,
            reinforcementStrength24h: 0f,
            terrainAdvantage: 0f,
            contact: new TacticalContactAssessment(TacticalContactState.Confirmed, 0.9f, 7500f, "visible"),
            sectors: sectors));

        AssertEqual(1, output.DecisiveSectorId, "decisive sector");
        AssertTrue(output.EconomyOfForceSectorIds.Length >= 1, "other sectors should remain economy/fix candidates");
    }

    private static void TacticalOddsInferiorNoReliefPreservesForce()
    {
        var output = TacticalOddsDoctrine.Evaluate(new TacticalOddsInput(
            ownStrength: 4000f,
            enemyStrengthConfirmed: 12000f,
            enemyStrengthRecent: 12000f,
            enemyStrengthInferred: 12000f,
            reinforcementStrength24h: 0f,
            terrainAdvantage: 0f,
            contact: new TacticalContactAssessment(TacticalContactState.Confirmed, 0.9f, 12000f, "visible"),
            sectors: Array.Empty<TacticalSectorAssessment>()));

        AssertEqual(TacticalInferiorForcePosture.PreserveOrRetreat, output.InferiorForcePosture, "posture");
    }

    private static void TacticalOddsInferiorWithReliefDelays()
    {
        var output = TacticalOddsDoctrine.Evaluate(new TacticalOddsInput(
            ownStrength: 4000f,
            enemyStrengthConfirmed: 12000f,
            enemyStrengthRecent: 12000f,
            enemyStrengthInferred: 12000f,
            reinforcementStrength24h: 5000f,
            terrainAdvantage: 0.8f,
            contact: new TacticalContactAssessment(TacticalContactState.Confirmed, 0.9f, 12000f, "visible"),
            sectors: Array.Empty<TacticalSectorAssessment>()));

        AssertEqual(TacticalInferiorForcePosture.DelayOnStrongGround, output.InferiorForcePosture, "posture");
    }

    private static void TacticalGroupVisibleLineContactDrivesWeakPoint()
    {
        var sector = TacticalGroupSectorEstimator.BuildSector(new TacticalGroupContactInput(
            sectorId: 4,
            ownStrength: 17651f,
            enemiesInRangeStrength: 0f,
            angleEnemyStrength: 0f,
            closestEnemyStrength: 914f,
            closestEnemyUnitType: TacticalUnitType.Infantry,
            closestEnemyName: "Hampton's Legion",
            closestEnemyRouted: false,
            closestEnemyPermanentlyDetached: false,
            flankRisk: false,
            strongPoint: false));

        AssertEqual(TacticalSectorSource.VisibleLineContact, sector.Source, "source");
        AssertEqual(TacticalSectorMission.AttackWeakPoint, sector.Mission, "visible line contact should drive weak point");
        AssertTrue(sector.Confidence >= 0.75f, "visible line contact should raise confidence");
        AssertNear(914f, sector.EnemyStrength, 0.01f, "closest visible line strength feeds sector enemy");
    }

    private static void TacticalGroupNamedDetachmentLineContactDrivesWeakPoint()
    {
        var sector = TacticalGroupSectorEstimator.BuildSector(new TacticalGroupContactInput(
            sectorId: 4,
            ownStrength: 17651f,
            enemiesInRangeStrength: 0f,
            angleEnemyStrength: 0f,
            closestEnemyStrength: 914f,
            closestEnemyUnitType: TacticalUnitType.Infantry,
            closestEnemyName: "Whiting's Detachment",
            closestEnemyRouted: false,
            closestEnemyPermanentlyDetached: false,
            flankRisk: false,
            strongPoint: false));

        AssertEqual(TacticalSectorSource.VisibleLineContact, sector.Source, "source");
        AssertEqual(TacticalSectorMission.AttackWeakPoint, sector.Mission, "named detachment with formed infantry strength should drive weak point");
        AssertTrue(sector.Confidence >= 0.75f, "named detachment line contact should raise confidence");
        AssertNear(914f, sector.EnemyStrength, 0.01f, "detachment line strength feeds sector enemy");
    }

    private static void TacticalGroupBattleCommandContactDrivesWeakPoint()
    {
        var sector = TacticalGroupSectorEstimator.BuildSector(new TacticalGroupContactInput(
            sectorId: 4,
            ownStrength: 17651f,
            enemiesInRangeStrength: 0f,
            angleEnemyStrength: 0f,
            closestEnemyStrength: 914f,
            closestEnemyUnitType: TacticalUnitType.BattleGroupBrigade,
            closestEnemyName: "Whiting's Detachment",
            closestEnemyRouted: false,
            closestEnemyPermanentlyDetached: false,
            flankRisk: false,
            strongPoint: false));

        AssertEqual(TacticalSectorSource.VisibleLineContact, sector.Source, "source");
        AssertEqual(TacticalSectorMission.AttackWeakPoint, sector.Mission, "battle command contact should drive weak point");
        AssertTrue(sector.Confidence >= 0.75f, "battle command contact should raise confidence");
        AssertNear(914f, sector.EnemyStrength, 0.01f, "battle command line strength feeds sector enemy");
    }

    private static void TacticalGroupScreenContactDoesNotDriveWeakPoint()
    {
        var sector = TacticalGroupSectorEstimator.BuildSector(new TacticalGroupContactInput(
            sectorId: 4,
            ownStrength: 17651f,
            enemiesInRangeStrength: 0f,
            angleEnemyStrength: 0f,
            closestEnemyStrength: 914f,
            closestEnemyUnitType: TacticalUnitType.Skirmisher,
            closestEnemyName: "Berry's Detachment",
            closestEnemyRouted: false,
            closestEnemyPermanentlyDetached: true,
            flankRisk: false,
            strongPoint: false));

        AssertTrue(sector.Mission != TacticalSectorMission.AttackWeakPoint, "screen contact must not commit formed regiments");
        AssertEqual(0f, sector.EnemyStrength, "screen-only contact should not become line strength");
        // Pre-contact floor was lifted from 0.45 to 0.55 (SoW-aligned). Confidence
        // must still stay below the weak-point threshold (0.75) and the upper-band
        // line-contact value (0.65 with enemy). 0.60 is the right upper bound here.
        AssertTrue(sector.Confidence <= 0.60f, "screen-only contact should not become high confidence");
    }

    private static TacticalOddsAssessment Odds(
        float current,
        int decisive = -1,
        TacticalInferiorForcePosture posture = TacticalInferiorForcePosture.None,
        float confidence = 0.9f,
        bool assault = false)
    {
        return new TacticalOddsAssessment(
            current,
            current,
            decisive,
            Array.Empty<int>(),
            posture,
            confidence,
            assault);
    }

    private static void TacticalMacroDynamicIsNotAttack()
    {
        var decision = TacticalDoctrineScorer.DecideMacro(new TacticalMacroDecisionInput(
            vanillaMacro: -1,
            debugOverrideActive: false,
            saveRestoreMacroActive: false,
            vanillaRetreatActive: false,
            commanderAggression01: 0.5f,
            odds: Odds(1.2f, confidence: 0.1f)));

        AssertEqual(TacticalDoctrineDecisionKind.Apply, decision.Kind, "kind");
        AssertTrue(decision.MacroAi == -1 || decision.MacroAi == 2, "no-contact dynamic should stay dynamic/defend");
    }

    private static void TacticalMacroDebugOverrideSkips()
    {
        var decision = TacticalDoctrineScorer.DecideMacro(new TacticalMacroDecisionInput(
            vanillaMacro: 1,
            debugOverrideActive: true,
            saveRestoreMacroActive: false,
            vanillaRetreatActive: false,
            commanderAggression01: 1f,
            odds: Odds(3f, decisive: 1, confidence: 1f, assault: true)));

        AssertEqual(TacticalDoctrineDecisionKind.Skip, decision.Kind, "debug override must skip");
    }

    private static void TacticalMacroInferiorNoReliefRetreats()
    {
        var decision = TacticalDoctrineScorer.DecideMacro(new TacticalMacroDecisionInput(
            vanillaMacro: 2,
            debugOverrideActive: false,
            saveRestoreMacroActive: false,
            vanillaRetreatActive: false,
            commanderAggression01: 0.5f,
            odds: Odds(0.33f, posture: TacticalInferiorForcePosture.PreserveOrRetreat, confidence: 0.9f)));

        AssertEqual(3, decision.MacroAi, "macro retreat pressure");
    }

    private static void TacticalGroupDecisiveSectorAttacksWithoutCharge()
    {
        var sector = new TacticalSectorAssessment(4, TacticalSectorSource.ObjectiveChain, 5000f, 2000f, 0.9f, strongPoint: false, flankRisk: false, TacticalSectorMission.AttackWeakPoint);
        var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
            vanillaStance: 2,
            macroAi: 1,
            sector: sector,
            orderFrictionAllowsChange: true,
            wlAllowsControl: true));

        AssertEqual(TacticalDoctrineDecisionKind.Apply, decision.Kind, "kind");
        AssertEqual(3, decision.GroupStance, "attack stance, not charge");
    }

    private static void TacticalGroupDefensiveVisibleWeakPointCounterattacks()
    {
        var sector = new TacticalSectorAssessment(4, TacticalSectorSource.VisibleLineContact, 17651f, 914f, 0.85f, strongPoint: false, flankRisk: false, TacticalSectorMission.AttackWeakPoint);
        var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
            vanillaStance: 2,
            macroAi: 2,
            sector: sector,
            orderFrictionAllowsChange: true,
            wlAllowsControl: true));

        AssertEqual(TacticalDoctrineDecisionKind.Apply, decision.Kind, "kind");
        AssertEqual(3, decision.GroupStance, "defensive visible weak point should counterattack");
        AssertEqual("defensive-counterstroke", decision.Reason, "reason");
    }

    private static void TacticalGroupWeakPointUnderDefendHolds()
    {
        var sector = new TacticalSectorAssessment(4, TacticalSectorSource.ObjectiveChain, 5000f, 2000f, 0.9f, strongPoint: false, flankRisk: false, TacticalSectorMission.AttackWeakPoint);
        var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
            vanillaStance: 2,
            macroAi: 2,
            sector: sector,
            orderFrictionAllowsChange: true,
            wlAllowsControl: true));

        AssertEqual(TacticalDoctrineDecisionKind.Apply, decision.Kind, "kind");
        AssertEqual(2, decision.GroupStance, "defensive weak point should hold");
        AssertEqual("defend-hold", decision.Reason, "reason");
    }

    private static void TacticalGroupFixUnderDefendHolds()
    {
        var sector = new TacticalSectorAssessment(4, TacticalSectorSource.ObjectiveChain, 5000f, 2000f, 0.9f, strongPoint: false, flankRisk: false, TacticalSectorMission.Fix);
        var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
            vanillaStance: 2,
            macroAi: 2,
            sector: sector,
            orderFrictionAllowsChange: true,
            wlAllowsControl: true));

        AssertEqual(TacticalDoctrineDecisionKind.Apply, decision.Kind, "kind");
        AssertEqual(2, decision.GroupStance, "defensive fix should hold");
        AssertEqual("defend-hold", decision.Reason, "reason");
    }

    private static void TacticalGroupLocalStanceWriterOnlyControlsBrigades()
    {
        AssertEqual(true, TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(TacticalUnitType.BattleGroupBrigade), "brigade");
        AssertFalse(TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(TacticalUnitType.BattleGroupDivision), "division");
        AssertFalse(TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(TacticalUnitType.BattleGroupArmy), "army");
    }

    private static void TacticalGroupRetreatMacroKeepsVanilla()
    {
        var sector = new TacticalSectorAssessment(4, TacticalSectorSource.ObjectiveChain, 5000f, 500f, 1.0f, strongPoint: false, flankRisk: false, TacticalSectorMission.AttackWeakPoint);
        var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
            vanillaStance: 1,
            macroAi: 3,
            sector: sector,
            orderFrictionAllowsChange: true,
            wlAllowsControl: true));

        AssertEqual(TacticalDoctrineDecisionKind.Skip, decision.Kind, "kind");
        AssertEqual(1, decision.GroupStance, "retreat stance stays vanilla-owned");
    }

    private static void TacticalGroupExplicitProbeBypassesLowConfidenceSkip()
    {
        var sector = new TacticalSectorAssessment(4, TacticalSectorSource.AngleSlice, 5000f, 0f, 0.45f, strongPoint: false, flankRisk: false, TacticalSectorMission.Probe);
        var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
            vanillaStance: 2,
            macroAi: 2,
            sector: sector,
            orderFrictionAllowsChange: true,
            wlAllowsControl: true));

        AssertEqual(TacticalDoctrineDecisionKind.Apply, decision.Kind, "kind");
        AssertEqual(1, decision.GroupStance, "probe stance");
    }

    private static void TacticalGroupLowConfidenceKeepsVanilla()
    {
        var sector = new TacticalSectorAssessment(4, TacticalSectorSource.AngleSlice, 5000f, 2000f, 0.2f, strongPoint: false, flankRisk: false, TacticalSectorMission.AttackWeakPoint);
        var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
            vanillaStance: 2,
            macroAi: 1,
            sector: sector,
            orderFrictionAllowsChange: true,
            wlAllowsControl: true));

        AssertEqual(TacticalDoctrineDecisionKind.Skip, decision.Kind, "low confidence");
    }

    private static void TacticalGroupSectorEstimatorPreContactReachesActFloor()
    {
        // SoW-aligned floor: no-enemy, no-line-contact now returns 0.55 (was 0.45).
        // This lets DecideGroupStance apply pre-contact-hold instead of skipping.
        var sector = TacticalGroupSectorEstimator.BuildSector(new TacticalGroupContactInput(
            sectorId: 0,
            ownStrength: 1000f,
            enemiesInRangeStrength: 0f,
            angleEnemyStrength: 0f,
            closestEnemyStrength: 0f,
            closestEnemyUnitType: -1,
            closestEnemyName: "",
            closestEnemyRouted: false,
            closestEnemyPermanentlyDetached: false,
            flankRisk: false,
            strongPoint: false));
        AssertTrue(sector.Confidence >= 0.55f, "pre-contact confidence must reach act floor");
    }

    private static void TacticalGroupDoctrineIssuesPreContactHoldWhenNoEnemyVisible()
    {
        // Mid-band confidence (between low-confidence floor 0.40 and act floor 0.55)
        // returns Apply(vanillaStance, "pre-contact-preserve-vanilla") instead of
        // Skip(low-confidence). BattleGroupStancePatch:109 short-circuits when
        // decision.GroupStance == vanillaOrdered, so no actual stance write happens —
        // movement orders flow through #61 / TacticalNavMeshPlanner independently.
        var sector = new TacticalSectorAssessment(
            sectorId: 0,
            source: TacticalSectorSource.AngleSlice,
            ownStrength: 1000f,
            enemyStrength: 0f,
            confidence: 0.50f,
            strongPoint: false,
            flankRisk: false,
            mission: TacticalSectorMission.Hold);
        var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
            vanillaStance: 3,
            macroAi: 1,
            sector: sector,
            orderFrictionAllowsChange: true,
            wlAllowsControl: true));

        AssertEqual(TacticalDoctrineDecisionKind.Apply, decision.Kind, "pre-contact emits Apply");
        AssertEqual(3, decision.GroupStance, "preserves vanilla stance (3 in, 3 out)");
        AssertEqual("pre-contact-preserve-vanilla", decision.Reason, "reason");
    }

    private static void TacticalGroupDoctrinePreContactPreservesAnyVanillaStance()
    {
        // Regression: preserves whatever vanilla wanted (1, 2, or 3) for the
        // mid-band confidence range. The Apply-with-vanillaStance pattern means
        // #45 BattleGroupStancePatch short-circuits the write since the new value
        // equals the existing value.
        var sector = new TacticalSectorAssessment(
            sectorId: 0, source: TacticalSectorSource.AngleSlice,
            ownStrength: 1000f, enemyStrength: 0f, confidence: 0.50f,
            strongPoint: false, flankRisk: false,
            mission: TacticalSectorMission.Hold);
        foreach (int vanilla in new[] { 1, 2, 3 })
        {
            var d = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
                vanillaStance: vanilla, macroAi: 1, sector: sector,
                orderFrictionAllowsChange: true, wlAllowsControl: true));
            AssertEqual(TacticalDoctrineDecisionKind.Apply, d.Kind, "kind for vanilla=" + vanilla);
            AssertEqual(vanilla, d.GroupStance, "preserves vanilla stance for input " + vanilla);
            AssertEqual("pre-contact-preserve-vanilla", d.Reason, "reason for vanilla=" + vanilla);
        }
    }

    private static void TacticalGroupDoctrineStillRejectsBelowLowConfidenceFloor()
    {
        // Strict below-floor still skips (regression coverage for the 0.40 boundary).
        var sector = new TacticalSectorAssessment(
            sectorId: 0,
            source: TacticalSectorSource.AngleSlice,
            ownStrength: 1000f,
            enemyStrength: 0f,
            confidence: 0.30f,
            strongPoint: false,
            flankRisk: false,
            mission: TacticalSectorMission.Hold);
        var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
            vanillaStance: 3,
            macroAi: 1,
            sector: sector,
            orderFrictionAllowsChange: true,
            wlAllowsControl: true));
        AssertEqual(TacticalDoctrineDecisionKind.Skip, decision.Kind, "kind");
        AssertEqual("low-confidence", decision.Reason, "reason");
    }

    private static void TacticalGroupDoctrineLineContactStillReachesAttackWeakPoint()
    {
        // Regression: line-contact behavior unchanged (high confidence still drives attack).
        var sector = new TacticalSectorAssessment(
            sectorId: 0,
            source: TacticalSectorSource.VisibleLineContact,
            ownStrength: 1000f,
            enemyStrength: 400f,
            confidence: 0.85f,
            strongPoint: false,
            flankRisk: false,
            mission: TacticalSectorMission.AttackWeakPoint);
        var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
            vanillaStance: 3,
            macroAi: 1,
            sector: sector,
            orderFrictionAllowsChange: true,
            wlAllowsControl: true));

        AssertEqual(TacticalDoctrineDecisionKind.Apply, decision.Kind, "kind");
        AssertEqual(3, decision.GroupStance, "attack-weak-point stance");
        AssertEqual("attack-weak-point", decision.Reason, "reason");
    }

    private static void TacticalGroupWlPlayerSubordinateSkips()
    {
        var sector = new TacticalSectorAssessment(4, TacticalSectorSource.ObjectiveChain, 5000f, 2000f, 0.9f, strongPoint: false, flankRisk: false, TacticalSectorMission.AttackWeakPoint);
        var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
            vanillaStance: 2,
            macroAi: 1,
            sector: sector,
            orderFrictionAllowsChange: true,
            wlAllowsControl: false));

        AssertEqual(TacticalDoctrineDecisionKind.Skip, decision.Kind, "wl ownership");
    }

    private static void TacticalDiagnosticsDetectCampaignCurrentOrderReplacementRisk()
    {
        var oldOrder = new TacticalCurrentOrderSignature(7, 11, 100f, 200f, 45f, "Hill");
        var nearOrder = new TacticalCurrentOrderSignature(7, 11, 104f, 202f, 47f, "Hill");
        var materialOrder = new TacticalCurrentOrderSignature(7, 12, 104f, 202f, 47f, "Hill");

        var duplicate = TacticalBattlefieldBugDiagnostics.ClassifyCurrentOrderReplacement(
            calledFromCampaign: true,
            oldOrder: oldOrder,
            newOrder: nearOrder,
            nearDistance: 10f,
            nearRotationDegrees: 5f);
        var battleCall = TacticalBattlefieldBugDiagnostics.ClassifyCurrentOrderReplacement(
            calledFromCampaign: false,
            oldOrder: oldOrder,
            newOrder: nearOrder,
            nearDistance: 10f,
            nearRotationDegrees: 5f);
        var material = TacticalBattlefieldBugDiagnostics.ClassifyCurrentOrderReplacement(
            calledFromCampaign: true,
            oldOrder: oldOrder,
            newOrder: materialOrder,
            nearDistance: 10f,
            nearRotationDegrees: 5f);

        AssertTrue(duplicate.IsRisk, "campaign near replacement should be risky");
        AssertEqual(TacticalBattlefieldBugObservationKind.CurrentOrderReplacement, duplicate.Kind, "kind");
        AssertEqual("campaign-duplicate-near", duplicate.Reason, "reason");
        AssertContains(duplicate.Summary, "[TacticalCurrentOrder]", "summary prefix");
        AssertTrue(!battleCall.IsRisk, "battle calls should rely on vanilla duplicate guard");
        AssertTrue(material.IsRisk, "campaign material replacement should still be visible");
        AssertEqual("campaign-replacement-material-change", material.Reason, "material reason");
    }

    private static void TacticalDiagnosticsDetectDelayedWaypointDrift()
    {
        var drift = TacticalBattlefieldBugDiagnostics.ClassifyDelayedWaypointDrift(
            orderDelayEnabled: true,
            activeMoveOrder: true,
            queueAdded: false,
            pathCountBefore: 1,
            pathCountAfter: 2,
            xBefore: 10f,
            zBefore: 20f,
            xAfter: 15f,
            zAfter: 25f);
        var queued = TacticalBattlefieldBugDiagnostics.ClassifyDelayedWaypointDrift(
            orderDelayEnabled: true,
            activeMoveOrder: true,
            queueAdded: true,
            pathCountBefore: 1,
            pathCountAfter: 2,
            xBefore: 10f,
            zBefore: 20f,
            xAfter: 15f,
            zAfter: 25f);

        AssertTrue(drift.IsRisk, "path mutation without queue insert should be risky");
        AssertEqual("path-mutated-without-queue", drift.Reason, "reason");
        AssertContains(drift.Signature, "paths=1->2", "path counts");
        AssertTrue(!queued.IsRisk, "queued waypoint changes should not be drift risk");
    }

    private static void TacticalDiagnosticsDetectSecondaryCourierQueueMismatchRisk()
    {
        var mismatch = TacticalBattlefieldBugDiagnostics.ClassifyCourierQueueIndex(
            secondaryCourier: true,
            orderQueueCount: 3,
            activeQueueIndex: 0,
            appendQueueIndex: 2);
        var sameQueue = TacticalBattlefieldBugDiagnostics.ClassifyCourierQueueIndex(
            secondaryCourier: true,
            orderQueueCount: 3,
            activeQueueIndex: 2,
            appendQueueIndex: 2);

        AssertTrue(mismatch.IsRisk, "secondary courier appended to a different queue should be risky");
        AssertEqual("secondary-courier-appended-to-latest", mismatch.Reason, "reason");
        AssertContains(mismatch.Summary, "[TacticalCourierQueue]", "summary prefix");
        AssertTrue(!sameQueue.IsRisk, "secondary courier on active queue should not be risky");
    }

    private static void TacticalDiagnosticsDetectObjectiveChainPlayerSubordinateRisk()
    {
        var risky = TacticalBattlefieldBugDiagnostics.ClassifyObjectiveChainMovement(
            objectiveChainMove: true,
            centerGroupUnderPlayerCommander: false,
            attachedPlayerSubordinate: true,
            attachedUnitCount: 4);
        var aiOnly = TacticalBattlefieldBugDiagnostics.ClassifyObjectiveChainMovement(
            objectiveChainMove: true,
            centerGroupUnderPlayerCommander: false,
            attachedPlayerSubordinate: false,
            attachedUnitCount: 4);
        var center = TacticalBattlefieldBugDiagnostics.ClassifyObjectiveChainMovement(
            objectiveChainMove: true,
            centerGroupUnderPlayerCommander: true,
            attachedPlayerSubordinate: false,
            attachedUnitCount: 4);

        AssertTrue(risky.IsRisk, "objective-chain movement with player-subordinate attachments should be risky");
        AssertEqual("objective-chain-player-subordinate-attached", risky.Reason, "reason");
        AssertContains(risky.Signature, "attached=4", "attached count");
        AssertTrue(!aiOnly.IsRisk, "AI-only objective-chain movement should remain observation-only");
        AssertTrue(center.IsRisk, "objective-chain movement with player center group should be risky");
        AssertEqual("objective-chain-player-center-group", center.Reason, "center reason");
    }

    private static void TacticalDiagnosticsDetectObjectiveChainMovementMutationProof()
    {
        var noMutation = TacticalBattlefieldBugDiagnostics.ClassifyObjectiveChainMutation(
            exposedPlayerSubordinateChain: true,
            centerMutated: false,
            attachedPlayerSubordinateMutated: false,
            changedUnitCount: 0);
        var centerMutation = TacticalBattlefieldBugDiagnostics.ClassifyObjectiveChainMutation(
            exposedPlayerSubordinateChain: true,
            centerMutated: true,
            attachedPlayerSubordinateMutated: false,
            changedUnitCount: 1);
        var attachedMutation = TacticalBattlefieldBugDiagnostics.ClassifyObjectiveChainMutation(
            exposedPlayerSubordinateChain: true,
            centerMutated: false,
            attachedPlayerSubordinateMutated: true,
            changedUnitCount: 2);
        var aiOnlyMutation = TacticalBattlefieldBugDiagnostics.ClassifyObjectiveChainMutation(
            exposedPlayerSubordinateChain: false,
            centerMutated: true,
            attachedPlayerSubordinateMutated: true,
            changedUnitCount: 3);

        AssertTrue(!noMutation.IsRisk, "exposure without path or position mutation should not justify behavior patch");
        AssertEqual("objective-chain-no-mutation", noMutation.Reason, "no mutation reason");
        AssertTrue(centerMutation.IsRisk, "center mutation in an exposed chain proves behavior impact");
        AssertEqual("objective-chain-center-mutated", centerMutation.Reason, "center mutation reason");
        AssertTrue(attachedMutation.IsRisk, "attached player-subordinate mutation proves behavior impact");
        AssertEqual("objective-chain-player-subordinate-mutated", attachedMutation.Reason, "attached mutation reason");
        AssertTrue(!aiOnlyMutation.IsRisk, "AI-only chains should not count as player-subordinate bug proof");
        AssertContains(attachedMutation.Signature, "changed=2", "changed count");
    }

    private static void TacticalDiagnosticsDetectReserveDirectPathDelayBypass()
    {
        var bypass = TacticalBattlefieldBugDiagnostics.ClassifyReserveDirectPathBypass(
            reserveSupportMove: true,
            orderDelayEnabled: true,
            directPathIssued: true,
            queuedOrderIssued: false,
            reserveCandidateCount: 2);
        var queued = TacticalBattlefieldBugDiagnostics.ClassifyReserveDirectPathBypass(
            reserveSupportMove: true,
            orderDelayEnabled: true,
            directPathIssued: true,
            queuedOrderIssued: true,
            reserveCandidateCount: 2);

        AssertTrue(bypass.IsRisk, "reserve direct path should be risky when order delay is bypassed");
        AssertEqual("reserve-direct-path-bypasses-delay", bypass.Reason, "reason");
        AssertContains(bypass.Summary, "[TacticalReserveMove]", "summary prefix");
        AssertTrue(!queued.IsRisk, "queued reserve movement should not be a bypass risk");
    }

    private static void TacticalDiagnosticsDetectPathfinderBacktrackShape()
    {
        var backtrack = TacticalBattlefieldBugDiagnostics.ClassifyPathShape(
            showMovementOptions: true,
            pathCreated: true,
            cornerCount: 4,
            directDistance: 100f,
            pathLength: 120f,
            firstSegmentDeltaDegrees: 135f,
            navStatus: "PathComplete",
            pathStatus: 2,
            orderDelayEnabled: false);
        var longRoute = TacticalBattlefieldBugDiagnostics.ClassifyPathShape(
            showMovementOptions: true,
            pathCreated: true,
            cornerCount: 5,
            directDistance: 100f,
            pathLength: 210f,
            firstSegmentDeltaDegrees: 20f,
            navStatus: "PathComplete",
            pathStatus: 2,
            orderDelayEnabled: false);
        var aiPath = TacticalBattlefieldBugDiagnostics.ClassifyPathShape(
            showMovementOptions: false,
            pathCreated: true,
            cornerCount: 4,
            directDistance: 100f,
            pathLength: 210f,
            firstSegmentDeltaDegrees: 135f,
            navStatus: "PathComplete",
            pathStatus: 2,
            orderDelayEnabled: false);

        AssertTrue(backtrack.IsRisk, "player UI path with backward first segment should be risky");
        AssertEqual(TacticalBattlefieldBugObservationKind.PathfinderBacktrackShape, backtrack.Kind, "kind");
        AssertEqual("backward-first-segment", backtrack.Reason, "backtrack reason");
        AssertContains(backtrack.Summary, "[TacticalPathShape]", "summary prefix");
        AssertTrue(longRoute.IsRisk, "excessive route ratio should be risky");
        AssertEqual("excessive-path-ratio", longRoute.Reason, "ratio reason");
        AssertTrue(!aiPath.IsRisk, "AI path shapes should not count as player right-click proof");
        AssertEqual("non-ui-path", aiPath.Reason, "ai reason");
    }

    private static void TacticalDiagnosticsSuppressOnlyTacticalNullFallbackExceptions()
    {
        AssertTrue(
            TacticalBattlefieldBugDiagnostics.ShouldSuppressFallbackRetreatException(
                "MicroAICheckForRetreats",
                new NullReferenceException("null attached unit")),
            "retreat null exception should be suppressed");
        AssertTrue(
            TacticalBattlefieldBugDiagnostics.ShouldSuppressFallbackRetreatException(
                "CheckLineFallbacks",
                new NullReferenceException("null attached unit")),
            "line fallback null exception should be suppressed");
        AssertTrue(
            TacticalBattlefieldBugDiagnostics.ShouldSuppressFallbackRetreatException(
                "CheckOutOfFireRange",
                new NullReferenceException("null closestenemyunitfar")),
            "BUG-TAC-015: CheckOutOfFireRange null exception should be suppressed");
        AssertTrue(
            !TacticalBattlefieldBugDiagnostics.ShouldSuppressFallbackRetreatException(
                "MicroAICheckForRetreats",
                new InvalidOperationException("not null")),
            "non-null exceptions must propagate");
        AssertTrue(
            !TacticalBattlefieldBugDiagnostics.ShouldSuppressFallbackRetreatException(
                "CheckAIBombardment",
                new NullReferenceException("different method")),
            "other tactical methods must propagate");
    }

    private static void TacticalDiagnosticsHandleEmptyNullAndSanitizedValues()
    {
        var empty = TacticalBattlefieldBugDiagnostics.ClassifyCurrentOrderReplacement(
            calledFromCampaign: true,
            oldOrder: TacticalCurrentOrderSignature.Empty,
            newOrder: new TacticalCurrentOrderSignature(7, 11, float.NaN, float.PositiveInfinity, -15f, null),
            nearDistance: float.NaN,
            nearRotationDegrees: float.PositiveInfinity);
        var sanitized = TacticalBattlefieldBugDiagnostics.ClassifyCourierQueueIndex(
            secondaryCourier: true,
            orderQueueCount: -3,
            activeQueueIndex: -1,
            appendQueueIndex: 2);
        var before = new TacticalCurrentOrderSignature(7, 11, 100.11f, 200.11f, 45f, "Hill Road");
        var sameBucket = new TacticalCurrentOrderSignature(7, 11, 100.14f, 200.14f, 45.02f, "Hill Road");
        var material = new TacticalCurrentOrderSignature(7, 11, 101.11f, 200.11f, 45f, "Hill Road");
        var unsafeDestination = new TacticalCurrentOrderSignature(7, 11, 100f, 200f, 45f, "Hill\nRoad\tA=B{C}|D");
        var unsafeDecision = new TacticalBugDiagnosticDecision(
            TacticalBattlefieldBugObservationKind.CurrentOrderReplacement,
            isRisk: true,
            reason: "bad reason\nwith=value",
            signature: "sig\nline\tvalue");

        AssertTrue(!empty.IsRisk, "empty current order should be safe");
        AssertEqual("missing-order", empty.Reason, "empty reason");
        AssertContains(empty.Summary, "dest=-", "null destination should be sanitized");
        AssertTrue(!sanitized.IsRisk, "invalid queue counts should be safe");
        AssertContains(sanitized.Signature, "queues=0", "negative queue count should clamp");
        AssertTrue(!empty.Summary.Contains("NaN"), "summary should not contain NaN");
        AssertTrue(!empty.Summary.Contains("Infinity"), "summary should not contain Infinity");
        AssertEqual(before.Signature, sameBucket.Signature, "same bucket signature");
        AssertTrue(before.Signature != material.Signature, "material position change should alter signature");
        AssertContains(unsafeDestination.Signature, "dest=Hill_Road_A_B_C__D", "unsafe destination should be sanitized");
        AssertTrue(!unsafeDestination.Signature.Contains("\n"), "destination signature should be one line");
        AssertTrue(!unsafeDestination.Signature.Contains("\t"), "destination signature should not contain tabs");
        AssertTrue(!unsafeDecision.Summary.Contains("\n"), "decision summary should be one line");
        AssertTrue(!unsafeDecision.Summary.Contains("\t"), "decision summary should not contain tabs");
        AssertTrue(!unsafeDecision.Reason.Contains("="), "decision reason should not contain equals");
    }

    private static void TacticalReserveDoctrineDenyRemovalIgnoresOrderDelays()
    {
        AssertTrue(
            TacticalReservePolicyLedger.ShouldRemoveDeniedReserveMovement(
                doctrineDeny: true,
                beforePaths: 0,
                afterPaths: 1,
                beforeQueueCount: 0,
                afterQueueCount: 0,
                useOrderDelays: false),
            "direct path added under doctrine deny should be removed even without order delays");
        AssertTrue(
            TacticalReservePolicyLedger.ShouldRemoveDeniedReserveMovement(
                doctrineDeny: true,
                beforePaths: 2,
                afterPaths: 3,
                beforeQueueCount: 1,
                afterQueueCount: 1,
                useOrderDelays: true),
            "direct path added under doctrine deny should be removed with order delays");
        AssertFalse(
            TacticalReservePolicyLedger.ShouldRemoveDeniedReserveMovement(
                doctrineDeny: false,
                beforePaths: 0,
                afterPaths: 1,
                beforeQueueCount: 0,
                afterQueueCount: 0,
                useOrderDelays: false),
            "allow/observe should not remove vanilla reserve support");
        AssertFalse(
            TacticalReservePolicyLedger.ShouldRemoveDeniedReserveMovement(
                doctrineDeny: true,
                beforePaths: 0,
                afterPaths: 1,
                beforeQueueCount: 0,
                afterQueueCount: 1,
                useOrderDelays: false),
            "queued delayed order is not the direct vanilla path shape");
    }

    private static void TacticalReserveDelayGuardDoesNotConsumeDoctrineDenyMovement()
    {
        AssertFalse(
            TacticalReservePolicyLedger.ShouldDelayGuardConsumeReserveMovement(
                doctrineDeny: true,
                beforePaths: 0,
                afterPaths: 1,
                beforeQueueCount: 0,
                afterQueueCount: 0,
                useOrderDelays: true),
            "delay guard must not remove doctrine-denied direct movement before full restore owner runs");
        AssertTrue(
            TacticalReservePolicyLedger.ShouldDelayGuardConsumeReserveMovement(
                doctrineDeny: false,
                beforePaths: 0,
                afterPaths: 1,
                beforeQueueCount: 0,
                afterQueueCount: 0,
                useOrderDelays: true),
            "non-deny direct reserve movement remains eligible for delay conversion");
        AssertFalse(
            TacticalReservePolicyLedger.ShouldDelayGuardConsumeReserveMovement(
                doctrineDeny: false,
                beforePaths: 0,
                afterPaths: 1,
                beforeQueueCount: 0,
                afterQueueCount: 0,
                useOrderDelays: false),
            "delay conversion still requires order delays");
    }

    private static void TacticalGateHelpersWlOwnership()
    {
        AssertTrue(TacticalGateHelpers.PassesWlOwnership(aiFeudStance: -1, isPlayerAiOrFeud: 0), "feud=-1 passes");
        AssertTrue(TacticalGateHelpers.PassesWlOwnership(aiFeudStance: 5, isPlayerAiOrFeud: 2), "playerai=2 passes");
        AssertFalse(TacticalGateHelpers.PassesWlOwnership(aiFeudStance: 5, isPlayerAiOrFeud: 0), "neither passes");
    }

    private static void TacticalSupportScreenSupportedAndSteady()
    {
        var input = new TacticalSupportScreen.Input
        {
            ProtectedUnitMorale = 0.7f,
            MoraleFallbackThreshold = 0.4f,
            BattleStartMorale = 0.8f,
            EnemyDistance = 100f,
            DangerRadius = 200f,
            ScreenUnitCount = 1,
            AiFeudStance = -1,
            IsPlayerAiOrFeud = 0,
        };
        AssertEqual(TacticalSupportScreen.Result.Screened, TacticalSupportScreen.Score(input), "screened steady");
    }

    private static void TacticalSupportScreenShakenWithScreen()
    {
        var input = new TacticalSupportScreen.Input
        {
            ProtectedUnitMorale = 0.30f,
            MoraleFallbackThreshold = 0.40f,
            BattleStartMorale = 0.80f,
            EnemyDistance = 100f,
            DangerRadius = 200f,
            ScreenUnitCount = 1,
            AiFeudStance = -1,
            IsPlayerAiOrFeud = 0,
        };
        AssertEqual(TacticalSupportScreen.Result.Shaken, TacticalSupportScreen.Score(input), "shaken with screen");
    }

    private static void TacticalSupportScreenUnsupportedNoScreen()
    {
        var input = new TacticalSupportScreen.Input
        {
            ProtectedUnitMorale = 0.7f,
            MoraleFallbackThreshold = 0.4f,
            BattleStartMorale = 0.8f,
            EnemyDistance = 100f,
            DangerRadius = 200f,
            ScreenUnitCount = 0,
            AiFeudStance = -1,
            IsPlayerAiOrFeud = 0,
        };
        AssertEqual(TacticalSupportScreen.Result.Unsupported, TacticalSupportScreen.Score(input), "unsupported");
    }

    private static void TacticalSupportScreenUnknownOnUninitialized()
    {
        var input = new TacticalSupportScreen.Input
        {
            ProtectedUnitMorale = 0.7f,
            MoraleFallbackThreshold = 0.4f,
            BattleStartMorale = -1f,
            EnemyDistance = 100f,
            DangerRadius = 200f,
            ScreenUnitCount = 1,
            AiFeudStance = -1,
            IsPlayerAiOrFeud = 0,
        };
        AssertEqual(TacticalSupportScreen.Result.Unknown, TacticalSupportScreen.Score(input), "uninitialized");
    }

    private static void TacticalSupportScreenWlGateBlocks()
    {
        var input = new TacticalSupportScreen.Input
        {
            ProtectedUnitMorale = 0.7f,
            MoraleFallbackThreshold = 0.4f,
            BattleStartMorale = 0.8f,
            EnemyDistance = 100f,
            DangerRadius = 200f,
            ScreenUnitCount = 1,
            AiFeudStance = 5,
            IsPlayerAiOrFeud = 0,
        };
        AssertEqual(TacticalSupportScreen.Result.Unknown, TacticalSupportScreen.Score(input), "W&L gate blocks");
    }

    private static void TacticalArtilleryDoctrinePreservesFireWhenScreenedAndAmmoOk()
    {
        var input = new TacticalArtilleryDoctrine.Input
        {
            ScreenResult = TacticalSupportScreen.Result.Screened,
            AmmoTotalRatio = 0.6f,
            CanisterAmmo = 0.3f,
            ClosestEnemyDistance = 600f,
            UnitFireRange = 800f,
            EnemyArtilleryVisible = false,
            CombatBehaviorOrdered = 8,
            AiFeudStance = -1,
            IsPlayerAiOrFeud = 0,
        };
        AssertEqual(TacticalArtilleryDoctrine.Decision.PreserveFire,
            TacticalArtilleryDoctrine.Score(input), "screened + ammo ok -> preserve fire");
    }

    private static void TacticalArtilleryDoctrineCounterBatteryWhenEnemyArtVisible()
    {
        var input = new TacticalArtilleryDoctrine.Input
        {
            ScreenResult = TacticalSupportScreen.Result.Screened,
            AmmoTotalRatio = 0.6f,
            ClosestEnemyDistance = 700f,
            UnitFireRange = 800f,
            EnemyArtilleryVisible = true,
            CombatBehaviorOrdered = 8,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalArtilleryDoctrine.Decision.CounterBattery,
            TacticalArtilleryDoctrine.Score(input), "enemy art visible -> CB");
    }

    private static void TacticalArtilleryDoctrineCancelBombardWhenUnsupported()
    {
        var input = new TacticalArtilleryDoctrine.Input
        {
            ScreenResult = TacticalSupportScreen.Result.Unsupported,
            AmmoTotalRatio = 0.5f,
            ClosestEnemyDistance = 80f,
            UnitFireRange = 800f,
            CombatBehaviorOrdered = 8,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalArtilleryDoctrine.Decision.CancelBombard,
            TacticalArtilleryDoctrine.Score(input), "unsupported -> cancel bombard");
    }

    private static void TacticalArtilleryDoctrineDefensiveFallbackWhenShakenAndUnsupported()
    {
        var input = new TacticalArtilleryDoctrine.Input
        {
            ScreenResult = TacticalSupportScreen.Result.Shaken,
            AmmoTotalRatio = 0.5f,
            ClosestEnemyDistance = 90f,
            UnitFireRange = 800f,
            CombatBehaviorOrdered = 8,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalArtilleryDoctrine.Decision.DefensiveFallback,
            TacticalArtilleryDoctrine.Score(input), "shaken close enemy -> defensive fallback");
    }

    private static void TacticalArtilleryDoctrineCancelBombardOnLowAmmo()
    {
        var input = new TacticalArtilleryDoctrine.Input
        {
            ScreenResult = TacticalSupportScreen.Result.Screened,
            AmmoTotalRatio = 0.05f,
            ClosestEnemyDistance = 600f,
            UnitFireRange = 800f,
            CombatBehaviorOrdered = 8,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalArtilleryDoctrine.Decision.CancelBombard,
            TacticalArtilleryDoctrine.Score(input), "low ammo -> cancel bombard");
    }

    private static void TacticalArtilleryDoctrineWlGateBlocks()
    {
        var input = new TacticalArtilleryDoctrine.Input
        {
            ScreenResult = TacticalSupportScreen.Result.Screened,
            AmmoTotalRatio = 0.6f,
            ClosestEnemyDistance = 600f,
            UnitFireRange = 800f,
            CombatBehaviorOrdered = 8,
            AiFeudStance = 5,
            IsPlayerAiOrFeud = 0,
        };
        AssertEqual(TacticalArtilleryDoctrine.Decision.PreserveFire,
            TacticalArtilleryDoctrine.Score(input), "W&L gate -> safe default PreserveFire");
    }

    private static void TacticalArtilleryDoctrineFriendlyCloseDenyCancelsBombard()
    {
        var input = new TacticalArtilleryDoctrine.Input
        {
            ScreenResult = TacticalSupportScreen.Result.Screened,
            AmmoTotalRatio = 0.6f,
            ClosestEnemyDistance = 600f,
            UnitFireRange = 800f,
            EnemyArtilleryVisible = true,
            CombatBehaviorOrdered = 9,
            AiFeudStance = -1,
            DoctrineDecision = new DoctrineArtilleryDecision(
                DoctrineConsumerAction.Deny,
                "friendly-close-range"),
        };

        AssertEqual(TacticalArtilleryDoctrine.Decision.CancelBombard,
            TacticalArtilleryDoctrine.Score(input), "friendly close doctrine deny cancels bombard");
    }

    private static void TacticalArtilleryDoctrineSupportMainEffortSuppressesStrongpoint()
    {
        var input = new TacticalArtilleryDoctrine.Input
        {
            ScreenResult = TacticalSupportScreen.Result.Screened,
            AmmoTotalRatio = 0.6f,
            ClosestEnemyDistance = 900f,
            UnitFireRange = 800f,
            EnemyArtilleryVisible = true,
            CombatBehaviorOrdered = 7,
            AiFeudStance = -1,
            DoctrineDecision = new DoctrineArtilleryDecision(
                DoctrineConsumerAction.Allow,
                "support-main-effort"),
        };

        AssertEqual(TacticalArtilleryDoctrine.Decision.SuppressStrongpoint,
            TacticalArtilleryDoctrine.Score(input), "support main effort remains telemetry-only suppress strongpoint");
    }

    private static void TacticalMoralePressureStable()
    {
        var input = new TacticalMoralePressure.Input
        {
            CurrentMorale = 0.85f,
            BattleStartMorale = 0.90f,
            FallbackThreshold = 0.40f,
            Outflanked = 0,
            FriendlyRoutedNear = 0f,
            EnemyRoutedNear = 0f,
            ReceivedFireFromClosestFar = false,
            CoverValue = 0.5f,
            CoverObject = 0,
            AiFeudStance = -1,
            IsPlayerAiOrFeud = 0,
            BattleStartMoraleInitialized = true,
        };
        AssertEqual(TacticalMoralePressure.Result.Stable, TacticalMoralePressure.Score(input), "stable");
    }

    private static void TacticalMoralePressureUnderPressureFromOutflankedTier()
    {
        var input = new TacticalMoralePressure.Input
        {
            CurrentMorale = 0.85f,
            BattleStartMorale = 0.90f,
            FallbackThreshold = 0.40f,
            Outflanked = 1,
            BattleStartMoraleInitialized = true,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalMoralePressure.Result.UnderPressure,
            TacticalMoralePressure.Score(input), "outflanked tier 1 -> under pressure");
    }

    private static void TacticalMoralePressureFallbackCandidate()
    {
        var input = new TacticalMoralePressure.Input
        {
            CurrentMorale = 0.45f,
            BattleStartMorale = 0.85f,
            FallbackThreshold = 0.40f,
            Outflanked = 0,
            ReceivedFireFromClosestFar = true,
            BattleStartMoraleInitialized = true,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalMoralePressure.Result.FallbackCandidate,
            TacticalMoralePressure.Score(input), "fallback candidate");
    }

    private static void TacticalMoralePressureWithdrawalCandidateFlankNoCover()
    {
        var input = new TacticalMoralePressure.Input
        {
            CurrentMorale = 0.45f,
            BattleStartMorale = 0.85f,
            FallbackThreshold = 0.40f,
            Outflanked = 4,
            ReceivedFireFromClosestFar = true,
            CoverValue = 0f,
            CoverObject = 3,
            BattleStartMoraleInitialized = true,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalMoralePressure.Result.WithdrawalCandidate,
            TacticalMoralePressure.Score(input), "flank tier 4 + no cover -> withdrawal");
    }

    private static void TacticalMoralePressureCollapseCandidate()
    {
        var input = new TacticalMoralePressure.Input
        {
            CurrentMorale = 0.30f,
            BattleStartMorale = 0.85f,
            FallbackThreshold = 0.40f,
            BattleStartMoraleInitialized = true,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalMoralePressure.Result.CollapseCandidate,
            TacticalMoralePressure.Score(input), "morale below threshold -> collapse");
    }

    private static void TacticalMoralePressureStableOnUninitializedDeferToCaller()
    {
        var input = new TacticalMoralePressure.Input
        {
            CurrentMorale = 0.45f,
            BattleStartMorale = -1f,
            BattleStartMoraleInitialized = false,
            FallbackThreshold = 0.4f,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalMoralePressure.Result.Stable,
            TacticalMoralePressure.Score(input), "uninitialized -> stable (caller separates)");
    }

    private static void TacticalWithdrawalDoctrineHoldLineWhenStable()
    {
        var input = new TacticalWithdrawalDoctrine.Input
        {
            MoralePressure = TacticalMoralePressure.Result.Stable,
            RearPressureFlag = false,
            Fatigue = TacticalFatigueState.Result.Fresh,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalWithdrawalDoctrine.Decision.HoldLine,
            TacticalWithdrawalDoctrine.Score(input), "stable -> hold line");
    }

    private static void TacticalWithdrawalDoctrineStabilizeUnderPressure()
    {
        var input = new TacticalWithdrawalDoctrine.Input
        {
            MoralePressure = TacticalMoralePressure.Result.UnderPressure,
            Fatigue = TacticalFatigueState.Result.Tiring,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalWithdrawalDoctrine.Decision.Stabilize,
            TacticalWithdrawalDoctrine.Score(input), "under pressure -> stabilize");
    }

    private static void TacticalWithdrawalDoctrineScreenForFallbackCandidate()
    {
        var input = new TacticalWithdrawalDoctrine.Input
        {
            MoralePressure = TacticalMoralePressure.Result.FallbackCandidate,
            Fatigue = TacticalFatigueState.Result.Tiring,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalWithdrawalDoctrine.Decision.Screen,
            TacticalWithdrawalDoctrine.Score(input), "fallback candidate -> screen");
    }

    private static void TacticalWithdrawalDoctrineRearGuardForWithdrawalCandidate()
    {
        var input = new TacticalWithdrawalDoctrine.Input
        {
            MoralePressure = TacticalMoralePressure.Result.WithdrawalCandidate,
            Fatigue = TacticalFatigueState.Result.Spent,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalWithdrawalDoctrine.Decision.RearGuard,
            TacticalWithdrawalDoctrine.Score(input), "withdrawal candidate -> rear guard");
    }

    private static void TacticalWithdrawalDoctrineFullRetreatOnCollapse()
    {
        var input = new TacticalWithdrawalDoctrine.Input
        {
            MoralePressure = TacticalMoralePressure.Result.CollapseCandidate,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalWithdrawalDoctrine.Decision.FullRetreat,
            TacticalWithdrawalDoctrine.Score(input), "collapse -> full retreat");
    }

    private static void TacticalWithdrawalDoctrineRearPressureBumpsLadder()
    {
        var input = new TacticalWithdrawalDoctrine.Input
        {
            MoralePressure = TacticalMoralePressure.Result.UnderPressure,
            RearPressureFlag = true,
            Fatigue = TacticalFatigueState.Result.Spent,
            AiFeudStance = -1,
        };
        // Rear-pressure + Spent fatigue bumps UnderPressure to Screen (mid-ladder).
        AssertEqual(TacticalWithdrawalDoctrine.Decision.Screen,
            TacticalWithdrawalDoctrine.Score(input), "rear pressure + spent fatigue bumps to screen");
    }

    private static void TacticalWithdrawalDoctrineWlGateBlocks()
    {
        var input = new TacticalWithdrawalDoctrine.Input
        {
            MoralePressure = TacticalMoralePressure.Result.CollapseCandidate,
            AiFeudStance = 5,
            IsPlayerAiOrFeud = 0,
        };
        AssertEqual(TacticalWithdrawalDoctrine.Decision.HoldLine,
            TacticalWithdrawalDoctrine.Score(input), "W&L gate -> safe default HoldLine");
    }

    private static void TacticalSupportScreenQuietWhenNoEnemyAndNoScreen()
    {
        // Documents the design intent: "no enemy near = nothing to support against = treat as Screened."
        // B7 wiring plans must treat Result.Screened as "OK to fire" only when an enemy IS in range.
        var input = new TacticalSupportScreen.Input
        {
            ProtectedUnitMorale = 0.7f,
            MoraleFallbackThreshold = 0.4f,
            BattleStartMorale = 0.8f,
            EnemyDistance = 9999f,
            DangerRadius = 200f,
            ScreenUnitCount = 0,
            AiFeudStance = -1,
            IsPlayerAiOrFeud = 0,
        };
        AssertEqual(TacticalSupportScreen.Result.Screened,
            TacticalSupportScreen.Score(input), "no enemy + no screen falls through to Screened");
    }

    private static void TacticalUnitTypeConstantsMatchVanillaUnittyp()
    {
        AssertEqual(0, TacticalUnitType.Infantry, "infantry = 0");
        AssertEqual(1, TacticalUnitType.Cavalry, "cavalry = 1");
        AssertEqual(2, TacticalUnitType.Artillery, "artillery = 2");
        AssertEqual(3, TacticalUnitType.Skirmisher, "skirmisher = 3");
        AssertEqual(4, TacticalUnitType.Officer, "officer = 4");
        AssertEqual(5, TacticalUnitType.Excluded, "excluded = 5");
        AssertEqual(13, TacticalUnitType.MaxCombat, "max combat = 13");
        AssertEqual(14, TacticalUnitType.BattleGroupBrigade, "battle group brigade = 14");
        AssertEqual(15, TacticalUnitType.BattleGroupDivision, "battle group division = 15");
        AssertEqual(16, TacticalUnitType.BattleGroupArmy, "battle group army = 16");
    }

    private static void TacticalWithdrawalInputAdapterToMoralePressureInput()
    {
        var snapshot = new TacticalWithdrawalInputAdapter.Snapshot
        {
            Morale = 0.55f,
            BattleStartMorale = 0.85f,
            BattleStartMoraleInitialized = true,
            FallbackThreshold = 0.40f,
            Outflanked = 2,
            FriendlyRoutedNear = 1f,
            EnemyRoutedNear = 0f,
            ReceivedFireFromClosestFar = true,
            CoverValue = 0.2f,
            CoverObject = 0,
            AiFeudStance = -1,
            IsPlayerAiOrFeud = 0,
        };
        var input = TacticalWithdrawalInputAdapter.ToMoralePressureInput(snapshot);
        AssertEqual(0.55f, input.CurrentMorale, "morale carried");
        AssertEqual(2, input.Outflanked, "outflanked carried");
        AssertEqual(true, input.ReceivedFireFromClosestFar, "fire flag carried");
        AssertEqual(true, input.BattleStartMoraleInitialized, "init flag carried");
    }

    private static void TacticalWithdrawalInputAdapterToQuadrantInput()
    {
        var slices = new float[36];
        var snapshot = new TacticalWithdrawalInputAdapter.Snapshot
        {
            EnemyStrengthWithinAngle = slices,
            SliceWidthDegrees = 10f,
            UnitFacingDegrees = 90f,
        };
        var input = TacticalWithdrawalInputAdapter.ToQuadrantInput(snapshot);
        AssertEqual(slices.Length, input.Slices.Length, "slices carried");
        AssertEqual(10f, input.SliceWidthDegrees, "slice width carried");
        AssertEqual(90f, input.UnitFacingDegrees, "facing carried");
    }

    private static void TacticalFatigueStateBands()
    {
        AssertEqual(TacticalFatigueState.Result.Fresh, TacticalFatigueState.Score(0.10f), "0.10 fresh");
        AssertEqual(TacticalFatigueState.Result.Fresh, TacticalFatigueState.Score(0.24f), "boundary < 0.25");
        AssertEqual(TacticalFatigueState.Result.Tiring, TacticalFatigueState.Score(0.25f), "0.25 tiring");
        AssertEqual(TacticalFatigueState.Result.Tiring, TacticalFatigueState.Score(0.54f), "boundary < 0.55");
        AssertEqual(TacticalFatigueState.Result.Spent, TacticalFatigueState.Score(0.55f), "0.55 spent");
        AssertEqual(TacticalFatigueState.Result.Spent, TacticalFatigueState.Score(0.79f), "boundary < 0.80");
        AssertEqual(TacticalFatigueState.Result.Exhausted, TacticalFatigueState.Score(0.80f), "0.80 exhausted");
        AssertEqual(TacticalFatigueState.Result.Exhausted, TacticalFatigueState.Score(1.00f), "1.00 exhausted");
    }

    private static void TacticalFatigueStateClampsBelow()
    {
        AssertEqual(TacticalFatigueState.Result.Fresh, TacticalFatigueState.Score(-0.5f), "negative clamps fresh");
    }

    private static void TacticalFatigueStateClampsAbove()
    {
        AssertEqual(TacticalFatigueState.Result.Exhausted, TacticalFatigueState.Score(2.0f), "above 1 clamps exhausted");
    }

    // ---- Army orchestrator tests (O1.7) ----

    private static void TacticalIntentModelRecordsAllFields()
    {
        var evidence = new[] { EvidenceTag.SectorConcentration, EvidenceTag.ReserveUncommitted };
        var model = new TacticalIntentModel(
            primaryIntent: InferredIntent.Attack,
            inferredMainEffort: 3,
            confidence01: 0.62f,
            ageSeconds: 12.5f,
            supportingEvidence: evidence);

        AssertEqual(InferredIntent.Attack, model.PrimaryIntent, "primary intent");
        AssertEqual(3, model.InferredMainEffort, "inferred main effort");
        AssertNear(0.62f, model.Confidence01, 1e-5f, "confidence");
        AssertNear(12.5f, model.AgeSeconds, 1e-5f, "age");
        AssertEqual(2, model.SupportingEvidence.Length, "supporting evidence length");
        AssertEqual(EvidenceTag.SectorConcentration, model.SupportingEvidence[0], "evidence 0");
        AssertEqual(EvidenceTag.ReserveUncommitted, model.SupportingEvidence[1], "evidence 1");

        evidence[0] = EvidenceTag.Unknown;
        AssertEqual(EvidenceTag.SectorConcentration, model.SupportingEvidence[0], "evidence snapshot owns copy");
    }

    private static void TacticalIntentModelClampsConfidenceAndAge()
    {
        var clampedHigh = new TacticalIntentModel(
            InferredIntent.Defend,
            inferredMainEffort: 0,
            confidence01: 1.5f,
            ageSeconds: -3f,
            supportingEvidence: null);

        AssertNear(1.0f, clampedHigh.Confidence01, 1e-5f, "high confidence clamps to 1");
        AssertNear(0f, clampedHigh.AgeSeconds, 1e-5f, "negative age clamps to 0");
        AssertEqual(0, clampedHigh.SupportingEvidence.Length, "null evidence becomes empty");

        var clampedLow = new TacticalIntentModel(
            InferredIntent.Defend,
            inferredMainEffort: 0,
            confidence01: -0.2f,
            ageSeconds: float.NaN,
            supportingEvidence: null);

        AssertNear(0f, clampedLow.Confidence01, 1e-5f, "low confidence clamps to 0");
        AssertNear(0f, clampedLow.AgeSeconds, 1e-5f, "NaN age becomes 0");

        var confidenceNaN = new TacticalIntentModel(InferredIntent.Defend, 0, float.NaN, 0f, null);
        var confidencePositiveInfinity = new TacticalIntentModel(InferredIntent.Defend, 0, float.PositiveInfinity, 0f, null);
        var confidenceNegativeInfinity = new TacticalIntentModel(InferredIntent.Defend, 0, float.NegativeInfinity, 0f, null);
        var agePositiveInfinity = new TacticalIntentModel(InferredIntent.Defend, 0, 0.5f, float.PositiveInfinity, null);
        var ageNegativeInfinity = new TacticalIntentModel(InferredIntent.Defend, 0, 0.5f, float.NegativeInfinity, null);

        AssertNear(0f, confidenceNaN.Confidence01, 1e-5f, "NaN confidence becomes 0");
        AssertNear(0f, confidencePositiveInfinity.Confidence01, 1e-5f, "positive infinity confidence becomes 0");
        AssertNear(0f, confidenceNegativeInfinity.Confidence01, 1e-5f, "negative infinity confidence becomes 0");
        AssertNear(0f, agePositiveInfinity.AgeSeconds, 1e-5f, "positive infinity age becomes 0");
        AssertNear(0f, ageNegativeInfinity.AgeSeconds, 1e-5f, "negative infinity age becomes 0");
    }

    private static void TacticalIntentModelUnknownPrimaryIntentSentinel()
    {
        var unknown = new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, null);

        AssertEqual(InferredIntent.Unknown, unknown.PrimaryIntent, "unknown primary intent");
        AssertEqual(-1, unknown.InferredMainEffort, "unknown main effort sentinel");
        AssertNear(0f, unknown.Confidence01, 1e-5f, "unknown confidence");
    }

    private static TacticalReserveCommitGate.Input ReserveGateInput(
        bool vanillaCommitted = true,
        bool resolved = true,
        DirectChildRole role = DirectChildRole.Reserve,
        bool playerControlled = false,
        bool committedUnitAlreadyEngaged = false,
        float ownStrengthRatio = 1.0f,
        float localOdds = 1.0f)
    {
        return new TacticalReserveCommitGate.Input(
            vanillaCommitted,
            new CommandIntentResolution(
                resolved,
                new CommandNodeIntent(
                    "node-200",
                    "node-200",
                    role,
                    DirectChildAxis.Hold,
                    primarySector: 2,
                    supportPriority: 50,
                    aggressionBias01: 0.5f,
                    depth: 1),
                resolved ? "exact-command-node" : "command-node-not-found"),
            playerControlled,
            committedUnitAlreadyEngaged,
            ownStrengthRatio,
            localOdds);
    }

    private static void TacticalReserveCommitGateObservesWhenNoVanillaMove()
    {
        var d = TacticalReserveCommitGate.Decide(ReserveGateInput(vanillaCommitted: false));
        AssertEqual(TacticalReserveCommitGate.Action.Observe, d.Action, "action");
        AssertEqual("no-vanilla-commit", d.Reason, "reason");
    }

    private static void TacticalReserveCommitGateDeniesReserveRoleMovement()
    {
        var d = TacticalReserveCommitGate.Decide(ReserveGateInput(role: DirectChildRole.Reserve));
        AssertEqual(TacticalReserveCommitGate.Action.Deny, d.Action, "action");
        AssertEqual("role-reserve-hold", d.Reason, "reason");
    }

    private static void TacticalReserveCommitGateAllowsMainUnderstrengthMovement()
    {
        var d = TacticalReserveCommitGate.Decide(ReserveGateInput(role: DirectChildRole.Main, ownStrengthRatio: 0.60f));
        AssertEqual(TacticalReserveCommitGate.Action.Allow, d.Action, "action");
        AssertEqual("main-understrength-release", d.Reason, "reason");
    }

    private static void TacticalReserveCommitGateAllowsFallbackScreenMovement()
    {
        var d = TacticalReserveCommitGate.Decide(ReserveGateInput(role: DirectChildRole.Fallback, localOdds: 0.70f));
        AssertEqual(TacticalReserveCommitGate.Action.Allow, d.Action, "action");
        AssertEqual("fallback-screen-retreat", d.Reason, "reason");
    }

    private static void TacticalReserveCommitGateObservesPlayerControlledGroup()
    {
        var d = TacticalReserveCommitGate.Decide(ReserveGateInput(playerControlled: true));
        AssertEqual(TacticalReserveCommitGate.Action.Observe, d.Action, "action");
        AssertEqual("player-controlled", d.Reason, "reason");
    }

    private static void TacticalReserveCommitGateAllowsAlreadyEngagedReserve()
    {
        var d = TacticalReserveCommitGate.Decide(ReserveGateInput(role: DirectChildRole.Reserve, committedUnitAlreadyEngaged: true));
        AssertEqual(TacticalReserveCommitGate.Action.Allow, d.Action, "action");
        AssertEqual("already-committed-contact", d.Reason, "reason");
    }
}
