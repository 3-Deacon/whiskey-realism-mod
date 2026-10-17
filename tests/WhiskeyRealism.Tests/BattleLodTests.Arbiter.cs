using WhiskeyRealism.Tactical;
using System;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using static TestHarness;
using static TestFixtures;

// BC15 decision 4: carried claims. A Distant group that is not due submits nothing this tick; the claims it published last tick are carried into
// this tick's table (published, so D4 refusals, the tie hysteresis and the retry test see them) and never handed to the executor again.
internal static partial class BattleLodTests
{
    private static (string, Action)[] ArbiterTests()
    {
        return new (string, Action)[]
        {
            ("bc15 ARBITER CarryForwardRunsAfterBeginTickBeforeProducers", CarryForwardRunsAfterBeginTick),
            ("bc15 ARBITER CarriedClaimRefusesInlineIntentLikeSubmitted (D4)", CarriedClaimRefusesInlineIntent),
            ("bc15 ARBITER CarriedClaimKeepsTieHysteresis: a tie keeps the carried source", CarriedClaimKeepsTieHysteresis),
            ("bc15 ARBITER a fresh intent of the carried source replaces the carried cell", FreshIntentReplacesCarriedOfSameSource),
            ("bc15 ARBITER a higher priority beats a carried claim, a lower one loses", PriorityStillDecidesAgainstACarriedClaim),
            ("bc15 ARBITER RetryStillClaimedAcrossSkippedTick", RetryStillClaimedAcrossSkippedTick),
            ("bc15 ARBITER CarriedClaimNeverExecuted: carried cells are published, not written to the winners", CarriedClaimNeverExecuted),
            ("bc15 ARBITER CarriedClaimLivesUntilDueOrPromoted", CarriedClaimLivesUntilDueOrPromoted),
            ("bc15 ARBITER only the tiered producers' sources are carried", OnlyTieredSourcesAreCarried),
            ("bc15 ARBITER DropCarried removes a group's carried cells and only those", DropCarriedRemovesOnlyCarried),
            ("bc15 ARBITER CarriedMoveClaimStillShieldsAChainFanOut: the executor sees carried Move claims", CarriedMoveClaimStillShieldsAFanOut),
            ("bc15 ARBITER StandDownResetsTiersToFull", StandDownResetsTiersToFull),
            ("bc15 ARBITER NewBattleDayResetsTiersToFull", NewBattleDayResetsTiersToFull),
            ("bc15 ARBITER carrying and resolving allocate nothing after warm-up", CarryAllocatesNothingAfterWarmup),
            ("bc15 ARBITER the stance hold is renewed on a Distant group's due tick before it lapses", DistantIntervalKeepsTheStanceHoldAlive),
        };
    }

    private static BattleOrderIntent Claim(int group, OrderChannel channel, IntentPriority priority, IntentSource source, float x = 100f, float z = 100f)
    {
        return new BattleOrderIntent
        {
            GroupId = group, Channel = channel, TargetX = x, TargetZ = z, Heading = -1f, Priority = priority, Source = source,
            Task = CommandTaskType.HoldObjective, Reason = "test", Value = channel == OrderChannel.Stance ? 2 : 0,
        };
    }

    // Publishes the intents as a tick's winners and returns the arbiter ready for the next tick.
    private static OrderArbiter PublishedArbiter(params BattleOrderIntent[] tick)
    {
        var arbiter = new OrderArbiter();
        arbiter.BeginTick();
        foreach (BattleOrderIntent i in tick) arbiter.Submit(i);
        arbiter.Resolve(new BattleOrderIntent[32]);
        return arbiter;
    }

    private static void CarryForwardRunsAfterBeginTick()
    {
        OrderArbiter arbiter = PublishedArbiter(Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.BeginTick();
        AssertEqual(1, arbiter.CarryForward(7), "one cell carried");
        arbiter.Resolve(new BattleOrderIntent[32]);
        AssertTrue(arbiter.TryGetClaim(7, OrderChannel.Move, out _), "published after a carry that followed BeginTick");

        // The wrong order (the plan's mutation): BeginTick clears the working table, so a carry made before it is lost and the claim ends.
        OrderArbiter wrong = PublishedArbiter(Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor));
        wrong.CarryForward(7);
        wrong.BeginTick();
        wrong.Resolve(new BattleOrderIntent[32]);
        AssertFalse(wrong.TryGetClaim(7, OrderChannel.Move, out _), "a carry made before BeginTick is dropped");
    }

    private static void CarriedClaimRefusesInlineIntent()
    {
        OrderArbiter arbiter = PublishedArbiter(Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.BeginTick();
        arbiter.CarryForward(7);
        arbiter.Resolve(new BattleOrderIntent[32]);
        var inline = Claim(7, OrderChannel.Move, IntentPriority.Scorer, IntentSource.FeudReplay);
        AssertEqual(SkipReason.LowerPriorityThanClaim, OrderExecutor.InlineClaimCheck(arbiter, in inline), "a lower inline intent is refused by the carried claim");
        var higher = Claim(7, OrderChannel.Move, IntentPriority.Safety, IntentSource.FeudReplay);
        AssertEqual(SkipReason.None, OrderExecutor.InlineClaimCheck(arbiter, in higher), "a higher one passes, as it would a submitted claim");
    }

    private static void CarriedClaimKeepsTieHysteresis()
    {
        OrderArbiter arbiter = PublishedArbiter(Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.BeginTick();
        arbiter.CarryForward(7);
        arbiter.Submit(Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.ReserveRelease, 500f, 500f));   // a tie, other source
        var winners = new BattleOrderIntent[8];
        int n = arbiter.Resolve(winners);
        AssertEqual(0, n, "the carried cell won the tie, and carried cells are not winners");
        AssertTrue(arbiter.TryGetClaim(7, OrderChannel.Move, out BattleOrderIntent claim), "a claim is published");
        AssertEqual(IntentSource.PostureExecutor, claim.Source, "the tie kept last tick's source, as the hysteresis would have");
    }

    private static void FreshIntentReplacesCarriedOfSameSource()
    {
        OrderArbiter arbiter = PublishedArbiter(Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor, 100f, 100f));
        arbiter.BeginTick();
        arbiter.CarryForward(7);
        arbiter.Submit(Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor, 900f, 900f));   // a late wake-up resubmits
        var winners = new BattleOrderIntent[8];
        int n = arbiter.Resolve(winners);
        AssertEqual(1, n, "the fresh intent is a winner");
        AssertNear(900f, winners[0].TargetX, 0.001f, "with its own target");
        arbiter.TryGetClaim(7, OrderChannel.Move, out BattleOrderIntent claim);
        AssertNear(900f, claim.TargetX, 0.001f, "and it is the published claim");
    }

    private static void PriorityStillDecidesAgainstACarriedClaim()
    {
        OrderArbiter arbiter = PublishedArbiter(Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.BeginTick();
        arbiter.CarryForward(7);
        arbiter.Submit(Claim(7, OrderChannel.Move, IntentPriority.Safety, IntentSource.Withdrawal, 50f, 50f));
        var winners = new BattleOrderIntent[8];
        AssertEqual(1, arbiter.Resolve(winners), "a Safety intent outranks the carried Executor claim");
        AssertEqual(IntentSource.Withdrawal, winners[0].Source, "and wins");

        OrderArbiter held = PublishedArbiter(Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor));
        held.BeginTick();
        held.CarryForward(7);
        held.Submit(Claim(7, OrderChannel.Move, IntentPriority.Play, IntentSource.DivisionPlay));
        AssertEqual(0, held.Resolve(winners), "a lower intent loses to the carried claim");
        held.TryGetClaim(7, OrderChannel.Move, out BattleOrderIntent kept);
        AssertEqual(IntentSource.PostureExecutor, kept.Source, "and the carried claim stands");
    }

    private static void RetryStillClaimedAcrossSkippedTick()
    {
        OrderArbiter arbiter = PublishedArbiter(Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor));
        var retry = Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor, 130f, 130f);
        arbiter.BeginTick();
        arbiter.CarryForward(7);
        arbiter.Resolve(new BattleOrderIntent[8]);
        AssertTrue(OrderExecutor.RetryStillClaimed(arbiter, in retry), "a retry of the source that holds the carried claim still runs");
        var other = Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.ReserveRelease);
        AssertFalse(OrderExecutor.RetryStillClaimed(arbiter, in other), "a retry of another source is superseded");
    }

    private static void CarriedClaimNeverExecuted()
    {
        OrderArbiter arbiter = PublishedArbiter(
            Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor),
            Claim(7, OrderChannel.Stance, IntentPriority.Executor, IntentSource.PostureExecutor),
            Claim(8, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.BeginTick();
        AssertEqual(2, arbiter.CarryForward(7), "group 7 carries its two cells");
        arbiter.Submit(Claim(8, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor, 300f, 300f));   // group 8 is due and resubmits
        var winners = new BattleOrderIntent[8];
        int n = arbiter.Resolve(winners);
        AssertEqual(1, n, "only the due group's intent is a winner");
        AssertEqual(8, winners[0].GroupId, "group 8's");
        var carried = new BattleOrderIntent[8];
        AssertEqual(2, arbiter.CarriedWinners(carried), "the two carried cells are reported apart");
        AssertEqual(7, carried[0].GroupId, "carried cell of group 7");
        AssertTrue(arbiter.TryGetClaim(7, OrderChannel.Stance, out _), "and published");
    }

    private static void CarriedClaimLivesUntilDueOrPromoted()
    {
        OrderArbiter arbiter = PublishedArbiter(Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor));
        var winners = new BattleOrderIntent[8];
        // Three ticks the group is Distant and not due: carried, no row of its own, the claim stands every tick (spacing never matters: no carry count).
        for (int tick = 0; tick < 3; tick++)
        {
            arbiter.BeginTick();
            arbiter.CarryForward(7);
            arbiter.Resolve(winners);
            AssertTrue(arbiter.TryGetClaim(7, OrderChannel.Move, out _), "claim on carried tick " + tick);
        }
        // The group is due: it is not carried and its producers resubmit.
        arbiter.BeginTick();
        arbiter.Submit(Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor, 200f, 200f));
        AssertEqual(1, arbiter.Resolve(winners), "due: resubmitted and executed");
        // Due again, but the producers now say nothing: the claim ends (the group is no longer held to a move).
        arbiter.BeginTick();
        arbiter.Resolve(winners);
        AssertFalse(arbiter.TryGetClaim(7, OrderChannel.Move, out _), "a due group that submits nothing has no claim");
        // Promoted: the group is simply not carried that tick.
        arbiter.BeginTick();
        arbiter.Submit(Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.Resolve(winners);
        arbiter.BeginTick();
        arbiter.Resolve(winners);
        AssertFalse(arbiter.TryGetClaim(7, OrderChannel.Move, out _), "a promoted group's old claim is not carried");
    }

    private static void OnlyTieredSourcesAreCarried()
    {
        var tiered = new[]
        {
            IntentSource.PostureExecutor, IntentSource.FacingPulse, IntentSource.FireControl, IntentSource.DivisionPlay,
            IntentSource.GroupStanceScorer, IntentSource.ArtilleryDoctrine,
        };
        foreach (IntentSource source in Enum.GetValues(typeof(IntentSource)))
        {
            bool expected = Array.IndexOf(tiered, source) >= 0;
            AssertEqual(expected, BattleLod.IsCarriedSource(source), "IsCarriedSource(" + source + ")");
            if (source == IntentSource.FeudReplay) continue;   // never reaches the arbiter
            OrderArbiter arbiter = PublishedArbiter(Claim(7, OrderChannel.Move, IntentPriority.Executor, source));
            arbiter.BeginTick();
            AssertEqual(expected ? 1 : 0, arbiter.CarryForward(7), "carried cells of source " + source);
        }
    }

    private static void DropCarriedRemovesOnlyCarried()
    {
        OrderArbiter arbiter = PublishedArbiter(
            Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor),
            Claim(7, OrderChannel.Stance, IntentPriority.Executor, IntentSource.PostureExecutor),
            Claim(8, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.BeginTick();
        arbiter.CarryForward(7);
        arbiter.CarryForward(8);
        AssertEqual(2, arbiter.DropCarried(7), "group 7's two carried cells are dropped");
        AssertEqual(0, arbiter.DropCarried(7), "and nothing is left to drop");
        AssertEqual(0, arbiter.DropCarried(99), "an unknown group has none");
        arbiter.Submit(Claim(7, OrderChannel.Facing, IntentPriority.Scorer, IntentSource.FacingPulse));   // the woken group's producers submit afresh
        var winners = new BattleOrderIntent[8];
        AssertEqual(1, arbiter.Resolve(winners), "the fresh intent resolves");
        AssertFalse(arbiter.TryGetClaim(7, OrderChannel.Move, out _), "the carried Move is gone");
        AssertFalse(arbiter.TryGetClaim(7, OrderChannel.Stance, out _), "and the carried Stance");
        AssertTrue(arbiter.TryGetClaim(7, OrderChannel.Facing, out _), "the fresh Facing stands");
        AssertTrue(arbiter.TryGetClaim(8, OrderChannel.Move, out _), "another group's carried cell is untouched");
    }

    private static void CarriedMoveClaimStillShieldsAFanOut()
    {
        // division 50 -> brigade 100 -> two regiments; the brigade holds a carried Executor Move claim, the division moves at Play priority.
        var w = new LodWorld();
        w.Division(50, 0, 1000f, 1000f);
        w.Brigade(100, 50, 1000f, 1000f);
        w.Regiment(1001, 100, 970f, 1000f);
        w.Regiment(1002, 100, 1030f, 1000f);
        w.Brigade(101, 50, 1200f, 1000f);
        w.Regiment(1011, 101, 1170f, 1000f);
        w.Build();
        var executor = new OrderExecutor();
        executor.Bind(new OpenGuard(), null);
        executor.SetMapSize(5000f, 5000f);
        var divisionMove = Claim(50, OrderChannel.Move, IntentPriority.Play, IntentSource.DivisionPlay, 1000f, 2000f);
        divisionMove.Task = CommandTaskType.AttackObjective;
        var carried = new[] { Claim(100, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor, 1000f, 1050f) };

        executor.BeginTickClaims(w.S, new[] { divisionMove }, 1);
        ExecutionPlan unshielded = executor.Plan(w.S, divisionMove, 1.0);
        AssertTrue(unshielded.Execute, "the division moves");
        AssertFalse(unshielded.Targets.FannedOut, "with no claim below it, the division orders itself");

        executor.BeginTickClaims(w.S, new[] { divisionMove }, 1, carried, 1);
        ExecutionPlan shielded = executor.Plan(w.S, divisionMove, 1.0);
        AssertTrue(shielded.Execute, "the division still moves");
        AssertTrue(shielded.Targets.FannedOut, "but fans out around the brigade whose carried claim outranks it");
        bool reachesBrigade = false;
        for (int t = 0; t < shielded.Targets.Count; t++)
            if (shielded.Targets.TargetIndices[t] == w.S.IndexOfOwn(100)) reachesBrigade = true;
        AssertFalse(reachesBrigade, "the carried brigade is not re-pathed");
        executor.EndTickClaims();
    }

    private sealed class OpenGuard : IOrderGuard
    {
        public bool CanWrite(in UnitRecord unit, out SkipReason reason)
        {
            reason = SkipReason.None;
            return true;
        }
    }

    private static void StandDownResetsTiersToFull()
    {
        LodWorld w = QuietWorld(3);
        w.TickUntilDistant(100);
        w.Tick();
        AssertEqual(LodTier.Distant, w.Tier(100), "precondition: Distant");
        // BattleCoreTick.StandDown: the claims are cleared and the side's tiers reset.
        w.State.ResetAllFull(w.Hours);
        w.Tick();
        for (int b = 0; b < 3; b++) AssertEqual(LodTier.Full, w.Tier(100 + b), "brigade " + b + " is Full after a stand-down");
        AssertEqual(LodTier.Full, w.Tier(100), "and none of them is due-skipped");
        w.Tick();
        AssertEqual(LodTier.Distant, w.Tier(100), "Distant again two cycles after the reset, with its signature re-captured");
        OrderArbiter arbiter = PublishedArbiter(Claim(7, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.ClearClaims();
        arbiter.BeginTick();
        AssertEqual(0, arbiter.CarryForward(7), "a stand-down cleared the claims: nothing to carry");
    }

    private static void NewBattleDayResetsTiersToFull()
    {
        LodWorld w = QuietWorld(2);
        w.TickUntilDistant(100);
        w.State.ResetAllFull(w.Hours);
        AssertFalse(w.State.Covers(w.S), "a reset state covers no snapshot until the next pass: every unit is due");
        AssertTrue(w.State.IsDue(w.S.IndexOfOwn(100)), "due before the next pass");
        // The plan signature starts over: the first signature after a reset is the baseline, not a change, whatever it is.
        w.PlanSignature += 5;
        w.Tick();
        AssertEqual(LodTier.Full, w.Tier(100), "Full on the next pass");
        AssertFalse((w.Why(100) & LodPromotion.Plan) != 0, "the signature seen after the reset is the baseline, not a plan change");
    }

    private static void CarryAllocatesNothingAfterWarmup()
    {
        var intents = new BattleOrderIntent[30];
        for (int g = 0; g < 10; g++)
        {
            intents[g * 3] = Claim(100 + g, OrderChannel.Move, IntentPriority.Executor, IntentSource.PostureExecutor);
            intents[g * 3 + 1] = Claim(100 + g, OrderChannel.Stance, IntentPriority.Executor, IntentSource.PostureExecutor);
            intents[g * 3 + 2] = Claim(100 + g, OrderChannel.Formation, IntentPriority.Executor, IntentSource.PostureExecutor);
        }
        OrderArbiter arbiter = PublishedArbiter(intents);
        var winners = new BattleOrderIntent[64];
        var carried = new BattleOrderIntent[64];
        for (int warm = 0; warm < 3; warm++)
        {
            arbiter.BeginTick();
            for (int g = 0; g < 10; g++) arbiter.CarryForward(100 + g);
            arbiter.Resolve(winners);
            arbiter.CarriedWinners(carried);
        }
        long before = BeginAllocationWindow();
        for (int tick = 0; tick < 50; tick++)
        {
            arbiter.BeginTick();
            for (int g = 0; g < 10; g++) arbiter.CarryForward(100 + g);
            arbiter.DropCarried(105);
            arbiter.Resolve(winners);
            arbiter.CarriedWinners(carried);
        }
        AssertEqual(0L, AllocatedSince(before), "bytes allocated by 50 carry-and-resolve ticks of ten groups");
    }

    // The arithmetic that keeps decision 5 honest for the one channel that is never a Duplicate: a stance intent is a hold refresh that the stance
    // writer acts on only in a full pass (record age at least HoldHours / 2, TacticalStanceOwnership.SteadyStateHolds), and vanilla takes the group
    // back HoldHours after the last stamp. A Distant group's producers run every DistantIntervalHours, so the stamp is renewed at the first due
    // tick whose record age reached half a hold: the worst gap is that many whole intervals, plus a tick of spacing jitter. It holds for every
    // side-stat cycle up to 0.03 h (the shipped 0.015 leaves 0.077 h); a battlefield pref above 0.03 loses the guarantee (the As built says so).
    private static void DistantIntervalKeepsTheStanceHoldAlive()
    {
        double hold = TacticalStanceOwnership.HoldHours;
        for (double cycle = 0.005; cycle <= 0.0301; cycle += 0.005)
        {
            double interval = BattleLod.DistantIntervalHours(cycle);
            double gap = Math.Ceiling(hold * 0.5 / interval) * interval + cycle;
            AssertTrue(gap < hold, "cycle " + cycle + ": worst gap between stance refreshes " + gap + " h stays inside the " + hold + " h hold");
        }
        double shipped = Math.Ceiling(hold * 0.5 / BattleLod.DistantIntervalHours(0.015)) * BattleLod.DistantIntervalHours(0.015) + 0.015;
        AssertTrue(hold - shipped > 0.07, "the shipped cycle keeps more than 0.07 h of margin (" + (hold - shipped) + ")");
    }
}
