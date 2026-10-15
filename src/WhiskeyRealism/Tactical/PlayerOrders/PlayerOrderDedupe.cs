using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.PlayerOrders
{
    // The player's current DLC_WL.givenorder as the dispatch emitter sees it.
    internal readonly struct GivenOrderView
    {
        public GivenOrderView(bool hasOrder, int type, float x, float z, int session)
        {
            HasOrder = hasOrder;
            Type = type;
            X = x;
            Z = z;
            Session = session;
        }

        public bool HasOrder { get; }
        public int Type { get; }
        public float X { get; }
        public float Z { get; }
        public int Session { get; } // DLC_WL.GivenOrders.givenorderssession
    }

    // Owned by TacticalPlayerDispatchRuntime (battle-core D9): one instance serves
    // #62's player-order shadows and the emitter's last-dispatch record, so #62 and
    // the executor's ancestor dispatches share one dedupe.
    internal sealed class PlayerOrderDedupeState
    {
        private readonly Dictionary<string, PlayerOrderShadow> _shadows = new Dictionary<string, PlayerOrderShadow>();
        private bool _hasDispatch;
        private int _dispatchType;
        private float _dispatchX;
        private float _dispatchZ;
        private int _dispatchSession;

        // A dispatch repeats the last one only while the player still holds what it
        // produced: same type, target within `meters` of the last emitted one, no
        // given order issued since (vanilla bumps givenorderssession on every new
        // order, 8651), and the current given order is still that dispatch. Vanilla
        // clears givenorder without moving the session (end of day 47989, zone
        // completion 123533), and a refused attempt leaves another order active;
        // both re-arm. Type 11 is never suppressed: onto an active type 11 it is
        // vanilla's in-place refresh (flag4, no session bump), which vanilla's own
        // 8643 test already limits.
        public bool IsDuplicateDispatch(int type, float x, float z, in GivenOrderView current, float meters)
        {
            if (!_hasDispatch || type == 11 || _dispatchType != type) return false;
            if (!current.HasOrder || current.Session != _dispatchSession || current.Type != _dispatchType) return false;
            return Within(x, z, _dispatchX, _dispatchZ, meters) &&
                Within(current.X, current.Z, _dispatchX, _dispatchZ, meters);
        }

        private static bool Within(float ax, float az, float bx, float bz, float meters)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return dx * dx + dz * dz < meters * meters;
        }

        // givenOrderSession is the session after the vanilla call, so an accepted
        // order and a rejected attempt both dedupe until the next given order.
        public void RecordDispatch(int type, float x, float z, int givenOrderSession)
        {
            _hasDispatch = true;
            _dispatchType = type;
            _dispatchX = x;
            _dispatchZ = z;
            _dispatchSession = givenOrderSession;
        }

        public bool TryGetShadow(string unitKey, out PlayerOrderShadow shadow)
        {
            return _shadows.TryGetValue(unitKey ?? string.Empty, out shadow);
        }

        public void Record(PlayerOrderCandidate candidate, long tick)
        {
            RecordAttempt(candidate, tick);
        }

        public void RecordAttempt(PlayerOrderCandidate candidate, long tick)
        {
            if (!candidate.HasCandidate) return;
            var signature = PlayerOrderSignature.FromCandidate(candidate);
            _shadows[candidate.UnitKey] = new PlayerOrderShadow(signature, tick, candidate.BattleIdentity);
        }

        public void RecordAccepted(PlayerOrderCandidate request, PlayerOrderCandidate active, long tick)
        {
            var requestSignature = PlayerOrderSignature.FromCandidate(request);
            var activeSignature = PlayerOrderSignature.FromCandidate(active);
            var shadow = new PlayerOrderShadow(requestSignature, activeSignature, tick, request.BattleIdentity);
            _shadows[request.UnitKey] = shadow;
            if (!string.Equals(request.UnitKey, active.UnitKey, System.StringComparison.Ordinal))
            {
                _shadows[active.UnitKey] = shadow;
            }
        }

        public void ClearForBattleBoundary(string battleIdentity)
        {
            _shadows.Clear();
            _hasDispatch = false;
        }

        public void ClearForPlayerCommandChange()
        {
            _shadows.Clear();
            _hasDispatch = false;
        }
    }

    internal static class PlayerOrderDedupe
    {
        public static bool VanillaWouldBlock(
            PlayerOrderActiveSnapshot active,
            PlayerOrderCandidate candidate,
            bool campaignGroupFlag)
        {
            if (!active.HasActiveOrder || !candidate.HasCandidate)
            {
                return false;
            }

            if (active.Stale || active.BattleEnded)
            {
                return false;
            }

            if (active.VanillaType == 15 &&
                candidate.Scope == PlayerOrderScope.Tactical)
            {
                return true;
            }

            if (candidate.VanillaType == 12 && IsAny(active.VanillaType, 0, 1, 2, 3, 4, 5, 13))
            {
                return true;
            }

            if (active.VanillaType == 13 && candidate.VanillaType != 13)
            {
                return true;
            }

            if (candidate.VanillaType == 14 && active.VanillaType != 12)
            {
                return true;
            }

            if (campaignGroupFlag && candidate.VanillaType == 2 && IsAny(active.VanillaType, 0, 1, 2, 3, 4))
            {
                return true;
            }

            return active.VanillaType == candidate.VanillaType;
        }

        public static PlayerOrderDedupeDecision Decide(
            PlayerOrderCandidate candidate,
            PlayerOrderActiveSnapshot active,
            PlayerOrderDedupeState state,
            PlayerOrderDedupeOptions options,
            long tick)
        {
            state = state ?? new PlayerOrderDedupeState();
            if (!candidate.HasCandidate)
            {
                return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.NoCandidate, "no-candidate");
            }

            if (!options.WritesEnabled)
            {
                return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.BlockedByDisabledWrites, "writes-disabled");
            }

            if (IsVanillaTransition(active))
            {
                return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.YieldVanillaTransition, "vanilla-transition");
            }

            if (IsAmbiguousUnknownOrVanilla(active) &&
                !IsValidEmergencyRetreat(candidate) &&
                !HasWhiskeyShadowForActive(active, state))
            {
                return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.BlockedByUnknownActiveOrder, "ambiguous-active-order");
            }

            if (VanillaWouldBlock(active, candidate, candidate.CampaignGroupFlag || active.CampaignGroupFlag))
            {
                return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.BlockedByVanillaDedupe, "vanilla-dedupe");
            }

            if (IsVanillaSupportRequest(active) && candidate.Priority <= active.Priority)
            {
                return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.BlockedByScopePriority, "vanilla-support-request");
            }

            if (active.HasActiveOrder && active.Priority == int.MaxValue)
            {
                return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.BlockedByUnknownActiveOrder, "unknown-active-order");
            }

            if (active.HasActiveOrder &&
                candidate.Scope == active.Scope &&
                active.Priority > candidate.Priority &&
                !active.Stale &&
                !active.BattleEnded &&
                !IsClearHoldTransition(candidate, active))
            {
                return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.BlockedByScopePriority, "same-scope-priority");
            }

            if (!PassesScopePolicy(candidate, active))
            {
                return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.BlockedByScopePriority, "scope-priority");
            }

            var signature = PlayerOrderSignature.FromCandidate(candidate);
            if (state.TryGetShadow(candidate.UnitKey, out var shadow))
            {
                if (shadow.RequestSignature.MaterialEquals(signature))
                {
                    return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.SuppressSignature, "signature-match");
                }

                if (tick - shadow.Tick >= 0 && tick - shadow.Tick < options.ThrottleTicks)
                {
                    return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.SuppressThrottle, "throttle-window");
                }
            }

            return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.Issue, "issue");
        }

        public static PlayerOrderDedupeDecision Preview(
            PlayerOrderCandidate candidate,
            PlayerOrderActiveSnapshot active,
            PlayerOrderDedupeState state,
            long throttleTicks,
            long tick)
        {
            return Decide(
                candidate,
                active,
                state,
                new PlayerOrderDedupeOptions(writesEnabled: true, throttleTicks: throttleTicks),
                tick);
        }

        private static bool IsVanillaTransition(PlayerOrderActiveSnapshot active)
        {
            if (!active.HasActiveOrder || active.Stale || active.BattleEnded)
            {
                return false;
            }

            if (active.VanillaType == 14)
            {
                return active.Provenance == PlayerOrderProvenance.Vanilla;
            }

            return IsAny(active.VanillaType, 13, 15) &&
                (active.Provenance == PlayerOrderProvenance.Vanilla ||
                    active.Provenance == PlayerOrderProvenance.Unknown);
        }

        private static bool IsAmbiguousUnknownOrVanilla(PlayerOrderActiveSnapshot active)
        {
            return active.HasActiveOrder &&
                IsAny(active.VanillaType, 7, 12) &&
                (active.Provenance == PlayerOrderProvenance.Vanilla ||
                    active.Provenance == PlayerOrderProvenance.Unknown);
        }

        private static bool IsVanillaSupportRequest(PlayerOrderActiveSnapshot active)
        {
            return active.HasActiveOrder &&
                active.VanillaType == 11 &&
                !active.Stale &&
                !active.BattleEnded &&
                (active.Provenance == PlayerOrderProvenance.Vanilla ||
                    active.Provenance == PlayerOrderProvenance.Unknown);
        }

        private static bool HasWhiskeyShadowForActive(PlayerOrderActiveSnapshot active, PlayerOrderDedupeState state)
        {
            return state.TryGetShadow(active.UnitKey, out var shadow) &&
                shadow.BattleIdentity == active.BattleIdentity &&
                shadow.ActiveSignature.MatchesActiveOrder(active);
        }

        private static bool IsClearHoldTransition(PlayerOrderCandidate candidate, PlayerOrderActiveSnapshot active)
        {
            return candidate.VanillaType == 14 && active.VanillaType == 12;
        }

        private static bool IsValidEmergencyRetreat(PlayerOrderCandidate candidate)
        {
            return candidate.Intent == PlayerOrderIntent.RetreatToExit &&
                candidate.VanillaType == 15 &&
                candidate.ValidExitPoint;
        }

        private static bool PassesScopePolicy(PlayerOrderCandidate candidate, PlayerOrderActiveSnapshot active)
        {
            if (!active.HasActiveOrder || candidate.Scope == active.Scope)
            {
                return true;
            }

            if (candidate.Scope == PlayerOrderScope.Tactical && active.Scope == PlayerOrderScope.Campaign)
            {
                if (candidate.Intent == PlayerOrderIntent.RetreatToExit)
                {
                    return candidate.ValidExitPoint;
                }

                return candidate.Priority >= active.Priority + 40 && !active.ActiveCampaignActionable;
            }

            return true;
        }

        private static bool IsAny(int value, params int[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (value == values[i])
                {
                    return true;
                }
            }

            return false;
        }
    }
}
