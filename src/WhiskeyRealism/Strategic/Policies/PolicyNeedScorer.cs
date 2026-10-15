using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Policies
{
    public struct PolicyScore
    {
        public int PolicyId;
        public float Total;
        public float Need;
        public float Personality;
        public float Profile;
        public float LookAhead;
        public float SlotPressure;
        public float Duration;
        public float SideCost;
        public float Irreversible;
    }

    // Need-driven, history-weighted policy score (spec § Score). History is the vanilla
    // personality list (Campaigns/<lvl>/<sub>/aipersonalities.dat, the order vanilla's
    // CheckAIPolicyChange 211079 walks), counted as a preference rather than a cage.
    public static class PolicyNeedScorer
    {
        public const float PersonalityWeight = 0.7f;
        public const float ProfileWeight = 0.3f;
        public const float LookAheadWeight = 0.5f;
        public const int LookAheadDepth = 3;
        public const float SlotPressureWeight = 0.4f;
        public const float DurationWeight = 1.0f;       // per research-year × the largest need
        public const float IrreversiblePenalty = 1.5f;
        public const float IrreversibleNeedGate = 0.7f;

        public static PolicyScore Score(
            PolicyCandidateFacts facts,
            NeedVector need,
            IReadOnlyList<int> personalityList,
            GrandStrategyProfile profile,
            int usedSlots,
            int maxSlots,
            Func<int, IEnumerable<int>> dependents)
        {
            var effect = PolicyEffectCatalog.Get(facts.PolicyId);
            var score = new PolicyScore { PolicyId = facts.PolicyId };
            if (need == null) need = new NeedVector();

            score.Need = NeedMet(effect, need);
            score.Personality = PersonalityPrior(facts.PolicyId, personalityList);
            score.Profile = ProfileWeight * ProfilePrior(effect, profile);
            score.LookAhead = LookAheadWeight * BestDependentNeed(facts.PolicyId, need, dependents);
            if (!facts.IsAct && maxSlots > 0 && usedSlots > 0)
            {
                float fill = Math.Min(1f, usedSlots / (float)maxSlots);
                score.SlotPressure = -SlotPressureWeight * fill * fill;
            }
            score.Duration = -DurationWeight * Math.Max(0f, facts.ResearchDuration) * need.Max;
            score.SideCost = -SideCost(effect, need);
            if (effect != null && effect.IrreversibleReason != null && GatingNeed(effect, need) < IrreversibleNeedGate)
                score.Irreversible = -IrreversiblePenalty;

            score.Total = score.Need + score.Personality + score.Profile + score.LookAhead
                + score.SlotPressure + score.Duration + score.SideCost + score.Irreversible;
            return score;
        }

        private static float NeedMet(PolicyEffect effect, NeedVector need)
        {
            if (effect == null) return 0f;
            float sum = 0f;
            for (int k = 0; k < PolicyEffectCatalog.NeedKindCount; k++)
                sum += need[(NeedKind)k] * effect.Weight((NeedKind)k);
            return sum;
        }

        private static float SideCost(PolicyEffect effect, NeedVector need)
        {
            if (effect == null) return 0f;
            float sum = 0f;
            for (int k = 0; k < PolicyEffectCatalog.NeedKindCount; k++)
                sum += need[(NeedKind)k] * effect.Cost((NeedKind)k);
            return sum;
        }

        // The largest need among the kinds the policy answers.
        private static float GatingNeed(PolicyEffect effect, NeedVector need)
        {
            float max = 0f;
            for (int k = 0; k < PolicyEffectCatalog.NeedKindCount; k++)
                if (effect.Weight((NeedKind)k) > 0f && need[(NeedKind)k] > max)
                    max = need[(NeedKind)k];
            return max;
        }

        private static float PersonalityPrior(int policyId, IReadOnlyList<int> list)
        {
            if (list == null || list.Count == 0) return 0f;
            for (int i = 0; i < list.Count; i++)
                if (list[i] == policyId)
                    return PersonalityWeight * (1f - i / (float)list.Count);
            return 0f;
        }

        private static float ProfilePrior(PolicyEffect effect, GrandStrategyProfile profile)
        {
            if (effect == null || profile == null) return 0f;
            float sum = 0f;
            for (int k = 0; k < PolicyEffectCatalog.NeedKindCount; k++)
            {
                var kind = (NeedKind)k;
                float weight = effect.Weight(kind);
                if (weight > 0f) sum += TagWeight(kind, profile) * weight;
            }
            return sum;
        }

        // NeedKind → grand-strategy tags (GrandStrategyRegistry); the strongest tag counts.
        // Credit, Treasury, Morale and Support have no strategy tag.
        private static float TagWeight(NeedKind kind, GrandStrategyProfile profile)
        {
            switch (kind)
            {
                case NeedKind.Manpower: return Math.Max(profile.WeightFor(StrategyTag.Manpower), profile.WeightFor(StrategyTag.Recruitment));
                case NeedKind.ContractStability: return profile.WeightFor(StrategyTag.Recruitment);
                case NeedKind.Recognition: return profile.WeightFor(StrategyTag.ForeignRecognition);
                case NeedKind.Blockade:
                    return Math.Max(profile.WeightFor(StrategyTag.Blockade),
                        Math.Max(profile.WeightFor(StrategyTag.PortAccess), profile.WeightFor(StrategyTag.TradeWarfare)));
                case NeedKind.Industry: return Math.Max(profile.WeightFor(StrategyTag.IndustrialBase), profile.WeightFor(StrategyTag.ArmsImports));
                case NeedKind.Agriculture: return profile.WeightFor(StrategyTag.Agriculture);
                case NeedKind.Logistics: return Math.Max(profile.WeightFor(StrategyTag.Logistics), profile.WeightFor(StrategyTag.RailHub));
                case NeedKind.Supply: return profile.WeightFor(StrategyTag.Logistics);
                default: return 0f;
            }
        }

        // Best need a policy unlocks: max over dependents d at depth n of NeedMet(d) / (1 + n),
        // up to LookAheadDepth. The runtime passes only same-side, unresearched dependents
        // that no unheld grand act blocks.
        private static float BestDependentNeed(int policyId, NeedVector need, Func<int, IEnumerable<int>> dependents)
        {
            if (dependents == null) return 0f;
            return BestDependentNeed(policyId, need, dependents, 1);
        }

        private static float BestDependentNeed(int policyId, NeedVector need, Func<int, IEnumerable<int>> dependents, int depth)
        {
            if (depth > LookAheadDepth) return 0f;
            var children = dependents(policyId);
            if (children == null) return 0f;
            float best = 0f;
            foreach (int child in children)
            {
                float own = NeedMet(PolicyEffectCatalog.Get(child), need) / (1f + depth);
                float deeper = BestDependentNeed(child, need, dependents, depth + 1);
                best = Math.Max(best, Math.Max(own, deeper));
            }
            return best;
        }
    }
}
