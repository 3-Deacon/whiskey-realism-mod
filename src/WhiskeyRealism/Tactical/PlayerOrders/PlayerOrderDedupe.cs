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
            var signature = PlayerOrderSignature.FromCandidate(candidate);
            _shadows[candidate.UnitKey] = new PlayerOrderShadow(signature, tick, candidate.BattleIdentity);
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

            if (IsAmbiguousUnknownOrVanilla(active) && !HasWhiskeyShadowForActive(active, state))
            {
                return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.BlockedByUnknownActiveOrder, "ambiguous-active-order");
            }

            if (VanillaWouldBlock(active, candidate, candidate.CampaignGroupFlag || active.CampaignGroupFlag))
            {
                return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.BlockedByVanillaDedupe, "vanilla-dedupe");
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
                if (shadow.Signature.MaterialEquals(signature))
                {
                    return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.SuppressSignature, "signature-match");
                }

                if (tick - shadow.Tick >= 0 && tick - shadow.Tick < options.ThrottleTicks)
                {
                    return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.SuppressThrottle, "throttle-window");
                }
            }

            state.Record(candidate, tick);
            return new PlayerOrderDedupeDecision(PlayerOrderDedupeDecisionKind.Issue, "issue");
        }

        private static bool IsVanillaTransition(PlayerOrderActiveSnapshot active)
        {
            return active.HasActiveOrder &&
                IsAny(active.VanillaType, 13, 14, 15) &&
                !active.Stale &&
                !active.BattleEnded &&
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

        private static bool HasWhiskeyShadowForActive(PlayerOrderActiveSnapshot active, PlayerOrderDedupeState state)
        {
            return state.TryGetShadow(active.UnitKey, out var shadow) &&
                shadow.BattleIdentity == active.BattleIdentity &&
                shadow.Signature.MatchesActiveOrder(active);
        }

        private static bool IsClearHoldTransition(PlayerOrderCandidate candidate, PlayerOrderActiveSnapshot active)
        {
            return candidate.VanillaType == 14 && active.VanillaType == 12;
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
