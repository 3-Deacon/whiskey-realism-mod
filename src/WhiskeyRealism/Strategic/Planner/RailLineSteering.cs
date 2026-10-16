using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The pure decision of the railroad line steering Prefix (plan G5c, spec C11; the user approved the Prefix-skip 2026-10-15).
    //
    // Vanilla's AICampaign.UpdateRailroadConstruction (16052-16074) rolls every predefined line on its own: one
    // Random.Range(0, 1) per line in line order, tested BEFORE the line's progress (16069, the draw happens for every line), and a
    // success on a line with constructionprogress <= 0 calls Railroad.StartConstruction(alliance), which refuses a complete line,
    // a line whose IIPs the alliance does not all own and a line that is not permitted (77818-77831). No Postfix can redirect an
    // independent per-line roll, and filtering the line list would lower the start rate, so the patch replaces the loop and keeps
    // vanilla's probability, its draws and its number of starts; only WHICH eligible line starts is chosen.
    //
    // Vanilla starts only the line it is rolling, so at line j's roll the state of j equals the state at entry (a start touches
    // only its own line; IsPermitted and the IIP owners do not change inside the loop). Eligibility is therefore taken once, before
    // any start. A success on an eligible line starts exactly one line: the highest-scored eligible line not yet started in this
    // call, the rolled line itself when no other is strictly better. A success on an ineligible line starts nothing, as in vanilla.
    // The count: eligible lines >= successes on eligible lines, and every success uses one, so one eligible line not yet started
    // always remains; the number of starts equals vanilla's for the same draws.
    public static class RailLineSteering
    {
        // 16058-16069: the per-call chance is (probrailroadconstructionperyear + probrailroadconstructionperyearperindustr for each
        // of the alliance's four industry policies) x timediff, and a roll succeeds when it is <= that. Policies 7..10 (alliance 0)
        // and 107..110 are the four (16060-16066).
        public const int IndustryPolicyCount = 4;

        public static float ProbabilityPerCall(float baseProbabilityPerYear, float perIndustry, int industryPoliciesActive, float timediff)
        {
            int active = Math.Max(0, Math.Min(IndustryPolicyCount, industryPoliciesActive));
            return (baseProbabilityPerYear + perIndustry * active) * timediff;
        }

        // Whether the Prefix replaces vanilla's loop (true) or lets vanilla run (false). Vanilla's own first test (16054) returns for
        // the player's alliance outside ai_vs_ai and for alliance >= 2; this returns false for them too, so vanilla's guard is the one
        // that returns. It also stands down for the W&L commander-in-chief player's own alliance (under ai_vs_ai the planner stands
        // down there, 11620-11628), while vanilla's job machine is paused (11368: vanilla keeps rolling railroads on the alliance tick,
        // 81741, so its own roll runs), with the steering flags off, and without fresh scores (no plan, a stale plan, pre-war).
        public static bool ShouldSteer(bool enabled, int alliance, int playerAlliance, bool aiVsAi, bool wlCicPlayerAlliance, bool jobMachinePaused, bool freshScores)
        {
            if (!enabled || alliance < 0 || alliance >= 2) return false;
            if (alliance == playerAlliance && !aiVsAi) return false;
            if (wlCicPlayerAlliance || jobMachinePaused) return false;
            return freshScores;
        }

        // The line vanilla could start now: roll tested before progress (16069), then StartConstruction(alliance, checkonly: true).
        public static bool Eligible(float constructionProgress, bool startable)
        {
            return constructionProgress <= 0f && startable;
        }

        // The lines whose roll started something in vanilla's loop, in line order: a success on a line vanilla could start.
        public static int[] RolledLines(bool[] eligible, float[] rolls, float probabilityPerCall)
        {
            var rolled = new List<int>();
            for (int j = 0; j < eligible.Length; j++)
                if (rolls[j] <= probabilityPerCall && eligible[j]) rolled.Add(j);
            return rolled.ToArray();
        }

        // The lines to start, in start order, for the draws `rolls` (rolls[j] is the j-th Random.Range(0f, 1f) of the call): the
        // k-th start answers the k-th rolled line (RolledLines). eligible[j] was taken once before any start; score[j] is the
        // steering preference (higher first).
        public static int[] Choose(bool[] eligible, float[] rolls, float probabilityPerCall, float[] score)
        {
            var rolled = RolledLines(eligible, rolls, probabilityPerCall);
            var started = new int[rolled.Length];
            var taken = new bool[eligible.Length];
            for (int k = 0; k < rolled.Length; k++)
            {
                int j = rolled[k];
                int best = BestNotTaken(eligible, taken, score);
                // The count argument in the header: k successes on eligible lines leave at least one eligible line not taken.
                if (best < 0) throw new InvalidOperationException("railroad steering: a success on an eligible line found no eligible line left");
                // The rolled line keeps its start unless another is strictly better; a rolled line already taken is not a candidate.
                if (!taken[j] && score[j] >= score[best]) best = j;
                taken[best] = true;
                started[k] = best;
            }
            return started;
        }

        // The highest-scored eligible line not yet started; the lowest index among equal scores.
        private static int BestNotTaken(bool[] eligible, bool[] taken, float[] score)
        {
            int best = -1;
            for (int k = 0; k < eligible.Length; k++)
            {
                if (!eligible[k] || taken[k]) continue;
                if (best < 0 || score[k] > score[best]) best = k;
            }
            return best;
        }
    }
}
