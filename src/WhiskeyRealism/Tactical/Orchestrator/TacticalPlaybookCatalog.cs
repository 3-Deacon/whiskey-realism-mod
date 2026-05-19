using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Registers playbooks and runs the weighted selection algorithm:
    ///   score = 0.5*personality + 0.2*terrain + 0.15*odds + 0.1*hint + 0.05*jitter
    /// per umbrella spec §"Selection algorithm". Highest-scoring playbook wins.
    /// Empty catalog returns null. Jitter is deterministic per JitterSeed.
    /// </summary>
    public sealed class TacticalPlaybookCatalog
    {
        private readonly List<TacticalPlaybook> _playbooks = new List<TacticalPlaybook>();

        public void Register(TacticalPlaybook playbook)
        {
            if (playbook == null) return;
            _playbooks.Add(playbook);
        }

        public int Count => _playbooks.Count;

        public TacticalPlaybook Select(PlaybookContext ctx)
        {
            if (_playbooks.Count == 0) return null;

            TacticalPlaybook best = null;
            float bestScore = float.NegativeInfinity;
            // Diagnostic counters: how often the faction-bias penalty engaged
            // in this selection pass. Emitted via EmitFactionBiasTrace below
            // so smoke can verify the bias actually fired (vs accidentally
            // picking the right playbook by personality-fit alone).
            int factionPenaltyApplied = 0;
            int factionEitherSkipped = 0;
            // Deterministic per-seed jitter (xorshift32). Seed must be non-zero for the
            // generator to produce useful state, so OR with 1.
            uint state = unchecked((uint)ctx.JitterSeed | 1u);
            for (int i = 0; i < _playbooks.Count; i++)
            {
                var pb = _playbooks[i];
                float personalityScore = pb.Fit.Score(ctx.CommanderPersonality);
                float terrainScore = pb.TerrainFit.Score(ctx.Terrain);
                float oddsScore = pb.PreferredOdds.Score(ctx.CurrentOdds);
                float hintScore = HintAffinity(pb.Id, ctx.OpposingCommanderHint);
                state = NextRand(state);
                float jitter = (state & 0xFFFFu) / 65535f;

                float score = 0.5f * personalityScore
                            + 0.2f * terrainScore
                            + 0.15f * oddsScore
                            + 0.1f * hintScore
                            + 0.05f * jitter;

                // Faction affinity bias (added 2026-05-19): subtract a small
                // penalty when the playbook's historical faction does NOT
                // match the commander's alliance. Prevents jarring outcomes
                // like "CSA Beauregard picks ShermanManeuverFix" where the
                // personality vector happens to score the Union playbook
                // slightly higher than Lee/Hood. Penalty magnitude (max 0.10)
                // is calibrated to flip CSA→Union or Union→CSA selections
                // when personality fit is close, but not override a strongly
                // mismatched personality. Generic + MeetingEngagement
                // playbooks are Either (no penalty either way).
                int affinity = FactionAffinity(pb.Id);
                if (affinity == 0) factionEitherSkipped++;
                if (affinity != 0 && ctx.AllianceId >= 0 && ctx.AllianceId <= 1)
                {
                    // affinity=+1 means CSA-historical, affinity=-1 means
                    // Union-historical. AllianceId=0 typically Union, =1 CSA
                    // (matches HistoricalFigureRegistry convention).
                    int commanderFactionSign = (ctx.AllianceId == 1) ? +1 : -1;
                    if (commanderFactionSign != affinity)
                    {
                        score -= FactionMismatchPenalty;
                        factionPenaltyApplied++;
                    }
                }

                // Envelopment bias: when the doctrine signals AttackNow + clear
                // standing advantage, add an additive bonus weighted by the
                // playbook's historical envelopment affinity. Bonus magnitude
                // (max 0.2) is tuned to swing the choice toward Lee/Jackson
                // when personality fit is close, but not override a strongly
                // mismatched commander (personality fit carries 0.5 weight).
                if (ctx.EnvelopmentPressure)
                {
                    score += 0.2f * EnvelopmentAffinity(pb.Id);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = pb;
                }
            }
            EmitFactionBiasTrace(ctx, best, factionPenaltyApplied, factionEitherSkipped);
            return best;
        }

        /// <summary>
        /// Diagnostic emission: after the selection loop, log the chosen
        /// playbook + how many candidates the faction-bias penalty
        /// engaged. Per the project's diagnostic-first pattern, we want
        /// smoke logs to show the bias actively fired so we can verify
        /// "CSA picks Lee" was driven by the alliance penalty, not by
        /// personality-fit coincidence. Signature-deduped per (allianceId,
        /// chosen-plan) so steady-state replans don't spam the sink.
        /// </summary>
        private static void EmitFactionBiasTrace(
            PlaybookContext ctx,
            TacticalPlaybook chosen,
            int penaltyApplied,
            int eitherSkipped)
        {
            if (chosen == null) return;
            if (ctx.AllianceId < 0 || ctx.AllianceId > 1) return;
            // Use System.Environment.TickCount instead of UnityEngine.Time —
            // Unity Time is an ECall (external native call) whose JIT-time
            // verification throws SecurityException in pure-.NET test
            // environments, escaping even our try-catch. TickCount is in
            // mscorlib, works in tests AND in Unity runtime. Wraps every
            // ~25 days which is irrelevant for a battle session.
            float nowSeconds = System.Environment.TickCount * 0.001f;

            try
            {
                int chosenAffinity = FactionAffinity(chosen.Id);
                int commanderFactionSign = (ctx.AllianceId == 1) ? +1 : -1;
                bool factionMatched = chosenAffinity == 0 || chosenAffinity == commanderFactionSign;
                string sig = "alliance=" + ctx.AllianceId
                    + "|plan=" + chosen.Id
                    + "|affinity=" + chosenAffinity
                    + "|match=" + factionMatched
                    + "|penaltyApplied=" + penaltyApplied
                    + "|eitherSkipped=" + eitherSkipped;
                string key = "tactical-faction-bias:" + ctx.AllianceId + ":" + chosen.Id;
                if (!WhiskeyRealism.Tactical.TacticalTelemetry.ShouldEmit(_lastFactionBiasTelemetryAt, key, sig, nowSeconds, FactionBiasTelemetrySeconds, false))
                    return;
                WhiskeyRealism.Telemetry.TelemetryRouter.Emit(
                    WhiskeyRealism.Telemetry.TelemetryLayer.Tactical,
                    WhiskeyRealism.Telemetry.TelemetryCategory.Decision,
                    "TacticalPlaybookFactionBias",
                    WhiskeyRealism.Telemetry.TelemetrySeverity.Info,
                    ev => ev
                        .WithDecision("TacticalPlaybookFactionBias", factionMatched ? "match" : "mismatch", sig)
                        .WithField("alliance", ctx.AllianceId)
                        .WithField("plan", chosen.Id.ToString())
                        .WithField("affinity", chosenAffinity)
                        .WithField("match", factionMatched)
                        .WithField("penaltyApplied", penaltyApplied)
                        .WithField("eitherSkipped", eitherSkipped));
            }
            catch { }
        }

        private static readonly System.Collections.Generic.Dictionary<string, float> _lastFactionBiasTelemetryAt = new System.Collections.Generic.Dictionary<string, float>();
        private const float FactionBiasTelemetrySeconds = 30f;

        /// <summary>
        /// Penalty subtracted from a playbook's score when the commander's
        /// alliance does not match the playbook's historical faction. Tuned
        /// to swing close personality-fit ties (e.g., Beauregard could score
        /// Sherman fractionally higher than Lee on pure personality) but not
        /// override a strongly mismatched personality (Hood commander with
        /// McClellan playbook still wins on personality magnitude).
        /// </summary>
        public const float FactionMismatchPenalty = 0.10f;

        /// <summary>
        /// Faction signature of each playbook. +1 = CSA-historical doctrine,
        /// -1 = Union-historical doctrine, 0 = generic / either-faction.
        /// Used by Select() to bias against CSA→Union (or vice versa)
        /// playbook selection on close ties.
        /// </summary>
        public static int FactionAffinity(BattlePlanId id)
        {
            switch (id)
            {
                // CSA-historical playbooks
                case BattlePlanId.LeeEnvelopment:           return +1;
                case BattlePlanId.JacksonValleyShuffle:     return +1;
                case BattlePlanId.LongstreetDefensiveOverslope: return +1;
                case BattlePlanId.HoodFrontalAssault:       return +1;
                case BattlePlanId.BraggIndecisiveCommit:    return +1;
                case BattlePlanId.ForrestCavalryRaid:       return +1;
                case BattlePlanId.JohnstonFabianDelay:      return +1;
                // Union-historical playbooks
                case BattlePlanId.McClellanPreparedDefense: return -1;
                case BattlePlanId.ShermanManeuverFix:       return -1;
                case BattlePlanId.GrantContinuousAttrition: return -1;
                case BattlePlanId.HookerFlankDeparture:     return -1;
                case BattlePlanId.BurnsideForcedAssault:    return -1;
                case BattlePlanId.BufordCavalryScreenDelay: return -1;
                // Generic / either-faction (no historical anchor) — no penalty
                case BattlePlanId.GenericAggressive:
                case BattlePlanId.GenericCautious:
                case BattlePlanId.GenericMethodical:
                case BattlePlanId.GenericDesperate:
                case BattlePlanId.MeetingEngagement:
                default:                                    return 0;
            }
        }

        /// <summary>
        /// Per-playbook envelopment affinity on [0, 1]. 1.0 = literal flank /
        /// envelopment doctrine; 0.0 = frontal-only or defensive. Values are
        /// historical: Lee's Chancellorsville/Second-Manassas envelopment,
        /// Jackson's Valley Campaign flank marches, Hooker's Chancellorsville
        /// flank-march (initial plan, before he stopped), Sherman's Atlanta
        /// maneuver-then-fix; vs Hood's frontal Devil's Den / Burnside's
        /// Fredericksburg assault.
        /// </summary>
        public static float EnvelopmentAffinity(BattlePlanId id)
        {
            switch (id)
            {
                case BattlePlanId.LeeEnvelopment:           return 1.00f;
                case BattlePlanId.JacksonValleyShuffle:     return 0.90f;
                case BattlePlanId.HookerFlankDeparture:     return 0.85f;
                case BattlePlanId.ShermanManeuverFix:       return 0.80f;
                case BattlePlanId.GenericAggressive:        return 0.50f;
                case BattlePlanId.GrantContinuousAttrition: return 0.40f;
                case BattlePlanId.MeetingEngagement:        return 0.40f;
                case BattlePlanId.ForrestCavalryRaid:       return 0.30f;
                case BattlePlanId.GenericMethodical:        return 0.30f;
                case BattlePlanId.GenericDesperate:         return 0.30f;
                case BattlePlanId.BufordCavalryScreenDelay: return 0.20f;
                case BattlePlanId.BraggIndecisiveCommit:    return 0.20f;
                case BattlePlanId.HoodFrontalAssault:       return 0.10f;
                case BattlePlanId.BurnsideForcedAssault:    return 0.10f;
                case BattlePlanId.GenericCautious:          return 0.10f;
                case BattlePlanId.JohnstonFabianDelay:      return 0.10f;
                case BattlePlanId.McClellanPreparedDefense: return 0.00f;
                case BattlePlanId.LongstreetDefensiveOverslope: return 0.00f;
                default:                                    return 0.20f;
            }
        }

        private static float HintAffinity(BattlePlanId id, float opposingCommanderHint)
        {
            if (opposingCommanderHint <= 0f) return 0f;
            float target = ResponseTargetFor(id);
            float hint = Clamp01(opposingCommanderHint);
            return 1f - Math.Min(1f, Math.Abs(target - hint) * 2f);
        }

        private static float ResponseTargetFor(BattlePlanId id)
        {
            switch (id)
            {
                case BattlePlanId.McClellanPreparedDefense:
                case BattlePlanId.LongstreetDefensiveOverslope:
                case BattlePlanId.GenericCautious:
                case BattlePlanId.JohnstonFabianDelay:
                    return 0.2f;
                case BattlePlanId.BraggIndecisiveCommit:
                case BattlePlanId.GenericMethodical:
                case BattlePlanId.BufordCavalryScreenDelay:
                case BattlePlanId.MeetingEngagement:
                    return 0.4f;
                case BattlePlanId.LeeEnvelopment:
                case BattlePlanId.JacksonValleyShuffle:
                case BattlePlanId.ShermanManeuverFix:
                case BattlePlanId.GrantContinuousAttrition:
                case BattlePlanId.HookerFlankDeparture:
                case BattlePlanId.HoodFrontalAssault:
                case BattlePlanId.BurnsideForcedAssault:
                case BattlePlanId.GenericAggressive:
                case BattlePlanId.GenericDesperate:
                case BattlePlanId.ForrestCavalryRaid:
                    return 0.6f;
                default:
                    return 0.4f;
            }
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        // xorshift32 — deterministic, fast, no allocations.
        private static uint NextRand(uint x)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x;
        }
    }
}
