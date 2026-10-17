using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Battle.Producers;
using WhiskeyRealism.Tactical.Operations;
using static TestHarness;

// BC15 verify round 1: the carried posture record (F1), the inputs the rulings added to the tier pass (F2: the stance decision, the orders
// of the units below a group), and the boundaries and filters the verifier's plants found untested.
internal static partial class BattleLodTests
{
    private static (string, Action)[] AmendmentTests()
    {
        return new (string, Action)[]
        {
            ("bc15 CARRY a carried group answers fire control and recon with the Submitted flags of its last evaluation", CarriedRecordKeepsSubmittedFlags),
            ("bc15 CARRY the facing flag joins the record only after the facing pulse, and a group left with no posture is forgotten", RecordTakenAfterFacingPulse),
            ("bc15 CARRY fire control, recon and the facing pulse read exactly the flags the record keeps", ConsumersReadTheRecordedFlags),
            ("bc15 STANCE a change of the #45 cached decision, gained, changed or lost, promotes; an unchanged one does not", StanceDecisionPromotes),
            ("bc15 STANCE a change of the stance ordered on the group promotes", StanceOrderedPromotes),
            ("bc15 PENDING a refusal or a pending move of a regiment or battery under the group keeps it Full; a sibling's does not", UnsettledBelowKeepsGroupFull),
            ("bc15 PENDING a refusal of Duplicate, or none, below the group is the steady state", UnsettledBelowIgnoresDuplicate),
            ("bc15 PENDING the executor lists every pending move, retry ticket and refusal, and a retry of a regiment keeps its group Full", UnsettledListCoversEveryKind),
            ("bc15 PROMOTE (d) the 250 m distance boundary: exactly 250 promotes, 249 does not", BearingDistanceBoundary),
            ("bc15 PROMOTE (d) the 15 degree bearing boundary: exactly 15 promotes, just under does not", BearingTurnBoundary),
            ("bc15 TIER a reinforcement column's memory blocks nothing", ReinforcementMemoryBlocksNothing),
            ("bc15 PROMOTE a reinforcement column's sighting promotes nothing", ReinforcementSightingPromotesNothing),
            ("bc15 LATCH the drop log keeps only entries at the blocking confidence, and never a reinforcement", DropLogFilters),
            ("bc15 REASONS every LodPromotion flag is counted and has its name in the TacticalLod row", EveryReasonIsCountedAndNamed),
            ("bc15 CLAIM a lapsed claim of a producer that runs every tick wakes the Distant group that tick", LapsedClaimWakesTheGroup),
            ("bc15 CLAIM a renewed claim, a carried source, a due group and a Full group wake nothing", ClaimWakeControls),
            ("bc15 CLAIM the intent a lapsed claim hid executes on the tick it lapses, as at full rate", HiddenIntentSurfacesOnLapse),
            ("bc15 CLAIM the wake-up runs the tiered producers once more for the woken groups alone", WakePassIsWired),
        };
    }

    // ---- F1: the carried record ----

    private static GroupPosture Posture(bool submitted, bool facing)
    {
        return new GroupPosture { Has = true, Submitted = submitted, FacingSubmitted = facing, CloseEngaged = true, FlankRisk = true };
    }

    private static void CarriedRecordKeepsSubmittedFlags()
    {
        var cache = new PostureRecordCache();
        var table = new GroupPosture[4];
        table[1] = Posture(submitted: true, facing: false);   // a hold task with an order: resubmits Stance and Move every full-rate tick
        table[2] = Posture(submitted: false, facing: true);
        table[3] = Posture(submitted: false, facing: false);
        for (int g = 1; g <= 3; g++) cache.Remember(table, g, 70 + g);

        var next = new GroupPosture[4];   // the per-tick table is cleared every tick
        for (int g = 1; g <= 3; g++) AssertTrue(cache.Carry(next, g, 70 + g), "group " + g + " has a record");
        AssertTrue(next[1].Submitted && !next[1].FacingSubmitted, "Submitted survives a carried tick: fire control and the scout test stay out of the group");
        AssertTrue(!next[2].Submitted && next[2].FacingSubmitted, "FacingSubmitted survives too");
        AssertTrue(!next[3].Submitted && !next[3].FacingSubmitted, "a group full rate leaves alone is left alone");
        AssertTrue(next[1].Has && next[1].CloseEngaged && next[1].FlankRisk, "and the rest of the record is the one the evaluation made");
    }

    private static void RecordTakenAfterFacingPulse()
    {
        var cache = new PostureRecordCache();
        var table = new GroupPosture[2];
        table[0] = Posture(submitted: false, facing: false);
        cache.Remember(table, 0, 70);        // after the posture producer
        table[0].FacingSubmitted = true;     // the facing pulse ran
        cache.Remember(table, 0, 70);        // after the facing pulse
        var next = new GroupPosture[2];
        AssertTrue(cache.Carry(next, 0, 70) && next[0].FacingSubmitted, "the facing pulse's answer is in the record");

        table[0] = default(GroupPosture);    // the next evaluation left the group without a posture
        cache.Remember(table, 0, 70);
        var empty = new GroupPosture[2];
        AssertFalse(cache.Carry(empty, 0, 70), "a group left with no posture has no record to carry");
        AssertFalse(empty[0].Has, "and its row stays empty");
        table[0] = Posture(true, false);
        cache.Remember(table, 0, 70);
        AssertEqual(1, cache.Count, "one record");
        cache.Clear();
        AssertFalse(cache.Carry(new GroupPosture[2], 0, 70), "a stand-down clears them");
    }

    private static void ConsumersReadTheRecordedFlags()
    {
        string posture = Source(PostureFile);
        AssertTrue(posture.Contains("if (!posture.Has || posture.Submitted || posture.FacingSubmitted) continue;"), "fire control skips on the two recorded flags");
        string recon = Source("src/WhiskeyRealism/Tactical/Battle/Producers/ReconIntentProducer.cs");
        AssertTrue(recon.Contains("p.PostureMoved[i] = posture != null && posture.Groups[i].Submitted;"), "recon reads Submitted from the same table");
        string pulse = ClassText(PostureFile, "FacingPulseIntentProducer");
        // The whole statement, so a guard like "if (false && ...)" does not satisfy it.
        AssertTrue(Regex.IsMatch(pulse,
            @"g\.FacingSubmitted = submitted \|\| pulses > 0;\s*\}\s*(//[^\n]*\s*)*for \(int i = 0; i < s\.OwnCount; i\+\+\)\s*" +
            @"if \(t\.Groups\[i\]\.Has && BattleLodRuntime\.IsDue\(s, i\)\) t\.Records\.Remember\(t\.Groups, i, s\.Own\[i\]\.InstanceId\);"),
            "the record is taken after the facing flag is set, for the groups that ran");
        string record = Source("src/WhiskeyRealism/Tactical/Battle/PostureRecords.cs");
        AssertFalse(record.Contains("Submitted = false"), "a carried row never has its flags cleared");
    }

    // ---- F2: the stance inputs ----

    private static void StanceDecisionPromotes()
    {
        LodWorld gained = DistantNotDueWorld();
        gained.Tasks.Stance[100] = 7;
        AssertPromoted(gained, LodPromotion.Stance, "a decision appearing");

        LodWorld changed = DistantNotDueWorld(w => w.Tasks.Stance[100] = 7);
        changed.Tasks.Stance[100] = 8;
        AssertPromoted(changed, LodPromotion.Stance, "a decision changing");

        LodWorld lost = DistantNotDueWorld(w => w.Tasks.Stance[100] = 7);
        lost.Tasks.Stance.Remove(100);
        AssertPromoted(lost, LodPromotion.Stance, "a decision going");

        LodWorld same = DistantNotDueWorld(w => w.Tasks.Stance[100] = 7);
        AssertStays(same, "an unchanged decision");
    }

    private static void StanceOrderedPromotes()
    {
        LodWorld w = DistantNotDueWorld();
        w.Unit(100).AiStanceOrdered = 2;
        AssertPromoted(w, LodPromotion.Stance, "vanilla re-ordering the group's stance");
        LodWorld same = DistantNotDueWorld(x => x.Unit(100).AiStanceOrdered = 2);
        AssertStays(same, "the stance ordered staying the same");
    }

    // ---- F2: the orders of the units below a group ----

    private static void UnsettledBelowKeepsGroupFull()
    {
        foreach (string how in new[] { "refusal of a regiment", "pending move of a regiment", "refusal of a battery" })
        {
            LodWorld w = DistantNotDueWorld(null, brigades: 2);
            switch (how)
            {
                case "refusal of a regiment": w.Executor.NoteRefusal(1001, SkipReason.Cooldown); break;
                case "pending move of a regiment": IssueMove(w, 1002); break;
                case "refusal of a battery": w.Executor.NoteRefusal(1002, SkipReason.Water); break;
            }
            AssertPromoted(w, LodPromotion.Pending, how + " under the group");
            AssertEqual(LodTier.Distant, w.Tier(101), how + ": the sibling brigade, which has no such order below it, stays Distant");
        }

        // Held, not only promoted: a regiment's order that stays unsettled keeps the group Full tick after tick.
        LodWorld held = QuietWorld(1);
        for (int t = 0; t < 8; t++)
        {
            IssueMove(held, 1001);
            held.Tick();
            AssertEqual(LodTier.Full, held.Tier(100), "a pending regiment move keeps the group Full on tick " + t);
        }
    }

    private static void UnsettledBelowIgnoresDuplicate()
    {
        LodWorld w = DistantNotDueWorld();
        w.Executor.NoteRefusal(1001, SkipReason.Duplicate);
        w.Executor.NoteRefusal(1002, SkipReason.None);
        AssertStays(w, "a Duplicate or no reason below the group");
        AssertEqual(0, w.Executor.UnsettledCount, "nothing is listed unsettled");
    }

    private static void UnsettledListCoversEveryKind()
    {
        LodWorld w = DistantNotDueWorld();
        AssertEqual(0, w.Executor.UnsettledCount, "precondition: nothing in flight");
        IssueMove(w, 1001);
        IssueMove(w, 1002);
        AssertEqual(2, w.Executor.UnsettledCount, "two pending moves");
        AssertEqual("1001,1002", ListedUnsettled(w), "are listed under their units");
        var outcomes = new OrderOutcome[8];
        w.Executor.ObserveOutcomes(w.S, w.Hours, outcomes, out _);   // no path took: the moves fail and a retry ticket waits for each
        AssertEqual(2, w.Executor.UnsettledCount, "the retry tickets replace them");
        AssertEqual("1001,1002", ListedUnsettled(w), "under the same units");
        w.Executor.NoteRefusal(1011, SkipReason.Cooldown);
        w.Executor.NoteRefusal(1012, SkipReason.Cooldown);
        w.Executor.NoteRefusal(1012, SkipReason.Cooldown);
        AssertEqual(4, w.Executor.UnsettledCount, "two refusals, one noted twice, are listed once each");
        AssertEqual("1001,1002,1011,1012", ListedUnsettled(w), "after the tickets");
        AssertTrue(w.Executor.IsUnsettled(1001) && w.Executor.IsUnsettled(1011) && !w.Executor.IsUnsettled(100), "IsUnsettled stays the unit's own answer");
        AssertPromoted(w, LodPromotion.Pending, "retry tickets and refusals below the group");
    }

    private static string ListedUnsettled(LodWorld w)
    {
        var ids = new System.Collections.Generic.List<int>();
        for (int k = 0; k < w.Executor.UnsettledCount; k++) ids.Add(w.Executor.UnsettledId(k));
        ids.Sort();
        return string.Join(",", ids);
    }

    // ---- minors: boundaries and filters ----

    private static void BearingDistanceBoundary()
    {
        const float cx = 1000f, cz = 1000f;
        LodWorld exact = DistantNotDueWorld(w => w.SeeEnemy(9001, cx, cz + 3000f));
        exact.MoveEnemy(9001, cx, cz + 3250f);
        AssertPromoted(exact, LodPromotion.Bearing, "an enemy exactly 250 m further along the same bearing");

        LodWorld under = DistantNotDueWorld(w => w.SeeEnemy(9001, cx, cz + 3000f));
        under.MoveEnemy(9001, cx, cz + 3249f);
        AssertStays(under, "an enemy 249 m further");
    }

    private static void BearingTurnBoundary()
    {
        const float cx = 1000f, cz = 1000f;
        var start = Around(cx, cz, 3000f, 0f);
        float h0 = PostureSense.HeadingTo(cx, cz, start.x, start.z);
        float Turn(float degrees)
        {
            var p = Around(cx, cz, 3000f, degrees);
            float d = Math.Abs(h0 - PostureSense.HeadingTo(cx, cz, p.x, p.z)) % 360f;
            return d > 180f ? 360f - d : d;
        }

        // The bearing the pass computes is a float: find the point whose turn is exactly 15f, and one whose turn is just below it.
        float exactDegrees = float.NaN, belowDegrees = float.NaN;
        for (int k = -20000; k <= 20000 && float.IsNaN(exactDegrees); k++)
        {
            float degrees = 15f + k * 1e-6f;
            if (Turn(degrees) == 15f) exactDegrees = degrees;
        }
        AssertFalse(float.IsNaN(exactDegrees), "a point whose computed bearing change is exactly 15 degrees exists");
        for (int k = 1; k <= 20000 && float.IsNaN(belowDegrees); k++)
        {
            float degrees = exactDegrees - k * 1e-5f;
            if (Turn(degrees) < 15f && Turn(degrees) > 14.99f) belowDegrees = degrees;
        }
        AssertFalse(float.IsNaN(belowDegrees), "and one just under it");

        var exactPoint = Around(cx, cz, 3000f, exactDegrees);
        LodWorld exact = DistantNotDueWorld(w => w.SeeEnemy(9001, start.x, start.z));
        exact.MoveEnemy(9001, exactPoint.x, exactPoint.z);
        AssertPromoted(exact, LodPromotion.Bearing, "an enemy whose bearing changed by exactly 15 degrees");

        var belowPoint = Around(cx, cz, 3000f, belowDegrees);
        LodWorld under = DistantNotDueWorld(w => w.SeeEnemy(9001, start.x, start.z));
        under.MoveEnemy(9001, belowPoint.x, belowPoint.z);
        AssertStays(under, "an enemy whose bearing changed by just under 15 degrees");
    }

    private static void ReinforcementMemoryBlocksNothing()
    {
        LodWorld blocked = QuietWorld();
        blocked.RememberEnemy(9300, 1000f, 1400f, 0.9f);   // a confident memory 400 m north
        for (int t = 0; t < 8; t++)
        {
            blocked.Tick();
            AssertEqual(LodTier.Full, blocked.Tier(100), "control: a remembered enemy blocks Distant on tick " + t);
        }

        LodWorld column = QuietWorld();
        column.RememberEnemy(9300, 1000f, 1400f, 0.9f);
        column.Memory[column.IndexOfEnemy(9300)].IsReinforcement = true;
        column.TickUntilDistant(100);
        AssertEqual(LodTier.Distant, column.Tier(100), "the same memory of a reinforcement column does not");
    }

    private static void ReinforcementSightingPromotesNothing()
    {
        LodWorld column = DistantNotDueWorld();
        column.SeeEnemy(9301, 1000f, 3000f, reinforcement: true);   // 2,000 m north, inside the 3,000 m promotion radius
        AssertStays(column, "a reinforcement column coming into sight");

        LodWorld regular = DistantNotDueWorld();
        regular.SeeEnemy(9301, 1000f, 3000f);
        AssertPromoted(regular, LodPromotion.Observation, "control: the same sighting of a regiment");
    }

    private static void DropLogFilters()
    {
        foreach ((float confidence, bool logged) in new[] { (0.49f, false), (0.5f, true), (0.9f, true) })
        {
            var m = new LatchMemory();
            var p = new LatchProbe();
            m.Tick(p.Clear().Add(7, true, 100f, 200f), 10.0);
            int at = m.IndexOf(7);
            m.Items[at].State = ObservationState.Remembered;
            m.Items[at].Confidence01 = confidence;
            m.Items[at].HiddenTicks = BattlePerception.HideConfirmTicks;
            m.Consume();
            m.Tick(p.Clear().Add(7, true, 150f, 250f, routed: true), 10.0 + LatchMinute);
            AssertEqual(logged ? 1 : 0, m.Drops.Count, "a memory at confidence " + confidence + " seen routing: logged = " + logged);
        }

        var column = new LatchMemory();
        var probe = new LatchProbe();
        column.Tick(probe.Clear().Add(8, true, 100f, 200f), 10.0);
        column.Items[column.IndexOf(8)].IsReinforcement = true;
        column.Consume();
        column.Tick(probe.Clear().Add(8, true, 150f, 250f, routed: true), 10.0 + LatchMinute);
        AssertEqual(0, column.Drops.Count, "a reinforcement column seen routing leaves no drop");
    }

    // ---- N1: every promotion reason is counted and named ----

    private static string Kebab(string pascal) => Regex.Replace(pascal, "(?<=[a-z])(?=[A-Z])", "-").ToLowerInvariant();

    private static void EveryReasonIsCountedAndNamed()
    {
        string runtime = Source(RuntimeFile);
        Match initializer = Regex.Match(runtime, @"ReasonNames\s*=\s*\{([^}]*)\}");
        AssertTrue(initializer.Success, "ReasonNames is in the runtime source");
        var names = new List<string>();
        foreach (Match m in Regex.Matches(initializer.Groups[1].Value, "\"([^\"]+)\"")) names.Add(m.Groups[1].Value);

        int flags = 0, highestBit = -1;
        foreach (LodPromotion flag in Enum.GetValues(typeof(LodPromotion)))
        {
            if (flag == LodPromotion.None) continue;
            flags++;
            int bit = 0;
            for (int v = (int)flag; v > 1; v >>= 1) bit++;
            if (bit > highestBit) highestBit = bit;

            var state = new BattleLodState();
            state.CountPromotion(flag);
            AssertEqual(1, state.PromotionCountOf(flag), flag + " is counted");
            for (int other = 0; other <= highestBit; other++)
                if ((1 << other) != (int)flag) AssertEqual(0, state.PromotionCountOf((LodPromotion)(1 << other)), flag + " counts into no other reason");
            AssertTrue(bit < names.Count, flag + " has a slot in ReasonNames (" + names.Count + " names)");
            AssertEqual(Kebab(flag.ToString()), names[bit], flag + " is named in the TacticalLod row");
        }
        AssertEqual(flags, names.Count, "ReasonNames names every flag and nothing else");
        AssertEqual(highestBit + 1, LodReasons.Count, "the counter array is as wide as the highest flag");
        AssertTrue(!Regex.IsMatch(Source("src/WhiskeyRealism/Tactical/Battle/BattleLodState.cs"), @"LodPromotionReasons\s*=\s*\d"), "and the width is never typed by hand");
        AssertTrue(runtime.Contains("for (int bit = 0; bit < ReasonNames.Length; bit++)"), "the row walks every name");
    }

    // ---- decision 14: a lapsed claim wakes the group ----

    private static BattleOrderIntent PlayClaim(int group, OrderChannel channel, IntentSource source = IntentSource.DivisionPlay,
        IntentPriority priority = IntentPriority.Play)
    {
        return new BattleOrderIntent { GroupId = group, Channel = channel, Priority = priority, Source = source, Heading = -1f, Value = 1, Task = CommandTaskType.HoldObjective, Reason = "test" };
    }

    private static void LapsedClaimWakesTheGroup()
    {
        foreach (OrderChannel channel in new[] { OrderChannel.Stance, OrderChannel.Move, OrderChannel.Facing })
        {
            LodWorld w = DistantNotDueWorld();
            int index = w.S.IndexOfOwn(100);
            OrderArbiter arbiter = PublishedArbiter(PlayClaim(100, channel));   // last tick's published claim of the division play
            arbiter.BeginTick();                                                // this tick: the play submits nothing
            AssertTrue(arbiter.HasLapsedUncarriedClaim(100), channel + ": the claim was not renewed");
            AssertEqual(1, BattleLod.WakeLapsedClaims(w.S, w.State, arbiter, w.Hours), channel + ": one group woken");
            AssertTrue(w.State.IsDue(index) && w.State.IsClaimWoken(index), channel + ": due this tick, and named for the second pass");
            AssertEqual(LodTier.Full, w.State.TierOf(index), channel + ": Full");
            AssertEqual(LodPromotion.Claim, w.State.LastPromotion(index), channel + ": for the claim");
            AssertEqual(1, w.State.PromotionCountOf(LodPromotion.Claim), channel + ": counted in the TacticalLod window");
            AssertEqual(0, BattleLod.WakeLapsedClaims(w.S, w.State, arbiter, w.Hours), channel + ": woken once");
        }

        // Another source taking the cell is a change of source, and the claim of the first is gone.
        LodWorld changed = DistantNotDueWorld();
        OrderArbiter other = PublishedArbiter(PlayClaim(100, OrderChannel.Stance));
        other.BeginTick();
        other.Submit(PlayClaim(100, OrderChannel.Stance, IntentSource.Withdrawal, IntentPriority.Safety));
        AssertEqual(1, BattleLod.WakeLapsedClaims(changed.S, changed.State, other, changed.Hours), "a change of source wakes the group");

        // The woken group's carried cells are dropped, so the second pass starts from an empty table.
        LodWorld dropped = DistantNotDueWorld();
        OrderArbiter carried = PublishedArbiter(PlayClaim(100, OrderChannel.Stance), PlayClaim(100, OrderChannel.Move, IntentSource.PostureExecutor, IntentPriority.Executor));
        carried.BeginTick();
        AssertEqual(1, carried.CarryForward(100), "precondition: the posture's Move cell is carried");
        BattleLod.WakeLapsedClaims(dropped.S, dropped.State, carried, dropped.Hours);
        carried.Resolve(new BattleOrderIntent[8]);
        AssertFalse(carried.TryGetClaim(100, OrderChannel.Move, out _), "and dropped when the group is woken");
    }

    private static void ClaimWakeControls()
    {
        LodWorld renewed = DistantNotDueWorld();
        OrderArbiter a = PublishedArbiter(PlayClaim(100, OrderChannel.Stance));
        a.BeginTick();
        a.Submit(PlayClaim(100, OrderChannel.Stance));
        AssertFalse(a.HasLapsedUncarriedClaim(100), "a claim the same source renews");
        AssertEqual(0, BattleLod.WakeLapsedClaims(renewed.S, renewed.State, a, renewed.Hours), "wakes nothing");

        LodWorld carriedSource = DistantNotDueWorld();
        OrderArbiter b = PublishedArbiter(PlayClaim(100, OrderChannel.Stance, IntentSource.PostureExecutor, IntentPriority.Executor));
        b.BeginTick();
        AssertFalse(b.HasLapsedUncarriedClaim(100), "a claim of a tiered source is carried, not lapsed");
        AssertEqual(0, BattleLod.WakeLapsedClaims(carriedSource.S, carriedSource.State, b, carriedSource.Hours), "wakes nothing");

        LodWorld otherGroup = DistantNotDueWorld(null, brigades: 2);
        OrderArbiter c = PublishedArbiter(PlayClaim(101, OrderChannel.Stance));
        c.BeginTick();
        AssertEqual(1, BattleLod.WakeLapsedClaims(otherGroup.S, otherGroup.State, c, otherGroup.Hours), "the claim of group 101 wakes group 101");
        AssertEqual(LodTier.Full, otherGroup.State.TierOf(otherGroup.S.IndexOfOwn(101)), "that group");
        AssertEqual(LodTier.Distant, otherGroup.State.TierOf(otherGroup.S.IndexOfOwn(100)), "and not its neighbour");
        AssertTrue(otherGroup.State.IsClaimWoken(otherGroup.S.IndexOfOwn(101)) && !otherGroup.State.IsClaimWoken(otherGroup.S.IndexOfOwn(100)),
            "only the woken group is named for the second pass");
        otherGroup.Tick();
        AssertFalse(otherGroup.State.IsClaimWoken(otherGroup.S.IndexOfOwn(101)), "and the name lasts one tick");

        // The renewal must be the same source, on the same cell of the same group.
        foreach (string how in new[] { "another channel", "another group", "another source" })
        {
            LodWorld w = DistantNotDueWorld(null, brigades: 2);
            OrderArbiter x = PublishedArbiter(PlayClaim(100, OrderChannel.Stance));
            x.BeginTick();
            switch (how)
            {
                case "another channel": x.Submit(PlayClaim(100, OrderChannel.Move)); break;
                case "another group": x.Submit(PlayClaim(101, OrderChannel.Stance)); break;
                default: x.Submit(PlayClaim(100, OrderChannel.Stance, IntentSource.Withdrawal, IntentPriority.Safety)); break;
            }
            AssertTrue(x.HasLapsedUncarriedClaim(100), "a renewal on " + how + " is no renewal");
        }

        // A due group already runs its producers, a Full group always does: neither is touched.
        LodWorld full = QuietWorld();
        full.Tick();
        OrderArbiter d = PublishedArbiter(PlayClaim(100, OrderChannel.Stance));
        d.BeginTick();
        AssertEqual(0, BattleLod.WakeLapsedClaims(full.S, full.State, d, full.Hours), "a Full group");
        LodWorld due = QuietWorld();
        due.TickUntilDistant(100);
        while (!due.Due(100)) due.Tick();
        OrderArbiter e = PublishedArbiter(PlayClaim(100, OrderChannel.Stance));
        e.BeginTick();
        AssertEqual(0, BattleLod.WakeLapsedClaims(due.S, due.State, e, due.Hours), "a Distant group on its due tick");

        // A snapshot the pass did not classify is not woken (the flag is off, or the pass failed).
        LodWorld unclassified = DistantNotDueWorld();
        unclassified.Build();
        OrderArbiter f = PublishedArbiter(PlayClaim(100, OrderChannel.Stance));
        f.BeginTick();
        AssertEqual(0, BattleLod.WakeLapsedClaims(unclassified.S, unclassified.State, f, unclassified.Hours), "a snapshot no pass covered");
    }

    // A reference for the group stance (tiered: Scorer priority, submitted by due groups only) and the division play (Full-only: Play priority on
    // the stance cell while it holds). At full rate the stance intent is hidden while the play holds and wins on the tick the play ends.
    private static List<int> StanceWinnerTicks(bool tiers, bool wake, int playFrom, int playUntil, out int evaluations)
    {
        evaluations = 0;
        LodWorld w = QuietWorld();
        var arbiter = new OrderArbiter();
        var winners = new BattleOrderIntent[16];
        var ticks = new List<int>();
        for (int tick = 0; tick < 24; tick++)
        {
            if (tiers) w.Tick();
            else { w.Hours += w.CycleHours; w.Build(); }
            arbiter.BeginTick();
            int index = w.S.IndexOfOwn(100);
            bool due = !tiers || w.State.IsDue(index);
            if (tiers && !due) arbiter.CarryForward(100);
            if (due)
            {
                evaluations++;
                arbiter.Submit(PlayClaim(100, OrderChannel.Stance, IntentSource.GroupStanceScorer, IntentPriority.Scorer));
            }
            if (tick >= playFrom && tick < playUntil) arbiter.Submit(PlayClaim(100, OrderChannel.Stance));
            if (tiers && wake && BattleLod.WakeLapsedClaims(w.S, w.State, arbiter, w.Hours) > 0 && w.State.IsClaimWoken(index))
            {
                evaluations++;
                arbiter.Submit(PlayClaim(100, OrderChannel.Stance, IntentSource.GroupStanceScorer, IntentPriority.Scorer));   // the second pass
            }
            int count = arbiter.Resolve(winners);
            for (int k = 0; k < count; k++)
                if (winners[k].GroupId == 100 && winners[k].Channel == OrderChannel.Stance && winners[k].Source == IntentSource.GroupStanceScorer) ticks.Add(tick);
        }
        return ticks;
    }

    private static void HiddenIntentSurfacesOnLapse()
    {
        int controlMisses = 0, evaluationsFull = 0, evaluationsTiered = 0;
        // The play holds for four ticks and lapses at tick L; the phase of the group's due ticks differs with L, so try four consecutive ones.
        for (int lapse = 12; lapse < 16; lapse++)
        {
            List<int> fullRate = StanceWinnerTicks(tiers: false, wake: false, playFrom: lapse - 4, playUntil: lapse, out int fullEvaluations);
            AssertTrue(fullRate.Contains(lapse), "lapse at " + lapse + ": at full rate the stance intent wins on the tick the play ends");
            AssertFalse(fullRate.Contains(lapse - 1), "lapse at " + lapse + ": and is hidden the tick before");

            List<int> woken = StanceWinnerTicks(tiers: true, wake: true, playFrom: lapse - 4, playUntil: lapse, out int tieredEvaluations);
            evaluationsFull += fullEvaluations;
            evaluationsTiered += tieredEvaluations;
            AssertTrue(woken.Contains(lapse), "lapse at " + lapse + ": with the tiers it wins on that same tick");
            AssertFalse(woken.Contains(lapse - 1), "lapse at " + lapse + ": the play still hides it while it holds");

            if (!StanceWinnerTicks(tiers: true, wake: false, playFrom: lapse - 4, playUntil: lapse, out _).Contains(lapse)) controlMisses++;
        }
        Console.WriteLine("INFO bc15 CLAIM stance evaluations over 4 x 24 ticks with a division-play claim lapsing: at full rate " + evaluationsFull + ", with the tiers and the wake-up " + evaluationsTiered
            + " (" + (100 * evaluationsTiered / evaluationsFull) + " %)");
        AssertTrue(controlMisses >= 1, "without the wake-up the intent waits for the group's next due tick in at least one phase (" + controlMisses + " of 4)");
    }

    private static void WakePassIsWired()
    {
        string tick = Source(TickFile);
        int producers = At(tick, "producer.Produce(snapshot, arbiter);");
        int wake = At(tick, "BattleLodRuntime.WakeLapsedClaims(snapshot, arbiter, now) > 0", producers);
        int begin = At(tick, "BattleLodRuntime.BeginWakePass();", wake);
        int tiered = At(tick, "if (!BattleLodRuntime.IsTieredProducer(producer.Name)) continue;", begin);
        int end = At(tick, "BattleLodRuntime.EndWakePass();", tiered);
        int resolve = At(tick, "winners = arbiter.Resolve(_winners);", end);
        AssertTrue(producers < wake && wake < begin && begin < tiered && tiered < end && end < resolve,
            "after the producers, before Resolve: wake, then the tiered producers once more, then the pass ends in a finally");
        AssertTrue(Regex.IsMatch(tick, @"finally\s*\{\s*BattleLodRuntime\.EndWakePass\(\);\s*\}"), "the pass always ends");

        string runtime = Source(RuntimeFile);
        AssertTrue(Regex.IsMatch(runtime, @"if \(_wakePass\) return state != null && state\.IsClaimWoken\(ownIndex\);"), "in the second pass IsDue speaks for the woken groups alone");
        AssertTrue(runtime.Contains("return name == \"GroupStance\" || name == \"Posture\" || name == \"FacingPulse\";"), "the tiered producers are the three that are gated");
        AssertTrue(runtime.Contains("tick.Groups[i] = default(GroupPosture);"), "a woken group's carried posture row is emptied for the second pass");
        AssertTrue(runtime.Contains("BattleLod.WakeLapsedClaims(s, state, arbiter, nowHours)"), "the runtime asks the pure wake-up");
        int wakeAt = At(runtime, "internal static int WakeLapsedClaims(");
        AssertTrue(runtime.IndexOf("Plugin.BattleAiLevelOfDetailEnabled()", wakeAt, StringComparison.Ordinal) is int flagAt && flagAt > wakeAt &&
            flagAt < At(runtime, "BattleLod.WakeLapsedClaims(s, state, arbiter, nowHours)", wakeAt), "and only with the flag on");
        AssertTrue(Regex.IsMatch(Source(PostureFile), @"if \(!BattleLodRuntime\.InWakePass\) t\.Records\.Carry\(t\.Groups, i, rec\.InstanceId\);"),
            "the second pass does not put last tick's row over a group that ran in the first");
    }
}
