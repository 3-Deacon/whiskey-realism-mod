using WhiskeyRealism.Tactical.PlayerOrders;

namespace WhiskeyRealism.Strategic
{
    // W&L campaign dispatch decisions (chain-of-command spec §D, stage F plan; amended 2026-10-16).
    // Pure: WlCampaignDispatchRuntime reads the vanilla facts.
    //
    // Vanilla anchor (decompile, build 14703111): AIBattle.CheckCurrentOrderUpdate campaign gate 8366:
    //   a campaign order reaches the player only when the player's command is its own campaign-map
    //   group (flag2, 8271-8274). A brigade/division player inside a corps never gets one, and Whiskey
    //   sends him none either: vanilla moves that corps like any AI group and he is carried along.
    //   Campaign orders have no vanilla dedupe (the 8640-8646 check is battle-only), so the dispatcher
    //   of Whiskey's order for a map-group player rate-limits itself (Decide).

    // The targets of the last Capacity dispatches this dispatcher emitted (ring).
    // The rate limit is per target whatever the type: a strategic order for the same
    // target under another intent (16, then 6) is the same order to the player.
    internal sealed class WlDispatchHistory
    {
        public const int Capacity = 4;

        private readonly float[] _x = new float[Capacity];
        private readonly float[] _z = new float[Capacity];
        private readonly double[] _day = new double[Capacity];
        private int _count;
        private int _next;

        public void Record(float x, float z, double day)
        {
            _x[_next] = x;
            _z[_next] = z;
            _day[_next] = day;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        public bool TargetedWithin(float x, float z, float radius, double nowDays, double windowDays)
        {
            for (int i = 0; i < _count; i++)
            {
                float dx = x - _x[i];
                float dz = z - _z[i];
                if (dx * dx + dz * dz < radius * radius && nowDays - _day[i] < windowDays) return true;
            }
            return false;
        }

        public void Clear()
        {
            _count = 0;
            _next = 0;
        }
    }

    internal static class WlCampaignDispatchPolicy
    {
        public const int Redeploy = 5;
        public const int OffensiveOperation = 6;
        public const int Engage = 7;
        public const int DefendCapital = 8;
        public const int Offensive = 16;

        public const string Issue = "issue";

        // Same-target radius: vanilla's own order dedupe distance (8643).
        public const float DedupeUnits = 110f;
        // One pop per target per game day.
        public const double RateLimitDays = 1.0;
        // The campaign clock counts years of 8,766 hours (365.25 days, vanilla's own: 30171, 66611); the one
        // day-to-year conversion every campaign window uses (WlCampaignDispatchRuntime.NowDays,
        // the courier queue's cadence window).
        public const double HoursPerYear = 8766.0;
        public const double RateLimitYears = RateLimitDays * 24.0 / HoursPerYear;
        // Vanilla's campaign order zone is 20 x 20 (14455, 14052); a target nearer than that to
        // the group is no order.
        public const float MinMoveUnits = 20f;

        // Issue, or the skip reason.
        public static string Decide(int type, float x, float z, float moveDistance, WlDispatchHistory history, in GivenOrderView current, double nowDays)
        {
            if (type < 0 || !IsFinite(x) || !IsFinite(z)) return "invalid-target";
            if (IsFinite(moveDistance) && moveDistance < MinMoveUnits) return "no-op-move";
            if (current.HasOrder && current.Type == type && Within(x, z, current.X, current.Z)) return "active-duplicate";
            if (history != null && history.TargetedWithin(x, z, DedupeUnits, nowDays, RateLimitDays)) return "rate-limit";
            return Issue;
        }

        private static bool Within(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return dx * dx + dz * dz < DedupeUnits * DedupeUnits;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
