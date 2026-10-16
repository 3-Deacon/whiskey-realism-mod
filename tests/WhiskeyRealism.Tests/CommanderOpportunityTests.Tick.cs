using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;
using static TestHarness;

// BC13 task 7, the pure half of the opportunity producer: OpportunityTick over a snapshot fixture, a fake world and a recording
// sink (spec § 6, requirement-opportunity-producer). The runtime shell (OpportunityIntentProducer) only supplies the world, the
// perceived-ground oracle and the telemetry sink.
internal static partial class CommanderOpportunityTests
{
    private static (string, Action)[] TickTests()
    {
        return new (string, Action)[]
        {
            ("bc13 OPP-TICK a confirmed gap is launched on a reserve brigade, claimed, bound and committed", TickLaunch),
            ("bc13 OPP-TICK the launched exploit runs Advance, Strike, Consolidate to Success and is collected", TickRunsToSuccess),
            ("bc13 OPP-TICK a Safety claim aborts the exploit and the commander is hesitant for that opening", TickSafetyAbort),
            ("bc13 OVR-FEEDBACK the flank emergency set by the posture pipeline aborts on the next tick", TickFlankFeedback),
            ("bc13 LIFE-ABORTS a crisis release of the asset's parent line ends the exploit as line-crisis", TickCrisisAbort),
            ("bc13 OVR-STANDDOWN a stand-down ends every live exploit and empties the table", TickStandDown),
            ("bc13 OVR-NEWDAY a new battle day ends every live exploit and forgets the decision state", TickNewDay),
            ("bc13 OPP-PLAYER a player-owned descendant excludes the asset; the next reserve is chosen", TickPlayerAsset),
            ("bc13 OPP-PHASE the Withdraw phase launches nothing and ends a live exploit", TickWithdraw),
            ("bc13 COUNTER-ASSET a Defender launches a Penetration counterstroke on a second-line reserve", TickDefender),
            ("bc13 OPP-TICK a stand-by tick with no confirmed opening submits nothing and holds no entry", TickIdle),
            ("bc13 OPP-ROLL a roll-miss passes the opening over for 0.1 h before the same commander decides on it again", TickRollMissHesitates),
            ("bc13 OPP-INTEGRITY-SIBLING two divisions deciding in one tick: the second sees the first's launch in the commitment ledger", TickSiblingLedger),
        };
    }

    // ------------------------------------------------------------------ fixtures

    private sealed class FakeWorld : IOpportunityWorld
    {
        public BattleStance Stance { get; set; } = BattleStance.Attacker;
        public BattlePhase PlanPhase { get; set; } = BattlePhase.MainEffort;
        public int MacroAi { get; set; } = 1;
        public int MainEffortSector { get; set; } = 2;
        public int BattleDay { get; set; } = 1;
        public double SideStatCycleHours { get; set; } = 0.01;
        public float CavalryMoraleBar { get; set; } = 0.72f;
        public OpportunityPersonality Persona = new OpportunityPersonality
        {
            Agg01 = 0.9f, Caution01 = 0.5f, Audacity01 = 0.9f, CasualtyTolerance01 = 0.5f, Duty = 1f, Source = PersonalitySource.Registry,
        };
        public readonly HashSet<int> Safety = new HashSet<int>(), GaveUp = new HashSet<int>(), Crisis = new HashSet<int>();
        public readonly Dictionary<int, (CommandNodeRole, CommandTaskType)> Roles = new Dictionary<int, (CommandNodeRole, CommandTaskType)>();

        public bool TryPersonality(int commanderIndex, out OpportunityPersonality personality)
        {
            personality = Persona;
            return true;
        }
        public bool DrivenByPosture(int groupId) => true;
        public bool TryRoleTask(int groupId, out CommandNodeRole role, out CommandTaskType task)
        {
            if (Roles.TryGetValue(groupId, out var rt))
            {
                role = rt.Item1;
                task = rt.Item2;
                return true;
            }
            role = CommandNodeRole.Unknown;
            task = CommandTaskType.None;
            return false;
        }
        public bool IsMainEffortChild(int armyChildGroupId) => false;
        public bool SafetyClaim(int groupId) => Safety.Contains(groupId);
        public bool RecentExecutorMove(int groupId, double nowHours) => false;
        public bool UnderManeuver(int groupId) => false;
        public bool UnderRecon(int groupId) => false;
        public bool UnderProbeHold(int groupId) => false;
        public bool UnderArrivalHold(int groupId) => false;
        public bool AdvanceGaveUp(int groupId) => GaveUp.Contains(groupId);
        public bool ParentCrisisRelease(int groupId) => Crisis.Contains(groupId);
    }

    private sealed class AnchoredTerrain : IOpeningTerrain
    {
        public bool RiverWithin(float x, float z, float dirX, float dirZ, float meters) => true;
        public bool CrestWithin(float x, float z, float meters) => false;
        public bool SegmentCrossesRiver(float x0, float z0, float x1, float z1) => false;
    }

    private sealed class RecordingSink : IOpportunitySink
    {
        public readonly List<ExploitEntry> Launches = new List<ExploitEntry>();
        public readonly List<ExploitEntry> Ends = new List<ExploitEntry>();
        public readonly List<string> Declines = new List<string>();
        public readonly List<ExploitPhase> Phases = new List<ExploitPhase>();
        public int Summaries;
        public void Launched(in ExploitEntry entry, in DecisionResult result, in OpportunityPersonality personality, int live) => Launches.Add(entry);
        public void Declined(int nodeId, int commanderIndex, in DecisionResult result, in OpportunityPersonality personality, int live) => Declines.Add(result.Reason + "@" + result.AssetId + "/" + result.OpeningId);
        public void PhaseChanged(in ExploitEntry entry, ExploitPhase from, int live) => Phases.Add(entry.Phase);
        public void Ended(in ExploitEntry entry, double nowHours, bool leftSector, int live) => Ends.Add(entry);
        public void OpeningSummary(OpportunityTick tick, ExploitTable table, double nowHours) => Summaries++;
    }

    // An army, one division of five brigades in a line at depth 0 (laterals -250 .. 750, 130 m between regiment edges), two enemy
    // regiments at depth 300 with a 200 m gap between them at lateral 150 (300 m apart: not isolated clusters, which would compete). Brigade 2 (lateral 250) and the two beyond it are the
    // division's reserve; the first two hold the line.
    private sealed class TickFx
    {
        public readonly DetFixture Fx = new DetFixture();
        public readonly FakeWorld World = new FakeWorld();
        public readonly OpportunityTick Tick = new OpportunityTick();
        public readonly RecordingSink Sink = new RecordingSink();
        public readonly ExploitTable Table = ExploitTable.ForAlliance(0);
        public int Army, Div;
        public readonly int[] Brigade = new int[5], Regiment = new int[5];
        // Every flank end is river-anchored, so the only opening in the fixtures is the gap (no FlankInAir competes for the asset).
        public IOpeningTerrain Terrain = new AnchoredTerrain();
        public IPerceivedGround Ground = new AllPerceived();
        public OpeningRules Rules = OpeningRules.Shipped;
        public double Now = 10.0;

        public static TickFx Attacker()
        {
            var t = new TickFx();
            t.Army = t.Fx.OwnGroup();
            t.Patch(t.Army, (ref UnitRecord r) => { r.UnitTyp = TacticalUnitType.BattleGroupArmy; });
            t.Div = t.Fx.OwnGroup(t.Army);
            t.Patch(t.Div, (ref UnitRecord r) => { r.UnitTyp = TacticalUnitType.BattleGroupDivision; r.Commander = ReliableCommander; r.GroupMorale = 0.8f; });
            for (int i = 0; i < 5; i++)
            {
                float lateral = -250f + 250f * i;
                t.Brigade[i] = t.Fx.OwnGroup(t.Div);
                t.Patch(t.Brigade[i], (ref UnitRecord r) => { r.UnitTyp = TacticalUnitType.BattleGroupBrigade; r.Commander = 100 + i; r.GroupMorale = 0.8f; r.X = lateral; r.Z = 0f; r.GroupWidth = 120f; });
                t.Regiment[i] = t.Fx.OwnInf(lateral, 0f, 120f, t.Brigade[i]);
                t.Patch(t.Regiment[i], (ref UnitRecord r) => { r.Strength = 2000; });
                bool holds = i < 2;
                t.World.Roles[IdOf(t.Brigade[i])] = holds
                    ? (CommandNodeRole.MainEffort, CommandTaskType.AttackObjective)
                    : (CommandNodeRole.Reserve, CommandTaskType.ReserveWait);
            }
            t.Fx.Inf(0f, 300f);
            t.Fx.Inf(300f, 300f);
            return t;
        }

        // A commander whose first eight decision rolls on opening 1 all sit below 0.9 (the chance is clamped to 0.95), so the
        // tests do not depend on a roll-miss that the deterministic hash happens to deal.
        public static readonly int ReliableCommander = FindReliableCommander();

        private static int FindReliableCommander()
        {
            for (int c = 40; c < 5000; c++)
            {
                bool ok = true;
                for (int n = 0; n < 8 && ok; n++) ok = OpportunityDoctrine.Roll(7, c, 1, n) < 0.9f;
                if (ok) return c;
            }
            throw new Exception("no reliable commander index");
        }

        public static int IdOf(int ownIndex) => ownIndex + 1;
        public int BrigadeId(int i) => IdOf(Brigade[i]);

        public void Patch(int index, PatchRecord edit)
        {
            UnitRecord r = Fx.Own[index];
            edit(ref r);
            Fx.Own[index] = r;
        }

        public delegate void PatchRecord(ref UnitRecord record);

        public void Move(int brigade, float x, float z)
        {
            Patch(Brigade[brigade], (ref UnitRecord r) => { r.X = x; r.Z = z; });
            Patch(Regiment[brigade], (ref UnitRecord r) => { r.X = x; r.Z = z; });
        }

        public void Run(double step = 0.03125)
        {
            Now += step;
            BattleSnapshot s = Fx.Build(Now);
            Tick.Run(s, World, Ground, Terrain, in Rules, Table, Sink);
        }

        // Runs until an exploit is live or the limit; true if one launched.
        public bool RunToLaunch(int maxTicks = 10)
        {
            for (int i = 0; i < maxTicks && Table.Count == 0; i++) Run();
            return Table.Count > 0;
        }
    }

    // ------------------------------------------------------------------ tests

    private static void TickLaunch()
    {
        BattleScope.ResetAll();
        TickFx t = TickFx.Attacker();
        AssertTrue(t.RunToLaunch(), "a confirmed gap is launched within a few ticks");
        AssertEqual(1, t.Table.Count, "one live exploit");
        ExploitEntry e = t.Table.At(0);
        AssertEqual(OpeningKind.Gap, e.Kind, "a gap");
        AssertEqual(ExploitPhase.Advance, e.Phase, "starts in Advance");
        AssertEqual(t.BrigadeId(0), e.AssetId, "equal scores go to the lowest id: the first brigade, which leaves the line end and opens no gap");
        AssertEqual(TickFx.IdOf(t.Div), e.DecidingNodeId, "decided by the division");
        AssertEqual(TickFx.ReliableCommander, e.CommanderIndex, "the division commander");
        AssertEqual(1, t.Sink.Launches.Count, "one launch row");
        AssertTrue(t.Table.IsExploiting(TickFx.IdOf(t.Div)), "the division is an ancestor of a live exploit");
        AssertTrue(t.Table.IsExploiting(t.BrigadeId(0)), "the asset");
        AssertFalse(t.Table.IsExploiting(t.BrigadeId(3)), "a sibling is not");
        AssertTrue(t.Table.TryOverride(t.BrigadeId(0), out CommandDoctrineOrder order, out _), "the asset is overridden");
        AssertEqual("exploit-advance", order.Reason, "advance order");
        AssertTrue(t.Tick.Detector.Count >= 1, "the detector holds the opening");
        Opening gap = First(t.Tick.Detector, OpeningKind.Gap);
        AssertTrue(gap.Claimed, "the opening is claimed by the exploit");

        // One exploit per node; the concurrency cap and the node cap hold on later ticks.
        for (int i = 0; i < 8; i++) t.Run();
        AssertTrue(t.Table.Count <= 1, "the deciding node has at most one live exploit");
    }

    private static void TickRunsToSuccess()
    {
        BattleScope.ResetAll();
        TickFx t = TickFx.Attacker();
        AssertTrue(t.RunToLaunch(), "launched");
        t.Run();
        t.Move(0, 250f, 300f);   // 141 m from the aim (150, 400): Strike
        t.Run();
        AssertEqual(ExploitPhase.Strike, t.Table.At(0).Phase, "within 150 m of the aim: Strike");
        t.Move(0, 150f, 380f);   // at the aim, 180 m from the nearest enemy: held
        t.Run();
        AssertEqual(ExploitPhase.Consolidate, t.Table.At(0).Phase, "held: Consolidate");
        for (int i = 0; i < 8 && t.Sink.Ends.Count == 0; i++) t.Run();
        AssertEqual(1, t.Sink.Ends.Count, "one end row");
        AssertEqual(ExploitEnd.Success, t.Sink.Ends[0].End, "Success");
        AssertTrue(t.Table.Count == 0 || t.Table.At(0).AssetId != t.BrigadeId(0), "the ended entry was collected (the freed opening may be taken by another asset)");
        AssertTrue(t.Sink.Phases.Contains(ExploitPhase.Strike) && t.Sink.Phases.Contains(ExploitPhase.Consolidate), "the phase rows");
        AssertTrue(t.Table.InCooldown(t.BrigadeId(0), t.Now), "the asset cools down");
        Opening gap = First(t.Tick.Detector, OpeningKind.Gap);
        AssertTrue(!gap.Claimed || t.Table.Count > 0, "the claim was released (or another asset took the opening)");
        AssertTrue(t.Sink.Launches.Count <= 2, "at most one more launch took the released opening");
    }

    private static void TickSafetyAbort()
    {
        BattleScope.ResetAll();
        TickFx t = TickFx.Attacker();
        AssertTrue(t.RunToLaunch(), "launched");
        int assetId = t.Table.At(0).AssetId;
        t.World.Safety.Add(assetId);
        double abortAt = t.Now + 0.03125;
        t.Run();
        AssertEqual(0, t.Table.Count, "the Safety claim aborted and the entry was collected");
        AssertEqual(ExploitEnd.Safety, t.Sink.Ends[0].End, "safety");
        AssertEqual(1, t.Sink.Launches.Count, "one launch so far");

        // Hesitant for 0.1 h: the next reserve (not in cooldown) is not sent at the same opening within the window.
        t.World.Safety.Clear();
        while (t.Now < abortAt + 0.09)
        {
            t.Run();
            AssertEqual(0, t.Table.Count, "no relaunch while the commander hesitates over that opening (t=" + t.Now + ")");
        }
        for (int i = 0; i < 14 && t.Table.Count == 0; i++) t.Run();
        AssertEqual(1, t.Table.Count, "after the hesitation another reserve is sent (declines " + string.Join(",", t.Sink.Declines) + ")");
        AssertTrue(t.Table.At(0).AssetId != assetId, "not the asset in cooldown (asset " + t.Table.At(0).AssetId + " vs " + assetId + " now " + t.Now + " abortAt " + abortAt + " cool " + t.Table.InCooldown(assetId, t.Now) + " declines " + string.Join(",", t.Sink.Declines) + " openings " + Describe(t.Tick.Detector) + ")");
    }

    private static string Describe(OpeningDetector d)
    {
        var sb = new System.Text.StringBuilder();
        foreach (Opening o in d.Openings.ToArray()) sb.Append(o.Id).Append(':').Append(o.Kind).Append('@').Append(o.AimX).Append(',').Append(o.AimZ).Append(o.Confirmed ? "C" : "u").Append(o.Claimed ? "!" : "").Append(' ');
        return sb.ToString();
    }

    private static void TickFlankFeedback()
    {
        BattleScope.ResetAll();
        TickFx t = TickFx.Attacker();
        AssertTrue(t.RunToLaunch(), "launched");
        ExploitFeedback.SetFlankEmergency(t.Table.At(0).AssetId);
        t.Run();
        AssertEqual(0, t.Table.Count, "collected");
        AssertEqual(ExploitEnd.FlankThreat, t.Sink.Ends[0].End, "the flank emergency aborts as flank-threat");
        BattleScope.ResetAll();
    }

    private static void TickCrisisAbort()
    {
        BattleScope.ResetAll();
        TickFx t = TickFx.Attacker();
        AssertTrue(t.RunToLaunch(), "launched");
        t.World.Crisis.Add(t.Table.At(0).AssetId);
        t.Run();
        AssertEqual(ExploitEnd.LineCrisis, t.Sink.Ends[0].End, "the allocator's crisis release of the parent line aborts as line-crisis");
        AssertFalse(t.Sink.Ends[0].LeftSector, "it is the allocator's, not the asset's departure opening a gap");

        string src = ReadSrc("Tactical/Battle/Producers/OpportunityIntentProducer.cs");
        AssertContains(src, "_army.ReserveRelease.CrisisReleased", "the runtime world reads the allocator's published crisis releases");
        string army = ReadSrc("Tactical/Orchestrator/ArmyOrchestrator.cs");
        AssertContains(army, "exploiting[i] = instanceId != 0 && exploits.IsExploiting(instanceId);", "the allocator is told which children have a live exploit");
        AssertContains(army, "_reserveRelease.Clear();", "a plan change clears the release state");
        BattleScope.ResetAll();
    }

    private static void TickStandDown()
    {
        BattleScope.ResetAll();
        TickFx t = TickFx.Attacker();
        AssertTrue(t.RunToLaunch(), "launched");
        OpportunityTick.EndAll(t.Table, ExploitEnd.StandDown, t.Now, t.Sink);
        AssertEqual(0, t.Table.Count, "the table is empty");
        AssertEqual(1, t.Sink.Ends.Count, "an end row per live exploit");
        AssertEqual(ExploitEnd.StandDown, t.Sink.Ends[0].End, "stand-down");
        AssertFalse(t.Table.TryOverride(t.Sink.Ends[0].AssetId, out _, out _), "no override after");
        OpportunityTick.EndAll(t.Table, ExploitEnd.StandDown, t.Now, t.Sink);
        AssertEqual(1, t.Sink.Ends.Count, "an empty table emits nothing");
    }

    private static void TickNewDay()
    {
        BattleScope.ResetAll();
        TickFx t = TickFx.Attacker();
        AssertTrue(t.RunToLaunch(), "launched");
        t.World.BattleDay = 2;
        t.Run();
        AssertEqual(ExploitEnd.StandDown, t.Sink.Ends[0].End, "a new battle day ends the exploit as stand-down");
        AssertTrue(t.Table.Count == 0 || t.Table.At(0).LaunchHours >= t.Now - 1e-9, "anything live afterwards launched on the new day");
        BattleScope.ResetAll();
    }

    private static void TickPlayerAsset()
    {
        BattleScope.ResetAll();
        TickFx t = TickFx.Attacker();
        t.Patch(t.Regiment[0], (ref UnitRecord r) => { r.Flags |= UnitFlags.PlayerOwned; });
        AssertTrue(t.RunToLaunch(), "launched on another brigade");
        // Brigade 1 would open a 380 m gap in the line (line-integrity), so the next reserve is chosen.
        AssertEqual(t.BrigadeId(2), t.Table.At(0).AssetId, "the player's brigade is skipped; brigade 1 fails line-integrity; the next reserve is the asset");
    }

    private static void TickWithdraw()
    {
        BattleScope.ResetAll();
        TickFx t = TickFx.Attacker();
        t.World.PlanPhase = BattlePhase.Withdraw;
        AssertFalse(t.RunToLaunch(12), "no launch in the Withdraw phase");
        AssertTrue(t.Sink.Declines.Exists(d => d.StartsWith("phase@")), "declined with the phase reason");

        TickFx u = TickFx.Attacker();
        AssertTrue(u.RunToLaunch(), "launched");
        u.World.PlanPhase = BattlePhase.Withdraw;
        u.Run();
        AssertEqual(ExploitEnd.WithdrawPhase, u.Sink.Ends[0].End, "the plan entering Withdraw ends a live exploit");
    }

    private static void TickDefender()
    {
        BattleScope.ResetAll();
        var t = new TickFx();
        t.World.Stance = BattleStance.Defender;
        t.Army = t.Fx.OwnGroup();
        t.Patch(t.Army, (ref UnitRecord r) => { r.UnitTyp = TacticalUnitType.BattleGroupArmy; });
        t.Div = t.Fx.OwnGroup(t.Army);
        t.Patch(t.Div, (ref UnitRecord r) => { r.UnitTyp = TacticalUnitType.BattleGroupDivision; r.Commander = 42; r.GroupMorale = 0.8f; });
        // Four line brigades (one regiment each) at depth 0, three reserve brigades 300 m behind.
        var ids = new List<int>();
        for (int i = 0; i < 4; i++) AddBrigade(t, i * 100f, 0f, CommandNodeRole.Defender, CommandTaskType.HoldObjective, ids);
        int reserve = AddBrigade(t, 150f, -300f, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, ids);
        AddBrigade(t, 0f, -300f, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, ids);
        AddBrigade(t, 300f, -300f, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, ids);
        int enemy = t.Fx.Inf(150f, -150f);

        AssertTrue(t.RunToLaunch(14), "a Defender launches a counterstroke at the penetration");
        ExploitEntry e = t.Table.At(0);
        AssertEqual(OpeningKind.Penetration, e.Kind, "a Penetration");
        AssertEqual(enemy, e.TargetObservation, "against the penetrating sample");
        AssertEqual(TickFx.IdOf(reserve), e.AssetId, "the second-line reserve nearest the aim is the asset");
        AssertTrue(t.Table.TryOverride(e.AssetId, out CommandDoctrineOrder order, out CommandNodeOperationalState state), "overridden");
        AssertEqual(CommandTaskType.Counterstroke, order.Task, "the Counterstroke task");
        AssertEqual(CommandTaskType.Counterstroke, state.Task, "in the state too");

        // No Defender counterstroke with the flag off.
        BattleScope.ResetAll();
        var off = new TickFx();
        off.World.Stance = BattleStance.Defender;
        off.Rules.CounterstrokeOn = false;
        off.Army = off.Fx.OwnGroup();
        off.Patch(off.Army, (ref UnitRecord r) => { r.UnitTyp = TacticalUnitType.BattleGroupArmy; });
        off.Div = off.Fx.OwnGroup(off.Army);
        off.Patch(off.Div, (ref UnitRecord r) => { r.UnitTyp = TacticalUnitType.BattleGroupDivision; r.Commander = 42; r.GroupMorale = 0.8f; });
        for (int i = 0; i < 4; i++) AddBrigade(off, i * 100f, 0f, CommandNodeRole.Defender, CommandTaskType.HoldObjective, new List<int>());
        for (int i = 0; i < 3; i++) AddBrigade(off, 150f, -300f, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, new List<int>());
        off.Fx.Inf(150f, -150f);
        AssertFalse(off.RunToLaunch(14), "with Enable Defender Counterstroke off a Defender never launches");
    }

    private static int AddBrigade(TickFx t, float lateral, float depth, CommandNodeRole role, CommandTaskType task, List<int> ids)
    {
        int g = t.Fx.OwnGroup(t.Div);
        t.Patch(g, (ref UnitRecord r) => { r.UnitTyp = TacticalUnitType.BattleGroupBrigade; r.Commander = 100 + g; r.GroupMorale = 0.8f; r.X = lateral; r.Z = depth; r.GroupWidth = 100f; });
        int reg = t.Fx.OwnInf(lateral, depth, 100f, g);
        t.Patch(reg, (ref UnitRecord r) => { r.Strength = 2000; });
        t.World.Roles[TickFx.IdOf(g)] = (role, task);
        ids.Add(g);
        return g;
    }

    private static void TickIdle()
    {
        BattleScope.ResetAll();
        // Close the gap: the second enemy regiment joins the first.
        var closed = new TickFx();
        closed.Army = closed.Fx.OwnGroup();
        closed.Patch(closed.Army, (ref UnitRecord r) => { r.UnitTyp = TacticalUnitType.BattleGroupArmy; });
        closed.Div = closed.Fx.OwnGroup(closed.Army);
        closed.Patch(closed.Div, (ref UnitRecord r) => { r.UnitTyp = TacticalUnitType.BattleGroupDivision; r.Commander = 42; r.GroupMorale = 0.8f; });
        for (int i = 0; i < 3; i++) AddBrigade(closed, i * 100f, 0f, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, new List<int>());
        closed.Fx.Inf(0f, 300f);
        closed.Fx.Inf(100f, 300f);
        closed.Fx.Inf(200f, 300f);
        for (int i = 0; i < 10; i++) closed.Run();
        AssertEqual(0, closed.Table.Count, "no confirmed opening, no exploit");
        AssertEqual(0, closed.Sink.Launches.Count, "no launch row");
        AssertTrue(closed.Sink.Summaries >= 1, "the opening summary row is emitted on its interval");
    }

    // The first commander index at or above `from` whose first eight rolls on opening 1 are all below 0.9 (see FindReliableCommander).
    private static int ReliableCommanderFrom(int from, int staggerSlot = -1)
    {
        for (int c = from; c < 20000; c++)
        {
            if (staggerSlot >= 0 && (c & 3) != staggerSlot) continue;
            bool ok = true;
            for (int n = 0; n < 8 && ok; n++) ok = OpportunityDoctrine.Roll(7, c, 1, n) < 0.9f;
            if (ok) return c;
        }
        throw new Exception("no reliable commander index");
    }

    private static void TickRollMissHesitates()
    {
        BattleScope.ResetAll();
        // A commander whose first roll on opening 1 misses (>= the 0.95 chance cap) and whose next six all hit: without the
        // hesitation mark he would launch at his very next decision, 0.05 h later.
        int missing = -1;
        for (int c = 40; c < 40000 && missing < 0; c++)
        {
            if (OpportunityDoctrine.Roll(7, c, 1, 0) < 0.95f) continue;
            bool hits = true;
            for (int n = 1; n <= 6 && hits; n++) hits = OpportunityDoctrine.Roll(7, c, 1, n) < 0.9f;
            if (hits) missing = c;
        }
        AssertTrue(missing >= 0, "a first-miss commander exists");
        TickFx t = TickFx.Attacker();
        t.Patch(t.Div, (ref UnitRecord r) => { r.Commander = missing; });
        bool missed = false;
        for (int i = 0; i < 14 && !missed; i++)
        {
            t.Run();
            foreach (string d in t.Sink.Declines) if (d.StartsWith(OpportunityDoctrine.RollMiss, StringComparison.Ordinal)) missed = true;
        }
        AssertTrue(missed, "the first decision is a roll-miss");
        AssertEqual(0, t.Table.Count, "and launches nothing");
        double passedOver = t.Now;
        while (t.Now < passedOver + 0.09)
        {
            t.Run();
            AssertEqual(0, t.Table.Count, "the passed-over opening is not decided again within 0.1 h (t=" + t.Now + ")");
        }
        AssertTrue(t.RunToLaunch(8), "once the hesitation has run out the next decision launches");
    }

    private static void TickSiblingLedger()
    {
        BattleScope.ResetAll();
        // One closed line of five holding brigades at depth 0, 110 m apart (100 m wide: a 10 m edge gap). Division A holds g0 and
        // g1, division B holds g2 and g3. Removing one interior brigade opens 120 m (below the 150 m gap minimum), two adjacent
        // ones 230 m. Division A launches g1 on the single gap in front. Division B decides on the same tick: for its g2 the
        // commitment ledger must already carry g1, so the line-integrity check (risk 2) fails; without A's same-tick launch in
        // the ledger g2 would pass it and fail only the later concurrency check (risk 7, the opening is claimed). Both commanders
        // are due on the same tick (their stagger is 0). The division's last-failing reason is the one reported, so each has one asset.
        var t = new TickFx();
        t.Army = t.Fx.OwnGroup();
        t.Patch(t.Army, (ref UnitRecord r) => { r.UnitTyp = TacticalUnitType.BattleGroupArmy; });
        int commanderA = ReliableCommanderFrom(40, 0);
        int commanderB = ReliableCommanderFrom(commanderA + 1, 0);
        var ids = new List<int>();
        t.Div = t.Fx.OwnGroup(t.Army);
        t.Patch(t.Div, (ref UnitRecord r) => { r.UnitTyp = TacticalUnitType.BattleGroupDivision; r.Commander = commanderA; r.GroupMorale = 0.8f; });
        int g1 = AddBrigade(t, 110f, 0f, CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, ids);
        int g0 = AddBrigade(t, 0f, 0f, CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, ids);
        t.Div = t.Fx.OwnGroup(t.Army);
        t.Patch(t.Div, (ref UnitRecord r) => { r.UnitTyp = TacticalUnitType.BattleGroupDivision; r.Commander = commanderB; r.GroupMorale = 0.8f; });
        AddBrigade(t, 220f, 0f, CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, ids);
        int g3 = AddBrigade(t, 330f, 0f, CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, ids);
        // g0 and g3 hold the line ends and are not candidates (a Safety claim makes an asset ineligible), so each division has
        // exactly one asset: g1 and g2.
        t.World.Safety.Add(TickFx.IdOf(g0));
        t.World.Safety.Add(TickFx.IdOf(g3));
        t.World.MainEffortSector = -1;
        t.Fx.Inf(60f, 300f);
        t.Fx.Inf(330f, 300f);
        for (int i = 0; i < 12 && t.Table.Count == 0; i++) t.Run();
        AssertEqual(1, t.Table.Count, "one launch");
        AssertEqual(TickFx.IdOf(g1), t.Table.At(0).AssetId, "division A's g1, a brigade of the closed line");
        string declines = string.Join(",", t.Sink.Declines);
        AssertTrue(declines.Contains("line-integrity@"), "division B's g2 is refused for the line the ledger shows, not for the claimed opening: " + declines);
        AssertFalse(declines.Contains("concurrency@"), "the integrity check comes first: " + declines);
    }
}
