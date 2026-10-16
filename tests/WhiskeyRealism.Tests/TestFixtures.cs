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

// Test-only builders and oracles for production seams (kept out of src so the
// production assembly carries no test-only members).
internal static class TestFixtures
{
    // Deployment terrain rules the discipline tests are written against.
    internal static TacticalTerrainRules DeploymentTerrainRules =>
        new TacticalTerrainRules(60f, 90f, requireDeploymentZone: true, requireVisibleEnemyForFacing: false);

    // Oracle: vanilla AIBattle.AdjustGroupAIStance re-evaluates a group's stance
    // only once lastaistancechangetime < now + updatetimeforresettingofaistance
    // (decompile 4264).
    internal static bool VanillaWouldReevaluateStance(float lastStanceChangeHours, float nowHours, float vanillaResetHours)
        => lastStanceChangeHours < nowHours + vanillaResetHours;

    internal static TacticalDirectChildGate.Input WithIntendedTargetSector(this TacticalDirectChildGate.Input input, int sector)
        => new TacticalDirectChildGate.Input(
            input.GateEnabled, input.SideIsAi, input.Role, input.AxisSector, input.PrimarySector,
            input.IntendedTargetBearingFromGroupRadians, input.IntendedTargetDistanceFromGroup,
            input.NearestEnemyBearingFromGroupRadians, input.FeudMaxDistance, sector);

    internal static bool TryEnqueue(this TelemetryQueue queue, TelemetryEvent ev)
        => queue.Enqueue(ev) == TelemetryQueueResult.Enqueued;

    internal static string CreateSessionDirectory(string gameRoot, string sessionId)
    {
        string directory = TelemetrySession.SessionDirectory(gameRoot, sessionId);
        Directory.CreateDirectory(directory);
        return directory;
    }

    internal static FrontSectorLedger BuildLedger()
    {
        return FrontSectorLedger.Build(new[]
        {
            new FrontSectorInput
            {
                SectorKey = "Richmond",
                Theater = Theater.East,
                OwnStrength = 9000f,
                EnemyStrength = 12000f,
                StrategicImportance = 1.0f,
                IsCritical = true,
                IsPlanTarget = false,
                CommanderAudacity = 0.2f,
                CommanderCaution = 0.4f,
                AverageMorale = 0.75f,
                AverageSupply = 0.8f,
                AverageReadiness = 0.8f
            },
            new FrontSectorInput
            {
                SectorKey = "Coast",
                Theater = Theater.Coast,
                OwnStrength = 6000f,
                EnemyStrength = 9000f,
                StrategicImportance = 0.35f,
                IsCritical = false,
                IsPlanTarget = false,
                CommanderAudacity = 0.1f,
                CommanderCaution = 0.2f,
                AverageMorale = 0.7f,
                AverageSupply = 0.7f,
                AverageReadiness = 0.7f
            },
            new FrontSectorInput
            {
                SectorKey = "Vicksburg",
                Theater = Theater.River,
                OwnStrength = 18000f,
                EnemyStrength = 10000f,
                StrategicImportance = 0.9f,
                IsCritical = true,
                IsPlanTarget = true,
                CommanderAudacity = 0.5f,
                CommanderCaution = 0.1f,
                AverageMorale = 0.85f,
                AverageSupply = 0.85f,
                AverageReadiness = 0.85f
            }
        }, new FrontLedgerOptions
        {
            MinimumHoldRatio = 0.9f,
            CriticalHoldRatioBonus = 0.25f,
            ConcessionRatio = 0.75f,
            ExploitRatio = 1.25f
        });
    }

    internal static ObjectiveRecord ObjectiveRecordFor(
        string objectiveId,
        float enemyStrength,
        float friendlyAssignedStrength,
        TacticalObjectiveStatus status = TacticalObjectiveStatus.Scouting)
    {
        return new ObjectiveRecord(
            new ObjectiveObservationInput(
                objectiveId,
                TacticalObjectiveType.Ridge,
                TacticalObjectiveSource.VerifiedSceneObject,
                new TacticalMapPoint(10f, 20f),
                0.9f,
                1.0f,
                typeAnchorVerified: true),
            status,
            enemyStrength,
            friendlyAssignedStrength);
    }

    internal static CommandNodeOperationalState CommandState(CommandTaskType task)
    {
        return new CommandNodeOperationalState(
            "node-1",
            CommandEchelonKind.DivisionLike,
            CommandNodeRole.MainEffort,
            task,
            CommandTaskState.Committed);
    }

    internal static CommandDoctrineOrder DoctrineOrder(
        CommandTaskType task,
        DoctrineTargetPoint primary = default(DoctrineTargetPoint),
        DoctrineTargetPoint support = default(DoctrineTargetPoint),
        DoctrineTargetPoint fallback = default(DoctrineTargetPoint),
        DoctrineAllowedIdleReason allowedIdle = DoctrineAllowedIdleReason.None,
        string objectiveId = "objective-1",
        CommandNodeRole role = CommandNodeRole.MainEffort)
    {
        return CommandDoctrineOrder.Create(
            "node-1",
            role,
            task,
            objectiveId,
            primary,
            support,
            fallback,
            allowedIdle,
            minCommitUntilSeconds: 600f,
            issuedAtSeconds: 50f,
            confidence01: 0.8f,
            reason: "test");
    }

    internal static PlayerOrderActiveSnapshot PlayerOrderActive(
        string unitKey,
        int vanillaType,
        PlayerOrderScope scope = PlayerOrderScope.Tactical,
        PlayerOrderProvenance provenance = PlayerOrderProvenance.Vanilla,
        bool activeCampaignActionable = false,
        string battleIdentity = "battle-a",
        int givenOrderSession = 1,
        string objectiveKey = "active",
        PlayerOrderPoint? targetPoint = null,
        float rotation = 45f,
        bool battleEnded = false,
        bool stale = false,
        PlayerOrderIntent activeIntent = PlayerOrderIntent.None,
        double ageHours = 0.0)
    {
        var point = targetPoint ?? new PlayerOrderPoint(100f, 200f);
        return new PlayerOrderActiveSnapshot(
            scope: scope,
            intent: activeIntent,
            vanillaType: vanillaType,
            priority: PlayerOrderPriority.ForActiveVanillaType(vanillaType, scope, provenance),
            unitKey: unitKey,
            battleIdentity: battleIdentity,
            givenOrderSession: givenOrderSession,
            targetPoint: point,
            rotation: rotation,
            objectiveKey: objectiveKey,
            reason: "active",
            activeCampaignActionable: activeCampaignActionable,
            campaignGroupFlag: scope == PlayerOrderScope.Campaign,
            provenance: provenance,
            battleEnded: battleEnded,
            stale: stale,
            ageHours: ageHours);
    }

    internal static FormationSnapshot Snapshot(
        string key,
        int alliance,
        int unitType,
        float strength,
        float enemy,
        FormationLevel enemyLevel,
        FrontPosture posture)
    {
        return new FormationSnapshot
        {
            UnitKey = key,
            AllianceId = alliance,
            UnitName = key,
            UnitType = unitType,
            IsTopUnit = true,
            GroupStrengthActive = strength,
            Morale = 0.8f,
            Readiness = 0.8f,
            RifleAmmo = 0.8f,
            ArtilleryAmmo = 0.8f,
            Supply = 0.8f,
            WeaponFirepower = 1.0f,
            AreaKey = "VirginiaCapitalCorridor",
            SectorKey = "Richmond",
            LocalEnemyStrength = enemy,
            VisibleEnemyLevel = enemyLevel,
            FrontPosture = posture
        };
    }

    internal static JObject ParseJsonLine(string json)
    {
        using (var textReader = new StringReader(json))
        using (var jsonReader = new JsonTextReader(textReader) { DateParseHandling = DateParseHandling.None })
        {
            return JObject.Load(jsonReader);
        }
    }

    internal static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "wr-tests-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(path);
        return path;
    }

    internal static void DeleteDirectoryQuietly(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
        }
    }

    internal static float DistanceSquared(float ax, float az, float bx, float bz)
    {
        float dx = ax - bx;
        float dz = az - bz;
        return dx * dx + dz * dz;
    }

    // ---- Major historical playbook selection tests (O1.5) ----

    internal static class SeedCatalog
    {
        public static TacticalPlaybookCatalog AllHistoricalAndGeneric()
        {
            var c = new TacticalPlaybookCatalog();
            c.Register(new DoubleEnvelopmentPlaybook());
            c.Register(new MobileFlankMarchPlaybook());
            c.Register(new PreparedDefensePlaybook());
            c.Register(new ManeuverAndFixPlaybook());
            c.Register(new ContinuousAttritionPlaybook());
            c.Register(new ReverseSlopeDefensePlaybook());
            c.Register(new WideFlankApproachPlaybook());
            c.Register(new FrontalShockAssaultPlaybook());
            c.Register(new ForcedFrontalAttackPlaybook());
            c.Register(new HesitatedCommitPlaybook());
            c.Register(new GenericAggressivePlaybook());
            c.Register(new GenericCautiousPlaybook());
            c.Register(new GenericMethodicalPlaybook());
            c.Register(new GenericDesperatePlaybook());
            c.Register(new CavalryScreeningDelayPlaybook());
            c.Register(new CavalryRaidPlaybook());
            c.Register(new MeetingEngagementPlaybook());
            c.Register(new DelayingDefensePlaybook());
            return c;
        }
    }

    internal static ArmyOrchestrator NewArmyOrchestratorWithPlan(int mainSector = 2)
    {
        var personality = new PersonalityVector(0.2f, 0f, 0f, 0f, 0f);
        var catalog = new TacticalPlaybookCatalog();
        var orch = new ArmyOrchestrator(allianceId: 0, catalog, personality);
        orch.SetPlanForTesting(new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort,
            mainSector, Array.Empty<int>(), Array.Empty<int>(), 1.2f, 0f, 0));
        return orch;
    }

    internal static DirectChildIntent DirectIntent(
        string childId,
        DirectChildRole role,
        DirectChildAxis axis,
        int primarySector,
        float supportPriority01)
    {
        return new DirectChildIntent(
            childId,
            rawUnitTyp: 15,
            effectiveCommandLevel: 15,
            displayName: childId,
            primarySector,
            role,
            axis,
            axisSector: primarySector,
            supportPriority01,
            new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>()));
    }

    // ====== Reinforcement opportunity doctrine tests ======

    internal static TacticalForceBalanceEvidence MakeForceBalance(
        float own, float enemy,
        ReinforcementArrival[] ownArrivals = null,
        float aggression = 0.5f, float initiative = 0.5f, float intel = 0.8f,
        float ownCasualtyRatio = 0f, float ownRoutedFraction = 0f,
        float battleHoursElapsed = 0f,
        BattleStance stance = BattleStance.Meeting,
        float? casualtyTolerance = null)
    {
        // The casualty escalation threshold follows the commander's casualty tolerance (BC13); a fixture
        // that names no tolerance gives the commander the tolerance of his aggression, as the older tests read it.
        return new TacticalForceBalanceEvidence(
            own, enemy,
            ownArrivals ?? System.Array.Empty<ReinforcementArrival>(),
            initiative, aggression, intel,
            ownCasualtyRatio01: ownCasualtyRatio,
            ownRoutedFraction01: ownRoutedFraction,
            battleHoursElapsed: battleHoursElapsed,
            stance: stance,
            commanderCasualtyTolerance01: casualtyTolerance ?? aggression);
    }


    // The one way an allocation test measures a window. GC.GetAllocatedBytesForCurrentThread()
    // reads alloc_bytes - (alloc_limit - alloc_ptr): the bytes of every allocation-context
    // quantum the thread took, less the part it has not used yet. When a background GC ends
    // its mark phase the runtime voids every thread's context (alloc_ptr = alloc_limit = 0,
    // gc.cpp repair_allocation_contexts(FALSE)) without taking the unused part back off
    // alloc_bytes, so a thread that allocated nothing reads up to one quantum (~8 KB) more
    // than before. Any test run after a background GC thread exists (the first gen2 does it)
    // can land in that window, and the result is a random 0.2-8 KB "allocation".
    //
    // The unused part is the only thing the void can add, so the window opens on an empty
    // context: a blocking gen0 GC resets the calling thread's context to alloc_ptr = 0 (and
    // takes its unused part off alloc_bytes properly), and nothing is allocated between that
    // GC and the first read. A path that really allocates still takes a quantum and reads
    // nonzero; one that allocates nothing reads exactly zero whatever the GC does.
    // Pinned by AllocationMeasurementTests.
    internal static long BeginAllocationWindow()
    {
        GC.Collect(0, GCCollectionMode.Forced, blocking: true);
        return GC.GetAllocatedBytesForCurrentThread();
    }

    // Bytes the calling thread allocated since BeginAllocationWindow returned `begin`.
    internal static long AllocatedSince(long begin)
        => GC.GetAllocatedBytesForCurrentThread() - begin;

    // Walks up from the working directory to the first ancestor holding relativePath.
    internal static string FindRepoPath(string relativePath)
    {
        string current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrWhiteSpace(current))
        {
            string candidate = Path.Combine(current, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        throw new FileNotFoundException("repo path not found", relativePath);
    }

    internal static string ReadRepoFile(string relativePath)
        => File.ReadAllText(FindRepoPath(relativePath));

    // True when one line of the body, trimmed, is exactly `line`: a commented-out or prefixed
    // copy of the statement does not match.
    internal static bool HasLine(string body, string line)
    {
        foreach (var raw in body.Split('\n'))
            if (raw.Trim() == line) return true;
        return false;
    }

    // The division play's order for one child node, default when it has none
    // (the producer walks decision.Orders itself).
    internal static TacticalDivisionPlayOrder PlayOrderFor(TacticalDivisionPlayDecision decision, string nodeId)
    {
        for (int i = 0; i < decision.Orders.Count; i++)
        {
            if (string.Equals(decision.Orders[i].NodeId, nodeId, StringComparison.Ordinal))
                return decision.Orders[i];
        }
        return default;
    }
}
