using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Battle.BugFixes;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;
using static TestHarness;

// BC13 verify round 1 (REVISE): the reset between battles, playerSkips, the #119 row in every case, the RangeOnly reach per alliance,
// behaviour tests for the guards that were pinned only by source text, and the minors.
internal static partial class CommanderOpportunityTests
{
    private static (string, Action)[] ReviseTests()
    {
        return new (string, Action)[]
        {
            ("bc13 REV-RESET a second battle on an earlier doctrine clock inherits no opening, id, claim, launch or proved ground", ReviseTwoBattles),
            ("bc13 REV-RESET every BC13 static registers its own BattleScope reset from its owner's static constructor", ReviseStaticsRegistered),
            ("bc13 REV-REACH each alliance reads its own RangeOnly reach", ReviseReachPerAlliance),
            ("bc13 REV-PLAYERSKIPS the count is keyed by alliance and exploit, read once, and reset with the battle", RevisePlayerSkipsBook),
            ("bc13 REV-PLAYERSKIPS a posture request carries the exploit id on its Move, Formation and Stance intents", RevisePlayerSkipsIntent),
            ("bc13 REV-PLAYERSKIPS an exploit whose chain includes a player-owned child counts it from the executor's plan", RevisePlayerSkipsChain),
            ("bc13 REV-PLAYERSKIPS the tick drains the count into the entry and the end row carries it", RevisePlayerSkipsEndRow),
            ("bc13 REV-STANCE-ROW the #119 plan observes in every case and names the case", ReviseStancePlan),
            ("bc13 REV-GATE vanilla fixes run in every Commander Mode; the steers that serve Whiskey's command only in Active", ReviseFixGate),
            ("bc13 REV-GATE #121 suppresses only a Whiskey-commanded group the write guard allows", ReviseSeparatedGate),
            ("bc13 REV-GATE #120 acts only when every term holds and sees only a Visible observation", ReviseCavalryGate),
            ("bc13 REV-GATE a follower submits no Move or Formation", ReviseFollowRule),
            ("bc13 REV-PLAYER a node that is PlayerOwned only, PlayerCommand only, Feud only or Routed only never decides", RevisePlayerNodes),
            ("bc13 REV-PLAYER an asset with a Feud descendant is never chosen", ReviseFeudDescendant),
            ("bc13 DET-REINFORCEMENT a reinforcement column in the gap's corridor closes nothing", ReviseReinforcement),
            ("bc13 DET-REMEMBERED-ROW a Remembered sample's true row is never read for its width", ReviseRememberedRow),
            ("bc13 REV-ALLOC the child id is parsed without allocating and as int.TryParse does", ReviseChildIds),
            ("bc13 REV-ARMY-AGG #44's army fallback without a table entry uses the ranked Default, as the orchestrator does", ReviseArmyAggression),
            ("bc13 REV-ONCELOG a concatenated first-fire message is built only when its key has not fired", ReviseOnceLogGuard),
        };
    }

    // ------------------------------------------------------------------ reset between battles

    private static void ReviseTwoBattles()
    {
        BattleScope.ResetAll();
        TickFx t = TickFx.Attacker();
        t.Now = 50.0;
        AssertTrue(t.RunToLaunch(), "battle 1 launches");
        int firstOpeningId = t.Table.At(0).OpeningId;
        int firstExploitId = t.Table.At(0).Id;
        AssertTrue(t.Tick.Detector.Count >= 1, "and holds openings");

        // Battle 2: the scope reset the producer registers resets its tick; the clock restarts earlier.
        BattleScope.ResetAll();
        t.Tick.Reset();
        AssertEqual(0, t.Tick.Detector.Count, "no opening carries over");
        AssertEqual(0, t.Table.Count, "no exploit carries over");
        t.Now = 10.0;
        t.Ground = new FnGround((x, z) => false);
        for (int i = 0; i < 3; i++) t.Run();
        AssertEqual(0, t.Tick.Detector.Count, "ground the second battle never probed proves no gap (a completed lattice does not carry over)");
        AssertEqual(0, t.Table.Count, "and nothing launches");

        t.Ground = new AllPerceived();
        BattleScope.ResetAll();
        t.Tick.Reset();
        t.Now = 10.0;
        AssertTrue(t.RunToLaunch(), "battle 2 launches on its own ground");
        AssertEqual(firstOpeningId, t.Table.At(0).OpeningId, "opening ids start over");
        AssertEqual(firstExploitId, t.Table.At(0).Id, "exploit ids start over");
        AssertTrue(!t.Tick.Detector.Openings.ToArray().Any(o => o.FirstSeenHours > 20.0), "no opening is dated from battle 1's clock");
    }

    private static void ReviseStaticsRegistered()
    {
        // Behaviour: the feedback book and the reach buffers reset with the battle.
        ExploitFeedback.NotePlan(0, 3, SkipReason.PlayerOwned, 2);
        OwnReachBuffer.ForAlliance(0).Add(1f, 2f, 3f);
        BattleScope.ResetAll();
        AssertEqual(0, ExploitFeedback.TakePlayerSkips(0, 3), "the player-skip book is empty after a battle reset");
        AssertEqual(0, OwnReachBuffer.ForAlliance(0).Count, "and the reach buffer");

        // Sweep: every BC13 file that owns mutable static state registers its own reset (AGENTS § Scoped static state).
        string[] owners =
        {
            "Tactical/Battle/Producers/OpportunityIntentProducer.cs", "Tactical/Battle/ExploitLifecycle.cs",
            "Tactical/Battle/CommanderPersonalityRuntime.cs", "Tactical/Battle/PerceivedGroundRuntime.cs",
            "Tactical/Battle/PerceivedGroundLattice.cs", "Patches/VanillaStanceCooldownFixPatch.cs",
            "Patches/SeparatedEnemyOwnershipPatch.cs", "Patches/CavalryStrikeDisciplinePatch.cs",
        };
        foreach (string owner in owners)
            AssertContains(ReadSrc(owner), "BattleScope.Register(", owner + " registers a reset");
        // The tick's own reset reaches the detector.
        AssertContains(ReadSrc("Tactical/Battle/OpportunityTick.cs"), "_detector.Reset();", "OpportunityTick.Reset clears the detector");
    }

    // ------------------------------------------------------------------ RangeOnly reach

    private static void ReviseReachPerAlliance()
    {
        BattleScope.ResetAll();
        OwnReachBuffer own0 = OwnReachBuffer.ForAlliance(0), own1 = OwnReachBuffer.ForAlliance(1);
        AssertFalse(ReferenceEquals(own0, own1), "one buffer per alliance");
        AssertTrue(OwnReachBuffer.ForAlliance(2) == null && OwnReachBuffer.ForAlliance(-1) == null, "Europe (and no side) gets no buffer, none is created");
        AssertFalse(OwnReachBuffer.WithinFor(2, 0f, 0f), "and no reach: not perceived (fail closed)");
        var none = new RangeOnlyPerceivedGround();
        var held = new OwnReachBuffer();
        held.Add(0f, 0f, 500f);
        none.SetReach(held);
        AssertTrue(none.IsPerceived(0f, 0f), "control: a ground holding reach perceives its own point");
        none.SetReach(OwnReachBuffer.ForAlliance(2));
        AssertFalse(none.IsPerceived(0f, 0f), "a ground for such an alliance perceives nothing");
        own0.Add(0f, 0f, 100f);
        own1.Add(1000f, 0f, 100f);   // the second side's probe runs after the first side's, on the same tick
        var g0 = new RangeOnlyPerceivedGround();
        var g1 = new RangeOnlyPerceivedGround();
        g0.SetReach(own0);
        g1.SetReach(own1);
        AssertTrue(g0.IsPerceived(0f, 50f), "side 0 sees near its own unit");
        AssertFalse(g0.IsPerceived(1000f, 0f), "side 0 does not see by side 1's reach");
        AssertTrue(g1.IsPerceived(1000f, 50f), "side 1 sees near its own unit");
        AssertFalse(g1.IsPerceived(0f, 0f), "side 1 does not see by side 0's reach");
        string probe = ReadSrc("Tactical/Battle/BattlePerceptionProbe.cs");
        AssertFalse(probe.Contains("_ownReachCount") || probe.Contains("_ownX"), "the probe keeps no shared reach buffer");
        AssertContains(probe, "OwnReachBuffer.ForAlliance(s.Alliance)", "the probe fills the side's own buffer");
        AssertContains(probe, "OwnReachBuffer.WithinFor(s.Alliance, record.X, record.Z)", "and reads reach through the fail-closed lookup");
        AssertEqual(2, ReadSrc("Tactical/Battle/PerceivedGroundLattice.cs").Split(new[] { "new OwnReachBuffer()" }, StringSplitOptions.None).Length - 1, "a buffer is built only for the two sides");
        AssertContains(ReadSrc("Tactical/Battle/PerceivedGroundRuntime.cs"), "FillRangeOnlyGround(alliance, Range[alliance])", "and the oracle reads the side's");
    }

    // ------------------------------------------------------------------ playerSkips

    private static void RevisePlayerSkipsBook()
    {
        BattleScope.ResetAll();
        ExploitFeedback.NotePlan(0, 5, SkipReason.PlayerOwned, 0);
        ExploitFeedback.NotePlan(0, 5, SkipReason.None, 2);
        ExploitFeedback.NotePlan(1, 5, SkipReason.None, 3);
        ExploitFeedback.NotePlan(0, 0, SkipReason.PlayerOwned, 4);   // not an exploit's intent
        ExploitFeedback.NotePlan(0, 6, SkipReason.Duplicate, 0);      // a skip that is not the player's
        AssertEqual(3, ExploitFeedback.TakePlayerSkips(0, 5), "alliance 0's exploit 5: one owned skip and two excluded children");
        AssertEqual(3, ExploitFeedback.TakePlayerSkips(1, 5), "alliance 1's exploit 5 is a different exploit");
        AssertEqual(0, ExploitFeedback.TakePlayerSkips(0, 5), "read once");
        AssertEqual(0, ExploitFeedback.TakePlayerSkips(0, 6), "other skip reasons count nothing");
        AssertEqual(0, ExploitFeedback.TakePlayerSkips(0, 0), "no exploit id counts nothing");
    }

    private sealed class IntentCollector : IIntentSink
    {
        public readonly List<BattleOrderIntent> Intents = new List<BattleOrderIntent>();
        public void Submit(in BattleOrderIntent intent) => Intents.Add(intent);
    }

    private static void RevisePlayerSkipsIntent()
    {
        var sink = new IntentCollector();
        var request = new PostureIntentRequest
        {
            GroupId = 7, Action = PostureExecutionAction.SetFormationAndWaypoint, Task = CommandTaskType.AttackObjective,
            HasTarget = true, TargetX = 100f, TargetZ = 200f, TargetFormation = 1, NeedsFormation = true,
            Exploit = true, ExploitId = 5, Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Reason = "exploit-advance",
        };
        PostureIntentPlan.Submit(sink, in request);
        AssertTrue(sink.Intents.Any(i => i.Channel == OrderChannel.Move), "a Move");
        AssertTrue(sink.Intents.Any(i => i.Channel == OrderChannel.Formation), "a Formation");
        AssertTrue(sink.Intents.Count >= 2, "intents were submitted");
        foreach (BattleOrderIntent intent in sink.Intents)
            AssertEqual(5, intent.ExploitId, intent.Channel + " carries the exploit id");
        // The stamp itself (PosturePipeline asks this helper for the request's ExploitId).
        AssertEqual(5, ExploitPosture.IntentExploitId(true, true, 5), "an exploit asset with its order stamps the order's id");
        AssertEqual(0, ExploitPosture.IntentExploitId(false, true, 5), "a ledger order stamps none");
        AssertEqual(0, ExploitPosture.IntentExploitId(true, false, 5), "no order, none");
        AssertEqual(0, ExploitPosture.IntentExploitId(true, true, 0), "an order without an exploit id, none");
        AssertEqual(0, ExploitPosture.IntentExploitId(true, true, -1), "ExploitId -1 (not an exploit order), none");
        AssertContains(ReadSrc("Tactical/Battle/Producers/PostureIntentProducer.cs"), "ExploitId = ExploitPosture.IntentExploitId(exploit, hasOrder, order.ExploitId),", "the pipeline stamps through it");
        var ledger = new IntentCollector();
        var plain = request;
        plain.Exploit = false;
        plain.ExploitId = 0;
        PostureIntentPlan.Submit(ledger, in plain);
        foreach (BattleOrderIntent intent in ledger.Intents) AssertEqual(0, intent.ExploitId, "a ledger order carries none");
    }

    private sealed class OpenGuard2 : IOrderGuard
    {
        public bool CanWrite(in UnitRecord unit, out SkipReason reason) { reason = SkipReason.None; return true; }
    }

    private static void RevisePlayerSkipsChain()
    {
        BattleScope.ResetAll();
        // A group of two regiments: one AI, one the player's (exposed: no detached node between).
        var fx = new DetFixture();
        int group = fx.OwnGroup();
        int ai = fx.OwnInf(0f, 0f, 100f, group);
        int mine = fx.OwnInf(150f, 0f, 100f, group);
        UnitRecord rec = fx.Own[mine];
        rec.Flags |= UnitFlags.PlayerOwned;
        fx.Own[mine] = rec;
        BattleSnapshot s = fx.Build();
        var executor = new OrderExecutor();
        executor.Bind(new OpenGuard2(), null);
        executor.SetMapSize(2000f, 2000f);
        var intent = new BattleOrderIntent
        {
            GroupId = TickFx.IdOf(group), Channel = OrderChannel.Move, TargetX = 300f, TargetZ = 300f, Heading = -1f, Task = CommandTaskType.AttackObjective,
            Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Reason = "posture-executor", ExploitId = 7,
        };
        ExecutionPlan plan = executor.Plan(s, in intent, 1.0);
        AssertTrue(plan.Execute, "the group's order executes: " + plan.Skip);
        AssertTrue(plan.Targets.FannedOut, "it fans out around the player's regiment");
        AssertEqual(1, plan.Targets.Count, "only the AI regiment is a target");
        AssertEqual(1, plan.Targets.PlayerExcluded, "and the player's child is counted as excluded");
        ExploitFeedback.NotePlan(0, in intent, in plan);
        AssertEqual(1, ExploitFeedback.TakePlayerSkips(0, 7), "the exploit's playerSkips");

        // The asset itself player-owned: the executor skips it, which counts as one.
        UnitRecord g = fx.Own[group];
        g.Flags |= UnitFlags.PlayerOwned;
        fx.Own[group] = g;
        BattleSnapshot s2 = fx.Build();
        ExecutionPlan skipped = executor.Plan(s2, in intent, 2.0);
        AssertFalse(skipped.Execute, "a player-owned asset is skipped");
        AssertEqual(SkipReason.PlayerOwned, skipped.Skip, "for PlayerOwned");
        ExploitFeedback.NotePlan(0, in intent, in skipped);
        AssertEqual(1, ExploitFeedback.TakePlayerSkips(0, 7), "counted once");
        // A ledger order (no exploit id) is never counted.
        var ledgerOrder = intent;
        ledgerOrder.ExploitId = 0;
        ExploitFeedback.NotePlan(0, in ledgerOrder, in skipped);
        AssertEqual(0, ExploitFeedback.TakePlayerSkips(0, 0), "a ledger order counts nothing");
        AssertContains(ReadSrc("Tactical/Battle/BattleCoreTick.cs"), "ExploitFeedback.NotePlan(alliance, in _winners[w], in plan);", "the arbiter tick notes every winner's plan");
    }

    private static void RevisePlayerSkipsEndRow()
    {
        BattleScope.ResetAll();
        TickFx t = TickFx.Attacker();
        AssertTrue(t.RunToLaunch(), "launched");
        ExploitEntry e = t.Table.At(0);
        ExploitFeedback.NotePlan(0, e.Id, SkipReason.PlayerOwned, 0);
        ExploitFeedback.NotePlan(0, e.Id, SkipReason.None, 2);
        t.World.Safety.Add(e.AssetId);
        t.Run();
        AssertEqual(1, t.Sink.Ends.Count, "ended");
        AssertEqual(3, t.Sink.Ends[0].PlayerSkips, "the end row carries the executor skip and the excluded children");
        AssertEqual(0, ExploitFeedback.TakePlayerSkips(0, e.Id), "and the book was drained");
        // A stand-down drains too.
        BattleScope.ResetAll();
        TickFx u = TickFx.Attacker();
        AssertTrue(u.RunToLaunch(), "launched again");
        ExploitFeedback.NotePlan(0, u.Table.At(0).Id, SkipReason.None, 4);
        OpportunityTick.EndAll(u.Table, ExploitEnd.StandDown, u.Now, u.Sink);
        AssertEqual(4, u.Sink.Ends[0].PlayerSkips, "a stand-down end row carries it too");
        AssertContains(ReadSrc("Tactical/Battle/Producers/OpportunityIntentProducer.cs"), ".WithField(\"playerSkips\", e.PlayerSkips)", "the TacticalExploit end row emits it");
    }

    // ------------------------------------------------------------------ #119 row, gates

    private static void ReviseStancePlan()
    {
        StanceCooldownPlan on = StanceCooldownPlan.Decide(true, false);
        AssertTrue(on.Restamp && on.FixActing && !on.ReleaseOnRetreat, "flag on, no retreat: re-stamp");
        AssertEqual("restamp", on.Reason, "reason");
        StanceCooldownPlan off = StanceCooldownPlan.Decide(false, false);
        AssertFalse(off.Restamp || off.FixActing || off.ReleaseOnRetreat, "flag off: nothing is written");
        AssertEqual("flag-off", off.Reason, "the row says the flag is off");
        StanceCooldownPlan offRetreat = StanceCooldownPlan.Decide(false, true);
        AssertFalse(offRetreat.Restamp || offRetreat.ReleaseOnRetreat, "flag off under a retreat: nothing");
        AssertEqual("flag-off", offRetreat.Reason, "the flag wins the label");
        StanceCooldownPlan retreat = StanceCooldownPlan.Decide(true, true);
        AssertTrue(retreat.ReleaseOnRetreat && !retreat.Restamp && !retreat.FixActing, "macroai 3: release, no re-stamp, the fix is not acting");
        AssertEqual("retreat", retreat.Reason, "the row says retreat");
        // The patch counts vanilla's changes and emits the row in every case (it no longer returns before them).
        string patch = ReadSrc("Patches/VanillaStanceCooldownFixPatch.cs");
        AssertFalse(patch.Contains("|| __instance.macroai == 3) return;"), "the Postfix does not stop at macroai 3");
        AssertContains(patch, "EmitRowIfDue(side, book, doctrine, plan);", "the row follows the plan");
        AssertContains(patch, "StanceCooldownPlan.Decide(", "the plan decides");
    }

    private static void ReviseFixGate()
    {
        foreach (BattleFixKind kind in Enum.GetValues(typeof(BattleFixKind)))
        {
            bool serves = BattleFixGate.ServesWhiskeyCommand(kind);
            AssertEqual(kind == BattleFixKind.CavalryStrike || kind == BattleFixKind.SeparatedEnemy, serves, kind + " scope");
            AssertTrue(BattleFixGate.Runs(kind, true, true, true), kind + " runs in Active");
            AssertEqual(!serves, BattleFixGate.Runs(kind, true, true, false), kind + (serves ? " stops outside Active" : " runs in Off and MonitorOnly too"));
            AssertFalse(BattleFixGate.Runs(kind, true, false, true), kind + " stops with its flag off");
            AssertFalse(BattleFixGate.Runs(kind, false, true, true), kind + " stops with the plugin off");
        }
        // Each patch asks the gate under its own kind (a swapped kind would change its Commander Mode rule).
        foreach (var (patch, kind) in new[]
        {
            ("VanillaStanceCooldownFixPatch", "StanceCooldown"), ("MacroAssaultUpgradeFixPatch", "MacroUpgrade"), ("ArtilleryChainSideFixPatch", "ArtillerySide"),
            ("ReserveSupportPickFixPatch", "ReserveSupportPick"), ("CavalryStrikeDisciplinePatch", "CavalryStrike"),
        })
            AssertContains(ReadSrc("Patches/" + patch + ".cs"), "BattleFixGate.Runs(BattleFixKind." + kind + ",", patch + " decides through the gate as " + kind);
    }

    private static SeparatedEnemySteerInput SteerInput() => new SeparatedEnemySteerInput
    {
        HasInstanceAndGroup = true, SideDrivenByOrchestrator = true, FeudInstance = false, SideInRange = true, InAiPool = true,
        HasDoctrineState = true, CanWrite = true, FlagOn = true, CommanderModeAllowsWrites = true,
    };

    private static void ReviseSeparatedGate()
    {
        SeparatedEnemySteerInput all = SteerInput();
        AssertTrue(SeparatedEnemySteerGate.Suppress(in all) && SeparatedEnemySteerGate.WhiskeyCommanded(in all), "a Whiskey-commanded group is suppressed");
        var player = SteerInput(); player.CanWrite = false;   // the player's unit, or one with an exposed player descendant
        AssertFalse(SeparatedEnemySteerGate.WhiskeyCommanded(in player), "the write guard refuses a player's group");
        AssertFalse(SeparatedEnemySteerGate.Suppress(in player), "so it is never suppressed");
        AssertTrue(SeparatedEnemySteerGate.Tallied(in player), "but vanilla's launch is still tallied");
        var vanillaOwned = SteerInput(); vanillaOwned.HasDoctrineState = false;
        AssertFalse(SeparatedEnemySteerGate.Suppress(in vanillaOwned), "a group the ledger does not command is vanilla's");
        var outsidePool = SteerInput(); outsidePool.InAiPool = false;
        AssertFalse(SeparatedEnemySteerGate.Suppress(in outsidePool), "a group outside the AI pool is vanilla's");
        var feud = SteerInput(); feud.FeudInstance = true;
        AssertFalse(SeparatedEnemySteerGate.Suppress(in feud) || SeparatedEnemySteerGate.Tallied(in feud), "the feud instance is untouched");
        var undriven = SteerInput(); undriven.SideDrivenByOrchestrator = false;
        AssertFalse(SeparatedEnemySteerGate.Suppress(in undriven), "a side without an orchestrator is untouched");
        var flagOff = SteerInput(); flagOff.FlagOn = false;
        AssertFalse(SeparatedEnemySteerGate.Suppress(in flagOff), "flag off: vanilla runs");
        AssertTrue(SeparatedEnemySteerGate.WhiskeyCommanded(in flagOff), "the group is still classified for the tally");
        var monitor = SteerInput(); monitor.CommanderModeAllowsWrites = false;
        AssertFalse(SeparatedEnemySteerGate.Suppress(in monitor), "outside Active the steer does not act");
        var absent = SteerInput(); absent.HasInstanceAndGroup = false;
        AssertFalse(SeparatedEnemySteerGate.Suppress(in absent), "no instance or group");
        AssertContains(ReadSrc("Patches/SeparatedEnemyOwnershipPatch.cs"), "input.CanWrite = TacticalWriteGuard.CanWrite(group);", "the write guard term is read");
    }

    private static CavalryStrikeScope CavScope() => new CavalryStrikeScope
    {
        InsideAdjustCavalry = true, HasVanillaResult = true, FlagOn = true, ChargerIsCavalry = true, SideDrivenByOrchestrator = true,
        FeudInstance = false, CanWrite = true, FogHonestPerceptionOn = true, HasSnapshot = true, ChargerInSnapshot = true,
    };

    private static void ReviseCavalryGate()
    {
        CavalryStrikeScope all = CavScope();
        AssertTrue(CavalryStrikeGate.Applies(in all), "every term holds: the discipline acts");
        var noWrite = CavScope(); noWrite.CanWrite = false;
        AssertFalse(CavalryStrikeGate.Applies(in noWrite), "a player's regiment (or Commander Mode not Active) keeps vanilla's pick");
        var feud = CavScope(); feud.FeudInstance = true;
        AssertFalse(CavalryStrikeGate.Applies(in feud), "feud instance");
        var infantry = CavScope(); infantry.ChargerIsCavalry = false;
        AssertFalse(CavalryStrikeGate.Applies(in infantry), "only cavalry");
        var flagOff = CavScope(); flagOff.FlagOn = false;
        AssertFalse(CavalryStrikeGate.Applies(in flagOff), "flag off");
        var noFog = CavScope(); noFog.FogHonestPerceptionOn = false;
        AssertFalse(CavalryStrikeGate.Applies(in noFog), "without fog-honest perception it fails open");
        var noSnapshot = CavScope(); noSnapshot.HasSnapshot = false;
        AssertFalse(CavalryStrikeGate.Applies(in noSnapshot), "no snapshot: fail open");
        var missing = CavScope(); missing.ChargerInSnapshot = false;
        AssertFalse(CavalryStrikeGate.Applies(in missing), "charger missing from the snapshot: fail open");
        var outside = CavScope(); outside.InsideAdjustCavalry = false;
        AssertFalse(CavalryStrikeGate.Applies(in outside), "outside AdjustCavalryBehavior");
        var undriven = CavScope(); undriven.SideDrivenByOrchestrator = false;
        AssertFalse(CavalryStrikeGate.Applies(in undriven), "a side without an orchestrator");

        AssertTrue(CavalryStrikeCheck.TargetSeen(true, ObservationState.Visible), "a Visible observation is seen");
        AssertFalse(CavalryStrikeCheck.TargetSeen(true, ObservationState.Remembered), "a Remembered one is not (hidden)");
        AssertFalse(CavalryStrikeCheck.TargetSeen(false, ObservationState.Visible), "no observation at all is hidden");
    }

    private static void ReviseFollowRule()
    {
        AssertFalse(ExploitPosture.SubmitsMoveOrFormation(ExploitOverrideKind.Follow), "a follower submits no Move or Formation");
        AssertTrue(ExploitPosture.SubmitsMoveOrFormation(ExploitOverrideKind.Exploit), "the asset does");
        AssertTrue(ExploitPosture.SubmitsMoveOrFormation(ExploitOverrideKind.None), "and so does a ledger order");
    }

    // ------------------------------------------------------------------ player and feud nodes

    private static void RevisePlayerNodes()
    {
        var fx = new DetFixture();
        int army = fx.OwnGroup();
        Retype(fx, army, TacticalUnitType.BattleGroupArmy, 0);
        UnitFlags[] oddities = { UnitFlags.PlayerOwned, UnitFlags.PlayerCommand, UnitFlags.Feud, UnitFlags.Routed };
        foreach (UnitFlags odd in oddities)
        {
            int div = fx.OwnGroup(army);
            Retype(fx, div, TacticalUnitType.BattleGroupDivision, odd);
        }
        int normal = fx.OwnGroup(army);
        Retype(fx, normal, TacticalUnitType.BattleGroupDivision, 0);
        BattleSnapshot s = fx.Build();
        var nodes = new int[8];
        var depths = new int[8];
        int n = OpportunityDoctrine.CollectDecidingNodes(s, nodes, depths);
        AssertEqual(1, n, "only the plain division decides");
        AssertEqual(normal, nodes[0], "the plain division");
    }

    private static void Retype(DetFixture fx, int index, int unitTyp, UnitFlags extra)
    {
        UnitRecord r = fx.Own[index];
        r.UnitTyp = unitTyp;
        r.Flags |= extra;
        fx.Own[index] = r;
    }

    private static void ReviseFeudDescendant()
    {
        BattleScope.ResetAll();
        TickFx t = TickFx.Attacker();
        t.Patch(t.Regiment[0], (ref UnitRecord r) => { r.Flags |= UnitFlags.Feud; });
        AssertTrue(t.RunToLaunch(), "another brigade launches");
        AssertTrue(t.Table.At(0).AssetId != t.BrigadeId(0), "the brigade with a Feud regiment is not the asset");
        AssertFalse(t.Table.IsExploiting(t.BrigadeId(0)), "and is not in an exploit");
    }

    // ------------------------------------------------------------------ detector fixtures

    private static void ReviseReinforcement()
    {
        var fx = GapLine();
        fx.Obs.Add(new EnemyObservation
        {
            InstanceId = 9001, UnitTyp = TacticalUnitType.Infantry, X = 200f, Z = 850f, Strength = 500f, State = ObservationState.Visible,
            Confidence01 = 1f, Morale = 0.8f, IsReinforcement = true,
        });
        var d = new OpeningDetector();
        Detect(d, fx);
        AssertEqual(1, CountKind(d, OpeningKind.Gap), "a column still on the map's edge stands in no corridor");
        var control = GapLine();
        control.Inf(200f, 850f);
        var d2 = new OpeningDetector();
        Detect(d2, control);
        AssertEqual(0, CountKind(d2, OpeningKind.Gap), "control: the same unit as a regiment closes the gap");
    }

    private static void ReviseRememberedRow()
    {
        var fx = new DetFixture();
        fx.Inf(0f, 600f);
        int id = fx.Remembered(TacticalUnitType.Infantry, 400f, 600f, confidence: 0.9f);
        // The true battle state has a very narrow regiment there; the side only remembers one, so the default frontage applies.
        fx.Rows.Add(new UnitRecord { InstanceId = id, UnitTyp = TacticalUnitType.Infantry, X = 400f, Z = 600f, UnitWidth = 40f, Morale = 0.8f,
            Flags = UnitFlags.Alive | UnitFlags.Active, ParentIndex = -1 });
        var d = new OpeningDetector();
        Detect(d, fx);
        AssertEqual(1, CountKind(d, OpeningKind.Gap), "the remembered pair frames a gap");
        AssertNear(300f, First(d, OpeningKind.Gap).Width, 1e-3f, "its width uses the default frontage, never the true row's");
    }

    // ------------------------------------------------------------------ minors

    private static void ReviseChildIds()
    {
        string[] samples = { "child-12345", "child--98765", "child-0", "child-", "child--", "child-12a", "node-5", null, "", "child-2147483647", "child--2147483648", "child-2147483648", "child-99999999999", "child- 5" };
        foreach (string text in samples)
        {
            int expected = 0;
            if (text != null && text.StartsWith("child-", StringComparison.Ordinal) && int.TryParse(text.Substring(6), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int parsed)) expected = parsed;
            AssertEqual(expected, DirectChildIds.InstanceId(text), "DirectChildIds.InstanceId(" + (text ?? "null") + ")");
            AssertEqual(expected, ArmyOrchestrator.ChildInstanceId(text), "ArmyOrchestrator.ChildInstanceId(" + (text ?? "null") + ")");
        }
        string a = "child-123456", b = "child--98765";
        DirectChildIds.InstanceId(a);
        ArmyOrchestrator.ChildInstanceId(b);
        long before = TestFixtures.BeginAllocationWindow();
        int sum = 0;
        for (int i = 0; i < 10000; i++) sum += DirectChildIds.InstanceId(a) + ArmyOrchestrator.ChildInstanceId(b);
        long bytes = TestFixtures.AllocatedSince(before);
        AssertEqual(10000 * (123456 - 98765), sum, "sum");
        AssertEqual(0L, bytes, "parsing a child id allocated bytes");
        AssertContains(ReadSrc("Tactical/Orchestrator/Plays/PlayExecutorRuntime.cs"), "ParseChildInstanceId(string childId) => DirectChildIds.InstanceId(childId);", "the play runtime parses through the same allocation-free reader");
        AssertFalse(ReadSrc("Tactical/Orchestrator/ArmyOrchestrator.cs").Contains("childId.Substring("), "and the orchestrator does not allocate substrings");
    }

    private static void ReviseArmyAggression()
    {
        foreach (int alliance in new[] { 0, 1 })
        {
            PersonalityVector ranked = CommanderPersonalityDerivation.Default(alliance, EchelonKind.Army);
            float expected = CommanderPersonalityDerivation.To01(ranked.Aggression);
            AssertNear(expected, CommanderPersonalityDerivation.ArmyAggression01(false, default, alliance), 1e-6f, "alliance " + alliance + ": no table entry reads the ranked Default");
            var own = new PersonalityVector(0.9f, 0f, 0f, 0f, 0f);
            AssertNear(CommanderPersonalityDerivation.To01(0.9f), CommanderPersonalityDerivation.ArmyAggression01(true, in own, alliance), 1e-6f, "a table entry wins");
        }
        bool differs = false;
        foreach (int alliance in new[] { 0, 1 })
            differs |= Math.Abs(CommanderPersonalityDerivation.ArmyAggression01(false, default, alliance) - 0.5f) > 1e-4f;
        AssertTrue(differs, "the ranked Default is not the flat 0.5 for at least one side");
        AssertContains(ReadSrc("Patches/BattleMacroStrategyPatch.cs"), "CommanderPersonalityDerivation.ArmyAggression01(found, in vector, bunits.alliance[side])", "#44 uses it");
    }

    private static void ReviseOnceLogGuard()
    {
        OnceLog.Reset();
        const string key = "tactical-battle-core:revise-guard";
        AssertFalse(OnceLog.AlreadyLogged(key), "not fired yet");
        OnceLog.Info(key, "first");
        AssertTrue(OnceLog.AlreadyLogged(key), "fired");
        OnceLog.AlreadyLogged(key);
        long before = TestFixtures.BeginAllocationWindow();
        for (int i = 0; i < 1000; i++) OnceLog.AlreadyLogged(key);
        AssertEqual(0L, TestFixtures.AllocatedSince(before), "asking allocates nothing");
        string producer = ReadSrc("Tactical/Battle/Producers/OpportunityIntentProducer.cs");
        AssertContains(producer, "!OnceLog.AlreadyLogged(\"tactical-battle-core:opportunity-first-opening\")", "the opening line is built only when it logs");
        AssertContains(producer, "!OnceLog.AlreadyLogged(\"tactical-battle-core:opportunity-first-exploit\")", "and the exploit line");
        AssertContains(ReadSrc("Patches/CavalryStrikeDisciplinePatch.cs"), "!OnceLog.AlreadyLogged(\"tactical-battle-core:cavalry-strike-first-refusal\")", "and the refusal line");
        OnceLog.Reset();
    }
}
