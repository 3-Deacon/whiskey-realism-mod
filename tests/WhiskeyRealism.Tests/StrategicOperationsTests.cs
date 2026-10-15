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

internal static class StrategicOperationsTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("daily cadence fires on first call and day rollover only", DailyCadenceFiresOnFirstCallAndDayRolloverOnly),
            ("daily cadence rejects invalid dates", DailyCadenceRejectsInvalidDates),
            ("strategic cadence alternates formation by alliance", StrategicCadenceAlternatesFormationByAlliance),
            ("strategic cadence refreshes weekly or on source change", StrategicCadenceRefreshesWeeklyOrSourceChange),
            ("strategic cadence stable source can skip downstream rebuild", StrategicCadenceStableSourceSkipsDownstreamRebuild),
            ("strategic battle intent snapshot sanitizes nonfinite pressure", StrategicBattleIntentSnapshotSanitizesNonfinitePressure),
            ("operational startup gate fires once when runtime becomes ready same day", OperationalStartupGateFiresOnceWhenRuntimeBecomesReadySameDay),
            ("historical operation catalog exact objective match", HistoricalOperationCatalogExactObjectiveMatch),
            ("historical operation catalog no profile for unmatched objective", HistoricalOperationCatalogNoProfileForUnmatchedObjective),
            ("historical operation dynamic victory exploits", HistoricalOperationDynamicVictoryExploits),
            ("historical operation unavailable objective aborts", HistoricalOperationUnavailableObjectiveAborts),
            ("historical operation dynamic action mutates phase posture", HistoricalOperationDynamicActionMutatesPhasePosture),
            ("formation directive summary changes when assignment changes", FormationDirectiveSummaryChangesWhenAssignmentChanges),
            ("formation directive carries stable id and position", FormationDirectiveCarriesStableIdAndPosition),
            ("formation directive summary changes on stable position", FormationDirectiveSummaryChangesOnStablePosition),
            ("operational probe assigns one bounded same-area formation", OperationalProbeAssignsOneBoundedSameAreaFormation),
            ("operational probe pauses on enemy reaction", OperationalProbePausesOnEnemyReaction),
            ("operational probe escalates after favorable contact", OperationalProbeEscalatesAfterFavorableContact),
            ("operational probe stale belief cannot escalate", OperationalProbeStaleBeliefCannotEscalate),
            ("operational probe refuses critical hold donor", OperationalProbeRefusesCriticalHoldDonor),
            ("operational probe overlays formation directive", OperationalProbeOverlaysFormationDirective),
            ("operational probe escalates with support package", OperationalProbeEscalatesWithSupportPackage),
            ("operational probe package escalation requires favorable evidence", OperationalProbePackageEscalationRequiresFavorableEvidence),
            ("operational probe package options use local enemy fallback", OperationalProbePackageOptionsUseLocalEnemyFallback),
            ("operational probe support overlay blocks donor", OperationalProbeSupportOverlayBlocksDonor),
            ("operational probe stays continuing on no contact even after minimum days", OperationalProbeStaysContinuingOnNoContactAfterMinimumDays),
            ("operational probe state has single source on coordinator", OperationalProbeStateHasSingleSourceOnCoordinator),
            ("operational probe copies objective id", OperationalProbeCopiesObjectiveId),
            ("coordinated ops attack selects local support", CoordinatedOpsAttackSelectsLocalSupport),
            ("coordinated ops blocked wl support does not fake attack", CoordinatedOpsBlockedWlSupportDoesNotFakeAttack),
            ("coordinated ops lead selection rejects remote oversized candidate", CoordinatedOpsLeadSelectionRejectsRemoteOversizedCandidate),
            ("coordinated ops lead overmatch stays single lead", CoordinatedOpsLeadOvermatchStaysSingleLead),
            ("coordinated ops reinforce uses defensive eligibility", CoordinatedOpsReinforceUsesDefensiveEligibility),
            ("coordinated ops reinforce blocks non donor support", CoordinatedOpsReinforceBlocksNonDonorSupport),
            ("coordinated ops wl current order does not require direct movement", CoordinatedOpsWlCurrentOrderDoesNotRequireDirectMovement),
            ("coordinated ops bridge decision maps blocked commit mode", CoordinatedOpsBridgeDecisionMapsBlockedCommitMode),
            ("coordinated ops nearest map name resolves target", CoordinatedOpsNearestMapNameResolvesTarget),
            ("coordinated ops target name falls back to area key", CoordinatedOpsTargetNameFallsBackToAreaKey),
            ("coordinated ops empty target is single lead", CoordinatedOpsEmptyTargetIsSingleLead),
            ("coordinated ops high risk tightens donor caps", CoordinatedOpsHighRiskTightensDonorCaps),
            ("coordinated ops player cic returns none", CoordinatedOpsPlayerCicReturnsNone),
            ("coordinated ops refuses live operation list candidates", CoordinatedOpsRefusesLiveOperationListCandidates),
            ("coordinated ops deterministic tie by stable id", CoordinatedOpsDeterministicTieByStableId),
            ("operational tempo chapter one delays escalation", OperationalTempoChapterOneDelaysEscalation),
            ("operational tempo late union sustains pressure", OperationalTempoLateUnionSustainsPressure),
            ("operational tempo winter slows probes", OperationalTempoWinterSlowsProbes),
            ("operational tempo late csa is more conservative than union", OperationalTempoLateCsaMoreConservativeThanUnion),
            ("strategic movement budget blocks area export from hold sector", StrategicMovementBudgetBlocksAreaExportFromHoldSector),
            ("phase truth advances when target accomplished", PhaseTruthAdvancesWhenTargetAccomplished),
            ("phase truth replans when objective unavailable", PhaseTruthReplansWhenObjectiveUnavailable),
            ("phase truth recovers when force below threshold", PhaseTruthRecoversWhenForceBelowThreshold),
            ("phase truth deadline expired advances or replans", PhaseTruthDeadlineExpiredAdvancesOrReplans),
            ("phase truth no contact stays continue", PhaseTruthNoContactStaysContinue),
            ("cic review plan replans when phase truth says target accomplished", CicReviewPlanReplansWhenPhaseTruthSaysAccomplished),
        });
    }

    private static void StrategicCadenceAlternatesFormationByAlliance()
    {
        AssertTrue(StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day: 1, alliance: 1),
            "CSA should run on odd days");
        AssertTrue(!StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day: 1, alliance: 0),
            "Union should skip odd days without a force refresh");
        AssertTrue(StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day: 2, alliance: 0),
            "Union should run on even days");
        AssertTrue(!StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day: 2, alliance: 1),
            "CSA should skip even days without a force refresh");
        AssertTrue(StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day: 2, alliance: 1, forceRefresh: true),
            "force refresh should override alternating cadence");
    }

    private static void StrategicCadenceRefreshesWeeklyOrSourceChange()
    {
        AssertTrue(StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                day: 1,
                currentSourceSignature: "sig-a",
                previousSourceSignature: null,
                forceRefresh: false),
            "first source observation should run");
        AssertTrue(!StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                day: 3,
                currentSourceSignature: "sig-a",
                previousSourceSignature: "sig-a",
                forceRefresh: false),
            "stable source before weekly boundary should skip");
        AssertTrue(StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                day: 3,
                currentSourceSignature: "sig-b",
                previousSourceSignature: "sig-a",
                forceRefresh: false),
            "source change should run immediately");
        AssertTrue(StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                day: 7,
                currentSourceSignature: "sig-a",
                previousSourceSignature: "sig-a",
                forceRefresh: false),
            "weekly boundary should run even when source is stable");
        AssertTrue(StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                day: 3,
                currentSourceSignature: "sig-a",
                previousSourceSignature: "sig-a",
                forceRefresh: true),
            "force refresh should run immediately");
    }

    private static void StrategicCadenceStableSourceSkipsDownstreamRebuild()
    {
        AssertTrue(StrategicCadencePolicy.SourceChanged("front:2", "front:1"),
            "different front signatures should count as changed");
        AssertTrue(!StrategicCadencePolicy.SourceChanged("front:1", "front:1"),
            "same front signatures should be stable");
        AssertTrue(StrategicCadencePolicy.SourceChanged("front:1", null),
            "missing previous signature should force first rebuild");
    }

    private static void StrategicBattleIntentSnapshotSanitizesNonfinitePressure()
    {
        var snapshot = new StrategicBattleIntentSnapshot(
            casualtyPressure: float.PositiveInfinity,
            timePressure: float.NaN,
            theaterIntent: null,
            campaignIntent: "  ",
            allianceId: 3,
            campaignObjectiveId: null,
            theaterPriority: float.PositiveInfinity,
            casualtyTolerance: -2f,
            preserveForceBias: float.NaN,
            commanderPersonality: new PersonalityVector(2f, -2f, float.NaN, 0.5f, 0f));

        AssertEqual(0f, snapshot.CasualtyPressure, "casualty pressure");
        AssertEqual(0f, snapshot.TimePressure, "time pressure");
        AssertEqual(string.Empty, snapshot.TheaterIntent, "theater intent");
        AssertEqual(string.Empty, snapshot.CampaignIntent, "campaign intent");
        AssertEqual(-1, snapshot.AllianceId, "invalid alliance");
        AssertEqual(string.Empty, snapshot.CampaignObjectiveId, "campaign objective id");
        AssertEqual(0f, snapshot.TheaterPriority, "theater priority");
        AssertEqual(-1f, snapshot.CasualtyTolerance, "casualty tolerance");
        AssertEqual(0f, snapshot.PreserveForceBias, "preserve force bias");
        AssertEqual(1f, snapshot.CommanderPersonality.Aggression, "commander aggression");
        AssertEqual(-1f, snapshot.CommanderPersonality.Caution, "commander caution");
        AssertEqual(0f, snapshot.CommanderPersonality.Audacity, "commander audacity");
    }

    private static void DailyCadenceFiresOnFirstCallAndDayRolloverOnly()
    {
        var cadence = new DailyCadence();
        AssertTrue(cadence.ShouldFire(1, 6, 1861), "first call should fire");
        AssertTrue(!cadence.ShouldFire(1, 6, 1861), "same day should not fire again");
        AssertTrue(cadence.ShouldFire(2, 6, 1861), "next day should fire");
        AssertTrue(cadence.ShouldFire(1, 7, 1861), "month rollover should fire");
        AssertTrue(cadence.ShouldFire(1, 1, 1862), "year rollover should fire");
    }

    private static void DailyCadenceRejectsInvalidDates()
    {
        var cadence = new DailyCadence();
        AssertTrue(!cadence.ShouldFire(0, 6, 1861), "day 0 should be ignored");
        AssertTrue(!cadence.ShouldFire(1, 0, 1861), "month 0 should be ignored");
        AssertTrue(!cadence.ShouldFire(1, 6, 0), "year 0 should be ignored");
    }

    private static void OperationalStartupGateFiresOnceWhenRuntimeBecomesReadySameDay()
    {
        var gate = new OperationalStartupGate();

        AssertEqual(true, gate.ShouldNotify(dateChanged: true, runtimeReady: false));
        AssertEqual(false, gate.ShouldNotify(dateChanged: false, runtimeReady: false));
        AssertEqual(true, gate.ShouldNotify(dateChanged: false, runtimeReady: true));
        AssertEqual(false, gate.ShouldNotify(dateChanged: false, runtimeReady: true));
        AssertEqual(true, gate.ShouldNotify(dateChanged: true, runtimeReady: true));
    }

    private static void HistoricalOperationCatalogExactObjectiveMatch()
    {
        AssertTrue(ObjectiveCatalog.TryResolve(3, out var objective), "expected objective 3");
        var match = HistoricalOperationCatalog.Resolve(
            allianceId: 0,
            era: EraStage.Amateur1861,
            vanillaChapter: 1,
            month: 7,
            year: 1861,
            candidate: new HistoricalOperationCandidate { ObjectiveId = 3, Objective = objective, ObjectiveScore = 10f },
            strategy: GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861),
            cicPersonality: default(PersonalityVector),
            posture: null,
            context: null);

        AssertEqual(HistoricalOperationMatchKind.Matched, match.Kind);
        AssertEqual("union-east-pressure", match.Profile.OperationId);
        AssertTrue(match.Profile.Phases[0].TargetObjectiveId >= 0, "phase must preserve objective id");
    }

    private static void HistoricalOperationCatalogNoProfileForUnmatchedObjective()
    {
        var match = HistoricalOperationCatalog.Resolve(
            allianceId: 0,
            era: EraStage.Amateur1861,
            vanillaChapter: 1,
            month: 7,
            year: 1861,
            candidate: new HistoricalOperationCandidate
            {
                ObjectiveId = 999,
                Objective = ObjectiveMetadata.DefaultDerived(Theater.Unknown, 0f, 0f),
                ObjectiveScore = 1f
            },
            strategy: null,
            cicPersonality: default(PersonalityVector),
            posture: null,
            context: null);

        AssertEqual(HistoricalOperationMatchKind.NoProfile, match.Kind);
        AssertEqual("no-explicit-profile", match.Reason);
    }

    private static void HistoricalOperationDynamicVictoryExploits()
    {
        AssertTrue(HistoricalOperationCatalog.TryGetById("union-late-pressure", out var profile), "expected late profile");
        var output = OperationDynamicRuleEvaluator.Evaluate(
            new PhaseTruthOutput
            {
                Verdict = PhaseTruthVerdict.Valid,
                RecommendedAction = PhaseTruthAction.Continue,
                Reason = "phase-valid"
            },
            profile,
            new HistoricalOperationContext
            {
                MajorFriendlyVictoryNearTarget = true,
                TargetLocalOwnStrength = 20000f,
                TargetLocalEnemyStrength = 10000f,
                TargetLocalRatio = 2f
            },
            allianceId: 0,
            daySerial: 1864 * 372);

        AssertEqual(PhaseTruthAction.Exploit, output.RecommendedAction);
        AssertEqual("friendly-victory-exploit", output.RuleId);
    }

    private static void HistoricalOperationUnavailableObjectiveAborts()
    {
        AssertTrue(HistoricalOperationCatalog.TryGetById("union-east-pressure", out var profile), "expected early profile");
        var plan = new OperationalPlan();
        plan.Phases.Add(new Phase { TargetObjectiveId = 3, DeadlineMonth = 12, DeadlineYear = 1861 });

        var output = PhaseTruthLedger.Evaluate(new PhaseTruthInput
        {
            Plan = plan,
            OperationProfile = profile,
            ObjectiveAvailable = false,
            TargetPositionResolves = true,
            CurrentMonth = 7,
            CurrentYear = 1861
        });

        AssertEqual(PhaseTruthVerdict.ObjectiveUnavailable, output.Verdict);
        AssertEqual(PhaseTruthAction.Abort, output.RecommendedAction);
        AssertEqual("objective-unavailable-abort", output.RuleId);
    }

    private static void HistoricalOperationDynamicActionMutatesPhasePosture()
    {
        // R3: posture actions still run the deadline baseline, so the fixture needs live deadlines.
        var plan = new OperationalPlan
        {
            OperationPosture = OperationPosture.ProbeAndDevelop,
            CurrentPhaseIndex = 0,
            PlanDeadlineMonth = 12,
            PlanDeadlineYear = 1864
        };
        plan.Phases.Add(new Phase
        {
            TargetObjectiveId = 3,
            DeadlineMonth = 12,
            DeadlineYear = 1864,
            OperationPosture = OperationPosture.ProbeAndDevelop,
            AllowCoordinatedAttack = false,
            AllowReinforcementPackage = false,
            AllowProbeOnly = true
        });

        bool keepPlan = CicReviewRouter.Route(
            plan,
            new PhaseTruthOutput
            {
                Verdict = PhaseTruthVerdict.Valid,
                RecommendedAction = PhaseTruthAction.Exploit,
                RuleId = "friendly-victory-exploit"
            },
            1864 * 372 + 7 * 31 + 1,
            7,
            1864) == CicRouteOutcome.KeepPlan;

        AssertEqual(true, keepPlan);
        AssertEqual(OperationPosture.ExploitBreakthrough, plan.OperationPosture);
        AssertEqual(OperationPosture.ExploitBreakthrough, plan.CurrentPhase.OperationPosture);
        AssertEqual(true, plan.CurrentPhase.AllowCoordinatedAttack);
        AssertEqual(true, plan.CurrentPhase.AllowReinforcementPackage);
        AssertEqual(false, plan.CurrentPhase.AllowProbeOnly);
    }

    private static void FormationDirectiveSummaryChangesWhenAssignmentChanges()
    {
        var a = Snapshot("unit", 0, 15, 15000f, 10000f, FormationLevel.Corps, FrontPosture.Hold);
        var b = Snapshot("unit", 0, 15, 15000f, 10000f, FormationLevel.Corps, FrontPosture.Counterstroke);

        string first = FormationDirectiveLedger.Build(new[] { a }, EraStage.Operational1862, null).Summary();
        string second = FormationDirectiveLedger.Build(new[] { b }, EraStage.Operational1862, null).Summary();

        AssertEqual(false, string.Equals(first, second, StringComparison.Ordinal));
    }

    private static void FormationDirectiveCarriesStableIdAndPosition()
    {
        var snap = Snapshot("position-corps", 1, 15, 9000f, 5000f, FormationLevel.Corps, FrontPosture.Counterstroke);
        snap.StableUnitId = 4242;
        snap.X = 123.5f;
        snap.Z = -456.25f;

        var ledger = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Operational1862, null);
        var assignment = ledger.GetAssignment("position-corps");

        AssertEqual(4242, assignment.StableUnitId);
        AssertNear(123.5f, assignment.X, 0.0001f, "assignment X");
        AssertNear(-456.25f, assignment.Z, 0.0001f, "assignment Z");
    }

    private static void FormationDirectiveSummaryChangesOnStablePosition()
    {
        var a = Snapshot("position-corps", 1, 15, 9000f, 5000f, FormationLevel.Corps, FrontPosture.Counterstroke);
        var b = Snapshot("position-corps", 1, 15, 9000f, 5000f, FormationLevel.Corps, FrontPosture.Counterstroke);
        a.StableUnitId = 1;
        b.StableUnitId = 1;
        a.X = 10f;
        a.Z = 10f;
        b.X = 40f;
        b.Z = 10f;

        string first = FormationDirectiveLedger.Build(new[] { a }, EraStage.Operational1862, null).Summary();
        string second = FormationDirectiveLedger.Build(new[] { b }, EraStage.Operational1862, null).Summary();

        AssertTrue(first != second, "summary must change when stable position changes");
    }

    private static OperationalProbeInput BuildProbeInput()
    {
        var front = FrontSectorLedger.Build(new[]
        {
            new FrontSectorInput
            {
                SectorKey = "VirginiaCapitalCorridor",
                Theater = Theater.East,
                OwnStrength = 22000f,
                EnemyStrength = 14000f,
                StrategicImportance = 0.8f,
                IsCritical = false,
                IsPlanTarget = true,
                CommanderAudacity = 0.3f,
                CommanderCaution = 0.2f,
                AverageMorale = 0.8f,
                AverageSupply = 0.8f,
                AverageReadiness = 0.8f
            },
            new FrontSectorInput
            {
                SectorKey = "Richmond",
                Theater = Theater.East,
                OwnStrength = 20000f,
                EnemyStrength = 18000f,
                StrategicImportance = 1.0f,
                IsCritical = true,
                IsPlanTarget = false,
                CommanderAudacity = 0.1f,
                CommanderCaution = 0.5f,
                AverageMorale = 0.8f,
                AverageSupply = 0.8f,
                AverageReadiness = 0.8f
            }
        });

        return new OperationalProbeInput
        {
            AllianceId = 1,
            DaySerial = 100,
            PlanTargetAreaKey = "VirginiaCapitalCorridor",
            Fronts = front,
            FormationDirectives = FormationDirectiveLedger.Build(new[]
            {
                ProbeSnapshot("probe-corps", 1, 15, 7000f, 5000f, FormationLevel.Division, FrontPosture.Counterstroke, "VirginiaCapitalCorridor"),
                Snapshot("hold-army", 1, 16, 26000f, 18000f, FormationLevel.Army, FrontPosture.Hold)
            }, EraStage.Operational1862, "VirginiaCapitalCorridor")
        };
    }

    private static FormationSnapshot ProbeSnapshot(
        string key,
        int alliance,
        int unitType,
        float strength,
        float enemy,
        FormationLevel enemyLevel,
        FrontPosture posture,
        string sectorKey)
    {
        var snapshot = Snapshot(key, alliance, unitType, strength, enemy, enemyLevel, posture);
        snapshot.SectorKey = sectorKey;
        return snapshot;
    }

    private static void OperationalProbeAssignsOneBoundedSameAreaFormation()
    {
        var output = OperationalProbeLedger.Build(BuildProbeInput());

        AssertEqual(OperationalProbeDecision.Probe, output.Decision);
        AssertEqual("probe-corps", output.SelectedUnitKey);
        AssertEqual("VirginiaCapitalCorridor", output.TargetAreaKey);
        AssertEqual(false, output.RequiresMassCommitment);
    }

    private static void OperationalProbePausesOnEnemyReaction()
    {
        var input = BuildProbeInput();
        input.Previous = new OperationalProbeState
        {
            ProbeId = "1:VirginiaCapitalCorridor:probe-corps",
            UnitKey = "probe-corps",
            TargetAreaKey = "VirginiaCapitalCorridor",
            StartedDaySerial = 96,
            LastObservedEnemyStrength = 7000f,
            LastObservedFriendlyStrength = 7000f
        };
        input.CurrentEnemyStrength = 13000f;
        input.CurrentFriendlyStrength = 8000f;

        var output = OperationalProbeLedger.Build(input);

        AssertEqual(OperationalProbeDecision.Pause, output.Decision);
        AssertEqual("enemy-reaction", output.Reason);
        AssertEqual(false, output.RequiresMassCommitment);
    }

    private static void OperationalProbeEscalatesAfterFavorableContact()
    {
        var input = BuildProbeInput();
        input.DaySerial = 104;
        input.Previous = new OperationalProbeState
        {
            ProbeId = "1:VirginiaCapitalCorridor:probe-corps",
            UnitKey = "probe-corps",
            TargetAreaKey = "VirginiaCapitalCorridor",
            StartedDaySerial = 100,
            LastObservedEnemyStrength = 7000f,
            LastObservedFriendlyStrength = 7000f
        };
        input.CurrentEnemyStrength = 4000f;
        input.CurrentFriendlyStrength = 9000f;

        var output = OperationalProbeLedger.Build(input);

        AssertEqual(OperationalProbeDecision.Escalate, output.Decision);
        AssertEqual(true, output.RequiresMassCommitment);
    }

    // G1b runtime step: the surviving probe layer reads beliefs, whose strength is confidence-weighted, so a
    // decayed army reads small. The same fixture that escalates on a current picture holds on a stale one.
    private static void OperationalProbeStaleBeliefCannotEscalate()
    {
        var input = BuildProbeInput();
        input.DaySerial = 104;
        input.Previous = new OperationalProbeState
        {
            ProbeId = "1:VirginiaCapitalCorridor:probe-corps",
            UnitKey = "probe-corps",
            TargetAreaKey = "VirginiaCapitalCorridor",
            StartedDaySerial = 100,
            LastObservedEnemyStrength = 7000f,
            LastObservedFriendlyStrength = 7000f
        };
        input.CurrentEnemyStrength = 4000f;
        input.CurrentFriendlyStrength = 9000f;
        AssertEqual(OperationalProbeDecision.Escalate, OperationalProbeLedger.Build(input).Decision);

        input.BeliefConfidence01 = ContactEvidenceLedger.MinEscalationConfidence - 0.01f;
        var stale = OperationalProbeLedger.Build(input);
        AssertTrue(stale.Decision != OperationalProbeDecision.Escalate, "a stale belief must not escalate");
        AssertFalse(stale.RequiresMassCommitment, "and commits no mass");

        input.CurrentEnemyStrength = 100000f;
        input.BeliefConfidence01 = 0.1f;
        AssertEqual(OperationalProbeDecision.Withdraw, OperationalProbeLedger.Build(input).Decision);
    }

    private static void OperationalProbeRefusesCriticalHoldDonor()
    {
        var input = BuildProbeInput();
        input.FormationDirectives = FormationDirectiveLedger.Build(new[]
        {
            Snapshot("critical-army", 1, 16, 20000f, 18000f, FormationLevel.Army, FrontPosture.Hold)
        }, EraStage.Operational1862, "VirginiaCapitalCorridor");

        var output = OperationalProbeLedger.Build(input);

        AssertEqual(OperationalProbeDecision.None, output.Decision);
        AssertEqual("no-eligible-probe-formation", output.Reason);
    }

    private static void OperationalProbeOverlaysFormationDirective()
    {
        var input = BuildProbeInput();
        var output = OperationalProbeLedger.Build(input);

        bool changed = input.FormationDirectives.ApplyOperationalProbe(output);
        var assignment = input.FormationDirectives.GetAssignment("probe-corps");

        AssertEqual(true, changed);
        AssertEqual(FormationDirective.Probe, assignment.Directive);
        AssertEqual("limited-contact-probe", assignment.Reason);
        AssertEqual(false, assignment.TransferDonorAllowed);
    }

    private static void OperationalProbeEscalatesWithSupportPackage()
    {
        var input = BuildProbeInput();
        input.DaySerial = 104;
        input.Previous = new OperationalProbeState
        {
            ProbeId = "1:VirginiaCapitalCorridor:probe-corps",
            UnitKey = "probe-corps",
            TargetAreaKey = "VirginiaCapitalCorridor",
            SourceSectorKey = "VirginiaCapitalCorridor",
            StartedDaySerial = 100,
            LastObservedEnemyStrength = 7000f,
            LastObservedFriendlyStrength = 7000f
        };
        input.CurrentEnemyStrength = 10000f;
        input.CurrentFriendlyStrength = 9000f;
        input.ContactEvidence = ContactEvidence.FavorableContact;
        input.PackageOptions = CoordinatedOperationOptions.StableDefaults(10000f);

        var probe = ProbeSnapshot("probe-corps", 1, 15, 9000f, 4000f, FormationLevel.Division, FrontPosture.Counterstroke, "VirginiaCapitalCorridor");
        probe.StableUnitId = 111;
        probe.X = 10f;
        probe.Z = 10f;
        probe.Morale = 1f;
        probe.Readiness = 1f;
        probe.RifleAmmo = 1f;
        probe.ArtilleryAmmo = 1f;
        probe.Supply = 1f;
        var support = Snapshot("support-corps", 1, 15, 7000f, 2000f, FormationLevel.Division, FrontPosture.Counterstroke);
        support.StableUnitId = 222;
        support.AreaKey = "VirginiaCapitalCorridor";
        support.SectorKey = "VirginiaCapitalCorridor";
        support.X = 12f;
        support.Z = 10f;
        support.Morale = 1f;
        support.Readiness = 1f;
        support.RifleAmmo = 1f;
        support.ArtilleryAmmo = 1f;
        support.Supply = 1f;
        input.FormationDirectives = FormationDirectiveLedger.Build(new[] { probe, support }, EraStage.Operational1862, "VirginiaCapitalCorridor");

        var output = OperationalProbeLedger.Build(input);

        AssertEqual(OperationalProbeDecision.Escalate, output.Decision);
        AssertTrue(output.Package != null, "package output should be set");
        AssertEqual(CoordinatedOperationDecision.CoordinateAttack, output.Package.Decision);
    }

    private static void OperationalProbePackageEscalationRequiresFavorableEvidence()
    {
        var input = BuildProbeInput();
        input.DaySerial = 104;
        input.Previous = new OperationalProbeState
        {
            ProbeId = "1:VirginiaCapitalCorridor:probe-corps",
            UnitKey = "probe-corps",
            TargetAreaKey = "VirginiaCapitalCorridor",
            SourceSectorKey = "VirginiaCapitalCorridor",
            StartedDaySerial = 100,
            LastObservedEnemyStrength = 7000f,
            LastObservedFriendlyStrength = 7000f
        };
        input.CurrentEnemyStrength = 10000f;
        input.CurrentFriendlyStrength = 9000f;
        input.ContactEvidence = ContactEvidence.SkirmishObserved;
        input.PackageOptions = CoordinatedOperationOptions.StableDefaults(10000f);

        var probe = ProbeSnapshot("probe-corps", 1, 15, 9000f, 4000f, FormationLevel.Division, FrontPosture.Counterstroke, "VirginiaCapitalCorridor");
        probe.StableUnitId = 111;
        probe.X = 10f;
        probe.Z = 10f;
        probe.Morale = 1f;
        probe.Readiness = 1f;
        probe.RifleAmmo = 1f;
        probe.ArtilleryAmmo = 1f;
        probe.Supply = 1f;
        var support = Snapshot("support-corps", 1, 15, 7000f, 2000f, FormationLevel.Division, FrontPosture.Counterstroke);
        support.StableUnitId = 222;
        support.AreaKey = "VirginiaCapitalCorridor";
        support.SectorKey = "VirginiaCapitalCorridor";
        support.X = 12f;
        support.Z = 10f;
        support.Morale = 1f;
        support.Readiness = 1f;
        support.RifleAmmo = 1f;
        support.ArtilleryAmmo = 1f;
        support.Supply = 1f;
        input.FormationDirectives = FormationDirectiveLedger.Build(new[] { probe, support }, EraStage.Operational1862, "VirginiaCapitalCorridor");

        var output = OperationalProbeLedger.Build(input);

        AssertEqual(OperationalProbeDecision.Probe, output.Decision);
        AssertEqual("continue-probe", output.Reason);
    }

    private static void OperationalProbePackageOptionsUseLocalEnemyFallback()
    {
        var input = BuildProbeInput();
        input.CurrentEnemyStrength = -1f;
        input.Previous = new OperationalProbeState
        {
            ProbeId = "1:VirginiaCapitalCorridor:probe-corps",
            UnitKey = "probe-corps",
            TargetAreaKey = "VirginiaCapitalCorridor",
            SourceSectorKey = "VirginiaCapitalCorridor",
            StartedDaySerial = 100,
            LastObservedEnemyStrength = 7000f,
            LastObservedFriendlyStrength = 7000f
        };

        var probe = ProbeSnapshot("probe-corps", 1, 15, 9000f, 10000f, FormationLevel.Division, FrontPosture.Counterstroke, "VirginiaCapitalCorridor");
        probe.StableUnitId = 111;
        probe.X = 10f;
        probe.Z = 10f;
        probe.Morale = 1f;
        probe.Readiness = 1f;
        probe.RifleAmmo = 1f;
        probe.ArtilleryAmmo = 1f;
        probe.Supply = 1f;
        var support = Snapshot("support-corps", 1, 15, 7000f, 2000f, FormationLevel.Division, FrontPosture.Counterstroke);
        support.StableUnitId = 222;
        support.AreaKey = "VirginiaCapitalCorridor";
        support.SectorKey = "VirginiaCapitalCorridor";
        support.X = 12f;
        support.Z = 10f;
        support.Morale = 1f;
        support.Readiness = 1f;
        support.RifleAmmo = 1f;
        support.ArtilleryAmmo = 1f;
        support.Supply = 1f;
        input.FormationDirectives = FormationDirectiveLedger.Build(new[] { probe, support }, EraStage.Operational1862, "VirginiaCapitalCorridor");

        float desired = OperationalProbeLedger.ResolvePackageDesiredStrength(input);
        var options = CoordinatedOperationOptions.StableDefaults(desired);
        var output = CoordinatedOperationPackageLedger.Build(new CoordinatedOperationInput
        {
            AllianceId = 1,
            IsPlayerCic = false,
            Intent = CoordinatedOperationIntent.Attack,
            TargetName = "Manassas",
            TargetAreaKey = "VirginiaCapitalCorridor",
            TargetSectorKey = "VirginiaCapitalCorridor",
            TargetX = 10f,
            TargetZ = 10f,
            TargetEnemyStrength = desired,
            Options = options,
            Candidates = new List<CoordinatedOperationCandidate>
            {
                OpCandidate(111, "probe-corps", 10f, 10f, 9000f),
                OpCandidate(222, "support-corps", 12f, 10f, 7000f)
            }
        });

        AssertNear(10000f, desired, 0.0001f, "desired package strength");
        AssertTrue(
            output.Decision == CoordinatedOperationDecision.CoordinateAttack,
            "fallback package should coordinate attack, got " + output.Decision + " reason=" + output.Reason +
            " suppressed=" + string.Join(",", output.Suppressed.ConvertAll(s => s.DisplayUnitKey + ":" + s.Reason)));
    }

    private static void OperationalProbeSupportOverlayBlocksDonor()
    {
        var input = BuildProbeInput();
        var output = OperationalProbeLedger.Build(input);
        output.Package = new CoordinatedOperationOutput
        {
            Decision = CoordinatedOperationDecision.Reinforce,
            LeadDisplayUnitKey = "probe-corps",
            Reason = "reinforce-ratio-passed"
        };
        output.Package.SupportDisplayUnitKeys.Add("support-corps");

        var probe = ProbeSnapshot("probe-corps", 1, 15, 9000f, 4000f, FormationLevel.Division, FrontPosture.Counterstroke, "VirginiaCapitalCorridor");
        probe.StableUnitId = 111;
        var support = Snapshot("support-corps", 1, 15, 7000f, 2000f, FormationLevel.Division, FrontPosture.Counterstroke);
        support.StableUnitId = 222;
        input.FormationDirectives = FormationDirectiveLedger.Build(new[] { probe, support }, EraStage.Operational1862, "VirginiaCapitalCorridor");

        bool changed = input.FormationDirectives.ApplyOperationalProbe(output);
        var assignment = input.FormationDirectives.GetAssignment("support-corps");

        AssertEqual(true, changed);
        AssertEqual(false, assignment.TransferDonorAllowed);
        AssertEqual("probe-support:reinforce-ratio-passed", assignment.Reason);
    }

    private static void OperationalProbeStaysContinuingOnNoContactAfterMinimumDays()
    {
        var input = BuildProbeInput();
        input.DaySerial = 107;
        input.Previous = new OperationalProbeState
        {
            ProbeId = "1:VirginiaCapitalCorridor:probe-corps",
            UnitKey = "probe-corps",
            TargetAreaKey = "VirginiaCapitalCorridor",
            SourceSectorKey = "VirginiaCapitalCorridor",
            StartedDaySerial = 100,
            LastObservedEnemyStrength = 0f,
            LastObservedFriendlyStrength = 8000f
        };
        input.CurrentEnemyStrength = 0f;
        input.CurrentFriendlyStrength = 8000f;
        input.Options = new OperationalProbeOptions { MinimumProbeDays = 3, EscalateFriendlyRatio = 1.8f, WithdrawFriendlyRatio = 0.55f };
        input.ContactEvidence = ContactEvidence.NoContact;

        var output = OperationalProbeLedger.Build(input);
        AssertTrue(output.Decision != OperationalProbeDecision.Escalate, "no-contact must not escalate");
        AssertEqual(OperationalProbeDecision.Probe, output.Decision);
    }

    private static void OperationalProbeStateHasSingleSourceOnCoordinator()
    {
        // After Build returns, the output.State must be usable as Previous in a follow-up call.
        // This tests that OperationalProbeLedger's contract aligns with StrategicCoordinator's
        // single-source-of-truth pattern: the coordinator owns _operationalProbeStates[alliance],
        // and output.State references that slot.

        var input = BuildProbeInput();
        var first = OperationalProbeLedger.Build(input);
        AssertTrue(first.State != null, "fresh probe should publish a state");

        // Pass the same reference to the next call to confirm it's accepted as Previous.
        var second = OperationalProbeLedger.Build(new OperationalProbeInput
        {
            AllianceId = input.AllianceId,
            DaySerial = input.DaySerial + 1,
            PlanTargetAreaKey = input.PlanTargetAreaKey,
            Fronts = input.Fronts,
            FormationDirectives = input.FormationDirectives,
            Previous = first.State, // pass the same reference
            CurrentEnemyStrength = 1000f,
            CurrentFriendlyStrength = 4000f,
            Options = new OperationalProbeOptions()
        });
        AssertTrue(second.State != null, "continuing probe publishes state");
        AssertEqual(first.State.ProbeId, second.State.ProbeId);
    }

    private static void OperationalProbeCopiesObjectiveId()
    {
        var input = BuildProbeInput();
        input.ObjectiveId = 42;

        var output = OperationalProbeLedger.Build(input);

        AssertEqual(42, output.ObjectiveId);
    }

    private static CoordinatedOperationCandidate OpCandidate(
        int id,
        string key,
        float x,
        float z,
        float strength,
        CoordinatedCommitMode commit = CoordinatedCommitMode.DirectMovement,
        string sector = "VirginiaCapitalCorridor",
        string area = "VirginiaCapitalCorridor")
    {
        return new CoordinatedOperationCandidate
        {
            StableUnitId = id,
            DisplayUnitKey = key,
            AllianceId = 1,
            Level = FormationLevel.Corps,
            Directive = FormationDirective.Counterstroke,
            AreaKey = area,
            SectorKey = sector,
            X = x,
            Z = z,
            CombatAvailability = strength,
            ExchangePressure = strength,
            Readiness = 0.8f,
            Morale = 0.8f,
            Ammo = 0.8f,
            Supply = 0.8f,
            OffensiveAllowed = true,
            DefensiveAllowed = true,
            DirectMovementAllowed = true,
            CommitMode = commit,
            FrontPosture = FrontPosture.Counterstroke
        };
    }

    private static CoordinatedOperationInput OpInput(params CoordinatedOperationCandidate[] candidates)
    {
        return new CoordinatedOperationInput
        {
            AllianceId = 1,
            IsPlayerCic = false,
            Intent = CoordinatedOperationIntent.Attack,
            TargetName = "Manassas",
            TargetAreaKey = "VirginiaCapitalCorridor",
            TargetSectorKey = "VirginiaCapitalCorridor",
            TargetX = 0f,
            TargetZ = 0f,
            TargetEnemyStrength = 10000f,
            Options = CoordinatedOperationOptions.StableDefaults(10000f),
            Candidates = new List<CoordinatedOperationCandidate>(candidates)
        };
    }

    private static void CoordinatedOpsAttackSelectsLocalSupport()
    {
        var output = CoordinatedOperationPackageLedger.Build(OpInput(
            OpCandidate(10, "lead", 0f, 0f, 8000f),
            OpCandidate(20, "support", 5f, 0f, 6000f),
            OpCandidate(30, "remote", 500f, 0f, 8000f, CoordinatedCommitMode.DirectMovement, "RemoteSector", "RemoteArea")));

        AssertEqual(CoordinatedOperationDecision.CoordinateAttack, output.Decision);
        AssertEqual(10, output.LeadStableUnitId);
        AssertEqual(1, output.SupportStableUnitIds.Count);
        AssertEqual(20, output.SupportStableUnitIds[0]);
        AssertTrue(output.Ratio >= 1.25f, "attack ratio should pass");
    }

    private static void CoordinatedOpsBlockedWlSupportDoesNotFakeAttack()
    {
        var output = CoordinatedOperationPackageLedger.Build(OpInput(
            OpCandidate(10, "lead", 0f, 0f, 9000f),
            OpCandidate(20, "blocked", 5f, 0f, 6000f, CoordinatedCommitMode.BlockedWlPlayerChain)));

        AssertTrue(output.Decision != CoordinatedOperationDecision.CoordinateAttack, "blocked support must not count");
        AssertEqual(CoordinatedOperationDecision.SingleLead, output.Decision);
        AssertEqual(1, output.Suppressed.Count);
        AssertEqual("blocked-commit-mode", output.Suppressed[0].Reason);
    }

    private static void CoordinatedOpsLeadSelectionRejectsRemoteOversizedCandidate()
    {
        var output = CoordinatedOperationPackageLedger.Build(OpInput(
            OpCandidate(30, "remote", 500f, 0f, 20000f, CoordinatedCommitMode.DirectMovement, "Remote", "Remote"),
            OpCandidate(10, "local", 0f, 0f, 9000f)));

        AssertEqual(10, output.LeadStableUnitId);
        var remote = output.Suppressed.Find(s => s.StableUnitId == 30);
        AssertTrue(remote != null, "remote candidate should be suppressed");
        AssertTrue(remote.Reason == "remote-tier-blocked" || remote.Reason == "outside-range",
            "remote candidate should be blocked by range semantics");
    }

    private static void CoordinatedOpsLeadOvermatchStaysSingleLead()
    {
        var output = CoordinatedOperationPackageLedger.Build(OpInput(
            OpCandidate(10, "lead", 0f, 0f, 14000f),
            OpCandidate(20, "support", 5f, 0f, 4000f)));

        AssertEqual(CoordinatedOperationDecision.SingleLead, output.Decision);
        AssertEqual(0, output.SupportStableUnitIds.Count);
        var support = output.Suppressed.Find(s => s.StableUnitId == 20);
        AssertTrue(support != null, "support should be suppressed");
        AssertTrue(support.Reason == "overmatch" || support.Reason == "lead-overmatch",
            "support should be blocked by lead overmatch");
    }

    private static void CoordinatedOpsReinforceUsesDefensiveEligibility()
    {
        var lead = OpCandidate(10, "lead", 0f, 0f, 6000f);
        var support = OpCandidate(20, "support", 5f, 0f, 5000f);
        lead.OffensiveAllowed = false;
        support.OffensiveAllowed = false;
        support.TransferDonorAllowed = true;
        var input = OpInput(lead, support);
        input.Intent = CoordinatedOperationIntent.Reinforce;
        input.TargetEnemyStrength = 12000f;

        var output = CoordinatedOperationPackageLedger.Build(input);

        AssertEqual(CoordinatedOperationDecision.Reinforce, output.Decision);
        AssertEqual(10, output.LeadStableUnitId);
        AssertEqual(1, output.SupportStableUnitIds.Count);
        AssertEqual(20, output.SupportStableUnitIds[0]);
        AssertTrue(output.Ratio >= input.Options.RequiredReinforceRatio, "reinforce ratio should pass");
        AssertTrue(output.Ratio < input.Options.RequiredAttackRatio, "attack ratio should not pass");
    }

    private static void CoordinatedOpsReinforceBlocksNonDonorSupport()
    {
        var lead = OpCandidate(10, "lead", 0f, 0f, 6000f);
        var support = OpCandidate(20, "support", 5f, 0f, 5000f);
        lead.OffensiveAllowed = false;
        support.OffensiveAllowed = false;
        support.DefensiveAllowed = true;
        support.TransferDonorAllowed = false;
        var input = OpInput(lead, support);
        input.Intent = CoordinatedOperationIntent.Reinforce;
        input.TargetEnemyStrength = 12000f;

        var output = CoordinatedOperationPackageLedger.Build(input);

        AssertTrue(output.Decision != CoordinatedOperationDecision.Reinforce,
            "non donor support must not create reinforce package");
        AssertEqual(CoordinatedOperationDecision.Delay, output.Decision); // 0.5 ratio lone lead: below RequiredReinforceRatio (C-CoordOff)
        AssertEqual(0, output.SupportStableUnitIds.Count);
        var blocked = output.Suppressed.Find(s => s.StableUnitId == 20);
        AssertTrue(blocked != null, "non donor support should be suppressed");
        AssertEqual("transfer-donor-blocked", blocked.Reason);
    }

    private static void CoordinatedOpsWlCurrentOrderDoesNotRequireDirectMovement()
    {
        var support = OpCandidate(20, "wl-support", 5f, 0f, 5000f, CoordinatedCommitMode.WlCurrentOrder);
        support.DirectMovementAllowed = false;
        var blocked = OpCandidate(30, "blocked", 6f, 0f, 6000f, CoordinatedCommitMode.BlockedWlPlayerChain);
        var output = CoordinatedOperationPackageLedger.Build(OpInput(
            OpCandidate(10, "lead", 0f, 0f, 9000f),
            support,
            blocked));

        AssertEqual(CoordinatedOperationDecision.CoordinateAttack, output.Decision);
        AssertEqual(1, output.SupportStableUnitIds.Count);
        AssertEqual(20, output.SupportStableUnitIds[0]);
        var blockedSuppression = output.Suppressed.Find(s => s.StableUnitId == 30);
        AssertTrue(blockedSuppression != null, "blocked W&L player-chain candidate should be suppressed");
        AssertEqual("blocked-commit-mode", blockedSuppression.Reason);
    }

    private static void CoordinatedOpsBridgeDecisionMapsBlockedCommitMode()
    {
        var blocked = new WlStrategicOrderDecision(
            WlStrategicOrderResult.WlCurrentOrderIneligible,
            16,
            mayDirectMove: false,
            mayMutateOperationList: false,
            reason: "chain");
        var issued = new WlStrategicOrderDecision(
            WlStrategicOrderResult.IssuedWlCurrentOrder,
            16,
            mayDirectMove: false,
            mayMutateOperationList: false,
            reason: "issued");
        var direct = new WlStrategicOrderDecision(
            WlStrategicOrderResult.DirectMovementAllowed,
            16,
            mayDirectMove: true,
            mayMutateOperationList: true,
            reason: "direct");

        AssertEqual(CoordinatedCommitMode.BlockedWlPlayerChain, CoordinatedOperationRuntime.CommitModeFromBridge(blocked));
        AssertEqual(CoordinatedCommitMode.WlCurrentOrder, CoordinatedOperationRuntime.CommitModeFromBridge(issued));
        AssertEqual(CoordinatedCommitMode.DirectMovement, CoordinatedOperationRuntime.CommitModeFromBridge(direct));
    }

    private static void CoordinatedOpsNearestMapNameResolvesTarget()
    {
        var map = CampaignMapLedger.Build(new[]
        {
            new CampaignMapTown { CityName = "Richmond", X = 100f, Z = 100f },
            new CampaignMapTown { CityName = "Manassas", X = 10f, Z = 0f }
        });

        string name = CoordinatedOperationRuntime.NearestMapName(map, new UnityEngine.Vector3(11f, 0f, 0f));

        AssertEqual("Manassas", name);
    }

    private static void CoordinatedOpsTargetNameFallsBackToAreaKey()
    {
        string name = CoordinatedOperationRuntime.ResolveTargetName(
            -1,
            "VirginiaCapitalCorridor",
            null,
            new UnityEngine.Vector3(11f, 0f, 0f));

        AssertEqual("VirginiaCapitalCorridor", name);
    }

    private static void CoordinatedOpsEmptyTargetIsSingleLead()
    {
        var input = OpInput(
            OpCandidate(10, "lead", 0f, 0f, 9000f),
            OpCandidate(20, "support", 5f, 0f, 9000f));
        input.Intent = CoordinatedOperationIntent.Probe;
        input.TargetEnemyStrength = 0f;

        var output = CoordinatedOperationPackageLedger.Build(input);

        AssertEqual(CoordinatedOperationDecision.SingleLead, output.Decision);
        AssertEqual(0, output.SupportStableUnitIds.Count);
    }

    private static void CoordinatedOpsHighRiskTightensDonorCaps()
    {
        var options = CoordinatedOperationOptions.FromDirector(10000f, new DirectorPosture
        {
            Pace = CampaignPace.Overheated,
            Risk = CollapseRisk.Critical
        });
        var input = OpInput(
            OpCandidate(10, "lead", 0f, 0f, 9000f),
            OpCandidate(20, "support-a", 5f, 0f, 4000f),
            OpCandidate(30, "support-b", 6f, 0f, 4000f));
        input.Options = options;

        var output = CoordinatedOperationPackageLedger.Build(input);

        AssertTrue(output.SupportStableUnitIds.Count <= 1, "high risk caps support units to one");
    }

    private static void CoordinatedOpsPlayerCicReturnsNone()
    {
        var input = OpInput(OpCandidate(10, "lead", 0f, 0f, 20000f));
        input.IsPlayerCic = true;

        var output = CoordinatedOperationPackageLedger.Build(input);

        AssertEqual(CoordinatedOperationDecision.None, output.Decision);
        AssertEqual("player-cic", output.Reason);
    }

    private static void CoordinatedOpsRefusesLiveOperationListCandidates()
    {
        var inOps = OpCandidate(10, "in-ops", 0f, 0f, 20000f);
        inOps.InOffensiveOperation = true;

        var output = CoordinatedOperationPackageLedger.Build(OpInput(inOps));

        AssertEqual(CoordinatedOperationDecision.None, output.Decision);
        AssertEqual("no-eligible-lead", output.Reason);
    }

    private static void CoordinatedOpsDeterministicTieByStableId()
    {
        var output = CoordinatedOperationPackageLedger.Build(OpInput(
            OpCandidate(30, "higher", 0f, 0f, 9000f),
            OpCandidate(10, "lower", 0f, 0f, 9000f)));

        AssertEqual(10, output.LeadStableUnitId);
    }

    private static void OperationalTempoChapterOneDelaysEscalation()
    {
        var input = BuildProbeInput();
        input.DaySerial = 103;
        input.Options = OperationalTempoDoctrine.For(
            allianceId: 0,
            era: EraStage.Amateur1861,
            policyChapter: 1,
            campaignMonth: 7,
            personality: new PersonalityVector());
        input.Previous = new OperationalProbeState
        {
            ProbeId = "0:VirginiaCapitalCorridor:probe-corps",
            UnitKey = "probe-corps",
            TargetAreaKey = "VirginiaCapitalCorridor",
            StartedDaySerial = 100,
            LastObservedEnemyStrength = 7000f,
            LastObservedFriendlyStrength = 7000f
        };
        input.CurrentEnemyStrength = 4000f;
        input.CurrentFriendlyStrength = 8500f;

        var output = OperationalProbeLedger.Build(input);

        AssertEqual(OperationalProbeDecision.Probe, output.Decision);
        AssertEqual("continue-probe", output.Reason);
    }

    private static void OperationalTempoLateUnionSustainsPressure()
    {
        var input = BuildProbeInput();
        input.DaySerial = 202;
        input.Options = OperationalTempoDoctrine.For(
            allianceId: 0,
            era: EraStage.TotalWar1864,
            policyChapter: 3,
            campaignMonth: 6,
            personality: new PersonalityVector());
        input.Previous = new OperationalProbeState
        {
            ProbeId = "0:VirginiaCapitalCorridor:probe-corps",
            UnitKey = "probe-corps",
            TargetAreaKey = "VirginiaCapitalCorridor",
            StartedDaySerial = 200,
            LastObservedEnemyStrength = 7000f,
            LastObservedFriendlyStrength = 7000f
        };
        input.CurrentEnemyStrength = 5000f;
        input.CurrentFriendlyStrength = 8500f;

        var output = OperationalProbeLedger.Build(input);

        AssertEqual(OperationalProbeDecision.Escalate, output.Decision);
        AssertEqual(true, output.RequiresMassCommitment);
    }

    private static void OperationalTempoWinterSlowsProbes()
    {
        var summer = OperationalTempoDoctrine.For(
            allianceId: 0,
            era: EraStage.Operational1862,
            policyChapter: 2,
            campaignMonth: 7,
            personality: new PersonalityVector());
        var winter = OperationalTempoDoctrine.For(
            allianceId: 0,
            era: EraStage.Operational1862,
            policyChapter: 2,
            campaignMonth: 1,
            personality: new PersonalityVector());

        AssertTrue(winter.MinimumProbeDays > summer.MinimumProbeDays,
            "winter should require a longer probe/operation pause");
        AssertTrue(winter.MaximumProbeStrengthFraction < summer.MaximumProbeStrengthFraction,
            "winter should limit probe size");
    }

    private static void OperationalTempoLateCsaMoreConservativeThanUnion()
    {
        var union = OperationalTempoDoctrine.For(
            allianceId: 0,
            era: EraStage.TotalWar1864,
            policyChapter: 3,
            campaignMonth: 6,
            personality: new PersonalityVector());
        var csa = OperationalTempoDoctrine.For(
            allianceId: 1,
            era: EraStage.TotalWar1864,
            policyChapter: 3,
            campaignMonth: 6,
            personality: new PersonalityVector());

        AssertTrue(csa.EscalateFriendlyRatio > union.EscalateFriendlyRatio,
            "late CSA should require better odds to escalate");
        AssertTrue(csa.MaximumProbeStrengthFraction < union.MaximumProbeStrengthFraction,
            "late CSA should commit smaller probes than Union");
    }

    private static void StrategicMovementBudgetBlocksAreaExportFromHoldSector()
    {
        var front = FrontSectorLedger.Build(new[]
        {
            new FrontSectorInput
            {
                SectorKey = "WashingtonDefenses",
                Theater = Theater.East,
                OwnStrength = 7000f,
                EnemyStrength = 9000f,
                StrategicImportance = 1.0f,
                IsCritical = true,
                AverageMorale = 0.8f,
                AverageReadiness = 0.8f,
                AverageSupply = 0.8f
            },
            new FrontSectorInput
            {
                SectorKey = "OhioValley",
                Theater = Theater.West,
                OwnStrength = 12000f,
                EnemyStrength = 6000f,
                StrategicImportance = 0.5f,
                IsCritical = false
            }
        });

        var decision = StrategicMovementBudget.EvaluateAreaMovement(
            front,
            "WashingtonDefenses",
            "OhioValley",
            4000f);

        AssertTrue(decision != null && !decision.Allowed, "expected area movement to be blocked");
        AssertEqual("min-hold", decision.Reason);
    }

    // -----------------------------------------------------------------------
    // PhaseTruthLedger tests
    // -----------------------------------------------------------------------

    private static void PhaseTruthAdvancesWhenTargetAccomplished()
    {
        var input = new PhaseTruthInput
        {
            Plan = new OperationalPlan { Phases = { new Phase { TargetObjectiveId = 29, DeadlineMonth = 12, DeadlineYear = 1862 } } },
            TargetAccomplished = true,
            ObjectiveAvailable = true,
            TargetLocalOwnStrength = 10000f,
            TargetLocalEnemyStrength = 5000f,
            CurrentMonth = 6, CurrentYear = 1862
        };
        var output = PhaseTruthLedger.Evaluate(input);
        AssertEqual(PhaseTruthVerdict.TargetAccomplished, output.Verdict);
        AssertEqual(PhaseTruthAction.Advance, output.RecommendedAction);
    }

    private static void PhaseTruthReplansWhenObjectiveUnavailable()
    {
        var input = new PhaseTruthInput
        {
            Plan = new OperationalPlan { Phases = { new Phase { TargetObjectiveId = 29, DeadlineMonth = 12, DeadlineYear = 1862 } } },
            TargetAccomplished = false,
            ObjectiveAvailable = false,
            TargetLocalOwnStrength = 10000f,
            TargetLocalEnemyStrength = 5000f,
            CurrentMonth = 6, CurrentYear = 1862
        };
        var output = PhaseTruthLedger.Evaluate(input);
        AssertEqual(PhaseTruthVerdict.ObjectiveUnavailable, output.Verdict);
        AssertEqual(PhaseTruthAction.Replan, output.RecommendedAction);
    }

    // R3: force is a LOCAL own:enemy ratio (0.5 + ForceFractionRequired) and is only
    // checked while the phase deadline has not expired (see CampaignLifecycleFixTests).
    private static void PhaseTruthRecoversWhenForceBelowThreshold()
    {
        var input = new PhaseTruthInput
        {
            Plan = new OperationalPlan { Phases = { new Phase { TargetObjectiveId = 29, ForceFractionRequired = 0.5f, DeadlineMonth = 12, DeadlineYear = 1862 } } },
            TargetAccomplished = false,
            ObjectiveAvailable = true,
            TargetLocalOwnStrength = 1000f,
            TargetLocalEnemyStrength = 5000f,
            CurrentMonth = 6, CurrentYear = 1862
        };
        var output = PhaseTruthLedger.Evaluate(input);
        AssertEqual(PhaseTruthVerdict.ForceBelowThreshold, output.Verdict);
        AssertEqual(PhaseTruthAction.Recover, output.RecommendedAction);
    }

    private static void PhaseTruthDeadlineExpiredAdvancesOrReplans()
    {
        var input = new PhaseTruthInput
        {
            Plan = new OperationalPlan { Phases = { new Phase { TargetObjectiveId = 29, DeadlineMonth = 1, DeadlineYear = 1862 } } },
            TargetAccomplished = false,
            ObjectiveAvailable = true,
            TargetLocalOwnStrength = 10000f,
            TargetLocalEnemyStrength = 5000f,
            CurrentMonth = 6, CurrentYear = 1862
        };
        var output = PhaseTruthLedger.Evaluate(input);
        AssertEqual(PhaseTruthVerdict.DeadlineExpired, output.Verdict);
        AssertTrue(output.RecommendedAction == PhaseTruthAction.Advance ||
                   output.RecommendedAction == PhaseTruthAction.Replan,
                   "deadline expired should advance or replan");
    }

    private static void PhaseTruthNoContactStaysContinue()
    {
        var input = new PhaseTruthInput
        {
            Plan = new OperationalPlan { Phases = { new Phase { TargetObjectiveId = 29, DeadlineMonth = 12, DeadlineYear = 1862 } } },
            TargetAccomplished = false,
            ObjectiveAvailable = true,
            TargetLocalOwnStrength = 10000f,
            TargetLocalEnemyStrength = 5000f,
            TargetEngagedRecently = false,
            CurrentMonth = 6, CurrentYear = 1862
        };
        var output = PhaseTruthLedger.Evaluate(input);
        AssertEqual(PhaseTruthVerdict.Valid, output.Verdict);
        AssertEqual(PhaseTruthAction.Continue, output.RecommendedAction);
    }

    // Task 13: CicReviewRouter.Route routes Advance → AdvancePhase, which exhausts the
    // last phase and marks the plan dirty. Single-phase plan: after accomplished truth,
    // IsDirty=true, return false (signals replan to caller).
    // CIC.ReviewPlanWithTruth delegates to CicReviewRouter, so this exercises the real routing
    // logic without requiring BepInEx/HarmonyLib in the test harness.
    private static void CicReviewPlanReplansWhenPhaseTruthSaysAccomplished()
    {
        var plan = new OperationalPlan
        {
            CurrentPhaseIndex = 0,
            PlanDeadlineMonth = 12,
            PlanDeadlineYear = 1862,
            IsDirty = false
        };
        plan.Phases.Add(new Phase
        {
            TargetObjectiveId = 42,
            ForceFractionRequired = 0.5f,
            Transition = PhaseTransition.TargetTaken,
            DeadlineMonth = 12,
            DeadlineYear = 1862
        });

        // Accomplished → PhaseTruthAction.Advance → AdvancePhase (no next phase) → IsDirty=true, false
        var truth = new PhaseTruthOutput
        {
            Verdict = PhaseTruthVerdict.TargetAccomplished,
            RecommendedAction = PhaseTruthAction.Advance,
            Reason = "target-accomplished"
        };

        bool result = CicReviewRouter.Route(plan, truth, 1862 * 372 + 6 * 31 + 1, 6, 1862) == CicRouteOutcome.KeepPlan;
        AssertTrue(!result, "Route should not keep the plan when last phase is exhausted by Advance");
        AssertTrue(plan.IsDirty, "plan.IsDirty should be true after last phase exhausted by Advance");
    }
}
