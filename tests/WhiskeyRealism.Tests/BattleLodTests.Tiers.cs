using WhiskeyRealism.Tactical;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using static TestHarness;
using static TestFixtures;

// BC15 decisions 1, 3, 6 and 11: the Distant conditions, the schedule, the stagger, the clock.
internal static partial class BattleLodTests
{
    private static (string, Action)[] TierTests()
    {
        return new (string, Action)[]
        {
            ("bc15 TIER a quiet settled group on a static task is Full for the minimum hold, then Distant", QuietGroupEntersDistant),
            ("bc15 TIER DistantNeedsEveryCondition: each condition alone keeps a group Full, and promotes one that was Distant", DistantNeedsEveryCondition),
            ("bc15 TIER enemies beyond the distance and memories below the blocking confidence do not block", FarAndFadedDoNotBlock),
            ("bc15 TIER MovingTaskNeverDistant: Distant exactly for the static task list", OnlyStaticTasksAreDistant),
            ("bc15 TIER a regiment answers with the tier of its command group", RegimentFollowsItsGroup),
            ("bc15 TIER PendingOrderKeepsFull: an issued move, a retry ticket and a refused winner", PendingOrderKeepsFull),
            ("bc15 TIER PlayerChainAlwaysFull: the player's command and its command ancestors", PlayerChainAlwaysFull),
            ("bc15 TIER SettledUsesProducerActiveMoveTest: moving regiments under a still group", SettledUsesTheSubtreeMovingCount),
            ("bc15 TIER TierUsesOnlyPerceivedState: a hidden enemy 300 m away promotes nothing", TierUsesOnlyPerceivedState),
            ("bc15 TIER RememberedBelowHalfConfidenceDoesNotBlockDistant", RememberedBelowHalfConfidenceDoesNotBlock),
            ("bc15 TIER DistantIntervalFromSideStatCycle: a due tick every fourth, at cycles 0.015 and 0.03", DistantIntervalFromSideStatCycle),
            ("bc15 TIER the interval is four cycles less a half tick", IntervalConstants),
            ("bc15 TIER StaggerSpreadsDistantGroupsOverFourTicks", StaggerSpreadsOverFourTicks),
            ("bc15 TIER MinFullHoldsTwoTicks after a promotion", MinFullHoldsTwoTicks),
            ("bc15 TIER a group first met through a nav report captures a signature before it can be Distant", NewGroupWithoutASignatureStaysFull),
            ("bc15 TIER TiersFreezeWithDoctrineClockOvernight: no real time input, a frozen clock adds no due tick", TiersFreezeWithTheDoctrineClock),
            ("bc15 TIER a cycle of zero or no lookup is every group every tick (the flag-off shape)", UnconfiguredIsFullRate),
            ("bc15 TIER the tier pass reads no real time, no true enemy record and no hard-coded cycle (source pin)", SourcePinsForPurity),
            ("bc15 TIER the counts add up and a promotion is counted once per reason", CountsAddUp),
            ("bc15 TIER Classify allocates nothing after warm-up", ClassifyAllocatesNothingAfterWarmup),
        };
    }

    private static LodWorld QuietWorld(int brigades = 1)
    {
        var w = new LodWorld();
        for (int b = 0; b < brigades; b++) w.QuietBrigade(100 + b, 1000f + b * 150f, 1000f);
        return w;
    }

    private static void QuietGroupEntersDistant()
    {
        LodWorld w = QuietWorld();
        var tiers = new LodTier[6];
        for (int t = 0; t < tiers.Length; t++)
        {
            w.Tick();
            tiers[t] = w.Tier(100);
        }
        AssertEqual(LodTier.Full, tiers[0], "tick 0: a new group is Full");
        AssertEqual(LodTier.Full, tiers[1], "tick 1: its signature is captured, still inside the minimum hold");
        AssertEqual(LodTier.Distant, tiers[2], "tick 2: quiet for two cycles with a signature");
        AssertEqual(LodTier.Distant, tiers[5], "and it stays Distant while nothing changes");
        AssertEqual(LodPromotion.None, w.Why(100), "no promotion reason");
    }

    private sealed class Condition
    {
        public string Name;
        public Action<LodWorld> Arm;   // run before every tick, so a condition that is consumed (a refusal) holds each tick
        public LodPromotion Reason;    // what a Distant group promoted by it reports (None: it only keeps a group Full)
    }

    private static Condition[] Conditions()
    {
        return new[]
        {
            new Condition { Name = "visible enemy within 1,500 m", Reason = LodPromotion.Distance, Arm = w => EnsureEnemy(w, 9001, 1000f, 2200f) },
            // 2,400 m from the brigade but 1,400 m from a regiment that stands 1,000 m out in front of it.
            new Condition { Name = "visible enemy near a regiment under the group", Reason = LodPromotion.Distance, Arm = w => { w.Unit(1001).Z = 2000f; EnsureEnemy(w, 9001, 970f, 3400f); } },
            // 1,900 m from the brigade, 1,400 m from a regiment 500 m out: inside the group's subtree radius plus 1,500 m.
            new Condition { Name = "remembered enemy near a regiment under the group", Reason = LodPromotion.Distance, Arm = w => { w.Unit(1001).Z = 1500f; EnsureRemembered(w, 9002, 970f, 2900f, 0.6f); } },
            new Condition { Name = "remembered enemy at 0.6 within 1,500 m", Reason = LodPromotion.Distance, Arm = w => EnsureRemembered(w, 9002, 1000f, 2300f, 0.6f) },
            new Condition { Name = "fire received by a regiment under the group", Reason = LodPromotion.Fire, Arm = w => w.Unit(1001).ReceivedFire = true },
            new Condition { Name = "melee in a regiment under the group", Reason = LodPromotion.Fire, Arm = w => w.Unit(1002).Flags |= UnitFlags.InMelee },
            new Condition { Name = "the group has a path", Reason = LodPromotion.Moving, Arm = w => w.Unit(100).RegimentPaths = 1 },
            new Condition { Name = "a regiment under the group has a path", Reason = LodPromotion.Moving, Arm = w => w.Unit(1001).RegimentPaths = 1 },
            new Condition { Name = "the group is in a movement mode", Reason = LodPromotion.Moving, Arm = w => w.Unit(100).MovementMode = 3 },
            new Condition { Name = "the group is path-interrupted", Reason = LodPromotion.Moving, Arm = w => w.Unit(100).Flags |= UnitFlags.PathInterrupted },
            new Condition { Name = "orders queued on the group", Reason = LodPromotion.Pending, Arm = w => w.Unit(100).QueuedOrders = 2 },
            new Condition { Name = "a refused winner last tick", Reason = LodPromotion.Pending, Arm = w => w.Executor.NoteRefusal(100, SkipReason.Cooldown) },
            new Condition { Name = "a move issued and not yet judged", Reason = LodPromotion.Pending, Arm = w => IssueMove(w, 100) },
            new Condition { Name = "a NavMesh plan the budget deferred", Reason = LodPromotion.Pending, Arm = w => w.State.MarkNavDeferred(100) },
            new Condition { Name = "a movement task", Reason = LodPromotion.Target, Arm = w => w.Tasks.Task[100] = CommandTaskType.AttackObjective },
            new Condition { Name = "a latch or maneuver", Reason = LodPromotion.Target, Arm = w => w.Tasks.Latched.Add(100) },
            new Condition { Name = "the player's command", Reason = LodPromotion.PlayerChain, Arm = w => w.Unit(100).Flags |= UnitFlags.PlayerCommand },
            new Condition { Name = "an ancestor of the player's command", Reason = LodPromotion.PlayerChain, Arm = w => w.Unit(100).Flags |= UnitFlags.AncestorOfPlayerCommand },
            new Condition { Name = "a group the player owns", Reason = LodPromotion.PlayerChain, Arm = w => w.Unit(100).Flags |= UnitFlags.PlayerOwned },
            new Condition { Name = "the side's plan changed", Reason = LodPromotion.Plan, Arm = w => w.PlanSignature++ },
            new Condition { Name = "an inactive group", Reason = LodPromotion.None, Arm = w => w.Unit(100).Flags &= ~UnitFlags.Active },
            new Condition { Name = "a routed group", Reason = LodPromotion.None, Arm = w => w.Unit(100).Flags |= UnitFlags.Routed },
            new Condition { Name = "a group on retreat", Reason = LodPromotion.None, Arm = w => w.Unit(100).Flags |= UnitFlags.OnRetreat },
            new Condition { Name = "a garrisoned group", Reason = LodPromotion.None, Arm = w => w.Unit(100).Flags |= UnitFlags.Garrisoned },
            new Condition { Name = "a permanently detached group", Reason = LodPromotion.None, Arm = w => w.Unit(100).Flags |= UnitFlags.PermanentlyDetached },
        };
    }

    private static void EnsureEnemy(LodWorld w, int id, float x, float z)
    {
        if (w.IndexOfEnemy(id) < 0) w.SeeEnemy(id, x, z);
    }

    private static void EnsureRemembered(LodWorld w, int id, float x, float z, float confidence)
    {
        if (w.IndexOfEnemy(id) < 0) w.RememberEnemy(id, x, z, confidence);
    }

    private static void IssueMove(LodWorld w, int groupId)
    {
        var intent = new BattleOrderIntent { GroupId = groupId, Channel = OrderChannel.Move, TargetX = 900f, TargetZ = 900f, Source = IntentSource.PostureExecutor };
        w.Executor.RecordIssued(in intent, w.Hours, null, 0, null, 0, 0, 0);
    }

    private static void DistantNeedsEveryCondition()
    {
        foreach (Condition c in Conditions())
        {
            // 1. The condition holds from the start: the group is never Distant.
            LodWorld held = QuietWorld();
            for (int t = 0; t < 8; t++)
            {
                c.Arm(held);
                held.Tick();
                AssertEqual(LodTier.Full, held.Tier(100), c.Name + ": Full on tick " + t);
                AssertTrue(held.Due(100), c.Name + ": and due on tick " + t);
            }

            // 2. It arises while the group is Distant: promoted on that very tick.
            LodWorld arising = QuietWorld();
            arising.TickUntilDistant(100);
            arising.Tick();
            AssertEqual(LodTier.Distant, arising.Tier(100), c.Name + ": still Distant before the condition");
            c.Arm(arising);
            arising.Tick();
            AssertEqual(LodTier.Full, arising.Tier(100), c.Name + ": promoted on the tick it arises");
            AssertTrue(arising.Due(100), c.Name + ": and due that tick");
            if (c.Reason != LodPromotion.None)
                AssertTrue((arising.Why(100) & c.Reason) == c.Reason, c.Name + ": promoted for " + c.Reason + ", got " + arising.Why(100));
        }

        LodWorld control = QuietWorld();
        control.TickUntilDistant(100);
        AssertEqual(LodTier.Distant, control.Tier(100), "control: nothing armed, Distant");
    }

    private static void FarAndFadedDoNotBlock()
    {
        LodWorld w = QuietWorld();
        w.SeeEnemy(9001, 1000f, 1000f + BattleLod.DistantMeters + 200f);   // visible, but beyond 1,500 m of every unit
        w.RememberEnemy(9002, 1000f, 1300f, 0.4f);                          // near, but a faded memory
        w.TickUntilDistant(100);
        AssertEqual(LodTier.Distant, w.Tier(100), "a far visible enemy and a faded memory leave the group Distant");
    }

    private static void OnlyStaticTasksAreDistant()
    {
        var statics = new[]
        {
            CommandTaskType.None, CommandTaskType.FormUp, CommandTaskType.HoldObjective, CommandTaskType.HoldChoke,
            CommandTaskType.GuardFlank, CommandTaskType.ReserveWait, CommandTaskType.Consolidate,
        };
        foreach (CommandTaskType task in Enum.GetValues(typeof(CommandTaskType)))
        {
            bool expected = Array.IndexOf(statics, task) >= 0;
            AssertEqual(expected, BattleLod.IsStaticTask(task), "IsStaticTask(" + task + ")");
            LodWorld w = QuietWorld();
            w.Tasks.Task[100] = task;
            for (int t = 0; t < 6; t++) w.Tick();
            AssertEqual(expected ? LodTier.Distant : LodTier.Full, w.Tier(100), "the tier of a group on " + task);
        }
    }

    private static void RegimentFollowsItsGroup()
    {
        LodWorld w = QuietWorld();
        w.Tick();
        AssertTrue(w.Due(1001), "a regiment of a Full group is due");
        w.TickUntilDistant(100);
        w.Tick();
        w.Tick();
        // After the stagger's due tick the group is not due; its regiments follow it either way.
        for (int t = 0; t < 6; t++)
        {
            w.Tick();
            AssertEqual(w.Due(100), w.Due(1001), "regiment due == group due, tick " + t);
            AssertEqual(w.Tier(100), w.Tier(1001), "regiment tier == group tier, tick " + t);
        }
        var orphan = new LodWorld();
        orphan.Regiment(77, 0, 0f, 0f);
        orphan.Tick();
        AssertTrue(orphan.Due(77), "a regiment with no group above it is always due");
    }

    private static void PendingOrderKeepsFull()
    {
        // 1. A move issued and not yet judged keeps its group Full; once judged (the group took it) the group moves, so it stays Full for that.
        LodWorld w = QuietWorld();
        w.TickUntilDistant(100);
        IssueMove(w, 100);
        w.Tick();
        AssertEqual(LodTier.Full, w.Tier(100), "an Issued move not yet resolved");
        AssertTrue((w.Why(100) & LodPromotion.Pending) != 0, "reported as pending");

        // 2. A retry ticket (the move failed and a rotated retry waits) is an order in flight.
        LodWorld r = QuietWorld();
        r.Tick();
        IssueMove(r, 100);
        var outcomes = new OrderOutcome[8];
        r.Executor.ObserveOutcomes(r.S, r.Hours, outcomes, out int count);   // no path took: Failed, then a rotated Retried
        AssertTrue(r.Executor.IsUnsettled(100), "a failed move leaves a retry ticket (" + count + " outcomes)");
        r.Tick();
        AssertEqual(LodTier.Full, r.Tier(100), "a queued retry");

        // 3. Last tick's refusal: only reasons other than Duplicate count, and ClearRefusals ends it.
        LodWorld c = QuietWorld();
        c.Executor.NoteRefusal(100, SkipReason.Duplicate);
        AssertFalse(c.Executor.IsUnsettled(100), "a Duplicate is the steady state of a settled group");
        c.Executor.NoteRefusal(100, SkipReason.StandingClaim);
        AssertFalse(c.Executor.IsUnsettled(100), "and so is a posted guard's standing claim");
        c.Executor.NoteRefusal(100, SkipReason.None);
        AssertFalse(c.Executor.IsUnsettled(100), "no reason is no refusal");
        c.Executor.NoteRefusal(100, SkipReason.Cooldown);
        AssertTrue(c.Executor.IsUnsettled(100), "a Cooldown refusal");
        c.Executor.ClearRefusals();
        AssertFalse(c.Executor.IsUnsettled(100), "cleared once the tier pass read it");
        foreach (SkipReason reason in Enum.GetValues(typeof(SkipReason)))
        {
            if (reason == SkipReason.None || reason == SkipReason.Duplicate || reason == SkipReason.StandingClaim) continue;
            c.Executor.NoteRefusal(100, reason);
            AssertTrue(c.Executor.IsUnsettled(100), "refusal " + reason);
            c.Executor.ClearRefusals();
        }
    }

    private static void PlayerChainAlwaysFull()
    {
        var w = new LodWorld();
        w.Division(50, 0, 1000f, 1000f);
        w.Brigade(100, 50, 1000f, 1000f);
        w.Regiment(1001, 100, 1000f, 1000f);
        w.Brigade(101, 50, 1200f, 1000f);
        w.Regiment(1011, 101, 1200f, 1000f);
        w.Unit(50).Flags |= UnitFlags.AncestorOfPlayerCommand;
        w.Unit(100).Flags |= UnitFlags.PlayerCommand;
        for (int t = 0; t < 8; t++) w.Tick();
        AssertEqual(LodTier.Full, w.Tier(50), "the command ancestor");
        AssertEqual(LodTier.Full, w.Tier(100), "the player's command");
        AssertEqual(LodTier.Distant, w.Tier(101), "an AI sibling of the player's command is not part of the chain");
        AssertTrue((w.Why(50) & LodPromotion.PlayerChain) != 0 && (w.Why(100) & LodPromotion.PlayerChain) != 0, "reported as the player chain");
    }

    // The snapshot's moving-unit count is the producer's test (PostureTick.State.Moving); vanilla's groupsubordinatesmoving would read 0 here.
    private static void SettledUsesTheSubtreeMovingCount()
    {
        var w = new LodWorld();
        w.Division(50, 0, 1000f, 1000f);
        w.Brigade(100, 50, 1000f, 1000f);
        w.Regiment(1001, 100, 1000f, 1000f);
        w.Regiment(1002, 100, 1040f, 1000f);
        for (int t = 0; t < 4; t++) w.Tick();
        AssertEqual(LodTier.Distant, w.Tier(100), "precondition: quiet brigade is Distant");
        w.Unit(1001).RegimentPaths = 1;       // a regiment walks; the brigade's own record shows no path
        w.Tick();
        AssertEqual(0, w.S.Own[w.S.IndexOfOwn(100)].RegimentPaths, "the brigade record itself has no path");
        AssertTrue(w.Moving[w.S.IndexOfOwn(100)] > 0, "the snapshot counts the walking regiment under it");
        AssertEqual(LodTier.Full, w.Tier(100), "so the brigade is not settled");
        AssertEqual(LodTier.Full, w.Tier(50), "nor the division above it");
    }

    private static void TierUsesOnlyPerceivedState()
    {
        LodWorld w = QuietWorld();
        // The true enemy record 300 m from the brigade; the side has no observation of it.
        w.EnemyRecords.Add(new UnitRecord { InstanceId = 8001, UnitTyp = TacticalUnitType.Infantry, Alliance = 1, X = 1000f, Z = 1300f, Flags = UnitFlags.Alive | UnitFlags.Active, Strength = 500 });
        w.TickUntilDistant(100);
        for (int t = 0; t < 8; t++)
        {
            w.Tick();
            AssertEqual(LodTier.Distant, w.Tier(100), "a hidden enemy promotes nothing, tick " + t);
        }
    }

    private static void RememberedBelowHalfConfidenceDoesNotBlock()
    {
        foreach (float confidence in new[] { 0.05f, 0.3f, 0.49f })
        {
            LodWorld w = QuietWorld();
            w.RememberEnemy(9002, 1000f, 1300f, confidence);
            w.TickUntilDistant(100);
            AssertEqual(LodTier.Distant, w.Tier(100), "a memory at " + confidence + " leaves the group Distant");
        }
        foreach (float confidence in new[] { 0.5f, 0.75f, 1f })
        {
            LodWorld w = QuietWorld();
            w.RememberEnemy(9002, 1000f, 1300f, confidence);
            for (int t = 0; t < 8; t++) w.Tick();
            AssertEqual(LodTier.Full, w.Tier(100), "a memory at " + confidence + " keeps it Full");
        }
        AssertNear(0.5f, BattleLod.RememberedMinConfidence, 0f, "the blocking confidence is the fog rule's (EnemySamples)");
    }

    private static void IntervalConstants()
    {
        AssertNear(0.0525f, (float)BattleLod.DistantIntervalHours(0.015), 1e-6f, "4 x 0.015 - 0.0075");
        AssertNear(0.105f, (float)BattleLod.DistantIntervalHours(0.03), 1e-6f, "4 x 0.03 - 0.015");
        AssertNear(0.03f, (float)BattleLod.MinFullHours(0.015), 1e-6f, "two cycles");
        AssertEqual(4, BattleLod.IntervalTicks, "interval ticks");
        AssertEqual(2, BattleLod.MinFullTicks, "minimum full ticks");
        AssertNear(1500f, BattleLod.DistantMeters, 0f, "distant meters");
        AssertNear(3000f, BattleLod.PromoteMeters, 0f, "promote meters");
        AssertNear(60f, BattleLod.TargetMoveMeters, 0f, "the executor's dedupe radius");
        AssertNear(15f, BattleLod.TurnDegrees, 0f, "the executor's facing tolerance");
        AssertNear(250f, BattleLod.EnemyDistanceChangeMeters, 0f, "enemy distance change");
        AssertNear(250f, BattleLod.CorridorWidthMeters, 0f, "corridor width");
    }

    private static void DistantIntervalFromSideStatCycle()
    {
        foreach (double cycle in new[] { 0.015, 0.03 })
        {
            LodWorld w = QuietWorld();
            w.CycleHours = cycle;
            w.TickUntilDistant(100);
            var due = new System.Collections.Generic.List<int>();
            for (int t = 0; t < 24; t++)
            {
                w.Tick();
                AssertEqual(LodTier.Distant, w.Tier(100), "stays Distant, cycle " + cycle + " tick " + t);
                if (w.Due(100)) due.Add(t);
            }
            AssertTrue(due.Count >= 5 && due.Count <= 7, "about one due tick in four over 24 ticks at cycle " + cycle + ": " + string.Join(",", due));
            for (int k = 1; k < due.Count; k++)
                AssertEqual(4, due[k] - due[k - 1], "due ticks are four cycles apart at cycle " + cycle);
        }
    }

    // Eight groups enter Distant on the same tick; their due ticks fall two to a tick across the next four ticks.
    private static void StaggerSpreadsOverFourTicks()
    {
        LodWorld w = QuietWorld(8);
        for (int t = 0; t < 2; t++) w.Tick();
        w.Tick();   // the tick all eight enter Distant
        for (int b = 0; b < 8; b++) AssertEqual(LodTier.Distant, w.Tier(100 + b), "group " + b + " entered Distant together");
        var perTick = new int[8];
        for (int t = 0; t < 8; t++)
        {
            w.Tick();
            for (int b = 0; b < 8; b++) if (w.Due(100 + b)) perTick[t]++;
        }
        for (int t = 0; t < 8; t++) AssertEqual(2, perTick[t], "two of eight groups are due on tick " + t + " (" + string.Join(",", perTick) + ")");
    }

    private static void MinFullHoldsTwoTicks()
    {
        LodWorld w = QuietWorld();
        w.UseCorridor = false;
        w.Tasks.Target[100] = (1000f, 1000f, -1f, -1);
        w.TickUntilDistant(100);
        w.Tick();
        // A trigger promotes it (the order target moves 200 m). The clock is set where 1.0 + 0.015 + 0.015 - 1.0 is 0.0299999999999998 in
        // doubles, short of two cycles: the hold compares with a clock epsilon, so the promotion still ends after two ticks.
        w.Hours = 1.0;
        w.Tasks.Target[100] = (1000f, 1200f, -1f, -1);
        w.Tick(advance: 0.0);
        AssertEqual(LodTier.Full, w.Tier(100), "promoted");
        w.Tick();
        AssertEqual(LodTier.Full, w.Tier(100), "still inside the two-cycle hold one tick later");
        w.Tick();
        AssertEqual(LodTier.Distant, w.Tier(100), "Distant again two ticks after the promotion, not sooner");
    }

    // A group the tier state first met through a nav report (never classified, no signature, a cause hour of 0) must still capture a
    // signature before it can be Distant: with none, the triggers (b)-(e) would have nothing to compare with.
    private static void NewGroupWithoutASignatureStaysFull()
    {
        LodWorld w = QuietWorld();
        w.State.MarkNavDeferred(100);
        w.State.MarkNavPlanned(100);
        w.Tick();
        AssertEqual(LodTier.Full, w.Tier(100), "captures its signature first, however long ago its entry was made");
        w.Tick();
        AssertEqual(LodTier.Distant, w.Tier(100), "and is Distant on the next quiet tick");
    }

    private static void TiersFreezeWithTheDoctrineClock()
    {
        LodWorld w = QuietWorld();
        w.TickUntilDistant(100);
        w.Tick();
        // The night end-of-day cycle: wall time and vanilla's clock jump, the doctrine clock stands still.
        int dueWhileFrozen = 0;
        for (int t = 0; t < 40; t++)
        {
            w.Tick(advance: 0.0);
            if (w.Due(100) && w.Tier(100) == LodTier.Distant) dueWhileFrozen++;
            AssertEqual(LodTier.Distant, w.Tier(100), "stays Distant across a frozen clock");
        }
        AssertTrue(dueWhileFrozen <= 1, "a frozen clock adds no due tick (" + dueWhileFrozen + ")");
        int dueAfter = 0;
        for (int t = 0; t < 8; t++)
        {
            w.Tick();
            if (w.Due(100)) dueAfter++;
        }
        AssertEqual(2, dueAfter, "when the clock runs again the schedule continues, two due ticks in eight");
    }

    private static void UnconfiguredIsFullRate()
    {
        LodWorld w = QuietWorld();
        w.CycleHours = 0.0;
        for (int t = 0; t < 6; t++)
        {
            w.Tick(advance: 0.015);
            AssertEqual(LodTier.Full, w.Tier(100), "no cycle: Full on tick " + t);
            AssertTrue(w.Due(100) && w.Due(1001), "no cycle: due on tick " + t);
        }
        LodWorld none = QuietWorld();
        var side = none.Side();
        side.Tasks = null;
        none.Hours += Cycle;
        none.Build();
        BattleLod.Classify(none.S, none.State, side, none.Hours);
        AssertTrue(none.Due(100), "no task lookup: due");
        AssertEqual(LodTier.Full, none.Tier(100), "no task lookup: Full");
    }

    private static void SourcePinsForPurity()
    {
        string dir = FindRepoPath("src/WhiskeyRealism/Tactical/Battle");
        foreach (string file in new[] { "BattleLod.cs", "BattleLodState.cs" })
        {
            string text = File.ReadAllText(Path.Combine(dir, file));
            AssertFalse(Regex.IsMatch(text, @"realtimeSinceStartup|Environment\.TickCount|DateTime"), file + " reads real time");
            AssertFalse(Regex.IsMatch(text, @"EnemyRecords|combatstatussubordinates|CombatStatusSubordinates"), file + " reads the enemy's true state");
            AssertFalse(Regex.IsMatch(text, @"0\.015"), file + " hard-codes the side-stat cycle");
        }
    }

    private static void CountsAddUp()
    {
        LodWorld w = QuietWorld(5);
        w.TickUntilDistant(100);
        w.Tick();
        w.Tick();
        int groups = 5;
        AssertEqual(groups, w.State.FullCount + w.State.DistantCount, "every group is Full or Distant");
        AssertTrue(w.State.DueCount <= w.State.DistantCount, "due is a subset of Distant");
        AssertEqual(w.State.DistantCount - w.State.DueCount, w.State.CarriedCount, "carried = Distant not due");
        w.State.ClearPromotionCounts();
        w.Unit(100).RegimentPaths = 1;
        w.Unit(101).RegimentPaths = 1;
        w.Unit(101).ReceivedFire = true;
        w.Tick();
        AssertEqual(2, w.State.PromotionCountOf(LodPromotion.Moving), "two groups promoted for moving");
        AssertEqual(1, w.State.PromotionCountOf(LodPromotion.Fire), "one for fire");
        AssertEqual(0, w.State.PromotionCountOf(LodPromotion.Plan), "none for the plan");
        w.State.ClearPromotionCounts();
        AssertEqual(0, w.State.PromotionCountOf(LodPromotion.Moving), "cleared");
        AssertEqual(0, w.State.PromotionCountOf(LodPromotion.Observation | LodPromotion.Parent), "only a single flag names a reason");
    }

    private static void ClassifyAllocatesNothingAfterWarmup()
    {
        LodWorld w = QuietWorld(40);
        w.SeeEnemy(9001, 1000f, 9000f);
        w.RememberEnemy(9002, 4000f, 4000f, 0.8f);
        for (int t = 0; t < 8; t++) w.Tick();
        // Classify alone: the snapshot is rebuilt outside the window.
        w.Hours += Cycle;
        w.Build();
        LodSideInput side = w.Side();
        BattleLod.Classify(w.S, w.State, side, w.Hours);
        w.Hours += Cycle;
        w.Build();
        long before = BeginAllocationWindow();
        for (int k = 0; k < 20; k++) BattleLod.Classify(w.S, w.State, side, w.Hours + k * Cycle);
        AssertEqual(0L, AllocatedSince(before), "bytes allocated by 20 tier passes over 40 brigades");
    }
}
