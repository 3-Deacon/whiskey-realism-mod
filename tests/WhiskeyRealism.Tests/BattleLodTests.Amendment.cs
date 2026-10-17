using System;
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
        int set = At(pulse, "g.FacingSubmitted = submitted || pulses > 0;");
        int remember = At(pulse, "t.Records.Remember(t.Groups, i, s.Own[i].InstanceId);", set);
        AssertTrue(set < remember && pulse.Contains("t.Groups[i].Has && BattleLodRuntime.IsDue(s, i)"), "the record is taken after the facing flag is set, for the groups that ran");
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
}
