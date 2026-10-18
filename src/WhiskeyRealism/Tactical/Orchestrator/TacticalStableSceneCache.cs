using System;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // Stage A perf refactor (2026-05-21), corrected 2026-10-14 (BR-H5). Battle-lifetime
    // cache for scene-scan reads. Only the IMMUTABLE parts are cached; everything vanilla
    // mutates mid-battle is re-read live at capture time.
    //
    // Map objectives:
    //   cached  — the Objectives references (vanilla's own BattleUnits.objectives, 78910,
    //             built in InitBattleUnits incl. inactive ones; FindObjectsOfType fallback),
    //             their ids (objectivename) and positions (objectives do not move).
    //   live    — Objectives.owner (written on capture, 105746), Objectives.points (zeroed when a Touch or timed Hold objective
    //             resolves, 105726-105735), isActiveAndEnabled and gameObject.activeInHierarchy (BC14O).
    // Approach avenues:
    //   cached  — the avenue array, keyed by a TacticalAvenueSourceSignature
    //             (scheduledarrival count: RemoveAt 87620 / Add 87834; EntryPoint.owner
    //             hash: 87601, 87691, 87891-87927, 94743; battlepasseddays: deployment groups can
    //             be re-placed when a new day's deployment panel closes,
    //             DoPlacementAIUnitsWithinDeploymentzoneNew 85524 via 164879-164880).
    //             Any change rebuilds the array.
    //   BattleUnits reference is kept from Populate instead of FindObjectOfType per scan.
    //
    // Lifecycle: Populate() from TacticalBattleCoordinator.OnBattleStart, Clear() at
    // battle end (BattleScope). When not populated every read falls back to a live scan.
    //
    // Thread safety: main thread only (live reads touch Unity objects).
    internal static class TacticalStableSceneCache
    {
        static TacticalStableSceneCache()
        {
            BattleScope.Register("stable-scene-cache", Clear);
        }

        private static Objectives[] _objectiveRefs;
        private static string[] _objectiveIds;
        private static TacticalMapPoint[] _objectivePoints;
        private static BattleUnits _battleUnits;
        private static TacticalApproachAvenueObservation[] _approachAvenues;
        private static TacticalAvenueSourceSignature _avenueSignature;
        private static bool _populated;

        public static bool IsPopulated => _populated;

        public static MapObjectiveCapture[] GetOrCaptureMapObjectives()
        {
            var refs = _objectiveRefs;
            if (!_populated || refs == null) return TacticalVisionRuntimeAdapter.CaptureMapObjectivesLive();

            var result = new MapObjectiveCapture[refs.Length];
            for (int i = 0; i < refs.Length; i++)
            {
                result[i] = TacticalVisionRuntimeAdapter.ReadObjectiveLive(refs[i], _objectiveIds[i], _objectivePoints[i]);
            }
            return result;
        }

        /// <summary>
        /// The id (the director's own string) of a vanilla objective reference, or null when it is not one of the battle's objectives: a designer chain's
        /// <c>objectives[0]</c> is mapped to the capture's id by reference against the cached list.
        /// </summary>
        public static string IdOf(Objectives objective)
        {
            Objectives[] refs = _objectiveRefs;
            if (!_populated || refs == null || ReferenceEquals(objective, null)) return null;
            for (int i = 0; i < refs.Length; i++)
                if (ReferenceEquals(refs[i], objective)) return _objectiveIds[i];
            return null;
        }

        public static TacticalApproachAvenueObservation[] GetOrCaptureApproachAvenues(int allianceId)
        {
            if (!_populated || _battleUnits == null)
                return TacticalVisionRuntimeAdapter.BuildApproachAvenueObservationsLive(allianceId);

            var signature = TacticalVisionRuntimeAdapter.ReadAvenueSourceSignature(_battleUnits);
            var cached = _approachAvenues;
            if (cached != null && signature.Equals(_avenueSignature)) return cached;

            var rebuilt = TacticalVisionRuntimeAdapter.BuildApproachAvenueObservations(_battleUnits)
                ?? Array.Empty<TacticalApproachAvenueObservation>();
            _approachAvenues = rebuilt;
            _avenueSignature = signature;
            return rebuilt;
        }

        public static void Populate(BattleUnits battleUnits)
        {
            try
            {
                using (TelemetryPerf.Scope(
                    "tactical.scene-cache.populate",
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Performance,
                    5.0))
                {
                    Objectives[] refs = TacticalVisionRuntimeAdapter.SceneObjectives();
                    var ids = new string[refs.Length];
                    var points = new TacticalMapPoint[refs.Length];
                    for (int i = 0; i < refs.Length; i++)
                    {
                        ids[i] = TacticalVisionRuntimeAdapter.MapObjectiveId(refs[i], i);
                        points[i] = TacticalVisionRuntimeAdapter.SafeObjectivePoint(refs[i]);
                    }

                    _objectiveRefs = refs;
                    _objectiveIds = ids;
                    _objectivePoints = points;
                    _battleUnits = battleUnits;
                    _approachAvenues = null;
                    _avenueSignature = default(TacticalAvenueSourceSignature);
                    _populated = true;

                    OnceLog.Info(
                        "tactical-scene-cache:populated",
                        "[TacticalSceneCache] populated mapObjectives=" + refs.Length
                        + " battleUnits=" + (battleUnits != null)
                        + " — objective owner/active and avenue sources read live");
                }
            }
            catch (Exception e)
            {
                OnceLog.Warning(
                    "tactical-scene-cache:populate-failed",
                    "[TacticalSceneCache] populate failed: " + e.GetType().Name + " " + e.Message
                    + " — live-scan fallback engaged for the rest of this battle");
                Clear();
            }
        }

        public static void Clear()
        {
            bool wasPopulated = _populated;
            _objectiveRefs = null;
            _objectiveIds = null;
            _objectivePoints = null;
            _battleUnits = null;
            _approachAvenues = null;
            _avenueSignature = default(TacticalAvenueSourceSignature);
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
