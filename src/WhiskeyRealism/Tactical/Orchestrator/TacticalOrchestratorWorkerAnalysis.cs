using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure worker-thread analysis: takes an immutable
    /// <see cref="TacticalOrchestratorWorkerInput"/>, produces a fresh
    /// <see cref="TacticalDecisionSnapshot"/>. The analysis path runs entirely
    /// off the Unity main thread.
    ///
    /// MUST NOT call any Unity API or vanilla static
    /// (<c>BattleUnits.completeunitlist</c>, <c>Regiment.*</c> instance fields,
    /// <c>Time.*</c>, <c>Plugin.Instance.*</c> config getters, etc.). All
    /// inputs must come from the <see cref="TacticalOrchestratorWorkerInput"/>
    /// container — Task 8 baked the frozen config snapshot into the input so
    /// the worker never reads <c>Plugin.Instance</c>.
    ///
    /// On exception, returns <see cref="TacticalDecisionSnapshot.Empty"/> and
    /// logs via <c>Plugin.Log.LogWarning</c> (BepInEx <c>ManualLogSource</c> is
    /// the only thread-safe logger we have; <see cref="WhiskeyRealism.Util.OnceLog"/>
    /// dedup state is not proven thread-safe). The empty-on-failure contract
    /// keeps Task 14's main-thread patches on their legacy fallback path
    /// (<c>TryGetGroupStance</c> returns false → vanilla branch).
    /// </summary>
    internal static class TacticalOrchestratorWorkerAnalysis
    {
        public static TacticalDecisionSnapshot Analyze(
            TacticalOrchestratorWorkerInput input,
            long cycleId)
        {
            try
            {
                var side0 = ProcessSide(0, input);
                var side1 = ProcessSide(1, input);

                var groupStance0 = new Dictionary<int, TacticalGroupStanceDecision>(0);
                var groupStance1 = new Dictionary<int, TacticalGroupStanceDecision>(0);
                PopulateGroupDecisions(0, input, groupStance0);
                PopulateGroupDecisions(1, input, groupStance1);

                return new TacticalDecisionSnapshot(
                    battleSequence: input.BattleSequence,
                    cycleId: cycleId,
                    capturedAtBattleHours: input.BattleHours,
                    capturedAtRealtimeSeconds: input.RealtimeSeconds,
                    side0: side0,
                    side1: side1,
                    groupStance0: groupStance0,
                    groupStance1: groupStance1);
            }
            catch (Exception e)
            {
                // Worker-thread-safe warning: do NOT use OnceLog (dedup state not thread-safe).
                Plugin.Log.LogWarning("[TacticalOrchestratorWorkerAnalysis] cycle "
                    + cycleId + " degraded: " + e.GetType().Name + " " + e.Message);
                return TacticalDecisionSnapshot.Empty;
            }
        }

        private static TacticalSideDecision ProcessSide(int sideIdx, TacticalOrchestratorWorkerInput input)
        {
            // Stub: Task 11 replaces this with calls into the real analysis
            // pipeline (ArmyIntentInference, ArmyTickCycle.EvaluateReplanTrigger,
            // playbook scoring, etc.). For now: return TacticalSideDecision.Empty
            // so the worker compiles and runs without producing any usable
            // decisions. Task 12 wires lifecycle; Task 14 wires patches to read
            // these decisions — both are no-ops while ProcessSide returns Empty.
            return TacticalSideDecision.Empty;
        }

        private static void PopulateGroupDecisions(
            int sideIdx,
            TacticalOrchestratorWorkerInput input,
            Dictionary<int, TacticalGroupStanceDecision> output)
        {
            // Stub: Task 11 fills group stance decisions from the captured
            // snapshot's per-unit observations (TacticalDoctrineScorer.DecideGroupStance
            // fed by Side{N}Snapshot + Side{N}Bundle). For now: output stays
            // empty, which means TryGetGroupStance always returns false and
            // Task 14's patch falls back to its legacy per-group computation.
        }
    }
}
