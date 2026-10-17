using System;
using System.IO;
using System.Text.RegularExpressions;
using static TestHarness;
using static TestFixtures;

// BC15's runtime halves (BattleCoreTick, BattleLodRuntime, the five tiered producers, the flag, the telemetry row) are not compiled into this
// project, so their wiring is pinned at the source, as BattleCoreIntegrationTests pins the tick's claims.
internal static partial class BattleLodTests
{
    private static (string, Action)[] RuntimeTests()
    {
        return new (string, Action)[]
        {
            ("bc15 RUNTIME the tick runs BeginTick, the tier pass and the carry, then the producers, then Resolve", TickOrderIsPinned),
            ("bc15 RUNTIME the executor sees the carried Move claims and the tick notes every refusal", TickPassesCarriedAndNotesRefusals),
            ("bc15 RUNTIME StandDown and a new battle day reset the tiers", ResetsAreWired),
            ("bc15 RUNTIME exactly the five tiered producers gate on the tier", OnlyTheTieredProducersGate),
            ("bc15 RUNTIME the posture producer restores a carried group's record and walks groups by id", PostureProducerWiring),
            ("bc15 RUNTIME the nav budget reports reach the tier state", NavReportsAreWired),
            ("bc15 RUNTIME the adapter mirrors the posture pipeline's inputs", AdapterMirrorsThePipeline),
            ("bc15 RUNTIME FlagOffIsEveryGroupEveryTick: the flag gates the pass and a failure falls back to full rate", FlagAndFailSafe),
            ("bc15 RUNTIME the TacticalLod row, its route and the first-fire line", TelemetryIsWired),
            ("bc15 RUNTIME BattleLod runtime files read no real time, no enemy truth and no hard-coded cycle", RuntimeFilesArePure),
        };
    }

    private static string Source(string repoRelative) => File.ReadAllText(FindRepoPath(repoRelative));

    private static int At(string text, string needle, int from = 0)
    {
        int at = text.IndexOf(needle, from, StringComparison.Ordinal);
        AssertTrue(at >= 0, "expected '" + needle + "' in the source");
        return at;
    }

    private static string ClassText(string file, string className)
    {
        string text = Source(file);
        Match m = Regex.Match(text, "class " + className + @"\b");
        AssertTrue(m.Success, className + " not found in " + file);
        int open = text.IndexOf('{', m.Index);
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text.Substring(m.Index, i - m.Index + 1);
        }
        return text.Substring(m.Index);
    }

    private const string TickFile = "src/WhiskeyRealism/Tactical/Battle/BattleCoreTick.cs";
    private const string PostureFile = "src/WhiskeyRealism/Tactical/Battle/Producers/PostureIntentProducer.cs";
    private const string RuntimeFile = "src/WhiskeyRealism/Tactical/Battle/BattleLodRuntime.cs";

    private static void TickOrderIsPinned()
    {
        string tick = Source(TickFile);
        int begin = At(tick, "arbiter.BeginTick();");
        int pass = At(tick, "BattleLodRuntime.BeginTick(", begin);
        int producers = At(tick, "producer.Produce(snapshot, arbiter);", pass);
        int resolve = At(tick, "winners = arbiter.Resolve(_winners);", producers);
        AssertTrue(begin < pass && pass < producers && producers < resolve, "BeginTick, then the tier pass, then the producers, then Resolve");
        string runtime = Source(RuntimeFile);
        int classify = At(runtime, "BattleLod.Classify(");
        int consume = At(runtime, "BattlePerceptionProbe.ConsumeChangeLatch(", classify);
        int refusals = At(runtime, "executor.ClearRefusals();", classify);
        int carryCall = At(runtime, "CarryClaims(s, state, arbiter);", Math.Max(consume, refusals));
        int report = At(runtime, "Report(s, state, nowHours);", carryCall);
        int carry = At(runtime, "arbiter.CarryForward(", report);
        AssertTrue(classify < consume && classify < refusals && Math.Max(consume, refusals) < carryCall && carryCall < report && report < carry,
            "the pass reads the latches and refusals, they are cleared, and only then are claims carried");
    }

    private static void TickPassesCarriedAndNotesRefusals()
    {
        string tick = Source(TickFile);
        AssertTrue(tick.Contains("arbiter.CarriedWinners(_carried)"), "the carried cells are collected after Resolve");
        AssertTrue(tick.Contains("executor.BeginTickClaims(snapshot, _winners, winners, _carried, carriedCount);"), "and handed to the executor's claims");
        int claims = At(tick, "executor.BeginTickClaims(");
        int observe = At(tick, "executor.ObserveOutcomes(", claims);
        AssertTrue(claims < observe, "the claims are set before outcomes, retries and winners are planned");
        AssertEqual(2, Regex.Matches(tick, @"executor\.NoteRefusal\(").Count, "a refused winner and a refused retry are noted");
        AssertTrue(Regex.IsMatch(tick, @"if \(!plan\.Execute\)\s*\{\s*stats\.Skip\(plan\.Skip\);\s*executor\.NoteRefusal\("), "the winner's refusal is noted where it is tallied");
    }

    private static void ResetsAreWired()
    {
        string tick = Source(TickFile);
        string stand = ClassText(TickFile, "BattleCoreTick");
        int standDown = At(stand, "internal static void StandDown(int alliance)");
        AssertTrue(stand.IndexOf("BattleLodRuntime.StandDown(alliance)", standDown, StringComparison.Ordinal) > standDown, "StandDown resets the side's tiers");
        string coordinator = Source("src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs");
        int day = At(coordinator, "private static void PrepareForNewBattleDay()");
        int dayEnd = At(coordinator, "private static bool SafeAiVsAi()", day);
        AssertTrue(coordinator.IndexOf("BattleLodRuntime.NewBattleDay()", day, StringComparison.Ordinal) is int at && at > day && at < dayEnd,
            "a new battle day resets both sides' tiers");
        AssertTrue(tick.Contains("BattleLodRuntime.StandDown(alliance)"), "BattleCoreTick names the reset");
    }

    private static void OnlyTheTieredProducersGate()
    {
        // BC15 decision 5 as amended (integrator ruling, verify round 1): tier a producer only where every input it reads is a trigger or a Distant
        // condition. The posture producer and the facing pulse carry the NavMesh cost; the group stance reads two more inputs, both signature fields.
        const string gate = "BattleLodRuntime.IsDue(";
        AssertTrue(ClassText(PostureFile, "PostureIntentProducer").Contains(gate), "the posture producer gates");
        AssertTrue(Regex.IsMatch(ClassText(PostureFile, "FacingPulseIntentProducer"),
            @"if \(!g\.Has \|\| g\.Submitted\) continue;\s*(//[^\n]*\s*)*if \(!BattleLodRuntime\.IsDue\(s, i\)\) continue;"), "the facing pulse gates its group loop");
        AssertFalse(ClassText(PostureFile, "FireControlIntentProducer").Contains(gate),
            "fire control is not tiered: it reads the posture record, which a carried group keeps");
        AssertTrue(Source("src/WhiskeyRealism/Tactical/Battle/Producers/GroupStanceIntentProducer.cs").Contains(gate), "the group stance gates");
        foreach (string untiered in new[] { "PlayIntentProducer.cs", "ArtilleryIntentProducer.cs", "WithdrawalIntentProducer.cs", "WithdrawalIntentProducer.Plan.cs",
            "ChargeIntentProducer.cs", "ChargeIntentProducer.Plan.cs", "ChargeDemotionIntentProducer.cs", "ReconIntentProducer.cs", "ReserveReleaseIntentProducer.cs",
            "OpportunityIntentProducer.cs" })
            AssertFalse(Source("src/WhiskeyRealism/Tactical/Battle/Producers/" + untiered).Contains("BattleLodRuntime"),
                untiered + " is not tiered (it reads inputs that are neither a trigger nor a Distant condition, acts in contact, on scouts or side-wide)");
    }

    private static void PostureProducerWiring()
    {
        string posture = ClassText(PostureFile, "PostureIntentProducer");
        AssertTrue(posture.Contains("int start = t.Nav.StartIndex(t.GroupIds, t.GroupCount);"), "the walk starts after the last planned id");
        AssertTrue(posture.Contains("t.GroupOrder[(start + k) % t.GroupCount]"), "and goes by ascending instance id");
        AssertTrue(Regex.IsMatch(posture, @"if \(!BattleLodRuntime\.IsDue\(s, i\)\)\s*\{\s*t\.Records\.Carry\(t\.Groups, i, rec\.InstanceId\);\s*continue;\s*\}"),
            "a group that is not due keeps its recorded posture (recon and fire control read it) and submits nothing");
        AssertTrue(posture.Contains("t.Records.Remember(t.Groups, i, rec.InstanceId);"), "an evaluated group's record is kept for the ticks it is carried");
        string tick = ClassText(PostureFile, "PostureTick");
        AssertTrue(tick.Contains("public readonly PostureRecordCache Records = new PostureRecordCache();"), "the record cache lives with the per-side scratch");
        AssertEqual(2, Regex.Matches(tick, @"Records\.Clear\(\)").Count, "and is reset with the battle and on a stand-down");
    }

    private static void NavReportsAreWired()
    {
        string posture = Source(PostureFile);
        AssertTrue(Regex.IsMatch(posture, @"if \(!t\.Nav\.TryConsume\(s\.Own\[i\]\.InstanceId\)\)\s*\{\s*t\.NavDeferred = true;\s*(//[^\n]*\s*)*BattleLodRuntime\.NavDeferred\("),
            "a plan the budget could not give is reported to the tier state");
        AssertTrue(Regex.IsMatch(posture, @"if \(recordTick\) BattleLodRuntime\.NavPlanned\("), "a posture evaluation starts by clearing its own deferral");
    }

    private static void AdapterMirrorsThePipeline()
    {
        string runtime = Source(RuntimeFile);
        // Decision 2(c): the resolved target is the order's primary target, else the operation's primary objective.
        AssertTrue(runtime.Contains("PostureTargets.TryOperationObjective("), "the objective-approach fallback of TryTarget");
        AssertTrue(runtime.Contains("order.PrimaryTarget"), "the order's primary target");
        AssertTrue(runtime.Contains("order.ManeuverHeading") && runtime.Contains("order.HasFormation"), "the order's maneuver heading and formation (BC11, BC14W)");
        // The task is the one the pipeline decides from: opportunity override and stance guard, for a pool group's order and a leaf brigade's task.
        AssertTrue(runtime.Contains("TacticalOpportunityTaskOverride.Override(") && runtime.Contains("PostureStanceGuard.Task("), "the rewrites that change a task");
        AssertTrue(runtime.Contains("army.CurrentReinforcementOpportunity") && runtime.Contains("army.CurrentBattleStance"), "and the plan signature carries what they read");
        int signature = At(runtime, "private static int PlanSignature(");
        string signatureBody = runtime.Substring(signature, At(runtime, "internal static ReinforcementOpportunity ReadOpportunity(", signature) - signature);
        AssertTrue(signatureBody.Contains("h = h * 31 + (int)ReadOpportunity(army);"), "the plan signature folds the reinforcement opportunity");
        AssertTrue(signatureBody.Contains("h = h * 31 + (int)army.CurrentBattleStance;"), "and the battle stance");
        // The latches and maneuvers: Full-only.
        foreach (string needle in new[]
        {
            "exploits.IsExploiting(", "WorksHoldGuard.Holds(", "army.PinsWorksNode(", "ReconIntentProducer.HasProbeOrHold(", "ReconIntentProducer.MissionScoutId(",
            "PostureIntentProducer.UnderArrivalHold(", ".ManeuverHold", ".HasVia", "WorksHeld",
        })
            AssertTrue(runtime.Contains(needle), "HasLatchOrManeuver reads " + needle);
        // The stance producer's own inputs (decision 5 as amended): the #45 cached decision, hashed over everything the producer reads of it.
        int stance = At(runtime, "public int StanceDecision(int ownIndex)");
        string stanceBody = runtime.Substring(stance, At(runtime, "public bool TrySlot(", stance) - stance);
        AssertTrue(stanceBody.Contains("GroupStanceIntentProducer.TryResolvedDecision(_s, ownIndex, out TacticalResolvedGroupDecision r)") && stanceBody.Contains("return -1;"),
            "the lookup asks the producer's own resolver and says -1 for no decision");
        AssertTrue(stanceBody.Contains("(int)r.Kind") && stanceBody.Contains("r.FinalStance") && stanceBody.Contains("r.FromDoctrine") && stanceBody.Contains("r.FromLedger"),
            "and hashes the kind, the final stance and where it came from");
        AssertTrue(runtime.Contains("LeafSlotTarget.TryResolve("), "a leaf brigade's layout slot");
        AssertTrue(runtime.Contains("CorridorSignature("), "the friendly corridor");
        AssertFalse(runtime.Contains("TryNavPlan") || runtime.Contains("RouteBudget") || runtime.Contains("TryConsume"), "the lookup spends no nav budget");
    }

    private static void FlagAndFailSafe()
    {
        string runtime = Source(RuntimeFile);
        string plugin = Source("src/WhiskeyRealism/Plugin.cs");
        AssertTrue(Regex.IsMatch(plugin, "\"Tactical\",\\s*\"Enable Battle AI Level Of Detail\",\\s*true,"), "the flag, in the Tactical section, default ON");
        AssertTrue(plugin.Contains("internal static bool BattleAiLevelOfDetailEnabled()"), "its null-safe reader");
        int run = At(runtime, "internal static void BeginTick(");
        int flag = At(runtime, "Plugin.BattleAiLevelOfDetailEnabled()", run);
        int classify = At(runtime, "BattleLod.Classify(", run);
        AssertTrue(flag < classify, "the flag is read before the pass runs");
        AssertTrue(Regex.IsMatch(runtime, @"catch \(Exception ex\)\s*\{[^}]*ResetAllFull\("), "a failure resets every group to Full");
        AssertTrue(runtime.Contains("\"tactical-battle-core:lod-failed\""), "and warns once under the smoke checklist's key");
        AssertTrue(runtime.Contains("arbiter.BeginTick();"), "and clears whatever the failed pass carried");
        AssertTrue(runtime.Contains("TelemetryPerf.Scope(\"tactical.lod\", TelemetryLayer.Tactical, TelemetryCategory.Performance, 0.5)"), "the tier pass has its scope (0.5 ms)");
        int gate = At(runtime, "internal static bool IsDue(");
        AssertTrue(runtime.IndexOf("state.Covers(s)", gate, StringComparison.Ordinal) > gate, "an unclassified snapshot is always due");
        AssertTrue(runtime.IndexOf("ExploitTable.ForAlliance(s.Alliance).IsExploiting(", gate, StringComparison.Ordinal) > gate &&
            runtime.IndexOf("DropCarried(", gate, StringComparison.Ordinal) > gate, "a group an exploit just launched on is woken in the same tick (decision 12)");
        AssertTrue(Regex.IsMatch(runtime, @"if \(!state\.PromoteNow\(s, ownIndex, LodPromotion\.Target, s\.DoctrineHours\)\) return false;\s*OrderArbiter\.ForAlliance\(s\.Alliance\)\?\.DropCarried\("),
            "the wake-up promotes through trigger (c) on the doctrine clock, then drops the carried cells");
    }

    private static void TelemetryIsWired()
    {
        string runtime = Source(RuntimeFile);
        AssertTrue(runtime.Contains("\"TacticalLod\""), "the row");
        foreach (string field in new[] { "\"full\"", "\"distant\"", "\"due\"", "\"carried\"", "\"navDeferred\"", "\"promotions\"" })
            AssertTrue(runtime.Contains(".WithField(" + field), "row field " + field);
        foreach (string reason in new[] { "observation", "parent", "target", "bearing", "corridor", "plan", "distance", "fire", "moving", "pending", "player-chain", "stance" })
            AssertTrue(runtime.Contains("\"" + reason + "\""), "promotion reason " + reason);
        AssertTrue(runtime.Contains("0.1"), "one row per 0.1 doctrine hours");
        AssertTrue(Regex.IsMatch(runtime, @"EnableTacticalObserver\.Value\)\s*EmitRow\(a, state\);"), "the row is emitted under Enable Tactical Observer");
        AssertTrue(Regex.IsMatch(runtime, @"finally\s*\{\s*state\.ClearPromotionCounts\(\);\s*\}"), "and the promotion counts restart for the next row's window");
        AssertTrue(runtime.Contains("OnceLog.Info(\"tactical-battle-core:lod-first-fire\""), "the first-fire line");
        AssertTrue(runtime.Contains("[BattleLod] first distant tier: alliance="), "with the checklist's text");
        string policy = Source("src/WhiskeyRealism/Telemetry/TelemetryTagPolicy.cs");
        AssertTrue(Regex.IsMatch(policy, @"AddTactical\(""TacticalLod"", TelemetryCategory\.State\)"), "the row has a route, or it is dropped");
    }

    private static void RuntimeFilesArePure()
    {
        string runtime = Source(RuntimeFile);
        AssertFalse(Regex.IsMatch(runtime, @"realtimeSinceStartup|Environment\.TickCount|DateTime"), "BattleLodRuntime reads real time");
        AssertFalse(Regex.IsMatch(runtime, @"EnemyRecords|combatstatussubordinates|CombatStatusSubordinates"), "BattleLodRuntime reads the enemy's true state");
        AssertFalse(Regex.IsMatch(runtime, @"0\.015"), "BattleLodRuntime hard-codes the side-stat cycle");
        AssertTrue(runtime.Contains("GamePrefs.sidestatupdatecycle"), "the cycle comes from vanilla's prefs");
    }
}
