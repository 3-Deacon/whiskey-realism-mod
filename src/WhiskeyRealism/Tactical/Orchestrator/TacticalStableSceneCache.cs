using System;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // Stage A perf refactor (2026-05-21). Battle-lifetime cache for scene-scan reads.
    //
    // Problem this solves: BuildObjectiveRecordsFromContext calls FindObjectsOfType<Objectives>()
    // and BuildApproachAvenueObservations (which calls FindObjectOfType<BattleUnits>() plus
    // EntryPoint / scheduledarrival / grp[] reflection) on every heavy-gate fire — ~3-4ms of
    // pure Unity scene scanning per fire. These scene objects are STABLE for the duration of
    // a battle (Objectives, EntryPoints, the BattleUnits singleton, the initial deployment grp[]
    // — none of them change between battle-start and battle-end), so the scan only needs to
    // happen ONCE.
    //
    // Lifecycle:
    //   - Populate() called from TacticalBattleCoordinator.OnBattleStart after vanilla has
    //     finished initializing the battle scene.
    //   - Clear() called from TacticalBattleCoordinator.OnBattleEnd.
    //   - GetOrCapture*() methods used by callers. When the cache is populated they return
    //     the cached array directly. When the cache is empty (Populate failed, pre-battle
    //     path, or a code path that runs before OnBattleStart) they fall back to a live
    //     scan so behavior never regresses.
    //
    // Diagnostic emissions:
    //   - tactical-scene-cache:populated — first successful populate with counts.
    //   - tactical-scene-cache:populate-failed — populate threw; cache stays empty; live
    //     scan fallback engages on every read until next battle.
    //   - tactical-scene-cache:cleared — explicit clear.
    //
    // Thread safety: main thread only for writes. Reads are non-atomic but the volatile array
    // references guarantee tear-free reads under x86/x64 memory ordering. We accept the
    // small race where a Clear() during a concurrent read returns a stale array — callers
    // tolerate empty/old arrays (worker-thread analysis is one-cycle-lag tolerant by design).
    internal static class TacticalStableSceneCache
    {
        // Volatile field references — readers see either the prior populated array or null
        // without partial-update tearing. Arrays themselves are immutable after assignment.
        private static volatile MapObjectiveCapture[] _mapObjectives;
        private static volatile TacticalApproachAvenueObservation[] _approachAvenues;
        private static volatile bool _populated;

        public static bool IsPopulated => _populated;

        // Read accessor with live-scan fallback. Returns cached data when populated, else
        // does a fresh scan so callers always get something usable. The fallback is the
        // pre-cache cost (~2ms / call) — strictly no worse than the old code path.
        public static MapObjectiveCapture[] GetOrCaptureMapObjectives()
        {
            var cached = _mapObjectives;
            if (_populated && cached != null) return cached;
            return TacticalVisionRuntimeAdapter.CaptureMapObjectivesLive();
        }

        public static TacticalApproachAvenueObservation[] GetOrCaptureApproachAvenues(int allianceId)
        {
            var cached = _approachAvenues;
            if (_populated && cached != null) return cached;
            return TacticalVisionRuntimeAdapter.BuildApproachAvenueObservationsLive(allianceId);
        }

        // Populates the cache by running one live scan of each scene-scan source. Called
        // from TacticalBattleCoordinator.OnBattleStart on the main thread, after vanilla
        // has built the battle scene. Safe to call multiple times — re-populates idempotently.
        // The approach-avenue scan accepts an allianceId argument but the result is the same
        // for both alliances (vanilla EntryPoint.owner and grp[].alliance fields identify
        // which side each avenue belongs to, but the FULL avenue array contains both sides'
        // entries). We pass allianceId=0 by convention; both sides consume the same array.
        public static void Populate()
        {
            try
            {
                using (TelemetryPerf.Scope(
                    "tactical.scene-cache.populate",
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Performance,
                    5.0))
                {
                    var mapObjectives = TacticalVisionRuntimeAdapter.CaptureMapObjectivesLive();
                    var approachAvenues = TacticalVisionRuntimeAdapter.BuildApproachAvenueObservationsLive(0);

                    _mapObjectives = mapObjectives ?? Array.Empty<MapObjectiveCapture>();
                    _approachAvenues = approachAvenues ?? Array.Empty<TacticalApproachAvenueObservation>();
                    _populated = true;

                    OnceLog.Info(
                        "tactical-scene-cache:populated",
                        "[TacticalSceneCache] populated mapObjectives=" + _mapObjectives.Length
                        + " approachAvenues=" + _approachAvenues.Length
                        + " — per-heavy-gate scene scans now bypassed");
                }
            }
            catch (Exception e)
            {
                OnceLog.Warning(
                    "tactical-scene-cache:populate-failed",
                    "[TacticalSceneCache] populate failed: " + e.GetType().Name + " " + e.Message
                    + " — live-scan fallback engaged for the rest of this battle");
                _mapObjectives = Array.Empty<MapObjectiveCapture>();
                _approachAvenues = Array.Empty<TacticalApproachAvenueObservation>();
                _populated = false;
            }
        }

        // Clears all caches. Called from TacticalBattleCoordinator.OnBattleEnd so the next
        // battle starts with a clean state. Safe to call when not populated (no-op).
        public static void Clear()
        {
            bool wasPopulated = _populated;
            _mapObjectives = null;
            _approachAvenues = null;
            _populated = false;
            if (wasPopulated)
            {
                OnceLog.Info(
                    "tactical-scene-cache:cleared",
                    "[TacticalSceneCache] cleared at battle end");
            }
        }
    }
}
