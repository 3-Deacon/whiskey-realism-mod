using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class RecruitmentIntent
    {
        public int AllianceId;
        public Theater PreferredTheater = Theater.Unknown;
        public float StrengthRatio = 1f;
        public float OwnStateSupportFloor = 0.5f;
        public bool AllowDraftReplacement;
    }

    public sealed class RecruitmentStateCandidate
    {
        public int StateId;
        public Theater Theater;
        public int Volunteers;
        public int Drafts;
        public float Support;
        public bool IsRecruitable;
        public bool IsEnemyState;
        public bool IsLocalArea;
    }

    public struct RecruitmentStateDecision
    {
        public bool ShouldReplace;
        public int StateId;
        public string Reason;
    }

    public static class RecruitmentIntentLedger
    {
        public static RecruitmentStateDecision SelectState(
            RecruitmentIntent intent,
            IEnumerable<RecruitmentStateCandidate> candidates,
            int vanillaStateId,
            int strengthNeeded,
            bool excludeEnemyStates)
        {
            intent = intent ?? new RecruitmentIntent();

            RecruitmentStateCandidate best = null;
            float bestScore = float.MinValue;
            bool bestUsesDrafts = false;

            foreach (var candidate in candidates)
            {
                if (!IsEligible(intent, candidate, strengthNeeded, excludeEnemyStates, out bool usesDrafts))
                    continue;

                float score = Score(intent, candidate, strengthNeeded, usesDrafts);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                    bestUsesDrafts = usesDrafts;
                }
            }

            if (best == null)
                return Keep(vanillaStateId);

            if (best.StateId == vanillaStateId)
                return Keep(vanillaStateId);

            var vanilla = Find(candidates, vanillaStateId);
            float vanillaScore = vanilla != null && IsEligible(intent, vanilla, strengthNeeded, excludeEnemyStates, out bool vanillaUsesDrafts)
                ? Score(intent, vanilla, strengthNeeded, vanillaUsesDrafts)
                : float.MinValue;

            if (bestScore < vanillaScore + 0.25f)
                return Keep(vanillaStateId);

            return new RecruitmentStateDecision
            {
                ShouldReplace = true,
                StateId = best.StateId,
                Reason = Reason(intent, best, bestUsesDrafts)
            };
        }

        private static bool IsEligible(
            RecruitmentIntent intent,
            RecruitmentStateCandidate candidate,
            int strengthNeeded,
            bool excludeEnemyStates,
            out bool usesDrafts)
        {
            usesDrafts = false;
            if (candidate == null || !candidate.IsRecruitable) return false;
            if (excludeEnemyStates && candidate.IsEnemyState) return false;
            if (strengthNeeded > 0 && candidate.Support < intent.OwnStateSupportFloor) return false;

            int needed = strengthNeeded < 0 ? 0 : strengthNeeded;
            if (needed == 0)
                return candidate.Volunteers > 0 || candidate.Drafts > 0;

            if (candidate.Volunteers >= needed)
                return true;

            if (candidate.Drafts >= needed)
            {
                usesDrafts = true;
                return intent.AllowDraftReplacement || intent.StrengthRatio < 1f;
            }

            return false;
        }

        private static float Score(RecruitmentIntent intent, RecruitmentStateCandidate candidate, int strengthNeeded, bool usesDrafts)
        {
            float score = candidate.Support * 2.0f;
            if (candidate.Theater == intent.PreferredTheater) score += 1.0f;
            if (candidate.IsLocalArea) score += 0.35f;
            if (candidate.Volunteers >= strengthNeeded) score += 0.55f;
            if (usesDrafts) score -= 0.75f;
            score += CapacityScore(candidate, strengthNeeded);
            return score;
        }

        private static float CapacityScore(RecruitmentStateCandidate candidate, int strengthNeeded)
        {
            int needed = strengthNeeded <= 0 ? 1000 : strengthNeeded;
            float volunteers = candidate.Volunteers / (float)needed;
            float drafts = candidate.Drafts / (float)needed;
            float capped = volunteers > 2f ? 2f : volunteers;
            capped += drafts > 2f ? 0.5f : drafts * 0.25f;
            return capped * 0.2f;
        }

        private static RecruitmentStateCandidate Find(IEnumerable<RecruitmentStateCandidate> candidates, int stateId)
        {
            foreach (var candidate in candidates)
            {
                if (candidate != null && candidate.StateId == stateId)
                    return candidate;
            }
            return null;
        }

        private static RecruitmentStateDecision Keep(int vanillaStateId)
        {
            return new RecruitmentStateDecision { ShouldReplace = false, StateId = vanillaStateId, Reason = "vanilla" };
        }

        private static string Reason(RecruitmentIntent intent, RecruitmentStateCandidate candidate, bool usesDrafts)
        {
            if (candidate.Theater == intent.PreferredTheater && !usesDrafts)
                return "preferred-theater-volunteers";
            if (candidate.Theater == intent.PreferredTheater)
                return "preferred-theater";
            return !usesDrafts ? "volunteer-high-support" : "draft-needed";
        }
    }
}
