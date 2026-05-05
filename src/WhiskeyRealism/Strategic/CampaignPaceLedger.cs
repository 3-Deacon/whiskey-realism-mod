using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class CampaignPaceInput
    {
        public int AllianceId;
        public int Year;
        public int Month;
        public int PolicyChapter;
        public float OwnNationalMorale;
        public float EnemyNationalMorale;
        public float BreakMoraleTrigger;
        public float MinNationalMoraleSurrender;
        public int BattlesIn14Days;
        public int MajorBattlesIn14Days;
        public int CapitalDangerStreakDays;
        public int DaysSinceFrontSignatureChange;
        public bool IsWinter;
        public TheaterPressureView TheaterPressure;
    }

    public sealed class CampaignPaceOutput
    {
        public CampaignPace Pace;
        public CollapseRisk Risk;
        public bool IntentBlockedFromPreserve;
        public string Reason;
        public Theater TheaterPriority = Theater.Unknown;
    }

    public static class CampaignPaceLedger
    {
        public static CollapseRisk RiskFor(float ownMorale, float breakMoraleTrigger, float minSurrender)
        {
            if (ownMorale <= breakMoraleTrigger * 1.15f) return CollapseRisk.Critical;
            if (ownMorale <= minSurrender * 1.10f)        return CollapseRisk.Critical;
            if (ownMorale <= breakMoraleTrigger * 1.50f) return CollapseRisk.Elevated;
            return CollapseRisk.Low;
        }

        public static CampaignPaceOutput Build(CampaignPaceInput input)
        {
            var output = new CampaignPaceOutput();
            if (input == null) { output.Pace = CampaignPace.Stable; output.Reason = "missing-input"; return output; }

            output.Risk = RiskFor(input.OwnNationalMorale, input.BreakMoraleTrigger, input.MinNationalMoraleSurrender);

            // 1864 CSA collapse floor: cannot publish Preserve when risk >= Elevated.
            output.IntentBlockedFromPreserve =
                input.AllianceId == 1 && input.Year >= 1864 && output.Risk >= CollapseRisk.Elevated;

            // Derive theater priority from highest normalized enemy pressure.
            // Must be set before the cascade so all early-return paths carry the value.
            output.TheaterPriority = ComputeTheaterPriority(input.TheaterPressure);

            // Rule 1 — TooFastCollapse: Critical risk before 1864.
            if (output.Risk == CollapseRisk.Critical && input.Year <= 1863)
            {
                output.Pace = CampaignPace.TooFastCollapse;
                output.Reason = "critical-morale-pre-1864";
                return output;
            }

            // Rule 2 — LateWarPressure: chapter 3 OR (year >= 1864 + Union when CSA still has runway).
            bool unionLateRunway = input.AllianceId == 0 && input.Year >= 1864 &&
                                   input.EnemyNationalMorale > input.BreakMoraleTrigger * 1.5f;
            if (input.PolicyChapter >= 3 || unionLateRunway)
            {
                output.Pace = CampaignPace.LateWarPressure;
                output.Reason = "chapter3-or-late-union-runway";
                return output;
            }

            // Rule 3 — Overheated.
            if (input.MajorBattlesIn14Days >= 4 || (input.MajorBattlesIn14Days >= 2 && input.BattlesIn14Days >= 5))
            {
                output.Pace = CampaignPace.Overheated;
                output.Reason = "heavy-14d-battle-volume";
                return output;
            }

            // Rule 4 — TooQuiet (suppressed in chapter 1 winter).
            bool chapter1Winter = input.PolicyChapter <= 1 && input.IsWinter;
            if (!chapter1Winter &&
                input.BattlesIn14Days == 0 &&
                input.CapitalDangerStreakDays == 0)
            {
                output.Pace = CampaignPace.TooQuiet;
                output.Reason = "no-battles-no-streak-outside-winter1";
                return output;
            }

            // Rule 5 — Stalemated: chapter 2 + static front.
            if (input.PolicyChapter == 2 && input.DaysSinceFrontSignatureChange >= 60)
            {
                output.Pace = CampaignPace.Stalemated;
                output.Reason = "chapter2-front-static";
                return output;
            }

            output.Pace = CampaignPace.Stable;
            output.Reason = "default";
            return output;
        }

        private static Theater ComputeTheaterPriority(TheaterPressureView view)
        {
            if (view == null) return Theater.Unknown;
            var keys = view.EnemyStrengthByTheater.Keys;
            if (keys == null) return Theater.Unknown;

            Theater best = Theater.Unknown;
            float bestPressure = -1f;
            foreach (Theater theater in keys)
            {
                float p = view.NormalizedPressure(theater);
                if (p > bestPressure)
                {
                    bestPressure = p;
                    best = theater;
                }
            }
            return best;
        }
    }
}
