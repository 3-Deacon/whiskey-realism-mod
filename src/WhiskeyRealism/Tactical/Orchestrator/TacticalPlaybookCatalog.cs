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

                if (score > bestScore)
                {
                    bestScore = score;
                    best = pb;
                }
            }
            return best;
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
                    return 0.2f;
                case BattlePlanId.BraggIndecisiveCommit:
                case BattlePlanId.GenericMethodical:
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
