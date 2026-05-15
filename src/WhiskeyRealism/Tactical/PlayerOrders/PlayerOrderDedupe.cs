using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.PlayerOrders
{
    internal sealed class PlayerOrderDedupeState
    {
        private readonly Dictionary<string, PlayerOrderShadow> _shadows = new Dictionary<string, PlayerOrderShadow>();

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
        }

        public void ClearForPlayerCommandChange()
        {
            _shadows.Clear();
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

            if (candidate.Scope == PlayerOrderScope.Campaign && active.Scope == PlayerOrderScope.Tactical)
            {
                return active.Stale || active.BattleEnded || candidate.Priority >= active.Priority + 40;
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
