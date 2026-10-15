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
    //   Vanilla's own campaign order types for the same decisions: offensive 5 (no
    //     enemy at the target area) / 16 (14449-14456), offensive micro-movement 6
    //     (14052), engage 7 (13715, 13775), capital defense 8 (11864). The offensive
    //     and the defensive attack branch list the unit only after MoveUnitTo returns
    //     (14383-14386, 13771-13775), so the runtime classifies after the vanilla call.
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

    // The moved unit's vanilla operation lists before the MoveUnitTo call
    // (WasInOffensive) and after its caller finished (the rest).
    internal struct WlMoveContext
    {
        public bool WasInOffensive;
        public bool InOffensive;
        public bool InDefensive;
        public bool InCapitalDefense;
        public bool EnemyAtTarget; // AIArea.enemycampaignunitsstrengthclose > 0 at the destination
    }

    // The last dispatch this dispatcher emitted.
    internal readonly struct WlDispatchRecord
    {
        public WlDispatchRecord(int type, float x, float z, double day)
        {
            HasValue = true;
            Type = type;
            X = x;
            Z = z;
            Day = day;
        }

        public bool HasValue { get; }
        public int Type { get; }
        public float X { get; }
        public float Z { get; }
        public double Day { get; }
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

        public static int ClassifyType(in WlMoveContext context)
        {
            if (context.InDefensive) return Engage;
            if (context.InCapitalDefense) return DefendCapital;
            if (context.InOffensive)
            {
                if (context.WasInOffensive) return OffensiveOperation;
                return context.EnemyAtTarget ? Offensive : Redeploy;
            }
            return Redeploy;
        }

        // Issue, or the skip reason.
        public static string Decide(int type, float x, float z, float moveDistance, in WlDispatchRecord last, in GivenOrderView current, double nowDays)
        {
            if (type < 0 || !IsFinite(x) || !IsFinite(z)) return "invalid-target";
            if (IsFinite(moveDistance) && moveDistance < MinMoveUnits) return "no-op-move";
            if (current.HasOrder && current.Type == type && Within(x, z, current.X, current.Z)) return "active-duplicate";
            if (last.HasValue && Within(x, z, last.X, last.Z) && nowDays - last.Day < RateLimitDays) return "rate-limit";
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
