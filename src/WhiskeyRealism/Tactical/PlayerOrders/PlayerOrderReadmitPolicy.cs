using System;

namespace WhiskeyRealism.Tactical.PlayerOrders
{
    // Who produced an order bound for the W&L player's panel. The origin decides the
    // courier cadence key (a release never rate-limits vanilla's defend), the release
    // exception (D4 rule 4) and whether the order pays prestige (D21: Release and
    // FollowUp are automatic and pay none).
    internal enum OrderOrigin
    {
        Vanilla,
        P62,
        Executor,
        Release,
        FollowUp,
        Retreat,
        // The campaign map's Whiskey orders: Whiskey's strategic order bridge and the #69 dispatcher
        // (WlCampaignDispatchRuntime). Vanilla's own campaign calls are Vanilla.
        CampaignBridge,
        C69,
    }

    internal enum ReadmitAction
    {
        Admit,          // vanilla accepts the order as it stands
        AdmitWithClear, // vanilla would drop it; the policy re-admits (empty the slot, call vanilla)
        Defer,          // re-admittable once the hold has passed (deferUntil)
        Drop,           // not delivered
    }

    // The player's current DLC_WL.givenorder as the policy needs it. Position is the stored
    // one (terrain-sampled Y, 8638-8639); AreaRotation the stored arearotation (the issuing
    // call's rotation + 180, 8648).
    internal readonly struct ActiveOrderView
    {
        public ActiveOrderView(bool hasOrder, int type, float x, float y, float z, float areaRotation,
            double ageHours, double nowHours, bool objectiveOwnHeld, bool armyDefensive, bool campaignGroupFlag)
        {
            HasOrder = hasOrder;
            Type = type;
            X = x;
            Y = y;
            Z = z;
            AreaRotation = areaRotation;
            AgeHours = ageHours;
            NowHours = nowHours;
            ObjectiveOwnHeld = objectiveOwnHeld;
            ArmyDefensive = armyDefensive;
            CampaignGroupFlag = campaignGroupFlag;
        }

        public bool HasOrder { get; }
        public int Type { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float AreaRotation { get; }
        // Doctrine hours since the order was accepted; +infinity (or NaN) when Whiskey never saw it
        // issued (loaded, or present at battle start): treated as past the hold.
        public double AgeHours { get; }
        // The doctrine clock the age was read on, so a deferral can name an absolute time.
        public double NowHours { get; }
        public bool ObjectiveOwnHeld { get; }
        public bool ArmyDefensive { get; }
        // Vanilla's flag2: the player's command is a campaign-map group (a top-level player).
        public bool CampaignGroupFlag { get; }
    }

    // The order a caller wants delivered: the call's own arguments (rotation is the
    // `rotation` argument, before vanilla adds 180).
    internal readonly struct OrderCandidateView
    {
        public OrderCandidateView(int type, float x, float y, float z, float rotation, OrderOrigin origin, bool campaignCall)
        {
            Type = type;
            X = x;
            Y = y;
            Z = z;
            Rotation = rotation;
            Origin = origin;
            CampaignCall = campaignCall;
        }

        public int Type { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float Rotation { get; }
        public OrderOrigin Origin { get; }
        public bool CampaignCall { get; }
    }

    // The W&L order re-admit policy (D4-D6). Pure.
    //
    // Vanilla's battle-only dedupe (AIBattle.CheckCurrentOrderUpdate, decompile 8640-8646,
    // build 14703111) drops an order in seven cases, here (a)-(g) in source order:
    //   (a) same type, and type > 0 or within 110 m, unless a type 11 onto an active 11 (flag4);
    //   (b) a type 11 onto an active 11 within 110 m and 35 degrees;
    //   (c) a 12 while 0-5 or 13 is active;
    //   (d) anything but 13 while 13 is active;
    //   (e) a 14 while anything but 12 is active;
    //   (f) a top-level player's (flag2) 2 while 0-4 is active;
    //   (g) anything while 15 is active.
    // Vanilla locks the player into the first order of each type (and into 13 from first
    // contact on); this policy decides which dropped orders Whiskey re-admits at delivery.
    // It never admits what (e), (f) or (g) drop, and never touches a type-11 refresh.
    internal static class PlayerOrderReadmitPolicy
    {
        public const float ReadmitMeters = 110f;
        public const double MinHoldBattleHours = 0.25, ReleaseHysteresisBattleHours = 0.1, RetryThrottleBattleHours = 0.02;
        public const float RoutedShare = 0.5f;
        // Doctrine hours are doubles differenced from absolute clock readings; a hold or hysteresis
        // that has "exactly" elapsed must not miss by rounding (5.1 - 5.0 < 0.1).
        private const double ClockEpsilonHours = 1e-9;

        private const int Retreat = 15, EngageAtWill = 13, ClearHold = 14, Defend = 12, Reserve = 11, Support = 2;

        // True when vanilla's dedupe (8643) would drop the candidate. Campaign calls are never
        // deduped (8640 tests calledfromcampaign), and an empty slot has nothing to dedupe against.
        public static bool PredictVanilla(in ActiveOrderView active, in OrderCandidateView c)
        {
            if (!active.HasOrder || c.CampaignCall) return false;
            int at = active.Type;
            int t = c.Type;
            bool flag4 = t == Reserve && at == Reserve;
            float distance = Distance3D(in active, in c);

            if (at == t && (t > 0 || distance < ReadmitMeters) && !flag4) return true;                                     // (a)
            if (at == t && t == Reserve && distance < ReadmitMeters
                && AbsAngleDifference(active.AreaRotation, c.Rotation + 180f) < 35f) return true;                         // (b)
            if (t == Defend && IsAttackFamily(at)) return true;                                                           // (c)
            if (t != EngageAtWill && at == EngageAtWill) return true;                                                     // (d)
            if (at != Defend && t == ClearHold) return true;                                                              // (e)
            if (active.CampaignGroupFlag && at >= 0 && at <= 4 && t == Support) return true;                              // (f)
            return at == Retreat;                                                                                         // (g)
        }

        // The policy's verdict. `reason` is one of a fixed set (telemetry, tests); `deferUntil` is
        // the doctrine hour a Defer becomes admittable (NaN otherwise).
        public static ReadmitAction Decide(in ActiveOrderView active, in OrderCandidateView c, out double deferUntil, out string reason)
        {
            deferUntil = double.NaN;
            if (!PredictVanilla(in active, in c))
            {
                reason = "vanilla-accepts";
                return ReadmitAction.Admit;
            }

            int at = active.Type;
            int t = c.Type;

            // Rule 5: clauses (e), (f), (g) are kept, whatever else would admit.
            if (t == ClearHold && at != Defend) { reason = "clause-e"; return ReadmitAction.Drop; }
            if (active.CampaignGroupFlag && at >= 0 && at <= 4 && t == Support) { reason = "clause-f"; return ReadmitAction.Drop; }
            if (at == Retreat) { reason = t == Retreat ? "retreat-terminal" : "clause-g"; return ReadmitAction.Drop; }

            // Rule 1: a retreat over anything. Only an active 13 can still be blocking it here.
            if (t == Retreat) { reason = "retreat-over-engage"; return ReadmitAction.AdmitWithClear; }

            // Rule 4: engage-at-will gives way to the retreat (above) and to the release only.
            if (at == EngageAtWill)
            {
                if (t == Defend && c.Origin == OrderOrigin.Release) { reason = "release"; return ReadmitAction.AdmitWithClear; }
                reason = "engage-at-will";
                return ReadmitAction.Drop;
            }

            // The follow-up (D17 (b)): once a 1-4 order has been reached and held, the superior's "hold
            // what you reached" replaces it; vanilla's clause (c) would drop any other 12 over it.
            if (t == Defend && c.Origin == OrderOrigin.FollowUp && at >= 1 && at <= 4) { reason = "follow-up"; return ReadmitAction.AdmitWithClear; }

            bool pastHold = !(active.AgeHours < MinHoldBattleHours - ClockEpsilonHours);

            if (at == t)
            {
                if (t == Reserve) { reason = "reserve-refresh"; return ReadmitAction.Drop; }
                if (IsRetargetable(t))
                {
                    // Rule 2: the same type at a new place, after the hold.
                    if (Distance3D(in active, in c) <= ReadmitMeters) { reason = "retarget-near"; return ReadmitAction.Drop; }
                    if (pastHold) { reason = "retarget-after-hold"; return ReadmitAction.AdmitWithClear; }
                    deferUntil = active.NowHours - active.AgeHours + MinHoldBattleHours;
                    reason = "retarget-deferred";
                    return ReadmitAction.Defer;
                }
                reason = "vanilla-drops";
                return ReadmitAction.Drop;
            }

            if (t == Defend && IsAttackFamily(at))
            {
                // Rule 3: a defend over an attack once the objective is held or the army turned defensive.
                if (!active.ObjectiveOwnHeld && !active.ArmyDefensive) { reason = "defend-over-attack"; return ReadmitAction.Drop; }
                if (pastHold)
                {
                    reason = active.ObjectiveOwnHeld ? "defend-objective-held" : "defend-army-defensive";
                    return ReadmitAction.AdmitWithClear;
                }
                deferUntil = active.NowHours - active.AgeHours + MinHoldBattleHours;
                reason = "defend-deferred";
                return ReadmitAction.Defer;
            }

            reason = "vanilla-drops";
            return ReadmitAction.Drop;
        }

        // Engage-at-will is released when both the fog-honest test (no Visible enemy in fire
        // range) and vanilla's own 13 trigger (6800) have been false for the hysteresis, or at
        // once while the command is routed (D5, D6). quietSinceHours is when both last became
        // false; NaN or +infinity means they are not both false now.
        public static bool ReleaseDue(bool visibleEnemyInFireRange, bool vanillaContactTrigger, double quietSinceHours, double nowHours, bool routed)
        {
            if (routed) return true;
            if (visibleEnemyInFireRange || vanillaContactTrigger) return false;
            if (double.IsNaN(quietSinceHours) || double.IsInfinity(quietSinceHours)) return false;
            return nowHours - quietSinceHours >= ReleaseHysteresisBattleHours - ClockEpsilonHours;
        }

        // D6: at least RoutedShare of the command's regimental strength is routed or retreating.
        // A non-finite or negative strength counts as nothing; zero total strength is not routed.
        public static bool Routed(ReadOnlySpan<float> regimentStrength, ReadOnlySpan<bool> routedOrRetreating, int count)
        {
            int n = Math.Min(count, Math.Min(regimentStrength.Length, routedOrRetreating.Length));
            double total = 0.0, routed = 0.0;
            for (int i = 0; i < n; i++)
            {
                float s = regimentStrength[i];
                if (float.IsNaN(s) || float.IsInfinity(s) || s <= 0f) continue;
                total += s;
                if (routedOrRetreating[i]) routed += s;
            }
            return total > 0.0 && routed >= RoutedShare * total;
        }

        private static bool IsAttackFamily(int type) => type >= 0 && type <= 5;

        // Types whose same-type order is re-aimable at a new place (rule 2).
        private static bool IsRetargetable(int type) => (type >= 1 && type <= 4) || type == Defend;

        // Vector3.Distance, as vanilla measures num5 (8639-8640).
        private static float Distance3D(in ActiveOrderView a, in OrderCandidateView c)
        {
            float dx = c.X - a.X;
            float dy = c.Y - a.Y;
            float dz = c.Z - a.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // Tools.GetAbsAngleDifference (decompile 151779): the wrapped difference in [0, 180].
        // A non-finite input is a maximal difference rather than vanilla's endless loop.
        private static float AbsAngleDifference(float angle1, float angle2)
        {
            float d = angle1 - angle2;
            if (float.IsNaN(d) || float.IsInfinity(d)) return 180f;
            while (d < -180f) d += 360f;
            while (d > 180f) d -= 360f;
            return Math.Abs(d);
        }
    }
}
