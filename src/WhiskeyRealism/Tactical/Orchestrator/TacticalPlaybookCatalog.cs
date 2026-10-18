using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using WhiskeyRealism.Util;

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

        public TacticalPlaybook Select(PlaybookContext ctx) => Select(ctx, null);

        /// <summary>
        /// The playbooks that reach the scoring step for this context: they pass their precondition, the stance filter and, for a crest play,
        /// the crest check. BC14's feasibility evaluation times exactly these candidates. Writes at most <paramref name="into"/>.Length ids
        /// and returns how many.
        /// </summary>
        public int EligiblePlans(PlaybookContext ctx, BattlePlanId[] into)
        {
            int n = 0;
            for (int i = 0; i < _playbooks.Count && n < into.Length; i++)
            {
                TacticalPlaybook pb = _playbooks[i];
                if (PassesGates(pb, in ctx) && (!pb.RequiresCrest || ctx.HasCrestLine)) into[n++] = pb.Id;
            }
            return n;
        }

        private static bool PassesGates(TacticalPlaybook pb, in PlaybookContext ctx)
            => pb.MatchesPreconditions(ctx) && StanceAllowsPlaybook(ctx.Stance, pb.Id);

        // requirement-never-empty: when every candidate that reaches scoring is Impossible, the feasibility filter is lifted for this selection.
        private bool AllCandidatesImpossible(in PlaybookContext ctx)
        {
            int candidates = 0, impossible = 0;
            for (int i = 0; i < _playbooks.Count; i++)
            {
                TacticalPlaybook pb = _playbooks[i];
                if (!PassesGates(pb, in ctx) || (pb.RequiresCrest && !ctx.HasCrestLine)) continue;
                candidates++;
                if (ctx.Feasibility.TryGet(pb.Id, out CandidateResult r) && r.Verdict == FeasibilityVerdict.Impossible) impossible++;
            }
            return candidates > 0 && impossible == candidates;
        }

        /// <summary>
        /// <see cref="Select(PlaybookContext)"/> that also reports its candidates (ranked, with the feasibility term) into a pooled
        /// <paramref name="report"/> for the TacticalPlanSelection row. With <c>ctx.Feasibility</c> set (BC14), an Impossible candidate is skipped
        /// after its jitter and crest draws and every other candidate's score loses <see cref="ManeuverFeasibility.FeasibilityWeight"/> times its
        /// penalty; the term only subtracts, so a sound plan scores exactly as before.
        /// </summary>
        public TacticalPlaybook Select(PlaybookContext ctx, SelectionReport report)
        {
            report?.Clear();
            if (_playbooks.Count == 0) return null;

            TacticalPlaybook best = null;
            float bestScore = float.NegativeInfinity;
            // BC14 hysteresis: the plan being kept, scored in this pass.
            TacticalPlaybook current = null;
            float currentScore = float.NegativeInfinity;
            // Deterministic per-seed jitter (xorshift32). Seed must be non-zero for the
            // generator to produce useful state, so OR with 1.
            // Precondition filter (2026-05-21). Excludes plays the force can't
            // execute (cavalry plays w/o cavalry). Hard filter — happens
            // before scoring so an ineligible play can't win on personality
            // fit.
            int preconditionFiltered = 0, stanceFiltered = 0, crestFiltered = 0, feasibilityFiltered = 0;
            bool lift = ctx.Feasibility != null && AllCandidatesImpossible(in ctx);
            if (lift) OnceLog.Warning("tactical-battle-core:feasibility-all-impossible",
                "[Feasibility] every candidate is impossible: the feasibility filter is lifted for this selection");
            // BC14O: the pressure of the victory clock (zero with the doctrine off): a side behind late takes more risk, one ahead leans to holding plays.
            ObjectivePressure pressure = ctx.Feasibility != null && Plugin.ObjectiveDoctrineEnabled() ? ctx.Feasibility.Pressure : default(ObjectivePressure);
            uint state = unchecked((uint)ctx.JitterSeed | 1u);
            for (int i = 0; i < _playbooks.Count; i++)
            {
                var pb = _playbooks[i];
                if (!pb.MatchesPreconditions(ctx))
                {
                    preconditionFiltered++;
                    continue;
                }
                // Stance hard filter (2026-05-23 / L): defenders can ONLY pick
                // defensive archetypes; attackers can ONLY pick offensive (or
                // neutral) archetypes. Mirrors Scourge of War's attack-vs-defend
                // play selection (offai.cpp:1153-1159) — the choice is dictated
                // by vanilla's GetBattleType, not by per-tick force balance.
                if (!StanceAllowsPlaybook(ctx.Stance, pb.Id))
                {
                    stanceFiltered++;
                    continue;
                }
                float personalityScore = pb.Fit.Score(ctx.CommanderPersonality);
                float terrainScore = pb.TerrainFit.Score(ctx.Terrain);
                float oddsScore = pb.PreferredOdds.Score(ctx.CurrentOdds);
                float hintScore = HintAffinity(pb.Id, ctx.OpposingCommanderHint);
                state = NextRand(state);
                float jitter = (state & 0xFFFFu) / 65535f;
                // BC12: a play built on a crest (ReverseSlopeDefense) needs the picture to have found one across the front. The
                // check follows the jitter draw so the other plays' jitter stream does not depend on whether a crest exists.
                if (pb.RequiresCrest && !ctx.HasCrestLine)
                {
                    crestFiltered++;
                    continue;
                }
                // BC14: the feasibility verdict follows the same two draws, for the same reason. An Impossible play is skipped.
                var verdict = FeasibilityVerdict.Unknown;
                float penalty = 0f;
                bool mirrored = false;
                string reason = null;
                if (ctx.Feasibility != null && ctx.Feasibility.TryGet(pb.Id, out CandidateResult feasibility))
                {
                    verdict = feasibility.Verdict;
                    penalty = feasibility.Penalty;
                    mirrored = feasibility.Mirrored;
                    reason = feasibility.Reason;
                    if (verdict == FeasibilityVerdict.Impossible && !lift)
                    {
                        feasibilityFiltered++;
                        report?.Add(pb.Id, 0f, 0f, verdict, mirrored, reason, skipped: true);
                        continue;
                    }
                }

                float score = 0.5f * personalityScore
                            + 0.2f * terrainScore
                            + 0.15f * oddsScore
                            + 0.1f * hintScore
                            + 0.05f * jitter;

                // Envelopment bias: when the doctrine signals AttackNow + clear
                // standing advantage, add an additive bonus weighted by the
                // playbook's envelopment-doctrine affinity. Bonus magnitude
                // (max 0.2) is tuned to swing the choice toward envelopment
                // archetypes when personality fit is close, but not override
                // a strongly mismatched commander (personality fit carries
                // 0.5 weight).
                if (ctx.EnvelopmentPressure)
                {
                    score += 0.2f * EnvelopmentAffinity(pb.Id);
                }

                // Delaying bias (2026-05-23 / I): when the doctrine signals
                // DelayingDefense (outnumbered + no near parity + still
                // defensible), bias toward delay-and-skirmish archetypes.
                // Bonus magnitude (max 0.2) parallels EnvelopmentPressure.
                if (ctx.DelayingDefensePressure)
                {
                    score += 0.2f * DelayingAffinity(pb.Id);
                }

                // Defensive bias (2026-05-23 / K): broader signal that fires
                // for any non-offensive doctrine outcome (DelayingDefense,
                // DefensiveHold, WaitAndConsolidate). Stronger weight (0.35)
                // than DelayingDefensePressure (0.2) — when doctrine doesn't
                // want an attack, the playbook MUST agree. Without this, the
                // smoke showed doctrine=DefensiveHold + playbook=ManeuverAndFix
                // mismatches, which caused lone-brigade attacks while macroai=2
                // (defend) was set.
                if (ctx.DefensivePressure)
                {
                    score += 0.35f * DelayingAffinity(pb.Id);
                }

                score += ObjectiveScoring.HoldBias(in pressure, StanceOf(pb.Id));

                float baseScore = score;
                float term = -ObjectiveScoring.SelectRiskWeight(in pressure) * penalty;
                score += term;
                report?.Add(pb.Id, baseScore, term, verdict, mirrored, reason, skipped: false);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = pb;
                }
                if (pb.Id == ctx.HysteresisPlan && ctx.HysteresisPlan != BattlePlanId.Unknown)
                {
                    current = pb;
                    currentScore = score;
                }
            }
            // The current plan stays unless it is absent or Impossible (then it was never scored) or the best beats it by the margin.
            if (current != null && best != current && bestScore - currentScore < ctx.HysteresisMargin) best = current;
            if (report != null)
            {
                report.PreconditionFiltered = preconditionFiltered;
                report.StanceFiltered = stanceFiltered;
                report.CrestFiltered = crestFiltered;
                report.FeasibilityFiltered = feasibilityFiltered;
                report.AllImpossible = lift;
                report.Rank();
            }
            return best;
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
        /// <summary>
        /// Playbook stance classification. Used by <see cref="Select"/> to
        /// hard-filter incompatible playbooks for a given battle stance
        /// (defenders can't pick offensive; attackers can't pick pure-delay).
        /// </summary>
        public enum PlaybookStance
        {
            /// <summary>Acceptable on either side (Meeting / Generic).</summary>
            Neutral = 0,
            /// <summary>Only attackers should pick this.</summary>
            Offensive = 1,
            /// <summary>Only defenders should pick this.</summary>
            Defensive = 2,
        }

        /// <summary>
        /// Classifies each playbook by stance. Offensive archetypes only fire
        /// when we're attacking; defensive only fire when we're defending.
        /// Neutral archetypes (MeetingEngagement + Generic*) fire on any stance.
        /// </summary>
        public static PlaybookStance StanceOf(BattlePlanId id)
        {
            switch (id)
            {
                // Offensive archetypes — only valid for an attacker.
                case BattlePlanId.DoubleEnvelopment:
                case BattlePlanId.MobileFlankMarch:
                case BattlePlanId.WideFlankApproach:
                case BattlePlanId.ManeuverAndFix:
                case BattlePlanId.ContinuousAttrition:
                case BattlePlanId.FrontalShockAssault:
                case BattlePlanId.ForcedFrontalAttack:
                case BattlePlanId.HesitatedCommit:
                case BattlePlanId.CavalryRaid:
                case BattlePlanId.GenericAggressive:
                case BattlePlanId.GenericDesperate:
                case BattlePlanId.FocalAssault:
                    return PlaybookStance.Offensive;
                // Defensive archetypes — only valid for a defender.
                case BattlePlanId.PreparedDefense:
                case BattlePlanId.ReverseSlopeDefense:
                case BattlePlanId.DelayingDefense:
                case BattlePlanId.CavalryScreeningDelay:
                case BattlePlanId.GenericCautious:
                    return PlaybookStance.Defensive;
                // Neutral — fits any stance.
                case BattlePlanId.MeetingEngagement:
                case BattlePlanId.GenericMethodical:
                default:
                    return PlaybookStance.Neutral;
            }
        }

        /// <summary>
        /// True when a playbook is compatible with the given side stance.
        /// Neutral playbooks always pass. Offensive playbooks fail on Defender.
        /// Defensive playbooks fail on Attacker.
        /// </summary>
        public static bool StanceAllowsPlaybook(BattleStance stance, BattlePlanId id)
        {
            var pbStance = StanceOf(id);
            if (pbStance == PlaybookStance.Neutral) return true;
            switch (stance)
            {
                case BattleStance.Attacker:
                    return pbStance != PlaybookStance.Defensive;
                case BattleStance.Defender:
                    return pbStance != PlaybookStance.Offensive;
                case BattleStance.Meeting:
                default:
                    return true;
            }
        }

        /// <summary>
        /// Per-playbook affinity for Fabian-delay archetypes (outnumbered +
        /// defensible). 1.0 = literal delay doctrine; 0.0 = offensive-only or
        /// defense-without-delay.
        /// </summary>
        public static float DelayingAffinity(BattlePlanId id)
        {
            switch (id)
            {
                case BattlePlanId.DelayingDefense:           return 1.00f;
                case BattlePlanId.CavalryScreeningDelay:     return 0.85f;
                case BattlePlanId.ReverseSlopeDefense:       return 0.70f;
                case BattlePlanId.PreparedDefense:           return 0.50f;
                case BattlePlanId.GenericCautious:           return 0.45f;
                case BattlePlanId.GenericMethodical:         return 0.30f;
                case BattlePlanId.HesitatedCommit:           return 0.20f;
                case BattlePlanId.MeetingEngagement:         return 0.15f;
                case BattlePlanId.ContinuousAttrition:       return 0.10f;
                case BattlePlanId.ManeuverAndFix:            return 0.10f;
                // Offensive archetypes — should NOT win when delay is signaled.
                case BattlePlanId.DoubleEnvelopment:
                case BattlePlanId.MobileFlankMarch:
                case BattlePlanId.WideFlankApproach:
                case BattlePlanId.FrontalShockAssault:
                case BattlePlanId.ForcedFrontalAttack:
                case BattlePlanId.CavalryRaid:
                case BattlePlanId.GenericAggressive:
                case BattlePlanId.GenericDesperate:
                case BattlePlanId.FocalAssault:              return 0.00f;
                default:                                     return 0.20f;
            }
        }

        public static float EnvelopmentAffinity(BattlePlanId id)
        {
            switch (id)
            {
                case BattlePlanId.DoubleEnvelopment:           return 1.00f;
                case BattlePlanId.MobileFlankMarch:     return 0.90f;
                case BattlePlanId.WideFlankApproach:     return 0.85f;
                case BattlePlanId.ManeuverAndFix:       return 0.80f;
                case BattlePlanId.GenericAggressive:        return 0.50f;
                case BattlePlanId.ContinuousAttrition: return 0.40f;
                case BattlePlanId.MeetingEngagement:        return 0.40f;
                case BattlePlanId.CavalryRaid:       return 0.30f;
                case BattlePlanId.GenericMethodical:        return 0.30f;
                case BattlePlanId.GenericDesperate:         return 0.30f;
                case BattlePlanId.CavalryScreeningDelay: return 0.20f;
                case BattlePlanId.HesitatedCommit:    return 0.20f;
                case BattlePlanId.FrontalShockAssault:       return 0.10f;
                case BattlePlanId.ForcedFrontalAttack:    return 0.10f;
                case BattlePlanId.FocalAssault:           return 0.10f;   // a frontal assault on the works, not a turn
                case BattlePlanId.GenericCautious:          return 0.10f;
                case BattlePlanId.DelayingDefense:      return 0.10f;
                case BattlePlanId.PreparedDefense: return 0.00f;
                case BattlePlanId.ReverseSlopeDefense: return 0.00f;
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

        internal static float ResponseTargetFor(BattlePlanId id)
        {
            switch (id)
            {
                case BattlePlanId.PreparedDefense:
                case BattlePlanId.ReverseSlopeDefense:
                case BattlePlanId.GenericCautious:
                case BattlePlanId.DelayingDefense:
                    return 0.2f;
                case BattlePlanId.HesitatedCommit:
                case BattlePlanId.GenericMethodical:
                case BattlePlanId.CavalryScreeningDelay:
                case BattlePlanId.MeetingEngagement:
                    return 0.4f;
                case BattlePlanId.DoubleEnvelopment:
                case BattlePlanId.MobileFlankMarch:
                case BattlePlanId.ManeuverAndFix:
                case BattlePlanId.ContinuousAttrition:
                case BattlePlanId.WideFlankApproach:
                case BattlePlanId.FrontalShockAssault:
                case BattlePlanId.ForcedFrontalAttack:
                case BattlePlanId.GenericAggressive:
                case BattlePlanId.GenericDesperate:
                case BattlePlanId.CavalryRaid:
                case BattlePlanId.FocalAssault:
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
