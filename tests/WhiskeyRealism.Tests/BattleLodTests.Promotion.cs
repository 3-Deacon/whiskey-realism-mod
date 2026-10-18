using System;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using static TestHarness;

// BC15 decision 2: every input the tiered producers read is either a Distant condition or a promotion trigger. One case per trigger, each
// changing only that input, each over the threshold (promoted on that same tick, for that reason) and under it (not promoted).
internal static partial class BattleLodTests
{
    private static (string, Action)[] PromotionTests()
    {
        return new (string, Action)[]
        {
            ("bc15 PROMOTE ContactEventNeverDelayedByTier: a new sighting 1,200 m away", ContactNear),
            ("bc15 PROMOTE ContactEventNeverDelayedByTier: a new sighting 2,800 m away", ContactFar),
            ("bc15 PROMOTE ContactEventNeverDelayedByTier: fire with no observation change", ContactFire),
            ("bc15 PROMOTE ContactEventNeverDelayedByTier: the sighting is made on a build between two arbiter ticks", ContactBetweenBuilds),
            ("bc15 PROMOTE (a) a change beyond 3,000 m promotes nothing; a drop within it promotes; an overflow promotes everyone", TriggerObservation),
            ("bc15 PROMOTE the change and remembered lists keep their entries when they grow (regression)", ListsKeepTheirEntriesWhenTheyGrow),
            ("bc15 PROMOTE (e) the corridor's count is read on its own, whatever its hash", CorridorCountIsReadAlone),
            ("bc15 PROMOTE (a) a change is read once: the tick after it, the group can be Distant again", ObservationIsReadOnce),
            ("bc15 PROMOTE (b) the command parent moving, pending or unsettled promotes its children", TriggerParentActivity),
            ("bc15 PROMOTE (b) the layout slot moving more than 60 m or turning more than 15 degrees promotes", TriggerSlot),
            ("bc15 PROMOTE ParentPromotionDoesNotCascade: a division promoted for its own reason leaves a settled child Distant", ParentPromotionDoesNotCascade),
            ("bc15 PROMOTE (c) the order target moving more than 60 m, its task, formation or maneuver heading changing promotes", TriggerTarget),
            ("bc15 PROMOTE (d) the closest visible enemy changing bearing by 15 degrees or distance by 250 m, or being gained or lost, promotes", TriggerBearing),
            ("bc15 PROMOTE PromotionRateBoundedUnderReSortingEnemyLine: the closest unit alternating without a bearing or distance change", ReSortingEnemyLineDoesNotPromote),
            ("bc15 PROMOTE (e) a friendly unit entering or leaving the path corridor promotes", TriggerCorridor),
            ("bc15 PROMOTE PlanChangePromotesWholeSide", PlanChangePromotesWholeSide),
            ("bc15 PROMOTE a promotion is read once per tick and reported with its reason", PromotionReasonsAreReported),
        };
    }

    // A quiet world whose group 100 is Distant on a tick it is not due, ready to have one input changed.
    private static LodWorld DistantNotDueWorld(Action<LodWorld> prepare = null, int brigades = 1)
    {
        LodWorld w = QuietWorld(brigades);
        prepare?.Invoke(w);
        for (int t = 0; t < 12; t++)
        {
            w.Tick();
            if (t >= 2 && w.Tier(100) == LodTier.Distant && !w.Due(100)) return w;
        }
        throw new Exception("group 100 never reached a Distant tick that is not due");
    }

    private static void AssertPromoted(LodWorld w, LodPromotion reason, string label)
    {
        bool single = reason != LodPromotion.None && ((int)reason & ((int)reason - 1)) == 0;
        int counted = single ? w.State.PromotionCountOf(reason) : 0;
        w.Tick();
        if (single) AssertTrue(w.State.PromotionCountOf(reason) > counted, label + ": " + reason + " is counted for the TacticalLod row");
        AssertEqual(LodTier.Full, w.Tier(100), label + ": promoted on the tick the input changed");
        AssertTrue(w.Due(100), label + ": its producers run that tick");
        AssertTrue((w.Why(100) & reason) == reason, label + ": for " + reason + ", reported " + w.Why(100));
    }

    private static void AssertStays(LodWorld w, string label)
    {
        // Two ticks cover a due tick, whichever phase the group is in.
        for (int t = 0; t < 2; t++)
        {
            w.Tick();
            AssertEqual(LodTier.Distant, w.Tier(100), label + ": stays Distant (tick " + t + ", reported " + w.Why(100) + ")");
        }
    }

    private static void ContactNear()
    {
        LodWorld w = DistantNotDueWorld();
        w.SeeEnemy(9001, 1000f, 2200f);   // 1,200 m: inside DistantMeters and PromoteMeters
        AssertPromoted(w, LodPromotion.Distance, "sighting at 1,200 m");
        AssertTrue(w.Due(1001) && w.Due(1002), "and its regiments run too");
    }

    private static void ContactFar()
    {
        LodWorld w = DistantNotDueWorld();
        w.SeeEnemy(9001, 1000f, 3800f);   // 2,800 m: beyond DistantMeters, inside PromoteMeters
        AssertPromoted(w, LodPromotion.Observation, "sighting at 2,800 m");
        AssertEqual(LodPromotion.None, w.Why(100) & LodPromotion.Distance, "it is not a near enemy");
    }

    private static void ContactFire()
    {
        LodWorld w = DistantNotDueWorld();
        w.Unit(1002).ReceivedFire = true;
        AssertPromoted(w, LodPromotion.Fire, "fire received");
    }

    private static void ContactBetweenBuilds()
    {
        LodWorld w = DistantNotDueWorld();
        w.SeeEnemy(9001, 1000f, 3300f);   // the build of a deployment or gated-off tick sees the enemy first
        w.Build();
        w.SeeEnemy(9001, 1000f, 3300f);   // a second build before any arbiter tick: nothing new, the first sighting must survive
        w.Build();
        w.Classify();
        AssertEqual(LodTier.Full, w.Tier(100), "promoted on the next arbiter tick although the sighting was two builds ago");
        AssertTrue((w.Why(100) & LodPromotion.Observation) != 0, "for the observation, reported " + w.Why(100));
    }

    private static void TriggerObservation()
    {
        // Beyond PromoteMeters: no trigger. (An enemy that is the group's closest visible one is trigger (d)'s, at any range; here a nearer
        // enemy stays the closest and a second one appears 3,300 m away.)
        LodWorld far = DistantNotDueWorld(w => w.SeeEnemy(9000, 1000f, 3000f));
        far.SeeEnemy(9001, 1000f, 1000f + BattleLod.PromoteMeters + 300f);
        AssertStays(far, "a sighting 3,300 m away");

        // A visible enemy within 3,000 m that is then seen routing: its drop is a change at its last position.
        LodWorld drop = DistantNotDueWorld(w => w.SeeEnemy(9001, 1000f, 3500f));
        drop.ForgetEnemy(9001);
        AssertPromoted(drop, LodPromotion.Observation, "an enemy seen routing within 3,000 m");

        // More drops than the log holds: everything is a change.
        LodWorld flood = DistantNotDueWorld();
        for (int d = 0; d <= ObservationDropLog.MaxEntries; d++) flood.Drops.Add(9000f, 9000f);
        AssertTrue(flood.Drops.Overflow, "precondition: the log overflowed");
        AssertPromoted(flood, LodPromotion.Observation, "an overflowed drop log");
    }

    // Found by the overflow plant: the change and remembered lists grew without copying, so every entry past the first 64 read as (0, 0).
    private static void ListsKeepTheirEntriesWhenTheyGrow()
    {
        LodWorld drops = DistantNotDueWorld();
        for (int d = 0; d < 100; d++) drops.Drops.Add(9000f, 9000f);
        AssertFalse(drops.Drops.Overflow, "precondition: 100 drops fit the log");
        AssertStays(drops, "100 drops far away");

        // The lists are rewritten every tick, so the growth only shows on the tick it happens: add the memories to a group that is already Distant.
        LodWorld memories = DistantNotDueWorld();
        for (int e = 0; e < 70; e++) memories.RememberEnemy(9200 + e, 9000f + e, 9000f, 0.9f);
        AssertStays(memories, "70 strong memories far away");
    }

    // The corridor's count and its id hash are two signals: a corridor whose hash cannot tell two sets apart is still read by its count.
    private sealed class CountOnlyCorridor : IFriendlyCorridor
    {
        public int Count;
        public int Signature(int selfId, float x0, float z0, float x1, float z1, float widthMeters, out int count)
        {
            count = Count;
            return 0;
        }
    }

    private static void CorridorCountIsReadAlone()
    {
        var corridor = new CountOnlyCorridor { Count = 1 };
        LodWorld w = DistantNotDueWorld(x => x.Tasks.Target[100] = (1000f, 1800f, -1f, -1));
        LodSideInput side = w.Side();
        side.Friendly = corridor;
        // Re-capture the signature under the count-only corridor on a Full tick, then settle again.
        w.Tasks.Target[100] = (1000f, 1801f, -1f, -1);
        for (int t = 0; t < 8; t++)
        {
            w.Hours += w.CycleHours;
            w.Build();
            BattleLod.Classify(w.S, w.State, side, w.Hours);
            BattlePerception.ClearChangeLatch(w.Memory, w.MemoryCount, w.Drops);
        }
        AssertEqual(LodTier.Distant, w.Tier(100), "precondition: Distant under the count-only corridor");
        corridor.Count = 2;
        int guard = 0;
        while (w.Due(100) && guard++ < 4)
        {
            w.Hours += w.CycleHours;
            w.Build();
            BattleLod.Classify(w.S, w.State, side, w.Hours);
        }
        corridor.Count = 3;
        w.Hours += w.CycleHours;
        w.Build();
        BattleLod.Classify(w.S, w.State, side, w.Hours);
        AssertEqual(LodTier.Full, w.Tier(100), "a changed count with an unchanged hash promotes");
        AssertTrue((w.Why(100) & LodPromotion.Corridor) != 0, "for the corridor, reported " + w.Why(100));
    }

    private static void ObservationIsReadOnce()
    {
        LodWorld w = DistantNotDueWorld();
        w.SeeEnemy(9001, 1000f, 3800f);
        w.Tick();
        AssertEqual(LodTier.Full, w.Tier(100), "promoted by the sighting");
        for (int t = 0; t < 6; t++) w.Tick();
        AssertEqual(LodTier.Distant, w.Tier(100), "the sighting was consumed by the tier pass: the group is Distant again");
    }

    private static void TriggerParentActivity()
    {
        foreach (string how in new[] { "path", "moving descendant", "queued orders", "refusal", "pending move" })
        {
            LodWorld w = QuietWorld(2);
            w.Division(50, 0, 1075f, 1000f);
            w.Unit(100).ParentId = 50;
            w.Unit(101).ParentId = 50;
            for (int t = 0; t < 6; t++) w.Tick();
            while (w.Due(100)) w.Tick();
            AssertEqual(LodTier.Distant, w.Tier(100), how + ": precondition, a settled child");
            switch (how)
            {
                case "path": w.Unit(50).RegimentPaths = 1; break;
                case "moving descendant": w.Unit(1011).RegimentPaths = 1; break;   // a sibling's regiment walks
                case "queued orders": w.Unit(50).QueuedOrders = 1; break;
                case "refusal": w.Executor.NoteRefusal(50, SkipReason.Cooldown); break;
                case "pending move": IssueMove(w, 50); break;
            }
            AssertPromoted(w, LodPromotion.Parent, "parent " + how);
        }
    }

    private static void TriggerSlot()
    {
        float x = 1000f, z = 1400f;   // a slot 400 m in front of the brigade, so the corridor to it is not degenerate
        // Under the thresholds: a slot that moves 30 m and turns 10 degrees changes nothing.
        LodWorld quiet = DistantNotDueWorld(w => w.Tasks.Slot[100] = (x, z, 90f));
        quiet.Tasks.Slot[100] = (x + 30f, z, 100f);
        AssertStays(quiet, "a slot moving 30 m and turning 10 degrees");

        LodWorld moved = DistantNotDueWorld(w => w.Tasks.Slot[100] = (x, z, 90f));
        moved.Tasks.Slot[100] = (x + 80f, z, 90f);
        AssertPromoted(moved, LodPromotion.Parent, "a slot moving 80 m");

        LodWorld turned = DistantNotDueWorld(w => w.Tasks.Slot[100] = (x, z, 90f));
        turned.Tasks.Slot[100] = (x, z, 110f);
        AssertPromoted(turned, LodPromotion.Parent, "a slot turning 20 degrees");

        LodWorld wrapped = DistantNotDueWorld(w => w.Tasks.Slot[100] = (x, z, 355f));
        wrapped.Tasks.Slot[100] = (x, z, 5f);
        AssertStays(wrapped, "a slot turning 10 degrees across north");

        LodWorld gained = DistantNotDueWorld();
        gained.Tasks.Slot[100] = (x, z, 90f);
        AssertPromoted(gained, LodPromotion.Parent, "a slot gained");

        LodWorld lost = DistantNotDueWorld(w => w.Tasks.Slot[100] = (x, z, 90f));
        lost.Tasks.Slot.Remove(100);
        AssertPromoted(lost, LodPromotion.Parent, "a slot lost");
    }

    private static void ParentPromotionDoesNotCascade()
    {
        LodWorld w = QuietWorld(2);
        w.Division(50, 0, 1075f, 1000f);
        w.Unit(100).ParentId = 50;
        w.Unit(101).ParentId = 50;
        w.UseCorridor = false;
        w.Tasks.Target[50] = (1075f, 1500f, -1f, -1);
        for (int t = 0; t < 6; t++) w.Tick();
        AssertEqual(LodTier.Distant, w.Tier(50), "precondition: the division is Distant");
        AssertEqual(LodTier.Distant, w.Tier(100), "precondition: so are its brigades");
        w.Tasks.Target[50] = (1075f, 1900f, -1f, -1);   // the division's own order target moves 400 m
        w.Tick();
        AssertEqual(LodTier.Full, w.Tier(50), "the division is promoted by its own trigger (c)");
        AssertEqual(LodTier.Distant, w.Tier(100), "its settled child brigade is not (it reads the parent's activity and its slot, not its tier)");
        AssertEqual(LodTier.Distant, w.Tier(101), "nor its sibling");
    }

    private static void TriggerTarget()
    {
        float tx = 1000f, tz = 1600f;
        Func<LodWorld> baseline = () =>
        {
            LodWorld w = DistantNotDueWorld(x => { x.UseCorridor = false; x.Tasks.Target[100] = (tx, tz, -1f, -1); });
            return w;
        };

        LodWorld small = baseline();
        small.Tasks.Target[100] = (tx + 40f, tz + 20f, -1f, -1);   // 44.7 m
        AssertStays(small, "a target moving 45 m");

        LodWorld moved = baseline();
        moved.Tasks.Target[100] = (tx, tz + 70f, -1f, -1);
        AssertPromoted(moved, LodPromotion.Target, "a target moving 70 m");

        LodWorld lost = baseline();
        lost.Tasks.Target.Remove(100);
        AssertPromoted(lost, LodPromotion.Target, "a target lost");

        LodWorld gained = DistantNotDueWorld(x => x.UseCorridor = false);
        gained.Tasks.Target[100] = (tx, tz, -1f, -1);
        AssertPromoted(gained, LodPromotion.Target, "a target gained");

        LodWorld task = baseline();
        task.Tasks.Task[100] = CommandTaskType.Consolidate;   // another static task: still the group's decision changes
        AssertPromoted(task, LodPromotion.Target, "the task changed to another static task");

        LodWorld formation = baseline();
        formation.Tasks.Target[100] = (tx, tz, -1f, 4);        // BC14W: the order gained a formation
        AssertPromoted(formation, LodPromotion.Target, "the order gained a formation (OrderFormationOnDistantGroupPromoted)");

        LodWorld headingGained = baseline();
        headingGained.Tasks.Target[100] = (tx, tz, 80f, -1);
        AssertPromoted(headingGained, LodPromotion.Target, "a maneuver heading gained");

        LodWorld headingTurned = DistantNotDueWorld(x => { x.UseCorridor = false; x.Tasks.Target[100] = (tx, tz, 80f, -1); });
        headingTurned.Tasks.Target[100] = (tx, tz, 100f, -1);
        AssertPromoted(headingTurned, LodPromotion.Target, "a maneuver heading turned 20 degrees");

        LodWorld headingSmall = DistantNotDueWorld(x => { x.UseCorridor = false; x.Tasks.Target[100] = (tx, tz, 80f, -1); });
        headingSmall.Tasks.Target[100] = (tx, tz, 90f, -1);
        AssertStays(headingSmall, "a maneuver heading turned 10 degrees");
    }

    private static (float x, float z) Around(float cx, float cz, float radius, float degrees)
    {
        double r = degrees * Math.PI / 180.0;
        return (cx + (float)(radius * Math.Sin(r)), cz + (float)(radius * Math.Cos(r)));
    }

    private static void TriggerBearing()
    {
        // The enemy stands 3,000 m due north of the brigade (1,000, 1,000): beyond the distant radius, a bearing and a distance to track.
        const float cx = 1000f, cz = 1000f;
        var start = Around(cx, cz, 3000f, 0f);

        LodWorld small = DistantNotDueWorld(w => w.SeeEnemy(9001, start.x, start.z));
        var near = Around(cx, cz, 3100f, 8f);                    // 8 degrees, 100 m
        small.MoveEnemy(9001, near.x, near.z);
        AssertStays(small, "an enemy moving 8 degrees and 100 m");

        LodWorld turned = DistantNotDueWorld(w => w.SeeEnemy(9001, start.x, start.z));
        var side = Around(cx, cz, 3000f, 20f);                   // 20 degrees at the same range
        turned.MoveEnemy(9001, side.x, side.z);
        AssertPromoted(turned, LodPromotion.Bearing, "an enemy crossing laterally by 20 degrees");

        LodWorld nearer = DistantNotDueWorld(w => w.SeeEnemy(9001, start.x, start.z));
        var closer = Around(cx, cz, 3300f, 0f);                  // 300 m further along the same bearing
        nearer.MoveEnemy(9001, closer.x, closer.z);
        AssertPromoted(nearer, LodPromotion.Bearing, "an enemy 300 m further away on the same bearing");

        LodWorld gained = DistantNotDueWorld();
        gained.SeeEnemy(9001, start.x, start.z);
        AssertPromoted(gained, LodPromotion.Bearing, "a closest visible enemy gained");

        LodWorld lost = DistantNotDueWorld(w => w.SeeEnemy(9001, start.x, start.z));
        lost.ForgetEnemy(9001);
        AssertPromoted(lost, LodPromotion.Bearing, "a closest visible enemy lost");
    }

    // A perceived enemy line: the unit that is closest alternates tick to tick, the line itself does not move. The facing and the threat target act on
    // the bearing and distance of the closest, which stay inside the thresholds, so the group stays Distant however often the identity changes.
    private static void ReSortingEnemyLineDoesNotPromote()
    {
        const float cx = 1000f, cz = 1000f;
        LodWorld w = DistantNotDueWorld(x =>
        {
            for (int e = 0; e < 6; e++)
            {
                var p = Around(cx + e * 60f, cz, 3000f, 0f);
                x.SeeEnemy(9100 + e, p.x, p.z);
            }
        });
        for (int t = 0; t < 40; t++)
        {
            // Two units of the line trade places as the closest by a few metres.
            int near = 9100 + (t % 2);
            int far = 9100 + ((t + 1) % 2);
            w.MoveEnemy(near, w.Memory[w.IndexOfEnemy(near)].X, cz + 2995f);
            w.MoveEnemy(far, w.Memory[w.IndexOfEnemy(far)].X, cz + 3005f);
            w.Tick();
            AssertEqual(LodTier.Distant, w.Tier(100), "the alternating closest unit promotes nothing, tick " + t + " (" + w.Why(100) + ")");
        }
    }

    private static void TriggerCorridor()
    {
        const float tz = 1800f;
        LodWorld entering = DistantNotDueWorld(w => w.Tasks.Target[100] = (1000f, tz, -1f, -1));
        entering.Corridor.Friendly.Add((7001, 1040f, 1400f));   // a friendly brigade walks onto the path
        AssertPromoted(entering, LodPromotion.Corridor, "a friendly unit entering the corridor");

        LodWorld beside = DistantNotDueWorld(w => w.Tasks.Target[100] = (1000f, tz, -1f, -1));
        beside.Corridor.Friendly.Add((7001, 1500f, 1400f));      // 500 m off the path: outside the 250 m corridor
        AssertStays(beside, "a friendly unit passing outside the corridor");

        LodWorld leaving = DistantNotDueWorld(w =>
        {
            w.Tasks.Target[100] = (1000f, tz, -1f, -1);
            w.Corridor.Friendly.Add((7001, 1040f, 1400f));
        });
        leaving.Corridor.Friendly.RemoveAll(f => f.id == 7001);
        AssertPromoted(leaving, LodPromotion.Corridor, "a friendly unit leaving the corridor");

        LodWorld swap = DistantNotDueWorld(w =>
        {
            w.Tasks.Target[100] = (1000f, tz, -1f, -1);
            w.Corridor.Friendly.Add((7001, 1040f, 1400f));
        });
        swap.Corridor.Friendly.RemoveAll(f => f.id == 7001);
        swap.Corridor.Friendly.Add((7002, 1040f, 1400f));        // same count, a different unit: the hash tells
        AssertPromoted(swap, LodPromotion.Corridor, "one unit replaced by another");

        // No target: the corridor runs to the layout slot.
        LodWorld slot = DistantNotDueWorld(w => w.Tasks.Slot[100] = (1000f, 1500f, 0f));
        slot.Corridor.Friendly.Add((7001, 1020f, 1250f));
        AssertPromoted(slot, LodPromotion.Corridor, "a friendly unit entering the corridor to the slot");
    }

    private static void PlanChangePromotesWholeSide()
    {
        LodWorld w = QuietWorld(6);
        for (int t = 0; t < 6; t++) w.Tick();
        while (w.Due(100)) w.Tick();
        int distant = 0;
        for (int b = 0; b < 6; b++) if (w.Tier(100 + b) == LodTier.Distant) distant++;
        AssertEqual(6, distant, "precondition: all six brigades are Distant");
        w.PlanSignature++;
        w.Tick();
        for (int b = 0; b < 6; b++)
        {
            AssertEqual(LodTier.Full, w.Tier(100 + b), "brigade " + b + " promoted by the plan change");
            AssertTrue(w.Due(100 + b), "and due");
            AssertTrue((w.Why(100 + b) & LodPromotion.Plan) != 0, "for the plan");
        }
        AssertEqual(6, w.State.PromotionCountOf(LodPromotion.Plan), "counted for every group of the side");
        // The plan signature is read once: the next tick it is the same signature and the groups settle again after the minimum hold.
        w.Tick();
        AssertFalse((w.Why(100) & LodPromotion.Plan) != 0, "a standing plan promotes nothing");
        for (int t = 0; t < 4; t++) w.Tick();
        AssertEqual(LodTier.Distant, w.Tier(100), "Distant again");
    }

    private static void PromotionReasonsAreReported()
    {
        LodWorld w = DistantNotDueWorld(null, 2);
        w.State.ClearPromotionCounts();
        w.Unit(1001).ReceivedFire = true;                 // brigade 100: fire
        w.Unit(101).RegimentPaths = 1;                    // brigade 101: moving
        w.Tick();
        AssertEqual(1, w.State.PromotionCountOf(LodPromotion.Fire), "one promotion for fire");
        AssertEqual(1, w.State.PromotionCountOf(LodPromotion.Moving), "one for moving");
        // Still Full next tick for the same reasons: a group that is already Full is not promoted again.
        w.Tick();
        AssertEqual(1, w.State.PromotionCountOf(LodPromotion.Fire), "a group that stays Full is counted once");
        AssertEqual(1, w.State.PromotionCountOf(LodPromotion.Moving), "moving too");
    }
}
