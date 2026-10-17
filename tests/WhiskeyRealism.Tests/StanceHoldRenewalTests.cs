using System;
using System.Text.RegularExpressions;
using WhiskeyRealism.Tactical;

using static TestHarness;

// The stance hold of a group that carries an order (2026-10-16 smoke, 1st_Brigade): the posture executor
// wrote stance 1 at doctrine hour 17.180 together with the brigade's move; the producers then stood still
// (the order-friction gate, the posture executor's pending-order stop) for as long as the move was pending
// and the brigade marched, nothing refreshed the 0.25 h hold, and at 17.437 vanilla's AdjustGroupAIStance
// (4264, inverted test) wrote its own stance 3 over it: a queued order that undid Whiskey's stance, and a
// second one when Whiskey wrote its stance back. A hold on a group that carries an order is renewed.
internal static class StanceHoldRenewalTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("stance renewal predicate needs a record that agrees with the ordered stance and an order carried", PredicateNeedsRecordAgreementAndAnOrder),
            ("stance renewal keeps vanilla from re-deciding a marching group", RenewalKeepsVanillaFromRedecidingAMarchingGroup),
            ("stance renewal control: without it the hold lapses at 0.25 h as in the smoke", WithoutRenewalTheHoldLapsesAtTheSmokeTime),
            ("stance renewal stops when the group settles and its producers are silent", RenewalStopsWhenTheGroupSettles),
            ("stance renewal does not outlive a vanilla weak-group yield", RenewalStillYieldsToVanillaWeakGroup),
            ("stance renewal candidates are AI groups that carry an order", CandidatesAreAiGroupsThatCarryAnOrder),
            ("stance renewal is bounded on the doctrine clock", RenewalIsBounded),
            ("stance renewal pin wired after the winners and through the writer", RenewalIsWiredAfterTheWinnersAndThroughTheWriter),
            ("stance renewal start is cleared at battle end and on a yield", RenewalStartIsClearedAtBattleEndAndOnAYield),
            ("stance renewal carries the works-hold flag of the record it renews", RenewalCarriesTheWorksHoldFlag),
        });
    }

    private const float Reset = 1f;        // GamePrefs.updatetimeforresettingofaistance, battleprefs.txt: 1 game-hour
    private const float Cycle = 0.015f;    // sidestatupdatecycle
    private const float Written = 17.18f;  // the smoke's TacticalStanceWrite
    private const int Scout = 1;           // the posture executor's stance for the Scout task

    private static StanceWriteInput Input(StanceHoldRecord record, float now, int ordered = Scout, int vanilla = 3)
    {
        return new StanceWriteInput(true, Scout, ordered, 0, now, record, StanceWriteSource.Executor, vanilla, now);
    }

    private static void PredicateNeedsRecordAgreementAndAnOrder()
    {
        var record = new StanceHoldRecord(Scout, Written, StanceWriteSource.Executor);
        AssertTrue(TacticalStanceOwnership.RenewsWhileOrdered(record, Scout, carryingOrder: true), "a held stance that is still ordered, order carried");
        AssertFalse(TacticalStanceOwnership.RenewsWhileOrdered(record, Scout, carryingOrder: false), "a settled group's producers claim for it");
        AssertFalse(TacticalStanceOwnership.RenewsWhileOrdered(record, 3, carryingOrder: true), "vanilla already changed it: the producers reassert, not a renewal");
        AssertFalse(TacticalStanceOwnership.RenewsWhileOrdered(default(StanceHoldRecord), Scout, carryingOrder: true), "no record, no hold to renew");
    }

    // One cycle of the writer as TacticalStanceWriter.EnsureStance runs it for a renewal (cheap path first,
    // else the full decision); returns the stamp after the cycle.
    private static float Renew(ref StanceHoldRecord record, float stamp, float now, int vanillaGroupStance = 3)
    {
        if (TacticalStanceOwnership.SteadyStateHolds(record, Scout, record.Source, Scout, now, stamp, now, Reset)) return stamp;
        StanceWriteInput input = Input(record, now, vanilla: vanillaGroupStance);
        StanceWriteDecision decision = TacticalStanceOwnership.Decide(input);
        if (TacticalStanceOwnership.StampsVanillaHold(decision, input)) stamp = TacticalStanceOwnership.VanillaHoldStamp(input, Reset);
        if (TacticalStanceOwnership.ReleasesVanillaHold(decision)) stamp = TacticalStanceOwnership.ReleaseStamp(now);
        record = TacticalStanceOwnership.Next(decision, input);
        return stamp;
    }

    // Vanilla 4264: the group is re-decided when the stamp is not ahead of now + T.
    private static bool VanillaRedecides(float stamp, float now) => stamp < now + Reset;

    private static void RenewalKeepsVanillaFromRedecidingAMarchingGroup()
    {
        var first = Input(default(StanceHoldRecord), Written, ordered: 3);
        StanceWriteDecision write = TacticalStanceOwnership.Decide(first);
        AssertEqual(StanceWriteAction.Write, write.Action, "the smoke's write");
        StanceHoldRecord record = TacticalStanceOwnership.Next(write, first);
        float stamp = TacticalStanceOwnership.VanillaHoldStamp(first, Reset);
        int redecided = 0;
        for (float now = Written + Cycle; now < Written + 1.0f; now += Cycle)
        {
            if (TacticalStanceOwnership.RenewsWhileOrdered(record, Scout, carryingOrder: true))
                stamp = Renew(ref record, stamp, now);
            if (VanillaRedecides(stamp, now)) redecided++;
        }
        AssertEqual(0, redecided, "vanilla's AdjustGroupAIStance never re-decides the group during an hour of march");
    }

    private static void WithoutRenewalTheHoldLapsesAtTheSmokeTime()
    {
        // Control: the same timeline with no renewal. Vanilla re-decides the first cycle the stamp is not
        // ahead, 0.25 h after the write: the smoke's stance-3 at doctrine hour 17.437 (17.180 + 0.257).
        var first = Input(default(StanceHoldRecord), Written, ordered: 3);
        StanceWriteDecision write = TacticalStanceOwnership.Decide(first);
        float stamp = TacticalStanceOwnership.VanillaHoldStamp(first, Reset);
        float lapse = -1f;
        for (float now = Written + Cycle; now < Written + 1.0f; now += Cycle)
        {
            if (VanillaRedecides(stamp, now)) { lapse = now - Written; break; }
        }
        AssertTrue(lapse > 0.25f - 1e-4f && lapse < 0.25f + Cycle + 1e-4f, "the hold lapses " + lapse.ToString("0.000") + " h after the write (the smoke: 0.257)");
        AssertEqual(StanceWriteAction.Write, write.Action, "(the write that stamped it)");
    }

    private static void RenewalStopsWhenTheGroupSettles()
    {
        // Once the group no longer carries an order the predicate is false: the producers claim for it again,
        // and when they do not, the hold lapses as before and vanilla resumes (design: "once Whiskey stops
        // claiming, the hold lapses").
        var record = new StanceHoldRecord(Scout, Written, StanceWriteSource.Executor);
        AssertFalse(TacticalStanceOwnership.RenewsWhileOrdered(record, Scout, carryingOrder: false), "settled: no renewal");
    }

    private static void RenewalStillYieldsToVanillaWeakGroup()
    {
        // The carve-out is the writer's own decision: a renewal where vanilla's per-group stance is defend (2)
        // and Whiskey wants screen (1) yields instead of claiming, and the stamp is released.
        var record = new StanceHoldRecord(Scout, Written, StanceWriteSource.Executor);
        float stamp = TacticalStanceOwnership.VanillaHoldStamp(Written, TacticalStanceOwnership.HoldHours, Reset);
        float now = Written + 0.2f;
        StanceWriteDecision decision = TacticalStanceOwnership.Decide(Input(record, now, vanilla: 2));
        AssertEqual(StanceWriteAction.Yield, decision.Action, "vanilla says defend: Whiskey yields");
        float after = Renew(ref record, stamp, now, vanillaGroupStance: 2);
        AssertTrue(VanillaRedecides(after, now + Cycle), "the stamp is released so vanilla's weak-group stance fires");
        AssertFalse(record.HasValue, "and the record is dropped");
    }

    // Both arms of every test RenewWhileOrdered makes before it renews (the verify round's surviving plants).
    private static void CandidatesAreAiGroupsThatCarryAnOrder()
    {
        AssertTrue(TacticalStanceOwnership.IsRenewalCandidate(true, false, 1, 0), "a group with a path");
        AssertTrue(TacticalStanceOwnership.IsRenewalCandidate(true, false, 0, 1), "a group with an order in the queue");
        AssertTrue(TacticalStanceOwnership.IsRenewalCandidate(true, false, 3, 2), "both");
        AssertFalse(TacticalStanceOwnership.IsRenewalCandidate(true, false, 0, 0), "a settled group is its producers'");
        AssertFalse(TacticalStanceOwnership.IsRenewalCandidate(true, true, 1, 1), "never a player-owned group, however it marches");
        AssertFalse(TacticalStanceOwnership.IsRenewalCandidate(false, false, 1, 1), "a regiment has no group stance");
        AssertTrue(TacticalStanceOwnership.CarriesOrder(1, 0) && TacticalStanceOwnership.CarriesOrder(0, 1), "a path or a queued order");
        AssertFalse(TacticalStanceOwnership.CarriesOrder(0, 0), "neither");
        AssertFalse(TacticalStanceOwnership.CarriesOrder(-1, 0), "a negative path count is no path");
    }

    // A march that never settles (nothing assigns a recovery task; the interrupted-path recovery acts on an
    // idle group only) cannot keep a stance for ever: renewals alone end after RenewalBoundHours.
    private static void RenewalIsBounded()
    {
        AssertTrue(TacticalStanceOwnership.RenewalLive(10f, 10f), "the first renewal");
        AssertTrue(TacticalStanceOwnership.RenewalLive(10f, 10f + TacticalStanceOwnership.RenewalBoundHours - 0.01f), "just inside the bound");
        AssertFalse(TacticalStanceOwnership.RenewalLive(10f, 10f + TacticalStanceOwnership.RenewalBoundHours), "at the bound");
        AssertFalse(TacticalStanceOwnership.RenewalLive(10f, 9.9f), "a clock that went backwards ends them");
        AssertTrue(TacticalStanceOwnership.RenewalBoundHours > 3f * 0.5f - 1e-4f,
            "the bound is at least three times the smoke's march (1000 m, about 0.5 h)");
        AssertTrue(TacticalStanceOwnership.RenewalBoundHours <= 3f, "and short enough that a stuck march does not hold a stance for hours");
        // Past the bound the hold lapses 0.25 h later and vanilla re-decides the group (the control above).
        var first = Input(default(StanceHoldRecord), Written, ordered: 3);
        StanceWriteDecision write = TacticalStanceOwnership.Decide(first);
        StanceHoldRecord record = TacticalStanceOwnership.Next(write, first);
        float stamp = TacticalStanceOwnership.VanillaHoldStamp(first, Reset);
        float lapse = -1f;
        for (float now = Written + Cycle; now < Written + 4.0f; now += Cycle)
        {
            if (TacticalStanceOwnership.RenewalLive(Written + Cycle, now) &&
                TacticalStanceOwnership.RenewsWhileOrdered(record, Scout, carryingOrder: true))
                stamp = Renew(ref record, stamp, now);
            if (VanillaRedecides(stamp, now)) { lapse = now - Written; break; }
        }
        AssertTrue(lapse > TacticalStanceOwnership.RenewalBoundHours && lapse < TacticalStanceOwnership.RenewalBoundHours + 0.5f,
            "vanilla resumes " + lapse.ToString("0.00") + " h after the write: the bound plus one hold");
    }

    private static void RenewalIsWiredAfterTheWinnersAndThroughTheWriter()
    {
        string tick = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Tactical/Battle/BattleCoreTick.cs");
        int renew = tick.IndexOf("TacticalStanceWriter.RenewWhileOrdered(bunits, snapshot);", StringComparison.Ordinal);
        int end = tick.IndexOf("executor.EndTickClaims();", StringComparison.Ordinal);
        int emit = tick.IndexOf("stats.EmitIfDue(alliance);", StringComparison.Ordinal);
        AssertTrue(end >= 0 && renew > end && emit > renew, "the arbiter tick renews after the winners were issued, so a producer's own claim comes first");
        string writer = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Util/TacticalStanceWriter.cs");
        int at = writer.IndexOf("public static int RenewWhileOrdered(", StringComparison.Ordinal);
        AssertTrue(at >= 0, "the renewal exists");
        string body = writer.Substring(at, writer.IndexOf("// Hand one group back to vanilla", at, StringComparison.Ordinal) - at);
        AssertTrue(Regex.IsMatch(body, @"IsRenewalCandidate\(\(own\[i\]\.Flags & UnitFlags\.IsGroup\) != 0,\s*\(own\[i\]\.Flags & UnitFlags\.PlayerOwned\) != 0, own\[i\]\.RegimentPaths, own\[i\]\.QueuedOrders\)\) continue;"),
            "the pure candidate test decides (group, not player-owned, path or queued order), from the snapshot record");
        AssertTrue(body.Contains("TacticalStanceOwnership.RenewsWhileOrdered(record, own[i].AiStanceOrdered, carryingOrder: true)"), "the pure predicate decides");
        AssertTrue(Regex.IsMatch(body, @"if \(!_renewSince\.TryGetValue\(id, out float since\)\) _renewSince\[id\] = since = now;\s*if \(!TacticalStanceOwnership\.RenewalLive\(since, now\)\) continue;"), "bounded, from the first renewal after a producer's claim");
        AssertTrue(Regex.IsMatch(body, @"EnsureStance\(bunits, group, record\.Stance, RenewTag, record\.Source, record\.WorksHold\)"), "through EnsureStance with the record's own stance, source and works-hold flag");
        string ensure = writer.Substring(writer.IndexOf("public static bool EnsureStance(", StringComparison.Ordinal));
        AssertTrue(ensure.Contains("if (!ReferenceEquals(contextTag, RenewTag)) _renewSince.Remove(id);"), "a producer's claim restarts the bound; a renewal does not");
        AssertFalse(body.Contains("ChangeStance("), "the renewal itself never writes a stance order (EnsureStance owns that)");
    }

    // R3 of verify round 2: the renewal's start is state a battle must not leak into the next (a stale start would
    // read as an expired bound at once and the first renewal of the next battle would be refused) and a group handed
    // back to vanilla must not keep its start (a later claim would otherwise inherit an old bound).
    private static void RenewalStartIsClearedAtBattleEndAndOnAYield()
    {
        string writer = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Util/TacticalStanceWriter.cs");
        int reset = writer.IndexOf("public static void ResetForBattleEnd()", StringComparison.Ordinal);
        AssertTrue(reset >= 0, "the battle-end reset exists");
        string resetBody = writer.Substring(reset, writer.IndexOf('}', reset) - reset);
        AssertTrue(resetBody.Contains("_renewSince.Clear();"), "battle end clears every renewal start");
        AssertTrue(writer.Contains("BattleScope.Register(\"stance-writer\", ResetForBattleEnd);"), "and the reset is registered with the battle scope");
        int yield = writer.IndexOf("private static void Yield(", StringComparison.Ordinal);
        AssertTrue(yield >= 0, "the yield exists");
        string yieldBody = writer.Substring(yield, writer.IndexOf("_records.Remove(id)", yield, StringComparison.Ordinal) - yield);
        AssertTrue(yieldBody.Contains("_renewSince.Remove(id);"), "a yield drops the group's renewal start before anything can return early");
    }

    // BC14W verify round 2: a renewal is the group's own record through the full decision, so the record has to
    // carry the works-hold flag the producer wrote it with. A renewal that wrote the flag-less record back would
    // turn a works hold into an ordinary defend hold until the producer next resubmitted it, and the producers go
    // quiet while the group marches.
    private static void RenewalCarriesTheWorksHoldFlag()
    {
        const int Defend = 2;
        var hold = new StanceWriteInput(true, Defend, 3, 0, Written, default(StanceHoldRecord), StanceWriteSource.Executor, 3, Written, worksHold: true);
        StanceWriteDecision write = TacticalStanceOwnership.Decide(hold);
        AssertEqual(StanceWriteAction.Write, write.Action, "the works hold's first write");
        StanceHoldRecord record = TacticalStanceOwnership.Next(write, hold);
        AssertTrue(record.WorksHold, "the record a works-hold write stamps carries the flag");
        AssertFalse(TacticalStanceOwnership.Next(TacticalStanceOwnership.Decide(Input(default(StanceHoldRecord), Written, ordered: 3)),
            Input(default(StanceHoldRecord), Written, ordered: 3)).WorksHold, "an ordinary write stamps no flag");

        // The renewal, as EnsureStance builds it from the record: same flag, a later hour, the stance now ordered.
        float now = Written + 0.12f;
        var renew = new StanceWriteInput(true, record.Stance, Defend, 0, now, record, record.Source, 3, now, worksHold: record.WorksHold);
        StanceWriteDecision claim = TacticalStanceOwnership.Decide(renew);
        AssertEqual(StanceWriteAction.Claim, claim.Action, "the renewal claims the hold it already has");
        StanceHoldRecord renewed = TacticalStanceOwnership.Next(claim, renew);
        AssertTrue(renewed.WorksHold, "the renewed record still carries the works-hold flag");

        // Control: the flag-less renewal the writer used to make claims the same hold and stamps a plain record.
        var plain = new StanceWriteInput(true, record.Stance, Defend, 0, now, record, record.Source, 3, now);
        StanceWriteDecision plainClaim = TacticalStanceOwnership.Decide(plain);
        AssertEqual(StanceWriteAction.Claim, plainClaim.Action, "control: the same decision without the flag");
        AssertFalse(TacticalStanceOwnership.Next(plainClaim, plain).WorksHold, "control: a renewal that does not pass the flag drops it");
    }
}
