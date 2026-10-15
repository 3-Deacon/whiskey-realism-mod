using System;
using WhiskeyRealism.Tactical.PlayerOrders;

using static TestHarness;

// Courier delay (D7): vanilla's own order-delay model for an order that reaches the W&L
// player's command (AIBattle/BattleUnits.AddOrderCourierline, decompile 125009-125171).
internal static partial class WlOrderFlowTests
{
    // unitprefs.txt defaults: processing 0.02 h, route 0.02 h, bugle recognition 0.02 h,
    // courier speed 2.33 * 2.2 * 1000 = 5126 m per game hour.
    private static BattleCourierPrefs DefaultBattlePrefs() => new BattleCourierPrefs(0.02f, 0.02f, 0.02f, 5126f, true);

    private static CourierLeg[] Legs(params (float meters, float bugle)[] legs)
    {
        var result = new CourierLeg[legs.Length];
        for (int i = 0; i < legs.Length; i++) result[i] = new CourierLeg(legs[i].meters, legs[i].bugle);
        return result;
    }

    private static void BugleLegIsMaxOfProcessingAndRecognition()
    {
        // Within bugle range the recognition time overlaps the processing time (125229-125237).
        var slowRecognition = new BattleCourierPrefs(0.02f, 0.02f, 0.05f, 5126f, true);
        var legs = Legs((50f, 75f));
        AssertNear(0.05f, (float)WlCourierDelay.BattleHours(legs, 1, in slowRecognition), 1e-6f, "recognition longer than processing");
        var slowProcessing = new BattleCourierPrefs(0.03f, 0.02f, 0.02f, 5126f, true);
        AssertNear(0.03f, (float)WlCourierDelay.BattleHours(legs, 1, in slowProcessing), 1e-6f, "processing longer than recognition");
        var defaults = DefaultBattlePrefs();
        AssertNear(0.02f, (float)WlCourierDelay.BattleHours(legs, 1, in defaults), 1e-6f, "default bugle floor is 0.02 h, not 0.04");
        // The range test is strict (num < buglerange): at exactly the range a courier rides.
        AssertTrue(WlCourierDelay.BattleHours(Legs((75f, 75f)), 1, in defaults) > 0.0200001, "75 m of a 75 m range is a courier");
    }

    private static void CourierLegIsProcessingPlusTravel()
    {
        var p = DefaultBattlePrefs();
        double hours = WlCourierDelay.BattleHours(Legs((2500f, 75f)), 1, in p);
        AssertNear(0.02f + 2500f / 5126f, (float)hours, 1e-5f, "2.5 km courier");
        AssertTrue(hours > 0.49 && hours < 0.52, "about half a game hour for 2.5 km");
    }

    private static void RelayLegAddsRouteTime()
    {
        var p = DefaultBattlePrefs();
        // Issuer 50 m from the division (bugle), the division 1000 m from the player (courier relay).
        double hours = WlCourierDelay.BattleHours(Legs((50f, 75f), (1000f, 75f)), 2, in p);
        AssertNear(0.02f + (0.02f + 1000f / 5126f), (float)hours, 1e-5f, "bugle leg + route time + travel");
    }

    private static void BugleRelayLegHasNoRouteTime()
    {
        var p = DefaultBattlePrefs();
        double hours = WlCourierDelay.BattleHours(Legs((50f, 75f), (60f, 75f)), 2, in p);
        AssertNear(0.04f, (float)hours, 1e-6f, "two bugle legs: processing/recognition then recognition only");
    }

    private static void OrderDelaysOffIsZero()
    {
        var off = new BattleCourierPrefs(0.02f, 0.02f, 0.02f, 5126f, false);
        AssertEqual(0.0, WlCourierDelay.BattleHours(Legs((2500f, 75f), (900f, 75f)), 2, in off), "battle order delays off");
        var campaignOff = new CampaignCourierPrefs(0.0001f, 0.005f, false);
        AssertEqual(0.0, WlCourierDelay.CampaignYears(5000f, in campaignOff, CampaignIssuerFactors.One), "campaign order delays off");
        // No legs: nothing to ride.
        var p = DefaultBattlePrefs();
        AssertEqual(0.0, WlCourierDelay.BattleHours(new CourierLeg[0], 0, in p), "no legs");
        // A non-finite or non-positive speed adds no travel rather than infinity.
        var stalled = new BattleCourierPrefs(0.02f, 0.02f, 0.02f, 0f, true);
        double stalledHours = WlCourierDelay.BattleHours(Legs((2500f, 75f)), 1, in stalled);
        AssertTrue(!double.IsInfinity(stalledHours) && !double.IsNaN(stalledHours), "zero courier speed stays finite");
    }

    private static void DefaultPrefsGiveTwelveHundredthsAt500m()
    {
        var p = DefaultBattlePrefs();
        double hours = WlCourierDelay.BattleHours(Legs((500f, 75f)), 1, in p);
        AssertNear(0.02f + 500f / 5126f, (float)hours, 1e-5f, "0.02 + 500/5126");
        AssertTrue(hours > 0.117 && hours < 0.118, "about 0.12 h");
    }

    private static void CampaignFloorIsTenOverThousand()
    {
        var p = new CampaignCourierPrefs(0.0001f, 0.005f, true);
        double atZero = WlCourierDelay.CampaignYears(0f, in p, CampaignIssuerFactors.One);
        AssertNear(0.005f * 10f / 1000f + 0.0001f, (float)atZero, 1e-8f, "max(10, 0) / 1000 floor plus processing");
        // sqrt(d) * 5 only exceeds the floor of 10 past d = 4.
        double d4 = WlCourierDelay.CampaignYears(4f, in p, CampaignIssuerFactors.One);
        AssertNear((float)atZero, (float)d4, 1e-8f, "d = 4 is still the floor");
        double far = WlCourierDelay.CampaignYears(10000f, in p, CampaignIssuerFactors.One);
        AssertNear(0.005f * 500f / 1000f + 0.0001f, (float)far, 1e-7f, "d = 10,000: sqrt 100 * 5 = 500");
    }

    private static void CampaignFactorsMultiplyIssuerOnly()
    {
        var p = new CampaignCourierPrefs(0.0001f, 0.005f, true);
        var f = new CampaignIssuerFactors(telegraph: 0.5f, winterQuarters: 2f, entrenchment: 1f, armyGroup: 1f, readiness: 1f, perks: 1f);
        double plain = WlCourierDelay.CampaignYears(10000f, in p, CampaignIssuerFactors.One);
        double scaled = WlCourierDelay.CampaignYears(10000f, in p, in f);
        // Factors scale the recognition part (x1.0 here: 0.5 * 2); the processing time is unscaled.
        AssertNear((float)plain, (float)scaled, 1e-9f, "0.5 * 2 = 1");
        var slow = new CampaignIssuerFactors(2f, 1f, 1f, 1f, 1f, 1f);
        AssertNear(0.005f * 500f / 1000f * 2f + 0.0001f, (float)WlCourierDelay.CampaignYears(10000f, in p, in slow), 1e-7f, "factor 2 doubles recognition only");
        // A factor that is zero, negative or NaN counts as 1 (vanilla never multiplies by it).
        var junk = new CampaignIssuerFactors(0f, -3f, float.NaN, float.PositiveInfinity, 1f, 1f);
        AssertNear((float)plain, (float)WlCourierDelay.CampaignYears(10000f, in p, in junk), 1e-9f, "non-positive or non-finite factors count as 1");
    }
}
