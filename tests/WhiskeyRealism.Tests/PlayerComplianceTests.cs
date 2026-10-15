using System;
using WhiskeyRealism.Strategic;

using static TestHarness;

internal static class PlayerComplianceTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("player compliance starts at the prior with no samples", StartsAtPrior),
            ("player compliance complied order raises reliability", CompliedRaises),
            ("player compliance campaign order missed after the grace window", CampaignMissedAfterGrace),
            ("player compliance campaign order pending inside the grace window", CampaignPendingInsideGrace),
            ("player compliance battle grace window is half an hour", BattleGraceIsHalfHour),
            ("player compliance superseded order is neutral", SupersededIsNeutral),
            ("player compliance cleared order is neutral", ClearedIsNeutral),
            ("player compliance reach of another session is ignored", ReachOfOtherSessionIgnored),
            ("player compliance scopes are independent", ScopesIndependent),
            ("player compliance clock regress drops the pending order", ClockRegressDrops),
            ("player compliance repeated misses decay toward zero", RepeatedMissesDecay),
            ("player compliance restore only into an empty ledger", RestoreOnlyWhenEmpty),
            ("player compliance restore clamps and rejects non-finite", RestoreClampsAndRejects),
            ("player compliance strength weight discounts only player-owned units", StrengthWeight),
            ("player compliance sidecar round trip keeps the career estimate", SidecarRoundTrip),
            ("player compliance sidecar without the field decodes to none", SidecarWithoutField),
        });
    }

    private static void StartsAtPrior()
    {
        var ledger = new PlayerComplianceLedger();
        AssertNear(0.5f, ledger.Reliability, 1e-6f, "prior");
        AssertEqual(0, ledger.Samples);
        AssertFalse(ledger.HasPending(ComplianceScope.Campaign), "no pending");
    }

    private static void CompliedRaises()
    {
        var ledger = new PlayerComplianceLedger();
        AssertEqual(ComplianceOutcome.None, ledger.OnDispatch(ComplianceScope.Campaign, 4, 100.0));
        AssertEqual(ComplianceOutcome.Complied, ledger.OnReached(ComplianceScope.Campaign, 4));
        AssertNear(0.625f, ledger.Reliability, 1e-6f, "0.5 + 0.25 * 0.5");
        AssertEqual(1, ledger.Samples);
        AssertFalse(ledger.HasPending(ComplianceScope.Campaign), "resolved");
        AssertEqual(ComplianceOutcome.None, ledger.OnReached(ComplianceScope.Campaign, 4));
        AssertEqual(1, ledger.Samples);
    }

    private static void CampaignMissedAfterGrace()
    {
        var ledger = new PlayerComplianceLedger();
        ledger.OnDispatch(ComplianceScope.Campaign, 1, 1000.0);
        AssertEqual(ComplianceOutcome.Missed, ledger.Tick(ComplianceScope.Campaign, 1000.0 + PlayerComplianceLedger.CampaignGraceHours + 0.01));
        AssertNear(0.375f, ledger.Reliability, 1e-6f, "0.5 - 0.25 * 0.5");
        AssertEqual(1, ledger.Samples);
    }

    private static void CampaignPendingInsideGrace()
    {
        var ledger = new PlayerComplianceLedger();
        ledger.OnDispatch(ComplianceScope.Campaign, 1, 1000.0);
        AssertEqual(ComplianceOutcome.None, ledger.Tick(ComplianceScope.Campaign, 1071.0));
        AssertTrue(ledger.HasPending(ComplianceScope.Campaign), "still pending");
        AssertEqual(0, ledger.Samples);
    }

    private static void BattleGraceIsHalfHour()
    {
        var ledger = new PlayerComplianceLedger();
        ledger.OnDispatch(ComplianceScope.Battle, 9, 10.0);
        AssertEqual(ComplianceOutcome.None, ledger.Tick(ComplianceScope.Battle, 10.49));
        AssertEqual(ComplianceOutcome.Missed, ledger.Tick(ComplianceScope.Battle, 10.51));
    }

    private static void SupersededIsNeutral()
    {
        var ledger = new PlayerComplianceLedger();
        ledger.OnDispatch(ComplianceScope.Battle, 1, 10.0);
        AssertEqual(ComplianceOutcome.Neutral, ledger.OnDispatch(ComplianceScope.Battle, 2, 10.2));
        AssertEqual(0, ledger.Samples);
        AssertEqual(2, ledger.PendingSession(ComplianceScope.Battle));
    }

    private static void ClearedIsNeutral()
    {
        var ledger = new PlayerComplianceLedger();
        ledger.OnDispatch(ComplianceScope.Campaign, 3, 10.0);
        AssertEqual(ComplianceOutcome.Neutral, ledger.DropPending(ComplianceScope.Campaign));
        AssertEqual(0, ledger.Samples);
        AssertEqual(ComplianceOutcome.None, ledger.Tick(ComplianceScope.Campaign, 1000.0));
    }

    private static void ReachOfOtherSessionIgnored()
    {
        var ledger = new PlayerComplianceLedger();
        ledger.OnDispatch(ComplianceScope.Battle, 5, 1.0);
        AssertEqual(ComplianceOutcome.None, ledger.OnReached(ComplianceScope.Battle, 4));
        AssertTrue(ledger.HasPending(ComplianceScope.Battle), "pending kept");
    }

    private static void ScopesIndependent()
    {
        var ledger = new PlayerComplianceLedger();
        ledger.OnDispatch(ComplianceScope.Campaign, 1, 500.0);
        ledger.OnDispatch(ComplianceScope.Battle, 2, 1.0);
        AssertEqual(ComplianceOutcome.Complied, ledger.OnReached(ComplianceScope.Battle, 2));
        AssertTrue(ledger.HasPending(ComplianceScope.Campaign), "campaign pending survives a battle outcome");
        AssertEqual(1, ledger.PendingSession(ComplianceScope.Campaign));
    }

    private static void ClockRegressDrops()
    {
        var ledger = new PlayerComplianceLedger();
        ledger.OnDispatch(ComplianceScope.Campaign, 1, 500.0);
        AssertEqual(ComplianceOutcome.Neutral, ledger.Tick(ComplianceScope.Campaign, 400.0));
        AssertFalse(ledger.HasPending(ComplianceScope.Campaign), "dropped");
        AssertEqual(0, ledger.Samples);
    }

    private static void RepeatedMissesDecay()
    {
        var ledger = new PlayerComplianceLedger();
        for (int i = 0; i < 20; i++)
        {
            ledger.OnDispatch(ComplianceScope.Battle, i, i * 10.0);
            ledger.Tick(ComplianceScope.Battle, i * 10.0 + 1.0);
        }
        AssertTrue(ledger.Reliability < 0.01f, "twenty misses fall below 0.01");
        AssertTrue(ledger.Reliability >= 0f, "never negative");
        AssertEqual(20, ledger.Samples);
    }

    private static void RestoreOnlyWhenEmpty()
    {
        var empty = new PlayerComplianceLedger();
        AssertTrue(empty.Restore(0.8f, 12), "empty ledger adopts");
        AssertNear(0.8f, empty.Reliability, 1e-6f, "restored");
        AssertEqual(12, empty.Samples);

        var used = new PlayerComplianceLedger();
        used.OnDispatch(ComplianceScope.Battle, 1, 1.0);
        used.OnReached(ComplianceScope.Battle, 1);
        AssertFalse(used.Restore(0.1f, 40), "a ledger with this session's samples keeps them");
        AssertNear(0.625f, used.Reliability, 1e-6f, "kept");
    }

    private static void RestoreClampsAndRejects()
    {
        var a = new PlayerComplianceLedger();
        AssertTrue(a.Restore(1.7f, 3), "clamped restore");
        AssertNear(1f, a.Reliability, 1e-6f, "upper clamp");

        var b = new PlayerComplianceLedger();
        AssertFalse(b.Restore(float.NaN, 3), "NaN rejected");
        AssertFalse(b.Restore(0.4f, 0), "zero samples carry nothing");
        AssertNear(0.5f, b.Reliability, 1e-6f, "prior kept");
    }

    private static void StrengthWeight()
    {
        AssertNear(1f, PlayerComplianceLedger.StrengthWeight(false, 0.2f), 1e-6f, "AI unit");
        AssertNear(0.2f, PlayerComplianceLedger.StrengthWeight(true, 0.2f), 1e-6f, "player unit");
        AssertNear(0f, PlayerComplianceLedger.StrengthWeight(true, -3f), 1e-6f, "clamped");
        AssertNear(1f, PlayerComplianceLedger.StrengthWeight(true, float.NaN), 1e-6f, "non-finite counts fully");
    }

    private static void SidecarRoundTrip()
    {
        var snap = SidecarSnapshot.Empty();
        snap.PlayerCompliance = new PlayerComplianceDto { CareerKey = "42:002/A", Reliability = 0.3f, Samples = 7 };
        string json = SidecarDtoCodec.Serialize(SidecarDtoCodec.Encode(snap));
        AssertTrue(SidecarDtoCodec.TryDeserialize(json, out var dto, out _), "deserialize");
        AssertTrue(SidecarDtoCodec.TryDecode(dto, out var back, out _), "decode");
        AssertEqual("42:002/A", back.PlayerCompliance.CareerKey);
        AssertNear(0.3f, back.PlayerCompliance.Reliability, 1e-6f, "reliability");
        AssertEqual(7, back.PlayerCompliance.Samples);
    }

    private static void SidecarWithoutField()
    {
        string json = SidecarDtoCodec.Serialize(SidecarDtoCodec.Encode(SidecarSnapshot.Empty()));
        AssertFalse(json.Contains("playerCompliance\": {"), "nothing written without an estimate");
        AssertTrue(SidecarDtoCodec.TryDeserialize(json, out var dto, out _), "deserialize");
        AssertTrue(SidecarDtoCodec.TryDecode(dto, out var back, out _), "decode");
        AssertTrue(back.PlayerCompliance == null, "no estimate");
    }
}
