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
            ("stance renewal pin wired after the winners and through the writer", RenewalIsWiredAfterTheWinnersAndThroughTheWriter),
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
        AssertTrue(body.Contains("UnitFlags.PlayerOwned"), "never a player-owned group");
        AssertTrue(body.Contains("TacticalStanceOwnership.RenewsWhileOrdered(record, own[i].AiStanceOrdered, carrying)"), "the pure predicate decides");
        AssertTrue(Regex.IsMatch(body, @"EnsureStance\(bunits, group, record\.Stance, ""hold-renew"", record\.Source\)"), "through EnsureStance with the record's own stance and source");
        AssertFalse(body.Contains("ChangeStance("), "the renewal itself never writes a stance order (EnsureStance owns that)");
    }
}
