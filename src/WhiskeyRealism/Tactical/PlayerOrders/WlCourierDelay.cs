using System;

namespace WhiskeyRealism.Tactical.PlayerOrders
{
    // One hop of an order's path: from the issuing headquarters (or the previous relay) to the
    // next group, and the bugle range of the sender.
    internal readonly struct CourierLeg
    {
        public CourierLeg(float meters, float bugleRange)
        {
            Meters = meters;
            BugleRange = bugleRange;
        }

        public float Meters { get; }
        public float BugleRange { get; }
    }

    // GamePrefs read at runtime: processingtimegroupstandard (unitprefs.txt:1076), processingtimegrouproute
    // (:1078), buglestandardrecognitiontime (:1080), and the courier speed in metres per game hour
    // (unitbasicspeed[5, mounted] * runningspeedfact[5] * basicunitspeed; :231, :243, :854).
    internal readonly struct BattleCourierPrefs
    {
        public BattleCourierPrefs(float processing, float route, float recognition, float speedPerHour, bool orderDelays)
        {
            Processing = processing;
            Route = route;
            Recognition = recognition;
            SpeedPerHour = speedPerHour;
            OrderDelays = orderDelays;
        }

        public float Processing { get; }
        public float Route { get; }
        public float Recognition { get; }
        public float SpeedPerHour { get; }
        public bool OrderDelays { get; }
    }

    // Campaign GamePrefs: the processing time (campaignprefs.txt:205/207) and the raw bugle
    // recognition time (:209), which vanilla scales by max(10, sqrt(d) * 5) / 1000.
    internal readonly struct CampaignCourierPrefs
    {
        public CampaignCourierPrefs(float processing, float recognitionPer1000, bool orderDelays)
        {
            Processing = processing;
            RecognitionPer1000 = recognitionPer1000;
            OrderDelays = orderDelays;
        }

        public float Processing { get; }
        public float RecognitionPer1000 { get; }
        public bool OrderDelays { get; }
    }

    // The issuing unit's multipliers on the campaign recognition time (decompile 125037-125110),
    // each computed by the runtime exactly as vanilla computes it. The product of all of them.
    internal readonly struct CampaignIssuerFactors
    {
        public CampaignIssuerFactors(float telegraph, float winterQuarters, float entrenchment, float armyGroup, float readiness, float perks)
        {
            Telegraph = telegraph;
            WinterQuarters = winterQuarters;
            Entrenchment = entrenchment;
            ArmyGroup = armyGroup;
            Readiness = readiness;
            Perks = perks;
        }

        public static CampaignIssuerFactors One => new CampaignIssuerFactors(1f, 1f, 1f, 1f, 1f, 1f);

        public float Telegraph { get; }
        public float WinterQuarters { get; }
        public float Entrenchment { get; }
        public float ArmyGroup { get; }
        public float Readiness { get; }
        public float Perks { get; }
    }

    // How long an order takes to reach the W&L player's command, by vanilla's own order-delay
    // model (D7). Vanilla gives the player's command no delay at all (`flag`, 125017-125019,
    // 125138-125141); this is what the order flow replaces that with. Pure.
    internal static class WlCourierDelay
    {
        // Battle: the first leg costs max(P, R) within the sender's bugle range, else P + d / v;
        // each relay leg costs R within bugle range, else the route time + d / v (125031-125032,
        // 125148, 125163, 125229-125237). The range test is vanilla's `distance < buglerange`
        // (125035). A non-positive or non-finite courier speed adds no travel time.
        public static double BattleHours(ReadOnlySpan<CourierLeg> legsFromIssuer, int count, in BattleCourierPrefs p)
        {
            if (!p.OrderDelays) return 0.0;
            int n = Math.Min(count, legsFromIssuer.Length);
            double total = 0.0;
            for (int i = 0; i < n; i++)
            {
                CourierLeg leg = legsFromIssuer[i];
                double meters = Finite(leg.Meters);
                bool bugle = meters < leg.BugleRange;
                if (bugle)
                {
                    total += i == 0 ? Math.Max(p.Processing, p.Recognition) : p.Recognition;
                    continue;
                }
                double travel = p.SpeedPerHour > 0f && !float.IsInfinity(p.SpeedPerHour) && !float.IsNaN(p.SpeedPerHour)
                    ? meters / p.SpeedPerHour
                    : 0.0;
                total += (i == 0 ? p.Processing : p.Route) + travel;
            }
            return total;
        }

        // Campaign: always the bugle branch, R * max(10, sqrt(d) * 5) / 1000 times the issuer's
        // factors, plus the processing time (125033-125128). In campaign years.
        public static double CampaignYears(float meters, in CampaignCourierPrefs p, in CampaignIssuerFactors f)
        {
            if (!p.OrderDelays) return 0.0;
            double d = Finite(meters);
            double recognition = p.RecognitionPer1000 * Math.Max(10.0, Math.Sqrt(d) * 5.0) / 1000.0;
            recognition *= Factor(f.Telegraph) * Factor(f.WinterQuarters) * Factor(f.Entrenchment)
                * Factor(f.ArmyGroup) * Factor(f.Readiness) * Factor(f.Perks);
            return recognition + p.Processing;
        }

        // A factor that is not a positive finite number counts as 1: vanilla never multiplies by it.
        private static double Factor(float factor)
        {
            return factor > 0f && !float.IsInfinity(factor) ? factor : 1.0;
        }

        private static double Finite(float meters)
        {
            return float.IsNaN(meters) || float.IsInfinity(meters) || meters < 0f ? 0.0 : meters;
        }
    }
}
