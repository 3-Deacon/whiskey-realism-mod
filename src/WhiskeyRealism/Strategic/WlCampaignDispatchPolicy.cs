using System;
using WhiskeyRealism.Tactical.PlayerOrders;

namespace WhiskeyRealism.Strategic
{
    // W&L campaign dispatches for a subordinate player (chain-of-command spec §D,
    // stage F plan). Pure: WlCampaignDispatchRuntime reads the vanilla facts.
    //
    // Vanilla anchors (decompile, build 14703111):
    //   AIBattle.CheckCurrentOrderUpdate campaign gate 8366: a campaign order reaches
    //     the player only when the player's command is its own campaign-map group
    //     (flag2, 8271-8274), so a brigade/division player inside a corps never gets
    //     one; vanilla moves that corps like any AI group.
    //   AICampaign.MoveUnitTo 14479-14540: true after a direct SetWaypoint (14514) or
    //     a queued DefensiveMovingOrder (14523); the queued order re-enters MoveUnitTo
    //     when it executes (DefensiveMovingOrder.UpdateOrders, 10048).
    //   AICampaign.MoveUnitTo reassigns its `position` argument to the stack position
    //     (14498) before anything else, so #69 reads the caller's target in a Prefix;
    //     `movearoundenemyunits` (never reassigned) selects the queued path.
    //   Vanilla's own campaign order types for the same decisions: offensive 5 when
    //     the target area's num2 <= 0, else 16 (num2 14228-14229, order 14449-14456),
    //     offensive micro-movement 6 (14052), engage 7 (13715, 13775), capital defense
    //     8 (11864). The offensive and the defensive attack branch list the unit only
    //     after MoveUnitTo returns (14383-14386, 13771-13775), so the runtime
    //     classifies after the vanilla call.
    //   RollUpEnemyObjectivesInZone (13883-13966, UpdateUnitAI job 35, 11598) moves a
    //     unit outside every operation (IsUnitAvailableForOffensiveOperations
    //     14080-14159 with checkexistingoperations) to the closest objective of its own
    //     area when the enemy there is no stronger (13944-13955); vanilla skips a W&L
    //     player's unit without an order (13915). Its semantics are the micro-movement's
    //     (same GetClosestObjectivePosOfArea target, 14024), whose order is 6 ("The enemy
    //     appears to be weak ... occupy further ground", tooltip 1087).
    //   CheckFinalPositions (17898-17936, job 34, 11593) moves a unit off terrain it
    //     cannot hold; vanilla gives no order for it.
    //   Campaign orders have no vanilla dedupe: the 8640-8646 check is battle-only.
    internal struct WlMoveFacts
    {
        public bool MoveSucceeded;
        public bool WlActive;
        public bool HasCommand;
        public bool PlayerIsCic;
        public bool PlayerIsOwnCampaignGroup;   // flag2: the player's command is a map group
        public bool MovedIsPlayerCampaignGroup; // BattleUnits.GetCampaignGroup(currentcommand)
        public bool MovedIsPlayerOwned;         // Regiment.dlcw_isundercommander
        public bool MovedByPlayer;              // DLC_WL.IsMovedByPlayer (player Operation)
        public bool MovedIsPlayerAlliance;
    }

    // The vanilla caller of a MoveUnitTo, where list membership cannot tell it
    // (#69 context patches on the two callers).
    internal enum WlMoveSource
    {
        Other = 0,
        FinalPositions = 1, // CheckFinalPositions 17928
        RollUp = 2,         // RollUpEnemyObjectivesInZone 13955
    }

    // The moved unit's vanilla operation lists before the MoveUnitTo call
    // (WasInOffensive) and after its caller finished (the rest).
    internal struct WlMoveContext
    {
        public WlMoveSource Source;
        public bool Queued;             // MoveUnitTo queued a DefensiveMovingOrder (14517-14523)
        public bool QueuedStillRunning; // DefensiveMovingOrder.OrderRunning at flush (9933)
        public bool ExecutesSkippedCorrection; // the queued execution of a skipped CheckFinalPositions move (WlCorrectionMemory)
        public bool WasInOffensive;
        public bool InOffensive;
        public bool InDefensive;
        public bool InCapitalDefense;
        public bool EnemyAtTarget; // vanilla's num2 > 0 for the target area (VanillaEnemyAtTarget)
    }

    // The targets of the last Capacity dispatches this dispatcher emitted (ring).
    // The rate limit is per target whatever the type: a queued move re-enters
    // MoveUnitTo (10048) after vanilla listed the unit, so its second pass
    // classifies as another type (16 -> 6) for the same target.
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

    // Terrain corrections #69 skipped, by unit and target. CheckFinalPositions
    // queues its move (17928, movearoundenemyunits: true), and UpdateOrders later
    // re-enters MoveUnitTo for it as a direct move (10048) with no vanilla caller to
    // mark it: that execution is the same correction and gives no dispatch either.
    // An entry lives only while it can still be that execution: it is dropped at the
    // first flush after the unit's queued order is gone (executed, or cancelled by
    // RemoveOrder 9949) and after one game day at most. TUnit is Regiment at runtime.
    internal sealed class WlCorrectionMemory<TUnit> where TUnit : class
    {
        public const int Capacity = 4;
        public const double ExpiryDays = 1.0;

        private readonly TUnit[] _unit = new TUnit[Capacity];
        private readonly float[] _x = new float[Capacity];
        private readonly float[] _z = new float[Capacity];
        private readonly double[] _day = new double[Capacity];
        private int _next;

        public int Count
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Capacity; i++) if (_unit[i] != null) n++;
                return n;
            }
        }

        // Called for every skipped pending move; remembers only a terrain correction.
        public void NoteSkip(TUnit unit, float x, float z, double nowDays, in WlMoveContext context, string skipReason)
        {
            if (unit == null || context.Source != WlMoveSource.FinalPositions
                || !string.Equals(skipReason, WlCampaignDispatchPolicy.PositionCorrection, StringComparison.Ordinal)) return;
            _unit[_next] = unit;
            _x[_next] = x;
            _z[_next] = z;
            _day[_next] = nowDays;
            _next = (_next + 1) % Capacity;
        }

        // True (and forgotten) when this direct move executes a live remembered
        // correction: same unit, target within the same-target radius (the queued
        // destination is re-stacked, 14498), younger than ExpiryDays.
        public bool ConsumeExecution(TUnit unit, float x, float z, double nowDays)
        {
            if (unit == null) return false;
            float r = WlCampaignDispatchPolicy.DedupeUnits;
            for (int i = 0; i < Capacity; i++)
            {
                if (!ReferenceEquals(_unit[i], unit) || !Live(i, nowDays)) continue;
                float dx = x - _x[i];
                float dz = z - _z[i];
                if (dx * dx + dz * dz >= r * r) continue;
                _unit[i] = null;
                return true;
            }
            return false;
        }

        // End of each flush: drops entries whose queued order is gone or that aged out.
        public void Expire(double nowDays, Func<TUnit, bool> queuedOrderRunning)
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (_unit[i] == null) continue;
                if (!Live(i, nowDays) || queuedOrderRunning == null || !queuedOrderRunning(_unit[i]))
                    _unit[i] = null;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < Capacity; i++) _unit[i] = null;
            _next = 0;
        }

        private bool Live(int i, double nowDays)
        {
            double age = nowDays - _day[i];
            return !double.IsNaN(age) && age >= 0.0 && age < ExpiryDays;
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

        // User decision 2026-10-15: subordinate dispatches keep vanilla's zone and
        // arrival prestige exactly as the base game pays them (CheckDLCWLTopics
        // 123484-123517: 150/100/120 for types 5/6/7), although the corps carries the
        // player into the zone; the one-per-target-per-day cap bounds it. Set false
        // to make them informational (showzone false, as vanilla's types 13/14: no
        // zone mark, no prestige).
        public const bool SubordinateDispatchesShowZone = true;

        public static bool ShowsZone(bool subordinateDispatch)
        {
            return !subordinateDispatch || SubordinateDispatchesShowZone;
        }

        // Same-target radius: vanilla's own order dedupe distance (8643).
        public const float DedupeUnits = 110f;
        // One pop per target per game day.
        public const double RateLimitDays = 1.0;
        // Vanilla's campaign order zone is 20 x 20 (14455, 14052); a move shorter than
        // that (a water or stacking correction, CheckFinalPositions 17928) is no order.
        public const float MinMoveUnits = 20f;

        public static bool IsTrigger(in WlMoveFacts facts, out string reason)
        {
            if (!facts.MoveSucceeded) { reason = "move-failed"; return false; }
            if (!facts.WlActive) { reason = "wl-inactive"; return false; }
            if (!facts.HasCommand) { reason = "no-command"; return false; }
            if (facts.PlayerIsCic) { reason = "player-cic"; return false; }
            if (facts.PlayerIsOwnCampaignGroup) { reason = "player-top-level"; return false; }
            if (!facts.MovedIsPlayerCampaignGroup) { reason = "not-player-campaign-group"; return false; }
            if (facts.MovedIsPlayerOwned) { reason = "player-owned"; return false; }
            if (facts.MovedByPlayer) { reason = "moved-by-player"; return false; }
            if (!facts.MovedIsPlayerAlliance) { reason = "other-alliance"; return false; }
            reason = "trigger";
            return true;
        }

        public const string QueuedOrderCancelled = "queued-order-cancelled";
        public const string PositionCorrection = "position-correction";

        // A recorded move that is no order at flush, or null.
        //   A queued move whose DefensiveMovingOrder is gone was cancelled after
        //   MoveUnitTo returned true (#25 CancelQueuedDefensiveMove -> RemoveOrder 9949;
        //   UpdateOrders 79295 runs outside UpdateUnitAI, so within the pass nothing
        //   else consumes it). The corps never marches there.
        //   A CheckFinalPositions move is a terrain correction vanilla gives no order for.
        public static string PendingSkipReason(in WlMoveContext context)
        {
            if (context.Queued && !context.QueuedStillRunning) return QueuedOrderCancelled;
            if (context.Source == WlMoveSource.FinalPositions || context.ExecutesSkippedCorrection) return PositionCorrection;
            return null;
        }

        // Vanilla's num2 for the target area (CheckOffensiveMovements 14228-14229):
        // enemy strength close x troop morale in zone x aiimportanceenemyunits x enemy
        // weapon factor, divided by the national morale factor; order 5 when num2 <= 0,
        // else 16 (14449-14456). Kept as vanilla's comparison, so NaN counts as enemy.
        public static bool VanillaEnemyAtTarget(float strengthClose, float troopMorale, float importanceEnemyUnits, float weaponFactor, float nationalMoraleFactor)
        {
            float num2 = strengthClose * troopMorale * importanceEnemyUnits * weaponFactor;
            num2 /= nationalMoraleFactor;
            return !(num2 <= 0f);
        }

        // One UpdateUnitAI pass can move the player's corps more than once (#25's
        // move back after a forbidden commit, a second job's move); only its last
        // move is the order.
        public static bool IsLastMoveOfUnit(int[] unitIds, int count, int index)
        {
            for (int i = index + 1; i < count; i++)
                if (unitIds[i] == unitIds[index]) return false;
            return true;
        }

        public static int ClassifyType(in WlMoveContext context)
        {
            if (context.InDefensive) return Engage;
            if (context.InCapitalDefense) return DefendCapital;
            if (context.Source == WlMoveSource.RollUp) return OffensiveOperation;
            if (context.InOffensive)
            {
                if (context.WasInOffensive) return OffensiveOperation;
                return context.EnemyAtTarget ? Offensive : Redeploy;
            }
            return Redeploy;
        }

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
