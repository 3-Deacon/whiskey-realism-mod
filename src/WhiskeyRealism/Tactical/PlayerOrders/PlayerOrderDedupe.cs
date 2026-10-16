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

    // Owned by TacticalPlayerDispatchRuntime: #62's player-order shadows. (The emitter's
    // own 110 m last-dispatch record is gone: the courier queue's cadence and en-route
    // tests, WlCourierQueue, cover #62 and the executor's ancestor dispatches alike.)
    internal sealed class PlayerOrderDedupeState
    {
        private readonly Dictionary<string, PlayerOrderShadow> _shadows = new Dictionary<string, PlayerOrderShadow>();
        public bool TryGetShadow(string unitKey, out PlayerOrderShadow shadow)
        {
            return _shadows.TryGetValue(unitKey ?? string.Empty, out shadow);
        }

        public void Record(PlayerOrderCandidate candidate, double nowHours)
        {
            RecordAttempt(candidate, nowHours);
        }

        // The candidate was queued or refused: its signature suppresses a repeat, and the throttle
        // window starts now.
        public void RecordAttempt(PlayerOrderCandidate candidate, double nowHours)
        {
            if (!candidate.HasCandidate) return;
            var signature = PlayerOrderSignature.FromCandidate(candidate);
            _shadows[candidate.UnitKey] = new PlayerOrderShadow(signature, nowHours, candidate.BattleIdentity);
        }

        // The candidate's order is gone for a reason that says nothing about #62 (superseded, unit gone,
        // cleared): forget the pending signature so the next evaluation starts fresh.
        public void ClearPending(string unitKey)
        {
            _shadows.Remove(unitKey ?? string.Empty);
        }

        public void RecordAccepted(PlayerOrderCandidate request, PlayerOrderCandidate active, double nowHours)
        {
            var requestSignature = PlayerOrderSignature.FromCandidate(request);
            var activeSignature = PlayerOrderSignature.FromCandidate(active);
            var shadow = new PlayerOrderShadow(requestSignature, activeSignature, nowHours, request.BattleIdentity);
            _shadows[request.UnitKey] = shadow;
            if (!string.Equals(request.UnitKey, active.UnitKey, System.StringComparison.Ordinal))
            {
                _shadows[active.UnitKey] = shadow;
            }
        }

        public void ClearForBattleBoundary(string battleIdentity)
        {
            _shadows.Clear();
        }

        public void ClearForPlayerCommandChange()
        {
            _shadows.Clear();
        }
    }

    internal static class PlayerOrderDedupe
    {
        public static PlayerOrderDedupeDecision Decide(
            PlayerOrderCandidate candidate,
            PlayerOrderActiveSnapshot active,
            PlayerOrderDedupeState state,
            PlayerOrderDedupeOptions options,
            double nowHours)
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

            bool retreat = IsValidEmergencyRetreat(candidate);

            // A retreat is the one order that goes through engage-at-will, a clear-hold transition or a
            // fresh order (capture policy rule 1); everything else yields to vanilla's own transitions.
            if (!retreat && IsVanillaTransition(active))
            {
                return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.YieldVanillaTransition, "vanilla-transition");
            }

            if (IsAmbiguousUnknownOrVanilla(active) &&
                !retreat &&
                !HasWhiskeyShadowForActive(active, state))
            {
                return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.BlockedByUnknownActiveOrder, "ambiguous-active-order");
            }

            // Nothing replaces a retreat (vanilla's clause g). Every other question of what vanilla's
            // dedupe would drop, and what Whiskey re-admits, is PlayerOrderReadmitPolicy's, decided
            // where the order is captured; #62 keeps no second model of it.
            if (active.HasActiveOrder && !active.Stale && !active.BattleEnded && active.VanillaType == 15 &&
                candidate.Scope == PlayerOrderScope.Tactical)
            {
                return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.BlockedByVanillaDedupe, "retreat-terminal");
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
                !retreat &&
                !IsClearHoldTransition(candidate, active) &&
                active.AgeHours < PlayerOrderReadmitPolicy.MinHoldBattleHours)
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

                double since = nowHours - shadow.NowHours;
                if (since >= 0.0 && since < options.ThrottleHours)
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
            double throttleHours,
            double nowHours)
        {
            return Decide(
                candidate,
                active,
                state,
                new PlayerOrderDedupeOptions(writesEnabled: true, throttleHours: throttleHours),
                nowHours);
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
