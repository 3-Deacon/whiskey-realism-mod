using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.PlayerOrders;

using static TestHarness;

// Campaign couriers and capital defense (plan 2026-10-15-wl-order-flow.md D9, D11): the pure
// WlCampaignCourierPolicy (what a campaign call does at capture and at delivery), the campaign
// half of WlCourierDelay and WlCourierQueue, the sidecar mapping and the capital speed restore.
internal static class WlCampaignCourierTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("wl campaign courier repop of the active order is dropped", CampaignRepopOfActiveDropped),
            ("wl campaign courier type eight repop is dropped under the capital fix", TypeEightRepopDropped),
            ("wl campaign courier a changed capital still arrives", ChangedCapitalStillArrives),
            ("wl campaign courier another type or no order never repops", AnotherTypeNeverRepops),
            ("wl campaign courier capture never consults readmit", CampaignNeverConsultsReadmit),
            ("wl campaign courier cadence key includes the target", CampaignCadenceKeyIncludesTarget),
            ("wl campaign courier window is one day in years", CampaignWindowIsOneDayInYears),
            ("wl campaign courier years formula", CampaignYearsFormula),
            ("wl campaign courier non-positive factor counts as one", NonPositiveFactorCountsAsOne),
            ("wl campaign courier delivery re-decides a whiskey order", CampaignDeliveryReDecides),
            ("wl campaign courier delivery drops a vanilla repop", DeliveryDropsVanillaRepop),
            ("wl campaign courier history is recorded only for whiskey origins on accepted delivery", HistoryRecordedOnlyOnAcceptedDelivery),
            ("wl campaign courier delivers only on the campaign map", CampaignDeliversOnlyOnCampaignMap),
            ("wl campaign courier restored entry with a missing unit is discarded", RestoredEntryWithMissingUnitDiscarded),
            ("wl campaign courier queue sidecar round trip", CampaignQueueSidecarRoundTrip),
            ("wl campaign courier sidecar without the field", SidecarWithoutField),
            ("wl campaign courier sidecar rejects a bad entry", SidecarRejectsBadEntries),
            ("wl campaign courier queue restore keeps issue and due times", QueueRestoreKeepsTimes),
            ("wl campaign courier capital speed restored for player-owned only", SpeedRestoredForPlayerOwnedOnly),
            ("wl campaign courier capital speed keeps the player's own forced march", SpeedRestoreKeepsPlayersOwnForcedMarch),
            ("wl campaign courier capital speed skips cic and other factions", SpeedRestoreSkipsCic),
            ("wl campaign courier pin capture branch runs vanilla's gate first", CaptureBranchRunsGateFirst),
            ("wl campaign courier pin delivery runs after flush on the campaign map", DeliveryRunsAfterFlush),
            ("wl campaign courier pin delivery restores the bypass in a finally", DeliveryRestoresBypass),
            ("wl campaign courier pin writes no player unit order", NoPlayerUnitWriter),
            ("wl campaign courier pin sidecar export and restore are wired", SidecarWired),
            ("wl campaign courier pin capital patch is a non-skipping prefix and finalizer", CapitalPatchShape),
            ("wl campaign courier pin flags are on and described", FlagsDescribed),
        });
    }

    private static GivenOrderView Active(int type, float x, float z) => new GivenOrderView(true, type, x, z, 5);
    private static GivenOrderView NoOrder => new GivenOrderView(false, -1, 0f, 0f, 5);

    private static CourierEntry Entry(OrderOrigin origin, int type, float x, float z, string unit = "Army of the Potomac", double issued = 1.0, double due = 1.001)
    {
        return new CourierEntry
        {
            UnitId = 11, UnitName = unit, UnitCommanderId = 3, Type = type, X = x, Y = 10f, Z = z, Name = "Richmond",
            Rotation = -1f, Width = 20f, Depth = 20f, CampaignCall = true, Origin = origin, IssuerKey = 77,
            IssuerCommanderId = 4, ShowZone = true, IssuedAt = issued, DueAt = due,
        };
    }

    private sealed class NullSink : ICourierDiscardSink
    {
        public readonly List<string> Reasons = new List<string>();
        public void Discarded(in CourierEntry e, string reason) { Reasons.Add(reason); }
    }

    // ------------------------------------------------------------------ capture

    private static void CampaignRepopOfActiveDropped()
    {
        AssertEqual("repop", WlCampaignCourierPolicy.CaptureDropReason(6, 1050f, 1000f, Active(6, 1000f, 1000f), capitalFixOn: false, couriersOn: true), "same type, 50 units away");
        AssertEqual(null, WlCampaignCourierPolicy.CaptureDropReason(6, 1050f, 1000f, Active(6, 1000f, 1000f), capitalFixOn: true, couriersOn: false), "couriers off: a type 6 re-pop is vanilla's");
        AssertTrue(WlCampaignCourierPolicy.RepopOfActive(Active(16, 0f, 0f), 16, 3f, 4f), "3-4-5 is within 110");
    }

    private static void TypeEightRepopDropped()
    {
        AssertEqual("repop", WlCampaignCourierPolicy.CaptureDropReason(8, 2000f, 2000f, Active(8, 2010f, 2000f), capitalFixOn: true, couriersOn: false), "type 8 under the capital fix");
        AssertEqual("repop", WlCampaignCourierPolicy.CaptureDropReason(8, 2000f, 2000f, Active(8, 2010f, 2000f), capitalFixOn: true, couriersOn: true), "and with couriers");
        AssertEqual(null, WlCampaignCourierPolicy.CaptureDropReason(8, 2000f, 2000f, Active(8, 2010f, 2000f), capitalFixOn: false, couriersOn: true), "fix off: the queue's cadence handles it, not the repop rule");
    }

    private static void ChangedCapitalStillArrives()
    {
        AssertEqual(null, WlCampaignCourierPolicy.CaptureDropReason(8, 2000f, 2000f, NoOrder, true, true), "the first type 8");
        AssertEqual(null, WlCampaignCourierPolicy.CaptureDropReason(8, 2111f, 2000f, Active(8, 2000f, 2000f), true, true), "111 units away is a new capital");
        AssertEqual("repop", WlCampaignCourierPolicy.CaptureDropReason(8, 2110f, 2000f, Active(8, 2000f, 2000f), true, true), "exactly 110 is the same place (inclusive)");
    }

    private static void AnotherTypeNeverRepops()
    {
        AssertEqual(null, WlCampaignCourierPolicy.CaptureDropReason(6, 1000f, 1000f, Active(5, 1000f, 1000f), true, true), "another type on the panel");
        AssertEqual(null, WlCampaignCourierPolicy.CaptureDropReason(6, 1000f, 1000f, NoOrder, true, true), "nothing on the panel");
        AssertEqual(null, WlCampaignCourierPolicy.CaptureDropReason(6, float.NaN, 1000f, Active(6, 1000f, 1000f), true, true), "a non-finite point is vanilla's problem");
    }

    private static void CampaignNeverConsultsReadmit()
    {
        // Vanilla does not dedupe campaign calls (8640), so the battle re-admit policy has no say.
        foreach (string path in new[]
        {
            "src/WhiskeyRealism/Strategic/WlCampaignCourierPolicy.cs",
            "src/WhiskeyRealism/Strategic/WlCampaignCourierRuntime.cs",
        })
        {
            string text = TestFixtures.ReadRepoFile(path);
            AssertFalse(text.Contains("PlayerOrderReadmitPolicy"), path + " never consults the re-admit policy");
            AssertFalse(text.Contains("ReadmitOn"), path + " has no re-admit switch");
        }
    }

    // ------------------------------------------------------------------- queue

    private static void CampaignCadenceKeyIncludesTarget()
    {
        var queue = new WlCourierQueue(new NullSink());
        double window = WlCampaignDispatchPolicy.RateLimitYears;
        AssertTrue(queue.TryEnqueue(Entry(OrderOrigin.C69, 5, 1000f, 1000f, issued: 1.0), window, out _), "first order");
        AssertTrue(queue.TryTakeDue(2.0, out _, out _), "delivered");
        AssertTrue(queue.TryEnqueue(Entry(OrderOrigin.C69, 5, 3000f, 3000f, issued: 1.0 + window / 4), window, out string other) , "another target inside the window is not rate-limited: " + other);
        AssertFalse(queue.TryEnqueue(Entry(OrderOrigin.C69, 5, 1050f, 1000f, issued: 1.0 + window / 2), window, out string same), "the same target again");
        AssertEqual(WlCourierQueue.RateLimited, same, "rate-limited by issuer, type and target");
        AssertTrue(queue.TryEnqueue(Entry(OrderOrigin.C69, 5, 1050f, 1000f, issued: 1.0 + window * 1.5), window, out string later), "after the window it arrives again: " + later);
    }

    private static void CampaignWindowIsOneDayInYears()
    {
        AssertNear(1f / 365f, (float)WlCampaignDispatchPolicy.RateLimitYears, 1e-5f, "a game day in years");
        AssertNear((float)(24.0 / WlCampaignDispatchPolicy.HoursPerYear), (float)WlCampaignDispatchPolicy.RateLimitYears, 1e-9f, "one definition");
    }

    private static void CampaignYearsFormula()
    {
        var prefs = new CampaignCourierPrefs(0.0001f, 0.005f, true);
        AssertNear(0.00015f, (float)WlCourierDelay.CampaignYears(0f, prefs, CampaignIssuerFactors.One), 1e-7f, "d = 0: the floor of 10");
        AssertNear(0.0026f, (float)WlCourierDelay.CampaignYears(10000f, prefs, CampaignIssuerFactors.One), 1e-6f, "d = 10,000: sqrt(d) * 5 = 500");
        AssertNear(0f, (float)WlCourierDelay.CampaignYears(10000f, new CampaignCourierPrefs(0.0001f, 0.005f, false), CampaignIssuerFactors.One), 1e-9f, "order delays off");
    }

    private static void NonPositiveFactorCountsAsOne()
    {
        var prefs = new CampaignCourierPrefs(0f, 0.02f, true);
        double plain = WlCourierDelay.CampaignYears(10000f, prefs, CampaignIssuerFactors.One);
        double bad = WlCourierDelay.CampaignYears(10000f, prefs, new CampaignIssuerFactors(0f, -2f, float.NaN, float.PositiveInfinity, 0f, -0.5f));
        AssertNear((float)plain, (float)bad, 1e-9f, "a factor that is not positive and finite counts as 1");
        double halved = WlCourierDelay.CampaignYears(10000f, prefs, new CampaignIssuerFactors(0.5f, 1f, 1f, 1f, 1f, 1f));
        AssertNear((float)(plain / 2.0), (float)halved, 1e-9f, "a real factor multiplies");
    }

    // ---------------------------------------------------------------- delivery

    private static void CampaignDeliveryReDecides()
    {
        var history = new WlDispatchHistory();
        CourierEntry e = Entry(OrderOrigin.C69, 5, 1000f, 1000f);
        AssertEqual(null, WlCampaignCourierPolicy.DeliveryDropReason(in e, NoOrder, history, 100.0, true, true), "fresh: delivers (it is not rate-limited against its own target)");
        AssertEqual("active-duplicate", WlCampaignCourierPolicy.DeliveryDropReason(in e, Active(5, 1050f, 1000f), history, 100.0, true, true), "the order is already on the panel");
        history.Record(1000f, 1000f, 99.5);
        AssertEqual("rate-limit", WlCampaignCourierPolicy.DeliveryDropReason(in e, NoOrder, history, 100.0, true, true), "an earlier accepted order to the target within a day");
        AssertEqual(null, WlCampaignCourierPolicy.DeliveryDropReason(in e, NoOrder, history, 101.0, true, true), "a day later");
    }

    private static void DeliveryDropsVanillaRepop()
    {
        CourierEntry e = Entry(OrderOrigin.Vanilla, 8, 2000f, 2000f);
        AssertEqual("repop", WlCampaignCourierPolicy.DeliveryDropReason(in e, Active(8, 2020f, 2000f), null, 100.0, true, true), "the same order became active while it rode");
        AssertEqual(null, WlCampaignCourierPolicy.DeliveryDropReason(in e, Active(6, 2020f, 2000f), null, 100.0, true, true), "another order on the panel does not block a vanilla order");
    }

    private static void HistoryRecordedOnlyOnAcceptedDelivery()
    {
        AssertTrue(WlCampaignCourierPolicy.RecordsHistory(OrderOrigin.CampaignBridge), "bridge");
        AssertTrue(WlCampaignCourierPolicy.RecordsHistory(OrderOrigin.C69), "#69");
        AssertFalse(WlCampaignCourierPolicy.RecordsHistory(OrderOrigin.Vanilla), "a vanilla order has no dispatch history");
        // Emission queues; the history is written only after an order has been shown (the immediate path
        // with couriers off, or ShowQueued when a courier arrives), never when it is queued.
        string runtime = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Strategic/WlCampaignDispatchRuntime.cs");
        int start = runtime.IndexOf("private static string Emit(", StringComparison.Ordinal);
        int end = runtime.IndexOf("internal static bool ShowQueued(", StringComparison.Ordinal);
        AssertTrue(start >= 0 && end > start, "Emit and ShowQueued are where the plan says");
        string emit = runtime.Substring(start, end - start);
        int enqueue = emit.IndexOf("WlCampaignCourierRuntime.EnqueueWhiskey(", StringComparison.Ordinal);
        int show = emit.IndexOf("ShowOrder(", StringComparison.Ordinal);
        int record = emit.IndexOf("_history.Record(", StringComparison.Ordinal);
        AssertTrue(enqueue >= 0 && show > enqueue, "Emit queues first and shows after");
        AssertTrue(record > show, "Emit records only after the order has been shown");
        string queued = runtime.Substring(end);
        AssertTrue(queued.IndexOf("_history.Record(", StringComparison.Ordinal) > queued.IndexOf("ShowOrder(", StringComparison.Ordinal), "ShowQueued records after showing");
    }

    private static void CampaignDeliversOnlyOnCampaignMap()
    {
        CourierEntry e = Entry(OrderOrigin.Vanilla, 6, 1000f, 1000f);
        AssertEqual("cross-scope", WlCampaignCourierPolicy.DeliveryDropReason(in e, NoOrder, null, 100.0, false, true), "in a battle");
        CourierEntry battle = Entry(OrderOrigin.Vanilla, 6, 1000f, 1000f);
        battle.CampaignCall = false;
        AssertEqual("cross-scope", WlCampaignCourierPolicy.DeliveryDropReason(in battle, NoOrder, null, 100.0, true, true), "a battle entry never rides the campaign queue");
    }

    private static void RestoredEntryWithMissingUnitDiscarded()
    {
        CourierEntry e = Entry(OrderOrigin.C69, 5, 1000f, 1000f);
        AssertEqual("unit-gone", WlCampaignCourierPolicy.DeliveryDropReason(in e, NoOrder, null, 100.0, true, unitResolved: false), "the unit does not resolve after the load");
        CourierEntry noUnit = Entry(OrderOrigin.CampaignBridge, 5, 1000f, 1000f, unit: null);
        AssertEqual(null, WlCampaignCourierPolicy.DeliveryDropReason(in noUnit, NoOrder, null, 100.0, true, unitResolved: false), "an order with no issuer unit arrives from the player's group");
    }

    // ----------------------------------------------------------------- sidecar

    private static void CampaignQueueSidecarRoundTrip()
    {
        var snap = SidecarSnapshot.Empty();
        CourierEntry a = Entry(OrderOrigin.C69, 5, 1000f, 1500f, issued: 3.25, due: 3.2502);
        CourierEntry b = Entry(OrderOrigin.Vanilla, 8, 2000f, 2500f, unit: "II Corps", issued: 3.26, due: 3.2603);
        b.Name = "Washington";
        snap.CampaignCouriers = new List<CampaignCourierDto> { WlCampaignCourierSidecar.ToDto(in a), WlCampaignCourierSidecar.ToDto(in b) };
        string json = SidecarDtoCodec.Serialize(SidecarDtoCodec.Encode(snap));
        AssertTrue(SidecarDtoCodec.TryDeserialize(json, out var dto, out _), "deserialize");
        AssertTrue(SidecarDtoCodec.TryDecode(dto, out var back, out _), "decode");
        AssertEqual(2, back.CampaignCouriers.Count);
        AssertTrue(WlCampaignCourierSidecar.TryFromDto(back.CampaignCouriers[0], out CourierEntry a2), "first entry");
        AssertTrue(WlCampaignCourierSidecar.TryFromDto(back.CampaignCouriers[1], out CourierEntry b2), "second entry");
        AssertEqual("Army of the Potomac", a2.UnitName);
        AssertEqual(3, a2.UnitCommanderId);
        AssertEqual(5, a2.Type);
        AssertNear(1000f, a2.X, 1e-3f, "x");
        AssertNear(1500f, a2.Z, 1e-3f, "z");
        AssertEqual(OrderOrigin.C69, a2.Origin);
        AssertEqual(4, a2.IssuerCommanderId);
        AssertTrue(a2.ShowZone, "show zone");
        AssertTrue(a2.CampaignCall, "a campaign entry");
        AssertEqual(3.25, a2.IssuedAt);
        AssertEqual(3.2502, a2.DueAt);
        AssertEqual("II Corps", b2.UnitName);
        AssertEqual("Washington", b2.Name);
        AssertEqual(OrderOrigin.Vanilla, b2.Origin);
    }

    private static void SidecarWithoutField()
    {
        string json = SidecarDtoCodec.Serialize(SidecarDtoCodec.Encode(SidecarSnapshot.Empty()));
        AssertTrue(SidecarDtoCodec.TryDeserialize(json, out var dto, out _), "deserialize");
        AssertTrue(SidecarDtoCodec.TryDecode(dto, out var back, out _), "decode");
        AssertTrue(back.CampaignCouriers == null || back.CampaignCouriers.Count == 0, "an older sidecar has no pending orders");
    }

    private static void SidecarRejectsBadEntries()
    {
        CourierEntry good = Entry(OrderOrigin.C69, 5, 1000f, 1500f);
        CampaignCourierDto dto = WlCampaignCourierSidecar.ToDto(in good);
        AssertTrue(WlCampaignCourierSidecar.TryFromDto(dto, out _), "the good one");
        dto = WlCampaignCourierSidecar.ToDto(in good); dto.X = float.NaN;
        AssertFalse(WlCampaignCourierSidecar.TryFromDto(dto, out _), "NaN point");
        dto = WlCampaignCourierSidecar.ToDto(in good); dto.DueAt = double.PositiveInfinity;
        AssertFalse(WlCampaignCourierSidecar.TryFromDto(dto, out _), "infinite due time");
        dto = WlCampaignCourierSidecar.ToDto(in good); dto.Type = 3;
        AssertFalse(WlCampaignCourierSidecar.TryFromDto(dto, out _), "a type no campaign order has");
        dto = WlCampaignCourierSidecar.ToDto(in good); dto.Origin = "Executor";
        AssertFalse(WlCampaignCourierSidecar.TryFromDto(dto, out _), "a battle origin");
        dto = WlCampaignCourierSidecar.ToDto(in good); dto.Origin = "nonsense";
        AssertFalse(WlCampaignCourierSidecar.TryFromDto(dto, out _), "an unknown origin");
        AssertFalse(WlCampaignCourierSidecar.TryFromDto(null, out _), "null");
        var many = new List<CampaignCourierDto>();
        for (int i = 0; i < 20; i++) many.Add(WlCampaignCourierSidecar.ToDto(in good));
        var entries = new List<CourierEntry>();
        WlCampaignCourierSidecar.Sanitize(many, entries);
        AssertEqual(WlCourierQueue.MaxPending, entries.Count);
    }

    private static void QueueRestoreKeepsTimes()
    {
        var sink = new NullSink();
        var queue = new WlCourierQueue(sink);
        CourierEntry early = Entry(OrderOrigin.C69, 5, 1000f, 1000f, issued: 3.0, due: 3.5);
        CourierEntry late = Entry(OrderOrigin.Vanilla, 6, 4000f, 4000f, issued: 3.1, due: 3.2);
        queue.Restore(in early);
        queue.Restore(in late);
        AssertEqual(2, queue.Count);
        var copy = new CourierEntry[WlCourierQueue.MaxPending];
        AssertEqual(2, queue.CopyPending(copy));
        AssertFalse(queue.TryTakeDue(3.0, out _, out _), "nothing due before its time");
        AssertTrue(queue.TryTakeDue(3.3, out CourierEntry first, out bool superseded), "the later-issued one is due first");
        AssertEqual(6, first.Type);
        AssertFalse(superseded, "not superseded");
        AssertEqual(3.1, first.IssuedAt);
        AssertEqual(0, sink.Reasons.Count);
        // A restored entry forgets nothing it was told: the cadence history is not persisted.
        AssertTrue(queue.TryEnqueue(Entry(OrderOrigin.C69, 5, 1000f, 1000f, issued: 3.01), WlCampaignDispatchPolicy.RateLimitYears, out string drop) == false && drop == WlCourierQueue.EnRoute, "the restored entry is still riding to that point");
    }

    // ------------------------------------------------------------ capital speed

    private static void SpeedRestoredForPlayerOwnedOnly()
    {
        var snapshot = new CapitalSpeedSnapshot<string>();
        snapshot.Capture("player-corps", running: false, doublequick: false);
        snapshot.Capture("player-brigade", running: false, doublequick: false);
        var restored = new List<string>();
        var inCapitalList = new HashSet<string> { "player-corps", "ai-corps" };
        snapshot.Restore(unit => inCapitalList.Contains(unit), (unit, running, doublequick) => restored.Add(unit + ":" + running + ":" + doublequick));
        AssertEqual(1, restored.Count);
        AssertEqual("player-corps:False:False", restored[0]);
        AssertEqual(0, snapshot.Count);
        AssertFalse(restored.Contains("ai-corps:True:True"), "an AI group keeps vanilla's march order");
    }

    private static void SpeedRestoreKeepsPlayersOwnForcedMarch()
    {
        var snapshot = new CapitalSpeedSnapshot<string>();
        snapshot.Capture("player-corps", running: true, doublequick: true);
        var restored = new List<string>();
        snapshot.Restore(unit => true, (unit, running, doublequick) => restored.Add(running + ":" + doublequick));
        AssertEqual("True:True", restored[0]);
    }

    private static void SpeedRestoreSkipsCic()
    {
        AssertTrue(CapitalDefenseSpeed.Applies(wlActive: true, factionIsPlayers: true, playerIsCic: false, flagOn: true), "a subordinate player's own faction");
        AssertFalse(CapitalDefenseSpeed.Applies(true, true, playerIsCic: true, true), "CIC: vanilla skips the player's own faction");
        AssertFalse(CapitalDefenseSpeed.Applies(true, factionIsPlayers: false, false, true), "the enemy's faction");
        AssertFalse(CapitalDefenseSpeed.Applies(wlActive: false, true, false, true), "no W&L career");
        AssertFalse(CapitalDefenseSpeed.Applies(true, true, false, flagOn: false), "flag off");
    }

    // ------------------------------------------------------------------- pins

    private static string Code(string path)
    {
        return System.Text.RegularExpressions.Regex.Replace(TestFixtures.ReadRepoFile(path), @"//[^\n]*", string.Empty);
    }

    private static void CaptureBranchRunsGateFirst()
    {
        string flow = Code("src/WhiskeyRealism/Tactical/PlayerOrders/WlOrderFlowRuntime.cs");
        int start = flow.IndexOf("private static CaptureVerdict RouteCampaign(", StringComparison.Ordinal);
        AssertTrue(start >= 0, "a campaign branch");
        string body = flow.Substring(start, flow.IndexOf("private static void FillChainFacts(", start, StringComparison.Ordinal) - start);
        int gate = body.IndexOf("WlOrderCaptureGate.ReachesDedupe(", StringComparison.Ordinal);
        int capture = body.IndexOf("WlCampaignCourierRuntime.Capture(", StringComparison.Ordinal);
        AssertTrue(gate >= 0 && capture > gate, "vanilla's gate (8366) before the campaign runtime");
        AssertTrue(body.Contains("facts.CampaignCall = true"), "the gate is asked as a campaign call");
        AssertFalse(body.Contains("Readmit"), "no re-admit in the campaign branch");
        AssertTrue(flow.Contains("if (call.Campaign) return RouteCampaign(in call);"), "the route sends campaign calls there");
        string capturePatch = Code("src/WhiskeyRealism/Tactical/PlayerOrders/WlOrderFlowRuntime.cs");
        AssertTrue(capturePatch.Contains("if (Delivering) return true;"), "a delivered order is never captured twice");
    }

    private static void DeliveryRunsAfterFlush()
    {
        string patch = Code("src/WhiskeyRealism/Patches/WlCampaignMoveDispatchPatch.cs");
        int flush = patch.IndexOf("WlCampaignDispatchRuntime.Flush();", StringComparison.Ordinal);
        int deliver = patch.IndexOf("WlCampaignCourierRuntime.DeliverDue();", StringComparison.Ordinal);
        AssertTrue(flush >= 0 && deliver > flush, "the courier delivery follows the dispatcher's flush in UpdateUnitAI's Postfix");
        string runtime = Code("src/WhiskeyRealism/Strategic/WlCampaignCourierRuntime.cs");
        AssertTrue(runtime.Contains("onCampaignMap: true"), "delivery is a campaign-map pass");
        AssertFalse(runtime.Contains("Time.realtimeSinceStartup") || runtime.Contains("DateTime"), "the campaign clock only");
    }

    private static void DeliveryRestoresBypass()
    {
        string runtime = Code("src/WhiskeyRealism/Strategic/WlCampaignCourierRuntime.cs");
        int on = runtime.IndexOf("WlOrderBypass.Active = true;", StringComparison.Ordinal);
        int fin = runtime.IndexOf("finally", on, StringComparison.Ordinal);
        int off = runtime.IndexOf("WlOrderBypass.Active = false;", fin, StringComparison.Ordinal);
        AssertTrue(on >= 0 && fin > on && off > fin, "the bypass is cleared in a finally");
        AssertTrue(runtime.IndexOf("calledfromcampaign: true", StringComparison.Ordinal) > on, "vanilla is called as a campaign call");
    }

    private static void NoPlayerUnitWriter()
    {
        foreach (string path in new[]
        {
            "src/WhiskeyRealism/Strategic/WlCampaignCourierRuntime.cs",
            "src/WhiskeyRealism/Strategic/WlCampaignCourierPolicy.cs",
        })
        {
            string code = Code(path);
            foreach (string writer in new[] { "SetWaypoint(", "MoveUnitTo(", "ChangeStance(", "SetGroupFormation(", ".running =", ".doublequick =", "ChangeCombatBehavior(" })
                AssertFalse(code.Contains(writer), path + " never calls " + writer);
        }
        // The one write to a player-owned unit's fields is the march-mode restore (D9 b), and it only
        // writes back what the player had.
        string patch = Code("src/WhiskeyRealism/Patches/CapitalDefensePlayerSpeedFixPatch.cs");
        AssertTrue(patch.Contains("Snapshot.Capture(unit, unit.running, unit.doublequick)"), "the restore writes the snapshot");
        AssertTrue(patch.Contains("unit.dlcw_isundercommander"), "only player-owned units are snapshotted");
    }

    private static void SidecarWired()
    {
        string coordinator = Code("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        AssertTrue(coordinator.Contains("snap.CampaignCouriers = WlCampaignCourierRuntime.Export();"), "saved");
        AssertTrue(coordinator.Contains("WlCampaignCourierRuntime.Restore(snap.CampaignCouriers);"), "restored");
        string codec = Code("src/WhiskeyRealism/Strategic/SidecarDtoCodec.cs");
        AssertTrue(codec.Contains("CampaignCouriers = snap.CampaignCouriers") && codec.Contains("result.CampaignCouriers = dto.CampaignCouriers"), "encoded and decoded");
        string runtime = Code("src/WhiskeyRealism/Strategic/WlCampaignCourierRuntime.cs");
        AssertTrue(runtime.Contains("CampaignScope.Register(\"wl-campaign-courier\""), "campaign-scoped");
    }

    private static void CapitalPatchShape()
    {
        string patch = Code("src/WhiskeyRealism/Patches/CapitalDefensePlayerSpeedFixPatch.cs");
        AssertTrue(patch.Contains("[HarmonyPatch(typeof(AICampaign), \"AssignUnitToDefendCapital\")]"), "the private method");
        AssertTrue(patch.Contains("internal static void Prefix(int _aifaction)"), "a non-skipping Prefix");
        AssertTrue(patch.Contains("internal static Exception Finalizer(int _aifaction, Exception __exception)") && patch.Contains("return __exception;"), "a Finalizer that rethrows vanilla's own exception");
        AssertTrue(patch.Contains("campaign.patch.capital-defense-speed.prefix") && patch.Contains("campaign.patch.capital-defense-speed.finalizer"), "scope ids");
        AssertTrue(patch.Contains("PatchFirstFire.Mark(\"campaign.patch.capital-defense-speed.prefix\")") && patch.Contains("PatchFirstFire.Mark(\"campaign.patch.capital-defense-speed.finalizer\")"), "first-fire marks");
        AssertTrue(patch.Contains("CapitalDefenseSpeed.Applies("), "the pure gate decides");
        AssertTrue(patch.Contains("groupstodefendcapital"), "restores only what the call kept in the capital list");
    }

    private static void FlagsDescribed()
    {
        string plugin = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Plugin.cs");
        int at = plugin.IndexOf("\"Fix Capital Defense Player Order\"", StringComparison.Ordinal);
        AssertTrue(at >= 0, "the flag exists");
        string block = plugin.Substring(at, 700);
        AssertTrue(block.Contains("true,"), "default on");
        AssertTrue(block.Contains("Default ON.") && block.Contains("BUG-WL-018"), "described honestly");
        int couriers = plugin.IndexOf("\"Enable W&L Order Couriers\"", StringComparison.Ordinal);
        AssertTrue(plugin.Substring(couriers, 1200).Contains("campaign map"), "the couriers flag says it covers the campaign map");
    }
}
