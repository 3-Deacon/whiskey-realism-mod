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
            ("player compliance complied order restores reliability", CompliedRestores),
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
            ("player compliance career key survives a campaign battle round trip", CareerKeySurvivesBattleRoundTrip),
            ("player compliance career key does not allocate while unchanged", CareerKeyCachedWhileUnchanged),
            ("player compliance another career gets a fresh ledger", AnotherCareerGetsFreshLedger),
            ("player compliance export writes only the current career", ExportWritesOnlyCurrentCareer),
            ("player compliance export carries the restored current career", ExportCarriesRestoredCurrentCareer),
            ("player compliance vanilla re-issue of the same order is one sample", ReissueIsOneSample),
            ("player compliance a different order ends the repeat", DifferentOrderEndsRepeat),
            ("player compliance same order needs the same group, type and target", SameOrderIdentity),
            ("player compliance first look sets the baseline", FirstLookSetsBaseline),
        });
    }

    private static void StartsAtPrior()
    {
        // Trust until evidence: the AI adapts only after observed non-compliance.
        var ledger = new PlayerComplianceLedger();
        AssertNear(1f, ledger.Reliability, 1e-6f, "prior");
        AssertEqual(0, ledger.Samples);
        AssertFalse(ledger.HasPending(ComplianceScope.Campaign), "no pending");
    }

    private static void CompliedRestores()
    {
        var ledger = new PlayerComplianceLedger();
        ledger.OnDispatch(ComplianceScope.Battle, 1, 1.0);
        ledger.Tick(ComplianceScope.Battle, 2.0);
        AssertNear(0.75f, ledger.Reliability, 1e-6f, "one miss");
        AssertEqual(ComplianceOutcome.None, ledger.OnDispatch(ComplianceScope.Campaign, 4, 100.0));
        AssertEqual(ComplianceOutcome.Complied, ledger.OnReached(ComplianceScope.Campaign, 4));
        AssertNear(0.8125f, ledger.Reliability, 1e-6f, "0.75 + 0.25 * 0.25");
        AssertEqual(2, ledger.Samples);
        AssertFalse(ledger.HasPending(ComplianceScope.Campaign), "resolved");
        AssertEqual(ComplianceOutcome.None, ledger.OnReached(ComplianceScope.Campaign, 4));
        AssertEqual(2, ledger.Samples);
    }

    private static void CampaignMissedAfterGrace()
    {
        var ledger = new PlayerComplianceLedger();
        ledger.OnDispatch(ComplianceScope.Campaign, 1, 1000.0);
        AssertEqual(ComplianceOutcome.Missed, ledger.Tick(ComplianceScope.Campaign, 1000.0 + PlayerComplianceLedger.CampaignGraceHours + 0.01));
        AssertNear(0.75f, ledger.Reliability, 1e-6f, "1 - 0.25 * 1");
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
        AssertNear(1f, used.Reliability, 1e-6f, "kept");
        AssertEqual(1, used.Samples);
    }

    private static void RestoreClampsAndRejects()
    {
        var a = new PlayerComplianceLedger();
        AssertTrue(a.Restore(1.7f, 3), "clamped restore");
        AssertNear(1f, a.Reliability, 1e-6f, "upper clamp");

        var b = new PlayerComplianceLedger();
        AssertFalse(b.Restore(float.NaN, 3), "NaN rejected");
        AssertFalse(b.Restore(0.4f, 0), "zero samples carry nothing");
        AssertNear(1f, b.Reliability, 1e-6f, "prior kept");
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

    // Vanilla: LoadCampaignBattleScenario points GamePrefs.leveltoload at the
    // battlefield (36650-36651) but leaves SceneManagement.currentcampaignscenario
    // alone; the return rebuilds it from its own substrings (36810-36811, 36642).
    private static void CareerKeySurvivesBattleRoundTrip()
    {
        var careers = new PlayerComplianceCareers();
        string campaignScenario = "002/A";
        AssertTrue(careers.Ensure(12, campaignScenario), "first look at the career");
        careers.Ledger.OnDispatch(ComplianceScope.Campaign, 1, 0.0);
        careers.Ledger.Tick(ComplianceScope.Campaign, 100.0);
        AssertEqual(1, careers.Ledger.Samples);

        // In battle: leveltoload would read the battlefield; the scenario does not change.
        AssertFalse(careers.Ensure(12, campaignScenario), "campaign battle keeps the career");
        careers.Ledger.OnDispatch(ComplianceScope.Battle, 2, 1.0);
        careers.Ledger.OnReached(ComplianceScope.Battle, 2);
        AssertEqual(2, careers.Ledger.Samples);

        // Return: a new string with the same text.
        string rebuilt = campaignScenario.Substring(0, 3) + "/" + campaignScenario.Substring(4, 1);
        AssertFalse(ReferenceEquals(rebuilt, campaignScenario), "the reload builds a new string");
        AssertFalse(careers.Ensure(12, rebuilt), "the return keeps the career");
        AssertEqual(2, careers.Ledger.Samples);
    }

    private static void CareerKeyCachedWhileUnchanged()
    {
        var key = new ComplianceCareerKey();
        key.Update(12, "002/A");
        string first = key.Key;
        AssertEqual("12:002/A", first);
        for (int i = 0; i < 3; i++)
            AssertFalse(key.Update(12, "002/" + "A"), "unchanged");
        AssertTrue(ReferenceEquals(first, key.Key), "no new key string while unchanged");
        AssertTrue(key.Update(13, "002/A"), "another commander");
        AssertEqual("13:002/A", key.Key);
    }

    private static void AnotherCareerGetsFreshLedger()
    {
        var careers = new PlayerComplianceCareers();
        careers.Ensure(12, "002/A");
        careers.Ledger.OnDispatch(ComplianceScope.Campaign, 1, 0.0);
        careers.Ledger.Tick(ComplianceScope.Campaign, 100.0);
        AssertTrue(careers.Ensure(12, "001/A"), "another campaign");
        AssertEqual(0, careers.Ledger.Samples);
        AssertNear(PlayerComplianceLedger.Prior, careers.Ledger.Reliability, 1e-6f, "prior");
    }

    private static void ExportWritesOnlyCurrentCareer()
    {
        var careers = new PlayerComplianceCareers();
        careers.Restore("7:001/A", 0.4f, 6);
        careers.Ensure(12, "002/A");
        AssertFalse(careers.TryExport(out _, out _, out _), "another career's record stays out of this save");
        careers.Ledger.OnDispatch(ComplianceScope.Campaign, 1, 0.0);
        careers.Ledger.OnReached(ComplianceScope.Campaign, 1);
        AssertTrue(careers.TryExport(out string key, out _, out int samples), "current career");
        AssertEqual("12:002/A", key);
        AssertEqual(1, samples);
    }

    private static void ExportCarriesRestoredCurrentCareer()
    {
        var careers = new PlayerComplianceCareers();
        careers.Restore("12:002/A", 0.4f, 6);
        careers.Ensure(12, "002/A");
        AssertTrue(careers.TryExport(out string key, out float reliability, out int samples), "restored record");
        AssertEqual("12:002/A", key);
        AssertNear(0.4f, reliability, 1e-6f, "reliability");
        AssertEqual(6, samples);
    }

    // Vanilla re-issues capital defense every pass (11864) with a new session and
    // no campaign dedupe (8640-8646 is battle-only).
    private static void ReissueIsOneSample()
    {
        var stream = new ComplianceOrderStream();
        var capital = new ComplianceOrderKey(unit: 501, type: 8, x: 1000f, z: 2000f);
        AssertEqual(ComplianceOrderEvent.None, stream.See(false, 10, default(ComplianceOrderKey)), "baseline");
        AssertEqual(ComplianceOrderEvent.New, stream.See(true, 11, in capital));
        AssertEqual(ComplianceOrderEvent.Repeat, stream.See(true, 12, in capital));
        AssertEqual(ComplianceOrderEvent.None, stream.See(true, 12, in capital), "same session");
        AssertEqual(ComplianceOrderEvent.None, stream.See(false, 13, default(ComplianceOrderKey)), "cleared slot");
        var nudged = new ComplianceOrderKey(unit: 501, type: 8, x: 1050f, z: 2000f);
        AssertEqual(ComplianceOrderEvent.Repeat, stream.See(true, 14, in nudged), "a cleared slot does not end the order");
    }

    private static void DifferentOrderEndsRepeat()
    {
        var stream = new ComplianceOrderStream();
        var a = new ComplianceOrderKey(501, 5, 0f, 0f);
        var b = new ComplianceOrderKey(501, 5, 900f, 0f);
        stream.See(false, 1, default(ComplianceOrderKey));
        AssertEqual(ComplianceOrderEvent.New, stream.See(true, 2, in a));
        AssertEqual(ComplianceOrderEvent.New, stream.See(true, 3, in b));
        AssertEqual(ComplianceOrderEvent.New, stream.See(true, 4, in a), "A again after B is a new order");
    }

    private static void SameOrderIdentity()
    {
        var a = new ComplianceOrderKey(501, 5, 0f, 0f);
        AssertTrue(ComplianceOrderKey.Same(in a, new ComplianceOrderKey(501, 5, 100f, 0f)), "within 110");
        AssertFalse(ComplianceOrderKey.Same(in a, new ComplianceOrderKey(501, 5, 111f, 0f)), "beyond 110");
        AssertFalse(ComplianceOrderKey.Same(in a, new ComplianceOrderKey(502, 5, 0f, 0f)), "another corps");
        AssertFalse(ComplianceOrderKey.Same(in a, new ComplianceOrderKey(501, 16, 0f, 0f)), "another type");
        AssertFalse(ComplianceOrderKey.Same(in a, default(ComplianceOrderKey)), "no order");
    }

    private static void FirstLookSetsBaseline()
    {
        var stream = new ComplianceOrderStream();
        var standing = new ComplianceOrderKey(501, 8, 0f, 0f);
        AssertEqual(ComplianceOrderEvent.None, stream.See(true, 40, in standing), "standing order of unknown age");
        AssertEqual(ComplianceOrderEvent.Repeat, stream.See(true, 41, in standing), "its re-issue is not new");
        stream.Reset();
        AssertEqual(ComplianceOrderEvent.None, stream.See(true, 42, in standing), "reset: a new baseline");
    }
}
