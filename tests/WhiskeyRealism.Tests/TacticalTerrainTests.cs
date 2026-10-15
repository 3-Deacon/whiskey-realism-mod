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

internal static class TacticalTerrainTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("tactical deployment telemetry summarizes large moves", TacticalDeploymentTelemetrySummarizesLargeMoves),
            ("tactical deployment telemetry tracks new and removed groups", TacticalDeploymentTelemetryTracksNewAndRemovedGroups),
            ("tactical deployment telemetry matches stable keys across reorder", TacticalDeploymentTelemetryMatchesStableKeysAcrossReorder),
            ("tactical deployment telemetry formats skipped phase", TacticalDeploymentTelemetryFormatsSkippedPhase),
            ("tactical deployment snapshot carries terrain facing evidence", TacticalDeploymentSnapshotCarriesTerrainFacingEvidence),
            ("tactical terrain telemetry formats bounded row", TacticalTerrainTelemetryFormatsBoundedRow),
            ("tactical terrain telemetry sanitizes unsafe tokens", TacticalTerrainTelemetrySanitizesUnsafeTokens),
            ("tactical map knowledge telemetry formats inventory row", TacticalMapKnowledgeTelemetryFormatsInventoryRow),
            ("tactical map knowledge telemetry formats unit context row", TacticalMapKnowledgeTelemetryFormatsUnitContextRow),
            ("tactical terrain rejects water center", TacticalTerrainRejectsWaterCenter),
            ("tactical terrain rejects water footprint", TacticalTerrainRejectsWaterFootprint),
            ("tactical terrain rejects outside deployment zone", TacticalTerrainRejectsOutsideDeploymentZone),
            ("tactical terrain rejects footprint outside deployment zone", TacticalTerrainRejectsFootprintOutsideDeploymentZone),
            ("tactical terrain picks closest safe candidate", TacticalTerrainPicksClosestSafeCandidate),
            ("tactical terrain prefers visible enemy facing", TacticalTerrainPrefersVisibleEnemyFacing),
            ("tactical terrain no safe candidate keeps vanilla", TacticalTerrainNoSafeCandidateKeepsVanilla),
            ("tactical terrain missing visible enemy rejects when required", TacticalTerrainMissingVisibleEnemyRejectsWhenRequired),
            ("tactical terrain rejects nonfinite vanilla baseline", TacticalTerrainRejectsNonfiniteVanillaBaseline),
            ("tactical terrain rejects nonfinite candidate", TacticalTerrainRejectsNonfiniteCandidate),
            ("tactical terrain normalizes large positive angles", TacticalTerrainNormalizesLargePositiveAngles),
            ("tactical terrain normalizes large negative angles", TacticalTerrainNormalizesLargeNegativeAngles),
            ("tactical terrain rejects unknown terrain evidence", TacticalTerrainRejectsUnknownTerrainEvidence),
            ("tactical terrain preserves vanilla facing without visible enemy", TacticalTerrainPreservesVanillaFacingWithoutVisibleEnemy),
            ("tactical battlefront geometry detects wide unsupported gap", TacticalBattlefrontGeometryDetectsWideUnsupportedGap),
            ("tactical approach avenue planner prefers enemy entry route", TacticalApproachAvenuePlannerPrefersEnemyEntryRoute),
            ("tactical vision runtime adapter fallback objective uses visible enemy point", TacticalVisionRuntimeAdapterFallbackObjectiveUsesVisibleEnemyPoint),
            ("tactical vision fallback objective uses vanilla movement waypoint", TacticalVisionFallbackObjectiveUsesVanillaMovementWaypoint),
            ("tactical vision appends visible enemy line to objective chain picture", TacticalVisionAppendsVisibleEnemyLineToObjectiveChainPicture),
            ("tactical vision fallback objective carries committed attack confidence", TacticalVisionFallbackObjectiveCarriesCommittedAttackConfidence),
            ("tactical side log context marks player command without faction inference", TacticalSideLogContextMarksPlayerCommandWithoutFactionInference),
            ("tactical nav planner probes to standoff bound", TacticalNavPlannerProbesToStandoffBound),
            ("tactical nav planner breaks off close screen", TacticalNavPlannerBreaksOffCloseScreen),
            ("tactical nav planner offsets covered attack approach", TacticalNavPlannerOffsetsCoveredAttackApproach),
            ("tactical nav planner prefers road cover and avoids choke congestion", TacticalNavPlannerPrefersRoadCoverAndAvoidsChokeCongestion),
            ("tactical nav planner penalizes friendly front blockers", TacticalNavPlannerPenalizesFriendlyFrontBlockers),
            ("tactical nav planner preserves corridor continuity", TacticalNavPlannerPreservesCorridorContinuity),
            ("tactical nav planner avoids reserved bridge choke", TacticalNavPlannerAvoidsReservedBridgeChoke),
            ("tactical nav planner fallback avoids contested attack lane", TacticalNavPlannerFallbackAvoidsContestedAttackLane),
            ("tactical facing pulse observes division axis without write", TacticalFacingPulseObservesDivisionAxisWithoutWrite),
            ("tactical facing pulse corrects brigade frontage", TacticalFacingPulseCorrectsBrigadeFrontage),
            ("tactical facing pulse urgently reforms regiment under visible contact", TacticalFacingPulseUrgentlyReformsRegimentUnderVisibleContact),
            ("tactical facing pulse defers pending regiment", TacticalFacingPulseDefersPendingRegiment),
            ("tactical sector no measured enemy is not weak point", TacticalSectorNoMeasuredEnemyIsNotWeakPoint),
            ("tactical sector tiny angle contact is not weak point", TacticalSectorTinyAngleContactIsNotWeakPoint),
            ("tactical sector substantial contact remains weak point", TacticalSectorSubstantialContactRemainsWeakPoint),
            ("tactical hq link guard clears cross command auto link", TacticalHqLinkGuardClearsCrossCommandAutoLink),
            ("tactical hq link guard preserves valid command links", TacticalHqLinkGuardPreservesValidCommandLinks),
            ("tactical quadrant threat computes arcs", TacticalQuadrantThreatScorerComputesArcs),
            ("tactical quadrant threat detects rear pressure", TacticalQuadrantThreatScorerDetectsRearPressure),
            ("tactical quadrant threat null slices degrades gracefully", TacticalQuadrantThreatScorerNullSlicesDegradesGracefully),
            ("tactical refuse flank intent no refuse when balanced", TacticalRefuseFlankIntentNoRefuseWhenBalanced),
            ("tactical refuse flank intent refuse left when left threatened", TacticalRefuseFlankIntentRefuseLeftWhenLeftThreatened),
            ("tactical refuse flank intent refuse right when right threatened", TacticalRefuseFlankIntentRefuseRightWhenRightThreatened),
            ("tactical refuse flank intent no refuse on offensive posture", TacticalRefuseFlankIntentNoRefuseOnOffensivePosture),
        });
    }

    private static void TacticalDeploymentTelemetrySummarizesLargeMoves()
    {
        var before = new TacticalDeploymentSnapshot("pre", 0, 0, 0, new[]
        {
            new TacticalDeploymentGroupSnapshot("army", "Army", 0, 16, 100f, 100f, 1, 1, 0, false, true),
            new TacticalDeploymentGroupSnapshot("division", "Division", 0, 15, 300f, 100f, 1, 1, 0, false, true)
        });
        var after = new TacticalDeploymentSnapshot("post", 0, 0, 0, new[]
        {
            new TacticalDeploymentGroupSnapshot("army", "Army", 0, 16, 250f, 100f, 1, 1, 0, false, true),
            new TacticalDeploymentGroupSnapshot("division", "Division", 0, 15, 310f, 100f, 1, 1, 0, false, true)
        });

        var summary = TacticalDeploymentTelemetry.Delta("DoPlacementAIUnitsWithinDeploymentzoneNew", before, after);

        AssertEqual(2, summary.MatchedGroups, "matched groups");
        AssertEqual(2, summary.MovedGroups, "moved groups");
        AssertEqual(1, summary.LargeMoves, "large moves");
        AssertNear(150f, summary.MaxMoveDistance, 0.01f, "max move");
        AssertNear(80f, summary.AverageMoveDistance, 0.01f, "average move");
        AssertContains(TacticalDeploymentTelemetry.FormatSummary(summary), "[TacDeployObs]", "summary prefix");
        AssertContains(TacticalDeploymentTelemetry.FormatSummary(summary), "largeMoves=1", "large move field");
    }

    private static void TacticalDeploymentTelemetryTracksNewAndRemovedGroups()
    {
        var before = new TacticalDeploymentSnapshot("pre", 1, 5, 2, new[]
        {
            new TacticalDeploymentGroupSnapshot("kept", "Kept", 1, 14, 0f, 0f, 1, 1, 0, false, true),
            new TacticalDeploymentGroupSnapshot("removed", "Removed", 1, 14, 10f, 0f, 1, 1, 0, false, true)
        });
        var after = new TacticalDeploymentSnapshot("post", 1, 5, 2, new[]
        {
            new TacticalDeploymentGroupSnapshot("kept", "Kept", 1, 14, 0f, 0f, 1, 1, 0, false, true),
            new TacticalDeploymentGroupSnapshot("new", "New", 1, 14, 40f, 0f, 1, 1, 0, false, true)
        });

        var summary = TacticalDeploymentTelemetry.Delta("SetActiveDeploymentPhase", before, after);
        string signature = TacticalDeploymentTelemetry.Signature(summary);

        AssertEqual(1, summary.NewGroups, "new groups");
        AssertEqual(1, summary.RemovedGroups, "removed groups");
        AssertContains(signature, "surface=SetActiveDeploymentPhase", "surface signature");
        AssertContains(signature, "phase=eod", "phase signature");
        AssertContains(signature, "new=1", "new signature");
        AssertContains(signature, "removed=1", "removed signature");
    }

    private static void TacticalDeploymentTelemetryMatchesStableKeysAcrossReorder()
    {
        var before = new TacticalDeploymentSnapshot("pre", 0, 0, 0, new[]
        {
            new TacticalDeploymentGroupSnapshot("101", "Army", 0, 16, 10f, 10f, 1, 1, 0, false, true),
            new TacticalDeploymentGroupSnapshot("202", "Division", 0, 15, 20f, 20f, 1, 1, 0, false, true)
        }, TacticalDeploymentTelemetry.PhaseInitialPositioning);

        var after = new TacticalDeploymentSnapshot("post", 0, 0, 0, new[]
        {
            new TacticalDeploymentGroupSnapshot("202", "Division", 0, 15, 20f, 20f, 1, 1, 0, false, true),
            new TacticalDeploymentGroupSnapshot("101", "Army", 0, 16, 110f, 10f, 1, 1, 0, false, true)
        }, TacticalDeploymentTelemetry.PhaseInitialPositioning);

        var summary = TacticalDeploymentTelemetry.Delta("DoUnitPositioning", before, after);

        AssertEqual(2, summary.MatchedGroups, "reorder matched groups");
        AssertEqual(0, summary.NewGroups, "reorder new groups");
        AssertEqual(0, summary.RemovedGroups, "reorder removed groups");
        AssertEqual(1, summary.MovedGroups, "reorder moved groups");
        AssertEqual(1, summary.LargeMoves, "reorder large moves");
        AssertContains(TacticalDeploymentTelemetry.Signature(summary), "phase=initial-positioning", "initial positioning phase");
    }

    private static void TacticalDeploymentTelemetryFormatsSkippedPhase()
    {
        var before = new TacticalDeploymentSnapshot("pre", 1, 0, 0, new[]
        {
            new TacticalDeploymentGroupSnapshot("303", "Skipped", 1, 14, 50f, 50f, 1, 1, 0, false, true)
        }, TacticalDeploymentTelemetry.PhaseSkipped);

        var after = new TacticalDeploymentSnapshot("post", 1, 0, 0, new[]
        {
            new TacticalDeploymentGroupSnapshot("303", "Skipped", 1, 14, 50f, 50f, 1, 1, 0, false, true)
        }, TacticalDeploymentTelemetry.PhaseSkipped);

        var summary = TacticalDeploymentTelemetry.Delta("DoPlacementAIUnitsWithinDeploymentzoneNew", before, after);
        string formatted = TacticalDeploymentTelemetry.FormatSummary(summary);

        AssertContains(formatted, "[TacDeployObs]", "summary marker");
        AssertContains(formatted, "surface=DoPlacementAIUnitsWithinDeploymentzoneNew", "surface");
        AssertContains(formatted, "phase=skipped", "skipped phase");
        AssertContains(formatted, "matched=1", "matched count");
        AssertContains(formatted, "moved=0", "skipped move count");
    }

    private static void TacticalDeploymentSnapshotCarriesTerrainFacingEvidence()
    {
        var group = new TacticalDeploymentGroupSnapshot(
            "key",
            "Unit Name",
            1,
            15,
            10f,
            20f,
            1,
            1,
            0,
            routed: false,
            active: true,
            terrainId: 4,
            centerWater: true,
            footprintWater: false,
            insideDeploymentZone: false,
            facing: 180f,
            nearestVisibleEnemyBearing: 175f,
            nearestVisibleEnemyDistance: 500f);

        AssertEqual(4, group.TerrainId, "terrain id");
        AssertTrue(group.CenterWater, "center water");
        AssertFalse(group.InsideDeploymentZone, "zone");
        AssertTrue(group.HasTerrainEvidence, "terrain evidence");
        AssertTrue(group.HasVisibleEnemyBearing, "enemy bearing evidence");
        AssertNear(180f, group.Facing, 0.01f, "facing");
    }

    private static void TacticalTerrainTelemetryFormatsBoundedRow()
    {
        var candidate = TerrainCandidate(100f, 100f, 90f);
        var decision = new TacticalTerrainDecision(
            true,
            TacticalTerrainDecisionReason.Accepted,
            candidate,
            correctionDistance: 10f,
            facingDelta: 5f);

        string line = TacticalTerrainFacingTelemetry.Format(new TacticalTerrainFacingLogRow(
            "DoPlacementAIUnitsWithinDeploymentzoneNew",
            TacticalDeploymentTelemetry.PhaseInitial,
            1,
            "Test Division",
            0,
            centerWater: false,
            footprintWater: false,
            insideDeploymentZone: true,
            facing: 90f,
            enemyBearing: 95f,
            enemyDistance: 600f,
            decision));

        AssertContains(line, "[TacDeployTerrain]", "marker");
        AssertContains(line, "surface=DoPlacementAIUnitsWithinDeploymentzoneNew", "surface");
        AssertContains(line, "unit=Test_Division", "safe unit");
        AssertContains(line, "centerWater=false", "center water");
        AssertContains(line, "decision=Accepted", "reason");
        AssertContains(line, "accepted=true", "accepted");
    }

    private static void TacticalTerrainTelemetrySanitizesUnsafeTokens()
    {
        var candidate = TerrainCandidate(100f, 100f, 90f);
        var decision = new TacticalTerrainDecision(
            true,
            TacticalTerrainDecisionReason.Accepted,
            candidate,
            correctionDistance: 10f,
            facingDelta: 5f);

        string line = TacticalTerrainFacingTelemetry.Format(new TacticalTerrainFacingLogRow(
            "Do\nPlacement=AI|Units{New}",
            "initial\tphase|bad",
            1,
            "Test\r\nDivision=One|{A}",
            0,
            centerWater: false,
            footprintWater: false,
            insideDeploymentZone: true,
            facing: float.NaN,
            enemyBearing: float.PositiveInfinity,
            enemyDistance: float.NegativeInfinity,
            decision));

        AssertFalse(line.Contains("\r") || line.Contains("\n") || line.Contains("\t"), "telemetry row should stay single-line");
        AssertContains(line, "surface=Do_Placement_AI_Units_New", "safe surface");
        AssertContains(line, "phase=initial_phase_bad", "safe phase");
        AssertContains(line, "unit=Test__Division_One__A", "safe unit");
        AssertContains(line, "facing=0.0", "nonfinite facing");
        AssertContains(line, "enemyBearing=0.0", "nonfinite enemy bearing");
        AssertContains(line, "enemyDistance=0.0", "nonfinite enemy distance");
    }

    private static void TacticalMapKnowledgeTelemetryFormatsInventoryRow()
    {
        string line = TacticalMapKnowledgeTelemetry.FormatInventory(new TacticalMapKnowledgeInventoryRow(
            "CheckGlobalAIStrategy",
            objectives: 3,
            entryPoints: 2,
            aiFortifications: 1,
            fortificationGroups: 4,
            roads: 12,
            railroads: 1,
            sampleObjective: "Oak Ridge",
            sampleEntryPoint: "CSA Entry",
            sampleFortification: "Stone Wall"));

        AssertContains(line, "[TacticalMapKnowledge]", "marker");
        AssertContains(line, "surface=inventory", "surface");
        AssertContains(line, "source=CheckGlobalAIStrategy", "source");
        AssertContains(line, "objectives=3", "objective count");
        AssertContains(line, "entryPoints=2", "entry point count");
        AssertContains(line, "aiFortifications=1", "ai fortification count");
        AssertContains(line, "fortificationGroups=4", "fortification group count");
        AssertContains(line, "sampleObjective=Oak_Ridge", "safe objective sample");
        AssertContains(line, "sampleEntryPoint=CSA_Entry", "safe entry sample");
        AssertContains(line, "sampleFortification=Stone_Wall", "safe fort sample");
        AssertContains(line, "signature=objectives=3|entryPoints=2|fortGroups=4", "signature");
    }

    private static void TacticalMapKnowledgeTelemetryFormatsUnitContextRow()
    {
        string line = TacticalMapKnowledgeTelemetry.FormatUnitContext(new TacticalMapKnowledgeUnitContextRow(
            unit: "Hampton Legion",
            alliance: 0,
            x: 150.25f,
            z: 210.75f,
            facing: 45f,
            aiStance: 2,
            formation: 1,
            currentObjective: "Seminary Ridge",
            nearestObjective: "Oak Ridge",
            objectiveBearing: 90f,
            objectiveDistance: 350f,
            nearestEntryPoint: "Union Entry",
            entryPointBearing: 180f,
            entryPointDistance: 1200f,
            nearestFortification: "Fence Line A",
            fortificationBearing: 30f,
            fortificationDistance: 22f,
            coverValue: 0.35f,
            coverValueSubordinates: 0.62f,
            coverObject: 2,
            threatBearing: 270f,
            threatDistance: 500f,
            threatConfidence: "visible-enemy"));

        AssertContains(line, "[TacticalMapKnowledge]", "marker");
        AssertContains(line, "surface=unit", "surface");
        AssertContains(line, "unit=Hampton_Legion", "safe unit");
        AssertContains(line, "pos=(150.3,210.8)", "position");
        AssertContains(line, "facing=45.0", "facing");
        AssertContains(line, "stance=2", "stance");
        AssertContains(line, "formation=1", "formation");
        AssertContains(line, "currentObjective=Seminary_Ridge", "current objective");
        AssertContains(line, "nearestEntryPoint=Union_Entry", "entry point");
        AssertContains(line, "nearestFortification=Fence_Line_A", "fortification");
        AssertContains(line, "cover=0.35", "cover");
        AssertContains(line, "coverSubs=0.62", "cover subordinates");
        AssertContains(line, "threatBearing=270.0", "threat bearing");
        AssertContains(line, "threatConfidence=visible-enemy", "threat confidence");
    }

    private static TacticalTerrainCandidate TerrainCandidate(
        float x,
        float z,
        float facing,
        bool centerWater = false,
        bool footprintWater = false,
        bool insideZone = true)
    {
        return new TacticalTerrainCandidate(
            new TacticalPoint2(x, z),
            facing,
            new TacticalTerrainSample(centerWater ? 4 : 0, centerWater, insideZone),
            new[]
            {
                new TacticalTerrainSample(0, false, insideZone),
                new TacticalTerrainSample(footprintWater ? 4 : 0, footprintWater, insideZone)
            });
    }

    private static TacticalEnemyBearingEvidence VisibleEnemy(float bearing = 90f, float distance = 600f, float strength = 1200f)
    {
        return new TacticalEnemyBearingEvidence(true, bearing, distance, strength);
    }

    private static void TacticalTerrainRejectsWaterCenter()
    {
        var candidate = TerrainCandidate(100f, 100f, 90f, centerWater: true);
        var reason = TacticalTerrainFacingDiscipline.Reject(
            new TacticalPoint2(100f, 100f),
            candidate,
            VisibleEnemy(),
            TestFixtures.DeploymentTerrainRules,
            out _,
            out _);
        var decision = TacticalTerrainFacingDiscipline.Choose(
            new TacticalPoint2(100f, 100f),
            0f,
            new[] { candidate },
            VisibleEnemy(),
            TestFixtures.DeploymentTerrainRules);

        AssertEqual(TacticalTerrainDecisionReason.WaterCenter, reason, "water center rejection");
        AssertFalse(decision.Accepted, "water center should not be accepted");
        AssertEqual(TacticalTerrainDecisionReason.NoSafeCandidate, decision.Reason, "no accepted candidates");
    }

    private static void TacticalTerrainRejectsWaterFootprint()
    {
        var candidate = TerrainCandidate(100f, 100f, 90f, footprintWater: true);
        var reason = TacticalTerrainFacingDiscipline.Reject(
            new TacticalPoint2(100f, 100f),
            candidate,
            VisibleEnemy(),
            TestFixtures.DeploymentTerrainRules,
            out _,
            out _);
        var decision = TacticalTerrainFacingDiscipline.Choose(
            new TacticalPoint2(100f, 100f),
            0f,
            new[] { candidate },
            VisibleEnemy(),
            TestFixtures.DeploymentTerrainRules);

        AssertEqual(TacticalTerrainDecisionReason.WaterFootprint, reason, "water footprint rejection");
        AssertFalse(decision.Accepted, "water footprint should not be accepted");
    }

    private static void TacticalTerrainRejectsOutsideDeploymentZone()
    {
        var candidate = TerrainCandidate(105f, 100f, 90f, insideZone: false);
        var reason = TacticalTerrainFacingDiscipline.Reject(
            new TacticalPoint2(100f, 100f),
            candidate,
            VisibleEnemy(),
            TestFixtures.DeploymentTerrainRules,
            out _,
            out _);
        var decision = TacticalTerrainFacingDiscipline.Choose(
            new TacticalPoint2(100f, 100f),
            0f,
            new[] { candidate },
            VisibleEnemy(),
            TestFixtures.DeploymentTerrainRules);

        AssertEqual(TacticalTerrainDecisionReason.OutsideDeploymentZone, reason, "outside zone rejection");
        AssertFalse(decision.Accepted, "outside deployment zone should not be accepted");
    }

    private static void TacticalTerrainRejectsFootprintOutsideDeploymentZone()
    {
        var candidate = new TacticalTerrainCandidate(
            new TacticalPoint2(105f, 100f),
            90f,
            new TacticalTerrainSample(0, false, true),
            new[]
            {
                new TacticalTerrainSample(0, false, true),
                new TacticalTerrainSample(0, false, false)
            });
        var reason = TacticalTerrainFacingDiscipline.Reject(
            new TacticalPoint2(100f, 100f),
            candidate,
            VisibleEnemy(),
            TestFixtures.DeploymentTerrainRules,
            out _,
            out _);
        var decision = TacticalTerrainFacingDiscipline.Choose(
            new TacticalPoint2(100f, 100f),
            0f,
            new[] { candidate },
            VisibleEnemy(),
            TestFixtures.DeploymentTerrainRules);

        AssertEqual(TacticalTerrainDecisionReason.OutsideDeploymentZone, reason, "footprint outside zone rejection");
        AssertFalse(decision.Accepted, "footprint outside deployment zone should not be accepted");
    }

    private static void TacticalTerrainPicksClosestSafeCandidate()
    {
        var decision = TacticalTerrainFacingDiscipline.Choose(
            new TacticalPoint2(100f, 100f),
            0f,
            new[]
            {
                TerrainCandidate(140f, 100f, 90f),
                TerrainCandidate(110f, 100f, 90f)
            },
            VisibleEnemy(),
            TestFixtures.DeploymentTerrainRules);

        AssertTrue(decision.Accepted, "safe candidate should be accepted");
        AssertNear(110f, decision.Candidate.Point.X, 0.01f, "closest x");
    }

    private static void TacticalTerrainPrefersVisibleEnemyFacing()
    {
        var decision = TacticalTerrainFacingDiscipline.Choose(
            new TacticalPoint2(100f, 100f),
            0f,
            new[]
            {
                TerrainCandidate(110f, 100f, 270f),
                TerrainCandidate(111f, 100f, 90f)
            },
            VisibleEnemy(bearing: 90f),
            TestFixtures.DeploymentTerrainRules);

        AssertTrue(decision.Accepted, "visible enemy candidate should be accepted");
        AssertNear(90f, decision.Candidate.FacingDegrees, 0.01f, "enemy-facing candidate");
    }

    private static void TacticalTerrainNoSafeCandidateKeepsVanilla()
    {
        var decision = TacticalTerrainFacingDiscipline.Choose(
            new TacticalPoint2(100f, 100f),
            45f,
            new[]
            {
                TerrainCandidate(200f, 100f, 90f),
                TerrainCandidate(100f, 100f, 90f, centerWater: true)
            },
            VisibleEnemy(),
            TestFixtures.DeploymentTerrainRules);

        AssertFalse(decision.Accepted, "unsafe candidates should not be accepted");
        AssertEqual(TacticalTerrainDecisionReason.NoSafeCandidate, decision.Reason, "reason");
        AssertNear(45f, decision.Candidate.FacingDegrees, 0.01f, "vanilla facing preserved");
    }

    private static void TacticalTerrainMissingVisibleEnemyRejectsWhenRequired()
    {
        var rules = new TacticalTerrainRules(60f, 90f, requireDeploymentZone: true, requireVisibleEnemyForFacing: true);
        var candidate = TerrainCandidate(100f, 100f, 90f);
        var reason = TacticalTerrainFacingDiscipline.Reject(
            new TacticalPoint2(100f, 100f),
            candidate,
            new TacticalEnemyBearingEvidence(false, 0f, 0f, 0f),
            rules,
            out _,
            out _);
        var decision = TacticalTerrainFacingDiscipline.Choose(
            new TacticalPoint2(100f, 100f),
            0f,
            new[] { candidate },
            new TacticalEnemyBearingEvidence(false, 0f, 0f, 0f),
            rules);

        AssertEqual(TacticalTerrainDecisionReason.MissingVisibleEnemy, reason, "missing visible enemy rejection");
        AssertFalse(decision.Accepted, "missing visible enemy should reject when required");
    }

    private static void TacticalTerrainRejectsNonfiniteVanillaBaseline()
    {
        var decision = TacticalTerrainFacingDiscipline.Choose(
            new TacticalPoint2(float.NaN, 100f),
            45f,
            new[] { TerrainCandidate(100f, 100f, 90f) },
            VisibleEnemy(),
            TestFixtures.DeploymentTerrainRules);

        AssertFalse(decision.Accepted, "nonfinite vanilla baseline should fail closed");
        AssertEqual(TacticalTerrainDecisionReason.NonFiniteBaseline, decision.Reason, "nonfinite baseline reason");
        AssertNear(45f, decision.Candidate.FacingDegrees, 0.01f, "vanilla facing preserved");
    }

    private static void TacticalTerrainRejectsNonfiniteCandidate()
    {
        var candidate = TerrainCandidate(float.PositiveInfinity, 100f, 90f);
        var reason = TacticalTerrainFacingDiscipline.Reject(
            new TacticalPoint2(100f, 100f),
            candidate,
            VisibleEnemy(),
            TestFixtures.DeploymentTerrainRules,
            out _,
            out _);
        var decision = TacticalTerrainFacingDiscipline.Choose(
            new TacticalPoint2(100f, 100f),
            45f,
            new[] { candidate },
            VisibleEnemy(),
            TestFixtures.DeploymentTerrainRules);

        AssertEqual(TacticalTerrainDecisionReason.NonFiniteCandidate, reason, "nonfinite candidate rejection");
        AssertFalse(decision.Accepted, "nonfinite candidate should not be accepted");
        AssertNear(45f, decision.Candidate.FacingDegrees, 0.01f, "vanilla facing preserved");
    }

    private static void TacticalTerrainNormalizesLargePositiveAngles()
    {
        var enemy = new TacticalEnemyBearingEvidence(true, float.MaxValue, 600f, 1200f);
        var candidate = TerrainCandidate(100f, 100f, float.MaxValue);
        float delta = TacticalTerrainFacingDiscipline.AngleDelta(float.MaxValue, 90f);

        AssertFinite(enemy.BearingDegrees, "large positive enemy bearing");
        AssertTrue(enemy.BearingDegrees >= 0f && enemy.BearingDegrees < 360f, "large positive enemy bearing range");
        AssertFinite(candidate.FacingDegrees, "large positive candidate facing");
        AssertTrue(candidate.FacingDegrees >= 0f && candidate.FacingDegrees < 360f, "large positive candidate facing range");
        AssertFinite(delta, "large positive angle delta");
        AssertTrue(delta >= 0f && delta <= 180f, "large positive angle delta range");
    }

    private static void TacticalTerrainNormalizesLargeNegativeAngles()
    {
        var enemy = new TacticalEnemyBearingEvidence(true, -float.MaxValue, 600f, 1200f);
        var candidate = TerrainCandidate(100f, 100f, -float.MaxValue);
        float delta = TacticalTerrainFacingDiscipline.AngleDelta(-float.MaxValue, 90f);

        AssertFinite(enemy.BearingDegrees, "large negative enemy bearing");
        AssertTrue(enemy.BearingDegrees >= 0f && enemy.BearingDegrees < 360f, "large negative enemy bearing range");
        AssertFinite(candidate.FacingDegrees, "large negative candidate facing");
        AssertTrue(candidate.FacingDegrees >= 0f && candidate.FacingDegrees < 360f, "large negative candidate facing range");
        AssertFinite(delta, "large negative angle delta");
        AssertTrue(delta >= 0f && delta <= 180f, "large negative angle delta range");
    }

    private static void TacticalTerrainRejectsUnknownTerrainEvidence()
    {
        var unknownCenter = new TacticalTerrainCandidate(
            new TacticalPoint2(100f, 100f),
            90f,
            TacticalTerrainSample.Unknown,
            Array.Empty<TacticalTerrainSample>());
        var unknownCenterReason = TacticalTerrainFacingDiscipline.Reject(
            new TacticalPoint2(100f, 100f),
            unknownCenter,
            VisibleEnemy(),
            TestFixtures.DeploymentTerrainRules,
            out _,
            out _);

        AssertEqual(TacticalTerrainDecisionReason.UnknownTerrain, unknownCenterReason, "unknown center rejection");

        var unknownFootprint = new TacticalTerrainCandidate(
            new TacticalPoint2(100f, 100f),
            90f,
            new TacticalTerrainSample(0, false, true),
            new[] { TacticalTerrainSample.Unknown });
        var unknownFootprintReason = TacticalTerrainFacingDiscipline.Reject(
            new TacticalPoint2(100f, 100f),
            unknownFootprint,
            VisibleEnemy(),
            TestFixtures.DeploymentTerrainRules,
            out _,
            out _);
        var decision = TacticalTerrainFacingDiscipline.Choose(
            new TacticalPoint2(100f, 100f),
            45f,
            new[] { unknownFootprint },
            VisibleEnemy(),
            TestFixtures.DeploymentTerrainRules);

        AssertEqual(1, unknownFootprint.Footprint.Count, "unknown footprint retained");
        AssertEqual(TacticalTerrainDecisionReason.UnknownTerrain, unknownFootprintReason, "unknown footprint rejection");
        AssertFalse(decision.Accepted, "unknown terrain evidence should not accept correction");
    }

    private static void TacticalTerrainPreservesVanillaFacingWithoutVisibleEnemy()
    {
        var decision = TacticalTerrainFacingDiscipline.Choose(
            new TacticalPoint2(100f, 100f),
            45f,
            new[] { TerrainCandidate(100f, 100f, 180f) },
            new TacticalEnemyBearingEvidence(false, 0f, 0f, 0f),
            TestFixtures.DeploymentTerrainRules);

        AssertTrue(decision.Accepted, "safe terrain candidate can be accepted without visible enemy under default rules");
        AssertNear(45f, decision.Candidate.FacingDegrees, 0.01f, "vanilla facing preserved without visible enemy");
    }

    private static void TacticalBattlefrontGeometryDetectsWideUnsupportedGap()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("left", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, -450f, 0f, 0f),
            CommandNodeOperationalState.Create("right", CommandEchelonKind.BrigadeLike, CommandNodeRole.SupportingAttack, CommandTaskType.FormUp, 450f, 0f, 0f)
        };
        var objective = new BattlefieldObjectiveEstimate("ridge", TacticalObjectiveType.Ridge, 900f, 0.9f, true, 0.8f, 0f, 800f, 0.2f, 0.2f);

        TacticalBattlefrontSnapshot front = TacticalBattlefrontGeometry.Build(nodes, objective);

        AssertTrue(front.HasFrontage, "frontage");
        AssertTrue(front.FrontageWidth >= 850f, "wide frontage");
        AssertEqual(TacticalBattlefrontGap.WideUnsupportedGap, front.Gap, "gap");
        AssertTrue(front.LeftFlankTarget.HasValue, "left flank target");
        AssertTrue(front.RightFlankTarget.HasValue, "right flank target");
    }

    private static void TacticalApproachAvenuePlannerPrefersEnemyEntryRoute()
    {
        var objective = new BattlefieldObjectiveEstimate(
            "ridge-a",
            TacticalObjectiveType.Ridge,
            0f,
            0.75f,
            false,
            0.8f,
            1000f,
            1000f,
            0.4f,
            0.2f);

        TacticalApproachAvenueEstimate avenue = TacticalApproachAvenuePlanner.SelectBest(
            objective,
            ownAllianceId: 0,
            enemyAllianceId: 1,
            observations: new[]
            {
                new TacticalApproachAvenueObservation(
                    TacticalApproachAvenueSource.EntryPoint,
                    ownerAllianceId: 0,
                    originX: 1000f,
                    originZ: 2000f,
                    targetX: 1000f,
                    targetZ: 1600f,
                    confidence01: 0.70f,
                    roadAnchored: true,
                    crossingAnchored: false,
                    "friendly-entry"),
                new TacticalApproachAvenueObservation(
                    TacticalApproachAvenueSource.ScheduledArrival,
                    ownerAllianceId: 1,
                    originX: 1000f,
                    originZ: 0f,
                    targetX: 1000f,
                    targetZ: 250f,
                    confidence01: 0.85f,
                    roadAnchored: true,
                    crossingAnchored: true,
                    "enemy-arrival")
            });

        AssertTrue(avenue.HasAvenue, "avenue");
        AssertNear(1000f, avenue.Origin.X, 0.001f, "origin x");
        AssertNear(0f, avenue.Origin.Z, 0.001f, "origin z");
        AssertTrue(avenue.AxisZ > 0.90f, "axis should point from enemy origin toward objective");
        AssertTrue(avenue.Confidence01 >= 0.85f, "confidence should preserve strong vanilla evidence");
        AssertContains(avenue.Reason, "enemy-arrival", "reason");
    }

    private static void TacticalVisionRuntimeAdapterFallbackObjectiveUsesVisibleEnemyPoint()
    {
        var objectives = TacticalVisionRuntimeAdapter.BuildObjectiveRecordsWithFallback(
            Array.Empty<ObjectiveObservationInput>(),
            Array.Empty<TacticalObjectiveStatus>(),
            Array.Empty<float>(),
            Array.Empty<float>(),
            new TacticalMapPoint(250f, 400f),
            visibleEnemyStrength: 1200f,
            visibleFriendlyStrength: 3000f,
            allianceId: 1);

        AssertEqual(1, objectives.Length, "fallback objective count");
        AssertEqual("enemy-line-1", objectives[0].Observation.ObjectiveId, "fallback objective id");
        AssertEqual(TacticalObjectiveType.EnemyLine, objectives[0].Observation.Type, "fallback objective type");
        AssertEqual(TacticalObjectiveSource.VisibleEnemyLine, objectives[0].Observation.Source, "fallback source");
        AssertEqual(250f, objectives[0].Observation.Location.X, "fallback x");
        AssertEqual(400f, objectives[0].Observation.Location.Z, "fallback z");
        AssertEqual(1200f, objectives[0].EnemyStrength, "fallback enemy strength");
        AssertEqual(3000f, objectives[0].FriendlyAssignedStrength, "fallback friendly strength");
        AssertTrue(objectives[0].HasUsableStrengthEvidence, "fallback strength evidence");
    }

    private static void TacticalVisionFallbackObjectiveUsesVanillaMovementWaypoint()
    {
        var objectives = TacticalVisionRuntimeAdapter.BuildObjectiveRecordsWithMovementFallback(
            Array.Empty<ObjectiveObservationInput>(),
            Array.Empty<TacticalObjectiveStatus>(),
            Array.Empty<float>(),
            Array.Empty<float>(),
            visibleEnemyLine: null,
            visibleEnemyStrength: 0f,
            visibleFriendlyStrength: 0f,
            movementAnchor: new TacticalMapPoint(1886f, -619f),
            movementAnchorFriendlyStrength: 2400f,
            allianceId: 0);

        AssertEqual(1, objectives.Length, "movement fallback objective count");
        AssertEqual("movement-anchor-0", objectives[0].Observation.ObjectiveId, "movement fallback id");
        AssertEqual(TacticalObjectiveSource.FriendlyLineShape, objectives[0].Observation.Source, "movement fallback source");
        AssertEqual(1886f, objectives[0].Observation.Location.X, "movement fallback x");
        AssertEqual(-619f, objectives[0].Observation.Location.Z, "movement fallback z");
        AssertEqual(2400f, objectives[0].FriendlyAssignedStrength, "movement fallback friendly strength");
    }

    private static void TacticalVisionAppendsVisibleEnemyLineToObjectiveChainPicture()
    {
        var objectives = TacticalVisionRuntimeAdapter.BuildObjectiveRecordsWithFallback(
            new[]
            {
                new ObjectiveObservationInput(
                    "vanilla-chain-0",
                    TacticalObjectiveType.UnknownVanillaObjective,
                    TacticalObjectiveSource.ObjectiveChain,
                    new TacticalMapPoint(100f, 200f),
                    sourceConfidence: 0.75f,
                    value: 0.65f,
                    typeAnchorVerified: false),
            },
            new[] { TacticalObjectiveStatus.Scouting },
            new[] { 0f },
            new[] { 1400f },
            new TacticalMapPoint(300f, 400f),
            visibleEnemyStrength: 900f,
            visibleFriendlyStrength: 1600f,
            allianceId: 1);

        AssertEqual(2, objectives.Length, "objective count");
        AssertEqual("vanilla-chain-0", objectives[0].Observation.ObjectiveId, "chain id");
        AssertEqual(TacticalObjectiveType.EnemyLine, objectives[1].Observation.Type, "visible enemy type");
        AssertEqual(TacticalObjectiveSource.VisibleEnemyLine, objectives[1].Observation.Source, "visible enemy source");
        AssertEqual(TacticalObjectiveStatus.Contested, objectives[1].Status, "visible enemy status");
        AssertEqual(900f, objectives[1].EnemyStrength, "visible enemy strength");
        AssertEqual(1600f, objectives[1].FriendlyAssignedStrength, "visible friendly strength");
        AssertTrue(objectives[1].Observation.TypeAnchorVerified, "visible enemy type verified");
    }

    private static void TacticalVisionFallbackObjectiveCarriesCommittedAttackConfidence()
    {
        var objectives = TacticalVisionRuntimeAdapter.BuildObjectiveRecordsWithFallback(
            Array.Empty<ObjectiveObservationInput>(),
            Array.Empty<TacticalObjectiveStatus>(),
            Array.Empty<float>(),
            Array.Empty<float>(),
            new TacticalMapPoint(250f, 400f),
            visibleEnemyStrength: 1200f,
            visibleFriendlyStrength: 3000f,
            allianceId: 1);

        AssertEqual(1, objectives.Length, "fallback objective count");
        AssertTrue(
            objectives[0].Observation.SourceConfidence >= 0.65f,
            "visible enemy line fallback should clear committed attack threshold");
    }

    private static void TacticalNavPlannerProbesToStandoffBound()
    {
        var plan = TacticalNavMeshPlanner.Plan(new TacticalNavPlanInput(
            CommandTaskType.Probe,
            ownX: 0f,
            ownZ: 0f,
            primaryTarget: DoctrineTargetPoint.From(1000f, 0f),
            fallbackTarget: default(DoctrineTargetPoint),
            hasThreat: false,
            threatX: 0f,
            threatZ: 0f,
            hasCurrentWaypoint: false,
            currentWaypointX: 0f,
            currentWaypointZ: 0f,
            closeEngaged: false,
            minWaypointDistance: 15f,
            maxWaypointDistance: 2500f));

        AssertTrue(plan.HasTarget, "probe should produce target");
        AssertEqual("recon-bound", plan.Reason, "reason");
        AssertNear(780f, plan.Target.X, 0.1f, "probe x");
        AssertNear(0f, plan.Target.Z, 0.1f, "probe z");
    }

    private static void TacticalNavPlannerBreaksOffCloseScreen()
    {
        var plan = TacticalNavMeshPlanner.Plan(new TacticalNavPlanInput(
            CommandTaskType.Screen,
            ownX: 850f,
            ownZ: 0f,
            primaryTarget: DoctrineTargetPoint.From(1000f, 0f),
            fallbackTarget: DoctrineTargetPoint.From(650f, 0f),
            hasThreat: true,
            threatX: 1000f,
            threatZ: 0f,
            hasCurrentWaypoint: false,
            currentWaypointX: 0f,
            currentWaypointZ: 0f,
            closeEngaged: true,
            minWaypointDistance: 15f,
            maxWaypointDistance: 2500f));

        AssertTrue(plan.HasTarget, "close screen should break off");
        AssertEqual("recon-breakoff", plan.Reason, "reason");
        AssertNear(650f, plan.Target.X, 0.1f, "fallback x");
        AssertNear(0f, plan.Target.Z, 0.1f, "fallback z");
    }

    private static void TacticalNavPlannerOffsetsCoveredAttackApproach()
    {
        var plan = TacticalNavMeshPlanner.Plan(new TacticalNavPlanInput(
            CommandTaskType.AttackObjective,
            ownX: 0f,
            ownZ: 0f,
            primaryTarget: DoctrineTargetPoint.From(1000f, 0f),
            fallbackTarget: default(DoctrineTargetPoint),
            hasThreat: true,
            threatX: 850f,
            threatZ: 0f,
            hasCurrentWaypoint: false,
            currentWaypointX: 0f,
            currentWaypointZ: 0f,
            closeEngaged: false,
            minWaypointDistance: 15f,
            maxWaypointDistance: 2500f));

        AssertTrue(plan.HasTarget, "covered attack should produce target");
        AssertEqual("attack-approach-offset", plan.Reason, "reason");
        AssertTrue(plan.Target.X < 1000f, "attack approach should stop short of the objective");
        AssertTrue(Math.Abs(plan.Target.Z) > 50f, "attack approach should use a lateral offset");
        AssertTrue(
            DistanceSquared(plan.Target.X, plan.Target.Z, 850f, 0f) > DistanceSquared(925f, 0f, 850f, 0f),
            "offset approach should open distance from covered lane");
    }

    private static void TacticalNavPlannerPrefersRoadCoverAndAvoidsChokeCongestion()
    {
        var plan = TacticalNavMeshPlanner.Plan(new TacticalNavPlanInput(
            CommandTaskType.AttackObjective,
            ownX: 0f,
            ownZ: 0f,
            primaryTarget: DoctrineTargetPoint.From(1000f, 0f),
            fallbackTarget: default(DoctrineTargetPoint),
            hasThreat: true,
            threatX: 850f,
            threatZ: 0f,
            hasCurrentWaypoint: false,
            currentWaypointX: 0f,
            currentWaypointZ: 0f,
            closeEngaged: false,
            minWaypointDistance: 15f,
            maxWaypointDistance: 2500f,
            pathSamples: new[]
            {
                new TacticalPathQualitySample(925f, 0f, roadPreference01: 0.0f, slopeCost01: 0.75f, congestion01: 0.85f, chokeRisk01: 0.90f, bridgeRisk01: 0.70f, deadGround01: 0.05f),
                new TacticalPathQualitySample(850f, 180f, roadPreference01: 0.85f, slopeCost01: 0.10f, congestion01: 0.10f, chokeRisk01: 0.05f, bridgeRisk01: 0.0f, deadGround01: 0.65f)
            }));

        AssertTrue(plan.HasTarget, "path quality should produce target");
        AssertContains(plan.Reason, "path-quality", "reason");
        AssertNear(850f, plan.Target.X, 0.1f, "road x");
        AssertNear(180f, plan.Target.Z, 0.1f, "road z");
    }

    private static void TacticalNavPlannerPenalizesFriendlyFrontBlockers()
    {
        var plan = TacticalNavMeshPlanner.Plan(new TacticalNavPlanInput(
            CommandTaskType.SupportAttack,
            ownX: 0f,
            ownZ: 0f,
            primaryTarget: DoctrineTargetPoint.From(1000f, 0f),
            fallbackTarget: default(DoctrineTargetPoint),
            hasThreat: true,
            threatX: 850f,
            threatZ: 0f,
            hasCurrentWaypoint: false,
            currentWaypointX: 0f,
            currentWaypointZ: 0f,
            closeEngaged: false,
            minWaypointDistance: 15f,
            maxWaypointDistance: 2500f,
            pathSamples: new[]
            {
                new TacticalPathQualitySample(820f, 80f, roadPreference01: 0.90f, slopeCost01: 0.05f, congestion01: 0.05f, chokeRisk01: 0.0f, bridgeRisk01: 0.0f, deadGround01: 0.55f, friendlyBlocker01: 0.95f),
                new TacticalPathQualitySample(780f, -180f, roadPreference01: 0.60f, slopeCost01: 0.10f, congestion01: 0.08f, chokeRisk01: 0.0f, bridgeRisk01: 0.0f, deadGround01: 0.45f, friendlyBlocker01: 0.0f)
            }));

        AssertTrue(plan.HasTarget, "friendly blocker scoring should produce target");
        AssertContains(plan.Reason, "path-quality", "reason");
        AssertNear(780f, plan.Target.X, 0.1f, "unblocked x");
        AssertNear(-180f, plan.Target.Z, 0.1f, "unblocked z");
    }

    private static void TacticalNavPlannerPreservesCorridorContinuity()
    {
        var plan = TacticalNavMeshPlanner.Plan(new TacticalNavPlanInput(
            CommandTaskType.SupportAttack,
            ownX: 0f,
            ownZ: 0f,
            primaryTarget: DoctrineTargetPoint.From(1000f, 0f),
            fallbackTarget: default(DoctrineTargetPoint),
            hasThreat: true,
            threatX: 900f,
            threatZ: 0f,
            hasCurrentWaypoint: true,
            currentWaypointX: 760f,
            currentWaypointZ: -120f,
            closeEngaged: false,
            minWaypointDistance: 15f,
            maxWaypointDistance: 2500f,
            pathSamples: new[]
            {
                new TacticalPathQualitySample(830f, 170f, roadPreference01: 0.95f, slopeCost01: 0.05f, congestion01: 0.05f, chokeRisk01: 0.0f, bridgeRisk01: 0.0f, deadGround01: 0.60f, routeContinuity01: 0.0f),
                new TacticalPathQualitySample(760f, -120f, roadPreference01: 0.65f, slopeCost01: 0.08f, congestion01: 0.08f, chokeRisk01: 0.0f, bridgeRisk01: 0.0f, deadGround01: 0.50f, routeContinuity01: 1.0f)
            }));

        AssertTrue(plan.HasTarget, "corridor continuity should produce target");
        AssertContains(plan.Reason, "path-quality", "reason");
        AssertNear(760f, plan.Target.X, 0.1f, "corridor x");
        AssertNear(-120f, plan.Target.Z, 0.1f, "corridor z");
    }

    private static void TacticalNavPlannerAvoidsReservedBridgeChoke()
    {
        var plan = TacticalNavMeshPlanner.Plan(new TacticalNavPlanInput(
            CommandTaskType.AttackObjective,
            ownX: 0f,
            ownZ: 0f,
            primaryTarget: DoctrineTargetPoint.From(1100f, 0f),
            fallbackTarget: default(DoctrineTargetPoint),
            hasThreat: true,
            threatX: 950f,
            threatZ: 0f,
            hasCurrentWaypoint: false,
            currentWaypointX: 0f,
            currentWaypointZ: 0f,
            closeEngaged: false,
            minWaypointDistance: 15f,
            maxWaypointDistance: 2500f,
            pathSamples: new[]
            {
                new TacticalPathQualitySample(900f, 0f, roadPreference01: 0.95f, slopeCost01: 0.05f, congestion01: 0.30f, chokeRisk01: 0.80f, bridgeRisk01: 0.95f, deadGround01: 0.40f, reservationPressure01: 0.95f),
                new TacticalPathQualitySample(780f, -220f, roadPreference01: 0.55f, slopeCost01: 0.12f, congestion01: 0.10f, chokeRisk01: 0.05f, bridgeRisk01: 0.0f, deadGround01: 0.55f, reservationPressure01: 0.05f)
            }));

        AssertTrue(plan.HasTarget, "reservation pressure should produce target");
        AssertContains(plan.Reason, "path-quality", "reason");
        AssertNear(780f, plan.Target.X, 0.1f, "alternate x");
        AssertNear(-220f, plan.Target.Z, 0.1f, "alternate z");
    }

    private static void TacticalNavPlannerFallbackAvoidsContestedAttackLane()
    {
        var plan = TacticalNavMeshPlanner.Plan(new TacticalNavPlanInput(
            CommandTaskType.FallBackToLine,
            ownX: 800f,
            ownZ: 0f,
            primaryTarget: DoctrineTargetPoint.From(1000f, 0f),
            fallbackTarget: DoctrineTargetPoint.From(300f, 0f),
            hasThreat: true,
            threatX: 950f,
            threatZ: 0f,
            hasCurrentWaypoint: false,
            currentWaypointX: 0f,
            currentWaypointZ: 0f,
            closeEngaged: true,
            minWaypointDistance: 15f,
            maxWaypointDistance: 2500f,
            pathSamples: new[]
            {
                new TacticalPathQualitySample(450f, 0f, roadPreference01: 0.90f, slopeCost01: 0.05f, congestion01: 0.10f, chokeRisk01: 0.05f, bridgeRisk01: 0.0f, deadGround01: 0.20f, fallbackLaneConflict01: 0.95f),
                new TacticalPathQualitySample(420f, -180f, roadPreference01: 0.45f, slopeCost01: 0.12f, congestion01: 0.12f, chokeRisk01: 0.10f, bridgeRisk01: 0.0f, deadGround01: 0.55f, fallbackLaneConflict01: 0.05f)
            }));

        AssertTrue(plan.HasTarget, "fallback route should produce target");
        AssertContains(plan.Reason, "path-quality", "reason");
        AssertNear(420f, plan.Target.X, 0.1f, "fallback lane x");
        AssertNear(-180f, plan.Target.Z, 0.1f, "fallback lane z");
    }

    private static void TacticalFacingPulseObservesDivisionAxisWithoutWrite()
    {
        TacticalFacingPulseDecision decision = TacticalFacingPulseDoctrine.Decide(new TacticalFacingPulseInput(
            TacticalFacingPulseScope.Division,
            CommandTaskType.HoldObjective,
            currentFormation: 0,
            targetFormation: 0,
            currentFacingDegrees: 250f,
            targetFacingDegrees: 50f,
            hasThreatBearing: true,
            threatSource: TacticalFacingThreatSource.Visible,
            closeEngaged: true,
            flankRisk: false,
            playerProtected: false,
            routed: false,
            pendingOrder: false,
            activeMove: false,
            recentPulse: false,
            toleranceDegrees: 15f));

        AssertEqual(TacticalFacingPulseAction.ObserveAxis, decision.Action, "division action");
        AssertFalse(decision.ShouldWrite, "division must not write subordinate formation directly");
        AssertEqual("division-axis-intent", decision.Reason, "reason");
    }

    private static void TacticalFacingPulseCorrectsBrigadeFrontage()
    {
        TacticalFacingPulseDecision decision = TacticalFacingPulseDoctrine.Decide(new TacticalFacingPulseInput(
            TacticalFacingPulseScope.Brigade,
            CommandTaskType.HoldObjective,
            currentFormation: 3,
            targetFormation: 0,
            currentFacingDegrees: 246f,
            targetFacingDegrees: 51f,
            hasThreatBearing: true,
            threatSource: TacticalFacingThreatSource.Visible,
            closeEngaged: true,
            flankRisk: false,
            playerProtected: false,
            routed: false,
            pendingOrder: false,
            activeMove: false,
            recentPulse: false,
            toleranceDegrees: 15f));

        AssertEqual(TacticalFacingPulseAction.CorrectFormationAndFacing, decision.Action, "brigade action");
        AssertTrue(decision.ShouldWrite, "brigade should correct frontage under visible contact");
        AssertEqual(0, decision.TargetFormation, "target formation");
        AssertEqual("formation-and-facing", decision.Reason, "reason");
    }

    private static void TacticalFacingPulseUrgentlyReformsRegimentUnderVisibleContact()
    {
        TacticalFacingPulseDecision decision = TacticalFacingPulseDoctrine.Decide(new TacticalFacingPulseInput(
            TacticalFacingPulseScope.Regiment,
            CommandTaskType.HoldObjective,
            currentFormation: 3,
            targetFormation: 0,
            currentFacingDegrees: 139f,
            targetFacingDegrees: 45f,
            hasThreatBearing: true,
            threatSource: TacticalFacingThreatSource.Visible,
            closeEngaged: true,
            flankRisk: false,
            playerProtected: false,
            routed: false,
            pendingOrder: false,
            activeMove: false,
            recentPulse: false,
            toleranceDegrees: 15f));

        AssertEqual(TacticalFacingPulseAction.CorrectFormationAndFacing, decision.Action, "regiment action");
        AssertTrue(decision.ShouldWrite, "regiment should reform locally");
        AssertTrue(decision.Urgent, "visible close contact should be urgent");
    }

    private static void TacticalFacingPulseDefersPendingRegiment()
    {
        TacticalFacingPulseDecision decision = TacticalFacingPulseDoctrine.Decide(new TacticalFacingPulseInput(
            TacticalFacingPulseScope.Regiment,
            CommandTaskType.HoldObjective,
            currentFormation: 3,
            targetFormation: 0,
            currentFacingDegrees: 139f,
            targetFacingDegrees: 45f,
            hasThreatBearing: true,
            threatSource: TacticalFacingThreatSource.Visible,
            closeEngaged: true,
            flankRisk: false,
            playerProtected: false,
            routed: false,
            pendingOrder: true,
            activeMove: false,
            recentPulse: false,
            toleranceDegrees: 15f));

        AssertEqual(TacticalFacingPulseAction.NoWrite, decision.Action, "pending action");
        AssertFalse(decision.ShouldWrite, "pending regiment should not be interrupted");
        AssertEqual("pending-order", decision.Reason, "reason");
    }

    private static void TacticalSectorNoMeasuredEnemyIsNotWeakPoint()
    {
        var sectors = new[]
        {
            new TacticalSectorAssessment(7, TacticalSectorSource.AngleSlice, 5158f, 0f, 0.8f, false, false, TacticalSectorMission.Hold)
        };

        var result = TacticalSectorLedger.Evaluate(sectors);

        AssertEqual(-1, result.DecisiveSectorId, "no measured enemy cannot be decisive");
        AssertEqual(TacticalSectorMission.Hold, result.Sectors[0].Mission, "no measured enemy should hold");
        AssertEqual(0f, result.Sectors[0].Odds, "no measured enemy has no attack odds");
    }

    private static void TacticalSectorTinyAngleContactIsNotWeakPoint()
    {
        var sectors = new[]
        {
            new TacticalSectorAssessment(4, TacticalSectorSource.AngleSlice, 15306f, 63f, 0.8f, false, false, TacticalSectorMission.Hold)
        };

        var result = TacticalSectorLedger.Evaluate(sectors);

        AssertEqual(-1, result.DecisiveSectorId, "tiny angle contact should not become decisive");
        AssertEqual(TacticalSectorMission.Hold, result.Sectors[0].Mission, "tiny contact should not become weak-point attack");
    }

    private static void TacticalSectorSubstantialContactRemainsWeakPoint()
    {
        var sectors = new[]
        {
            new TacticalSectorAssessment(4, TacticalSectorSource.AngleSlice, 5000f, 2000f, 0.8f, false, false, TacticalSectorMission.Hold)
        };

        var result = TacticalSectorLedger.Evaluate(sectors);

        AssertEqual(4, result.DecisiveSectorId, "substantial contact can be decisive");
        AssertEqual(TacticalSectorMission.AttackWeakPoint, result.Sectors[0].Mission, "real contact should still attack weak point");
    }

    private static void TacticalHqLinkGuardClearsCrossCommandAutoLink()
    {
        bool clear = TacticalHqLinkGuard.ShouldClearAutoGroupLink(
            modEnabled: true,
            newlyLinked: true,
            sourceIsGroupUnit: true,
            targetExists: true,
            sameHierarchy: false,
            sameAiGroup: false);

        AssertTrue(clear, "cross-command group auto-link should be cleared");
    }

    private static void TacticalHqLinkGuardPreservesValidCommandLinks()
    {
        bool hierarchy = TacticalHqLinkGuard.ShouldClearAutoGroupLink(
            modEnabled: true,
            newlyLinked: true,
            sourceIsGroupUnit: true,
            targetExists: true,
            sameHierarchy: true,
            sameAiGroup: false);
        bool existingManualLink = TacticalHqLinkGuard.ShouldClearAutoGroupLink(
            modEnabled: true,
            newlyLinked: false,
            sourceIsGroupUnit: true,
            targetExists: true,
            sameHierarchy: false,
            sameAiGroup: false);

        AssertTrue(!hierarchy, "hierarchy link should be preserved");
        AssertTrue(!existingManualLink, "existing link should not be treated as a new auto-link");
    }

    private static void TacticalQuadrantThreatScorerComputesArcs()
    {
        var slices = new float[36];
        for (int i = 0; i < 9; i++) slices[i] = 10f;
        for (int i = 27; i < 36; i++) slices[i] = 10f;
        var input = new TacticalQuadrantThreatScorer.Input
        {
            Slices = slices,
            SliceWidthDegrees = 10f,
            UnitFacingDegrees = 0f,
        };
        var output = TacticalQuadrantThreatScorer.Score(input);
        AssertTrue(output.FrontStrength > output.RearStrength, "front > rear when enemy is front");
        AssertEqual(TacticalQuadrantThreatScorer.Direction.Front, output.DominantDirection, "dominant = front");
        AssertFalse(output.RearPressureFlag, "no rear pressure");
    }

    private static void TacticalQuadrantThreatScorerDetectsRearPressure()
    {
        var slices = new float[36];
        for (int i = 12; i < 24; i++) slices[i] = 50f;
        var input = new TacticalQuadrantThreatScorer.Input
        {
            Slices = slices,
            SliceWidthDegrees = 10f,
            UnitFacingDegrees = 0f,
        };
        var output = TacticalQuadrantThreatScorer.Score(input);
        AssertTrue(output.RearPressureFlag, "rear pressure when rear > front + max(L,R)");
        AssertEqual(TacticalQuadrantThreatScorer.Direction.Rear, output.DominantDirection, "dominant = rear");
    }

    private static void TacticalQuadrantThreatScorerNullSlicesDegradesGracefully()
    {
        var input = new TacticalQuadrantThreatScorer.Input
        {
            Slices = null,
            SliceWidthDegrees = 10f,
            UnitFacingDegrees = 0f,
        };
        var output = TacticalQuadrantThreatScorer.Score(input);
        AssertEqual(0f, output.FrontStrength, "null slices -> zero");
        AssertFalse(output.RearPressureFlag, "no flag");
    }

    private static void TacticalRefuseFlankIntentNoRefuseWhenBalanced()
    {
        var input = new TacticalRefuseFlankIntent.Input
        {
            LeftFlankStrength = 50f,
            RightFlankStrength = 50f,
            SectorPosture = TacticalRefuseFlankIntent.Posture.Defensive,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalRefuseFlankIntent.Decision.NoRefuse,
            TacticalRefuseFlankIntent.Score(input), "no refuse when balanced");
    }

    private static void TacticalRefuseFlankIntentRefuseLeftWhenLeftThreatened()
    {
        var input = new TacticalRefuseFlankIntent.Input
        {
            LeftFlankStrength = 200f,
            RightFlankStrength = 50f,
            SectorPosture = TacticalRefuseFlankIntent.Posture.Defensive,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalRefuseFlankIntent.Decision.RefuseLeft,
            TacticalRefuseFlankIntent.Score(input), "refuse left under left pressure");
    }

    private static void TacticalRefuseFlankIntentRefuseRightWhenRightThreatened()
    {
        var input = new TacticalRefuseFlankIntent.Input
        {
            LeftFlankStrength = 50f,
            RightFlankStrength = 200f,
            SectorPosture = TacticalRefuseFlankIntent.Posture.Defensive,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalRefuseFlankIntent.Decision.RefuseRight,
            TacticalRefuseFlankIntent.Score(input), "refuse right under right pressure");
    }

    private static void TacticalRefuseFlankIntentNoRefuseOnOffensivePosture()
    {
        var input = new TacticalRefuseFlankIntent.Input
        {
            LeftFlankStrength = 200f,
            RightFlankStrength = 50f,
            SectorPosture = TacticalRefuseFlankIntent.Posture.Offensive,
            AiFeudStance = -1,
        };
        AssertEqual(TacticalRefuseFlankIntent.Decision.NoRefuse,
            TacticalRefuseFlankIntent.Score(input), "offensive posture suppresses refuse");
    }

    private static void TacticalSideLogContextMarksPlayerCommandWithoutFactionInference()
    {
        string line = TacticalSideLogFormatter.Format(
            allianceId: 0,
            playerAllianceId: 0,
            aiVsAi: false,
            topCommandId: "node--225370",
            topCommandName: "Army_of_the_Potomac",
            topCommandLevel: "army",
            rootCommandId: "node--225374",
            rootCommandName: "3rd_Division");

        AssertContains(line, "side=0", "side");
        AssertContains(line, "playerAlliance=0", "player alliance");
        AssertContains(line, "relation=player", "player relation");
        AssertContains(line, "topCommandName=Army_of_the_Potomac", "top command name");
        AssertContains(line, "topCommandLevel=army", "top command level");
        AssertContains(line, "rootCommandName=3rd_Division", "root command name");
        AssertTrue(!line.Contains("Union") && !line.Contains("CSA"), "formatter must not infer faction from army name");
    }
}
