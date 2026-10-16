using System;
using System.Globalization;
using UnityEngine;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AICampaign.UpdateRailroadConstruction(int alliance, float timediff) (decompile 16052-16074), called once per alliance
    // tick (81741). It returns for the player's alliance outside ai_vs_ai and for alliance >= 2 (16054), computes one per-call
    // chance (probrailroadconstructionperyear plus probrailroadconstructionperyearperindustr for each of the four industry
    // policies the alliance holds, 16058-16066, times timediff) and then rolls EVERY predefined line on its own: one
    // Random.Range(0, 1) per line in line order, drawn before the line's progress is tested (16069), and a success on a line with
    // constructionprogress <= 0 calls Railroad.StartConstruction(alliance), which refuses a complete line, a line whose IIPs the
    // alliance does not all own and a line that is not permitted (77818-77831). No Postfix can redirect an independent per-line
    // roll, and filtering BattleUnits.railroad would lower the start rate.
    //
    // PREFIX-SKIP, APPROVED BY THE USER 2026-10-15 (plan P14, spec C11 "Prefix-skip justification"). This replaces the 22-line loop
    // and nothing else: vanilla's guards run (this lets vanilla run for every case it would return in), vanilla's probability is
    // computed as 16058-16066 do, one UnityEngine.Random.Range(0f, 1f) is drawn per line in line order as 16069 does (so vanilla's
    // random stream is consumed exactly as before), and the number of starts equals vanilla's for the same draws: before any start
    // the patch takes eligible[j] = constructionprogress <= 0 && StartConstruction(alliance, checkonly: true) for every line (a start
    // touches only its own line, so each line's state at its roll equals its state at entry); a success on an eligible line starts
    // exactly one eligible line not yet started in this call, the highest-scored (RailLineSteering.Choose; RailLineScorer by the
    // line's endpoints) and the rolled line itself when none scores higher; a success on an ineligible line starts nothing. Every
    // started line is one vanilla could have started. StartConstruction draws no random number: its body (77833-77845) and what it
    // calls, ConstructionSymbol.UpdateCampaignIcon(forcehiding: true) (188262-188359, which returns at its first statement for a
    // railroad's symbol) and the panel hide it reaches (HidePanel 207853-207891), were read and none draws one, so drawing first and
    // starting afterwards leaves vanilla's stream and its starts unchanged.
    //
    // Vanilla runs (Prefix returns true) for the player's alliance outside ai_vs_ai, for alliance >= 2, for the W&L commander-in-chief
    // player's own alliance, while vanilla's job machine is paused (11368; vanilla keeps rolling railroads on the alliance tick, so
    // its own roll runs), with no fresh board (no plan, a stale plan, pre-war), with Enable Railroad Steering or the planner
    // executors off, and on any failure before the first draw. From the first draw on the patch always skips vanilla: running it
    // afterwards would roll the lines twice.
    [HarmonyPatch(typeof(AICampaign), "UpdateRailroadConstruction")]
    internal static class RailroadLineSteeringPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(int alliance, float timediff)
        {
            using (TelemetryPerf.Scope("campaign.patch.railroad-steering", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.railroad-steering");
                bool[] eligible;
                float[] score;
                float perCall;
                int lines;
                try
                {
                    if (!TryPrepare(alliance, timediff, out eligible, out score, out perCall, out lines)) return true;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("railroad-steering:prepare", "[Patch:RailroadSteering] could not prepare (vanilla's roll runs): " + ex.Message);
                    return true;
                }
                try
                {
                    Roll(alliance, eligible, score, perCall, lines);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("railroad-steering:roll", "[Patch:RailroadSteering] roll failed after the draws (vanilla's loop is skipped): " + ex.Message);
                }
                return false;
            }
        }

        // Everything up to the first draw: the gate, the lines' eligibility, vanilla's chance and the scores. No side effect.
        private static bool TryPrepare(int alliance, float timediff, out bool[] eligible, out float[] score, out float perCall, out int lines)
        {
            eligible = null;
            score = null;
            perCall = 0f;
            lines = 0;
            bool enabled = Plugin.EnableRailroadSteering != null && Plugin.EnableRailroadSteering.Value && CampaignPlannerRuntime.ExecutorsEnabled();
            if (!enabled) return false; // the cheap exit for the common case of a flag off; RailLineSteering.ShouldSteer holds the rule
            bool cicPlayerAlliance = DLC_WL.dlc_scenarioactive && alliance == GameVars.playeralliance && DLC_WL.IsCommanderInChief();
            bool fresh = ConstructionSteeringBoard.TryRead(alliance, out var board);
            if (!RailLineSteering.ShouldSteer(enabled, alliance, GameVars.playeralliance, GameVars.ai_vs_ai, cicPlayerAlliance,
                    StrategicCoordinator.WlVanillaJobMachinePaused(), fresh)) return false;
            var railroads = BattleUnits.railroad;
            lines = railroads != null ? railroads.Count : 0;
            if (lines == 0) return false;

            eligible = new bool[lines];
            score = new float[lines];
            for (int j = 0; j < lines; j++)
            {
                var line = railroads[j];
                eligible[j] = RailLineSteering.Eligible(line.constructionprogress, line.StartConstruction(alliance, checkonly: true));
                score[j] = j < board.RailScore.Length ? board.RailScore[j] : 0f;
            }
            perCall = RailLineSteering.ProbabilityPerCall(GamePrefs.probrailroadconstructionperyear, GamePrefs.probrailroadconstructionperyearperindustr,
                IndustryPoliciesHeld(alliance), timediff);
            return true;
        }

        // 16059-16066: policies 7..10 for alliance 0, 107..110 otherwise.
        private static int IndustryPoliciesHeld(int alliance)
        {
            int first = alliance != 0 ? 107 : 7;
            int held = 0;
            for (int i = 0; i < RailLineSteering.IndustryPolicyCount; i++)
                if (Policy.GetStatusByPolicyID(first + i)) held++;
            return held;
        }

        // One draw per line in line order, as 16069; then the starts.
        private static void Roll(int alliance, bool[] eligible, float[] score, float perCall, int lines)
        {
            var rolls = new float[lines];
            for (int j = 0; j < lines; j++) rolls[j] = UnityEngine.Random.Range(0f, 1f);
            var rolled = RailLineSteering.RolledLines(eligible, rolls, perCall);
            var started = RailLineSteering.Choose(eligible, rolls, perCall, score);
            for (int k = 0; k < started.Length; k++)
            {
                BattleUnits.railroad[started[k]].StartConstruction(alliance);
                TelemetryRouter.LegacyInfo("[RailroadSteer] alliance=" + alliance + " rolled=" + rolled[k] + " started=" + started[k]
                    + " score=" + score[started[k]].ToString("0.000", CultureInfo.InvariantCulture), TelemetryLayer.Campaign);
            }
        }
    }
}
