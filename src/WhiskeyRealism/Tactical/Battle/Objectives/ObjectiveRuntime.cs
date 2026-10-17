using System;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // BC14O runtime half of the objective doctrine (spec § 7B). Excluded from the test csproj: it reads the battle's objectives, the victory bar and
    // the side's accrued points, and fills the pure evaluation's input once per evaluation. It reads vanilla's public objective state and exactly one
    // aggregate of the enemy's true state, the victory bar as the player sees it (BattleUnits.GetVictoryPointsRelation: user decision D39); it reads
    // no other (FEAS-PIN-O). Never throws: a failed read leaves the input without objectives, and the shipped director stands.
    internal static class ObjectiveRuntime
    {
        private static readonly string[][] DueChains = { new string[ManeuverFeasibility.MaxObjectives], new string[ManeuverFeasibility.MaxObjectives] };

        private static readonly ObjectiveFact[][] Facts =
        {
            new ObjectiveFact[ManeuverFeasibility.MaxObjectives],
            new ObjectiveFact[ManeuverFeasibility.MaxObjectives],
        };

        static ObjectiveRuntime()
        {
            BattleScope.Register("objective-runtime", Reset);
        }

        // One delegate for the process: a method group converted at the call would allocate one per evaluation.
        private static readonly Func<Objectives, string> IdOfObjective = TacticalStableSceneCache.IdOf;

        private static void Reset()
        {
            for (int i = 0; i < Facts.Length; i++)
            {
                Array.Clear(Facts[i], 0, Facts[i].Length);
                Array.Clear(DueChains[i], 0, DueChains[i].Length);
            }
        }

        /// <summary>
        /// Fills <paramref name="input"/>'s objectives and clock for an evaluation of <paramref name="alliance"/>'s side. The objectives are the live ones
        /// (gameObject.activeInHierarchy, as vanilla tests it); past <see cref="ManeuverFeasibility.MaxObjectives"/> the most valuable are kept. The arrays
        /// are pooled per side and read, never written, by an evaluation under way, so this runs only when a new evaluation starts.
        /// </summary>
        internal static void Fill(BattleUnits bunits, int alliance, BattleSnapshot snapshot, float daylightHours, ref FeasibilityInput input)
        {
            input.Objectives = null;
            input.ObjectiveCount = 0;
            input.HasClock = false;
            input.DueChainObjectiveIds = null;
            input.DueChainCount = 0;
            if (!Plugin.ObjectiveDoctrineEnabled() || bunits == null || alliance < 0 || alliance >= Facts.Length) return;
            try
            {
                ObjectiveFact[] facts = Facts[alliance];
                int n = ObjectiveScoring.FactsOf(TacticalStableSceneCache.GetOrCaptureMapObjectives(), alliance, snapshot, facts);
                if (n == 0) return;

                int side = bunits.GetSideFromAlliance(alliance);
                bool continuous = GamePrefs.continuousobjectivepoints;
                float accruedOwn = 0f, accruedEnemy = 0f;
                if (continuous)
                {
                    accruedOwn = bunits.sideinformation[side].objectivepoints;
                    accruedEnemy = bunits.sideinformation[1 - side].objectivepoints;
                }
                // The displayed bar, once per evaluation: vanilla logs a line from it in its degenerate cases, so it is not asked more often.
                float bar = bunits.GetVictoryPointsRelation(side);
                input.Clock = ObjectiveScoring.Clock(facts, n, continuous, accruedOwn, accruedEnemy, bar, daylightHours);
                input.HasClock = true;
                input.Objectives = facts;
                input.ObjectiveCount = n;
                // The side's own designer chains, from its own AIBattle (never by array position or the feud instance): the due ones' next objectives.
                if (ArmyEvidenceBuilder.TryResolveOwnAiBattle(bunits, alliance, out AIBattle own))
                {
                    input.DueChainCount = ObjectiveChains.CollectDue(own.objectivechain, GameVars.currenttimefromstart, IdOfObjective, DueChains[alliance]);
                    input.DueChainObjectiveIds = DueChains[alliance];
                }
            }
            catch (Exception e)
            {
                input.Objectives = null;
                input.ObjectiveCount = 0;
                input.HasClock = false;
                input.DueChainObjectiveIds = null;
                input.DueChainCount = 0;
                OnceLog.Warning("tactical-battle-core:objective-runtime", "[Objectives] the objectives or the victory bar could not be read, the shipped choice stands: "
                    + e.GetType().Name + " " + e.Message);
            }
        }
    }
}
